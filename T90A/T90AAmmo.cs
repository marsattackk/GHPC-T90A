using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class T90AAmmo
	{
		internal static int[] Counts = { 22, 8, 8, 4 };

		internal static readonly int[] CarouselCountsWithMissile = { 12, 4, 4, 0 };

		internal static readonly Dictionary<string, AmmoClipCodexScriptable> Apfsds = new Dictionary<string, AmmoClipCodexScriptable>();
		internal static AmmoClipCodexScriptable Heat;
		internal static AmmoClipCodexScriptable Missile;
		internal static readonly HashSet<string> EraDefeatingRounds = new HashSet<string> { "9M119M Invar" };
		static readonly PropertyInfo StoredClips = AccessTools.Property(typeof(GHPC.Weapons.AmmoRack), "StoredClips");
		static readonly MethodInfo RegisterAllBallistics = AccessTools.Method(typeof(LoadoutManager), "RegisterAllBallistics");
		static readonly FieldInfo FeedMissileGuidance = AccessTools.Field(typeof(AmmoFeed), "_missileGuidance");

		internal static bool Build()
		{
			if (Missile != null) return true;
			var reference = UnitRegistry.Find("T80B")?.BaseAmmoClipsReference;
			VehicleAmmoListScriptable list = null;
			if (reference != null && !string.IsNullOrEmpty(reference.AssetGUID))
				list = reference.Asset as VehicleAmmoListScriptable ?? reference.LoadAssetAsync<VehicleAmmoListScriptable>().WaitForCompletion();
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
				a.RhaPenetration = 550f;
				a.Mass = 4.85f;
				a.MuzzleVelocity = 1700f;
			});
			Apfsds["3BM60"] = MakeClip("3BM60", "3BM60 APFSDS-T", ap, a =>
			{
				a.RhaPenetration = 740f;
				a.Mass = 5.12f;
				a.MuzzleVelocity = 1660f;
			});
			Missile = MakeClip("9M119M", "9M119M Invar", kobra, a =>
			{
				a.RhaPenetration = 900f;
				a.Tandem = true;
				a.Mass = 16.5f;
				a.MuzzleVelocity = 350f;
				a.MaximumRange = 5000f;
				a.RangedFuseTime = 5000f / 350f + 0.3f;
				a.Guidance = AmmoType.GuidanceType.Laser;
			});
			return true;
		}

		static AmmoClipCodexScriptable MakeClip(string id, string displayName, AmmoType source, System.Action<AmmoType> set)
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
			if (!Apfsds.TryGetValue(apfsds ?? "", out var ap))
			{
				MelonLogger.Warning("T-90A ammo: unknown APFSDS '" + apfsds + "', using 3BM42.");
				ap = Apfsds["3BM42"];
			}

			var list = lm.LoadedAmmoList.AmmoClips.Take(3).ToList();
			list[0] = ap;
			list[1] = Heat;
			if (addMissile) list.Add(Missile);
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
			mgu.AimElement = mgu.transform;
			weapon.GuidanceUnit = mgu;
			weapon.Feed.ReloadDuringMissileTracking = false;
			FeedMissileGuidance.SetValue(weapon.Feed, mgu);
			weapon.FireWhileGuidingMissile = false;
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
