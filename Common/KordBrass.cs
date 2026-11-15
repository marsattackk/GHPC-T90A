using System.Collections.Generic;
using System.Diagnostics;
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
			internal int LastOn = -2;
			internal Vector3 LastN = Vector3.up;
			internal Vector3 AnchorP;
			internal float AnchorT;
			internal Vector3 Anchor2P;
			internal float Anchor2T;
			internal float Air;
			internal int Hops, Walls;
			internal bool Wedged;
		}

		sealed class Grid
		{
			internal string Name;
			internal Transform Frame;
			internal float X0, Z0;
			internal int Nx, Nz, Renderers, Triangles;
			internal float[] H;

			internal bool Sample(float x, float z, out float h, out Vector3 n)
			{
				h = 0f;
				n = Vector3.up;
				int i = Mathf.FloorToInt((x - X0) / Cell), k = Mathf.FloorToInt((z - Z0) / Cell);
				if (i < 0 || k < 0 || i >= Nx || k >= Nz) return false;
				float cellHeight = h = H[k * Nx + i];
				if (float.IsNegativeInfinity(h)) return false;
				float At(int column, int row)
				{
					if (column < 0 || row < 0 || column >= Nx || row >= Nz) return cellHeight;
					float height = H[row * Nx + column];
					return float.IsNegativeInfinity(height) ? cellHeight - 1f : height;
				}
				float Flat(float down, float up) => Mathf.Abs(down) < Mathf.Abs(up) ? down : up;
				float slopeX = Flat(
					cellHeight - At(i - 1, k),
					At(i + 1, k) - cellHeight) / Cell, slopeZ = Flat(cellHeight - At(i, k - 1), At(i, k + 1) - cellHeight) / Cell;
				n = new Vector3(-slopeX, 1f, -slopeZ).normalized;
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
		float _buildStart, _buildMainMs, _workerMs, _nextPoll;
		string _buildWhy, _buildInfo;
		Case[] _cases;
		int _next;
		GameObject _holder;
		Terrain[] _terrains;
		Vector3 _lastVel;
		readonly List<float> _due = new List<float>();
		internal int Ejected { get; private set; }

		internal int Caught { get; private set; }

		internal Bounds? Catcher => _src?.Catcher;

		internal static KordBrass Attach(
			Vehicle vehicle,
			WeaponSystem weapon,
			float cycle,
			Transform cradle,
			Vector3 chuteOnCradle,
			Vector3 dirOnCradle,
			KordBelt.Belt belt,
			Transform[] frames)
		{
			if (belt == null || belt.CaseMesh == null) return null;
			return Attach(vehicle, weapon, cycle, cradle, chuteOnCradle, dirOnCradle, CaseSource.FromBelt(belt), frames);
		}

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class CaseSource
		{
			internal Mesh Mesh;
			internal Material Material;
			internal ShadowCastingMode Shadows = ShadowCastingMode.On;
			internal int Layer;
			internal KordBelt.Belt Belt;
			internal string Tag = "Kord", MeshName = "Kord", Prefix = "", BeltObject;
			internal int MaxCases = 300;
			internal Bounds? Catcher;
			internal float Speed, Spread;

			internal static CaseSource FromBelt(KordBelt.Belt belt) => new CaseSource
			{
				Mesh = belt.CaseMesh,
				Belt = belt,
				Tag = belt.Set.Tag,
				MeshName = belt.Set.MeshName,
				Prefix = belt.Set.Prefix,
				BeltObject = belt.Set.BeltObject,
				MaxCases = belt.Set.MaxCases,
			};
		}
#pragma warning restore CS0649

		internal static KordBrass Attach(
			Vehicle vehicle,
			WeaponSystem weapon,
			float cycle,
			Transform cradle,
			Vector3 chuteOnCradle,
			Vector3 dirOnCradle,
			CaseSource src,
			Transform[] frames)
		{
			if (src == null || src.Mesh == null) return null;
			var brass = cradle.gameObject.AddComponent<KordBrass>();
			brass._vehicle = vehicle;
			brass._body = vehicle.GetComponent<Rigidbody>();
			if (brass._body == null) brass._body = vehicle.GetComponentInParent<Rigidbody>();
			brass._weapon = weapon;
			brass._cycle = cycle;
			brass._cradle = cradle;
			brass._src = src;
			brass._chute = chuteOnCradle;
			brass._dir = dirOnCradle.normalized;
			brass._frames = frames;
			brass._mesh = src.Mesh;
			brass._cases = new Case[brass.MaxCases];
			weapon.Fired += brass.OnFired;
			return brass;
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
			// Don't wait on the worker here. That stalled the main thread for up to 2 s.
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
					if (PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == _vehicle)
						StartGrids("the player took the tank");
				}
				if (_gridTask != null && _gridTask.IsCompleted) FinishGrids();
			}
			for (int i = _due.Count - 1; i >= 0; i--)
				if (Time.time >= _due[i])
				{
					_due.RemoveAt(i);
					Eject();
				}
			float deltaTime = Time.deltaTime;
			if (deltaTime <= 0f || _holder == null) return;
			var vel = _body != null ? _body.velocity : Vector3.zero;
			bool jolt = (vel - _lastVel).magnitude / Mathf.Max(deltaTime, Time.fixedDeltaTime) > WakeAccel;
			_lastVel = vel;
			_terrains = null;
			int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / MaxStep));
			float stepTime = deltaTime / steps;
			foreach (var casing in _cases)
			{
				if (casing == null || casing.State == 0 || casing.State == 3 || _src.Catcher.HasValue && casing.State == 4) continue;
				if (casing.State == 2)
				{
					var frame = _grids[casing.On].Frame;
					if (frame == null)
					{
						casing.State = 0;
						casing.R.enabled = false;
						continue;
					}
					casing.P = frame.TransformPoint(casing.LocalP);
					casing.Q = frame.rotation * casing.LocalQ;
					if (jolt || Vector3.Dot(frame.up, Vector3.up) < FlatUp)
					{
						casing.State = 1;
						casing.Slow = 0f;
						casing.V = _body != null ? _body.GetPointVelocity(casing.P) : Vector3.zero;
						casing.W = Random.onUnitSphere * 3f;
					}
					else
					{
						casing.T.SetPositionAndRotation(casing.P, casing.Q);
						continue;
					}
				}
				if (casing.State == 1 && _terrains == null) _terrains = Terrain.activeTerrains;
				for (int substep = 0; substep < steps && casing.State == 1; substep++) Step(casing, stepTime);
				if (casing.State == 1 && (casing.P.y < casing.StartY - 50f || Time.time - casing.Born > 30f))
				{
					casing.State = 0;
					casing.R.enabled = false;
					continue;
				}
				casing.T.SetPositionAndRotation(casing.P, casing.Q);
			}
		}

		void Eject()
		{
			if (_holder == null) _holder = new GameObject(_src.MeshName + " cases");
			if (_grids == null && _gridTask == null) StartGrids("first shot");
			var casing = _cases[_next] ?? (_cases[_next] = NewCase(_next));
			_next = (_next + 1) % _cases.Length;
			casing.P = _cradle.TransformPoint(_chute);
			float spread = _src.Spread > 0f ? _src.Spread : DirSpread;
			float speed = _src.Speed > 0f ? _src.Speed : EjectSpeed;
			var dir = Quaternion.AngleAxis(Random.Range(-spread, spread), _cradle.up)
				* Quaternion.AngleAxis(Random.Range(-spread, spread), _cradle.right) * _cradle.TransformDirection(_dir);
			casing.V = dir * speed * Random.Range(1f - SpeedSpread, 1f + SpeedSpread)
				+ (_body != null ? _body.GetPointVelocity(casing.P) : Vector3.zero);
			casing.Q = Quaternion.LookRotation(_cradle.forward, _cradle.up);
			casing.W = Vector3.Cross(dir, Random.onUnitSphere).normalized * Random.Range(4f, 12f);
			casing.State = 1;
			casing.Slow = 0f;
			casing.Born = Time.time;
			casing.StartY = casing.P.y;
			casing.AnchorT = Time.time;
			casing.AnchorP = new Vector3(1e6f, 0f, 0f);
			casing.Air = 0f;
			casing.Hops = 0;
			casing.Walls = 0;
			casing.Wedged = false;
			casing.Anchor2T = Time.time;
			casing.Anchor2P = casing.AnchorP;
			casing.T.SetPositionAndRotation(casing.P, casing.Q);
			casing.R.enabled = true;
			Ejected++;
		}

		Case NewCase(int i)
		{
			var casingObject = new GameObject($"{_src.MeshName} case {i:000}");
			casingObject.layer = _src.Belt != null ? _src.Belt.gameObject.layer : _src.Layer;
			casingObject.transform.SetParent(_holder.transform, false);
			casingObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
			var meshRenderer = casingObject.AddComponent<MeshRenderer>();
			var beltRenderer = _src.Belt != null ? _src.Belt.Rounds[0].t.GetComponent<Renderer>() : null;
			meshRenderer.sharedMaterial = beltRenderer != null ? beltRenderer.sharedMaterial : _src.Material;
			meshRenderer.shadowCastingMode = beltRenderer != null ? beltRenderer.shadowCastingMode : _src.Shadows;
			meshRenderer.enabled = false;
			return new Case { T = casingObject.transform, R = meshRenderer };
		}

		void Step(Case c, float h)
		{
			c.V += Physics.gravity * h;
			c.V *= 1f - Drag * h;
			var prev = c.P;
			c.P += c.V * h;
			if (_src.Catcher.HasValue && _src.Catcher.Value.Contains(_cradle.InverseTransformPoint(c.P)))
			{
				c.State = 4;
				c.R.enabled = false;
				c.V = Vector3.zero;
				c.W = Vector3.zero;
				Caught++;
				return;
			}
			if (c.W.sqrMagnitude > 1e-6f) c.Q = Quaternion.AngleAxis(c.W.magnitude * h * Mathf.Rad2Deg, c.W.normalized) * c.Q;

			int contactGrid = -2;
			float best = float.NegativeInfinity;
			Vector3 normal = Vector3.up, top = Vector3.zero;
			bool wall = false;
			void Floor(int gridIndex, Transform frame)
			{
				var localPoint = frame.InverseTransformPoint(c.P);
				if (!_grids[gridIndex].Sample(localPoint.x, localPoint.z, out float gridHeight, out Vector3 localNormal)) return;
				float depth = gridHeight + Radius - localPoint.y;
				if (depth <= 0f || depth > StepUp) return;
				var surf = frame.TransformPoint(new Vector3(localPoint.x, gridHeight + Radius, localPoint.z));
				if (surf.y > best)
				{
					best = surf.y;
					contactGrid = gridIndex;
					normal = frame.TransformDirection(localNormal);
					top = surf;
				}
			}
			var grids = _grids ?? NoGrids;
			for (int gridIndex = 0; gridIndex < grids.Length; gridIndex++)
			{
				var frame = _grids[gridIndex].Frame;
				if (frame == null) continue;
				var localPoint = frame.InverseTransformPoint(c.P);
				if (!_grids[gridIndex].Sample(localPoint.x, localPoint.z, out float gridHeight, out _)) continue;
				if (gridHeight + Radius - localPoint.y > StepUp)
				{
					var localPrevious = frame.InverseTransformPoint(prev);
					if (_grids[gridIndex].Sample(localPrevious.x, localPrevious.z, out float previousHeight, out _)
						&& previousHeight + Radius - localPrevious.y > StepUp)
						continue;
					var surfaceVelocity = _body != null ? _body.GetPointVelocity(c.P) : Vector3.zero;
					var localVelocity = frame.InverseTransformDirection(c.V - surfaceVelocity);
					localVelocity.x *= -BounceSteel;
					localVelocity.z *= -BounceSteel;
					c.V = frame.TransformDirection(localVelocity) + surfaceVelocity;
					c.P = frame.TransformPoint(new Vector3(localPrevious.x, localPoint.y, localPrevious.z));
					wall = true;
				}
				Floor(gridIndex, frame);
			}
			if (Ground(c.P, out float terrainHeight, out Vector3 terrainNormal)
				&& terrainHeight + Radius > c.P.y
				&& terrainHeight + Radius > best)
			{
				best = terrainHeight + Radius;
				contactGrid = -1;
				normal = terrainNormal;
				top = new Vector3(c.P.x, terrainHeight + Radius, c.P.z);
			}
			if (wall) c.Walls++;
			// Short hops keep the clocks, or a case bouncing on a slope never comes to rest.
			if (contactGrid == -2)
			{
				c.Air += h;
				if (!wall && c.Air > HopTime)
				{
					c.Slow = 0f;
					c.AnchorT = Time.time;
					c.Anchor2T = Time.time;
				}
				return;
			}
			if (c.Air > 0f && c.Air <= HopTime) c.Hops++;
			c.Air = 0f;
			c.LastOn = contactGrid;
			c.LastN = normal;

			c.P = top;
			var groundVelocity = contactGrid >= 0 && _body != null ? _body.GetPointVelocity(c.P) : Vector3.zero;
			var relativeVelocity = c.V - groundVelocity;
			float normalSpeed = Vector3.Dot(relativeVelocity, normal);
			var tangentVelocity = relativeVelocity - normalSpeed * normal;
			if (normalSpeed < 0f)
			{
				bool impact = normalSpeed < -0.6f;
				if (impact)
				{
					tangentVelocity *= ImpactKeep;
					c.W = c.W * 0.5f + Random.onUnitSphere * -normalSpeed * 6f;
				}
				normalSpeed = -normalSpeed * (contactGrid >= 0 ? BounceSteel : BounceGround);
			}
			float frictionLoss = Mu * -Physics.gravity.y * Mathf.Max(0f, normal.y) * h;
			tangentVelocity = tangentVelocity.magnitude > frictionLoss
				? tangentVelocity - tangentVelocity.normalized * frictionLoss
				: Vector3.zero;
			relativeVelocity = tangentVelocity + normalSpeed * normal;
			c.V = relativeVelocity + groundVelocity;
			c.W *= Mathf.Max(0f, 1f - 3f * h);
			var axis = c.Q * Vector3.forward;
			var lying = Vector3.ProjectOnPlane(axis, normal);
			if (lying.sqrMagnitude > 1e-4f)
				c.Q = Quaternion.Slerp(c.Q, Quaternion.FromToRotation(axis, lying.normalized) * c.Q, Mathf.Min(1f, TipOver * h));
			if (relativeVelocity.magnitude < RestSpeed)
			{
				c.Slow += h;
				if (c.Slow > (normal.y > FlatUp ? RestTime : WedgedTime))
				{
					Rest(c, contactGrid, normal);
					return;
				}
			}
			else c.Slow = 0f;
			var local = contactGrid >= 0 ? _grids[contactGrid].Frame.InverseTransformPoint(c.P) : c.P;
			if ((local - c.AnchorP).sqrMagnitude > 0.02f * 0.02f)
			{
				c.AnchorP = local;
				c.AnchorT = Time.time;
			}
			else if (Time.time - c.AnchorT > WedgedTime)
			{
				c.Wedged = true;
				Rest(c, contactGrid, normal);
				return;
			}
			if ((local - c.Anchor2P).sqrMagnitude > RockRadius * RockRadius)
			{
				c.Anchor2P = local;
				c.Anchor2T = Time.time;
			}
			else if (Time.time - c.Anchor2T > RockTime)
			{
				c.Wedged = true;
				Rest(c, contactGrid, normal);
			}
		}

		void Rest(Case c, int on, Vector3 n)
		{
			var axis = Vector3.ProjectOnPlane(c.Q * Vector3.forward, n);
			if (axis.sqrMagnitude < 1e-4f) axis = Vector3.ProjectOnPlane(Random.onUnitSphere, n);
			c.Q = Quaternion.LookRotation(axis.normalized, n) * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
			c.V = Vector3.zero;
			c.W = Vector3.zero;
			if (on >= 0)
			{
				var frame = _grids[on].Frame;
				c.State = 2;
				c.On = on;
				c.LocalP = frame.InverseTransformPoint(c.P);
				c.LocalQ = Quaternion.Inverse(frame.rotation) * c.Q;
			}
			else c.State = 3;
			c.T.SetPositionAndRotation(c.P, c.Q);
		}

		bool Ground(Vector3 p, out float h, out Vector3 n)
		{
			h = 0f;
			n = Vector3.up;
			if (_terrains == null) return false;
			foreach (var terrain in _terrains)
			{
				if (terrain == null || terrain.terrainData == null) continue;
				var origin = terrain.GetPosition();
				var size = terrain.terrainData.size;
				float fractionX = (p.x - origin.x) / size.x, fractionZ = (p.z - origin.z) / size.z;
				if (fractionX < 0f || fractionX > 1f || fractionZ < 0f || fractionZ > 1f) continue;
				h = terrain.SampleHeight(p) + origin.y;
				n = terrain.terrainData.GetInterpolatedNormal(fractionX, fractionZ);
				return true;
			}
			return false;
		}

		List<Mesh> _buildMeshes;
		List<(int grid, int mesh, Matrix4x4 m)> _buildParts;
		Grid[] _buildShells;

		void StartGrids(string why)
		{
			var stopwatch = Stopwatch.StartNew();
			_buildWhy = why;
			_buildStart = Time.realtimeSinceStartup;
			var root = _vehicle.transform;
			bool Ours(Transform part)
			{
				bool ours = false;
				for (var ancestor = part; ancestor != null && ancestor != root; ancestor = ancestor.parent)
				{
					if (ancestor == _cradle || (_src.BeltObject != null && ancestor.name.StartsWith(_src.BeltObject))) return false;
					if (ancestor.name.StartsWith(_src.Prefix)) ours = true;
				}
				return ours;
			}
			var meshes = new List<Mesh>();
			var index = new Dictionary<Mesh, int>();
			var parts = new List<(int grid, int mesh, Matrix4x4 m)>();
			var shells = new List<Grid>();
			var gridOf = Enumerable.Repeat(-1, _frames.Length).ToArray();
			int unreadable = 0;
			foreach (var meshRenderer in root.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (!meshRenderer.enabled || !Ours(meshRenderer.transform)) continue;
				var meshFilter = meshRenderer.GetComponent<MeshFilter>();
				if (meshFilter == null || meshFilter.sharedMesh == null) continue;
				if (!meshFilter.sharedMesh.isReadable)
				{
					unreadable++;
					continue;
				}
				int frameIndex = System.Array.FindIndex(_frames, f => f != null && meshRenderer.transform.IsChildOf(f));
				if (frameIndex < 0) continue;
				if (gridOf[frameIndex] < 0)
				{
					gridOf[frameIndex] = shells.Count;
					shells.Add(new Grid { Name = _frames[frameIndex].name, Frame = _frames[frameIndex] });
				}
				if (!index.TryGetValue(meshFilter.sharedMesh, out int meshIndex))
				{
					meshIndex = meshes.Count;
					index[meshFilter.sharedMesh] = meshIndex;
					meshes.Add(meshFilter.sharedMesh);
				}
				parts.Add((gridOf[frameIndex], meshIndex, _frames[frameIndex].worldToLocalMatrix * meshFilter.transform.localToWorldMatrix));
				shells[gridOf[frameIndex]].Renderers++;
			}
			_buildMeshes = meshes;
			_buildParts = parts;
			_buildShells = shells.ToArray();
			_buildInfo = unreadable > 0 ? $"; {unreadable} meshes not readable, left out" : "";
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
			_buildMainMs = (float)stopwatch.Elapsed.TotalMilliseconds;
			_gridTask = Task.Run(() =>
			{
				var workerWatch = Stopwatch.StartNew();
				var read = new Dictionary<int, (Vector3[] v, int[] i)>();
				FillAll(
					shellArr,
					parts,
					m => read.TryGetValue(m, out var meshData) ? meshData : (read[m] = Read(data[m])));
				_workerMs = (float)workerWatch.Elapsed.TotalMilliseconds;
				return shellArr;
			});
		}

		void MakeGridsHere()
		{
			var stopwatch = Stopwatch.StartNew();
			ReleaseMeshData();
			var meshes = _buildMeshes;
			FillAll(_buildShells, _buildParts, m => (meshes[m].vertices, meshes[m].triangles));
			_grids = _buildShells;
			_buildMainMs = (float)stopwatch.Elapsed.TotalMilliseconds;
			_workerMs = 0f;
			LogGrids("made on the main thread");
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
			_buildMeshes = null;
			_buildParts = null;
			LogGrids($"main thread {_buildMainMs:0.0} ms, worker {_workerMs:0} ms, ready {(Time.realtimeSinceStartup - _buildStart) * 1000f:0} ms after the start");
		}

		void LogGrids(string how)
		{
		}

		static (Vector3[] v, int[] i) Read(Mesh.MeshData md)
		{
			var vertices = new Vector3[md.vertexCount];
			using (var nativeVertices = new NativeArray<Vector3>(
				md.vertexCount,
				Allocator.Persistent,
				NativeArrayOptions.UninitializedMemory))
			{
				md.GetVertices(nativeVertices);
				nativeVertices.CopyTo(vertices);
			}
			var indices = new List<int>();
			for (int subMeshIndex = 0; subMeshIndex < md.subMeshCount; subMeshIndex++)
			{
				var subMesh = md.GetSubMesh(subMeshIndex);
				if (subMesh.topology != MeshTopology.Triangles || subMesh.indexCount == 0) continue;
				using (var nativeIndices = new NativeArray<int>(
					subMesh.indexCount,
					Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory))
				{
					md.GetIndices(nativeIndices, subMeshIndex);
					indices.AddRange(nativeIndices);
				}
			}
			return (vertices, indices.ToArray());
		}

		string Pt(Vector3 w)
		{
			var point = _vehicle.transform.InverseTransformPoint(w);
			return $"({point.x:0.000}, {point.y:0.000}, {point.z:0.000})";
		}

		static void FillAll(Grid[] grids, List<(int grid, int mesh, Matrix4x4 m)> parts, System.Func<int, (Vector3[] v, int[] i)> mesh)
		{
			for (int gridIndex = 0; gridIndex < grids.Length; gridIndex++)
			{
				var grid = grids[gridIndex];
				var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();
				float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
				foreach (var (partGrid, meshIndex, matrix) in parts)
				{
					if (partGrid != gridIndex) continue;
					var (vertices, triangles) = mesh(meshIndex);
					var localVertices = new Vector3[vertices.Length];
					for (int i = 0; i < vertices.Length; i++) localVertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
					for (int i = 0; i + 2 < triangles.Length; i += 3)
					{
						Vector3 cornerA = localVertices[triangles[i]], cornerB = localVertices[triangles[i
							+ 1]], cornerC = localVertices[triangles[i + 2]];
						var faceNormal = Vector3.Cross(cornerB - cornerA, cornerC - cornerA);
						if (faceNormal.sqrMagnitude < 1e-12f || Mathf.Abs(faceNormal.normalized.y) < 0.2f) continue;
						tris.Add((cornerA, cornerB, cornerC));
						minX = Mathf.Min(minX, cornerA.x, cornerB.x, cornerC.x);
						maxX = Mathf.Max(maxX, cornerA.x, cornerB.x, cornerC.x);
						minZ = Mathf.Min(minZ, cornerA.z, cornerB.z, cornerC.z);
						maxZ = Mathf.Max(maxZ, cornerA.z, cornerB.z, cornerC.z);
					}
				}
				grid.Triangles = tris.Count;
				if (tris.Count == 0)
				{
					grid.H = new float[0];
					continue;
				}
				grid.X0 = minX;
				grid.Z0 = minZ;
				grid.Nx = Mathf.CeilToInt((maxX - minX) / Cell) + 1;
				grid.Nz = Mathf.CeilToInt((maxZ - minZ) / Cell) + 1;
				int cellsX = grid.Nx, cellsZ = grid.Nz;
				var heights = grid.H = Enumerable.Repeat(float.NegativeInfinity, cellsX * cellsZ).ToArray();
				void Put(int i, int k, float y)
				{
					if (i >= 0 && k >= 0 && i < cellsX && k < cellsZ && y > heights[k * cellsX + i]) heights[k * cellsX + i] = y;
				}
				foreach (var (cornerA, cornerB, cornerC) in tris)
				{
					Put(Mathf.FloorToInt((cornerA.x - minX) / Cell), Mathf.FloorToInt((cornerA.z - minZ) / Cell), cornerA.y);
					Put(Mathf.FloorToInt((cornerB.x - minX) / Cell), Mathf.FloorToInt((cornerB.z - minZ) / Cell), cornerB.y);
					Put(Mathf.FloorToInt((cornerC.x - minX) / Cell), Mathf.FloorToInt((cornerC.z - minZ) / Cell), cornerC.y);
					float determinant = (cornerB.z - cornerC.z) * (cornerA.x - cornerC.x)
						+ (cornerC.x - cornerB.x) * (cornerA.z - cornerC.z);
					if (Mathf.Abs(determinant) < 1e-10f) continue;
					int firstColumn = Mathf.FloorToInt((Mathf.Min(cornerA.x, cornerB.x, cornerC.x)
						- minX) / Cell), lastColumn = Mathf.FloorToInt((Mathf.Max(cornerA.x, cornerB.x, cornerC.x) - minX) / Cell);
					int firstRow = Mathf.FloorToInt((Mathf.Min(cornerA.z, cornerB.z, cornerC.z)
						- minZ) / Cell), lastRow = Mathf.FloorToInt((Mathf.Max(cornerA.z, cornerB.z, cornerC.z) - minZ) / Cell);
					for (int k = firstRow; k <= lastRow; k++)
						for (int i = firstColumn; i <= lastColumn; i++)
						{
							float centerX = minX + (i + 0.5f) * Cell, centerZ = minZ + (k + 0.5f) * Cell;
							float weightA = ((cornerB.z - cornerC.z) * (centerX - cornerC.x)
								+ (cornerC.x - cornerB.x) * (centerZ - cornerC.z)) / determinant;
							float weightB = ((cornerC.z - cornerA.z) * (centerX - cornerC.x)
								+ (cornerA.x - cornerC.x) * (centerZ - cornerC.z)) / determinant;
							float weightC = 1f - weightA - weightB;
							if (weightA < -1e-4f || weightB < -1e-4f || weightC < -1e-4f) continue;
							Put(i, k, weightA * cornerA.y + weightB * cornerB.y + weightC * cornerC.y);
						}
				}
			}
		}

		internal static Mesh Case127(string name)
		{
			var prof = new[] { (0f, 0f), (0f, 0.010875f), (0.080f, 0.0099f), (0.086f, 0.0065f), (CaseLength, 0.0065f) };
			const int sides = 16;
			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var tris = new List<int>();
			for (int k = 0; k + 1 < prof.Length; k++)
			{
				var (startZ, startRadius) = prof[k];
				var (endZ, endRadius) = prof[k + 1];
				int start = vertices.Count;
				for (int side = 0; side <= sides; side++)
				{
					float angle = side * Mathf.PI * 2f / sides;
					var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
					var normal = startRadius == 0f
						? Vector3.back
						: (dir * (endZ - startZ) + Vector3.forward * (startRadius - endRadius)).normalized;
					vertices.Add(dir * startRadius + Vector3.forward * startZ);
					normals.Add(normal);
					vertices.Add(dir * endRadius + Vector3.forward * endZ);
					normals.Add(normal);
				}
				for (int side = 0; side < sides; side++)
				{
					int i = start + side * 2;
					tris.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
				}
			}
			var mesh = new Mesh { name = name };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		internal static Material Brass(string name)
		{
			var shader = Shader.Find("Standard (FLIR)") ?? Shader.Find("Standard");
			var mat = new Material(shader) { name = name, color = new Color(0.71f, 0.55f, 0.27f) };
			if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.85f);
			if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.55f);
			return mat;
		}

		internal static Mesh CaseMesh(
			Vector3[] v,
			Vector3[] n,
			Vector2[] uv,
			Vector4[] tg,
			int[] tris,
			List<int> ts,
			string name,
			out string log)
		{
			var indices = ts.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] }).Distinct().ToList();
			var centroid = Vector3.zero;
			foreach (int i in indices) centroid += v[i];
			centroid /= indices.Count;
			float sumXX = 0, sumXY = 0, sumXZ = 0, sumYY = 0, sumYZ = 0, sumZZ = 0;
			foreach (int i in indices)
			{
				var offset = v[i] - centroid;
				sumXX += offset.x * offset.x;
				sumXY += offset.x * offset.y;
				sumXZ += offset.x * offset.z;
				sumYY += offset.y * offset.y;
				sumYZ += offset.y * offset.z;
				sumZZ += offset.z * offset.z;
			}
			var axis = new Vector3(1f, 0.7f, 0.3f).normalized;
			for (int k = 0;
				k < 40;
				k++)
					axis = new Vector3(
						sumXX * axis.x + sumXY * axis.y + sumXZ * axis.z,
						sumXY * axis.x + sumYY * axis.y + sumYZ * axis.z,
						sumXZ * axis.x + sumYZ * axis.y + sumZZ * axis.z).normalized;
			float Rad(int i) => Vector3.ProjectOnPlane(v[i] - centroid, axis).magnitude;
			float rPos = indices.Where(i => Vector3.Dot(v[i] - centroid, axis) > 0f).Select(Rad).DefaultIfEmpty(0f).Average();
			float rNeg = indices.Where(i => Vector3.Dot(v[i] - centroid, axis) < 0f).Select(Rad).DefaultIfEmpty(0f).Average();
			if (rPos > rNeg) axis = -axis;
			float zMin = indices.Min(i => Vector3.Dot(v[i] - centroid, axis)), length = indices.Max(i => Vector3.Dot(v[i] - centroid, axis))
				- zMin;
			var origin = centroid + axis * zMin;
			var basisX = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			var basisY = Vector3.Cross(axis, basisX);
			Vector3 Rot(Vector3 vector) => new Vector3(Vector3.Dot(vector, basisX), Vector3.Dot(vector, basisY), Vector3.Dot(vector, axis));

			var cutVertices = new List<Vector3>();
			var cutNormals = new List<Vector3>();
			var cutUvs = new List<Vector2>();
			var cutTangents = new List<Vector4>();
			var cutIndices = new List<int>();
			bool hasN = n.Length == v.Length, hasUv = uv.Length == v.Length, hasT = tg.Length == v.Length;
			(Vector3 p, Vector3 n, Vector2 uv, Vector4 t) Vert(int i)
			{
				var tangent = hasT ? tg[i] : new Vector4(1f, 0f, 0f, 1f);
				var tangentDirection = Rot(new Vector3(tangent.x, tangent.y, tangent.z));
				return (Rot(v[i] - origin), hasN
					? Rot(n[i])
					: Vector3.up, hasUv
						? uv[i]
						: Vector2.zero, new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, tangent.w));
			}
			int kept = 0;
			var poly = new List<(Vector3 p, Vector3 n, Vector2 uv, Vector4 t)>();
			foreach (int triangle in ts)
			{
				var tri = new[] { Vert(tris[triangle]), Vert(tris[triangle + 1]), Vert(tris[triangle + 2]) };
				poly.Clear();
				for (int j = 0; j < 3; j++)
				{
					var current = tri[j];
					var next = tri[(j + 1) % 3];
					bool pin = current.p.z <= CaseLength, qin = next.p.z <= CaseLength;
					if (pin) poly.Add(current);
					if (pin != qin)
					{
						float fraction = (CaseLength - current.p.z) / (next.p.z - current.p.z);
						var cutTangent = Vector4.Lerp(current.t, next.t, fraction);
						var cutDirection = new Vector3(cutTangent.x, cutTangent.y, cutTangent.z).normalized;
						poly.Add((Vector3.Lerp(
							current.p,
							next.p,
							fraction), Vector3.Lerp(current.n, next.n, fraction).normalized, Vector2.Lerp(
							current.uv,
							next.uv,
							fraction), new Vector4(
							cutDirection.x,
							cutDirection.y,
							cutDirection.z,
							current.t.w)));
					}
				}
				if (poly.Count < 3) continue;
				kept++;
				int baseIndex = cutVertices.Count;
				foreach (var vertex in poly)
				{
					cutVertices.Add(vertex.p);
					cutNormals.Add(vertex.n);
					cutUvs.Add(vertex.uv);
					cutTangents.Add(vertex.t);
				}
				for (int j = 1; j + 1 < poly.Count; j++)
				{
					cutIndices.Add(baseIndex);
					cutIndices.Add(baseIndex + j);
					cutIndices.Add(baseIndex + j + 1);
				}
			}
			var mesh = new Mesh { name = name };
			mesh.SetVertices(cutVertices);
			mesh.SetNormals(cutNormals);
			mesh.SetUVs(0, cutUvs);
			if (hasT) mesh.SetTangents(cutTangents);
			mesh.SetTriangles(cutIndices, 0);
			mesh.RecalculateBounds();
			float rBase = indices
				.Where(i => Vector3.Dot(v[i] - origin, axis) < 0.002f)
				.Select(i => Vector3.ProjectOnPlane(v[i] - origin, axis).magnitude)
				.DefaultIfEmpty(0f)
				.Max();
			log = $"spent case: the feed round's cartridge ({ts.Count} triangles, {length * 1000f:0.0} mm long, base radius {rBase * 1000f:0.0} mm) cut at {CaseLength * 1000f:0} mm: "
				+ $"{kept} faces kept, {cutIndices.Count / 3} triangles, {cutVertices.Count} vertices"
				+ (length < 0.13f || length > 0.16f ? $" (WARNING: a 12.7x108 cartridge is ~147 mm; is this the cartridge?)" : "") + ".";
			return mesh;
		}
	}
}
