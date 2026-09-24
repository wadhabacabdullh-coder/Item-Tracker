using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Orthographic camera with integer pixel scaling (crisp pixel art at any resolution), smooth follow,
    /// look-ahead towards the cursor and optional, restrained screen shake.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam { get; private set; }

        public bool ShakeEnabled = true;
        public float BasePixelsTall = 280f;      // world pixels visible vertically before integer scaling
        public float Zoom = 1f;                  // 1 = default, >1 closer

        private Vector2 _target;
        private Vector2 _pos;
        private Vector2 _vel;
        private float _trauma;
        private Vector2 _kick;
        private int _lastW, _lastH;
        private float _lastZoom;

        private void Awake()
        {
            I = this;
            Cam = GetComponent<Camera>();
            if (Cam == null) Cam = gameObject.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.03f, 0.035f, 0.05f);
            Cam.nearClipPlane = -50f;
            Cam.farClipPlane = 50f;
            transform.position = new Vector3(0, 0, -10);
            UpdateSize();
        }

        public int PixelScale { get; private set; } = 4;

        private void UpdateSize()
        {
            if (Screen.width == _lastW && Screen.height == _lastH && Mathf.Approximately(_lastZoom, Zoom)) return;
            _lastW = Screen.width;
            _lastH = Screen.height;
            _lastZoom = Zoom;
            PixelScale = Mathf.Max(1, Mathf.RoundToInt(Screen.height / BasePixelsTall * Zoom));
            Cam.orthographicSize = Screen.height / (2f * SpriteLibrary.PPU * PixelScale);
        }

        public void Snap(Vector2 p)
        {
            _target = _pos = p;
            _vel = Vector2.zero;
            Apply();
        }

        /// <summary>Follow <paramref name="focus"/>, leaning towards <paramref name="aim"/> (world space).</summary>
        public void Follow(Vector2 focus, Vector2 aim, bool aiming, float dt)
        {
            Vector2 lean = aim - focus;
            float max = aiming ? 5.5f : 2.2f;
            lean = Vector2.ClampMagnitude(lean * (aiming ? 0.45f : 0.22f), max);
            _target = focus + lean;
            _pos = Vector2.SmoothDamp(_pos, _target, ref _vel, aiming ? 0.16f : 0.11f, 60f, dt);
            _trauma = Mathf.Max(0f, _trauma - dt * 2.2f);
            _kick = Vector2.Lerp(_kick, Vector2.zero, dt * 18f);
            Apply();
        }

        private void Apply()
        {
            UpdateSize();
            Vector2 p = _pos + _kick;
            if (ShakeEnabled && _trauma > 0f)
            {
                float s = _trauma * _trauma * 0.35f;
                float t = Time.unscaledTime * 40f;
                p += new Vector2((Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * s, (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * s);
            }
            // Snap to the screen pixel grid to avoid shimmering.
            float unit = 1f / (SpriteLibrary.PPU * PixelScale);
            p.x = Mathf.Round(p.x / unit) * unit;
            p.y = Mathf.Round(p.y / unit) * unit;
            transform.position = new Vector3(p.x, p.y, -10f);
        }

        /// <summary>Adds screen shake (0..1). Used sparingly: explosions, heavy weapons, getting hit.</summary>
        public void Shake(float amount)
        {
            if (!ShakeEnabled) return;
            _trauma = Mathf.Min(1f, _trauma + amount);
        }

        /// <summary>Small directional nudge for weapon recoil (always on, very subtle).</summary>
        public void Kick(Vector2 dir, float amount)
        {
            _kick -= dir.normalized * amount;
        }

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 w = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            return new Vector2(w.x, w.y);
        }

        public Vector2 WorldToScreen(Vector2 world)
        {
            Vector3 s = Cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            return new Vector2(s.x, s.y);
        }
    }
}
