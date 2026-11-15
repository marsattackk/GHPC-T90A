using System.IO;
using UnityEngine;

namespace T90A.Common
{
	// The text data. Resources\<name>.txt in the DLL wins and the bundle's TextAsset of that name is only the fallback,
	// so editing the numbers doesn't need Unity.
	internal static class PortText
	{
		internal static string Load(AssetBundle bundle, string name)
		{
			using (var stream = typeof(PortText).Assembly.GetManifestResourceStream(name + ".txt"))
			{
				if (stream != null)
				{
					using (var reader = new StreamReader(stream))
					{
						return reader.ReadToEnd();
					}
				}
			}
			var asset = bundle != null ? bundle.LoadAsset<TextAsset>(name) : null;
			if (asset == null) return null;
			return asset.text;
		}
	}
}
