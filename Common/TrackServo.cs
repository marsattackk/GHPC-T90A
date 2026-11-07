using System;

namespace T90A.Common
{
	internal sealed class TrackServo
	{
		internal float SlewRate = 5f;
		internal float PullIn = 0.1f;
		internal bool Integral = true;
		float _iYaw, _iPitch, _since;

		internal void Reset() { _iYaw = _iPitch = _since = 0f; }

		internal static float Wrap(float a)
		{
			a %= 360f;
			if (a > 180f) a -= 360f; else if (a < -180f) a += 360f;
			return a;
		}

		internal void Step(float eYaw, float ePitch, float dWantYaw, float dWantPitch, float dt, out float stepYaw, out float stepPitch)
		{
			if (dt <= 0f) { stepYaw = stepPitch = 0f; return; }
			_since += dt;
			stepYaw = One(Wrap(eYaw), Wrap(dWantYaw), dt, ref _iYaw);
			stepPitch = One(ePitch, dWantPitch, dt, ref _iPitch);
		}

		float One(float e, float dWant, float dt, ref float integral)
		{
			float ki = Integral ? 1f / (4f * PullIn * PullIn) : 0f;
			float r = e - dWant;
			// Only the error left over from last frame gets pulled in, take the whole lot and the target's move counts twice.
			float step = dWant + r * (1f - (float)Math.Exp(-dt / PullIn)) + ki * integral * dt;
			float max = SlewRate * dt;
			bool clamped = step > max || step < -max;
			if (clamped) step = step > 0f ? max : -max;
			// No integral while clamped or right after a capture, the pull onto the target winds it up and it overshoots.
			else if (ki > 0f && _since >= 3f * PullIn) integral = Clamp(integral + r * dt, -SlewRate / ki, SlewRate / ki);
			return step;
		}

		static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
	}
}
