using System;
using GHPC;

namespace T90A.Common
{
	// A line another protection system adds to GHPC's weapon text through ShtoraScreen's HUD patch, without the HUD
	// naming that system. The hard-kill APS (the T-80UM2's Drozd, the T-90M's Arena-M kit) fills these when its first
	// station sets up. A port without one doesn't ship its code.
	internal static class WeaponHudLines
	{
		internal static Func<bool> Any = () => false;

		internal static Func<Unit, (string name, string line)> For = u => (null, null);
	}
}
