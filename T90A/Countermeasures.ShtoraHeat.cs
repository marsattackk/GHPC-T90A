using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using GHPC.Vehicle;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// The Shtora-1 emitters warming up in thermal sights. Each draws about 500 W. The lens window soaks up the visible part
	// of the xenon light and warms in minutes, and the 30 kg box takes the rest of the heat and warms over twenty.
	// Thin copies of the turret's own surface over each emitter, shown only in thermal, carry the heat. While cold they
	// draw exactly like the turret, so nothing shows until the lamp has run.
	internal sealed class ShtoraHeat : MonoBehaviour
	{
		const string VehicleShader = "GHPC/VehicleShader";
		const float Lift = 0.002f;   // overlays this far out along the normals

		// Kelvin to GHPC's thermal heat: vanilla soldiers carry 1.23 against a vehicle's 0.8, for skin about 20 K over the air.
		const float HeatPerKelvin = 0.0215f;
		// Estimates from 1 kW for the pair, a 280 x 350 x 350 mm box of 30-35 kg on the turret, and the КС-19 filter in front.
		const float WindowRise = 60f;
		const float WindowSeconds = 360f;
		const float BoxRise = 25f;
		const float BoxSeconds = 1200f;

		// GHPC/VehicleShader never reads _FLIR_Emission. Its thermal pass adds heat for scorch, spread by _PaintMask's green,
		// so the box's copy gets a mask that's all green and its heat goes in as _ScorchAmount. Red and blue (camo, snow) stay
		// 0, as the T-90A's own material has them. Below 0.09 the scorch adds nothing and it's saturated by 0.12.
		// The value goes on each box's own material: a property block's _ScorchAmount did nothing once the turret's
		// HeatSource had put a block of its own on the copy.
		// Measured in the gunner's thermal sight against the lens copy: heat over the vehicle's own -> _ScorchAmount.
		static readonly (float heat, float scorch)[] ScorchForHeat =
		{
			(0f, 0.088f),
			(0.33f, 0.10f),
			(0.93f, 0.11f),
			(1.2f, 0.115f),
			(1.6f, 0.12f),
		};

		static readonly int Scorch = Shader.PropertyToID("_ScorchAmount");
		static readonly int Emission = Shader.PropertyToID("_FLIR_Emission");

		sealed class Emitter
		{
			internal bool Left;
			internal HeatSource Lens;
			internal Material Box;
			internal float Window, Body, Scorch = -1f;
		}

		Shtora.Station _station;
		MeshRenderer _turret;
		int _lensSlot, _boxSlot;
		readonly List<Emitter> _emitters = new List<Emitter>();
		MaterialPropertyBlock _block;

		internal static void Apply(Vehicle vehicle)
		{
			var station = vehicle.GetComponent<Shtora.Station>();
			var set = vehicle.GetComponent<ModuleBuilder.ModuleSet>();
			if (station == null || set == null) return;
			var root = vehicle.transform;
			var mesh = ShtoraGlow.FindLensMesh(root, out int lensSlot);
			var materials = mesh != null ? mesh.sharedMaterials : new Material[0];
			int boxSlot = Array.FindIndex(materials, m => m != null && m.shader.name == VehicleShader);
			if (mesh == null || boxSlot < 0)
			{
				MelonLogger.Warning($"{T90AMod.FriendlyName}: no Shtora lens or turret paint found; the emitters stay cold in thermal sights.");
				return;
			}
			var heat = vehicle.gameObject.AddComponent<ShtoraHeat>();
			heat._station = station;
			heat._turret = mesh;
			heat._lensSlot = lensSlot;
			heat._boxSlot = boxSlot;
			heat._block = new MaterialPropertyBlock();
			var source = mesh.GetComponent<MeshFilter>().sharedMesh;
			var turret = Followers.TurretBone(vehicle);
			var lenses = ShtoraGlow.Lenses(mesh, lensSlot).ToList();
			foreach (bool left in new[] { true, false })
			{
				string id = left ? station.S.EmitterLeft : station.S.EmitterRight;
				var module = set.Built.Select(b => b.m).FirstOrDefault(m => m.Id == id && m.Boxes != null && m.Boxes.Count > 0);
				if (module == null) continue;
				var bounds = module.Bounds;
				bounds.Expand(0.02f);
				// The module's box is in the vehicle's frame, and the turret is still straight ahead at spawn.
				var box = Overlay(source, boxSlot, v => bounds.Contains(root.InverseTransformPoint(mesh.transform.TransformPoint(v))));
				var emitter = new Emitter { Left = left };
				emitter.Box = new Material(materials[boxSlot]) { name = "T90A_shtora box heat" };
				emitter.Box.SetTexture("_PaintMask", Green());
				emitter.Box.SetFloat(Scorch, 0f);
				Show(box, mesh.transform, (left ? "left" : "right") + " box heat", emitter.Box);
				var lens = lenses.FirstOrDefault(l => ShtoraGlow.IsLeft(station, turret, mesh, l.centre) == left);
				if (lens.radius > 0f)
				{
					float side = Mathf.Sign(lens.centre.x);
					var glass = Overlay(source, lensSlot, v => Mathf.Sign(v.x) == side);
					var lensRenderer = Show(glass, mesh.transform, (left ? "left" : "right") + " lens heat", new Material(materials[lensSlot]));
					emitter.Lens = lensRenderer.gameObject.AddComponent<HeatSource>();
				}
				heat._emitters.Add(emitter);
			}
		}

		void Update()
		{
			float dt = Time.deltaTime;
			foreach (var emitter in _emitters)
			{
				bool lit = _station != null && _station.Working && (emitter.Left ? _station.LeftWorks : _station.RightWorks);
				emitter.Window = Approach(emitter.Window, lit ? WindowRise : 0f, WindowSeconds, dt);
				emitter.Body = Approach(emitter.Body, lit ? BoxRise : 0f, BoxSeconds, dt);
			}
			if (_turret == null || CameraManager.Instance == null || CameraManager.Instance.CurrentLightMode != NightVisionType.Thermal)
				return;
			// the turret's own values, read every frame: its HeatSource sets the glass, and a fire scorches the paint
			_turret.GetPropertyBlock(_block, _lensSlot);
			float glass = _block.GetFloat(Emission);
			_turret.GetPropertyBlock(_block, _boxSlot);
			float burnt = _block.GetFloat(Scorch);
			_turret.GetPropertyBlock(_block);
			burnt = Mathf.Max(burnt, _block.GetFloat(Scorch));
			foreach (var emitter in _emitters)
			{
				float lensHeat = glass + emitter.Window * HeatPerKelvin;
				if (emitter.Lens != null && !Mathf.Approximately(emitter.Lens.Heat, lensHeat))
					emitter.Lens.Heat = lensHeat;
				float scorch = Mathf.Max(burnt, ScorchFor(emitter.Body * HeatPerKelvin));
				if (Mathf.Abs(scorch - emitter.Scorch) < 0.0002f) continue;
				emitter.Scorch = scorch;
				emitter.Box.SetFloat(Scorch, scorch);
			}
		}

		static float Approach(float value, float target, float seconds, float dt) => value + (target - value) * (1f - Mathf.Exp(-dt / seconds));

		static float ScorchFor(float heat)
		{
			if (heat < 0.005f) return 0f;
			for (int i = 1; i < ScorchForHeat.Length; i++)
			{
				var (h0, s0) = ScorchForHeat[i - 1];
				var (h1, s1) = ScorchForHeat[i];
				if (heat <= h1) return Mathf.Lerp(s0, s1, (heat - h0) / (h1 - h0));
			}
			return ScorchForHeat[ScorchForHeat.Length - 1].scorch;
		}

		static Texture2D _green;

		static Texture2D Green()
		{
			if (_green != null) return _green;
			_green = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "T90A_shtora scorch mask" };
			_green.SetPixel(0, 0, new Color(0f, 1f, 0f, 1f));
			_green.Apply(false, true);
			return _green;
		}

		// The triangles of one submesh whose corners all pass, lifted off the surface, with the normals, tangents and UVs the
		// turret's shaders read.
		static Mesh Overlay(Mesh source, int submesh, Func<Vector3, bool> keep)
		{
			var vertices = source.vertices;
			var normals = source.normals;
			var tangents = source.tangents;
			var uv = new List<Vector2>();
			source.GetUVs(0, uv);
			var picked = new Dictionary<int, int>();
			var positions = new List<Vector3>();
			var facing = new List<Vector3>();
			var along = new List<Vector4>();
			var mapped = new List<Vector2>();
			var triangles = new List<int>();
			var corners = source.GetTriangles(submesh);
			for (int t = 0; t < corners.Length; t += 3)
			{
				if (!keep(vertices[corners[t]]) || !keep(vertices[corners[t + 1]]) || !keep(vertices[corners[t + 2]])) continue;
				for (int k = 0; k < 3; k++)
				{
					int i = corners[t + k];
					if (!picked.TryGetValue(i, out int j))
					{
						j = positions.Count;
						picked[i] = j;
						positions.Add(vertices[i] + normals[i] * Lift);
						facing.Add(normals[i]);
						along.Add(tangents[i]);
						mapped.Add(uv[i]);
					}
					triangles.Add(j);
				}
			}
			var mesh = new Mesh { name = "T90A_shtora heat" };
			mesh.SetVertices(positions);
			mesh.SetNormals(facing);
			mesh.SetTangents(along);
			mesh.SetUVs(0, mapped);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		static MeshRenderer Show(Mesh mesh, Transform parent, string name, Material material)
		{
			var go = new GameObject("T90A_shtora " + name);
			go.SetActive(false);
			go.transform.SetParent(parent, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.enabled = false;   // until the item's Awake shows it, in thermal only
			var item = go.AddComponent<LightBandExclusiveItem>();
			item.ShowInStandard = false;
			item.ShowInNightVision = false;
			item.ShowInThermal = true;
			item.StartSwitchedOn = true;
			go.SetActive(true);
			return renderer;
		}
	}
}
