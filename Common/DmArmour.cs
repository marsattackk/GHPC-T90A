using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace T90A.Common
{
	public sealed class DmArmour
	{
		public sealed class Entry
		{
			public string Piece, Mode, Name, Material = "", Xray = "", Source = "";
			public float Ke, Ce, KeAfter, CeAfter;
			public bool AngleMatters, Slat;

			public bool Variable => Mode == "geometry";
			public bool Era => Mode == "era";
			public bool Convex => Mode == "composite";
			public bool Drawn => Mode == "drawn";
		}

		public const string AssetName = "dm_armour";
		public static readonly string[] Modes = { "geometry", "nominal", "era", "composite", "drawn" };
		public static readonly string[] Materials =
		{
			"armour_steel",
			"mild_steel",
			"plain",
			"composite",
			"filling",
			"filling_steel",
			"liner"
		};

		public readonly List<Entry> Entries = new List<Entry>();
		readonly Dictionary<string, Entry> _byPiece = new Dictionary<string, Entry>();

		public Entry this[string piece] => piece != null && _byPiece.TryGetValue(piece, out var entry) ? entry : null;

		public void Add(Entry e)
		{
			Entries.Add(e);
			_byPiece[e.Piece] = e;
		}

		public bool Remove(string piece)
		{
			if (!_byPiece.TryGetValue(piece, out var entry)) return false;
			_byPiece.Remove(piece);
			return Entries.Remove(entry);
		}

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;
		static string F(float v) => v.ToString("R", CI);
		static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

		public string ToText()
		{
			var builder = new StringBuilder("# T90A.Common.DmArmour\n");
			foreach (var entry in Entries)
				builder.Append(string.Join("\t", Clean(entry.Piece), entry.Mode, F(entry.Ke), F(entry.Ce), F(entry.KeAfter), F(entry.CeAfter), entry.AngleMatters
					? "1"
					: "0",
					Clean(entry.Material), Clean(entry.Xray), Clean(entry.Name), Clean(entry.Source), entry.Slat ? "1" : "0")).Append('\n');
			return builder.ToString();
		}

		public static DmArmour Parse(string text)
		{
			var armour = new DmArmour();
			var lines = text.Split('\n');
			for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
			{
				var line = lines[lineIndex].TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				var fields = line.Split('\t');
				// older bundles stop at source, no slat column
				if (fields.Length < 11)
				{
					BundleText.Skip(AssetName, lineIndex + 1, line, $"{fields.Length} fields, want 11");
					continue;
				}
				try
				{
					armour.Add(new Entry
					{
						Piece = fields[0],
						Mode = fields[1],
						Ke = BundleText.Float(fields[2]),
						Ce = BundleText.Float(fields[3]),
						KeAfter = BundleText.Float(fields[4]),
						CeAfter = BundleText.Float(fields[5]),
						AngleMatters = fields[6] == "1",
						Material = fields[7],
						Xray = fields[8],
						Name = fields[9],
						Source = fields[10],
						Slat = fields.Length > 11 && fields[11] == "1",
					});
				}
				catch (System.FormatException exception) { BundleText.Skip(AssetName, lineIndex + 1, line, exception.Message); }
			}
			return armour;
		}

		public string Summary()
			=> $"{Entries.Count} pieces ({string.Join(", ", Entries.GroupBy(e => e.Mode).Select(m => $"{m.Key} {m.Count()}"))}" +
				$"{(Entries.Any(e => e.Slat) ? $"; {Entries.Count(x => x.Slat)} slat" : "")})";
	}
}
