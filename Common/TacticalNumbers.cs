using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	internal static class TacticalNumbers
	{
		internal sealed class Settings
		{
			public string Mode = "rear lamp";
			public Vector3 LampFace, LampNormal = new Vector3(0f, 0f, -1f);
			public Vector3? BoxFaceR, BoxNormalR, BoxFaceL, BoxNormalL;
			public float Standoff = 0.008f;
			public float DigitScale = 1.25f;
		}

		internal static void Apply(Transform root, Transform turret, Settings s)
		{
			if (s.Mode == "T-72 position") return;
			var plates = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("convoy number lights")).ToList();
			var done = new HashSet<Mesh>();
			var target = turret.TransformPoint(s.LampFace + s.LampNormal * s.Standoff);
			foreach (var r in plates)
			{
				var mf = r.GetComponent<MeshFilter>();
				if (mf != null)
				{
					var mesh = mf.mesh;
					if (done.Add(mesh)) ScaleDigits(mesh, s.DigitScale);
				}
				var c = mf != null ? r.transform.TransformPoint(mf.mesh.bounds.center) : r.bounds.center;
				r.transform.position += target - c;
			}
			var glow = root.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name == "numbers glow");
			if (glow != null && plates.Count > 0) glow.transform.position = target + turret.TransformDirection(s.LampNormal) * 0.05f;
			if (s.Mode == "side boxes")
			{
				var bustle = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "bustle_number_3digit");
				if (bustle != null && s.BoxFaceR.HasValue && s.BoxFaceL.HasValue)
				{
					SeatOnBoxes(bustle, turret, s);
					var mr = bustle.GetComponent<MeshRenderer>();
					if (mr != null) mr.enabled = true;
				}
			}
		}

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

		static void SeatOnBoxes(MeshFilter mf, Transform turret, Settings s)
		{
			var mesh = mf.mesh;
			var v = mesh.vertices; var n = mesh.normals;
			var t = mf.transform;
			foreach (var (sign, face, normal) in new[] { (1f, s.BoxFaceR.Value, s.BoxNormalR.Value), (-1f, s.BoxFaceL.Value, s.BoxNormalL.Value) })
			{
				var idx = Enumerable.Range(0, v.Length).Where(i => Mathf.Sign(turret.InverseTransformPoint(t.TransformPoint(v[i])).x) == sign).ToList();
				if (idx.Count == 0) continue;
				var pts = idx.Select(i => turret.InverseTransformPoint(t.TransformPoint(v[i]))).ToList();
				var c = pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
				var facing = n.Length == v.Length ? idx.Select(i => turret.InverseTransformDirection(t.TransformDirection(n[i]))).Aggregate(Vector3.zero, (a, b) => a + b).normalized : new Vector3(sign, 0f, 0f);
				var rot = Quaternion.FromToRotation(facing, normal.normalized);
				var target = face + normal.normalized * s.Standoff;
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
