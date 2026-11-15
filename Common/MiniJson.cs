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
			public new readonly List<string> Keys = new List<string>();
			public new void Add(string k, object v)
			{
				if (!ContainsKey(k)) Keys.Add(k);
				this[k] = v;
			}
		}

		public static object Parse(string text)
		{
			int i = 0;
			var parsed = Value(text, ref i);
			Skip(text, ref i);
			if (i < text.Length) throw Err(text, i, "trailing characters");
			return parsed;
		}

		static Exception Err(string s, int i, string what)
		{
			int line = 1;
			for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
			return new FormatException($"JSON: {what} at line {line}");
		}

		static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

		static object Value(string s, ref int i)
		{
			Skip(s, ref i);
			if (i >= s.Length) throw Err(s, i, "unexpected end");
			char current = s[i];
			if (current == '{')
			{
				var objectValue = new Obj();
				i++;
				Skip(s, ref i);
				if (i < s.Length && s[i] == '}')
				{
					i++;
					return objectValue;
				}
				while (true)
				{
					Skip(s, ref i);
					if (i >= s.Length || s[i] != '"') throw Err(s, i, "expected a key");
					string key = Str(s, ref i);
					Skip(s, ref i);
					if (i >= s.Length || s[i] != ':') throw Err(s, i, "expected ':'");
					i++;
					objectValue.Add(key, Value(s, ref i));
					Skip(s, ref i);
					if (i < s.Length && s[i] == ',')
					{
						i++;
						continue;
					}
					if (i < s.Length && s[i] == '}')
					{
						i++;
						return objectValue;
					}
					throw Err(s, i, "expected ',' or '}'");
				}
			}
			if (current == '[')
			{
				var arrayValue = new List<object>();
				i++;
				Skip(s, ref i);
				if (i < s.Length && s[i] == ']')
				{
					i++;
					return arrayValue;
				}
				while (true)
				{
					arrayValue.Add(Value(s, ref i));
					Skip(s, ref i);
					if (i < s.Length && s[i] == ',')
					{
						i++;
						continue;
					}
					if (i < s.Length && s[i] == ']')
					{
						i++;
						return arrayValue;
					}
					throw Err(s, i, "expected ',' or ']'");
				}
			}
			if (current == '"') return Str(s, ref i);
			if (Word(s, ref i, "true")) return true;
			if (Word(s, ref i, "false")) return false;
			if (Word(s, ref i, "null")) return null;
			int start = i;
			while (i < s.Length && (char.IsDigit(s[i]) || "+-.eE".IndexOf(s[i]) >= 0)) i++;
			if (i == start) throw Err(s, i, $"unexpected '{current}'");
			return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		static bool Word(string s, ref int i, string w)
		{
			if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
			i += w.Length;
			return true;
		}

		static string Str(string s, ref int i)
		{
			var builder = new StringBuilder();
			i++;
			while (i < s.Length)
			{
				char current = s[i++];
				if (current == '"') return builder.ToString();
				if (current != '\\')
				{
					builder.Append(current);
					continue;
				}
				if (i >= s.Length) break;
				char escape = s[i++];
				switch (escape)
				{
					case 'n':
						builder.Append('\n');
						break;
					case 't':
						builder.Append('\t');
						break;
					case 'r':
						builder.Append('\r');
						break;
					case 'b':
						builder.Append('\b');
						break;
					case 'f':
						builder.Append('\f');
						break;
					case 'u':
						if (i + 4 > s.Length) throw Err(s, i, "unterminated string");
						builder.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
						i += 4;
						break;
					default:
						builder.Append(escape);
						break;
				}
			}
			throw Err(s, i, "unterminated string");
		}
	}
}
