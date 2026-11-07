using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Shaders.CRT;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace T90A.Common
{
	internal static class ThermalSight
	{
		internal sealed class Settings
		{
			public string Tag;
			public string Name;
			public float WideFov;
			public float[] OtherFovs;
			public float ElectronicFov;
			public int Width, Height;
			public Vector2 ElevationLimits;
			public Action<UsableOptic> Reticle;
			public Action<UsableOptic> Readout;
			public bool Monitor;
			public bool Crt = true;
		}

		static readonly FieldInfo UnitNightVision = Reflect.Field(typeof(GHPC.Unit), "_nightVisionType");

		// AI night spotting (VisibilityManager) reads the unit's type, not the slot's.
		internal static void SetUnitNightVision(Vehicle vehicle, CameraSlot slot) => UnitNightVision?.SetValue(vehicle, slot.VisionType);

		internal static bool Apply(UsableOptic day, UsableOptic night, Settings s)
		{
			var d = Donor.Get(s.Tag);
			if (d == null) { MelonLogger.Warning($"{s.Tag}: M60A3 TTS thermal donor not found; {s.Name} not applied (the base night sight kept)."); return false; }
			var slot = night.slot;
			var daySlot = day.slot;

			slot.transform.SetPositionAndRotation(daySlot.transform.position, daySlot.transform.rotation);
			night.Alignment = day.Alignment;
			night.RotateElevation = day.RotateElevation;
			night.RotateAzimuth = day.RotateAzimuth;
			night.CantCorrect = day.CantCorrect;
			night.CantCorrectMaxSpeed = day.CantCorrectMaxSpeed;
			night.LevelTransform = day.LevelTransform;
			night.ForceHorizontalReticleAlign = day.ForceHorizontalReticleAlign;
			night.UseRotationForShake = day.UseRotationForShake;
			night.LocalElevationLimits = s.ElevationLimits;
			night.LocalAzimuthLimits = day.LocalAzimuthLimits;
			night.RegisterAtStart = false;
			slot.ExclusiveItems = daySlot.ExclusiveItems;

			slot.VisionType = NightVisionType.Thermal;
			slot.DefaultFov = s.WideFov;
			slot.OtherFovs = s.OtherFovs ?? new float[0];
			slot.fovAspect = false;
			slot.AllowFreeZoom = true;
			slot.WrapFovLevels = false;
			slot.OverrideFLIRResolution = true;
			slot.FLIRWidth = s.Width;
			slot.FLIRHeight = s.Height;
			slot.FLIRFilterMode = FilterMode.Bilinear;
			slot.DoMechanicalFLIR = false;
			slot.FLIRBlitMaterialOverride = d.BlitFor(s.Crt, s.Monitor);
			slot.CanToggleFlirPolarity = true;
			slot.FLIRPolarityOnAudioEvent = d.Slot.FLIRPolarityOnAudioEvent;
			slot.FLIRPolarityOffAudioEvent = d.Slot.FLIRPolarityOffAudioEvent;
			slot.blackHotThermalText = null;
			slot.whiteHotThermalText = null;
			slot.ZoomInAudioEvent = d.Slot.ZoomInAudioEvent;
			slot.ZoomOutAudioEvent = d.Slot.ZoomOutAudioEvent;
			slot.BaseBlur = d.Slot.BaseBlur;
			slot.VibrationBlurScale = d.Slot.VibrationBlurScale;
			slot.VibrationPreBlur = d.Slot.VibrationPreBlur;
			slot.VibrationShakeMultiplier = daySlot.VibrationShakeMultiplier;
			slot.SpriteType = d.Slot.SpriteType;

			var layer = night.post != null ? night.post.gameObject.layer : 16;
			if (night.post == null) night.post = NewVolume(night.transform, s.Name + " post", layer);
			night.post.sharedProfile = d.MainCam;
			night.post.priority = d.MainCamPriority;
			night.post.weight = 1f;
			night.post.isGlobal = true;
			if (d.EarlyGen != null)
			{
				var extra = NewVolume(night.post.transform.parent, s.Name + " post (FLIR only)", layer);
				extra.sharedProfile = s.Crt ? d.EarlyGen : d.EarlyGenClean();
				extra.priority = d.EarlyGenPriority;
				night.postAdditional = new[] { extra };
			}
			night.AltNightPost = null;

			if (d.Crt != null && s.Crt)
			{
				var crt = UnityEngine.Object.Instantiate(d.Crt, slot.transform, false);
				crt.name = d.Crt.name;
				var err = crt.GetComponent<CrtError>();
				if (err != null) slot.ZoomChanged += err.Wipe;
			}

			night.gameObject.AddComponent<Resolution>().Init(slot, s.ElectronicFov, s.Width, s.Height);

			// The gate's on the day sight's GPS, which freezes once it isn't the view: from the thermal the gun read off after a reload.
			if (day.FCS != null) night.gameObject.AddComponent<GateFollowsSight>().Init(day.FCS, slot);
			GunnerFcs.AimClamp.Register(day.FCS, night);
			if (s.Monitor) night.gameObject.AddComponent<NoScopeMask>().Init(slot);
			night.gameObject.AddComponent<CompassScale>().Init(slot);
			night.gameObject.AddComponent<WithoutDaySight>().Slot = slot;

			s.Reticle?.Invoke(night);
			s.Readout?.Invoke(night);
			return true;
		}

		static PostProcessVolume NewVolume(Transform parent, string name, int layer)
		{
			var go = new GameObject(name) { layer = layer };
			go.transform.SetParent(parent, false);
			var v = go.AddComponent<PostProcessVolume>();
			v.isGlobal = true;
			v.weight = 1f;
			v.enabled = false;
			return v;
		}

		internal class Resolution : MonoBehaviour
		{
			CameraSlot _slot;
			float _electronicFov;
			int _width, _height;

			internal void Init(CameraSlot slot, float electronicFov, int width, int height)
			{
				_slot = slot;
				_electronicFov = electronicFov;
				_width = width;
				_height = height;
				slot.ZoomChanged += Refresh;
				Refresh();
			}

			void OnDestroy()
			{
				if (_slot != null) _slot.ZoomChanged -= Refresh;
			}

			void Refresh()
			{
				bool electronic = _electronicFov > 0f && Mathf.Approximately(_slot.FovIndex == 0 ? _slot.DefaultFov : _slot.OtherFovs[_slot.FovIndex - 1], _electronicFov);
				_slot.FLIRWidth = electronic ? _width / 2 : _width;
				_slot.FLIRHeight = electronic ? _height / 2 : _height;
				// CameraSlot only hands FLIRWidth / Height over on going active.
				var flir = GHPC.Thermals.FLIRCamera.Instance;
				if (_slot.IsActive && flir != null && flir.enabled)
				{
					flir.WidthOverride = _slot.FLIRWidth;
					flir.HeightOverride = _slot.FLIRHeight;
				}
			}
		}

		internal class NoScopeMask : MonoBehaviour
		{
			CameraSlot _slot;
			CameraSpriteManager _sprites;
			static readonly HashSet<CameraSlot> Slots = new HashSet<CameraSlot>();

			internal static bool Showing { get { var a = CameraSlot.ActiveInstance; return a != null && Slots.Contains(a); } }

			internal void Init(CameraSlot slot)
			{
				_slot = slot;
				Slots.Add(slot);
				slot.ActiveChanged += OnActive;
			}

			void OnDestroy()
			{
				if (_slot != null) { _slot.ActiveChanged -= OnActive; Slots.Remove(_slot); }
			}

			float _nextLook;
			CameraSpriteManager Sprites()
			{
				if (_sprites != null || Time.unscaledTime < _nextLook) return _sprites;
				_nextLook = Time.unscaledTime + 1f;
				return _sprites = FindObjectOfType<CameraSpriteManager>();
			}

			// CameraSpriteManager never turns its scope or goggles sprite off, and a monitor wants neither.
			void LateUpdate()
			{
				if (_slot == null || !_slot.IsActive) return;
				var m = Sprites();
				if (m?.Sprites == null) return;
				foreach (var s in m.Sprites) if (s.SpriteObject != null && s.SpriteObject.activeSelf) s.SpriteObject.SetActive(false);
			}

			void OnActive(bool active)
			{
				if (active) return;
				var m = Sprites();
				if (m == null) return;
				var next = CameraSlot.ActiveInstance;
				m.SetActiveSprite(next != null && next != _slot ? next.SpriteType : CameraSpriteManager.SpriteType.DefaultScope, forced: true);
			}
		}

		// Below _minimumTickCount (2) Compass shows its whole 32-tick pool, a 30 deg tape on the 1.99998 deg x2 field.
		internal class CompassScale : MonoBehaviour
		{
			static readonly FieldInfo MinTicks = Reflect.Field(typeof(GHPC.UI.Hud.Compass), "_minimumTickCount");
			CameraSlot _slot;
			GHPC.UI.Hud.Compass _compass;
			int _saved = -1;

			internal void Init(CameraSlot slot)
			{
				_slot = slot;
				slot.ActiveChanged += OnActive;
				if (slot.IsActive) OnActive(true);
			}

			void OnDestroy()
			{
				if (_slot != null) _slot.ActiveChanged -= OnActive;
				OnActive(false);
			}

			void OnActive(bool active)
			{
				if (MinTicks == null) return;
				if (active)
				{
					if (_compass == null) { _compass = FindObjectOfType<GHPC.UI.Hud.Compass>(true); _saved = -1; }
					if (_compass == null || _saved >= 0) return;
					_saved = (int)MinTicks.GetValue(_compass);
					MinTicks.SetValue(_compass, 1);
				}
				else if (_saved >= 0)
				{
					if (_compass != null) MinTicks.SetValue(_compass, _saved);
					_saved = -1;
				}
			}
		}

		internal class GateFollowsSight : MonoBehaviour
		{
			FireControlSystem _fcs;
			CameraSlot _slot;
			Transform _line;
			internal Transform Original;

			internal void Init(FireControlSystem fcs, CameraSlot slot)
			{
				_fcs = fcs;
				_slot = slot;
				_line = slot.transform;
				Original = fcs.FireGateOverrideTransform;
				slot.ActiveChanged += OnActive;
				if (slot.IsActive) OnActive(true);
			}

			void OnDestroy()
			{
				if (_slot != null) _slot.ActiveChanged -= OnActive;
			}

			void OnActive(bool active)
			{
				if (_fcs == null) return;
				if (GunnerFcs.OwnLay.Contains(_fcs)) return;
				if (GunnerFcs.GateOnGun.Contains(_fcs)) return;
				if (active) _fcs.FireGateOverrideTransform = _line;
				else if (_fcs.FireGateOverrideTransform == _line) _fcs.FireGateOverrideTransform = Original;
			}
		}

		// GHPC's night toggle only goes from the view to its own LinkedNightSight, and a dead day sight is never the view.
		internal class WithoutDaySight : MonoBehaviour { internal CameraSlot Slot; }

		[HarmonyPatch(typeof(PlayerInput), "Update")]
		internal static class NightToggleWithoutDaySight
		{
			static readonly FieldInfo FullControl = Reflect.Field(typeof(PlayerInput), "_hasFullVehicleControl"),
				Position = Reflect.Field(typeof(PlayerInput), "_controlledPosition");
			static CameraSlot _before;

			static void Prefix() => _before = CameraSlot.ActiveInstance;

			static void Postfix(PlayerInput __instance)
			{
				try
				{
					var view = _before;
					_before = null;
					if (view == null || CameraSlot.ActiveInstance != view || view.IsExterior || view.AllowFreeLook || view.IsLinkedNightSight || !view.NightSightNullOrInvalid) return;
					var p = __instance;
					if (!p.IsInitialized || !p.AllowPlayerStrictLiveInput || !p.PlayerCanControlGunnerNow || p.CurrentPlayerCrewBrain == null) return;
					if (FullControl != null && Position != null && !(bool)FullControl.GetValue(p) && (CrewPosition)Position.GetValue(p) != CrewPosition.Gunner) return;
					if (!GHPC.Utility.InputUtil.MainPlayer.GetButtonDown("Toggle Night Sight")) return;
					Toggle(p, view);
				}
				catch (Exception e) { PatchGuard.Failed("Night sight with the day sight destroyed", e); }
			}

			internal static void Toggle(PlayerInput p, CameraSlot view)
			{
				var fcs = p.CurrentPlayerCrewBrain?.WeaponsModule?.ActiveWeapon?.FCS;
				if (fcs == null || p.CurrentPlayerUnit == null) return;
				foreach (var w in p.CurrentPlayerUnit.GetComponentsInChildren<WithoutDaySight>(true))
				{
					var night = w.Slot;
					var day = night != null ? night.LinkedDaySight : null;
					if (day == null || !day.Damaged || day.PairedOptic?.FCS != fcs || day.NightSightNullOrInvalid || !night.IsAvailable) continue;
					var cam = CameraManager.Instance;
					if (cam == null) return;
					day.WasUsingNightMode = true;
					cam.TempAttachToSlot(night);
					if (CameraSlot.ActiveInstance == night) { _from = view; _to = night; }
					return;
				}
			}

			// ResetTempAttach goes back by slot list index, and the dead sight's fallback isn't in the list.
			static CameraSlot _from, _to;
			static readonly MethodInfo AttachDirect = Reflect.Method(typeof(CameraManager), "AttachCamToSlotDirect", new[] { typeof(CameraSlot) });

			[HarmonyPatch(typeof(CameraManager), "AttachCamToSlotDirect")]
			static class Forget
			{
				static void Postfix(CameraSlot __0) { if (__0 != _to) _from = _to = null; }
			}

			[HarmonyPatch(typeof(CameraManager), "ResetTempAttach")]
			static class Back
			{
				static bool Prefix(CameraManager __instance)
				{
					try
					{
						var from = _from;
						if (from == null || AttachDirect == null || CameraSlot.ActiveInstance == null || CameraSlot.ActiveInstance != _to || !from.IsAvailable) return true;
						AttachDirect.Invoke(__instance, new object[] { from });
						return false;
					}
					catch (Exception e) { PatchGuard.Failed("Night sight off with the day sight destroyed", e); return true; }
				}
			}
		}

		internal static void WarmDonor(string tag) => Donor.Get(tag);

		sealed class Donor
		{
			internal CameraSlot Slot;
			internal Material Blit;
			internal PostProcessProfile MainCam, EarlyGen;
			internal float MainCamPriority, EarlyGenPriority;
			internal GameObject Crt;

			static Donor _cached;

			internal static Donor Get(string tag)
			{
				if (_cached != null && _cached.Slot != null) return _cached;
				_cached = null;
				var prefab = DonorParts.Prefab("M60A3TTS");
				if (prefab == null) return null;
				var slot = prefab.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => s.VisionType == NightVisionType.Thermal);
				var optic = slot == null ? null : prefab.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.slot == slot);
				if (slot == null || optic == null || slot.FLIRBlitMaterialOverride == null) { DonorParts.WarnOnce("thermal", $"{tag}: M60A3 TTS has no thermal slot / optic / blit material."); return null; }
				var copy = DonorParts.Copy(prefab, $"{tag} thermal donor ({prefab.name})", slot.gameObject)?.GetComponentInChildren<CameraSlot>(true);
				if (copy == null) { DonorParts.WarnOnce("thermal", $"{tag}: M60A3 TTS thermal slot could not be copied."); return null; }
				var d = new Donor { Slot = copy };
				d.Blit = GreyMonitor(slot.FLIRBlitMaterialOverride, tag);
				if (optic.post != null) { d.MainCam = optic.post.sharedProfile; d.MainCamPriority = optic.post.priority; }
				var extra = optic.postAdditional?.FirstOrDefault(p => p != null);
				if (extra != null) { d.EarlyGen = extra.sharedProfile; d.EarlyGenPriority = extra.priority; }
				d.Crt = copy.GetComponentsInChildren<CrtError>(true).FirstOrDefault()?.gameObject;
				_cached = d;
				return d;
			}

			readonly Dictionary<(bool, bool), Material> _blits = new Dictionary<(bool, bool), Material>();
			PostProcessProfile _earlyGenClean;

			internal PostProcessProfile EarlyGenClean()
			{
				if (_earlyGenClean != null) return _earlyGenClean;
				var p = ScriptableObject.CreateInstance<PostProcessProfile>();
				p.name = EarlyGen.name + " (no grain, no bloom)";
				foreach (var e in EarlyGen.settings)
				{
					if (e == null || e is Grain || e is Bloom) continue;
					var copy = UnityEngine.Object.Instantiate(e);
					copy.name = e.name;
					p.settings.Add(copy);
				}
				p.isDirty = true;
				return _earlyGenClean = p;
			}

			internal Material BlitFor(bool crt, bool monitor)
			{
				if (crt && !monitor) return Blit;
				if (_blits.TryGetValue((crt, monitor), out var m) && m != null) return m;
				m = new Material(Blit) { name = Blit.name + (crt ? "" : ", clean") + (monitor ? ", no scope edge" : "") };
				if (!crt)
				{
					m.SetFloat("_ScanlineBrightness", 0f);
					m.SetFloat("_WobbleAmp", 0f);
					m.SetTexture("_PixelCookie", Texture2D.whiteTexture);
				}
				if (monitor)
				{
					var clear = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "no scope edge" };
					clear.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
					clear.Apply();
					m.SetTexture("_ScopeEdge", clear);
				}
				return _blits[(crt, monitor)] = m;
			}

			// Grey from the «Сосна-У» figures, the ESSA's is inferred.
			static Material GreyMonitor(Material green, string tag)
			{
				var m = new Material(green) { name = "Thermal grey (from " + green.name + ")" };
				var ramp = green.HasProperty("_ColorRamp") ? green.GetTexture("_ColorRamp") : null;
				if (ramp != null) m.SetTexture("_ColorRamp", GreyRamp(ramp));
				else MelonLogger.Warning($"{tag}: donor blit material has no _ColorRamp; the thermal image stays {green.name}.");
				return m;
			}

			static Texture2D GreyRamp(Texture src)
			{
				bool srgb = src.isDataSRGB;
				var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
				Graphics.Blit(src, rt);
				var prev = RenderTexture.active;
				RenderTexture.active = rt;
				var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, !srgb) { name = "Thermal grey ramp", wrapMode = src.wrapMode, filterMode = src.filterMode };
				tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
				RenderTexture.active = prev;
				RenderTexture.ReleaseTemporary(rt);
				var px = tex.GetPixels();
				for (int i = 0; i < px.Length; i++)
				{
					float l = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
					px[i] = new Color(l, l, l, px[i].a);
				}
				tex.SetPixels(px);
				tex.Apply(false, false);
				return tex;
			}
		}
	}
}
