using System.Text;
using ShadowContract.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowContract.Game
{
    /// <summary>In-mission inventory (Tab): carried weapons, ammo, keys, intel, objectives and a map.</summary>
    public sealed class InventoryScreen : UIScreen
    {
        private RectTransform _weapons;
        private Text _items, _objectives;
        private RawImage _map;
        private RectTransform _you;

        protected override void Build()
        {
            var dim = UI.Box(Root, new Color(0, 0, 0, 0.75f), "dim");
            dim.rectTransform.Fill();
            var t = UI.Header(Root, "INVENTORY", 44, Theme.Text);
            t.rectTransform.At(0, 1, 60, -40, 800, 60, 0, 1);
            var hint = UI.Label(Root, "Press Tab or Esc to close", 28, Theme.Dim, TextAnchor.MiddleRight);
            hint.rectTransform.At(1, 1, -60, -44, 600, 50, 1, 1);

            var wp = UI.Panel(Root, Theme.Panel, "weapons");
            wp.rectTransform.Place(0, 0, 0.36f, 1, 60, 60, 8, 130);
            _weapons = UI.Rect(wp.transform, "list").Place(0, 0, 1, 1, 16, 16, 16, 16);
            UI.VList(_weapons, 8);

            var ip = UI.Panel(Root, Theme.Panel, "items");
            ip.rectTransform.Place(0.36f, 0.45f, 0.62f, 1, 8, 8, 8, 130);
            _items = UI.Label(ip.transform, "", 30, Theme.Text, TextAnchor.UpperLeft);
            _items.rectTransform.Place(0, 0, 1, 1, 20, 16, 20, 16);
            var op = UI.Panel(Root, Theme.Panel, "objectives");
            op.rectTransform.Place(0.36f, 0, 0.62f, 0.45f, 8, 60, 8, 8);
            _objectives = UI.Label(op.transform, "", 30, Theme.Text, TextAnchor.UpperLeft);
            _objectives.rectTransform.Place(0, 0, 1, 1, 20, 16, 20, 16);

            var mp = UI.Panel(Root, Theme.Panel, "map");
            mp.rectTransform.Place(0.62f, 0, 1, 1, 8, 60, 60, 130);
            var mapRoot = UI.Rect(mp.transform, "mapimg").Place(0, 0, 1, 1, 12, 12, 12, 12);
            _map = mapRoot.gameObject.AddComponent<RawImage>();
            _you = UI.Box(mapRoot, Theme.Accent, "you").rectTransform;
        }

        public override bool Back() { GM.ToggleInventory(); return true; }

        public override void Refresh()
        {
            var s = GM.Runner?.Session;
            if (s == null) return;
            var inv = s.Player.Inventory;
            UI.Clear(_weapons);
            for (int i = 0; i < 5; i++)
            {
                var w = inv.Slots[i];
                var row = UI.Panel(_weapons, i == inv.Current ? new Color(0.25f, 0.08f, 0.1f, 0.95f) : new Color(0.1f, 0.12f, 0.15f, 0.9f), "slot");
                UI.Size(row, -1, 110);
                var n = UI.Header(row.transform, (i + 1).ToString(), 20, Theme.Dim);
                n.rectTransform.At(0, 1, 10, -8, 30, 24, 0, 1);
                if (w == null) { var e = UI.Label(row.transform, "empty", 28, Theme.Dim); e.rectTransform.Place(0, 0, 1, 1, 50, 0, 0, 0); continue; }
                var ic = UI.Icon(row.transform, SpriteLibrary.WeaponIcon(w.Def.Id), 160, 80);
                ic.rectTransform.At(0, 0.5f, 40, 0, 160, 80, 0, 0.5f);
                string ammo = w.Def.IsGun ? $"{w.Mag} / {inv.GetReserve(w.Def.Ammo)} {GameSession.AmmoName(w.Def.Ammo)}" : w.Def.IsThrown ? $"x{inv.GetReserve(w.Def.Ammo)}" : "melee";
                var l = UI.Label(row.transform, $"{w.Def.Name}\n<color=#8d9bab>{ammo}</color>", 28, Theme.Text);
                l.rectTransform.Place(0, 0, 1, 1, 220, 0, 10, 0);
            }
            var sb = new StringBuilder("<color=#d13f3f>ITEMS</color>\n");
            sb.Append($"Medkits: {inv.Medkits}  (press {GameInput.Label(GameAction.Medkit)})\n");
            sb.Append($"Armor: {Mathf.CeilToInt(s.Player.Armor)}/{Mathf.CeilToInt(s.Player.MaxArmor)}\n");
            sb.Append($"Keycards: {(inv.Keys.Count == 0 ? "none" : string.Join(", ", inv.Keys))}\n");
            sb.Append($"Intel: {(inv.Intel.Count == 0 ? "none" : string.Join(", ", inv.Intel))}\n");
            sb.Append($"Cash found: <color=#f0c75e>{UI.Money(inv.CashFound)}</color>\n");
            sb.Append(s.Player.DraggingBody >= 0 ? "<color=#ffb04d>Dragging a body</color>\n" : "");
            _items.text = sb.ToString();
            var ob = new StringBuilder("<color=#d13f3f>OBJECTIVES</color>\n");
            foreach (var (def, done) in s.Objectives())
                ob.Append(done ? "<color=#73d97f>[x]</color> " : "[ ] ").Append(def.Text).Append('\n');
            ob.Append($"\n<color=#8d9bab>Time {UI.Time(s.Time)}  |  {(s.Spotted ? "Spotted" : "Undetected")}</color>");
            _objectives.text = ob.ToString();
            _map.texture = SpriteLibrary.Texture("Maps/" + s.Map.Id + "_preview");
            float aspect = (float)s.World.Width / s.World.Height;
            _map.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            var fit = _map.GetComponent<AspectRatioFitter>();
            if (fit == null) fit = _map.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = aspect;
            _you.anchorMin = _you.anchorMax = new Vector2(s.Player.Pos.x / s.World.Width, s.Player.Pos.y / s.World.Height);
            _you.sizeDelta = new Vector2(14, 14);
            _you.anchoredPosition = Vector2.zero;
        }
    }

    public sealed class MissionCompleteScreen : UIScreen
    {
        private Text _title, _rating, _stats, _money, _bonuses, _total, _unlocks;

        protected override void Build()
        {
            var dim = UI.Box(Root, new Color(0.01f, 0.015f, 0.02f, 0.9f), "dim");
            dim.rectTransform.Fill();
            var card = UI.Panel(Root, Theme.Panel, "card");
            card.rectTransform.At(0.5f, 0.5f, 0, 20, 1500, 860);
            var c = card.transform;
            _title = UI.Header(c, "CONTRACT COMPLETE", 60, Theme.Good);
            _title.rectTransform.At(0, 1, 50, -36, 1100, 80, 0, 1);
            _rating = UI.Header(c, "", 34, Theme.Gold);
            _rating.rectTransform.At(0, 1, 54, -118, 1200, 50, 0, 1);
            var line = UI.Box(c, Theme.Accent, "line");
            line.rectTransform.At(0, 1, 50, -176, 1400, 3, 0, 1);
            _stats = UI.Label(c, "", 34, Theme.Text, TextAnchor.UpperLeft);
            _stats.rectTransform.At(0, 1, 54, -200, 640, 420, 0, 1);
            _money = UI.Label(c, "", 34, Theme.Text, TextAnchor.UpperLeft);
            _money.rectTransform.At(0, 1, 740, -200, 700, 160, 0, 1);
            _bonuses = UI.Label(c, "", 32, Theme.Text, TextAnchor.UpperLeft);
            _bonuses.rectTransform.At(0, 1, 740, -360, 700, 300, 0, 1);
            _total = UI.Header(c, "", 44, Theme.Gold, TextAnchor.MiddleLeft);
            _total.rectTransform.At(0, 0, 740, 150, 700, 60, 0, 0);
            _unlocks = UI.Label(c, "", 30, Theme.Blue, TextAnchor.UpperLeft);
            _unlocks.rectTransform.At(0, 0, 54, 110, 660, 120, 0, 0);

            var shop = UI.Button(c, "Shop", () => GM.ShowShop(), 32, true);
            ((RectTransform)shop.transform).At(1, 0, -40, 32, 280, 72, 1, 0);
            var next = UI.Button(c, "Contracts", () => GM.ShowMissionSelect(), 30);
            ((RectTransform)next.transform).At(1, 0, -340, 32, 280, 72, 1, 0);
            var menu = UI.Button(c, "Main menu", () => GM.ShowMainMenu(), 28);
            ((RectTransform)menu.transform).At(1, 0, -640, 32, 260, 72, 1, 0);
        }

        public override bool Back() { GM.ShowMissionSelect(); return true; }

        public void Show(MissionResult r, MissionDef mission, string[] newlyUnlocked)
        {
            _rating.text = $"RATING: {r.Rating.ToUpperInvariant()}";
            _stats.text =
                $"<color=#8d9bab>Contract</color>  {mission.Name}\n" +
                $"<color=#8d9bab>Time</color>  {UI.Time(r.Time)}\n" +
                $"<color=#8d9bab>Targets eliminated</color>  {r.TargetsKilled}\n" +
                $"<color=#8d9bab>Enemies eliminated</color>  {r.NonTargetKills}\n" +
                $"<color=#8d9bab>Subdued</color>  {r.Subdued}\n" +
                $"<color=#8d9bab>Civilian casualties</color>  {(r.CiviliansKilled > 0 ? "<color=#d13f3f>" + r.CiviliansKilled + "</color>" : "0")}\n" +
                $"<color=#8d9bab>Bodies found</color>  {r.BodiesFound}\n" +
                $"<color=#8d9bab>Detection</color>  {(r.Spotted ? "<color=#d13f3f>Spotted</color>" : "<color=#73d97f>Never spotted</color>")}";
            _money.text =
                $"<color=#8d9bab>Contract payout</color>  {UI.Money(r.BaseReward)}\n" +
                $"<color=#8d9bab>Optional objectives</color>  {UI.Money(r.ObjectiveBonus)}\n" +
                $"<color=#8d9bab>Cash found</color>  {UI.Money(r.CashFound)}\n" +
                (r.Penalty > 0 ? $"<color=#d13f3f>Civilian penalty  -{UI.Money(r.Penalty)}</color>" : "");
            var sb = new StringBuilder("<color=#d13f3f>BONUS REWARDS</color>\n");
            foreach (var ch in mission.Challenges)
            {
                bool ok = r.ChallengesCompleted.Contains(ch);
                sb.Append(ok ? "<color=#73d97f>[x]</color> " : "<color=#6b7b8c>[ ]</color> ");
                sb.Append(ok ? ch.Name : $"<color=#6b7b8c>{ch.Name}</color>");
                sb.Append($"  <color=#8d9bab>{ch.Description}</color>");
                sb.Append(ok ? $"  <color=#f0c75e>+{UI.Money(ch.Bonus)}</color>\n" : "\n");
            }
            _bonuses.text = sb.ToString();
            _total.text = "EARNED  " + UI.Money(r.Total);
            _unlocks.text = newlyUnlocked.Length > 0 ? "NEW: " + string.Join(", ", newlyUnlocked) : "";
            Show();
        }
    }

    public sealed class GameOverScreen : UIScreen
    {
        private Text _title, _reason, _tip;
        private static readonly string[] Tips =
        {
            "Crouch in the dark: guards see much less of you.",
            "Throw a coin (slot 5) to lure a guard away from their post.",
            "Kill a guard before he finishes his radio call and the alarm never goes out.",
            "Drag bodies (E) into closets so nobody finds them.",
            "Suppressed weapons barely make noise. Loud guns bring everyone.",
            "Security terminals can switch off cameras. Fuse boxes kill the lights.",
            "Watch your target's routine: they visit the same rooms again and again.",
            "Low furniture hides you from guards if you crouch behind it.",
        };

        protected override void Build()
        {
            var dim = UI.Box(Root, new Color(0.03f, 0.005f, 0.01f, 0.92f), "dim");
            dim.rectTransform.Fill();
            _title = UI.Header(Root, "YOU DIED", 90, Theme.Accent, TextAnchor.MiddleCenter);
            _title.rectTransform.At(0.5f, 0.5f, 0, 230, 1500, 120);
            UI.AddShadow(_title, 5);
            _reason = UI.Label(Root, "", 40, Theme.Text, TextAnchor.MiddleCenter);
            _reason.rectTransform.At(0.5f, 0.5f, 0, 130, 1400, 60);
            _tip = UI.Label(Root, "", 32, Theme.Dim, TextAnchor.MiddleCenter);
            _tip.rectTransform.At(0.5f, 0.5f, 0, 50, 1400, 60);
            var col = UI.Rect(Root, "buttons").At(0.5f, 0.5f, 0, -150, 520, 300);
            UI.VList(col, 14);
            UI.Size(UI.Button(col, "Retry", () => GM.RestartMission(), 36, true), -1, 76);
            UI.Size(UI.Button(col, "Change loadout", () => GM.ShowLoadout(GM.LastMissionId), 30), -1, 64);
            UI.Size(UI.Button(col, "Contracts", () => GM.ShowMissionSelect(), 30), -1, 64);
        }

        public override bool Back() { GM.ShowMissionSelect(); return true; }

        public void Show(MissionResult r, bool died)
        {
            _title.text = died ? "YOU DIED" : "CONTRACT FAILED";
            _reason.text = r.FailReason ?? "";
            _tip.text = "TIP: " + Tips[Random.Range(0, Tips.Length)];
            Show();
        }
    }
}
