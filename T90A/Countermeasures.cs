using GHPC.Vehicle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// Shtora-1: the jammer and the screens.
	internal static partial class Countermeasures
	{
		// The jammer and the screens each have their own try, so one that throws leaves the other fitted.
		static readonly ConversionStep<Vehicle>[] Steps =
		{
			new ConversionStep<Vehicle> { What = "Shtora-1 jammer", Run = FitJammer },
			new ConversionStep<Vehicle> { What = "Shtora-1 screens", Run = FitScreens },
		};

		internal static void Apply(Vehicle vehicle)
		{
			bool on = T90AMod.ShtoraOn.Value;
			DevPreview(ref on);
			if (!on) return;
			ConversionSteps.Run(T90AMod.FriendlyName, Steps, vehicle, vehicle.name, atSpawn: true);
		}

		static void FitJammer(Vehicle vehicle)
		{
			var jammer = Jammer.Copy();
			jammer.OnAtSpawn = T90AMod.ShtoraJammerAtSpawn.Value;
			DevPreview(jammer);
			Shtora.Apply(vehicle, jammer);
		}

		static void FitScreens(Vehicle vehicle)
		{
			var screens = Screens.Copy();
			screens.Automatic = T90AMod.ShtoraAutomatic.Value;
			screens.AlarmTone = T90AMod.ShtoraTone.Value;
			screens.PanelShown = T90AMod.ShtoraPanel.Value;
			screens.PanelAlways = T90AMod.ShtoraPanelAlways.Value;
			DevPreview(screens);
			ShtoraScreen.Apply(vehicle, screens);
		}

		static readonly Shtora.Settings Jammer = new Shtora.Settings
		{
			Tag = T90AMod.FriendlyName,
			EmitterLeft = "shtora_l",
			EmitterRight = "shtora_r",
			SectorAz = 20f,
			SectorEl = 2.25f,
			BreakChance = 0.6f,
			Relock = new Vector2(0.3f, 0.7f),   // no source says where the lock comes back. This and the jitter are mine
			JitterDeg = 30f,
			Missiles = new[] { "BGM-71", "HOT", "MILAN", "9M111", "9M113", "9M112", "9M17", "9M114" },
			OnAtSpawn = true,
		};

		// Everything else is ShtoraScreen's defaults.
		internal static readonly ShtoraScreen.Settings Screens = new ShtoraScreen.Settings { Tag = T90AMod.FriendlyName };

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevPreview(ref bool on);
		static partial void DevPreview(Shtora.Settings jammer);
		static partial void DevPreview(ShtoraScreen.Settings screens);
	}
}
