using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	// GHPC narrows a jet's spall cone the more spare penetration it has (LiveRound.getSpallMaxAngle), down to nothing
	// on a thin side. The port's own HEAT gets the full 80 deg off the first armour plate it holes.
	internal static class BehindArmour
	{
		internal const float WidestCone = 80f;
		const float ReferenceThickness = 50f;
		internal const float WallThickness = 10f;
		const int Remembered = 64;

		static HashSet<string> _names;
		// Keyed by round id but checked against the round itself, because GHPC pools LiveRounds. The T-72M1's far wall is
		// the same IArmor as its near one, and only the jet's figure at the plate tells them apart.
		static readonly Dictionary<int, Wall> Holed = new Dictionary<int, Wall>();
		static readonly Queue<int> HoledOrder = new Queue<int>();

		struct Wall
		{
			public LiveRound Round;
			public IArmor Armour;
			public float Penetration;
		}

		internal static bool IsOwnHeat(AmmoType type)
		{
			if (type == null || type.Category != AmmoType.AmmoCategory.ShapedCharge || PortAmmo.S == null) return false;
			if (SplitCharges.IsExtraJet(type)) return true;
			if (_names == null)
			{
				var names = new HashSet<string>();
				foreach (var round in PortAmmo.S.HeatChoice)
					names.Add(round.Name);
				if (PortAmmo.S.Heat != null) names.Add(PortAmmo.S.Heat.Name);
				_names = names;
			}
			return _names.Contains(type.Name);
		}

		static bool HoledBefore(LiveRound round, IArmor armour, float penetration)
		{
			if (!Holed.TryGetValue(round.ID, out var wall) || !ReferenceEquals(wall.Round, round)) return false;
			return !ReferenceEquals(wall.Armour, armour) || !Mathf.Approximately(wall.Penetration, penetration);
		}

		static void MarkHoled(LiveRound round, IArmor armour, float penetration)
		{
			if (Holed.TryGetValue(round.ID, out var wall) && ReferenceEquals(wall.Round, round)) return;
			Holed[round.ID] = new Wall { Round = round, Armour = armour, Penetration = penetration };
			HoledOrder.Enqueue(round.ID);
			while (HoledOrder.Count > Remembered)
				Holed.Remove(HoledOrder.Dequeue());
		}

		internal static float Cone(LiveRound round, IArmor armour, float penetration, float losThickness, float gameCone)
		{
			// NormalizesHits stands in for "armour plate": set on hull and turret plates, never on wheels, fuel tanks or stowage
			if (armour == null || !armour.NormalizesHits || !IsOwnHeat(round.Info) || HoledBefore(round, armour, penetration))
				return gameCone;
			if (losThickness >= WallThickness) MarkHoled(round, armour, penetration);
			float multiplier = 1f;
			if (armour.ArmorType != null && armour.ArmorType.ArmorType != null)
				multiplier = armour.ArmorType.ArmorType.SpallAngleMultiplier;
			float cone = WidestCone * Mathf.Clamp01(losThickness / ReferenceThickness) * multiplier;
			return Mathf.Max(gameCone, cone);
		}
	}

	[HarmonyPatch(typeof(LiveRound), "getSpallMaxAngle")]
	internal static class BehindArmourConePatch
	{
		// by position: getSpallMaxAngle(IArmor armor, float penThickness, float losThickness, bool jetWasActive, float sqrDistance)
		static void Postfix(LiveRound __instance, IArmor __0, float __1, float __2, ref float __result)
		{
			try
			{
				float cone = BehindArmour.Cone(__instance, __0, __1, __2, __result);
				__result = cone;
			}
			catch (Exception exception) { PatchGuard.Failed("Behind-armour fragments", exception); }
		}
	}

	// penCheck makes no fragments for a round flagged IsSpall, and 3BK31's rear charge is one. Made here instead.
	[HarmonyPatch(typeof(LiveRound), "penCheck")]
	internal static class BehindArmourPenPatch
	{
		// Private LiveRound members by name. If an update renames one, Prepare quietly drops the whole patch.
		static readonly AccessTools.FieldRef<LiveRound, bool> JetActive = Reflect.FieldRefAccess<LiveRound, bool>("_jetActive");
		static readonly AccessTools.FieldRef<LiveRound, Vector3> JetStart = Reflect.FieldRefAccess<LiveRound, Vector3>("_jetStartPoint");
		static readonly AccessTools.FieldRef<LiveRound, int> Level = Reflect.FieldRefAccess<LiveRound, int>("_penetrationLevel");
		static readonly MethodInfo CreateSpall = Reflect.Method(typeof(LiveRound), "createSpall");

		// penCheck writes _penetrationLevel only on armour, so a 2 left from the last plate would pass for a new hole
		const int Unset = -1;

		static bool Prepare() => JetActive != null && JetStart != null && Level != null && CreateSpall != null;

		internal struct Before
		{
			public bool ExtraJet;
			public bool WasJet;
			public int Level;
		}

		static void Prefix(LiveRound __instance, out Before __state)
		{
			__state = default;
			try
			{
				if (!__instance.IsSpall || !SplitCharges.IsExtraJet(__instance.Info)) return;
				__state.ExtraJet = true;
				__state.WasJet = JetActive(__instance);
				__state.Level = Level(__instance);
				Level(__instance) = Unset;
			}
			catch (Exception exception) { PatchGuard.Failed("Behind-armour fragments", exception); }
		}

		// Arguments in penCheck's order: Collider, surfaceNormal, roundPath, impactPoint, ITarget
		static void Postfix(LiveRound __instance, Collider __0, Vector3 __1, Vector3 __2, Vector3 __3, bool __result, Before __state)
		{
			try
			{
				if (!__state.ExtraJet) return;
				var round = __instance;
				if (Level(round) == Unset)
				{
					Level(round) = __state.Level;   // the impact decal reads it
					return;
				}
				if (__0 != null && __result && Level(round) == 2) Fragments(round, __0, __1, __2, __3, __state.WasJet);
			}
			catch (Exception exception) { PatchGuard.Failed("Behind-armour fragments", exception); }
		}

		static void Fragments(LiveRound round, Collider plate, Vector3 normal, Vector3 path, Vector3 impact, bool wasJet)
		{
			if (round.Info.NoPenSpall || !plate.TryGetComponent<IArmor>(out var armour) || armour.MaxSpallAngleCe <= 0f) return;
			float los = armour.GetLosThickness(ResistanceType.CE, impact, normal, path, true);
			float pen = round.CurrentPenRating + los;   // the jet already lost this plate in penCheck
			var origin = round.transform.position;
			if (armour.SpallForwardRatio > 0f)
			{
				float physical = armour.GetLosThickness(ResistanceType.CE, impact, normal, path, true, true);
				origin += path.normalized * armour.SpallForwardRatio * physical / 1000f;
			}
			float distance = wasJet ? (round.transform.position - JetStart(round)).sqrMagnitude : -1f;
			var own = round.ShotInfo;
			// Init takes the fragments' parent shot off this round, and the recap only draws a shot's direct children
			round.ShotInfo = own.ParentShot ?? own;
			try { CreateSpall.Invoke(round, new object[] { armour, path, normal, origin, los, pen, wasJet, distance }); }
			finally { round.ShotInfo = own; }
		}
	}
}
