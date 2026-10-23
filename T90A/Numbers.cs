using T90A.Common;
using UnityEngine;

namespace T90A
{
	internal static class Numbers
	{
		static readonly TacticalNumbers.Settings S = new TacticalNumbers.Settings
		{
			LampFace = new Vector3(-0.489f, 2.380f - 1.369f, -1.684f + 0.527f),
			LampNormal = new Vector3(0f, 0f, -1f),
			BoxFaceR = new Vector3(1.179f, 1.804f - 1.369f, -1.592f + 0.527f), BoxNormalR = new Vector3(0.92f, -0.02f, -0.39f),
			BoxFaceL = new Vector3(-1.278f, 1.827f - 1.369f, -1.558f + 0.527f), BoxNormalL = new Vector3(-0.91f, -0.01f, -0.41f),
			DigitScale = 1.25f,
		};

		internal static void Apply(Transform root, Transform turret, string mode)
		{
			S.Mode = mode;
			TacticalNumbers.Apply(root, turret, S);
		}
	}
}
