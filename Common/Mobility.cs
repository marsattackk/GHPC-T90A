using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using UnityEngine;

namespace T90A.Common
{
	// Before Start: NwhChassis caches maxPower there, VehicleController.Initialize doubles the final drive on tracks.
	internal static class Mobility
	{
		internal sealed class Settings
		{
			// hp, as GHPC's tanks have it, not NWH's kW
			public float? MaxPower;
			public float? MaxRpm;
			public float? FinalDrive;
			public float? ShiftUpRpm;
			public float[] ReverseGears;
			public float? MaxForwardKmh;
			public float? ReverseCapKmh;
			public float? CombatMassKg;
			public float? LinearDrag;
		}

		internal static void Apply(Vehicle vehicle, Settings s)
		{
			var vc = vehicle.GetComponent<NWH.VehiclePhysics.VehicleController>();
			if (vc != null)
			{
				if (s.MaxPower.HasValue) vc.engine.maxPower = s.MaxPower.Value;
				if (s.MaxRpm.HasValue) vc.engine.maxRPM = s.MaxRpm.Value;
				if (s.FinalDrive.HasValue)
				{
					bool initialised = vc.IsInitialized;
					float k = vc.tracks.wheelEnlargementCoefficient;
					// NWH scales the brakes with the final drive, keep the base's.
					if (!initialised && k > 1f) vc.brakes.maxTorque *= vc.transmission.gearMultiplier / s.FinalDrive.Value;
					vc.transmission.gearMultiplier = s.FinalDrive.Value;
					// Past Initialize the doubling is mine to do, else it drives at twice the speed.
					if (initialised && k > 1f) vc.transmission.gearMultiplier *= k;
				}
				// Keep it under max rpm, the fuel cut never lets the engine reach it otherwise.
				if (s.ShiftUpRpm.HasValue) vc.transmission.targetShiftUpRPM = s.ShiftUpRpm.Value;
				if (s.ReverseGears != null) vc.transmission.ReverseGears = new List<float>(s.ReverseGears);
			}
			var chassis = vehicle.GetComponent<NwhChassis>();
			if (chassis != null)
			{
				var fwdCap = Reflect.Field(typeof(NwhChassis), "_maxForwardSpeed");
				var revCap = Reflect.Field(typeof(NwhChassis), "_maxReverseSpeed");
				if (s.MaxForwardKmh.HasValue) fwdCap?.SetValue(chassis, s.MaxForwardKmh.Value / 3.6f);
				if (s.ReverseCapKmh.HasValue) revCap?.SetValue(chassis, s.ReverseCapKmh.Value / 3.6f);
			}
			if (s.CombatMassKg.HasValue)
			{
				var rb = vehicle.GetComponent<Rigidbody>();
				if (rb != null) rb.mass = s.CombatMassKg.Value;
			}
			if (s.LinearDrag.HasValue)
			{
				var rb = vehicle.GetComponent<Rigidbody>();
				if (rb != null) rb.drag = s.LinearDrag.Value;
			}
		}
	}
}
