using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace T90A.Common
{
	internal sealed class ReticleSpec
	{
		internal abstract class Node { internal string Path; }

		internal sealed class Group : Node
		{
			internal string Name, Align, Ammo;
			internal double X, Y, Stadia;
			internal readonly List<Node> Children = new List<Node>();
			internal bool Ranged => Ammo != null || Stadia > 0;
		}

		internal sealed class Line : Node { internal double X, Y, Deg, Len, W; internal string Visual, Light; }

		internal sealed class Circle : Node { internal double X, Y, R, W; internal int Segments; internal string Visual, Light; }

		internal sealed class Text : Node { internal string Value; internal double X, Y, Size; internal string Visual, Light; }

		internal sealed class ScreenItem
		{
			internal string Kind, Name, Anchor = "centre", Value, Font, Align = "center", Path;
			internal double X, Y, W, H, Size, Deg;
			internal bool FullWidth;
			internal float[] Color = { 1f, 1f, 1f, 1f };
		}

		internal string Name, Plane = "first", Source;
		internal double Fov;
		internal bool LightsFromHost;
		internal readonly List<KeyValuePair<string, float[]>> Lights = new List<KeyValuePair<string, float[]>>();
		internal readonly List<Node> Top = new List<Node>();
		internal string ScreenName;
		internal readonly List<ScreenItem> Screen = new List<ScreenItem>();
		internal readonly List<string> Problems = new List<string>();
		readonly Env _globals = new Env(null);

		internal static ReticleSpec FromResource(System.Reflection.Assembly asm, string resource)
		{
			using (var s = asm.GetManifestResourceStream(resource))
			{
				if (s == null) throw new ArgumentException($"no embedded resource '{resource}'");
				using (var r = new System.IO.StreamReader(s, Encoding.UTF8)) return Parse(r.ReadToEnd(), resource);
			}
		}

		internal static ReticleSpec Parse(string text, string source)
		{
			var spec = new ReticleSpec { Source = source };
			var root = new Json(text, source).Read() as JObj ?? throw new FormatException(source + ": the top level must be an object");
			spec.Load(root);
			return spec;
		}

		static readonly string[] TopKeys = { "name", "fov", "plane", "units", "style", "lights", "vars", "defs", "elements", "screen", "note" };

		void Load(JObj root)
		{
			Unknown(root, TopKeys, "top level");
			Name = root.Str("name") ?? throw new FormatException(Source + ": no 'name'");
			Fov = root.Has("fov") ? ToNum(root["fov"], Source + ": fov") : 0;
			Plane = root.Str("plane") ?? "first";
			_globals.Set("Fov", Fov);
			_globals.Set("PI", Math.PI);
			if (root["vars"] is JObj vars)
				foreach (var kv in vars.Items) _globals.Set(kv.Key, Value(kv.Value, _globals, "vars." + kv.Key));
			if (root["defs"] is JObj defs)
				foreach (var kv in defs.Items) _defs[kv.Key] = kv.Value as JObj ?? throw new FormatException($"{Source}: defs.{kv.Key} must be an object");
			if (root["lights"] is JObj lights)
			{
				Unknown(lights, new[] { "from_host", "set" }, "lights");
				LightsFromHost = lights["from_host"] is bool b && b;
				if (lights["set"] is JObj set)
					foreach (var kv in set.Items) Lights.Add(new KeyValuePair<string, float[]>(kv.Key, Floats(kv.Value, _globals, "lights.set." + kv.Key)));
			}
			var style = new Style { W = 0.25, Visual = "painted", Light = "NightIllumination", K = 1 };
			if (root.Has("units")) style.K = Units(root["units"], "units");
			style = style.With(root["style"] as JObj, this, _globals, "style");
			foreach (var e in List(root["elements"], "elements"))
				Expand(e, _globals, style, null, Top, Name);
			if (root["screen"] is JObj screen)
			{
				Unknown(screen, new[] { "name", "items", "note" }, "screen");
				ScreenName = screen.Str("name") ?? Name + " overlay";
				foreach (var e in List(screen["items"], "screen.items")) ScreenExpand(e, _globals, Screen, "screen");
			}
		}

		readonly Dictionary<string, JObj> _defs = new Dictionary<string, JObj>();

		sealed class Style
		{
			internal double W, K;
			internal string Visual, Light;
			internal Style With(JObj o, ReticleSpec spec, Env env, string where)
			{
				var s = (Style)MemberwiseClone();
				if (o == null) return s;
				spec.Unknown(o, new[] { "w", "visual", "light" }, where);
				if (o.Has("w")) s.W = spec.Num(o["w"], env, where + ".w") * s.K;
				if (o.Has("visual")) s.Visual = spec.Interp(o["visual"], env, where);
				if (o.Has("light")) s.Light = spec.Interp(o["light"], env, where);
				return s;
			}
		}

		double Units(object v, string where)
		{
			if (v is double d) return d;
			switch (v as string)
			{
				case "mrad": return 1;
				case "mil": return 2000 * Math.PI / 6000;
				case "mil_nato": return 2000 * Math.PI / 6400;
				case "deg": return 1000 * Math.PI / 180;
				case "moa": return 1000 * Math.PI / 180 / 60;
				case "field":
					if (Fov <= 0) throw new FormatException($"{Source}: {where}: 'field' units need 'fov'");
					return Fov / 2 * 1000 * Math.PI / 180;
			}
			throw new FormatException($"{Source}: {where}: unknown units '{v}'");
		}

		static readonly string[] Kinds = { "group", "line", "seg", "poly", "dash", "arc", "circle", "text", "glyph", "for", "if", "use" };
		static readonly string[] StrokeKeys = { "w", "visual", "light", "note" };

		void Expand(object raw, Env env, Style style, Group parent, List<Node> into, string path)
		{
			if (!(raw is JObj e)) { Problems.Add($"{path}: element is not an object"); return; }
			string kind = Kinds.FirstOrDefault(e.Has);
			if (kind == null) { Problems.Add($"{path}: no element kind (one of {string.Join(", ", Kinds)}) in {{{string.Join(", ", e.Items.Select(kv => kv.Key))}}}"); return; }
			string here = path + "/" + kind + "[" + into.Count + "]";
			bool rangedParent = parent != null && parent.Ranged;
			double K = style.K;
			(double, double) At(object v, string w)
			{
				var p = Floats(v ?? new List<object> { 0.0, 0.0 }, env, w);
				if (p.Length != 2) throw new FormatException($"{Source}: {w}: 'at' needs [x, y]");
				return (p[0] * K, rangedParent ? p[1] : p[1] * K);
			}
			Style Own() => style.With(new JObj(e.Items.Where(kv => kv.Key == "w" || kv.Key == "visual" || kv.Key == "light")), this, env, here);
			void Add(Line l) { l.Path = here; into.Add(l); }
			void Seg(Style s, double x1, double y1, double x2, double y2)
			{
				double dx = x2 - x1, dy = y2 - y1;
				Add(new Line { X = (x1 + x2) / 2, Y = (y1 + y2) / 2, Deg = Math.Atan2(dy, dx) * 180 / Math.PI, Len = Math.Sqrt(dx * dx + dy * dy), W = s.W, Visual = s.Visual, Light = s.Light });
			}
			double Ys(double y) => rangedParent ? y : y * K;

			switch (kind)
			{
				case "group":
				{
					Unknown(e, new[] { "group", "at", "align", "ballistic", "stadia", "units", "style", "elements", "note" }, here);
					var (x, y) = At(e["at"], here + ".at");
					var g = new Group { Name = Interp(e["group"], env, here), X = x, Y = y, Path = here };
					if (e.Has("align")) g.Align = Interp(e["align"], env, here);
					if (e.Has("ballistic")) g.Ammo = Interp(e["ballistic"], env, here);
					if (e.Has("stadia")) g.Stadia = Num(e["stadia"], env, here + ".stadia");
					var s = style.With(null, this, env, here);
					if (e.Has("units")) s.K = Units(e["units"], here + ".units");
					s = s.With(e["style"] as JObj, this, env, here + ".style");
					foreach (var c in List(e["elements"], here + ".elements")) Expand(c, env, s, g, g.Children, here + ":" + g.Name);
					into.Add(g);
					return;
				}
				case "line":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "line" }).ToArray(), here);
					var v = Floats(e["line"], env, here);
					if (v.Length != 4) throw new FormatException($"{Source}: {here}: 'line' needs [x, y, deg, len]");
					var s = Own();
					Add(new Line { X = v[0] * K, Y = Ys(v[1]), Deg = v[2], Len = v[3] * K, W = s.W, Visual = s.Visual, Light = s.Light });
					return;
				}
				case "seg":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "seg" }).ToArray(), here);
					var v = Floats(e["seg"], env, here);
					if (v.Length != 4) throw new FormatException($"{Source}: {here}: 'seg' needs [x1, y1, x2, y2]");
					var s = Own();
					Seg(s, v[0] * K, Ys(v[1]), v[2] * K, Ys(v[3]));
					return;
				}
				case "poly":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "poly", "closed" }).ToArray(), here);
					var pts = List(e["poly"], here).Select((p, i) => Floats(p, env, here + "[" + i + "]")).ToList();
					if (e["closed"] is bool c && c && pts.Count > 2) pts.Add(pts[0]);
					var s = Own();
					for (int i = 0; i + 1 < pts.Count; i++) Seg(s, pts[i][0] * K, Ys(pts[i][1]), pts[i + 1][0] * K, Ys(pts[i + 1][1]));
					return;
				}
				case "dash":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "dash", "on", "off" }).ToArray(), here);
					var v = Floats(e["dash"], env, here);
					double on = Num(e["on"], env, here + ".on"), off = Num(e["off"], env, here + ".off");
					double len = Math.Sqrt((v[2] - v[0]) * (v[2] - v[0]) + (v[3] - v[1]) * (v[3] - v[1]));
					if (on <= 0 || off < 0 || len <= 0) throw new FormatException($"{Source}: {here}: dash needs on > 0, off >= 0 and a length");
					var s = Own();
					for (double t = 0; t < len - 1e-9; t += on + off)
					{
						double t2 = Math.Min(t + on, len), a = t / len, b = t2 / len;
						Seg(s, (v[0] + (v[2] - v[0]) * a) * K, Ys(v[1] + (v[3] - v[1]) * a), (v[0] + (v[2] - v[0]) * b) * K, Ys(v[1] + (v[3] - v[1]) * b));
					}
					return;
				}
				case "arc":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "arc", "segments" }).ToArray(), here);
					var v = Floats(e["arc"], env, here);
					if (v.Length != 5) throw new FormatException($"{Source}: {here}: 'arc' needs [cx, cy, r, from deg, to deg]");
					int n = e.Has("segments") ? (int)Num(e["segments"], env, here) : Math.Max(1, (int)Math.Ceiling(Math.Abs(v[4] - v[3]) / 10));
					var s = Own();
					for (int i = 0; i < n; i++)
					{
						double a1 = (v[3] + (v[4] - v[3]) * i / n) * Math.PI / 180, a2 = (v[3] + (v[4] - v[3]) * (i + 1) / n) * Math.PI / 180;
						Seg(s, (v[0] + v[2] * Math.Cos(a1)) * K, Ys(v[1] + v[2] * Math.Sin(a1)), (v[0] + v[2] * Math.Cos(a2)) * K, Ys(v[1] + v[2] * Math.Sin(a2)));
					}
					return;
				}
				case "circle":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "circle", "segments" }).ToArray(), here);
					var v = Floats(e["circle"], env, here);
					if (v.Length != 3) throw new FormatException($"{Source}: {here}: 'circle' needs [cx, cy, r]");
					var s = Own();
					into.Add(new Circle { X = v[0] * K, Y = Ys(v[1]), R = v[2] * K, W = s.W, Segments = e.Has("segments") ? (int)Num(e["segments"], env, here) : 32, Visual = s.Visual, Light = s.Light, Path = here });
					return;
				}
				case "text":
				{
					Unknown(e, new[] { "text", "at", "size", "visual", "light", "note" }, here);
					var (x, y) = At(e["at"], here + ".at");
					var s = Own();
					into.Add(new Text { Value = Interp(e["text"], env, here), X = x, Y = y, Size = e.Has("size") ? Num(e["size"], env, here) : 10, Visual = s.Visual, Light = s.Light, Path = here });
					return;
				}
				case "glyph":
				{
					Unknown(e, StrokeKeys.Concat(new[] { "glyph", "at", "h" }).ToArray(), here);
					var (cx, cy) = At(e["at"], here + ".at");
					double h = Num(e["h"], env, here + ".h") * K;
					string letter = Interp(e["glyph"], env, here);
					var strokes = Glyph(letter, cx, cy, h);
					if (strokes == null) { Problems.Add($"{here}: no stroke glyph for '{letter}' (known: {GlyphLetters})"); return; }
					var s = Own();
					foreach (var (x1, y1, x2, y2) in strokes) Seg(s, x1, y1, x2, y2);
					return;
				}
				case "for":
				{
					Unknown(e, new[] { "for", "in", "do", "note" }, here);
					var names = e["for"] is List<object> l ? l.Select(o => (string)o).ToArray() : new[] { (string)e["for"] };
					var items = new List<object>();
					if (e["in"] is List<object> lit)
						foreach (var it in lit)
						{
							var v = it is List<object> tuple ? tuple.Select(t => Value(t, env, here)).ToList() : Value(it, env, here);
							if (names.Length == 1 && v is List<object> splice && !(it is List<object>)) items.AddRange(splice);
							else items.Add(v);
						}
					else if (Value(e["in"], env, here) is List<object> ev) items.AddRange(ev);
					else throw new FormatException($"{Source}: {here}: 'in' must be a list or a range (a .. b step c)");
					int i = 0;
					foreach (var it in items)
					{
						var scope = new Env(env);
						if (names.Length == 1) scope.Set(names[0], it);
						else
						{
							var t = it as List<object>;
							if (t == null || t.Count != names.Length) throw new FormatException($"{Source}: {here}: item {i} does not match ({string.Join(", ", names)})");
							for (int k = 0; k < names.Length; k++) scope.Set(names[k], t[k]);
						}
						foreach (var c in List(e["do"], here + ".do")) Expand(c, scope, style, parent, into, here + "#" + i);
						i++;
					}
					return;
				}
				case "if":
				{
					Unknown(e, new[] { "if", "then", "else", "note" }, here);
					bool cond = Truth(Value(e["if"], env, here));
					var branch = cond ? e["then"] : e["else"];
					if (branch != null) foreach (var c in List(branch, here)) Expand(c, env, style, parent, into, here + (cond ? ".then" : ".else"));
					return;
				}
				case "use":
				{
					Unknown(e, new[] { "use", "args", "note" }, here);
					string name = (string)e["use"];
					if (!_defs.TryGetValue(name, out var def)) throw new FormatException($"{Source}: {here}: no def '{name}'");
					Unknown(def, new[] { "params", "elements", "note" }, "defs." + name);
					var ps = List(def["params"] ?? new List<object>(), "defs." + name + ".params").Select(o => (string)o).ToArray();
					var args = e["args"] as JObj ?? new JObj(Enumerable.Empty<KeyValuePair<string, object>>());
					foreach (var kv in args.Items) if (!ps.Contains(kv.Key)) Problems.Add($"{here}: '{name}' has no parameter '{kv.Key}'");
					var scope = new Env(_globals);
					foreach (var p in ps)
					{
						if (!args.Has(p)) throw new FormatException($"{Source}: {here}: '{name}' needs '{p}'");
						scope.Set(p, Value(args[p], env, here + "." + p));
					}
					foreach (var c in List(def["elements"], "defs." + name)) Expand(c, scope, style, parent, into, here + ":" + name);
					return;
				}
			}
		}

		void ScreenExpand(object raw, Env env, List<ScreenItem> into, string path)
		{
			if (!(raw is JObj e)) { Problems.Add($"{path}: item is not an object"); return; }
			string here = path + "[" + into.Count + "]";
			if (e.Has("for") || e.Has("if")) { Problems.Add($"{here}: for / if are not supported on the screen"); return; }
			string kind = new[] { "rect", "seg", "readout", "lamp", "text" }.FirstOrDefault(e.Has);
			if (kind == null) { Problems.Add($"{here}: no screen item kind (rect, seg, readout, lamp, text)"); return; }
			Unknown(e, new[] { kind, "at", "size", "text_size", "value", "anchor", "color", "full_width", "w", "font", "align", "note" }, here);
			var it = new ScreenItem { Kind = kind, Path = here };
			if (e.Has("anchor")) it.Anchor = Interp(e["anchor"], env, here);
			if (it.Anchor != "centre" && it.Anchor != "bottom" && it.Anchor != "top") Problems.Add($"{here}: anchor '{it.Anchor}' (centre, bottom, top)");
			if (e.Has("color")) it.Color = Floats(e["color"], env, here + ".color").Select(f => f).ToArray();
			if (it.Color.Length == 3) it.Color = it.Color.Concat(new[] { 1f }).ToArray();
			if (e.Has("font")) it.Font = Interp(e["font"], env, here);
			if (e.Has("align")) it.Align = Interp(e["align"], env, here);
			it.FullWidth = e["full_width"] is bool fw && fw;
			if (kind == "seg")
			{
				var v = Floats(e["seg"], env, here);
				double dx = v[2] - v[0], dy = v[3] - v[1];
				it.X = (v[0] + v[2]) / 2; it.Y = (v[1] + v[3]) / 2; it.W = Math.Sqrt(dx * dx + dy * dy); it.Deg = Math.Atan2(dy, dx) * 180 / Math.PI;
				it.H = e.Has("w") ? Num(e["w"], env, here + ".w") : 0.25;
				it.Name = "seg";
			}
			else
			{
				var p = Floats(e["at"] ?? new List<object> { 0.0, 0.0 }, env, here + ".at");
				it.X = p[0]; it.Y = p[1];
				it.Name = Interp(e[kind], env, here);
				var sz = e["size"];
				if (sz is List<object>) { var s = Floats(sz, env, here + ".size"); it.W = s[0]; it.H = s[1]; }
				else if (sz != null) it.Size = Num(sz, env, here + ".size");
				if (e.Has("text_size")) it.Size = Num(e["text_size"], env, here + ".text_size");
				if (kind == "text") { it.Value = it.Name; it.Name = "text"; }
				if (kind == "readout") it.Value = e.Has("value") ? Interp(e["value"], env, here) : "";
			}
			into.Add(it);
		}

		internal const string GlyphLetters = "П ш Б Г Л Н";
		internal static List<(double, double, double, double)> Glyph(string letter, double cx, double cy, double h)
		{
			double w = h * 0.6, l = cx - w / 2, r = cx + w / 2, b = cy - h / 2, t = cy + h / 2;
			switch (letter)
			{
				case "П": return new List<(double, double, double, double)> { (l, b, l, t), (r, b, r, t), (l, t, r, t) };
				case "ш": return new List<(double, double, double, double)> { (l, b, l, t), (cx, b, cx, t), (r, b, r, t), (l, b, r, b) };
				case "Б": return new List<(double, double, double, double)> { (l, b, l, t), (l, t, r, t), (l, cy, r, cy), (l, b, r, b), (r, b, r, cy) };
				case "Г": return new List<(double, double, double, double)> { (l, b, l, t), (l, t, r, t) };
				case "Л": return new List<(double, double, double, double)> { (l, b, cx, t), (cx, t, r, b) };
				case "Н": return new List<(double, double, double, double)> { (l, b, l, t), (r, b, r, t), (l, cy, r, cy) };
			}
			return null;
		}

		void Unknown(JObj o, string[] allowed, string where)
		{
			foreach (var kv in o.Items) if (!allowed.Contains(kv.Key)) Problems.Add($"{where}: unknown key '{kv.Key}'");
		}

		List<object> List(object v, string where) => v as List<object> ?? throw new FormatException($"{Source}: {where}: expected a list");

		object Value(object v, Env env, string where)
		{
			switch (v)
			{
				case string s: return Expr.Parse(s, $"{Source}: {where}").Eval(env);
				case List<object> l: return l.Select(x => Value(x, env, where)).ToList();
				default: return v;
			}
		}

		double Num(object v, Env env, string where) => ToNum(Value(v, env, where), $"{Source}: {where}");

		float[] Floats(object v, Env env, string where)
		{
			var o = Value(v, env, where);
			if (!(o is List<object> l)) throw new FormatException($"{Source}: {where}: expected a list of numbers");
			return l.Select(x => (float)ToNum(x, $"{Source}: {where}")).ToArray();
		}

		string Interp(object v, Env env, string where)
		{
			if (!(v is string s)) return v is double d ? Fmt(d) : v?.ToString();
			var sb = new StringBuilder();
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] == '{')
				{
					int j = s.IndexOf('}', i);
					if (j < 0) throw new FormatException($"{Source}: {where}: unclosed '{{' in \"{s}\"");
					var r = Expr.Parse(s.Substring(i + 1, j - i - 1), $"{Source}: {where}").Eval(env);
					sb.Append(r is double d ? Fmt(d) : r is bool b ? (b ? "1" : "0") : r?.ToString());
					i = j;
				}
				else sb.Append(s[i]);
			}
			return sb.ToString();
		}

		internal static string Fmt(double d)
		{
			if (Math.Abs(d - Math.Round(d)) < 1e-9) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
			return d.ToString("0.######", CultureInfo.InvariantCulture);
		}

		static double ToNum(object v, string where)
		{
			switch (v)
			{
				case double d: return d;
				case bool b: return b ? 1 : 0;
			}
			throw new FormatException($"{where}: expected a number, got {(v == null ? "nothing" : v is List<object> ? "a list" : "'" + v + "'")}");
		}

		static bool Truth(object v) => v is bool b ? b : v is double d ? d != 0 : v != null;

		internal sealed class Env
		{
			readonly Env _up;
			readonly Dictionary<string, object> _v = new Dictionary<string, object>();
			internal Env(Env up) { _up = up; }

			internal void Set(string k, object v) => _v[k] = v;
			internal bool TryGet(string k, out object v)
			{
				for (var e = this; e != null; e = e._up) if (e._v.TryGetValue(k, out v)) return true;
				v = null;
				return false;
			}
		}

		sealed class Fn
		{
			internal string[] Params;
			internal Expr Body;
			internal Env Closure;
			internal Func<double[], double> Builtin;
		}

		static readonly Dictionary<string, Fn> Builtins = new Dictionary<string, Fn>
		{
			{ "sqrt", new Fn { Builtin = a => Math.Sqrt(a[0]) } },
			{ "abs", new Fn { Builtin = a => Math.Abs(a[0]) } },
			{ "sin", new Fn { Builtin = a => Math.Sin(a[0]) } },
			{ "cos", new Fn { Builtin = a => Math.Cos(a[0]) } },
			{ "tan", new Fn { Builtin = a => Math.Tan(a[0]) } },
			{ "atan", new Fn { Builtin = a => Math.Atan(a[0]) } },
			{ "atan2", new Fn { Builtin = a => Math.Atan2(a[0], a[1]) } },
			{ "deg", new Fn { Builtin = a => a[0] * 180 / Math.PI } },
			{ "rad", new Fn { Builtin = a => a[0] * Math.PI / 180 } },
			{ "floor", new Fn { Builtin = a => Math.Floor(a[0]) } },
			{ "ceil", new Fn { Builtin = a => Math.Ceiling(a[0]) } },
			{ "round", new Fn { Builtin = a => Math.Round(a[0], MidpointRounding.AwayFromZero) } },
			{ "min", new Fn { Builtin = a => a.Min() } },
			{ "max", new Fn { Builtin = a => a.Max() } },
		};

		internal abstract class Expr
		{
			internal abstract object Eval(Env env);

			static readonly Dictionary<string, Expr> Cache = new Dictionary<string, Expr>();

			internal static Expr Parse(string s, string where)
			{
				if (Cache.TryGetValue(s, out var e)) return e;
				var p = new Parser(s, where);
				e = p.Top();
				p.End();
				return Cache[s] = e;
			}

			internal sealed class Lit : Expr { internal object V; internal override object Eval(Env env) => V; }

			internal sealed class Name : Expr
			{
				internal string N, Where;
				internal override object Eval(Env env)
				{
					if (env.TryGet(N, out var v)) return v;
					if (Builtins.TryGetValue(N, out var f)) return f;
					throw new FormatException($"{Where}: unknown name '{N}'");
				}
			}

			internal sealed class ListE : Expr { internal List<Expr> Items; internal override object Eval(Env env) => Items.Select(i => i.Eval(env)).ToList(); }

			internal sealed class Lambda : Expr { internal string[] Params; internal Expr Body; internal override object Eval(Env env) => new Fn { Params = Params, Body = Body, Closure = env }; }

			internal sealed class Call : Expr
			{
				internal Expr F; internal List<Expr> Args; internal string Where;
				internal override object Eval(Env env)
				{
					var f = F.Eval(env) as Fn ?? throw new FormatException($"{Where}: not a function");
					var a = Args.Select(x => x.Eval(env)).ToList();
					if (f.Builtin != null) return f.Builtin(a.Select(x => ToNum(x, Where)).ToArray());
					if (a.Count != f.Params.Length) throw new FormatException($"{Where}: {f.Params.Length} argument(s) expected, {a.Count} given");
					var scope = new Env(f.Closure);
					for (int i = 0; i < a.Count; i++) scope.Set(f.Params[i], a[i]);
					return f.Body.Eval(scope);
				}
			}

			internal sealed class Index : Expr
			{
				internal Expr L, I; internal string Where;
				internal override object Eval(Env env)
				{
					var l = L.Eval(env) as List<object> ?? throw new FormatException($"{Where}: indexing something that is not a list");
					int i = (int)ToNum(I.Eval(env), Where);
					if (i < 0) i += l.Count;
					if (i < 0 || i >= l.Count) throw new FormatException($"{Where}: index {i} outside the list (length {l.Count})");
					return l[i];
				}
			}

			internal sealed class Cond : Expr
			{
				internal Expr C, A, B;
				internal override object Eval(Env env) => Truth(C.Eval(env)) ? A.Eval(env) : B.Eval(env);
			}

			internal sealed class Range : Expr
			{
				internal Expr A, B, Step; internal string Where;
				internal override object Eval(Env env)
				{
					double a = ToNum(A.Eval(env), Where), b = ToNum(B.Eval(env), Where), st = Step != null ? ToNum(Step.Eval(env), Where) : 1;
					if (st == 0 || (b - a) / st < 0) throw new FormatException($"{Where}: range {Fmt(a)} .. {Fmt(b)} step {Fmt(st)} never ends");
					var r = new List<object>();
					int n = (int)Math.Floor((b - a) / st + 1e-9);
					for (int i = 0; i <= n; i++) r.Add(a + i * st);
					return r;
				}
			}

			internal sealed class Un : Expr
			{
				internal char Op; internal Expr A; internal string Where;
				internal override object Eval(Env env)
				{
					var v = A.Eval(env);
					return Op == '!' ? (object)!Truth(v) : Op == '-' ? -ToNum(v, Where) : ToNum(v, Where);
				}
			}

			internal sealed class Bin : Expr
			{
				internal string Op; internal Expr A, B; internal string Where;
				internal override object Eval(Env env)
				{
					if (Op == "&&") return Truth(A.Eval(env)) && Truth(B.Eval(env));
					if (Op == "||") return Truth(A.Eval(env)) || Truth(B.Eval(env));
					object a = A.Eval(env), b = B.Eval(env);
					if (Op == "==" || Op == "!=")
					{
						bool eq = a is string || b is string ? Equals(a as string, b as string) : Math.Abs(ToNum(a, Where) - ToNum(b, Where)) < 1e-9;
						return Op == "==" ? eq : !eq;
					}
					if (Op == "+" && a is List<object> la && b is List<object> lb) return la.Concat(lb).ToList();
					if (Op == "+" && (a is string || b is string)) return (a is double da ? Fmt(da) : a?.ToString()) + (b is double db ? Fmt(db) : b?.ToString());
					double x = ToNum(a, Where), y = ToNum(b, Where);
					switch (Op)
					{
						case "+": return x + y;
						case "-": return x - y;
						case "*": return x * y;
						case "/": return x / y;
						case "%": return x % y;
						case "<": return x < y;
						case "<=": return x <= y;
						case ">": return x > y;
						case ">=": return x >= y;
					}
					throw new FormatException($"{Where}: operator {Op}");
				}
			}

			sealed class Parser
			{
				readonly List<string> _t = new List<string>();
				readonly string _where;
				int _i;

				internal Parser(string s, string where)
				{
					_where = $"{where}: \"{s}\"";
					int i = 0;
					while (i < s.Length)
					{
						char c = s[i];
						if (char.IsWhiteSpace(c)) { i++; continue; }
						if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
						{
							int j = i;
							while (j < s.Length && char.IsDigit(s[j])) j++;
							if (j < s.Length && s[j] == '.' && !(j + 1 < s.Length && s[j + 1] == '.')) { j++; while (j < s.Length && char.IsDigit(s[j])) j++; }
							if (j < s.Length && (s[j] == 'e' || s[j] == 'E') && j + 1 < s.Length && (char.IsDigit(s[j + 1]) || ((s[j + 1] == '-' || s[j + 1] == '+') && j + 2 < s.Length && char.IsDigit(s[j + 2]))))
							{ j += 2; while (j < s.Length && char.IsDigit(s[j])) j++; }
							_t.Add(s.Substring(i, j - i)); i = j; continue;
						}
						if (char.IsLetter(c) || c == '_')
						{
							int j = i;
							while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
							_t.Add(s.Substring(i, j - i)); i = j; continue;
						}
						if (c == '\'')
						{
							int j = s.IndexOf('\'', i + 1);
							if (j < 0) throw new FormatException($"{_where}: unclosed string");
							_t.Add(s.Substring(i, j - i + 1)); i = j + 1; continue;
						}
						string two = i + 1 < s.Length ? s.Substring(i, 2) : null;
						if (two != null && new[] { "==", "!=", "<=", ">=", "&&", "||", "..", "=>" }.Contains(two)) { _t.Add(two); i += 2; continue; }
						if ("+-*/%<>!?:()[],".IndexOf(c) >= 0) { _t.Add(c.ToString()); i++; continue; }
						throw new FormatException($"{_where}: unexpected '{c}'");
					}
				}

				string Peek(int k = 0) => _i + k < _t.Count ? _t[_i + k] : null;
				string Next() => _i < _t.Count ? _t[_i++] : throw new FormatException($"{_where}: unexpected end");
				void Want(string s) { if (Next() != s) throw new FormatException($"{_where}: '{s}' expected"); }

				internal void End() { if (_i != _t.Count) throw new FormatException($"{_where}: unexpected '{_t[_i]}'"); }

				static bool IsName(string t) => t != null && (char.IsLetter(t[0]) || t[0] == '_');

				internal Expr Top()
				{
					if (IsName(Peek()) && Peek(1) == "=>") { var p = Next(); Next(); return new Lambda { Params = new[] { p }, Body = Top() }; }
					if (Peek() == "(")
					{
						if (Peek(1) == ")" && Peek(2) == "=>") { _i += 3; return new Lambda { Params = new string[0], Body = Top() }; }
						var ps = new List<string>();
						for (int k = 1; IsName(Peek(k)); k += 2)
						{
							ps.Add(Peek(k));
							if (Peek(k + 1) == ",") continue;
							if (Peek(k + 1) == ")" && Peek(k + 2) == "=>") { _i += k + 3; return new Lambda { Params = ps.ToArray(), Body = Top() }; }
							break;
						}
					}
					return Ternary();
				}

				Expr Ternary()
				{
					var c = Or();
					if (Peek() != "?") return c;
					Next();
					var a = Top();
					Want(":");
					return new Cond { C = c, A = a, B = Top() };
				}

				Expr Or() { var a = And(); while (Peek() == "||") { Next(); a = new Bin { Op = "||", A = a, B = And(), Where = _where }; } return a; }

				Expr And() { var a = Eq(); while (Peek() == "&&") { Next(); a = new Bin { Op = "&&", A = a, B = Eq(), Where = _where }; } return a; }

				Expr Eq() { var a = Rel(); while (Peek() == "==" || Peek() == "!=") { var op = Next(); a = new Bin { Op = op, A = a, B = Rel(), Where = _where }; } return a; }

				Expr Rel() { var a = Rng(); while (Peek() == "<" || Peek() == "<=" || Peek() == ">" || Peek() == ">=") { var op = Next(); a = new Bin { Op = op, A = a, B = Rng(), Where = _where }; } return a; }

				Expr Rng()
				{
					var a = Add();
					if (Peek() != "..") return a;
					Next();
					var r = new Range { A = a, B = Add(), Where = _where };
					if (Peek() == "step") { Next(); r.Step = Add(); }
					return r;
				}

				Expr Add() { var a = Mul(); while (Peek() == "+" || Peek() == "-") { var op = Next(); a = new Bin { Op = op, A = a, B = Mul(), Where = _where }; } return a; }

				Expr Mul() { var a = Unary(); while (Peek() == "*" || Peek() == "/" || Peek() == "%") { var op = Next(); a = new Bin { Op = op, A = a, B = Unary(), Where = _where }; } return a; }

				Expr Unary()
				{
					if (Peek() == "-" || Peek() == "!" || Peek() == "+") { var op = Next()[0]; return new Un { Op = op, A = Unary(), Where = _where }; }
					return Postfix();
				}

				Expr Postfix()
				{
					var a = Primary();
					while (true)
					{
						if (Peek() == "(")
						{
							Next();
							var args = new List<Expr>();
							if (Peek() != ")") { args.Add(Top()); while (Peek() == ",") { Next(); args.Add(Top()); } }
							Want(")");
							a = new Call { F = a, Args = args, Where = _where };
						}
						else if (Peek() == "[") { Next(); var i = Top(); Want("]"); a = new Index { L = a, I = i, Where = _where }; }
						else return a;
					}
				}

				Expr Primary()
				{
					var t = Next();
					if (char.IsDigit(t[0]) || t[0] == '.') return new Lit { V = double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture) };
					if (t[0] == '\'') return new Lit { V = t.Substring(1, t.Length - 2) };
					if (t == "true" || t == "false") return new Lit { V = t == "true" };
					if (IsName(t)) return new Name { N = t, Where = _where };
					if (t == "(") { var e = Top(); Want(")"); return e; }
					if (t == "[")
					{
						var items = new List<Expr>();
						if (Peek() != "]") { items.Add(Top()); while (Peek() == ",") { Next(); items.Add(Top()); } }
						Want("]");
						return new ListE { Items = items };
					}
					throw new FormatException($"{_where}: unexpected '{t}'");
				}
			}
		}

		internal sealed class JObj
		{
			internal readonly List<KeyValuePair<string, object>> Items;
			internal JObj(IEnumerable<KeyValuePair<string, object>> items) { Items = items.ToList(); }

			internal object this[string k] { get { foreach (var kv in Items) if (kv.Key == k) return kv.Value; return null; } }

			internal bool Has(string k) => Items.Any(kv => kv.Key == k);
			internal string Str(string k) => this[k] as string;
		}

		sealed class Json
		{
			readonly string _s, _src;
			int _i;
			internal Json(string s, string src) { _s = s; _src = src; }

			FormatException Err(string what)
			{
				int line = 1 + _s.Take(Math.Min(_i, _s.Length)).Count(c => c == '\n');
				return new FormatException($"{_src}: line {line}: {what}");
			}

			void Ws()
			{
				while (_i < _s.Length)
				{
					if (char.IsWhiteSpace(_s[_i])) _i++;
					else if (_s[_i] == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (_i < _s.Length && _s[_i] != '\n') _i++; }
					else if (_s[_i] == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*') { int j = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal); _i = j < 0 ? _s.Length : j + 2; }
					else break;
				}
			}

			internal object Read()
			{
				var v = Val();
				Ws();
				if (_i != _s.Length) throw Err("text after the end");
				return v;
			}

			object Val()
			{
				Ws();
				if (_i >= _s.Length) throw Err("unexpected end");
				char c = _s[_i];
				if (c == '{')
				{
					_i++;
					var items = new List<KeyValuePair<string, object>>();
					Ws();
					if (_s[_i] == '}') { _i++; return new JObj(items); }
					while (true)
					{
						Ws();
						if (_s[_i] == '}') { _i++; return new JObj(items); }
						string k = Str();
						Ws();
						if (_s[_i++] != ':') throw Err("':' expected");
						if (items.Any(kv => kv.Key == k)) throw Err($"key '{k}' twice");
						items.Add(new KeyValuePair<string, object>(k, Val()));
						Ws();
						if (_s[_i] == ',') { _i++; continue; }
						if (_s[_i] == '}') { _i++; return new JObj(items); }
						throw Err("',' or '}' expected");
					}
				}
				if (c == '[')
				{
					_i++;
					var l = new List<object>();
					while (true)
					{
						Ws();
						if (_s[_i] == ']') { _i++; return l; }
						l.Add(Val());
						Ws();
						if (_s[_i] == ',') { _i++; continue; }
						if (_s[_i] == ']') { _i++; return l; }
						throw Err("',' or ']' expected");
					}
				}
				if (c == '"') return Str();
				if (_s.Substring(_i).StartsWith("true")) { _i += 4; return true; }
				if (_s.Substring(_i).StartsWith("false")) { _i += 5; return false; }
				if (_s.Substring(_i).StartsWith("null")) { _i += 4; return null; }
				int j = _i;
				while (j < _s.Length && "+-0123456789.eE".IndexOf(_s[j]) >= 0) j++;
				if (j == _i) throw Err($"unexpected '{c}'");
				var num = double.Parse(_s.Substring(_i, j - _i), NumberStyles.Float, CultureInfo.InvariantCulture);
				_i = j;
				return num;
			}

			string Str()
			{
				if (_s[_i] != '"') throw Err("string expected");
				var sb = new StringBuilder();
				_i++;
				while (_s[_i] != '"')
				{
					if (_s[_i] == '\\')
					{
						char e = _s[++_i];
						if (e == 'n') sb.Append('\n');
						else if (e == 't') sb.Append('\t');
						else if (e == 'u') { sb.Append((char)Convert.ToInt32(_s.Substring(_i + 1, 4), 16)); _i += 4; }
						else sb.Append(e);
					}
					else sb.Append(_s[_i]);
					_i++;
				}
				_i++;
				return sb.ToString();
			}
		}
	}
}
