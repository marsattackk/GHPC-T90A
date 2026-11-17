using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace T90A.Common
{
	// Which of my materials stand in for which vanilla ones (glass, mostly).
	public sealed class MaterialRoles
	{
		public sealed class Entry
		{
			public string Material, Role, Vanilla;
			public string[] Units = { };
		}

		public const string AssetName = "material_roles";
		public List<Entry> Entries = new List<Entry>();

		public IEnumerable<Entry> Applied => Entries.Where(e => !string.IsNullOrEmpty(e.Vanilla));

		public string ToText()
		{
			var builder = new StringBuilder("# material\trole\tvanilla material at spawn\tdonor units\n");
			foreach (var entry in Entries)
				builder
					.Append(entry.Material)
					.Append('\t')
					.Append(entry.Role)
					.Append('\t')
					.Append(string.IsNullOrEmpty(entry.Vanilla) ? "-" : entry.Vanilla)
					.Append('\t')
					.Append(string.Join(",", entry.Units))
					.Append('\n');
			return builder.ToString();
		}

		public static MaterialRoles Parse(string text)
		{
			var roles = new MaterialRoles();
			foreach (var line in text.Split('\n'))
			{
				if (line.Length == 0 || line[0] == '#') continue;
				var fields = line.TrimEnd('\r').Split('\t');
				if (fields.Length < 2) continue;
				roles.Entries.Add(new Entry
				{
					Material = fields[0],
					Role = fields[1],
					Vanilla = fields.Length > 2 && fields[2] != "-" ? fields[2] : null,
					Units = fields.Length > 3 && fields[3].Length > 0 ? fields[3].Split(',') : new string[0],
				});
			}
			return roles;
		}

		public string Summary()
			=> $"{Entries.Count} materials in {Entries.Select(e => e.Role).Distinct().Count()} roles; at spawn: " +
				(Applied.Any()
					? string.Join(
						", ",
						Applied.GroupBy(e => e.Vanilla)
							.Select(g => $"'{g.Key}' on {string.Join(" / ", g.Select(e => e.Material))}"))
					: "none");
	}
}
