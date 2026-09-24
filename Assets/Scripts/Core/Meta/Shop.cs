using System;
using System.Collections.Generic;
using System.Linq;

namespace ShadowContract.Core
{
    public enum ShopCategory { Melee, Pistols, SMGs, Rifles, Shotguns, Throwables, Ammo, Gear, Upgrades }
    public enum ShopItemKind { Weapon, Upgrade, Ammo, Armor, Medkit, Gadget }
    public enum BuyResult { Ok, NotEnoughMoney, AlreadyOwned, Locked, Maxed, RequiresWeapon }

    public struct StatLine
    {
        public string Label;
        public float Value;     // 0..1 for the stat bar
        public string Text;     // exact value
        public StatLine(string label, float value, string text) { Label = label; Value = MathUtil.Clamp01(value); Text = text; }
    }

    public sealed class ShopItem
    {
        public string Id;
        public string Name;
        public string Description;
        public ShopCategory Category;
        public ShopItemKind Kind;
        public int Price;
        public string UnlockAfter;
        public string WeaponId;             // weapon / upgrade target
        public string UpgradeId;
        public AmmoType AmmoType;
        public int Amount;
        public string ArmorTier;
        public string Icon;                 // sprite name for the UI

        public List<StatLine> Stats()
        {
            var list = new List<StatLine>();
            var w = WeaponId != null ? WeaponCatalog.Get(WeaponId) : null;
            if (Kind == ShopItemKind.Weapon && w != null) return WeaponStats(w);
            if (Kind == ShopItemKind.Upgrade && w != null)
            {
                var up = UpgradeCatalog.Get(UpgradeId);
                var after = UpgradeCatalog.Apply(w, new HashSet<string> { UpgradeId });
                switch (up.Kind)
                {
                    case UpgradeKind.Suppressor:
                        list.Add(new StatLine("Noise", after.Noise / 22f, $"{w.Noise:0} -> {after.Noise:0.#} tiles"));
                        list.Add(new StatLine("Damage", after.Damage / 100f, $"{w.Damage:0} -> {after.Damage:0}"));
                        break;
                    case UpgradeKind.ExtendedMag:
                        list.Add(new StatLine("Magazine", after.MagSize / 45f, $"{w.MagSize} -> {after.MagSize}"));
                        break;
                    case UpgradeKind.Stabilizer:
                        list.Add(new StatLine("Recoil", 1f - after.Recoil / 7f, $"{w.Recoil:0.0} -> {after.Recoil:0.0}"));
                        list.Add(new StatLine("Accuracy", 1f - after.Spread / 12f, $"{w.Spread:0.0}deg -> {after.Spread:0.0}deg"));
                        break;
                }
                return list;
            }
            if (Kind == ShopItemKind.Ammo) list.Add(new StatLine("Rounds", Amount / 60f, $"+{Amount}"));
            if (Kind == ShopItemKind.Armor) list.Add(new StatLine("Armor", ProgressData.ArmorValue(ArmorTier) / 100f, $"{ProgressData.ArmorValue(ArmorTier)} points"));
            if (Kind == ShopItemKind.Medkit) list.Add(new StatLine("Heals", 0.6f, "+60 health"));
            return list;
        }

        public static List<StatLine> WeaponStats(WeaponDef w)
        {
            var list = new List<StatLine>();
            float dmg = w.Damage * w.Pellets;
            list.Add(new StatLine("Damage", dmg / 120f, w.Pellets > 1 ? $"{w.Damage:0} x{w.Pellets}" : $"{w.Damage:0}"));
            if (w.IsMelee)
            {
                list.Add(new StatLine("Speed", w.FireRate / 3f, $"{w.FireRate:0.0}/s"));
                list.Add(new StatLine("Reach", w.MeleeRange / 1.5f, $"{w.MeleeRange:0.0} m"));
                list.Add(new StatLine("Noise", 0.05f, "Silent"));
                return list;
            }
            list.Add(new StatLine("Fire rate", w.FireRate / 12f, w.Automatic ? $"{w.FireRate * 60:0} rpm auto" : $"{w.FireRate * 60:0} rpm"));
            if (w.IsGun)
            {
                list.Add(new StatLine("Magazine", w.MagSize / 45f, $"{w.MagSize}"));
                list.Add(new StatLine("Reload", 1f - (w.ShellReload ? w.ReloadTime * 3 : w.ReloadTime) / 3f, w.ShellReload ? $"{w.ReloadTime:0.00}s/shell" : $"{w.ReloadTime:0.0}s"));
                list.Add(new StatLine("Accuracy", 1f - w.Spread / 12f, $"{w.Spread:0.0}deg"));
                list.Add(new StatLine("Recoil", 1f - w.Recoil / 7f, $"{w.Recoil:0.0}"));
            }
            list.Add(new StatLine("Range", w.Range / 22f, $"{w.Range:0} m"));
            list.Add(new StatLine("Noise", w.Noise / 22f, w.Suppressed ? "Suppressed" : $"{w.Noise:0} m"));
            return list;
        }
    }

    public static class ShopService
    {
        public const int MaxMedkits = 3;
        public static readonly List<ShopItem> Catalog = BuildCatalog();

        private static List<ShopItem> BuildCatalog()
        {
            var items = new List<ShopItem>();
            foreach (var w in WeaponCatalog.All.Values)
            {
                if (w.Price <= 0) continue;
                items.Add(new ShopItem
                {
                    Id = "w." + w.Id, Name = w.Name, Description = w.Description, Kind = ShopItemKind.Weapon, WeaponId = w.Id,
                    Price = w.Price, UnlockAfter = w.UnlockAfter, Category = CategoryFor(w.Class), Icon = w.Id,
                });
            }
            foreach (var u in UpgradeCatalog.All)
                items.Add(new ShopItem
                {
                    Id = "u." + u.Id, Name = u.Name, Description = u.Description, Kind = ShopItemKind.Upgrade, WeaponId = u.WeaponId,
                    UpgradeId = u.Id, Price = u.Price, UnlockAfter = u.UnlockAfter, Category = ShopCategory.Upgrades, Icon = "up_" + u.Kind.ToString().ToLowerInvariant(),
                });
            items.Add(new ShopItem { Id = "a.pistol", Name = "9mm Ammo (48)", Description = "Fits pistols and the Vektor SMG.", Kind = ShopItemKind.Ammo, AmmoType = AmmoType.Pistol, Amount = 48, Price = 160, Category = ShopCategory.Ammo, Icon = "ammo_pistol" });
            items.Add(new ShopItem { Id = "a.rifle", Name = "5.56mm Ammo (60)", Description = "For the KR-7 rifle.", Kind = ShopItemKind.Ammo, AmmoType = AmmoType.Rifle, Amount = 60, Price = 320, Category = ShopCategory.Ammo, UnlockAfter = "office_cfo", Icon = "ammo_rifle" });
            items.Add(new ShopItem { Id = "a.shells", Name = "12G Shells (16)", Description = "Buckshot for the Breacher.", Kind = ShopItemKind.Ammo, AmmoType = AmmoType.Shells, Amount = 16, Price = 240, Category = ShopCategory.Ammo, UnlockAfter = "facility_voss", Icon = "ammo_shells" });
            items.Add(new ShopItem { Id = "a.knives", Name = "Throwing Knives (3)", Description = "Balanced blades. Recover them after use.", Kind = ShopItemKind.Ammo, AmmoType = AmmoType.Knives, Amount = 3, Price = 300, Category = ShopCategory.Ammo, WeaponId = "throwing_knives", Icon = "ammo_knives" });
            items.Add(new ShopItem { Id = "g.armor_light", Name = "Kevlar Vest", Description = "50 armor every mission. Absorbs 60% of incoming damage.", Kind = ShopItemKind.Armor, ArmorTier = "light", Price = 1800, Category = ShopCategory.Gear, Icon = "armor_light" });
            items.Add(new ShopItem { Id = "g.armor_heavy", Name = "Tactical Plate Carrier", Description = "100 armor every mission. Slightly slower.", Kind = ShopItemKind.Armor, ArmorTier = "heavy", Price = 4500, Category = ShopCategory.Gear, UnlockAfter = "facility_voss", Icon = "armor_heavy" });
            items.Add(new ShopItem { Id = "g.medkit", Name = "Medkit", Description = $"Restores 60 health (press H). Carry up to {MaxMedkits}.", Kind = ShopItemKind.Medkit, Price = 450, Category = ShopCategory.Gear, Icon = "medkit" });
            items.Add(new ShopItem { Id = "g.coin_pouch", Name = "Coin Pouch", Description = "+3 distraction coins every mission.", Kind = ShopItemKind.Gadget, UpgradeId = "coin.pouch", Price = 600, Category = ShopCategory.Gear, Icon = "coin" });
            return items;
        }

        private static ShopCategory CategoryFor(WeaponClass c)
        {
            switch (c)
            {
                case WeaponClass.Melee: return ShopCategory.Melee;
                case WeaponClass.Pistol: return ShopCategory.Pistols;
                case WeaponClass.SMG: return ShopCategory.SMGs;
                case WeaponClass.Rifle: return ShopCategory.Rifles;
                case WeaponClass.Shotgun: return ShopCategory.Shotguns;
                default: return ShopCategory.Throwables;
            }
        }

        public static ShopItem Get(string id) => Catalog.FirstOrDefault(i => i.Id == id);

        public static bool IsUnlocked(ProgressData p, ShopItem item) => item.UnlockAfter == null || p.IsCompleted(item.UnlockAfter);

        public static bool IsOwned(ProgressData p, ShopItem item)
        {
            switch (item.Kind)
            {
                case ShopItemKind.Weapon: return p.Owns(item.WeaponId);
                case ShopItemKind.Upgrade: return p.HasUpgrade(item.UpgradeId);
                case ShopItemKind.Gadget: return p.HasUpgrade(item.UpgradeId);
                case ShopItemKind.Armor: return p.Armor == item.ArmorTier || (item.ArmorTier == "light" && p.Armor == "heavy");
                default: return false;
            }
        }

        public static bool IsEquipped(ProgressData p, ShopItem item)
        {
            if (item.Kind == ShopItemKind.Weapon) return Array.IndexOf(p.Loadout, item.WeaponId) >= 0;
            if (item.Kind == ShopItemKind.Armor) return p.Armor == item.ArmorTier;
            return IsOwned(p, item);
        }

        public static BuyResult CanBuy(ProgressData p, ShopItem item)
        {
            if (!IsUnlocked(p, item)) return BuyResult.Locked;
            if (IsOwned(p, item)) return BuyResult.AlreadyOwned;
            if (item.Kind == ShopItemKind.Upgrade && !p.Owns(item.WeaponId)) return BuyResult.RequiresWeapon;
            if (item.Kind == ShopItemKind.Ammo && item.WeaponId != null && !p.Owns(item.WeaponId)) return BuyResult.RequiresWeapon;
            if (item.Kind == ShopItemKind.Medkit && p.Medkits >= MaxMedkits) return BuyResult.Maxed;
            if (item.Kind == ShopItemKind.Ammo && p.GetAmmo(item.AmmoType) >= MaxAmmo(item.AmmoType)) return BuyResult.Maxed;
            if (p.Money < item.Price) return BuyResult.NotEnoughMoney;
            return BuyResult.Ok;
        }

        public static int MaxAmmo(AmmoType t)
        {
            switch (t)
            {
                case AmmoType.Pistol: return 240;
                case AmmoType.Rifle: return 300;
                case AmmoType.Shells: return 64;
                case AmmoType.Knives: return 9;
                default: return 99;
            }
        }

        /// <summary>Attempts a purchase. Money is only taken when the purchase succeeds.</summary>
        public static BuyResult Buy(ProgressData p, ShopItem item)
        {
            var r = CanBuy(p, item);
            if (r != BuyResult.Ok) return r;
            p.Money -= item.Price;
            switch (item.Kind)
            {
                case ShopItemKind.Weapon:
                {
                    p.OwnedWeapons.Add(item.WeaponId);
                    var def = WeaponCatalog.Get(item.WeaponId);
                    int slot = (int)def.Slot;
                    // Auto-equip into an empty slot so new purchases are immediately usable.
                    if (p.Loadout[slot] == null || p.Loadout[slot] == "pistol" && def.Class == WeaponClass.Pistol) p.Loadout[slot] = def.Id;
                    // Starter ammo with a new gun / knives.
                    if (def.IsGun) p.Ammo[def.Ammo] = Math.Min(MaxAmmo(def.Ammo), p.GetAmmo(def.Ammo) + def.MagSize * 2);
                    if (def.Class == WeaponClass.Throwing) p.Ammo[AmmoType.Knives] = Math.Min(MaxAmmo(AmmoType.Knives), p.GetAmmo(AmmoType.Knives) + 3);
                    break;
                }
                case ShopItemKind.Upgrade:
                case ShopItemKind.Gadget:
                    p.Upgrades.Add(item.UpgradeId);
                    break;
                case ShopItemKind.Ammo:
                    p.Ammo[item.AmmoType] = Math.Min(MaxAmmo(item.AmmoType), p.GetAmmo(item.AmmoType) + item.Amount);
                    break;
                case ShopItemKind.Armor:
                    p.Armor = item.ArmorTier;
                    break;
                case ShopItemKind.Medkit:
                    p.Medkits++;
                    break;
            }
            return BuyResult.Ok;
        }

        /// <summary>Equips an owned weapon into its slot (or unequips when null). Returns false if not allowed.</summary>
        public static bool Equip(ProgressData p, WeaponSlot slot, string weaponId)
        {
            if (weaponId == null)
            {
                if (slot == WeaponSlot.Melee) return false; // always carry a blade
                p.Loadout[(int)slot] = null;
                return true;
            }
            var def = WeaponCatalog.Get(weaponId);
            if (def == null || !p.Owns(weaponId) || def.Slot != slot) return false;
            p.Loadout[(int)slot] = weaponId;
            return true;
        }

        public static string Describe(BuyResult r)
        {
            switch (r)
            {
                case BuyResult.Ok: return "Purchased";
                case BuyResult.NotEnoughMoney: return "Not enough money";
                case BuyResult.AlreadyOwned: return "Already owned";
                case BuyResult.Locked: return "Locked - complete more contracts";
                case BuyResult.Maxed: return "Carrying the maximum";
                case BuyResult.RequiresWeapon: return "Requires the weapon";
                default: return r.ToString();
            }
        }
    }
}
