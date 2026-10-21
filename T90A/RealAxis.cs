using GHPC.Vehicle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class RealAxis
	{
		internal static readonly Vector3 T72Turret = new Vector3(0f, 1.369f, -0.527f), T90Turret = new Vector3(0f, 1.369f, -0.748f);
		internal static readonly Vector3 T72Gun = new Vector3(0f, 1.651f, 0.372f), T90Gun = new Vector3(0f, 1.576f, 0.292f);
		internal const float GunLevelDeg = 1.2f;
		internal static Vector3 T90Muzzle => T90Gun + Quaternion.AngleAxis(-GunLevelDeg, Vector3.right) * (new Vector3(0f, 1.524f, 5.422f) - T90Gun);
		internal const float RingTiltDeg = 1.2f;

		internal static void Apply(Vehicle vehicle)
		{
			PivotMove.Apply(vehicle, new PivotMove.Settings
			{
				BaseTurret = T72Turret, NewTurret = T90Turret, BaseGun = T72Gun, NewGun = T90Gun, Tag = "T-90A turret axis", NewMuzzle = T90Muzzle,
			});
			TiltedRing.Apply(vehicle, Quaternion.AngleAxis(RingTiltDeg, Vector3.right), "T-90A tilted ring");
		}
	}
}
