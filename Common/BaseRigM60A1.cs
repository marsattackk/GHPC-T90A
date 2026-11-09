using System.Linq;

namespace T90A.Common
{
	public static class BaseRigM60A1
	{
		public const string HullPath = "--RIG/hull", TurretPath = "--RIG/hull/turret", GunPath = "--RIG/hull/turret/main gun mantlet";

		public const string HullGroup = "--HULL ARMOR", TurretGroup = "--TURRET ARMOR", GunGroup = "--GUN ARMOR", CupolaGroup = "--CUPOLA ARMOR", CupolaGunGroup = "--CUPOLA GUN ARMOR", FloorGroup = "TURRET FLOOR ARMOR";
		public static readonly string[] ColliderGroups = { HullGroup, TurretGroup, GunGroup, CupolaGroup, CupolaGunGroup, FloorGroup };
		public static bool IsColliderGroup(string name) => ColliderGroups.Contains(name);
		public static bool IsColliderGroup(string name, string group) => name == "--" + group.ToUpperInvariant() + " ARMOR";
	}
}
