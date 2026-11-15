using System;
using System.Collections.Generic;
using MelonLoader;

namespace T90A.Common
{
	// LiveRoundBatchHandler doesn't catch the first update of unborn rounds, and one throw from a patch there kills the
	// batch every frame. Patches on rounds log here three times, then go quiet.
	internal static class PatchGuard
	{
		internal const int Logged = 3;
		static readonly Dictionary<string, int> Failures = new Dictionary<string, int>();

		internal static void Failed(string feature, Exception e)
		{
			try
			{
				int count;
				lock (Failures)
				{
					count = Failures.TryGetValue(feature, out int had) ? had : 0;
					Failures[feature] = ++count;
				}
				if (count <= Logged)
					MelonLogger.Error($"{feature} failed (the game carries on without it this time; {count} of {Logged} logged): {e}");
				else if (count == Logged + 1)
					MelonLogger.Warning($"{feature} keeps failing ({count} times); not logging it again this session.");
			}
			catch { }
		}
	}
}
