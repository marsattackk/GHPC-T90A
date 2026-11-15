using System;
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

		internal ReticleSO Build(
			string file,
			ReticleSO host,
			ReticleSO fontHost,
			Dictionary<string, AmmoCodexScriptable> rounds,
			Dictionary<string, double> vars = null)
		{
			string key = file
				+ RoundsKey(rounds)
				+ (vars == null
					? ""
					: "|"
						+ string.Join(
							"|",
							Enumerable.Select(
								Enumerable.OrderBy(vars, p => p.Key, StringComparer.Ordinal),
								p => p.Key + "=" + p.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
			if (_built.TryGetValue(key, out var reticle) && reticle != null) return reticle;
			var spec = vars == null ? Spec(file) : ReticleSpec.FromResource(_assembly, file, vars);
			var problems = new List<string>();
			reticle = ReticleBuild.Build(
				spec,
				host,
				ReticleBuild.FontTemplate(fontHost),
				r => rounds != null && rounds.TryGetValue(r, out var ammo) ? ammo : null,
				problems);
			foreach (var problem in problems) MelonLogger.Warning($"{_tag} reticle '{spec.Name}': {problem}");
			return _built[key] = reticle;
		}

		static string RoundsKey(Dictionary<string, AmmoCodexScriptable> rounds) => rounds == null || rounds.Count == 0 ? ""
			: "|"
				+ string.Join(
					"|",
					Enumerable.Select(
						Enumerable.OrderBy(rounds, p => p.Key, StringComparer.Ordinal),
						p => p.Key + "=" + (p.Value != null ? p.Value.name : "-")));
	}
}
