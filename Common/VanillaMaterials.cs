using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	internal static class VanillaMaterials
	{
		static readonly Dictionary<string, Material> Copies = new Dictionary<string, Material>();
		static readonly HashSet<string> Missing = new HashSet<string>();

		internal static Material Get(string vanilla, string[] units)
		{
			string key = vanilla + "|" + string.Join(",", units);
			if (Copies.TryGetValue(key, out var m) && m != null) return m;
			foreach (var unit in units)
			{
				var prefab = DonorParts.Prefab(unit);
				if (prefab == null) continue;
				var src = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(x => x != null && x.name == vanilla);
				if (src == null) continue;
				m = new Material(src) { name = $"{vanilla} (vanilla {unit})", hideFlags = HideFlags.DontUnloadUnusedAsset };
				Copies[key] = m;
				return m;
			}
			if (Missing.Add(key)) DonorParts.WarnOnce("material " + vanilla, $"vanilla material '{vanilla}' not found on {string.Join(" / ", units)}; the port's own material stays.");
			return null;
		}

		static string Bare(string name) => name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;

		internal static void Apply(Transform root, System.Func<Renderer, bool> filter, MaterialRoles roles)
		{
			var done = new Dictionary<string, int>();
			var byName = new Dictionary<string, (MaterialRoles.Entry rule, Material mat)>();
			foreach (var e in roles?.Applied ?? Enumerable.Empty<MaterialRoles.Entry>()) byName[e.Material] = (e, Get(e.Vanilla, e.Units));
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
			{
				if (!filter(r)) continue;
				var mats = r.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < mats.Length; i++)
				{
					if (mats[i] == null || !byName.TryGetValue(Bare(mats[i].name), out var hit)) continue;
					if (hit.mat == null) continue;
					string line = $"{Bare(mats[i].name)} -> '{hit.mat.name}' ({hit.rule.Role})";
					done[line] = done.TryGetValue(line, out int c) ? c + 1 : 1;
					mats[i] = hit.mat;
					changed = true;
				}
				if (changed) r.sharedMaterials = mats;
			}
		}
	}
}
