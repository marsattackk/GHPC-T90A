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
using UnityEngine;
using DC = GHPC.Equipment.DestructibleComponent;
using GHPC.PhysicsHelpers;
using System.Runtime.CompilerServices;

namespace T90A.Common
{
	// Builds the modules (hit zones, boxes, armour, fuel) and wires them into GHPC's damage code.
	internal static class ModuleBuilder
	{
		sealed class HookDef
		{
			public Type Owner;
			public string Field;
			public (string evt, string method)[] Handlers;
			public HookDef(Type owner, string field, params (string, string)[] handlers)
			{
				Owner = owner;
				Field = field;
				Handlers = handlers;
			}
		}

		static readonly Dictionary<string, HookDef> Hooks = new Dictionary<string, HookDef>
		{
			["ChassisDamageManager.EngineHitZone"] = new HookDef(
				typeof(ChassisDamageManager),
				"EngineHitZone",
				("Destroyed", "engineDestroyed"),
				("HealthChanged", "engineDamaged"),
				("Repaired", "engineRepaired")),
			["ChassisDamageManager.TransmissionHitZone"] = new HookDef(
				typeof(ChassisDamageManager),
				"TransmissionHitZone",
				("Destroyed", "transmissionDestroyed"),
				("HealthChanged", "transmissionDamaged"),
				("Repaired", "transmissionRepaired")),
			["ChassisDamageManager.RadiatorHitZone"] = new HookDef(
				typeof(ChassisDamageManager),
				"RadiatorHitZone",
				("Destroyed", "radiatorDestroyed"),
				("Repaired", "radiatorRepaired")),
			["ChassisDamageManager.LeftTrackHitZone"] = new HookDef(
				typeof(ChassisDamageManager),
				"LeftTrackHitZone",
				("Destroyed", "leftTrackDestroyed"),
				("Repaired", "leftTrackRepaired")),
			["ChassisDamageManager.RightTrackHitZone"] = new HookDef(
				typeof(ChassisDamageManager),
				"RightTrackHitZone",
				("Destroyed", "rightTrackDestroyed"),
				("Repaired", "rightTrackRepaired")),
			["AimablePlatform.HitZones"] = new HookDef(
				typeof(AimablePlatform),
				"HitZones",
				("Destroyed", "hitZoneDestroyed"),
				("Repaired", "hitZoneRepaired")),
			["FireControlSystem.ComputerComponent"] = new HookDef(
				typeof(FireControlSystem),
				"ComputerComponent",
				("Destroyed", "ComputerDestroyed")),
			["FireControlSystem.LaserComponent"] = new HookDef(
				typeof(FireControlSystem),
				"LaserComponent",
				("Destroyed", "LaserDestroyed")),
			["CameraSlot.HitZone"] = new HookDef(typeof(CameraSlot), "HitZone", ("Destroyed", "HandleSlotDestroyed")),
			["MissileGuidanceUnit.GuidanceComponent"] = new HookDef(
				typeof(MissileGuidanceUnit),
				"GuidanceComponent",
				("Destroyed", "GuidanceDestroyed")),
			["AmmoFeed.Autoloader"] = new HookDef(typeof(AmmoFeed), "Autoloader", ("Destroyed", "AutoloaderDestroyed")),
			["WeaponSystem.BarrelHitZone"] = new HookDef(typeof(WeaponSystem), "BarrelHitZone", ("Destroyed", "SingleBarrelDestroyed")),
			["WeaponSystem.BreechHitZone"] = new HookDef(typeof(WeaponSystem), "BreechHitZone", ("Destroyed", "BreechDestroyed")),
			["Radio._radioDestructible"] = new HookDef(typeof(Radio), "_radioDestructible", ("Destroyed", "DestroyRadio")),
			["LightBandExclusiveItem.Destructible"] = new HookDef(
				typeof(LightBandExclusiveItem),
				"Destructible",
				("Destroyed", "Destructible_Destroyed"),
				("Repaired", "Destructible_Repaired")),
		};
		internal static IEnumerable<string> KnownHooks
			=> Hooks.Keys.Concat(new[] { "Module.PowerDrive", "Module.HandDrive", "Module.Stabiliser", "Module.Fuel", "Module.SmokeTube" });

		const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		static readonly FieldInfo DcHealth = Reflect.Field(typeof(DC), "_health"), DcThreshold = Reflect.Field(
			typeof(DC),
			"_damageThreshold"),
			DcPressure = Reflect.Field(typeof(DC), "_pressureTolerance"), DcChildren = Reflect.Field(typeof(DC), "_children");
		static readonly FieldInfo ArmourName = Reflect.Field(typeof(UniformArmor), "_name"), ArmourRicochet = Reflect.Field(
			typeof(UniformArmor),
			"_canRicochet"),
			ArmourNormalizes = Reflect.Field(typeof(UniformArmor), "_normalizesHits"), ArmourShatter = Reflect.Field(
				typeof(UniformArmor),
				"_canShatterLongRods");
		static readonly FieldInfo UnitStatuses = Reflect.Field(typeof(Unit), "DamageStatuses");
		static readonly FieldInfo GroupColliders = Reflect.Field(typeof(RaycastColliderGroup), "childColliders");

		internal sealed class ModuleSet : MonoBehaviour, IDamageStatus
		{
			internal ModuleSpec Spec;
			internal string Label;
			internal readonly List<(ModuleSpec.Module m, DC dc)> Built = new List<(ModuleSpec.Module, DC)>();
			internal readonly List<(DC dc, string text)> Lines = new List<(DC, string)>();
			internal readonly Dictionary<DC, VehicleSmokeManager.SmokeSlot> Tubes = new Dictionary<DC, VehicleSmokeManager.SmokeSlot>();
			internal bool Wired;
			int TubesLost => Tubes.Keys.Count(t => t != null && t.IsDestroyed);
			public bool Damaged => Lines.Any(l => l.dc != null && l.dc.IsDestroyed) || TubesLost > 0;
			public string DamageStatus
				=> string.Concat(Lines.Where(l => l.dc != null && l.dc.IsDestroyed).Select(x => x.text + "\n"))
					+ (TubesLost > 0 ? $"Smoke grenade launchers destroyed: {TubesLost} of {Tubes.Count}\n" : "");
		}

		internal static ModuleSet Build(
			Vehicle vehicle,
			ModuleSpec spec,
			string label,
			List<string> problems,
			Func<ModuleSpec.Module, bool> take = null)
		{
			var root = vehicle.transform;
			var set = vehicle.gameObject.GetComponent<ModuleSet>() ?? vehicle.gameObject.AddComponent<ModuleSet>();
			set.Spec = spec;
			set.Label = label;
			var look = Look(root);
			foreach (var module in spec.Modules)
			{
				if (module.Existing || (take != null && !take(module)) || set.Built.Any(b => b.m == module)) continue;
				try
				{
					var (group, bone) = Group(root, module.Group);
					if (group == null)
					{
						problems.Add($"'{module.Name}': no collider group '{module.Group}' on the unit; not built");
						continue;
					}
					var holder = new GameObject($"{label} module {module.Id}");
					// Set up inactive: Awake copies _health into _fullHealth, else it keeps the default 10.
					holder.SetActive(false);
					holder.layer = 8;
					// penCheck stops a round dead on any collider not tagged Penetrable, Compartment or Target.
					holder.tag = "Penetrable";
					Followers.PlaceOnBone(holder.transform, bone, group);
					var boneInRoot = root.worldToLocalMatrix * bone.localToWorldMatrix;
					float yaw = module.Boxes.Count > 0 ? module.Boxes[0].Yaw : 0f;
					if (yaw != 0f)
					{
						var turn = Quaternion.Inverse(boneInRoot.rotation) * Quaternion.AngleAxis(yaw, Vector3.up);
						holder.transform.localRotation = holder.transform.localRotation * turn;
						boneInRoot = boneInRoot * Matrix4x4.Rotate(turn);
					}
					var toBone = boneInRoot.inverse;
					float tilt = Quaternion.Angle(Quaternion.identity, boneInRoot.rotation);
					if (module.Hulls != null)
						foreach (var (hullVertices, hullTriangles) in module.Hulls)
						{
							var hullMesh = new Mesh { name = $"{label} {module.Id} hit zone" };
							hullMesh.vertices = hullVertices.Select(v => toBone.MultiplyPoint3x4(v)).ToArray();
							hullMesh.triangles = hullTriangles;
							hullMesh.RecalculateNormals();
							hullMesh.RecalculateBounds();
							var meshCollider = holder.AddComponent<MeshCollider>();
							meshCollider.sharedMesh = hullMesh;
							meshCollider.convex = true;
							if (look.material != null && module.XrayVerts == null && !module.NoXray)
							{
								var vis = new GameObject();
								vis.AddComponent<MeshFilter>().sharedMesh = hullMesh;
								vis.AddComponent<MeshRenderer>();
								XrayLook(vis, holder.transform, look);
							}
						}
					foreach (var boxSpec in module.Hulls != null ? new List<ModuleSpec.Box>() : module.Boxes)
					{
						var box = holder.AddComponent<BoxCollider>();
						box.center = toBone.MultiplyPoint3x4(boxSpec.Centre);
						box.size = boxSpec.Size;
						if (look.material != null && module.XrayVerts == null && !module.NoXray)
						{
							var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
							UnityEngine.Object.DestroyImmediate(vis.GetComponent<Collider>());
							vis.transform.localPosition = box.center;
							vis.transform.localScale = box.size;
							XrayLook(vis, holder.transform, look);
						}
					}
					if (look.material != null && module.XrayVerts != null)
					{
						var mesh = new Mesh { name = $"{label} {module.Id} x-ray" };
						mesh.vertices = module.XrayVerts.Select(v => toBone.MultiplyPoint3x4(v)).ToArray();
						mesh.triangles = module.XrayTris;
						mesh.RecalculateNormals();
						mesh.RecalculateBounds();
						var vis = new GameObject();
						vis.AddComponent<MeshFilter>().sharedMesh = mesh;
						vis.AddComponent<MeshRenderer>();
						XrayLook(vis, holder.transform, look);
					}
					var zone = holder.AddComponent<DC>();
					zone.SetName(module.Name);
					DcHealth.SetValue(zone, module.Health);
					DcThreshold.SetValue(zone, module.Threshold);
					DcPressure.SetValue(zone, module.Pressure);
					DcChildren.SetValue(zone, new List<DC>());
					if (module.Armour != null)
					{
						var uniformArmor = holder.AddComponent<UniformArmor>();
						ArmourName.SetValue(uniformArmor, module.Name);
						uniformArmor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
						uniformArmor.PrimarySabotRha = module.Armour.Ke;
						uniformArmor.PrimaryHeatRha = module.Armour.Ce;
						uniformArmor.AngleMatters = module.Armour.AngleMatters;
						ArmourRicochet.SetValue(uniformArmor, module.Armour.Ricochet);
						ArmourNormalizes.SetValue(uniformArmor, module.Armour.Normalizes);
						ArmourShatter.SetValue(uniformArmor, module.Armour.Shatter);
					}
					if (look.template != null && !module.NoXray)
					{
						var visual = holder.AddComponent<AarVisual>();
						visual.RenderMode = look.template.RenderMode;
						visual.SwitchMaterials = look.template.SwitchMaterials;
						visual.PreserveLayerUntilAar = look.template.PreserveLayerUntilAar;
						visual.HideUntilAar = look.template.HideUntilAar;
						visual.AarMaterial = AarLook(module, look.template.AarMaterial);
					}
					holder.SetActive(true);
					RaycastColliderGroup.AddNewColliderSetToParentGroup(holder.transform);
					set.Built.Add((module, zone));
					var centre = module.Bounds.center;
				}
				catch (Exception exception) { problems.Add($"'{module.Name}': {exception.GetType().Name}: {exception.Message}"); }
			}
			return set;
		}

		static (Transform group, Transform bone) Group(Transform root, string name)
		{
			var follower = root
				.GetComponentsInChildren<LateFollow>(true)
				.FirstOrDefault(c => RigBones.IsColliderGroup(c.name, name) && c.FollowTarget != null);
			if (follower == null) return (null, null);
			return (follower.transform.Find("AAR") ?? follower.transform, follower.FollowTarget);
		}

		static readonly Dictionary<string, Material> AarLooks = new Dictionary<string, Material>();
		static Material AarLook(ModuleSpec.Module m, Material unitLook)
		{
			if (m.XrayMaterial == null && m.XrayColour == null) return unitLook;
			string key = m.XrayMaterial + "|" + (m.XrayColour == null ? "" : string.Join(",", m.XrayColour));
			if (AarLooks.TryGetValue(key, out var had) && had != null) return had;
			Material found = null;
			if (m.XrayMaterial != null)
				foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
					if (mat != null && mat.name == m.XrayMaterial)
					{
						found = mat;
						break;
					}
			if (found == null && m.XrayColour != null && unitLook != null)
			{
				found = new Material(unitLook) { name = $"module x-ray {m.Id}" };
				var colour = m.XrayColour;
				found.color = new Color(colour[0], colour[1], colour[2], colour.Length > 3 ? colour[3] : unitLook.color.a);
			}
			if (found == null) found = unitLook;
			AarLooks[key] = found;
			return found;
		}

		static void XrayLook(GameObject vis, Transform zone, (AarVisual template, Material material, int layer, bool enabled) look)
		{
			vis.name = "x-ray";
			vis.layer = look.layer;
			vis.transform.SetParent(zone, false);
			var meshRenderer = vis.GetComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = look.material;
			meshRenderer.enabled = look.enabled;
			meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}

		static (AarVisual template, Material material, int layer, bool enabled) Look(Transform root)
		{
			foreach (var zone in root.GetComponentsInChildren<DC>(true))
			{
				if (zone.GetComponentInParent<GHPC.Crew.BodyPartComponentRegistry>(true) != null) continue;
				var visual = zone.GetComponent<AarVisual>();
				var renderer = zone.GetComponent<Renderer>() ?? zone.GetComponentInChildren<Renderer>(true);
				if (visual != null && renderer != null)
					return (visual, renderer.sharedMaterial, renderer.gameObject.layer, renderer.enabled);
			}
			return (null, null, 8, false);
		}

		// GHPC's owners subscribe to their hit zone fields once, in Awake / Start / OnEnable. Set later, it's wired by hand.
		internal static string Wire(Vehicle vehicle, List<string> problems)
		{
			var set = vehicle.GetComponent<ModuleSet>();
			if (set == null || set.Wired) return "";
			set.Wired = true;
			var label = set.Label;
			var all = UnitObjects(vehicle);
			var zones = all.SelectMany(p => p.GetComponentsInChildren<DC>(true)).Distinct().ToList();
			var lines = new List<string>();

			var main = MainWeapon(vehicle);
			foreach (var pair in set.Spec.PlatformNames)
			{
				var platform = Platform(main, pair.Key);
				if (platform != null) platform.Name = pair.Value;
				else problems.Add($"platform '{pair.Key}' not found (name '{pair.Value}' not set)");
			}

			var replaced = new HashSet<DC>();
			foreach (var (replacing, builtZone) in set.Built)
				foreach (var oldName in replacing.Replaces)
				{
					var old = zones.FirstOrDefault(c => c.Name == oldName
						&& !replaced.Contains(c)
						&& !set.Built.Any(b => b.dc == c));
					if (old == null)
					{
						problems.Add($"'{replacing.Name}': base hit zone '{oldName}' to replace not found");
						continue;
					}
					replaced.Add(old);
				}
			foreach (var old in replaced) SwitchOff(vehicle, all, old);

			foreach (var (builtModule, zone) in set.Built)
			{
				var notes = new List<string>();
				foreach (var hook in builtModule.Hooks)
				{
					try { notes.Add(WireHook(vehicle, set, builtModule, zone, hook, replaced)); }
					catch (Exception exception)
					{
						problems.Add($"'{builtModule.Name}' {hook.Name}: {exception.GetType().Name}: {exception.Message}");
						notes.Add($"{hook.Name} FAILED");
					}
				}
				if (builtModule.Status != null) set.Lines.Add((zone, builtModule.Status));
				if (builtModule.Kind == "fuel_tank") notes.Add(Fuel(vehicle, all, builtModule, zone));
				if (builtModule.Hooks.Count == 0)
					notes.Add("hit report only" + (builtModule.Status != null ? $", status '{builtModule.Status}'" : ""));
				lines.Add($"'{builtModule.Name}': "
					+ string.Join("; ", notes)
					+ (builtModule.Replaces.Count > 0
						? $"; replaces {string.Join(", ", builtModule.Replaces.Select(r => "'" + r + "'"))}"
						: ""));
			}
			foreach (var module in set.Spec.Modules.Where(c => c.Existing))
			{
				var existingZone = zones.FirstOrDefault(c => c.Name == module.Name);
				if (existingZone == null) problems.Add($"existing module '{module.Name}' not found");
			}
			if (set.Lines.Count > 0 || set.Tubes.Count > 0) AddStatus(vehicle, set);
			return $"{set.Built.Count} built, {set.Spec.Modules.Count(c => c.Existing)} existing, {replaced.Count} base hit zones replaced, {set.Lines.Count} status lines";
		}

		static string WireHook(Vehicle vehicle, ModuleSet set, ModuleSpec.Module m, DC dc, ModuleSpec.Hook h, HashSet<DC> replaced)
		{
			var main = MainWeapon(vehicle);
			// GHPC has no power-drive hit zone of its own: this takes the engine-power-lost path.
			if (h.Name == "Module.PowerDrive")
			{
				var platform = Platform(main, h.Target) ?? throw new InvalidOperationException($"no platform '{h.Target}'");
				bool stab = platform.Stabilized;
				dc.Destroyed += _ =>
				{
					platform.SetPowerActive(false);
					platform.Stabilized = false;
				};
				dc.Repaired += _ =>
				{
					platform.SetPowerActive(true);
					platform.Stabilized = stab;
				};
				set.Lines.Add((dc, $"{platform.Name} power drive destroyed"));
				return $"power drive of '{platform.Name}' (powered {platform.SpeedPowered:0.#} deg/s, unpowered {platform.SpeedUnpowered:0.#}, stabilised {stab})";
			}
			if (h.Name == "Module.HandDrive")
			{
				var handPlatform = Platform(main, h.Target) ?? throw new InvalidOperationException($"no platform '{h.Target}'");
				float hand = handPlatform.SpeedUnpowered;
				dc.Destroyed += _ => handPlatform.SpeedUnpowered = 0f;
				dc.Repaired += _ => handPlatform.SpeedUnpowered = hand;
				set.Lines.Add((dc, $"{handPlatform.Name} hand drive destroyed"));
				return $"hand drive of '{handPlatform.Name}' (unpowered {hand:0.#} deg/s, 0 while destroyed)";
			}
			// Stabilized is the capability, StabilizerActive only the player's switch.
			if (h.Name == "Module.Stabiliser")
			{
				var fcs = main?.FCS ?? throw new InvalidOperationException("no main FCS");
				var mounts = fcs.Mounts.Where(mount => mount != null).ToArray();
				var was = mounts.Select(mount => mount.Stabilized).ToArray();
				dc.Destroyed += _ =>
				{
					foreach (var mount in mounts) mount.Stabilized = false;
				};
				dc.Repaired += _ =>
				{
					for (int i = 0; i < mounts.Length; i++)
						if (mounts[i].PowerEnabled) mounts[i].Stabilized = was[i];
				};
				set.Lines.Add((dc, "Stabiliser destroyed"));
				return $"stabiliser of {string.Join(", ", mounts.Select(mount => "'" + mount.Name + "'"))}";
			}
			if (h.Name == "Module.SmokeTube")
			{
				// By tube number: just after spawn the turret's collider group still lags the moved pivot by 7-11 cm.
				var smoke = vehicle.GetComponentInChildren<VehicleSmokeManager>(true)
					?? throw new InvalidOperationException("no smoke manager");
				var slot = SmokeLaunchers.SlotOfTube(smoke, m.Tube)
					?? throw new InvalidOperationException($"no grenade slot seated on tube {m.Tube}");
				var taken = set.Tubes.FirstOrDefault(t => t.Value == slot).Key;
				if (taken != null) throw new InvalidOperationException($"slot already taken by '{taken.Name}'");
				set.Tubes[dc] = slot;
				int held = 0;
				dc.Destroyed += _ =>
				{
					held += slot.Rounds;
					slot.Rounds = 0;
				};
				dc.Repaired += _ =>
				{
					slot.Rounds = Math.Max(slot.Rounds, held);
					held = 0;
				};
				return $"tube {m.Tube}";
			}
			if (!Hooks.TryGetValue(h.Name, out var def)) throw new InvalidOperationException("unknown hook " + h.Name);
			var owner = Owner(vehicle, main, def.Owner, h.Target)
				?? throw new InvalidOperationException($"no {def.Owner.Name} '{h.Target ?? "-"}'");
			var field = def.Owner.GetField(def.Field, Any);
			string fieldNote;
			if (field.FieldType.IsArray)
			{
				var zones = ((DC[])field.GetValue(owner) ?? new DC[0]).Where(z => z != null && !replaced.Contains(z)).ToList();
				if (!zones.Contains(dc)) zones.Add(dc);
				field.SetValue(owner, zones.ToArray());
				fieldNote = $"{def.Field}[{zones.Count}]";
			}
			else
			{
				var cur = field.GetValue(owner) as DC;
				if (cur == null || replaced.Contains(cur))
				{
					field.SetValue(owner, dc);
					fieldNote = def.Field + (cur == null ? " set" : $" set (was '{cur.Name}')");
				}
				else fieldNote = $"{def.Field} kept '{cur.Name}', handler added";
			}
			int added = 0;
			foreach (var (evt, method) in def.Handlers)
			{
				bool fresh = Subscribe(dc, evt, owner, method);
				if (fresh) added++;
			}
			string who = owner is Component component ? component.name : owner.ToString();
			bool awake = !(owner is Behaviour behaviour) || behaviour.isActiveAndEnabled;
			return $"{h.Name} -> '{who}' ({fieldNote}; {(added > 0 ? $"{added} handler(s) added" : "handlers already subscribed")}{(awake ? "" : "; owner inactive: its own Awake / OnEnable may subscribe again")})";
		}

		static bool Subscribe(DC dc, string evt, object owner, string method)
		{
			var eventInfo = typeof(DC).GetEvent(evt);
			var methodInfo = owner.GetType().GetMethod(method, Any) ?? throw new MissingMethodException(owner.GetType().Name, method);
			var backing = (Delegate)typeof(DC).GetField(evt, Any)?.GetValue(dc);
			if (backing != null && backing.GetInvocationList().Any(h => h.Target == owner && h.Method == methodInfo))
				return false;
			eventInfo.AddEventHandler(dc, Delegate.CreateDelegate(eventInfo.EventHandlerType, owner, methodInfo));
			return true;
		}

		static void SwitchOff(Vehicle vehicle, List<Transform> all, DC old)
		{
			foreach (var def in Hooks.Values)
				foreach (var owner in all.SelectMany(p => p.GetComponentsInChildren(def.Owner, true)).Distinct())
				{
					var field = def.Owner.GetField(def.Field, Any);
					if (field.FieldType.IsArray)
						field.SetValue(owner, ((DC[])field.GetValue(owner) ?? new DC[0]).Where(z => z != old).ToArray());
					else if (field.GetValue(owner) as DC == old) field.SetValue(owner, null);
					foreach (var (evt, method) in def.Handlers)
					{
						var methodInfo = owner.GetType().GetMethod(method, Any);
						if (methodInfo != null)
							typeof(DC)
								.GetEvent(evt)
								.RemoveEventHandler(
									old,
									Delegate.CreateDelegate(typeof(DC).GetEvent(evt).EventHandlerType, owner, methodInfo));
					}
				}
			foreach (var col in old.GetComponents<Collider>())
				foreach (var colliderGroup in all.SelectMany(part =>
					part.GetComponentsInChildren<RaycastColliderGroup>(true)))
					((List<Collider>)GroupColliders.GetValue(colliderGroup)).Remove(col);
			old.Hidden = true;
			if (old.GetComponent<IArmor>() == null && old.transform.childCount == 0) old.gameObject.SetActive(false);
			else foreach (var col in old.GetComponents<Collider>()) UnityEngine.Object.Destroy(col);
			foreach (var renderer in old.GetComponents<Renderer>()) UnityEngine.Object.Destroy(renderer);
			foreach (var visual in old.GetComponents<AarVisual>()) UnityEngine.Object.Destroy(visual);
		}

		static string Fuel(Vehicle vehicle, List<Transform> all, ModuleSpec.Module m, DC dc)
		{
			string want = m.Fuel != null && m.Fuel.TryGetValue("compartment", out var compartmentValue)
				? compartmentValue as string
				: "fighting compartment";
			var comps = all
				.SelectMany(p => p.GetComponentsInChildren<Compartment>(true))
				.Distinct()
				.Where(c => c.Name == want)
				.ToList();
			var comp = comps.FirstOrDefault(c => c
				.GetComponentsInParent<LateFollow>(true)
				.Any(f => RigBones.IsColliderGroup(f.name, m.Group)))
				?? comps.FirstOrDefault();
			if (comp == null) return $"FUEL: no compartment '{want}'";
			var holder = dc.gameObject;
			holder.SetActive(false);
			var flammable = holder.AddComponent<FlammableItem>();
			if (m.Fuel != null)
				foreach (var pair in m.Fuel)
				{
					var field = Reflect.Field(typeof(FlammableItem), pair.Key);
					if (field != null && pair.Value is double amount) field.SetValue(flammable, (float)amount);
					else if (field != null && pair.Value is bool flag) field.SetValue(flammable, flag);
				}
			holder.SetActive(true);
			var cluster = new FlammablesCluster { Name = m.Name };
			cluster.Items.Add(flammable);
			flammable.RegisterCluster(cluster);
			comp.AddCluster(cluster);
			return $"fire source in '{comp.Name}' ({comp.name}), own cluster '{cluster.Name}'";
		}

		static void AddStatus(Vehicle vehicle, ModuleSet set)
		{
			var statuses = (IDamageStatus[])UnitStatuses.GetValue(vehicle) ?? new IDamageStatus[0];
			if (!statuses.Contains(set)) UnitStatuses.SetValue(vehicle, statuses.Concat(new IDamageStatus[] { set }).ToArray());
		}

		internal static WeaponSystem MainWeapon(Vehicle v) => v.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault()?.Weapon;

		static AimablePlatform Platform(WeaponSystem main, string target)
		{
			var mounts = main?.FCS?.Mounts?.Where(m => m != null).ToArray() ?? new AimablePlatform[0];
			if (target == "turret") return mounts.FirstOrDefault(m => m.Axis.ToString() == "Y");
			if (target == "gun") return mounts.FirstOrDefault(m => m.Axis.ToString() == "X");
			return null;
		}

		static readonly ConditionalWeakTable<FireControlSystem, CameraSlot> GunnerDay =
			new ConditionalWeakTable<FireControlSystem, CameraSlot>();

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
				// Kept from the first lookup: GHPC repoints MainOptic at whichever sight is in view.
				var fcs = main?.FCS;
				if (fcs == null) return null;
				if (!GunnerDay.TryGetValue(fcs, out var day))
				{
					var optic = fcs.MainOptic;
					day = optic == null ? null : optic.slot.IsLinkedNightSight ? optic.slot.LinkedDaySight : optic.slot;
					if (day != null) GunnerDay.Add(fcs, day);
				}
				return target == "gunner_day" ? day : day?.LinkedNightSight;
			}
			var objs = UnitObjects(v).SelectMany(p => p.GetComponentsInChildren(cls, true)).Distinct();
			if (cls == typeof(CameraSlot) && target == "commander") target = "commander";
			return objs.FirstOrDefault(c => c.name.IndexOf(target ?? "", StringComparison.OrdinalIgnoreCase) >= 0
				|| (c is AimablePlatform platform && platform.Name.IndexOf(target ?? "", StringComparison.OrdinalIgnoreCase) >= 0));
		}

		// LateFollow.Start moves the collider groups to the scene root, so GetComponentsInChildren misses them.
		internal static List<Transform> UnitObjects(Vehicle v)
		{
			var list = new List<Transform> { v.transform };
			foreach (var follower in UnityEngine.Object.FindObjectsByType<LateFollow>(
				FindObjectsInactive.Include,
				FindObjectsSortMode.None))
				if (follower != null
					&& follower.FollowTarget != null
					&& follower.FollowTarget.IsChildOf(v.transform)
					&& !follower.transform.IsChildOf(v.transform))
					list.Add(follower.transform);
			return list;
		}
	}
}
