using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weaponry;
using MelonLoader;
using Reticle;
using UnityEngine;
using ThermalSight = T90A.Common.ThermalSight;
using T90A.Common;

namespace T90A
{
	internal static class NightSight
	{
		internal const string Essa = "ESSA", BuranM = "Buran-M";

		const float EssaWide = 6.75f, EssaNarrow = 2.25f, EssaElectronic = 1.125f;
		const int EssaHeight = 576;
		// 754 across a 4:3 picture, stretched over a 16:9 screen.
		const int EssaWidth = 1005;
		static readonly Vector2 EssaElevationLimits = new Vector2(-10f, 20f);
		const float EssaAzimuth = 7.5f;
		static readonly Vector2 BuranElevationLimits = new Vector2(-10f, 20f);

		static readonly FieldInfo ReticleSmr = Reflect.Field(typeof(ReticleMesh), "SMR");

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
			if (choice == Essa)
			{
				GunnerFcs.AimClamp.SightAzimuth(night, EssaAzimuth);
			}
			night.gameObject.AddComponent<Marker>();
			// VisibilityManager goes off the unit's night vision type for AI spotting, not the slot's.
			ThermalSight.SetUnitNightVision(vehicle, slot);
		}

		internal class Marker : MonoBehaviour { }

		static bool ApplyEssa(UsableOptic day, UsableOptic night) =>
			ThermalSight.Apply(day, night, new ThermalSight.Settings
			{
				Tag = "T-90A",
				Name = Essa,
				WideFov = EssaWide,
				OtherFovs = T90AMod.EssaElectronicZoom.Value ? new[] { EssaNarrow, EssaElectronic } : new[] { EssaNarrow },
				ElectronicFov = EssaElectronic,
				Width = EssaWidth,
				Height = EssaHeight,
				ElevationLimits = EssaElevationLimits,
				Reticle = EssaMarks,
				Readout = MonitorEdges,
				Monitor = true,
				Crt = true,
				ScanStreaks = EssaScanStreaks,
				ScanRows = EssaHeight / 2,
				ScanDriftSeconds = 12f,
			});

		const float EssaScanStreaks = 0.27f;

		// GHPC won't draw a dark reticle over a thermal picture, so the cross is a screen item.
		static void EssaMarks(UsableOptic o)
		{
			var narrowSpec = Reticles.Spec("essa_narrow.jsonc");
			if (o.transform.Find(narrowSpec.ScreenName) != null) return;
			if (o.reticleMesh != null) o.reticleMesh.gameObject.SetActive(false);
			var problems = new List<string>();
			var wideSpec = Reticles.Spec("essa_wide.jsonc");
			var rowSpec = Reticles.Spec("essa_row.jsonc");
			var wide = ReticleBuild.BuildOverlay(wideSpec, o.transform, (float)wideSpec.Fov, problems);
			var narrow = ReticleBuild.BuildOverlay(narrowSpec, o.transform, (float)narrowSpec.Fov, problems);
			var row = ReticleBuild.BuildOverlay(rowSpec, o.transform, (float)rowSpec.Fov, problems);
			string mono = rowSpec.Num("Mono").ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
			SightReadout.Attach(o, row, new SightReadout.Settings
			{
				Range = "range", RangePrefix = $"<mspace={mono}em>", RangeSuffix = "</mspace>", RangeQuantize = 5,
				Ready = "ready",
				Ammo = "ammo", AmmoText = SightReadout.Cyrillic,
			}, problems);
			string lacks = ReticleBuild.Missing(row.Readouts.TryGetValue("ammo", out var t) ? t.font : null, "БОКУП");
			if (lacks.Length > 0) problems.Add($"readout font lacks '{lacks}'");
			ZoomReticles.Attach(o, null, new[] { new[] { wide.Root }, new[] { narrow.Root, row.Root }, new[] { narrow.Root, row.Root } });
			foreach (var p in problems) MelonLogger.Warning($"T-90A ESSA marks: {p}");
		}

		static void MonitorEdges(UsableOptic o)
		{
			var spec = Reticles.Spec("essa_monitor.jsonc");
			if (o.transform.Find(spec.ScreenName) != null) return;
			var problems = new List<string>();
			ReticleBuild.BuildOverlay(spec, o.transform, (float)spec.Fov, problems);
			foreach (var p in problems) MelonLogger.Warning($"T-90A ESSA monitor: {p}");
		}

		static bool ApplyBuranM(Vehicle vehicle, UsableOptic day, UsableOptic night)
		{
			var dOptic = BuranDonor();
			var dSlot = dOptic?.slot;
			if (dSlot == null) { MelonLogger.Warning("T-90A: T-80B TPN-3-49 donor not found; Buran-M not applied (TPN-1-49-23 kept)."); return false; }
			var slot = night.slot;

			// Nothing on the Buran-M's field or look, the T-80B's TPN-3-49 stands in.
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

			if (day.FCS != null) night.gameObject.AddComponent<ThermalSight.GateFollowsSight>().Init(day.FCS, slot);
			GunnerFcs.AimClamp.Register(day.FCS, night);

			if (night.post != null && dOptic.post != null)
			{
				night.post.sharedProfile = dOptic.post.sharedProfile;
				night.post.priority = dOptic.post.priority;
				night.post.weight = dOptic.post.weight;
			}

			if (night.reticleMesh != null && dOptic.reticleMesh != null && dOptic.reticleMesh.reticleSO != null)
			{
				var apfsds = BuranApfsds(vehicle, day);
				string key = Sight1G46.RoundsKey(apfsds);
				if (!_buranReticles.TryGetValue(key, out var so) || so == null) _buranReticles[key] = so = BuranReticle(day, dOptic.reticleMesh.reticleSO, apfsds);
				night.reticleMesh.reticleSO = so;
				night.reticleMesh.nightBrightness = dOptic.reticleMesh.nightBrightness;
				ReticleSmr.SetValue(night.reticleMesh, null);
				night.reticleMesh.Load();
			}
			return true;
		}

		static readonly Dictionary<string, ReticleSO> _buranReticles = new Dictionary<string, ReticleSO>();

		static AmmoCodexScriptable BuranApfsds(Vehicle vehicle, UsableOptic day)
		{
			var clips = vehicle.GetComponent<GHPC.Weapons.LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
			AmmoCodexScriptable apfsds = clips != null && clips.Length > 0 && clips[0] != null ? clips[0].ClipType.MinimalPattern[0] : null;
			return apfsds ?? Sight1G46.FindAmmo(Sight1G46.ApfsdsAmmo, day.reticleMesh.reticleSO, "3BM");
		}

		static ReticleSO BuranReticle(UsableOptic day, ReticleSO tpn3, AmmoCodexScriptable apfsds)
		{
			// Tree's [SerializeReference], so this is a real deep copy.
			var so = Object.Instantiate(tpn3);
			so.name = "TPN3 Buran-M (T-90A)";
			var host = day.reticleMesh.reticleSO;
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

		internal static UsableOptic BuranDonor()
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
