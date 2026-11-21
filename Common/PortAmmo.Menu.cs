using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Mission;
using GHPC.State;
using GHPC.UI.Menu;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace T90A.Common
{
	// Customiser ammo sets, the label suffix, campaign unlocks, and a stand-in that hides the fixed HEAT round while
	// GHPC picks the loadout.
	internal static partial class PortAmmo
	{
		internal static List<AmmoLogisticsScriptable> SetsForMenu(
			AssetReference baseList,
			AmmoLogisticsScriptable[] vanilla,
			out List<int> preferred)
		{
			preferred = null;
			if (!Build()) return null;
			var list = baseList?.Asset as VehicleAmmoListScriptable ?? OwnLoad(baseList);
			if (list?.AmmoClips == null || list.AmmoClips.Length < (S.SingleRound ? 1 : 3)) return null;
			var clips = PortList(list.AmmoClips, DefaultApfsds(warn: false));
			if (HeatSet == null) clips.Remove(Heat);
			var sets = new List<AmmoLogisticsScriptable>();
			preferred = new List<int>();
			var own = new[] { ApfsdsMenuSet ?? ApfsdsSet, HeatMenuSet ?? HeatSet };
			foreach (var set in own.Concat(vanilla ?? new AmmoLogisticsScriptable[0]))
			{
				var options = set?.AmmoLogistics?.AmmoOptions;
				if (options == null || sets.Contains(set)) continue;
				bool startOnRandom = (set == ApfsdsMenuSet && RandomWanted()) || (set == HeatMenuSet && RandomHeatWanted());
				for (int j = 0; j < options.Length; j++)
					if (options[j] != null && clips.Contains(options[j].AmmoType))
					{
						sets.Add(set);
						preferred.Add(startOnRandom ? options.Length - 1 : j);
						break;
					}
			}
			return sets;
		}
	}

	[HarmonyPatch(typeof(MissionConfigMenuPanel), "GetAmmoSetsByUnit")]
	internal static class AmmoMenuSetsPatch
	{
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, AmmoLogisticsSetScriptable> AllSets =
			Reflect.FieldRefAccess<MissionConfigMenuPanel, AmmoLogisticsSetScriptable>("_allAmmoSetsScriptable");
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, UnitPrefabLookupScriptable> AllUnits =
			Reflect.FieldRefAccess<MissionConfigMenuPanel, UnitPrefabLookupScriptable>("_allUnitsScriptable");

		static bool Prepare() => AllSets != null && AllUnits != null;

		static void Postfix(MissionConfigMenuPanel __instance, string __0, ref List<AmmoLogisticsScriptable> __result, ref List<int> __1)
		{
			string unitKey = __0;
			if (!PortAmmo.IsPort(unitKey)) return;
			try
			{
				var sets = PortAmmo.SetsForMenu(
					AllUnits(__instance)?.GetBaseAmmoClipLoadout(unitKey),
					AllSets(__instance)?.AllAmmoReplacementOrders,
					out var preferred);
				if (sets != null)
				{
					__result = sets;
					__1 = preferred;
				}
			}
			catch (Exception exception)
			{
				MelonLogger.Error($"{PortAmmo.S.Tag} ammo choice for the mission customizer failed: " + exception);
			}
		}
	}

	[HarmonyPatch(typeof(MissionConfigMenuPanel), nameof(MissionConfigMenuPanel.AddAmmoEntry))]
	internal static class AmmoMenuLabelPatch
	{
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, List<GameObject>> Entries =
			Reflect.FieldRefAccess<MissionConfigMenuPanel, List<GameObject>>("_ammoDataObjects");
		static readonly AccessTools.FieldRef<MissionConfigAmmoSelectionEntry, TextMeshProUGUI> Label =
			Reflect.FieldRefAccess<MissionConfigAmmoSelectionEntry, TextMeshProUGUI>("_label");

		static bool Prepare() => Entries != null && Label != null;

		static void Postfix(MissionConfigMenuPanel __instance, AmmoLogisticsScriptable __1)
		{
			var ammoLogisticsScriptable = __1;
			bool own = ammoLogisticsScriptable != null
				&& (ammoLogisticsScriptable == PortAmmo.ApfsdsSet
					|| ammoLogisticsScriptable == PortAmmo.ApfsdsMenuSet
					|| ammoLogisticsScriptable == PortAmmo.HeatSet
					|| ammoLogisticsScriptable == PortAmmo.HeatMenuSet);
			if (!own || string.IsNullOrEmpty(PortAmmo.S?.MenuSuffix))
				return;
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
			foreach (var set in new[] { PortAmmo.ApfsdsSet, PortAmmo.HeatSet }.Where(s => s != null))
				foreach (var faction in new[] { DynamicMissionLauncher.CurrentFactionBlue, DynamicMissionLauncher.CurrentFactionRed })
					if (faction != null && (faction.AmmoLogisticsData == null || !faction.AmmoLogisticsData.Contains(set)))
						faction.AmmoLogisticsData = (faction.AmmoLogisticsData ?? new AmmoLogisticsScriptable[0])
							.Concat(new[] { set })
							.ToArray();
		}

		static Exception Finalizer(LoadoutManager __instance, Exception __exception)
		{
			if (PortAmmo.ApfsdsSet == null || !IsPort(__instance)) return __exception;
			var clips = __instance.LoadedAmmoList?.AmmoClips;
			int slot = PortAmmo.RestoreFixedHeat(clips);
			PortAmmo.RollRandomRounds(__instance);
			return __exception;
		}

		static bool IsPort(LoadoutManager lm) => PortAmmo.IsPort(lm.GetComponent<Vehicle>()?.UniqueName);
	}

	[HarmonyPatch(typeof(AmmoReplacementOrder), nameof(AmmoReplacementOrder.SelectAmmoType))]
	internal static class AmmoCampaignUnlockPatch
	{
		// GHPC only honours BlockUntilMissionsCompleted for units. SelectAmmoType ignores it.
		static bool Prefix(AmmoReplacementOrder __instance, bool __2, ref AmmoClipCodexScriptable __3, ref AmmoClipCodexScriptable __result)
		{
			bool isCampaignMode = __2;
			bool apfsds = PortAmmo.ApfsdsSet != null && __instance == PortAmmo.ApfsdsSet.AmmoLogistics;
			bool heat = PortAmmo.HeatSet != null && __instance == PortAmmo.HeatSet.AmmoLogistics;
			if (!isCampaignMode || (!apfsds && !heat)) return true;
			if ((apfsds ? PortAmmo.S.ForceApfsds : PortAmmo.S.ForceHeat)?.Invoke() == true)
			{
				__result = apfsds ? PortAmmo.DefaultApfsds(warn: true) : PortAmmo.DefaultHeat(warn: true);
				__3 = null;
				return false;
			}
			int done = CampaignSaveState.ActiveSaveData?.TotalMissionsCompleted ?? 0;
			foreach (var option in __instance.AmmoOptions)
				option.AllowInCampaign = PortAmmo.RoundOfOption(__instance, option.AmmoType)?.InCampaign != false
					&& done >= option.BlockUntilMissionsCompleted;
			return true;
		}
	}
}
