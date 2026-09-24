using System;
using System.Collections.Generic;

namespace ShadowContract.Core
{
    public enum FireBlock { None, Cooldown, Reloading, Empty, NoWeapon }

    /// <summary>Runtime state of one carried weapon: magazine, cooldown, reload progress and recoil bloom.</summary>
    public sealed class WeaponState
    {
        public readonly WeaponDef Def;
        public int Mag;
        public float Cooldown;
        public float ReloadTimer;       // > 0 while reloading
        public float Bloom;             // extra spread from recoil, decays over time
        public bool TriggerReleased = true;

        public WeaponState(WeaponDef def, int mag = -1)
        {
            Def = def;
            Mag = mag < 0 ? def.MagSize : mag;
        }

        public bool Reloading => ReloadTimer > 0f;

        public void Tick(float dt)
        {
            if (Cooldown > 0f) Cooldown = Math.Max(0f, Cooldown - dt);
            Bloom = Math.Max(0f, Bloom - Def.RecoilRecovery * dt);
        }

        public FireBlock CanFire(bool triggerPressedThisFrame)
        {
            if (Reloading && !(Def.ShellReload && Mag > 0)) return FireBlock.Reloading;
            if (Cooldown > 0f) return FireBlock.Cooldown;
            if (!Def.Automatic && !Def.IsMelee && !TriggerReleased && !triggerPressedThisFrame) return FireBlock.Cooldown;
            if (Def.IsGun && Mag <= 0) return FireBlock.Empty;
            return FireBlock.None;
        }

        /// <summary>
        /// Consumes a round and returns the pellet directions (radians). Spread depends on movement and aiming.
        /// </summary>
        public List<float> Fire(Rng rng, float aimAngle, bool moving, bool aiming, float accuracyMult = 1f)
        {
            var dirs = new List<float>(Def.Pellets);
            if (Def.ShellReload && Reloading) ReloadTimer = 0f; // pumping the trigger interrupts a shell reload
            Cooldown = 1f / Def.FireRate;
            TriggerReleased = false;
            if (Def.IsGun) Mag--;

            float spreadDeg = CurrentSpread(moving, aiming) * accuracyMult;
            for (int i = 0; i < Def.Pellets; i++)
            {
                float s = Def.Pellets > 1 ? rng.Range(-1f, 1f) : rng.Spread();
                dirs.Add(aimAngle + s * spreadDeg * MathUtil.Deg2Rad);
            }
            Bloom = Math.Min(Bloom + Def.Recoil, Def.Recoil * 8f + 4f);
            return dirs;
        }

        public float CurrentSpread(bool moving, bool aiming)
        {
            float s = Def.Spread + Bloom + (moving ? Def.MoveSpread : 0f);
            if (aiming) s *= Def.Pellets > 1 ? 0.8f : 0.55f;
            return s;
        }

        public bool NeedsReload => Def.IsGun && Mag < Def.MagSize;

        /// <summary>Starts a reload if there is reserve ammo. Returns true if a reload began.</summary>
        public bool StartReload(int reserve)
        {
            if (!Def.IsGun || Reloading || Mag >= Def.MagSize || reserve <= 0) return false;
            ReloadTimer = Def.ReloadTime;
            return true;
        }

        /// <summary>
        /// Advances a reload. Returns the number of rounds moved from reserve into the magazine this tick.
        /// </summary>
        public int TickReload(float dt, int reserve)
        {
            if (!Reloading) return 0;
            ReloadTimer -= dt;
            if (ReloadTimer > 0f) return 0;
            if (Def.ShellReload)
            {
                int moved = reserve > 0 && Mag < Def.MagSize ? 1 : 0;
                Mag += moved;
                // Keep loading shells while there is ammo and room.
                ReloadTimer = (Mag < Def.MagSize && reserve - moved > 0) ? Def.ReloadTime : 0f;
                return moved;
            }
            int need = Def.MagSize - Mag;
            int take = Math.Min(need, reserve);
            Mag += take;
            ReloadTimer = 0f;
            return take;
        }

        public void CancelReload() => ReloadTimer = 0f;

        /// <summary>0..1 progress of the current reload (or current shell).</summary>
        public float ReloadProgress => Reloading && Def.ReloadTime > 0f ? 1f - ReloadTimer / Def.ReloadTime : 0f;
    }

    /// <summary>Everything the player carries during a mission.</summary>
    public sealed class Inventory
    {
        public readonly WeaponState[] Slots = new WeaponState[5];
        public readonly Dictionary<AmmoType, int> Reserve = new Dictionary<AmmoType, int>();
        public readonly HashSet<string> Keys = new HashSet<string>();
        public readonly List<string> Intel = new List<string>();
        public int Medkits;
        public int CashFound;
        public int Current;

        public WeaponState CurrentWeapon => Slots[Current];

        public int GetReserve(AmmoType t) => Reserve.TryGetValue(t, out var n) ? n : 0;
        public void AddReserve(AmmoType t, int n) { if (t != AmmoType.None) Reserve[t] = Math.Max(0, GetReserve(t) + n); }

        public void SetSlot(WeaponSlot slot, WeaponState w) => Slots[(int)slot] = w;

        /// <summary>Selects a slot if it holds a weapon. Returns true when the selection changed.</summary>
        public bool Select(int index)
        {
            if (index < 0 || index >= Slots.Length || Slots[index] == null || index == Current) return false;
            CurrentWeapon?.CancelReload();
            Current = index;
            return true;
        }

        public bool Cycle(int dir)
        {
            for (int i = 1; i <= Slots.Length; i++)
            {
                int idx = ((Current + dir * i) % Slots.Length + Slots.Length) % Slots.Length;
                if (Slots[idx] != null) return Select(idx);
            }
            return false;
        }

        /// <summary>Throwables have no magazine: the reserve is the count.</summary>
        public int AmmoCountForHud(WeaponState w)
        {
            if (w == null) return 0;
            return w.Def.IsThrown ? GetReserve(w.Def.Ammo) : w.Mag;
        }
    }
}
