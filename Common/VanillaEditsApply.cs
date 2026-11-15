using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using UnityEngine;

namespace T90A.Common
{
	internal static class VanillaEditsApply
	{
		static readonly FieldInfo RestRotation = Reflect.Field(typeof(AimablePlatform), "_originalLocalRotation");
		static readonly FieldInfo GroupColliders = Reflect.Field(
			typeof(RaycastColliderGroup),
			"childColliders");

		internal static string Apply(
			Transform root,
			VanillaEdits e,
			IDictionary<string, (Mesh mesh, Transform bone)> reshape,
			List<string> problems = null)
		{
			// Through the groups' stored offsets with turret and gun at rest, because the groups lag and the AI may be traversing.
			var rest = new Dictionary<Transform, Quaternion>();
			foreach (var platform in root.GetComponentsInChildren<AimablePlatform>(true))
				if (platform.Transform != null) rest[platform.Transform] = (Quaternion)RestRotation.GetValue(platform);
			var groups = Groups(root);
			var log = new List<string>();
			void Problem(string message)
			{
				log.Add(message);
				problems?.Add(message);
			}
			List<Transform> Find(string name)
			{
				var found =
					groups.SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Select(c => c.transform)
						.Where(p => p.name == name).Distinct().ToList();
				if (found.Count == 0) Problem($"'{name}' not found");
				return found;
			}

			foreach (var pieceName in e.Off)
				foreach (var piece in Find(pieceName))
				{
					piece.gameObject.SetActive(false);
					log.Add($"off {pieceName}");
				}

			foreach (var (partName, matrix) in e.Transforms)
				foreach (var piece in Find(partName))
				{
					var follower = Follower(piece);
					if (follower == null)
					{
						Problem($"'{partName}': no LateFollow above it, not moved");
						continue;
					}
					var boneRest = RestInRoot(root, follower.FollowTarget, rest);
					var inBone = boneRest.inverse * matrix * boneRest;
					var before = InBone(follower, piece);
					SetLocal(piece, InBone(follower, piece.parent).inverse * inBone * before);
					log.Add($"moved {partName} by {Shift(before, InBone(follower, piece), boneRest)} cm");
				}

			foreach (var pieceName in e.Reshape)
			{
				if (!reshape.TryGetValue(pieceName, out var replacement))
				{
					Problem($"'{pieceName}': no reshape piece in the bundle, not reshaped");
					continue;
				}
				if (!replacement.mesh.isReadable)
				{
					Problem($"'{pieceName}': reshape mesh not readable, not reshaped");
					continue;
				}
				foreach (var piece in Find(pieceName))
				{
					var follower = Follower(piece);
					var meshCollider = piece.GetComponent<MeshCollider>();
					if (follower == null || meshCollider == null)
					{
						Problem($"'{pieceName}': {(follower == null ? "no LateFollow above it" : "no MeshCollider")}, not reshaped");
						continue;
					}
					var pose = RestInRoot(root, follower.FollowTarget, rest).inverse * RestInRoot(root, replacement.bone, rest);
					var kids = piece
						.Cast<Transform>()
						.Where(c => c.name != "inverted armor clone")
						.Select(c => (c, follower.transform.worldToLocalMatrix * c.localToWorldMatrix))
						.ToList();
					SetLocal(piece, InBone(follower, piece.parent).inverse * pose);
					piece.localScale = Vector3.one;
					foreach (var (child, inGroup) in kids)
					{
						var worldPose = follower.transform.localToWorldMatrix * inGroup;
						child.SetPositionAndRotation(worldPose.GetColumn(3), worldPose.rotation);
					}
					bool wasConvex = meshCollider.convex;
					meshCollider.convex = false;
					meshCollider.sharedMesh = replacement.mesh;
					var meshFilter = piece.GetComponent<MeshFilter>();
					if (meshFilter != null) meshFilter.sharedMesh = replacement.mesh;
					string clone = "";
					// VariableArmor measures the exit through this clone, built from the old mesh in Awake.
					var invertedClone = piece.GetComponent<VariableArmor>() != null ? piece.Find("inverted armor clone") : null;
					if (invertedClone != null)
					{
						var inv = Inverted(replacement.mesh);
						var cloneFilter = invertedClone.GetComponent<MeshFilter>();
						if (cloneFilter != null) cloneFilter.sharedMesh = inv;
						var cloneCollider = invertedClone.GetComponent<MeshCollider>();
						if (cloneCollider != null) cloneCollider.sharedMesh = inv;
						clone = $", inverted clone replaced (filter {(cloneFilter != null ? "ok" : "MISSING")}, collider {(cloneCollider != null ? "ok" : "MISSING")})";
					}
					else if (piece.GetComponent<VariableArmor>() != null) clone = ", VariableArmor WITHOUT its inverted clone";
					log.Add($"reshaped {pieceName}: {replacement.mesh.triangles.Length / 3} faces{(wasConvex ? " (was convex)" : "")}{clone}{(kids.Count > 0 ? $", {kids.Count} children kept in place" : "")}");
				}
			}

			foreach (var (path, group, delta) in e.Bones)
			{
				var bone = root.Find(path);
				if (bone == null)
				{
					Problem($"bone '{path}' not found");
					continue;
				}
				var local = RestInRoot(root, bone.parent, rest).inverse.MultiplyVector(delta);
				bone.localPosition += local;
				var followers = Followers.On(root, bone);
				foreach (var follower in followers)
					follower.transform.SetPositionAndRotation(
						bone.TransformPoint(Followers.PosShift(follower)),
						bone.rotation * Followers.RotShift(follower));
				bool groupOn = followers.Any(f => f.name.Contains(group));
				log.Add($"bone {path} moved by ({delta.x * 100f:0.0}, {delta.y * 100f:0.0}, {delta.z * 100f:0.0}) cm, {followers.Count} followers re-synced"
					+ (groupOn ? $" ({group} among them)" : $"; '{group}' does NOT follow it"));
			}

			// Last. The edits above find pieces by their collider.
			foreach (var pieceName in e.Strip)
				foreach (var piece in Find(pieceName))
				{
					var cols = piece.GetComponents<Collider>();
					foreach (var colliderGroup in groups
						.SelectMany(g => g.GetComponentsInChildren<RaycastColliderGroup>(true))
						.Distinct())
						if (GroupColliders.GetValue(colliderGroup) is List<Collider> list) foreach (var col in cols) list.Remove(col);
					int armour = 0;
					foreach (var armourPart in piece.GetComponents<IArmor>())
					{
						Object.Destroy(armourPart as Component);
						armour++;
					}
					foreach (var col in cols) Object.Destroy(col);
					foreach (var renderer in piece.GetComponents<Renderer>()) Object.Destroy(renderer);
					log.Add($"stripped {pieceName} ({cols.Length} colliders, {armour} armour; {piece.childCount} children kept)");
				}

			return string.Join("; ", log);
		}

		static List<Transform> Groups(Transform root)
		{
			var set = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true));
			foreach (var target in root.GetComponentsInChildren<LateFollowTarget>(true))
				foreach (var follower in target.LateFollowers) if (follower != null) set.Add(follower);
			return set.Where(f => RigBones.IsColliderGroup(f.name)).Select(x => x.transform).ToList();
		}

		static LateFollow Follower(Transform t)
			=> t.GetComponentsInParent<LateFollow>(true).FirstOrDefault(f => f.FollowTarget != null);

		static Matrix4x4 InBone(LateFollow f, Transform t) =>
			Matrix4x4.TRS(
				Followers.PosShift(f),
				Followers.RotShift(f),
				Vector3.one) * (f.transform.worldToLocalMatrix * t.localToWorldMatrix);

		static Matrix4x4 RestInRoot(Transform root, Transform t, Dictionary<Transform, Quaternion> rest)
		{
			var matrix = Matrix4x4.identity;
			for (var ancestor = t; ancestor != null && ancestor != root; ancestor = ancestor.parent)
				matrix = Matrix4x4.TRS(
					ancestor.localPosition,
					rest.TryGetValue(ancestor, out var restRotation) ? restRotation : ancestor.localRotation,
					ancestor.localScale) * matrix;
			return matrix;
		}

		static void SetLocal(Transform t, Matrix4x4 local)
		{
			t.localPosition = local.GetColumn(3);
			t.localRotation = local.rotation;
		}

		static string Shift(Matrix4x4 before, Matrix4x4 after, Matrix4x4 boneRest)
		{
			var offset = boneRest.MultiplyVector((Vector3)after.GetColumn(3) - (Vector3)before.GetColumn(3)) * 100f;
			return $"({offset.x:0.0}, {offset.y:0.0}, {offset.z:0.0})";
		}

		static Mesh Inverted(Mesh m)
		{
			var inv = new Mesh { name = m.name + " (inverted)", indexFormat = m.indexFormat };
			inv.SetVertices(m.vertices);
			var triangles = m.triangles;
			for (int i = 0; i < triangles.Length; i += 3)
			{
				int first = triangles[i];
				triangles[i] = triangles[i + 1];
				triangles[i + 1] = first;
			}
			inv.SetTriangles(triangles, 0);
			inv.RecalculateNormals();
			inv.RecalculateBounds();
			return inv;
		}
	}
}
