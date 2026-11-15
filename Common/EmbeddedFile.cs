using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace T90A.Common
{
	internal static class EmbeddedFile
	{
		static readonly Dictionary<string, byte[]> Read = new Dictionary<string, byte[]>();

		internal static byte[] Bytes(string name)
		{
			if (Read.TryGetValue(name, out var had)) return had;
			byte[] bytes = null;
			using (var stream = typeof(EmbeddedFile).Assembly.GetManifestResourceStream(name))
			{
				if (stream != null)
				{
					var memory = new MemoryStream();
					stream.CopyTo(memory);
					bytes = memory.ToArray();
				}
			}
			Read[name] = bytes;
			return bytes;
		}

		static bool _skinWarned;

		// The T-72M1's running gear is one skinned mesh with the skirts in it, so a port ships a copy with them cut out.
		internal static byte[] T72M1Skin(string tag)
		{
			var skin = Bytes("T72M1_skin_hull_trimmed.bin");
			if (skin == null && !_skinWarned)
			{
				_skinWarned = true;
				MelonLogger.Warning($"{tag}: no trimmed T-72M1 skin in the DLL; the T-72 side skirts may show through the hull.");
			}
			return skin;
		}
	}
}
