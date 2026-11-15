using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace T90A.Common
{
	public sealed class BeltCurve
	{
		const int N = 48;
		readonly Vector3[] _p = new Vector3[N + 1];
		readonly float[] _s = new float[N + 1];
		Vector3 _upA, _upB;
		public float Length => _s[N];

		public BeltCurve(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp) =>
			Set(feed, feedDir, feedUp, mouth, mouthDir, mouthUp);

		public void Set(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp)
		{
			float handleLength = (mouth - feed).magnitude * 0.4f;
			Vector3 controlA = feed + feedDir * handleLength, controlB = mouth + mouthDir * handleLength;
			for (int i = 0; i <= N; i++)
			{
				float fraction = i / (float)N, remaining = 1f - fraction;
				_p[i] = remaining * remaining * remaining * feed
					+ 3f * remaining * remaining * fraction * controlA
					+ 3f * remaining * fraction * fraction * controlB
					+ fraction * fraction * fraction * mouth;
				_s[i] = i == 0 ? 0f : _s[i - 1] + (_p[i] - _p[i - 1]).magnitude;
			}
			_upA = feedUp;
			_upB = mouthUp;
		}

		int Seg(float s)
		{
			int i = 0;
			while (i < N - 1 && _s[i + 1] < s) i++;
			return i;
		}

		public Vector3 Point(float s)
		{
			if (s <= 0f) return _p[0] + (_p[1] - _p[0]).normalized * s;
			if (s >= Length) return _p[N] + (_p[N] - _p[N - 1]).normalized * (s - Length);
			int i = Seg(s);
			return Vector3.Lerp(_p[i], _p[i + 1], (s - _s[i]) / Mathf.Max(1e-6f, _s[i + 1] - _s[i]));
		}

		public Matrix4x4 Frame(float s)
		{
			int i = Mathf.Clamp(Seg(Mathf.Clamp(s, 0f, Length)), 0, N - 1);
			var tan = (_p[i + 1] - _p[i]).normalized;
			var up = Vector3.Slerp(_upA, _upB, Mathf.Clamp01(s / Mathf.Max(1e-6f, Length)));
			if (Mathf.Abs(Vector3.Dot(up, tan)) > 0.995f) up = Vector3.Cross(tan, Vector3.right);
			return Matrix4x4.TRS(Point(s), Quaternion.LookRotation(tan, up), Vector3.one);
		}

		public float Closest(Vector3 p)
		{
			int best = 0;
			for (int i = 1; i <= N; i++) if ((_p[i] - p).sqrMagnitude < (_p[best] - p).sqrMagnitude) best = i;
			float bestLength = _s[best], bestDistance = (_p[best] - p).sqrMagnitude;
			for (int i = Mathf.Max(0, best - 1); i < Mathf.Min(N, best + 1); i++)
				for (int j = 0; j <= 20; j++)
				{
					var point = Vector3.Lerp(_p[i], _p[i + 1], j / 20f);
					if ((point - p).sqrMagnitude < bestDistance)
					{
						bestDistance = (point - p).sqrMagnitude;
						bestLength = Mathf.Lerp(_s[i], _s[i + 1], j / 20f);
					}
				}
			return bestLength;
		}
	}

	public static class BeltRound
	{
		public sealed class Data
		{
			public Vector3[] V;
			public Vector2[] Uv;
			public int[] Round, Cartridge;
		}

		public static Data Read(byte[] gz)
		{
			if (gz == null) return null;
			using (var reader = new BinaryReader(new GZipStream(new MemoryStream(gz), CompressionMode.Decompress)))
			{
				if (new string(reader.ReadChars(4)) != "GBLT" || reader.ReadInt32() != 1) return null;
				var data = new Data();
				reader.ReadSingle();
				int count = reader.ReadInt32();
				data.V = new Vector3[count];
				data.Uv = new Vector2[count];
				for (int i = 0; i < count; i++)
				{
					data.V[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					data.Uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
				}
				int[] Ints()
				{
					var indices = new int[reader.ReadInt32()];
					for (int i = 0; i < indices.Length; i++) indices[i] = reader.ReadInt32();
					return indices;
				}
				data.Round = Ints();
				data.Cartridge = Ints();
				return data;
			}
		}

		public static Mesh RoundMesh(Data d, string name)
		{
			var mesh = new Mesh { name = name };
			mesh.vertices = d.V;
			mesh.uv = d.Uv;
			mesh.SetTriangles(d.Round, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();
			mesh.RecalculateBounds();
			return mesh;
		}

		public static (Vector3[] v, Vector3[] n, Vector2[] uv, Vector4[] tg, int[] tris, List<int> starts) CartridgeArrays(
			Mesh round,
			Data d)
		{
			var starts = new List<int>();
			for (int start = 0; start < d.Cartridge.Length; start += 3) starts.Add(start);
			return (round.vertices, round.normals, round.uv, round.tangents, d.Cartridge, starts);
		}

		public static (Vector3 p, Quaternion r) RestPose(BeltCurve c, int i, float pitch, Quaternion rot)
		{
			var frame = c.Frame(i * pitch);
			return (frame.MultiplyPoint3x4(Vector3.zero), frame.rotation * rot);
		}

		public static Quaternion CartridgeRot(BeltCurve rest, Vector3 cartridgeAxis)
		{
			var toF = rest.Frame(0f).inverse;
			var axis = toF.MultiplyVector(cartridgeAxis).normalized;
			var along = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
			return Quaternion.LookRotation(axis, Vector3.Cross(axis, along));
		}
	}
}
