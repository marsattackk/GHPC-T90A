using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace T90A.Common
{
	public sealed class RigMap
	{
		public sealed class Hatch
		{
			public string Wt, Role, Group, BaseBone;
			public Vector3 Hinge, Axis, Lift;
			public float Angle;
		}

		public sealed class Gear
		{
			public string Wt, Group, Bone;
			public Quaternion RestRot = Quaternion.identity;
		}

		public sealed class Whip
		{
			public string Wt, Group;
			public Vector3 Base, Tip;
			public float Rigid;
		}

		public sealed class Mount
		{
			public string Wt, Group, Rides = "";
			public Vector3 Pivot, Axis;
		}

		public sealed class Flap
		{
			public string Wt, Group;
			public Vector3 A, B, Out;
			public float Limit, Taper, Ramp;
			public float Lag;
		}

		public sealed class FollowPart
		{
			public string Wt, Group, Kind, Drive;
			public string Mate = "";
			public Vector3 Pivot, Target;
		}

		public string RunningGear = "base";
		public FollowPart[] FollowParts = { };
		public float WheelRestY;
		public Vector2 TrackUv;
		public float WheelRadius, RimGap;
		public float WheelRiseLimit;
		public Hatch[] Hatches = { };
		public Mount[] Mounts = { };
		public Gear[] GearParts = { };
		public Whip[] Whips = { };
		public Flap[] Flaps = { };
		public bool HasAxes;
		public Vector3 Turret, Gun, Muzzle;
		public float PitchDeg;

		public const string AssetName = "rig_map";
		public const float LiftShare = 0.3f;

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;
		static string F(float f) => f.ToString("R", CI);
		static string V(Vector3 v) => $"{F(v.x)},{F(v.y)},{F(v.z)}";

		public string ToText()
		{
			var builder = new StringBuilder("# T90A.Common.RigMap\n");
			builder.Append($"gear|{RunningGear}\n");
			if (HasAxes) builder.Append($"axes|{V(Turret)}|{V(Gun)}|{V(Muzzle)}|{F(PitchDeg)}\n");
			foreach (var hatch in Hatches)
				builder.Append($"hatch|{hatch.Wt}|{hatch.Role}|{hatch.Group}|{hatch.BaseBone}|{V(hatch.Hinge)}|{V(hatch.Axis)}|{F(hatch.Angle)}|{V(hatch.Lift)}\n");
			foreach (var gear in GearParts)
				builder.Append($"part|{gear.Wt}|{gear.Group}|{gear.Bone}|{F(gear.RestRot.x)},{F(gear.RestRot.y)},{F(gear.RestRot.z)},{F(gear.RestRot.w)}\n");
			if (FollowParts.Length > 0)
				builder.Append($"follow|{F(WheelRestY)}|{F(TrackUv.x)},{F(TrackUv.y)}|{F(WheelRadius)}|{F(RimGap)}|{F(WheelRiseLimit)}\n");
			foreach (var followPart in FollowParts)
				builder.Append($"fpart|{followPart.Wt}|{followPart.Group}|{followPart.Kind}|{followPart.Drive}|{V(followPart.Pivot)}|{V(followPart.Target)}|{followPart.Mate}\n");
			foreach (var whip in Whips) builder.Append($"whip|{whip.Wt}|{whip.Group}|{V(whip.Base)}|{V(whip.Tip)}|{F(whip.Rigid)}\n");
			foreach (var mount in Mounts)
				builder.Append($"mount|{mount.Wt}|{mount.Group}|{mount.Rides}|{V(mount.Pivot)}|{V(mount.Axis)}\n");
			foreach (var flap in Flaps)
				builder.Append($"flap|{flap.Wt}|{flap.Group}|{V(flap.A)}|{V(flap.B)}|{V(flap.Out)}|{F(flap.Limit)}|{F(flap.Taper)}|{F(flap.Ramp)}"
					+ (flap.Lag > 0f ? $"|{F(flap.Lag)}" : "")
					+ "\n");
			return builder.ToString();
		}

		static readonly Dictionary<string, int> MinFields = new Dictionary<string, int>
		{
			["gear"] = 2,
			["follow"] = 6,
			["fpart"] = 8,
			["axes"] = 5,
			["hatch"] = 9,
			["part"] = 5,
			["whip"] = 6,
			["mount"] = 6,
			["flap"] = 9,
		};

		public static RigMap Parse(string text)
		{
			var map = new RigMap();
			var followParts = new List<FollowPart>();
			var hatches = new List<Hatch>();
			var gears = new List<Gear>();
			var whips = new List<Whip>();
			var mounts = new List<Mount>();
			var flaps = new List<Flap>();
			float F(string numberText) => BundleText.Float(numberText);
			Vector3 Vec(string vectorText)
			{
				var numbers = BundleText.Floats(vectorText, 3);
				return new Vector3(numbers[0], numbers[1], numbers[2]);
			}
			var lines = text.Split('\n');
			for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
			{
				var line = lines[lineIndex].Trim();
				if (line.Length == 0 || line[0] == '#') continue;
				var parts = line.Split('|');
				if (!MinFields.TryGetValue(parts[0], out int want))
				{
					BundleText.Skip(AssetName, lineIndex + 1, line, $"unknown entry '{parts[0]}'");
					continue;
				}
				if (parts.Length < want)
				{
					BundleText.Skip(
						AssetName,
						lineIndex + 1,
						line,
						$"'{parts[0]}' wants {want - 1} field(s) after it, has {parts.Length - 1}");
					continue;
				}
				try
				{
					switch (parts[0])
					{
						case "gear":
							map.RunningGear = parts[1];
							break;
						case "follow":
							var uvs = BundleText.Floats(parts[2], 2);
							(map.WheelRestY, map.TrackUv, map.WheelRadius, map.RimGap, map.WheelRiseLimit) = (F(parts[1]), new Vector2(
								uvs[0],
								uvs[1]), F(parts[3]), F(parts[4]), F(parts[5]));
							break;
						case "fpart":
							followParts.Add(new FollowPart
							{
								Wt = parts[1],
								Group = parts[2],
								Kind = parts[3],
								Drive = parts[4],
								Pivot = Vec(parts[5]),
								Target = Vec(parts[6]),
								Mate = parts[7]
							});
							break;
						case "axes":
							(map.Turret, map.Gun, map.Muzzle, map.PitchDeg) = (Vec(parts[1]), Vec(parts[2]), Vec(parts[3]), F(parts[4]));
							map.HasAxes = true;
							break;
						case "hatch":
							hatches.Add(new Hatch
							{
								Wt = parts[1],
								Role = parts[2],
								Group = parts[3],
								BaseBone = parts[4],
								Hinge = Vec(parts[5]),
								Axis = Vec(parts[6]),
								Angle = F(parts[7]),
								Lift = Vec(parts[8])
							});
							break;
						case "part":
							var quaternion = BundleText.Floats(parts[4], 4);
							gears.Add(new Gear
							{
								Wt = parts[1],
								Group = parts[2],
								Bone = parts[3],
								RestRot = new Quaternion(quaternion[0], quaternion[1], quaternion[2], quaternion[3])
							});
							break;
						case "whip":
							whips.Add(new Whip
							{
								Wt = parts[1],
								Group = parts[2],
								Base = Vec(parts[3]),
								Tip = Vec(parts[4]),
								Rigid = F(parts[5])
							});
							break;
						case "mount":
							mounts.Add(new Mount
							{
								Wt = parts[1],
								Group = parts[2],
								Rides = parts[3],
								Pivot = Vec(parts[4]),
								Axis = Vec(parts[5])
							});
							break;
						case "flap":
							flaps.Add(new Flap
							{
								Wt = parts[1],
								Group = parts[2],
								A = Vec(parts[3]),
								B = Vec(parts[4]),
								Out = Vec(parts[5]),
								Limit = F(parts[6]),
								Taper = F(parts[7]),
								Ramp = F(parts[8]),
								Lag = parts.Length >= 10 ? F(parts[9]) : 0f
							});
							break;
					}
				}
				catch (System.FormatException exception) { BundleText.Skip(AssetName, lineIndex + 1, line, exception.Message); }
			}
			map.FollowParts = followParts.ToArray();
			map.Hatches = hatches.ToArray();
			map.GearParts = gears.ToArray();
			map.Whips = whips.ToArray();
			map.Mounts = mounts.ToArray();
			map.Flaps = flaps.ToArray();
			return map;
		}

		public string Summary()
			=> (HasAxes
				? $"axes turret {Turret}, gun {Gun}, muzzle {Muzzle}, pitch {PitchDeg:0.###} deg; "
				: "") + $"running gear '{RunningGear}', {Hatches.Length} hatches ({string.Join(", ", Hatches.Select(h => $"{h.Wt} {h.Role} {h.Angle:0.#} deg"))}), {GearParts.Length} running-gear parts"
				+ (Whips.Length > 0
					? $", {Whips.Length} whips ({string.Join(", ", Whips.Select(w => $"{w.Wt} {(w.Tip - w.Base).magnitude:0.00} m"))})"
					: "")
				+ (Mounts.Length > 0
					? $", {Mounts.Length} mounts ({string.Join(", ", Mounts.Select(m => $"{m.Wt} -> {m.Group}{(m.Axis != Vector3.zero ? " turning" : " riding " + (m.Rides.Length > 0 ? m.Rides : "its group"))}"))})"
					: "")
				+ (Flaps.Length > 0
					? $", {Flaps.Length} flaps ({string.Join(", ", Flaps.Select(f => $"{f.Group} {(f.B - f.A).magnitude:0.00} m, {f.Limit:0.#} deg"))})"
					: "");
	}
}
