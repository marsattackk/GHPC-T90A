using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Utility;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class ArmorSetup
	{
		static bool _fitWarned;

		internal static void Apply(Vehicle vehicle)
		{
			if (T90AMod.Armor == null) return;
			var root = vehicle.transform;
			var hullBone = root.Find("---MESH---/HULL");
			var turretBone = root.Find("---MESH---/HULL/TURRET");
			var hullArmor = ArmorRoot(vehicle, "HULL COLLIDERS");
			var turretArmor = ArmorRoot(vehicle, "TURRET COLLIDERS");
			if (hullBone == null || turretBone == null || hullArmor == null || turretArmor == null)
			{
				MelonLogger.Error("T-90A armour: collider roots not found; armour not changed.");
				return;
			}

			var sandbar = turretArmor.Find("Composite Armor Array");
			if (sandbar != null) sandbar.gameObject.SetActive(false);
			var gunArmor = ArmorRoot(vehicle, "GUN COLLIDERS");
			var problems = new List<string>();
			foreach (var (grp, n) in new[] { (turretArmor, "Turret.002"), (turretArmor, "Turret Storage box"), (turretArmor, "Sight Shielding"), (turretArmor, "Loaders Hatch.002"), (gunArmor, "Gun Mantlet") })
			{
				var t = grp != null ? grp.Find(n) : null;
				if (t == null) { problems.Add($"'{n}' not found, not removed"); continue; }
				t.gameObject.SetActive(false);
			}
			foreach (var kv in ArmorValues.GlacisKept)
			{
				var va = hullArmor.Find(kv.Key)?.GetComponent<VariableArmor>();
				if (va == null) { problems.Add($"glacis plate '{kv.Key}' not found, not renamed"); continue; }
				va.SetName(kv.Value.name);
				var aar = va.GetComponent<AarVisual>();
				var mat = kv.Value.xray != null ? XrayMaterial(kv.Value.xray) : null;
				if (aar != null && mat != null) { aar.SwitchMaterials = true; aar.AarMaterial = mat; }
			}

			var inst = Object.Instantiate(T90AMod.Armor);
			var gunBone = turretBone.Find("GUN");
			var groups = new[] { ("HULL", hullBone, hullArmor), ("TURRET", turretBone, turretArmor), ("GUN", gunBone, gunArmor) };
			if (T90AMod.Edits != null)
			{
				var reshape = new Dictionary<string, (Mesh, Transform)>();
				foreach (var (group, bone, _) in groups)
				{
					var g = inst.transform.Find(group);
					if (g == null || bone == null) continue;
					foreach (Transform p in g)
						if (p.name.StartsWith(VanillaEdits.ReshapeKind + "|")) reshape[p.name.Substring(p.name.IndexOf('|') + 1)] = (p.GetComponent<MeshFilter>().sharedMesh, bone);
				}
				VanillaEditsApply.Apply(root, T90AMod.Edits, reshape, problems);
			}
			foreach (var (group, bone, target) in groups)
			{
				var g = inst.transform.Find(group);
				if (g == null) continue;
				if (bone == null || target == null) { MelonLogger.Warning($"T-90A armour: no {group} bone / collider group; its pieces are skipped."); continue; }
				foreach (var piece in g.Cast<Transform>().ToList())
				{
					string[] tag = piece.name.Split('|');
					if (tag[0] == VanillaEdits.ReshapeKind) continue;
					var e = ArmorValues.Get(tag[0]);
					piece.name = tag.Length > 1 ? tag[1] : piece.name;
					string name = ArmorValues.PieceName(e.Kind, piece.name);
					if (e.DrawnOnly) Xray(piece.gameObject, e);
					else Setup(piece.gameObject, e, name, vehicle);
					Followers.PlaceOnBone(piece, bone, target);
				}
			}
			Object.Destroy(inst);
			if (problems.Count > 0 && !_fitWarned)
			{
				_fitWarned = true;
				MelonLogger.Warning($"T-90A armour: {problems.Count} T-72M1 armour piece(s) could not be fitted to the T-90A ({string.Join("; ", problems)}); "
					+ "the armour may not match the model (game update?).");
			}
		}

		static readonly AccessTools.FieldRef<UniformArmor, string> Name = AccessTools.FieldRefAccess<UniformArmor, string>("_name");
		static readonly AccessTools.FieldRef<UniformArmor, ArmorCodexScriptable> Material = AccessTools.FieldRefAccess<UniformArmor, ArmorCodexScriptable>("_armorType");
		static readonly AccessTools.FieldRef<UniformArmor, bool> IsEra = AccessTools.FieldRefAccess<UniformArmor, bool>("_isEra");
		static readonly AccessTools.FieldRef<UniformArmor, bool> NoDecals = AccessTools.FieldRefAccess<UniformArmor, bool>("_noImpactDecals");

		static void Setup(GameObject go, ArmorValues.Entry e, string name, Vehicle vehicle)
		{
			go.layer = 8;
			go.tag = "Penetrable";
			var col = go.AddComponent<MeshCollider>();
			col.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
			col.convex = e.Kind == "composite";

			if (e.Variable)
			{
				var va = go.AddComponent<VariableArmor>();
				va.SetName(name);
				AccessTools.Field(typeof(VariableArmor), "_armorType").SetValue(va, e.Material);
				va.Unit = vehicle;
				Xray(go, e);
				return;
			}
			var armor = go.AddComponent<UniformArmor>();
			Name(armor) = name;
			Material(armor) = e.Material;
			IsEra(armor) = e.Era;
			NoDecals(armor) = e.Era;
			armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
			armor.PrimarySabotRha = e.Ke;
			armor.PrimaryHeatRha = e.Ce;
			armor.SecondarySabotRha = e.KeAfter;
			armor.SecondaryHeatRha = e.CeAfter;
			armor.AngleMatters = e.AngleMatters;
			armor.DetonatedObjects = new GameObject[0];
			armor.UndetonatedObjects = new GameObject[0];
			armor.Unit = vehicle;
			Xray(go, e);
		}

		static void Xray(GameObject go, ArmorValues.Entry e)
		{
			if (!ArmorValues.ShownInXray(e.Kind))
			{
				var r = go.GetComponent<Renderer>();
				if (r != null) r.enabled = false;
				return;
			}
			var aar = go.AddComponent<AarVisual>();
			aar.HideUntilAar = true;
			var mat = XrayMaterial(e.Xray ?? e.Kind);
			aar.SwitchMaterials = mat != null;
			aar.AarMaterial = mat;
			aar.UpdateVisibility();
		}

		static readonly Dictionary<string, Material> _xrayMaterials = new Dictionary<string, Material>();

		internal static Material XrayMaterial(string key)
		{
			if (_xrayMaterials.TryGetValue(key, out var m) && m != null) return m;
			string name = ArmorValues.XrayTemplate;
			var from = Resources.FindObjectsOfTypeAll<AarVisual>().FirstOrDefault(v => v.AarMaterial != null && v.AarMaterial.name == name);
			m = from != null ? from.AarMaterial : null;
			bool own = ArmorValues.XrayColours.TryGetValue(key, out var c);
			if (m != null && own) m = new Material(m) { name = c.name, color = c.colour, hideFlags = HideFlags.DontUnloadUnusedAsset };
			if (m != null) _xrayMaterials[key] = m;
			return m;
		}

		static Transform ArmorRoot(Vehicle vehicle, string token)
		{
			Transform follower = vehicle.GetComponentsInChildren<LateFollowTarget>(true)
				.SelectMany(t => t.LateFollowers)
				.Where(f => f != null && f.name.Contains(token))
				.Select(f => f.transform)
				.FirstOrDefault()
				?? vehicle.transform.Cast<Transform>().FirstOrDefault(t => t.name.Contains(token));
			if (follower == null) return null;
			return follower.Find("ARMOR") ?? follower;
		}
	}
}
