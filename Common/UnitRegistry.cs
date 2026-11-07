using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.AI;
using GHPC.Mission;
using GHPC.Mission.Data;
using GHPC.State;
using GHPC.UI.Menu;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;

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
		public string[] ShieldFrom;
		public Func<string> ShieldName;
	}

	internal sealed class CampaignConfig
	{
		internal MelonPreferences_Entry<bool> InRoster;
		internal MelonPreferences_Entry<float> Prevalence;
		internal MelonPreferences_Entry<int> AfterMissions;

		internal static CampaignConfig Create(MelonPreferences_Category cfg, string friendlyName, float prevalence, int afterMissions) => new CampaignConfig
		{
			InRoster = cfg.CreateEntry("Add to campaign roster", true, description: $"Adds the {friendlyName} to the Soviet campaign roster."),
			Prevalence = cfg.CreateEntry("Campaign prevalence", prevalence, description: "How common it is compared to other tanks (1 = as common as a typical tank)."),
			AfterMissions = cfg.CreateEntry("Campaign unlock after missions", afterMissions),
		};

		internal void Use(UnitDef u)
		{
			u.InCampaign = () => InRoster.Value;
			u.CampaignPrevalence = () => Prevalence.Value;
			u.CampaignAfterMissions = () => AfterMissions.Value;
		}
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

		static AssetReference OwnReference(AssetReference r) =>
			r == null ? null : new AssetReference(r.AssetGUID) { SubObjectName = r.SubObjectName };

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
					// Shared on purpose, the spawner loads each reference once.
					PrefabReference = baseMeta.PrefabReference,
					// Not shared: the customizer loads every unit's ammo list and Addressables moans at a second load.
					BaseAmmoClipsReference = OwnReference(baseMeta.BaseAmmoClipsReference),
					UseDecalLayout = armyMeta.UseDecalLayout,
					DecalLayout = armyMeta.DecalLayout,
					Army = armyMeta.Army,
					Class = baseMeta.Class,
					AlternativeClasses = baseMeta.AlternativeClasses,
					AllowInCustomizer = true,
				});
				lookup.AllUnits = units.ToArray();
				var spawner = UnitSpawner.Instance;
				if (spawner != null) Reflect.Field(typeof(UnitSpawner), "_prefabLookupCached").SetValue(spawner, null);
				MenuListPatch.ClearCache();
			}
			return all;
		}

		// A campaign save holds no unit list, each mission draws from AllUnits, so entries can come and go.
		static readonly HashSet<TheaterUnitMetadata> _added = new HashSet<TheaterUnitMetadata>();

		internal static void StartCampaignRoster()
		{
			if (_added.Count > 0 || Units.Any(x => x.Enabled() && x.InCampaign())) MelonCoroutines.Start(UpdateCampaignRoster());
		}

		internal static IEnumerator UpdateCampaignRoster()
		{
			TheaterMetaScriptable meta = null;
			for (int frame = 0; frame < 600 && meta == null; frame++)
			{
				if (frame % 10 == 0) meta = Resources.FindObjectsOfTypeAll<TheaterMetaScriptable>().FirstOrDefault();
				if (meta == null) yield return null;
			}
			if (meta?.AllTheaters == null) yield break;

			foreach (var theater in meta.AllTheaters)
			{
				if (theater?.Factions == null) continue;
				foreach (var faction in theater.Factions)
				{
					if (faction?.AllUnits == null) continue;
					var gone = faction.AllUnits.Where(x => x != null && _added.Contains(x) && !Units.Any(u => u.Key == x.Key && u.Enabled() && u.InCampaign())).ToList();
					if (gone.Count == 0) continue;
					faction.AllUnits = faction.AllUnits.Except(gone).ToArray();
					foreach (var x in gone) _added.Remove(x);
				}
			}

			foreach (var u in Units.Where(x => x.Enabled() && x.InCampaign()))
				foreach (var theater in meta.AllTheaters)
				{
					if (theater?.Factions == null) continue;
					foreach (var faction in theater.Factions)
					{
						if (faction?.AllUnits == null) continue;
						var units = faction.AllUnits.ToList();
						var ours = units.FirstOrDefault(x => x != null && x.Key == u.Key);
						if (ours != null)
						{
							if (_added.Contains(ours)) { ours.Prevalence = u.CampaignPrevalence(); ours.BlockUntilMissionsCompleted = u.CampaignAfterMissions(); }
							continue;
						}
						var armyUnit = units.FirstOrDefault(x => x != null && x.Key == u.ArmyFrom());
						if (armyUnit == null) continue;
						var template = units.FirstOrDefault(x => x != null && x.Key == u.BaseUnit()) ?? armyUnit;
						var entry = new TheaterUnitMetadata
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
						};
						units.Add(entry);
						_added.Add(entry);
						faction.AllUnits = units.ToArray();
					}
				}
		}

		// The customizer has its own registry reference, not necessarily the one I added to.
		[HarmonyPatch(typeof(MissionConfigMenuPanel), "GetUnitKeysByClass")]
		internal static class MenuListPatch
		{
			static readonly AccessTools.FieldRef<MissionConfigMenuPanel, UnitPrefabLookupScriptable> AllUnits =
				Reflect.FieldRefAccess<MissionConfigMenuPanel, UnitPrefabLookupScriptable>("_allUnitsScriptable");
			static readonly System.Reflection.FieldInfo Cache = Reflect.Field(typeof(MissionConfigMenuPanel), "_unitKeysByClassCached");

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
			static readonly AccessTools.FieldRef<Unit, string> UniqueName = Reflect.FieldRefAccess<Unit, string>("_uniqueName");
			static readonly AccessTools.FieldRef<Unit, string> FriendlyNameRef = Reflect.FieldRefAccess<Unit, string>("_friendlyName");

			static void Postfix(string __0, IUnit __result)
			{
				string uniqueName = __0;
				var u = Units.FirstOrDefault(x => string.Equals(uniqueName, x.Key, StringComparison.OrdinalIgnoreCase));
				if (u == null) return;
				if (!(__result is Vehicle vehicle)) { MelonLogger.Warning($"{u.Key} spawn returned no Vehicle."); return; }
				// Now, not later: other mods convert the base unit by name at PlayerReady.
				UniqueName(vehicle) = u.Key;
				FriendlyNameRef(vehicle) = u.FriendlyName();
				u.OnSpawn?.Invoke(vehicle);
				ArmShield(vehicle, u);
			}
		}

		static readonly List<(Vehicle v, UnitDef u)> _shielded = new List<(Vehicle, UnitDef)>();
		static StateController _shieldArmedFor;
		static int _shieldCount;

		static string ShieldNameOf(UnitDef u) => u.ShieldName?.Invoke() ?? u.Key;

		static void ArmShield(Vehicle v, UnitDef u)
		{
			if (u.ShieldFrom == null) return;
			if (NameShield.Form(u.FriendlyName(), u.ShieldFrom, ShieldNameOf(u)) == null) return;
			if (StateController.IsGameStateTriggered(GameState.SkyReady)) return;
			var sc = StateController.Instance;
			if (sc == null) return;
			if (_shieldArmedFor != sc)
			{
				_shielded.Clear();
				if (StateController.RunOrDefer(GameState.SkyReady, ShieldOn, GameStatePriority.High) != GameStateInvocationStatus.Deferred) return;
				StateController.RunOrDefer(GameState.SkyReady, ShieldOff, GameStatePriority.Low);
				_shieldArmedFor = sc;
			}
			_shielded.Add((v, u));
		}

		static readonly AccessTools.FieldRef<Unit, string> ShieldNameRef = Reflect.FieldRefAccess<Unit, string>("_friendlyName");

		static IEnumerator ShieldOn(GameState _)
		{
			_shieldCount = 0;
			foreach (var (v, u) in _shielded)
				if (v != null && v.FriendlyName == u.FriendlyName()) { ShieldNameRef(v) = ShieldNameOf(u); _shieldCount++; }
			yield break;
		}

		static IEnumerator ShieldOff(GameState _)
		{
			int back = 0;
			foreach (var (v, u) in _shielded)
				if (v != null && v.FriendlyName == ShieldNameOf(u)) { ShieldNameRef(v) = u.FriendlyName(); back++; }
			if (_shielded.Count > 0) MelonLogger.Msg($"Name shield: {_shieldCount} unit(s) named '{ShieldNameOf(_shielded[0].u)}' over SkyReady, {back} given back '{_shielded[0].u.FriendlyName()}'.");
			_shielded.Clear(); _shieldArmedFor = null;
			yield break;
		}
	}
}
