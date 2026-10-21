using GHPC.Equipment.Optics;
using GHPC.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
			var root = new GameObject("TKN-4S indicator Canvas", typeof(RectTransform));
			root.transform.SetParent(day.transform, false);
			var canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = -1;
			var scaler = root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(800f, 600f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 1f;
			float u = 600f / (CommanderSight.DayFov * Mathf.Deg2Rad * 1000f);

			float h = Fcs1A45T.Readout.StripHeight;
			var strip = Fcs1A45T.Readout.Rect("strip", root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, h / 2f), new Vector2(0f, h));
			strip.gameObject.AddComponent<Image>().color = Color.black;
			var r = root.AddComponent<Tkn4sReadout>();
			r._gun = gun; r._gunFcs = gunFcs;
			r._digit = Fcs1A45T.Readout.Text("ammo text", root.transform, Tkn4sReticle.IndicatorX * u, 24f, TextAlignmentOptions.Center, "");
			r._lit = Fcs1A45T.Readout.Lamp("ready indicator", root.transform, Tkn4sReticle.ReadyX * u, Fcs1A45T.Readout.LampReady);
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
