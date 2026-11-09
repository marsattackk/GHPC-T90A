using System.Collections.Generic;
using System.Linq;
using Assets.DuckType.Jiggle;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace T90A.Common
{
	internal static class HatchRig
	{
		// AnimatedPart swings the base rig's bone about the base vehicle's hinge, so the lid gets its own and follows Openness.
		internal sealed class Follower : MonoBehaviour
		{
			public AnimatedPart Part;
			public Transform Swing;
			public Vector3 Axis, Lift;
			public float Angle;
			public bool Shut;
			float _last = -1f;

			void LateUpdate()
			{
				float o = Shut || Part == null ? 0f : Part.Openness;
				if (o == _last) return;
				_last = o;
				Pose(o);
			}

			internal void Pose(float o)
			{
				if (Swing == null) return;
				bool lifted = Lift.sqrMagnitude > 1e-8f;
				float rise = lifted ? Mathf.Clamp01(o / RigMap.LiftShare) : 0f;
				float turn = lifted ? Mathf.Clamp01((o - RigMap.LiftShare) / (1f - RigMap.LiftShare)) : Mathf.Clamp01(o);
				Swing.localRotation = Quaternion.AngleAxis(Angle * turn, Axis);
				Swing.localPosition = Lift * rise;
			}
		}

		static AnimatedPart WakeDormant(Transform root, AnimatedPart[] parts, string name)
		{
			var part = parts.FirstOrDefault(a => a.Transform == null && a.name == name && a.StartTransform != null && a.EndTransform != null);
			var hull = RigBones.Hull(root);
			if (part == null || hull == null) return null;
			var bone = new GameObject(name).transform;
			bone.SetParent(hull, false);
			bone.SetPositionAndRotation(part.StartTransform.position, part.StartTransform.rotation);
			part.Transform = bone;
			part.gameObject.SetActive(true);
			return part;
		}

		internal static void Apply(Transform root, RigMap map, string prefix, string tag)
		{
			if (map == null) return;
			var parts = root.GetComponentsInChildren<AnimatedPart>(true);
			Transform Find(string group) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == prefix + group)
				?? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == group);
			foreach (var h in map.Hatches)
			{
				var lid = Find(h.Group);
				var part = parts.FirstOrDefault(a => a.Transform != null && a.Transform.name == h.BaseBone);
				if (lid == null) continue;
				if (part == null) part = WakeDormant(root, parts, h.BaseBone);
				if (part == null)
				{
					MelonLogger.Warning($"{tag}: the {h.Role.ToLowerInvariant()}'s hatch has no opening animation on this vehicle; it stays closed.");
					continue;
				}
				var parent = part.Transform.parent;
				var hinge = new GameObject($"{prefix}{h.Group} hinge").transform;
				hinge.SetParent(parent, false);
				hinge.SetPositionAndRotation(root.TransformPoint(h.Hinge), root.rotation);
				var swing = new GameObject($"{prefix}{h.Group} swing").transform;
				swing.SetParent(hinge, false);
				lid.SetParent(swing, true);
				lid.name = prefix + h.Group;
				var f = hinge.gameObject.AddComponent<Follower>();
				f.Part = part; f.Swing = swing; f.Axis = h.Axis.normalized; f.Lift = h.Lift; f.Angle = h.Angle;
				f.Pose(part.Openness);
				var aar = part.OtherAarTransforms ?? new Transform[0];
				if (!aar.Contains(swing)) part.OtherAarTransforms = aar.Concat(new[] { swing }).ToArray();
			}
			if (map.RunningGear == "follow") GearFollow.Apply(root, map, prefix, Find);
			else if (map.RunningGear == "port")
			{
				foreach (var g in map.GearParts)
				{
					var vis = Find(g.Group);
					var bone = root.Find(g.Bone);
					if (vis == null || bone == null) continue;
					vis.SetParent(bone, false);
					vis.localPosition = Vector3.zero;
					vis.localRotation = Quaternion.Inverse(g.RestRot);
					vis.localScale = Vector3.one;
					vis.name = prefix + g.Group;
				}
			}
			foreach (var w in map.Whips)
			{
				var vis = Find(w.Group);
				if (vis == null) continue;
				vis.name = prefix + w.Group;
				Whip(root, vis, w, prefix);
			}
			FlapKicks kicks = null;
			foreach (var f in map.Flaps)
			{
				var vis = Find(f.Group);
				if (vis == null) continue;
				vis.name = prefix + f.Group;
				Flap(root, vis, f, prefix, out var j);
				if (j == null) continue;
				if (kicks == null) kicks = root.gameObject.AddComponent<FlapKicks>();
				kicks.Flaps.Add(j);
			}
		}

		// Jiggle only reacts to its own bone moving, firing and hits do nothing to it.
		internal sealed class FlapKicks : MonoBehaviour
		{
			internal readonly List<Jiggle> Flaps = new List<Jiggle>();
			internal const float BlastDegS = 400f, BlastRefM = 4f, RecoilDegS = 240f, HitDegS = 300f, HitRefM = 1f, HitMomentum = 20000f;
			static readonly HarmonyLib.AccessTools.FieldRef<Jiggle, Quaternion> Torque =
				Reflect.FieldRefAccess<Jiggle, Quaternion>("m_Torque");
			Vehicle _unit;

			void Start()
			{
				_unit = GetComponent<Vehicle>();
				if (_unit != null) _unit.Struck += OnStruck;
				foreach (var w in GetComponentsInChildren<WeaponSystem>(true))
				{
					if (w.Impulse < 20000f) continue;
					var gun = w;
					gun.Fired += (a, r) => OnFired(gun);
				}
			}

			void OnDestroy() { if (_unit != null) _unit.Struck -= OnStruck; }

			static float Kick(Jiggle j, Vector3 dir, float degS)
			{
				if (Torque == null || j == null) return 0f;
				float s = Vector3.Dot(dir.normalized, j.transform.right) * degS;
				if (Mathf.Abs(s) < 0.5f) return 0f;
				// m_Torque is a rotation per millisecond.
				Torque(j) = Quaternion.AngleAxis(s * 0.001f, Vector3.forward) * Torque(j);
				return Mathf.Abs(s);
			}

			// In the same frame the blast and the jolt cancel out on the front and rear nets.
			internal const float RecoilDelayS = 0.1f;

			void OnFired(WeaponSystem gun)
			{
				var m = gun.MuzzleIdentity != null ? gun.MuzzleIdentity : gun.transform;
				float most = 0f;
				foreach (var j in Flaps)
				{
					if (j == null) continue;
					var to = j.transform.position - m.position;
					most = Mathf.Max(most, Kick(j, to, BlastDegS / (1f + (to.magnitude / BlastRefM) * (to.magnitude / BlastRefM))));
				}
				StartCoroutine(Jolt(m.forward));
			}

			System.Collections.IEnumerator Jolt(Vector3 forward)
			{
				yield return new WaitForSeconds(RecoilDelayS);
				float most = 0f;
				foreach (var j in Flaps) if (j != null) most = Mathf.Max(most, Kick(j, forward, RecoilDegS));
			}

			void OnStruck(GHPC.IUnit shooter, AmmoType ammo, Vector3 point)
			{
				if (ammo == null || ammo.Name == "spall") return;
				float k = Mathf.Clamp(ammo.Mass * ammo.MuzzleVelocity / HitMomentum, 0.3f, 1.5f), most = 0f;
				foreach (var j in Flaps)
				{
					if (j == null) continue;
					var away = j.transform.position - point;
					most = Mathf.Max(most, Kick(j, away, HitDegS * k * Mathf.Exp(-away.magnitude / HitRefM)));
				}
			}
		}

		const float SkirtCom = 0.3f, SkirtInertia = 0.483f, SkirtSpring = 0.305f, SkirtDamping = 0.771f, SkirtGravity = 0.601f, SkirtLimit = 33.2f;

		internal static float FlapWeight(float s, float depth, float half, float taper, float ramp)
		{
			float S(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
			float ends = taper > 0f ? S((half - Mathf.Abs(s)) / taper) : 1f;
			return ends * S(depth / Mathf.Max(ramp, 1e-4f));
		}

		static void Flap(Transform root, Transform vis, RigMap.Flap f, string prefix, out Jiggle jiggle)
		{
			jiggle = null;
			var mf = vis.GetComponent<MeshFilter>();
			var mr = vis.GetComponent<MeshRenderer>();
			if (mf == null || mr == null || mf.sharedMesh == null) return;
			var along = vis.InverseTransformDirection(root.TransformDirection(f.B - f.A)).normalized;
			var up = Vector3.ProjectOnPlane(vis.InverseTransformDirection(root.up), along).normalized;
			float half = 0.5f * (f.B - f.A).magnitude;
			var foot = new GameObject($"{prefix}{f.Group} foot").transform;
			foot.SetParent(vis.parent, false);
			foot.localPosition = vis.localPosition; foot.localRotation = vis.localRotation;
			var hinge = new GameObject($"{prefix}{f.Group} hinge").transform;
			hinge.SetParent(foot, false);
			hinge.localRotation = Quaternion.LookRotation(along, up);
			var mesh = Object.Instantiate(mf.sharedMesh);
			mesh.name = mf.sharedMesh.name + " (flap)";
			var v = mesh.vertices;
			var bw = new BoneWeight[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				float w = FlapWeight(Vector3.Dot(v[i], along), -Vector3.Dot(v[i], up), half, f.Taper, f.Ramp);
				bw[i] = new BoneWeight { boneIndex0 = 1, weight0 = w, boneIndex1 = 0, weight1 = 1f - w };
			}
			mesh.boneWeights = bw;
			var bones = new[] { foot, hinge };
			mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * vis.localToWorldMatrix).ToArray();
			var mats = mr.sharedMaterials;
			var shadows = mr.shadowCastingMode;
			Object.DestroyImmediate(mr);
			Object.DestroyImmediate(mf);
			var smr = vis.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = bones;
			smr.rootBone = foot;
			smr.sharedMaterials = mats;
			smr.shadowCastingMode = shadows;
			var bounds = mesh.bounds; bounds.Expand(0.4f);
			smr.localBounds = bounds;
			NetCut.Register(root, vis, smr, along, half);
			float limit = Mathf.Min(SkirtLimit, f.Limit > 0f ? f.Limit : SkirtLimit);
			var j = hinge.gameObject.AddComponent<Jiggle>();
			j.UseCenterOfMass = true;
			j.CenterOfMass = new Vector3(0f, -SkirtCom, 0f);
			j.CenterOfMassInertia = f.Lag > 0f ? f.Lag : SkirtInertia;
			// Not the skirt's 1: that holds the bone's world turn and the nets trail a slewing turret, twisted.
			j.RotationInertia = 0f;
			j.SpringStrength = SkirtSpring;
			j.Dampening = SkirtDamping;
			j.Gravity = SkirtGravity;
			j.Hinge = true;
			j.HingeAngle = 0f;
			j.UseAngleLimit = true;
			j.AngleLimit = limit;
			j.UseSoftLimit = false;
			j.ShowViewportGizmos = false;
			jiggle = j;
		}

		// Centres of mass not scaled to the whip's length, so it bends as far in angle as the R-123.
		const float R123Second = 0.1f, R123Com1 = 1.5f, R123Com2 = 3.0f, R123Inertia1 = 1f, R123Inertia2 = 0.681f, R123Spring = 0.4f, R123Damping = 0.4f;

		// Skinned straight on the springs it kinks at the foot and bends in an S. A skinned bend only shows at vertices,
		// hence the extra rings.
		const int BendBones = 12;
		const float SliceStep = 0.025f;

		static void Whip(Transform root, Transform vis, RigMap.Whip w, string prefix)
		{
			var mf = vis.GetComponent<MeshFilter>();
			var mr = vis.GetComponent<MeshRenderer>();
			if (mf == null || mr == null || mf.sharedMesh == null) return;
			var dir = vis.InverseTransformDirection(root.TransformDirection(w.Tip - w.Base)).normalized;
			float flex = Mathf.Max(0.05f, (w.Tip - w.Base).magnitude - w.Rigid);
			var foot = new GameObject($"{prefix}{w.Group} foot").transform;
			foot.SetParent(vis.parent, false);
			foot.localPosition = vis.localPosition; foot.localRotation = vis.localRotation;
			var s1 = new GameObject($"{prefix}{w.Group} spring 1").transform;
			s1.SetParent(foot, false);
			s1.localPosition = dir * w.Rigid;
			var s2 = new GameObject($"{prefix}{w.Group} spring 2").transform;
			s2.SetParent(s1, false);
			s2.localPosition = dir * (R123Second * flex);
			float step = flex / BendBones;
			var chain = new Transform[BendBones + 1];
			for (int k = 0; k <= BendBones; k++)
			{
				chain[k] = new GameObject($"{prefix}{w.Group} bend {k}").transform;
				chain[k].SetParent(k == 0 ? foot : chain[k - 1], false);
				chain[k].localPosition = k == 0 ? dir * w.Rigid : dir * step;
			}
			var mesh = Sliced(mf.sharedMesh, dir, w.Rigid, w.Rigid + flex, SliceStep);
			mesh.name = mf.sharedMesh.name + " (whip)";
			var v = mesh.vertices;
			var bw = new BoneWeight[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				float u = (Vector3.Dot(v[i], dir) - w.Rigid) / step;
				if (u <= 0f) { bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f }; continue; }
				int k = Mathf.Min((int)u, BendBones - 1);
				float t = Mathf.Clamp01(u - k);
				bw[i] = t < 0.5f ? new BoneWeight { boneIndex0 = 1 + k, weight0 = 1f - t, boneIndex1 = 2 + k, weight1 = t }
					: new BoneWeight { boneIndex0 = 2 + k, weight0 = t, boneIndex1 = 1 + k, weight1 = 1f - t };
			}
			mesh.boneWeights = bw;
			var bones = new[] { foot }.Concat(chain).ToArray();
			mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * vis.localToWorldMatrix).ToArray();
			var mats = mr.sharedMaterials;
			var shadows = mr.shadowCastingMode;
			Object.DestroyImmediate(mr);
			Object.DestroyImmediate(mf);
			var smr = vis.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = bones;
			smr.rootBone = foot;
			smr.sharedMaterials = mats;
			smr.shadowCastingMode = shadows;
			var bounds = mesh.bounds; bounds.Expand(flex);
			smr.localBounds = bounds;
			Spring(s1, dir * R123Com1, R123Inertia1);
			Spring(s2, dir * R123Com2, R123Inertia2);
			var bend = foot.gameObject.AddComponent<WhipBend>();
			bend.Foot = foot; bend.Top = s2; bend.Chain = chain; bend.Dir = dir;
			bend.Rigid = w.Rigid; bend.TipFromTop = w.Rigid + flex - (w.Rigid + R123Second * flex);
		}

		internal sealed class WhipBend : MonoBehaviour
		{
			public Transform Foot, Top;
			public Transform[] Chain;
			public Vector3 Dir;
			public float Rigid, TipFromTop;
			const float MaxDeg = 80f;

			void OnEnable() => Application.onBeforeRender += Bend;
			void OnDisable() => Application.onBeforeRender -= Bend;

			void Bend()
			{
				if (Foot == null || Top == null) return;
				var chord = Foot.InverseTransformPoint(Top.TransformPoint(Dir * TipFromTop)) - Dir * Rigid;
				var axis = Vector3.Cross(Dir, chord);
				float phi = axis.sqrMagnitude < 1e-10f ? 0f : Mathf.Min(MaxDeg, 1.5f * Vector3.Angle(Dir, chord));
				axis = phi > 0f ? axis.normalized : Vector3.right;
				int n = Chain.Length - 1;
				float last = 0f;
				for (int k = 0; k <= n; k++)
				{
					float u = (float)k / n, a = phi * (2f * u - u * u);
					Chain[k].localRotation = Quaternion.AngleAxis(a - last, axis);
					last = a;
				}
			}
		}

		static Mesh Sliced(Mesh src, Vector3 dir, float from, float to, float step)
		{
			var pos = new List<Vector3>(src.vertices);
			var nrm = new List<Vector3>(src.normals);
			var tan = new List<Vector4>(src.tangents);
			var uv = new List<Vector2>(src.uv);
			var uv2 = new List<Vector2>(src.uv2);
			var col = new List<Color>(src.colors);
			var h = pos.Select(p => Vector3.Dot(p, dir)).ToList();
			var cuts = new Dictionary<(int, int, int), int>();
			int Cut(int[] tri, Vector3 bary, int plane)
			{
				int i0 = bary.x > 0f ? 0 : 1, i1 = bary.z > 0f ? 2 : 1;
				int a = tri[i0], b = tri[i1];
				var key = (Mathf.Min(a, b), Mathf.Max(a, b), plane);
				if (cuts.TryGetValue(key, out int idx)) return idx;
				idx = pos.Count;
				var c = new[] { bary.x, bary.y, bary.z };
				Vector3 P(List<Vector3> l) => l[tri[0]] * c[0] + l[tri[1]] * c[1] + l[tri[2]] * c[2];
				pos.Add(P(pos));
				h.Add(Vector3.Dot(pos[idx], dir));
				if (nrm.Count > 0) nrm.Add(P(nrm).normalized);
				if (tan.Count > 0)
				{
					var t = (Vector3)tan[tri[0]] * c[0] + (Vector3)tan[tri[1]] * c[1] + (Vector3)tan[tri[2]] * c[2];
					tan.Add(new Vector4(t.normalized.x, t.normalized.y, t.normalized.z, tan[tri[0]].w));
				}
				if (uv.Count > 0) uv.Add(uv[tri[0]] * c[0] + uv[tri[1]] * c[1] + uv[tri[2]] * c[2]);
				if (uv2.Count > 0) uv2.Add(uv2[tri[0]] * c[0] + uv2[tri[1]] * c[1] + uv2[tri[2]] * c[2]);
				if (col.Count > 0) col.Add(col[tri[0]] * c[0] + col[tri[1]] * c[1] + col[tri[2]] * c[2]);
				cuts[key] = idx;
				return idx;
			}
			var subs = new List<List<int>>();
			for (int s = 0; s < src.subMeshCount; s++)
			{
				var outTris = new List<int>();
				var t3 = src.GetTriangles(s);
				for (int i = 0; i < t3.Length; i += 3)
				{
					var tri = new[] { t3[i], t3[i + 1], t3[i + 2] };
					float lo = Mathf.Max(from, tri.Min(x => h[x])), hi = Mathf.Min(to, tri.Max(x => h[x]));
					int k0 = Mathf.FloorToInt((lo - from) / step) + 1, k1 = Mathf.CeilToInt((hi - from) / step) - 1;
					if (k1 < k0) { outTris.AddRange(tri); continue; }
					var poly = new List<(int v, Vector3 b, float h)> { (tri[0], Vector3.right, h[tri[0]]), (tri[1], Vector3.up, h[tri[1]]), (tri[2], Vector3.forward, h[tri[2]]) };
					for (int k = k0; k <= k1 + 1; k++)
					{
						float below = k <= k1 ? from + k * step : float.PositiveInfinity;
						var lower = new List<(int v, Vector3 b, float h)>();
						var upper = new List<(int v, Vector3 b, float h)>();
						for (int j = 0; j < poly.Count; j++)
						{
							var p = poly[j]; var q = poly[(j + 1) % poly.Count];
							if (p.h <= below) lower.Add(p);
							if (p.h >= below) upper.Add(p);
							if ((p.h < below && q.h > below) || (p.h > below && q.h < below))
							{
								float f = (below - p.h) / (q.h - p.h);
								var b = Vector3.Lerp(p.b, q.b, f);
								var x = (Cut(tri, b, k), b, below);
								lower.Add(x); upper.Add(x);
							}
						}
						for (int j = 1; j + 1 < lower.Count; j++) outTris.AddRange(new[] { lower[0].v, lower[j].v, lower[j + 1].v });
						poly = upper;
						if (poly.Count < 3) break;
					}
				}
				subs.Add(outTris);
			}
			var m = new Mesh { indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
			m.SetVertices(pos);
			if (nrm.Count == pos.Count) m.SetNormals(nrm);
			if (tan.Count == pos.Count) m.SetTangents(tan);
			if (uv.Count == pos.Count) m.SetUVs(0, uv);
			if (uv2.Count == pos.Count) m.SetUVs(1, uv2);
			if (col.Count == pos.Count) m.SetColors(col);
			m.subMeshCount = subs.Count;
			for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
			m.RecalculateBounds();
			return m;
		}

		static void Spring(Transform bone, Vector3 centreOfMass, float inertia)
		{
			var j = bone.gameObject.AddComponent<Jiggle>();
			j.UseCenterOfMass = true;
			j.CenterOfMass = centreOfMass;
			j.CenterOfMassInertia = inertia;
			j.RotationInertia = 1f;
			j.SpringStrength = R123Spring;
			j.Dampening = R123Damping;
			j.Gravity = 0f;
			j.UseAngleLimit = false;
			j.ShowViewportGizmos = false;
		}
	}
}
