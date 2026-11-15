using System.Reflection;
using Reticle;

namespace T90A.Common
{
	// GHPC's reticle mesh keeps the mesh it built in a private field. Clearing it makes the optic build the reticle
	// again on its next load, which every reticle swap in the mod needs.
	internal static class ReticleMeshes
	{
		internal static readonly FieldInfo Smr = Reflect.Field(typeof(ReticleMesh), "SMR");

		internal static void ClearSmr(ReticleMesh mesh) => Smr.SetValue(mesh, null);
	}
}
