using T90A.Common;

namespace T90A
{
	// The T-90A's engine and gearing.
	internal static partial class Drive
	{
		const float ReverseTrackFactor = 4.52f / 4.17f;   // GHPC's tracks run ~8 % over NWH's gear formula
		// Caps are cruise-control targets. One at the real 4.18 held it at 2.4. The gearing limits it instead.
		const float ReverseCapKmh = 12.5f;

		internal static readonly Mobility.Settings Settings = new Mobility.Settings
		{
			MaxPower = 1000f,
			MaxRpm = 2000f,
			FinalDrive = 5.297f,
			// NWH clamps the up-shift to 0.95 x max rpm. The T-72M1's 2250 sat above that and it never shifted up.
			ShiftUpRpm = 1900f,
			ReverseGears = new[] { -14.35f * ReverseTrackFactor },
			MaxForwardKmh = 60f,
			ReverseCapKmh = ReverseCapKmh,
		};
	}
}
