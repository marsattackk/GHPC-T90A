using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using UnityEngine;

namespace T90A.Common
{
	// Engine, gearbox and speeds have to go in before the controller starts: NwhChassis caches maxPower in Start, and
	// VehicleController.Initialize doubles the final drive on tracks.
	internal static class Mobility
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Tag = "Port";
			// hp, as GHPC's tanks have it, not NWH's kW
			public float? MaxPower;
			public float? MaxRpm;
			public float? MinRpm;
			public float? MaxRpmChange;
			public float? FinalDrive;
			public float? ShiftUpRpm;
			public float[] ForwardGears;
			public float[] ReverseGears;
			public float? MaxForwardKmh;
			public float? ReverseCapKmh;
			public float? CombatMassKg;
			public float? LinearDrag;
		}
#pragma warning restore CS0649

		internal static string Apply(Vehicle vehicle, Settings s)
		{
			var log = new List<string>();
			var controller = vehicle.GetComponent<NWH.VehiclePhysics.VehicleController>();
			if (controller != null)
			{
				if (s.MaxPower.HasValue)
				{
					log.Add($"power {controller.engine.maxPower:0} -> {s.MaxPower.Value:0} hp");
					controller.engine.maxPower = s.MaxPower.Value;
				}
				if (s.MaxRpm.HasValue)
				{
					log.Add($"max rpm {controller.engine.maxRPM:0} -> {s.MaxRpm.Value:0}");
					controller.engine.maxRPM = s.MaxRpm.Value;
				}
				if (s.MinRpm.HasValue)
				{
					log.Add($"idle {controller.engine.minRPM:0} -> {s.MinRpm.Value:0} rpm");
					controller.engine.minRPM = s.MinRpm.Value;
				}
				if (s.MaxRpmChange.HasValue)
				{
					log.Add($"rpm climb {controller.engine.maxRpmChange:0} -> {s.MaxRpmChange.Value:0} rpm/s");
					controller.engine.maxRpmChange = s.MaxRpmChange.Value;
				}
				if (s.FinalDrive.HasValue)
				{
					bool initialised = controller.IsInitialized;
					float k = controller.tracks.wheelEnlargementCoefficient;
					// Brakes stay the base's: NWH scales them with the final drive.
					if (!initialised && k > 1f) controller.brakes.maxTorque *= controller.transmission.gearMultiplier / s.FinalDrive.Value;
					log.Add($"final drive {(initialised && k > 1f ? controller.transmission.gearMultiplier / k : controller.transmission.gearMultiplier):0.000} -> {s.FinalDrive.Value:0.000}");
					controller.transmission.gearMultiplier = s.FinalDrive.Value;
					// Past Initialize I have to do the doubling myself, or it drives at twice the speed.
					if (initialised && k > 1f) controller.transmission.gearMultiplier *= k;
				}
				// Keep it under max rpm: the fuel cut never lets the engine reach it otherwise.
				if (s.ShiftUpRpm.HasValue)
				{
					log.Add($"shift up {controller.transmission.targetShiftUpRPM:0} -> {s.ShiftUpRpm.Value:0} rpm");
					controller.transmission.targetShiftUpRPM = s.ShiftUpRpm.Value;
				}
				if (s.ForwardGears != null)
				{
					log.Add($"forward {string.Join(" / ", controller.transmission.ForwardGears)} -> {string.Join(" / ", s.ForwardGears)}");
					controller.transmission.ForwardGears = new List<float>(s.ForwardGears);
				}
				if (s.ReverseGears != null)
				{
					log.Add($"reverse{string.Join(" / ", controller.transmission.ReverseGears)} -> {string.Join(" / ", s.ReverseGears)}");
					controller.transmission.ReverseGears = new List<float>(s.ReverseGears);
				}
			}
			else if (s.MaxPower.HasValue
				|| s.MaxRpm.HasValue
				|| s.MinRpm.HasValue
				|| s.MaxRpmChange.HasValue
				|| s.FinalDrive.HasValue
				|| s.ShiftUpRpm.HasValue
				|| s.ForwardGears != null
				|| s.ReverseGears != null)
				log.Add("no VehicleController: engine / gearing left as the base's");
			var chassis = vehicle.GetComponent<NwhChassis>();
			if (chassis != null)
			{
				var fwdCap = Reflect.Field(typeof(NwhChassis), "_maxForwardSpeed");
				var revCap = Reflect.Field(typeof(NwhChassis), "_maxReverseSpeed");
				if (s.MaxForwardKmh.HasValue)
				{
					log.Add(fwdCap != null
						? $"forward cap {s.MaxForwardKmh.Value:0.#} km/h"
						: "forward cap left as the base's (field missing)");
					fwdCap?.SetValue(chassis, s.MaxForwardKmh.Value / 3.6f);
				}
				if (s.ReverseCapKmh.HasValue)
				{
					log.Add(revCap != null
						? $"reverse cap {s.ReverseCapKmh.Value:0.#} km/h"
						: "reverse cap left as the base's (field missing)");
					revCap?.SetValue(chassis, s.ReverseCapKmh.Value / 3.6f);
				}
			}
			if (s.CombatMassKg.HasValue)
			{
				var rigidbody = vehicle.GetComponent<Rigidbody>();
				if (rigidbody != null)
				{
					log.Add($"mass {rigidbody.mass:0} -> {s.CombatMassKg.Value:0} kg");
					rigidbody.mass = s.CombatMassKg.Value;
				}
				else log.Add("no Rigidbody on the unit: mass left as the base's");
			}
			if (s.LinearDrag.HasValue)
			{
				var rigidbody = vehicle.GetComponent<Rigidbody>();
				if (rigidbody != null)
				{
					log.Add($"linear drag {rigidbody.drag:0.###} -> {s.LinearDrag.Value:0.###}");
					rigidbody.drag = s.LinearDrag.Value;
				}
				else log.Add("no Rigidbody on the unit: linear drag left as the base's");
			}
			return string.Join(", ", log);
		}
	}
}
