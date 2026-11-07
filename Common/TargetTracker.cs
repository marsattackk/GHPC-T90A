using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using GHPC.Weapons;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace T90A.Common
{
	[DefaultExecutionOrder(-50)]
	// Has to lay the aim before FireControlSystem's LateUpdate takes the aim's rate for the lead, or the lead lags a frame.
	internal sealed partial class TargetTracker : MonoBehaviour
	{
		internal enum Mode { Manual, Auto, Inertial }

		internal sealed class Settings
		{
			internal float InertialMax = 6f;
			internal float MissileFlash = 0.5f;
			internal float ReadyPixels = 8f;
			internal float SearchWidth = 3f;
			internal float SampleInterval = 0.1f;
			internal float Alpha = 0.15f, Beta = 0.01f, AlphaStart = 0.6f, BetaStart = 0.3f, Settle = 1.5f;
			internal float LossConfirm = 0.3f;
			internal float Cell = 0.25f;
			internal int MaxGrid = 21;
			internal Func<KeyCode> Key;
			internal WeaponSystem[] ResetOnFire;
			internal Func<string> ResetWhen;
			internal bool NeedsStabiliser = true;
		}

		internal sealed class Channel
		{
			internal string Name;
			internal CameraSlot Slot;
			internal int[] CaptureSteps = new int[0];
			internal int Lines = 576;
			internal float PixelFov;
			internal bool Thermal;
			internal string Group = "main";
			internal bool KeyCaptures = true, KeyResets = true;
			internal Func<AmmoType, bool> LaseCaptures;
			internal bool ZoomResets;
			internal bool AmmoResets;
			internal TextMeshProUGUI SymbolLeft, SymbolRight;
			internal string Left = "[", Right = "]";
			internal Marks Marks;
			internal int LastFov = -1; internal bool WasView;
		}

		internal FireControlSystem Fcs;
		internal Settings S;
		internal Channel[] Channels;
		internal Mode State { get; private set; }

		internal IUnit Target { get; private set; }

		internal string Group { get; private set; }

		Transform TargetTransform()
		{
			if (Target == null) return null;
			try { return Target.transform; } catch (NullReferenceException) { return null; }
		}

		IUnit _own;
		Vector3 _point, _pointVel;
		float _capturedAt;
		Vector2 _offset;
		Vector2 _pendingInput;
		Vector2 _half;
		Vector3 _memPoint, _memVel;
		float _memSince, _memHoldUntil;
		float _lostSince = -1f;
Vector3 _vel; bool _velValid;
		float _nextSample;
		bool _ready; IUnit _readyUnit; Vector3 _readyLocal; Vector2 _readyHalf;
		bool _lased;
		Vector3 _prevWant; bool _hasPrevWant;
		readonly TrackServo _servo = new TrackServo();
		Channel _source;

		static readonly Dictionary<FireControlSystem, TargetTracker> ByFcs = new Dictionary<FireControlSystem, TargetTracker>();

		void OnDestroy()
		{
			if (Fcs != null) { Fcs.LaserFired -= OnLase; ByFcs.Remove(Fcs); }
			if (S?.ResetOnFire != null) foreach (var w in S.ResetOnFire) if (w != null) w.Fired -= OnFired;
		}

		void OnLase() => _lased = true;

		void OnSelect(AmmoType.AmmoClip clip)
		{
			if (State == Mode.Manual) return;
			if (Channels.Any(c => c.Group == Group && c.AmmoResets)) Reset($"round '{clip?.Name}' selected in group {Group}");
		}

		void OnFired(AmmoType a, LiveRound r)
		{
			if (State == Mode.Manual || a == null) return;
			if (a.Guidance != AmmoType.GuidanceType.Unguided) { EnterInertial(S.MissileFlash); return; }
			Reset($"'{a.Name}' fired");
		}

		void ChannelReset(Channel c, string why)
		{
			if (State != Mode.Manual && c.Group == Group) Reset(why);
		}

		internal void Reset(string why)
		{
			if (State == Mode.Manual) return;
			State = Mode.Manual;
			Target = null;
			_offset = Vector2.zero; _pendingInput = Vector2.zero;
			_hasPrevWant = _velValid = false;
			_servo.Reset();
		}

		void Capture(IUnit unit, Vector3 local, Vector2 half, string group)
		{
			State = Mode.Auto;
			Target = unit;
			_point = unit.transform.TransformPoint(local); _pointVel = Vector3.zero; _capturedAt = Time.time;
			_half = half;
			_offset = Vector2.zero; _pendingInput = Vector2.zero;
			Group = group;
			_hasPrevWant = _velValid = false; _lostSince = -1f;
			_servo.Reset();
			_nextSample = 0f;
			if (_source == null || _source.Group != group) _source = Channels.FirstOrDefault(c => c.Group == group && c.CaptureSteps.Length > 0);
		}

		void EnterInertial(float hold = 0f)
		{
			var tr = TargetTransform();
			if (tr == null) return;
			_memPoint = _point;
			// The visible part's centre slows down as the target slides behind cover, so the target's own speed is used.
			var ch = Target.Chassis;
			_memVel = ch != null ? ch.Velocity : _velValid ? _vel : Vector3.zero;
			_memSince = Time.time;
			_memHoldUntil = Time.time + hold;
			State = Mode.Inertial;
			_lostSince = -1f;
			_hasPrevWant = false;
		}

		Vector3 Predicted() => _memPoint + _memVel * (Time.time - _memSince);

		Vector3 Eye(Channel c)
		{
			if (c?.Slot != null) return c.Slot.transform.position;
			if (_source?.Slot != null) return _source.Slot.transform.position;
			return Fcs.LaserOrigin != null ? Fcs.LaserOrigin.position : Fcs.ReferenceTransform.position;
		}

		bool Players()
		{
			var pi = GHPC.Player.PlayerInput.Instance;
			return pi != null && _own != null && (object)pi.CurrentPlayerUnit == _own;
		}

		Channel ActiveChannel()
		{
			foreach (var c in Channels) if (c.Slot != null && c.Slot.IsActive) return c;
			return null;
		}

		bool KeyDown()
		{
			var k = S.Key != null ? S.Key() : KeyCode.None;
			return k != KeyCode.None && Input.GetKeyDown(k);
		}

		void LateUpdate()
		{
			if (Fcs == null || !Fcs.enabled) return;
			var view = ActiveChannel();
			string off = !Players() ? "not the player's unit" : Fcs.FcsFault ? "fire control out of action" : S.NeedsStabiliser && !Fcs.StabsActive ? "stabiliser off"
				: S.ResetWhen?.Invoke();
			if (off != null)
			{
				Reset(off);
				_ready = false;
				_lased = false;
				WatchFields();
				ShowMarks(view);
				return;
			}
			WatchFields();
			if (State != Mode.Manual && view != null && view.Group != Group) Reset($"view changed to {view.Name} ({view.Group})");
			if (State != Mode.Manual && view != null && view.Slot.Damaged) Reset($"{view.Name} damaged");
			if (view != null && view.CaptureSteps.Length > 0 && (State == Mode.Manual || view.Group == Group)) _source = view;
			bool key = view != null && KeyDown();
			if (State == Mode.Manual)
			{
				bool lase = _lased && view != null && view.LaseCaptures != null && view.LaseCaptures(Fcs.CurrentAmmoType);
				UpdateReady(view, lase || key);
				if (_ready && view != null && (key && view.KeyCaptures || lase)) Capture(_readyUnit, _readyLocal, _readyHalf, view.Group);
			}
			else if (key && view.KeyResets) Reset("key");
			if (State != Mode.Manual) Track(view);
			_lased = false;
			_pendingInput = Vector2.zero;
			ShowMarks(view);
		}

		// ForceUpdateFov fires ZoomChanged with the field unchanged and an attach can put a field back, so the index has to actually move while the channel is the view.
		void WatchFields()
		{
			foreach (var c in Channels)
			{
				int f = c.Slot.FovIndex;
				bool isView = c.Slot.IsActive;
				if (c.ZoomResets && isView && c.WasView && f != c.LastFov) ChannelReset(c, $"{c.Name}: field changed (step {c.LastFov} -> {f})");
				c.LastFov = f; c.WasView = isView;
			}
		}

		void UpdateReady(Channel view, bool now)
		{
			if (view == null || !view.CaptureSteps.Contains(view.Slot.FovIndex) || view.Slot.Damaged)
			{
				_ready = false;
				return;
			}
			if (!now && Time.time < _nextSample) return;
			_nextSample = Time.time + S.SampleInterval;
			_ready = false;
			var cam = view.Slot.transform;
			var eye = cam.position;
			var line = cam.forward;
			var mask = ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask;
			if (!RaycastColliderUtils.Raycast(new Ray(eye + line * ClearOfOwn, line), out var hit, Fcs.MaxLaserRange > 0f ? Fcs.MaxLaserRange : 10000f, mask)) return;
			var u = Owner(hit.collider);
			if (u == null || u == _own || u.transform == null || Smoked(eye, line, hit.distance + ClearOfOwn, view.Thermal)) return;
			var s = Sample(u, eye, view.Thermal, S.Cell, S.MaxGrid);
			if (!s.Visible) return;
			float px = Mathf.Min(s.Half.x, s.Half.y) * 2f / ((view.PixelFov > 0f ? view.PixelFov : VerticalFov(view.Slot)) * Mathf.Deg2Rad / view.Lines);
			if (px < S.ReadyPixels) return;
			_ready = true;
			_readyUnit = u;
			_readyLocal = u.transform.InverseTransformPoint(s.Centre);
			_readyHalf = s.Half;
		}

		void Track(Channel view)
		{
			var tr = TargetTransform();
			if (tr == null) { Reset("target gone"); return; }
			float dt = Time.deltaTime;
			if (dt <= 0f) return;
			var cam = view != null && view.Group == Group ? view : _source;
			var eye = Eye(cam);
			bool thermal = _source == null || _source.Thermal;
			if (Time.time >= _nextSample)
			{
				_nextSample = Time.time + S.SampleInterval;
				if (State == Mode.Auto)
				{
					var s = Sample(Target, eye, thermal, S.Cell, S.MaxGrid);
					if (s.Visible)
					{
						float k = S.Settle > 0f ? Mathf.Clamp01((Time.time - _capturedAt) / S.Settle) : 1f;
						var r = s.Centre - _point;
						_point += Mathf.Lerp(S.AlphaStart, S.Alpha, k) * r;
						_pointVel += Mathf.Lerp(S.BetaStart, S.Beta, k) * r / Mathf.Max(S.SampleInterval, 0.02f);
						_vel = _pointVel; _velValid = true;
						_half = s.Half;
						_lostSince = -1f;
					}
					else if (_lostSince < 0f) _lostSince = Time.time;
					else if (Time.time - _lostSince >= S.LossConfirm) EnterInertial();
				}
				else if (Time.time >= _memHoldUntil)
				{
					var s = Sample(Target, eye, thermal, S.Cell, S.MaxGrid);
					var pred = Predicted();
					float window = S.SearchWidth * Mathf.Max(Mathf.Max(_half.x, _half.y), 0.002f);
					if (s.Visible && Vector3.Angle(s.Centre - eye, pred - eye) * Mathf.Deg2Rad <= window)
					{
						_point = s.Centre; _pointVel = _memVel; _capturedAt = Time.time;
						_half = s.Half;
						State = Mode.Auto;
						_lostSince = -1f;
						_hasPrevWant = false;
						_servo.Reset();
						_velValid = false;
					}
					else if (Time.time - _memSince > S.InertialMax) { Reset($"inertial longer than {S.InertialMax:0} s"); return; }
				}
			}
			if (State == Mode.Auto) _point += _pointVel * dt;
			Steer(State == Mode.Auto ? _point : Predicted(), eye, dt);
		}

		void Steer(Vector3 point, Vector3 eye, float dt)
		{
			var rt = Fcs.ReferenceTransform;
			var aim = Fcs.AimWorldVector.normalized;
			if (_pendingInput != Vector2.zero)
			{
				float lim = Mathf.Max(Mathf.Max(_half.x, _half.y) * 1.5f, 0.002f) * Mathf.Rad2Deg;
				var o = _offset + _pendingInput;
				_pendingInput = Vector2.zero;
				if (Mathf.Abs(o.x) > lim || Mathf.Abs(o.y) > lim) { Reset("aimed away from the target"); return; }
				_offset = o;
			}
			var line = (point - eye).normalized;
			var want = (Quaternion.AngleAxis(_offset.x, rt.up) * Quaternion.AngleAxis(-_offset.y, rt.right) * line).normalized;
			var e = YawPitch(rt, aim, want);
			var dWant = _hasPrevWant ? YawPitch(rt, _prevWant, want) : Vector2.zero;
			_prevWant = want; _hasPrevWant = true;
			_servo.Step(e.x, e.y, dWant.x, dWant.y, dt, out float sy, out float sp);
			// Same call as the gunner's handles. GHPC builds the lead off AdjustAimByDegrees only, set the aim any other way and there's no lead.
			Fcs.AdjustAimByDegrees(sy, -sp);
		}

		static Vector2 YawPitch(Transform frame, Vector3 a, Vector3 b)
		{
			var la = frame.InverseTransformDirection(a); var lb = frame.InverseTransformDirection(b);
			float ya = Mathf.Atan2(la.x, la.z), yb = Mathf.Atan2(lb.x, lb.z);
			float pa = Mathf.Atan2(la.y, new Vector2(la.x, la.z).magnitude), pb = Mathf.Atan2(lb.y, new Vector2(lb.x, lb.z).magnitude);
			return new Vector2(Mathf.DeltaAngle(ya * Mathf.Rad2Deg, yb * Mathf.Rad2Deg), (pb - pa) * Mathf.Rad2Deg);
		}

		internal struct Image { internal bool Visible; internal Vector3 Centre; internal Vector2 Half; internal int Hits; }

		const float ClearOfOwn = 8f;   // from the laser window a ray to a target off to the side clips our own turret stowage
		static readonly RaycastHit[] SmokeBuf = new RaycastHit[16];
		struct Hit { internal Vector3 P; internal float X, Y; }

		static readonly List<Hit> HitBuf = new List<Hit>();

		static readonly Dictionary<IUnit, Renderer[]> RenderersOf = new Dictionary<IUnit, Renderer[]>();
		static readonly Dictionary<Collider, IUnit> OwnerOf = new Dictionary<Collider, IUnit>();
		static readonly Vector3[] CornerBuf = new Vector3[8];

		static void CornersOf(Renderer r, Vector3[] into)
		{
			if (r is MeshRenderer)
			{
				var lb = r.localBounds;
				if (lb.size.sqrMagnitude > 1e-6f)
				{
					var m = r.transform.localToWorldMatrix;
					for (int i = 0; i < 8; i++) into[i] = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z));
					return;
				}
			}
			var b = r.bounds;
			for (int i = 0; i < 8; i++) into[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
		}

		static IUnit Owner(Collider c)
		{
			if (c == null) return null;
			if (OwnerOf.TryGetValue(c, out var u)) return u;
			if (OwnerOf.Count > 4096) OwnerOf.Clear();
			return OwnerOf[c] = CodeUtils.FindComponentInParentFollowAware<IUnit>(c.transform);
		}

		static bool Smoked(Vector3 eye, Vector3 dir, float dist, bool thermal)
		{
			int n = Physics.RaycastNonAlloc(eye, dir, SmokeBuf, dist, 1 << CodeUtils.LAYER_INDEX_VISIBILITYONLY);
			for (int i = 0; i < n; i++)
			{
				var h = SmokeBuf[i];
				if (!h.collider.CompareTag("Smoke")) continue;
				if (!thermal) return true;
				var b = h.collider.GetComponent<BlockerInformation>();
				if (b != null && b.IsMultispectral) return true;
			}
			return false;
		}

		internal static Image Sample(IUnit u, Vector3 eye, bool thermal, float cell = 0.25f, int maxGrid = 21)
		{
			var img = new Image();
			if (u == null || u.transform == null) return img;
			if (!RenderersOf.TryGetValue(u, out var rs) || rs == null || rs.Any(r => r == null))
			{
				rs = u.transform.GetComponentsInChildren<Renderer>(true).Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && !(r.sharedMaterial != null && r.sharedMaterial.renderQueue >= 3000)).ToArray();
				if (RenderersOf.Count > 256) RenderersOf.Clear();
				RenderersOf[u] = rs;
			}
			var box = new Bounds(); bool any = false;
			foreach (var r in rs)
			{
				if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
				var b = r.bounds;
				if (b.size.sqrMagnitude > 30f * 30f) continue;
				if (!any) { box = b; any = true; } else box.Encapsulate(b);
			}
			if (!any) return img;
			var fwd = (box.center - eye).normalized;
			var right = Vector3.Cross(Vector3.up, fwd).normalized;
			if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
			var up = Vector3.Cross(fwd, right);
			float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
			foreach (var r in rs)
			{
				if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
				if (r.bounds.size.sqrMagnitude > 30f * 30f) continue;
				CornersOf(r, CornerBuf);
				for (int i = 0; i < 8; i++)
				{
					var c = CornerBuf[i] - eye;
					float z = Mathf.Max(Vector3.Dot(c, fwd), 0.1f);
					float x = Mathf.Atan2(Vector3.Dot(c, right), z), y = Mathf.Atan2(Vector3.Dot(c, up), z);
					x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
				}
			}
			float dist = Vector3.Distance(eye, box.center);
			float reach = dist + box.extents.magnitude + 5f;
			float cellAng = Mathf.Max(cell, 0.02f) / Mathf.Max(dist, 1f);
			int nx = Mathf.Clamp(Mathf.CeilToInt((x1 - x0) / cellAng), 5, Mathf.Max(maxGrid, 5));
			int ny = Mathf.Clamp(Mathf.CeilToInt((y1 - y0) / cellAng), 5, Mathf.Max(maxGrid, 5));
			var mask = ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask;
			float cx = (x1 - x0) / nx, cy = (y1 - y0) / ny;
			float hx0 = float.MaxValue, hx1 = float.MinValue, hy0 = float.MaxValue, hy1 = float.MinValue;
			float sx = 0f, sy = 0f;
			HitBuf.Clear();
			for (int i = 0; i < nx; i++)
				for (int j = 0; j < ny; j++)
				{
					float ax = x0 + (i + 0.5f) * cx, ay = y0 + (j + 0.5f) * cy;
					var dir = (fwd + right * Mathf.Tan(ax) + up * Mathf.Tan(ay)).normalized;
					if (!RaycastColliderUtils.Raycast(new Ray(eye + dir * ClearOfOwn, dir), out var hit, reach, mask) || Owner(hit.collider) != u || Smoked(eye, dir, hit.distance + ClearOfOwn, thermal)) continue;
					img.Hits++;
					sx += ax; sy += ay;
					HitBuf.Add(new Hit { P = hit.point, X = ax, Y = ay });
					hx0 = Mathf.Min(hx0, ax); hx1 = Mathf.Max(hx1, ax); hy0 = Mathf.Min(hy0, ay); hy1 = Mathf.Max(hy1, ay);
				}
			if (img.Hits == 0) return img;
			img.Visible = true;
			float mx = sx / img.Hits, my = sy / img.Hits;
			// The silhouette's centroid can fall beside the tank (hull, turret and barrel), so the hit nearest it is the aim point.
			float best = float.MaxValue;
			foreach (var h in HitBuf)
			{
				float d = (h.X - mx) * (h.X - mx) + (h.Y - my) * (h.Y - my);
				if (d < best) { best = d; img.Centre = h.P; }
			}
			img.Half = new Vector2((hx1 - hx0 + cx) / 2f, (hy1 - hy0 + cy) / 2f);
			return img;
		}

		static float VerticalFov(CameraSlot s)
		{
			var cam = CameraManager.MainCam;
			return s.IsActive && cam != null ? cam.fieldOfView : s.CurrentFov;
		}

		void ShowMarks(Channel view)
		{
			foreach (var c in Channels)
			{
				if (c.SymbolLeft != null) c.SymbolLeft.text = State != Mode.Manual ? c.Left : "";
				if (c.SymbolRight != null) c.SymbolRight.text = State != Mode.Manual ? c.Right : "";
				if (c.Marks == null) continue;
				bool on = c == view && c.CaptureSteps.Contains(c.Slot.FovIndex);
				if (!on) { c.Marks.Show(false, false, false, default, default); continue; }
				if (State == Mode.Manual) { c.Marks.Show(_ready, false, false, default, default); continue; }
				var cam = c.Slot.transform;
				var p = State == Mode.Auto ? _point : Predicted();
				var l = cam.InverseTransformDirection(p - cam.position);
				float u = 600f / (VerticalFov(c.Slot) * Mathf.Deg2Rad);
				var centre = new Vector2(Mathf.Atan2(l.x, l.z), Mathf.Atan2(l.y, l.z)) * u;
				c.Marks.Show(false, State == Mode.Auto, State == Mode.Inertial, centre, _half * u);
			}
		}

		internal sealed class Marks
		{
			struct Part { internal RectTransform Rt; internal Vector2 Pos, Size; }

			Part[] _ready, _lock, _memory;
			Vector2 _lockHalf, _memHalf;
			bool _rOn, _lOn, _mOn;

			internal void Show(bool ready, bool locked, bool memory, Vector2 centre, Vector2 half)
			{
				Set(_ready, ready, ref _rOn);
				Set(_lock, locked, ref _lOn);
				Set(_memory, memory, ref _mOn);
				if (locked) Fit(_lock, _lockHalf, centre, half);
				if (memory) Fit(_memory, _memHalf, centre, half);
			}

			static void Set(Part[] ps, bool on, ref bool was)
			{
				if (on == was) return;
				was = on;
				foreach (var p in ps) if (p.Rt != null) p.Rt.gameObject.SetActive(on);
			}

			static void Fit(Part[] ps, Vector2 nominal, Vector2 centre, Vector2 half)
			{
				var h = new Vector2(Mathf.Clamp(half.x, nominal.x, 270f), Mathf.Clamp(half.y, nominal.y, 270f));
				var d = h - nominal;
				foreach (var p in ps)
				{
					if (p.Rt == null) continue;
					float sx = Mathf.Abs(p.Pos.x) < 0.01f ? 0f : Mathf.Sign(p.Pos.x), sy = Mathf.Abs(p.Pos.y) < 0.01f ? 0f : Mathf.Sign(p.Pos.y);
					p.Rt.anchoredPosition = centre + p.Pos + new Vector2(sx * d.x, sy * d.y);
					p.Rt.sizeDelta = p.Size + new Vector2(sx == 0f ? 2f * d.x : 0f, sy == 0f ? 2f * d.y : 0f);
				}
			}
		}

		[HarmonyPatch(typeof(FireControlSystem), nameof(FireControlSystem.AdjustAimByRatio))]
		static class InputPatch
		{
			static void Prefix(FireControlSystem __instance, out Vector3 __state) => __state = __instance.AimWorldVector;

			static void Postfix(FireControlSystem __instance, float horizontalRatio, float verticalRatio, Vector3 __state)
			{
				try
				{
					if (horizontalRatio == 0f && verticalRatio == 0f) return;
					if (!ByFcs.TryGetValue(__instance, out var t) || t == null || t.State == Mode.Manual || __state == Vector3.zero) return;
					t._pendingInput += YawPitch(__instance.ReferenceTransform, __state.normalized, __instance.AimWorldVector.normalized);
				}
				catch (Exception e) { PatchGuard.Failed("Target tracker input", e); }
			}
		}

		[HarmonyPatch(typeof(AmmoFeed), nameof(AmmoFeed.SetNextClipType))]
		static class SelectPatch
		{
			static void Prefix(AmmoFeed __instance, out AmmoType.AmmoClip __state)
			{
				__state = null;
				try { if (ByFcs.Count > 0) __state = __instance.QueuedClipType; }
				catch (Exception e) { PatchGuard.Failed("Target tracker round select", e); }
			}

			static void Postfix(AmmoFeed __instance, AmmoType.AmmoClip clipType, AmmoType.AmmoClip __state)
			{
				try
				{
					if (ByFcs.Count == 0 || clipType == null || clipType == __state) return;
					foreach (var t in ByFcs.Values)
						if (t != null && t.Fcs.LinkedWeaponSystems != null && t.Fcs.LinkedWeaponSystems.Any(w => w != null && w.Feed == __instance)) t.OnSelect(clipType);
				}
				catch (Exception e) { PatchGuard.Failed("Target tracker round select", e); }
			}
		}

		[HarmonyPatch(typeof(CameraSlot), nameof(CameraSlot.ToggleFLIRPolarity))]
		static class PolarityPatch
		{
			static void Postfix(CameraSlot __instance)
			{
				try
				{
					foreach (var t in ByFcs.Values)
						if (t != null) foreach (var c in t.Channels) if (c.Slot == __instance && c.Thermal) t.ChannelReset(c, $"{c.Name}: polarity changed");
				}
				catch (Exception e) { PatchGuard.Failed("Target tracker polarity", e); }
			}
		}
	}
}
