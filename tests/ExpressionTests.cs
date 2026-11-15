using System;
using System.Collections.Generic;
using System.Linq;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// The expression language on its own, as written in the reticle files' strings.
	public class ExpressionTests
	{
		static object Eval(string text, params (string name, object value)[] names)
		{
			var env = new ReticleSpec.Env(null);
			foreach (var (name, value) in names) env.Set(name, value);
			return ReticleSpec.Expr.Parse(text, "test").Eval(env);
		}

		static double Num(string text, params (string, object)[] names) => (double)Eval(text, names);

		static double[] Nums(string text) => ((List<object>)Eval(text)).Select(item => (double)item).ToArray();

		static FormatException Bad(string text, params (string, object)[] names) =>
			Assert.Throws<FormatException>(() => Eval(text, names));

		[Theory]
		[InlineData("1 + 2 * 3", 7)]
		[InlineData("(1 + 2) * 3", 9)]
		[InlineData("2 - 3 - 4", -5)]
		[InlineData("2 * 3 % 4", 2)]
		[InlineData("10 / 4", 2.5)]
		[InlineData("-2 * 3", -6)]
		[InlineData("- -3", 3)]
		[InlineData("--3", 3)]
		[InlineData("+true", 1)]
		[InlineData("7 % 3", 1)]
		public void Arithmetic(string text, double want) => Assert.Equal(want, Num(text), 9);

		[Theory]
		[InlineData(".5", 0.5)]
		[InlineData("1e3", 1000)]
		[InlineData("2.5E-1", 0.25)]
		[InlineData("1e+2", 100)]
		[InlineData("007", 7)]
		public void Numbers(string text, double want) => Assert.Equal(want, Num(text), 9);

		[Fact]
		public void Comparisons_give_bools()
		{
			Assert.Equal(true, Eval("1 < 2"));
			Assert.Equal(true, Eval("2 <= 2"));
			Assert.Equal(false, Eval("2 > 2"));
			Assert.Equal(true, Eval("3 >= 2"));
			Assert.Equal(true, Eval("1 < 2 == 2 > 1"));
		}

		[Fact]
		public void Equality_allows_rounding_error_but_not_more()
		{
			Assert.Equal(true, Eval("0.1 + 0.2 == 0.3"));
			Assert.Equal(true, Eval("1 != 1.0000001"));
			Assert.Equal(true, Eval("1 == true"));
		}

		[Fact]
		public void Strings_compare_only_with_strings()
		{
			Assert.Equal(true, Eval("'a' == 'a'"));
			Assert.Equal(false, Eval("'a' == 'b'"));
			Assert.Equal(false, Eval("'1' == 1"));
			Assert.Equal(true, Eval("'1' != 1"));
		}

		[Fact]
		public void And_or_skip_the_right_side_and_give_bools()
		{
			Assert.Equal(false, Eval("false && nothing_called_this"));
			Assert.Equal(true, Eval("true || nothing_called_this"));
			Assert.Equal(true, Eval("2 && 3"));
			Assert.Equal(false, Eval("0 || 0"));
			Bad("true && nothing_called_this");
		}

		[Fact]
		public void Not_and_truth()
		{
			Assert.Equal(true, Eval("!0"));
			Assert.Equal(false, Eval("!1"));
			Assert.Equal(false, Eval("!'text'"));
			Assert.Equal("yes", Eval("[] ? 'yes' : 'no'"));
			Assert.Equal("no", Eval("x ? 'yes' : 'no'", ("x", null)));
		}

		[Fact]
		public void Ternary_nests_to_the_right()
		{
			Assert.Equal(3.0, Eval("0 ? 1 : 0 ? 2 : 3"));
			Assert.Equal(2.0, Eval("1 > 2 ? 1 : 2"));
			Assert.Equal("a", Eval("1 ? 'a' : 'b'"));
		}

		[Fact]
		public void Ranges_include_the_end_when_a_step_lands_on_it()
		{
			Assert.Equal(new[] { 1.0, 2, 3 }, Nums("1 .. 3"));
			Assert.Equal(new[] { 0.0, 2, 4, 6, 8 }, Nums("0 .. 8 step 2"));
			Assert.Equal(new[] { 0.0, 2, 4, 6 }, Nums("0 .. 7 step 2"));
			Assert.Equal(new[] { 3.0, 2, 1 }, Nums("3 .. 1 step -1"));
			Assert.Equal(new[] { 2.0 }, Nums("2 .. 2"));
			var tenths = Nums("0 .. 1 step 0.1");
			Assert.Equal(11, tenths.Length);
			Assert.Equal(1.0, tenths.Last(), 9);
			Assert.Equal(4, Nums("0 .. 0.3 step 0.1").Length);   // 0.3 / 0.1 is 2.9999999999999996
		}

		[Fact]
		public void Range_numbers_need_no_spaces()
		{
			Assert.Equal(new[] { 1.0, 2, 3 }, Nums("1..3"));
			Assert.Equal(new[] { 1.5, 2.5 }, Nums("1.5..3"));
		}

		[Fact]
		public void Range_ends_are_sums()
		{
			Assert.Equal(new[] { 0.0, 1, 2, 3 }, Nums("0 .. 2 + 1"));
			Assert.Equal(new[] { 0.0, 3 }, Nums("0 .. 3 step 1 + 2"));
		}

		[Fact]
		public void A_range_that_never_gets_there_is_refused()
		{
			Assert.Contains("never ends", Bad("3 .. 1").Message);
			Assert.Contains("never ends", Bad("0 .. 1 step 0").Message);
		}

		[Fact]
		public void Step_is_only_a_keyword_after_a_range()
		{
			Assert.Equal(6.0, Num("step * 2", ("step", 3.0)));
		}

		[Fact]
		public void Lists_join_and_index_from_either_end()
		{
			Assert.Empty((List<object>)Eval("[]"));
			Assert.Equal(new[] { 1.0, 2, 3 }, Nums("[1, 2] + [3]"));
			Assert.Equal(new[] { 1.0, 2, 3, 4 }, Nums("[1, 2] + (3 .. 4)"));
			Assert.Equal(20.0, Eval("[10, 20, 30][1]"));
			Assert.Equal(30.0, Eval("[10, 20, 30][-1]"));
			Assert.Equal(20.0, Eval("[10, 20, 30][1.7]"));
			Assert.Equal(3.0, Eval("[[1, 2], [3, 4]][1][0]"));
			Assert.Contains("outside the list", Bad("[10, 20, 30][3]").Message);
			Assert.Contains("outside the list", Bad("[10, 20, 30][-4]").Message);
			Assert.Contains("not a list", Bad("5[0]").Message);
			Assert.Contains("got a list", Bad("[1, 2] + 3").Message);
		}

		[Fact]
		public void A_range_in_brackets_is_a_list_inside_a_list()
		{
			var outer = (List<object>)Eval("[0 .. 2]");
			Assert.Single(outer);
			Assert.Equal(3, ((List<object>)outer[0]).Count);
		}

		[Fact]
		public void Plus_with_a_string_joins_text()
		{
			Assert.Equal("R30", Eval("'R' + 30"));
			Assert.Equal("x1.5", Eval("'x' + 1.5"));
			Assert.Equal("3a", Eval("1 + 2 + 'a'"));
			Assert.Equal("a12", Eval("'a' + 1 + 2"));
		}

		[Fact]
		public void Lambdas_take_none_one_or_several_parameters()
		{
			Assert.Equal(6.0, Eval("(x => x * 2)(3)"));
			Assert.Equal(3.0, Eval("((a, b) => a - b)(5, 2)"));
			Assert.Equal(7.0, Eval("(() => 7)()"));
			Assert.Equal(3.0, Eval("(x => y => x + y)(1)(2)"));
		}

		[Fact]
		public void Lambdas_from_names_are_called_like_builtins()
		{
			var half = Eval("(a, b) => (a + b) / 2");
			Assert.Equal(0.3, Num("Half(0.2, 0.4)", ("Half", half)), 9);
		}

		[Fact]
		public void Calls_check_what_they_call()
		{
			Assert.Contains("1 argument(s) expected, 2 given", Bad("(x => x)(1, 2)").Message);
			Assert.Contains("not a function", Bad("3(1)").Message);
		}

		[Theory]
		[InlineData("sqrt(16)", 4)]
		[InlineData("abs(-2)", 2)]
		[InlineData("abs(true)", 1)]
		[InlineData("min(3, 1, 2)", 1)]
		[InlineData("max(3, 1, 2)", 3)]
		[InlineData("round(2.5)", 3)]
		[InlineData("round(-2.5)", -3)]
		[InlineData("floor(-1.5)", -2)]
		[InlineData("ceil(1.2)", 2)]
		[InlineData("deg(rad(90))", 90)]
		[InlineData("sin(rad(30))", 0.5)]
		[InlineData("cos(0)", 1)]
		[InlineData("tan(rad(45))", 1)]
		[InlineData("deg(atan(1))", 45)]
		[InlineData("deg(atan2(1, -1))", 135)]
		public void Builtins(string text, double want) => Assert.Equal(want, Num(text), 9);

		[Fact]
		public void A_name_in_the_file_wins_over_a_builtin()
		{
			Assert.Equal(5.0, Eval("sqrt", ("sqrt", 5.0)));
		}

		[Fact]
		public void Unknown_names_are_named_in_the_error()
		{
			Assert.Contains("unknown name 'foo'", Bad("foo + 1").Message);
		}

		[Theory]
		[InlineData("1 2")]
		[InlineData("1 +")]
		[InlineData("(1")]
		[InlineData("[1, 2")]
		[InlineData("1e")]
		[InlineData("'unclosed")]
		[InlineData("\"double quotes\"")]
		[InlineData("1 # 2")]
		[InlineData("1 ? 2")]
		[InlineData("")]
		public void Bad_text_is_a_format_error(string text) => Bad(text);

		[Theory]
		[InlineData(3.0, "3")]
		[InlineData(1.5, "1.5")]
		[InlineData(-0.5, "-0.5")]
		[InlineData(-1.0 / 3, "-0.333333")]
		[InlineData(0.1234567, "0.123457")]
		[InlineData(2.0000000001, "2")]
		[InlineData(1e-10, "0")]
		[InlineData(1234567.5, "1234567.5")]
		public void Numbers_print_without_trailing_zeros(double value, string want) => Assert.Equal(want, ReticleSpec.Fmt(value));
	}
}
