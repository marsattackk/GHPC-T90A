using MelonLoader;

namespace T90A.Common
{
	internal static class PaintFinish
	{
		internal static MelonPreferences_Entry<bool> Create(MelonPreferences_Category cfg, string unitName) =>
			cfg.CreateEntry("Weathered paint", true, description: $"Dirt, wear and rust on the paint (default); false = clean factory paint. For {unitName}s spawned after the change.");

		internal static void Apply(MelonPreferences_Entry<bool> weathered, bool forceClean = false)
		{
			VehiclePaint.Clean = (weathered != null && !weathered.Value) || forceClean;
		}
	}
}
