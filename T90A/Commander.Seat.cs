using System.Linq;
using GHPC.Vehicle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// The CUPOLA bone move doesn't take the commander's head slot or figure with it, so they're moved by the same
	// amount here.
	internal static partial class CommanderSeat
	{
		internal static void Apply(Vehicle vehicle)
		{
			var bone = T90AMod.Edits?.Bones.FirstOrDefault(e => e.bone.EndsWith("/CUPOLA"));
			if (bone == null || bone.Value.bone == null) return;
			var delta = bone.Value.delta;
			var root = vehicle.transform;
			var turret = Followers.TurretBone(vehicle);
			if (turret == null) return;

			var head = turret.GetComponentsInChildren<GHPC.Camera.CameraSlot>(true).FirstOrDefault(s => s.name == "commander head");
			if (head != null) head.transform.position += turret.TransformVector(delta);

			var figure = Followers.On(root, turret)
				.SelectMany(follower => follower
					.GetComponentsInChildren<Transform>(true)
					.Where(c => c.name == "Commander"
						&& c.GetComponent<GHPC.Crew.BodyPartComponentRegistry>() != null)
					.Select(commanderFigure => (follower, commanderFigure)))
				.FirstOrDefault();
			if (figure.commanderFigure != null)
			{
				var boneRot = figure.follower.transform.rotation * Quaternion.Inverse(Followers.RotShift(figure.follower));
				figure.commanderFigure.position += boneRot * delta;
			}
		}
	}
}
