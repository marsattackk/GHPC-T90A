using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
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
			public bool ViewStopsAtAzimuth;
			public float LeadLimit;
			public int RangeStep = 100;
			public int DisplayRangeIncrement = 50;
			public float FireGateAngle = 0.1f;
			public Func<bool> FireWhenLaid;
			public bool GateStaysOnGun;
			public bool ManualMode = true;
		}

		// AimWorldVector takes input without limit. Past a stabilised sight's stop it piles up unseen and the sight sticks
		// until it's wound back off. In azimuth the view has no limit at all.
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

			// Delta-D on a fire control without TurretPlatform floods Player.log with an error every frame.
			if (fcs.TurretPlatform != null)
			{
				fcs.UseDeltaD = true;
				fcs.ImperfectDeltaD = true;
				NoRangeDeltaD.Fcs.Add(fcs);
			}

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
			bool laid = s.FireWhenLaid == null || s.FireWhenLaid();
			if (laid)
			{
				OwnLayGate(fcs);
				LaidGate.Register(fcs);
			}
			else
			{
				OwnLay.Remove(fcs);
				LaidGate.Fcs.Remove(fcs);
				GateOnGun.RemoveWhere(k => k == null);
				if (s.GateStaysOnGun) GateOnGun.Add(fcs); else GateOnGun.Remove(fcs);
			}

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
			if (s.ViewStopsAtAzimuth && s.LineOfSightAzimuth > 0f) dayOptic.LocalAzimuthLimits = new Vector2(-s.LineOfSightAzimuth, s.LineOfSightAzimuth);
			AimClamp.Register(fcs, dayOptic, s.LineOfSightAzimuth);
			LeadClamp.Register(fcs, s.LeadLimit);
			SetPrivate(fcs, "_fixParallaxForVectorMode", false);

			Resync(fcs, dayOptic);
		}

		// With an override GHPC opens the gate within superelevation + FireGateAngle of the sight line, sign ignored, so a
		// shot before the gun has climbed goes low by up to the whole drop (3BK18M at 1500 m: 17 m).
		internal static readonly HashSet<FireControlSystem> OwnLay = new HashSet<FireControlSystem>();

		// In the thermal view the gate went onto the old TPN-1-49-23 slot, off the gun: no shot until the range was zeroed.
		internal static readonly HashSet<FireControlSystem> GateOnGun = new HashSet<FireControlSystem>();

		static void OwnLayGate(FireControlSystem fcs)
		{
			OwnLay.RemoveWhere(k => k == null);
			OwnLay.Add(fcs);
			fcs.FireGateOverrideTransform = null;
		}

		// GHPC's DegreesOffset gate reads 0 outside the drive's stabilised mode and when the goal is clipped to the barrel:
		// the gun fires wherever it is. Measure the lay yourself.
		// Range 0 crept up by hull jitter froze the gun: the goal became range x aim, a vector Unity takes for zero.
		internal static class NoRangeDeltaD
		{
			internal static readonly HashSet<FireControlSystem> Fcs = new HashSet<FireControlSystem>();
			internal const float NoRange = 1f;

			[HarmonyPatch(typeof(FireControlSystem), "Update")]
			static class Patch
			{
				static void Prefix(FireControlSystem __instance, out float __state) => __state = __instance.TargetRange;

				static void Postfix(FireControlSystem __instance, float __state)
				{
					try
					{
						if (__state != 0f || !Fcs.Contains(__instance) || !__instance.ActiveDeltaD) return;
						float r = __instance.TargetRange;
						if (r > 0f && r < NoRange)
						{
							__instance.TargetRange = 0f;
						}
					}
					catch (Exception e) { PatchGuard.Failed("No-range delta-D", e); }
				}
			}
		}

		internal static class LaidGate
		{
			internal static readonly HashSet<FireControlSystem> Fcs = new HashSet<FireControlSystem>();
			const float DefaultAngle = 0.1f;
			const int EpisodesLogged = 5;
			static readonly Dictionary<FireControlSystem, Frame> _frames = new Dictionary<FireControlSystem, Frame>();
			sealed class Frame { public bool Shut; public float Off; public bool Holding; public int Episodes, Held; }

			internal static void Register(FireControlSystem fcs)
			{
				Fcs.RemoveWhere(k => k == null);
				foreach (var k in _frames.Keys.Where(k => k == null).ToList()) _frames.Remove(k);
				if (fcs != null) Fcs.Add(fcs);
			}

			internal static float Off(FireControlSystem fcs)
			{
				if (fcs == null || !fcs.StabsActive || fcs.CurrentRange <= 0f || !fcs.SuperelevateWeapon) return 0f;
				var gated = fcs.GatedAimablePlatforms;
				if (gated == null || gated.Length == 0) return 0f;
				Vector3 lay = fcs.CompensatedWeaponVector(fcs.SuperleadWeapon);
				if (lay == Vector3.zero) return 0f;
				float worst = 0f;
				foreach (var p in gated)
				{
					if (p == null || p.Transform == null) continue;
					Vector3 right = p.Transform.right;
					float a = Vector3.Angle(Vector3.ProjectOnPlane(lay, right), Vector3.ProjectOnPlane(p.Transform.forward, right));
					if (a > worst) worst = a;
				}
				return worst;
			}

			internal static bool Shut(FireControlSystem fcs)
			{
				if (!_frames.TryGetValue(fcs, out var f)) _frames[fcs] = f = new Frame();
				f.Off = Off(fcs);
				float gate = fcs.FireGateAngle > 0f ? fcs.FireGateAngle : DefaultAngle;
				f.Shut = f.Off > gate;
				return f.Shut;
			}

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.FiringBlocked), MethodType.Getter)]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance, ref bool __result)
				{
					try
					{
						if (Fcs.Count == 0 || !Fcs.Contains(__instance)) return;
						bool shut = Shut(__instance);
						var f = _frames[__instance];
						if (shut && !__result)
						{
							if (!f.Holding) { f.Holding = true; f.Held = 0; }
							f.Held++;
						}
						else if (f.Holding)
						{
							f.Holding = false;
							if (f.Held > 0 && f.Episodes < EpisodesLogged) f.Episodes++;
						}
						if (shut) __result = true;
					}
					catch (Exception e) { PatchGuard.Failed("Fire only when laid", e); }
				}
			}
		}

		// GHPC's lead has no limit: tracking rate x range x flight time, tens of degrees after a long lase.
		internal static class LeadClamp
		{
			static readonly Dictionary<FireControlSystem, float> Limit = new Dictionary<FireControlSystem, float>();

			internal static void Register(FireControlSystem fcs, float deg)
			{
				foreach (var dead in Limit.Keys.Where(k => k == null).ToList()) Limit.Remove(dead);
				if (fcs != null && deg > 0f) Limit[fcs] = deg;
			}

			static readonly Dictionary<FireControlSystem, List<CameraSlot>> Off = new Dictionary<FireControlSystem, List<CameraSlot>>();

			[HarmonyPatch(typeof(FireControlSystem), "GetLeadWorldOffset")]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance, ref Vector3 __result)
				{
					try
					{
						if (Off.Count > 0 && Off.TryGetValue(__instance, out var slots) && slots.Any(x => x != null && x.IsActive)) { __result = Vector3.zero; return; }
						if (Limit.Count == 0 || !Limit.TryGetValue(__instance, out float deg)) return;
						float max = Mathf.Max(__instance.CurrentRange, 1f) * Mathf.Tan(deg * Mathf.Deg2Rad);
						if (__result.sqrMagnitude <= max * max) return;
						__result = __result.normalized * max;
					}
					catch (Exception e) { PatchGuard.Failed("Lead limit", e); }
				}
			}
		}

		[DefaultExecutionOrder(-100)]
		internal sealed class HeadPoint : MonoBehaviour
		{
			internal Transform Turret;
			internal Vector3 Local;
			internal void Put() { if (Turret != null) transform.position = Turret.TransformPoint(Local); }

			void Update() => Put();
			void LateUpdate() => Put();

			// AimablePlatform turns the turret in its LateUpdate, after this one, so once more before each camera culls.
			static readonly HashSet<HeadPoint> Live = new HashSet<HeadPoint>();
			static bool _hooked;
			void OnEnable()
			{
				Live.Add(this);
				if (!_hooked) { _hooked = true; Camera.onPreCull += PreCull; }
			}

			void OnDisable() => Live.Remove(this);
			static void PreCull(Camera _) => PutAll();
			static void PutAll()
			{
				foreach (var h in Live) if (h != null) h.Put();
			}

			// GHPC's instancer draws grass and trees only for a camera that hasn't moved since its LateUpdate, so the head goes in first.
			[HarmonyPatch]
			static class BeforeVegetation
			{
				static bool _warned;
				static MethodBase Target() => AccessTools.Method(AccessTools.TypeByName("GHPC.Rendering.Instancing.InstancedMeshRenderer"), "LateUpdate");
				static bool Prepare()
				{
					bool ok = Target() != null;
					if (!ok && !_warned) _warned = true; else return ok;
					MelonLoader.MelonLogger.Warning("Gunner's sight head view: GHPC's vegetation renderer not found; grass and trees may drop out in the sight while the gun moves.");
					return ok;
				}

				static MethodBase TargetMethod() => Target();
				static void Prefix()
				{
					try { if (Live.Count > 0) PutAll(); }
					catch (Exception e) { PatchGuard.Failed("Head view before vegetation", e); }
				}
			}
		}

		// UpdateLaserAim points the laser at the aim wherever the turret is, mid-swing that's through the turret's own front.
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

			static readonly Dictionary<FireControlSystem, List<(CameraSlot slot, Transform window, float max)>> Others = new Dictionary<FireControlSystem, List<(CameraSlot, Transform, float)>>();
			static readonly Dictionary<FireControlSystem, (Transform origin, float max)> Home = new Dictionary<FireControlSystem, (Transform, float)>();

			static void HoldNow(FireControlSystem fcs)
			{
				if (Sights.Count == 0 || !Sights.ContainsKey(fcs) || fcs.LaserOrigin == null) return;
				if (Others.Count > 0 && Others.TryGetValue(fcs, out var others))
				{
					if (!others.Any(o => o.window == fcs.LaserOrigin)) Home[fcs] = (fcs.LaserOrigin, fcs.MaxLaserRange);
					var home = Home[fcs];
					var want = others.FirstOrDefault(o => o.slot != null && o.slot.IsActive && o.window != null);
					if (want.window != null)
					{
						fcs.LaserOrigin = want.window;
						fcs.MaxLaserRange = want.max > 0f ? want.max : home.max;
						if (fcs.LaserAim == LaserAimMode.ImpactPoint) want.window.forward = fcs.AimWorldVector;
						return;
					}
					if (fcs.LaserOrigin != home.origin && home.origin != null) { fcs.LaserOrigin = home.origin; fcs.MaxLaserRange = home.max; }
				}
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

		// FireControlSystem caches these in Awake, RegisterOptic and DeferredStart.
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
