using System.Collections.Generic;
using GHPC.Vehicle;
using MelonLoader;
using T90A.Common;

namespace T90A
{
	internal static class Interior
	{
		const string Resource = "interior_map.txt";
		static InteriorMap _map;
		static bool _read;

		static InteriorMap Map
		{
			get
			{
				if (_read) return _map;
				_read = true;
				using (var s = typeof(Interior).Assembly.GetManifestResourceStream(Resource))
				{
					if (s == null) { MelonLogger.Warning("T-90A interior: data missing from the DLL; crew and ammo stay at the T-72M1's places."); return null; }
					using (var r = new System.IO.StreamReader(s)) _map = InteriorMap.Parse(r.ReadToEnd());
				}
				return _map;
			}
		}

		internal static void Apply(Vehicle vehicle)
		{
			var map = Map;
			if (map == null) return;
			var problems = new List<string>();
			string log = map.Apply(vehicle.transform, problems);
			if (problems.Count > 0) MelonLogger.Warning("T-90A interior: " + log);
		}
	}
}
