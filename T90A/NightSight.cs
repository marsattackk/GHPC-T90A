using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Shaders.CRT;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using MelonLoader;
using Reticle;
using T90A.Common;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace T90A
{
	internal static class NightSight
	{
		internal const string Essa = "ESSA", BuranM = "Buran-M";

		const float EssaWide = 6.75f, EssaNarrow = 2.25f, EssaElectronic = 1.125f;
		const int EssaHeight = 288;
		const int EssaWidth = 512;
		static readonly Vector2 EssaElevationLimits = new Vector2(-10f, 20f);
		static readonly Vector2 BuranElevationLimits = new Vector2(-10f, 20f);

		static readonly FieldInfo UnitNightVision = typeof(GHPC.Unit).GetField("_nightVisionType", BindingFlags.NonPublic | BindingFlags.Instance);
		static readonly FieldInfo ReticleSmr = typeof(ReticleMesh).GetField("SMR", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static string Choice => T90AMod.NightSightChoice.Value == BuranM ? BuranM : Essa;

		internal static void Apply(Vehicle vehicle, UsableOptic day)
		{
			string choice = Choice;
			var slot = day.slot.LinkedNightSight;
			var night = slot == null ? null : slot.PairedOptic ?? vehicle.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.slot == slot);
			if (night == null) { MelonLogger.Warning("T-90A: no gunner night sight on the base vehicle; night sight not changed."); return; }
			if (night.GetComponent<Marker>() != null) return;

			bool ok = choice == BuranM ? ApplyBuranM(vehicle, day, night) : ApplyEssa(day, night);
			if (!ok) return;
			night.gameObject.AddComponent<Marker>();
			UnitNightVision?.SetValue(vehicle, slot.VisionType);
		}

		internal class Marker : MonoBehaviour { }

		static bool ApplyEssa(UsableOptic day, UsableOptic night)
		{
			var d = EssaDonor.Get();
			if (d == null) { MelonLogger.Warning("T-90A: M60A3 TTS thermal donor not found; ESSA not applied (TPN-1-49-23 kept)."); return false; }
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
			night.LocalElevationLimits = EssaElevationLimits;
			night.LocalAzimuthLimits = day.LocalAzimuthLimits;
			night.RegisterAtStart = false;
			slot.ExclusiveItems = daySlot.ExclusiveItems;

			slot.VisionType = NightVisionType.Thermal;
			slot.DefaultFov = EssaWide;
			slot.OtherFovs = T90AMod.EssaElectronicZoom.Value ? new[] { EssaNarrow, EssaElectronic } : new[] { EssaNarrow };
			slot.fovAspect = false;
			slot.AllowFreeZoom = true;
			slot.WrapFovLevels = false;
			slot.OverrideFLIRResolution = true;
			slot.FLIRWidth = EssaWidth;
			slot.FLIRHeight = EssaHeight;
			slot.FLIRFilterMode = FilterMode.Bilinear;
			slot.DoMechanicalFLIR = false;
			slot.FLIRBlitMaterialOverride = d.Blit;
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
			if (night.post == null) night.post = NewVolume(night.transform, "ESSA post", layer);
			night.post.sharedProfile = d.MainCam;
			night.post.priority = d.MainCamPriority;
			night.post.weight = 1f;
			night.post.isGlobal = true;
			if (d.EarlyGen != null)
			{
				var extra = NewVolume(night.post.transform.parent, "ESSA post (FLIR only)", layer);
				extra.sharedProfile = d.EarlyGen;
				extra.priority = d.EarlyGenPriority;
				night.postAdditional = new[] { extra };
			}
			night.AltNightPost = null;

			if (d.Crt != null)
			{
				var crt = Object.Instantiate(d.Crt, slot.transform, false);
				crt.name = d.Crt.name;
				var err = crt.GetComponent<CrtError>();
				if (err != null) slot.ZoomChanged += err.Wipe;
			}

			night.gameObject.AddComponent<EssaResolution>().Init(slot, EssaElectronic);

			if (day.FCS != null) night.gameObject.AddComponent<GateFollowsSight>().Init(day.FCS, slot);
			Fcs1A45T.AimClamp.Register(day.FCS, night);

			if (night.reticleMesh != null)
			{
				night.reticleMesh.reticleSO = _essaReticle ?? (_essaReticle = Sight1G46.BuildEssa());
				night.reticleMesh.nightBrightness = 1f;
				ReticleSmr.SetValue(night.reticleMesh, null);
				night.reticleMesh.Load();
				ReticleWidth.Attach(night);
			}

			Fcs1A45T.Readout.Build(night);
			return true;
		}

		static ReticleSO _essaReticle;

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

		internal class EssaResolution : MonoBehaviour
		{
			CameraSlot _slot;
			float _electronicFov;

			internal void Init(CameraSlot slot, float electronicFov)
			{
				_slot = slot;
				_electronicFov = electronicFov;
				slot.ZoomChanged += Refresh;
				Refresh();
			}

			void OnDestroy()
			{
				if (_slot != null) _slot.ZoomChanged -= Refresh;
			}

			void Refresh()
			{
				bool electronic = Mathf.Approximately(_slot.FovIndex == 0 ? _slot.DefaultFov : _slot.OtherFovs[_slot.FovIndex - 1], _electronicFov);
				_slot.FLIRWidth = electronic ? EssaWidth / 2 : EssaWidth;
				_slot.FLIRHeight = electronic ? EssaHeight / 2 : EssaHeight;
				var flir = GHPC.Thermals.FLIRCamera.Instance;
				if (_slot.IsActive && flir != null && flir.enabled)
				{
					flir.WidthOverride = _slot.FLIRWidth;
					flir.HeightOverride = _slot.FLIRHeight;
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
				if (active) _fcs.FireGateOverrideTransform = _line;
				else if (_fcs.FireGateOverrideTransform == _line) _fcs.FireGateOverrideTransform = Original;
			}
		}

		sealed class EssaDonor
		{
			internal CameraSlot Slot;
			internal Material Blit;
			internal PostProcessProfile MainCam, EarlyGen;
			internal float MainCamPriority, EarlyGenPriority;
			internal GameObject Crt;

			static EssaDonor _cached;

			internal static EssaDonor Get()
			{
				if (_cached != null && _cached.Slot != null) return _cached;
				_cached = null;
				var prefab = DonorParts.Prefab("M60A3TTS");
				if (prefab == null) return null;
				var slot = prefab.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => s.VisionType == NightVisionType.Thermal);
				var optic = slot == null ? null : prefab.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.slot == slot);
				if (slot == null || optic == null || slot.FLIRBlitMaterialOverride == null) { DonorParts.WarnOnce("ESSA", "T-90A: M60A3 TTS has no thermal slot / optic / blit material."); return null; }
				var copy = DonorParts.Copy(prefab, "T-90A ESSA donor (" + prefab.name + ")", slot.gameObject)?.GetComponentInChildren<CameraSlot>(true);
				if (copy == null) { DonorParts.WarnOnce("ESSA", "T-90A: M60A3 TTS thermal slot could not be copied."); return null; }
				var d = new EssaDonor { Slot = copy };
				d.Blit = GreyMonitor(slot.FLIRBlitMaterialOverride);
				if (optic.post != null) { d.MainCam = optic.post.sharedProfile; d.MainCamPriority = optic.post.priority; }
				var extra = optic.postAdditional?.FirstOrDefault(p => p != null);
				if (extra != null) { d.EarlyGen = extra.sharedProfile; d.EarlyGenPriority = extra.priority; }
				d.Crt = copy.GetComponentsInChildren<CrtError>(true).FirstOrDefault()?.gameObject;
				_cached = d;
				return d;
			}

			static Material GreyMonitor(Material green)
			{
				var m = new Material(green) { name = "ESSA Grey (from " + green.name + ")" };
				var ramp = green.HasProperty("_ColorRamp") ? green.GetTexture("_ColorRamp") : null;
				if (ramp != null) m.SetTexture("_ColorRamp", GreyRamp(ramp));
				else MelonLogger.Warning("T-90A: donor blit material has no _ColorRamp; ESSA image stays " + green.name + ".");
				return m;
			}

			static Texture2D GreyRamp(Texture src)
			{
				bool srgb = src.isDataSRGB;
				var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
				Graphics.Blit(src, rt);
				var prev = RenderTexture.active;
				RenderTexture.active = rt;
				var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, !srgb) { name = "ESSA grey ramp", wrapMode = src.wrapMode, filterMode = src.filterMode };
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

		static bool ApplyBuranM(Vehicle vehicle, UsableOptic day, UsableOptic night)
		{
			var dOptic = BuranDonor();
			var dSlot = dOptic?.slot;
			if (dSlot == null) { MelonLogger.Warning("T-90A: T-80B TPN-3-49 donor not found; Buran-M not applied (TPN-1-49-23 kept)."); return false; }
			var slot = night.slot;

			slot.VisionType = NightVisionType.Intensifier;
			slot.DefaultFov = dSlot.DefaultFov;
			slot.OtherFovs = dSlot.OtherFovs?.ToArray() ?? new float[0];
			slot.AllowFreeZoom = dSlot.AllowFreeZoom;
			slot.fovAspect = dSlot.fovAspect;
			slot.BaseBlur = dSlot.BaseBlur;
			slot.VibrationBlurScale = dSlot.VibrationBlurScale;
			slot.VibrationPreBlur = dSlot.VibrationPreBlur;
			slot.VibrationShakeMultiplier = dSlot.VibrationShakeMultiplier;
			slot.SpriteType = dSlot.SpriteType;

			night.Alignment = day.Alignment;
			night.RotateElevation = day.RotateElevation;
			night.RotateAzimuth = day.RotateAzimuth;
			night.CantCorrect = day.CantCorrect;
			night.CantCorrectMaxSpeed = day.CantCorrectMaxSpeed;
			night.LevelTransform = day.LevelTransform;
			night.ForceHorizontalReticleAlign = day.ForceHorizontalReticleAlign;
			night.LocalElevationLimits = BuranElevationLimits;
			night.LocalAzimuthLimits = day.LocalAzimuthLimits;
			night.RegisterAtStart = false;

			if (day.FCS != null) night.gameObject.AddComponent<GateFollowsSight>().Init(day.FCS, slot);
			Fcs1A45T.AimClamp.Register(day.FCS, night);

			if (night.post != null && dOptic.post != null)
			{
				night.post.sharedProfile = dOptic.post.sharedProfile;
				night.post.priority = dOptic.post.priority;
				night.post.weight = dOptic.post.weight;
			}

			if (night.reticleMesh != null && dOptic.reticleMesh != null && dOptic.reticleMesh.reticleSO != null)
			{
				if (_buranReticle == null) _buranReticle = BuranReticle(vehicle, day, dOptic.reticleMesh.reticleSO);
				night.reticleMesh.reticleSO = _buranReticle;
				night.reticleMesh.nightBrightness = dOptic.reticleMesh.nightBrightness;
				ReticleSmr.SetValue(night.reticleMesh, null);
				night.reticleMesh.Load();
			}
			return true;
		}

		static ReticleSO _buranReticle;

		static ReticleSO BuranReticle(Vehicle vehicle, UsableOptic day, ReticleSO tpn3)
		{
			var so = Object.Instantiate(tpn3);
			so.name = "TPN3 Buran-M (T-90A)";
			var clips = vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
			var host = day.reticleMesh.reticleSO;
			AmmoCodexScriptable apfsds = clips != null && clips.Length > 0 && clips[0] != null ? clips[0].ClipType.MinimalPattern[0] : null;
			apfsds = apfsds ?? Sight1G46.FindAmmo(Sight1G46.ApfsdsAmmo, host, "3BM");
			var pkt = Sight1G46.FindAmmo(Sight1G46.CoaxAmmo, host, "762");
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e is ReticleTree.VerticalBallistic vb && vb.projectile != null)
				{
					var to = vb.projectile.name.Contains("762") ? pkt : vb.projectile.name.Contains("3BM") ? apfsds : null;
					if (to != null) { vb.projectile = to; vb.UpdateBC(); }
				}
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
			return so;
		}

		static UsableOptic _buranDonor;

		static UsableOptic BuranDonor()
		{
			if (_buranDonor != null && _buranDonor.slot != null) return _buranDonor;
			_buranDonor = null;
			var prefab = DonorParts.Prefab("T80B");
			var dSlot = prefab == null ? null : prefab.GetComponentsInChildren<CameraSlot>(true).Select(s => s.LinkedNightSight).FirstOrDefault(n => n != null && !n.IsExterior);
			var dOptic = dSlot == null ? null : prefab.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(o => o.slot == dSlot);
			if (dOptic == null) return null;
			var copy = DonorParts.Copy(prefab, "T-90A Buran-M donor (" + prefab.name + ")", dOptic.gameObject)?.GetComponentInChildren<UsableOptic>(true);
			if (copy?.slot == null) { DonorParts.WarnOnce("Buran-M", "T-90A: T-80B TPN-3-49 could not be copied (its slot is not on the optic's object)."); return null; }
			_buranDonor = copy;
			return copy;
		}
	}
}
