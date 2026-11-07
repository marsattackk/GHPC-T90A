using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Not BuildInfo, MelonLoader already has one.
	internal static class BuildStamp
	{
		internal static string Text => typeof(BuildStamp).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false) is object[] a && a.Length > 0
			? ((AssemblyInformationalVersionAttribute)a[0]).InformationalVersion : "unstamped";

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
					Match m = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
					return m.Success ? "Steam build " + m.Groups[1].Value : "Steam build unknown";
				}
				return "no Steam manifest";
			}
			catch { return "Steam build unreadable"; }
		}
	}
}
