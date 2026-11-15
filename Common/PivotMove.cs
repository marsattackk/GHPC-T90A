using System.Linq;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Moves the turret and gun pivots and keeps the platforms' rest rotation in step.
	internal static class PivotMove
	{
		internal sealed class Settings
		{
			public Vector3 BaseTurret, NewTurret, BaseGun, NewGun;
			public float[] Elevations = { -7f, 0f, 10f, 20f };
			public Transform Sight;
			public Vector3? NewMuzzle;
			public string Tag = "turret axis";
		}

		internal static bool Apply(GHPC.Vehicle.Vehicle vehicle, Settings s)
		{
			var root = vehicle.transform;
			var turret = RigBones.Turret(root);
			var gun = turret != null ? RigBones.Gun(root) : null;
			if (turret == null || gun == null)
			{
				MelonLogger.Warning($"{s.Tag}: TURRET/GUN bones not found; base pivots kept.");
				return false;
			}

			var plats = root.GetComponentsInChildren<AimablePlatform>(true);
			var orig = Reflect.Field(typeof(AimablePlatform), "_originalLocalRotation");
			var gunPlat = plats.FirstOrDefault(p => p.Transform == gun);
			var turretPlat = plats.FirstOrDefault(p => p.Transform == turret);
			var gunRest = gunPlat != null ? (Quaternion)orig.GetValue(gunPlat) : gun.localRotation;
			var turretRest = turretPlat != null ? (Quaternion)orig.GetValue(turretPlat) : turret.localRotation;
			// Rest pose first. The AI may already have turned the turret.
			turret.localRotation = turretRest;
			gun.localRotation = gunRest;

			var muzzle = vehicle.GetComponent<GHPC.Weapons.WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon?.MuzzleIdentity;

			// Followers and skins are redone from the old bone poses. The collider groups may still be lagging.
			var turretOld = (turret.position, turret.rotation);
			var gunOld = (gun.position, gun.rotation);
			Followers.ShiftBone(turret, turret.parent.InverseTransformVector(root.TransformVector(s.NewTurret - s.BaseTurret)));
			Followers.ShiftBone(gun, turret.InverseTransformVector(root.TransformVector(s.NewGun - s.BaseGun)));
			int followers = Followers.Refollow(root, turret, turretOld) + Followers.Refollow(root, gun, gunOld);
			int skins = Rebind(root, (turret, turretOld), (gun, gunOld));

			var turretLocal = root.InverseTransformPoint(turret.position);
			var gunLocal = root.InverseTransformPoint(gun.position);
			if (s.NewMuzzle.HasValue)
			{
				if (muzzle == null) MelonLogger.Warning($"{s.Tag}: no muzzle point to move.");
				else
				{
					var was = root.InverseTransformPoint(muzzle.position);
					var stay = muzzle.Cast<Transform>().Where(c => c.GetComponentInChildren<GHPC.Camera.CameraSlot>(true) != null
						|| c.GetComponentInChildren<GHPC.Equipment.Optics.UsableOptic>(true) != null)
						.Select(kept => (kept, kept.position, kept.rotation)).ToList();
					string Kid(Transform child)
						=> $"{child.name} [{string.Join(", ", child.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(x => x.GetType().Name))}]";
					var moved = muzzle.Cast<Transform>().Where(c => stay.All(saved => saved.kept != c)).Select(Kid).ToList();
					muzzle.position = root.TransformPoint(s.NewMuzzle.Value);
					foreach (var (child, position, rotation) in stay) child.SetPositionAndRotation(position, rotation);
					var now = root.InverseTransformPoint(muzzle.position);
				}
			}
			return true;
		}

		// Never write the mesh, every T-72M1 shares it (#4). A child at the old pose stands in for the moved bone.
		static int Rebind(Transform root, params (Transform bone, (Vector3 pos, Quaternion rot) old)[] moved)
		{
			int count = 0;
			var gun = moved[moved.Length - 1].bone;
			foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (skin.bones == null || !skin.bones.Contains(gun) || skin.sharedMesh == null) continue;
				var bones = skin.bones;
				foreach (var (bone, old) in moved)
				{
					Transform bind = null;
					for (int i = 0; i < bones.Length; i++)
					{
						if (bones[i] != bone) continue;
						if (bind == null)
						{
							bind = new GameObject(bone.name + " (bind)").transform;
							bind.SetParent(bone, false);
							bind.SetPositionAndRotation(old.pos, old.rot);
						}
						bones[i] = bind;
					}
				}
				skin.bones = bones;
				count++;
			}
			return count;
		}
	}
}
