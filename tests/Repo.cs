using System;
using System.IO;
using Xunit;

// Static state all over the tested code (the expression cache, BundleText.Warn, the settings file path), so one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace T90A.Tests
{
	static class Repo
	{
		static string _root;

		// The folder holding T90A.csproj, found from wherever the test DLL ended up.
		internal static string Root
		{
			get
			{
				if (_root != null) return _root;
				for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
					if (File.Exists(Path.Combine(dir.FullName, "T90A.csproj"))) return _root = dir.FullName;
				throw new DirectoryNotFoundException("no T90A.csproj above " + AppDomain.CurrentDomain.BaseDirectory);
			}
		}

		internal static string At(params string[] parts) => Path.Combine(Root, Path.Combine(parts));
	}
}
