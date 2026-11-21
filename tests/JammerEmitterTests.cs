using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// ShtoraHeat finds each emitter's module by the id in Countermeasures.cs. A renamed id only shows in game, as an emitter
	// that stays cold in thermal while it jams.
	public class JammerEmitterTests
	{
		[Theory]
		[InlineData("EmitterLeft")]
		[InlineData("EmitterRight")]
		public void Each_jammer_emitter_is_a_module_with_a_box(string field)
		{
			string source = File.ReadAllText(Repo.At("T90A", "Countermeasures.cs"));
			var match = Regex.Match(source, field + @"\s*=\s*""([^""]+)""");
			Assert.True(match.Success, field + " not found in Countermeasures.cs");
			string id = match.Groups[1].Value;
			var root = (MiniJson.Obj)MiniJson.Parse(File.ReadAllText(Repo.At("Resources", "modules_built.json")));
			var module = ((List<object>)root["modules"]).OfType<MiniJson.Obj>().FirstOrDefault(m => m["id"] as string == id);
			Assert.True(module != null, $"no module '{id}' in modules_built.json");
			Assert.NotEmpty((List<object>)module["boxes"]);
		}
	}
}
