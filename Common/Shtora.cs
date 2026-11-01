using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using DC = GHPC.Equipment.DestructibleComponent;

namespace T90A.Common
{
	internal static class Shtora
	{
		internal sealed class Settings
		{
			public string Tag;
			public string EmitterLeft, EmitterRight;
			public float SectorAz = 20f;
			public float SectorEl = 2.25f;
			public float BreakChance = 0.6f;
			public Vector2 Relock = new Vector2(0.3f, 0.7f);
			public float JitterDeg = 30f;
			public string[] Missiles = { };
			public bool OnAtSpawn;
			internal Settings Copy() => (Settings)MemberwiseClone();
		}

		internal sealed class Station : MonoBehaviour
		{
			internal Settings S;
			internal Vehicle Vehicle;
			internal bool On { get; private set; }

			internal DC Left, Right;
			internal Vector3 LeftLocal, RightLocal;
			Transform _turret;
			WeaponSystem _gun;
			bool _subscribed;

			bool _setUp;

			internal void Setup(Vehicle v, Settings s)
			{
				if (_setUp) return;
				_setUp = true;
				Vehicle = v; S = s;
				_turret = Followers.TurretBone(v);
				_gun = ModuleBuilder.MainWeapon(v);
				if (s.OnAtSpawn) On = true;
				Stations.Add(this);
			}

			internal void FindEmitters()
			{
				var set = Vehicle.GetComponent<ModuleBuilder.ModuleSet>();
				if (set == null || _turret == null)
				{
					DonorParts.WarnOnce("shtora emitters", $"{S.Tag} Shtora-1: no {(set == null ? "modules" : "turret")} found; the jammer has no emitters.");
					return;
				}
				foreach (var (m, dc) in set.Built)
				{
					bool left = m.Id == S.EmitterLeft, right = !left && m.Id == S.EmitterRight;
					if (!left && !right || m.Boxes == null || m.Boxes.Count == 0) continue;
					var local = _turret.InverseTransformPoint(Vehicle.transform.TransformPoint(m.Bounds.center));
					if (left) { Left = dc; LeftLocal = local; }
					else { Right = dc; RightLocal = local; }
				}
				if (Left == null || Right == null)
					DonorParts.WarnOnce("shtora emitters", $"{S.Tag} Shtora-1: the jammer's emitter module(s) {(Left == null ? S.EmitterLeft : "")}{(Left == null && Right == null ? " and " : "")}{(Right == null ? S.EmitterRight : "")} not found; the jammer doesn't work on that side.");
			}

			internal void Set(bool on)
			{
				if (on == On) return;
				On = on;
			}

			bool Alive(DC dc) => dc != null && !dc.IsDestroyed;
			internal bool LeftWorks => On && Alive(Left);
			internal bool RightWorks => On && Alive(Right);
			internal bool Working => (LeftWorks || RightWorks) && Vehicle != null && !Vehicle.Neutralized && _turret != null && _gun != null && _gun.MuzzleIdentity != null;

			internal Vector3 EmitterCentre()
			{
				bool l = LeftWorks, r = RightWorks;
				if (l && r) return (_turret.TransformPoint(LeftLocal) + _turret.TransformPoint(RightLocal)) * 0.5f;
				if (l) return _turret.TransformPoint(LeftLocal);
				if (r) return _turret.TransformPoint(RightLocal);
				return _gun.MuzzleIdentity.position;
			}

			internal Vector2 Bearing(Vector3 world)
			{
				if (_turret == null || _gun == null || _gun.MuzzleIdentity == null) return new Vector2(180f, 0f);
				var fwd = _gun.MuzzleIdentity.forward;
				var right = Vector3.Cross(_turret.up, fwd).normalized;
				var up = Vector3.Cross(fwd, right);
				var d = world - EmitterCentre();
				float az = Mathf.Atan2(Vector3.Dot(d, right), Vector3.Dot(d, fwd)) * Mathf.Rad2Deg;
				float el = Mathf.Atan2(Vector3.Dot(d, up), new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, fwd)).magnitude) * Mathf.Rad2Deg;
				return new Vector2(az, el);
			}

			internal bool Covers(Vector3 world)
			{
				if (!Working) return false;
				var b = Bearing(world);
				if (Mathf.Abs(b.y) > S.SectorEl || Mathf.Abs(b.x) > S.SectorAz) return false;
				return b.x <= 0f ? LeftWorks : RightWorks;
			}

			void Update()
			{
				if (Vehicle == null) return;
				if (Vehicle.Neutralized) { if (On) Set(false); return; }
				bool player = PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle;
				if (player || On) return;
				var ib = Vehicle.InfoBroker;
				if (ib == null) return;
				if (!_subscribed)
				{
					_subscribed = true;
					ib.WasFiredUpon += () => AiOn();
					ib.WasNearMissed += () => AiOn();
					ib.DangerousThreatDetected += () => AiOn();
				}
				if (ib.AI != null && ib.AI.HasTarget) AiOn();
			}

			void AiOn()
			{
				// WasFiredUpon still comes in after the knock-out.
				if (On || Vehicle == null || Vehicle.Neutralized || (PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle)) return;
				Set(true);
			}

			void OnDestroy() => Stations.Remove(this);
		}

		internal static readonly List<Station> Stations = new List<Station>();

		internal static Station Of(Unit u)
		{
			if (u == null) return null;
			for (int i = 0; i < Stations.Count; i++) { var s = Stations[i]; if (s != null && s.Vehicle == u) return s; }
			return null;
		}

		internal static void Apply(Vehicle v, Settings s)
		{
			var st = v.gameObject.GetComponent<Station>() ?? v.gameObject.AddComponent<Station>();
			st.Setup(v, s);
			st.FindEmitters();
		}

		internal sealed class Flight
		{
			public float Seen;
			public Vector3 Launch;
			public Station Target;
			public bool Picked, Rolled, Broken, Captured;
			public float RelockAt;
			public float Noise;
		}

		static readonly Dictionary<LiveRound, Flight> Flights = new Dictionary<LiveRound, Flight>();
		// GHPC reseeds UnityEngine.Random from the clock all over the place (RandomUtil.RandomizeUnityRandom).
		static readonly System.Random Rng = new System.Random();
		static float Draw() => (float)Rng.NextDouble();

		static bool Jammable(AmmoType a, Settings s) =>
			a != null && a.Guidance == AmmoType.GuidanceType.SACLOS && a.Name != null && s.Missiles.Any(p => a.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

		const float AimLineM = 4f, AimLinePerKm = 2f, HeadingM = 5f, HeadingPerKm = 17.5f;

		// GHPC never gives a round its target, WeaponsModule.Fire passes none to LiveRound.Init.
		static Station TargetOf(LiveRound r, Vector3 launch, Vector3 at)
		{
			var shooter = r.Shooter;
			bool player = shooter != null && PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == shooter;
			if (shooter != null && !player && shooter.InfoBroker?.AI is GHPC.AI.UnitAI ai && ai.Target?.Owner is Unit aimed)
			{
				var st = Of(aimed);
				return st != null && Hostile(shooter, st.Vehicle) ? st : null;
			}
			var line = GuidanceLine(r);
			if (line.HasValue) return OnLine(shooter, line.Value.origin, line.Value.direction, AimLineM, AimLinePerKm);
			var dir = at - launch;
			if (dir.sqrMagnitude < 1f) return null;
			return OnLine(shooter, launch, dir.normalized, HeadingM, HeadingPerKm);
		}

		static bool Hostile(Unit shooter, Unit target) => shooter == null || target == null || shooter.Allegiance != target.Allegiance;

		static Ray? GuidanceLine(LiveRound r)
		{
			if (r.Shooter == null) return null;
			foreach (var g in r.Shooter.GetComponentsInChildren<MissileGuidanceUnit>(true))
				if (g != null && g.AimElement != null && g.CurrentMissiles != null && g.CurrentMissiles.Contains(r))
					return new Ray(g.AimElement.position, g.AimElement.forward);
			return null;
		}

		static Station OnLine(Unit shooter, Vector3 from, Vector3 dir, float baseM, float perKm)
		{
			Station best = null; float bestOff = float.MaxValue;
			foreach (var st in Stations)
			{
				if (st == null || st.Vehicle == null || st.Vehicle == shooter || !Hostile(shooter, st.Vehicle)) continue;
				var to = (st.Vehicle.Center != null ? st.Vehicle.Center.position : st.Vehicle.transform.position) - from;
				float along = Vector3.Dot(to, dir);
				if (along <= 0f) continue;
				float off = (to - dir * along).magnitude;
				if (off > baseM + perKm * along / 1000f || off >= bestOff) continue;
				bestOff = off; best = st;
			}
			return best;
		}

		static void Step(LiveRound r)
		{
			// LiveRounds are pooled, a reused one restarts TimeTraveled at 0.
			if (!Flights.TryGetValue(r, out var f) || r.TimeTraveled < f.Seen)
			{
				f = new Flight { Launch = r.transform.position, Noise = Draw() * 100f };
				Flights[r] = f;
				if (Flights.Count > 64) foreach (var k in Flights.Keys.Where(k => k == null || k.IsDestroyed).ToList()) Flights.Remove(k);
			}
			f.Seen = r.TimeTraveled;
			if (!f.Picked)
			{
				if ((r.transform.position - f.Launch).sqrMagnitude < 50f * 50f) return;
				f.Picked = true;
				f.Target = TargetOf(r, f.Launch, r.transform.position);
				if (f.Target == null) return;
				if (!Jammable(r.Info, f.Target.S)) { f.Target = null; return; }
			}
			var st = f.Target;
			if (st == null || st.Vehicle == null) return;
			bool covered = st.Covers(f.Launch);
			if (!f.Rolled && covered)
			{
				f.Rolled = true;
				f.Broken = Draw() < st.S.BreakChance;
				var span = Vector3.Distance(f.Launch, st.Vehicle.transform.position);
				f.RelockAt = span * Mathf.Lerp(st.S.Relock.x, st.S.Relock.y, Draw());
			}
			if (!f.Broken) return;
			float flown = Vector3.Distance(r.transform.position, f.Launch);
			if (!f.Captured)
			{
				if (!covered || flown < f.RelockAt) return;
				f.Captured = true;
			}
			else if (!covered)
			{
				f.Captured = false; f.Broken = false;
				return;
			}
			var los = (st.Vehicle.Center != null ? st.Vehicle.Center.position : st.Vehicle.transform.position) - f.Launch;
			var aim = f.Launch + los;
			var cmd = Vector3.ProjectOnPlane(aim - st.EmitterCentre(), los);
			if (cmd.magnitude < 0.05f) cmd = Vector3.ProjectOnPlane(Vector3.down, los);
			float jitter = (Mathf.PerlinNoise(f.Noise, Time.time * 0.7f) - 0.5f) * 2f * st.S.JitterDeg;
			cmd = Quaternion.AngleAxis(jitter, los) * cmd.normalized;
			var fwd = r.transform.forward;
			r.GoalVector = (fwd + cmd).normalized * Mathf.Max(1f, r.GoalVector.magnitude);
		}

		// No missile tracker in GHPC, every guided round from every shooter just turns towards GoalVector in here.
		[HarmonyPatch(typeof(LiveRound), "DoUpdateImpl")]
		static class RoundPatch
		{
			static void Prefix(LiveRound __instance)
			{
				if (!__instance.Guided || Stations.Count == 0 || __instance.IsDestroyed) return;
				try { Step(__instance); }
				catch (Exception e) { PatchGuard.Failed("Shtora jammer on a missile", e); Flights[__instance] = new Flight { Picked = true, Seen = __instance.TimeTraveled}; }
			}
		}

		[HarmonyPatch(typeof(PlayerInput), "AddSmokeButtons")]
		static class MenuPatch
		{
			static readonly System.Reflection.MethodInfo Add = Reflect.Method(typeof(PlayerInput), "AddMenuButton", new[] { typeof(string), typeof(UnityAction) });

			static void Postfix(PlayerInput __instance)
			{
				var st = Of(__instance.CurrentPlayerUnit);
				if (st == null || Add == null) return;
				string state = !st.On ? "OFF" : st.Working ? "ON" : "ON, broken";
				Add.Invoke(__instance, new object[] { $"Shtora-1/Toggle IR Jammer ({state})", (UnityAction)(() => st.Set(!st.On)) });
			}
		}
	}
}
