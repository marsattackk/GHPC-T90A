using System;
using System.Collections;
using GHPC.State;
using GHPC.Vehicle;
using UnityEngine;

namespace T90A.Common
{
	internal static class ConvertSweep
	{
		// One marker per conversion: the spawn patch and the PlayerReady sweep may both see the same unit.
		internal sealed class Converted : MonoBehaviour { }

		internal static bool Mark(Vehicle vehicle)
		{
			if (vehicle == null || vehicle.GetComponent<Converted>() != null) return false;
			vehicle.gameObject.AddComponent<Converted>();
			return true;
		}

		internal static void AtPlayerReady(string unitKey, Action<Vehicle> convert, Action<Vehicle> other = null) =>
			StateController.RunOrDefer(GameState.PlayerReady, _ => Sweep(unitKey, convert, other), GameStatePriority.Low);

		static IEnumerator Sweep(string unitKey, Action<Vehicle> convert, Action<Vehicle> other)
		{
			foreach (var v in UnityEngine.Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
			{
				if (v.UniqueName == unitKey) convert(v);
				else other?.Invoke(v);
			}
			yield break;
		}
	}
}
