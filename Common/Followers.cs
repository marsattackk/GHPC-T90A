using System.Collections.Generic;
using System.Linq;
using GHPC.Utility;
using GHPC.Vehicle;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	// Collider groups only re-sync while something moves, so after spawn the turret group can sit 16 cm low for a
	// second. Place off the stored offset, never the current pose.
	internal static class Followers
	{
		internal static readonly AccessTools.FieldRef<LateFollow, Vector3> PosShift =
			Reflect.FieldRefAccess<LateFollow, Vector3>("_localPosShift");
		internal static readonly AccessTools.FieldRef<LateFollow, Quaternion> RotShift =
			Reflect.FieldRefAccess<LateFollow, Quaternion>("_localRotShift");

		internal static bool Ready => PosShift != null && RotShift != null;

		internal static void PlaceOnBone(Transform piece, Transform bone, Transform target, Vector3 offsetInBone = default)
		{
			var follow = Ready
				? target.GetComponentsInParent<LateFollow>(true).FirstOrDefault(f => f.FollowTarget == bone)
				: null;
			if (follow == null)
			{
				piece.SetParent(bone, false);
				piece.localPosition = offsetInBone;
				piece.localRotation = Quaternion.identity;
				piece.localScale = Vector3.one;
				piece.SetParent(target, true);
				return;
			}
			var boneToFollower = Matrix4x4.TRS(PosShift(follow), RotShift(follow), Vector3.one);
			var followerToTarget = follow.transform.worldToLocalMatrix * target.localToWorldMatrix;
			var boneInTarget = (boneToFollower * followerToTarget).inverse;
			piece.SetParent(target, false);
			piece.localPosition = boneInTarget.MultiplyPoint3x4(offsetInBone);
			piece.localRotation = boneInTarget.rotation;
			piece.localScale = Vector3.one;
		}

		// Groups live at the scene root and LateFollowers is the only way back to them.
		internal static HashSet<LateFollow> On(Transform root, Transform bone)
		{
			var set = new HashSet<LateFollow>(root
				.GetComponentsInChildren<LateFollow>(true)
				.Where(f => f.FollowTarget == bone));
			var target = bone.GetComponent<LateFollowTarget>();
			if (target != null) foreach (var follower in target.LateFollowers) if (follower != null) set.Add(follower);
			return set;
		}

		internal static int Refollow(Transform root, Transform bone, (Vector3 pos, Quaternion rot) old)
		{
			if (!Ready) return 0;
			var set = On(root, bone);
			var oldM = Matrix4x4.TRS(old.pos, old.rot, bone.lossyScale);
			foreach (var follower in set)
			{
				var world = oldM.MultiplyPoint3x4(PosShift(follower));
				var shift = RotShift(follower);
				PosShift(follower) = bone.InverseTransformPoint(world);
				RotShift(follower) = Quaternion.Inverse(bone.rotation) * old.rot * shift;
			}
			return set.Count;
		}

		internal static void ShiftBone(Transform bone, Vector3 delta)
		{
			var kids = bone.Cast<Transform>().Select(c => (c, c.position, c.rotation)).ToList();
			bone.localPosition += delta;
			foreach (var (child, position, rotation) in kids) child.SetPositionAndRotation(position, rotation);
		}

		internal static bool Synced(params Transform[] bones)
		{
			if (!Ready) return true;
			var list = bones.Where(b => b != null).ToList();
			foreach (var follower in Object.FindObjectsByType<LateFollow>(FindObjectsSortMode.None))
				if (list.Contains(follower.FollowTarget)
					&& (follower.transform.position - follower.FollowTarget.TransformPoint(PosShift(follower))).sqrMagnitude > 1e-6f)
					return false;
			return true;
		}

		internal static Transform ColliderGroup(Transform root, string name, Transform follow, params string[] children)
		{
			var holder = new GameObject(name) { layer = 8 };
			holder.SetActive(false);
			holder.transform.SetParent(root, false);
			holder.transform.SetPositionAndRotation(follow.position, follow.rotation);
			var lateFollow = holder.AddComponent<LateFollow>();
			lateFollow.FollowTarget = follow;
			lateFollow.ForceToRoot = true;
			foreach (var childName in children)
			{
				var child = new GameObject(childName) { layer = 8 }.transform;
				child.SetParent(holder.transform, false);
			}
			holder.SetActive(true);
			return holder.transform;
		}

		internal static Transform HullBone(Vehicle v) => RigBones.Hull(v.transform);
		internal static Transform TurretBone(Vehicle v) => RigBones.Turret(v.transform);
	}
}
