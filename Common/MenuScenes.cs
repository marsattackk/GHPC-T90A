using System;

namespace T90A.Common
{
	internal static class MenuScenes
	{
		static readonly string[] Names =
		{
			"MainMenu2_Scene",
			"MainMenu2-1_Scene",
			"LOADER_MENU",
			"LOADER_INITIAL",
			"t64_menu",
			"leo2_menu"
		};

		internal static bool Contains(string scene) => Array.IndexOf(Names, scene) >= 0;
	}
}
