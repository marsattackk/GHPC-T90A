using System.IO;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class BundleAssets
	{
		internal static AssetBundle Open(string path, string tag)
		{
			if (!File.Exists(path)) { MelonLogger.Error($"{tag} bundle not found: {path}"); return null; }
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null) MelonLogger.Error($"{tag} bundle could not be loaded: {path}");
			return bundle;
		}

		internal static GameObject Prefab(AssetBundle bundle, string name, string tag, bool required, string ifMissing = null)
		{
			var go = bundle != null ? bundle.LoadAsset<GameObject>(name) : null;
			if (go != null) { go.hideFlags = HideFlags.DontUnloadUnusedAsset; return go; }
			if (bundle == null) return null;
			string msg = $"{tag}: prefab {name} missing from bundle {bundle.name}" + (ifMissing != null ? "; " + ifMissing : ".");
			if (required) MelonLogger.Error(msg); else MelonLogger.Warning(msg);
			return null;
		}
	}
}
