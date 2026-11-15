using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Logs the version stamp with the game version and Steam build. Not called BuildInfo because MelonLoader already has one.
	internal static class BuildStamp
	{
		internal static string Text
			=> typeof(BuildStamp).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false) is object[] attributes
				&& attributes.Length > 0
				? ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion : "unstamped";

		internal static void Log(string name) => MelonLogger.Msg($"{name} {Text}, game {GameVersion()}, {SteamBuild()}");

		static string GameVersion()
		{
			try { return string.IsNullOrEmpty(Application.version) ? "version unknown" : Application.version; }
			catch { return "version unknown"; }
		}

		static string SteamBuild()
		{
			try
			{
				for (var dir = new DirectoryInfo(Application.dataPath).Parent; dir != null; dir = dir.Parent)
				{
					string manifest = Path.Combine(dir.FullName, "appmanifest_1705180.acf");
					if (!File.Exists(manifest)) continue;
					Match match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
					return match.Success ? "Steam build " + match.Groups[1].Value : "Steam build unknown";
				}
				return "no Steam manifest";
			}
			catch { return "Steam build unreadable"; }
		}
	}
}
