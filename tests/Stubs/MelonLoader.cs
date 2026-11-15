// Stand-ins for the bits of MelonLoader that BundleText and PortSettings touch. Same names and call shapes, nothing else.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MelonLoader
{
	public static class MelonLogger
	{
		public static readonly List<string> Lines = new List<string>();
		public static void Msg(string s) => Lines.Add("msg: " + s);
		public static void Warning(string s) => Lines.Add("warning: " + s);
	}

	public abstract class MelonPreferences_Entry
	{
		public string Identifier;
		public abstract Type GetReflectedType();
		public abstract object BoxedValue { get; set; }
		public abstract string GetValueAsString();
		public abstract string GetDefaultValueAsString();
	}

	public sealed class MelonPreferences_Entry<T> : MelonPreferences_Entry
	{
		public T Value, DefaultValue;
		public override Type GetReflectedType() => typeof(T);
		public override object BoxedValue { get => Value; set => Value = (T)value; }
		static string Text(T v) => Convert.ToString(v, CultureInfo.InvariantCulture);
		public override string GetValueAsString() => Text(Value);
		public override string GetDefaultValueAsString() => Text(DefaultValue);
	}

	public sealed class MelonPreferences_Category
	{
		public string Identifier, DisplayName;
		public readonly List<MelonPreferences_Entry> Entries = new List<MelonPreferences_Entry>();

		// What the settings file holds: CreateEntry takes a value from here before the default, like MelonLoader does.
		public readonly Dictionary<string, object> Saved = new Dictionary<string, object>();

		public MelonPreferences_Category(string id)
		{
			Identifier = id;
			DisplayName = id;
		}

		public MelonPreferences_Entry<T> CreateEntry<T>(
			string identifier,
			T default_value,
			string display_name = null,
			string description = null,
			bool is_hidden = false)
		{
			var entry = new MelonPreferences_Entry<T>
			{
				Identifier = identifier,
				DefaultValue = default_value,
				Value = Saved.TryGetValue(identifier, out var saved) ? (T)saved : default_value,
			};
			Entries.Add(entry);
			return entry;
		}

		public MelonPreferences_Entry GetEntry(string identifier) => Entries.Find(entry => entry.Identifier == identifier);
	}
}

namespace MelonLoader.Utils
{
	public static class MelonEnvironment
	{
		public static string UserDataDirectory => System.IO.Path.GetTempPath();
	}
}
