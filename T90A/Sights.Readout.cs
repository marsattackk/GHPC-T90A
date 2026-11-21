using System.Linq;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using T90A.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace T90A
{
	// The strip under the day sight: the laser lamp, the ammo letter and the range.
	internal static partial class Sights
	{
		internal class Readout : MonoBehaviour
		{
			internal const float StripHeight = 61f;
			const float TextY = 37f;
			const float LampX = 130f;
			const float LetterX = -78f;
			const float RangeX = 30f;
			const float LampSize = 18f;

			// Green ready lamp on the left, red override lamp on the right, as the legend of the manual's figure 9 has them.
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
				optic.RangeTextQuantize = 5;   // the digits in the sight, in 5 m steps as the 1G42's. Not the weapon info's 50 m
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
				var gameObject = new GameObject(name, typeof(RectTransform));
				gameObject.transform.SetParent(parent, false);
				var rectTransform = (RectTransform)gameObject.transform;
				rectTransform.anchorMin = anchorMin;
				rectTransform.anchorMax = anchorMax;
				rectTransform.pivot = new Vector2(0.5f, 0.5f);
				rectTransform.anchoredPosition = pos;
				rectTransform.sizeDelta = size;
				return rectTransform;
			}

			internal static TextMeshProUGUI Text(string name, Transform parent, float x, float width,
				TextAlignmentOptions align, string text)
			{
				var rectTransform = Rect(name, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
					new Vector2(x, TextY), new Vector2(width, 40f));
				var label = ((TextMeshProUGUI)rectTransform.gameObject.AddComponent(typeof(TextMeshProUGUI)));
				TMP_FontAsset fontAsset = Font();
				if (fontAsset != null) label.font = fontAsset;
				label.fontSize = 30f;
				label.color = Color.white;
				label.alignment = align;
				label.enableWordWrapping = false;
				label.richText = true;
				label.text = text;
				return label;
			}

			internal static GameObject Lamp(string name, Transform parent, float x, Color color)
			{
				var off = Rect(name + " (unlit)", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
					new Vector2(x, TextY), new Vector2(LampSize, LampSize));
				var offImg = ((Image)off.gameObject.AddComponent(typeof(Image)));
				offImg.sprite = LampSprite();
				offImg.color = new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 1f);

				var litRect = Rect(name, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
					new Vector2(x, TextY), new Vector2(LampSize, LampSize));
				var img = ((Image)litRect.gameObject.AddComponent(typeof(Image)));
				img.sprite = LampSprite();
				img.color = color;
				litRect.gameObject.SetActive(false);
				return litRect.gameObject;
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
				const int size = 64;
				var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
				float radius = size / 2f;
				for (int y = 0; y != size; y++)
					for (int x = 0; x != size; x++)
					{
						float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
						tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - 1f - distance)));
					}
				tex.Apply();
				lamp = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
				return lamp;
			}
		}
	}
}
