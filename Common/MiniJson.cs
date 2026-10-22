using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace T90A.Common
{
	public static class MiniJson
	{
		public sealed class Obj : Dictionary<string, object>
		{
			public new void Add(string k, object v) { this[k] = v; }
		}

		public static object Parse(string text)
		{
			int i = 0;
			var v = Value(text, ref i);
			Skip(text, ref i);
			if (i < text.Length) throw Err(text, i, "trailing characters");
			return v;
		}

		static Exception Err(string s, int i, string what)
		{
			int line = 1; for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
			return new FormatException($"JSON: {what} at line {line}");
		}

		static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

		static object Value(string s, ref int i)
		{
			Skip(s, ref i);
			if (i >= s.Length) throw Err(s, i, "unexpected end");
			char c = s[i];
			if (c == '{')
			{
				var o = new Obj(); i++;
				Skip(s, ref i);
				if (i < s.Length && s[i] == '}') { i++; return o; }
				while (true)
				{
					Skip(s, ref i);
					if (i >= s.Length || s[i] != '"') throw Err(s, i, "expected a key");
					string k = Str(s, ref i);
					Skip(s, ref i);
					if (i >= s.Length || s[i] != ':') throw Err(s, i, "expected ':'");
					i++;
					o.Add(k, Value(s, ref i));
					Skip(s, ref i);
					if (i < s.Length && s[i] == ',') { i++; continue; }
					if (i < s.Length && s[i] == '}') { i++; return o; }
					throw Err(s, i, "expected ',' or '}'");
				}
			}
			if (c == '[')
			{
				var a = new List<object>(); i++;
				Skip(s, ref i);
				if (i < s.Length && s[i] == ']') { i++; return a; }
				while (true)
				{
					a.Add(Value(s, ref i));
					Skip(s, ref i);
					if (i < s.Length && s[i] == ',') { i++; continue; }
					if (i < s.Length && s[i] == ']') { i++; return a; }
					throw Err(s, i, "expected ',' or ']'");
				}
			}
			if (c == '"') return Str(s, ref i);
			if (Word(s, ref i, "true")) return true;
			if (Word(s, ref i, "false")) return false;
			if (Word(s, ref i, "null")) return null;
			int start = i;
			while (i < s.Length && (char.IsDigit(s[i]) || "+-.eE".IndexOf(s[i]) >= 0)) i++;
			if (i == start) throw Err(s, i, $"unexpected '{c}'");
			return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		static bool Word(string s, ref int i, string w)
		{
			if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
			i += w.Length; return true;
		}

		static string Str(string s, ref int i)
		{
			var sb = new StringBuilder(); i++;
			while (i < s.Length)
			{
				char c = s[i++];
				if (c == '"') return sb.ToString();
				if (c != '\\') { sb.Append(c); continue; }
				char e = s[i++];
				switch (e)
				{
					case 'n': sb.Append('\n'); break;
					case 't': sb.Append('\t'); break;
					case 'r': sb.Append('\r'); break;
					case 'b': sb.Append('\b'); break;
					case 'f': sb.Append('\f'); break;
					case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
					default: sb.Append(e); break;
				}
			}
			throw Err(s, i, "unterminated string");
		}
	}
}
