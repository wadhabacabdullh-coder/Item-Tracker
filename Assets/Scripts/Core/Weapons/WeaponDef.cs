using System.Collections.Generic;

namespace ShadowContract.Core
{
    public enum WeaponClass { Melee, Throwing, Pistol, SMG, Rifle, Shotgun, Gadget }
    public enum AmmoType { None, Pistol, Rifle, Shells, Knives, Coins }

    /// <summary>Loadout slots selected with keys 1-5.</summary>
    public enum WeaponSlot { Melee = 0, Sidearm = 1, Primary = 2, Throwing = 3, Gadget = 4 }

    /// <summary>Static, data-driven weapon description. Upgrades modify a copy of these numbers at runtime.</summary>
    public sealed class WeaponDef
    {
        public string Id;
        public string Name;
        public string Description;
        public WeaponClass Class;
        public AmmoType Ammo;

        public float Damage;
        public int Pellets = 1;
        public float FireRate;          // shots per second
        public bool Automatic;
        public int MagSize;
        public float ReloadTime;        // full reload, or per shell when ShellReload
        public bool ShellReload;
        public float Spread;            // degrees (half-angle) while standing still
        public float MoveSpread = 2f;   // extra degrees while moving
        public float Range;             // tiles
        public float Recoil;            // degrees of bloom added per shot
        public float RecoilRecovery = 20f;
        public float Noise;             // tiles radius of the gunshot
        public bool Suppressed;
        public float MeleeArc = 100f;   // degrees
        public float MeleeRange = 1.15f;
        public float ProjectileSpeed;
        public float MoveSpeedMult = 1f;
        public float Shake;
        public bool CanSuppress;
        public int Price;
        public string UnlockAfter;      // mission id that unlocks it in the shop (null = always)
        public string Sound;            // sound id prefix for the audio layer

        public WeaponSlot Slot
        {
            get
            {
                switch (Class)
                {
                    case WeaponClass.Melee: return WeaponSlot.Melee;
                    case WeaponClass.Pistol: return WeaponSlot.Sidearm;
                    case WeaponClass.Throwing: return WeaponSlot.Throwing;
                    case WeaponClass.Gadget: return WeaponSlot.Gadget;
                    default: return WeaponSlot.Primary;
                }
            }
        }

        public bool IsMelee => Class == WeaponClass.Melee;
        public bool IsThrown => Class == WeaponClass.Throwing || Class == WeaponClass.Gadget;
        public bool IsGun => !IsMelee && !IsThrown;

        public WeaponDef Clone() => (WeaponDef)MemberwiseClone();
    }

    public static class WeaponCatalog
    {
        public static readonly Dictionary<string, WeaponDef> All = new Dictionary<string, WeaponDef>();

        public static WeaponDef Get(string id) => All.TryGetValue(id, out var d) ? d : null;

        private static void Add(WeaponDef d) => All[d.Id] = d;

        static WeaponCatalog()
        {
            Add(new WeaponDef
            {
                Id = "knife", Name = "Knife", Class = WeaponClass.Melee, Damage = 55, FireRate = 2.2f, MeleeRange = 1.1f, MeleeArc = 100,
                Noise = 1.5f, Sound = "knife", Description = "A simple folding blade. Silent takedowns from behind.",
            });
            Add(new WeaponDef
            {
                Id = "combat_knife", Name = "Combat Knife", Class = WeaponClass.Melee, Damage = 80, FireRate = 2.8f, MeleeRange = 1.3f, MeleeArc = 115,
                Noise = 1.2f, Sound = "knife", Price = 1200, Description = "Heavier, faster and longer reach. Two slashes drop most guards.",
            });
            Add(new WeaponDef
            {
                Id = "throwing_knives", Name = "Throwing Knives", Class = WeaponClass.Throwing, Ammo = AmmoType.Knives, Damage = 100, FireRate = 1.6f,
                MagSize = 1, ProjectileSpeed = 17f, Range = 12f, Spread = 1.5f, Noise = 1.5f, Sound = "throw", Price = 1500,
                Description = "Silent at range. Lethal on unaware targets. Retrieve them from bodies.",
            });
            Add(new WeaponDef
            {
                Id = "coin", Name = "Coins", Class = WeaponClass.Gadget, Ammo = AmmoType.Coins, Damage = 0, FireRate = 2f, MagSize = 1,
                ProjectileSpeed = 12f, Range = 9f, Spread = 3f, Noise = 6.5f, Sound = "coin", Description = "Toss to make noise and lure guards away.",
            });
            Add(new WeaponDef
            {
                Id = "pistol", Name = "Pistol", Class = WeaponClass.Pistol, Ammo = AmmoType.Pistol, Damage = 34, FireRate = 5f, MagSize = 12,
                ReloadTime = 1.3f, Spread = 2.5f, MoveSpread = 3f, Range = 16f, Recoil = 2.5f, Noise = 16f, Shake = 0.12f, CanSuppress = true,
                Sound = "pistol", Description = "Reliable 9mm sidearm. Loud without a suppressor.",
            });
            Add(new WeaponDef
            {
                Id = "silenced_pistol", Name = "Silverballer SD", Class = WeaponClass.Pistol, Ammo = AmmoType.Pistol, Damage = 38, FireRate = 4f,
                MagSize = 10, ReloadTime = 1.4f, Spread = 1.8f, MoveSpread = 2.5f, Range = 15f, Recoil = 2f, Noise = 3.2f, Suppressed = true,
                Shake = 0.06f, Sound = "pistol_sd", Price = 2600, Description = "Integrally suppressed precision pistol. The assassin's choice.",
            });
            Add(new WeaponDef
            {
                Id = "smg", Name = "Vektor SMG", Class = WeaponClass.SMG, Ammo = AmmoType.Pistol, Damage = 21, FireRate = 12f, Automatic = true,
                MagSize = 30, ReloadTime = 1.8f, Spread = 4.5f, MoveSpread = 3f, Range = 12f, Recoil = 1.1f, RecoilRecovery = 16f, Noise = 17f,
                Shake = 0.08f, CanSuppress = true, MoveSpeedMult = 0.96f, Sound = "smg", Price = 4200, UnlockAfter = "mansion_host",
                Description = "Compact bullet hose. Great up close, sprays at range.",
            });
            Add(new WeaponDef
            {
                Id = "shotgun", Name = "Breacher 12G", Class = WeaponClass.Shotgun, Ammo = AmmoType.Shells, Damage = 15, Pellets = 8, FireRate = 1.3f,
                MagSize = 6, ReloadTime = 0.45f, ShellReload = true, Spread = 11f, MoveSpread = 2f, Range = 8f, Recoil = 6f, RecoilRecovery = 18f,
                Noise = 22f, Shake = 0.35f, MoveSpeedMult = 0.92f, Sound = "shotgun", Price = 5200, UnlockAfter = "facility_voss",
                Description = "Pump-action. Devastating in corridors, useless at range.",
            });
            Add(new WeaponDef
            {
                Id = "assault_rifle", Name = "KR-7 Rifle", Class = WeaponClass.Rifle, Ammo = AmmoType.Rifle, Damage = 33, FireRate = 9f, Automatic = true,
                MagSize = 30, ReloadTime = 2.2f, Spread = 2.2f, MoveSpread = 4f, Range = 22f, Recoil = 1.6f, RecoilRecovery = 14f, Noise = 22f,
                Shake = 0.14f, CanSuppress = true, MoveSpeedMult = 0.9f, Sound = "rifle", Price = 7800, UnlockAfter = "office_cfo",
                Description = "Accurate, hard hitting and loud. For when stealth is over.",
            });
        }
    }

    public enum UpgradeKind { Suppressor, ExtendedMag, Stabilizer }

    public sealed class UpgradeDef
    {
        public string Id;          // e.g. "pistol.suppressor"
        public string WeaponId;
        public UpgradeKind Kind;
        public string Name;
        public string Description;
        public int Price;
        public string UnlockAfter;

        public static string MakeId(string weaponId, UpgradeKind k) => weaponId + "." + k.ToString().ToLowerInvariant();
    }

    public static class UpgradeCatalog
    {
        public static readonly List<UpgradeDef> All = new List<UpgradeDef>();

        static UpgradeCatalog()
        {
            Add("pistol", UpgradeKind.Suppressor, 1200);
            Add("pistol", UpgradeKind.ExtendedMag, 700);
            Add("pistol", UpgradeKind.Stabilizer, 600);
            Add("silenced_pistol", UpgradeKind.ExtendedMag, 900);
            Add("silenced_pistol", UpgradeKind.Stabilizer, 800);
            Add("smg", UpgradeKind.Suppressor, 1800, "mansion_host");
            Add("smg", UpgradeKind.ExtendedMag, 1200, "mansion_host");
            Add("smg", UpgradeKind.Stabilizer, 1000, "mansion_host");
            Add("shotgun", UpgradeKind.ExtendedMag, 1200, "facility_voss");
            Add("shotgun", UpgradeKind.Stabilizer, 900, "facility_voss");
            Add("assault_rifle", UpgradeKind.Suppressor, 2600, "office_cfo");
            Add("assault_rifle", UpgradeKind.ExtendedMag, 1500, "office_cfo");
            Add("assault_rifle", UpgradeKind.Stabilizer, 1400, "office_cfo");
        }

        private static void Add(string weapon, UpgradeKind k, int price, string unlock = null)
        {
            var w = WeaponCatalog.Get(weapon);
            string name, desc;
            switch (k)
            {
                case UpgradeKind.Suppressor: name = "Suppressor"; desc = "Gunshots are barely audible (-80% noise, -10% damage)."; break;
                case UpgradeKind.ExtendedMag: name = "Extended Magazine"; desc = "+50% magazine capacity."; break;
                default: name = "Stabilizer"; desc = "-40% recoil, -20% spread."; break;
            }
            All.Add(new UpgradeDef
            {
                Id = UpgradeDef.MakeId(weapon, k), WeaponId = weapon, Kind = k, Name = $"{w.Name} {name}", Description = desc, Price = price,
                UnlockAfter = unlock ?? w.UnlockAfter,
            });
        }

        public static UpgradeDef Get(string id) => All.Find(u => u.Id == id);

        /// <summary>Returns a copy of the weapon's stats with the given upgrades applied.</summary>
        public static WeaponDef Apply(WeaponDef baseDef, ICollection<string> upgrades)
        {
            var d = baseDef.Clone();
            if (upgrades == null) return d;
            if (upgrades.Contains(UpgradeDef.MakeId(d.Id, UpgradeKind.Suppressor)) && d.CanSuppress && !d.Suppressed)
            {
                d.Suppressed = true;
                d.Noise *= 0.2f;
                d.Damage *= 0.9f;
                d.Shake *= 0.6f;
                d.Sound += "_sd";
            }
            if (upgrades.Contains(UpgradeDef.MakeId(d.Id, UpgradeKind.ExtendedMag)))
                d.MagSize = (int)System.Math.Round(d.MagSize * 1.5f);
            if (upgrades.Contains(UpgradeDef.MakeId(d.Id, UpgradeKind.Stabilizer)))
            {
                d.Recoil *= 0.6f;
                d.Spread *= 0.8f;
                d.MoveSpread *= 0.8f;
            }
            return d;
        }
    }
}
