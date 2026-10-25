using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace T90A.Common
{
	internal static class DonorParts
	{
		static Transform _store;
		static readonly HashSet<string> Warned = new HashSet<string>();

		static Transform Store
		{
			get
			{
				if (_store != null) return _store;
				var go = new GameObject("T90A.Common donor parts (inactive copies)");
				go.SetActive(false);
				Object.DontDestroyOnLoad(go);
				_store = go.transform;
				return _store;
			}
		}

		internal static GameObject Prefab(string key)
		{
			var meta = UnitRegistry.Find(key)
				?? UnitRegistry.Lookup?.AllUnits?.FirstOrDefault(m => m != null && m.Name != null && m.Name.Replace(" ", "").Equals(key, System.StringComparison.OrdinalIgnoreCase));
			GameObject prefab = null;
			if (meta?.PrefabReference != null && meta.PrefabReference.RuntimeKeyIsValid())
				prefab = Addressables.LoadAssetAsync<GameObject>(meta.PrefabReference.RuntimeKey).WaitForCompletion();
			if (prefab == null) WarnOnce(key, $"donor prefab '{key}' {(meta == null ? "not in the unit registry" : "could not be loaded (destroyed by another mod?)")}.");
			return prefab;
		}

		internal static void WarnOnce(string key, string message)
		{
			if (Warned.Add(key + "|" + message)) MelonLogger.Warning(message);
		}

		internal static Transform Copy(GameObject prefab, string label, params GameObject[] parts)
		{
			if (prefab == null) return null;
			var box = new GameObject(label).transform;
			box.SetParent(Store, false);
			var prefabInv = prefab.transform.worldToLocalMatrix;
			foreach (var part in parts)
			{
				if (part == null) continue;
				var copy = Object.Instantiate(part, box, false);
				copy.name = part.name;
				var m = prefabInv * part.transform.localToWorldMatrix;
				copy.transform.localPosition = m.GetColumn(3);
				copy.transform.localRotation = m.rotation;
				copy.transform.localScale = m.lossyScale;
			}
			foreach (Transform c in box) Scrub(c.gameObject, prefab.transform);
			return box;
		}

		internal static int Scrub(GameObject clone, Transform owner)
		{
			int n = 0;
			bool Owned(Object o) => o != null && (o is Component c ? c.transform.IsChildOf(owner) : o is GameObject g && g.transform.IsChildOf(owner));
			foreach (var comp in clone.GetComponentsInChildren<MonoBehaviour>(true))
				for (var t = comp.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
					foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
					{
						if (f.IsInitOnly || f.IsLiteral) continue;
						if (typeof(Object).IsAssignableFrom(f.FieldType))
						{
							if (Owned(f.GetValue(comp) as Object)) { f.SetValue(comp, null); n++; }
						}
						else if (f.FieldType.IsArray && typeof(Object).IsAssignableFrom(f.FieldType.GetElementType()) && f.GetValue(comp) is System.Array a)
						{
							for (int i = 0; i < a.Length; i++) if (Owned(a.GetValue(i) as Object)) { a.SetValue(null, i); n++; }
						}
					}
			return n;
		}

		internal static bool PrefabsAtRisk
		{
			get
			{
				if (!System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "VanillaAssetUtil")) return false;
				try { return !(MelonPreferences.HasEntry("VanillaAssetUtil", "Legacy Mode") && MelonPreferences.GetEntryValue<bool>("VanillaAssetUtil", "Legacy Mode")); }
				catch { return true; }
			}
		}

		static bool _warmed;

		internal static void WarmAtMenu(string tag, System.Action copy)
		{
			if (_warmed || !PrefabsAtRisk) return;
			_warmed = true;
			MelonCoroutines.Start(Warm(tag, copy));
		}

		static System.Collections.IEnumerator Warm(string tag, System.Action copy)
		{
			for (int frame = 0; frame < 600 && UnitRegistry.Lookup?.AllUnits == null; frame++) yield return null;
			if (UnitRegistry.Lookup?.AllUnits == null) { _warmed = false; yield break; }
			try { copy(); }
			catch (System.Exception e) { MelonLogger.Warning($"{tag}: copying the vanilla donor parts at the main menu failed ({e.GetType().Name}: {e.Message}); they are copied at the first spawn."); }
		}
	}
}
