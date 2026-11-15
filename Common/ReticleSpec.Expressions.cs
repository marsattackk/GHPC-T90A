using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace T90A.Common
{
	// The reticle files' expression language: names, lists, ranges, lambdas and the usual operators.
	internal sealed partial class ReticleSpec
	{
		void Unknown(JObj o, string[] allowed, string where)
		{
			foreach (var pair in o.Items) if (!allowed.Contains(pair.Key)) Problems.Add($"{where}: unknown key '{pair.Key}'");
		}

		List<object> List(object v, string where) => v as List<object> ?? throw new FormatException($"{Source}: {where}: expected a list");

		object Value(object v, Env env, string where)
		{
			switch (v)
			{
				case string text: return Expr.Parse(text, $"{Source}: {where}").Eval(env);
				case List<object> list: return list.Select(i => Value(i, env, where)).ToList();
				default: return v;
			}
		}

		double Num(object v, Env env, string where) => ToNum(Value(v, env, where), $"{Source}: {where}");

		float[] Floats(object v, Env env, string where)
		{
			var evaluated = Value(v, env, where);
			if (!(evaluated is List<object> list)) throw new FormatException($"{Source}: {where}: expected a list of numbers");
			return list.Select(i => (float)ToNum(i, $"{Source}: {where}")).ToArray();
		}

		string Interp(object v, Env env, string where)
		{
			if (!(v is string text)) return v is double numberValue ? Fmt(numberValue) : v?.ToString();
			var builder = new StringBuilder();
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] == '{')
				{
					int j = text.IndexOf('}', i);
					if (j < 0) throw new FormatException($"{Source}: {where}: unclosed '{{' in \"{text}\"");
					var result = Expr.Parse(text.Substring(i + 1, j - i - 1), $"{Source}: {where}").Eval(env);
					builder.Append(result is double number ? Fmt(number) : result is bool flag ? (flag ? "1" : "0") : result?.ToString());
					i = j;
				}
				else builder.Append(text[i]);
			}
			return builder.ToString();
		}

		internal static string Fmt(double d)
		{
			if (Math.Abs(d - Math.Round(d)) < 1e-9) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
			return d.ToString("0.######", CultureInfo.InvariantCulture);
		}

		static double ToNum(object v, string where)
		{
			switch (v)
			{
				case double number: return number;
				case bool flag: return flag ? 1 : 0;
			}
			throw new FormatException($"{where}: expected a number, got {(v == null ? "nothing" : v is List<object> ? "a list" : "'" + v + "'")}");
		}

		static bool Truth(object v) => v is bool flag ? flag : v is double number ? number != 0 : v != null;

		internal sealed class Env
		{
			readonly Env _up;
			readonly Dictionary<string, object> _v = new Dictionary<string, object>();
			internal Env(Env up) { _up = up; }

			internal void Set(string k, object v) => _v[k] = v;
			internal bool TryGet(string k, out object v)
			{
				for (var scope = this; scope != null; scope = scope._up) if (scope._v.TryGetValue(k, out v)) return true;
				v = null;
				return false;
			}

			internal object Get(string k) => TryGet(k, out var found) ? found : throw new FormatException($"unknown name '{k}'");
		}

		sealed class Fn
		{
			internal string[] Params;
			internal Expr Body;
			internal Env Closure;
			internal Func<double[], double> Builtin;
		}

		static readonly Dictionary<string, Fn> Builtins = new Dictionary<string, Fn>
		{
			{ "sqrt", new Fn { Builtin = a => Math.Sqrt(a[0]) } },
			{ "abs", new Fn { Builtin = a => Math.Abs(a[0]) } },
			{ "sin", new Fn { Builtin = a => Math.Sin(a[0]) } },
			{ "cos", new Fn { Builtin = a => Math.Cos(a[0]) } },
			{ "tan", new Fn { Builtin = a => Math.Tan(a[0]) } },
			{ "atan", new Fn { Builtin = a => Math.Atan(a[0]) } },
			{ "atan2", new Fn { Builtin = a => Math.Atan2(a[0], a[1]) } },
			{ "deg", new Fn { Builtin = a => a[0] * 180 / Math.PI } },
			{ "rad", new Fn { Builtin = a => a[0] * Math.PI / 180 } },
			{ "floor", new Fn { Builtin = a => Math.Floor(a[0]) } },
			{ "ceil", new Fn { Builtin = a => Math.Ceiling(a[0]) } },
			{ "round", new Fn { Builtin = a => Math.Round(a[0], MidpointRounding.AwayFromZero) } },
			{ "min", new Fn { Builtin = a => a.Min() } },
			{ "max", new Fn { Builtin = a => a.Max() } },
		};

		internal abstract class Expr
		{
			internal abstract object Eval(Env env);

			static readonly Dictionary<string, Expr> Cache = new Dictionary<string, Expr>();

			internal static Expr Parse(string s, string where)
			{
				if (Cache.TryGetValue(s, out var expression)) return expression;
				var parser = new Parser(s, where);
				expression = parser.Top();
				parser.End();
				return Cache[s] = expression;
			}

			internal sealed class Lit : Expr
			{
				internal object V;
				internal override object Eval(Env env) => V;
			}

			internal sealed class Name : Expr
			{
				internal string N, Where;
				internal override object Eval(Env env)
				{
					if (env.TryGet(N, out var bound)) return bound;
					if (Builtins.TryGetValue(N, out var builtin)) return builtin;
					throw new FormatException($"{Where}: unknown name '{N}'");
				}
			}

			internal sealed class ListE : Expr
			{
				internal List<Expr> Items;
				internal override object Eval(Env env) => Items.Select(i => i.Eval(env)).ToList();
			}

			internal sealed class Lambda : Expr
			{
				internal string[] Params;
				internal Expr Body;
				internal override object Eval(Env env) => new Fn { Params = Params, Body = Body, Closure = env };
			}

			internal sealed class Call : Expr
			{
				internal Expr F;
				internal List<Expr> Args;
				internal string Where;
				internal override object Eval(Env env)
				{
					var function = F.Eval(env) as Fn ?? throw new FormatException($"{Where}: not a function");
					var arguments = Args.Select(a => a.Eval(env)).ToList();
					if (function.Builtin != null) return function.Builtin(arguments.Select(a => ToNum(a, Where)).ToArray());
					if (arguments.Count != function.Params.Length)
						throw new FormatException($"{Where}: {function.Params.Length} argument(s) expected, {arguments.Count} given");
					var scope = new Env(function.Closure);
					for (int i = 0; i < arguments.Count; i++) scope.Set(function.Params[i], arguments[i]);
					return function.Body.Eval(scope);
				}
			}

			internal sealed class Index : Expr
			{
				internal Expr L, I;
				internal string Where;
				internal override object Eval(Env env)
				{
					var list = L.Eval(env) as List<object> ?? throw new FormatException($"{Where}: indexing something that is not a list");
					int i = (int)ToNum(I.Eval(env), Where);
					if (i < 0) i += list.Count;
					if (i < 0 || i >= list.Count) throw new FormatException($"{Where}: index {i} outside the list (length {list.Count})");
					return list[i];
				}
			}

			internal sealed class Cond : Expr
			{
				internal Expr C, A, B;
				internal override object Eval(Env env) => Truth(C.Eval(env)) ? A.Eval(env) : B.Eval(env);
			}

			internal sealed class Range : Expr
			{
				internal Expr A, B, Step;
				internal string Where;
				internal override object Eval(Env env)
				{
					double start = ToNum(A.Eval(env), Where), stop = ToNum(B.Eval(env), Where), step = Step != null
						? ToNum(Step.Eval(env), Where)
						: 1;
					if (step == 0 || (stop - start) / step < 0)
						throw new FormatException($"{Where}: range {Fmt(start)} .. {Fmt(stop)} step {Fmt(step)} never ends");
					var values = new List<object>();
					int lastStep = (int)Math.Floor((stop - start) / step + 1e-9);
					for (int i = 0; i <= lastStep; i++) values.Add(start + i * step);
					return values;
				}
			}

			internal sealed class Un : Expr
			{
				internal char Op;
				internal Expr A;
				internal string Where;
				internal override object Eval(Env env)
				{
					var operand = A.Eval(env);
					return Op == '!' ? (object)!Truth(operand) : Op == '-' ? -ToNum(operand, Where) : ToNum(operand, Where);
				}
			}

			internal sealed class Bin : Expr
			{
				internal string Op;
				internal Expr A, B;
				internal string Where;
				internal override object Eval(Env env)
				{
					if (Op == "&&") return Truth(A.Eval(env)) && Truth(B.Eval(env));
					if (Op == "||") return Truth(A.Eval(env)) || Truth(B.Eval(env));
					object left = A.Eval(env), right = B.Eval(env);
					if (Op == "==" || Op == "!=")
					{
						bool equal = left is string || right is string
							? Equals(left as string, right as string)
							: Math.Abs(ToNum(left, Where) - ToNum(right, Where)) < 1e-9;
						return Op == "==" ? equal : !equal;
					}
					if (Op == "+" && left is List<object> leftList && right is List<object> rightList)
						return leftList.Concat(rightList).ToList();
					if (Op == "+" && (left is string || right is string))
						return (left is double leftNumber ? Fmt(leftNumber) : left?.ToString())
							+ (right is double rightNumber ? Fmt(rightNumber) : right?.ToString());
					double x = ToNum(left, Where), y = ToNum(right, Where);
					switch (Op)
					{
						case "+": return x + y;
						case "-": return x - y;
						case "*": return x * y;
						case "/": return x / y;
						case "%": return x % y;
						case "<": return x < y;
						case "<=": return x <= y;
						case ">": return x > y;
						case ">=": return x >= y;
					}
					throw new FormatException($"{Where}: operator {Op}");
				}
			}

			sealed class Parser
			{
				readonly List<string> _t = new List<string>();
				readonly string _where;
				int _i;

				internal Parser(string s, string where)
				{
					_where = $"{where}: \"{s}\"";
					int i = 0;
					while (i < s.Length)
					{
						char character = s[i];
						if (char.IsWhiteSpace(character))
						{
							i++;
							continue;
						}
						if (char.IsDigit(character) || (character == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
						{
							int j = i;
							while (j < s.Length && char.IsDigit(s[j])) j++;
							if (j < s.Length && s[j] == '.' && !(j + 1 < s.Length && s[j + 1] == '.'))
							{
								j++;
								while (j < s.Length && char.IsDigit(s[j])) j++;
							}
							if (j < s.Length
								&& (s[j] == 'e' || s[j] == 'E')
								&& j + 1 < s.Length
								&& (char.IsDigit(s[j + 1])
									|| ((s[j + 1] == '-' || s[j + 1] == '+') && j + 2 < s.Length && char.IsDigit(s[j + 2]))))
							{
								j += 2;
								while (j < s.Length && char.IsDigit(s[j])) j++;
							}
							_t.Add(s.Substring(i, j - i));
							i = j;
							continue;
						}
						if (char.IsLetter(character) || character == '_')
						{
							int j = i;
							while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
							_t.Add(s.Substring(i, j - i));
							i = j;
							continue;
						}
						if (character == '\'')
						{
							int j = s.IndexOf('\'', i + 1);
							if (j < 0) throw new FormatException($"{_where}: unclosed string");
							_t.Add(s.Substring(i, j - i + 1));
							i = j + 1;
							continue;
						}
						string two = i + 1 < s.Length ? s.Substring(i, 2) : null;
						if (two != null && new[] { "==", "!=", "<=", ">=", "&&", "||", "..", "=>" }.Contains(two))
						{
							_t.Add(two);
							i += 2;
							continue;
						}
						if ("+-*/%<>!?:()[],".IndexOf(character) >= 0)
						{
							_t.Add(character.ToString());
							i++;
							continue;
						}
						throw new FormatException($"{_where}: unexpected '{character}'");
					}
				}

				string Peek(int k = 0) => _i + k < _t.Count ? _t[_i + k] : null;
				string Next() => _i < _t.Count ? _t[_i++] : throw new FormatException($"{_where}: unexpected end");
				void Want(string s) { if (Next() != s) throw new FormatException($"{_where}: '{s}' expected"); }

				internal void End() { if (_i != _t.Count) throw new FormatException($"{_where}: unexpected '{_t[_i]}'"); }

				static bool IsName(string t) => t != null && (char.IsLetter(t[0]) || t[0] == '_');

				internal Expr Top()
				{
					if (IsName(Peek()) && Peek(1) == "=>")
					{
						var parameter = Next();
						Next();
						return new Lambda { Params = new[] { parameter }, Body = Top() };
					}
					if (Peek() == "(")
					{
						if (Peek(1) == ")" && Peek(2) == "=>")
						{
							_i += 3;
							return new Lambda { Params = new string[0], Body = Top() };
						}
						var parameters = new List<string>();
						for (int k = 1; IsName(Peek(k)); k += 2)
						{
							parameters.Add(Peek(k));
							if (Peek(k + 1) == ",") continue;
							if (Peek(k + 1) == ")" && Peek(k + 2) == "=>")
							{
								_i += k + 3;
								return new Lambda { Params = parameters.ToArray(), Body = Top() };
							}
							break;
						}
					}
					return Ternary();
				}

				Expr Ternary()
				{
					var condition = Or();
					if (Peek() != "?") return condition;
					Next();
					var thenBranch = Top();
					Want(":");
					return new Cond { C = condition, A = thenBranch, B = Top() };
				}

				Expr Or()
				{
					var left = And();
					while (Peek() == "||")
					{
						Next();
						left = new Bin { Op = "||", A = left, B = And(), Where = _where };
					}
					return left;
				}

				Expr And()
				{
					var left = Eq();
					while (Peek() == "&&")
					{
						Next();
						left = new Bin { Op = "&&", A = left, B = Eq(), Where = _where };
					}
					return left;
				}

				Expr Eq()
				{
					var left = Rel();
					while (Peek() == "==" || Peek() == "!=")
					{
						var symbol = Next();
						left = new Bin { Op = symbol, A = left, B = Rel(), Where = _where };
					}
					return left;
				}

				Expr Rel()
				{
					var left = Rng();
					while (Peek() == "<" || Peek() == "<=" || Peek() == ">" || Peek() == ">=")
					{
						var symbol = Next();
						left = new Bin { Op = symbol, A = left, B = Rng(), Where = _where };
					}
					return left;
				}

				Expr Rng()
				{
					var start = Add();
					if (Peek() != "..") return start;
					Next();
					var range = new Range { A = start, B = Add(), Where = _where };
					if (Peek() == "step")
					{
						Next();
						range.Step = Add();
					}
					return range;
				}

				Expr Add()
				{
					var left = Mul();
					while (Peek() == "+" || Peek() == "-")
					{
						var symbol = Next();
						left = new Bin { Op = symbol, A = left, B = Mul(), Where = _where };
					}
					return left;
				}

				Expr Mul()
				{
					var left = Unary();
					while (Peek() == "*" || Peek() == "/" || Peek() == "%")
					{
						var symbol = Next();
						left = new Bin { Op = symbol, A = left, B = Unary(), Where = _where };
					}
					return left;
				}

				Expr Unary()
				{
					if (Peek() == "-" || Peek() == "!" || Peek() == "+")
					{
						var sign = Next()[0];
						return new Un { Op = sign, A = Unary(), Where = _where };
					}
					return Postfix();
				}

				Expr Postfix()
				{
					var expression = Primary();
					while (true)
					{
						if (Peek() == "(")
						{
							Next();
							var args = new List<Expr>();
							if (Peek() != ")")
							{
								args.Add(Top());
								while (Peek() == ",")
								{
									Next();
									args.Add(Top());
								}
							}
							Want(")");
							expression = new Call { F = expression, Args = args, Where = _where };
						}
						else if (Peek() == "[")
						{
							Next();
							var index = Top();
							Want("]");
							expression = new Index { L = expression, I = index, Where = _where };
						}
						else return expression;
					}
				}

				Expr Primary()
				{
					var token = Next();
					if (char.IsDigit(token[0]) || token[0] == '.')
						return new Lit { V = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture) };
					if (token[0] == '\'') return new Lit { V = token.Substring(1, token.Length - 2) };
					if (token == "true" || token == "false") return new Lit { V = token == "true" };
					if (IsName(token)) return new Name { N = token, Where = _where };
					if (token == "(")
					{
						var inner = Top();
						Want(")");
						return inner;
					}
					if (token == "[")
					{
						var items = new List<Expr>();
						if (Peek() != "]")
						{
							items.Add(Top());
							while (Peek() == ",")
							{
								Next();
								items.Add(Top());
							}
						}
						Want("]");
						return new ListE { Items = items };
					}
					throw new FormatException($"{_where}: unexpected '{token}'");
				}
			}
		}
	}
}
