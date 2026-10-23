using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Vehicle;
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

		const float ReverseTrackFactor = 4.52f / 4.17f;
		const float ReverseCapKmh = 12.5f;

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
				try { PortAmmo.ApplyAtSpawn(vehicle); }
				catch (Exception e) { MelonLogger.Error("T-90A ammo setup failed: " + e); }
				try { PortAmmo.ApplyCoaxAtSpawn(vehicle); }
				catch (Exception e) { MelonLogger.Error("T-90A coax PKT setup failed: " + e); }
			}
			else MelonLogger.Warning("T-90A ammo: " + vehicle.name + " was converted after its racks were filled; keeping base ammo.");
			try { Crew(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A crew stations failed: " + e); }
			try { CommanderSeat.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A commander station failed: " + e); }
			try { Interior.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A interior failed (left as the T-72M1's): " + e); }
			try { Hatches(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A hatches failed (left closed): " + e); }
			try { RealAxis.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A turret axis failed: " + e); }
			if (atSpawn) try { Kord.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A Kord failed: " + e); }
			else MelonLogger.Warning("T-90A Kord: " + vehicle.name + " was converted after spawn; no remote Kord fitted.");
			if (atSpawn) try { Modules.Build(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A modules failed: " + e); }
			MelonCoroutines.Start(LateSetup(vehicle));
		}

		static readonly GunSetup.Settings Gun = new GunSetup.Settings
		{
			Tag = "T-90A",
			TurretSpeed = 24f,
			GunSpeed = 28f,
			GunLimits = new Vector2(-(6f + 13f / 60f), 13f + 47f / 60f),
			DepressionProfile = "gun_depression.txt",
			AutoloaderCycle = 6.5f,
			GunName = "125mm Gun 2A46M-5",
			CodexName = "T90A 2A46M-5",
		};

		static void Crew(Vehicle vehicle)
		{
			GunSetup.Apply(vehicle, Gun);
			SmokeLaunchers.Seat(vehicle, T90AMod.SmokePoints);
			SmokeCaps.Apply(vehicle.transform);

			var vc = vehicle.GetComponent<NWH.VehiclePhysics.VehicleController>();
			if (vc != null)
			{
				vc.engine.maxPower = 1000f;
				vc.engine.maxRPM = 2000f;
				bool initialised = vc.IsInitialized;
				if (!initialised && vc.tracks.wheelEnlargementCoefficient > 1f) vc.brakes.maxTorque *= vc.transmission.gearMultiplier / 5.297f;
				vc.transmission.gearMultiplier = 5.297f;
				if (initialised && vc.tracks.wheelEnlargementCoefficient > 1f) vc.transmission.gearMultiplier *= vc.tracks.wheelEnlargementCoefficient;
				vc.transmission.targetShiftUpRPM = 1900f;
				vc.transmission.ReverseGears = new List<float> { -14.35f * ReverseTrackFactor };
			}
			var chassis = vehicle.GetComponent<NwhChassis>();
			if (chassis != null)
			{
				AccessTools.Field(typeof(NwhChassis), "_maxForwardSpeed").SetValue(chassis, 60f / 3.6f);
				AccessTools.Field(typeof(NwhChassis), "_maxReverseSpeed").SetValue(chassis, ReverseCapKmh / 3.6f);
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
				var smoke = vehicle.GetComponentInChildren<GHPC.Equipment.VehicleSmokeManager>(true);
				if (smoke != null) SmokeLaunchers.RefreshCrew(smoke);
			}
			catch (Exception e) { MelonLogger.Error("T-90A smoke crew refresh failed: " + e); }
			try { Voices.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A voices failed: " + e); }
			try { Kord.LateSetup(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A Kord late setup failed: " + e); }
			try { SightSetup.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A sight setup failed: " + e); }
			try { Modules.Wire(vehicle); }
			catch (Exception e) { MelonLogger.Error("T-90A module wiring failed: " + e); }
			try
			{
				ThermalHeat.Apply(vehicle.transform, ThermalHeat.PortParts(vehicle.transform, "T90A_"));
			}
			catch (Exception e) { MelonLogger.Error("T-90A thermal heat failed: " + e); }
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
			if (cover != null)
			{
				Attach(cover, gun);
				string skinned = MantletCover.Skin(root, turret, gun, cover);
				if (skinned == null) MelonLogger.Warning("T-90A: mantlet cover has no mesh; left rigid on the gun.");
			}
			else MelonLogger.Warning("T-90A: bundle has no MANTLET_COVER; the cover stays part of the turret.");
			AttachAt(inst.transform.Find("CUPOLA"), turret, Kord.CupolaAxis - RealAxis.T72Turret);
			AttachAt(inst.transform.Find("KORD"), turret, Kord.Trunnion - RealAxis.T72Turret);
			UnityEngine.Object.Destroy(inst);

			VehiclePaint.Clean = !T90AMod.WeatheredPaint.Value;
			foreach (var name in new[] { "HULL", "TURRET", "GUN", "STUB_HATCH", "MANTLET_COVER", "CUPOLA", "KORD" })
			{
				var part = name != "HULL" && name != "TURRET" && name != "GUN" ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "T90A_" + name)
					: (name == "HULL" ? hull : name == "TURRET" ? turret : gun).Find("T90A_" + name);
				if (part == null) continue;
				VehiclePaint.ApplyPaint(part.gameObject, template, flir, PaintedPrefix);
			}
			CoatGlass(root);
			if (HatchUndersides.Plain(root, "t90a_hatch_inner") == 0) MelonLogger.Warning("T-90A: no hatch underside material found (old bundle?).");
			try
			{
				HatchUndersides.Paint(root, "t90a_hatch_inner", TurretPaint, new RectInt(320, 1350, 230, 110), new Vector2Int(4096, 2048));
			}
			catch (Exception e) { MelonLogger.Error("T-90A hatch undersides failed (left plain): " + e); }
			try { Numbers.Apply(root, turret, T90AMod.NumberPosition); }
			catch (Exception e) { MelonLogger.Error("T-90A tactical numbers failed: " + e); }
			try
			{
				BaseRigT72M1.HideLeftovers(root, "T90A_");
			}
			catch (Exception e) { MelonLogger.Error("T-90A leftover hiding failed: " + e); }
			try { VisualLods.Sync(root, new[] { KordBelt.BeltMaterial }); }
			catch (Exception e) { MelonLogger.Error("T-90A LOD materials failed: " + e); }
		}

		static readonly Vector3 HatchHinge = new Vector3(-0.224f, 2.100f - 1.369f, -1.494f + 0.527f);
		static readonly Vector3 HatchPortShift = new Vector3(-0.03f, 0.09f, -0.28f);

		static void StubHatch(Transform root, Transform turret, Transform hatch)
		{
			Attach(hatch, turret);
			hatch.localPosition = HatchHinge;
			BaseRigT72M1.MoveEjectionMarkers(root, turret.TransformVector(HatchPortShift));
		}

		static void Hatches(Vehicle vehicle)
		{
			var root = vehicle.transform;
			HatchRig.Apply(root, T90AMod.Rig, "T90A_", "T-90A");
		}

		const string TurretPaint = "t90a_t_90a_turret";

		static void CoatGlass(Transform root)
		{
			VanillaMaterials.Apply(root, r => r.name.StartsWith("T90A_"), T90AMod.Roles);
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
