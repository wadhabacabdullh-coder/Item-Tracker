using System.Collections.Generic;
using System.Linq;
using ShadowContract.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowContract.Game
{
    public sealed class LoadoutScreen : UIScreen
    {
        private Text _money, _gear, _detailName, _detailDesc;
        private RectTransform[] _columns;
        private RectTransform _stats;
        private Image _detailIcon;
        private Button _start;
        private string _missionId;
        private static readonly string[] SlotNames = { "1  MELEE", "2  SIDEARM", "3  PRIMARY", "4  THROWING", "5  GADGET" };

        public void SetMission(string missionId) => _missionId = missionId;

        protected override void Build()
        {
            _money = TitleBar("LOADOUT", "Equip what you own. Buy more in the shop.");
            _columns = new RectTransform[5];
            for (int i = 0; i < 5; i++)
            {
                var card = UI.Panel(Root, Theme.Panel, "slot" + i);
                card.rectTransform.Place(i / 5f, 0.42f, (i + 1) / 5f, 1, i == 0 ? 40 : 8, 0, i == 4 ? 40 : 8, 140);
                var h = UI.Header(card.transform, SlotNames[i], 22, Theme.Accent);
                h.rectTransform.Place(0, 1, 1, 1, 16, -48, 16, 8);
                _columns[i] = UI.Rect(card.transform, "list").Place(0, 0, 1, 1, 12, 12, 12, 56);
                UI.VList(_columns[i], 8);
            }
            var detail = UI.Panel(Root, Theme.Panel, "detail");
            detail.rectTransform.Place(0, 0, 0.58f, 0.42f, 40, 130, 8, 16);
            _detailIcon = UI.Icon(detail.transform, null, 192, 96);
            _detailIcon.rectTransform.At(0, 1, 24, -24, 192, 96, 0, 1);
            _detailName = UI.Header(detail.transform, "", 30, Theme.Text);
            _detailName.rectTransform.Place(0, 1, 1, 1, 240, -70, 24, 24);
            _detailDesc = UI.Label(detail.transform, "", 28, Theme.Dim, TextAnchor.UpperLeft);
            _detailDesc.rectTransform.Place(0, 1, 1, 1, 240, -140, 24, 74);
            _stats = UI.Rect(detail.transform, "stats").Place(0, 0, 1, 1, 24, 16, 24, 150);
            UI.VList(_stats, 4);

            var gear = UI.Panel(Root, Theme.Panel, "gear");
            gear.rectTransform.Place(0.58f, 0, 1, 0.42f, 8, 130, 40, 16);
            var gh = UI.Header(gear.transform, "GEAR & AMMO", 22, Theme.Accent);
            gh.rectTransform.Place(0, 1, 1, 1, 20, -48, 20, 8);
            _gear = UI.Label(gear.transform, "", 30, Theme.Text, TextAnchor.UpperLeft);
            _gear.rectTransform.Place(0, 0, 1, 1, 20, 12, 20, 56);

            var back = UI.Button(Root, "Back", () => Back(), 30);
            ((RectTransform)back.transform).At(0, 0, 40, 40, 220, 64, 0, 0);
            var shop = UI.Button(Root, "Shop", () => GM.ShowShop(), 30);
            ((RectTransform)shop.transform).At(0, 0, 280, 40, 220, 64, 0, 0);
            _start = UI.Button(Root, "Start contract", () => { if (_missionId != null) GM.StartMission(_missionId); }, 34, true);
            ((RectTransform)_start.transform).At(1, 0, -40, 40, 380, 64, 1, 0);
        }

        public override bool Back()
        {
            if (_missionId != null) GM.ShowMissionSelect();
            else GM.ShowMainMenu();
            return true;
        }

        public override void Refresh()
        {
            var p = GM.Progress;
            _money.text = UI.Money(p.Money);
            _start.gameObject.SetActive(_missionId != null);
            for (int i = 0; i < 5; i++)
            {
                UI.Clear(_columns[i]);
                var slot = (WeaponSlot)i;
                var owned = p.OwnedWeapons.Select(WeaponCatalog.Get).Where(w => w != null && w.Slot == slot).ToList();
                foreach (var w in owned)
                {
                    var wd = w;
                    bool equipped = p.Loadout[i] == w.Id;
                    var b = UI.Button(_columns[i], "", () =>
                    {
                        ShopService.Equip(p, slot, wd.Id);
                        GM.SaveProgress();
                        ShowDetail(wd);
                        Refresh();
                    }, 22);
                    UI.Size(b, -1, 110);
                    var fx = b.GetComponent<UIButtonFx>();
                    fx.SetColors(equipped ? Theme.AccentDark : new Color(0.13f, 0.16f, 0.2f), equipped ? Theme.Accent : new Color(0.22f, 0.27f, 0.34f));
                    var icon = UI.Icon(b.transform, SpriteLibrary.WeaponIcon(w.Id), 128, 64);
                    icon.rectTransform.At(0.5f, 1, 0, -6, 128, 64, 0.5f, 1);
                    var name = UI.Label(b.transform, w.Name + (equipped ? "  [E]" : ""), 26, equipped ? Theme.Gold : Theme.Text, TextAnchor.LowerCenter);
                    name.rectTransform.Place(0, 0, 1, 0, 4, 4, 4, -34);
                }
                if (slot == WeaponSlot.Sidearm || slot == WeaponSlot.Primary || slot == WeaponSlot.Throwing)
                {
                    bool empty = p.Loadout[i] == null;
                    var e = UI.Button(_columns[i], empty ? "Empty [E]" : "Leave empty", () => { ShopService.Equip(p, slot, null); GM.SaveProgress(); Refresh(); }, 22);
                    UI.Size(e, -1, 52);
                }
                if (owned.Count == 0)
                {
                    var none = UI.Label(_columns[i], "Nothing owned.\nVisit the shop.", 26, Theme.Dim, TextAnchor.UpperCenter);
                    UI.Size(none, -1, 80);
                }
            }
            string Ammo(AmmoType t) => $"{p.GetAmmo(t)}";
            string armor = p.Armor == "heavy" ? "Plate carrier (100)" : p.Armor == "light" ? "Kevlar vest (50)" : "None";
            var ups = p.Upgrades.Select(u => UpgradeCatalog.Get(u)?.Name ?? (u == "coin.pouch" ? "Coin pouch" : u)).ToList();
            _gear.text =
                $"Armor: <color=#7fb0e0>{armor}</color>\n" +
                $"Medkits: <color=#ef7a6a>{p.Medkits}/{ShopService.MaxMedkits}</color>\n" +
                $"9mm: {Ammo(AmmoType.Pistol)}   5.56: {Ammo(AmmoType.Rifle)}   Shells: {Ammo(AmmoType.Shells)}   Knives: {Ammo(AmmoType.Knives)}\n" +
                "<color=#8d9bab>Standard issue tops 9mm up to 36 rounds each contract.</color>\n" +
                $"Upgrades: <color=#f0c75e>{(ups.Count == 0 ? "none" : string.Join(", ", ups))}</color>";
            var shown = WeaponCatalog.Get(p.Loadout[1] ?? p.Loadout[0]);
            ShowDetail(shown);
        }

        private void ShowDetail(WeaponDef w)
        {
            if (w == null) return;
            var applied = UpgradeCatalog.Apply(w, new HashSet<string>(GM.Progress.Upgrades));
            _detailIcon.sprite = SpriteLibrary.WeaponIcon(w.Id);
            _detailName.text = w.Name.ToUpperInvariant() + (applied.Suppressed && !w.Suppressed ? "  <color=#59c1c9>SUPPRESSED</color>" : "");
            _detailDesc.text = w.Description;
            StatBars.Fill(_stats, ShopItem.WeaponStats(applied), 3);
        }
    }

    /// <summary>Renders a list of stat lines as labelled bars.</summary>
    public static class StatBars
    {
        public static void Fill(RectTransform root, List<StatLine> stats, int columns = 1)
        {
            UI.Clear(root);
            foreach (var s in stats)
            {
                var row = UI.Rect(root, "stat");
                UI.Size(row, -1, 34);
                var l = UI.Label(row, s.Label, 26, Theme.Dim);
                l.rectTransform.Place(0, 0, 0.3f, 1);
                var bar = UI.Bar(row, new Color(0, 0, 0, 0.5f), Color.Lerp(new Color(0.5f, 0.55f, 0.6f), Theme.Gold, s.Value), out var br);
                br.Place(0.3f, 0.2f, 0.72f, 0.8f);
                bar.fillAmount = Mathf.Max(0.03f, s.Value);
                var v = UI.Label(row, s.Text, 26, Theme.Text, TextAnchor.MiddleRight);
                v.rectTransform.Place(0.72f, 0, 1, 1);
            }
        }
    }

    public sealed class ShopScreen : UIScreen
    {
        private Text _money, _name, _desc, _price, _status, _feedback;
        private Image _icon;
        private RectTransform _list, _stats, _tabs;
        private Button _buy, _equip;
        private ShopCategory _category = ShopCategory.Pistols;
        private ShopItem _selected;
        private float _feedbackTime;
        private readonly List<(ShopCategory cat, Button b)> _tabButtons = new List<(ShopCategory, Button)>();

        protected override void Build()
        {
            _money = TitleBar("SHOP", "Weapons, ammunition, gear and upgrades");
            _tabs = UI.Rect(Root, "tabs").Place(0, 1, 1, 1, 40, -200, 40, 130);
            UI.HList(_tabs, 8);
            foreach (ShopCategory c in System.Enum.GetValues(typeof(ShopCategory)))
            {
                var cat = c;
                var b = UI.Button(_tabs, c.ToString(), () => { _category = cat; _selected = null; Refresh(); }, 24);
                _tabButtons.Add((c, b));
            }
            var left = UI.Panel(Root, Theme.Panel, "items");
            left.rectTransform.Place(0, 0, 0.5f, 1, 40, 130, 8, 220);
            _list = UI.ScrollList(left.transform, 8, out var sr);
            ((RectTransform)sr.transform).Place(0, 0, 1, 1, 12, 12, 12, 12);

            var right = UI.Panel(Root, Theme.Panel, "detail");
            right.rectTransform.Place(0.5f, 0, 1, 1, 8, 130, 40, 220);
            var rt = right.transform;
            var iconFrame = UI.Panel(rt, new Color(0, 0, 0, 0.45f), "iconFrame");
            iconFrame.rectTransform.At(0, 1, 24, -24, 300, 150, 0, 1);
            _icon = UI.Icon(iconFrame.transform, null, 240, 120);
            _icon.rectTransform.At(0.5f, 0.5f, 0, 0, 240, 120);
            _name = UI.Header(rt, "", 32, Theme.Text);
            _name.rectTransform.Place(0, 1, 1, 1, 344, -80, 24, 24);
            _price = UI.Header(rt, "", 30, Theme.Gold);
            _price.rectTransform.Place(0, 1, 1, 1, 344, -130, 24, 84);
            _status = UI.Label(rt, "", 28, Theme.Good);
            _status.rectTransform.Place(0, 1, 1, 1, 344, -176, 24, 134);
            _desc = UI.Label(rt, "", 30, Theme.Mist, TextAnchor.UpperLeft);
            _desc.rectTransform.Place(0, 1, 1, 1, 24, -290, 24, 196);
            _stats = UI.Rect(rt, "stats").Place(0, 0, 1, 1, 24, 120, 24, 300);
            UI.VList(_stats, 4);
            _buy = UI.Button(rt, "Buy", OnBuy, 34, true);
            ((RectTransform)_buy.transform).At(1, 0, -24, 24, 300, 72, 1, 0);
            _equip = UI.Button(rt, "Equip", OnEquip, 30);
            ((RectTransform)_equip.transform).At(1, 0, -340, 24, 220, 72, 1, 0);
            _feedback = UI.Header(rt, "", 20, Theme.Warn, TextAnchor.MiddleLeft);
            _feedback.rectTransform.At(0, 0, 24, 40, 520, 40, 0, 0);

            var back = UI.Button(Root, "Back", () => Back(), 30);
            ((RectTransform)back.transform).At(0, 0, 40, 40, 220, 64, 0, 0);
            var loadout = UI.Button(Root, "Loadout", () => GM.ShowLoadout(GM.PendingMissionId), 30);
            ((RectTransform)loadout.transform).At(0, 0, 280, 40, 240, 64, 0, 0);
        }

        public override bool Back() { GM.ShowPreviousFromShop(); return true; }

        public override void Refresh()
        {
            var p = GM.Progress;
            _money.text = UI.Money(p.Money);
            foreach (var (cat, b) in _tabButtons)
                b.GetComponent<UIButtonFx>().SetColors(cat == _category ? Theme.AccentDark : new Color(0.13f, 0.16f, 0.2f), cat == _category ? Theme.Accent : new Color(0.22f, 0.27f, 0.34f));

            UI.Clear(_list);
            var items = ShopService.Catalog.Where(i => i.Category == _category).ToList();
            foreach (var item in items)
            {
                var it = item;
                bool unlocked = ShopService.IsUnlocked(p, it);
                bool owned = ShopService.IsOwned(p, it);
                bool equipped = ShopService.IsEquipped(p, it);
                var b = UI.Button(_list, "", () => { _selected = it; ShowItem(); }, 22);
                UI.Size(b, -1, 92);
                var fx = b.GetComponent<UIButtonFx>();
                bool sel = _selected == it;
                fx.SetColors(sel ? new Color(0.2f, 0.12f, 0.14f) : new Color(0.1f, 0.12f, 0.15f), new Color(0.22f, 0.27f, 0.34f));
                var icon = UI.Icon(b.transform, IconFor(it), 120, 60);
                icon.rectTransform.At(0, 0.5f, 14, 0, 120, 60, 0, 0.5f);
                if (!unlocked) icon.color = new Color(0.3f, 0.3f, 0.35f);
                var n = UI.Label(b.transform, unlocked ? it.Name : it.Name + "  <color=#8d9bab>(locked)</color>", 30, unlocked ? Theme.Text : Theme.Dim);
                n.rectTransform.Place(0, 0.5f, 1, 1, 150, 0, 150, 6);
                string tag = !unlocked ? $"Unlocks after \"{MissionCatalog.Get(it.UnlockAfter)?.Name}\""
                    : equipped ? "<color=#73d97f>EQUIPPED</color>" : owned ? "<color=#7fb0e0>OWNED</color>" : "";
                var sub = UI.Label(b.transform, tag, 24, Theme.Dim);
                sub.rectTransform.Place(0, 0, 1, 0.5f, 150, 6, 150, 0);
                var price = UI.Header(b.transform, owned && it.Kind != ShopItemKind.Ammo && it.Kind != ShopItemKind.Medkit ? "" : UI.Money(it.Price), 22,
                    p.Money >= it.Price ? Theme.Gold : Theme.Accent, TextAnchor.MiddleRight);
                price.rectTransform.Place(0.6f, 0, 1, 1, 0, 0, 20, 0);
            }
            if (_selected == null || _selected.Category != _category) _selected = items.FirstOrDefault();
            ShowItem();
        }

        private static Sprite IconFor(ShopItem it)
        {
            switch (it.Kind)
            {
                case ShopItemKind.Weapon: return SpriteLibrary.WeaponIcon(it.WeaponId);
                default: return SpriteLibrary.Icon(it.Icon);
            }
        }

        private void ShowItem()
        {
            var p = GM.Progress;
            var it = _selected;
            bool any = it != null;
            _buy.gameObject.SetActive(any);
            _equip.gameObject.SetActive(false);
            if (!any) { _name.text = _desc.text = _price.text = _status.text = ""; UI.Clear(_stats); return; }
            _icon.sprite = IconFor(it);
            _name.text = it.Name.ToUpperInvariant();
            _price.text = UI.Money(it.Price);
            _desc.text = it.Description;
            StatBars.Fill(_stats, it.Stats());
            var can = ShopService.CanBuy(p, it);
            bool owned = ShopService.IsOwned(p, it);
            bool equipped = ShopService.IsEquipped(p, it);
            _status.text = equipped ? "Equipped" : owned ? "Owned" : can == BuyResult.Ok ? "Available" : ShopService.Describe(can);
            _status.color = can == BuyResult.Ok || owned ? Theme.Good : Theme.Accent;
            if (it.Kind == ShopItemKind.Ammo) _status.text += $"   (carrying {p.GetAmmo(it.AmmoType)}/{ShopService.MaxAmmo(it.AmmoType)})";
            if (it.Kind == ShopItemKind.Medkit) _status.text += $"   (carrying {p.Medkits}/{ShopService.MaxMedkits})";
            UI.SetLabel(_buy, owned ? "Owned" : "Buy  " + UI.Money(it.Price));
            UI.SetEnabled(_buy, can == BuyResult.Ok);
            if (it.Kind == ShopItemKind.Weapon && owned && !equipped)
            {
                _equip.gameObject.SetActive(true);
                UI.SetEnabled(_equip, true);
            }
        }

        private void OnBuy()
        {
            if (_selected == null) return;
            var p = GM.Progress;
            var r = ShopService.Buy(p, _selected);
            if (r == BuyResult.Ok)
            {
                AudioManager.I.Play2D("ui_buy", 0.8f);
                GM.SaveProgress();
                Feedback($"Purchased {_selected.Name}", Theme.Good);
            }
            else Feedback(ShopService.Describe(r), Theme.Accent);
            Refresh();
        }

        private void OnEquip()
        {
            if (_selected == null || _selected.Kind != ShopItemKind.Weapon) return;
            var def = WeaponCatalog.Get(_selected.WeaponId);
            if (ShopService.Equip(GM.Progress, def.Slot, def.Id))
            {
                GM.SaveProgress();
                Feedback($"{def.Name} equipped in slot {(int)def.Slot + 1}", Theme.Good);
            }
            Refresh();
        }

        private void Feedback(string text, Color c)
        {
            _feedback.text = text;
            _feedback.color = c;
            _feedbackTime = 3f;
        }

        protected override void Update()
        {
            base.Update();
            if (_feedbackTime > 0f)
            {
                _feedbackTime -= Time.unscaledDeltaTime;
                if (_feedbackTime <= 0f) _feedback.text = "";
            }
        }
    }
}
