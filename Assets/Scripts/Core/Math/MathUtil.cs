using System;

namespace ShadowContract.Core
{
    public static class MathUtil
    {
        public const float Pi = (float)Math.PI;
        public const float TwoPi = (float)(Math.PI * 2);
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Wraps an angle into (-PI, PI].</summary>
        public static float WrapAngle(float a)
        {
            while (a > Pi) a -= TwoPi;
            while (a <= -Pi) a += TwoPi;
            return a;
        }

        public static float AngleDelta(float from, float to) => WrapAngle(to - from);

        public static float RotateTowards(float current, float target, float maxDelta)
        {
            float d = AngleDelta(current, target);
            if (Math.Abs(d) <= maxDelta) return target;
            return WrapAngle(current + Math.Sign(d) * maxDelta);
        }

        public static float SmoothStep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>Small deterministic xorshift RNG so simulations are reproducible in tests.</summary>
    public sealed class Rng
    {
        private uint _state;

        public Rng(int seed) { _state = (uint)seed * 2654435761u + 1u; if (_state == 0) _state = 1; }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform float in [0,1).</summary>
        public float Value() => (NextUInt() & 0xFFFFFF) / 16777216f;
        public float Range(float min, float max) => min + (max - min) * Value();
        /// <summary>Integer in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive) => maxExclusive <= min ? min : min + (int)(NextUInt() % (uint)(maxExclusive - min));
        public bool Chance(float p) => Value() < p;
        /// <summary>Approximately gaussian value in [-1,1] (sum of uniforms).</summary>
        public float Spread() => (Value() + Value() + Value()) / 1.5f - 1f;
    }
}
