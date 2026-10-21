using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Equipment.Optics;
using GHPC.Weaponry;
using Reticle;
using UnityEngine;

namespace T90A
{
	public static class Sight1G46
	{
		public static readonly float[] ZoomFovs = { 20f, 16.6f, 13.8f, 11.4f, 9.5f, 7.9f, 6.5f, 5.4f, 4.5f };

		public static string ApfsdsAmmo = "ammo_3BM32";
		public static string HeAmmo = "ammo_3OF26";
		public static string HeatAmmo = "ammo_3BK14M";
		public static string CoaxAmmo = "ammo_762x54R";

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

		static ReticleSO built;

		public static void Apply(UsableOptic dayOptic, AmmoCodexScriptable apfsds, AmmoCodexScriptable he,
			AmmoCodexScriptable heat, AmmoCodexScriptable coax)
		{
			ReticleMesh rm = dayOptic.reticleMesh;
			if (built == null) built = Build(rm.reticleSO, apfsds, he, heat, coax);

			rm.reticleSO = built;
			typeof(ReticleMesh).GetField("SMR", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(rm, null);
			rm.Load();

			dayOptic.slot.AllowFreeZoom = true;
			dayOptic.slot.DefaultFov = ZoomFovs[0];
			dayOptic.slot.OtherFovs = ZoomFovs.Skip(1).ToArray();
		}

		public static ReticleSO Build(ReticleSO host, AmmoCodexScriptable apfsds, AmmoCodexScriptable he,
			AmmoCodexScriptable heat, AmmoCodexScriptable coax)
		{
			ReticleSO so = ScriptableObject.CreateInstance<ReticleSO>();
			so.name = "1G46";
			so.lights = new List<ReticleTree.Light>(host.lights);
			ReticleTree.Text font = FontTemplate(host);

			var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			so.planes.Add(plane);

			var bore = Group(null, "Boresight", 0f, 0f, ReticleTree.GroupBase.Alignment.Boresight);
			var moving = Group(null, "VerticalRange", 0f, 0f, ReticleTree.GroupBase.Alignment.VerticalRange);
			plane.elements.Add(bore);
			plane.elements.Add(moving);

			var chevron = Group(bore, "Chevron", 0f, 0f);
			chevron.elements.Add(Line(chevron, 0.75f, -1.55f, 294.97f, 3.6652f));
			chevron.elements.Add(Line(chevron, -0.7461f, -1.5504f, 65.03f, 3.6652f));
			bore.elements.Add(chevron);
			bore.elements.Add(Line(bore, 0f, TopY, 0f, 78.6f, TopThick));
			var mg = CoaxScale(bore, coax, font);
			float bottom = Mathf.Min(VerticalBottom, Superelevation(mg, MgMaxRange, -30.1f) - 1.0f);
			bore.elements.Add(Line(bore, 0f, (-0.9f + bottom) / 2f, 90f, -0.9f - bottom));
			bore.elements.Add(mg);
			bore.elements.Add(StadiaScale(bore, font));
			bore.elements.Add(CoaxParallaxCurve(bore, font));
			bore.elements.Add(InfoBlockMarks(bore));

			moving.elements.Add(LeadMarks(moving));
			moving.elements.Add(BScale(moving, apfsds, font));
			moving.elements.Add(VerticalScale(moving, "range-O", OScaleX, "O", he, font));
			moving.elements.Add(VerticalScale(moving, "range-H", HScaleX, "H", heat, font));

			return so;
		}

		public static ReticleSO BuildEssa()
		{
			ReticleSO so = ScriptableObject.CreateInstance<ReticleSO>();
			so.name = "ESSA";
			so.lights = new List<ReticleTree.Light> { new ReticleTree.Light { type = ReticleTree.Light.Type.Powered, color = new RGB(2f, 2f, 2f) } };

			var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			so.planes.Add(plane);

			var bore = Group(null, "Boresight", 0f, 0f, ReticleTree.GroupBase.Alignment.Boresight);
			var moving = Group(null, "VerticalRange", 0f, 0f, ReticleTree.GroupBase.Alignment.VerticalRange);
			plane.elements.Add(bore);
			plane.elements.Add(moving);

			var chevron = Group(bore, "Chevron", 0f, 0f);
			chevron.elements.Add(Line(chevron, 0.75f, -1.55f, 294.97f, 3.6652f));
			chevron.elements.Add(Line(chevron, -0.7461f, -1.5504f, 65.03f, 3.6652f));
			bore.elements.Add(chevron);
			moving.elements.Add(LeadMarks(moving));

			foreach (var line in AllOf<ReticleTree.Line>(so))
			{
				line.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
				line.illumination = ReticleTree.Light.Type.Powered;
			}
			return so;
		}

		public static AmmoCodexScriptable FindAmmo(string name, ReticleSO host, string fallbackToken)
		{
			AmmoCodexScriptable found = Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>().FirstOrDefault(a => a.name == name);
			if (found != null) return found;
			return AllOf<ReticleTree.VerticalBallistic>(host)
				.Select(vb => vb.projectile)
				.FirstOrDefault(p => p != null && p.name.Contains(fallbackToken));
		}

		static ReticleTree.Angular CoaxScale(ReticleTree.Angular parent, AmmoCodexScriptable coax, ReticleTree.Text font)
		{
			var group = Group(parent, "range-MG", 0f, 0f);
			var vb = Ballistic(group, coax, 0f, 0f);
			int[] ranges = { 200, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400 };
			foreach (int range in ranges)
			{
				bool major = range % 200 == 0 && range >= 400;
				var mark = Group(vb, range + "M", 0f, range);
				float len = range == 200 ? 0.5236f : (major ? 2.0944f : 1.5708f);
				float x = range == 200 ? -0.2454f : (major ? -0.9817f : -0.7363f);
				mark.elements.Add(Line(mark, x, 0f, 0f, len));
				if (major) mark.elements.Add(Label(mark, font, (range / 100).ToString(), -3.55f, 0.26f, 12f));
				if (range == 800) Glyph(mark, 'П', 2.35f, 0f, 1.4f);
				vb.elements.Add(mark);
			}
			group.elements.Add(vb);
			return group;
		}

		static ReticleTree.Angular StadiaScale(ReticleTree.Angular parent, ReticleTree.Text font)
		{
			var group = Group(parent, "Stadia", 0f, 0f);
			float left = StadiaX800 - 0.3f, right = StadiaX800 + 5 * StadiaXPer400 + 0.5f;
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
			var g = Group(parent, "Coax parallax", 0f, 0f);
			Func<float, float> y = x => -2.84f - 0.38f * (x - 0.69f);
			float angle = Mathf.Atan(-0.38f) * Mathf.Rad2Deg;

			g.elements.Add(Line(g, 0.95f, -2.55f, 135f, 0.75f));
			g.elements.Add(Line(g, 0.95f, -3.13f, 45f, 0.75f));
			for (float x = 1.6f; x < 16.6f; x += 2.0f)
				g.elements.Add(Line(g, x + 0.6f, y(x + 0.6f), angle, 1.2f));

			foreach (var mark in new[] { (2.16f, "2", 3.33f, -5.10f), (6.37f, "1", 7.25f, -6.27f), (14.7f, "0,5", 14.7f, -10.0f) })
			{
				g.elements.Add(Line(g, mark.Item1, y(mark.Item1) - 0.5f, 90f, 1.0f));
				g.elements.Add(Label(g, font, mark.Item2, mark.Item3, mark.Item4, 12f));
			}
			return g;
		}

		static ReticleTree.Angular InfoBlockMarks(ReticleTree.Angular parent)
		{
			var g = Group(parent, "Info block marks", 11.3f, 2.7f);
			g.elements.Add(Line(g, -1.6f, 0.4f, 0f, 2.0f));
			g.elements.Add(Line(g, -1.6f, -0.4f, 0f, 2.0f));
			g.elements.Add(Line(g, 0f, 0f, 90f, 1.3f));
			g.elements.Add(Line(g, 1.5f, 0.4f, 0f, 1.8f));
			g.elements.Add(Line(g, 1.5f, -0.4f, 0f, 1.8f));
			return g;
		}

		static ReticleTree.Angular LeadMarks(ReticleTree.Angular parent)
		{
			var g = Group(parent, "Horizontal", 0f, 0f);
			g.elements.Add(Line(g, 0f, 0f, 0f, 2f * Lead));
			for (int side = -1; side <= 1; side += 2)
			{
				for (int block = 0; block < 2; block++)
				{
					float o = side * block * 4 * Lead;
					g.elements.Add(Line(g, o + side * 1 * Lead, -0.4811f, 90f, 0.9817f));
					g.elements.Add(Line(g, o + side * 2 * Lead, -0.898f, 90f, 1.8326f));
					g.elements.Add(Line(g, o + side * 3 * Lead, -0.4811f, 90f, 0.9817f));
					float cx = o + side * 4 * Lead;
					g.elements.Add(Line(g, cx - 0.3595f, -0.9878f, 250f, 2.1991f));
					g.elements.Add(Line(g, cx + 0.3595f, -0.9878f, 290f, 2.1991f));
				}
			}
			return g;
		}

		static ReticleTree.Angular BScale(ReticleTree.Angular parent, AmmoCodexScriptable apfsds, ReticleTree.Text font)
		{
			var group = Group(parent, "range-B", BScaleX, TopY);
			Glyph(group, 'Б', -1.3f, -2.4f, 1.6f);
			var vb = Ballistic(group, apfsds, 0f, 0f);
			for (int range = 0; range <= 2800; range += 200)
			{
				bool major = range % 400 == 0;
				var mark = Group(vb, "-" + range + "M", range * BScaleSlope, -range);
				mark.elements.Add(Line(mark, 0.5f, 0f, 0f, major ? 1.0f : 0.5f));
				if (major) mark.elements.Add(Label(mark, font, (range / 100).ToString(), -1.2f, 0.9f, 10f));
				vb.elements.Add(mark);
			}
			group.elements.Add(vb);
			return group;
		}

		static ReticleTree.Angular VerticalScale(ReticleTree.Angular parent, string name, float x, string letter,
			AmmoCodexScriptable ammo, ReticleTree.Text font)
		{
			var group = Group(parent, name, x, TopY);
			group.elements.Add(Label(group, font, letter, 0f, -2.6311f, 12f));
			var vb = Ballistic(group, ammo, 0f, 0f);
			for (int range = 0; range <= 3700; range += 100)
			{
				float len = range % 400 == 0 && range < 3700 ? 2.513f : (range % 200 == 0 ? 1.885f : 1.257f);
				var mark = Group(vb, "-" + range + "M", 0f, -range);
				mark.elements.Add(Line(mark, 0f, 0f, 0f, len));
				if (range % 400 == 0 && range <= 3600)
				{
					float side = (range / 400) % 2 == 0 ? -1f : 1f;
					mark.elements.Add(Label(mark, font, (range / 100).ToString(), side * VScaleLabelX, 0f, 10f));
				}
				vb.elements.Add(mark);
			}
			group.elements.Add(vb);
			return group;
		}

		internal static void Glyph(ReticleTree.GroupBase parent, char letter, float cx, float cy, float h)
		{
			float w = h * 0.6f, l = cx - w / 2f, r = cx + w / 2f, b = cy - h / 2f, t = cy + h / 2f;
			var strokes = new List<(float, float, float, float)>();
			if (letter == 'П')
			{
				strokes.Add((l, b, l, t));
				strokes.Add((r, b, r, t));
				strokes.Add((l, t, r, t));
			}
			else if (letter == 'ш')
			{
				strokes.Add((l, b, l, t));
				strokes.Add((cx, b, cx, t));
				strokes.Add((r, b, r, t));
				strokes.Add((l, b, r, b));
			}
			else if (letter == 'Б')
			{
				strokes.Add((l, b, l, t));
				strokes.Add((l, t, r, t));
				strokes.Add((l, cy, r, cy));
				strokes.Add((l, b, r, b));
				strokes.Add((r, b, r, cy));
			}
			foreach (var (x1, y1, x2, y2) in strokes)
			{
				float len = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
				float deg = Mathf.Atan2(y2 - y1, x2 - x1) * Mathf.Rad2Deg;
				parent.elements.Add(Line(parent, (x1 + x2) / 2f, (y1 + y2) / 2f, deg, len));
			}
		}

		internal static ReticleTree.Angular Group(ReticleTree.TransformElement parent, string name, float x, float y,
			ReticleTree.GroupBase.Alignment align = ReticleTree.GroupBase.Alignment.None)
		{
			var g = new ReticleTree.Angular(new Vector2(x, y), parent, align);
			g.name = name;
			return g;
		}

		internal static ReticleTree.VerticalBallistic Ballistic(ReticleTree.TransformElement parent, AmmoCodexScriptable ammo, float x, float y)
		{
			var vb = new ReticleTree.VerticalBallistic(new Vector2(x, y), parent);
			vb.projectile = ammo;
			vb.UpdateBC();
			return vb;
		}

		internal static ReticleTree.Line Line(ReticleTree.TransformElement parent, float x, float y, float degrees, float length, float thickness = Thick)
		{
			var line = new ReticleTree.Line(new Vector2(x, y), degrees, length, thickness, 1f, parent);
			line.length.unit = AngularLength.AngularUnit.MIL_USSR;
			line.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
			line.visualType = ReticleTree.VisualElement.Type.Painted;
			line.illumination = ReticleTree.Light.Type.NightIllumination;
			return line;
		}

		static readonly MethodInfo memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static ReticleTree.Text Label(ReticleTree.TransformElement parent, ReticleTree.Text template, string text, float x, float y, float size)
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
			FieldInfo fontField = typeof(ReticleTree.Text).GetField("font");
			List<ReticleTree.Text> labels = AllOf<ReticleTree.Text>(host).ToList();
			ReticleTree.Text etched = labels.FirstOrDefault(t =>
				fontField.GetValue(t) is UnityEngine.Object f && f != null && f.name.StartsWith("TPD_Etch"));
			return etched ?? labels.First();
		}

		internal static float Superelevation(ReticleTree.Angular group, int range, float fallback)
		{
			try
			{
				ReticleTree.VerticalBallistic vb = group.elements.OfType<ReticleTree.VerticalBallistic>().First();
				MethodInfo toSe = typeof(ReticleTree.VerticalBallistic).GetMethod("DistanceToSuperelevation", BindingFlags.NonPublic | BindingFlags.Instance);
				var se = (AngularLength)toSe.Invoke(vb, new object[] { new LinearLength(range) });
				if (se.MRADS < 0f) return se.MRADS;
			}
			catch (Exception) { }
			return fallback;
		}

		internal static IEnumerable<T> AllOf<T>(ReticleSO so) where T : ReticleTree.TransformElement
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				ReticleTree.TransformElement e = stack.Pop();
				if (e is T match) yield return match;
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
		}
	}
}
