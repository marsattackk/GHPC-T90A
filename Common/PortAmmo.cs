using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Mission;
using GHPC.State;
using GHPC.UI.Menu;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using System.IO;
using TMPro;

namespace T90A.Common
{
	internal static class PortAmmo
	{
		internal sealed class Round
		{
			public string Id;
			public string Name;
			public string Source;
			public Action<AmmoType> Set;
			public float Prevalence = 1f;
			public int AfterMissions;
			public bool ScaleWithLogistics;
			public string Texture;
			public string Atlas;
			public string Shape;
		}

		internal sealed class Rack
		{
			public int Index;
			public int[] Counts;
			public int[] MissileSlots = new int[0];
			public bool NoRestockInto;
		}

		internal sealed class Settings
		{
			public string Tag = "Port";
			public string UnitKey;
			public string Prefix;
			public string SourceUnit = "T80B";
			public Round[] Apfsds = new Round[0];
			public Func<string> DefaultApfsds;
			public string ApfsdsListName;
			public string MenuSuffix;
			public string FixedHeat = "clip_3BK18M";
			public Round Missile;
			public Round[] Extra = new Round[0];
			public string[] EraDefeating = new string[0];
			public int[] TotalCounts;
			public Rack[] Racks = new Rack[0];
			public bool DropOtherRacks = false;
			public Vector3 GuidancePosition;
			public string GuidanceName = "guidance computer", BeamName = "guidance beam";
			public string CoaxFeed = "PKT";
			public int CoaxRounds, CoaxBelt = 250;
		}

		internal static Settings S;

		internal static readonly Dictionary<string, AmmoClipCodexScriptable> Apfsds = new Dictionary<string, AmmoClipCodexScriptable>();
		internal static AmmoClipCodexScriptable Heat;
		internal static AmmoClipCodexScriptable Missile;
		internal static readonly List<AmmoClipCodexScriptable> Extras = new List<AmmoClipCodexScriptable>();
		internal static AmmoLogisticsScriptable ApfsdsSet;
		static bool _built;
		static readonly MethodInfo RegisterAllBallistics = Reflect.Method(typeof(LoadoutManager), "RegisterAllBallistics");
		static readonly FieldInfo FeedMissileGuidance = Reflect.Field(typeof(AmmoFeed), "_missileGuidance");
		static readonly MethodInfo FeedGuidanceStopped = Reflect.Method(typeof(AmmoFeed), "MissileGuidanceStopped");

		internal static bool Build()
		{
			if (_built) return true;
			if (S == null) return false;
			var list = OwnLoad(UnitRegistry.Find(S.SourceUnit)?.BaseAmmoClipsReference);
			var clips = list?.AmmoClips ?? new AmmoClipCodexScriptable[0];
			AmmoType SourceOf(Round r) => clips.FirstOrDefault(c => c.name == r.Source)?.ClipType.MinimalPattern[0].AmmoType;
			Heat = clips.FirstOrDefault(c => c.name == S.FixedHeat);
			var missing = S.Apfsds.Concat(S.Missile != null ? new[] { S.Missile } : new Round[0]).Concat(S.Extra).Where(r => SourceOf(r) == null).Select(r => r.Source).ToList();
			if (Heat == null) missing.Add(S.FixedHeat);
			if (missing.Count > 0 || S.Apfsds.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} ammo: vanilla {string.Join(", ", missing)} not loaded yet; keeping base ammo.");
				return false;
			}
			foreach (var r in S.Apfsds) Apfsds[r.Id] = MakeClip(r, SourceOf(r));
			if (S.Missile != null)
			{
				Missile = MakeClip(S.Missile, SourceOf(S.Missile));
			}
			Extras.Clear();
			foreach (var r in S.Extra) Extras.Add(MakeClip(r, SourceOf(r)));
			ApfsdsSet = MakeApfsdsSet();
			_built = true;
			return true;
		}

		static readonly Dictionary<object, VehicleAmmoListScriptable> ownLists = new Dictionary<object, VehicleAmmoListScriptable>();

		static VehicleAmmoListScriptable OwnLoad(AssetReference reference)
		{
			if (reference == null || string.IsNullOrEmpty(reference.AssetGUID)) return null;
			var key = reference.RuntimeKey;
			if (!ownLists.TryGetValue(key, out var list) || list == null)
				ownLists[key] = list = Addressables.LoadAssetAsync<VehicleAmmoListScriptable>(key).WaitForCompletion();
			return list;
		}

		static AmmoLogisticsScriptable MakeApfsdsSet()
		{
			var set = ScriptableObject.CreateInstance<AmmoLogisticsScriptable>();
			set.name = $"ammo_logistics_{S.Prefix}_apfsds";
			set.hideFlags = HideFlags.DontUnloadUnusedAsset;
			set.AmmoLogistics = new AmmoReplacementOrder
			{
				Name = S.ApfsdsListName,
				AmmoOptions = S.Apfsds.Select(r => new AmmoReplacementOrder.AmmoTypeLogistics
				{
					AmmoType = Apfsds[r.Id], Prevalence = r.Prevalence, BlockUntilMissionsCompleted = r.AfterMissions,
					LogisticsModifierCurve = r.ScaleWithLogistics ? AnimationCurve.Linear(0f, 0f, 1f, 1f) : AnimationCurve.Constant(0f, 1f, 1f), AllowInCampaign = true,
				}).ToArray(),
			};
			return set;
		}

		// Clips match by value, so it holds no round to equal no option. Reusing the vanilla ClipType still matched.
		static AmmoClipCodexScriptable heatStandIn;

		static AmmoClipCodexScriptable HeatStandIn()
		{
			if (heatStandIn == null)
			{
				heatStandIn = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
				heatStandIn.name = $"clip_{S.Prefix}_{S.FixedHeat.Replace("clip_", "")}_fixed";
				heatStandIn.hideFlags = HideFlags.DontUnloadUnusedAsset;
				heatStandIn.ClipType = new AmmoType.AmmoClip { Name = Heat.ClipType.Name, Capacity = 1, MinimalPattern = new AmmoCodexScriptable[0] };
			}
			return heatStandIn;
		}

		internal static void HideFixedHeat(AmmoClipCodexScriptable[] clips)
		{
			if (Heat == null || clips == null) return;
			int i = Array.IndexOf(clips, Heat);
			if (i >= 0) clips[i] = HeatStandIn();
		}

		internal static void RestoreFixedHeat(AmmoClipCodexScriptable[] clips)
		{
			if (heatStandIn == null || clips == null) return;
			int i = Array.FindIndex(clips, c => ReferenceEquals(c, heatStandIn));
			if (i >= 0) clips[i] = Heat;
		}

		internal static AmmoClipCodexScriptable DefaultApfsds(bool warn)
		{
			string id = S.DefaultApfsds?.Invoke();
			if (Apfsds.TryGetValue(id ?? "", out var ap)) return ap;
			if (warn) MelonLogger.Warning($"{S.Tag} ammo: unknown APFSDS '{id}', using {S.Apfsds[0].Id}.");
			return Apfsds[S.Apfsds[0].Id];
		}

		static List<AmmoClipCodexScriptable> PortList(IEnumerable<AmmoClipCodexScriptable> baseClips, AmmoClipCodexScriptable ap)
		{
			var list = baseClips.Take(3).ToList();
			list[0] = ap;
			list[1] = Heat;
			if (Missile != null) list.Add(Missile);
			list.AddRange(Extras);
			return list;
		}

		internal static bool IsPort(string unitKey) => S != null && string.Equals(unitKey, S.UnitKey, StringComparison.OrdinalIgnoreCase);

		internal static List<AmmoLogisticsScriptable> SetsForMenu(AssetReference baseList, AmmoLogisticsScriptable[] vanilla, out List<int> preferred)
		{
			preferred = null;
			if (!Build()) return null;
			var list = baseList?.Asset as VehicleAmmoListScriptable ?? OwnLoad(baseList);
			if (list?.AmmoClips == null || list.AmmoClips.Length < 3) return null;
			var clips = PortList(list.AmmoClips, DefaultApfsds(warn: false));
			clips.Remove(Heat);
			var sets = new List<AmmoLogisticsScriptable>();
			preferred = new List<int>();
			foreach (var set in new[] { ApfsdsSet }.Concat(vanilla ?? new AmmoLogisticsScriptable[0]))
			{
				var options = set?.AmmoLogistics?.AmmoOptions;
				if (options == null || sets.Contains(set)) continue;
				for (int j = 0; j < options.Length; j++)
					if (options[j] != null && clips.Contains(options[j].AmmoType)) { sets.Add(set); preferred.Add(j); break; }
			}
			return sets;
		}

		// Rack rounds are Instantiates of these. Left live, anything the game did to one went into every round after.
		static Transform _modelHolder;

		static Transform ModelHolder
		{
			get
			{
				if (_modelHolder != null) return _modelHolder;
				var go = new GameObject($"{S.Tag} round models (inactive)");
				go.SetActive(false);
				Object.DontDestroyOnLoad(go);
				_modelHolder = go.transform;
				return _modelHolder;
			}
		}

		static AmmoClipCodexScriptable MakeClip(Round r, AmmoType source)
		{
			var ammo = new AmmoType();
			foreach (var f in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (!f.IsLiteral && !f.IsInitOnly) f.SetValue(ammo, f.GetValue(source));
			// AmmoType.Equals compares names: a vanilla name drags the round into that round's logistics lists.
			ammo.Name = r.Name;
			ammo.ArmorOptimizations = new AmmoType.ArmorOptimization[0];
			r.Set(ammo);

			var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
			codex.name = $"ammo_{S.Prefix}_{r.Id}";
			codex.AmmoType = ammo;

			var clip = new AmmoType.AmmoClip { Capacity = 1, Name = r.Name, MinimalPattern = new[] { codex } };
			var clipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
			clipCodex.name = $"clip_{S.Prefix}_{r.Id}";
			clipCodex.ClipType = clip;

			if (source.VisualModel != null)
			{
				var visual = Object.Instantiate(source.VisualModel, ModelHolder, false);
				visual.name = r.Id + " visual";
				var stored = visual.GetComponent<AmmoStoredVisual>();
				if (stored != null) { stored.AmmoType = ammo; stored.AmmoScriptable = codex; }
				ApplyLook(visual, r);
				ApplyShape(visual, r);
				ammo.VisualModel = visual;
			}
			return clipCodex;
		}

		static readonly Dictionary<string, Texture2D> _looks = new Dictionary<string, Texture2D>();
		static readonly Dictionary<(Material, string), Material> _lookMaterials = new Dictionary<(Material, string), Material>();

		static void ApplyLook(GameObject model, Round r)
		{
			if (string.IsNullOrEmpty(r.Texture) || string.IsNullOrEmpty(r.Atlas)) return;
			foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
			{
				var mats = rend.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < mats.Length; i++)
				{
					var m = mats[i];
					if (m == null || !m.HasProperty("_MainTex") || m.mainTexture == null || m.mainTexture.name != r.Atlas) continue;
					if (!_lookMaterials.TryGetValue((m, r.Texture), out var copy) || copy == null)
					{
						var tex = LookTexture(r.Texture, m.mainTexture);
						if (tex == null) { MelonLogger.Warning($"{S.Tag}: the {r.Id} stencils ({r.Texture}) are missing from the mod; its rounds keep the {r.Atlas} look."); return; }
						copy = new Material(m) { name = $"{m.name} ({S.Prefix} {r.Id})", hideFlags = HideFlags.DontUnloadUnusedAsset };
						copy.mainTexture = tex;
						_lookMaterials[(m, r.Texture)] = copy;
					}
					mats[i] = copy;
					changed = true;
				}
				if (changed) rend.sharedMaterials = mats;
			}
		}

		static readonly Dictionary<string, Mesh> _shapeMeshes = new Dictionary<string, Mesh>();
		static string[] _shapeIndex;

		static void ApplyShape(GameObject model, Round r)
		{
			if (string.IsNullOrEmpty(r.Shape)) return;
			if (_shapeIndex == null)
			{
				using (var st = typeof(PortAmmo).Assembly.GetManifestResourceStream("round_meshes.txt"))
					_shapeIndex = st == null ? new string[0] : new StreamReader(st).ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToArray();
			}
			int meshes = 0, missing = 0;
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			foreach (var line in _shapeIndex)
			{
				var f = line.Split('|');
				if (f.Length < 4 || f[1] != r.Shape) continue;
				try
				{
					if (f[0] == "mesh")
					{
						var mesh = ShapeMesh(f[3], f.Length >= 7 ? f : null);
						if (mesh == null) { missing++; continue; }
						foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true).Where(x => x.name == f[2])) { mf.sharedMesh = mesh; meshes++; }
					}
					else if (f[0] == "capsule")
					{
						var c = f.Length >= 7 ? f[3].Split(',') : null;
						if (c == null || c.Length != 3) { missing++; MelonLogger.Warning($"{S.Tag}: round shape line '{line}' is malformed; that hit zone keeps the {r.Source} shape."); continue; }
						var centre = new Vector3(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv));
						float radius = float.Parse(f[4], inv), height = float.Parse(f[5], inv);
						int axis = int.Parse(f[6], inv);
						foreach (var cc in model.GetComponentsInChildren<CapsuleCollider>(true).Where(x => x.name == f[2]))
						{
							cc.center = centre; cc.radius = radius; cc.height = height; cc.direction = axis;
						}
					}
				}
				catch (Exception e) { missing++; MelonLogger.Warning($"{S.Tag}: round shape line '{line}' can't be used ({e.GetType().Name}: {e.Message}); that piece keeps the {r.Source} shape."); }
			}
			if (missing > 0 || meshes == 0) MelonLogger.Warning($"{S.Tag}: the {r.Id} model's shape ({r.Shape}) is missing from the mod ({missing} pieces); its rounds keep the {r.Source} shape.");
		}

		static readonly HashSet<string> _shapeFailed = new HashSet<string>();
		static Mesh ShapeMesh(string resource, string[] index)
		{
			if (_shapeMeshes.TryGetValue(resource, out var m) && m != null) return m;
			if (_shapeFailed.Contains(resource)) return null;
			Mesh Bad(string why) { _shapeFailed.Add(resource); MelonLogger.Warning($"{S.Tag}: round shape piece {resource} {why}; that part keeps the source round's shape."); return null; }
			byte[] bytes;
			using (var st = typeof(PortAmmo).Assembly.GetManifestResourceStream(resource))
			{
				if (st == null) { _shapeFailed.Add(resource); return null; }
				bytes = new byte[st.Length];
				int got = 0;
				while (got < bytes.Length) { int n = st.Read(bytes, got, bytes.Length - got); if (n <= 0) break; got += n; }
				if (got != bytes.Length) return Bad("is truncated");
			}
			if (bytes.Length < 8) return Bad("is truncated");
			int nv = BitConverter.ToInt32(bytes, 0), nt = BitConverter.ToInt32(bytes, 4);
			if (nv <= 0 || nt <= 0 || (long)nv * 32 + (long)nt * 12 + 8 != bytes.Length) return Bad($"has {bytes.Length} bytes for {nv} vertices / {nt} triangles");
			if (index != null)
			{
				if (int.TryParse(index[4], out int iv) && int.TryParse(index[5], out int it) && (iv != nv || it != nt)) return Bad($"holds {nv} / {nt}, its index says {iv} / {it}");
				string want = index[6].Trim();
				if (want.Length > 0)
				{
					string sha;
					using (var h = System.Security.Cryptography.SHA1.Create()) sha = BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
					if (!sha.StartsWith(want, StringComparison.OrdinalIgnoreCase)) return Bad("doesn't match its index's SHA-1");
				}
			}
			using (var br = new BinaryReader(new MemoryStream(bytes, 8, bytes.Length - 8)))
			{
				Vector3[] V3() { var a = new Vector3[nv]; for (int i = 0; i < nv; i++) a[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); return a; }
				var pos = V3(); var nrm = V3();
				var uv = new Vector2[nv]; for (int i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
				var tri = new int[nt * 3];
				for (int i = 0; i < tri.Length; i++) { tri[i] = br.ReadInt32(); if (tri[i] < 0 || tri[i] >= nv) return Bad($"has a triangle index {tri[i]} out of 0-{nv - 1}"); }
				m = new Mesh { name = Path.GetFileNameWithoutExtension(resource), hideFlags = HideFlags.DontUnloadUnusedAsset };
				if (nv > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				m.vertices = pos; m.normals = nrm; m.uv = uv; m.triangles = tri;
				m.RecalculateTangents();
				m.RecalculateBounds();
				m.UploadMeshData(true);
			}
			_shapeMeshes[resource] = m;
			return m;
		}

		static Texture2D LookTexture(string resource, Texture like)
		{
			if (_looks.TryGetValue(resource, out var t) && (ReferenceEquals(t, null) || t != null)) return t;
			byte[] buf;
			using (var st = typeof(PortAmmo).Assembly.GetManifestResourceStream(resource))
			{
				if (st == null) return null;
				buf = new byte[st.Length];
				int got = 0;
				while (got < buf.Length) { int n = st.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
			}
			t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(resource) };
			if (!ImageConversion.LoadImage(t, buf, false))
			{
				Object.Destroy(t);
				_looks[resource] = null;
				return null;
			}
			t.filterMode = like.filterMode; t.anisoLevel = like.anisoLevel; t.wrapMode = like.wrapMode;
			t.Compress(true);
			t.Apply(false, true);
			t.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_looks[resource] = t;
			return t;
		}

		internal static void ApplyAtSpawn(Vehicle vehicle)
		{
			if (!Build()) return;
			var lm = vehicle.GetComponent<LoadoutManager>();
			if (lm == null) return;
			var weapon = vehicle.GetComponent<WeaponsManager>().Weapons[0].Weapon;
			var list = PortList(lm.LoadedAmmoList.AmmoClips, DefaultApfsds(warn: true));
			lm.LoadedAmmoList.AmmoClips = list.ToArray();
			lm.TotalAmmoCounts = Fit(S.TotalCounts, list.Count);
			int missile = Missile != null ? list.IndexOf(Missile) : -1;
			var kept = new List<LoadoutManager.RackLoadout>();
			for (int i = 0; i < lm.RackLoadouts.Length; i++)
			{
				var rl = lm.RackLoadouts[i];
				var r = S.Racks.FirstOrDefault(x => x.Index == i);
				if (r == null) { if (!S.DropOtherRacks) kept.Add(rl); continue; }
				if (r.Counts != null) rl.AmmoCounts = Fit(r.Counts, list.Count);
				if (missile >= 0 && r.MissileSlots.Length > 0)
					rl.FixedChoices = r.MissileSlots.Select(s => new LoadoutManager.RackLoadoutFixedChoice { AmmoClipIndex = missile, RackSlotIndex = s }).ToArray();
				if (r.NoRestockInto && rl.Rack != null && rl.Rack.GetComponent<NoRestockInto>() == null) rl.Rack.gameObject.AddComponent<NoRestockInto>();
				kept.Add(rl);
			}
			var all = lm.RackLoadouts;
			if (S.DropOtherRacks) kept = kept.OrderBy(rl => Array.FindIndex(S.Racks, x => x.Index == Array.IndexOf(all, rl))).ToList();
			lm.RackLoadouts = kept.ToArray();
			if (missile >= 0) AddGuidanceUnit(weapon);
			RegisterAllBallistics.Invoke(lm, null);
		}

		static int[] Fit(int[] counts, int n)
		{
			var a = new int[n];
			if (counts != null) Array.Copy(counts, a, Math.Min(n, counts.Length));
			return a;
		}

		internal sealed class NoRestockInto : MonoBehaviour { }

		[HarmonyPatch(typeof(LoadoutManager), "RackHasRestockSpace", new[] { typeof(LoadoutManager.RackLoadout) })]
		static class RestockTargetPatch
		{
			static bool Prefix(LoadoutManager.RackLoadout __0, ref bool __result)
			{
				var rackLoadout = __0;
				if (rackLoadout?.Rack == null || rackLoadout.Rack.GetComponent<NoRestockInto>() == null) return true;
				__result = false;
				return false;
			}
		}

		static readonly HashSet<string> reported = new HashSet<string>();

		internal static void SelfCheck(Vehicle vehicle)
		{
			if (S == null || !_built || vehicle == null) return;
			var problems = new List<string>();
			var own = Apfsds.Select(kv => (kv.Key, kv.Value)).ToList();
			if (Missile != null) own.Add((S.Missile.Id, Missile));
			for (int i = 0; i < Extras.Count; i++) own.Add((S.Extra[i].Id, Extras[i]));
			foreach (var (id, clip) in own)
			{
				var model = clip?.ClipType?.MinimalPattern?.FirstOrDefault()?.AmmoType?.VisualModel;
				if (model == null) { problems.Add($"{id} round model missing"); continue; }
				if (model.activeInHierarchy) problems.Add($"{id} round model is active in the scene");
				string damage = ModelDamage(model.transform);
				if (damage != null) problems.Add($"{id} round model {damage}");
			}
			var lm = vehicle.GetComponent<LoadoutManager>();
			var clips = lm?.LoadedAmmoList?.AmmoClips;
			if (lm == null || clips == null) { Report(vehicle, problems.Concat(new[] { "no loadout" })); return; }
			int expected = 3 + (Missile != null ? 1 : 0) + Extras.Count;
			if (clips.Length != expected) problems.Add($"ammo list has {clips.Length} entries, expected {expected}");
			if (clips.Length > 0 && !Apfsds.Values.Any(a => ReferenceEquals(a, clips[0]))) problems.Add($"APFSDS slot holds '{clips[0]?.ClipType?.Name}'");
			if (clips.Length > 1 && !ReferenceEquals(clips[1], Heat)) problems.Add($"HEAT slot holds '{clips[1]?.ClipType?.Name}'");
			bool xray = AarController.ShowXray || AarController.InAar;
			var cm = ConstantsAndInfoManager.Instance;
			int aarLayer = cm != null ? cm.AarLayer : 15, xrayLayer = cm != null ? cm.XrayOnlyLayer : 21;
			var bad = new Dictionary<string, int>();
			void Bad(string what) => bad[what] = bad.TryGetValue(what, out int n) ? n + 1 : 1;
			int stored = 0;
			foreach (var rl in lm.RackLoadouts ?? new LoadoutManager.RackLoadout[0])
			{
				if (rl.Rack == null) continue;
				foreach (var slot in rl.Rack.VisualSlots)
				{
					var v = slot != null ? slot.GetComponentsInChildren<AmmoStoredVisual>(true).FirstOrDefault(x => !x.Hidden) : null;
					if (v == null) continue;
					string name = v.AmmoType?.Name ?? "unknown round";
					var parts = v.GetComponentsInChildren<Renderer>(true).ToList();
					if (v.ChargeVisual != null) parts.AddRange(v.ChargeVisual.GetComponentsInChildren<Renderer>(true));
					if (parts.Count == 0) { Bad($"{name} stored without a model"); continue; }
					string damage = ModelDamage(v.transform);
					if (damage != null) Bad($"{name} stored {damage}");
					if (!xray && parts.Any(r => r.gameObject.layer == aarLayer || r.gameObject.layer == xrayLayer)) Bad($"{name} stored on the x-ray layer");
				}
			}
			foreach (var kv in bad) problems.Add($"{kv.Key} ({kv.Value}x)");
			for (int i = 0; i < clips.Length; i++) stored += lm.GetCurrentAmmoCount(i);
			var weapon = vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon;
			int breech = weapon?.Feed?.AmmoTypeInBreech != null ? 1 : 0;
			int total = (lm.TotalAmmoCounts ?? new int[0]).Sum();
			if (lm.FillAmmoOnStart && Math.Abs(stored + breech - total) > 1) problems.Add($"{stored} rounds stored + {breech} in the breech, expected {total}");
			if (Missile != null && clips.Contains(Missile) && weapon != null)
			{
				if (weapon.GuidanceUnit == null) problems.Add("missile guidance unit missing");
				else if (!ReferenceEquals(FeedMissileGuidance.GetValue(weapon.Feed), weapon.GuidanceUnit)) problems.Add("missile guidance unit not linked to the gun's feed");
			}
			Report(vehicle, problems);
		}

		static string ModelDamage(Transform model)
		{
			var issues = new List<string>();
			var filters = model.GetComponentsInChildren<MeshFilter>(true);
			if (filters.Any(f => f.sharedMesh == null)) issues.Add("lost meshes");
			if (model.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null))) issues.Add("lost materials");
			if (model.GetComponentsInChildren<Collider>(true).Length == 0) issues.Add("has no hit zones");
			return issues.Count > 0 ? string.Join(", ", issues) : null;
		}

		static void Report(Vehicle vehicle, IEnumerable<string> problems)
		{
			var fresh = problems.Where(p => reported.Add(p)).ToList();
			if (fresh.Count == 0) return;
			MelonLogger.Warning($"{S.Tag} ammunition self-check ({vehicle.name}): {string.Join("; ", fresh)}. Please include this log in a bug report.");
		}

		static readonly FieldInfo InitialClipCounts = Reflect.Field(typeof(GHPC.Weapons.AmmoRack), "_initialClipCounts");

		internal static void ApplyCoaxAtSpawn(Vehicle vehicle)
		{
			if (S == null || S.CoaxRounds <= 0) return;
			var feed = vehicle.GetComponentsInChildren<AmmoFeed>(true).FirstOrDefault(f => f.name.Contains(S.CoaxFeed));
			var rack = feed?.ReadyRack;
			if (rack == null || rack.ClipTypes == null || rack.ClipTypes.Length == 0)
			{
				MelonLogger.Warning($"{S.Tag} coax {S.CoaxFeed}: feed or ready rack NOT FOUND; keeping the base load.");
				return;
			}
			var old = rack.ClipTypes[0];
			int belts = Mathf.Max(1, Mathf.RoundToInt(S.CoaxRounds / (float)S.CoaxBelt));
			var belt = new AmmoType.AmmoClip { Name = old.Name, MinimalPattern = old.MinimalPattern, Capacity = S.CoaxBelt };
			rack.ClipTypes = new[] { belt };
			InitialClipCounts.SetValue(rack, new[] { belts });
			rack.StoredClips.Clear();
			for (int i = 0; i < belts; i++) rack.StoredClips.Add(belt);
		}

		static void AddGuidanceUnit(WeaponSystem weapon)
		{
			if (weapon.GuidanceUnit != null) return;
			var parent = weapon.FCS.transform.parent;
			var go = new GameObject(S.GuidanceName);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = S.GuidancePosition;
			go.transform.localEulerAngles = Vector3.zero;
			var mgu = go.AddComponent<MissileGuidanceUnit>();
			// Sight line, not the gun's axis: the autoloader drops the gun to load mid-guidance and the missile followed it.
			var beam = new GameObject(S.BeamName).AddComponent<GuidanceBeam>();
			beam.transform.SetParent(weapon.FCS.GetComponentInParent<Vehicle>()?.transform ?? parent, false);
			beam.Fcs = weapon.FCS;
			beam.Gun = go.transform;
			beam.Sync();
			mgu.AimElement = beam.transform;
			weapon.GuidanceUnit = mgu;
			weapon.Feed.ReloadDuringMissileTracking = false;
			// The feed only subscribes in RegisterWeaponSystem at Awake. Without this WaitingOnMissile sticks after the first
			// missile and the gun stops reloading by itself.
			FeedMissileGuidance.SetValue(weapon.Feed, mgu);
			mgu.GuidanceStopped += (Action)Delegate.CreateDelegate(typeof(Action), weapon.Feed, FeedGuidanceStopped);
			weapon.FireWhileGuidingMissile = false;
		}
	}

	[HarmonyPatch(typeof(MissionConfigMenuPanel), "GetAmmoSetsByUnit")]
	internal static class AmmoMenuSetsPatch
	{
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, AmmoLogisticsSetScriptable> AllSets =
			Reflect.FieldRefAccess<MissionConfigMenuPanel, AmmoLogisticsSetScriptable>("_allAmmoSetsScriptable");
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, UnitPrefabLookupScriptable> AllUnits =
			Reflect.FieldRefAccess<MissionConfigMenuPanel, UnitPrefabLookupScriptable>("_allUnitsScriptable");

		static void Postfix(MissionConfigMenuPanel __instance, string __0, ref List<AmmoLogisticsScriptable> __result, ref List<int> __1)
		{
			string unitKey = __0;
			if (!PortAmmo.IsPort(unitKey)) return;
			try
			{
				var sets = PortAmmo.SetsForMenu(AllUnits(__instance)?.GetBaseAmmoClipLoadout(unitKey), AllSets(__instance)?.AllAmmoReplacementOrders, out var preferred);
				if (sets != null) { __result = sets; __1 = preferred; }
			}
			catch (Exception e) { MelonLogger.Error($"{PortAmmo.S.Tag} ammo choice for the mission customizer failed: " + e); }
		}
	}

	[HarmonyPatch(typeof(MissionConfigMenuPanel), nameof(MissionConfigMenuPanel.AddAmmoEntry))]
	internal static class AmmoMenuLabelPatch
	{
		static readonly AccessTools.FieldRef<MissionConfigMenuPanel, List<GameObject>> Entries =
			Reflect.FieldRefAccess<MissionConfigMenuPanel, List<GameObject>>("_ammoDataObjects");
		static readonly AccessTools.FieldRef<MissionConfigAmmoSelectionEntry, TextMeshProUGUI> Label =
			Reflect.FieldRefAccess<MissionConfigAmmoSelectionEntry, TextMeshProUGUI>("_label");

		static void Postfix(MissionConfigMenuPanel __instance, AmmoLogisticsScriptable __1)
		{
			var ammoLogisticsScriptable = __1;
			if (ammoLogisticsScriptable == null || ammoLogisticsScriptable != PortAmmo.ApfsdsSet || string.IsNullOrEmpty(PortAmmo.S?.MenuSuffix)) return;
			var entry = Entries(__instance)?.LastOrDefault()?.GetComponent<MissionConfigAmmoSelectionEntry>();
			if (entry == null || entry.AmmoSet != ammoLogisticsScriptable) return;
			var label = Label(entry);
			if (label != null && !label.text.EndsWith(")")) label.text += PortAmmo.S.MenuSuffix;
		}
	}

	[HarmonyPatch(typeof(LoadoutManager), "DoFlexMissionLogisticsChanges")]
	internal static class AmmoCampaignPatch
	{
		static void Prefix(LoadoutManager __instance)
		{
			if (PortAmmo.ApfsdsSet == null || !IsPort(__instance)) return;
			PortAmmo.HideFixedHeat(__instance.LoadedAmmoList?.AmmoClips);
			if (!DynamicMissionComposer.CampaignMode) return;
			var set = PortAmmo.ApfsdsSet;
			foreach (var f in new[] { DynamicMissionLauncher.CurrentFactionBlue, DynamicMissionLauncher.CurrentFactionRed })
				if (f != null && (f.AmmoLogisticsData == null || !f.AmmoLogisticsData.Contains(set)))
					f.AmmoLogisticsData = (f.AmmoLogisticsData ?? new AmmoLogisticsScriptable[0]).Concat(new[] { set }).ToArray();
		}

		static Exception Finalizer(LoadoutManager __instance, Exception __exception)
		{
			if (PortAmmo.ApfsdsSet == null || !IsPort(__instance)) return __exception;
			var clips = __instance.LoadedAmmoList?.AmmoClips;
			PortAmmo.RestoreFixedHeat(clips);
			return __exception;
		}

		static bool IsPort(LoadoutManager lm) => PortAmmo.IsPort(lm.GetComponent<Vehicle>()?.UniqueName);
	}

	[HarmonyPatch(typeof(AmmoReplacementOrder), nameof(AmmoReplacementOrder.SelectAmmoType))]
	internal static class AmmoCampaignUnlockPatch
	{
		// GHPC honours BlockUntilMissionsCompleted for units only, SelectAmmoType ignores it.
		static void Prefix(AmmoReplacementOrder __instance, bool __2)
		{
			bool isCampaignMode = __2;
			if (!isCampaignMode || PortAmmo.ApfsdsSet == null || __instance != PortAmmo.ApfsdsSet.AmmoLogistics) return;
			int done = CampaignSaveState.ActiveSaveData?.TotalMissionsCompleted ?? 0;
			foreach (var o in __instance.AmmoOptions) o.AllowInCampaign = done >= o.BlockUntilMissionsCompleted;
		}
	}

	[HarmonyPatch(typeof(LiveRound), "penCheck")]
	internal static class TandemEraPatch
	{
		static readonly AccessTools.FieldRef<LiveRound, bool> Sacrificed = Reflect.FieldRefAccess<LiveRound, bool>("_sacrificedWarhead");

		// Never throw out of a penCheck patch: the round fails every frame after, and in its first frame no new round flies.
		static void Prefix(LiveRound __instance, Collider __0)
		{
			try { Precursor(__instance, __0); }
			catch (Exception e) { PatchGuard.Failed("Tandem ERA charge", e); }
		}

		// GHPC's Tandem only beats slat armour.
		static void Precursor(LiveRound __instance, Collider cObject)
		{
			AmmoType info = __instance.Info;
			if (info == null || !info.Tandem || __instance.IsSpall || cObject == null || PortAmmo.S == null) return;
			if (Array.IndexOf(PortAmmo.S.EraDefeating, info.Name) < 0 || Sacrificed(__instance)) return;
			if (!cObject.TryGetComponent<IArmor>(out var armor) || !armor.IsEra || armor.IsDetonated) return;
			if (armor.Unit != null && __instance.ShotInfo != null && __instance.ShotInfo.Shooter == armor.Unit) return;

			armor.Detonate();
			Sacrificed(__instance) = true;
		}
	}
}
