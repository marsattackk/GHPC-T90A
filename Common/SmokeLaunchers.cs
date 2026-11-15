using System;
using System.Collections;
using System.Collections.Generic;
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
			var parts = text.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var values = new float[parts.Length];
			for (int i = 0; i < parts.Length; i++)
				if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
				{
					MelonLogger.Warning($"'{AssetName}': bad number '{parts[i]}', the smoke launch points are not used.");
					return null;
				}
			if (values.Length % 3 != 0)
			{
				MelonLogger.Warning($"'{AssetName}': {values.Length} numbers, not x y z per tube; the smoke launch points are not used.");
				return null;
			}
			return Enumerable.Range(0, values.Length / 3)
				.Select(i => new Vector3(values[3 * i], values[3 * i + 1], values[3 * i + 2]))
				.ToArray();
		}

		internal static Vector3[] Load(AssetBundle bundle)
		{
			var text = PortText.Load(bundle, AssetName);
			return text != null ? Parse(text) : null;
		}

		static readonly ConditionalWeakTable<VehicleSmokeManager, VehicleSmokeManager.SmokeSlot[]> TubeSlots =
			new ConditionalWeakTable<VehicleSmokeManager, VehicleSmokeManager.SmokeSlot[]>();

		internal static VehicleSmokeManager.SmokeSlot SlotOfTube(VehicleSmokeManager mgr, int n) =>
			mgr != null && TubeSlots.TryGetValue(mgr, out var slots) && n >= 1 && n <= slots.Length ? slots[n - 1] : null;

		internal static string Seat(Vehicle vehicle, Vector3[] tubes)
		{
			if (tubes == null || tubes.Length == 0) return "no tube positions (bundle smoke_points)";
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var turret = RigBones.Turret(vehicle.transform);
			if (mgr == null || turret == null) return "no smoke manager / turret";
			var slots = SlotsField?.GetValue(mgr) as VehicleSmokeManager.SmokeSlot[];
			if (slots == null) return "no smoke slots";
			var left = Enumerable.Range(0, tubes.Length).Where(i => tubes[i].x < 0).OrderByDescending(i => tubes[i].y).ToList();
			var right = Enumerable.Range(0, tubes.Length).Where(i => tubes[i].x >= 0).OrderByDescending(i => tubes[i].y).ToList();
			var bySide = slots
				.Where(s => s.SpawnLocation != null)
				.OrderBy(s => turret.InverseTransformPoint(s.SpawnLocation.position).x)
				.ToList();
			var slotsL = bySide.Take(left.Count).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			var slotsR = bySide.Skip(left.Count).OrderByDescending(s => s.SpawnLocation.position.y).ToList();
			int moved = 0;
			var byTube = new VehicleSmokeManager.SmokeSlot[tubes.Length];
			for (int i = 0; i < slotsL.Count && i < left.Count; i++)
			{
				slotsL[i].SpawnLocation.position = turret.TransformPoint(tubes[left[i]]);
				byTube[left[i]] = slotsL[i];
				moved++;
			}
			for (int i = 0; i < slotsR.Count && i < right.Count; i++)
			{
				slotsR[i].SpawnLocation.position = turret.TransformPoint(tubes[right[i]]);
				byTube[right[i]] = slotsR[i];
				moved++;
			}
			TubeSlots.Remove(mgr);
			TubeSlots.Add(mgr, byTube);
			return $"{moved} of {slots.Length} grenades moved to the tubes ({Math.Min(slotsL.Count, left.Count)} left, {Math.Min(slotsR.Count, right.Count)} right)";
		}

		static readonly System.Reflection.FieldInfo SlotsField = Reflect.Field(typeof(VehicleSmokeManager), "_smokeSlots"),
			GroupsField = Reflect.Field(typeof(VehicleSmokeManager), "_smokeGroups"),
			ForwardField = Reflect.Field(typeof(VehicleSmokeManager), "_forwardReference");

		internal static string Grow(Vehicle vehicle, int tubes)
		{
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var slots = mgr != null ? SlotsField?.GetValue(mgr) as VehicleSmokeManager.SmokeSlot[] : null;
			if (slots == null || slots.Length == 0) return "no smoke slots";
			if (slots.Length >= tubes) return $"{slots.Length} slots for {tubes} tubes: none added";
			var last = slots.Last(s => s.SpawnLocation != null);
			var grown = slots.ToList();
			for (int i = slots.Length; i < tubes; i++)
			{
				var port = new GameObject($"Smoke_port_{i + 1}").transform;
				port.SetParent(last.SpawnLocation.parent, false);
				port.position = last.SpawnLocation.position;
				port.rotation = last.SpawnLocation.rotation;
				grown.Add(new VehicleSmokeManager.SmokeSlot { SpawnLocation = port, Angle = last.Angle, Rounds = last.Rounds });
			}
			SlotsField.SetValue(mgr, grown.ToArray());
			return $"{tubes - slots.Length} slots added to the base's {slots.Length}";
		}

		internal static string OneVolley(Vehicle vehicle)
		{
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var slots = mgr != null ? SlotsField?.GetValue(mgr) as VehicleSmokeManager.SmokeSlot[] : null;
			if (slots == null || GroupsField == null) return "no smoke slots";
			var was = mgr.SmokeGroups?.Length ?? 0;
			GroupsField.SetValue(mgr, new[] { new VehicleSmokeManager.SmokePattern {
				SmokePatternData = Enumerable.Range(0, slots.Length)
					.Select(i => new VehicleSmokeManager.SmokePatternData { SmokeSlotIndex = i })
					.ToArray() } });
			return $"one volley of {slots.Length} grenades (the base had {was} salvos)";
		}

		internal static Vector3[] LoadResource(string name)
		{
			using (var stream = typeof(SmokeLaunchers).Assembly.GetManifestResourceStream(name))
			{
				if (stream == null) return null;
				using (var reader = new System.IO.StreamReader(stream))
					return Parse(string.Join("\n", reader.ReadToEnd().Split('\n').Where(l => !l.TrimStart().StartsWith("#"))));
			}
		}

		static readonly ConditionalWeakTable<VehicleSmokeManager, Dictionary<float, float>> Elevations =
			new ConditionalWeakTable<VehicleSmokeManager, Dictionary<float, float>>();

		internal static string AimAlongTubes(Vehicle vehicle, Vector3[] dirs)
		{
			var mgr = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			var turret = RigBones.Turret(vehicle.transform);
			if (dirs == null || dirs.Length == 0) return "no tube directions";
			if (mgr == null || turret == null) return "no smoke manager / turret";
			var fwd = ForwardField?.GetValue(mgr) as Transform;
			if (fwd == null) fwd = turret;
			var table = new Dictionary<float, float>();
			var said = new List<string>();
			for (int i = 0; i < dirs.Length; i++)
			{
				var slot = SlotOfTube(mgr, i + 1);
				if (slot == null) continue;
				var direction = turret.TransformDirection(dirs[i]).normalized;
				float azimuth = Vector3.SignedAngle(
					Vector3.ProjectOnPlane(fwd.forward, fwd.up),
					Vector3.ProjectOnPlane(direction, fwd.up),
					fwd.up);
				float elevation = 90f - Vector3.Angle(fwd.up, direction);
				while (table.ContainsKey(azimuth)) azimuth += 0.01f;
				slot.Angle = azimuth;
				table[azimuth] = elevation;
				said.Add($"{i + 1}: {azimuth:+0;-0}/{elevation:0}");
			}
			Elevations.Remove(mgr);
			Elevations.Add(mgr, table);
			return $"{table.Count} grenades along their tubes (tube: azimuth/elevation deg) {string.Join(", ", said)}";
		}

		[HarmonyPatch(typeof(VehicleSmokeManager), "CalculateLaunchImpulse", new[] { typeof(float), typeof(float), typeof(float) })]
		static class ElevationPatch
		{
			static void Prefix(VehicleSmokeManager __instance, float angle, ref float launchAngle)
			{
				if (!Elevations.TryGetValue(__instance, out var table) || !table.TryGetValue(angle, out var elevation)) return;
				launchAngle = elevation;
			}
		}

		// VehicleSmokeManager caches its crew brains in Start, before a runtime-spawned unit has any, and then never fires.
		internal static void RefreshCrew(VehicleSmokeManager mgr)
		{
			var managerType = typeof(VehicleSmokeManager);
			var get = Reflect.Method(managerType, "GetCrewBrainsArrayFromFlag");
			var broker = Reflect.Field(managerType, "_unitInfoBroker");
			if (get == null || broker == null || broker.GetValue(mgr) == null) return;
			foreach (var (cache, flag) in new[]
			{
				("_smokeGrenadeCrewBrains", "_smokeGrenadeRequiredCrewPositions"),
				("_smokeScreenCrewBrains", "_smokeScreenRequiredCrewPositions")
			})
			{
				var cacheField = Reflect.Field(managerType, cache);
				var flagField = Reflect.Field(managerType, flag);
				if (cacheField == null || flagField == null) continue;
				var brains = (Array)cacheField.GetValue(mgr);
				if (brains != null && brains.Cast<object>().All(b => b != null)) continue;
				cacheField.SetValue(mgr, get.Invoke(mgr, new[] { flagField.GetValue(mgr) }));
			}
		}

		internal static IEnumerator RefreshCrewLater(Vehicle vehicle, string tag)
		{
			yield return null;
			yield return null;
			if (vehicle == null) yield break;
			try
			{
				var smoke = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
				if (smoke != null) RefreshCrew(smoke);
			}
			catch (Exception exception) { MelonLogger.Error(tag + " smoke crew refresh failed: " + exception); }
		}

		[HarmonyPatch(typeof(VehicleSmokeManager), nameof(VehicleSmokeManager.FireSmokes))]
		static class CrewRefreshPatch
		{
			static void Prefix(VehicleSmokeManager __instance, bool __2)
			{
				var unit = __instance.GetComponentInParent<Vehicle>();
				var definition = unit != null ? UnitRegistry.Units.FirstOrDefault(c => c.Key == unit.UniqueName) : null;
				if (definition == null) return;
				try
				{
					RefreshCrew(__instance);
					KeyReport.Called(__instance, __2);
				}
				catch (Exception exception) { PatchGuard.Failed("Smoke launcher crew refresh", exception); }
			}
		}

		// G goes through GHPC's own gates and FireSmokes' crew check but the menu's screen entry
		// doesn't, so the log says which one stopped it.
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
				var managerType = typeof(VehicleSmokeManager);
				if (!mgr.IsInitialized) return "the smoke launcher isn't set up yet";
				if (!mgr.HasSmokeLauncher) return "the smoke launcher is gone (destroyed, or its part missing)";
				if (Reflect.Field(managerType, "_smokeGrenadeRequiresCrewBrain")?.GetValue(mgr) is bool needs && needs
					&& Reflect.Field(managerType, "_smokeGrenadeCrewBrains")?.GetValue(mgr) is Array brains
					&& !brains.Cast<ICrewBrain>().Any(b => b != null && !b.Incapacitated && !b.Suspended))
					return $"no crew member able to fire it (the game's smoke launcher needs one of these, and they are: {CrewStates(mgr, brains)})";
				var groups = mgr.SmokeGroups;
				if (groups == null || !Enumerable.Range(0, groups.Length).Any(mgr.SalvoHasRoundsAvailable)) return "no grenades left";
				return null;
			}

			static string CrewStates(VehicleSmokeManager mgr, Array brains)
			{
				var crew = mgr.GetComponentInParent<Vehicle>()?.CrewManager?.CrewMembers;
				return string.Join(", ", brains.Cast<ICrewBrain>().Select(brain =>
				{
					var seat = crew?.FirstOrDefault(c => c != null && c.Brain == brain);
					string who = seat != null ? $"the {seat.Position.ToString().ToLowerInvariant()}" : "a crew member";
					return $"{who} {(brain == null ? "missing" : brain.Incapacitated ? "out of action" : brain.Suspended ? "not at their post" : "able")}";
				}));
			}

			static int Rounds(VehicleSmokeManager mgr)
				=> (Reflect.Field(
					typeof(VehicleSmokeManager),
					"_smokeSlots")?.GetValue(mgr) as VehicleSmokeManager.SmokeSlot[])?.Sum(s => s.Rounds)
					?? -1;

			static string InputGate(PlayerInput pi, Vehicle unit, VehicleSmokeManager mgr)
			{
				var inputType = typeof(PlayerInput);
				if (pi.ContextMenuRoot != null && pi.ContextMenuRoot.gameObject.activeInHierarchy)
					return "the vehicle menu was open (the game ignores the smoke key then)";
				if (!pi.AllowPlayerStrictLiveInput) return "the game wasn't taking vehicle controls (a menu, the map or a cutscene)";
				if (!pi.PlayerCanControlGunnerNow) return "the game didn't let the player work the gunner's controls at that moment";
				if (pi.CurrentPlayerCrewBrain == null) return "no crew position under the player";
				if (Reflect.Field(inputType, "_hasFullVehicleControl")?.GetValue(pi) is bool full
					&& !full
					&& Reflect.Field(inputType, "_controlledPosition")?.GetValue(pi) is CrewPosition pos
					&& pos != CrewPosition.Gunner)
					return $"the player was in the {pos}'s seat without full control";
				var piMgr = Reflect.Field(inputType, "_smokeLauncherManager")?.GetValue(pi) as VehicleSmokeManager;
				if (piMgr != mgr)
					return piMgr == null
						? "the game had no smoke launcher for the player's vehicle"
						: $"the game's smoke key went to another smoke launcher ({piMgr.name})";
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
						var definition = UnitRegistry.Units.FirstOrDefault(c => c.Key == unit.UniqueName);
						if (definition == null) return;
						var mgr = unit.GetComponentInChildren<VehicleSmokeManager>(true);
						if (mgr == null)
						{
							Line(definition.FriendlyName(), unit, "the vehicle has no smoke launcher");
							return;
						}
						bool called = _calledFrame == Time.frameCount;
						string gate = called ? null : InputGate(__instance, unit, mgr);
						string refusal = called ? _refusal : null;
						MelonCoroutines.Start(After(
							definition.FriendlyName(),
							unit,
							mgr,
							called ? _roundsBefore : Rounds(mgr),
							called,
							gate,
							refusal));
					}
					catch (Exception exception) { PatchGuard.Failed("Smoke key report", exception); }
				}
			}

			static IEnumerator After(
				string name,
				Vehicle unit,
				VehicleSmokeManager mgr,
				int before,
				bool called,
				string gate,
				string refusal)
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
				var station = ShtoraScreen.Of(unit);
				var slot = GHPC.Camera.CameraSlot.ActiveInstance;
				return $"screen mode {(station == null ? "none" : station.S.Automatic ? "automatic" : "semi-automatic")}, view {(slot == null ? "none" : slot.name)}, vehicle {(unit.Neutralized ? "knocked out" : "fighting")}";
			}
		}
	}
}
