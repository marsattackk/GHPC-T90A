using GHPC.Vehicle;
using T90A.Common;

namespace T90A
{
	internal static class Interior
	{
		static readonly InteriorMap.Embedded Map = new InteriorMap.Embedded(typeof(Interior).Assembly, "T-90A", "T90A", "T-72M1");

		internal static void Apply(Vehicle vehicle) => Map.Apply(vehicle);
	}
}
