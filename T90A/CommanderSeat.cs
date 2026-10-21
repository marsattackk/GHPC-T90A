using System.Linq;
using GHPC.Vehicle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class CommanderSeat
	{
		internal static void Apply(Vehicle vehicle)
		{
			var bone = T90AMod.Edits?.Bones.FirstOrDefault(b => b.bone.EndsWith("/CUPOLA"));
			if (bone == null || bone.Value.bone == null) return;
			var delta = bone.Value.delta;
			var root = vehicle.transform;
			var turret = Followers.TurretBone(vehicle);
			if (turret == null) return;

			var head = turret.GetComponentsInChildren<GHPC.Camera.CameraSlot>(true).FirstOrDefault(c => c.name == "commander head");
			if (head != null) head.transform.position += turret.TransformVector(delta);

			var figure = Followers.On(root, turret).SelectMany(f => f.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Commander" && t.GetComponent<GHPC.Crew.BodyPartComponentRegistry>() != null).Select(t => (f, t))).FirstOrDefault();
			if (figure.t != null)
			{
				var boneRot = figure.f.transform.rotation * Quaternion.Inverse(Followers.RotShift(figure.f));
				figure.t.position += boneRot * delta;
			}
		}
	}
}
