using UnityEngine;

namespace T90A.Common
{
	public sealed class BeltCurve
	{
		const int N = 48;
		readonly Vector3[] _p = new Vector3[N + 1];
		readonly float[] _s = new float[N + 1];
		Vector3 _upA, _upB;
		public float Length => _s[N];

		public BeltCurve(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp) =>
			Set(feed, feedDir, feedUp, mouth, mouthDir, mouthUp);

		public void Set(Vector3 feed, Vector3 feedDir, Vector3 feedUp, Vector3 mouth, Vector3 mouthDir, Vector3 mouthUp)
		{
			float h = (mouth - feed).magnitude * 0.4f;
			Vector3 p1 = feed + feedDir * h, p2 = mouth + mouthDir * h;
			for (int i = 0; i <= N; i++)
			{
				float t = i / (float)N, u = 1f - t;
				_p[i] = u * u * u * feed + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * mouth;
				_s[i] = i == 0 ? 0f : _s[i - 1] + (_p[i] - _p[i - 1]).magnitude;
			}
			_upA = feedUp; _upB = mouthUp;
		}

		int Seg(float s) { int i = 0; while (i < N - 1 && _s[i + 1] < s) i++; return i; }

		public Vector3 Point(float s)
		{
			if (s <= 0f) return _p[0] + (_p[1] - _p[0]).normalized * s;
			if (s >= Length) return _p[N] + (_p[N] - _p[N - 1]).normalized * (s - Length);
			int i = Seg(s);
			return Vector3.Lerp(_p[i], _p[i + 1], (s - _s[i]) / Mathf.Max(1e-6f, _s[i + 1] - _s[i]));
		}

		public Matrix4x4 Frame(float s)
		{
			int i = Mathf.Clamp(Seg(Mathf.Clamp(s, 0f, Length)), 0, N - 1);
			var tan = (_p[i + 1] - _p[i]).normalized;
			var up = Vector3.Slerp(_upA, _upB, Mathf.Clamp01(s / Mathf.Max(1e-6f, Length)));
			if (Mathf.Abs(Vector3.Dot(up, tan)) > 0.995f) up = Vector3.Cross(tan, Vector3.right);
			return Matrix4x4.TRS(Point(s), Quaternion.LookRotation(tan, up), Vector3.one);
		}

		public float Closest(Vector3 p)
		{
			int best = 0;
			for (int i = 1; i <= N; i++) if ((_p[i] - p).sqrMagnitude < (_p[best] - p).sqrMagnitude) best = i;
			float bs = _s[best], bd = (_p[best] - p).sqrMagnitude;
			for (int i = Mathf.Max(0, best - 1); i < Mathf.Min(N, best + 1); i++)
				for (int j = 0; j <= 20; j++)
				{
					var q = Vector3.Lerp(_p[i], _p[i + 1], j / 20f);
					if ((q - p).sqrMagnitude < bd) { bd = (q - p).sqrMagnitude; bs = Mathf.Lerp(_s[i], _s[i + 1], j / 20f); }
				}
			return bs;
		}
	}
}
