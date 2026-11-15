using System.IO;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Anything missing from the bundle gets a console warning, not an exception.
	internal static class BundleAssets
	{
		internal static AssetBundle Open(string path, string tag)
		{
			if (!File.Exists(path))
			{
				MelonLogger.Error($"{tag} bundle not found: {path}");
				return null;
			}
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null) MelonLogger.Error($"{tag} bundle could not be loaded: {path}");
			return bundle;
		}

		internal static GameObject Prefab(AssetBundle bundle, string name, string tag, bool required, string ifMissing = null)
		{
			var prefab = bundle != null ? bundle.LoadAsset<GameObject>(name) : null;
			if (prefab != null)
			{
				prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
				return prefab;
			}
			if (bundle == null) return null;
			string msg = $"{tag}: prefab {name} missing from bundle {bundle.name}" + (ifMissing != null ? "; " + ifMissing : ".");
			if (required) MelonLogger.Error(msg);
			else MelonLogger.Warning(msg);
			return null;
		}
	}
}
