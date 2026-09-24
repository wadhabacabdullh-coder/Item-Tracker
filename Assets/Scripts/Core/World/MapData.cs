using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ShadowContract.Core
{
    /// <summary>A generic entity line from a map file: "kind key=value key=\"quoted value\" ...".</summary>
    public sealed class EntityDef
    {
        public string Kind;
        public readonly Dictionary<string, string> Props = new Dictionary<string, string>();
        public int Line;

        public bool Has(string key) => Props.ContainsKey(key);
        public string Str(string key, string fallback = null) => Props.TryGetValue(key, out var v) ? v : fallback;

        public float Float(string key, float fallback = 0f) =>
            Props.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

        public int Int(string key, int fallback = 0) =>
            Props.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

        public bool Bool(string key, bool fallback = false) =>
            Props.TryGetValue(key, out var v) ? (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "yes") : fallback;

        public override string ToString() => $"{Kind} (line {Line})";
    }

    /// <summary>A point on a route plus an optional wait time / facing / activity label.</summary>
    public struct Waypoint
    {
        public Vec2 Pos;
        public float Wait;
        public float LookAngle; // NaN = keep current facing
        public string Activity;
    }

    /// <summary>
    /// Parsed map: tile grid (world Y up, row 0 of the text is the TOP), markers, entity definitions.
    /// </summary>
    public sealed class MapData
    {
        public string Id;
        public string Name;
        public string Theme;
        public string Description;
        public float Ambient = 0.3f;
        public int Width;
        public int Height;
        public Tile[] Tiles;
        public readonly Dictionary<char, Int2> Markers = new Dictionary<char, Int2>();
        public readonly List<EntityDef> Entities = new List<EntityDef>();
        public readonly Dictionary<string, List<EntityDef>> MissionEntities = new Dictionary<string, List<EntityDef>>();

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public Tile Get(int x, int y) => InBounds(x, y) ? Tiles[y * Width + x] : TileLegend.FromChar(' ');
        public Tile Get(Int2 c) => Get(c.x, c.y);
        public void Set(int x, int y, Tile t) { if (InBounds(x, y)) Tiles[y * Width + x] = t; }

        public Int2 Marker(char c)
        {
            if (!Markers.TryGetValue(c, out var p))
                throw new KeyNotFoundException($"Map '{Id}' has no marker '{c}'");
            return p;
        }

        public Vec2 MarkerPos(char c) => Marker(c).Center;

        public IEnumerable<EntityDef> EntitiesFor(string missionId)
        {
            foreach (var e in Entities) yield return e;
            if (missionId != null && MissionEntities.TryGetValue(missionId, out var list))
                foreach (var e in list) yield return e;
        }

        /// <summary>
        /// Parses a point: a single marker character, or "col,row" in text-grid coordinates (row 0 = top line).
        /// </summary>
        public Vec2 ParsePoint(string s)
        {
            s = s.Trim();
            if (s.Length == 1) return MarkerPos(s[0]);
            int comma = s.IndexOf(',');
            if (comma < 0) throw new FormatException($"Bad point '{s}' in map '{Id}'");
            float col = float.Parse(s.Substring(0, comma), CultureInfo.InvariantCulture);
            float row = float.Parse(s.Substring(comma + 1), CultureInfo.InvariantCulture);
            return new Vec2(col + 0.5f, Height - 1 - row + 0.5f);
        }

        /// <summary>Parses "12,30:2:90|14,30|a:1.5::Reading" (point[:wait[:lookDegrees[:activity]]] separated by '|').</summary>
        public List<Waypoint> ParseRoute(string route)
        {
            var result = new List<Waypoint>();
            if (string.IsNullOrWhiteSpace(route)) return result;
            foreach (var part in route.Split('|'))
            {
                var bits = part.Trim().Split(':');
                var wp = new Waypoint { Pos = ParsePoint(bits[0]), Wait = 0f, LookAngle = float.NaN };
                if (bits.Length > 1 && bits[1].Length > 0) wp.Wait = float.Parse(bits[1], CultureInfo.InvariantCulture);
                if (bits.Length > 2 && bits[2].Length > 0) wp.LookAngle = float.Parse(bits[2], CultureInfo.InvariantCulture) * MathUtil.Deg2Rad;
                if (bits.Length > 3) wp.Activity = bits[3].Replace('_', ' ');
                result.Add(wp);
            }
            return result;
        }

        /// <summary>A rectangle given as "col,row,w,h" in text-grid coordinates (col,row = top-left tile).</summary>
        public Rect ParseRect(string s)
        {
            var b = s.Split(',');
            if (b.Length != 4) throw new FormatException($"Bad rect '{s}' in map '{Id}'");
            float col = float.Parse(b[0], CultureInfo.InvariantCulture), row = float.Parse(b[1], CultureInfo.InvariantCulture);
            float w = float.Parse(b[2], CultureInfo.InvariantCulture), h = float.Parse(b[3], CultureInfo.InvariantCulture);
            return new Rect(col, Height - row - h, w, h);
        }

        // ------------------------------------------------------------------ parsing

        public static MapData Parse(string text)
        {
            var map = new MapData();
            var tileRows = new List<string>();
            string section = null;
            List<EntityDef> currentEntities = null;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("@"))
                {
                    var head = trimmed.Substring(1).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    section = head[0];
                    if (section == "mission")
                    {
                        if (head.Length < 2) throw new FormatException($"Line {i + 1}: @mission needs an id");
                        currentEntities = new List<EntityDef>();
                        map.MissionEntities[head[1].Trim()] = currentEntities;
                    }
                    else if (section == "entities") currentEntities = map.Entities;
                    continue;
                }

                if (section == "tiles") { if (raw.Length > 0 || tileRows.Count > 0) tileRows.Add(raw); continue; }
                if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;

                if (section == "meta")
                {
                    int colon = trimmed.IndexOf(':');
                    if (colon < 0) continue;
                    string key = trimmed.Substring(0, colon).Trim();
                    string val = trimmed.Substring(colon + 1).Trim();
                    switch (key)
                    {
                        case "id": map.Id = val; break;
                        case "name": map.Name = val; break;
                        case "theme": map.Theme = val; break;
                        case "description": map.Description = val; break;
                        case "ambient": map.Ambient = float.Parse(val, CultureInfo.InvariantCulture); break;
                    }
                }
                else if (section == "entities" || section == "mission")
                {
                    var e = ParseEntityLine(trimmed);
                    e.Line = i + 1;
                    currentEntities.Add(e);
                }
            }

            TrimTrailingEmpty(tileRows);
            if (tileRows.Count == 0) throw new FormatException("Map has no @tiles section");

            map.Height = tileRows.Count;
            map.Width = 0;
            foreach (var r in tileRows) map.Width = Math.Max(map.Width, r.Length);
            map.Tiles = new Tile[map.Width * map.Height];

            for (int row = 0; row < map.Height; row++)
            {
                string r = tileRows[row];
                int y = map.Height - 1 - row;
                for (int x = 0; x < map.Width; x++)
                {
                    char c = x < r.Length ? r[x] : ' ';
                    if (IsMarkerChar(c))
                    {
                        if (map.Markers.ContainsKey(c))
                            throw new FormatException($"Map '{map.Id}': marker '{c}' is defined twice");
                        map.Markers[c] = new Int2(x, y);
                        c = InferFloor(tileRows, row, x);
                    }
                    try { map.Tiles[y * map.Width + x] = TileLegend.FromChar(c); }
                    catch (FormatException ex) { throw new FormatException($"Map '{map.Id}' row {row + 1} col {x + 1}: {ex.Message}"); }
                }
            }
            return map;
        }

        /// <summary>Characters that mark named points inside the tile grid. Mirrored in tools/maplib.py.</summary>
        public const string MarkerChars = "AEFGHIJKNOQRUVXYZadlz0123456789!$%&*+?^<>'/\\{}[]()";

        public static bool IsMarkerChar(char c) => MarkerChars.IndexOf(c) >= 0;

        /// <summary>A marker stands on the most common floor character among its neighbours.</summary>
        public static char InferFloor(List<string> rows, int row, int col)
        {
            var counts = new Dictionary<char, int>();
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int rr = row + dy, cc = col + dx;
                if ((dx == 0 && dy == 0) || rr < 0 || rr >= rows.Count || cc < 0 || cc >= rows[rr].Length) continue;
                char n = rows[rr][cc];
                if (".:;_,\"".IndexOf(n) < 0) continue;
                counts.TryGetValue(n, out int k);
                counts[n] = k + 1;
            }
            char best = '.';
            int bestN = 0;
            foreach (var kv in counts)
                if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best)) { best = kv.Key; bestN = kv.Value; }
            return best;
        }

        private static void TrimTrailingEmpty(List<string> rows)
        {
            while (rows.Count > 0 && rows[rows.Count - 1].Length == 0) rows.RemoveAt(rows.Count - 1);
        }

        public static EntityDef ParseEntityLine(string line)
        {
            var e = new EntityDef();
            int i = 0;
            e.Kind = ReadToken(line, ref i);
            while (true)
            {
                SkipSpaces(line, ref i);
                if (i >= line.Length) break;
                int eq = line.IndexOf('=', i);
                if (eq < 0) throw new FormatException($"Bad entity property near '{line.Substring(i)}'");
                string key = line.Substring(i, eq - i).Trim();
                i = eq + 1;
                string val;
                if (i < line.Length && line[i] == '"')
                {
                    int end = line.IndexOf('"', i + 1);
                    if (end < 0) throw new FormatException("Unterminated quote in: " + line);
                    val = line.Substring(i + 1, end - i - 1);
                    i = end + 1;
                }
                else val = ReadToken(line, ref i);
                e.Props[key] = val;
            }
            return e;
        }

        private static void SkipSpaces(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static string ReadToken(string s, ref int i)
        {
            SkipSpaces(s, ref i);
            var sb = new StringBuilder();
            while (i < s.Length && !char.IsWhiteSpace(s[i])) sb.Append(s[i++]);
            return sb.ToString();
        }
    }
}
