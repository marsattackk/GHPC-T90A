using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace T90A.Common
{
	internal static class SplitCharges
	{
		internal const string Suffix = " rear charge";
		static readonly Dictionary<AmmoType, AmmoType> ExtraJets = new Dictionary<AmmoType, AmmoType>();

		internal static AmmoType ExtraJetOf(AmmoType shell, float share)
		{
			if (ExtraJets.TryGetValue(shell, out var jet)) return jet;
			jet = new AmmoType();
			foreach (var field in typeof(AmmoType).GetFields(BindingFlags.Instance
				| BindingFlags.Public
				| BindingFlags.NonPublic))
				if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(jet, field.GetValue(shell));
			jet.Name = shell.Name + Suffix;
			jet.RhaPenetration = shell.RhaPenetration * share;
			jet.MuzzleVelocity = 2000f;
			jet.Mass = 0.1f;
			jet.Coeff = 0f;
			jet.TntEquivalentKg = 0f;
			jet.Tandem = false;
			jet.ArmingDistance = 0f;
			jet.VisualType = LiveRoundMarshaller.LiveRoundVisualType.Invisible;
			ExtraJets[shell] = jet;
			return jet;
		}

		internal static bool IsExtraJet(AmmoType type) => type != null && ExtraJets.ContainsValue(type);
	}

	[HarmonyPatch(typeof(LiveRound), "logHit")]
	internal static class SplitChargePatch
	{
		static readonly AccessTools.FieldRef<LiveRound, bool> JetActive = Reflect.FieldRefAccess<LiveRound, bool>("_jetActive");
		static readonly AccessTools.FieldRef<LiveRound, Vector3> JetStart = Reflect.FieldRefAccess<LiveRound, Vector3>("_jetStartPoint");
		static readonly AccessTools.FieldRef<LiveRound, bool> Armed = Reflect.FieldRefAccess<LiveRound, bool>("_armed");

		static bool Prepare() => JetActive != null && JetStart != null && Armed != null;

		static void Prefix(LiveRound __instance, out bool __state)
		{
			__state = true;
			try { __state = JetActive(__instance); }
			catch (Exception exception) { PatchGuard.Failed("Split charge", exception); }
		}

		// Never throw out of a LiveRound patch: the round fails every frame after.
		static void Postfix(LiveRound __instance, bool __state)
		{
			try { Split(__instance, __state); }
			catch (Exception exception) { PatchGuard.Failed("Split charge", exception); }
		}

		static void Split(LiveRound shell, bool wasJet)
		{
			if (wasJet || shell.IsSpall || !JetActive(shell) || PortAmmo.S?.SplitCharges == null || shell.Info == null) return;
			if (!PortAmmo.S.SplitCharges.TryGetValue(shell.Info.Name, out var rule)) return;
			float total = shell.CurrentPenRating;
			float rear = Mathf.Max(1f, total * rule.FirstShare);
			var forward = shell.transform.forward;
			var side = Vector3.Cross(Vector3.up, forward);
			if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(Vector3.forward, forward);
			var type = SplitCharges.ExtraJetOf(shell.Info, rule.FirstShare);
			var jet = LiveRoundMarshaller.Instance.GetRoundOfVisualType(LiveRoundMarshaller.LiveRoundVisualType.Invisible);
			jet.Info = type;
			jet.CurrentSpeed = type.MuzzleVelocity;
			jet.MaxSpeed = type.MuzzleVelocity;
			jet.NpcRound = shell.NpcRound;
			jet.IsSpall = true;
			jet.Shooter = shell.Shooter;
			jet.UseGravity = false;
			// starts 30 cm short of the plate so GHPC arms it into a jet there
			jet.transform.position = JetStart(shell) + side.normalized * rule.Offset - forward * 0.3f;
			jet.transform.forward = forward;
			jet.Init(shell);
			jet.gameObject.name = "rear charge " + jet.ID;
			Armed(jet) = true;
			jet.RhaPenetrationOverride = rear;
		}
	}
}
