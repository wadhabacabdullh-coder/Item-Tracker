using UnityEngine;
using UnityEngine.UI;

namespace ShadowContract.Game
{
    /// <summary>Base class for full-screen menus. Each screen builds its widgets once and refreshes on show.</summary>
    public abstract class UIScreen : MonoBehaviour
    {
        protected RectTransform Root;
        protected GameManager GM => GameManager.I;
        private CanvasGroup _group;
        private float _fade;

        public void Init(RectTransform canvasRoot)
        {
            Root = UI.Rect(canvasRoot, GetType().Name).Fill();
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            Build();
            Root.gameObject.SetActive(false);
        }

        protected abstract void Build();
        public virtual void Refresh() { }
        /// <summary>Esc / back behaviour. Return true if handled.</summary>
        public virtual bool Back() => false;

        public bool Visible => Root != null && Root.gameObject.activeSelf;

        public void Show()
        {
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            _fade = 0f;
            _group.alpha = 0f;
            Refresh();
        }

        public void Hide() => Root.gameObject.SetActive(false);

        protected virtual void Update()
        {
            if (!Visible) return;
            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime * 6f);
                _group.alpha = _fade;
            }
        }

        // ------------------------------------------------------------------ shared building blocks

        /// <summary>Dark full-screen backdrop with a title bar and the player's money.</summary>
        protected Text TitleBar(string title, string subtitle = null)
        {
            var bg = UI.Box(Root, Theme.Bg, "bg");
            bg.rectTransform.Fill();
            var bar = UI.Box(Root, new Color(0.02f, 0.025f, 0.035f, 1f), "titlebar");
            bar.rectTransform.Place(0, 1, 1, 1, 0, -110, 0, 0);
            var line = UI.Box(Root, Theme.Accent, "line");
            line.rectTransform.Place(0, 1, 1, 1, 0, -113, 0, 110);
            var t = UI.Header(bar.transform, title, 44, Theme.Text);
            t.rectTransform.Place(0, 0, 0.7f, 1, 60, subtitle != null ? 34 : 0, 0, 0);
            if (subtitle != null)
            {
                var s = UI.Label(bar.transform, subtitle, 28, Theme.Dim);
                s.rectTransform.Place(0, 0, 0.7f, 0, 62, 10, 0, -44);
            }
            var money = UI.Header(bar.transform, "", 30, Theme.Gold, TextAnchor.MiddleRight);
            money.rectTransform.Place(0.6f, 0, 1, 1, 0, 0, 60, 0);
            return money;
        }

        protected static Image Card(Transform parent, float x0, float y0, float x1, float y1)
        {
            var p = UI.Panel(parent, Theme.Panel, "card");
            p.rectTransform.Place(x0, y0, x1, y1);
            return p;
        }
    }
}
