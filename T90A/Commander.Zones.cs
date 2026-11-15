using System.Linq;
using GHPC;
using GHPC.Vehicle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	using DC = GHPC.Equipment.DestructibleComponent;
	using Donor = T90A.Common.RemoteMg.Donor;

	// Where the Kord's cupola, mount and gun sit in the turret, as the zones the shot checks see.
	internal static partial class Commander
	{
		static Zones FitZones(
			Vehicle vehicle,
			Donor d,
			Transform cupolaBone,
			Transform turret,
			Transform cradle,
			Transform holder)
			=> new ZoneFitter(vehicle, d, holder).Fit(cupolaBone, turret, cradle);

		// Copies the donor's hit zones onto the T-90A's cupola, cradle and turret, moved by the cupola's offset.
		sealed class ZoneFitter
		{
			readonly Vehicle _vehicle;
			readonly Transform _root;
			readonly Donor _d;
			readonly Transform _holder;
			readonly Vector3 _shift;

			internal ZoneFitter(Vehicle vehicle, Donor d, Transform holder)
			{
				_vehicle = vehicle;
				_root = vehicle.transform;
				_d = d;
				_holder = holder;
				_shift = CupolaAxis - d.Axis;
				_shift.y = 0f;
			}

			internal Zones Fit(Transform cupolaBone, Transform turret, Transform cradle)
			{
				var zones = new Zones();
				var cupAar = Aar(cupolaBone, "CUPOLA COLLIDERS");
				var turAar = Aar(turret, "TURRET COLLIDERS");
				FitCupolaDrive(zones, turret, turAar);
				FitElevationDrive(zones, cupolaBone, cupAar);
				FitSight(zones, cupolaBone, cupAar);
				FitTkn4sSight(zones, cupolaBone, cupAar);
				FitGun(zones, cradle);
				return zones;
			}

			Transform Aar(Transform bone, string token)
			{
				var follow = Followers
					.On(_root, bone)
					.FirstOrDefault(c => c != null && c.name.Contains(token));
				return follow == null ? null : (follow.transform.Find("AAR") ?? follow.transform);
			}

			void FitCupolaDrive(Zones zones, Transform turret, Transform turAar)
			{
				if (_d.CupolaDriveZone == null || turAar == null) return;
				var drive = _d.CupolaDriveZone.GetComponent<MeshFilter>()?.sharedMesh;
				var inBone = Moved(_d.CupolaDriveZone, turret);
				inBone = Within(inBone, turret, drive, 2, float.NegativeInfinity, CupolaDriveFrontZ);
				inBone = Within(inBone, turret, drive, 0, float.NegativeInfinity, CupolaDriveOuterX);
				inBone = Within(inBone, turret, drive, 1, CupolaDriveY.x, CupolaDriveY.y);
				zones.CupolaDrive = Wake(
					Clone(_d.CupolaDriveZone, "ZPU cupola drive"),
					turret,
					turAar,
					inBone,
					"commander's cupola drive");
			}

			void FitElevationDrive(Zones zones, Transform cupolaBone, Transform cupAar)
			{
				if (_d.ElevationDriveZone == null || cupAar == null) return;
				zones.ElevationDrive = Wake(
					Clone(_d.ElevationDriveZone, "ZPU elevation drive"),
					cupolaBone,
					cupAar,
					Moved(_d.ElevationDriveZone, cupolaBone),
					"ZPU elevation drive");
			}

			void FitSight(Zones zones, Transform cupolaBone, Transform cupAar)
			{
				if (_d.SightZone == null || cupAar == null) return;
				zones.Sight = Wake(
					Clone(_d.SightZone, "PZU-7 sight"),
					cupolaBone,
					cupAar,
					Moved(_d.SightZone, cupolaBone),
					"PZU-7 sight");
			}

			void FitTkn4sSight(Zones zones, Transform cupolaBone, Transform cupAar)
			{
				if (_d.SightZone == null || cupAar == null || _d.CommanderOptic == null) return;
				var zoneObject = Clone(_d.SightZone, "TKN-4S sight");
				zoneObject.GetComponent<MeshFilter>().sharedMesh = RemoteMg.BoxMesh("T90A hit zone box");
				var inBone = (_root.worldToLocalMatrix * cupolaBone.localToWorldMatrix).inverse * Matrix4x4.TRS(
					CommanderSight.ZoneCentre,
					Quaternion.identity,
					CommanderSight.ZoneSize);
				zones.Tkn4s = Wake(zoneObject, cupolaBone, cupAar, inBone, "TKN-4S commander's sight");
			}

			void FitGun(Zones zones, Transform cradle)
			{
				if (_d.ElevationDriveZone == null) return;
				// Its own LateFollow group. A collider left under the unit joins its rigidbody.
				var aar = Followers.ColliderGroup(_root, "T90A KORD COLLIDERS", cradle, "AAR").Find("AAR");
				var zoneObject = Clone(_d.ElevationDriveZone, "Kord barrel");
				zoneObject.GetComponent<MeshFilter>().sharedMesh = RemoteMg.BoxMesh("T90A hit zone box");
				foreach (var visual in zoneObject.GetComponentsInChildren<AarVisual>(true)) Object.DestroyImmediate(visual);
				foreach (var renderer in zoneObject.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
				var inBone = Matrix4x4.TRS(GunZoneCentre, Quaternion.identity, GunZoneSize);
				zones.Gun = Wake(zoneObject, cradle, aar, inBone, "6P49 Kord");
			}

			GameObject Clone(DC src, string name)
			{
				var zoneObject = Object.Instantiate(src.gameObject, _holder, false);
				zoneObject.SetActive(false);
				zoneObject.name = "T90A_" + name;
				RemoteMg.Scrub(zoneObject, _d.Root);
				return zoneObject;
			}

			DC Wake(GameObject zoneObject, Transform bone, Transform group, Matrix4x4 inBone, string dcName)
			{
				Followers.PlaceOnBone(zoneObject.transform, bone, group);
				var local = Matrix4x4.TRS(zoneObject.transform.localPosition, zoneObject.transform.localRotation, Vector3.one);
				var matrix = local * inBone;
				zoneObject.transform.localPosition = matrix.GetColumn(3);
				zoneObject.transform.localRotation = matrix.rotation;
				zoneObject.transform.localScale = matrix.lossyScale;
				// Box, not the donor's mesh collider: two are non-convex and the sight's a thin shell a round can thread.
				var mesh = zoneObject.GetComponent<MeshFilter>()?.sharedMesh;
				foreach (var collider in zoneObject.GetComponents<Collider>()) Object.DestroyImmediate(collider);
				var box = zoneObject.AddComponent<BoxCollider>();
				if (mesh != null)
				{
					box.center = mesh.bounds.center;
					box.size = mesh.bounds.size;
				}
				var armor = zoneObject.GetComponent<UniformArmor>();
				if (armor != null)
				{
					armor.Unit = _vehicle;
					ArmorName.SetValue(armor, dcName);
				}
				var component = zoneObject.GetComponent<DC>();
				component.SetName(dcName);
				zoneObject.SetActive(true);
				GHPC.PhysicsHelpers.RaycastColliderGroup.AddNewColliderSetToParentGroup(zoneObject.transform);
				return component;
			}

			Matrix4x4 Moved(DC src, Transform bone)
			{
				var boneInPrefab = (_root.worldToLocalMatrix * bone.localToWorldMatrix).inverse;
				var srcInDonor = _d.Root.transform.worldToLocalMatrix * src.transform.localToWorldMatrix;
				return boneInPrefab * Matrix4x4.Translate(_shift) * srcInDonor;
			}

			// Trims the zone's box along one prefab axis to the given limits, so it stays inside the part it guards.
			Matrix4x4 Within(Matrix4x4 inBone, Transform bone, Mesh mesh, int axisIndex, float lowLimit, float highLimit)
			{
				if (mesh == null) return inBone;
				var toPrefab = (_root.worldToLocalMatrix * bone.localToWorldMatrix) * inBone;
				Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
				var bounds = mesh.bounds;
				int k = 0;
				float best = -1f;
				for (int i = 0; i < 3; i++)
				{
					float alignment = Mathf.Abs(toPrefab.MultiplyVector(Axis(i)).normalized[axisIndex]);
					if (alignment > best)
					{
						best = alignment;
						k = i;
					}
				}
				var dir = Axis(k) * Mathf.Sign(toPrefab.MultiplyVector(Axis(k))[axisIndex]);
				var low = bounds.center - dir * bounds.extents[k];
				var high = bounds.center + dir * bounds.extents[k];
				float spanStart = toPrefab.MultiplyPoint(low)[axisIndex];
				float spanEnd = toPrefab.MultiplyPoint(high)[axisIndex];
				float clampedStart = Mathf.Max(spanStart, lowLimit);
				float clampedEnd = Mathf.Min(spanEnd, highLimit);
				if ((clampedStart <= spanStart && clampedEnd >= spanEnd) || clampedEnd <= clampedStart) return inBone;
				float startFraction = (clampedStart - spanStart) / (spanEnd - spanStart);
				float endFraction = (clampedEnd - spanStart) / (spanEnd - spanStart);
				var scale = Vector3.one;
				scale[k] = endFraction - startFraction;
				var start = low + dir * (2f * bounds.extents[k] * startFraction);
				return inBone * Matrix4x4.Translate(start) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-low);
			}
		}
	}
}
