using System;

namespace T90A.Common
{
	internal static class NameShield
	{
		internal static string Form(string name, string[] from, string shieldName)
		{
			if (string.IsNullOrEmpty(name) || from == null || string.IsNullOrEmpty(shieldName)) return null;
			bool hit = false;
			foreach (var f in from)
			{
				if (string.IsNullOrEmpty(f)) continue;
				if (shieldName.IndexOf(f, StringComparison.Ordinal) >= 0) return null;
				hit |= name.IndexOf(f, StringComparison.Ordinal) >= 0;
			}
			return hit ? shieldName : null;
		}
	}
}
