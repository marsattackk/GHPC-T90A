using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Weaponry;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace T90A
{
	internal static class Tkn4sReticle
	{
		internal const string Name = "TKN-4S (T-90A)";
		internal const float ScaleZeroY = 25.1f;
		const float LeadStep = 2.094f;
		internal static readonly float[] CoaxScalePhoto = { 3.9f, 6.9f, 10.4f, 14.3f, 18.6f, 23.5f, 28.8f, 34.9f, 41.4f };
		internal static readonly Dictionary<int, float> CoaxScaleShortPhoto = new Dictionary<int, float> { { 400, 7.5f }, { 500, 6.5f }, { 600, 4.5f }, { 700, 2.8f }, { 800, 1.0f } };
		const float Thin = 0.25f, Bar = 0.3f;
		const float StadiaX = 36.7f, StadiaY = 12.3f, StadiaX40 = 29.0f, StadiaStep = 2.2f;
		static readonly RGB Amber = new RGB(4f, 2.2f, 0.5f);
		internal const float FieldR = 68.07f;
		internal const float ReadyX = -0.18f * FieldR, IndicatorX = 0.118f * FieldR;

		static ReticleSO _built;

		internal static ReticleSO Get(ReticleSO host, ReticleSO fontHost, AmmoCodexScriptable b32, AmmoCodexScriptable apfsds, AmmoCodexScriptable he, AmmoCodexScriptable heat)
		{
			if (_built != null) return _built;
			var so = ScriptableObject.CreateInstance<ReticleSO>();
			so.name = Name;
			so.lights = host.lights.Select(l => new ReticleTree.Light { type = l.type, color = l.type == ReticleTree.Light.Type.NightIllumination ? Amber : l.color }).ToList();
			if (!so.lights.Any(l => l.type == ReticleTree.Light.Type.NightIllumination)) so.lights.Add(new ReticleTree.Light { type = ReticleTree.Light.Type.NightIllumination, color = Amber });
			ReticleTree.Text text = null;
			try { if (fontHost != null) text = Sight1G46.FontTemplate(fontHost); } catch (InvalidOperationException) { }
			if (text == null) MelonLogger.Warning($"T-90A Kord (ZPU): no reticle label to take the font from ('{fontHost?.name}'); TKN-4S figures not drawn.");
			_text = text;

			var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			so.planes.Add(plane);
			var bore = G(null, "Boresight", 0f, 0f, ReticleTree.GroupBase.Alignment.Boresight);
			var range = G(null, "Range line", 0f, 0f, ReticleTree.GroupBase.Alignment.Impact);
			plane.elements.Add(bore);
			plane.elements.Add(range);

			var chev = G(bore, "Chevron", 0f, 0f);
			chev.elements.Add(L(chev, 0.585f, -1.275f, 294.65f, 2.806f, Bar));
			chev.elements.Add(L(chev, -0.585f, -1.275f, 65.35f, 2.806f, Bar));
			bore.elements.Add(chev);

			var lead = G(bore, "Lead", 0f, 0f);
			for (int side = -1; side <= 1; side += 2)
				for (int k = 1; k <= 10; k++)
				{
					float x = side * k * LeadStep;
					if (k == 4 || k == 8)
					{
						lead.elements.Add(L(lead, x + 0.3f, -0.625f, 295.6f, 1.386f));
						lead.elements.Add(L(lead, x - 0.3f, -0.625f, 64.4f, 1.386f));
					}
					else lead.elements.Add(L(lead, x, -0.6f, 90f, 1.2f));
				}
			bore.elements.Add(lead);

			var mg = G(bore, "P scale (fixed)", 0f, 0f);
			void Mark(int r, float y)
			{
				var m = G(mg, r + "M", 0f, y);
				m.elements.Add(L(m, -0.4f, 0f, 0f, 0.8f));
				m.elements.Add(L(m, -0.8f, -0.5f, 90f, 1.0f));
				if (r % 200 == 0) m.elements.Add(T(m, text, (r / 100).ToString(), r == 600 ? -2.2f : -2.4f, -0.5f, 10f));
				mg.elements.Add(m);
			}
			foreach (var kv in CoaxScaleShortPhoto) Mark(kv.Key, kv.Value);
			for (int i = 0; i < CoaxScalePhoto.Length; i++) Mark(1000 + 100 * i, -CoaxScalePhoto[i]);
			Sight1G46.Glyph(mg, 'П', -0.4f, -CoaxScalePhoto[CoaxScalePhoto.Length - 1] - 3.5f, 1.0f);
			void Hook(string label, float x, float y, float labelX)
			{
				var h = G(mg, "hook " + label, 0f, y);
				h.elements.Add(L(h, x, 0f, 0f, 0.8f));
				h.elements.Add(L(h, x + 0.4f, -0.3f, 90f, 0.6f));
				h.elements.Add(T(h, text, label, labelX, -0.5f, 9f));
				mg.elements.Add(h);
			}
			Hook("1,5", -1.9f, CoaxScaleShortPhoto[500], -4.2f);
			Hook("1", -3.1f, CoaxScaleShortPhoto[600], -4.85f);
			Hook("0,5", -4.0f, -CoaxScalePhoto[0], -5.8f);
			bore.elements.Add(mg);

			bore.elements.Add(Stadia(bore, text));

			bore.elements.Add(Scale(bore, text, "B", -30.5f, apfsds, ScaleRanges(600, 4000), r => r == 4000 || (r - 600) % 400 == 0, r => (r == 4000 || ((r - 600) / 400) % 2 == 1) ? 1 : -1, 0));
			bore.elements.Add(Scale(bore, text, "O", -16.6f, he, ScaleRanges(400, 2400, true), r => r % 400 == 0, r => -1, 1));
			bore.elements.Add(Scale(bore, text, "K", 16.4f, heat, ScaleRanges(400, 2400, true), r => r % 400 == 0, r => 1, -1));
			bore.elements.Add(Scale(bore, text, "3", 30.0f, b32, ScaleRanges(400, 2000, true), r => r % 400 == 0, r => (r / 400) % 2 == 0 ? -1 : 1, 0));

			range.elements.Add(L(range, -21.75f, ScaleZeroY, 0f, 31.5f, Bar));
			range.elements.Add(L(range, 20.75f, ScaleZeroY, 0f, 30.5f, Bar));
			return _built = so;
		}

		static ReticleSO _night;
		static ReticleTree.Text _text;

		const float NightPx = 2.12f, NightSym = 0.5f;
		internal static ReticleSO Night()
		{
			if (_night != null) return _night;
			var so = ScriptableObject.CreateInstance<ReticleSO>();
			so.name = Name + " night";
			so.lights = new List<ReticleTree.Light> { new ReticleTree.Light { type = ReticleTree.Light.Type.Powered, color = Amber * 0.5f } };
			var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			so.planes.Add(plane);
			var bore = G(null, "Boresight", 0f, 0f, ReticleTree.GroupBase.Alignment.Boresight);
			var range = G(null, "Range line", 0f, 0f, ReticleTree.GroupBase.Alignment.Impact);
			plane.elements.Add(bore);
			plane.elements.Add(range);
			var text = _text;

			float P(float px) => px / NightPx;
			float S(float px) => px / NightPx * NightSym;
			void H(ReticleTree.Angular g, float x1, float x2, float y, float thick = Thin) => g.elements.Add(L(g, P((x1 + x2) / 2f), P(y), 0f, S(x2 - x1), thick));
			void Tx(ReticleTree.Angular g, string s, float x, float y, float size = 24f) => g.elements.Add(T(g, text, s, P(x), P(y), size));

			var c = G(bore, "Central mark", 0f, 0f);
			float hw = S(11.9f), th = S(15f), side = Mathf.Sqrt(hw * hw + th * th), ang = Mathf.Atan2(th, hw) * Mathf.Rad2Deg;
			c.elements.Add(L(c, -hw / 2f, -th / 2f, ang, side, Bar));
			c.elements.Add(L(c, hw / 2f, -th / 2f, 180f - ang, side, Bar));
			c.elements.Add(L(c, 0f, -th, 0f, 2f * hw, Bar));
			c.elements.Add(L(c, 0f, P(-28.75f), 0f, S(16.25f), Bar));
			bore.elements.Add(c);

			var lead = G(bore, "Lead", 0f, 0f);
			foreach (var (x, y, len) in new[] { (-56.75f, -7.5f, 15f), (21.25f, -5.6f, 18.75f), (45f, -5f, 17.5f) })
				lead.elements.Add(L(lead, P(x), P(y), 90f, S(len)));
			bore.elements.Add(lead);

			var mg = G(bore, "MG marks", 0f, 0f);
			foreach (var (x, y, s, lx, ly) in new[] { (-18.75f, -25f, "4", -30f, -16f), (-30f, -40f, "1", -41f, -36f), (-10f, -56.25f, "8", -16f, -46f), (-38.75f, -71.25f, "0,5", -35f, -88.75f) })
			{
				float w = S(11f), hgt = S(7.5f), leg = Mathf.Sqrt(w * w / 4f + hgt * hgt), a = Mathf.Atan2(hgt, w / 2f) * Mathf.Rad2Deg;
				mg.elements.Add(L(mg, P(x) - w / 4f, P(y) - hgt / 2f, a, leg));
				mg.elements.Add(L(mg, P(x) + w / 4f, P(y) - hgt / 2f, 180f - a, leg));
				Tx(mg, s, lx, ly);
			}
			bore.elements.Add(mg);

			var sc = G(bore, "Scales", 0f, 0f);
			foreach (var (x1, x2, y) in new[] {
				(-121.25f, -97.5f, -6.75f), (-121.25f, -85f, -13.25f), (-121.25f, -101.25f, -20.75f), (-121.25f, -91.25f, -28.25f),
				(86.25f, 110f, -8.75f), (86.25f, 110f, -15f), (86.25f, 110f, -22.5f), (86.25f, 110f, -28.75f), (86.25f, 110f, -33.25f),
				(81.25f, 112.5f, -51.25f), (92.5f, 107.5f, -60f), (85f, 110f, -68.75f), (93.75f, 107.5f, -81.25f),
				(91.25f, 110f, -107.5f), (91.25f, 110f, -112.5f), (91.25f, 110f, -117.5f), (85f, 110f, -125f) })
				H(sc, x1, x2, y);
			Tx(sc, "16", -80f, -2.5f); Tx(sc, "12", -78.75f, -23.75f);
			Tx(sc, "16", 76.25f, -6.25f); Tx(sc, "12", 78.75f, -25f);
			Tx(sc, "4", 81.25f, -60f); Tx(sc, "0", 82.5f, -81.25f); Tx(sc, "20", 80f, -106.25f);
			Sight1G46.Glyph(sc, 'ш', P(87.5f), P(-43.75f), 2.0f);
			Sight1G46.Glyph(sc, 'П', P(86.25f), P(-123.75f) + 1.0f, 2.0f);
			bore.elements.Add(sc);

			var idx = G(bore, "Index letters", 0f, 0f);
			Tx(idx, "O", -139.5f, -8.75f, 28f);
			Tx(idx, "K", 130f, -15f, 28f);
			Sight1G46.Glyph(idx, 'Б', P(133.75f), P(-61.25f), S(12f));
			bore.elements.Add(idx);
			H(range, -147.5f, -127.5f, -21.25f, Bar);
			H(range, 113.75f, 140f, -22.5f, Bar);
			H(range, 113.75f, 140f, -73.75f, Bar);

			foreach (var v in Sight1G46.AllOf<ReticleTree.VisualElement>(so))
			{
				v.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
				v.illumination = ReticleTree.Light.Type.Powered;
			}
			return _night = so;
		}

		static IEnumerable<int> ScaleRanges(int from, int to, bool zero = false)
		{
			if (zero) yield return 0;
			for (int r = from; r <= to; r += 200) yield return r;
		}

		static ReticleTree.Angular Scale(ReticleTree.Angular parent, ReticleTree.Text text, string letter, float x, AmmoCodexScriptable ammo,
			IEnumerable<int> ranges, Func<int, bool> major, Func<int, int> side, int twoSide)
		{
			var g = G(parent, "range-" + letter, x, ScaleZeroY);
			if (letter == "B") Sight1G46.Glyph(g, 'Б', 0f, -2.6f, 2.0f);
			else g.elements.Add(T(g, text, letter, 0f, -2.6f, 20f));
			if (ammo == null) return g;
			var vb = Ballistic(g, ammo);
			foreach (int r in ranges)
			{
				bool big = major(r);
				var m = G(vb, "-" + r + "M", 0f, -r);
				m.elements.Add(L(m, 0f, 0f, 0f, big ? 2.4f : 1.4f));
				if (big) m.elements.Add(T(m, text, (r / 100).ToString(), side(r) * 2.4f, 0f, 9f));
				if (r == 400 && twoSide != 0) m.elements.Add(T(m, text, "2", twoSide * 2.4f, 0f, 9f));
				vb.elements.Add(m);
			}
			g.elements.Add(vb);
			return g;
		}

		static ReticleTree.Angular Stadia(ReticleTree.Angular parent, ReticleTree.Text text)
		{
			var g = G(parent, "Stadia", 0f, 0f);
			g.elements.Add(L(g, StadiaX, StadiaY, 0f, 17.8f, Bar));
			int[] hundreds = { 40, 35, 30, 25, 20, 15, 10, 5 };
			for (int i = 0; i < hundreds.Length; i++)
			{
				int R = hundreds[i];
				float x = StadiaX40 + i * StadiaStep;
				float y2 = StadiaY - 20f / R;
				g.elements.Add(L(g, x, y2, 0f, 1.4f));
				g.elements.Add(L(g, x, y2 - 0.6f, 90f, 1.2f));
				g.elements.Add(T(g, text, R.ToString(), x, y2 - 2.0f, 8f));
				if (R < 10) continue;
				float y25 = StadiaY + 25f / R;
				g.elements.Add(L(g, x, y25, 0f, 1.4f));
				g.elements.Add(L(g, x, y25 + 0.6f, 90f, 1.2f));
				g.elements.Add(T(g, text, R.ToString(), x, y25 + 2.0f, 8f));
			}
			g.elements.Add(T(g, text, "2,5", StadiaX + 11.0f, StadiaY + 2.2f, 18f));
			g.elements.Add(T(g, text, "2", StadiaX + 10.4f, StadiaY - 2.0f, 18f));
			return g;
		}

		static ReticleTree.Angular G(ReticleTree.TransformElement parent, string name, float x, float y, ReticleTree.GroupBase.Alignment a = ReticleTree.GroupBase.Alignment.None)
			=> Sight1G46.Group(parent, name, x, y, a);

		static ReticleTree.VerticalBallistic Ballistic(ReticleTree.TransformElement parent, AmmoCodexScriptable ammo) => Sight1G46.Ballistic(parent, ammo, 0f, 0f);

		static ReticleTree.Line L(ReticleTree.TransformElement parent, float x, float y, float deg, float len, float thick = Thin) => Sight1G46.Line(parent, x, y, deg, len, thick);

		static ReticleTree.TransformElement T(ReticleTree.TransformElement parent, ReticleTree.Text template, string s, float x, float y, float size)
			=> template != null ? (ReticleTree.TransformElement)Sight1G46.Label(parent, template, s, x, y, size) : Sight1G46.Group(parent, "no font: " + s, x, y);
	}
}
