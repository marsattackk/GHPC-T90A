using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A
{
	internal static class KordBelt
	{
		internal const string BeltMaterial = "t90a_mg_6p49_kord_ammo";
		const int SpareRounds = 24;

		internal static void Fit(Transform cupVis, Transform cradle)
		{
			var mr = cupVis.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith(BeltMaterial)));
			var mf = mr?.GetComponent<MeshFilter>();
			if (mf == null || mf.sharedMesh == null) return;
			if (!mf.sharedMesh.isReadable) return;
			var mats = mr.sharedMaterials;
			int k = System.Array.FindIndex(mats, m => m != null && m.name.StartsWith(BeltMaterial));
			var src = mf.sharedMesh;
			var v = src.vertices; var n = src.normals; var uv = src.uv; var tg = src.tangents;
			var tris = src.GetTriangles(k);

			var weld = new Dictionary<Vector3Int, int>();
			var wid = new int[tris.Length];
			for (int t = 0; t < tris.Length; t++)
			{
				var key = Vector3Int.RoundToInt(v[tris[t]] * 1000f);
				if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
				wid[t] = id;
			}
			var par = Enumerable.Range(0, weld.Count).ToArray();
			int Find(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
			for (int t = 0; t < tris.Length; t += 3) { par[Find(wid[t + 1])] = Find(wid[t]); par[Find(wid[t + 2])] = Find(wid[t]); }
			int W(int t) => wid[t];
			var pieces = new Dictionary<int, List<int>>();
			for (int t = 0; t < tris.Length; t += 3) { int r = Find(W(t)); if (!pieces.TryGetValue(r, out var l)) pieces[r] = l = new List<int>(); l.Add(t); }
			Vector3 Centre(List<int> ts) { var b = new Bounds(v[tris[ts[0]]], Vector3.zero); foreach (int t in ts) for (int j = 0; j < 3; j++) b.Encapsulate(v[tris[t + j]]); return b.center; }
			int big = pieces.Values.Max(p => p.Count);
			var rounds = pieces.Values.Where(p => p.Count >= big / 2).Select(p => new List<int>(p)).ToList();
			var centres = rounds.Select(Centre).ToList();
			foreach (var p in pieces.Values.Where(p => p.Count < big / 2))
			{
				var c = Centre(p);
				int best = Enumerable.Range(0, rounds.Count).OrderBy(i => (centres[i] - c).sqrMagnitude).First();
				rounds[best].AddRange(p);
			}
			centres = rounds.Select(Centre).ToList();
			if (rounds.Count < 3) return;

			var trunnion = cupVis.InverseTransformPoint(cradle.position);
			var bore = cupVis.InverseTransformDirection(cradle.forward);
			float ToBore(Vector3 p) => Vector3.ProjectOnPlane(p - trunnion, bore).magnitude;
			int e0 = 0, e1 = 1;
			for (int i = 0; i < rounds.Count; i++)
				for (int j = i + 1; j < rounds.Count; j++)
					if ((centres[i] - centres[j]).sqrMagnitude > (centres[e0] - centres[e1]).sqrMagnitude) { e0 = i; e1 = j; }
			int feedEnd = ToBore(centres[e0]) <= ToBore(centres[e1]) ? e0 : e1;
			var order = new List<int> { feedEnd };
			while (order.Count < rounds.Count)
			{
				var last = centres[order[order.Count - 1]];
				order.Add(Enumerable.Range(0, rounds.Count).Where(i => !order.Contains(i)).OrderBy(i => (centres[i] - last).sqrMagnitude).First());
			}
			var c0 = order.Select(i => centres[i]).ToArray();
			int nr = c0.Length;
			float pitch = 0f; for (int i = 1; i < nr; i++) pitch += (c0[i] - c0[i - 1]).magnitude; pitch /= nr - 1;

			var go = new GameObject("T90A_KORD_BELT");
			go.transform.SetParent(cupVis, false);
			var belt = go.AddComponent<Belt>();
			belt.Cupola = cupVis; belt.Cradle = cradle; belt.Pitch = pitch;
			belt.FeedOnCradle = cradle.InverseTransformPoint(cupVis.TransformPoint(c0[0]));
			belt.FeedDirOnCradle = cradle.InverseTransformDirection(cupVis.TransformDirection((c0[1] - c0[0]).normalized));
			belt.FeedUpOnCradle = cradle.InverseTransformDirection(cupVis.up);
			belt.Mouth = c0[nr - 1];
			belt.MouthDir = (c0[nr - 2] - c0[nr - 1]).normalized;

			var rest = belt.Curve(c0[0], (c0[1] - c0[0]).normalized, Vector3.up, c0[nr - 1], belt.MouthDir, Vector3.up);
			var s = new float[nr];
			for (int i = 0; i < nr; i++) s[i] = rest.Closest(c0[i]);
			for (int i = 1; i < nr; i++) if (s[i] <= s[i - 1]) s[i] = s[i - 1] + 0.001f;

			var shadows = mr.shadowCastingMode;
			Mesh RoundMesh(List<int> ts, Matrix4x4 toLocal, string name)
			{
				var map = new Dictionary<int, int>();
				var vv = new List<Vector3>(); var nn = new List<Vector3>(); var uu = new List<Vector2>(); var tt = new List<Vector4>(); var ii = new List<int>();
				foreach (int t in ts)
					for (int j = 0; j < 3; j++)
					{
						int o = tris[t + j];
						if (!map.TryGetValue(o, out int x))
						{
							x = vv.Count; map[o] = x;
							vv.Add(toLocal.MultiplyPoint3x4(v[o]));
							nn.Add(n.Length > 0 ? toLocal.MultiplyVector(n[o]).normalized : Vector3.up);
							uu.Add(uv.Length > 0 ? uv[o] : Vector2.zero);
							if (tg.Length > 0) { var d = toLocal.MultiplyVector(tg[o]).normalized; tt.Add(new Vector4(d.x, d.y, d.z, tg[o].w)); }
						}
						ii.Add(x);
					}
				var m = new Mesh { name = name };
				m.SetVertices(vv); m.SetNormals(nn); m.SetUVs(0, uu); if (tt.Count == vv.Count) m.SetTangents(tt);
				m.SetTriangles(ii, 0); m.RecalculateBounds();
				return m;
			}
			var parts = new List<(Transform t, float s)>();
			Mesh lastMesh = null;
			for (int i = 0; i < nr + SpareRounds; i++)
			{
				float si = i < nr ? s[i] : s[nr - 1] + (i - nr + 1) * pitch;
				Mesh m;
				if (i < nr) { m = RoundMesh(rounds[order[i]], rest.Frame(s[i]).inverse, $"T90A Kord belt round {i:00}"); if (i == nr - 1) lastMesh = m; }
				else m = lastMesh;
				var r = new GameObject($"T90A_KORD_BELT_round_{i:00}");
				r.transform.SetParent(go.transform, false);
				r.AddComponent<MeshFilter>().sharedMesh = m;
				var rr = r.AddComponent<MeshRenderer>();
				rr.sharedMaterial = mats[k];
				rr.shadowCastingMode = shadows;
				parts.Add((r.transform, si));
			}
			belt.Rounds = parts.ToArray();

			var copy = Object.Instantiate(src);
			copy.name = src.name + " (no belt)";
			copy.SetTriangles(new int[0], k);
			mf.sharedMesh = copy;

			belt.Place();
		}

		internal sealed class Curve
		{
			const int N = 48;
			readonly Vector3[] _p = new Vector3[N + 1];
			readonly float[] _s = new float[N + 1];
			readonly Vector3 _upA, _upB;
			internal float Length => _s[N];

			internal Curve(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp)
			{
				float h = (mouth - feed).magnitude * 0.4f;
				Vector3 p1 = feed + feedDir * h, p2 = mouth + mouthDir * h;
				for (int i = 0; i <= N; i++)
				{
					float t = i / (float)N, u = 1f - t;
					_p[i] = u * u * u * feed + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * mouth;
					_s[i] = i == 0 ? 0f : _s[i - 1] + (_p[i] - _p[i - 1]).magnitude;
				}
				_upA = feedUp; _upB = mouthUp;
			}

			int Seg(float s) { int i = 0; while (i < N - 1 && _s[i + 1] < s) i++; return i; }

			internal Vector3 Point(float s)
			{
				if (s <= 0f) return _p[0] + (_p[1] - _p[0]).normalized * s;
				if (s >= Length) return _p[N] + (_p[N] - _p[N - 1]).normalized * (s - Length);
				int i = Seg(s);
				return Vector3.Lerp(_p[i], _p[i + 1], (s - _s[i]) / Mathf.Max(1e-6f, _s[i + 1] - _s[i]));
			}

			internal Matrix4x4 Frame(float s)
			{
				int i = Mathf.Clamp(Seg(Mathf.Clamp(s, 0f, Length)), 0, N - 1);
				var tan = (_p[i + 1] - _p[i]).normalized;
				var up = Vector3.Slerp(_upA, _upB, Mathf.Clamp01(s / Mathf.Max(1e-6f, Length)));
				if (Mathf.Abs(Vector3.Dot(up, tan)) > 0.995f) up = Vector3.Cross(tan, Vector3.right);
				return Matrix4x4.TRS(Point(s), Quaternion.LookRotation(tan, up), Vector3.one);
			}

			internal float Closest(Vector3 p)
			{
				int best = 0;
				for (int i = 1; i <= N; i++) if ((_p[i] - p).sqrMagnitude < (_p[best] - p).sqrMagnitude) best = i;
				float bs = _s[best], bd = (_p[best] - p).sqrMagnitude;
				for (int i = Mathf.Max(0, best - 1); i < Mathf.Min(N, best + 1); i++)
					for (int j = 0; j <= 20; j++)
					{
						var q = Vector3.Lerp(_p[i], _p[i + 1], j / 20f);
						if ((q - p).sqrMagnitude < bd) { bd = (q - p).sqrMagnitude; bs = Mathf.Lerp(_s[i], _s[i + 1], j / 20f); }
					}
				return bs;
			}
		}

		[DefaultExecutionOrder(20)]
		internal class Belt : MonoBehaviour
		{
			internal Transform Cupola, Cradle;
			internal Vector3 FeedOnCradle, FeedDirOnCradle, FeedUpOnCradle;
			internal Vector3 Mouth, MouthDir;
			internal float Pitch;
			internal (Transform t, float s)[] Rounds;
			Renderer[] _renderers;

			internal Curve Curve(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp) =>
				new Curve(feed, feedDir, feedUp, mouth, mouthDir, mouthUp);

			void LateUpdate() => Place();

			internal void Place()
			{
				if (Cupola == null || Cradle == null || Rounds == null) return;
				var feed = Cupola.InverseTransformPoint(Cradle.TransformPoint(FeedOnCradle));
				var feedDir = Cupola.InverseTransformDirection(Cradle.TransformDirection(FeedDirOnCradle));
				var feedUp = Cupola.InverseTransformDirection(Cradle.TransformDirection(FeedUpOnCradle));
				var c = new Curve(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up);
				if (_renderers == null) _renderers = Rounds.Select(r => r.t.GetComponent<Renderer>()).ToArray();
				for (int i = 0; i < Rounds.Length; i++)
				{
					var (t, s) = Rounds[i];
					bool show = s <= c.Length + 0.3f * Pitch;
					if (_renderers[i].enabled != show) _renderers[i].enabled = show;
					if (!show) continue;
					var f = c.Frame(s);
					t.localPosition = f.GetColumn(3);
					t.localRotation = f.rotation;
				}
			}
		}
	}
}
