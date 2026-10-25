using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace T90A.Common
{
	internal static class Reflect
	{
		static readonly Dictionary<string, MemberInfo> Cache = new Dictionary<string, MemberInfo>();
		static readonly Dictionary<string, Delegate> Refs = new Dictionary<string, Delegate>();

		internal static FieldInfo Field(Type type, string name) =>
			(FieldInfo)Find("field", type, name, null, () => AccessTools.Field(type, name));

		internal static MethodInfo Method(Type type, string name, Type[] parameters = null, Type[] generics = null) =>
			(MethodInfo)Find("method", type, name, parameters, () => AccessTools.Method(type, name, parameters, generics));

		internal static PropertyInfo Property(Type type, string name) =>
			(PropertyInfo)Find("property", type, name, null, () => AccessTools.Property(type, name));

		internal static MethodInfo PropertySetter(Type type, string name) =>
			(MethodInfo)Find("property setter", type, name, null, () => AccessTools.PropertySetter(type, name));

		internal static AccessTools.FieldRef<T, F> FieldRefAccess<T, F>(string name)
		{
			string key = $"ref {typeof(T).FullName}.{name} {typeof(F).FullName}";
			lock (Refs)
			{
				if (Refs.TryGetValue(key, out var known)) return (AccessTools.FieldRef<T, F>)known;
				AccessTools.FieldRef<T, F> made = null;
				var field = Field(typeof(T), name);
				if (field != null)
				{
					try { made = AccessTools.FieldRefAccess<T, F>(field); }
					catch (Exception e) { Warn("field", typeof(T), name, $"can't be read as {typeof(F).Name} ({e.GetBaseException().Message})"); }
				}
				Refs[key] = made;
				return made;
			}
		}

		static MemberInfo Find(string kind, Type type, string name, Type[] parameters, Func<MemberInfo> look)
		{
			string key = $"{kind} {type?.FullName}.{name}({(parameters == null ? "*" : string.Join(",", Array.ConvertAll(parameters, p => p.FullName)))})";
			lock (Cache)
			{
				if (Cache.TryGetValue(key, out var known)) return known;
				MemberInfo found = null;
				try { found = look(); if (found == null) Warn(kind, type, name, "not found"); }
				catch (Exception e) { Warn(kind, type, name, $"can't be looked up ({e.GetBaseException().Message})"); }
				Cache[key] = found;
				return found;
			}
		}

		static void Warn(string kind, Type type, string name, string what) =>
			MelonLogger.Warning($"Game {kind} {type?.FullName ?? "(no type)"}.{name} {what}: a game update may have changed it, and what the mod does with it won't work.");
	}
}
