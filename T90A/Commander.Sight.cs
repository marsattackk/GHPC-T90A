using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using Reticle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	using DC = GHPC.Equipment.DestructibleComponent;

	// The TKN-4S: two optics as its day and night channels, tied to the Kord. Avtomat keeps the Kord on target through traverse.
	internal static partial class CommanderSight
	{
		// Eye 1.5 cm proud of the head's front plate. Put it any deeper and the view's a black wall.
		internal static readonly Vector3 Head = new Vector3(0.600f, 2.240f, -0.665f);
		// Measured off the photo with the reticle's rangefinder as the scale. Suvorov's table says 7.
		internal const float DayFov = 7.8f;
		// Inferred, day field x 7.5 / 5.1. Nothing gives the night field.
		internal const float NightFov = 11.5f;
		// Avtomat holds the Kord between -4 and +20 deg (Suvorov, «Т-90. Первый серийный российский танк»).
		internal static readonly Vector2 AutoElevation = new Vector2(-4f, 20f);
		// The range keys stop at 1600 m, where the З scale ends (figure 4.65 of «Прицельно-наблюдательный комплекс
		// ПНК-4С», odetievbrony.ru). That З is the Kord's scale is my reading.
		internal static readonly Vector2 RangeLimits = new Vector2(0f, 1600f);
		// The sight's hit box: War Thunder's damage part for it, its back trimmed 4 cm to clear the commander's face.
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
				optic.OverrideRangeStep = 100;   // 100 m a press, as the gunner's range keys
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
	}
}
