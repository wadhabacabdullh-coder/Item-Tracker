using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShadowContract.Core;

namespace ShadowContract.Tests
{
    /// <summary>Drives the player like a (very determined) human would: A* to a goal, then act.</summary>
    public sealed class Bot
    {
        private readonly GameSession _s;
        private List<PathNode> _path;
        private int _idx;
        private Vec2 _goal;
        private float _repath;

        public Bot(GameSession s) { _s = s; }

        public bool MoveTowards(Vec2 goal, PlayerInput inp, float dt, float tolerance = 0.4f)
        {
            var p = _s.Player;
            if (Vec2.Distance(p.Pos, goal) < tolerance) { inp.Move = Vec2.Zero; return true; }
            _repath -= dt;
            if (_path == null || _repath <= 0f || Vec2.Distance(goal, _goal) > 1f)
            {
                _path = _s.Paths.FindPath(p.Pos, goal, 0.34f);
                _idx = 0;
                _goal = goal;
                _repath = 0.5f;
            }
            if (_path == null) { inp.Move = (goal - p.Pos).Normalized; return false; }
            while (_idx < _path.Count && !_path[_idx].Teleport && Vec2.Distance(_path[_idx].Pos, p.Pos) < 0.3f) _idx++;
            if (_idx >= _path.Count) { inp.Move = (goal - p.Pos).Normalized; return false; }
            var node = _path[_idx];
            if (node.Teleport)
            {
                inp.InteractPressed = true; // take the stairs
                _path = null;
                return false;
            }
            inp.Move = (node.Pos - p.Pos).Normalized;
            inp.Aim = node.Pos + inp.Move;
            return false;
        }
    }

    public class SessionTests
    {
        private static MissionDef TestMission => new MissionDef
        {
            Id = "t", MapId = "test", Name = "Test",
            Objectives = { new ObjectiveDef { Id = "x", Type = ObjectiveType.Extract, Text = "Leave" } },
        };

        private static GameSession Room(float ambient, string entities, string extraTiles = null)
        {
            string text = $@"@meta
id: test
name: Test
ambient: {ambient.ToString(System.Globalization.CultureInfo.InvariantCulture)}
@tiles
################
#..............#
#..............#
#..............#
#..............#
#.C............#
#..............#
################
@entities
@mission t
{entities}
";
            var map = MapData.Parse(text);
            return new GameSession(map, TestMission, Difficulty.Normal, new LoadoutConfig
            {
                SlotWeapons = new[] { "knife", "pistol", null, "throwing_knives", "coin" },
                Ammo = { [AmmoType.Pistol] = 36, [AmmoType.Coins] = 3, [AmmoType.Knives] = 3 },
            });
        }

        private static void Run(GameSession s, float seconds, Action<PlayerInput> input = null, Action<GameSession> each = null)
        {
            var inp = new PlayerInput { Aim = s.Player.Pos + new Vec2(1, 0) };
            for (float t = 0; t < seconds; t += GameSession.Tick)
            {
                inp.ClearEdges();
                inp.Move = Vec2.Zero;
                inp.FireHeld = false;
                input?.Invoke(inp);
                s.Update(GameSession.Tick, inp);
                each?.Invoke(s);
                if (s.State != MissionState.Playing) break;
            }
        }

        [Test]
        public void GuardSpotsPlayerInLitRoomAndShoots()
        {
            var s = Room(0.9f, "spawn at=2,3\nguard id=g at=8,3 facing=180");
            bool shot = false;
            Run(s, 4f, each: ss => shot |= ss.Events.Any(e => e.Type == EvType.Shot && e.ActorId != 0));
            var g = s.FindNpc("g");
            Assert.IsTrue(s.Spotted);
            Assert.AreEqual(AIState.Attack, g.State);
            Assert.IsTrue(shot, "guard opened fire");
            Assert.Less(s.Player.Health, s.Player.MaxHealth);
        }

        [Test]
        public void DetectionIsNotInstant()
        {
            var s = Room(0.9f, "spawn at=2,3\nguard id=g at=11,3 facing=180");
            Run(s, 0.25f);
            Assert.IsFalse(s.Spotted, "suspicion builds up over time");
            Assert.Greater(s.FindNpc("g").Suspicion, 0f);
        }

        [Test]
        public void DarknessAndCrouchingKeepYouHidden()
        {
            var s = Room(0.05f, "spawn at=3,3\nguard id=g at=12,3 facing=180");
            s.Player.Crouched = true;
            Run(s, 4f);
            Assert.IsFalse(s.Spotted);
            Assert.Less(s.FindNpc("g").Suspicion, 0.1f);
        }

        [Test]
        public void SneakingBehindIsSilentButSprintingIsHeard()
        {
            var s = Room(0.9f, "spawn at=6,3\nguard id=g at=10,3 facing=0");
            s.Player.Crouched = true;
            Run(s, 2f, inp => { inp.Move = new Vec2(0.5f, 0); inp.Aim = new Vec2(20, 4.5f); });
            Assert.AreNotEqual(AIState.Investigate, s.FindNpc("g").State);
            Assert.IsFalse(s.Spotted);

            var s2 = Room(0.9f, "spawn at=3,3\nguard id=g at=10,3 facing=0");
            Run(s2, 1.2f, inp => { inp.Move = new Vec2(1, 0); inp.SprintHeld = true; inp.Aim = new Vec2(20, 4.5f); });
            var g2 = s2.FindNpc("g");
            Assert.That(g2.State == AIState.Suspicious || g2.State == AIState.Investigate || g2.State == AIState.Attack, $"heard footsteps, state {g2.State}");
        }

        [Test]
        public void SilentTakedownFromBehind()
        {
            var s = Room(0.9f, "spawn at=9,3\nguard id=g at=10,3 facing=0");
            s.Player.Inventory.Select(0);
            Run(s, 0.3f, inp => { inp.Aim = s.FindNpc("g").Pos; inp.FirePressed = true; inp.FireHeld = true; });
            var g = s.FindNpc("g");
            Assert.IsFalse(g.Alive);
            Assert.IsFalse(s.Spotted);
            Assert.AreEqual(1, s.Kills);
            Assert.AreEqual(1, s.Bodies.Count);
        }

        [Test]
        public void CoinDistractsGuard()
        {
            var s = Room(0.2f, "spawn at=2,6\nguard id=g at=12,2 facing=90");
            s.Player.Inventory.Select(4);
            var target = new Vec2(8.5f, 5.5f);
            bool thrown = false;
            Run(s, 3f, inp =>
            {
                inp.Aim = target;
                if (!thrown) { inp.FirePressed = true; inp.FireHeld = true; thrown = true; }
            });
            var g = s.FindNpc("g");
            Assert.That(g.State == AIState.Investigate || g.State == AIState.Search || g.State == AIState.Suspicious, $"state {g.State}");
            Assert.Less(Vec2.Distance(g.InvestigatePos, target), 4f);
        }

        [Test]
        public void GuardFindsBodyAndRaisesAlarm()
        {
            var s = Room(0.9f, "spawn at=2,6\nguard id=a at=5,3 facing=270\nguard id=b at=13,1 route=\"13,1:1|6,3:3\"");
            var a = s.FindNpc("a");
            s.KillNpc(a, new Vec2(1, 0), true);
            s.Player.Pos = new Vec2(1.5f, 6.5f);
            s.Player.Hidden = true; // stay out of the way
            bool found = false;
            Run(s, 15f, each: ss => found |= ss.Events.Any(e => e.Type == EvType.BodyFound));
            Assert.IsTrue(found);
            Assert.GreaterOrEqual(s.Alert, AlertLevel.Alarmed);
            Assert.AreEqual(1, s.BodiesFound);
        }

        [Test]
        public void HidingInClosetMakesYouInvisible()
        {
            var s = Room(0.9f, "spawn at=3,5\nguard id=g at=12,5 facing=180");
            // closet at text col 2 row 5 -> player next to it
            Run(s, GameSession.Tick, inp => inp.InteractPressed = true);
            Assert.IsTrue(s.Player.Hidden, s.Candidate.Prompt ?? "no candidate");
            Run(s, 3f);
            Assert.IsFalse(s.Spotted);
            Run(s, GameSession.Tick, inp => inp.InteractPressed = true);
            Assert.IsFalse(s.Player.Hidden);
        }

        [Test]
        public void GunshotAlertsGuardsBehindWalls()
        {
            var s = Room(0.9f, "spawn at=2,1\nguard id=g at=13,6 facing=0");
            Run(s, 0.2f, inp => { inp.Aim = new Vec2(2.5f, 1f); inp.FirePressed = true; inp.FireHeld = true; });
            var g = s.FindNpc("g");
            Assert.That(g.State == AIState.Investigate || g.State == AIState.Attack, $"state {g.State}");
            Assert.GreaterOrEqual(s.Alert, AlertLevel.Alarmed);
        }

        [Test]
        public void ReloadAndAmmoAccounting()
        {
            var s = Room(0.9f, "spawn at=2,3");
            var inv = s.Player.Inventory;
            var w = inv.CurrentWeapon;
            Assert.AreEqual("pistol", w.Def.Id);
            int start = w.Mag + inv.GetReserve(AmmoType.Pistol);
            int fired = 0;
            Run(s, 3f, inp =>
            {
                inp.Aim = new Vec2(2.5f, 0.5f);
                inp.FirePressed = true;
                inp.FireHeld = true;
            }, ss => fired += ss.Events.Count(e => e.Type == EvType.Shot && e.ActorId == 0));
            Assert.Greater(fired, 5);
            Assert.AreEqual(start - fired, w.Mag + inv.GetReserve(AmmoType.Pistol));
        }

        // ------------------------------------------------------------------ full missions

        /// <summary>Plays a whole contract: sneak to the target, knife it, walk to extraction.</summary>
        [TestCase("mansion_host")]
        [TestCase("facility_voss")]
        [TestCase("office_cfo")]
        public void BotCompletesMission(string missionId)
        {
            var mission = MissionCatalog.Get(missionId);
            var map = TestMaps.Load(mission.MapId);
            var progress = new ProgressData();
            var s = new GameSession(map, mission, Difficulty.Normal, progress.BuildLoadout());
            s.Player.GodMode = true;
            s.Player.Ghost = true;       // this test checks mission flow, not stealth
            var bot = new Bot(s);
            s.Player.Inventory.Select(0);
            // Keys are placed on the map; for the test we assume the player found them.
            s.Player.Inventory.Keys.Add("blue");
            s.Player.Inventory.Keys.Add("red");
            foreach (var d in s.World.Doors) if (d.Type != DoorType.Secret) d.Locked = false;

            var targets = mission.Objectives.Where(o => o.Type == ObjectiveType.Eliminate).SelectMany(o => o.TargetIds).Select(s.FindNpc).ToList();
            var inp = new PlayerInput();
            float t = 0;
            while (t < 900f && s.State == MissionState.Playing)
            {
                inp.ClearEdges();
                inp.FireHeld = false;
                var alive = targets.FirstOrDefault(x => !x.Down);
                if (alive != null)
                {
                    if (Vec2.Distance(s.Player.Pos, alive.Pos) < 0.95f)
                    {
                        inp.Move = Vec2.Zero;
                        inp.Aim = alive.Pos;
                        inp.FireHeld = true;
                        inp.FirePressed = true;
                    }
                    else bot.MoveTowards(alive.Pos, inp, GameSession.Tick, 0.8f);
                }
                else
                {
                    // Pick up any required intel on the way out.
                    var intel = s.Pickups.FirstOrDefault(p => !p.Taken && p.Kind == PickupKind.Intel);
                    var goal = intel != null && mission.Objectives.Any(o => !o.Optional && o.ItemId == intel.ItemId) ? intel.Pos : s.Extracts[0].Rect.Center;
                    bot.MoveTowards(goal, inp, GameSession.Tick, 0.2f);
                }
                s.Update(GameSession.Tick, inp);
                t += GameSession.Tick;
            }
            Assert.AreEqual(MissionState.Complete, s.State, $"{missionId}: state {s.State} after {t:0}s, player at {s.Player.Pos}, objective '{s.CurrentObjective?.Text}'");
            var r = s.BuildResult();
            Assert.IsTrue(r.Success);
            Assert.Greater(r.Total, 0);
            TestContext.WriteLine($"{missionId}: {t:0}s, kills {r.Kills}, spotted {r.Spotted}, rating {r.Rating}, payout {r.Total}");
        }

        /// <summary>Random mayhem on every contract must never throw or push characters into walls.</summary>
        [Test]
        public void ChaosSimulationIsStable([Values("mansion_host", "facility_voss", "office_cfo", "mansion_ledger", "facility_prototype", "office_breach")] string missionId)
        {
            var mission = MissionCatalog.Get(missionId);
            var map = TestMaps.Load(mission.MapId);
            var progress = new ProgressData { Money = 0 };
            progress.OwnedWeapons.AddRange(new[] { "smg", "shotgun", "throwing_knives" });
            progress.Loadout = new[] { "combat_knife", "pistol", "shotgun", "throwing_knives", "coin" };
            progress.OwnedWeapons.Add("combat_knife");
            progress.Ammo[AmmoType.Shells] = 30;
            progress.Ammo[AmmoType.Knives] = 4;
            var s = new GameSession(map, mission, Difficulty.Hard, progress.BuildLoadout(), seed: missionId.Length * 977 + missionId[0]);
            s.Player.GodMode = true;
            s.DebugNoFail = true;
            var rng = new Rng(7);
            var inp = new PlayerInput();
            var bot = new Bot(s);
            Vec2 wander = s.Player.Pos;
            for (float t = 0; t < 150f && s.State == MissionState.Playing; t += GameSession.Tick)
            {
                inp.ClearEdges();
                if (rng.Chance(0.01f))
                {
                    var n = s.Npcs[rng.Range(0, s.Npcs.Count)];
                    wander = n.Pos;
                }
                bot.MoveTowards(wander, inp, GameSession.Tick);
                inp.Aim = s.Player.Pos + Vec2.FromAngle(rng.Range(0f, 6.28f)) * 3f;
                inp.FireHeld = rng.Chance(0.3f);
                inp.FirePressed = inp.FireHeld && rng.Chance(0.3f);
                inp.SprintHeld = rng.Chance(0.5f);
                inp.CrouchPressed = rng.Chance(0.005f);
                inp.InteractPressed = rng.Chance(0.02f);
                inp.InteractHeld = rng.Chance(0.5f);
                inp.ReloadPressed = rng.Chance(0.01f);
                inp.AltPressed = rng.Chance(0.01f);
                if (rng.Chance(0.01f)) inp.SelectSlot = rng.Range(0, 5);
                s.Update(GameSession.Tick, inp);

                foreach (var n in s.Npcs)
                    if (!n.Down && !n.IsCamera)
                        Assert.IsFalse(s.World.CircleOverlapsSolid(n.Pos, n.Radius * 0.7f, MoverKind.Npc), $"{n.Key} inside geometry at {n.Pos} ({n.State})");
                if (!s.Player.Hidden)
                    Assert.IsFalse(s.World.CircleOverlapsSolid(s.Player.Pos, s.Player.Radius * 0.7f, MoverKind.PlayerCrouched), $"player inside geometry at {s.Player.Pos}");
            }
            TestContext.WriteLine($"{missionId}: kills {s.Kills}, alert {s.Alert}, state {s.State}");
        }
    }
}
