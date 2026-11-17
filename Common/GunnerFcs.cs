using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace T90A.Common
{
	internal static class UnityV3
	{
		internal static V3 ToV3(this Vector3 vector) => new V3(vector.x, vector.y, vector.z);
	}

	// Patches on the gunner's FireControlSystem. Most of the trouble is the sight's stops and the fire gate.
	internal static class GunnerFcs
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Tag;
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
			public bool LeadLikeM1;
		}
#pragma warning restore CS0649

		// AimWorldVector takes input without limit. Past a stabilised sight's stop it piles up unseen and the sight sticks
		// until it's wound back off. In azimuth the view has no limit at all.
		internal static class AimClamp
		{
			static readonly Dictionary<FireControlSystem, List<UsableOptic>> Sights =
				new Dictionary<FireControlSystem, List<UsableOptic>>();
			static readonly Dictionary<FireControlSystem, float> Azimuth = new Dictionary<FireControlSystem, float>();
			static readonly MethodInfo SetAim = Reflect.PropertySetter(typeof(FireControlSystem), nameof(FireControlSystem.AimWorldVector));

			internal static void Register(FireControlSystem fcs, UsableOptic optic)
			{
				if (fcs == null || optic == null) return;
				UnityKeys.Prune(Sights);
				if (!Sights.TryGetValue(fcs, out var optics)) Sights[fcs] = optics = new List<UsableOptic>();
				if (!optics.Contains(optic)) optics.Add(optic);
			}

			internal static void Register(FireControlSystem fcs, UsableOptic optic, float azimuth)
			{
				Register(fcs, optic);
				UnityKeys.Prune(Azimuth);
				if (fcs != null && azimuth > 0f) Azimuth[fcs] = azimuth;
			}

			internal static void SightAzimuth(UsableOptic optic, float azimuth)
			{
				if (optic == null) return;
				UnityKeys.Prune(OpticAzimuth);
				if (azimuth > 0f) OpticAzimuth[optic] = azimuth;
				else OpticAzimuth.Remove(optic);
			}

			static readonly Dictionary<UsableOptic, float> OpticAzimuth = new Dictionary<UsableOptic, float>();

			internal static float StopOfView(FireControlSystem fcs, float fallback)
			{
				if (fcs == null || OpticAzimuth.Count == 0 || !Sights.TryGetValue(fcs, out var optics)) return fallback;
				return GunnerFcsMath.StopAzimuth(
					optics.Where(o => o != null && o.slot != null).Select(x => new KeyValuePair<bool, float>(
						x.slot.IsActive,
						OpticAzimuth.TryGetValue(x, out float own) ? own : 0f)),
					fallback);
			}

			internal static float AzimuthOf(FireControlSystem fcs)
				=> fcs != null && Azimuth.TryGetValue(fcs, out float azimuth) ? azimuth : 0f;

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.AdjustAimByRatio))]
			static class Patch
			{
				static void Prefix(FireControlSystem __instance, out Vector3 __state)
				{
					__state = Vector3.zero;
					try { __state = __instance.AimWorldVector; }
					catch (Exception exception) { PatchGuard.Failed("Sight aim limit", exception); }
				}

				static void Postfix(FireControlSystem __instance, Vector3 __state)
				{
					try { Clamp(__instance, __state); }
					catch (Exception exception) { PatchGuard.Failed("Sight aim limit", exception); }
				}

				static void Clamp(FireControlSystem __instance, Vector3 __state)
				{
					if (!Sights.TryGetValue(__instance, out var optics)) return;
					var mode = __instance.CurrentStabMode;
					if (mode != StabilizationMode.Vector && mode != StabilizationMode.None) return;
					if (mode == StabilizationMode.None && SetAim == null) return;
					UsableOptic activeOptic = null;
					foreach (var optic in optics)
						if (optic != null && optic.slot != null && optic.slot.IsActive)
						{
							activeOptic = optic;
							break;
						}
					if (activeOptic == null) return;
					var parent = activeOptic.slot.transform.parent;
					if (parent == null) return;
					var lim = activeOptic.LocalElevationLimits;
					float az0;
					if (OpticAzimuth.Count == 0 || !OpticAzimuth.TryGetValue(activeOptic, out az0))
						Azimuth.TryGetValue(__instance, out az0);
					if (!GunnerFcsMath.HasAimLimits(lim.x, lim.y, az0)) return;
					var direction = parent.InverseTransformDirection(__instance.AimWorldVector).normalized;
					var before = parent.InverseTransformDirection(__state);
					if (!GunnerFcsMath.ClampAim(direction.ToV3(), before.ToV3(), lim.x, lim.y, az0, out var clamped)) return;
					var worldDirection = parent.TransformDirection(new Vector3(clamped.x, clamped.y, clamped.z));
					if (mode == StabilizationMode.Vector) __instance.SetAimVector(worldDirection, true);
					else SetAim.Invoke(__instance, new object[] { worldDirection });
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
				NoRangeDeltaD.Fcs.RemoveWhere(f => f == null);
				NoRangeDeltaD.Fcs.Add(fcs);
			}

			fcs.CurrentStabMode = StabilizationMode.Vector;
			fcs.ImperfectFixedRangePointStab = false;
			fcs.PointStabIncludesVertical = false;
			fcs.SuperelevateWeapon = true;
			fcs.SuperleadWeapon = true;
			fcs.RefreshAimOnAmmoChange = true;

			fcs.RecordTraverseRateBuffer = true;
			fcs.TraverseBufferSeconds = s.LeadLikeM1 ? 0.5f : 1f;
			fcs.DynamicLead = true;
			fcs.InertialCompensation = false;
			SetPrivate(fcs, "PointStabAffectsLead", false);
			SetPrivate(fcs, "InertiaLeadIncludesVertical", false);
			SetPrivate(fcs, "_counterMotionLeadAlwaysOn", false);
			SetPrivate(fcs, "_autoDumpViaPalmSwitches", s.LeadLikeM1);
			SetPrivate(fcs, "_horizontalLeadOnly", !s.LeadLikeM1);
			SetPrivate(fcs, "_useSeparateLead", false);

			if (s.FireGateAngle >= 0f) fcs.FireGateAngle = s.FireGateAngle;
			fcs.SuperelevateFireGating = true;
			fcs.IgnoreHorizontalForFireGating = true;
			bool laid = s.FireWhenLaid == null || s.FireWhenLaid();
			if (laid)
			{
				GateOnGun.Remove(fcs);
				OwnLayGate(fcs);
				LaidGate.Register(fcs);
			}
			else
			{
				OwnLay.Remove(fcs);
				LaidGate.Fcs.Remove(fcs);
				GateOnGun.RemoveWhere(f => f == null);
				if (s.GateStaysOnGun) GateOnGun.Add(fcs);
				else GateOnGun.Remove(fcs);
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
			if (s.ViewStopsAtAzimuth && s.LineOfSightAzimuth > 0f)
				dayOptic.LocalAzimuthLimits = new Vector2(-s.LineOfSightAzimuth, s.LineOfSightAzimuth);
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
			OwnLay.RemoveWhere(f => f == null);
			OwnLay.Add(fcs);
			fcs.FireGateOverrideTransform = null;
		}

		// GHPC's DegreesOffset gate reads 0 outside the drive's stabilised mode and when the goal is clipped to the barrel:
		// the gun fires wherever it is. Measure the lay yourself.
		// Range 0 crept up by hull jitter froze the gun: the goal became range x aim, a vector Unity takes for zero.
		// It creeps before the port registers the fire control too: put back any range under NoRange, not only one leaving 0.
		internal static class NoRangeDeltaD
		{
			internal static readonly HashSet<FireControlSystem> Fcs = new HashSet<FireControlSystem>();
			internal const float NoRange = 1f;

			[HarmonyPatch(typeof(FireControlSystem), "Update")]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance)
				{
					try
					{
						if (!Fcs.Contains(__instance) || !__instance.ActiveDeltaD) return;
						float range = __instance.TargetRange;
						if (range > 0f && range < NoRange)
						{
							__instance.TargetRange = 0f;
						}
					}
					catch (Exception exception) { PatchGuard.Failed("No-range delta-D", exception); }
				}
			}
		}

		internal static class LaidGate
		{
			internal static readonly HashSet<FireControlSystem> Fcs = new HashSet<FireControlSystem>();

			internal static void Register(FireControlSystem fcs)
			{
				Fcs.RemoveWhere(f => f == null);
				if (fcs != null) Fcs.Add(fcs);
			}

			internal static float Off(FireControlSystem fcs)
			{
				if (fcs == null || !fcs.StabsActive || fcs.CurrentRange <= 0f || !fcs.SuperelevateWeapon) return 0f;
				var gated = fcs.GatedAimablePlatforms;
				if (gated == null || gated.Length == 0) return 0f;
				Vector3 lay = fcs.CompensatedWeaponVector(fcs.SuperleadWeapon);
				Drives.Clear();
				foreach (var platform in gated)
				{
					if (platform == null || platform.Transform == null) continue;
					// A traverse drive in GatedAimablePlatforms kept this gate shut for good after a lase: measure elevation drives only.
					if (platform.Axis == GHPC.Utility.EnumHelpers.Axis3.Y) continue;
					var axes = platform.Transform;
					Drives.Add(new GunnerFcsMath.Drive(axes.right.ToV3(), axes.forward.ToV3(), false));
				}
				return GunnerFcsMath.LaidOff(lay.ToV3(), Drives);
			}

			static readonly List<GunnerFcsMath.Drive> Drives = new List<GunnerFcsMath.Drive>();

			internal static bool Shut(FireControlSystem fcs) => GunnerFcsMath.GateShut(Off(fcs), fcs.FireGateAngle);

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.FiringBlocked), MethodType.Getter)]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance, ref bool __result)
				{
					try
					{
						if (Fcs.Count == 0 || !Fcs.Contains(__instance)) return;
						bool shut = Shut(__instance);
						if (shut) __result = true;
					}
					catch (Exception exception) { PatchGuard.Failed("Fire only when laid", exception); }
				}
			}
		}

		// DoLase doesn't skip the unit's own colliders, but a ray starting inside one misses it.
		internal static Transform LaserWindow(Transform root, FireControlSystem fcs, Transform turret, Vector3 window, string name)
		{
			if (fcs == null || turret == null) return null;
			var origin = new GameObject(name).transform;
			origin.SetParent(turret, false);
			origin.SetPositionAndRotation(root.TransformPoint(window), root.rotation);
			fcs.LaserOrigin = origin;
			return origin;
		}

		internal static void HeadView(string tag, Transform window, params Transform[] parts)
		{
			if (window == null || window.parent == null)
			{
				MelonLogger.Warning($"{tag}: no laser window; the sight's view stays on the base sight.");
				return;
			}
			foreach (var part in parts.Where(c => c != null).Distinct())
			{
				var headPoint = part.GetComponent<HeadPoint>() ?? part.gameObject.AddComponent<HeadPoint>();
				headPoint.Turret = window.parent;
				headPoint.Local = window.localPosition;
				headPoint.Put();
			}
		}

		// GHPC measures a stabilised sight's elevation stop from its slot's parent: on the gun, the gun's moves drag the view.
		internal static void MountOnTurret(string tag, Transform root, Transform turret, params Transform[] parts)
		{
			if (root == null || turret == null) return;
			var frame = new GameObject(tag + " sight frame").transform;
			frame.SetParent(turret, false);
			frame.SetPositionAndRotation(turret.position, root.rotation);
			foreach (var part in parts.Where(c => c != null).Distinct())
			{
				string was = part.parent != null ? part.parent.name : "none";
				part.SetParent(frame, true);
			}
		}

		// At range 0 the parallax term alone lays the gun from the muzzle back at the sight head.
		internal static class Parallax
		{
			internal static readonly HashSet<FireControlSystem> Fcs = new HashSet<FireControlSystem>();
			internal const float MinRange = 10f;
			static readonly FieldInfo Flag = Reflect.Field(typeof(FireControlSystem), "_fixParallaxForVectorMode");

			internal static void Register(FireControlSystem fcs)
			{
				Fcs.RemoveWhere(f => f == null);
				if (fcs != null) Fcs.Add(fcs);
			}

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.CompensatedWeaponVector))]
			static class Patch
			{
				static void Prefix(FireControlSystem __instance)
				{
					try { Fix(__instance); }
					catch (Exception exception) { PatchGuard.Failed("Gun parallax", exception); }
				}

				static void Fix(FireControlSystem __instance)
				{
					if (Flag == null || Fcs.Count == 0 || !Fcs.Contains(__instance)) return;
					Flag.SetValue(__instance, __instance.CurrentRange >= MinRange);
				}
			}
		}

		// GHPC lays the gun parallel to the sight's line; a sight 0.6 m off the bore needs its lines converged.
		internal static class Converge
		{
			static readonly Dictionary<FireControlSystem, List<(CameraSlot slot, float distance)>> Sights =
				new Dictionary<FireControlSystem, List<(CameraSlot, float)>>();

			internal static void Register(FireControlSystem fcs, CameraSlot slot, float distance)
			{
				if (fcs == null || slot == null || distance <= 0f) return;
				UnityKeys.Prune(Sights);
				if (!Sights.TryGetValue(fcs, out var entries)) Sights[fcs] = entries = new List<(CameraSlot, float)>();
				entries.RemoveAll(e => e.slot == slot);
				entries.Add((slot, distance));
			}

			// Quaternion == / Angle call a turn under ~0.08 deg identity. Keep tiny turns as vectors.
			internal static bool Lines(FireControlSystem fcs, out Vector3 sight, out Vector3 toPoint)
			{
				sight = toPoint = Vector3.zero;
				if (Sights.Count == 0 || fcs == null || !Sights.TryGetValue(fcs, out var entries)) return false;
				var active = CameraSlot.ActiveInstance;
				foreach (var (slot, distance) in entries)
				{
					if (slot == null || slot != active) continue;
					var weapon = fcs.LinkedWeaponSystems != null && fcs.LinkedWeaponSystems.Length > 0 ? fcs.LinkedWeaponSystems[0] : null;
					var aim = fcs.AimWorldVector;
					if (weapon == null || weapon.MuzzleIdentity == null || aim.sqrMagnitude < 1e-6f) return false;
					sight = aim.normalized;
					toPoint = (slot.transform.position + sight * distance - weapon.MuzzleIdentity.position).normalized;
					return true;
				}
				return false;
			}

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.CompensatedWeaponVector))]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance, ref Vector3 __result)
				{
					try
					{
						if (Sights.Count == 0 || !Lines(__instance, out var sight, out var toPoint)) return;
						var turn = toPoint - sight;
						__result += turn * __result.magnitude;
					}
					catch (Exception exception) { PatchGuard.Failed("Sight convergence", exception); }
				}
			}
		}

		// GHPC's lead has no limit: tracking rate x range x flight time, tens of degrees after a long lase.
		internal static class LeadClamp
		{
			static readonly Dictionary<FireControlSystem, float> Limit = new Dictionary<FireControlSystem, float>();

			internal static void Register(FireControlSystem fcs, float deg)
			{
				UnityKeys.Prune(Limit);
				if (fcs != null && deg > 0f) Limit[fcs] = deg;
			}

			internal static void NoLeadWhile(FireControlSystem fcs, CameraSlot slot)
			{
				UnityKeys.Prune(Off);
				if (fcs == null || slot == null) return;
				if (!Off.TryGetValue(fcs, out var slots)) Off[fcs] = slots = new List<CameraSlot>();
				if (!slots.Contains(slot)) slots.Add(slot);
			}

			static readonly Dictionary<FireControlSystem, List<CameraSlot>> Off =
				new Dictionary<FireControlSystem, List<CameraSlot>>();

			[HarmonyPatch(typeof(FireControlSystem), "GetLeadWorldOffset")]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance, ref Vector3 __result)
				{
					try
					{
						if (Off.Count > 0 && Off.TryGetValue(__instance, out var slots) && slots.Any(s => s != null && s.IsActive))
						{
							__result = Vector3.zero;
							return;
						}
						if (Limit.Count == 0 || !Limit.TryGetValue(__instance, out float deg)) return;
						float max = Mathf.Max(__instance.CurrentRange, 1f) * Mathf.Tan(deg * Mathf.Deg2Rad);
						if (__result.sqrMagnitude <= max * max) return;
						__result = __result.normalized * max;
					}
					catch (Exception exception) { PatchGuard.Failed("Lead limit", exception); }
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
				if (!_hooked)
				{
					_hooked = true;
					Camera.onPreCull += PreCull;
				}
			}

			void OnDisable() => Live.Remove(this);
			static void PreCull(Camera _) => PutAll();
			static void PutAll()
			{
				foreach (var headPoint in Live) if (headPoint != null) headPoint.Put();
			}

			// GHPC's instancer draws grass and trees only for a camera that hasn't moved since its LateUpdate, so the head goes in first.
			[HarmonyPatch]
			static class BeforeVegetation
			{
				static bool _warned;
				static MethodBase Target()
					=> AccessTools.Method(
						AccessTools.TypeByName("GHPC.Rendering.Instancing.InstancedMeshRenderer"),
						"LateUpdate");
				static bool Prepare()
				{
					bool found = Target() != null;
					if (!found && !_warned) _warned = true;
					else return found;
					MelonLogger.Warning("Gunner's sight head view: GHPC's vegetation renderer not found; grass and trees may drop out in the sight while the gun moves.");
					return found;
				}

				static MethodBase TargetMethod() => Target();
				static void Prefix()
				{
					try { if (Live.Count > 0) PutAll(); }
					catch (Exception exception) { PatchGuard.Failed("Head view before vegetation", exception); }
				}
			}
		}

		// UpdateLaserAim points the laser at the aim wherever the turret is. Mid-swing that's straight through the turret's own front.
		internal static class LaserAim
		{
			static readonly Dictionary<FireControlSystem, (UsableOptic sight, Settings s)> Sights =
				new Dictionary<FireControlSystem, (UsableOptic, Settings)>();

			internal static void Register(FireControlSystem fcs, UsableOptic sight, Settings s)
			{
				if (fcs == null || sight == null || s == null) return;
				UnityKeys.Prune(Sights);
				Sights[fcs] = (sight, s);
			}

			static void Hold(FireControlSystem fcs)
			{
				try { HoldNow(fcs); }
				catch (Exception exception) { PatchGuard.Failed("Laser on the sight head", exception); }
			}

			// A sight with its own rangefinder lases from its own window while it's the view, else the spot sits off its centre.
			internal static void FromWhile(FireControlSystem fcs, CameraSlot slot, Transform window, float maxRange)
			{
				foreach (var dead in Others.Keys.Where(k => k == null).ToList())
				{
					Others.Remove(dead);
					Home.Remove(dead);
				}
				if (fcs == null || slot == null || window == null) return;
				if (!Others.TryGetValue(fcs, out var others)) Others[fcs] = others = new List<(CameraSlot, Transform, float)>();
				others.RemoveAll(o => o.slot == slot);
				others.Add((slot, window, maxRange));
			}

			static readonly Dictionary<FireControlSystem, List<(CameraSlot slot, Transform window, float max)>> Others =
				new Dictionary<FireControlSystem, List<(CameraSlot, Transform, float)>>();
			static readonly Dictionary<FireControlSystem, (Transform origin, float max)> Home =
				new Dictionary<FireControlSystem, (Transform, float)>();

			static void HoldNow(FireControlSystem fcs)
			{
				if (Sights.Count == 0 || !Sights.ContainsKey(fcs) || fcs.LaserOrigin == null) return;
				if (Others.Count > 0 && Others.TryGetValue(fcs, out var others))
				{
					if (!others.Any(o => o.window == fcs.LaserOrigin)) Home[fcs] = (fcs.LaserOrigin, fcs.MaxLaserRange);
					Home.TryGetValue(fcs, out var home);
					var want = others.FirstOrDefault(o => o.slot != null && o.slot.IsActive && o.window != null);
					if (want.window != null)
					{
						fcs.LaserOrigin = want.window;
						fcs.MaxLaserRange = want.max > 0f ? want.max : home.origin != null ? home.max : fcs.MaxLaserRange;
						if (fcs.LaserAim == LaserAimMode.ImpactPoint) want.window.forward = fcs.AimWorldVector;
						return;
					}
					if (fcs.LaserOrigin != home.origin && home.origin != null)
					{
						fcs.LaserOrigin = home.origin;
						fcs.MaxLaserRange = home.max;
					}
				}
				var limited = Limit(
					fcs,
					fcs.LaserAim == LaserAimMode.ImpactPoint ? fcs.AimWorldVector : fcs.LaserOrigin.forward,
					out bool held);
				if (!held) return;
				fcs.LaserOrigin.forward = limited;
			}

			internal static Vector3 Limit(FireControlSystem fcs, Vector3 aim, out bool held)
			{
				held = false;
				if (fcs == null || !Sights.TryGetValue(fcs, out var entry) || entry.sight == null || entry.sight.slot == null) return aim;
				var parent = entry.sight.slot.transform.parent;
				if (parent == null) return aim;
				var direction = parent.InverseTransformDirection(aim).normalized;
				float azimuth = Mathf.Atan2(
					direction.x,
					direction.z) * Mathf.Rad2Deg, elevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
				float azimuthLimit = AimClamp.StopOfView(fcs, entry.s.LineOfSightAzimuth), minElevation = entry.s.LineOfSightLimits.x != 0f
					? entry.s.LineOfSightLimits.x
					: -90f, maxElevation = entry.s.LineOfSightLimits.y != 0f ? entry.s.LineOfSightLimits.y : 90f;
				float az2 = azimuthLimit > 0f
					? Mathf.Clamp(azimuth, -azimuthLimit, azimuthLimit)
					: azimuth, el2 = Mathf.Clamp(elevation, minElevation, maxElevation);
				if (az2 == azimuth && el2 == elevation) return aim;
				held = true;
				float elevationRadians = el2 * Mathf.Deg2Rad, azimuthRadians = az2 * Mathf.Deg2Rad;
				return parent.TransformDirection(new Vector3(
					Mathf.Sin(azimuthRadians) * Mathf.Cos(elevationRadians),
					Mathf.Sin(elevationRadians),
					Mathf.Cos(azimuthRadians) * Mathf.Cos(elevationRadians)));
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
