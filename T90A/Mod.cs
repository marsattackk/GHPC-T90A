using System.Collections;
using System.IO;
using System.Linq;
using GHPC.State;
using GHPC.Vehicle;
using MelonLoader;
using MelonLoader.Utils;
using T90A.Common;
using UnityEngine;

[assembly: MelonInfo(typeof(T90A.T90AMod), "T-90A", "0.3.0", "marsattackk")]
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
		internal static MelonPreferences_Entry<bool> InCampaign;
		internal static MelonPreferences_Entry<float> CampaignPrevalence;
		internal static MelonPreferences_Entry<int> CampaignAfterMissions;
		internal static MelonPreferences_Entry<string> NightSightChoice;
		internal static MelonPreferences_Entry<bool> EssaElectronicZoom;
		internal static MelonPreferences_Entry<string> Apfsds;
		internal static MelonPreferences_Entry<bool> WeatheredPaint;

		static readonly string[] MenuScenes = { "MainMenu2_Scene", "MainMenu2-1_Scene", "LOADER_MENU", "LOADER_INITIAL", "t64_menu", "leo2_menu" };

		public override void OnInitializeMelon()
		{
			var cfg = MelonPreferences.CreateCategory("T-90A");
			Enabled = cfg.CreateEntry("Enabled", true, description: "Adds the T-90A as its own unit (Instant Action, campaign roster, ATAC key \"T90A\").");
			InCampaign = cfg.CreateEntry("Add to campaign roster", true, description: "Adds the T-90A to the Soviet campaign roster.");
			CampaignPrevalence = cfg.CreateEntry("Campaign prevalence", 0.5f, description: "How common it is compared to other tanks (1 = as common as a typical tank).");
			CampaignAfterMissions = cfg.CreateEntry("Campaign unlock after missions", 2);
			Apfsds = cfg.CreateEntry("APFSDS", "3BM42", description: "Default APFSDS where the game does not choose one (the mission customizer's ammo dropdown and the campaign's ammo supply override it): 3BM42 Mango (default; the T-90A's stock round) or 3BM60 Svinets-2 (a 2010s round, for T-90As with the long-round autoloader of 2006).");
			NightSightChoice = cfg.CreateEntry("Gunner night sight", NightSight.Essa, description: "ESSA = thermal sight, T-90A from 2006 (default); Buran-M = 3rd-gen image intensifier of the first T-90As (obr. 2004).");
			EssaElectronicZoom = cfg.CreateEntry("ESSA electronic zoom", true, description: "ESSA x2 electronic zoom (1.125 deg, half the thermal resolution); not fitted on every tank.");
			WeatheredPaint = cfg.CreateEntry("Weathered paint", true, description: "Dirt, wear and rust on the paint (default); false = clean factory paint. For T-90As spawned after the change.");
			ArmorValues.Config(cfg);

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

			UnitRegistry.Add(new UnitDef
			{
				Key = UnitKey,
				FriendlyName = () => FriendlyName,
				BaseUnit = () => BaseUnit,
				ArmyFrom = () => ArmyFrom,
				Enabled = () => Enabled.Value,
				InCampaign = () => InCampaign.Value,
				CampaignPrevalence = () => CampaignPrevalence.Value,
				CampaignAfterMissions = () => CampaignAfterMissions.Value,
				OnSpawn = v => Conversion.Convert(v, atSpawn: true),
			});
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
			if (InCampaign.Value) MelonCoroutines.Start(UnitRegistry.AddToCampaignRoster());
			ShowcaseSpawner.OnSceneLoaded(sceneName);
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
