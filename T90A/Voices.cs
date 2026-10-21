using GHPC;
using GHPC.Effects.Voices;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class Voices
	{
		static GameObject _soviet;

		internal static void Apply(Vehicle vehicle)
		{
			var german = Conversion.FindDeep(vehicle.transform, "DE Tank Voice");
			if (german == null) MelonLogger.Warning("T-90A voices: German voice object \"DE Tank Voice\" not found.");
			var source = SovietVoice();
			if (source == null) { MelonLogger.Warning("T-90A voices: vanilla T-80B voice not found; keeping the T-72M1's."); return; }

			var voice = Object.Instantiate(source, vehicle.transform);
			voice.name = "RU Tank Voice (T-90A)";
			voice.transform.localPosition = Vector3.zero;
			voice.transform.localRotation = Quaternion.identity;
			var handler = voice.GetComponent<CrewVoiceHandler>();
			AccessTools.Field(typeof(CrewVoiceHandler), "_chassis").SetValue(handler, vehicle.Chassis as NwhChassis);
			AccessTools.Field(typeof(CrewVoiceHandler), "_reloadType").SetValue(handler, CrewVoiceHandler.ReloaderType.AutoLoaderAZ);
			AccessTools.Field(typeof(Unit), "_crewVoiceHandler").SetValue(vehicle, handler);
			voice.SetActive(true);
			if (german != null) german.gameObject.SetActive(false);
		}

		static GameObject SovietVoice()
		{
			if (_soviet != null) return _soviet;
			var prefab = DonorParts.Prefab("T80B");
			var handler = prefab != null ? prefab.GetComponentInChildren<CrewVoiceHandler>(true) : null;
			if (handler == null) return null;
			var box = DonorParts.Copy(prefab, "T-90A voices donor (" + prefab.name + ")", handler.gameObject);
			_soviet = box != null ? box.GetComponentInChildren<CrewVoiceHandler>(true)?.gameObject : null;
			return _soviet;
		}
	}
}
