using System;
using System.Collections.Generic;

namespace ShadowContract.Core
{
    public struct PathNode
    {
        public Vec2 Pos;
        public bool Teleport; // arrive here instantly (stairs link)
        public PathNode(Vec2 p, bool teleport = false) { Pos = p; Teleport = teleport; }
    }

    /// <summary>
    /// Grid A* with 8-way movement (no corner cutting), link edges (stairs) and string-pulled output.
    /// Buffers are reused between searches so pathfinding does not allocate per call beyond the result list.
    /// </summary>
    public sealed class Pathfinder
    {
        private readonly World _world;
        private readonly int _w, _h;
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly bool[] _viaLink;
        private int _search;
        private readonly MinHeap _open;
        private readonly Dictionary<int, List<int>> _links = new Dictionary<int, List<int>>();

        public int MaxExpansions = 12000;

        public Pathfinder(World world)
        {
            _world = world;
            _w = world.Width;
            _h = world.Height;
            int n = _w * _h;
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _viaLink = new bool[n];
            _open = new MinHeap(n);
        }

        public void AddLink(Int2 a, Int2 b)
        {
            Add(a, b);
            Add(b, a);
        }

        private void Add(Int2 a, Int2 b)
        {
            int ia = a.y * _w + a.x, ib = b.y * _w + b.x;
            if (!_links.TryGetValue(ia, out var list)) _links[ia] = list = new List<int>();
            list.Add(ib);
        }

        public bool TryGetLink(Int2 c, out Int2 other)
        {
            other = default;
            if (!_links.TryGetValue(c.y * _w + c.x, out var list) || list.Count == 0) return false;
            other = new Int2(list[0] % _w, list[0] / _w);
            return true;
        }

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Finds a path. Returns null if unreachable.</summary>
        public List<PathNode> FindPath(Vec2 from, Vec2 to, float agentRadius = 0.3f)
        {
            var start = _world.NearestWalkable(Int2.FromWorld(from), 2);
            var goal = _world.NearestWalkable(Int2.FromWorld(to), 3);
            if (!_world.IsWalkableForNpc(start.x, start.y) || !_world.IsWalkableForNpc(goal.x, goal.y)) return null;

            _search++;
            if (_search == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); _search = 1; }
            _open.Clear();
            int si = start.y * _w + start.x, gi = goal.y * _w + goal.x;
            Touch(si);
            _g[si] = 0f;
            _parent[si] = -1;
            _open.Push(si, Heuristic(start, goal));
            int expansions = 0;
            bool found = false;

            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (cur == gi) { found = true; break; }
                if (++expansions > MaxExpansions) break;
                int cx = cur % _w, cy = cur / _w;
                float gc = _g[cur];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (!_world.IsWalkableForNpc(nx, ny)) continue;
                    if (d >= 4 && (!_world.IsWalkableForNpc(cx + Dx[d], cy) || !_world.IsWalkableForNpc(cx, cy + Dy[d]))) continue;
                    float cost = d >= 4 ? 1.4142f : 1f;
                    if (_world.TileAt(nx, ny).Kind == TileKind.Door) cost += 0.6f;
                    Relax(cur, ny * _w + nx, gc + cost, goal, false);
                }

                if (_links.TryGetValue(cur, out var links))
                    foreach (int li in links) Relax(cur, li, gc + 2f, goal, true);
            }

            if (!found) return null;

            // Reconstruct.
            var cells = new List<int>();
            for (int c = gi; c != -1; c = _parent[c]) cells.Add(c);
            cells.Reverse();

            var raw = new List<PathNode>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                raw.Add(new PathNode(new Vec2(c % _w + 0.5f, c / _w + 0.5f), i > 0 && _viaLink[c]));
            }
            if (raw.Count > 0 && Vec2.Distance(to, raw[raw.Count - 1].Pos) < 0.75f && _world.IsWalkableForNpc(Int2.FromWorld(to).x, Int2.FromWorld(to).y))
                raw[raw.Count - 1] = new PathNode(to, raw[raw.Count - 1].Teleport);
            return Smooth(from, raw, agentRadius);
        }

        private void Relax(int from, int to, float g, Int2 goal, bool viaLink)
        {
            if (_stamp[to] != _search) { Touch(to); _g[to] = float.MaxValue; }
            if (g >= _g[to]) return;
            _g[to] = g;
            _parent[to] = from;
            _viaLink[to] = viaLink;
            _open.PushOrDecrease(to, g + Heuristic(new Int2(to % _w, to / _w), goal));
        }

        private void Touch(int i) { _stamp[i] = _search; _viaLink[i] = false; }

        private static float Heuristic(Int2 a, Int2 b)
        {
            int dx = Math.Abs(a.x - b.x), dy = Math.Abs(a.y - b.y);
            return (dx + dy) + (1.4142f - 2f) * Math.Min(dx, dy);
        }

        /// <summary>String-pulling: skip intermediate nodes while a straight walk is clear. Never skips across teleports.</summary>
        private List<PathNode> Smooth(Vec2 from, List<PathNode> raw, float radius)
        {
            var result = new List<PathNode>();
            Vec2 anchor = from;
            int i = 0;
            while (i < raw.Count)
            {
                int best = i;
                for (int j = i + 1; j < raw.Count; j++)
                {
                    if (raw[j].Teleport) break;
                    if (_world.HasClearWalk(anchor, raw[j].Pos, radius + 0.05f)) best = j;
                    else break;
                }
                // Also stop at the node before a teleport so the NPC walks onto the stairs first.
                result.Add(raw[best]);
                anchor = raw[best].Pos;
                i = best + 1;
            }
            // Drop a first node that is basically where we are standing.
            if (result.Count > 1 && Vec2.Distance(result[0].Pos, from) < 0.2f && !result[0].Teleport) result.RemoveAt(0);
            return result;
        }

        // Small indexed binary heap keyed by cell index.
        private sealed class MinHeap
        {
            private readonly int[] _heap;
            private readonly float[] _key;
            private readonly int[] _pos; // position in heap + 1, 0 = not in heap
            public int Count;

            public MinHeap(int capacity)
            {
                _heap = new int[capacity];
                _key = new float[capacity];
                _pos = new int[capacity];
            }

            public void Clear()
            {
                for (int i = 0; i < Count; i++) _pos[_heap[i]] = 0;
                Count = 0;
            }

            public void Push(int item, float key) => PushOrDecrease(item, key);

            public void PushOrDecrease(int item, float key)
            {
                int p = _pos[item] - 1;
                if (p < 0)
                {
                    p = Count++;
                    _heap[p] = item;
                    _pos[item] = p + 1;
                    _key[item] = key;
                    Up(p);
                }
                else if (key < _key[item])
                {
                    _key[item] = key;
                    Up(p);
                }
            }

            public int Pop()
            {
                int top = _heap[0];
                _pos[top] = 0;
                Count--;
                if (Count > 0)
                {
                    _heap[0] = _heap[Count];
                    _pos[_heap[0]] = 1;
                    Down(0);
                }
                return top;
            }

            private void Up(int i)
            {
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_key[_heap[parent]] <= _key[_heap[i]]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            private void Down(int i)
            {
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, s = i;
                    if (l < Count && _key[_heap[l]] < _key[_heap[s]]) s = l;
                    if (r < Count && _key[_heap[r]] < _key[_heap[s]]) s = r;
                    if (s == i) break;
                    Swap(i, s);
                    i = s;
                }
            }

            private void Swap(int a, int b)
            {
                int t = _heap[a];
                _heap[a] = _heap[b];
                _heap[b] = t;
                _pos[_heap[a]] = a + 1;
                _pos[_heap[b]] = b + 1;
            }
        }
    }
}
