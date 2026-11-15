using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace T90A.Common
{
	// The T-72M1 hull skin with the listed triangles (the side skirts) taken out.
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
			string Fail(string why)
			{
				Failure = why;
				return why + "; " + fallback;
			}
			Failure = null;
			var src = skin.sharedMesh;
			if (src == null) return Fail("skin has no mesh");
			if (_trimmed != null && src == _trimmed) return "trimmed skin already in place";
			try
			{
				using (var reader = new BinaryReader(new GZipStream(
					new MemoryStream(data),
					CompressionMode.Decompress)))
				{
					string Str() => System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
					if (new string(reader.ReadChars(4)) != "GSKN" || reader.ReadInt32() != 1)
						return Fail("trimmed skin data: unknown format");
					string meshName = Str();
					int srcVerts = reader.ReadInt32(), boneCount = reader.ReadInt32();
					var names = new string[boneCount];
					for (int i = 0; i < boneCount; i++) names[i] = Str();
					var bones = skin.bones;
					string mismatch = meshName != src.name ? $"mesh {src.name}, data from {meshName}"
						: srcVerts != src.vertexCount ? $"mesh has {src.vertexCount} vertices, data {srcVerts}"
						: bones.Length != boneCount ? $"skin has {bones.Length} bones, data {boneCount}"
						: Enumerable.Range(0, boneCount)
							.Where(i => bones[i] == null || bones[i].name != names[i])
							.Select(i => $"bone {i} is {bones[i]?.name ?? "null"}, data {names[i]}")
							.FirstOrDefault();
					if (mismatch != null) return Fail($"running-gear mesh data doesn't match the game's mesh ({mismatch}; game updated?)");

					if (_trimmed == null || _trimmedFrom != src)
					{
						int vertexCount = reader.ReadInt32();
						Vector3[] V3()
						{
							var values = new Vector3[vertexCount];
							for (int i = 0;
								i < vertexCount;
								i++) values[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
							return values;
						}
						Vector2[] V2()
						{
							var values = new Vector2[vertexCount];
							for (int i = 0; i < vertexCount; i++) values[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
							return values;
						}
						var pos = V3();
						var nrm = V3();
						var tan = new Vector4[vertexCount];
						for (int i = 0;
							i < vertexCount;
							i++) tan[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
						var uv0 = V2();
						var uv1 = V2();
						var boneWeights = new BoneWeight[vertexCount];
						for (int i = 0;
							i < vertexCount;
							i++)
								boneWeights[i] = new BoneWeight
								{
									boneIndex0 = reader.ReadInt32(),
									weight0 = reader.ReadSingle(),
									boneIndex1 = reader.ReadInt32(),
									weight1 = reader.ReadSingle()
								};
						var bind = new Matrix4x4[boneCount];
						for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
						{
							var bindPose = new Matrix4x4();
							for (int k = 0; k < 16; k++) bindPose[k / 4, k % 4] = reader.ReadSingle();
							bind[boneIndex] = bindPose;
						}
						var indices = new int[reader.ReadInt32()];
						for (int i = 0; i < indices.Length; i++) indices[i] = reader.ReadInt32();

						var mesh = new Mesh { name = src.name + " (trimmed)", hideFlags = HideFlags.DontUnloadUnusedAsset };
						mesh.vertices = pos;
						mesh.normals = nrm;
						mesh.tangents = tan;
						mesh.uv = uv0;
						mesh.uv2 = uv1;
						mesh.boneWeights = boneWeights;
						mesh.bindposes = bind;
						mesh.triangles = indices;
						mesh.bounds = src.bounds;
						mesh.UploadMeshData(true);
						_trimmed = mesh;
						_trimmedFrom = src;
						_trimmedTris = indices.Length / 3;
						_sourceTris = (int)(src.GetIndexCount(0) / 3);
					}
				}
				var bounds = skin.localBounds;
				skin.sharedMesh = _trimmed;
				skin.localBounds = bounds;
				return $"skin swapped for the trimmed copy ({_trimmed.vertexCount} vertices, {_trimmedTris} of {_sourceTris} triangles kept: {_removed})";
			}
			catch (System.Exception exception)
			{
				return Fail("trimmed skin failed (" + exception.GetType().Name + ": " + exception.Message + ")");
			}
		}
	}
}
