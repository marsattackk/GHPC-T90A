using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// Exceptions in my MonoBehaviours and coroutines only reach Unity's own log, where nobody looks.
	internal static class UnityErrors
	{
		// Every port compiles this class: one table in AppDomain data, mscorlib types only, the first copy hooks the event.
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
					typeof(UnityErrors).Assembly.ManifestModule.ModuleVersionId.ToString("N"), (Action<string>)Write };
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
			catch (Exception e) { MelonLogger.Warning("Unity error copy not installed: " + e.Message); }
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
				key += "|" + ((stack ?? "").Split('\n').FirstOrDefault(l => l.Contains("T90A.Common")) ?? "").Trim();
				int n = Counts.TryGetValue(key, out int had) ? had : 0;
				if (n >= PerMessage) return;
				Counts[key] = ++n;
				_total++;
				var lines = (stack ?? "").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
				string s = $"Unity {type.ToString().ToLowerInvariant()} in our code: {text}\n  {string.Join("\n  ", lines.Take(StackLines))}"
					+ (lines.Length > StackLines ? $"\n  ... {lines.Length - StackLines} more line(s)" : "")
					+ (n == PerMessage ? $"\n  ({PerMessage} copies of this message logged; not logging it again)" : "")
					+ (_total == Total ? $"\n  ({Total} Unity errors from our code logged; no more this session)" : "");
				((Action<string>)sink[3])(s);
			}
			catch { }
			finally { _inside = false; }
		}

		// Both ports compile the shared namespace, the module id in the frame tells them apart.
		static object[] Owner(string stack)
		{
			if (string.IsNullOrEmpty(stack)) return null;
			object[][] sinks;
			lock (_sinks) sinks = _sinks.ToArray();
			foreach (var raw in stack.Split('\n'))
			{
				var line = raw.TrimStart();
				if (line.StartsWith("at ")) line = line.Substring(3);
				foreach (var k in sinks) if (line.StartsWith((string)k[0], StringComparison.Ordinal)) return k;
				object[] first = null;
				foreach (var k in sinks)
				{
					if (!line.StartsWith((string)k[1], StringComparison.Ordinal)) continue;
					if (line.IndexOf((string)k[2], StringComparison.OrdinalIgnoreCase) >= 0) return k;
					if (first == null) first = k;
				}
				if (first != null) return first;
			}
			return null;
		}
	}
}
