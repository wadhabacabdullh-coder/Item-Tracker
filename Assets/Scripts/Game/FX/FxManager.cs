using System.Collections.Generic;
using UnityEngine;

namespace ShadowContract.Game
{
    public static class Layers
    {
        // sortingOrder values (single sorting layer keeps setup-free)
        public const int Map = -100, Decal = -90, Pickup = -70, Body = -65, Door = -60, Legs = 0, Char = 1, Weapon = 2,
            Projectile = 40, Particle = 45, Tracer = 46, Overlay = 60, Darkness = 70, Cone = 74, Glow = 80, PlayerRim = 82, WorldUi = 90;
    }

    /// <summary>
    /// Pooled sprite particles, decals, tracers, muzzle flashes and explosions. Nothing is instantiated during play
    /// after warm-up, which keeps garbage collection and CPU cost low.
    /// </summary>
    public sealed class FxManager : MonoBehaviour
    {
        public static FxManager I { get; private set; }

        private sealed class Particle
        {
            public SpriteRenderer R;
            public Vector2 Vel;
            public float Spin, Life, MaxLife, Drag, Grow, StartAlpha;
            public Sprite[] Frames;
            public bool Active;
        }

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Queue<SpriteRenderer> _decals = new Queue<SpriteRenderer>();
        private readonly List<SpriteRenderer> _decalPool = new List<SpriteRenderer>();
        private int _decalIndex;
        private Transform _root;

        private Sprite _spark, _casing, _shell, _glass, _hit, _dust, _smoke, _glow, _ring, _pixel, _hole, _scorch;
        private Sprite[] _muzzle, _explosion, _stains;

        private const int MaxParticles = 450;
        private const int MaxDecals = 260;

        private void Awake()
        {
            I = this;
            _root = new GameObject("FX").transform;
            _root.SetParent(transform, false);
            _spark = SpriteLibrary.Get("FX/spark");
            _casing = SpriteLibrary.Get("FX/casing");
            _shell = SpriteLibrary.Get("FX/shell");
            _glass = SpriteLibrary.Get("FX/glass");
            _hit = SpriteLibrary.Get("FX/hit");
            _dust = SpriteLibrary.Get("FX/dust");
            _smoke = SpriteLibrary.Get("FX/smoke");
            _glow = SpriteLibrary.Get("FX/glow");
            _ring = SpriteLibrary.Get("FX/ring");
            _pixel = SpriteLibrary.Get("FX/pixel", 0f, 0.5f);
            _hole = SpriteLibrary.Get("FX/bullet_hole");
            _scorch = SpriteLibrary.Get("FX/scorch");
            _muzzle = new[] { SpriteLibrary.Get("FX/muzzle_0", 0.25f, 0.5f), SpriteLibrary.Get("FX/muzzle_1", 0.25f, 0.5f), SpriteLibrary.Get("FX/muzzle_2", 0.25f, 0.5f) };
            _explosion = new Sprite[6];
            for (int i = 0; i < 6; i++) _explosion[i] = SpriteLibrary.Get("FX/explosion_" + i);
            _stains = new[] { SpriteLibrary.Get("FX/stain_0"), SpriteLibrary.Get("FX/stain_1"), SpriteLibrary.Get("FX/stain_2") };

            for (int i = 0; i < MaxParticles; i++)
            {
                var go = new GameObject("p");
                go.transform.SetParent(_root, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.enabled = false;
                _particles.Add(new Particle { R = r });
            }
            for (int i = 0; i < MaxDecals; i++)
            {
                var go = new GameObject("d");
                go.transform.SetParent(_root, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.enabled = false;
                r.sortingOrder = Layers.Decal;
                _decalPool.Add(r);
            }
        }

        public void Clear()
        {
            foreach (var p in _particles) { p.Active = false; p.R.enabled = false; }
            foreach (var d in _decalPool) d.enabled = false;
        }

        private Particle Spawn(Sprite s, Vector2 pos, Vector2 vel, float life, int order, float scale = 1f, float angle = 0f, Material mat = null)
        {
            Particle p = null;
            for (int i = 0; i < _particles.Count; i++)
                if (!_particles[i].Active) { p = _particles[i]; break; }
            if (p == null)
            {
                // Recycle the oldest-looking particle.
                p = _particles[Random.Range(0, _particles.Count)];
            }
            p.Active = true;
            p.Vel = vel;
            p.Life = p.MaxLife = life;
            p.Spin = 0f;
            p.Drag = 4f;
            p.Grow = 0f;
            p.Frames = null;
            p.StartAlpha = 1f;
            var r = p.R;
            r.sprite = s;
            r.enabled = true;
            r.sortingOrder = order;
            r.color = Color.white;
            r.sharedMaterial = mat != null ? mat : DefaultMat;
            var t = r.transform;
            t.position = new Vector3(pos.x, pos.y, 0f);
            t.rotation = Quaternion.Euler(0, 0, angle);
            t.localScale = new Vector3(scale, scale, 1f);
            return p;
        }

        private Material _defaultMat;
        private Material DefaultMat
        {
            get
            {
                if (_defaultMat == null) _defaultMat = _decalPool[0].sharedMaterial;
                return _defaultMat;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                if (!p.Active) continue;
                p.Life -= dt;
                if (p.Life <= 0f) { p.Active = false; p.R.enabled = false; continue; }
                var t = p.R.transform;
                Vector3 pos = t.position;
                pos.x += p.Vel.x * dt;
                pos.y += p.Vel.y * dt;
                t.position = pos;
                p.Vel *= Mathf.Max(0f, 1f - p.Drag * dt);
                if (p.Spin != 0f) t.Rotate(0, 0, p.Spin * dt);
                float k = p.Life / p.MaxLife;
                if (p.Grow != 0f) t.localScale += new Vector3(p.Grow * dt, p.Grow * dt, 0);
                if (p.Frames != null)
                {
                    int f = Mathf.Clamp((int)((1f - k) * p.Frames.Length), 0, p.Frames.Length - 1);
                    p.R.sprite = p.Frames[f];
                }
                var c = p.R.color;
                c.a = p.StartAlpha * Mathf.Clamp01(k * 2.5f);
                p.R.color = c;
            }
        }

        // ------------------------------------------------------------------ decals

        public void Decal(Sprite s, Vector2 pos, float angle, float alpha = 1f, float scale = 1f)
        {
            var r = _decalPool[_decalIndex];
            _decalIndex = (_decalIndex + 1) % _decalPool.Count;
            r.sprite = s;
            r.enabled = true;
            r.color = new Color(1, 1, 1, alpha);
            r.transform.position = new Vector3(pos.x, pos.y, 0);
            r.transform.rotation = Quaternion.Euler(0, 0, angle);
            r.transform.localScale = new Vector3(scale, scale, 1);
        }

        // ------------------------------------------------------------------ effects

        public void MuzzleFlash(Vector2 pos, float angleRad, bool suppressed)
        {
            float deg = angleRad * Mathf.Rad2Deg;
            if (!suppressed)
            {
                var p = Spawn(_muzzle[0], pos, Vector2.zero, 0.06f, Layers.Glow, 1f, deg, SpriteLibrary.Additive);
                p.Frames = _muzzle;
                var g = Spawn(_glow, pos, Vector2.zero, 0.08f, Layers.Glow, 1.6f, 0f, SpriteLibrary.Additive);
                g.R.color = new Color(1f, 0.8f, 0.45f, 0.55f);
                g.StartAlpha = 0.55f;
            }
            else
            {
                var s = Spawn(_smoke, pos, Vector2.zero, 0.25f, Layers.Particle, 0.35f);
                s.StartAlpha = 0.35f;
                s.Grow = 0.8f;
            }
        }

        public void Tracer(Vector2 from, Vector2 to, bool player)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return;
            var p = Spawn(_pixel, from, Vector2.zero, 0.05f, Layers.Tracer, 1f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, SpriteLibrary.Additive);
            p.R.transform.localScale = new Vector3(len * 4f, 0.25f, 1f); // pixel sprite is 4px = 0.25 world units
            p.R.color = player ? new Color(1f, 0.9f, 0.6f, 0.7f) : new Color(1f, 0.55f, 0.4f, 0.7f);
            p.StartAlpha = 0.7f;
        }

        public void Casing(Vector2 pos, float ejectAngleRad, bool shell)
        {
            Vector2 v = new Vector2(Mathf.Cos(ejectAngleRad), Mathf.Sin(ejectAngleRad)) * Random.Range(2.5f, 4f);
            var p = Spawn(shell ? _shell : _casing, pos, v, 0.45f, Layers.Particle, 1f, Random.Range(0, 360));
            p.Spin = Random.Range(-900f, 900f);
            p.Drag = 7f;
            // leave a resting casing behind for a while
            Vector2 rest = pos + v * 0.14f;
            Decal(shell ? _shell : _casing, rest, Random.Range(0, 360), 0.9f);
        }

        public void Impact(Vector2 pos, Vector2 normal, bool metal)
        {
            Decal(_hole, pos - normal * 0.02f, 0f, 0.8f);
            int n = metal ? 6 : 4;
            for (int i = 0; i < n; i++)
            {
                Vector2 v = (normal + Random.insideUnitCircle * 0.9f).normalized * Random.Range(2f, 5f);
                var p = Spawn(metal ? _spark : _dust, pos, v, Random.Range(0.12f, 0.3f), Layers.Particle, 1f);
                p.Drag = 6f;
            }
            if (!metal)
            {
                var s = Spawn(_smoke, pos + normal * 0.1f, normal * 0.6f, 0.4f, Layers.Particle, 0.4f);
                s.StartAlpha = 0.3f;
                s.Grow = 0.8f;
            }
        }

        /// <summary>Non-graphic hit feedback: a few dark droplets and a small stain.</summary>
        public void Hit(Vector2 pos, float angleRad, bool camera)
        {
            if (camera)
            {
                for (int i = 0; i < 6; i++) Spawn(_spark, pos, Random.insideUnitCircle * 4f, 0.25f, Layers.Particle).Drag = 6f;
                return;
            }
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            for (int i = 0; i < 5; i++)
            {
                Vector2 v = (dir + Random.insideUnitCircle * 0.6f) * Random.Range(1.5f, 3.5f);
                var p = Spawn(_hit, pos, v, Random.Range(0.2f, 0.35f), Layers.Particle);
                p.Drag = 9f;
            }
            if (Random.value < 0.5f) Decal(_stains[Random.Range(0, _stains.Length)], pos + dir * 0.4f, Random.Range(0, 360), 0.55f, 0.45f);
        }

        public void Stain(Vector2 pos)
        {
            Decal(_stains[Random.Range(0, _stains.Length)], pos, Random.Range(0, 360), 0.7f, 1f);
        }

        public void Explosion(Vector2 pos, float radius)
        {
            var e = Spawn(_explosion[0], pos, Vector2.zero, 0.55f, Layers.Glow, radius / 1.5f);
            e.Frames = _explosion;
            var g = Spawn(_glow, pos, Vector2.zero, 0.35f, Layers.Glow, radius * 2.5f, 0, SpriteLibrary.Additive);
            g.R.color = new Color(1f, 0.6f, 0.25f, 1f);
            for (int i = 0; i < 18; i++)
            {
                var s = Spawn(_spark, pos, Random.insideUnitCircle.normalized * Random.Range(4f, 10f), Random.Range(0.3f, 0.7f), Layers.Particle);
                s.Drag = 3f;
            }
            for (int i = 0; i < 8; i++)
            {
                var s = Spawn(_smoke, pos + Random.insideUnitCircle, Random.insideUnitCircle * 1.2f, Random.Range(1f, 1.8f), Layers.Particle, 1.2f);
                s.R.color = new Color(0.3f, 0.3f, 0.32f, 0.7f);
                s.StartAlpha = 0.7f;
                s.Grow = 1.2f;
                s.Drag = 1.5f;
            }
            Decal(_scorch, pos, Random.Range(0, 360), 0.9f, radius / 1.2f);
        }

        public void GlassBurst(Vector2 pos)
        {
            for (int i = 0; i < 12; i++)
            {
                var p = Spawn(_glass, pos, Random.insideUnitCircle * 4f, Random.Range(0.3f, 0.6f), Layers.Particle);
                p.Spin = Random.Range(-600f, 600f);
                p.Drag = 5f;
                Decal(_glass, pos + Random.insideUnitCircle * 0.9f, Random.Range(0, 360), 0.8f);
            }
        }

        public void Sparks(Vector2 pos, int count, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Spawn(_spark, pos, Random.insideUnitCircle * 3f, Random.Range(0.2f, 0.4f), Layers.Particle);
                p.R.color = color;
            }
        }

        /// <summary>Faint expanding ring that visualises how far a noise carries (stealth readability).</summary>
        public void NoiseRing(Vector2 pos, float radius, Color color)
        {
            var p = Spawn(_ring, pos, Vector2.zero, 0.5f, Layers.WorldUi - 1, 0.2f);
            p.R.color = color;
            p.StartAlpha = color.a;
            p.Grow = radius * 2f / 4f / 0.5f; // reach full diameter (ring sprite is 4 world units) in its lifetime
        }

        public void Puff(Vector2 pos, float scale = 0.6f)
        {
            var s = Spawn(_smoke, pos, Random.insideUnitCircle * 0.5f, 0.5f, Layers.Particle, scale);
            s.StartAlpha = 0.4f;
            s.Grow = 0.6f;
        }
    }
}
