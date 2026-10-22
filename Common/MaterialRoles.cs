using System.Collections.Generic;
using System.Linq;

namespace T90A.Common
{
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

		public static MaterialRoles Parse(string text)
		{
			var m = new MaterialRoles();
			foreach (var line in text.Split('\n'))
			{
				if (line.Length == 0 || line[0] == '#') continue;
				var f = line.TrimEnd('\r').Split('\t');
				if (f.Length < 2) continue;
				m.Entries.Add(new Entry
				{
					Material = f[0], Role = f[1], Vanilla = f.Length > 2 && f[2] != "-" ? f[2] : null,
					Units = f.Length > 3 && f[3].Length > 0 ? f[3].Split(',') : new string[0],
				});
			}
			return m;
		}
	}
}
