using System.Collections.Generic;
using UnityEngine;

namespace T90A.Common
{
	// Paint with the thermal shader, clean or weathered. Plain Standard doesn't show in thermal. Spare slots:
	// _DetailMask paint mask, _DetailAlbedoMap / _DetailNormalMap the clean albedo and metal-gloss under weathering.
	public static class VehiclePaint
	{
		public static bool Clean;

		public static void ApplyPaint(GameObject part, Material template, Shader flir, string paintedPrefix) =>
			ApplyPaint(part, template, flir, new[] { paintedPrefix });

		public static readonly List<string> QueueKept = new List<string>();

		public static void ApplyPaint(GameObject part, Material template, Shader flir, string[] paintedPrefixes)
		{
			bool Painted(string name)
			{
				foreach (var prefix in paintedPrefixes) if (name.StartsWith(prefix)) return true;
				return false;
			}
			var made = new Dictionary<Material, Material>();
			foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
			{
				var mats = renderer.sharedMaterials;
				for (int i = 0; i < mats.Length; i++)
				{
					var src = mats[i];
					if (src == null) continue;
					if (!made.TryGetValue(src, out var dst))
					{
						if (template != null && Painted(src.name)) dst = VehicleMaterial(src, template);
						else if (flir != null && src.shader.name == "Standard")
						{
							int queue = src.renderQueue;
							dst = new Material(src) { name = src.name };
							dst.shader = flir;
							if (dst.renderQueue != queue)
							{
								QueueKept.Add($"{src.name} {queue} (the swap gave {dst.renderQueue})");
								dst.renderQueue = queue;
							}
						}
						else dst = src;
						made[src] = dst;
					}
					mats[i] = dst;
				}
				renderer.sharedMaterials = mats;
			}
		}

		static Material VehicleMaterial(Material src, Material template)
		{
			var material = new Material(template) { name = src.name + " (vehicle)" };
			var albedo = src.GetTexture("_MainTex");
			var gloss = src.GetTexture("_MetallicGlossMap");
			var cleanAlbedo = src.HasProperty("_DetailAlbedoMap") ? src.GetTexture("_DetailAlbedoMap") : null;
			var cleanMetal = src.HasProperty("_DetailNormalMap") ? src.GetTexture("_DetailNormalMap") : null;
			if (Clean && cleanAlbedo != null) albedo = cleanAlbedo;
			if (Clean && cleanMetal != null) gloss = cleanMetal;
			material.SetTexture("_Albedo", albedo);
			material.SetTexture("_Normal", src.GetTexture("_BumpMap"));
			material.SetTexture("_Smoothness", gloss);
			var mask = src.GetTexture("_DetailMask");
			if (mask != null) material.SetTexture("_PaintMask", mask);
			var occlusion = src.HasProperty("_OcclusionMap") ? src.GetTexture("_OcclusionMap") : null;
			material.SetTexture("_Occlusion", occlusion != null ? occlusion : Texture2D.whiteTexture);
			// Camo's in the albedo. _PaintMask still places scorch and snow.
			material.SetFloat("_CamoAmount", 0f);
			material.SetFloat("_hackcamoUVs", 0f);
			material.DisableKeyword("_HACKCAMOUVS_ON");
			material.SetFloat("_ScorchAmount", 0f);
			return material;
		}
	}
}
