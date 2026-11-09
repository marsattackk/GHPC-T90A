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
			var mr = cupVis.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith(BeltMaterial)));
			var mf = mr?.GetComponent<MeshFilter>();
			if (mf == null || mf.sharedMesh == null) return null;
			if (!mf.sharedMesh.isReadable) return null;
			var mats = mr.sharedMaterials;
			int k = System.Array.FindIndex(mats, m => m != null && m.name.StartsWith(BeltMaterial));
			var src = mf.sharedMesh;
			var v = src.vertices; var n = src.normals; var uv = src.uv; var tg = src.tangents;
			if (mr.transform != cupVis)
			{
				var m2 = cupVis.worldToLocalMatrix * mr.transform.localToWorldMatrix;
				for (int i = 0; i < v.Length; i++) v[i] = m2.MultiplyPoint3x4(v[i]);
				for (int i = 0; i < n.Length; i++) n[i] = m2.MultiplyVector(n[i]).normalized;
				for (int i = 0; i < tg.Length; i++) { var d = m2.MultiplyVector(tg[i]).normalized; tg[i] = new Vector4(d.x, d.y, d.z, tg[i].w); }
			}
			var tris = src.GetTriangles(k);

			var weld = new Dictionary<Vector3Int, int>(Hashing.Vec3Int);
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
			var cartridges = rounds.Select(p => new List<int>(p)).ToList();
			var centres = rounds.Select(Centre).ToList();
			foreach (var p in pieces.Values.Where(p => p.Count < big / 2))
			{
				var c = Centre(p);
				int best = Enumerable.Range(0, rounds.Count).OrderBy(i => (centres[i] - c).sqrMagnitude).First();
				rounds[best].AddRange(p);
			}
			centres = rounds.Select(Centre).ToList();
			if (rounds.Count < 3) return null;

			// The dark backs behind the belt's faces live in the other submeshes. Left behind, they're black slivers.
			var roundOfRoot = new Dictionary<int, int>();
			for (int r = 0; r < rounds.Count; r++) foreach (int t in rounds[r]) roundOfRoot[Find(W(t))] = r;
			var otherSubs = new Dictionary<int, int[]>();
			var extra = new Dictionary<(int round, int sub), List<int>>();
			for (int j = 0; j < src.subMeshCount; j++)
			{
				if (j == k) continue;
				var tj = src.GetTriangles(j);
				for (int t = 0; t < tj.Length; t += 3)
				{
					int r = -1;
					for (int c = 0; c < 3; c++)
					{
						if (!weld.TryGetValue(Vector3Int.RoundToInt(v[tj[t + c]] * 1000f), out int id) || !roundOfRoot.TryGetValue(Find(id), out int rc) || (r >= 0 && rc != r)) { r = -1; break; }
						r = rc;
					}
					if (r < 0) continue;
					otherSubs[j] = tj;
					if (!extra.TryGetValue((r, j), out var l)) extra[(r, j)] = l = new List<int>();
					l.Add(t);
				}
			}
			var extraSubs = otherSubs.Keys.OrderBy(j => j).ToList();

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

			var go = new GameObject(set.BeltObject);
			go.transform.SetParent(cupVis, false);
			var belt = go.AddComponent<Belt>();
			belt.Set = set;
			belt.Cupola = cupVis; belt.Cradle = cradle; belt.Pitch = pitch;
			belt.CaseMesh = KordBrass.CaseMesh(v, n, uv, tg, tris, cartridges[order[0]], set.MeshName + " spent case");
			belt.FeedOnCradle = cradle.InverseTransformPoint(cupVis.TransformPoint(c0[0]));
			belt.FeedDirOnCradle = cradle.InverseTransformDirection(cupVis.TransformDirection((c0[1] - c0[0]).normalized));
			belt.FeedUpOnCradle = cradle.InverseTransformDirection(cupVis.up);
			belt.Mouth = c0[nr - 1];
			belt.MouthDir = (c0[nr - 2] - c0[nr - 1]).normalized;

			var rest = belt.Curve(c0[0], (c0[1] - c0[0]).normalized, Vector3.up, c0[nr - 1], belt.MouthDir, Vector3.up);
			var s = new float[nr];
			for (int i = 0; i < nr; i++) s[i] = rest.Closest(c0[i]);
			for (int i = 1; i < nr; i++) if (s[i] <= s[i - 1]) s[i] = s[i - 1] + 0.001f;
			belt.FeedPitch = s[nr - 1] / (nr - 1);

			// The belt twists, so shapes are stored per cartridge, not per place on the curve.
			var cartPos = new Vector3[nr]; var cartRot = new Quaternion[nr];
			for (int i = 0; i < nr; i++)
			{
				var toF = rest.Frame(s[i]).inverse;
				var pts = cartridges[order[i]].SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] }).Distinct().Select(o => toF.MultiplyPoint3x4(v[o])).ToList();
				var cc = Vector3.zero; foreach (var p in pts) cc += p; cc /= pts.Count;
				float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
				foreach (var p in pts) { var d = p - cc; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
				var ax = new Vector3(1f, 0.7f, 0.3f).normalized;
				for (int k2 = 0; k2 < 40; k2++) ax = new Vector3(xx * ax.x + xy * ax.y + xz * ax.z, xy * ax.x + yy * ax.y + yz * ax.z, xz * ax.x + yz * ax.y + zz * ax.z).normalized;
				float RadAt(bool pos) => pts.Where(p => (Vector3.Dot(p - cc, ax) > 0f) == pos).Select(p => Vector3.ProjectOnPlane(p - cc, ax).magnitude).DefaultIfEmpty(0f).Average();
				if (RadAt(true) > RadAt(false)) ax = -ax;
				var across = Vector3.ProjectOnPlane(Vector3.forward, ax);
				if (across.sqrMagnitude < 1e-6f) across = Vector3.ProjectOnPlane(Vector3.right, ax);
				cartPos[i] = cc;
				cartRot[i] = Quaternion.LookRotation(ax, Vector3.Cross(ax, across.normalized));
			}
			belt.RestS = s; belt.CartPos = cartPos; belt.CartRot = cartRot;

			var shadows = mr.shadowCastingMode;
			Mesh RoundMesh(int r, Matrix4x4 toLocal, string name)
			{
				var map = new Dictionary<int, int>();
				var vv = new List<Vector3>(); var nn = new List<Vector3>(); var uu = new List<Vector2>(); var tt = new List<Vector4>();
				List<int> Faces(int[] arr, IEnumerable<int> starts)
				{
					var ii = new List<int>();
					foreach (int t in starts)
						for (int j = 0; j < 3; j++)
						{
							int o = arr[t + j];
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
					return ii;
				}
				var subs = new List<List<int>> { Faces(tris, rounds[r]) };
				foreach (int j in extraSubs) subs.Add(Faces(otherSubs[j], extra.TryGetValue((r, j), out var l) ? l : new List<int>()));
				var m = new Mesh { name = name };
				m.SetVertices(vv); m.SetNormals(nn); m.SetUVs(0, uu); if (tt.Count == vv.Count) m.SetTangents(tt);
				m.subMeshCount = subs.Count;
				for (int s2 = 0; s2 < subs.Count; s2++) m.SetTriangles(subs[s2], s2);
				m.RecalculateBounds();
				return m;
			}
			var roundMats = new[] { mats[k] }.Concat(extraSubs.Select(j => mats[j])).ToArray();
			var parts = new List<(Transform t, float s)>();
			const int typical = 0;
			Mesh typicalMesh = null;
			for (int i = 0; i < nr + SpareRounds; i++)
			{
				float si = i < nr ? s[i] : s[nr - 1] + (i - nr + 1) * belt.FeedPitch;
				Mesh m;
				if (i < nr) m = RoundMesh(order[i], Matrix4x4.TRS(cartPos[i], cartRot[i], Vector3.one).inverse * rest.Frame(s[i]).inverse, $"{set.MeshName} belt round {i:00}");
				else m = typicalMesh ?? (typicalMesh = RoundMesh(order[typical], Matrix4x4.TRS(cartPos[typical], cartRot[typical], Vector3.one).inverse * rest.Frame(s[typical]).inverse, $"{set.MeshName} belt round (as {typical:00})"));
				var r = new GameObject($"{set.BeltObject}_round_{i:00}");
				r.transform.SetParent(go.transform, false);
				r.AddComponent<MeshFilter>().sharedMesh = m;
				var rr = r.AddComponent<MeshRenderer>();
				rr.sharedMaterials = roundMats;
				rr.shadowCastingMode = shadows;
				parts.Add((r.transform, si));
			}
			belt.Rounds = parts.ToArray();
			belt.TypicalMesh = typicalMesh;

			var copy = Object.Instantiate(src);
			copy.name = src.name + " (no belt)";
			copy.SetTriangles(new int[0], k);
			foreach (int j in extraSubs)
			{
				var claimed = new HashSet<int>(extra.Where(e => e.Key.sub == j).SelectMany(e => e.Value));
				var arr = otherSubs[j];
				var keep = new List<int>(arr.Length);
				for (int t = 0; t < arr.Length; t += 3) if (!claimed.Contains(t)) { keep.Add(arr[t]); keep.Add(arr[t + 1]); keep.Add(arr[t + 2]); }
				copy.SetTriangles(keep, j);
			}
			mf.sharedMesh = copy;

			belt.Place();
			return belt;
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
				int n = RestS.Length;
				if (s <= RestS[0]) return (CartPos[0], CartRot[0]);
				if (s >= RestS[n - 1]) return (CartPos[n - 1], CartRot[n - 1]);
				int i = 0;
				while (i < n - 2 && RestS[i + 1] < s) i++;
				float u = (s - RestS[i]) / Mathf.Max(1e-6f, RestS[i + 1] - RestS[i]);
				return (Vector3.Lerp(CartPos[i], CartPos[i + 1], u), Quaternion.Slerp(CartRot[i], CartRot[i + 1], u));
			}

			internal Mesh CaseMesh;
			internal Mesh TypicalMesh;
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
				_weapon = weapon; _feed = weapon.Feed; _cycle = cycle;
				_weapon.Fired += OnFired;
			}

			void OnDestroy() { if (_weapon != null) _weapon.Fired -= OnFired; }

			void OnFired(AmmoType ammo, LiveRound round) { _cycle = Cycle(_weapon, _cycle); _target += FeedPitch; }

			internal static float Cycle(WeaponSystem w, float fallback)
			{
				float c = w != null && RemoteMg.WeaponCycle != null ? (float)RemoteMg.WeaponCycle.GetValue(w) : 0f;
				return c > 0f ? c : fallback;
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
						var mf = Rounds[_head % Rounds.Length].t.GetComponent<MeshFilter>();
						if (TypicalMesh != null && mf.sharedMesh != TypicalMesh) mf.sharedMesh = TypicalMesh;
						_head++;
					}
					if (_head >= Rounds.Length) { _head -= Rounds.Length; _fed -= Rounds.Length * FeedPitch; _target -= Rounds.Length * FeedPitch; }
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
				if (_placed && _fed == _lastFed && _head == _lastHead && left == _lastLeft && feed == _lastFeed && feedDir == _lastFeedDir && feedUp == _lastFeedUp) return;
				if (_placed && _target <= _fed && !Seen()) return;
				_placed = true; _lastFed = _fed; _lastHead = _head; _lastLeft = left; _lastFeed = feed; _lastFeedDir = feedDir; _lastFeedUp = feedUp;
				var c = _curve ?? (_curve = new BeltCurve(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up));
				c.Set(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up);
				int nAll = Rounds.Length;
				float frac = _fed - _head * FeedPitch;
				float fade = Mathf.Clamp01(1f - _fedSince / (2f * FeedPitch));
				for (int i = 0; i < nAll; i++)
				{
					var (t, s0) = Rounds[i];
					int q = ((i - _head) % nAll + nAll) % nAll;
					float s = q * FeedPitch - frac + (s0 - i * FeedPitch) * fade;
					// Renderer, not the object: the burn effect and heat source collect active renderers once, at Start.
					bool show = s <= c.Length + 0.3f * Pitch && s > -0.95f * FeedPitch && q < left;
					if (_renderers[i].enabled != show) _renderers[i].enabled = show;
					if (!show) continue;
					var f = c.Frame(Mathf.Max(0f, s));
					var (cp, cr) = Cartridge(Mathf.Max(0f, s));
					var p = f.MultiplyPoint3x4(cp);
					if (s < 0f) p -= feedUp.normalized * (Mathf.Clamp01(-s / FeedPitch) * Drop);
					t.localPosition = p;
					t.localRotation = f.rotation * cr;
				}
			}

			BeltCurve _curve;
			bool _placed;
			float _lastFed;
			int _lastHead, _lastLeft;
			Vector3 _lastFeed, _lastFeedDir, _lastFeedUp;

			bool Seen()
			{
				foreach (var r in _renderers) if (r != null && r.enabled && r.isVisible) return true;
				return false;
			}
		}
	}
}
