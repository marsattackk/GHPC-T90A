using System;
using System.Collections.Generic;
using GHPC.Weapons;
using TMPro;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class SightReadout : MonoBehaviour
	{
		// Seven segments can't do Cyrillic: 6 for Б, K for К, 0 for О, Y for У.
		internal static string Glyph7(AmmoType ammo) =>
			ammo.Guidance != AmmoType.GuidanceType.Unguided ? "Y"
			: ammo.Category == AmmoType.AmmoCategory.Penetrator ? "6"
			: ammo.Category == AmmoType.AmmoCategory.ShapedCharge ? "K"
			: "0";

		readonly List<(GameObject lamp, Func<bool> lit)> _lit = new List<(GameObject, Func<bool>)>();

		FireControlSystem _fcs;
		TextMeshProUGUI _ammo;
		Func<AmmoType, string> _ammoText;
		GameObject _laser;
		float _laserRecharge, _laserDarkUntil = -1f;

		void OnLase() => _laserDarkUntil = Time.time + _laserRecharge;

		void OnDestroy()
		{
			if (_fcs != null && _laser != null) _fcs.LaserFired -= OnLase;
		}

		void Update()
		{
			if (_ammo != null && _fcs != null)
			{
				var a = _fcs.CurrentAmmoType;
				string t = a != null ? _ammoText(a) : "";
				if (_ammo.text != t) _ammo.text = t;
			}
			if (_laser != null)
			{
				bool lit = Time.time >= _laserDarkUntil;
				if (_laser.activeSelf != lit) _laser.SetActive(lit);
			}
			foreach (var (lamp, test) in _lit)
			{
				bool on = false;
				try { on = test(); } catch { }
				if (lamp != null && lamp.activeSelf != on) lamp.SetActive(on);
			}
		}
	}
}
