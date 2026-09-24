using System;
using System.Collections.Generic;

namespace ShadowContract.Core
{
    public sealed partial class GameSession
    {
        // ------------------------------------------------------------------ player attacks

        private void PlayerAttack(WeaponState w, bool pressedThisFrame)
        {
            var p = Player;
            var inv = p.Inventory;
            var block = w.CanFire(pressedThisFrame);
            if (block == FireBlock.Empty)
            {
                if (pressedThisFrame)
                {
                    Emit(EvType.DryFire, p.Pos, null, 0, 0, w.Def.Sound);
                    if (w.StartReload(inv.GetReserve(w.Def.Ammo)))
                        Emit(EvType.ReloadStart, p.Pos, w.Def.Name, w.Def.ReloadTime, 0, w.Def.Sound);
                    else if (_msgThrottle <= 0f) { Message($"Out of {AmmoName(w.Def.Ammo)}"); _msgThrottle = 2f; }
                }
                return;
            }
            if (block != FireBlock.None) return;

            if (w.Def.IsMelee) { MeleeAttack(w); return; }

            if (w.Def.IsThrown)
            {
                if (inv.GetReserve(w.Def.Ammo) <= 0)
                {
                    if (pressedThisFrame && _msgThrottle <= 0f) { Message($"No {AmmoName(w.Def.Ammo)} left"); _msgThrottle = 2f; }
                    return;
                }
                inv.AddReserve(w.Def.Ammo, -1);
                var dirs = w.Fire(Rng, p.Facing, p.Moving, p.Aiming);
                ThrowProjectile(w.Def, dirs[0]);
                return;
            }

            // Firearm
            var angles = w.Fire(Rng, p.Facing, p.Moving, p.Aiming);
            ShotsFired++;
            Vec2 muzzle = p.Pos + Vec2.FromAngle(p.Facing) * 0.55f;
            // Don't shoot from inside a wall when hugging it.
            if (!World.HasLineOfSight(p.Pos, muzzle)) muzzle = p.Pos;
            foreach (float a in angles)
                Hitscan(p, muzzle, a, w.Def.Range, w.Def.Damage);
            Emit(new GameEvent { Type = EvType.Shot, Pos = muzzle, Angle = p.Facing, Sound = w.Def.Sound, ActorId = 0, Flag = w.Def.Suppressed, Value = w.Def.Shake });
            Emit(new GameEvent { Type = EvType.Casing, Pos = p.Pos, Angle = p.Facing - MathUtil.Pi / 2f, Sound = w.Def.Class == WeaponClass.Shotgun ? "shell" : "casing" });
            EmitNoise(p.Pos, w.Def.Noise, w.Def.Suppressed ? NoiseKind.SuppressedShot : NoiseKind.Gunshot, p);
            p.LastShotNoise = 0f;
            // Firing a loud gun in view of anyone gives you away immediately.
            if (!w.Def.Suppressed) WitnessesSeePlayer(1f);

            if (w.Mag == 0 && inv.GetReserve(w.Def.Ammo) > 0 && w.StartReload(inv.GetReserve(w.Def.Ammo)))
                Emit(EvType.ReloadStart, p.Pos, w.Def.Name, w.Def.ReloadTime, 0, w.Def.Sound);
        }

        private void MeleeAttack(WeaponState w)
        {
            var p = Player;
            w.Fire(Rng, p.Facing, false, false);
            Emit(new GameEvent { Type = EvType.Melee, Pos = p.Pos, Angle = p.Facing, Sound = w.Def.Sound, ActorId = 0, Value = w.Def.MeleeArc });

            Npc best = null;
            float bestD = float.MaxValue;
            foreach (var n in Npcs)
            {
                if (n.Down || n.IsCamera) continue;
                Vec2 to = n.Pos - p.Pos;
                float d = to.Length;
                if (d > w.Def.MeleeRange + n.Radius) continue;
                if (Math.Abs(MathUtil.AngleDelta(p.Facing, to.Angle)) > w.Def.MeleeArc * 0.5f * MathUtil.Deg2Rad + 0.2f) continue;
                if (!World.HasLineOfSight(p.Pos, n.Pos)) continue;
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best == null) return;

            if (IsUnawareOfPlayer(best) && IsBehind(best))
            {
                // Silent assassination.
                best.Health = 0f;
                Emit(new GameEvent { Type = EvType.Takedown, Pos = best.Pos, Angle = p.Facing, ActorId = best.Id, Sound = "stab" });
                KillNpc(best, (best.Pos - p.Pos).Normalized, silent: true);
                p.ActionLock = 0.35f;
                EmitNoise(best.Pos, w.Def.Noise, NoiseKind.Takedown, p);
            }
            else
            {
                DamageNpc(best, w.Def.Damage * (IsUnawareOfPlayer(best) ? 1.6f : 1f), (best.Pos - p.Pos).Normalized, p, "stab");
                EmitNoise(best.Pos, 3f, NoiseKind.Takedown, p);
            }
        }

        private void TrySubdue()
        {
            var p = Player;
            foreach (var n in Npcs)
            {
                if (n.Down || n.IsCamera) continue;
                Vec2 to = n.Pos - p.Pos;
                if (to.Length > 1.15f + n.Radius) continue;
                if (Math.Abs(MathUtil.AngleDelta(p.Facing, to.Angle)) > 1.0f) continue;
                if (!IsUnawareOfPlayer(n) || !IsBehind(n))
                {
                    if (_msgThrottle <= 0f) { Message("Get behind an unaware target to subdue"); _msgThrottle = 2f; }
                    return;
                }
                n.Unconscious = true;
                n.State = AIState.Unconscious;
                n.Velocity = Vec2.Zero;
                Subdued++;
                p.ActionLock = 0.9f;
                p.ActionLabel = "Subduing";
                CreateBody(n);
                CancelRadio(n);
                Emit(new GameEvent { Type = EvType.Subdue, Pos = n.Pos, Angle = p.Facing, ActorId = n.Id, Text = n.IsTarget ? $"{n.DisplayName} subdued" : null });
                EmitNoise(n.Pos, 1.2f, NoiseKind.BodyFall, p);
                WitnessesSeeKill(n);
                return;
            }
        }

        public bool IsUnawareOfPlayer(Npc n)
        {
            if (n.IsCamera) return false;
            switch (n.State)
            {
                case AIState.Attack:
                case AIState.Chase:
                case AIState.Panic:
                    return false;
            }
            return !(n.SeesPlayer && n.Suspicion > 0.5f);
        }

        /// <summary>Is the player outside this NPC's field of view (roughly behind or beside them)?</summary>
        public bool IsBehind(Npc n)
        {
            Vec2 toPlayer = Player.Pos - n.Pos;
            float delta = Math.Abs(MathUtil.AngleDelta(n.Facing, toPlayer.Angle));
            return delta > 70f * MathUtil.Deg2Rad || !n.SeesPlayer;
        }

        // ------------------------------------------------------------------ projectiles

        private void ThrowProjectile(WeaponDef def, float angle)
        {
            var p = Player;
            var kind = def.Class == WeaponClass.Throwing ? ProjectileKind.Knife : ProjectileKind.Coin;
            var pr = new Projectile
            {
                Id = NewId(), Kind = kind, Pos = p.Pos + Vec2.FromAngle(angle) * 0.4f, Vel = Vec2.FromAngle(angle) * def.ProjectileSpeed,
                Angle = angle, Life = def.Range / def.ProjectileSpeed, Damage = def.Damage, FromPlayer = true,
            };
            if (!World.HasLineOfSight(p.Pos, pr.Pos)) pr.Pos = p.Pos;
            Projectiles.Add(pr);
            Emit(new GameEvent { Type = EvType.Throw, Pos = pr.Pos, Angle = angle, Sound = def.Sound, ActorId = pr.Id });
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                var pr = Projectiles[i];
                if (pr.Done) { Projectiles.RemoveAt(i); continue; }
                Vec2 step = pr.Vel * dt;
                float len = step.Length;
                var hit = World.Raycast(pr.Pos, step / Math.Max(len, 1e-6f), len, RayMode.Projectile);
                Vec2 next = hit.Hit ? hit.Point - step / Math.Max(len, 1e-6f) * 0.05f : pr.Pos + step;

                if (pr.Kind == ProjectileKind.Knife)
                {
                    foreach (var n in Npcs)
                    {
                        if (n.Down || n.IsCamera) continue;
                        if (SegmentCircle(pr.Pos, next, n.Pos, n.Radius + 0.05f))
                        {
                            bool unaware = IsUnawareOfPlayer(n);
                            float dmg = unaware ? 150f : 70f;
                            DamageNpc(n, dmg, pr.Vel.Normalized, Player, "knife_hit");
                            var body = n.Down ? Bodies.Find(b => b.Npc == n) : null;
                            if (body != null) body.KnivesInside++;
                            else DropKnife(n.Pos + pr.Vel.Normalized * -0.4f);
                            pr.Done = true;
                            break;
                        }
                    }
                    if (pr.Done) continue;
                }

                pr.Pos = next;
                pr.Angle += dt * (pr.Kind == ProjectileKind.Knife ? 25f : 12f);
                pr.Life -= dt;
                if (hit.Hit || pr.Life <= 0f)
                {
                    pr.Done = true;
                    if (pr.Kind == ProjectileKind.Knife)
                    {
                        Emit(new GameEvent { Type = EvType.KnifeStuck, Pos = pr.Pos, Angle = pr.Vel.Angle, ActorId = pr.Id });
                        EmitNoise(pr.Pos, 2.5f, NoiseKind.Distraction, Player);
                        DropKnife(pr.Pos);
                    }
                    else
                    {
                        Emit(new GameEvent { Type = EvType.CoinLand, Pos = pr.Pos, ActorId = pr.Id, Sound = "coin" });
                        EmitNoise(pr.Pos, 6.5f, NoiseKind.Distraction, Player);
                    }
                }
            }
        }

        private void DropKnife(Vec2 pos)
        {
            var c = World.NearestWalkable(Int2.FromWorld(pos), 2);
            Vec2 at = World.IsWalkableForNpc(Int2.FromWorld(pos).x, Int2.FromWorld(pos).y) ? pos : c.Center;
            Pickups.Add(new Pickup { Id = NewId(), Kind = PickupKind.Knives, AmmoType = AmmoType.Knives, Amount = 1, Name = "Throwing knife", Pos = at, ItemId = "knife" });
        }

        private static bool SegmentCircle(Vec2 a, Vec2 b, Vec2 c, float r)
        {
            Vec2 ab = b - a;
            float len2 = ab.SqrLength;
            float t = len2 < 1e-8f ? 0f : MathUtil.Clamp01(Vec2.Dot(c - a, ab) / len2);
            Vec2 closest = a + ab * t;
            return Vec2.SqrDistance(closest, c) <= r * r;
        }

        // ------------------------------------------------------------------ hitscan

        /// <summary>Traces one bullet. Handles windows (they shatter), explosive barrels and actors.</summary>
        public void Hitscan(Actor shooter, Vec2 origin, float angle, float range, float damage)
        {
            Vec2 dir = Vec2.FromAngle(angle);
            Vec2 start = origin;
            float remaining = range;
            Vec2 end = origin + dir * range;
            RayHit wall = default;
            for (int guard = 0; guard < 6; guard++)
            {
                wall = World.Raycast(start, dir, remaining, RayMode.Bullets);
                if (!wall.Hit) { end = start + dir * remaining; break; }
                var t = World.TileAt(wall.Cell.x, wall.Cell.y);
                if (t.Kind == TileKind.Window && !World.IsWindowBroken(wall.Cell.x, wall.Cell.y))
                {
                    // Glass is not bullet-proof: shatter and continue.
                    World.BreakWindow(wall.Cell.x, wall.Cell.y);
                    Emit(new GameEvent { Type = EvType.WindowBreak, Pos = wall.Cell.Center, Value = wall.Cell.x, Angle = wall.Cell.y });
                    EmitNoise(wall.Cell.Center, 9f, NoiseKind.GlassBreak, shooter);
                    float used = wall.Distance + 0.01f;
                    start = start + dir * used;
                    remaining -= used;
                    continue;
                }
                end = wall.Point;
                break;
            }

            // Nearest actor along the segment.
            float segLen = Vec2.Distance(origin, end);
            Actor victim = null;
            float bestT = float.MaxValue;
            if (shooter != Player && !Player.Hidden && !Player.InVent)
                TestHit(Player, origin, dir, segLen, ref victim, ref bestT);
            foreach (var n in Npcs)
            {
                if (n == shooter || n.Down) continue;
                if (shooter is Npc sn && n.IsHostile && sn.IsHostile) continue; // no friendly fire between guards
                TestHit(n, origin, dir, segLen, ref victim, ref bestT);
            }

            if (victim != null)
            {
                end = origin + dir * bestT;
                if (victim == Player) DamagePlayer(damage * Diff.EnemyDamage, dir);
                else DamageNpc((Npc)victim, damage, dir, shooter, "bullet");
                Emit(new GameEvent { Type = EvType.Hit, Pos = end, Angle = angle, ActorId = victim.Id, Flag = victim is Npc vn && vn.IsCamera });
            }
            else if (wall.Hit)
            {
                var tile = World.TileAt(wall.Cell.x, wall.Cell.y);
                Emit(new GameEvent { Type = EvType.Impact, Pos = end, Pos2 = wall.Normal, Angle = angle, Text = tile.Kind == TileKind.Furniture ? tile.Furniture.ToString() : tile.Kind.ToString() });
                if (tile.Kind == TileKind.Furniture && tile.Furniture == FurnitureType.Barrel)
                    Explode(wall.Cell.Center, shooter, wall.Cell);
            }
            // Tracer
            Events.Add(new GameEvent { Type = EvType.Tracer, Pos = origin, Pos2 = end, Angle = angle, ActorId = shooter.Id });
        }

        private static void TestHit(Actor a, Vec2 origin, Vec2 dir, float maxT, ref Actor victim, ref float bestT)
        {
            Vec2 oc = a.Pos - origin;
            float t = Vec2.Dot(oc, dir);
            if (t < 0f || t > maxT) return;
            float d2 = oc.SqrLength - t * t;
            float r = a.Radius + 0.06f;
            if (d2 > r * r) return;
            float tHit = t - (float)Math.Sqrt(Math.Max(0f, r * r - d2));
            if (tHit < bestT) { bestT = Math.Max(0f, tHit); victim = a; }
        }

        // ------------------------------------------------------------------ damage & death

        public void DamagePlayer(float dmg, Vec2 dir)
        {
            var p = Player;
            if (p.GodMode || !p.Alive) return;
            if (p.Armor > 0f)
            {
                float absorbed = Math.Min(p.Armor, dmg * 0.6f);
                p.Armor -= absorbed;
                dmg -= absorbed;
            }
            p.Health -= dmg;
            p.DamageFlash = 0.35f;
            p.InteractProgress = 0f;
            Emit(new GameEvent { Type = EvType.PlayerHurt, Pos = p.Pos, Angle = dir.Angle, Value = dmg, ActorId = 0 });
            if (p.Health <= 0f) { p.Health = 0f; Die(); }
        }

        public void DamageNpc(Npc n, float dmg, Vec2 dir, Actor source, string cause)
        {
            if (n.Down) return;
            n.Health -= dmg;
            if (n.Health <= 0f)
            {
                KillNpc(n, dir, silent: false);
                return;
            }
            if (n.IsCamera) return;
            // Survivors react: they know roughly where the attack came from.
            if (source == Player)
            {
                n.LastKnownPlayer = Player.Pos;
                n.LastSeenAgo = 0.5f;
                if (n.IsHostile)
                {
                    n.Suspicion = Math.Max(n.Suspicion, 0.95f);
                    n.Facing = (Player.Pos - n.Pos).Angle;
                    SetState(n, CanSee(n, Player.Pos, Player.Crouched) > 0f ? AIState.Attack : AIState.Chase);
                    Bark(n, "hurt");
                }
                else Panic(n, Player.Pos);
            }
        }

        public void KillNpc(Npc n, Vec2 dir, bool silent)
        {
            if (!n.Alive) return;
            n.Alive = false;
            n.Health = 0f;
            n.Velocity = Vec2.Zero;
            n.State = AIState.Dead;
            CancelRadio(n);
            if (n.IsCamera)
            {
                Emit(new GameEvent { Type = EvType.Death, Pos = n.Pos, ActorId = n.Id, Flag = true });
                EmitNoise(n.Pos, 4f, NoiseKind.GlassBreak, Player);
                return;
            }
            Kills++;
            if (n.IsTarget)
            {
                TargetsKilled++;
                Message($"Target eliminated: {n.DisplayName}");
            }
            else if (n.IsCivilian) CiviliansKilled++;
            else NonTargetKills++;
            CreateBody(n, dir.Angle);
            Emit(new GameEvent { Type = EvType.Death, Pos = n.Pos, Angle = dir.Angle, ActorId = n.Id, Flag = silent });
            EmitNoise(n.Pos, silent ? 1.2f : 2.2f, NoiseKind.BodyFall, Player);
            WitnessesSeeKill(n);
        }

        private Body CreateBody(Npc n, float facing = float.NaN)
        {
            var b = new Body { Id = NewId(), Npc = n, Pos = n.Pos, Facing = float.IsNaN(facing) ? n.Facing : facing };
            Bodies.Add(b);
            return b;
        }

        /// <summary>Anyone who can see the victim AND the player when a kill happens recognises the killer.</summary>
        private void WitnessesSeeKill(Npc victim)
        {
            foreach (var w in Npcs)
            {
                if (w == victim || w.Down || w.State == AIState.Disabled) continue;
                if (CanSee(w, victim.Pos, false) <= 0f) continue;
                if (CanSee(w, Player.Pos, Player.Crouched) > 0f) SpotPlayer(w);
                else
                {
                    // Saw the victim drop but not the attacker.
                    OnBodySeen(w, Bodies.Find(b => b.Npc == victim));
                }
            }
        }

        private void WitnessesSeePlayer(float amount)
        {
            foreach (var w in Npcs)
            {
                if (w.Down || w.State == AIState.Disabled) continue;
                if (CanSee(w, Player.Pos, Player.Crouched) > 0f)
                {
                    w.Suspicion = Math.Min(1f, w.Suspicion + amount);
                    if (w.Suspicion >= 1f) SpotPlayer(w);
                }
            }
        }

        public void Explode(Vec2 pos, Actor source, Int2? barrelCell = null)
        {
            if (barrelCell.HasValue)
            {
                var c = barrelCell.Value;
                var t = World.TileAt(c.x, c.y);
                if (!(t.Kind == TileKind.Furniture && t.Furniture == FurnitureType.Barrel)) return;
                var floor = TileLegend.FromChar('.');
                floor.Floor = FloorStyle.D;
                Map.Set(c.x, c.y, floor);
            }
            const float radius = 2.8f;
            Emit(new GameEvent { Type = EvType.Explosion, Pos = pos, Value = radius, ActorId = source?.Id ?? -1 });
            EmitNoise(pos, 26f, NoiseKind.Explosion, source);
            LightsDirty = true;

            float d = Vec2.Distance(Player.Pos, pos);
            if (d < radius && !Player.Hidden && World.HasLineOfSight(pos, Player.Pos))
                DamagePlayer(140f * (1f - d / radius), (Player.Pos - pos).Normalized);
            foreach (var n in Npcs.ToArray())
            {
                if (n.Down) continue;
                float nd = Vec2.Distance(n.Pos, pos);
                if (nd < radius && World.HasLineOfSight(pos, n.Pos))
                    DamageNpc(n, 180f * (1f - nd / radius) + 20f, (n.Pos - pos).Normalized, source, "explosion");
            }
            // Chain reaction
            int r = (int)Math.Ceiling(radius);
            var cell = Int2.FromWorld(pos);
            for (int y = cell.y - r; y <= cell.y + r; y++)
            for (int x = cell.x - r; x <= cell.x + r; x++)
            {
                var t = World.TileAt(x, y);
                if (t.Kind == TileKind.Furniture && t.Furniture == FurnitureType.Barrel && Vec2.Distance(new Int2(x, y).Center, pos) < radius)
                    Explode(new Int2(x, y).Center, source, new Int2(x, y));
            }
        }

        // ------------------------------------------------------------------ NPC shooting

        private void NpcFire(Npc n)
        {
            var w = n.Weapon;
            if (w == null) return;
            if (w.Mag <= 0)
            {
                if (!w.Reloading) { w.StartReload(999); Emit(EvType.ReloadStart, n.Pos, null, w.Def.ReloadTime, n.Id, w.Def.Sound); }
                return;
            }
            if (w.CanFire(true) != FireBlock.None) return;
            // Aim slightly ahead of a moving player, with accuracy depending on distance and difficulty.
            Vec2 target = Player.Pos + Player.Velocity * 0.08f;
            float aim = (target - n.Pos).Angle;
            float moving = Player.Velocity.Length > 0.5f ? 1.4f : 1f;
            var angles = w.Fire(Rng, aim, n.Speed > 0.3f, false, Diff.EnemySpread * moving * (n.Kind == NpcKind.Elite ? 0.8f : 1.2f));
            Vec2 muzzle = n.Pos + Vec2.FromAngle(n.Facing) * 0.5f;
            if (!World.HasLineOfSight(n.Pos, muzzle)) muzzle = n.Pos;
            foreach (float a in angles) Hitscan(n, muzzle, a, w.Def.Range, w.Def.Damage * 0.8f);
            Emit(new GameEvent { Type = EvType.Shot, Pos = muzzle, Angle = n.Facing, Sound = w.Def.Sound, ActorId = n.Id, Flag = w.Def.Suppressed });
            Emit(new GameEvent { Type = EvType.Casing, Pos = n.Pos, Angle = n.Facing - MathUtil.Pi / 2f, Sound = "casing" });
            EmitNoise(n.Pos, w.Def.Noise, NoiseKind.Gunshot, n);
        }
    }
}
