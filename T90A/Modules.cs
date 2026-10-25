using GHPC.Vehicle;
using T90A.Common;

namespace T90A
{
	internal static class Modules
	{
		static readonly PortModules All = new PortModules(typeof(Modules).Assembly, "T-90A", "T-72M1", "T90A");

		internal static void Build(Vehicle vehicle) => All.Build(vehicle);

		internal static void Wire(Vehicle vehicle) => All.Wire(vehicle);
	}
}
