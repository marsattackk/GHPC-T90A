using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Player;
using GHPC.UI.Hud;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace T90A.Common
{
	// The smoke menu entries, and the extra lines I add to the weapon text on the HUD.
	internal static partial class ShtoraScreen
	{
		[HarmonyPatch(typeof(PlayerInput), "AddSmokeButtons")]
		static class MenuPatch
		{
			static readonly System.Reflection.MethodInfo Add = Reflect.Method(
				typeof(PlayerInput),
				"AddMenuButton",
				new[] { typeof(string), typeof(UnityAction) });

			static void Postfix(PlayerInput __instance)
			{
				var station = Of(__instance.CurrentPlayerUnit);
				if (station == null || Add == null || station.Slots == null || !station.S.Heads) return;
				Add.Invoke(
					__instance,
					new object[] { $"{station.S.MenuName}/Aerosol Screen ({station.Slots.Sum(s => s.Rounds)})", (UnityAction)(() =>
					{
						var fcs = station.Vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon;
						float azimuth = fcs?.MuzzleIdentity != null
							? station.Bearing(fcs.MuzzleIdentity.position + fcs.MuzzleIdentity.forward * 100f).x
							: 0f;
						var fired = station.TryScreen(azimuth, "button");
					}) });
				Add.Invoke(
					__instance,
					new object[] { $"{station.S.MenuName}/Screen Mode ({(station.S.Automatic ? "AUTO" : "SEMI")})", (UnityAction)(() =>
					{
						station.S.Automatic = !station.S.Automatic;
					}) });
				if (Time.time - station.LastWarning.at < 20f && !station.LastWarning.precise)
					Add.Invoke(
						__instance,
						new object[]
						{
							$"{station.S.MenuName}/Turn to Laser",
							(UnityAction)(() => MelonCoroutines.Start(station.TurnToThreat()))
						});
			}
		}

		// WeaponHud pins its text by the top, so extra lines push it down onto the speed text. Lift it by what I add.
		[HarmonyPatch(typeof(WeaponHud), "Update")]
		static class WeaponHudPatch
		{
			static readonly AccessTools.FieldRef<WeaponHud, Text> HudText =
				Reflect.FieldRefAccess<WeaponHud, Text>("_hudText");
			static readonly Dictionary<RectTransform, object[]> Lifted =
				Shared<Dictionary<RectTransform, object[]>>("GHPCMods.ShtoraScreen.HudLift.v2");

			static void Postfix(WeaponHud __instance)
			{
				if (Stations.Count == 0 && JammerState == null && !WeaponHudLines.Any() && Lifted.Count == 0) return;
				if (HudText == null) return;
				try { AddLines(__instance); }
				catch (Exception exception) { PatchGuard.Failed("Screen lines in the weapon text", exception); }
			}

			static void AddLines(WeaponHud __instance)
			{
				var text = HudText(__instance);
				if (text != null && _ours != null && ReferenceEquals(text.text, _ours)) return;
				Settle(text);
				if (Stations.Count == 0 && JammerState == null && !WeaponHudLines.Any()) return;
				var unit = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerUnit : null;
				var station = Of(unit);
				var jam = JammerState?.Invoke(unit);
				if (station != null && !station.S.Heads) station = null;
				var (apsName, aps) = WeaponHudLines.For(unit);
				if (station == null && jam == null && aps == null) return;
				string name = station?.S.MenuName ?? "Shtora-1";
				bool shtora = station != null || jam != null;
				if (text == null
					|| string.IsNullOrEmpty(text.text)
					|| (shtora && text.text.Contains(name + ":"))
					|| (aps != null && text.text.Contains(apsName + ":")))
					return;
				float before = Height(text, text.text);
				var parts = new List<string>();
				if (jam != null) parts.Add(jam);
				if (station != null) parts.Add(station.S.Automatic ? "auto" : "semi-auto");
				var lines = text.text.Split('\n').ToList();
				int insertIndex = lines.FindLastIndex(l => l.StartsWith("Stabilizers") || l.StartsWith("Delta-D"));
				if (insertIndex < 0) insertIndex = Math.Min(2, lines.Count - 1);
				if (shtora)
				{
					lines.Insert(insertIndex + 1, $"{name}: {string.Join(", ", parts)}");
					if (station != null) lines.Insert(insertIndex + 2, $"{name} screen: {station.ScreenState()}");
				}
				if (aps != null) lines.Insert(insertIndex + 1 + (shtora ? (station != null ? 2 : 1) : 0), aps);
				text.text = string.Join("\n", lines);
				_ours = text.text;
				Lift(text, Height(text, text.text) - before);
			}

			static string _ours;

			// Unwrapped: with zero extents and wrapping on, every character gets a line of its own.
			static float Height(Text t, string s)
			{
				var settings = t.GetGenerationSettings(Vector2.zero);
				settings.horizontalOverflow = HorizontalWrapMode.Overflow;
				return t.cachedTextGeneratorForLayout.GetPreferredHeight(s, settings) / t.pixelsPerUnit;
			}

			static void Lift(Text t, float by)
			{
				var rect = t.rectTransform;
				bool known = Lifted.TryGetValue(rect, out var lifted);
				if (!known) UnityKeys.Prune(Lifted);
				float top = known ? (float)lifted[0] : rect.anchoredPosition.y;
				rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, top + by);
				Lifted[rect] = new object[] { top, t.text };
			}

			static void Settle(Text t)
			{
				if (t == null || Lifted.Count == 0) return;
				var rect = t.rectTransform;
				if (!Lifted.TryGetValue(rect, out var lifted) || !(lifted[1] is string text) || text == t.text) return;
				rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, (float)lifted[0]);
				Lifted.Remove(rect);
			}
		}
	}
}
