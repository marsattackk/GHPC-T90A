using System.Collections.Generic;
using GHPC.Equipment;
using MelonLoader;
using UnityEngine;

namespace T90A
{
	internal static class ArmorValues
	{
		internal class Entry
		{
			public string Kind;
			public string Name;
			public float Ke, Ce;
			public float KeAfter, CeAfter;
			public bool Era;
			public bool AngleMatters;
			public bool Variable;
			public float DepthMm;
			public bool DrawnOnly;
			public float Weight = 1f;
			public string Xray;
			public ArmorCodexScriptable Material;
		}

		internal static readonly Dictionary<string, Entry> Table = new Dictionary<string, Entry>
		{
			["era"] = new Entry
			{
				Kind = "era", Name = "Kontakt-5 ERA", Ke = 190f, Ce = 550f, Era = true, AngleMatters = false, Xray = "era",
				KeAfter = 0f, CeAfter = 0f,
			},
			["composite"] = new Entry
			{
				Kind = "composite", Name = "Turret insert (reflective plates)", Ke = 391f, Ce = 535f, AngleMatters = false,
			},
			["pkg_plate"] = new Entry
			{
				Kind = "pkg_plate", Name = "cheek pack: SK-2Sh 50 mm", Variable = true, AngleMatters = true, Weight = 1f, Xray = "sk2sh",
			},
			["pkg_layer"] = new Entry
			{
				Kind = "pkg_layer", Name = "cheek pack: rubber + 2x4 mm", Variable = true, AngleMatters = true, Weight = 8.6f / 14f,
			},
			["glacis_nera"] = new Entry
			{
				Kind = "glacis_nera", Name = "Glacis rubber + 3 mm", Variable = true, AngleMatters = true, Weight = 3.5f / 8f,
			},
			["glacis_plate"] = new Entry
			{
				Kind = "glacis_plate", Name = "Glacis middle plate (BTK-1Sh 60)", Variable = true, AngleMatters = true, Weight = 1f, Xray = "btk1sh",
			},
			["glacis_pp"] = new Entry
			{
				Kind = "glacis_pp", Name = "Glacis polypropylene, 10 mm", Variable = true, AngleMatters = true, Weight = 0.1f, Xray = "polypropylene",
			},
			["xray_rubber"] = new Entry { Kind = "xray_rubber", Name = "rubber (x-ray only)", DrawnOnly = true, Xray = "rubber"},
			["xray_thin"] = new Entry { Kind = "xray_thin", Name = "thin steel plate (x-ray only)", DrawnOnly = true, Xray = "thin"},
			["liner"] = new Entry
			{
				Kind = "liner", Name = LinerName, Ke = 0.1f, Ce = 0.1f, AngleMatters = true, Variable = true, Xray = "lining",
			},
			["screen"] = new Entry
			{
				Kind = "screen", Name = "rubber-fabric side screen", Ke = 10f, Ce = 10f, AngleMatters = false,
			},
			["steel_front"] = Steel("steel_front", "turret front plate (cheek outer wall)", 65f),
			["steel_inner"] = Steel("steel_inner", "turret cheek inner wall", 150f),
			["steel_side"] = Steel("steel_side", "turret side", 70f),
			["steel_sidefwd"] = Steel("steel_sidefwd", "turret side, forward part", 80f),
			["steel_rear"] = Steel("steel_rear", "turret rear / bustle", 76f),
			["steel_roof"] = Steel("steel_roof", "turret roof", 45f),
			["steel_roofcav"] = Steel("steel_roofcav", "turret roof over the cavity", 60f),
			["steel_under"] = Steel("steel_under", "turret bustle floor", 45f),
			["mantlet"] = new Entry
			{
				Kind = "mantlet", Name = "gun mantlet", Ke = 420f, Ce = 505f, AngleMatters = true, Variable = true, DepthMm = MantletDepthMm,
			},
			["steel_base"] = new Entry
			{
				Kind = "steel_base", Name = "turret bottom plate", Ke = SteelKe, Ce = SteelCe, AngleMatters = true, Variable = true,
			},
			["box"] = new Entry
			{
				Kind = "box", Name = "turret stowage box", Ke = 5f, Ce = 5f, AngleMatters = true,
			},
		};

		const float SteelKe = 1.05f, SteelCe = 1.0f;
		internal const float MantletDepthMm = 749f;

		static Entry Steel(string kind, string name, float mm) => new Entry
		{
			Kind = kind, Name = name, Ke = mm * SteelKe, Ce = mm * SteelCe, AngleMatters = true,
		};

		internal static bool IsSteel(string kind) => kind.StartsWith("steel_");

		internal static bool ShownInXray(string kind) => Table.TryGetValue(kind, out var e) && e.Xray != null || kind == "composite";

		internal static readonly Dictionary<string, (string name, Color colour)> XrayColours = new Dictionary<string, (string, Color)>
		{
			["era"] = ("t90a see-through ERA", new Color(0.90f, 0.15f, 0.70f, 0.5f)),
			["sk2sh"] = ("t90a see-through SK-2Sh plate", new Color(0.00f, 0.62f, 0.85f, 0.55f)),
			["btk1sh"] = ("t90a see-through BTK-1Sh plate", new Color(0.05f, 0.55f, 0.10f, 0.55f)),
			["rubber"] = ("t90a see-through rubber", new Color(0.85f, 0.08f, 0.08f, 0.35f)),
			["thin"] = ("t90a see-through thin steel plate", new Color(0.55f, 0.10f, 0.95f, 0.35f)),
			["polypropylene"] = ("t90a see-through polypropylene", new Color(0.45f, 1.00f, 0.55f, 0.55f)),
			["lining"] = ("t90a see-through lining", new Color(1.00f, 0.62f, 0.85f, 0.25f)),
		};
		internal const string XrayTemplate = "see-through teal gray";
		internal static readonly Dictionary<string, (string name, string xray)> GlacisKept = new Dictionary<string, (string, string)>
		{
			["Upper Front Plate"] = ("Glacis outer plate (BTK-1Sh 60)", null),
			["Backing Plate Armor.004"] = ("Glacis back plate (BTK-1Sh 50)", "btk1sh"),
		};

		internal static (float ke, float ce) CheekPerMm => SourceOnly ? (0.622f, 1.030f) : (0.871f, 1.181f);
		internal static (float ke, float ce) GlacisPerMm => SourceOnly ? (1.095f, 1.910f) : (1.367f, 2.454f);

		internal static bool IsFilling(string kind) => kind.StartsWith("pkg_") || kind.StartsWith("glacis_");

		internal static string PieceName(string kind, string piece)
		{
			var e = Table[kind];
			if (kind.StartsWith("pkg_") && piece.Length > 7 && piece.Substring(1, 4) == "_pkg" && int.TryParse(piece.Substring(5, 2), out int n))
				return $"{piece[0]} cheek pack {n}: {PackLayer(e)}";
			if (kind == "glacis_nera") return e.Name + (piece.EndsWith("front") ? ", front" : ", rear");
			return e.Name;
		}

		static string PackLayer(Entry e) => e.Name.Substring(e.Name.IndexOf(':') + 2);

		static MelonPreferences_Entry<string> _armourPreset;
		static bool SourceOnly => _armourPreset != null && _armourPreset.Value == "source-only";

		internal static void Config(MelonPreferences_Category cfg)
		{
			_armourPreset = cfg.CreateEntry("Armour preset", "game-scale", description: "game-scale (default: matched to GHPC's other tanks; turret 690/820, glacis 550/750 without ERA) or source-only (open-source figures; turret 575/750, glacis 500/650).");
		}

		internal static Entry Get(string kind)
		{
			var e = Table[kind];
			if (kind == "composite") { e.Ke = SourceOnly ? 276f : 391f; e.Ce = SourceOnly ? 465f : 535f; }
			if (IsFilling(kind))
			{
				var m = kind.StartsWith("pkg_") ? CheekPerMm : GlacisPerMm;
				e.Ke = m.ke * e.Weight; e.Ce = m.ce * e.Weight;
				e.Material = FillingMaterial(e, SourceOnly);
				return e;
			}
			if (e.DrawnOnly) return e;
			if (e.Material == null) e.Material = kind == "liner" ? LinerMaterial(e.Name)
				: IsSteel(kind) && e.Variable ? VariableSteelMaterial("T-90A welded turret steel (geometry)", e.Ke, e.Ce)
				: IsSteel(kind) ? SteelMaterial("T-90A welded turret steel", 313f, true)
				: kind == "box" ? SteelMaterial("T-90A stowage box sheet steel", 129f, false)
				: e.DepthMm > 0f ? MakeMaterial(e.Name, e.Kind, e.Ke / e.DepthMm, e.Ce / e.DepthMm)
				: MakeMaterial(e.Name, e.Kind, 1f, 1f);
			return e;
		}

		static readonly Dictionary<string, ArmorCodexScriptable> _steel = new Dictionary<string, ArmorCodexScriptable>();
		static ArmorCodexScriptable SteelMaterial(string name, float bhn, bool shatter)
		{
			if (_steel.TryGetValue(name, out var m)) return m;
			var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
			codex.name = name;
			codex.ArmorType = new ArmorType
			{
				Name = shatter ? "armor steel" : "mild steel", ThicknessSource = ArmorType.RhaSource.BHN, BHN = bhn,
				RhaeMultiplierKe = 1f, RhaeMultiplierCe = 1f, CanShatterLongRods = shatter, CanRicochet = true, NormalizesHits = true,
				HarderIsBetter = false, SpallAngleMultiplier = 1f, SpallPowerMultiplier = 1f, CrushThicknessModifier = 1f,
			};
			Object.DontDestroyOnLoad(codex);
			return _steel[name] = codex;
		}

		static ArmorCodexScriptable VariableSteelMaterial(string name, float ke, float ce)
		{
			if (_steel.TryGetValue(name, out var m)) return m;
			var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
			codex.name = name;
			codex.ArmorType = new ArmorType
			{
				Name = "armor steel", ThicknessSource = ArmorType.RhaSource.Multipliers, RhaeMultiplierKe = ke, RhaeMultiplierCe = ce, BHN = 313f,
				CanShatterLongRods = true, CanRicochet = true, NormalizesHits = true, HarderIsBetter = false,
				SpallAngleMultiplier = 1f, SpallPowerMultiplier = 1f, CrushThicknessModifier = 1f,
			};
			Object.DontDestroyOnLoad(codex);
			return _steel[name] = codex;
		}

		static readonly Dictionary<(string, bool), ArmorCodexScriptable> _filling = new Dictionary<(string, bool), ArmorCodexScriptable>();
		static ArmorCodexScriptable FillingMaterial(Entry e, bool sourceOnly)
		{
			if (_filling.TryGetValue((e.Kind, sourceOnly), out var m)) return m;
			bool steel = e.Kind == "pkg_plate" || e.Kind == "glacis_plate";
			var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
			codex.name = "T-90A " + e.Name + (sourceOnly ? " (source-only)" : "");
			codex.ArmorType = new ArmorType
			{
				Name = e.Name, ThicknessSource = ArmorType.RhaSource.Multipliers, RhaeMultiplierKe = e.Ke, RhaeMultiplierCe = e.Ce,
				CanRicochet = false, NormalizesHits = false, CanShatterLongRods = steel, HarderIsBetter = false, BHN = steel ? 450f : 100f,
				SpallAngleMultiplier = 0.5f, SpallPowerMultiplier = 0.5f, CrushThicknessModifier = 1f,
			};
			Object.DontDestroyOnLoad(codex);
			return _filling[(e.Kind, sourceOnly)] = codex;
		}

		internal const string LinerName = "T-90A anti-radiation lining";

		static ArmorCodexScriptable LinerMaterial(string name)
		{
			var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
			codex.name = name;
			codex.ArmorType = new ArmorType
			{
				Name = name, ThicknessSource = ArmorType.RhaSource.Multipliers, RhaeMultiplierKe = 0.1f, RhaeMultiplierCe = 0.1f,
				SpallAngleMultiplier = 0.33f, SpallPowerMultiplier = 0.9f, CanShatterLongRods = false, CanRicochet = false,
				NormalizesHits = false, HarderIsBetter = true, BHN = 2f, CrushThicknessModifier = 0.05f,
			};
			Object.DontDestroyOnLoad(codex);
			return codex;
		}

		static ArmorCodexScriptable MakeMaterial(string name, string kind, float ke, float ce)
		{
			var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
			codex.name = "T-90A " + name;
			codex.ArmorType = new ArmorType
			{
				Name = name,
				ThicknessSource = ArmorType.RhaSource.Multipliers,
				RhaeMultiplierKe = ke,
				RhaeMultiplierCe = ce,
				CanRicochet = kind == "composite",
				CanShatterLongRods = kind == "composite",
				NormalizesHits = true,
				SpallAngleMultiplier = kind == "composite" ? 0.5f : 1f,
				SpallPowerMultiplier = kind == "composite" ? 0.5f : 1f,
				CrushThicknessModifier = 1f,
			};
			Object.DontDestroyOnLoad(codex);
			return codex;
		}
	}
}
