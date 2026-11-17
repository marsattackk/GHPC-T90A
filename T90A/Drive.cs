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
			// The V-92S2's 1000 hp at 2000 rpm (Suvorov, «Т-90. Первый серийный российский танк»; Russian Wikipedia, «Т-90»).
			MaxPower = 1000f,
			MaxRpm = 2000f,
			// Solved for the T-72A manual's speeds at 2000 rpm, 7.32 to 60 km/h: its 0.706 input gear and 5.454 final drive
			// on a 14-tooth, 137 mm sprocket, folded into GHPC's 0.42 m wheels («Танк Т-72А. Техническое описание», 1986).
			FinalDrive = 5.297f,
			// NWH clamps the up-shift to 0.95 x max rpm. The T-72M1's 2250 sat above that and it never shifted up.
			ShiftUpRpm = 1900f,
			// The T-72A manual's reverse, 14.35, raised by the ~8 % GHPC's tracks run fast: its 4.18 km/h at 2000 rpm.
			ReverseGears = new[] { -14.35f * ReverseTrackFactor },
			// 60 km/h on a hard road (btvt.narod.ru, «Основной боевой танк Т-90А»), the T-72A manual's 7th gear at 2000 rpm.
			MaxForwardKmh = 60f,
			ReverseCapKmh = ReverseCapKmh,
		};
	}
}
