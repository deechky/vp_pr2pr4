using System;

namespace Common
{
	public class EisFileInfo
	{
		public string BatteryId { get; set; }
		public string TestId { get; set; }
		public int SocPercent { get; set; }
		public string FilePath { get; set; }
		public string FileName { get; set; }

		public override string ToString()
		{
			return $"{BatteryId}/{TestId} - SoC {SocPercent}% - {FileName}";
		}
	}
}
