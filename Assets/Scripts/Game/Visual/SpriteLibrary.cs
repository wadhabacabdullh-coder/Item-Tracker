using System.Collections.Generic;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Loads generated textures from Resources and slices them into sprites at runtime (so no import-time slicing is required).
    /// Layout contract with tools/art/gen_sprites.py: character sheets are 6 poses x 24px on row 0 and 4 leg frames on row 1.
    /// </summary>
    public static class SpriteLibrary
    {
        public const float PPU = 16f;
        public const int CharCell = 24;

        public enum Pose { Unarmed = 0, OneHand = 1, TwoHand = 2, Knife = 3, Dead = 4, Hidden = 5 }

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite[]> CharCache = new Dictionary<string, Sprite[]>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static Font _ui, _uiBold, _body;

        public static Texture2D Texture(string path)
        {
            var t = Resources.Load<Texture2D>("Sprites/" + path);
            if (t != null)
            {
                t.filterMode = FilterMode.Point;
                t.wrapMode = TextureWrapMode.Clamp;
            }
            return t;
        }

        /// <summary>A whole texture as one sprite. Pivot defaults to the centre.</summary>
        public static Sprite Get(string path, float pivotX = 0.5f, float pivotY = 0.5f)
        {
            string key = path + "|" + pivotX + "|" + pivotY;
            if (Cache.TryGetValue(key, out var s)) return s;
            var tex = Texture(path);
            if (tex == null)
            {
                Debug.LogWarning("Missing sprite: " + path);
                tex = Texture2D.whiteTexture;
            }
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(pivotX, pivotY), PPU, 0, SpriteMeshType.FullRect);
            s.name = path;
            Cache[key] = s;
            return s;
        }

        /// <summary>Character sheet: indices 0-5 = poses, 6-9 = leg frames.</summary>
        public static Sprite[] Character(string type)
        {
            if (CharCache.TryGetValue(type, out var arr)) return arr;
            var tex = Texture("Characters/" + type) ?? Texture("Characters/guard");
            arr = new Sprite[10];
            // Slice proportionally (6 columns x 2 rows) so the sheet still works if it was imported at another size.
            float cw = tex.width / 6f, ch = tex.height / 2f;
            float ppu = PPU * cw / CharCell;
            var pivot = new Vector2(11.5f / CharCell, 0.5f);
            for (int i = 0; i < 6; i++)
                arr[i] = Sprite.Create(tex, new Rect(i * cw, ch, cw, ch), pivot, ppu, 0, SpriteMeshType.FullRect);
            for (int i = 0; i < 4; i++)
                arr[6 + i] = Sprite.Create(tex, new Rect(i * cw, 0, cw, ch), pivot, ppu, 0, SpriteMeshType.FullRect);
            CharCache[type] = arr;
            return arr;
        }

        public static Sprite Icon(string name) => Get("Icons/" + name);
        public static Sprite WeaponIcon(string weaponId) => Get("Icons/w_" + weaponId);
        public static Sprite HeldWeapon(string weaponId) => Get("Weapons/" + weaponId, 2f / 20f, 0.5f);

        public static string CharacterTypeFor(ShadowContract.Core.Npc n)
        {
            if (n.IsTarget) return "target";
            if (n.Kind == ShadowContract.Core.NpcKind.Elite) return "elite";
            if (n.Kind == ShadowContract.Core.NpcKind.Guard) return "guard";
            switch (n.Type)
            {
                case "scientist": return "scientist";
                case "worker": return "worker";
                case "guest": return n.Id % 2 == 0 ? "guest" : "guest2";
                default: return "staff";
            }
        }

        // ------------------------------------------------------------------ materials

        public static Material Mat(string shader)
        {
            if (Materials.TryGetValue(shader, out var m)) return m;
            var sh = Resources.Load<Shader>("Shaders/" + shader);
            if (sh == null) sh = Shader.Find("Sprites/Default");
            m = new Material(sh) { name = shader };
            Materials[shader] = m;
            return m;
        }

        public static Material Additive => Mat("Additive");
        public static Material VertexColor => Mat("VertexColor");
        public static Material LightMultiply => Mat("LightMultiply");

        // ------------------------------------------------------------------ fonts

        public static Font UIFont => _ui ? _ui : (_ui = LoadFont("Fonts/Silkscreen-Regular"));
        public static Font UIFontBold => _uiBold ? _uiBold : (_uiBold = LoadFont("Fonts/Silkscreen-Bold"));
        public static Font BodyFont => _body ? _body : (_body = LoadFont("Fonts/VT323-Regular"));

        private static Font LoadFont(string path)
        {
            var f = Resources.Load<Font>(path);
            if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f;
        }

        /// <summary>Keeps dynamic pixel fonts crisp: the atlas is regenerated at runtime, so re-apply point filtering.</summary>
        public static void KeepFontsCrisp()
        {
            Font.textureRebuilt += font =>
            {
                if (font != null && font.material != null && font.material.mainTexture != null)
                    font.material.mainTexture.filterMode = FilterMode.Point;
            };
        }
    }
}
