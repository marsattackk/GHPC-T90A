using System.Collections.Generic;
using System.Linq;
using GHPC.Weapons;
using UnityEngine;

namespace T90A.Common
{
	internal static class KordBelt
	{
		internal sealed class Settings
		{
			internal string Tag = "Kord";
			internal string Prefix = "PORT_";
			internal string MeshName = "Kord";
			internal string BeltMaterial = "mg_6p49_kord_ammo";
			internal int SpareRounds = 24;
			internal int MaxCases = 300;
			internal string BeltObject => Prefix + "KORD_BELT";
		}

		internal static Belt Fit(Transform carrier, Transform cradle, Settings set)
		{
			var cupVis = carrier;
			string BeltMaterial = set.BeltMaterial;
			int SpareRounds = set.SpareRounds;
			var beltRenderer = cupVis
				.GetComponentsInChildren<MeshRenderer>(true)
				.FirstOrDefault(r => r.sharedMaterials.Any(m => m != null
					&& m.name.StartsWith(BeltMaterial)));
			var meshFilter = beltRenderer?.GetComponent<MeshFilter>();
			if (meshFilter == null || meshFilter.sharedMesh == null) return null;
			if (!meshFilter.sharedMesh.isReadable) return null;
			var mats = beltRenderer.sharedMaterials;
			int k = System.Array.FindIndex(mats, m => m != null && m.name.StartsWith(BeltMaterial));
			var src = meshFilter.sharedMesh;
			var vertices = src.vertices;
			var normals = src.normals;
			var uv = src.uv;
			var tangents = src.tangents;
			if (beltRenderer.transform != cupVis)
			{
				var toCarrier = cupVis.worldToLocalMatrix * beltRenderer.transform.localToWorldMatrix;
				for (int i = 0; i < vertices.Length; i++) vertices[i] = toCarrier.MultiplyPoint3x4(vertices[i]);
				for (int i = 0; i < normals.Length; i++) normals[i] = toCarrier.MultiplyVector(normals[i]).normalized;
				for (int i = 0; i < tangents.Length; i++)
				{
					var direction = toCarrier.MultiplyVector(tangents[i]).normalized;
					tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
				}
			}
			var tris = src.GetTriangles(k);

			var weld = new Dictionary<Vector3Int, int>(Hashing.Vec3Int);
			var wid = new int[tris.Length];
			for (int corner = 0; corner < tris.Length; corner++)
			{
				var key = Vector3Int.RoundToInt(vertices[tris[corner]] * 1000f);
				if (!weld.TryGetValue(key, out int weldId))
				{
					weldId = weld.Count;
					weld[key] = weldId;
				}
				wid[corner] = weldId;
			}
			var par = Enumerable.Range(0, weld.Count).ToArray();
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
				par[Find(wid[triangle + 1])] = Find(wid[triangle]);
				par[Find(wid[triangle + 2])] = Find(wid[triangle]);
			}
			int W(int corner) => wid[corner];
			var pieces = new Dictionary<int, List<int>>();
			for (int triangle = 0; triangle < tris.Length; triangle += 3)
			{
				int pieceRoot = Find(W(triangle));
				if (!pieces.TryGetValue(pieceRoot, out var pieceStarts)) pieces[pieceRoot] = pieceStarts = new List<int>();
				pieceStarts.Add(triangle);
			}
			Vector3 Centre(List<int> pieceStarts)
			{
				var bounds = new Bounds(vertices[tris[pieceStarts[0]]], Vector3.zero);
				foreach (int triangle in pieceStarts) for (int j = 0; j < 3; j++) bounds.Encapsulate(vertices[tris[triangle + j]]);
				return bounds.center;
			}
			int big = pieces.Values.Max(p => p.Count);
			var rounds = pieces.Values.Where(p => p.Count >= big / 2).Select(x => new List<int>(x)).ToList();
			var cartridges = rounds.Select(p => new List<int>(p)).ToList();
			var centres = rounds.Select(Centre).ToList();
			foreach (var smallPiece in pieces.Values.Where(p => p.Count < big / 2))
			{
				var pieceCentre = Centre(smallPiece);
				int best = Enumerable.Range(0, rounds.Count).OrderBy(i => (centres[i] - pieceCentre).sqrMagnitude).First();
				rounds[best].AddRange(smallPiece);
			}
			centres = rounds.Select(Centre).ToList();
			if (rounds.Count < 3) return null;

			// The dark backs behind the belt's faces live in the other submeshes. Left behind, they're black slivers.
			var roundOfRoot = new Dictionary<int, int>();
			for (int roundIndex = 0;
				roundIndex < rounds.Count;
				roundIndex++) foreach (int triangle in rounds[roundIndex]) roundOfRoot[Find(W(triangle))] = roundIndex;
			var otherSubs = new Dictionary<int, int[]>();
			var extra = new Dictionary<(int round, int sub), List<int>>();
			for (int j = 0; j < src.subMeshCount; j++)
			{
				if (j == k) continue;
				var subTriangles = src.GetTriangles(j);
				for (int triangle = 0; triangle < subTriangles.Length; triangle += 3)
				{
					int owner = -1;
					for (int corner = 0; corner < 3; corner++)
					{
						if (!weld.TryGetValue(Vector3Int.RoundToInt(vertices[subTriangles[triangle + corner]] * 1000f), out int weldId)
							|| !roundOfRoot.TryGetValue(Find(weldId), out int pieceRound)
							|| (owner >= 0 && pieceRound != owner))
						{
							owner = -1;
							break;
						}
						owner = pieceRound;
					}
					if (owner < 0) continue;
					otherSubs[j] = subTriangles;
					if (!extra.TryGetValue((owner, j), out var faceStarts)) extra[(owner, j)] = faceStarts = new List<int>();
					faceStarts.Add(triangle);
				}
			}
			var extraSubs = otherSubs.Keys.OrderBy(j => j).ToList();

			var trunnion = cupVis.InverseTransformPoint(cradle.position);
			var bore = cupVis.InverseTransformDirection(cradle.forward);
			float ToBore(Vector3 point) => Vector3.ProjectOnPlane(point - trunnion, bore).magnitude;
			int endA = 0, endB = 1;
			for (int i = 0; i < rounds.Count; i++)
				for (int j = i + 1; j < rounds.Count; j++)
					if ((centres[i] - centres[j]).sqrMagnitude > (centres[endA] - centres[endB]).sqrMagnitude)
					{
						endA = i;
						endB = j;
					}
			int feedEnd = ToBore(centres[endA]) <= ToBore(centres[endB]) ? endA : endB;
			var order = new List<int> { feedEnd };
			while (order.Count < rounds.Count)
			{
				var last = centres[order[order.Count - 1]];
				order.Add(Enumerable.Range(0, rounds.Count)
					.Where(i => !order.Contains(i))
					.OrderBy(i => (centres[i] - last).sqrMagnitude)
					.First());
			}
			var orderedCentres = order.Select(i => centres[i]).ToArray();
			int roundCount = orderedCentres.Length;
			float pitch = 0f;
			for (int i = 1; i < roundCount; i++) pitch += (orderedCentres[i] - orderedCentres[i - 1]).magnitude;
			pitch /= roundCount - 1;

			var beltObject = new GameObject(set.BeltObject);
			beltObject.transform.SetParent(cupVis, false);
			var belt = beltObject.AddComponent<Belt>();
			belt.Set = set;
			belt.Cupola = cupVis;
			belt.Cradle = cradle;
			belt.Pitch = pitch;
			belt.CaseMesh = KordBrass.CaseMesh(
				vertices,
				normals,
				uv,
				tangents,
				tris,
				cartridges[order[0]],
				set.MeshName + " spent case",
				out string caseLog);
			belt.FeedOnCradle = cradle.InverseTransformPoint(cupVis.TransformPoint(orderedCentres[0]));
			belt.FeedDirOnCradle = cradle.InverseTransformDirection(cupVis.TransformDirection((orderedCentres[1]
				- orderedCentres[0]).normalized));
			belt.FeedUpOnCradle = cradle.InverseTransformDirection(cupVis.up);
			belt.Mouth = orderedCentres[roundCount - 1];
			belt.MouthDir = (orderedCentres[roundCount - 2] - orderedCentres[roundCount - 1]).normalized;

			var rest = belt.Curve(
				orderedCentres[0],
				(orderedCentres[1] - orderedCentres[0]).normalized,
				Vector3.up,
				orderedCentres[roundCount - 1],
				belt.MouthDir,
				Vector3.up);
			var distances = new float[roundCount];
			for (int i = 0; i < roundCount; i++) distances[i] = rest.Closest(orderedCentres[i]);
			for (int i = 1; i < roundCount; i++) if (distances[i] <= distances[i - 1]) distances[i] = distances[i - 1] + 0.001f;
			belt.FeedPitch = distances[roundCount - 1] / (roundCount - 1);

			// The belt twists, so shapes are stored per cartridge, not per place on the curve.
			var cartPos = new Vector3[roundCount];
			var cartRot = new Quaternion[roundCount];
			for (int i = 0; i < roundCount; i++)
			{
				var toF = rest.Frame(distances[i]).inverse;
				var pts = cartridges[order[i]]
					.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] })
					.Distinct()
					.Select(v => toF.MultiplyPoint3x4(vertices[v]))
					.ToList();
				var centroid = Vector3.zero;
				foreach (var point in pts) centroid += point;
				centroid /= pts.Count;
				float sumXX = 0, sumXY = 0, sumXZ = 0, sumYY = 0, sumYZ = 0, sumZZ = 0;
				foreach (var point in pts)
				{
					var offset = point - centroid;
					sumXX += offset.x * offset.x;
					sumXY += offset.x * offset.y;
					sumXZ += offset.x * offset.z;
					sumYY += offset.y * offset.y;
					sumYZ += offset.y * offset.z;
					sumZZ += offset.z * offset.z;
				}
				var axis = new Vector3(1f, 0.7f, 0.3f).normalized;
				for (int iteration = 0;
					iteration < 40;
					iteration++)
						axis = new Vector3(
							sumXX * axis.x + sumXY * axis.y + sumXZ * axis.z,
							sumXY * axis.x + sumYY * axis.y + sumYZ * axis.z,
							sumXZ * axis.x + sumYZ * axis.y + sumZZ * axis.z).normalized;
				float RadAt(bool pos)
					=> pts
						.Where(p => (Vector3.Dot(p - centroid, axis) > 0f) == pos)
						.Select(p => Vector3.ProjectOnPlane(p - centroid, axis).magnitude)
						.DefaultIfEmpty(0f)
						.Average();
				if (RadAt(true) > RadAt(false)) axis = -axis;
				var across = Vector3.ProjectOnPlane(Vector3.forward, axis);
				if (across.sqrMagnitude < 1e-6f) across = Vector3.ProjectOnPlane(Vector3.right, axis);
				cartPos[i] = centroid;
				cartRot[i] = Quaternion.LookRotation(axis, Vector3.Cross(axis, across.normalized));
			}
			belt.RestS = distances;
			belt.CartPos = cartPos;
			belt.CartRot = cartRot;

			var shadows = beltRenderer.shadowCastingMode;
			Mesh RoundMesh(int roundIndex, Matrix4x4 toLocal, string name)
			{
				var map = new Dictionary<int, int>();
				var roundVertices = new List<Vector3>();
				var roundNormals = new List<Vector3>();
				var roundUvs = new List<Vector2>();
				var roundTangents = new List<Vector4>();
				List<int> Faces(int[] indices, IEnumerable<int> starts)
				{
					var result = new List<int>();
					foreach (int triangle in starts)
						for (int j = 0; j < 3; j++)
						{
							int vertexIndex = indices[triangle + j];
							if (!map.TryGetValue(vertexIndex, out int x))
							{
								x = roundVertices.Count;
								map[vertexIndex] = x;
								roundVertices.Add(toLocal.MultiplyPoint3x4(vertices[vertexIndex]));
								roundNormals.Add(normals.Length > 0 ? toLocal.MultiplyVector(normals[vertexIndex]).normalized : Vector3.up);
								roundUvs.Add(uv.Length > 0 ? uv[vertexIndex] : Vector2.zero);
								if (tangents.Length > 0)
								{
									var tangentDirection = toLocal.MultiplyVector(tangents[vertexIndex]).normalized;
									roundTangents.Add(new Vector4(
										tangentDirection.x,
										tangentDirection.y,
										tangentDirection.z,
										tangents[vertexIndex].w));
								}
							}
							result.Add(x);
						}
					return result;
				}
				var subs = new List<List<int>> { Faces(tris, rounds[roundIndex]) };
				foreach (int j in extraSubs)
					subs.Add(Faces(otherSubs[j], extra.TryGetValue((roundIndex, j), out var faceStarts) ? faceStarts : new List<int>()));
				var mesh = new Mesh { name = name };
				mesh.SetVertices(roundVertices);
				mesh.SetNormals(roundNormals);
				mesh.SetUVs(0, roundUvs);
				if (roundTangents.Count == roundVertices.Count) mesh.SetTangents(roundTangents);
				mesh.subMeshCount = subs.Count;
				for (int subIndex = 0; subIndex < subs.Count; subIndex++) mesh.SetTriangles(subs[subIndex], subIndex);
				mesh.RecalculateBounds();
				return mesh;
			}
			var roundMats = new[] { mats[k] }.Concat(extraSubs.Select(j => mats[j])).ToArray();
			var parts = new List<(Transform t, float s)>();
			const int typical = 0;
			Mesh typicalMesh = null;
			for (int i = 0; i < roundCount + SpareRounds; i++)
			{
				float distance = i < roundCount ? distances[i] : distances[roundCount - 1] + (i - roundCount + 1) * belt.FeedPitch;
				Mesh roundMesh;
				if (i < roundCount)
					roundMesh = RoundMesh(
						order[i],
						Matrix4x4.TRS(cartPos[i], cartRot[i], Vector3.one).inverse * rest.Frame(distances[i]).inverse,
						$"{set.MeshName} belt round {i:00}");
				else
					roundMesh = typicalMesh
						?? (typicalMesh = RoundMesh(
							order[typical],
							Matrix4x4.TRS(cartPos[typical], cartRot[typical], Vector3.one).inverse * rest.Frame(distances[typical]).inverse,
							$"{set.MeshName} belt round (as {typical:00})"));
				var roundObject = new GameObject($"{set.BeltObject}_round_{i:00}");
				roundObject.transform.SetParent(beltObject.transform, false);
				roundObject.AddComponent<MeshFilter>().sharedMesh = roundMesh;
				var roundRenderer = roundObject.AddComponent<MeshRenderer>();
				roundRenderer.sharedMaterials = roundMats;
				roundRenderer.shadowCastingMode = shadows;
				parts.Add((roundObject.transform, distance));
			}
			belt.Rounds = parts.ToArray();
			belt.TypicalMesh = typicalMesh;

			var copy = Object.Instantiate(src);
			copy.name = src.name + " (no belt)";
			copy.SetTriangles(new int[0], k);
			var moved = new List<string>();
			foreach (int j in extraSubs)
			{
				var claimed = new HashSet<int>(extra.Where(e => e.Key.sub == j).SelectMany(x => x.Value));
				var indices = otherSubs[j];
				var keep = new List<int>(indices.Length);
				for (int triangle = 0; triangle < indices.Length; triangle += 3)
					if (!claimed.Contains(triangle))
					{
						keep.Add(indices[triangle]);
						keep.Add(indices[triangle + 1]);
						keep.Add(indices[triangle + 2]);
					}
				copy.SetTriangles(keep, j);
				moved.Add($"{claimed.Count} of {indices.Length / 3} '{(mats[j] != null ? mats[j].name : "?")}'");
			}
			meshFilter.sharedMesh = copy;

			belt.Place();
			float dev = 0f;
			var per = new List<string>();
			var root = cupVis.root;
			for (int i = 0; i < roundCount; i++)
			{
				var localVertices = belt.Rounds[i].t.GetComponent<MeshFilter>().sharedMesh.vertices;
				var orig = rounds[order[i]]
					.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] })
					.Distinct()
					.Select(v => cupVis.TransformPoint(vertices[v]))
					.ToList();
				var now = localVertices.Select(p => belt.Rounds[i].t.TransformPoint(p)).ToList();
				float deviation = 0f;
				for (int j = 0; j < now.Count && j < orig.Count; j++) deviation = Mathf.Max(deviation, (now[j] - orig[j]).magnitude);
				dev = Mathf.Max(dev, deviation);
				var prefabPoint = root.InverseTransformPoint(cupVis.TransformPoint(orderedCentres[i]));
				per.Add($"{i}: ({prefabPoint.x:0.000}, {prefabPoint.y:0.000}, {prefabPoint.z:0.000}) s {distances[i] * 100f:0.0} cm, {rounds[order[i]].Count} tris, off {deviation * 1000f:0.0} mm, shown {belt.Rounds[i].t.GetComponent<Renderer>().enabled}");
			}
			return belt;
		}

		internal static Belt Build(Transform carrier, Transform cradle, Settings set, Vector3 feed, Vector3 feedDir, Vector3 mouth, Vector3 mouthDir, Vector3 cartridgeAxis, float pitch,
			BeltRound.Data round = null, Material roundMat = null)
		{
			var beltObject = new GameObject(set.BeltObject);
			beltObject.transform.SetParent(carrier, false);
			beltObject.layer = carrier.gameObject.layer;
			var belt = beltObject.AddComponent<Belt>();
			belt.Set = set;
			belt.Cupola = carrier;
			belt.Cradle = cradle;
			belt.Pitch = pitch;
			belt.FeedPitch = pitch;
			feedDir = feedDir.normalized;
			mouthDir = mouthDir.normalized;
			belt.FeedOnCradle = cradle.InverseTransformPoint(carrier.TransformPoint(feed));
			belt.FeedDirOnCradle = cradle.InverseTransformDirection(carrier.TransformDirection(feedDir));
			belt.FeedUpOnCradle = cradle.InverseTransformDirection(carrier.up);
			belt.Mouth = mouth;
			belt.MouthDir = mouthDir;
			var rest = belt.Curve(feed, feedDir, Vector3.up, mouth, mouthDir, Vector3.up);
			var rot = BeltRound.CartridgeRot(rest, cartridgeAxis);
			belt.RestS = new[] { 0f, Mathf.Max(0.01f, rest.Length) };
			belt.CartPos = new[] { Vector3.zero, Vector3.zero };
			belt.CartRot = new[] { rot, rot };
			Mesh mesh;
			Material[] mats;
			if (round != null && roundMat != null)
			{
				mesh = BeltRound.RoundMesh(round, set.MeshName + " belt round (modelled)");
				var (cartridgeVertices, cartridgeNormals, cuv, ctg, ctris, cstarts) = BeltRound.CartridgeArrays(mesh, round);
				belt.CaseMesh = KordBrass.CaseMesh(
					cartridgeVertices,
					cartridgeNormals,
					cuv,
					ctg,
					ctris,
					cstarts,
					set.MeshName + " spent case",
					out string caseLog);
				mats = new[] { roundMat };
			}
			else
			{
				mesh = Round127(set.MeshName + " belt round", pitch);
				belt.CaseMesh = KordBrass.Case127(set.MeshName + " spent case");
				mats = new[]
				{
					KordBrass.Brass(set.MeshName + " case brass"),
					Metal(set.MeshName + " bullet jacket", new Color(0.55f, 0.36f, 0.22f), 0.8f, 0.5f),
					Metal(set.MeshName + " link steel", new Color(0.13f, 0.13f, 0.12f), 0.6f, 0.3f)
				};
			}
			belt.TypicalMesh = mesh;
			int roundCount = Mathf.CeilToInt(rest.Length / pitch) + 1 + set.SpareRounds;
			var parts = new List<(Transform t, float s)>();
			for (int i = 0; i < roundCount; i++)
			{
				var roundObject = new GameObject($"{set.BeltObject}_round_{i:00}");
				roundObject.layer = beltObject.layer;
				roundObject.transform.SetParent(beltObject.transform, false);
				roundObject.AddComponent<MeshFilter>().sharedMesh = mesh;
				roundObject.AddComponent<MeshRenderer>().sharedMaterials = mats;
				parts.Add((roundObject.transform, i * pitch));
			}
			belt.Rounds = parts.ToArray();
			belt.Place();
			return belt;
		}

		static Material Metal(string name, Color c, float metallic, float gloss)
		{
			var shader = Shader.Find("Standard (FLIR)") ?? Shader.Find("Standard");
			var mat = new Material(shader) { name = name, color = c };
			if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
			if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", gloss);
			return mat;
		}

		internal static Mesh Round127(string name, float pitch)
		{
			const float linkZ = 0.030f;
			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var subs = new[] { new List<int>(), new List<int>(), new List<int>() };
			void Lathe((float z, float r)[] prof, int sub, int sides)
			{
				for (int k = 0; k + 1 < prof.Length; k++)
				{
					var (startZ, startRadius) = prof[k];
					var (endZ, endRadius) = prof[k + 1];
					int start = vertices.Count;
					for (int side = 0; side <= sides; side++)
					{
						float angle = side * Mathf.PI * 2f / sides;
						var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
						var nrm = startRadius == 0f && endRadius > 0f && startZ == endZ
							? Vector3.back
							: (dir * (endZ - startZ) + Vector3.forward * (startRadius - endRadius)).normalized;
						vertices.Add(dir * startRadius + Vector3.forward * (startZ - linkZ));
						normals.Add(nrm);
						vertices.Add(dir * endRadius + Vector3.forward * (endZ - linkZ));
						normals.Add(nrm);
					}
					for (int side = 0; side < sides; side++)
					{
						int i = start + side * 2;
						subs[sub].AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
					}
				}
			}
			void Box(Vector3 min, Vector3 max, int sub)
			{
				var faces = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
				var mid = (min + max) * 0.5f;
				var half = (max - min) * 0.5f;
				foreach (var face in faces)
				{
					var tangent = face.x != 0f ? Vector3.up : Vector3.right;
					var bitangent = Vector3.Cross(face, tangent);
					int i = vertices.Count;
					foreach (var (cornerU, cornerV) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
					{
						var corner = face + tangent * cornerU + bitangent * cornerV;
						vertices.Add(mid + Vector3.Scale(corner, half) - Vector3.forward * 0f);
						normals.Add(face);
					}
					subs[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
				}
			}
			Lathe(new[] { (0f, 0f), (0f, 0.010875f), (0.080f, 0.0099f), (0.086f, 0.0065f), (0.108f, 0.0065f) }, 0, 12);
			Lathe(new[] { (0.108f, 0.0065f), (0.118f, 0.0065f), (0.134f, 0.0045f), (0.147f, 0.0008f), (0.147f, 0f) }, 1, 12);
			Lathe(new[] { (0.021f, 0.0118f), (0.039f, 0.0118f), (0.039f, 0.0109f), (0.021f, 0.0109f), (0.021f, 0.0118f) }, 2, 12);
			Box(
				new Vector3(0f, -0.0125f, 0.024f - linkZ) + new Vector3(-0.002f, 0f, 0f),
				new Vector3(pitch + 0.002f, -0.0110f, 0.036f - linkZ),
				2);
			var mesh = new Mesh { name = name };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.subMeshCount = 3;
			for (int subIndex = 0; subIndex < 3; subIndex++) mesh.SetTriangles(subs[subIndex], subIndex);
			mesh.RecalculateBounds();
			return mesh;
		}

		[DefaultExecutionOrder(20)]
		internal class Belt : MonoBehaviour
		{
			// Half is a guess, the source only gives the order of the motions.
			const float FeedPart = 0.5f;

			internal Settings Set;
			internal Transform Cupola, Cradle;
			internal Vector3 FeedOnCradle, FeedDirOnCradle, FeedUpOnCradle;
			internal Vector3 Mouth, MouthDir;
			internal float Pitch;
			internal (Transform t, float s)[] Rounds;
			internal float[] RestS;
			internal Vector3[] CartPos;
			internal Quaternion[] CartRot;

			(Vector3 p, Quaternion r) Cartridge(float s)
			{
				int count = RestS.Length;
				if (s <= RestS[0]) return (CartPos[0], CartRot[0]);
				if (s >= RestS[count - 1]) return (CartPos[count - 1], CartRot[count - 1]);
				int i = 0;
				while (i < count - 2 && RestS[i + 1] < s) i++;
				float fraction = (s - RestS[i]) / Mathf.Max(1e-6f, RestS[i + 1] - RestS[i]);
				return (Vector3.Lerp(CartPos[i], CartPos[i + 1], fraction), Quaternion.Slerp(CartRot[i], CartRot[i + 1], fraction));
			}

			internal Mesh CaseMesh;
			internal Mesh TypicalMesh;
			internal float Length { get; private set; }

			internal int Visible { get; private set; }

			internal int Feeds { get; private set; }

			internal float FeedPitch;
			float Drop => Mathf.Max(0.02f, FeedOnCradle.y);
			Renderer[] _renderers;
			WeaponSystem _weapon;
			AmmoFeed _feed;
			float _cycle, _fed, _target, _fedSince;
			int _head;

			internal BeltCurve Curve(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp) =>
				new BeltCurve(feed, feedDir, feedUp, mouth, mouthDir, mouthUp);

			internal void Attach(WeaponSystem weapon, float cycle)
			{
				_weapon = weapon;
				_feed = weapon.Feed;
				_cycle = cycle;
				_weapon.Fired += OnFired;
			}

			void OnDestroy() { if (_weapon != null) _weapon.Fired -= OnFired; }

			void OnFired(AmmoType ammo, LiveRound round)
			{
				_cycle = Cycle(_weapon, _cycle);
				_target += FeedPitch;
				Feeds++;
			}

			internal static float Cycle(WeaponSystem w, float fallback)
			{
				float cycle = w != null && RemoteMg.WeaponCycle != null ? (float)RemoteMg.WeaponCycle.GetValue(w) : 0f;
				return cycle > 0f ? cycle : fallback;
			}

			void LateUpdate() => Tick();

			void Tick()
			{
				if (_target > _fed)
				{
					float was = _fed;
					_fed = Mathf.MoveTowards(_fed, _target, FeedPitch / Mathf.Max(0.01f, FeedPart * _cycle) * Time.deltaTime);
					_fedSince = Mathf.Min(1f, _fedSince + _fed - was);
					while (_fed - _head * FeedPitch >= FeedPitch - 1e-5f)
					{
						var meshFilter = Rounds[_head % Rounds.Length].t.GetComponent<MeshFilter>();
						if (TypicalMesh != null && meshFilter.sharedMesh != TypicalMesh) meshFilter.sharedMesh = TypicalMesh;
						_head++;
					}
					if (_head >= Rounds.Length)
					{
						_head -= Rounds.Length;
						_fed -= Rounds.Length * FeedPitch;
						_target -= Rounds.Length * FeedPitch;
					}
				}
				Place();
			}

			internal void Place()
			{
				if (Cupola == null || Cradle == null || Rounds == null) return;
				var feed = Cupola.InverseTransformPoint(Cradle.TransformPoint(FeedOnCradle));
				var feedDir = Cupola.InverseTransformDirection(Cradle.TransformDirection(FeedDirOnCradle));
				var feedUp = Cupola.InverseTransformDirection(Cradle.TransformDirection(FeedUpOnCradle));
				if (_renderers == null) _renderers = Rounds.Select(r => r.t.GetComponent<Renderer>()).ToArray();
				int left = _feed != null ? _feed.CurrentClipRemainingCount - (_feed.AmmoTypeInBreech != null ? 1 : 0) : int.MaxValue;
				if (_placed
					&& _fed == _lastFed
					&& _head == _lastHead
					&& left == _lastLeft
					&& feed == _lastFeed
					&& feedDir == _lastFeedDir
					&& feedUp == _lastFeedUp)
					return;
				if (_placed && _target <= _fed && !Seen()) return;
				_placed = true;
				_lastFed = _fed;
				_lastHead = _head;
				_lastLeft = left;
				_lastFeed = feed;
				_lastFeedDir = feedDir;
				_lastFeedUp = feedUp;
				var curve = _curve ?? (_curve = new BeltCurve(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up));
				curve.Set(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up);
				Length = curve.Length;
				int shown = 0;
				int nAll = Rounds.Length;
				float frac = _fed - _head * FeedPitch;
				float fade = Mathf.Clamp01(1f - _fedSince / (2f * FeedPitch));
				for (int i = 0; i < nAll; i++)
				{
					var (roundTransform, restDistance) = Rounds[i];
					int place = ((i - _head) % nAll + nAll) % nAll;
					float distance = place * FeedPitch - frac + (restDistance - i * FeedPitch) * fade;
					// Renderer, not the object: the burn effect and heat source collect active renderers once, at Start.
					bool show = distance <= curve.Length + 0.3f * Pitch && distance > -0.95f * FeedPitch && place < left;
					if (_renderers[i].enabled != show) _renderers[i].enabled = show;
					if (!show) continue;
					shown++;
					var frame = curve.Frame(Mathf.Max(0f, distance));
					var (cartridgePoint, cartridgeRotation) = Cartridge(Mathf.Max(0f, distance));
					var position = frame.MultiplyPoint3x4(cartridgePoint);
					if (distance < 0f) position -= feedUp.normalized * (Mathf.Clamp01(-distance / FeedPitch) * Drop);
					roundTransform.localPosition = position;
					roundTransform.localRotation = frame.rotation * cartridgeRotation;
				}
				Visible = shown;
			}

			BeltCurve _curve;
			bool _placed;
			float _lastFed;
			int _lastHead, _lastLeft;
			Vector3 _lastFeed, _lastFeedDir, _lastFeedUp;

			bool Seen()
			{
				foreach (var roundRenderer in _renderers)
					if (roundRenderer != null && roundRenderer.enabled && roundRenderer.isVisible) return true;
				return false;
			}
		}
	}
}
