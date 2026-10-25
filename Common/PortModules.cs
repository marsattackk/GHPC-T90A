using System.Collections.Generic;
using System.Reflection;
using GHPC.Vehicle;
using MelonLoader;

namespace T90A.Common
{
	internal sealed class PortModules
	{
		const string Resource = "modules_built.json";
		readonly Assembly _assembly;
		readonly string _tag, _base;
		ModuleSpec _spec;
		bool _read;

		internal PortModules(Assembly assembly, string tag, string baseName, string mod)
		{
			_assembly = assembly;
			_tag = tag;
			_base = baseName;
		}

		internal ModuleSpec Spec
		{
			get
			{
				if (_read) return _spec;
				_read = true;
				using (var s = _assembly.GetManifestResourceStream(Resource))
				{
					if (s == null) { MelonLogger.Warning($"{_tag} modules: data missing from the DLL; modules stay the {_base}'s."); return null; }
					using (var r = new System.IO.StreamReader(s)) _spec = ModuleSpec.Parse(r.ReadToEnd(), Resource);
				}
				return _spec;
			}
		}

		internal void Build(Vehicle vehicle)
		{
			var spec = Spec;
			if (spec == null) return;
			var problems = new List<string>();
			ModuleBuilder.Build(vehicle, spec, _tag, problems);
			if (problems.Count > 0) MelonLogger.Warning($"{_tag} modules: " + string.Join("; ", problems));
		}

		internal void Wire(Vehicle vehicle)
		{
			var problems = new List<string>();
			ModuleBuilder.Wire(vehicle, problems);
			if (problems.Count > 0) MelonLogger.Warning($"{_tag} modules: " + string.Join("; ", problems));
		}
	}
}
