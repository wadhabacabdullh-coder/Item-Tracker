using System;
using System.Collections.Generic;

namespace ShadowContract.Core
{
    public enum CandidateKind { None, LeaveHiding, StashBody, DropBody, Hide, DragBody, Interactable, WeaponPickup, Door, Window }

    public struct InteractCandidate
    {
        public CandidateKind Kind;
        public string Prompt;
        public Vec2 Pos;
        public float HoldTime;          // > 0 = hold-to-use
        public bool Enabled;            // false = only show the prompt (e.g. locked door without key)
        public Interactable Interactable;
        public Body Body;
        public DoorState Door;
        public Pickup Pickup;
        public Int2 Cell;
        public Int2 Dir;
    }

    public sealed partial class GameSession
    {
        /// <summary>What pressing Interact would do right now. Read by the HUD for the prompt.</summary>
        public InteractCandidate Candidate;
        private readonly Dictionary<Int2, int> _closetBodies = new Dictionary<Int2, int>();
        private float _holdProgress;
        private InteractCandidate _holdTarget;

        public float HoldProgress => _holdProgress;

        private static readonly Int2[] Dirs4 = { new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1) };

        private void UpdateInteraction(float dt, PlayerInput input)
        {
            Candidate = FindCandidate();
            var c = Candidate;
            if (c.Kind == CandidateKind.None || !c.Enabled)
            {
                _holdProgress = 0f;
                if (c.Kind != CandidateKind.None && input.InteractPressed && c.Door != null) Emit(EvType.DoorLocked, c.Door.Cell.Center);
                return;
            }

            if (c.HoldTime > 0f)
            {
                bool same = _holdTarget.Kind == c.Kind && _holdTarget.Interactable == c.Interactable;
                if (input.InteractHeld && same && !Player.Moving)
                {
                    _holdProgress += dt / c.HoldTime;
                    Player.ActionLabel = c.Prompt;
                    if (_holdProgress >= 1f)
                    {
                        _holdProgress = 0f;
                        Player.ActionLabel = null;
                        Execute(c);
                    }
                }
                else
                {
                    _holdProgress = input.InteractPressed ? 0.0001f : 0f;
                    if (!input.InteractHeld) Player.ActionLabel = null;
                }
                _holdTarget = c;
                return;
            }
            _holdProgress = 0f;
            if (input.InteractPressed) Execute(c);
        }

        private InteractCandidate FindCandidate()
        {
            var p = Player;
            var best = new InteractCandidate { Kind = CandidateKind.None };
            float bestScore = float.MaxValue;
            Vec2 fwd = Vec2.FromAngle(p.Facing);

            void Consider(InteractCandidate c, float dist, float priority)
            {
                // Prefer things in front of the player.
                Vec2 to = c.Pos - p.Pos;
                float facingPenalty = to.SqrLength > 0.01f ? (1f - Vec2.Dot(to.Normalized, fwd)) * 0.4f : 0f;
                float score = dist + facingPenalty + priority;
                if (score < bestScore) { bestScore = score; best = c; }
            }

            var pc = Int2.FromWorld(p.Pos);
            bool dragging = p.DraggingBody >= 0;

            // Closets (hide / stash)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var cell = new Int2(pc.x + dx, pc.y + dy);
                var t = World.TileAt(cell.x, cell.y);
                if (!t.Is(TileFlags.HidingSpot)) continue;
                float d = Vec2.Distance(cell.Center, p.Pos);
                if (d > 1.25f) continue;
                bool occupied = _closetBodies.ContainsKey(cell);
                if (dragging)
                    Consider(new InteractCandidate { Kind = CandidateKind.StashBody, Pos = cell.Center, Cell = cell, Enabled = !occupied, Prompt = occupied ? "Closet is full" : "Stash body in closet" }, d, -0.5f);
                else
                    Consider(new InteractCandidate { Kind = CandidateKind.Hide, Pos = cell.Center, Cell = cell, Enabled = !occupied, Prompt = occupied ? "Closet is full" : "Hide in closet" }, d, 0.1f);
            }

            if (dragging)
            {
                Consider(new InteractCandidate { Kind = CandidateKind.DropBody, Pos = p.Pos, Enabled = true, Prompt = "Drop body" }, 1.2f, 0f);
                return best; // while dragging only body actions are available
            }

            // Bodies
            foreach (var b in Bodies)
            {
                if (b.Hidden) continue;
                float d = Vec2.Distance(b.Pos, p.Pos);
                if (d > 1.1f) continue;
                Consider(new InteractCandidate { Kind = CandidateKind.DragBody, Pos = b.Pos, Body = b, Enabled = true, Prompt = "Drag body" }, d, 0.2f);
            }

            // Interactables
            foreach (var it in Interactables)
            {
                float d = Vec2.Distance(it.Pos, p.Pos);
                if (d > 1.35f) continue;
                if (it.Kind != InteractKind.Stairs && !World.HasLineOfSight(p.Pos, it.Pos) && d > 0.8f) continue;
                var c = new InteractCandidate { Kind = CandidateKind.Interactable, Pos = it.Pos, Interactable = it, Enabled = true };
                switch (it.Kind)
                {
                    case InteractKind.PowerBox:
                        c.Prompt = it.Active ? $"Restore power ({it.Label})" : $"Cut power ({it.Label})";
                        break;
                    case InteractKind.CameraTerminal:
                        c.Prompt = it.Used ? "Cameras offline" : "Disable security cameras";
                        c.Enabled = !it.Used;
                        c.HoldTime = it.Duration;
                        break;
                    case InteractKind.DownloadTerminal:
                        c.Prompt = it.Used ? "Download complete" : $"Download data ({it.Label})";
                        c.Enabled = !it.Used;
                        c.HoldTime = it.Duration;
                        break;
                    case InteractKind.Distraction:
                        c.Prompt = it.Active ? $"{it.Label} is running" : $"Turn on {it.Label.ToLowerInvariant()}";
                        c.Enabled = !it.Active;
                        break;
                    case InteractKind.Stairs:
                        bool locked = it.Lock != null && !p.Inventory.Keys.Contains(it.Lock);
                        c.Prompt = locked ? $"{it.Label} - requires {it.Lock} keycard" : (it.Label == "Stairs" ? "Take the stairs" : $"Use {it.Label.ToLowerInvariant()}");
                        c.Enabled = !locked;
                        break;
                }
                Consider(c, d, 0f);
            }

            // Weapon pickups
            foreach (var pk in Pickups)
            {
                if (pk.Taken || !pk.RequiresInteract) continue;
                float d = Vec2.Distance(pk.Pos, p.Pos);
                if (d > 1.0f) continue;
                Consider(new InteractCandidate { Kind = CandidateKind.WeaponPickup, Pos = pk.Pos, Pickup = pk, Enabled = true, Prompt = $"Pick up {pk.Name}" }, d, 0.1f);
            }

            // Doors and windows next to the player
            foreach (var dir in Dirs4)
            for (int reach = 1; reach <= 1; reach++)
            {
                var cell = new Int2(pc.x + dir.x * reach, pc.y + dir.y * reach);
                float d = Vec2.Distance(cell.Center, p.Pos);
                if (d > 1.3f) continue;
                var door = World.DoorAt(cell.x, cell.y);
                if (door != null)
                {
                    var c = new InteractCandidate { Kind = CandidateKind.Door, Pos = cell.Center, Door = door, Enabled = true, Cell = cell };
                    if (door.Type == DoorType.Secret && !door.Open) c.Prompt = "Examine bookshelf";
                    else if (door.Open) c.Prompt = "Close door";
                    else if (door.Locked)
                    {
                        bool has = p.Inventory.Keys.Contains(door.KeyId);
                        c.Prompt = has ? $"Unlock with {door.KeyId} keycard" : $"Locked - requires {door.KeyId} keycard";
                        c.Enabled = has;
                    }
                    else c.Prompt = "Open door";
                    Consider(c, d, 0.15f);
                    continue;
                }
                var t = World.TileAt(cell.x, cell.y);
                if (t.Kind == TileKind.Window)
                {
                    var beyond = cell + dir;
                    bool ok = !World.IsSolid(beyond.x, beyond.y, MoverKind.Player);
                    if (ok) Consider(new InteractCandidate { Kind = CandidateKind.Window, Pos = cell.Center, Cell = cell, Dir = dir, Enabled = true, Prompt = "Climb through window" }, d, 0.3f);
                }
            }
            return best;
        }

        private void Execute(InteractCandidate c)
        {
            var p = Player;
            switch (c.Kind)
            {
                case CandidateKind.Hide:
                    p.Hidden = true;
                    p.HiddenIn = c.Cell;
                    p.HideExitPos = p.Pos;
                    p.Crouched = true;
                    p.Inventory.CurrentWeapon?.CancelReload();
                    Emit(new GameEvent { Type = EvType.Hide, Pos = c.Cell.Center, ActorId = 0 });
                    break;
                case CandidateKind.StashBody:
                {
                    var body = Bodies.Find(b => b.Id == p.DraggingBody);
                    if (body != null)
                    {
                        body.Hidden = true;
                        body.Dragged = false;
                        body.Pos = c.Cell.Center;
                        _closetBodies[c.Cell] = body.Id;
                        Emit(new GameEvent { Type = EvType.BodyStash, Pos = c.Cell.Center, ActorId = body.Id });
                    }
                    p.DraggingBody = -1;
                    break;
                }
                case CandidateKind.DropBody:
                {
                    var body = Bodies.Find(b => b.Id == p.DraggingBody);
                    if (body != null) { body.Dragged = false; Emit(new GameEvent { Type = EvType.BodyDrop, Pos = body.Pos, ActorId = body.Id }); }
                    p.DraggingBody = -1;
                    break;
                }
                case CandidateKind.DragBody:
                    p.DraggingBody = c.Body.Id;
                    c.Body.Dragged = true;
                    p.Crouched = p.Crouched && true;
                    if (p.Inventory.CurrentWeapon != null && p.Inventory.CurrentWeapon.Reloading) p.Inventory.CurrentWeapon.CancelReload();
                    Emit(new GameEvent { Type = EvType.BodyDrag, Pos = c.Body.Pos, ActorId = c.Body.Id });
                    break;
                case CandidateKind.WeaponPickup:
                    TakePickup(c.Pickup);
                    break;
                case CandidateKind.Door:
                    UseDoor(c.Door);
                    break;
                case CandidateKind.Window:
                {
                    var dest = (c.Cell + c.Dir).Center;
                    p.Pos = dest;
                    p.ActionLock = 0.55f;
                    p.ActionLabel = "Climbing";
                    Emit(new GameEvent { Type = EvType.Vault, Pos = c.Cell.Center, Pos2 = dest, ActorId = 0 });
                    EmitNoise(c.Cell.Center, p.Crouched ? 2.5f : 3.5f, NoiseKind.Door, p);
                    break;
                }
                case CandidateKind.Interactable:
                    UseInteractable(c.Interactable);
                    break;
            }
        }

        private void LeaveHiding()
        {
            var p = Player;
            p.Hidden = false;
            p.Pos = World.ResolveCircle(p.HideExitPos, p.Radius, MoverKind.PlayerCrouched);
            Emit(new GameEvent { Type = EvType.Unhide, Pos = p.HiddenIn.Center, ActorId = 0 });
            // Someone waiting outside sees you come out.
            foreach (var n in Npcs)
                if (!n.Down && CanSee(n, p.Pos, p.Crouched) > 0f && Vec2.Distance(n.Pos, p.Pos) < 4f)
                    n.Suspicion = Math.Min(1f, n.Suspicion + 0.6f);
        }

        /// <summary>Called by searching guards that check a closet. Returns true if they found the player.</summary>
        public bool CheckCloset(Npc n, Int2 cell)
        {
            if (Player.Hidden && Player.HiddenIn == cell)
            {
                LeaveHiding();
                SpotPlayer(n);
                Bark(n, "found");
                return true;
            }
            if (_closetBodies.TryGetValue(cell, out int bodyId))
            {
                var body = Bodies.Find(b => b.Id == bodyId);
                if (body != null && !body.Discovered)
                {
                    body.Hidden = false;
                    _closetBodies.Remove(cell);
                    OnBodySeen(n, body);
                }
            }
            return false;
        }

        private void UseDoor(DoorState d)
        {
            var p = Player;
            if (d.Open) { CloseDoor(d); return; }
            if (d.Type == DoorType.Secret)
            {
                d.Locked = false;
                OpenDoor(d, p, false);
                Emit(new GameEvent { Type = EvType.SecretFound, Pos = d.Cell.Center, Text = "A hidden passage!" });
                Message("A hidden passage!");
                return;
            }
            if (d.Locked)
            {
                if (!p.Inventory.Keys.Contains(d.KeyId)) { Emit(EvType.DoorLocked, d.Cell.Center); return; }
                d.Locked = false;
                Emit(new GameEvent { Type = EvType.DoorUnlock, Pos = d.Cell.Center, Text = d.KeyId });
            }
            OpenDoor(d, p, false);
        }

        private void UseInteractable(Interactable it)
        {
            var p = Player;
            switch (it.Kind)
            {
                case InteractKind.PowerBox:
                    SetLightGroup(it, !it.Active);
                    EmitNoise(it.Pos, 3f, NoiseKind.Door, p);
                    if (it.Active) AssignFixer(it);
                    break;
                case InteractKind.CameraTerminal:
                    it.Used = true;
                    foreach (var n in Npcs)
                        if (n.IsCamera && n.Group == it.Group && n.Alive)
                        {
                            n.State = AIState.Disabled;
                            n.Suspicion = 0f;
                        }
                    Emit(new GameEvent { Type = EvType.CameraDisabled, Pos = it.Pos, Text = it.Group });
                    Message("Security cameras disabled");
                    break;
                case InteractKind.DownloadTerminal:
                    it.Used = true;
                    Emit(new GameEvent { Type = EvType.Download, Pos = it.Pos, Text = it.Key });
                    Message("Download complete");
                    break;
                case InteractKind.Distraction:
                    it.Active = true;
                    it.Timer = 10f;
                    Emit(new GameEvent { Type = EvType.Distraction, Pos = it.Pos, Text = it.Sound, Flag = true, ActorId = it.Id });
                    EmitNoise(it.Pos, 7.5f, NoiseKind.Distraction, null, it);
                    break;
                case InteractKind.Stairs:
                {
                    Vec2 from = p.Pos;
                    p.Pos = World.ResolveCircle(it.Pos2, p.Radius, MoverKind.Player);
                    if (p.DraggingBody >= 0)
                    {
                        var body = Bodies.Find(b => b.Id == p.DraggingBody);
                        if (body != null) body.Pos = p.Pos;
                    }
                    p.ActionLock = 0.3f;
                    Emit(new GameEvent { Type = EvType.Teleport, Pos = from, Pos2 = p.Pos, Text = it.Label, ActorId = 0 });
                    break;
                }
            }
        }

        public void SetLightGroup(Interactable box, bool off)
        {
            box.Active = off;
            foreach (var l in Lights)
                if (l.Group == box.Group) l.On = !off;
            LightMap.Bake(World, Lights, 1, null);
            LightsDirty = true;
            Emit(new GameEvent { Type = EvType.Lights, Pos = box.Pos, Text = box.Group, Flag = off });
            if (off) Message("Power cut - the area goes dark");
        }

        /// <summary>The nearest guard comes to switch the power back on.</summary>
        private void AssignFixer(Interactable box)
        {
            Npc best = null;
            float bestD = 22f;
            foreach (var n in Npcs)
            {
                if (n.Down || !n.IsHostile || n.IsTarget || n.State == AIState.Attack || n.State == AIState.Chase || n.State == AIState.Follow) continue;
                float d = Vec2.Distance(n.Pos, box.Pos);
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best == null) return;
            best.InvestigateObject = box;
            best.InvestigatePos = box.Pos;
            best.InvestigateIsLoud = false;
            SetState(best, AIState.Investigate);
            Bark(best, "lights");
        }

        private void UpdateInteractables(float dt)
        {
            foreach (var it in Interactables)
            {
                if (it.Kind != InteractKind.Distraction || !it.Active) continue;
                it.Timer -= dt;
                if (it.Timer <= 0f) { StopDistraction(it); continue; }
                // Repeated pulses so guards further away keep being drawn in.
                if ((int)(it.Timer / 2.5f) != (int)((it.Timer + dt) / 2.5f))
                    EmitNoise(it.Pos, 7.5f, NoiseKind.Distraction, null, it);
            }
        }

        public void StopDistraction(Interactable it)
        {
            if (!it.Active) return;
            it.Active = false;
            Emit(new GameEvent { Type = EvType.Distraction, Pos = it.Pos, Text = it.Sound, Flag = false, ActorId = it.Id });
        }
    }
}
