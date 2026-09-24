using System;
using System.Collections.Generic;

namespace ShadowContract.Core
{
    public sealed class LightDef
    {
        public Vec2 Pos;
        public float Radius = 6f;
        public float Intensity = 1f;
        public float R = 1f, G = 0.9f, B = 0.75f;
        public string Group;       // lights sharing a group can be switched off together (fuse boxes)
        public bool Flicker;
        public bool On = true;

        public static LightDef FromEntity(EntityDef e, MapData map)
        {
            var l = new LightDef
            {
                Pos = map.ParsePoint(e.Str("at")),
                Radius = e.Float("r", 6f),
                Intensity = e.Float("i", 1f),
                Group = e.Str("group"),
                Flicker = e.Bool("flicker"),
            };
            ParseHexColor(e.Str("color", "ffe6c0"), out l.R, out l.G, out l.B);
            return l;
        }

        public static void ParseHexColor(string hex, out float r, out float g, out float b)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            r = ((v >> 16) & 0xFF) / 255f;
            g = ((v >> 8) & 0xFF) / 255f;
            b = (v & 0xFF) / 255f;
        }
    }

    /// <summary>
    /// Bakes light into (a) a per-tile brightness used by the stealth system and
    /// (b) an RGB texture buffer (several samples per tile) used by the renderer's darkness overlay.
    /// Walls cast hard shadows because every sample is ray-tested against the light.
    /// </summary>
    public static class LightMap
    {
        public const float AmbientR = 0.55f, AmbientG = 0.62f, AmbientB = 0.85f; // cool night tint

        public static void Bake(World world, IList<LightDef> lights, int samplesPerTile, float[] rgbOut)
        {
            int w = world.Width * samplesPerTile, h = world.Height * samplesPerTile;
            if (rgbOut != null && rgbOut.Length < w * h * 3) throw new ArgumentException("rgbOut too small");
            float amb = world.Map.Ambient;
            float inv = 1f / samplesPerTile;

            // Per-tile brightness for stealth (sampled at tile centres).
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                float b = amb;
                Vec2 p = new Vec2(x + 0.5f, y + 0.5f);
                foreach (var l in lights) b += Contribution(world, l, p);
                world.SetLight(x, y, MathUtil.Clamp01(b));
            }

            if (rgbOut == null) return;
            for (int sy = 0; sy < h; sy++)
            for (int sx = 0; sx < w; sx++)
            {
                Vec2 p = new Vec2((sx + 0.5f) * inv, (sy + 0.5f) * inv);
                float r = amb * AmbientR, g = amb * AmbientG, bl = amb * AmbientB;
                foreach (var l in lights)
                {
                    float c = Contribution(world, l, p);
                    if (c <= 0f) continue;
                    r += c * l.R; g += c * l.G; bl += c * l.B;
                }
                int i = (sy * w + sx) * 3;
                rgbOut[i] = Math.Min(r, 1.4f);
                rgbOut[i + 1] = Math.Min(g, 1.4f);
                rgbOut[i + 2] = Math.Min(bl, 1.4f);
            }
        }

        private static float Contribution(World world, LightDef l, Vec2 p)
        {
            if (!l.On) return 0f;
            float d = Vec2.Distance(l.Pos, p);
            if (d >= l.Radius) return 0f;
            float falloff = 1f - d / l.Radius;
            falloff = falloff * falloff * (3f - 2f * falloff);
            if (d > 0.75f)
            {
                // Light reaching a wall face should still light the wall itself: test up to just before the sample.
                Vec2 dir = (p - l.Pos) / d;
                var hit = world.Raycast(l.Pos, dir, d - 0.05f, (x, y) =>
                {
                    var t = world.TileAt(x, y);
                    if (t.Kind == TileKind.Wall || t.Kind == TileKind.Void) return true;
                    if (t.Kind == TileKind.Door) { var door = world.DoorAt(x, y); return door != null && !door.Open; }
                    return t.Kind == TileKind.Furniture && t.Is(TileFlags.BlocksSight) && t.Furniture != FurnitureType.Tree;
                });
                if (hit.Hit)
                {
                    // Let light spill one cell into the blocking surface so walls are lit on their face.
                    if (hit.Cell != Int2.FromWorld(p)) return 0f;
                }
            }
            return falloff * l.Intensity;
        }
    }
}
