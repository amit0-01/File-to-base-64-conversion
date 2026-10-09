using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace File_To_Base64_Signature
{
    internal class Program
    {
        private static readonly string ImageFolder = ConfigurationManager.AppSettings["ImageFolder"];

        private static readonly string ConnectionString = ConfigurationManager.ConnectionStrings["FullConnStr"] == null ? null: ConfigurationManager.ConnectionStrings["FullConnStr"].ConnectionString;
        private static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
        };

        private static async Task Main(string[] args)
        {
            if (string.IsNullOrWhiteSpace(ImageFolder) || !Directory.Exists(ImageFolder))
            {
                Console.WriteLine("ImageFolder is missing or does not exist in App.config.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                Console.WriteLine("DefaultConnection is missing from App.config.");
                return;
            }

            var files = Directory.GetFiles(ImageFolder).Where(file => ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)).ToArray();

            if (files.Length == 0)
            {
                Console.WriteLine("No image files found.");
                return;
            }
            int totalSignatures = files.Length;
            int insertedCount = 0;
            int notFoundCount = 0;
            int failedCount = 0;
            var notFoundSignatures = new List<string>();
            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();

                const string profileSql = @"SELECT TOP (1) UserProfileId FROM dbo.UserProfiles WHERE Signature LIKE @SignatureFileName + '%' AND IsActive = 1 AND IsDeleted = 0 ORDER BY LastModifiedDate DESC;";

                const string insertSql = @"INSERT INTO dbo.UserOfficerSignatures (UserProfileRefId, SignatureBase64, isActive, UpdatedOn) VALUES (@UserProfileRefId, @SignatureBase64, @IsActive, @UpdatedOn);";


                foreach (var filePath in files)
                {
                    try
                    {
                        byte[] imageBytes = File.ReadAllBytes(filePath);
                        string base64 = Convert.ToBase64String(imageBytes);

                        object profileIdValue;

                        string signatureFileName = Path.GetFileNameWithoutExtension(filePath);

                        using (var lookupCommand = new SqlCommand(profileSql, connection))
                        {
                            lookupCommand.Parameters.Add("@SignatureFileName", SqlDbType.NVarChar, 260).Value = signatureFileName;

                            profileIdValue = await lookupCommand.ExecuteScalarAsync();
                        }

                        if (profileIdValue == null || profileIdValue == DBNull.Value)
                        {
                            Console.WriteLine("No active user profile found for file name: " + signatureFileName);
                            notFoundCount++;
                            notFoundSignatures.Add(signatureFileName);
                            continue;
                        }

                        long profileId = Convert.ToInt64(profileIdValue);

                        using (var insertCommand = new SqlCommand(insertSql, connection))
                        {
                            insertCommand.Parameters.Add("@UserProfileRefId", SqlDbType.BigInt).Value = profileId;
                            insertCommand.Parameters.Add("@SignatureBase64", SqlDbType.NVarChar, -1).Value = base64;
                            insertCommand.Parameters.Add("@IsActive", SqlDbType.Bit).Value = true;
                            insertCommand.Parameters.Add("@UpdatedOn", SqlDbType.DateTime2).Value = DateTime.Now;

                            await insertCommand.ExecuteNonQueryAsync();
                            insertedCount++;
                        }

                        Console.WriteLine("Saved signature for File: " + signatureFileName);
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        Console.WriteLine("Failed to process " + Path.GetFileName(filePath) + ": " + ex.Message);
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine("Total image signatures found: " + totalSignatures);
            Console.WriteLine("Inserted into database: " + insertedCount);
            Console.WriteLine("Profile not found: " + notFoundCount);
            Console.WriteLine("Processing/database failures: " + failedCount);

            if (notFoundSignatures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Signatures with no matching user profile:");

                foreach (string signature in notFoundSignatures)
                {
                    Console.WriteLine(" - " + signature);
                }
            }
        }
    }
}