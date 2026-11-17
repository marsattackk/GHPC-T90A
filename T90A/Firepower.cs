using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static partial class Firepower
	{
		// 42 rounds, 22 in the carousel (Russian Wikipedia, «Т-90»; btvt.narod.ru, «Основной боевой танк Т-90А»). The mix
		// is mine: the carousel takes any mix (Suvorov, «Т-90») and I found no standard load.
		internal static readonly PortAmmo.Mix Counts = new PortAmmo.Mix { Apfsds = 22, Heat = 8, He = 8, Missile = 4 };

		// With the two Invars, the carousel's 22. The split is mine.
		internal static readonly PortAmmo.Mix CarouselCountsWithMissile = new PortAmmo.Mix { Apfsds = 12, Heat = 4, He = 4 };

		// The 3BM60 in the campaign: after four missions, at a quarter of the 3BM42's weight, scaled by the army's
		// logistics. My numbers: nothing says how common it was.
		internal const int Svinets2AfterMissions = 4;
		internal const float Svinets2Prevalence = 0.25f;

		// Eight belts of 250 rounds, 2000 in all (Russian Wikipedia, «Т-90»; the T-90M carries five boxes of 400).
		internal const int PktBelt = 250;

		internal static readonly GunSetup.Settings Gun = new GunSetup.Settings
		{
			Tag = "T-90A",
			// 24 deg/s, the turret's top speed (vpk.name, «Основной боевой танк Т-90 "Владимир"»).
			TurretSpeed = 24f,
			// I found no elevation speed for the 2E42-4. 28 deg/s is the 2E42-2's hydraulic drive, the same kind: patent
			// RU 2421679's electric drive is 1.6 times faster, at up to 45 deg/s.
			GunSpeed = 28f,
			// Depression -6 deg 13' and elevation +13 deg 47' over the nose, the T-72A manual's figures («Танк Т-72А.
			// Техническое описание и инструкция по эксплуатации», 1986).
			GunLimits = new Vector2(-(6f + 13f / 60f), 13f + 47f / 60f),
			DepressionProfile = "gun_depression.txt",   // mine, no source: barrel kept 1 cm off the hull all the way round
			// No AutoloaderCycle: the autoloader keeps the game's own T-72M1 timing, about 7.2 s a round.
			GunName = "125mm Gun 2A46M-5",
			CodexName = "T90A 2A46M-5",
		};

		internal static PortAmmo.Settings Settings() => new PortAmmo.Settings
		{
			Tag = T90AMod.FriendlyName,
			UnitKey = T90AMod.UnitKey,
			Prefix = "t90a",
			SourceUnit = "T80B",
			// Pen and drag set as a pair so each round meets the maker's 2000 m figure at 2000 m.
			Apfsds = new[]
			{
				// A round named like another mod's shares its firing table: whoever spawns first sets the aim for both.
				new PortAmmo.Round
				{
					Id = "3BM42",
					Name = "3BM42 Mango APFSDS-T",
					Source = "clip_3BM32",
					Texture = "round_3bm42_co.png",
					Atlas = "125mm_co",
					Set = ammo =>
					{
						// 578 mm at the muzzle: the maker's 230 mm at 60 deg at 2000 m (NIMI's brochure from «Армия-2019»), 460 mm
						// in line of sight, carried back through this drag. 4.85 kg without the sabot (btvt.info, «Боекомплект
						// танков Т-64/72/80/90. БОПС»), 1700 m/s the maker's; the drag is the game's 3BM32's.
						ammo.RhaPenetration = 578f;
						ammo.Mass = 4.85f;
						ammo.MuzzleVelocity = 1700f;
						ammo.Coeff = 0.16f;
					},
					Prevalence = 1f,   // the 3BM42's campaign weight, against the 3BM60's 0.25: mine
				},
				new PortAmmo.Round
				{
					Id = "3BM60",
					Name = "3BM60 Svinets-2 APFSDS-T",
					Source = "clip_3BM32",
					Texture = "round_3bm60_co.png",
					Atlas = "125mm_co",
					Shape = "3BM60",
					Set = ammo =>
					{
						// 693 mm at the muzzle: the maker's 300 mm at 60 deg at 2000 m (NIMI's brochure from «Армия-2019»; Rostec
						// to Interfax, 2019), 600 mm in line of sight, carried back through this drag.
						ammo.RhaPenetration = 693f;
						// The 4.5 kg is the core's mass from the one Russian list that gives it (vpk.name, «Основной боевой танк
						// Т-90 "Владимир"»); the flight mass isn't published. The coefficient is paired with it for a 3.5 %
						// velocity loss per 1000 m, from the same list. The 1660 m/s muzzle velocity and the 8.4 kg with sabot
						// are the maker's.
						ammo.Mass = 4.5f;
						ammo.MuzzleVelocity = 1660f;
						ammo.Coeff = 0.093f;
					},
					Prevalence = Svinets2Prevalence,
					AfterMissions = Svinets2AfterMissions,
					ScaleWithLogistics = true,
				},
			},
			DefaultApfsds = () => T90AMod.Apfsds.Value,
			ForceApfsds = () => T90AMod.ForceApfsds.Value,
			ApfsdsListName = "125mm APFSDS (T-90A)",
			MenuSuffix = " (T-90A)",
			FixedHeat = "clip_3BK18M",
			Missile = new PortAmmo.Round
			{
				Id = "9M119M",
				Name = "9M119M Invar",
				Source = "clip_9M112M",
				Texture = "round_9m119m_co.png",
				Atlas = "kobra_co",
				Shape = "9M119M",
				Set = ammo =>
				{
					// Rosoboronexport's 3UBK20 sheet: 700 mm mean at normal, a tandem warhead, a 17.2 kg missile, 100 to 5000 m.
					ammo.RhaPenetration = 700f;
					ammo.Tandem = true;
					ammo.Mass = 17.2f;
					// GHPC flies a missile at constant speed. 284 m/s gives the real 17.6 s to 5 km.
					ammo.MuzzleVelocity = 284f;
					ammo.MaximumRange = 5000f;
					ammo.ArmingDistance = 100f;
					ammo.RangedFuseTime = 5000f / 284f + 0.3f;   // just past 5 km; the 0.3 s is mine
					ammo.Guidance = AmmoType.GuidanceType.Laser;
				},
			},
			EraDefeating = new[] { "9M119M Invar" },
			TotalCounts = Counts,
			// Two Invars in the carousel and two in the hull's rear rack: my placement.
			Racks = new[]
			{
				new PortAmmo.Rack { Index = 0, Counts = CarouselCountsWithMissile, MissileSlots = new[] { 21, 20 } },
				new PortAmmo.Rack { Index = 2, MissileSlots = new[] { 10, 9 } },
			},
			GuidancePosition = new Vector3(-0.727f, 0.4631f, -5.7249f),   // the T-72M1 gunner's sight, from the fire control's parent
			GuidanceName = "T90A guidance computer",
			BeamName = "T90A 1G46 guidance beam",
			CoaxFeed = "PKT",
			CoaxRounds = T90AMod.CoaxRounds,
			CoaxBelt = PktBelt,
		};
	}
}
