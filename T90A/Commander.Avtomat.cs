using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using UnityEngine;

namespace T90A
{
	// The TKN-4S's Avtomat mode: the Kord follows the sight, stabilised in elevation, and turns with the turret in traverse.
	internal static partial class CommanderSight
	{
		// Ahead of FireControlSystem and AimablePlatform (both at the default 0), so they take this frame's aim
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
				day.slot.ActiveChanged += active =>
				{
					if (active) Set(true);
				};
				night.slot.ActiveChanged += active =>
				{
					if (active) Set(true);
				};
				pzu7.ActiveChanged += active =>
				{
					if (active) Set(false);
				};
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
				float superelevation = _fcs.CurrentRange > 0f && _b32 != null && BallisticComputerRepository.Instance != null
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
}
