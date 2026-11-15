using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// Vanilla materials off donor units onto my parts, per the role table. Vanilla glass is see-through Standard (FLIR)
	// over dark geometry. An opaque coat of my own read as plastic.
	internal static class VanillaMaterials
	{
		static readonly Dictionary<string, Material> Copies = new Dictionary<string, Material>();
		static readonly HashSet<string> Missing = new HashSet<string>();

		internal static Material Get(string vanilla, string[] units)
		{
			string key = vanilla + "|" + string.Join(",", units);
			if (Copies.TryGetValue(key, out var copy) && copy != null) return copy;
			foreach (var unit in units)
			{
				var prefab = DonorParts.Prefab(unit);
				if (prefab == null) continue;
				var src = prefab
					.GetComponentsInChildren<Renderer>(true)
					.SelectMany(r => r.sharedMaterials)
					.FirstOrDefault(c => c != null && c.name == vanilla);
				if (src == null) continue;
				copy = new Material(src) { name = $"{vanilla} (vanilla {unit})", hideFlags = HideFlags.DontUnloadUnusedAsset };
				Copies[key] = copy;
				return copy;
			}
			if (Missing.Add(key))
				DonorParts.WarnOnce(
					"material " + vanilla,
					$"vanilla material '{vanilla}' not found on {string.Join(" / ", units)}; the port's own material stays.");
			return null;
		}

		internal static void Warm(MaterialRoles roles)
		{
			foreach (var entry in roles?.Applied ?? Enumerable.Empty<MaterialRoles.Entry>()) Get(entry.Vanilla, entry.Units);
		}

		static string Bare(string name) => name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;

		internal static List<string> Apply(Transform root, MaterialRoles roles)
		{
			var done = new Dictionary<string, int>();
			var kept = new HashSet<string>();
			var byName = new Dictionary<string, (MaterialRoles.Entry rule, Material mat)>();
			foreach (var entry in roles?.Applied ?? Enumerable.Empty<MaterialRoles.Entry>())
				byName[entry.Material] = (entry, Get(entry.Vanilla, entry.Units));
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
			{
				var mats = renderer.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < mats.Length; i++)
				{
					if (mats[i] == null || !byName.TryGetValue(Bare(mats[i].name), out var hit)) continue;
					if (hit.mat == null)
					{
						kept.Add(Bare(mats[i].name));
						continue;
					}
					string line = $"{Bare(mats[i].name)} -> '{hit.mat.name}' ({hit.rule.Role})";
					done[line] = done.TryGetValue(line, out int count) ? count + 1 : 1;
					mats[i] = hit.mat;
					changed = true;
				}
				if (changed) renderer.sharedMaterials = mats;
			}
			return done
				.Select(p => $"{p.Key}: {p.Value} renderer(s)")
				.Concat(kept.Select(n => $"{n} kept (vanilla material missing)"))
				.ToList();
		}
	}
}
