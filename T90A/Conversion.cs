using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Equipment;
using GHPC.Thermals;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class Conversion
	{
		class Converted : MonoBehaviour { }

		const string PaintedPrefix = "t90a_t_90a_";

		internal static void Convert(Vehicle vehicle, bool atSpawn = false)
		{
			if (vehicle == null || vehicle.GetComponent<Converted>() != null) return;
			vehicle.gameObject.AddComponent<Converted>();
			try { ApplyVisuals(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A visuals failed: " + e); }
			try { ArmorSetup.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A armour failed: " + e); }
			if (atSpawn)
			{
				try { T90AAmmo.ApplyAtSpawn(vehicle, T90AMod.Apfsds.Value, addMissile: true); }
				catch (Exception e) { MelonLogger.Error("T-90A ammo setup failed: " + e); }
				try { T90AAmmo.ApplyCoaxAtSpawn(vehicle, T90AMod.CoaxRounds); }
				catch (Exception e) { MelonLogger.Error("T-90A coax PKT setup failed: " + e); }
			}
			else MelonLogger.Warning("T-90A ammo: " + vehicle.name + " was converted after its racks were filled; keeping base ammo.");
			try { Crew(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A crew stations failed: " + e); }
			try { CommanderSeat.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A commander station failed: " + e); }
			try { RealAxis.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A turret axis failed: " + e); }
			if (atSpawn) try { Kord.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A Kord failed: " + e); }
			else MelonLogger.Warning("T-90A Kord: " + vehicle.name + " was converted after spawn; no remote Kord fitted.");
			MelonCoroutines.Start(LateSetup(vehicle));
		}

		static readonly Vector3[] Tubes =
		{
			new Vector3(1.385f, 0.578f, -0.404f), new Vector3(-1.035f, 0.678f, -0.269f), new Vector3(1.277f, 0.578f, -0.379f),
			new Vector3(-1.143f, 0.680f, -0.278f), new Vector3(1.407f, 0.454f, -0.464f), new Vector3(-1.302f, 0.449f, -0.427f),
			new Vector3(1.369f, 0.338f, -0.484f), new Vector3(-1.283f, 0.333f, -0.427f), new Vector3(1.312f, 0.462f, -0.411f),
			new Vector3(-1.401f, 0.455f, -0.474f), new Vector3(1.283f, 0.348f, -0.417f), new Vector3(-1.367f, 0.330f, -0.501f),
		};

		static WeaponSystemCodexScriptable _gunCodex;

		internal static void RefreshSmokeBrains(VehicleSmokeManager mgr)
		{
			var t = typeof(VehicleSmokeManager);
			var get = AccessTools.Method(t, "GetCrewBrainsArrayFromFlag");
			if (AccessTools.Field(t, "_unitInfoBroker").GetValue(mgr) == null) return;
			foreach (var (cache, flag) in new[] { ("_smokeGrenadeCrewBrains", "_smokeGrenadeRequiredCrewPositions"), ("_smokeScreenCrewBrains", "_smokeScreenRequiredCrewPositions") })
			{
				var arr = (Array)AccessTools.Field(t, cache).GetValue(mgr);
				if (arr != null && arr.Cast<object>().All(b => b != null)) continue;
				var fresh = get.Invoke(mgr, new[] { AccessTools.Field(t, flag).GetValue(mgr) });
				AccessTools.Field(t, cache).SetValue(mgr, fresh);
			}
		}

		static void SmokeTubes(Vehicle vehicle)
		{
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var turret = vehicle.transform.Find("---MESH---/HULL/TURRET");
			if (mgr == null || turret == null) return;
			var slots = (VehicleSmokeManager.SmokeSlot[])AccessTools.Field(typeof(VehicleSmokeManager), "_smokeSlots").GetValue(mgr);
			if (slots == null) return;
			var bySide = slots.Where(s => s.SpawnLocation != null).OrderBy(s => turret.InverseTransformPoint(s.SpawnLocation.position).x).ToList();
			int half = bySide.Count / 2;
			var slotsL = bySide.Take(half).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			var slotsR = bySide.Skip(half).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			var left = Tubes.Where(t => t.x < 0).OrderByDescending(t => t.y).ToList();
			var right = Tubes.Where(t => t.x >= 0).OrderByDescending(t => t.y).ToList();
			for (int i = 0; i < slotsL.Count && i < left.Count; i++) Seat(slotsL[i], turret, left[i]);
			for (int i = 0; i < slotsR.Count && i < right.Count; i++) Seat(slotsR[i], turret, right[i]);
		}

		static void Seat(VehicleSmokeManager.SmokeSlot slot, Transform turret, Vector3 local)
		{
			slot.SpawnLocation.position = turret.TransformPoint(local);
		}

		[HarmonyPatch(typeof(VehicleSmokeManager), nameof(VehicleSmokeManager.FireSmokes))]
		static class SmokeCrewRefresh
		{
			static void Prefix(VehicleSmokeManager __instance)
			{
				var unit = __instance.GetComponentInParent<Vehicle>();
				if (unit == null || unit.GetComponent<Converted>() == null) return;
				RefreshSmokeBrains(__instance);
			}
		}

		static void Crew(Vehicle vehicle)
		{
			var traverse = FindDeep(vehicle.transform, "---TURRET SCRIPTS---")?.GetComponent<AimablePlatform>();
			if (traverse != null) traverse.SpeedPowered = 24f;
			else MelonLogger.Warning("T-90A: turret traverse platform not found; T-72M1 traverse speed kept.");
			var elevation = vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p != traverse && p.transform.parent != null && p.transform.parent.name.Contains("MAIN GUN"))
				?? vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p != traverse);
			if (elevation == null) MelonLogger.Warning("T-90A: gun elevation platform not found; vanilla gun limits kept.");
			else
			{
				elevation.SpeedPowered = 25f;
				elevation.LocalEulerLimits = new Vector2(-7f, 20f);
			}
			SmokeTubes(vehicle);

			var vc = vehicle.GetComponent<NWH.VehiclePhysics.VehicleController>();
			if (vc != null) vc.engine.maxPower = 1000f;
			var chassis = vehicle.GetComponent<NwhChassis>();
			if (chassis != null)
			{
				AccessTools.Field(typeof(NwhChassis), "_maxForwardSpeed").SetValue(chassis, 60f / 3.6f);
				AccessTools.Field(typeof(NwhChassis), "_maxReverseSpeed").SetValue(chassis, 16.1f / 3.6f);
			}

			var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0].Weapon?.Feed;
			if (feed != null && feed.ClipReloadStages != null)
			{
				var dur = AccessTools.Field(typeof(AmmoFeed.ReloadStage), "Duration");
				float total = feed.ClipReloadStages.Sum(s => (float)dur.GetValue(s));
				if (total > 0f)
				{
					float k = 6.5f / total;
					foreach (var s in feed.ClipReloadStages) dur.SetValue(s, (float)dur.GetValue(s) * k);
					AccessTools.Method(typeof(AmmoFeed), "ResetLiveDurations")?.Invoke(feed, null);
					AccessTools.Method(typeof(AmmoFeed), "CalculateTotalTime")?.Invoke(feed, new object[] { feed.ClipReloadStages, false });
				}
			}

			var wm = vehicle.GetComponent<WeaponsManager>();
			if (wm != null && wm.Weapons.Length > 0)
			{
				wm.Weapons[0].Name = "125mm Gun 2A46M-5";
				var gun = wm.Weapons[0].Weapon;
				var codexField = AccessTools.Field(typeof(WeaponSystem), "CodexEntry");
				var codex = gun != null ? (WeaponSystemCodexScriptable)codexField.GetValue(gun) : null;
				if (codex != null && codex != _gunCodex)
				{
					if (_gunCodex == null)
					{
						_gunCodex = UnityEngine.Object.Instantiate(codex);
						_gunCodex.name = "T90A 2A46M-5";
						_gunCodex.FriendlyName = "125mm Gun 2A46M-5";
					}
					codexField.SetValue(gun, _gunCodex);
				}
			}
			var peri = FindDeep(vehicle.transform, "Gunner's Periscope Camera")?.GetComponent<GHPC.Camera.CameraSlot>();
			if (peri != null) peri.DirectAttachOnly = true;
			else MelonLogger.Warning("T-90A: gunner's periscope slot not found.");
		}

		static IEnumerator LateSetup(Vehicle vehicle)
		{
			yield return null;
			yield return null;
			if (vehicle == null) yield break;
			try
			{
				var smoke = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
				if (smoke != null) RefreshSmokeBrains(smoke);
			}
			catch (Exception e) { MelonLogger.Error("T-90A smoke crew refresh failed: " + e); }
			try { Voices.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A voices failed: " + e); }
			try { Kord.LateSetup(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A Kord late setup failed: " + e); }
			try { SightSetup.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A sight setup failed: " + e); }
		}

		static byte[] _trimmedSkin;
		static bool _trimmedSkinRead, _skinWarned;

		static byte[] TrimmedSkin()
		{
			if (_trimmedSkinRead) return _trimmedSkin;
			_trimmedSkinRead = true;
			using (var s = typeof(Conversion).Assembly.GetManifestResourceStream("T72M1_skin_hull_trimmed.bin"))
			{
				if (s == null) { MelonLogger.Warning("T-90A: running-gear mesh missing from the DLL; T-72 skirts hidden by collapsed bones only."); return null; }
				var ms = new System.IO.MemoryStream(); s.CopyTo(ms); _trimmedSkin = ms.ToArray();
			}
			return _trimmedSkin;
		}

		static void ApplyVisuals(Vehicle vehicle)
		{
			if (T90AMod.Visual == null) return;
			var root = vehicle.transform;
			var hull = root.Find("---MESH---/HULL");
			var turret = root.Find("---MESH---/HULL/TURRET");
			var gun = root.Find("---MESH---/HULL/TURRET/GUN");
			if (hull == null || turret == null || gun == null)
			{
				MelonLogger.Error("T-90A: base vehicle has no ---MESH---/HULL/TURRET/GUN rig; visuals not applied.");
				return;
			}

			BaseRigT72M1.HideBaseVisuals(root, TrimmedSkin());
			if (BaseRigT72M1.TrimFailure != null && !_skinWarned)
			{
				_skinWarned = true;
				MelonLogger.Warning("T-90A: " + BaseRigT72M1.TrimFailure + "; the T-72 side skirts may show through the T-90A hull.");
			}
			var template = BaseRigT72M1.FindTemplate(root);
			var flir = Shader.Find("Standard (FLIR)");

			var inst = UnityEngine.Object.Instantiate(T90AMod.Visual);
			Attach(inst.transform.Find("HULL"), hull);
			Attach(inst.transform.Find("TURRET"), turret);
			Attach(inst.transform.Find("GUN"), gun);
			var hatch = inst.transform.Find("STUB_HATCH");
			if (hatch != null) StubHatch(root, turret, hatch);
			var cover = inst.transform.Find("MANTLET_COVER");
			if (cover != null) MantletCover(root, turret, gun, cover);
			else MelonLogger.Warning("T-90A: bundle has no MANTLET_COVER; the cover stays part of the turret.");
			AttachAt(inst.transform.Find("CUPOLA"), turret, Kord.CupolaAxis - RealAxis.T72Turret);
			AttachAt(inst.transform.Find("KORD"), turret, Kord.Trunnion - RealAxis.T72Turret);
			UnityEngine.Object.Destroy(inst);

			foreach (var name in new[] { "HULL", "TURRET", "GUN", "STUB_HATCH", "MANTLET_COVER", "CUPOLA", "KORD" })
			{
				var part = name != "HULL" && name != "TURRET" && name != "GUN" ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "T90A_" + name)
					: (name == "HULL" ? hull : name == "TURRET" ? turret : gun).Find("T90A_" + name);
				if (part == null) continue;
				VehiclePaint.ApplyPaint(part.gameObject, template, flir, PaintedPrefix);
				var heat = part.gameObject.AddComponent<HeatSource>();
				AccessTools.Field(typeof(HeatSource), "heat").SetValue(heat, 0.4f);
			}
			CoatGlass(root);
			HatchInner(root);
			try { HatchUndersides(root); }
			catch (Exception e) { MelonLogger.Error("T-90A hatch undersides failed (left plain): " + e); }
			try { Numbers.Apply(root, turret, T90AMod.NumberPosition); }
			catch (Exception e) { MelonLogger.Error("T-90A tactical numbers failed: " + e); }
			try { HideLeftovers(root); }
			catch (Exception e) { MelonLogger.Error("T-90A leftover hiding failed: " + e); }
		}

		static readonly Vector3 HatchHinge = new Vector3(-0.224f, 2.100f - 1.369f, -1.494f + 0.527f);
		static readonly Vector3 HatchPortShift = new Vector3(-0.03f, 0.09f, -0.28f);
		const float HatchRoofSlope = 8.5f;

		static void StubHatch(Transform root, Transform turret, Transform hatch)
		{
			var door = root.GetComponentsInChildren<AnimatedPart>(true).FirstOrDefault(a => a.Transform != null && a.Transform.name == "EJECTOR");
			var bone = door?.Transform; var start = door?.StartTransform; var end = door?.EndTransform;
			if (bone == null || start == null || end == null)
			{
				MelonLogger.Warning("T-90A: T-72M1 ejector door not found; stub hatch left closed and ejection unchanged.");
				Attach(hatch, turret);
				hatch.localPosition = HatchHinge;
				return;
			}
			float s = Mathf.Sin(HatchRoofSlope * Mathf.Deg2Rad), c = Mathf.Cos(HatchRoofSlope * Mathf.Deg2Rad);
			var closed = turret.rotation * Quaternion.LookRotation(new Vector3(0f, -s, -c), new Vector3(0f, c, -s));
			var hinge = turret.TransformPoint(HatchHinge);
			start.SetPositionAndRotation(hinge, closed);
			end.SetPositionAndRotation(hinge, closed * Quaternion.Euler(0f, 0f, 270f));
			bone.SetPositionAndRotation(hinge, closed);
			Attach(hatch, turret);
			hatch.localPosition = HatchHinge;
			hatch.SetParent(bone, true);
			var shift = turret.TransformVector(HatchPortShift);
			foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "ejection path" || x.name == "ejection port fire marker").ToList())
			{
				t.position += shift;
			}
		}

		static readonly Color HatchInnerColor = new Color(0.192f, 0.208f, 0.161f, 1f);

		static void HatchInner(Transform root)
		{
			int n = 0;
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
				foreach (var m in r.sharedMaterials)
					if (m != null && m.name.StartsWith("t90a_hatch_inner"))
					{
						if (m.HasProperty("_Color")) m.SetColor("_Color", HatchInnerColor);
						if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
						n++;
					}
			if (n == 0) MelonLogger.Warning("T-90A: no hatch underside material found (old bundle?).");
		}

		static readonly Rect UndersidePatch = new Rect(320f / 4096f, 1f - 1460f / 2048f, 230f / 4096f, 110f / 2048f);
		const string TurretPaint = "t90a_t_90a_turret";

		static void HatchUndersides(Transform root)
		{
			var anyPaint = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
				.FirstOrDefault(m => m != null && m.name.StartsWith(TurretPaint) && m.shader.name == "GHPC/VehicleShader");
			foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				var mats = mr.sharedMaterials;
				int k = Array.FindIndex(mats, m => m != null && m.name.StartsWith("t90a_hatch_inner"));
				var mf = mr.GetComponent<MeshFilter>();
				if (k < 0 || mf == null || mf.sharedMesh == null) continue;
				var paint = mats.FirstOrDefault(m => m != null && m.name.StartsWith(TurretPaint) && m.shader.name == "GHPC/VehicleShader") ?? anyPaint;
				if (paint == null || !mf.sharedMesh.isReadable) continue;
				var mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
				mesh.name = mf.sharedMesh.name + " (painted undersides)";
				var v = new List<Vector3>(); mesh.GetVertices(v);
				var n = new List<Vector3>(); mesh.GetNormals(n);
				var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
				var tris = mesh.GetTriangles(k);
				var wid = tris.Select(i => Vector3Int.RoundToInt(v[i] * 1000f)).ToArray();
				var ids = wid.Distinct().Select((key, i) => (key, i)).ToDictionary(p => p.key, p => p.i);
				var par = Enumerable.Range(0, ids.Count).ToArray();
				int Find(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
				for (int t = 0; t < tris.Length; t += 3) { par[Find(ids[wid[t + 1]])] = Find(ids[wid[t]]); par[Find(ids[wid[t + 2]])] = Find(ids[wid[t]]); }
				var pieces = Enumerable.Range(0, tris.Length / 3).GroupBy(t => Find(ids[wid[t * 3]])).Select(g => g.ToList()).ToList();
				var outTris = new int[tris.Length];
				foreach (var piece in pieces)
				{
					var nrm = Vector3.zero;
					foreach (int t in piece) nrm += Vector3.Cross(v[tris[t * 3 + 1]] - v[tris[t * 3]], v[tris[t * 3 + 2]] - v[tris[t * 3]]);
					nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.down;
					var ax = Vector3.ProjectOnPlane(Mathf.Abs(nrm.x) < 0.9f ? Vector3.right : Vector3.forward, nrm).normalized;
					var ay = Vector3.Cross(nrm, ax);
					var used = piece.SelectMany(t => new[] { tris[t * 3], tris[t * 3 + 1], tris[t * 3 + 2] }).Distinct().ToList();
					float x0 = used.Min(i => Vector3.Dot(v[i], ax)), x1 = used.Max(i => Vector3.Dot(v[i], ax));
					float y0 = used.Min(i => Vector3.Dot(v[i], ay)), y1 = used.Max(i => Vector3.Dot(v[i], ay));
					bool swap = (y1 - y0) > (x1 - x0);
					float ex = swap ? y1 - y0 : x1 - x0, ey = swap ? x1 - x0 : y1 - y0;
					float pxPerM = Mathf.Min(300f, Mathf.Min(230f / Mathf.Max(ex, 1e-3f), 110f / Mathf.Max(ey, 1e-3f)));
					float mx = (230f - ex * pxPerM) / 2f, my = (110f - ey * pxPerM) / 2f;
					var map = new Dictionary<int, int>();
					foreach (int i in used)
					{
						map[i] = v.Count;
						v.Add(v[i]); n.Add(n.Count > i ? n[i] : -nrm);
						float px = Vector3.Dot(v[i], ax) - x0, py = Vector3.Dot(v[i], ay) - y0;
						if (swap) { var tmp = px; px = py; py = tmp; }
						uv.Add(new Vector2(UndersidePatch.xMin + (mx + px * pxPerM) / 4096f, UndersidePatch.yMin + (my + py * pxPerM) / 2048f));
					}
					foreach (int t in piece) for (int j = 0; j < 3; j++) outTris[t * 3 + j] = map[tris[t * 3 + j]];
				}
				mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
				mesh.SetTriangles(outTris, k);
				mesh.RecalculateTangents();
				mesh.RecalculateBounds();
				mf.sharedMesh = mesh;
				mats[k] = paint;
				mr.sharedMaterials = mats;
			}
		}

		static void MantletCover(Transform root, Transform turret, Transform gun, Transform cover)
		{
			Attach(cover, gun);
			var mf = cover.GetComponent<MeshFilter>();
			var mr = cover.GetComponent<MeshRenderer>();
			if (mf == null || mr == null || mf.sharedMesh == null) { MelonLogger.Warning("T-90A: mantlet cover has no mesh; left rigid on the gun."); return; }
			var mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
			mesh.name = mf.sharedMesh.name + " (skinned)";
			var v = mesh.vertices;
			float zmin = v.Min(p => p.z), zmax = v.Max(p => p.z), len = zmax - zmin;
			float zr = zmin + 0.3f * len, zf = zmax - 0.3f * len;
			var w = new BoneWeight[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				float g = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zr, zf, v[i].z));
				w[i] = g >= 0.5f ? new BoneWeight { boneIndex0 = 1, weight0 = g, boneIndex1 = 0, weight1 = 1f - g }
					: new BoneWeight { boneIndex0 = 0, weight0 = 1f - g, boneIndex1 = 1, weight1 = g };
			}
			mesh.boneWeights = w;
			var plat = root.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p.Transform == gun);
			var restRot = plat != null ? (Quaternion)AccessTools.Field(typeof(AimablePlatform), "_originalLocalRotation").GetValue(plat) : gun.localRotation;
			var gunRestInTurret = gun.parent == turret ? Matrix4x4.TRS(gun.localPosition, restRot, gun.localScale) : turret.worldToLocalMatrix * gun.localToWorldMatrix;
			mesh.bindposes = new[] { gunRestInTurret, Matrix4x4.identity };
			var mats = mr.sharedMaterials;
			UnityEngine.Object.DestroyImmediate(mr);
			UnityEngine.Object.DestroyImmediate(mf);
			var smr = cover.gameObject.AddComponent<SkinnedMeshRenderer>();
			smr.sharedMesh = mesh;
			smr.bones = new[] { turret, gun };
			smr.rootBone = gun;
			smr.sharedMaterials = mats;
			var b = mesh.bounds; b.Expand(0.3f);
			smr.localBounds = b;
		}

		static readonly string[] LeftoverMaterials = { "T72_interior", "GST emission", "IR searchlight glow" };
		static readonly string[] LeftoverNames = { "Luna lens glow", "Luna visible glow" };

		class KeepHidden : MonoBehaviour
		{
			internal Renderer[] Renderers;
			internal Light[] Lights;

			void LateUpdate()
			{
				foreach (var r in Renderers) if (r != null && r.enabled) r.enabled = false;
				foreach (var l in Lights) if (l != null && l.enabled) l.enabled = false;
			}
		}

		static void HideLeftovers(Transform root)
		{
			bool UnderOurs(Transform t) { for (var x = t; x != null && x != root; x = x.parent) if (x.name.StartsWith("T90A_")) return true; return false; }
			string Clean(Material m) => m == null ? "" : m.name.Replace(" (Instance)", "");
			var rs = root.GetComponentsInChildren<Renderer>(true)
				.Where(r => !UnderOurs(r.transform) && !(r is ParticleSystemRenderer)
					&& (LeftoverNames.Contains(r.name) || r.sharedMaterials.Any(m => LeftoverMaterials.Contains(Clean(m)))))
				.ToArray();
			var ls = root.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("GST light")).ToArray();
			foreach (var r in rs) r.enabled = false;
			foreach (var l in ls) l.enabled = false;
			var keep = root.gameObject.AddComponent<KeepHidden>();
			keep.Renderers = rs; keep.Lights = ls;
		}

		static readonly string[] GlassWords = { "glass", "lens", "prism", "periscope", "optic" };

		static void CoatGlass(Transform root)
		{
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
			{
				if (!r.name.StartsWith("T90A_")) continue;
				foreach (var m in r.materials)
				{
					if (m == null || m.shader.name == "GHPC/VehicleShader") continue;
					string n = m.name.ToLowerInvariant();
					bool smooth = m.HasProperty("_Glossiness") && m.GetFloat("_Glossiness") > 0.7f;
					bool transparent = m.renderQueue >= 3000;
					if (!GlassWords.Any(n.Contains) && !smooth && !transparent) continue;
					bool lamp = n.Contains("headlight");
					if (m.HasProperty("_Color")) m.SetColor("_Color", lamp ? new Color(0.45f, 0.45f, 0.42f, 1f) : new Color(0.035f, 0.045f, 0.05f, 1f));
					if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.55f);
					if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0.55f);
					if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
					m.DisableKeyword("_METALLICGLOSSMAP");
					m.DisableKeyword("_EMISSION");
					m.renderQueue = -1;
				}
			}
		}

		internal static Transform FindDeep(Transform root, string name) =>
			root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

		static void Attach(Transform part, Transform bone) => AttachAt(part, bone, Vector3.zero);

		static void AttachAt(Transform part, Transform bone, Vector3 offset)
		{
			if (part == null) return;
			part.name = "T90A_" + part.name;
			part.SetParent(bone, false);
			part.localPosition = offset;
			part.localRotation = Quaternion.identity;
			part.localScale = Vector3.one;
		}
	}
}
