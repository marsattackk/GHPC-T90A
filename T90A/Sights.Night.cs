using System.Collections.Generic;
using System.Linq;
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
	// ESSA thermal or Buran-M intensifier, whichever the setting picks.
	internal static partial class NightSight
	{
		internal const string Essa = "ESSA";
		internal const string BuranM = "Buran-M";

		// Fields 9 x 6.75 and 3 x 2.25 deg, 1.5 x 1.125 with the electronic zoom (the ESSA's technical description,
		// odetievbrony.ru). The Catherine FC camera gives 576 lines (Russian Wikipedia, «Катрин-ФС»).
		const float EssaWide = 6.75f;
		const float EssaNarrow = 2.25f;
		const float EssaElectronic = 1.125f;
		const int EssaHeight = 576;
		// 754 across a 4:3 picture, stretched over a 16:9 screen.
		const int EssaWidth = 1005;
		// The ESSA's mirror travels -10 / +20 deg and 7.5 deg either side (the same technical description), and Russian
		// Wikipedia's «Т-90» gives -10 / +20 for both the ESSA and the Buran-M.
		static readonly Vector2 EssaElevationLimits = new Vector2(-10f, 20f);
		const float EssaAzimuth = 7.5f;
		static readonly Vector2 BuranElevationLimits = new Vector2(-10f, 20f);

		static bool _unknownWarned;

		internal static string Choice
		{
			get
			{
				string value = (T90AMod.NightSightChoice.Value ?? "").Trim();
				if (string.Equals(value, BuranM, System.StringComparison.OrdinalIgnoreCase)) return BuranM;
				if (!_unknownWarned && !string.Equals(value, Essa, System.StringComparison.OrdinalIgnoreCase))
				{
					_unknownWarned = true;
					MelonLogger.Warning($"T-90A: Gunner night sight \"{value}\" is neither {Essa} nor {BuranM}; fitting the {Essa}.");
				}
				return Essa;
			}
		}

		internal static void Apply(Vehicle vehicle, UsableOptic day)
		{
			string choice = Choice;
			var slot = day.slot.LinkedNightSight;
			var night = slot == null
				? null
				: slot.PairedOptic
					?? vehicle.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(c => c.slot == slot);
			if (night == null)
			{
				MelonLogger.Warning("T-90A: no gunner night sight on the base vehicle; night sight not changed.");
				return;
			}
			if (night.GetComponent<Marker>() != null) return;

			bool applied = choice == BuranM ? ApplyBuranM(vehicle, day, night) : ApplyEssa(day, night);
			if (!applied) return;
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
				// One streak per detector: the camera scans a 288 x 4 array (glavcom.blogspot.com, «Технологии в
				// танкостроении: СУО, прицелы»). Their strength and the 12 s they take to wander are set by eye.
				ScanStreaks = EssaScanStreaks,
				ScanRows = EssaHeight / 2,
				ScanDriftSeconds = 12f,
			});

		const float EssaScanStreaks = 0.27f;   // set by eye

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
				Range = "range",
				RangePrefix = $"<mspace={mono}em>",
				RangeSuffix = "</mspace>",
				RangeQuantize = 5,   // 5 m steps, as the T-80B's 1G42 readout
				Ready = "ready",
				Ammo = "ammo",
				AmmoText = SightReadout.Cyrillic,
			}, problems);
			string lacks = ReticleBuild.Missing(
				row.Readouts.TryGetValue("ammo", out var readout) ? readout.font : null,
				"БОКУП");
			if (lacks.Length > 0) problems.Add($"readout font lacks '{lacks}'");
			ZoomReticles.Attach(
				o,
				null,
				new[] { new[] { wide.Root }, new[] { narrow.Root, row.Root }, new[] { narrow.Root, row.Root } });
			foreach (var problem in problems) MelonLogger.Warning($"T-90A ESSA marks: {problem}");
		}

		static void MonitorEdges(UsableOptic o)
		{
			var spec = Reticles.Spec("essa_monitor.jsonc");
			if (o.transform.Find(spec.ScreenName) != null) return;
			var problems = new List<string>();
			ReticleBuild.BuildOverlay(spec, o.transform, (float)spec.Fov, problems);
			foreach (var problem in problems) MelonLogger.Warning($"T-90A ESSA monitor: {problem}");
		}

		static bool ApplyBuranM(Vehicle vehicle, UsableOptic day, UsableOptic night)
		{
			var dOptic = BuranDonor();
			var dSlot = dOptic?.slot;
			if (dSlot == null)
			{
				MelonLogger.Warning("T-90A: T-80B TPN-3-49 donor not found; Buran-M not applied (TPN-1-49-23 kept).");
				return false;
			}
			var slot = night.slot;

			// Nothing on the Buran-M's field or look. The T-80B's TPN-3-49 stands in.
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
				if (!_buranReticles.TryGetValue(key, out var reticle) || reticle == null)
					_buranReticles[key] = reticle = BuranReticle(day, dOptic.reticleMesh.reticleSO, apfsds);
				night.reticleMesh.reticleSO = reticle;
				night.reticleMesh.nightBrightness = dOptic.reticleMesh.nightBrightness;
				ReticleMeshes.ClearSmr(night.reticleMesh);
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
			var reticle = Object.Instantiate(tpn3);
			reticle.name = "TPN3 Buran-M (T-90A)";
			var host = day.reticleMesh.reticleSO;
			var pkt = Sight1G46.FindAmmo(Sight1G46.CoaxAmmo, host, "762");
			var stack = new Stack<ReticleTree.TransformElement>(reticle.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var element = stack.Pop();
				if (element is ReticleTree.VerticalBallistic ballistic && ballistic.projectile != null)
				{
					var target = ballistic.projectile.name.Contains("762")
						? pkt
						: ballistic.projectile.name.Contains("3BM") ? apfsds : null;
					if (target != null)
					{
						ballistic.projectile = target;
						ballistic.UpdateBC();
					}
				}
				if (element is ReticleTree.GroupBase container) foreach (var child in container.elements) stack.Push(child);
			}
			return reticle;
		}

		static UsableOptic _buranDonor;

		internal static UsableOptic BuranDonor()
		{
			if (_buranDonor != null && _buranDonor.slot != null) return _buranDonor;
			_buranDonor = null;
			var prefab = DonorParts.Prefab("T80B");
			var dSlot = prefab == null
				? null
				: prefab
					.GetComponentsInChildren<CameraSlot>(true)
					.Select(s => s.LinkedNightSight)
					.FirstOrDefault(n => n != null && !n.IsExterior);
			var dOptic = dSlot == null
				? null
				: prefab.GetComponentsInChildren<UsableOptic>(true).FirstOrDefault(c => c.slot == dSlot);
			if (dOptic == null) return null;
			var copy = DonorParts.Copy(
				prefab,
				"T-90A Buran-M donor (" + prefab.name + ")",
				dOptic.gameObject)?.GetComponentInChildren<UsableOptic>(true);
			if (copy?.slot == null)
			{
				DonorParts.WarnOnce(
					"Buran-M",
					"T-90A: T-80B TPN-3-49 could not be copied (its slot is not on the optic's object).");
				return null;
			}
			_buranDonor = copy;
			return copy;
		}
	}
}
