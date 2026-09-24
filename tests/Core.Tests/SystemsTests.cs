using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShadowContract.Core;

namespace ShadowContract.Tests
{
    public class WeaponTests
    {
        [Test]
        public void FireRateIsRespected()
        {
            var w = new WeaponState(WeaponCatalog.Get("smg"));
            var rng = new Rng(1);
            int shots = 0;
            for (float t = 0; t < 1f; t += 1f / 120f)
            {
                w.Tick(1f / 120f);
                if (w.CanFire(false) == FireBlock.None) { w.Fire(rng, 0, false, false); shots++; }
            }
            Assert.AreEqual(12, shots, 1, "12 rounds per second");
        }

        [Test]
        public void SemiAutoNeedsTriggerRelease()
        {
            var w = new WeaponState(WeaponCatalog.Get("pistol"));
            var rng = new Rng(1);
            w.Fire(rng, 0, false, false);
            w.Tick(1f);
            Assert.AreEqual(FireBlock.Cooldown, w.CanFire(false), "holding the trigger does not refire");
            Assert.AreEqual(FireBlock.None, w.CanFire(true), "a new press fires");
        }

        [Test]
        public void ReloadMovesAmmoFromReserve()
        {
            var w = new WeaponState(WeaponCatalog.Get("pistol"), 2);
            Assert.IsTrue(w.StartReload(5));
            Assert.AreEqual(0, w.TickReload(0.5f, 5));
            int moved = w.TickReload(1f, 5);
            Assert.AreEqual(5, moved, "only what the reserve holds");
            Assert.AreEqual(7, w.Mag);
            Assert.IsFalse(w.Reloading);
        }

        [Test]
        public void ShotgunLoadsShellByShell()
        {
            var w = new WeaponState(WeaponCatalog.Get("shotgun"), 3);
            Assert.IsTrue(w.StartReload(10));
            int total = 0;
            for (int i = 0; i < 20 && w.Reloading; i++) total += w.TickReload(0.5f, 10 - total);
            Assert.AreEqual(6, w.Mag);
            Assert.AreEqual(3, total);
        }

        [Test]
        public void ShotgunFiresPellets()
        {
            var w = new WeaponState(WeaponCatalog.Get("shotgun"));
            Assert.AreEqual(8, w.Fire(new Rng(3), 0f, false, false).Count);
        }

        [Test]
        public void SuppressorReducesNoise()
        {
            var baseDef = WeaponCatalog.Get("pistol");
            var sd = UpgradeCatalog.Apply(baseDef, new System.Collections.Generic.HashSet<string> { "pistol.suppressor", "pistol.extendedmag" });
            Assert.IsTrue(sd.Suppressed);
            Assert.Less(sd.Noise, baseDef.Noise * 0.25f);
            Assert.AreEqual(18, sd.MagSize);
            Assert.AreEqual(12, baseDef.MagSize, "catalog entry untouched");
        }

        [Test]
        public void AllWeaponsHaveSaneStats()
        {
            foreach (var w in WeaponCatalog.All.Values)
            {
                Assert.Greater(w.FireRate, 0, w.Id);
                if (w.IsGun)
                {
                    Assert.Greater(w.MagSize, 0, w.Id);
                    Assert.Greater(w.ReloadTime, 0, w.Id);
                    Assert.Greater(w.Range, 0, w.Id);
                    Assert.AreNotEqual(AmmoType.None, w.Ammo, w.Id);
                }
            }
            Assert.GreaterOrEqual(WeaponCatalog.All.Values.Count(w => w.IsMelee || w.Class == WeaponClass.Throwing), 3);
            Assert.GreaterOrEqual(WeaponCatalog.All.Values.Count(w => w.IsGun), 5);
        }
    }

    public class ShopAndSaveTests
    {
        [Test]
        public void CannotBuyWithoutMoney()
        {
            var p = new ProgressData { Money = 100 };
            var item = ShopService.Get("w.silenced_pistol");
            Assert.AreEqual(BuyResult.NotEnoughMoney, ShopService.Buy(p, item));
            Assert.AreEqual(100, p.Money, "money untouched");
            Assert.IsFalse(p.Owns("silenced_pistol"));
        }

        [Test]
        public void BuyingDeductsAndEquips()
        {
            var p = new ProgressData { Money = 5000 };
            var item = ShopService.Get("w.silenced_pistol");
            Assert.AreEqual(BuyResult.Ok, ShopService.Buy(p, item));
            Assert.AreEqual(5000 - item.Price, p.Money);
            Assert.IsTrue(p.Owns("silenced_pistol"));
            Assert.AreEqual("silenced_pistol", p.Loadout[1], "replaces the starter pistol");
            Assert.AreEqual(BuyResult.AlreadyOwned, ShopService.Buy(p, item));
        }

        [Test]
        public void LockedItemsNeedProgress()
        {
            var p = new ProgressData { Money = 99999 };
            Assert.AreEqual(BuyResult.Locked, ShopService.Buy(p, ShopService.Get("w.smg")));
            p.CompletedMissions.Add("mansion_host");
            Assert.AreEqual(BuyResult.Ok, ShopService.Buy(p, ShopService.Get("w.smg")));
            Assert.AreEqual("smg", p.Loadout[2]);
        }

        [Test]
        public void UpgradeRequiresWeaponAndMedkitsCap()
        {
            var p = new ProgressData { Money = 99999 };
            p.CompletedMissions.Add("mansion_host");
            Assert.AreEqual(BuyResult.RequiresWeapon, ShopService.Buy(p, ShopService.Get("u.smg.suppressor")));
            Assert.AreEqual(BuyResult.Ok, ShopService.Buy(p, ShopService.Get("u.pistol.suppressor")));
            for (int i = 0; i < 5; i++) ShopService.Buy(p, ShopService.Get("g.medkit"));
            Assert.AreEqual(ShopService.MaxMedkits, p.Medkits);
        }

        [Test]
        public void EveryShopItemHasPriceAndStatsOrDescription()
        {
            Assert.GreaterOrEqual(ShopService.Catalog.Count, 20);
            foreach (var i in ShopService.Catalog)
            {
                Assert.Greater(i.Price, 0, i.Id);
                Assert.IsNotEmpty(i.Description, i.Id);
                Assert.IsNotNull(i.Stats(), i.Id);
            }
            foreach (var cat in new[] { ShopCategory.Melee, ShopCategory.Pistols, ShopCategory.SMGs, ShopCategory.Rifles, ShopCategory.Shotguns, ShopCategory.Ammo, ShopCategory.Gear, ShopCategory.Upgrades })
                Assert.IsTrue(ShopService.Catalog.Any(i => i.Category == cat), cat.ToString());
        }

        [Test]
        public void SaveRoundTripAndCorruptionRecovery()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sc_test_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "progress.json");
            var p = new ProgressData { Money = 4321 };
            p.OwnedWeapons.Add("smg");
            p.Upgrades.Add("pistol.suppressor");
            p.Ammo[AmmoType.Rifle] = 77;
            p.CompletedMissions.Add("mansion_host");
            SaveStore.Save(path, p);
            p.Money = 9999;
            SaveStore.Save(path, p);  // second save creates a backup of the first

            var loaded = SaveStore.Load<ProgressData>(path, out bool rec);
            Assert.IsFalse(rec);
            Assert.AreEqual(9999, loaded.Money);
            Assert.AreEqual(1, loaded.OwnedWeapons.Count(w => w == "knife"), "lists are replaced, not merged with defaults");
            Assert.Contains("smg", loaded.OwnedWeapons);
            Assert.AreEqual(77, loaded.GetAmmo(AmmoType.Rifle));

            File.WriteAllText(path, "{ this is not json");
            var recovered = SaveStore.Load<ProgressData>(path, out rec);
            Assert.IsTrue(rec, "fell back to the backup");
            Assert.AreEqual(4321, recovered.Money);

            File.WriteAllText(path + ".bak", "garbage");
            var fresh = SaveStore.Load<ProgressData>(path, out _);
            Assert.AreEqual(new ProgressData().Money, fresh.Money, "fresh profile when everything is corrupt");
            Directory.Delete(dir, true);
        }

        [Test]
        public void SanitizeRepairsBadLoadout()
        {
            var p = new ProgressData();
            p.Loadout = new[] { "smg", "assault_rifle", "pistol", null, null };
            p.Money = -50;
            p.Sanitize();
            Assert.AreEqual("knife", p.Loadout[0]);
            Assert.IsNull(p.Loadout[1], "not owned");
            Assert.IsNull(p.Loadout[2], "wrong slot");
            Assert.AreEqual("coin", p.Loadout[4]);
            Assert.AreEqual(0, p.Money);
        }

        [Test]
        public void ResultUnlocksNextMissionsAndPays()
        {
            var p = new ProgressData { Money = 0 };
            var r = new MissionResult { MissionId = "mansion_host", Success = true, Total = 5000, Rating = "Shadow" };
            r.AmmoLeft[AmmoType.Pistol] = 20;
            p.ApplyResult(r);
            Assert.AreEqual(5000, p.Money);
            Assert.IsTrue(p.IsUnlocked("facility_voss"));
            Assert.AreEqual(20, p.GetAmmo(AmmoType.Pistol));
            Assert.AreEqual("Shadow", p.Records["mansion_host"].BestRating);

            var failed = new MissionResult { MissionId = "facility_voss", Success = false };
            p.ApplyResult(failed);
            Assert.AreEqual(5000, p.Money);
            Assert.IsFalse(p.IsCompleted("facility_voss"));
        }

        [Test]
        public void LoadoutTopsUpStandardIssueAmmo()
        {
            var p = new ProgressData();
            p.Ammo[AmmoType.Pistol] = 0;
            var lo = p.BuildLoadout();
            Assert.GreaterOrEqual(lo.Ammo[AmmoType.Pistol], 36);
            Assert.AreEqual(3, lo.Ammo[AmmoType.Coins]);
        }
    }

    public class MissionCatalogTests
    {
        [Test]
        public void MissionsReferenceRealMapsAndTargets()
        {
            Assert.GreaterOrEqual(MissionCatalog.All.Count, 6);
            foreach (var m in MissionCatalog.All)
            {
                var map = TestMaps.Load(m.MapId);
                Assert.IsTrue(map.MissionEntities.ContainsKey(m.Id), $"{m.Id} has entities on {m.MapId}");
                var ents = map.EntitiesFor(m.Id).ToList();
                foreach (var o in m.Objectives)
                {
                    if (o.Type == ObjectiveType.Eliminate)
                        foreach (var t in o.TargetIds)
                            Assert.IsTrue(ents.Any(e => e.Kind == "target" && e.Str("id") == t), $"{m.Id}: target {t}");
                    if (o.Type == ObjectiveType.Retrieve && !o.ItemId.StartsWith("keycard_"))
                        Assert.IsTrue(ents.Any(e => e.Kind == "pickup" && e.Str("id") == o.ItemId), $"{m.Id}: item {o.ItemId}");
                    if (o.Type == ObjectiveType.Reach)
                        Assert.IsTrue(ents.Any(e => e.Kind == "zone" && e.Str("name") == o.ZoneName), $"{m.Id}: zone {o.ZoneName}");
                    if (o.Type == ObjectiveType.Download)
                        Assert.IsTrue(ents.Any(e => e.Kind == "terminal" && e.Str("id") == o.ItemId), $"{m.Id}: terminal {o.ItemId}");
                }
                Assert.IsTrue(m.Objectives.Last().Type == ObjectiveType.Extract);
                if (m.UnlockedBy != null) Assert.IsNotNull(MissionCatalog.Get(m.UnlockedBy));
            }
        }

        [Test]
        public void EveryMissionIsReachableThroughProgression()
        {
            var unlocked = new System.Collections.Generic.HashSet<string> { "mansion_host" };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in MissionCatalog.All)
                    if (!unlocked.Contains(m.Id) && m.UnlockedBy != null && unlocked.Contains(m.UnlockedBy)) { unlocked.Add(m.Id); changed = true; }
            }
            Assert.AreEqual(MissionCatalog.All.Count, unlocked.Count);
        }
    }
}
