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
using UnityEngine.UI;

namespace T90A.Common
{
	// Thermal sight on a donor M60A3 TTS slot: views, scan streaks, resolution, night toggle and monitor.
	internal static class ThermalSight
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
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
			public float ScanStreaks;
			public int ScanRows;
			public float ScanDriftSeconds;
		}
#pragma warning restore CS0649

		static FieldInfo _unitNightVision;

		// AI night spotting (VisibilityManager) reads the unit's type, not the slot's.
		internal static void SetUnitNightVision(Vehicle vehicle, CameraSlot slot)
			=> (_unitNightVision ??= Reflect.Field(typeof(GHPC.Unit), "_nightVisionType"))?.SetValue(vehicle, slot.VisionType);

		internal static string Describe(UsableOptic o)
		{
			var slot = o.slot;
			return $"slot '{slot.name}' {slot.VisionType}, fov {slot.DefaultFov} [{string.Join(", ", slot.OtherFovs ?? new float[0])}] aspect {slot.fovAspect}, "
				+ $"FLIR {(slot.OverrideFLIRResolution ? $"{slot.FLIRWidth}x{slot.FLIRHeight} {slot.FLIRFilterMode}" : "screen")}, blit {(slot.FLIRBlitMaterialOverride ? slot.FLIRBlitMaterialOverride.name : "-")}, "
				+ $"polarity toggle {slot.CanToggleFlirPolarity}, blur {slot.BaseBlur}/{slot.VibrationBlurScale} pre {slot.VibrationPreBlur}, shake {slot.VibrationShakeMultiplier}; "
				+ $"optic {o.Alignment} elev {o.RotateElevation} azim {o.RotateAzimuth} cant {o.CantCorrect} limits {o.LocalElevationLimits}; "
				+ $"post {(o.post && o.post.sharedProfile ? o.post.sharedProfile.name : "-")}{string.Concat((o.postAdditional ?? new PostProcessVolume[0]).Select(v => " + " + (v && v.sharedProfile ? v.sharedProfile.name : "-")))}; "
				+ $"reticle {(o.reticleMesh && o.reticleMesh.reticleSO ? o.reticleMesh.reticleSO.name : "-")}; at {slot.transform.position}";
		}

		internal static bool Apply(UsableOptic day, UsableOptic night, Settings s)
		{
			var donor = Donor.Get(s.Tag);
			if (donor == null)
			{
				MelonLogger.Warning($"{s.Tag}: M60A3 TTS thermal donor not found; {s.Name} not applied (the base night sight kept).");
				return false;
			}
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
			slot.FLIRBlitMaterialOverride = donor.BlitFor(s.Crt, s.Monitor, s.Tag);
			slot.CanToggleFlirPolarity = true;
			slot.FLIRPolarityOnAudioEvent = donor.Slot.FLIRPolarityOnAudioEvent;
			slot.FLIRPolarityOffAudioEvent = donor.Slot.FLIRPolarityOffAudioEvent;
			slot.blackHotThermalText = null;
			slot.whiteHotThermalText = null;
			slot.ZoomInAudioEvent = donor.Slot.ZoomInAudioEvent;
			slot.ZoomOutAudioEvent = donor.Slot.ZoomOutAudioEvent;
			slot.BaseBlur = donor.Slot.BaseBlur;
			slot.VibrationBlurScale = donor.Slot.VibrationBlurScale;
			slot.VibrationPreBlur = donor.Slot.VibrationPreBlur;
			slot.VibrationShakeMultiplier = daySlot.VibrationShakeMultiplier;
			slot.SpriteType = donor.Slot.SpriteType;

			var layer = night.post != null ? night.post.gameObject.layer : 16;
			if (night.post == null) night.post = NewVolume(night.transform, s.Name + " post", layer);
			night.post.sharedProfile = donor.MainCam;
			night.post.priority = donor.MainCamPriority;
			night.post.weight = 1f;
			night.post.isGlobal = true;
			if (donor.EarlyGen != null)
			{
				var extra = NewVolume(night.post.transform.parent, s.Name + " post (FLIR only)", layer);
				extra.sharedProfile = s.Crt ? donor.EarlyGen : donor.EarlyGenClean(s.Tag);
				extra.priority = donor.EarlyGenPriority;
				night.postAdditional = new[] { extra };
			}
			night.AltNightPost = null;

			if (donor.Crt != null && s.Crt)
			{
				var crt = UnityEngine.Object.Instantiate(donor.Crt, slot.transform, false);
				crt.name = donor.Crt.name;
				var err = crt.GetComponent<CrtError>();
				if (err != null) slot.ZoomChanged += err.Wipe;
			}

			night.gameObject.AddComponent<Resolution>().Init(slot, s.ElectronicFov, s.Width, s.Height);
			if (s.ScanStreaks > 0f && s.ScanRows > 0)
				ScanStreaks.Add(night, s.ScanStreaks, s.ScanRows, s.ElectronicFov, s.ScanDriftSeconds);

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
			var holder = new GameObject(name) { layer = layer };
			holder.transform.SetParent(parent, false);
			var volume = holder.AddComponent<PostProcessVolume>();
			volume.isGlobal = true;
			volume.weight = 1f;
			volume.enabled = false;
			return volume;
		}

		internal class ScanStreaks : MonoBehaviour
		{
			CameraSlot _slot;
			float _electronicFov;
			RawImage _image;
			Texture2D _tex;
			Color32[] _pixels;
			float[] _gain;
			float _drift, _nextPaint;

			internal static void Add(UsableOptic optic, float strength, int rows, float electronicFov, float driftSeconds)
			{
				var root = new GameObject("Thermal scan streaks", typeof(RectTransform));
				root.transform.SetParent(optic.transform, false);
				var canvas = root.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = -2;
				var picture = new GameObject("streaks", typeof(RectTransform));
				picture.transform.SetParent(root.transform, false);
				var pictureRect = (RectTransform)picture.transform;
				pictureRect.anchorMin = Vector2.zero;
				pictureRect.anchorMax = Vector2.one;
				pictureRect.sizeDelta = Vector2.zero;
				var streaks = root.AddComponent<ScanStreaks>();
				streaks._slot = optic.slot;
				streaks._electronicFov = electronicFov;
				streaks._image = picture.AddComponent<RawImage>();
				streaks._drift = driftSeconds;
				streaks._tex = new Texture2D(
					1,
					rows,
					TextureFormat.RGBA32,
					false) { name = "scan streaks", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
				streaks._pixels = new Color32[rows];
				streaks._gain = new float[rows];
				var rnd = new System.Random(rows);
				for (int y = 0; y < rows; y++) streaks._gain[y] = strength * (rnd.Next(24) == 0 ? 1.6f : 1f);
				streaks.Paint();
				streaks._image.texture = streaks._tex;
				streaks._image.raycastTarget = false;
				optic.slot.ZoomChanged += streaks.Refresh;
				streaks.Refresh();
			}

			void Paint()
			{
				float phase = (_drift > 0f ? Time.time / _drift : 0f) + 0.37f;
				for (int y = 0; y < _pixels.Length; y++)
				{
					float noise = Mathf.Clamp01((Mathf.PerlinNoise(y * 13.37f + 0.5f, phase) - 0.25f) * 2f);
					_pixels[y] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(noise * _gain[y]) * 255f));
				}
				_tex.SetPixels32(_pixels);
				_tex.Apply(false, false);
			}

			void Update()
			{
				if (_drift <= 0f || !_slot.IsActive || Time.time < _nextPaint) return;
				_nextPaint = Time.time + 0.25f;
				Paint();
			}

			void OnDestroy()
			{
				if (_slot != null) _slot.ZoomChanged -= Refresh;
				if (_tex != null) Destroy(_tex);
			}

			void Refresh()
			{
				bool electronic = _electronicFov > 0f
					&& _slot.FovIndex > 0
					&& Mathf.Approximately(_slot.OtherFovs[_slot.FovIndex - 1], _electronicFov);
				_image.uvRect = electronic ? new Rect(0f, 0.25f, 1f, 0.5f) : new Rect(0f, 0f, 1f, 1f);
			}
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
				bool electronic = _electronicFov > 0f
					&& Mathf.Approximately(_slot.FovIndex == 0 ? _slot.DefaultFov : _slot.OtherFovs[_slot.FovIndex - 1], _electronicFov);
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

			internal static bool Showing
			{
				get
				{
					var active = CameraSlot.ActiveInstance;
					return active != null && Slots.Contains(active);
				}
			}

			internal void Init(CameraSlot slot)
			{
				_slot = slot;
				Slots.RemoveWhere(t => t == null);
				Slots.Add(slot);
				slot.ActiveChanged += OnActive;
			}

			void OnDestroy()
			{
				if (_slot != null)
				{
					_slot.ActiveChanged -= OnActive;
					Slots.Remove(_slot);
				}
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
				var spriteManager = Sprites();
				if (spriteManager?.Sprites == null) return;
				foreach (var sprite in spriteManager.Sprites)
					if (sprite.SpriteObject != null && sprite.SpriteObject.activeSelf) sprite.SpriteObject.SetActive(false);
			}

			void OnActive(bool active)
			{
				if (active) return;
				var spriteManager = Sprites();
				if (spriteManager == null) return;
				var next = CameraSlot.ActiveInstance;
				spriteManager.SetActiveSprite(
					next != null && next != _slot ? next.SpriteType : CameraSpriteManager.SpriteType.DefaultScope,
					forced: true);
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
					if (_compass == null)
					{
						_compass = FindObjectOfType<GHPC.UI.Hud.Compass>(true);
						_saved = -1;
					}
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
					if (view == null
						|| CameraSlot.ActiveInstance != view
						|| view.IsExterior
						|| view.AllowFreeLook
						|| view.IsLinkedNightSight
						|| !view.NightSightNullOrInvalid)
						return;
					var input = __instance;
					if (!input.IsInitialized
						|| !input.AllowPlayerStrictLiveInput
						|| !input.PlayerCanControlGunnerNow
						|| input.CurrentPlayerCrewBrain == null)
						return;
					if (FullControl != null
						&& Position != null
						&& !(bool)FullControl.GetValue(input)
						&& (CrewPosition)Position.GetValue(input) != CrewPosition.Gunner)
						return;
					if (!GHPC.Utility.InputUtil.MainPlayer.GetButtonDown("Toggle Night Sight")) return;
					Toggle(input, view);
				}
				catch (Exception exception) { PatchGuard.Failed("Night sight with the day sight destroyed", exception); }
			}

			internal static bool Toggle(PlayerInput p, CameraSlot view)
			{
				var fcs = p.CurrentPlayerCrewBrain?.WeaponsModule?.ActiveWeapon?.FCS;
				if (fcs == null || p.CurrentPlayerUnit == null) return false;
				foreach (var withoutDay in p.CurrentPlayerUnit.GetComponentsInChildren<WithoutDaySight>(true))
				{
					var night = withoutDay.Slot;
					var day = night != null ? night.LinkedDaySight : null;
					if (day == null || !day.Damaged || day.PairedOptic?.FCS != fcs || day.NightSightNullOrInvalid || !night.IsAvailable)
						continue;
					var cam = CameraManager.Instance;
					if (cam == null) return false;
					day.WasUsingNightMode = true;
					cam.TempAttachToSlot(night);
					if (CameraSlot.ActiveInstance == night)
					{
						_from = view;
						_to = night;
					}
					return true;
				}
				return false;
			}

			// ResetTempAttach goes back by slot list index, and the dead sight's fallback isn't in the list.
			static CameraSlot _from, _to;
			static readonly MethodInfo AttachDirect = Reflect.Method(
				typeof(CameraManager),
				"AttachCamToSlotDirect",
				new[] { typeof(CameraSlot) });

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
						if (from == null
							|| AttachDirect == null
							|| CameraSlot.ActiveInstance == null
							|| CameraSlot.ActiveInstance != _to
							|| !from.IsAvailable)
							return true;
						AttachDirect.Invoke(__instance, new object[] { from });
						return false;
					}
					catch (Exception exception)
					{
						PatchGuard.Failed("Night sight off with the day sight destroyed", exception);
						return true;
					}
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
				var slot = prefab
					.GetComponentsInChildren<CameraSlot>(true)
					.FirstOrDefault(c => c.VisionType == NightVisionType.Thermal);
				var optic = slot == null
					? null
					: prefab.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(c => c.slot == slot);
				if (slot == null || optic == null || slot.FLIRBlitMaterialOverride == null)
				{
					DonorParts.WarnOnce("thermal", $"{tag}: M60A3 TTS has no thermal slot / optic / blit material.");
					return null;
				}
				var copy = DonorParts.Copy(
					prefab,
					$"{tag} thermal donor ({prefab.name})",
					slot.gameObject)?.GetComponentInChildren<CameraSlot>(true);
				if (copy == null)
				{
					DonorParts.WarnOnce("thermal", $"{tag}: M60A3 TTS thermal slot could not be copied.");
					return null;
				}
				var donor = new Donor { Slot = copy };
				donor.Blit = GreyMonitor(slot.FLIRBlitMaterialOverride, tag);
				if (optic.post != null)
				{
					donor.MainCam = optic.post.sharedProfile;
					donor.MainCamPriority = optic.post.priority;
				}
				var extra = optic.postAdditional?.FirstOrDefault(v => v != null);
				if (extra != null)
				{
					donor.EarlyGen = extra.sharedProfile;
					donor.EarlyGenPriority = extra.priority;
				}
				donor.Crt = copy.GetComponentsInChildren<CrtError>(true).FirstOrDefault()?.gameObject;
				_cached = donor;
				return donor;
			}

			readonly Dictionary<(bool, bool), Material> _blits =
				new Dictionary<(bool, bool), Material>();
			PostProcessProfile _earlyGenClean;

			internal PostProcessProfile EarlyGenClean(string tag)
			{
				if (_earlyGenClean != null) return _earlyGenClean;
				var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
				profile.name = EarlyGen.name + " (no grain, no bloom)";
				var kept = new List<string>();
				foreach (var effect in EarlyGen.settings)
				{
					if (effect == null || effect is Grain || effect is Bloom) continue;
					var copy = UnityEngine.Object.Instantiate(effect);
					copy.name = effect.name;
					profile.settings.Add(copy);
					kept.Add(effect.GetType().Name);
				}
				profile.isDirty = true;
				return _earlyGenClean = profile;
			}

			internal Material BlitFor(bool crt, bool monitor, string tag)
			{
				if (crt && !monitor) return Blit;
				if (_blits.TryGetValue((crt, monitor), out var material) && material != null) return material;
				material = new Material(Blit) { name = Blit.name + (crt ? "" : ", clean") + (monitor ? ", no scope edge" : "") };
				if (!crt)
				{
					material.SetFloat("_ScanlineBrightness", 0f);
					material.SetFloat("_WobbleAmp", 0f);
					material.SetTexture("_PixelCookie", Texture2D.whiteTexture);
				}
				if (monitor)
				{
					var clear = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "no scope edge" };
					clear.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
					clear.Apply();
					material.SetTexture("_ScopeEdge", clear);
				}
				return _blits[(crt, monitor)] = material;
			}

			// Grey from the «Сосна-У» figures. The ESSA's own is inferred.
			static Material GreyMonitor(Material green, string tag)
			{
				var grey = new Material(green) { name = "Thermal grey (from " + green.name + ")" };
				var ramp = green.HasProperty("_ColorRamp") ? green.GetTexture("_ColorRamp") : null;
				if (ramp != null) grey.SetTexture("_ColorRamp", GreyRamp(ramp, tag));
				else MelonLogger.Warning($"{tag}: donor blit material has no _ColorRamp; the thermal image stays {green.name}.");
				return grey;
			}

			static Texture2D GreyRamp(Texture src, string tag)
			{
				bool srgb = src.isDataSRGB;
				var renderTexture = RenderTexture.GetTemporary(
					src.width,
					src.height,
					0,
					RenderTextureFormat.ARGB32,
					srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
				Graphics.Blit(src, renderTexture);
				var prev = RenderTexture.active;
				RenderTexture.active = renderTexture;
				var tex = new Texture2D(
					src.width,
					src.height,
					TextureFormat.RGBA32,
					false,
					!srgb) { name = "Thermal grey ramp", wrapMode = src.wrapMode, filterMode = src.filterMode };
				tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
				RenderTexture.active = prev;
				RenderTexture.ReleaseTemporary(renderTexture);
				var pixels = tex.GetPixels();
				for (int i = 0; i < pixels.Length; i++)
				{
					float luminance = 0.2126f * pixels[i].r + 0.7152f * pixels[i].g + 0.0722f * pixels[i].b;
					pixels[i] = new Color(luminance, luminance, luminance, pixels[i].a);
				}
				tex.SetPixels(pixels);
				tex.Apply(false, false);
				return tex;
			}
		}
	}
}
