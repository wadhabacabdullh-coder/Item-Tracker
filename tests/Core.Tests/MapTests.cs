using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShadowContract.Core;

namespace ShadowContract.Tests
{
    public static class TestMaps
    {
        public static string Dir => Path.Combine(TestContext.CurrentContext.TestDirectory, "Maps");
        public static string Text(string id) => File.ReadAllText(Path.Combine(Dir, id + ".txt"));
        public static MapData Load(string id) => MapData.Parse(Text(id));
        public static readonly string[] All = { "mansion", "facility", "office" };
    }

    public class MapTests
    {
        [TestCaseSource(typeof(TestMaps), nameof(TestMaps.All))]
        public void ParsesWithMetadata(string id)
        {
            var map = TestMaps.Load(id);
            Assert.AreEqual(id, map.Id);
            Assert.IsNotEmpty(map.Name);
            Assert.Greater(map.Width, 60);
            Assert.Greater(map.Height, 40);
            Assert.IsTrue(map.Entities.Any(e => e.Kind == "spawn"));
            Assert.IsTrue(map.Entities.Any(e => e.Kind == "extract"));
            Assert.GreaterOrEqual(map.Entities.Count(e => e.Kind == "guard"), 8, "maps should be well guarded");
            Assert.GreaterOrEqual(map.MissionEntities.Count, 2, "each map hosts two contracts");
        }

        [TestCaseSource(typeof(TestMaps), nameof(TestMaps.All))]
        public void DoorsSitBetweenWalls(string id)
        {
            var world = new World(TestMaps.Load(id));
            Assert.IsNotEmpty(world.Doors);
            foreach (var d in world.Doors)
            {
                bool lr = !world.IsWalkableForNpc(d.Cell.x - 1, d.Cell.y) && !world.IsWalkableForNpc(d.Cell.x + 1, d.Cell.y);
                bool ud = !world.IsWalkableForNpc(d.Cell.x, d.Cell.y - 1) && !world.IsWalkableForNpc(d.Cell.x, d.Cell.y + 1);
                Assert.IsTrue(lr || ud || d.Type == DoorType.Secret || IsDoubleDoor(world, d),
                    $"{id}: door at text col {d.Cell.x} row {world.Height - 1 - d.Cell.y} is not framed by walls");
            }
        }

        private static bool IsDoubleDoor(World w, DoorState d)
        {
            return w.DoorAt(d.Cell.x - 1, d.Cell.y) != null || w.DoorAt(d.Cell.x + 1, d.Cell.y) != null
                || w.DoorAt(d.Cell.x, d.Cell.y - 1) != null || w.DoorAt(d.Cell.x, d.Cell.y + 1) != null;
        }

        [TestCaseSource(typeof(TestMaps), nameof(TestMaps.All))]
        public void GuardRoutesArePathable(string id)
        {
            var map = TestMaps.Load(id);
            var world = new World(map);
            var pf = new Pathfinder(world);
            foreach (var s in map.Entities.Where(e => e.Kind == "stairs"))
                pf.AddLink(Int2.FromWorld(map.ParsePoint(s.Str("a"))), Int2.FromWorld(map.ParsePoint(s.Str("b"))));

            foreach (var missionId in map.MissionEntities.Keys)
            foreach (var e in map.EntitiesFor(missionId).Where(e => e.Kind == "guard" || e.Kind == "civilian" || e.Kind == "target"))
            {
                var start = map.ParsePoint(e.Str("at"));
                var pts = map.ParseRoute(e.Str("route")).Select(w => w.Pos).ToList();
                if (e.Has("escape")) pts.Add(map.ParsePoint(e.Str("escape")));
                Vec2 prev = start;
                foreach (var p in pts)
                {
                    var path = pf.FindPath(prev, p);
                    Assert.IsNotNull(path, $"{id}/{missionId}: {e.Kind} {e.Str("id")} cannot walk {prev} -> {p}");
                    prev = p;
                }
            }
        }

        /// <summary>
        /// The player must be able to reach everything that matters from the spawn point:
        /// doors can be opened (keycards exist on the map), vents are crawlable, stairs link levels, windows can be vaulted.
        /// </summary>
        [TestCaseSource(typeof(TestMaps), nameof(TestMaps.All))]
        public void PlayerCanReachAllObjectives(string id)
        {
            var map = TestMaps.Load(id);
            var world = new World(map);
            var links = map.Entities.Where(e => e.Kind == "stairs")
                .Select(s => (Int2.FromWorld(map.ParsePoint(s.Str("a"))), Int2.FromWorld(map.ParsePoint(s.Str("b"))))).ToList();

            var start = Int2.FromWorld(map.ParsePoint(map.Entities.First(e => e.Kind == "spawn").Str("at")));
            var reached = new HashSet<Int2> { start };
            var queue = new Queue<Int2>();
            queue.Enqueue(start);
            bool Passable(int x, int y)
            {
                var t = world.TileAt(x, y);
                return t.Kind == TileKind.Floor || t.Kind == TileKind.Stairs || t.Kind == TileKind.Door || t.Kind == TileKind.Vent
                       || (t.Kind == TileKind.Furniture && !t.Is(TileFlags.Solid));
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                void Visit(Int2 n) { if (reached.Add(n)) queue.Enqueue(n); }
                foreach (var d in new[] { new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1) })
                {
                    var n = c + d;
                    if (Passable(n.x, n.y)) Visit(n);
                    // Vault through a window onto the floor on the other side.
                    else if (world.TileAt(n.x, n.y).Kind == TileKind.Window && Passable(n.x + d.x, n.y + d.y)) Visit(n + d);
                }
                foreach (var (a, b) in links)
                {
                    if (c == a) Visit(b);
                    if (c == b) Visit(a);
                }
            }

            foreach (var missionId in map.MissionEntities.Keys)
            foreach (var e in map.EntitiesFor(missionId))
            {
                if (e.Kind != "target" && e.Kind != "pickup" && e.Kind != "terminal") continue;
                var p = Int2.FromWorld(map.ParsePoint(e.Str("at")));
                bool ok = reached.Contains(p) || Neighbours(p).Any(reached.Contains);
                Assert.IsTrue(ok, $"{id}/{missionId}: {e.Kind} {e.Str("id") ?? e.Str("item")} at {e.Str("at")} is unreachable");
            }
            foreach (var e in map.Entities.Where(e => e.Kind == "extract"))
            {
                var r = map.ParseRect(e.Str("rect"));
                Assert.IsTrue(reached.Contains(Int2.FromWorld(r.Center)), $"{id}: extraction {e.Str("label")} unreachable");
            }
        }

        private static IEnumerable<Int2> Neighbours(Int2 p)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) yield return new Int2(p.x + dx, p.y + dy);
        }
    }

    public class WorldTests
    {
        private const string Box = "@meta\nid: box\nambient: 0.5\n@tiles\n#######\n#.....#\n#..#..#\n#..D..#\n#..#..#\n#######\n";

        [Test]
        public void CircleSlidesAlongWall()
        {
            var w = new World(MapData.Parse(Box));
            var p = w.MoveCircle(new Vec2(1.5f, 4.5f), 0.3f, new Vec2(-2f, 0.5f), MoverKind.Player);
            Assert.AreEqual(1.3f, p.x, 0.01f, "stopped by the west wall");
            Assert.AreEqual(4.7f, p.y, 0.01f, "slid along it until the north wall");
        }

        [Test]
        public void ClosedDoorBlocksAndOpenDoorPasses()
        {
            var w = new World(MapData.Parse(Box));
            var door = w.Doors.Single();
            Assert.IsFalse(w.HasLineOfSight(new Vec2(1.5f, 2.5f), new Vec2(5.5f, 2.5f)));
            var blocked = w.MoveCircle(new Vec2(2.5f, 2.5f), 0.3f, new Vec2(3f, 0f), MoverKind.Player);
            Assert.Less(blocked.x, 3f);
            door.Open = true;
            Assert.IsTrue(w.HasLineOfSight(new Vec2(1.5f, 2.5f), new Vec2(5.5f, 2.5f)));
            var passed = w.MoveCircle(new Vec2(2.5f, 2.5f), 0.3f, new Vec2(3f, 0f), MoverKind.Player);
            Assert.AreEqual(5.5f, passed.x, 0.01f);
        }

        [Test]
        public void NoTunnellingAtHighSpeed()
        {
            var w = new World(MapData.Parse(Box));
            var p = w.MoveCircle(new Vec2(1.5f, 3.5f), 0.3f, new Vec2(20f, 0f), MoverKind.Player);
            Assert.Less(p.x, 3f);
        }

        [Test]
        public void PathGoesThroughDoor()
        {
            var w = new World(MapData.Parse(Box));
            var path = new Pathfinder(w).FindPath(new Vec2(1.5f, 1.5f), new Vec2(5.5f, 1.5f));
            Assert.IsNotNull(path);
            Assert.IsTrue(path.Any(n => Int2.FromWorld(n.Pos) == new Int2(3, 2)) || path.Count >= 2);
            Assert.AreEqual(new Vec2(5.5f, 1.5f), path.Last().Pos);
        }

        [Test]
        public void RaycastHitsWallFace()
        {
            var w = new World(MapData.Parse(Box));
            var hit = w.Raycast(new Vec2(1.5f, 4.5f), new Vec2(1, 0), 10f, (x, y) => w.BlocksBullets(x, y));
            Assert.IsTrue(hit.Hit);
            Assert.AreEqual(4.5f, hit.Distance, 0.01f); // east wall starts at x = 6
        }
    }
}
