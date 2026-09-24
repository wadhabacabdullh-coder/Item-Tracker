using System;

namespace ShadowContract.Core
{
    /// <summary>Vision, hearing, detection and alarm propagation.</summary>
    public sealed partial class GameSession
    {
        public const float PerceptionInterval = 0.1f;

        private static bool IsAlertState(AIState s) =>
            s == AIState.Attack || s == AIState.Chase || s == AIState.Search || s == AIState.Investigate;

        private static bool IsCalmState(AIState s) =>
            s == AIState.Idle || s == AIState.Patrol || s == AIState.Suspicious || s == AIState.ReturnToPatrol || s == AIState.Follow;

        /// <summary>
        /// How well an NPC sees a point (0 = not at all, up to ~1.4 = clearly, close and lit).
        /// Takes the vision cone, distance, lighting, crouching, bushes and walls/low cover into account.
        /// </summary>
        public float CanSee(Npc n, Vec2 pos, bool targetCrouched)
        {
            if (n.Down || n.State == AIState.Disabled) return 0f;
            Vec2 to = pos - n.Pos;
            float d = to.Length;
            bool alerted = IsAlertState(n.State) || Alert >= AlertLevel.Alarmed;

            float light = World.LightAt(pos);
            float range = n.ViewRange * (0.3f + 0.7f * Math.Min(1f, light * 1.15f));
            if (targetCrouched) range *= 0.85f;
            var tile = World.TileAt((int)Math.Floor(pos.x), (int)Math.Floor(pos.y));
            if (tile.Is(TileFlags.Concealment)) range *= targetCrouched ? 0.35f : 0.6f;
            if (alerted) range *= 1.2f;
            if (n.IsCamera) range = n.ViewRange * (0.5f + 0.5f * Math.Min(1f, light * 1.2f));

            bool peripheral = !n.IsCamera && d < 1.3f && !(targetCrouched && !Player.Moving);
            if (d > range && !peripheral) return 0f;

            float fov = n.ViewAngle * (alerted && !n.IsCamera ? 1.45f : 1f);
            if (Math.Abs(MathUtil.AngleDelta(n.Facing, to.Angle)) > fov * 0.5f && !peripheral) return 0f;

            if (!World.HasLineOfSight(n.Pos, pos, targetCrouched)) return 0f;
            return MathUtil.Clamp(1.4f - d / Math.Max(range, 0.5f), 0.25f, 1.4f);
        }

        private bool PlayerTargetable => Player.Alive && !Player.Hidden && !Player.InVent && !Player.Ghost;

        private void Perceive(Npc n, float dt)
        {
            float v = PlayerTargetable ? CanSee(n, Player.Pos, Player.Crouched) : 0f;
            if (v > 0f)
            {
                n.SeesPlayer = true;
                n.SeeTime += dt;
                n.LastKnownPlayer = Player.Pos;
                n.LastSeenAgo = 0f;
                float stateMult = n.State == AIState.Attack || n.State == AIState.Chase ? 4f
                    : n.State == AIState.Search || n.State == AIState.Investigate ? 1.6f
                    : n.State == AIState.Suspicious ? 1.25f : 1f;
                if (Alert >= AlertLevel.Alarmed) stateMult *= 1.3f;
                float moveMult = Player.Sprinting ? 1.35f : Player.Moving ? 1f : 0.55f;
                float kindMult = n.IsCivilian ? 0.8f : n.IsCamera ? 1.25f : 1f;
                // Carrying a body in plain sight is very suspicious.
                if (Player.DraggingBody >= 0) kindMult *= 1.8f;
                float gain = 1.05f * v * stateMult * moveMult * kindMult * Diff.Detection * n.Awareness;
                n.Suspicion = Math.Min(1f, n.Suspicion + gain * dt);
                n.SuspicionDecayDelay = 2.5f;
                if (n.Suspicion >= 1f && n.State != AIState.Attack && n.State != AIState.Chase && n.State != AIState.Panic && n.State != AIState.Flee)
                    SpotPlayer(n);
            }
            else
            {
                n.SeesPlayer = false;
                n.SeeTime = 0f;
                n.LastSeenAgo += dt;
                n.SuspicionDecayDelay -= dt;
                if (n.SuspicionDecayDelay <= 0f && (IsCalmState(n.State) || n.IsCamera))
                    n.Suspicion = Math.Max(0f, n.Suspicion - 0.14f * dt);
            }

            // Bodies in view
            if (n.IsCamera || n.State == AIState.Attack || n.State == AIState.Chase || n.State == AIState.Panic || n.State == AIState.Flee) return;
            foreach (var b in Bodies)
            {
                if (b.Discovered || b.Hidden || b.Npc == n) continue;
                if (n.InvestigateBody == b.Id) continue;
                if (Vec2.SqrDistance(b.Pos, n.Pos) > n.ViewRange * n.ViewRange) continue;
                if (CanSee(n, b.Pos, false) <= 0f) continue;
                OnBodySeen(n, b);
                break;
            }
        }

        /// <summary>The NPC fully detected the player.</summary>
        public void SpotPlayer(Npc n)
        {
            if (n.Down) return;
            n.Suspicion = 1f;
            n.LastKnownPlayer = Player.Pos;
            n.LastSeenAgo = 0f;
            if (!Spotted)
            {
                Spotted = true;
                Emit(new GameEvent { Type = EvType.Spotted, Pos = n.Pos, ActorId = n.Id });
            }
            if (n.IsCamera) { CameraAlarm(n); return; }
            if (n.IsCivilian || (n.IsTarget && !n.Armed)) { Panic(n, Player.Pos); return; }
            if (n.State != AIState.Attack && n.State != AIState.Chase)
            {
                Bark(n, "spotted", force: true);
                n.ReactionTimer = Diff.Reaction * (n.Kind == NpcKind.Elite ? 0.7f : 1f);
                SetState(n, AIState.Attack);
            }
            if (!n.HasRadioed && n.RadioTimer < 0f)
            {
                n.RadioTimer = 1.6f;
                Emit(new GameEvent { Type = EvType.Radio, Pos = n.Pos, ActorId = n.Id, Flag = true, Value = 1.6f });
            }
        }

        public void CancelRadio(Npc n)
        {
            if (n.RadioTimer > 0f)
                Emit(new GameEvent { Type = EvType.Radio, Pos = n.Pos, ActorId = n.Id, Flag = false });
            n.RadioTimer = -1f;
        }

        /// <summary>A guard finished calling it in: everyone nearby joins the hunt.</summary>
        private void CompleteRadio(Npc caller)
        {
            caller.RadioTimer = -1f;
            caller.HasRadioed = true;
            Bark(caller, "radio", force: true);
            Emit(new GameEvent { Type = EvType.Radio, Pos = caller.Pos, ActorId = caller.Id, Flag = false, Text = "alarm" });
            RaiseAlarm(60f);
            foreach (var n in Npcs)
            {
                if (n == caller || n.Down || n.IsCamera) continue;
                if (Vec2.Distance(n.Pos, caller.Pos) > 40f) continue;
                n.LastKnownPlayer = caller.LastKnownPlayer;
                if (n.IsHostile)
                {
                    if (n.State != AIState.Attack && n.State != AIState.Chase)
                    {
                        n.LastSeenAgo = 1f;
                        SetState(n, AIState.Chase);
                    }
                }
                else if (n.IsTarget) SetState(n, AIState.Flee);
            }
        }

        private void CameraAlarm(Npc cam)
        {
            cam.CameraCooldown = 8f;
            Emit(new GameEvent { Type = EvType.Radio, Pos = cam.Pos, ActorId = cam.Id, Text = "camera" });
            RaiseAlarm(45f);
            // The three closest guards come running.
            for (int k = 0; k < 3; k++)
            {
                Npc best = null;
                float bestD = 45f;
                foreach (var n in Npcs)
                {
                    if (n.Down || !n.IsHostile || n.IsTarget || n.State == AIState.Attack || n.State == AIState.Chase) continue;
                    if (n.InvestigateIsLoud && n.State == AIState.Investigate && Vec2.Distance(n.InvestigatePos, Player.Pos) < 3f) continue;
                    float d = Vec2.Distance(n.Pos, Player.Pos);
                    if (d < bestD) { bestD = d; best = n; }
                }
                if (best == null) break;
                best.InvestigatePos = Player.Pos;
                best.InvestigateIsLoud = true;
                best.InvestigateObject = null;
                best.Suspicion = Math.Max(best.Suspicion, 0.7f);
                SetState(best, AIState.Investigate);
                if (k == 0) Bark(best, "camera", force: true);
            }
        }

        public void OnBodySeen(Npc n, Body b)
        {
            if (b == null || b.Discovered || n.Down) return;
            if (n.IsHostile && !n.IsTarget)
            {
                if (n.State == AIState.Attack || n.State == AIState.Chase) return;
                n.InvestigateBody = b.Id;
                n.InvestigatePos = b.Pos;
                n.InvestigateIsLoud = true;
                n.InvestigateObject = null;
                Bark(n, "body_seen", force: true);
                SetState(n, AIState.Investigate);
            }
            else
            {
                DiscoverBody(n, b);
                Panic(n, b.Pos);
            }
        }

        private void DiscoverBody(Npc n, Body b)
        {
            if (b.Discovered) return;
            b.Discovered = true;
            BodiesFound++;
            Emit(new GameEvent { Type = EvType.BodyFound, Pos = b.Pos, ActorId = n.Id });
            Bark(n, "body", force: true);
            RaiseAlarm(70f);
            if (!n.IsHostile) return;
            // Nearby guards help search the area.
            foreach (var o in Npcs)
            {
                if (o == n || o.Down || !o.IsHostile || o.IsTarget) continue;
                if (o.State == AIState.Attack || o.State == AIState.Chase || o.State == AIState.Follow) continue;
                if (Vec2.Distance(o.Pos, b.Pos) > 16f) continue;
                o.InvestigatePos = b.Pos;
                o.InvestigateIsLoud = true;
                o.InvestigateObject = null;
                SetState(o, AIState.Investigate);
            }
        }

        /// <summary>Civilians run to the nearest guard to report; unarmed targets run for the exit.</summary>
        public void Panic(Npc n, Vec2 threat)
        {
            if (n.Down || n.State == AIState.Panic || n.State == AIState.Flee || n.State == AIState.Cower) return;
            n.LastKnownPlayer = threat;
            if (n.IsTarget && !n.Armed)
            {
                Bark(n, "flee", force: true);
                SetState(n, AIState.Flee);
                Message($"{n.DisplayName} is trying to escape!");
                return;
            }
            Npc best = null;
            float bestD = 30f;
            foreach (var g in Npcs)
            {
                if (g.Down || !g.IsHostile || g.IsTarget) continue;
                float d = Vec2.Distance(g.Pos, n.Pos);
                if (d < bestD) { bestD = d; best = g; }
            }
            Bark(n, "panic", force: true);
            if (best == null) { SetState(n, AIState.Cower); return; }
            n.ReportTo = best;
            SetState(n, AIState.Panic);
        }

        // ------------------------------------------------------------------ noise

        /// <summary>Emits a noise. Walls muffle sound (radius x0.6 without line of sight).</summary>
        public void EmitNoise(Vec2 pos, float radius, NoiseKind kind, Actor source, Interactable obj = null)
        {
            if (radius <= 0f) return;
            Emit(new GameEvent { Type = EvType.Noise, Pos = pos, Value = radius, Text = kind.ToString(), ActorId = source?.Id ?? -1 });
            foreach (var n in Npcs)
            {
                if (n.Down || n.IsCamera || n == source) continue;
                float d = Vec2.Distance(n.Pos, pos);
                if (d > radius) continue;
                if (d > radius * 0.6f && !World.HasLineOfSight(n.Pos, pos)) continue;
                HearNoise(n, pos, kind, source, obj);
            }
        }

        private void HearNoise(Npc n, Vec2 pos, NoiseKind kind, Actor source, Interactable obj)
        {
            bool loud = kind == NoiseKind.Gunshot || kind == NoiseKind.Explosion || kind == NoiseKind.GlassBreak || kind == NoiseKind.Shout;

            if (!n.IsHostile)
            {
                if (loud && (source == Player || kind == NoiseKind.Explosion))
                {
                    if (n.IsTarget) Panic(n, pos);
                    else if (n.State != AIState.Panic && n.State != AIState.Cower)
                    {
                        Bark(n, "panic");
                        n.LastKnownPlayer = pos;
                        SetState(n, AIState.Cower);
                    }
                }
                else if (IsCalmState(n.State) && n.State != AIState.Suspicious && kind != NoiseKind.Footstep)
                {
                    n.DesiredFacing = (pos - n.Pos).Angle;
                    n.LastKnownPlayer = pos;
                }
                return;
            }

            if (n.State == AIState.Attack || n.State == AIState.Chase)
            {
                if (loud && source == Player) n.LastKnownPlayer = pos;
                return;
            }

            if (loud)
            {
                // Gunfire from colleagues means combat: go help.
                n.InvestigatePos = source is Npc ally && ally.IsHostile ? ally.LastKnownPlayer : pos;
                n.InvestigateIsLoud = true;
                n.InvestigateObject = null;
                n.Suspicion = Math.Max(n.Suspicion, 0.7f);
                if (n.State != AIState.Investigate || !n.InvestigateIsLoud) Bark(n, "gunshot");
                if (n.State != AIState.Follow) SetState(n, AIState.Investigate);
                else n.DesiredFacing = (pos - n.Pos).Angle;
                RaiseAlarm(40f);
                return;
            }

            if (n.State == AIState.Search || n.State == AIState.Fixing) return;
            if (n.State == AIState.Investigate && Vec2.Distance(n.InvestigatePos, pos) < 3f) return;

            if (kind == NoiseKind.Footstep)
            {
                n.Suspicion = Math.Min(0.75f, n.Suspicion + 0.3f);
                n.LastKnownPlayer = pos;
                n.SuspicionDecayDelay = 2f;
                if (n.State == AIState.Follow) { n.DesiredFacing = (pos - n.Pos).Angle; return; }
                if (n.State != AIState.Investigate) SetState(n, AIState.Suspicious);
                return;
            }

            // Curious noises: doors, distractions, takedowns, suppressed shots, bodies falling.
            if (n.State == AIState.Follow) { n.DesiredFacing = (pos - n.Pos).Angle; return; }
            // Don't send everyone: if someone else already investigates this spot, just look.
            foreach (var o in Npcs)
                if (o != n && !o.Down && o.State == AIState.Investigate && Vec2.Distance(o.InvestigatePos, pos) < 3f)
                {
                    n.DesiredFacing = (pos - n.Pos).Angle;
                    if (n.State == AIState.Idle || n.State == AIState.Patrol) SetState(n, AIState.Suspicious);
                    n.LastKnownPlayer = pos;
                    return;
                }
            n.InvestigatePos = pos;
            n.InvestigateIsLoud = kind == NoiseKind.SuppressedShot || kind == NoiseKind.Takedown;
            n.InvestigateObject = obj;
            n.Suspicion = Math.Max(n.Suspicion, 0.4f);
            Bark(n, kind == NoiseKind.Distraction && obj != null ? "noise" : "suspicious");
            SetState(n, AIState.Investigate);
        }
    }
}
