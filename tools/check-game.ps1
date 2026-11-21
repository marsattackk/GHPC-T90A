# Says which of the mod's game hooks a GHPC build has broken, without starting the game. It reads the built T90A.dll,
# finds every Harmony patch and every private member the mod looks up, and looks each one up in the game's own assemblies:
#   - [HarmonyPatch] attributes, and Reflect.Field / Property / Method / FieldRefAccess with literal names (Common\Reflect.cs)
#   - AccessTools.Method(typeof(X) or AccessTools.TypeByName("X"), "name"), a patch target when its method hands it to Harmony.Patch or is
#     the Target() of a patch class
#   - Traverse.Create(x).Field("name") (the class is x's declared class)
#   - the hit-zone hook table of Common\ModuleBuilder.cs: each `new HookDef(typeof(X), "Field", ("Event", "handler"), ...)` gives the
#     owner's field, its handler methods and the destructible component's events (and their backing fields)
#   - Type.GetField / GetMethod / GetProperty / GetEvent with a literal name on a game class
# Anything the IL can't spell out (a name that comes from a variable) is read from check-game-list.txt, next to this script.
# It cannot check by name: field enumerations (Type.GetFields: the code takes whatever fields the class has now) and
# Type.GetField / GetMethod with a name from a variable. The summary lists where those are, to read a game update against.
#
#   powershell -ExecutionPolicy Bypass -File tools\check-game.ps1 -GameDir "<GHPC folder>\Bin"
#   ... -Dll <path to a built T90A.dll>   skip the build and read that DLL
#   ... -OnlyProblems                     print the broken lines only
#
# Exit code 0: everything found. 1: something is missing or the list doesn't cover a call it should.
# Mono.Cecil comes from the game's own MelonLoader. Nothing to install. Windows PowerShell 5.1 is enough.
param(
	[string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Gunner HEAT PC\Bin",
	[string]$Dll = "",
	[string]$List = "",
	[switch]$OnlyProblems
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $List) { $List = Join-Path $PSScriptRoot "check-game-list.txt" }
$managed = Join-Path $GameDir "GHPC_Data\Managed"
$cecil = @("net35", "net472") | ForEach-Object { Join-Path $GameDir "MelonLoader\$_\Mono.Cecil.dll" } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not (Test-Path (Join-Path $managed "Assembly-CSharp.dll"))) { throw "No Assembly-CSharp.dll in $managed. -GameDir is the folder with GHPC.exe in it." }
if (-not $cecil) { throw "No Mono.Cecil.dll under $GameDir\MelonLoader. Start the game once so MelonLoader sets itself up." }
if (-not (Test-Path $List)) { throw "No list file at $List" }

$work = ""
try {
	if (-not $Dll) {
		$work = Join-Path ([IO.Path]::GetTempPath()) ("t90a-check-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
		Write-Host "Building the working tree..."
		& dotnet build (Join-Path $repo "T90A.csproj") -c Release -nologo -v q "-p:GameDir=$GameDir" -p:GitCommit=check -o $work | Out-Null
		if ($LASTEXITCODE -ne 0) { throw "The build failed. Run it by hand to see why: dotnet build T90A.csproj -p:GameDir=..." }
		$Dll = Join-Path $work "T90A.dll"
	}
	if (-not (Test-Path $Dll)) { throw "No DLL at $Dll" }

	[Reflection.Assembly]::LoadFrom($cecil) | Out-Null
	if (-not ("GameCheck.Checker" -as [type])) {
		Add-Type -ReferencedAssemblies $cecil -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GameCheck
{
	public class Hook
	{
		public string Kind;      // field, method, property, setter, patch
		public string Origin;    // Reflect / HarmonyPatch / list
		public string Where;     // the type and method in the mod that uses it
		public string Type;
		public string Member;
		public string Params;    // null when the call doesn't give parameter types
		public string FieldType; // for a field read through FieldRefAccess
		public bool Target;      // a Reflect call that feeds a Harmony patch
		public bool Ok;
		public string Detail;
	}

	public class Unread
	{
		public string Where;
		public string Call;
	}

	// What the stack holds while one method body is walked. Anything it can't know stays Unknown.
	class V { }
	class VStr : V { public string S; }
	class VInt : V { public int I; }
	class VTok : V { public TypeReference T; }
	class VType : V { public TypeReference T; }
	class VArr : V { public VType[] Items; public bool Unknown; }
	class VNull : V { }
	class VTypeNm : V { public string N; }                      // a type named by a string: AccessTools.TypeByName("...")
	class VObj : V { public TypeReference T; }                  // a value of a known static type (a local, an argument)
	class VTrav : V { public TypeReference T; }                 // Traverse.Create(x) on such a value
	class VTuple : V { public string A, B; }                    // ("Destroyed", "engineDestroyed")
	class VPairs : V { public List<VTuple> Items = new List<VTuple>(); public bool Unknown; }   // the params array of those

	public class Checker
	{
		readonly Dictionary<string, TypeDefinition> types = new Dictionary<string, TypeDefinition>();
		readonly Dictionary<string, List<TypeDefinition>> byShort = new Dictionary<string, List<TypeDefinition>>();
		public List<Hook> Hooks = new List<Hook>();
		public List<Unread> Unreads = new List<Unread>();
		public int ReflectCalls;
		// Looked up in ways a name can't be checked for: field enumerations (GetFields), and Type.GetField / GetMethod ... with a name from a variable.
		public List<Unread> Enumerations = new List<Unread>();
		public int NameVars;
		TypeReference eventOwner;                       // the type the mod's GetEvent calls ask (DestructibleComponent)
		readonly List<Hook> pendingOnEventOwner = new List<Hook>();
		public int ModuleCount;
		public int TypeCount;

		public void LoadGame(string managedDir)
		{
			var files = Directory.GetFiles(managedDir, "*.dll");
			// Assembly-CSharp first, so a name in two assemblies resolves to the game's own.
			Array.Sort(files, (a, b) => Rank(a).CompareTo(Rank(b)) != 0 ? Rank(a).CompareTo(Rank(b)) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
			var mods = new ModuleDefinition[files.Length];
			Parallel.For(0, files.Length, i =>
			{
				try { mods[i] = ModuleDefinition.ReadModule(files[i], new ReaderParameters(ReadingMode.Deferred)); }
				catch (Exception) { mods[i] = null; }
			});
			foreach (var m in mods)
			{
				if (m == null) continue;
				ModuleCount++;
				foreach (var t in m.GetTypes())
				{
					TypeCount++;
					if (!types.ContainsKey(t.FullName)) types[t.FullName] = t;
					List<TypeDefinition> l;
					if (!byShort.TryGetValue(t.Name, out l)) byShort[t.Name] = l = new List<TypeDefinition>();
					l.Add(t);
				}
			}
		}

		static int Rank(string f)
		{
			string n = Path.GetFileName(f);
			if (n.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)) return 0;
			if (n.Equals("Assembly-CSharp-firstpass.dll", StringComparison.OrdinalIgnoreCase)) return 1;
			return 2;
		}

		// Reading the mod.

		public void ReadMod(string dll, string[] searchDirs)
		{
			// Decoding the Harmony attributes needs 0Harmony.dll, which sits in MelonLoader's folder.
			var resolver = new DefaultAssemblyResolver();
			foreach (var d in searchDirs) resolver.AddSearchDirectory(d);
			var mod = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = resolver });
			foreach (var t in mod.GetTypes())
			{
				// The mod's own classes too (after the game's, so a game name still wins): a Reflect call into one of them is checked against the mod.
				if (!types.ContainsKey(t.FullName)) types[t.FullName] = t;
				ReadPatchAttributes(t);
				foreach (var m in t.Methods)
					if (m.HasBody) ReadBody(m);
			}
			// A hook table's events and their backing fields are looked up on the type the mod's GetEvent calls name.
			foreach (var h in pendingOnEventOwner)
			{
				if (eventOwner != null) h.Type = eventOwner.FullName;
				else { h.Type = "(unknown)"; h.Ok = false; h.Detail = "no GetEvent call in the mod names the type that owns these events"; }
			}
		}

		static string Top(TypeDefinition t)
		{
			while (t.DeclaringType != null) t = t.DeclaringType;
			return t.FullName;
		}

		static string Pretty(string full)
		{
			return full.Replace("T90A.Common.", "").Replace("T90A.", "").Replace('/', '.');
		}

		static string WhereOf(MethodDefinition m)
		{
			var d = m.DeclaringType;
			string owner = Pretty(d.FullName);
			// Lambdas and iterators live in a generated nested class: name the real owner.
			if (d.Name.StartsWith("<>")) owner = Pretty(Top(d));
			return owner + "." + CleanName(m.Name);
		}

		static string CleanName(string n)
		{
			return n.StartsWith("<") && n.IndexOf('>') > 0 ? n.Substring(1, n.IndexOf('>') - 1) : n;
		}

		void ReadPatchAttributes(TypeDefinition t)
		{
			if (!t.HasCustomAttributes) return;
			foreach (var a in t.CustomAttributes)
			{
				if (a.AttributeType.FullName != "HarmonyLib.HarmonyPatch") continue;
				if (!a.HasConstructorArguments) continue; // argument-less: its targets are picked in code
				TypeReference target = null; string name = null; CustomAttributeArgument[] ps = null; int mtype = 0; bool hasParams = false;
				foreach (var arg in a.ConstructorArguments)
				{
					var v = arg.Value;
					if (v is TypeReference && target == null) target = (TypeReference)v;
					else if (v is string && name == null) name = (string)v;
					else if (v is CustomAttributeArgument[] && ps == null) { ps = (CustomAttributeArgument[])v; hasParams = true; }
					else if (arg.Type.FullName == "HarmonyLib.MethodType") mtype = Convert.ToInt32(v);
				}
				var h = new Hook { Kind = "patch", Origin = "HarmonyPatch", Where = Pretty(t.FullName) };
				if (target == null) { h.Type = "(no type)"; h.Member = name ?? "?"; h.Ok = false; h.Detail = "the attribute names no class"; Hooks.Add(h); continue; }
				h.Type = target.FullName;
				// MethodType: 0 normal, 1 getter, 2 setter, 3 constructor
				h.Member = name ?? ".ctor";
				if (mtype == 1) h.Kind = "patch getter"; else if (mtype == 2) h.Kind = "patch setter"; else if (mtype == 3) h.Kind = "patch constructor";
				if (hasParams) h.Params = string.Join(",", ps.Select(p => ((TypeReference)p.Value).FullName).ToArray());
				Hooks.Add(h);
			}
		}

		void ReadBody(MethodDefinition m)
		{
			if (m.DeclaringType.FullName == "T90A.Common.Reflect") return; // Reflect.FieldRefAccess calls Reflect.Field with its own variable
			var ins = m.Body.Instructions;
			bool any = false;
			foreach (var i in ins)
				if ((i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt || i.OpCode.Code == Code.Newobj) && Interesting(i.Operand as MethodReference)) { any = true; break; }
			if (!any) return;

			// Instructions other jumps land on: the stack there isn't what the previous line left.
			var targets = new HashSet<Instruction>();
			foreach (var i in ins)
			{
				var one = i.Operand as Instruction;
				if (one != null) targets.Add(one);
				var many = i.Operand as Instruction[];
				if (many != null) foreach (var x in many) targets.Add(x);
			}
			if (m.Body.HasExceptionHandlers)
				foreach (var h in m.Body.ExceptionHandlers)
				{
					if (h.HandlerStart != null) targets.Add(h.HandlerStart);
					if (h.FilterStart != null) targets.Add(h.FilterStart);
				}

			// A local assigned once in the whole method (var t = typeof(X)) is as good as the value itself.
			var stores = new Dictionary<int, int>();
			foreach (var i in ins)
			{
				int sl = StoreIndex(i);
				if (sl >= 0) stores[sl] = (stores.ContainsKey(sl) ? stores[sl] : 0) + 1;
			}
			var locals = new Dictionary<int, V>();
			var stack = new List<V>();
			// The methods that hand Harmony its targets: PlayerControlPatch.Targets, and the two by-hand patchers.
			// A method that hands its lookup to Harmony.Patch in the same body (NetScreens.Apply), and the Target() of a patch class, are patchers too.
			bool patcher = m.Name == "Targets" || m.Name == "EnsurePatched" || m.Name == "Patch"
				|| ((m.Name == "Target" || m.Name == "TargetMethod" || m.Name == "TargetMethods") && m.DeclaringType.HasCustomAttributes
					&& m.DeclaringType.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
				|| ins.Any(x => (x.OpCode.Code == Code.Call || x.OpCode.Code == Code.Callvirt) && x.Operand is MethodReference
					&& ((MethodReference)x.Operand).DeclaringType.FullName == "HarmonyLib.Harmony"
					&& (((MethodReference)x.Operand).Name == "Patch" || ((MethodReference)x.Operand).Name == "PatchAll"));
			foreach (var i in ins)
			{
				if (targets.Contains(i)) stack.Clear();
				var code = i.OpCode.Code;
				if ((code == Code.Call || code == Code.Callvirt) && IsReflect((MethodReference)i.Operand))
				{
					ReflectCalls++;
					ReadReflect(m, (MethodReference)i.Operand, stack, patcher);
				}
				else if ((code == Code.Call || code == Code.Callvirt || code == Code.Newobj) && i.Operand is MethodReference && ReadOther(m, (MethodReference)i.Operand, code, stack, patcher))
					continue;   // handled, the stack is left as the call would
				int arg = ArgIndex(i);
				if (arg >= 0)
				{
					stack.Add(ArgValue(m, arg));
					continue;
				}
				int st = StoreIndex(i);
				if (st >= 0 && stores[st] == 1 && stack.Count > 0 && (stack[stack.Count - 1] is VType || stack[stack.Count - 1] is VStr)) locals[st] = stack[stack.Count - 1];
				int ld = LoadIndex(i);
				if (ld >= 0)
				{
					V known;
					if (stores.ContainsKey(ld) && stores[ld] == 1 && locals.TryGetValue(ld, out known)) stack.Add(known); else stack.Add(LocalValue(m, ld));
					continue;
				}
				Step(i, stack);
			}
		}

		static int StoreIndex(Instruction i)
		{
			switch (i.OpCode.Code)
			{
				case Code.Stloc_0: return 0;
				case Code.Stloc_1: return 1;
				case Code.Stloc_2: return 2;
				case Code.Stloc_3: return 3;
				case Code.Stloc:
				case Code.Stloc_S: return ((VariableDefinition)i.Operand).Index;
			}
			return -1;
		}

		static int LoadIndex(Instruction i)
		{
			switch (i.OpCode.Code)
			{
				case Code.Ldloc_0: return 0;
				case Code.Ldloc_1: return 1;
				case Code.Ldloc_2: return 2;
				case Code.Ldloc_3: return 3;
				case Code.Ldloc:
				case Code.Ldloc_S: return ((VariableDefinition)i.Operand).Index;
			}
			return -1;
		}

		static bool IsReflect(MethodReference r)
		{
			return r != null && r.DeclaringType.FullName == "T90A.Common.Reflect";
		}

		static V Peek(List<V> s, int fromTop)
		{
			int idx = s.Count - 1 - fromTop;
			return idx >= 0 ? s[idx] : null;
		}

		void ReadReflect(MethodDefinition m, MethodReference call, List<V> stack, bool target, string origin = "Reflect")
		{
			string name = call.Name;
			string where = WhereOf(m);
			var h = new Hook { Origin = origin, Where = where, Target = target && name == "Method" };
			string typeName = null; string member = null; V parms = null;
			if (name == "FieldRefAccess")
			{
				var g = call as GenericInstanceMethod;
				var s = Peek(stack, 0) as VStr;
				if (g != null && g.GenericArguments.Count == 2 && s != null && !(g.GenericArguments[0] is GenericParameter))
				{
					typeName = g.GenericArguments[0].FullName; member = s.S; h.FieldType = g.GenericArguments[1].FullName; h.Kind = "field";
				}
			}
			else if (name == "Field" || name == "Property" || name == "PropertySetter" || name == "Method")
			{
				int n = name == "Method" ? 4 : 2;
				string tn = TName(Peek(stack, n - 1));
				var s = Peek(stack, n - 2) as VStr;
				if (tn != null && s != null)
				{
					typeName = tn; member = s.S;
					h.Kind = name == "PropertySetter" ? "setter" : name.ToLower();
					if (name == "Method") parms = Peek(stack, 1);
				}
			}
			else return;

			if (typeName == null)
			{
				if (origin == "Reflect") Unreads.Add(new Unread { Where = where, Call = "Reflect." + name });
				return;
			}
			h.Type = typeName;
			h.Member = member;
			if (name == "Method")
			{
				var arr = parms as VArr;
				if (parms is VNull) h.Params = null;
				else if (arr != null && !arr.Unknown && arr.Items.All(x => x != null)) h.Params = string.Join(",", arr.Items.Select(x => x.T.FullName).ToArray());
				else { if (origin == "Reflect") Unreads.Add(new Unread { Where = where, Call = "Reflect.Method" }); return; }
			}
			Hooks.Add(h);
		}

		// The class a value names: a typeof(X) or TypeByName("X"); null for a generic parameter or anything unknown.
		static string TName(V v)
		{
			var t = v as VType;
			if (t != null) return t.T is GenericParameter ? null : t.T.FullName;
			var n = v as VTypeNm;
			return n != null ? n.N : null;
		}

		// The calls the body reader looks at besides Reflect.*: AccessTools.Method / TypeByName, Traverse, the hook table's constructor, and
		// Type.GetField / GetMethod / GetProperty / GetEvent / GetFields.
		static bool Interesting(MethodReference r)
		{
			if (r == null) return false;
			string d = r.DeclaringType.FullName;
			return d == "T90A.Common.Reflect" || d == "HarmonyLib.AccessTools" || d == "HarmonyLib.Traverse" || d == "HarmonyLib.Harmony"
				|| d == "System.Type" || (r.Name == ".ctor" && r.DeclaringType.Name == "HookDef");
		}

		static int ArgIndex(Instruction i)
		{
			switch (i.OpCode.Code)
			{
				case Code.Ldarg_0: return 0;
				case Code.Ldarg_1: return 1;
				case Code.Ldarg_2: return 2;
				case Code.Ldarg_3: return 3;
				case Code.Ldarg:
				case Code.Ldarg_S:
				{
					var p = (ParameterDefinition)i.Operand;
					var owner = p.Method as MethodDefinition;
					return p.Index + (owner != null && owner.HasThis ? 1 : 0);
				}
			}
			return -1;
		}

		// An argument as a value of its declared class (`this` counts as argument 0 of an instance method).
		static V ArgValue(MethodDefinition m, int index)
		{
			if (m.HasThis) { if (index == 0) return new VObj { T = m.DeclaringType }; index--; }
			if (index < 0 || index >= m.Parameters.Count) return new V();
			var t = m.Parameters[index].ParameterType;
			return t is GenericParameter || t.IsByReference ? new V() : (V)new VObj { T = t };
		}

		static V LocalValue(MethodDefinition m, int index)
		{
			if (index >= m.Body.Variables.Count) return new V();
			var t = m.Body.Variables[index].VariableType;
			return t is GenericParameter || t.IsByReference ? new V() : (V)new VObj { T = t };
		}

		// One call the plain stack walk can't follow. True when it also left the stack as the call would (so the walk moves on).
		bool ReadOther(MethodDefinition m, MethodReference call, Code code, List<V> stack, bool patcher)
		{
			string decl = call.DeclaringType.FullName;
			string where = WhereOf(m);
			if (decl == "HarmonyLib.AccessTools" && call.Name == "TypeByName" && call.Parameters.Count == 1)
			{
				var s = Pop(stack) as VStr;
				stack.Add(s != null ? (V)new VTypeNm { N = s.S } : new V());
				return true;
			}
			if (decl == "HarmonyLib.AccessTools" && call.Name == "Method" && call.Parameters.Count == 4)
			{
				ReadReflect(m, call, stack, patcher, "AccessTools");
				return false;   // the plain walk pops the four arguments and pushes the result
			}
			if (decl == "HarmonyLib.Traverse" && call.Name == "Create" && call.Parameters.Count == 1)
			{
				var root = Pop(stack) as VObj;
				stack.Add(root != null ? (V)new VTrav { T = root.T } : new V());
				return true;
			}
			if (decl == "HarmonyLib.Traverse" && call.Name == "Create" && call is GenericInstanceMethod && call.Parameters.Count == 0)
			{
				stack.Add(new VTrav { T = ((GenericInstanceMethod)call).GenericArguments[0] });
				return true;
			}
			if (decl == "HarmonyLib.Traverse" && call.Name == "Field" && call.Parameters.Count == 1)
			{
				var s = Pop(stack) as VStr;
				var trav = Pop(stack) as VTrav;
				if (s != null && trav != null && !(trav.T is GenericParameter))
					Hooks.Add(new Hook { Kind = "field", Origin = "Traverse", Where = where, Type = trav.T.FullName, Member = s.S });
				stack.Add(new V());
				return true;
			}
			if (code == Code.Newobj && call.Name == ".ctor" && call.DeclaringType.Name == "HookDef" && call.Parameters.Count == 3)
			{
				var pairs = Pop(stack) as VPairs;
				var field = Pop(stack) as VStr;
				string owner = TName(Pop(stack));
				stack.Add(new V());
				if (pairs == null || pairs.Unknown || field == null || owner == null)
				{
					Unreads.Add(new Unread { Where = where, Call = "the hook table's entry" });
					return true;
				}
				AddOnce(new Hook { Kind = "field", Origin = "hook table", Where = where, Type = owner, Member = field.S });
				foreach (var pair in pairs.Items)
				{
					AddOnce(new Hook { Kind = "method", Origin = "hook table", Where = where, Type = owner, Member = pair.B });
					var evt = new Hook { Kind = "event", Origin = "hook table", Where = where, Type = null, Member = pair.A };
					var back = new Hook { Kind = "field", Origin = "hook table", Where = where, Type = null, Member = pair.A };
					if (AddOnce(evt)) pendingOnEventOwner.Add(evt);
					if (AddOnce(back)) pendingOnEventOwner.Add(back);
				}
				return true;
			}
			if (decl == "System.Type" && call.HasThis)
			{
				int n = call.Parameters.Count;
				if (call.Name == "GetFields")
				{
					string tn = TName(Peek(stack, n));
					Enumerations.Add(new Unread { Where = where, Call = "GetFields on " + (tn ?? "a type from a variable") });
					return false;
				}
				string kind = call.Name == "GetField" ? "field" : call.Name == "GetMethod" ? "method" : call.Name == "GetProperty" ? "property" : call.Name == "GetEvent" ? "event" : null;
				if (kind != null && n >= 1 && call.Parameters[0].ParameterType.FullName == "System.String")
				{
					string tn = TName(Peek(stack, n));
					var s = Peek(stack, n - 1) as VStr;
					var receiver = Peek(stack, n) as VType;
					if (kind == "event" && receiver != null && eventOwner == null) eventOwner = receiver.T;
					if (tn == null) return false;
					if (s == null) { NameVars++; return false; }
					if (tn.StartsWith("System.")) return false;   // the base library, not the game
					Hooks.Add(new Hook { Kind = kind, Origin = "Type." + call.Name, Where = where, Type = tn, Member = s.S });
				}
				return false;
			}
			return false;
		}

		bool AddOnce(Hook h)
		{
			foreach (var x in Hooks)
				if (x.Origin == h.Origin && x.Kind == h.Kind && x.Type == h.Type && x.Member == h.Member) return false;
			Hooks.Add(h);
			return true;
		}

		// One instruction's effect on the stack. Only strings, ints, type tokens, Type values and Type[] are tracked.
		static void Step(Instruction i, List<V> s)
		{
			var op = i.OpCode;
			switch (op.Code)
			{
				case Code.Ldstr: s.Add(new VStr { S = (string)i.Operand }); return;
				case Code.Ldnull: s.Add(new VNull()); return;
				case Code.Ldc_I4_M1: s.Add(new VInt { I = -1 }); return;
				case Code.Ldc_I4_0: s.Add(new VInt { I = 0 }); return;
				case Code.Ldc_I4_1: s.Add(new VInt { I = 1 }); return;
				case Code.Ldc_I4_2: s.Add(new VInt { I = 2 }); return;
				case Code.Ldc_I4_3: s.Add(new VInt { I = 3 }); return;
				case Code.Ldc_I4_4: s.Add(new VInt { I = 4 }); return;
				case Code.Ldc_I4_5: s.Add(new VInt { I = 5 }); return;
				case Code.Ldc_I4_6: s.Add(new VInt { I = 6 }); return;
				case Code.Ldc_I4_7: s.Add(new VInt { I = 7 }); return;
				case Code.Ldc_I4_8: s.Add(new VInt { I = 8 }); return;
				case Code.Ldc_I4_S: s.Add(new VInt { I = (sbyte)i.Operand }); return;
				case Code.Ldc_I4: s.Add(new VInt { I = (int)i.Operand }); return;
				case Code.Ldtoken:
					if (i.Operand is TypeReference) s.Add(new VTok { T = (TypeReference)i.Operand }); else s.Add(new V());
					return;
				case Code.Ldsfld:
				{
					var f = (FieldReference)i.Operand;
					if (f.DeclaringType.FullName == "System.Type" && f.Name == "EmptyTypes") s.Add(new VArr { Items = new VType[0] });
					else s.Add(new V());
					return;
				}
				case Code.Newarr:
				{
					var n = Pop(s) as VInt;
					if (((TypeReference)i.Operand).FullName.StartsWith("System.ValueTuple`2<System.String,System.String>")) s.Add(new VPairs());
					else if (n != null && n.I >= 0 && n.I < 64 && ((TypeReference)i.Operand).FullName == "System.Type") s.Add(new VArr { Items = new VType[n.I] });
					else s.Add(new VArr { Unknown = true, Items = new VType[0] });
					return;
				}
				case Code.Dup:
					if (s.Count > 0) s.Add(s[s.Count - 1]); else s.Add(new V());
					return;
				case Code.Stelem_Ref:
				case Code.Stelem_Any:
				{
					var val = Pop(s); var idx = Pop(s) as VInt; var any = Pop(s);
					var pairs = any as VPairs;
					if (pairs != null)
					{
						var tuple = val as VTuple;
						if (tuple != null) pairs.Items.Add(tuple); else pairs.Unknown = true;
						return;
					}
					var arr = any as VArr;
					if (arr != null && !arr.Unknown && idx != null && idx.I >= 0 && idx.I < arr.Items.Length) arr.Items[idx.I] = val as VType;
					else if (arr != null) arr.Unknown = true;
					return;
				}
				case Code.Call:
				case Code.Callvirt:
				case Code.Newobj:
				{
					var r = (MethodReference)i.Operand;
					if (op.Code == Code.Call && r.Name == "GetTypeFromHandle" && r.DeclaringType.FullName == "System.Type")
					{
						var tok = Pop(s) as VTok;
						if (tok != null) s.Add(new VType { T = tok.T }); else s.Add(new V());
						return;
					}
					if (op.Code == Code.Newobj && r.DeclaringType.FullName.StartsWith("System.ValueTuple`2<System.String,System.String>") && r.Parameters.Count == 2)
					{
						var second = Pop(s) as VStr; var first = Pop(s) as VStr;
						s.Add(first != null && second != null ? (V)new VTuple { A = first.S, B = second.S } : new V());
						return;
					}
					int pops = r.Parameters.Count + (r.HasThis && op.Code != Code.Newobj ? 1 : 0);
					for (int k = 0; k < pops; k++) Pop(s);
					if (op.Code == Code.Newobj || r.ReturnType.FullName != "System.Void") s.Add(new V());
					return;
				}
				case Code.Calli:
					s.Clear(); return;
				case Code.Ret:
				case Code.Throw:
				case Code.Rethrow:
				case Code.Leave:
				case Code.Leave_S:
				case Code.Endfinally:
				case Code.Br:
				case Code.Br_S:
					s.Clear(); return;
			}
			int pop = PopCount(op.StackBehaviourPop);
			for (int k = 0; k < pop; k++) Pop(s);
			int push = PushCount(op.StackBehaviourPush);
			for (int k = 0; k < push; k++) s.Add(new V());
		}

		static V Pop(List<V> s)
		{
			if (s.Count == 0) return null;
			var v = s[s.Count - 1];
			s.RemoveAt(s.Count - 1);
			return v;
		}

		static int PopCount(StackBehaviour b)
		{
			switch (b)
			{
				case StackBehaviour.Pop0: return 0;
				case StackBehaviour.Pop1:
				case StackBehaviour.Popi:
				case StackBehaviour.Popref: return 1;
				case StackBehaviour.Pop1_pop1:
				case StackBehaviour.Popi_pop1:
				case StackBehaviour.Popi_popi:
				case StackBehaviour.Popi_popi8:
				case StackBehaviour.Popi_popr4:
				case StackBehaviour.Popi_popr8:
				case StackBehaviour.Popref_pop1:
				case StackBehaviour.Popref_popi: return 2;
				case StackBehaviour.Popi_popi_popi:
				case StackBehaviour.Popref_popi_popi:
				case StackBehaviour.Popref_popi_popi8:
				case StackBehaviour.Popref_popi_popr4:
				case StackBehaviour.Popref_popi_popr8:
				case StackBehaviour.Popref_popi_popref: return 3;
			}
			return 0;
		}

		static int PushCount(StackBehaviour b)
		{
			switch (b)
			{
				case StackBehaviour.Push1:
				case StackBehaviour.Pushi:
				case StackBehaviour.Pushi8:
				case StackBehaviour.Pushr4:
				case StackBehaviour.Pushr8:
				case StackBehaviour.Pushref: return 1;
				case StackBehaviour.Push1_push1: return 2;
			}
			return 0;
		}

		// Reading the hand-kept list.

		public void ReadList(string path)
		{
			int n = 0;
			foreach (var raw in File.ReadAllLines(path))
			{
				n++;
				string line = raw;
				int c = line.IndexOf('#');
				string note = "";
				if (c >= 0) { note = line.Substring(c + 1).Trim(); line = line.Substring(0, c); }
				line = line.Trim();
				if (line.Length == 0) continue;
				var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts[0] == "covers")
				{
					if (parts.Length != 2) throw new Exception("List line " + n + ": covers <Type.Method>");
					Covers.Add(parts[1]);
					continue;
				}
				if (parts.Length < 3) throw new Exception("List line " + n + ": expected kind, type and member, got: " + raw);
				string kind = parts[0];
				if (kind != "field" && kind != "method" && kind != "property" && kind != "setter" && kind != "patch")
					throw new Exception("List line " + n + ": unknown kind '" + kind + "'");
				string member = parts[2]; string prm = null;
				int p = member.IndexOf('(');
				if (p >= 0)
				{
					string rest = string.Join(" ", parts.Skip(2).ToArray());
					p = rest.IndexOf('(');
					member = rest.Substring(0, p).Trim();
					prm = rest.Substring(p + 1, rest.LastIndexOf(')') - p - 1).Replace(" ", "");
				}
				Hooks.Add(new Hook { Kind = kind, Origin = "list", Where = note.Length > 0 ? note : "check-game-list.txt line " + n, Type = parts[1], Member = member, Params = prm });
			}
		}

		public HashSet<string> Covers = new HashSet<string>();

		// Looking things up in the game.

		TypeDefinition FindType(string name, out string problem)
		{
			problem = null;
			TypeDefinition t;
			if (types.TryGetValue(name, out t)) return t;
			// A list entry may give the short name, "AmmoFeed/ReloadStage" or "FireControlSystem".
			string last = name.Contains("/") ? name.Substring(name.LastIndexOf('/') + 1) : (name.Contains(".") ? name.Substring(name.LastIndexOf('.') + 1) : name);
			List<TypeDefinition> l;
			if (byShort.TryGetValue(last, out l))
			{
				var hit = l.Where(x => x.FullName == name || x.FullName.EndsWith("." + name) || x.FullName.EndsWith("/" + name) || x.FullName == name).ToList();
				if (hit.Count == 0 && !name.Contains(".") && !name.Contains("/")) hit = l;
				if (hit.Count == 1) return hit[0];
				if (hit.Count > 1) { problem = "the name matches " + hit.Count + " classes, give the full name"; return null; }
			}
			return null;
		}

		TypeDefinition Base(TypeDefinition t)
		{
			if (t.BaseType == null) return null;
			TypeDefinition b;
			return types.TryGetValue(t.BaseType.GetElementType().FullName, out b) ? b : null;
		}

		static string[] ParamNames(MethodDefinition m)
		{
			return m.Parameters.Select(p => p.ParameterType.FullName).ToArray();
		}

		public void Resolve()
		{
			foreach (var h in Hooks) Resolve(h);
		}

		void Resolve(Hook h)
		{
			string problem;
			var t = FindType(h.Type, out problem);
			if (t == null) { h.Ok = false; h.Detail = problem ?? "the class isn't in the game's assemblies any more"; return; }
			h.Type = t.FullName;
			bool declaredOnly = h.Kind.StartsWith("patch");
			string kind = h.Kind;
			string[] want = h.Params == null ? null : (h.Params.Length == 0 ? new string[0] : h.Params.Split(','));

			for (var c = t; c != null; c = declaredOnly ? null : Base(c))
			{
				if (kind == "field")
				{
					var f = c.Fields.FirstOrDefault(x => x.Name == h.Member);
					if (f == null) continue;
					h.Ok = true;
					if (h.FieldType != null && !SameType(f.FieldType.FullName, h.FieldType))
					{
						h.Ok = false;
						h.Detail = "the field is still there but it's a " + f.FieldType.FullName + " now, the mod reads it as " + h.FieldType;
					}
					return;
				}
				if (kind == "event")
				{
					if (!c.Events.Any(x => x.Name == h.Member)) continue;
					h.Ok = true;
					return;
				}
				if (kind == "property" || kind == "setter" || kind == "patch getter" || kind == "patch setter")
				{
					var p = c.Properties.FirstOrDefault(x => x.Name == h.Member);
					if (p == null) continue;
					bool wantSet = kind == "setter" || kind == "patch setter";
					h.Ok = kind == "property" || (wantSet ? p.SetMethod != null : p.GetMethod != null);
					if (!h.Ok) h.Detail = "the property is there but has no " + (wantSet ? "setter" : "getter");
					return;
				}
				if (kind == "patch constructor")
				{
					h.Ok = c.Methods.Any(x => x.IsConstructor && !x.IsStatic && (want == null || SameParams(ParamNames(x), want)));
					if (!h.Ok) h.Detail = "no constructor with those parameters";
					return;
				}
				// method, patch
				var cands = c.Methods.Where(x => x.Name == h.Member && !x.IsConstructor).ToList();
				if (want != null) cands = cands.Where(x => SameParams(ParamNames(x), want)).ToList();
				if (cands.Count == 0)
				{
					if (want != null && c.Methods.Any(x => x.Name == h.Member))
					{
						h.Ok = false;
						h.Detail = "the method is there but its parameters changed: " + string.Join("; ", c.Methods.Where(x => x.Name == h.Member).Select(x => "(" + string.Join(", ", ParamNames(x).Select(ShortName).ToArray()) + ")").ToArray());
						return;
					}
					continue;
				}
				if (cands.Count > 1) { h.Ok = false; h.Detail = "the name now matches " + cands.Count + " overloads, so the lookup can't pick one"; return; }
				h.Ok = true;
				return;
			}
			h.Ok = false;
			h.Detail = "no " + (kind.StartsWith("patch") ? "method" : kind) + " of that name on " + t.FullName + (declaredOnly ? "" : " or the classes it extends");
		}

		static string ShortName(string full)
		{
			int i = full.LastIndexOf('.');
			return i >= 0 ? full.Substring(i + 1) : full;
		}

		static bool SameType(string a, string b)
		{
			return a == b;
		}

		static bool SameParams(string[] a, string[] b)
		{
			if (a.Length != b.Length) return false;
			for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
			return true;
		}

		public string Describe(Hook h)
		{
			string type = h.Type.Replace('/', '.');
			string ps = h.Params == null ? "" : "(" + string.Join(", ", h.Params.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(ShortName).ToArray()) + ")";
			return type + "." + h.Member + ps;
		}
	}
}
'@
	}

	$c = New-Object GameCheck.Checker
	$c.LoadGame($managed)
	$c.ReadMod($Dll, [string[]]@((Split-Path -Parent $cecil), $managed))
	$c.ReadList($List)
	$c.Resolve()

	# A call whose name the IL doesn't spell out must be named in the list by a "covers Type.Method" line.
	$uncovered = @($c.Unreads | Where-Object { -not $c.Covers.Contains($_.Where) })
	$readCalls = $c.ReflectCalls - $c.Unreads.Count

	$hooks = $c.Hooks | Sort-Object @{ e = { $_.Origin } }, @{ e = { $_.Where } }, @{ e = { $_.Type } }, @{ e = { $_.Member } }
	foreach ($h in $hooks) {
		if ($OnlyProblems -and $h.Ok) { continue }
		$state = if ($h.Ok) { "ok     " } else { "MISSING" }
		$kind = $h.Kind.PadRight(8)
		$line = "$state $kind $($c.Describe($h))"
		$tag = if ($h.Target) { "patch target in $($h.Where)" } else { $h.Where }
		$line += "   [$tag]"
		if (-not $h.Ok) { $line += "`n        $($h.Detail)" }
		if ($h.Ok) { Write-Host $line } else { Write-Host $line -ForegroundColor Red }
	}
	foreach ($u in $uncovered) {
		Write-Host "NOT COVERED $($u.Call) in $($u.Where): the name comes from a variable and the list has no 'covers $($u.Where)' line for it." -ForegroundColor Red
	}

	$missing = @($c.Hooks | Where-Object { -not $_.Ok })
	$attr = @($c.Hooks | Where-Object { $_.Origin -eq "HarmonyPatch" })
	$refl = @($c.Hooks | Where-Object { $_.Origin -eq "Reflect" })
	$listed = @($c.Hooks | Where-Object { $_.Origin -eq "list" })
	$targets = @($c.Hooks | Where-Object { $_.Target })
	$table = @($c.Hooks | Where-Object { $_.Origin -eq "hook table" })
	$trav = @($c.Hooks | Where-Object { $_.Origin -eq "Traverse" })
	$direct = @($c.Hooks | Where-Object { $_.Origin -like "Type.*" -or $_.Origin -eq "AccessTools" })
	Write-Host ""
	$listedPatch = @($listed | Where-Object { $_.Kind -like "patch*" })
	Write-Host "Read $($c.ModuleCount) game assemblies ($($c.TypeCount) classes) and $(Split-Path -Leaf $Dll)."
	Write-Host "Harmony patches"
	Write-Host "  from [HarmonyPatch] attributes:     $($attr.Count)"
	Write-Host "  from the list:                      $($listedPatch.Count)"
	Write-Host "  targets found through Reflect:      $($targets.Count) (Reflect.Method / AccessTools.Method handed to Harmony)"
	Write-Host "Reflect calls (total $($c.ReflectCalls))"
	Write-Host "  read from the IL:                   $readCalls"
	Write-Host "  name from a variable, in the list:  $($c.Unreads.Count - $uncovered.Count) ($($listed.Count - $listedPatch.Count) members)"
	Write-Host "Other lookups read from the IL"
	Write-Host "  the hit-zone hook table (ModuleBuilder): $($table.Count) names (fields, events, handler methods)"
	Write-Host "  Traverse.Create(x).Field(`"...`") names:    $($trav.Count)"
	Write-Host "  Type.GetField / GetMethod ... / AccessTools.Method with literal names: $($direct.Count)"
	Write-Host "Cannot be checked by name (the member names are whatever the class has now)"
	Write-Host "  field enumerations (GetFields):       $($c.Enumerations.Count)$(if ($c.Enumerations.Count) { ' in ' + (($c.Enumerations | ForEach-Object { $_.Where + ' (' + $_.Call + ')' }) -join '; ') })"
	Write-Host "  Type.GetField / GetMethod ... with a name from a variable: $($c.NameVars)"
	Write-Host "Checked: $($c.Hooks.Count). Found: $($c.Hooks.Count - $missing.Count). Missing: $($missing.Count)."
	if ($missing.Count -gt 0 -or $uncovered.Count -gt 0) {
		if ($missing.Count -gt 0) { Write-Host "$($missing.Count) hook(s) not found in this GHPC build. docs\game-updates.md says how to find the new names." -ForegroundColor Red }
		if ($uncovered.Count -gt 0) { Write-Host "$($uncovered.Count) call(s) aren't covered by the list, so the check is incomplete." -ForegroundColor Red }
		exit 1
	}
	Write-Host "Every hook the mod uses is in this GHPC build."
	exit 0
}
finally {
	if ($work) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}
