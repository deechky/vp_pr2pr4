using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace Common
{
	[DataContract]
	public class EisMeta
	{
		[DataMember]
		public string BatteryId { get; set; }

		[DataMember]
		public string TestId { get; set; }

		[DataMember]
		public int SocPercent { get; set; }

		[DataMember]
		public string FileName { get; set; }

		[DataMember]
		public int TotalRows { get; set; }

		[DataMember]
		public DateTime StartedAt { get; set; }

		[DataMember]
		public double VThreshold { get; set; }

		[DataMember]
		public double ZThreshold { get; set; }

		[DataMember]
		public double DeviationPercent { get; set; }
	}

	[DataContract]
	public class Ack
	{
		[DataMember]
		public bool Success { get; set; }

		[DataMember]
		public string Message { get; set; }

		[DataMember]
		public string Status { get; set; }
	}

	[ServiceContract]
	public interface IBatteryService
	{
		[OperationContract]
		[FaultContract(typeof(DataFormatFault))]
		[FaultContract(typeof(ValidationFault))]
		Ack StartSession(EisMeta meta);

		[OperationContract]
		[FaultContract(typeof(DataFormatFault))]
		[FaultContract(typeof(ValidationFault))]
		Ack PushSample(EisSample sample);

		[OperationContract]
		[FaultContract(typeof(DataFormatFault))]
		[FaultContract(typeof(ValidationFault))]
		Ack EndSession();
	}

	[DataContract]
	public class DataFormatFault
	{
		[DataMember]
		public string Message { get; set; }

		[DataMember]
		public string Details { get; set; }
	}

	[DataContract]
	public class ValidationFault
	{
		[DataMember]
		public string Message { get; set; }

		[DataMember]
		public string Field { get; set; }

		[DataMember]
		public string Value { get; set; }
	}
}
