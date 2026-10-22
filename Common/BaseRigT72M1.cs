using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public static class BaseRigT72M1
	{
		public static readonly string[] HiddenRenderers =
		{
			"T72M1_hull", "T72M1_turret", "T72M1_skin_turret", "lp_turret_cables001", "equipment",
			"t72m hull net", "t72m turret net", "t72m gun net",
			"roundels_72M", "roundels_72M1", "bustle_number_3digit", "bustle_number_4digit",
			"nsv_mount", "lp_R123",
		};

		public static bool IsHullDetailBone(string n) =>
			n == "fender_L" || n == "fender_R" || n == "flaps" || n.StartsWith("gill_") || n.StartsWith("skirt_");

		public static void HideBaseVisuals(Transform root, byte[] trimmedSkin = null)
		{
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
				if (HiddenRenderers.Contains(r.gameObject.name) || (r.transform.parent != null && HiddenRenderers.Contains(r.transform.parent.name)))
					r.enabled = false;
			foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name == "T72M1_skin_hull"))
			{
				foreach (var bone in skin.bones)
					if (bone != null && IsHullDetailBone(bone.name)) bone.localScale = Vector3.zero;
				if (trimmedSkin != null) TrimSkin(skin, trimmedSkin);
			}

			var smoke = root.Find("---MESH---/HULL/TURRET/smoke rack");
			if (smoke != null) smoke.localScale = Vector3.zero;
		}

		static Mesh _trimmed;
		static Mesh _trimmedFrom;

		public static string TrimFailure { get; private set; }

		public static void TrimSkin(SkinnedMeshRenderer skin, byte[] data)
		{
			void Fail(string why) { TrimFailure = why; }
			TrimFailure = null;
			var src = skin.sharedMesh;
			if (src == null) { Fail("skin has no mesh"); return; }
			if (_trimmed != null && src == _trimmed) return;
			try
			{
				using (var r = new BinaryReader(new GZipStream(new MemoryStream(data), CompressionMode.Decompress)))
				{
					string Str() => System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
					if (new string(r.ReadChars(4)) != "GSKN" || r.ReadInt32() != 1) { Fail("trimmed skin data: unknown format"); return; }
					string meshName = Str();
					int srcVerts = r.ReadInt32(), boneCount = r.ReadInt32();
					var names = new string[boneCount];
					for (int i = 0; i < boneCount; i++) names[i] = Str();
					var bones = skin.bones;
					string mismatch = meshName != src.name ? $"mesh {src.name}, data from {meshName}"
						: srcVerts != src.vertexCount ? $"mesh has {src.vertexCount} vertices, data {srcVerts}"
						: bones.Length != boneCount ? $"skin has {bones.Length} bones, data {boneCount}"
						: Enumerable.Range(0, boneCount).Where(i => bones[i] == null || bones[i].name != names[i]).Select(i => $"bone {i} is {bones[i]?.name ?? "null"}, data {names[i]}").FirstOrDefault();
					if (mismatch != null) { Fail($"running-gear mesh data doesn't match the game's mesh ({mismatch}; game updated?)"); return; }

					if (_trimmed == null || _trimmedFrom != src)
					{
						int n = r.ReadInt32();
						Vector3[] V3() { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); return a; }
						Vector2[] V2() { var a = new Vector2[n]; for (int i = 0; i < n; i++) a[i] = new Vector2(r.ReadSingle(), r.ReadSingle()); return a; }
						var pos = V3(); var nrm = V3();
						var tan = new Vector4[n];
						for (int i = 0; i < n; i++) tan[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
						var uv0 = V2(); var uv1 = V2();
						var bw = new BoneWeight[n];
						for (int i = 0; i < n; i++) bw[i] = new BoneWeight { boneIndex0 = r.ReadInt32(), weight0 = r.ReadSingle(), boneIndex1 = r.ReadInt32(), weight1 = r.ReadSingle() };
						var bind = new Matrix4x4[boneCount];
						for (int b = 0; b < boneCount; b++) { var m = new Matrix4x4(); for (int k = 0; k < 16; k++) m[k / 4, k % 4] = r.ReadSingle(); bind[b] = m; }
						var idx = new int[r.ReadInt32()];
						for (int i = 0; i < idx.Length; i++) idx[i] = r.ReadInt32();

						var mesh = new Mesh { name = src.name + " (trimmed)", hideFlags = HideFlags.DontUnloadUnusedAsset };
						mesh.vertices = pos; mesh.normals = nrm; mesh.tangents = tan; mesh.uv = uv0; mesh.uv2 = uv1;
						mesh.boneWeights = bw; mesh.bindposes = bind;
						mesh.triangles = idx;
						mesh.bounds = src.bounds;
						mesh.UploadMeshData(true);
						_trimmed = mesh; _trimmedFrom = src;
					}
				}
				var bounds = skin.localBounds;
				skin.sharedMesh = _trimmed;
				skin.localBounds = bounds;
			}
			catch (System.Exception e)
			{
				Fail("trimmed skin failed (" + e.GetType().Name + ": " + e.Message + ")");
			}
		}

		public static Material FindTemplate(Transform root)
		{
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
				if (r.name == "T72M1_hull" || r.name == "T72M1_turret")
				{
					var m = r.sharedMaterials.FirstOrDefault(x => x != null && x.shader.name == "GHPC/VehicleShader");
					if (m != null) return m;
				}
			return null;
		}
	}
}
