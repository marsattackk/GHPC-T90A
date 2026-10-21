using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A
{
	internal static class Numbers
	{
		static readonly Vector3 LampFace = new Vector3(-0.489f, 2.380f - 1.369f, -1.684f + 0.527f);
		static readonly Vector3 LampNormal = new Vector3(0f, 0f, -1f);
		static readonly Vector3 BoxFaceR = new Vector3(1.179f, 1.804f - 1.369f, -1.592f + 0.527f);
		static readonly Vector3 BoxNormalR = new Vector3(0.92f, -0.02f, -0.39f);
		static readonly Vector3 BoxFaceL = new Vector3(-1.278f, 1.827f - 1.369f, -1.558f + 0.527f);
		static readonly Vector3 BoxNormalL = new Vector3(-0.91f, -0.01f, -0.41f);
		const float Standoff = 0.008f;

		internal static void Apply(Transform root, Transform turret, string mode)
		{
			if (mode == "T-72 position") return;
			var plates = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("convoy number lights")).ToList();
			var done = new HashSet<Mesh>();
			var target = turret.TransformPoint(LampFace + LampNormal * Standoff);
			foreach (var r in plates)
			{
				var mf = r.GetComponent<MeshFilter>();
				if (mf != null)
				{
					var mesh = mf.mesh;
					if (done.Add(mesh)) ScaleDigits(mesh, DigitScale);
				}
				var c = mf != null ? r.transform.TransformPoint(mf.mesh.bounds.center) : r.bounds.center;
				r.transform.position += target - c;
			}
			var glow = root.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name == "numbers glow");
			if (glow != null && plates.Count > 0) glow.transform.position = target + turret.TransformDirection(LampNormal) * 0.05f;
			if (mode == "side boxes")
			{
				var bustle = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "bustle_number_3digit");
				if (bustle != null)
				{
					SeatOnBoxes(bustle, turret);
					var mr = bustle.GetComponent<MeshRenderer>();
					if (mr != null) mr.enabled = true;
				}
			}
		}

		const float DigitScale = 1.25f;

		static void ScaleDigits(Mesh mesh, float k)
		{
			var v = mesh.vertices;
			if (v.Length == 0) return;
			float cx = v.Average(p => p.x);
			var groups = new[] { Enumerable.Range(0, v.Length).Where(i => v[i].x < cx).ToList(), Enumerable.Range(0, v.Length).Where(i => v[i].x >= cx).ToList() };
			foreach (var g in groups)
			{
				if (g.Count == 0) continue;
				var c = g.Aggregate(Vector3.zero, (a, i) => a + v[i]) / g.Count;
				foreach (int i in g) v[i] = c + (v[i] - c) * k;
			}
			mesh.vertices = v;
			mesh.RecalculateBounds();
		}

		static void SeatOnBoxes(MeshFilter mf, Transform turret)
		{
			var mesh = mf.mesh;
			var v = mesh.vertices; var n = mesh.normals;
			var t = mf.transform;
			foreach (var (sign, face, normal) in new[] { (1f, BoxFaceR, BoxNormalR), (-1f, BoxFaceL, BoxNormalL) })
			{
				var idx = Enumerable.Range(0, v.Length).Where(i => Mathf.Sign(turret.InverseTransformPoint(t.TransformPoint(v[i])).x) == sign).ToList();
				if (idx.Count == 0) continue;
				var pts = idx.Select(i => turret.InverseTransformPoint(t.TransformPoint(v[i]))).ToList();
				var c = pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
				var facing = n.Length == v.Length ? idx.Select(i => turret.InverseTransformDirection(t.TransformDirection(n[i]))).Aggregate(Vector3.zero, (a, b) => a + b).normalized : new Vector3(sign, 0f, 0f);
				var rot = Quaternion.FromToRotation(facing, normal.normalized);
				var target = face + normal.normalized * Standoff;
				for (int k = 0; k < idx.Count; k++)
				{
					var p = target + rot * (pts[k] - c);
					v[idx[k]] = t.InverseTransformPoint(turret.TransformPoint(p));
					if (n.Length == v.Length) n[idx[k]] = t.InverseTransformDirection(turret.TransformDirection(rot * turret.InverseTransformDirection(t.TransformDirection(n[idx[k]]))));
				}
			}
			mesh.vertices = v;
			if (n.Length == v.Length) mesh.normals = n;
			mesh.RecalculateBounds();
		}
	}
}
