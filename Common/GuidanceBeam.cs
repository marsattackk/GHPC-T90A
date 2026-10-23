using GHPC.Equipment.Optics;
using GHPC.Weapons;
using UnityEngine;

namespace T90A.Common
{
	[DefaultExecutionOrder(-50)]
	internal sealed class GuidanceBeam : MonoBehaviour
	{
		internal FireControlSystem Fcs;
		internal Transform Gun;
		Transform _sight;

		void Update() => Sync();

		internal void Sync()
		{
			if (Fcs == null || Gun == null) return;
			if (_sight == null) _sight = DayOptic(Fcs)?.slot?.transform;
			var dir = Fcs.AimWorldVector;
			if (dir.sqrMagnitude < 1e-6f) dir = Gun.forward;
			var at = _sight != null ? _sight.position : Gun.position;
			transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir.normalized, Gun.up));
		}

		internal static UsableOptic DayOptic(FireControlSystem fcs)
		{
			if (fcs?.MainOptic == null) return null;
			return fcs.MainOptic.slot.IsLinkedNightSight ? fcs.MainOptic.slot.LinkedDaySight.PairedOptic : fcs.MainOptic;
		}
	}
}
