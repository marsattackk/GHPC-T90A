using System;
using System.Collections.Generic;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	public class MiniJsonTests
	{
		[Fact]
		public void Objects_arrays_and_values()
		{
			var root = (MiniJson.Obj)MiniJson.Parse(" { \"a\": 1, \"b\": [true, false, null, -1.5e2], \"c\": { }, \"d\": [ ] } ");
			Assert.Equal(1.0, root["a"]);
			Assert.Equal(new List<object> { true, false, null, -150.0 }, (List<object>)root["b"]);
			Assert.Empty((MiniJson.Obj)root["c"]);
			Assert.Empty((List<object>)root["d"]);
		}

		[Fact]
		public void Every_number_is_a_double()
		{
			Assert.IsType<double>(MiniJson.Parse("3"));
			Assert.Equal(0.25, MiniJson.Parse("2.5E-1"));
		}

		[Fact]
		public void String_escapes()
		{
			Assert.Equal("a\nb\tc\rd\be\ff\"g\\h/i\u00e9", MiniJson.Parse("\"a\\nb\\tc\\rd\\be\\ff\\\"g\\\\h\\/i\\u00e9\""));
			Assert.Equal("Т-90А", MiniJson.Parse("\"Т-90А\""));
		}

		[Fact]
		public void A_key_twice_keeps_the_last()
		{
			var root = (MiniJson.Obj)MiniJson.Parse("{ \"a\": 1, \"a\": 2 }");
			Assert.Single(root);
			Assert.Equal(2.0, root["a"]);
		}

		[Theory]
		[InlineData("", "unexpected end")]
		[InlineData("{ \"a\": 1, }", "expected a key")]
		[InlineData("{ 'a': 1 }", "expected a key")]
		[InlineData("[1, ]", "unexpected ']'")]
		[InlineData("{ \"a\" 1 }", "expected ':'")]
		[InlineData("{ \"a\": 1 \"b\": 2 }", "expected ',' or '}'")]
		[InlineData("[1 2]", "expected ',' or ']'")]
		[InlineData("1 2", "trailing characters")]
		[InlineData("\"abc", "unterminated string")]
		[InlineData("tru", "unexpected 't'")]
		[InlineData("{\n\"a\": 1,\n\"b\": ?\n}", "at line 3")]
		public void Errors_say_what_and_where(string text, string want)
		{
			Assert.Contains(want, Assert.Throws<FormatException>(() => MiniJson.Parse(text)).Message);
		}

		[Theory]
		[InlineData("-")]
		[InlineData("1.2.3")]
		public void A_malformed_number_is_a_format_error(string text)
		{
			Assert.Throws<FormatException>(() => MiniJson.Parse(text));
		}

		[Theory]
		[InlineData("\"abc\\")]
		[InlineData("\"\\u12\"")]
		public void A_string_cut_short_after_a_backslash_is_a_format_error(string text)
		{
			Assert.Throws<FormatException>(() => MiniJson.Parse(text));
		}

		[Fact]
		public void The_shipped_module_data_parses()
		{
			var root = MiniJson.Parse(System.IO.File.ReadAllText(Repo.At("Resources", "modules_built.json")));
			Assert.IsType<MiniJson.Obj>(root);
		}
	}
}
