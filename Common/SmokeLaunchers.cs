using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using GHPC.Crew;
using GHPC.Equipment;
using GHPC.Player;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class SmokeLaunchers
	{
		internal const string AssetName = "smoke_points";

		internal static Vector3[] Parse(string text)
		{
			var f = text.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
			return Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2])).ToArray();
		}

		internal static Vector3[] Load(AssetBundle bundle)
		{
			var a = bundle != null ? bundle.LoadAsset<TextAsset>(AssetName) : null;
			return a != null ? Parse(a.text) : null;
		}

		static readonly ConditionalWeakTable<VehicleSmokeManager, VehicleSmokeManager.SmokeSlot[]> TubeSlots =
			new ConditionalWeakTable<VehicleSmokeManager, VehicleSmokeManager.SmokeSlot[]>();

		internal static VehicleSmokeManager.SmokeSlot SlotOfTube(VehicleSmokeManager mgr, int n) =>
			mgr != null && TubeSlots.TryGetValue(mgr, out var a) && n >= 1 && n <= a.Length ? a[n - 1] : null;

		internal static void Seat(Vehicle vehicle, Vector3[] tubes)
		{
			if (tubes == null || tubes.Length == 0) return;
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var turret = RigBones.Turret(vehicle.transform);
			if (mgr == null || turret == null) return;
			var slots = (VehicleSmokeManager.SmokeSlot[])Reflect.Field(typeof(VehicleSmokeManager), "_smokeSlots").GetValue(mgr);
			if (slots == null) return;
			var left = Enumerable.Range(0, tubes.Length).Where(i => tubes[i].x < 0).OrderByDescending(i => tubes[i].y).ToList();
			var right = Enumerable.Range(0, tubes.Length).Where(i => tubes[i].x >= 0).OrderByDescending(i => tubes[i].y).ToList();
			var bySide = slots.Where(s => s.SpawnLocation != null).OrderBy(s => turret.InverseTransformPoint(s.SpawnLocation.position).x).ToList();
			var slotsL = bySide.Take(left.Count).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			var slotsR = bySide.Skip(left.Count).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			var byTube = new VehicleSmokeManager.SmokeSlot[tubes.Length];
			for (int i = 0; i < slotsL.Count && i < left.Count; i++) { slotsL[i].SpawnLocation.position = turret.TransformPoint(tubes[left[i]]); byTube[left[i]] = slotsL[i]; }
			for (int i = 0; i < slotsR.Count && i < right.Count; i++) { slotsR[i].SpawnLocation.position = turret.TransformPoint(tubes[right[i]]); byTube[right[i]] = slotsR[i]; }
			TubeSlots.Remove(mgr);
			TubeSlots.Add(mgr, byTube);
		}

		// VehicleSmokeManager caches its crew brains in Start, before a runtime-spawned unit has any, and then never fires.
		internal static void RefreshCrew(VehicleSmokeManager mgr)
		{
			var t = typeof(VehicleSmokeManager);
			var get = Reflect.Method(t, "GetCrewBrainsArrayFromFlag");
			if (Reflect.Field(t, "_unitInfoBroker").GetValue(mgr) == null) return;
			foreach (var (cache, flag) in new[] { ("_smokeGrenadeCrewBrains", "_smokeGrenadeRequiredCrewPositions"), ("_smokeScreenCrewBrains", "_smokeScreenRequiredCrewPositions") })
			{
				var arr = (Array)Reflect.Field(t, cache).GetValue(mgr);
				if (arr != null && arr.Cast<object>().All(b => b != null)) continue;
				var fresh = get.Invoke(mgr, new[] { Reflect.Field(t, flag).GetValue(mgr) });
				Reflect.Field(t, cache).SetValue(mgr, fresh);
			}
		}

		[HarmonyPatch(typeof(VehicleSmokeManager), nameof(VehicleSmokeManager.FireSmokes))]
		static class CrewRefreshPatch
		{
			static void Prefix(VehicleSmokeManager __instance, bool __2)
			{
				var unit = __instance.GetComponentInParent<Vehicle>();
				var u = unit != null ? UnitRegistry.Units.FirstOrDefault(x => x.Key == unit.UniqueName) : null;
				if (u == null) return;
				try { RefreshCrew(__instance); KeyReport.Called(__instance, __2); }
				catch (Exception e) { PatchGuard.Failed("Smoke launcher crew refresh", e); }
			}
		}

		// G goes through GHPC's own gates and FireSmokes' crew check, the menu's screen entry doesn't. Say which one stopped it.
		internal static class KeyReport
		{
			const int MaxLines = 30;
			static int _lines, _calledFrame = -1, _roundsBefore;
			static string _refusal;

			// FireSmokes takes the first grenades in the same frame, so count them before it runs, not after the key.
			internal static void Called(VehicleSmokeManager mgr, bool force)
			{
				_calledFrame = Time.frameCount;
				_roundsBefore = Rounds(mgr);
				_refusal = force ? null : Refusal(mgr);
			}

			static string Refusal(VehicleSmokeManager mgr)
			{
				var t = typeof(VehicleSmokeManager);
				if (!mgr.IsInitialized) return "the smoke launcher isn't set up yet";
				if (!mgr.HasSmokeLauncher) return "the smoke launcher is gone (destroyed, or its part missing)";
				if (Reflect.Field(t, "_smokeGrenadeRequiresCrewBrain")?.GetValue(mgr) is bool needs && needs
					&& Reflect.Field(t, "_smokeGrenadeCrewBrains")?.GetValue(mgr) is Array brains
					&& !brains.Cast<ICrewBrain>().Any(b => b != null && !b.Incapacitated && !b.Suspended))
					return $"no crew member able to fire it (the game's smoke launcher needs one of these, and they are: {CrewStates(mgr, brains)})";
				var groups = mgr.SmokeGroups;
				if (groups == null || !Enumerable.Range(0, groups.Length).Any(mgr.SalvoHasRoundsAvailable)) return "no grenades left";
				return null;
			}

			static string CrewStates(VehicleSmokeManager mgr, Array brains)
			{
				var crew = mgr.GetComponentInParent<Vehicle>()?.CrewManager?.CrewMembers;
				return string.Join(", ", brains.Cast<ICrewBrain>().Select(b =>
				{
					var seat = crew?.FirstOrDefault(c => c != null && c.Brain == b);
					string who = seat != null ? $"the {seat.Position.ToString().ToLowerInvariant()}" : "a crew member";
					return $"{who} {(b == null ? "missing" : b.Incapacitated ? "out of action" : b.Suspended ? "not at their post" : "able")}";
				}));
			}

			static int Rounds(VehicleSmokeManager mgr) => (Reflect.Field(typeof(VehicleSmokeManager), "_smokeSlots")?.GetValue(mgr) as VehicleSmokeManager.SmokeSlot[])?.Sum(s => s.Rounds) ?? -1;

			static string InputGate(PlayerInput pi, VehicleSmokeManager mgr)
			{
				var t = typeof(PlayerInput);
				if (pi.ContextMenuRoot != null && pi.ContextMenuRoot.gameObject.activeInHierarchy) return "the vehicle menu was open (the game ignores the smoke key then)";
				if (!pi.AllowPlayerStrictLiveInput) return "the game wasn't taking vehicle controls (a menu, the map or a cutscene)";
				if (!pi.PlayerCanControlGunnerNow) return "the game didn't let the player work the gunner's controls at that moment";
				if (pi.CurrentPlayerCrewBrain == null) return "no crew position under the player";
				if (Reflect.Field(t, "_hasFullVehicleControl")?.GetValue(pi) is bool full && !full && Reflect.Field(t, "_controlledPosition")?.GetValue(pi) is CrewPosition pos && pos != CrewPosition.Gunner)
					return $"the player was in the {pos}'s seat without full control";
				var piMgr = Reflect.Field(t, "_smokeLauncherManager")?.GetValue(pi) as VehicleSmokeManager;
				if (piMgr != mgr) return piMgr == null ? "the game had no smoke launcher for the player's vehicle" : $"the game's smoke key went to another smoke launcher ({piMgr.name})";
				return null;
			}

			[HarmonyPatch(typeof(PlayerInput), "Update")]
			static class KeyPatch
			{
				static void Postfix(PlayerInput __instance)
				{
					try
					{
						if (UnitRegistry.Units.Count == 0) return;
						if (_lines >= MaxLines) return;
						var unit = __instance.CurrentPlayerUnit as Vehicle;
						if (unit == null || !GHPC.Utility.InputUtil.MainPlayer.GetButtonDown("Smoke Grenades")) return;
						var u = UnitRegistry.Units.FirstOrDefault(x => x.Key == unit.UniqueName);
						if (u == null) return;
						var mgr = unit.GetComponentInChildren<VehicleSmokeManager>(true);
						if (mgr == null) { Line(u.FriendlyName(), unit, "the vehicle has no smoke launcher"); return; }
						bool called = _calledFrame == Time.frameCount;
						string gate = called ? null : InputGate(__instance, mgr);
						string refusal = called ? _refusal : null;
						MelonCoroutines.Start(After(u.FriendlyName(), unit, mgr, called ? _roundsBefore : Rounds(mgr), called, gate, refusal));
					}
					catch (Exception e) { PatchGuard.Failed("Smoke key report", e); }
				}
			}

			static IEnumerator After(string name, Vehicle unit, VehicleSmokeManager mgr, int before, bool called, string gate, string refusal)
			{
				yield return new WaitForSeconds(1.5f);
				if (unit == null || mgr == null) yield break;
				int after = Rounds(mgr);
				if (after != before)
				{
					yield break;
				}
				string why = !called ? $"the game didn't pass the key to the smoke launcher: {gate ?? "no reason found among its checks"}"
					: refusal != null ? $"the smoke launcher refused: {refusal}"
					: $"the smoke launcher took the key but no grenade left the tubes ({after} left)";
				Line(name, unit, why);
			}

			static void Line(string name, Vehicle unit, string why)
			{
				if (_lines >= MaxLines) return;
				if (++_lines == MaxLines) why += $" (the last such line this session)";
				string line = $"{name} smoke key: no grenades fired: {why}; {State(unit)}.";
				MelonLogger.Msg(line);
			}

			static string State(Vehicle unit)
			{
				var sc = ShtoraScreen.Of(unit);
				var slot = GHPC.Camera.CameraSlot.ActiveInstance;
				return $"screen mode {(sc == null ? "none" : sc.S.Automatic ? "automatic" : "semi-automatic")}, view {(slot == null ? "none" : slot.name)}, vehicle {(unit.Neutralized ? "knocked out" : "fighting")}";
			}
		}
	}
}
