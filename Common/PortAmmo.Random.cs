using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace T90A.Common
{
	internal static class PortAmmoPool
	{
		internal static List<KeyValuePair<string, double>> Parse(
			string pool,
			IList<string> ids,
			out string warning,
			string kind = "APFSDS")
		{
			warning = null;
			if (string.IsNullOrWhiteSpace(pool)) return null;
			var result = new List<KeyValuePair<string, double>>();
			foreach (string part in pool.Split(','))
			{
				string text = part.Trim();
				string problem = null;
				string id = null;
				double weight = 1;
				if (text.Length == 0) problem = "has an empty entry";
				else
				{
					int colon = text.IndexOf(':');
					string name = (colon < 0 ? text : text.Substring(0, colon)).Trim();
					string given = colon < 0 ? "" : text.Substring(colon + 1).Trim();
					id = ids.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
					if (id == null) problem = $"names '{name}', which is not one of the {kind} rounds";
					else if (colon >= 0 && !ParseWeight(given, out weight))
						problem = $"gives '{given}' as the weight of {id}, which is not a number above 0";
					else if (result.Any(e => e.Key == id)) problem = $"names {id} twice";
				}
				if (problem != null)
				{
					warning = $"{kind} pool '{pool.Trim()}' {problem} (rounds: {string.Join(", ", ids)}); "
						+ $"rolling from every {kind} at its campaign weight.";
					return null;
				}
				result.Add(new KeyValuePair<string, double>(id, weight));
			}
			return result;
		}

		static bool ParseWeight(string text, out double weight)
		{
			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out weight)) return false;
			return !double.IsNaN(weight) && !double.IsInfinity(weight) && weight > 0;
		}

		internal static List<KeyValuePair<string, double>> Default(IList<string> ids, IList<float> prevalences)
		{
			bool useWeights = prevalences.Sum() > 0;
			var result = new List<KeyValuePair<string, double>>();
			for (int i = 0; i < ids.Count; i++)
				result.Add(new KeyValuePair<string, double>(ids[i], useWeights ? Math.Max(0f, prevalences[i]) : 1));
			return result;
		}

		internal static string Pick(IList<KeyValuePair<string, double>> pool, Random random)
		{
			double sum = pool.Sum(e => e.Value);
			double roll = random.NextDouble() * sum;
			foreach (var entry in pool)
			{
				roll -= entry.Value;
				if (roll < 0) return entry.Key;
			}
			return pool.Last(e => e.Value > 0).Key;
		}

		internal static string Describe(IList<KeyValuePair<string, double>> pool)
			=> string.Join(", ", pool.Select(e => e.Key + ":" + e.Value.ToString("0.##", CultureInfo.InvariantCulture)));
	}
}
