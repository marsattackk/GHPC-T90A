using System.Linq;
using GHPC;
using GHPC.Effects.Voices;
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
	}
}
