using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC.Vehicle;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// Turns a spawned T-72M1 into a T-90A. Most of it happens at spawn, sights and voices two frames later.
	internal static partial class Conversion
	{
		const string FriendlyName = T90AMod.FriendlyName;
		const string PaintedPrefix = "t90a_t_90a_";
		const string HullPath = "---MESH---/HULL";
		const string TurretPath = HullPath + "/TURRET";
		const string GunPath = TurretPath + "/GUN";

		// What the steps share: the vehicle and the three bones the visuals hang on.
		sealed class Parts
		{
			internal Vehicle Vehicle;
			internal Transform Root;
			internal Transform Hull;
			internal Transform Turret;
			internal Transform Gun;
			internal bool HasRig => Hull != null && Turret != null && Gun != null;
		}

		// The order matters: visuals and armour go on the T-72's bones before the turret axis moves, and the Kord and the
		// modules are built on the final rig. The commander's seat comes before the interior: both move the commander figure,
		// the seat by an offset and the interior to a fixed place, which has to be the one that stands.
		static readonly ConversionStep<Parts>[] Steps =
		{
			new ConversionStep<Parts> { What = "base visuals", Run = HideBase },
			new ConversionStep<Parts> { What = "visuals", Run = AttachVisuals },
			new ConversionStep<Parts> { What = "paint", Run = Paint },
			new ConversionStep<Parts> { What = "hatch undersides", Otherwise = "left plain", Run = PaintHatchUndersides },
			new ConversionStep<Parts> { What = "tactical numbers", Run = PlaceNumbers },
			new ConversionStep<Parts> { What = "leftovers", Run = HideLeftovers },
			new ConversionStep<Parts> { What = "armour", Run = p => Armour.Apply(p.Vehicle) },
			new ConversionStep<Parts> { What = "commander's seat", Run = p => CommanderSeat.Apply(p.Vehicle) },
			new ConversionStep<Parts>
			{
				What = "interior",
				Otherwise = "left as the T-72M1's",
				Run = p => Interior.Apply(p.Vehicle),
			},
			new ConversionStep<Parts>
			{
				What = "ammo setup",
				Run = p => PortAmmo.ApplyAtSpawn(p.Vehicle),
				SpawnOnly = true,
				Late = "T-90A ammo: {0} was converted after its racks were filled; keeping base ammo.",
			},
			new ConversionStep<Parts>
			{
				What = "coax setup",
				Run = p => PortAmmo.ApplyCoaxAtSpawn(p.Vehicle),
				SpawnOnly = true
			},
			new ConversionStep<Parts> { What = "gun setup", Otherwise = "left as the T-72M1's", Run = SetGun },
			new ConversionStep<Parts> { What = "mobility setup", Otherwise = "left as the T-72M1's", Run = SetMobility },
			new ConversionStep<Parts> { What = "smoke launchers", Run = SeatSmoke },
			new ConversionStep<Parts> { What = "hatches", Otherwise = "left closed", Run = MoveHatches },
			new ConversionStep<Parts> { What = "turret axis", Run = MoveAxes },
			new ConversionStep<Parts>
			{
				What = "commander's station",
				Run = p => Commander.Apply(p.Vehicle),
				SpawnOnly = true,
				Late = "T-90A Kord: {0} was converted after spawn; no remote Kord fitted.",
			},
			new ConversionStep<Parts> { What = "modules", Run = p => Modules.Build(p.Vehicle), SpawnOnly = true },
			new ConversionStep<Parts> { What = "periscope placement", Run = PlacePeriscope, SpawnOnly = true },
			new ConversionStep<Parts> { What = "Shtora-1", Run = p => Countermeasures.Apply(p.Vehicle), SpawnOnly = true },
			new ConversionStep<Parts> { What = "LOD materials", Run = SyncLods },
		};

		internal static void Convert(Vehicle vehicle, bool atSpawn = false)
		{
			if (T90AMod.Visual == null || !ConvertSweep.Mark(vehicle)) return;
			var root = vehicle.transform;
			var parts = new Parts
			{
				Vehicle = vehicle,
				Root = root,
				Hull = root.Find(HullPath),
				Turret = root.Find(TurretPath),
				Gun = root.Find(GunPath),
			};
			if (!parts.HasRig)
				MelonLogger.Error($"{FriendlyName}: base vehicle has no ---MESH---/HULL/TURRET/GUN rig; visuals not applied.");
			Func<ConversionStep<Parts>, bool> keep = null;
			DevKeeps(ref keep);
			ConversionSteps.Run(FriendlyName, Steps, parts, vehicle.name, atSpawn, keep);
			MelonCoroutines.Start(LateSetup(vehicle, atSpawn));
		}

		static void HideBase(Parts parts)
		{
			if (!parts.HasRig) return;
			BaseRigT72M1.HideBaseVisuals(parts.Root, EmbeddedFile.T72M1Skin(FriendlyName));
			if (BaseRigT72M1.TrimFailure != null && !_skinWarned)
			{
				_skinWarned = true;
				MelonLogger.Warning(
					$"{FriendlyName}: {BaseRigT72M1.TrimFailure}; the T-72 side skirts may show through the T-90A hull.");
			}
		}

		static bool _skinWarned;

		static void AttachVisuals(Parts parts)
		{
			if (!parts.HasRig) return;
			var attached = VisualParts.Attach(parts.Root, T90AMod.Visual, "T90A_", new[]
			{
				("HULL", HullPath, Vector3.zero),
				("TURRET", TurretPath, Vector3.zero),
				("GUN", GunPath, Vector3.zero),
				("STUB_HATCH", TurretPath, HatchHinge),
				("MANTLET_COVER", GunPath, Vector3.zero),
				("CUPOLA", TurretPath, Commander.CupolaAxis - BaseRigT72M1.TurretPivot),
				("KORD", TurretPath, Commander.Trunnion - BaseRigT72M1.TurretPivot),
			});
			if (attached.ContainsKey("STUB_HATCH"))
				BaseRigT72M1.MoveEjectionMarkers(parts.Root, parts.Turret.TransformVector(HatchPortShift));
			if (!attached.TryGetValue("MANTLET_COVER", out var cover))
			{
				MelonLogger.Warning($"{FriendlyName}: bundle has no MANTLET_COVER; the cover stays part of the turret.");
				return;
			}
			if (MantletCover.Skin(parts.Root, parts.Turret, parts.Gun, cover) == null)
				MelonLogger.Warning($"{FriendlyName}: mantlet cover has no mesh; left rigid on the gun.");
		}

		static void Paint(Parts parts)
		{
			if (!parts.HasRig) return;
			var root = parts.Root;
			var template = BaseRigT72M1.FindTemplate(root);
			var flir = Shader.Find("Standard (FLIR)");
			PaintFinish.Apply(T90AMod.WeatheredPaint);
			// The three big parts sit right under their bone. The small ones are found by name anywhere on the vehicle, all
			// in one scan.
			var smallParts = new Dictionary<string, Transform>();
			foreach (var child in root.GetComponentsInChildren<Transform>(true))
			{
				if (child.name.StartsWith("T90A_") && !smallParts.ContainsKey(child.name)) smallParts.Add(child.name, child);
			}
			var paintedParts = new[]
			{
				parts.Hull.Find("T90A_HULL"),
				parts.Turret.Find("T90A_TURRET"),
				parts.Gun.Find("T90A_GUN"),
				Lookup(smallParts, "T90A_STUB_HATCH"),
				Lookup(smallParts, "T90A_MANTLET_COVER"),
				Lookup(smallParts, "T90A_CUPOLA"),
				Lookup(smallParts, "T90A_KORD"),
			};
			foreach (var part in paintedParts)
			{
				if (part == null) continue;
				VehiclePaint.ApplyPaint(part.gameObject, template, flir, PaintedPrefix);
			}
			VanillaMaterials.Apply(root, T90AMod.Roles);
			DevPaint(parts);
		}

		static Transform Lookup(Dictionary<string, Transform> parts, string name)
		{
			parts.TryGetValue(name, out var part);
			return part;
		}

		static void PaintHatchUndersides(Parts parts)
		{
			if (!parts.HasRig) return;
			if (HatchUndersides.Plain(parts.Root, "t90a_hatch_inner") == 0)
				MelonLogger.Warning($"{FriendlyName}: no hatch underside material found (old bundle?).");
			HatchUndersides.Paint(
				parts.Root,
				"t90a_hatch_inner",
				TurretPaint,
				// A smooth plate of the turret atlas (x 320-550, y 1350-1460 of 4096 x 2048): a flat normal map with no bolts, welds or
				// cast texture, so the undersides take the turret's paint as one green colour (the owner's call, 2026-10-20).
				new RectInt(320, 1350, 230, 110),
				new Vector2Int(4096, 2048));
		}

		static void PlaceNumbers(Parts parts)
		{
			if (!parts.HasRig) return;
			TacticalNumbers.Apply(parts.Root, parts.Turret, Numbers);
		}

		static void HideLeftovers(Parts parts)
		{
			if (!parts.HasRig) return;
			BaseRigT72M1.HideLeftovers(parts.Root, "T90A_");
		}

		static void SyncLods(Parts parts)
		{
			if (!parts.HasRig) return;
			VisualLods.Sync(parts.Root, FriendlyName, new[] { Commander.BeltSettings.BeltMaterial });
		}

		static void SetGun(Parts parts)
		{
			GunSetup.Apply(parts.Vehicle, Firepower.Gun);
		}

		static void SeatSmoke(Parts parts)
		{
			SmokeLaunchers.Seat(parts.Vehicle, T90AMod.SmokePoints);
			SmokeCaps.Apply(parts.Root);
		}

		static void SetMobility(Parts parts)
		{
			Mobility.Apply(parts.Vehicle, Drive.Settings);
		}

		static void PlacePeriscope(Parts parts)
		{
			var peri = FindDeep(parts.Root, "Gunner's Periscope Camera")?.GetComponent<GHPC.Camera.CameraSlot>();
			if (peri != null) peri.DirectAttachOnly = true;   // Unit skips these when it lists the views
			else MelonLogger.Warning($"{FriendlyName}: gunner's periscope slot not found.");
		}

		// The T-90A's turret sits 22 cm further back on the ring than the T-72's, the trunnion 8 cm back and 7.5 cm lower, and
		// turret and seat sit 1.2 deg nose-down; the numbers are in the rig map.
		static void MoveAxes(Parts parts)
		{
			var axes = PortAxes.FromRig(T90AMod.Rig, BaseRigT72M1.TurretPivot, BaseRigT72M1.GunPivot, FriendlyName);
			if (axes != null) PortAxes.Apply(parts.Vehicle, axes);
			else MelonLogger.Warning($"{FriendlyName}: the rig map has no axes; the turret turns on the T-72M1's pivots.");
		}

		static void MoveHatches(Parts parts)
		{
			HatchRig.Apply(parts.Root, T90AMod.Rig, "T90A_", FriendlyName);
		}

		// The steps that need the components' Start to have run, in order. Each has its own try, as in Steps, so one that
		// throws leaves the rest of the tank set up.
		static readonly ConversionStep<Vehicle>[] LateSteps =
		{
			new ConversionStep<Vehicle> { What = "voices", Run = v => CrewVoices.Apply(v, FriendlyName) },
			new ConversionStep<Vehicle> { What = "sight setup", Run = Sights.Apply },
			new ConversionStep<Vehicle> { What = "commander's station late setup", Run = Commander.LateSetup },
			new ConversionStep<Vehicle> { What = "module wiring", Run = v => Modules.Wire(v), SpawnOnly = true },
			new ConversionStep<Vehicle> { What = "thermal heat", Run = ApplyThermalHeat },
			new ConversionStep<Vehicle> { What = "ammunition self-check", Run = PortAmmo.SelfCheck, SpawnOnly = true },
		};

		static void ApplyThermalHeat(Vehicle vehicle)
		{
			var parts = ThermalHeat.PortParts(vehicle.transform, "T90A_");
			ThermalHeat.Apply(vehicle.transform, parts, FriendlyName);
		}

		static IEnumerator LateSetup(Vehicle vehicle, bool atSpawn)
		{
			// A spawned unit's smoke launcher cached the crew before it existed and never fires without a refresh.
			yield return SmokeLaunchers.RefreshCrewLater(vehicle, FriendlyName);
			if (vehicle == null) yield break;
			Func<ConversionStep<Vehicle>, bool> keep = null;
			DevKeeps(ref keep);
			ConversionSteps.Run(FriendlyName, LateSteps, vehicle, vehicle.name, atSpawn, keep);
			DevAfter(vehicle, atSpawn);
		}

		// Turret-bone space: prefab minus the T-72 ring centre.
		static readonly TacticalNumbers.Settings Numbers = new TacticalNumbers.Settings
		{
			Mode = T90AMod.NumberPosition,
			LampFace = new Vector3(-0.489f, 2.380f, -1.684f) - BaseRigT72M1.TurretPivot,
			LampNormal = new Vector3(0f, 0f, -1f),
			BoxFaceR = new Vector3(1.179f, 1.804f, -1.592f) - BaseRigT72M1.TurretPivot,
			BoxNormalR = new Vector3(0.92f, -0.02f, -0.39f),
			BoxFaceL = new Vector3(-1.278f, 1.827f, -1.558f) - BaseRigT72M1.TurretPivot,
			BoxNormalL = new Vector3(-0.91f, -0.01f, -0.41f),
			DigitScale = 1.25f,
		};

		static readonly InteriorMap.Embedded Interior = new InteriorMap.Embedded(
			typeof(Conversion).Assembly,
			FriendlyName,
			T90AMod.UnitKey,
			"T-72M1");
		static readonly PortModules Modules = new PortModules(
			typeof(Conversion).Assembly,
			FriendlyName,
			"T-72M1",
			T90AMod.UnitKey);

		// The hinge of the War Thunder model's hatch, in turret-bone space.
		static readonly Vector3 HatchHinge = new Vector3(-0.224f, 2.100f, -1.494f) - BaseRigT72M1.TurretPivot;
		// From the T-72M1's hatch port centre to the T-90A's hatch centre.
		static readonly Vector3 HatchPortShift = new Vector3(-0.03f, 0.09f, -0.28f);

		const string TurretPaint = "t90a_t_90a_turret";

		internal static Transform FindDeep(Transform root, string name) =>
			root.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == name);

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevKeeps(ref Func<ConversionStep<Parts>, bool> keep);
		static partial void DevKeeps(ref Func<ConversionStep<Vehicle>, bool> keep);
		static partial void DevPaint(Parts parts);
		static partial void DevAfter(Vehicle vehicle, bool atSpawn);
	}
}
