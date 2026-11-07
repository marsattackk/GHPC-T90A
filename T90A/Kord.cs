using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using MelonLoader;
using Reticle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	using DC = GHPC.Equipment.DestructibleComponent;
	using Donor = T90A.Common.RemoteMg.Donor;

	internal static class Kord
	{
		internal static readonly Vector3 CupolaAxis = new Vector3(0.602f, 2.054f, -1.033f);
		internal static readonly Vector3 Trunnion = new Vector3(0.583f, 2.526f, -0.593f);
		static readonly Vector3 WtMuzzle = new Vector3(0.582f, 2.510f, 0.873f);
		// Top front of the head. On the rod pivot the cradle filled half the view.
		static readonly Vector3 PzuHead = new Vector3(0.187f, 2.280f, -0.850f);
		internal static Vector3 Muzzle => Trunnion + Quaternion.AngleAxis(-RealAxis.GunLevelDeg, Vector3.right) * (WtMuzzle - Trunnion);
		static readonly Vector3 WtCaseChute = new Vector3(0.612f, 2.505f, -0.090f);
		internal static Vector3 CaseChute => Trunnion + Quaternion.AngleAxis(-RealAxis.GunLevelDeg, Vector3.right) * (WtCaseChute - Trunnion);
		static readonly Vector3 CaseChuteDir = new Vector3(0.10f, -0.08f, 1f);
		internal static readonly KordBelt.Settings BeltSettings = new KordBelt.Settings
		{
Prefix = "T90A_", MeshName = "T90A Kord", BeltMaterial = "t90a_mg_6p49_kord_ammo",
			SpareRounds = 24,
			MaxCases = BeltRounds * Belts,
		};

		internal const string Name = "12.7mm Machine Gun 6P49 Kord";
		internal const int BeltRounds = 150, Belts = 2;
		internal const float RoundsPerMinute = 625f;
		static readonly Vector2 Traverse = new Vector2(-45f, 60f);
		static readonly Vector2 Elevation = new Vector2(-5f, 70f);
		const float CupolaSpeed = 35f;
		const float ElevationSpeed = 24f;   // inferred, it has to keep up with the ТКН-4С's slew
		const int BeltB32PerTracer = 3;

		static readonly FieldInfo SlotReparent = Reflect.Field(typeof(CameraSlot), "_reparentTarget");
		static readonly FieldInfo ArmorName = Reflect.Field(typeof(UniformArmor), "_name");

		internal class Station : RemoteMg.Station
		{
			internal UsableOptic Pzu7;
			internal UsableOptic Tkn4s;
			internal CommanderSight.Avtomat Auto;
			internal override IEnumerable<CameraSlot> Sights => KordSlots(Pzu7?.slot, Tkn4s?.slot);
		}

		internal class Zones
		{
			internal DC CupolaDrive, ElevationDrive, Sight, Gun, Tkn4s;
		}

		static readonly Vector3 GunZoneCentre = new Vector3(0.010f, 0.012f, 0.685f), GunZoneSize = new Vector3(0.112f, 0.176f, 1.562f);
		const float CupolaDriveFrontZ = -1.36f;
		const float CupolaDriveOuterX = 0.755f;
		static readonly Vector2 CupolaDriveY = new Vector2(1.505f, 1.905f);

		internal static void Apply(Vehicle vehicle)
		{
			var root = vehicle.transform;
			RemoteMg.SetLogTag("T-90A Kord (ZPU): ");
			var d = Donor.Get("T-90A");
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
			KordBelt.Belt beltVis = null;
			if (cupVis != null)
				try { beltVis = KordBelt.Fit(cupVis, cradle, BeltSettings); }
				catch (System.Exception e) { MelonLogger.Error("T-90A Kord belt failed: " + e); }

			// Before anything wakes: the drives, gun and sight pick their hit zones up in Awake.
			var zones = FitZones(vehicle, d, cupolaBone, turret, cradle, holder.transform);

			var gun = RemoteMg.CloneGun(d, holder.transform, new RemoteMg.GunSettings { Name = Name, DamageName = "6P49 Kord", BeltRounds = BeltRounds, Belts = Belts, RoundsPerMinute = RoundsPerMinute, B32PerTracer = BeltB32PerTracer }, "T-90A");
			if (gun == null) { Object.Destroy(holder); return; }
			var gunGo = gun.Go; var weapon = gun.Weapon; var feed = gun.Feed; var b32 = gun.B32;
			int removed = gun.Removed, scrubbed = gun.Scrubbed;
			float cycle = gun.Cycle;
			weapon.BarrelHitZone = zones.Gun;

			// Last under the root, GetComponentInChildren<FireControlSystem>() elsewhere has to find the gunner's.
			var (fcsGo, fcs, opticGo, fcsRemoved, fcsScrubbed) = RemoteMg.CloneFcs(d, holder.transform, "T90A Kord fire control");
			removed += fcsRemoved; scrubbed += fcsScrubbed;
			var reference = new GameObject("T90A Kord aim reference").transform;
			reference.SetParent(cupolaBone, false);
			reference.SetPositionAndRotation(W(PzuHead), cupolaBone.rotation);
			fcs.ReferenceTransform = reference;
			fcs.LinkedWeaponSystems = new[] { weapon };

			var scripts = new GameObject("---T90A ZPU SCRIPTS---");
			scripts.transform.SetParent(root, false);
			var cupola = RemoteMg.Platform(scripts.transform, "ZPU cupola drive", d.CupolaDrive, cupolaBone, EnumHelpers.Axis3.Y, Traverse, CupolaSpeed, zones.CupolaDrive);
			var elev = RemoteMg.Platform(scripts.transform, "ZPU elevation drive", d.ElevationDrive, cradle, EnumHelpers.Axis3.X, Elevation, ElevationSpeed, zones.ElevationDrive);
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
			// Without a fallback the camera's left floating at the dead sight.
			slot.SlotToUseOnDestroy = root.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => s.name == "commander head");
			if (optic.reticleMesh != null && optic.reticleMesh.reticleSO != null)
			{
				if (_pzuReticle == null) _pzuReticle = Pzu7Reticle(optic.reticleMesh.reticleSO, b32);
				optic.reticleMesh.reticleSO = _pzuReticle;
			}

			UsableOptic tkn = null, tknNight = null;
			Transform tknHead = null;
			if (d.CommanderOptic != null)
			{
				tknHead = new GameObject("T90A TKN-4S head").transform;
				tknHead.SetParent(cupolaBone, false);
				tknHead.SetPositionAndRotation(W(CommanderSight.Head), cupolaBone.rotation);
				tkn = RemoteMg.CloneOptic(d, d.CommanderOptic, holder.transform, ref removed, ref scrubbed);
				tknNight = RemoteMg.CloneOptic(d, d.CommanderOptic, holder.transform, ref removed, ref scrubbed);
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
			// Night channel first, the day slot marks its linked night sight when it wakes.
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
			if (beltVis != null)
			{
				beltVis.Attach(weapon, cycle);
				if (T90AMod.KordCases.Value) KordBrass.Attach(vehicle, weapon, cycle, cradle, cradle.InverseTransformPoint(W(CaseChute)), CaseChuteDir, beltVis,
					new[] { cupolaBone, turret.Find("GUN"), turret, turret.parent });
			}

			var info = new WeaponSystemInfo { Name = Name, Role = WeaponSystemRole.RoofGun, Weapon = weapon, FCS = fcs, PreAimWeapon = WeaponSystemRole.MainGun, PreAimAngleThreshold = 3f };
			var gunner = new[] { wm.Weapons[0].Weapon, wm.Weapons[1].Weapon };
			RemoteMg.Register(vehicle, wm, info, new[] { cupola, elev }, new Component[] { cupola, elev, weapon, feed, fcs, slot, tkn?.slot, tknNight?.slot }, KordSlots(slot, tkn?.slot));
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

		internal static void LateSetup(Vehicle vehicle) => RemoteMg.LateSetup(vehicle);

		static IEnumerable<CameraSlot> KordSlots(CameraSlot pzu7, CameraSlot tkn) => new[] { tkn, pzu7 }.Where(s => s != null);
		static ReticleSO _pzuReticle;

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
				RemoteMg.Scrub(go, d.Root);
				return go;
			}
			DC Wake(GameObject go, Transform bone, Transform group, Matrix4x4 inBone, string dcName)
			{
				Followers.PlaceOnBone(go.transform, bone, group);
				var m = Matrix4x4.TRS(go.transform.localPosition, go.transform.localRotation, Vector3.one) * inBone;
				go.transform.localPosition = m.GetColumn(3);
				go.transform.localRotation = m.rotation;
				go.transform.localScale = m.lossyScale;
				// Box, not the donor's mesh collider: two are non-convex and the sight's a thin shell a round can thread.
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

			Matrix4x4 Within(Matrix4x4 inBone, Transform bone, Mesh mesh, int pa, float lo, float hi)
			{
				if (mesh == null) return inBone;
				var toPrefab = (root.worldToLocalMatrix * bone.localToWorldMatrix) * inBone;
				Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
				var b = mesh.bounds; int k = 0; float best = -1f;
				for (int i = 0; i < 3; i++) { float a = Mathf.Abs(toPrefab.MultiplyVector(Axis(i)).normalized[pa]); if (a > best) { best = a; k = i; } }
				var dir = Axis(k) * Mathf.Sign(toPrefab.MultiplyVector(Axis(k))[pa]);
				var low = b.center - dir * b.extents[k];
				float p0 = toPrefab.MultiplyPoint(low)[pa], p1 = toPrefab.MultiplyPoint(b.center + dir * b.extents[k])[pa];
				float n0 = Mathf.Max(p0, lo), n1 = Mathf.Min(p1, hi);
				if ((n0 <= p0 && n1 >= p1) || n1 <= n0) return inBone;
				float u0 = (n0 - p0) / (p1 - p0), u1 = (n1 - p0) / (p1 - p0);
				var scale = Vector3.one; scale[k] = u1 - u0;
				return inBone * Matrix4x4.Translate(low + dir * (2f * b.extents[k] * u0)) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-low);
			}

			var drive = d.CupolaDriveZone != null ? d.CupolaDriveZone.GetComponent<MeshFilter>()?.sharedMesh : null;
			if (d.CupolaDriveZone != null && turAar != null) z.CupolaDrive = Wake(Clone(d.CupolaDriveZone, "ZPU cupola drive"), turret, turAar,
				Within(Within(Within(Moved(d.CupolaDriveZone, turret), turret, drive, 2, float.NegativeInfinity, CupolaDriveFrontZ),
					turret, drive, 0, float.NegativeInfinity, CupolaDriveOuterX), turret, drive, 1, CupolaDriveY.x, CupolaDriveY.y), "commander's cupola drive");
			if (d.ElevationDriveZone != null && cupAar != null) z.ElevationDrive = Wake(Clone(d.ElevationDriveZone, "ZPU elevation drive"), cupolaBone, cupAar, Moved(d.ElevationDriveZone, cupolaBone), "ZPU elevation drive");
			if (d.SightZone != null && cupAar != null) z.Sight = Wake(Clone(d.SightZone, "PZU-7 sight"), cupolaBone, cupAar, Moved(d.SightZone, cupolaBone), "PZU-7 sight");
			if (d.SightZone != null && cupAar != null && d.CommanderOptic != null)
			{
				var go = Clone(d.SightZone, "TKN-4S sight");
				go.GetComponent<MeshFilter>().sharedMesh = RemoteMg.BoxMesh("T90A hit zone box");
				var inBone = (root.worldToLocalMatrix * cupolaBone.localToWorldMatrix).inverse * Matrix4x4.TRS(CommanderSight.ZoneCentre, Quaternion.identity, CommanderSight.ZoneSize);
				z.Tkn4s = Wake(go, cupolaBone, cupAar, inBone, "TKN-4S commander's sight");
			}

			if (d.ElevationDriveZone != null)
			{
				// Its own LateFollow group. A collider left under the unit joins its rigidbody.
				var aar = Followers.ColliderGroup(root, "T90A KORD COLLIDERS", cradle, "AAR").Find("AAR");
				var go = Clone(d.ElevationDriveZone, "Kord barrel");
				go.GetComponent<MeshFilter>().sharedMesh = RemoteMg.BoxMesh("T90A hit zone box");
				foreach (var v in go.GetComponentsInChildren<AarVisual>(true)) Object.DestroyImmediate(v);
				foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
				z.Gun = Wake(go, cradle, aar, Matrix4x4.TRS(GunZoneCentre, Quaternion.identity, GunZoneSize), "6P49 Kord");
			}
			return z;
		}

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
	}
}
