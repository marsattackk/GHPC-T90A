using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.AI.Radios;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using DC = GHPC.Equipment.DestructibleComponent;

namespace T90A.Common
{
	// RepairKit searches under the unit, but the collider groups holding the modules sit at the scene root. When
	// RepairKit is installed this points it there too.
	internal static class RepairKitCompat
	{
		const string ApplierType = "RepairKit.RepairApplier";
		static bool _tried;
		internal static bool Patched { get; private set; }

		internal static void TryPatch(HarmonyLib.Harmony harmony)
		{
			if (_tried) return;
			_tried = true;
			var type = ApplierTypeOrNull();
			if (type == null) return;
			var apply = AccessTools.Method(type, "Apply", new[] { typeof(Unit), typeof(NwhChassis) });
			if (apply == null)
			{
				MelonLogger.Warning("RepairKit found, but not its repair method: the modules in the collider groups won't be repaired by it.");
				return;
			}
			try { harmony.Patch(apply, postfix: new HarmonyMethod(typeof(RepairKitCompat), nameof(Postfix))); }
			catch (Exception exception)
			{
				MelonLogger.Warning($"RepairKit found, but its repair method couldn't be patched ({exception.GetType().Name}: {exception.Message}): the modules in the collider groups won't be repaired by it.");
				return;
			}
			Patched = true;
		}

		// Not TypeByName: that one warns in every game without RepairKit.
		internal static Type ApplierTypeOrNull() => AppDomain.CurrentDomain.GetAssemblies()
			.Select(a => a.GetType(ApplierType, false)).FirstOrDefault(t => t != null);

		static void Postfix(Unit __0)
		{
			var unit = __0;
			try
			{
				if (!(unit is Vehicle vehicle)
					|| !UnitRegistry.Units.Any(u => string.Equals(u.Key, unit.UniqueName, StringComparison.OrdinalIgnoreCase)))
					return;
				Heal(vehicle, Target());
			}
			catch (Exception exception)
			{
				MelonLogger.Warning($"RepairKit repair of {unit?.name}: our modules not healed ({exception.GetType().Name}: {exception.Message}).");
			}
		}

		internal static float Target()
		{
			float percent = 70f;
			try
			{
				if (MelonPreferences.HasEntry("RepairKit", "RepairHealthPercent"))
					percent = MelonPreferences.GetEntryValue<float>("RepairKit", "RepairHealthPercent");
			}
			catch { }
			return Mathf.Clamp(percent, 1f, 100f);
		}

		internal static List<DC> Outside(Vehicle v) => ModuleBuilder.UnitObjects(v).Skip(1)
			.SelectMany(r => r.GetComponentsInChildren<DC>(true)).Distinct()
			.Where(c => c != null && !c.Hidden && (c.Unit == null || c.Unit == v)
				&& c.GetComponentInParent<GHPC.Crew.BodyPartComponentRegistry>(true) == null && c.GetComponentInParent<GHPC.Humans.Human>(true) == null)
			.ToList();

		static readonly MethodInfo GuidanceDamagedSet = Reflect.PropertySetter(
			typeof(MissileGuidanceUnit),
			nameof(MissileGuidanceUnit.Damaged));
		static readonly FieldInfo RadioZone = Reflect.Field(typeof(Radio), "_radioDestructible");

		internal static List<DC> Heal(Vehicle v, float target)
		{
			var healed = new List<DC>();
			foreach (var component in Outside(v))
			{
				if (component.HealthPercent >= target) continue;
				bool wasDestroyed = component.IsDestroyed;
				// Fires Repaired on a destroyed zone so the owners undo their effects.
				component.SetHealthPercent(target);
				if (wasDestroyed) healed.Add(component);
			}
			if (healed.Count == 0) return healed;
			// Neither GHPC nor RepairKit ever repairs the radio or the missile guidance.
			var objs = ModuleBuilder.UnitObjects(v);
			foreach (var guidance in objs.SelectMany(r => r.GetComponentsInChildren<MissileGuidanceUnit>(true)).Distinct())
				if (GuidanceDamagedSet != null && guidance.Damaged && healed.Contains(guidance.GuidanceComponent))
					GuidanceDamagedSet.Invoke(guidance, new object[] { false });
			foreach (var radio in objs.SelectMany(r => r.GetComponentsInChildren<Radio>(true)).Distinct())
				if (!radio.enabled && healed.Contains(RadioZone?.GetValue(radio) as DC)) radio.enabled = true;
			return healed;
		}
	}
}
