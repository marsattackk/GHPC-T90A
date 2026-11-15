using System;
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
					string otherwise = step.Otherwise != null ? " (" + step.Otherwise + ")" : "";
					MelonLogger.Error($"{tag} {step.What} failed{otherwise}: {exception}");
				}
			}
		}
	}
}
