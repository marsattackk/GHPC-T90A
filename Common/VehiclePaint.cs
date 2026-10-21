using System.Collections.Generic;
using UnityEngine;

namespace T90A.Common
{
	public static class VehiclePaint
	{
		public static bool Worn;

		public static void ApplyPaint(GameObject part, Material template, Shader flir, string paintedPrefix)
		{
			var made = new Dictionary<Material, Material>();
			foreach (var r in part.GetComponentsInChildren<Renderer>(true))
			{
				var mats = r.sharedMaterials;
				for (int i = 0; i < mats.Length; i++)
				{
					var src = mats[i];
					if (src == null) continue;
					if (!made.TryGetValue(src, out var dst))
					{
						if (template != null && src.name.StartsWith(paintedPrefix)) dst = VehicleMaterial(src, template);
						else if (flir != null && src.shader.name == "Standard") { dst = new Material(src) { name = src.name }; dst.shader = flir; }
						else dst = src;
						made[src] = dst;
					}
					mats[i] = dst;
				}
				r.sharedMaterials = mats;
			}
		}

		static Material VehicleMaterial(Material src, Material template)
		{
			var m = new Material(template) { name = src.name + " (vehicle)" };
			var wornAlbedo = src.GetTexture("_EmissionMap");
			var wornMetal = src.GetTexture("_ParallaxMap");
			m.SetTexture("_Albedo", Worn && wornAlbedo != null ? wornAlbedo : src.GetTexture("_MainTex"));
			m.SetTexture("_Normal", src.GetTexture("_BumpMap"));
			m.SetTexture("_Smoothness", Worn && wornMetal != null ? wornMetal : src.GetTexture("_MetallicGlossMap"));
			var mask = src.GetTexture("_DetailMask");
			if (mask != null) m.SetTexture("_PaintMask", mask);
			m.SetTexture("_Occlusion", Texture2D.whiteTexture);
			m.SetFloat("_CamoAmount", 0f);
			m.SetFloat("_hackcamoUVs", 0f);
			m.DisableKeyword("_HACKCAMOUVS_ON");
			m.SetFloat("_ScorchAmount", 0f);
			return m;
		}
	}
}
