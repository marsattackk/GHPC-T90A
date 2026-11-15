using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// The second LOD gets the first one's materials. Has to happen before HeatSource collects its renderers, which it only does once.
	internal static class VisualLods
	{
		internal static void Sync(Transform root, string tag, string[] dropMaterialPrefixes = null)
		{
			var notes = new List<string>();
			foreach (var lodGroup in root.GetComponentsInChildren<LODGroup>(true))
			{
				var lods = lodGroup.GetLODs();
				if (lods.Length < 2 || lods[0].renderers.Length != 1 || lods[1].renderers.Length != 1) continue;
				var lod0Renderer = lods[0].renderers[0];
				var lod1Renderer = lods[1].renderers[0];
				if (lod0Renderer == null || lod1Renderer == null) continue;
				var mats = lod0Renderer.sharedMaterials;
				var mf1 = lod1Renderer.GetComponent<MeshFilter>();
				if (mf1 == null || mf1.sharedMesh == null || mf1.sharedMesh.subMeshCount != mats.Length)
				{
					notes.Add($"{lodGroup.name}: LOD1 left as built (submeshes {mf1?.sharedMesh?.subMeshCount} vs {mats.Length} materials)");
					continue;
				}
				lod1Renderer.sharedMaterials = mats;
				lod1Renderer.shadowCastingMode = lod0Renderer.shadowCastingMode;
				lod1Renderer.receiveShadows = lod0Renderer.receiveShadows;
				int dropped = 0;
				if (dropMaterialPrefixes != null && mf1.sharedMesh.isReadable)
				{
					var drop = Enumerable.Range(0, mats.Length)
						.Where(i => mats[i] != null && dropMaterialPrefixes.Any(p => mats[i].name.StartsWith(p)))
						.ToList();
					if (drop.Count > 0)
					{
						var mesh = Object.Instantiate(mf1.sharedMesh);   // the bundle's mesh is every unit's
						mesh.name = mf1.sharedMesh.name;
						foreach (int i in drop)
						{
							dropped += mesh.GetIndexCount(i) / 3 > 0 ? 1 : 0;
							mesh.SetTriangles(new int[0], i);
						}
						mf1.sharedMesh = mesh;
					}
				}
				notes.Add($"{lodGroup.name}: LOD1 {mats.Length} materials{(dropped > 0 ? $", {dropped} dropped" : "")}, below {lods[0].screenRelativeTransitionHeight:0.00} of the screen");
			}
		}
	}
}
