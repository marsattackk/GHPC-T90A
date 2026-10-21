using System.Collections.Generic;
using System.Linq;
using GHPC.Utility;
using GHPC.Vehicle;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class Followers
	{
		internal static readonly AccessTools.FieldRef<LateFollow, Vector3> PosShift = AccessTools.FieldRefAccess<LateFollow, Vector3>("_localPosShift");
		internal static readonly AccessTools.FieldRef<LateFollow, Quaternion> RotShift = AccessTools.FieldRefAccess<LateFollow, Quaternion>("_localRotShift");

		internal static void PlaceOnBone(Transform piece, Transform bone, Transform target)
		{
			var follow = target.GetComponentsInParent<LateFollow>(true).FirstOrDefault(f => f.FollowTarget == bone);
			if (follow == null)
			{
				piece.SetParent(bone, false);
				piece.localPosition = Vector3.zero; piece.localRotation = Quaternion.identity; piece.localScale = Vector3.one;
				piece.SetParent(target, true);
				return;
			}
			var boneToFollower = Matrix4x4.TRS(PosShift(follow), RotShift(follow), Vector3.one);
			var followerToTarget = follow.transform.worldToLocalMatrix * target.localToWorldMatrix;
			var boneInTarget = (boneToFollower * followerToTarget).inverse;
			piece.SetParent(target, false);
			piece.localPosition = boneInTarget.MultiplyPoint3x4(Vector3.zero);
			piece.localRotation = boneInTarget.rotation;
			piece.localScale = Vector3.one;
		}

		internal static HashSet<LateFollow> On(Transform root, Transform bone)
		{
			var set = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true).Where(f => f.FollowTarget == bone));
			var target = bone.GetComponent<LateFollowTarget>();
			if (target != null) foreach (var f in target.LateFollowers) if (f != null) set.Add(f);
			return set;
		}

		internal static void Refollow(Transform root, Transform bone, (Vector3 pos, Quaternion rot) old)
		{
			var set = On(root, bone);
			var oldM = Matrix4x4.TRS(old.pos, old.rot, bone.lossyScale);
			foreach (var f in set)
			{
				var world = oldM.MultiplyPoint3x4(PosShift(f));
				var r = RotShift(f);
				PosShift(f) = bone.InverseTransformPoint(world);
				RotShift(f) = Quaternion.Inverse(bone.rotation) * old.rot * r;
			}
		}

		internal static void ShiftBone(Transform bone, Vector3 delta)
		{
			var kids = bone.Cast<Transform>().Select(c => (c, c.position, c.rotation)).ToList();
			bone.localPosition += delta;
			foreach (var (c, p, r) in kids) c.SetPositionAndRotation(p, r);
		}

		internal static Transform TurretBone(Vehicle v) => v.transform.Find("---MESH---/HULL/TURRET");
	}
}
