using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GHPC;
using GHPC.AI;
using GHPC.AI.Interfaces;
using GHPC.AI.Targeting;
using GHPC.Effects;
using GHPC.Equipment;
using GHPC.PhysicsHelpers;
using GHPC.Player;
using GHPC.UI.Hud;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace T90A.Common
{
	internal static class ShtoraScreen
	{
		internal enum Look { ControlPanel, ScreenPage }

		internal sealed class Settings
		{
			public string Tag;
			public float PreciseHalfAz = 45f;
			public float PreciseStep = 3.75f;
			public Look Display = Look.ControlPanel;
			public string PageTitle = "СПЗ";
			public string MenuName = "Shtora-1";
			public bool PageTone = true;
			public Vector2 HeadEl = new Vector2(-5f, 25f);
			public bool Automatic = true;
			public float OwnShotBlock = 15f;
			public float LaunchAngle = 12f;
			public Vector2 Distance = new Vector2(50f, 80f);
			public float CloudBuild = 3f, CloudLife = 20f;
			public float CloudSpread = 8f;
			public float CloudSizeScale = 4f;
			public int CloudParticles = 60;
			public float CloudRise = 0f;
			public float FuseSeconds = 1f;
			public float TubeHeight = 2.3f;
			public string[] WhiteSmokeDonors = { "LEO2A3", "M60A3", "LEO1A3", "M1" };
			public float CloudBottomHeight = 2f;
			public int WhiteBottoms = 5, WhiteTracers = 6;
			public float AiRelase = 10f;
			public Vector2 AiLaseDelay = new Vector2(0.5f, 1f);
			public float SameThreatDeg = 20f;
			public bool AlarmTone = true;
			public bool PanelShown = true;
			public bool PanelAlways;
			public bool BlindAi = true;
			public Vector2 BlindRange = new Vector2(0.9f, 1.1f);
			internal Settings Copy() => (Settings)MemberwiseClone();
		}

		internal sealed class Station : MonoBehaviour
		{
			internal Settings S;
			internal Vehicle Vehicle;
			internal VehicleSmokeManager Smoke;
			internal float LastOwnShot = -1e9f;
			internal readonly List<(float az, float until)> Screens = new List<(float, float)>();
			internal (float az, float at, bool precise) LastWarning = (0f, -1e9f, false);
			internal Vector3 LastLaserFrom;
			internal VehicleSmokeManager.SmokeSlot[] Slots;
			internal float[] SlotAz;
			Transform _turret;

			bool _setUp;

			internal void Setup(Vehicle v, Settings s)
			{
				if (_setUp) return;
				_setUp = true;
				Vehicle = v; S = s;
				_turret = Followers.TurretBone(v);
				Smoke = v.GetComponentInChildren<VehicleSmokeManager>(true);
				Stations.Add(this);
				var main = ModuleBuilder.MainWeapon(v);
				if (main != null) main.Fired += (a, r) => { if (a != null && a.Caliber >= 20f) LastOwnShot = Time.time; };
				if (Smoke == null || _turret == null) return;
				if (SlotsField == null || PrefabField == null) return;
				Slots = SlotsField.GetValue(Smoke) as VehicleSmokeManager.SmokeSlot[];
				if (Slots == null) return;
				var fwdRef = ForwardRefField?.GetValue(Smoke) as Transform;
				if (fwdRef == null) fwdRef = _turret;
				SlotAz = Slots.Select(sl =>
				{
					var axis = sl.DisplayMesh != null ? sl.DisplayMesh.transform.forward : fwdRef.forward;
					var flat = Vector3.ProjectOnPlane(axis, fwdRef.up);
					return Vector3.SignedAngle(Vector3.ProjectOnPlane(fwdRef.forward, fwdRef.up), flat, fwdRef.up);
				}).ToArray();
				for (int i = 0; i < Slots.Length; i++) Slots[i].Angle = SlotAz[i];
				LaunchAngleField?.SetValue(Smoke, s.LaunchAngle);
				DistanceField?.SetValue(Smoke, new Vector2(LandingFor(s.Distance.x, s), LandingFor(s.Distance.y, s)));
				var basePrefab = PrefabField.GetValue(Smoke) as GameObject;
				var grenade = Grenade3D17(basePrefab, s);
				var pool = ObjectPoolManager.Instance;
				if (grenade != null && pool != null && (!PooledIn.TryGetValue(grenade, out var was) || was != pool))
				{
					pool.Register(grenade);
					PooledIn[grenade] = pool;
				}
				if (grenade != null) PrefabField.SetValue(Smoke, grenade);
			}

			internal Vector3 TurretPoint => _turret != null ? _turret.position + _turret.up * 0.5f
				: Vehicle.Center != null ? Vehicle.Center.position : Vehicle.transform.position;

			internal Vector2 Bearing(Vector3 world)
			{
				var d = world - _turret.position;
				var flat = Vector3.ProjectOnPlane(d, _turret.up);
				float az = Vector3.SignedAngle(Vector3.ProjectOnPlane(_turret.forward, _turret.up), flat, _turret.up);
				float el = Mathf.Atan2(Vector3.Dot(d, _turret.up), flat.magnitude) * Mathf.Rad2Deg;
				return new Vector2(az, el);
			}

			internal bool HatchOpen()
			{
				var crew = Vehicle.CrewManager?.CrewMembers;
				if (crew == null) return false;
				foreach (var c in crew)
				{
					if (c == null) continue;
					if (c.ExitHatch != null && c.ExitHatch.Openness > 0.1f) return true;
					if (c.Postures != null && c.CurrentPostureIndex >= 0 && c.CurrentPostureIndex < c.Postures.Length && c.Postures[c.CurrentPostureIndex].Posture != GHPC.Crew.CrewRidePosture.Buttoned) return true;
				}
				return false;
			}

			internal void NoteAi(string s) => Note(s);

			void Note(string s)
			{
			}

			internal void Lased(Vector3 from)
			{
				if (Vehicle == null || Vehicle.Neutralized || _turret == null) return;
				var b = Bearing(from);
				if (b.y < S.HeadEl.x || b.y > S.HeadEl.y) return;
				bool precise = Mathf.Abs(b.x) <= S.PreciseHalfAz;
				LastWarning = (b.x, Time.time, precise);
				LastLaserFrom = from;
				string done;
				if (!precise)
				{
					done = "coarse head: warning only";
					if (!IsPlayer && Vehicle.InfoBroker?.AI is UnitAI ai)
					{
						try { if (LookAt == null) throw new MissingMethodException("UnitAI.FinalLookAtVector"); LookAt.Invoke(ai, new object[] { (from - _turret.position).normalized, 12f }); done += "; AI turns the turret to it"; }
						catch (Exception e) { done += "; AI turn failed: " + e.Message; }
					}
				}
				else if (!S.Automatic) done = "precise head: warning (semi-automatic)";
				else done = TryScreen(b.x, "automatic");
				if (!precise) Held = ("side laser, turn to it", Time.time);
				else if (!S.Automatic) Held = ("semi-auto mode", Time.time);
				if (IsPlayer && S.Display == Look.ScreenPage) ShtoraPage.Alarm(Head(b.x, S), this, precise ? b.x : float.NaN, done);
				if (IsPlayer && S.Display == Look.ControlPanel) Hud.Alarm(Head(b.x, S), S.AlarmTone);
			}

			internal bool IsPlayer => PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle;

			internal (string why, float at) Held = (null, -1e9f);
			internal const float HeldShow = 6f;

			static readonly string[] LiveBlocks = { "no tubes", "no grenades left", "hatch open", "own shot" };
			internal string ScreenState()
			{
				if (Smoke == null || Slots == null) return "no tubes";
				int left = 0;
				foreach (var sl in Slots) left += sl.Rounds;
				if (left <= 0) return "no grenades left";
				if (S.Automatic && HatchOpen()) return "held: hatch open";
				float shot = S.OwnShotBlock - (Time.time - LastOwnShot);
				if (S.Automatic && shot > 0f) return $"held: own shot, {Mathf.CeilToInt(shot)} s";
				if (Held.why != null && Time.time - Held.at <= HeldShow && Array.IndexOf(LiveBlocks, Held.why) < 0) return "held: " + Held.why;
				return $"ready ({left})";
			}

			internal string TryScreen(float az, string why)
			{
				string Blocked(string shortWhy, string log) { Held = (shortWhy, Time.time); return log; }
				if (Smoke == null || Slots == null) return Blocked("no tubes", "no tubes");
				if (why == "automatic" && HatchOpen()) return Blocked("hatch open", "screen blocked: hatch open");
				if (why == "automatic" && Time.time - LastOwnShot < S.OwnShotBlock) return Blocked("own shot", $"screen blocked: own shot {Time.time - LastOwnShot:0.0} s ago");
				Screens.RemoveAll(x => x.until < Time.time);
				if (Screens.Any(x => Mathf.Abs(Mathf.DeltaAngle(x.az, az)) < S.SameThreatDeg)) return Blocked("cloud already there", "screen already standing there");
				int best = -1; float bestD = float.MaxValue;
				for (int i = 0; i < Slots.Length; i++)
				{
					if (Slots[i].Rounds <= 0 || Slots[i].SpawnLocation == null) continue;
					float d = Mathf.Abs(Mathf.DeltaAngle(SlotAz[i], az));
					if (d < bestD) { bestD = d; best = i; }
				}
				if (best < 0) return Blocked("no grenades left", "no grenades left");
				if (!FireSlot(best)) return Blocked("launch failed", "screen failed (pool)");
				Held = (null, Time.time);
				Screens.Add((SlotAz[best], Time.time + S.CloudLife));
				LastScreenSlot = best; LastScreenAt = Time.time;
				return $"screen fired ({why}) from tube {best} at {SlotAz[best]:+0;-0} deg, {Slots.Sum(x => x.Rounds)} grenades left";
			}

			internal int LastScreenSlot = -1;
			internal float LastScreenAt = -1e9f;

			bool FireSlot(int i)
			{
				var slot = Slots[i];
				var prefab = PrefabField?.GetValue(Smoke) as GameObject;
				if (prefab == null || ImpulseMethod == null || ObjectPoolManager.Instance == null) return false;
				ObjectPoolManager.Instance.Register(prefab);
				var obj = ObjectPoolManager.Instance.Get(prefab);
				if (obj == null) return false;
				var g = obj.GetComponent<Grenade>();
				var ret = PoolReturnField?.GetValue(Smoke) as Action<Grenade>;
				if (g != null && ret != null) g.Exploded = (Action<Grenade>)Delegate.Combine(g.Exploded, ret);
				var pos = slot.SpawnLocation.position;
				obj.transform.position = pos;
				var rb = obj.GetComponent<Rigidbody>();
				rb.position = pos;
				var v = (Vector3)ImpulseMethod.Invoke(Smoke, new object[] { slot.Angle, S.LaunchAngle, LandingFor(UnityEngine.Random.Range(S.Distance.x, S.Distance.y), S) });
				var chassis = Vehicle.InfoBroker?.AI?.Chassis;
				if (chassis != null) v += chassis.Velocity;
				rb.AddForce(v, ForceMode.VelocityChange);
				if (v != Vector3.zero) obj.transform.rotation = Quaternion.LookRotation(v, Smoke.transform.forward);
				slot.Rounds--;
				if (slot.DisableVisualOnUse && slot.DisplayMesh != null) slot.DisplayMesh.enabled = false;
				if (slot.ScaleBoneToZero && slot.DisplayBone != null) slot.DisplayBone.localScale = Vector3.zero;
				var ev = ShootEventField?.GetValue(Smoke) as string;
				var fwd = ForwardRefField?.GetValue(Smoke) as Transform;
				if (!string.IsNullOrEmpty(ev)) GHPC.Audio.FmodGenericAudioManager.PlayOneShotWithCameraSoundDelay(ev, (fwd != null ? fwd : Smoke.transform).position);
				return true;
			}

			internal IEnumerator TurnToThreat()
			{
				var fcs = Vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.FCS;
				if (fcs == null) yield break;
				var target = LastLaserFrom;
				float t0 = Time.time;
				while (Time.time - t0 < 8f && Vehicle != null && Mathf.Abs(Bearing(target).x) > 2f)
				{
					fcs.SetAimVector((target - fcs.transform.position).normalized, true);
					yield return null;
				}
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

		static readonly FieldInfo SlotsField = Reflect.Field(typeof(VehicleSmokeManager), "_smokeSlots"),
			PrefabField = Reflect.Field(typeof(VehicleSmokeManager), "_smokePrefab"),
			LaunchAngleField = Reflect.Field(typeof(VehicleSmokeManager), "_launchAngle"),
			DistanceField = Reflect.Field(typeof(VehicleSmokeManager), "_distanceRange"),
			ForwardRefField = Reflect.Field(typeof(VehicleSmokeManager), "_forwardReference"),
			PoolReturnField = Reflect.Field(typeof(VehicleSmokeManager), "_poolReturnDelegate"),
			ShootEventField = Reflect.Field(typeof(VehicleSmokeManager), "_shootSmokeEvent"),
			FuseField = Reflect.Field(typeof(Grenade), "_fuseTimeRange"),
			EffectField = Reflect.Field(typeof(Grenade), "_effectPrefab"),
			LaserDestroyedField = Reflect.Field(typeof(FireControlSystem), "_laserDestroyed");
		static readonly MethodInfo ImpulseMethod = Reflect.Method(typeof(VehicleSmokeManager), "CalculateLaunchImpulse", new[] { typeof(float), typeof(float), typeof(float) }),
			LookAt = Reflect.Method(typeof(UnitAI), "FinalLookAtVector", new[] { typeof(Vector3), typeof(float) });

		static bool LaserDestroyed(FireControlSystem fcs) => LaserDestroyedField?.GetValue(fcs) is bool b && b;

		internal static void Apply(Vehicle v, Settings s)
		{
			var st = v.gameObject.GetComponent<Station>() ?? v.gameObject.AddComponent<Station>();
			if (s.PanelShown) Hud.Ensure();
			if (s.Display == Look.ScreenPage && s.PanelAlways) ShtoraPage.Ensure(s);
			st.Setup(v, s);
		}

		static float Speed(float r, Settings s) => r / (Mathf.Cos(s.LaunchAngle * Mathf.Deg2Rad) * s.FuseSeconds);
		internal static float LandingFor(float r, Settings s) { float v = Speed(r, s); return v * v * Mathf.Sin(2f * s.LaunchAngle * Mathf.Deg2Rad) / -Physics.gravity.y; }

		static float BurstHeight(float r, Settings s) { float t = s.FuseSeconds; return s.TubeHeight + Speed(r, s) * Mathf.Sin(s.LaunchAngle * Mathf.Deg2Rad) * t + 0.5f * Physics.gravity.y * t * t; }

		static Transform _holder;
		static readonly Dictionary<GameObject, GameObject> Made = new Dictionary<GameObject, GameObject>();
		static readonly Dictionary<GameObject, ObjectPoolManager> PooledIn = new Dictionary<GameObject, ObjectPoolManager>();

		static GameObject Grenade3D17(GameObject basePrefab, Settings s)
		{
			if (basePrefab == null) return null;
			if (Made.TryGetValue(basePrefab, out var done) && done != null) return done;
			if (Made.ContainsValue(basePrefab)) return basePrefab;
			try
			{
				if (_holder == null) { var h = new GameObject("Shtora 3D17 holder"); h.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(h); _holder = h.transform; }
				var g = UnityEngine.Object.Instantiate(basePrefab, _holder, false);
				g.name = $"Smoke - 3D17 81mm ({s.Tag})";
				var gren = g.GetComponent<Grenade>();
				FuseField.SetValue(gren, new Vector2(s.FuseSeconds, s.FuseSeconds));
				float burstH = BurstHeight((s.Distance.x + s.Distance.y) / 2f, s);
				var effect = (GameObject)EffectField.GetValue(gren);
				var made = new List<string>();
				var white = WhiteEffect(s);
				if (white != null)
				{
					var fx = UnityEngine.Object.Instantiate(white, _holder, false);
					fx.name = $"Smoke 3D17 aerosol ({s.Tag}, from {white.name})";
					TuneWhite(fx, s, burstH, made);
					EffectField.SetValue(gren, fx);
				}
				else if (effect != null)
				{
					var fx = UnityEngine.Object.Instantiate(effect, _holder, false);
					fx.name = $"Smoke 3D17 aerosol ({s.Tag})";
					foreach (Transform c in fx.transform) if (c.name.IndexOf("Fuse", StringComparison.OrdinalIgnoreCase) >= 0) c.gameObject.SetActive(false);
					foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
					{
						var main = ps.main;
						if (ps.GetComponent<ParticleSystemVisionBlocker>() != null || ps.transform == fx.transform)
						{
							main.duration = s.CloudLife + 2f;
							main.startLifetime = s.CloudLife;
							continue;
						}
						if (!ps.gameObject.activeInHierarchy && ps.transform.parent != null && !ps.transform.parent.gameObject.activeSelf) continue;
						int max = Mathf.Max(s.CloudParticles, main.maxParticles);
						main.maxParticles = max;
						main.duration = s.CloudBuild;
						main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f * s.CloudLife, 0.9f * s.CloudLife);
						main.startSizeMultiplier *= s.CloudSizeScale;
						var em = ps.emission;
						em.enabled = false;
						if (ps.GetComponent<BurstOnStart>() == null) ps.gameObject.AddComponent<BurstOnStart>().Count = max;
						var sh = ps.shape;
						sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box;
						sh.scale = new Vector3(2f * s.CloudSpread, burstH, 2f * s.CloudSpread); sh.position = new Vector3(0f, -burstH / 2f, 0f);
						var col = ps.colorOverLifetime;
						string mode = col.enabled ? col.color.mode.ToString() : "off";
						if (col.enabled)
						{
							var c = col.color.Evaluate(0.5f, 0.5f);
							var gr = new Gradient();
							gr.SetKeys(new[] { new GradientColorKey(new Color(c.r, c.g, c.b), 0f), new GradientColorKey(new Color(c.r, c.g, c.b), 1f) },
								new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.85f, 0.65f), new GradientAlphaKey(0f, 1f) });
							col.color = new ParticleSystem.MinMaxGradient(gr);
						}
						made.Add($"'{ps.name}' {max} particles, colour over life {mode}, size over life {(ps.sizeOverLifetime.enabled ? "on" : "off")}");
						var size = ps.sizeOverLifetime;
						if (size.enabled) size.size = new ParticleSystem.MinMaxCurve(1.5f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.15f, 1f), new Keyframe(1f, 1.3f)));
						var force = ps.forceOverLifetime;
						if (force.enabled) { force.x = new ParticleSystem.MinMaxCurve(0f); force.y = new ParticleSystem.MinMaxCurve(s.CloudRise); force.z = new ParticleSystem.MinMaxCurve(0f); }
						var wind = ps.externalForces;
						if (wind.enabled) wind.multiplier = 1f;
					}
					EffectField.SetValue(gren, fx);
				}
				Made[basePrefab] = g;
				return g;
			}
			catch (Exception e) { MelonLogger.Error($"{s.Tag} Shtora 3D17 grenade copy failed: {e.Message}"); return null; }
		}

		static GameObject _white;

		internal static void WarmDonors(Settings s)
		{
			if (_holder == null) { var h = new GameObject("Shtora 3D17 holder"); h.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(h); _holder = h.transform; }
			WhiteEffect(s);
		}

		static GameObject WhiteEffect(Settings s)
		{
			if (_white != null) return _white;
			foreach (var key in s.WhiteSmokeDonors ?? new string[0])
			{
				var prefab = DonorParts.Prefab(key);
				var mgr = prefab != null ? prefab.GetComponentInChildren<VehicleSmokeManager>(true) : null;
				var gp = mgr != null ? PrefabField?.GetValue(mgr) as GameObject : null;
				var g = gp != null ? gp.GetComponent<Grenade>() : null;
				var fx = g != null ? EffectField?.GetValue(g) as GameObject : null;
				if (fx == null) continue;
				_white = UnityEngine.Object.Instantiate(fx, _holder, false);
				_white.name = fx.name;
				return _white;
			}
			DonorParts.WarnOnce("shtora white smoke", $"{s.Tag}: no white smoke donor ({string.Join(", ", s.WhiteSmokeDonors ?? new string[0])}) found; the 3D17 screen uses the 3D6's dark cloud.");
			return null;
		}

		static void TuneWhite(GameObject fx, Settings s, float burstH, List<string> made)
		{
			foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
			{
				string n = ps.name;
				if (n == "Smoke Bottom") ps.transform.localPosition += Vector3.down * (burstH - s.CloudBottomHeight);
				else if (n == "Smoke Top") ps.transform.localPosition += Vector3.down * (burstH * 0.5f);
				if (n == "Smoke Bottom" || n == "Smoke Top") Spread(ps, s.WhiteBottoms, s.CloudSpread, new Vector3(1f, 0.15f, 1f));
				else if (n.StartsWith("Smoke Ground Tracer")) Spread(ps, s.WhiteTracers, 0f, Vector3.one);
				var main = ps.main;
				bool smoke = n.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Tracer", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Spawner", StringComparison.OrdinalIgnoreCase) < 0;
				if (smoke && main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
				{
					float k = s.CloudLife / Mathf.Max(1f, main.startLifetime.constantMax);
					main.startLifetime = new ParticleSystem.MinMaxCurve(main.startLifetime.constantMin * k, main.startLifetime.constantMax * k);
					main.duration = Mathf.Max(main.duration, s.CloudLife + 2f);
					made.Add($"'{n}' lifetime x{k:0.00}" + (n == "Smoke Bottom" || n == "Smoke Top" ? $", {ps.transform.localPosition.y:+0.0;-0.0} m" : ""));
				}
				else if (smoke && main.startLifetime.mode == ParticleSystemCurveMode.Constant)
				{
					float k = s.CloudLife / Mathf.Max(1f, main.startLifetime.constant);
					main.startLifetime = main.startLifetime.constant * k;
					main.duration = Mathf.Max(main.duration, s.CloudLife + 2f);
					made.Add($"'{n}' lifetime x{k:0.00}" + (n == "Smoke Bottom" || n == "Smoke Top" ? $", {ps.transform.localPosition.y:+0.0;-0.0} m" : ""));
				}
			}
		}

		static void Spread(ParticleSystem ps, int count, float radius, Vector3 scale)
		{
			var main = ps.main;
			main.maxParticles = Mathf.Max(main.maxParticles, count);
			var em = ps.emission;
			if (em.burstCount > 0) { var b = em.GetBurst(0); b.count = count; em.SetBurst(0, b); }
			else em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
			if (radius > 0f)
			{
				var sh = ps.shape;
				sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius; sh.scale = scale;
			}
		}

		internal sealed class BurstOnStart : MonoBehaviour
		{
			public int Count;
			bool _started;
			void Start() { _started = true; Burst(); }

			void OnEnable() { if (_started) Burst(); }

			void Burst()
			{
				var ps = GetComponent<ParticleSystem>();
				if (ps == null) return;
				if (!ps.isPlaying) ps.Play(false);
				ps.Emit(Count);
			}
		}

		static Unit UnitOf(Collider c)
		{
			if (c == null) return null;
			var u = c.GetComponentInParent<Unit>();
			if (u != null) return u;
			var lf = c.transform.root.GetComponent<LateFollow>();
			return lf != null && lf.OriginalParent != null ? lf.OriginalParent.GetComponent<Unit>() : null;
		}

		static bool LineClear(Vector3 a, Vector3 b, Unit shooter, Unit target)
		{
			var cm = ConstantsAndInfoManager.Instance;
			if (cm == null) return true;
			var start = a;
			for (int i = 0; i < 4; i++)
			{
				var d = b - start;
				float len = d.magnitude;
				if (len < 0.5f) return true;
				var dir = d / len;
				if (!RaycastColliderUtils.Raycast(new Ray(start, dir), out var hit, len, cm.LaserRangefinderLayerMask)) return true;
				var u = UnitOf(hit.collider);
				if (u != null && u == target) return true;
				if (u == null || u != shooter) return false;
				start = hit.point + dir * 0.05f;
			}
			return true;
		}

		static bool SmokeBetween(Vector3 a, Vector3 b)
		{
			var d = b - a;
			return Physics.Raycast(a, d, out var hit, d.magnitude, 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY) && hit.collider.CompareTag("Smoke");
		}

		[HarmonyPatch(typeof(FireControlSystem), "DoLase")]
		static class LasePatch
		{
			static void Postfix(FireControlSystem __instance)
			{
				if (Stations.Count == 0) return;
				try
				{
					if (__instance.LaserOrigin == null || __instance.MaxLaserRange <= 0f || LaserDestroyed(__instance)) return;
					var ray = new Ray(__instance.LaserOrigin.position, __instance.LaserOrigin.forward);
					float smoke = -1f;
					if (Physics.Raycast(ray, out var sh, __instance.MaxLaserRange, 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY) && sh.collider.CompareTag("Smoke")) smoke = sh.distance;
					if (!RaycastColliderUtils.Raycast(ray, out var hit, __instance.MaxLaserRange, ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask)) return;
					if (smoke >= 0f && smoke < hit.distance) return;
					var st = Of(UnitOf(hit.collider));
					var shooter = __instance.GetComponentInParent<Unit>();
					if (st == null || st.Vehicle == shooter) return;
					st.Lased(__instance.LaserOrigin.position);
				}
				catch (Exception e) { PatchGuard.Failed("Laser warning on a lase", e); }
			}
		}

		static readonly Dictionary<UnitAI, object> Lasing = Shared<Dictionary<UnitAI, object>>("GHPCMods.ShtoraScreen.Lasing.v2");

		static T Shared<T>(string key) where T : class, new()
		{
			var had = AppDomain.CurrentDomain.GetData(key);
			if (had is T t) return t;
			if (had != null) { MelonLogger.Warning($"Shtora screens: table {key} of another port has another layout; this port keeps its own."); return new T(); }
			t = new T();
			AppDomain.CurrentDomain.SetData(key, t);
			return t;
		}

		static void Prune<K, V>(Dictionary<K, V> d) where K : UnityEngine.Object
		{
			List<K> dead = null;
			foreach (var k in d.Keys) if (k == null) { if (dead == null) dead = new List<K>(); dead.Add(k); }
			if (dead != null) foreach (var k in dead) d.Remove(k);
		}

		[HarmonyPatch(typeof(UnitAI), nameof(UnitAI.SetTarget), new[] { typeof(ITarget), typeof(bool) })]
		static class AiTargetPatch
		{
			static void Postfix(UnitAI __instance, ITarget __0)
			{
				var target = __0;
				if (Stations.Count == 0 || target == null) return;
				var st = Of(target.Owner);
				if (st == null || Lasing.TryGetValue(__instance, out var cur) && ReferenceEquals(cur, st)) return;
				if (__instance.Unit == null || __instance.Unit == st.Vehicle || !Lases(__instance, st.Vehicle)) return;
				var fcs = __instance.Unit.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.FCS;
				if (fcs == null || fcs.LaserOrigin == null || fcs.MaxLaserRange <= 0f) return;
				Prune(Lasing); Prune(Blinded);
				Lasing[__instance] = st;
				MelonCoroutines.Start(AiLasing(__instance, fcs, st));
			}
		}

		static readonly FieldInfo GunnerAiField = Reflect.Field(typeof(UnitAI), "_gunnerAI");
		static bool Lases(UnitAI ai, Unit target)
		{
			var rf = Blinded.TryGetValue(ai, out var was) ? BlindRf(was) : ai.RangefinderType;
			if (rf != AimTimeCalculator.RangeFinderType.Laser) return false;
			if (GunnerAiField?.GetValue(ai) is Behaviour g && !g.enabled) return false;
			var a = ai.Unit.Allegiance; var b = target.Allegiance;
			return a != b && a != Faction.Neutral && b != Faction.Neutral;
		}

		const float BlindCheck = 0.25f;
		static readonly WaitForSeconds BlindWait = new WaitForSeconds(BlindCheck);
		static IEnumerator AiLasing(UnitAI ai, FireControlSystem fcs, Station st)
		{
			string why = "lasing stopped (error)";
			try
			{
				yield return new WaitForSeconds(UnityEngine.Random.Range(st.S.AiLaseDelay.x, st.S.AiLaseDelay.y));
				float nextLase = 0f;
				while (ai != null && st != null && st.Vehicle != null && !st.Vehicle.Neutralized && ai.Unit != null && !ai.Unit.Neutralized && ai.Target?.Owner == st.Vehicle && Lases(ai, st.Vehicle)
					&& Lasing.TryGetValue(ai, out var mine) && ReferenceEquals(mine, st))
				{
					bool smoke = false;
					if (!LaserDestroyed(fcs) && fcs.LaserOrigin != null)
					{
						var from = fcs.LaserOrigin.position;
						var to = st.Vehicle.Center != null ? st.Vehicle.Center.position : st.Vehicle.transform.position;
						bool inRange = (to - from).magnitude <= fcs.MaxLaserRange;
						smoke = inRange && SmokeBetween(from, to);
						if (Time.time >= nextLase)
						{
							nextLase = Time.time + st.S.AiRelase;
							if (inRange && !smoke && (LineClear(from, to, ai.Unit, st.Vehicle) || LineClear(from, st.TurretPoint, ai.Unit, st.Vehicle))) st.Lased(from);
						}
					}
					Blind(ai, st, smoke && st.S.BlindAi, "laser line clear");
					yield return BlindWait;
				}
				why = ai == null ? "shooter gone" : st == null || st.Vehicle == null || st.Vehicle.Neutralized ? "unit gone" : ai.Unit == null || ai.Unit.Neutralized ? "shooter knocked out"
					: ai.Target?.Owner != st.Vehicle ? "target changed" : "stopped lasing (gunner AI off)";
			}
			finally { EndLasing(ai, st, why); }
		}

		static void EndLasing(UnitAI ai, Station st, string why)
		{
			bool mine = Lasing.TryGetValue(ai, out var now) && ReferenceEquals(now, st);
			if (mine) Lasing.Remove(ai);
			if (ai == null) { Blinded.Remove(ai); return; }
			if (mine || !Lasing.ContainsKey(ai))
			{
				try { Blind(ai, st, false, why); }
				catch (Exception e) { Blinded.Remove(ai); MelonLogger.Warning("Shtora screens: an AI's own ranging couldn't be restored: " + e.Message); }
			}
		}

		static readonly Dictionary<UnitAI, object[]> Blinded = Shared<Dictionary<UnitAI, object[]>>("GHPCMods.ShtoraScreen.Blinded.v2");
		static AimTimeCalculator.RangeFinderType BlindRf(object[] was) => (AimTimeCalculator.RangeFinderType)was[0];

		static void Blind(UnitAI ai, Station st, bool on, string why)
		{
			var r = ai?.AccuracyModifiers?.Range;
			if (r == null) return;
			bool now = Blinded.TryGetValue(ai, out var was);
			if (on == now) return;
			if (on)
			{
				Blinded[ai] = new object[] { ai.RangefinderType, r.Min, r.Max, r.IncreaseAccuracyPerShot };
				ai.RangefinderType = AimTimeCalculator.RangeFinderType.Stadia;
				r.Min = st.S.BlindRange.x; r.Max = st.S.BlindRange.y; r.IncreaseAccuracyPerShot = true;
				r.Reset();
			}
			else
			{
				Blinded.Remove(ai);
				ai.RangefinderType = BlindRf(was);
				r.Min = (float)was[1]; r.Max = (float)was[2]; r.IncreaseAccuracyPerShot = (bool)was[3];
				r.Reset();
				if (st != null) st.NoteAi($"{(ai.Unit != null ? ai.Unit.name : "?")} no longer blinded ({why}): its own laser again (range x{r.Value:0.000})");
			}
		}

		[HarmonyPatch(typeof(PlayerInput), "AddSmokeButtons")]
		static class MenuPatch
		{
			static readonly MethodInfo Add = Reflect.Method(typeof(PlayerInput), "AddMenuButton", new[] { typeof(string), typeof(UnityAction) });

			static void Postfix(PlayerInput __instance)
			{
				var st = Of(__instance.CurrentPlayerUnit);
				if (st == null || Add == null || st.Slots == null) return;
				Add.Invoke(__instance, new object[] { $"{st.S.MenuName}/Aerosol Screen ({st.Slots.Sum(x => x.Rounds)})", (UnityAction)(() =>
				{
					var fcs = st.Vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon;
					float az = fcs?.MuzzleIdentity != null ? st.Bearing(fcs.MuzzleIdentity.position + fcs.MuzzleIdentity.forward * 100f).x : 0f;
					st.TryScreen(az, "button");
				}) });
				Add.Invoke(__instance, new object[] { $"{st.S.MenuName}/Screen Mode ({(st.S.Automatic ? "AUTO" : "SEMI")})", (UnityAction)(() =>
				{
					st.S.Automatic = !st.S.Automatic;
				}) });
				if (Time.time - st.LastWarning.at < 20f && !st.LastWarning.precise)
					Add.Invoke(__instance, new object[] { $"{st.S.MenuName}/Turn to Laser", (UnityAction)(() => MelonCoroutines.Start(st.TurnToThreat())) });
			}
		}

		[HarmonyPatch(typeof(WeaponHud), "Update")]
		static class WeaponHudPatch
		{
			static readonly AccessTools.FieldRef<WeaponHud, Text> HudText = Reflect.FieldRefAccess<WeaponHud, Text>("_hudText");
			static readonly Dictionary<RectTransform, object[]> Lifted = Shared<Dictionary<RectTransform, object[]>>("GHPCMods.ShtoraScreen.HudLift.v2");

			static void Postfix(WeaponHud __instance)
			{
				if (Stations.Count == 0 && Shtora.Stations.Count == 0 && Lifted.Count == 0) return;
				if (HudText == null) return;
				try { AddLines(__instance); }
				catch (Exception e) { PatchGuard.Failed("Screen lines in the weapon text", e); }
			}

			static void AddLines(WeaponHud __instance)
			{
				var t = HudText(__instance);
				if (t != null && _ours != null && ReferenceEquals(t.text, _ours)) return;
				Settle(t);
				if (Stations.Count == 0 && Shtora.Stations.Count == 0) return;
				var unit = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerUnit : null;
				var sc = Of(unit); var jam = Shtora.Of(unit);
				if (sc == null && jam == null) return;
				string name = sc?.S.MenuName ?? "Shtora-1";
				if (t == null || string.IsNullOrEmpty(t.text) || t.text.Contains(name + ":")) return;
				float before = Height(t, t.text);
				var parts = new List<string>();
				if (jam != null)
					parts.Add("IR jammer " + (!jam.On ? "off" : jam.LeftWorks && jam.RightWorks ? "on" : jam.LeftWorks ? "on, left only" : jam.RightWorks ? "on, right only" : "broken"));
				if (sc != null) parts.Add(sc.S.Automatic ? "auto" : "semi-auto");
				var lines = t.text.Split('\n').ToList();
				int at = lines.FindLastIndex(l => l.StartsWith("Stabilizers") || l.StartsWith("Delta-D"));
				if (at < 0) at = Math.Min(2, lines.Count - 1);
				lines.Insert(at + 1, $"{name}: {string.Join(", ", parts)}");
				if (sc != null) lines.Insert(at + 2, $"{name} screen: {sc.ScreenState()}");
				t.text = string.Join("\n", lines);
				_ours = t.text;
				Lift(t, Height(t, t.text) - before);
			}

			static string _ours;

			static float Height(Text t, string s)
			{
				var g = t.GetGenerationSettings(Vector2.zero);
				g.horizontalOverflow = HorizontalWrapMode.Overflow;
				return t.cachedTextGeneratorForLayout.GetPreferredHeight(s, g) / t.pixelsPerUnit;
			}

			static void Lift(Text t, float by)
			{
				var rt = t.rectTransform;
				bool known = Lifted.TryGetValue(rt, out var l);
				if (!known) Prune(Lifted);
				float top = known ? (float)l[0] : rt.anchoredPosition.y;
				rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, top + by);
				Lifted[rt] = new object[] { top, t.text };
			}

			static void Settle(Text t)
			{
				if (t == null || Lifted.Count == 0) return;
				var rt = t.rectTransform;
				if (!Lifted.TryGetValue(rt, out var l) || !(l[1] is string text) || text == t.text) return;
				rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, (float)l[0]);
				Lifted.Remove(rt);
			}
		}

		internal static int Head(float az, Settings s) => Mathf.Abs(az) <= s.PreciseHalfAz ? (az < 0f ? 0 : 1) : (az < 0f ? 2 : 3);
		internal static readonly string[] HeadNames = { "front left", "front right", "rear left", "rear right" };

		internal static class Hud
		{
			static GameObject _root, _panel;
			static Canvas _canvas;
			static WeaponHud _weaponHud;
			static Text _weaponHudText;
			static float _nextHudLook;
			static Image[] _lamps;
			static Image _spz;
			static AudioSource _audio;
			static float _until;
			static int _lit = -1;
			static Sprite _lampOn, _lampOff;
			static readonly Color LampOff = new Color(0.30f, 0.05f, 0.05f, 1f), LampOn = new Color(1f, 0.16f, 0.10f, 1f);

			internal static void Alarm(int h, bool tone = true)
			{
				try
				{
					Build();
					_lit = h;
					_until = Time.unscaledTime + 5f;
					if (_audio != null && tone && !_audio.isPlaying) _audio.Play();
				}
				catch (Exception e) { MelonLogger.Error("Shtora panel failed: " + e.Message); }
			}

			internal static void Ensure()
			{
				try { Build(); }
				catch (Exception e) { MelonLogger.Error("Shtora panel failed: " + e.Message); }
			}

			static bool Wanted()
			{
				var st = PlayerInput.Instance != null ? Of(PlayerInput.Instance.CurrentPlayerUnit) : null;
				if (st == null || !st.S.PanelShown || st.Vehicle == null || st.Vehicle.Neutralized || WorldScript.HideHud) return false;
				if (!st.S.PanelAlways && Time.unscaledTime > _until) return false;
				return GameHud(_canvas);
			}

			internal static bool GameHud(Canvas own)
			{
				if (_weaponHud == null && Time.unscaledTime >= _nextHudLook)
				{
					_nextHudLook = Time.unscaledTime + 1f;
					_weaponHud = UnityEngine.Object.FindObjectOfType<WeaponHud>();
					_weaponHudText = _weaponHud != null ? _weaponHud.GetComponent<Text>() : null;
				}
				var hud = _weaponHud != null ? _weaponHud.GetComponentInParent<Canvas>()?.rootCanvas : null;
				if (own != null && hud != null && (own.sortingLayerID != hud.sortingLayerID || own.sortingOrder != hud.sortingOrder)) { own.sortingLayerID = hud.sortingLayerID; own.sortingOrder = hud.sortingOrder; }
				return _weaponHud != null && _weaponHud.isActiveAndEnabled && _weaponHudText != null && _weaponHudText.enabled;
			}

			internal static bool InSight() { var slot = GHPC.Camera.CameraSlot.ActiveInstance; return slot != null && !slot.IsExterior; }

			internal const float SightMaxHeight = 100f;
			internal static readonly Vector2 OutsideAt = new Vector2(-40f, 110f), SightAt = new Vector2(-280f, 5f);
			static int _layout = -1;
			static void Fit(RectTransform panel) { _layout = -1; Place(panel, false); }

			static void Place(RectTransform panel, bool inSight)
			{
				if (_layout == (inSight ? 1 : 0)) return;
				_layout = inSight ? 1 : 0;
				panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
				panel.anchoredPosition = inSight ? SightAt : OutsideAt;
				float s = inSight ? Mathf.Min(1f, SightMaxHeight / panel.sizeDelta.y) : 1f;
				panel.localScale = new Vector3(s, s, 1f);
			}

			static Sprite _dot;
			internal static Sprite Dot()
			{
				if (_dot != null) return _dot;
				const int n = 32;
				var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
				for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
				{
					float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
					tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(n / 2f - d)));
				}
				tex.Apply();
				_dot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
				return _dot;
			}

			internal static RectTransform Box(string name, Transform parent, Vector2 pos, Vector2 size)
			{
				var go = new GameObject(name, typeof(RectTransform));
				go.transform.SetParent(parent, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
				rt.anchoredPosition = pos; rt.sizeDelta = size;
				return rt;
			}

			static void Label(string s, Transform parent, Vector2 pos)
			{
				var t = Box("label " + s, parent, pos, new Vector2(60f, 18f)).gameObject.AddComponent<Text>();
				t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
				t.text = s; t.fontSize = 13; t.color = new Color(0.1f, 0.1f, 0.1f, 1f); t.alignment = TextAnchor.MiddleCenter;
			}

			internal static byte[] Resource(string name)
			{
				using (var st = typeof(ShtoraScreen).Assembly.GetManifestResourceStream("shtora_" + name))
				{
					if (st == null) return null;
					var buf = new byte[st.Length];
					int got = 0;
					while (got < buf.Length) { int n = st.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
					return buf;
				}
			}

			internal static Sprite Picture(string name)
			{
				var bytes = Resource(name);
				if (bytes == null) return null;
				var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "shtora_" + name };
				if (!ImageConversion.LoadImage(tex, bytes)) { UnityEngine.Object.Destroy(tex); return null; }
				tex.filterMode = FilterMode.Trilinear; tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 1;
				return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
			}

			internal static void Free(Sprite s)
			{
				if (s == null) return;
				if (s.texture != null) UnityEngine.Object.Destroy(s.texture);
				UnityEngine.Object.Destroy(s);
			}

			static Dictionary<string, List<float[]>> Layout()
			{
				var bytes = Resource("layout.txt");
				if (bytes == null) return null;
				var d = new Dictionary<string, List<float[]>>();
				try
				{
					foreach (var raw in Encoding.UTF8.GetString(bytes).Split('\n'))
					{
						var line = raw.Trim();
						if (line.Length == 0 || line[0] == '#') continue;
						var f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
						if (!d.TryGetValue(f[0], out var list)) d[f[0]] = list = new List<float[]>();
						list.Add(f.Skip(1).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
					}
				}
				catch (FormatException) { return null; }
				return d;
			}

			internal static AudioClip Wav(string name)
			{
				var b = Resource(name);
				if (b == null || b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF") return null;
				int channels = 1, rate = 44100, bits = 16, pos = 12;
				while (pos + 8 <= b.Length)
				{
					string id = Encoding.ASCII.GetString(b, pos, 4);
					int size = BitConverter.ToInt32(b, pos + 4);
					if (id == "fmt ") { channels = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
					else if (id == "data" && bits == 16)
					{
						int n = Math.Min(size, b.Length - pos - 8) / 2;
						var data = new float[n];
						for (int i = 0; i < n; i++) data[i] = BitConverter.ToInt16(b, pos + 8 + 2 * i) / 32768f;
						var clip = AudioClip.Create("shtora_alarm.wav", n / channels, channels, rate, false);
						clip.SetData(data, 0);
						return clip;
					}
					pos += 8 + size + (size & 1);
				}
				return null;
			}

			static void Build()
			{
				if (_root != null) return;
				_root = new GameObject("Shtora panel");
				UnityEngine.Object.DontDestroyOnLoad(_root);
				_canvas = _root.AddComponent<Canvas>();
				_canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 0;
				var scaler = _root.AddComponent<CanvasScaler>();
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 1f;
				var panelGo = new GameObject("ПУ ТШУ-1М", typeof(RectTransform));
				_panel = panelGo;
				panelGo.transform.SetParent(_root.transform, false);
				var panel = (RectTransform)panelGo.transform;
				_audio = _root.AddComponent<AudioSource>();
				_audio.spatialBlend = 0f; _audio.playOnAwake = false;
				_root.AddComponent<Ticker>();
				panelGo.SetActive(false);
				var art = Picture("panel.png");
				var layout = Layout();
				_lampOn = Picture("lamp_on.png"); _lampOff = Picture("lamp_off.png");
				if (art != null && layout != null && _lampOn != null && _lampOff != null && layout.TryGetValue("head", out var heads) && heads.Count == 4 && heads.All(p => p.Length >= 2)
					&& layout.TryGetValue("scale", out var sc) && sc.Count > 0 && sc[0].Length > 0 && sc[0][0] > 0f
					&& layout.TryGetValue("lamp", out var lp) && lp.Count > 0 && lp[0].Length > 0
					&& layout.TryGetValue("spz", out var sp) && sp.Count > 0 && sp[0].Length >= 2)
				{
					float k = 1f / sc[0][0], lamp = lp[0][0] * k;
					panel.sizeDelta = new Vector2(art.rect.width * k, art.rect.height * k);
					panelGo.AddComponent<Image>().sprite = art;
					Vector2 At(float[] p) => new Vector2(p[0] * k, -p[1] * k);
					_spz = Box("lamp СПЗ", panel, At(sp[0]), new Vector2(lamp, lamp)).gameObject.AddComponent<Image>();
					_spz.sprite = _lampOn;
					_lamps = new Image[4];
					for (int i = 0; i < 4; i++)
					{
						_lamps[i] = Box("lamp " + HeadNames[i], panel, At(heads[i]), new Vector2(lamp, lamp)).gameObject.AddComponent<Image>();
						_lamps[i].sprite = _lampOff;
					}
					_audio.clip = Wav("alarm.wav") ?? Tone();
					_audio.volume = 0.6f;
					Fit(panel);
					return;
				}
				Free(art); Free(_lampOn); Free(_lampOff);
				_lampOn = _lampOff = null;
				panel.sizeDelta = new Vector2(170f, 96f);
				var bg = panelGo.AddComponent<Image>();
				bg.color = new Color(0.86f, 0.86f, 0.83f, 0.95f);
				var edge = panelGo.AddComponent<Outline>(); edge.effectColor = new Color(0.25f, 0.25f, 0.25f, 1f); edge.effectDistance = new Vector2(2f, -2f);
				Label("СПЗ", panel, new Vector2(40f, -62f));
				_spz = Box("lamp СПЗ", panel, new Vector2(40f, -44f), new Vector2(15f, 15f)).gameObject.AddComponent<Image>();
				_spz.sprite = Dot(); _spz.color = LampOn;
				Label("ПУСК", panel, new Vector2(118f, -14f));
				var cx = 118f; var cy = -52f;
				var h = Box("cross h", panel, new Vector2(cx, cy), new Vector2(54f, 2f)).gameObject.AddComponent<Image>(); h.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				var v = Box("cross v", panel, new Vector2(cx, cy), new Vector2(2f, 46f)).gameObject.AddComponent<Image>(); v.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				_lamps = new Image[4];
				var at = new[] { new Vector2(cx - 14f, cy + 12f), new Vector2(cx + 14f, cy + 12f), new Vector2(cx - 14f, cy - 12f), new Vector2(cx + 14f, cy - 12f) };
				for (int i = 0; i < 4; i++)
				{
					_lamps[i] = Box("lamp " + HeadNames[i], panel, at[i], new Vector2(15f, 15f)).gameObject.AddComponent<Image>();
					_lamps[i].sprite = Dot(); _lamps[i].color = LampOff;
				}
				_audio.volume = 0.35f;
				_audio.clip = Tone();
				Fit(panel);
			}

			static AudioClip Tone()
			{
				int rate = 44100, n = (int)(rate * 0.9f);
				var data = new float[n];
				for (int i = 0; i < n; i++) { float t = i / (float)rate; bool on = (t % 0.3f) < 0.18f; data[i] = on ? 0.5f * Mathf.Sin(2f * Mathf.PI * 1500f * t) : 0f; }
				var clip = AudioClip.Create("shtora_alarm", n, 1, rate, false);
				clip.SetData(data, 0);
				return clip;
			}

			sealed class Ticker : MonoBehaviour
			{
				void Update()
				{
					bool show = Wanted();
					if (_panel.activeSelf != show) _panel.SetActive(show);
					if (show) Place((RectTransform)_panel.transform, InSight());
					if (_lamps == null) return;
					float left = _until - Time.unscaledTime;
					bool blinkOn = left > 1.5f && (Time.unscaledTime * 2.5f) % 1f < 0.6f;
					for (int i = 0; i < 4; i++)
					{
						bool on = i == _lit && blinkOn;
						if (_lampOn != null) _lamps[i].sprite = on ? _lampOn : _lampOff;
						else _lamps[i].color = on ? LampOn : LampOff;
					}
					if (left <= 0f) _lit = -1;
				}
			}
		}
	}
}
