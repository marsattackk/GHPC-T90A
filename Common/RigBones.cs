using UnityEngine;

namespace T90A.Common
{
	// Hull, turret and gun bones on whichever base rig it is (T-72M1, T-80B or M60A1), so nothing else hard-codes
	// paths.
	internal static class RigBones
	{
		static readonly (string hull, string turret, string gun)[] Layouts =
		{
			(BaseRigT72M1.HullPath, BaseRigT72M1.TurretPath, BaseRigT72M1.GunPath),
		};

		internal static bool IsColliderGroup(string name) => name.Contains("COLLIDERS");
		internal static bool IsColliderGroup(string name, string group) =>
			name.IndexOf(group + " COLLIDERS", System.StringComparison.OrdinalIgnoreCase) >= 0;

		internal static Transform Hull(Transform root)
		{
			foreach (var layout in Layouts)
			{
				var bone = root.Find(layout.hull);
				if (bone != null) return bone;
			}
			return null;
		}

		internal static Transform Turret(Transform root)
		{
			foreach (var layout in Layouts)
			{
				var bone = root.Find(layout.turret);
				if (bone != null) return bone;
			}
			return null;
		}

		internal static Transform Gun(Transform root)
		{
			foreach (var layout in Layouts)
			{
				var bone = root.Find(layout.gun);
				if (bone != null) return bone;
			}
			return null;
		}

		internal static string TurretName(Transform root)
		{
			foreach (var layout in Layouts) if (root.Find(layout.turret) != null) return layout.turret;
			return "no TURRET bone";
		}
	}
}
