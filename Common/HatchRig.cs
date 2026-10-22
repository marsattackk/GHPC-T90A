using System.Linq;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class HatchRig
	{
		internal sealed class Follower : MonoBehaviour
		{
			public AnimatedPart Part;
			public Transform Swing;
			public Vector3 Axis, Lift;
			public float Angle;
			float _last = -1f;

			void LateUpdate()
			{
				float o = Part != null ? Part.Openness : 0f;
				if (o == _last) return;
				_last = o;
				Pose(o);
			}

			internal void Pose(float o)
			{
				if (Swing == null) return;
				bool lifted = Lift.sqrMagnitude > 1e-8f;
				float rise = lifted ? Mathf.Clamp01(o / RigMap.LiftShare) : 0f;
				float turn = lifted ? Mathf.Clamp01((o - RigMap.LiftShare) / (1f - RigMap.LiftShare)) : Mathf.Clamp01(o);
				Swing.localRotation = Quaternion.AngleAxis(Angle * turn, Axis);
				Swing.localPosition = Lift * rise;
			}
		}

		internal static void Apply(Transform root, RigMap map, string prefix, string tag)
		{
			if (map == null) return;
			var parts = root.GetComponentsInChildren<AnimatedPart>(true);
			Transform Find(string group) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == prefix + group)
				?? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == group);
			foreach (var h in map.Hatches)
			{
				var lid = Find(h.Group);
				var part = parts.FirstOrDefault(a => a.Transform != null && a.Transform.name == h.BaseBone);
				if (lid == null) continue;
				if (part == null)
				{
					MelonLogger.Warning($"{tag}: the {h.Role.ToLowerInvariant()}'s hatch has no opening animation on this vehicle; it stays closed.");
					continue;
				}
				var parent = part.Transform.parent;
				var hinge = new GameObject($"{prefix}{h.Group} hinge").transform;
				hinge.SetParent(parent, false);
				hinge.SetPositionAndRotation(root.TransformPoint(h.Hinge), root.rotation);
				var swing = new GameObject($"{prefix}{h.Group} swing").transform;
				swing.SetParent(hinge, false);
				lid.SetParent(swing, true);
				lid.name = prefix + h.Group;
				var f = hinge.gameObject.AddComponent<Follower>();
				f.Part = part; f.Swing = swing; f.Axis = h.Axis.normalized; f.Lift = h.Lift; f.Angle = h.Angle;
				f.Pose(part.Openness);
				var aar = part.OtherAarTransforms ?? new Transform[0];
				if (!aar.Contains(swing)) part.OtherAarTransforms = aar.Concat(new[] { swing }).ToArray();
			}
			if (map.RunningGear == "port")
			{
				foreach (var g in map.GearParts)
				{
					var vis = Find(g.Group);
					var bone = root.Find(g.Bone);
					if (vis == null || bone == null) continue;
					vis.SetParent(bone, false);
					vis.localPosition = Vector3.zero;
					vis.localRotation = Quaternion.Inverse(g.RestRot);
					vis.localScale = Vector3.one;
					vis.name = prefix + g.Group;
				}
			}
		}
	}
}
