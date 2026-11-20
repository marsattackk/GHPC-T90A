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
			public Vector3 Centre, Size;
			public float Yaw;
			public Quaternion Turn => Quaternion.AngleAxis(Yaw, Vector3.up);
			public bool Contains(Vector3 p)
			{
				var localPoint = Quaternion.Inverse(Turn) * (p - Centre);
				var half = Size / 2f;
				return Mathf.Abs(localPoint.x) <= half.x && Mathf.Abs(localPoint.y) <= half.y && Mathf.Abs(localPoint.z) <= half.z;
			}

			public Vector3 Corner(int i)
			{
				var half = Size / 2f;
				return Centre
					+ Turn * new Vector3((i & 1) != 0 ? half.x : -half.x, (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z);
			}
		}

		public sealed class Armour
		{
			public float Ke, Ce;
			public bool AngleMatters, Ricochet, Normalizes, Shatter;
		}

		public sealed class Module
		{
			public string Id, Kind, Name, Group, Status;
			public string Kit;
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
			public bool Contains(Vector3 p)
			{
				if (Hulls == null) return Boxes.Any(b => b.Contains(p));
				foreach (var (vertices, triangles) in Hulls)
				{
					var centre = Vector3.zero;
					foreach (var vertex in vertices) centre += vertex;
					centre /= vertices.Length;
					bool inside = true;
					for (int i = 0; i < triangles.Length && inside; i += 3)
					{
						var normal = Vector3.Cross(
							vertices[triangles[i + 1]] - vertices[triangles[i]],
							vertices[triangles[i + 2]] - vertices[triangles[i]]);
						if (normal.sqrMagnitude < 1e-14f) continue;
						if (Vector3.Dot(normal, centre - vertices[triangles[i]]) > 0f) normal = -normal;
						if (Vector3.Dot(normal, p - vertices[triangles[i]]) > 1e-6f) inside = false;
					}
					if (inside) return true;
				}
				return false;
			}

			public string XrayMaterial;
			public float[] XrayColour;
			public Bounds Bounds
			{
				get
				{
					if (Boxes == null || Boxes.Count == 0) return new Bounds();
					var bounds = new Bounds(Boxes[0].Corner(0), Vector3.zero);
					foreach (var box in Boxes) for (int i = 0; i < 8; i++) bounds.Encapsulate(box.Corner(i));
					return bounds;
				}
			}
		}

		public readonly List<Module> Modules = new List<Module>();
		public readonly Dictionary<string, string> PlatformNames = new Dictionary<string, string>();

		public static ModuleSpec Parse(string text, string source)
		{
			var root = MiniJson.Parse(text) as MiniJson.Obj ?? throw new FormatException(source + ": the top level must be an object");
			var spec = new ModuleSpec();
			if (root.TryGetValue("platform_names", out var namesValue) && namesValue is MiniJson.Obj names)
				foreach (var entry in names) if (entry.Value is string platformName) spec.PlatformNames[entry.Key] = platformName;
			foreach (var moduleValue in (List<object>)root["modules"])
			{
				var moduleJson = moduleValue as MiniJson.Obj ?? throw new FormatException(source + ": a module must be an object");
				var mod = new Module
				{
					Id = Str(moduleJson, "id", "?"),
					Kind = Str(moduleJson, "kind", "report_only"),
					Name = Str(moduleJson, "name", "unnamed module"),
					Group = Str(moduleJson, "group", "HULL"),
					Status = Str(moduleJson, "status", null),
					Kit = Str(moduleJson, "kit", null),
					Health = Num(moduleJson, "health", 10f),
					Threshold = Num(moduleJson, "threshold", 0f),
					Pressure = Num(moduleJson, "pressure", 0.5f),
					Existing = moduleJson.TryGetValue("existing", out var existingValue) && existingValue is bool existing && existing,
					Fuel = moduleJson.TryGetValue("fuel", out var fuelValue) ? fuelValue as MiniJson.Obj : null,
				};
				foreach (var hookValue in (List<object>)moduleJson["hooks"])
				{
					var hookJson = (MiniJson.Obj)hookValue;
					mod.Hooks.Add(new Hook { Name = Str(hookJson, "hook", ""), Target = Str(hookJson, "target", null) });
				}
				foreach (var boxValue in (List<object>)moduleJson["boxes"])
				{
					var boxJson = (MiniJson.Obj)boxValue;
					mod.Boxes.Add(new Box { Centre = Vec(boxJson, "c"), Size = Vec(boxJson, "s"), Yaw = Num(moduleJson, "yaw", 0f) });
				}
				if (moduleJson.TryGetValue("armour", out var armourValue) && armourValue is MiniJson.Obj armourJson)
					mod.Armour = new Armour
					{
						Ke = Num(armourJson, "ke", 0f),
						Ce = Num(armourJson, "ce", 0f),
						AngleMatters = Flag(armourJson, "angle_matters"),
						Ricochet = Flag(armourJson, "ricochet"),
						Normalizes = Flag(armourJson, "normalizes"),
						Shatter = Flag(armourJson, "shatter")
					};
				if (moduleJson.TryGetValue("replaces", out var replacesValue) && replacesValue is List<object> replaces)
					mod.Replaces.AddRange(replaces.OfType<string>());
				mod.Tube = (int)Num(moduleJson, "tube", 0f);
				if (moduleJson.TryGetValue("xray_material", out var xmo) && xmo is string xms) mod.XrayMaterial = xms;
				if (moduleJson.TryGetValue("xray_colour", out var xco) && xco is List<object> xcl && xcl.Count >= 3)
					mod.XrayColour = xcl.Select(c => (float)(double)c).ToArray();
				if (moduleJson.TryGetValue("xray", out var xrayValue) && xrayValue is bool drawn && !drawn) mod.NoXray = true;
				else if (xrayValue is MiniJson.Obj xrayJson)
				{
					var coords = Floats(Str(xrayJson, "v", ""));
					mod.XrayVerts = new Vector3[coords.Length / 3];
					for (int i = 0;
						i < mod.XrayVerts.Length;
						i++) mod.XrayVerts[i] = new Vector3(coords[3 * i], coords[3 * i + 1], coords[3 * i + 2]);
					mod.XrayTris = Floats(Str(xrayJson, "t", "")).Select(n => (int)n).ToArray();
					if (mod.XrayTris.Length % 3 != 0 || mod.XrayTris.Any(i => i < 0 || i >= mod.XrayVerts.Length))
						throw new FormatException($"{source}: module '{mod.Id}' has a broken x-ray mesh");
				}
				if (moduleJson.TryGetValue("hulls", out var hlo) && hlo is List<object> hullList)
				{
					mod.Hulls = new List<(Vector3[], int[])>();
					foreach (var hullJson in hullList.OfType<MiniJson.Obj>())
					{
						var coords = Floats(Str(hullJson, "v", ""));
						var hullVertices = new Vector3[coords.Length / 3];
						for (int i = 0;
							i < hullVertices.Length;
							i++) hullVertices[i] = new Vector3(coords[3 * i], coords[3 * i + 1], coords[3 * i + 2]);
						var hullTriangles = Floats(Str(hullJson, "t", "")).Select(n => (int)n).ToArray();
						if (hullVertices.Length < 4
							|| hullTriangles.Length % 3 != 0
							|| hullTriangles.Any(i => i < 0 || i >= hullVertices.Length))
							throw new FormatException($"{source}: module '{mod.Id}' has a broken hit-zone mesh");
						mod.Hulls.Add((hullVertices, hullTriangles));
					}
				}
				spec.Modules.Add(mod);
			}
			return spec;
		}

		static string Str(MiniJson.Obj o, string key, string fallback)
			=> o.TryGetValue(key, out var found) && found is string text ? text : fallback;
		static float[] Floats(string s) => s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
			.Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		static bool Flag(MiniJson.Obj o, string key) => o.TryGetValue(key, out var found) && found is bool flag && flag;
		static float Num(MiniJson.Obj o, string key, float fallback)
			=> o.TryGetValue(key, out var found) && found is double number ? (float)number : fallback;
		static Vector3 Vec(MiniJson.Obj o, string key)
		{
			var list = (List<object>)o[key];
			return new Vector3((float)(double)list[0], (float)(double)list[1], (float)(double)list[2]);
		}
	}
}
