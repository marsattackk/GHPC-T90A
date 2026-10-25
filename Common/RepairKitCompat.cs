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
	internal static class RepairKitCompat
	{
		const string ApplierType = "RepairKit.RepairApplier";
		static bool _tried;

		internal static void TryPatch(HarmonyLib.Harmony harmony)
		{
			if (_tried) return;
			_tried = true;
			var type = ApplierTypeOrNull();
			if (type == null) return;
			var apply = AccessTools.Method(type, "Apply", new[] { typeof(Unit), typeof(NwhChassis) });
			if (apply == null) { MelonLogger.Warning("RepairKit found, but not its repair method: the modules in the collider groups won't be repaired by it."); return; }
			try { harmony.Patch(apply, postfix: new HarmonyMethod(typeof(RepairKitCompat), nameof(Postfix))); }
			catch (Exception e) { MelonLogger.Warning($"RepairKit found, but its repair method couldn't be patched ({e.GetType().Name}: {e.Message}): the modules in the collider groups won't be repaired by it."); }
		}

		internal static Type ApplierTypeOrNull() => AppDomain.CurrentDomain.GetAssemblies()
			.Select(a => a.GetType(ApplierType, false)).FirstOrDefault(t => t != null);

		static void Postfix(Unit __0)
		{
			var unit = __0;
			try
			{
				if (!(unit is Vehicle v) || !UnitRegistry.Units.Any(u => string.Equals(u.Key, unit.UniqueName, StringComparison.OrdinalIgnoreCase))) return;
				Heal(v, Target());
			}
			catch (Exception e) { MelonLogger.Warning($"RepairKit repair of {unit?.name}: our modules not healed ({e.GetType().Name}: {e.Message})."); }
		}

		internal static float Target()
		{
			float t = 70f;
			try { if (MelonPreferences.HasEntry("RepairKit", "RepairHealthPercent")) t = MelonPreferences.GetEntryValue<float>("RepairKit", "RepairHealthPercent"); }
			catch { }
			return Mathf.Clamp(t, 1f, 100f);
		}

		internal static List<DC> Outside(Vehicle v) => ModuleBuilder.UnitObjects(v).Skip(1)
			.SelectMany(t => t.GetComponentsInChildren<DC>(true)).Distinct()
			.Where(d => d != null && !d.Hidden && (d.Unit == null || d.Unit == v)
				&& d.GetComponentInParent<GHPC.Crew.BodyPartComponentRegistry>(true) == null && d.GetComponentInParent<GHPC.Humans.Human>(true) == null)
			.ToList();

		static readonly MethodInfo GuidanceDamagedSet = Reflect.PropertySetter(typeof(MissileGuidanceUnit), nameof(MissileGuidanceUnit.Damaged));
		static readonly FieldInfo RadioZone = Reflect.Field(typeof(Radio), "_radioDestructible");

		internal static void Heal(Vehicle v, float target)
		{
			var healed = new List<DC>();
			foreach (var d in Outside(v))
			{
				if (d.HealthPercent >= target) continue;
				bool wasDestroyed = d.IsDestroyed;
				d.SetHealthPercent(target);
				if (wasDestroyed) healed.Add(d);
			}
			if (healed.Count == 0) return;
			var objs = ModuleBuilder.UnitObjects(v);
			foreach (var g in objs.SelectMany(t => t.GetComponentsInChildren<MissileGuidanceUnit>(true)).Distinct())
				if (g.Damaged && healed.Contains(g.GuidanceComponent)) GuidanceDamagedSet.Invoke(g, new object[] { false });
			foreach (var r in objs.SelectMany(t => t.GetComponentsInChildren<Radio>(true)).Distinct())
				if (!r.enabled && healed.Contains(RadioZone?.GetValue(r) as DC)) r.enabled = true;
		}
	}
}
