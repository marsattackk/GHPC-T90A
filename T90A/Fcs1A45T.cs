using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using HarmonyLib;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace T90A
{
	public static class Fcs1A45T
	{
		const float LaserMin = 400f;
		const float LaserMax = 5000f;

		const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
		internal static readonly Vector2 LineOfSightLimits = new Vector2(-15f, 20f);

		internal static class AimClamp
		{
			static readonly Dictionary<FireControlSystem, List<UsableOptic>> Sights =
				new Dictionary<FireControlSystem, List<UsableOptic>>();
			static readonly MethodInfo SetAim = AccessTools.PropertySetter(typeof(FireControlSystem), nameof(FireControlSystem.AimWorldVector));

			internal static void Register(FireControlSystem fcs, UsableOptic optic)
			{
				if (fcs == null || optic == null) return;
				foreach (var dead in Sights.Keys.Where(k => k == null).ToList()) Sights.Remove(dead);
				if (!Sights.TryGetValue(fcs, out var l)) Sights[fcs] = l = new List<UsableOptic>();
				if (!l.Contains(optic)) l.Add(optic);
			}

			[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.AdjustAimByRatio))]
			static class Patch
			{
				static void Postfix(FireControlSystem __instance)
				{
					if (!Sights.TryGetValue(__instance, out var optics)) return;
					var mode = __instance.CurrentStabMode;
					if (mode != StabilizationMode.Vector && mode != StabilizationMode.None) return;
					UsableOptic o = null;
					foreach (var x in optics) if (x != null && x.slot != null && x.slot.IsActive) { o = x; break; }
					if (o == null) return;
					var parent = o.slot.transform.parent;
					if (parent == null) return;
					var lim = o.LocalElevationLimits;
					if (lim == Vector2.zero) return;
					var d = parent.InverseTransformDirection(__instance.AimWorldVector).normalized;
					float e = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
					float lo = lim.x != 0f ? lim.x : -90f, hi = lim.y != 0f ? lim.y : 90f;
					if (e >= lo && e <= hi) return;
					float c = Mathf.Clamp(e, lo, hi) * Mathf.Deg2Rad;
					var flat = new Vector3(d.x, 0f, d.z);
					flat = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
					var w = parent.TransformDirection(flat * Mathf.Cos(c) + Vector3.up * Mathf.Sin(c));
					if (mode == StabilizationMode.Vector) __instance.SetAimVector(w, true);
					else SetAim.Invoke(__instance, new object[] { w });
				}
			}
		}

		public static void Apply(UsableOptic dayOptic)
		{
			FireControlSystem fcs = dayOptic.FCS;

			fcs.MaxLaserRange = LaserMax;
			fcs.RegisteredRangeLimits = new Vector2(0f, LaserMax);
			fcs.MarkRangeInvalidBelow = LaserMin;
			fcs.IgnoreRangeBelow = 0f;
			fcs.LaserAim = LaserAimMode.ImpactPoint;
			fcs.randomRadius = new AngularLength(5f, AngularLength.AngularUnit.MIL_NATO);
			fcs.RangeStep = 100;
			fcs.RangeGranularity = 0;
			fcs.DisplayRangeIncrement = 50;
			fcs.EnableRangeInterpolation = false;

			fcs.UseDeltaD = true;
			fcs.ImperfectDeltaD = true;

			fcs.CurrentStabMode = StabilizationMode.Vector;
			fcs.ImperfectFixedRangePointStab = false;
			fcs.PointStabIncludesVertical = false;
			fcs.SuperelevateWeapon = true;
			fcs.SuperleadWeapon = true;
			fcs.RefreshAimOnAmmoChange = true;

			fcs.RecordTraverseRateBuffer = true;
			fcs.TraverseBufferSeconds = 1f;
			fcs.DynamicLead = true;
			fcs.InertialCompensation = false;
			SetPrivate(fcs, "PointStabAffectsLead", false);
			SetPrivate(fcs, "InertiaLeadIncludesVertical", false);
			SetPrivate(fcs, "_counterMotionLeadAlwaysOn", false);
			SetPrivate(fcs, "_autoDumpViaPalmSwitches", false);
			SetPrivate(fcs, "_horizontalLeadOnly", true);
			SetPrivate(fcs, "_useSeparateLead", false);

			fcs.FireGateAngle = 0.1f;
			fcs.SuperelevateFireGating = true;
			fcs.IgnoreHorizontalForFireGating = true;

			fcs.HasManualMode = true;
			fcs.ManualModeTriggers = new[] { FcsManualModeStartTrigger.SetRangeManually };
			fcs.AutoModeTriggers = new[] { FcsManualModeCancelTrigger.Lase };
			fcs.ManualModeLead = FcsManualModeLead.DisableLeadCompensation;
			fcs.ManualModeSuperelevation = FcsManualModeSuperelevation.DisableSuperelevation;
			fcs.ManualModeOpticAlign = OpticAlignment.BoresightStabilized;
			fcs.ZeroReticleRangeInAutoMode = true;

			dayOptic.Alignment = OpticAlignment.BoresightStabilized;
			dayOptic.RotateElevation = true;
			dayOptic.RotateAzimuth = true;
			dayOptic.CantCorrect = true;
			dayOptic.ForceHorizontalReticleAlign = true;
			dayOptic.LocalElevationLimits = LineOfSightLimits;
			AimClamp.Register(fcs, dayOptic);

			Readout.Build(dayOptic);
			Resync(fcs, dayOptic);
		}

		static void Resync(FireControlSystem fcs, UsableOptic dayOptic)
		{
			SetPrivate(fcs, "_manualModeOnRangeSet", true);
			SetPrivate(fcs, "_autoModeOnLase", true);
			SetPrivate(fcs, "_originalSuperelevationMode", true);
			SetPrivate(fcs, "_originalSuperleadMode", true);
			SetPrivate(fcs, "_originalRangeLimits", fcs.RegisteredRangeLimits);
			SetPrivate(fcs, "_originalRangeStep", fcs.RangeStep);
			SetPrivate(fcs, "_recordedOriginalRangeValues", true);
			SetPrivate(fcs, "_originalMainOpticAlign", dayOptic.Alignment);

			if (!(bool)typeof(FireControlSystem).GetField("_initialized", Private).GetValue(fcs)) return;

			typeof(FireControlSystem).GetMethod("SetManualMode", Private).Invoke(fcs, new object[] { false });
			typeof(FireControlSystem).GetMethod("EngageLead", Private).Invoke(fcs, null);
		}

		static void SetPrivate(object target, string field, object value)
		{
			typeof(FireControlSystem).GetField(field, Private).SetValue(target, value);
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

			internal static string Glyph(AmmoType ammo) =>
				ammo.Guidance != AmmoType.GuidanceType.Unguided ? "Y"
				: ammo.Category == AmmoType.AmmoCategory.Penetrator ? "6"
				: ammo.Category == AmmoType.AmmoCategory.ShapedCharge ? "K"
				: "0";

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
