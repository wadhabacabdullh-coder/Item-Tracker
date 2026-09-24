using System;
using System.Collections.Generic;
using ShadowContract.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowContract.Game
{
    public sealed class SettingsScreen : UIScreen
    {
        private readonly List<(Text text, Func<string> value)> _values = new List<(Text, Func<string>)>();
        private readonly Dictionary<GameAction, Text> _keyLabels = new Dictionary<GameAction, Text>();
        private GameAction? _waiting;
        private int _captureDelay;
        private Text _hint;
        private Action _onBack;

        public void SetReturn(Action onBack) => _onBack = onBack;

        private static readonly (GameAction a, string name)[] Actions =
        {
            (GameAction.MoveUp, "Move up"), (GameAction.MoveDown, "Move down"), (GameAction.MoveLeft, "Move left"), (GameAction.MoveRight, "Move right"),
            (GameAction.Attack, "Attack / shoot"), (GameAction.Aim, "Aim / subdue"), (GameAction.Reload, "Reload"), (GameAction.Interact, "Interact"),
            (GameAction.Sprint, "Sprint"), (GameAction.Crouch, "Crouch (C also works)"), (GameAction.Medkit, "Use medkit"),
            (GameAction.Slot1, "Weapon slot 1"), (GameAction.Slot2, "Weapon slot 2"), (GameAction.Slot3, "Weapon slot 3"), (GameAction.Slot4, "Weapon slot 4"),
            (GameAction.Slot5, "Weapon slot 5"), (GameAction.Inventory, "Inventory"), (GameAction.Pause, "Pause"),
        };

        protected override void Build()
        {
            TitleBar("SETTINGS", "Changes are saved automatically");
            var left = UI.Panel(Root, Theme.Panel, "general");
            left.rectTransform.Place(0, 0, 0.5f, 1, 40, 130, 8, 140);
            var lc = UI.ScrollList(left.transform, 6, out var lsr);
            ((RectTransform)lsr.transform).Place(0, 0, 1, 1, 16, 16, 16, 16);
            var s = GM.Settings;

            Section(lc, "AUDIO");
            Selector(lc, "Master volume", () => Pct(s.MasterVolume), d => s.MasterVolume = Step(s.MasterVolume, d));
            Selector(lc, "Music volume", () => Pct(s.MusicVolume), d => s.MusicVolume = Step(s.MusicVolume, d));
            Selector(lc, "Effects volume", () => Pct(s.SfxVolume), d => s.SfxVolume = Step(s.SfxVolume, d));
            Section(lc, "GAMEPLAY");
            Selector(lc, "Difficulty", () => s.Difficulty.ToString(), d => s.Difficulty = (Difficulty)(((int)s.Difficulty + d + 3) % 3));
            Selector(lc, "Screen shake", () => s.ScreenShake ? "On" : "Off", d => s.ScreenShake = !s.ScreenShake);
            Selector(lc, "Vision cones", () => s.ShowVisionCones ? "On" : "Off", d => s.ShowVisionCones = !s.ShowVisionCones);
            Section(lc, "DISPLAY");
            Selector(lc, "Fullscreen", () => s.Fullscreen ? "On" : "Off", d => s.Fullscreen = !s.Fullscreen);
            var diffNote = UI.Label(lc, "Easy: slower detection, weaker guards.  Hard: sharper eyes, deadlier aim.", 26, Theme.Dim);
            UI.Size(diffNote, -1, 70);

            var right = UI.Panel(Root, Theme.Panel, "controls");
            right.rectTransform.Place(0.5f, 0, 1, 1, 8, 130, 40, 140);
            var rc = UI.ScrollList(right.transform, 4, out var rsr);
            ((RectTransform)rsr.transform).Place(0, 0, 1, 1, 16, 90, 16, 16);
            Section(rc, "CONTROLS  (click a key, then press a new one)");
            foreach (var (a, name) in Actions)
            {
                var row = UI.Rect(rc, "bind");
                UI.Size(row, -1, 50);
                var l = UI.Label(row, name, 30, Theme.Text);
                l.rectTransform.Place(0, 0, 0.6f, 1, 12, 0, 0, 0);
                var act = a;
                var b = UI.Button(row, GameInput.Label(a), () => BeginCapture(act), 26);
                ((RectTransform)b.transform).Place(0.6f, 0, 1, 1, 0, 4, 8, 4);
                _keyLabels[a] = b.GetComponent<UIButtonFx>().Label;
            }
            var reset = UI.Button(right.transform, "Reset controls", () =>
            {
                GameInput.ResetDefaults();
                SaveAll();
            }, 26);
            ((RectTransform)reset.transform).At(0, 0, 16, 16, 320, 60, 0, 0);
            _hint = UI.Header(right.transform, "", 18, Theme.Warn, TextAnchor.MiddleRight);
            _hint.rectTransform.At(1, 0, -20, 30, 500, 40, 1, 0);

            var back = UI.Button(Root, "Back", () => Back(), 30);
            ((RectTransform)back.transform).At(0, 0, 40, 40, 220, 64, 0, 0);
        }

        private static string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";
        private static float Step(float v, int d) => Mathf.Clamp01(Mathf.Round(v * 10f + d) / 10f);

        private static void Section(Transform parent, string title)
        {
            var h = UI.Header(parent, title, 22, Theme.Accent);
            UI.Size(h, -1, 50);
        }

        private void Selector(Transform parent, string label, Func<string> value, Action<int> change)
        {
            Text t = null;
            t = UI.Selector(parent, label, value, d =>
            {
                change(d);
                SaveAll();
            });
            _values.Add((t, value));
        }

        private void SaveAll()
        {
            GM.Settings.Bindings = GameInput.Save();
            GM.ApplySettings();
            GM.SaveSettings();
            RefreshValues();
        }

        private void RefreshValues()
        {
            foreach (var (t, v) in _values) t.text = v();
            foreach (var kv in _keyLabels) kv.Value.text = _waiting == kv.Key ? "PRESS A KEY..." : GameInput.Label(kv.Key).ToUpperInvariant();
        }

        public override void Refresh()
        {
            _waiting = null;
            _hint.text = "";
            RefreshValues();
        }

        private void BeginCapture(GameAction a)
        {
            _waiting = a;
            _captureDelay = 2;
            _hint.text = "Press a key or mouse button (Esc cancels)";
            RefreshValues();
        }

        public override bool Back()
        {
            if (_waiting != null) { _waiting = null; _hint.text = ""; RefreshValues(); return true; }
            if (_onBack != null) { var cb = _onBack; _onBack = null; cb(); }
            else GM.ShowMainMenu();
            return true;
        }

        public bool Capturing => _waiting != null;

        protected override void Update()
        {
            base.Update();
            if (_waiting == null) return;
            if (_captureDelay > 0) { _captureDelay--; return; }
            if (!GameInput.AnyKeyDown(out var key)) return;
            if (key == KeyCode.Escape && _waiting != GameAction.Pause)
            {
                _waiting = null;
                _hint.text = "";
                RefreshValues();
                return;
            }
            GameInput.Set(_waiting.Value, key);
            _waiting = null;
            _hint.text = "";
            SaveAll();
        }
    }

    public sealed class PauseScreen : UIScreen
    {
        private Text _objectives, _stats;

        protected override void Build()
        {
            var dim = UI.Box(Root, new Color(0, 0, 0, 0.7f), "dim");
            dim.rectTransform.Fill();
            var card = UI.Panel(Root, Theme.Panel, "card");
            card.rectTransform.At(0.5f, 0.5f, 0, 0, 1100, 640);
            var t = UI.Header(card.transform, "PAUSED", 56, Theme.Text);
            t.rectTransform.At(0, 1, 40, -30, 600, 70, 0, 1);
            var line = UI.Box(card.transform, Theme.Accent, "line");
            line.rectTransform.At(0, 1, 40, -104, 300, 4, 0, 1);
            var col = UI.Rect(card.transform, "buttons").At(0, 1, 40, -140, 400, 460, 0, 1);
            UI.VList(col, 12);
            UI.Size(UI.Button(col, "Resume", () => GM.ResumeMission(), 34, true), -1, 72);
            UI.Size(UI.Button(col, "Restart contract", () => GM.RestartMission(), 30), -1, 64);
            UI.Size(UI.Button(col, "Settings", () => GM.ShowSettings(() => GM.PauseMission()), 30), -1, 64);
            UI.Size(UI.Button(col, "Abandon contract", () => GM.AbandonMission(), 30), -1, 64);
            _objectives = UI.Label(card.transform, "", 30, Theme.Text, TextAnchor.UpperLeft);
            _objectives.rectTransform.At(0, 1, 480, -130, 580, 330, 0, 1);
            _stats = UI.Label(card.transform, "", 28, Theme.Dim, TextAnchor.UpperLeft);
            _stats.rectTransform.At(0, 1, 480, -460, 580, 160, 0, 1);
        }

        public override bool Back() { GM.ResumeMission(); return true; }

        public override void Refresh()
        {
            var s = GM.Runner?.Session;
            if (s == null) return;
            var sb = new System.Text.StringBuilder($"<color=#d13f3f>{s.Mission.Name.ToUpperInvariant()}</color>\n");
            foreach (var (def, done) in s.Objectives())
                sb.Append(done ? "<color=#73d97f>[x]</color> " : "[ ] ").Append(def.Text).Append('\n');
            _objectives.text = sb.ToString();
            _stats.text = $"Time {UI.Time(s.Time)}   Kills {s.Kills}   Subdued {s.Subdued}\n" +
                          $"Status: {(s.Spotted ? "<color=#d13f3f>Spotted</color>" : "<color=#73d97f>Undetected</color>")}   Bodies found: {s.BodiesFound}\n" +
                          $"Location: {s.CurrentZoneName()}";
        }
    }
}
