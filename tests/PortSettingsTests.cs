using System;
using System.Linq;
using System.Reflection;
using MelonLoader;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// Version comparison (the update check and the migrations), settings saved as text, and Finish end to end.
	public class PortSettingsTests
	{
		static readonly string DecimalComma = Repo.At("tests", "data", "settings_decimal_comma.cfg");

		static void UseFile(string path) =>
			typeof(PortSettings).GetField("_file", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, path);

		static MelonPreferences_Category T90A(params (string key, object value)[] saved)
		{
			var category = new MelonPreferences_Category("T-90A");
			foreach (var (key, value) in saved) category.Saved[key] = value;
			category.CreateEntry("Enabled", true);
			category.CreateEntry("Campaign prevalence", 0.1f);
			category.CreateEntry("Campaign unlock after missions", 2);
			category.CreateEntry("APFSDS", "3BM22");
			return category;
		}

		static T Value<T>(MelonPreferences_Category category, string entry) =>
			((MelonPreferences_Entry<T>)category.GetEntry(entry)).Value;


		[Theory]
		[InlineData("v0.5.0", "0.5.0.0")]
		[InlineData("V1.2", "1.2.0.0")]
		[InlineData("0.5", "0.5.0.0")]
		[InlineData("3", "3.0.0.0")]
		[InlineData("0.5.0-beta", "0.5.0.0")]
		[InlineData("0.4.4+3dac81f.public", "0.4.4.0")]
		[InlineData(" 1.0.0 ", "1.0.0.0")]
		[InlineData("1.2.3.4", "1.2.3.4")]
		public void Parse_versions(string text, string want) => Assert.Equal(new Version(want), PortSettings.Parse(text));

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("abc")]
		[InlineData("v")]
		public void Parse_refuses_what_is_not_a_version(string text) => Assert.Null(PortSettings.Parse(text));

		[Theory]
		[InlineData("v0.4.5", "0.4.4", true)]
		[InlineData("v1.0.0", "0.9.9", true)]
		[InlineData("0.10.0", "0.9.0", true)]
		[InlineData("v0.5.0", "0.5.0-beta", true)]
		[InlineData("v0.5.0-beta", "0.5.0", false)]
		[InlineData("0.5.0-beta.10", "0.5.0-beta.2", true)]
		[InlineData("0.5.0-rc", "0.5.0-beta", true)]
		[InlineData("0.5.0-beta", "0.5.0-1", true)]
		[InlineData("0.5.0-beta.1", "0.5.0-beta", true)]
		[InlineData("0.5.1-beta", "0.5.0", true)]
		[InlineData("0.5.0", "0.5.0+3dac81f.public", false)]
		[InlineData("0.5.0", "0.5.0", false)]
		[InlineData("0.4.4", "0.5.0", false)]
		[InlineData("abc", "0.5.0", false)]
		[InlineData("0.5.0", "", false)]
		public void Newer(string latest, string installed, bool want) => Assert.Equal(want, PortSettings.IsNewer(latest, installed));


		[Fact]
		public void Numbers_with_a_comma_or_a_point()
		{
			Assert.Equal((object)0.3f, PortSettings.Number("0,3", typeof(float)));
			Assert.Equal((object)0.35, PortSettings.Number(" 0.35 ", typeof(double)));
			Assert.Equal((object)3, PortSettings.Number("3", typeof(int)));
			Assert.Equal((object)7L, PortSettings.Number("7", typeof(long)));
			Assert.Equal((object)(-2), PortSettings.Number("-2", typeof(int)));
		}

		[Theory]
		[InlineData("3.5", typeof(int))]
		[InlineData("1,000.5", typeof(float))]
		[InlineData("1.000,5", typeof(float))]
		[InlineData("drei", typeof(float))]
		[InlineData("NaN", typeof(double))]
		[InlineData("Infinity", typeof(double))]
		[InlineData("1e40", typeof(float))]
		[InlineData("3000000000", typeof(int))]
		[InlineData("", typeof(int))]
		public void Not_a_number_of_that_type(string text, Type type) => Assert.Null(PortSettings.Number(text, type));

		[Fact]
		public void Quoted_values_of_one_section()
		{
			var lines = System.IO.File.ReadAllLines(DecimalComma);
			var t90a = PortSettings.QuotedValues(lines, "T-90A");
			Assert.Equal("0,3", t90a["Campaign prevalence"]);
			Assert.Equal("drei", t90a["Campaign unlock after missions"]);
			Assert.Equal("3BM15", t90a["APFSDS"]);
			Assert.False(t90a.ContainsKey("Enabled"));
			var other = PortSettings.QuotedValues(lines, "T-72M1");
			Assert.Equal("0,35", other["Campaign prevalence"]);
			Assert.Equal(2, other.Count);
			Assert.Empty(PortSettings.QuotedValues(lines, "T-80B"));
		}

		[Fact]
		public void Quoted_keys_and_headers_single_quotes_comments_and_table_arrays()
		{
			var found = PortSettings.QuotedValues(
				new[] { "[\"X\"]", "'a b' = '1,5' # comment", "c = \"2\"", "d = 3", "e = \"open", "[[X]]", "f = \"4\"", "[Y]", "g = \"5\"" },
				"X");
			Assert.Equal("1,5", found["a b"]);
			Assert.Equal("2", found["c"]);
			Assert.Equal(2, found.Count);
		}


		[Fact]
		public void Finish_reads_text_numbers_and_moves_an_old_default()
		{
			UseFile(DecimalComma);
			MelonLogger.Lines.Clear();
			var category = T90A(("APFSDS", "3BM15"));
			PortSettings.Finish(category, "0.3.0", SettingsMigration.Default("APFSDS", "0.3.0", "3BM15"));
			Assert.Equal(0.3f, Value<float>(category, "Campaign prevalence"));
			Assert.Equal(2, Value<int>(category, "Campaign unlock after missions"));
			Assert.Contains(MelonLogger.Lines, line => line.StartsWith("warning:") && line.Contains("drei"));
			Assert.Equal("3BM22", Value<string>(category, "APFSDS"));
			Assert.Equal("0.3.0", Value<string>(category, PortSettings.VersionEntry));
		}

		[Fact]
		public void Finish_leaves_a_setting_written_by_the_version_that_changed_it()
		{
			UseFile(DecimalComma);
			var category = T90A(("APFSDS", "3BM15"), (PortSettings.VersionEntry, "0.3.0"));
			PortSettings.Finish(category, "0.3.1", SettingsMigration.Default("APFSDS", "0.3.0", "3BM15"));
			Assert.Equal("3BM15", Value<string>(category, "APFSDS"));
			Assert.Equal("0.3.1", Value<string>(category, PortSettings.VersionEntry));
		}

		[Fact]
		public void Finish_leaves_a_setting_the_player_changed()
		{
			UseFile(DecimalComma);
			var category = T90A(("APFSDS", "3BM9"));
			PortSettings.Finish(category, "0.3.0", SettingsMigration.Default("APFSDS", "0.3.0", "3BM15"));
			Assert.Equal("3BM9", Value<string>(category, "APFSDS"));
		}

		[Fact]
		public void Finish_skips_a_migration_for_a_missing_entry_or_a_bad_version()
		{
			UseFile(Repo.At("tests", "data", "no_such_file.cfg"));
			var category = T90A(("APFSDS", "3BM15"));
			PortSettings.Finish(
				category,
				"0.3.0",
				SettingsMigration.Default("Gone", "0.3.0", 1f),
				SettingsMigration.Default("APFSDS", "soon", "3BM15"));
			Assert.Equal("3BM15", Value<string>(category, "APFSDS"));
		}

		// A bad version is reported before a missing entry, a missing entry before a wrong type.
		[Fact]
		public void Finish_warns_about_a_migration_that_can_never_move_anything()
		{
			UseFile(Repo.At("tests", "data", "no_such_file.cfg"));
			MelonLogger.Lines.Clear();
			var category = T90A(("Campaign prevalence", 0.25f));
			PortSettings.Finish(
				category,
				"0.3.0",
				SettingsMigration.Default("Gone", "soon", 1f),
				SettingsMigration.Default("Gone", "0.3.0", 0.25),
				SettingsMigration.Default("Campaign prevalence", "0.3.0", 0.25),
				SettingsMigration.Default("Campaign prevalence", "0.2.0", 0.25f));
			string start = "warning: T-90A settings: the default change of ";
			Assert.Equal(
				new[]
				{
					start + "\"Gone\" in soon does nothing: \"soon\" is not a version.",
					start + "\"Gone\" in 0.3.0 does nothing: no setting has that name.",
					start + "\"Campaign prevalence\" in 0.3.0 does nothing: the setting holds Single, the rule gives a Double.",
				},
				MelonLogger.Lines.Where(line => line.Contains("does nothing")).ToArray());
			Assert.Equal(0.1f, Value<float>(category, "Campaign prevalence"));
		}

		[Fact]
		public void Finish_matches_a_float_old_default_within_rounding()
		{
			UseFile(Repo.At("tests", "data", "no_such_file.cfg"));
			var category = new MelonPreferences_Category("Z");
			category.Saved["x"] = 0.1000001f;
			category.CreateEntry("x", 0.25f);
			PortSettings.Finish(category, "1.0.0", SettingsMigration.Default("x", "0.5", 0.1f));
			Assert.Equal(0.25f, Value<float>(category, "x"));
		}

		[Fact]
		public void Finish_runs_every_migration_newer_than_the_saved_version()
		{
			UseFile(Repo.At("tests", "data", "no_such_file.cfg"));
			var category = new MelonPreferences_Category("Z");
			category.Saved["a"] = 1;
			category.Saved["b"] = 1;
			category.Saved[PortSettings.VersionEntry] = "0.3.0";
			category.CreateEntry("a", 2);
			category.CreateEntry("b", 2);
			PortSettings.Finish(
				category,
				"0.5.0",
				SettingsMigration.Default("a", "0.3.0", 1),
				SettingsMigration.Default("b", "0.4.0-beta", 1));
			Assert.Equal(1, Value<int>(category, "a"));
			Assert.Equal(2, Value<int>(category, "b"));
			Assert.Single(category.Entries, entry => entry.Identifier == PortSettings.VersionEntry);
		}

		// Finish with no migrations at all: the numbers saved as text and the version entry, once.
		[Fact]
		public void Finish_reads_text_numbers_and_writes_the_version()
		{
			UseFile(DecimalComma);
			MelonLogger.Lines.Clear();
			var category = T90A(("APFSDS", "3BM15"));
			PortSettings.Finish(category, "0.4.4");
			Assert.Equal(0.3f, Value<float>(category, "Campaign prevalence"));
			Assert.Equal(2, Value<int>(category, "Campaign unlock after missions"));
			Assert.Contains(MelonLogger.Lines, line => line.StartsWith("warning:") && line.Contains("drei"));
			Assert.Equal("3BM15", Value<string>(category, "APFSDS"));
			Assert.Equal("0.4.4", Value<string>(category, PortSettings.VersionEntry));
			Assert.Single(category.Entries, entry => entry.Identifier == PortSettings.VersionEntry);
		}
	}
}
