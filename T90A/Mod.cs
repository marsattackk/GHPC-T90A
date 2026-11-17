using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using T90A.Common;
using UnityEngine;

[assembly: MelonInfo(typeof(T90A.T90AMod), "T-90A", "0.5.1", "marsattackk")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace T90A
{
	// Loads the bundle, registers the unit on the T-72M1 prefab and hangs the War Thunder model's HULL / TURRET / GUN on the base
	// rig's bones (the base's visuals off, paint, real axes). The armour goes on before the axes move; the Kord station, the
	// modules and Shtora-1 are fitted at spawn, the sights and voices once the unit has started (Conversion.Convert).
	public partial class T90AMod : MelonMod
	{
		internal const string UnitKey = "T90A";

		internal static GameObject Visual;   // the War Thunder model, the bundle's T90A_visual prefab
		internal static VanillaEdits Edits;   // the T-72M1's pieces switched off or changed (Resources\vanilla_edits.txt)
		internal static MaterialRoles Roles;   // material roles: the vanilla glass at spawn (Resources\material_roles.txt)
		internal static RigMap Rig;   // moving parts: hatches, the turret and gun axes (Resources\rig_map.txt)
		internal static Vector3[] SmokePoints;   // the smoke tubes' launch points (Resources\smoke_points.txt)

		internal const string FriendlyName = "T-90A";
		const string BaseUnit = "T72M1";   // registry key of the vanilla prefab "T72M1" (crew, interior, damage model, running gear)
		// The T-72M1 is NVA in GHPC, so the army comes off the T-80B.
		const string ArmyFrom = "T80B";
		// 2000 rounds for the PKT (Russian Wikipedia, «Т-90»; «Описание танка Т-90А», odetievbrony.ru).
		internal const int CoaxRounds = 2000;
		internal const string NumberPosition = "rear lamp";

		internal static MelonPreferences_Entry<bool> Enabled;
		internal static CampaignConfig Campaign;
		internal static MelonPreferences_Entry<string> Apfsds;
		internal static MelonPreferences_Entry<bool> ForceApfsds;
		internal static MelonPreferences_Entry<bool> FireWhenLaid;
		internal static MelonPreferences_Entry<bool> WeatheredPaint;
		internal static MelonPreferences_Entry<bool> KordCases;
		internal static MelonPreferences_Entry<bool> EraEffects;
		internal static MelonPreferences_Entry<string> NightSightChoice;
		internal static MelonPreferences_Entry<bool> EssaElectronicZoom;
		internal static MelonPreferences_Entry<bool> ShtoraOn;
		internal static MelonPreferences_Entry<bool> ShtoraAutomatic;
		internal static MelonPreferences_Entry<bool> ShtoraTone;
		internal static MelonPreferences_Entry<bool> ShtoraJammerAtSpawn;
		internal static MelonPreferences_Entry<float> ShtoraJamChance;
		internal static MelonPreferences_Entry<bool> ShtoraPanel;
		internal static MelonPreferences_Entry<bool> ShtoraPanelAlways;

		const string Repo = "marsattackk/GHPC-T90A";
		// A changed default needs a rule here. MelonLoader keeps the saved one.
		static readonly SettingsMigration[] Migrations =
		{
			SettingsMigration.Default("APFSDS", "0.4.2", "3BM42"),
			SettingsMigration.Default("Fire only when the gun is laid", "0.4.4", false),
		};

		public override void OnInitializeMelon()
		{
			BuildStamp.Log(Info.Name);
			UnityErrors.Install(typeof(T90AMod).Namespace);
			CreateSettings();
			if (!Enabled.Value) return;
			PortAmmo.S = Firepower.Settings();
			LoadData();
			RegisterUnit();
		}

		// The settings in MelonPreferences.cfg, then the file's version and its migrations.
		void CreateSettings()
		{
			var cfg = MelonPreferences.CreateCategory("T-90A");
			Enabled = cfg.CreateEntry(
				"Enabled",
				true,
				description: SettingText.Enabled);
			// The T-90A is the more common tank; the newer, rarer T-90M shows up half as often (0.25).
			Campaign = CampaignConfig.Create(cfg, FriendlyName, 0.5f, 2);
			Apfsds = cfg.CreateEntry(
				"APFSDS",
				"3BM60",
				description: SettingText.Apfsds);
			ForceApfsds = cfg.CreateEntry(
				"Force APFSDS",
				false,
				description: SettingText.ForceApfsds);
			NightSightChoice = cfg.CreateEntry(
				"Gunner night sight",
				NightSight.Essa,
				description: SettingText.NightSight);
			EssaElectronicZoom = cfg.CreateEntry(
				"ESSA electronic zoom",
				true,
				description: SettingText.EssaZoom);
			FireWhenLaid = cfg.CreateEntry(
				"Fire only when the gun is laid",
				true,
				description: SettingText.FireWhenLaid);
			WeatheredPaint = PaintFinish.Create(cfg, "T-90A");
			ShtoraOn = cfg.CreateEntry(
				"Shtora-1",
				true,
				description: SettingText.Shtora);
			ShtoraAutomatic = cfg.CreateEntry(
				"Shtora-1 automatic screens",
				true,
				description: SettingText.ShtoraAutomatic);
			ShtoraTone = cfg.CreateEntry(
				"Shtora-1 alarm tone",
				true,
				description: SettingText.ShtoraTone);
			ShtoraJammerAtSpawn = cfg.CreateEntry(
				"Shtora-1 jammer on at spawn",
				true,
				description: SettingText.ShtoraJammerAtSpawn);
			ShtoraJamChance = cfg.CreateEntry(
				"Shtora-1 jam chance",
				Countermeasures.JamChance,
				description: SettingText.ShtoraJamChance);
			ShtoraPanel = cfg.CreateEntry(
				"Shtora-1 panel on screen",
				true,
				description: SettingText.ShtoraPanel);
			ShtoraPanelAlways = cfg.CreateEntry(
				"Shtora-1 panel always on screen",
				false,
				description: SettingText.ShtoraPanelAlways);
			KordCases = cfg.CreateEntry(
				"Kord spent cases",
				true,
				description: SettingText.KordCases);
			// Off by default on the T-90A, as it has always shipped; on by default on the T-90M.
			EraEffects = cfg.CreateEntry(
				"ERA destruction effects",
				false,
				description: SettingText.EraEffects);
			Armour.Config(cfg);
			UpdateCheck.Config(cfg, FriendlyName, Info.Version, Repo);
			DevInit(cfg);
			PortSettings.Finish(cfg, Info.Version, Migrations);
		}

		// The bundle's prefabs and the text data in Resources\ (armour, base edits, rig, glass, ERA, smoke points).
		static void LoadData()
		{
			var bundle = BundleAssets.Open(Path.Combine(MelonEnvironment.ModsDirectory, "T90A", "t90a"), FriendlyName);
			Visual = BundleAssets.Prefab(bundle, "T90A_visual", FriendlyName, required: true);
			Armour.Load(bundle);
			if (Visual != null)
				MelonLogger.Msg($"Loaded {FriendlyName} visuals" + (Armour.Prefab != null && Armour.Values != null ? " and armour." : "."));
			DevLoadData(bundle);
			if (bundle == null) return;
			var roles = PortText.Load(bundle, MaterialRoles.AssetName);
			if (roles != null) Roles = MaterialRoles.Parse(roles);
			else MelonLogger.Warning($"{FriendlyName}: no material roles; glass keeps the bundle's own materials.");
			var rig = PortText.Load(bundle, RigMap.AssetName);
			if (rig != null) Rig = RigMap.Parse(rig);
			else MelonLogger.Warning($"{FriendlyName}: no rig map; the hatches stay closed.");
			EraBricks.LoadLinks(bundle);
			EraBricks.LoadGone(bundle);
			EraBricks.Look = () => EraEffects.Value;
			SmokePoints = SmokeLaunchers.Load(bundle);
			if (SmokePoints == null)
				MelonLogger.Warning($"{FriendlyName}: no smoke launch points; the smoke grenades leave from the T-72's positions.");
		}

		// The T-90A as a unit of its own: the game's unit lists, the campaign roster and the Showcase.
		static void RegisterUnit()
		{
			var unit = new UnitDef
			{
				Key = UnitKey,
				FriendlyName = () => FriendlyName,
				BaseUnit = () => BaseUnit,
				ArmyFrom = () => ArmyFrom,
				Enabled = () => Enabled.Value,
				OnSpawn = v => Conversion.Convert(v, atSpawn: true),
			};
			Campaign.Use(unit);
			UnitRegistry.Add(unit);
			UnitRegistry.TryRegister();
			ShowcaseSpawner.Add(new ShowcaseSpawner.Entry
			{
				Key = UnitKey,
				Name = () => FriendlyName,
				Anchors = new[] { ArmyFrom, BaseUnit }
			});
		}

		public override void OnSceneWasInitialized(int buildIndex, string sceneName)
		{
			if (!Enabled.Value) return;
			UnitRegistry.TryRegister();
		}

		public override void OnSceneWasLoaded(int buildIndex, string sceneName)
		{
			if (!Enabled.Value) return;
			UpdateCheck.OnSceneLoaded(MenuScenes.Contains(sceneName));
			UnitRegistry.StartCampaignRoster();
			RepairKitCompat.TryPatch(HarmonyInstance);
			ShowcaseSpawner.OnSceneLoaded(sceneName);
			// VanillaAssetUtil can destroy the prefabs after a mission, so grab the donor parts while they're still there.
			if (MenuScenes.Contains(sceneName))
				DonorParts.WarmAtMenu(FriendlyName,
					() => CrewVoices.Donor(FriendlyName),
					() => NightSight.BuranDonor(),
					() => RemoteMg.Donor.Get(FriendlyName),
					() => ThermalSight.WarmDonor(FriendlyName),
					() => VanillaMaterials.Warm(Roles),
					() => ShtoraScreen.WarmDonors(Countermeasures.Screens));
			if (!MenuScenes.Contains(sceneName))
				ConvertSweep.AtPlayerReady(UnitKey, v => Conversion.Convert(v));
			DevSceneLoaded(sceneName, MenuScenes.Contains(sceneName), HarmonyInstance);
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevInit(MelonPreferences_Category cfg);
		static partial void DevLoadData(AssetBundle bundle);
		static partial void DevSceneLoaded(string sceneName, bool menu, HarmonyLib.Harmony harmony);
	}
}
