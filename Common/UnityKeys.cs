using System.Collections.Generic;

namespace T90A.Common
{
	internal static class UnityKeys
	{
		internal static void Prune<TKey, TValue>(Dictionary<TKey, TValue> table) where TKey : UnityEngine.Object
		{
			List<TKey> dead = null;
			foreach (var key in table.Keys)
			{
				if (key != null) continue;
				if (dead == null) dead = new List<TKey>();
				dead.Add(key);
			}
			if (dead == null) return;
			foreach (var key in dead) table.Remove(key);
		}
	}
}
