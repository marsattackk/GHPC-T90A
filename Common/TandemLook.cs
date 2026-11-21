using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Effects;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class TandemLook
	{
		internal const float Stagger = 0.06f;
		const float StandOff = 0.3f;
		const float PrecursorDepth = 0.15f;
		static readonly Color Yellow = new Color(4f, 3.2f, 0.2f);

		static AmmoType _precursorType;

		static AmmoType PrecursorType(AmmoType shell)
		{
			if (_precursorType != null) return _precursorType;
			var type = new AmmoType();
			foreach (var field in typeof(AmmoType).GetFields(BindingFlags.Instance
				| BindingFlags.Public
				| BindingFlags.NonPublic))
				if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(type, field.GetValue(shell));
			type.Name = "tandem precursor";
			var look = shell.ImpactEffectDescriptor;
			look.HasImpactEffect = true;
			look.ImpactCategory = ParticleEffectsManager.Category.Heat;
			look.EffectSize = ParticleEffectsManager.EffectSize.Rocket;
			look.Flags = ParticleEffectsManager.ImpactModifierFlags.Small;
			type.ImpactEffectDescriptor = look;
			_precursorType = type;
			return type;
		}

		internal static bool IsPrecursor(AmmoType type) => type != null && ReferenceEquals(type, _precursorType);

		internal static bool IsTandem(LiveRound round)
		{
			var info = round?.Info;
			if (info == null || !info.Tandem || round.IsSpall || PortAmmo.S == null) return false;
			return Array.IndexOf(PortAmmo.S.EraDefeating, info.Name) >= 0
				|| (PortAmmo.S.EraRules != null && PortAmmo.S.EraRules.ContainsKey(info.Name));
		}

		internal static void RecordPrecursor(LiveRound shell, Vector3 jetStart)
		{
			if (shell.ShotInfo == null || AarController.Instance == null) return;
			var forward = shell.transform.forward;
			var start = jetStart - forward * StandOff;
			var end = jetStart + forward * PrecursorDepth;
			var shot = new GHPC.Weapons.ShotInfo(start, forward, PrecursorType(shell.Info), shell.Shooter, shell.ShotInfo);
			shot.AddShotFrame(end, forward, new GHPC.Weapons.ShotInfo.ShotFrameData { IsJet = true }, end, null);
			AarController.Instance.AddShot(shot);
		}

		internal static IEnumerator Bursts(
			GameObject main,
			AmmoType shell,
			ParticleEffectsManager.FusedStatus fused,
			ParticleEffectsManager.SurfaceMaterial surface,
			Vector3 at,
			Vector3 forward,
			float jetLength,
			PortAmmo.SplitCharge split)
		{
			var pop = ParticleEffectsManager.Instance?.CreateImpactEffectOfType(PrecursorType(shell), fused, surface, false, at - forward * StandOff);
			if (pop != null)
			{
				pop.transform.forward = forward;
				pop.GetComponentInChildren<HeatJet>(true)?.SetJetLength(StandOff + PrecursorDepth);
			}
			yield return new WaitForSeconds(Stagger);
			if (main != null) main.SetActive(true);
			if (split == null) yield break;
			yield return new WaitForSeconds(Stagger);
			var side = Vector3.Cross(Vector3.up, forward);
			if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(Vector3.forward, forward);
			var second = ParticleEffectsManager.Instance?.CreateImpactEffectOfType(shell, fused, surface, false, at + side.normalized * split.Offset);
			if (second == null) yield break;
			second.transform.forward = forward;
			second.GetComponentInChildren<HeatJet>(true)?.SetJetLength(jetLength);
		}

		internal static void MakeYellow(ShotTraceVisual visual)
		{
			visual.IsSpall = false;
			visual.IsJet = true;
			// thicker than the shell's flight line (its round's radius) and the jet (0.025) it lies inside, or they hide it
			visual.radius = 0.075f;
			JetColour(visual) = Yellow;
		}

		static readonly AccessTools.FieldRef<ShotTraceVisual, Color> JetColour = Reflect.FieldRefAccess<ShotTraceVisual, Color>("jetColor");
		internal static bool CanColour => JetColour != null;
	}

	[HarmonyPatch(typeof(LiveRound), "doImpactVFX")]
	internal static class TandemLookImpactPatch
	{
		static readonly AccessTools.FieldRef<LiveRound, bool> JetActive = Reflect.FieldRefAccess<LiveRound, bool>("_jetActive");
		static readonly AccessTools.FieldRef<LiveRound, Vector3> JetStart = Reflect.FieldRefAccess<LiveRound, Vector3>("_jetStartPoint");
		static readonly AccessTools.FieldRef<LiveRound, ParticleEffectsManager.FusedStatus> Fused =
			Reflect.FieldRefAccess<LiveRound, ParticleEffectsManager.FusedStatus>("_fusedStatus");
		static readonly AccessTools.FieldRef<LiveRound, ParticleEffectsManager.SurfaceMaterial> Surface = Reflect.FieldRefAccess<LiveRound, ParticleEffectsManager.SurfaceMaterial>("_materialHit");

		static bool Prepare() => JetActive != null && JetStart != null && Fused != null && Surface != null;

		[ThreadStatic] internal static LiveRound Current;
		[ThreadStatic] internal static GameObject Held;

		static void Prefix(LiveRound __instance, bool __0)
		{
			Current = null;
			Held = null;
			try
			{
				if (!__0 && JetActive(__instance) && TandemLook.IsTandem(__instance)) Current = __instance;
			}
			catch (Exception exception) { PatchGuard.Failed("Tandem look", exception); }
		}

		static void Postfix(LiveRound __instance)
		{
			try
			{
				var held = Held;
				var round = Current;
				Current = null;
				Held = null;
				if (round == null || held == null || round != __instance) return;
				var forward = round.transform.forward;
				float jetLength = Vector3.Distance(JetStart(round), round.transform.position);
				PortAmmo.SplitCharge split = null;
				PortAmmo.S?.SplitCharges?.TryGetValue(round.Info.Name, out split);
				MelonLoader.MelonCoroutines.Start(TandemLook.Bursts(held, round.Info, Fused(round), Surface(round), held.transform.position, forward, jetLength, split));
			}
			catch (Exception exception) { PatchGuard.Failed("Tandem look", exception); }
		}
	}

	[HarmonyPatch(typeof(ParticleEffectsManager), nameof(ParticleEffectsManager.CreateImpactEffectOfType))]
	internal static class TandemLookEffectPatch
	{
		static void Postfix(bool __3, GameObject __result)
		{
			try
			{
				if (TandemLookImpactPatch.Current == null || TandemLookImpactPatch.Held != null || __3 || __result == null) return;
				TandemLookImpactPatch.Held = __result;
				__result.SetActive(false);
			}
			catch (Exception exception) { PatchGuard.Failed("Tandem look", exception); }
		}
	}

	[HarmonyPatch(typeof(LiveRound), "logHit")]
	internal static class TandemLookRecapRecord
	{
		static readonly AccessTools.FieldRef<LiveRound, bool> JetActive = Reflect.FieldRefAccess<LiveRound, bool>("_jetActive");
		static readonly AccessTools.FieldRef<LiveRound, Vector3> JetStart = Reflect.FieldRefAccess<LiveRound, Vector3>("_jetStartPoint");

		static bool Prepare() => JetActive != null && JetStart != null;

		static void Prefix(LiveRound __instance, out bool __state)
		{
			__state = true;
			try { __state = JetActive(__instance); }
			catch (Exception exception) { PatchGuard.Failed("Tandem recap", exception); }
		}

		static void Postfix(LiveRound __instance, bool __state)
		{
			try
			{
				if (!__state && JetActive(__instance) && TandemLook.IsTandem(__instance))
					TandemLook.RecordPrecursor(__instance, JetStart(__instance));
			}
			catch (Exception exception) { PatchGuard.Failed("Tandem recap", exception); }
		}
	}

	[HarmonyPatch(typeof(AarController), "createShotTrace")]
	internal static class TandemLookRecapPatch
	{
		static readonly AccessTools.FieldRef<AarController, List<GameObject>> Traces =
			Reflect.FieldRefAccess<AarController, List<GameObject>>("_shotTraces");

		static bool Prepare() => Traces != null && TandemLook.CanColour;

		static void Prefix(AarController __instance, out int __state)
		{
			__state = -1;
			try { __state = Traces(__instance)?.Count ?? -1; }
			catch (Exception exception) { PatchGuard.Failed("Tandem recap", exception); }
		}

		static void Postfix(AarController __instance, GHPC.Weapons.ShotInfo __0, bool __1, int __state)
		{
			try
			{
				var traces = Traces(__instance);
				var type = __0?.TypeInfo;
				bool precursor = TandemLook.IsPrecursor(type), second = SplitCharges.IsExtraJet(type);
				if (!__1 || __state < 0 || traces == null || (!precursor && !second)) return;
				for (int i = __state; i < traces.Count; i++)
				{
					var visual = traces[i] != null ? traces[i].GetComponent<ShotTraceVisual>() : null;
					if (visual == null) continue;
					// read in ShotTraceVisual.Start, the frame after: set here it takes
					if (precursor) TandemLook.MakeYellow(visual);
					else
					{
						visual.IsSpall = false;
						visual.IsJet = true;
						visual.radius = 0.025f;
					}
				}
			}
			catch (Exception exception) { PatchGuard.Failed("Tandem recap", exception); }
		}
	}
}
