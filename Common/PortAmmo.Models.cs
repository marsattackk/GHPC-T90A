using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;
using GHPC.Effects;
using System.IO;

namespace T90A.Common
{
	internal static partial class PortAmmo
	{
		// Rack rounds are Instantiates of these. Left live, anything the game did to one went into every round after.
		static Transform _modelHolder;

		static Transform ModelHolder
		{
			get
			{
				if (_modelHolder != null) return _modelHolder;
				var holder = new GameObject($"{S.Tag} round models (inactive)");
				holder.SetActive(false);
				Object.DontDestroyOnLoad(holder);
				_modelHolder = holder.transform;
				return _modelHolder;
			}
		}

		static AmmoClipCodexScriptable MakeClip(Round r, AmmoType source, AmmoType modelSource = null)
		{
			var model = modelSource?.VisualModel ?? source.VisualModel;
			var ammo = new AmmoType();
			foreach (var field in typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(ammo, field.GetValue(source));
			// AmmoType.Equals compares names: a vanilla name drags the round into that round's logistics lists.
			ammo.Name = r.Name;
			ammo.ArmorOptimizations = new AmmoType.ArmorOptimization[0];
			r.Set(ammo);

			var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
			codex.name = $"ammo_{S.Prefix}_{r.Id}";
			codex.AmmoType = ammo;

			var clip = new AmmoType.AmmoClip { Capacity = 1, Name = r.Name, MinimalPattern = new[] { codex } };
			var clipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
			clipCodex.name = $"clip_{S.Prefix}_{r.Id}";
			clipCodex.ClipType = clip;

			if (model != null)
			{
				var visual = Object.Instantiate(model, ModelHolder, false);
				visual.name = r.Id + " visual";
				var stored = visual.GetComponent<AmmoStoredVisual>();
				if (stored != null)
				{
					stored.AmmoType = ammo;
					stored.AmmoScriptable = codex;
				}
				ApplyLook(visual, r);
				ApplyShape(visual, r);
				ApplyCharge(visual, r);
				ApplyRackTnt(visual, r, ammo);
				ammo.VisualModel = visual;
				created.Add(visual);
			}
			created.Add(codex);
			created.Add(clipCodex);
			return clipCodex;
		}

		static readonly Dictionary<string, Texture2D> _looks = new Dictionary<string, Texture2D>();
		static readonly Dictionary<(Material, string), Material> _lookMaterials = new Dictionary<(Material, string), Material>();

		static void ApplyLook(GameObject model, Round r)
		{
			if (string.IsNullOrEmpty(r.Texture) || string.IsNullOrEmpty(r.Atlas)) return;
			int swapped = 0;
			foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
			{
				var mats = rend.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < mats.Length; i++)
				{
					var material = mats[i];
					if (material == null
						|| !material.HasProperty("_MainTex")
						|| material.mainTexture == null
						|| material.mainTexture.name != r.Atlas)
						continue;
					if (!_lookMaterials.TryGetValue((material, r.Texture), out var copy) || copy == null)
					{
						var tex = LookTexture(r.Texture, material.mainTexture);
						if (tex == null)
						{
							MelonLogger.Warning($"{S.Tag}: the {r.Id} stencils ({r.Texture}) are missing from the mod; its rounds keep the {r.Atlas} look.");
							return;
						}
						copy = new Material(material)
						{
							name = $"{material.name} ({S.Prefix} {r.Id})",
							hideFlags = HideFlags.DontUnloadUnusedAsset
						};
						copy.mainTexture = tex;
						_lookMaterials[(material, r.Texture)] = copy;
					}
					mats[i] = copy;
					changed = true;
					swapped++;
				}
				if (changed) rend.sharedMaterials = mats;
			}
		}

		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class ChargeSplit
		{
			public string[] Parts;
			public string Projectile;
		}
#pragma warning restore CS0649

		static void ApplyCharge(GameObject model, Round r)
		{
			if (r.Charge == null) return;
			var stored = model.GetComponent<AmmoStoredVisual>();
			var all = model.GetComponentsInChildren<Transform>(true);
			var parts = r.Charge.Parts
				.Select(p => all.FirstOrDefault(c => c != model.transform && ShapePartIs(model, c, p)))
				.ToArray();
			if (stored == null || parts.Any(p => p == null))
			{
				MelonLogger.Warning($"{S.Tag}: the {r.Id} model has no such charge parts; it stays one piece.");
				return;
			}
			var keep = r.Charge.Projectile != null ? all.FirstOrDefault(c => ShapePartIs(model, c, r.Charge.Projectile)) : null;
			if (keep != null)
			{
				var from = keep.parent.GetComponent<AarVisual>();
				keep.SetParent(model.transform, true);
				if (from != null && keep.GetComponent<AarVisual>() == null)
				{
					var aar = keep.gameObject.AddComponent<AarVisual>();
					aar.RenderMode = from.RenderMode;
					aar.SwitchMaterials = from.SwitchMaterials;
					aar.PreserveLayerUntilAar = from.PreserveLayerUntilAar;
					aar.HideUntilAar = from.HideUntilAar;
					aar.AarMaterial = from.AarMaterial;
				}
			}
			var charge = new GameObject(r.Id + " charge");
			charge.layer = model.layer;
			charge.transform.SetParent(model.transform, false);
			foreach (var part in parts) part.SetParent(charge.transform, true);
			stored.ChargeVisual = charge.transform;
		}

		static readonly FieldInfo ItemTnt = Reflect.Field(typeof(FlammableItem), "_tntEquivalent");

		static void ApplyRackTnt(GameObject model, Round r, AmmoType ammo)
		{
			if (!r.RackTntIsFiller) return;
			var items = model.GetComponentsInChildren<FlammableItem>(true).Where(i => i.Explosive).ToList();
			if (items.Count == 0)
			{
				MelonLogger.Warning($"{S.Tag}: the {r.Id} model has no explosive part; its rack detonation stays as the source round's.");
				return;
			}
			foreach (var item in items) ItemTnt.SetValue(item, ammo.TntEquivalentKg);
		}

		static readonly AccessTools.FieldRef<GHPC.Weapons.AmmoRack, Compartment> RackRoom =
			Reflect.FieldRefAccess<GHPC.Weapons.AmmoRack, Compartment>("_compartment");

		static void BurstInParent(GHPC.Weapons.AmmoRack rack)
		{
			var room = RackRoom(rack);
			var parent = room != null ? room.Parent : null;
			var cluster = rack.Flammables;
			if (parent == null)
			{
				MelonLogger.Warning($"{S.Tag}: rack '{rack.name}' is not in a child compartment; its burst stays as it was.");
				return;
			}
			if (room.Clusters.RemoveAll(e => e == cluster) == 0)
			{
				MelonLogger.Warning($"{S.Tag}: rack '{rack.name}' has not joined its compartment yet; its burst stays as it was.");
				return;
			}
			if (!parent.Clusters.Contains(cluster)) parent.AddChildCluster(cluster);
			cluster.RegisterCompartment(parent);
			RackRoom(rack) = parent;
		}

		static readonly Dictionary<string, Mesh> _shapeMeshes = new Dictionary<string, Mesh>();
		static string[] _shapeIndex;

		static bool ShapePartIs(GameObject model, Transform t, string part)
		{
			if (!part.StartsWith("/")) return t.name == part;
			var path = "";
			for (var ancestor = t;
				ancestor != null && ancestor != model.transform;
				ancestor = ancestor.parent) path = "/" + ancestor.name + path;
			return path == part;
		}

		static void ApplyShape(GameObject model, Round r)
		{
			if (string.IsNullOrEmpty(r.Shape)) return;
			if (_shapeIndex == null)
			{
				using (var stream = typeof(PortAmmo).Assembly.GetManifestResourceStream("round_meshes.txt"))
					_shapeIndex = stream == null
						? new string[0]
						: new StreamReader(stream)
							.ReadToEnd()
							.Split('\n')
							.Select(e => e.Trim())
							.Where(e => e.Length > 0 && e[0] != '#')
							.ToArray();
			}
			int meshes = 0, zones = 0, missing = 0;
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			foreach (var line in _shapeIndex)
			{
				var fields = line.Split('|');
				if (fields.Length < 4 || fields[1] != r.Shape) continue;
				try
				{
					if (fields[0] == "mesh")
					{
						var mesh = ShapeMesh(fields[3], fields.Length >= 7 ? fields : null);
						if (mesh == null)
						{
							missing++;
							continue;
						}
						foreach (var meshFilter in model
							.GetComponentsInChildren<MeshFilter>(true)
							.Where(f => ShapePartIs(model, f.transform, fields[2])))
						{
							meshFilter.sharedMesh = mesh;
							meshes++;
						}
					}
					else if (fields[0] == "capsule")
					{
						var numbers = fields.Length >= 7 ? fields[3].Split(',') : null;
						if (numbers == null || numbers.Length != 3)
						{
							missing++;
							MelonLogger.Warning($"{S.Tag}: round shape line '{line}' is malformed; that hit zone keeps the {r.Source} shape.");
							continue;
						}
						var centre = new Vector3(float.Parse(numbers[0], inv), float.Parse(numbers[1], inv), float.Parse(numbers[2], inv));
						float radius = float.Parse(fields[4], inv), height = float.Parse(fields[5], inv);
						int axis = int.Parse(fields[6], inv);
						foreach (var capsule in model
							.GetComponentsInChildren<CapsuleCollider>(true)
							.Where(c => c.name == fields[2]))
						{
							capsule.center = centre;
							capsule.radius = radius;
							capsule.height = height;
							capsule.direction = axis;
							zones++;
						}
					}
				}
				catch (Exception exception)
				{
					missing++;
					MelonLogger.Warning($"{S.Tag}: round shape line '{line}' can't be used ({exception.GetType().Name}: {exception.Message}); that piece keeps the {r.Source} shape.");
				}
			}
			if (missing > 0 || meshes == 0)
				MelonLogger.Warning($"{S.Tag}: the {r.Id} model's shape ({r.Shape}) is missing from the mod ({missing} pieces); its rounds keep the {r.Source} shape.");
		}

		static readonly HashSet<string> _shapeFailed = new HashSet<string>();
		static Mesh ShapeMesh(string resource, string[] index)
		{
			if (_shapeMeshes.TryGetValue(resource, out var mesh) && mesh != null) return mesh;
			if (_shapeFailed.Contains(resource)) return null;
			Mesh Bad(string why)
			{
				_shapeFailed.Add(resource);
				MelonLogger.Warning($"{S.Tag}: round shape piece {resource} {why}; that part keeps the source round's shape.");
				return null;
			}
			byte[] bytes;
			using (var stream = typeof(PortAmmo).Assembly.GetManifestResourceStream(resource))
			{
				if (stream == null)
				{
					_shapeFailed.Add(resource);
					return null;
				}
				bytes = new byte[stream.Length];
				int got = 0;
				while (got < bytes.Length)
				{
					int read = stream.Read(bytes, got, bytes.Length - got);
					if (read <= 0) break;
					got += read;
				}
				if (got != bytes.Length) return Bad("is truncated");
			}
			if (bytes.Length < 8) return Bad("is truncated");
			int vertexCount = BitConverter.ToInt32(bytes, 0), triangleCount = BitConverter.ToInt32(bytes, 4);
			if (vertexCount <= 0 || triangleCount <= 0 || (long)vertexCount * 32 + (long)triangleCount * 12 + 8 != bytes.Length)
				return Bad($"has {bytes.Length} bytes for {vertexCount} vertices / {triangleCount} triangles");
			if (index != null)
			{
				if (int.TryParse(index[4], out int indexedVertexCount)
					&& int.TryParse(index[5], out int indexedTriangleCount)
					&& (indexedVertexCount != vertexCount || indexedTriangleCount != triangleCount))
					return Bad($"holds {vertexCount} / {triangleCount}, its index says {indexedVertexCount} / {indexedTriangleCount}");
				string want = index[6].Trim();
				if (want.Length > 0)
				{
					string sha;
					using (var hasher = System.Security.Cryptography.SHA1.Create())
						sha = BitConverter.ToString(hasher.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
					if (!sha.StartsWith(want, StringComparison.OrdinalIgnoreCase)) return Bad("doesn't match its index's SHA-1");
				}
			}
			using (var reader = new BinaryReader(new MemoryStream(bytes, 8, bytes.Length - 8)))
			{
				Vector3[] V3()
				{
					var vertices = new Vector3[vertexCount];
					for (int i = 0;
						i < vertexCount;
						i++) vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					return vertices;
				}
				var pos = V3();
				var nrm = V3();
				var uv = new Vector2[vertexCount];
				for (int i = 0; i < vertexCount; i++) uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
				var tri = new int[triangleCount * 3];
				for (int i = 0; i < tri.Length; i++)
				{
					tri[i] = reader.ReadInt32();
					if (tri[i] < 0 || tri[i] >= vertexCount) return Bad($"has a triangle index {tri[i]} out of 0-{vertexCount - 1}");
				}
				mesh = new Mesh
				{
					name = Path.GetFileNameWithoutExtension(resource),
					hideFlags = HideFlags.DontUnloadUnusedAsset
				};
				if (vertexCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				mesh.vertices = pos;
				mesh.normals = nrm;
				mesh.uv = uv;
				mesh.triangles = tri;
				mesh.RecalculateTangents();
				mesh.RecalculateBounds();
				mesh.UploadMeshData(true);
			}
			_shapeMeshes[resource] = mesh;
			return mesh;
		}

		static Texture2D LookTexture(string resource, Texture like)
		{
			if (_looks.TryGetValue(resource, out var texture) && (ReferenceEquals(texture, null) || texture != null)) return texture;
			byte[] buffer;
			using (var stream = typeof(PortAmmo).Assembly.GetManifestResourceStream(resource))
			{
				if (stream == null) return null;
				buffer = new byte[stream.Length];
				int got = 0;
				while (got < buffer.Length)
				{
					int read = stream.Read(buffer, got, buffer.Length - got);
					if (read <= 0) break;
					got += read;
				}
			}
			texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(resource) };
			if (!ImageConversion.LoadImage(texture, buffer, false))
			{
				Object.Destroy(texture);
				_looks[resource] = null;
				return null;
			}
			texture.filterMode = like.filterMode;
			texture.anisoLevel = like.anisoLevel;
			texture.wrapMode = like.wrapMode;
			texture.Compress(true);
			texture.Apply(false, true);
			texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_looks[resource] = texture;
			return texture;
		}
	}
}
