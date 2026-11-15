using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Mission;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace T90A.Common
{
	internal static partial class PortAmmo
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Round
		{
			public string Id;
			public string Name;
			public string Source;
			public string SourceUnit;
			public string ModelFrom;
			public Action<AmmoType> Set;
			public float Prevalence = 1f;
			public int AfterMissions;
			public bool ScaleWithLogistics;
			public string Texture;
			public string Atlas;
			public string Shape;
			public ChargeSplit Charge;
			public bool RackTntIsFiller;
		}
#pragma warning restore CS0649

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Rack
		{
			public int Index;
			public Mix Counts;
			public int[] MissileSlots = new int[0];
			public bool NoRestockInto;
			public bool BurstsInParent;
		}
#pragma warning restore CS0649

		internal sealed class Mix
		{
			public int Apfsds;
			public int Heat;
			public int He;
			public int Missile;
			public int[] Extra = new int[0];
			internal int Sum => Apfsds + Heat + He + Missile + Extra.Sum();
		}

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
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
			public Round Heat;
			public Round Missile;
			public Round[] Extra = new Round[0];
			public string[] EraDefeating = new string[0];
			public Mix TotalCounts;
			public Rack[] Racks = new Rack[0];
			public bool DropOtherRacks = false;
			public Vector3 GuidancePosition;
			public string GuidanceName = "guidance computer", BeamName = "guidance beam";
			public bool BeamOnBaseUnit;
			public string CoaxFeed = "PKT";
			public int CoaxRounds, CoaxBelt = 250;
			public bool SingleRound;
			public bool NoBaseHe;
		}
#pragma warning restore CS0649

		internal static Settings S;

		internal static readonly Dictionary<string, AmmoClipCodexScriptable> Apfsds = new Dictionary<string, AmmoClipCodexScriptable>();
		internal static AmmoClipCodexScriptable Heat;
		internal static AmmoClipCodexScriptable Missile;
		internal static readonly List<AmmoClipCodexScriptable> Extras = new List<AmmoClipCodexScriptable>();
		internal static AmmoLogisticsScriptable ApfsdsSet;
		static bool _built;

		static readonly List<Object> created = new List<Object>();
		static readonly MethodInfo RegisterAllBallistics = Reflect.Method(typeof(LoadoutManager), "RegisterAllBallistics");
		static readonly FieldInfo FeedMissileGuidance = Reflect.Field(typeof(AmmoFeed), "_missileGuidance");
		static readonly MethodInfo FeedGuidanceStopped = Reflect.Method(typeof(AmmoFeed), "MissileGuidanceStopped");

		internal static bool Build()
		{
			if (_built) return true;
			if (S == null) return false;
			AmmoClipCodexScriptable[] ClipsOf(string unit)
				=> OwnLoad(UnitRegistry.Find(unit)?.BaseAmmoClipsReference)?.AmmoClips ?? new AmmoClipCodexScriptable[0];
			var clips = ClipsOf(S.SourceUnit);
			AmmoType Find(string unit, string clip)
				=> (unit == null
					? clips
					: ClipsOf(unit)).FirstOrDefault(a => a.name == clip)?.ClipType.MinimalPattern[0].AmmoType;
			AmmoType SourceOf(Round round) => Find(round.SourceUnit, round.Source);
			AmmoType ModelOfRound(Round round)
			{
				if (string.IsNullOrEmpty(round.ModelFrom)) return null;
				int colon = round.ModelFrom.IndexOf(':');
				return colon < 0
					? Find(null, round.ModelFrom)
					: Find(round.ModelFrom.Substring(0, colon), round.ModelFrom.Substring(colon + 1));
			}
			Heat = S.Heat == null ? clips.FirstOrDefault(a => a.name == S.FixedHeat) : null;
			var ported = S.Apfsds
				.Concat(S.Heat != null ? new[] { S.Heat } : new Round[0])
				.Concat(S.Missile != null ? new[] { S.Missile } : new Round[0])
				.Concat(S.Extra)
				.ToList();
			var missing = ported.Where(r => SourceOf(r) == null).Select(x => x.Source)
				.Concat(ported
					.Where(r => !string.IsNullOrEmpty(r.ModelFrom) && ModelOfRound(r)?.VisualModel == null)
					.Select(r => r.ModelFrom))
				.ToList();
			if (S.Heat == null && Heat == null && !S.SingleRound) missing.Add(S.FixedHeat);
			if (missing.Count > 0 || S.Apfsds.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} ammo: vanilla {string.Join(", ", missing)} not loaded yet; keeping base ammo.");
				return false;
			}
			foreach (var round in S.Apfsds) Apfsds[round.Id] = MakeClip(round, SourceOf(round), ModelOfRound(round));
			if (S.Heat != null) Heat = MakeClip(S.Heat, SourceOf(S.Heat), ModelOfRound(S.Heat));
			if (S.Missile != null)
			{
				Missile = MakeClip(S.Missile, SourceOf(S.Missile), ModelOfRound(S.Missile));
			}
			Extras.Clear();
			foreach (var round in S.Extra) Extras.Add(MakeClip(round, SourceOf(round), ModelOfRound(round)));
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
					AmmoType = Apfsds[r.Id],
					Prevalence = r.Prevalence,
					BlockUntilMissionsCompleted = r.AfterMissions,
					LogisticsModifierCurve = r.ScaleWithLogistics
						? AnimationCurve.Linear(0f, 0f, 1f, 1f)
						: AnimationCurve.Constant(0f, 1f, 1f),
					AllowInCampaign = true,
				}).ToArray(),
			};
			created.Add(set);
			return set;
		}

		// Clips match by value, so it holds no round to equal no option. Reusing the vanilla ClipType still matched.
		static AmmoClipCodexScriptable heatStandIn;

		static AmmoClipCodexScriptable HeatStandIn()
		{
			if (heatStandIn == null)
			{
				heatStandIn = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
				heatStandIn.name = $"clip_{S.Prefix}_{S.FixedHeat.Replace("clip_", "")}_fixed";
				heatStandIn.hideFlags = HideFlags.DontUnloadUnusedAsset;
				heatStandIn.ClipType = new AmmoType.AmmoClip
				{
					Name = Heat.ClipType.Name,
					Capacity = 1,
					MinimalPattern = new AmmoCodexScriptable[0]
				};
				created.Add(heatStandIn);
			}
			return heatStandIn;
		}

		internal static int HideFixedHeat(AmmoClipCodexScriptable[] clips)
		{
			if (Heat == null || clips == null) return -1;
			int i = Array.IndexOf(clips, Heat);
			if (i >= 0) clips[i] = HeatStandIn();
			return i;
		}

		internal static int RestoreFixedHeat(AmmoClipCodexScriptable[] clips)
		{
			if (heatStandIn == null || clips == null) return -1;
			int i = Array.FindIndex(clips, a => ReferenceEquals(a, heatStandIn));
			if (i >= 0) clips[i] = Heat;
			return i;
		}

		internal static AmmoClipCodexScriptable DefaultApfsds(bool warn)
		{
			string id = S.DefaultApfsds?.Invoke();
			if (Apfsds.TryGetValue(id ?? "", out var apfsdsClip)) return apfsdsClip;
			if (warn) MelonLogger.Warning($"{S.Tag} ammo: unknown APFSDS '{id}', using {S.Apfsds[0].Id}.");
			return Apfsds[S.Apfsds[0].Id];
		}

		static List<AmmoClipCodexScriptable> PortList(IEnumerable<AmmoClipCodexScriptable> baseClips, AmmoClipCodexScriptable ap)
		{
			if (S.SingleRound) return new List<AmmoClipCodexScriptable> { ap };
			var list = new List<AmmoClipCodexScriptable> { ap, Heat };
			var baseHe = S.NoBaseHe ? null : baseClips.Skip(2).FirstOrDefault();
			if (baseHe != null) list.Add(baseHe);
			if (Missile != null) list.Add(Missile);
			list.AddRange(Extras);
			return list;
		}

		internal static bool IsPort(string unitKey) => S != null && string.Equals(unitKey, S.UnitKey, StringComparison.OrdinalIgnoreCase);

		static int[] Columns(Mix mix, List<AmmoClipCodexScriptable> list, string what)
		{
			var counts = new int[list.Count];
			for (int i = 0; i < list.Count; i++)
			{
				var clip = list[i];
				int extra = Extras.IndexOf(clip);
				if (i == 0) counts[i] = mix.Apfsds;
				else if (clip == Heat) counts[i] = mix.Heat;
				else if (Missile != null && clip == Missile) counts[i] = mix.Missile;
				else if (extra >= 0) counts[i] = extra < mix.Extra.Length ? mix.Extra[extra] : 0;
				else counts[i] = mix.He;
			}
			int unplaced = mix.Sum - counts.Sum();
			if (unplaced != 0 && UnplacedWarned.Add(what))
				MelonLogger.Warning($"{S.Tag} ammo: {unplaced} round(s) of {what} have no clip in the ammo list; left out.");
			return counts;
		}

		static readonly HashSet<string> UnplacedWarned = new HashSet<string>();

		internal sealed class NoRestockInto : MonoBehaviour { }

		[HarmonyPatch(typeof(LoadoutManager), "RackHasRestockSpace", new[] { typeof(LoadoutManager.RackLoadout) })]
		static class RestockTargetPatch
		{
			static bool Prefix(LoadoutManager.RackLoadout __0, ref bool __result)
			{
				var rackLoadout = __0;
				if (rackLoadout?.Rack == null || rackLoadout.Rack.GetComponent<NoRestockInto>() == null) return true;
				__result = false;
				return false;
			}
		}
	}

	[HarmonyPatch(typeof(LiveRound), "penCheck")]
	internal static class TandemEraPatch
	{
		static readonly AccessTools.FieldRef<LiveRound, bool> Sacrificed = Reflect.FieldRefAccess<LiveRound, bool>("_sacrificedWarhead");

		// Never throw out of a penCheck patch: the round fails every frame after, and in its first frame no new round flies.
		static void Prefix(LiveRound __instance, Collider __0)
		{
			try { Precursor(__instance, __0); }
			catch (Exception exception) { PatchGuard.Failed("Tandem ERA charge", exception); }
		}

		// GHPC's Tandem only beats slat armour.
		static void Precursor(LiveRound __instance, Collider cObject)
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
