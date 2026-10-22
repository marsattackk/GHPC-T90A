using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.AI.Radios;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.Equipment;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;
using DC = GHPC.Equipment.DestructibleComponent;
using GHPC.PhysicsHelpers;

namespace T90A.Common
{
	internal static class ModuleBuilder
	{
		sealed class HookDef
		{
			public Type Owner; public string Field; public (string evt, string method)[] Handlers;
			public HookDef(Type owner, string field, params (string, string)[] handlers) { Owner = owner; Field = field; Handlers = handlers; }
		}

		static readonly Dictionary<string, HookDef> Hooks = new Dictionary<string, HookDef>
		{
			["ChassisDamageManager.EngineHitZone"] = new HookDef(typeof(ChassisDamageManager), "EngineHitZone", ("Destroyed", "engineDestroyed"), ("HealthChanged", "engineDamaged"), ("Repaired", "engineRepaired")),
			["ChassisDamageManager.TransmissionHitZone"] = new HookDef(typeof(ChassisDamageManager), "TransmissionHitZone", ("Destroyed", "transmissionDestroyed"), ("HealthChanged", "transmissionDamaged"), ("Repaired", "transmissionRepaired")),
			["ChassisDamageManager.RadiatorHitZone"] = new HookDef(typeof(ChassisDamageManager), "RadiatorHitZone", ("Destroyed", "radiatorDestroyed"), ("Repaired", "radiatorRepaired")),
			["ChassisDamageManager.LeftTrackHitZone"] = new HookDef(typeof(ChassisDamageManager), "LeftTrackHitZone", ("Destroyed", "leftTrackDestroyed"), ("Repaired", "leftTrackRepaired")),
			["ChassisDamageManager.RightTrackHitZone"] = new HookDef(typeof(ChassisDamageManager), "RightTrackHitZone", ("Destroyed", "rightTrackDestroyed"), ("Repaired", "rightTrackRepaired")),
			["AimablePlatform.HitZones"] = new HookDef(typeof(AimablePlatform), "HitZones", ("Destroyed", "hitZoneDestroyed"), ("Repaired", "hitZoneRepaired")),
			["FireControlSystem.ComputerComponent"] = new HookDef(typeof(FireControlSystem), "ComputerComponent", ("Destroyed", "ComputerDestroyed")),
			["FireControlSystem.LaserComponent"] = new HookDef(typeof(FireControlSystem), "LaserComponent", ("Destroyed", "LaserDestroyed")),
			["CameraSlot.HitZone"] = new HookDef(typeof(CameraSlot), "HitZone", ("Destroyed", "HandleSlotDestroyed")),
			["MissileGuidanceUnit.GuidanceComponent"] = new HookDef(typeof(MissileGuidanceUnit), "GuidanceComponent", ("Destroyed", "GuidanceDestroyed")),
			["AmmoFeed.Autoloader"] = new HookDef(typeof(AmmoFeed), "Autoloader", ("Destroyed", "AutoloaderDestroyed")),
			["WeaponSystem.BarrelHitZone"] = new HookDef(typeof(WeaponSystem), "BarrelHitZone", ("Destroyed", "SingleBarrelDestroyed")),
			["WeaponSystem.BreechHitZone"] = new HookDef(typeof(WeaponSystem), "BreechHitZone", ("Destroyed", "BreechDestroyed")),
			["Radio._radioDestructible"] = new HookDef(typeof(Radio), "_radioDestructible", ("Destroyed", "DestroyRadio")),
			["LightBandExclusiveItem.Destructible"] = new HookDef(typeof(LightBandExclusiveItem), "Destructible", ("Destroyed", "Destructible_Destroyed"), ("Repaired", "Destructible_Repaired")),
		};

		const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		static readonly FieldInfo DcHealth = AccessTools.Field(typeof(DC), "_health"), DcThreshold = AccessTools.Field(typeof(DC), "_damageThreshold"),
			DcPressure = AccessTools.Field(typeof(DC), "_pressureTolerance"), DcChildren = AccessTools.Field(typeof(DC), "_children");
		static readonly FieldInfo ArmourName = AccessTools.Field(typeof(UniformArmor), "_name"), ArmourRicochet = AccessTools.Field(typeof(UniformArmor), "_canRicochet"),
			ArmourNormalizes = AccessTools.Field(typeof(UniformArmor), "_normalizesHits"), ArmourShatter = AccessTools.Field(typeof(UniformArmor), "_canShatterLongRods");
		static readonly FieldInfo UnitStatuses = AccessTools.Field(typeof(Unit), "DamageStatuses");
		static readonly FieldInfo GroupColliders = AccessTools.Field(typeof(RaycastColliderGroup), "childColliders");

		internal sealed class ModuleSet : MonoBehaviour, IDamageStatus
		{
			internal ModuleSpec Spec;
			internal readonly List<(ModuleSpec.Module m, DC dc)> Built = new List<(ModuleSpec.Module, DC)>();
			internal readonly List<(DC dc, string text)> Lines = new List<(DC, string)>();
			internal bool Wired;
			public bool Damaged => Lines.Any(l => l.dc != null && l.dc.IsDestroyed);
			public string DamageStatus => string.Concat(Lines.Where(l => l.dc != null && l.dc.IsDestroyed).Select(l => l.text + "\n"));
		}

		internal static void Build(Vehicle vehicle, ModuleSpec spec, string label, List<string> problems)
		{
			var root = vehicle.transform;
			var set = vehicle.gameObject.GetComponent<ModuleSet>() ?? vehicle.gameObject.AddComponent<ModuleSet>();
			set.Spec = spec;
			var look = Look(root);
			foreach (var m in spec.Modules)
			{
				if (m.Existing) continue;
				try
				{
					var (group, bone) = Group(root, m.Group);
					if (group == null) { problems.Add($"'{m.Name}': no collider group '{m.Group}' on the unit; not built"); continue; }
					var go = new GameObject($"{label} module {m.Id}");
					go.SetActive(false);
					go.layer = 8;
					go.tag = "Penetrable";
					Followers.PlaceOnBone(go.transform, bone, group);
					var boneInRoot = root.worldToLocalMatrix * bone.localToWorldMatrix;
					var toBone = boneInRoot.inverse;
					foreach (var b in m.Boxes)
					{
						var box = go.AddComponent<BoxCollider>();
						box.center = toBone.MultiplyPoint3x4(b.Centre);
						box.size = b.Size;
						if (look.material != null)
						{
							var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
							UnityEngine.Object.DestroyImmediate(vis.GetComponent<Collider>());
							vis.name = "x-ray";
							vis.layer = look.layer;
							vis.transform.SetParent(go.transform, false);
							vis.transform.localPosition = box.center;
							vis.transform.localScale = box.size;
							var r = vis.GetComponent<MeshRenderer>();
							r.sharedMaterial = look.material;
							r.enabled = look.enabled;
							r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
						}
					}
					var dc = go.AddComponent<DC>();
					dc.SetName(m.Name);
					DcHealth.SetValue(dc, m.Health);
					DcThreshold.SetValue(dc, m.Threshold);
					DcPressure.SetValue(dc, m.Pressure);
					DcChildren.SetValue(dc, new List<DC>());
					if (m.Armour != null)
					{
						var ua = go.AddComponent<UniformArmor>();
						ArmourName.SetValue(ua, m.Name);
						ua.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
						ua.PrimarySabotRha = m.Armour.Ke; ua.PrimaryHeatRha = m.Armour.Ce;
						ua.AngleMatters = m.Armour.AngleMatters;
						ArmourRicochet.SetValue(ua, m.Armour.Ricochet); ArmourNormalizes.SetValue(ua, m.Armour.Normalizes); ArmourShatter.SetValue(ua, m.Armour.Shatter);
					}
					if (look.template != null)
					{
						var av = go.AddComponent<AarVisual>();
						av.RenderMode = look.template.RenderMode; av.SwitchMaterials = look.template.SwitchMaterials; av.PreserveLayerUntilAar = look.template.PreserveLayerUntilAar;
						av.HideUntilAar = look.template.HideUntilAar; av.AarMaterial = look.template.AarMaterial;
					}
					go.SetActive(true);
					RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
					set.Built.Add((m, dc));
				}
				catch (Exception e) { problems.Add($"'{m.Name}': {e.GetType().Name}: {e.Message}"); }
			}
		}

		static (Transform group, Transform bone) Group(Transform root, string name)
		{
			var f = root.GetComponentsInChildren<LateFollow>(true).FirstOrDefault(x => x.name.IndexOf(name + " COLLIDERS", StringComparison.OrdinalIgnoreCase) >= 0 && x.FollowTarget != null);
			if (f == null) return (null, null);
			return (f.transform.Find("AAR") ?? f.transform, f.FollowTarget);
		}

		static (AarVisual template, Material material, int layer, bool enabled) Look(Transform root)
		{
			foreach (var dc in root.GetComponentsInChildren<DC>(true))
			{
				if (dc.GetComponentInParent<GHPC.Crew.BodyPartComponentRegistry>(true) != null) continue;
				var av = dc.GetComponent<AarVisual>();
				var r = dc.GetComponent<Renderer>() ?? dc.GetComponentInChildren<Renderer>(true);
				if (av != null && r != null) return (av, r.sharedMaterial, r.gameObject.layer, r.enabled);
			}
			return (null, null, 8, false);
		}

		internal static void Wire(Vehicle vehicle, List<string> problems)
		{
			var set = vehicle.GetComponent<ModuleSet>();
			if (set == null || set.Wired) return;
			set.Wired = true;
			var all = UnitObjects(vehicle);
			var zones = all.SelectMany(t => t.GetComponentsInChildren<DC>(true)).Distinct().ToList();

			var main = MainWeapon(vehicle);
			foreach (var kv in set.Spec.PlatformNames)
			{
				var p = Platform(main, kv.Key);
				if (p != null) p.Name = kv.Value; else problems.Add($"platform '{kv.Key}' not found (name '{kv.Value}' not set)");
			}

			var replaced = new HashSet<DC>();
			foreach (var (m, dc) in set.Built)
				foreach (var r in m.Replaces)
				{
					var old = zones.FirstOrDefault(z => z.Name == r && !set.Built.Any(b => b.dc == z));
					if (old == null) { problems.Add($"'{m.Name}': base hit zone '{r}' to replace not found"); continue; }
					replaced.Add(old);
				}
			foreach (var old in replaced) SwitchOff(all, old);

			foreach (var (m, dc) in set.Built)
			{
				foreach (var h in m.Hooks)
				{
					try { WireHook(vehicle, set, dc, h, replaced); }
					catch (Exception e) { problems.Add($"'{m.Name}' {h.Name}: {e.GetType().Name}: {e.Message}"); }
				}
				if (m.Status != null) set.Lines.Add((dc, m.Status));
				if (m.Kind == "fuel_tank") Fuel(all, m, dc);
			}
			foreach (var m in set.Spec.Modules.Where(x => x.Existing))
			{
				var z = zones.FirstOrDefault(x => x.Name == m.Name);
				if (z == null) problems.Add($"existing module '{m.Name}' not found");
			}
			if (set.Lines.Count > 0) AddStatus(vehicle, set);
		}

		static void WireHook(Vehicle vehicle, ModuleSet set, DC dc, ModuleSpec.Hook h, HashSet<DC> replaced)
		{
			var main = MainWeapon(vehicle);
			if (h.Name == "Module.PowerDrive")
			{
				var p = Platform(main, h.Target) ?? throw new InvalidOperationException($"no platform '{h.Target}'");
				bool stab = p.Stabilized;
				dc.Destroyed += _ => { p.SetPowerActive(false); p.Stabilized = false; };
				dc.Repaired += _ => { p.SetPowerActive(true); p.Stabilized = stab; };
				set.Lines.Add((dc, $"{p.Name} power drive destroyed"));
				return;
			}
			if (h.Name == "Module.Stabiliser")
			{
				var fcs = main?.FCS ?? throw new InvalidOperationException("no main FCS");
				var mounts = fcs.Mounts.Where(x => x != null).ToArray();
				var was = mounts.Select(x => x.Stabilized).ToArray();
				dc.Destroyed += _ => { foreach (var x in mounts) x.Stabilized = false; };
				dc.Repaired += _ => { for (int i = 0; i < mounts.Length; i++) if (mounts[i].PowerEnabled) mounts[i].Stabilized = was[i]; };
				set.Lines.Add((dc, "Stabiliser destroyed"));
				return;
			}
			if (!Hooks.TryGetValue(h.Name, out var def)) throw new InvalidOperationException("unknown hook " + h.Name);
			var owner = Owner(vehicle, main, def.Owner, h.Target) ?? throw new InvalidOperationException($"no {def.Owner.Name} '{h.Target ?? "-"}'");
			var field = def.Owner.GetField(def.Field, Any);
			if (field.FieldType.IsArray)
			{
				var arr = ((DC[])field.GetValue(owner) ?? new DC[0]).Where(z => z != null && !replaced.Contains(z)).ToList();
				if (!arr.Contains(dc)) arr.Add(dc);
				field.SetValue(owner, arr.ToArray());
			}
			else
			{
				var cur = field.GetValue(owner) as DC;
				if (cur == null || replaced.Contains(cur)) field.SetValue(owner, dc);
			}
			foreach (var (evt, method) in def.Handlers)
			{
				Subscribe(dc, evt, owner, method);
			}
		}

		static void Subscribe(DC dc, string evt, object owner, string method)
		{
			var ev = typeof(DC).GetEvent(evt);
			var mi = owner.GetType().GetMethod(method, Any) ?? throw new MissingMethodException(owner.GetType().Name, method);
			var backing = (Delegate)typeof(DC).GetField(evt, Any)?.GetValue(dc);
			if (backing != null && backing.GetInvocationList().Any(d => d.Target == owner && d.Method == mi)) return;
			ev.AddEventHandler(dc, Delegate.CreateDelegate(ev.EventHandlerType, owner, mi));
		}

		static void SwitchOff(List<Transform> all, DC old)
		{
			foreach (var def in Hooks.Values)
				foreach (var owner in all.SelectMany(t => t.GetComponentsInChildren(def.Owner, true)).Distinct())
				{
					var field = def.Owner.GetField(def.Field, Any);
					if (field.FieldType.IsArray) field.SetValue(owner, ((DC[])field.GetValue(owner) ?? new DC[0]).Where(z => z != old).ToArray());
					else if (field.GetValue(owner) as DC == old) field.SetValue(owner, null);
					foreach (var (evt, method) in def.Handlers)
					{
						var mi = owner.GetType().GetMethod(method, Any);
						if (mi != null) typeof(DC).GetEvent(evt).RemoveEventHandler(old, Delegate.CreateDelegate(typeof(DC).GetEvent(evt).EventHandlerType, owner, mi));
					}
				}
			foreach (var col in old.GetComponents<Collider>())
				foreach (var g in all.SelectMany(t => t.GetComponentsInChildren<RaycastColliderGroup>(true)))
					((List<Collider>)GroupColliders.GetValue(g)).Remove(col);
			old.Hidden = true;
			if (old.GetComponent<IArmor>() == null && old.transform.childCount == 0) old.gameObject.SetActive(false);
			else foreach (var col in old.GetComponents<Collider>()) UnityEngine.Object.Destroy(col);
			foreach (var r in old.GetComponents<Renderer>()) UnityEngine.Object.Destroy(r);
			foreach (var av in old.GetComponents<AarVisual>()) UnityEngine.Object.Destroy(av);
		}

		static void Fuel(List<Transform> all, ModuleSpec.Module m, DC dc)
		{
			string want = m.Fuel != null && m.Fuel.TryGetValue("compartment", out var cn) ? cn as string : "fighting compartment";
			var comps = all.SelectMany(t => t.GetComponentsInChildren<Compartment>(true)).Distinct().Where(c => c.Name == want).ToList();
			var comp = comps.FirstOrDefault(c => c.GetComponentsInParent<LateFollow>(true).Any(f => f.name.IndexOf(m.Group + " COLLIDERS", StringComparison.OrdinalIgnoreCase) >= 0)) ?? comps.FirstOrDefault();
			if (comp == null) return;
			var go = dc.gameObject;
			go.SetActive(false);
			var fi = go.AddComponent<FlammableItem>();
			if (m.Fuel != null)
				foreach (var kv in m.Fuel)
				{
					var f = AccessTools.Field(typeof(FlammableItem), kv.Key);
					if (f != null && kv.Value is double d) f.SetValue(fi, (float)d);
					else if (f != null && kv.Value is bool b) f.SetValue(fi, b);
				}
			go.SetActive(true);
			var cluster = new FlammablesCluster { Name = m.Name };
			cluster.Items.Add(fi);
			fi.RegisterCluster(cluster);
			comp.AddCluster(cluster);
		}

		static void AddStatus(Vehicle vehicle, ModuleSet set)
		{
			var arr = (IDamageStatus[])UnitStatuses.GetValue(vehicle) ?? new IDamageStatus[0];
			if (!arr.Contains(set)) UnitStatuses.SetValue(vehicle, arr.Concat(new IDamageStatus[] { set }).ToArray());
		}

		internal static WeaponSystem MainWeapon(Vehicle v) => v.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon;

		static AimablePlatform Platform(WeaponSystem main, string target)
		{
			var mounts = main?.FCS?.Mounts?.Where(x => x != null).ToArray() ?? new AimablePlatform[0];
			if (target == "turret") return mounts.FirstOrDefault(x => x.Axis.ToString() == "Y");
			if (target == "gun") return mounts.FirstOrDefault(x => x.Axis.ToString() == "X");
			return null;
		}

		static object Owner(Vehicle v, WeaponSystem main, Type cls, string target)
		{
			if (cls == typeof(ChassisDamageManager) || cls == typeof(Radio)) return v.GetComponentInChildren(cls, true);
			if (target == "main")
			{
				if (cls == typeof(WeaponSystem)) return main;
				if (cls == typeof(FireControlSystem)) return main?.FCS;
				if (cls == typeof(MissileGuidanceUnit)) return main?.GuidanceUnit;
				if (cls == typeof(AmmoFeed)) return main?.Feed;
			}
			if (cls == typeof(AimablePlatform) && (target == "turret" || target == "gun")) return Platform(main, target);
			if (cls == typeof(CameraSlot) && (target == "gunner_day" || target == "gunner_night"))
			{
				var o = main?.FCS?.MainOptic;
				var day = o == null ? null : o.slot.IsLinkedNightSight ? o.slot.LinkedDaySight : o.slot;
				return target == "gunner_day" ? day : day?.LinkedNightSight;
			}
			var objs = UnitObjects(v).SelectMany(t => t.GetComponentsInChildren(cls, true)).Distinct();
			if (cls == typeof(CameraSlot) && target == "commander") target = "commander";
			return objs.FirstOrDefault(o => o.name.IndexOf(target ?? "", StringComparison.OrdinalIgnoreCase) >= 0
				|| (o is AimablePlatform p && p.Name.IndexOf(target ?? "", StringComparison.OrdinalIgnoreCase) >= 0));
		}

		internal static List<Transform> UnitObjects(Vehicle v)
		{
			var list = new List<Transform> { v.transform };
			foreach (var f in UnityEngine.Object.FindObjectsByType<LateFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
				if (f != null && f.FollowTarget != null && f.FollowTarget.IsChildOf(v.transform) && !f.transform.IsChildOf(v.transform)) list.Add(f.transform);
			return list;
		}
	}
}
