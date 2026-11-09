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
				foreach (var w in Wheels)
				{
					w.Drawn = RiseLimit > 0f ? Mathf.Min(Dy(w.Bone), RiseLimit) : Dy(w.Bone);
					GearArc.Wheel(w.Rz, w.Ry, w.Drawn, out w.Z, out w.Y);
				}
				foreach (var a in Wheels)
				{
					var b = a.Mate;
					if (b == null || Wheels.IndexOf(b) < Wheels.IndexOf(a)) continue;
					GearArc.Bogie(a.Pz, a.Py, a.Rz, a.Ry, a.Drawn, b.Pz, b.Py, b.Rz, b.Ry, b.Drawn, MinDist, out a.Z, out a.Y, out b.Z, out b.Y);
				}
				foreach (var p in Parts)
				{
					switch (p.Kind)
					{
						case "wheel":
							p.Swing.localPosition = new Vector3(0f, p.Wheel.Y - p.Wheel.Ry, p.Wheel.Z - p.Wheel.Rz);
							p.Swing.localRotation = Quaternion.AngleAxis(SpinDeg(Root, p.Bone), Vector3.right);
							break;
						case "arm":
							p.Swing.localRotation = Quaternion.AngleAxis(GearArc.ArmDeg(p.Wheel.Rz, p.Wheel.Ry, p.Wheel.Z, p.Wheel.Y), Vector3.right);
							break;
						case "rod":
							p.Swing.localPosition = new Vector3(0f, p.Wheel.Drawn, 0f);
							break;
						default:
							p.Swing.localRotation = Quaternion.AngleAxis(SpinDeg(Root, p.Bone), Vector3.right);
							break;
					}
				}
				foreach (var n in Nodes) n.T.localPosition = n.Rest + Vector3.up * n.Wheel.Drawn;
			}
		}

		internal sealed class RoadWheel { public Transform Bone; public RoadWheel Mate; public float Rz, Ry, Pz, Py, Drawn, Z, Y; }

		internal sealed class Part { public string Kind; public Transform Bone, Swing; public RoadWheel Wheel; }

		internal sealed class Node { public Transform T; public RoadWheel Wheel; public Vector3 Rest; }

		internal static float SpinDeg(Transform root, Transform bone)
		{
			var f = Quaternion.Inverse(root.rotation) * bone.rotation * Vector3.forward;
			return Mathf.Atan2(-f.y, f.z) * Mathf.Rad2Deg;
		}

		static readonly AccessTools.FieldRef<Tracks, Material> LeftMat = Reflect.FieldRefAccess<Tracks, Material>("leftMat");
		static readonly AccessTools.FieldRef<Tracks, Material> RightMat = Reflect.FieldRefAccess<Tracks, Material>("rightMat");

		internal static void Apply(Transform root, RigMap map, string prefix, Func<string, Transform> find)
		{
			var hull = RigBones.Hull(root);
			if (hull == null) return;
			var bones = hull.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
			var driver = root.gameObject.AddComponent<Driver>();
			driver.Root = root; driver.RestY = map.WheelRestY; driver.MinDist = 2f * map.WheelRadius + map.RimGap; driver.RiseLimit = map.WheelRiseLimit;
			var wheels = new Dictionary<string, RoadWheel>();
			foreach (var g in map.FollowParts.Where(x => x.Kind == "wheel"))
				if (bones.TryGetValue(g.Drive, out var b))
				{
					var w = new RoadWheel { Bone = b, Rz = g.Pivot.z - g.Target.z, Ry = g.Pivot.y - g.Target.y, Pz = g.Target.z, Py = g.Target.y };
					wheels[g.Drive] = w; driver.Wheels.Add(w);
				}
			foreach (var g in map.FollowParts.Where(x => x.Kind == "wheel" && x.Mate.Length > 0))
				if (wheels.TryGetValue(g.Drive, out var a) && wheels.TryGetValue(g.Mate, out var b)) a.Mate = b;
			var skins = new List<(string wt, SkinnedMeshRenderer smr)>();
			foreach (var g in map.FollowParts)
			{
				var vis = find(g.Group);
				if (vis == null) continue;
				vis.name = prefix + g.Group;
				if (g.Kind == "track")
				{
					var smr = vis.GetComponent<SkinnedMeshRenderer>();
					var names = g.Drive.Split(',');
					int found = 0;
					for (int i = 0; smr != null && i < names.Length && i + 1 < smr.bones.Length; i++)
						if (wheels.TryGetValue(names[i], out var rw)) { driver.Nodes.Add(new Node { T = smr.bones[i + 1], Wheel = rw, Rest = smr.bones[i + 1].localPosition }); found++; }
					if (!(smr == null || found != names.Length)) skins.Add((g.Wt, smr));
					continue;
				}
				if (!bones.TryGetValue(g.Drive, out var bone)) continue;
				wheels.TryGetValue(g.Drive, out var own);
				if (g.Kind != "spin" && own == null) continue;
				var holder = new GameObject($"{prefix}{g.Group} holder").transform;
				holder.SetParent(hull, false);
				holder.SetPositionAndRotation(root.TransformPoint(g.Pivot), root.rotation);
				var swing = new GameObject($"{prefix}{g.Group} swing").transform;
				swing.SetParent(holder, false);
				vis.SetParent(swing, true);
				driver.Parts.Add(new Part { Kind = g.Kind, Bone = bone, Swing = swing, Wheel = own });
			}
			UseTracks(root, skins, map.TrackUv);
		}

		static void UseTracks(Transform root, List<(string wt, SkinnedMeshRenderer smr)> skins, Vector2 uv)
		{
			var vc = root.GetComponent<VehicleController>();
			var left = skins.FirstOrDefault(s => s.wt.EndsWith("_l")).smr;
			var right = skins.FirstOrDefault(s => s.wt.EndsWith("_r")).smr;
			if (vc == null || vc.tracks == null || left == null || right == null) return;
			var t = vc.tracks;
			var src = t.leftTrackRenderer != null ? t.leftTrackRenderer.sharedMaterial : null;
			if (src != null) { Dress(left, src); Dress(right, src); }
			t.leftTrackRenderer = left; t.rightTrackRenderer = right; t.uvDirection = uv;
			if (LeftMat != null && LeftMat(t) != null) { LeftMat(t) = left.material; RightMat(t) = right.material; }
		}

		static void Dress(SkinnedMeshRenderer r, Material src)
		{
			var own = r.sharedMaterial;
			var m = new Material(src) { name = own.name + " (track)" };
			foreach (var (to, from) in new[] { ("_MainTex", "_MainTex"), ("_Colour", "_MainTex"), ("_Normal", "_BumpMap"), ("_Smoothness", "_MetallicGlossMap") })
				if (m.HasProperty(to) && own.HasProperty(from) && own.GetTexture(from) != null) m.SetTexture(to, own.GetTexture(from));
			r.sharedMaterial = m;
		}
	}
}
