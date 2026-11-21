using System;
using MelonLoader;

namespace T90A.Common
{
	// Needs [assembly: HarmonyDontPatchAll] in the mod. Without it MelonLoader has patched the lot before Enabled is read.
	internal static class PatchClasses
	{
		internal static int Apply(MelonBase mod)
		{
			int failed = 0;
			foreach (var type in mod.MelonAssembly.Assembly.GetValidTypes())
			{
				try { mod.HarmonyInstance.CreateClassProcessor(type, allowUnannotatedType: false).Patch(); }
				catch (Exception exception)
				{
					failed++;
					MelonLogger.Error($"{type.FullName} could not be patched (a game update?), so that part of the mod is off: {exception}");
				}
			}
			return failed;
		}
	}
}
