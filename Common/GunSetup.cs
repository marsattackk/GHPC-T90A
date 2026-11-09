using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class GunSetup
	{
		internal sealed class Settings
		{
			public string Tag = "Port";
			public float TurretSpeed, GunSpeed;
			public Vector2? GunLimits;
			public string DepressionProfile;
			public float AutoloaderCycle;
			public string GunName, CodexName;
			public float CodexCaliberMm;
			public string TurretPlatform;
			public string GunPlatform;
			public bool TurretUnstabilised;
		}

		static readonly Dictionary<string, List<KeyValuePair<float, float>>> _profiles = new Dictionary<string, List<KeyValuePair<float, float>>>();
		static WeaponSystemCodexScriptable _codex;

		internal static void Apply(Vehicle vehicle, Settings s)
		{
			// GHPC re-parents the script containers under the mesh bones at Start, prefab paths are no use.
			string turretName = s.TurretPlatform ?? "---TURRET SCRIPTS---";
			var traverse = vehicle.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == turretName)?.GetComponent<AimablePlatform>();
			if (traverse == null) MelonLogger.Warning($"{s.Tag}: turret traverse platform not found; the base's traverse speed kept.");
			else
			{
				if (s.TurretSpeed > 0f) traverse.SpeedPowered = s.TurretSpeed;
				if (s.TurretUnstabilised) traverse.Stabilized = false;
			}
			var elevation = s.GunPlatform != null
				? vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p.name == s.GunPlatform)
				: vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p != traverse && p.transform.parent != null && p.transform.parent.name.Contains("MAIN GUN"))
				?? vehicle.GetComponentsInChildren<AimablePlatform>(true).FirstOrDefault(p => p != traverse);
			if (elevation == null) MelonLogger.Warning($"{s.Tag}: gun elevation platform not found; the base's gun limits kept.");
			else
			{
				if (s.GunSpeed > 0f) elevation.SpeedPowered = s.GunSpeed;
				if (s.GunLimits.HasValue) elevation.LocalEulerLimits = s.GunLimits.Value;
				if (traverse != null && s.DepressionProfile != null) DepressionProfile(elevation, traverse, s);
			}
			Autoloader(vehicle, s);
			GunName(vehicle, s);
		}

		static void DepressionProfile(AimablePlatform gun, AimablePlatform traverse, Settings s)
		{
			if (!_profiles.TryGetValue(s.DepressionProfile, out var knots))
				using (var st = typeof(GunSetup).Assembly.GetManifestResourceStream(s.DepressionProfile))
				{
					if (st == null) { MelonLogger.Warning($"{s.Tag}: gun depression profile missing from the DLL; the gun keeps {gun.LocalEulerLimits.x:0.#} deg at every turret angle."); return; }
					using (var r = new System.IO.StreamReader(st)) _profiles[s.DepressionProfile] = knots = TraverseLimits.Parse(r.ReadToEnd());
				}
			TraverseLimits.Apply(gun, traverse, knots);
		}

		// One stage list per gun, no per-type timing. The first stage waits for the carousel, so switches run longer anyway.
		static void Autoloader(Vehicle vehicle, Settings s)
		{
			if (s.AutoloaderCycle <= 0f) return;
			var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0].Weapon?.Feed;
			if (feed?.ClipReloadStages == null) return;
			var dur = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
			float total = feed.ClipReloadStages.Sum(st => (float)dur.GetValue(st));
			if (total <= 0f) return;
			float k = s.AutoloaderCycle / total;
			foreach (var st in feed.ClipReloadStages) dur.SetValue(st, (float)dur.GetValue(st) * k);
			Reflect.Method(typeof(AmmoFeed), "ResetLiveDurations")?.Invoke(feed, null);
			Reflect.Method(typeof(AmmoFeed), "CalculateTotalTime")?.Invoke(feed, new object[] { feed.ClipReloadStages, false });
		}

		// Every T-72M1 shares the codex entry, rename a copy or they all get the new gun name.
		static void GunName(Vehicle vehicle, Settings s)
		{
			if (s.GunName == null) return;
			var wm = vehicle.GetComponent<WeaponsManager>();
			if (wm == null || wm.Weapons.Length == 0) return;
			wm.Weapons[0].Name = s.GunName;
			var gun = wm.Weapons[0].Weapon;
			var codexField = Reflect.Field(typeof(WeaponSystem), "CodexEntry");
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
	}
}
