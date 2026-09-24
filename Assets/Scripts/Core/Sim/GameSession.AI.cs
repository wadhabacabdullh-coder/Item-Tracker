using System;

namespace ShadowContract.Core
{
    /// <summary>NPC behaviour: a finite state machine per NPC plus path following.</summary>
    public sealed partial class GameSession
    {
        private void UpdateNpcs(float dt)
        {
            foreach (var n in Npcs)
            {
                if (n.Down) continue;
                n.StateTime += dt;
                n.BarkCooldown -= dt;

                if (n.IsCamera) { UpdateCamera(n, dt); continue; }

                if (n.Weapon != null)
                {
                    n.Weapon.Tick(dt);
                    if (n.Weapon.Reloading)
                    {
                        n.Weapon.TickReload(dt, 999);
                        if (!n.Weapon.Reloading) Emit(EvType.ReloadDone, n.Pos, null, 0, n.Id, n.Weapon.Def.Sound);
                    }
                }

                n.PerceptionTimer -= dt;
                if (n.PerceptionTimer <= 0f)
                {
                    n.PerceptionTimer += PerceptionInterval;
                    Perceive(n, PerceptionInterval);
                }

                if (n.RadioTimer > 0f)
                {
                    n.RadioTimer -= dt;
                    if (n.RadioTimer <= 0f) CompleteRadio(n);
                }

                n.Speed = 0f;
                if (n.IsHostile) ThinkHostile(n, dt);
                else ThinkCivilian(n, dt);

                if (!float.IsNaN(n.DesiredFacing))
                {
                    n.Facing = MathUtil.RotateTowards(n.Facing, n.DesiredFacing, n.TurnSpeed * dt);
                    if (Math.Abs(MathUtil.AngleDelta(n.Facing, n.DesiredFacing)) < 0.01f && IsCalmState(n.State) && n.State != AIState.Suspicious)
                        n.DesiredFacing = float.NaN;
                }
            }
            Separate();
        }

        public void SetState(Npc n, AIState s)
        {
            if (n.Down) return;
            if (n.State == s && s != AIState.Investigate) return;
            var prev = n.State;
            n.State = s;
            n.StateTime = 0f;
            n.Path = null;
            n.WaitTimer = 0f;
            n.LookTimer = 0f;
            switch (s)
            {
                case AIState.Suspicious:
                    n.DesiredFacing = (n.LastKnownPlayer - n.Pos).Angle;
                    if (prev != AIState.Suspicious) Bark(n, "suspicious");
                    break;
                case AIState.Search:
                    n.SearchTimer = 14f + Rng.Range(0f, 6f);
                    n.SearchMoving = false;
                    Bark(n, "search");
                    break;
                case AIState.ReturnToPatrol:
                    n.InvestigateBody = -1;
                    n.InvestigateObject = null;
                    n.InvestigateIsLoud = false;
                    break;
                case AIState.Attack:
                    if (prev != AIState.Chase) n.ReactionTimer = Math.Max(n.ReactionTimer, Diff.Reaction);
                    break;
            }
        }

        // ------------------------------------------------------------------ hostile NPCs

        private void ThinkHostile(Npc n, float dt)
        {
            float patrolSpeed = 1.9f;
            switch (n.State)
            {
                case AIState.Idle:
                    if (Vec2.Distance(n.Pos, n.HomePos) > 0.6f) { SetState(n, AIState.ReturnToPatrol); break; }
                    LookAround(n, dt, n.LookAngles ?? (n.LookAngles = PostGlances(n.HomeFacing)), 3.5f);
                    break;

                case AIState.Patrol:
                    FollowRoute(n, dt, patrolSpeed);
                    break;

                case AIState.Follow:
                    FollowPrincipal(n, dt);
                    break;

                case AIState.Suspicious:
                    n.DesiredFacing = (n.LastKnownPlayer - n.Pos).Angle;
                    if (n.Suspicion >= 0.6f)
                    {
                        n.InvestigatePos = n.LastKnownPlayer;
                        n.InvestigateIsLoud = false;
                        n.InvestigateObject = null;
                        Bark(n, "investigate");
                        SetState(n, AIState.Investigate);
                    }
                    else if ((n.StateTime > 4f && n.Suspicion < 0.35f) || n.Suspicion < 0.08f)
                    {
                        Bark(n, "calm");
                        SetState(n, n.FollowTarget != null ? AIState.Follow : AIState.ReturnToPatrol);
                    }
                    break;

                case AIState.Investigate:
                    Investigate(n, dt);
                    break;

                case AIState.Fixing:
                    n.DesiredFacing = (n.InvestigatePos - n.Pos).Angle;
                    if (n.StateTime > 2.5f)
                    {
                        if (n.InvestigateObject is Interactable it)
                        {
                            if (it.Kind == InteractKind.Distraction) StopDistraction(it);
                            else if (it.Kind == InteractKind.PowerBox && it.Active) SetLightGroup(it, false);
                        }
                        Bark(n, "calm");
                        SetState(n, AIState.ReturnToPatrol);
                    }
                    break;

                case AIState.Search:
                    Search(n, dt);
                    break;

                case AIState.Chase:
                {
                    if (n.SeesPlayer && n.Suspicion >= 0.9f)
                    {
                        SetState(n, AIState.Attack);
                        break;
                    }
                    float speed = n.Kind == NpcKind.Elite ? 4.5f : 4.1f;
                    bool arrived = MoveTo(n, n.LastKnownPlayer, speed, dt, 0.6f);
                    if (arrived || n.LastSeenAgo > 12f)
                    {
                        Bark(n, "lost");
                        n.InvestigatePos = n.LastKnownPlayer;
                        SetState(n, AIState.Search);
                    }
                    break;
                }

                case AIState.Attack:
                    Attack(n, dt);
                    break;

                case AIState.ReturnToPatrol:
                {
                    if (n.FollowTarget != null && !n.FollowTarget.Down) { SetState(n, AIState.Follow); break; }
                    Vec2 goal = n.Route.Count > 0 ? n.Route[n.RouteIndex].Pos : n.HomePos;
                    if (MoveTo(n, goal, patrolSpeed, dt))
                    {
                        if (n.Route.Count > 0) SetState(n, AIState.Patrol);
                        else
                        {
                            n.DesiredFacing = n.HomeFacing;
                            SetState(n, AIState.Idle);
                        }
                        n.Suspicion = Math.Min(n.Suspicion, 0.2f);
                    }
                    break;
                }

                default:
                    SetState(n, AIState.ReturnToPatrol);
                    break;
            }
        }

        private void Attack(Npc n, float dt)
        {
            if (!PlayerTargetable)
            {
                n.LastSeenAgo = Math.Max(n.LastSeenAgo, 1f);
                SetState(n, AIState.Chase);
                return;
            }
            Vec2 to = Player.Pos - n.Pos;
            float dist = to.Length;
            n.DesiredFacing = to.Angle;
            n.Facing = MathUtil.RotateTowards(n.Facing, to.Angle, 9f * dt);

            if (!n.SeesPlayer)
            {
                if (n.LastSeenAgo > 0.8f) SetState(n, AIState.Chase);
                return;
            }
            if (n.Weapon == null) { SetState(n, AIState.Chase); return; }

            float range = n.Weapon.Def.Range;
            if (dist > range * 0.85f)
            {
                MoveTo(n, Player.Pos, 3.0f, dt, 0.6f);
                return;
            }
            // Elites strafe while shooting.
            if (n.Kind == NpcKind.Elite && dist > 2.5f)
            {
                Vec2 side = to.Normalized.Perp * (((int)(Time / 1.7f + n.Id) % 2 == 0) ? 1f : -1f);
                n.Pos = World.MoveCircle(n.Pos, n.Radius, side * 1.6f * dt, MoverKind.Npc);
                n.Speed = 1.6f;
                n.MoveAnim += 1.6f * dt;
            }
            n.ReactionTimer -= dt;
            if (n.ReactionTimer > 0f) return;
            if (Math.Abs(MathUtil.AngleDelta(n.Facing, to.Angle)) > 0.25f) return;
            // Short bursts with pauses keep automatic weapons fair.
            if (n.Weapon.Def.Automatic)
            {
                n.BurstTimer += dt;
                if (n.BurstTimer % 1.3f > 0.55f) return;
            }
            NpcFire(n);
        }

        private void Investigate(Npc n, float dt)
        {
            float speed = n.InvestigateIsLoud ? 3.3f : 2.3f;
            bool arrived = MoveTo(n, n.InvestigatePos, speed, dt, n.InvestigateObject != null ? 1.0f : 0.7f);
            if (!arrived) return;

            if (n.InvestigateBody >= 0)
            {
                var b = Bodies.Find(x => x.Id == n.InvestigateBody);
                n.InvestigateBody = -1;
                if (b != null && !b.Discovered && !b.Hidden)
                {
                    DiscoverBody(n, b);
                    n.InvestigatePos = b.Pos;
                    SetState(n, AIState.Search);
                    return;
                }
            }
            if (n.InvestigateObject is Interactable it && (it.Active))
            {
                SetState(n, AIState.Fixing);
                return;
            }
            // Check a closet right next to the spot the player was last seen at.
            if (n.Suspicion > 0.55f) CheckNearbyClosets(n, n.InvestigatePos, 1.6f);

            if (n.WaitTimer <= 0f) n.WaitTimer = 3.2f;
            LookAround(n, dt, null, 1.1f);
            n.WaitTimer -= dt;
            if (n.WaitTimer <= 0.05f)
            {
                if (n.InvestigateIsLoud || n.Suspicion > 0.6f || Alert >= AlertLevel.Alarmed) SetState(n, AIState.Search);
                else
                {
                    Bark(n, "calm");
                    SetState(n, AIState.ReturnToPatrol);
                }
            }
        }

        private void CheckNearbyClosets(Npc n, Vec2 around, float radius)
        {
            var c = Int2.FromWorld(around);
            int r = (int)Math.Ceiling(radius);
            for (int y = c.y - r; y <= c.y + r; y++)
            for (int x = c.x - r; x <= c.x + r; x++)
            {
                if (!World.TileAt(x, y).Is(TileFlags.HidingSpot)) continue;
                var cell = new Int2(x, y);
                if (Vec2.Distance(cell.Center, n.Pos) > 1.6f) continue;
                if (CheckCloset(n, cell)) return;
            }
        }

        private void Search(Npc n, float dt)
        {
            n.SearchTimer -= dt;
            if (n.SearchTimer <= 0f)
            {
                Bark(n, "calm");
                n.Suspicion = 0.2f;
                n.Awareness = Math.Min(1.6f, n.Awareness + 0.1f);
                SetState(n, AIState.ReturnToPatrol);
                return;
            }
            if (!n.SearchMoving)
            {
                n.WaitTimer -= dt;
                LookAround(n, dt, null, 0.9f);
                if (n.WaitTimer > 0f) return;
                // Pick a reachable point near the search centre.
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vec2 p = n.InvestigatePos + new Vec2(Rng.Range(-6f, 6f), Rng.Range(-6f, 6f));
                    var cell = Int2.FromWorld(p);
                    if (!World.IsWalkableForNpc(cell.x, cell.y)) continue;
                    n.SearchPoint = cell.Center;
                    n.SearchMoving = true;
                    n.Path = null;
                    break;
                }
                if (!n.SearchMoving) n.WaitTimer = 1f;
                return;
            }
            if (MoveTo(n, n.SearchPoint, 2.7f, dt))
            {
                n.SearchMoving = false;
                n.WaitTimer = 1.4f + Rng.Range(0f, 1f);
                if (Rng.Chance(0.5f)) CheckNearbyClosets(n, n.Pos, 1.5f);
            }
        }

        private void FollowRoute(Npc n, float dt, float speed)
        {
            if (n.Route.Count == 0) { SetState(n, AIState.Idle); return; }
            var wp = n.Route[n.RouteIndex];
            if (n.WaitTimer > 0f)
            {
                n.WaitTimer -= dt;
                n.Activity = wp.Activity;
                if (!float.IsNaN(wp.LookAngle)) n.DesiredFacing = wp.LookAngle;
                else LookAround(n, dt, null, 2.2f);
                if (n.WaitTimer <= 0f) n.RouteIndex = (n.RouteIndex + 1) % n.Route.Count;
                return;
            }
            if (MoveTo(n, wp.Pos, speed, dt))
            {
                n.WaitTimer = wp.Wait > 0f ? wp.Wait : 0.01f;
                n.Path = null;
            }
        }

        private void FollowPrincipal(Npc n, float dt)
        {
            var t = n.FollowTarget;
            if (t == null || t.Down)
            {
                n.FollowTarget = null;
                n.InvestigatePos = t?.Pos ?? n.Pos;
                n.InvestigateIsLoud = true;
                RaiseAlarm(60f);
                SetState(n, AIState.Search);
                return;
            }
            // Stay slightly behind the principal.
            Vec2 spot = t.Pos - Vec2.FromAngle(t.Facing) * 1.3f;
            var cell = Int2.FromWorld(spot);
            if (!World.IsWalkableForNpc(cell.x, cell.y)) spot = t.Pos;
            float dist = Vec2.Distance(n.Pos, spot);
            if (dist > 0.8f)
            {
                float speed = t.State == AIState.Flee ? 4.2f : dist > 4f ? 3.2f : 2.1f;
                if (n.RepathTimer <= 0f || n.Path == null) { n.Path = null; n.RepathTimer = 0.7f; }
                n.RepathTimer -= dt;
                MoveTo(n, spot, speed, dt, 0.5f);
            }
            else
            {
                n.DesiredFacing = t.Speed > 0.1f ? t.Facing : (t.Pos - n.Pos).Angle + 1.2f * (float)Math.Sin(Time * 0.5f + n.Id);
            }
        }

        /// <summary>A posted guard mostly watches their post and glances to either side.</summary>
        private static float[] PostGlances(float home) => new[] { home, home + 0.9f, home, home - 0.9f };

        private void LookAround(Npc n, float dt, float[] angles, float interval)
        {
            n.LookTimer -= dt;
            if (n.LookTimer > 0f) return;
            n.LookTimer = interval + Rng.Range(0f, interval * 0.5f);
            if (angles != null && angles.Length > 0)
            {
                n.LookIndex = (n.LookIndex + 1) % angles.Length;
                n.DesiredFacing = angles[n.LookIndex];
            }
            else n.DesiredFacing = n.Facing + Rng.Range(-1.6f, 1.6f);
        }

        // ------------------------------------------------------------------ civilians & targets

        private void ThinkCivilian(Npc n, float dt)
        {
            switch (n.State)
            {
                case AIState.Idle:
                    LookAround(n, dt, n.LookAngles ?? (n.LookAngles = PostGlances(n.HomeFacing)), 4f);
                    if (n.IsTarget && Alert == AlertLevel.Combat) Panic(n, n.LastKnownPlayer);
                    break;
                case AIState.Patrol:
                    FollowRoute(n, dt, n.IsTarget ? 1.6f : 1.5f);
                    if (n.IsTarget && Alert == AlertLevel.Combat) Panic(n, n.LastKnownPlayer);
                    break;
                case AIState.Suspicious:
                    n.DesiredFacing = (n.LastKnownPlayer - n.Pos).Angle;
                    if (n.StateTime > 3f && n.Suspicion < 0.3f) SetState(n, AIState.ReturnToPatrol);
                    break;
                case AIState.Panic:
                {
                    var g = n.ReportTo;
                    if (g == null || g.Down) { SetState(n, AIState.Cower); break; }
                    if (n.RepathTimer <= 0f) { n.Path = null; n.RepathTimer = 1f; }
                    n.RepathTimer -= dt;
                    if (MoveTo(n, g.Pos, 4.4f, dt, 1.6f) || Vec2.Distance(n.Pos, g.Pos) < 1.8f)
                    {
                        Bark(n, "report", force: true);
                        if (g.State != AIState.Attack && g.State != AIState.Chase)
                        {
                            g.InvestigatePos = n.LastKnownPlayer;
                            g.InvestigateIsLoud = true;
                            g.InvestigateObject = null;
                            g.Suspicion = Math.Max(g.Suspicion, 0.75f);
                            SetState(g, AIState.Investigate);
                        }
                        RaiseAlarm(45f);
                        SetState(n, AIState.Cower);
                    }
                    break;
                }
                case AIState.Cower:
                    if (n.StateTime > 25f && Alert < AlertLevel.Combat)
                    {
                        n.Suspicion = 0f;
                        SetState(n, AIState.ReturnToPatrol);
                    }
                    break;
                case AIState.Flee:
                    if (MoveTo(n, n.EscapePos, 4.2f, dt, 0.6f))
                    {
                        n.Escaped = true;
                    }
                    break;
                case AIState.ReturnToPatrol:
                {
                    Vec2 goal = n.Route.Count > 0 ? n.Route[n.RouteIndex].Pos : n.HomePos;
                    if (MoveTo(n, goal, 1.6f, dt)) SetState(n, n.Route.Count > 0 ? AIState.Patrol : AIState.Idle);
                    break;
                }
                default:
                    SetState(n, AIState.ReturnToPatrol);
                    break;
            }
        }

        // ------------------------------------------------------------------ cameras

        private void UpdateCamera(Npc n, float dt)
        {
            if (n.State == AIState.Disabled) { n.SeesPlayer = false; return; }
            n.CameraCooldown -= dt;
            n.PerceptionTimer -= dt;
            if (n.PerceptionTimer <= 0f)
            {
                n.PerceptionTimer += PerceptionInterval;
                if (n.CameraCooldown <= 0f) Perceive(n, PerceptionInterval);
                else { n.SeesPlayer = false; n.Suspicion = Math.Max(0f, n.Suspicion - 0.1f * PerceptionInterval); }
            }
            if (n.SeesPlayer && n.Suspicion > 0.15f)
            {
                float target = (Player.Pos - n.Pos).Angle;
                // Cameras can only track within their sweep limits.
                float rel = MathUtil.Clamp(MathUtil.AngleDelta(n.SweepCenter, target), -n.SweepHalf, n.SweepHalf);
                n.Facing = MathUtil.RotateTowards(n.Facing, n.SweepCenter + rel, 2f * dt);
            }
            else
            {
                n.SweepPhase += dt * 0.55f;
                n.Facing = n.SweepCenter + (float)Math.Sin(n.SweepPhase) * n.SweepHalf;
            }
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Walks along a (cached) A* path to the goal. Returns true when within <paramref name="tolerance"/>.</summary>
        private bool MoveTo(Npc n, Vec2 goal, float speed, float dt, float tolerance = 0.25f)
        {
            if (Vec2.Distance(n.Pos, goal) <= tolerance) { n.Path = null; return true; }

            if (n.Path == null || Vec2.Distance(n.PathGoal, goal) > 0.75f)
            {
                n.Path = Paths.FindPath(n.Pos, goal, n.Radius);
                n.PathIndex = 0;
                n.PathGoal = goal;
                n.StuckTimer = 0f;
                n.StuckPos = n.Pos;
                if (n.Path == null || n.Path.Count == 0) { n.Path = null; return true; } // unreachable: give up
            }

            var node = n.Path[n.PathIndex];
            if (node.Teleport)
            {
                n.Pos = node.Pos;
                n.PathIndex++;
                if (n.PathIndex >= n.Path.Count) { n.Path = null; return Vec2.Distance(n.Pos, goal) <= tolerance + 0.5f; }
                node = n.Path[n.PathIndex];
            }

            Vec2 to = node.Pos - n.Pos;
            float dist = to.Length;
            bool last = n.PathIndex == n.Path.Count - 1;
            if (dist < (last ? Math.Min(tolerance, 0.2f) : 0.3f))
            {
                n.PathIndex++;
                if (n.PathIndex >= n.Path.Count) { n.Path = null; return true; }
                return false;
            }

            Vec2 dir = to / dist;
            // Open doors on the way.
            Vec2 probe = n.Pos + dir * (n.Radius + 0.35f);
            var door = World.DoorAt((int)Math.Floor(probe.x), (int)Math.Floor(probe.y));
            if (door != null && !door.Open && door.Type != DoorType.Secret) OpenDoor(door, n, true);

            float step = Math.Min(speed * dt, dist);
            Vec2 before = n.Pos;
            n.Pos = World.MoveCircle(n.Pos, n.Radius, dir * step, MoverKind.Npc);
            float moved = Vec2.Distance(before, n.Pos);
            n.MoveAnim += moved;
            n.Speed = moved / Math.Max(dt, 1e-5f);
            n.Velocity = (n.Pos - before) / Math.Max(dt, 1e-5f);
            if (n.State != AIState.Attack) n.Facing = MathUtil.RotateTowards(n.Facing, dir.Angle, n.TurnSpeed * dt);
            n.DesiredFacing = float.NaN;

            // Stuck detection: repath (or skip the node) if we barely moved for a while.
            n.StuckTimer += dt;
            if (n.StuckTimer > 1.2f)
            {
                if (Vec2.Distance(n.StuckPos, n.Pos) < 0.2f)
                {
                    if (n.PathIndex < n.Path.Count - 1) n.PathIndex++;
                    else n.Path = null;
                }
                n.StuckTimer = 0f;
                n.StuckPos = n.Pos;
            }
            return false;
        }

        /// <summary>Keeps characters from overlapping each other.</summary>
        private void Separate()
        {
            for (int i = 0; i < Npcs.Count; i++)
            {
                var a = Npcs[i];
                if (a.Down || a.IsCamera) continue;
                for (int j = i + 1; j < Npcs.Count; j++)
                {
                    var b = Npcs[j];
                    if (b.Down || b.IsCamera) continue;
                    Push(a, b, 0.5f);
                }
                if (Player.Alive && !Player.Hidden) Push(a, Player, 0.85f);
            }
        }

        private void Push(Actor a, Actor b, float aShare)
        {
            Vec2 d = a.Pos - b.Pos;
            float min = a.Radius + b.Radius;
            float len2 = d.SqrLength;
            if (len2 >= min * min || len2 < 1e-8f) return;
            float len = (float)Math.Sqrt(len2);
            Vec2 push = d / len * (min - len);
            a.Pos = World.ResolveCircle(a.Pos + push * aShare, a.Radius, MoverKind.Npc);
            var mb = b == Player && Player.Crouched ? MoverKind.PlayerCrouched : b == Player ? MoverKind.Player : MoverKind.Npc;
            b.Pos = World.ResolveCircle(b.Pos - push * (1f - aShare), b.Radius, mb);
        }

        // ------------------------------------------------------------------ barks

        private static readonly System.Collections.Generic.Dictionary<string, string[]> BarkLines = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["suspicious"] = new[] { "Hm?", "What was that?", "Hello?", "Did something move?" },
            ["investigate"] = new[] { "I'll check it out.", "Someone there?", "Show yourself!" },
            ["spotted"] = new[] { "Intruder!", "Contact!", "There! Hostile!", "Freeze!" },
            ["radio"] = new[] { "Control, we have an intruder!", "All units, hostile on site!" },
            ["lost"] = new[] { "Lost visual!", "Where'd he go?", "He's slipping away!" },
            ["search"] = new[] { "Search the area.", "He's here somewhere...", "Check every corner." },
            ["calm"] = new[] { "Must've been nothing.", "Probably rats.", "Back to it.", "Hm. Nothing." },
            ["body"] = new[] { "Man down! We've got a body!", "Oh god... Raise the alarm!" },
            ["body_seen"] = new[] { "What the...?", "Is that... someone on the floor?" },
            ["gunshot"] = new[] { "Shots fired!", "Gunfire!", "Was that a gunshot?" },
            ["hurt"] = new[] { "Argh!", "I'm hit!", "Taking fire!" },
            ["lights"] = new[] { "Who killed the lights?", "Power's out... I'll check the box." },
            ["noise"] = new[] { "Who left that on?", "What's that racket?" },
            ["found"] = new[] { "Gotcha!", "Found you!" },
            ["panic"] = new[] { "Help! Security!", "He's got a weapon!", "Somebody help!" },
            ["flee"] = new[] { "Get me out of here!", "I'm leaving, now!" },
            ["report"] = new[] { "Over there! Someone's in the building!", "Security! I saw something!" },
            ["camera"] = new[] { "Camera picked something up!", "Movement on the cameras!" },
        };

        public void Bark(Npc n, string key, bool force = false)
        {
            if (n.Down || (!force && n.BarkCooldown > 0f)) return;
            if (!BarkLines.TryGetValue(key, out var lines)) return;
            n.BarkCooldown = 3f;
            string line = lines[Rng.Range(0, lines.Length)];
            string voice = n.IsCivilian || (n.IsTarget && !n.Armed) ? "civ" : n.Kind == NpcKind.Elite ? "elite" : "guard";
            Emit(new GameEvent { Type = EvType.Bark, Pos = n.Pos, Text = line, ActorId = n.Id, Sound = voice + "_" + key });
        }
    }
}
