using System;
using System.Collections.Generic;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// The fire gate, the aim clamp and the Shtora sector, against geometry worked out by hand in the comments, never read
	// back from the code. x right, y up, z forward, angles in degrees.
	public class FcsMathTests
	{
		const double Rad = Math.PI / 180.0;

		static readonly V3 Right = new V3(1f, 0f, 0f), Up = new V3(0f, 1f, 0f), Forward = new V3(0f, 0f, 1f);

		static V3 Unit(double yaw, double pitch) => new V3(
			(float)(Math.Sin(yaw * Rad) * Math.Cos(pitch * Rad)),
			(float)Math.Sin(pitch * Rad),
			(float)(Math.Cos(yaw * Rad) * Math.Cos(pitch * Rad)));

		static double Az(V3 v) => Math.Atan2(v.x, v.z) / Rad;

		static double El(V3 v) => Math.Atan2(v.y, Math.Sqrt(v.x * v.x + v.z * v.z)) / Rad;

		static void Near(double expected, double actual, string what, double tolerance) =>
			Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: {actual} (expected {expected} +- {tolerance})");

		static float Off(V3 lay, params GunnerFcsMath.Drive[] drives) =>
			GunnerFcsMath.LaidOff(lay * 1000f, new List<GunnerFcsMath.Drive>(drives));

		static GunnerFcsMath.Drive Elevation(V3 forward) => new GunnerFcsMath.Drive(Right, forward, false);

		[Fact]
		public void Gate_reads_0_with_the_gun_on_the_lay()
		{
			Near(0, Off(Forward, Elevation(Forward)), "level on level", 1e-3);
			Near(0, Off(Unit(0, 2), Elevation(Unit(0, 2))), "2 deg up on 2 deg up", 1e-3);
		}

		[Fact]
		public void Gate_reads_the_elevation_angle_either_way()
		{
			Near(1.5, Off(Unit(0, 1.5), Elevation(Forward)), "lay above", 5e-3);
			Near(2.0, Off(Forward, Elevation(Unit(0, 2))), "lay below", 5e-3);
		}

		[Fact]
		public void Gate_ignores_a_traverse_error()
		{
			// Lay 5 deg right of a level barrel. Projected on the right axis it's straight ahead again.
			Near(0, Off(Unit(5, 0), Elevation(Forward)), "5 deg right", 1e-3);
		}

		[Fact]
		public void Gate_plane_turns_with_the_drive()
		{
			// Barrel at yaw 20, right axis (cos 20, 0, -sin 20). The lay raised 1 deg from it reads exactly 1, lowered 0.5 reads 0.5.
			var forward = Unit(20, 0);
			var right = new V3((float)Math.Cos(20 * Rad), 0f, (float)-Math.Sin(20 * Rad));
			var drive = new GunnerFcsMath.Drive(right, forward, false);
			Near(1.0, Off(Unit(20, 1), drive), "raised 1 deg", 5e-3);
			Near(0.5, Off(Unit(20, -0.5), drive), "lowered 0.5 deg", 5e-3);
		}

		[Fact]
		public void Gate_takes_the_worst_elevation_drive()
		{
			Near(3.0, Off(Forward, Elevation(Unit(0, 1)), Elevation(Unit(0, 3))), "worst of 1 and 3", 5e-3);
		}

		[Fact]
		public void Gate_skips_a_traverse_drive()
		{
			// A turret 10 deg above the lay would read the whole angle and keep the gate shut.
			var turret = new GunnerFcsMath.Drive(Right, Unit(0, 10), true);
			Near(0, Off(Forward, Elevation(Forward), turret), "traverse skipped", 1e-3);
			Near(10.0, Off(Forward, Elevation(Forward), new GunnerFcsMath.Drive(Right, Unit(0, 10), false)), "as an elevation drive", 5e-3);
			Near(0, Off(Forward, turret), "a traverse drive alone", 1e-9);
		}

		[Fact]
		public void Gate_reads_0_with_no_lay_or_no_drive()
		{
			Near(0, GunnerFcsMath.LaidOff(new V3(0f, 0f, 0f), new List<GunnerFcsMath.Drive> { Elevation(Unit(0, 5)) }), "zero lay", 1e-9);
			Near(0, GunnerFcsMath.LaidOff(Forward, new List<GunnerFcsMath.Drive>()), "no drives", 1e-9);
		}

		[Fact]
		public void Gate_angle_falls_back_to_0_1()
		{
			Near(0.1, GunnerFcsMath.GateAngle(0f), "0", 1e-6);
			Near(0.1, GunnerFcsMath.GateAngle(-1f), "negative", 1e-6);
			Near(0.25, GunnerFcsMath.GateAngle(0.25f), "set", 1e-6);
			Near(0.1, GunnerFcsMath.DefaultGateAngle, "the default", 1e-6);
		}

		[Fact]
		public void Gate_shuts_only_above_the_threshold()
		{
			Assert.True(GunnerFcsMath.GateShut(0.15f, 0f));
			Assert.False(GunnerFcsMath.GateShut(0.05f, 0f));
			Assert.False(GunnerFcsMath.GateShut(0.1f, 0f));
			Assert.False(GunnerFcsMath.GateShut(0.2f, 0.3f));
			Assert.True(GunnerFcsMath.GateShut(0.4f, 0.3f));
			Assert.True(GunnerFcsMath.GateShut(0.15f, -1f));
		}

		[Fact]
		public void Gate_on_and_off_target()
		{
			// The gun 0.5 deg under the lay: shut at the default 0.1, open with the gate at 1.
			float off = Off(Unit(0, 0.5), Elevation(Forward));
			Near(0.5, off, "off", 5e-3);
			Assert.True(GunnerFcsMath.GateShut(off, 0.1f));
			Assert.False(GunnerFcsMath.GateShut(off, 1f));
			Assert.False(GunnerFcsMath.GateShut(Off(Forward, Elevation(Forward)), 0.1f));
		}

		// The aim at az / el against the limits. before is the azimuth before the input.
		static (bool clamped, double az, double el) Clamp(double az, double el, float minEl, float maxEl, float azLimit, double before = 0)
		{
			var direction = Unit(az, el);
			bool changed = GunnerFcsMath.ClampAim(direction, Unit(before, 0), minEl, maxEl, azLimit, out var clamped);
			var result = changed ? clamped : direction;
			Near(1, Math.Sqrt(V3.Dot(result, result)), "unit length", 1e-5);
			return (changed, Az(result), El(result));
		}

		[Fact]
		public void Clamp_leaves_an_aim_inside_alone()
		{
			Assert.False(Clamp(3, 5, -10, 30, 7.5f).clamped);
			Assert.False(Clamp(0, 0, -10, 30, 0).clamped);
		}

		[Fact]
		public void Clamp_keeps_an_aim_at_a_limit()
		{
			// The sin / asin round trip in float may nudge it by a float step, no more.
			var upper = Clamp(3, 30, -10, 30, 7.5f);
			Near(30, upper.el, "upper", 1e-3);
			Near(3, upper.az, "azimuth kept", 1e-3);
			Near(-10, Clamp(3, -10, -10, 30, 7.5f).el, "lower", 1e-3);
			Near(7.5, Clamp(7.5, 5, -10, 30, 7.5f).az, "azimuth at its limit", 1e-3);
		}

		[Fact]
		public void Clamp_brings_elevation_back_and_keeps_azimuth()
		{
			var up = Clamp(3, 45, -10, 30, 7.5f);
			Assert.True(up.clamped);
			Near(30, up.el, "above", 1e-3);
			Near(3, up.az, "azimuth kept", 1e-3);
			var down = Clamp(-4, -25, -10, 30, 7.5f);
			Assert.True(down.clamped);
			Near(-10, down.el, "below", 1e-3);
			Near(-4, down.az, "azimuth kept", 1e-3);
		}

		[Fact]
		public void Clamp_treats_a_0_elevation_limit_as_open()
		{
			Assert.False(Clamp(0, -25, 0, 30, 0).clamped);
			Near(30, Clamp(0, 45, 0, 30, 0).el, "45 with upper 30", 1e-3);
			Assert.False(Clamp(0, 60, -10, 0, 0).clamped);
		}

		[Fact]
		public void Clamp_brings_azimuth_back_either_side()
		{
			var right = Clamp(20, 5, -10, 30, 7.5f);
			Assert.True(right.clamped);
			Near(7.5, right.az, "20 to +7.5", 1e-3);
			Near(5, right.el, "elevation kept", 1e-3);
			var left = Clamp(-20, 5, -10, 30, 7.5f);
			Near(-7.5, left.az, "-20 to -7.5", 1e-3);
			Near(5, left.el, "elevation kept", 1e-3);
		}

		[Fact]
		public void Clamp_with_an_azimuth_limit_alone()
		{
			var c = Clamp(40, 60, 0, 0, 8f);
			Near(8, c.az, "azimuth", 1e-3);
			Near(60, c.el, "elevation untouched", 1e-3);
		}

		[Fact]
		public void Clamp_past_both_limits()
		{
			var c = Clamp(20, 45, -10, 30, 7.5f);
			Near(7.5, c.az, "azimuth", 1e-3);
			Near(30, c.el, "elevation", 1e-3);
		}

		[Fact]
		public void Clamp_only_stops_an_aim_already_further_out_from_growing()
		{
			// Other code had the aim at 12 deg right, the limit is 7.5, so 12 is allowed. 15 is held at 12. 10 and -3 are left alone.
			var grown = Clamp(15, 5, -10, 30, 7.5f, 12);
			Assert.True(grown.clamped);
			Near(12, grown.az, "15 to 12", 1e-3);
			Assert.False(Clamp(10, 5, -10, 30, 7.5f, 12).clamped);
			Assert.False(Clamp(-3, 5, -10, 30, 7.5f, 12).clamped);
			Near(-12, Clamp(-15, 5, -10, 30, 7.5f, -12).az, "-15 to -12", 1e-3);
		}

		[Fact]
		public void Clamp_brings_straight_up_to_the_upper_limit_ahead()
		{
			Assert.True(GunnerFcsMath.ClampAim(Up, Forward, -10, 30, 0, out var clamped));
			Near(0, clamped.x, "x", 1e-6);
			Near(Math.Sin(30 * Rad), clamped.y, "y = sin 30", 1e-6);
			Near(Math.Cos(30 * Rad), clamped.z, "z = cos 30", 1e-6);
		}

		[Fact]
		public void No_limits_means_no_clamp()
		{
			Assert.False(GunnerFcsMath.HasAimLimits(0f, 0f, 0f));
			Assert.False(GunnerFcsMath.HasAimLimits(0f, 0f, -1f));
			Assert.True(GunnerFcsMath.HasAimLimits(-10f, 30f, 0f));
			Assert.True(GunnerFcsMath.HasAimLimits(0f, 0f, 7.5f));
			Assert.True(GunnerFcsMath.HasAimLimits(0f, 30f, 0f));
		}

		// Emitters 2 m up on the turret, the T-90A's sector: 20 deg either side, 2.25 up and down.
		static readonly V3 Emitters = new V3(0f, 2f, 0f);

		static (float az, float el) Bearing(V3 offset, V3 forward, V3 turretUp)
		{
			ShtoraMath.Bearing(Emitters + offset, Emitters, forward, turretUp, out float az, out float el);
			return (az, el);
		}

		static bool Covers(V3 offset, bool left = true, bool right = true)
		{
			var (az, el) = Bearing(offset, Forward, Up);
			return ShtoraMath.InSector(az, el, 20f, 2.25f, left, right);
		}

		[Fact]
		public void Bearing_ahead_left_right_behind()
		{
			// 100 m on. 10 m left: az = -atan(0.1) = -5.7106. Behind: atan2(0, -100) = 180.
			var ahead = Bearing(new V3(0f, 0f, 100f), Forward, Up);
			Near(0, ahead.az, "ahead az", 1e-4);
			Near(0, ahead.el, "ahead el", 1e-4);
			var left = Bearing(new V3(-10f, 0f, 100f), Forward, Up);
			Near(-5.7106, left.az, "left az", 1e-3);
			Near(0, left.el, "left el", 1e-4);
			Near(5.7106, Bearing(new V3(10f, 0f, 100f), Forward, Up).az, "right az", 1e-3);
			var behind = Bearing(new V3(0f, 0f, -100f), Forward, Up);
			Near(180, behind.az, "behind az", 1e-3);
			Near(0, behind.el, "behind el", 1e-4);
		}

		[Fact]
		public void Bearing_elevation_is_over_the_horizontal_range()
		{
			// (10, 2, 100): el = atan(2 / sqrt(10^2 + 100^2)) = 1.14012, az = atan(0.1) = 5.7106. Straight above: 90.
			var b = Bearing(new V3(10f, 2f, 100f), Forward, Up);
			Near(5.7106, b.az, "az", 1e-3);
			Near(1.14012, b.el, "el", 1e-3);
			Near(90, Bearing(new V3(0f, 10f, 0f), Forward, Up).el, "overhead", 1e-3);
		}

		[Fact]
		public void Bearing_is_against_the_bore_with_the_turret_up_as_vertical()
		{
			// Gun up 10 deg. A point along the bore reads 0 / 0, a level one el = atan2(-100 sin 10, 100 cos 10) = -10.
			var forward = Unit(0, 10);
			var along = Bearing(forward * 100f, forward, Up);
			Near(0, along.az, "along the bore az", 1e-3);
			Near(0, along.el, "along the bore el", 1e-3);
			var level = Bearing(new V3(0f, 0f, 100f), forward, Up);
			Near(0, level.az, "level az", 1e-3);
			Near(-10, level.el, "level el", 1e-3);
		}

		[Fact]
		public void Bearing_is_from_the_emitters_centre()
		{
			ShtoraMath.Bearing(new V3(3f, 2f, 105f), new V3(3f, 2f, 5f), Forward, Up, out float az, out float el);
			Near(0, az, "dead ahead az", 1e-4);
			Near(0, el, "dead ahead el", 1e-4);
			ShtoraMath.Bearing(new V3(13f, 2f, 105f), new V3(3f, 2f, 5f), Forward, Up, out az, out el);
			Near(5.7106, az, "10 m right of the centre", 1e-3);
		}

		[Fact]
		public void Sector_dead_ahead_belongs_to_the_left_emitter()
		{
			Assert.True(Covers(new V3(0f, 0f, 100f)));
			Assert.True(Covers(new V3(0f, 0f, 100f), left: true, right: false));
			Assert.False(Covers(new V3(0f, 0f, 100f), left: false, right: true));
		}

		[Fact]
		public void Sector_each_side_needs_its_own_emitter()
		{
			var left = new V3(-10f, 0f, 100f);
			var right = new V3(10f, 0f, 100f);
			Assert.True(Covers(left) && Covers(right));
			Assert.True(Covers(left, left: true, right: false));
			Assert.False(Covers(left, left: false, right: true));
			Assert.True(Covers(right, left: false, right: true));
			Assert.False(Covers(right, left: true, right: false));
			Assert.False(Covers(left, left: false, right: false) || Covers(right, left: false, right: false));
		}

		[Fact]
		public void Sector_does_not_cover_behind()
		{
			Assert.False(Covers(new V3(0f, 0f, -100f)));
			Assert.False(Covers(new V3(-100f, 0f, -10f)));
		}

		[Fact]
		public void Sector_stops_at_the_elevation_limit()
		{
			// 100 m ahead: 3.9 m up is atan(0.039) = 2.2334, inside 2.25. 4.0 m is atan(0.04) = 2.2906, outside. Same below.
			Assert.True(Covers(new V3(0f, 3.9f, 100f)));
			Assert.False(Covers(new V3(0f, 4.0f, 100f)));
			Assert.True(Covers(new V3(0f, -3.9f, 100f)));
			Assert.False(Covers(new V3(0f, -4.0f, 100f)));
			Assert.False(Covers(new V3(0f, 10f, 100f)));
		}

		[Fact]
		public void Sector_stops_at_the_azimuth_limit()
		{
			// atan(0.34) = 18.77 is inside 20, atan(0.5) = 26.57 isn't.
			Assert.True(Covers(new V3(34f, 0f, 100f)));
			Assert.True(Covers(new V3(-34f, 0f, 100f)));
			Assert.False(Covers(new V3(50f, 0f, 100f)));
			Assert.False(Covers(new V3(-50f, 0f, 100f)));
		}

		[Fact]
		public void Sector_limits_come_from_the_settings()
		{
			var (azFar, elFar) = Bearing(new V3(34f, 0f, 100f), Forward, Up);
			Assert.False(ShtoraMath.InSector(azFar, elFar, 10f, 2.25f, true, true));
			var (azNear, elNear) = Bearing(new V3(10f, 0f, 100f), Forward, Up);
			Assert.True(ShtoraMath.InSector(azNear, elNear, 10f, 2.25f, true, true));
			var (azUp, elUp) = Bearing(new V3(10f, 2f, 100f), Forward, Up);
			Assert.True(ShtoraMath.InSector(azUp, elUp, 20f, 2.25f, true, true));
			Assert.False(ShtoraMath.InSector(azUp, elUp, 20f, 1f, true, true));
		}
	}
}
