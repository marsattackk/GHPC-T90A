using System.Linq;
using GHPC.Equipment;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class SmokeCaps
	{
		internal static void Apply(Transform unit, float maxOffAxis = 0.04f, float maxBehind = 0.6f)
		{
			var mgr = unit.GetComponentInChildren<VehicleSmokeManager>(true);
			if (mgr == null) return;
			var slots = (VehicleSmokeManager.SmokeSlot[])AccessTools.Field(typeof(VehicleSmokeManager), "_smokeSlots").GetValue(mgr);
			var caps = unit.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("smoke_cap_")).ToList();
			if (slots == null || caps.Count == 0) return;
			var free = caps.ToList();
			float OffAxis(MeshRenderer c, Vector3 p) => Vector3.ProjectOnPlane(p - c.transform.position, c.transform.forward).magnitude;
			float Behind(MeshRenderer c, Vector3 p) => -Vector3.Dot(p - c.transform.position, c.transform.forward);
			foreach (var s in slots.Where(s => s.SpawnLocation != null))
			{
				var p = s.SpawnLocation.position;
				var best = free.Where(c => Behind(c, p) > -0.02f && Behind(c, p) < maxBehind).OrderBy(c => OffAxis(c, p)).FirstOrDefault();
				if (best == null) continue;
				float d = OffAxis(best, p);
				if (d > maxOffAxis) continue;
				free.Remove(best);
				s.DisplayMesh = best;
				s.DisableVisualOnUse = true;
				best.enabled = s.Rounds > 0;
			}
		}
	}
}
