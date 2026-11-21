using System;
using System.Collections.Generic;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using TMPro;
using UnityEngine;

namespace T90A.Common
{
	// Range, ready lamp, ammo and laser lamps on a sight's readout, in seven-segment or Cyrillic.
	internal sealed class SightReadout : MonoBehaviour
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Range;
			public string RangeFormat = "0000";
			public int RangeQuantize = 10;
			public string RangePrefix = "", RangeSuffix = "";
			public string Ready;
			public string Ammo;
			public Func<AmmoType, string> AmmoText;
			public string LaserLamp;
			public float LaserRecharge = 2f;
			public (string name, Func<bool> lit)[] Lit = new (string, Func<bool>)[0];
		}
#pragma warning restore CS0649

		// Seven segments can't do Cyrillic: 6 for Б, K for К, 0 for О, Y for У.
		internal static string Glyph7(AmmoType ammo) =>
			ammo.Guidance != AmmoType.GuidanceType.Unguided ? "Y"
			: ammo.Category == AmmoType.AmmoCategory.Penetrator ? "6"
			: ammo.Category == AmmoType.AmmoCategory.ShapedCharge ? "K"
			: "0";

		internal static string Cyrillic(AmmoType a) =>
			a.Caliber < 20f ? "П"
			: a.Guidance != AmmoType.GuidanceType.Unguided ? "У"
			: a.Category == AmmoType.AmmoCategory.Penetrator ? "Б"
			: a.Category == AmmoType.AmmoCategory.ShapedCharge ? "К"
			: "О";

		readonly List<(GameObject lamp, Func<bool> lit)> _lit = new List<(GameObject, Func<bool>)>();

		FireControlSystem _fcs;
		TextMeshProUGUI _ammo;
		Func<AmmoType, string> _ammoText;
		GameObject _laser;
		float _laserRecharge, _laserDarkUntil = -1f;

		internal static string Attach(UsableOptic optic, ReticleBuild.Overlay o, Settings s, List<string> problems)
		{
			var bound = new List<string>();
			GameObject Item(string name)
			{
				if (string.IsNullOrEmpty(name)) return null;
				if (o.Readouts.TryGetValue(name, out var label)) return label.gameObject;
				if (o.Lamps.TryGetValue(name, out var found)) return found;
				problems.Add($"no readout or lamp '{name}' in the description");
				return null;
			}
			if (!string.IsNullOrEmpty(s.Range))
			{
				if (o.Readouts.TryGetValue(s.Range, out var range))
				{
					optic.RangeText = range;   // GHPC writes the range into it from here on
					optic.RangeTextQuantize = s.RangeQuantize;
					optic.RangeTextDivideBy = 1;
					optic.RangeTextArchetype = s.RangeFormat;
					optic.RangeTextPrefix = s.RangePrefix;
					optic.RangeTextSuffix = s.RangeSuffix;
					optic.InvalidRangeBlink = true;
					optic.ZeroOutInvalidRange = true;
					bound.Add($"range '{s.Range}'");
				}
				else problems.Add($"no readout '{s.Range}' for the range");
			}
			var ready = Item(s.Ready);
			if (ready != null)
			{
				optic.ReadyToFireObject = ready;
				ready.SetActive(false);
				bound.Add($"ready '{s.Ready}'");
			}
			var readout = o.Root.AddComponent<SightReadout>();
			readout._fcs = optic.FCS;
			if (!string.IsNullOrEmpty(s.Ammo) && s.AmmoText != null)
			{
				if (o.Readouts.TryGetValue(s.Ammo, out var ammo))
				{
					readout._ammo = ammo;
					readout._ammoText = s.AmmoText;
					bound.Add($"round '{s.Ammo}'");
				}
				else problems.Add($"no readout '{s.Ammo}' for the round");
			}
			if (!string.IsNullOrEmpty(s.LaserLamp))
			{
				readout._laser = Item(s.LaserLamp);
				if (readout._laser != null)
				{
					readout._laserRecharge = s.LaserRecharge;
					readout._laser.SetActive(true);
					if (readout._fcs != null) readout._fcs.LaserFired += readout.OnLase;
					bound.Add($"laser lamp '{s.LaserLamp}' ({s.LaserRecharge:0.#} s)");
				}
			}
			foreach (var (name, lit) in s.Lit ?? new (string, Func<bool>)[0])
			{
				var lamp = Item(name);
				if (lamp == null || lit == null) continue;
				lamp.SetActive(false);
				readout._lit.Add((lamp, lit));
				bound.Add($"lamp '{name}'");
			}
			return string.Join(", ", bound);
		}

		void OnLase() => _laserDarkUntil = Time.time + _laserRecharge;

		void OnDestroy()
		{
			if (_fcs != null && _laser != null) _fcs.LaserFired -= OnLase;
		}

		void Update()
		{
			if (_ammo != null && _fcs != null)
			{
				var ammo = _fcs.CurrentAmmoType;
				string text = ammo != null ? _ammoText(ammo) : "";
				if (_ammo.text != text) _ammo.text = text;
			}
			if (_laser != null)
			{
				bool lit = Time.time >= _laserDarkUntil;
				if (_laser.activeSelf != lit) _laser.SetActive(lit);
			}
			for (int i = 0; i < _lit.Count; i++)
			{
				var (lamp, test) = _lit[i];
				bool active;
				try { active = test(); }
				catch (Exception exception)
				{
					PatchGuard.Failed("Sight readout lamp", exception);
					_lit.RemoveAt(i--);
					active = false;
				}
				if (lamp != null && lamp.activeSelf != active) lamp.SetActive(active);
			}
		}
	}
}
