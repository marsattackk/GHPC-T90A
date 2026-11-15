using System.Collections.Generic;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using MelonLoader;
using Reticle;
using T90A.Common;
using TMPro;
using UnityEngine;

namespace T90A
{
	using DC = GHPC.Equipment.DestructibleComponent;

	// The TKN-4S: two optics as its day and night channels, tied to the Kord. Avtomat keeps the Kord on target through
	// traverse.
	internal static partial class CommanderSight
	{
		// Eye 1.5 cm proud of the head's front plate. Put it any deeper and the view's a black wall.
		internal static readonly Vector3 Head = new Vector3(0.600f, 2.240f, -0.665f);
		// Measured off the photo with the reticle's rangefinder as the scale. Suvorov's table says 7.
		internal const float DayFov = 7.8f;
		// Inferred, day field x 7.5 / 5.1. Nothing gives the night field.
		internal const float NightFov = 11.5f;
		internal static readonly Vector2 AutoElevation = new Vector2(-4f, 20f);
		internal static readonly Vector2 RangeLimits = new Vector2(0f, 1600f);
		internal static readonly Vector3 ZoneCentre = new Vector3(
			0.535f,
			2.0205f,
			-0.9025f), ZoneSize = new Vector3(0.416f, 0.501f, 0.235f);

		static readonly System.Reflection.FieldInfo SlotReparent = Reflect.Field(typeof(CameraSlot), "_reparentTarget");

		internal static void Configure(
			UsableOptic day,
			UsableOptic night,
			UsableOptic nightLook,
			Transform head,
			FireControlSystem fcs,
			WeaponSystem kord,
			GHPC.Weaponry.AmmoCodexScriptable b32,
			Transform level,
			CameraSlot pzu7,
			DC hitZone,
			ReticleSO reticle,
			ReticleSO nightReticle)
		{
			foreach (var optic in new[] { day, night })
			{
				optic.FCS = fcs;
				optic.RegisterAtStart = false;
				optic.Alignment = OpticAlignment.BoresightStabilized;
				optic.RotateElevation = true;
				optic.RotateAzimuth = false;
				optic.Mounts = new AimablePlatform[0];
				optic.LocalElevationLimits = AutoElevation;
				optic.LocalAzimuthLimits = Vector2.zero;
				optic.LevelTransform = level;
				optic.RegisteredAmmoTypes = new[] { b32 };
				optic.OverrideRangeLimits = RangeLimits;
				optic.OverrideRangeStep = 100;
				var slot = optic.slot;
				SlotReparent.SetValue(slot, head);
				slot.AllowFreeZoom = false;
				slot.OtherFovs = new float[0];
				slot.ExclusiveWeapons = new[] { kord };
				slot.HitZone = hitZone;
				slot.SlotToUseOnDestroy = pzu7;
				slot.IsExterior = false;
				var opticReticle = optic == night ? nightReticle : reticle;
				if (optic.reticleMesh != null && opticReticle != null)
				{
					optic.reticleMesh.reticleSO = opticReticle;
					ReticleMeshes.ClearSmr(optic.reticleMesh);
				}
				var round = b32.AmmoType;
				optic.gameObject.AddComponent<ReticleRangeLine>().Init(optic, fcs, () => round);
			}
			day.name = "TKN-4S sight";
			day.slot.DefaultFov = DayFov;
			day.slot.VisionType = NightVisionType.None;
			day.slot.LinkedNightSight = night.slot;
			day.slot.NightSightAtNightOnly = false;
			night.name = "TKN-4S night channel";
			var nightSlot = night.slot;
			nightSlot.DefaultFov = NightFov;
			nightSlot.VisionType = NightVisionType.Intensifier;
			nightSlot.LinkedNightSight = null;
			if (nightLook != null)
			{
				var lookSlot = nightLook.slot;
				nightSlot.BaseBlur = lookSlot.BaseBlur;
				nightSlot.VibrationPreBlur = lookSlot.VibrationPreBlur;
				nightSlot.SpriteType = lookSlot.SpriteType;
				if (night.post != null && nightLook.post != null)
				{
					night.post.sharedProfile = nightLook.post.sharedProfile;
					night.post.priority = nightLook.post.priority;
					night.post.weight = nightLook.post.weight;
				}
				if (night.reticleMesh != null && nightLook.reticleMesh != null)
					night.reticleMesh.nightBrightness = nightLook.reticleMesh.nightBrightness;
			}
			foreach (var sightSlot in new[] { day.slot, nightSlot }) SightMotion.Stabilised(sightSlot);
		}

		[DefaultExecutionOrder(-50)]
		internal partial class Avtomat : MonoBehaviour
		{
			internal bool On { get; private set; }

			FireControlSystem _fcs;
			AimablePlatform _cupola, _elev;
			Transform _turret;
			global::AmmoType _b32;
			Vector2 _manual, _traverse;
			float _lastHeading;
			bool _protect;

			internal void Init(
				FireControlSystem fcs,
				AimablePlatform cupola,
				AimablePlatform elev,
				Transform turret,
				UsableOptic day,
				UsableOptic night,
				CameraSlot pzu7,
				global::AmmoType b32,
				Vector2 traverse)
			{
				_fcs = fcs;
				_cupola = cupola;
				_elev = elev;
				_turret = turret;
				_b32 = b32;
				_traverse = traverse;
				_manual = elev.LocalEulerLimits;
				_protect = elev.ProtectFromBadProjection;
				day.slot.ActiveChanged += a => { if (a) Set(true); };
				night.slot.ActiveChanged += a => { if (a) Set(true); };
				pzu7.ActiveChanged += a => { if (a) Set(false); };
			}

			internal void Set(bool on)
			{
				if (on == On || _fcs == null) return;
				On = on;
				_elev.LocalEulerLimits = on ? AutoElevation : _manual;
				// ProtectFromBadProjection drops a goal past the drive's travel. A quick aim from +20 to -4 froze the Kord at +20.
				_elev.ProtectFromBadProjection = !on && _protect;
				_elev.Stabilized = on;
				_cupola.Stabilized = on;
				_fcs.SuperelevateWeapon = on;
				if (on)
				{
					_fcs.CurrentStabMode = StabilizationMode.Vector;
					_fcs.SetStabsActive(true);
					_fcs.SetAimVector(Clamp(_fcs.LinkedWeaponSystems[0].MuzzleIdentity.forward), true);
				}
				else
				{
					_fcs.SetStabsActive(false);
					_fcs.CurrentStabMode = StabilizationMode.None;
				}
				_lastHeading = Heading();
			}

			float Heading()
				=> _turret == null
					? 0f
					: Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(_turret.forward, Vector3.up), Vector3.up);

			// Stabiliser would hold the cupola's heading in the world. The real one's dependent in traverse.
			void Update()
			{
				float heading = Heading();
				float headingChange = Mathf.DeltaAngle(_lastHeading, heading);
				_lastHeading = heading;
				if (!On || !_fcs.StabsActive || Mathf.Abs(headingChange) < 1e-4f) return;
				_fcs.SetAimVector(Quaternion.AngleAxis(headingChange, Vector3.up) * _fcs.AimWorldVector, true);
			}

			void LateUpdate()
			{
				if (!On || !_fcs.StabsActive) return;
				var aim = _fcs.AimWorldVector;
				var clamped = Clamp(aim);
				if (Vector3.Angle(aim, clamped) > 0.01f)
				{
					_fcs.SetAimVector(clamped, true);
					DevClamped();
				}
			}

			Vector3 Clamp(Vector3 world)
			{
				var local = _turret.InverseTransformDirection(world);
				float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
				float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
				float superelevation = _fcs.CurrentRange > 0f && _b32 != null
					? BallisticComputerRepository.Instance.GetSuperelevation(_b32, _fcs.CurrentRange)
					: 0f;
				yaw = Mathf.Clamp(yaw, _traverse.x, _traverse.y);
				pitch = Mathf.Clamp(pitch, AutoElevation.x, AutoElevation.y - superelevation);
				return _turret.TransformDirection(Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);
			}

			// Dev hook: optional method the author's own test harness fills in. It compiles to nothing in a normal build.
			partial void DevClamped();
		}
	}

	// Ammo digit and ready lamp on the TKN-4S strip. The real strip is fed for Дубль, which isn't in the game, so it
	// runs off the gunner's 2A46. Screen overlay, not reticle strokes: a stroke with no alignment bone takes the whole
	// reticle down.
	internal partial class Tkn4sReadout : MonoBehaviour
	{
		WeaponSystem _gun;
		FireControlSystem _gunFcs;
		TextMeshProUGUI _digit;
		GameObject _lit;
		float _offFor;

		internal static void Build(UsableOptic day, WeaponSystem gun, FireControlSystem gunFcs)
		{
			if (day == null || gun == null) return;
			var spec = Reticles.Spec("tkn4s_day.jsonc");
			var problems = new List<string>();
			var overlay = ReticleBuild.BuildOverlay(spec, day.transform, (float)spec.Fov, problems);
			foreach (var problem in problems) MelonLogger.Warning($"T-90A TKN-4S indicator strip: {problem}");
			var readout = overlay.Root.AddComponent<Tkn4sReadout>();
			readout._gun = gun;
			readout._gunFcs = gunFcs;
			readout._digit = overlay.Readouts.TryGetValue("ammo", out var digit) ? digit : null;
			readout._lit = overlay.Lamps.TryGetValue("ready indicator", out var lamp) ? lamp : null;
			if (readout._digit == null || readout._lit == null)
				MelonLogger.Warning("T-90A TKN-4S indicator strip: no 'ammo' readout or 'ready indicator' lamp in its description.");
		}

		void Update()
		{
			if (_gun == null || _digit == null || _lit == null) return;
			var ammo = _gunFcs != null ? _gunFcs.CurrentAmmoType : null;
			string glyph = ammo != null ? Sights.Readout.Glyph(ammo) : "";
			if (_digit.text != glyph) _digit.text = glyph;
			bool able = _gun.AbleToFire;
			_offFor = able ? 0f : _offFor + Time.deltaTime;
			bool lit = able || (_lit.activeSelf && _offFor < 0.5f);
			if (_lit.activeSelf != lit) _lit.SetActive(lit);
		}
	}
}
