using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.AI;
using GHPC.Mission;
using GHPC.Mission.Data;
using GHPC.UI.Menu;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class UnitDef
	{
		public string Key;
		public Func<string> FriendlyName;
		public Func<string> BaseUnit;
		public Func<string> ArmyFrom;
		public Func<bool> Enabled = () => true;
		public Func<bool> InCampaign = () => false;
		public Func<float> CampaignPrevalence = () => 0.5f;
		public Func<int> CampaignAfterMissions = () => 0;
		public Action<Vehicle> OnSpawn;
	}

	internal static class UnitRegistry
	{
		internal static readonly List<UnitDef> Units = new List<UnitDef>();
		static bool _registered, _running;

		internal static void Add(UnitDef u) => Units.Add(u);

		internal static UnitPrefabLookupScriptable Lookup => Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>().FirstOrDefault();

		internal static UnitPrefabLookupScriptable.UnitPrefabMetadata Find(string key) => FindIn(Lookup, key);

		static UnitPrefabLookupScriptable.UnitPrefabMetadata FindIn(UnitPrefabLookupScriptable lookup, string key) =>
			lookup?.AllUnits?.FirstOrDefault(m => m != null && string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));

		internal static void TryRegister()
		{
			if (_registered || _running) return;
			_running = true;
			MelonCoroutines.Start(RegisterRoutine());
		}

		static IEnumerator RegisterRoutine()
		{
			while (!_registered)
			{
				bool any = false;
				foreach (var lookup in Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>())
					any |= EnsureRegistered(lookup);
				if (any) _registered = true;
				else yield return null;
			}
			_running = false;
		}

		internal static bool EnsureRegistered(UnitPrefabLookupScriptable lookup)
		{
			if (lookup?.AllUnits == null) return false;
			bool all = true;
			foreach (var u in Units.Where(x => x.Enabled()))
			{
				if (FindIn(lookup, u.Key) != null) continue;
				var baseMeta = FindIn(lookup, u.BaseUnit());
				if (baseMeta?.PrefabReference == null) { all = false; continue; }
				var armyMeta = FindIn(lookup, u.ArmyFrom()) ?? baseMeta;
				var units = lookup.AllUnits.ToList();
				units.Add(new UnitPrefabLookupScriptable.UnitPrefabMetadata
				{
					Name = u.Key,
					FriendlyName = u.FriendlyName(),
					PrefabReference = baseMeta.PrefabReference,
					BaseAmmoClipsReference = baseMeta.BaseAmmoClipsReference,
					UseDecalLayout = armyMeta.UseDecalLayout,
					DecalLayout = armyMeta.DecalLayout,
					Army = armyMeta.Army,
					Class = baseMeta.Class,
					AlternativeClasses = baseMeta.AlternativeClasses,
					AllowInCustomizer = true,
				});
				lookup.AllUnits = units.ToArray();
				var spawner = UnitSpawner.Instance;
				if (spawner != null) AccessTools.Field(typeof(UnitSpawner), "_prefabLookupCached").SetValue(spawner, null);
				MenuListPatch.ClearCache();
			}
			return all;
		}

		internal static IEnumerator AddToCampaignRoster()
		{
			TheaterMetaScriptable meta = null;
			for (int frame = 0; frame < 600 && meta == null; frame++)
			{
				meta = Resources.FindObjectsOfTypeAll<TheaterMetaScriptable>().FirstOrDefault();
				if (meta == null) yield return null;
			}
			if (meta?.AllTheaters == null) yield break;

			foreach (var u in Units.Where(x => x.Enabled() && x.InCampaign()))
				foreach (var theater in meta.AllTheaters)
				{
					if (theater?.Factions == null) continue;
					foreach (var faction in theater.Factions)
					{
						if (faction?.AllUnits == null) continue;
						var units = faction.AllUnits.ToList();
						if (units.Any(x => x != null && x.Key == u.Key)) continue;
						var armyUnit = units.FirstOrDefault(x => x != null && x.Key == u.ArmyFrom());
						if (armyUnit == null) continue;
						var template = units.FirstOrDefault(x => x != null && x.Key == u.BaseUnit()) ?? armyUnit;
						units.Add(new TheaterUnitMetadata
						{
							Key = u.Key,
							FriendlyName = u.FriendlyName(),
							Type = armyUnit.Type,
							Class = armyUnit.Class,
							Tier = armyUnit.Tier,
							SpawnRatio = armyUnit.SpawnRatio,
							Prevalence = u.CampaignPrevalence(),
							BlockUntilMissionsCompleted = u.CampaignAfterMissions(),
							LogisticsModifierCurve = template.LogisticsModifierCurve,
						});
						faction.AllUnits = units.ToArray();
					}
				}
		}

		[HarmonyPatch(typeof(MissionConfigMenuPanel), "GetUnitKeysByClass")]
		internal static class MenuListPatch
		{
			static readonly AccessTools.FieldRef<MissionConfigMenuPanel, UnitPrefabLookupScriptable> AllUnits =
				AccessTools.FieldRefAccess<MissionConfigMenuPanel, UnitPrefabLookupScriptable>("_allUnitsScriptable");
			static readonly System.Reflection.FieldInfo Cache = AccessTools.Field(typeof(MissionConfigMenuPanel), "_unitKeysByClassCached");

			internal static void ClearCache() => (Cache?.GetValue(null) as IDictionary)?.Clear();

			static void Prefix(MissionConfigMenuPanel __instance)
			{
				var lookup = AllUnits(__instance);
				if (lookup?.AllUnits == null) return;
				if (Units.Any(u => u.Enabled() && !lookup.AllUnits.Any(m => m != null && m.Name == u.Key)))
					EnsureRegistered(lookup);
			}
		}

		[HarmonyPatch(typeof(UnitSpawner), nameof(UnitSpawner.SpawnUnit), new[] { typeof(string), typeof(UnitMetaData), typeof(WaypointHolder), typeof(Transform) })]
		internal static class SpawnPatch
		{
			static readonly AccessTools.FieldRef<Unit, string> UniqueName = AccessTools.FieldRefAccess<Unit, string>("_uniqueName");
			static readonly AccessTools.FieldRef<Unit, string> FriendlyNameRef = AccessTools.FieldRefAccess<Unit, string>("_friendlyName");

			static void Postfix(string uniqueName, IUnit __result)
			{
				var u = Units.FirstOrDefault(x => string.Equals(uniqueName, x.Key, StringComparison.OrdinalIgnoreCase));
				if (u == null) return;
				if (!(__result is Vehicle vehicle)) { MelonLogger.Warning($"{u.Key} spawn returned no Vehicle."); return; }
				UniqueName(vehicle) = u.Key;
				FriendlyNameRef(vehicle) = u.FriendlyName();
				u.OnSpawn?.Invoke(vehicle);
			}
		}
	}
}
