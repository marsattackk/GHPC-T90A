using System.Linq;
using GHPC;
using UnityEngine;

namespace T90A.Common
{
	// A rigid cover either cuts into the turret or lets the barrel slide out of it, so it's skinned to both.
	internal static class MantletCover
	{
		internal static string Skin(Transform root, Transform turret, Transform gun, Transform cover)
		{
			var meshFilter = cover.GetComponent<MeshFilter>();
			var meshRenderer = cover.GetComponent<MeshRenderer>();
			if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null) return null;
			var mesh = Object.Instantiate(meshFilter.sharedMesh);
			mesh.name = meshFilter.sharedMesh.name + " (skinned)";
			var vertices = mesh.vertices;
			float zmin = vertices.Min(v => v.z), zmax = vertices.Max(x => x.z), len = zmax - zmin;
			float rearZ = zmin + 0.3f * len, frontZ = zmax - 0.3f * len;
			var weights = new BoneWeight[vertices.Length];
			for (int i = 0; i < vertices.Length; i++)
			{
				float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rearZ, frontZ, vertices[i].z));
				weights[i] = blend >= 0.5f ? new BoneWeight { boneIndex0 = 1, weight0 = blend, boneIndex1 = 0, weight1 = 1f - blend }
					: new BoneWeight { boneIndex0 = 0, weight0 = 1f - blend, boneIndex1 = 1, weight1 = blend };
			}
			mesh.boneWeights = weights;
			// The gun can already be elevated here, bind at rest.
			var plat = root.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(c => c.Transform == gun);
			var restRot = plat != null
				? (Quaternion)Reflect.Field(typeof(AimablePlatform), "_originalLocalRotation").GetValue(plat)
				: gun.localRotation;
			var gunRestInTurret = gun.parent == turret
				? Matrix4x4.TRS(gun.localPosition, restRot, gun.localScale)
				: turret.worldToLocalMatrix * gun.localToWorldMatrix;
			mesh.bindposes = new[] { gunRestInTurret, Matrix4x4.identity };
			var mats = meshRenderer.sharedMaterials;
			Object.DestroyImmediate(meshRenderer);
			Object.DestroyImmediate(meshFilter);
			var smr = cover.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = new[] { turret, gun };
			smr.rootBone = gun;
			smr.sharedMaterials = mats;
			var bounds = mesh.bounds;
			bounds.Expand(0.3f);
			smr.localBounds = bounds;
			return $"skinned between turret and gun ({vertices.Length} verts, gun-local z {zmin:0.00}..{zmax:0.00} m; turret-only behind {rearZ:0.00}, gun-only ahead of {frontZ:0.00}; bind pose from {(plat != null ? "the stowed gun rotation" : "the current gun rotation")})";
		}
	}
}
