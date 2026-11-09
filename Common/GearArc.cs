using System;

namespace T90A.Common
{
	public static class GearArc
	{
		public static void Wheel(float rz, float ry, float dy, out float z, out float y)
		{
			double l = Math.Sqrt(rz * rz + ry * ry), h = Math.Max(-0.98 * l, Math.Min(0.98 * l, ry + dy));
			y = (float)h;
			z = (float)(Math.Sign(rz) * Math.Sqrt(l * l - h * h));
		}

		public static void Bogie(float paz, float pay, float raz, float ray, float da, float pbz, float pby, float rbz, float rby, float db, float minDist,
			out float az, out float ay, out float bz, out float by)
		{
			Wheel(raz, ray, da, out az, out ay);
			Wheel(rbz, rby, db, out bz, out by);
			double dz = paz + az - pbz - bz, h = pay + ay - pby - by;
			if (Math.Abs(h) >= minDist) return;
			double need = Math.Sqrt(minDist * minDist - h * h), gap = Math.Abs(dz);
			if (gap >= need) return;
			float half = (float)((need - gap) / 2.0), s = dz >= 0.0 ? 1f : -1f;
			az += s * half;
			bz -= s * half;
		}

		public static float ArmDeg(float rz, float ry, float z, float y) => (float)((Math.Atan2(ry, rz) - Math.Atan2(y, z)) * 180.0 / Math.PI);
	}
}
