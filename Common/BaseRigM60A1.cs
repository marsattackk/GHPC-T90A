using System;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public static class BaseRigM60A1
	{
		public const string PrefabPath = "Assets/Units/1 - Tank - M60/M60A1.prefab";
		public const string HullPath = "--RIG/hull", TurretPath = "--RIG/hull/turret", GunPath = "--RIG/hull/turret/main gun mantlet";

		public static readonly Vector3 TurretPivot = new Vector3(0f, 1.6724f, 0.6236f), GunPivot = new Vector3(0f, 2.0391f, 1.6411f);

		public const string HullGroup = "--HULL ARMOR",
			TurretGroup = "--TURRET ARMOR",
			GunGroup = "--GUN ARMOR",
			CupolaGroup = "--CUPOLA ARMOR",
			CupolaGunGroup = "--CUPOLA GUN ARMOR",
			FloorGroup = "TURRET FLOOR ARMOR";
		public static readonly string[] ColliderGroups = { HullGroup, TurretGroup, GunGroup, CupolaGroup, CupolaGunGroup, FloorGroup };
		public static bool IsColliderGroup(string name) => ColliderGroups.Contains(name);
		public static bool IsColliderGroup(string name, string group) => name == "--" + group.ToUpperInvariant() + " ARMOR";

		public static readonly string[] HiddenRenderers =
		{
			"hull_early",
			"hull_mid",
			"hull_late",
			"turret_early",
			"turret_mid",
			"gun_early",
			"gun_mid",
			"gun_late",
			"sightbox_A3",
			"sightbox_IR",
			"sightbox_TTS",
			"smoke launcher mounts",
			"spare_wheel_mantlet",
			"luggage",
			"bumper_numbers",
			"M60 gun net",
			"M60 hull net",
			"M60 mantlet net",
			"M60 turret net",
			"VVS day",
			"VVS night",
			"Luna lens glow",
			"Luna visible glow",
		};

		public static readonly string[] HiddenGroups = { "Turret decorations", "vehicle numbers" };

		public static readonly string[] LampRenderers =
		{
			"headlamp_emit",
			"convoy_marker_l",
			"convoy_marker_r",
			"brakelight_emit",
			"brakelight_blackout_emit",
			"IR_emit"
		};

		public static readonly string[] KeptRenderers =
		{
			"running_gear",
			"wheels_alu",
			"wheels_steel",
			"wheels_mixed1",
			"wheels_mixed2",
			"track_L",
			"track_R",
			"Object092"
		};

		public const string TurretVolume = "TURRET COMPARTMENT", HullVolume = "FIGHTING COMPARTMENT", EngineVolume = "ENGINE COMPARTMENT";

		public static readonly string[] ImportReplaces =
		{
			"hull casting",
			"engine deck",
			"driver's hatch",
			"filter left",
			"filter right",
			"sponson left",
			"sponson right",
			"sponson storage left",
			"sponson storage right",
			"turret casting no-jowl",
			"mantlet",
			"cupola",
			"cupola hatch",
			"cupola sight box",
			"cupola sight box cover",
			"Commander View Ports",
			"cupola mantlet",
			"coincidence bulge left",
			"coincidence bulge right",
			"rangefinder glass left",
			"rangefinder glass right",
			"filter dome",
			"loader hatch",
			"sight box",
			"sight box glass",
			"sight cover",
		};

		public static bool IsKept(string rendererName) => KeptRenderers.Contains(rendererName);

		public static readonly string[] GearRenderers =
		{
			"running_gear",
			"wheels_alu",
			"wheels_steel",
			"wheels_mixed1",
			"wheels_mixed2",
			"track_L",
			"track_R"
		};

		public static string HideRunningGear(Transform root, string ownPrefix, Action<string> onReenabled = null)
		{
			int count = KeepHidden.HideGear(root, GearRenderers, ownPrefix, "M60A1 running gear", onReenabled);
			return $"{count} M60A1 running-gear renderers hidden";
		}

		public static string HideBaseVisuals(
			Transform root,
			string ownPrefix,
			Action<string> onReenabled = null,
			bool keepLamps = false)
		{
			bool Under(Transform part, Func<string, bool> test)
			{
				for (var current = part; current != null && current != root; current = current.parent) if (test(current.name)) return true;
				return false;
			}
			var names = keepLamps ? HiddenRenderers : HiddenRenderers.Concat(LampRenderers).ToArray();
			var renderers = root.GetComponentsInChildren<Renderer>(true)
				.Where(r => !(r is ParticleSystemRenderer) && !Under(r.transform, n => n.StartsWith(ownPrefix))
					&& (names.Contains(r.gameObject.name) || Under(r.transform, n => HiddenGroups.Contains(n))))
				.ToArray();
			foreach (var renderer in renderers) renderer.enabled = false;
			KeepHidden.Add(root, renderers, null, "M60A1 part", onReenabled);
			var missing = names.Where(n => renderers.All(r => r.gameObject.name != n)).ToArray();
			return $"{renderers.Length} M60A1 renderers hidden" + (missing.Length > 0 ? $"; not found: {string.Join(", ", missing)}" : "");
		}

		public static Material FindTemplate(Transform root)
		{
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
				if (renderer.name.StartsWith("hull_") || renderer.name.StartsWith("turret_"))
				{
					var template = renderer.sharedMaterials.FirstOrDefault(m => m != null
						&& m.shader.name == "GHPC/VehicleShader");
					if (template != null) return template;
				}
			return null;
		}
	}
}
