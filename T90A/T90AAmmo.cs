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
using T90A.Common;
using TMPro;

namespace T90A
{
	internal static class T90AAmmo
	{
		internal static int[] Counts = { 22, 8, 8, 4 };

		internal static readonly int[] CarouselCountsWithMissile = { 12, 4, 4, 0 };

		internal static readonly Dictionary<string, AmmoClipCodexScriptable> Apfsds = new Dictionary<string, AmmoClipCodexScriptable>();
		internal static AmmoClipCodexScriptable Heat;
		internal static AmmoClipCodexScriptable Missile;

		internal static AmmoLogisticsScriptable ApfsdsSet;
		internal const int Svinets2AfterMissions = 4;
		internal const float Svinets2Prevalence = 0.25f;
		internal static readonly HashSet<string> EraDefeatingRounds = new HashSet<string> { "9M119M Invar" };
		static readonly PropertyInfo StoredClips = AccessTools.Property(typeof(GHPC.Weapons.AmmoRack), "StoredClips");
		static readonly MethodInfo RegisterAllBallistics = AccessTools.Method(typeof(LoadoutManager), "RegisterAllBallistics");
		static readonly FieldInfo FeedMissileGuidance = AccessTools.Field(typeof(AmmoFeed), "_missileGuidance");
		static readonly MethodInfo FeedGuidanceStopped = AccessTools.Method(typeof(AmmoFeed), "MissileGuidanceStopped");

		internal static bool Build()
		{
			if (Missile != null) return true;
			var list = OwnLoad(UnitRegistry.Find("T80B")?.BaseAmmoClipsReference);
			var clips = list?.AmmoClips ?? new AmmoClipCodexScriptable[0];
			var src3bm32 = clips.FirstOrDefault(c => c.name == "clip_3BM32");
			var src9m112 = clips.FirstOrDefault(c => c.name == "clip_9M112M");
			Heat = clips.FirstOrDefault(c => c.name == "clip_3BK18M");
			if (src3bm32 == null || src9m112 == null || Heat == null)
			{
				MelonLogger.Warning("T-90A ammo: vanilla 3BM32, 9M112M or 3BK18M not loaded yet; keeping base ammo.");
				return false;
			}
			AmmoType ap = src3bm32.ClipType.MinimalPattern[0].AmmoType;
			AmmoType kobra = src9m112.ClipType.MinimalPattern[0].AmmoType;

			Apfsds["3BM42"] = MakeClip("3BM42", "3BM42 APFSDS-T", ap, a =>
			{
				a.RhaPenetration = 578f;
				a.Mass = 4.85f;
				a.MuzzleVelocity = 1700f;
				a.Coeff = 0.16f;
			});
			Apfsds["3BM60"] = MakeClip("3BM60", "3BM60 APFSDS-T", ap, a =>
			{
				a.RhaPenetration = 693f;
				a.Mass = 5.12f;
				a.MuzzleVelocity = 1660f;
				a.Coeff = 0.106f;
			});
			Missile = MakeClip("9M119M", "9M119M Invar", kobra, a =>
			{
				a.RhaPenetration = 700f;
				a.Tandem = true;
				a.Mass = 17.2f;
				a.MuzzleVelocity = 284f;
				a.MaximumRange = 5000f;
				a.ArmingDistance = 100f;
				a.RangedFuseTime = 5000f / 284f + 0.3f;
				a.Guidance = AmmoType.GuidanceType.Laser;
			});
			ApfsdsSet = MakeApfsdsSet();
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
			set.name = "ammo_logistics_t90a_apfsds";
			set.hideFlags = HideFlags.DontUnloadUnusedAsset;
			set.AmmoLogistics = new AmmoReplacementOrder
			{
				Name = "125mm APFSDS (T-90A)",
				AmmoOptions = new[]
				{
					new AmmoReplacementOrder.AmmoTypeLogistics { AmmoType = Apfsds["3BM42"], Prevalence = 1f, LogisticsModifierCurve = AnimationCurve.Constant(0f, 1f, 1f), AllowInCampaign = true },
					new AmmoReplacementOrder.AmmoTypeLogistics
					{
						AmmoType = Apfsds["3BM60"], Prevalence = Svinets2Prevalence, BlockUntilMissionsCompleted = Svinets2AfterMissions,
						LogisticsModifierCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f), AllowInCampaign = true,
					},
				},
			};
			return set;
		}

		static AmmoClipCodexScriptable heatStandIn;

		static AmmoClipCodexScriptable HeatStandIn()
		{
			if (heatStandIn == null)
			{
				heatStandIn = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
				heatStandIn.name = "clip_t90a_3BK18M_fixed";
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

		internal static AmmoClipCodexScriptable DefaultApfsds(string apfsds, bool warn)
		{
			if (Apfsds.TryGetValue(apfsds ?? "", out var ap)) return ap;
			if (warn) MelonLogger.Warning("T-90A ammo: unknown APFSDS '" + apfsds + "', using 3BM42.");
			return Apfsds["3BM42"];
		}

		static List<AmmoClipCodexScriptable> T90AList(IEnumerable<AmmoClipCodexScriptable> baseClips, AmmoClipCodexScriptable ap, bool addMissile)
		{
			var list = baseClips.Take(3).ToList();
			list[0] = ap;
			list[1] = Heat;
			if (addMissile) list.Add(Missile);
			return list;
		}

		internal static List<AmmoLogisticsScriptable> SetsForMenu(AssetReference baseList, AmmoLogisticsScriptable[] vanilla, out List<int> preferred)
		{
			preferred = null;
			if (!Build()) return null;
			var list = baseList?.Asset as VehicleAmmoListScriptable ?? OwnLoad(baseList);
			if (list?.AmmoClips == null || list.AmmoClips.Length < 3) return null;
			var clips = T90AList(list.AmmoClips, DefaultApfsds(T90AMod.Apfsds.Value, warn: false), addMissile: true);
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

		static AmmoClipCodexScriptable MakeClip(string id, string displayName, AmmoType source, Action<AmmoType> set)
		{
			var ammo = new AmmoType();
			foreach (var f in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (!f.IsLiteral && !f.IsInitOnly) f.SetValue(ammo, f.GetValue(source));
			ammo.Name = displayName;
			ammo.ArmorOptimizations = new AmmoType.ArmorOptimization[0];
			set(ammo);

			var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
			codex.name = "ammo_t90a_" + id;
			codex.AmmoType = ammo;

			var clip = new AmmoType.AmmoClip { Capacity = 1, Name = displayName, MinimalPattern = new[] { codex } };
			var clipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
			clipCodex.name = "clip_t90a_" + id;
			clipCodex.ClipType = clip;

			if (source.VisualModel != null)
			{
				var visual = Object.Instantiate(source.VisualModel);
				visual.name = id + " visual";
				Object.DontDestroyOnLoad(visual);
				var stored = visual.GetComponent<AmmoStoredVisual>();
				if (stored != null) { stored.AmmoType = ammo; stored.AmmoScriptable = codex; }
				ammo.VisualModel = visual;
			}
			return clipCodex;
		}

		internal static void ApplyAtSpawn(Vehicle vehicle, string apfsds, bool addMissile)
		{
			if (!Build()) return;
			var lm = vehicle.GetComponent<LoadoutManager>();
			if (lm == null) return;
			var weapon = vehicle.GetComponent<WeaponsManager>().Weapons[0].Weapon;
			var list = T90AList(lm.LoadedAmmoList.AmmoClips, DefaultApfsds(apfsds, warn: true), addMissile);
			lm.LoadedAmmoList.AmmoClips = list.ToArray();
			lm.TotalAmmoCounts = Counts.Take(list.Count).ToArray();
			if (addMissile)
			{
				lm.RackLoadouts[0].FixedChoices = MissileSlots(21, 20);
				lm.RackLoadouts[0].AmmoCounts = (int[])CarouselCountsWithMissile.Clone();
				lm.RackLoadouts[2].FixedChoices = MissileSlots(10, 9);
				AddGuidanceUnit(weapon);
			}
			RegisterAllBallistics.Invoke(lm, null);
		}

		internal const int PktBelt = 250;
		static readonly FieldInfo InitialClipCounts = AccessTools.Field(typeof(GHPC.Weapons.AmmoRack), "_initialClipCounts");

		internal static void ApplyCoaxAtSpawn(Vehicle vehicle, int rounds)
		{
			var feed = vehicle.GetComponentsInChildren<AmmoFeed>(true).FirstOrDefault(f => f.name.Contains("PKT"));
			var rack = feed?.ReadyRack;
			if (rack == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0)
			{
				MelonLogger.Warning("T-90A coax PKT: feed or ready rack NOT FOUND; keeping the base load.");
				return;
			}
			var old = rack.ClipTypes[0];
			int belts = Mathf.Max(1, Mathf.RoundToInt(rounds / (float)PktBelt));
			var belt = new AmmoType.AmmoClip { Name = old.Name, MinimalPattern = old.MinimalPattern, Capacity = PktBelt };
			rack.ClipTypes = new[] { belt };
			InitialClipCounts.SetValue(rack, new[] { belts });
			rack.StoredClips.Clear();
			for (int i = 0; i < belts; i++) rack.StoredClips.Add(belt);
		}

		static LoadoutManager.RackLoadoutFixedChoice[] MissileSlots(int a, int b)
		{
			return new[]
			{
				new LoadoutManager.RackLoadoutFixedChoice { AmmoClipIndex = 3, RackSlotIndex = a },
				new LoadoutManager.RackLoadoutFixedChoice { AmmoClipIndex = 3, RackSlotIndex = b },
			};
		}

		static void AddGuidanceUnit(WeaponSystem weapon)
		{
			if (weapon.GuidanceUnit != null) return;
			var parent = weapon.FCS.transform.parent;
			var go = new GameObject("T90A guidance computer");
			go.transform.SetParent(parent, false);
			go.transform.localPosition = new Vector3(-0.727f, 0.4631f, -5.7249f);
			go.transform.localEulerAngles = Vector3.zero;
			var mgu = go.AddComponent<MissileGuidanceUnit>();
			var beam = new GameObject("T90A 1G46 guidance beam").AddComponent<GuidanceBeam>();
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
			if (!string.Equals(unitKey, T90AMod.UnitKey, StringComparison.OrdinalIgnoreCase)) return;
			try
			{
				var sets = T90AAmmo.SetsForMenu(AllUnits(__instance)?.GetBaseAmmoClipLoadout(unitKey), AllSets(__instance)?.AllAmmoReplacementOrders, out var preferred);
				if (sets != null) { __result = sets; preferredIndices = preferred; }
			}
			catch (Exception e) { MelonLogger.Error("T-90A ammo choice for the mission customizer failed: " + e); }
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
			if (ammoLogisticsScriptable == null || ammoLogisticsScriptable != T90AAmmo.ApfsdsSet) return;
			var entry = Entries(__instance)?.LastOrDefault()?.GetComponent<MissionConfigAmmoSelectionEntry>();
			if (entry == null || entry.AmmoSet != ammoLogisticsScriptable) return;
			var label = Label(entry);
			if (label != null && !label.text.EndsWith(")")) label.text += " (T-90A)";
		}
	}

	[HarmonyPatch(typeof(LoadoutManager), "DoFlexMissionLogisticsChanges")]
	internal static class AmmoCampaignPatch
	{
		static void Prefix(LoadoutManager __instance)
		{
			if (T90AAmmo.ApfsdsSet == null || !IsT90A(__instance)) return;
			T90AAmmo.HideFixedHeat(__instance.LoadedAmmoList?.AmmoClips);
			if (!DynamicMissionComposer.CampaignMode) return;
			var set = T90AAmmo.ApfsdsSet;
			foreach (var f in new[] { DynamicMissionLauncher.CurrentFactionBlue, DynamicMissionLauncher.CurrentFactionRed })
				if (f != null && (f.AmmoLogisticsData == null || !f.AmmoLogisticsData.Contains(set)))
					f.AmmoLogisticsData = (f.AmmoLogisticsData ?? new AmmoLogisticsScriptable[0]).Concat(new[] { set }).ToArray();
		}

		static Exception Finalizer(LoadoutManager __instance, Exception __exception)
		{
			if (T90AAmmo.ApfsdsSet == null || !IsT90A(__instance)) return __exception;
			var clips = __instance.LoadedAmmoList?.AmmoClips;
			T90AAmmo.RestoreFixedHeat(clips);
			return __exception;
		}

		static bool IsT90A(LoadoutManager lm) => lm.GetComponent<Vehicle>()?.UniqueName == T90AMod.UnitKey;
	}

	[HarmonyPatch(typeof(AmmoReplacementOrder), nameof(AmmoReplacementOrder.SelectAmmoType))]
	internal static class AmmoCampaignUnlockPatch
	{
		static void Prefix(AmmoReplacementOrder __instance, bool isCampaignMode)
		{
			if (!isCampaignMode || T90AAmmo.ApfsdsSet == null || __instance != T90AAmmo.ApfsdsSet.AmmoLogistics) return;
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
			if (info == null || !info.Tandem || __instance.IsSpall || cObject == null) return;
			if (!T90AAmmo.EraDefeatingRounds.Contains(info.Name) || Sacrificed(__instance)) return;
			if (!cObject.TryGetComponent<IArmor>(out var armor) || !armor.IsEra || armor.IsDetonated) return;
			if (armor.Unit != null && __instance.ShotInfo != null && __instance.ShotInfo.Shooter == armor.Unit) return;

			armor.Detonate();
			Sacrificed(__instance) = true;
		}
	}
}
