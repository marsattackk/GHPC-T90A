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
	// ReticleSpec to ReticleSO, or to an overlay for the thermal view.
	internal static class ReticleBuild
	{
		static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static ReticleSO Build(
			ReticleSpec spec,
			ReticleSO host,
			ReticleTree.Text font,
			Func<string, AmmoCodexScriptable> ammo,
			List<string> problems)
		{
			var reticle = ScriptableObject.CreateInstance<ReticleSO>();
			reticle.name = spec.Name;
			problems.AddRange(spec.Problems);
			if (spec.LightsFromHost && host != null)
				reticle.lights = host.lights
					.Select(l => new ReticleTree.Light { type = l.type, color = Set(spec, l.type) ?? l.color })
					.ToList();
			foreach (var entry in spec.Lights)
			{
				if (!Enum.TryParse(entry.Key, out ReticleTree.Light.Type lightType))
				{
					problems.Add($"lights: unknown light '{entry.Key}'");
					continue;
				}
				if (!reticle.lights.Any(l => l.type == lightType))
					reticle.lights.Add(new ReticleTree.Light { type = lightType, color = Rgb(entry.Value) });
			}
			// GHPC ignores Second. Every reticle draws in the first focal plane.
			var plane = new ReticleTree.FocalPlane(spec.Plane == "second"
				? ReticleTree.FocalPlane.Type.Second
				: ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			reticle.planes.Add(plane);
			var ctx = new Ctx { Font = font, Ammo = ammo };
			foreach (var node in spec.Top)
			{
				var element = Make(node, null, false, ctx, problems);
				if (element != null) plane.elements.Add(element);
			}
			foreach (var light in AllOf<ReticleTree.VisualElement>(reticle).Select(v => v.illumination).Distinct())
				if (light != ReticleTree.Light.Type.None && !reticle.lights.Any(l => l.type == light))
					problems.Add($"elements lit by '{light}', which the reticle has no colour for");
			foreach (var unaligned in Unaligned(reticle)) problems.Add("still without an alignment: " + unaligned);
			return reticle;
		}

		// A copied reticle mesh keeps the old built mesh (Load then builds nothing) and the old light states by position.
		internal static void PrepareCopy(ReticleMesh rm)
		{
			if (rm == null || rm.reticleSO == null) return;
			var smr = rm.GetComponent<SkinnedMeshRenderer>();
			if (smr != null) smr.sharedMesh = null;
			rm.lights = rm.reticleSO.lights
				.Select(l => new ReticleEditor.LightState(l, l.type == ReticleTree.Light.Type.NightIllumination ? 0f : 1f))
				.ToList();
		}

		static RGB? Set(ReticleSpec spec, ReticleTree.Light.Type t)
		{
			foreach (var entry in spec.Lights) if (entry.Key == t.ToString()) return Rgb(entry.Value);
			return null;
		}

		static RGB Rgb(float[] c) => new RGB(c[0], c[1], c[2]);

		sealed class Ctx
		{
			internal ReticleTree.Text Font;
			internal Func<string, AmmoCodexScriptable> Ammo;
			internal bool FontMissingReported;
		}

		static ReticleTree.TransformElement Make(
			ReticleSpec.Node n,
			ReticleTree.GroupBase parent,
			bool aligned,
			Ctx ctx,
			List<string> problems)
		{
			var pos = new Vector2();
			if (n is ReticleSpec.Group specGroup)
			{
				var align = ReticleTree.GroupBase.Alignment.None;
				if (specGroup.Align != null && !Enum.TryParse(specGroup.Align, out align))
				{
					problems.Add($"{specGroup.Path}: unknown alignment '{specGroup.Align}' (group left out)");
					return null;
				}
				pos = new Vector2((float)specGroup.X, (float)specGroup.Y);
				ReticleTree.GroupBase grp;
				if (specGroup.Ammo != null)
				{
					var round = ctx.Ammo?.Invoke(specGroup.Ammo);
					if (round == null)
					{
						problems.Add($"{specGroup.Path}: no round for '{specGroup.Ammo}' (ballistic group left out)");
						return null;
					}
					var ballistic = new ReticleTree.VerticalBallistic(pos, parent, align) { projectile = round };
					ballistic.UpdateBC();
					grp = ballistic;
				}
				else if (specGroup.Stadia > 0)
					grp = new ReticleTree.Stadia(pos, parent, align) { targetSize = new LinearLength((float)specGroup.Stadia) };
				else grp = new ReticleTree.Angular(pos, parent, align) { name = specGroup.Name };
				bool under = aligned || align != ReticleTree.GroupBase.Alignment.None;
				foreach (var child in specGroup.Children)
				{
					var element = Make(child, grp, under, ctx, problems);
					if (element != null) grp.elements.Add(element);
				}
				return grp;
			}
			// Outside an aligned group ReticleMesh gives it bone -1, which hides the whole reticle without a word.
			if (!aligned)
			{
				problems.Add($"{n.Path}: not under a group with an alignment (it would hide the whole reticle; left out)");
				return null;
			}
			switch (n)
			{
				case ReticleSpec.Line lineSpec:
				{
					var line = new ReticleTree.Line(
						new Vector2((float)lineSpec.X, (float)lineSpec.Y),
						(float)lineSpec.Deg,
						(float)lineSpec.Len,
						(float)lineSpec.W,
						1f,
						parent);
					line.length.unit = AngularLength.AngularUnit.MIL_USSR;
					line.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
					return Look(line, lineSpec.Visual, lineSpec.Light, lineSpec.Path, problems);
				}
				case ReticleSpec.Circle circleSpec:
				{
					var circle = new ReticleTree.Circle(
						new Vector2((float)circleSpec.X, (float)circleSpec.Y),
						0f,
						(float)circleSpec.R,
						(float)circleSpec.W,
						circleSpec.Segments,
						parent);
					return Look(circle, circleSpec.Visual, circleSpec.Light, circleSpec.Path, problems);
				}
				case ReticleSpec.Text textSpec:
				{
					if (ctx.Font == null)
					{
						if (!ctx.FontMissingReported) problems.Add($"{textSpec.Path}: no reticle font to clone; labels left out");
						ctx.FontMissingReported = true;
						return null;
					}
					var label = (ReticleTree.Text)Clone.Invoke(ctx.Font, null);
					label.position = new ReticleTree.Position((float)textSpec.X, (float)textSpec.Y, AngularLength.AngularUnit.MIL_USSR);
					label.rotation = new AngularLength(0f, AngularLength.AngularUnit.DEG);
					label.parent = parent;
					label.text = textSpec.Value;
					label.fontSize = (float)textSpec.Size;
					return Look(label, textSpec.Visual, textSpec.Light, textSpec.Path, problems);
				}
			}
			return null;
		}

		static ReticleTree.VisualElement Look(ReticleTree.VisualElement v, string visual, string light, string path, List<string> problems)
		{
			if (visual == "painted") v.visualType = ReticleTree.VisualElement.Type.Painted;
			else if (visual == "additive") v.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
			else problems.Add($"{path}: unknown visual '{visual}' (painted, additive)");
			if (Enum.TryParse(light, out ReticleTree.Light.Type lightType)) v.illumination = lightType;
			else problems.Add($"{path}: unknown light '{light}'");
			return v;
		}

		internal static List<string> Unaligned(ReticleSO so)
		{
			var bad = new List<string>();
			void Walk(ReticleTree.TransformElement element, bool aligned, string path)
			{
				if (element is ReticleTree.GroupBase container)
				{
					bool under = aligned || container.align != ReticleTree.GroupBase.Alignment.None;
					foreach (var child in container.elements) Walk(child, under, path + "/" + container);
				}
				else if (element is ReticleTree.VisualElement && !aligned) bad.Add(path + "/" + element);
			}
			foreach (var plane in so.planes) foreach (var element in plane.elements) Walk(element, false, so.name);
			return bad;
		}

		internal static ReticleTree.Text FontTemplate(ReticleSO host)
		{
			if (host == null) return null;
			var labels = AllOf<ReticleTree.Text>(host).ToList();
			return labels.FirstOrDefault(l => l.font != null && l.font.name.StartsWith("TPD_Etch")) ?? labels.FirstOrDefault();
		}

		internal static ReticleSO FontHost(ReticleSO own, Transform unit)
		{
			bool Labelled(ReticleSO reticle)
				=> reticle != null && reticle.planes != null && AllOf<ReticleTree.Text>(reticle).Any(l => l.font != null);
			if (Labelled(own)) return own;
			var onUnit = unit == null
				? null
				: unit
					.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true)
					.Select(o => o.reticleMesh != null ? o.reticleMesh.reticleSO : null)
					.FirstOrDefault(Labelled);
			if (onUnit != null) return onUnit;
			var loaded = Resources.FindObjectsOfTypeAll<ReticleSO>()
				.Where(Labelled)
				.OrderBy(r => r.name, StringComparer.Ordinal)
				.ToList();
			return loaded.FirstOrDefault(r => FontTemplate(r)?.font?.name.StartsWith("TPD_Etch") == true)
				?? loaded.FirstOrDefault();
		}

		internal static IEnumerable<T> AllOf<T>(ReticleSO so) where T : ReticleTree.TransformElement
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var element = stack.Pop();
				if (element is T match) yield return match;
				if (element is ReticleTree.GroupBase container) foreach (var child in container.elements) stack.Push(child);
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
			var overlay = new Overlay { Root = new GameObject(spec.ScreenName, typeof(RectTransform)) };
			overlay.Root.transform.SetParent(parent, false);
			var canvas = overlay.Root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = -1;
			var scaler = overlay.Root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(800f, 600f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 1f;
			float scale = 600f / (fovDeg * Mathf.Deg2Rad * 1000f);
			foreach (var item in spec.Screen)
			{
				float anchorY = item.Anchor == "bottom" ? 0f : item.Anchor == "top" ? 1f : 0.5f;
				var pos = new Vector2((float)item.X * scale, (float)item.Y * scale);
				var col = new Color(item.Color[0], item.Color[1], item.Color[2], item.Color[3]);
				switch (item.Kind)
				{
					case "rect":
					case "seg":
					{
						var rectTransform = item.FullWidth ? Rect(
							item.Name,
							overlay.Root.transform,
							new Vector2(0f, anchorY),
							new Vector2(1f, anchorY),
							pos,
							new Vector2(0f, (float)item.H * scale))
							: Rect(
								item.Name,
								overlay.Root.transform,
								new Vector2(0.5f, anchorY),
								new Vector2(0.5f, anchorY),
								pos,
								new Vector2((float)item.W * scale, (float)item.H * scale));
						if (item.Kind == "seg") rectTransform.localRotation = Quaternion.Euler(0f, 0f, (float)item.Deg);
						rectTransform.gameObject.AddComponent<Image>().color = col;
						break;
					}
					case "text":
					case "readout":
					{
						var rectTransform = Rect(
							item.Kind == "readout" ? item.Name + " text" : "text",
							overlay.Root.transform,
							new Vector2(0.5f, anchorY),
							new Vector2(0.5f, anchorY),
							pos,
							new Vector2((float)item.W * scale, (float)item.H * scale));
						var label = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
						var font = item.Font != null ? Font(item.Font) : null;
						if (font != null) label.font = font;
						else if (item.Font != null) problems.Add($"{item.Path}: no TMP font '{item.Font}' (default font used)");
						var lacks = Missing(font ?? label.font, item.Value);
						if (lacks.Length > 0) problems.Add($"{item.Path}: font '{(font ?? label.font)?.name}' lacks '{lacks}'");
						label.fontSize = (float)item.Size * scale;
						label.color = col;
						label.alignment = item.Align == "left"
							? TextAlignmentOptions.Left
							: item.Align == "right" ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
						label.enableWordWrapping = false;
						label.richText = true;
						label.text = item.Value ?? "";
						if (item.Kind == "readout") overlay.Readouts[item.Name] = label;
						break;
					}
					case "lamp":
					{
						var size = new Vector2((float)item.Size * scale, (float)item.Size * scale);
						var off = Rect(
							item.Name + " (unlit)",
							overlay.Root.transform,
							new Vector2(0.5f, anchorY),
							new Vector2(0.5f, anchorY),
							pos,
							size);
						var offImg = off.gameObject.AddComponent<Image>();
						offImg.sprite = LampSprite();
						offImg.color = new Color(col.r * 0.22f, col.g * 0.22f, col.b * 0.22f, 1f);
						var litRect = Rect(
							item.Name,
							overlay.Root.transform,
							new Vector2(0.5f, anchorY),
							new Vector2(0.5f, anchorY),
							pos,
							size);
						var img = litRect.gameObject.AddComponent<Image>();
						img.sprite = LampSprite();
						img.color = col;
						litRect.gameObject.SetActive(false);
						overlay.Lamps[item.Name] = litRect.gameObject;
						break;
					}
				}
			}
			return overlay;
		}

		static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
		{
			var rectTransform = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
			rectTransform.SetParent(parent, false);
			rectTransform.anchorMin = anchorMin;
			rectTransform.anchorMax = anchorMax;
			rectTransform.pivot = new Vector2(0.5f, 0.5f);
			rectTransform.anchoredPosition = pos;
			rectTransform.sizeDelta = size;
			return rectTransform;
		}

		internal static string Missing(TMP_FontAsset font, string text)
		{
			if (font == null || string.IsNullOrEmpty(text)) return "";
			string plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "").Replace(" ", "");
			if (plain.Length == 0 || font.HasCharacters(plain, out uint[] missing, true, true)) return "";
			return new string((missing ?? new uint[0]).Distinct().Select(c => (char)c).ToArray());
		}

		static readonly Dictionary<string, TMP_FontAsset> Fonts = new Dictionary<string, TMP_FontAsset>();

		internal static TMP_FontAsset Font(string name)
		{
			if (Fonts.TryGetValue(name, out var font) && font != null) return font;
			// Loaded fonts only (one that lives in a vehicle bundle turns up with its vehicle).
			font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(a => a.name == name)
				?? Resources.LoadAll<TMP_FontAsset>("").FirstOrDefault(a => a.name == name);
			if (font != null) Fonts[name] = font;
			return font;
		}

		static Sprite _lamp;

		static Sprite LampSprite()
		{
			if (_lamp != null) return _lamp;
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
			_lamp = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
			return _lamp;
		}
	}
}
