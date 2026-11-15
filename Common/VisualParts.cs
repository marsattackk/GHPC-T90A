using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// Hangs a prefab's parts on the vehicle's bones by group name.
	internal static class VisualParts
	{
		internal static int Attach(Transform root, GameObject prefab, string prefix, (string group, string path)[] groups)
		{
			var attached = Attach(root, prefab, prefix, groups.Select(e => (e.group, e.path, Vector3.zero)).ToArray());
			return attached.Count;
		}

		internal static Dictionary<string, Transform> Attach(
			Transform root,
			GameObject prefab,
			string prefix,
			(string group, string path, Vector3 offset)[] groups)
		{
			var instance = Object.Instantiate(prefab);
			var attached = new Dictionary<string, Transform>();
			foreach (var (group, path, offset) in groups)
			{
				var part = instance.transform.Find(group);
				var bone = string.IsNullOrEmpty(path) ? null : root.Find(path);
				if (part == null || bone == null) continue;
				part.SetParent(bone, false);
				part.localPosition = offset;
				part.localRotation = Quaternion.identity;
				part.localScale = Vector3.one;
				part.name = prefix + group;
				attached[group] = part;
			}
			Object.Destroy(instance);
			return attached;
		}
	}
}
