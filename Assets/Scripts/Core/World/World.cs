using System;
using System.Collections.Generic;

namespace ShadowContract.Core
{
    public enum MoverKind { Npc, Player, PlayerCrouched }

    public sealed class DoorState
    {
        public int Index;
        public Int2 Cell;
        public DoorType Type;
        public bool Open;
        public bool Locked;          // player needs the matching keycard
        public bool Horizontal;      // walls to the left & right: door panel lies along X
        public float AutoClose;      // > 0: NPC opened it, closes when timer runs out and doorway is clear
        public float Anim;           // 0 = closed, 1 = open (for the view)

        public string KeyId => Type == DoorType.LockedBlue ? "blue" : Type == DoorType.LockedRed ? "red" : null;
    }

    public struct RayHit
    {
        public bool Hit;
        public float Distance;
        public Vec2 Point;
        public Int2 Cell;
        public Vec2 Normal;
    }

    /// <summary>
    /// Runtime state of a map: doors, broken windows, collision and line-of-sight queries.
    /// All queries are grid based (O(cells touched)) which keeps them cheap enough to run for every NPC.
    /// </summary>
    public sealed class World
    {
        public readonly MapData Map;
        public readonly int Width, Height;
        public readonly List<DoorState> Doors = new List<DoorState>();
        private readonly int[] _doorIndex;     // -1 when no door
        private readonly bool[] _brokenWindow;
        private readonly float[] _light;       // baked per-tile light level 0..1 (see LightMap)

        public World(MapData map)
        {
            Map = map;
            Width = map.Width;
            Height = map.Height;
            _doorIndex = new int[Width * Height];
            _brokenWindow = new bool[Width * Height];
            _light = new float[Width * Height];
            for (int i = 0; i < _doorIndex.Length; i++) _doorIndex[i] = -1;

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var t = map.Get(x, y);
                if (t.Kind != TileKind.Door) continue;
                var type = TileLegend.DoorTypeFromChar(t.Symbol);
                bool wallsLR = IsWallLike(x - 1, y) || IsWallLike(x + 1, y);
                var d = new DoorState
                {
                    Index = Doors.Count,
                    Cell = new Int2(x, y),
                    Type = type,
                    Locked = type == DoorType.LockedBlue || type == DoorType.LockedRed || type == DoorType.Secret,
                    Horizontal = wallsLR,
                };
                _doorIndex[y * Width + x] = d.Index;
                Doors.Add(d);
            }
            for (int i = 0; i < _light.Length; i++) _light[i] = map.Ambient;
        }

        private bool IsWallLike(int x, int y)
        {
            var t = Map.Get(x, y);
            return t.Kind == TileKind.Wall || t.Kind == TileKind.Window || t.Kind == TileKind.Door || t.Kind == TileKind.Void
                   || (t.Kind == TileKind.Furniture && t.Furniture == FurnitureType.Bookshelf);
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public Tile TileAt(int x, int y) => Map.Get(x, y);

        public DoorState DoorAt(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            int i = _doorIndex[y * Width + x];
            return i < 0 ? null : Doors[i];
        }

        public bool IsWindowBroken(int x, int y) => InBounds(x, y) && _brokenWindow[y * Width + x];
        public void BreakWindow(int x, int y) { if (InBounds(x, y)) _brokenWindow[y * Width + x] = true; }

        public float LightAt(int x, int y) => InBounds(x, y) ? _light[y * Width + x] : 0f;
        public float LightAt(Vec2 p) => LightAt((int)Math.Floor(p.x), (int)Math.Floor(p.y));
        public void SetLight(int x, int y, float v) { if (InBounds(x, y)) _light[y * Width + x] = v; }

        // ------------------------------------------------------------------ tile queries

        public bool IsSolid(int x, int y, MoverKind mover)
        {
            if (!InBounds(x, y)) return true;
            var t = Map.Tiles[y * Width + x];
            switch (t.Kind)
            {
                case TileKind.Void:
                case TileKind.Wall:
                case TileKind.Water:
                case TileKind.Window:
                    return true;
                case TileKind.Door:
                    return !Doors[_doorIndex[y * Width + x]].Open;
                case TileKind.Vent:
                    return mover != MoverKind.PlayerCrouched;
                case TileKind.Furniture:
                    return t.Is(TileFlags.Solid);
                default:
                    return false;
            }
        }

        /// <summary>True if this cell stops vision. Low cover only hides crouched targets close behind it.</summary>
        public bool BlocksSight(int x, int y, bool targetCrouched, Vec2 targetPos)
        {
            if (!InBounds(x, y)) return true;
            var t = Map.Tiles[y * Width + x];
            switch (t.Kind)
            {
                case TileKind.Void:
                case TileKind.Wall:
                case TileKind.Vent:
                    return true;
                case TileKind.Door:
                    return !Doors[_doorIndex[y * Width + x]].Open;
                case TileKind.Furniture:
                    if (t.Is(TileFlags.BlocksSight)) return true;
                    if (targetCrouched && t.Is(TileFlags.LowCover))
                    {
                        float dx = x + 0.5f - targetPos.x, dy = y + 0.5f - targetPos.y;
                        return dx * dx + dy * dy < 2.6f * 2.6f;
                    }
                    return false;
                default:
                    return false;
            }
        }

        public bool BlocksBullets(int x, int y)
        {
            if (!InBounds(x, y)) return true;
            var t = Map.Tiles[y * Width + x];
            switch (t.Kind)
            {
                case TileKind.Void:
                case TileKind.Wall:
                case TileKind.Vent:
                    return true;
                case TileKind.Window:
                    return !_brokenWindow[y * Width + x];
                case TileKind.Door:
                    return !Doors[_doorIndex[y * Width + x]].Open;
                case TileKind.Furniture:
                    return t.Is(TileFlags.BlocksBullets);
                default:
                    return false;
            }
        }

        /// <summary>Walkable for pathfinding. NPCs treat closed normal/locked doors as passable (they open them), never secret doors.</summary>
        public bool IsWalkableForNpc(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var t = Map.Tiles[y * Width + x];
            switch (t.Kind)
            {
                case TileKind.Floor:
                case TileKind.Stairs:
                    return true;
                case TileKind.Door:
                    var d = Doors[_doorIndex[y * Width + x]];
                    return d.Type != DoorType.Secret || d.Open;
                case TileKind.Furniture:
                    return !t.Is(TileFlags.Solid);
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// Moves a circle through the grid, sliding along walls. Sub-steps keep fast movement from tunnelling.
        /// </summary>
        public Vec2 MoveCircle(Vec2 pos, float radius, Vec2 delta, MoverKind mover)
        {
            float len = delta.Length;
            if (len < 1e-7f) return pos;
            int steps = Math.Max(1, (int)Math.Ceiling(len / (radius * 0.5f)));
            Vec2 step = delta / steps;
            for (int s = 0; s < steps; s++)
            {
                pos += step;
                pos = ResolveCircle(pos, radius, mover);
            }
            return pos;
        }

        public Vec2 ResolveCircle(Vec2 pos, float radius, MoverKind mover)
        {
            for (int iter = 0; iter < 3; iter++)
            {
                bool moved = false;
                int minX = (int)Math.Floor(pos.x - radius), maxX = (int)Math.Floor(pos.x + radius);
                int minY = (int)Math.Floor(pos.y - radius), maxY = (int)Math.Floor(pos.y + radius);
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!IsSolid(x, y, mover)) continue;
                    float cx = MathUtil.Clamp(pos.x, x, x + 1);
                    float cy = MathUtil.Clamp(pos.y, y, y + 1);
                    float dx = pos.x - cx, dy = pos.y - cy;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= radius * radius) continue;
                    if (d2 > 1e-10f)
                    {
                        float d = (float)Math.Sqrt(d2);
                        float push = radius - d;
                        pos.x += dx / d * push;
                        pos.y += dy / d * push;
                    }
                    else
                    {
                        // Centre is inside the tile: push out along the shallowest axis.
                        float left = pos.x - x, right = x + 1 - pos.x, down = pos.y - y, up = y + 1 - pos.y;
                        float m = Math.Min(Math.Min(left, right), Math.Min(down, up));
                        if (m == left) pos.x = x - radius;
                        else if (m == right) pos.x = x + 1 + radius;
                        else if (m == down) pos.y = y - radius;
                        else pos.y = y + 1 + radius;
                    }
                    moved = true;
                }
                if (!moved) break;
            }
            return pos;
        }

        public bool CircleOverlapsSolid(Vec2 pos, float radius, MoverKind mover)
        {
            int minX = (int)Math.Floor(pos.x - radius), maxX = (int)Math.Floor(pos.x + radius);
            int minY = (int)Math.Floor(pos.y - radius), maxY = (int)Math.Floor(pos.y + radius);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsSolid(x, y, mover)) continue;
                float cx = MathUtil.Clamp(pos.x, x, x + 1), cy = MathUtil.Clamp(pos.y, y, y + 1);
                float dx = pos.x - cx, dy = pos.y - cy;
                if (dx * dx + dy * dy < radius * radius - 1e-5f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ rays

        /// <summary>Grid DDA raycast. <paramref name="blocks"/> decides which cells stop the ray.</summary>
        public RayHit Raycast(Vec2 origin, Vec2 dir, float maxDist, Func<int, int, bool> blocks)
        {
            var hit = new RayHit { Hit = false, Distance = maxDist, Point = origin + dir * maxDist };
            if (dir.SqrLength < 1e-10f) return hit;
            dir = dir.Normalized;
            int x = (int)Math.Floor(origin.x), y = (int)Math.Floor(origin.y);
            int stepX = dir.x > 0 ? 1 : -1, stepY = dir.y > 0 ? 1 : -1;
            float tDeltaX = Math.Abs(dir.x) < 1e-9f ? float.MaxValue : Math.Abs(1f / dir.x);
            float tDeltaY = Math.Abs(dir.y) < 1e-9f ? float.MaxValue : Math.Abs(1f / dir.y);
            float tMaxX = Math.Abs(dir.x) < 1e-9f ? float.MaxValue : (dir.x > 0 ? (x + 1 - origin.x) : (origin.x - x)) * tDeltaX;
            float tMaxY = Math.Abs(dir.y) < 1e-9f ? float.MaxValue : (dir.y > 0 ? (y + 1 - origin.y) : (origin.y - y)) * tDeltaY;
            float t = 0f;
            Vec2 normal = Vec2.Zero;
            int guard = 0;
            while (t <= maxDist && guard++ < 4096)
            {
                if (blocks(x, y) && t > 0f)
                {
                    hit.Hit = true;
                    hit.Distance = t;
                    hit.Point = origin + dir * t;
                    hit.Cell = new Int2(x, y);
                    hit.Normal = normal;
                    return hit;
                }
                if (tMaxX < tMaxY) { t = tMaxX; tMaxX += tDeltaX; x += stepX; normal = new Vec2(-stepX, 0); }
                else { t = tMaxY; tMaxY += tDeltaY; y += stepY; normal = new Vec2(0, -stepY); }
            }
            return hit;
        }

        /// <summary>Line of sight between two points for vision.</summary>
        public bool HasLineOfSight(Vec2 from, Vec2 to, bool targetCrouched = false)
        {
            Vec2 d = to - from;
            float dist = d.Length;
            if (dist < 1e-4f) return true;
            var hit = Raycast(from, d / dist, dist, (x, y) => BlocksSight(x, y, targetCrouched, to));
            if (!hit.Hit) return true;
            // The target's own cell never blocks (e.g. player standing in an open doorway).
            return hit.Cell == Int2.FromWorld(to);
        }

        /// <summary>Clear straight walk for an NPC of the given radius (used for path smoothing).</summary>
        public bool HasClearWalk(Vec2 from, Vec2 to, float radius)
        {
            Vec2 d = to - from;
            float dist = d.Length;
            if (dist < 1e-4f) return true;
            Vec2 n = d / dist;
            Vec2 side = n.Perp * radius;
            foreach (var off in new[] { Vec2.Zero, side, -side })
            {
                var hit = Raycast(from + off, n, dist, (x, y) => !IsWalkableForNpc(x, y));
                if (hit.Hit) return false;
            }
            return true;
        }

        /// <summary>Nearest floor cell to a position (used when an entity is spawned inside geometry).</summary>
        public Int2 NearestWalkable(Int2 c, int maxRadius = 6)
        {
            if (IsWalkableForNpc(c.x, c.y)) return c;
            for (int r = 1; r <= maxRadius; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                if (IsWalkableForNpc(c.x + dx, c.y + dy)) return new Int2(c.x + dx, c.y + dy);
            }
            return c;
        }
    }
}
