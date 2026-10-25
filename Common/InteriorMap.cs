using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Utility;
using MelonLoader;
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

		internal sealed class Copy
		{
			public string Template, Name, ChargeTemplate, ChargeName;
			public int RackIndex;
		}

		internal sealed class Shape
		{
			public string Path;
			public Vector3[] Vertices;
			public int[] Triangles;
		}

		internal sealed class NewShape
		{
			public string Parent, Name, Template;
			public Vector3[] Vertices;
			public int[] Triangles;
		}

		internal readonly List<Move> Moves = new List<Move>();
		internal readonly List<Copy> Copies = new List<Copy>();
		internal readonly List<Shape> Shapes = new List<Shape>();
		internal readonly List<NewShape> News = new List<NewShape>();
		static readonly FieldInfo ArmourName = Reflect.Field(typeof(UniformArmor), "_name");

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		internal sealed class Embedded
		{
			const string Resource = "interior_map.txt";
			readonly Assembly _asm;
			readonly string _tag, _base;
			InteriorMap _map;
			bool _read;

			internal Embedded(Assembly asm, string tag, string mod, string baseName) { _asm = asm; _tag = tag; _base = baseName; }

			internal InteriorMap Map
			{
				get
				{
					if (_read) return _map;
					_read = true;
					using (var s = _asm.GetManifestResourceStream(Resource))
					{
						if (s == null) { MelonLogger.Warning($"{_tag} interior: data missing from the DLL; crew and ammo stay at the {_base}'s places."); return null; }
						using (var r = new System.IO.StreamReader(s)) _map = Parse(r.ReadToEnd());
					}
					return _map;
				}
			}

			internal void Apply(GHPC.Vehicle.Vehicle vehicle)
			{
				var map = Map;
				if (map == null) return;
				var problems = new List<string>();
				map.Apply(vehicle.transform, problems);
				if (problems.Count > 0) MelonLogger.Warning($"{_tag} interior: " + string.Join("; ", problems));
			}
		}

		internal static InteriorMap Parse(string text)
		{
			var map = new InteriorMap();
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				var f = line.Split('|');
				if (f[0] == "copy" && f.Length >= 6)
				{
					map.Copies.Add(new Copy { Template = f[1], Name = f[2], ChargeTemplate = f[3] == "-" ? null : f[3], ChargeName = f[4] == "-" ? null : f[4], RackIndex = int.Parse(f[5], CI) });
					continue;
				}
				if (f[0] == "mesh" && f.Length >= 4)
				{
					map.Shapes.Add(new Shape { Path = f[1], Vertices = Verts(f[2]), Triangles = f[3].Split(',').Select(x => int.Parse(x, CI)).ToArray() });
					continue;
				}
				if (f[0] == "newmesh" && f.Length >= 6)
				{
					map.News.Add(new NewShape { Parent = f[1], Name = f[2], Template = f[3], Vertices = Verts(f[4]), Triangles = f[5].Split(',').Select(x => int.Parse(x, CI)).ToArray() });
					continue;
				}
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

		static Vector3[] Verts(string s)
		{
			var v = s.Split(',').Select(x => float.Parse(x, CI)).ToArray();
			var verts = new Vector3[v.Length / 3];
			for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
			return verts;
		}

		static Mesh MeshOf(string name, Vector3[] verts, int[] tris)
		{
			var mesh = new Mesh { name = name };
			if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			mesh.vertices = verts; mesh.triangles = tris;
			mesh.RecalculateNormals(); mesh.RecalculateBounds();
			return mesh;
		}

		static Quaternion Q(string s) { var a = s.Split(','); return new Quaternion(float.Parse(a[0], CI), float.Parse(a[1], CI), float.Parse(a[2], CI), float.Parse(a[3], CI)); }

		internal void Apply(Transform root, List<string> problems = null)
		{
			var grown = new HashSet<GHPC.Weapons.AmmoRack>();
			foreach (var c in Copies)
			{
				var t = Find(root, c.Template);
				var ph = t != null ? t.GetComponent<GHPC.Weapons.AmmoVisualPlaceholder>() : null;
				var rack = t != null ? t.GetComponentInParent<GHPC.Weapons.AmmoRack>() : null;
				if (ph == null || rack == null) { problems?.Add($"'{c.Template}' (slot to copy) not found"); continue; }
				if (t.parent.Find(c.Name) != null) continue;
				var copy = Object.Instantiate(t.gameObject, t.parent, false);
				copy.name = c.Name;
				copy.transform.localPosition = t.localPosition; copy.transform.localRotation = t.localRotation; copy.transform.localScale = t.localScale;
				var cph = copy.GetComponent<GHPC.Weapons.AmmoVisualPlaceholder>();
				cph.RackIndex = c.RackIndex;
				cph.PairedChargeSlot = null;
				if (c.ChargeTemplate != null)
				{
					var ct = Find(root, c.ChargeTemplate);
					if (ct == null) problems?.Add($"'{c.ChargeTemplate}' (charge slot to copy) not found; '{c.Name}' has no charge");
					else
					{
						var cc = Object.Instantiate(ct.gameObject, ct.parent, false);
						cc.name = c.ChargeName;
						cc.transform.localPosition = ct.localPosition; cc.transform.localRotation = ct.localRotation; cc.transform.localScale = ct.localScale;
						foreach (var r in cc.GetComponents<MeshRenderer>()) Object.Destroy(r);
						foreach (var mf in cc.GetComponents<MeshFilter>()) Object.Destroy(mf);
						cph.PairedChargeSlot = cc.transform;
					}
				}
				grown.Add(rack);
			}
			foreach (var r in grown) r.SetUpVisualSlots();
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
			foreach (var s in Shapes)
			{
				var t = Find(root, s.Path);
				if (t == null) { problems?.Add($"'{s.Path}' (reshaped holder) not found"); continue; }
				var mesh = MeshOf(t.name + " (reshaped)", s.Vertices, s.Triangles);
				var mf = t.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = mesh;
				var mc = t.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = mesh;
			}
			foreach (var n in News)
			{
				var parent = Find(root, n.Parent); var tpl = Find(root, n.Template);
				if (parent == null) { problems?.Add($"'{n.Parent}' (parent of new holder '{n.Name}') not found"); continue; }
				if (tpl == null) { problems?.Add($"'{n.Template}' (template of new holder '{n.Name}') not found"); continue; }
				if (parent.Find(n.Name) != null) continue;
				var go = Object.Instantiate(tpl.gameObject, parent, false);
				go.name = n.Name;
				if (!go.activeSelf)
				{
					go.SetActive(true);
				}
				var kids = new List<GameObject>(); foreach (Transform ch in go.transform) kids.Add(ch.gameObject);
				foreach (var k in kids) Object.Destroy(k);
				go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
				var mesh = MeshOf(n.Name, n.Vertices, n.Triangles);
				var mf = go.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = mesh;
				var mc = go.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = mesh;
				foreach (var ua in go.GetComponents<UniformArmor>()) ArmourName?.SetValue(ua, n.Name.ToLowerInvariant());
				GHPC.PhysicsHelpers.RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
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
