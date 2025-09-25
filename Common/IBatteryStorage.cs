using System;
using System.Collections.Generic;

namespace Common
{
	public interface IBatteryStorage : IDisposable
	{
		void InitializeSession(EisMeta meta);
		void StoreSample(EisSample sample);
		void StoreRejectedSample(string reason, string rawData);
		void StoreAnalyticsEvent(string alertType, string message, double value, double threshold);
		void FinalizeSession();
		string GetSessionDirectory();
		int GetSampleCount();
	}

	public delegate void TransferStartedHandler(object sender, BatteryEventArgs e);
	public delegate void SampleReceivedHandler(object sender, BatteryEventArgs e);
	public delegate void TransferCompletedHandler(object sender, BatteryEventArgs e);
	public delegate void WarningRaisedHandler(object sender, BatteryEventArgs e);
	public delegate void VoltageSpikeDHandler(object sender, VoltageEventArgs e);
	public delegate void ImpedanceJumpHandler(object sender, ImpedanceEventArgs e);
	public delegate void OutOfBandWarningHandler(object sender, OutOfBandEventArgs e);
}
