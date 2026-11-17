using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Equipment;
using GHPC.Equipment.Lamps;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;

namespace T90A.Common
{
	internal static class BaseStrip
	{
		// the T-72M1's and the T-80B's Luna: its IR light and both glows. A port whose model hides the lamp takes this out too,
		// or the crew lights the ground at night from a lamp nobody can see.
		internal const string BaseLuna = "---ELECTRICS SCRIPTS---/spotlight";

		internal static string TakeOutLuna(Vehicle vehicle, string tag)
			=> Apply(vehicle, new Settings { Tag = tag, OffPaths = new[] { BaseLuna } });

#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Tag = "";
			public string[] OffPaths = new string[0];
			public string[] WeaponsOff = new string[0];
			public bool NoOpticalRangefinder, NoNightVision;
		}
#pragma warning restore CS0649

		static readonly FieldInfo UnitSlots = Reflect.Field(typeof(Unit), "_designatedCameraSlots");
		static readonly FieldInfo NightType = Reflect.Field(typeof(Unit), "_nightVisionType");
		static readonly FieldInfo Spotter = Reflect.Field(typeof(Unit), "_targetSpotterSettings");
		static readonly FieldInfo LampsByType = Reflect.Field(typeof(EquipmentManager), "lampsDict");
		static readonly MethodInfo SetRangefinder = Reflect.PropertySetter(
			typeof(FireControlSystem),
			nameof(FireControlSystem.OpticalRangefinder));

		internal static string Apply(Vehicle vehicle, Settings s)
		{
			var root = vehicle.transform;
			var log = new List<string>();

			var weapons = vehicle.GetComponent<WeaponsManager>();
			if (weapons?.Weapons != null && s.WeaponsOff.Length > 0)
			{
				bool Drop(WeaponSystemInfo weapon) => weapon?.Weapon != null && s.WeaponsOff.Contains(weapon.Weapon.name);
				int count = weapons.Weapons.Count(Drop);
				weapons.Weapons = weapons.Weapons.Where(w => !Drop(w)).ToArray();
				var gunner = vehicle.GetComponent<CrewManager>()?.GetCrewBrain(CrewPosition.Gunner)?.WeaponsModule;
				gunner?.Weapons?.RemoveAll(Drop);
				log.Add($"{count} weapon(s) dropped");
				// GetIdealWeaponRole reads .IsUnusable on the role's weapon with no null test: a preference for a dropped role throws in the AI's target sensor.
				if (weapons.Preferences != null)
					weapons.Preferences = weapons.Preferences
						.Where(p => weapons.Weapons.Any(w => w.Role == p.Role))
						.ToArray();
			}

			if (s.NoOpticalRangefinder)
				foreach (var fcs in vehicle.GetComponentsInChildren<FireControlSystem>(true))
					if (fcs.OpticalRangefinder != null)
					{
						SetRangefinder?.Invoke(fcs, new object[] { null });
						log.Add("optical rangefinder forgotten");
					}

			var missing = new List<string>();
			int off = 0;
			foreach (var path in s.OffPaths)
			{
				var part = LivePath.Find(root, path);
				if (part == null)
				{
					missing.Add(path);
					continue;
				}
				part.gameObject.SetActive(false);
				off++;
			}
			log.Add($"{off} object(s) off");

			if (UnitSlots?.GetValue(vehicle) is CameraSlot[] slots)
				UnitSlots.SetValue(vehicle, slots.Where(x => x != null && x.gameObject.activeInHierarchy).ToArray());

			RelinkSlots(vehicle, log);
			DropDeadLamps(vehicle, log);

			if (s.NoNightVision)
			{
				NightType?.SetValue(vehicle, GHPC.Equipment.Optics.NightVisionType.None);
				var settings = Spotter?.GetValue(vehicle);
				var best = settings == null ? null : Reflect.Field(settings.GetType(), "_bestNightVision");
				if (best != null)
				{
					best.SetValue(settings, GHPC.AI.TargetSpotterSettings.NightVisionQuality.None);
					if (settings.GetType().IsValueType) Spotter.SetValue(vehicle, settings);
				}
				log.Add("night vision off");
			}

			if (missing.Count > 0) MelonLogger.Warning($"{s.Tag}: base equipment not found (game update?): {string.Join(", ", missing)}.");
			return string.Join("; ", log);
		}

		static void RelinkSlots(Vehicle vehicle, List<string> log)
		{
			var all = vehicle.GetComponentsInChildren<CameraSlot>(true);
			var kept = (UnitSlots?.GetValue(vehicle) as CameraSlot[])?.Where(s => s != null).ToArray() ?? new CameraSlot[0];
			int night = 0, destroy = 0;
			foreach (var slot in all)
			{
				if (slot.LinkedNightSight != null && !slot.LinkedNightSight.gameObject.activeInHierarchy)
				{
					slot.LinkedNightSight = null;
					night++;
				}
				if (slot.SlotToUseOnDestroy != null && !slot.SlotToUseOnDestroy.gameObject.activeInHierarchy)
				{
					slot.SlotToUseOnDestroy = kept.FirstOrDefault(k => k != slot);
					destroy++;
				}
			}
			if (night + destroy > 0) log.Add($"{night} night link(s) cleared, {destroy} fallback view(s) moved");
		}

		static void DropDeadLamps(Vehicle vehicle, List<string> log)
		{
			var equipment = vehicle.GetComponent<EquipmentManager>();
			if (equipment?.AllLamps == null) return;
			bool Dead(LampInfo lampInfo)
				=> lampInfo?.Lamps != null
					&& lampInfo.Lamps.Length > 0
					&& lampInfo.Lamps.All(l => l == null || !l.gameObject.activeInHierarchy);
			var dead = equipment.AllLamps.Where(Dead).ToArray();
			if (dead.Length == 0) return;
			equipment.AllLamps = equipment.AllLamps.Except(dead).ToArray();
			if (LampsByType?.GetValue(equipment) is Dictionary<LampType, List<LampInfo>> byType)
				foreach (var type in byType.Keys.ToArray())
				{
					byType[type].RemoveAll(dead.Contains);
					if (byType[type].Count == 0) byType.Remove(type);
				}
			log.Add($"{dead.Length} lamp(s) dropped");
		}
	}
}
