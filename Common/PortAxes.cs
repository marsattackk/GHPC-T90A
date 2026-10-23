using GHPC.Vehicle;
using UnityEngine;

namespace T90A.Common
{
	internal static class PortAxes
	{
		internal sealed class Settings
		{
			public Vector3 BaseTurret, NewTurret, BaseGun, NewGun;
			public Vector3 Muzzle;
			public float PitchDeg;
			public bool TiltRing = true;
			public string Tag = "port";
			public Vector3 LevelledMuzzle => NewGun + Quaternion.AngleAxis(-PitchDeg, Vector3.right) * (Muzzle - NewGun);
		}

		internal static void Apply(Vehicle vehicle, Settings s)
		{
			PivotMove.Apply(vehicle, new PivotMove.Settings
			{
				BaseTurret = s.BaseTurret, NewTurret = s.NewTurret, BaseGun = s.BaseGun, NewGun = s.NewGun, Tag = s.Tag + " turret axis", NewMuzzle = s.LevelledMuzzle,
			});
			if (s.TiltRing && Mathf.Abs(s.PitchDeg) > 1e-4f)
				TiltedRing.Apply(vehicle, Quaternion.AngleAxis(s.PitchDeg, Vector3.right), s.Tag + " tilted ring");
		}
	}
}
