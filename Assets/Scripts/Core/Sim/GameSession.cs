using System;
using System.Collections.Generic;
using System.Linq;

namespace ShadowContract.Core
{
    /// <summary>
    /// The complete, engine-independent simulation of one mission. The Unity layer feeds it input every frame,
    /// renders its state and turns <see cref="Events"/> into sounds, particles and UI.
    /// Split across partial files: Player, Combat, Interaction, AI, Perception.
    /// </summary>
    public sealed partial class GameSession
    {
        public const float Tick = 1f / 60f;

        public readonly MapData Map;
        public readonly MissionDef Mission;
        public readonly World World;
        public readonly Pathfinder Paths;
        public readonly DifficultySettings Diff;
        public readonly Rng Rng;

        public readonly Player Player = new Player();
        public readonly List<Npc> Npcs = new List<Npc>();
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<Pickup> Pickups = new List<Pickup>();
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Interactable> Interactables = new List<Interactable>();
        public readonly List<LightDef> Lights = new List<LightDef>();
        public readonly List<Zone> Zones = new List<Zone>();
        public readonly List<ExtractZone> Extracts = new List<ExtractZone>();
        public readonly List<GameEvent> Events = new List<GameEvent>();

        public float Time;
        public MissionState State = MissionState.Playing;
        public string FailReason;
        public AlertLevel Alert = AlertLevel.Calm;
        public float AlertTimer;
        public bool Spotted;
        public bool LightsDirty = true;     // view must rebake the light texture
        public bool ShowVisionCones = true;
        public bool DebugNoFail;            // debug cheat: targets escaping does not end the mission

        // statistics
        public int Kills, TargetsKilled, CiviliansKilled, NonTargetKills, Subdued, BodiesFound, ShotsFired;

        private readonly bool[] _objectiveDone;
        private int _nextId = 1;
        private float _extractMsgCooldown;
        private MissionResult _result;

        public GameSession(MapData map, MissionDef mission, Difficulty difficulty, LoadoutConfig loadout, int seed = 12345)
        {
            Map = map;
            Mission = mission;
            World = new World(map);
            Paths = new Pathfinder(World);
            Diff = DifficultySettings.For(difficulty);
            Rng = new Rng(seed);
            _objectiveDone = new bool[mission.Objectives.Count];
            Build(loadout ?? new LoadoutConfig());
            LightMap.Bake(World, Lights, 1, null);
        }

        public int NewId() => _nextId++;

        // ------------------------------------------------------------------ construction

        private void Build(LoadoutConfig loadout)
        {
            var targets = new Dictionary<string, Npc>();
            foreach (var e in Map.EntitiesFor(Mission.Id))
            {
                switch (e.Kind)
                {
                    case "spawn":
                        Player.Pos = Map.ParsePoint(e.Str("at"));
                        Player.Facing = MathUtil.Pi / 2f;
                        break;
                    case "extract":
                        Extracts.Add(new ExtractZone { Label = e.Str("label", "Extraction"), Rect = Map.ParseRect(e.Str("rect")) });
                        break;
                    case "zone":
                        Zones.Add(new Zone { Name = e.Str("name"), Rect = Map.ParseRect(e.Str("rect")) });
                        break;
                    case "light":
                        Lights.Add(LightDef.FromEntity(e, Map));
                        break;
                    case "stairs":
                    {
                        var a = Map.ParsePoint(e.Str("a"));
                        var b = Map.ParsePoint(e.Str("b"));
                        Paths.AddLink(Int2.FromWorld(a), Int2.FromWorld(b));
                        string label = e.Str("label", "Stairs");
                        string lk = e.Str("lock");
                        Interactables.Add(new Interactable { Id = NewId(), Kind = InteractKind.Stairs, Pos = a, Pos2 = b, Label = label, Lock = lk, Secret = e.Bool("secret") });
                        Interactables.Add(new Interactable { Id = NewId(), Kind = InteractKind.Stairs, Pos = b, Pos2 = a, Label = label, Lock = lk, Secret = e.Bool("secret") });
                        break;
                    }
                    case "powerbox":
                        Interactables.Add(new Interactable { Id = NewId(), Kind = InteractKind.PowerBox, Pos = Map.ParsePoint(e.Str("at")), Group = e.Str("group"), Label = e.Str("label", "Fuse box") });
                        break;
                    case "terminal":
                    {
                        var kind = e.Str("action") == "cameras" ? InteractKind.CameraTerminal : InteractKind.DownloadTerminal;
                        Interactables.Add(new Interactable
                        {
                            Id = NewId(), Kind = kind, Key = e.Str("id"), Pos = Map.ParsePoint(e.Str("at")), Group = e.Str("group"),
                            Label = e.Str("label", "Terminal"), Duration = e.Float("time", kind == InteractKind.CameraTerminal ? 2f : 5f),
                        });
                        break;
                    }
                    case "distraction":
                        Interactables.Add(new Interactable { Id = NewId(), Kind = InteractKind.Distraction, Pos = Map.ParsePoint(e.Str("at")), Sound = e.Str("kind", "radio"), Label = DistractionLabel(e.Str("kind", "radio")) });
                        break;
                    case "pickup":
                        Pickups.Add(MakePickup(e));
                        break;
                    case "camera":
                    {
                        var cam = new Npc
                        {
                            Id = NewId(), Key = "cam" + Npcs.Count, Kind = NpcKind.Camera, Type = "camera", Pos = Map.ParsePoint(e.Str("at")),
                            Group = e.Str("group"), ViewRange = 8f, ViewAngle = 60f * MathUtil.Deg2Rad, Health = 30, MaxHealth = 30, Radius = 0.25f,
                            State = AIState.Idle,
                        };
                        cam.SweepCenter = e.Float("facing") * MathUtil.Deg2Rad;
                        cam.SweepHalf = e.Float("sweep", 45f) * MathUtil.Deg2Rad;
                        cam.Facing = cam.SweepCenter;
                        cam.PerceptionTimer = Rng.Value() * 0.1f;
                        Npcs.Add(cam);
                        break;
                    }
                    case "guard":
                    case "civilian":
                    case "target":
                    {
                        var npc = MakeNpc(e);
                        Npcs.Add(npc);
                        if (npc.IsTarget) targets[npc.Key] = npc;
                        break;
                    }
                }
            }
            // Bodyguards follow their principal (only present in the mission that defines both).
            foreach (var n in Npcs)
                if (n.FollowKey != null && targets.TryGetValue(n.FollowKey, out var t))
                {
                    n.FollowTarget = t;
                    n.State = AIState.Follow;
                }
                else n.FollowKey = null;

            SetupPlayer(loadout);
        }

        private static string DistractionLabel(string kind)
        {
            switch (kind)
            {
                case "piano": return "Piano";
                case "vending": return "Vending machine";
                case "printer": return "Printer";
                default: return "Radio";
            }
        }

        private Pickup MakePickup(EntityDef e)
        {
            string item = e.Str("item");
            var p = new Pickup { Id = NewId(), Pos = Map.ParsePoint(e.Str("at")), Amount = e.Int("amount", 1), ItemId = item };
            switch (item)
            {
                case "medkit": p.Kind = PickupKind.Medkit; p.Name = "Medkit"; break;
                case "armor": p.Kind = PickupKind.Armor; p.Name = "Armor plate"; p.Amount = e.Int("amount", 35); break;
                case "cash": p.Kind = PickupKind.Cash; p.Name = "Cash"; break;
                case "ammo": p.Kind = PickupKind.Ammo; p.Name = "Ammo"; p.AmmoType = AmmoType.Pistol; p.Amount = e.Int("amount", 24); break;
                case "throwing_knives": p.Kind = PickupKind.Knives; p.Name = "Throwing knives"; p.AmmoType = AmmoType.Knives; break;
                case "keycard_blue": p.Kind = PickupKind.Keycard; p.ItemId = "blue"; p.Name = "Blue Keycard"; break;
                case "keycard_red": p.Kind = PickupKind.Keycard; p.ItemId = "red"; p.Name = "Red Keycard"; break;
                case "intel": p.Kind = PickupKind.Intel; p.ItemId = e.Str("id"); p.Name = e.Str("name", "Intel"); break;
                default:
                    if (item.StartsWith("weapon_"))
                    {
                        string wid = item.Substring(7);
                        if (wid == "rifle") wid = "assault_rifle";
                        var def = WeaponCatalog.Get(wid) ?? throw new FormatException("Unknown weapon pickup " + item);
                        p.Kind = PickupKind.Weapon;
                        p.ItemId = def.Id;
                        p.Name = def.Name;
                        p.Amount = def.MagSize * 2;
                        p.AmmoType = def.Ammo;
                    }
                    else throw new FormatException($"Unknown pickup '{item}' at line {e.Line}");
                    break;
            }
            return p;
        }

        private Npc MakeNpc(EntityDef e)
        {
            var n = new Npc
            {
                Id = NewId(),
                Key = e.Str("id"),
                Pos = Map.ParsePoint(e.Str("at")),
                Facing = e.Float("facing", 270f) * MathUtil.Deg2Rad,
                Route = Map.ParseRoute(e.Str("route")),
                CarriedKey = e.Str("key"),
                FollowKey = e.Str("follow"),
                PerceptionTimer = Rng.Value() * 0.1f,
            };
            string type = e.Str("type", e.Kind == "civilian" ? "staff" : e.Kind);
            n.Type = type;
            if (e.Kind == "civilian") { n.Kind = NpcKind.Civilian; n.ViewRange = 8f; }
            else if (e.Kind == "target") { n.Kind = NpcKind.Target; n.Type = "target"; n.DisplayName = e.Str("name", "Target"); }
            else n.Kind = type == "elite" ? NpcKind.Elite : NpcKind.Guard;

            string weapon = e.Str("weapon", n.Kind == NpcKind.Guard || n.Kind == NpcKind.Elite ? "pistol" : "none");
            if (weapon != "none")
            {
                if (weapon == "rifle") weapon = "assault_rifle";
                n.Weapon = new WeaponState(WeaponCatalog.Get(weapon) ?? WeaponCatalog.Get("pistol"));
            }
            n.MaxHealth = n.Health = e.Float("hp", n.Kind == NpcKind.Elite ? 150f : 100f);
            if (n.Kind == NpcKind.Elite) n.ViewRange = 10f;
            n.HomePos = n.Pos;
            n.HomeFacing = n.Facing;
            if (e.Has("look"))
                n.LookAngles = e.Str("look").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture) * MathUtil.Deg2Rad).ToArray();
            if (e.Has("escape")) n.EscapePos = Map.ParsePoint(e.Str("escape"));
            n.State = n.Route.Count > 0 ? AIState.Patrol : AIState.Idle;
            n.LookTimer = 2f + Rng.Range(0f, 2f);
            if (n.Route.Count > 0) n.Activity = n.Route[0].Activity;
            return n;
        }

        private void SetupPlayer(LoadoutConfig lo)
        {
            Player.Id = 0;
            Player.MaxHealth = Player.Health = Diff.PlayerHealth;
            Player.MaxArmor = Player.Armor = lo.Armor;
            Player.SpeedMult = lo.SpeedMult;
            var inv = Player.Inventory;
            foreach (var kv in lo.Ammo) inv.Reserve[kv.Key] = kv.Value;
            inv.Medkits = lo.Medkits;
            for (int i = 0; i < 5; i++)
            {
                string id = lo.SlotWeapons != null && i < lo.SlotWeapons.Length ? lo.SlotWeapons[i] : null;
                var def = id != null ? WeaponCatalog.Get(id) : null;
                if (def == null) continue;
                var applied = UpgradeCatalog.Apply(def, lo.Upgrades);
                var ws = new WeaponState(applied, 0);
                if (applied.IsGun)
                {
                    int take = Math.Min(applied.MagSize, inv.GetReserve(applied.Ammo));
                    ws.Mag = take;
                    inv.AddReserve(applied.Ammo, -take);
                }
                inv.SetSlot(applied.Slot, ws);
            }
            if (inv.Slots[0] == null) inv.SetSlot(WeaponSlot.Melee, new WeaponState(WeaponCatalog.Get("knife")));
            inv.Current = inv.Slots[1] != null ? 1 : 0;
        }

        // ------------------------------------------------------------------ main loop

        /// <summary>Advances the simulation. Call with a fixed step (see <see cref="Tick"/>).</summary>
        public void Update(float dt, PlayerInput input)
        {
            Events.Clear();
            if (State != MissionState.Playing) return;
            Time += dt;
            _extractMsgCooldown -= dt;

            UpdatePlayer(dt, input);
            UpdateProjectiles(dt);
            UpdateNpcs(dt);
            UpdateDoors(dt);
            UpdateInteractables(dt);
            UpdateAlert(dt);
            UpdateObjectives();
        }

        public void Emit(EvType type, Vec2 pos, string text = null, float value = 0f, int actor = -1, string sound = null)
        {
            Events.Add(new GameEvent { Type = type, Pos = pos, Text = text, Value = value, ActorId = actor, Sound = sound });
        }

        public void Emit(GameEvent e) => Events.Add(e);

        public void Message(string text) => Emit(EvType.Message, Player.Pos, text);

        public string CurrentZoneName()
        {
            string best = null;
            float bestArea = float.MaxValue;
            foreach (var z in Zones)
            {
                if (!z.Rect.Contains(Player.Pos)) continue;
                float area = z.Rect.W * z.Rect.H;
                if (area < bestArea) { bestArea = area; best = z.Name; }
            }
            return best ?? Map.Name;
        }

        public Npc FindNpc(string key) => Npcs.Find(n => n.Key == key);

        /// <summary>Highest suspicion any NPC currently holds towards the player (0..1) - drives the HUD meter.</summary>
        public float DetectionLevel
        {
            get
            {
                float m = 0f;
                foreach (var n in Npcs) if (!n.Down && n.State != AIState.Disabled) m = Math.Max(m, n.Suspicion);
                return m;
            }
        }

        // ------------------------------------------------------------------ doors

        private void UpdateDoors(float dt)
        {
            foreach (var d in World.Doors)
            {
                d.Anim = MathUtil.MoveTowards(d.Anim, d.Open ? 1f : 0f, dt * 6f);
                if (d.AutoClose > 0f)
                {
                    d.AutoClose -= dt;
                    if (d.AutoClose <= 0f)
                    {
                        if (AnyActorInCell(d.Cell)) d.AutoClose = 0.5f;
                        else
                        {
                            d.Open = false;
                            Emit(EvType.DoorClose, d.Cell.Center, null, d.Index);
                            LightsDirty = true;
                        }
                    }
                }
            }
        }

        /// <summary>Is any character (or body) touching this cell? Doors never close on someone.</summary>
        private bool AnyActorInCell(Int2 cell, float margin = 0.08f)
        {
            if (!Player.Hidden && CircleTouchesCell(Player.Pos, Player.Radius + margin, cell)) return true;
            foreach (var n in Npcs) if (!n.IsCamera && CircleTouchesCell(n.Pos, n.Radius + margin, cell)) return true;
            foreach (var b in Bodies) if (!b.Hidden && CircleTouchesCell(b.Pos, 0.3f, cell)) return true;
            return false;
        }

        private static bool CircleTouchesCell(Vec2 p, float r, Int2 c)
        {
            float cx = MathUtil.Clamp(p.x, c.x, c.x + 1), cy = MathUtil.Clamp(p.y, c.y, c.y + 1);
            float dx = p.x - cx, dy = p.y - cy;
            return dx * dx + dy * dy < r * r;
        }

        public void OpenDoor(DoorState d, Actor opener, bool npc)
        {
            if (d.Open) return;
            d.Open = true;
            if (npc && d.Locked) d.AutoClose = 2.2f;
            // Double doors open together.
            var step = d.Horizontal ? new Int2(1, 0) : new Int2(0, 1);
            foreach (var nb in new[] { d.Cell + step, d.Cell - step })
            {
                var other = World.DoorAt(nb.x, nb.y);
                if (other == null || other.Open || other.Type != d.Type) continue;
                if (other.Locked && !npc && d.Locked) continue;
                other.Open = true;
                if (npc && other.Locked) other.AutoClose = d.AutoClose;
                if (!npc && d.Type != DoorType.Normal) other.Locked = false;
            }
            Emit(EvType.DoorOpen, d.Cell.Center, null, d.Index, opener?.Id ?? -1);
            LightsDirty = true;
            if (!npc)
                EmitNoise(d.Cell.Center, Player.Crouched ? 1.8f : 3.2f, NoiseKind.Door, opener);
        }

        public void CloseDoor(DoorState d)
        {
            if (!d.Open || AnyActorInCell(d.Cell)) return;
            d.Open = false;
            Emit(EvType.DoorClose, d.Cell.Center, null, d.Index);
            LightsDirty = true;
        }

        // ------------------------------------------------------------------ alert level

        private void UpdateAlert(float dt)
        {
            bool combat = false, searching = false, suspicious = false;
            foreach (var n in Npcs)
            {
                if (n.Down || n.IsCamera) continue;
                if ((n.State == AIState.Attack || n.State == AIState.Chase) && n.LastSeenAgo < 12f) combat = true;
                else if (n.State == AIState.Search || n.State == AIState.Panic || n.State == AIState.Flee) searching = true;
                else if (n.State == AIState.Suspicious || n.State == AIState.Investigate) suspicious = true;
            }
            var prev = Alert;
            if (combat) { Alert = AlertLevel.Combat; AlertTimer = 25f; }
            else if (Alert == AlertLevel.Combat)
            {
                AlertTimer -= dt;
                if (AlertTimer <= 0f) { Alert = AlertLevel.Alarmed; AlertTimer = 45f; }
            }
            else if (Alert == AlertLevel.Alarmed)
            {
                AlertTimer -= dt;
                if (AlertTimer <= 0f && !searching) Alert = suspicious ? AlertLevel.Suspicious : AlertLevel.Calm;
            }
            else if (searching) { Alert = AlertLevel.Alarmed; AlertTimer = 30f; }
            else Alert = suspicious ? AlertLevel.Suspicious : AlertLevel.Calm;

            if (Alert != prev) Emit(EvType.Alert, Player.Pos, Alert.ToString(), (int)Alert);
        }

        public void RaiseAlarm(float duration = 45f)
        {
            if (Alert < AlertLevel.Alarmed)
            {
                Alert = AlertLevel.Alarmed;
                Emit(EvType.Alert, Player.Pos, Alert.ToString(), (int)Alert);
            }
            AlertTimer = Math.Max(AlertTimer, duration);
            foreach (var n in Npcs) n.Awareness = Math.Min(1.6f, n.Awareness + 0.15f);
        }

        // ------------------------------------------------------------------ objectives

        public bool IsObjectiveDone(int i) => _objectiveDone[i];

        public IEnumerable<(ObjectiveDef def, bool done)> Objectives()
        {
            for (int i = 0; i < Mission.Objectives.Count; i++) yield return (Mission.Objectives[i], _objectiveDone[i]);
        }

        /// <summary>The objective the HUD should highlight.</summary>
        public ObjectiveDef CurrentObjective
        {
            get
            {
                for (int i = 0; i < Mission.Objectives.Count; i++)
                    if (!_objectiveDone[i] && !Mission.Objectives[i].Optional && (Mission.Objectives[i].Type != ObjectiveType.Extract || RequiredDone))
                        return Mission.Objectives[i];
                return Mission.Objectives.LastOrDefault();
            }
        }

        public bool RequiredDone
        {
            get
            {
                for (int i = 0; i < Mission.Objectives.Count; i++)
                {
                    var o = Mission.Objectives[i];
                    if (!o.Optional && o.Type != ObjectiveType.Extract && !_objectiveDone[i]) return false;
                }
                return true;
            }
        }

        private void UpdateObjectives()
        {
            for (int i = 0; i < Mission.Objectives.Count; i++)
            {
                if (_objectiveDone[i]) continue;
                var o = Mission.Objectives[i];
                bool done = false;
                switch (o.Type)
                {
                    case ObjectiveType.Eliminate:
                        done = o.TargetIds.All(id => { var t = FindNpc(id); return t == null || t.Down; });
                        break;
                    case ObjectiveType.Retrieve:
                        done = Player.Inventory.Intel.Contains(o.ItemId)
                               || (o.ItemId.StartsWith("keycard_") && Player.Inventory.Keys.Contains(o.ItemId.Substring(8)));
                        break;
                    case ObjectiveType.Download:
                        done = Interactables.Any(t => t.Kind == InteractKind.DownloadTerminal && t.Key == o.ItemId && t.Used);
                        break;
                    case ObjectiveType.Reach:
                        done = Zones.Any(z => z.Name == o.ZoneName && z.Rect.Contains(Player.Pos));
                        break;
                    case ObjectiveType.Extract:
                        break; // handled when the player enters an extraction zone
                }
                if (done)
                {
                    _objectiveDone[i] = true;
                    Emit(EvType.Objective, Player.Pos, o.Text, i);
                    if (RequiredDone && CurrentObjective != null && CurrentObjective.Type == ObjectiveType.Extract)
                        Message("All objectives complete - get to an extraction point");
                }
            }

            foreach (var n in Npcs)
                if (n.IsTarget && n.Escaped && !n.Down)
                {
                    Fail($"{n.DisplayName} escaped");
                    return;
                }

            // Extraction
            if (State == MissionState.Playing)
                foreach (var ez in Extracts)
                {
                    if (!ez.Rect.Contains(Player.Pos)) continue;
                    if (RequiredDone)
                    {
                        for (int i = 0; i < Mission.Objectives.Count; i++)
                            if (Mission.Objectives[i].Type == ObjectiveType.Extract) _objectiveDone[i] = true;
                        Complete();
                    }
                    else if (_extractMsgCooldown <= 0f)
                    {
                        Message("Objectives incomplete - extraction unavailable");
                        _extractMsgCooldown = 4f;
                    }
                    break;
                }
        }

        public void Fail(string reason)
        {
            if (State != MissionState.Playing || DebugNoFail) return;
            State = MissionState.Failed;
            FailReason = reason;
            Emit(EvType.MissionFailed, Player.Pos, reason);
        }

        private void Complete()
        {
            if (State != MissionState.Playing) return;
            State = MissionState.Complete;
            Emit(EvType.MissionComplete, Player.Pos, Mission.Name);
        }

        public void Die()
        {
            if (State != MissionState.Playing) return;
            Player.Alive = false;
            State = MissionState.Dead;
            FailReason = "You were killed";
            Emit(EvType.PlayerDied, Player.Pos);
        }

        // ------------------------------------------------------------------ results

        public MissionResult BuildResult()
        {
            if (_result != null) return _result;
            var r = new MissionResult
            {
                MissionId = Mission.Id,
                Success = State == MissionState.Complete,
                FailReason = FailReason,
                Time = Time,
                Kills = Kills,
                TargetsKilled = TargetsKilled,
                CiviliansKilled = CiviliansKilled,
                NonTargetKills = NonTargetKills,
                Subdued = Subdued,
                BodiesFound = BodiesFound,
                Spotted = Spotted,
                CashFound = Player.Inventory.CashFound,
                MedkitsLeft = Player.Inventory.Medkits,
            };
            // Ammo left = reserve + rounds still in magazines.
            foreach (var kv in Player.Inventory.Reserve) r.AmmoLeft[kv.Key] = kv.Value;
            foreach (var w in Player.Inventory.Slots)
                if (w != null && w.Def.IsGun)
                {
                    r.AmmoLeft.TryGetValue(w.Def.Ammo, out int have);
                    r.AmmoLeft[w.Def.Ammo] = have + w.Mag;
                }

            if (r.Success)
            {
                r.BaseReward = Mission.Reward;
                for (int i = 0; i < Mission.Objectives.Count; i++)
                    if (_objectiveDone[i])
                    {
                        r.ObjectivesCompleted.Add(Mission.Objectives[i].Id);
                        if (Mission.Objectives[i].Optional) r.ObjectiveBonus += Mission.Objectives[i].Bonus;
                    }
                foreach (var c in Mission.Challenges)
                {
                    bool ok;
                    switch (c.Type)
                    {
                        case ChallengeType.SilentAssassin: ok = !Spotted; break;
                        case ChallengeType.NoCivilianCasualties: ok = CiviliansKilled == 0; break;
                        case ChallengeType.Professional: ok = NonTargetKills == 0 && CiviliansKilled == 0; break;
                        case ChallengeType.NoBodiesFound: ok = BodiesFound == 0; break;
                        case ChallengeType.Speed: ok = Time <= c.ParTime; break;
                        default: ok = false; break;
                    }
                    if (!ok) continue;
                    r.ChallengesCompleted.Add(c);
                    r.ChallengeBonus += c.Bonus;
                }
                r.Penalty = CiviliansKilled * 750;
                r.Total = Math.Max(0, r.BaseReward + r.ObjectiveBonus + r.ChallengeBonus + r.CashFound - r.Penalty);
            }
            r.Rating = Rate(r);
            _result = r;
            return r;
        }

        private static string Rate(MissionResult r)
        {
            if (!r.Success) return "Contract Failed";
            if (!r.Spotted && r.NonTargetKills == 0 && r.CiviliansKilled == 0 && r.BodiesFound == 0) return "Silent Assassin";
            if (!r.Spotted) return "Shadow";
            if (r.CiviliansKilled > 0) return "Butcher";
            if (r.NonTargetKills <= 3) return "Professional";
            return "Mercenary";
        }
    }
}
