using System;
using System.Collections.Generic;
using MelonLoader;

namespace T90A.Common
{
	// LiveRoundBatchHandler doesn't catch the unborn rounds' first update, one throw from a patch there kills the batch
	// every frame.
	internal static class PatchGuard
	{
		internal const int Logged = 3;
		static readonly Dictionary<string, int> Failures = new Dictionary<string, int>();

		internal static void Failed(string feature, Exception e)
		{
			try
			{
				int n;
				lock (Failures)
				{
					n = Failures.TryGetValue(feature, out int had) ? had : 0;
					Failures[feature] = ++n;
				}
				if (n <= Logged) MelonLogger.Error($"{feature} failed (the game carries on without it this time; {n} of {Logged} logged): {e}");
				else if (n == Logged + 1) MelonLogger.Warning($"{feature} keeps failing ({n} times); not logging it again this session.");
			}
			catch { }
		}
	}
}
