using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class VanillaEditsApply
	{
		static readonly FieldInfo RestRotation = AccessTools.Field(typeof(AimablePlatform), "_originalLocalRotation");
		static readonly FieldInfo GroupColliders = AccessTools.Field(typeof(RaycastColliderGroup), "childColliders");

		internal static void Apply(Transform root, VanillaEdits e, IDictionary<string, (Mesh mesh, Transform bone)> reshape, List<string> problems = null)
		{
			var rest = new Dictionary<Transform, Quaternion>();
			foreach (var p in root.GetComponentsInChildren<AimablePlatform>(true))
				if (p.Transform != null) rest[p.Transform] = (Quaternion)RestRotation.GetValue(p);
			var groups = Groups(root);
			void Problem(string s) { problems?.Add(s); }
			List<Transform> Find(string name)
			{
				var found = groups.SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Select(c => c.transform)
					.Where(t => t.name == name).Distinct().ToList();
				if (found.Count == 0) Problem($"'{name}' not found");
				return found;
			}

			foreach (var n in e.Off)
				foreach (var t in Find(n)) t.gameObject.SetActive(false);

			foreach (var (n, m) in e.Transforms)
				foreach (var t in Find(n))
				{
					var f = Follower(t);
					if (f == null) { Problem($"'{n}': no LateFollow above it, not moved"); continue; }
					var boneRest = RestInRoot(root, f.FollowTarget, rest);
					var inBone = boneRest.inverse * m * boneRest;
					var before = InBone(f, t);
					SetLocal(t, InBone(f, t.parent).inverse * inBone * before);
				}

			foreach (var n in e.Reshape)
			{
				if (!reshape.TryGetValue(n, out var r)) { Problem($"'{n}': no reshape piece in the bundle, not reshaped"); continue; }
				if (!r.mesh.isReadable) { Problem($"'{n}': reshape mesh not readable, not reshaped"); continue; }
				foreach (var t in Find(n))
				{
					var f = Follower(t);
					var mc = t.GetComponent<MeshCollider>();
					if (f == null || mc == null) { Problem($"'{n}': {(f == null ? "no LateFollow above it" : "no MeshCollider")}, not reshaped"); continue; }
					var pose = RestInRoot(root, f.FollowTarget, rest).inverse * RestInRoot(root, r.bone, rest);
					var kids = t.Cast<Transform>().Where(k => k.name != "inverted armor clone").Select(k => (k, f.transform.worldToLocalMatrix * k.localToWorldMatrix)).ToList();
					SetLocal(t, InBone(f, t.parent).inverse * pose);
					t.localScale = Vector3.one;
					foreach (var (k, inGroup) in kids)
					{
						var w = f.transform.localToWorldMatrix * inGroup;
						k.SetPositionAndRotation(w.GetColumn(3), w.rotation);
					}
					mc.convex = false;
					mc.sharedMesh = r.mesh;
					var mf = t.GetComponent<MeshFilter>();
					if (mf != null) mf.sharedMesh = r.mesh;
					var c = t.GetComponent<VariableArmor>() != null ? t.Find("inverted armor clone") : null;
					if (c != null)
					{
						var inv = Inverted(r.mesh);
						var cf = c.GetComponent<MeshFilter>(); if (cf != null) cf.sharedMesh = inv;
						var cc = c.GetComponent<MeshCollider>(); if (cc != null) cc.sharedMesh = inv;
					}
				}
			}

			foreach (var (path, group, delta) in e.Bones)
			{
				var bone = root.Find(path);
				if (bone == null) { Problem($"bone '{path}' not found"); continue; }
				var local = RestInRoot(root, bone.parent, rest).inverse.MultiplyVector(delta);
				bone.localPosition += local;
				var fs = Followers.On(root, bone);
				foreach (var f in fs) f.transform.SetPositionAndRotation(bone.TransformPoint(Followers.PosShift(f)), bone.rotation * Followers.RotShift(f));
			}

			foreach (var n in e.Strip)
				foreach (var t in Find(n))
				{
					var cols = t.GetComponents<Collider>();
					foreach (var g in groups.SelectMany(x => x.GetComponentsInChildren<RaycastColliderGroup>(true)).Distinct())
						if (GroupColliders.GetValue(g) is List<Collider> list) foreach (var col in cols) list.Remove(col);
					foreach (var a in t.GetComponents<IArmor>()) Object.Destroy(a as Component);
					foreach (var col in cols) Object.Destroy(col);
					foreach (var r in t.GetComponents<Renderer>()) Object.Destroy(r);
				}
		}

		static List<Transform> Groups(Transform root)
		{
			var set = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true));
			foreach (var t in root.GetComponentsInChildren<LateFollowTarget>(true))
				foreach (var f in t.LateFollowers) if (f != null) set.Add(f);
			return set.Where(f => f.name.Contains("COLLIDERS")).Select(f => f.transform).ToList();
		}

		static LateFollow Follower(Transform t) => t.GetComponentsInParent<LateFollow>(true).FirstOrDefault(f => f.FollowTarget != null);

		static Matrix4x4 InBone(LateFollow f, Transform t) =>
			Matrix4x4.TRS(Followers.PosShift(f), Followers.RotShift(f), Vector3.one) * (f.transform.worldToLocalMatrix * t.localToWorldMatrix);

		static Matrix4x4 RestInRoot(Transform root, Transform t, Dictionary<Transform, Quaternion> rest)
		{
			var m = Matrix4x4.identity;
			for (var x = t; x != null && x != root; x = x.parent)
				m = Matrix4x4.TRS(x.localPosition, rest.TryGetValue(x, out var r) ? r : x.localRotation, x.localScale) * m;
			return m;
		}

		static void SetLocal(Transform t, Matrix4x4 local)
		{
			t.localPosition = local.GetColumn(3);
			t.localRotation = local.rotation;
		}

		static Mesh Inverted(Mesh m)
		{
			var inv = new Mesh { name = m.name + " (inverted)", indexFormat = m.indexFormat };
			inv.SetVertices(m.vertices);
			var t = m.triangles;
			for (int i = 0; i < t.Length; i += 3) { int a = t[i]; t[i] = t[i + 1]; t[i + 1] = a; }
			inv.SetTriangles(t, 0);
			inv.RecalculateNormals(); inv.RecalculateBounds();
			return inv;
		}
	}
}
