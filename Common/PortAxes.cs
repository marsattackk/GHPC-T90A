using System.Linq;
using GHPC.Vehicle;
using UnityEngine;

namespace T90A.Common
{
	// Pivots through PivotMove, a pitched ring through TiltedRing.
	internal static class PortAxes
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public Vector3 BaseTurret, NewTurret, BaseGun, NewGun;
			public Vector3 Muzzle;
			public float PitchDeg;
			public bool TiltRing = true;
			public Transform Sight;
			public string Tag = "port";
			public Vector3 LevelledMuzzle => NewGun + Quaternion.AngleAxis(-PitchDeg, Vector3.right) * (Muzzle - NewGun);
		}
#pragma warning restore CS0649

		internal static Settings FromRig(RigMap rig, Vector3 baseTurret, Vector3 baseGun, string tag) =>
			rig == null || !rig.HasAxes ? null : new Settings
			{
				BaseTurret = baseTurret,
				NewTurret = rig.Turret,
				BaseGun = baseGun,
				NewGun = rig.Gun,
				Muzzle = rig.Muzzle,
				PitchDeg = rig.PitchDeg,
				Tag = tag,
			};

		// At spawn the FCS hasn't picked its main optic yet.
		internal static Transform GunnerDaySight(Vehicle vehicle)
		{
			var gun = RigBones.Gun(vehicle.transform);
			return gun == null ? null : vehicle.transform.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true)
				.FirstOrDefault(o => o.slot != null
					&& !o.slot.IsLinkedNightSight
					&& o.transform.IsChildOf(gun))?.slot.transform;
		}

		internal static void Apply(Vehicle vehicle, Settings s)
		{
			PivotMove.Apply(vehicle, new PivotMove.Settings
			{
				BaseTurret = s.BaseTurret,
				NewTurret = s.NewTurret,
				BaseGun = s.BaseGun,
				NewGun = s.NewGun,
				Sight = s.Sight ?? GunnerDaySight(vehicle),
				Tag = s.Tag + " turret axis",
				NewMuzzle = s.LevelledMuzzle,
			});
			// After the pivots, the seat goes on the moved one.
			if (s.TiltRing && Mathf.Abs(s.PitchDeg) > 1e-4f)
				TiltedRing.Apply(vehicle, Quaternion.AngleAxis(s.PitchDeg, Vector3.right), s.Tag + " tilted ring");
		}
	}
}
