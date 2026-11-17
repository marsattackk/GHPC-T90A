namespace T90A
{
	// The settings' descriptions in MelonPreferences.cfg. Static fields, not consts: tools\check-settings.ps1 reads
	// every const as a possible default.
	internal static partial class SettingText
	{
		static readonly string AfterChange = " For T-90As spawned after the change.";

		internal static readonly string Enabled =
			"Adds the T-90A as its own unit (Instant Action, campaign roster, ATAC key \"T90A\").";

		internal static readonly string Apfsds =
			"Default APFSDS where the game does not choose one (the mission customizer's ammo dropdown and the campaign's ammo"
			+ " supply override it): 3BM60 Svinets-2 (default; a 2010s round, for T-90As with the long-round autoloader of 2006)"
			+ " or 3BM42 Mango (the T-90A's stock round at its service entry).";

		internal static readonly string ForceApfsds =
			"false (default): the campaign picks each T-90A's APFSDS from the army's ammo supply (3BM42 Mango, and from 4 missions on 3BM60"
			+ " Svinets-2, more often as the logistics score rises). true = every T-90A carries the APFSDS setting's round in the campaign"
			+ " too. In Instant Action the mission customizer's ammo dropdown still decides."
			+ AfterChange;

		internal static readonly string NightSight =
			"ESSA = thermal sight, T-90A from 2006 (default); Buran-M = 3rd-gen image intensifier of the first T-90As (obr. 2004).";

		internal static readonly string EssaZoom =
			"ESSA x2 electronic zoom (1.125 deg, half the thermal resolution); not fitted on every tank.";

		internal static readonly string FireWhenLaid =
			"true (default): after a lase the trigger does nothing until the gun has climbed to the elevation the computer set for"
			+ " that range, as the real firing-permission circuit has it (the ready lamp comes on when it is there). false = the gun"
			+ " fires at any time, as the game's own tanks do; a shot while it is still climbing after a lase falls short."
			+ AfterChange;

		internal static readonly string Shtora =
			"Shtora-1 (default): the IR jammer against wire-guided missiles, the laser warning on the commander's panel and the"
			+ " automatic 3D17 aerosol screens. false = none of it: the smoke launchers fire the game's ordinary smoke salvo."
			+ AfterChange;

		internal static readonly string ShtoraAutomatic =
			"true (default): a laser from the front fires an aerosol screen toward it by itself (automatic mode). false ="
			+ " semi-automatic mode: the warning and the panel only, the screen is fired by hand (vehicle menu: Shtora-1, or the"
			+ " smoke key)."
			+ AfterChange;

		internal static readonly string ShtoraTone =
			"true (default): the laser warning sounds a tone with the panel's lamps. false = the lamps only, no sound."
			+ AfterChange;

		internal static readonly string ShtoraJammerAtSpawn =
			"true (default): the IR jammer is on from the start. false = off until you switch it on (vehicle menu: Shtora-1), as"
			+ " the real crew does near a threat; AI crews still switch it on in combat."
			+ AfterChange;

		internal static readonly string ShtoraJamChance =
			"Chance, from 0 to 1, that the IR jammer breaks a wire-guided missile's guidance (TOW, HOT, MILAN, Konkurs and the"
			+ " like). 0.6 (default) is the figure Russian sources give against MILAN and HOT. It only acts on a missile launched"
			+ " from inside the jammer's sector, 20 deg either side of the gun: one fired from the flank or the rear is never jammed."
			+ AfterChange;

		internal static readonly string ShtoraPanel =
			"true (default): the laser warning shows the commander's control panel at the bottom right. false = no panel, the"
			+ " tone only."
			+ AfterChange;

		internal static readonly string ShtoraPanelAlways =
			"false (default): the commander's control panel comes up on a laser warning and goes away a few seconds after. true ="
			+ " it stays on screen the whole time you're in the T-90A, the warning lamp lighting up on it. Either way, looking"
			+ " through a sight it is drawn smaller, in the black strip under the picture."
			+ AfterChange;

		internal static readonly string KordCases =
			"true (default): the Kord throws its spent cases out of the chute, and they lie on the tank until it tilts or brakes"
			+ " hard. false = no cases (the belt still feeds)."
			+ AfterChange;

		internal static readonly string EraEffects =
			"false (default): a set-off ERA brick looks as before (it still works as armour, once). true = it blows open with a"
			+ " blast, charred, its lid thrown, the plate round it sooted, nets over it cut, the stowage boxes on the cheek modules"
			+ " gone with them."
			+ AfterChange;

		internal static readonly string ArmourPreset =
			"game-scale (default: matched to GHPC's other tanks; turret 690/820, glacis 550/750 without ERA) or"
			+ " source-only (open-source figures; turret 575/750, glacis 500/650)."
			+ AfterChange;
	}
}
