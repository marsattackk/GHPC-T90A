using System;
using System.Globalization;

namespace T90A.Common
{
	// A bad line in a text asset is skipped with a warning naming the asset and the line, so a typo can't stop the mod
	// loading.
	public static class BundleText
	{
		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		public static Action<string> Warn = Log;

		static void Log(string message)
		{
			MelonLoader.MelonLogger.Warning(message);
		}

		public static void Skip(string asset, int lineNo, string line, string why)
		{
			if (line.Length > 120) line = line.Substring(0, 120) + " (cut)";
			Warn?.Invoke($"'{asset}' line {lineNo} skipped ({why}): {line}");
		}

		public static float Float(string s) =>
			float.TryParse(s, NumberStyles.Float, CI, out var number) ? number : throw new FormatException($"bad number '{s}'");

		public static float[] Floats(string s, int n)
		{
			var parts = s.Split(',');
			if (parts.Length != n) throw new FormatException($"want {n} numbers, got {parts.Length} in '{s}'");
			var numbers = new float[n];
			for (int i = 0; i < n; i++) numbers[i] = Float(parts[i]);
			return numbers;
		}
	}
}
