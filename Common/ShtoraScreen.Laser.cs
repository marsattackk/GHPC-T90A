using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.AI;
using GHPC.AI.Interfaces;
using GHPC.AI.Targeting;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Tells a Shtora tank it was lased, and has the AI lase back. An AI gunner whose laser line runs through the screen
	// goes to stadia ranging until it clears.
	internal static partial class ShtoraScreen
	{
		static Unit UnitOf(Collider c)
		{
			if (c == null) return null;
			var unit = c.GetComponentInParent<Unit>();
			if (unit != null) return unit;
			var lateFollow = c.transform.root.GetComponent<LateFollow>();
			return lateFollow != null && lateFollow.OriginalParent != null ? lateFollow.OriginalParent.GetComponent<Unit>() : null;
		}

		// GHPC's laser ray doesn't skip the shooter's own colliders. Step past them.
		static bool LineClear(Vector3 a, Vector3 b, Unit shooter, Unit target)
		{
			var constants = ConstantsAndInfoManager.Instance;
			if (constants == null) return true;
			var start = a;
			for (int i = 0; i < 4; i++)
			{
				var offset = b - start;
				float len = offset.magnitude;
				if (len < 0.5f) return true;
				var dir = offset / len;
				if (!RaycastColliderUtils.Raycast(new Ray(start, dir), out var hit, len, constants.LaserRangefinderLayerMask)) return true;
				var hitUnit = UnitOf(hit.collider);
				if (hitUnit != null && hitUnit == target) return true;
				if (hitUnit == null || hitUnit != shooter) return false;
				start = hit.point + dir * 0.05f;
			}
			return true;
		}

		static bool SmokeBetween(Vector3 a, Vector3 b)
		{
			var offset = b - a;
			return Physics.Raycast(a, offset, out var hit, offset.magnitude, 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY)
				&& hit.collider.CompareTag("Smoke");
		}

		[HarmonyPatch(typeof(FireControlSystem), "DoLase")]
		static class LasePatch
		{
			static void Postfix(FireControlSystem __instance)
			{
				if (Stations.Count == 0) return;
				try
				{
					if (__instance.LaserOrigin == null || __instance.MaxLaserRange <= 0f || LaserDestroyed(__instance)) return;
					var ray = new Ray(__instance.LaserOrigin.position, __instance.LaserOrigin.forward);
					float smoke = -1f;
					if (Physics.Raycast(ray, out var laserHit, __instance.MaxLaserRange, 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY)
						&& laserHit.collider.CompareTag("Smoke"))
						smoke = laserHit.distance;
					if (!RaycastColliderUtils.Raycast(
						ray,
						out var hit,
						__instance.MaxLaserRange,
						ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask))
						return;
					if (smoke >= 0f && smoke < hit.distance) return;
					var station = Of(UnitOf(hit.collider));
					var shooter = __instance.GetComponentInParent<Unit>();
					if (station == null || station.Vehicle == shooter) return;
					station.Lased(__instance.LaserOrigin.position, $"{shooter?.name ?? "?"}'s laser");
				}
				catch (Exception exception) { PatchGuard.Failed("Laser warning on a lase", exception); }
			}
		}

		static readonly Dictionary<UnitAI, object> Lasing = Shared<Dictionary<UnitAI, object>>("GHPCMods.ShtoraScreen.Lasing.v2");

		// One table per game, not per DLL: every port compiles its own copy of this class. Game and mscorlib types only,
		// no ValueTuple.
		static T Shared<T>(string key) where T : class, new()
		{
			var had = AppDomain.CurrentDomain.GetData(key);
			if (had is T shared) return shared;
			if (had != null)
			{
				MelonLogger.Warning($"Shtora screens: table {key} of another port has another layout; this port keeps its own.");
				return new T();
			}
			shared = new T();
			AppDomain.CurrentDomain.SetData(key, shared);
			return shared;
		}

		[HarmonyPatch(typeof(UnitAI), nameof(UnitAI.SetTarget), new[] { typeof(ITarget), typeof(bool) })]
		static class AiTargetPatch
		{
			static void Postfix(UnitAI __instance, ITarget __0)
			{
				var target = __0;
				if (Stations.Count == 0 || target == null) return;
				var station = Of(target.Owner);
				if (station == null || Lasing.TryGetValue(__instance, out var cur) && ReferenceEquals(cur, station)) return;
				if (__instance.Unit == null || __instance.Unit == station.Vehicle || !Lases(__instance, station.Vehicle)) return;
				var fcs = __instance.Unit.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.FCS;
				if (fcs == null || fcs.LaserOrigin == null || fcs.MaxLaserRange <= 0f) return;
				UnityKeys.Prune(Lasing);
				UnityKeys.Prune(Blinded);
				Lasing[__instance] = station;
				MelonCoroutines.Start(AiLasing(__instance, fcs, station));
			}
		}

		static readonly System.Reflection.FieldInfo GunnerAiField = Reflect.Field(typeof(UnitAI), "_gunnerAI");
		// MaxLaserRange is 4000 on every FCS, coincidence rangefinders included, so go by RangefinderType.
		// Neutral is out both ways. GHPC lets a Neutral unit target anyone.
		static bool Lases(UnitAI ai, Unit target)
		{
			var rangefinder = Blinded.TryGetValue(ai, out var was) ? BlindRf(was) : ai.RangefinderType;
			if (rangefinder != AimTimeCalculator.RangeFinderType.Laser) return false;
			if (GunnerAiField?.GetValue(ai) is Behaviour gunnerBehaviour && !gunnerBehaviour.enabled) return false;
			var aiFaction = ai.Unit.Allegiance;
			var targetFaction = target.Allegiance;
			return aiFaction != targetFaction && aiFaction != Faction.Neutral && targetFaction != Faction.Neutral;
		}

		const float BlindCheck = 0.25f;
		static readonly WaitForSeconds BlindWait = new WaitForSeconds(BlindCheck);
		static IEnumerator AiLasing(UnitAI ai, FireControlSystem fcs, Station st)
		{
			string why = "lasing stopped (error)";
			try
			{
				yield return new WaitForSeconds(UnityEngine.Random.Range(st.S.AiLaseDelay.x, st.S.AiLaseDelay.y));
				float nextLase = 0f;
				while (ai != null && st != null && st.Vehicle != null && !st.Vehicle.Neutralized && ai.Unit != null && !ai.Unit.Neutralized && ai.Target?.Owner == st.Vehicle && Lases(
					ai,
					st.Vehicle)
					&& Lasing.TryGetValue(ai, out var mine) && ReferenceEquals(mine, st))
				{
					bool smoke = false;
					if (!LaserDestroyed(fcs) && fcs.LaserOrigin != null)
					{
						var from = fcs.LaserOrigin.position;
						var targetPosition = st.Vehicle.Center != null ? st.Vehicle.Center.position : st.Vehicle.transform.position;
						bool inRange = (targetPosition - from).magnitude <= fcs.MaxLaserRange;
						smoke = inRange && SmokeBetween(from, targetPosition);
						if (Time.time >= nextLase)
						{
							nextLase = Time.time + st.S.AiRelase;
							if (inRange
								&& !smoke
								&& (LineClear(from, targetPosition, ai.Unit, st.Vehicle)
									|| LineClear(from, st.TurretPoint, ai.Unit, st.Vehicle)))
								st.Lased(
									from,
									$"{ai.Unit.name}'s rangefinder (AI, {(Blinded.TryGetValue(ai, out var original) ? BlindRf(original) : ai.RangefinderType)})");
						}
					}
					Blind(ai, st, smoke && st.S.BlindAi, "laser line clear");
					yield return BlindWait;
				}
				why = ai == null
					? "shooter gone"
					: st == null || st.Vehicle == null || st.Vehicle.Neutralized
						? "unit gone"
						: ai.Unit == null || ai.Unit.Neutralized ? "shooter knocked out"
							: ai.Target?.Owner != st.Vehicle ? "target changed" : "stopped lasing (gunner AI off)";
			}
			finally { EndLasing(ai, st, why); }
		}

		static void EndLasing(UnitAI ai, Station st, string why)
		{
			bool mine = Lasing.TryGetValue(ai, out var now) && ReferenceEquals(now, st);
			if (mine) Lasing.Remove(ai);
			if (ai == null)
			{
				Blinded.Remove(ai);
				return;
			}
			if (mine || !Lasing.ContainsKey(ai))
			{
				try { Blind(ai, st, false, why); }
				catch (Exception exception)
				{
					Blinded.Remove(ai);
					MelonLogger.Warning("Shtora screens: an AI's own ranging couldn't be restored: " + exception.Message);
				}
			}
		}

		static readonly Dictionary<UnitAI, object[]> Blinded = Shared<Dictionary<UnitAI, object[]>>("GHPCMods.ShtoraScreen.Blinded.v2");
		static AimTimeCalculator.RangeFinderType BlindRf(object[] was)
			=> (AimTimeCalculator.RangeFinderType)was[0];

		static void Blind(UnitAI ai, Station st, bool on, string why)
		{
			var rangeModifier = ai?.AccuracyModifiers?.Range;
			if (rangeModifier == null) return;
			bool now = Blinded.TryGetValue(ai, out var was);
			if (on == now) return;
			if (on)
			{
				Blinded[ai] = new object[]
				{
					ai.RangefinderType,
					rangeModifier.Min,
					rangeModifier.Max,
					rangeModifier.IncreaseAccuracyPerShot
				};
				ai.RangefinderType = AimTimeCalculator.RangeFinderType.Stadia;
				rangeModifier.Min = st.S.BlindRange.x;
				rangeModifier.Max = st.S.BlindRange.y;
				rangeModifier.IncreaseAccuracyPerShot = true;
				rangeModifier.Reset();
			}
			else
			{
				Blinded.Remove(ai);
				ai.RangefinderType = BlindRf(was);
				rangeModifier.Min = (float)was[1];
				rangeModifier.Max = (float)was[2];
				rangeModifier.IncreaseAccuracyPerShot = (bool)was[3];
				rangeModifier.Reset();
			}
		}
	}
}
