using System;

namespace T90A.Common
{
	// A plain vector for FcsMath, so the tests build without UnityEngine.
	internal struct V3
	{
		public float x, y, z;
		public V3(float x, float y, float z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}

		public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
		public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
		public static V3 operator *(V3 a, float k) => new V3(a.x * k, a.y * k, a.z * k);
		public static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
		public float Length => (float)Math.Sqrt(x * x + y * y + z * z);
		public V3 Normalized
		{
			get
			{
				float length = Length;
				return length > 1e-9f ? this * (1f / length) : new V3(0f, 0f, 1f);
			}
		}

		public static V3 FromAzEl(float az, float el)
		{
			double azimuth = az * Math.PI / 180.0, elevation = el * Math.PI / 180.0;
			return new V3(
				(float)(Math.Sin(azimuth) * Math.Cos(elevation)),
				(float)Math.Sin(elevation),
				(float)(Math.Cos(azimuth) * Math.Cos(elevation)));
		}

		public float Az => (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
		public float El => (float)(Math.Atan2(y, Math.Sqrt(x * x + z * z)) * 180.0 / Math.PI);
		public override string ToString() => $"({x:0.00}, {y:0.00}, {z:0.00})";
	}
}
