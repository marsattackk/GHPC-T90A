using System.Collections.Generic;
using UnityEngine;

namespace T90A.Common
{
	public static class VehiclePaint
	{
		public static bool Clean;

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
			var albedo = src.GetTexture("_MainTex");
			var gloss = src.GetTexture("_MetallicGlossMap");
			var cleanAlbedo = src.HasProperty("_DetailAlbedoMap") ? src.GetTexture("_DetailAlbedoMap") : null;
			var cleanMetal = src.HasProperty("_DetailNormalMap") ? src.GetTexture("_DetailNormalMap") : null;
			if (Clean && cleanAlbedo != null) albedo = cleanAlbedo;
			if (Clean && cleanMetal != null) gloss = cleanMetal;
			m.SetTexture("_Albedo", albedo);
			m.SetTexture("_Normal", src.GetTexture("_BumpMap"));
			m.SetTexture("_Smoothness", gloss);
			var mask = src.GetTexture("_DetailMask");
			if (mask != null) m.SetTexture("_PaintMask", mask);
			var ao = src.HasProperty("_OcclusionMap") ? src.GetTexture("_OcclusionMap") : null;
			m.SetTexture("_Occlusion", ao != null ? ao : Texture2D.whiteTexture);
			m.SetFloat("_CamoAmount", 0f);
			m.SetFloat("_hackcamoUVs", 0f);
			m.DisableKeyword("_HACKCAMOUVS_ON");
			m.SetFloat("_ScorchAmount", 0f);
			return m;
		}
	}
}
