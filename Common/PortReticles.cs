using System.Collections.Generic;
using System.Linq;
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
			string key = file + RoundsKey(rounds);
			if (_built.TryGetValue(key, out var so) && so != null) return so;
			var spec = Spec(file);
			var problems = new List<string>();
			so = ReticleBuild.Build(spec, host, ReticleBuild.FontTemplate(fontHost), k => rounds != null && rounds.TryGetValue(k, out var a) ? a : null, problems);
			foreach (var p in problems) MelonLogger.Warning($"{_tag} reticle '{spec.Name}': {p}");
			return _built[key] = so;
		}

		static string RoundsKey(Dictionary<string, AmmoCodexScriptable> rounds) => rounds == null || rounds.Count == 0 ? ""
			: "|" + string.Join("|", Enumerable.Select(Enumerable.OrderBy(rounds, kv => kv.Key, System.StringComparer.Ordinal), kv => kv.Key + "=" + (kv.Value != null ? kv.Value.name : "-")));
	}
}
