using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GHPC;
using GHPC.AI.Interfaces;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class EraBricks
	{
		// GHPC sets ERA off on every hit, spall included. Kontakt-5 ignores up to 30 mm and HE fragments from beyond 10 m.
		// Relikt gets the same rule, inferred.
		internal const float MaxCalibreMm = 30f;
		internal const float BurstRangeM = 10f;
		internal const string OpenSuffix = "_open";
		internal const string LidSuffix = "_lid";
		internal const string SootSuffix = "_soot";
		internal const string LinksAsset = "era_links";
		static readonly Dictionary<string, string> _links = new Dictionary<string, string>();

		internal static void LoadLinks(TextAsset links)
		{
			_links.Clear();
			if (links == null) return;
			foreach (var line in links.text.Split('\n'))
			{
				var f = line.TrimEnd('\r').Split('\t');
				if (f.Length >= 2 && !f[0].StartsWith("#")) _links[f[0]] = f[1];
			}
		}

		internal const string GoneAsset = "era_gone";
		internal const string GoneArmourPrefix = "box_";
		static readonly Dictionary<string, string[]> _gone = new Dictionary<string, string[]>();
		static readonly Dictionary<GameObject, List<(Transform part, GameObject armour)>> _goneBy = new Dictionary<GameObject, List<(Transform, GameObject)>>();

		internal static void LoadGone(TextAsset gone)
		{
			_gone.Clear();
			if (gone == null) return;
			foreach (var line in gone.text.Split('\n'))
			{
				var f = line.TrimEnd('\r').Split('\t');
				if (f.Length >= 2 && !f[0].StartsWith("#") && f[1].Length > 0) _gone[f[0]] = f[1].Split(',');
			}
		}

		internal static bool Vanish;
		internal static Func<bool> Look = () => true;
		internal static Func<string, bool> VanishRest = n => false;
		internal const float ScorchAmount = 1f;
		const string DetonatedEvent = "Detonated ERA";
		static readonly int ScorchProp = Shader.PropertyToID("_ScorchAmount");

		static readonly Dictionary<UniformArmor, Transform> _ours = new Dictionary<UniformArmor, Transform>();
		static readonly Dictionary<UniformArmor, Transform> _lidOf = new Dictionary<UniformArmor, Transform>();
		static readonly Dictionary<UniformArmor, Transform> _sootOf = new Dictionary<UniformArmor, Transform>();
		static readonly HashSet<Transform> _linked = new HashSet<Transform>();
		static bool _patched;

		internal static void Register(UniformArmor armour, Transform vehicleRoot)
		{
			foreach (var dead in _ours.Keys.Where(k => k == null).ToList()) _ours.Remove(dead);
			_linked.RemoveWhere(t => t == null);
			foreach (var dead in _goneBy.Keys.Where(k => k == null).ToList()) _goneBy.Remove(dead);
			_done.RemoveWhere(a => a == null);
			foreach (var dead in _roundOf.Keys.Where(k => k == null).ToList()) _roundOf.Remove(dead);
			_ours[armour] = vehicleRoot;
			Version++;
			if (vehicleRoot != null && vehicleRoot.GetComponent<EraWatch>() == null) vehicleRoot.gameObject.AddComponent<EraWatch>();
			if (!_patched) Patch();
		}

		static readonly HashSet<UniformArmor> _done = new HashSet<UniformArmor>();
		internal static int Version;
		static readonly Dictionary<UniformArmor, string> _roundOf = new Dictionary<UniformArmor, string>();

		internal static void SetOffDetonated(List<UniformArmor> pieces)
		{
			foreach (var a in pieces)
			{
				if (a == null || !a.IsDetonated || _done.Contains(a)) continue;
				_done.Add(a);
				SetOff(a);
			}
		}

		internal static bool Insensitive(LiveRound r, Vector3 brick)
		{
			if (r.IsSpall)
			{
				var shot = r.ShotInfo;
				var parent = shot?.ParentShot?.TypeInfo;
				bool burstNear = parent != null && parent.TntEquivalentKg > 0f && !Small(parent) && (shot.StartPosition - brick).sqrMagnitude <= BurstRangeM * BurstRangeM;
				return !burstNear;
			}
			return Small(r.Info);
		}

		static bool Small(AmmoType a) => a != null && a.Caliber > 0f && a.Caliber <= MaxCalibreMm;   // missiles and some APFSDS carry calibre 0

		internal static List<UniformArmor> Of(Transform root) => _ours.Where(kv => kv.Value == root && kv.Key != null).Select(kv => kv.Key).ToList();

		// Not Collider.bounds: armour colliders stay off until a round's near, and an off collider reports its pivot.
		internal static Vector3 Centre(UniformArmor a)
		{
			if (a.CenterPoint != null) return a.CenterPoint.position;
			var m = a.GetComponent<MeshCollider>()?.sharedMesh ?? a.GetComponent<MeshFilter>()?.sharedMesh;
			return m != null ? a.transform.TransformPoint(m.bounds.center) : a.transform.position;
		}

		// Linked at the first detonation: the turret's collider group is stale just after spawn.
		internal static void Link(Transform root)
		{
			_linked.Add(root);
			var bricks = new List<(Transform t, Transform open, Bounds b)>();
			foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
			{
				if (mf.sharedMesh == null || mf.name.EndsWith(OpenSuffix) || mf.transform.parent == null) continue;
				var open = mf.transform.parent.Find(mf.name + OpenSuffix);
				if (open == null || open.GetComponent<Renderer>() == null || mf.GetComponent<Renderer>() == null) continue;
				var b = mf.sharedMesh.bounds; b.Expand(0.04f);
				bricks.Add((mf.transform, open, b));
			}
			var assigned = new List<(UniformArmor a, Transform t)>();
			foreach (var a in Of(root))
			{
				var pts = Points(a);
				int best = -1, bestIn = 0; float bestD = float.MaxValue;
				// Geometry alone puts a cheek box's element plates in the next box, they overlap its bounds.
				if (_links.TryGetValue(a.name, out var linkedName)) { best = bricks.FindIndex(b => b.t.name == linkedName); if (best >= 0) bestIn = pts.Count; }
				bool byGeometry = best < 0;
				for (int i = 0; byGeometry && i < bricks.Count; i++)
				{
					var (t, _, b) = bricks[i];
					int inside = 0; float d = float.MaxValue;
					foreach (var p in pts)
					{
						var q = t.InverseTransformPoint(p);
						if (b.Contains(q)) inside++;
						else d = Mathf.Min(d, (b.ClosestPoint(q) - q).magnitude);
					}
					if (inside > 0) d = 0f;
					if (inside > bestIn || (inside == bestIn && d < bestD)) { best = i; bestIn = inside; bestD = d; }
				}
				if (best < 0 || (bestIn == 0 && bestD > 0.1f)) continue;
				var (bt, bo, _) = bricks[best];
				a.UndetonatedObjects = new[] { bt.gameObject };
				a.DetonatedObjects = new[] { bo.gameObject };
				assigned.Add((a, bt));
				var lid = bt.parent.Find(bt.name + LidSuffix);
				_lidOf[a] = lid;
				_sootOf[a] = bt.parent.Find(bt.name + SootSuffix);
			}
			var taken = new HashSet<Transform>(assigned.Select(x => x.t));
			foreach (var (a, bt) in assigned)
			{
				int at = bt.name.LastIndexOf("_c", StringComparison.Ordinal);
				if (at <= 0) continue;
				string brick = bt.name.Substring(0, at);
				var extra = bricks.Where(x => !taken.Contains(x.t) && x.t.parent == bt.parent && x.t.name.StartsWith(brick + "_c", StringComparison.Ordinal)).ToList();
				if (extra.Count == 0) continue;
				a.UndetonatedObjects = a.UndetonatedObjects.Concat(extra.Select(x => x.t.gameObject)).ToArray();
				a.DetonatedObjects = a.DetonatedObjects.Concat(extra.Select(x => x.open.gameObject)).ToArray();
			}
			if (_gone.Count > 0)
			{
				var all = root.GetComponentsInChildren<Transform>(true);
				var armourGroups = Of(root).Select(a => a.transform.parent).Where(p => p != null).Distinct().ToList();
				var armourPieces = armourGroups.SelectMany(p => p.GetComponentsInChildren<UniformArmor>(true)).Concat(root.GetComponentsInChildren<UniformArmor>(true)).ToList();
				foreach (var kv in _gone)
				{
					var part = all.FirstOrDefault(t => t.name == kv.Key && t.GetComponent<Renderer>() != null);
					if (part == null) continue;
					var armour = armourPieces.FirstOrDefault(u => u.name == GoneArmourPrefix + kv.Key)?.gameObject;
					foreach (var cname in kv.Value)
					{
						var c = bricks.FirstOrDefault(b => b.t.name == cname).t;
						if (c == null) continue;
						if (!_goneBy.TryGetValue(c.gameObject, out var list)) _goneBy[c.gameObject] = list = new List<(Transform, GameObject)>();
						list.Add((part, armour));
					}
				}
			}
		}

		static void GoneWith(UniformArmor a)
		{
			foreach (var go in a.UndetonatedObjects ?? new GameObject[0])
			{
				if (go == null || !_goneBy.TryGetValue(go, out var list)) continue;
				foreach (var (part, armour) in list)
				{
					if (part == null) continue;
					int off = 0;
					foreach (var r in part.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; off++; }
					if (off == 0) continue;
					if (armour != null && armour.activeSelf) armour.SetActive(false);
				}
			}
		}

		static List<Vector3> Points(UniformArmor a)
		{
			var mc = a.GetComponent<MeshCollider>();
			var m = mc != null ? mc.sharedMesh : null;
			if (m == null || !m.isReadable) return new List<Vector3> { Centre(a) };
			var t = a.transform;
			return m.vertices.Select(v => t.TransformPoint(v)).ToList();
		}

		// No Harmony __state: it's keyed by patch class name, and two ports carrying this class overwrote each other's.
		[ThreadStatic] static LiveRound _round;
		[ThreadStatic] static bool _hadLine, _parentHadLine;
		[ThreadStatic] static Stack<(LiveRound round, bool had, bool parentHad)> _penStack;
		static readonly AccessTools.FieldRef<LiveRound.ShotStory, HashSet<string>> StoryEvents = Reflect.FieldRefAccess<LiveRound.ShotStory, HashSet<string>>("events");
		static readonly AccessTools.FieldRef<LiveRound.ShotStory, StringBuilder> StoryText = Reflect.FieldRefAccess<LiveRound.ShotStory, StringBuilder>("builder");

		static void Patch()
		{
			_patched = true;
			try
			{
				var harmony = new HarmonyLib.Harmony("ghpcmods.erabricks." + typeof(EraBricks).Assembly.GetName().Name);
				var pen = Reflect.Method(typeof(LiveRound), "penCheck", new[] { typeof(Collider), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(ITarget) });
				var det = Reflect.Method(typeof(UniformArmor), nameof(UniformArmor.Detonate), Type.EmptyTypes);
				if (pen == null || det == null) return;
				harmony.Patch(pen, prefix: new HarmonyMethod(typeof(EraBricks), nameof(PenPrefix)), finalizer: new HarmonyMethod(typeof(EraBricks), nameof(PenFinalizer)));
				harmony.Patch(det, prefix: new HarmonyMethod(typeof(EraBricks), nameof(DetonatePrefix)));
			}
			catch (Exception e) { MelonLogger.Warning($"ERA bricks: patching failed ({e.GetType().Name}: {e.Message}); bricks behave as in GHPC."); }
		}

		static void PenPrefix(LiveRound __instance)
		{
			(_penStack ?? (_penStack = new Stack<(LiveRound, bool, bool)>())).Push((_round, _hadLine, _parentHadLine));
			try
			{
				bool had = Said(__instance.Story), parentHad = Said(__instance.ParentRound?.Story);
				(_round, _hadLine, _parentHadLine) = (__instance, had, parentHad);
			}
			catch (Exception e) { PatchGuard.Failed("ERA bricks: round in penCheck", e); }
		}

		static Exception PenFinalizer()
		{
			if (_penStack != null && _penStack.Count > 0) (_round, _hadLine, _parentHadLine) = _penStack.Pop();
			else (_round, _hadLine, _parentHadLine) = (null, false, false);
			return null;
		}

		static bool Said(LiveRound.ShotStory story)
		{
			if (story == null) return false;
			var events = StoryEvents(story);
			return events != null && events.Contains(DetonatedEvent);
		}

		static bool DetonatePrefix(UniformArmor __instance)
		{
			try
			{
				if (__instance.IsDetonated || !_ours.TryGetValue(__instance, out var root)) return true;
				var r = _round;
				if (r != null && Insensitive(r, Centre(__instance)))
				{
					Unsay(r);
					return false;
				}
				if (root != null && !_linked.Contains(root) && Look()) Link(root);
				_roundOf[__instance] = r?.Info?.Name;
			}
			catch (Exception e) { PatchGuard.Failed("ERA bricks: set-off rule", e); }
			return true;
		}

		static void SetOff(UniformArmor a)
		{
			try
			{
				if (!Look())
				{
					foreach (var go in a.UndetonatedObjects ?? new GameObject[0]) if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
					foreach (var go in a.DetonatedObjects ?? new GameObject[0]) if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
					return;
				}
				var at = Centre(a);
				Blast.Play(at, Blast.Size.EraBrick);
				NetCut.Cut(a.UndetonatedObjects?.FirstOrDefault(g => g != null)?.transform);
				GoneWith(a);
				if (Vanish)
				{
					foreach (var go in a.DetonatedObjects ?? new GameObject[0])
						if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
					var sr0 = _sootOf.TryGetValue(a, out var s) && s != null ? s.GetComponent<Renderer>() : null;
					if (sr0 != null) sr0.enabled = true;
					RestGone(a);
					return;
				}
				Char(a.DetonatedObjects);
				if (_sootOf.TryGetValue(a, out var sootT) && sootT != null) { var sr = sootT.GetComponent<Renderer>(); if (sr != null) sr.enabled = true; }
				if (_lidOf.TryGetValue(a, out var lid) && lid != null)
				{
					if (lid.GetComponent<EraLid>() == null)
					{
						_ours.TryGetValue(a, out var root);
						EraLid.Throw(lid, root != null ? root.GetComponent<Rigidbody>() : null);
						Char(new[] { lid.gameObject });
					}
				}
			}
			catch (Exception e) { PatchGuard.Failed("ERA bricks: blast", e); }
		}

		static void RestGone(UniformArmor a)
		{
			var c = a.UndetonatedObjects?.FirstOrDefault(g => g != null)?.transform;
			if (c == null || c.parent == null) return;
			int at = c.name.LastIndexOf("_c", StringComparison.Ordinal);
			if (at <= 0) return;
			string brick = c.name.Substring(0, at);
			if (!VanishRest(brick)) return;
			var rest = c.parent.Find(brick);
			if (rest == null) return;
			var containers = Enumerable.Range(0, c.parent.childCount).Select(i => c.parent.GetChild(i))
				.Where(t => t.name.StartsWith(brick + "_c", StringComparison.Ordinal) && int.TryParse(t.name.Substring(brick.Length + 2), out _));
			if (containers.Any(t => t.GetComponent<Renderer>()?.enabled == true)) return;
			foreach (var r in rest.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
		}

		internal static void Char(GameObject[] objects)
		{
			foreach (var go in objects ?? new GameObject[0])
			{
				if (go == null) continue;
				foreach (var r in go.GetComponentsInChildren<Renderer>(true))
				{
					foreach (var m in r.materials) if (m != null && m.HasProperty(ScorchProp)) m.SetFloat(ScorchProp, ScorchAmount);
				}
			}
		}

		static void Unsay(LiveRound r)
		{
			foreach (var (story, had) in new[] { (r.Story, _hadLine), (r.ParentRound?.Story, _parentHadLine) })
			{
				if (story == null || had) continue;
				var text = StoryText(story); var events = StoryEvents(story);
				string line = DetonatedEvent + Environment.NewLine;
				if (text == null || events == null || text.Length < line.Length || text.ToString(text.Length - line.Length, line.Length) != line) continue;
				text.Length -= line.Length;
				events.Remove(DetonatedEvent);
			}
		}
	}

	internal sealed class EraWatch : MonoBehaviour
	{
		List<UniformArmor> _pieces;
		int _version = -1;

		void LateUpdate()
		{
			if (_version != EraBricks.Version) { _pieces = EraBricks.Of(transform); _version = EraBricks.Version; }
			EraBricks.SetOffDetonated(_pieces);
		}
	}

	// Never destroyed: HeatSource and FlammablesManager hold renderer lists for the vehicle's life and would throw.
	internal sealed class EraLid : MonoBehaviour
	{
		internal const float LidSpeed = 6f, SideSpeed = 3f, LifeS = 60f, PlateM = 0.015f, SteelKgM3 = 7850f;
		internal float MassKg;
		float _t0;
		Rigidbody _rb;
		GameObject _col, _dp;

		internal static void Throw(Transform lid, Rigidbody vehicle)
		{
			var go = lid.gameObject;
			if (go.GetComponent<EraLid>() != null) return;
			var l = go.AddComponent<EraLid>();
			var r = go.GetComponent<Renderer>();
			if (r != null) r.enabled = true;
			var mf = go.GetComponent<MeshFilter>();
			var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(0.3f, 0.02f, 0.3f));
			l.MassKg = Mathf.Clamp(b.size.x * b.size.z * PlateM * SteelKgM3, 2f, 30f);
			var colGo = new GameObject("lid collider");
			colGo.SetActive(false);
			colGo.layer = 14;
			colGo.transform.SetParent(lid, false);
			var box = colGo.AddComponent<BoxCollider>();
			box.center = b.center; box.size = Vector3.Max(b.size, new Vector3(0.03f, 0.03f, 0.03f));
			l._col = colGo;
			var dpGo = new GameObject("lid throw");
			dpGo.SetActive(false);
			dpGo.transform.SetParent(lid, false);
			var dp = dpGo.AddComponent<GHPC.Effects.DetachableParent>();
			dp.Transform = lid; dp.UpReferenceTransform = lid; dp.ForceReceivingLocation = lid;
			dp.MassKg = l.MassKg;
			dp.MaxTorque = l.MassKg * 0.4f; dp.MinLateralForce = 0f; dp.MaxLateralForce = 0f;
			dp.DownforceMultiplier = 0f; dp.LocalShiftOnDetach = new Vector3(0f, 0.02f, 0f);
			dp.ColliderObjects = new[] { colGo }; dp.ObjectsToDeactivate = new GameObject[0]; dp.TransformsToLeaveBehind = new Transform[0];
			dp.AarHideItemsParents = new Transform[0]; dp.OtherParents = new Transform[0];
			dpGo.SetActive(true);
			l._dp = dpGo;
			var up = lid.up;
			dp.Detach(0f);
			l._rb = dp.RBody;
			if (l._rb != null)
			{
				l._rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
				var side = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, up).normalized;
				l._rb.velocity = up * LidSpeed + side * (SideSpeed * UnityEngine.Random.Range(0.5f, 1f)) + (vehicle != null ? vehicle.velocity : Vector3.zero);
			}
			l._t0 = Time.time;
		}

		void Update()
		{
			float age = Time.time - _t0;
			if (_rb != null && ((age > 1.5f && _rb.IsSleeping()) || transform.position.y < -100f)) Rest();
			if (age > LifeS)
			{
				var r = GetComponent<Renderer>();
				if (r != null) r.enabled = false;
				Rest();
				enabled = false;
			}
		}

		void Rest()
		{
			if (_dp != null) Destroy(_dp);
			if (_col != null) Destroy(_col);
			if (_rb != null) Destroy(_rb);
			_dp = null; _col = null; _rb = null;
		}
	}
}
