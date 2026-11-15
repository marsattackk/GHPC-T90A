using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// The reticles in Resources\, expanded and compared with tests\expanded\<file>.txt. A change to the language that moves
	// a single line of a real reticle shows up here. After changing a reticle on purpose, run the tests with
	// T90A_UPDATE_EXPANDED=1 to write the files again, and read the diff.
	public class ShippedReticleTests
	{
		public static IEnumerable<object[]> Files() =>
			Directory.GetFiles(Repo.At("Resources"), "*.jsonc").Select(path => new object[] { Path.GetFileName(path) });

		static ReticleSpec Load(string file) =>
			ReticleSpec.Parse(File.ReadAllText(Repo.At("Resources", file), Encoding.UTF8), file);

		[Theory]
		[MemberData(nameof(Files))]
		public void Expands_without_problems(string file)
		{
			var spec = Load(file);
			Assert.True(spec.Problems.Count == 0, string.Join("; ", spec.Problems));
			Assert.NotEmpty(spec.Top.Cast<object>().Concat(spec.Screen));
		}

		[Theory]
		[MemberData(nameof(Files))]
		public void Expands_as_before(string file)
		{
			string expanded = Dump(Load(file));
			string saved = Repo.At("tests", "expanded", Path.ChangeExtension(file, ".txt"));
			if (Environment.GetEnvironmentVariable("T90A_UPDATE_EXPANDED") == "1")
			{
				Directory.CreateDirectory(Path.GetDirectoryName(saved));
				File.WriteAllText(saved, expanded, new UTF8Encoding(false));
				return;
			}
			Assert.True(File.Exists(saved), $"no {saved}, run with T90A_UPDATE_EXPANDED=1 to write it");
			var want = File.ReadAllText(saved, Encoding.UTF8).Replace("\r\n", "\n").Split('\n');
			var got = expanded.Split('\n');
			for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
			{
				string wantLine = i < want.Length ? want[i] : "(nothing)";
				string gotLine = i < got.Length ? got[i] : "(nothing)";
				Assert.True(wantLine == gotLine, $"{file} line {i + 1}\n  was: {wantLine}\n  now: {gotLine}");
			}
		}

		static string N(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

		// One line per node, indented by depth, then the screen items and any problems.
		internal static string Dump(ReticleSpec spec)
		{
			var text = new StringBuilder();
			text.Append($"{spec.Name}|{N(spec.Fov)}|{spec.Plane}|{spec.LightsFromHost}\n");
			foreach (var light in spec.Lights) text.Append($"light|{light.Key}|{string.Join(",", light.Value.Select(c => N(c)))}\n");
			void Walk(ReticleSpec.Node node, int depth)
			{
				string pad = new string(' ', depth);
				switch (node)
				{
					case ReticleSpec.Group group:
						text.Append($"{pad}G|{group.Name}|{group.Align}|{group.Ammo}|{N(group.Stadia)}|{N(group.X)}|{N(group.Y)}\n");
						foreach (var child in group.Children) Walk(child, depth + 1);
						break;
					case ReticleSpec.Line line:
						text.Append($"{pad}L|{N(line.X)}|{N(line.Y)}|{N(line.Deg)}|{N(line.Len)}|{N(line.W)}|{line.Visual}|{line.Light}\n");
						break;
					case ReticleSpec.Circle circle:
						text.Append($"{pad}C|{N(circle.X)}|{N(circle.Y)}|{N(circle.R)}|{N(circle.W)}|{circle.Segments}|{circle.Visual}|{circle.Light}\n");
						break;
					case ReticleSpec.Text label:
						text.Append($"{pad}T|{label.Value}|{N(label.X)}|{N(label.Y)}|{N(label.Size)}|{label.Visual}|{label.Light}\n");
						break;
				}
			}
			foreach (var node in spec.Top) Walk(node, 0);
			if (spec.ScreenName != null) text.Append($"screen|{spec.ScreenName}\n");
			foreach (var item in spec.Screen)
				text.Append(
					$"S|{item.Kind}|{item.Name}|{item.Anchor}|{N(item.X)}|{N(item.Y)}|{N(item.W)}|{N(item.H)}|{N(item.Size)}|{N(item.Deg)}"
					+ $"|{(item.FullWidth ? 1 : 0)}|{item.Value}|{item.Font}|{item.Align}|{string.Join(",", item.Color.Select(c => N(c)))}\n");
			foreach (var problem in spec.Problems) text.Append("P|" + problem + "\n");
			return text.ToString();
		}
	}
}
