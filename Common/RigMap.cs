using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public sealed class RigMap
	{
		public sealed class Hatch
		{
			public string Role, Group, BaseBone;
			public Vector3 Hinge, Axis, Lift;
			public float Angle;
		}

		public sealed class Gear
		{
			public string Group, Bone;
			public Quaternion RestRot = Quaternion.identity;
		}

		public sealed class Whip
		{
			public string Group;
			public Vector3 Base, Tip;
			public float Rigid;
		}

		public sealed class Mount
		{
		}

		public sealed class Flap
		{
			public string Group;
			public Vector3 A, B;
			public float Limit, Taper, Ramp;
		}

		public string RunningGear = "base";
		public Hatch[] Hatches = { };
		public Gear[] GearParts = { };
		public Whip[] Whips = { };
		public Flap[] Flaps = { };

		public const string AssetName = "rig_map";
		public const float LiftShare = 0.3f;

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		public static RigMap Parse(string text)
		{
			var m = new RigMap();
			var hs = new List<Hatch>(); var gs = new List<Gear>(); var ws = new List<Whip>(); var fs = new List<Flap>();
			float[] Fs(string s) => s.Split(',').Select(x => float.Parse(x, CI)).ToArray();
			Vector3 Vec(string s) { var a = Fs(s); return new Vector3(a[0], a[1], a[2]); }
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0 || line[0] == '#') continue;
				var p = line.Split('|');
				if (p[0] == "gear" && p.Length > 1) m.RunningGear = p[1];
				else if (!(p[0] == "axes" && p.Length >= 5)) if (p[0] == "hatch" && p.Length >= 9)
					hs.Add(new Hatch { Role = p[2], Group = p[3], BaseBone = p[4], Hinge = Vec(p[5]), Axis = Vec(p[6]), Angle = float.Parse(p[7], CI), Lift = Vec(p[8]) });
				else if (p[0] == "part" && p.Length >= 5)
				{
					var q = Fs(p[4]);
					gs.Add(new Gear { Group = p[2], Bone = p[3], RestRot = new Quaternion(q[0], q[1], q[2], q[3]) });
				}
				else if (p[0] == "whip" && p.Length >= 6)
					ws.Add(new Whip { Group = p[2], Base = Vec(p[3]), Tip = Vec(p[4]), Rigid = float.Parse(p[5], CI) });
				else if (p[0] == "mount" && p.Length >= 6)
					new Mount { };
				else if (p[0] == "flap" && p.Length >= 9)
					fs.Add(new Flap { Group = p[2], A = Vec(p[3]), B = Vec(p[4]), Limit = float.Parse(p[6], CI), Taper = float.Parse(p[7], CI), Ramp = float.Parse(p[8], CI) });
			}
			m.Hatches = hs.ToArray(); m.GearParts = gs.ToArray(); m.Whips = ws.ToArray(); m.Flaps = fs.ToArray();
			return m;
		}
	}
}
