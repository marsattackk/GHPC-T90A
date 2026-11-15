using System;
using System.Collections.Generic;

namespace T90A.Common
{
	// Unity's Vector3 and Mathf sums written out on V3, same formulas and the same float order, so the tests get what the
	// game gets.
	internal static class V3Ops
	{
		internal const float Deg2Rad = 0.0174532924f;
		internal const float Rad2Deg = 57.29578f;

		internal static float Clamp(float value, float min, float max)
		{
			if (value < min) value = min;
			else if (value > max) value = max;
			return value;
		}

		internal static float Max(float a, float b) => a > b ? a : b;

		internal static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

		internal static float SqrMagnitude(V3 v) => v.x * v.x + v.y * v.y + v.z * v.z;

		internal static V3 Normalized(V3 v)
		{
			float magnitude = (float)Math.Sqrt(SqrMagnitude(v));
			return magnitude > 1E-05f ? new V3(v.x / magnitude, v.y / magnitude, v.z / magnitude) : new V3(0f, 0f, 0f);
		}

		internal static V3 ProjectOnPlane(V3 vector, V3 normal)
		{
			float normalSquared = V3.Dot(normal, normal);
			if (normalSquared < float.Epsilon) return vector;
			float along = V3.Dot(vector, normal);
			return new V3(
				vector.x - normal.x * along / normalSquared,
				vector.y - normal.y * along / normalSquared,
				vector.z - normal.z * along / normalSquared);
		}

		internal static float Angle(V3 from, V3 to)
		{
			float denominator = (float)Math.Sqrt(SqrMagnitude(from) * SqrMagnitude(to));
			if (denominator < 1E-15f) return 0f;
			float cosine = Clamp(V3.Dot(from, to) / denominator, -1f, 1f);
			return (float)Math.Acos(cosine) * Rad2Deg;
		}

		internal static bool IsZero(V3 v) => SqrMagnitude(v) < 9.99999944E-11f;
	}

	// The fire gate and the aim clamp without Unity. GunnerFcs reads the transforms and hands them in.
	internal static class GunnerFcsMath
	{
		internal const float DefaultGateAngle = 0.1f;

		internal struct Drive
		{
			public V3 Right, Forward;
			public bool Traverse;
			public Drive(V3 right, V3 forward, bool traverse)
			{
				Right = right;
				Forward = forward;
				Traverse = traverse;
			}
		}

		// How far the gun is off the lay in the elevation plane, worst drive counts. A traverse drive is skipped because it
		// would read the whole aiming angle.
		internal static float LaidOff(V3 lay, List<Drive> drives)
		{
			if (V3Ops.IsZero(lay)) return 0f;
			float worst = 0f;
			for (int i = 0; i < drives.Count; i++)
			{
				var drive = drives[i];
				if (drive.Traverse) continue;
				float angle = V3Ops.Angle(V3Ops.ProjectOnPlane(lay, drive.Right), V3Ops.ProjectOnPlane(drive.Forward, drive.Right));
				if (angle > worst) worst = angle;
			}
			return worst;
		}

		internal static float GateAngle(float fireGateAngle) => fireGateAngle > 0f ? fireGateAngle : DefaultGateAngle;

		internal static bool GateShut(float off, float fireGateAngle) => off > GateAngle(fireGateAngle);

		internal static bool HasAimLimits(float minElevation, float maxElevation, float azimuth)
		{
			bool noElevation = minElevation * minElevation + maxElevation * maxElevation < 9.99999944E-11f;
			return !(noElevation && azimuth <= 0f);
		}

		internal static float StopAzimuth(IEnumerable<KeyValuePair<bool, float>> sights, float fallback)
		{
			foreach (var sight in sights)
				if (sight.Key) return sight.Value > 0f ? sight.Value : fallback;
			return fallback;
		}

		internal static float Yaw(V3 direction) => (float)Math.Atan2(direction.x, direction.z) * V3Ops.Rad2Deg;

		// False when the aim is inside the limits. A 0 elevation limit is open on that side. An aim other code already put
		// further out in azimuth is only kept from growing, never pulled back in.
		internal static bool ClampAim(
			V3 direction,
			V3 before,
			float minElevation,
			float maxElevation,
			float azimuth,
			out V3 clamped)
		{
			clamped = default;
			float elevation = (float)Math.Asin(V3Ops.Clamp(direction.y, -1f, 1f)) * V3Ops.Rad2Deg;
			float lowest = minElevation != 0f ? minElevation : -90f;
			float highest = maxElevation != 0f ? maxElevation : 90f;
			float yaw = Yaw(direction);
			float limitedYaw = yaw;
			if (azimuth > 0f)
			{
				float azimuthLimit = V3Ops.Max(azimuth, Math.Abs(Yaw(before)));
				limitedYaw = V3Ops.Clamp(yaw, -azimuthLimit, azimuthLimit);
			}
			if (elevation >= lowest && elevation <= highest && limitedYaw == yaw) return false;
			float elevationRadians = V3Ops.Clamp(elevation, lowest, highest) * V3Ops.Deg2Rad;
			var flat = limitedYaw != yaw
				? new V3(
					(float)Math.Sin(limitedYaw * V3Ops.Deg2Rad),
					0f,
					(float)Math.Cos(limitedYaw * V3Ops.Deg2Rad))
				: new V3(direction.x, 0f, direction.z);
			flat = V3Ops.SqrMagnitude(flat) > 1e-8f ? V3Ops.Normalized(flat) : new V3(0f, 0f, 1f);
			float flatPart = (float)Math.Cos(elevationRadians);
			clamped = new V3(flat.x * flatPart, (float)Math.Sin(elevationRadians), flat.z * flatPart);
			return true;
		}
	}
}
