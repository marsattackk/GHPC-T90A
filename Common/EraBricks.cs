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
	// Kontakt-5 and Relikt. A brick goes off on a hit, takes its neighbours with it, throws its lid and leaves the
	// plate under bare.
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

		internal static void LoadLinks(AssetBundle bundle)
		{
			_links.Clear();
			var text = PortText.Load(bundle, LinksAsset);
			if (text == null) return;
			foreach (var line in text.Split('\n'))
			{
				var fields = line.TrimEnd('\r').Split('\t');
				if (fields.Length >= 2 && !fields[0].StartsWith("#")) _links[fields[0]] = fields[1];
			}
		}

		internal static void AddLinks(IEnumerable<(string Piece, string Container)> links)
		{
			foreach (var (piece, container) in links) _links[piece] = container;
		}

		internal const string KitRootPrefix = "KIT_";
		static readonly HashSet<Transform> _retired = new HashSet<Transform>();

		internal static void Retire(Transform brick)
		{
			if (brick != null) _retired.Add(brick);
		}

		internal static void Relink(Transform vehicleRoot)
		{
			if (vehicleRoot != null) _linked.Remove(vehicleRoot);
		}

		static bool OffKitObject(Transform part)
		{
			if (part.gameObject.activeInHierarchy) return false;
			for (var parent = part.parent; parent != null; parent = parent.parent)
				if (parent.name.StartsWith(KitRootPrefix, StringComparison.Ordinal)) return true;
			return false;
		}

		internal const string GoneAsset = "era_gone";
		internal const string GoneArmourPrefix = "box_";
		static readonly Dictionary<string, string[]> _gone = new Dictionary<string, string[]>();
		static readonly Dictionary<GameObject, List<(Transform part, GameObject armour)>> _goneBy =
			new Dictionary<GameObject, List<(Transform, GameObject)>>();

		internal static void LoadGone(AssetBundle bundle)
		{
			_gone.Clear();
			var text = PortText.Load(bundle, GoneAsset);
			if (text == null) return;
			foreach (var line in text.Split('\n'))
			{
				var fields = line.TrimEnd('\r').Split('\t');
				if (fields.Length >= 2 && !fields[0].StartsWith("#") && fields[1].Length > 0) _gone[fields[0]] = fields[1].Split(',');
			}
		}

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal static bool Vanish;
#pragma warning restore CS0649
		internal static Func<bool> Look = () => true;
		internal static Func<string, bool> VanishRest = n => false;
		internal const float ScorchAmount = 1f;
		const string DetonatedEvent = "Detonated ERA";
		static readonly int ScorchProp = Shader.PropertyToID("_ScorchAmount");

		static readonly Dictionary<UniformArmor, Transform> _ours = new Dictionary<UniformArmor, Transform>();
		static readonly Dictionary<UniformArmor, Transform> _lidOf = new Dictionary<UniformArmor, Transform>();
		static readonly Dictionary<UniformArmor, Transform> _sootOf = new Dictionary<UniformArmor, Transform>();
		static readonly HashSet<Transform> _linked = new HashSet<Transform>();

		internal static Transform LidOf(UniformArmor a) => _lidOf.TryGetValue(a, out var lid) ? lid : null;
		static bool _patched;

		internal static void Register(UniformArmor armour, Transform vehicleRoot)
		{
			UnityKeys.Prune(_ours);
			_linked.RemoveWhere(p => p == null);
			_retired.RemoveWhere(p => p == null);
			UnityKeys.Prune(_goneBy);
			_done.RemoveWhere(p => p == null);
			_quietMates.RemoveWhere(p => p == null);
			UnityKeys.Prune(_roundOf);
			UnityKeys.Prune(_lidOf);
			UnityKeys.Prune(_sootOf);
			_ours[armour] = vehicleRoot;
			Version++;
			if (vehicleRoot != null && vehicleRoot.GetComponent<EraWatch>() == null) vehicleRoot.gameObject.AddComponent<EraWatch>();
			if (!_patched) Patch();
		}

		internal static void Unregister(UniformArmor armour)
		{
			if (armour == null) return;
			_ours.Remove(armour);
			_lidOf.Remove(armour);
			_sootOf.Remove(armour);
			_done.Remove(armour);
			_quietMates.Remove(armour);
			_roundOf.Remove(armour);
			Version++;
		}

		static readonly HashSet<UniformArmor> _done = new HashSet<UniformArmor>();
		internal static int Version;
		static readonly Dictionary<UniformArmor, string> _roundOf = new Dictionary<UniformArmor, string>();

		internal static int SetOffDetonated(List<UniformArmor> pieces)
		{
			int count = 0;
			foreach (var piece in pieces)
			{
				if (piece == null || !piece.IsDetonated || _done.Contains(piece)) continue;
				_done.Add(piece);
				if (_quietMates.Remove(piece)) continue;
				SetOff(piece);
				count++;
			}
			return count;
		}

		static readonly HashSet<UniformArmor> _quietMates = new HashSet<UniformArmor>();
		static bool _settingOffMates;

		internal static List<UniformArmor> MatesOf(UniformArmor a)
		{
			if (a == null || !_ours.TryGetValue(a, out var root) || !_links.TryGetValue(a.name, out var box))
				return new List<UniformArmor>();
			return Of(root).Where(m => m != a && _links.TryGetValue(m.name, out var mateBox) && mateBox == box).ToList();
		}

		static void DetonatePostfix(UniformArmor __instance)
		{
			if (_settingOffMates) return;
			try
			{
				if (__instance == null || !__instance.IsDetonated || !_ours.ContainsKey(__instance)) return;
				string box = _links.TryGetValue(__instance.name, out var linkedBox) ? linkedBox : null;
				_settingOffMates = true;
				foreach (var mate in MatesOf(__instance))
				{
					if (mate.IsDetonated) continue;
					var effect = mate.DetonateEffect;
					mate.DetonateEffect = null;
					try { mate.Detonate(); }
					finally { mate.DetonateEffect = effect; }
					if (mate.IsDetonated) _quietMates.Add(mate);
				}
			}
			catch (Exception exception) { PatchGuard.Failed("ERA bricks: container mates", exception); }
			finally { _settingOffMates = false; }
		}

		internal static bool IsOurs(UniformArmor a) => a != null && _ours.ContainsKey(a);

		internal static bool Insensitive(LiveRound r, Vector3 brick)
		{
			if (r.IsSpall)
			{
				var shot = r.ShotInfo;
				var parent = shot?.ParentShot?.TypeInfo;
				bool burstNear = parent != null
					&& parent.TntEquivalentKg > 0f
					&& !Small(parent)
					&& (shot.StartPosition - brick).sqrMagnitude <= BurstRangeM * BurstRangeM;
				return !burstNear;
			}
			return Small(r.Info);
		}

		static bool Small(AmmoType a) => a != null && a.Caliber > 0f && a.Caliber <= MaxCalibreMm;   // missiles and some APFSDS carry calibre 0

		internal static List<UniformArmor> Of(Transform root)
			=> _ours.Where(p => p.Value == root && p.Key != null).Select(x => x.Key).ToList();

		// Not Collider.bounds: armour colliders stay off until a round's near, and an off collider reports its pivot.
		internal static Vector3 Centre(UniformArmor a)
		{
			if (a.CenterPoint != null) return a.CenterPoint.position;
			var mesh = a.GetComponent<MeshCollider>()?.sharedMesh ?? a.GetComponent<MeshFilter>()?.sharedMesh;
			return mesh != null ? a.transform.TransformPoint(mesh.bounds.center) : a.transform.position;
		}

		// Linked at the first detonation: the turret's collider group is stale just after spawn.
		internal static string Link(Transform root)
		{
			_linked.Add(root);
			var bricks = new List<(Transform t, Transform open, Bounds b)>();
			foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
			{
				if (meshFilter.sharedMesh == null || meshFilter.name.EndsWith(OpenSuffix) || meshFilter.transform.parent == null) continue;
				if (_retired.Contains(meshFilter.transform) || OffKitObject(meshFilter.transform)) continue;
				var open = meshFilter.transform.parent.Find(meshFilter.name + OpenSuffix);
				if (open == null || open.GetComponent<Renderer>() == null || meshFilter.GetComponent<Renderer>() == null) continue;
				var bounds = meshFilter.sharedMesh.bounds;
				bounds.Expand(0.04f);
				bricks.Add((meshFilter.transform, open, bounds));
			}
			int linked = 0, none = 0, byName = 0;
			var assigned = new List<(UniformArmor a, Transform t)>();
			foreach (var piece in Of(root))
			{
				var pts = Points(piece);
				int best = -1, bestIn = 0;
				float bestD = float.MaxValue;
				// Geometry alone puts a cheek box's element plates in the next box because they overlap its bounds.
				if (_links.TryGetValue(piece.name, out var linkedName))
				{
					best = bricks.FindIndex(b => b.t.name == linkedName);
					if (best >= 0)
					{
						bestIn = pts.Count;
						byName++;
					}
				}
				bool byGeometry = best < 0;
				for (int i = 0; byGeometry && i < bricks.Count; i++)
				{
					var (brickPart, _, brickBounds) = bricks[i];
					int inside = 0;
					float distance = float.MaxValue;
					foreach (var point in pts)
					{
						var localPoint = brickPart.InverseTransformPoint(point);
						if (brickBounds.Contains(localPoint)) inside++;
						else distance = Mathf.Min(distance, (brickBounds.ClosestPoint(localPoint) - localPoint).magnitude);
					}
					if (inside > 0) distance = 0f;
					if (inside > bestIn || (inside == bestIn && distance < bestD))
					{
						best = i;
						bestIn = inside;
						bestD = distance;
					}
				}
				if (best < 0 || (bestIn == 0 && bestD > 0.1f))
				{
					none++;
					continue;
				}
				var (bestBrick, bestOpen, _) = bricks[best];
				piece.UndetonatedObjects = new[] { bestBrick.gameObject };
				piece.DetonatedObjects = new[] { bestOpen.gameObject };
				assigned.Add((piece, bestBrick));
				var lid = bestBrick.parent.Find(bestBrick.name + LidSuffix);
				_lidOf[piece] = lid;
				_sootOf[piece] = bestBrick.parent.Find(bestBrick.name + SootSuffix);
				linked++;
			}
			var taken = new HashSet<Transform>(assigned.Select(e => e.t));
			int mates = 0;
			foreach (var (brickArmour, brickTransform) in assigned)
			{
				int suffixIndex = brickTransform.name.LastIndexOf("_c", StringComparison.Ordinal);
				if (suffixIndex <= 0) continue;
				string brick = brickTransform.name.Substring(0, suffixIndex);
				var extra = bricks
					.Where(c => !taken.Contains(c.t)
						&& c.t.parent == brickTransform.parent
						&& c.t.name.StartsWith(brick + "_c", StringComparison.Ordinal))
					.ToList();
				if (extra.Count == 0) continue;
				brickArmour.UndetonatedObjects = brickArmour.UndetonatedObjects
					.Concat(extra.Select(c => c.t.gameObject))
					.ToArray();
				brickArmour.DetonatedObjects = brickArmour.DetonatedObjects
					.Concat(extra.Select(c => c.open.gameObject))
					.ToArray();
				mates += extra.Count;
			}
			int goneParts = 0, goneLinks = 0;
			if (_gone.Count > 0)
			{
				var all = root.GetComponentsInChildren<Transform>(true);
				var armourGroups = Of(root).Select(p => p.transform.parent).Where(p => p != null).Distinct().ToList();
				var armourPieces = armourGroups
					.SelectMany(p => p.GetComponentsInChildren<UniformArmor>(true))
					.Concat(root.GetComponentsInChildren<UniformArmor>(true))
					.ToList();
				foreach (var entry in _gone)
				{
					var part = all.FirstOrDefault(c => c.name == entry.Key && c.GetComponent<Renderer>() != null);
					if (part == null) continue;
					var armour = armourPieces.FirstOrDefault(p => p.name == GoneArmourPrefix + entry.Key)?.gameObject;
					goneParts++;
					foreach (var cname in entry.Value)
					{
						var container = bricks.FirstOrDefault(b => b.t.name == cname).t;
						if (container == null) continue;
						if (!_goneBy.TryGetValue(container.gameObject, out var list))
							_goneBy[container.gameObject] = list = new List<(Transform, GameObject)>();
						list.Add((part, armour));
						goneLinks++;
					}
				}
			}
			return $"ERA bricks of {root.name}: {linked} armour pieces linked to {bricks.Count} visual bricks ({byName} by the build's links, the rest by geometry), {none} without one, {mates} unlinked containers going with a linked one, lids {_lidOf.Count(p => p.Value != null && p.Key != null && _ours.TryGetValue(p.Key, out var ownRoot) && ownRoot == root)}, gone parts {goneParts} on {goneLinks} containers"
				;
		}

		static string GoneWith(UniformArmor a)
		{
			var notes = new List<string>();
			foreach (var brick in a.UndetonatedObjects ?? new GameObject[0])
			{
				if (brick == null || !_goneBy.TryGetValue(brick, out var list)) continue;
				foreach (var (part, armour) in list)
				{
					if (part == null) continue;
					int off = 0;
					foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
						if (renderer.enabled)
						{
							renderer.enabled = false;
							off++;
						}
					if (off == 0) continue;
					bool armourOff = false;
					if (armour != null && armour.activeSelf)
					{
						armour.SetActive(false);
						armourOff = true;
					}
					notes.Add($"{part.name} gone ({off} renderers off{(armourOff ? $", armour {armour.name} off" : "")})");
				}
			}
			return notes.Count > 0 ? string.Join(", ", notes) : "no gone parts";
		}

		static List<Vector3> Points(UniformArmor a)
		{
			var meshCollider = a.GetComponent<MeshCollider>();
			var mesh = meshCollider != null ? meshCollider.sharedMesh : null;
			if (mesh == null || !mesh.isReadable) return new List<Vector3> { Centre(a) };
			var armourTransform = a.transform;
			return mesh.vertices.Select(v => armourTransform.TransformPoint(v)).ToList();
		}

		// No Harmony __state: it's keyed by patch class name, and two ports carrying this class overwrote each other's.
		[ThreadStatic] static LiveRound _round;
		[ThreadStatic] static bool _hadLine, _parentHadLine;
		[ThreadStatic] static Stack<(LiveRound round, bool had, bool parentHad)> _penStack;
		static readonly AccessTools.FieldRef<LiveRound.ShotStory, HashSet<string>> StoryEvents =
			Reflect.FieldRefAccess<LiveRound.ShotStory, HashSet<string>>("events");
		static readonly AccessTools.FieldRef<LiveRound.ShotStory, StringBuilder> StoryText =
			Reflect.FieldRefAccess<LiveRound.ShotStory, StringBuilder>("builder");

		static void Patch()
		{
			_patched = true;
			try
			{
				var harmony = new HarmonyLib.Harmony("ghpcmods.erabricks." + typeof(EraBricks).Assembly.GetName().Name);
				var pen = Reflect.Method(
					typeof(LiveRound),
					"penCheck",
					new[] { typeof(Collider), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(ITarget) });
				var det = Reflect.Method(typeof(UniformArmor), nameof(UniformArmor.Detonate), Type.EmptyTypes);
				if (pen == null || det == null) return;
				harmony.Patch(
					pen,
					prefix: new HarmonyMethod(typeof(EraBricks), nameof(PenPrefix)),
					finalizer: new HarmonyMethod(typeof(EraBricks), nameof(PenFinalizer)));
				harmony.Patch(
					det,
					prefix: new HarmonyMethod(typeof(EraBricks), nameof(DetonatePrefix)),
					postfix: new HarmonyMethod(typeof(EraBricks), nameof(DetonatePostfix)));
			}
			catch (Exception exception)
			{
				MelonLogger.Warning($"ERA bricks: patching failed ({exception.GetType().Name}: {exception.Message}); bricks behave as in GHPC.");
			}
		}

		static void PenPrefix(LiveRound __instance)
		{
			(_penStack ?? (_penStack = new Stack<(LiveRound, bool, bool)>())).Push((_round, _hadLine, _parentHadLine));
			try
			{
				bool had = Said(__instance.Story), parentHad = Said(__instance.ParentRound?.Story);
				(_round, _hadLine, _parentHadLine) = (__instance, had, parentHad);
			}
			catch (Exception exception) { PatchGuard.Failed("ERA bricks: round in penCheck", exception); }
		}

		static Exception PenFinalizer()
		{
			if (_penStack != null && _penStack.Count > 0) (_round, _hadLine, _parentHadLine) = _penStack.Pop();
			else (_round, _hadLine, _parentHadLine) = (null, false, false);
			return null;
		}

		static bool Said(LiveRound.ShotStory story)
		{
			if (story == null || StoryEvents == null) return false;
			var events = StoryEvents(story);
			return events != null && events.Contains(DetonatedEvent);
		}

		static bool DetonatePrefix(UniformArmor __instance)
		{
			try
			{
				if (__instance.IsDetonated || !_ours.TryGetValue(__instance, out var root)) return true;
				var round = _round;
				if (round != null && Insensitive(round, Centre(__instance)))
				{
					Unsay(round);
					return false;
				}
				if (root != null && !_linked.Contains(root) && Look())
				{
					string links = Link(root);
				}
				_roundOf[__instance] = round?.Info?.Name;
			}
			catch (Exception exception) { PatchGuard.Failed("ERA bricks: set-off rule", exception); }
			return true;
		}

		static void SetOff(UniformArmor a)
		{
			try
			{
				string cause = _roundOf.TryGetValue(a, out var roundName) && roundName != null ? $" by {roundName}" : "";
				if (!Look())
				{
					foreach (var brick in a.UndetonatedObjects ?? new GameObject[0])
						if (brick != null) foreach (var renderer in brick.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
					foreach (var openBrick in a.DetonatedObjects ?? new GameObject[0])
						if (openBrick != null)
							foreach (var renderer in openBrick.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
					return;
				}
				var position = Centre(a);
				Blast.Play(position, Blast.Size.EraBrick);
				string nets = NetCut.Cut(a.UndetonatedObjects?.FirstOrDefault(b => b != null)?.transform);
				string gone = GoneWith(a);
				if (Vanish)
				{
					int off = 0;
					foreach (var openBrick in a.DetonatedObjects ?? new GameObject[0])
						if (openBrick != null)
							foreach (var renderer in openBrick.GetComponentsInChildren<Renderer>(true))
							{
								renderer.enabled = false;
								off++;
							}
					var sr0 = _sootOf.TryGetValue(a, out var sootMark) && sootMark != null ? sootMark.GetComponent<Renderer>() : null;
					if (sr0 != null) sr0.enabled = true;
					bool sooted = sr0 != null;
					string rest = RestGone(a);
					return;
				}
				int charred = Char(a.DetonatedObjects);
				if (_sootOf.TryGetValue(a, out var sootT) && sootT != null)
				{
					var sootRenderer = sootT.GetComponent<Renderer>();
					if (sootRenderer != null)
					{
						sootRenderer.enabled = true;
					}
				}
				string lidNote = "no lid";
				if (_lidOf.TryGetValue(a, out var lid) && lid != null)
				{
					if (lid.GetComponent<EraLid>() != null) lidNote = "lid thrown already";
					else
					{
						_ours.TryGetValue(a, out var root);
						var thrown = EraLid.Throw(lid, root != null ? root.GetComponent<Rigidbody>() : null);
						Char(new[] { lid.gameObject });
						lidNote = thrown != null ? $"lid thrown ({thrown.MassKg:0.0} kg at {EraLid.LidSpeed:0} m/s)" : "lid not thrown";
					}
				}
			}
			catch (Exception exception) { PatchGuard.Failed("ERA bricks: blast", exception); }
		}

		static string RestGone(UniformArmor a)
		{
			var container = a.UndetonatedObjects?.FirstOrDefault(c => c != null)?.transform;
			if (container == null || container.parent == null) return "no container";
			int suffixIndex = container.name.LastIndexOf("_c", StringComparison.Ordinal);
			if (suffixIndex <= 0) return "no brick name";
			string brick = container.name.Substring(0, suffixIndex);
			if (!VanishRest(brick)) return "rest kept";
			var rest = container.parent.Find(brick);
			if (rest == null) return "no rest object";
			var containers = Enumerable.Range(0, container.parent.childCount).Select(i => container.parent.GetChild(i))
				.Where(c => c.name.StartsWith(brick + "_c", StringComparison.Ordinal)
					&& int.TryParse(c.name.Substring(brick.Length + 2), out _));
			if (containers.Any(c => c.GetComponent<Renderer>()?.enabled == true)) return "rest kept (containers left)";
			int count = 0;
			foreach (var renderer in rest.GetComponentsInChildren<Renderer>(true))
			{
				renderer.enabled = false;
				count++;
			}
			return $"rest '{brick}' gone ({count} renderers)";
		}

		internal static int Char(GameObject[] objects)
		{
			int count = 0;
			foreach (var part in objects ?? new GameObject[0])
			{
				if (part == null) continue;
				foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
				{
					bool any = false;
					foreach (var material in renderer.materials)
						if (material != null && material.HasProperty(ScorchProp))
						{
							material.SetFloat(ScorchProp, ScorchAmount);
							any = true;
						}
					if (any) count++;
				}
			}
			return count;
		}

		static void Unsay(LiveRound r)
		{
			if (StoryText == null || StoryEvents == null) return;
			foreach (var (story, had) in new[] { (r.Story, _hadLine), (r.ParentRound?.Story, _parentHadLine) })
			{
				if (story == null || had) continue;
				var text = StoryText(story);
				var events = StoryEvents(story);
				string line = DetonatedEvent + Environment.NewLine;
				if (text == null
					|| events == null
					|| text.Length < line.Length
					|| text.ToString(text.Length - line.Length, line.Length) != line)
					continue;
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
			if (_version != EraBricks.Version)
			{
				_pieces = EraBricks.Of(transform);
				_version = EraBricks.Version;
			}
			EraBricks.SetOffDetonated(_pieces);
		}
	}

	// Never destroyed: HeatSource and FlammablesManager hold renderer lists for the vehicle's life and would throw.
	internal sealed class EraLid : MonoBehaviour
	{
		internal const float LidSpeed = 6f, SideSpeed = 3f, LifeS = 60f, PlateM = 0.015f, SteelKgM3 = 7850f;
		internal static int Thrown;
		internal float MassKg;
		internal bool Resting => _rb == null;
		float _t0;
		Rigidbody _rb;
		GameObject _col, _dp;

		internal static EraLid Throw(Transform lid, Rigidbody vehicle)
		{
			var lidObject = lid.gameObject;
			if (lidObject.GetComponent<EraLid>() != null) return null;
			var eraLid = lidObject.AddComponent<EraLid>();
			var renderer = lidObject.GetComponent<Renderer>();
			if (renderer != null) renderer.enabled = true;
			var meshFilter = lidObject.GetComponent<MeshFilter>();
			var bounds = meshFilter != null && meshFilter.sharedMesh != null
				? meshFilter.sharedMesh.bounds
				: new Bounds(Vector3.zero, new Vector3(0.3f, 0.02f, 0.3f));
			eraLid.MassKg = Mathf.Clamp(bounds.size.x * bounds.size.z * PlateM * SteelKgM3, 2f, 30f);
			var colGo = new GameObject("lid collider");
			colGo.SetActive(false);
			colGo.layer = 14;
			colGo.transform.SetParent(lid, false);
			var box = colGo.AddComponent<BoxCollider>();
			box.center = bounds.center;
			box.size = Vector3.Max(bounds.size, new Vector3(0.03f, 0.03f, 0.03f));
			eraLid._col = colGo;
			var dpGo = new GameObject("lid throw");
			dpGo.SetActive(false);
			dpGo.transform.SetParent(lid, false);
			var detachable = dpGo.AddComponent<GHPC.Effects.DetachableParent>();
			detachable.Transform = lid;
			detachable.UpReferenceTransform = lid;
			detachable.ForceReceivingLocation = lid;
			detachable.MassKg = eraLid.MassKg;
			detachable.MaxTorque = eraLid.MassKg * 0.4f;
			detachable.MinLateralForce = 0f;
			detachable.MaxLateralForce = 0f;
			detachable.DownforceMultiplier = 0f;
			detachable.LocalShiftOnDetach = new Vector3(0f, 0.02f, 0f);
			detachable.ColliderObjects = new[] { colGo };
			detachable.ObjectsToDeactivate = new GameObject[0];
			detachable.TransformsToLeaveBehind = new Transform[0];
			detachable.AarHideItemsParents = new Transform[0];
			detachable.OtherParents = new Transform[0];
			dpGo.SetActive(true);
			eraLid._dp = dpGo;
			var up = lid.up;
			detachable.Detach(0f);
			eraLid._rb = detachable.RBody;
			if (eraLid._rb != null)
			{
				eraLid._rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
				var side = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, up).normalized;
				eraLid._rb.velocity = up * LidSpeed
					+ side * (SideSpeed * UnityEngine.Random.Range(0.5f, 1f))
					+ (vehicle != null ? vehicle.velocity : Vector3.zero);
			}
			eraLid._t0 = Time.time;
			Thrown++;
			return eraLid;
		}

		void Update()
		{
			float age = Time.time - _t0;
			if (_rb != null && ((age > 1.5f && _rb.IsSleeping()) || transform.position.y < -100f)) Rest();
			if (age > LifeS)
			{
				var lidRenderer = GetComponent<Renderer>();
				if (lidRenderer != null) lidRenderer.enabled = false;
				Rest();
				enabled = false;
			}
		}

		void Rest()
		{
			if (_dp != null) Destroy(_dp);
			if (_col != null) Destroy(_col);
			if (_rb != null) Destroy(_rb);
			_dp = null;
			_col = null;
			_rb = null;
		}
	}
}
