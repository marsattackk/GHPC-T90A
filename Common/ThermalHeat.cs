using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Thermals;
using UnityEngine;

namespace T90A.Common
{
	// HeatSource collects its renderers once, in OnEnable, and anything parented later draws black in white-hot. This
	// gives the parts I add the vehicle's heat.
	internal static class ThermalHeat
	{
		static readonly FieldInfo Serialized = Reflect.Field(typeof(HeatSource), "heat");
		static readonly FieldInfo Current = Reflect.Field(typeof(HeatSource), "_heat");
		static readonly FieldInfo Held = Reflect.Field(typeof(HeatSource), "_mpbRenderers");

		internal static IEnumerable<Transform> PortParts(Transform unit, string prefix)
		{
			var rig = RigBones.Hull(unit)?.parent;
			if (rig == null) return Enumerable.Empty<Transform>();
			return unit.GetComponentsInChildren<Transform>(true).Where(child =>
				child.name.StartsWith(prefix) && child.GetComponentsInChildren<Renderer>(true).Length > 0 && child
					.GetComponentsInParent<Transform>(true)
					.Contains(rig)
					&& (child.parent == null
						|| !child.parent.GetComponentsInParent<Transform>(true).Any(a => a.name.StartsWith(prefix))));
		}

		internal static HeatSource AddSource(GameObject go, float heat)
		{
			var heatSource = go.GetComponent<HeatSource>() ?? go.AddComponent<HeatSource>();
			Serialized.SetValue(heatSource, heat);
			return heatSource;
		}

		internal static void Apply(Transform unit, IEnumerable<Transform> parts, string tag, IDictionary<string, float> overrides = null)
		{
			var ownParts = parts.Where(p => p != null).ToList();
			float baseHeat = BaseHeat(unit, ownParts);
			foreach (var part in ownParts)
			{
				float heat = overrides != null && overrides.TryGetValue(part.name, out var overrideHeat) ? overrideHeat : baseHeat;
				var heatSource = part.GetComponent<HeatSource>() ?? part.gameObject.AddComponent<HeatSource>();
				Serialized.SetValue(heatSource, heat);
			}
			int sources = 0, materials = 0;
			// Heat's setter skips an unchanged value and only collects into an empty list, hence the clear and the NaN.
			// Outer first, so a part's own value wins.
			foreach (var heatSource in unit.GetComponentsInChildren<HeatSource>(true).OrderBy(Depth))
			{
				(Held.GetValue(heatSource) as IList)?.Clear();
				Current.SetValue(heatSource, float.NaN);
				heatSource.Heat = (float)Serialized.GetValue(heatSource);
				materials += (Held.GetValue(heatSource) as IList)?.Count ?? 0;
				sources++;
			}
			var flir = Shader.PropertyToID("_FLIR_Emission");
			int orphans =
				unit.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<HeatSource>(true) == null)
					.Sum(r => r.sharedMaterials.Count(m => m != null && m.HasProperty(flir)));
		}

		static float BaseHeat(Transform unit, List<Transform> ownParts)
		{
			foreach (var part in ownParts)
				for (var ancestor = part.parent; ancestor != null; ancestor = ancestor.parent)
				{
					var heatSource = ancestor.GetComponent<HeatSource>();
					if (heatSource != null) return (float)Serialized.GetValue(heatSource);
					if (ancestor == unit) break;
				}
			var any = unit
				.GetComponentsInChildren<HeatSource>(true)
				.FirstOrDefault(s => s.transform.parent == unit || s.transform == unit);
			return any != null ? (float)Serialized.GetValue(any) : 0.8f;
		}

		static int Depth(Component c)
		{
			int depth = 0;
			for (var ancestor = c.transform; ancestor != null; ancestor = ancestor.parent) depth++;
			return depth;
		}
	}
}
