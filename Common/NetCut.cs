using System.Collections.Generic;
using UnityEngine;

namespace T90A.Common
{
	// Cuts the camo net triangles inside a destroyed ERA brick's box.
	internal static class NetCut
	{
		internal const float MarginM = 0.08f;

		sealed class Net
		{
			internal Transform Root, Vis;
			internal SkinnedMeshRenderer Smr;
			internal Vector3[] Hang;
			internal int Cut;
		}

		static readonly List<Net> _nets = new List<Net>();

		internal static void Register(Transform root, Transform vis, SkinnedMeshRenderer smr, Vector3 along, float half)
		{
			_nets.RemoveAll(n => n.Vis == null);
			var mesh = smr.sharedMesh;
			var vertices = mesh.vertices;
			var hang = new List<Vector3>();
			for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
			{
				var triangles = mesh.GetTriangles(subMesh);
				for (int i = 0; i + 2 < triangles.Length; i += 3)
				{
					var centre = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
					hang.Add(along * Mathf.Clamp(Vector3.Dot(centre, along), -half, half));
				}
			}
			_nets.Add(new Net { Root = root, Vis = vis, Smr = smr, Hang = hang.ToArray() });
		}

		internal static string Cut(Transform container)
		{
			var meshFilter = container != null ? container.GetComponent<MeshFilter>() : null;
			if (meshFilter == null || meshFilter.sharedMesh == null) return "nets: no container mesh";
			var box = meshFilter.sharedMesh.bounds;
			box.Expand(2f * MarginM);
			var root = container.root;
			var parts = new List<string>();
			foreach (var net in _nets)
			{
				if (net.Vis == null || net.Smr == null || net.Root == null || net.Root.root != root) continue;
				var mesh = net.Smr.sharedMesh;
				int k = 0, cut = 0;
				for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
				{
					var triangles = mesh.GetTriangles(subMesh);
					var kept = new List<int>(triangles.Length);
					for (int i = 0; i + 2 < triangles.Length; i += 3, k++)
					{
						var point = container.InverseTransformPoint(net.Vis.TransformPoint(net.Hang[k]));
						// Cut triangles stay as degenerates so the hang points keep their order.
						if (box.Contains(point) && !(triangles[i] == triangles[i + 1] && triangles[i] == triangles[i + 2]))
						{
							cut++;
							kept.Add(triangles[i]);
							kept.Add(triangles[i]);
							kept.Add(triangles[i]);
						}
						else
						{
							kept.Add(triangles[i]);
							kept.Add(triangles[i + 1]);
							kept.Add(triangles[i + 2]);
						}
					}
					if (cut > 0) mesh.SetTriangles(kept, subMesh, false);
				}
				if (cut > 0)
				{
					net.Cut += cut;
					parts.Add($"{net.Vis.name} -{cut} ({net.Cut} of {net.Hang.Length})");
				}
			}
			return parts.Count > 0 ? "nets cut: " + string.Join(", ", parts) : "nets: none hang from it";
		}
	}
}
