using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static partial class PortAmmo
	{
		internal static void ApplyAtSpawn(Vehicle vehicle)
		{
			if (!Build()) return;
			var loadoutManager = vehicle.GetComponent<LoadoutManager>();
			if (loadoutManager == null) return;
			var weapon = vehicle.GetComponent<WeaponsManager>().Weapons[0].Weapon;
			var list = PortList(loadoutManager.LoadedAmmoList.AmmoClips, DefaultApfsds(warn: true));
			loadoutManager.LoadedAmmoList.AmmoClips = list.ToArray();
			loadoutManager.TotalAmmoCounts = Columns(S.TotalCounts, list, "the load");
			int missile = Missile != null ? list.IndexOf(Missile) : -1;
			var kept = new List<LoadoutManager.RackLoadout>();
			for (int i = 0; i < loadoutManager.RackLoadouts.Length; i++)
			{
				var rackLoadout = loadoutManager.RackLoadouts[i];
				var rack = S.Racks.FirstOrDefault(c => c.Index == i);
				if (rack == null)
				{
					if (!S.DropOtherRacks) kept.Add(rackLoadout);
					continue;
				}
				if (rack.Counts != null) rackLoadout.AmmoCounts = Columns(rack.Counts, list, "rack " + i);
				if (missile >= 0 && rack.MissileSlots.Length > 0)
					rackLoadout.FixedChoices = rack.MissileSlots
						.Select(s => new LoadoutManager.RackLoadoutFixedChoice { AmmoClipIndex = missile, RackSlotIndex = s })
						.ToArray();
				if (rack.NoRestockInto && rackLoadout.Rack != null && rackLoadout.Rack.GetComponent<NoRestockInto>() == null)
					rackLoadout.Rack.gameObject.AddComponent<NoRestockInto>();
				if (rack.BurstsInParent && rackLoadout.Rack != null) BurstInParent(rackLoadout.Rack);
				kept.Add(rackLoadout);
			}
			string dropped = kept.Count < loadoutManager.RackLoadouts.Length
				? "; racks left out: "
					+ string.Join(
						", ",
						loadoutManager.RackLoadouts.Except(kept).Select(l => l.Rack != null ? l.Rack.name : "?"))
				: "";
			var all = loadoutManager.RackLoadouts;
			if (S.DropOtherRacks)
				kept = kept.OrderBy(l => Array.FindIndex(S.Racks, r => r.Index == Array.IndexOf(all, l))).ToList();
			loadoutManager.RackLoadouts = kept.ToArray();
			if (missile >= 0) AddGuidanceUnit(weapon);
			RegisterAllBallistics.Invoke(loadoutManager, null);
		}

		static readonly HashSet<string> reported = new HashSet<string>();

		internal static void SelfCheck(Vehicle vehicle)
		{
			if (S == null || !_built || vehicle == null) return;
			var problems = new List<string>();
			var own = Apfsds.Select(e => (e.Key, e.Value)).ToList();
			if (S.Heat != null) own.Add((S.Heat.Id, Heat));
			if (Missile != null) own.Add((S.Missile.Id, Missile));
			for (int i = 0; i < Extras.Count; i++) own.Add((S.Extra[i].Id, Extras[i]));
			foreach (var (id, clip) in own)
			{
				var model = clip?.ClipType?.MinimalPattern?.FirstOrDefault()?.AmmoType?.VisualModel;
				if (model == null)
				{
					problems.Add($"{id} round model missing");
					continue;
				}
				if (model.activeInHierarchy) problems.Add($"{id} round model is active in the scene");
				string damage = ModelDamage(model.transform);
				if (damage != null) problems.Add($"{id} round model {damage}");
			}
			var loadoutManager = vehicle.GetComponent<LoadoutManager>();
			var clips = loadoutManager?.LoadedAmmoList?.AmmoClips;
			if (loadoutManager == null || clips == null)
			{
				Report(vehicle, problems.Concat(new[] { "no loadout" }));
				return;
			}
			int expected = S.SingleRound ? 1 : (S.NoBaseHe ? 2 : 3) + (Missile != null ? 1 : 0) + Extras.Count;
			if (clips.Length != expected) problems.Add($"ammo list has {clips.Length} entries, expected {expected}");
			if (clips.Length > 0 && !Apfsds.Values.Any(a => ReferenceEquals(a, clips[0])))
				problems.Add($"APFSDS slot holds '{clips[0]?.ClipType?.Name}'");
			if (!S.SingleRound && clips.Length > 1 && !ReferenceEquals(clips[1], Heat))
				problems.Add($"HEAT slot holds '{clips[1]?.ClipType?.Name}'");
			bool xray = AarController.ShowXray || AarController.InAar;
			var constants = ConstantsAndInfoManager.Instance;
			int aarLayer = constants != null ? constants.AarLayer : 15, xrayLayer = constants != null ? constants.XrayOnlyLayer : 21;
			var bad = new Dictionary<string, int>();
			void Bad(string what) => bad[what] = bad.TryGetValue(what, out int count) ? count + 1 : 1;
			int stored = 0;
			foreach (var rackLoadout in loadoutManager.RackLoadouts ?? new LoadoutManager.RackLoadout[0])
			{
				if (rackLoadout.Rack == null) continue;
				foreach (var slot in rackLoadout.Rack.VisualSlots)
				{
					var visual = slot != null
						? slot.GetComponentsInChildren<AmmoStoredVisual>(true).FirstOrDefault(c => !c.Hidden)
						: null;
					if (visual == null) continue;
					string name = visual.AmmoType?.Name ?? "unknown round";
					var parts = visual.GetComponentsInChildren<Renderer>(true).ToList();
					if (visual.ChargeVisual != null) parts.AddRange(visual.ChargeVisual.GetComponentsInChildren<Renderer>(true));
					if (parts.Count == 0)
					{
						Bad($"{name} stored without a model");
						continue;
					}
					string damage = ModelDamage(visual.transform);
					if (damage != null) Bad($"{name} stored {damage}");
					if (!xray && parts.Any(r => r.gameObject.layer == aarLayer || r.gameObject.layer == xrayLayer))
						Bad($"{name} stored on the x-ray layer");
				}
			}
			foreach (var entry in bad) problems.Add($"{entry.Key} ({entry.Value}x)");
			for (int i = 0; i < clips.Length; i++) stored += loadoutManager.GetCurrentAmmoCount(i);
			var weapon = vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon;
			int breech = weapon?.Feed?.AmmoTypeInBreech != null ? 1 : 0;
			int total = (loadoutManager.TotalAmmoCounts ?? new int[0]).Sum();
			if (loadoutManager.FillAmmoOnStart && Math.Abs(stored + breech - total) > 1)
				problems.Add($"{stored} rounds stored + {breech} in the breech, expected {total}");
			if (Missile != null && clips.Contains(Missile) && weapon != null)
			{
				if (weapon.GuidanceUnit == null) problems.Add("missile guidance unit missing");
				else if (!ReferenceEquals(FeedMissileGuidance.GetValue(weapon.Feed), weapon.GuidanceUnit))
					problems.Add("missile guidance unit not linked to the gun's feed");
			}
			Report(vehicle, problems);
		}

		static string ModelDamage(Transform model)
		{
			var issues = new List<string>();
			var filters = model.GetComponentsInChildren<MeshFilter>(true);
			if (filters.Any(m => m.sharedMesh == null)) issues.Add("lost meshes");
			if (model.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null)))
				issues.Add("lost materials");
			if (model.GetComponentsInChildren<Collider>(true).Length == 0) issues.Add("has no hit zones");
			return issues.Count > 0 ? string.Join(", ", issues) : null;
		}

		static void Report(Vehicle vehicle, IEnumerable<string> problems)
		{
			var fresh = problems.Where(p => reported.Add(p)).ToList();
			if (fresh.Count == 0) return;
			MelonLogger.Warning($"{S.Tag} ammunition self-check ({vehicle.name}): {string.Join("; ", fresh)}. Please include this log in a bug report.");
		}

		static readonly FieldInfo InitialClipCounts = Reflect.Field(typeof(GHPC.Weapons.AmmoRack), "_initialClipCounts");

		internal static void ApplyCoaxAtSpawn(Vehicle vehicle)
		{
			if (S == null || S.CoaxRounds <= 0) return;
			var feed = vehicle.GetComponentsInChildren<AmmoFeed>(true).FirstOrDefault(c => c.name.Contains(S.CoaxFeed));
			var rack = feed?.ReadyRack;
			if (rack == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} coax {S.CoaxFeed}: feed or ready rack NOT FOUND; keeping the base load.");
				return;
			}
			var old = rack.ClipTypes[0];
			int before = rack.GetCurrentStoredAmmoCount(old), oldBelts = rack.StoredClips.Count;
			int belts = Mathf.Max(1, Mathf.RoundToInt(S.CoaxRounds / (float)S.CoaxBelt));
			var belt = new AmmoType.AmmoClip { Name = old.Name, MinimalPattern = old.MinimalPattern, Capacity = S.CoaxBelt };
			rack.ClipTypes = new[] { belt };
			InitialClipCounts.SetValue(rack, new[] { belts });
			rack.StoredClips.Clear();
			for (int i = 0; i < belts; i++) rack.StoredClips.Add(belt);
		}

		static void AddGuidanceUnit(WeaponSystem weapon)
		{
			if (weapon.GuidanceUnit != null)
			{
				if (S.BeamOnBaseUnit) BeamOnBase(weapon);
				return;
			}
			var parent = weapon.FCS.transform.parent;
			var guidanceObject = new GameObject(S.GuidanceName);
			guidanceObject.transform.SetParent(parent, false);
			guidanceObject.transform.localPosition = S.GuidancePosition;
			guidanceObject.transform.localEulerAngles = Vector3.zero;
			var mgu = guidanceObject.AddComponent<MissileGuidanceUnit>();
			// Sight line, not the gun's axis: the autoloader drops the gun to load mid-guidance and the missile followed it.
			var beam = new GameObject(S.BeamName).AddComponent<GuidanceBeam>();
			beam.transform.SetParent(weapon.FCS.GetComponentInParent<Vehicle>()?.transform ?? parent, false);
			beam.Fcs = weapon.FCS;
			beam.Gun = guidanceObject.transform;
			beam.Sync();
			mgu.AimElement = beam.transform;
			weapon.GuidanceUnit = mgu;
			weapon.Feed.ReloadDuringMissileTracking = false;
			// The feed only subscribes in RegisterWeaponSystem at Awake. Without this WaitingOnMissile sticks after the first
			// missile and the gun stops reloading by itself.
			FeedMissileGuidance.SetValue(weapon.Feed, mgu);
			mgu.GuidanceStopped += (Action)Delegate.CreateDelegate(typeof(Action), weapon.Feed, FeedGuidanceStopped);
			weapon.FireWhileGuidingMissile = false;
		}

		static void BeamOnBase(WeaponSystem weapon)
		{
			var mgu = weapon.GuidanceUnit;
			if (mgu.AimElement != null && mgu.AimElement.GetComponent<GuidanceBeam>() != null) return;
			string before = mgu.AimElement != null ? mgu.AimElement.name : "none";
			var beam = new GameObject(S.BeamName).AddComponent<GuidanceBeam>();
			beam.transform.SetParent(weapon.FCS.GetComponentInParent<Vehicle>()?.transform ?? mgu.transform, false);
			beam.Fcs = weapon.FCS;
			beam.Gun = mgu.transform;
			beam.Sync();
			mgu.AimElement = beam.transform;
		}
	}
}
