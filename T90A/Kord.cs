using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Equipment.Optics;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using Reticle;
using T90A.Common;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace T90A
{
	using AmmoRack = GHPC.Weapons.AmmoRack;
	using DC = GHPC.Equipment.DestructibleComponent;
	using IDamageStatus = GHPC.Equipment.IDamageStatus;

	internal static class Kord
	{
		internal static readonly Vector3 CupolaAxis = new Vector3(0.602f, 2.054f, -1.033f);
		internal static readonly Vector3 Trunnion = new Vector3(0.583f, 2.526f, -0.593f);
		static readonly Vector3 WtMuzzle = new Vector3(0.582f, 2.510f, 0.873f);
		static readonly Vector3 PzuHead = new Vector3(0.187f, 2.280f, -0.850f);
		internal static Vector3 Muzzle => Trunnion + Quaternion.AngleAxis(-RealAxis.GunLevelDeg, Vector3.right) * (WtMuzzle - Trunnion);

		internal const string Name = "12.7mm Machine Gun 6P49 Kord";
		internal const int BeltRounds = 150, Belts = 2;
		internal const float RoundsPerMinute = 625f;
		static readonly Vector2 Traverse = new Vector2(-45f, 60f);
		static readonly Vector2 Elevation = new Vector2(-5f, 70f);
		const float CupolaSpeed = 35f;
		const float ElevationSpeed = 24f;
		const int BeltB32PerTracer = 3;

		static readonly FieldInfo StageDuration = AccessTools.Field(typeof(AmmoFeed.ReloadStage), "Duration");
		static readonly FieldInfo RackClipTypes = AccessTools.Field(typeof(AmmoRack), "_clipTypes");
		static readonly FieldInfo RackInitialCounts = AccessTools.Field(typeof(AmmoRack), "_initialClipCounts");
		static readonly FieldInfo SlotReparent = AccessTools.Field(typeof(CameraSlot), "_reparentTarget");
		static readonly FieldInfo UnitSlots = AccessTools.Field(typeof(Unit), "_designatedCameraSlots");
		static readonly PropertyInfo UnitPlatforms = AccessTools.Property(typeof(Unit), nameof(Unit.AimablePlatforms));
		static readonly FieldInfo UnitStatuses = AccessTools.Field(typeof(Unit), "DamageStatuses");
		static readonly FieldInfo ArmorName = AccessTools.Field(typeof(UniformArmor), "_name");

		internal class Station : MonoBehaviour
		{
			internal WeaponSystemInfo Info;
			internal UsableOptic Pzu7;
			internal UsableOptic Tkn4s;
			internal CommanderSight.Avtomat Auto;
		}

		internal class Zones
		{
			internal DC CupolaDrive, ElevationDrive, Sight, Gun, Tkn4s;
		}

		static readonly Vector3 GunZoneCentre = new Vector3(0.010f, 0.012f, 0.685f), GunZoneSize = new Vector3(0.112f, 0.176f, 1.562f);

		internal static void Apply(Vehicle vehicle)
		{
			var root = vehicle.transform;
			var d = Donor.Get();
			if (d == null) return;
			var turret = root.Find("---MESH---/HULL/TURRET");
			var cupolaBone = turret?.Find("CUPOLA");
			var wm = vehicle.GetComponent<WeaponsManager>();
			if (cupolaBone == null || wm == null || wm.Weapons == null || wm.Weapons.Length < 2) { DonorParts.WarnOnce("Kord", "T-90A Kord (ZPU): TURRET/CUPOLA bone or the main gun / coax rows not found; no Kord fitted."); return; }
			Vector3 W(Vector3 prefab) => root.TransformPoint(prefab);

			var holder = new GameObject("T90A ZPU (setup)");
			holder.SetActive(false);
			holder.transform.SetParent(root, false);

			var cradle = new GameObject("T90A Kord cradle").transform;
			cradle.SetParent(cupolaBone, false);
			cradle.SetPositionAndRotation(W(Trunnion), cupolaBone.rotation);
			var pzuHead = new GameObject("T90A PZU-7 head").transform;
			pzuHead.SetParent(cupolaBone, false);
			pzuHead.SetPositionAndRotation(W(PzuHead), cupolaBone.rotation);
			pzuHead.gameObject.AddComponent<RodLink>().Source = cradle;

			var cupVis = turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "T90A_CUPOLA");
			var kordVis = turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "T90A_KORD");
			cupVis?.SetParent(cupolaBone, true);
			kordVis?.SetParent(cradle, true);
			if (cupVis != null)
				try { KordBelt.Fit(cupVis, cradle); }
				catch (System.Exception e) { MelonLogger.Error("T-90A Kord belt failed: " + e); }

			var zones = FitZones(vehicle, d, cupolaBone, turret, cradle, holder.transform);

			var gunGo = Object.Instantiate(d.Weapon.gameObject, holder.transform, false);
			gunGo.name = Name;
			StripReparent(gunGo);
			var weapon = gunGo.GetComponent<WeaponSystem>();
			var feed = gunGo.GetComponent<AmmoFeed>();
			var rack = feed.ReadyRack;
			if (rack == null || !rack.transform.IsChildOf(gunGo.transform)) { DonorParts.WarnOnce("Kord", "T-90A Kord (ZPU): the donor's ammo feed has no rack of its own; no Kord fitted."); Object.Destroy(holder); return; }
			Scrub(gunGo, d.Root);
			weapon.DamageName = "6P49 Kord";
			weapon.BarrelHitZone = zones.Gun;
			float donorCycle = feed.RoundCycleStages.Sum(s => (float)StageDuration.GetValue(s));
			float cycle = 60f / RoundsPerMinute;
			foreach (var s in feed.RoundCycleStages) StageDuration.SetValue(s, (float)StageDuration.GetValue(s) * cycle / donorCycle);
			var donorBelt = rack.ClipTypes[0];
			var donorB32 = donorBelt.MinimalPattern[0];
			var tracer = donorBelt.MinimalPattern.First(a => a != donorB32);
			var pattern = Enumerable.Repeat(donorB32, BeltB32PerTracer).Append(tracer).ToArray();
			var belt = new AmmoType.AmmoClip { Name = "12.7mm B-32 / BZT-44", MinimalPattern = pattern, Capacity = BeltRounds };
			RackClipTypes.SetValue(rack, new[] { belt });
			RackInitialCounts.SetValue(rack, new[] { Belts });
			var b32 = belt.MinimalPattern[0];

			var fcsGo = Object.Instantiate(d.Fcs.gameObject, holder.transform, false);
			fcsGo.name = "T90A Kord fire control";
			StripReparent(fcsGo);
			var fcs = fcsGo.GetComponent<FireControlSystem>();
			var opticGo = fcsGo.GetComponentInChildren<UsableOptic>(true).gameObject;
			opticGo.transform.SetParent(holder.transform, false);
			Scrub(fcsGo, d.Root);
			Scrub(opticGo, d.Root);
			var reference = new GameObject("T90A Kord aim reference").transform;
			reference.SetParent(cupolaBone, false);
			reference.SetPositionAndRotation(W(PzuHead), cupolaBone.rotation);
			fcs.ReferenceTransform = reference;
			fcs.LinkedWeaponSystems = new[] { weapon };

			var scripts = new GameObject("---T90A ZPU SCRIPTS---");
			scripts.transform.SetParent(root, false);
			var cupola = Platform(scripts.transform, "ZPU cupola drive", d.CupolaDrive, cupolaBone, EnumHelpers.Axis3.Y, Traverse, CupolaSpeed, zones.CupolaDrive);
			var elev = Platform(scripts.transform, "ZPU elevation drive", d.ElevationDrive, cradle, EnumHelpers.Axis3.X, Elevation, ElevationSpeed, zones.ElevationDrive);
			fcs.Mounts = new[] { cupola, elev };

			var optic = opticGo.GetComponent<UsableOptic>();
			var slot = optic.slot;
			opticGo.name = "PZU-7 sight";
			SlotReparent.SetValue(slot, pzuHead);
			optic.FCS = fcs;
			optic.LevelTransform = Conversion.FindDeep(root, "---TURRET SCRIPTS---") ?? turret;
			optic.RegisteredAmmoTypes = new[] { b32 };
			slot.ExclusiveWeapons = new[] { weapon };
			slot.HitZone = zones.Sight;
			slot.SlotToUseOnDestroy = root.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => s.name == "commander head");
			if (optic.reticleMesh != null && optic.reticleMesh.reticleSO != null)
				optic.reticleMesh.reticleSO = _pzuReticle ?? (_pzuReticle = Pzu7Reticle(optic.reticleMesh.reticleSO, b32));

			UsableOptic tkn = null, tknNight = null;
			Transform tknHead = null;
			if (d.CommanderOptic != null)
			{
				tknHead = new GameObject("T90A TKN-4S head").transform;
				tknHead.SetParent(cupolaBone, false);
				tknHead.SetPositionAndRotation(W(CommanderSight.Head), cupolaBone.rotation);
				UsableOptic CloneOptic()
				{
					var go = Object.Instantiate(d.CommanderOptic.gameObject, holder.transform, false);
					StripReparent(go);
					Scrub(go, d.Root);
					foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
					return go.GetComponent<UsableOptic>();
				}
				tkn = CloneOptic();
				tknNight = CloneOptic();
				var gunnerDay = (wm.Weapons[0].FCS != null ? SightSetup.DayOptic(wm.Weapons[0].FCS) : null)
					?? vehicle.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.FCS == wm.Weapons[0].FCS && o.slot != null && !o.slot.IsLinkedNightSight);
				var fontHost = gunnerDay?.reticleMesh?.reticleSO;
				var clips = vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
				AmmoCodexScriptable Round(int i, string name, string token) =>
					(clips != null && clips.Length > i && clips[i] != null ? clips[i].ClipType.MinimalPattern[0] : null) ?? (fontHost != null ? Sight1G46.FindAmmo(name, fontHost, token) : null);
				var rounds = new Dictionary<string, AmmoCodexScriptable> { { "b32", b32 },
					{ "apfsds", Round(0, Sight1G46.ApfsdsAmmo, "3BM") }, { "he", Round(2, Sight1G46.HeAmmo, "3OF") }, { "heat", Round(1, Sight1G46.HeatAmmo, "3BK") } };
				var reticle = Reticles.Build("tkn4s_day.jsonc", tkn.reticleMesh.reticleSO, fontHost, rounds);
				var nightReticle = Reticles.Build("tkn4s_night.jsonc", null, fontHost, null);
				CommanderSight.Configure(tkn, tknNight, d.NightLook, tknHead, fcs, weapon, b32, optic.LevelTransform, slot, zones.Tkn4s, reticle, nightReticle);
				Tkn4sReadout.Build(tkn, wm.Weapons[0].Weapon, wm.Weapons[0].FCS);
			}

			gunGo.transform.SetParent(cradle, false);
			gunGo.transform.SetPositionAndRotation(W(Muzzle), cradle.rotation);
			gunGo.SetActive(true);
			fcsGo.transform.SetParent(scripts.transform, false);
			fcsGo.SetActive(true);
			opticGo.transform.SetParent(pzuHead, false);
			opticGo.transform.localPosition = Vector3.zero;
			opticGo.transform.localRotation = Quaternion.identity;
			opticGo.SetActive(true);
			if (tkn != null)
				foreach (var o in new[] { tknNight, tkn })
				{
					o.transform.SetParent(tknHead, false);
					o.transform.localPosition = Vector3.zero;
					o.transform.localRotation = Quaternion.identity;
					o.gameObject.SetActive(true);
				}
			Object.Destroy(holder);
			weapon.SetCycleTime(cycle);

			var info = new WeaponSystemInfo { Name = Name, Role = WeaponSystemRole.RoofGun, Weapon = weapon, FCS = fcs, PreAimWeapon = WeaponSystemRole.MainGun, PreAimAngleThreshold = 3f };
			var gunner = new[] { wm.Weapons[0].Weapon, wm.Weapons[1].Weapon };
			wm.Weapons = wm.Weapons.Concat(new[] { info }).ToArray();
			UnitPlatforms.SetValue(vehicle, (vehicle.AimablePlatforms ?? new AimablePlatform[0]).Concat(new[] { cupola, elev }).ToArray());
			var statuses = (IDamageStatus[])UnitStatuses.GetValue(vehicle) ?? new IDamageStatus[0];
			var ours = new Component[] { cupola, elev, weapon, feed, fcs, slot, tkn?.slot, tknNight?.slot }.Where(c => c != null).OfType<IDamageStatus>().Where(s => !statuses.Contains(s)).ToArray();
			UnitStatuses.SetValue(vehicle, statuses.Concat(ours).ToArray());
			if (vehicle.DesignatedCameraSlots != null && vehicle.DesignatedCameraSlots.Length > 0)
				UnitSlots.SetValue(vehicle, vehicle.DesignatedCameraSlots.Concat(KordSlots(slot, tkn?.slot)).ToArray());
			var dayOptic = wm.Weapons[0].FCS != null ? SightSetup.DayOptic(wm.Weapons[0].FCS) : null;
			var gunnerSlots = new List<CameraSlot>();
			var daySlot = dayOptic?.slot ?? vehicle.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.FCS == wm.Weapons[0].FCS && !o.slot.IsLinkedNightSight)?.slot;
			if (daySlot != null) { gunnerSlots.Add(daySlot); if (daySlot.LinkedNightSight != null) gunnerSlots.Add(daySlot.LinkedNightSight); }
			foreach (var s in gunnerSlots) s.ExclusiveWeapons = gunner;

			var st = scripts.AddComponent<Station>();
			st.Info = info; st.Pzu7 = optic;
			st.Tkn4s = tkn;
			if (tkn != null)
			{
				st.Auto = scripts.AddComponent<CommanderSight.Avtomat>();
				st.Auto.Init(fcs, cupola, elev, turret, tkn, tknNight, slot, b32.AmmoType, Traverse);
			}
		}

		internal static void LateSetup(Vehicle vehicle)
		{
			var st = vehicle.GetComponentInChildren<Station>(true);
			if (st == null) return;
			var slots = vehicle.DesignatedCameraSlots;
			var missing = KordSlots(st.Pzu7.slot, st.Tkn4s?.slot).Where(s => slots == null || !slots.Contains(s)).ToArray();
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

		[HarmonyPatch(typeof(CameraManager), "RescanCamSlots")]
		static class CameraSlotsPatch
		{
			static void Prefix(ref CameraSlot[] cameraSlots)
			{
				if (cameraSlots == null || cameraSlots.Length == 0) return;
				var unit = cameraSlots.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
				var st = unit != null ? unit.GetComponentInChildren<Station>(true) : null;
				if (st == null || st.Pzu7 == null) return;
				var given = cameraSlots;
				var missing = KordSlots(st.Pzu7.slot, st.Tkn4s?.slot).Where(s => !given.Contains(s)).ToArray();
				if (missing.Length == 0) return;
				cameraSlots = cameraSlots.Concat(missing).ToArray();
			}
		}

		internal static CameraSlot LastSight { get; private set; }

		static readonly FieldInfo FixedSlotsField = AccessTools.Field(typeof(CameraManager), "_allFixedCamSlots"),
			FreeSlotsField = AccessTools.Field(typeof(CameraManager), "_allFreeLookCamSlots"),
			ValidSlotsField = AccessTools.Field(typeof(CameraManager), "_validCamSlots"),
			InteriorIndexField = AccessTools.Field(typeof(CameraManager), "_interiorSlotIndex"),
			ExteriorIndexField = AccessTools.Field(typeof(CameraManager), "_exteriorSlotIndex");

		static bool HasStation(IEnumerable<CameraSlot> slots)
		{
			var unit = slots?.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
			return unit != null && unit.GetComponentInChildren<Station>(true) != null;
		}

		[HarmonyPatch(typeof(CameraManager), "AttachCamToSlotDirect")]
		static class RememberSightPatch
		{
			static void Postfix(CameraSlot camSlot)
			{
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

		static IEnumerable<CameraSlot> KordSlots(CameraSlot pzu7, CameraSlot tkn) => new[] { pzu7, tkn }.Where(s => s != null);
		static ReticleSO _pzuReticle;

		static AimablePlatform Platform(Transform parent, string name, AimablePlatform donor, Transform turns, EnumHelpers.Axis3 axis, Vector2 limits, float speed, DC hitZone)
		{
			var go = new GameObject(name);
			go.SetActive(false);
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

		static Zones FitZones(Vehicle vehicle, Donor d, Transform cupolaBone, Transform turret, Transform cradle, Transform holder)
		{
			var z = new Zones();
			var root = vehicle.transform;
			Transform Aar(Transform bone, string token)
			{
				var f = Followers.On(root, bone).FirstOrDefault(x => x != null && x.name.Contains(token));
				return f == null ? null : (f.transform.Find("AAR") ?? f.transform);
			}
			var cupAar = Aar(cupolaBone, "CUPOLA COLLIDERS");
			var turAar = Aar(turret, "TURRET COLLIDERS");
			var shift = CupolaAxis - d.Axis;
			shift.y = 0f;

			GameObject Clone(DC src, string name)
			{
				var go = Object.Instantiate(src.gameObject, holder, false);
				go.SetActive(false);
				go.name = "T90A_" + name;
				Scrub(go, d.Root);
				return go;
			}
			DC Wake(GameObject go, Transform bone, Transform group, Matrix4x4 inBone, string dcName)
			{
				Followers.PlaceOnBone(go.transform, bone, group);
				var m = Matrix4x4.TRS(go.transform.localPosition, go.transform.localRotation, Vector3.one) * inBone;
				go.transform.localPosition = m.GetColumn(3);
				go.transform.localRotation = m.rotation;
				go.transform.localScale = m.lossyScale;
				var mesh = go.GetComponent<MeshFilter>()?.sharedMesh;
				foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
				var box = go.AddComponent<BoxCollider>();
				if (mesh != null) { box.center = mesh.bounds.center; box.size = mesh.bounds.size; }
				var armor = go.GetComponent<UniformArmor>();
				if (armor != null) { armor.Unit = vehicle; ArmorName.SetValue(armor, dcName); }
				var dc = go.GetComponent<DC>();
				dc.SetName(dcName);
				go.SetActive(true);
				GHPC.PhysicsHelpers.RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
				return dc;
			}
			Matrix4x4 Moved(DC src, Transform bone) =>
				(root.worldToLocalMatrix * bone.localToWorldMatrix).inverse * Matrix4x4.Translate(shift) * (d.Root.transform.worldToLocalMatrix * src.transform.localToWorldMatrix);

			if (d.CupolaDriveZone != null && turAar != null) z.CupolaDrive = Wake(Clone(d.CupolaDriveZone, "ZPU cupola drive"), turret, turAar, Moved(d.CupolaDriveZone, turret), "commander's cupola drive");
			if (d.ElevationDriveZone != null && cupAar != null) z.ElevationDrive = Wake(Clone(d.ElevationDriveZone, "ZPU elevation drive"), cupolaBone, cupAar, Moved(d.ElevationDriveZone, cupolaBone), "ZPU elevation drive");
			if (d.SightZone != null && cupAar != null) z.Sight = Wake(Clone(d.SightZone, "PZU-7 sight"), cupolaBone, cupAar, Moved(d.SightZone, cupolaBone), "PZU-7 sight");
			if (d.SightZone != null && cupAar != null && d.CommanderOptic != null)
			{
				var go = Clone(d.SightZone, "TKN-4S sight");
				go.GetComponent<MeshFilter>().sharedMesh = BoxMesh();
				var inBone = (root.worldToLocalMatrix * cupolaBone.localToWorldMatrix).inverse * Matrix4x4.TRS(CommanderSight.ZoneCentre, Quaternion.identity, CommanderSight.ZoneSize);
				z.Tkn4s = Wake(go, cupolaBone, cupAar, inBone, "TKN-4S commander's sight");
			}

			if (d.ElevationDriveZone != null)
			{
				var grp = new GameObject("T90A KORD COLLIDERS");
				grp.SetActive(false);
				grp.layer = 8;
				grp.transform.SetParent(root, false);
				grp.transform.SetPositionAndRotation(cradle.position, cradle.rotation);
				var follow = grp.AddComponent<LateFollow>();
				follow.FollowTarget = cradle;
				follow.ForceToRoot = true;
				var aar = new GameObject("AAR").transform;
				aar.gameObject.layer = 8;
				aar.SetParent(grp.transform, false);
				grp.SetActive(true);
				var go = Clone(d.ElevationDriveZone, "Kord barrel");
				go.GetComponent<MeshFilter>().sharedMesh = BoxMesh();
				foreach (var v in go.GetComponentsInChildren<AarVisual>(true)) Object.DestroyImmediate(v);
				foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
				z.Gun = Wake(go, cradle, aar, Matrix4x4.TRS(GunZoneCentre, Quaternion.identity, GunZoneSize), "6P49 Kord");
			}
			return z;
		}

		static Mesh _box;
		static Mesh BoxMesh()
		{
			if (_box != null) return _box;
			var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
			_box = Object.Instantiate(c.GetComponent<MeshFilter>().sharedMesh);
			_box.name = "T90A hit zone box";
			Object.DestroyImmediate(c);
			return _box;
		}

		static void StripReparent(GameObject go)
		{
			var rs = go.GetComponentsInChildren<Reparent>(true);
			foreach (var r in rs) Object.DestroyImmediate(r);
		}

		static void Scrub(GameObject clone, GameObject donorRoot) => DonorParts.Scrub(clone, donorRoot.transform);

		static ReticleSO Pzu7Reticle(ReticleSO pzu5, AmmoCodexScriptable b32)
		{
			var so = Object.Instantiate(pzu5);
			so.name = "PZU-7 (T-90A, from " + pzu5.name + ")";
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e is ReticleTree.VerticalBallistic vb && vb.projectile != null && vb.projectile != b32) { vb.projectile = b32; vb.UpdateBC(); }
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
			return so;
		}

		[DefaultExecutionOrder(10)]
		internal class RodLink : MonoBehaviour
		{
			internal Transform Source;
			void LateUpdate() { if (Source != null) transform.localRotation = Source.localRotation; }
		}

		sealed class Donor
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

			internal static Donor Get()
			{
				if (_cached != null && _cached.Root != null && _cached.Weapon != null) return _cached;
				_cached = null;
				var all = UnitRegistry.Lookup?.AllUnits;
				if (all == null) { DonorParts.WarnOnce("Kord", "T-90A Kord (ZPU): no unit registry yet; no donor."); return null; }
				var metas = all.Where(m => m?.Name != null && m.Name.StartsWith("T64") && m.PrefabReference != null && m.PrefabReference.RuntimeKeyIsValid())
					.OrderByDescending(m => (m.Name.StartsWith("T64B") ? 2 : 0) + (m.Name.EndsWith("84") ? 1 : 0)).ToList();
				var tried = new List<string>();
				foreach (var meta in metas)
				{
					var prefab = Addressables.LoadAssetAsync<GameObject>(meta.PrefabReference.RuntimeKey).WaitForCompletion();
					tried.Add($"{meta.Name} -> {(prefab == null ? "null" : prefab.name)}");
					var found = prefab == null ? null : From(prefab, null);
					if (found == null) continue;
					var parts = new[] { found.Scripts.gameObject, found.CupolaDriveZone?.gameObject, found.ElevationDriveZone?.gameObject, found.SightZone?.gameObject, found.NightLook?.gameObject };
					var box = DonorParts.Copy(prefab, "T-90A Kord donor (" + prefab.name + ")", parts);
					var d = box != null ? From(box.gameObject, found.Axis) : null;
					if (d == null) { tried[tried.Count - 1] += " (copy incomplete)"; continue; }
					_cached = d;
					return d;
				}
				DonorParts.WarnOnce("Kord", $"T-90A Kord (ZPU): no donor with a remote NSV station; tried {(tried.Count == 0 ? "nothing (no T64* registry entry)" : string.Join("; ", tried))}.");
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
