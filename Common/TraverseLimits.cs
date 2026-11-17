using System;
using System.Collections.Generic;
using System.Globalization;
using GHPC;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	// Gun depression by turret bearing, from a profile.
	internal sealed class TraverseLimits : MonoBehaviour
	{
		public AimablePlatform Gun, Traverse;
		public float Min;
		float[] _angles = new float[0], _limits = new float[0];

		static readonly AccessTools.FieldRef<AimablePlatform, Quaternion> Orig =
			Reflect.FieldRefAccess<AimablePlatform, Quaternion>("_originalLocalRotation");
		static readonly AccessTools.FieldRef<AimablePlatform, bool> Destroyed = Reflect.FieldRefAccess<AimablePlatform, bool>("_destroyed");
		static readonly System.Reflection.MethodInfo EnforceMethod = Reflect.Method(typeof(AimablePlatform), "enforceEulerLimits");
		static readonly Action<AimablePlatform> Enforce = EnforceMethod == null
			? null
			: AccessTools.MethodDelegate<Action<AimablePlatform>>(EnforceMethod);

		internal static List<KeyValuePair<float, float>> Parse(string text)
		{
			var knots = new List<KeyValuePair<float, float>>();
			foreach (var raw in text.Split('\n'))
			{
				int hash = raw.IndexOf('#');
				var line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
				if (line.Length == 0 || line.StartsWith("outside")) continue;
				var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length != 2) throw new FormatException("traverse limit profile: bad line '" + line + "'");
				knots.Add(new KeyValuePair<float, float>(
					float.Parse(parts[0], CultureInfo.InvariantCulture),
					float.Parse(parts[1], CultureInfo.InvariantCulture)));
			}
			return knots;
		}

		internal static TraverseLimits Apply(
			AimablePlatform gun,
			AimablePlatform traverse,
			string tag,
			List<KeyValuePair<float, float>> knots)
		{
			if (gun == null || traverse == null || knots == null || Orig == null) return null;
			var component = gun.GetComponent<TraverseLimits>() ?? gun.gameObject.AddComponent<TraverseLimits>();
			component.Gun = gun;
			component.Traverse = traverse;
			component.Min = gun.LocalEulerLimits.x;
			var sorted = new List<KeyValuePair<float, float>>(knots.Count);
			foreach (var knot in knots) sorted.Add(new KeyValuePair<float, float>(Mathf.Repeat(knot.Key, 360f), knot.Value));
			sorted.Sort((f, s) => f.Key.CompareTo(s.Key));
			component._angles = sorted.ConvertAll(k => k.Key).ToArray();
			component._limits = sorted.ConvertAll(k => k.Value).ToArray();
			return component;
		}

		internal float[] KnotAngles => _angles;
		internal float[] KnotLimits => _limits;

		internal float Yaw()
		{
			var turret = Traverse.Transform;
			return Mathf.DeltaAngle(0f, (Quaternion.Inverse(Orig(Traverse)) * turret.localRotation).eulerAngles.y);
		}

		internal float MinAt(float yaw)
		{
			int count = _angles.Length;
			if (count == 0) return Min;
			float angle = Mathf.Repeat(yaw, 360f);
			int upper = 0;
			while (upper < count && _angles[upper] < angle) upper++;
			int lower = upper - 1;
			float lowerAngle, upperAngle;
			if (upper == count)
			{
				upper = 0;
				lowerAngle = _angles[count - 1];
				upperAngle = _angles[0] + 360f;
				lower = count - 1;
			}
			else if (lower < 0)
			{
				lower = count - 1;
				lowerAngle = _angles[count - 1] - 360f;
				upperAngle = _angles[upper];
			}
			else
			{
				lowerAngle = _angles[lower];
				upperAngle = _angles[upper];
			}
			float limit = upperAngle > lowerAngle
				? Mathf.Lerp(_limits[lower], _limits[upper], (angle - lowerAngle) / (upperAngle - lowerAngle))
				: _limits[upper];
			return Mathf.Max(Min, limit);
		}

		// Update, so the limit's in before the platforms' LateUpdate.
		void Update()
		{
			if (Gun == null || Traverse == null || Traverse.Transform == null) return;
			float lim = MinAt(Yaw());
			var eulerLimits = Gun.LocalEulerLimits;
			if (eulerLimits.x != lim)
			{
				eulerLimits.x = lim;
				Gun.LocalEulerLimits = eulerLimits;
			}
			if (!Gun.enabled || Destroyed != null && Destroyed(Gun)) return;
			// The limits only clamp inside doRotation, which an idle unstabilised gun never runs. It'd sit under the new
			// limit forever.
			if (Gun.GetCurrentEulerAngle() < lim - 0.001f)
			{
				Enforce?.Invoke(Gun);
			}
		}
	}
}
