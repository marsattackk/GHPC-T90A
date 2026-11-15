using System;
using System.Collections.Generic;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// dm_armour from the bundle, and the number rules every bundle text asset shares.
	public class DmArmourTests : IDisposable
	{
		readonly List<string> _warnings = new List<string>();
		readonly Action<string> _keep = BundleText.Warn;

		public DmArmourTests() => BundleText.Warn = _warnings.Add;

		public void Dispose() => BundleText.Warn = _keep;

		const string Glacis = "hull_glacis\tgeometry\t1.25\t2.5\t0\t0\t1\tarmour_steel\ttemplate\tupper glacis\t[src:a]";
		const string Brick = "era_1\tera\t200\t400\t10\t20\t0\tplain\t1,0.5,0,1\tKontakt-5\t[src:b]\t1";

		[Fact]
		public void Reads_every_field()
		{
			var armour = DmArmour.Parse("# header\n\n" + Glacis + "\r\n" + Brick + "\n");
			Assert.Equal(2, armour.Entries.Count);
			Assert.Empty(_warnings);

			var glacis = armour["hull_glacis"];
			Assert.Equal("geometry", glacis.Mode);
			Assert.Equal(1.25f, glacis.Ke);
			Assert.Equal(2.5f, glacis.Ce);
			Assert.True(glacis.AngleMatters);
			Assert.Equal("armour_steel", glacis.Material);
			Assert.Equal("template", glacis.Xray);
			Assert.Equal("upper glacis", glacis.Name);
			Assert.Equal("[src:a]", glacis.Source);
			Assert.False(glacis.Slat);

			var brick = armour["era_1"];
			Assert.Equal(10f, brick.KeAfter);
			Assert.Equal(20f, brick.CeAfter);
			Assert.False(brick.AngleMatters);
			Assert.Equal("1,0.5,0,1", brick.Xray);
			Assert.True(brick.Slat);
		}

		[Theory]
		[InlineData("geometry", true, false, false, false)]
		[InlineData("era", false, true, false, false)]
		[InlineData("composite", false, false, true, false)]
		[InlineData("drawn", false, false, false, true)]
		[InlineData("nominal", false, false, false, false)]
		public void Mode_flags(string mode, bool variable, bool era, bool convex, bool drawn)
		{
			var entry = new DmArmour.Entry { Mode = mode };
			Assert.Equal(new[] { variable, era, convex, drawn }, new[] { entry.Variable, entry.Era, entry.Convex, entry.Drawn });
		}

		[Fact]
		public void A_bad_line_is_skipped_with_a_warning_and_the_rest_kept()
		{
			var armour = DmArmour.Parse(
				Glacis + "\n" + Glacis.Replace("hull_glacis\tgeometry\t1.25", "b\tgeometry\t0..05") + "\nshort\tline\n"
				+ Glacis.Replace("1.25", "0,5").Replace("hull_glacis", "comma") + "\n" + Brick);
			Assert.Equal(2, armour.Entries.Count);
			Assert.Null(armour["b"]);
			Assert.Null(armour["short"]);
			Assert.Null(armour["comma"]);
			Assert.Equal(3, _warnings.Count);
			Assert.StartsWith("'dm_armour' line 2 skipped (bad number '0..05')", _warnings[0]);
			Assert.StartsWith("'dm_armour' line 3 skipped (2 fields, want 11)", _warnings[1]);
			Assert.StartsWith("'dm_armour' line 4 skipped (bad number '0,5')", _warnings[2]);
		}

		[Fact]
		public void A_long_bad_line_is_cut_in_the_warning()
		{
			DmArmour.Parse("x" + new string('y', 200));
			Assert.EndsWith(" (cut)", _warnings[0]);
			Assert.True(_warnings[0].Length < 200);
		}

		[Fact]
		public void Lookup_of_a_piece_it_lacks_is_null()
		{
			var armour = DmArmour.Parse(Glacis);
			Assert.Null(armour["nothing"]);
			Assert.Null(armour[null]);
		}

		[Fact]
		public void A_piece_twice_is_listed_twice_and_found_as_the_last()
		{
			var armour = DmArmour.Parse(Glacis + "\n" + Glacis.Replace("upper glacis", "again"));
			Assert.Equal(2, armour.Entries.Count);
			Assert.Equal("again", armour["hull_glacis"].Name);
		}

		[Theory]
		[InlineData("0.3", 0.3f)]
		[InlineData("-2", -2f)]
		[InlineData("3e-2", 0.03f)]
		[InlineData(" 1 ", 1f)]
		public void Bundle_numbers_use_a_decimal_point(string text, float want) => Assert.Equal(want, BundleText.Float(text));

		[Theory]
		[InlineData("0,3")]
		[InlineData("")]
		[InlineData("one")]
		public void Anything_else_is_a_bad_number(string text)
		{
			Assert.Contains($"bad number '{text}'", Assert.Throws<FormatException>(() => BundleText.Float(text)).Message);
		}

		[Fact]
		public void Number_lists_need_exactly_the_count()
		{
			Assert.Equal(new[] { 1f, -2.5f, 0.03f }, BundleText.Floats("1,-2.5,3e-2", 3));
			Assert.Throws<FormatException>(() => BundleText.Floats("1,2", 3));
			Assert.Throws<FormatException>(() => BundleText.Floats("1,2,3,4", 3));
		}
	}
}
