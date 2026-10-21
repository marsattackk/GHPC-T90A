using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;

namespace T90A
{
	internal static class SightSetup
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
			day.slot.fovAspect = true;
			Fcs1A45T.Apply(day);
			NightSight.Apply(vehicle, day);
		}

		internal static UsableOptic DayOptic(FireControlSystem fcs)
		{
			if (fcs?.MainOptic == null) return null;
			return fcs.MainOptic.slot.IsLinkedNightSight ? fcs.MainOptic.slot.LinkedDaySight.PairedOptic : fcs.MainOptic;
		}
	}
}
