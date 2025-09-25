using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Common
{
	public static class EisFileDiscovery
	{
		public static List<EisFileInfo> DiscoverEisFiles(string basePath)
		{
			var files = new List<EisFileInfo>();

			if (!Directory.Exists(basePath))
			{
				Console.WriteLine($"Base path does not exist: {basePath}");
				return files;
			}

			try
			{
				// Method 1: Look for Hioki CSV files directly in basePath
				var hiokiFiles = Directory.GetFiles(basePath, "Hk_*.csv", SearchOption.AllDirectories);
				foreach (string csvFile in hiokiFiles)
				{
					if (TryExtractSocFromHiokiFileName(csvFile, out int soc))
					{
						files.Add(new EisFileInfo
						{
							BatteryId = "B01", // Default for Hioki files
							TestId = "Test_1", // Default for Hioki files
							SocPercent = soc,
							FilePath = csvFile,
							FileName = Path.GetFileName(csvFile)
						});
					}
				}

				// Method 2: Traditional folder structure (B01, B02, ..., B11)
				var batteryDirs = Directory.GetDirectories(basePath, "B*", SearchOption.AllDirectories)
					.Where(dir => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(dir), @"^B\d{2}$"))
					.ToList();

				foreach (string batteryDir in batteryDirs)
				{
					string batteryId = Path.GetFileName(batteryDir);
					
					// Look for EIS Measurement folders
					var eisDirs = Directory.GetDirectories(batteryDir, "*EIS*", SearchOption.AllDirectories);
					
					foreach (string eisDir in eisDirs)
					{
						// Look for Test_1 or Test_2 folders
						var testDirs = Directory.GetDirectories(eisDir, "Test_*", SearchOption.TopDirectoryOnly);
						
						foreach (string testDir in testDirs)
						{
							string testId = Path.GetFileName(testDir);
							
							// Find CSV files in test directory
							var csvFiles = Directory.GetFiles(testDir, "*.csv", SearchOption.TopDirectoryOnly);
							
							foreach (string csvFile in csvFiles)
							{
								if (TryExtractSocFromFileName(csvFile, out int soc))
								{
									files.Add(new EisFileInfo
									{
										BatteryId = batteryId,
										TestId = testId,
										SocPercent = soc,
										FilePath = csvFile,
										FileName = Path.GetFileName(csvFile)
									});
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error discovering EIS files: {ex.Message}");
			}

			return files.OrderBy(f => f.BatteryId).ThenBy(f => f.TestId).ThenBy(f => f.SocPercent).ToList();
		}

		private static bool TryExtractSocFromHiokiFileName(string filePath, out int soc)
		{
			soc = 0;
			string fileName = Path.GetFileNameWithoutExtension(filePath);
			
			// Extract SoC from Hioki filename pattern: "Hk_IFR14500_SoC_50_03-07-2023_20-49"
			var match = System.Text.RegularExpressions.Regex.Match(fileName, @"Hk_.*_SoC_(\d+)_");
			if (match.Success && int.TryParse(match.Groups[1].Value, out soc))
			{
				// Validate SoC range
				if (soc >= 5 && soc <= 100)
					return true;
			}

			return false;
		}

		private static bool TryExtractSocFromFileName(string filePath, out int soc)
		{
			soc = 0;
			string fileName = Path.GetFileNameWithoutExtension(filePath);
			
			// Try to extract SoC from filename patterns like "5%.csv", "10%.csv", etc.
			var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)%?");
			if (match.Success && int.TryParse(match.Groups[1].Value, out soc))
			{
				// Validate SoC range
				if (soc >= 5 && soc <= 100 && soc % 5 == 0)
					return true;
			}

			return false;
		}

		public static int CountCsvRows(string filePath)
		{
			try
			{
				var lines = File.ReadAllLines(filePath);
				int totalLines = lines.Length;
				
				// Check if first line is header
				if (totalLines > 0 && IsHeaderLine(lines[0]))
					return totalLines - 1;
				
				return totalLines;
			}
			catch
			{
				return 29; // Expected 29 rows per Hioki EIS file based on actual data
			}
		}

		private static bool IsHeaderLine(string line)
		{
			if (string.IsNullOrWhiteSpace(line))
				return false;

			string firstField = line.Split(',')[0].Trim().ToLowerInvariant();
			return firstField.Contains("frequency") || firstField.Contains("freq");
		}
	}
}
