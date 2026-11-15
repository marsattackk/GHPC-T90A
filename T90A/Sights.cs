using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// Day sight: the 1G46 reticle, the 1A45T, then the night sight.
	internal static partial class Sights
	{
		internal static void Apply(Vehicle vehicle)
		{
			var fcs = vehicle.GetComponentInChildren<FireControlSystem>();
			var day = DayOptic(fcs);
			if (day == null)
			{
				MelonLogger.Warning("T-90A: no gunner day sight found.");
				return;
			}

			var clips = vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
			GHPC.Weaponry.AmmoCodexScriptable Round(int i) =>
				clips != null && clips.Length > i && clips[i] != null ? clips[i].ClipType.MinimalPattern[0] : null;
			var host = day.reticleMesh.reticleSO;
			Sight1G46.Apply(day,
				Round(0) ?? Sight1G46.FindAmmo(Sight1G46.ApfsdsAmmo, host, "3BM"),
				Round(2) ?? Sight1G46.FindAmmo(Sight1G46.HeAmmo, host, "3OF"),
				Round(1) ?? Sight1G46.FindAmmo(Sight1G46.HeatAmmo, host, "3BK"),
				Sight1G46.FindAmmo(Sight1G46.CoaxAmmo, host, "762"));
			ReticleWidth.Attach(day);
			day.slot.ZoomInAudioEvent = "event:/Effects/Optic/Optic_Zoom_In";
			day.slot.ZoomOutAudioEvent = "event:/Effects/Optic/Optic_Zoom_Out";
			day.slot.fovAspect = true;   // horizontal FOV like the 1G42, otherwise the field's 1.7x too big
			bool skip = false;
			DevSkip("fire control", ref skip);
			if (!skip) ApplyFcs(day);
			skip = false;
			DevSkip("night sight", ref skip);
			if (!skip) NightSight.Apply(vehicle, day);
		}

		// The 1A45T: laser limits and a fire gate that waits for the gun to be laid.
		const float LaserMin = 400f;
		const float LaserMax = 5000f;

		// The sight can go past the gun's stops. The fire gate just stays shut.
		internal static readonly Vector2 LineOfSightLimits = new Vector2(-15f, 20f);

		static readonly GunnerFcs.Settings Settings = new GunnerFcs.Settings
		{
			LaserMin = LaserMin,
			LaserMax = LaserMax,
			LineOfSightLimits = LineOfSightLimits,
			LineOfSightAzimuth = 8f,
			RangeStep = 100,
			DisplayRangeIncrement = 50,
			FireGateAngle = 0.1f,   // 1V528's isn't published. GHPC's 1A33 value
			FireWhenLaid = () => T90AMod.FireWhenLaid == null || T90AMod.FireWhenLaid.Value,
		};

		static void ApplyFcs(UsableOptic dayOptic)
		{
			GunnerFcs.Apply(dayOptic, Settings);
			var fcs = dayOptic.FCS;
			if (fcs != null)
			{
				GunnerFcs.LaserAim.Register(fcs, dayOptic, Settings);
			}
			Readout.Build(dayOptic);
		}

		internal static UsableOptic DayOptic(FireControlSystem fcs) => GuidanceBeam.DayOptic(fcs);

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevSkip(string step, ref bool skip);
	}
}
