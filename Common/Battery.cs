using System;
using System.Runtime.Serialization;

namespace Common
{
	[DataContract]
	public class Battery
	{
		[DataMember]
		public string BatteryId { get; set; }

		[DataMember]
		public string TestId { get; set; }

		[DataMember]
		public int SocPercent { get; set; }

		[DataMember]
		public DateTime CreatedAt { get; set; }

		[DataMember]
		public string Status { get; set; }

		[DataMember]
		public int TotalSamples { get; set; }

		public Battery()
		{
			CreatedAt = DateTime.UtcNow;
			Status = "Active";
		}

		public override string ToString()
		{
			return $"Battery {BatteryId} - Test {TestId} - SoC {SocPercent}%";
		}
	}
}
