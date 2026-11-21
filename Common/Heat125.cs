using System.Collections.Generic;

namespace T90A.Common
{
	internal static class Heat125
	{
		internal static PortAmmo.Round[] Choice(string suffix) => new[]
		{
			new PortAmmo.Round
			{
				Id = "3BK29M",
				Name = "3BK29M" + suffix,
				Source = "clip_3BK18M",
				Texture = "round_3bk29m_co.png",
				Atlas = "125mm_co",
				Shape = "3BK29M",
				Set = ammo =>
				{
					ammo.RhaPenetration = 700f;
					ammo.MuzzleVelocity = 915f;
					ammo.Mass = 18.4f;
					ammo.Tandem = true;
				},
				Prevalence = 1f,
			},
			new PortAmmo.Round
			{
				Id = "3BK31",
				Name = "3BK31 Start-2" + suffix,
				Source = "clip_3BK18M",
				Texture = "round_3bk31_co.png",
				Atlas = "125mm_co",
				Shape = "3BK31",
				Set = ammo =>
				{
					ammo.RhaPenetration = 800f;
					ammo.MuzzleVelocity = 905f;
					ammo.Mass = 19f;
					ammo.Tandem = true;
				},
				Prevalence = 1f,
				InCampaign = false,
			},
			new PortAmmo.Round
			{
				Id = "3BK18M",
				Name = "3BK18M" + suffix,
				Source = "clip_3BK18M",
				Set = a => { },
				Prevalence = 1f,
			},
		};

		internal static Dictionary<string, PortAmmo.EraRule> EraRules(string suffix) => new Dictionary<string, PortAmmo.EraRule>
		{
			["3BK29M" + suffix] = new PortAmmo.EraRule { AddOnOnly = true, CostAddOn = 50f },
			["3BK31 Start-2" + suffix] = new PortAmmo.EraRule { CostAddOn = 120f, CostBuiltIn = 140f },
		};

		internal static Dictionary<string, PortAmmo.SplitCharge> SplitCharges(string suffix) => new Dictionary<string, PortAmmo.SplitCharge>
		{
			["3BK31 Start-2" + suffix] = new PortAmmo.SplitCharge { FirstShare = 1f / 3f, Offset = 0.04f },
		};
	}
}
