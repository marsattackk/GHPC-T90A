using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class T90AAmmo
	{
		internal static int[] Counts = { 22, 8, 8, 4 };

		internal static readonly int[] CarouselCountsWithMissile = { 12, 4, 4, 0 };

		internal const int Svinets2AfterMissions = 4;
		internal const float Svinets2Prevalence = 0.25f;

		internal const int PktBelt = 250;

		internal static PortAmmo.Settings Settings() => new PortAmmo.Settings
		{
			Tag = T90AMod.FriendlyName,
			UnitKey = T90AMod.UnitKey,
			Prefix = "t90a",
			SourceUnit = "T80B",
			// Pen and drag set as a pair so each round meets the maker's 2000 m figure at 2000 m.
			Apfsds = new[]
			{
				new PortAmmo.Round
				{
					Id = "3BM42", Name = "3BM42 APFSDS-T", Source = "clip_3BM32", Texture = "round_3bm42_co.png", Atlas = "125mm_co",
					Set = a =>
					{
						a.RhaPenetration = 578f;
						a.Mass = 4.85f;
						a.MuzzleVelocity = 1700f;
						a.Coeff = 0.16f;
					},
					Prevalence = 1f,
				},
				new PortAmmo.Round
				{
					Id = "3BM60", Name = "3BM60 APFSDS-T", Source = "clip_3BM32", Texture = "round_3bm60_co.png", Atlas = "125mm_co", Shape = "3BM60",
					Set = a =>
					{
						a.RhaPenetration = 693f;
						// Placeholder, only the 8.4 kg with sabot is official.
						a.Mass = 5.12f;
						a.MuzzleVelocity = 1660f;
						a.Coeff = 0.106f;
					},
					Prevalence = Svinets2Prevalence, AfterMissions = Svinets2AfterMissions, ScaleWithLogistics = true,
				},
			},
			DefaultApfsds = () => T90AMod.Apfsds.Value,
			ApfsdsListName = "125mm APFSDS (T-90A)",
			MenuSuffix = " (T-90A)",
			FixedHeat = "clip_3BK18M",
			Missile = new PortAmmo.Round
			{
				Id = "9M119M", Name = "9M119M Invar", Source = "clip_9M112M", Texture = "round_9m119m_co.png", Atlas = "kobra_co", Shape = "9M119M",
				Set = a =>
				{
					a.RhaPenetration = 700f;
					a.Tandem = true;
					a.Mass = 17.2f;
					// GHPC flies a missile at constant speed, 284 m/s gives the real 17.6 s to 5 km.
					a.MuzzleVelocity = 284f;
					a.MaximumRange = 5000f;
					a.ArmingDistance = 100f;
					a.RangedFuseTime = 5000f / 284f + 0.3f;
					a.Guidance = AmmoType.GuidanceType.Laser;
				},
			},
			EraDefeating = new[] { "9M119M Invar" },
			TotalCounts = Counts,
			Racks = new[]
			{
				new PortAmmo.Rack { Index = 0, Counts = CarouselCountsWithMissile, MissileSlots = new[] { 21, 20 } },
				new PortAmmo.Rack { Index = 2, MissileSlots = new[] { 10, 9 } },
			},
			GuidancePosition = new Vector3(-0.727f, 0.4631f, -5.7249f),
			GuidanceName = "T90A guidance computer",
			BeamName = "T90A 1G46 guidance beam",
			CoaxFeed = "PKT",
			CoaxRounds = T90AMod.CoaxRounds,
			CoaxBelt = PktBelt,
		};
	}
}
