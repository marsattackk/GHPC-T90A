using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace T90A.Common
{
	// A reticle file as a tree of groups, lines, circles and text, with expressions for the repeated parts.
	internal sealed partial class ReticleSpec
	{
		internal abstract class Node { internal string Path; }

		internal sealed class Group : Node
		{
			internal string Name, Align, Ammo;
			internal double X, Y, Stadia;
			internal readonly List<Node> Children = new List<Node>();
			internal bool Ranged => Ammo != null || Stadia > 0;
		}

		internal sealed class Line : Node
		{
			internal double X, Y, Deg, Len, W;
			internal string Visual, Light;
		}

		internal sealed class Circle : Node
		{
			internal double X, Y, R, W;
			internal int Segments;
			internal string Visual, Light;
		}

		internal sealed class Text : Node
		{
			internal string Value;
			internal double X, Y, Size;
			internal string Visual, Light;
		}

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
		IDictionary<string, double> _varOverrides;

		internal double Num(string name) => ToNum(_globals.Get(name), name);
		internal double[] Nums(string name) => ((List<object>)_globals.Get(name)).Select(i => ToNum(i, name)).ToArray();
		internal bool Has(string name) => _globals.TryGet(name, out _);

		internal static ReticleSpec FromResource(System.Reflection.Assembly asm, string resource, IDictionary<string, double> vars = null)
		{
			using (var stream = asm.GetManifestResourceStream(resource))
			{
				if (stream == null) throw new ArgumentException($"no embedded resource '{resource}'");
				using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8)) return Parse(reader.ReadToEnd(), resource, vars);
			}
		}

		internal static ReticleSpec Parse(string text, string source, IDictionary<string, double> vars = null)
		{
			var spec = new ReticleSpec { Source = source, _varOverrides = vars };
			var root = new Json(text, source).Read() as JObj ?? throw new FormatException(source + ": the top level must be an object");
			spec.Load(root);
			return spec;
		}

		static readonly string[] TopKeys =
		{
			"name",
			"fov",
			"plane",
			"units",
			"style",
			"lights",
			"vars",
			"defs",
			"elements",
			"screen",
			"note"
		};

		void Load(JObj root)
		{
			Unknown(root, TopKeys, "top level");
			Name = root.Str("name") ?? throw new FormatException(Source + ": no 'name'");
			Fov = root.Has("fov") ? ToNum(root["fov"], Source + ": fov") : 0;
			Plane = root.Str("plane") ?? "first";
			_globals.Set("Fov", Fov);
			_globals.Set("PI", Math.PI);
			if (root["vars"] is JObj vars)
				foreach (var pair in vars.Items)
					_globals.Set(
						pair.Key,
						_varOverrides != null && _varOverrides.TryGetValue(pair.Key, out var over)
							? over
							: Value(pair.Value, _globals, "vars." + pair.Key));
			if (root["defs"] is JObj defs)
				foreach (var pair in defs.Items)
					_defs[pair.Key] = pair.Value as JObj ?? throw new FormatException($"{Source}: defs.{pair.Key} must be an object");
			if (root["lights"] is JObj lights)
			{
				Unknown(lights, new[] { "from_host", "set" }, "lights");
				LightsFromHost = lights["from_host"] is bool fromHost && fromHost;
				if (lights["set"] is JObj set)
					foreach (var pair in set.Items)
						Lights.Add(new KeyValuePair<string, float[]>(pair.Key, Floats(pair.Value, _globals, "lights.set." + pair.Key)));
			}
			var style = new Style { W = 0.25, Visual = "painted", Light = "NightIllumination", K = 1 };
			if (root.Has("units")) style.K = Units(root["units"], "units");
			style = style.With(root["style"] as JObj, this, _globals, "style");
			foreach (var element in List(root["elements"], "elements"))
				Expand(element, _globals, style, null, Top, Name);
			if (root["screen"] is JObj screen)
			{
				Unknown(screen, new[] { "name", "items", "note" }, "screen");
				ScreenName = screen.Str("name") ?? Name + " overlay";
				foreach (var item in List(screen["items"], "screen.items")) ScreenExpand(item, _globals, Screen, "screen");
			}
		}

		readonly Dictionary<string, JObj> _defs = new Dictionary<string, JObj>();

		sealed class Style
		{
			internal double W, K;
			internal string Visual, Light;
			internal Style With(JObj o, ReticleSpec spec, Env env, string where)
			{
				var copy = (Style)MemberwiseClone();
				if (o == null) return copy;
				spec.Unknown(o, new[] { "w", "visual", "light" }, where);
				if (o.Has("w")) copy.W = spec.Num(o["w"], env, where + ".w") * copy.K;
				if (o.Has("visual")) copy.Visual = spec.Interp(o["visual"], env, where);
				if (o.Has("light")) copy.Light = spec.Interp(o["light"], env, where);
				return copy;
			}
		}

		double Units(object v, string where)
		{
			if (v is double number) return number;
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
	}
}
