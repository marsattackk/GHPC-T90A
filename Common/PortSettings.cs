using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader;
using MelonLoader.Utils;

namespace T90A.Common
{
	internal static class PortSettings
	{
		internal const string VersionEntry = "ConfigVersion";
		static readonly Type[] Numbers = { typeof(float), typeof(double), typeof(int), typeof(long) };

		static string _file = Path.Combine(MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg");

		internal static void Finish(MelonPreferences_Category cfg, string modVersion, params SettingsMigration[] migrations)
		{
			try { Rescue(cfg); }
			catch (Exception e) { MelonLogger.Warning($"{cfg.DisplayName} settings: could not read the settings file for numbers saved as text ({e.GetType().Name}: {e.Message})."); }
			var version = cfg.CreateEntry(VersionEntry, "", description: "The mod version that last wrote these settings (a newer version uses it to move settings still at an old default to the new one). Leave it as it is.", is_hidden: true);
			var last = Parse(version.Value) ?? new Version(0, 0, 0, 0);
			foreach (var m in migrations)
			{
				var changed = Parse(m.ChangedIn);
				var e = cfg.GetEntry(m.Entry);
				if (changed == null || e == null || last >= changed || !m.AtOld(e)) continue;
				string old = e.GetValueAsString();
				m.ToNew(e);
				Say($"{cfg.DisplayName} settings: \"{m.Entry}\" was still {old}, the default before {m.ChangedIn}; now {e.GetValueAsString()}, the new default.", false);
			}
			if (version.Value != modVersion) version.Value = modVersion;
		}

		// A quoted "0,3" from a decimal-comma Windows: MelonLoader silently keeps the default and saves it over the
		// player's value at exit.
		static void Rescue(MelonPreferences_Category cfg)
		{
			if (!File.Exists(_file)) return;
			var text = QuotedValues(File.ReadAllLines(_file), cfg.Identifier);
			foreach (var e in cfg.Entries)
			{
				var type = e.GetReflectedType();
				if (!Numbers.Contains(type) || !text.TryGetValue(e.Identifier, out string s)) continue;
				object value = Number(s, type);
				if (value == null)
				{
					Say($"{cfg.DisplayName} settings: \"{e.Identifier}\" is saved as \"{s}\", which is not a number; using the default {e.GetDefaultValueAsString()}.", true);
					continue;
				}
				e.BoxedValue = value;
				Say($"{cfg.DisplayName} settings: \"{e.Identifier}\" was saved as text \"{s}\"; read as {e.GetValueAsString()} (it is saved back as a number when the game closes).", false);
			}
		}

		static void Say(string s, bool warn)
		{
			if (warn) MelonLogger.Warning(s); else MelonLogger.Msg(s);
		}

		internal static object Number(string s, Type type)
		{
			s = s.Trim();
			if (s.Count(c => c == ',' || c == '.') > 1) return null;
			if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d)) return null;
			if (type == typeof(float)) return Math.Abs(d) <= float.MaxValue ? (object)(float)d : null;
			if (type == typeof(double)) return d;
			if (d != Math.Floor(d)) return null;
			if (type == typeof(int)) return d >= int.MinValue && d <= int.MaxValue ? (object)(int)d : null;
			return d >= long.MinValue && d <= long.MaxValue ? (object)(long)d : null;
		}

		internal static Dictionary<string, string> QuotedValues(string[] lines, string category)
		{
			var found = new Dictionary<string, string>();
			bool inside = false;
			foreach (var raw in lines)
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#') continue;
				if (line[0] == '[')
				{
					int end = line.LastIndexOf(']');
					inside = !line.StartsWith("[[") && end > 0 && Unquote(line.Substring(1, end - 1).Trim()) == category;
					continue;
				}
				if (!inside) continue;
				string key, rest;
				if (line[0] == '"' || line[0] == '\'')
				{
					int close = line.IndexOf(line[0], 1);
					if (close < 0) continue;
					key = line.Substring(1, close - 1);
					rest = line.Substring(close + 1).TrimStart();
				}
				else
				{
					int eq = line.IndexOf('=');
					if (eq < 0) continue;
					key = line.Substring(0, eq).Trim();
					rest = line.Substring(eq);
				}
				if (rest.Length == 0 || rest[0] != '=') continue;
				rest = rest.Substring(1).TrimStart();
				if (rest.Length < 2 || (rest[0] != '"' && rest[0] != '\'')) continue;
				int valueEnd = rest.IndexOf(rest[0], 1);
				if (valueEnd < 0) continue;
				found[key] = rest.Substring(1, valueEnd - 1);
			}
			return found;
		}

		static string Unquote(string s) => s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0] ? s.Substring(1, s.Length - 2) : s;

		internal static Version Parse(string s)
		{
			if (string.IsNullOrEmpty(s)) return null;
			s = s.Trim().TrimStart('v', 'V');
			int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
			if (cut >= 0) s = s.Substring(0, cut);
			if (s.IndexOf('.') < 0) s += ".0";
			if (!Version.TryParse(s, out var v)) return null;
			return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
		}
	}

	// The file keeps every value, a new default in code never reaches an existing install.
	internal sealed class SettingsMigration
	{
		internal string Entry, ChangedIn;
		internal Func<MelonPreferences_Entry, bool> AtOld;
		internal Action<MelonPreferences_Entry> ToNew;

		internal static SettingsMigration Default<T>(string entry, string changedIn, T oldDefault) => new SettingsMigration
		{
			Entry = entry, ChangedIn = changedIn,
			AtOld = e => e is MelonPreferences_Entry<T> t && Same(t.Value, oldDefault),
			ToNew = e => { var t = (MelonPreferences_Entry<T>)e; t.Value = t.DefaultValue; },
		};

		static bool Same(object a, object b) =>
			a is float fa && b is float fb ? Math.Abs(fa - fb) < 1e-6f
			: a is double da && b is double db ? Math.Abs(da - db) < 1e-6
			: Equals(a, b);
	}
}
