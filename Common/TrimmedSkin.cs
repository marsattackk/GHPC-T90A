using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	public sealed class TrimmedSkin
	{
		Mesh _trimmed;
		Mesh _trimmedFrom;
		int _trimmedTris, _sourceTris;
		readonly string _removed;

		public TrimmedSkin(string removed) { _removed = removed; }

		public string Failure { get; private set; }

		public string Swap(SkinnedMeshRenderer skin, byte[] data, string fallback)
		{
			string Fail(string why) { Failure = why; return why + "; " + fallback; }
			Failure = null;
			var src = skin.sharedMesh;
			if (src == null) return Fail("skin has no mesh");
			if (_trimmed != null && src == _trimmed) return "trimmed skin already in place";
			try
			{
				using (var r = new BinaryReader(new GZipStream(new MemoryStream(data), CompressionMode.Decompress)))
				{
					string Str() => System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
					if (new string(r.ReadChars(4)) != "GSKN" || r.ReadInt32() != 1) return Fail("trimmed skin data: unknown format");
					string meshName = Str();
					int srcVerts = r.ReadInt32(), boneCount = r.ReadInt32();
					var names = new string[boneCount];
					for (int i = 0; i < boneCount; i++) names[i] = Str();
					var bones = skin.bones;
					string mismatch = meshName != src.name ? $"mesh {src.name}, data from {meshName}"
						: srcVerts != src.vertexCount ? $"mesh has {src.vertexCount} vertices, data {srcVerts}"
						: bones.Length != boneCount ? $"skin has {bones.Length} bones, data {boneCount}"
						: Enumerable.Range(0, boneCount).Where(i => bones[i] == null || bones[i].name != names[i]).Select(i => $"bone {i} is {bones[i]?.name ?? "null"}, data {names[i]}").FirstOrDefault();
					if (mismatch != null) return Fail($"running-gear mesh data doesn't match the game's mesh ({mismatch}; game updated?)");

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
						_trimmed = mesh; _trimmedFrom = src; _trimmedTris = idx.Length / 3; _sourceTris = (int)(src.GetIndexCount(0) / 3);
					}
				}
				var bounds = skin.localBounds;
				skin.sharedMesh = _trimmed;
				skin.localBounds = bounds;
				return $"skin swapped for the trimmed copy ({_trimmed.vertexCount} vertices, {_trimmedTris} of {_sourceTris} triangles kept: {_removed})";
			}
			catch (System.Exception e)
			{
				return Fail("trimmed skin failed (" + e.GetType().Name + ": " + e.Message + ")");
			}
		}
	}
}
