using UnityEngine;

namespace T90A.Common
{
	internal static class RigBones
	{
		static readonly (string hull, string turret, string gun)[] Layouts =
		{
			(BaseRigT72M1.HullPath, BaseRigT72M1.TurretPath, BaseRigT72M1.GunPath),
			(BaseRigT80B.HullPath, BaseRigT80B.TurretPath, BaseRigT80B.GunPath),
			(BaseRigM60A1.HullPath, BaseRigM60A1.TurretPath, BaseRigM60A1.GunPath),
		};

		internal static bool IsColliderGroup(string name) => name.Contains("COLLIDERS") || BaseRigM60A1.IsColliderGroup(name);
		internal static bool IsColliderGroup(string name, string group) =>
			name.IndexOf(group + " COLLIDERS", System.StringComparison.OrdinalIgnoreCase) >= 0 || BaseRigM60A1.IsColliderGroup(name, group);

		internal static Transform Hull(Transform root) { foreach (var l in Layouts) { var t = root.Find(l.hull); if (t != null) return t; } return null; }

		internal static Transform Turret(Transform root) { foreach (var l in Layouts) { var t = root.Find(l.turret); if (t != null) return t; } return null; }

		internal static Transform Gun(Transform root) { foreach (var l in Layouts) { var t = root.Find(l.gun); if (t != null) return t; } return null; }

		internal static string TurretName(Transform root)
		{
			foreach (var l in Layouts) if (root.Find(l.turret) != null) return l.turret;
			return "no TURRET bone";
		}
	}
}
