using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace ShadowContract.Game
{
    /// <summary>Colour scheme shared by every screen (dark tactical).</summary>
    public static class Theme
    {
        public static readonly Color Bg = new Color(0.035f, 0.045f, 0.06f, 0.96f);
        public static readonly Color Panel = new Color(0.065f, 0.08f, 0.105f, 0.94f);
        public static readonly Color PanelLight = new Color(0.1f, 0.12f, 0.155f, 0.96f);
        public static readonly Color Line = new Color(0.23f, 0.27f, 0.33f, 1f);
        public static readonly Color Text = new Color(0.9f, 0.92f, 0.95f);
        public static readonly Color Dim = new Color(0.55f, 0.6f, 0.68f);
        public static readonly Color Mist = new Color(0.71f, 0.75f, 0.8f);
        public static readonly Color Accent = new Color(0.82f, 0.25f, 0.25f);
        public static readonly Color AccentDark = new Color(0.42f, 0.1f, 0.13f);
        public static readonly Color Gold = new Color(0.94f, 0.78f, 0.37f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.5f);
        public static readonly Color Blue = new Color(0.45f, 0.65f, 0.95f);
        public static readonly Color Warn = new Color(1f, 0.7f, 0.3f);
    }

    /// <summary>Hover/click feedback and sounds for buttons.</summary>
    public sealed class UIButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Background;
        public Color Normal, Hover;
        public Text Label;
        public bool Interactable = true;

        public void OnPointerEnter(PointerEventData e)
        {
            if (!Interactable) return;
            if (Background) Background.color = Hover;
            AudioManager.I?.Play2D("ui_hover", 0.5f);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (Background) Background.color = Normal;
        }

        public void SetColors(Color normal, Color hover)
        {
            Normal = normal;
            Hover = hover;
            if (Background) Background.color = normal;
        }
    }

    public static class UI
    {
        private static Sprite _panel, _button, _slot, _white;

        public static Sprite PanelSprite => _panel ? _panel : (_panel = Sliced("UI/panel"));
        public static Sprite ButtonSprite => _button ? _button : (_button = Sliced("UI/button"));
        public static Sprite SlotSprite => _slot ? _slot : (_slot = Sliced("UI/slot"));

        public static Sprite White
        {
            get
            {
                if (_white == null) _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                return _white;
            }
        }

        private static Sprite Sliced(string path)
        {
            var tex = SpriteLibrary.Texture(path);
            if (tex == null) return White;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
        }

        // ------------------------------------------------------------------ canvas

        public static Canvas CreateCanvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.pixelPerfect = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions(); // created from code: no actions asset is assigned otherwise
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // ------------------------------------------------------------------ layout helpers

        public static RectTransform Rect(Transform parent, string name = "rect")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors a rect: anchors in 0..1, then offsets in reference pixels.</summary>
        public static RectTransform Place(this RectTransform r, float minX, float minY, float maxX, float maxY, float l = 0, float b = 0, float rgt = 0, float t = 0)
        {
            r.anchorMin = new Vector2(minX, minY);
            r.anchorMax = new Vector2(maxX, maxY);
            r.offsetMin = new Vector2(l, b);
            r.offsetMax = new Vector2(-rgt, -t);
            return r;
        }

        /// <summary>Fixed-size rect anchored at a point.</summary>
        public static RectTransform At(this RectTransform r, float ax, float ay, float x, float y, float w, float h, float pivotX = 0.5f, float pivotY = 0.5f)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, ay);
            r.pivot = new Vector2(pivotX, pivotY);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
            return r;
        }

        public static RectTransform Fill(this RectTransform r) => r.Place(0, 0, 1, 1);

        public static Image Panel(Transform parent, Color color, string name = "panel", bool sliced = true)
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sliced ? PanelSprite : White;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.pixelsPerUnitMultiplier = 0.25f;
            return img;
        }

        public static Image Box(Transform parent, Color color, string name = "box")
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.color = color;
            return img;
        }

        public static Image Icon(Transform parent, Sprite sprite, float w, float h, string name = "icon")
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            r.sizeDelta = new Vector2(w, h);
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool header = false, string name = "text")
        {
            var r = Rect(parent, name);
            var t = r.gameObject.AddComponent<Text>();
            t.font = header ? SpriteLibrary.UIFontBold : SpriteLibrary.BodyFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Text Header(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = Label(parent, text, size, color, align, true, "header");
            t.font = SpriteLibrary.UIFont;
            return t;
        }

        public static Shadow AddShadow(Graphic g, float dist = 2f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.8f);
            s.effectDistance = new Vector2(dist, -dist);
            return s;
        }

        // ------------------------------------------------------------------ controls

        public static Button Button(Transform parent, string label, Action onClick, int fontSize = 30, bool primary = false, string name = "button")
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = ButtonSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.25f;
            var btn = r.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var fx = r.gameObject.AddComponent<UIButtonFx>();
            fx.Background = img;
            Color normal = primary ? Theme.AccentDark : new Color(0.13f, 0.16f, 0.2f, 1f);
            Color hover = primary ? Theme.Accent : new Color(0.22f, 0.27f, 0.34f, 1f);
            fx.SetColors(normal, hover);
            var t = Header(r, label.ToUpperInvariant(), fontSize - 8, Theme.Text, TextAnchor.MiddleCenter);
            t.rectTransform.Fill();
            fx.Label = t;
            btn.onClick.AddListener(() =>
            {
                if (!fx.Interactable) { AudioManager.I?.Play2D("ui_error", 0.6f); return; }
                AudioManager.I?.Play2D("ui_click", 0.7f);
                onClick?.Invoke();
            });
            return btn;
        }

        public static void SetEnabled(Button b, bool enabled)
        {
            var fx = b.GetComponent<UIButtonFx>();
            if (fx == null) return;
            fx.Interactable = enabled;
            if (fx.Label) fx.Label.color = enabled ? Theme.Text : new Color(0.45f, 0.48f, 0.52f);
            fx.Background.color = enabled ? fx.Normal : new Color(0.1f, 0.11f, 0.13f, 1f);
        }

        public static void SetLabel(Button b, string text)
        {
            var fx = b.GetComponent<UIButtonFx>();
            if (fx != null && fx.Label) fx.Label.text = text.ToUpperInvariant();
        }

        /// <summary>Horizontal bar with a fill (health, armor, stats, detection).</summary>
        public static Image Bar(Transform parent, Color back, Color fill, out RectTransform root, string name = "bar")
        {
            root = Rect(parent, name);
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = White;
            bg.color = back;
            bg.raycastTarget = false;
            var f = Rect(root, "fill").Fill();
            f.offsetMin = new Vector2(2, 2);
            f.offsetMax = new Vector2(-2, -2);
            var img = f.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillAmount = 1f;
            img.color = fill;
            img.raycastTarget = false;
            return img;
        }

        public static VerticalLayoutGroup VList(RectTransform r, float spacing, RectOffset padding = null, bool controlHeight = true)
        {
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childControlHeight = controlHeight;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childAlignment = TextAnchor.UpperCenter;
            return v;
        }

        public static HorizontalLayoutGroup HList(RectTransform r, float spacing, RectOffset padding = null)
        {
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = true;
            return h;
        }

        public static LayoutElement Size(Component c, float prefW = -1, float prefH = -1, float flexW = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>(); // (no ?? : Unity's fake-null objects)
            if (prefW >= 0) le.preferredWidth = prefW;
            if (prefH >= 0) { le.preferredHeight = prefH; le.minHeight = prefH; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        /// <summary>Vertical scroll view. Returns the content rect (add children to it).</summary>
        public static RectTransform ScrollList(Transform parent, float spacing, out ScrollRect scroll)
        {
            var view = Rect(parent, "scroll");
            var img = view.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.color = new Color(0, 0, 0, 0.01f);
            view.gameObject.AddComponent<RectMask2D>();
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            var content = Rect(view, "content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VList(content, spacing);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = view;
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>"&lt; value &gt;" selector used for settings (keyboard/mouse friendly, pixel-art style).</summary>
        public static Text Selector(Transform parent, string label, Func<string> value, Action<int> change, float height = 56)
        {
            var row = Rect(parent, "selector");
            Size(row, -1, height);
            var name = Label(row, label, 32, Theme.Text);
            name.rectTransform.Place(0, 0, 0.5f, 1, 12, 0, 0, 0);
            var left = Button(row, "<", () => change(-1), 30);
            ((RectTransform)left.transform).At(0.5f, 0.5f, 0, 0, 56, height - 12, 0f, 0.5f);
            var val = Label(row, value(), 32, Theme.Gold, TextAnchor.MiddleCenter);
            val.rectTransform.Place(0.5f, 0, 1, 1, 64, 0, 64, 0);
            var right = Button(row, ">", () => change(1), 30);
            ((RectTransform)right.transform).At(1f, 0.5f, 0, 0, 56, height - 12, 1f, 0.5f);
            return val;
        }

        public static string Money(int amount) => "$" + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        public static string Time(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
