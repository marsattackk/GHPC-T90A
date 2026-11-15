using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GHPC;
using GHPC.Effects;
using GHPC.Equipment;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	// Builds GHPC armour from the bundle's damage-model pieces, with armour materials of my own so the game's shared
	// ones are never touched.
	internal static partial class DmArmourApply
	{
		[HarmonyPatch(typeof(LiveRound), "penCheck")]
		static class CompartmentAdvance
		{
			static void Postfix(LiveRound __instance, Collider cObject, Vector3 roundPath, Vector3 impactPoint, bool __result)
			{
				try
				{
					if (!__result || cObject == null || cObject.GetComponent<ImportedCompartment>() == null) return;
					var direction = roundPath.normalized;
					float progress = Vector3.Dot(__instance.transform.position - impactPoint, direction);
					if (progress >= 0.01f) return;
					__instance.transform.position += direction * (0.01f - progress);
				}
				catch (System.Exception exception) { PatchGuard.Failed("Imported compartment advance", exception); }
			}
		}

		internal static readonly (string group, string bone, string colliders)[] T72Groups =
		{
			("HULL", "---MESH---/HULL", "HULL COLLIDERS"),
			("TURRET", "---MESH---/HULL/TURRET", "TURRET COLLIDERS"),
			("GUN", "---MESH---/HULL/TURRET/GUN", "GUN COLLIDERS"),
		};

		internal static readonly (string group, string bone, string colliders)[] T80BGroups =
		{
			("HULL", BaseRigT80B.HullPath, "T-80 HULL COLLIDERS"),
			("TURRET", BaseRigT80B.TurretPath, "T-80 TURRET COLLIDERS"),
			("GUN", BaseRigT80B.GunPath, "T-80 GUN COLLIDERS"),
		};

		internal static readonly (string group, string bone, string colliders)[] M60A1Groups =
		{
			("HULL", BaseRigM60A1.HullPath, BaseRigM60A1.HullGroup),
			("TURRET", BaseRigM60A1.TurretPath, BaseRigM60A1.TurretGroup),
			("GUN", BaseRigM60A1.GunPath, BaseRigM60A1.GunGroup),
		};

		internal static (string group, string bone, string colliders)[] GroupsFor(Transform root)
		{
			return T72Groups;
		}

		internal static (int count, string edits) Install(
			Vehicle vehicle,
			GameObject prefab,
			DmArmour values,
			VanillaEdits edits,
			string tag,
			List<string> problems,
			StringBuilder table = null,
			Dictionary<string, (Mesh mesh, Transform bone)> volumes = null)
		{
			var shapes = new Dictionary<string, (Mesh, Transform)>();
			var groups = GroupsFor(vehicle.transform);
			foreach (var (group, path, _) in groups)
			{
				var source = prefab.transform.Find(group);
				var bone = vehicle.transform.Find(path);
				if (source == null || bone == null) continue;
				foreach (Transform child in source)
				{
					string name = child.name.Substring(child.name.IndexOf('|') + 1);
					if (child.name.StartsWith(VanillaEdits.ReshapeKind + "|"))
						shapes[name] = (child.GetComponent<MeshFilter>().sharedMesh, bone);
					if (volumes != null && child.name.StartsWith(VanillaEdits.VolumeKind + "|"))
						volumes[name] = (child.GetComponent<MeshFilter>().sharedMesh, bone);
				}
			}
			string changed = edits == null
				? "no vanilla_edits in the bundle"
				: VanillaEditsApply.Apply(vehicle.transform, edits, shapes, problems);
			int count = Apply(vehicle, prefab, values, tag, problems, table, groups);
			return (count, changed);
		}

		internal static int EmptyVolumes(Vehicle vehicle, GameObject prefab, List<string> problems)
		{
			var flammables = vehicle.GetComponent<FlammablesManager>();
			if (flammables == null)
			{
				problems.Add("no compartment manager");
				return 0;
			}
			int count = 0;
			foreach (var (group, path, token) in GroupsFor(vehicle.transform))
			{
				var source = prefab.transform.Find(group);
				var bone = vehicle.transform.Find(path);
				var parent = ArmorRoot(vehicle, token);
				if (source == null || bone == null || parent == null) continue;
				foreach (Transform child in source)
				{
					if (!child.name.StartsWith(VanillaEdits.VolumeKind + "|")) continue;
					string name = child.name.Substring(child.name.IndexOf('|') + 1);
					if (flammables.Compartments.Any(e => e != null && e.name == name))
						continue;
					var volumeObject = new GameObject(name);
					volumeObject.SetActive(false);
					var template = flammables.Compartments
						.SelectMany(t => t.Volumes)
						.First(v => v != null);
					volumeObject.layer = template.gameObject.layer;
					volumeObject.tag = template.gameObject.tag;
					Followers.PlaceOnBone(volumeObject.transform, bone, parent);
					var collider = volumeObject.AddComponent<MeshCollider>();
					collider.sharedMesh = child.GetComponent<MeshFilter>().sharedMesh;
					collider.convex = false;
					var compartment = volumeObject.AddComponent<Compartment>();
					volumeObject.AddComponent<ImportedCompartment>();
					compartment.Name = name;
					compartment.Parent = null;
					compartment.Volumes = new Collider[] { collider };
					compartment.TotalVolume = 1f;
					compartment.Exits = new List<CompartmentExit>();
					compartment.Detachables = new List<DetachableParent>();
					compartment.Clusters = new List<FlammablesCluster>();
					compartment.UnsecuredFiresHere = false;
					volumeObject.SetActive(true);
					compartment.RegisterFlammablesManager(flammables);
					flammables.Compartments = flammables.Compartments.Concat(new[] { compartment }).ToArray();
					count++;
				}
			}
			return count;
		}

		internal static int Apply(Vehicle vehicle, GameObject prefab, DmArmour values, string tag, List<string> problems, StringBuilder table =
			null,
			(string group, string bone, string colliders)[] groups = null)
		{
			if (prefab == null || values == null)
			{
				problems.Add("no damage-model pieces or values in the bundle");
				return 0;
			}
			var root = vehicle.transform;
			var instance = Object.Instantiate(prefab);
			int count = 0;
			try
			{
				foreach (var (group, bonePath, token) in groups ?? GroupsFor(root))
				{
					var groupRoot = instance.transform.Find(group);
					if (groupRoot == null) continue;
					var bone = root.Find(bonePath);
					var target = ArmorRoot(vehicle, token);
					if (bone == null || target == null)
					{
						problems.Add($"{group}: no rig bone / collider group, {groupRoot.childCount} piece(s) left out");
						continue;
					}
					foreach (var piece in groupRoot.Cast<Transform>().ToList())
					{
						string[] fields = piece.name.Split('|');
						if (fields.Length < 2 || !fields[0].StartsWith("dm_")) continue;
						try
						{
							var entry = values[fields[1]];
							if (entry == null)
							{
								problems.Add($"'{fields[1]}': no value, left out");
								continue;
							}
							if (entry.Drawn)
							{
								piece.name = fields[1];
								Xray(piece.gameObject, entry, tag);
								Followers.PlaceOnBone(piece, bone, target);
								continue;
							}
							var codex = Codex(entry, tag);
							if (codex == null)
							{
								problems.Add($"'{fields[1]}': unknown material '{entry.Material}', left out");
								continue;
							}
							piece.name = fields[1];
							Setup(piece.gameObject, entry, codex, vehicle, tag);
							Followers.PlaceOnBone(piece, bone, target);
							count++;
							table?.AppendLine($"  {group,-6} {fields[1],-30} {entry.Name,-42} {entry.Mode,-9} {(entry.Variable ? $"KE x{entry.Ke:0.000}/mm HEAT x{entry.Ce:0.000}/mm" : $"KE {entry.Ke,4:0} mm HEAT {entry.Ce,4:0} mm")}"
								+ $"{(entry.Era ? $" (after {entry.KeAfter:0}/{entry.CeAfter:0})" : "")}{(!entry.Variable && entry.AngleMatters ? " angled" : "")}{(entry.Slat ? " slat" : "")} {entry.Material}{(entry.Xray.Length > 0 ? " x-ray " + entry.Xray : "")}  [{entry.Source}]");
						}
						catch (System.Exception exception)
						{
							problems.Add($"'{fields[1]}': failed, left out ({exception.GetType().Name}: {exception.Message})");
						}
					}
				}
			}
			finally { Object.Destroy(instance); }
			return count;
		}

		static readonly AccessTools.FieldRef<UniformArmor, string> UName = Reflect.FieldRefAccess<UniformArmor, string>("_name");
		static readonly AccessTools.FieldRef<UniformArmor, ArmorCodexScriptable> UMaterial =
			Reflect.FieldRefAccess<UniformArmor, ArmorCodexScriptable>("_armorType");
		static readonly AccessTools.FieldRef<UniformArmor, bool> UIsEra = Reflect.FieldRefAccess<UniformArmor, bool>("_isEra");
		static readonly AccessTools.FieldRef<UniformArmor, bool> UIsSlat = Reflect.FieldRefAccess<UniformArmor, bool>("_isSlatArmor");
		static readonly AccessTools.FieldRef<UniformArmor, bool> UNoDecals = Reflect.FieldRefAccess<UniformArmor, bool>("_noImpactDecals");

		static void Setup(GameObject go, DmArmour.Entry e, ArmorCodexScriptable codex, Vehicle vehicle, string tag)
		{
			go.layer = 8;
			go.tag = "Penetrable";
			var col = go.AddComponent<MeshCollider>();
			col.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
			// VariableArmor measures entry to exit through its own mesh, so it has to stay non-convex.
			col.convex = e.Convex;
			if (e.Variable)
			{
				var armor = go.AddComponent<VariableArmor>();
				armor.SetName(e.Name);
				Reflect.Field(typeof(VariableArmor), "_armorType").SetValue(armor, codex);
				armor.Unit = vehicle;
			}
			else
			{
				var armor = go.AddComponent<UniformArmor>();
				UName(armor) = e.Name;
				UMaterial(armor) = codex;
				UIsEra(armor) = e.Era;
				UIsSlat(armor) = e.Slat;
				UNoDecals(armor) = e.Era;
				armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
				armor.PrimarySabotRha = e.Ke;
				armor.PrimaryHeatRha = e.Ce;
				armor.SecondarySabotRha = e.KeAfter;
				armor.SecondaryHeatRha = e.CeAfter;
				armor.AngleMatters = e.AngleMatters;
				armor.DetonatedObjects = new GameObject[0];
				armor.UndetonatedObjects = new GameObject[0];
				armor.Unit = vehicle;
				if (e.Era) EraBricks.Register(armor, vehicle.transform);
			}
			Xray(go, e, tag);
		}

		static void Xray(GameObject go, DmArmour.Entry e, string tag)
		{
			if (e.Xray.Length == 0)
			{
				var renderer = go.GetComponent<Renderer>();
				if (renderer != null) renderer.enabled = false;
				return;
			}
			var aar = go.AddComponent<AarVisual>();
			aar.HideUntilAar = true;
			var mat = XrayMaterial(e.Xray, tag);
			aar.SwitchMaterials = mat != null;
			aar.AarMaterial = mat;
			aar.UpdateVisibility();
		}

		internal const string XrayTemplate = "see-through teal gray";
		static readonly Dictionary<string, Material> _xray = new Dictionary<string, Material>();

		internal static Material XrayMaterial(string spec, string tag)
		{
			if (_xray.TryGetValue(spec, out var material) && material != null) return material;
			// No handle on GHPC's x-ray material, but the T-72M1's cheek array carries it, so any loaded AarVisual will do.
			var from = Resources.FindObjectsOfTypeAll<AarVisual>()
				.FirstOrDefault(v => v.AarMaterial != null && v.AarMaterial.name == XrayTemplate);
			material = from != null ? from.AarMaterial : null;
			if (material == null) return null;
			if (spec != "template")
			{
				var channels = spec.Split(',').Select(t => float.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
				if (channels.Length < 3) return null;
				material = new Material(material)
				{
					name = $"{tag} see-through {spec}",
					color = new Color(channels[0], channels[1], channels[2], channels.Length > 3 ? channels[3] : 0.5f),
					hideFlags = HideFlags.DontUnloadUnusedAsset
				};
			}
			return _xray[spec] = material;
		}

		internal static readonly string[] Materials = DmArmour.Materials;
		static readonly Dictionary<string, ArmorCodexScriptable> _codex = new Dictionary<string, ArmorCodexScriptable>();

		// My own codex assets. The vanilla ones are shared by every tank in the game.
		static ArmorCodexScriptable Codex(DmArmour.Entry e, string tag)
		{
			if (!Materials.Contains(e.Material)) return null;
			string key = e.Variable
				? $"{e.Material}|{e.Ke.ToString("R", CultureInfo.InvariantCulture)}|{e.Ce.ToString("R", CultureInfo.InvariantCulture)}"
				: e.Material;
			if (_codex.TryGetValue(key, out var cached) && cached != null) return cached;
			float kineticMultiplier = e.Variable ? e.Ke : 1f, chemicalMultiplier = e.Variable ? e.Ce : 1f;
			var armorType = new ArmorType
			{
				Name = e.Material.Replace('_', ' '),
				ThicknessSource = ArmorType.RhaSource.Multipliers,
				RhaeMultiplierKe = kineticMultiplier,
				RhaeMultiplierCe = chemicalMultiplier,
				CanRicochet = false,
				NormalizesHits = true,
				CanShatterLongRods = false,
				HarderIsBetter = false,
				SpallAngleMultiplier = 1f,
				SpallPowerMultiplier = 1f,
				CrushThicknessModifier = 1f,
			};
			switch (e.Material)
			{
				case "armour_steel":
				case "mild_steel":
					bool armour = e.Material == "armour_steel";
					armorType.Name = armour ? "armor steel" : "mild steel";
					armorType.BHN = armour ? 313f : 129f;
					if (!e.Variable) armorType.ThicknessSource = ArmorType.RhaSource.BHN;
					armorType.CanShatterLongRods = armour;
					armorType.CanRicochet = true;
					break;
				case "plain": break;
				case "composite":
					armorType.CanRicochet = true;
					armorType.CanShatterLongRods = true;
					armorType.SpallAngleMultiplier = 0.5f;
					armorType.SpallPowerMultiplier = 0.5f;
					break;
				case "filling":
				case "filling_steel":
					bool steel = e.Material == "filling_steel";
					armorType.NormalizesHits = false;
					armorType.CanShatterLongRods = steel;
					armorType.BHN = steel ? 450f : 100f;
					armorType.SpallAngleMultiplier = 0.5f;
					armorType.SpallPowerMultiplier = 0.5f;
					break;
				case "liner":
					armorType.NormalizesHits = false;
					armorType.HarderIsBetter = true;
					armorType.BHN = 2f;
					armorType.SpallAngleMultiplier = 0.33f;
					armorType.SpallPowerMultiplier = 0.9f;
					armorType.CrushThicknessModifier = 0.05f;
					break;
			}
			var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
			codex.name = $"{tag} {armorType.Name}{(e.Variable ? $" x{kineticMultiplier:0.###}/{chemicalMultiplier:0.###} per mm" : "")}";
			codex.ArmorType = armorType;
			Object.DontDestroyOnLoad(codex);
			return _codex[key] = codex;
		}

		internal static Transform ArmorRoot(Vehicle vehicle, string token)
		{
			Transform follower = vehicle.GetComponentsInChildren<LateFollowTarget>(true)
				.SelectMany(f => f.LateFollowers)
				.Where(f => f != null && f.name.Contains(token))
				.Select(f => f.transform)
				.FirstOrDefault()
				?? vehicle.transform.Cast<Transform>().FirstOrDefault(c => c.name.Contains(token));
			if (follower == null) return null;
			return follower.Find("ARMOR") ?? follower;
		}
	}
}
