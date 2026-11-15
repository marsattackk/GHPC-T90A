// Common\ is code my GHPC mods share, stamped whole into this repo (Common\STAMP.txt lists the
// SHA-256 of every file, computed with CRLF read as LF). This test fails, naming the file, when a Common file was edited here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace T90A.Tests
{
	public class CommonSealTests
	{
		const string Why = "Common is code shared by my GHPC mods and stamped into each repo, so it is never edited here. To change it, "
			+ "open an issue or a pull request on this repo describing the change; I apply it to the shared copy and stamp every mod again.";

		static string CommonFolder()
		{
			for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "Common");
				if (File.Exists(Path.Combine(candidate, "STAMP.txt"))) return candidate;
			}
			throw new InvalidOperationException("Common\\STAMP.txt not found above " + AppContext.BaseDirectory + ". " + Why);
		}

		static string Sha256(string path)
		{
			var bytes = File.ReadAllBytes(path);
			var text = new List<byte>(bytes.Length);
			for (int i = 0; i < bytes.Length; i++)
			{
				if (bytes[i] == 13 && i + 1 < bytes.Length && bytes[i + 1] == 10) continue;   // CRLF as LF
				text.Add(bytes[i]);
			}
			using (var sha = SHA256.Create())
				return string.Concat(sha.ComputeHash(text.ToArray()).Select(b => b.ToString("x2")));
		}

		[Fact]
		public void EveryCommonFileIsAsStamped()
		{
			var folder = CommonFolder();
			var sealedFiles = new Dictionary<string, string>();
			foreach (var line in File.ReadAllLines(Path.Combine(folder, "STAMP.txt")))
			{
				var match = Regex.Match(line.Trim(), "^([0-9a-f]{64})  (.+)$");
				if (match.Success) sealedFiles[match.Groups[2].Value] = match.Groups[1].Value;
			}
			Assert.True(sealedFiles.Count > 0, "Common\\STAMP.txt lists no file. " + Why);
			var problems = new List<string>();
			foreach (var pair in sealedFiles.OrderBy(p => p.Key, StringComparer.Ordinal))
			{
				var path = Path.Combine(folder, pair.Key);
				if (!File.Exists(path)) problems.Add("Common\\" + pair.Key + " is stamped but missing");
				else if (Sha256(path) != pair.Value) problems.Add("Common\\" + pair.Key + " differs from the stamp");
			}
			foreach (var path in Directory.GetFiles(folder, "*.cs"))
				if (!sealedFiles.ContainsKey(Path.GetFileName(path))) problems.Add("Common\\" + Path.GetFileName(path) + " is not in the stamp");
			Assert.True(problems.Count == 0, string.Join("; ", problems) + ". " + Why);
		}
	}
}
