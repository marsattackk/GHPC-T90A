using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GHPC.Utility;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class InteriorMap
	{
		internal sealed class Move
		{
			public string Path, Kind;
			public Vector3 Local, From;
			public Quaternion? Rot;
			public Vector3? Scale;
			public string Parent;
			public string CurrentPath => Parent == null ? Path : Parent + "/" + Path.Substring(Path.LastIndexOf('/') + 1);
		}

		internal readonly List<Move> Moves = new List<Move>();

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		internal static InteriorMap Parse(string text)
		{
			var map = new InteriorMap();
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				var f = line.Split('|');
				if (f[0] != "move" || f.Length < 8) continue;
				map.Moves.Add(new Move
				{
					Path = f[1], Kind = f[2], Local = V3(f[4]), Rot = f[5] == "-" ? (Quaternion?)null : Q(f[5]), From = V3(f[6]),
					Scale = f.Length > 9 && f[9] != "-" ? V3(f[9]) : (Vector3?)null,
					Parent = f.Length > 10 && f[10] != "-" ? f[10] : null,
				});
			}
			return map;
		}

		static Vector3 V3(string s) { var a = s.Split(','); return new Vector3(float.Parse(a[0], CI), float.Parse(a[1], CI), float.Parse(a[2], CI)); }

		static Quaternion Q(string s) { var a = s.Split(','); return new Quaternion(float.Parse(a[0], CI), float.Parse(a[1], CI), float.Parse(a[2], CI), float.Parse(a[3], CI)); }

		internal string Apply(Transform root, List<string> problems = null)
		{
			var counts = new Dictionary<string, int>();
			var before = new List<string>();
			float largest = 0f; string largestName = ""; int reparented = 0;
			var missing = new List<string>();
			foreach (var m in Moves)
			{
				var t = Find(root, m.Path) ?? (m.Parent != null ? Find(root, m.CurrentPath) : null);
				if (t == null) { missing.Add(m.Path); problems?.Add($"'{m.Path}' not found"); continue; }
				Transform parent = null;
				if (m.Parent != null && (parent = Find(root, m.Parent)) == null) { missing.Add(m.Parent); problems?.Add($"'{m.Parent}' (new parent of '{m.Path}') not found"); continue; }
				float pre = parent == null || t.parent != parent ? (t.localPosition - m.From).magnitude : 0f;
				if (pre > 0.001f) before.Add($"{t.name} {pre * 100f:0.0} cm");
				var was = t.position;
				if (parent != null && t.parent != parent) { t.SetParent(parent, false); reparented++; }
				t.localPosition = m.Local;
				if (m.Rot.HasValue) t.localRotation = m.Rot.Value;
				if (m.Scale.HasValue) t.localScale = m.Scale.Value;
				float d = (t.position - was).magnitude;
				if (d > largest) { largest = d; largestName = t.name; }
				counts[m.Kind] = counts.TryGetValue(m.Kind, out var n) ? n + 1 : 1;
			}
			return $"{Moves.Count - missing.Count} of {Moves.Count} moved ({string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}"))}), largest {largest * 100f:0.0} cm ({largestName})"
				+ (reparented > 0 ? $"; {reparented} re-parented into another rig group" : "")
				+ (before.Count > 0 ? $"; already moved before (now the map's pose): {string.Join(", ", before)}" : "")
				+ (missing.Count > 0 ? $"; {missing.Count} NOT FOUND: {string.Join(", ", missing)}" : "");
		}

		internal static Transform Find(Transform root, string path)
		{
			var t = root.Find(path);
			if (t != null) return t;
			int i = path.IndexOf('/');
			string top = i < 0 ? path : path.Substring(0, i), rest = i < 0 ? null : path.Substring(i + 1);
			var groups = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true));
			foreach (var lt in root.GetComponentsInChildren<LateFollowTarget>(true))
				foreach (var f in lt.LateFollowers) if (f != null) groups.Add(f);
			foreach (var g in groups)
				if (g.name == top) { var hit = rest == null ? g.transform : g.transform.Find(rest); if (hit != null) return hit; }
			return null;
		}
	}
}
