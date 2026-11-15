using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace T90A.Common
{
	public sealed class VanillaEdits
	{
		public string[] Off = { };
		public (string piece, Matrix4x4 m)[] Transforms = { };
		public string[] Reshape = { };
		public (string bone, string group, Vector3 delta)[] Bones = { };
		// Off takes the children too. Strip leaves a hit zone hanging under the piece alone.
		public string[] Strip = { };

		public const string AssetName = "vanilla_edits", ReshapeKind = "reshape";
		public const string VolumeKind = "volume";

		public static Matrix4x4 RotateMove(Vector3 pivot, Vector3 axis, float deg, Vector3 shift) =>
			Matrix4x4.Translate(shift + pivot) * Matrix4x4.Rotate(Quaternion.AngleAxis(deg, axis)) * Matrix4x4.Translate(-pivot);

		static readonly CultureInfo CI = CultureInfo.InvariantCulture;

		public string ToText()
		{
			var builder = new StringBuilder("# T90A.Common.VanillaEdits\n");
			foreach (var name in Off) builder.Append($"off|{name}\n");
			foreach (var (partName, matrix) in Transforms) builder.Append($"xform|{partName}|{string.Join(",", Enumerable.Range(0, 16).Select(i => matrix[i].ToString("R", CI)))}\n");
			foreach (var name in Reshape) builder.Append($"reshape|{name}\n");
			foreach (var (bone, groupName, delta) in Bones) builder.Append($"bone|{bone}|{groupName}|{delta.x.ToString("R", CI)},{delta.y.ToString("R", CI)},{delta.z.ToString("R", CI)}\n");
			foreach (var name in Strip) builder.Append($"strip|{name}\n");
			return builder.ToString();
		}

		public static VanillaEdits Parse(string text)
		{
			var edits = new VanillaEdits();
			var off = new List<string>();
			var moves = new List<(string, Matrix4x4)>();
			var reshapes = new List<string>();
			var bones = new List<(string, string, Vector3)>();
			var strip = new List<string>();
			var lines = text.Split('\n');
			for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
			{
				var line = lines[lineIndex].TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				var fields = line.Split('|');
				int want = fields[0] == "xform" ? 3 : fields[0] == "bone" ? 4 : 2;
				if (fields.Length < want || fields[1].Length == 0)
				{
					BundleText.Skip(AssetName, lineIndex + 1, line, $"'{fields[0]}' wants {want - 1} field(s) after it");
					continue;
				}
				try
				{
					switch (fields[0])
					{
						case "off":
							off.Add(fields[1]);
							break;
						case "reshape":
							reshapes.Add(fields[1]);
							break;
						case "strip":
							strip.Add(fields[1]);
							break;
						case "xform":
							var floats = BundleText.Floats(fields[2], 16);
							var matrix = new Matrix4x4();
							for (int i = 0; i < 16; i++) matrix[i] = floats[i];
							moves.Add((fields[1], matrix));
							break;
						case "bone":
							var deltaValues = BundleText.Floats(fields[3], 3);
							bones.Add((fields[1], fields[2], new Vector3(deltaValues[0], deltaValues[1], deltaValues[2])));
							break;
						default:
							BundleText.Skip(AssetName, lineIndex + 1, line, $"unknown edit '{fields[0]}'");
							break;
					}
				}
				catch (System.FormatException exception) { BundleText.Skip(AssetName, lineIndex + 1, line, exception.Message); }
			}
			edits.Off = off.ToArray();
			edits.Transforms = moves.ToArray();
			edits.Reshape = reshapes.ToArray();
			edits.Bones = bones.ToArray();
			edits.Strip = strip.ToArray();
			return edits;
		}

		public string Summary()
			=> $"{Off.Length} switched off ({string.Join(", ", Off)}), {Transforms.Length} moved ({string.Join(", ", Transforms.Select(m => m.piece))}), "
				+ $"{Reshape.Length} reshaped ({string.Join(", ", Reshape)}), {Strip.Length} stripped ({string.Join(", ", Strip)}), {Bones.Length} bones moved ({string.Join(", ", Bones.Select(b => $"{b.bone} by {b.delta}"))})";
	}
}
