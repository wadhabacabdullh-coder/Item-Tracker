using System;

namespace ShadowContract.Core
{
    /// <summary>
    /// Engine-independent 2D vector. World units are tiles (1 unit = 1 tile = 16 pixels).
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x;
        public float y;

        public Vec2(float x, float y) { this.x = x; this.y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 One = new Vec2(1f, 1f);
        public static readonly Vec2 Right = new Vec2(1f, 0f);
        public static readonly Vec2 Up = new Vec2(0f, 1f);

        public float Length => (float)Math.Sqrt(x * x + y * y);
        public float SqrLength => x * x + y * y;

        public Vec2 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-6f ? new Vec2(x / len, y / len) : Zero;
            }
        }

        /// <summary>Angle in radians, measured from +X counter-clockwise.</summary>
        public float Angle => (float)Math.Atan2(y, x);

        public static Vec2 FromAngle(float radians) => new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));

        public Vec2 Rotated(float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vec2(x * c - y * s, x * s + y * c);
        }

        public Vec2 Perp => new Vec2(-y, x);

        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.y * b.y;
        public static float Cross(Vec2 a, Vec2 b) => a.x * b.y - a.y * b.x;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        public static Vec2 MoveTowards(Vec2 current, Vec2 target, float maxDelta)
        {
            Vec2 d = target - current;
            float len = d.Length;
            if (len <= maxDelta || len < 1e-6f) return target;
            return current + d / len * maxDelta;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.x / s, a.y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public bool Equals(Vec2 other) => this == other;
        public override bool Equals(object obj) => obj is Vec2 v && this == v;
        public override int GetHashCode() => x.GetHashCode() * 397 ^ y.GetHashCode();
        public override string ToString() => $"({x:0.00}, {y:0.00})";
    }

    /// <summary>Integer tile coordinate.</summary>
    [Serializable]
    public struct Int2 : IEquatable<Int2>
    {
        public int x;
        public int y;
        public Int2(int x, int y) { this.x = x; this.y = y; }

        public Vec2 Center => new Vec2(x + 0.5f, y + 0.5f);
        public static Int2 FromWorld(Vec2 p) => new Int2((int)Math.Floor(p.x), (int)Math.Floor(p.y));
        public static int Manhattan(Int2 a, Int2 b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.x + b.x, a.y + b.y);
        public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.x - b.x, a.y - b.y);
        public static bool operator ==(Int2 a, Int2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Int2 a, Int2 b) => !(a == b);
        public bool Equals(Int2 other) => this == other;
        public override bool Equals(object obj) => obj is Int2 v && this == v;
        public override int GetHashCode() => x * 73856093 ^ y * 19349663;
        public override string ToString() => $"[{x},{y}]";
    }
}
