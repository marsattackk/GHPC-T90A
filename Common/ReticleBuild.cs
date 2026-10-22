using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Weaponry;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace T90A.Common
{
	internal static class ReticleBuild
	{
		static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static ReticleSO Build(ReticleSpec spec, ReticleSO host, ReticleTree.Text font, Func<string, AmmoCodexScriptable> ammo, List<string> problems)
		{
			var so = ScriptableObject.CreateInstance<ReticleSO>();
			so.name = spec.Name;
			problems.AddRange(spec.Problems);
			if (spec.LightsFromHost && host != null)
				so.lights = host.lights.Select(l => new ReticleTree.Light { type = l.type, color = Set(spec, l.type) ?? l.color }).ToList();
			foreach (var kv in spec.Lights)
			{
				if (!Enum.TryParse(kv.Key, out ReticleTree.Light.Type t)) { problems.Add($"lights: unknown light '{kv.Key}'"); continue; }
				if (!so.lights.Any(l => l.type == t)) so.lights.Add(new ReticleTree.Light { type = t, color = Rgb(kv.Value) });
			}
			var plane = new ReticleTree.FocalPlane(spec.Plane == "second" ? ReticleTree.FocalPlane.Type.Second : ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			so.planes.Add(plane);
			var ctx = new Ctx { Font = font, Ammo = ammo };
			foreach (var n in spec.Top)
			{
				var e = Make(n, null, false, ctx, problems);
				if (e != null) plane.elements.Add(e);
			}
			foreach (var light in AllOf<ReticleTree.VisualElement>(so).Select(v => v.illumination).Distinct())
				if (light != ReticleTree.Light.Type.None && !so.lights.Any(l => l.type == light))
					problems.Add($"elements lit by '{light}', which the reticle has no colour for");
			foreach (var u in Unaligned(so)) problems.Add("still without an alignment: " + u);
			return so;
		}

		static RGB? Set(ReticleSpec spec, ReticleTree.Light.Type t)
		{
			foreach (var kv in spec.Lights) if (kv.Key == t.ToString()) return Rgb(kv.Value);
			return null;
		}

		static RGB Rgb(float[] c) => new RGB(c[0], c[1], c[2]);

		sealed class Ctx
		{
			internal ReticleTree.Text Font;
			internal Func<string, AmmoCodexScriptable> Ammo;
			internal bool FontMissingReported;
		}

		static ReticleTree.TransformElement Make(ReticleSpec.Node n, ReticleTree.GroupBase parent, bool aligned, Ctx ctx, List<string> problems)
		{
			var pos = new Vector2();
			if (n is ReticleSpec.Group g)
			{
				var align = ReticleTree.GroupBase.Alignment.None;
				if (g.Align != null && !Enum.TryParse(g.Align, out align)) { problems.Add($"{g.Path}: unknown alignment '{g.Align}' (group left out)"); return null; }
				pos = new Vector2((float)g.X, (float)g.Y);
				ReticleTree.GroupBase grp;
				if (g.Ammo != null)
				{
					var round = ctx.Ammo?.Invoke(g.Ammo);
					if (round == null) { problems.Add($"{g.Path}: no round for '{g.Ammo}' (ballistic group left out)"); return null; }
					var vb = new ReticleTree.VerticalBallistic(pos, parent, align) { projectile = round };
					vb.UpdateBC();
					grp = vb;
				}
				else if (g.Stadia > 0) grp = new ReticleTree.Stadia(pos, parent, align) { targetSize = new LinearLength((float)g.Stadia) };
				else grp = new ReticleTree.Angular(pos, parent, align) { name = g.Name };
				bool under = aligned || align != ReticleTree.GroupBase.Alignment.None;
				foreach (var c in g.Children)
				{
					var e = Make(c, grp, under, ctx, problems);
					if (e != null) grp.elements.Add(e);
				}
				return grp;
			}
			if (!aligned)
			{
				problems.Add($"{n.Path}: not under a group with an alignment (it would hide the whole reticle; left out)");
				return null;
			}
			switch (n)
			{
				case ReticleSpec.Line l:
				{
					var line = new ReticleTree.Line(new Vector2((float)l.X, (float)l.Y), (float)l.Deg, (float)l.Len, (float)l.W, 1f, parent);
					line.length.unit = AngularLength.AngularUnit.MIL_USSR;
					line.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
					return Look(line, l.Visual, l.Light, l.Path, problems);
				}
				case ReticleSpec.Circle c:
				{
					var circle = new ReticleTree.Circle(new Vector2((float)c.X, (float)c.Y), 0f, (float)c.R, (float)c.W, c.Segments, parent);
					return Look(circle, c.Visual, c.Light, c.Path, problems);
				}
				case ReticleSpec.Text t:
				{
					if (ctx.Font == null)
					{
						if (!ctx.FontMissingReported) problems.Add($"{t.Path}: no reticle font to clone; labels left out");
						ctx.FontMissingReported = true;
						return null;
					}
					var label = (ReticleTree.Text)Clone.Invoke(ctx.Font, null);
					label.position = new ReticleTree.Position((float)t.X, (float)t.Y, AngularLength.AngularUnit.MIL_USSR);
					label.rotation = new AngularLength(0f, AngularLength.AngularUnit.DEG);
					label.parent = parent;
					label.text = t.Value;
					label.fontSize = (float)t.Size;
					return Look(label, t.Visual, t.Light, t.Path, problems);
				}
			}
			return null;
		}

		static ReticleTree.VisualElement Look(ReticleTree.VisualElement v, string visual, string light, string path, List<string> problems)
		{
			if (visual == "painted") v.visualType = ReticleTree.VisualElement.Type.Painted;
			else if (visual == "additive") v.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
			else problems.Add($"{path}: unknown visual '{visual}' (painted, additive)");
			if (Enum.TryParse(light, out ReticleTree.Light.Type t)) v.illumination = t;
			else problems.Add($"{path}: unknown light '{light}'");
			return v;
		}

		internal static List<string> Unaligned(ReticleSO so)
		{
			var bad = new List<string>();
			void Walk(ReticleTree.TransformElement e, bool aligned, string path)
			{
				if (e is ReticleTree.GroupBase g)
				{
					bool under = aligned || g.align != ReticleTree.GroupBase.Alignment.None;
					foreach (var c in g.elements) Walk(c, under, path + "/" + g);
				}
				else if (e is ReticleTree.VisualElement && !aligned) bad.Add(path + "/" + e);
			}
			foreach (var p in so.planes) foreach (var e in p.elements) Walk(e, false, so.name);
			return bad;
		}

		internal static ReticleTree.Text FontTemplate(ReticleSO host)
		{
			if (host == null) return null;
			var labels = AllOf<ReticleTree.Text>(host).ToList();
			return labels.FirstOrDefault(t => t.font != null && t.font.name.StartsWith("TPD_Etch")) ?? labels.FirstOrDefault();
		}

		internal static IEnumerable<T> AllOf<T>(ReticleSO so) where T : ReticleTree.TransformElement
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e is T match) yield return match;
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
		}

		internal sealed class Overlay
		{
			internal GameObject Root;
			internal readonly Dictionary<string, TextMeshProUGUI> Readouts = new Dictionary<string, TextMeshProUGUI>();
			internal readonly Dictionary<string, GameObject> Lamps = new Dictionary<string, GameObject>();
		}

		internal static Overlay BuildOverlay(ReticleSpec spec, Transform parent, float fovDeg, List<string> problems)
		{
			var o = new Overlay { Root = new GameObject(spec.ScreenName, typeof(RectTransform)) };
			o.Root.transform.SetParent(parent, false);
			var canvas = o.Root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = -1;
			var scaler = o.Root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(800f, 600f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 1f;
			float u = 600f / (fovDeg * Mathf.Deg2Rad * 1000f);
			foreach (var it in spec.Screen)
			{
				float ay = it.Anchor == "bottom" ? 0f : it.Anchor == "top" ? 1f : 0.5f;
				var pos = new Vector2((float)it.X * u, (float)it.Y * u);
				var col = new Color(it.Color[0], it.Color[1], it.Color[2], it.Color[3]);
				switch (it.Kind)
				{
					case "rect":
					case "seg":
					{
						var rt = it.FullWidth ? Rect(it.Name, o.Root.transform, new Vector2(0f, ay), new Vector2(1f, ay), pos, new Vector2(0f, (float)it.H * u))
							: Rect(it.Name, o.Root.transform, new Vector2(0.5f, ay), new Vector2(0.5f, ay), pos, new Vector2((float)it.W * u, (float)it.H * u));
						if (it.Kind == "seg") rt.localRotation = Quaternion.Euler(0f, 0f, (float)it.Deg);
						rt.gameObject.AddComponent<Image>().color = col;
						break;
					}
					case "text":
					case "readout":
					{
						var rt = Rect(it.Kind == "readout" ? it.Name + " text" : "text", o.Root.transform, new Vector2(0.5f, ay), new Vector2(0.5f, ay), pos, new Vector2((float)it.W * u, (float)it.H * u));
						var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
						var f = it.Font != null ? Font(it.Font) : null;
						if (f != null) tmp.font = f;
						else if (it.Font != null) problems.Add($"{it.Path}: no TMP font '{it.Font}' (default font used)");
						tmp.fontSize = (float)it.Size * u;
						tmp.color = col;
						tmp.alignment = it.Align == "left" ? TextAlignmentOptions.Left : it.Align == "right" ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
						tmp.enableWordWrapping = false;
						tmp.richText = true;
						tmp.text = it.Value ?? "";
						if (it.Kind == "readout") o.Readouts[it.Name] = tmp;
						break;
					}
					case "lamp":
					{
						var size = new Vector2((float)it.Size * u, (float)it.Size * u);
						var off = Rect(it.Name + " (unlit)", o.Root.transform, new Vector2(0.5f, ay), new Vector2(0.5f, ay), pos, size);
						var offImg = off.gameObject.AddComponent<Image>();
						offImg.sprite = LampSprite();
						offImg.color = new Color(col.r * 0.22f, col.g * 0.22f, col.b * 0.22f, 1f);
						var on = Rect(it.Name, o.Root.transform, new Vector2(0.5f, ay), new Vector2(0.5f, ay), pos, size);
						var img = on.gameObject.AddComponent<Image>();
						img.sprite = LampSprite();
						img.color = col;
						on.gameObject.SetActive(false);
						o.Lamps[it.Name] = on.gameObject;
						break;
					}
				}
			}
			return o;
		}

		static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
		{
			var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
			rt.SetParent(parent, false);
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = pos;
			rt.sizeDelta = size;
			return rt;
		}

		static readonly Dictionary<string, TMP_FontAsset> Fonts = new Dictionary<string, TMP_FontAsset>();

		internal static TMP_FontAsset Font(string name)
		{
			if (Fonts.TryGetValue(name, out var f) && f != null) return f;
			f = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(x => x.name == name) ?? Resources.LoadAll<TMP_FontAsset>("").FirstOrDefault(x => x.name == name);
			if (f != null) Fonts[name] = f;
			return f;
		}

		static Sprite _lamp;

		static Sprite LampSprite()
		{
			if (_lamp != null) return _lamp;
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
			_lamp = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f));
			return _lamp;
		}
	}
}
