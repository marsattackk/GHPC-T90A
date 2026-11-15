using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// T-72M1 bone paths, and the base parts that get hidden under the new model.
	public static class BaseRigT72M1
	{
		public const string PrefabPath = "Assets/Units/1 - Tank - T-72/T72M1.prefab";
		public const string HullPath = "---MESH---/HULL", TurretPath = "---MESH---/HULL/TURRET", GunPath = "---MESH---/HULL/TURRET/GUN";

		public static readonly string[] HiddenRenderers =
		{
			"T72M1_hull",
			"T72M1_turret",
			"T72M1_skin_turret",
			"lp_turret_cables001",
			"equipment",
			"t72m hull net",
			"t72m turret net",
			"t72m gun net",
			"roundels_72M",
			"roundels_72M1",
			"bustle_number_3digit",
			"bustle_number_4digit",
			"nsv_mount",
			"lp_R123",
		};

		public static readonly string[] RunningGearColliders = { "Track", "Track.001 (1)", "Road wheel", "Sprocket", "Idler Wheel" };
		public static readonly string[] HullAttachmentColliders =
		{
			"Mud Guard",
			"Mudguard Rubber",
			"Sponson",
			"Sponson Fuel Tank",
			"Sponson Storage Container",
			"Log",
			"Plow",
			"Exhaust"
		};
		public static readonly string[] CarouselSideShields = { "Carosel Plate.001", "Carosel Plate.002", "Carosel Plate.004" };
		public static readonly string[] NotProtectionColliders = RunningGearColliders.Concat(HullAttachmentColliders).Concat(new[]
		{
			"Fuel tanks",
			"Fuel tank front",
			"Fuel tank front.001",
			"Fuel tank ammo storage",
			"Rear Fuel tank",
			"Fire Wall",
			"Engine Cover",
			"Carosel Plate.001",
			"Carosel Plate.002",
			"Carosel Plate.003",
			"Carosel Plate.004",
			"Carosell assembly",
			"lp_hull_sym.003",
			"Autoloader Arm",
			"Gunner Sight",
			"fire control system hit zone",
			"LRF hit zone",
			"Night Sight",
			"SEAT",
			"SEAT.001",
			"Breech",
			"Breechblock",
			"Commander Para",
			"Vision Block.003",
			"Commanders Spotlight",
			"Commanders Light Cover",
			"Luna",
			"Luna Cover",
			"Gunsight Glass",
			"Night Sight Glass",
		}).ToArray();

		// "Gunner Sight" and "Night Sight" stand inside the new left cheek. "Fuel tanks" is the right fender's tank row.
		public static readonly string[] ImportReplaces =
		{
			"Upper Front Plate",
			"Upper Front Plate Extra Armor",
			"Textolite.001",
			"Backing Plate Armor.004",
			"Driver Extra Armor",
			"Lower Front Plate",
			"Hull Side",
			"Hull Side.001",
			"Hull Floor",
			"Hull Roof 20mm",
			"Hull Roof 30mm ",
			"Hull rear",
			"Hull rear.001",
			"Turret Ring",
			"lp_driverhatch",
			"Mud Guard",
			"Mudguard Rubber",
			"Sponson",
			"Sponson Fuel Tank",
			"Fuel tanks",
			"Sponson Storage Container",
			"Log",
			"Plow",
			"Turret.002",
			"Composite Armor Array",
			"Turret Storage box",
			"Sight Shielding",
			"Loaders Hatch.002",
			"Ejector Door",
			"Gun Mantlet",
			"Commanders Cupola",
			"Commanders Hatch",
			"Commanders Spotlight",
			"Commanders Light Cover",
			"Luna",
			"Luna Cover",
			"Gunsight Housing",
			"NightSight Housing",
			"Night Sight Cover",
			"Gunsight Glass",
			"Night Sight Glass",
			"Gunner Sight",
			"Night Sight",
		};

		// Collapsing them leaves the skirt vertices that are blended with HULL half pulled, and they stick through the
		// new hull sides as slivers. So the skin gets swapped for a trimmed one and the collapse is only the fallback.
		public static bool IsHullDetailBone(string n) =>
			n == "fender_L" || n == "fender_R" || n == "flaps" || n.StartsWith("gill_") || n.StartsWith("skirt_");

		public static string HideBaseVisuals(Transform root, byte[] trimmedSkin = null)
		{
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
				if (HiddenRenderers.Contains(renderer.gameObject.name)
					|| (renderer.transform.parent != null && HiddenRenderers.Contains(renderer.transform.parent.name)))
					renderer.enabled = false;

			string skinReport = "no T72M1_skin_hull found";
			foreach (var skin in root
				.GetComponentsInChildren<SkinnedMeshRenderer>(true)
				.Where(c => c.name == "T72M1_skin_hull"))
			{
				foreach (var bone in skin.bones)
					if (bone != null && IsHullDetailBone(bone.name)) bone.localScale = Vector3.zero;
				skinReport = trimmedSkin == null ? "no trimmed skin data, hull-detail bones collapsed only" : TrimSkin(skin, trimmedSkin);
			}

			var smoke = root.Find("---MESH---/HULL/TURRET/smoke rack");
			if (smoke != null) smoke.localScale = Vector3.zero;
			return skinReport;
		}

		static TrimmedSkin _trim;
		static TrimmedSkin Trim => _trim ??= new TrimmedSkin("skirts / flaps / fenders / gills removed");

		public static string TrimFailure => Trim.Failure;

		public static string TrimSkin(SkinnedMeshRenderer skin, byte[] data) => Trim.Swap(skin, data, "bones collapsed only");

		// The T-72 interior pokes through the new turret. The IR searchlight's light stays because the night sight needs it.
		public static readonly string[] LeftoverMaterials = { "T72_interior", "GST emission", "IR searchlight glow" };
		public static readonly string[] LeftoverNames = { "Luna lens glow", "Luna visible glow" };
		public const string LeftoverLightPrefix = "GST light";

		public static string HideLeftovers(Transform root, string ownPrefix, System.Action<string> onReenabled = null)
		{
			bool UnderOurs(Transform part)
			{
				for (var ancestor = part;
					ancestor != null && ancestor != root;
					ancestor = ancestor.parent) if (ancestor.name.StartsWith(ownPrefix)) return true;
				return false;
			}
			string Clean(Material material) => material == null ? "" : material.name.Replace(" (Instance)", "");
			var renderers = root.GetComponentsInChildren<Renderer>(true)
				.Where(r => !UnderOurs(r.transform) && !(r is ParticleSystemRenderer)
					&& (LeftoverNames.Contains(r.name)
						|| r.sharedMaterials.Any(s => LeftoverMaterials.Contains(Clean(s)))))
				.ToArray();
			var lights = root.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith(LeftoverLightPrefix)).ToArray();
			foreach (var renderer in renderers) renderer.enabled = false;
			foreach (var light in lights) light.enabled = false;
			KeepHidden.Add(root, renderers, lights, "T-72M1 leftover", onReenabled);
			return $"{renderers.Length} T-72M1 renderers ({string.Join(", ", renderers.Select(r => r.name).Distinct())}), {lights.Length} GST marker lights";
		}

		public static readonly Vector3 TurretPivot = new Vector3(0f, 1.369f, -0.527f), GunPivot = new Vector3(0f, 1.651f, 0.372f);

		public static readonly Vector3 EjectionPortCentre = new Vector3(0.003f, 1.974f, -1.214f);

		public static int MoveEjectionMarkers(Transform root, Vector3 worldShift)
		{
			int moved = 0;
			foreach (var marker in root
				.GetComponentsInChildren<Transform>(true)
				.Where(c => c.name == "ejection path" || c.name == "ejection port fire marker")
				.ToList())
			{
				marker.position += worldShift;
				moved++;
			}
			return moved;
		}

		public static Material FindTemplate(Transform root)
		{
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
				if (renderer.name == "T72M1_hull" || renderer.name == "T72M1_turret")
				{
					var material = renderer.sharedMaterials.FirstOrDefault(c => c != null
						&& c.shader.name == "GHPC/VehicleShader");
					if (material != null) return material;
				}
			return null;
		}
	}
}
