using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Exceptions in my MonoBehaviours and coroutines only reach Unity's own log, where nobody looks. This copies them
	// to the console.
	internal static class UnityErrors
	{
		// Every port compiles this class. The table lives in AppDomain data (mscorlib types only) and
		// whichever copy loads first hooks the event.
		const string Key = "GHPCMods.UnityErrors.v1";
		internal const int PerMessage = 3, Total = 60;
		const int StackLines = 12;

		static List<object[]> _sinks;
		static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
		static int _total;
		[ThreadStatic] static bool _inside;

		internal static void Install(string portNamespace)
		{
			try
			{
				var sink = new object[] { portNamespace + ".", typeof(UnityErrors).Namespace + ".",
					typeof(UnityErrors).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
					(Action<string>)Write };
				var had = AppDomain.CurrentDomain.GetData(Key);
				if (had is List<object[]> list) { lock (list) list.Add(sink); }
				else
				{
					_sinks = new List<object[]> { sink };
					if (had == null) AppDomain.CurrentDomain.SetData(Key, _sinks);
					else MelonLogger.Warning("Unity error copy: another port's table has another layout; this port keeps its own.");
					Application.logMessageReceived += OnLog;
				}
			}
			catch (Exception exception) { MelonLogger.Warning("Unity error copy not installed: " + exception.Message); }
		}

		// MelonLoader names the mod from the calling frame.
		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Write(string s) => MelonLogger.Error(s);

		static void OnLog(string text, string stack, LogType type)
		{
			if (_inside || type == LogType.Log || type == LogType.Warning) return;
			_inside = true;
			try
			{
				if (_total >= Total) return;
				var sink = Owner(stack);
				if (sink == null) return;
				string key = text ?? "";
				if (key.Length > 300) key = key.Substring(0, 300);
				string portCode = (string)sink[0];
				string sharedCode = (string)sink[1];
				string place = (stack ?? "").Split('\n').FirstOrDefault(l => l.Contains(portCode) || l.Contains(sharedCode));
				key += "|" + (place ?? "").Trim();
				int count = Counts.TryGetValue(key, out int had) ? had : 0;
				if (count >= PerMessage) return;
				Counts[key] = ++count;
				_total++;
				var lines = (stack ?? "").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
				string message =
					$"Unity {type.ToString().ToLowerInvariant()} in our code: {text}\n  {string.Join("\n  ", lines.Take(StackLines))}"
						+ (lines.Length > StackLines ? $"\n  ... {lines.Length - StackLines} more line(s)" : "")
						+ (count == PerMessage ? $"\n  ({PerMessage} copies of this message logged; not logging it again)" : "")
						+ (_total == Total ? $"\n  ({Total} Unity errors from our code logged; no more this session)" : "");
				((Action<string>)sink[3])(message);
			}
			catch { }
			finally { _inside = false; }
		}

		// The module id in the frame says which port's copy it is.
		static object[] Owner(string stack)
		{
			if (string.IsNullOrEmpty(stack)) return null;
			object[][] sinks;
			lock (_sinks) sinks = _sinks.ToArray();
			foreach (var raw in stack.Split('\n'))
			{
				var line = raw.TrimStart();
				if (line.StartsWith("at ")) line = line.Substring(3);
				foreach (var sink in sinks) if (line.StartsWith((string)sink[0], StringComparison.Ordinal)) return sink;
				object[] first = null;
				foreach (var sink in sinks)
				{
					if (!line.StartsWith((string)sink[1], StringComparison.Ordinal)) continue;
					if (line.IndexOf((string)sink[2], StringComparison.OrdinalIgnoreCase) >= 0) return sink;
					if (first == null) first = sink;
				}
				if (first != null) return first;
			}
			return null;
		}
	}
}
