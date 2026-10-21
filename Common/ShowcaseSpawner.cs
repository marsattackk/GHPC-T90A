using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Mission;
using GHPC.Mission.Data;
using GHPC.State;
using GHPC.Utility;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal static class ShowcaseSpawner
	{
		internal const string Scene = "TR01_showcase";

		internal sealed class Entry
		{
			public string Key;
			public Func<string> Name;
			public string[] Anchors = { "T80B", "T72M1" };
			public float[] Offsets = { 7f, -7f, 14f, -14f, 21f, -21f };
			public float MinGap = 6f;
		}

		static readonly List<Entry> Entries = new List<Entry>();

		internal static void Add(Entry e) => Entries.Add(e);

		internal static void OnSceneLoaded(string sceneName)
		{
			if (sceneName == Scene && Entries.Count > 0) StateController.RunOrDefer(GameState.GameReady, Spawn);
		}

		static IEnumerator Spawn(GameState _)
		{
			var anchors = Entries.SelectMany(e => e.Anchors).ToList();
			Vehicle[] vehicles = new Vehicle[0];
			for (int i = 0; i < 300; i++)
			{
				vehicles = UnityEngine.Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None);
				if (vehicles.Any(v => anchors.Contains(v.UniqueName))) break;
				yield return null;
			}
			foreach (var e in Entries)
			{
				vehicles = UnityEngine.Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None);
				Place(e, vehicles);
			}
		}

		static void Place(Entry e, Vehicle[] vehicles)
		{
			string name = e.Name?.Invoke() ?? e.Key;
			if (vehicles.Any(v => v.UniqueName == e.Key)) return;
			var anchor = e.Anchors.Select(k => vehicles.FirstOrDefault(v => v.UniqueName == k)).FirstOrDefault(v => v != null) ?? vehicles.FirstOrDefault();
			if (anchor == null || UnitSpawner.Instance == null) { MelonLogger.Warning($"Showcase: no vehicle to place the {name} next to."); return; }

			var t = anchor.transform;
			foreach (float k in e.Offsets)
			{
				var pos = t.position + t.right * k;
				if (vehicles.Any(v => Vector3.Distance(v.transform.position, pos) < e.MinGap)) continue;
				if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out var ground, 60f, 1 << CodeUtils.LAYER_INDEX_TERRAIN))
					pos = ground.point;
				UnitSpawner.Instance.SpawnUnit(e.Key, new UnitMetaData
				{
					Name = e.Key,
					Allegiance = Faction.Neutral,
					Position = pos,
					Rotation = t.rotation,
					UnitType = UnitType.GroundVehicle,
				});
				return;
			}
			MelonLogger.Warning($"Showcase: no free spot for the {name} next to {anchor.FriendlyName}.");
		}
	}
}
