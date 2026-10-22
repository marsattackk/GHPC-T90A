using System.Collections.Generic;
using GHPC.Weaponry;
using MelonLoader;
using Reticle;
using T90A.Common;

namespace T90A
{
	internal static class Reticles
	{
		static readonly Dictionary<string, ReticleSpec> Specs = new Dictionary<string, ReticleSpec>();
		static readonly Dictionary<string, ReticleSO> Built = new Dictionary<string, ReticleSO>();

		internal static ReticleSpec Spec(string file)
		{
			if (!Specs.TryGetValue(file, out var spec)) Specs[file] = spec = ReticleSpec.FromResource(typeof(Reticles).Assembly, file);
			return spec;
		}

		internal static ReticleSO Build(string file, ReticleSO host, ReticleSO fontHost, Dictionary<string, AmmoCodexScriptable> rounds)
		{
			if (Built.TryGetValue(file, out var so) && so != null) return so;
			var spec = Spec(file);
			var problems = new List<string>();
			so = ReticleBuild.Build(spec, host, ReticleBuild.FontTemplate(fontHost), k => rounds != null && rounds.TryGetValue(k, out var a) ? a : null, problems);
			foreach (var p in problems) MelonLogger.Warning($"T-90A reticle '{spec.Name}': {p}");
			return Built[file] = so;
		}
	}
}
