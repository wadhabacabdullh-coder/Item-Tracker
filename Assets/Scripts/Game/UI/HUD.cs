using System.Collections.Generic;
using ShadowContract.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowContract.Game
{
    /// <summary>In-mission heads-up display. Reads the simulation every frame; never modifies it.</summary>
    public sealed class HUD : MonoBehaviour
    {
        private MissionRunner _runner;
        private GameSession S => _runner?.Session;
        private Canvas _canvas;
        private RectTransform _root;

        // widgets
        private Image _hp, _armor, _detect, _reloadBar, _holdBar, _weaponIcon, _damage, _flash, _crosshair, _eye;
        private RectTransform _armorRoot, _holdRoot, _promptRoot, _crossRt, _toastRoot, _subRoot, _arrowRoot, _minimapRoot;
        private Text _hpText, _armorText, _weaponName, _ammo, _money, _detectText, _zone, _prompt, _banner, _bannerSub, _warning, _medkits, _keys;
        private Text[] _objectiveLines;
        private Image[] _slotIcons;
        private Image[] _slotFrames;
        private RawImage _minimap;
        private RectTransform _mmPlayer;
        private readonly List<RectTransform> _mmDots = new List<RectTransform>();
        private readonly List<(RectTransform rt, Text t, float life)> _toasts = new List<(RectTransform, Text, float)>();
        private readonly Dictionary<int, (RectTransform rt, Text t, float life)> _subs = new Dictionary<int, (RectTransform, Text, float)>();
        private readonly Dictionary<int, (RectTransform rt, Image bar, float dur, float t)> _radios = new Dictionary<int, (RectTransform, Image, float, float)>();
        private readonly List<Image> _arrows = new List<Image>();
        private float _bannerTime, _warnTime, _damageAlpha, _flashAlpha;
        private float _shownMoney;
        private int _profileMoney;

        /// <summary>The HUD has no clickable elements, so it never swallows shots.</summary>
        public bool PointerOverUi => false;

        public void Build()
        {
            _canvas = UI.CreateCanvas("HUD", 10, transform);
            _root = (RectTransform)_canvas.transform;

            // ---- damage vignette & flash
            _damage = UI.Box(_root, new Color(0.6f, 0.02f, 0.05f, 0f), "damage");
            _damage.rectTransform.Fill();
            _damage.raycastTarget = false;
            _damage.sprite = SpriteLibrary.Get("UI/vignette");
            _damage.color = new Color(0, 0, 0, 0);
            _flash = UI.Box(_root, new Color(0, 0, 0, 0), "flash");
            _flash.rectTransform.Fill();
            _flash.raycastTarget = false;

            // ---- top-left: zone + objectives
            var obj = UI.Panel(_root, Theme.Panel, "objectives");
            obj.rectTransform.At(0, 1, 24, -24, 560, 200, 0, 1);
            obj.raycastTarget = false;
            _zone = UI.Header(obj.transform, "", 18, Theme.Dim);
            _zone.rectTransform.At(0, 1, 18, -10, 520, 28, 0, 1);
            var accent = UI.Box(obj.transform, Theme.Accent, "accent");
            accent.rectTransform.At(0, 1, 0, -12, 4, 26, 0, 1);
            _objectiveLines = new Text[5];
            for (int i = 0; i < _objectiveLines.Length; i++)
            {
                _objectiveLines[i] = UI.Label(obj.transform, "", 30, Theme.Text);
                _objectiveLines[i].rectTransform.At(0, 1, 18, -44 - i * 32, 530, 32, 0, 1);
                _objectiveLines[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            // ---- top-right: money + detection + minimap
            var tr = UI.Panel(_root, Theme.Panel, "status");
            tr.rectTransform.At(1, 1, -24, -24, 380, 118, 1, 1);
            tr.raycastTarget = false;
            var moneyIcon = UI.Icon(tr.transform, SpriteLibrary.Icon("money"), 36, 36);
            moneyIcon.rectTransform.At(0, 1, 16, -12, 36, 36, 0, 1);
            _money = UI.Header(tr.transform, "$0", 28, Theme.Gold);
            _money.rectTransform.At(0, 1, 62, -10, 300, 40, 0, 1);
            _eye = UI.Icon(tr.transform, SpriteLibrary.Icon("eye"), 36, 36);
            _eye.rectTransform.At(0, 1, 16, -62, 36, 36, 0, 1);
            _detect = UI.Bar(tr.transform, new Color(0, 0, 0, 0.6f), Theme.Good, out var detRoot, "detection");
            detRoot.At(0, 1, 62, -66, 190, 26, 0, 1);
            _detectText = UI.Header(tr.transform, "HIDDEN", 18, Theme.Good);
            _detectText.rectTransform.At(0, 1, 262, -64, 120, 30, 0, 1);

            var mm = UI.Panel(_root, Theme.Panel, "minimap");
            mm.rectTransform.At(1, 1, -24, -154, 240, 240, 1, 1);
            _minimapRoot = mm.rectTransform;
            var mmMask = UI.Rect(mm.transform, "mask").Place(0, 0, 1, 1, 6, 6, 6, 6);
            mmMask.gameObject.AddComponent<RectMask2D>();
            _minimap = mmMask.gameObject.AddComponent<RawImage>();
            _minimap.color = new Color(0.75f, 0.78f, 0.85f, 1f);
            _mmPlayer = UI.Box(mmMask, Color.white, "you").rectTransform;
            _mmPlayer.At(0.5f, 0.5f, 0, 0, 8, 8);

            // ---- bottom-left: health / armor / medkits / keys
            var bl = UI.Panel(_root, Theme.Panel, "vitals");
            bl.rectTransform.At(0, 0, 24, 24, 470, 130, 0, 0);
            bl.raycastTarget = false;
            var hIcon = UI.Icon(bl.transform, SpriteLibrary.Icon("health"), 34, 34);
            hIcon.rectTransform.At(0, 1, 16, -16, 34, 34, 0, 1);
            _hp = UI.Bar(bl.transform, new Color(0.15f, 0.03f, 0.04f, 0.9f), new Color(0.85f, 0.2f, 0.22f), out var hpRoot, "hp");
            hpRoot.At(0, 1, 60, -18, 300, 30, 0, 1);
            _hpText = UI.Header(bl.transform, "100", 20, Theme.Text, TextAnchor.MiddleRight);
            _hpText.rectTransform.At(0, 1, 370, -16, 80, 34, 0, 1);
            var aIcon = UI.Icon(bl.transform, SpriteLibrary.Icon("shield"), 30, 30);
            aIcon.rectTransform.At(0, 1, 18, -58, 30, 30, 0, 1);
            _armor = UI.Bar(bl.transform, new Color(0.03f, 0.07f, 0.15f, 0.9f), Theme.Blue, out _armorRoot, "armor");
            _armorRoot.At(0, 1, 60, -60, 300, 22, 0, 1);
            _armorText = UI.Header(bl.transform, "0", 18, Theme.Blue, TextAnchor.MiddleRight);
            _armorText.rectTransform.At(0, 1, 370, -56, 80, 30, 0, 1);
            var mk = UI.Icon(bl.transform, SpriteLibrary.Icon("medkit"), 28, 28);
            mk.rectTransform.At(0, 1, 18, -94, 28, 28, 0, 1);
            _medkits = UI.Label(bl.transform, "x0  [H]", 26, Theme.Text);
            _medkits.rectTransform.At(0, 1, 56, -92, 160, 30, 0, 1);
            _keys = UI.Label(bl.transform, "", 26, Theme.Gold);
            _keys.rectTransform.At(0, 1, 200, -92, 260, 30, 0, 1);

            // ---- bottom-right: weapon
            var br = UI.Panel(_root, Theme.Panel, "weapon");
            br.rectTransform.At(1, 0, -24, 24, 470, 170, 1, 0);
            br.raycastTarget = false;
            _weaponIcon = UI.Icon(br.transform, null, 144, 72);
            _weaponIcon.rectTransform.At(0, 1, 16, -12, 144, 72, 0, 1);
            _weaponName = UI.Header(br.transform, "", 20, Theme.Text);
            _weaponName.rectTransform.At(0, 1, 176, -14, 280, 30, 0, 1);
            _ammo = UI.Header(br.transform, "", 40, Theme.Text);
            _ammo.rectTransform.At(0, 1, 176, -44, 280, 50, 0, 1);
            _reloadBar = UI.Bar(br.transform, new Color(0, 0, 0, 0.5f), Theme.Gold, out var rlRoot, "reload");
            rlRoot.At(0, 1, 176, -94, 270, 10, 0, 1);
            _slotIcons = new Image[5];
            _slotFrames = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var f = UI.Panel(br.transform, new Color(0.1f, 0.12f, 0.15f, 0.9f), "slot" + i);
                f.rectTransform.At(0, 0, 16 + i * 88, 12, 80, 44, 0, 0);
                f.raycastTarget = false;
                _slotFrames[i] = f;
                var num = UI.Header(f.transform, (i + 1).ToString(), 14, Theme.Dim);
                num.rectTransform.At(0, 1, 4, -2, 20, 18, 0, 1);
                _slotIcons[i] = UI.Icon(f.transform, null, 64, 32);
                _slotIcons[i].rectTransform.At(0.5f, 0.5f, 4, -2, 64, 32);
            }

            // ---- centre: prompt, toasts, banners, warnings
            _promptRoot = UI.Rect(_root, "prompt").At(0.5f, 0, 0, 200, 700, 56);
            var pbg = UI.Panel(_promptRoot, new Color(0.04f, 0.05f, 0.07f, 0.85f));
            pbg.rectTransform.Fill();
            pbg.raycastTarget = false;
            _prompt = UI.Label(_promptRoot, "", 32, Theme.Text, TextAnchor.MiddleCenter);
            _prompt.rectTransform.Fill();
            _holdBar = UI.Bar(_promptRoot, new Color(0, 0, 0, 0.6f), Theme.Gold, out _holdRoot, "hold");
            _holdRoot.At(0.5f, 0, 0, -8, 400, 10, 0.5f, 1f);

            _toastRoot = UI.Rect(_root, "toasts").At(0.5f, 0, 0, 270, 900, 240, 0.5f, 0);
            _subRoot = UI.Rect(_root, "subtitles").Fill();
            _arrowRoot = UI.Rect(_root, "arrows").Fill();

            _banner = UI.Header(_root, "", 64, Theme.Text, TextAnchor.MiddleCenter);
            _banner.rectTransform.At(0.5f, 1, 0, -170, 1400, 90);
            UI.AddShadow(_banner, 4);
            _bannerSub = UI.Label(_root, "", 36, Theme.Dim, TextAnchor.MiddleCenter);
            _bannerSub.rectTransform.At(0.5f, 1, 0, -236, 1400, 50);
            UI.AddShadow(_bannerSub, 2);
            _warning = UI.Header(_root, "", 40, Theme.Accent, TextAnchor.MiddleCenter);
            _warning.rectTransform.At(0.5f, 1, 0, -300, 1200, 60);
            UI.AddShadow(_warning, 3);

            // ---- crosshair (follows the mouse)
            _crosshair = UI.Icon(_root, SpriteLibrary.Get("UI/crosshair"), 51, 51, "crosshair");
            _crossRt = _crosshair.rectTransform;
            _crossRt.anchorMin = _crossRt.anchorMax = Vector2.zero;
            gameObject.SetActive(false);
        }

        public void Bind(MissionRunner runner)
        {
            _runner = runner;
            _profileMoney = GameManager.I.Progress.Money;
            _shownMoney = _profileMoney;
            foreach (var t in _toasts) Destroy(t.rt.gameObject);
            _toasts.Clear();
            foreach (var s in _subs.Values) Destroy(s.rt.gameObject);
            _subs.Clear();
            foreach (var r in _radios.Values) Destroy(r.rt.gameObject);
            _radios.Clear();
            foreach (var a in _arrows) Destroy(a.gameObject);
            _arrows.Clear();
            foreach (var d in _mmDots) Destroy(d.gameObject);
            _mmDots.Clear();
            _banner.text = _bannerSub.text = _warning.text = "";
            var prev = SpriteLibrary.Texture("Maps/" + runner.Session.Map.Id + "_preview");
            _minimap.texture = prev;
            gameObject.SetActive(true);
        }

        public void Show(bool on)
        {
            gameObject.SetActive(on);
            Cursor.visible = !on;
        }

        // ------------------------------------------------------------------ messages

        public void Toast(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            var rt = UI.Rect(_toastRoot, "toast");
            var bg = UI.Panel(rt, new Color(0.03f, 0.04f, 0.06f, 0.85f));
            bg.rectTransform.Fill();
            bg.raycastTarget = false;
            var t = UI.Label(rt, text, 28, color, TextAnchor.MiddleCenter);
            t.rectTransform.Fill();
            rt.At(0.5f, 0, 0, 0, Mathf.Min(880, 40 + text.Length * 14), 40, 0.5f, 0);
            _toasts.Insert(0, (rt, t, 3.2f));
            while (_toasts.Count > 4)
            {
                Destroy(_toasts[_toasts.Count - 1].rt.gameObject);
                _toasts.RemoveAt(_toasts.Count - 1);
            }
        }

        public void Banner(string title, string sub, Color color)
        {
            _banner.text = title;
            _banner.color = color;
            _bannerSub.text = sub ?? "";
            _bannerTime = 3.2f;
        }

        public void Warning(string text)
        {
            _warning.text = text;
            _warnTime = 2.4f;
        }

        public void Subtitle(int actorId, string text)
        {
            if (_subs.TryGetValue(actorId, out var old)) { Destroy(old.rt.gameObject); _subs.Remove(actorId); }
            var rt = UI.Rect(_subRoot, "sub");
            var bg = UI.Panel(rt, new Color(0.02f, 0.02f, 0.03f, 0.8f));
            bg.rectTransform.Fill();
            bg.raycastTarget = false;
            var t = UI.Label(rt, text, 26, Theme.Text, TextAnchor.MiddleCenter);
            t.rectTransform.Fill();
            rt.sizeDelta = new Vector2(Mathf.Min(560, 30 + text.Length * 12.5f), 36);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            _subs[actorId] = (rt, t, 2.6f);
        }

        public void RadioStarted(int actorId, float duration)
        {
            RadioEnded(actorId);
            var rt = UI.Rect(_subRoot, "radio");
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(160, 40);
            var t = UI.Header(rt, "RADIO", 16, Theme.Accent, TextAnchor.UpperCenter);
            t.rectTransform.Fill();
            var bar = UI.Bar(rt, new Color(0, 0, 0, 0.7f), Theme.Accent, out var br, "radioBar");
            br.At(0.5f, 0, 0, 2, 150, 12, 0.5f, 0);
            _radios[actorId] = (rt, bar, duration, 0f);
            Warning("A GUARD IS CALLING IT IN");
        }

        public void RadioEnded(int actorId)
        {
            if (_radios.TryGetValue(actorId, out var r)) { Destroy(r.rt.gameObject); _radios.Remove(actorId); }
        }

        public void Damage(float angle) => _damageAlpha = Mathf.Min(0.85f, _damageAlpha + 0.45f);
        public void Flash(float alpha) => _flashAlpha = alpha;

        // ------------------------------------------------------------------ update

        private void LateUpdate()
        {
            var s = S;
            if (s == null) return;
            float dt = Time.unscaledDeltaTime;
            var p = s.Player;
            var inv = p.Inventory;
            var cam = CameraRig.I;
            float uiScale = _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1f;

            // vitals
            _hp.fillAmount = Mathf.Clamp01(p.Health / p.MaxHealth);
            _hp.color = p.Health / p.MaxHealth < 0.3f ? Color.Lerp(new Color(0.85f, 0.2f, 0.22f), Color.white, Mathf.PingPong(Time.time * 3f, 1f) * 0.5f) : new Color(0.85f, 0.2f, 0.22f);
            _hpText.text = Mathf.CeilToInt(p.Health).ToString();
            bool hasArmor = p.MaxArmor > 0f;
            _armor.fillAmount = hasArmor ? Mathf.Clamp01(p.Armor / p.MaxArmor) : 0f;
            _armorText.text = hasArmor ? Mathf.CeilToInt(p.Armor).ToString() : "-";
            _medkits.text = $"x{inv.Medkits}  [{GameInput.Label(GameAction.Medkit)}]";
            string keys = "";
            if (inv.Keys.Contains("blue")) keys += "<color=#7fb0e0>BLUE KEY</color>  ";
            if (inv.Keys.Contains("red")) keys += "<color=#ef7a6a>RED KEY</color>";
            _keys.text = keys;

            // weapon
            var w = inv.CurrentWeapon;
            if (w != null)
            {
                _weaponIcon.sprite = SpriteLibrary.WeaponIcon(w.Def.Id);
                _weaponName.text = w.Def.Name.ToUpperInvariant() + (w.Def.Suppressed && w.Def.Id != "silenced_pistol" ? " SD" : "");
                if (w.Def.IsGun)
                {
                    int reserve = inv.GetReserve(w.Def.Ammo);
                    _ammo.text = $"{w.Mag}<size=26><color=#8d9bab> / {reserve}</color></size>";
                    _ammo.color = w.Mag == 0 ? Theme.Accent : Theme.Text;
                }
                else if (w.Def.IsThrown) _ammo.text = $"x{inv.GetReserve(w.Def.Ammo)}";
                else _ammo.text = "<size=26><color=#8d9bab>MELEE</color></size>";
                _reloadBar.fillAmount = w.Reloading ? w.ReloadProgress : 0f;
                _reloadBar.transform.parent.gameObject.SetActive(w.Reloading);
            }
            for (int i = 0; i < 5; i++)
            {
                var sw = inv.Slots[i];
                _slotIcons[i].sprite = sw != null ? SpriteLibrary.WeaponIcon(sw.Def.Id) : null;
                _slotIcons[i].enabled = sw != null;
                _slotFrames[i].color = i == inv.Current ? new Color(0.42f, 0.1f, 0.13f, 0.95f) : new Color(0.1f, 0.12f, 0.15f, 0.9f);
            }

            // money (profile money + cash found this mission)
            int money = _profileMoney + inv.CashFound;
            _shownMoney = Mathf.MoveTowards(_shownMoney, money, Mathf.Max(20f, Mathf.Abs(money - _shownMoney) * 4f) * dt);
            _money.text = UI.Money(Mathf.RoundToInt(_shownMoney));

            // detection
            float det = s.DetectionLevel;
            _detect.fillAmount = s.Alert == AlertLevel.Combat ? 1f : det;
            string status;
            Color sc;
            switch (s.Alert)
            {
                case AlertLevel.Combat: status = "COMBAT"; sc = Theme.Accent; break;
                case AlertLevel.Alarmed: status = "SEARCHING"; sc = Theme.Warn; break;
                default:
                    if (p.Hidden || p.InVent) { status = "HIDDEN"; sc = Theme.Good; }
                    else if (det > 0.5f) { status = "DETECTED?"; sc = Theme.Warn; }
                    else if (det > 0.05f) { status = "NOTICED"; sc = new Color(1f, 0.9f, 0.5f); }
                    else { status = "UNSEEN"; sc = Theme.Good; }
                    break;
            }
            _detectText.text = status;
            _detectText.color = sc;
            _detect.color = Color.Lerp(Theme.Good, Theme.Accent, s.Alert == AlertLevel.Combat ? 1f : det);
            _eye.color = det > 0.05f || s.Alert >= AlertLevel.Alarmed ? Color.Lerp(Color.white, Theme.Accent, Mathf.PingPong(Time.time * 4f, 1f)) : new Color(1, 1, 1, 0.6f);

            // objectives
            _zone.text = s.CurrentZoneName().ToUpperInvariant();
            int line = 0;
            var current = s.CurrentObjective;
            foreach (var (def, done) in s.Objectives())
            {
                if (line >= _objectiveLines.Length) break;
                if (def.Type == ObjectiveType.Extract && !s.RequiredDone) continue;
                string mark = done ? "<color=#73d97f>[x]</color> " : def == current ? "<color=#d13f3f>></color> " : "<color=#6b7b8c>-</color> ";
                string col = done ? "#6b7b8c" : def.Optional ? "#b5c0cc" : "#e1e6ec";
                _objectiveLines[line].text = mark + $"<color={col}>{def.Text}</color>";
                line++;
            }
            for (int i = line; i < _objectiveLines.Length; i++) _objectiveLines[i].text = "";

            // interaction prompt
            var c = s.Candidate;
            bool showPrompt = c.Kind != CandidateKind.None && !string.IsNullOrEmpty(c.Prompt);
            if (p.Hidden) { showPrompt = true; c.Prompt = "Leave hiding spot"; c.Enabled = true; }
            _promptRoot.gameObject.SetActive(showPrompt || p.ActionLabel != null);
            if (p.ActionLabel != null && !showPrompt) _prompt.text = p.ActionLabel.ToUpperInvariant() + "...";
            else if (showPrompt)
                _prompt.text = c.Enabled ? $"<color=#f0c75e>[{GameInput.Label(GameAction.Interact)}]</color> {c.Prompt}{(c.HoldTime > 0 ? " (hold)" : "")}" : $"<color=#8d9bab>{c.Prompt}</color>";
            _holdRoot.gameObject.SetActive(s.HoldProgress > 0f);
            _holdBar.fillAmount = s.HoldProgress;

            // crosshair: gap follows the current spread
            Vector2 mouse = GameInput.MousePosition;
            _crossRt.anchoredPosition = mouse / uiScale;
            float spread = w != null && w.Def.IsGun ? w.CurrentSpread(p.Moving, p.Aiming) : 1f;
            float cs = 34f + spread * 3.2f;
            _crossRt.sizeDelta = new Vector2(cs, cs);
            _crosshair.color = s.Candidate.Kind != CandidateKind.None ? Theme.Gold : Color.white;
            bool menuOpen = _runner.Paused || _runner.Ended;
            _crosshair.enabled = !menuOpen;
            Cursor.visible = menuOpen;

            // toasts
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var (rt, t, life) = _toasts[i];
                life -= dt;
                if (life <= 0f) { Destroy(rt.gameObject); _toasts.RemoveAt(i); continue; }
                _toasts[i] = (rt, t, life);
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0, i * 46), dt * 12f);
                var cg = t.color;
                cg.a = Mathf.Clamp01(life * 2f);
                t.color = cg;
            }

            // subtitles & radio bars above NPCs
            var ids = new List<int>(_subs.Keys);
            foreach (int id in ids)
            {
                var (rt, t, life) = _subs[id];
                life -= dt;
                var npc = s.Npcs.Find(n => n.Id == id);
                if (life <= 0f || npc == null || npc.Down) { Destroy(rt.gameObject); _subs.Remove(id); continue; }
                _subs[id] = (rt, t, life);
                Vector2 sp = cam.WorldToScreen(new Vector2(npc.Pos.x, npc.Pos.y + 1.6f));
                rt.anchoredPosition = sp / uiScale;
                bool onScreen = sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height;
                rt.gameObject.SetActive(onScreen);
            }
            ids = new List<int>(_radios.Keys);
            foreach (int id in ids)
            {
                var (rt, bar, dur, t) = _radios[id];
                t += Time.deltaTime;
                var npc = s.Npcs.Find(n => n.Id == id);
                if (npc == null || npc.Down || t > dur + 0.2f) { Destroy(rt.gameObject); _radios.Remove(id); continue; }
                _radios[id] = (rt, bar, dur, t);
                bar.fillAmount = Mathf.Clamp01(t / dur);
                Vector2 sp = cam.WorldToScreen(new Vector2(npc.Pos.x, npc.Pos.y + 2.2f));
                rt.anchoredPosition = sp / uiScale;
            }

            UpdateArrows(s, cam, uiScale);
            UpdateMinimap(s);

            // banner / warning / vignette fades
            _bannerTime -= dt;
            float ba = Mathf.Clamp01(_bannerTime);
            _banner.color = new Color(_banner.color.r, _banner.color.g, _banner.color.b, ba);
            _bannerSub.color = new Color(Theme.Dim.r, Theme.Dim.g, Theme.Dim.b, ba);
            _warnTime -= dt;
            _warning.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, _warnTime > 0 ? (0.6f + 0.4f * Mathf.PingPong(Time.unscaledTime * 5f, 1f)) * Mathf.Clamp01(_warnTime) : 0f);
            float lowHp = p.Health / p.MaxHealth < 0.3f ? 0.25f + 0.1f * Mathf.Sin(Time.time * 5f) : 0f;
            _damageAlpha = Mathf.MoveTowards(_damageAlpha, 0f, dt * 1.5f);
            _damage.color = new Color(0.7f, 0.02f, 0.05f, Mathf.Max(_damageAlpha * 0.6f, lowHp));
            _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, dt * 2f);
            _flash.color = new Color(0, 0, 0, _flashAlpha);
        }

        /// <summary>Screen-edge arrows towards living targets that are off screen.</summary>
        private void UpdateArrows(GameSession s, CameraRig cam, float uiScale)
        {
            int k = 0;
            foreach (var n in s.Npcs)
            {
                if (!n.IsTarget || n.Down) continue;
                Vector2 sp = cam.WorldToScreen(new Vector2(n.Pos.x, n.Pos.y));
                bool on = sp.x > 40 && sp.x < Screen.width - 40 && sp.y > 40 && sp.y < Screen.height - 40;
                if (k >= _arrows.Count)
                {
                    var img = UI.Icon(_arrowRoot, SpriteLibrary.Get("UI/target_marker"), 36, 28, "arrow");
                    img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                    _arrows.Add(img);
                }
                var a = _arrows[k++];
                a.enabled = !on;
                if (on) continue;
                Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 dir = (sp - center).normalized;
                float m = Mathf.Min((Screen.width * 0.5f - 60) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (Screen.height * 0.5f - 60) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                a.rectTransform.anchoredPosition = (center + dir * m) / uiScale;
                a.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);
            }
            for (int i = k; i < _arrows.Count; i++) _arrows[i].enabled = false;
        }

        private void UpdateMinimap(GameSession s)
        {
            if (_minimap.texture == null) return;
            const float span = 36f; // tiles visible
            float W = s.World.Width, H = s.World.Height;
            var p = s.Player.Pos;
            _minimap.uvRect = new UnityEngine.Rect((p.x - span / 2) / W, (p.y - span / 2) / H, span / W, span / H);
            var root = (RectTransform)_minimap.transform;
            float size = root.rect.width;
            int k = 0;
            void Dot(Vec2 pos, Color c, float sz)
            {
                if (k >= _mmDots.Count)
                {
                    var d = UI.Box(root, Color.white, "dot");
                    d.raycastTarget = false;
                    _mmDots.Add(d.rectTransform);
                }
                var rt = _mmDots[k++];
                Vector2 rel = new Vector2(pos.x - p.x, pos.y - p.y) / span * size;
                bool inside = Mathf.Abs(rel.x) < size / 2 && Mathf.Abs(rel.y) < size / 2;
                rt.gameObject.SetActive(inside);
                rt.At(0.5f, 0.5f, rel.x, rel.y, sz, sz);
                rt.GetComponent<Image>().color = c;
            }
            foreach (var n in s.Npcs)
            {
                if (n.Down) continue;
                if (n.IsTarget) Dot(n.Pos, Theme.Accent, 10);
                else if (n.IsCamera) continue;
                else if (n.IsHostile && (n.State == AIState.Attack || n.State == AIState.Chase || n.State == AIState.Search || n.State == AIState.Investigate)) Dot(n.Pos, Theme.Warn, 6);
            }
            foreach (var pk in s.Pickups)
                if (!pk.Taken && (pk.Kind == PickupKind.Intel || pk.Kind == PickupKind.Keycard)) Dot(pk.Pos, Theme.Gold, 8);
            if (s.RequiredDone)
                foreach (var e in s.Extracts) Dot(e.Rect.Center, Theme.Good, 12);
            for (int i = k; i < _mmDots.Count; i++) _mmDots[i].gameObject.SetActive(false);
            _mmPlayer.SetAsLastSibling();
            _mmPlayer.localRotation = Quaternion.Euler(0, 0, s.Player.Facing * Mathf.Rad2Deg);
        }
    }
}
