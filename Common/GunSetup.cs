using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class GunSetup
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Tag = "Port";
			public float TurretSpeed, GunSpeed;
			public Vector2? GunLimits;
			public string DepressionProfile;
			public float AutoloaderCycle;
			public float TypeChangeCycle;
			public AmmoFeed.DoctrinalReloadMode? ReloadMode;
			public string GunName, CodexName;
			public float CodexCaliberMm;
			public string TurretPlatform;
			public string GunPlatform;
			public bool TurretUnstabilised;
			public float TurretInertia = -1f, GunInertia = -1f;
			public float Deviation;
			public string ShotEvent;
		}
#pragma warning restore CS0649

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Recoil
		{
			public string Tag = "Port";
			public string Bone;
			public string Script;
			public string Barrel;
			public float Travel;
		}
#pragma warning restore CS0649

		internal static bool BarrelRecoil(Vehicle vehicle, Recoil r)
		{
			var gunBone = RigBones.Gun(vehicle.transform);
			var bone = vehicle.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == r.Bone);
			var barrel = gunBone != null ? gunBone.Find(r.Barrel) : null;
			var script = vehicle.GetComponentsInChildren<AnimatedPart>(true).FirstOrDefault(p => p.name == r.Script);
			var gun =
				vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault(w => w.Role == WeaponSystemRole.MainGun)?.Weapon;
			if (gunBone == null
				|| bone == null
				|| barrel == null
				|| script == null
				|| gun == null
				|| script.StartTransform == null
				|| script.EndTransform == null
				|| script.Transform != bone)
			{
				MelonLogger.Warning($"{r.Tag}: the barrel's recoil was not set up (gun bone {gunBone != null}, recoil bone {bone != null}, barrel {barrel != null}, script {script != null}, gun {gun != null}); the barrel stays fixed.");
				return false;
			}
			barrel.SetParent(bone, true);
			var muzzle = gun.MuzzleIdentity;
			script.StartTransform.SetParent(gunBone, true);
			script.EndTransform.SetParent(gunBone, true);
			script.StartTransform.SetPositionAndRotation(bone.position, bone.rotation);
			script.EndTransform.SetPositionAndRotation(bone.position - muzzle.forward * r.Travel, bone.rotation);
			return true;
		}

		static readonly Dictionary<string, List<KeyValuePair<float, float>>> _profiles =
			new Dictionary<string, List<KeyValuePair<float, float>>>();
		static WeaponSystemCodexScriptable _codex;

		internal static void Apply(Vehicle vehicle, Settings s)
		{
			// GHPC re-parents the script containers under the mesh bones at Start, prefab paths are no use.
			string turretName = s.TurretPlatform ?? "---TURRET SCRIPTS---";
			// GHPC re-parents the script containers under the mesh bones at Start. Prefab paths are no use.
			var traverse = vehicle
				.GetComponentsInChildren<Transform>(true)
				.FirstOrDefault(c => c.name == turretName)?.GetComponent<AimablePlatform>();
			if (traverse == null) MelonLogger.Warning($"{s.Tag}: turret traverse platform not found; the base's traverse speed kept.");
			else
			{
				if (s.TurretSpeed > 0f) traverse.SpeedPowered = s.TurretSpeed;
				if (s.TurretUnstabilised) traverse.Stabilized = false;
				if (s.TurretInertia >= 0f) traverse.InertiaRatio = s.TurretInertia;
			}
			var elevation = s.GunPlatform != null
				? vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p.name == s.GunPlatform)
				: vehicle
					.GetComponentsInChildren<AimablePlatform>(true)
					.FirstOrDefault(p => p != traverse
						&& p.transform.parent != null
						&& p.transform.parent.name.Contains("MAIN GUN"))
				?? vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p != traverse);
			if (elevation == null) MelonLogger.Warning($"{s.Tag}: gun elevation platform not found; the base's gun limits kept.");
			else
			{
				if (s.GunSpeed > 0f) elevation.SpeedPowered = s.GunSpeed;
				if (s.GunLimits.HasValue) elevation.LocalEulerLimits = s.GunLimits.Value;
				if (s.GunInertia >= 0f) elevation.InertiaRatio = s.GunInertia;
				if (traverse != null && s.DepressionProfile != null) DepressionProfile(elevation, traverse, s);
			}
			Autoloader(vehicle, s);
			if (s.ReloadMode.HasValue)
			{
				var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0].Weapon?.Feed;
				if (feed == null) MelonLogger.Warning($"{s.Tag}: main gun feed not found; the base's reload mode kept.");
				else feed.ReloadMode = s.ReloadMode.Value;
			}
			GunName(vehicle, s);
			var main =
				vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault(w => w.Role == WeaponSystemRole.MainGun)?.Weapon;
			if (main != null && s.Deviation > 0f) main.BaseDeviationAngle = s.Deviation;
			if (s.ShotEvent != null)
			{
				if (main?.WeaponSound == null)
					MelonLogger.Warning($"{s.Tag}: main gun sound emitter not found; the base's shot sound kept.");
				else main.WeaponSound.SingleShotEventPaths = new[] { s.ShotEvent };
			}
		}

		static void DepressionProfile(AimablePlatform gun, AimablePlatform traverse, Settings s)
		{
			if (!_profiles.TryGetValue(s.DepressionProfile, out var knots))
				using (var stream = typeof(GunSetup).Assembly.GetManifestResourceStream(s.DepressionProfile))
				{
					if (stream == null)
					{
						MelonLogger.Warning($"{s.Tag}: gun depression profile missing from the DLL; the gun keeps {gun.LocalEulerLimits.x:0.#} deg at every turret angle.");
						return;
					}
					using (var reader = new System.IO.StreamReader(stream))
						_profiles[s.DepressionProfile] = knots = TraverseLimits.Parse(reader.ReadToEnd());
				}
			TraverseLimits.Apply(gun, traverse, s.Tag + " gun", knots);
		}

		// One stage list per gun, no per-type timing. The first stage waits for the carousel, so switches run longer anyway.
		static void Autoloader(Vehicle vehicle, Settings s)
		{
			if (s.AutoloaderCycle <= 0f) return;
			var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0].Weapon?.Feed;
			if (feed?.ClipReloadStages == null) return;
			var dur = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
			if (dur == null) return;
			float total = feed.ClipReloadStages.Sum(x => (float)dur.GetValue(x));
			if (total <= 0f) return;
			float k = s.AutoloaderCycle / total;
			foreach (var stage in feed.ClipReloadStages) dur.SetValue(stage, (float)dur.GetValue(stage) * k);
			Reflect.Method(typeof(AmmoFeed), "ResetLiveDurations")?.Invoke(feed, null);
			Reflect.Method(typeof(AmmoFeed), "CalculateTotalTime")?.Invoke(feed, new object[] { feed.ClipReloadStages, false });
			if (s.TypeChangeCycle > 0f)
			{
				var reloadByType = feed.GetComponent<ReloadByType>() ?? feed.gameObject.AddComponent<ReloadByType>();
				reloadByType.Tag = s.Tag;
				reloadByType.Same = s.AutoloaderCycle;
				reloadByType.Change = s.TypeChangeCycle;
			}
		}

		internal sealed class ReloadByType : MonoBehaviour
		{
			public string Tag;
			public float Same, Change;
		}

		static readonly FieldInfo LockedIn = Reflect.Field(typeof(AmmoFeed), "_queuedClipTypeLockedIn");
		static readonly FieldInfo StageDuration = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
		static readonly FieldInfo CarouselGoal = Reflect.Field(typeof(AmmoCarousel), "_goalLocalRotation");
		static readonly MethodInfo TotalTime = Reflect.Method(typeof(AmmoFeed), "CalculateTotalTime");

		// The carousel goes straight to its slot: the source's cycle already holds the conveyor's search, travel would count twice.
		[HarmonyPatch(typeof(AmmoFeed), nameof(AmmoFeed.FeedNewClip))]
		static class ReloadByTypePatch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(AmmoFeed __instance, out bool __state) => __state = __instance.Reloading;

			static void Postfix(AmmoFeed __instance, bool __state)
			{
				if (LockedIn == null || StageDuration == null || TotalTime == null) return;
				if (__state || !__instance.Reloading) return;
				var reloadByType = __instance.GetComponent<ReloadByType>();
				if (reloadByType == null || __instance.ClipReloadStages == null) return;
				var queued = LockedIn.GetValue(__instance) as AmmoType.AmmoClip;
				bool same = __instance.LoadedClipType != null && queued != null && __instance.LoadedClipType.Equals(queued);
				float target = same ? reloadByType.Same : reloadByType.Change;
				var car = __instance.Carousel;
				if (car != null && CarouselGoal != null && car.IsRotatingNow)
				{
					var angles = car.RotationTransform.localEulerAngles;
					car.RotationTransform.localEulerAngles = new Vector3(angles.x, (float)CarouselGoal.GetValue(car), angles.z);
					car.KillAnimation();
				}
				var stages = __instance.ClipReloadStages;
				float work = stages.Where(s => !s.WaitForCarousel).Sum(x => (float)StageDuration.GetValue(x));
				if (work <= 0f) return;
				float foreign = ReloadRules.ForeignFactor(
					stages.Where(s => !s.WaitForCarousel).Sum(x => x.LiveDuration),
					work);
				foreach (var stage in stages)
					stage.LiveDuration = stage.WaitForCarousel ? 0f : (float)StageDuration.GetValue(stage) * target * foreign / work;
				TotalTime.Invoke(__instance, new object[] { stages, false });
			}
		}

		// Every T-72M1 shares the codex entry. Rename a copy or they all get the new gun name.
		static void GunName(Vehicle vehicle, Settings s)
		{
			if (s.GunName == null) return;
			var weaponsManager = vehicle.GetComponent<WeaponsManager>();
			if (weaponsManager == null || weaponsManager.Weapons.Length == 0) return;
			weaponsManager.Weapons[0].Name = s.GunName;
			var gun = weaponsManager.Weapons[0].Weapon;
			var codexField = Reflect.Field(typeof(WeaponSystem), "CodexEntry");
			if (codexField == null) return;
			var codex = gun != null ? (WeaponSystemCodexScriptable)codexField.GetValue(gun) : null;
			if (codex == null || codex == _codex) return;
			if (_codex == null)
			{
				_codex = Object.Instantiate(codex);
				_codex.name = s.CodexName ?? s.GunName;
				_codex.FriendlyName = s.GunName;
				if (s.CodexCaliberMm > 0f) _codex.CaliberMm = s.CodexCaliberMm;
			}
			codexField.SetValue(gun, _codex);
		}

		static string Path(Transform t, Transform root)
		{
			string path = t.name;
			for (var node = t.parent; node != null && node != root; node = node.parent) path = node.name + "/" + path;
			return path;
		}
	}
}
