using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using UnityEngine;

namespace T90A.Common
{
	internal static class Mobility
	{
		internal sealed class Settings
		{
			public float? MaxPower;
			public float? MaxRpm;
			public float? FinalDrive;
			public float? ShiftUpRpm;
			public float[] ReverseGears;
			public float? MaxForwardKmh;
			public float? ReverseCapKmh;
			public float? CombatMassKg;
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
					if (!initialised && k > 1f) vc.brakes.maxTorque *= vc.transmission.gearMultiplier / s.FinalDrive.Value;
					vc.transmission.gearMultiplier = s.FinalDrive.Value;
					if (initialised && k > 1f) vc.transmission.gearMultiplier *= k;
				}
				if (s.ShiftUpRpm.HasValue) vc.transmission.targetShiftUpRPM = s.ShiftUpRpm.Value;
				if (s.ReverseGears != null) vc.transmission.ReverseGears = new List<float>(s.ReverseGears);
			}
			var chassis = vehicle.GetComponent<NwhChassis>();
			if (chassis != null)
			{
				if (s.MaxForwardKmh.HasValue) Reflect.Field(typeof(NwhChassis), "_maxForwardSpeed").SetValue(chassis, s.MaxForwardKmh.Value / 3.6f);
				if (s.ReverseCapKmh.HasValue) Reflect.Field(typeof(NwhChassis), "_maxReverseSpeed").SetValue(chassis, s.ReverseCapKmh.Value / 3.6f);
			}
			if (s.CombatMassKg.HasValue)
			{
				var rb = vehicle.GetComponent<Rigidbody>();
				if (rb != null) rb.mass = s.CombatMassKg.Value;
			}
		}
	}
}
