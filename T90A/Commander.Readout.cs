using System.Collections.Generic;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using MelonLoader;
using T90A.Common;
using TMPro;
using UnityEngine;

namespace T90A
{
	// Ammo digit and ready lamp on the TKN-4S strip. The real strip is fed for Дубль, which isn't in the game, so it
	// runs off the gunner's 2A46. Screen overlay, not reticle strokes: a stroke with no alignment bone takes the whole
	// reticle down.
	internal partial class Tkn4sReadout : MonoBehaviour
	{
		WeaponSystem _gun;
		FireControlSystem _gunFcs;
		TextMeshProUGUI _digit;
		GameObject _lit;
		float _offFor;

		internal static void Build(UsableOptic day, WeaponSystem gun, FireControlSystem gunFcs)
		{
			if (day == null || gun == null) return;
			var spec = Reticles.Spec("tkn4s_day.jsonc");
			var problems = new List<string>();
			var overlay = ReticleBuild.BuildOverlay(spec, day.transform, (float)spec.Fov, problems);
			foreach (var problem in problems) MelonLogger.Warning($"T-90A TKN-4S indicator strip: {problem}");
			var readout = overlay.Root.AddComponent<Tkn4sReadout>();
			readout._gun = gun;
			readout._gunFcs = gunFcs;
			readout._digit = overlay.Readouts.TryGetValue("ammo", out var digit) ? digit : null;
			readout._lit = overlay.Lamps.TryGetValue("ready indicator", out var lamp) ? lamp : null;
			if (readout._digit == null || readout._lit == null)
				MelonLogger.Warning("T-90A TKN-4S indicator strip: no 'ammo' readout or 'ready indicator' lamp in its description.");
		}

		void Update()
		{
			if (_gun == null || _digit == null || _lit == null) return;
			var ammo = _gunFcs != null ? _gunFcs.CurrentAmmoType : null;
			string glyph = ammo != null ? Sights.Readout.Glyph(ammo) : "";
			if (_digit.text != glyph) _digit.text = glyph;
			bool able = _gun.AbleToFire;
			_offFor = able ? 0f : _offFor + Time.deltaTime;
			bool lit = able || (_lit.activeSelf && _offFor < 0.5f);   // the game's own ready lamps stay lit 0.5 s the same way
			if (_lit.activeSelf != lit) _lit.SetActive(lit);
		}
	}
}
