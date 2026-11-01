using System.Collections.Generic;
using UnityEngine;

namespace T90A.Common
{
	public static class Hashing
	{
		// Vector3Int's own hash collides in heaps on a millimetre grid.
		public static readonly IEqualityComparer<Vector3Int> Vec3Int = new V3();

		sealed class V3 : IEqualityComparer<Vector3Int>
		{
			public bool Equals(Vector3Int a, Vector3Int b) => a.x == b.x && a.y == b.y && a.z == b.z;
			public int GetHashCode(Vector3Int v) { unchecked { return Mix((uint)v.x * 73856093u ^ (uint)v.y * 19349663u ^ (uint)v.z * 83492791u); } }
		}

		static int Mix(uint h) { unchecked { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; return (int)h; } }
	}
}
