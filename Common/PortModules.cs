using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace T90A.Common
{
	internal sealed class PortModules
	{
		const string Resource = "modules_built.json";
		readonly Assembly _assembly;
		readonly string _tag, _base, _mod;
		ModuleSpec _spec;
		bool _read;

		internal PortModules(Assembly assembly, string tag, string baseName, string mod)
		{
			_assembly = assembly;
			_tag = tag;
			_base = baseName;
			_mod = mod;
		}

		internal ModuleSpec Spec
		{
			get
			{
				if (_read) return _spec;
				_read = true;
				using (var stream = _assembly.GetManifestResourceStream(Resource))
				{
					if (stream == null)
					{
						MelonLogger.Warning($"{_tag} modules: data missing from the DLL; modules stay the {_base}'s.");
						return null;
					}
					try { using (var reader = new System.IO.StreamReader(stream)) _spec = ModuleSpec.Parse(reader.ReadToEnd(), Resource); }
					catch (Exception exception)
					{
						_spec = null;
						MelonLogger.Warning($"{_tag} modules: the module data in the DLL can't be read ({exception.GetType().Name}: {exception.Message}); modules stay the {_base}'s.");
					}
				}
				return _spec;
			}
		}

		internal void Build(Vehicle vehicle, params string[] later)
		{
			var spec = Spec;
			if (spec == null) return;
			var problems = new List<string>();
			ModuleBuilder.Build(
				vehicle,
				spec,
				_tag,
				problems,
				m => !later.Any(l => string.Equals(l, m.Group, StringComparison.OrdinalIgnoreCase)));
			if (problems.Count > 0) MelonLogger.Warning($"{_tag} modules: " + string.Join("; ", problems));
		}

		internal void BuildOn(Vehicle vehicle, string group, Transform follow)
		{
			var spec = Spec;
			if (spec == null || follow == null) return;
			if (!spec.Modules.Any(m => string.Equals(m.Group, group, StringComparison.OrdinalIgnoreCase))) return;
			var problems = new List<string>();
			Followers.ColliderGroup(vehicle.transform, $"{_tag} {group} COLLIDERS", follow, "AAR");
			ModuleBuilder.Build(
				vehicle,
				spec,
				_tag,
				problems,
				m => string.Equals(m.Group, group, StringComparison.OrdinalIgnoreCase));
			if (problems.Count > 0) MelonLogger.Warning($"{_tag} modules: " + string.Join("; ", problems));
		}

		internal void Wire(Vehicle vehicle)
		{
			var problems = new List<string>();
			string log = ModuleBuilder.Wire(vehicle, problems);
			if (problems.Count > 0) MelonLogger.Warning($"{_tag} modules: " + string.Join("; ", problems));
		}
	}
}
