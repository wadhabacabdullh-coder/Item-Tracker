using ShadowContract.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowContract.Game
{
    public sealed class MainMenuScreen : UIScreen
    {
        private Text _money, _stats, _confirm;
        private Button _continue, _reset;
        private float _confirmTimer;

        protected override void Build()
        {
            var back = UI.Icon(Root, SpriteLibrary.Get("UI/backdrop"), 1920, 1080, "backdrop");
            back.preserveAspect = false;
            back.rectTransform.Fill();
            var shade = UI.Box(Root, new Color(0, 0, 0, 0.35f), "shade");
            shade.rectTransform.Fill();
            var side = UI.Box(Root, new Color(0.02f, 0.025f, 0.035f, 0.82f), "side");
            side.rectTransform.Place(0, 0, 0, 1, 0, 0, -720, 0);
            var edge = UI.Box(Root, Theme.Accent, "edge");
            edge.rectTransform.Place(0, 0, 0, 1, 720, 0, -724, 0);

            var title = UI.Header(Root, "SHADOW", 110, Theme.Text);
            title.rectTransform.At(0, 1, 80, -120, 700, 120, 0, 1);
            UI.AddShadow(title, 5);
            var title2 = UI.Header(Root, "CONTRACT", 110, Theme.Accent);
            title2.rectTransform.At(0, 1, 80, -230, 700, 120, 0, 1);
            UI.AddShadow(title2, 5);
            var tag = UI.Label(Root, "Every target has a routine. Learn it.", 34, Theme.Dim);
            tag.rectTransform.At(0, 1, 86, -350, 640, 40, 0, 1);

            var list = UI.Rect(Root, "menu").At(0, 1, 80, -420, 520, 560, 0, 1);
            UI.VList(list, 14);
            _continue = UI.Button(list, "Contracts", () => GM.ShowMissionSelect(), 38, true);
            UI.Size(_continue, -1, 76);
            UI.Size(UI.Button(list, "Loadout", () => GM.ShowLoadout(null), 34), -1, 66);
            UI.Size(UI.Button(list, "Shop", () => GM.ShowShop(), 34), -1, 66);
            UI.Size(UI.Button(list, "Settings", () => GM.ShowSettings(), 34), -1, 66);
            UI.Size(UI.Button(list, "Quit", () => GM.Quit(), 34), -1, 66);

            _money = UI.Header(Root, "", 32, Theme.Gold);
            _money.rectTransform.At(0, 0, 86, 120, 600, 40, 0, 0);
            _stats = UI.Label(Root, "", 28, Theme.Dim);
            _stats.rectTransform.At(0, 0, 86, 70, 620, 40, 0, 0);

            _reset = UI.Button(Root, "New profile", OnReset, 22);
            ((RectTransform)_reset.transform).At(1, 0, -40, 40, 260, 50, 1, 0);
            _confirm = UI.Label(Root, "", 26, Theme.Warn, TextAnchor.MiddleRight);
            _confirm.rectTransform.At(1, 0, -40, 96, 700, 40, 1, 0);
            var ver = UI.Label(Root, "v1.0  -  WASD move  |  Mouse aim  |  LMB attack  |  E interact  |  Esc pause", 24, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleRight);
            ver.rectTransform.At(1, 1, -40, -30, 1000, 30, 1, 1);
        }

        public override void Refresh()
        {
            var p = GM.Progress;
            _money.text = "FUNDS  " + UI.Money(p.Money);
            _stats.text = $"Contracts completed: {p.CompletedMissions.Count}/{MissionCatalog.All.Count}    Total earned: {UI.Money(p.TotalEarned)}";
            _confirm.text = "";
            _confirmTimer = 0f;
            UI.SetLabel(_reset, "New profile");
        }

        private void OnReset()
        {
            if (_confirmTimer <= 0f)
            {
                _confirmTimer = 4f;
                _confirm.text = "This erases all progress. Click again to confirm.";
                UI.SetLabel(_reset, "Confirm reset");
                return;
            }
            GM.ResetProgress();
            Refresh();
            _confirm.text = "Profile reset.";
        }

        protected override void Update()
        {
            base.Update();
            if (_confirmTimer > 0f)
            {
                _confirmTimer -= Time.unscaledDeltaTime;
                if (_confirmTimer <= 0f) { _confirm.text = ""; UI.SetLabel(_reset, "New profile"); }
            }
        }
    }

    public sealed class MissionSelectScreen : UIScreen
    {
        private Text _money, _name, _location, _reward, _briefing, _objectives, _record, _stars;
        private RawImage _preview;
        private RectTransform _list;
        private Button _start, _loadout;
        private MissionDef _selected;

        protected override void Build()
        {
            _money = TitleBar("CONTRACTS", "Choose your next target");
            var left = Card(Root, 0, 0, 0, 1);
            left.rectTransform.offsetMin = new Vector2(40, 130);
            left.rectTransform.offsetMax = new Vector2(620, -140);
            _list = UI.Rect(left.transform, "list").Place(0, 0, 1, 1, 16, 16, 16, 16);
            UI.VList(_list, 10);

            var right = Card(Root, 0, 0, 1, 1);
            right.rectTransform.offsetMin = new Vector2(650, 130);
            right.rectTransform.offsetMax = new Vector2(-40, -140);
            var rt = right.transform;
            var prevFrame = UI.Panel(rt, new Color(0, 0, 0, 0.6f), "previewFrame");
            prevFrame.rectTransform.At(0, 1, 24, -24, 560, 380, 0, 1);
            _preview = UI.Rect(prevFrame.transform, "preview").Place(0, 0, 1, 1, 6, 6, 6, 6).gameObject.AddComponent<RawImage>();
            _name = UI.Header(rt, "", 46, Theme.Text);
            _name.rectTransform.Place(0, 1, 1, 1, 610, -84, 24, 24);
            _location = UI.Label(rt, "", 32, Theme.Dim);
            _location.rectTransform.Place(0, 1, 1, 1, 612, -126, 24, 84);
            _stars = UI.Header(rt, "", 24, Theme.Accent);
            _stars.rectTransform.Place(0, 1, 1, 1, 612, -166, 24, 128);
            _reward = UI.Header(rt, "", 30, Theme.Gold);
            _reward.rectTransform.Place(0, 1, 1, 1, 612, -214, 24, 170);
            _record = UI.Label(rt, "", 28, Theme.Good);
            _record.rectTransform.Place(0, 1, 1, 1, 612, -300, 24, 220);
            _record.verticalOverflow = VerticalWrapMode.Overflow;
            _briefing = UI.Label(rt, "", 30, Theme.Text, TextAnchor.UpperLeft);
            _briefing.rectTransform.Place(0, 0, 1, 1, 28, 280, 28, 430);
            _objectives = UI.Label(rt, "", 30, Theme.Mist, TextAnchor.UpperLeft);
            _objectives.rectTransform.Place(0, 0, 1, 0, 28, 110, 28, -270);

            _start = UI.Button(rt, "Start contract", () => { if (_selected != null) GM.StartMission(_selected.Id); }, 36, true);
            ((RectTransform)_start.transform).At(1, 0, -24, 24, 380, 72, 1, 0);
            _loadout = UI.Button(rt, "Loadout", () => { if (_selected != null) GM.ShowLoadout(_selected.Id); }, 32);
            ((RectTransform)_loadout.transform).At(1, 0, -420, 24, 260, 72, 1, 0);
            var shop = UI.Button(rt, "Shop", () => GM.ShowShop(), 32);
            ((RectTransform)shop.transform).At(1, 0, -696, 24, 200, 72, 1, 0);
            var back = UI.Button(Root, "Back", () => GM.ShowMainMenu(), 30);
            ((RectTransform)back.transform).At(0, 0, 40, 40, 220, 64, 0, 0);
        }

        public override bool Back() { GM.ShowMainMenu(); return true; }

        public override void Refresh()
        {
            var p = GM.Progress;
            _money.text = UI.Money(p.Money);
            UI.Clear(_list);
            MissionDef firstOpen = null;
            foreach (var m in MissionCatalog.All)
            {
                bool unlocked = p.IsUnlocked(m.Id);
                bool done = p.IsCompleted(m.Id);
                if (unlocked && firstOpen == null && !done) firstOpen = m;
                var mm = m;
                string label = unlocked ? m.Name : "Locked";
                var b = UI.Button(_list, label, () => { if (p.IsUnlocked(mm.Id)) Select(mm); }, 30);
                UI.Size(b, -1, 88);
                var bt = b.transform;
                var fx = b.GetComponent<UIButtonFx>();
                fx.Label.alignment = TextAnchor.UpperLeft;
                fx.Label.rectTransform.Place(0, 0, 1, 1, 20, 0, 20, 12);
                var sub = UI.Label(bt, unlocked ? $"{MissionCatalog.Get(m.Id).Location}" : $"Complete \"{MissionCatalog.Get(m.UnlockedBy)?.Name}\"", 26, Theme.Dim);
                sub.rectTransform.Place(0, 0, 1, 0, 20, 6, 120, -40);
                if (done)
                {
                    var check = UI.Icon(bt, SpriteLibrary.Icon("check"), 36, 36);
                    check.rectTransform.At(1, 0.5f, -20, 0, 36, 36, 1, 0.5f);
                }
                else if (!unlocked)
                {
                    var lk = UI.Icon(bt, SpriteLibrary.Icon("lock"), 34, 34);
                    lk.rectTransform.At(1, 0.5f, -20, 0, 34, 34, 1, 0.5f);
                    UI.SetEnabled(b, false);
                }
            }
            Select(_selected != null && p.IsUnlocked(_selected.Id) ? _selected : firstOpen ?? MissionCatalog.All.Find(m => p.IsUnlocked(m.Id)));
        }

        private void Select(MissionDef m)
        {
            _selected = m;
            if (m == null) return;
            var p = GM.Progress;
            _name.text = m.Name.ToUpperInvariant();
            _location.text = m.Location;
            _stars.text = "DIFFICULTY  " + new string('*', m.Difficulty) + new string('.', 3 - m.Difficulty);
            _reward.text = "PAYOUT  " + UI.Money(m.Reward) + "  + bonuses";
            _briefing.text = m.Briefing;
            var sb = new System.Text.StringBuilder("<color=#d13f3f>OBJECTIVES</color>\n");
            foreach (var o in m.Objectives) sb.Append(o.Optional ? "  <color=#8d9bab>(optional)</color> " : "  - ").Append(o.Text).Append('\n');
            sb.Append("<color=#8d9bab>Bonuses: ");
            sb.Append(string.Join(" | ", m.Challenges.ConvertAll(c => c.Name)));
            sb.Append("</color>");
            _objectives.text = sb.ToString();
            if (p.Records.TryGetValue(m.Id, out var rec))
                _record.text = $"Best rating: {rec.BestRating}\nBest time: {UI.Time(rec.BestTime)}" + (rec.SilentAssassin ? "   <color=#f0c75e>SILENT ASSASSIN</color>" : "");
            else _record.text = "<color=#8d9bab>Not yet completed</color>";
            _preview.texture = SpriteLibrary.Texture("Maps/" + m.MapId + "_preview");
            UI.SetEnabled(_start, p.IsUnlocked(m.Id));
        }
    }
}
