using System;
using MelonLoader;

namespace T90A.Common
{
	// game-scale is matched to GHPC's other tanks and source-only uses the open-source figures. The bundle holds
	// one set and the other's worked out from it.
	internal static class ArmourPresets
	{
		internal const string GameScale = "game-scale", SourceOnly = "source-only";

		internal static MelonPreferences_Entry<string> Create(MelonPreferences_Category cfg, string description)
			=> cfg.CreateEntry("Armour preset", GameScale, description: description);

		internal static string Pick(MelonPreferences_Entry<string> entry)
		{
			string preset = entry?.Value ?? GameScale;
			return string.Equals(preset?.Trim(), SourceOnly, StringComparison.OrdinalIgnoreCase) ? SourceOnly : GameScale;
		}

		internal static DmArmour Scaled(DmArmour values, Func<DmArmour.Entry, (float ke, float ce)?> factor)
		{
			var armour = new DmArmour();
			foreach (var entry in values.Entries)
			{
				var copy = new DmArmour.Entry
				{
					Piece = entry.Piece,
					Mode = entry.Mode,
					Name = entry.Name,
					Material = entry.Material,
					Xray = entry.Xray,
					Source = entry.Source,
					Ke = entry.Ke,
					Ce = entry.Ce,
					KeAfter = entry.KeAfter,
					CeAfter = entry.CeAfter,
					AngleMatters = entry.AngleMatters,
					Slat = entry.Slat,
				};
				if (factor(copy) is (float kineticFactor, float chemicalFactor))
				{
					copy.Ke *= kineticFactor;
					copy.Ce *= chemicalFactor;
				}
				armour.Add(copy);
			}
			return armour;
		}

		internal sealed class Pair
		{
			readonly string _bundlePreset;
			readonly Func<DmArmour.Entry, (float ke, float ce)?> _toOther;
			DmArmour _from, _other;

			internal Pair(string bundlePreset, Func<DmArmour.Entry, (float ke, float ce)?> toOther)
			{
				_bundlePreset = bundlePreset;
				_toOther = toOther;
			}

			internal DmArmour For(DmArmour bundle, string preset)
			{
				if (bundle == null || preset == _bundlePreset) return bundle;
				if (!ReferenceEquals(bundle, _from))
				{
					_from = bundle;
					_other = Scaled(bundle, _toOther);
				}
				return _other;
			}
		}
	}
}
