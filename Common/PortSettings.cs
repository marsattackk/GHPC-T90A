using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader;
using MelonLoader.Utils;

namespace T90A.Common
{
	// Rescues numbers saved as text, stamps the mod version and moves settings still at an old default.
	internal static class PortSettings
	{
		internal const string VersionEntry = "ConfigVersion";
		static readonly Type[] Numbers = { typeof(float), typeof(double), typeof(int), typeof(long) };

		static string _file = Path.Combine(MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg");

		internal static void Finish(MelonPreferences_Category cfg, string modVersion, params SettingsMigration[] migrations)
		{
			try { Rescue(cfg); }
			catch (Exception exception)
			{
				MelonLogger.Warning($"{cfg.DisplayName} settings: could not read the settings file for numbers saved as text ({exception.GetType().Name}: {exception.Message}).");
			}
			var version = cfg.CreateEntry(
				VersionEntry,
				"",
				description: "The mod version that last wrote these settings (a newer version uses it to move settings still at an old default to the new one). Leave it as it is.",
				is_hidden: true);
			var last = Parse(version.Value) ?? new Version(0, 0, 0, 0);
			foreach (var migration in migrations)
			{
				var changed = Parse(migration.ChangedIn);
				var entry = cfg.GetEntry(migration.Entry);
				string broken = migration.Broken(entry, changed);
				if (broken != null)
				{
					Say($"{cfg.DisplayName} settings: the default change of \"{migration.Entry}\" in {migration.ChangedIn} does nothing: {broken}.", true);
					continue;
				}
				if (last >= changed || !migration.AtOld(entry)) continue;
				string old = entry.GetValueAsString();
				migration.ToNew(entry);
				Say(
					$"{cfg.DisplayName} settings: \"{migration.Entry}\" was still {old}, the default before {migration.ChangedIn}; now {entry.GetValueAsString()}, the new default.",
					false);
			}
			if (version.Value != modVersion) version.Value = modVersion;
		}

		// A quoted "0,3" from a decimal-comma Windows: MelonLoader silently keeps the default and saves it over the
		// player's value at exit.
		static void Rescue(MelonPreferences_Category cfg)
		{
			if (!File.Exists(_file)) return;
			var text = QuotedValues(File.ReadAllLines(_file), cfg.Identifier);
			foreach (var entry in cfg.Entries)
			{
				var type = entry.GetReflectedType();
				if (!Numbers.Contains(type) || !text.TryGetValue(entry.Identifier, out string rawValue)) continue;
				object value = Number(rawValue, type);
				if (value == null)
				{
					Say(
						$"{cfg.DisplayName} settings: \"{entry.Identifier}\" is saved as \"{rawValue}\", which is not a number; using the default {entry.GetDefaultValueAsString()}.",
						true);
					continue;
				}
				entry.BoxedValue = value;
				Say(
					$"{cfg.DisplayName} settings: \"{entry.Identifier}\" was saved as text \"{rawValue}\"; read as {entry.GetValueAsString()} (it is saved back as a number when the game closes).",
					false);
			}
		}

		static void Say(string s, bool warn)
		{
			if (warn) MelonLogger.Warning(s);
			else MelonLogger.Msg(s);
		}

		internal static object Number(string s, Type type)
		{
			s = s.Trim();
			if (s.Count(c => c == ',' || c == '.') > 1) return null;
			if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
				|| double.IsNaN(parsed)
				|| double.IsInfinity(parsed))
				return null;
			if (type == typeof(float)) return Math.Abs(parsed) <= float.MaxValue ? (object)(float)parsed : null;
			if (type == typeof(double)) return parsed;
			if (parsed != Math.Floor(parsed)) return null;
			if (type == typeof(int)) return parsed >= int.MinValue && parsed <= int.MaxValue ? (object)(int)parsed : null;
			return parsed >= long.MinValue && parsed <= long.MaxValue ? (object)(long)parsed : null;
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
					int equalsIndex = line.IndexOf('=');
					if (equalsIndex < 0) continue;
					key = line.Substring(0, equalsIndex).Trim();
					rest = line.Substring(equalsIndex);
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

		static string Unquote(string s)
			=> s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0] ? s.Substring(1, s.Length - 2) : s;

		internal static Version Parse(string s)
		{
			if (string.IsNullOrEmpty(s)) return null;
			s = s.Trim().TrimStart('v', 'V');
			int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
			if (cut >= 0) s = s.Substring(0, cut);
			if (s.IndexOf('.') < 0) s += ".0";
			if (!Version.TryParse(s, out var version)) return null;
			return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
		}

		internal static bool IsNewer(string latest, string installed)
		{
			var latestVersion = Parse(latest);
			var installedVersion = Parse(installed);
			if (latestVersion == null || installedVersion == null) return false;
			if (latestVersion != installedVersion) return latestVersion > installedVersion;
			return ComparePreRelease(PreRelease(latest), PreRelease(installed)) > 0;
		}

		static string PreRelease(string s)
		{
			s = s.Trim();
			int plus = s.IndexOf('+');
			if (plus >= 0) s = s.Substring(0, plus);
			int dash = s.IndexOf('-');
			return dash < 0 || dash == s.Length - 1 ? null : s.Substring(dash + 1);
		}

		static int ComparePreRelease(string a, string b)
		{
			if (a == b) return 0;
			if (a == null) return 1;
			if (b == null) return -1;
			var partsA = a.Split('.');
			var partsB = b.Split('.');
			for (int i = 0; i < Math.Min(partsA.Length, partsB.Length); i++)
			{
				bool numberA = int.TryParse(partsA[i], NumberStyles.None, CultureInfo.InvariantCulture, out int valueA);
				bool numberB = int.TryParse(partsB[i], NumberStyles.None, CultureInfo.InvariantCulture, out int valueB);
				int order = numberA && numberB ? valueA.CompareTo(valueB)
					: numberA ? -1
					: numberB ? 1
					: string.CompareOrdinal(partsA[i], partsB[i]);
				if (order != 0) return order;
			}
			return partsA.Length.CompareTo(partsB.Length);
		}
	}

	// A new default in code never reaches an existing install, because the file keeps every value.
	internal sealed class SettingsMigration
	{
		internal string Entry, ChangedIn;
		internal Type Kind;
		internal Func<MelonPreferences_Entry, bool> AtOld;
		internal Action<MelonPreferences_Entry> ToNew;

		internal string Broken(MelonPreferences_Entry entry, Version changed)
		{
			if (changed == null) return $"\"{ChangedIn}\" is not a version";
			if (entry == null) return "no setting has that name";
			var type = entry.GetReflectedType();
			if (Kind != null && type != Kind) return $"the setting holds {type.Name}, the rule gives a {Kind.Name}";
			return null;
		}

		// T is the entry's own type. 0.6 on a float entry is a double and never matches (Finish warns about it).
		internal static SettingsMigration Default<T>(string entry, string changedIn, T oldDefault) => new SettingsMigration
		{
			Entry = entry,
			ChangedIn = changedIn,
			Kind = typeof(T),
			AtOld = s => s is MelonPreferences_Entry<T> typed && Same(typed.Value, oldDefault),
			ToNew = setting =>
			{
				var typed = (MelonPreferences_Entry<T>)setting;
				typed.Value = typed.DefaultValue;
			},
		};

		static bool Same(object a, object b) =>
			a is float floatA && b is float floatB ? Math.Abs(floatA - floatB) < 1e-6f
			: a is double doubleA && b is double doubleB ? Math.Abs(doubleA - doubleB) < 1e-6
			: Equals(a, b);
	}
}
