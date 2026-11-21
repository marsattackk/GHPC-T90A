using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using T90A.Common;
using Xunit;

namespace T90A.Tests
{
	// The dm_armour.txt in Resources\ as it ships: every line reads, and the figures are the kind the damage model takes.
	public class ShippedTextTests : IDisposable
	{
		// What this tank's table holds: its number of pieces, and whether any of them is reactive armour.
		const int Pieces = 348;
		const bool Reactive = true;

		readonly List<string> _warnings = new List<string>();
		readonly Action<string> _keep = BundleText.Warn;

		public ShippedTextTests() => BundleText.Warn = _warnings.Add;

		public void Dispose() => BundleText.Warn = _keep;

		static DmArmour Shipped() => DmArmour.Parse(File.ReadAllText(Repo.At("Resources", "dm_armour.txt")));

		[Fact]
		public void Every_line_of_dm_armour_reads_without_a_warning()
		{
			var armour = Shipped();
			Assert.Empty(_warnings);
			Assert.True(
				armour.Entries.Count == Pieces,
				$"dm_armour.txt has {armour.Entries.Count} pieces and Pieces says {Pieces}: a piece added or taken out changes both.");
			Assert.Equal(armour.Entries.Count, armour.Entries.Select(e => e.Piece).Distinct().Count());
		}

		[Fact]
		public void Every_piece_has_a_known_mode_and_material()
		{
			foreach (var entry in Shipped().Entries)
			{
				Assert.True(DmArmour.Modes.Contains(entry.Mode), $"{entry.Piece}: mode '{entry.Mode}'");
				if (entry.Drawn) continue;
				Assert.True(DmArmour.Materials.Contains(entry.Material), $"{entry.Piece}: material '{entry.Material}'");
			}
		}

		[Fact]
		public void The_figures_are_positive_and_reactive_armour_is_where_the_tank_has_it()
		{
			var entries = Shipped().Entries.Where(e => !e.Drawn).ToList();
			foreach (var entry in entries)
				Assert.True(entry.Ke > 0f && entry.Ce > 0f, $"{entry.Piece}: {entry.Ke} / {entry.Ce}");
			Assert.Equal(Reactive, entries.Any(e => e.Era));
		}
	}
}
