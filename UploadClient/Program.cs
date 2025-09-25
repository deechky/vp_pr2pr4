using Common;
using System;
using System.IO;
using System.ServiceModel;

namespace UploadClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Li-ion Battery EIS Data Upload Client ===");
            Console.WriteLine("This client uploads multiple EIS files to the Battery Analysis Service");
            Console.WriteLine();

            try
            {
                // Determine dataset path
                string datasetPath = GetDatasetPath(args);
                
                if (!Directory.Exists(datasetPath))
                {
                    Console.WriteLine($"❌ Dataset path not found: {datasetPath}");
                    Console.WriteLine("Usage: UploadClient.exe [dataset_path]");
                    Console.WriteLine("If no path is provided, will look for Dataset folder in current directory");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine($"Dataset path: {datasetPath}");
                
                using (var uploader = new BatteryDataUploader(datasetPath))
                {
                    // Subscribe to events for progress tracking
                    uploader.OnUploadStarted += (s, e) => 
                    {
                        Console.WriteLine($"🚀 {e.Message}");
                    };
                    
                    uploader.OnFileUploaded += (s, e) => 
                    {
                        Console.WriteLine($"✅ [{e.ProcessedFiles}/{e.TotalFiles}] {e.Message}");
                    };
                    
                    uploader.OnUploadCompleted += (s, e) => 
                    {
                        Console.WriteLine($"🎉 {e.Message}");
                    };
                    
                    uploader.OnUploadError += (s, e) => 
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ {e.Message}");
                        Console.ResetColor();
                    };

                    // Test connection first
                    uploader.TestConnection();
                    Console.WriteLine();

                    // Ask user for confirmation
                    Console.WriteLine("Ready to upload all EIS files to the Battery Analysis Service.");
                    Console.WriteLine("Make sure the Server is running before proceeding.");
                    Console.Write("Continue? (y/N): ");
                    
                    string response = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(response) || !response.ToLowerInvariant().StartsWith("y"))
                    {
                        Console.WriteLine("Upload cancelled by user.");
                        return;
                    }

                    Console.WriteLine();
                    Console.WriteLine("Starting upload...");
                    Console.WriteLine("Watch the Server console for voltage and impedance analysis alerts! 🔴");
                    Console.WriteLine();

                    // Start the upload process
                    uploader.UploadAllBatteryData();
                }
            }
            catch (EndpointNotFoundException ex)
            {
                Console.WriteLine("❌ Cannot connect to Battery service.");
                Console.WriteLine("Make sure the Server is running on localhost:4100");
                Console.WriteLine($"Error: {ex.Message}");
            }
            catch (CommunicationException ex)
            {
                Console.WriteLine("❌ Communication error with Battery service.");
                Console.WriteLine($"Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Unexpected error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static string GetDatasetPath(string[] args)
        {
            // Check command line argument first
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                string argPath = args[0];
                if (Directory.Exists(argPath))
                    return argPath;
            }

            // Look for the specific Hioki folder with your uploaded CSV files
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Project Dataset folder (when running from IDE) - PRIORITY
            string projDataset = Path.Combine(baseDir, "..", "..", "..", "Client", "Dataset");
            if (Directory.Exists(projDataset))
            {
                string hiokiPath = Path.Combine(projDataset, "Hioki");
                if (Directory.Exists(hiokiPath) && Directory.GetFiles(hiokiPath, "Hk_*.csv").Length > 0)
                {
                    Console.WriteLine($"Found Hioki files in: {hiokiPath}");
                    return projDataset;
                }
            }

            // 2. bin/Debug/Dataset (when running from build output)
            string binDataset = Path.Combine(baseDir, "Dataset");
            if (Directory.Exists(binDataset))
            {
                string hiokiPath = Path.Combine(binDataset, "Hioki");
                if (Directory.Exists(hiokiPath) && Directory.GetFiles(hiokiPath, "Hk_*.csv").Length > 0)
                {
                    Console.WriteLine($"Found Hioki files in: {hiokiPath}");
                    return binDataset;
                }
            }

            // 3. Look for Dataset folder in parent directories
            DirectoryInfo currentDir = new DirectoryInfo(baseDir);
            while (currentDir != null)
            {
                string candidatePath = Path.Combine(currentDir.FullName, "Dataset");
                if (Directory.Exists(candidatePath))
                {
                    string hiokiPath = Path.Combine(candidatePath, "Hioki");
                    if (Directory.Exists(hiokiPath) && Directory.GetFiles(hiokiPath, "Hk_*.csv").Length > 0)
                    {
                        Console.WriteLine($"Found Hioki files in: {hiokiPath}");
                        return candidatePath;
                    }
                }
                currentDir = currentDir.Parent;
            }

            // 4. Default fallback to project dataset
            string fallbackPath = Path.Combine(baseDir, "..", "..", "..", "Client", "Dataset");
            Console.WriteLine($"Using fallback path: {fallbackPath}");
            return Path.GetFullPath(fallbackPath);
        }
    }
}
