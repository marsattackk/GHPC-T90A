using System;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public static class BaseRigT80B
	{
		public const string PrefabPath = "Assets/Units/1 - Tank - T-80/T80B.prefab";
		public const string HullPath = "T80B_rig/HULL", TurretPath = "T80B_rig/HULL/TURRET", GunPath = "T80B_rig/HULL/TURRET/gun";

		public static readonly Vector3 TurretPivot = new Vector3(0f, 1.412f, -1.1715f), GunPivot = new Vector3(0f, 1.6518f, -0.1493f);

		public static readonly string[] HiddenRenderers =
		{
			"body",
			"turret",
			"turret_skin",
			"skirts",
			"driver hatch mesh",
			"headlight_glass",
			"NSV",
			"nsv mount",
			"12.7_1",
			"12.7_2",
			"12.7_3",
			"camo_rear",
			"camo_side",
			"hose",
			"log",
			"sidepack",
			"suitcase_3stack_a",
			"towropes_front",
			"net_barrel",
			"net_glacis",
			"net_turret",
			"tac marker",
			"turret numbers",
			"Luna visible glow",
			"Luna lens glow",
			"lp_R123",
		};

		public static readonly string[] LampRenderers = { "gs_turret", "gst_illum", "march light", "march light non illum" };
		public const string TurretMarkerLight = "GST light red", NumbersGlow = "numbers glow";
		public static readonly string[] HullLights = { "front marker lamp L", "front marker lamp R", "tail lamp L", "tail lamp R" };

		public static readonly string[] KeptRenderers = { "wheels", "track_l", "track_r", "turret hole void filler" };

		public const string TurretVolume = "turret volume", HullVolume = "fighting compartment", EngineVolume = "engine compartment";

		// "fuel tank", "fuel tank 1", "fuel tank001..003" are the fender tanks: kept, the port moves them.
		public static readonly string[] ImportReplaces =
		{
			"60mm plate",
			"50mm texolite insert",
			"45mm plate",
			"16mm applique",
			"lower glacis",
			"side plate variable",
			"side plate variable002",
			"hull upper",
			"hull rear",
			"hull floor",
			"hatch",
			"peri armour",
			"plow",
			"plow attachments top",
			"plow attachments bottom",
			"tow hooks",
			"rib",
			"rib 1",
			"skirt",
			"skirt 1",
			"mud flap",
			"mud flap 1",
			"mud flap 2",
			"mud flap 3",
			"tinwork",
			"tinwork001",
			"ZIP box",
			"ZIP box001",
			"exhaust",
			"turret casting",
			"cheek inserts",
			"mantlet",
			"command mount",
			"gunner's hatch",
			"sight hood",
			"sight armour",
			"night sight armour",
			"Luna",
		};

		public static readonly string[] RunningGearColliders =
			Enumerable.Range(1, 12)
				.Select(i => $"RoadWheel_{i:000}")
				.Concat(new[]
				{
					"SprocketWheel_001",
					"SprocketWheel_002",
					"IdlerWheel_001",
					"IdlerWheel_002",
					"Tracks_Left_001",
					"Tracks_Left_002"
				})
				.ToArray();
		public static readonly string[] NotProtectionColliders = RunningGearColliders.Concat(new[]
		{
			"fuel tank",
			"fuel tank 1",
			"fuel tank001",
			"fuel tank002",
			"fuel tank003",
			"FuelTank_001",
			"FuelTank_002",
			"FuelTank_003",
			"FuelTank_004",
			"FuelTank_005",
			"FuelTank_011",
			"FuelTank_012",
			"FuelTank_013",
			"Firewall",
			"Engine_GTD-1000TF",
			"Engine_AirIntake_001 (Very thin metal)",
			"Engine_AirPurifier_001",
			"Engine_AirPurifier_002",
			"Gearbox_001",
			"Gearbox_002",
			"Radiator_EngineOil_001",
			"Radiator_TransmissionOil_001",
			"OilTank_001",
			"TransmissionOilTank_001 Not sure about the shape on the bottom",
			"Turret_TraverseMotor_Hull mounted parts of 2E26M stabilizer",
			"Driver_Seat",
			"Driver_Periscope_TNPO-160_001",
			"Driver_Periscope_TNPO-160_002",
			"Driver_Periscope_TNPO-160_003",
			"LRF hit zone",
			"Autoloader_MZ",
			"Carousel_Assembly(Rotates)",
			"Carousel_ElectricDrive(Can be manually cranked if failed)",
			"Turret_Seats(Vertically adjustable)",
			"Turret_basket_approximate",
			"Turret_ManualDrive",
			"parts of 2E26M stabilizer",
			"Gunner_Sight_TPN‑3‑49",
			"Gunner_Sight_1G42",
			"Gunner_Periscope_TNPA-65A_001",
			"Gun_Elevation_ManualDrive",
			"Gun_Elevation_HydroelectricPump(part of 2E26M stabilizer)",
			"MG_Coax_PKT_7.62mm",
			"Gun_Breach_Gun_Breach_BitsThatRecoil",
			"Gun_Breach_BitsThatDontRecoil",
			"Commander_Sight_TKN-3M",
			"Commander_Periscope_TNPA-65A_001",
			"Commander_Periscope_TNPA-65A_002",
			"Commnader_Periscope_TNPO-160_001",
			"Commnader_Periscope_TNPO-160_002",
		}).ToArray();

		public static readonly string[] FenderTanks = { "fuel tank 1", "fuel tank001", "fuel tank", "fuel tank002", "fuel tank003" };
		public static bool IsKept(string rendererName) => KeptRenderers.Contains(rendererName);

		public static readonly string[] GearRenderers = { "wheels", "track_l", "track_r" };

		public static string HideRunningGear(Transform root, string ownPrefix, Action<string> onReenabled = null)
		{
			int hidden = KeepHidden.HideGear(root, GearRenderers, ownPrefix, "T-80B running gear", onReenabled);
			return $"{hidden} T-80B running-gear renderers hidden";
		}

		public static string HideBaseVisuals(
			Transform root,
			string ownPrefix,
			Action<string> onReenabled = null,
			bool keepLamps = false)
		{
			bool UnderOurs(Transform part)
			{
				for (var ancestor = part;
					ancestor != null && ancestor != root;
					ancestor = ancestor.parent) if (ancestor.name.StartsWith(ownPrefix)) return true;
				return false;
			}
			var names = keepLamps ? HiddenRenderers : HiddenRenderers.Concat(LampRenderers).ToArray();
			var renderers = root.GetComponentsInChildren<Renderer>(true)
				.Where(r => !(r is ParticleSystemRenderer)
					&& names.Contains(r.gameObject.name)
					&& !UnderOurs(r.transform))
				.ToArray();
			foreach (var renderer in renderers) renderer.enabled = false;
			KeepHidden.Add(root, renderers, null, "T-80B part", onReenabled);
			var missing = names.Where(n => renderers.All(r => r.gameObject.name != n)).ToArray();
			return $"{renderers.Length} T-80B renderers hidden" + (missing.Length > 0 ? $"; not found: {string.Join(", ", missing)}" : "");
		}

		static TrimmedSkin _trim;
		static TrimmedSkin Trim => _trim ?? (_trim = new TrimmedSkin("front fender flaps and rear mud flaps removed"));
		public static string TrimFailure => _trim?.Failure;
		public static string TrimWheels(Transform root, byte[] trimmedSkin)
		{
			var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(c => c.name == "wheels");
			if (skin == null) return "no T-80B wheels skin found";
			if (trimmedSkin == null) return "no trimmed wheels data, the T-80B's fender and mud flaps stay";
			return Trim.Swap(skin, trimmedSkin, "the T-80B's fender and mud flaps stay");
		}

		public static Material FindTemplate(Transform root)
		{
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
				if (renderer.name == "body" || renderer.name == "turret")
				{
					var material = renderer.sharedMaterials.FirstOrDefault(c => c != null
						&& c.shader.name == "GHPC/VehicleShader");
					if (material != null) return material;
				}
			return null;
		}
	}
}
