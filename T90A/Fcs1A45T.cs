using System.Linq;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using T90A.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace T90A
{
	public static class Fcs1A45T
	{
		const float LaserMin = 400f;
		const float LaserMax = 5000f;

		// Sight goes past the gun's stops, the fire gate just stays shut.
		internal static readonly Vector2 LineOfSightLimits = new Vector2(-15f, 20f);

		static readonly GunnerFcs.Settings Settings = new GunnerFcs.Settings
		{
			LaserMin = LaserMin,
			LaserMax = LaserMax,
			LineOfSightLimits = LineOfSightLimits,
			LineOfSightAzimuth = 8f,
			RangeStep = 100,
			DisplayRangeIncrement = 50,
			FireGateAngle = 0.1f,   // 1V528's isn't published, this is GHPC's 1A33 value
			FireWhenLaid = () => T90AMod.FireWhenLaid == null || T90AMod.FireWhenLaid.Value,
		};

		public static void Apply(UsableOptic dayOptic)
		{
			GunnerFcs.Apply(dayOptic, Settings);
			var fcs = dayOptic.FCS;
			if (fcs != null)
			{
				GunnerFcs.LaserAim.Register(fcs, dayOptic, Settings);
			}
			Readout.Build(dayOptic);
		}

		public class Readout : MonoBehaviour
		{
			internal const float StripHeight = 61f;
			const float TextY = 37f;
			const float LampX = 130f;
			const float LetterX = -78f;
			const float RangeX = 30f;
			const float LampSize = 18f;

			internal static readonly Color LampReady = new Color(0.157f, 1f, 0f, 1f);
			static readonly Color LampOverride = new Color(1f, 0.11f, 0.17f, 1f);

			static TMP_FontAsset font;
			static Sprite lamp;

			FireControlSystem fcs;
			TextMeshProUGUI letter;

			public static void Build(UsableOptic optic)
			{
				if (optic.transform.Find("1G46 Canvas") != null) return;

				var root = new GameObject("1G46 Canvas", typeof(RectTransform));
				root.transform.SetParent(optic.transform, false);
				var canvas = ((Canvas)root.AddComponent(typeof(Canvas)));
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = -1;
				var scaler = ((CanvasScaler)root.AddComponent(typeof(CanvasScaler)));
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
				scaler.referenceResolution = new Vector2(800f, 600f);
				scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
				scaler.matchWidthOrHeight = 1f;

				var strip = Rect("strip", root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
					new Vector2(0f, StripHeight / 2f), new Vector2(0f, StripHeight));
				((Image)strip.gameObject.AddComponent(typeof(Image))).color = Color.black;

				var range = Text("range text", root.transform, RangeX, 120f, TextAlignmentOptions.Right, "0000");
				var readout = ((Readout)root.AddComponent(typeof(Readout)));
				readout.fcs = optic.FCS;
				readout.letter = Text("ammo text", root.transform, LetterX, 24f, TextAlignmentOptions.Center, "6");

				optic.RangeText = range;
				optic.RangeTextQuantize = 5;
				optic.RangeTextDivideBy = 1;
				optic.RangeTextArchetype = "0000";
				optic.RangeTextPrefix = "<mspace=0.9em>";
				optic.RangeTextSuffix = "</mspace>";
				optic.InvalidRangeBlink = true;
				optic.ZeroOutInvalidRange = true;
				optic.ReadyToFireObject = Lamp("ready indicator", root.transform, -LampX, LampReady);
				optic.OverridingObject = Lamp("override indicator", root.transform, LampX, LampOverride);
			}

			void Update()
			{
				AmmoType ammo = fcs != null ? fcs.CurrentAmmoType : null;
				if (ammo == null) return;
				string glyph = Glyph(ammo);
				if (letter.text != glyph) letter.text = glyph;
			}

			internal static string Glyph(AmmoType ammo) => SightReadout.Glyph7(ammo);

			internal static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
				Vector2 pos, Vector2 size)
			{
				var go = new GameObject(name, typeof(RectTransform));
				go.transform.SetParent(parent, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = anchorMin;
				rt.anchorMax = anchorMax;
				rt.pivot = new Vector2(0.5f, 0.5f);
				rt.anchoredPosition = pos;
				rt.sizeDelta = size;
				return rt;
			}

			internal static TextMeshProUGUI Text(string name, Transform parent, float x, float width,
				TextAlignmentOptions align, string text)
			{
				var rt = Rect(name, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
					new Vector2(x, TextY), new Vector2(width, 40f));
				var tmp = ((TextMeshProUGUI)rt.gameObject.AddComponent(typeof(TextMeshProUGUI)));
				TMP_FontAsset f = Font();
				if (f != null) tmp.font = f;
				tmp.fontSize = 30f;
				tmp.color = Color.white;
				tmp.alignment = align;
				tmp.enableWordWrapping = false;
				tmp.richText = true;
				tmp.text = text;
				return tmp;
			}

			internal static GameObject Lamp(string name, Transform parent, float x, Color color)
			{
				var off = Rect(name + " (unlit)", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
					new Vector2(x, TextY), new Vector2(LampSize, LampSize));
				var offImg = ((Image)off.gameObject.AddComponent(typeof(Image)));
				offImg.sprite = LampSprite();
				offImg.color = new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 1f);

				var rt = Rect(name, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
					new Vector2(x, TextY), new Vector2(LampSize, LampSize));
				var img = ((Image)rt.gameObject.AddComponent(typeof(Image)));
				img.sprite = LampSprite();
				img.color = color;
				rt.gameObject.SetActive(false);
				return rt.gameObject;
			}

			internal static TMP_FontAsset Font()
			{
				if (font != null) return font;
				const string name = "Soviet7SegmentFont SDF";
				font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == name)
					?? Resources.LoadAll<TMP_FontAsset>("").FirstOrDefault(f => f.name == name);
				return font;
			}

			internal static Sprite LampSprite()
			{
				if (lamp != null) return lamp;
				const int n = 64;
				var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
				float r = n / 2f;
				for (int y = 0; y != n; y++)
					for (int x = 0; x != n; x++)
					{
						float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
						tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - 1f - d)));
					}
				tex.Apply();
				lamp = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f));
				return lamp;
			}
		}
	}
}
