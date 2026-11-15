using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// MergedVehicleNumberControl draws the digits as UV shifts, so the meshes can go anywhere.
	internal static class TacticalNumbers
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Mode = "rear lamp";
			public Vector3 LampFace, LampNormal = new Vector3(0f, 0f, -1f);
			public Vector3? BoxFaceR, BoxNormalR, BoxFaceL, BoxNormalL;
			public float Standoff = 0.008f;
			public float DigitScale = 1.25f;
			public float? DigitSpacing;
			public string[] PlateNames = { "convoy number lights" };
			public string Tag = "port";
		}
#pragma warning restore CS0649

		internal static List<string> Apply(Transform root, Transform turret, Settings s)
		{
			var log = new List<string>();
			if (s.Mode == "T-72 position") return log;
			var plates = root
				.GetComponentsInChildren<MeshRenderer>(true)
				.Where(r => s.PlateNames.Any(p => r.name.StartsWith(p)))
				.ToList();
			var target = turret.TransformPoint(s.LampFace + s.LampNormal * s.Standoff);
			foreach (var renderer in plates)
			{
				var meshFilter = renderer.GetComponent<MeshFilter>();
				string spacing = "";
				if (meshFilter != null)
				{
					// Not sharedMesh, the number control works on the instance.
					spacing = ScaleDigits(meshFilter.mesh, s.DigitScale, s.DigitSpacing, renderer.transform.lossyScale.x);
				}
				var centre = meshFilter != null ? renderer.transform.TransformPoint(meshFilter.mesh.bounds.center) : renderer.bounds.center;
				renderer.transform.position += target - centre;
				var localPoint = turret.InverseTransformPoint(target);
				log.Add($"{s.Tag} number plate {renderer.name}: turret-space centre ({localPoint.x:0.000}, {localPoint.y:0.000}, {localPoint.z:0.000}), {spacing}rotation {renderer.transform.rotation.eulerAngles}, scale {renderer.transform.lossyScale}, enabled {renderer.enabled}, material {renderer.sharedMaterial?.name}.");
			}
			var glow = root.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name == "numbers glow");
			if (glow != null && plates.Count > 0) glow.transform.position = target + turret.TransformDirection(s.LampNormal) * 0.05f;
			string boxes = "";
			if (s.Mode == "side boxes")
			{
				var bustle = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "bustle_number_3digit");
				if (bustle != null && s.BoxFaceR.HasValue && s.BoxFaceL.HasValue)
				{
					SeatOnBoxes(bustle, turret, s);
					var meshRenderer = bustle.GetComponent<MeshRenderer>();
					if (meshRenderer != null) meshRenderer.enabled = true;
					boxes = ", bustle numbers re-seated on the side boxes";
				}
				else boxes = bustle == null ? ", bustle numbers not found" : ", no side boxes for this port";
			}
			log.Add($"{s.Tag} tactical numbers ({s.Mode}): {plates.Count} convoy plates on the rear lamp{boxes}.");
			return log;
		}

		static string ScaleDigits(Mesh mesh, float k, float? spacing, float unit)
		{
			var vertices = mesh.vertices;
			if (vertices.Length == 0) return "";
			var size = mesh.bounds.size;
			int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
			float middle = vertices.Average(v => v[axis]);
			var groups = new[]
			{
				Enumerable.Range(0, vertices.Length).Where(i => vertices[i][axis] < middle).ToList(),
				Enumerable.Range(0, vertices.Length).Where(i => vertices[i][axis] >= middle).ToList()
			};
			if (groups.Any(i => i.Count == 0)) return "";
			var centres = groups
				.Select(indexGroup => indexGroup.Aggregate(Vector3.zero, (t, i) => t + vertices[i]) / indexGroup.Count)
				.ToArray();
			float vanilla = Vector3.Distance(centres[0], centres[1]);
			var mid = (centres[0] + centres[1]) * 0.5f;
			var dir = (centres[1] - centres[0]).normalized;
			float half = spacing.HasValue ? spacing.Value / unit * 0.5f : 0f;
			vanilla *= unit;
			for (int groupIndex = 0; groupIndex < 2; groupIndex++)
				foreach (int i in groups[groupIndex])
					vertices[i] = (spacing.HasValue ? mid + dir * (groupIndex == 0 ? -half : half) : centres[groupIndex])
						+ (vertices[i] - centres[groupIndex]) * k;
			mesh.vertices = vertices;
			mesh.RecalculateBounds();
			var extent = mesh.bounds.size * 100f * unit;
			var sides = new[] { extent.x, extent.y, extent.z }.OrderByDescending(x => x).ToArray();
			string sep = spacing.HasValue
				? $"centre spacing {spacing.Value * 100f:0.0} cm (vanilla {vanilla * 100f:0.0})"
				: $"centre spacing {vanilla * 100f:0.0} cm (vanilla)";
			return $"digits x{k:0.##} each, {sep}, plate {sides[0]:0.0} x {sides[1]:0.0} cm, ";
		}

		static void SeatOnBoxes(MeshFilter mf, Transform turret, Settings s)
		{
			var mesh = mf.mesh;
			var vertices = mesh.vertices;
			var normals = mesh.normals;
			var meshTransform = mf.transform;
			foreach (var (sign, face, normal) in new[]
			{
				(1f, s.BoxFaceR.Value, s.BoxNormalR.Value),
				(-1f, s.BoxFaceL.Value, s.BoxNormalL.Value)
			})
			{
				var indices = Enumerable.Range(0, vertices.Length)
					.Where(i => Mathf.Sign(turret.InverseTransformPoint(meshTransform.TransformPoint(vertices[i])).x) == sign)
					.ToList();
				if (indices.Count == 0) continue;
				var pts = indices.Select(i => turret.InverseTransformPoint(meshTransform.TransformPoint(vertices[i]))).ToList();
				var centre = pts.Aggregate(Vector3.zero, (t, p) => t + p) / pts.Count;
				var facing = normals.Length == vertices.Length
					? indices
						.Select(i => turret.InverseTransformDirection(meshTransform.TransformDirection(normals[i])))
						.Aggregate(Vector3.zero, (t, p) => t + p).normalized
					: new Vector3(sign, 0f, 0f);
				var rot = Quaternion.FromToRotation(facing, normal.normalized);
				var target = face + normal.normalized * s.Standoff;
				for (int k = 0; k < indices.Count; k++)
				{
					var placed = target + rot * (pts[k] - centre);
					vertices[indices[k]] = meshTransform.InverseTransformPoint(turret.TransformPoint(placed));
					if (normals.Length == vertices.Length)
						normals[indices[k]] = meshTransform.InverseTransformDirection(turret.TransformDirection(rot * turret.InverseTransformDirection(meshTransform.TransformDirection(normals[indices[k]]))));
				}
			}
			mesh.vertices = vertices;
			if (normals.Length == vertices.Length) mesh.normals = normals;
			mesh.RecalculateBounds();
		}
	}
}
