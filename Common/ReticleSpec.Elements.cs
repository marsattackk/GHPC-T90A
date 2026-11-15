using System;
using System.Collections.Generic;
using System.Linq;

namespace T90A.Common
{
	internal sealed partial class ReticleSpec
	{
		static readonly string[] Kinds = { "group", "line", "seg", "poly", "dash", "arc", "circle", "text", "glyph", "for", "if", "use" };
		static readonly string[] StrokeKeys = { "w", "visual", "light", "note" };

		void Expand(object raw, Env env, Style style, Group parent, List<Node> into, string path)
		{
			if (!(raw is JObj element))
			{
				Problems.Add($"{path}: element is not an object");
				return;
			}
			string kind = Kinds.FirstOrDefault(element.Has);
			if (kind == null)
			{
				Problems.Add($"{path}: no element kind (one of {string.Join(", ", Kinds)}) in {{{string.Join(", ", element.Items.Select(p => p.Key))}}}");
				return;
			}
			string here = path + "/" + kind + "[" + into.Count + "]";
			bool rangedParent = parent != null && parent.Ranged;
			double scale = style.K;
			// y is range in m here, and GHPC puts +range below the origin.
			(double, double) At(object position, string label)
			{
				var coordinates = Floats(position ?? new List<object> { 0.0, 0.0 }, env, label);
				if (coordinates.Length != 2) throw new FormatException($"{Source}: {label}: 'at' needs [x, y]");
				return (coordinates[0] * scale, rangedParent ? coordinates[1] : coordinates[1] * scale);
			}
			Style Own()
				=> style.With(
					new JObj(element.Items.Where(p => p.Key == "w" || p.Key == "visual" || p.Key == "light")),
					this,
					env,
					here);
			void Add(Line line)
			{
				line.Path = here;
				into.Add(line);
			}
			void Seg(Style strokeStyle, double startX, double startY, double endX, double endY)
			{
				double deltaX = endX - startX, deltaY = endY - startY;
				Add(new Line
				{
					X = (startX + endX) / 2,
					Y = (startY + endY) / 2,
					Deg = Math.Atan2(deltaY, deltaX) * 180 / Math.PI,
					Len = Math.Sqrt(deltaX * deltaX + deltaY * deltaY),
					W = strokeStyle.W,
					Visual = strokeStyle.Visual,
					Light = strokeStyle.Light
				});
			}
			double Ys(double y) => rangedParent ? y : y * scale;

			switch (kind)
			{
				case "group":
				{
					Unknown(element, new[] { "group", "at", "align", "ballistic", "stadia", "units", "style", "elements", "note" }, here);
					var (x, y) = At(element["at"], here + ".at");
					var groupNode = new Group { Name = Interp(element["group"], env, here), X = x, Y = y, Path = here };
					if (element.Has("align")) groupNode.Align = Interp(element["align"], env, here);
					if (element.Has("ballistic")) groupNode.Ammo = Interp(element["ballistic"], env, here);
					if (element.Has("stadia")) groupNode.Stadia = Num(element["stadia"], env, here + ".stadia");
					var groupStyle = style.With(null, this, env, here);
					if (element.Has("units")) groupStyle.K = Units(element["units"], here + ".units");
					groupStyle = groupStyle.With(element["style"] as JObj, this, env, here + ".style");
					foreach (var child in List(element["elements"], here + ".elements"))
						Expand(child, env, groupStyle, groupNode, groupNode.Children, here + ":" + groupNode.Name);
					into.Add(groupNode);
					return;
				}
				case "line":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "line" }).ToArray(), here);
					var numbers = Floats(element["line"], env, here);
					if (numbers.Length != 4) throw new FormatException($"{Source}: {here}: 'line' needs [x, y, deg, len]");
					var strokeStyle = Own();
					Add(new Line
					{
						X = numbers[0] * scale,
						Y = Ys(numbers[1]),
						Deg = numbers[2],
						Len = numbers[3] * scale,
						W = strokeStyle.W,
						Visual = strokeStyle.Visual,
						Light = strokeStyle.Light
					});
					return;
				}
				case "seg":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "seg" }).ToArray(), here);
					var numbers = Floats(element["seg"], env, here);
					if (numbers.Length != 4) throw new FormatException($"{Source}: {here}: 'seg' needs [x1, y1, x2, y2]");
					var strokeStyle = Own();
					Seg(strokeStyle, numbers[0] * scale, Ys(numbers[1]), numbers[2] * scale, Ys(numbers[3]));
					return;
				}
				case "poly":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "poly", "closed" }).ToArray(), here);
					var pts = List(element["poly"], here).Select((p, i) => Floats(p, env, here + "[" + i + "]")).ToList();
					if (element["closed"] is bool closed && closed && pts.Count > 2) pts.Add(pts[0]);
					var strokeStyle = Own();
					for (int i = 0;
						i + 1 < pts.Count;
						i++) Seg(strokeStyle, pts[i][0] * scale, Ys(pts[i][1]), pts[i + 1][0] * scale, Ys(pts[i + 1][1]));
					return;
				}
				case "dash":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "dash", "on", "off" }).ToArray(), here);
					var numbers = Floats(element["dash"], env, here);
					double dashLength = Num(element["on"], env, here + ".on"), off = Num(element["off"], env, here + ".off");
					double len = Math.Sqrt((numbers[2] - numbers[0]) * (numbers[2] - numbers[0])
						+ (numbers[3] - numbers[1]) * (numbers[3] - numbers[1]));
					if (dashLength <= 0 || off < 0 || len <= 0)
						throw new FormatException($"{Source}: {here}: dash needs on > 0, off >= 0 and a length");
					var strokeStyle = Own();
					for (double dashStart = 0; dashStart < len - 1e-9; dashStart += dashLength + off)
					{
						double dashEnd = Math.Min(
							dashStart + dashLength,
							len), startFraction = dashStart / len, endFraction = dashEnd / len;
						Seg(
							strokeStyle,
							(numbers[0] + (numbers[2] - numbers[0]) * startFraction) * scale,
							Ys(numbers[1] + (numbers[3] - numbers[1]) * startFraction),
							(numbers[0] + (numbers[2] - numbers[0]) * endFraction) * scale,
							Ys(numbers[1] + (numbers[3] - numbers[1]) * endFraction));
					}
					return;
				}
				case "arc":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "arc", "segments" }).ToArray(), here);
					var numbers = Floats(element["arc"], env, here);
					if (numbers.Length != 5) throw new FormatException($"{Source}: {here}: 'arc' needs [cx, cy, r, from deg, to deg]");
					int segmentCount = element.Has("segments")
						? (int)Num(element["segments"], env, here)
						: Math.Max(1, (int)Math.Ceiling(Math.Abs(numbers[4] - numbers[3]) / 10));
					var strokeStyle = Own();
					for (int i = 0; i < segmentCount; i++)
					{
						double startAngle = (numbers[3]
							+ (numbers[4] - numbers[3]) * i / segmentCount) * Math.PI / 180, endAngle = (numbers[3]
							+ (numbers[4] - numbers[3]) * (i + 1) / segmentCount) * Math.PI / 180;
						Seg(
							strokeStyle,
							(numbers[0] + numbers[2] * Math.Cos(startAngle)) * scale,
							Ys(numbers[1] + numbers[2] * Math.Sin(startAngle)),
							(numbers[0] + numbers[2] * Math.Cos(endAngle)) * scale,
							Ys(numbers[1] + numbers[2] * Math.Sin(endAngle)));
					}
					return;
				}
				case "circle":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "circle", "segments" }).ToArray(), here);
					var numbers = Floats(element["circle"], env, here);
					if (numbers.Length != 3) throw new FormatException($"{Source}: {here}: 'circle' needs [cx, cy, r]");
					var strokeStyle = Own();
					into.Add(new Circle
					{
						X = numbers[0] * scale,
						Y = Ys(numbers[1]),
						R = numbers[2] * scale,
						W = strokeStyle.W,
						Segments = element.Has("segments") ? (int)Num(element["segments"], env, here) : 32,
						Visual = strokeStyle.Visual,
						Light = strokeStyle.Light,
						Path = here
					});
					return;
				}
				case "text":
				{
					Unknown(element, new[] { "text", "at", "size", "visual", "light", "note" }, here);
					var (x, y) = At(element["at"], here + ".at");
					var strokeStyle = Own();
					into.Add(new Text
					{
						Value = Interp(element["text"], env, here),
						X = x,
						Y = y,
						Size = element.Has("size") ? Num(element["size"], env, here) : 10,
						Visual = strokeStyle.Visual,
						Light = strokeStyle.Light,
						Path = here
					});
					return;
				}
				case "glyph":
				{
					Unknown(element, StrokeKeys.Concat(new[] { "glyph", "at", "h" }).ToArray(), here);
					var (centerX, centerY) = At(element["at"], here + ".at");
					double height = Num(element["h"], env, here + ".h") * scale;
					string letter = Interp(element["glyph"], env, here);
					var strokes = Glyph(letter, centerX, centerY, height);
					if (strokes == null)
					{
						Problems.Add($"{here}: no stroke glyph for '{letter}' (known: {GlyphLetters})");
						return;
					}
					var strokeStyle = Own();
					foreach (var (startX, startY, endX, endY) in strokes) Seg(strokeStyle, startX, startY, endX, endY);
					return;
				}
				case "for":
				{
					Unknown(element, new[] { "for", "in", "do", "note" }, here);
					var names = element["for"] is List<object> nameList
						? nameList.Select(n => (string)n).ToArray()
						: new[] { (string)element["for"] };
					var items = new List<object>();
					if (element["in"] is List<object> lit)
						foreach (var item in lit)
						{
							var evaluated = item is List<object> tuple
								? tuple.Select(p => Value(p, env, here)).ToList()
								: Value(item, env, here);
							if (names.Length == 1 && evaluated is List<object> splice && !(item is List<object>)) items.AddRange(splice);
							else items.Add(evaluated);
						}
					else if (Value(element["in"], env, here) is List<object> rangeItems) items.AddRange(rangeItems);
					else throw new FormatException($"{Source}: {here}: 'in' must be a list or a range (a .. b step c)");
					int i = 0;
					foreach (var item in items)
					{
						var scope = new Env(env);
						if (names.Length == 1) scope.Set(names[0], item);
						else
						{
							var tupleItems = item as List<object>;
							if (tupleItems == null || tupleItems.Count != names.Length)
								throw new FormatException($"{Source}: {here}: item {i} does not match ({string.Join(", ", names)})");
							for (int k = 0; k < names.Length; k++) scope.Set(names[k], tupleItems[k]);
						}
						foreach (var child in List(element["do"], here + ".do")) Expand(child, scope, style, parent, into, here + "#" + i);
						i++;
					}
					return;
				}
				case "if":
				{
					Unknown(element, new[] { "if", "then", "else", "note" }, here);
					bool cond = Truth(Value(element["if"], env, here));
					var branch = cond ? element["then"] : element["else"];
					if (branch != null)
						foreach (var child in List(branch, here))
							Expand(child, env, style, parent, into, here + (cond ? ".then" : ".else"));
					return;
				}
				case "use":
				{
					Unknown(element, new[] { "use", "args", "note" }, here);
					string name = (string)element["use"];
					if (!_defs.TryGetValue(name, out var def)) throw new FormatException($"{Source}: {here}: no def '{name}'");
					Unknown(def, new[] { "params", "elements", "note" }, "defs." + name);
					var parameterNames = List(def["params"] ?? new List<object>(), "defs." + name + ".params")
						.Select(n => (string)n)
						.ToArray();
					var args = element["args"] as JObj ?? new JObj(Enumerable.Empty<KeyValuePair<string, object>>());
					foreach (var pair in args.Items)
						if (!parameterNames.Contains(pair.Key)) Problems.Add($"{here}: '{name}' has no parameter '{pair.Key}'");
					var scope = new Env(_globals);
					foreach (var parameterName in parameterNames)
					{
						if (!args.Has(parameterName)) throw new FormatException($"{Source}: {here}: '{name}' needs '{parameterName}'");
						scope.Set(parameterName, Value(args[parameterName], env, here + "." + parameterName));
					}
					foreach (var child in List(def["elements"], "defs." + name))
						Expand(child, scope, style, parent, into, here + ":" + name);
					return;
				}
			}
		}

		void ScreenExpand(object raw, Env env, List<ScreenItem> into, string path)
		{
			if (!(raw is JObj element))
			{
				Problems.Add($"{path}: item is not an object");
				return;
			}
			string here = path + "[" + into.Count + "]";
			if (element.Has("for") || element.Has("if"))
			{
				Problems.Add($"{here}: for / if are not supported on the screen");
				return;
			}
			string kind = new[] { "rect", "seg", "readout", "lamp", "text" }.FirstOrDefault(element.Has);
			if (kind == null)
			{
				Problems.Add($"{here}: no screen item kind (rect, seg, readout, lamp, text)");
				return;
			}
			Unknown(
				element,
				new[] { kind, "at", "size", "text_size", "value", "anchor", "color", "full_width", "w", "font", "align", "note" },
				here);
			var item = new ScreenItem { Kind = kind, Path = here };
			if (element.Has("anchor")) item.Anchor = Interp(element["anchor"], env, here);
			if (item.Anchor != "centre" && item.Anchor != "bottom" && item.Anchor != "top")
				Problems.Add($"{here}: anchor '{item.Anchor}' (centre, bottom, top)");
			if (element.Has("color")) item.Color = Floats(element["color"], env, here + ".color").Select(c => c).ToArray();
			if (item.Color.Length == 3) item.Color = item.Color.Concat(new[] { 1f }).ToArray();
			if (element.Has("font")) item.Font = Interp(element["font"], env, here);
			if (element.Has("align")) item.Align = Interp(element["align"], env, here);
			item.FullWidth = element["full_width"] is bool fullWidth && fullWidth;
			if (kind == "seg")
			{
				var numbers = Floats(element["seg"], env, here);
				double deltaX = numbers[2] - numbers[0], deltaY = numbers[3] - numbers[1];
				item.X = (numbers[0] + numbers[2]) / 2;
				item.Y = (numbers[1] + numbers[3]) / 2;
				item.W = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
				item.Deg = Math.Atan2(deltaY, deltaX) * 180 / Math.PI;
				item.H = element.Has("w") ? Num(element["w"], env, here + ".w") : 0.25;
				item.Name = "seg";
			}
			else
			{
				var position = Floats(element["at"] ?? new List<object> { 0.0, 0.0 }, env, here + ".at");
				item.X = position[0];
				item.Y = position[1];
				item.Name = Interp(element[kind], env, here);
				var size = element["size"];
				var dimensions = size is List<object> ? Floats(size, env, here + ".size") : null;
				if (dimensions != null)
				{
					item.W = dimensions[0];
					item.H = dimensions[1];
				}
				else if (size != null) item.Size = Num(size, env, here + ".size");
				if (element.Has("text_size")) item.Size = Num(element["text_size"], env, here + ".text_size");
				if (kind == "text")
				{
					item.Value = item.Name;
					item.Name = "text";
				}
				if (kind == "readout") item.Value = element.Has("value") ? Interp(element["value"], env, here) : "";
			}
			into.Add(item);
		}

		// GHPC's reticle fonts have no Cyrillic.
		internal const string GlyphLetters = "П ш Б Г Л Н";
		internal static List<(double, double, double, double)> Glyph(string letter, double cx, double cy, double h)
		{
			double width = h * 0.6, left = cx - width / 2, right = cx + width / 2, bottom = cy - h / 2, top = cy + h / 2;
			switch (letter)
			{
				case "П": return new List<(double, double, double, double)>
				{
					(left, bottom, left, top),
					(right, bottom, right, top),
					(left, top, right, top)
				};
				case "ш": return new List<(double, double, double, double)>
				{
					(left, bottom, left, top),
					(cx, bottom, cx, top),
					(right, bottom, right, top),
					(left, bottom, right, bottom)
				};
				case "Б": return new List<(double, double, double, double)>
				{
					(left, bottom, left, top),
					(left, top, right, top),
					(left, cy, right, cy),
					(left, bottom, right, bottom),
					(right, bottom, right, cy)
				};
				case "Г": return new List<(double, double, double, double)> { (left, bottom, left, top), (left, top, right, top) };
				case "Л": return new List<(double, double, double, double)> { (left, bottom, cx, top), (cx, top, right, bottom) };
				case "Н": return new List<(double, double, double, double)>
				{
					(left, bottom, left, top),
					(right, bottom, right, top),
					(left, cy, right, cy)
				};
			}
			return null;
		}
	}
}
