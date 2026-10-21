using System.Linq;
using GHPC;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class PivotMove
	{
		internal sealed class Settings
		{
			public Vector3 BaseTurret, NewTurret, BaseGun, NewGun;
			public Vector3? NewMuzzle;
			public string Tag = "turret axis";
		}

		internal static void Apply(GHPC.Vehicle.Vehicle vehicle, Settings s)
		{
			var root = vehicle.transform;
			var turret = root.Find("---MESH---/HULL/TURRET");
			var gun = turret != null ? turret.Find("GUN") : null;
			if (turret == null || gun == null) { MelonLogger.Warning($"{s.Tag}: TURRET/GUN bones not found; base pivots kept."); return; }

			var plats = root.GetComponentsInChildren<AimablePlatform>(true);
			var orig = AccessTools.Field(typeof(AimablePlatform), "_originalLocalRotation");
			var gunPlat = plats.FirstOrDefault(p => p.Transform == gun);
			var turretPlat = plats.FirstOrDefault(p => p.Transform == turret);
			var gunRest = gunPlat != null ? (Quaternion)orig.GetValue(gunPlat) : gun.localRotation;
			var turretRest = turretPlat != null ? (Quaternion)orig.GetValue(turretPlat) : turret.localRotation;
			turret.localRotation = turretRest;
			gun.localRotation = gunRest;

			var muzzle = vehicle.GetComponent<GHPC.Weapons.WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon?.MuzzleIdentity;

			var turretOld = (turret.position, turret.rotation); var gunOld = (gun.position, gun.rotation);
			Followers.ShiftBone(turret, turret.parent.InverseTransformVector(root.TransformVector(s.NewTurret - s.BaseTurret)));
			Followers.ShiftBone(gun, turret.InverseTransformVector(root.TransformVector(s.NewGun - s.BaseGun)));
			Followers.Refollow(root, turret, turretOld);
			Followers.Refollow(root, gun, gunOld);
			Rebind(root, gun);
			if (s.NewMuzzle.HasValue)
			{
				if (muzzle == null) MelonLogger.Warning($"{s.Tag}: no muzzle point to move.");
				else
				{
					var stay = muzzle.Cast<Transform>().Where(c => c.GetComponentInChildren<GHPC.Camera.CameraSlot>(true) != null || c.GetComponentInChildren<GHPC.Equipment.Optics.UsableOptic>(true) != null)
						.Select(c => (c, c.position, c.rotation)).ToList();
					muzzle.position = root.TransformPoint(s.NewMuzzle.Value);
					foreach (var (c, p, r) in stay) c.SetPositionAndRotation(p, r);
				}
			}
		}

		static void Rebind(Transform root, Transform gun)
		{
			foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (s.bones == null || !s.bones.Contains(gun) || s.sharedMesh == null) continue;
				var mesh = s.sharedMesh;
				mesh.bindposes = s.bones.Select(b => b.worldToLocalMatrix * s.transform.localToWorldMatrix).ToArray();
				s.sharedMesh = mesh;
			}
		}
	}
}
