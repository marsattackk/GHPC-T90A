using System.Collections.Generic;
using System.Reflection;
using GHPC.Weaponry;
using MelonLoader;
using Reticle;

namespace T90A.Common
{
	internal sealed class PortReticles
	{
		readonly Assembly _assembly;
		readonly string _tag;
		readonly Dictionary<string, ReticleSpec> _specs = new Dictionary<string, ReticleSpec>();
		readonly Dictionary<string, ReticleSO> _built = new Dictionary<string, ReticleSO>();

		internal PortReticles(Assembly assembly, string tag)
		{
			_assembly = assembly;
			_tag = tag;
		}

		internal ReticleSpec Spec(string file)
		{
			if (!_specs.TryGetValue(file, out var spec)) _specs[file] = spec = ReticleSpec.FromResource(_assembly, file);
			return spec;
		}

		internal ReticleSO Build(string file, ReticleSO host, ReticleSO fontHost, Dictionary<string, AmmoCodexScriptable> rounds)
		{
			if (_built.TryGetValue(file, out var so) && so != null) return so;
			var spec = Spec(file);
			var problems = new List<string>();
			so = ReticleBuild.Build(spec, host, ReticleBuild.FontTemplate(fontHost), k => rounds != null && rounds.TryGetValue(k, out var a) ? a : null, problems);
			foreach (var p in problems) MelonLogger.Warning($"{_tag} reticle '{spec.Name}': {p}");
			return _built[file] = so;
		}
	}
}
