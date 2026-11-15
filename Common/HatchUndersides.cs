using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// The model's UVs put the hatch undersides on bolt detail, so they get a flat patch of atlas of their own.
	public static class HatchUndersides
	{
		public static readonly Color FallbackColour = new Color(0.192f, 0.208f, 0.161f, 1f);

		public static int Plain(Transform root, string innerName)
		{
			int count = 0;
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
				foreach (var material in renderer.sharedMaterials)
					if (material != null && material.name.StartsWith(innerName))
					{
						if (material.HasProperty("_Color")) material.SetColor("_Color", FallbackColour);
						if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);
						count++;
					}
			return count;
		}

		// The model's own UVs land the undersides on bolt and cast detail, hence flat UVs into a smooth patch.
		public static List<string> Paint(Transform root, string innerName, string paintName, RectInt patch, Vector2Int atlas)
		{
			float atlasWidth = atlas.x, atlasHeight = atlas.y;
			var uvPatch = new Rect(
				patch.x / atlasWidth,
				1f - (patch.y + patch.height) / atlasHeight,
				patch.width / atlasWidth,
				patch.height / atlasHeight);
			float patchWidth = patch.width, patchHeight = patch.height;
			var anyPaint = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
				.FirstOrDefault(m => m != null
					&& m.name.StartsWith(paintName)
					&& m.shader.name == "GHPC/VehicleShader");
			var notes = new List<string>();
			foreach (var meshRenderer in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				var mats = meshRenderer.sharedMaterials;
				int k = Array.FindIndex(mats, m => m != null && m.name.StartsWith(innerName));
				var meshFilter = meshRenderer.GetComponent<MeshFilter>();
				if (k < 0 || meshFilter == null || meshFilter.sharedMesh == null) continue;
				var paint = mats.FirstOrDefault(m => m != null
					&& m.name.StartsWith(paintName)
					&& m.shader.name == "GHPC/VehicleShader")
					?? anyPaint;
				if (paint == null || !meshFilter.sharedMesh.isReadable)
				{
					notes.Add($"{meshRenderer.name}: left plain ({(paint == null ? "no painted turret material" : "mesh not readable")})");
					continue;
				}
				var mesh = UnityEngine.Object.Instantiate(meshFilter.sharedMesh);
				mesh.name = meshFilter.sharedMesh.name + " (painted undersides)";
				var vertices = new List<Vector3>();
				mesh.GetVertices(vertices);
				var normals = new List<Vector3>();
				mesh.GetNormals(normals);
				var uv = new List<Vector2>();
				mesh.GetUVs(0, uv);
				var tris = mesh.GetTriangles(k);
				var wid = tris.Select(i => Vector3Int.RoundToInt(vertices[i] * 1000f)).ToArray();
				var ids = wid.Distinct().Select((key, i) => (key, i)).ToDictionary(p => p.key, x => x.i);
				var par = Enumerable.Range(0, ids.Count).ToArray();
				int Find(int node)
				{
					while (par[node] != node)
					{
						par[node] = par[par[node]];
						node = par[node];
					}
					return node;
				}
				for (int triangle = 0; triangle < tris.Length; triangle += 3)
				{
					par[Find(ids[wid[triangle + 1]])] = Find(ids[wid[triangle]]);
					par[Find(ids[wid[triangle + 2]])] = Find(ids[wid[triangle]]);
				}
				var pieces = Enumerable.Range(0, tris.Length / 3)
					.GroupBy(t => Find(ids[wid[t * 3]]))
					.Select(b => b.ToList())
					.ToList();
				var outTris = new int[tris.Length];
				foreach (var piece in pieces)
				{
					var nrm = Vector3.zero;
					foreach (int triangle in piece)
						nrm += Vector3.Cross(
							vertices[tris[triangle * 3 + 1]] - vertices[tris[triangle * 3]],
							vertices[tris[triangle * 3 + 2]] - vertices[tris[triangle * 3]]);
					nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.down;
					var axisX = Vector3.ProjectOnPlane(Mathf.Abs(nrm.x) < 0.9f ? Vector3.right : Vector3.forward, nrm).normalized;
					var axisY = Vector3.Cross(nrm, axisX);
					var used = piece
						.SelectMany(t => new[] { tris[t * 3], tris[t * 3 + 1], tris[t * 3 + 2] })
						.Distinct()
						.ToList();
					float minX = used.Min(i => Vector3.Dot(vertices[i], axisX)), maxX = used.Max(i => Vector3.Dot(vertices[i], axisX));
					float minY = used.Min(i => Vector3.Dot(vertices[i], axisY)), maxY = used.Max(i => Vector3.Dot(vertices[i], axisY));
					bool swap = (maxY - minY) > (maxX - minX);
					float extentX = swap ? maxY - minY : maxX - minX, extentY = swap ? maxX - minX : maxY - minY;
					float pxPerM = Mathf.Min(
						300f,
						Mathf.Min(patchWidth / Mathf.Max(extentX, 1e-3f), patchHeight / Mathf.Max(extentY, 1e-3f)));
					float marginX = (patchWidth - extentX * pxPerM) / 2f, marginY = (patchHeight - extentY * pxPerM) / 2f;
					var map = new Dictionary<int, int>();
					foreach (int i in used)
					{
						map[i] = vertices.Count;
						vertices.Add(vertices[i]);
						normals.Add(normals.Count > i ? normals[i] : -nrm);
						float localX = Vector3.Dot(vertices[i], axisX) - minX, localY = Vector3.Dot(vertices[i], axisY) - minY;
						if (swap)
						{
							var previousX = localX;
							localX = localY;
							localY = previousX;
						}
						uv.Add(new Vector2(
							uvPatch.xMin + (marginX + localX * pxPerM) / atlasWidth,
							uvPatch.yMin + (marginY + localY * pxPerM) / atlasHeight));
					}
					foreach (int triangle in piece) for (int j = 0; j < 3; j++) outTris[triangle * 3 + j] = map[tris[triangle * 3 + j]];
					if (piece.Count >= 20)
						notes.Add($"{meshRenderer.name}: piece of {piece.Count} triangles, {extentX * 100f:0} x {extentY * 100f:0} cm, {pxPerM / 100f:0.0} px/cm");
				}
				notes.Add($"{meshRenderer.name}: {pieces.Count} pieces, {tris.Length / 3} triangles in all");
				mesh.SetVertices(vertices);
				mesh.SetNormals(normals);
				mesh.SetUVs(0, uv);
				mesh.SetTriangles(outTris, k);
				mesh.RecalculateTangents();
				mesh.RecalculateBounds();
				meshFilter.sharedMesh = mesh;
				mats[k] = paint;
				meshRenderer.sharedMaterials = mats;
			}
			notes.Insert(
				0,
				$"painted turret material {(anyPaint != null ? anyPaint.name : "NONE FOUND")}, atlas patch x {patch.x} y {patch.y} {patch.width} x {patch.height} of {atlas.x} x {atlas.y} px");
			return notes;
		}
	}
}
