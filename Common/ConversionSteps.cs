using System;
using System.Collections.Generic;
using MelonLoader;

namespace T90A.Common
{
	internal sealed class ConversionStep<T>
	{
		internal string What;
		internal string Otherwise;
		internal Action<T> Run;
		// Spawn only: Unit builds its weapon, camera and rack lists straight after spawn and a part added later
		// never makes it in.
		internal bool SpawnOnly;
		internal string Late;
	}

	internal static class ConversionSteps
	{
		internal static void Run<T>(
			string tag,
			ConversionStep<T>[] steps,
			T state,
			string unit,
			bool atSpawn,
			Func<ConversionStep<T>, bool> keep = null)
		{
			foreach (var step in steps)
			{
				if (keep != null && keep(step)) continue;
				if (step.SpawnOnly && !atSpawn)
				{
					if (step.Late != null) MelonLogger.Warning(string.Format(step.Late, unit));
					continue;
				}
				try { step.Run(state); }
				catch (Exception exception)
				{
					Failed(tag, step, unit, exception);
				}
			}
		}

		static readonly Dictionary<string, int> Failures = new Dictionary<string, int>();

		static void Failed<T>(string tag, ConversionStep<T> step, string unit, Exception exception)
		{
			string otherwise = step.Otherwise != null ? " (" + step.Otherwise + ")" : "";
			string key = tag + " " + step.What;
			int count = Failures.TryGetValue(key, out int had) ? had + 1 : 1;
			Failures[key] = count;
			if (count == 1)
			{
				MelonLogger.Error($"{tag} {step.What} failed{otherwise}: {exception}");
				return;
			}
			string why = $"{exception.GetType().Name}: {exception.Message}";
			MelonLogger.Error($"{tag} {step.What} failed again on {unit}{otherwise}: {why} ({count} times)");
		}
	}
}
