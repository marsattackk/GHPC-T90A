using System;

namespace T90A.Common
{
	internal static class NameShield
	{
		internal static string Form(string name, string[] from, string shieldName)
		{
			if (string.IsNullOrEmpty(name) || from == null || string.IsNullOrEmpty(shieldName)) return null;
			bool hit = false;
			foreach (var fragment in from)
			{
				if (string.IsNullOrEmpty(fragment)) continue;
				if (shieldName.IndexOf(fragment, StringComparison.Ordinal) >= 0) return null;
				hit |= name.IndexOf(fragment, StringComparison.Ordinal) >= 0;
			}
			return hit ? shieldName : null;
		}
	}
}
