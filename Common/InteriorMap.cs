using System.Collections.Generic;
using System.Globalization;
using GHPC.Utility;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class InteriorMap
	{
		internal sealed class Move
		{
			public string Path;
			public Vector3 Local;
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
					Path = f[1], Local = V3(f[4]), Rot = f[5] == "-" ? (Quaternion?)null : Q(f[5]),
					Scale = f.Length > 9 && f[9] != "-" ? V3(f[9]) : (Vector3?)null,
					Parent = f.Length > 10 && f[10] != "-" ? f[10] : null,
				});
			}
			return map;
		}

		static Vector3 V3(string s) { var a = s.Split(','); return new Vector3(float.Parse(a[0], CI), float.Parse(a[1], CI), float.Parse(a[2], CI)); }

		static Quaternion Q(string s) { var a = s.Split(','); return new Quaternion(float.Parse(a[0], CI), float.Parse(a[1], CI), float.Parse(a[2], CI), float.Parse(a[3], CI)); }

		internal void Apply(Transform root, List<string> problems = null)
		{
			foreach (var m in Moves)
			{
				var t = Find(root, m.Path) ?? (m.Parent != null ? Find(root, m.CurrentPath) : null);
				if (t == null)
				{
					problems?.Add($"'{m.Path}' not found");
					continue;
				}
				Transform parent = null;
				if (m.Parent != null && (parent = Find(root, m.Parent)) == null)
				{
					problems?.Add($"'{m.Parent}' (new parent of '{m.Path}') not found");
					continue;
				}
				if (parent != null && t.parent != parent) t.SetParent(parent, false);
				t.localPosition = m.Local;
				if (m.Rot.HasValue) t.localRotation = m.Rot.Value;
				if (m.Scale.HasValue) t.localScale = m.Scale.Value;
			}
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
