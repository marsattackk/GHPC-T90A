using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.AI;
using GHPC.Equipment;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using UnityEngine;

namespace T90A.Common
{
	// Shtora-1's screen side: warning heads, 3D17 grenades, blinding the AI that lases it.
	internal static partial class ShtoraScreen
	{
		internal enum Look { ControlPanel, ScreenPage }

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			public string Tag;
			public bool Heads = true;
			public float PreciseHalfAz = 45f;
			public float PreciseStep = 3.75f;
			public Look Display = Look.ControlPanel;
			public string PageTitle = "СПЗ";
			public string MenuName = "Shtora-1";
			public bool PageTone = true;
			public Vector2 HeadEl = new Vector2(-5f, 25f);
			public bool Automatic = true;
			public float OwnShotBlock = 15f;
			public float LaunchAngle = 12f;
			public Vector2 Distance = new Vector2(50f, 80f);
			public float CloudBuild = 3f, CloudLife = 20f;
			public float CloudSpread = 8f;
			public float CloudSizeScale = 4f;
			public int CloudParticles = 60;
			public float CloudRise = 0f;
			public float FuseSeconds = 1f;
			public float TubeHeight = 2.3f;
			public string[] WhiteSmokeDonors = { "LEO2A3", "M60A3", "LEO1A3", "M1" };
			public float CloudBottomHeight = 2f;
			public int WhiteBottoms = 5, WhiteTracers = 6;
			public float AiRelase = 10f;
			public Vector2 AiLaseDelay = new Vector2(0.5f, 1f);
			public float SameThreatDeg = 20f;
			public bool AlarmTone = true;
			public bool PanelShown = true;
			public bool PanelAlways;
			public bool BlindAi = true;
			public Vector2 BlindRange = new Vector2(0.9f, 1.1f);
			internal Settings Copy() => (Settings)MemberwiseClone();
		}
#pragma warning restore CS0649

		internal static readonly List<Station> Stations = new List<Station>();
		internal static Station Of(Unit u)
		{
			if (u == null) return null;
			for (int i = 0; i < Stations.Count; i++)
			{
				var station = Stations[i];
				if (station != null && station.Vehicle == u) return station;
			}
			return null;
		}

		static readonly FieldInfo SlotsField = Reflect.Field(typeof(VehicleSmokeManager), "_smokeSlots"),
			PrefabField = Reflect.Field(typeof(VehicleSmokeManager), "_smokePrefab"),
			LaunchAngleField = Reflect.Field(typeof(VehicleSmokeManager), "_launchAngle"),
			DistanceField = Reflect.Field(typeof(VehicleSmokeManager), "_distanceRange"),
			ForwardRefField = Reflect.Field(typeof(VehicleSmokeManager), "_forwardReference"),
			PoolReturnField = Reflect.Field(typeof(VehicleSmokeManager), "_poolReturnDelegate"),
			ShootEventField = Reflect.Field(typeof(VehicleSmokeManager), "_shootSmokeEvent"),
			FuseField = Reflect.Field(typeof(Grenade), "_fuseTimeRange"),
			EffectField = Reflect.Field(typeof(Grenade), "_effectPrefab"),
			LaserDestroyedField = Reflect.Field(typeof(FireControlSystem), "_laserDestroyed");
		static readonly MethodInfo ImpulseMethod = Reflect.Method(
			typeof(VehicleSmokeManager),
			"CalculateLaunchImpulse",
			new[] { typeof(float), typeof(float), typeof(float) }),
			LookAt = Reflect.Method(typeof(UnitAI), "FinalLookAtVector", new[] { typeof(Vector3), typeof(float) });

		static bool LaserDestroyed(FireControlSystem fcs) => LaserDestroyedField?.GetValue(fcs) is bool destroyed && destroyed;

		internal static string Clock(float az)
		{
			int hour = Mathf.RoundToInt(((az % 360f) + 360f) % 360f / 30f) % 12;
			return $"{(hour == 0 ? 12 : hour)} o'clock";
		}

		internal static string Apply(Vehicle v, Settings s)
		{
			var station = v.gameObject.GetComponent<Station>() ?? v.gameObject.AddComponent<Station>();
			if (s.Heads && s.PanelShown) Hud.Ensure();
			if (s.Heads && s.Display == Look.ScreenPage && s.PanelAlways) ShtoraPage.Ensure(s);
			return station.Setup(v, s);
		}

		internal static Func<Unit, string> JammerState = null;
	}
}
