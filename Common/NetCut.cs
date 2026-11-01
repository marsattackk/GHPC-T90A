using System.Collections.Generic;
using UnityEngine;

namespace T90A.Common
{
	internal static class NetCut
	{
		internal const float MarginM = 0.08f;

		sealed class Net
		{
			internal Transform Root, Vis;
			internal SkinnedMeshRenderer Smr;
			internal Vector3[] Hang;
		}

		static readonly List<Net> _nets = new List<Net>();

		internal static void Register(Transform root, Transform vis, SkinnedMeshRenderer smr, Vector3 along, float half)
		{
			_nets.RemoveAll(n => n.Vis == null);
			var mesh = smr.sharedMesh;
			var v = mesh.vertices;
			var hang = new List<Vector3>();
			for (int s = 0; s < mesh.subMeshCount; s++)
			{
				var t = mesh.GetTriangles(s);
				for (int i = 0; i + 2 < t.Length; i += 3)
				{
					var c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
					hang.Add(along * Mathf.Clamp(Vector3.Dot(c, along), -half, half));
				}
			}
			_nets.Add(new Net { Root = root, Vis = vis, Smr = smr, Hang = hang.ToArray() });
		}

		internal static void Cut(Transform container)
		{
			var mf = container != null ? container.GetComponent<MeshFilter>() : null;
			if (mf == null || mf.sharedMesh == null) return;
			var box = mf.sharedMesh.bounds; box.Expand(2f * MarginM);
			var root = container.root;
			foreach (var n in _nets)
			{
				if (n.Vis == null || n.Smr == null || n.Root == null || n.Root.root != root) continue;
				var mesh = n.Smr.sharedMesh;
				int k = 0, cut = 0;
				for (int s = 0; s < mesh.subMeshCount; s++)
				{
					var t = mesh.GetTriangles(s);
					var kept = new List<int>(t.Length);
					for (int i = 0; i + 2 < t.Length; i += 3, k++)
					{
						var p = container.InverseTransformPoint(n.Vis.TransformPoint(n.Hang[k]));
						// Cut triangles stay as degenerates so the hang points keep their order.
						if (box.Contains(p) && !(t[i] == t[i + 1] && t[i] == t[i + 2])) { cut++; kept.Add(t[i]); kept.Add(t[i]); kept.Add(t[i]); }
						else { kept.Add(t[i]); kept.Add(t[i + 1]); kept.Add(t[i + 2]); }
					}
					if (cut > 0) mesh.SetTriangles(kept, s, false);
				}
			}
		}
	}
}
