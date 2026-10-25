using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using HarmonyLib;
using Reticle;
using UnityEngine;

namespace T90A.Common
{
	internal static class GunnerFcs
	{
		internal sealed class Settings
		{
			public float LaserMin, LaserMax;
			public Vector2 LineOfSightLimits;
			public float LineOfSightAzimuth;
			public int RangeStep = 100;
			public int DisplayRangeIncrement = 50;
			public float FireGateAngle = 0.1f;
			public bool ManualMode = true;
		}

		internal static class AimClamp
		{
			static readonly Dictionary<FireControlSystem, List<UsableOptic>> Sights = new Dictionary<FireControlSystem, List<UsableOptic>>();
			static readonly Dictionary<FireControlSystem, float> Azimuth = new Dictionary<FireControlSystem, float>();
			static readonly MethodInfo SetAim = Reflect.PropertySetter(typeof(FireControlSystem), nameof(FireControlSystem.AimWorldVector));

			internal static void Register(FireControlSystem fcs, UsableOptic optic)
			{
				if (fcs == null || optic == null) return;
				foreach (var dead in Sights.Keys.Where(k => k == null).ToList()) Sights.Remove(dead);
				if (!Sights.TryGetValue(fcs, out var l)) Sights[fcs] = l = new List<UsableOptic>();
				if (!l.Contains(optic)) l.Add(optic);
			}

			internal static void Register(FireControlSystem fcs, UsableOptic optic, float azimuth)
			{
				Register(fcs, optic);
				foreach (var dead in Azimuth.Keys.Where(k => k == null).ToList()) Azimuth.Remove(dead);
				if (fcs != null && azimuth > 0f) Azimuth[fcs] = azimuth;
			}

			internal static void SightAzimuth(UsableOptic optic, float azimuth)
			{
				if (optic == null) return;
				foreach (var dead in OpticAzimuth.Keys.Where(k => k == null).ToList()) OpticAzimuth.Remove(dead);
				if (azimuth > 0f) OpticAzimuth[optic] = azimuth; else OpticAzimuth.Remove(optic);
			}

			static readonly Dictionary<UsableOptic, float> OpticAzimuth = new Dictionary<UsableOptic, float>();

			static float Yaw(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.AdjustAimByRatio))]
			static class Patch
			{
				static void Prefix(FireControlSystem __instance, out Vector3 __state)
				{
					__state = Vector3.zero;
					try { __state = __instance.AimWorldVector; }
					catch (Exception e) { PatchGuard.Failed("Sight aim limit", e); }
				}

				static void Postfix(FireControlSystem __instance, Vector3 __state)
				{
					try { Clamp(__instance, __state); }
					catch (Exception e) { PatchGuard.Failed("Sight aim limit", e); }
				}

				static void Clamp(FireControlSystem __instance, Vector3 __state)
				{
					if (!Sights.TryGetValue(__instance, out var optics)) return;
					var mode = __instance.CurrentStabMode;
					if (mode != StabilizationMode.Vector && mode != StabilizationMode.None) return;
					UsableOptic o = null;
					foreach (var x in optics) if (x != null && x.slot != null && x.slot.IsActive) { o = x; break; }
					if (o == null) return;
					var parent = o.slot.transform.parent;
					if (parent == null) return;
					var lim = o.LocalElevationLimits;
					float az0;
					if (OpticAzimuth.Count == 0 || !OpticAzimuth.TryGetValue(o, out az0)) Azimuth.TryGetValue(__instance, out az0);
					if (lim == Vector2.zero && az0 <= 0f) return;
					var d = parent.InverseTransformDirection(__instance.AimWorldVector).normalized;
					float e = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
					float lo = lim.x != 0f ? lim.x : -90f, hi = lim.y != 0f ? lim.y : 90f;
					float yaw = Yaw(d), yaw2 = yaw;
					if (az0 > 0f)
					{
						float a = Mathf.Max(az0, Mathf.Abs(Yaw(parent.InverseTransformDirection(__state))));
						yaw2 = Mathf.Clamp(yaw, -a, a);
					}
					if (e >= lo && e <= hi && yaw2 == yaw) return;
					float c = Mathf.Clamp(e, lo, hi) * Mathf.Deg2Rad;
					var flat = yaw2 != yaw ? new Vector3(Mathf.Sin(yaw2 * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw2 * Mathf.Deg2Rad)) : new Vector3(d.x, 0f, d.z);
					flat = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
					var w = parent.TransformDirection(flat * Mathf.Cos(c) + Vector3.up * Mathf.Sin(c));
					if (mode == StabilizationMode.Vector) __instance.SetAimVector(w, true);
					else SetAim.Invoke(__instance, new object[] { w });
				}
			}
		}

		internal static void Apply(UsableOptic dayOptic, Settings s)
		{
			FireControlSystem fcs = dayOptic.FCS;

			fcs.MaxLaserRange = s.LaserMax;
			fcs.RegisteredRangeLimits = new Vector2(0f, s.LaserMax);
			fcs.MarkRangeInvalidBelow = s.LaserMin;
			fcs.IgnoreRangeBelow = 0f;
			fcs.LaserAim = LaserAimMode.ImpactPoint;
			fcs.randomRadius = new AngularLength(5f, AngularLength.AngularUnit.MIL_NATO);
			fcs.RangeStep = s.RangeStep;
			fcs.RangeGranularity = 0;
			fcs.DisplayRangeIncrement = s.DisplayRangeIncrement;
			fcs.EnableRangeInterpolation = false;

			fcs.UseDeltaD = true;
			fcs.ImperfectDeltaD = true;

			fcs.CurrentStabMode = StabilizationMode.Vector;
			fcs.ImperfectFixedRangePointStab = false;
			fcs.PointStabIncludesVertical = false;
			fcs.SuperelevateWeapon = true;
			fcs.SuperleadWeapon = true;
			fcs.RefreshAimOnAmmoChange = true;

			fcs.RecordTraverseRateBuffer = true;
			fcs.TraverseBufferSeconds = 1f;
			fcs.DynamicLead = true;
			fcs.InertialCompensation = false;
			SetPrivate(fcs, "PointStabAffectsLead", false);
			SetPrivate(fcs, "InertiaLeadIncludesVertical", false);
			SetPrivate(fcs, "_counterMotionLeadAlwaysOn", false);
			SetPrivate(fcs, "_autoDumpViaPalmSwitches", false);
			SetPrivate(fcs, "_horizontalLeadOnly", true);
			SetPrivate(fcs, "_useSeparateLead", false);

			if (s.FireGateAngle >= 0f) fcs.FireGateAngle = s.FireGateAngle;
			fcs.SuperelevateFireGating = true;
			fcs.IgnoreHorizontalForFireGating = true;
			OwnLayGate(fcs);

			fcs.HasManualMode = s.ManualMode;
			fcs.ManualModeTriggers = s.ManualMode ? new[] { FcsManualModeStartTrigger.SetRangeManually } : new FcsManualModeStartTrigger[0];
			fcs.AutoModeTriggers = s.ManualMode ? new[] { FcsManualModeCancelTrigger.Lase } : new FcsManualModeCancelTrigger[0];
			fcs.ManualModeLead = FcsManualModeLead.DisableLeadCompensation;
			fcs.ManualModeSuperelevation = FcsManualModeSuperelevation.DisableSuperelevation;
			fcs.ManualModeOpticAlign = OpticAlignment.BoresightStabilized;
			fcs.ZeroReticleRangeInAutoMode = true;

			dayOptic.Alignment = OpticAlignment.BoresightStabilized;
			dayOptic.RotateElevation = true;
			dayOptic.RotateAzimuth = true;
			dayOptic.CantCorrect = true;
			dayOptic.ForceHorizontalReticleAlign = true;
			dayOptic.LocalElevationLimits = s.LineOfSightLimits;
			AimClamp.Register(fcs, dayOptic, s.LineOfSightAzimuth);
			SetPrivate(fcs, "_fixParallaxForVectorMode", false);

			Resync(fcs, dayOptic);
		}

		internal static readonly HashSet<FireControlSystem> OwnLay = new HashSet<FireControlSystem>();

		static void OwnLayGate(FireControlSystem fcs)
		{
			OwnLay.RemoveWhere(k => k == null);
			OwnLay.Add(fcs);
			fcs.FireGateOverrideTransform = null;
		}

		[DefaultExecutionOrder(-100)]
		internal sealed class HeadPoint : MonoBehaviour
		{
			internal Transform Turret;
			internal Vector3 Local;
			internal void Put() { if (Turret != null) transform.position = Turret.TransformPoint(Local); }

			void Update() => Put();
			void LateUpdate() => Put();
		}

		internal static class LaserAim
		{
			static readonly Dictionary<FireControlSystem, (UsableOptic sight, Settings s)> Sights = new Dictionary<FireControlSystem, (UsableOptic, Settings)>();

			internal static void Register(FireControlSystem fcs, UsableOptic sight, Settings s)
			{
				if (fcs == null || sight == null || s == null) return;
				foreach (var dead in Sights.Keys.Where(k => k == null).ToList()) Sights.Remove(dead);
				Sights[fcs] = (sight, s);
			}

			static void Hold(FireControlSystem fcs)
			{
				try { HoldNow(fcs); }
				catch (Exception e) { PatchGuard.Failed("Laser on the sight head", e); }
			}

			static void HoldNow(FireControlSystem fcs)
			{
				if (Sights.Count == 0 || !Sights.ContainsKey(fcs) || fcs.LaserOrigin == null) return;
				var w = Limit(fcs, fcs.LaserAim == LaserAimMode.ImpactPoint ? fcs.AimWorldVector : fcs.LaserOrigin.forward, out bool held);
				if (!held) return;
				fcs.LaserOrigin.forward = w;
			}

			internal static Vector3 Limit(FireControlSystem fcs, Vector3 aim, out bool held)
			{
				held = false;
				if (fcs == null || !Sights.TryGetValue(fcs, out var e) || e.sight == null || e.sight.slot == null) return aim;
				var parent = e.sight.slot.transform.parent;
				if (parent == null) return aim;
				var d = parent.InverseTransformDirection(aim).normalized;
				float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, el = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
				float a = e.s.LineOfSightAzimuth, lo = e.s.LineOfSightLimits.x != 0f ? e.s.LineOfSightLimits.x : -90f, hi = e.s.LineOfSightLimits.y != 0f ? e.s.LineOfSightLimits.y : 90f;
				float az2 = a > 0f ? Mathf.Clamp(az, -a, a) : az, el2 = Mathf.Clamp(el, lo, hi);
				if (az2 == az && el2 == el) return aim;
				held = true;
				float c = el2 * Mathf.Deg2Rad, h = az2 * Mathf.Deg2Rad;
				return parent.TransformDirection(new Vector3(Mathf.Sin(h) * Mathf.Cos(c), Mathf.Sin(c), Mathf.Cos(h) * Mathf.Cos(c)));
			}

			[HarmonyPatch(typeof(FireControlSystem), "UpdateLaserAim")]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance) => Hold(__instance);
			}

			[HarmonyPatch(typeof(FireControlSystem), "DoLase")]
			static class LasePatch
			{
				static void Prefix(FireControlSystem __instance) => Hold(__instance);
			}
		}

		static void Resync(FireControlSystem fcs, UsableOptic dayOptic)
		{
			SetPrivate(fcs, "_manualModeOnRangeSet", fcs.HasManualMode);
			SetPrivate(fcs, "_autoModeOnLase", fcs.HasManualMode);
			SetPrivate(fcs, "_originalSuperelevationMode", true);
			SetPrivate(fcs, "_originalSuperleadMode", true);
			SetPrivate(fcs, "_originalRangeLimits", fcs.RegisteredRangeLimits);
			SetPrivate(fcs, "_originalRangeStep", fcs.RangeStep);
			SetPrivate(fcs, "_recordedOriginalRangeValues", true);
			SetPrivate(fcs, "_originalMainOpticAlign", dayOptic.Alignment);

			if (!(Reflect.Field(typeof(FireControlSystem), "_initialized")?.GetValue(fcs) is bool initialized) || !initialized) return;

			Reflect.Method(typeof(FireControlSystem), "SetManualMode", new[] { typeof(bool) })?.Invoke(fcs, new object[] { false });
			Reflect.Method(typeof(FireControlSystem), "EngageLead")?.Invoke(fcs, null);
		}

		static void SetPrivate(object target, string field, object value)
		{
			Reflect.Field(typeof(FireControlSystem), field)?.SetValue(target, value);
		}
	}
}
