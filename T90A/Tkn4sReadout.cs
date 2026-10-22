using System.Collections.Generic;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using MelonLoader;
using T90A.Common;
using TMPro;
using UnityEngine;

namespace T90A
{
	internal class Tkn4sReadout : MonoBehaviour
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
			var o = ReticleBuild.BuildOverlay(spec, day.transform, (float)spec.Fov, problems);
			foreach (var p in problems) MelonLogger.Warning($"T-90A TKN-4S indicator strip: {p}");
			var r = o.Root.AddComponent<Tkn4sReadout>();
			r._gun = gun; r._gunFcs = gunFcs;
			r._digit = o.Readouts.TryGetValue("ammo", out var digit) ? digit : null;
			r._lit = o.Lamps.TryGetValue("ready indicator", out var lamp) ? lamp : null;
			if (r._digit == null || r._lit == null) MelonLogger.Warning("T-90A TKN-4S indicator strip: no 'ammo' readout or 'ready indicator' lamp in its description.");
		}

		void Update()
		{
			if (_gun == null) return;
			var ammo = _gunFcs != null ? _gunFcs.CurrentAmmoType : null;
			string g = ammo != null ? Fcs1A45T.Readout.Glyph(ammo) : "";
			if (_digit.text != g) _digit.text = g;
			bool able = _gun.AbleToFire;
			_offFor = able ? 0f : _offFor + Time.deltaTime;
			bool lit = able || (_lit.activeSelf && _offFor < 0.5f);
			if (_lit.activeSelf != lit) _lit.SetActive(lit);
		}
	}
}
