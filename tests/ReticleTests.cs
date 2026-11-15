using System;
using System.Collections.Generic;
using System.Linq;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// Whole reticle files: the JSON flavour, units, the element kinds, generators, defs and the screen overlay.
	public class ReticleTests
	{
		static ReticleSpec Spec(string elements, string extra = "", IDictionary<string, double> vars = null) =>
			ReticleSpec.Parse("{ \"name\": \"t\", " + extra + " \"elements\": [" + elements + "] }", "test", vars);

		static List<ReticleSpec.Line> Lines(ReticleSpec spec) => spec.Top.OfType<ReticleSpec.Line>().ToList();

		static string Problems(ReticleSpec spec) => string.Join("; ", spec.Problems);


		[Fact]
		public void Comments_and_trailing_commas_are_fine()
		{
			var spec = ReticleSpec.Parse(
				"// top\n{ \"name\": \"t\", /* inline */ \"elements\": [ { \"line\": [0, 0, 0, 1], }, ], }",
				"test");
			Assert.Single(spec.Top);
			Assert.Empty(spec.Problems);
		}

		[Fact]
		public void A_key_twice_is_refused_with_its_line()
		{
			var error = Assert.Throws<FormatException>(() => ReticleSpec.Parse("{\n\"name\": \"a\",\n\"name\": \"b\" }", "file.jsonc"));
			Assert.Contains("file.jsonc: line 3", error.Message);
			Assert.Contains("'name' twice", error.Message);
		}

		[Fact]
		public void Text_after_the_end_is_refused()
		{
			Assert.Throws<FormatException>(() => ReticleSpec.Parse("{ \"name\": \"t\", \"elements\": [] } x", "test"));
		}

		[Fact]
		public void The_top_level_must_be_an_object_with_a_name()
		{
			Assert.Throws<FormatException>(() => ReticleSpec.Parse("[]", "test"));
			Assert.Contains("no 'name'", Assert.Throws<FormatException>(() => ReticleSpec.Parse("{ \"elements\": [] }", "test")).Message);
		}

		// Hand-edited files with a missing bracket or quote should get a line number, not a crash.
		[Theory]
		[InlineData("{")]
		[InlineData("{ \"name\": \"t\"")]
		[InlineData("{ \"name\": \"t")]
		[InlineData("[1, 2")]
		[InlineData("{ \"name\": \"t\\")]
		[InlineData("\"\\u12")]
		public void A_file_cut_short_is_a_format_error(string text)
		{
			Assert.StartsWith("test: line ", Assert.Throws<FormatException>(() => ReticleSpec.Parse(text, "test")).Message);
		}


		[Fact]
		public void Defaults_and_predefined_names()
		{
			var spec = Spec("", "\"fov\": 4, \"vars\": { \"A\": \"Fov * 2\", \"B\": \"PI\" },");
			Assert.Equal("first", spec.Plane);
			Assert.Equal(8, spec.Num("A"));
			Assert.Equal(Math.PI, spec.Num("B"));
		}

		[Fact]
		public void Vars_run_in_file_order_and_can_be_overridden()
		{
			const string vars = "\"vars\": { \"W\": 2, \"H\": \"W * 3\" },";
			Assert.Equal(6, Spec("", vars).Num("H"));
			var over = Spec("", vars, new Dictionary<string, double> { ["W"] = 5 });
			Assert.Equal(5, over.Num("W"));
			Assert.Equal(15, over.Num("H"));
		}

		[Fact]
		public void Unknown_keys_and_kinds_are_problems_not_errors()
		{
			var spec = Spec("{ \"line\": [0, 0, 0, 1], \"bogus\": 1 }, { \"wiggle\": 1 }, 5", "\"extra\": 1,");
			Assert.Contains(spec.Problems, problem => problem.Contains("top level: unknown key 'extra'"));
			Assert.Contains(spec.Problems, problem => problem.Contains("unknown key 'bogus'"));
			Assert.Contains(spec.Problems, problem => problem.Contains("no element kind"));
			Assert.Contains(spec.Problems, problem => problem.Contains("element is not an object"));
			Assert.Single(spec.Top);
		}

		[Fact]
		public void Lights()
		{
			var spec = Spec("", "\"lights\": { \"from_host\": true, \"set\": { \"NightIllumination\": [4, \"1 + 1\", 0.5] } },");
			Assert.True(spec.LightsFromHost);
			Assert.Equal("NightIllumination", spec.Lights.Single().Key);
			Assert.Equal(new[] { 4f, 2f, 0.5f }, spec.Lights.Single().Value);
		}


		[Theory]
		[InlineData("\"mrad\"", 1)]
		[InlineData("\"mil\"", 2000 * Math.PI / 6000)]
		[InlineData("\"mil_nato\"", 2000 * Math.PI / 6400)]
		[InlineData("\"deg\"", 1000 * Math.PI / 180)]
		[InlineData("\"moa\"", 1000 * Math.PI / 180 / 60)]
		[InlineData("2.5", 2.5)]
		public void Units_scale_into_milliradians(string units, double perUnit)
		{
			var line = Lines(Spec("{ \"line\": [1, 1, 0, 1] }", "\"units\": " + units + ","))[0];
			Assert.Equal(perUnit, line.Len, 9);
			Assert.Equal(perUnit, line.X, 9);
			Assert.Equal(perUnit, line.Y, 9);
		}

		[Fact]
		public void Field_units_are_half_the_field_of_view()
		{
			var line = Lines(Spec("{ \"line\": [0, 0, 0, 1] }", "\"fov\": 10, \"units\": \"field\","))[0];
			Assert.Equal(5 * 1000 * Math.PI / 180, line.Len, 9);
			Assert.Contains("need 'fov'", Assert.Throws<FormatException>(() => Spec("", "\"units\": \"field\",")).Message);
			Assert.Contains("unknown units", Assert.Throws<FormatException>(() => Spec("", "\"units\": \"furlong\",")).Message);
		}

		[Fact]
		public void Stroke_width_is_in_the_units_too_but_the_default_is_not()
		{
			const double mil = 2000 * Math.PI / 6000;
			Assert.Equal(0.25, Lines(Spec("{ \"line\": [0, 0, 0, 1] }", "\"units\": \"mil\","))[0].W, 9);
			Assert.Equal(mil, Lines(Spec("{ \"line\": [0, 0, 0, 1] }", "\"units\": \"mil\", \"style\": { \"w\": 1 },"))[0].W, 9);
			Assert.Equal(2 * mil, Lines(Spec("{ \"line\": [0, 0, 0, 1], \"w\": 2 }", "\"units\": \"mil\","))[0].W, 9);
		}

		[Fact]
		public void Style_reaches_children_and_an_element_overrides_it()
		{
			var spec = Spec(
				"{ \"group\": \"g\", \"style\": { \"visual\": \"lit\", \"light\": \"L{1 + 1}\" }, \"elements\": ["
				+ "{ \"line\": [0, 0, 0, 1] }, { \"line\": [0, 0, 0, 1], \"visual\": \"painted\" } ] }");
			var lines = ((ReticleSpec.Group)spec.Top[0]).Children.Cast<ReticleSpec.Line>().ToList();
			Assert.Equal("lit", lines[0].Visual);
			Assert.Equal("L2", lines[0].Light);
			Assert.Equal("painted", lines[1].Visual);
			Assert.Equal("L2", lines[1].Light);
		}

		[Fact]
		public void Under_a_ballistic_or_stadia_group_y_is_range_and_not_scaled()
		{
			const double mil = 2000 * Math.PI / 6000;
			foreach (var ranged in new[] { "\"ballistic\": \"3BM60\"", "\"stadia\": 2.7" })
			{
				var group = (ReticleSpec.Group)Spec(
					"{ \"group\": \"g\", " + ranged + ", \"units\": \"mil\", \"elements\": [ { \"line\": [1, 500, 0, 1] } ] }").Top[0];
				Assert.True(group.Ranged);
				var line = (ReticleSpec.Line)group.Children[0];
				Assert.Equal(mil, line.X, 9);
				Assert.Equal(500, line.Y, 9);
			}
		}


		[Fact]
		public void Seg_becomes_a_line_through_its_middle()
		{
			var line = Lines(Spec("{ \"seg\": [0, 0, 3, 4] }")).Single();
			Assert.Equal(1.5, line.X, 9);
			Assert.Equal(2, line.Y, 9);
			Assert.Equal(5, line.Len, 9);
			Assert.Equal(Math.Atan2(4, 3) * 180 / Math.PI, line.Deg, 9);
		}

		[Fact]
		public void Poly_closes_only_when_asked()
		{
			Assert.Equal(2, Lines(Spec("{ \"poly\": [[0, 0], [1, 0], [1, 1]] }")).Count);
			Assert.Equal(3, Lines(Spec("{ \"poly\": [[0, 0], [1, 0], [1, 1]], \"closed\": true }")).Count);
		}

		[Fact]
		public void Dash_cuts_the_last_dash_short()
		{
			var lines = Lines(Spec("{ \"dash\": [0, 0, 10, 0], \"on\": 2, \"off\": 1 }"));
			Assert.Equal(new[] { 1.0, 4, 7, 9.5 }, lines.Select(line => Math.Round(line.X, 9)));
			Assert.Equal(1, lines.Last().Len, 9);
			Assert.Throws<FormatException>(() => Spec("{ \"dash\": [0, 0, 10, 0], \"on\": 0, \"off\": 1 }"));
		}

		[Fact]
		public void Arc_has_a_segment_per_ten_degrees_unless_told()
		{
			var lines = Lines(Spec("{ \"arc\": [0, 0, 1, 0, 90] }"));
			Assert.Equal(9, lines.Count);
			Assert.Equal(4, Lines(Spec("{ \"arc\": [0, 0, 1, 0, 90], \"segments\": 4 }")).Count);
			Assert.Single(Lines(Spec("{ \"arc\": [0, 0, 1, 0, 5] }")));
		}

		[Fact]
		public void Circle_has_32_segments_unless_told()
		{
			var spec = Spec("{ \"circle\": [1, 2, 3] }, { \"circle\": [0, 0, 1], \"segments\": 16 }");
			var circles = spec.Top.Cast<ReticleSpec.Circle>().ToList();
			Assert.Equal(32, circles[0].Segments);
			Assert.Equal(3, circles[0].R);
			Assert.Equal(16, circles[1].Segments);
		}

		[Fact]
		public void Wrong_number_counts_are_errors()
		{
			Assert.Throws<FormatException>(() => Spec("{ \"line\": [0, 0, 0] }"));
			Assert.Throws<FormatException>(() => Spec("{ \"seg\": [0, 0, 1] }"));
			Assert.Throws<FormatException>(() => Spec("{ \"arc\": [0, 0, 1, 0] }"));
			Assert.Throws<FormatException>(() => Spec("{ \"circle\": [0, 0] }"));
			Assert.Throws<FormatException>(() => Spec("{ \"text\": \"x\", \"at\": [0] }"));
		}

		[Fact]
		public void Text_fills_in_braces()
		{
			var spec = Spec(
				"{ \"text\": \"R{L[-1] * 10}\" }, { \"text\": \"{1 < 2}{0.5}\", \"size\": 20 }",
				"\"vars\": { \"L\": \"[1, 2, 3]\" },");
			var texts = spec.Top.Cast<ReticleSpec.Text>().ToList();
			Assert.Equal("R30", texts[0].Value);
			Assert.Equal(10, texts[0].Size);
			Assert.Equal("10.5", texts[1].Value);
			Assert.Contains("unclosed", Assert.Throws<FormatException>(() => Spec("{ \"text\": \"a{1\" }")).Message);
		}

		[Fact]
		public void Glyphs_draw_the_letters_the_fonts_lack()
		{
			Assert.Equal(3, Lines(Spec("{ \"glyph\": \"П\", \"at\": [0, 0], \"h\": 2 }")).Count);
			var spec = Spec("{ \"glyph\": \"Ж\", \"at\": [0, 0], \"h\": 1 }");
			Assert.Empty(spec.Top);
			Assert.Contains(spec.Problems, problem => problem.Contains("no stroke glyph for 'Ж'"));
			foreach (var letter in ReticleSpec.GlyphLetters.Split(' '))
				Assert.NotNull(ReticleSpec.Glyph(letter, 0, 0, 1));
		}


		[Fact]
		public void For_over_a_range()
		{
			var lines = Lines(Spec("{ \"for\": \"r\", \"in\": \"0 .. 8 step 2\", \"do\": [ { \"line\": [\"r\", 0, 90, 1] } ] }"));
			Assert.Equal(new[] { 0.0, 2, 4, 6, 8 }, lines.Select(line => line.X));
		}

		[Fact]
		public void For_splices_a_string_that_gives_a_list_but_keeps_a_written_list_whole()
		{
			var spliced = Lines(Spec("{ \"for\": \"x\", \"in\": [\"0 .. 1\", 5], \"do\": [ { \"line\": [\"x\", 0, 0, 1] } ] }"));
			Assert.Equal(new[] { 0.0, 1, 5 }, spliced.Select(line => line.X));
			var tuples = Lines(Spec("{ \"for\": [\"a\", \"b\"], \"in\": [[1, 2], [3, 4]], \"do\": [ { \"seg\": [\"a\", 0, \"b\", 0] } ] }"));
			Assert.Equal(new[] { 1.5, 3.5 }, tuples.Select(line => line.X));
		}

		[Fact]
		public void For_items_must_match_the_names()
		{
			var error = Assert.Throws<FormatException>(() =>
				Spec("{ \"for\": [\"a\", \"b\"], \"in\": [[1, 2], [3]], \"do\": [ { \"seg\": [\"a\", 0, \"b\", 0] } ] }"));
			Assert.Contains("item 1 does not match (a, b)", error.Message);
			Assert.Throws<FormatException>(() => Spec("{ \"for\": \"x\", \"in\": 5, \"do\": [] }"));
		}

		[Fact]
		public void If_then_else()
		{
			const string tick = "{ \"for\": \"r\", \"in\": \"0 .. 3\", \"do\": [ { \"if\": \"r % 2 == 0\", "
				+ "\"then\": [ { \"line\": [\"r\", 0, 0, 2] } ], \"else\": [ { \"line\": [\"r\", 0, 0, 1] } ] } ] }";
			Assert.Equal(new[] { 2.0, 1, 2, 1 }, Lines(Spec(tick)).Select(line => line.Len));
			Assert.Empty(Spec("{ \"if\": false, \"then\": [ { \"line\": [0, 0, 0, 1] } ] }").Top);
		}

		[Fact]
		public void Lambdas_see_where_they_were_written_not_where_they_are_called()
		{
			var spec = Spec(
				"{ \"for\": \"K\", \"in\": [10], \"do\": [ { \"text\": \"{F(0)}\" } ] }",
				"\"vars\": { \"K\": 1, \"F\": \"x => x + K\" },");
			Assert.Equal("1", ((ReticleSpec.Text)spec.Top[0]).Value);
		}


		const string Tick = "\"defs\": { \"Tick\": { \"params\": [\"x\", \"len\"], \"elements\": [ { \"line\": [\"x\", 0, 90, \"len\"] } ] } },";

		[Fact]
		public void Use_fills_in_a_def()
		{
			var line = Lines(Spec("{ \"use\": \"Tick\", \"args\": { \"x\": \"1 + 1\", \"len\": 3 } }", Tick)).Single();
			Assert.Equal(2, line.X);
			Assert.Equal(3, line.Len);
		}

		[Fact]
		public void Use_checks_its_arguments()
		{
			Assert.Contains("needs 'len'", Assert.Throws<FormatException>(() => Spec("{ \"use\": \"Tick\", \"args\": { \"x\": 1 } }", Tick)).Message);
			Assert.Contains("no def 'Tock'", Assert.Throws<FormatException>(() => Spec("{ \"use\": \"Tock\" }", Tick)).Message);
			var extra = Spec("{ \"use\": \"Tick\", \"args\": { \"x\": 1, \"len\": 1, \"w\": 2 } }", Tick);
			Assert.Contains(extra.Problems, problem => problem.Contains("'Tick' has no parameter 'w'"));
		}

		[Fact]
		public void A_def_sees_the_vars_but_not_the_loop_it_is_used_in()
		{
			const string defs = "\"vars\": { \"G\": 4 }, \"defs\": { \"D\": { \"elements\": [ { \"line\": [\"G\", 0, 0, \"i\"] } ] } },";
			var error = Assert.Throws<FormatException>(() => Spec("{ \"for\": \"i\", \"in\": [1], \"do\": [ { \"use\": \"D\" } ] }", defs));
			Assert.Contains("unknown name 'i'", error.Message);
			const string seesVars = "\"vars\": { \"G\": 4 }, \"defs\": { \"D\": { \"elements\": [ { \"line\": [\"G\", 0, 0, 1] } ] } },";
			Assert.Equal(4, Lines(Spec("{ \"use\": \"D\" }", seesVars)).Single().X);
		}


		static ReticleSpec Screen(string items) =>
			ReticleSpec.Parse("{ \"name\": \"t\", \"elements\": [], \"screen\": { \"items\": [" + items + "] } }", "test");

		[Fact]
		public void Screen_items()
		{
			var spec = Screen(
				"{ \"rect\": \"bar\", \"at\": [1, 2], \"size\": [3, 4], \"color\": [0, 0, 0] },"
				+ "{ \"seg\": [0, 0, 3, 4], \"w\": 0.5 },"
				+ "{ \"readout\": \"range\", \"value\": \"{2 * 2}\", \"text_size\": 12, \"anchor\": \"bottom\" },"
				+ "{ \"text\": \"hello\", \"size\": 9 }");
			Assert.Equal("t overlay", spec.ScreenName);
			Assert.Empty(spec.Problems);
			var rect = spec.Screen[0];
			Assert.Equal(new[] { 1.0, 2, 3, 4 }, new[] { rect.X, rect.Y, rect.W, rect.H });
			Assert.Equal(new[] { 0f, 0f, 0f, 1f }, rect.Color);
			var seg = spec.Screen[1];
			Assert.Equal(new[] { 1.5, 2, 5, 0.5 }, new[] { seg.X, seg.Y, seg.W, seg.H });
			Assert.Equal("4", spec.Screen[2].Value);
			Assert.Equal(12, spec.Screen[2].Size);
			Assert.Equal("bottom", spec.Screen[2].Anchor);
			Assert.Equal("text", spec.Screen[3].Name);
			Assert.Equal("hello", spec.Screen[3].Value);
			Assert.Equal(9, spec.Screen[3].Size);
		}

		[Fact]
		public void Screen_problems()
		{
			var spec = Screen("{ \"for\": \"i\", \"in\": [1] }, { \"blob\": 1 }, { \"lamp\": \"a\", \"anchor\": \"left\" }");
			Assert.Contains(spec.Problems, problem => problem.Contains("not supported on the screen"));
			Assert.Contains(spec.Problems, problem => problem.Contains("no screen item kind"));
			Assert.Contains(spec.Problems, problem => problem.Contains("anchor 'left'"));
		}
	}
}
