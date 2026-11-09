using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Weapons;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace T90A.Common
{
	// No colliders or rigid bodies, nothing a round or GHPC's raycasts can hit.
	[DefaultExecutionOrder(20)]
	internal sealed class KordBrass : MonoBehaviour
	{
		internal const float CaseLength = 0.108f;
		const float Radius = 0.0105f;
		const float EjectAt = 0.9f;
		// Speeds, spread, bounce and friction are guesses.
		const float EjectSpeed = 3f, SpeedSpread = 0.2f;
		const float DirSpread = 6f;
		const float Drag = 0.3f;
		const float BounceSteel = 0.35f, BounceGround = 0.15f, Mu = 0.45f, ImpactKeep = 0.6f;
		const float RestSpeed = 0.12f, RestTime = 0.2f, FlatUp = 0.85f;
		const float WedgedTime = 1f;
		const float HopTime = 0.1f;
		const float RockRadius = 0.06f, RockTime = 2f;
		const float TipOver = 10f;
		const float StepUp = 0.06f;
		const float WakeAccel = 4f;
		const float Cell = 0.02f;
		const float MaxStep = 1f / 120f;
		int MaxCases => _src != null ? _src.MaxCases : 300;

		sealed class Case
		{
			internal Transform T;
			internal Renderer R;
			internal Vector3 P, V, W, LocalP;
			internal Quaternion Q, LocalQ;
			internal int State;
			internal int On;
			internal float Slow, Born, StartY;
			internal Vector3 AnchorP;
			internal float AnchorT;
			internal Vector3 Anchor2P;
			internal float Anchor2T;
			internal float Air;
		}

		sealed class Grid
		{
			internal Transform Frame;
			internal float X0, Z0;
			internal int Nx, Nz;
			internal float[] H;

			internal bool Sample(float x, float z, out float h, out Vector3 n)
			{
				h = 0f; n = Vector3.up;
				int i = Mathf.FloorToInt((x - X0) / Cell), k = Mathf.FloorToInt((z - Z0) / Cell);
				if (i < 0 || k < 0 || i >= Nx || k >= Nz) return false;
				float h0 = h = H[k * Nx + i];
				if (float.IsNegativeInfinity(h)) return false;
				float At(int a, int b) { if (a < 0 || b < 0 || a >= Nx || b >= Nz) return h0; float v = H[b * Nx + a]; return float.IsNegativeInfinity(v) ? h0 - 1f : v; }
				float Flat(float down, float up) => Mathf.Abs(down) < Mathf.Abs(up) ? down : up;
				float dx = Flat(h0 - At(i - 1, k), At(i + 1, k) - h0) / Cell, dz = Flat(h0 - At(i, k - 1), At(i, k + 1) - h0) / Cell;
				n = new Vector3(-dx, 1f, -dz).normalized;
				return true;
			}
		}

		Vehicle _vehicle;
		Rigidbody _body;
		WeaponSystem _weapon;
		CaseSource _src;
		Transform _cradle;
		Transform[] _frames;
		Vector3 _chute, _dir;
		float _cycle;
		Mesh _mesh;
		Grid[] _grids;
		static readonly Grid[] NoGrids = new Grid[0];
		// On the main thread the maps cost a 160-190 ms frame.
		Task<Grid[]> _gridTask;
		Mesh.MeshDataArray _meshData;
		bool _meshDataHeld;
		float _nextPoll;
		Case[] _cases;
		int _next;
		GameObject _holder;
		Terrain[] _terrains;
		Vector3 _lastVel;
		readonly List<float> _due = new List<float>();

		internal static void Attach(Vehicle vehicle, WeaponSystem weapon, float cycle, Transform cradle, Vector3 chuteOnCradle, Vector3 dirOnCradle, KordBelt.Belt belt, Transform[] frames)
		{
			if (belt == null || belt.CaseMesh == null) return;
			Attach(vehicle, weapon, cycle, cradle, chuteOnCradle, dirOnCradle, CaseSource.FromBelt(belt), frames);
		}

		internal sealed class CaseSource
		{
			internal Mesh Mesh;
			internal Material Material;
			internal ShadowCastingMode Shadows = ShadowCastingMode.On;
			internal int Layer;
			internal KordBelt.Belt Belt;
			internal string MeshName = "Kord", Prefix = "", BeltObject;
			internal int MaxCases = 300;
			internal Bounds? Catcher;
			internal float Speed, Spread;

			internal static CaseSource FromBelt(KordBelt.Belt belt) => new CaseSource
			{
				Mesh = belt.CaseMesh, Belt = belt, MeshName = belt.Set.MeshName, Prefix = belt.Set.Prefix,
				BeltObject = belt.Set.BeltObject, MaxCases = belt.Set.MaxCases,
			};
		}

		internal static void Attach(Vehicle vehicle, WeaponSystem weapon, float cycle, Transform cradle, Vector3 chuteOnCradle, Vector3 dirOnCradle, CaseSource src, Transform[] frames)
		{
			if (src == null || src.Mesh == null) return;
			var b = cradle.gameObject.AddComponent<KordBrass>();
			b._vehicle = vehicle; b._body = vehicle.GetComponent<Rigidbody>();
			if (b._body == null) b._body = vehicle.GetComponentInParent<Rigidbody>();
			b._weapon = weapon; b._cycle = cycle; b._cradle = cradle; b._src = src;
			b._chute = chuteOnCradle; b._dir = dirOnCradle.normalized; b._frames = frames; b._mesh = src.Mesh;
			b._cases = new Case[b.MaxCases];
			weapon.Fired += b.OnFired;
		}

		void OnFired(AmmoType ammo, LiveRound round)
		{
			_cycle = KordBelt.Belt.Cycle(_weapon, _cycle);
			_due.Add(Time.time + EjectAt * _cycle);
		}

		void OnDestroy()
		{
			if (_weapon != null) _weapon.Fired -= OnFired;
			if (_holder != null) Destroy(_holder);
			// Don't wait on the worker here, that held the main thread up to 2 s.
			if (_meshDataHeld && _gridTask != null && !_gridTask.IsCompleted)
			{
				_meshDataHeld = false;
				MelonLoader.MelonCoroutines.Start(DisposeAfter(_gridTask, _meshData));
				return;
			}
			ReleaseMeshData();
		}

		static System.Collections.IEnumerator DisposeAfter(Task task, Mesh.MeshDataArray data)
		{
			while (!task.IsCompleted) yield return null;
			data.Dispose();
		}

		void ReleaseMeshData()
		{
			if (!_meshDataHeld) return;
			_meshDataHeld = false;
			_meshData.Dispose();
		}

		void LateUpdate() => Tick();

		void Tick()
		{
			if (_cases == null) return;
			if (_grids == null)
			{
				if (_gridTask == null && Time.time >= _nextPoll)
				{
					_nextPoll = Time.time + 0.5f;
					if (PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == _vehicle) StartGrids();
				}
				if (_gridTask != null && _gridTask.IsCompleted) FinishGrids();
			}
			for (int i = _due.Count - 1; i >= 0; i--)
				if (Time.time >= _due[i]) { _due.RemoveAt(i); Eject(); }
			float dt = Time.deltaTime;
			if (dt <= 0f || _holder == null) return;
			var vel = _body != null ? _body.velocity : Vector3.zero;
			bool jolt = (vel - _lastVel).magnitude / Mathf.Max(dt, Time.fixedDeltaTime) > WakeAccel;
			_lastVel = vel;
			_terrains = null;
			int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStep));
			float h = dt / steps;
			foreach (var c in _cases)
			{
				if (c == null || c.State == 0 || c.State == 3 || c.State == 4) continue;
				if (c.State == 2)
				{
					var f = _grids[c.On].Frame;
					if (f == null) { c.State = 0; c.R.enabled = false; continue; }
					c.P = f.TransformPoint(c.LocalP); c.Q = f.rotation * c.LocalQ;
					if (jolt || Vector3.Dot(f.up, Vector3.up) < FlatUp)
					{
						c.State = 1; c.Slow = 0f;
						c.V = _body != null ? _body.GetPointVelocity(c.P) : Vector3.zero;
						c.W = Random.onUnitSphere * 3f;
					}
					else { c.T.SetPositionAndRotation(c.P, c.Q); continue; }
				}
				if (c.State == 1 && _terrains == null) _terrains = Terrain.activeTerrains;
				for (int s = 0; s < steps && c.State == 1; s++) Step(c, h);
				if (c.State == 1 && (c.P.y < c.StartY - 50f || Time.time - c.Born > 30f)) { c.State = 0; c.R.enabled = false; continue; }
				c.T.SetPositionAndRotation(c.P, c.Q);
			}
		}

		void Eject()
		{
			if (_holder == null) _holder = new GameObject(_src.MeshName + " cases");
			if (_grids == null && _gridTask == null) StartGrids();
			var c = _cases[_next] ?? (_cases[_next] = NewCase(_next));
			_next = (_next + 1) % _cases.Length;
			c.P = _cradle.TransformPoint(_chute);
			float spread = _src.Spread > 0f ? _src.Spread : DirSpread, speed = _src.Speed > 0f ? _src.Speed : EjectSpeed;
			var dir = Quaternion.AngleAxis(Random.Range(-spread, spread), _cradle.up)
				* Quaternion.AngleAxis(Random.Range(-spread, spread), _cradle.right) * _cradle.TransformDirection(_dir);
			c.V = dir * speed * Random.Range(1f - SpeedSpread, 1f + SpeedSpread) + (_body != null ? _body.GetPointVelocity(c.P) : Vector3.zero);
			c.Q = Quaternion.LookRotation(_cradle.forward, _cradle.up);
			c.W = Vector3.Cross(dir, Random.onUnitSphere).normalized * Random.Range(4f, 12f);
			c.State = 1; c.Slow = 0f; c.Born = Time.time; c.StartY = c.P.y; c.AnchorT = Time.time; c.AnchorP = new Vector3(1e6f, 0f, 0f);
			c.Air = 0f; c.Anchor2T = Time.time; c.Anchor2P = c.AnchorP;
			c.T.SetPositionAndRotation(c.P, c.Q);
			c.R.enabled = true;
		}

		Case NewCase(int i)
		{
			var go = new GameObject($"{_src.MeshName} case {i:000}");
			go.layer = _src.Belt != null ? _src.Belt.gameObject.layer : _src.Layer;
			go.transform.SetParent(_holder.transform, false);
			go.AddComponent<MeshFilter>().sharedMesh = _mesh;
			var r = go.AddComponent<MeshRenderer>();
			var beltRenderer = _src.Belt != null ? _src.Belt.Rounds[0].t.GetComponent<Renderer>() : null;
			r.sharedMaterial = beltRenderer != null ? beltRenderer.sharedMaterial : _src.Material;
			r.shadowCastingMode = beltRenderer != null ? beltRenderer.shadowCastingMode : _src.Shadows;
			r.enabled = false;
			return new Case { T = go.transform, R = r };
		}

		void Step(Case c, float h)
		{
			c.V += Physics.gravity * h;
			c.V *= 1f - Drag * h;
			var prev = c.P;
			c.P += c.V * h;
			if (_src.Catcher.HasValue && _src.Catcher.Value.Contains(_cradle.InverseTransformPoint(c.P)))
			{
				c.State = 4; c.R.enabled = false; c.V = Vector3.zero; c.W = Vector3.zero;
				return;
			}
			if (c.W.sqrMagnitude > 1e-6f) c.Q = Quaternion.AngleAxis(c.W.magnitude * h * Mathf.Rad2Deg, c.W.normalized) * c.Q;

			int on = -2; float best = float.NegativeInfinity; Vector3 n = Vector3.up, top = Vector3.zero;
			bool wall = false;
			void Floor(int g, Transform f)
			{
				var lp = f.InverseTransformPoint(c.P);
				if (!_grids[g].Sample(lp.x, lp.z, out float gh, out Vector3 ln)) return;
				float depth = gh + Radius - lp.y;
				if (depth <= 0f || depth > StepUp) return;
				var surf = f.TransformPoint(new Vector3(lp.x, gh + Radius, lp.z));
				if (surf.y > best) { best = surf.y; on = g; n = f.TransformDirection(ln); top = surf; }
			}
			var grids = _grids ?? NoGrids;
			for (int g = 0; g < grids.Length; g++)
			{
				var f = _grids[g].Frame;
				if (f == null) continue;
				var lp = f.InverseTransformPoint(c.P);
				if (!_grids[g].Sample(lp.x, lp.z, out float gh, out _)) continue;
				if (gh + Radius - lp.y > StepUp)
				{
					var lq = f.InverseTransformPoint(prev);
					if (_grids[g].Sample(lq.x, lq.z, out float ph, out _) && ph + Radius - lq.y > StepUp) continue;
					var vs = _body != null ? _body.GetPointVelocity(c.P) : Vector3.zero;
					var lv = f.InverseTransformDirection(c.V - vs);
					lv.x *= -BounceSteel; lv.z *= -BounceSteel;
					c.V = f.TransformDirection(lv) + vs;
					c.P = f.TransformPoint(new Vector3(lq.x, lp.y, lq.z));
					wall = true;
				}
				Floor(g, f);
			}
			if (Ground(c.P, out float th, out Vector3 tn) && th + Radius > c.P.y && th + Radius > best)
			{
				best = th + Radius; on = -1; n = tn; top = new Vector3(c.P.x, th + Radius, c.P.z);
			}
			// Short hops keep the clocks, or a case bouncing on a slope never comes to rest.
			if (on == -2)
			{
				c.Air += h;
				if (!wall && c.Air > HopTime) { c.Slow = 0f; c.AnchorT = Time.time; c.Anchor2T = Time.time; }
				return;
			}
			c.Air = 0f;

			c.P = top;
			var sv = on >= 0 && _body != null ? _body.GetPointVelocity(c.P) : Vector3.zero;
			var vr = c.V - sv;
			float vn = Vector3.Dot(vr, n);
			var vt = vr - vn * n;
			if (vn < 0f)
			{
				bool impact = vn < -0.6f;
				if (impact) { vt *= ImpactKeep; c.W = c.W * 0.5f + Random.onUnitSphere * -vn * 6f; }
				vn = -vn * (on >= 0 ? BounceSteel : BounceGround);
			}
			float dv = Mu * -Physics.gravity.y * Mathf.Max(0f, n.y) * h;
			vt = vt.magnitude > dv ? vt - vt.normalized * dv : Vector3.zero;
			vr = vt + vn * n;
			c.V = vr + sv;
			c.W *= Mathf.Max(0f, 1f - 3f * h);
			var axis = c.Q * Vector3.forward;
			var lying = Vector3.ProjectOnPlane(axis, n);
			if (lying.sqrMagnitude > 1e-4f) c.Q = Quaternion.Slerp(c.Q, Quaternion.FromToRotation(axis, lying.normalized) * c.Q, Mathf.Min(1f, TipOver * h));
			if (vr.magnitude < RestSpeed)
			{
				c.Slow += h;
				if (c.Slow > (n.y > FlatUp ? RestTime : WedgedTime)) { Rest(c, on, n); return; }
			}
			else c.Slow = 0f;
			var local = on >= 0 ? _grids[on].Frame.InverseTransformPoint(c.P) : c.P;
			if ((local - c.AnchorP).sqrMagnitude > 0.02f * 0.02f) { c.AnchorP = local; c.AnchorT = Time.time; }
			else if (Time.time - c.AnchorT > WedgedTime) { Rest(c, on, n); return; }
			if ((local - c.Anchor2P).sqrMagnitude > RockRadius * RockRadius) { c.Anchor2P = local; c.Anchor2T = Time.time; }
			else if (Time.time - c.Anchor2T > RockTime) Rest(c, on, n);
		}

		void Rest(Case c, int on, Vector3 n)
		{
			var axis = Vector3.ProjectOnPlane(c.Q * Vector3.forward, n);
			if (axis.sqrMagnitude < 1e-4f) axis = Vector3.ProjectOnPlane(Random.onUnitSphere, n);
			c.Q = Quaternion.LookRotation(axis.normalized, n) * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
			c.V = Vector3.zero; c.W = Vector3.zero;
			if (on >= 0)
			{
				var f = _grids[on].Frame;
				c.State = 2; c.On = on;
				c.LocalP = f.InverseTransformPoint(c.P); c.LocalQ = Quaternion.Inverse(f.rotation) * c.Q;
			}
			else c.State = 3;
			c.T.SetPositionAndRotation(c.P, c.Q);
		}

		bool Ground(Vector3 p, out float h, out Vector3 n)
		{
			h = 0f; n = Vector3.up;
			if (_terrains == null) return false;
			foreach (var t in _terrains)
			{
				if (t == null || t.terrainData == null) continue;
				var o = t.GetPosition(); var s = t.terrainData.size;
				float u = (p.x - o.x) / s.x, w = (p.z - o.z) / s.z;
				if (u < 0f || u > 1f || w < 0f || w > 1f) continue;
				h = t.SampleHeight(p) + o.y;
				n = t.terrainData.GetInterpolatedNormal(u, w);
				return true;
			}
			return false;
		}

		List<Mesh> _buildMeshes;
		List<(int grid, int mesh, Matrix4x4 m)> _buildParts;
		Grid[] _buildShells;

		void StartGrids()
		{
			var root = _vehicle.transform;
			bool Ours(Transform t)
			{
				bool ours = false;
				for (var x = t; x != null && x != root; x = x.parent)
				{
					if (x == _cradle || (_src.BeltObject != null && x.name.StartsWith(_src.BeltObject))) return false;
					if (x.name.StartsWith(_src.Prefix)) ours = true;
				}
				return ours;
			}
			var meshes = new List<Mesh>();
			var index = new Dictionary<Mesh, int>();
			var parts = new List<(int grid, int mesh, Matrix4x4 m)>();
			var shells = new List<Grid>();
			var gridOf = Enumerable.Repeat(-1, _frames.Length).ToArray();
			foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (!r.enabled || !Ours(r.transform)) continue;
				var mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null) continue;
				if (!mf.sharedMesh.isReadable) continue;
				int f = System.Array.FindIndex(_frames, x => x != null && r.transform.IsChildOf(x));
				if (f < 0) continue;
				if (gridOf[f] < 0) { gridOf[f] = shells.Count; shells.Add(new Grid { Frame = _frames[f] }); }
				if (!index.TryGetValue(mf.sharedMesh, out int mi)) { mi = meshes.Count; index[mf.sharedMesh] = mi; meshes.Add(mf.sharedMesh); }
				parts.Add((gridOf[f], mi, _frames[f].worldToLocalMatrix * mf.transform.localToWorldMatrix));
			}
			_buildMeshes = meshes; _buildParts = parts; _buildShells = shells.ToArray();
			try
			{
				_meshData = Mesh.AcquireReadOnlyMeshData(meshes);
				_meshDataHeld = true;
			}
			catch (System.Exception)
			{
				MakeGridsHere();
				return;
			}
			var data = _meshData;
			var shellArr = _buildShells;
			_gridTask = Task.Run(() =>
			{
				var read = new Dictionary<int, (Vector3[] v, int[] i)>();
				FillAll(shellArr, parts, mi => read.TryGetValue(mi, out var vi) ? vi : (read[mi] = Read(data[mi])));
				return shellArr;
			});
		}

		void MakeGridsHere()
		{
			ReleaseMeshData();
			var meshes = _buildMeshes;
			FillAll(_buildShells, _buildParts, mi => (meshes[mi].vertices, meshes[mi].triangles));
			_grids = _buildShells;
		}

		void FinishGrids()
		{
			var task = _gridTask;
			_gridTask = null;
			if (task.IsFaulted || task.IsCanceled)
			{
				MakeGridsHere();
				return;
			}
			_grids = task.Result;
			ReleaseMeshData();
			_buildMeshes = null; _buildParts = null;
		}

		static (Vector3[] v, int[] i) Read(Mesh.MeshData md)
		{
			var v = new Vector3[md.vertexCount];
			using (var nv = new NativeArray<Vector3>(md.vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory))
			{
				md.GetVertices(nv);
				nv.CopyTo(v);
			}
			var idx = new List<int>();
			for (int s = 0; s < md.subMeshCount; s++)
			{
				var sm = md.GetSubMesh(s);
				if (sm.topology != MeshTopology.Triangles || sm.indexCount == 0) continue;
				using (var ni = new NativeArray<int>(sm.indexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory))
				{
					md.GetIndices(ni, s);
					idx.AddRange(ni);
				}
			}
			return (v, idx.ToArray());
		}

		static void FillAll(Grid[] grids, List<(int grid, int mesh, Matrix4x4 m)> parts, System.Func<int, (Vector3[] v, int[] i)> mesh)
		{
			for (int gi = 0; gi < grids.Length; gi++)
			{
				var g = grids[gi];
				var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();
				float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
				foreach (var (pg, mi, m) in parts)
				{
					if (pg != gi) continue;
					var (v, t) = mesh(mi);
					var lv = new Vector3[v.Length];
					for (int i = 0; i < v.Length; i++) lv[i] = m.MultiplyPoint3x4(v[i]);
					for (int i = 0; i + 2 < t.Length; i += 3)
					{
						Vector3 a = lv[t[i]], b = lv[t[i + 1]], c = lv[t[i + 2]];
						var nn = Vector3.Cross(b - a, c - a);
						if (nn.sqrMagnitude < 1e-12f || Mathf.Abs(nn.normalized.y) < 0.2f) continue;
						tris.Add((a, b, c));
						x0 = Mathf.Min(x0, a.x, b.x, c.x); x1 = Mathf.Max(x1, a.x, b.x, c.x);
						z0 = Mathf.Min(z0, a.z, b.z, c.z); z1 = Mathf.Max(z1, a.z, b.z, c.z);
					}
				}
				if (tris.Count == 0) { g.H = new float[0]; continue; }
				g.X0 = x0; g.Z0 = z0;
				g.Nx = Mathf.CeilToInt((x1 - x0) / Cell) + 1; g.Nz = Mathf.CeilToInt((z1 - z0) / Cell) + 1;
				int nx = g.Nx, nz = g.Nz;
				var H = g.H = Enumerable.Repeat(float.NegativeInfinity, nx * nz).ToArray();
				void Put(int i, int k, float y) { if (i >= 0 && k >= 0 && i < nx && k < nz && y > H[k * nx + i]) H[k * nx + i] = y; }
				foreach (var (a, b, c) in tris)
				{
					Put(Mathf.FloorToInt((a.x - x0) / Cell), Mathf.FloorToInt((a.z - z0) / Cell), a.y);
					Put(Mathf.FloorToInt((b.x - x0) / Cell), Mathf.FloorToInt((b.z - z0) / Cell), b.y);
					Put(Mathf.FloorToInt((c.x - x0) / Cell), Mathf.FloorToInt((c.z - z0) / Cell), c.y);
					float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
					if (Mathf.Abs(d) < 1e-10f) continue;
					int i0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - x0) / Cell), i1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) - x0) / Cell);
					int k0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z, c.z) - z0) / Cell), k1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z, c.z) - z0) / Cell);
					for (int k = k0; k <= k1; k++)
						for (int i = i0; i <= i1; i++)
						{
							float px = x0 + (i + 0.5f) * Cell, pz = z0 + (k + 0.5f) * Cell;
							float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / d;
							float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d;
							float l3 = 1f - l1 - l2;
							if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
							Put(i, k, l1 * a.y + l2 * b.y + l3 * c.y);
						}
				}
			}
		}

		internal static Mesh CaseMesh(Vector3[] v, Vector3[] n, Vector2[] uv, Vector4[] tg, int[] tris, List<int> ts, string name)
		{
			var idx = ts.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] }).Distinct().ToList();
			var c = Vector3.zero;
			foreach (int i in idx) c += v[i];
			c /= idx.Count;
			float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
			foreach (int i in idx) { var d = v[i] - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
			var a = new Vector3(1f, 0.7f, 0.3f).normalized;
			for (int k = 0; k < 40; k++) a = new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z).normalized;
			float Rad(int i) => Vector3.ProjectOnPlane(v[i] - c, a).magnitude;
			float rPos = idx.Where(i => Vector3.Dot(v[i] - c, a) > 0f).Select(Rad).DefaultIfEmpty(0f).Average();
			float rNeg = idx.Where(i => Vector3.Dot(v[i] - c, a) < 0f).Select(Rad).DefaultIfEmpty(0f).Average();
			if (rPos > rNeg) a = -a;
			float zMin = idx.Min(i => Vector3.Dot(v[i] - c, a));
			var o = c + a * zMin;
			var e1 = Vector3.Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			var e2 = Vector3.Cross(a, e1);
			Vector3 Rot(Vector3 d) => new Vector3(Vector3.Dot(d, e1), Vector3.Dot(d, e2), Vector3.Dot(d, a));

			var vv = new List<Vector3>(); var nn = new List<Vector3>(); var uu = new List<Vector2>(); var tt = new List<Vector4>(); var ii = new List<int>();
			bool hasN = n.Length == v.Length, hasUv = uv.Length == v.Length, hasT = tg.Length == v.Length;
			(Vector3 p, Vector3 n, Vector2 uv, Vector4 t) Vert(int i)
			{
				var t4 = hasT ? tg[i] : new Vector4(1f, 0f, 0f, 1f);
				var td = Rot(new Vector3(t4.x, t4.y, t4.z));
				return (Rot(v[i] - o), hasN ? Rot(n[i]) : Vector3.up, hasUv ? uv[i] : Vector2.zero, new Vector4(td.x, td.y, td.z, t4.w));
			}
			var poly = new List<(Vector3 p, Vector3 n, Vector2 uv, Vector4 t)>();
			foreach (int t in ts)
			{
				var tri = new[] { Vert(tris[t]), Vert(tris[t + 1]), Vert(tris[t + 2]) };
				poly.Clear();
				for (int j = 0; j < 3; j++)
				{
					var p = tri[j]; var q = tri[(j + 1) % 3];
					bool pin = p.p.z <= CaseLength, qin = q.p.z <= CaseLength;
					if (pin) poly.Add(p);
					if (pin != qin)
					{
						float f = (CaseLength - p.p.z) / (q.p.z - p.p.z);
						var tq = Vector4.Lerp(p.t, q.t, f);
						var td = new Vector3(tq.x, tq.y, tq.z).normalized;
						poly.Add((Vector3.Lerp(p.p, q.p, f), Vector3.Lerp(p.n, q.n, f).normalized, Vector2.Lerp(p.uv, q.uv, f), new Vector4(td.x, td.y, td.z, p.t.w)));
					}
				}
				if (poly.Count < 3) continue;
				int b0 = vv.Count;
				foreach (var x in poly) { vv.Add(x.p); nn.Add(x.n); uu.Add(x.uv); tt.Add(x.t); }
				for (int j = 1; j + 1 < poly.Count; j++) { ii.Add(b0); ii.Add(b0 + j); ii.Add(b0 + j + 1); }
			}
			var m = new Mesh { name = name };
			m.SetVertices(vv); m.SetNormals(nn); m.SetUVs(0, uu); if (hasT) m.SetTangents(tt);
			m.SetTriangles(ii, 0); m.RecalculateBounds();
			return m;
		}
	}
}
