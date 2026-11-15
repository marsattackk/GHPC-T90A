using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace T90A.Common
{
	// JSON for the reticle files: keys stay in file order, comments are allowed, a duplicate key is refused.
	internal sealed partial class ReticleSpec
	{
		internal sealed class JObj
		{
			internal readonly List<KeyValuePair<string, object>> Items;
			internal JObj(IEnumerable<KeyValuePair<string, object>> items) { Items = items.ToList(); }

			internal object this[string k]
			{
				get
				{
					foreach (var pair in Items) if (pair.Key == k) return pair.Value;
					return null;
				}
			}

			internal bool Has(string k) => Items.Any(p => p.Key == k);
			internal string Str(string k) => this[k] as string;
		}

		sealed class Json
		{
			readonly string _s, _src;
			int _i;
			internal Json(string s, string src)
			{
				_s = s;
				_src = src;
			}

			FormatException Err(string what)
			{
				int line = 1 + _s.Take(Math.Min(_i, _s.Length)).Count(c => c == '\n');
				return new FormatException($"{_src}: line {line}: {what}");
			}

			void Ws()
			{
				while (_i < _s.Length)
				{
					if (char.IsWhiteSpace(_s[_i])) _i++;
					else if (_s[_i] == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (_i < _s.Length && _s[_i] != '\n') _i++; }
					else if (_s[_i] == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
					{
						int j = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
						_i = j < 0 ? _s.Length : j + 2;
					}
					else break;
				}
			}

			internal object Read()
			{
				var result = Val();
				Ws();
				if (_i != _s.Length) throw Err("text after the end");
				return result;
			}

			char Cur(string what = "unexpected end") => _i < _s.Length ? _s[_i] : throw Err(what);

			object Val()
			{
				Ws();
				if (_i >= _s.Length) throw Err("unexpected end");
				char character = _s[_i];
				if (character == '{')
				{
					_i++;
					var items = new List<KeyValuePair<string, object>>();
					Ws();
					if (Cur() == '}')
					{
						_i++;
						return new JObj(items);
					}
					while (true)
					{
						Ws();
						if (Cur() == '}')
						{
							_i++;
							return new JObj(items);
						}
						string key = Str();
						Ws();
						if (Cur() != ':') throw Err("':' expected");
						_i++;
						if (items.Any(p => p.Key == key)) throw Err($"key '{key}' twice");
						items.Add(new KeyValuePair<string, object>(key, Val()));
						Ws();
						if (Cur() == ',')
						{
							_i++;
							continue;
						}
						if (Cur() == '}')
						{
							_i++;
							return new JObj(items);
						}
						throw Err("',' or '}' expected");
					}
				}
				if (character == '[')
				{
					_i++;
					var list = new List<object>();
					while (true)
					{
						Ws();
						if (Cur() == ']')
						{
							_i++;
							return list;
						}
						list.Add(Val());
						Ws();
						if (Cur() == ',')
						{
							_i++;
							continue;
						}
						if (Cur() == ']')
						{
							_i++;
							return list;
						}
						throw Err("',' or ']' expected");
					}
				}
				if (character == '"') return Str();
				if (_s.Substring(_i).StartsWith("true"))
				{
					_i += 4;
					return true;
				}
				if (_s.Substring(_i).StartsWith("false"))
				{
					_i += 5;
					return false;
				}
				if (_s.Substring(_i).StartsWith("null"))
				{
					_i += 4;
					return null;
				}
				int j = _i;
				while (j < _s.Length && "+-0123456789.eE".IndexOf(_s[j]) >= 0) j++;
				if (j == _i) throw Err($"unexpected '{character}'");
				var num = double.Parse(_s.Substring(_i, j - _i), NumberStyles.Float, CultureInfo.InvariantCulture);
				_i = j;
				return num;
			}

			string Str()
			{
				if (Cur() != '"') throw Err("string expected");
				var builder = new StringBuilder();
				_i++;
				while (Cur("unclosed string") != '"')
				{
					if (_s[_i] == '\\')
					{
						_i++;
						char escape = Cur("unclosed string");
						if (escape == 'n') builder.Append('\n');
						else if (escape == 't') builder.Append('\t');
						else if (escape == 'u')
						{
							if (_i + 4 >= _s.Length) throw Err("unclosed string");
							builder.Append((char)Convert.ToInt32(_s.Substring(_i + 1, 4), 16));
							_i += 4;
						}
						else builder.Append(escape);
					}
					else builder.Append(_s[_i]);
					_i++;
				}
				_i++;
				return builder.ToString();
			}
		}
	}
}
