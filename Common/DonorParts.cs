using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace T90A.Common
{
	// Inactive copies of parts from other vanilla units (T-80B voices and sight, M60A3 thermal and so on).
	// VanillaAssetUtil DestroyImmediates vanilla prefabs even with my handle open, so I keep copies. Parts only, never
	// a whole unit: SceneUnitsManager and ObjectPoolManager search inactive objects too.
	internal static class DonorParts
	{
		static Transform _store;
		static readonly HashSet<string> Warned = new HashSet<string>();

		static Transform Store
		{
			get
			{
				if (_store != null) return _store;
				var holder = new GameObject("T90A.Common donor parts (inactive copies)");
				holder.SetActive(false);   // first, so no copy ever wakes up
				Object.DontDestroyOnLoad(holder);
				_store = holder.transform;
				return _store;
			}
		}

		internal static GameObject Prefab(string key)
		{
			var meta = UnitRegistry.Find(key)
				?? UnitRegistry.Lookup?.AllUnits?.FirstOrDefault(u => u != null
					&& u.Name != null
					&& u.Name.Replace(" ", "").Equals(key, System.StringComparison.OrdinalIgnoreCase));
			var prefab = Load(meta);
			if (prefab == null)
				WarnOnce(
					key,
					$"donor prefab '{key}' {(meta == null ? "not in the unit registry" : "could not be loaded (destroyed by another mod?)")}.");
			return prefab;
		}

		// Never released. The copies still draw this prefab's meshes and textures.
		static readonly Dictionary<string, GameObject> Loaded = new Dictionary<string, GameObject>();
		internal static GameObject Load(GHPC.Mission.UnitPrefabLookupScriptable.UnitPrefabMetadata meta)
		{
			if (meta?.PrefabReference == null || !meta.PrefabReference.RuntimeKeyIsValid()) return null;
			string key = meta.PrefabReference.RuntimeKey.ToString();
			if (Loaded.TryGetValue(key, out var had) && had != null) return had;
			if (_atMenu)
			{
				if (FailedAtMenu.Contains(key)) return null;
				if (!Pending.ContainsKey(key))
					Pending[key] = (meta.Name, Addressables.LoadAssetAsync<GameObject>(meta.PrefabReference.RuntimeKey));
				throw new NotLoadedYet();
			}
			var prefab = Addressables.LoadAssetAsync<GameObject>(meta.PrefabReference.RuntimeKey).WaitForCompletion();
			if (prefab != null) Loaded[key] = prefab;
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
				var pose = prefabInv * part.transform.localToWorldMatrix;
				copy.transform.localPosition = pose.GetColumn(3);
				copy.transform.localRotation = pose.rotation;
				copy.transform.localScale = pose.lossyScale;
			}
			int cleared = 0;
			foreach (Transform child in box) cleared += Scrub(child.gameObject, prefab.transform);
			return box;
		}

		internal static int Scrub(GameObject clone, Transform owner)
		{
			int count = 0;
			bool Owned(Object unityObject)
				=> unityObject != null
					&& (unityObject is Component component
						? component.transform.IsChildOf(owner)
						: unityObject is GameObject gameObject && gameObject.transform.IsChildOf(owner));
			foreach (var comp in clone.GetComponentsInChildren<MonoBehaviour>(true))
				for (var type = comp.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
					foreach (var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
					{
						if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral) continue;
						if (typeof(Object).IsAssignableFrom(fieldInfo.FieldType))
						{
							if (Owned(fieldInfo.GetValue(comp) as Object))
							{
								fieldInfo.SetValue(comp, null);
								count++;
							}
						}
						else if (fieldInfo.FieldType.IsArray
							&& typeof(Object).IsAssignableFrom(fieldInfo.FieldType.GetElementType())
							&& fieldInfo.GetValue(comp) is System.Array array)
						{
							for (int i = 0; i < array.Length; i++)
								if (Owned(array.GetValue(i) as Object))
								{
									array.SetValue(null, i);
									count++;
								}
						}
					}
			return count;
		}

		internal static bool PrefabsAtRisk
		{
			get
			{
				if (!System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "VanillaAssetUtil"))
					return false;
				try
				{
					return !(MelonPreferences.HasEntry("VanillaAssetUtil", "Legacy Mode")
						&& MelonPreferences.GetEntryValue<bool>("VanillaAssetUtil", "Legacy Mode"));
				}
				catch { return true; }
			}
		}

		static bool _warmed;

		internal static void WarmAtMenu(string tag, System.Action copy) => WarmAtMenu(tag, new[] { copy });

		internal static void WarmAtMenu(string tag, params System.Action[] steps)
		{
			if (_warmed || !PrefabsAtRisk) return;
			_warmed = true;
			MelonCoroutines.Start(Warm(tag, steps));
		}

		// A blocking load right after Ammo Upgrade Pack releases its vehicles in leo2_menu never comes back: WaitForCompletion
		// makes the main thread wait out the released bundles' UnloadAsync. An async load just queues behind the unload.
		static bool _atMenu;
		static readonly Dictionary<string, (string name, AsyncOperationHandle<GameObject> handle)> Pending =
			new Dictionary<string, (string, AsyncOperationHandle<GameObject>)>();
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
			if (UnitRegistry.Lookup?.AllUnits == null)
			{
				_warmed = false;
				yield break;
			}
			var pending = steps.Where(a => a != null).ToList();
			int total = pending.Count, failed = 0;
			for (int round = 1; pending.Count > 0; round++)
			{
				var again = new List<System.Action>();
				foreach (var step in pending)
				{
					_atMenu = true;
					try { step(); }
					catch (NotLoadedYet) { again.Add(step); }
					catch (System.Exception exception)
					{
						failed++;
						MelonLogger.Warning($"{tag}: copying a vanilla donor part at the main menu failed ({exception.GetType().Name}: {exception.Message}); it is copied at the first spawn.");
					}
					finally { _atMenu = false; }
					yield return null;
				}
				if (again.Count == 0) break;
				float start = Time.realtimeSinceStartup;
				while (Pending.Values.Any(l => !l.handle.IsDone) && Time.realtimeSinceStartup - start < LoadSeconds)
					yield return null;
				var done = Pending.Where(p => p.Value.handle.IsDone).ToList();
				foreach (var pair in done)
				{
					var handle = pair.Value.handle;
					var prefab = handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
					if (prefab != null) Loaded[pair.Key] = prefab;
					else
					{
						FailedAtMenu.Add(pair.Key);
						if (handle.IsValid()) Addressables.Release(handle);
					}
					Pending.Remove(pair.Key);
				}
				if (Pending.Count > 0 || round == Rounds)
				{
					failed += again.Count;
					MelonLogger.Warning($"{tag}: vanilla donor prefabs not loaded at the main menu ({(Pending.Count > 0 ? string.Join(", ", Pending.Values.Select(l => l.name)) + $" still loading after {LoadSeconds:0} s" : $"{Rounds} rounds")}); {again.Count} donor part(s) are copied at the first spawn.");
					break;
				}
				pending = again;
			}
			FailedAtMenu.Clear();
		}
	}
}
