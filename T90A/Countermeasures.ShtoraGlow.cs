using System.Collections.Generic;
using System.Linq;
using GHPC.Effects;
using GHPC.Vehicle;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	// The Shtora-1 emitters lit while they jam. The xenon lamps shine from 0.7 µm up, so an image intensifier sees them blaze
	// and lights the ground ahead, and the eye gets a dim deep red. Thermal sights don't see the beam at all (ShtoraHeat).
	// Copies of the T-72M1's own Luna glow and IR spotlight do the work: LightBandExclusiveItem shows each one only in the
	// views its flags name.
	internal sealed class ShtoraGlow : MonoBehaviour
	{
		internal const string LensGlass = "headlight_glass_a_under";
		const string LensGlow = "Luna visuals";      // a bright disc
		const string EyeGlow = "Luna visuals 2";     // the normal view
		const string Spotlight = "IR light source";
		// As bright along the beam as the Luna (1.6) but over 20 x 4.5° each. No source gives either lamp's output. The jammer
		// has to outshine a missile's tracer in the launcher's IR tracker at its range, so it's at least a searchlight.
		const float BeamIntensity = 1.6f;
		const int CookieSize = 128;
		// The eye's share: a soft deep red, brightest in the middle, as night footage of a jamming emitter shows it.
		// Judged on screenshots against that footage.
		static readonly Color EyeColour = new Color(0.15f, 0.006f, 0.0045f, 1f);
		// additive, so it only adds light. The Luna's own glow material is opaque and hid the lens grid by day, and the game
		// ships no Standard variant that is both see-through and emissive. This one is the game's own (its sparks use it).
		const string EyeShader = "Legacy Shaders/Particles/Additive";
		const int FalloffSize = 128;

		Shtora.Station _station;
		readonly List<LightBandExclusiveItem> _left = new List<LightBandExclusiveItem>();
		readonly List<LightBandExclusiveItem> _right = new List<LightBandExclusiveItem>();
		bool _leftOn, _rightOn;

		internal static void Apply(Vehicle vehicle)
		{
			var station = vehicle.GetComponent<Shtora.Station>();
			if (station == null) return;
			var root = vehicle.transform;
			var lens = FindItem(root, LensGlow);
			var eye = FindItem(root, EyeGlow);
			var mesh = FindLensMesh(root, out int submesh);
			if (lens == null || eye == null || mesh == null)
			{
				string missing = mesh == null ? "Shtora lens" : "T-72M1 Luna glow";
				MelonLogger.Warning($"{T90AMod.FriendlyName}: no {missing} found; the emitters don't glow.");
				return;
			}
			var glow = vehicle.gameObject.AddComponent<ShtoraGlow>();
			glow._station = station;
			var eyeShader = Shader.Find(EyeShader);
			if (eyeShader == null)
				MelonLogger.Warning($"{T90AMod.FriendlyName}: no {EyeShader} shader; the emitters don't glow to the eye.");
			var eyeMaterial = eyeShader != null ? new Material(eyeShader) { name = "T90A_shtora eye glow" } : null;
			if (eyeMaterial != null)
			{
				eyeMaterial.mainTexture = Falloff();
				eyeMaterial.SetColor("_TintColor", EyeColour);
			}
			var spotlight = FindItem(root, Spotlight);
			if (spotlight == null)
				MelonLogger.Warning($"{T90AMod.FriendlyName}: no T-72M1 {Spotlight} found; the jammer doesn't light the ground in night sights.");
			var turret = Followers.TurretBone(vehicle);
			foreach (var (centre, normal, radius) in Lenses(mesh, submesh))
			{
				bool left = IsLeft(station, turret, mesh, centre);
				var side = left ? glow._left : glow._right;
				string name = left ? "left" : "right";
				side.Add(Copy(lens, mesh.transform, centre, normal, radius, name + " lens glow", null));
				if (eyeMaterial != null)
					side.Add(Copy(eye, mesh.transform, centre, normal, radius, name + " eye glow", eyeMaterial));
				if (spotlight != null)
					side.Add(Beam(spotlight, mesh.transform, centre + normal * 0.01f, turret, left, station.S));
			}
		}

		// Each lens goes with the emitter module nearest to it, so a knocked-out emitter goes dark on its own side.
		internal static bool IsLeft(Shtora.Station station, Transform turret, MeshRenderer mesh, Vector3 centre)
		{
			var local = turret.InverseTransformPoint(mesh.transform.TransformPoint(centre));
			return Vector3.Distance(local, station.LeftLocal) < Vector3.Distance(local, station.RightLocal);
		}

		// One emitter's beam: as wide as the jammer's sector and as tall as its band, turned out by half its width. The emitters
		// sit on the turret, so the beam turns with it and never rises with the gun.
		static LightBandExclusiveItem Beam(
			LightBandExclusiveItem source,
			Transform parent,
			Vector3 position,
			Transform turret,
			bool left,
			Shtora.Settings settings)
		{
			var item = Clone(source, parent, (left ? "left" : "right") + " beam");
			item.transform.localPosition = position;
			item.transform.rotation = turret.rotation * Quaternion.Euler(0f, (left ? -0.5f : 0.5f) * settings.SectorAz, 0f);
			foreach (var light in item.GetComponentsInChildren<Light>(true))
			{
				light.spotAngle = settings.SectorAz;
				light.innerSpotAngle = settings.SectorAz * 0.9f;
				light.cookie = Cookie(settings);
				light.intensity = BeamIntensity;
				light.shadows = LightShadows.None;
			}
			item.gameObject.SetActive(true);
			return item;
		}

		static Texture2D _cookie;

		// The beam's shape inside the spotlight's cone: a band SectorEl high either side, soft at its edges.
		static Texture2D Cookie(Shtora.Settings settings)
		{
			if (_cookie != null) return _cookie;
			_cookie = new Texture2D(CookieSize, CookieSize, TextureFormat.RGBA32, false)
			{
				name = "T90A_shtora beam",
				wrapMode = TextureWrapMode.Clamp,
			};
			float band = Mathf.Tan(settings.SectorEl * Mathf.Deg2Rad) / Mathf.Tan(0.5f * settings.SectorAz * Mathf.Deg2Rad);
			var pixels = new Color32[CookieSize * CookieSize];
			for (int y = 0; y < CookieSize; y++)
				for (int x = 0; x < CookieSize; x++)
				{
					float across = Mathf.Abs((x + 0.5f) / CookieSize * 2f - 1f);
					float up = Mathf.Abs((y + 0.5f) / CookieSize * 2f - 1f);
					float value = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - up / band) * 2f)) * Mathf.Clamp01((1f - across) * 6f);
					byte level = (byte)Mathf.RoundToInt(value * 255f);
					pixels[y * CookieSize + x] = new Color32(level, level, level, level);
				}
			_cookie.SetPixels32(pixels);
			_cookie.Apply(false, true);
			return _cookie;
		}

		void Update()
		{
			bool working = _station != null && _station.Working;
			Switch(_left, working && _station.LeftWorks, ref _leftOn);
			Switch(_right, working && _station.RightWorks, ref _rightOn);
		}

		static void Switch(List<LightBandExclusiveItem> items, bool on, ref bool was)
		{
			if (on == was) return;
			was = on;
			foreach (var item in items)
				if (item != null) item.SwitchedOn = on;
		}

		static Texture2D _falloff;

		// A round glow that fades from the middle to nothing at the rim: (1 - r²)².
		static Texture2D Falloff()
		{
			if (_falloff != null) return _falloff;
			_falloff = new Texture2D(FalloffSize, FalloffSize, TextureFormat.RGBA32, false)
			{
				name = "T90A_shtora glow falloff",
				wrapMode = TextureWrapMode.Clamp,
			};
			var pixels = new Color32[FalloffSize * FalloffSize];
			for (int y = 0; y < FalloffSize; y++)
				for (int x = 0; x < FalloffSize; x++)
				{
					float dx = (x + 0.5f) / FalloffSize * 2f - 1f;
					float dy = (y + 0.5f) / FalloffSize * 2f - 1f;
					float r2 = dx * dx + dy * dy;
					float value = r2 >= 1f ? 0f : (1f - r2) * (1f - r2);
					byte level = (byte)Mathf.RoundToInt(value * 255f);
					pixels[y * FalloffSize + x] = new Color32(level, level, level, level);
				}
			_falloff.SetPixels32(pixels);
			_falloff.Apply(false, true);
			return _falloff;
		}

		static LightBandExclusiveItem FindItem(Transform root, string name)
			=> root.GetComponentsInChildren<LightBandExclusiveItem>(true).FirstOrDefault(i => i.name == name);

		// The turret's full-detail mesh (not a LOD) and the submesh of the lens backing.
		internal static MeshRenderer FindLensMesh(Transform root, out int submesh)
		{
			foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				if (renderer.name.StartsWith("LOD")) continue;
				var materials = renderer.sharedMaterials;
				for (int i = 0; i < materials.Length; i++)
				{
					if (materials[i] == null || !materials[i].name.EndsWith(LensGlass)) continue;
					var filter = renderer.GetComponent<MeshFilter>();
					if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
					submesh = i;
					return renderer;
				}
			}
			submesh = -1;
			return null;
		}

		// Centre, facing and radius of each lens in the mesh's frame, one per side of the gun.
		internal static IEnumerable<(Vector3 centre, Vector3 normal, float radius)> Lenses(MeshRenderer renderer, int submesh)
		{
			var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
			var corners = mesh.GetTriangles(submesh);
			var vertices = mesh.vertices;
			var normals = mesh.normals;
			foreach (float side in new[] { -1f, 1f })
			{
				var picked = corners.Where(i => Mathf.Sign(vertices[i].x) == side).ToArray();
				if (picked.Length == 0) continue;
				var centre = picked.Aggregate(Vector3.zero, (sum, i) => sum + vertices[i]) / picked.Length;
				// the lens is two-sided: its back faces would cancel the normal, so every normal is turned forward first
				var normal = picked.Aggregate(Vector3.zero, (sum, i) => sum + (normals[i].z < 0f ? -normals[i] : normals[i])).normalized;
				float radius = picked.Max(i => (vertices[i] - centre).magnitude);
				yield return (centre, normal, radius);
			}
		}

		static LightBandExclusiveItem Copy(
			LightBandExclusiveItem source,
			Transform parent,
			Vector3 centre,
			Vector3 normal,
			float radius,
			string name,
			Material material)
		{
			var item = Clone(source, parent, name);
			if (material != null)
				foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
					renderer.sharedMaterial = material;
			// The Luna's disc is 0.02 across at a scale of 1: sized to 90 % of the lens, thin along its facing.
			float scale = 0.9f * 2f * radius / 0.02f;
			item.transform.localPosition = centre + normal * 0.006f;
			item.transform.localRotation = Quaternion.LookRotation(normal, Vector3.up);
			item.transform.localScale = new Vector3(scale, scale, scale * 0.1f);
			item.gameObject.SetActive(true);
			return item;
		}

		// Left inactive for the caller to place.
		static LightBandExclusiveItem Clone(LightBandExclusiveItem source, Transform parent, string name)
		{
			// copied inactive: Awake would hook the copy to the Luna's own DestructibleComponent and put it out with the Luna
			var sourceObject = source.gameObject;
			bool wasActive = sourceObject.activeSelf;
			sourceObject.SetActive(false);
			var copy = Instantiate(sourceObject, parent, false);
			sourceObject.SetActive(wasActive);
			copy.name = "T90A_shtora " + name;
			var item = copy.GetComponent<LightBandExclusiveItem>();
			item.Destructible = null;
			item.StartSwitchedOn = false;
			// the Luna's lens glow shows in thermal too. Glass doesn't pass 8-12 µm, so a thermal sight never sees the beam.
			item.ShowInThermal = false;
			// renamed: the T-72M1 leftover hiding goes by the Luna's renderer names
			foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
				renderer.name = copy.name;
			return item;
		}
	}
}
