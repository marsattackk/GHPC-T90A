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

	// Base for a machine gun fired from inside the tank, with its own camera slots and sight, and the patches that stop
	// GHPC treating it as the main gun.
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
			// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
			internal WeaponSystemInfo Info;
#pragma warning restore CS0649
			internal abstract IEnumerable<CameraSlot> Sights { get; }

			internal virtual IEnumerable<CameraSlot> WeaponSights => Sights;
			internal virtual bool FromHeadView => false;
			internal bool IsWeaponSight(CameraSlot s)
				=> s != null
					&& WeaponSights.Any(w => w != null
						&& (w == s || (s.IsLinkedNightSight && s.LinkedDaySight == w)));
		}

		static readonly Dictionary<WeaponSystem, Station> ByWeapon = new Dictionary<WeaponSystem, Station>();
		internal static Station Of(WeaponSystem w)
		{
			if (w == null) return null;
			if (ByWeapon.TryGetValue(w, out var station) && (station == null || station != null && station.Info?.Weapon == w))
				return station;
			UnityKeys.Prune(ByWeapon);
			var vehicle = w.GetComponentInParent<Vehicle>();
			station = vehicle != null
				? vehicle.GetComponentsInChildren<Station>(true).FirstOrDefault(c => c.Info?.Weapon == w)
				: null;
			ByWeapon[w] = station;
			return station;
		}

		// In a free-look slot GHPC aims the selected weapon at the cursor and fires it. The station is remote.
		internal static bool InPlayerInput;

		[HarmonyPatch(typeof(PlayerInput), "Update")]
		static class PlayerInputScope
		{
			static void Prefix() => InPlayerInput = true;
			static System.Exception Finalizer(System.Exception __exception)
			{
				InPlayerInput = false;
				return __exception;
			}
		}

		[HarmonyPatch]
		static class PlayerControlPatch
		{
			static MethodBase[] _targets;
			static MethodBase[] Targets()
			{
				if (_targets != null) return _targets;
				var moduleType = typeof(CrewBrainWeaponsModule);
				return _targets = new MethodBase[]
				{
					Reflect.Method(
						moduleType,
						nameof(CrewBrainWeaponsModule.Fire),
						new[] { typeof(WeaponSystemRole), typeof(IUnit) }),
					Reflect.Method(
						moduleType,
						nameof(CrewBrainWeaponsModule.AdjustAimByRatio),
						new[] { typeof(WeaponSystemRole), typeof(float), typeof(float) }),
					Reflect.Method(
						moduleType,
						nameof(CrewBrainWeaponsModule.SetAimVector),
						new[] { typeof(WeaponSystemRole), typeof(Vector3), typeof(bool) }),
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
				var weapon = __instance.ActiveWeapon;
				if (weapon == null || weapon.Role != weaponSystemRole) return true;
				var station = Of(weapon.Weapon);
				return station == null || station.FromHeadView || station.IsWeaponSight(slot);
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

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class GunSettings
		{
			public string Name;
			public string DamageName;
			public int BeltRounds, Belts;
			public float RoundsPerMinute;
			public int B32PerTracer;
			public string BeltName = "12.7mm B-32 / BZT-44";
		}
#pragma warning restore CS0649

		internal static Gun CloneGun(Donor d, Transform holder, GunSettings s, string tag)
		{
			if (StageDuration == null || RackClipTypes == null || RackInitialCounts == null) return null;
			if (UnitPlatforms == null || UnitStatuses == null) return null;
			var gun = new Gun();
			gun.Go = Object.Instantiate(d.Weapon.gameObject, holder, false);
			gun.Go.name = s.Name;
			gun.Removed = StripReparent(gun.Go);
			gun.Weapon = gun.Go.GetComponent<WeaponSystem>();
			gun.Feed = gun.Go.GetComponent<AmmoFeed>();
			gun.Rack = gun.Feed.ReadyRack;
			if (gun.Rack == null || !gun.Rack.transform.IsChildOf(gun.Go.transform))
			{
				DonorParts.WarnOnce("Kord", $"{tag} Kord (ZPU): the donor's ammo feed has no rack of its own; no Kord fitted.");
				return null;
			}
			gun.Scrubbed = Scrub(gun.Go, d.Root);
			gun.Weapon.DamageName = s.DamageName;
			gun.DonorCycle = gun.Feed.RoundCycleStages.Sum(x => (float)StageDuration.GetValue(x));
			gun.Cycle = 60f / s.RoundsPerMinute;
			// The weapon takes its rate from the feed's stages in Awake.
			ScaleStages(gun.Feed, gun.Cycle);
			gun.DonorBelt = gun.Rack.ClipTypes[0];
			var donorB32 = gun.DonorBelt.MinimalPattern[0];
			var tracer = gun.DonorBelt.MinimalPattern.First(a => a != donorB32);
			var pattern = Enumerable.Repeat(donorB32, s.B32PerTracer).Append(tracer).ToArray();
			gun.Belt = new AmmoType.AmmoClip { Name = s.BeltName, MinimalPattern = pattern, Capacity = s.BeltRounds };
			RackClipTypes.SetValue(gun.Rack, new[] { gun.Belt });
			RackInitialCounts.SetValue(gun.Rack, new[] { s.Belts });
			gun.B32 = gun.Belt.MinimalPattern[0];
			return gun;
		}

		internal static float StagesSum(IEnumerable<AmmoFeed.ReloadStage> stages)
			=> StageDuration == null ? 0f : stages.Sum(s => (float)StageDuration.GetValue(s));

		internal static void SetRate(WeaponSystem w, float roundsPerMinute)
		{
			float cycle = 60f / roundsPerMinute;
			ScaleStages(w.Feed, cycle);
			w.SetCycleTime(cycle);
		}

		static void ScaleStages(AmmoFeed feed, float cycle)
		{
			float now = StagesSum(feed.RoundCycleStages);
			if (now > 0f)
				foreach (var stage in feed.RoundCycleStages)
					StageDuration.SetValue(stage, (float)StageDuration.GetValue(stage) * cycle / now);
		}

		internal static string InitialCountsText(AmmoRack r) => string.Join("/", (int[])RackInitialCounts?.GetValue(r) ?? new int[0]);

		internal static (GameObject fcsGo, FireControlSystem fcs, GameObject opticGo, int removed, int scrubbed) CloneFcs(
			Donor d,
			Transform holder,
			string name)
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
			var clone = Object.Instantiate(src.gameObject, holder, false);
			removed += StripReparent(clone);
			scrubbed += Scrub(clone, d.Root);
			foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
			return clone.GetComponent<UsableOptic>();
		}

		internal static AimablePlatform Platform(
			Transform parent,
			string name,
			AimablePlatform donor,
			Transform turns,
			EnumHelpers.Axis3 axis,
			Vector2 limits,
			float speed,
			DC hitZone)
		{
			var platformObject = new GameObject(name);
			platformObject.SetActive(false);   // HitZones are hooked up in Awake
			platformObject.transform.SetParent(parent, false);
			var platform = platformObject.AddComponent<AimablePlatform>();
			foreach (var fieldInfo in typeof(AimablePlatform).GetFields(BindingFlags.Public | BindingFlags.Instance))
				if (!typeof(Object).IsAssignableFrom(fieldInfo.FieldType)
					&& !(fieldInfo.FieldType.IsArray && typeof(Object).IsAssignableFrom(fieldInfo.FieldType.GetElementType())))
					fieldInfo.SetValue(platform, fieldInfo.GetValue(donor));
			platform.Name = name;
			platform.Transform = turns;
			platform.Axis = axis;
			platform.LocalEulerLimits = limits;
			platform.ReverseLocalEuler = axis == EnumHelpers.Axis3.X;
			platform.SpeedPowered = speed;
			platform.HitZones = hitZone != null ? new[] { hitZone } : new DC[0];
			platform.ParentPlatform = null;
			platformObject.SetActive(true);
			return platform;
		}

		internal static IDamageStatus[] Register(
			Vehicle vehicle,
			WeaponsManager wm,
			WeaponSystemInfo info,
			AimablePlatform[] platforms,
			IEnumerable<Component> reporters,
			IEnumerable<CameraSlot> sights)
		{
			if (UnitPlatforms == null || UnitStatuses == null) return new IDamageStatus[0];
			wm.Weapons = wm.Weapons.Concat(new[] { info }).ToArray();
			UnitPlatforms.SetValue(vehicle, (vehicle.AimablePlatforms ?? new AimablePlatform[0]).Concat(platforms).ToArray());
			var statuses = (IDamageStatus[])UnitStatuses.GetValue(vehicle) ?? new IDamageStatus[0];
			var ours = reporters
				.Where(r => r != null)
				.OfType<IDamageStatus>()
				.Where(s => !statuses.Contains(s))
				.ToArray();
			UnitStatuses.SetValue(vehicle, statuses.Concat(ours).ToArray());
			// Empty means every slot under the unit. Append to it and ours are the only sights left.
			if (UnitSlots != null && vehicle.DesignatedCameraSlots != null && vehicle.DesignatedCameraSlots.Length > 0)
				UnitSlots.SetValue(vehicle, vehicle.DesignatedCameraSlots.Concat(sights.Where(s => s != null)).ToArray());
			return ours;
		}

		internal static void LateSetup(Vehicle vehicle)
		{
			foreach (var station in vehicle.GetComponentsInChildren<Station>(true)) LateSetup(vehicle, station);
		}

		static void LateSetup(Vehicle vehicle, Station st)
		{
			var slots = vehicle.DesignatedCameraSlots;
			var missing = st.Sights.Where(s => s != null && (slots == null || !slots.Contains(s))).ToArray();
			if (UnitSlots != null && slots != null && slots.Length > 0 && missing.Length > 0)
			{
				UnitSlots.SetValue(vehicle, slots.Concat(missing).ToArray());
			}
			var crew = vehicle.GetComponent<CrewManager>();
			if (crew?.CrewMembers == null || st.Info == null) return;
			foreach (var member in crew.CrewMembers)
			{
				var module = member?.Brain?.WeaponsModule;
				if (module?.Weapons == null || member.Position != CrewPosition.Gunner) continue;
				if (!module.Weapons.Contains(st.Info)) module.Weapons.Add(st.Info);
			}
		}

		// Every path to the camera manager ends here, and the slots it gets don't have the station's sights.
		[HarmonyPatch(typeof(CameraManager), "RescanCamSlots")]
		static class CameraSlotsPatch
		{
			static bool _seen;
			static void Prefix(ref CameraSlot[] __0)
			{
				try { __0 = WithStation(__0); }
				catch (System.Exception exception) { PatchGuard.Failed("Station sights for the camera", exception); }
			}

			static CameraSlot[] WithStation(CameraSlot[] cameraSlots)
			{
				if (cameraSlots == null || cameraSlots.Length == 0) return cameraSlots;
				var unit = cameraSlots.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
				var stations = unit != null ? unit.GetComponentsInChildren<Station>(true) : new Station[0];
				if (stations.Length == 0) return cameraSlots;
				var given = cameraSlots;
				var missing = stations
					.SelectMany(s => s.Sights)
					.Where(s => s != null && !given.Contains(s))
					.Distinct()
					.ToArray();
				if (missing.Length == 0) return cameraSlots;
				if (!_seen) _seen = true;
				return cameraSlots.Concat(missing).ToArray();
			}
		}

		// Shift goes back in by a stored index into a list that shrinks when the Kord isn't selected. Out of range it throws
		// and leaves the view stuck outside.
		static CameraSlot _lastSight;
		internal static CameraSlot LastSight
		{
			get
			{
				if (!ReferenceEquals(_lastSight, null) && _lastSight == null) _lastSight = null;
				return _lastSight;
			}
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
				if (camSlot != null && !camSlot.AllowFreeLook)
					LastSight = camSlot.IsLinkedNightSight && camSlot.LinkedDaySight != null ? camSlot.LinkedDaySight : camSlot;
			}
		}

		[HarmonyPatch(typeof(CameraManager), "ToggleCameraSlotSet")]
		static class SlotSetPatch
		{
			static bool Prepare() => FixedSlotsField != null
				&& FreeSlotsField != null
				&& InteriorIndexField != null
				&& ExteriorIndexField != null;

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
				if (goingIn)
				{
					i = LastSight != null ? usable.IndexOf(LastSight) : -1;
					if (i < 0) i = 0;
				}
				else i = old >= 0 && old < usable.Count ? old : 0;
				if (i == old) return;
				field.SetValue(__instance, i);
			}
		}

		[HarmonyPatch(typeof(CameraManager), "ResetTempAttach")]
		static class TempAttachPatch
		{
			static bool Prepare() => ValidSlotsField != null && InteriorIndexField != null && ExteriorIndexField != null;

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

		internal static string GroupOf(Transform t)
		{
			while (t != null)
			{
				var colliderGroup = t.GetComponent<GHPC.PhysicsHelpers.RaycastColliderGroup>();
				if (colliderGroup != null) return colliderGroup.name;
				if (t.parent != null)
				{
					t = t.parent;
					continue;
				}
				var lateFollow = t.GetComponent<LateFollow>();
				t = lateFollow != null ? lateFollow.FollowTarget : null;
			}
			return null;
		}

		static readonly Dictionary<string, Mesh> _boxes = new Dictionary<string, Mesh>();
		internal static Mesh BoxMesh(string name)
		{
			if (_boxes.TryGetValue(name ?? "", out var box) && box != null) return box;
			var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
			box = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
			box.name = name;
			Object.DestroyImmediate(cube);
			return _boxes[name ?? ""] = box;
		}

		internal static int StripReparent(GameObject go)
		{
			var reparents = go.GetComponentsInChildren<Reparent>(true);
			foreach (var reparent in reparents) Object.DestroyImmediate(reparent);
			return reparents.Length;
		}

		internal static int Scrub(GameObject clone, GameObject donorRoot) => DonorParts.Scrub(clone, donorRoot.transform);

		internal sealed class Donor
		{
			internal GameObject Root;
			internal WeaponSystem Weapon;
			internal AmmoRack Rack;
			internal FireControlSystem Fcs;
			internal AimablePlatform CupolaDrive, ElevationDrive;
			internal DC CupolaDriveZone, ElevationDriveZone, SightZone;
			internal Vector3 Axis;
			internal UsableOptic CommanderOptic, NightLook;

			internal Transform Scripts;

			static Donor _cached;
			internal static string Key;

			internal static Donor Get(string tag)
			{
				if (_cached != null && _cached.Root != null && _cached.Weapon != null) return _cached;
				_cached = null;
				var all = UnitRegistry.Lookup?.AllUnits;
				if (all == null)
				{
					DonorParts.WarnOnce("Kord", $"{tag} Kord (ZPU): no unit registry yet; no donor.");
					return null;
				}
				var metas = all.Where(m => m?.Name != null
					&& m.Name.StartsWith("T64")
					&& m.PrefabReference != null
					&& m.PrefabReference.RuntimeKeyIsValid())
					.OrderByDescending(m => (m.Name.StartsWith("T64B") ? 2 : 0) + (m.Name.EndsWith("84") ? 1 : 0))
					.ToList();
				var tried = new List<string>();
				foreach (var meta in metas)
				{
					var prefab = DonorParts.Load(meta);
					tried.Add($"{meta.Name} -> {(prefab == null ? "null" : prefab.name)}");
					var found = prefab == null ? null : From(prefab, null);
					if (found == null) continue;
					var parts = new[]
					{
						found.Scripts.gameObject,
						found.CupolaDriveZone?.gameObject,
						found.ElevationDriveZone?.gameObject,
						found.SightZone?.gameObject,
						found.NightLook?.gameObject
					};
					var box = DonorParts.Copy(prefab, $"{tag} Kord donor (" + prefab.name + ")", parts);
					var donor = box != null ? From(box.gameObject, found.Axis) : null;
					if (donor == null)
					{
						tried[tried.Count - 1] += " (copy incomplete)";
						continue;
					}
					_cached = donor;
					Key = meta.Name;
					return donor;
				}
				DonorParts.WarnOnce(
					"Kord",
					$"{tag} Kord (ZPU): no donor with a remote NSV station; tried {(tried.Count == 0 ? "nothing (no T64* registry entry)" : string.Join("; ", tried))}.");
				return null;
			}

			static Donor From(GameObject root, Vector3? axis)
			{
				var scripts = root.transform.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith("---CUPOLA SCRIPTS"));
				var weapon = scripts?.GetComponentsInChildren<WeaponSystem>(true)
					.FirstOrDefault(c => c.name.Contains("NSV"));
				var fcs = scripts?.GetComponentsInChildren<FireControlSystem>(true)
					.FirstOrDefault(c => c.LinkedWeaponSystems != null
						&& c.LinkedWeaponSystems.Contains(weapon));
				if (weapon == null
					|| fcs == null
					|| fcs.Mounts == null
					|| fcs.Mounts.Length < 2
					|| fcs.Mounts.Any(m => m == null)
					|| weapon.Feed?.ReadyRack == null
					|| fcs.GetComponentInChildren<UsableOptic>(true) == null)
					return null;
				if (axis == null && fcs.Mounts[0].Transform == null) return null;
				var zones = root.GetComponentsInChildren<DC>(true);
				DC Zone(string start) => zones.FirstOrDefault(z => z.name.StartsWith(start));
				return new Donor
				{
					Root = root,
					Scripts = scripts,
					Weapon = weapon,
					Rack = weapon.Feed.ReadyRack,
					Fcs = fcs,
					CupolaDrive = fcs.Mounts[0],
					ElevationDrive = fcs.Mounts[1],
					CupolaDriveZone = Zone("Commander_Cupola_Drive"),
					ElevationDriveZone = Zone("Commander_AAMG_Elevation_Drive"),
					SightZone = Zone("Commander_Sight_PZU-5"),
					Axis = axis ?? root.transform.InverseTransformPoint(fcs.Mounts[0].Transform.position),
					CommanderOptic = scripts
						.GetComponentsInChildren<UsableOptic>(true)
						.FirstOrDefault(o => o.name == "Commander's Optic" && o.slot != null),
					NightLook = root
						.GetComponentsInChildren<UsableOptic>(true)
						.FirstOrDefault(o => o.slot != null
							&& o.slot.VisionType == NightVisionType.Intensifier
							&& !o.slot.IsExterior),
				};
			}
		}
	}
}
