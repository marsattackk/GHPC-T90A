using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Equipment.Optics;
using GHPC.Weaponry;
using MelonLoader;
using Reticle;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// 1G46 reticle and zoom steps. Positions in mrad off figure 9, «Вид поля зрения прицела», of «Комплекс управления
	// огнем 1А45» (odetievbrony.ru) at 12x, the 0-08 chevron (8.378 mrad) as the scale, origin at the chevron tip. The
	// strokes' shapes are the game's own 1G42's.
	internal static partial class Sight1G46
	{
		// 20 to 4.5 deg, the 1G46's 2.7-12x (Suvorov, «Т-90»). The real zoom is smooth, so the seven stops between are mine.
		internal static readonly float[] ZoomFovs = { 20f, 16.6f, 13.8f, 11.4f, 9.5f, 7.9f, 6.5f, 5.4f, 4.5f };

		// Only for when the loaded ammo list can't be read: the T-80B rounds the T-90A's are copied from, else one of that
		// kind off the host reticle. The list goes APFSDS, HEAT, then the T-72M1's HE (PortAmmo.PortList), which is why
		// Sights.Apply and the TKN-4S both read slot 2 as HE.
		internal static readonly string ApfsdsAmmo = "ammo_3BM32";
		internal static readonly string HeAmmo = "ammo_3OF26";
		internal static readonly string HeatAmmo = "ammo_3BK14M";
		internal static readonly string CoaxAmmo = "ammo_762x54R";

		// Off the figure: the range-input bar's height, the vertical stroke's end and where each scale stands. Its legend
		// gives the lead step (0-02), the Б scale's 200 m divisions and the stadia's 2.5 m target. Its П scale ends at 14.
		const float Thick = 0.1571f;
		const float TopThick = 0.2094f;
		const float Lead = 2.094f;
		const float TopY = 14.8f;
		const float VerticalBottom = -30.8f;
		const int MgMaxRange = 1400;

		const float BScaleX = -27.2f;
		const float BScaleSlope = 0.00747f;
		const float OScaleX = 8.0f;
		const float HScaleX = 16.1f;
		const float VScaleLabelX = 2.5f;

		const float StadiaTarget = 2.5f;
		const float StadiaBaseY = -14.4f;
		const float StadiaX800 = -28.2f;
		const float StadiaXPer400 = 3.39f;

		// One per set of rounds. A shared one drew the first tank's Б scale in every T-90A after it.
		static readonly Dictionary<string, ReticleSO> built = new Dictionary<string, ReticleSO>();

		internal static string RoundsKey(params AmmoCodexScriptable[] rounds)
			=> string.Join("|", rounds.Select(r => r != null ? r.name : "-"));

		internal static void Apply(UsableOptic dayOptic, AmmoCodexScriptable apfsds, AmmoCodexScriptable he,
			AmmoCodexScriptable heat, AmmoCodexScriptable coax)
		{
			ReticleMesh reticleMesh = dayOptic.reticleMesh;
			string key = RoundsKey(apfsds, he, heat, coax);
			if (!built.TryGetValue(key, out var reticle) || reticle == null)
				built[key] = reticle = Build(reticleMesh.reticleSO, apfsds, he, heat, coax);

			reticleMesh.reticleSO = reticle;
			ReticleMeshes.ClearSmr(reticleMesh);
			reticleMesh.Load();

			dayOptic.slot.AllowFreeZoom = true;
			dayOptic.slot.DefaultFov = ZoomFovs[0];
			dayOptic.slot.OtherFovs = ZoomFovs.Skip(1).ToArray();

			SightMotion.Stabilised(dayOptic.slot);
		}

		internal static ReticleSO Build(ReticleSO host, AmmoCodexScriptable apfsds, AmmoCodexScriptable he,
			AmmoCodexScriptable heat, AmmoCodexScriptable coax)
		{
			ReticleSO reticle = ScriptableObject.CreateInstance<ReticleSO>();
			reticle.name = "1G46";
			reticle.lights = new List<ReticleTree.Light>(host.lights);
			ReticleTree.Text font = FontTemplate(host);

			var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			reticle.planes.Add(plane);

			var bore = Group(null, "Boresight", 0f, 0f, ReticleTree.GroupBase.Alignment.Boresight);
			var moving = Group(null, "VerticalRange", 0f, 0f, ReticleTree.GroupBase.Alignment.VerticalRange);
			plane.elements.Add(bore);
			plane.elements.Add(moving);

			// The chevron and the range-input bar across the whole 4.5 deg field at 12x. The vertical stroke runs from under the
			// tip down to the strip, or 1 mrad past the 1400 m coax mark if that's lower (-30.1 if the ballistics can't be read).
			var chevron = Group(bore, "Chevron", 0f, 0f);
			chevron.elements.Add(Line(chevron, 0.75f, -1.55f, 294.97f, 3.6652f));
			chevron.elements.Add(Line(chevron, -0.7461f, -1.5504f, 65.03f, 3.6652f));
			bore.elements.Add(chevron);
			bore.elements.Add(Line(bore, 0f, TopY, 0f, 78.6f, TopThick));
			var coaxGroup = CoaxScale(bore, coax, font);
			float bottom = Mathf.Min(VerticalBottom, Superelevation(coaxGroup, MgMaxRange, -30.1f) - 1.0f);
			bore.elements.Add(Line(bore, 0f, (-0.9f + bottom) / 2f, 90f, -0.9f - bottom));
			bore.elements.Add(coaxGroup);
			bore.elements.Add(StadiaScale(bore, font));
			bore.elements.Add(CoaxParallaxCurve(bore, font));
			bore.elements.Add(InfoBlockMarks(bore));

			moving.elements.Add(LeadMarks(moving));
			moving.elements.Add(BScale(moving, apfsds, font));
			moving.elements.Add(VerticalScale(moving, "range-O", OScaleX, "O", he, font));
			moving.elements.Add(VerticalScale(moving, "range-H", HScaleX, "H", heat, font));

			return reticle;
		}

		internal static AmmoCodexScriptable FindAmmo(string name, ReticleSO host, string fallbackToken)
		{
			AmmoCodexScriptable found = Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>().FirstOrDefault(a => a.name == name);
			if (found != null) return found;
			return AllOf<ReticleTree.VerticalBallistic>(host)
				.Select(b => b.projectile)
				.FirstOrDefault(c => c != null && c.name.Contains(fallbackToken));
		}

		static ReticleTree.Angular CoaxScale(ReticleTree.Angular parent, AmmoCodexScriptable coax, ReticleTree.Text font)
		{
			var group = Group(parent, "range-MG", 0f, 0f);
			var ballistic = Ballistic(group, coax, 0f, 0f);
			// The figure's coax scale: 100 m divisions to 1400 m, numbered in hundreds every 200, П by the 800, with the marks'
			// lengths and places measured off it.
			int[] ranges = { 200, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400 };
			foreach (int range in ranges)
			{
				bool major = range % 200 == 0 && range >= 400;
				var mark = Group(ballistic, range + "M", 0f, range);
				float len = range == 200 ? 0.5236f : (major ? 2.0944f : 1.5708f);
				float x = range == 200 ? -0.2454f : (major ? -0.9817f : -0.7363f);
				mark.elements.Add(Line(mark, x, 0f, 0f, len));
				if (major) mark.elements.Add(Label(mark, font, (range / 100).ToString(), -3.55f, 0.26f, 12f));
				if (range == 800) Glyph(mark, 'П', 2.35f, 0f, 1.4f);
				ballistic.elements.Add(mark);
			}
			group.elements.Add(ballistic);
			return group;
		}

		static ReticleTree.Angular StadiaScale(ReticleTree.Angular parent, ReticleTree.Text font)
		{
			var group = Group(parent, "Stadia", 0f, 0f);
			// The figure's stadia for a 2.5 m target, 800 to 2800 m in 200 m divisions, its bar and marks measured off it.
			float left = StadiaX800 - 0.3f;
			float right = StadiaX800 + 5 * StadiaXPer400 + 0.5f;
			group.elements.Add(Line(group, (left + right) / 2f, StadiaBaseY, 0f, right - left, 0.1309f));
			group.elements.Add(Label(group, font, "2,5", left + 1.5f, StadiaBaseY - 1.2f, 10f));

			var stadia = new ReticleTree.Stadia(new Vector2(0f, StadiaBaseY), group);
			stadia.targetSize = new LinearLength(StadiaTarget);
			for (int range = 800; range <= 2800; range += 200)
			{
				bool major = range % 400 == 0;
				var mark = Group(stadia, range + "M", StadiaX800 + (range - 800) / 400f * StadiaXPer400, range);
				mark.elements.Add(Line(mark, 0f, 0f, 0f, 0.6f, 0.1309f));
				float stem = major ? 1.0f : 0.5f;
				mark.elements.Add(Line(mark, 0f, stem / 2f, 90f, stem, 0.1309f));
				if (major) mark.elements.Add(Label(mark, font, (range / 100).ToString(), 0f, 2.2f, 10f));
				stadia.elements.Add(mark);
			}
			group.elements.Add(stadia);
			return group;
		}

		static ReticleTree.Angular CoaxParallaxCurve(ReticleTree.Angular parent, ReticleTree.Text font)
		{
			var angularGroup = Group(parent, "Coax parallax", 0f, 0f);
			// The figure's dashed coax line for short range with its 2, 1 and 0,5 marks, slope and places measured off it.
			Func<float, float> lineY = x => -2.84f - 0.38f * (x - 0.69f);
			float angle = Mathf.Atan(-0.38f) * Mathf.Rad2Deg;

			angularGroup.elements.Add(Line(angularGroup, 0.95f, -2.55f, 135f, 0.75f));
			angularGroup.elements.Add(Line(angularGroup, 0.95f, -3.13f, 45f, 0.75f));
			for (float x = 1.6f; x < 16.6f; x += 2.0f)
				angularGroup.elements.Add(Line(angularGroup, x + 0.6f, lineY(x + 0.6f), angle, 1.2f));

			foreach (var mark in new[] { (2.16f, "2", 3.33f, -5.10f), (6.37f, "1", 7.25f, -6.27f), (14.7f, "0,5", 14.7f, -10.0f) })
			{
				angularGroup.elements.Add(Line(angularGroup, mark.Item1, lineY(mark.Item1) - 0.5f, 90f, 1.0f));
				angularGroup.elements.Add(Label(angularGroup, font, mark.Item2, mark.Item3, mark.Item4, 12f));
			}
			return angularGroup;
		}

		static ReticleTree.Angular InfoBlockMarks(ReticleTree.Angular parent)
		{
			// The "= | =" marks right of the chevron, measured off the figure.
			var angularGroup = Group(parent, "Info block marks", 11.3f, 2.7f);
			angularGroup.elements.Add(Line(angularGroup, -1.6f, 0.4f, 0f, 2.0f));
			angularGroup.elements.Add(Line(angularGroup, -1.6f, -0.4f, 0f, 2.0f));
			angularGroup.elements.Add(Line(angularGroup, 0f, 0f, 90f, 1.3f));
			angularGroup.elements.Add(Line(angularGroup, 1.5f, 0.4f, 0f, 1.8f));
			angularGroup.elements.Add(Line(angularGroup, 1.5f, -0.4f, 0f, 1.8f));
			return angularGroup;
		}

		static ReticleTree.Angular LeadMarks(ReticleTree.Angular parent)
		{
			// The lead scales: 0-02 a division out to ±0-16, a small chevron every 0-08 («Цена одного малого деления 0-02,
			// общий диапазон ±0-16»). The bar over the chevron spans the two 0-02 marks.
			var angularGroup = Group(parent, "Horizontal", 0f, 0f);
			angularGroup.elements.Add(Line(angularGroup, 0f, 0f, 0f, 2f * Lead));
			for (int side = -1; side <= 1; side += 2)
			{
				for (int block = 0; block < 2; block++)
				{
					float blockOffset = side * block * 4 * Lead;
					angularGroup.elements.Add(Line(angularGroup, blockOffset + side * 1 * Lead, -0.4811f, 90f, 0.9817f));
					angularGroup.elements.Add(Line(angularGroup, blockOffset + side * 2 * Lead, -0.898f, 90f, 1.8326f));
					angularGroup.elements.Add(Line(angularGroup, blockOffset + side * 3 * Lead, -0.4811f, 90f, 0.9817f));
					float centerX = blockOffset + side * 4 * Lead;
					angularGroup.elements.Add(Line(angularGroup, centerX - 0.3595f, -0.9878f, 250f, 2.1991f));
					angularGroup.elements.Add(Line(angularGroup, centerX + 0.3595f, -0.9878f, 290f, 2.1991f));
				}
			}
			return angularGroup;
		}

		static ReticleTree.Angular BScale(ReticleTree.Angular parent, AmmoCodexScriptable apfsds, ReticleTree.Text font)
		{
			// The Б scale: 200 m a division, numbered in hundreds every 400, slanting as the figure draws it.
			var group = Group(parent, "range-B", BScaleX, TopY);
			Glyph(group, 'Б', -1.3f, -2.4f, 1.6f);
			var ballistic = Ballistic(group, apfsds, 0f, 0f);
			for (int range = 0; range <= 2800; range += 200)
			{
				bool major = range % 400 == 0;
				var mark = Group(ballistic, "-" + range + "M", range * BScaleSlope, -range);
				mark.elements.Add(Line(mark, 0.5f, 0f, 0f, major ? 1.0f : 0.5f));
				if (major) mark.elements.Add(Label(mark, font, (range / 100).ToString(), -1.2f, 0.9f, 10f));
				ballistic.elements.Add(mark);
			}
			group.elements.Add(ballistic);
			return group;
		}

		static ReticleTree.Angular VerticalScale(ReticleTree.Angular parent, string name, float x, string letter,
			AmmoCodexScriptable ammo, ReticleTree.Text font)
		{
			// The О and Н scales: 100 m a division, numbered in hundreds every 400 on alternate sides, as on the figure.
			var group = Group(parent, name, x, TopY);
			group.elements.Add(Label(group, font, letter, 0f, -2.6311f, 12f));
			var ballistic = Ballistic(group, ammo, 0f, 0f);
			for (int range = 0; range <= 3700; range += 100)
			{
				float len = range % 400 == 0 && range < 3700 ? 2.513f : (range % 200 == 0 ? 1.885f : 1.257f);
				var mark = Group(ballistic, "-" + range + "M", 0f, -range);
				mark.elements.Add(Line(mark, 0f, 0f, 0f, len));
				if (range % 400 == 0 && range <= 3600)
				{
					float side = (range / 400) % 2 == 0 ? -1f : 1f;
					mark.elements.Add(Label(mark, font, (range / 100).ToString(), side * VScaleLabelX, 0f, 10f));
				}
				ballistic.elements.Add(mark);
			}
			group.elements.Add(ballistic);
			return group;
		}

		// Strokes for the letters the sight draws, in case the reticle font has no Cyrillic.
		internal static void Glyph(ReticleTree.GroupBase parent, char letter, float cx, float cy, float h)
		{
			float width = h * 0.6f;
			float left = cx - width / 2f;
			float right = cx + width / 2f;
			float bottom = cy - h / 2f;
			float top = cy + h / 2f;
			var strokes = new List<(float, float, float, float)>();
			if (letter == 'П')
			{
				strokes.Add((left, bottom, left, top));
				strokes.Add((right, bottom, right, top));
				strokes.Add((left, top, right, top));
			}
			else if (letter == 'Б')
			{
				strokes.Add((left, bottom, left, top));
				strokes.Add((left, top, right, top));
				strokes.Add((left, cy, right, cy));
				strokes.Add((left, bottom, right, bottom));
				strokes.Add((right, bottom, right, cy));
			}
			foreach (var (startX, startY, endX, endY) in strokes)
			{
				float len = Mathf.Sqrt((endX - startX) * (endX - startX) + (endY - startY) * (endY - startY));
				float deg = Mathf.Atan2(endY - startY, endX - startX) * Mathf.Rad2Deg;
				parent.elements.Add(Line(parent, (startX + endX) / 2f, (startY + endY) / 2f, deg, len));
			}
		}

		internal static ReticleTree.Angular Group(ReticleTree.TransformElement parent, string name, float x, float y,
			ReticleTree.GroupBase.Alignment align = ReticleTree.GroupBase.Alignment.None)
		{
			var angularGroup = new ReticleTree.Angular(new Vector2(x, y), parent, align);
			angularGroup.name = name;
			return angularGroup;
		}

		internal static ReticleTree.VerticalBallistic Ballistic(
			ReticleTree.TransformElement parent,
			AmmoCodexScriptable ammo,
			float x,
			float y)
		{
			var ballistic = new ReticleTree.VerticalBallistic(new Vector2(x, y), parent);
			ballistic.projectile = ammo;
			ballistic.UpdateBC();
			return ballistic;
		}

		internal static ReticleTree.Line Line(
			ReticleTree.TransformElement parent,
			float x,
			float y,
			float degrees,
			float length,
			float thickness = Thick)
		{
			var line = new ReticleTree.Line(new Vector2(x, y), degrees, length, thickness, 1f, parent);
			line.length.unit = AngularLength.AngularUnit.MIL_USSR;
			line.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
			line.visualType = ReticleTree.VisualElement.Type.Painted;
			line.illumination = ReticleTree.Light.Type.NightIllumination;
			return line;
		}

		static readonly MethodInfo memberwiseClone = typeof(object).GetMethod(
			"MemberwiseClone",
			BindingFlags.NonPublic | BindingFlags.Instance);

		internal static ReticleTree.Text Label(
			ReticleTree.TransformElement parent,
			ReticleTree.Text template,
			string text,
			float x,
			float y,
			float size)
		{
			var label = (ReticleTree.Text)memberwiseClone.Invoke(template, null);
			label.position = new ReticleTree.Position(x, y, AngularLength.AngularUnit.MIL_USSR);
			label.rotation = new AngularLength(0f, AngularLength.AngularUnit.DEG);
			label.parent = parent;
			label.text = text;
			label.fontSize = size;
			label.visualType = ReticleTree.VisualElement.Type.Painted;
			label.illumination = ReticleTree.Light.Type.NightIllumination;
			return label;
		}

		internal static ReticleTree.Text FontTemplate(ReticleSO host)
		{
			FieldInfo fontField = Reflect.Field(typeof(ReticleTree.Text), "font");
			List<ReticleTree.Text> labels = AllOf<ReticleTree.Text>(host).ToList();
			ReticleTree.Text etched = fontField == null ? null : labels.FirstOrDefault(label =>
				fontField.GetValue(label) is UnityEngine.Object font && font != null && font.name.StartsWith("TPD_Etch"));
			return etched ?? labels.First();
		}

		static bool _superelevationWarned;

		internal static float Superelevation(ReticleTree.Angular group, int range, float fallback)
		{
			try
			{
				ReticleTree.VerticalBallistic ballistic = group.elements.OfType<ReticleTree.VerticalBallistic>().First();
				MethodInfo toSe = Reflect.Method(typeof(ReticleTree.VerticalBallistic), "DistanceToSuperelevation");
				var elevation = (AngularLength)toSe.Invoke(ballistic, new object[] { new LinearLength(range) });
				if (elevation.MRADS < 0f) return elevation.MRADS;
			}
			catch (Exception exception)
			{
				if (!_superelevationWarned)
				{
					_superelevationWarned = true;
					MelonLogger.Warning($"{T90AMod.FriendlyName}: the game's range-to-elevation lookup failed "
						+ $"({exception.Message}); the 1G46 marks use the built-in figures.");
				}
			}
			return fallback;
		}

		internal static IEnumerable<T> AllOf<T>(ReticleSO so) where T : ReticleTree.TransformElement
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				ReticleTree.TransformElement element = stack.Pop();
				if (element is T match) yield return match;
				if (element is ReticleTree.GroupBase groupElement) foreach (var child in groupElement.elements) stack.Push(child);
			}
		}
	}
}
