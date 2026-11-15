using System;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// GHPC turns some hidden renderers back on (lamps, damage states). This keeps them off.
	public class KeepHidden : MonoBehaviour
	{
		internal Renderer[] Renderers = new Renderer[0];
		internal Light[] Lights = new Light[0];

		void LateUpdate()
		{
			foreach (var renderer in Renderers)
				if (renderer != null && renderer.enabled)
				{
					renderer.enabled = false;
				}
			foreach (var light in Lights)
				if (light != null && light.enabled)
				{
					light.enabled = false;
				}
		}

		public static int HideGear(Transform root, string[] names, string ownPrefix, string what, Action<string> onReenabled = null)
		{
			var renderers = Enumerable.ToArray(Enumerable.Where(root.GetComponentsInChildren<Renderer>(true),
				r => Array.IndexOf(names, r.gameObject.name) >= 0 && !r.name.StartsWith(ownPrefix)));
			foreach (var renderer in renderers) renderer.enabled = false;
			Add(root, renderers, null, what, onReenabled);
			return renderers.Length;
		}

		public static KeepHidden Add(
			Transform root,
			Renderer[] renderers,
			Light[] lights,
			string what,
			Action<string> onReenabled = null)
		{
			var keep = root.gameObject.AddComponent<KeepHidden>();
			keep.Renderers = renderers ?? new Renderer[0];
			keep.Lights = lights ?? new Light[0];
			return keep;
		}
	}
}
