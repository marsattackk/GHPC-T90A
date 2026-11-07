using System;

namespace T90A.Common
{
	internal struct V3
	{
		public float x, y, z;
		public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

		public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
		public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
		public static V3 operator *(V3 a, float k) => new V3(a.x * k, a.y * k, a.z * k);
		public static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
		public float Length => (float)Math.Sqrt(x * x + y * y + z * z);
		public static V3 FromAzEl(float az, float el)
		{
			double a = az * Math.PI / 180.0, e = el * Math.PI / 180.0;
			return new V3((float)(Math.Sin(a) * Math.Cos(e)), (float)Math.Sin(e), (float)(Math.Cos(a) * Math.Cos(e)));
		}

		public float Az => (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
		public float El => (float)(Math.Atan2(y, Math.Sqrt(x * x + z * z)) * 180.0 / Math.PI);
		public override string ToString() => $"({x:0.00}, {y:0.00}, {z:0.00})";
	}

	internal static class ApsMath
	{
		internal enum Threat { Spall, Atgm, Grenade, Heat, He, Kinetic, Other }

		internal static Threat ClassOf(bool spall, bool guided, int category, string name, string[] grenadePrefixes)
		{
			if (spall) return Threat.Spall;
			if (guided) return Threat.Atgm;
			if (category == 0)
			{
				if (name != null && grenadePrefixes != null)
					foreach (var p in grenadePrefixes) if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return Threat.Grenade;
				return Threat.Heat;
			}
			if (category == 2) return Threat.He;
			if (category == 1) return Threat.Kinetic;
			return Threat.Other;
		}

		internal static float Diameter(float sectionalArea) => sectionalArea > 0f ? 2f * (float)Math.Sqrt(sectionalArea / Math.PI) : 0f;

		internal static string Window(Geometry g, float speed, float diameter, bool spall)
		{
			if (spall) return "spall";
			if (speed > g.SpeedMax) return $"speed {speed:0} > {g.SpeedMax:0} m/s";
			if (speed < g.SpeedMin) return $"speed {speed:0} < {g.SpeedMin:0} m/s";
			if (diameter < g.SizeMin) return $"size {diameter * 1000f:0} mm < {g.SizeMin * 1000f:0} mm";
			return null;
		}

		internal sealed class Block
		{
			public string Name;
			public V3 Mouth;
			public float AxisAz, AxisEl;
			public float AzMin, AzMax;
			public int Side;
			public int Rounds = 2;
			public V3 Axis => V3.FromAzEl(AxisAz, AxisEl);
			public V3 Burst(float range) => Mouth + Axis * range;
		}

		internal sealed class Geometry
		{
			public Block[] Blocks = new Block[0];
			public V3 Origin;
			public float ElMin = -6f, ElMax = 20f;
			public float TrackRange = 130f, MinRange = 11f;
			public float SpeedMin = 70f, SpeedMax = 700f;
			public float SizeMin = 0.036f;
			public float ReactionTime = 0.02f;
			public float RoundSpeed = 190f;
			public float InterceptRange = 6.7f;
			public float BurstMin = 6f, BurstMax = 8f;
			public float FragReach = 1.5f;
			public float ConeHalf = 15f, ConeReach = 3f;
			public float Cooldown = 0.35f, OtherSide = 0.035f;
		}

		internal static float BurstRange(Geometry g, Block b, V3 p, V3 d)
		{
			var a = b.Axis; var w0 = b.Mouth - p;
			float k = V3.Dot(a, d), denom = 1f - k * k;
			if (denom < 1e-6f) return g.InterceptRange;
			float u = (k * V3.Dot(w0, d) - V3.Dot(w0, a)) / denom;
			return Math.Max(g.BurstMin, Math.Min(g.BurstMax, u));
		}

		internal sealed class State
		{
			public int[] Rounds;
			public bool[] Lost;
			public float[] SideFired = { float.NegativeInfinity, float.NegativeInfinity };
			public State(Geometry g)
			{
				Rounds = new int[g.Blocks.Length]; Lost = new bool[g.Blocks.Length];
				for (int i = 0; i < Rounds.Length; i++) Rounds[i] = g.Blocks[i].Rounds;
			}

			public int SideRounds(Geometry g, int side) { int n = 0; for (int i = 0; i < Rounds.Length; i++) if (g.Blocks[i].Side == side && !Lost[i]) n += Rounds[i]; return n; }

			public int Total { get { int n = 0; for (int i = 0; i < Rounds.Length; i++) if (!Lost[i]) n += Rounds[i]; return n; } }
		}

		internal sealed class Track
		{
			public float SeenAt = -1f;
			public int Block = -1;
			public bool Committed, Done;
			public float BurstAt;
			public V3 BurstPoint;
			public float BurstRange;
			public string Why;
			public bool Seen => SeenAt >= 0f;
		}

		internal enum Act { None, Seen, Fire, Intercept, Drop }

		internal struct Result
		{
			public Act Act;
			public V3 Hit;
			public float Moved;
			public float Miss;
			public bool Fired;
			public bool InField;
		}

		internal static int BlockFor(Geometry g, float az)
		{
			for (int i = 0; i < g.Blocks.Length; i++) { var b = g.Blocks[i]; if (az >= b.AzMin && az < b.AzMax) return i; }
			for (int i = 0; i < g.Blocks.Length; i++) { var b = g.Blocks[i]; if (az == b.AzMax && !HasStart(g, az)) return i; }
			return -1;
		}

		static bool HasStart(Geometry g, float az) { foreach (var b in g.Blocks) if (b.AzMin == az) return true; return false; }

		internal static bool Sphere(V3 p, V3 d, V3 c, float r, out float sIn, out float sOut)
		{
			var m = p - c;
			float b = V3.Dot(m, d), cc = V3.Dot(m, m) - r * r, disc = b * b - cc;
			if (disc < 0f) { sIn = sOut = -b; return false; }
			float q = (float)Math.Sqrt(disc);
			sIn = -b - q; sOut = -b + q;
			return true;
		}

		internal static bool RayHitsBox(V3 p, V3 d, V3 centre, V3 half)
		{
			float t0 = 0f, t1 = float.PositiveInfinity;
			float[] o = { p.x - centre.x, p.y - centre.y, p.z - centre.z }, dir = { d.x, d.y, d.z }, h = { half.x, half.y, half.z };
			for (int k = 0; k < 3; k++)
			{
				if (Math.Abs(dir[k]) < 1e-9f) { if (o[k] < -h[k] || o[k] > h[k]) return false; continue; }
				float a = (-h[k] - o[k]) / dir[k], b = (h[k] - o[k]) / dir[k];
				if (a > b) { var t = a; a = b; b = t; }
				t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
				if (t0 > t1) return false;
			}
			return true;
		}

		internal static Result Step(Geometry g, State s, Track t, V3 p, V3 v, bool atBox, float now, float dt, string ready)
		{
			var res = new Result { Act = Act.None };
			if (t.Done) return res;
			float speed = v.Length;
			if (speed < 1e-3f) return res;
			var d = v * (1f / speed);
			var rel = p - g.Origin;
			float range = rel.Length;
			if (!t.Seen)
			{
				if (range > g.TrackRange) return res;
				if (V3.Dot(rel, d) >= 0f) return res;
				float az = rel.Az, el = rel.El;
				if (!atBox) return res;
				t.SeenAt = now - Math.Min(dt, Math.Max(0f, (g.TrackRange - range) / speed));
				if (el < g.ElMin || el > g.ElMax || BlockFor(g, az) < 0) return Drop(t, res, $"not engaged (bearing {az:+0.0;-0.0}, el {el:+0.0;-0.0}: outside every sector)");
				if (ready != null) return Drop(t, res, $"not engaged ({ready})");
				if (range + speed * dt < g.MinRange) return Drop(t, res, $"too close (track at {range:0.0} m, {range + speed * dt:0.0} m a frame before < {g.MinRange:0} m)");
				t.Block = BlockFor(g, az);
				if (s.Lost[t.Block]) return Drop(t, res, $"not engaged (block {g.Blocks[t.Block].Name} lost)");
				if (s.Rounds[t.Block] <= 0) return Drop(t, res, $"not engaged (no rounds left in block {g.Blocks[t.Block].Name}, {s.SideRounds(g, g.Blocks[t.Block].Side)} on that side)");
				res.Act = Act.Seen;
			}
			var blk = g.Blocks[t.Block];
			float burstRange = t.Committed ? t.BurstRange : BurstRange(g, blk, p, d);
			var B = t.Committed ? t.BurstPoint : blk.Burst(burstRange);
			var axis = blk.Axis;
			if (!t.Committed)
			{
				if (!Field(g, p, d, B, axis, out float sHit, out float sLeave))
				{
					if (V3.Dot(B - p, d) < 0f) return Drop(t, res, $"not fired (passes {Wide(p, d, B):0.0} m wide of the burst point, outside the fragment field)");
					return res;
				}
				if (sLeave < 0f) return Drop(t, res, "late (past the burst point)");
				float flight = burstRange / g.RoundSpeed;
				float want = now + Math.Max(0f, sHit) / speed - flight;
				if (want > now + dt) return res;
				if (ready != null) return Drop(t, res, $"not fired ({ready})");
				if (s.Rounds[t.Block] <= 0 || s.Lost[t.Block]) return Drop(t, res, $"not fired (block {blk.Name} {(s.Lost[t.Block] ? "lost" : "empty")})");
				int other = 1 - blk.Side;
				float react = t.SeenAt + g.ReactionTime, own = s.SideFired[blk.Side] + g.Cooldown, near = s.SideFired[other] + g.OtherSide;
				float launch = Math.Max(Math.Max(want, now - dt), Math.Max(react, Math.Max(own, near)));
				float leaveAt = now + sLeave / speed;
				if (launch + flight > leaveAt + 1e-5f)
				{
					string cause = launch == react || launch == want || launch == now - dt
						? $"late (track {now - t.SeenAt:0.000} s, {sLeave / speed:0.000} s left in the fragment field's reach, reaction {g.ReactionTime:0.000} + flight {flight:0.000} s)"
						: $"busy (the {(launch == own ? "side's" : "other side's")} last launch {now - Math.Max(s.SideFired[blk.Side], s.SideFired[other]):0.000} s ago)";
					return Drop(t, res, cause);
				}
				if (launch > now + dt) return res;
				s.Rounds[t.Block]--;
				s.SideFired[blk.Side] = launch;
				t.Committed = true; t.BurstAt = launch + flight; t.BurstPoint = B; t.BurstRange = burstRange;
				res.Act = Act.Fire; res.Fired = true;
			}
			if (t.BurstAt > now + dt + 1e-6f) return res;
			float move = speed * (t.BurstAt - now);
			if (move < -speed * dt) move = -speed * dt;
			res.Act = Act.Intercept;
			res.Moved = move;
			res.Hit = p + d * move;
			res.Miss = (res.Hit - B).Length;
			res.InField = InField(g, res.Hit, B, axis);
			t.Done = true;
			return res;
		}

		static float Wide(V3 p, V3 d, V3 c) { var m = c - p; return (m - d * V3.Dot(m, d)).Length; }

		internal static bool InField(Geometry g, V3 x, V3 B, V3 axis)
		{
			var rel = x - B; float r = rel.Length;
			if (r <= g.FragReach + 0.01f) return true;
			return r <= g.ConeReach + 0.01f && V3.Dot(rel, axis) >= r * (float)Math.Cos((g.ConeHalf + 0.5f) * Math.PI / 180.0);
		}

		static bool Field(Geometry g, V3 p, V3 d, V3 B, V3 axis, out float sHit, out float sLeave)
		{
			if (Sphere(p, d, B, g.FragReach, out float i0, out float o0)) { sHit = 0.5f * (i0 + o0); sLeave = o0; return true; }
			if (Sphere(p, d, B, g.ConeReach, out float a, out float b) && InField(g, p + d * Math.Max(0f, a), B, axis))
			{
				sLeave = Math.Min(b, V3.Dot(B - p, d));
				sHit = 0.5f * (a + sLeave);
				return true;
			}
			sHit = sLeave = 0f;
			return false;
		}

		static Result Drop(Track t, Result res, string why) { t.Done = true; t.Why = why; res.Act = Act.Drop; return res; }

		internal static bool Kills(float pKill, float draw) => draw < pKill;

		internal static bool BrokenUp(float pBrokenUp, float draw) => draw < pBrokenUp;
	}
}
