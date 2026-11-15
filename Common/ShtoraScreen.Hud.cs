using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GHPC.Player;
using GHPC.UI.Hud;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace T90A.Common
{
	// The warning panel: four lamps, the alarm and where it sits on screen. Drawn by hand when the pictures are
	// missing.
	internal static partial class ShtoraScreen
	{
		internal static int Head(float az, Settings s) => Mathf.Abs(az) <= s.PreciseHalfAz ? (az < 0f ? 0 : 1) : (az < 0f ? 2 : 3);
		internal static readonly string[] HeadNames = { "front left", "front right", "rear left", "rear right" };

		internal static class Hud
		{
			static GameObject _root, _panel;
			static Canvas _canvas;
			static WeaponHud _weaponHud;
			static Text _weaponHudText;
			static float _nextHudLook;
			static Image[] _lamps;
			static Image _spz;
			static AudioSource _audio;
			static float _until;
			static int _lit = -1;
			static Sprite _lampOn, _lampOff;
			static string _art = "drawn";
			static readonly Color LampOff = new Color(0.30f, 0.05f, 0.05f, 1f), LampOn = new Color(1f, 0.16f, 0.10f, 1f);

			internal static void Alarm(int h, bool tone = true)
			{
				try
				{
					Build();
					_lit = h;
					_until = Time.unscaledTime + 5f;
					if (_audio != null && tone && !_audio.isPlaying) _audio.Play();
				}
				catch (Exception exception) { MelonLogger.Error("Shtora panel failed: " + exception.Message); }
			}

			internal static void Ensure()
			{
				try { Build(); }
				catch (Exception exception) { MelonLogger.Error("Shtora panel failed: " + exception.Message); }
			}

			static bool Wanted()
			{
				var station = PlayerInput.Instance != null ? Of(PlayerInput.Instance.CurrentPlayerUnit) : null;
				if (station == null
					|| !station.S.PanelShown
					|| station.Vehicle == null
					|| station.Vehicle.Neutralized
					|| WorldScript.HideHud)
					return false;
				if (!station.S.PanelAlways && Time.unscaledTime > _until) return false;
				return GameHud(_canvas);
			}

			internal static bool GameHud(Canvas own)
			{
				if (_weaponHud == null && Time.unscaledTime >= _nextHudLook)
				{
					_nextHudLook = Time.unscaledTime + 1f;
					_weaponHud = UnityEngine.Object.FindObjectOfType<WeaponHud>();
					_weaponHudText = _weaponHud != null ? _weaponHud.GetComponent<Text>() : null;
				}
				var hud = _weaponHud != null ? _weaponHud.GetComponentInParent<Canvas>()?.rootCanvas : null;
				if (own != null && hud != null && (own.sortingLayerID != hud.sortingLayerID || own.sortingOrder != hud.sortingOrder))
				{
					own.sortingLayerID = hud.sortingLayerID;
					own.sortingOrder = hud.sortingOrder;
				}
				return _weaponHud != null && _weaponHud.isActiveAndEnabled && _weaponHudText != null && _weaponHudText.enabled;
			}

			internal static bool InSight()
			{
				var slot = GHPC.Camera.CameraSlot.ActiveInstance;
				return slot != null && !slot.IsExterior;
			}

			internal const float SightMaxHeight = 100f;
			internal static readonly Vector2 OutsideAt = new Vector2(-40f, 110f), SightAt = new Vector2(-280f, 5f);

			internal const float MonitorMargin = 10f, MonitorFoot = 70f, MonitorMinBar = 120f;
			internal static (Vector2 at, float scale)? MonitorBar(Vector2 size)
			{
				if (!ThermalSight.NoScopeMask.Showing || Screen.height <= 0 || size.x <= 0f || size.y <= 0f) return null;
				float bar = (1080f * Screen.width / Screen.height - 1440f) / 2f;
				if (bar < MonitorMinBar) return null;
				float fitScale = Mathf.Min((bar - 2f * MonitorMargin) / size.x, (1080f - MonitorFoot - MonitorMargin) / size.y);
				return (new Vector2(-(bar - size.x * fitScale) / 2f, MonitorFoot), fitScale);
			}

			internal static int LayoutKey(bool inSight)
				=> !inSight ? 0 : !ThermalSight.NoScopeMask.Showing ? 1 : 2 + Screen.width * 8192 + Screen.height;

			static int _layout = -1;
			static void Fit(RectTransform panel)
			{
				_layout = -1;
				Place(panel, false);
			}

			static void Place(RectTransform panel, bool inSight)
			{
				int key = LayoutKey(inSight);
				if (_layout == key) return;
				_layout = key;
				panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
				var bar = inSight ? MonitorBar(panel.sizeDelta) : null;
				panel.anchoredPosition = bar?.at ?? (inSight ? SightAt : OutsideAt);
				float panelScale = bar?.scale ?? (inSight ? Mathf.Min(1f, SightMaxHeight / panel.sizeDelta.y) : 1f);
				panel.localScale = new Vector3(panelScale, panelScale, 1f);
			}

			static Sprite _dot;
			internal static Sprite Dot()
			{
				if (_dot != null) return _dot;
				const int size = 32;
				var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
				for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
				{
					float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
					tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size / 2f - distance)));
				}
				tex.Apply();
				_dot = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
				return _dot;
			}

			internal static RectTransform Box(string name, Transform parent, Vector2 pos, Vector2 size)
			{
				var boxObject = new GameObject(name, typeof(RectTransform));
				boxObject.transform.SetParent(parent, false);
				var rect = (RectTransform)boxObject.transform;
				rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
				rect.pivot = new Vector2(0.5f, 0.5f);
				rect.anchoredPosition = pos;
				rect.sizeDelta = size;
				return rect;
			}

			static Text Label(string s, Transform parent, Vector2 pos)
			{
				var text = Box("label " + s, parent, pos, new Vector2(60f, 18f)).gameObject.AddComponent<Text>();
				text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
				text.text = s;
				text.fontSize = 13;
				text.color = new Color(0.1f, 0.1f, 0.1f, 1f);
				text.alignment = TextAnchor.MiddleCenter;
				return text;
			}

			internal static byte[] Resource(string name)
			{
				using (var stream = typeof(ShtoraScreen).Assembly.GetManifestResourceStream("shtora_" + name))
				{
					if (stream == null) return null;
					var buffer = new byte[stream.Length];
					int got = 0;
					while (got < buffer.Length)
					{
						int count = stream.Read(buffer, got, buffer.Length - got);
						if (count <= 0) break;
						got += count;
					}
					return buffer;
				}
			}

			internal static Sprite Picture(string name)
			{
				var bytes = Resource(name);
				if (bytes == null) return null;
				var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "shtora_" + name };
				if (!ImageConversion.LoadImage(tex, bytes))
				{
					UnityEngine.Object.Destroy(tex);
					return null;
				}
				tex.filterMode = FilterMode.Trilinear;
				tex.wrapMode = TextureWrapMode.Clamp;
				tex.anisoLevel = 1;
				return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
			}

			internal static void Free(Sprite s)
			{
				if (s == null) return;
				if (s.texture != null) UnityEngine.Object.Destroy(s.texture);
				UnityEngine.Object.Destroy(s);
			}

			static Dictionary<string, List<float[]>> Layout()
			{
				var bytes = Resource("layout.txt");
				if (bytes == null) return null;
				var table = new Dictionary<string, List<float[]>>();
				try
				{
					foreach (var raw in Encoding.UTF8.GetString(bytes).Split('\n'))
					{
						var line = raw.Trim();
						if (line.Length == 0 || line[0] == '#') continue;
						var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
						if (!table.TryGetValue(tokens[0], out var list)) table[tokens[0]] = list = new List<float[]>();
						list.Add(tokens
							.Skip(1)
							.Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture))
							.ToArray());
					}
				}
				catch (FormatException) { return null; }
				return table;
			}

			internal static AudioClip Wav(string name)
			{
				var bytes = Resource(name);
				if (bytes == null || bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF") return null;
				int channels = 1, rate = 44100, bits = 16, pos = 12;
				while (pos + 8 <= bytes.Length)
				{
					string chunkId = Encoding.ASCII.GetString(bytes, pos, 4);
					int size = BitConverter.ToInt32(bytes, pos + 4);
					if (chunkId == "fmt ")
					{
						channels = BitConverter.ToInt16(bytes, pos + 10);
						rate = BitConverter.ToInt32(bytes, pos + 12);
						bits = BitConverter.ToInt16(bytes, pos + 22);
					}
					else if (chunkId == "data" && bits == 16)
					{
						int sampleCount = Math.Min(size, bytes.Length - pos - 8) / 2;
						var data = new float[sampleCount];
						for (int i = 0; i < sampleCount; i++) data[i] = BitConverter.ToInt16(bytes, pos + 8 + 2 * i) / 32768f;
						var clip = AudioClip.Create("shtora_alarm.wav", sampleCount / channels, channels, rate, false);
						clip.SetData(data, 0);
						return clip;
					}
					pos += 8 + size + (size & 1);
				}
				return null;
			}

			static void Build()
			{
				if (_root != null) return;
				_root = new GameObject("Shtora panel");
				UnityEngine.Object.DontDestroyOnLoad(_root);
				_canvas = _root.AddComponent<Canvas>();
				_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				_canvas.sortingOrder = 0;
				var scaler = _root.AddComponent<CanvasScaler>();
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
				scaler.referenceResolution = new Vector2(1920f, 1080f);
				scaler.matchWidthOrHeight = 1f;
				var panelGo = new GameObject("ПУ ТШУ-1М", typeof(RectTransform));
				_panel = panelGo;
				panelGo.transform.SetParent(_root.transform, false);
				var panel = (RectTransform)panelGo.transform;
				_audio = _root.AddComponent<AudioSource>();
				_audio.spatialBlend = 0f;
				_audio.playOnAwake = false;
				_root.AddComponent<Ticker>();
				panelGo.SetActive(false);
				var art = Picture("panel.png");
				var layout = Layout();
				_lampOn = Picture("lamp_on.png");
				_lampOff = Picture("lamp_off.png");
				if (art != null && layout != null && _lampOn != null && _lampOff != null && layout.TryGetValue(
					"head",
					out var heads) && heads.Count == 4 && heads.All(h => h.Length >= 2)
					&& layout.TryGetValue(
						"scale",
						out var scaleRows) && scaleRows.Count > 0 && scaleRows[0].Length > 0 && scaleRows[0][0] > 0f
					&& layout.TryGetValue("lamp", out var lampRows) && lampRows.Count > 0 && lampRows[0].Length > 0
					&& layout.TryGetValue("spz", out var zoneRows) && zoneRows.Count > 0 && zoneRows[0].Length >= 2)
				{
					float k = 1f / scaleRows[0][0], lamp = lampRows[0][0] * k;
					panel.sizeDelta = new Vector2(art.rect.width * k, art.rect.height * k);
					panelGo.AddComponent<Image>().sprite = art;
					Vector2 At(float[] row) => new Vector2(row[0] * k, -row[1] * k);
					_spz = Box("lamp СПЗ", panel, At(zoneRows[0]), new Vector2(lamp, lamp)).gameObject.AddComponent<Image>();
					_spz.sprite = _lampOn;
					_lamps = new Image[4];
					for (int i = 0; i < 4; i++)
					{
						_lamps[i] = Box("lamp " + HeadNames[i], panel, At(heads[i]), new Vector2(lamp, lamp)).gameObject
							.AddComponent<Image>();
						_lamps[i].sprite = _lampOff;
					}
					_audio.clip = Wav("alarm.wav") ?? Tone();
					_audio.volume = 0.6f;
					_art = $"port art {art.rect.width}x{art.rect.height}, tone {_audio.clip.name} {_audio.clip.length:0.00} s";
					Fit(panel);
					return;
				}
				Free(art);
				Free(_lampOn);
				Free(_lampOff);
				_lampOn = _lampOff = null;
				panel.sizeDelta = new Vector2(170f, 96f);
				var background = panelGo.AddComponent<Image>();
				background.color = new Color(0.86f, 0.86f, 0.83f, 0.95f);
				var edge = panelGo.AddComponent<Outline>();
				edge.effectColor = new Color(0.25f, 0.25f, 0.25f, 1f);
				edge.effectDistance = new Vector2(2f, -2f);
				Label("СПЗ", panel, new Vector2(40f, -62f));
				_spz = Box("lamp СПЗ", panel, new Vector2(40f, -44f), new Vector2(15f, 15f)).gameObject.AddComponent<Image>();
				_spz.sprite = Dot();
				_spz.color = LampOn;
				Label("ПУСК", panel, new Vector2(118f, -14f));
				var centerX = 118f;
				var centerY = -52f;
				var horizontalBar = Box("cross h", panel, new Vector2(centerX, centerY), new Vector2(54f, 2f)).gameObject
					.AddComponent<Image>();
				horizontalBar.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				var verticalBar = Box("cross v", panel, new Vector2(centerX, centerY), new Vector2(2f, 46f)).gameObject
					.AddComponent<Image>();
				verticalBar.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				_lamps = new Image[4];
				var positions = new[]
				{
					new Vector2(centerX - 14f, centerY + 12f),
					new Vector2(centerX + 14f, centerY + 12f),
					new Vector2(centerX - 14f, centerY - 12f),
					new Vector2(centerX + 14f, centerY - 12f)
				};
				for (int i = 0; i < 4; i++)
				{
					_lamps[i] = Box("lamp " + HeadNames[i], panel, positions[i], new Vector2(15f, 15f)).gameObject.AddComponent<Image>();
					_lamps[i].sprite = Dot();
					_lamps[i].color = LampOff;
				}
				_audio.volume = 0.35f;
				_audio.clip = Tone();
				Fit(panel);
			}

			static AudioClip Tone()
			{
				int rate = 44100, sampleCount = (int)(rate * 0.9f);
				var data = new float[sampleCount];
				for (int i = 0; i < sampleCount; i++)
				{
					float seconds = i / (float)rate;
					bool audible = (seconds % 0.3f) < 0.18f;
					data[i] = audible ? 0.5f * Mathf.Sin(2f * Mathf.PI * 1500f * seconds) : 0f;
				}
				var clip = AudioClip.Create("shtora_alarm", sampleCount, 1, rate, false);
				clip.SetData(data, 0);
				return clip;
			}

			sealed class Ticker : MonoBehaviour
			{
				void Update()
				{
					bool show = Wanted();
					if (_panel.activeSelf != show) _panel.SetActive(show);
					if (show) Place((RectTransform)_panel.transform, InSight());
					if (_lamps == null) return;
					float left = _until - Time.unscaledTime;
					bool blinkOn = left > 1.5f && (Time.unscaledTime * 2.5f) % 1f < 0.6f;
					for (int i = 0; i < 4; i++)
					{
						bool lit = i == _lit && blinkOn;
						if (_lampOn != null) _lamps[i].sprite = lit ? _lampOn : _lampOff;
						else _lamps[i].color = lit ? LampOn : LampOff;
					}
					if (left <= 0f) _lit = -1;
				}
			}
		}
	}
}
