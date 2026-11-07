// LiveRound.Init resets RhaPenetrationOverride to 0 (resetLocalValues): an override set before Init reaches only the
// ShotInfo; the round itself then uses Info.RhaPenetration x (speed / MaxSpeed)^2. penAfterInit sets it again after Init
// (both users do since 2026-11-07: the APS from the start, the T-90M's air burst since its fix).
using System;
using System.Reflection;
using GHPC;
using GHPC.Weapons;
using UnityEngine;

namespace T90A.Common
{
	internal static class FragmentCone
	{
		internal static AmmoType Type(string name, float mass, float area, float coeff, float caliber, float pen, float penSpeed)
		{
			var a = new AmmoType();
			foreach (var fi in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (!fi.IsLiteral && !fi.IsInitOnly) fi.SetValue(a, fi.GetValue(LiveRound.SpallAmmoType));
			a.Name = name;
			a.IsSpallType = true;
			a.Mass = mass;
			a.SectionalArea = area;
			a.Coeff = coeff;
			a.Caliber = caliber;
			a.RhaPenetration = pen;
			a.MuzzleVelocity = penSpeed;
			a.VisualType = LiveRoundMarshaller.LiveRoundVisualType.Invisible;
			return a;
		}

		internal static int Spawn(AmmoType info, Vector3 at, Vector3 fwd, float halfAngle, int count, float speed, float pen,
			Unit shooter, bool npc, LiveRound parent, string name, Func<float> random, bool penAfterInit, Vector3 fallbackRight, Action<LiveRound> spawned = null)
		{
			var marshaller = LiveRoundMarshaller.Instance;
			if (marshaller == null || count <= 0) return 0;
			var right = Vector3.Cross(Vector3.up, fwd);
			right = right.sqrMagnitude > 1e-6f ? right.normalized : fallbackRight;
			var up = Vector3.Cross(fwd, right);
			float tan = Mathf.Tan(halfAngle * Mathf.Deg2Rad);
			for (int i = 0; i < count; i++)
			{
				// sqrt spreads them evenly over the cone's cross-section, not bunched in the middle
				float rr = tan * Mathf.Sqrt(random()), phi = 2f * Mathf.PI * random();
				var dir = (fwd + rr * (Mathf.Cos(phi) * right + Mathf.Sin(phi) * up)).normalized;
				var e = marshaller.GetRoundOfVisualType(LiveRoundMarshaller.LiveRoundVisualType.Invisible);
				e.Info = info;
				e.RhaPenetrationOverride = pen;
				e.CurrentSpeed = speed;
				e.MaxSpeed = speed;
				e.NpcRound = npc;
				e.IsSpall = true;
				e.Shooter = shooter;
				e.transform.position = at;
				e.transform.forward = dir;
				e.Init(parent);
				if (penAfterInit) e.RhaPenetrationOverride = pen;
				e.gameObject.name = $"{name} {e.ID}";
				spawned?.Invoke(e);
			}
			return count;
		}
	}
}
