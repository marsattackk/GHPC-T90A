using System.Collections.Generic;
using System.Linq;
using GHPC.Vehicle;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// The armour shapes come from the bundle and their numbers from Resources\dm_armour.txt. Switches off the T-72M1 pieces
	// War Thunder's model replaces, and scales the cheeks and glacis filling down for source-only.
	internal static partial class Armour
	{
		internal static GameObject Prefab;
		internal static DmArmour Values;
		internal static MelonPreferences_Entry<string> Preset;

		static readonly string[] BaseOff = { "Fire Wall" };

		internal static void Config(MelonPreferences_Category cfg)
		{
			Preset = ArmourPresets.Create(
				cfg,
				SettingText.ArmourPreset);
		}

		internal static void Load(AssetBundle bundle)
		{
			Prefab = BundleAssets.Prefab(
				bundle,
				"T90A_armor",
				T90AMod.FriendlyName,
				required: false,
				ifMissing: "armour will not be changed.");
			var values = PortText.Load(bundle, DmArmour.AssetName);
			if (values != null) Values = DmArmour.Parse(values);
			var edits = PortText.Load(bundle, VanillaEdits.AssetName);
			if (edits != null) T90AMod.Edits = VanillaEdits.Parse(edits);
			if (T90AMod.Edits != null) T90AMod.Edits.Off = T90AMod.Edits.Off.Union(BaseRigT72M1.ImportReplaces).Union(BaseOff).ToArray();
			if (Prefab != null && Values == null)
				MelonLogger.Warning($"{T90AMod.FriendlyName}: no armour values; the T-72's armour stays.");
			if (Prefab != null && T90AMod.Edits == null)
				MelonLogger.Warning($"{T90AMod.FriendlyName}: no vanilla_edits; the T-72M1's armour stays under the T-90A's.");
		}

		static bool _warned;

		internal static void Apply(Vehicle vehicle)
		{
			if (Prefab == null || Values == null) return;
			var problems = new List<string>();
			string preset = ArmourPresets.Pick(Preset);
			var values = Presets.For(Values, preset);
			DmArmourApply.Install(vehicle, Prefab, values, T90AMod.Edits, T90AMod.FriendlyName, problems);
			if (!_warned && problems.Count > 0)
			{
				_warned = true;
				MelonLogger.Warning($"{T90AMod.FriendlyName} armour: {problems.Count} piece(s) not found or left out "
					+ $"({string.Join("; ", problems)}); the armour may not match the model (game update?).");
			}
			DevApplied(vehicle, values, preset, problems);
		}

		// "Armour preset" source-only: cheeks 575 / 750 and glacis 500 / 650 without ERA, against the game-scale 690 / 820 and
		// 550 / 750 dm_armour.txt is calibrated to. Only the calibrated fillings change, each by one KE and one HEAT
		// factor, the ratio of the filling that gives the source-only figures to the one that gives the game-scale figures.
		static readonly string[] GlacisFilling = { "composite_armor_hull_01_dm", "turret_04_top_dm", "turret_05_front_dm" };
		// Cheek packages: the filling for 575 / 750 over the one for 690 / 820.
		const float PackageKe = 0.7478f / 1.0929f;
		const float PackageCe = 1.3193f / 1.5293f;
		// Glacis middle layers: the filling for 500 / 650 over the one for 550 / 750.
		const float GlacisKe = 1.0560f / 1.3315f;
		const float GlacisCe = 1.9632f / 2.5141f;
		static readonly ArmourPresets.Pair Presets = new ArmourPresets.Pair(ArmourPresets.GameScale, entry =>
			entry.Variable && entry.Piece.Contains("_pkg") ? (PackageKe, PackageCe)
			: entry.Variable && System.Array.IndexOf(GlacisFilling, entry.Piece) >= 0 ? (GlacisKe, GlacisCe)
			: ((float, float)?)null);

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevApplied(Vehicle vehicle, DmArmour values, string preset, List<string> problems);
	}
}
