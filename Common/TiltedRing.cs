using System.Collections.Generic;
using System.Linq;
using GHPC;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// For a ring that isn't level (a tilted seat). AimablePlatform turns about the parent's up and wipes other
	// tilt, so the tilted turret rode up to 4 cm off its seat. A tilted parent would break every HULL/TURRET path,
	// which is why there's a drive. Side effect: AzimuthLock measures off the drive and the gun loads up to Tilt low.
	internal static class TiltedRing
	{
		internal sealed class Link : MonoBehaviour
		{
			public AimablePlatform Platform;
			public Unit Unit;
			public Transform Hull, Turret, Seat, Drive;
			public Quaternion Tilt;
			public string Tag;

			internal void Sync()
			{
				if (Turret == null || Drive == null || Turret.parent != Hull) return;
				Turret.localRotation = Tilt * Drive.localRotation * Quaternion.Inverse(Tilt);
			}

			// For poses set straight on the drive, like the AAR replay's, which the postfix never sees.
			void LateUpdate() => Sync();
		}

		static readonly Dictionary<AimablePlatform, Link> Links = new Dictionary<AimablePlatform, Link>();
		static readonly AccessTools.FieldRef<AimablePlatform, Quaternion> Orig =
			Reflect.FieldRefAccess<AimablePlatform, Quaternion>("_originalLocalRotation");
		static readonly AccessTools.FieldRef<AimablePlatform, Vector3> Stowed =
			Reflect.FieldRefAccess<AimablePlatform, Vector3>("_stowedVectorInParentSpace");

		internal static bool Apply(GHPC.Vehicle.Vehicle vehicle, Quaternion tilt, string tag = "tilted ring")
		{
			if (Orig == null || Stowed == null) return false;
			var root = vehicle.transform;
			var hull = RigBones.Hull(root);
			var turret = hull != null ? RigBones.Turret(root) : null;
			var plat = turret == null
				? null
				: root.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p.Transform == turret);
			if (plat == null)
			{
				MelonLogger.Warning($"{tag}: no traverse platform on {RigBones.TurretName(root)}; turret turns about the vertical.");
				return false;
			}
			if (Quaternion.Angle(Orig(plat), Quaternion.identity) > 0.01f)
			{
				MelonLogger.Warning($"{tag}: the turret's rest rotation isn't the hull's ({Orig(plat).eulerAngles}); turret turns about the vertical.");
				return false;
			}
			UnityKeys.Prune(Links);

			var seat = new GameObject("TURRET SEAT (tilted ring)").transform;
			seat.SetParent(hull, false);
			seat.localPosition = turret.localPosition;
			seat.localRotation = tilt;
			var drive = new GameObject("TURRET DRIVE (tilted ring)").transform;
			drive.SetParent(seat, false);
			drive.localRotation = Quaternion.Euler(0f, turret.localEulerAngles.y, 0f);
			var link = drive.gameObject.AddComponent<Link>();
			link.Platform = plat;
			link.Unit = vehicle;
			link.Hull = hull;
			link.Turret = turret;
			link.Seat = seat;
			link.Drive = drive;
			link.Tilt = tilt;
			link.Tag = tag;

			var stowed = Stowed(plat);
			// Rest and stowed vector are in the bone's parent space, the seat from here on.
			plat.Transform = drive;
			Orig(plat) = Quaternion.identity;
			Stowed(plat) = Quaternion.Inverse(tilt) * stowed;
			plat.LocalRotation = drive.localRotation;
			Links[plat] = link;
			link.Sync();

			return true;
		}

		internal static void SyncNow(Transform root)
		{
			foreach (var link in Links.Values) if (link != null && link.Turret != null && link.Turret.IsChildOf(root)) link.Sync();
		}

		[HarmonyPatch(typeof(AimablePlatform), "MoveTransformToMatchVirtualRotation")]
		static class FollowDrive
		{
			static void Postfix(AimablePlatform __instance)
			{
				if (Links.Count > 0 && Links.TryGetValue(__instance, out var link) && link != null) link.Sync();
			}
		}

		// The AAR snapshots the drive now. TURRET too, or a replay can't put a blown-off turret back on the hull.
		[HarmonyPatch(typeof(GHPC.Weapons.ShotInfo), nameof(GHPC.Weapons.ShotInfo.PopulateUnitPoses))]
		static class AarPose
		{
			static void Postfix(GHPC.Weapons.ShotInfo __instance)
			{
				if (Links.Count == 0) return;
				foreach (var pose in __instance.UnitPoses)
				{
					if (pose.PosedTransforms == null) continue;
					foreach (var link in Links.Values)
						if (link != null && link.Turret != null && (object)link.Unit == pose.Unit)
							pose.PosedTransforms.Add(new GHPC.Weapons.ShotInfo.PosRot
							{
								Trans = link.Turret,
								Pos = link.Turret.localPosition,
								Rot = link.Turret.localRotation,
								Parent = link.Turret.parent
							});
				}
			}
		}
	}
}
