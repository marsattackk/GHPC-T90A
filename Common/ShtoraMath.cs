using System;

namespace T90A.Common
{
	internal static class ShtoraMath
	{
		internal static void Bearing(V3 point, V3 centre, V3 forward, V3 turretUp, out float azimuth, out float elevation)
		{
			var right = V3Ops.Normalized(V3Ops.Cross(turretUp, forward));
			var up = V3Ops.Cross(forward, right);
			var offset = point - centre;
			float sideways = V3.Dot(offset, right);
			float ahead = V3.Dot(offset, forward);
			azimuth = (float)Math.Atan2(sideways, ahead) * V3Ops.Rad2Deg;
			float horizontal = (float)Math.Sqrt(sideways * sideways + ahead * ahead);
			elevation = (float)Math.Atan2(V3.Dot(offset, up), horizontal) * V3Ops.Rad2Deg;
		}

		// Left emitter covers az <= 0, the right one the rest.
		internal static bool InSector(
			float azimuth,
			float elevation,
			float sectorAzimuth,
			float sectorElevation,
			bool leftWorks,
			bool rightWorks)
		{
			if (Math.Abs(elevation) > sectorElevation || Math.Abs(azimuth) > sectorAzimuth) return false;
			return azimuth <= 0f ? leftWorks : rightWorks;
		}
	}
}
