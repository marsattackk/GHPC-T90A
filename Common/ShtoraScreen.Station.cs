using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC.AI;
using GHPC.Equipment;
using GHPC.Player;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using UnityEngine;

namespace T90A.Common
{
	// One per tank with the screen: its tubes, the last warning, and when a grenade goes.
	internal static partial class ShtoraScreen
	{
		internal sealed class Station : MonoBehaviour
		{
			internal Settings S;
			internal Vehicle Vehicle;
			internal VehicleSmokeManager Smoke;
			internal float LastOwnShot = -1e9f;
			internal readonly List<(float az, float until)> Screens = new List<(float, float)>();
			internal (float az, float at, bool precise) LastWarning = (0f, -1e9f, false);
			internal Vector3 LastLaserFrom;
			internal readonly List<string> Events = new List<string>();
			internal VehicleSmokeManager.SmokeSlot[] Slots;
			internal float[] SlotAz;
			Transform _turret;

			bool _setUp;

			internal string Setup(Vehicle v, Settings s)
			{
				if (_setUp) return "already set up";
				_setUp = true;
				Vehicle = v;
				S = s;
				_turret = Followers.TurretBone(v);
				Smoke = v.GetComponentInChildren<VehicleSmokeManager>(true);
				Stations.Add(this);
				var main = ModuleBuilder.MainWeapon(v);
				if (main != null)
					main.Fired += (ammo, round) =>
					{
						if (ammo != null && ammo.Caliber >= 20f) LastOwnShot = Time.time;
					};
				if (Smoke == null || _turret == null) return "no smoke manager / turret: no screens";
				if (SlotsField == null || PrefabField == null) return "the game's smoke launcher fields are missing: no screens";
				Slots = SlotsField.GetValue(Smoke) as VehicleSmokeManager.SmokeSlot[];
				if (Slots == null) return "no smoke slots: no screens";
				var fwdRef = ForwardRefField?.GetValue(Smoke) as Transform;
				if (fwdRef == null) fwdRef = _turret;
				SlotAz = Slots.Select(slot =>
				{
					var axis = slot.DisplayMesh != null ? slot.DisplayMesh.transform.forward : fwdRef.forward;
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
				return $"{Slots.Length} tubes at {s.LaunchAngle:0} deg to {s.Distance.x:0}-{s.Distance.y:0} m, azimuths [{string.Join(", ", SlotAz.Select(a => a.ToString("+0;-0")))}] deg; grenade {(grenade != null ? grenade.name : "the base vehicle's (copy failed)")}";
			}

			internal Vector3 TurretPoint => _turret != null ? _turret.position + _turret.up * 0.5f
				: Vehicle.Center != null ? Vehicle.Center.position : Vehicle.transform.position;

			internal Vector2 Bearing(Vector3 world)
			{
				var offset = world - _turret.position;
				var flat = Vector3.ProjectOnPlane(offset, _turret.up);
				float azimuth = Vector3.SignedAngle(Vector3.ProjectOnPlane(_turret.forward, _turret.up), flat, _turret.up);
				float elevation = Mathf.Atan2(Vector3.Dot(offset, _turret.up), flat.magnitude) * Mathf.Rad2Deg;
				return new Vector2(azimuth, elevation);
			}

			internal bool HatchOpen()
			{
				var crew = Vehicle.CrewManager?.CrewMembers;
				if (crew == null) return false;
				foreach (var member in crew)
				{
					if (member == null) continue;
					if (member.ExitHatch != null && member.ExitHatch.Openness > 0.1f) return true;
					if (member.Postures != null
						&& member.CurrentPostureIndex >= 0
						&& member.CurrentPostureIndex < member.Postures.Length
						&& member.Postures[member.CurrentPostureIndex].Posture != GHPC.Crew.CrewRidePosture.Buttoned)
						return true;
				}
				return false;
			}

			internal void NoteAi(string s) => Note(s);

			void Note(string s)
			{
				Events.Add($"{Time.time:0.0} {s}");
			}

			internal void Lased(Vector3 from, string by)
			{
				if (Vehicle == null || Vehicle.Neutralized || _turret == null || !S.Heads) return;
				var bearing = Bearing(from);
				if (bearing.y < S.HeadEl.x || bearing.y > S.HeadEl.y)
				{
					Note($"laser from {by} at az {bearing.x:+0;-0} / el {bearing.y:+0;-0} deg: outside the heads' {S.HeadEl.x:0}..{S.HeadEl.y:+0} deg, not seen");
					return;
				}
				bool precise = Mathf.Abs(bearing.x) <= S.PreciseHalfAz;
				LastWarning = (bearing.x, Time.time, precise);
				LastLaserFrom = from;
				string clock = Clock(bearing.x);
				string done;
				if (!precise)
				{
					done = "coarse head: warning only";
					if (!IsPlayer && Vehicle.InfoBroker?.AI is UnitAI unitAi)
					{
						try
						{
							if (LookAt == null) throw new MissingMethodException("UnitAI.FinalLookAtVector");
							LookAt.Invoke(unitAi, new object[] { (from - _turret.position).normalized, 12f });
							done += "; AI turns the turret to it";
						}
						catch (Exception exception) { done += "; AI turn failed: " + exception.Message; }
					}
				}
				else if (!S.Automatic) done = "precise head: warning (semi-automatic)";
				else done = TryScreen(bearing.x, "automatic");
				if (!precise) Held = ("side laser, turn to it", Time.time);
				else if (!S.Automatic) Held = ("semi-auto mode", Time.time);
				if (IsPlayer && S.Display == Look.ScreenPage)
					ShtoraPage.Alarm(Head(bearing.x, S), this, precise ? bearing.x : float.NaN, done);
				Note($"LASER from {by} at {clock} (az {bearing.x:+0;-0}, el {bearing.y:+0;-0} deg), {(precise ? "precise" : "coarse")} head; {done}");
				if (IsPlayer && S.Display == Look.ControlPanel) Hud.Alarm(Head(bearing.x, S), S.AlarmTone);
			}

			internal bool IsPlayer => PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle;

			internal (string why, float at) Held = (null, -1e9f);
			internal const float HeldShow = 6f;

			static readonly string[] LiveBlocks = { "no tubes", "no grenades left", "hatch open", "own shot" };
			internal string ScreenState()
			{
				if (Smoke == null || Slots == null) return "no tubes";
				int left = 0;
				foreach (var slot in Slots) left += slot.Rounds;
				if (left <= 0) return "no grenades left";
				if (S.Automatic && HatchOpen()) return "held: hatch open";
				float shot = S.OwnShotBlock - (Time.time - LastOwnShot);
				if (S.Automatic && shot > 0f) return $"held: own shot, {Mathf.CeilToInt(shot)} s";
				if (Held.why != null && Time.time - Held.at <= HeldShow && Array.IndexOf(LiveBlocks, Held.why) < 0)
					return "held: " + Held.why;
				return $"ready ({left})";
			}

			internal string TryScreen(float az, string why)
			{
				string Blocked(string shortWhy, string log)
				{
					Held = (shortWhy, Time.time);
					return log;
				}
				if (Smoke == null || Slots == null) return Blocked("no tubes", "no tubes");
				if (why == "automatic" && HatchOpen()) return Blocked("hatch open", "screen blocked: hatch open");
				if (why == "automatic" && Time.time - LastOwnShot < S.OwnShotBlock)
					return Blocked("own shot", $"screen blocked: own shot {Time.time - LastOwnShot:0.0} s ago");
				Screens.RemoveAll(s => s.until < Time.time);
				if (Screens.Any(s => Mathf.Abs(Mathf.DeltaAngle(s.az, az)) < S.SameThreatDeg))
					return Blocked("cloud already there", "screen already standing there");
				int best = -1;
				float bestD = float.MaxValue;
				for (int i = 0; i < Slots.Length; i++)
				{
					if (Slots[i].Rounds <= 0 || Slots[i].SpawnLocation == null) continue;
					float difference = Mathf.Abs(Mathf.DeltaAngle(SlotAz[i], az));
					if (difference < bestD)
					{
						bestD = difference;
						best = i;
					}
				}
				if (best < 0) return Blocked("no grenades left", "no grenades left");
				if (!FireSlot(best)) return Blocked("launch failed", "screen failed (pool)");
				Held = (null, Time.time);
				Screens.Add((SlotAz[best], Time.time + S.CloudLife));
				LastScreenSlot = best;
				LastScreenAt = Time.time;
				return $"screen fired ({why}) from tube {best} at {SlotAz[best]:+0;-0} deg, {Slots.Sum(s => s.Rounds)} grenades left";
			}

			internal int LastScreenSlot = -1;
			internal float LastScreenAt = -1e9f;

			bool FireSlot(int i)
			{
				var slot = Slots[i];
				var prefab = PrefabField?.GetValue(Smoke) as GameObject;
				if (prefab == null || ImpulseMethod == null || ObjectPoolManager.Instance == null) return false;
				// The pool wipes its table during the after-action review but keeps the instance. Re-register every launch.
				ObjectPoolManager.Instance.Register(prefab);
				var grenadeObject = ObjectPoolManager.Instance.Get(prefab);
				if (grenadeObject == null) return false;
				var grenade = grenadeObject.GetComponent<Grenade>();
				var returnToPool = PoolReturnField?.GetValue(Smoke) as Action<Grenade>;
				if (grenade != null && returnToPool != null)
					grenade.Exploded = (Action<Grenade>)Delegate.Combine(grenade.Exploded, returnToPool);
				var pos = slot.SpawnLocation.position;
				grenadeObject.transform.position = pos;
				var body = grenadeObject.GetComponent<Rigidbody>();
				body.position = pos;
				var velocity = (Vector3)ImpulseMethod.Invoke(
					Smoke,
					new object[] { slot.Angle, S.LaunchAngle, LandingFor(UnityEngine.Random.Range(S.Distance.x, S.Distance.y), S) });
				var chassis = Vehicle.InfoBroker?.AI?.Chassis;
				if (chassis != null) velocity += chassis.Velocity;
				body.AddForce(velocity, ForceMode.VelocityChange);
				if (velocity != Vector3.zero) grenadeObject.transform.rotation = Quaternion.LookRotation(velocity, Smoke.transform.forward);
				slot.Rounds--;
				if (slot.DisableVisualOnUse && slot.DisplayMesh != null) slot.DisplayMesh.enabled = false;
				if (slot.ScaleBoneToZero && slot.DisplayBone != null) slot.DisplayBone.localScale = Vector3.zero;
				var shootEvent = ShootEventField?.GetValue(Smoke) as string;
				var fwd = ForwardRefField?.GetValue(Smoke) as Transform;
				if (!string.IsNullOrEmpty(shootEvent))
					GHPC.Audio.FmodGenericAudioManager.PlayOneShotWithCameraSoundDelay(
						shootEvent,
						(fwd != null ? fwd : Smoke.transform).position);
				return true;
			}

			internal IEnumerator TurnToThreat()
			{
				var fcs = Vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.FCS;
				if (fcs == null) yield break;
				var target = LastLaserFrom;
				Note($"turret turned to the laser at {Clock(LastWarning.az)} (button)");
				float startTime = Time.time;
				while (Time.time - startTime < 8f && Vehicle != null && Mathf.Abs(Bearing(target).x) > 2f)
				{
					fcs.SetAimVector((target - fcs.transform.position).normalized, true);
					yield return null;
				}
			}

			void OnDestroy() => Stations.Remove(this);
		}
	}
}
