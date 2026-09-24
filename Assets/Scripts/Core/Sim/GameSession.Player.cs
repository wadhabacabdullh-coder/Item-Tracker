using System;

namespace ShadowContract.Core
{
    public sealed partial class GameSession
    {
        public const float WalkSpeed = 3.7f;
        public const float SprintSpeed = 6.1f;
        public const float CrouchSpeed = 1.9f;

        private float _msgThrottle;

        private void UpdatePlayer(float dt, PlayerInput input)
        {
            var p = Player;
            var inv = p.Inventory;
            _msgThrottle -= dt;
            p.LastShotNoise += dt;
            p.DamageFlash = Math.Max(0f, p.DamageFlash - dt);
            foreach (var w in inv.Slots) w?.Tick(dt);
            if (!input.FireHeld && inv.CurrentWeapon != null) inv.CurrentWeapon.TriggerReleased = true;

            if (p.ActionLock > 0f)
            {
                p.ActionLock -= dt;
                p.Moving = false;
                if (p.ActionLock <= 0f) p.ActionLabel = null;
                return;
            }

            // ---- hidden in a closet: only interaction (to leave) is possible
            if (p.Hidden)
            {
                p.Moving = false;
                p.Sprinting = false;
                if (input.InteractPressed) LeaveHiding();
                return;
            }

            // ---- stance
            bool inVentTile = World.TileAt((int)Math.Floor(p.Pos.x), (int)Math.Floor(p.Pos.y)).Kind == TileKind.Vent;
            if (input.CrouchPressed)
            {
                if (p.Crouched)
                {
                    if (!World.CircleOverlapsSolid(p.Pos, p.Radius, MoverKind.Player)) p.Crouched = false;
                    else if (_msgThrottle <= 0f) { Message("Not enough room to stand"); _msgThrottle = 2f; }
                }
                else p.Crouched = true;
            }

            var cur = inv.CurrentWeapon;
            bool dragging = p.DraggingBody >= 0;
            p.Aiming = input.AltHeld && cur != null && (cur.Def.IsGun || cur.Def.IsThrown) && !dragging;

            Vec2 move = input.Move;
            if (move.SqrLength > 1f) move = move.Normalized;
            p.Moving = move.SqrLength > 0.01f;

            p.Sprinting = input.SprintHeld && p.Moving && !p.Aiming && !dragging && !inVentTile;
            if (p.Sprinting && p.Crouched)
            {
                if (!World.CircleOverlapsSolid(p.Pos, p.Radius, MoverKind.Player)) p.Crouched = false;
                else p.Sprinting = false;
            }

            float speed = p.Sprinting ? SprintSpeed : p.Crouched ? CrouchSpeed : WalkSpeed;
            if (p.Aiming) speed *= 0.65f;
            if (dragging) speed = Math.Min(speed, 2.1f);
            if (cur != null) speed *= cur.Def.MoveSpeedMult;
            speed *= p.SpeedMult;

            // ---- movement (bump-open unlocked doors first)
            if (p.Moving)
            {
                Vec2 delta = move * speed * dt;
                BumpDoors(p.Pos, move);
                var mover = p.Crouched ? MoverKind.PlayerCrouched : MoverKind.Player;
                Vec2 before = p.Pos;
                p.Pos = World.MoveCircle(p.Pos, p.Radius, delta, mover);
                float moved = Vec2.Distance(before, p.Pos);
                p.MoveAnim += moved;
                p.Velocity = (p.Pos - before) / dt;
                FootstepCheck(moved);
            }
            else p.Velocity = Vec2.Zero;

            p.InVent = World.TileAt((int)Math.Floor(p.Pos.x), (int)Math.Floor(p.Pos.y)).Kind == TileKind.Vent;
            if (p.InVent) p.Crouched = true;

            // ---- facing follows the mouse
            Vec2 aimDir = input.Aim - p.Pos;
            if (aimDir.SqrLength > 0.0001f) p.Facing = aimDir.Angle;

            // ---- dragged body follows
            if (dragging)
            {
                var body = Bodies.Find(b => b.Id == p.DraggingBody);
                if (body == null || body.Hidden) p.DraggingBody = -1;
                else
                {
                    Vec2 back = p.Pos - Vec2.FromAngle(p.Facing) * 0.55f;
                    body.Pos = World.MoveCircle(body.Pos, 0.2f, back - body.Pos, MoverKind.Player);
                    body.Facing = (p.Pos - body.Pos).Angle;
                }
            }

            // ---- weapon selection
            if (input.SelectSlot >= 0 && inv.Select(input.SelectSlot))
                Emit(EvType.WeaponSwitch, p.Pos, inv.CurrentWeapon.Def.Name, input.SelectSlot, 0, inv.CurrentWeapon.Def.Sound);
            else if (input.Cycle != 0 && inv.Cycle(input.Cycle))
                Emit(EvType.WeaponSwitch, p.Pos, inv.CurrentWeapon.Def.Name, inv.Current, 0, inv.CurrentWeapon.Def.Sound);
            cur = inv.CurrentWeapon;

            // ---- reload
            if (cur != null && cur.Def.IsGun)
            {
                if (input.ReloadPressed && cur.StartReload(inv.GetReserve(cur.Def.Ammo)))
                    Emit(EvType.ReloadStart, p.Pos, cur.Def.Name, cur.Def.ReloadTime, 0, cur.Def.Sound);
                if (cur.Reloading)
                {
                    int moved = cur.TickReload(dt, inv.GetReserve(cur.Def.Ammo));
                    if (moved > 0)
                    {
                        inv.AddReserve(cur.Def.Ammo, -moved);
                        if (!cur.Reloading) Emit(EvType.ReloadDone, p.Pos, cur.Def.Name, 0, 0, cur.Def.Sound);
                        else if (cur.Def.ShellReload) Emit(EvType.ReloadStart, p.Pos, "shell", cur.Def.ReloadTime, 0, cur.Def.Sound);
                    }
                }
            }

            // ---- attacks
            if (cur != null && !dragging && !p.InVent)
            {
                bool wantFire = cur.Def.Automatic ? input.FireHeld : input.FirePressed || (cur.Def.IsMelee && input.FireHeld);
                if (wantFire) PlayerAttack(cur, input.FirePressed);
                if (input.AltPressed && cur.Def.IsMelee) TrySubdue();
            }

            // ---- medkit
            if (input.MedkitPressed) UseMedkit();

            // ---- interaction
            UpdateInteraction(dt, input);

            // ---- walk-over pickups & looting
            AutoPickups();
        }

        private void FootstepCheck(float moved)
        {
            var p = Player;
            p.FootstepDist += moved;
            float stride = p.Sprinting ? 1.25f : p.Crouched ? 0.8f : 0.95f;
            if (p.FootstepDist < stride) return;
            p.FootstepDist = 0f;
            var t = World.TileAt((int)Math.Floor(p.Pos.x), (int)Math.Floor(p.Pos.y));
            float loud = p.Sprinting ? 1f : p.Crouched ? 0.15f : 0.45f;
            Emit(new GameEvent { Type = EvType.Footstep, Pos = p.Pos, Value = loud, ActorId = 0, Text = SurfaceName(t) });
            if (p.Sprinting) EmitNoise(p.Pos, 5.5f, NoiseKind.Footstep, p);
            else if (!p.Crouched) EmitNoise(p.Pos, 1.7f, NoiseKind.Footstep, p);
        }

        public static string SurfaceName(Tile t)
        {
            if (t.Kind == TileKind.Vent) return "metal";
            switch (t.Floor)
            {
                case FloorStyle.Grass: return "grass";
                case FloorStyle.C: return "carpet";
                case FloorStyle.B: return "wood";
                case FloorStyle.D: return "tile";
                case FloorStyle.Path: return "gravel";
                default: return "hard";
            }
        }

        private void BumpDoors(Vec2 pos, Vec2 dir)
        {
            Vec2 probe = pos + dir.Normalized * (Player.Radius + 0.25f);
            var d = World.DoorAt((int)Math.Floor(probe.x), (int)Math.Floor(probe.y));
            if (d == null || d.Open) return;
            if (!d.Locked) OpenDoor(d, Player, false);
            else if (d.Type != DoorType.Secret && _msgThrottle <= 0f)
            {
                if (Player.Inventory.Keys.Contains(d.KeyId)) Message($"Press [Interact] to unlock with the {d.KeyId} keycard");
                else { Message($"Locked - requires a {d.KeyId} keycard"); Emit(EvType.DoorLocked, d.Cell.Center); }
                _msgThrottle = 2.5f;
            }
        }

        private void UseMedkit()
        {
            var p = Player;
            if (p.Inventory.Medkits <= 0) { Message("No medkits"); return; }
            if (p.Health >= p.MaxHealth) { Message("Health is full"); return; }
            p.Inventory.Medkits--;
            p.Health = Math.Min(p.MaxHealth, p.Health + 60f);
            p.ActionLock = 0.6f;
            p.ActionLabel = "Healing";
            Emit(EvType.Medkit, p.Pos, null, p.Health);
        }

        // ------------------------------------------------------------------ pickups

        private void AutoPickups()
        {
            var p = Player;
            foreach (var pk in Pickups)
            {
                if (pk.Taken || pk.RequiresInteract) continue;
                if (Vec2.SqrDistance(pk.Pos, p.Pos) > 0.75f * 0.75f) continue;
                TakePickup(pk);
            }
            foreach (var b in Bodies)
            {
                if (b.Looted || b.Hidden || Vec2.SqrDistance(b.Pos, p.Pos) > 0.9f * 0.9f) continue;
                LootBody(b);
            }
        }

        public void TakePickup(Pickup pk)
        {
            var inv = Player.Inventory;
            string msg = null;
            switch (pk.Kind)
            {
                case PickupKind.Ammo:
                {
                    // Ammo boxes feed whatever the player's current gun uses (pistol by default).
                    var gun = inv.CurrentWeapon != null && inv.CurrentWeapon.Def.IsGun ? inv.CurrentWeapon.Def : inv.Slots[1]?.Def;
                    var type = gun?.Ammo ?? pk.AmmoType;
                    int amount = type == AmmoType.Shells ? Math.Max(4, pk.Amount / 4) : type == AmmoType.Rifle ? pk.Amount : pk.Amount;
                    inv.AddReserve(type, amount);
                    msg = $"+{amount} {AmmoName(type)}";
                    break;
                }
                case PickupKind.Medkit:
                    if (inv.Medkits >= 3) { if (_msgThrottle <= 0f) { Message("Medkits full (3)"); _msgThrottle = 3f; } return; }
                    inv.Medkits++; msg = "+1 Medkit"; break;
                case PickupKind.Armor:
                    if (Player.Armor >= Math.Max(Player.MaxArmor, 50f)) return;
                    Player.MaxArmor = Math.Max(Player.MaxArmor, 50f);
                    Player.Armor = Math.Min(Player.MaxArmor, Player.Armor + pk.Amount);
                    msg = $"+{pk.Amount} Armor"; break;
                case PickupKind.Cash:
                    inv.CashFound += pk.Amount; msg = $"+${pk.Amount}"; break;
                case PickupKind.Keycard:
                    inv.Keys.Add(pk.ItemId); msg = $"Picked up {pk.Name}"; break;
                case PickupKind.Intel:
                    inv.Intel.Add(pk.ItemId); msg = $"Acquired: {pk.Name}"; break;
                case PickupKind.Knives:
                    inv.AddReserve(AmmoType.Knives, pk.Amount);
                    if (inv.Slots[(int)WeaponSlot.Throwing] == null) inv.SetSlot(WeaponSlot.Throwing, new WeaponState(WeaponCatalog.Get("throwing_knives")));
                    msg = pk.Amount == 1 ? "Recovered throwing knife" : $"+{pk.Amount} Throwing knives"; break;
                case PickupKind.Coins:
                    inv.AddReserve(AmmoType.Coins, pk.Amount); msg = "+1 Coin"; break;
                case PickupKind.Weapon:
                {
                    var def = WeaponCatalog.Get(pk.ItemId);
                    var ws = new WeaponState(def, def.MagSize);
                    inv.SetSlot(def.Slot, ws);
                    inv.AddReserve(def.Ammo, pk.Amount);
                    inv.Select((int)def.Slot);
                    msg = $"Picked up {def.Name}";
                    break;
                }
            }
            pk.Taken = true;
            Emit(new GameEvent { Type = EvType.Pickup, Pos = pk.Pos, Text = msg, Sound = pk.Kind.ToString(), ActorId = pk.Id });
        }

        public static string AmmoName(AmmoType t)
        {
            switch (t)
            {
                case AmmoType.Pistol: return "9mm";
                case AmmoType.Rifle: return "5.56mm";
                case AmmoType.Shells: return "shells";
                case AmmoType.Knives: return "knives";
                case AmmoType.Coins: return "coins";
                default: return "";
            }
        }

        private void LootBody(Body b)
        {
            b.Looted = true;
            var inv = Player.Inventory;
            var n = b.Npc;
            string msg = null;
            if (n.CarriedKey != null && !inv.Keys.Contains(n.CarriedKey))
            {
                inv.Keys.Add(n.CarriedKey);
                msg = $"Took {Capitalize(n.CarriedKey)} Keycard";
            }
            if (n.Weapon != null && n.Weapon.Def.IsGun)
            {
                int amount = Math.Max(4, n.Weapon.Def.MagSize / 2);
                inv.AddReserve(n.Weapon.Def.Ammo, amount);
                msg = (msg != null ? msg + ", " : "") + $"+{amount} {AmmoName(n.Weapon.Def.Ammo)}";
            }
            if (b.KnivesInside > 0)
            {
                inv.AddReserve(AmmoType.Knives, b.KnivesInside);
                msg = (msg != null ? msg + ", " : "") + $"recovered {b.KnivesInside} knife";
                b.KnivesInside = 0;
            }
            if (msg != null) Emit(new GameEvent { Type = EvType.Pickup, Pos = b.Pos, Text = msg, Sound = "Loot" });
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
