using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GHPC.Player;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace T90A.Common
{
	internal static class ShtoraPage
	{
		static GameObject _root, _panel;
		static Canvas _canvas;
		static int _layout = -1;
		static float _nextIdle;
		static Image[] _sectors;
		static RectTransform _mark, _dotParent;
		static Text _what, _done, _left, _mode;
		static float _until, _dotR, _dotSize;
		static int _lit = -1;
		static Font _font;
		static AudioSource _audio;
		static readonly List<Image> _dots = new List<Image>();
		static readonly Color PageText = new Color(0.72f, 0.95f, 0.78f, 1f), PageDim = new Color(0.10f, 0.30f, 0.19f, 1f);
		static readonly Color TubeFired = new Color(1f, 0.67f, 0.16f, 1f), TubeEmpty = new Color(0.24f, 0.35f, 0.27f, 1f);
		static Color _text = PageText, _dim = PageDim, _red = new Color(1f, 0.23f, 0.13f, 1f);
		const float RingIn = 40f, RingOut = 62f;
		const float PageScale = 0.8f;

		internal static void Alarm(int h, ShtoraScreen.Station st, float az, string done)
		{
			try
			{
				Build(st.S);
				Fill(h, st, az, done);
				_lit = h;
				_until = Time.unscaledTime + 5f;
				_panel.SetActive(!WorldScript.HideHud);
				if (st.S.PageTone && _audio?.clip != null && !_audio.isPlaying) _audio.Play();
			}
			catch (Exception e) { MelonLogger.Error("Shtora page failed: " + e.Message); }
		}

		internal static void Ensure(ShtoraScreen.Settings s)
		{
			try { Build(s); }
			catch (Exception e) { MelonLogger.Error("Shtora page failed: " + e.Message); }
		}

		static void Build(ShtoraScreen.Settings s)
		{
			if (_root != null) return;
			_root = new GameObject("Shtora page");
			UnityEngine.Object.DontDestroyOnLoad(_root);
			var canvas = _canvas = _root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
			var scaler = _root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 1f;
			_root.AddComponent<Ticker>();
			_audio = _root.AddComponent<AudioSource>();
			_audio.spatialBlend = 0f; _audio.playOnAwake = false; _audio.volume = 0.6f;
			_audio.clip = ShtoraScreen.Hud.Wav("alarm.wav");
			_panel = new GameObject("multifunction screen page", typeof(RectTransform));
			_panel.transform.SetParent(_root.transform, false);
			var panel = (RectTransform)_panel.transform;
			panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
			panel.anchoredPosition = new Vector2(-40f, 110f);
			panel.localScale = new Vector3(PageScale, PageScale, 1f);
			_panel.SetActive(false);
			var art = ShtoraScreen.Hud.Picture("pmf_page.png"); var over = ShtoraScreen.Hud.Picture("pmf_over.png"); var words = Words("pmf_layout.txt");
			if (art != null && over != null && words != null) BuildArt(panel, s, art, over, words);
			else { ShtoraScreen.Hud.Free(art); ShtoraScreen.Hud.Free(over); BuildPlain(panel, s); }
		}

		static void BuildArt(RectTransform panel, ShtoraScreen.Settings s, Sprite art, Sprite over, Dictionary<string, string[]> lay)
		{
			float N(string key, int i, float def) => lay.TryGetValue(key, out var w) && i < w.Length && float.TryParse(w[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
			Color C(int i, Color def) => lay.ContainsKey("colours") ? new Color(N("colours", 3 * i, 0f) / 255f, N("colours", 3 * i + 1, 0f) / 255f, N("colours", 3 * i + 2, 0f) / 255f, 1f) : def;
			float k = 1f / N("scale", 0, 1f);
			Vector2 At(float x, float y) => new Vector2(x * k, -y * k);
			panel.sizeDelta = new Vector2(art.rect.width * k, art.rect.height * k);
			var bg = panel.gameObject.AddComponent<Image>(); bg.sprite = art; bg.raycastTarget = false;
			_text = C(0, PageText); _dim = C(1, PageDim); _red = C(2, _red);
			float rin = N("ring", 2, 148f) * k, rout = N("ring", 3, 224f) * k;
			var ringAt = At(N("ring", 0, 0f), N("ring", 1, 0f));
			Sectors(ShtoraScreen.Hud.Box("ring", panel, ringAt, new Vector2(2f * rout, 2f * rout)), s, rin, rout);
			var top = ShtoraScreen.Hud.Box("over", panel, new Vector2(panel.sizeDelta.x / 2f, -panel.sizeDelta.y / 2f), panel.sizeDelta).gameObject.AddComponent<Image>();
			top.sprite = over; top.raycastTarget = false;
			_dotParent = ShtoraScreen.Hud.Box("tubes", panel, ringAt, new Vector2(2f * rout, 2f * rout));
			_dotR = N("dots", 0, 248f) * k; _dotSize = N("dots", 1, 20f) * k;
			Needle(ShtoraScreen.Hud.Box("needle", panel, ringAt, new Vector2(2f * rout, 2f * rout)), rin, rout, 1.6f);
			_font = LoadFont(lay.TryGetValue("font", out var fw) && fw.Length > 0 ? string.Join(" ", fw) : null);
			float em = _font == Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ? 1.1f : 1.25f;
			int Size(float px) => Mathf.Max(6, Mathf.RoundToInt(px * k * em));
			_mode = Line(s.Automatic ? "АВТ" : "П/АВТ", panel, At(N("mode", 0, 0f), N("mode", 1, 0f)), new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, Size(N("mode", 2, 36f)), new Vector2(40f, 16f));
			float lx = N("lines", 0, 0f), ly = N("lines", 1, 0f), step = N("lines", 2, 68f);
			int lineSize = Size(N("lines", 3, 36f));
			var box = new Vector2(panel.sizeDelta.x - lx * k - 6f, step * k);
			_what = Line("", panel, At(lx, ly), new Vector2(0f, 1f), TextAnchor.UpperLeft, lineSize, box);
			_done = Line("", panel, At(lx, ly + step), new Vector2(0f, 1f), TextAnchor.UpperLeft, lineSize, box);
			_left = Line("", panel, At(lx, ly + 2f * step), new Vector2(0f, 1f), TextAnchor.UpperLeft, lineSize, box);
		}

		static void BuildPlain(RectTransform panel, ShtoraScreen.Settings s)
		{
			panel.sizeDelta = new Vector2(262f, 268f);
			var bezel = panel.gameObject.AddComponent<Image>();
			bezel.color = new Color(0.17f, 0.18f, 0.17f, 0.97f);
			var edge = panel.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(0.05f, 0.05f, 0.05f, 1f); edge.effectDistance = new Vector2(2f, -2f);
			for (int i = 0; i < 5; i++) Key(panel, new Vector2(43f + i * 44f, -250f), new Vector2(30f, 14f));
			for (int i = 0; i < 3; i++) { Key(panel, new Vector2(9f, -60f - i * 60f), new Vector2(10f, 26f)); Key(panel, new Vector2(253f, -60f - i * 60f), new Vector2(10f, 26f)); }
			var screen = ShtoraScreen.Hud.Box("screen", panel, new Vector2(131f, -118f), new Vector2(226f, 220f));
			var bg = screen.gameObject.AddComponent<Image>(); bg.color = new Color(0.015f, 0.035f, 0.04f, 1f);
			_font = LoadFont(null);
			Line(s.PageTitle, screen, new Vector2(8f, -4f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 14, new Vector2(150f, 18f));
			_mode = Line(s.Automatic ? "АВТ" : "П/АВТ", screen, new Vector2(196f, -13f), new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, 14, new Vector2(50f, 18f));
			var centre = ShtoraScreen.Hud.Box("ring", screen, new Vector2(113f, -92f), new Vector2(2f * RingOut, 2f * RingOut));
			Sectors(centre, s, RingIn, RingOut);
			var turret = ShtoraScreen.Hud.Box("turret", centre, new Vector2(RingOut, -RingOut), new Vector2(2f * RingIn - 10f, 2f * RingIn - 10f)).gameObject.AddComponent<Image>();
			turret.sprite = TurretTop(); turret.color = PageText;
			Needle(centre, RingIn, RingOut, 3f);
			var box = new Vector2(212f, 17f);
			_what = Line("", screen, new Vector2(8f, -162f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 13, box);
			_done = Line("", screen, new Vector2(8f, -179f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 13, box);
			_left = Line("", screen, new Vector2(8f, -196f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 13, box);
		}

		static void Sectors(RectTransform centre, ShtoraScreen.Settings s, float rin, float rout)
		{
			var ring = Ring(rin / rout);
			float p = s.PreciseHalfAz, gap = 1.5f;
			var spans = new[] { (-p, 0f), (0f, p), (-180f, -p), (p, 180f) };
			_sectors = new Image[4];
			for (int i = 0; i < 4; i++)
			{
				var sec = new GameObject("sector " + ShtoraScreen.HeadNames[i], typeof(RectTransform));
				sec.transform.SetParent(centre, false);
				var rt = (RectTransform)sec.transform;
				rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
				rt.localEulerAngles = new Vector3(0f, 0f, -(spans[i].Item1 + gap));
				var img = sec.AddComponent<Image>();
				img.sprite = ring; img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360;
				img.fillOrigin = (int)Image.Origin360.Top; img.fillClockwise = true;
				img.fillAmount = (spans[i].Item2 - spans[i].Item1 - 2f * gap) / 360f;
				img.color = _dim; img.raycastTarget = false;
				_sectors[i] = img;
			}
		}

		static void Needle(RectTransform centre, float rin, float rout, float width)
		{
			var mark = new GameObject("direction", typeof(RectTransform));
			mark.transform.SetParent(centre, false);
			_mark = (RectTransform)mark.transform;
			_mark.anchorMin = _mark.anchorMax = new Vector2(0.5f, 0.5f); _mark.sizeDelta = Vector2.zero;
			var bar = ShtoraScreen.Hud.Box("bar", _mark, Vector2.zero, new Vector2(width, rout - rin + 3f));
			bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f); bar.anchoredPosition = new Vector2(0f, (rin + rout) / 2f + 0.5f);
			var img = bar.gameObject.AddComponent<Image>(); img.color = new Color(1f, 0.89f, 0.43f, 1f); img.raycastTarget = false;
			mark.SetActive(false);
		}

		static void Key(Transform parent, Vector2 pos, Vector2 size)
		{
			var k = ShtoraScreen.Hud.Box("key", parent, pos, size).gameObject.AddComponent<Image>();
			k.color = new Color(0.30f, 0.31f, 0.30f, 1f);
			var o = k.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.06f, 0.06f, 0.06f, 1f); o.effectDistance = new Vector2(1f, -1f);
		}

		static Text Line(string s, Transform parent, Vector2 pos, Vector2 pivot, TextAnchor align, int size, Vector2 box)
		{
			var go = new GameObject("text", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			var rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = pivot;
			rt.anchoredPosition = pos; rt.sizeDelta = box;
			var t = go.AddComponent<Text>();
			t.font = _font; t.text = s; t.fontSize = size; t.color = _text; t.alignment = align;
			t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
			return t;
		}

		static Font LoadFont(string name)
		{
			if (_font != null) return _font;
			try { if (!string.IsNullOrEmpty(name) && Font.GetOSInstalledFontNames().Contains(name)) _font = Font.CreateDynamicFontFromOSFont(name, 32); }
			catch (Exception e) { MelonLogger.Warning($"Shtora page font '{name}' not loaded ({e.Message}); Unity's own font used."); }
			return _font ?? (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
		}

		static Dictionary<string, string[]> Words(string name)
		{
			var bytes = ShtoraScreen.Hud.Resource(name);
			if (bytes == null) return null;
			var d = new Dictionary<string, string[]>();
			foreach (var raw in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0 || line[0] == '#') continue;
				var f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (!d.ContainsKey(f[0])) d[f[0]] = f.Skip(1).ToArray();
			}
			return d;
		}

		static void Fill(int h, ShtoraScreen.Station st, float az, string done)
		{
			var s = st?.S;
			string head = h < 2 ? "ТОЧНАЯ" : "ГРУБАЯ";
			string side = h % 2 == 0 ? "ЛЕВ." : "ПРАВ.";
			bool dir = !float.IsNaN(az) && s != null;
			float q = dir ? Mathf.Round(az / s.PreciseStep) * s.PreciseStep : 0f;
			_what.text = $"ОБЛУЧЕНИЕ: {head} {side}" + (dir ? $"  {(q < 0f ? "-" : q > 0f ? "+" : "")}{Mathf.Floor(Mathf.Abs(q)):0}°{Mathf.Round(Mathf.Abs(q) % 1f * 60f):00}'" : "");
			_done.text = Done(done, st);
			_mark.localEulerAngles = new Vector3(0f, 0f, -q);
			_mark.gameObject.SetActive(dir);
			Status(st);
		}

		static void Idle(ShtoraScreen.Station st)
		{
			_what.text = "ОБЛУЧЕНИЯ НЕТ";
			_done.text = "";
			_mark.gameObject.SetActive(false);
			Status(st);
		}

		static void Status(ShtoraScreen.Station st)
		{
			_left.text = $"ГРАНАТЫ 3Д17: {st?.Slots?.Sum(x => x.Rounds) ?? 0}";
			if (st?.S != null) _mode.text = st.S.Automatic ? "АВТ" : "П/АВТ";
			if (_dotParent == null || st?.Slots == null || st.SlotAz == null) return;
			while (_dots.Count < st.Slots.Length)
			{
				var d = ShtoraScreen.Hud.Box("tube", _dotParent, Vector2.zero, new Vector2(_dotSize, _dotSize));
				d.anchorMin = d.anchorMax = new Vector2(0.5f, 0.5f);
				var img = d.gameObject.AddComponent<Image>(); img.sprite = ShtoraScreen.Hud.Dot(); img.raycastTarget = false;
				_dots.Add(img);
			}
			for (int i = 0; i < _dots.Count; i++)
			{
				bool has = i < st.Slots.Length;
				_dots[i].gameObject.SetActive(has);
				if (!has) continue;
				float a = st.SlotAz[i] * Mathf.Deg2Rad;
				((RectTransform)_dots[i].transform).anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * _dotR;
				bool fresh = i == st.LastScreenSlot && Time.time - st.LastScreenAt < 6f;
				_dots[i].color = fresh ? TubeFired : st.Slots[i].Rounds > 0 ? _text : TubeEmpty;
			}
		}

		static string Done(string done, ShtoraScreen.Station st)
		{
			if (string.IsNullOrEmpty(done)) return "";
			if (done.StartsWith("screen fired")) return $"ЗАВЕСА: ПУ {st?.LastScreenSlot + 1}";
			if (done.Contains("hatch open")) return "БЛОКИРОВКА: ЛЮК ОТКРЫТ";
			if (done.Contains("own shot")) return "БЛОКИРОВКА: ВЫСТРЕЛ";
			if (done.Contains("already standing")) return "ЗАВЕСА ПОСТАВЛЕНА";
			if (done.Contains("no grenades")) return "НЕТ ГРАНАТ";
			if (done.Contains("semi-automatic")) return "ЗАВЕСА: ПО КОМАНДЕ";
			if (done.StartsWith("coarse")) return "ДОВОРОТ БАШНИ ПО КНОПКЕ";
			return "НЕИСПРАВНОСТЬ";
		}

		static Sprite _ring, _turretTop;
		static Sprite Ring(float ratio)
		{
			if (_ring != null) return _ring;
			const int n = 512;
			var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
			float ro = n / 2f, ri = ro * ratio;
			var px = new Color32[n * n];
			for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
			{
				float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(ro, ro));
				px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(Mathf.Min(ro - 1f - d, d - ri))));
			}
			tex.SetPixels32(px);
			tex.Apply();
			_ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
			return _ring;
		}

		static Sprite TurretTop()
		{
			if (_turretTop != null) return _turretTop;
			const int n = 128;
			var body = new[] { new Vector2(-0.55f, 0.30f), new Vector2(-0.22f, 0.56f), new Vector2(0.22f, 0.56f), new Vector2(0.55f, 0.30f),
				new Vector2(0.60f, -0.38f), new Vector2(0.48f, -0.88f), new Vector2(-0.48f, -0.88f), new Vector2(-0.60f, -0.38f) };
			var gun = new[] { new Vector2(-0.045f, 0.50f), new Vector2(0.045f, 0.50f), new Vector2(0.045f, 1.0f), new Vector2(-0.045f, 1.0f) };
			var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			float px = 2f / n;
			for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
			{
				var p = new Vector2((x + 0.5f) * px - 1f, (y + 0.5f) * px - 1f);
				float a = Mathf.Max(EdgeAlpha(body, p, px), Inside(gun, p) ? 1f : 0f);
				if (Inside(body, p)) a = Mathf.Max(a, 0.22f);
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
			}
			tex.Apply();
			_turretTop = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
			return _turretTop;
		}

		static bool Inside(Vector2[] poly, Vector2 p)
		{
			bool inside = false;
			for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
				if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
			return inside;
		}

		static float EdgeAlpha(Vector2[] poly, Vector2 p, float px)
		{
			float best = float.MaxValue;
			for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
			{
				var a = poly[j]; var b = poly[i]; var ab = b - a;
				float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
				best = Mathf.Min(best, Vector2.Distance(p, a + t * ab));
			}
			return Mathf.Clamp01(1.75f - best / px);
		}

		static void Place(bool inSight)
		{
			if (_layout == (inSight ? 1 : 0)) return;
			_layout = inSight ? 1 : 0;
			var panel = (RectTransform)_panel.transform;
			panel.anchoredPosition = inSight ? ShtoraScreen.Hud.SightAt : ShtoraScreen.Hud.OutsideAt;
			float k = inSight ? Mathf.Min(PageScale, ShtoraScreen.Hud.SightMaxHeight / panel.sizeDelta.y) : PageScale;
			panel.localScale = new Vector3(k, k, 1f);
		}

		sealed class Ticker : MonoBehaviour
		{
			void Update()
			{
				if (_sectors == null || _panel == null) return;
				float left = _until - Time.unscaledTime;
				bool blinkOn = left > 1.5f && (Time.unscaledTime * 2.5f) % 1f < 0.6f;
				for (int i = 0; i < 4; i++) _sectors[i].color = i == _lit && left > 1.5f ? _red : _dim;
				if (_what != null) _what.color = _lit >= 0 && blinkOn ? _red : _text;
				if (left <= 0f) _lit = -1;
				var st = PlayerInput.Instance != null ? ShtoraScreen.Of(PlayerInput.Instance.CurrentPlayerUnit) : null;
				bool always = st != null && st.S.Display == ShtoraScreen.Look.ScreenPage && st.S.PanelAlways && st.Vehicle != null && !st.Vehicle.Neutralized;
				bool show = !WorldScript.HideHud && (left > 0f || always && ShtoraScreen.Hud.GameHud(_canvas));
				if (always && left <= 0f && Time.unscaledTime >= _nextIdle) { _nextIdle = Time.unscaledTime + 0.5f; Idle(st); }
				if (_panel.activeSelf != show) _panel.SetActive(show);
				if (show) Place(ShtoraScreen.Hud.InSight());
			}
		}
	}
}
