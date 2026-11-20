using System;
using System.Collections.Generic;
using System.Linq;

namespace T90A.Common
{
	internal static class PortAmmoLoad
	{
		internal sealed class Row
		{
			public string Name;
			public int[] Counts;
			public int MissileSlots;
		}

		internal sealed class Result
		{
			public bool[] Emptied;
			public int[] Total;
			public List<string> Warnings = new List<string>();
			public string Choice = "Full";
		}

		internal const string FullName = "Full";

		internal static Result Resolve(
			IList<Row> rows,
			int[] whole,
			int missileColumn,
			string autoloader,
			IDictionary<string, string[]> presets,
			string preset,
			string emptyRacks,
			string tag)
		{
			var result = new Result { Emptied = new bool[rows.Count], Total = (int[])whole.Clone() };
			var names = new List<string>();
			string source;
			bool fromRacks = !string.IsNullOrWhiteSpace(emptyRacks);
			if (fromRacks)
			{
				source = "Empty racks";
				names.AddRange(emptyRacks.Split(',').Select(p => p.Trim()).Where(x => x.Length > 0));
			}
			else
			{
				source = "Ammunition load";
				string wanted = (preset ?? "").Trim();
				if (wanted.Length == 0 || wanted.Equals(FullName, StringComparison.OrdinalIgnoreCase)) return result;
				string key = presets?.Keys
					.FirstOrDefault(c => c.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase));
				if (key == null)
				{
					string valid = string.Join(", ", new[] { FullName }.Concat(presets?.Keys ?? Enumerable.Empty<string>()));
					result.Warnings.Add($"{tag}: Ammunition load '{wanted}' is not one of {valid}; the full load stays.");
					return result;
				}
				result.Choice = key;
				names.AddRange(presets[key] ?? new string[0]);
			}
			var valids = rows.Select(r => r.Name)
				.Where(n => !string.IsNullOrEmpty(n) && !Same(n, autoloader))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			bool ignoredAutoloader = false;
			var emptyNames = new List<string>();
			foreach (string name in names)
			{
				if (Same(name, autoloader))
				{
					ignoredAutoloader = true;
					continue;
				}
				string match = valids.FirstOrDefault(v => Same(v, name));
				if (match == null)
				{
					string rackList = string.Join(", ", valids);
					result.Warnings.Add($"{tag}: {source} names '{name}', which is not a rack ({rackList}); the full load stays.");
					result.Choice = FullName;
					return result;
				}
				if (!emptyNames.Contains(match, StringComparer.OrdinalIgnoreCase)) emptyNames.Add(match);
			}
			if (ignoredAutoloader) result.Warnings.Add($"{tag}: {source} can't empty the autoloader; ignored.");
			for (int i = 0; i < rows.Count; i++)
				result.Emptied[i] = rows[i].Name != null && emptyNames.Contains(rows[i].Name, StringComparer.OrdinalIgnoreCase);
			if (!result.Emptied.Any(e => e))
			{
				result.Choice = FullName;
				return result;
			}
			var emptiedRows = rows.Where((r, i) => result.Emptied[i]).ToList();
			var keptRows = rows.Where((r, i) => !result.Emptied[i]).ToList();
			if (emptiedRows.Any(r => r.Counts == null))
			{
				if (keptRows.Any(r => r.Counts == null))
				{
					result.Warnings.Add($"{tag}: {source} can't be used on this tank; the full load stays.");
					result.Emptied = new bool[rows.Count];
					result.Choice = FullName;
					return result;
				}
				var total = new int[whole.Length];
				foreach (var row in keptRows) Add(total, row, missileColumn, 1);
				result.Total = total;
			}
			else foreach (var row in emptiedRows) Add(result.Total, row, missileColumn, -1);
			if (fromRacks) result.Choice = "Empty racks: " + string.Join(", ", emptyNames);
			return result;
		}

		static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

		static void Add(int[] total, Row row, int missileColumn, int sign)
		{
			for (int i = 0; i < total.Length && row.Counts != null && i < row.Counts.Length; i++) total[i] += sign * row.Counts[i];
			if (missileColumn >= 0 && missileColumn < total.Length) total[missileColumn] += sign * row.MissileSlots;
		}
	}
}
