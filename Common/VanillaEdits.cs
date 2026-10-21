using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace T90A.Common
{
	public sealed class VanillaEdits
	{
		public string[] Off = { };
		public (string piece, Matrix4x4 m)[] Transforms = { };
		public string[] Reshape = { };
		public (string bone, string group, Vector3 delta)[] Bones = { };

		public const string AssetName = "vanilla_edits", ReshapeKind = "reshape";

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		public static VanillaEdits Parse(string text)
		{
			var e = new VanillaEdits();
			var off = new List<string>(); var xf = new List<(string, Matrix4x4)>(); var rs = new List<string>(); var bones = new List<(string, string, Vector3)>();
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				var f = line.Split('|');
				switch (f[0])
				{
					case "off": off.Add(f[1]); break;
					case "reshape": rs.Add(f[1]); break;
					case "xform":
						var m = new Matrix4x4(); var a = f[2].Split(',');
						for (int i = 0; i < 16; i++) m[i] = float.Parse(a[i], CI);
						xf.Add((f[1], m)); break;
					case "bone":
						var d = f[3].Split(',');
						bones.Add((f[1], f[2], new Vector3(float.Parse(d[0], CI), float.Parse(d[1], CI), float.Parse(d[2], CI)))); break;
				}
			}
			e.Off = off.ToArray(); e.Transforms = xf.ToArray(); e.Reshape = rs.ToArray(); e.Bones = bones.ToArray();
			return e;
		}
	}
}
