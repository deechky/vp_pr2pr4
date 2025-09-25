using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;

namespace UploadClient
{
	public class BatteryDataUploader : IDisposable
	{
		private readonly string datasetPath;
		private ChannelFactory<IBatteryService> factory;
		private IBatteryService service;
		private bool disposed = false;

		// Events for upload progress
		public event EventHandler<BatteryUploadEventArgs> OnUploadStarted;
		public event EventHandler<BatteryUploadEventArgs> OnFileUploaded;
		public event EventHandler<BatteryUploadEventArgs> OnUploadCompleted;
		public event EventHandler<BatteryUploadEventArgs> OnUploadError;

		public BatteryDataUploader(string datasetPath)
		{
			this.datasetPath = datasetPath;
			
			// Initialize WCF client
			factory = new ChannelFactory<IBatteryService>("Battery");
			service = factory.CreateChannel();
		}

		public void UploadAllBatteryData()
		{
			try
			{
				var eisFiles = EisFileDiscovery.DiscoverEisFiles(datasetPath);
				
				if (eisFiles.Count == 0)
				{
					OnUploadError?.Invoke(this, new BatteryUploadEventArgs 
					{ 
						Message = $"No EIS files found in {datasetPath}",
						IsError = true
					});
					return;
				}

				OnUploadStarted?.Invoke(this, new BatteryUploadEventArgs 
				{ 
					Message = $"Starting upload of {eisFiles.Count} EIS files",
					TotalFiles = eisFiles.Count
				});

				Console.WriteLine($"Found {eisFiles.Count} EIS files to upload:");
				
				int successCount = 0;
				int errorCount = 0;

				foreach (var fileInfo in eisFiles)
				{
					try
					{
						Console.WriteLine($"\nUploading: {fileInfo}");
						UploadSingleFile(fileInfo);
						successCount++;
						
						OnFileUploaded?.Invoke(this, new BatteryUploadEventArgs 
						{ 
							Message = $"Successfully uploaded {fileInfo.FileName}",
							BatteryId = fileInfo.BatteryId,
							TestId = fileInfo.TestId,
							SocPercent = fileInfo.SocPercent,
							FileName = fileInfo.FileName,
							ProcessedFiles = successCount + errorCount,
							TotalFiles = eisFiles.Count
						});
					}
					catch (Exception ex)
					{
						errorCount++;
						Console.WriteLine($"❌ Error uploading {fileInfo.FileName}: {ex.Message}");
						
						OnUploadError?.Invoke(this, new BatteryUploadEventArgs 
						{ 
							Message = $"Error uploading {fileInfo.FileName}: {ex.Message}",
							BatteryId = fileInfo.BatteryId,
							TestId = fileInfo.TestId,
							SocPercent = fileInfo.SocPercent,
							FileName = fileInfo.FileName,
							IsError = true
						});
					}
				}

				OnUploadCompleted?.Invoke(this, new BatteryUploadEventArgs 
				{ 
					Message = $"Upload completed. Success: {successCount}, Errors: {errorCount}",
					ProcessedFiles = successCount + errorCount,
					TotalFiles = eisFiles.Count,
					SuccessCount = successCount,
					ErrorCount = errorCount
				});

				Console.WriteLine($"\n✅ Upload summary: {successCount} successful, {errorCount} errors");
			}
			catch (Exception ex)
			{
				OnUploadError?.Invoke(this, new BatteryUploadEventArgs 
				{ 
					Message = $"Fatal upload error: {ex.Message}",
					IsError = true
				});
				throw;
			}
		}

		public void UploadSingleFile(EisFileInfo fileInfo)
		{
			// Create meta information for this file
			var meta = new EisMeta
			{
				BatteryId = fileInfo.BatteryId,
				TestId = fileInfo.TestId,
				SocPercent = fileInfo.SocPercent,
				FileName = fileInfo.FileName,
				TotalRows = CountCsvRows(fileInfo.FilePath),
				StartedAt = DateTime.UtcNow,
				VThreshold = 0.1, // Could be configurable
				ZThreshold = 0.5,  // Could be configurable
				DeviationPercent = 25 // Could be configurable
			};

			// Start session
			var startAck = service.StartSession(meta);
			if (!startAck.Success)
			{
				throw new Exception($"Failed to start session: {startAck.Message}");
			}

			int uploadedCount = 0;
			string tempRejects = Path.GetTempFileName();
			
			try
			{
				// Upload all samples from the file
				using (var reader = new SimpleEisCsvReader(fileInfo.FilePath, tempRejects))
				{
					while (reader.TryReadNext(out var sample))
					{
						var pushAck = service.PushSample(sample);
						if (pushAck.Success)
						{
							uploadedCount++;
						}
						else
						{
							Console.WriteLine($"⚠️ Sample rejected: {pushAck.Message}");
						}

						// Show progress
						if (uploadedCount % 10 == 0)
						{
							Console.Write(".");
						}
					}
					
					Console.WriteLine($" Uploaded {uploadedCount} samples (accepted: {reader.AcceptedCount}, rejected: {reader.RejectedCount})");
				}

				// End session
				var endAck = service.EndSession();
				if (!endAck.Success)
				{
					Console.WriteLine($"⚠️ Warning: End session returned: {endAck.Message}");
				}
			}
			finally
			{
				// Clean up temp rejects file
				try
				{
					if (File.Exists(tempRejects))
						File.Delete(tempRejects);
				}
				catch { /* Ignore cleanup errors */ }
			}
		}

		private int CountCsvRows(string filePath)
		{
			return EisFileDiscovery.CountCsvRows(filePath);
		}

		public void TestConnection()
		{
			try
			{
				// Try a simple operation to test the connection
				Console.WriteLine("Testing connection to Battery service...");
				
				// We'll test with a minimal meta object
				var testMeta = new EisMeta
				{
					BatteryId = "TEST",
					TestId = "Test_Connection",
					SocPercent = 50,
					FileName = "test.csv",
					TotalRows = 1,
					StartedAt = DateTime.UtcNow,
					VThreshold = 0.1,
					ZThreshold = 0.5,
					DeviationPercent = 25
				};

				var ack = service.StartSession(testMeta);
				if (ack.Success)
				{
					service.EndSession(); // Clean up
					Console.WriteLine("✅ Connection test successful");
				}
				else
				{
					Console.WriteLine($"⚠️ Connection test warning: {ack.Message}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Connection test failed: {ex.Message}");
				throw;
			}
		}

		public void Dispose()
		{
			if (disposed)
				return;

			try
			{
				if (service is ICommunicationObject commObj)
				{
					if (commObj.State == CommunicationState.Faulted)
						commObj.Abort();
					else
						commObj.Close();
				}
				
				factory?.Close();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error disposing BatteryDataUploader: {ex.Message}");
				factory?.Abort();
			}
			finally
			{
				disposed = true;
			}
		}
	}

	public class BatteryUploadEventArgs : EventArgs
	{
		public string Message { get; set; }
		public string BatteryId { get; set; }
		public string TestId { get; set; }
		public int SocPercent { get; set; }
		public string FileName { get; set; }
		public int ProcessedFiles { get; set; }
		public int TotalFiles { get; set; }
		public int SuccessCount { get; set; }
		public int ErrorCount { get; set; }
		public bool IsError { get; set; }
		public DateTime Timestamp { get; set; }

		public BatteryUploadEventArgs()
		{
			Timestamp = DateTime.UtcNow;
		}
	}
}
