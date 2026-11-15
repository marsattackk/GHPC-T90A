using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Effects.Voices;
using GHPC.Effects.Voices.VoiceRoutines;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// The T-80B's Russian crew voice in place of the T-72M1's German one.
	internal static class CrewVoices
	{
		static GameObject _soviet;

		internal static void Apply(Vehicle vehicle, string tag)
		{
			var german = vehicle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == "DE Tank Voice");
			if (german == null) MelonLogger.Warning($"{tag} voices: German voice object \"DE Tank Voice\" not found.");
			var source = Donor(tag);
			if (source == null)
			{
				MelonLogger.Warning($"{tag} voices: vanilla T-80B voice not found; keeping the T-72M1's.");
				return;
			}

			var voice = Object.Instantiate(source, vehicle.transform);
			voice.name = $"RU Tank Voice ({tag})";
			voice.transform.localPosition = Vector3.zero;
			voice.transform.localRotation = Quaternion.identity;
			var handler = voice.GetComponent<CrewVoiceHandler>();
			Reflect.Field(typeof(CrewVoiceHandler), "_chassis").SetValue(handler, vehicle.Chassis as NwhChassis);
			Reflect.Field(typeof(CrewVoiceHandler), "_reloadType").SetValue(handler, CrewVoiceHandler.ReloaderType.AutoLoaderAZ);
			Reflect.Field(typeof(Unit), "_crewVoiceHandler").SetValue(vehicle, handler);
			voice.SetActive(true);
			if (german != null) german.gameObject.SetActive(false);
		}

		internal static void Mark(Vehicle vehicle, string tag)
		{
			var handler = vehicle
				.GetComponentsInChildren<CrewVoiceHandler>(true)
				.FirstOrDefault(c => c.name == "RU Tank Voice");
			if (handler == null)
			{
				MelonLogger.Warning($"{tag} voices: the base's \"RU Tank Voice\" not found; reload callouts are not limited.");
				return;
			}
			handler.name = $"RU Tank Voice ({tag})";
		}

		internal static GameObject Donor(string tag)
		{
			if (_soviet != null) return _soviet;
			var prefab = DonorParts.Prefab("T80B");
			var handler = prefab != null ? prefab.GetComponentInChildren<CrewVoiceHandler>(true) : null;
			if (handler == null) return null;
			var box = DonorParts.Copy(prefab, $"{tag} voices donor ({prefab.name})", handler.gameObject);
			_soviet = box != null ? box.GetComponentInChildren<CrewVoiceHandler>(true)?.gameObject : null;
			return _soviet;
		}

		static readonly FieldInfo TurnStamp = Reflect.Field(typeof(GHPC.Weapons.AmmoCarousel), "_lastRotationStartTime");
		static readonly FieldInfo RoutineHandler = Reflect.Field(
			typeof(VoiceRoutinesBase),
			"_crewVoiceHandler");
		static readonly Dictionary<CrewVoiceHandler, float> Announced =
			new Dictionary<CrewVoiceHandler, float>();

		// AmmoCarousel stalls short of its slot and restarts, firing StartedRotation each time, so "AZ" got spammed.
		// One callout per _lastRotationStartTime, which the restarts don't change.
		[HarmonyLib.HarmonyPatch(
			typeof(USSRVoiceRoutines),
			nameof(USSRVoiceRoutines.AmmoCarouselStartedRotation))]
		static class OneCalloutPerTurn
		{
			static bool Prefix(USSRVoiceRoutines __instance)
			{
				try { return Announce(__instance); }
				catch (System.Exception exception)
				{
					PatchGuard.Failed("Autoloader callout", exception);
					return true;
				}
			}

			static bool Announce(USSRVoiceRoutines routine)
			{
				if (TurnStamp == null || RoutineHandler == null) return true;
				var handler = RoutineHandler.GetValue(routine) as CrewVoiceHandler;
				if (handler == null || !handler.name.StartsWith("RU Tank Voice (")) return true;
				var carousel = handler.GetComponentInParent<Unit>()?.LoadoutManager?.Weapon?.Feed?.Carousel;
				if (carousel == null) return true;
				float stamp = (float)TurnStamp.GetValue(carousel);
				if (Announced.TryGetValue(handler, out float last) && last == stamp) return false;
				UnityKeys.Prune(Announced);
				Announced[handler] = stamp;
				return true;
			}
		}
	}
}
