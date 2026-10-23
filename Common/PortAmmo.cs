using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Mission;
using GHPC.State;
using GHPC.UI.Menu;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using TMPro;

namespace T90A.Common
{
	internal static class PortAmmo
	{
		internal sealed class Round
		{
			public string Id;
			public string Name;
			public string Source;
			public Action<AmmoType> Set;
			public float Prevalence = 1f;
			public int AfterMissions;
			public bool ScaleWithLogistics;
		}

		internal sealed class Rack
		{
			public int Index;
			public int[] Counts;
			public int[] MissileSlots = new int[0];
		}

		internal sealed class Settings
		{
			public string Tag = "Port";
			public string UnitKey;
			public string Prefix;
			public string SourceUnit = "T80B";
			public Round[] Apfsds = new Round[0];
			public Func<string> DefaultApfsds;
			public string ApfsdsListName;
			public string MenuSuffix;
			public string FixedHeat = "clip_3BK18M";
			public Round Missile;
			public string[] EraDefeating = new string[0];
			public int[] TotalCounts;
			public Rack[] Racks = new Rack[0];
			public bool DropOtherRacks = false;
			public Vector3 GuidancePosition;
			public string GuidanceName = "guidance computer", BeamName = "guidance beam";
			public string CoaxFeed = "PKT";
			public int CoaxRounds, CoaxBelt = 250;
		}

		internal static Settings S;

		internal static readonly Dictionary<string, AmmoClipCodexScriptable> Apfsds = new Dictionary<string, AmmoClipCodexScriptable>();
		internal static AmmoClipCodexScriptable Heat;
		internal static AmmoClipCodexScriptable Missile;
		internal static AmmoLogisticsScriptable ApfsdsSet;
		static bool _built;
		static readonly MethodInfo RegisterAllBallistics = AccessTools.Method(typeof(LoadoutManager), "RegisterAllBallistics");
		static readonly FieldInfo FeedMissileGuidance = AccessTools.Field(typeof(AmmoFeed), "_missileGuidance");
		static readonly MethodInfo FeedGuidanceStopped = AccessTools.Method(typeof(AmmoFeed), "MissileGuidanceStopped");

		internal static bool Build()
		{
			if (_built) return true;
			if (S == null) return false;
			var list = OwnLoad(UnitRegistry.Find(S.SourceUnit)?.BaseAmmoClipsReference);
			var clips = list?.AmmoClips ?? new AmmoClipCodexScriptable[0];
			AmmoType SourceOf(Round r) => clips.FirstOrDefault(c => c.name == r.Source)?.ClipType.MinimalPattern[0].AmmoType;
			Heat = clips.FirstOrDefault(c => c.name == S.FixedHeat);
			var missing = S.Apfsds.Concat(S.Missile != null ? new[] { S.Missile } : new Round[0]).Where(r => SourceOf(r) == null).Select(r => r.Source).ToList();
			if (Heat == null) missing.Add(S.FixedHeat);
			if (missing.Count > 0 || S.Apfsds.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} ammo: vanilla {string.Join(", ", missing)} not loaded yet; keeping base ammo.");
				return false;
			}
			foreach (var r in S.Apfsds) Apfsds[r.Id] = MakeClip(r, SourceOf(r));
			if (S.Missile != null)
			{
				Missile = MakeClip(S.Missile, SourceOf(S.Missile));
			}
			ApfsdsSet = MakeApfsdsSet();
			_built = true;
			return true;
		}

		static readonly Dictionary<object, VehicleAmmoListScriptable> ownLists = new Dictionary<object, VehicleAmmoListScriptable>();

		static VehicleAmmoListScriptable OwnLoad(AssetReference reference)
		{
			if (reference == null || string.IsNullOrEmpty(reference.AssetGUID)) return null;
			var key = reference.RuntimeKey;
			if (!ownLists.TryGetValue(key, out var list) || list == null)
				ownLists[key] = list = Addressables.LoadAssetAsync<VehicleAmmoListScriptable>(key).WaitForCompletion();
			return list;
		}

		static AmmoLogisticsScriptable MakeApfsdsSet()
		{
			var set = ScriptableObject.CreateInstance<AmmoLogisticsScriptable>();
			set.name = $"ammo_logistics_{S.Prefix}_apfsds";
			set.hideFlags = HideFlags.DontUnloadUnusedAsset;
			set.AmmoLogistics = new AmmoReplacementOrder
			{
				Name = S.ApfsdsListName,
				AmmoOptions = S.Apfsds.Select(r => new AmmoReplacementOrder.AmmoTypeLogistics
				{
					AmmoType = Apfsds[r.Id], Prevalence = r.Prevalence, BlockUntilMissionsCompleted = r.AfterMissions,
					LogisticsModifierCurve = r.ScaleWithLogistics ? AnimationCurve.Linear(0f, 0f, 1f, 1f) : AnimationCurve.Constant(0f, 1f, 1f), AllowInCampaign = true,
				}).ToArray(),
			};
			return set;
		}

		static AmmoClipCodexScriptable heatStandIn;

		static AmmoClipCodexScriptable HeatStandIn()
		{
			if (heatStandIn == null)
			{
				heatStandIn = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
				heatStandIn.name = $"clip_{S.Prefix}_{S.FixedHeat.Replace("clip_", "")}_fixed";
				heatStandIn.hideFlags = HideFlags.DontUnloadUnusedAsset;
				heatStandIn.ClipType = new AmmoType.AmmoClip { Name = Heat.ClipType.Name, Capacity = 1, MinimalPattern = new AmmoCodexScriptable[0] };
			}
			return heatStandIn;
		}

		internal static void HideFixedHeat(AmmoClipCodexScriptable[] clips)
		{
			if (Heat == null || clips == null) return;
			int i = Array.IndexOf(clips, Heat);
			if (i >= 0) clips[i] = HeatStandIn();
		}

		internal static void RestoreFixedHeat(AmmoClipCodexScriptable[] clips)
		{
			if (heatStandIn == null || clips == null) return;
			int i = Array.FindIndex(clips, c => ReferenceEquals(c, heatStandIn));
			if (i >= 0) clips[i] = Heat;
		}

		internal static AmmoClipCodexScriptable DefaultApfsds(bool warn)
		{
			string id = S.DefaultApfsds?.Invoke();
			if (Apfsds.TryGetValue(id ?? "", out var ap)) return ap;
			if (warn) MelonLogger.Warning($"{S.Tag} ammo: unknown APFSDS '{id}', using {S.Apfsds[0].Id}.");
			return Apfsds[S.Apfsds[0].Id];
		}

		static List<AmmoClipCodexScriptable> PortList(IEnumerable<AmmoClipCodexScriptable> baseClips, AmmoClipCodexScriptable ap)
		{
			var list = baseClips.Take(3).ToList();
			list[0] = ap;
			list[1] = Heat;
			if (Missile != null) list.Add(Missile);
			return list;
		}

		internal static bool IsPort(string unitKey) => S != null && string.Equals(unitKey, S.UnitKey, StringComparison.OrdinalIgnoreCase);

		internal static List<AmmoLogisticsScriptable> SetsForMenu(AssetReference baseList, AmmoLogisticsScriptable[] vanilla, out List<int> preferred)
		{
			preferred = null;
			if (!Build()) return null;
			var list = baseList?.Asset as VehicleAmmoListScriptable ?? OwnLoad(baseList);
			if (list?.AmmoClips == null || list.AmmoClips.Length < 3) return null;
			var clips = PortList(list.AmmoClips, DefaultApfsds(warn: false));
			clips.Remove(Heat);
			var sets = new List<AmmoLogisticsScriptable>();
			preferred = new List<int>();
			foreach (var set in new[] { ApfsdsSet }.Concat(vanilla ?? new AmmoLogisticsScriptable[0]))
			{
				var options = set?.AmmoLogistics?.AmmoOptions;
				if (options == null || sets.Contains(set)) continue;
				for (int j = 0; j < options.Length; j++)
					if (options[j] != null && clips.Contains(options[j].AmmoType)) { sets.Add(set); preferred.Add(j); break; }
			}
			return sets;
		}

		static AmmoClipCodexScriptable MakeClip(Round r, AmmoType source)
		{
			var ammo = new AmmoType();
			foreach (var f in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (!f.IsLiteral && !f.IsInitOnly) f.SetValue(ammo, f.GetValue(source));
			ammo.Name = r.Name;
			ammo.ArmorOptimizations = new AmmoType.ArmorOptimization[0];
			r.Set(ammo);

			var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
			codex.name = $"ammo_{S.Prefix}_{r.Id}";
			codex.AmmoType = ammo;

			var clip = new AmmoType.AmmoClip { Capacity = 1, Name = r.Name, MinimalPattern = new[] { codex } };
			var clipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
			clipCodex.name = $"clip_{S.Prefix}_{r.Id}";
			clipCodex.ClipType = clip;

			if (source.VisualModel != null)
			{
				var visual = Object.Instantiate(source.VisualModel);
				visual.name = r.Id + " visual";
				Object.DontDestroyOnLoad(visual);
				var stored = visual.GetComponent<AmmoStoredVisual>();
				if (stored != null) { stored.AmmoType = ammo; stored.AmmoScriptable = codex; }
				ammo.VisualModel = visual;
			}
			return clipCodex;
		}

		internal static void ApplyAtSpawn(Vehicle vehicle)
		{
			if (!Build()) return;
			var lm = vehicle.GetComponent<LoadoutManager>();
			if (lm == null) return;
			var weapon = vehicle.GetComponent<WeaponsManager>().Weapons[0].Weapon;
			var list = PortList(lm.LoadedAmmoList.AmmoClips, DefaultApfsds(warn: true));
			lm.LoadedAmmoList.AmmoClips = list.ToArray();
			lm.TotalAmmoCounts = S.TotalCounts.Take(list.Count).ToArray();
			int missile = Missile != null ? list.IndexOf(Missile) : -1;
			var kept = new List<LoadoutManager.RackLoadout>();
			for (int i = 0; i < lm.RackLoadouts.Length; i++)
			{
				var rl = lm.RackLoadouts[i];
				var r = S.Racks.FirstOrDefault(x => x.Index == i);
				if (r == null) { if (!S.DropOtherRacks) kept.Add(rl); continue; }
				if (r.Counts != null) rl.AmmoCounts = r.Counts.Take(list.Count).ToArray();
				if (missile >= 0 && r.MissileSlots.Length > 0)
					rl.FixedChoices = r.MissileSlots.Select(s => new LoadoutManager.RackLoadoutFixedChoice { AmmoClipIndex = missile, RackSlotIndex = s }).ToArray();
				kept.Add(rl);
			}
			if (kept.Count < lm.RackLoadouts.Length) string.Join(", ", lm.RackLoadouts.Except(kept).Select(x => x.Rack != null ? x.Rack.name : "?"));
			lm.RackLoadouts = kept.ToArray();
			if (missile >= 0) AddGuidanceUnit(weapon);
			RegisterAllBallistics.Invoke(lm, null);
		}

		static readonly FieldInfo InitialClipCounts = AccessTools.Field(typeof(GHPC.Weapons.AmmoRack), "_initialClipCounts");

		internal static void ApplyCoaxAtSpawn(Vehicle vehicle)
		{
			if (S == null || S.CoaxRounds <= 0) return;
			var feed = vehicle.GetComponentsInChildren<AmmoFeed>(true).FirstOrDefault(f => f.name.Contains(S.CoaxFeed));
			var rack = feed?.ReadyRack;
			if (rack == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} coax {S.CoaxFeed}: feed or ready rack NOT FOUND; keeping the base load.");
				return;
			}
			var old = rack.ClipTypes[0];
			int belts = Mathf.Max(1, Mathf.RoundToInt(S.CoaxRounds / (float)S.CoaxBelt));
			var belt = new AmmoType.AmmoClip { Name = old.Name, MinimalPattern = old.MinimalPattern, Capacity = S.CoaxBelt };
			rack.ClipTypes = new[] { belt };
			InitialClipCounts.SetValue(rack, new[] { belts });
			rack.StoredClips.Clear();
			for (int i = 0; i < belts; i++) rack.StoredClips.Add(belt);
		}

		static void AddGuidanceUnit(WeaponSystem weapon)
		{
			if (weapon.GuidanceUnit != null) return;
			var parent = weapon.FCS.transform.parent;
			var go = new GameObject(S.GuidanceName);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = S.GuidancePosition;
			go.transform.localEulerAngles = Vector3.zero;
			var mgu = go.AddComponent<MissileGuidanceUnit>();
			var beam = new GameObject(S.BeamName).AddComponent<GuidanceBeam>();
			beam.transform.SetParent(weapon.FCS.GetComponentInParent<Vehicle>()?.transform ?? parent, false);
			beam.Fcs = weapon.FCS;
			beam.Gun = go.transform;
			beam.Sync();
			mgu.AimElement = beam.transform;
			weapon.GuidanceUnit = mgu;
			weapon.Feed.ReloadDuringMissileTracking = false;
			FeedMissileGuidance.SetValue(weapon.Feed, mgu);
			mgu.GuidanceStopped += (Action)Delegate.CreateDelegate(typeof(Action), weapon.Feed, FeedGuidanceStopped);
			weapon.FireWhileGuidingMissile = false;
		}
	}

	[HarmonyPatch(typeof(MissionConfigMenuPanel), "GetAmmoSetsByUnit")]
	internal static class AmmoMenuSetsPatch
	{
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, AmmoLogisticsSetScriptable> AllSets =
			AccessTools.FieldRefAccess<MissionConfigMenuPanel, AmmoLogisticsSetScriptable>("_allAmmoSetsScriptable");
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, UnitPrefabLookupScriptable> AllUnits =
			AccessTools.FieldRefAccess<MissionConfigMenuPanel, UnitPrefabLookupScriptable>("_allUnitsScriptable");

		static void Postfix(MissionConfigMenuPanel __instance, string unitKey, ref List<AmmoLogisticsScriptable> __result, ref List<int> preferredIndices)
		{
			if (!PortAmmo.IsPort(unitKey)) return;
			try
			{
				var sets = PortAmmo.SetsForMenu(AllUnits(__instance)?.GetBaseAmmoClipLoadout(unitKey), AllSets(__instance)?.AllAmmoReplacementOrders, out var preferred);
				if (sets != null) { __result = sets; preferredIndices = preferred; }
			}
			catch (Exception e) { MelonLogger.Error($"{PortAmmo.S.Tag} ammo choice for the mission customizer failed: " + e); }
		}
	}

	[HarmonyPatch(typeof(MissionConfigMenuPanel), nameof(MissionConfigMenuPanel.AddAmmoEntry))]
	internal static class AmmoMenuLabelPatch
	{
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, List<GameObject>> Entries =
			AccessTools.FieldRefAccess<MissionConfigMenuPanel, List<GameObject>>("_ammoDataObjects");
		static readonly AccessTools.FieldRef<MissionConfigAmmoSelectionEntry, TextMeshProUGUI> Label =
			AccessTools.FieldRefAccess<MissionConfigAmmoSelectionEntry, TextMeshProUGUI>("_label");

		static void Postfix(MissionConfigMenuPanel __instance, AmmoLogisticsScriptable ammoLogisticsScriptable)
		{
			if (ammoLogisticsScriptable == null || ammoLogisticsScriptable != PortAmmo.ApfsdsSet || string.IsNullOrEmpty(PortAmmo.S?.MenuSuffix)) return;
			var entry = Entries(__instance)?.LastOrDefault()?.GetComponent<MissionConfigAmmoSelectionEntry>();
			if (entry == null || entry.AmmoSet != ammoLogisticsScriptable) return;
			var label = Label(entry);
			if (label != null && !label.text.EndsWith(")")) label.text += PortAmmo.S.MenuSuffix;
		}
	}

	[HarmonyPatch(typeof(LoadoutManager), "DoFlexMissionLogisticsChanges")]
	internal static class AmmoCampaignPatch
	{
		static void Prefix(LoadoutManager __instance)
		{
			if (PortAmmo.ApfsdsSet == null || !IsPort(__instance)) return;
			PortAmmo.HideFixedHeat(__instance.LoadedAmmoList?.AmmoClips);
			if (!DynamicMissionComposer.CampaignMode) return;
			var set = PortAmmo.ApfsdsSet;
			foreach (var f in new[] { DynamicMissionLauncher.CurrentFactionBlue, DynamicMissionLauncher.CurrentFactionRed })
				if (f != null && (f.AmmoLogisticsData == null || !f.AmmoLogisticsData.Contains(set)))
					f.AmmoLogisticsData = (f.AmmoLogisticsData ?? new AmmoLogisticsScriptable[0]).Concat(new[] { set }).ToArray();
		}

		static Exception Finalizer(LoadoutManager __instance, Exception __exception)
		{
			if (PortAmmo.ApfsdsSet == null || !IsPort(__instance)) return __exception;
			var clips = __instance.LoadedAmmoList?.AmmoClips;
			PortAmmo.RestoreFixedHeat(clips);
			return __exception;
		}

		static bool IsPort(LoadoutManager lm) => PortAmmo.IsPort(lm.GetComponent<Vehicle>()?.UniqueName);
	}

	[HarmonyPatch(typeof(AmmoReplacementOrder), nameof(AmmoReplacementOrder.SelectAmmoType))]
	internal static class AmmoCampaignUnlockPatch
	{
		static void Prefix(AmmoReplacementOrder __instance, bool isCampaignMode)
		{
			if (!isCampaignMode || PortAmmo.ApfsdsSet == null || __instance != PortAmmo.ApfsdsSet.AmmoLogistics) return;
			int done = CampaignSaveState.ActiveSaveData?.TotalMissionsCompleted ?? 0;
			foreach (var o in __instance.AmmoOptions) o.AllowInCampaign = done >= o.BlockUntilMissionsCompleted;
		}
	}

	[HarmonyPatch(typeof(LiveRound), "penCheck")]
	internal static class TandemEraPatch
	{
		static readonly AccessTools.FieldRef<LiveRound, bool> Sacrificed = AccessTools.FieldRefAccess<LiveRound, bool>("_sacrificedWarhead");

		static void Prefix(LiveRound __instance, Collider cObject)
		{
			AmmoType info = __instance.Info;
			if (info == null || !info.Tandem || __instance.IsSpall || cObject == null || PortAmmo.S == null) return;
			if (Array.IndexOf(PortAmmo.S.EraDefeating, info.Name) < 0 || Sacrificed(__instance)) return;
			if (!cObject.TryGetComponent<IArmor>(out var armor) || !armor.IsEra || armor.IsDetonated) return;
			if (armor.Unit != null && __instance.ShotInfo != null && __instance.ShotInfo.Shooter == armor.Unit) return;

			armor.Detonate();
			Sacrificed(__instance) = true;
		}
	}
}
