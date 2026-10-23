using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public static class HatchUndersides
	{
		public static readonly Color FallbackColour = new Color(0.192f, 0.208f, 0.161f, 1f);

		public static int Plain(Transform root, string innerName)
		{
			int n = 0;
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
				foreach (var m in r.sharedMaterials)
					if (m != null && m.name.StartsWith(innerName))
					{
						if (m.HasProperty("_Color")) m.SetColor("_Color", FallbackColour);
						if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
						n++;
					}
			return n;
		}

		public static void Paint(Transform root, string innerName, string paintName, RectInt patch, Vector2Int atlas)
		{
			float W = atlas.x, H = atlas.y;
			var uvPatch = new Rect(patch.x / W, 1f - (patch.y + patch.height) / H, patch.width / W, patch.height / H);
			float pw = patch.width, ph = patch.height;
			var anyPaint = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
				.FirstOrDefault(m => m != null && m.name.StartsWith(paintName) && m.shader.name == "GHPC/VehicleShader");
			foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				var mats = mr.sharedMaterials;
				int k = Array.FindIndex(mats, m => m != null && m.name.StartsWith(innerName));
				var mf = mr.GetComponent<MeshFilter>();
				if (k < 0 || mf == null || mf.sharedMesh == null) continue;
				var paint = mats.FirstOrDefault(m => m != null && m.name.StartsWith(paintName) && m.shader.name == "GHPC/VehicleShader") ?? anyPaint;
				if (paint == null || !mf.sharedMesh.isReadable) continue;
				var mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
				mesh.name = mf.sharedMesh.name + " (painted undersides)";
				var v = new List<Vector3>(); mesh.GetVertices(v);
				var n = new List<Vector3>(); mesh.GetNormals(n);
				var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
				var tris = mesh.GetTriangles(k);
				var wid = tris.Select(i => Vector3Int.RoundToInt(v[i] * 1000f)).ToArray();
				var ids = wid.Distinct().Select((key, i) => (key, i)).ToDictionary(p => p.key, p => p.i);
				var par = Enumerable.Range(0, ids.Count).ToArray();
				int Find(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
				for (int t = 0; t < tris.Length; t += 3) { par[Find(ids[wid[t + 1]])] = Find(ids[wid[t]]); par[Find(ids[wid[t + 2]])] = Find(ids[wid[t]]); }
				var pieces = Enumerable.Range(0, tris.Length / 3).GroupBy(t => Find(ids[wid[t * 3]])).Select(g => g.ToList()).ToList();
				var outTris = new int[tris.Length];
				foreach (var piece in pieces)
				{
					var nrm = Vector3.zero;
					foreach (int t in piece) nrm += Vector3.Cross(v[tris[t * 3 + 1]] - v[tris[t * 3]], v[tris[t * 3 + 2]] - v[tris[t * 3]]);
					nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.down;
					var ax = Vector3.ProjectOnPlane(Mathf.Abs(nrm.x) < 0.9f ? Vector3.right : Vector3.forward, nrm).normalized;
					var ay = Vector3.Cross(nrm, ax);
					var used = piece.SelectMany(t => new[] { tris[t * 3], tris[t * 3 + 1], tris[t * 3 + 2] }).Distinct().ToList();
					float x0 = used.Min(i => Vector3.Dot(v[i], ax)), x1 = used.Max(i => Vector3.Dot(v[i], ax));
					float y0 = used.Min(i => Vector3.Dot(v[i], ay)), y1 = used.Max(i => Vector3.Dot(v[i], ay));
					bool swap = (y1 - y0) > (x1 - x0);
					float ex = swap ? y1 - y0 : x1 - x0, ey = swap ? x1 - x0 : y1 - y0;
					float pxPerM = Mathf.Min(300f, Mathf.Min(pw / Mathf.Max(ex, 1e-3f), ph / Mathf.Max(ey, 1e-3f)));
					float mx = (pw - ex * pxPerM) / 2f, my = (ph - ey * pxPerM) / 2f;
					var map = new Dictionary<int, int>();
					foreach (int i in used)
					{
						map[i] = v.Count;
						v.Add(v[i]); n.Add(n.Count > i ? n[i] : -nrm);
						float px = Vector3.Dot(v[i], ax) - x0, py = Vector3.Dot(v[i], ay) - y0;
						if (swap) { var tmp = px; px = py; py = tmp; }
						uv.Add(new Vector2(uvPatch.xMin + (mx + px * pxPerM) / W, uvPatch.yMin + (my + py * pxPerM) / H));
					}
					foreach (int t in piece) for (int j = 0; j < 3; j++) outTris[t * 3 + j] = map[tris[t * 3 + j]];
				}
				mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
				mesh.SetTriangles(outTris, k);
				mesh.RecalculateTangents();
				mesh.RecalculateBounds();
				mf.sharedMesh = mesh;
				mats[k] = paint;
				mr.sharedMaterials = mats;
			}
		}
	}
}
