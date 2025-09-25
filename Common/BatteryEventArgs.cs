using System;

namespace Common
{
	public class BatteryEventArgs : EventArgs
	{
		public string BatteryId { get; set; }
		public string TestId { get; set; }
		public int SocPercent { get; set; }
		public string Message { get; set; }
		public DateTime Timestamp { get; set; }
		public string AlertType { get; set; }
		public double Value { get; set; }
		public double Threshold { get; set; }

		public BatteryEventArgs()
		{
			Timestamp = DateTime.UtcNow;
		}

		public BatteryEventArgs(string batteryId, string testId, int socPercent, string message) : this()
		{
			BatteryId = batteryId;
			TestId = testId;
			SocPercent = socPercent;
			Message = message;
		}
	}

	public class VoltageEventArgs : BatteryEventArgs
	{
		public double VoltageChange { get; set; }
		public double PreviousVoltage { get; set; }
		public double CurrentVoltage { get; set; }
		public string Direction { get; set; }

		public VoltageEventArgs(string batteryId, string testId, int socPercent, 
			double voltageChange, double previousVoltage, double currentVoltage, string direction)
			: base(batteryId, testId, socPercent, $"Voltage spike detected: ΔV={voltageChange:F3}V ({direction})")
		{
			VoltageChange = voltageChange;
			PreviousVoltage = previousVoltage;
			CurrentVoltage = currentVoltage;
			Direction = direction;
			AlertType = "VoltageSpike";
			Value = Math.Abs(voltageChange);
		}
	}

	public class ImpedanceEventArgs : BatteryEventArgs
	{
		public double ImpedanceChange { get; set; }
		public double PreviousImpedance { get; set; }
		public double CurrentImpedance { get; set; }
		public string Direction { get; set; }

		public ImpedanceEventArgs(string batteryId, string testId, int socPercent,
			double impedanceChange, double previousImpedance, double currentImpedance, string direction)
			: base(batteryId, testId, socPercent, $"Impedance jump detected: ΔZ={impedanceChange:F3}Ω ({direction})")
		{
			ImpedanceChange = impedanceChange;
			PreviousImpedance = previousImpedance;
			CurrentImpedance = currentImpedance;
			Direction = direction;
			AlertType = "ImpedanceJump";
			Value = Math.Abs(impedanceChange);
		}
	}

	public class OutOfBandEventArgs : BatteryEventArgs
	{
		public double ExpectedValue { get; set; }
		public double ActualValue { get; set; }
		public double RunningMean { get; set; }
		public string Parameter { get; set; }

		public OutOfBandEventArgs(string batteryId, string testId, int socPercent,
			string parameter, double actualValue, double expectedValue, double runningMean, string direction)
			: base(batteryId, testId, socPercent, $"{parameter} out of band: {actualValue:F3} {direction} expected range (Mean: {runningMean:F3})")
		{
			Parameter = parameter;
			ActualValue = actualValue;
			ExpectedValue = expectedValue;
			RunningMean = runningMean;
			AlertType = "OutOfBandWarning";
			Value = actualValue;
		}
	}
}
