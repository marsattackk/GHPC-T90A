using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Mission;
using GHPC.Vehicle;
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
			public bool InCampaign = true;
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
			public int Index;              // the base vehicle's RackLoadouts index, before any rack is dropped
			public string Name;
			public Mix Counts;
			public int[] MissileSlots = new int[0];   // slot numbers in that rack, on top of Counts (leave them out of Counts.Missile)
			public bool NoRestockInto;
			public bool BurstsInParent;
		}
#pragma warning restore CS0649

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Mix
		{
			public int Apfsds;
			public int Heat;
			public int He;
			public int Missile;
			public int[] Extra = new int[0];
			internal int Sum => Apfsds + Heat + He + Missile + Extra.Sum();
		}
#pragma warning restore CS0649

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class EraRule
		{
			public bool AddOnOnly;
			public float CostAddOn;
			public float CostBuiltIn;
		}

		internal sealed class SplitCharge
		{
			public float FirstShare = 1f / 3f;
			public float Offset = 0.01f;
		}
#pragma warning restore CS0649

		static readonly string[] BuiltInEraNames = { "Kontakt-5", "Relikt", "Контакт-5", "Реликт" };

		internal static bool IsBuiltInEra(string armourName)
			=> armourName != null
				&& BuiltInEraNames.Any(n => armourName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

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
			public Func<bool> ForceApfsds;
			public string ApfsdsListName;
			public string MenuSuffix;
			public string FixedHeat = "clip_3BK18M";
			public Round Heat;
			public Round[] HeatChoice = new Round[0];
			public Func<string> DefaultHeat;
			public Func<bool> ForceHeat;
			public string HeatListName;
			public Round Missile;
			public Round[] Extra = new Round[0];
			public string[] EraDefeating = new string[0];
			public Dictionary<string, EraRule> EraRules = new Dictionary<string, EraRule>();
			public Dictionary<string, SplitCharge> SplitCharges = new Dictionary<string, SplitCharge>();
			public Mix TotalCounts;
			public Rack[] Racks = new Rack[0];
			public bool DropOtherRacks = false;
			public string OtherRacksName;
			public string AutoloaderRack = "autoloader";
			public Dictionary<string, string[]> LoadPresets;
			public Func<string> LoadPreset;
			public Func<string> EmptyRacks;
			public Func<bool> RandomApfsds;
			public Func<string> ApfsdsPool;
			public Func<bool> RandomHeat;
			public Func<string> HeatPool;
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
		internal static readonly Dictionary<string, AmmoClipCodexScriptable> Heats = new Dictionary<string, AmmoClipCodexScriptable>();
		internal static AmmoLogisticsScriptable HeatSet;
		internal static AmmoClipCodexScriptable Missile;
		internal static readonly List<AmmoClipCodexScriptable> Extras = new List<AmmoClipCodexScriptable>();
		internal static AmmoLogisticsScriptable ApfsdsSet;
		internal static AmmoLogisticsScriptable ApfsdsMenuSet;
		internal static AmmoClipCodexScriptable RandomStandIn;
		internal static AmmoLogisticsScriptable HeatMenuSet;
		internal static AmmoClipCodexScriptable RandomHeatStandIn;
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
			bool heatChoice = S.HeatChoice.Length > 0;
			Heat = S.Heat == null && !heatChoice ? clips.FirstOrDefault(a => a.name == S.FixedHeat) : null;
			var ported = S.Apfsds
				.Concat(S.Heat != null ? new[] { S.Heat } : new Round[0])
				.Concat(S.HeatChoice)
				.Concat(S.Missile != null ? new[] { S.Missile } : new Round[0])
				.Concat(S.Extra)
				.ToList();
			var missing = ported.Where(r => SourceOf(r) == null).Select(x => x.Source)
				.Concat(ported
					.Where(r => !string.IsNullOrEmpty(r.ModelFrom) && ModelOfRound(r)?.VisualModel == null)
					.Select(r => r.ModelFrom))
				.ToList();
			if (S.Heat == null && !heatChoice && Heat == null && !S.SingleRound) missing.Add(S.FixedHeat);
			if (missing.Count > 0 || S.Apfsds.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} ammo: vanilla {string.Join(", ", missing)} not loaded yet; keeping base ammo.");
				return false;
			}
			foreach (var round in S.Apfsds) Apfsds[round.Id] = MakeClip(round, SourceOf(round), ModelOfRound(round));
			if (S.Heat != null) Heat = MakeClip(S.Heat, SourceOf(S.Heat), ModelOfRound(S.Heat));
			foreach (var round in S.HeatChoice) Heats[round.Id] = MakeClip(round, SourceOf(round), ModelOfRound(round));
			if (heatChoice)
			{
				HeatSet = MakeSet("heat", S.HeatListName, S.HeatChoice, Heats);
				Heat = DefaultHeat(warn: true);
			}
			if (S.Missile != null)
			{
				Missile = MakeClip(S.Missile, SourceOf(S.Missile), ModelOfRound(S.Missile));
			}
			Extras.Clear();
			foreach (var round in S.Extra) Extras.Add(MakeClip(round, SourceOf(round), ModelOfRound(round)));
			ApfsdsSet = MakeSet("apfsds", S.ApfsdsListName, S.Apfsds, Apfsds);
			if (RandomPossible)
			{
				RandomStandIn = MakeRandomStandIn("random", Apfsds[S.Apfsds[0].Id]);
				ApfsdsMenuSet = MakeMenuSet("apfsds_menu", ApfsdsSet, RandomStandIn);
			}
			if (RandomHeatPossible)
			{
				RandomHeatStandIn = MakeRandomStandIn("random_heat", Heats[S.HeatChoice[0].Id]);
				HeatMenuSet = MakeMenuSet("heat_menu", HeatSet, RandomHeatStandIn);
			}
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

		static AmmoLogisticsScriptable MakeSet(
			string kind,
			string listName,
			Round[] rounds,
			Dictionary<string, AmmoClipCodexScriptable> built)
		{
			var set = ScriptableObject.CreateInstance<AmmoLogisticsScriptable>();
			set.name = $"ammo_logistics_{S.Prefix}_{kind}";
			set.hideFlags = HideFlags.DontUnloadUnusedAsset;
			set.AmmoLogistics = new AmmoReplacementOrder
			{
				Name = listName,
				AmmoOptions = rounds.Select(r => new AmmoReplacementOrder.AmmoTypeLogistics
				{
					AmmoType = built[r.Id],
					Prevalence = r.Prevalence,
					BlockUntilMissionsCompleted = r.AfterMissions,
					LogisticsModifierCurve = r.ScaleWithLogistics
						? AnimationCurve.Linear(0f, 0f, 1f, 1f)
						: AnimationCurve.Constant(0f, 1f, 1f),
					AllowInCampaign = r.InCampaign,
				}).ToArray(),
			};
			created.Add(set);
			return set;
		}

		internal static Round RoundOfOption(AmmoReplacementOrder order, AmmoClipCodexScriptable clip)
		{
			if (ApfsdsSet != null && order == ApfsdsSet.AmmoLogistics)
				return S.Apfsds.FirstOrDefault(r => Apfsds.TryGetValue(r.Id, out var built) && built == clip);
			if (HeatSet != null && order == HeatSet.AmmoLogistics)
				return S.HeatChoice.FirstOrDefault(r => Heats.TryGetValue(r.Id, out var built) && built == clip);
			return null;
		}

		internal static bool RandomPossible => S != null && S.RandomApfsds != null && S.Apfsds.Length >= 2;

		internal static bool RandomHeatPossible => S != null && S.RandomHeat != null && S.HeatChoice.Length >= 2;

		// GHPC compares clips by the rounds they hold (AmmoClip.Equals), never by the clip's name. A stand-in holding no round
		// would equal the HEAT stand-in (and every other port's): the customizer's Random entry replaced the HEAT slot too.
		// So it holds one round of its own, a copy of the kind's first round (valid figures for GHPC's firing table) under a
		// name no other round has. The APFSDS and HEAT stand-ins share the name "Random (<tank>)" the dropdowns show; they
		// never equal each other, since AmmoType.Equals also compares the round's category (AP, HEAT).
		static AmmoClipCodexScriptable MakeRandomStandIn(string kind, AmmoClipCodexScriptable first)
		{
			string name = $"Random ({S.Tag})";
			var source = first.ClipType.MinimalPattern[0].AmmoType;
			var ammo = new AmmoType();
			foreach (var field in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(ammo, field.GetValue(source));
			ammo.Name = name;
			var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
			codex.name = $"ammo_{S.Prefix}_{kind}";
			codex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			codex.AmmoType = ammo;
			var standIn = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
			standIn.name = $"clip_{S.Prefix}_{kind}";
			standIn.hideFlags = HideFlags.DontUnloadUnusedAsset;
			standIn.ClipType = new AmmoType.AmmoClip { Name = name, Capacity = 1, MinimalPattern = new[] { codex } };
			created.Add(codex);
			created.Add(standIn);
			return standIn;
		}

		// The campaign's list (ApfsdsSet, HeatSet) never holds the stand-in: the factions get that one, and GHPC rolls it for
		// real rounds only.
		static AmmoLogisticsScriptable MakeMenuSet(string kind, AmmoLogisticsScriptable campaignSet, AmmoClipCodexScriptable standIn)
		{
			var set = ScriptableObject.CreateInstance<AmmoLogisticsScriptable>();
			set.name = $"ammo_logistics_{S.Prefix}_{kind}";
			set.hideFlags = HideFlags.DontUnloadUnusedAsset;
			var options = campaignSet.AmmoLogistics.AmmoOptions.Select(o => new AmmoReplacementOrder.AmmoTypeLogistics
			{
				AmmoType = o.AmmoType,
				Prevalence = o.Prevalence,
				BlockUntilMissionsCompleted = o.BlockUntilMissionsCompleted,
				LogisticsModifierCurve = o.LogisticsModifierCurve,
				AllowInCampaign = o.AllowInCampaign,
			}).ToList();
			options.Add(new AmmoReplacementOrder.AmmoTypeLogistics
			{
				AmmoType = standIn,
				Prevalence = 0f,
				BlockUntilMissionsCompleted = 0,
				LogisticsModifierCurve = AnimationCurve.Constant(0f, 1f, 1f),
				AllowInCampaign = false,
			});
			set.AmmoLogistics = new AmmoReplacementOrder { Name = campaignSet.AmmoLogistics.Name, AmmoOptions = options.ToArray() };
			created.Add(set);
			return set;
		}

		static string Effective(Func<string> setting, string flagName, Func<string, string> fromFlag)
		{
			string value = setting?.Invoke();
			return value;
		}

		internal static bool RandomWanted() => RandomPossible && Wanted(S.RandomApfsds, "randomapfsds");

		internal static bool RandomHeatWanted() => RandomHeatPossible && Wanted(S.RandomHeat, "randomheat");

		static bool Wanted(Func<bool> setting, string flagName)
		{
			bool wanted = setting();
			return wanted;
		}

		internal const int MissileColumn = 3;

		static int[] ToVector(Mix mix)
		{
			var vector = new int[4 + S.Extra.Length];
			vector[0] = mix.Apfsds;
			vector[1] = mix.Heat;
			vector[2] = mix.He;
			vector[MissileColumn] = mix.Missile;
			for (int i = 0; i < S.Extra.Length && i < mix.Extra.Length; i++) vector[4 + i] = mix.Extra[i];
			return vector;
		}

		static Mix ToMix(int[] vector)
		{
			var mix = new Mix { Apfsds = vector[0], Heat = vector[1], He = vector[2], Missile = vector[MissileColumn] };
			mix.Extra = new int[S.Extra.Length];
			for (int i = 0; i < mix.Extra.Length; i++) mix.Extra[i] = vector[4 + i];
			return mix;
		}

		static readonly System.Random Roller = new System.Random(Environment.TickCount);
		static readonly HashSet<string> PoolWarned = new HashSet<string>();

		static AmmoClipCodexScriptable RollApfsds(Vehicle vehicle)
		{
			var clip = Roll(vehicle, "APFSDS", S.Apfsds, Apfsds, S.ApfsdsPool, "apfsdspool");
			return clip;
		}

		static AmmoClipCodexScriptable RollHeat(Vehicle vehicle)
		{
			var clip = Roll(vehicle, "HEAT", S.HeatChoice, Heats, S.HeatPool, "heatpool");
			return clip;
		}

		static AmmoClipCodexScriptable Roll(
			Vehicle vehicle,
			string kind,
			Round[] rounds,
			Dictionary<string, AmmoClipCodexScriptable> built,
			Func<string> poolSetting,
			string flagName)
		{
			var ids = rounds.Select(r => r.Id).ToArray();
			string spelled = Effective(poolSetting, flagName, g => g.Replace('+', ',').Replace('=', ':'));
			var pool = PortAmmoPool.Parse(spelled, ids, out string warning, kind);
			if (warning != null && PoolWarned.Add(warning)) MelonLogger.Warning($"{S.Tag}: {warning}");
			pool = pool ?? PortAmmoPool.Default(ids, rounds.Select(r => r.InCampaign ? r.Prevalence : 0f).ToArray());
			string id = PortAmmoPool.Pick(pool, Roller);
			return built[id];
		}

		internal static void RollRandomRounds(LoadoutManager loadoutManager)
		{
			var clips = loadoutManager?.LoadedAmmoList?.AmmoClips;
			if (clips == null || clips.Length == 0) return;
			var vehicle = loadoutManager.GetComponent<Vehicle>();
			bool rolled = false;
			if (RandomStandIn != null && ReferenceEquals(clips[0], RandomStandIn))
			{
				clips[0] = RollApfsds(vehicle);
				rolled = true;
			}
			int heatSlot = RandomHeatStandIn != null ? Array.FindIndex(clips, c => ReferenceEquals(c, RandomHeatStandIn)) : -1;
			if (heatSlot >= 0)
			{
				clips[heatSlot] = RollHeat(vehicle);
				rolled = true;
			}
			if (rolled) RegisterAllBallistics.Invoke(loadoutManager, null);
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
			if (Heat == null || clips == null || HeatSet != null) return -1;
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

		internal static AmmoClipCodexScriptable DefaultHeat(bool warn)
		{
			if (S.HeatChoice.Length == 0) return Heat;
			string id = S.DefaultHeat?.Invoke();
			if (Heats.TryGetValue(id ?? "", out var heatClip)) return heatClip;
			if (warn) MelonLogger.Warning($"{S.Tag} ammo: unknown HEAT '{id}', using {S.HeatChoice[0].Id}.");
			return Heats[S.HeatChoice[0].Id];
		}

		internal static bool IsHeat(AmmoClipCodexScriptable clip)
			=> clip != null && (ReferenceEquals(clip, Heat) || Heats.ContainsValue(clip));

		static List<AmmoClipCodexScriptable> PortList(
			IEnumerable<AmmoClipCodexScriptable> baseClips,
			AmmoClipCodexScriptable ap,
			AmmoClipCodexScriptable heat = null)
		{
			if (S.SingleRound) return new List<AmmoClipCodexScriptable> { ap };
			var list = new List<AmmoClipCodexScriptable> { ap, heat ?? DefaultHeat(warn: false) };
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
				else if (IsHeat(clip)) counts[i] = mix.Heat;
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

		internal sealed class EmptiedRack : MonoBehaviour
		{
			internal Vehicle Owner;
		}

		static readonly HashSet<string> LoadWarned = new HashSet<string>();

		static PortAmmoLoad.Result ResolveLoad(
			LoadoutManager loadoutManager,
			List<int> keptIndexes,
			out string choice)
		{
			choice = null;
			if (S.LoadPresets == null) return null;
			var rows = keptIndexes.Select(index =>
			{
				var rack = S.Racks.FirstOrDefault(c => c.Index == index);
				return rack != null
					? new PortAmmoLoad.Row
					{
						Name = rack.Name,
						Counts = rack.Counts != null ? ToVector(rack.Counts) : null,
						MissileSlots = rack.MissileSlots.Length
					}
					: new PortAmmoLoad.Row { Name = S.OtherRacksName };
			}).ToList();
			string preset = Effective(S.LoadPreset, "loadpreset", g => g.Replace('+', ' '));
			string emptyRacks = Effective(S.EmptyRacks, "emptyracks", g => g.Replace('+', ','));
			var result = PortAmmoLoad.Resolve(
				rows,
				ToVector(S.TotalCounts),
				MissileColumn,
				S.AutoloaderRack,
				S.LoadPresets,
				preset,
				emptyRacks,
				S.Tag);
			foreach (string warning in result.Warnings)
				if (LoadWarned.Add(warning)) MelonLogger.Warning(warning);
			choice = result.Choice;
			return result;
		}

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

		static bool Prepare() => Sacrificed != null;

		// Never throw out of a penCheck patch: the round fails every frame after, and in its first frame no new round flies.
		static void Prefix(LiveRound __instance, Collider __0)
		{
			try { Precursor(__instance, __0); }
			catch (Exception exception) { PatchGuard.Failed("Tandem ERA charge", exception); }
		}

		// GHPC's Tandem only beats slat armour. One precursor per round: a second block meets the main charge (GHPC's penCheck).
		static void Precursor(LiveRound __instance, Collider cObject)
		{
			AmmoType info = __instance.Info;
			if (info == null || !info.Tandem || __instance.IsSpall || cObject == null || PortAmmo.S == null) return;
			PortAmmo.EraRule rule = null;
			bool listed = Array.IndexOf(PortAmmo.S.EraDefeating, info.Name) >= 0
				|| (PortAmmo.S.EraRules != null && PortAmmo.S.EraRules.TryGetValue(info.Name, out rule));
			if (!listed || Sacrificed(__instance)) return;
			if (!cObject.TryGetComponent<IArmor>(out var armor) || !armor.IsEra || armor.IsDetonated) return;
			if (armor.Unit != null && __instance.ShotInfo != null && __instance.ShotInfo.Shooter == armor.Unit) return;
			bool builtIn = PortAmmo.IsBuiltInEra(armor.Name);
			// A precursor for add-on ERA meets built-in ERA as a single charge does: GHPC's own penCheck takes the block.
			if (rule != null && rule.AddOnOnly && builtIn) return;

			armor.Detonate();
			Sacrificed(__instance) = true;
			float cost = rule == null ? 0f : builtIn ? rule.CostBuiltIn : rule.CostAddOn;
			if (cost > 0f && __instance.RhaPenetrationOverride <= 0f)
				__instance.RhaPenetrationOverride = Mathf.Max(1f, info.RhaPenetration - cost);
		}
	}
}
