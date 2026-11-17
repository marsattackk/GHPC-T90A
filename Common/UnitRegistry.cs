using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	// Gets my unit into the unit list, the mission menu, the spawner and the campaign, and runs its spawn code. A port
	// fills in a UnitDef.
	// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
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
#pragma warning restore CS0649

	internal sealed class CampaignConfig
	{
		internal MelonPreferences_Entry<bool> InRoster;
		internal MelonPreferences_Entry<float> Prevalence;
		internal MelonPreferences_Entry<int> AfterMissions;

		internal static CampaignConfig Create(MelonPreferences_Category cfg, string friendlyName, float prevalence, int afterMissions)
			=> Create(cfg, friendlyName, prevalence, afterMissions, "Soviet");

		internal static CampaignConfig Create(MelonPreferences_Category cfg, string friendlyName, float prevalence, int afterMissions, string side)
			=> new CampaignConfig
			{
				InRoster = cfg.CreateEntry(
					"Add to campaign roster",
					true,
					description: $"Adds the {friendlyName} to the {side} campaign roster."),
				Prevalence = cfg.CreateEntry(
					"Campaign prevalence",
					prevalence,
					description: "How common it is compared to other tanks (1 = as common as a typical tank)."),
				AfterMissions = cfg.CreateEntry("Campaign unlock after missions", afterMissions),
			};

		internal void Use(UnitDef u)
		{
			u.InCampaign = () => InRoster.Value;
			u.CampaignPrevalence = () => Prevalence.Value;
			u.CampaignAfterMissions = () => AfterMissions.Value;
		}
	}

	internal static partial class UnitRegistry
	{
		internal static readonly List<UnitDef> Units = new List<UnitDef>();
		static bool _registered, _running;

		internal static void Add(UnitDef u) => Units.Add(u);

		internal static UnitPrefabLookupScriptable Lookup => Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>().FirstOrDefault();

		internal static UnitPrefabLookupScriptable.UnitPrefabMetadata Find(string key) => FindIn(Lookup, key);

		static UnitPrefabLookupScriptable.UnitPrefabMetadata FindIn(UnitPrefabLookupScriptable lookup, string key) =>
			lookup?.AllUnits?.FirstOrDefault(m => m != null
				&& string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));

		internal static void TryRegister()
		{
			if (_registered || _running) return;
			_running = true;
			MelonCoroutines.Start(RegisterRoutine());
		}

		static IEnumerator RegisterRoutine()
		{
			float started = Time.realtimeSinceStartup;
			bool warned = false;
			while (!_registered)
			{
				bool any = false;
				foreach (var lookup in Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>())
					any |= EnsureRegistered(lookup);
				if (any)
				{
					_registered = true;
					break;
				}
				if (Time.realtimeSinceStartup - started < 60f)
				{
					yield return null;
					continue;
				}
				if (!warned)
				{
					warned = true;
					string keys = string.Join(", ", Units.Where(c => c.Enabled()).Select(x => x.Key));
					MelonLogger.Warning($"{keys}: not added to the unit list after a minute (base unit not found); still trying.");
				}
				yield return new WaitForSecondsRealtime(1f);
			}
			_running = false;
		}

		static AssetReference OwnReference(AssetReference r) =>
			r == null ? null : new AssetReference(r.AssetGUID) { SubObjectName = r.SubObjectName };

		internal static bool EnsureRegistered(UnitPrefabLookupScriptable lookup)
		{
			if (lookup?.AllUnits == null) return false;
			bool all = true;
			foreach (var definition in Units.Where(c => c.Enabled()))
			{
				if (FindIn(lookup, definition.Key) != null) continue;
				var baseMeta = FindIn(lookup, definition.BaseUnit());
				if (baseMeta?.PrefabReference == null)
				{
					all = false;
					continue;
				}
				var armyMeta = FindIn(lookup, definition.ArmyFrom()) ?? baseMeta;
				var units = lookup.AllUnits.ToList();
				units.Add(new UnitPrefabLookupScriptable.UnitPrefabMetadata
				{
					Name = definition.Key,
					FriendlyName = definition.FriendlyName(),
					// Shared on purpose. The spawner loads each reference once.
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
				if (spawner != null) Reflect.Field(typeof(UnitSpawner), "_prefabLookupCached")?.SetValue(spawner, null);
				MenuListPatch.ClearCache();
			}
			return all;
		}

		// A campaign save holds no unit list. Each mission draws from AllUnits, so entries can come and go.
		static readonly HashSet<TheaterUnitMetadata> _added = new HashSet<TheaterUnitMetadata>();
		static readonly HashSet<string> _notFielded = new HashSet<string>();

		internal static void StartCampaignRoster()
		{
			if (_added.Count > 0 || Units.Any(d => d.Enabled() && d.InCampaign()))
				MelonCoroutines.Start(UpdateCampaignRoster());
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
					var gone = faction.AllUnits
						.Where(m => m != null
							&& _added.Contains(m)
							&& !Units.Any(d => d.Key == m.Key && d.Enabled() && d.InCampaign()))
						.ToList();
					if (gone.Count == 0) continue;
					faction.AllUnits = faction.AllUnits.Except(gone).ToArray();
					foreach (var metadata in gone) _added.Remove(metadata);
				}
			}

			foreach (var definition in Units.Where(c => c.Enabled() && c.InCampaign()))
			{
				bool fielded = false;
				foreach (var theater in meta.AllTheaters)
				{
					if (theater?.Factions == null) continue;
					foreach (var faction in theater.Factions)
					{
						if (faction?.AllUnits == null) continue;
						var units = faction.AllUnits.ToList();
						var ours = units.FirstOrDefault(m => m != null && m.Key == definition.Key);
						if (ours != null)
						{
							fielded = true;
							if (_added.Contains(ours))
							{
								ours.Prevalence = definition.CampaignPrevalence();
								ours.BlockUntilMissionsCompleted = definition.CampaignAfterMissions();
							}
							continue;
						}
						var armyUnit = units.FirstOrDefault(m => m != null && m.Key == definition.ArmyFrom());
						if (armyUnit == null) continue;
						fielded = true;
						var template = units.FirstOrDefault(m => m != null && m.Key == definition.BaseUnit())
							?? armyUnit;
						var entry = new TheaterUnitMetadata
						{
							Key = definition.Key,
							FriendlyName = definition.FriendlyName(),
							Type = armyUnit.Type,
							Class = armyUnit.Class,
							Tier = armyUnit.Tier,
							SpawnRatio = armyUnit.SpawnRatio,
							Prevalence = definition.CampaignPrevalence(),
							BlockUntilMissionsCompleted = definition.CampaignAfterMissions(),
							LogisticsModifierCurve = template.LogisticsModifierCurve,
						};
						units.Add(entry);
						_added.Add(entry);
						faction.AllUnits = units.ToArray();
					}
				}
				if (!fielded && _notFielded.Add(definition.Key))
					MelonLogger.Msg($"{definition.FriendlyName()}: no campaign side fields the {definition.ArmyFrom()}, so it is not in the campaign roster.");
			}
		}

		// The customizer has its own registry reference, not necessarily the one I added to.
		[HarmonyPatch(typeof(MissionConfigMenuPanel), "GetUnitKeysByClass")]
		internal static class MenuListPatch
		{
			static readonly AccessTools.FieldRef<MissionConfigMenuPanel, UnitPrefabLookupScriptable> AllUnits =
				Reflect.FieldRefAccess<MissionConfigMenuPanel, UnitPrefabLookupScriptable>("_allUnitsScriptable");
			static readonly FieldInfo Cache = Reflect.Field(
				typeof(MissionConfigMenuPanel),
				"_unitKeysByClassCached");

			internal static void ClearCache() => (Cache?.GetValue(null) as IDictionary)?.Clear();

			static void Prefix(MissionConfigMenuPanel __instance)
			{
				if (AllUnits == null) return;
				var lookup = AllUnits(__instance);
				if (lookup?.AllUnits == null) return;
				if (Units.Any(d => d.Enabled()
					&& !lookup.AllUnits.Any(m => m != null && m.Name == d.Key)))
					EnsureRegistered(lookup);
			}
		}

		// Re-added at the spawner, not only at the menu: a mission's spawner can hold a list without our units.

		[HarmonyPatch(typeof(UnitSpawner), "GetMetadataByUniqueName")]
		internal static class LookupPatch
		{
			static readonly FieldInfo CacheField = Reflect.Field(typeof(UnitSpawner), "_prefabLookupCached");
			static readonly HashSet<int> _told = new HashSet<int>();

			static void Prefix(UnitSpawner __instance, string __0)
			{
				var definition = Units.FirstOrDefault(c => string.Equals(__0, c.Key, StringComparison.OrdinalIgnoreCase));
				if (definition == null || !definition.Enabled() || __instance == null) return;
				var lookup = __instance.PrefabLookup;
				if (lookup?.AllUnits == null) return;
				bool listed = FindIn(lookup, definition.Key) != null;
				if (!listed) EnsureRegistered(lookup);
				var cache = CacheField?.GetValue(__instance) as IDictionary;
				bool stale = cache != null && !cache.Contains(definition.Key);
				if (stale || !listed) CacheField?.SetValue(__instance, null);
				if ((stale || !listed) && _told.Add(__instance.GetInstanceID()))
					MelonLogger.Msg($"{definition.Key}: the unit spawner's list {(listed ? "had it but its name cache did not" : $"'{lookup.name}' lacked it")}; added again before the spawn.");
			}
		}

		[HarmonyPatch(
			typeof(UnitSpawner),
			nameof(UnitSpawner.SpawnUnit),
			new[] { typeof(string), typeof(UnitMetaData), typeof(WaypointHolder), typeof(Transform) })]
		internal static class SpawnPatch
		{
			static readonly AccessTools.FieldRef<Unit, string> UniqueName = Reflect.FieldRefAccess<Unit, string>("_uniqueName");
			static readonly AccessTools.FieldRef<Unit, string> FriendlyNameRef = Reflect.FieldRefAccess<Unit, string>("_friendlyName");

			static void Postfix(string __0, IUnit __result)
			{
				string uniqueName = __0;
				var definition = Units.FirstOrDefault(c => string.Equals(
					uniqueName,
					c.Key,
					StringComparison.OrdinalIgnoreCase));
				if (definition == null) return;
				if (!(__result is Vehicle vehicle))
				{
					MelonLogger.Warning($"{definition.Key} spawn returned no Vehicle.");
					return;
				}
				// Now, not later: other mods convert the base unit by name at PlayerReady.
				if (UniqueName != null) UniqueName(vehicle) = definition.Key;
				if (FriendlyNameRef != null) FriendlyNameRef(vehicle) = definition.FriendlyName();
				// A throw out of this postfix reaches GHPC's mission spawner: the conversion's failure stays here.
				try { definition.OnSpawn?.Invoke(vehicle); }
				catch (Exception exception)
				{
					MelonLogger.Error($"{definition.Key} conversion failed at spawn, the unit may be half converted: {exception}");
				}
				ArmShield(vehicle, definition);
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
			var controller = StateController.Instance;
			if (controller == null) return;
			if (_shieldArmedFor != controller)
			{
				_shielded.Clear();
				if (StateController.RunOrDefer(GameState.SkyReady, ShieldOn, GameStatePriority.High) != GameStateInvocationStatus.Deferred)
					return;
				StateController.RunOrDefer(GameState.SkyReady, ShieldOff, GameStatePriority.Low);
				_shieldArmedFor = controller;
			}
			_shielded.Add((v, u));
		}

		static readonly AccessTools.FieldRef<Unit, string> ShieldNameRef = Reflect.FieldRefAccess<Unit, string>("_friendlyName");

		static IEnumerator ShieldOn(GameState _)
		{
			_shieldCount = 0;
			foreach (var (vehicle, definition) in _shielded)
				if (vehicle != null && vehicle.FriendlyName == definition.FriendlyName())
				{
					ShieldNameRef(vehicle) = ShieldNameOf(definition);
					_shieldCount++;
				}
			yield break;
		}

		static IEnumerator ShieldOff(GameState _)
		{
			int back = 0;
			foreach (var (vehicle, definition) in _shielded)
				if (vehicle != null && vehicle.FriendlyName == ShieldNameOf(definition))
				{
					ShieldNameRef(vehicle) = definition.FriendlyName();
					back++;
				}
			if (_shielded.Count > 0)
				MelonLogger.Msg($"Name shield: {_shieldCount} unit(s) named '{ShieldNameOf(_shielded[0].u)}' over SkyReady, {back} given back '{_shielded[0].u.FriendlyName()}'.");
			_shielded.Clear();
			_shieldArmedFor = null;
			yield break;
		}
	}
}
