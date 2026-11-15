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

	// The remote Kord on the cupola, with the PZU-7 and TKN-4S as its views.
	internal static partial class Commander
	{
		// Prefab space (x right, y up, z forward), the War Thunder model mirrored in x. Cupola axis: where that model's
		// cupola turns (x 0.602, z -1.033), at the height of the T-72M1's CUPOLA bone.
		internal static readonly Vector3 CupolaAxis = new Vector3(0.602f, 2.054f, -1.033f);
		internal static readonly Vector3 Trunnion = new Vector3(0.583f, 2.526f, -0.593f);   // where the model's Kord tilts
		static readonly Vector3 WtMuzzle = new Vector3(0.582f, 2.510f, 0.873f);   // the model's muzzle flash point
		// PZU-7 eye: the top front of the model's sight head. On the model's rod pivot, 3 cm left of the Kord's cradle, the
		// cradle filled half the view.
		static readonly Vector3 PzuHead = new Vector3(0.187f, 2.280f, -0.850f);
		// The gun's nose-down level from the rig map; none when the rig map has no axes, as the turret is not moved then.
		static float GunLevelDeg => T90AMod.Rig != null && T90AMod.Rig.HasAxes ? T90AMod.Rig.PitchDeg : 0f;
		internal static Vector3 Muzzle => Trunnion + Quaternion.AngleAxis(-GunLevelDeg, Vector3.right) * (WtMuzzle - Trunnion);
		// Spent cases leave forward through the chute on the right of the receiver's front (Vimpel's Kord page). Its mouth
		// is inferred: just ahead of War Thunder's receiver, inside its right wall, 1.5 cm under the bore; the direction
		// (cradle frame) is forward, a little out to the right and down, also inferred.
		static readonly Vector3 WtCaseChute = new Vector3(0.612f, 2.505f, -0.090f);
		internal static Vector3 CaseChute
			=> Trunnion + Quaternion.AngleAxis(-GunLevelDeg, Vector3.right) * (WtCaseChute - Trunnion);
		static readonly Vector3 CaseChuteDir = new Vector3(0.10f, -0.08f, 1f);
		internal static readonly KordBelt.Settings BeltSettings = new KordBelt.Settings
		{
			Prefix = "T90A_",
			MeshName = "T90A Kord",
			BeltMaterial = "t90a_mg_6p49_kord_ammo",
			SpareRounds = 24,   // enough belt for +70 deg elevation: the curve grows from about 0.27 m to 0.7 m at a 2.7 cm pitch
			MaxCases = BeltRounds * Belts,
		};

		internal const string Name = "12.7mm Machine Gun 6P49 Kord";
		// 300 rounds in 2 belts of 150 (btvt.narod.ru, «Основной боевой танк Т-90А»).
		internal const int BeltRounds = 150;
		internal const int Belts = 2;
		// The plain 6P49's cyclic rate: the middle of the 600-650 the Russian sources give (the T-90M's 6P49MT has two governed
		// rates, 200 and 100).
		internal const float RoundsPerMinute = 625f;
		// The cupola's automatic sector: 45 deg left and 60 right of the gun (Suvorov, «Т-90»).
		static readonly Vector2 Traverse = new Vector2(-45f, 60f);
		static readonly Vector2 Elevation = new Vector2(-5f, 70f);   // btvt.narod.ru, «Основной боевой танк Т-90А»
		// The sight turns the cupola at 0-35 deg/s absolute (the ПНК-4С manual).
		const float CupolaSpeed = 35f;
		// A follow-up drive on the ТКН-4С's stabilised line of sight (ПНК-4С manual), so at least its fastest slew,
		// 16-24 deg/s (Suvorov, «Т-90»). 24 is inferred: it has to keep up with the ТКН-4С's slew.
		const float ElevationSpeed = 24f;
		// «Б-32, БЗТ-44 ... в соотношении 3:1» (Vimpel's Kord page).
		const int BeltB32PerTracer = 3;

		static readonly FieldInfo SlotReparent = Reflect.Field(typeof(CameraSlot), "_reparentTarget");
		static readonly FieldInfo ArmorName = Reflect.Field(typeof(UniformArmor), "_name");

		internal partial class Station : RemoteMg.Station
		{
			internal UsableOptic Pzu7;
			internal UsableOptic Tkn4s;
			internal CommanderSight.Avtomat Auto;
			internal override IEnumerable<CameraSlot> Sights => KordSlots(Pzu7?.slot, Tkn4s?.slot);
		}

		internal class Zones
		{
			internal DC CupolaDrive;
			internal DC ElevationDrive;
			internal DC Sight;
			internal DC Gun;
			internal DC Tkn4s;
		}

		// The Kord barrel's hit zone in the cradle frame (origin at the trunnion, level): the War Thunder model's barrel damage
		// part, levelled 1.2 deg about the trunnion like the Kord itself.
		static readonly Vector3 GunZoneCentre = new Vector3(0.010f, 0.012f, 0.685f);
		static readonly Vector3 GunZoneSize = new Vector3(0.112f, 0.176f, 1.562f);
		// The cupola drive piece's front (prefab z): 2 cm behind the commander's torso as the interior map puts him.
		const float CupolaDriveFrontZ = -1.36f;
		// Its outer side and height (prefab, inferred): the turret's right liner stands at x 0.770 from y 1.505 to 1.905 over the
		// piece's depth, measured from the turret volume's walls; the piece sits 1.5 cm inside it.
		const float CupolaDriveOuterX = 0.755f;
		static readonly Vector2 CupolaDriveY = new Vector2(1.505f, 1.905f);

		// What the build steps share. The new parts wait inactive under Holder and are woken together at the end.
		sealed class Fit
		{
			internal Vehicle Vehicle;
			internal Donor Donor;
			internal WeaponsManager Weapons;
			internal Transform Root;
			internal Transform Turret;
			internal Transform CupolaBone;
			internal Transform Holder;
			internal Transform Cradle;
			internal Transform PzuHead;
			internal Transform Scripts;
			internal KordBelt.Belt Belt;
			internal Zones Zones;
			internal RemoteMg.Gun Gun;
			internal GameObject FcsGo;
			internal GameObject OpticGo;
			internal FireControlSystem Fcs;
			internal AimablePlatform Cupola;
			internal AimablePlatform Elev;
			internal UsableOptic Pzu7;
			internal UsableOptic Tkn;
			internal UsableOptic TknNight;
			internal CameraSlot Pzu7Slot;
			internal Transform TknHead;

			internal Vector3 World(Vector3 prefab) => Root.TransformPoint(prefab);
		}

		internal static void Apply(Vehicle vehicle)
		{
			DevFitting(vehicle);
			var fit = Begin(vehicle);
			if (fit == null) return;
			FitCradle(fit);
			// Before anything wakes: the drives, gun and sight pick their hit zones up in Awake.
			fit.Zones = FitZones(vehicle, fit.Donor, fit.CupolaBone, fit.Turret, fit.Cradle, fit.Holder);
			if (!CloneGun(fit)) return;
			CloneFireControl(fit);
			FitDrives(fit);
			FitPzu7(fit);
			FitTkn4s(fit);
			WakeParts(fit);
			Register(fit);
			DevFitted(fit.Scripts.GetComponent<Station>(), fit);
		}

		static Fit Begin(Vehicle vehicle)
		{
			var donor = Donor.Get(T90AMod.FriendlyName);
			if (donor == null) return null;
			var root = vehicle.transform;
			var turret = root.Find("---MESH---/HULL/TURRET");
			var cupolaBone = turret?.Find("CUPOLA");
			var weapons = vehicle.GetComponent<WeaponsManager>();
			if (cupolaBone == null || weapons == null || weapons.Weapons == null || weapons.Weapons.Length < 2)
			{
				DonorParts.WarnOnce(
					"Kord",
					"T-90A Kord (ZPU): TURRET/CUPOLA bone or the main gun / coax rows not found; no Kord fitted.");
				return null;
			}
			return new Fit
			{
				Vehicle = vehicle,
				Donor = donor,
				Weapons = weapons,
				Root = root,
				Turret = turret,
				CupolaBone = cupolaBone,
			};
		}

		// The cradle the gun sits in, the PZU-7's head, and the cupola and Kord meshes moved onto them.
		static void FitCradle(Fit fit)
		{
			var holder = new GameObject("T90A ZPU (setup)");
			holder.SetActive(false);
			holder.transform.SetParent(fit.Root, false);
			fit.Holder = holder.transform;

			var cradle = new GameObject("T90A Kord cradle").transform;
			cradle.SetParent(fit.CupolaBone, false);
			cradle.SetPositionAndRotation(fit.World(Trunnion), fit.CupolaBone.rotation);
			fit.Cradle = cradle;
			var pzuHead = new GameObject("T90A PZU-7 head").transform;
			pzuHead.SetParent(fit.CupolaBone, false);
			pzuHead.SetPositionAndRotation(fit.World(PzuHead), fit.CupolaBone.rotation);
			pzuHead.gameObject.AddComponent<RodLink>().Source = cradle;
			fit.PzuHead = pzuHead;

			var cupVis = fit.Turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(p => p.name == "T90A_CUPOLA");
			var kordVis = fit.Turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(p => p.name == "T90A_KORD");
			cupVis?.SetParent(fit.CupolaBone, true);
			kordVis?.SetParent(cradle, true);
			if (cupVis == null) return;
			try
			{
				fit.Belt = KordBelt.Fit(cupVis, cradle, BeltSettings);
			}
			catch (System.Exception exception)
			{
				MelonLogger.Error($"{T90AMod.FriendlyName} Kord belt failed: " + exception);
			}
		}

		// The Kord cloned from the donor. False when that fails: the holder goes and no station is fitted.
		static bool CloneGun(Fit fit)
		{
			var gun = RemoteMg.CloneGun(
				fit.Donor,
				fit.Holder,
				new RemoteMg.GunSettings
				{
					Name = Name,
					DamageName = "6P49 Kord",
					BeltRounds = BeltRounds,
					Belts = Belts,
					RoundsPerMinute = RoundsPerMinute,
					B32PerTracer = BeltB32PerTracer
				},
				T90AMod.FriendlyName);
			if (gun == null)
			{
				Object.Destroy(fit.Holder.gameObject);
				return false;
			}
			fit.Gun = gun;
			gun.Weapon.BarrelHitZone = fit.Zones.Gun;
			return true;
		}

		static void CloneFireControl(Fit fit)
		{
			// Last under the root: GetComponentInChildren<FireControlSystem>() elsewhere has to find the gunner's first.
			var (fcsGo, fcs, opticGo, _, _) = RemoteMg.CloneFcs(fit.Donor, fit.Holder, "T90A Kord fire control");
			var reference = new GameObject("T90A Kord aim reference").transform;
			reference.SetParent(fit.CupolaBone, false);
			reference.SetPositionAndRotation(fit.World(PzuHead), fit.CupolaBone.rotation);
			fcs.ReferenceTransform = reference;
			fcs.LinkedWeaponSystems = new[] { fit.Gun.Weapon };
			fit.FcsGo = fcsGo;
			fit.Fcs = fcs;
			fit.OpticGo = opticGo;
		}

		// The cupola and elevation drives, and the fire control's mounts.
		static void FitDrives(Fit fit)
		{
			var scripts = new GameObject("---T90A ZPU SCRIPTS---");
			scripts.transform.SetParent(fit.Root, false);
			fit.Scripts = scripts.transform;
			fit.Cupola = RemoteMg.Platform(
				fit.Scripts,
				"ZPU cupola drive",
				fit.Donor.CupolaDrive,
				fit.CupolaBone,
				EnumHelpers.Axis3.Y,
				Traverse,
				CupolaSpeed,
				fit.Zones.CupolaDrive);
			fit.Elev = RemoteMg.Platform(
				fit.Scripts,
				"ZPU elevation drive",
				fit.Donor.ElevationDrive,
				fit.Cradle,
				EnumHelpers.Axis3.X,
				Elevation,
				ElevationSpeed,
				fit.Zones.ElevationDrive);
			fit.Fcs.Mounts = new[] { fit.Cupola, fit.Elev };
		}

		// The PZU-7: the donor's optic on the head, tied to the Kord's fire control and given the B32 reticle.
		static void FitPzu7(Fit fit)
		{
			var optic = fit.OpticGo.GetComponent<UsableOptic>();
			var slot = optic.slot;
			fit.OpticGo.name = "PZU-7 sight";
			SlotReparent.SetValue(slot, fit.PzuHead);
			optic.FCS = fit.Fcs;
			optic.LevelTransform = Conversion.FindDeep(fit.Root, "---TURRET SCRIPTS---") ?? fit.Turret;
			optic.RegisteredAmmoTypes = new[] { fit.Gun.B32 };
			slot.ExclusiveWeapons = new[] { fit.Gun.Weapon };
			slot.HitZone = fit.Zones.Sight;
			// Without a fallback the camera's left floating at the dead sight.
			slot.SlotToUseOnDestroy = fit.Root
				.GetComponentsInChildren<CameraSlot>(true)
				.FirstOrDefault(c => c.name == "commander head");
			if (optic.reticleMesh != null && optic.reticleMesh.reticleSO != null)
			{
				if (_pzuReticle == null) _pzuReticle = Pzu7Reticle(optic.reticleMesh.reticleSO, fit.Gun.B32);
				optic.reticleMesh.reticleSO = _pzuReticle;
			}
			fit.Pzu7 = optic;
			fit.Pzu7Slot = slot;
		}

		// The TKN-4S: day and night optics cloned from the donor's commander sight, with the ammo reticle and readout.
		static void FitTkn4s(Fit fit)
		{
			var donor = fit.Donor;
			if (donor.CommanderOptic == null) return;
			var head = new GameObject("T90A TKN-4S head").transform;
			head.SetParent(fit.CupolaBone, false);
			head.SetPositionAndRotation(fit.World(CommanderSight.Head), fit.CupolaBone.rotation);
			fit.TknHead = head;
			// CloneOptic counts into these; nobody reads the totals.
			int removed = 0;
			int scrubbed = 0;
			fit.Tkn = RemoteMg.CloneOptic(donor, donor.CommanderOptic, fit.Holder, ref removed, ref scrubbed);
			fit.TknNight = RemoteMg.CloneOptic(donor, donor.CommanderOptic, fit.Holder, ref removed, ref scrubbed);
			var gunner = fit.Weapons.Weapons[0];
			var fontHost = GunnerDay(fit.Vehicle, gunner.FCS)?.reticleMesh?.reticleSO;
			var reticle = Reticles.Build("tkn4s_day.jsonc", fit.Tkn.reticleMesh.reticleSO, fontHost, TknRounds(fit, fontHost));
			var nightReticle = Reticles.Build("tkn4s_night.jsonc", null, fontHost, null);
			CommanderSight.Configure(
				fit.Tkn,
				fit.TknNight,
				donor.NightLook,
				head,
				fit.Fcs,
				fit.Gun.Weapon,
				fit.Gun.B32,
				fit.Pzu7.LevelTransform,
				fit.Pzu7Slot,
				fit.Zones.Tkn4s,
				reticle,
				nightReticle);
			Tkn4sReadout.Build(fit.Tkn, gunner.Weapon, gunner.FCS);
		}

		// The rounds the TKN-4S reticle draws its ranges for: the Kord's belt and the gunner's three.
		static Dictionary<string, AmmoCodexScriptable> TknRounds(Fit fit, ReticleSO fontHost)
		{
			var clips = fit.Vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
			AmmoCodexScriptable Round(int i, string name, string token) =>
				(clips != null && clips.Length > i && clips[i] != null ? clips[i].ClipType.MinimalPattern[0] : null)
					?? (fontHost != null ? Sight1G46.FindAmmo(name, fontHost, token) : null);
			return new Dictionary<string, AmmoCodexScriptable>
			{
				{ "b32", fit.Gun.B32 },
				{ "apfsds", Round(0, Sight1G46.ApfsdsAmmo, "3BM") },
				{ "he", Round(2, Sight1G46.HeAmmo, "3OF") },
				{ "heat", Round(1, Sight1G46.HeatAmmo, "3BK") },
			};
		}

		// Wakes everything that was built inactive, in the order the game needs.
		static void WakeParts(Fit fit)
		{
			var cradle = fit.Cradle;
			var gunGo = fit.Gun.Go;
			gunGo.transform.SetParent(cradle, false);
			gunGo.transform.SetPositionAndRotation(fit.World(Muzzle), cradle.rotation);
			gunGo.SetActive(true);
			fit.FcsGo.transform.SetParent(fit.Scripts, false);
			fit.FcsGo.SetActive(true);
			Mount(fit.OpticGo.transform, fit.PzuHead);
			fit.OpticGo.SetActive(true);
			// Night channel first because the day slot marks its linked night sight when it wakes.
			if (fit.Tkn != null)
			{
				foreach (var tknOptic in new[] { fit.TknNight, fit.Tkn })
				{
					Mount(tknOptic.transform, fit.TknHead);
					tknOptic.gameObject.SetActive(true);
				}
			}
			Object.Destroy(fit.Holder.gameObject);
			var weapon = fit.Gun.Weapon;
			var cycle = fit.Gun.Cycle;
			weapon.SetCycleTime(cycle);
			if (fit.Belt == null) return;
			fit.Belt.Attach(weapon, cycle);
			if (!T90AMod.KordCases.Value) return;
			var turret = fit.Turret;
			KordBrass.Attach(
				fit.Vehicle,
				weapon,
				cycle,
				cradle,
				cradle.InverseTransformPoint(fit.World(CaseChute)),
				CaseChuteDir,
				fit.Belt,
				new[] { fit.CupolaBone, turret.Find("GUN"), turret, turret.parent });
		}

		static void Mount(Transform part, Transform parent)
		{
			part.SetParent(parent, false);
			part.localPosition = Vector3.zero;
			part.localRotation = Quaternion.identity;
		}

		// The Kord joins the weapon list, the gunner's sights are bound to the main gun and coax only, and the
		// commander's station behaviours go on.
		static void Register(Fit fit)
		{
			var weapons = fit.Weapons;
			var weapon = fit.Gun.Weapon;
			var info = new WeaponSystemInfo
			{
				Name = Name,
				Role = WeaponSystemRole.RoofGun,
				Weapon = weapon,
				FCS = fit.Fcs,
				PreAimWeapon = WeaponSystemRole.MainGun,
				PreAimAngleThreshold = 3f
			};
			var platforms = new[] { fit.Cupola, fit.Elev };
			var tknSlot = fit.Tkn?.slot;
			RemoteMg.Register(
				fit.Vehicle,
				weapons,
				info,
				platforms,
				new Component[] { fit.Cupola, fit.Elev, weapon, fit.Gun.Feed, fit.Fcs, fit.Pzu7Slot, tknSlot, fit.TknNight?.slot },
				KordSlots(fit.Pzu7Slot, tknSlot));
			BindGunnerSights(fit, new[] { weapons.Weapons[0].Weapon, weapons.Weapons[1].Weapon });

			var station = fit.Scripts.gameObject.AddComponent<Station>();
			station.Info = info;
			station.Pzu7 = fit.Pzu7;
			station.Tkn4s = fit.Tkn;
			if (fit.Tkn == null) return;
			station.Auto = fit.Scripts.gameObject.AddComponent<CommanderSight.Avtomat>();
			station.Auto.Init(
				fit.Fcs,
				fit.Cupola,
				fit.Elev,
				fit.Turret,
				fit.Tkn,
				fit.TknNight,
				fit.Pzu7Slot,
				fit.Gun.B32.AmmoType,
				Traverse);
		}

		// The gunner's day sight and its night sight only take the main gun and the coax, not the Kord.
		static void BindGunnerSights(Fit fit, WeaponSystem[] gunnerWeapons)
		{
			var fcs = fit.Weapons.Weapons[0].FCS;
			// The fire control's own day optic, else the first other optic on it when that one has no camera slot.
			var daySlot = (fcs != null ? Sights.DayOptic(fcs)?.slot : null) ?? FirstDayOptic(fit.Vehicle, fcs)?.slot;
			if (daySlot == null) return;
			daySlot.ExclusiveWeapons = gunnerWeapons;
			if (daySlot.LinkedNightSight != null) daySlot.LinkedNightSight.ExclusiveWeapons = gunnerWeapons;
		}

		// The gunner's day optic: the fire control's own, else the first optic on it that is not a night sight.
		static UsableOptic GunnerDay(Vehicle vehicle, FireControlSystem fcs) =>
			(fcs != null ? Sights.DayOptic(fcs) : null) ?? FirstDayOptic(vehicle, fcs);

		static UsableOptic FirstDayOptic(Vehicle vehicle, FireControlSystem fcs) =>
			vehicle
				.GetComponentsInChildren<UsableOptic>(true)
				.FirstOrDefault(f => f.FCS == fcs && f.slot != null && !f.slot.IsLinkedNightSight);

		internal static void LateSetup(Vehicle vehicle) => RemoteMg.LateSetup(vehicle);

		static IEnumerable<CameraSlot> KordSlots(CameraSlot pzu7, CameraSlot tkn) => new[] { tkn, pzu7 }.Where(s => s != null);
		static ReticleSO _pzuReticle;

		static ReticleSO Pzu7Reticle(ReticleSO pzu5, AmmoCodexScriptable b32)
		{
			var reticle = Object.Instantiate(pzu5);
			reticle.name = "PZU-7 (T-90A, from " + pzu5.name + ")";
			var stack = new Stack<ReticleTree.TransformElement>(reticle.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var element = stack.Pop();
				if (element is ReticleTree.VerticalBallistic verticalBallistic
					&& verticalBallistic.projectile != null
					&& verticalBallistic.projectile != b32)
				{
					verticalBallistic.projectile = b32;
					verticalBallistic.UpdateBC();
				}
				if (element is ReticleTree.GroupBase container) foreach (var child in container.elements) stack.Push(child);
			}
			return reticle;
		}

		[DefaultExecutionOrder(10)]
		internal class RodLink : MonoBehaviour
		{
			internal Transform Source;
			void LateUpdate()
			{
				if (Source != null) transform.localRotation = Source.localRotation;
			}
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevFitting(Vehicle vehicle);
		static partial void DevFitted(Station station, Fit fit);
	}
}
