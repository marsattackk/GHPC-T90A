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
	internal static class CrewVoices
	{
		static GameObject _soviet;

		internal static void Apply(Vehicle vehicle, string tag)
		{
			var german = vehicle.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DE Tank Voice");
			if (german == null) MelonLogger.Warning($"{tag} voices: German voice object \"DE Tank Voice\" not found.");
			var source = Donor(tag);
			if (source == null) { MelonLogger.Warning($"{tag} voices: vanilla T-80B voice not found; keeping the T-72M1's."); return; }

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
		static readonly FieldInfo RoutineHandler = Reflect.Field(typeof(VoiceRoutinesBase), "_crewVoiceHandler");
		static readonly Dictionary<CrewVoiceHandler, float> Announced = new Dictionary<CrewVoiceHandler, float>();

		// AmmoCarousel stalls short of its slot and restarts, firing StartedRotation each time, so "AZ" got spammed.
		// One callout per _lastRotationStartTime, the restarts keep that stamp.
		[HarmonyLib.HarmonyPatch(typeof(USSRVoiceRoutines), nameof(USSRVoiceRoutines.AmmoCarouselStartedRotation))]
		static class OneCalloutPerTurn
		{
			static bool Prefix(USSRVoiceRoutines __instance)
			{
				try { return Announce(__instance); }
				catch (System.Exception e) { PatchGuard.Failed("Autoloader callout", e); return true; }
			}

			static bool Announce(USSRVoiceRoutines routine)
			{
				if (TurnStamp == null || RoutineHandler == null) return true;
				var h = RoutineHandler.GetValue(routine) as CrewVoiceHandler;
				if (h == null || !h.name.StartsWith("RU Tank Voice (")) return true;
				var carousel = h.GetComponentInParent<Unit>()?.LoadoutManager?.Weapon?.Feed?.Carousel;
				if (carousel == null) return true;
				float stamp = (float)TurnStamp.GetValue(carousel);
				if (Announced.TryGetValue(h, out float last) && last == stamp) return false;
				foreach (var dead in Announced.Keys.Where(k => k == null).ToList()) Announced.Remove(dead);
				Announced[h] = stamp;
				return true;
			}
		}
	}
}
