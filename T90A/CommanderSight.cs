using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using HarmonyLib;
using Reticle;
using UnityEngine;

namespace T90A
{
	using DC = GHPC.Equipment.DestructibleComponent;

	internal static class CommanderSight
	{
		internal static readonly Vector3 Head = new Vector3(0.600f, 2.240f, -0.665f);
		internal const float DayFov = 7.8f;
		internal const float NightFov = 11.5f;
		internal static readonly Vector2 AutoElevation = new Vector2(-4f, 20f);
		internal static readonly Vector2 RangeLimits = new Vector2(0f, 1600f);
		internal static readonly Vector3 ZoneCentre = new Vector3(0.535f, 2.0205f, -0.9225f), ZoneSize = new Vector3(0.416f, 0.501f, 0.275f);

		static readonly FieldInfo SlotReparent = AccessTools.Field(typeof(CameraSlot), "_reparentTarget");
		static readonly FieldInfo ReticleSmr = AccessTools.Field(typeof(ReticleMesh), "SMR");

		internal static void Configure(UsableOptic day, UsableOptic night, UsableOptic nightLook, Transform head, FireControlSystem fcs, WeaponSystem kord,
			GHPC.Weaponry.AmmoCodexScriptable b32, Transform level, CameraSlot pzu7, DC hitZone, ReticleSO reticle, ReticleSO nightReticle)
		{
			foreach (var o in new[] { day, night })
			{
				o.FCS = fcs;
				o.RegisterAtStart = false;
				o.Alignment = OpticAlignment.BoresightStabilized;
				o.RotateElevation = true;
				o.RotateAzimuth = false;
				o.Mounts = new AimablePlatform[0];
				o.LocalElevationLimits = AutoElevation;
				o.LocalAzimuthLimits = Vector2.zero;
				o.LevelTransform = level;
				o.RegisteredAmmoTypes = new[] { b32 };
				o.OverrideRangeLimits = RangeLimits;
				o.OverrideRangeStep = 100;
				var s = o.slot;
				SlotReparent.SetValue(s, head);
				s.AllowFreeZoom = false;
				s.OtherFovs = new float[0];
				s.ExclusiveWeapons = new[] { kord };
				s.HitZone = hitZone;
				s.SlotToUseOnDestroy = pzu7;
				s.IsExterior = false;
				var so = o == night ? nightReticle : reticle;
				if (o.reticleMesh != null && so != null) { o.reticleMesh.reticleSO = so; ReticleSmr.SetValue(o.reticleMesh, null); }
				o.gameObject.AddComponent<RangeLine>().Init(o, fcs, b32.AmmoType);
			}
			day.name = "TKN-4S sight";
			day.slot.DefaultFov = DayFov;
			day.slot.VisionType = NightVisionType.None;
			day.slot.LinkedNightSight = night.slot;
			day.slot.NightSightAtNightOnly = false;
			night.name = "TKN-4S night channel";
			var ns = night.slot;
			ns.DefaultFov = NightFov;
			ns.VisionType = NightVisionType.Intensifier;
			ns.LinkedNightSight = null;
			if (nightLook != null)
			{
				var ls = nightLook.slot;
				ns.BaseBlur = ls.BaseBlur; ns.VibrationBlurScale = ls.VibrationBlurScale; ns.VibrationPreBlur = ls.VibrationPreBlur;
				ns.VibrationShakeMultiplier = ls.VibrationShakeMultiplier; ns.SpriteType = ls.SpriteType;
				if (night.post != null && nightLook.post != null) { night.post.sharedProfile = nightLook.post.sharedProfile; night.post.priority = nightLook.post.priority; night.post.weight = nightLook.post.weight; }
				if (night.reticleMesh != null && nightLook.reticleMesh != null) night.reticleMesh.nightBrightness = nightLook.reticleMesh.nightBrightness;
			}
		}

		[DefaultExecutionOrder(-50)]
		internal class Avtomat : MonoBehaviour
		{
			internal bool On { get; private set; }

			FireControlSystem _fcs;
			AimablePlatform _cupola, _elev;
			Transform _turret;
			global::AmmoType _b32;
			Vector2 _manual, _traverse;
			float _lastHeading;
			bool _protect;

			internal void Init(FireControlSystem fcs, AimablePlatform cupola, AimablePlatform elev, Transform turret, UsableOptic day, UsableOptic night, CameraSlot pzu7, global::AmmoType b32, Vector2 traverse)
			{
				_fcs = fcs; _cupola = cupola; _elev = elev; _turret = turret; _b32 = b32; _traverse = traverse;
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

			float Heading() => _turret == null ? 0f : Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(_turret.forward, Vector3.up), Vector3.up);

			void Update()
			{
				float h = Heading();
				float dh = Mathf.DeltaAngle(_lastHeading, h);
				_lastHeading = h;
				if (!On || !_fcs.StabsActive || Mathf.Abs(dh) < 1e-4f) return;
				_fcs.SetAimVector(Quaternion.AngleAxis(dh, Vector3.up) * _fcs.AimWorldVector, true);
			}

			void LateUpdate()
			{
				if (!On || !_fcs.StabsActive) return;
				var aim = _fcs.AimWorldVector;
				var clamped = Clamp(aim);
				if (Vector3.Angle(aim, clamped) > 0.01f) _fcs.SetAimVector(clamped, true);
			}

			Vector3 Clamp(Vector3 world)
			{
				var d = _turret.InverseTransformDirection(world);
				float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
				float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
				float se = _fcs.CurrentRange > 0f && _b32 != null ? BallisticComputerRepository.Instance.GetSuperelevation(_b32, _fcs.CurrentRange) : 0f;
				yaw = Mathf.Clamp(yaw, _traverse.x, _traverse.y);
				pitch = Mathf.Clamp(pitch, AutoElevation.x, AutoElevation.y - se);
				return _turret.TransformDirection(Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);
			}
		}

		[DefaultExecutionOrder(20)]
		internal class RangeLine : MonoBehaviour
		{
			UsableOptic _optic;
			FireControlSystem _fcs;
			global::AmmoType _ammo;

			internal void Init(UsableOptic optic, FireControlSystem fcs, global::AmmoType ammo) { _optic = optic; _fcs = fcs; _ammo = ammo; }

			void LateUpdate()
			{
				if (_optic?.reticleMesh == null || _fcs == null || _ammo == null) return;
				float se = _fcs.CurrentRange > 0f ? BallisticComputerRepository.Instance.GetSuperelevation(_ammo, _fcs.CurrentRange) : 0f;
				_optic.reticleMesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact, new AngularVector2(0f, se, AngularLength.AngularUnit.DEG));
			}
		}
	}
}
