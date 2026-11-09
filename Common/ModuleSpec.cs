using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public sealed class ModuleSpec
	{
		public sealed class Hook { public string Name, Target; }

		public sealed class Box
		{
			public Vector3 Centre, Size; public float Yaw;
			public Quaternion Turn => Quaternion.AngleAxis(Yaw, Vector3.up);
			public Vector3 Corner(int i) { var h = Size / 2f; return Centre + Turn * new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z); }
		}

		public sealed class Armour { public float Ke, Ce; public bool AngleMatters, Ricochet, Normalizes, Shatter; }

		public sealed class Module
		{
			public string Id, Kind, Name, Group, Status;
			public List<Hook> Hooks = new List<Hook>();
			public List<Box> Boxes = new List<Box>();
			public float Health, Threshold, Pressure;
			public List<string> Replaces = new List<string>();
			public bool Existing;
			public Armour Armour;
			public Dictionary<string, object> Fuel;
			public int Tube;
			public Vector3[] XrayVerts;
			public int[] XrayTris;
			public bool NoXray;
			public List<(Vector3[] V, int[] T)> Hulls;
			public string XrayMaterial;
			public float[] XrayColour;
			public Bounds Bounds
			{
				get
				{
					if (Boxes == null || Boxes.Count == 0) return new Bounds();
					var b = new Bounds(Boxes[0].Corner(0), Vector3.zero);
					foreach (var x in Boxes) for (int i = 0; i < 8; i++) b.Encapsulate(x.Corner(i));
					return b;
				}
			}
		}

		public readonly List<Module> Modules = new List<Module>();
		public readonly Dictionary<string, string> PlatformNames = new Dictionary<string, string>();

		public static ModuleSpec Parse(string text, string source)
		{
			var root = MiniJson.Parse(text) as MiniJson.Obj ?? throw new FormatException(source + ": the top level must be an object");
			var s = new ModuleSpec();
			if (root.TryGetValue("platform_names", out var pn) && pn is MiniJson.Obj po)
				foreach (var kv in po) if (kv.Value is string v) s.PlatformNames[kv.Key] = v;
			foreach (var o in (List<object>)root["modules"])
			{
				var m = o as MiniJson.Obj ?? throw new FormatException(source + ": a module must be an object");
				var mod = new Module
				{
					Id = Str(m, "id", "?"), Kind = Str(m, "kind", "report_only"), Name = Str(m, "name", "unnamed module"), Group = Str(m, "group", "HULL"),
					Status = Str(m, "status", null),
					Health = Num(m, "health", 10f), Threshold = Num(m, "threshold", 0f), Pressure = Num(m, "pressure", 0.5f),
					Existing = m.TryGetValue("existing", out var e) && e is bool eb && eb,
					Fuel = m.TryGetValue("fuel", out var f) ? f as MiniJson.Obj : null,
				};
				foreach (var h in (List<object>)m["hooks"])
				{
					var ho = (MiniJson.Obj)h;
					mod.Hooks.Add(new Hook { Name = Str(ho, "hook", ""), Target = Str(ho, "target", null) });
				}
				foreach (var b in (List<object>)m["boxes"])
				{
					var bo = (MiniJson.Obj)b;
					mod.Boxes.Add(new Box { Centre = Vec(bo, "c"), Size = Vec(bo, "s"), Yaw = Num(m, "yaw", 0f) });
				}
				if (m.TryGetValue("armour", out var ao) && ao is MiniJson.Obj a)
					mod.Armour = new Armour { Ke = Num(a, "ke", 0f), Ce = Num(a, "ce", 0f), AngleMatters = Flag(a, "angle_matters"), Ricochet = Flag(a, "ricochet"), Normalizes = Flag(a, "normalizes"), Shatter = Flag(a, "shatter") };
				if (m.TryGetValue("replaces", out var r) && r is List<object> rl) mod.Replaces.AddRange(rl.OfType<string>());
				mod.Tube = (int)Num(m, "tube", 0f);
				if (m.TryGetValue("xray_material", out var xmo) && xmo is string xms) mod.XrayMaterial = xms;
				if (m.TryGetValue("xray_colour", out var xco) && xco is List<object> xcl && xcl.Count >= 3) mod.XrayColour = xcl.Select(x => (float)(double)x).ToArray();
				if (m.TryGetValue("xray", out var xo) && xo is bool drawn && !drawn) mod.NoXray = true;
				else if (xo is MiniJson.Obj xm)
				{
					var v = Floats(Str(xm, "v", ""));
					mod.XrayVerts = new Vector3[v.Length / 3];
					for (int i = 0; i < mod.XrayVerts.Length; i++) mod.XrayVerts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
					mod.XrayTris = Floats(Str(xm, "t", "")).Select(t => (int)t).ToArray();
					if (mod.XrayTris.Length % 3 != 0 || mod.XrayTris.Any(t => t < 0 || t >= mod.XrayVerts.Length))
						throw new FormatException($"{source}: module '{mod.Id}' has a broken x-ray mesh");
				}
				if (m.TryGetValue("hulls", out var hlo) && hlo is List<object> hl)
				{
					mod.Hulls = new List<(Vector3[], int[])>();
					foreach (var ho in hl.OfType<MiniJson.Obj>())
					{
						var v = Floats(Str(ho, "v", ""));
						var hv = new Vector3[v.Length / 3];
						for (int i = 0; i < hv.Length; i++) hv[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
						var ht = Floats(Str(ho, "t", "")).Select(t => (int)t).ToArray();
						if (hv.Length < 4 || ht.Length % 3 != 0 || ht.Any(t => t < 0 || t >= hv.Length))
							throw new FormatException($"{source}: module '{mod.Id}' has a broken hit-zone mesh");
						mod.Hulls.Add((hv, ht));
					}
				}
				s.Modules.Add(mod);
			}
			return s;
		}

		static string Str(MiniJson.Obj o, string key, string fallback) => o.TryGetValue(key, out var v) && v is string s ? s : fallback;
		static float[] Floats(string s) => s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
			.Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		static bool Flag(MiniJson.Obj o, string key) => o.TryGetValue(key, out var v) && v is bool b && b;
		static float Num(MiniJson.Obj o, string key, float fallback) => o.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;
		static Vector3 Vec(MiniJson.Obj o, string key)
		{
			var a = (List<object>)o[key];
			return new Vector3((float)(double)a[0], (float)(double)a[1], (float)(double)a[2]);
		}
	}
}
