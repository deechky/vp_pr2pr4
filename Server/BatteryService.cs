using Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.ServiceModel;

namespace Server
{
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Single)]
	public class BatteryService : IBatteryService, IDisposable
	{
		private readonly string storageRoot = ConfigurationManager.AppSettings["storagePath"] ?? "BatteryStorage";
		private IBatteryStorage storage;
		private string currentSessionDir;

		// Current session data
		private EisMeta currentSession;
		private double? lastVoltage;
		private double? lastImpedance;
		private double runningMeanImpedance;
		private long sampleCount;
		private int written;
		private readonly object lockObject = new object();

		// Events according to specification
		public event TransferStartedHandler OnTransferStarted;
		public event SampleReceivedHandler OnSampleReceived;
		public event TransferCompletedHandler OnTransferCompleted;
		public event WarningRaisedHandler OnWarningRaised;
		public event VoltageSpikeDHandler OnVoltageSpike;
		public event ImpedanceJumpHandler OnImpedanceJump;
		public event OutOfBandWarningHandler OnOutOfBandWarning;

		public BatteryService()
		{
			// Subscribe to own events for logging
			OnTransferStarted += (s, e) => Console.WriteLine($"[START] {e.Message}");
			OnSampleReceived += (s, e) => Console.Write('.');
			OnTransferCompleted += (s, e) => Console.WriteLine($"\n[END] {e.Message}");
			OnWarningRaised += (s, e) => 
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"\n⚠️ WARNING: {e.Message}");
				Console.ResetColor();
			};
			
			// Subscribe to specific analytics events
			OnVoltageSpike += (s, e) => 
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"\n🔴 VOLTAGE SPIKE: ΔV={e.VoltageChange:F3}V ({e.Direction})");
				Console.ResetColor();
			};
			
			OnImpedanceJump += (s, e) => 
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"\n🔴 IMPEDANCE JUMP: ΔZ={e.ImpedanceChange:F3}Ω ({e.Direction})");
				Console.ResetColor();
			};
			
			OnOutOfBandWarning += (s, e) => 
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"\n🟡 OUT OF BAND: {e.Parameter}={e.ActualValue:F3} (Mean: {e.RunningMean:F3})");
				Console.ResetColor();
			};
		}

		public Ack StartSession(EisMeta meta)
		{
			lock (lockObject)
			{
				try
				{
					if (meta == null)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "EisMeta is null", 
							Field = "meta", 
							Value = "null" 
						});

					// Validate meta-header according to specification
					if (string.IsNullOrWhiteSpace(meta.BatteryId))
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "BatteryId is required", 
							Field = "BatteryId", 
							Value = meta.BatteryId ?? "null" 
						});

					if (string.IsNullOrWhiteSpace(meta.TestId))
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "TestId is required", 
							Field = "TestId", 
							Value = meta.TestId ?? "null" 
						});

					if (meta.SocPercent < 0 || meta.SocPercent > 100)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "SoC% must be between 0 and 100", 
							Field = "SocPercent", 
							Value = meta.SocPercent.ToString() 
						});

					if (meta.VThreshold <= 0)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "V_threshold must be positive", 
							Field = "VThreshold", 
							Value = meta.VThreshold.ToString() 
						});

					if (meta.ZThreshold <= 0)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "Z_threshold must be positive", 
							Field = "ZThreshold", 
							Value = meta.ZThreshold.ToString() 
						});

					if (meta.DeviationPercent <= 0 || meta.DeviationPercent > 100)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "DeviationPercent must be between 0 and 100", 
							Field = "DeviationPercent", 
							Value = meta.DeviationPercent.ToString() 
						});

					currentSession = meta;
					
					// Create storage structure: Data/<BatteryId>/<TestId>/<SoC%>/session.csv
					currentSessionDir = Path.Combine(storageRoot, meta.BatteryId, meta.TestId, $"{meta.SocPercent}%");
					Directory.CreateDirectory(currentSessionDir);

					storage = new BatteryFileStorage(currentSessionDir);
					storage.InitializeSession(meta);

					// Reset analytics state
					lastVoltage = null;
					lastImpedance = null;
					runningMeanImpedance = 0;
					sampleCount = 0;
					written = 0;

					var startEvent = new BatteryEventArgs(meta.BatteryId, meta.TestId, meta.SocPercent, 
						$"Session started - File: {meta.FileName}, Expected rows: {meta.TotalRows}");
					OnTransferStarted?.Invoke(this, startEvent);

					Console.WriteLine($"✅ Battery session started successfully");
					Console.WriteLine($"   Directory: {currentSessionDir}");
					Console.WriteLine($"   Battery: {meta.BatteryId}, Test: {meta.TestId}, SoC: {meta.SocPercent}%");
					Console.WriteLine($"   Thresholds: V={meta.VThreshold:F3}, Z={meta.ZThreshold:F3}, Deviation={meta.DeviationPercent}%");

					return new Ack { Success = true, Message = "Session started", Status = "IN_PROGRESS" };
				}
				catch (FaultException)
				{
					throw; // Re-throw fault exceptions as-is
				}
				catch (Exception ex)
				{
					throw new FaultException<DataFormatFault>(new DataFormatFault 
					{ 
						Message = ex.Message, 
						Details = ex.StackTrace 
					});
				}
			}
		}

		public Ack PushSample(EisSample sample)
		{
			lock (lockObject)
			{
				try
				{
					if (storage == null || currentSession == null)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "Session not started", 
							Field = "session", 
							Value = "null" 
						});

					// Validate sample according to specification
					if (!ValidateSample(sample, out string validationError))
					{
						storage.StoreRejectedSample(validationError, SerializeSample(sample));
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = validationError, 
							Field = "sample", 
							Value = SerializeSample(sample) 
						});
					}

					// Store valid sample
					storage.StoreSample(sample);
					written++;

					// ANALITIKA 1: Detekcija naglih promena napona (ΔV)
					if (lastVoltage.HasValue)
					{
						double deltaV = sample.V - lastVoltage.Value;
						if (Math.Abs(deltaV) > currentSession.VThreshold)
						{
							string direction = deltaV > 0 ? "IZNAD očekivanog" : "ISPOD očekivanog";
							var voltageEvent = new VoltageEventArgs(currentSession.BatteryId, currentSession.TestId, 
								currentSession.SocPercent, deltaV, lastVoltage.Value, sample.V, direction);
							voltageEvent.Threshold = currentSession.VThreshold;
							
							OnVoltageSpike?.Invoke(this, voltageEvent);
							storage.StoreAnalyticsEvent("VoltageSpike", voltageEvent.Message, Math.Abs(deltaV), currentSession.VThreshold);
						}
					}
					lastVoltage = sample.V;

					// ANALITIKA 2: Detekcija promene impedanse (ΔZ)
					double currentImpedance = sample.CalculateImpedance();
					if (lastImpedance.HasValue)
					{
						double deltaZ = currentImpedance - lastImpedance.Value;
						if (Math.Abs(deltaZ) > currentSession.ZThreshold)
						{
							string direction = deltaZ > 0 ? "IZNAD očekivanog" : "ISPOD očekivanog";
							var impedanceEvent = new ImpedanceEventArgs(currentSession.BatteryId, currentSession.TestId,
								currentSession.SocPercent, deltaZ, lastImpedance.Value, currentImpedance, direction);
							impedanceEvent.Threshold = currentSession.ZThreshold;
							
							OnImpedanceJump?.Invoke(this, impedanceEvent);
							storage.StoreAnalyticsEvent("ImpedanceJump", impedanceEvent.Message, Math.Abs(deltaZ), currentSession.ZThreshold);
						}
					}

					// Running mean i ±25% odstupanje
					runningMeanImpedance = ((runningMeanImpedance * sampleCount) + currentImpedance) / (sampleCount + 1);
					sampleCount++;

					double lowBound = runningMeanImpedance * (1 - currentSession.DeviationPercent / 100.0);
					double highBound = runningMeanImpedance * (1 + currentSession.DeviationPercent / 100.0);
					
					if (currentImpedance < lowBound)
					{
						var outOfBandEvent = new OutOfBandEventArgs(currentSession.BatteryId, currentSession.TestId,
							currentSession.SocPercent, "Impedance", currentImpedance, lowBound, runningMeanImpedance, "ISPOD očekivane vrednosti");
						OnOutOfBandWarning?.Invoke(this, outOfBandEvent);
						storage.StoreAnalyticsEvent("OutOfBandWarning", outOfBandEvent.Message, currentImpedance, lowBound);
					}
					else if (currentImpedance > highBound)
					{
						var outOfBandEvent = new OutOfBandEventArgs(currentSession.BatteryId, currentSession.TestId,
							currentSession.SocPercent, "Impedance", currentImpedance, highBound, runningMeanImpedance, "IZNAD očekivane vrednosti");
						OnOutOfBandWarning?.Invoke(this, outOfBandEvent);
						storage.StoreAnalyticsEvent("OutOfBandWarning", outOfBandEvent.Message, currentImpedance, highBound);
					}

					lastImpedance = currentImpedance;

					var sampleEvent = new BatteryEventArgs(currentSession.BatteryId, currentSession.TestId, 
						currentSession.SocPercent, "Sample received");
					OnSampleReceived?.Invoke(this, sampleEvent);

					if (written <= 3)
					{
						Console.WriteLine($"\n✅ Sample {written} accepted: V={sample.V:F3}V, Z={currentImpedance:F3}Ω, F={sample.FrequencyHz:F3}Hz");
					}
					if (written % 20 == 0) Console.Write(" ");

					return new Ack { Success = true, Message = "Sample accepted", Status = "IN_PROGRESS" };
				}
				catch (FaultException)
				{
					throw; // Re-throw fault exceptions as-is
				}
				catch (Exception ex)
				{
					if (storage != null)
						storage.StoreRejectedSample($"Processing error: {ex.Message}", SerializeSample(sample));
					
					throw new FaultException<DataFormatFault>(new DataFormatFault 
					{ 
						Message = ex.Message, 
						Details = ex.StackTrace 
					});
				}
			}
		}

		public Ack EndSession()
		{
			lock (lockObject)
			{
				try
				{
					if (storage == null || currentSession == null)
						throw new FaultException<ValidationFault>(new ValidationFault 
						{ 
							Message = "No active session", 
							Field = "session", 
							Value = "null" 
						});

					storage.FinalizeSession();
					
					var endEvent = new BatteryEventArgs(currentSession.BatteryId, currentSession.TestId, 
						currentSession.SocPercent, $"Session completed - {written} samples processed");
					OnTransferCompleted?.Invoke(this, endEvent);

					Console.WriteLine($"\n✅ Battery session completed successfully");
					Console.WriteLine($"   Processed samples: {written}");
					Console.WriteLine($"   Directory: {currentSessionDir}");

					Dispose();
					return new Ack { Success = true, Message = "Session completed", Status = "COMPLETED" };
				}
				catch (FaultException)
				{
					throw; // Re-throw fault exceptions as-is
				}
				catch (Exception ex)
				{
					throw new FaultException<DataFormatFault>(new DataFormatFault 
					{ 
						Message = ex.Message, 
						Details = ex.StackTrace 
					});
				}
			}
		}

		private bool ValidateSample(EisSample sample, out string error)
		{
			error = string.Empty;
			
			if (sample == null) 
			{ 
				error = "Sample is null"; 
				return false; 
			}

			// Validate FrequencyHz > 0
			if (sample.FrequencyHz <= 0 || double.IsNaN(sample.FrequencyHz) || double.IsInfinity(sample.FrequencyHz))
			{ 
				error = $"Invalid FrequencyHz: {sample.FrequencyHz} (must be positive)"; 
				return false; 
			}

			// Validate real values for R, X, V
			if (double.IsNaN(sample.R_ohm) || double.IsInfinity(sample.R_ohm))
			{ 
				error = $"Invalid R_ohm: {sample.R_ohm}"; 
				return false; 
			}

			if (double.IsNaN(sample.X_ohm) || double.IsInfinity(sample.X_ohm))
			{ 
				error = $"Invalid X_ohm: {sample.X_ohm}"; 
				return false; 
			}

			if (double.IsNaN(sample.V) || double.IsInfinity(sample.V))
			{ 
				error = $"Invalid V: {sample.V}"; 
				return false; 
			}

			// Validate monotonic RowIndex increase
			if (sample.RowIndex < 0)
			{ 
				error = $"Invalid RowIndex: {sample.RowIndex} (must be non-negative)"; 
				return false; 
			}

			// Check for required fields
			if (sample.Timestamp == default(DateTime))
			{ 
				error = "Invalid Timestamp"; 
				return false; 
			}

			return true;
		}

		private static string SerializeSample(EisSample sample)
		{
			if (sample == null) return "<null>";
			return $"{sample.FrequencyHz},{sample.R_ohm},{sample.X_ohm},{sample.V},{sample.T_degC},{sample.Range_ohm},{sample.RowIndex}";
		}

		public void Dispose()
		{
			lock (lockObject)
			{
				try
				{
					storage?.Dispose();
					storage = null;
					currentSession = null;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error during disposal: {ex.Message}");
				}
			}
		}
	}
}
