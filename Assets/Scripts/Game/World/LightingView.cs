using System.Collections.Generic;
using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Darkness overlay: the core bakes RGB light (with wall shadows) into a small texture that is stretched over the map
    /// and multiplied onto the scene. Additive glow sprites sit on top for lamps; flickering lamps animate their glow.
    /// Rebakes are throttled (doors opening and power cuts change the lighting).
    /// </summary>
    public sealed class LightingView : MonoBehaviour
    {
        private const int Samples = 2; // light samples per tile
        private GameSession _s;
        private Texture2D _tex;
        private float[] _rgb;
        private Color32[] _pixels;
        private float _rebakeCooldown;
        private readonly List<(LightDef def, SpriteRenderer glow, float seed)> _glows = new List<(LightDef, SpriteRenderer, float)>();
        private MeshRenderer _quad;

        public void Build(GameSession s, Transform parent)
        {
            _s = s;
            int w = s.World.Width * Samples, h = s.World.Height * Samples;
            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Lightmap" };
            _rgb = new float[w * h * 3];
            _pixels = new Color32[w * h];

            var go = new GameObject("Darkness");
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            _quad = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = "DarknessQuad" };
            float W = s.World.Width, H = s.World.Height;
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(W, 0, 0), new Vector3(W, H, 0), new Vector3(0, H, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mf.sharedMesh = mesh;
            var mat = new Material(SpriteLibrary.LightMultiply) { mainTexture = _tex };
            _quad.sharedMaterial = mat;
            _quad.sortingOrder = Layers.Darkness;

            var glowSprite = SpriteLibrary.Get("FX/glow");
            foreach (var l in s.Lights)
            {
                var g = new GameObject("LampGlow");
                g.transform.SetParent(parent, false);
                g.transform.position = new Vector3(l.Pos.x, l.Pos.y, 0);
                var r = g.AddComponent<SpriteRenderer>();
                r.sprite = glowSprite;
                r.sharedMaterial = SpriteLibrary.Additive;
                r.sortingOrder = Layers.Glow;
                float size = Mathf.Min(l.Radius * 0.28f, 1.6f);
                g.transform.localScale = new Vector3(size, size, 1);
                _glows.Add((l, r, Random.value * 100f));
            }
            Rebake();
        }

        public void Rebake()
        {
            LightMap.Bake(_s.World, _s.Lights, Samples, _rgb);
            for (int i = 0; i < _pixels.Length; i++)
            {
                // store light / 2 so the 2x multiply shader can also brighten
                _pixels[i] = new Color32(
                    (byte)Mathf.Clamp(_rgb[i * 3] * 127.5f, 0, 255),
                    (byte)Mathf.Clamp(_rgb[i * 3 + 1] * 127.5f, 0, 255),
                    (byte)Mathf.Clamp(_rgb[i * 3 + 2] * 127.5f, 0, 255), 255);
            }
            _tex.SetPixels32(_pixels);
            _tex.Apply(false);
            _s.LightsDirty = false;
        }

        private void LateUpdate()
        {
            if (_s == null) return;
            _rebakeCooldown -= Time.unscaledDeltaTime;
            if (_s.LightsDirty && _rebakeCooldown <= 0f)
            {
                Rebake();
                _rebakeCooldown = 0.2f;
            }
            float t = Time.time;
            foreach (var (def, glow, seed) in _glows)
            {
                float a = def.On ? 0.18f * def.Intensity : 0f;
                if (def.Flicker && def.On)
                {
                    float n = Mathf.PerlinNoise(seed, t * 7f);
                    a *= n > 0.25f ? 0.7f + n * 0.5f : 0.1f;
                }
                glow.color = new Color(def.R, def.G, def.B, a);
            }
        }
    }
}
