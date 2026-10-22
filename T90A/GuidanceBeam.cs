using GHPC.Weapons;
using UnityEngine;

namespace T90A
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
			if (_sight == null) _sight = SightSetup.DayOptic(Fcs)?.slot?.transform;
			var dir = Fcs.AimWorldVector;
			if (dir.sqrMagnitude < 1e-6f) dir = Gun.forward;
			var at = _sight != null ? _sight.position : Gun.position;
			transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir.normalized, Gun.up));
		}
	}
}
