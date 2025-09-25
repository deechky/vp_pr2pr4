using Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Client
{
	public class EisCsvReader : IDisposable
	{
		private readonly string csvFilePath;
		private readonly string rejectsFilePath;
		private StreamReader reader;
		private StreamWriter rejectsWriter;
		private int currentRowIndex = 0;
		private int acceptedCount = 0;
		private int rejectedCount = 0;
		private bool disposed = false;

		public int AcceptedCount => acceptedCount;
		public int RejectedCount => rejectedCount;

		public EisCsvReader(string csvFilePath, string rejectsFilePath)
		{
			this.csvFilePath = csvFilePath;
			this.rejectsFilePath = rejectsFilePath;
			
			if (!File.Exists(csvFilePath))
				throw new FileNotFoundException($"CSV file not found: {csvFilePath}");

			reader = new StreamReader(csvFilePath);
			
			// Create rejects file directory if needed
			Directory.CreateDirectory(Path.GetDirectoryName(rejectsFilePath));
			rejectsWriter = new StreamWriter(rejectsFilePath, false) { AutoFlush = true };
			rejectsWriter.WriteLine("RowIndex,Reason,RawLine");

			// Skip header if present
			if (!reader.EndOfStream)
			{
				string firstLine = reader.ReadLine();
				// Check if first line is a header (contains non-numeric data)
				if (IsHeaderLine(firstLine))
				{
					Console.WriteLine($"Skipped header: {firstLine}");
				}
				else
				{
					// Put back the line if it's not a header
					reader.BaseStream.Seek(0, SeekOrigin.Begin);
					reader = new StreamReader(reader.BaseStream);
				}
			}
		}

		private bool IsHeaderLine(string line)
		{
			if (string.IsNullOrWhiteSpace(line))
				return false;

			string[] parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.None);
			if (parts.Length < 6)
				return false;

			// Check for known header patterns
			string firstField = parts[0].Trim().ToLowerInvariant();
			if (firstField.Contains("frequency") || firstField.Contains("freq"))
				return true;

			// Try to parse first few fields as numbers
			var ci = CultureInfo.InvariantCulture;
			return !double.TryParse(parts[0].Trim(), NumberStyles.Float, ci, out _);
		}

		public bool TryReadNext(out EisSample sample)
		{
			sample = null;

			if (reader.EndOfStream)
				return false;

			string line = reader.ReadLine();
			currentRowIndex++;

			if (string.IsNullOrWhiteSpace(line))
			{
				rejectedCount++;
				rejectsWriter.WriteLine($"{currentRowIndex},Empty line,\"{line}\"");
				return TryReadNext(out sample); // Try next line
			}

			if (EisSample.TryParseCsv(line, currentRowIndex, out sample, out string error))
			{
				acceptedCount++;
				return true;
			}
			else
			{
				rejectedCount++;
				rejectsWriter.WriteLine($"{currentRowIndex},{error.Replace(',', ';')},\"{line}\"");
				// Continue to next line on parse error
				return TryReadNext(out sample);
			}
		}

		public static List<Common.EisFileInfo> DiscoverEisFiles(string basePath)
		{
			var files = new List<Common.EisFileInfo>();

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
						files.Add(new Common.EisFileInfo
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
									files.Add(new Common.EisFileInfo
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

		public void Dispose()
		{
			if (disposed)
				return;

			try
			{
				reader?.Dispose();
				rejectsWriter?.Dispose();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error disposing EisCsvReader: {ex.Message}");
			}
			finally
			{
				disposed = true;
			}
		}
	}

}
