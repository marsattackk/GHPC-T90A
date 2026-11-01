using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace T90A.Common
{
	// VanillaAssetUtil DestroyImmediates vanilla prefabs even with my handle open, so I keep copies.
	// Parts only, never a whole unit: SceneUnitsManager and ObjectPoolManager search inactive objects too.
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
				go.SetActive(false);   // first, so no copy ever wakes up
				Object.DontDestroyOnLoad(go);
				_store = go.transform;
				return _store;
			}
		}

		internal static GameObject Prefab(string key)
		{
			var meta = UnitRegistry.Find(key)
				?? UnitRegistry.Lookup?.AllUnits?.FirstOrDefault(m => m != null && m.Name != null && m.Name.Replace(" ", "").Equals(key, System.StringComparison.OrdinalIgnoreCase));
			var prefab = Load(meta);
			if (prefab == null) WarnOnce(key, $"donor prefab '{key}' {(meta == null ? "not in the unit registry" : "could not be loaded (destroyed by another mod?)")}.");
			return prefab;
		}

		// Never released, the copies draw this prefab's meshes and textures.
		static readonly Dictionary<string, GameObject> Loaded = new Dictionary<string, GameObject>();
		internal static GameObject Load(GHPC.Mission.UnitPrefabLookupScriptable.UnitPrefabMetadata meta)
		{
			if (meta?.PrefabReference == null || !meta.PrefabReference.RuntimeKeyIsValid()) return null;
			string k = meta.PrefabReference.RuntimeKey.ToString();
			if (Loaded.TryGetValue(k, out var had) && had != null) return had;
			if (_atMenu)
			{
				if (FailedAtMenu.Contains(k)) return null;
				if (!Pending.ContainsKey(k)) Pending[k] = (meta.Name, Addressables.LoadAssetAsync<GameObject>(meta.PrefabReference.RuntimeKey));
				throw new NotLoadedYet();
			}
			var prefab = Addressables.LoadAssetAsync<GameObject>(meta.PrefabReference.RuntimeKey).WaitForCompletion();
			if (prefab != null) Loaded[k] = prefab;
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

		internal static void WarmAtMenu(string tag, params System.Action[] steps)
		{
			if (_warmed || !PrefabsAtRisk) return;
			_warmed = true;
			MelonCoroutines.Start(Warm(tag, steps));
		}

		// A blocking load right after Ammo Upgrade Pack releases its vehicles in leo2_menu never comes back: WaitForCompletion
		// makes the main thread wait out the released bundles' UnloadAsync. An async load just queues behind the unload.
		static bool _atMenu;
		static readonly Dictionary<string, (string name, AsyncOperationHandle<GameObject> handle)> Pending = new Dictionary<string, (string, AsyncOperationHandle<GameObject>)>();
		static readonly HashSet<string> FailedAtMenu = new HashSet<string>();
		sealed class NotLoadedYet : System.Exception { }

		const float LoadSeconds = 120f;
		const int Rounds = 16;

		static System.Collections.IEnumerator Warm(string tag, System.Action[] steps)
		{
			for (int frame = 0; frame < 600; frame++)
			{
				if (frame % 10 == 0 && UnitRegistry.Lookup?.AllUnits != null) break;
				yield return null;
			}
			if (UnitRegistry.Lookup?.AllUnits == null) { _warmed = false; yield break; }
			var pending = steps.Where(s => s != null).ToList();
			for (int round = 1; pending.Count > 0; round++)
			{
				var again = new List<System.Action>();
				foreach (var step in pending)
				{
					_atMenu = true;
					try { step(); }
					catch (NotLoadedYet) { again.Add(step); }
					catch (System.Exception e) { MelonLogger.Warning($"{tag}: copying a vanilla donor part at the main menu failed ({e.GetType().Name}: {e.Message}); it is copied at the first spawn."); }
					finally { _atMenu = false; }
					yield return null;
				}
				if (again.Count == 0) break;
				float start = Time.realtimeSinceStartup;
				while (Pending.Values.Any(p => !p.handle.IsDone) && Time.realtimeSinceStartup - start < LoadSeconds) yield return null;
				var done = Pending.Where(p => p.Value.handle.IsDone).ToList();
				foreach (var p in done)
				{
					var h = p.Value.handle;
					var prefab = h.IsValid() && h.Status == AsyncOperationStatus.Succeeded ? h.Result : null;
					if (prefab != null) Loaded[p.Key] = prefab;
					else { FailedAtMenu.Add(p.Key); if (h.IsValid()) Addressables.Release(h); }
					Pending.Remove(p.Key);
				}
				if (Pending.Count > 0 || round == Rounds)
				{
					MelonLogger.Warning($"{tag}: vanilla donor prefabs not loaded at the main menu ({(Pending.Count > 0 ? string.Join(", ", Pending.Values.Select(p => p.name)) + $" still loading after {LoadSeconds:0} s" : $"{Rounds} rounds")}); {again.Count} donor part(s) are copied at the first spawn.");
					break;
				}
				pending = again;
			}
			FailedAtMenu.Clear();
		}
	}
}
