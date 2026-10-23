using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Thermals;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class ThermalHeat
	{
		static readonly FieldInfo Serialized = AccessTools.Field(typeof(HeatSource), "heat");
		static readonly FieldInfo Current = AccessTools.Field(typeof(HeatSource), "_heat");
		static readonly FieldInfo Held = AccessTools.Field(typeof(HeatSource), "_mpbRenderers");

		internal static IEnumerable<Transform> PortParts(Transform unit, string prefix) =>
			unit.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(prefix) && t.GetComponentsInChildren<Renderer>(true).Length > 0 && t.GetComponentsInParent<Transform>(true).Any(p => p.name == "---MESH---")
				&& (t.parent == null || !t.parent.GetComponentsInParent<Transform>(true).Any(p => p.name.StartsWith(prefix))));

		internal static void Apply(Transform unit, IEnumerable<Transform> parts, IDictionary<string, float> overrides = null)
		{
			var ownParts = parts.Where(p => p != null).ToList();
			float baseHeat = BaseHeat(unit, ownParts);
			foreach (var p in ownParts)
			{
				float h = overrides != null && overrides.TryGetValue(p.name, out var o) ? o : baseHeat;
				var hs = p.GetComponent<HeatSource>() ?? p.gameObject.AddComponent<HeatSource>();
				Serialized.SetValue(hs, h);
			}
			foreach (var hs in unit.GetComponentsInChildren<HeatSource>(true).OrderBy(Depth))
			{
				(Held.GetValue(hs) as System.Collections.IList)?.Clear();
				Current.SetValue(hs, float.NaN);
				hs.Heat = (float)Serialized.GetValue(hs);
			}
			var flir = Shader.PropertyToID("_FLIR_Emission");
			unit.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<HeatSource>(true) == null)
				.Sum(r => r.sharedMaterials.Count(m => m != null && m.HasProperty(flir)));
		}

		static float BaseHeat(Transform unit, List<Transform> ownParts)
		{
			foreach (var p in ownParts)
				for (var x = p.parent; x != null; x = x.parent)
				{
					var hs = x.GetComponent<HeatSource>();
					if (hs != null) return (float)Serialized.GetValue(hs);
					if (x == unit) break;
				}
			var any = unit.GetComponentsInChildren<HeatSource>(true).FirstOrDefault(h => h.transform.parent == unit || h.transform == unit);
			return any != null ? (float)Serialized.GetValue(any) : 0.8f;
		}

		static int Depth(Component c) { int d = 0; for (var x = c.transform; x != null; x = x.parent) d++; return d; }
	}
}
