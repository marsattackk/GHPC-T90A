using System.Collections;
using System.IO;
using System.Linq;
using GHPC.State;
using GHPC.Vehicle;
using MelonLoader;
using MelonLoader.Utils;
using T90A.Common;
using UnityEngine;

[assembly: MelonInfo(typeof(T90A.T90AMod), "T-90A", "0.4.1", "marsattackk")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace T90A
{
	public class T90AMod : MelonMod
	{
		internal const string UnitKey = "T90A";

		internal static GameObject Visual, Armor;
		internal static VanillaEdits Edits;
		internal static MaterialRoles Roles;
		internal static RigMap Rig;
		internal static Vector3[] SmokePoints;

		internal const string FriendlyName = "T-90A";
		const string BaseUnit = "T72M1";
		const string ArmyFrom = "T80B";
		internal const int CoaxRounds = 2000;
		internal const string NumberPosition = "rear lamp";

		internal static MelonPreferences_Entry<bool> Enabled;
		internal static CampaignConfig Campaign;
		internal static MelonPreferences_Entry<string> NightSightChoice;
		internal static MelonPreferences_Entry<bool> EssaElectronicZoom;
		internal static MelonPreferences_Entry<string> Apfsds;
		internal static MelonPreferences_Entry<bool> WeatheredPaint;
		internal static MelonPreferences_Entry<bool> ShtoraOn;
		internal static MelonPreferences_Entry<bool> ShtoraAutomatic;
		internal static MelonPreferences_Entry<bool> ShtoraTone;
		internal static MelonPreferences_Entry<bool> ShtoraJammerAtSpawn;
		internal static MelonPreferences_Entry<bool> ShtoraPanel;
		internal static MelonPreferences_Entry<bool> ShtoraPanelAlways;
		internal static MelonPreferences_Entry<bool> KordCases;

		const string Repo = "marsattackk/GHPC-T90A";
		static readonly SettingsMigration[] Migrations = { };

		static readonly string[] MenuScenes = { "MainMenu2_Scene", "MainMenu2-1_Scene", "LOADER_MENU", "LOADER_INITIAL", "t64_menu", "leo2_menu" };

		public override void OnInitializeMelon()
		{
			UnityErrors.Install(typeof(T90AMod).Namespace);
			var cfg = MelonPreferences.CreateCategory("T-90A");
			Enabled = cfg.CreateEntry("Enabled", true, description: "Adds the T-90A as its own unit (Instant Action, campaign roster, ATAC key \"T90A\").");
			Campaign = CampaignConfig.Create(cfg, "T-90A", 0.5f, 2);
			Apfsds = cfg.CreateEntry("APFSDS", "3BM42", description: "Default APFSDS where the game does not choose one (the mission customizer's ammo dropdown and the campaign's ammo supply override it): 3BM42 Mango (default; the T-90A's stock round) or 3BM60 Svinets-2 (a 2010s round, for T-90As with the long-round autoloader of 2006).");
			NightSightChoice = cfg.CreateEntry("Gunner night sight", NightSight.Essa, description: "ESSA = thermal sight, T-90A from 2006 (default); Buran-M = 3rd-gen image intensifier of the first T-90As (obr. 2004).");
			EssaElectronicZoom = cfg.CreateEntry("ESSA electronic zoom", true, description: "ESSA x2 electronic zoom (1.125 deg, half the thermal resolution); not fitted on every tank.");
			WeatheredPaint = cfg.CreateEntry("Weathered paint", true, description: "Dirt, wear and rust on the paint (default); false = clean factory paint. For T-90As spawned after the change.");
			ShtoraOn = cfg.CreateEntry("Shtora-1", true, description: "Shtora-1 (default): the IR jammer against wire-guided missiles, the laser warning on the commander's panel and the automatic 3D17 aerosol screens. false = none of it: the smoke launchers fire the game's ordinary smoke salvo. For T-90As spawned after the change.");
			ShtoraAutomatic = cfg.CreateEntry("Shtora-1 automatic screens", true, description: "true (default): a laser from the front fires an aerosol screen toward it by itself (automatic mode). false = semi-automatic mode: the warning and the panel only, the screen is fired by hand (vehicle menu: Shtora-1, or the smoke key). For T-90As spawned after the change.");
			ShtoraTone = cfg.CreateEntry("Shtora-1 alarm tone", true, description: "true (default): the laser warning sounds a tone with the panel's lamps. false = the lamps only, no sound. For T-90As spawned after the change.");
			ShtoraJammerAtSpawn = cfg.CreateEntry("Shtora-1 jammer on at spawn", true, description: "true (default): the IR jammer is on from the start. false = off until you switch it on (vehicle menu: Shtora-1), as the real crew does near a threat; AI crews still switch it on in combat. For T-90As spawned after the change.");
			ShtoraPanel = cfg.CreateEntry("Shtora-1 panel on screen", true, description: "true (default): the laser warning shows the commander's control panel at the bottom right. false = no panel, the tone only. For T-90As spawned after the change.");
			ShtoraPanelAlways = cfg.CreateEntry("Shtora-1 panel always on screen", false, description: "false (default): the commander's control panel comes up on a laser warning and goes away a few seconds after. true = it stays on screen the whole time you're in the T-90A, the warning lamp lighting up on it. Either way, looking through a sight it is drawn smaller, in the black strip under the picture. For T-90As spawned after the change.");
			KordCases = cfg.CreateEntry("Kord spent cases", true, description: "true (default): the Kord throws its spent cases out of the chute, and they lie on the tank until it tilts or brakes hard. false = no cases (the belt still feeds). For T-90As spawned after the change.");
			ArmorValues.Config(cfg);
			UpdateCheck.Config(cfg, FriendlyName, Info.Version, Repo);
			PortSettings.Finish(cfg, Info.Version, Migrations);

			if (!Enabled.Value) return;
			var bundle = BundleAssets.Open(Path.Combine(MelonEnvironment.ModsDirectory, "T90A", "t90a"), "T-90A");
			Visual = BundleAssets.Prefab(bundle, "T90A_visual", "T-90A", required: true);
			Armor = BundleAssets.Prefab(bundle, "T90A_armor", "T-90A", required: false, ifMissing: "armour will not be changed.");
			if (Visual != null) MelonLogger.Msg("Loaded T-90A visuals" + (Armor != null ? " and armour." : "."));
			if (bundle != null)
			{
				var edits = bundle.LoadAsset<TextAsset>(VanillaEdits.AssetName);
				if (edits != null) Edits = VanillaEdits.Parse(edits.text);
				else MelonLogger.Warning("T-90A: bundle has no vanilla_edits; the kept T-72M1 armour stays as it is.");
				var roles = bundle.LoadAsset<TextAsset>(MaterialRoles.AssetName);
				if (roles != null) Roles = MaterialRoles.Parse(roles.text);
				else MelonLogger.Warning("T-90A: bundle has no material roles; glass keeps the bundle's own materials.");
				var rig = bundle.LoadAsset<TextAsset>(RigMap.AssetName);
				if (rig != null) Rig = RigMap.Parse(rig.text);
				else MelonLogger.Warning("T-90A: bundle has no rig map; the hatches stay closed.");
				SmokePoints = SmokeLaunchers.Load(bundle);
				if (SmokePoints == null) MelonLogger.Warning("T-90A: bundle has no smoke launch points; the smoke grenades leave from the T-72's positions.");
			}

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
			PortAmmo.S = T90AAmmo.Settings();
			ShowcaseSpawner.Add(new ShowcaseSpawner.Entry { Key = UnitKey, Name = () => FriendlyName, Anchors = new[] { "T80B", "T72M1" } });
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
			if (MenuScenes.Contains(sceneName))
				DonorParts.WarmAtMenu(FriendlyName,
					() => CrewVoices.Donor(FriendlyName),
					() => NightSight.BuranDonor(),
					() => RemoteMg.Donor.Get(FriendlyName),
					() => ThermalSight.WarmDonor(FriendlyName),
					() => VanillaMaterials.Warm(Roles),
					() => ShtoraScreen.WarmDonors(Conversion.Screens));
			if (!MenuScenes.Contains(sceneName))
				StateController.RunOrDefer(GameState.PlayerReady, ConvertAll, GameStatePriority.Low);
		}

		static IEnumerator ConvertAll(GameState _)
		{
			foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
			{
				if (v.UniqueName == UnitKey) Conversion.Convert(v);
			}
			yield break;
		}
	}
}
