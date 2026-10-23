using System;
using System.Globalization;
using System.Linq;
using GHPC.Equipment;
using GHPC.Vehicle;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class SmokeLaunchers
	{
		internal const string AssetName = "smoke_points";

		internal static Vector3[] Parse(string text)
		{
			var f = text.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
			return Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2])).ToArray();
		}

		internal static Vector3[] Load(AssetBundle bundle)
		{
			var a = bundle != null ? bundle.LoadAsset<TextAsset>(AssetName) : null;
			return a != null ? Parse(a.text) : null;
		}

		internal static void Seat(Vehicle vehicle, Vector3[] tubes)
		{
			if (tubes == null || tubes.Length == 0) return;
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var turret = vehicle.transform.Find("---MESH---/HULL/TURRET");
			if (mgr == null || turret == null) return;
			var slots = (VehicleSmokeManager.SmokeSlot[])AccessTools.Field(typeof(VehicleSmokeManager), "_smokeSlots").GetValue(mgr);
			if (slots == null) return;
			var left = tubes.Where(t => t.x < 0).OrderByDescending(t => t.y).ToList();
			var right = tubes.Where(t => t.x >= 0).OrderByDescending(t => t.y).ToList();
			var bySide = slots.Where(s => s.SpawnLocation != null).OrderBy(s => turret.InverseTransformPoint(s.SpawnLocation.position).x).ToList();
			var slotsL = bySide.Take(left.Count).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			var slotsR = bySide.Skip(left.Count).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			for (int i = 0; i < slotsL.Count && i < left.Count; i++) { slotsL[i].SpawnLocation.position = turret.TransformPoint(left[i]); }
			for (int i = 0; i < slotsR.Count && i < right.Count; i++) { slotsR[i].SpawnLocation.position = turret.TransformPoint(right[i]); }
		}

		internal static void RefreshCrew(VehicleSmokeManager mgr)
		{
			var t = typeof(VehicleSmokeManager);
			var get = AccessTools.Method(t, "GetCrewBrainsArrayFromFlag");
			if (AccessTools.Field(t, "_unitInfoBroker").GetValue(mgr) == null) return;
			foreach (var (cache, flag) in new[] { ("_smokeGrenadeCrewBrains", "_smokeGrenadeRequiredCrewPositions"), ("_smokeScreenCrewBrains", "_smokeScreenRequiredCrewPositions") })
			{
				var arr = (Array)AccessTools.Field(t, cache).GetValue(mgr);
				if (arr != null && arr.Cast<object>().All(b => b != null)) continue;
				var fresh = get.Invoke(mgr, new[] { AccessTools.Field(t, flag).GetValue(mgr) });
				AccessTools.Field(t, cache).SetValue(mgr, fresh);
			}
		}

		[HarmonyPatch(typeof(VehicleSmokeManager), nameof(VehicleSmokeManager.FireSmokes))]
		static class CrewRefreshPatch
		{
			static void Prefix(VehicleSmokeManager __instance)
			{
				var unit = __instance.GetComponentInParent<Vehicle>();
				var u = unit != null ? UnitRegistry.Units.FirstOrDefault(x => x.Key == unit.UniqueName) : null;
				if (u == null) return;
				RefreshCrew(__instance);
			}
		}
	}
}
