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
	// Hatches, flaps and the radio whip, driven by the base vehicle's animation and the rig map.
	internal static class HatchRig
	{
		// AnimatedPart swings the base rig's bone about the base vehicle's hinge, so the lid gets its own and follows Openness.
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
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
				float openness = Shut || Part == null ? 0f : Part.Openness;
				if (openness == _last) return;
				_last = openness;
				Pose(openness);
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
#pragma warning restore CS0649

		static AnimatedPart WakeDormant(Transform root, AnimatedPart[] parts, string name)
		{
			var part = parts.FirstOrDefault(a => a.Transform == null
				&& a.name == name
				&& a.StartTransform != null
				&& a.EndTransform != null);
			var hull = RigBones.Hull(root);
			if (part == null || hull == null) return null;
			var bone = new GameObject(name).transform;
			bone.SetParent(hull, false);
			bone.SetPositionAndRotation(part.StartTransform.position, part.StartTransform.rotation);
			part.Transform = bone;
			part.gameObject.SetActive(true);
			return part;
		}

		internal static List<string> Apply(Transform root, RigMap map, string prefix, string tag)
		{
			var log = new List<string>();
			if (map == null)
			{
				log.Add("no rig map in the bundle; hatches stay closed");
				return log;
			}
			var parts = root.GetComponentsInChildren<AnimatedPart>(true);
			Transform Find(string group)
				=> root.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == prefix + group)
					?? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == group);
			string P(Vector3 worldPoint)
			{
				var localPoint = root.InverseTransformPoint(worldPoint);
				return $"({localPoint.x:0.000}, {localPoint.y:0.000}, {localPoint.z:0.000})";
			}
			foreach (var hatch in map.Hatches)
			{
				var lid = Find(hatch.Group);
				var part = parts.FirstOrDefault(a => a.Transform != null
					&& a.Transform.name == hatch.BaseBone);
				if (lid == null)
				{
					log.Add($"{hatch.Wt} ({hatch.Role}): visual '{hatch.Group}' NOT FOUND");
					continue;
				}
				string woke = "";
				if (part == null)
				{
					part = WakeDormant(root, parts, hatch.BaseBone);
					if (part != null) woke = " (dormant AnimatedPart woken on a stand-in bone under the hull)";
				}
				if (part == null)
				{
					log.Add($"{hatch.Wt} ({hatch.Role}): no AnimatedPart on '{hatch.BaseBone}'; lid stays closed");
					MelonLogger.Warning($"{tag}: the {hatch.Role.ToLowerInvariant()}'s hatch has no opening animation on this vehicle; it stays closed.");
					continue;
				}
				var parent = part.Transform.parent;
				var hinge = new GameObject($"{prefix}{hatch.Group} hinge").transform;
				hinge.SetParent(parent, false);
				hinge.SetPositionAndRotation(root.TransformPoint(hatch.Hinge), root.rotation);
				var swing = new GameObject($"{prefix}{hatch.Group} swing").transform;
				swing.SetParent(hinge, false);
				lid.SetParent(swing, true);
				lid.name = prefix + hatch.Group;
				var follower = hinge.gameObject.AddComponent<Follower>();
				follower.Part = part;
				follower.Swing = swing;
				follower.Axis = hatch.Axis.normalized;
				follower.Lift = hatch.Lift;
				follower.Angle = hatch.Angle;
				follower.Pose(part.Openness);
				var aar = part.OtherAarTransforms ?? new Transform[0];
				if (!aar.Contains(swing)) part.OtherAarTransforms = aar.Concat(new[] { swing }).ToArray();
				log.Add($"{hatch.Wt} ({hatch.Role}): lid {lid.name} on its hinge {P(hinge.position)} under {parent.name}, follows AnimatedPart '{part.name}' (bone {hatch.BaseBone}, openness {part.Openness:0.00}): " +
					$"{hatch.Angle:0.#} deg about ({hatch.Axis.x:0.000}, {hatch.Axis.y:0.000}, {hatch.Axis.z:0.000})" + (hatch.Lift.sqrMagnitude > 0f
						? $" after rising {hatch.Lift.magnitude * 100f:0.#} cm"
						: "") + woke);
			}
			if (map.RunningGear == "follow") log.AddRange(GearFollow.Apply(root, map, prefix, Find));
			else if (map.RunningGear == "port")
			{
				int count = 0;
				var miss = new List<string>();
				foreach (var gear in map.GearParts)
				{
					var vis = Find(gear.Group);
					var bone = root.Find(gear.Bone);
					if (vis == null || bone == null)
					{
						miss.Add(gear.Wt);
						continue;
					}
					vis.SetParent(bone, false);
					vis.localPosition = Vector3.zero;
					vis.localRotation = Quaternion.Inverse(gear.RestRot);
					vis.localScale = Vector3.one;
					vis.name = prefix + gear.Group;
					count++;
				}
				log.Add($"running gear: {count} of {map.GearParts.Length} War Thunder parts on their rig bones"
					+ (miss.Count > 0 ? $"; NOT FOUND: {string.Join(", ", miss)}" : ""));
			}
			else log.Add("running gear: the base vehicle's");
			foreach (var whip in map.Whips)
			{
				var vis = Find(whip.Group);
				if (vis == null)
				{
					log.Add($"{whip.Wt}: visual '{whip.Group}' NOT FOUND");
					continue;
				}
				vis.name = prefix + whip.Group;
				log.Add($"{whip.Wt}: " + Whip(root, vis, whip, prefix));
			}
			var flaps = new List<string>();
			FlapKicks kicks = null;
			foreach (var flap in map.Flaps)
			{
				var vis = Find(flap.Group);
				if (vis == null)
				{
					flaps.Add($"{flap.Wt}: visual '{flap.Group}' NOT FOUND");
					continue;
				}
				vis.name = prefix + flap.Group;
				flaps.Add(Flap(root, vis, flap, prefix, out var jiggle));
				if (jiggle == null) continue;
				if (kicks == null)
				{
					kicks = root.gameObject.AddComponent<FlapKicks>();
					kicks.Tag = tag;
				}
				kicks.Flaps.Add(jiggle);
			}
			if (map.Flaps.Length > 0)
				log.Add($"{map.Flaps.Length} flaps (T-72M1 skirt flap settings, kicked by the main gun and hits): "
					+ string.Join("; ", flaps));
			return log;
		}

		// Jiggle only reacts to its own bone moving. Firing and hits do nothing to it.
		internal sealed class FlapKicks : MonoBehaviour
		{
			internal readonly List<Jiggle> Flaps = new List<Jiggle>();
			internal string Tag;
			internal const float BlastDegS = 400f, BlastRefM = 4f, RecoilDegS = 240f, HitDegS = 300f, HitRefM = 1f, HitMomentum = 20000f;
			internal int Shots, Hits;
			internal float ShotMax, JoltMax, HitMax;
			internal string Guns = "";
			static readonly HarmonyLib.AccessTools.FieldRef<Jiggle, Quaternion> Torque =
				Reflect.FieldRefAccess<Jiggle, Quaternion>("m_Torque");
			Vehicle _unit;

			void Start()
			{
				_unit = GetComponent<Vehicle>();
				if (_unit != null) _unit.Struck += OnStruck;
				foreach (var weapon in GetComponentsInChildren<WeaponSystem>(true))
				{
					if (weapon.Impulse < 20000f) continue;
					var gun = weapon;
					gun.Fired += (a, r) => OnFired(gun);
					Guns += (Guns.Length > 0 ? ", " : "") + gun.name;
				}
			}

			void OnDestroy() { if (_unit != null) _unit.Struck -= OnStruck; }

			static float Kick(Jiggle j, Vector3 dir, float degS)
			{
				if (Torque == null || j == null) return 0f;
				float swing = Vector3.Dot(dir.normalized, j.transform.right) * degS;
				if (Mathf.Abs(swing) < 0.5f) return 0f;
				// m_Torque is a rotation per millisecond.
				Torque(j) = Quaternion.AngleAxis(swing * 0.001f, Vector3.forward) * Torque(j);
				return Mathf.Abs(swing);
			}

			// In the same frame the blast and the jolt cancel out on the front and rear nets.
			internal const float RecoilDelayS = 0.1f;

			void OnFired(WeaponSystem gun)
			{
				var muzzle = gun.MuzzleIdentity != null ? gun.MuzzleIdentity : gun.transform;
				float most = 0f;
				foreach (var jiggle in Flaps)
				{
					if (jiggle == null) continue;
					var offset = jiggle.transform.position - muzzle.position;
					most = Mathf.Max(
						most,
						Kick(jiggle, offset, BlastDegS / (1f + (offset.magnitude / BlastRefM) * (offset.magnitude / BlastRefM))));
				}
				Shots++;
				ShotMax = Mathf.Max(ShotMax, most);
				StartCoroutine(Jolt(muzzle.forward));
			}

			System.Collections.IEnumerator Jolt(Vector3 forward)
			{
				yield return new WaitForSeconds(RecoilDelayS);
				float most = 0f;
				foreach (var jiggle in Flaps) if (jiggle != null) most = Mathf.Max(most, Kick(jiggle, forward, RecoilDegS));
				JoltMax = Mathf.Max(JoltMax, most);
			}

			void OnStruck(GHPC.IUnit shooter, AmmoType ammo, Vector3 point)
			{
				if (ammo == null || ammo.Name == "spall") return;
				float k = Mathf.Clamp(ammo.Mass * ammo.MuzzleVelocity / HitMomentum, 0.3f, 1.5f), most = 0f;
				foreach (var jiggle in Flaps)
				{
					if (jiggle == null) continue;
					var away = jiggle.transform.position - point;
					most = Mathf.Max(most, Kick(jiggle, away, HitDegS * k * Mathf.Exp(-away.magnitude / HitRefM)));
				}
				Hits++;
				HitMax = Mathf.Max(HitMax, most);
			}
		}

		const float SkirtCom = 0.3f,
			SkirtInertia = 0.483f,
			SkirtSpring = 0.305f,
			SkirtDamping = 0.771f,
			SkirtGravity = 0.601f,
			SkirtLimit = 33.2f;

		internal static float FlapWeight(float s, float depth, float half, float taper, float ramp)
		{
			float S(float fraction)
			{
				fraction = Mathf.Clamp01(fraction);
				return fraction * fraction * (3f - 2f * fraction);
			}
			float ends = taper > 0f ? S((half - Mathf.Abs(s)) / taper) : 1f;
			return ends * S(depth / Mathf.Max(ramp, 1e-4f));
		}

		static string Flap(Transform root, Transform vis, RigMap.Flap f, string prefix, out Jiggle jiggle)
		{
			jiggle = null;
			var meshFilter = vis.GetComponent<MeshFilter>();
			var meshRenderer = vis.GetComponent<MeshRenderer>();
			if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null) return $"{f.Group} no mesh, drawn rigid";
			var along = vis.InverseTransformDirection(root.TransformDirection(f.B - f.A)).normalized;
			var up = Vector3.ProjectOnPlane(vis.InverseTransformDirection(root.up), along).normalized;
			float half = 0.5f * (f.B - f.A).magnitude;
			var foot = new GameObject($"{prefix}{f.Group} foot").transform;
			foot.SetParent(vis.parent, false);
			foot.localPosition = vis.localPosition;
			foot.localRotation = vis.localRotation;
			var hinge = new GameObject($"{prefix}{f.Group} hinge").transform;
			hinge.SetParent(foot, false);
			hinge.localRotation = Quaternion.LookRotation(along, up);
			var mesh = Object.Instantiate(meshFilter.sharedMesh);
			mesh.name = meshFilter.sharedMesh.name + " (flap)";
			var vertices = mesh.vertices;
			var weights = new BoneWeight[vertices.Length];
			int swinging = 0;
			for (int i = 0; i < vertices.Length; i++)
			{
				float weight = FlapWeight(Vector3.Dot(vertices[i], along), -Vector3.Dot(vertices[i], up), half, f.Taper, f.Ramp);
				weights[i] = new BoneWeight { boneIndex0 = 1, weight0 = weight, boneIndex1 = 0, weight1 = 1f - weight };
				if (weight > 0.5f) swinging++;
			}
			mesh.boneWeights = weights;
			var bones = new[] { foot, hinge };
			mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * vis.localToWorldMatrix).ToArray();
			var mats = meshRenderer.sharedMaterials;
			var shadows = meshRenderer.shadowCastingMode;
			Object.DestroyImmediate(meshRenderer);
			Object.DestroyImmediate(meshFilter);
			var smr = vis.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = bones;
			smr.rootBone = foot;
			smr.sharedMaterials = mats;
			smr.shadowCastingMode = shadows;
			var bounds = mesh.bounds;
			bounds.Expand(0.4f);
			smr.localBounds = bounds;
			NetCut.Register(root, vis, smr, along, half);
			float limit = Mathf.Min(SkirtLimit, f.Limit > 0f ? f.Limit : SkirtLimit);
			var spring = hinge.gameObject.AddComponent<Jiggle>();
			spring.UseCenterOfMass = true;
			spring.CenterOfMass = new Vector3(0f, -SkirtCom, 0f);
			spring.CenterOfMassInertia = f.Lag > 0f ? f.Lag : SkirtInertia;
			// Not the skirt's 1: that holds the bone's world turn and the nets trail a slewing turret, twisted.
			spring.RotationInertia = 0f;
			spring.SpringStrength = SkirtSpring;
			spring.Dampening = SkirtDamping;
			spring.Gravity = SkirtGravity;
			spring.Hinge = true;
			spring.HingeAngle = 0f;
			spring.UseAngleLimit = true;
			spring.AngleLimit = limit;
			spring.UseSoftLimit = false;
			spring.ShowViewportGizmos = false;
			jiggle = spring;
			return $"{vis.name} {2f * half:0.00} m, {swinging} of {vertices.Length} verts swinging, limit {limit:0.#} deg"
				+ (f.Lag > 0f ? $", centre-of-mass lag {f.Lag:0.###} (skirt {SkirtInertia})" : "");
		}

		// Centres of mass not scaled to the whip's length, so it bends as far in angle as the R-123.
		const float R123Second = 0.1f,
			R123Com1 = 1.5f,
			R123Com2 = 3.0f,
			R123Inertia1 = 1f,
			R123Inertia2 = 0.681f,
			R123Spring = 0.4f,
			R123Damping = 0.4f;

		// Skinned straight on the springs it kinks at the foot and bends in an S. A skinned bend only shows at the
		// vertices, which is what the extra rings are for.
		const int BendBones = 12;
		const float SliceStep = 0.025f;

		static string Whip(Transform root, Transform vis, RigMap.Whip w, string prefix)
		{
			var meshFilter = vis.GetComponent<MeshFilter>();
			var meshRenderer = vis.GetComponent<MeshRenderer>();
			if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null) return "no mesh; drawn rigid";
			var dir = vis.InverseTransformDirection(root.TransformDirection(w.Tip - w.Base)).normalized;
			float flex = Mathf.Max(0.05f, (w.Tip - w.Base).magnitude - w.Rigid);
			var foot = new GameObject($"{prefix}{w.Group} foot").transform;
			foot.SetParent(vis.parent, false);
			foot.localPosition = vis.localPosition;
			foot.localRotation = vis.localRotation;
			var firstSpring = new GameObject($"{prefix}{w.Group} spring 1").transform;
			firstSpring.SetParent(foot, false);
			firstSpring.localPosition = dir * w.Rigid;
			var secondSpring = new GameObject($"{prefix}{w.Group} spring 2").transform;
			secondSpring.SetParent(firstSpring, false);
			secondSpring.localPosition = dir * (R123Second * flex);
			float step = flex / BendBones;
			var chain = new Transform[BendBones + 1];
			for (int k = 0; k <= BendBones; k++)
			{
				chain[k] = new GameObject($"{prefix}{w.Group} bend {k}").transform;
				chain[k].SetParent(k == 0 ? foot : chain[k - 1], false);
				chain[k].localPosition = k == 0 ? dir * w.Rigid : dir * step;
			}
			int before = meshFilter.sharedMesh.vertexCount;
			var mesh = Sliced(meshFilter.sharedMesh, dir, w.Rigid, w.Rigid + flex, SliceStep);
			mesh.name = meshFilter.sharedMesh.name + " (whip)";
			var vertices = mesh.vertices;
			var weights = new BoneWeight[vertices.Length];
			int fixedVerts = 0;
			for (int i = 0; i < vertices.Length; i++)
			{
				float chainPosition = (Vector3.Dot(vertices[i], dir) - w.Rigid) / step;
				if (chainPosition <= 0f)
				{
					weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
					fixedVerts++;
					continue;
				}
				int k = Mathf.Min((int)chainPosition, BendBones - 1);
				float blend = Mathf.Clamp01(chainPosition - k);
				weights[i] = blend < 0.5f ? new BoneWeight { boneIndex0 = 1 + k, weight0 = 1f - blend, boneIndex1 = 2 + k, weight1 = blend }
					: new BoneWeight { boneIndex0 = 2 + k, weight0 = blend, boneIndex1 = 1 + k, weight1 = 1f - blend };
			}
			mesh.boneWeights = weights;
			var bones = new[] { foot }.Concat(chain).ToArray();
			mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * vis.localToWorldMatrix).ToArray();
			var mats = meshRenderer.sharedMaterials;
			var shadows = meshRenderer.shadowCastingMode;
			Object.DestroyImmediate(meshRenderer);
			Object.DestroyImmediate(meshFilter);
			var smr = vis.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = bones;
			smr.rootBone = foot;
			smr.sharedMaterials = mats;
			smr.shadowCastingMode = shadows;
			var bounds = mesh.bounds;
			bounds.Expand(flex);
			smr.localBounds = bounds;
			Spring(firstSpring, dir * R123Com1, R123Inertia1);
			Spring(secondSpring, dir * R123Com2, R123Inertia2);
			var bend = foot.gameObject.AddComponent<WhipBend>();
			bend.Foot = foot;
			bend.Top = secondSpring;
			bend.Chain = chain;
			bend.Dir = dir;
			bend.Rigid = w.Rigid;
			bend.TipFromTop = w.Rigid + flex - (w.Rigid + R123Second * flex);
			return $"whip {vis.name}: {fixedVerts} of {vertices.Length} verts fixed (lowest {w.Rigid * 100f:0.#} cm; {before} verts before a ring every {SliceStep * 100f:0.#} cm), " +
				$"{flex:0.00} m flexible drawn on {BendBones} bend bones; springs at {w.Rigid:0.000} / {w.Rigid + R123Second * flex:0.000} m, " +
				$"centres of mass {R123Com1:0.00} / {R123Com2:0.00} m out (R-123 settings)";
		}

		internal sealed class WhipBend : MonoBehaviour
		{
			public Transform Foot, Top;
			public Transform[] Chain;
			public Vector3 Dir;
			public float Rigid, TipFromTop;
			public float TipDeg { get; private set; }

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
				TipDeg = phi;
				int lastIndex = Chain.Length - 1;
				float last = 0f;
				for (int k = 0; k <= lastIndex; k++)
				{
					float fraction = (float)k / lastIndex, angle = phi * (2f * fraction - fraction * fraction);
					Chain[k].localRotation = Quaternion.AngleAxis(angle - last, axis);
					last = angle;
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
			var heights = pos.Select(v => Vector3.Dot(v, dir)).ToList();
			var cuts = new Dictionary<(int, int, int), int>();
			int Cut(int[] tri, Vector3 bary, int plane)
			{
				int lowIndex = bary.x > 0f ? 0 : 1, highIndex = bary.z > 0f ? 2 : 1;
				int firstVertex = tri[lowIndex], secondVertex = tri[highIndex];
				var key = (Mathf.Min(firstVertex, secondVertex), Mathf.Max(firstVertex, secondVertex), plane);
				if (cuts.TryGetValue(key, out int cutIndex)) return cutIndex;
				cutIndex = pos.Count;
				var coords = new[] { bary.x, bary.y, bary.z };
				Vector3 P(List<Vector3> list) => list[tri[0]] * coords[0] + list[tri[1]] * coords[1] + list[tri[2]] * coords[2];
				pos.Add(P(pos));
				heights.Add(Vector3.Dot(pos[cutIndex], dir));
				if (nrm.Count > 0) nrm.Add(P(nrm).normalized);
				if (tan.Count > 0)
				{
					var tangent = (Vector3)tan[tri[0]] * coords[0] + (Vector3)tan[tri[1]] * coords[1] + (Vector3)tan[tri[2]] * coords[2];
					tan.Add(new Vector4(tangent.normalized.x, tangent.normalized.y, tangent.normalized.z, tan[tri[0]].w));
				}
				if (uv.Count > 0) uv.Add(uv[tri[0]] * coords[0] + uv[tri[1]] * coords[1] + uv[tri[2]] * coords[2]);
				if (uv2.Count > 0) uv2.Add(uv2[tri[0]] * coords[0] + uv2[tri[1]] * coords[1] + uv2[tri[2]] * coords[2]);
				if (col.Count > 0) col.Add(col[tri[0]] * coords[0] + col[tri[1]] * coords[1] + col[tri[2]] * coords[2]);
				cuts[key] = cutIndex;
				return cutIndex;
			}
			var subs = new List<List<int>>();
			for (int subMesh = 0; subMesh < src.subMeshCount; subMesh++)
			{
				var outTris = new List<int>();
				var triangles = src.GetTriangles(subMesh);
				for (int i = 0; i < triangles.Length; i += 3)
				{
					var tri = new[] { triangles[i], triangles[i + 1], triangles[i + 2] };
					float lowest = Mathf.Max(from, tri.Min(x => heights[x])), highest = Mathf.Min(to, tri.Max(x => heights[x]));
					int firstCut = Mathf.FloorToInt((lowest - from) / step) + 1, lastCut = Mathf.CeilToInt((highest - from) / step) - 1;
					if (lastCut < firstCut)
					{
						outTris.AddRange(tri);
						continue;
					}
					var poly = new List<(int v, Vector3 b, float h)>
					{
						(tri[0], Vector3.right, heights[tri[0]]),
						(tri[1], Vector3.up, heights[tri[1]]),
						(tri[2], Vector3.forward, heights[tri[2]])
					};
					for (int k = firstCut; k <= lastCut + 1; k++)
					{
						float below = k <= lastCut ? from + k * step : float.PositiveInfinity;
						var lower = new List<(int v, Vector3 b, float h)>();
						var upper = new List<(int v, Vector3 b, float h)>();
						for (int j = 0; j < poly.Count; j++)
						{
							var point = poly[j];
							var next = poly[(j + 1) % poly.Count];
							if (point.h <= below) lower.Add(point);
							if (point.h >= below) upper.Add(point);
							if ((point.h < below && next.h > below) || (point.h > below && next.h < below))
							{
								float fraction = (below - point.h) / (next.h - point.h);
								var interpolated = Vector3.Lerp(point.b, next.b, fraction);
								var cutPoint = (Cut(tri, interpolated, k), interpolated, below);
								lower.Add(cutPoint);
								upper.Add(cutPoint);
							}
						}
						for (int j = 1; j + 1 < lower.Count; j++) outTris.AddRange(new[] { lower[0].v, lower[j].v, lower[j + 1].v });
						poly = upper;
						if (poly.Count < 3) break;
					}
				}
				subs.Add(outTris);
			}
			var slicedMesh = new Mesh { indexFormat = pos.Count > 65000
				? IndexFormat.UInt32
				: IndexFormat.UInt16 };
			slicedMesh.SetVertices(pos);
			if (nrm.Count == pos.Count) slicedMesh.SetNormals(nrm);
			if (tan.Count == pos.Count) slicedMesh.SetTangents(tan);
			if (uv.Count == pos.Count) slicedMesh.SetUVs(0, uv);
			if (uv2.Count == pos.Count) slicedMesh.SetUVs(1, uv2);
			if (col.Count == pos.Count) slicedMesh.SetColors(col);
			slicedMesh.subMeshCount = subs.Count;
			for (int subMesh = 0; subMesh < subs.Count; subMesh++) slicedMesh.SetTriangles(subs[subMesh], subMesh);
			slicedMesh.RecalculateBounds();
			return slicedMesh;
		}

		static void Spring(Transform bone, Vector3 centreOfMass, float inertia)
		{
			var jiggle = bone.gameObject.AddComponent<Jiggle>();
			jiggle.UseCenterOfMass = true;
			jiggle.CenterOfMass = centreOfMass;
			jiggle.CenterOfMassInertia = inertia;
			jiggle.RotationInertia = 1f;
			jiggle.SpringStrength = R123Spring;
			jiggle.Dampening = R123Damping;
			jiggle.Gravity = 0f;
			jiggle.UseAngleLimit = false;
			jiggle.ShowViewportGizmos = false;
		}
	}
}
