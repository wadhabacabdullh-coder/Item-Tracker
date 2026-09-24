using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ShadowContract.Core
{
    public sealed class MissionRecord
    {
        public float BestTime;
        public string BestRating;
        public int BestPayout;
        public bool SilentAssassin;
        public int Completions;
    }

    /// <summary>Everything that persists between missions. Serialized as JSON.</summary>
    public sealed class ProgressData
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public int Money = 1500;
        public List<string> OwnedWeapons = new List<string> { "knife", "pistol", "coin" };
        public List<string> Upgrades = new List<string>();
        public Dictionary<AmmoType, int> Ammo = new Dictionary<AmmoType, int> { [AmmoType.Pistol] = 48 };
        public int Medkits = 1;
        public string Armor = "none";                  // none / light / heavy
        public string[] Loadout = { "knife", "pistol", null, null, "coin" };
        public List<string> UnlockedMissions = new List<string> { "mansion_host" };
        public List<string> CompletedMissions = new List<string>();
        public Dictionary<string, MissionRecord> Records = new Dictionary<string, MissionRecord>();
        public int TotalKills;
        public int TotalEarned;
        public int MissionsPlayed;

        public bool Owns(string weaponId) => OwnedWeapons.Contains(weaponId);
        public bool HasUpgrade(string upgradeId) => Upgrades.Contains(upgradeId);
        public int GetAmmo(AmmoType t) => Ammo.TryGetValue(t, out int n) ? n : 0;
        public bool IsCompleted(string missionId) => CompletedMissions.Contains(missionId);
        public bool IsUnlocked(string missionId) => UnlockedMissions.Contains(missionId);

        public static int ArmorValue(string armor) => armor == "heavy" ? 100 : armor == "light" ? 50 : 0;

        /// <summary>Fixes anything a hand-edited or old save could get wrong.</summary>
        public void Sanitize()
        {
            OwnedWeapons = OwnedWeapons ?? new List<string>();
            foreach (var basic in new[] { "knife", "pistol", "coin" })
                if (!OwnedWeapons.Contains(basic)) OwnedWeapons.Add(basic);
            OwnedWeapons.RemoveAll(id => WeaponCatalog.Get(id) == null);
            Upgrades = Upgrades ?? new List<string>();
            Upgrades.RemoveAll(id => UpgradeCatalog.Get(id) == null && id != "coin.pouch");
            Ammo = Ammo ?? new Dictionary<AmmoType, int>();
            UnlockedMissions = UnlockedMissions ?? new List<string>();
            if (!UnlockedMissions.Contains("mansion_host")) UnlockedMissions.Add("mansion_host");
            CompletedMissions = CompletedMissions ?? new List<string>();
            Records = Records ?? new Dictionary<string, MissionRecord>();
            if (Armor != "light" && Armor != "heavy") Armor = "none";
            Money = Math.Max(0, Money);
            Medkits = MathUtil.Clamp(Medkits, 0, ShopService.MaxMedkits);
            if (Loadout == null || Loadout.Length != 5) Loadout = new[] { "knife", "pistol", null, null, "coin" };
            // Every equipped weapon must be owned and sit in the right slot.
            for (int i = 0; i < 5; i++)
            {
                var def = Loadout[i] != null ? WeaponCatalog.Get(Loadout[i]) : null;
                if (def == null || !Owns(def.Id) || (int)def.Slot != i) Loadout[i] = null;
            }
            if (Loadout[0] == null) Loadout[0] = "knife";
            if (Loadout[4] == null) Loadout[4] = "coin";
            Version = CurrentVersion;
        }

        /// <summary>Builds the mission loadout, topping up free "standard issue" pistol ammo so a player can never soft-lock.</summary>
        public LoadoutConfig BuildLoadout()
        {
            var lo = new LoadoutConfig
            {
                SlotWeapons = (string[])Loadout.Clone(),
                Upgrades = new HashSet<string>(Upgrades),
                Medkits = Medkits,
                Armor = ArmorValue(Armor),
                SpeedMult = Armor == "heavy" ? 0.94f : 1f,
            };
            foreach (var kv in Ammo) lo.Ammo[kv.Key] = kv.Value;
            lo.Ammo[AmmoType.Pistol] = Math.Max(GetAmmo(AmmoType.Pistol), 36);
            lo.Ammo[AmmoType.Coins] = 3 + (HasUpgrade("coin.pouch") ? 3 : 0);
            if (Owns("throwing_knives") && GetAmmo(AmmoType.Knives) <= 0 && Loadout[3] != null) lo.Ammo[AmmoType.Knives] = 2;
            return lo;
        }

        /// <summary>Applies a mission result: money, ammo, unlocks, records.</summary>
        public void ApplyResult(MissionResult r)
        {
            MissionsPlayed++;
            if (!r.Success) return;
            Money += r.Total;
            TotalEarned += r.Total;
            TotalKills += r.Kills;
            foreach (var kv in r.AmmoLeft)
                if (kv.Key != AmmoType.Coins) Ammo[kv.Key] = Math.Max(0, kv.Value);
            Medkits = MathUtil.Clamp(r.MedkitsLeft, 0, ShopService.MaxMedkits);

            if (!CompletedMissions.Contains(r.MissionId)) CompletedMissions.Add(r.MissionId);
            foreach (var m in MissionCatalog.All)
                if (m.UnlockedBy == r.MissionId && !UnlockedMissions.Contains(m.Id)) UnlockedMissions.Add(m.Id);

            if (!Records.TryGetValue(r.MissionId, out var rec)) Records[r.MissionId] = rec = new MissionRecord { BestTime = float.MaxValue };
            rec.Completions++;
            rec.BestTime = Math.Min(rec.BestTime, r.Time);
            rec.BestPayout = Math.Max(rec.BestPayout, r.Total);
            if (!r.Spotted) rec.SilentAssassin = true;
            if (rec.BestRating == null || RatingRank(r.Rating) > RatingRank(rec.BestRating)) rec.BestRating = r.Rating;
        }

        public static int RatingRank(string rating)
        {
            switch (rating)
            {
                case "Silent Assassin": return 5;
                case "Shadow": return 4;
                case "Professional": return 3;
                case "Mercenary": return 2;
                case "Butcher": return 1;
                default: return 0;
            }
        }
    }

    public sealed class SettingsData
    {
        public float MasterVolume = 0.8f;
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.9f;
        public bool ScreenShake = true;
        public bool Fullscreen = true;
        public bool ShowVisionCones = true;
        public Difficulty Difficulty = Difficulty.Normal;
        public Dictionary<string, string> Bindings = new Dictionary<string, string>();

        public void Sanitize()
        {
            MasterVolume = MathUtil.Clamp01(MasterVolume);
            MusicVolume = MathUtil.Clamp01(MusicVolume);
            SfxVolume = MathUtil.Clamp01(SfxVolume);
            Bindings = Bindings ?? new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// JSON persistence with atomic writes and a backup copy, so a crash while saving never loses progress.
    /// </summary>
    public static class SaveStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,   // don't merge saved lists into default lists
            NullValueHandling = NullValueHandling.Include,
            Converters = { new StringEnumConverter() },
        };

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static void Save(string path, object data)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(data));
            if (File.Exists(path))
            {
                string bak = path + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Replace(tmp, path, bak);
            }
            else File.Move(tmp, path);
        }

        /// <summary>Loads a file, falling back to the backup, then to a fresh object. Never throws.</summary>
        public static T Load<T>(string path, out bool recovered) where T : new()
        {
            recovered = false;
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    var obj = Deserialize<T>(File.ReadAllText(candidate));
                    if (obj == null) continue;
                    recovered = candidate != path;
                    return obj;
                }
                catch (Exception)
                {
                    // corrupt file: try the next candidate
                }
            }
            return new T();
        }
    }
}
