using GHPC.Camera;

namespace T90A.Common
{
	internal static class SightMotion
	{
		internal const float Blur = 0.08f;
		internal const float Shake = 0.12f;

		internal static void Stabilised(CameraSlot slot, bool keepBlur = false)
		{
			if (slot == null) return;
			if (!keepBlur) slot.VibrationBlurScale = Blur;
			slot.VibrationShakeMultiplier = Shake;
		}
	}
}
