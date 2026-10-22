using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	internal static class VisualLods
	{
		internal static void Sync(Transform root, string[] dropMaterialPrefixes = null)
		{
			foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
			{
				var lods = g.GetLODs();
				if (lods.Length < 2 || lods[0].renderers.Length != 1 || lods[1].renderers.Length != 1) continue;
				var r0 = lods[0].renderers[0]; var r1 = lods[1].renderers[0];
				if (r0 == null || r1 == null) continue;
				var mats = r0.sharedMaterials;
				var mf1 = r1.GetComponent<MeshFilter>();
				if (mf1 == null || mf1.sharedMesh == null || mf1.sharedMesh.subMeshCount != mats.Length) continue;
				r1.sharedMaterials = mats;
				r1.shadowCastingMode = r0.shadowCastingMode;
				r1.receiveShadows = r0.receiveShadows;
				if (dropMaterialPrefixes != null && mf1.sharedMesh.isReadable)
				{
					var drop = Enumerable.Range(0, mats.Length).Where(i => mats[i] != null && dropMaterialPrefixes.Any(p => mats[i].name.StartsWith(p))).ToList();
					if (drop.Count > 0)
					{
						var m = Object.Instantiate(mf1.sharedMesh);
						m.name = mf1.sharedMesh.name;
						foreach (int i in drop) m.SetTriangles(new int[0], i);
						mf1.sharedMesh = m;
					}
				}
			}
		}
	}
}
