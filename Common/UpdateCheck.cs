using System;
using System.Collections;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;

namespace T90A.Common
{
	internal static class UpdateCheck
	{
		internal enum Outcome { Newer, UpToDate, NoReply }

		internal sealed class Result
		{
			internal Outcome Outcome;
			internal string Latest, Via, Why;
			internal float Seconds;
			public override string ToString()
				=> $"{Outcome}{(Latest != null ? " " + Latest : "")}{(Via != null ? " via " + Via : "")}{(Why != null ? " (" + Why + ")" : "")} in {Seconds:0.0} s";
		}

		internal const int TimeoutSeconds = 15;
		static string _name, _version, _repo;
		static MelonPreferences_Entry<bool> _enabled;
		static bool _started;

		internal static string ReleasesPage(string repo) => $"https://github.com/{repo}/releases";
		static string ApiUrl(string repo) => $"https://api.github.com/repos/{repo}/releases/latest";
		static string LatestPage(string repo) => $"https://github.com/{repo}/releases/latest";

		internal static void Config(MelonPreferences_Category cfg, string friendlyName, string modVersion, string repo)
		{
			_name = friendlyName;
			_version = modVersion;
			_repo = repo;
			if (repo == null) return;
			_enabled = cfg.CreateEntry(
				"Check for updates",
				true,
				description: $"true (default): once per game start, asks GitHub whether a newer {friendlyName} release is out and, if so, says so in the MelonLoader console (nothing is downloaded or installed). false = no check.");
		}

		internal static void OnSceneLoaded(bool isMenu)
		{
			if (!isMenu || _started || _repo == null || _enabled == null || !_enabled.Value) return;
			_started = true;
			MelonCoroutines.Start(Run());
		}

		static IEnumerator Run()
		{
			Result result = null;
			yield return Ask(_repo, _version, ApiUrl(_repo), LatestPage(_repo), r => result = r);
			if (result == null) yield break;
			if (result.Outcome == Outcome.Newer)
				MelonLogger.Msg($"{_name} {result.Latest} is out (you have {_version}): {ReleasesPage(_repo)}");
		}

		internal static IEnumerator Ask(string repo, string installed, string apiUrl, string pageUrl, Action<Result> done)
		{
			float startTime = Time.realtimeSinceStartup;
			string tag = null, via = null, why = null;
			yield return Get(apiUrl, false, (code, body, location, error) =>
			{
				var match = body != null ? Regex.Match(body, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"") : Match.Empty;
				if (code == 200 && match.Success)
				{
					tag = match.Groups[1].Value;
					via = "API";
				}
				else why = "API " + (error ?? "HTTP " + code);
			});
			// The API has an hourly limit per address but the release page's redirect still answers past it.
			if (tag == null)
			{
				var tagIn = new Regex("/" + Regex.Escape(repo) + "/releases/tag/([^\"'?#<>\\s/]+)", RegexOptions.IgnoreCase);
				yield return Get(pageUrl, true, (code, body, location, error) =>
				{
					var match = location != null ? tagIn.Match(location) : Match.Empty;
					if (!match.Success && body != null) match = tagIn.Match(body);
					if (match.Success)
					{
						tag = Uri.UnescapeDataString(match.Groups[1].Value);
						via = "release page";
						why = null;
					}
					else why += "; release page " + (error ?? "HTTP " + code) + (location != null ? " to " + location : "");
				});
			}
			var result = new Result { Via = via, Why = why, Seconds = Time.realtimeSinceStartup - startTime };
			if (PortSettings.Parse(tag) == null || PortSettings.Parse(installed) == null)
			{
				result.Outcome = Outcome.NoReply;
				result.Why = why ?? $"not a version: '{tag}' / '{installed}'";
			}
			else
			{
				result.Latest = tag.TrimStart('v', 'V');
				result.Outcome = PortSettings.IsNewer(tag, installed) ? Outcome.Newer : Outcome.UpToDate;
			}
			try { done(result); }
			catch { }
		}

		static IEnumerator Get(string url, bool noRedirect, Action<long, string, string, string> done)
		{
			UnityWebRequest req = null;
			UnityWebRequestAsyncOperation operation = null;
			string error = null;
			try
			{
				req = UnityWebRequest.Get(url);
				req.timeout = TimeoutSeconds;
				if (noRedirect) req.redirectLimit = 0;
				req.SetRequestHeader(
					"User-Agent",
					(_name ?? "GHPC mod").Replace(" ", "") + "/" + (_version ?? "0") + " (GHPC mod update check)");
				req.SetRequestHeader("Accept", "application/vnd.github+json");
				operation = req.SendWebRequest();
			}
			catch (Exception exception) { error = exception.GetType().Name; }
			float until = Time.realtimeSinceStartup + TimeoutSeconds + 5f;
			while (operation != null && !operation.isDone && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			long code = 0;
			string body = null, location = null;
			try
			{
				if (operation != null && !operation.isDone)
				{
					req.Abort();
					error = "no answer in " + TimeoutSeconds + " s";
				}
				else if (req != null && error == null)
				{
					code = req.responseCode;
					location = req.GetResponseHeader("Location");
					if (req.result == UnityWebRequest.Result.Success) body = req.downloadHandler?.text;
					else if (location == null) error = req.error ?? req.result.ToString();
				}
			}
			catch (Exception exception) { error = exception.GetType().Name; }
			finally
			{
				try { req?.Dispose(); }
				catch { }
			}
			try { done(code, body, location, error); }
			catch { }
		}
	}
}
