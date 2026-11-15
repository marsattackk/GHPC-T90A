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
	// The Shtora-1 jammer: its sector, the chance a SACLOS missile loses lock, and the relock. The screen side is
	// ShtoraScreen.
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

			internal string Why = "off at spawn";
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
				Vehicle = v;
				S = s;
				_turret = Followers.TurretBone(v);
				_gun = ModuleBuilder.MainWeapon(v);
				if (s.OnAtSpawn)
				{
					On = true;
					Why = "on at spawn";
				}
				Stations.Add(this);
			}

			internal string FindEmitters()
			{
				var set = Vehicle.GetComponent<ModuleBuilder.ModuleSet>();
				if (set == null || _turret == null)
				{
					DonorParts.WarnOnce(
						"shtora emitters",
						$"{S.Tag} Shtora-1: no {(set == null ? "modules" : "turret")} found; the jammer has no emitters.");
					return "no modules / turret: the jammer has no emitters";
				}
				foreach (var (module, component) in set.Built)
				{
					bool left = module.Id == S.EmitterLeft, right = !left && module.Id == S.EmitterRight;
					if (!left && !right || module.Boxes == null || module.Boxes.Count == 0) continue;
					var local = _turret.InverseTransformPoint(Vehicle.transform.TransformPoint(module.Bounds.center));
					if (left)
					{
						Left = component;
						LeftLocal = local;
					}
					else
					{
						Right = component;
						RightLocal = local;
					}
				}
				if (Left == null || Right == null)
				{
					string missing = Left == null && Right == null
						? "both emitter modules"
						: Left == null ? "the left emitter module" : "the right emitter module";
					DonorParts.WarnOnce("shtora emitters", $"{S.Tag} Shtora-1: {missing} not found; the jammer doesn't work on that side.");
				}
				return $"emitters left {(Left != null ? $"at {LeftLocal}" : "MISSING")}, right {(Right != null ? $"at {RightLocal}" : "MISSING")} (turret frame)";
			}

			internal void Set(bool on, string why)
			{
				if (on == On) return;
				On = on;
				Why = why;
			}

			bool Alive(DC dc) => dc != null && !dc.IsDestroyed;
			internal bool LeftWorks => On && Alive(Left);
			internal bool RightWorks => On && Alive(Right);
			internal bool Working
				=> (LeftWorks || RightWorks)
					&& Vehicle != null
					&& !Vehicle.Neutralized
					&& _turret != null
					&& _gun != null
					&& _gun.MuzzleIdentity != null;

			internal Vector3 EmitterCentre()
			{
				bool leftWorks = LeftWorks, rightWorks = RightWorks;
				if (leftWorks && rightWorks) return (_turret.TransformPoint(LeftLocal) + _turret.TransformPoint(RightLocal)) * 0.5f;
				if (leftWorks) return _turret.TransformPoint(LeftLocal);
				if (rightWorks) return _turret.TransformPoint(RightLocal);
				return _gun.MuzzleIdentity.position;
			}

			internal Vector2 Bearing(Vector3 world)
			{
				if (_turret == null || _gun == null || _gun.MuzzleIdentity == null) return new Vector2(180f, 0f);
				ShtoraMath.Bearing(
					world.ToV3(),
					EmitterCentre().ToV3(),
					_gun.MuzzleIdentity.forward.ToV3(),
					_turret.up.ToV3(),
					out float azimuth,
					out float elevation);
				return new Vector2(azimuth, elevation);
			}

			internal bool Covers(Vector3 world)
			{
				if (!Working) return false;
				var angles = Bearing(world);
				return ShtoraMath.InSector(angles.x, angles.y, S.SectorAz, S.SectorEl, LeftWorks, RightWorks);
			}

			void Update()
			{
				if (Vehicle == null) return;
				if (Vehicle.Neutralized)
				{
					if (On) Set(false, "unit knocked out");
					return;
				}
				bool player = PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle;
				if (player || On) return;
				var infoBroker = Vehicle.InfoBroker;
				if (infoBroker == null) return;
				if (!_subscribed)
				{
					_subscribed = true;
					infoBroker.WasFiredUpon += () => AiOn("fired upon");
					infoBroker.WasNearMissed += () => AiOn("near miss");
					infoBroker.DangerousThreatDetected += () => AiOn("dangerous threat seen");
				}
				if (infoBroker.AI != null && infoBroker.AI.HasTarget) AiOn("AI has a target");
			}

			void AiOn(string why)
			{
				// WasFiredUpon still comes in after the knock-out.
				if (On
					|| Vehicle == null
					|| Vehicle.Neutralized
					|| (PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle))
					return;
				Set(true, "AI: " + why);
			}

			void OnDestroy() => Stations.Remove(this);
		}

		internal static readonly List<Station> Stations = new List<Station>();

		internal static Station Of(Unit u)
		{
			if (u == null) return null;
			for (int i = 0; i < Stations.Count; i++)
			{
				var station = Stations[i];
				if (station != null && station.Vehicle == u) return station;
			}
			return null;
		}

		internal static string Apply(Vehicle v, Settings s)
		{
			var station = v.gameObject.GetComponent<Station>() ?? v.gameObject.AddComponent<Station>();
			station.Setup(v, s);
			CheckList(s);
			ShtoraScreen.JammerState = StateText;
			return station.FindEmitters();
		}

		static string StateText(Unit unit)
		{
			var jam = Of(unit);
			if (jam == null) return null;
			if (!jam.On) return "IR jammer off";
			if (jam.LeftWorks && jam.RightWorks) return "IR jammer on";
			return jam.LeftWorks ? "IR jammer on, left only" : jam.RightWorks ? "IR jammer on, right only" : "IR jammer broken";
		}

		static readonly HashSet<string> Told = new HashSet<string>();

		static List<string> LoadedSaclos()
			=> Resources.FindObjectsOfTypeAll<GHPC.Weaponry.AmmoCodexScriptable>().Select(c => c.AmmoType)
				.Where(a => a != null && a.Guidance == AmmoType.GuidanceType.SACLOS && a.Name != null)
				.Select(a => a.Name)
				.Distinct()
				.ToList();

		// Once per scene and port. A missile loaded later is reported at its first flight.
		static readonly HashSet<string> Checked = new HashSet<string>();

		static void CheckList(Settings s)
		{
			if (!Checked.Add(UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle + "|" + s.Tag)) return;
			var loaded = LoadedSaclos();
			foreach (var name in loaded) Unlisted(name, s, loaded);
		}

		static void Unlisted(string name, Settings s, List<string> loaded = null)
		{
			if (s.Missiles.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || !Told.Add(s.Tag + "|" + name))
				return;
			loaded = loaded ?? LoadedSaclos();
			if (!loaded.Contains(name)) loaded.Add(name);
			var idle = s.Missiles
				.Where(p => !loaded.Any(l => l.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
				.ToList();
			MelonLogger.Warning($"{s.Tag} Shtora-1: the SACLOS missile \"{name}\" is not on the jammer's list, so it is not jammed (added by another mod, or renamed by a game update?)."
				+ (idle.Count > 0 ? $" On the list but matching no missile loaded now: {string.Join(", ", idle)}." : ""));
		}

		internal sealed class Flight
		{
			public float Seen;
			public Vector3 Launch;
			public Station Target;
			public bool Picked, Rolled, Broken, Captured;
			public float RelockAt;
			public float Noise;
			public string Story;
		}

		static readonly Dictionary<LiveRound, Flight> Flights = new Dictionary<LiveRound, Flight>();
		static float Draw() => ModRandom.Value;

		internal static string Fate(LiveRound r) => r != null && Flights.TryGetValue(r, out var flight) ? flight.Story ?? "" : "";

		static bool Jammable(AmmoType a, Settings s) =>
			a != null
				&& a.Guidance == AmmoType.GuidanceType.SACLOS
				&& a.Name != null
				&& s.Missiles.Any(p => a.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

		const float AimLineM = 4f, AimLinePerKm = 2f, HeadingM = 5f, HeadingPerKm = 17.5f;

		// GHPC never gives a round its target. WeaponsModule.Fire passes none to LiveRound.Init.
		static Station TargetOf(LiveRound r, Vector3 launch, Vector3 at)
		{
			var shooter = r.Shooter;
			bool player = shooter != null && PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == shooter;
			if (shooter != null && !player && shooter.InfoBroker?.AI is GHPC.AI.UnitAI shooterAi && shooterAi.Target?.Owner is Unit aimed)
			{
				var station = Of(aimed);
				return station != null && Hostile(shooter, station.Vehicle) ? station : null;
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
			foreach (var guidance in r.Shooter.GetComponentsInChildren<MissileGuidanceUnit>(true))
				if (guidance != null
					&& guidance.AimElement != null
					&& guidance.CurrentMissiles != null
					&& guidance.CurrentMissiles.Contains(r))
					return new Ray(guidance.AimElement.position, guidance.AimElement.forward);
			return null;
		}

		static Station OnLine(Unit shooter, Vector3 from, Vector3 dir, float baseM, float perKm)
		{
			Station best = null;
			float bestOff = float.MaxValue;
			foreach (var station in Stations)
			{
				if (station == null || station.Vehicle == null || station.Vehicle == shooter || !Hostile(shooter, station.Vehicle))
					continue;
				var offset = (station.Vehicle.Center != null ? station.Vehicle.Center.position : station.Vehicle.transform.position) - from;
				float along = Vector3.Dot(offset, dir);
				if (along <= 0f) continue;
				float off = (offset - dir * along).magnitude;
				if (off > baseM + perKm * along / 1000f || off >= bestOff) continue;
				bestOff = off;
				best = station;
			}
			return best;
		}

		static void Step(LiveRound r)
		{
			// LiveRounds are pooled and a reused one restarts TimeTraveled at 0.
			if (!Flights.TryGetValue(r, out var flight) || r.TimeTraveled < flight.Seen)
			{
				flight = new Flight { Launch = r.transform.position, Noise = Draw() * 100f };
				Flights[r] = flight;
				if (Flights.Count > 64)
					foreach (var staleRound in Flights.Keys.Where(round => round == null || round.IsDestroyed).ToList())
						Flights.Remove(staleRound);
			}
			flight.Seen = r.TimeTraveled;
			if (!flight.Picked)
			{
				if ((r.transform.position - flight.Launch).sqrMagnitude < 50f * 50f) return;
				flight.Picked = true;
				flight.Target = TargetOf(r, flight.Launch, r.transform.position);
				if (flight.Target == null)
				{
					flight.Story = "not engaging an enemy unit with the jammer";
					return;
				}
				if (!Jammable(r.Info, flight.Target.S))
				{
					if (r.Info != null && r.Info.Guidance == AmmoType.GuidanceType.SACLOS && r.Info.Name != null)
						Unlisted(r.Info.Name, flight.Target.S);
					flight.Story = "not a missile the jammer works on";
					flight.Target = null;
					return;
				}
			}
			var station = flight.Target;
			if (station == null || station.Vehicle == null) return;
			bool covered = station.Covers(flight.Launch);
			if (!flight.Rolled && covered)
			{
				flight.Rolled = true;
				flight.Broken = Draw() < station.S.BreakChance;
				var span = Vector3.Distance(flight.Launch, station.Vehicle.transform.position);
				flight.RelockAt = span * Mathf.Lerp(station.S.Relock.x, station.S.Relock.y, Draw());
				var angles = station.Bearing(flight.Launch);
				flight.Story = flight.Broken
					? $"in the sector (az {angles.x:+0.0;-0.0}, el {angles.y:+0.0;-0.0}): guidance to be broken at {flight.RelockAt:0} m of {span:0}"
					: $"in the sector (az {angles.x:+0.0;-0.0}, el {angles.y:+0.0;-0.0}): roll held, guidance kept";
			}
			if (!flight.Broken) return;
			float flown = Vector3.Distance(r.transform.position, flight.Launch);
			if (!flight.Captured)
			{
				if (!covered || flown < flight.RelockAt) return;
				flight.Captured = true;
				flight.Story = $"captured at {flown:0} m";
			}
			else if (!covered)
			{
				flight.Captured = false;
				flight.Broken = false;
				flight.Story += $"; released at {flown:0} m (jammer off / emitters lost / out of the sector)";
				return;
			}
			var los = (station.Vehicle.Center != null ? station.Vehicle.Center.position : station.Vehicle.transform.position)
				- flight.Launch;
			var aim = flight.Launch + los;
			var cmd = Vector3.ProjectOnPlane(aim - station.EmitterCentre(), los);
			if (cmd.magnitude < 0.05f) cmd = Vector3.ProjectOnPlane(Vector3.down, los);
			float jitter = (Mathf.PerlinNoise(flight.Noise, Time.time * 0.7f) - 0.5f) * 2f * station.S.JitterDeg;
			cmd = Quaternion.AngleAxis(jitter, los) * cmd.normalized;
			var fwd = r.transform.forward;
			r.GoalVector = (fwd + cmd).normalized * Mathf.Max(1f, r.GoalVector.magnitude);
		}

		// GHPC has no missile tracker. Every guided round from every shooter just turns towards GoalVector in here.
		[HarmonyPatch(typeof(LiveRound), "DoUpdateImpl")]
		static class RoundPatch
		{
			static void Prefix(LiveRound __instance)
			{
				if (!__instance.Guided || Stations.Count == 0 || __instance.IsDestroyed) return;
				try { Step(__instance); }
				catch (Exception exception)
				{
					PatchGuard.Failed("Shtora jammer on a missile", exception);
					Flights[__instance] = new Flight { Picked = true, Seen = __instance.TimeTraveled, Story = "error" };
				}
			}
		}

		[HarmonyPatch(typeof(PlayerInput), "AddSmokeButtons")]
		static class MenuPatch
		{
			static readonly System.Reflection.MethodInfo Add = Reflect.Method(
				typeof(PlayerInput),
				"AddMenuButton",
				new[] { typeof(string), typeof(UnityAction) });

			static void Postfix(PlayerInput __instance)
			{
				var station = Of(__instance.CurrentPlayerUnit);
				if (station == null || Add == null) return;
				string state = !station.On ? "OFF" : station.Working ? "ON" : "ON, broken";
				Add.Invoke(
					__instance,
					new object[] { $"Shtora-1/Toggle IR Jammer ({state})", (UnityAction)(() => station.Set(!station.On, "player")) });
			}
		}
	}
}
