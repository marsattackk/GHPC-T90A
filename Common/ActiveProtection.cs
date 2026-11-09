using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Crew;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using DC = GHPC.Equipment.DestructibleComponent;
using System.Reflection;

namespace T90A.Common
{
	internal static class Aps
	{
		internal sealed class BlockSpec
		{
			public string Name;
			public Vector3[] Mouths;
			public float AxisAz, AxisEl;
			public float AzMin, AzMax;
			public int Side;
			public string Module;
		}

		internal sealed class Settings
		{
			public string Tag = "Port APS";
			public string Name = "APS";
			public bool OnAtSpawn = true;
			public float WarmUp = 300f;
			public bool HatchInterlock = true;
			public BlockSpec[] Blocks = new BlockSpec[0];
			public int RoundsPerBlock = 2;
			public string[] SensorModules = new string[0];
			public string ComputerModule;
			public float ElMin = -6f, ElMax = 20f, SearchRange = 330f, TrackRange = 130f, MinRange = 11f;
			public float SpeedMin = 70f, SpeedMax = 700f, SizeMin = 0.036f, ReactionTime = 0.02f;
			public float RoundSpeed = 190f, InterceptRange = 6.7f, BurstMin = 6f, BurstMax = 8f, FragReach = 1.5f, ConeReach = 3f, Cooldown = 0.35f, OtherSide = 0.035f;
			public string VisualPrefix;
			public float ProtectMargin = 0f;
			public float HeadingSmoothing = 0.15f;
			public string[] BoxLeaveOut = { };
			public float PAtgm = 1f, PGrenade = 0.7f, PHeat = 0.78f, PHe = 0.78f;
			public float PBrokenUp = 0f;
			public float WarheadFragShare = 1f;
			public string[] GrenadePrefixes = { "PG-", "RPG", "M72", "S-5", "S-8", "Hydra" };
			public int FragCount = 60;
			public float FragConeHalf = 15f, FragSpeed = 1600f;
			public float FragMass = 0.003f, FragArea = 4.86e-5f, FragCoeff = 0.6f, FragPen = 6f, FragPenSpeed = 1200f;
			public float ReloadPerRound = 60f, ReloadMaxSpeed = 0.5f;
			public string LaunchEvent = "event:/Weapons/launcher_9M14";
			public string CapPrefix = "aps_cap_";
		}

		internal sealed class Watched
		{
public int Id;
			public ApsMath.Track T = new ApsMath.Track();
			public Vector3 Prev; public int PrevFrame = -10;
			public V3 Vel; public bool HasVel;
			public Vector3 Mean; public bool HasMean;
			public bool WindowLogged;
			public Vector3 Mouth; public bool Launched; public int Tube;
			public int LastFrame;
		}

		internal sealed class Station : MonoBehaviour
		{
			internal Settings S;
			internal Vehicle Vehicle;
			internal ApsMath.Geometry G;
			internal ApsMath.State St;
			internal bool On { get; private set; }

			internal float ReadyAt;
			internal bool Reloading;
			internal float ReloadLeft;
			internal Transform Turret;
			internal DC[] BlockDc, SensorDc;
			internal DC ComputerDc;
			internal Renderer[][] Caps;
			internal Bounds Box;
			readonly Dictionary<LiveRound, Watched> _tracks = new Dictionary<LiveRound, Watched>();
			readonly List<LiveRound> _gone = new List<LiveRound>();
			bool _setUp, _modulesBound;
			float _nextModuleLook;
			int Key => GetInstanceID();

			internal string Setup(Vehicle v, Settings s)
			{
				if (_setUp) return "already set up";
				_setUp = true;
				Vehicle = v; S = s;
				Turret = Followers.TurretBone(v);
				if (Turret == null) return "no turret bone found: the APS stays off";
				var root = v.transform;
				Vector3 Local(Vector3 prefab) => Turret.InverseTransformPoint(root.TransformPoint(prefab));
				G = new ApsMath.Geometry
				{
					ElMin = s.ElMin, ElMax = s.ElMax, TrackRange = s.TrackRange, MinRange = s.MinRange,
					SpeedMin = s.SpeedMin, SpeedMax = s.SpeedMax, SizeMin = s.SizeMin, ReactionTime = s.ReactionTime,
					RoundSpeed = s.RoundSpeed, InterceptRange = s.InterceptRange, BurstMin = s.BurstMin, BurstMax = s.BurstMax, FragReach = s.FragReach,
					ConeHalf = s.FragConeHalf, ConeReach = s.ConeReach, Cooldown = s.Cooldown, OtherSide = s.OtherSide,
					Blocks = s.Blocks.Select(b =>
					{
						var m = Vector3.zero; foreach (var p in b.Mouths) m += Local(p); m /= Mathf.Max(1, b.Mouths.Length);
						return new ApsMath.Block { Name = b.Name, Mouth = new V3(m.x, m.y, m.z), AxisAz = b.AxisAz, AxisEl = b.AxisEl, AzMin = b.AzMin, AzMax = b.AzMax, Side = b.Side, Rounds = Mathf.Min(s.RoundsPerBlock, b.Mouths.Length) };
					}).ToArray(),
				};
				var o = Vector3.zero; foreach (var b in G.Blocks) o += new Vector3(b.Mouth.x, b.Mouth.y, b.Mouth.z); o /= Mathf.Max(1, G.Blocks.Length);
				G.Origin = new V3(0f, o.y, o.z);
				St = new ApsMath.State(G);
				BlockDc = new DC[G.Blocks.Length]; SensorDc = new DC[2];
				Box = OwnBox(v, s);
				Caps = BindCaps(s, out var capNote);
				if (s.OnAtSpawn) { On = true; ReadyAt = 0f; }
				Stations.Add(this);
				EnsurePatched();
				return $"{G.Blocks.Length} blocks ({string.Join("; ", G.Blocks.Select((b, i) => $"{b.Name} {b.AzMin:+0;-0}..{b.AzMax:+0;-0} deg, axis {b.AxisAz:+0;-0}, mouth {b.Mouth} turret frame, burst {b.Burst(G.InterceptRange)}, {St.Rounds[i]} rounds"))}), "
					+ $"radar origin {G.Origin}, protected box {Box.center:F2} size {Box.size:F2} (vehicle frame; {_boxParts}), {(On ? "on at spawn" : "off at spawn")}, {capNote}";
			}

			static Bounds OwnBox(Vehicle v, Settings s)
			{
				var root = v.transform;
				var pts = new List<(Vector3 p, string n)>();
				void Corners(Bounds lb, Transform t, string n)
				{
					for (int i = 0; i < 8; i++)
						pts.Add((root.InverseTransformPoint(t.TransformPoint(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))), n));
				}
				string source = "armour colliders";
				foreach (var c in v.GetComponentsInChildren<Collider>(true))
				{
					if (c == null || c.isTrigger || !c.TryGetComponent<IArmor>(out _) || LeftOut(c.transform, s.BoxLeaveOut)) continue;
					if (c is BoxCollider bc) Corners(new Bounds(bc.center, bc.size), c.transform, c.name);
					else if (c is MeshCollider mc && mc.sharedMesh != null) Corners(mc.sharedMesh.bounds, c.transform, c.name);
					else if (c is SphereCollider sc) Corners(new Bounds(sc.center, Vector3.one * 2f * sc.radius), c.transform, c.name);
					else if (c is CapsuleCollider cc) Corners(new Bounds(cc.center, Vector3.one * Mathf.Max(cc.height, 2f * cc.radius)), c.transform, c.name);
				}
				if (pts.Count == 0)
				{
					source = "visual parts";
					foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
					{
						if (s.VisualPrefix != null && !Under(r.transform, s.VisualPrefix)) continue;
						if (LeftOut(r.transform, s.BoxLeaveOut)) continue;
						var mf = r.GetComponent<MeshFilter>();
						if (mf == null || mf.sharedMesh == null) continue;
						var lb = mf.sharedMesh.bounds;
						var sz = Vector3.Scale(lb.size, r.transform.lossyScale); var abs = new[] { Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z) }; Array.Sort(abs);
						if (abs[1] < 0.05f) continue;
						Corners(lb, r.transform, r.name);
					}
				}
				if (pts.Count == 0) { _boxParts = "nothing found: a default box"; return new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(3.6f, 2.4f, 7.2f)); }
				var b = new Bounds(pts[0].p, Vector3.zero);
				foreach (var (p, _) in pts) b.Encapsulate(p);
				string At(Func<Vector3, float> f) { float best = float.MinValue; string n = null; foreach (var (p, nm) in pts) if (f(p) > best) { best = f(p); n = nm; } return n; }
				_boxParts = $"from {source}: lowest '{At(p => -p.y)}', highest '{At(p => p.y)}', rearmost '{At(p => -p.z)}', foremost '{At(p => p.z)}', widest '{At(p => Mathf.Abs(p.x))}'";
				b.Expand(2f * s.ProtectMargin);
				return b;
			}

			static string _boxParts;

			static bool Under(Transform t, string prefix) { for (; t != null; t = t.parent) if (t.name.StartsWith(prefix)) return true; return false; }

			static bool LeftOut(Transform t, string[] names)
			{
				if (names == null || names.Length == 0) return false;
				for (; t != null; t = t.parent) foreach (var n in names) if (t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
				return false;
			}

			Renderer[][] BindCaps(Settings s, out string note)
			{
				var root = Vehicle.transform;
				var free = Vehicle.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith(s.CapPrefix)).ToList();
				int found = free.Count, bound = 0; float worst = 0f;
				var all = new Renderer[s.Blocks.Length][];
				for (int i = 0; i < s.Blocks.Length; i++)
				{
					var b = s.Blocks[i];
					all[i] = new Renderer[b.Mouths.Length];
					for (int k = 0; k < b.Mouths.Length; k++)
					{
						var at = root.TransformPoint(b.Mouths[k]);
						var best = free.OrderBy(r => (r.transform.position - at).sqrMagnitude).FirstOrDefault();
						if (best == null || (best.transform.position - at).magnitude > 0.15f) continue;
						worst = Mathf.Max(worst, (best.transform.position - at).magnitude);
						free.Remove(best); all[i][k] = best; bound++;
					}
				}
				note = found == 0 ? $"no {s.CapPrefix}* covers in the bundle (tubes shown open)" : $"{bound} of {found} covers bound to their tubes (at most {worst * 100f:0.0} cm off a mouth)";
				if (found > 0 && bound < s.Blocks.Sum(x => x.Mouths.Length)) DonorParts.WarnOnce("aps covers", $"{s.Tag}: only {bound} tube covers found for {s.Blocks.Sum(x => x.Mouths.Length)} tubes.");
				return all;
			}

			internal void ShowCaps()
			{
				if (Caps == null) return;
				for (int i = 0; i < Caps.Length; i++)
					for (int k = 0; k < Caps[i].Length; k++)
						if (Caps[i][k] != null) Caps[i][k].enabled = !St.Lost[i] && k < St.Rounds[i];
			}

			void BindModules()
			{
				if (_modulesBound || Time.time < _nextModuleLook) return;
				_nextModuleLook = Time.time + 1f;
				var set = Vehicle.GetComponent<ModuleBuilder.ModuleSet>();
				if (set == null || set.Built.Count == 0) return;
				_modulesBound = true;
				foreach (var (m, dc) in set.Built)
				{
					for (int i = 0; i < S.Blocks.Length; i++) if (S.Blocks[i].Module != null && m.Id == S.Blocks[i].Module) BlockDc[i] = dc;
					for (int k = 0; k < S.SensorModules.Length && k < 2; k++) if (S.SensorModules[k] != null && m.Id == S.SensorModules[k]) SensorDc[k] = dc;
					if (S.ComputerModule != null && m.Id == S.ComputerModule) ComputerDc = dc;
				}
			}

			internal string NotReady()
			{
				if (Vehicle == null || Vehicle.Neutralized) return "unit knocked out";
				if (!On) return "switched off";
				if (ComputerDc != null && ComputerDc.IsDestroyed) return "computer destroyed";
				if (Time.time < ReadyAt) return $"warming up, {ReadyAt - Time.time:0} s left";
				if (Reloading) return "reloading";
				if (S.HatchInterlock && HatchOpen() is string h
				) return $"hatch open ({h})";
				return null;
			}

			internal string HatchOpen()
			{
				var crew = Vehicle != null ? Vehicle.GetComponent<CrewManager>()?.CrewMembers : null;
				if (crew == null) return null;
				foreach (var c in crew) if (c?.ExitHatch != null && c.ExitHatch.Openness > 0.05f) return c.Position.ToString().ToLowerInvariant();
				return null;
			}

			internal void Set(bool on)
			{
				if (on == On) return;
				On = on;
				if (on) ReadyAt = Time.time + S.WarmUp;
				if (IsPlayer) MelonLogger.Msg($"{S.Name}: {(on ? $"switched on, ready in {S.WarmUp / 60f:0.#} min" : "switched off")}.");
			}

			internal bool IsPlayer => PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle;

			internal int Empty { get { int n = 0; for (int i = 0; i < St.Rounds.Length; i++) if (!St.Lost[i]) n += G.Blocks[i].Rounds - St.Rounds[i]; return n; } }

			internal void StartReload()
			{
				if (Reloading || Empty == 0) return;
				Reloading = true; ReloadLeft = S.ReloadPerRound;
				if (IsPlayer) MelonLogger.Msg($"{S.Name}: reloading {Empty} round(s), {S.ReloadPerRound:0} s each with the tank stopped.");
			}

			internal void StopReload(string why)
			{
				if (!Reloading) return;
				Reloading = false;
				if (IsPlayer) MelonLogger.Msg($"{S.Name}: reload {why}; {St.Total} rounds loaded.");
			}

			internal bool Stopped
			{
				get { var rb = Vehicle != null ? Vehicle.GetComponent<Rigidbody>() : null; return rb == null || rb.velocity.magnitude <= S.ReloadMaxSpeed; }
			}

			void Update()
			{
				if (Vehicle == null || G == null) return;
				BindModules();
				for (int i = 0; i < BlockDc.Length; i++)
					if (!St.Lost[i] && BlockDc[i] != null && BlockDc[i].IsDestroyed)
					{
						St.Lost[i] = true;
						if (IsPlayer) MelonLogger.Msg($"{S.Name}: {G.Blocks[i].Name} launcher block destroyed.");
						ShowCaps();
					}
				if (Reloading)
				{
					if (Vehicle.Neutralized) { StopReload("stopped: unit knocked out"); return; }
					if (!Stopped) return;
					ReloadLeft -= Time.deltaTime;
					if (ReloadLeft > 0f) return;
					int i;
					for (i = 0; i < St.Rounds.Length; i++) if (!St.Lost[i] && St.Rounds[i] < G.Blocks[i].Rounds) break;
					if (i < St.Rounds.Length) { St.Rounds[i]++; ShowCaps(); }
					if (Empty == 0) StopReload("done");
					else ReloadLeft = S.ReloadPerRound;
				}
			}

			void OnDestroy() => Stations.Remove(this);

			internal void BeginFrame()
			{
				int f = Time.frameCount;
				_gone.Clear();
				foreach (var kv in _tracks)
					if (kv.Key == null || kv.Key.IsDestroyed || kv.Key.ID != kv.Value.Id || kv.Value.LastFrame < f - 1) _gone.Add(kv.Key);
				foreach (var r in _gone) _tracks.Remove(r);
			}

			internal void Consider(LiveRound r, Vector3 pos, float now, float dt)
			{
				if (Turret == null || r.Shooter == Vehicle) return;
				var center = Turret.position;
				if ((pos - center).sqrMagnitude > S.SearchRange * S.SearchRange) return;
				if (!_tracks.TryGetValue(r, out var w) || w.Id != r.ID)
				{
					w = new Watched { Id = r.ID };
					_tracks[r] = w;
				}
				int f = Time.frameCount;
				var fb = r.transform.forward * (r.CurrentSpeed > 1f ? r.CurrentSpeed : (r.Info != null ? r.Info.MuzzleVelocity : 0f));
				var mv = pos - w.Prev;
				var v3 = ApsMath.FrameVelocity(w.PrevFrame == f - 1, new V3(mv.x, mv.y, mv.z), dt, w.HasVel, w.Vel, new V3(fb.x, fb.y, fb.z));
				Vector3 vel = new Vector3(v3.x, v3.y, v3.z);
				w.Vel = v3; w.HasVel = true;
				w.Prev = pos; w.PrevFrame = f; w.LastFrame = f;
				if (w.T.Done) return;
				var pT = Turret.InverseTransformPoint(pos);
				var vT = Turret.InverseTransformDirection(vel);
				var p = new V3(pT.x, pT.y, pT.z); var v = new V3(vT.x, vT.y, vT.z);
				var root = Vehicle.transform;
				if (!w.HasMean) { w.Mean = vel; w.HasMean = true; }
				else w.Mean += (vel - w.Mean) * (1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, S.HeadingSmoothing)));
				var pH = root.InverseTransformPoint(pos); var dH = root.InverseTransformDirection(w.Mean.normalized);
				bool atBox = ApsMath.RayHitsBox(new V3(pH.x, pH.y, pH.z), new V3(dH.x, dH.y, dH.z), new V3(Box.center.x, Box.center.y, Box.center.z), new V3(Box.extents.x, Box.extents.y, Box.extents.z));
				float diameter = r.Info != null ? ApsMath.Diameter(r.Info.SectionalArea) : 0f;
				string window = ApsMath.Window(G, vel.magnitude, diameter, r.IsSpall);
				if (window != null)
				{
					if (!w.WindowLogged && atBox && (p - G.Origin).Length <= G.TrackRange)
					{
						w.WindowLogged = true; w.T.Done = true; w.T.Why = $"ignored ({window})";
						Report(w);
					}
					return;
				}
				if (!w.T.Committed && Claimed(r, Key)) { w.T.Done = true; w.T.Why = "left to another station that engages it"; Report(w); return; }
				string ready = NotReady();
				if (ready == null && G.Blocks.Length > 0)
				{
					float az = (p - G.Origin).Az;
					int side = az < 0f ? 0 : 1;
					if (SensorDc[side] != null && SensorDc[side].IsDestroyed) ready = $"the {(side == 0 ? "left" : "right")} radar head lost";
				}
				var res = ApsMath.Step(G, St, w.T, p, v, atBox, now, dt, ready);
				if (res.Fired) Launch(r, w);
				if (res.Act == ApsMath.Act.Intercept) Burst(r, w, res);
				else if (res.Act == ApsMath.Act.Drop) Report(w);
			}

			void Launch(LiveRound r, Watched w)
			{
				int bi = w.T.Block;
				Claim(r, Key);
				var spec = S.Blocks[bi];
				int tube = Mathf.Clamp(St.Rounds[bi], 0, spec.Mouths.Length - 1);
				var mouth = Vehicle.transform.TransformPoint(spec.Mouths[tube]);
				w.Mouth = spec.Mouths[tube]; w.Launched = true; w.Tube = tube + 1;
				if (Caps != null && Caps[bi][tube] != null) Caps[bi][tube].enabled = false;
				Blast.Play(mouth, Blast.Size.ApsLaunch, false);
				try { GHPC.Audio.FmodGenericAudioManager.PlayOneShot(S.LaunchEvent, mouth); } catch (Exception e) { DonorParts.WarnOnce("aps launch sound", $"{S.Tag}: launch sound '{S.LaunchEvent}' failed: {e.Message}"); }
				w.T.Why = $"block {G.Blocks[bi].Name} tube {tube + 1} fired";
			}

			void Burst(LiveRound r, Watched w, ApsMath.Result res)
			{
				int bi = w.T.Block;
				var blk = G.Blocks[bi];
				var B = w.T.BurstPoint;
				var burstWorld = Turret.TransformPoint(new Vector3(B.x, B.y, B.z));
				var hitWorld = Turret.TransformPoint(new Vector3(res.Hit.x, res.Hit.y, res.Hit.z));
				var axisWorld = Turret.TransformDirection(new Vector3(blk.Axis.x, blk.Axis.y, blk.Axis.z));
				var from = r.transform.position;
				var seg = hitWorld - from;
				if (seg.sqrMagnitude > 1e-6f && Physics.Raycast(from, seg.normalized, out var block, seg.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore) && !OwnCollider(block.collider))
				{
					w.T.Why = $"{w.T.Why}; burst, but {block.collider.name} between the threat and the burst point: no effect";
					Blast.Play(burstWorld, Blast.Size.ApsBurst);
					Fragments(burstWorld, axisWorld);
					r.Story?.TryAddStoryEvent($"{Engaged(blk.Name, w)}: {block.collider.name} in the way, no effect");
					RecapLines(r, w, burstWorld, axisWorld, null);
					Report(w);
					return;
				}
				Blast.Play(burstWorld, Blast.Size.ApsBurst);
				int frags = Fragments(burstWorld, axisWorld);
				if (!res.InField)
				{
					r.Story?.TryAddStoryEvent($"{Engaged(blk.Name, w)}: missed by {res.Miss:0.0} m, outside the fragments, not destroyed");
					RecapLines(r, w, burstWorld, axisWorld, null);
					w.T.Why = $"{w.T.Why}, burst {w.T.BurstRange:0.0} m out, the threat {res.Miss:0.0} m from it, outside the fragment field: no effect; frags {frags}";
					Report(w);
					return;
				}
				var cls = ClassOf(r);
				float pk = PKill(cls);
				float draw = ModRandom.Value;
				bool kill = ApsMath.Kills(pk, draw);
				string fate;
				r.Story?.TryAddStoryEvent($"{Engaged(blk.Name, w)}: fragments hit it {res.Miss:0.0} m from the burst");
				RecapLines(r, w, burstWorld, axisWorld, hitWorld);
				if (kill)
				{
					r.transform.position = hitWorld;
					float draw2 = ModRandom.Value;
					bool broken = ApsMath.BrokenUp(S.PBrokenUp, draw2);
					if (broken)
					{
						FailedToDetonate(r) = true;
						r.Story?.TryAddStoryEvent($"Broken up by {S.Name} ({blk.Name} block), warhead not set off");
						r.Detonate();
						fate = $"destroyed (broken up, warhead not set off; roll {draw2:0.00} < {S.PBrokenUp:0.00})";
					}
					else
					{
						Armed(r) = true;
						r.Story?.TryAddStoryEvent($"Intercepted by {S.Name} ({blk.Name} block)");
						var info = r.Info;
						if (Explode != null && info != null)
						{
							int orig = info.DetonateSpallCount;
							int n = Mathf.Max(1, Mathf.RoundToInt((orig > 0 ? orig : 50) * S.WarheadFragShare));
							try { info.DetonateSpallCount = n; Explode.Invoke(r, new object[] { false, 0f, Vector3.zero, 0.03f, n }); }
							catch (Exception e) { PatchGuard.Failed($"{S.Tag} warhead fragments", e); }
							finally { info.DetonateSpallCount = orig; }
						}
						r.Detonate();
						fate = "destroyed (warhead set off)";
					}
					InRecap(r, hitWorld);
				}
				else
				{
					r.Story?.TryAddStoryEvent($"Not destroyed by {S.Name}, flies on");
					fate = $"not destroyed (roll {draw:0.00} >= {pk:0.00}), flies on";
				}
				var shooter = r.Shooter;
				if (shooter != null && shooter.Allegiance != Vehicle.Allegiance)
					try { Vehicle.NotifyStruck(shooter, r.Info, hitWorld); } catch (Exception e) { PatchGuard.Failed($"{S.Tag} AI notice", e); }
				w.T.Why = $"{w.T.Why}, burst {w.T.BurstRange:0.0} m out, threat hit {res.Miss:0.0} m from it (put {res.Moved:0.00} m ahead) -> {fate} (P {pk:0.00}); frags {frags} in +-{S.FragConeHalf:0} deg";
				Report(w);
				if (IsPlayer)
					MelonLogger.Msg($"{S.Name}: {(kill ? "intercepted" : "engaged")} {r.Info?.Name ?? "a round"} ({blk.Name} block){(kill ? "" : ", not destroyed")}; {St.SideRounds(G, blk.Side)} round(s) left on the {(blk.Side == 0 ? "left" : "right")} side.");
			}

			void InRecap(LiveRound r, Vector3 at)
			{
				try
				{
					var si = r.ShotInfo;
					if (si == null) return;
					si = si.ParentShot ?? si;
					si.IsHit = true;
					si.DetonationPosition = at;
					if (!si.HitUnits.Contains(Vehicle))
					{
						si.HitUnits.Add(Vehicle);
						if (IsPlayer)
						{
							si.HitPlayerUnit = true;
							if (si.HitUnits[0] == (IUnit)Vehicle) si.HitPlayerUnitFirst = true;
						}
					}
					if (si.Distance < 0f) si.Distance = Vector3.Distance(si.StartPosition, at);
					var own = r.ShotInfo;
					var dir = at - own.StartPosition;
					own.AllShotFrames.Add(new GHPC.Weapons.ShotInfo.ShotSubHitInfo
					{
						WorldPosition = at, LocalPosition = at, ParentTransform = null,
						InstantVector = dir.sqrMagnitude > 1e-6f ? dir.normalized : r.transform.forward,
					});
					if (r.Story != null)
					{
						r.Story.ImpactPosition = at;
						if (r.Story.ImpactTime <= 0f) r.Story.ImpactTime = SceneController.MissionTime;
					}
				}
				catch (Exception e) { PatchGuard.Failed($"{S.Tag} recap entry", e); }
			}

			const int RecapFan = 10;
			void RecapLines(LiveRound r, Watched w, Vector3 burst, Vector3 axis, Vector3? caught)
			{
				try
				{
					var aar = AarController.Instance;
					var si = r.ShotInfo;
					if (aar == null || si == null || !w.Launched) return;
					var parent = si.ParentShot ?? si;
					Line(aar, parent, Vehicle.transform.TransformPoint(w.Mouth), burst, 20f);
					if (caught.HasValue) Line(aar, parent, burst, caught.Value, 12f);
					var right = Vector3.Cross(Vector3.up, axis);
					right = right.sqrMagnitude > 1e-6f ? right.normalized : Turret.right;
					var up = Vector3.Cross(axis, right);
					float tan = Mathf.Tan(S.FragConeHalf * Mathf.Deg2Rad);
					for (int i = 0; i < RecapFan; i++)
					{
						float rr = tan * Mathf.Sqrt((i + 0.5f) / RecapFan), phi = i * 2.39996323f;
						var d = (axis + rr * (Mathf.Cos(phi) * right + Mathf.Sin(phi) * up)).normalized;
						Line(aar, parent, burst, burst + d * S.ConeReach, 3f);
					}
				}
				catch (Exception e) { PatchGuard.Failed($"{S.Tag} recap lines", e); }
			}

			void Line(AarController aar, GHPC.Weapons.ShotInfo parent, Vector3 from, Vector3 to, float pen)
			{
				var dir = to - from;
				if (dir.sqrMagnitude < 1e-4f) return;
				if (_frag == null) _frag = FragmentCone.Type($"{S.Name} fragment", S.FragMass, S.FragArea, S.FragCoeff, 0f, S.FragPen, S.FragPenSpeed);
				var shot = new GHPC.Weapons.ShotInfo(from, dir.normalized, _frag, Vehicle, parent, pen);
				shot.IsHit = true;
				shot.Distance = dir.magnitude;
				shot.AllShotFrames.Add(new GHPC.Weapons.ShotInfo.ShotSubHitInfo { WorldPosition = to, LocalPosition = to, ParentTransform = null, InstantVector = dir.normalized });
				aar.AddShot(shot);
			}

			string Engaged(string block, Watched w) => $"{S.Name} {block} block, tube {w.Tube}: burst {w.T.BurstRange:0.0} m out";

			bool OwnCollider(Collider c) => c.GetComponentInParent<Unit>() == Vehicle || (c.TryGetComponent<IArmor>(out var a) && a.Unit == (IUnit)Vehicle);

			int Fragments(Vector3 at, Vector3 axis)
			{
				if (_frag == null) _frag = FragmentCone.Type($"{S.Name} fragment", S.FragMass, S.FragArea, S.FragCoeff, 0f, S.FragPen, S.FragPenSpeed);
				float pen = S.FragPen * (S.FragSpeed / S.FragPenSpeed) * (S.FragSpeed / S.FragPenSpeed);
				return FragmentCone.Spawn(_frag, at, axis, S.FragConeHalf, S.FragCount, S.FragSpeed, pen, Vehicle, !IsPlayer, null, $"{S.Name} fragment",
					() => ModRandom.Value, penAfterInit: true, fallbackRight: Turret.right
					);
			}

			AmmoType _frag;

			ApsMath.Threat ClassOf(LiveRound r) => r.Info == null ? ApsMath.Threat.Other
				: ApsMath.ClassOf(r.IsSpall, r.Info.Guidance != AmmoType.GuidanceType.Unguided, (int)r.Info.Category, r.Info.Name, S.GrenadePrefixes);

			float PKill(ApsMath.Threat t) => t == ApsMath.Threat.Atgm ? S.PAtgm : t == ApsMath.Threat.Grenade ? S.PGrenade : t == ApsMath.Threat.Heat ? S.PHeat : t == ApsMath.Threat.He ? S.PHe : 0f;

			void Report(Watched w)
			{
			}

			internal string HudLine()
			{
				string why = NotReady();
				string state = why == null ? "ready"
					: Reloading ? (Stopped ? $"reloading, {ReloadLeft:0} s ({Empty} to load)" : $"reloading: stop the tank ({Empty} to load)")
					: why;
				return $"{S.Name}: L {St.SideRounds(G, 0)} R {St.SideRounds(G, 1)}, {state}";
			}
		}

		static readonly AccessTools.FieldRef<LiveRound, bool> Armed = Reflect.FieldRefAccess<LiveRound, bool>("_armed");
		static readonly AccessTools.FieldRef<LiveRound, bool> FailedToDetonate = Reflect.FieldRefAccess<LiveRound, bool>("_failedToDetonate");

		internal static readonly List<Station> Stations = new List<Station>();

		internal static Station Of(Unit u)
		{
			if (u == null) return null;
			for (int i = 0; i < Stations.Count; i++) { var s = Stations[i]; if (s != null && s.Vehicle == u) return s; }
			return null;
		}

		internal static string Apply(Vehicle v, Settings s)
		{
			var st = v.gameObject.GetComponent<Station>() ?? v.gameObject.AddComponent<Station>();
			var line = st.Setup(v, s);
			st.ShowCaps();
			return line;
		}

		internal static string HudLine(Unit u) { var s = Of(u); return s != null && s.G != null ? s.HudLine() : null; }

		internal static string HudName(Unit u) => Of(u)?.S.Name;

		static Dictionary<long, int> _claims;
		static Dictionary<long, int> ClaimsTable
		{
			get
			{
				if (_claims != null) return _claims;
				const string key = "T90A.Common.Aps.Claims.v1";
				var had = AppDomain.CurrentDomain.GetData(key);
				if (had is Dictionary<long, int> t) _claims = t;
				else
				{
					if (had != null) MelonLogger.Warning("APS: the claim table of another port has another layout; this port keeps its own.");
					_claims = new Dictionary<long, int>();
					if (had == null) AppDomain.CurrentDomain.SetData(key, _claims);
				}
				return _claims;
			}
		}

		static long RoundKey(LiveRound r) => ((long)r.GetInstanceID() << 32) | (uint)r.ID;

		static bool Claimed(LiveRound r, int by) { lock (ClaimsTable) return ClaimsTable.TryGetValue(RoundKey(r), out int k) && k != by; }

		static void Claim(LiveRound r, int by)
		{
			lock (ClaimsTable)
			{
				if (ClaimsTable.Count > 1024) ClaimsTable.Clear();
				ClaimsTable[RoundKey(r)] = by;
			}
		}

		static bool _patched;
		static readonly AccessTools.FieldRef<LiveRoundBatchHandler, List<LiveRound>> LiveRounds = Reflect.FieldRefAccess<LiveRoundBatchHandler, List<LiveRound>>("_liveRounds");

		// Not [HarmonyPatch]: every port compiles this file; only a port with an APS patches anything.
		static void EnsurePatched()
		{
			if (_patched) return;
			_patched = true;
			var h = new HarmonyLib.Harmony(typeof(Aps).Assembly.GetName().Name + ".aps");
			var update = Reflect.Method(typeof(LiveRoundBatchHandler), "Update");
			if (update != null) h.Patch(update, postfix: new HarmonyMethod(typeof(Aps), nameof(WatchPostfix)));
			var menu = Reflect.Method(typeof(PlayerInput), "AddSmokeButtons");
			if (menu != null) h.Patch(menu, postfix: new HarmonyMethod(typeof(Aps), nameof(MenuPostfix)));
			if (LiveRounds == null) MelonLogger.Warning("APS: GHPC's live-round list not found (game update?); the APS sees nothing.");
		}

		static void WatchPostfix(LiveRoundBatchHandler __instance)
		{
			if (Stations.Count == 0 || Time.timeScale == 0f || LiveRounds == null) return;
			try { Scan(__instance); }
			catch (Exception e) { PatchGuard.Failed("APS watch", e); }
		}

		static void Scan(LiveRoundBatchHandler h)
		{
			if (AarController.InAar) return;
			var list = LiveRounds(h);
			if (list == null) return;
			float now = Time.time, dt = Time.deltaTime;
			for (int s = 0; s < Stations.Count; s++) Stations[s]?.BeginFrame();
			for (int i = 0; i < list.Count; i++)
			{
				var r = list[i];
				if (r == null || r.IsSpall || r.IsDestroyed || r.Info == null) continue;
				var pos = r.transform.position;
				for (int s = 0; s < Stations.Count; s++)
				{
					var st = Stations[s];
					if (st == null || st.Vehicle == null || st.G == null) continue;
					st.Consider(r, pos, now, dt);
					if (r.IsDestroyed) break;
				}
			}
		}

		static readonly MethodInfo Explode = Reflect.Method(typeof(LiveRound), "createExplosion", new[] { typeof(bool), typeof(float), typeof(Vector3), typeof(float), typeof(int) });
		static readonly MethodInfo AddButton = Reflect.Method(typeof(PlayerInput), "AddMenuButton", new[] { typeof(string), typeof(UnityAction) });

		static void MenuPostfix(PlayerInput __instance)
		{
			try
			{
				var st = Of(__instance.CurrentPlayerUnit);
				if (st == null || AddButton == null || st.G == null) return;
				string state = !st.On ? "OFF" : Time.time < st.ReadyAt ? $"ON, ready in {Mathf.CeilToInt(st.ReadyAt - Time.time)} s" : "ON";
				AddButton.Invoke(__instance, new object[] { $"{st.S.Name}/Toggle APS ({state})", (UnityAction)(() => st.Set(!st.On)) });
				if (st.Reloading) AddButton.Invoke(__instance, new object[] { $"{st.S.Name}/Cancel Reload", (UnityAction)(() => st.StopReload("cancelled")) });
				else if (st.Empty > 0) AddButton.Invoke(__instance, new object[] { $"{st.S.Name}/Reload ({st.Empty} empty)", (UnityAction)(() => st.StartReload()) });
			}
			catch (Exception e) { PatchGuard.Failed("APS vehicle menu", e); }
		}
	}
}
