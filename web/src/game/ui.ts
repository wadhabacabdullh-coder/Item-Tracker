// Menu screens (port of the Unity UI/Screens): main menu, contracts, loadout, shop, settings, pause, inventory,
// mission complete and game over. Each screen rebuilds its DOM when shown or refreshed.
import { MISSIONS, getMission, MissionDef, MissionResult, Difficulty } from '../core/entities';
import { WEAPONS, WeaponSlot, WeaponDef, applyUpgrades, getUpgrade, AmmoType } from '../core/weapons';
import { ProgressData, SettingsData, CATALOG, ShopCategory, ShopItem, ShopItemKind, BuyResult, canBuy, buy, equip, isOwned, isEquipped, isUnlocked,
  itemStats, weaponStats, maxAmmo, describeBuy, MAX_MEDKITS, StatLine } from '../core/meta';
import { GameSession, ammoName } from '../core/session';
import { audio } from './audio';
import { input, ACTIONS, Action } from './input';
import { money, fmtTime, esc, src } from './hud';

export interface GameApi {
  progress: ProgressData; settings: SettingsData;
  session: GameSession | null; lastMissionId: string | null; pendingMissionId: string | null;
  saveProgress(): void; saveSettings(): void; applySettings(): void; resetProgress(): void;
  startMission(id: string): void; resumeMission(): void; toggleInventory(): void; restartMission(): void;
  endMissionWorld(): void; toggleFullscreen(): void; isFullscreen(): boolean;
}

export type ScreenName = 'main' | 'select' | 'loadout' | 'shop' | 'settings' | 'pause' | 'inventory' | 'complete' | 'gameover';

type Child = Node | string | null | undefined | false;
function h(tag: string, attrs: Record<string, unknown> | null, ...kids: Child[]): HTMLElement {
  const e = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v === undefined || v === null || v === false) continue;
    if (k.startsWith('on')) e.addEventListener(k.substring(2), v as EventListener);
    else if (k === 'html') e.innerHTML = v as string;
    else if (k === 'style') e.setAttribute('style', v as string);
    else e.setAttribute(k === 'cls' ? 'class' : k, v === true ? '' : String(v));
  }
  for (const c of kids) if (c !== null && c !== undefined && c !== false) e.append(c);
  return e;
}
const icon = (key: string, cls = 'icon') => h('img', { cls, src: src(key), alt: '' });

function button(label: string, onClick: () => void, opts: { primary?: boolean; disabled?: boolean; cls?: string; sound?: string; html?: boolean } = {}) {
  const b = h('button', { cls: `btn ${opts.primary ? 'primary' : ''} ${opts.cls ?? ''}`, disabled: opts.disabled }) as HTMLButtonElement;
  if (opts.html) b.innerHTML = label; else b.textContent = label;
  b.addEventListener('click', () => { if (b.disabled) return; audio.play2D(opts.sound ?? 'ui_click', 0.6); onClick(); });
  b.addEventListener('mouseenter', () => { if (!b.disabled) audio.play2D('ui_hover', 0.25); });
  return b;
}

function statBars(stats: StatLine[]) {
  return h('div', { cls: 'stats' }, ...stats.map(s => h('div', { cls: 'stat' },
    h('span', { cls: 'label' }, s.label),
    h('div', { cls: 'bar' }, h('div', { cls: 'fill', style: `width:${Math.max(3, s.value * 100)}%;background:${mixGold(s.value)}` })),
    h('span', { cls: 'val' }, s.text))));
}
function mixGold(t: number) { const a = [128, 140, 153], b = [240, 199, 94]; return `rgb(${a.map((v, i) => Math.round(v + (b[i] - v) * t)).join(',')})`; }

const TIPS = [
  'Crouch in the dark: guards see much less of you.',
  'Throw a coin (slot 5) to lure a guard away from their post.',
  'Kill a guard before he finishes his radio call and the alarm never goes out.',
  'Drag bodies (E) into closets so nobody finds them.',
  'Suppressed weapons barely make noise. Loud guns bring everyone.',
  'Security terminals can switch off cameras. Fuse boxes kill the lights.',
  "Watch your target's routine: they visit the same rooms again and again.",
  'Low furniture hides you from guards if you crouch behind it.',
];
const SLOT_NAMES = ['1  MELEE', '2  SIDEARM', '3  PRIMARY', '4  THROWING', '5  GADGET'];
const CAT_NAMES = ['Melee', 'Pistols', 'SMGs', 'Rifles', 'Shotguns', 'Throwables', 'Ammo', 'Gear', 'Upgrades'];

export class Menus {
  root: HTMLElement;
  current: ScreenName | null = null;
  capturing = false;
  private beforeShop: ScreenName | null = null;
  private selected: MissionDef | null = null;
  private loadoutMission: string | null = null;
  private shopCat = ShopCategory.Pistols;
  private shopSel: ShopItem | null = null;
  private feedback = { text: '', color: '', until: 0 };
  private settingsBack: (() => void) | null = null;
  private resetArmed = 0;
  private last: { result: MissionResult; mission: MissionDef; news: string[] } | null = null;
  private lastFail: { result: MissionResult; died: boolean; tip: string } | null = null;

  constructor(parent: HTMLElement, private g: GameApi) {
    this.root = h('div', { cls: 'menus' });
    parent.appendChild(this.root);
  }

  get visible() { return this.current !== null; }

  hide() { this.current = null; this.root.innerHTML = ''; this.root.className = 'menus'; }

  private mount(name: ScreenName, content: HTMLElement, cls = '') {
    this.current = name;
    this.root.className = 'menus on ' + cls;
    this.root.replaceChildren(content);
  }

  refresh() { if (this.current) this.show(this.current); }

  show(name: ScreenName) {
    switch (name) {
      case 'main': return this.mainMenu();
      case 'select': return this.missionSelect();
      case 'loadout': return this.loadout(this.loadoutMission);
      case 'shop': return this.shop();
      case 'settings': return this.settingsScreen(this.settingsBack);
      case 'pause': return this.pause();
      case 'inventory': return this.inventory();
      case 'complete': return this.last && this.complete(this.last.result, this.last.mission, this.last.news);
      case 'gameover': return this.lastFail && this.gameOver(this.lastFail.result, this.lastFail.died, this.lastFail.tip);
    }
  }

  /** Esc / back navigation. */
  back() {
    switch (this.current) {
      case 'main': return;
      case 'select': return this.mainMenu();
      case 'loadout': return this.loadoutMission ? this.missionSelect() : this.mainMenu();
      case 'shop': return this.fromShop();
      case 'settings': return this.settingsBack ? this.settingsBack() : this.mainMenu();
      case 'pause': return this.g.resumeMission();
      case 'inventory': return this.g.toggleInventory();
      case 'complete': case 'gameover': return this.missionSelect();
    }
  }

  private titleBar(title: string, sub: string) {
    return h('div', { cls: 'titlebar' },
      h('div', null, h('h1', null, title), h('div', { cls: 'sub' }, sub)),
      h('div', { cls: 'funds' }, icon('Icons/money'), h('span', null, money(this.g.progress.money))));
  }

  // ------------------------------------------------------------------ main menu
  mainMenu() {
    this.g.endMissionWorld();
    audio.playMenu();
    const p = this.g.progress;
    const confirm = h('div', { cls: 'confirm' });
    const reset = button('New profile', () => {
      if (performance.now() > this.resetArmed) {
        this.resetArmed = performance.now() + 4000;
        confirm.textContent = 'This erases all progress. Click again to confirm.';
        reset.textContent = 'Confirm reset';
        setTimeout(() => { if (performance.now() >= this.resetArmed) { confirm.textContent = ''; reset.textContent = 'New profile'; } }, 4100);
        return;
      }
      this.resetArmed = 0;
      this.g.resetProgress();
      this.mainMenu();
      (this.root.querySelector('.confirm') as HTMLElement).textContent = 'Profile reset.';
    }, { cls: 'small' });
    const firstTime = p.completedMissions.length === 0 && p.missionsPlayed === 0;
    this.mount('main', h('div', { cls: 'screen main-menu', style: `background-image:url(${src('UI/backdrop')})` },
      h('div', { cls: 'side' },
        h('div', { cls: 'logo' }, h('div', null, 'SHADOW'), h('div', { cls: 'red' }, 'CONTRACT')),
        h('div', { cls: 'tag' }, 'Every target has a routine. Learn it.'),
        h('div', { cls: 'menu-list' },
          button(firstTime ? 'Play' : 'Contracts', () => this.missionSelect(), { primary: true }),
          button('Loadout', () => this.loadout(null)),
          button('Shop', () => this.shop()),
          button('Settings', () => this.settingsScreen(null))),
        h('div', { cls: 'funds-line' }, 'FUNDS  ' + money(p.money)),
        h('div', { cls: 'dim' }, `Contracts completed: ${p.completedMissions.length}/${MISSIONS.length}    Total earned: ${money(p.totalEarned)}`)),
      h('div', { cls: 'controls-hint' }, 'WASD move · Mouse aim · LMB attack · RMB aim/subdue · E interact · Shift sprint · C crouch · Esc pause'),
      h('div', { cls: 'reset' }, confirm, reset)));
  }

  // ------------------------------------------------------------------ contracts
  missionSelect() {
    this.g.endMissionWorld();
    audio.playMenu();
    this.g.pendingMissionId = null;
    const p = this.g.progress;
    let firstOpen: MissionDef | null = null;
    for (const m of MISSIONS) if (p.isUnlocked(m.id) && !p.isCompleted(m.id) && !firstOpen) firstOpen = m;
    if (!this.selected || !p.isUnlocked(this.selected.id)) this.selected = firstOpen ?? MISSIONS.find(m => p.isUnlocked(m.id)) ?? null;
    const list = h('div', { cls: 'mission-list' });
    for (const m of MISSIONS) {
      const unlocked = p.isUnlocked(m.id), done = p.isCompleted(m.id);
      const b = h('button', { cls: `mission ${this.selected === m ? 'sel' : ''}`, disabled: !unlocked },
        h('div', { cls: 'mname' }, unlocked ? m.name : 'Locked'),
        h('div', { cls: 'mloc' }, unlocked ? m.location : `Complete "${getMission(m.unlockedBy)?.name}"`),
        done ? icon('Icons/check', 'badge') : !unlocked ? icon('Icons/lock', 'badge') : null);
      b.addEventListener('click', () => { if (!unlocked) return; audio.play2D('ui_click', 0.6); this.selected = m; this.missionSelect(); });
      list.append(b);
    }
    const m = this.selected;
    let detail: HTMLElement = h('div', null);
    if (m) {
      const rec = p.records[m.id];
      detail = h('div', { cls: 'mission-detail' },
        h('div', { cls: 'md-top' },
          h('div', { cls: 'preview' }, icon('Maps/' + m.mapId + '_preview', 'pix')),
          h('div', { cls: 'md-info' },
            h('h2', null, m.name.toUpperCase()),
            h('div', { cls: 'dim big' }, m.location),
            h('div', { cls: 'red' }, 'DIFFICULTY  ' + '*'.repeat(m.difficulty) + '.'.repeat(3 - m.difficulty)),
            h('div', { cls: 'gold big' }, 'PAYOUT  ' + money(m.reward) + '  + bonuses'),
            h('div', { cls: 'good', html: rec ? `Best rating: ${esc(rec.bestRating ?? '-')}<br>Best time: ${fmtTime(rec.bestTime)}${rec.silentAssassin ? '   <span class="gold">SILENT ASSASSIN</span>' : ''}` : '<span class="dim">Not yet completed</span>' }))),
        h('p', { cls: 'briefing' }, m.briefing),
        h('div', { cls: 'objectives', html: '<span class="red">OBJECTIVES</span><br>' + m.objectives.map(o => (o.optional ? '&nbsp;&nbsp;<span class="dim">(optional)</span> ' : '&nbsp;&nbsp;- ') + esc(o.text)).join('<br>')
          + `<br><span class="dim">Bonuses: ${m.challenges.map(c => esc(c.name)).join(' | ')}</span>` }),
        h('div', { cls: 'actions' },
          button('Shop', () => this.shop()),
          button('Loadout', () => this.loadout(m.id)),
          button('Start contract', () => this.g.startMission(m.id), { primary: true, disabled: !p.isUnlocked(m.id) })));
    }
    this.mount('select', h('div', { cls: 'screen' },
      this.titleBar('CONTRACTS', 'Choose your next target'),
      h('div', { cls: 'two-col' }, h('div', { cls: 'card list-card' }, list), h('div', { cls: 'card' }, detail)),
      h('div', { cls: 'footer' }, button('Back', () => this.mainMenu(), { sound: 'ui_back' }))));
  }

  // ------------------------------------------------------------------ loadout
  loadout(missionId: string | null) {
    this.g.endMissionWorld();
    this.loadoutMission = missionId;
    if (missionId) this.g.pendingMissionId = missionId;
    const p = this.g.progress;
    const detailBox = h('div', { cls: 'card detail' });
    const showDetail = (w: WeaponDef | undefined) => {
      if (!w) return;
      const ap = applyUpgrades(w, new Set(p.upgrades));
      detailBox.replaceChildren(
        h('div', { cls: 'row' }, icon('Icons/w_' + w.id, 'wicon-big'),
          h('div', null, h('h3', { html: esc(w.name.toUpperCase()) + (ap.suppressed && !w.suppressed ? '  <span class="teal">SUPPRESSED</span>' : '') }), h('p', { cls: 'dim' }, w.description))),
        statBars(weaponStats(ap)));
    };
    const cols = h('div', { cls: 'slot-cols' });
    for (let i = 0; i < 5; i++) {
      const slot = i as WeaponSlot;
      const col = h('div', { cls: 'card slot-col' }, h('div', { cls: 'red head' }, SLOT_NAMES[i]));
      const owned = p.ownedWeapons.map(id => WEAPONS.get(id)).filter((w): w is WeaponDef => !!w && w.slot === slot);
      for (const w of owned) {
        const eq = p.loadout[i] === w.id;
        const b = h('button', { cls: `weapon-card ${eq ? 'eq' : ''}` }, icon('Icons/w_' + w.id, 'wicon'), h('div', { cls: eq ? 'gold' : '' }, w.name + (eq ? '  [E]' : '')));
        b.addEventListener('click', () => { audio.play2D('switch', 0.5); equip(p, slot, w.id); this.g.saveProgress(); this.loadout(missionId); showDetailLater = w; });
        b.addEventListener('mouseenter', () => showDetail(w));
        col.append(b);
      }
      if (slot === WeaponSlot.Sidearm || slot === WeaponSlot.Primary || slot === WeaponSlot.Throwing) {
        const empty = p.loadout[i] === null;
        col.append(button(empty ? 'Empty [E]' : 'Leave empty', () => { equip(p, slot, null); this.g.saveProgress(); this.loadout(missionId); }, { cls: 'small' }));
      }
      if (owned.length === 0) col.append(h('div', { cls: 'dim center' }, 'Nothing owned. Visit the shop.'));
      cols.append(col);
    }
    const ups = p.upgrades.map(u => getUpgrade(u)?.name ?? (u === 'coin.pouch' ? 'Coin pouch' : u));
    const armor = p.armor === 'heavy' ? 'Plate carrier (100)' : p.armor === 'light' ? 'Kevlar vest (50)' : 'None';
    const gear = h('div', { cls: 'card gear', html:
      `<div class="red head">GEAR &amp; AMMO</div>Armor: <span class="blue">${armor}</span><br>Medkits: <span class="pink">${p.medkits}/${MAX_MEDKITS}</span><br>` +
      `9mm: ${p.getAmmo(AmmoType.Pistol)} &nbsp; 5.56: ${p.getAmmo(AmmoType.Rifle)} &nbsp; Shells: ${p.getAmmo(AmmoType.Shells)} &nbsp; Knives: ${p.getAmmo(AmmoType.Knives)}<br>` +
      `<span class="dim">Standard issue tops 9mm up to 36 rounds each contract.</span><br>Upgrades: <span class="gold">${ups.length ? ups.map(esc).join(', ') : 'none'}</span>` });
    const m = missionId ? getMission(missionId) : null;
    this.mount('loadout', h('div', { cls: 'screen' },
      this.titleBar('LOADOUT', m ? `Contract: ${m.name}` : 'Equip what you own. Buy more in the shop.'),
      cols, h('div', { cls: 'two-col bottom' }, detailBox, gear),
      h('div', { cls: 'footer' },
        button('Back', () => this.back(), { sound: 'ui_back' }), button('Shop', () => this.shop()),
        h('div', { cls: 'spacer' }),
        missionId ? button('Start contract', () => this.g.startMission(missionId), { primary: true }) : null)));
    showDetail(showDetailLater ?? WEAPONS.get(p.loadout[1] ?? p.loadout[0] ?? 'knife'));
    showDetailLater = undefined;
  }

  // ------------------------------------------------------------------ shop
  shop() {
    if (this.current !== 'shop') this.beforeShop = this.current;
    this.g.endMissionWorld();
    audio.playMenu();
    const p = this.g.progress;
    const items = CATALOG.filter(i => i.category === this.shopCat);
    if (!this.shopSel || this.shopSel.category !== this.shopCat) this.shopSel = items[0] ?? null;
    const tabs = h('div', { cls: 'tabs' }, ...CAT_NAMES.map((n, i) => {
      const b = button(n, () => { this.shopCat = i; this.shopSel = null; this.shop(); }, { cls: i === this.shopCat ? 'tab on' : 'tab' });
      return b;
    }));
    const iconFor = (it: ShopItem) => it.kind === ShopItemKind.Weapon ? 'Icons/w_' + it.weaponId : 'Icons/' + it.icon;
    const list = h('div', { cls: 'shop-list' });
    for (const it of items) {
      const unlocked = isUnlocked(p, it), owned = isOwned(p, it), eq = isEquipped(p, it);
      const tag = !unlocked ? `Unlocks after "${esc(getMission(it.unlockAfter)?.name ?? '')}"` : eq ? '<span class="good">EQUIPPED</span>' : owned ? '<span class="blue">OWNED</span>' : '';
      const showPrice = !(owned && it.kind !== ShopItemKind.Ammo && it.kind !== ShopItemKind.Medkit);
      const b = h('button', { cls: `shop-item ${this.shopSel === it ? 'sel' : ''} ${unlocked ? '' : 'locked'}` },
        icon(iconFor(it), 'wicon'),
        h('div', { cls: 'si-text' }, h('div', null, it.name), h('div', { cls: 'dim small', html: tag })),
        h('div', { cls: `price ${p.money >= it.price ? 'gold' : 'red'}` }, showPrice ? money(it.price) : ''));
      b.addEventListener('click', () => { audio.play2D('ui_click', 0.5); this.shopSel = it; this.shop(); });
      list.append(b);
    }
    const it = this.shopSel;
    let detail: HTMLElement = h('div', null);
    if (it) {
      const can = canBuy(p, it), owned = isOwned(p, it), eq = isEquipped(p, it);
      let status = eq ? 'Equipped' : owned ? 'Owned' : can === BuyResult.Ok ? 'Available' : describeBuy(can);
      if (it.kind === ShopItemKind.Ammo) status += `   (carrying ${p.getAmmo(it.ammoType!)}/${maxAmmo(it.ammoType!)})`;
      if (it.kind === ShopItemKind.Medkit) status += `   (carrying ${p.medkits}/${MAX_MEDKITS})`;
      const fb = performance.now() < this.feedback.until ? this.feedback : null;
      detail = h('div', { cls: 'shop-detail' },
        h('div', { cls: 'row' }, h('div', { cls: 'icon-frame' }, icon(iconFor(it), 'wicon-big')),
          h('div', null, h('h3', null, it.name.toUpperCase()), h('div', { cls: 'gold big' }, money(it.price)),
            h('div', { cls: can === BuyResult.Ok || owned ? 'good' : 'red' }, status))),
        h('p', { cls: 'mist' }, it.description),
        statBars(itemStats(it)),
        h('div', { cls: 'actions' },
          h('div', { cls: 'feedback', style: fb ? `color:${fb.color}` : '' }, fb ? fb.text : ''),
          it.kind === ShopItemKind.Weapon && owned && !eq ? button('Equip', () => {
            const d = WEAPONS.get(it.weaponId!)!;
            if (equip(p, d.slot, d.id)) { this.g.saveProgress(); this.fb(`${d.name} equipped in slot ${d.slot + 1}`, '#73d980'); }
            this.shop();
          }) : null,
          button(owned ? 'Owned' : 'Buy  ' + money(it.price), () => {
            const r = buy(p, it);
            if (r === BuyResult.Ok) { audio.play2D('ui_buy', 0.8); this.g.saveProgress(); this.fb(`Purchased ${it.name}`, '#73d980'); }
            else { audio.play2D('ui_error', 0.6); this.fb(describeBuy(r), '#d14040'); }
            this.shop();
          }, { primary: true, disabled: can !== BuyResult.Ok, sound: 'none' })));
    }
    this.mount('shop', h('div', { cls: 'screen' },
      this.titleBar('SHOP', 'Weapons, ammunition, gear and upgrades'), tabs,
      h('div', { cls: 'two-col' }, h('div', { cls: 'card list-card' }, list), h('div', { cls: 'card' }, detail)),
      h('div', { cls: 'footer' },
        button('Back', () => this.fromShop(), { sound: 'ui_back' }),
        button('Loadout', () => this.loadout(this.g.pendingMissionId)))));
  }
  private fb(text: string, color: string) { this.feedback = { text, color, until: performance.now() + 3000 }; }
  private fromShop() {
    const b = this.beforeShop;
    if (b === 'loadout') this.loadout(this.g.pendingMissionId);
    else if (b === 'select' || b === 'complete') this.missionSelect();
    else this.mainMenu();
  }

  // ------------------------------------------------------------------ settings
  settingsScreen(onBack: (() => void) | null) {
    this.settingsBack = onBack;
    const s = this.g.settings;
    const apply = () => { this.g.applySettings(); this.g.saveSettings(); };
    const slider = (label: string, get: () => number, set: (v: number) => void) => {
      const val = h('span', { cls: 'val' }, Math.round(get() * 100) + '%');
      const r = h('input', { type: 'range', min: 0, max: 100, value: Math.round(get() * 100) }) as HTMLInputElement;
      r.addEventListener('input', () => { set(+r.value / 100); val.textContent = r.value + '%'; apply(); });
      r.addEventListener('change', () => audio.play2D('ui_click', 0.6));
      return h('div', { cls: 'setting' }, h('span', { cls: 'label' }, label), r, val);
    };
    const selector = (label: string, text: () => string, next: () => void) => {
      const b = button(text(), () => { next(); apply(); b.textContent = text(); }, { cls: 'sel-btn' });
      return h('div', { cls: 'setting' }, h('span', { cls: 'label' }, label), b);
    };
    const binds = h('div', { cls: 'binds' });
    const drawBinds = () => {
      binds.replaceChildren(...ACTIONS.map(([a, label]) => {
        const b = button(input.label(a as Action), () => {
          b.textContent = 'Press a key...';
          b.classList.add('waiting');
          this.capturing = true;
          input.capture = code => {
            this.capturing = false;
            if (code !== 'Escape' || a === 'Pause') input.set(a as Action, code);
            s.bindings = { ...input.bindings };
            apply();
            drawBinds();
          };
        }, { cls: 'bind-btn' });
        return h('div', { cls: 'setting bind' }, h('span', { cls: 'label' }, label), b);
      }));
    };
    drawBinds();
    const diffNames = ['Easy', 'Normal', 'Hard'];
    this.mount('settings', h('div', { cls: 'screen' },
      this.titleBar('SETTINGS', 'Audio, gameplay and controls'),
      h('div', { cls: 'two-col' },
        h('div', { cls: 'card' },
          h('div', { cls: 'red head' }, 'AUDIO'),
          slider('Master volume', () => s.masterVolume, v => s.masterVolume = v),
          slider('Music volume', () => s.musicVolume, v => s.musicVolume = v),
          slider('Effects volume', () => s.sfxVolume, v => s.sfxVolume = v),
          h('div', { cls: 'red head' }, 'GAMEPLAY'),
          selector('Difficulty', () => diffNames[s.difficulty], () => { s.difficulty = ((s.difficulty + 1) % 3) as Difficulty; }),
          h('div', { cls: 'dim small' }, 'Easy: slower detection, weaker enemies. Hard: sharp-eyed guards who hit hard. Applies to the next contract.'),
          selector('Screen shake', () => s.screenShake ? 'On' : 'Off', () => { s.screenShake = !s.screenShake; }),
          selector('Vision cones', () => s.showVisionCones ? 'On' : 'Off', () => { s.showVisionCones = !s.showVisionCones; }),
          selector('Fullscreen', () => this.g.isFullscreen() ? 'On' : 'Off', () => this.g.toggleFullscreen())),
        h('div', { cls: 'card' },
          h('div', { cls: 'red head' }, 'CONTROLS  ', h('span', { cls: 'dim small' }, 'click a key to rebind')),
          binds,
          button('Reset controls', () => { input.reset(); s.bindings = {}; apply(); drawBinds(); }, { cls: 'small' }))),
      h('div', { cls: 'footer' }, button('Back', () => this.back(), { sound: 'ui_back' }))));
  }

  // ------------------------------------------------------------------ in-mission overlays
  pause() {
    const s = this.g.session;
    const m = s?.mission;
    this.mount('pause', h('div', { cls: 'screen overlay' },
      h('div', { cls: 'pause-card card' },
        h('h1', null, 'PAUSED'),
        m ? h('div', { cls: 'dim' }, `${m.name} · ${m.location}`) : null,
        s ? h('div', { cls: 'objectives', html: s.objectives().map(o => (o.done ? '<span class="good">[x]</span> ' : '[ ] ') + esc(o.def.text)).join('<br>') }) : null,
        h('div', { cls: 'menu-list' },
          button('Resume', () => this.g.resumeMission(), { primary: true }),
          button('Restart contract', () => this.g.restartMission()),
          button('Settings', () => this.settingsScreen(() => this.pause())),
          button('Abandon contract', () => this.missionSelect(), { sound: 'ui_back' })))), 'dim-bg');
  }

  inventory() {
    const s = this.g.session; if (!s) return;
    const inv = s.player.inventory;
    const rows = inv.slots.map((w, i) => h('div', { cls: `inv-slot ${i === inv.current ? 'on' : ''}` },
      h('span', { cls: 'n dim' }, String(i + 1)),
      w ? icon('Icons/w_' + w.def.id, 'wicon') : null,
      h('div', { html: w ? `${esc(w.def.name)}<br><span class="dim">${w.def.isGun ? `${w.mag} / ${inv.getReserve(w.def.ammo)} ${ammoName(w.def.ammo)}` : w.def.isThrown ? 'x' + inv.getReserve(w.def.ammo) : 'melee'}</span>` : '<span class="dim">empty</span>' })));
    const items = '<span class="red">ITEMS</span><br>' +
      `Medkits: ${inv.medkits}  (press ${esc(input.label('Medkit'))})<br>` +
      `Armor: ${Math.ceil(s.player.armor)}/${Math.ceil(s.player.maxArmor)}<br>` +
      `Keycards: ${inv.keys.size === 0 ? 'none' : [...inv.keys].map(esc).join(', ')}<br>` +
      `Intel: ${inv.intel.length === 0 ? 'none' : inv.intel.map(esc).join(', ')}<br>` +
      `Cash found: <span class="gold">${money(inv.cashFound)}</span><br>` +
      (s.player.draggingBody >= 0 ? '<span class="warn">Dragging a body</span>' : '');
    const objs = '<span class="red">OBJECTIVES</span><br>' + s.objectives().map(o => (o.done ? '<span class="good">[x]</span> ' : '[ ] ') + esc(o.def.text)).join('<br>') +
      `<br><br><span class="dim">Time ${fmtTime(s.time)}  |  ${s.spotted ? 'Spotted' : 'Undetected'}</span>`;
    const W = s.world.width, H = s.world.height;
    const map = h('div', { cls: 'inv-map', style: `aspect-ratio:${W}/${H}` }, icon('Maps/' + s.map.id + '_preview', 'pix'),
      h('div', { cls: 'you', style: `left:${(s.player.pos.x / W) * 100}%;top:${(1 - s.player.pos.y / H) * 100}%` }));
    this.mount('inventory', h('div', { cls: 'screen overlay' },
      h('div', { cls: 'titlebar' }, h('h1', null, 'INVENTORY'), h('div', { cls: 'dim' }, `Press ${esc(input.label('Inventory'))} or Esc to close`)),
      h('div', { cls: 'inv-grid' },
        h('div', { cls: 'card' }, ...rows),
        h('div', { cls: 'inv-mid' }, h('div', { cls: 'card', html: items }), h('div', { cls: 'card', html: objs })),
        h('div', { cls: 'card map-card' }, map))), 'dim-bg');
  }

  complete(r: MissionResult, mission: MissionDef, news: string[]) {
    this.last = { result: r, mission, news };
    const stats = [
      ['Contract', esc(mission.name)], ['Time', fmtTime(r.time)], ['Targets eliminated', r.targetsKilled], ['Enemies eliminated', r.nonTargetKills],
      ['Subdued', r.subdued], ['Civilian casualties', r.civiliansKilled > 0 ? `<span class="red">${r.civiliansKilled}</span>` : '0'],
      ['Bodies found', r.bodiesFound], ['Detection', r.spotted ? '<span class="red">Spotted</span>' : '<span class="good">Never spotted</span>'],
    ].map(([a, b]) => `<div class="kv"><span class="dim">${a}</span><span>${b}</span></div>`).join('');
    const pay = [['Contract payout', money(r.baseReward)], ['Optional objectives', money(r.objectiveBonus)], ['Cash found', money(r.cashFound)]]
      .map(([a, b]) => `<div class="kv"><span class="dim">${a}</span><span>${b}</span></div>`).join('')
      + (r.penalty > 0 ? `<div class="kv red"><span>Civilian penalty</span><span>-${money(r.penalty)}</span></div>` : '');
    const bonuses = '<div class="red head">BONUS REWARDS</div>' + mission.challenges.map(ch => {
      const ok = r.challengesCompleted.includes(ch);
      return `<div class="bonus">${ok ? '<span class="good">[x]</span>' : '<span class="dim">[ ]</span>'} <span class="${ok ? '' : 'dim'}">${esc(ch.name)}</span> <span class="dim small">${esc(ch.description)}</span>${ok ? ` <span class="gold">+${money(ch.bonus)}</span>` : ''}</div>`;
    }).join('');
    this.mount('complete', h('div', { cls: 'screen overlay' },
      h('div', { cls: 'card result-card' },
        h('h1', { cls: 'good' }, 'CONTRACT COMPLETE'),
        h('div', { cls: 'gold big' }, 'RATING: ' + r.rating.toUpperCase()),
        h('div', { cls: 'rule' }),
        h('div', { cls: 'two-col' }, h('div', { html: stats }), h('div', { html: pay + bonuses + `<div class="total gold">EARNED ${money(r.total)}</div>` })),
        news.length ? h('div', { cls: 'blue unlocks' }, 'NEW: ' + news.join(', ')) : null,
        h('div', { cls: 'actions' },
          button('Main menu', () => this.mainMenu(), { sound: 'ui_back' }),
          button('Contracts', () => this.missionSelect()),
          button('Shop', () => this.shop(), { primary: true })))), 'dim-bg');
  }

  gameOver(r: MissionResult, died: boolean, tip?: string) {
    tip = tip ?? TIPS[Math.floor(Math.random() * TIPS.length)];
    this.lastFail = { result: r, died, tip };
    this.mount('gameover', h('div', { cls: 'screen overlay gameover' },
      h('h1', { cls: 'red huge' }, died ? 'YOU DIED' : 'CONTRACT FAILED'),
      h('div', { cls: 'big' }, r.failReason ?? ''),
      h('div', { cls: 'dim' }, 'TIP: ' + tip),
      h('div', { cls: 'menu-list narrow' },
        button('Retry', () => this.g.restartMission(), { primary: true }),
        button('Change loadout', () => this.loadout(this.g.lastMissionId)),
        button('Contracts', () => this.missionSelect(), { sound: 'ui_back' }))), 'dim-bg red-bg');
  }
}

let showDetailLater: WeaponDef | undefined;
