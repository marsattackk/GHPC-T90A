using System;
using System.Collections.Generic;
using System.Globalization;
using GHPC;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class TraverseLimits : MonoBehaviour
	{
		public AimablePlatform Gun, Traverse;
		public float Min;
		float[] _angles = new float[0], _limits = new float[0];

		static readonly AccessTools.FieldRef<AimablePlatform, Quaternion> Orig = AccessTools.FieldRefAccess<AimablePlatform, Quaternion>("_originalLocalRotation");
		static readonly AccessTools.FieldRef<AimablePlatform, bool> Destroyed = AccessTools.FieldRefAccess<AimablePlatform, bool>("_destroyed");
		static readonly Action<AimablePlatform> Enforce = AccessTools.MethodDelegate<Action<AimablePlatform>>(AccessTools.Method(typeof(AimablePlatform), "enforceEulerLimits"));

		internal static List<KeyValuePair<float, float>> Parse(string text)
		{
			var knots = new List<KeyValuePair<float, float>>();
			foreach (var raw in text.Split('\n'))
			{
				int hash = raw.IndexOf('#');
				var line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
				if (line.Length == 0 || line.StartsWith("outside")) continue;
				var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (p.Length != 2) throw new FormatException("traverse limit profile: bad line '" + line + "'");
				knots.Add(new KeyValuePair<float, float>(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)));
			}
			return knots;
		}

		internal static void Apply(AimablePlatform gun, AimablePlatform traverse, List<KeyValuePair<float, float>> knots)
		{
			if (gun == null || traverse == null || knots == null) return;
			var c = gun.GetComponent<TraverseLimits>() ?? gun.gameObject.AddComponent<TraverseLimits>();
			c.Gun = gun; c.Traverse = traverse; c.Min = gun.LocalEulerLimits.x;
			var sorted = new List<KeyValuePair<float, float>>(knots.Count);
			foreach (var k in knots) sorted.Add(new KeyValuePair<float, float>(Mathf.Repeat(k.Key, 360f), k.Value));
			sorted.Sort((a, b) => a.Key.CompareTo(b.Key));
			c._angles = sorted.ConvertAll(k => k.Key).ToArray();
			c._limits = sorted.ConvertAll(k => k.Value).ToArray();
		}

		internal float Yaw()
		{
			var t = Traverse.Transform;
			return Mathf.DeltaAngle(0f, (Quaternion.Inverse(Orig(Traverse)) * t.localRotation).eulerAngles.y);
		}

		internal float MinAt(float yaw)
		{
			int n = _angles.Length;
			if (n == 0) return Min;
			float t = Mathf.Repeat(yaw, 360f);
			int hi = 0;
			while (hi < n && _angles[hi] < t) hi++;
			int lo = hi - 1;
			float a0, a1;
			if (hi == n) { hi = 0; a0 = _angles[n - 1]; a1 = _angles[0] + 360f; lo = n - 1; }
			else if (lo < 0) { lo = n - 1; a0 = _angles[n - 1] - 360f; a1 = _angles[hi]; }
			else { a0 = _angles[lo]; a1 = _angles[hi]; }
			float v = a1 > a0 ? Mathf.Lerp(_limits[lo], _limits[hi], (t - a0) / (a1 - a0)) : _limits[hi];
			return Mathf.Max(Min, v);
		}

		void Update()
		{
			if (Gun == null || Traverse == null || Traverse.Transform == null) return;
			float lim = MinAt(Yaw());
			var l = Gun.LocalEulerLimits;
			if (l.x != lim) { l.x = lim; Gun.LocalEulerLimits = l; }
			if (!Gun.enabled || Destroyed(Gun)) return;
			if (Gun.GetCurrentEulerAngle() < lim - 0.001f)
			{
				Enforce(Gun);
			}
		}
	}
}
