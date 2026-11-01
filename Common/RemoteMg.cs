using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	using AmmoRack = GHPC.Weapons.AmmoRack;
	using DC = GHPC.Equipment.DestructibleComponent;
	using IDamageStatus = GHPC.Equipment.IDamageStatus;

	internal static class RemoteMg
	{
		static readonly FieldInfo StageDuration = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
		static readonly FieldInfo RackClipTypes = Reflect.Field(typeof(AmmoRack), "_clipTypes");
		static readonly FieldInfo RackInitialCounts = Reflect.Field(typeof(AmmoRack), "_initialClipCounts");
		static readonly FieldInfo UnitSlots = Reflect.Field(typeof(Unit), "_designatedCameraSlots");
		static readonly PropertyInfo UnitPlatforms = Reflect.Property(typeof(Unit), nameof(Unit.AimablePlatforms));
		static readonly FieldInfo UnitStatuses = Reflect.Field(typeof(Unit), "DamageStatuses");
		internal static readonly FieldInfo WeaponCycle = Reflect.Field(typeof(WeaponSystem), "_cycleTimeSeconds");

		internal abstract class Station : MonoBehaviour
		{
			internal WeaponSystemInfo Info;
			internal abstract IEnumerable<CameraSlot> Sights { get; }

			internal virtual IEnumerable<CameraSlot> WeaponSights => Sights;
			internal bool IsWeaponSight(CameraSlot s) => s != null && WeaponSights.Any(w => w != null && (w == s || (s.IsLinkedNightSight && s.LinkedDaySight == w)));
		}

		static readonly Dictionary<WeaponSystem, Station> ByWeapon = new Dictionary<WeaponSystem, Station>();
		internal static Station Of(WeaponSystem w)
		{
			if (w == null) return null;
			if (ByWeapon.TryGetValue(w, out var st) && (st == null || st != null && st.Info?.Weapon == w)) return st;
			foreach (var dead in ByWeapon.Keys.Where(k => k == null).ToList()) ByWeapon.Remove(dead);
			var v = w.GetComponentInParent<Vehicle>();
			st = v != null ? v.GetComponentsInChildren<Station>(true).FirstOrDefault(x => x.Info?.Weapon == w) : null;
			ByWeapon[w] = st;
			return st;
		}

		// In a free-look slot GHPC aims the selected weapon at the cursor and fires it. The station is remote.
		internal static bool InPlayerInput;

		[HarmonyPatch(typeof(PlayerInput), "Update")]
		static class PlayerInputScope
		{
			static void Prefix() => InPlayerInput = true;
			static System.Exception Finalizer(System.Exception __exception) { InPlayerInput = false; return __exception; }
		}

		[HarmonyPatch]
		static class PlayerControlPatch
		{
			static MethodBase[] _targets;
			static MethodBase[] Targets()
			{
				if (_targets != null) return _targets;
				var t = typeof(CrewBrainWeaponsModule);
				return _targets = new MethodBase[]
				{
					Reflect.Method(t, nameof(CrewBrainWeaponsModule.Fire), new[] { typeof(WeaponSystemRole), typeof(IUnit) }),
					Reflect.Method(t, nameof(CrewBrainWeaponsModule.AdjustAimByRatio), new[] { typeof(WeaponSystemRole), typeof(float), typeof(float) }),
					Reflect.Method(t, nameof(CrewBrainWeaponsModule.SetAimVector), new[] { typeof(WeaponSystemRole), typeof(Vector3), typeof(bool) }),
				}.Where(m => m != null).ToArray();
			}

			static bool Prepare() => Targets().Length > 0;
			static IEnumerable<MethodBase> TargetMethods() => Targets();

			static bool Prefix(CrewBrainWeaponsModule __instance, WeaponSystemRole __0)
			{
				var weaponSystemRole = __0;
				if (!InPlayerInput || weaponSystemRole != WeaponSystemRole.RoofGun) return true;
				var slot = CameraSlot.ActiveInstance;
				if (slot == null || !slot.AllowFreeLook) return true;
				var input = PlayerInput.Instance;
				if (input == null || (object)input.CurrentPlayerCrewBrain?.WeaponsModule != __instance) return true;
				var w = __instance.ActiveWeapon;
				if (w == null || w.Role != weaponSystemRole) return true;
				var st = Of(w.Weapon);
				return st == null || st.IsWeaponSight(slot);
			}
		}

		internal sealed class Gun
		{
			internal GameObject Go;
			internal WeaponSystem Weapon;
			internal AmmoFeed Feed;
			internal AmmoRack Rack;
			internal AmmoType.AmmoClip Belt, DonorBelt;
			internal AmmoCodexScriptable B32;
			internal float DonorCycle, Cycle;
			internal int Removed, Scrubbed;
		}

		internal sealed class GunSettings
		{
			public string Name;
			public string DamageName;
			public int BeltRounds, Belts;
			public float RoundsPerMinute;
			public int B32PerTracer;
			public string BeltName = "12.7mm B-32 / BZT-44";
		}

		internal static Gun CloneGun(Donor d, Transform holder, GunSettings s, string tag)
		{
			var g = new Gun();
			g.Go = Object.Instantiate(d.Weapon.gameObject, holder, false);
			g.Go.name = s.Name;
			g.Removed = StripReparent(g.Go);
			g.Weapon = g.Go.GetComponent<WeaponSystem>();
			g.Feed = g.Go.GetComponent<AmmoFeed>();
			g.Rack = g.Feed.ReadyRack;
			if (g.Rack == null || !g.Rack.transform.IsChildOf(g.Go.transform)) { DonorParts.WarnOnce("Kord", $"{tag} Kord (ZPU): the donor's ammo feed has no rack of its own; no Kord fitted."); return null; }
			g.Scrubbed = Scrub(g.Go, d.Root);
			g.Weapon.DamageName = s.DamageName;
			g.DonorCycle = g.Feed.RoundCycleStages.Sum(x => (float)StageDuration.GetValue(x));
			g.Cycle = 60f / s.RoundsPerMinute;
			// The weapon takes its rate from the feed's stages in Awake.
			foreach (var x in g.Feed.RoundCycleStages) StageDuration.SetValue(x, (float)StageDuration.GetValue(x) * g.Cycle / g.DonorCycle);
			g.DonorBelt = g.Rack.ClipTypes[0];
			var donorB32 = g.DonorBelt.MinimalPattern[0];
			var tracer = g.DonorBelt.MinimalPattern.First(a => a != donorB32);
			var pattern = Enumerable.Repeat(donorB32, s.B32PerTracer).Append(tracer).ToArray();
			g.Belt = new AmmoType.AmmoClip { Name = s.BeltName, MinimalPattern = pattern, Capacity = s.BeltRounds };
			RackClipTypes.SetValue(g.Rack, new[] { g.Belt });
			RackInitialCounts.SetValue(g.Rack, new[] { s.Belts });
			g.B32 = g.Belt.MinimalPattern[0];
			return g;
		}

		internal static (GameObject fcsGo, FireControlSystem fcs, GameObject opticGo, int removed, int scrubbed) CloneFcs(Donor d, Transform holder, string name)
		{
			var fcsGo = Object.Instantiate(d.Fcs.gameObject, holder, false);
			fcsGo.name = name;
			int removed = StripReparent(fcsGo);
			var fcs = fcsGo.GetComponent<FireControlSystem>();
			var opticGo = fcsGo.GetComponentInChildren<UsableOptic>(true).gameObject;
			opticGo.transform.SetParent(holder, false);
			int scrubbed = Scrub(fcsGo, d.Root) + Scrub(opticGo, d.Root);
			return (fcsGo, fcs, opticGo, removed, scrubbed);
		}

		internal static UsableOptic CloneOptic(Donor d, UsableOptic src, Transform holder, ref int removed, ref int scrubbed)
		{
			var go = Object.Instantiate(src.gameObject, holder, false);
			removed += StripReparent(go);
			scrubbed += Scrub(go, d.Root);
			foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
			return go.GetComponent<UsableOptic>();
		}

		internal static AimablePlatform Platform(Transform parent, string name, AimablePlatform donor, Transform turns, EnumHelpers.Axis3 axis, Vector2 limits, float speed, DC hitZone)
		{
			var go = new GameObject(name);
			go.SetActive(false);   // HitZones are hooked up in Awake
			go.transform.SetParent(parent, false);
			var p = go.AddComponent<AimablePlatform>();
			foreach (var f in typeof(AimablePlatform).GetFields(BindingFlags.Public | BindingFlags.Instance))
				if (!typeof(Object).IsAssignableFrom(f.FieldType) && !(f.FieldType.IsArray && typeof(Object).IsAssignableFrom(f.FieldType.GetElementType())))
					f.SetValue(p, f.GetValue(donor));
			p.Name = name;
			p.Transform = turns;
			p.Axis = axis;
			p.LocalEulerLimits = limits;
			p.ReverseLocalEuler = axis == EnumHelpers.Axis3.X;
			p.SpeedPowered = speed;
			p.HitZones = hitZone != null ? new[] { hitZone } : new DC[0];
			p.ParentPlatform = null;
			go.SetActive(true);
			return p;
		}

		internal static void Register(Vehicle vehicle, WeaponsManager wm, WeaponSystemInfo info, AimablePlatform[] platforms, IEnumerable<Component> reporters, IEnumerable<CameraSlot> sights)
		{
			wm.Weapons = wm.Weapons.Concat(new[] { info }).ToArray();
			UnitPlatforms.SetValue(vehicle, (vehicle.AimablePlatforms ?? new AimablePlatform[0]).Concat(platforms).ToArray());
			var statuses = (IDamageStatus[])UnitStatuses.GetValue(vehicle) ?? new IDamageStatus[0];
			var ours = reporters.Where(c => c != null).OfType<IDamageStatus>().Where(x => !statuses.Contains(x)).ToArray();
			UnitStatuses.SetValue(vehicle, statuses.Concat(ours).ToArray());
			// Empty means every slot under the unit. Append to it and ours are the only sights left.
			if (vehicle.DesignatedCameraSlots != null && vehicle.DesignatedCameraSlots.Length > 0)
				UnitSlots.SetValue(vehicle, vehicle.DesignatedCameraSlots.Concat(sights.Where(x => x != null)).ToArray());
		}

		internal static void LateSetup(Vehicle vehicle)
		{
			var st = vehicle.GetComponentInChildren<Station>(true);
			if (st == null) return;
			var slots = vehicle.DesignatedCameraSlots;
			var missing = st.Sights.Where(s => s != null && (slots == null || !slots.Contains(s))).ToArray();
			if (slots != null && slots.Length > 0 && missing.Length > 0) UnitSlots.SetValue(vehicle, slots.Concat(missing).ToArray());
			var crew = vehicle.GetComponent<CrewManager>();
			if (crew?.CrewMembers == null) return;
			foreach (var m in crew.CrewMembers)
			{
				var module = m?.Brain?.WeaponsModule;
				if (module?.Weapons == null || m.Position != CrewPosition.Gunner) continue;
				if (!module.Weapons.Contains(st.Info)) module.Weapons.Add(st.Info);
			}
		}

		static string _logTag = "";
		internal static void SetLogTag(string tag) => _logTag = tag ?? "";

		// Every path to the camera manager ends here, and the slots it gets don't have the station's sights.
		[HarmonyPatch(typeof(CameraManager), "RescanCamSlots")]
		static class CameraSlotsPatch
		{
			static void Prefix(ref CameraSlot[] __0)
			{
				try { __0 = WithStation(__0); }
				catch (System.Exception e) { PatchGuard.Failed("Station sights for the camera", e); }
			}

			static CameraSlot[] WithStation(CameraSlot[] cameraSlots)
			{
				if (cameraSlots == null || cameraSlots.Length == 0) return cameraSlots;
				var unit = cameraSlots.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
				var st = unit != null ? unit.GetComponentInChildren<Station>(true) : null;
				if (st == null) return cameraSlots;
				var given = cameraSlots;
				var missing = st.Sights.Where(s => s != null && !given.Contains(s)).ToArray();
				if (missing.Length == 0) return cameraSlots;
				return cameraSlots.Concat(missing).ToArray();
			}
		}

		// Shift goes back in by a stored index into a list that shrinks when the Kord isn't selected. Out of range it throws
		// and leaves the view stuck outside.
		static CameraSlot _lastSight;
		internal static CameraSlot LastSight
		{
			get { if (!ReferenceEquals(_lastSight, null) && _lastSight == null) _lastSight = null; return _lastSight; }
			private set => _lastSight = value;
		}

		static readonly FieldInfo FixedSlotsField = Reflect.Field(typeof(CameraManager), "_allFixedCamSlots"),
			FreeSlotsField = Reflect.Field(typeof(CameraManager), "_allFreeLookCamSlots"),
			ValidSlotsField = Reflect.Field(typeof(CameraManager), "_validCamSlots"),
			InteriorIndexField = Reflect.Field(typeof(CameraManager), "_interiorSlotIndex"),
			ExteriorIndexField = Reflect.Field(typeof(CameraManager), "_exteriorSlotIndex");

		static bool HasStation(IEnumerable<CameraSlot> slots)
		{
			var unit = slots?.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
			return unit != null && unit.GetComponentInChildren<Station>(true) != null;
		}

		[HarmonyPatch(typeof(CameraManager), "AttachCamToSlotDirect")]
		static class RememberSightPatch
		{
			static void Postfix(CameraSlot __0)
			{
				var camSlot = __0;
				if (camSlot != null && !camSlot.AllowFreeLook) LastSight = camSlot.IsLinkedNightSight && camSlot.LinkedDaySight != null ? camSlot.LinkedDaySight : camSlot;
			}
		}

		[HarmonyPatch(typeof(CameraManager), "ToggleCameraSlotSet")]
		static class SlotSetPatch
		{
			static void Prefix(CameraManager __instance)
			{
				bool goingIn = __instance.ExteriorMode;
				var all = (CameraSlot[])(goingIn ? FixedSlotsField : FreeSlotsField).GetValue(__instance);
				if (all == null || !HasStation(all)) return;
				var usable = all.Where(s => s != null && s.IsAvailable).ToList();
				if (usable.Count == 0) return;
				var field = goingIn ? InteriorIndexField : ExteriorIndexField;
				int old = (int)field.GetValue(__instance);
				int i;
				if (goingIn) { i = LastSight != null ? usable.IndexOf(LastSight) : -1; if (i < 0) i = 0; }
				else i = old >= 0 && old < usable.Count ? old : 0;
				if (i == old) return;
				field.SetValue(__instance, i);
			}
		}

		[HarmonyPatch(typeof(CameraManager), "ResetTempAttach")]
		static class TempAttachPatch
		{
			static void Prefix(CameraManager __instance)
			{
				var valid = (List<CameraSlot>)ValidSlotsField.GetValue(__instance);
				if (valid == null || valid.Count == 0) return;
				var field = __instance.ExteriorMode ? ExteriorIndexField : InteriorIndexField;
				int old = (int)field.GetValue(__instance);
				if (old >= 0 && old < valid.Count) return;
				if (!HasStation(valid)) return;
				int i = LastSight != null ? valid.IndexOf(LastSight) : -1;
				field.SetValue(__instance, i < 0 ? 0 : i);
			}
		}

		static readonly Dictionary<string, Mesh> _boxes = new Dictionary<string, Mesh>();
		internal static Mesh BoxMesh(string name)
		{
			if (_boxes.TryGetValue(name ?? "", out var box) && box != null) return box;
			var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
			box = Object.Instantiate(c.GetComponent<MeshFilter>().sharedMesh);
			box.name = name;
			Object.DestroyImmediate(c);
			return _boxes[name ?? ""] = box;
		}

		internal static int StripReparent(GameObject go)
		{
			var rs = go.GetComponentsInChildren<Reparent>(true);
			foreach (var r in rs) Object.DestroyImmediate(r);
			return rs.Length;
		}

		internal static int Scrub(GameObject clone, GameObject donorRoot) => DonorParts.Scrub(clone, donorRoot.transform);

		internal sealed class Donor
		{
			internal GameObject Root;
			internal WeaponSystem Weapon;
			internal FireControlSystem Fcs;
			internal AimablePlatform CupolaDrive, ElevationDrive;
			internal DC CupolaDriveZone, ElevationDriveZone, SightZone;
			internal Vector3 Axis;
			internal UsableOptic CommanderOptic, NightLook;

			internal Transform Scripts;

			static Donor _cached;

			internal static Donor Get(string tag)
			{
				if (_cached != null && _cached.Root != null && _cached.Weapon != null) return _cached;
				_cached = null;
				var all = UnitRegistry.Lookup?.AllUnits;
				if (all == null) { DonorParts.WarnOnce("Kord", $"{tag} Kord (ZPU): no unit registry yet; no donor."); return null; }
				var metas = all.Where(m => m?.Name != null && m.Name.StartsWith("T64") && m.PrefabReference != null && m.PrefabReference.RuntimeKeyIsValid())
					.OrderByDescending(m => (m.Name.StartsWith("T64B") ? 2 : 0) + (m.Name.EndsWith("84") ? 1 : 0)).ToList();
				var tried = new List<string>();
				foreach (var meta in metas)
				{
					var prefab = DonorParts.Load(meta);
					tried.Add($"{meta.Name} -> {(prefab == null ? "null" : prefab.name)}");
					var found = prefab == null ? null : From(prefab, null);
					if (found == null) continue;
					var parts = new[] { found.Scripts.gameObject, found.CupolaDriveZone?.gameObject, found.ElevationDriveZone?.gameObject, found.SightZone?.gameObject, found.NightLook?.gameObject };
					var box = DonorParts.Copy(prefab, $"{tag} Kord donor (" + prefab.name + ")", parts);
					var d = box != null ? From(box.gameObject, found.Axis) : null;
					if (d == null) { tried[tried.Count - 1] += " (copy incomplete)"; continue; }
					_cached = d;
					return d;
				}
				DonorParts.WarnOnce("Kord", $"{tag} Kord (ZPU): no donor with a remote NSV station; tried {(tried.Count == 0 ? "nothing (no T64* registry entry)" : string.Join("; ", tried))}.");
				return null;
			}

			static Donor From(GameObject root, Vector3? axis)
			{
				var scripts = root.transform.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("---CUPOLA SCRIPTS"));
				var weapon = scripts?.GetComponentsInChildren<WeaponSystem>(true).FirstOrDefault(w => w.name.Contains("NSV"));
				var fcs = scripts?.GetComponentsInChildren<FireControlSystem>(true).FirstOrDefault(f => f.LinkedWeaponSystems != null && f.LinkedWeaponSystems.Contains(weapon));
				if (weapon == null || fcs == null || fcs.Mounts == null || fcs.Mounts.Length < 2 || fcs.Mounts.Any(m => m == null) || weapon.Feed?.ReadyRack == null || fcs.GetComponentInChildren<UsableOptic>(true) == null) return null;
				if (axis == null && fcs.Mounts[0].Transform == null) return null;
				var zones = root.GetComponentsInChildren<DC>(true);
				DC Zone(string start) => zones.FirstOrDefault(z => z.name.StartsWith(start));
				return new Donor
				{
					Root = root, Scripts = scripts, Weapon = weapon, Fcs = fcs, CupolaDrive = fcs.Mounts[0], ElevationDrive = fcs.Mounts[1],
					CupolaDriveZone = Zone("Commander_Cupola_Drive"), ElevationDriveZone = Zone("Commander_AAMG_Elevation_Drive"), SightZone = Zone("Commander_Sight_PZU-5"),
					Axis = axis ?? root.transform.InverseTransformPoint(fcs.Mounts[0].Transform.position),
					CommanderOptic = scripts.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.name == "Commander's Optic" && o.slot != null),
					NightLook = root.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.slot != null && o.slot.VisionType == NightVisionType.Intensifier && !o.slot.IsExterior),
				};
			}
		}
	}
}
