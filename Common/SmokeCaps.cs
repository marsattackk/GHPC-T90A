using System.Linq;
using GHPC.Equipment;
using UnityEngine;

namespace T90A.Common
{
	internal static class SmokeCaps
	{
		// Needs the launch points already moved onto the tubes (SmokeLaunchers.Seat).
		internal static string Apply(Transform unit, float maxOffAxis = 0.04f, float maxBehind = 0.6f)
		{
			var mgr = unit.GetComponentInChildren<VehicleSmokeManager>(true);
			if (mgr == null) return "no smoke manager";
			var slots = Reflect.Field(typeof(VehicleSmokeManager), "_smokeSlots")?.GetValue(mgr) as VehicleSmokeManager.SmokeSlot[];
			var caps = unit.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("smoke_cap_")).ToList();
			if (slots == null || caps.Count == 0) return $"{caps.Count} caps, {slots?.Length ?? 0} slots: nothing wired";
			var free = caps.ToList();
			int wired = 0;
			float worst = 0f;
			float OffAxis(MeshRenderer cap, Vector3 point)
				=> Vector3.ProjectOnPlane(point - cap.transform.position, cap.transform.forward).magnitude;
			float Behind(MeshRenderer cap, Vector3 point) => -Vector3.Dot(point - cap.transform.position, cap.transform.forward);
			foreach (var slot in slots.Where(c => c.SpawnLocation != null))
			{
				var spawnPoint = slot.SpawnLocation.position;
				var best = free
					.Where(c => Behind(c, spawnPoint) > -0.02f && Behind(c, spawnPoint) < maxBehind)
					.OrderBy(c => OffAxis(c, spawnPoint))
					.FirstOrDefault();
				if (best == null) continue;
				float offset = OffAxis(best, spawnPoint);
				if (offset > maxOffAxis) continue;
				free.Remove(best);
				slot.DisplayMesh = best;
				slot.DisableVisualOnUse = true;
				best.enabled = slot.Rounds > 0;
				wired++;
				worst = Mathf.Max(worst, offset);
			}
			return $"{wired} of {slots.Length} slots given a cap (of {caps.Count}; launch points at most {worst * 100f:0.0} cm off their cap's axis)";
		}
	}
}
