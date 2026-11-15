using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NWH.VehiclePhysics;
using UnityEngine;

namespace T90A.Common
{
	internal static class GearFollow
	{
		[DefaultExecutionOrder(2000)]
		internal sealed class Driver : MonoBehaviour
		{
			internal Transform Root;
			internal float RestY;
			internal readonly List<Part> Parts = new List<Part>();
			internal readonly List<Node> Nodes = new List<Node>();
			internal readonly List<RoadWheel> Wheels = new List<RoadWheel>();
			internal float MinDist;
			internal float RiseLimit;

			float Dy(Transform bone) => Root.InverseTransformPoint(bone.position).y - RestY;

			void LateUpdate()
			{
				foreach (var wheel in Wheels)
				{
					wheel.Drawn = RiseLimit > 0f ? Mathf.Min(Dy(wheel.Bone), RiseLimit) : Dy(wheel.Bone);
					GearArc.Wheel(wheel.Rz, wheel.Ry, wheel.Drawn, out wheel.Z, out wheel.Y);
				}
				foreach (var wheel in Wheels)
				{
					var mate = wheel.Mate;
					if (mate == null || Wheels.IndexOf(mate) < Wheels.IndexOf(wheel)) continue;
					GearArc.Bogie(
						wheel.Pz,
						wheel.Py,
						wheel.Rz,
						wheel.Ry,
						wheel.Drawn,
						mate.Pz,
						mate.Py,
						mate.Rz,
						mate.Ry,
						mate.Drawn,
						MinDist,
						out wheel.Z,
						out wheel.Y,
						out mate.Z,
						out mate.Y);
				}
				foreach (var part in Parts)
				{
					switch (part.Kind)
					{
						case "wheel":
							part.Swing.localPosition = new Vector3(0f, part.Wheel.Y - part.Wheel.Ry, part.Wheel.Z - part.Wheel.Rz);
							part.Swing.localRotation = Quaternion.AngleAxis(SpinDeg(Root, part.Bone), Vector3.right);
							break;
						case "arm":
							part.Swing.localRotation = Quaternion.AngleAxis(
								GearArc.ArmDeg(part.Wheel.Rz, part.Wheel.Ry, part.Wheel.Z, part.Wheel.Y),
								Vector3.right);
							break;
						case "rod":
							part.Swing.localPosition = new Vector3(0f, part.Wheel.Drawn, 0f);
							break;
						default:
							part.Swing.localRotation = Quaternion.AngleAxis(SpinDeg(Root, part.Bone), Vector3.right);
							break;
					}
				}
				foreach (var node in Nodes) node.T.localPosition = node.Rest + Vector3.up * node.Wheel.Drawn;
			}
		}

		internal sealed class RoadWheel
		{
			public Transform Bone;
			public RoadWheel Mate;
			public float Rz, Ry, Pz, Py, Drawn, Z, Y;
		}

		internal sealed class Part
		{
			public string Kind;
			public Transform Bone, Swing;
			public RoadWheel Wheel;
		}

		internal sealed class Node
		{
			public Transform T;
			public RoadWheel Wheel;
			public Vector3 Rest;
		}

		internal static float SpinDeg(Transform root, Transform bone)
		{
			var forward = Quaternion.Inverse(root.rotation) * bone.rotation * Vector3.forward;
			return Mathf.Atan2(-forward.y, forward.z) * Mathf.Rad2Deg;
		}

		static readonly AccessTools.FieldRef<Tracks, Material> LeftMat = Reflect.FieldRefAccess<Tracks, Material>("leftMat");
		static readonly AccessTools.FieldRef<Tracks, Material> RightMat = Reflect.FieldRefAccess<Tracks, Material>("rightMat");

		internal static List<string> Apply(Transform root, RigMap map, string prefix, Func<string, Transform> find)
		{
			var log = new List<string>();
			var hull = RigBones.Hull(root);
			if (hull == null)
			{
				log.Add("running gear: no hull bone; nothing moved");
				return log;
			}
			var bones = hull
				.GetComponentsInChildren<Transform>(true)
				.GroupBy(b => b.name)
				.ToDictionary(n => n.Key, nb => nb.First());
			var driver = root.gameObject.AddComponent<Driver>();
			driver.Root = root;
			driver.RestY = map.WheelRestY;
			driver.MinDist = 2f * map.WheelRadius + map.RimGap;
			driver.RiseLimit = map.WheelRiseLimit;
			var miss = new List<string>();
			var wheels = new Dictionary<string, RoadWheel>();
			foreach (var followPart in map.FollowParts.Where(p => p.Kind == "wheel"))
				if (bones.TryGetValue(followPart.Drive, out var driveBone))
				{
					var wheel = new RoadWheel
					{
						Bone = driveBone,
						Rz = followPart.Pivot.z - followPart.Target.z,
						Ry = followPart.Pivot.y - followPart.Target.y,
						Pz = followPart.Target.z,
						Py = followPart.Target.y
					};
					wheels[followPart.Drive] = wheel;
					driver.Wheels.Add(wheel);
				}
			foreach (var followPart in map.FollowParts.Where(p => p.Kind == "wheel" && p.Mate.Length > 0))
				if (wheels.TryGetValue(followPart.Drive, out var wheel) && wheels.TryGetValue(followPart.Mate, out var mate))
					wheel.Mate = mate;
			var skins = new List<(string wt, SkinnedMeshRenderer smr)>();
			foreach (var followPart in map.FollowParts)
			{
				var vis = find(followPart.Group);
				if (vis == null)
				{
					miss.Add(followPart.Wt);
					continue;
				}
				vis.name = prefix + followPart.Group;
				if (followPart.Kind == "track")
				{
					var smr = vis.GetComponent<SkinnedMeshRenderer>();
					var names = followPart.Drive.Split(',');
					int found = 0;
					for (int i = 0; smr != null && i < names.Length && i + 1 < smr.bones.Length; i++)
						if (wheels.TryGetValue(names[i], out var roadWheel))
						{
							driver.Nodes.Add(new Node { T = smr.bones[i + 1], Wheel = roadWheel, Rest = smr.bones[i + 1].localPosition });
							found++;
						}
					if (smr == null || found != names.Length) miss.Add(followPart.Wt + " (track nodes)");
					else skins.Add((followPart.Wt, smr));
					continue;
				}
				if (!bones.TryGetValue(followPart.Drive, out var bone))
				{
					miss.Add(followPart.Wt + " (bone " + followPart.Drive + ")");
					continue;
				}
				wheels.TryGetValue(followPart.Drive, out var own);
				if (followPart.Kind != "spin" && own == null)
				{
					miss.Add(followPart.Wt + " (no road wheel " + followPart.Drive + ")");
					continue;
				}
				var holder = new GameObject($"{prefix}{followPart.Group} holder").transform;
				holder.SetParent(hull, false);
				holder.SetPositionAndRotation(root.TransformPoint(followPart.Pivot), root.rotation);
				var swing = new GameObject($"{prefix}{followPart.Group} swing").transform;
				swing.SetParent(holder, false);
				vis.SetParent(swing, true);
				driver.Parts.Add(new Part { Kind = followPart.Kind, Bone = bone, Swing = swing, Wheel = own });
			}
			log.Add($"running gear (follow): {driver.Parts.Count} parts, {driver.Nodes.Count} track nodes on the base wheel bones"
				+ (miss.Count > 0 ? "; NOT FOUND: " + string.Join(", ", miss) : ""));
			log.Add(UseTracks(root, skins, map.TrackUv));
			return log;
		}

		static string UseTracks(Transform root, List<(string wt, SkinnedMeshRenderer smr)> skins, Vector2 uv)
		{
			var vehicleController = root.GetComponent<VehicleController>();
			var left = skins.FirstOrDefault(s => s.wt.EndsWith("_l")).smr;
			var right = skins.FirstOrDefault(s => s.wt.EndsWith("_r")).smr;
			if (vehicleController == null || vehicleController.tracks == null || left == null || right == null)
				return "tracks: no VehicleController or skin; the track doesn't scroll";
			var tracks = vehicleController.tracks;
			var src = tracks.leftTrackRenderer != null ? tracks.leftTrackRenderer.sharedMaterial : null;
			if (src != null)
			{
				Dress(left, src);
				Dress(right, src);
			}
			tracks.leftTrackRenderer = left;
			tracks.rightTrackRenderer = right;
			tracks.uvDirection = uv;
			if (LeftMat != null && LeftMat(tracks) != null)
			{
				LeftMat(tracks) = left.material;
				RightMat(tracks) = right.material;
			}
			return $"tracks: own skins scroll along ({uv.x}, {uv.y}), material {(src != null ? src.shader.name : "the bundle's")}";
		}

		static void Dress(SkinnedMeshRenderer r, Material src)
		{
			var own = r.sharedMaterial;
			var material = new Material(src) { name = own.name + " (track)" };
			foreach (var (target, from) in new[]
			{
				("_MainTex", "_MainTex"),
				("_Colour", "_MainTex"),
				("_Normal", "_BumpMap"),
				("_Smoothness", "_MetallicGlossMap")
			})
				if (material.HasProperty(target) && own.HasProperty(from) && own.GetTexture(from) != null)
					material.SetTexture(target, own.GetTexture(from));
			r.sharedMaterial = material;
		}
	}
}
