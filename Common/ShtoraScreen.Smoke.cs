using System;
using System.Collections.Generic;
using GHPC.Effects;
using GHPC.Equipment;
using GHPC.Utility;
using GHPC.Weaponry;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	// The 3D17 grenade. I copy the vanilla smoke grenade once, put a white aerosol cloud from a donor tank in it and
	// size it to burst at the fuse range.
	internal static partial class ShtoraScreen
	{
		// CalculateLaunchImpulse wants a flat-ground landing range, so hand it the one whose speed covers R in FuseSeconds.
		static float Speed(float r, Settings s) => r / (Mathf.Cos(s.LaunchAngle * Mathf.Deg2Rad) * s.FuseSeconds);
		internal static float LandingFor(float r, Settings s)
		{
			float speed = Speed(r, s);
			return speed * speed * Mathf.Sin(2f * s.LaunchAngle * Mathf.Deg2Rad) / -Physics.gravity.y;
		}

		static float BurstHeight(float r, Settings s)
		{
			float fuse = s.FuseSeconds;
			return s.TubeHeight + Speed(r, s) * Mathf.Sin(s.LaunchAngle * Mathf.Deg2Rad) * fuse + 0.5f * Physics.gravity.y * fuse * fuse;
		}

		static Transform _holder;
		static readonly Dictionary<GameObject, GameObject> Made = new Dictionary<GameObject, GameObject>();
		static readonly Dictionary<GameObject, ObjectPoolManager> PooledIn = new Dictionary<GameObject, ObjectPoolManager>();

		static GameObject Grenade3D17(GameObject basePrefab, Settings s)
		{
			if (basePrefab == null) return null;
			if (Made.TryGetValue(basePrefab, out var done) && done != null) return done;
			if (Made.ContainsValue(basePrefab)) return basePrefab;
			try
			{
				if (_holder == null)
				{
					var holder = new GameObject("Shtora 3D17 holder");
					holder.SetActive(false);
					UnityEngine.Object.DontDestroyOnLoad(holder);
					_holder = holder.transform;
				}
				var grenadeModel = UnityEngine.Object.Instantiate(basePrefab, _holder, false);
				grenadeModel.name = $"Smoke - 3D17 81mm ({s.Tag})";
				var gren = grenadeModel.GetComponent<Grenade>();
				FuseField.SetValue(gren, new Vector2(s.FuseSeconds, s.FuseSeconds));
				float burstH = BurstHeight((s.Distance.x + s.Distance.y) / 2f, s);
				var effect = (GameObject)EffectField.GetValue(gren);
				var made = new List<string>();
				var white = WhiteEffect(s);
				if (white != null)
				{
					var whiteEffect = UnityEngine.Object.Instantiate(white, _holder, false);
					whiteEffect.name = $"Smoke 3D17 aerosol ({s.Tag}, from {white.name})";
					TuneWhite(whiteEffect, s, burstH, made);
					EffectField.SetValue(gren, whiteEffect);
				}
				else if (effect != null)
				{
					var effectClone = UnityEngine.Object.Instantiate(effect, _holder, false);
					effectClone.name = $"Smoke 3D17 aerosol ({s.Tag})";
					foreach (Transform child in effectClone.transform)
						if (child.name.IndexOf("Fuse", StringComparison.OrdinalIgnoreCase) >= 0) child.gameObject.SetActive(false);
					foreach (var particles in effectClone.GetComponentsInChildren<ParticleSystem>(true))
					{
						var main = particles.main;
						if (particles.GetComponent<ParticleSystemVisionBlocker>() != null || particles.transform == effectClone.transform)
						{
							main.duration = s.CloudLife + 2f;
							main.startLifetime = s.CloudLife;
							continue;
						}
						if (!particles.gameObject.activeInHierarchy
							&& particles.transform.parent != null
							&& !particles.transform.parent.gameObject.activeSelf)
							continue;
						// Vision blockers scale with particle alpha, so the 3Д6's fade-in blocks nothing for 2 s, and its plume
						// climbs clear of any line of sight.
						int max = Mathf.Max(s.CloudParticles, main.maxParticles);
						main.maxParticles = max;
						main.duration = s.CloudBuild;
						main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f * s.CloudLife, 0.9f * s.CloudLife);
						main.startSizeMultiplier *= s.CloudSizeScale;
						var emission = particles.emission;
						emission.enabled = false;
						if (particles.GetComponent<BurstOnStart>() == null) particles.gameObject.AddComponent<BurstOnStart>().Count = max;
						var shape = particles.shape;
						shape.enabled = true;
						shape.shapeType = ParticleSystemShapeType.Box;
						shape.scale = new Vector3(2f * s.CloudSpread, burstH, 2f * s.CloudSpread);
						shape.position = new Vector3(0f, -burstH / 2f, 0f);
						var col = particles.colorOverLifetime;
						string mode = col.enabled ? col.color.mode.ToString() : "off";
						if (col.enabled)
						{
							var color = col.color.Evaluate(0.5f, 0.5f);
							var gradient = new Gradient();
							gradient.SetKeys(new[]
							{
								new GradientColorKey(new Color(color.r, color.g, color.b), 0f),
								new GradientColorKey(new Color(color.r, color.g, color.b), 1f)
							},
								new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.85f, 0.65f), new GradientAlphaKey(0f, 1f) });
							col.color = new ParticleSystem.MinMaxGradient(gradient);
						}
						made.Add($"'{particles.name}' {max} particles, colour over life {mode}, size over life {(particles.sizeOverLifetime.enabled ? "on" : "off")}");
						var size = particles.sizeOverLifetime;
						if (size.enabled)
							size.size = new ParticleSystem.MinMaxCurve(
								1.5f,
								new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.15f, 1f), new Keyframe(1f, 1.3f)));
						var force = particles.forceOverLifetime;
						if (force.enabled)
						{
							force.x = new ParticleSystem.MinMaxCurve(0f);
							force.y = new ParticleSystem.MinMaxCurve(s.CloudRise);
							force.z = new ParticleSystem.MinMaxCurve(0f);
						}
						var wind = particles.externalForces;
						if (wind.enabled) wind.multiplier = 1f;
					}
					EffectField.SetValue(gren, effectClone);
				}
				Made[basePrefab] = grenadeModel;
				return grenadeModel;
			}
			catch (Exception exception)
			{
				MelonLogger.Error($"{s.Tag} Shtora 3D17 grenade copy failed: {exception.Message}");
				return null;
			}
		}

		static GameObject _white;

		internal static void WarmDonors(Settings s)
		{
			if (_holder == null)
			{
				var holder = new GameObject("Shtora 3D17 holder");
				holder.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(holder);
				_holder = holder.transform;
			}
			WhiteEffect(s);
		}

		static GameObject WhiteEffect(Settings s)
		{
			if (_white != null) return _white;
			foreach (var key in s.WhiteSmokeDonors ?? new string[0])
			{
				var prefab = DonorParts.Prefab(key);
				var mgr = prefab != null ? prefab.GetComponentInChildren<VehicleSmokeManager>(true) : null;
				var grenadePrefab = mgr != null ? PrefabField?.GetValue(mgr) as GameObject : null;
				var grenade = grenadePrefab != null ? grenadePrefab.GetComponent<Grenade>() : null;
				var effect = grenade != null ? EffectField?.GetValue(grenade) as GameObject : null;
				if (effect == null) continue;
				_white = UnityEngine.Object.Instantiate(effect, _holder, false);
				_white.name = effect.name;
				return _white;
			}
			DonorParts.WarnOnce(
				"shtora white smoke",
				$"{s.Tag}: no white smoke donor ({string.Join(", ", s.WhiteSmokeDonors ?? new string[0])}) found; the 3D17 screen uses the 3D6's dark cloud.");
			return null;
		}

		static void TuneWhite(GameObject fx, Settings s, float burstH, List<string> made)
		{
			foreach (var particles in fx.GetComponentsInChildren<ParticleSystem>(true))
			{
				string partName = particles.name;
				if (partName == "Smoke Bottom") particles.transform.localPosition += Vector3.down * (burstH - s.CloudBottomHeight);
				else if (partName == "Smoke Top") particles.transform.localPosition += Vector3.down * (burstH * 0.5f);
				if (partName == "Smoke Bottom" || partName == "Smoke Top")
					Spread(particles, s.WhiteBottoms, s.CloudSpread, new Vector3(1f, 0.15f, 1f));
				else if (partName.StartsWith("Smoke Ground Tracer")) Spread(particles, s.WhiteTracers, 0f, Vector3.one);
				var main = particles.main;
				bool smoke = partName.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0
					&& partName.IndexOf("Tracer", StringComparison.OrdinalIgnoreCase) < 0
					&& partName.IndexOf("Spawner", StringComparison.OrdinalIgnoreCase) < 0;
				if (smoke && main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
				{
					float k = s.CloudLife / Mathf.Max(1f, main.startLifetime.constantMax);
					main.startLifetime = new ParticleSystem.MinMaxCurve(
						main.startLifetime.constantMin * k,
						main.startLifetime.constantMax * k);
					main.duration = Mathf.Max(main.duration, s.CloudLife + 2f);
					made.Add($"'{partName}' lifetime x{k:0.00}"
						+ (partName == "Smoke Bottom" || partName == "Smoke Top"
							? $", {particles.transform.localPosition.y:+0.0;-0.0} m"
							: ""));
				}
				else if (smoke && main.startLifetime.mode == ParticleSystemCurveMode.Constant)
				{
					float k = s.CloudLife / Mathf.Max(1f, main.startLifetime.constant);
					main.startLifetime = main.startLifetime.constant * k;
					main.duration = Mathf.Max(main.duration, s.CloudLife + 2f);
					made.Add($"'{partName}' lifetime x{k:0.00}"
						+ (partName == "Smoke Bottom" || partName == "Smoke Top"
							? $", {particles.transform.localPosition.y:+0.0;-0.0} m"
							: ""));
				}
			}
		}

		static void Spread(ParticleSystem ps, int count, float radius, Vector3 scale)
		{
			var main = ps.main;
			main.maxParticles = Mathf.Max(main.maxParticles, count);
			var emission = ps.emission;
			if (emission.burstCount > 0)
			{
				var burst = emission.GetBurst(0);
				burst.count = count;
				emission.SetBurst(0, burst);
			}
			else emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
			if (radius > 0f)
			{
				var shape = ps.shape;
				shape.enabled = true;
				shape.shapeType = ParticleSystemShapeType.Sphere;
				shape.radius = radius;
				shape.scale = scale;
			}
		}

		internal sealed class BurstOnStart : MonoBehaviour
		{
			public int Count;
			bool _started;
			void Start()
			{
				_started = true;
				Burst();
			}

			void OnEnable() { if (_started) Burst(); }

			void Burst()
			{
				var particles = GetComponent<ParticleSystem>();
				if (particles == null) return;
				if (!particles.isPlaying) particles.Play(false);
				particles.Emit(Count);
			}
		}
	}
}
