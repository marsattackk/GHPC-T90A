using System.Collections.Generic;
using System.Linq;
using GHPC.Camera;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class LivePath
	{
		static readonly System.Reflection.FieldInfo SlotTarget = Reflect.Field(typeof(CameraSlot), "_reparentTarget");

		internal static Transform Find(Transform root, string path)
		{
			if (root == null || string.IsNullOrEmpty(path)) return null;
			var t = root.Find(path);
			if (t != null) return t;
			var moved = Moved(root);
			var hits = new HashSet<Transform>();
			Walk(root, path.Split('/'), 0, moved, hits);
			if (hits.Count == 1) return hits.First();
			if (hits.Count > 1) MelonLogger.Warning($"{root.name}: '{path}' fits {hits.Count} moved objects; none taken.");
			return null;
		}

		static void Walk(Transform at, string[] parts, int i, List<Transform> moved, HashSet<Transform> hits)
		{
			if (i == parts.Length) { hits.Add(at); return; }
			var child = at.Find(parts[i]);
			if (child != null) { Walk(child, parts, i + 1, moved, hits); return; }
			foreach (var m in moved)
				if (m.name == parts[i]) Walk(m, parts, i + 1, moved, hits);
		}

		static List<Transform> Moved(Transform root)
		{
			var groups = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true));
			foreach (var lt in root.GetComponentsInChildren<LateFollowTarget>(true))
				foreach (var f in lt.LateFollowers) if (f != null) groups.Add(f);
			var moved = new HashSet<Transform>(groups.Select(g => g.transform));
			foreach (var r in root.GetComponentsInChildren<Reparent>(true)) moved.Add(r.transform);
			// A CameraSlot with a _reparentTarget moves itself in its own Awake, no Reparent component involved (the M60's "M85 gunsight").
			foreach (var c in root.GetComponentsInChildren<CameraSlot>(true))
				if (SlotTarget?.GetValue(c) is Transform) moved.Add(c.transform);
			foreach (var g in groups)
				foreach (var r in g.GetComponentsInChildren<Reparent>(true)) moved.Add(r.transform);
			return moved.ToList();
		}
	}
}
