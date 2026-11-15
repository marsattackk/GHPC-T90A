using System;
namespace T90A.Common
{
	// GHPC reseeds UnityEngine.Random from the clock all over the place (RandomUtil.RandomizeUnityRandom), so I keep my own.
	internal static class ModRandom
	{
		static readonly Random Rng = new Random();

		internal static float Value => (float)Rng.NextDouble();
	}
}
