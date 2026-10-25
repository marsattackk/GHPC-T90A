using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using DC = GHPC.Equipment.DestructibleComponent;

namespace T90A.Common
{
	internal static class Shtora
	{
		internal sealed class Settings
		{
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

			internal void Setup(Vehicle v, Settings s)
			{
				Vehicle = v; S = s;
				_turret = Followers.TurretBone(v);
				_gun = ModuleBuilder.MainWeapon(v);
				if (s.OnAtSpawn) On = true;
				Stations.Add(this);
			}

			internal void FindEmitters()
			{
				var set = Vehicle.GetComponent<ModuleBuilder.ModuleSet>();
				if (set == null || _turret == null) return;
				foreach (var (m, dc) in set.Built)
				{
					var local = _turret.InverseTransformPoint(Vehicle.transform.TransformPoint(m.Bounds.center));
					if (m.Id == S.EmitterLeft) { Left = dc; LeftLocal = local; }
					else if (m.Id == S.EmitterRight) { Right = dc; RightLocal = local; }
				}
			}

			internal void Set(bool on)
			{
				if (on == On) return;
				On = on;
			}

			bool Alive(DC dc) => dc != null && !dc.IsDestroyed;
			internal bool LeftWorks => On && Alive(Left);
			internal bool RightWorks => On && Alive(Right);
			internal bool Working => (LeftWorks || RightWorks) && Vehicle != null && !Vehicle.Neutralized && _gun?.MuzzleIdentity != null;

			internal Vector3 EmitterCentre()
			{
				var pts = new List<Vector3>();
				if (LeftWorks) pts.Add(_turret.TransformPoint(LeftLocal));
				if (RightWorks) pts.Add(_turret.TransformPoint(RightLocal));
				if (pts.Count == 0) return _gun.MuzzleIdentity.position;
				return pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
			}

			internal Vector2 Bearing(Vector3 world)
			{
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
				if (On || Vehicle == null || Vehicle.Neutralized || (PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle)) return;
				Set(true);
			}

			void OnDestroy() => Stations.Remove(this);
		}

		internal static readonly List<Station> Stations = new List<Station>();

		internal static Station Of(Unit u) => u == null ? null : Stations.FirstOrDefault(s => s != null && s.Vehicle == u);

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
		static readonly System.Random Rng = new System.Random();
		static float Draw() => (float)Rng.NextDouble();

		static bool Jammable(AmmoType a, Settings s) =>
			a != null && a.Guidance == AmmoType.GuidanceType.SACLOS && a.Name != null && s.Missiles.Any(p => a.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

		static Station TargetOf(Vector3 launch, Vector3 at)
		{
			Station best = null; float bestOff = float.MaxValue;
			var dir = at - launch;
			if (dir.sqrMagnitude < 1f) return null;
			foreach (var st in Stations)
			{
				if (st == null || st.Vehicle == null) continue;
				var to = st.Vehicle.transform.position - launch;
				float a = Vector3.Angle(dir, to);
				if (a > 5f) continue;
				if (a < bestOff) { bestOff = a; best = st; }
			}
			return best;
		}

		static void Step(LiveRound r)
		{
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
				f.Target = TargetOf(f.Launch, r.transform.position);
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

		[HarmonyPatch(typeof(LiveRound), "DoUpdateImpl")]
		static class RoundPatch
		{
			static void Prefix(LiveRound __instance)
			{
				if (!__instance.Guided || Stations.Count == 0 || __instance.IsDestroyed) return;
				try { Step(__instance); }
				catch (Exception e) { MelonLogger.Error("Shtora jammer failed on a missile: " + e.Message); Flights[__instance] = new Flight { Picked = true, Seen = __instance.TimeTraveled}; }
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
