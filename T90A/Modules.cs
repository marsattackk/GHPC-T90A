using System.Collections.Generic;
using GHPC.Vehicle;
using MelonLoader;
using T90A.Common;

namespace T90A
{
	internal static class Modules
	{
		const string Resource = "modules_built.json";
		static ModuleSpec _spec;
		static bool _read;

		internal static ModuleSpec Spec
		{
			get
			{
				if (_read) return _spec;
				_read = true;
				using (var s = typeof(Modules).Assembly.GetManifestResourceStream(Resource))
				{
					if (s == null) { MelonLogger.Warning("T-90A modules: data missing from the DLL; modules stay the T-72M1's."); return null; }
					using (var r = new System.IO.StreamReader(s)) _spec = ModuleSpec.Parse(r.ReadToEnd(), Resource);
				}
				return _spec;
			}
		}

		internal static void Build(Vehicle vehicle)
		{
			var spec = Spec;
			if (spec == null) return;
			var problems = new List<string>();
			ModuleBuilder.Build(vehicle, spec, "T-90A", problems);
			if (problems.Count > 0) MelonLogger.Warning("T-90A modules: " + string.Join("; ", problems));
		}

		internal static void Wire(Vehicle vehicle)
		{
			var problems = new List<string>();
			ModuleBuilder.Wire(vehicle, problems);
			if (problems.Count > 0) MelonLogger.Warning("T-90A modules: " + string.Join("; ", problems));
		}
	}
}
