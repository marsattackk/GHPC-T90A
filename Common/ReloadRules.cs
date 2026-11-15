using System;

namespace T90A.Common
{
	internal static class ReloadRules
	{
		internal enum Act { Play, Hold, Seek }

		internal static int CycleIndex(float seconds, float[] lengths)
		{
			int best = 0;
			for (int i = 1; i < lengths.Length; i++)
				if (Math.Abs(seconds - lengths[i]) < Math.Abs(seconds - lengths[best])) best = i;
			return best;
		}

		internal static float ClipTarget(float t, float T, float L, float[] holds, out bool holding)
		{
			holding = false;
			if (t < 0f) t = 0f;
			if (T <= L || holds == null || holds.Length == 0)
				return Math.Min(L, T <= L ? L - Math.Max(0f, T - t) : t);
			float each = (T - L) / holds.Length, elapsed = 0f, prev = 0f;
			foreach (var hold in holds)
			{
				float run = hold - prev;
				if (t < elapsed + run) return prev + (t - elapsed);
				elapsed += run;
				if (t < elapsed + each)
				{
					holding = true;
					return hold;
				}
				elapsed += each;
				prev = hold;
			}
			return Math.Min(L, prev + (t - elapsed));
		}

		internal static Act Decide(float target, bool holding, float position, float tolerance)
		{
			if (holding || position > target + tolerance) return Act.Hold;
			return target > position + tolerance ? Act.Seek : Act.Play;
		}

		internal static float ForeignFactor(float liveWork, float storedWork)
		{
			if (storedWork <= 0f || liveWork <= 0f || float.IsNaN(liveWork) || float.IsInfinity(liveWork)) return 1f;
			float k = liveWork / storedWork;
			return Math.Abs(k - 1f) < 0.001f ? 1f : k;
		}

		internal static float[] CueTimes(float[] stageSeconds, float T)
		{
			var cues = new float[stageSeconds.Length];
			float total = 0f;
			foreach (var seconds in stageSeconds) total += seconds;
			float elapsed = 0f;
			for (int i = 0; i < stageSeconds.Length; i++)
			{
				cues[i] = total > 0f ? elapsed * T / total : 0f;
				elapsed += stageSeconds[i];
			}
			return cues;
		}

		internal static bool Audible(bool ownUnit, float distance, float maxDistance) => ownUnit || distance <= maxDistance;
	}
}
