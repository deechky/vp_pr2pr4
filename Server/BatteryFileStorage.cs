using Common;
using System;
using System.Globalization;
using System.IO;

namespace Server
{
	public class BatteryFileStorage : IBatteryStorage
	{
		private readonly string sessionDirectory;
		private FileStream sessionStream;
		private StreamWriter sessionWriter;
		private FileStream rejectsStream;
		private StreamWriter rejectsWriter;
		private FileStream analyticsStream;
		private StreamWriter analyticsWriter;
		private int sampleCount;
		private bool disposed = false;

		public BatteryFileStorage(string sessionDirectory)
		{
			this.sessionDirectory = sessionDirectory;
		}

		public void InitializeSession(EisMeta meta)
		{
			try
			{
				// Create unique session files with timestamp to avoid overwriting
				string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
				
				// Create session.csv for valid samples
				string sessionFile = Path.Combine(sessionDirectory, $"session_{timestamp}.csv");
				sessionStream = new FileStream(sessionFile, FileMode.Create, FileAccess.Write, FileShare.Read);
				sessionWriter = new StreamWriter(sessionStream) { AutoFlush = true };
				sessionWriter.WriteLine("FrequencyHz,R_ohm,X_ohm,V,T_degC,Range_ohm,RowIndex,Timestamp,Impedance");

				// Create rejects.csv for rejected samples
				string rejectsFile = Path.Combine(sessionDirectory, $"rejects_{timestamp}.csv");
				rejectsStream = new FileStream(rejectsFile, FileMode.Create, FileAccess.Write, FileShare.Read);
				rejectsWriter = new StreamWriter(rejectsStream) { AutoFlush = true };
				rejectsWriter.WriteLine("Reason,RawData");

				// Create analytics.csv for alerts
				string analyticsFile = Path.Combine(sessionDirectory, $"analytics_{timestamp}.csv");
				analyticsStream = new FileStream(analyticsFile, FileMode.Create, FileAccess.Write, FileShare.Read);
				analyticsWriter = new StreamWriter(analyticsStream) { AutoFlush = true };
				analyticsWriter.WriteLine("Timestamp,AlertType,Message,Value,Threshold");

				sampleCount = 0;
				Console.WriteLine($"✅ Storage initialized in: {sessionDirectory}");
				Console.WriteLine($"   Session files: session_{timestamp}.csv, rejects_{timestamp}.csv, analytics_{timestamp}.csv");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Failed to initialize storage: {ex.Message}");
				throw;
			}
		}

		public void StoreSample(EisSample sample)
		{
			if (sessionWriter == null)
				throw new InvalidOperationException("Storage not initialized");

			try
			{
				double impedance = sample.CalculateImpedance();
				var ci = CultureInfo.InvariantCulture;
				
				sessionWriter.WriteLine(string.Join(",",
					sample.FrequencyHz.ToString(ci),
					sample.R_ohm.ToString(ci),
					sample.X_ohm.ToString(ci),
					sample.V.ToString(ci),
					sample.T_degC.ToString(ci),
					sample.Range_ohm.ToString(ci),
					sample.RowIndex.ToString(),
					sample.Timestamp.ToString("O"),
					impedance.ToString(ci)));

				sampleCount++;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Failed to store sample: {ex.Message}");
				throw;
			}
		}

		public void StoreRejectedSample(string reason, string rawData)
		{
			if (rejectsWriter == null)
				return; // Silently ignore if not initialized

			try
			{
				// Escape commas in reason and rawData
				string escapedReason = reason?.Replace(",", ";") ?? "Unknown";
				string escapedData = rawData?.Replace(",", ";") ?? "Unknown";
				
				rejectsWriter.WriteLine($"{escapedReason},{escapedData}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Failed to store rejected sample: {ex.Message}");
			}
		}

		public void StoreAnalyticsEvent(string alertType, string message, double value, double threshold)
		{
			if (analyticsWriter == null)
				return; // Silently ignore if not initialized

			try
			{
				var ci = CultureInfo.InvariantCulture;
				string escapedMessage = message?.Replace(",", ";") ?? "Unknown";
				
				analyticsWriter.WriteLine(string.Join(",",
					DateTime.UtcNow.ToString("O"),
					alertType,
					escapedMessage,
					value.ToString(ci),
					threshold.ToString(ci)));
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Failed to store analytics event: {ex.Message}");
			}
		}

		public void FinalizeSession()
		{
			// Flush all writers to ensure data is written
			try
			{
				sessionWriter?.Flush();
				rejectsWriter?.Flush();
				analyticsWriter?.Flush();
				Console.WriteLine($"✅ Session finalized - {sampleCount} samples stored");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error finalizing session: {ex.Message}");
			}
		}

		public string GetSessionDirectory()
		{
			return sessionDirectory;
		}

		public int GetSampleCount()
		{
			return sampleCount;
		}

		public void Dispose()
		{
			if (disposed)
				return;

			try
			{
				FinalizeSession();

				sessionWriter?.Dispose();
				sessionStream?.Dispose();
				rejectsWriter?.Dispose();
				rejectsStream?.Dispose();
				analyticsWriter?.Dispose();
				analyticsStream?.Dispose();

				sessionWriter = null;
				sessionStream = null;
				rejectsWriter = null;
				rejectsStream = null;
				analyticsWriter = null;
				analyticsStream = null;

				Console.WriteLine($"✅ Storage disposed properly");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error during storage disposal: {ex.Message}");
			}
			finally
			{
				disposed = true;
			}
		}
	}
}
