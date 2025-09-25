using System;

namespace Common
{
	public class BatterySubscriber
	{
		public string SubscriberId { get; set; }
		public string Name { get; set; }

		public BatterySubscriber(string subscriberId, string name)
		{
			SubscriberId = subscriberId;
			Name = name;
		}

		public void OnTransferStarted(object sender, BatteryEventArgs e)
		{
			Console.WriteLine($"[{SubscriberId}] Transfer Started: {e.Message}");
		}

		public void OnSampleReceived(object sender, BatteryEventArgs e)
		{
			// Quiet operation for performance
		}

		public void OnTransferCompleted(object sender, BatteryEventArgs e)
		{
			Console.WriteLine($"[{SubscriberId}] Transfer Completed: {e.Message}");
		}

		public void OnWarningRaised(object sender, BatteryEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"[{SubscriberId}] ⚠️ WARNING: {e.Message}");
			Console.ResetColor();
		}

		public void OnVoltageSpike(object sender, VoltageEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[{SubscriberId}] 🔴 VOLTAGE SPIKE: ΔV={e.VoltageChange:F3}V ({e.Direction})");
			Console.WriteLine($"    Previous: {e.PreviousVoltage:F3}V → Current: {e.CurrentVoltage:F3}V");
			Console.ResetColor();
		}

		public void OnImpedanceJump(object sender, ImpedanceEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[{SubscriberId}] 🔴 IMPEDANCE JUMP: ΔZ={e.ImpedanceChange:F3}Ω ({e.Direction})");
			Console.WriteLine($"    Previous: {e.PreviousImpedance:F3}Ω → Current: {e.CurrentImpedance:F3}Ω");
			Console.ResetColor();
		}

		public void OnOutOfBandWarning(object sender, OutOfBandEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"[{SubscriberId}] 🟡 OUT OF BAND: {e.Parameter}={e.ActualValue:F3} (Mean: {e.RunningMean:F3})");
			Console.ResetColor();
		}
	}
}
