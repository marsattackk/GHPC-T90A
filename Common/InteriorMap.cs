using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class InteriorMap
	{
		internal sealed class Move
		{
			public string Path, Kind, Twin, Measure;
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
		internal readonly List<(string slot, string charge)> Drops = new List<(string, string)>();
		internal readonly List<string> Frozen = new List<string>();
		internal sealed class ChargeSlot
		{
			public string Slot, Name;
			public Vector3 Local;
			public Quaternion Rot;
		}

		internal readonly List<ChargeSlot> ChargeSlots = new List<ChargeSlot>();

		internal sealed class CrewCopy
		{
			public string Template, Name;
			public Vector3 Offset;
		}

		internal readonly List<CrewCopy> CrewCopies = new List<CrewCopy>();
		internal readonly List<Shape> Shapes = new List<Shape>();
		internal readonly List<NewShape> News = new List<NewShape>();
		static readonly FieldInfo ArmourName = Reflect.Field(typeof(UniformArmor), "_name");
		const float RackRha = 15f;
		internal string Header = "";

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		internal sealed class Embedded
		{
			const string Resource = "interior_map.txt";
			readonly Assembly _asm;
			readonly string _tag, _mod, _base;
			InteriorMap _map;
			bool _read;

			internal Embedded(Assembly asm, string tag, string mod, string baseName)
			{
				_asm = asm;
				_tag = tag;
				_mod = mod;
				_base = baseName;
			}

			internal InteriorMap Map
			{
				get
				{
					if (_read) return _map;
					_read = true;
					using (var stream = _asm.GetManifestResourceStream(Resource))
					{
						if (stream == null)
						{
							MelonLogger.Warning($"{_tag} interior: data missing from the DLL; crew and ammo stay at the {_base}'s places.");
							return null;
						}
						try { using (var reader = new System.IO.StreamReader(stream)) _map = Parse(reader.ReadToEnd()); }
						catch (System.Exception exception)
						{
							_map = null;
							MelonLogger.Warning($"{_tag} interior: the interior data in the DLL can't be read ({exception.GetType().Name}: {exception.Message}); crew and ammo stay at the {_base}'s places.");
						}
					}
					return _map;
				}
			}

			internal void Apply(GHPC.Vehicle.Vehicle vehicle)
			{
				var map = Map;
				if (map == null) return;
				var problems = new List<string>();
				string log = map.Apply(vehicle.transform, problems);
				if (problems.Count > 0) MelonLogger.Warning($"{_tag} interior: " + string.Join("; ", problems));
			}
		}

		internal static InteriorMap Parse(string text)
		{
			var map = new InteriorMap();
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.StartsWith("# T90A.Common.InteriorMap")) map.Header = line.Substring(2);
				if (line.Length == 0 || line.StartsWith("#")) continue;
				var fields = line.Split('|');
				if (fields[0] == "copy" && fields.Length >= 6)
				{
					map.Copies.Add(new Copy
					{
						Template = fields[1],
						Name = fields[2],
						ChargeTemplate = fields[3] == "-" ? null : fields[3],
						ChargeName = fields[4] == "-" ? null : fields[4],
						RackIndex = int.Parse(fields[5], CI)
					});
					continue;
				}
				if (fields[0] == "drop" && fields.Length >= 3)
				{
					map.Drops.Add((fields[1], fields[2] == "-" ? null : fields[2]));
					continue;
				}
				if (fields[0] == "freezecarousel" && fields.Length >= 2)
				{
					map.Frozen.Add(fields[1]);
					continue;
				}
				if (fields[0] == "chargeslot" && fields.Length >= 5)
				{
					map.ChargeSlots.Add(new ChargeSlot { Slot = fields[1], Name = fields[2], Local = V3(fields[3]), Rot = Q(fields[4]) });
					continue;
				}
				if (fields[0] == "crewcopy" && fields.Length >= 4)
				{
					map.CrewCopies.Add(new CrewCopy { Template = fields[1], Name = fields[2], Offset = V3(fields[3]) });
					continue;
				}
				if (fields[0] == "mesh" && fields.Length >= 4)
				{
					map.Shapes.Add(new Shape
					{
						Path = fields[1],
						Vertices = Verts(fields[2]),
						Triangles = fields[3].Split(',').Select(t => int.Parse(t, CI)).ToArray()
					});
					continue;
				}
				if (fields[0] == "newmesh" && fields.Length >= 6)
				{
					map.News.Add(new NewShape
					{
						Parent = fields[1],
						Name = fields[2],
						Template = fields[3],
						Vertices = Verts(fields[4]),
						Triangles = fields[5].Split(',').Select(t => int.Parse(t, CI)).ToArray()
					});
					continue;
				}
				if (fields[0] != "move" || fields.Length < 8) continue;
				map.Moves.Add(new Move
				{
					Path = fields[1],
					Kind = fields[2],
					Twin = fields[3],
					Local = V3(fields[4]),
					Rot = fields[5] == "-" ? (Quaternion?)null : Q(fields[5]),
					Measure = fields.Length > 8 ? fields[8] : "-",
					Scale = fields.Length > 9 && fields[9] != "-" ? V3(fields[9]) : (Vector3?)null,
					Parent = fields.Length > 10 && fields[10] != "-" ? fields[10] : null,
				});
			}
			return map;
		}

		static Vector3 V3(string s)
		{
			var parts = s.Split(',');
			return new Vector3(float.Parse(parts[0], CI), float.Parse(parts[1], CI), float.Parse(parts[2], CI));
		}

		static Vector3[] Verts(string s)
		{
			var numbers = s.Split(',').Select(t => float.Parse(t, CI)).ToArray();
			var verts = new Vector3[numbers.Length / 3];
			for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(numbers[3 * i], numbers[3 * i + 1], numbers[3 * i + 2]);
			return verts;
		}

		static Mesh MeshOf(string name, Vector3[] verts, int[] tris)
		{
			var mesh = new Mesh { name = name };
			if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			mesh.vertices = verts;
			mesh.triangles = tris;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		static Quaternion Q(string s)
		{
			var parts = s.Split(',');
			return new Quaternion(
				float.Parse(parts[0], CI),
				float.Parse(parts[1], CI),
				float.Parse(parts[2], CI),
				float.Parse(parts[3], CI));
		}

		// Run before LoadoutManager.Start: rounds spawn as their slot's child, so a moved slot gets its round in place.
		internal string Apply(Transform root, List<string> problems = null)
		{
			var grown = new HashSet<GHPC.Weapons.AmmoRack>();
			int dropped = 0;
			foreach (var (slotPath, chargePath) in Drops)
			{
				var slot = Find(root, slotPath);
				if (slot == null) continue;
				var rack = slot.GetComponentInParent<GHPC.Weapons.AmmoRack>();
				var placeholder = slot.GetComponent<GHPC.Weapons.AmmoVisualPlaceholder>();
				var chargeSlot = placeholder != null && placeholder.PairedChargeSlot != null
					? placeholder.PairedChargeSlot
					: chargePath != null ? Find(root, chargePath) : null;
				if (chargeSlot != null) Object.DestroyImmediate(chargeSlot.gameObject);
				Object.DestroyImmediate(slot.gameObject);
				if (rack != null) grown.Add(rack);
				dropped++;
			}
			int frozen = 0;
			foreach (var carouselPath in Frozen)
			{
				var carouselTransform = Find(root, carouselPath);
				var car = carouselTransform != null ? carouselTransform.GetComponent<GHPC.Weapons.AmmoCarousel>() : null;
				if (car == null)
				{
					problems?.Add($"'{carouselPath}' (carousel to fix) not found");
					continue;
				}
				var rotationTransform = car.RotationTransform != null ? car.RotationTransform : car.transform;
				if (rotationTransform.name.EndsWith(" (fixed stand-in)")) continue;
				var stand = new GameObject(rotationTransform.name + " (fixed stand-in)").transform;
				stand.SetParent(rotationTransform.parent, false);
				stand.localPosition = rotationTransform.localPosition;
				stand.localRotation = rotationTransform.localRotation;
				stand.localScale = Vector3.one;
				car.RotationTransform = stand;
				frozen++;
			}
			foreach (var copyEntry in Copies)
			{
				var template = Find(root, copyEntry.Template);
				var placeholder = template != null ? template.GetComponent<GHPC.Weapons.AmmoVisualPlaceholder>() : null;
				var rack = template != null ? template.GetComponentInParent<GHPC.Weapons.AmmoRack>() : null;
				if (placeholder == null || rack == null)
				{
					problems?.Add($"'{copyEntry.Template}' (slot to copy) not found");
					continue;
				}
				if (template.parent.Find(copyEntry.Name) != null) continue;
				var copy = Object.Instantiate(template.gameObject, template.parent, false);
				copy.name = copyEntry.Name;
				copy.transform.localPosition = template.localPosition;
				copy.transform.localRotation = template.localRotation;
				copy.transform.localScale = template.localScale;
				// A filled template's round comes along with Instantiate, and the rack doesn't know it.
				foreach (var visual in copy.GetComponentsInChildren<GHPC.Weapons.AmmoStoredVisual>(true))
					Object.DestroyImmediate(visual.gameObject);
				var cph = copy.GetComponent<GHPC.Weapons.AmmoVisualPlaceholder>();
				cph.RackIndex = copyEntry.RackIndex;
				cph.PairedChargeSlot = null;
				if (copyEntry.ChargeTemplate != null)
				{
					var chargeTemplate = Find(root, copyEntry.ChargeTemplate);
					if (chargeTemplate == null)
						problems?.Add($"'{copyEntry.ChargeTemplate}' (charge slot to copy) not found; '{copyEntry.Name}' has no charge");
					else
					{
						var chargeClone = Object.Instantiate(chargeTemplate.gameObject, chargeTemplate.parent, false);
						chargeClone.name = copyEntry.ChargeName;
						chargeClone.transform.localPosition = chargeTemplate.localPosition;
						chargeClone.transform.localRotation = chargeTemplate.localRotation;
						chargeClone.transform.localScale = chargeTemplate.localScale;
						var charges = new List<GameObject>();
						foreach (Transform child in chargeClone.transform) charges.Add(child.gameObject);
						foreach (var charge in charges) Object.DestroyImmediate(charge);
						foreach (var renderer in chargeClone.GetComponents<MeshRenderer>()) Object.Destroy(renderer);
						foreach (var meshFilter in chargeClone.GetComponents<MeshFilter>()) Object.Destroy(meshFilter);
						cph.PairedChargeSlot = chargeClone.transform;
					}
				}
				grown.Add(rack);
			}
			foreach (var grownRack in grown) grownRack.SetUpVisualSlots();
			foreach (var move in Moves)
			{
				var moved = Find(root, move.Path) ?? (move.Parent != null ? Find(root, move.CurrentPath) : null);
				if (moved == null)
				{
					problems?.Add($"'{move.Path}' not found");
					continue;
				}
				Transform parent = null;
				if (move.Parent != null && (parent = Find(root, move.Parent)) == null)
				{
					problems?.Add($"'{move.Parent}' (new parent of '{move.Path}') not found");
					continue;
				}
				if (parent != null && moved.parent != parent) moved.SetParent(parent, false);
				moved.localPosition = move.Local;
				if (move.Rot.HasValue) moved.localRotation = move.Rot.Value;
				if (move.Scale.HasValue) moved.localScale = move.Scale.Value;
			}
			foreach (var chargeSpec in ChargeSlots)
			{
				var slotTransform = Find(root, chargeSpec.Slot);
				var placeholder = slotTransform != null ? slotTransform.GetComponent<GHPC.Weapons.AmmoVisualPlaceholder>() : null;
				if (placeholder == null)
				{
					problems?.Add($"'{chargeSpec.Slot}' (slot for charge slot '{chargeSpec.Name}') not found");
					continue;
				}
				if (slotTransform.parent.Find(chargeSpec.Name) != null) continue;
				var slotObject = new GameObject(chargeSpec.Name);
				slotObject.layer = slotTransform.gameObject.layer;
				slotObject.transform.SetParent(slotTransform.parent, false);
				slotObject.transform.localPosition = chargeSpec.Local;
				slotObject.transform.localRotation = chargeSpec.Rot;
				placeholder.PairedChargeSlot = slotObject.transform;
			}
			foreach (var shape in Shapes)
			{
				var shapeTransform = Find(root, shape.Path);
				if (shapeTransform == null)
				{
					problems?.Add($"'{shape.Path}' (reshaped holder) not found");
					continue;
				}
				var mesh = MeshOf(shapeTransform.name + " (reshaped)", shape.Vertices, shape.Triangles);
				var meshFilter = shapeTransform.GetComponent<MeshFilter>();
				if (meshFilter != null) meshFilter.sharedMesh = mesh;
				var meshCollider = shapeTransform.GetComponent<MeshCollider>();
				if (meshCollider != null) meshCollider.sharedMesh = mesh;
			}
			foreach (var newShape in News)
			{
				var parent = Find(root, newShape.Parent);
				var tpl = Find(root, newShape.Template);
				if (parent == null)
				{
					problems?.Add($"'{newShape.Parent}' (parent of new holder '{newShape.Name}') not found");
					continue;
				}
				if (tpl == null)
				{
					problems?.Add($"'{newShape.Template}' (template of new holder '{newShape.Name}') not found");
					continue;
				}
				if (parent.Find(newShape.Name) != null) continue;
				var clone = Object.Instantiate(tpl.gameObject, parent, false);
				clone.name = newShape.Name;
				if (!clone.activeSelf)
				{
					clone.SetActive(true);
				}
				// Immediate: the colliders get registered below, and a deferred Destroy left them in the group.
				var kids = new List<GameObject>();
				foreach (Transform child in clone.transform) kids.Add(child.gameObject);
				foreach (var kid in kids) Object.DestroyImmediate(kid);
				clone.transform.localPosition = Vector3.zero;
				clone.transform.localRotation = Quaternion.identity;
				clone.transform.localScale = Vector3.one;
				var mesh = MeshOf(newShape.Name, newShape.Vertices, newShape.Triangles);
				var meshFilter = clone.GetComponent<MeshFilter>();
				if (meshFilter != null) meshFilter.sharedMesh = mesh;
				var meshCollider = clone.GetComponent<MeshCollider>();
				if (meshCollider != null) meshCollider.sharedMesh = mesh;
				foreach (var uniformArmor in clone.GetComponents<UniformArmor>())
				{
					ArmourName?.SetValue(uniformArmor, newShape.Name.ToLowerInvariant());
					uniformArmor.PrimarySabotRha = RackRha;
					uniformArmor.PrimaryHeatRha = RackRha;
				}
				GHPC.PhysicsHelpers.RaycastColliderGroup.AddNewColliderSetToParentGroup(clone.transform);
			}
			return null;
		}

		internal static Transform Find(Transform root, string path) => LivePath.Find(root, path);
	}
}
