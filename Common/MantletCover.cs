using System.Linq;
using GHPC;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class MantletCover
	{
		internal static string Skin(Transform root, Transform turret, Transform gun, Transform cover)
		{
			var mf = cover.GetComponent<MeshFilter>();
			var mr = cover.GetComponent<MeshRenderer>();
			if (mf == null || mr == null || mf.sharedMesh == null) return null;
			var mesh = Object.Instantiate(mf.sharedMesh);
			mesh.name = mf.sharedMesh.name + " (skinned)";
			var v = mesh.vertices;
			float zmin = v.Min(p => p.z), zmax = v.Max(p => p.z), len = zmax - zmin;
			float zr = zmin + 0.3f * len, zf = zmax - 0.3f * len;
			var w = new BoneWeight[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				float g = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zr, zf, v[i].z));
				w[i] = g >= 0.5f ? new BoneWeight { boneIndex0 = 1, weight0 = g, boneIndex1 = 0, weight1 = 1f - g }
					: new BoneWeight { boneIndex0 = 0, weight0 = 1f - g, boneIndex1 = 1, weight1 = g };
			}
			mesh.boneWeights = w;
			var plat = root.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p.Transform == gun);
			var restRot = plat != null ? (Quaternion)AccessTools.Field(typeof(AimablePlatform), "_originalLocalRotation").GetValue(plat) : gun.localRotation;
			var gunRestInTurret = gun.parent == turret ? Matrix4x4.TRS(gun.localPosition, restRot, gun.localScale) : turret.worldToLocalMatrix * gun.localToWorldMatrix;
			mesh.bindposes = new[] { gunRestInTurret, Matrix4x4.identity };
			var mats = mr.sharedMaterials;
			Object.DestroyImmediate(mr);
			Object.DestroyImmediate(mf);
			var smr = cover.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = new[] { turret, gun };
			smr.rootBone = gun;
			smr.sharedMaterials = mats;
			var b = mesh.bounds; b.Expand(0.3f);
			smr.localBounds = b;
			return $"skinned between turret and gun ({v.Length} verts, gun-local z {zmin:0.00}..{zmax:0.00} m; turret-only behind {zr:0.00}, gun-only ahead of {zf:0.00}; bind pose from {(plat != null ? "the stowed gun rotation" : "the current gun rotation")})";
		}
	}
}
