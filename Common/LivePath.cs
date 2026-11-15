using System.Collections.Generic;
using System.Linq;
using GHPC.Camera;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Finds an object by its original path after something has moved it, which Transform.Find can't.
	internal static class LivePath
	{
		static readonly System.Reflection.FieldInfo SlotTarget = Reflect.Field(typeof(CameraSlot), "_reparentTarget");

		internal static Transform Find(Transform root, string path)
		{
			if (root == null || string.IsNullOrEmpty(path)) return null;
			var found = root.Find(path);
			if (found != null) return found;
			var moved = Moved(root);
			var hits = new HashSet<Transform>();
			Walk(root, path.Split('/'), 0, moved, hits);
			if (hits.Count == 1) return hits.First();
			if (hits.Count > 1) MelonLogger.Warning($"{root.name}: '{path}' fits {hits.Count} moved objects; none taken.");
			return null;
		}

		static void Walk(Transform at, string[] parts, int i, List<Transform> moved, HashSet<Transform> hits)
		{
			if (i == parts.Length)
			{
				hits.Add(at);
				return;
			}
			var child = at.Find(parts[i]);
			if (child != null)
			{
				Walk(child, parts, i + 1, moved, hits);
				return;
			}
			foreach (var node in moved)
				if (node.name == parts[i]) Walk(node, parts, i + 1, moved, hits);
		}

		static List<Transform> Moved(Transform root)
		{
			var groups = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true));
			foreach (var target in root.GetComponentsInChildren<LateFollowTarget>(true))
				foreach (var follower in target.LateFollowers) if (follower != null) groups.Add(follower);
			var moved = new HashSet<Transform>(groups.Select(f => f.transform));
			foreach (var reparent in root.GetComponentsInChildren<Reparent>(true)) moved.Add(reparent.transform);
			// A CameraSlot with a _reparentTarget moves itself in its own Awake, no Reparent component involved (the M60's "M85 gunsight").
			foreach (var slot in root.GetComponentsInChildren<CameraSlot>(true))
				if (SlotTarget?.GetValue(slot) is Transform) moved.Add(slot.transform);
			foreach (var holder in groups)
				foreach (var reparent in holder.GetComponentsInChildren<Reparent>(true)) moved.Add(reparent.transform);
			return moved.ToList();
		}
	}
}
