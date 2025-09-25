using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;

namespace Common
{
	[DataContract]
	public class EisSample
	{
		[DataMember]
		public double FrequencyHz { get; set; }

		[DataMember]
		public double R_ohm { get; set; }

		[DataMember]
		public double X_ohm { get; set; }

		[DataMember]
		public double V { get; set; }

		[DataMember]
		public double T_degC { get; set; }

		[DataMember]
		public double Range_ohm { get; set; }

		[DataMember]
		public int RowIndex { get; set; }

		// Dodato za analitiku
		[DataMember]
		public DateTime Timestamp { get; set; }

		// Metoda za računanje impedanse Z = sqrt(R^2 + X^2)
		public double CalculateImpedance()
		{
			return Math.Sqrt(R_ohm * R_ohm + X_ohm * X_ohm);
		}

		public static bool TryParseCsv(string csvLine, int expectedRowIndex, out EisSample sample, out string error)
		{
			sample = null;
			error = string.Empty;
			
			if (string.IsNullOrWhiteSpace(csvLine))
			{
				error = "Empty line";
				return false;
			}

			// Split tolerantno: ",", ";" ili tab, i ukloni navodnike
			string cleaned = csvLine.Replace("\"", "");
			string[] parts = cleaned.Split(new[] { ',', ';', '\t' }, StringSplitOptions.None);

			var ci = CultureInfo.InvariantCulture;

			if (parts.Length < 6)
			{
				error = $"Expected at least 6 columns, found {parts.Length}";
				return false;
			}

			try
			{
				// Parsing EIS data format: FrequencyHz, R_ohm, X_ohm, V, T_degC, Range_ohm
				// Also supports Hioki format: Frequency(Hz), R(ohm), X(ohm), V(V), T(deg C), Range(Ohm)
				double frequencyHz = double.Parse(parts[0], NumberStyles.Float, ci);
				double r_ohm = double.Parse(parts[1], NumberStyles.Float, ci);
				double x_ohm = double.Parse(parts[2], NumberStyles.Float, ci);
				double v = double.Parse(parts[3], NumberStyles.Float, ci);
				double t_degC = double.Parse(parts[4], NumberStyles.Float, ci);
				double range_ohm = double.Parse(parts[5], NumberStyles.Float, ci);

				// Validacija
				if (frequencyHz <= 0)
				{
					error = $"FrequencyHz must be positive, got {frequencyHz}";
					return false;
				}

				if (double.IsNaN(r_ohm) || double.IsInfinity(r_ohm))
				{
					error = $"Invalid R_ohm value: {r_ohm}";
					return false;
				}

				if (double.IsNaN(x_ohm) || double.IsInfinity(x_ohm))
				{
					error = $"Invalid X_ohm value: {x_ohm}";
					return false;
				}

				if (double.IsNaN(v) || double.IsInfinity(v))
				{
					error = $"Invalid V value: {v}";
					return false;
				}

				sample = new EisSample
				{
					FrequencyHz = frequencyHz,
					R_ohm = r_ohm,
					X_ohm = x_ohm,
					V = v,
					T_degC = t_degC,
					Range_ohm = range_ohm,
					RowIndex = expectedRowIndex,
					Timestamp = DateTime.UtcNow
				};

				return true;
			}
			catch (Exception ex)
			{
				error = $"Parsing error: {ex.Message}";
				return false;
			}
		}
	}
}
