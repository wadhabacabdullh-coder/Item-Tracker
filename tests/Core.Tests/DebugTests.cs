using System.Linq;
using NUnit.Framework;
using ShadowContract.Core;
namespace ShadowContract.Tests
{
    public class DebugTests
    {
        [Test, Explicit]
        public void DebugChaos([Range(1, 25)] int seed, [Values("mansion_host", "facility_voss", "office_cfo", "mansion_ledger", "facility_prototype", "office_breach")] string mid)
        {
            var mission = MissionCatalog.Get(mid);
            var map = TestMaps.Load(mission.MapId);
            var progress = new ProgressData { Money = 0 };
            progress.OwnedWeapons.AddRange(new[] { "smg", "shotgun", "throwing_knives", "combat_knife" });
            progress.Loadout = new[] { "combat_knife", "pistol", "shotgun", "throwing_knives", "coin" };
            progress.Ammo[AmmoType.Shells] = 30; progress.Ammo[AmmoType.Knives] = 4;
            var s = new GameSession(map, mission, Difficulty.Hard, progress.BuildLoadout(), seed: seed);
            s.Player.GodMode = true; s.DebugNoFail = true;
            var rng = new Rng(seed); var inp = new PlayerInput(); var bot = new Bot(s); Vec2 wander = s.Player.Pos;
            
            for (float t = 0; t < 150f && s.State == MissionState.Playing; t += GameSession.Tick)
            {
                inp.ClearEdges();
                if (rng.Chance(0.01f)) { var q = s.Npcs[rng.Range(0, s.Npcs.Count)]; wander = q.Pos; }
                bot.MoveTowards(wander, inp, GameSession.Tick);
                inp.Aim = s.Player.Pos + Vec2.FromAngle(rng.Range(0f, 6.28f)) * 3f;
                inp.FireHeld = rng.Chance(0.3f); inp.FirePressed = inp.FireHeld && rng.Chance(0.3f);
                inp.SprintHeld = rng.Chance(0.5f); inp.CrouchPressed = rng.Chance(0.005f);
                inp.InteractPressed = rng.Chance(0.02f); inp.InteractHeld = rng.Chance(0.5f);
                inp.ReloadPressed = rng.Chance(0.01f); inp.AltPressed = rng.Chance(0.01f);
                if (rng.Chance(0.01f)) inp.SelectSlot = rng.Range(0, 5);
                var prevs = s.Npcs.Select(x => (x.Pos, x.State)).ToList();
                s.Update(GameSession.Tick, inp);
                int k = s.Npcs.FindIndex(x => !x.Down && !x.IsCamera && s.World.CircleOverlapsSolid(x.Pos, x.Radius * 0.7f, MoverKind.Npc));
                if (k >= 0)
                {
                    var n = s.Npcs[k];
                    TestContext.WriteLine($"seed {seed} t={t:0.00} {n.Key} prev={prevs[k].Pos} {prevs[k].State} now={n.Pos} {n.State} player={s.Player.Pos}");
                    foreach (var e in s.Events) TestContext.WriteLine("  " + e);
                    var c = Int2.FromWorld(n.Pos);
                    var d = s.World.DoorAt(c.x, c.y);
                    TestContext.WriteLine($"tile {s.World.TileAt(c.x,c.y).Kind} door {(d==null?"-":d.Open+" auto "+d.AutoClose)}");
                    Assert.Fail("overlap");
                }
            }
        }
    }
}
namespace ShadowContract.Tests
{
    public class DebugSpot
    {
        [Test, Explicit]
        public void Trace()
        {
            var map = MapData.Parse("@meta\nid: test\nambient: 0.9\n@tiles\n################\n#..............#\n#..............#\n#..............#\n#..............#\n#.C............#\n#..............#\n################\n@entities\n@mission t\nspawn at=2,3\nguard id=g at=8,3 facing=180\n");
            var m = new MissionDef { Id = "t", Objectives = { new ObjectiveDef { Type = ObjectiveType.Extract } } };
            var s = new GameSession(map, m, Difficulty.Normal, new LoadoutConfig());
            var inp = new PlayerInput { Aim = new Vec2(5, 5) };
            var g = s.FindNpc("g");
            for (int i = 0; i < 240; i++)
            {
                s.Update(GameSession.Tick, inp);
                if (i % 12 == 0) TestContext.WriteLine($"{i * GameSession.Tick:0.0}s {g.State} susp {g.Suspicion:0.00} sees {g.SeesPlayer} facing {g.Facing:0.0} pos {g.Pos} light {s.World.LightAt(s.Player.Pos)}");
            }
        }
    }
}
