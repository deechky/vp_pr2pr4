using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Globalization;
using System.IO;
using System.Configuration;

namespace Client
{
    public class Program
    {
        static void Main(string[] args)
        {
            ChannelFactory<IBatteryService> batteryFactory = null;
            IBatteryService batteryProxy = null;
            
            try
            {
                batteryFactory = new ChannelFactory<IBatteryService>("Battery");
                batteryProxy = batteryFactory.CreateChannel();
                
                // Test connection
                Console.WriteLine("Testing connection to Battery EIS Analysis service...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to Battery service: {ex.Message}");
                Console.WriteLine("Make sure the Server is running on localhost:4100");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("=== Li-ion Battery EIS Data Analysis Client ===");
            Console.WriteLine("Choose operation:");
            Console.WriteLine("1. Process single EIS file");
            Console.WriteLine("2. Auto-discover and process EIS files from dataset");
            Console.Write("Enter choice (1-2): ");
            
            string choice = Console.ReadLine();
            
            if (choice == "2")
            {
                ProcessMultipleFiles(batteryProxy);
            }
            else
            {
                ProcessSingleFile(batteryProxy);
            }

            try
            {
                if (batteryProxy is ICommunicationObject commObj)
                    commObj.Close();
                batteryFactory?.Close();
            }
            catch { }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static void ProcessSingleFile(IBatteryService batteryProxy)
        {
            Console.WriteLine("Enter path to EIS CSV file (or Enter for auto-detection):");
            string path = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(path))
            {
                // Auto-detect Hioki EIS files from the Dataset/Hioki folder
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Console.WriteLine($"Looking for Hioki files from: {baseDir}");
                
                // Try multiple possible locations
                string[] possiblePaths = {
                    Path.Combine(baseDir, "Dataset", "Hioki"),                    // bin/Debug/Dataset/Hioki
                    Path.Combine(baseDir, "..", "..", "..", "Dataset", "Hioki"),  // Project/Dataset/Hioki
                    Path.Combine(baseDir, "..", "..", "Dataset", "Hioki"),       // Battery/Dataset/Hioki
                    Path.Combine(baseDir, "..", "Dataset", "Hioki")              // Battery/Client/../Dataset/Hioki
                };
                
                string hiokiPath = null;
                foreach (string candidatePath in possiblePaths)
                {
                    string fullPath = Path.GetFullPath(candidatePath);
                    Console.WriteLine($"Checking: {fullPath}");
                    if (Directory.Exists(fullPath))
                    {
                        var csvFiles = Directory.GetFiles(fullPath, "Hk_*.csv");
                        if (csvFiles.Length > 0)
                        {
                            hiokiPath = fullPath;
                            Console.WriteLine($"✅ Found Hioki folder with {csvFiles.Length} files: {hiokiPath}");
                            break;
                        }
                    }
                }
                
                if (hiokiPath != null)
                {
                    var hiokiFiles = Directory.GetFiles(hiokiPath, "Hk_*.csv");
                    // Use the first Hioki file found
                    path = hiokiFiles[0];
                    Console.WriteLine($"Auto-detected Hioki file: {Path.GetFileName(path)}");
                }
                else
                {
                    Console.WriteLine("❌ No Hioki CSV files found. Please specify path manually.");
                }
            }

            while (!File.Exists(path))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine("❌ No file path specified.");
                    Console.WriteLine("Enter full path to .csv file (or 'exit' to quit):");
                }
                else
                {
                    Console.WriteLine($"❌ File not found: {path}");
                    Console.WriteLine("Enter full path to .csv file (or 'exit' to quit):");
                }
                
                path = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(path) || path.ToLowerInvariant() == "exit")
                {
                    Console.WriteLine("Exiting...");
                    return;
                }
            }

            ProcessEisFile(batteryProxy, path, ExtractMetaFromPath(path));
        }

        private static void ProcessMultipleFiles(IBatteryService batteryProxy)
        {
            // Look specifically in the Hioki folder for your uploaded files
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Console.WriteLine($"Looking for multiple Hioki files from: {baseDir}");
            
            string[] possiblePaths = {
                Path.Combine(baseDir, "Dataset"),                    // bin/Debug/Dataset
                Path.Combine(baseDir, "..", "..", "..", "Dataset"),  // Project/Dataset
                Path.Combine(baseDir, "..", "..", "Dataset"),       // Battery/Dataset
                Path.Combine(baseDir, "..", "Dataset")              // Battery/Client/../Dataset
            };
            
            var files = new List<Common.EisFileInfo>();
            foreach (string candidatePath in possiblePaths)
            {
                string fullPath = Path.GetFullPath(candidatePath);
                Console.WriteLine($"Checking dataset path: {fullPath}");
                if (Directory.Exists(fullPath))
                {
                    files = EisFileDiscovery.DiscoverEisFiles(fullPath);
                    if (files.Count > 0)
                    {
                        Console.WriteLine($"✅ Found {files.Count} EIS files in: {fullPath}");
                        break;
                    }
                }
            }

            if (files.Count == 0)
            {
                Console.WriteLine("❌ No Hioki EIS files found. Make sure your Hioki CSV files are in Dataset/Hioki/ folder.");
                return;
            }

            Console.WriteLine($"Found {files.Count} EIS files:");
            foreach (var file in files.Take(10))
            {
                Console.WriteLine($"  {file}");
            }
            if (files.Count > 10)
                Console.WriteLine($"  ... and {files.Count - 10} more");

            Console.WriteLine("\nProcessing all files...");
            
            int processed = 0;
            foreach (var fileInfo in files)
            {
                Console.WriteLine($"\n--- Processing {fileInfo} ---");
                
                var meta = new EisMeta
                {
                    BatteryId = fileInfo.BatteryId,
                    TestId = fileInfo.TestId,
                    SocPercent = fileInfo.SocPercent,
                    FileName = fileInfo.FileName,
                    TotalRows = CountCsvRows(fileInfo.FilePath),
                    StartedAt = DateTime.UtcNow,
                    VThreshold = double.Parse(ConfigurationManager.AppSettings["V_threshold"] ?? "0.1", CultureInfo.InvariantCulture),
                    ZThreshold = double.Parse(ConfigurationManager.AppSettings["Z_threshold"] ?? "0.5", CultureInfo.InvariantCulture),
                    DeviationPercent = double.Parse(ConfigurationManager.AppSettings["DeviationPercent"] ?? "25", CultureInfo.InvariantCulture)
                };

                ProcessEisFile(batteryProxy, fileInfo.FilePath, meta);
                processed++;
                
                if (processed >= 3) // Limit for demo
                {
                    Console.WriteLine($"\nProcessed {processed} files (demo limit reached)");
                    break;
                }
            }
        }

        private static void ProcessEisFile(IBatteryService batteryProxy, string filePath, EisMeta meta)
        {
            try
            {
                var ack = batteryProxy.StartSession(meta);
                Console.WriteLine($"Battery session: {ack.Status}");
                if (!ack.Success)
                {
                    Console.WriteLine($"Error: {ack.Message}");
                    return;
                }

                int sent = 0;
                int successful = 0;
                int failed = 0;
                Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataset"));
                string rejects = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataset", $"rejects_client_{meta.BatteryId}_{meta.TestId}_{meta.SocPercent}.csv");
                
                Console.WriteLine($"Reading from: {filePath}");
                Console.WriteLine($"Expected samples: {meta.TotalRows}");
                Console.WriteLine($"Thresholds: V={meta.VThreshold}, Z={meta.ZThreshold}, Deviation={meta.DeviationPercent}%");
                Console.WriteLine("Watch for VOLTAGE and IMPEDANCE alerts in Server console! 🔴");
                
                using (var reader = new EisCsvReader(filePath, rejects))
                {
                    while (reader.TryReadNext(out var sample))
                    {
                        var resp = batteryProxy.PushSample(sample);
                        sent++;
                        if (resp.Success)
                            successful++;
                        else
                        {
                            failed++;
                            if (failed <= 3)
                            {
                                Console.WriteLine($"\n❌ Client received failure: {resp.Message}");
                            }
                        }
                        
                        if (sent % 10 == 0)
                            Console.Write($"\rSent: {sent}, Success: {successful}, Failed: {failed}");
                        
                        // Add small delay for real-time streaming effect
                        System.Threading.Thread.Sleep(10);
                    }
                    Console.WriteLine($"\nClient processed: Accepted={reader.AcceptedCount} Rejected={reader.RejectedCount}");
                }
                
                var end = batteryProxy.EndSession();
                Console.WriteLine($"\nBattery session finished: {end.Status}");
                Console.WriteLine($"Total sent: {sent}, Successful: {successful}, Failed: {failed}");
                
                if (sent == 0)
                {
                    Console.WriteLine("No samples sent. Check CSV format or file path.");
                }
            }
            catch (FaultException<DataFormatFault> ex)
            {
                Console.WriteLine($"DATA FORMAT ERROR: {ex.Detail.Message}");
                if (!string.IsNullOrEmpty(ex.Detail.Details))
                    Console.WriteLine($"Details: {ex.Detail.Details}");
            }
            catch (FaultException<ValidationFault> ex)
            {
                Console.WriteLine($"VALIDATION ERROR: {ex.Detail.Message}");
                Console.WriteLine($"Field: {ex.Detail.Field}, Value: {ex.Detail.Value}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        }

        private static EisMeta ExtractMetaFromPath(string filePath)
        {
            // Try to extract battery info from path
            var pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            string batteryId = "B01"; // default
            string testId = "Test_1"; // default
            int socPercent = 50; // default
            
            for (int i = 0; i < pathParts.Length; i++)
            {
                if (pathParts[i].StartsWith("B") && pathParts[i].Length == 3)
                {
                    batteryId = pathParts[i];
                }
                else if (pathParts[i].StartsWith("Test_"))
                {
                    testId = pathParts[i];
                }
            }
            
            // Try to extract SoC from filename
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)%?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int extractedSoc))
            {
                if (extractedSoc >= 5 && extractedSoc <= 100)
                    socPercent = extractedSoc;
            }

            return new EisMeta
            {
                BatteryId = batteryId,
                TestId = testId,
                SocPercent = socPercent,
                FileName = Path.GetFileName(filePath),
                TotalRows = CountCsvRows(filePath),
                StartedAt = DateTime.UtcNow,
                VThreshold = double.Parse(ConfigurationManager.AppSettings["V_threshold"] ?? "0.1", CultureInfo.InvariantCulture),
                ZThreshold = double.Parse(ConfigurationManager.AppSettings["Z_threshold"] ?? "0.5", CultureInfo.InvariantCulture),
                DeviationPercent = double.Parse(ConfigurationManager.AppSettings["DeviationPercent"] ?? "25", CultureInfo.InvariantCulture)
            };
        }

        private static int CountCsvRows(string filePath)
        {
            return EisFileDiscovery.CountCsvRows(filePath);
        }
    }
}


