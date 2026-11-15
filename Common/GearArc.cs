using System;

namespace T90A.Common
{
	// Wheel arm maths: where an arm ends up for a given rise, and how two wheels share the gap.
	public static class GearArc
	{
		public static void Wheel(float rz, float ry, float dy, out float z, out float y)
		{
			double distance = Math.Sqrt(rz * rz + ry * ry), height = Math.Max(-0.98 * distance, Math.Min(0.98 * distance, ry + dy));
			y = (float)height;
			z = (float)(Math.Sign(rz) * Math.Sqrt(distance * distance - height * height));
		}

		public static void Bogie(float paz, float pay, float raz, float ray, float da, float pbz, float pby, float rbz, float rby, float db, float minDist,
			out float az, out float ay, out float bz, out float by)
		{
			Wheel(raz, ray, da, out az, out ay);
			Wheel(rbz, rby, db, out bz, out by);
			double deltaZ = paz + az - pbz - bz, deltaY = pay + ay - pby - by;
			if (Math.Abs(deltaY) >= minDist) return;
			double need = Math.Sqrt(minDist * minDist - deltaY * deltaY), gap = Math.Abs(deltaZ);
			if (gap >= need) return;
			float half = (float)((need - gap) / 2.0), sign = deltaZ >= 0.0 ? 1f : -1f;
			az += sign * half;
			bz -= sign * half;
		}

		public static float ArmDeg(float rz, float ry, float z, float y)
			=> (float)((Math.Atan2(ry, rz) - Math.Atan2(y, z)) * 180.0 / Math.PI);

		public static float ArmMismatch(float rz, float ry, float z, float y)
			=> (float)(Math.Sqrt(z * z + y * y) - Math.Sqrt(rz * rz + ry * ry));

		public static float WheelDepth(float x, float y, float z, float cy, float cz, float r, float x0, float x1)
		{
			double radial = r - Math.Sqrt((y - cy) * (double)(y - cy) + (z - cz) * (double)(z - cz));
			double side = Math.Min(x - x0, x1 - x);
			return (float)Math.Min(radial, side);
		}
	}
}
