// In-mission heads-up display (port of HUD.cs). DOM widgets that read the simulation each frame and never modify it.
import { GameSession, CandidateKind } from '../core/session';
import { AIState, AlertLevel, ObjectiveType, PickupKind } from '../core/entities';
import { img } from './assets';
import { input } from './input';
import { Renderer } from './render';

export const money = (n: number) => '$' + Math.round(n).toLocaleString('en-US');
export const fmtTime = (s: number) => { const m = Math.floor(s / 60), r = Math.floor(s % 60); return `${m}:${r < 10 ? '0' : ''}${r}`; };
export const esc = (s: string) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]!));
export const src = (key: string) => img(key)?.src ?? '';

function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls: string, parent?: HTMLElement, html?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (html !== undefined) e.innerHTML = html;
  if (parent) parent.appendChild(e);
  return e;
}

/** Only touches the DOM when a value actually changes. */
function setHtml(e: HTMLElement, html: string) { if ((e as unknown as { _h?: string })._h !== html) { (e as unknown as { _h?: string })._h = html; e.innerHTML = html; } }
function setStyle(e: HTMLElement, k: string, v: string) { const st = e.style as unknown as Record<string, string>; if (st[k] !== v) st[k] = v; }

interface Floater { el: HTMLElement; life: number; bar?: HTMLElement; dur?: number; t?: number; }

export class Hud {
  root: HTMLElement;
  private s: GameSession | null = null;
  private r!: Renderer;
  private hp!: HTMLElement; private hpText!: HTMLElement; private armor!: HTMLElement; private armorText!: HTMLElement;
  private medkits!: HTMLElement; private keys!: HTMLElement;
  private wIcon!: HTMLImageElement; private wName!: HTMLElement; private ammo!: HTMLElement; private reload!: HTMLElement; private reloadBar!: HTMLElement;
  private slots: { frame: HTMLElement; icon: HTMLImageElement }[] = [];
  private moneyEl!: HTMLElement; private detect!: HTMLElement; private detectText!: HTMLElement; private eye!: HTMLImageElement;
  private zone!: HTMLElement; private objLines!: HTMLElement;
  private prompt!: HTMLElement; private promptText!: HTMLElement; private hold!: HTMLElement; private holdBar!: HTMLElement;
  private toastRoot!: HTMLElement; private floatRoot!: HTMLElement;
  private banner!: HTMLElement; private bannerSub!: HTMLElement; private warning!: HTMLElement;
  private cross!: HTMLImageElement; private vignette!: HTMLElement; private flashEl!: HTMLElement;
  private mm!: HTMLCanvasElement; private mmx!: CanvasRenderingContext2D;
  private toasts: Floater[] = [];
  private subs = new Map<number, Floater>();
  private radios = new Map<number, Floater>();
  private arrows: HTMLImageElement[] = [];
  private bannerTime = 0; private warnTime = 0; private damageAlpha = 0; private flashAlpha = 0;
  private shownMoney = 0; private profileMoney = 0;

  constructor(parent: HTMLElement) {
    this.root = el('div', 'hud hidden', parent);
    const R = this.root;
    this.vignette = el('div', 'vignette', R);
    this.vignette.style.backgroundImage = `url(${src('UI/vignette')})`;
    this.flashEl = el('div', 'flash', R);
    this.floatRoot = el('div', 'floaters', R);

    const obj = el('div', 'panel hud-obj', R);
    el('div', 'accent-bar', obj);
    this.zone = el('div', 'zone', obj);
    this.objLines = el('div', 'obj-lines', obj);

    const st = el('div', 'panel hud-status', R);
    const m = el('div', 'row', st);
    el('img', 'ico', m).src = src('Icons/money');
    this.moneyEl = el('div', 'money', m);
    const d = el('div', 'row', st);
    this.eye = el('img', 'ico', d); this.eye.src = src('Icons/eye');
    const db = el('div', 'bar detect', d);
    this.detect = el('div', 'fill', db);
    this.detectText = el('div', 'detect-text', d);

    const mmp = el('div', 'panel hud-mm', R);
    this.mm = el('canvas', 'mm', mmp);
    this.mm.width = 144; this.mm.height = 144;
    this.mmx = this.mm.getContext('2d')!;

    const bl = el('div', 'panel hud-vitals', R);
    const hr = el('div', 'row', bl);
    el('img', 'ico', hr).src = src('Icons/health');
    const hb = el('div', 'bar hp', hr); this.hp = el('div', 'fill', hb);
    this.hpText = el('div', 'num', hr);
    const ar = el('div', 'row', bl);
    el('img', 'ico', ar).src = src('Icons/shield');
    const ab = el('div', 'bar armor', ar); this.armor = el('div', 'fill', ab);
    this.armorText = el('div', 'num blue', ar);
    const mr = el('div', 'row', bl);
    el('img', 'ico', mr).src = src('Icons/medkit');
    this.medkits = el('div', 'medkits', mr);
    this.keys = el('div', 'keys', mr);

    const br = el('div', 'panel hud-weapon', R);
    const top = el('div', 'row', br);
    this.wIcon = el('img', 'wicon', top);
    const col = el('div', 'col', top);
    this.wName = el('div', 'wname', col);
    this.ammo = el('div', 'ammo', col);
    this.reload = el('div', 'bar reload', col); this.reloadBar = el('div', 'fill', this.reload);
    const sl = el('div', 'slots', br);
    for (let i = 0; i < 5; i++) {
      const f = el('div', 'slot', sl);
      el('span', 'n', f, String(i + 1));
      this.slots.push({ frame: f, icon: el('img', '', f) });
    }

    this.prompt = el('div', 'prompt', R);
    this.promptText = el('div', 'ptext', this.prompt);
    this.hold = el('div', 'bar hold', this.prompt); this.holdBar = el('div', 'fill', this.hold);
    this.toastRoot = el('div', 'toasts', R);
    this.banner = el('div', 'banner', R);
    this.bannerSub = el('div', 'banner-sub', R);
    this.warning = el('div', 'warning', R);
    this.cross = el('img', 'cross', R); this.cross.src = src('UI/crosshair');
  }

  bind(s: GameSession, r: Renderer, profileMoney: number) {
    this.s = s; this.r = r;
    this.profileMoney = profileMoney; this.shownMoney = profileMoney;
    this.toastRoot.innerHTML = ''; this.floatRoot.innerHTML = '';
    this.toasts = []; this.subs.clear(); this.radios.clear(); this.arrows = [];
    this.banner.textContent = this.bannerSub.textContent = this.warning.textContent = '';
    this.bannerTime = this.warnTime = this.damageAlpha = this.flashAlpha = 0;
  }
  unbind() { this.s = null; }

  show(on: boolean) { this.root.classList.toggle('hidden', !on); }

  // ------------------------------------------------------------------ messages
  toast(text: string | null | undefined, color = '#e6ebf2') {
    if (!text) return;
    const e = el('div', 'toast', this.toastRoot);
    e.textContent = text; e.style.color = color;
    this.toastRoot.prepend(e);
    this.toasts.unshift({ el: e, life: 3.2 });
    while (this.toasts.length > 4) this.toasts.pop()!.el.remove();
  }
  bannerMsg(title: string, sub: string | null, color: string) {
    this.banner.textContent = title; this.banner.style.color = color;
    this.bannerSub.textContent = sub ?? '';
    this.bannerTime = 3.2;
  }
  warn(text: string) { this.warning.textContent = text; this.warnTime = 2.4; }
  subtitle(id: number, text: string | null | undefined) {
    if (!text) return;
    this.subs.get(id)?.el.remove();
    const e = el('div', 'npc-sub', this.floatRoot); e.textContent = text;
    this.subs.set(id, { el: e, life: 2.6 });
  }
  radioStarted(id: number, dur: number) {
    this.radioEnded(id);
    const e = el('div', 'radio', this.floatRoot, '<span>RADIO</span>');
    const b = el('div', 'bar', e);
    this.radios.set(id, { el: e, life: 0, bar: el('div', 'fill', b), dur, t: 0 });
    this.warn('A GUARD IS CALLING IT IN');
  }
  radioEnded(id: number) { const r = this.radios.get(id); if (r) { r.el.remove(); this.radios.delete(id); } }
  damage() { this.damageAlpha = Math.min(0.85, this.damageAlpha + 0.45); }
  flash(a: number) { this.flashAlpha = a; }

  // ------------------------------------------------------------------ frame
  update(dt: number, simDt: number, menuOpen: boolean) {
    const s = this.s; if (!s) return;
    const p = s.player, inv = p.inventory, now = performance.now() / 1000;
    const pp = (x: number) => Math.abs(((x % 2) + 2) % 2 - 1); // 1..0..1 ping-pong

    // vitals
    const hf = Math.max(0, Math.min(1, p.health / p.maxHealth));
    setStyle(this.hp, 'width', (hf * 100).toFixed(1) + '%');
    setStyle(this.hp, 'background', hf < 0.3 ? `rgb(${217 + 38 * pp(now * 3) * 0.5},${51 + 204 * (1 - pp(now * 3)) * 0.5},${56 + 199 * (1 - pp(now * 3)) * 0.5})` : '#d93338');
    setHtml(this.hpText, String(Math.ceil(p.health)));
    const hasArmor = p.maxArmor > 0;
    setStyle(this.armor, 'width', (hasArmor ? Math.max(0, p.armor / p.maxArmor) * 100 : 0).toFixed(1) + '%');
    setHtml(this.armorText, hasArmor ? String(Math.ceil(p.armor)) : '-');
    setHtml(this.medkits, `x${inv.medkits} <span class="dim">[${esc(input.label('Medkit'))}]</span>`);
    let keys = '';
    if (inv.keys.has('blue')) keys += '<span style="color:#7fb0e0">BLUE KEY</span> ';
    if (inv.keys.has('red')) keys += '<span style="color:#ef7a6a">RED KEY</span>';
    setHtml(this.keys, keys);

    // weapon
    const w = inv.currentWeapon;
    if (w) {
      const s2 = src('Icons/w_' + w.def.id);
      if (this.wIcon.getAttribute('src') !== s2) this.wIcon.src = s2;
      setHtml(this.wName, esc(w.def.name.toUpperCase() + (w.def.suppressed && w.def.id !== 'silenced_pistol' ? ' SD' : '')));
      if (w.def.isGun) setHtml(this.ammo, `<span class="${w.mag === 0 ? 'red' : ''}">${w.mag}</span><small> / ${inv.getReserve(w.def.ammo)}</small>`);
      else if (w.def.isThrown) setHtml(this.ammo, `x${inv.getReserve(w.def.ammo)}`);
      else setHtml(this.ammo, '<small>MELEE</small>');
      setStyle(this.reload, 'visibility', w.reloading ? 'visible' : 'hidden');
      setStyle(this.reloadBar, 'width', (w.reloading ? w.reloadProgress * 100 : 0).toFixed(1) + '%');
    }
    for (let i = 0; i < 5; i++) {
      const sw = inv.slots[i], sl = this.slots[i];
      const s2 = sw ? src('Icons/w_' + sw.def.id) : '';
      if (sl.icon.getAttribute('src') !== s2) { if (s2) sl.icon.src = s2; else sl.icon.removeAttribute('src'); }
      setStyle(sl.icon, 'visibility', sw ? 'visible' : 'hidden');
      sl.frame.classList.toggle('on', i === inv.current);
    }

    // money
    const target = this.profileMoney + inv.cashFound;
    const step = Math.max(20, Math.abs(target - this.shownMoney) * 4) * dt;
    this.shownMoney = Math.abs(target - this.shownMoney) <= step ? target : this.shownMoney + Math.sign(target - this.shownMoney) * step;
    setHtml(this.moneyEl, money(this.shownMoney));

    // detection
    const det = s.detectionLevel;
    const combat = s.alert === AlertLevel.Combat;
    const fill = combat ? 1 : det;
    setStyle(this.detect, 'width', (fill * 100).toFixed(1) + '%');
    const mix = (a: number[], b: number[], t: number) => `rgb(${a.map((v, i) => Math.round(v + (b[i] - v) * t)).join(',')})`;
    setStyle(this.detect, 'background', mix([115, 217, 128], [209, 64, 64], fill));
    let status: string, sc: string;
    if (combat) { status = 'COMBAT'; sc = '#d14040'; }
    else if (s.alert === AlertLevel.Alarmed) { status = 'SEARCHING'; sc = '#ffb34d'; }
    else if (p.hidden || p.inVent) { status = 'HIDDEN'; sc = '#73d980'; }
    else if (det > 0.5) { status = 'DETECTED?'; sc = '#ffb34d'; }
    else if (det > 0.05) { status = 'NOTICED'; sc = '#ffe680'; }
    else { status = 'UNSEEN'; sc = '#73d980'; }
    setHtml(this.detectText, status);
    setStyle(this.detectText, 'color', sc);
    setStyle(this.eye, 'opacity', det > 0.05 || s.alert >= AlertLevel.Alarmed ? String(0.5 + 0.5 * pp(now * 4)) : '0.6');

    // objectives
    setHtml(this.zone, esc(s.currentZoneName().toUpperCase()));
    const cur = s.currentObjective;
    let html = '';
    for (const { def, done } of s.objectives()) {
      if (def.type === ObjectiveType.Extract && !s.requiredDone) continue;
      const mark = done ? '<span class="good">[x]</span>' : def === cur ? '<span class="red">&gt;</span>' : '<span class="dim">-</span>';
      const cls = done ? 'dim' : def.optional ? 'mist' : '';
      html += `<div>${mark} <span class="${cls}">${esc(def.text)}</span></div>`;
    }
    setHtml(this.objLines, html);

    // interaction prompt
    const c = s.candidate;
    let showPrompt = c.kind !== CandidateKind.None && !!c.prompt;
    let ptxt = c.prompt, pen = c.enabled, phold = c.holdTime;
    if (p.hidden) { showPrompt = true; ptxt = 'Leave hiding spot'; pen = true; phold = 0; }
    const showAny = showPrompt || !!p.actionLabel;
    setStyle(this.prompt, 'display', showAny ? 'block' : 'none');
    if (p.actionLabel && !showPrompt) setHtml(this.promptText, esc(p.actionLabel.toUpperCase()) + '...');
    else if (showPrompt) setHtml(this.promptText, pen ? `<span class="gold">[${esc(input.label('Interact'))}]</span> ${esc(ptxt)}${phold > 0 ? ' <span class="dim">(hold)</span>' : ''}` : `<span class="dim">${esc(ptxt)}</span>`);
    const hp = s.holdProgressValue;
    setStyle(this.hold, 'visibility', hp > 0 ? 'visible' : 'hidden');
    setStyle(this.holdBar, 'width', (hp * 100).toFixed(1) + '%');

    // crosshair: its gap follows the current spread
    const spread = w && w.def.isGun ? w.currentSpread(p.moving, p.aiming) : 1;
    const cs = Math.round(26 + spread * 2.4);
    setStyle(this.cross, 'width', cs + 'px'); setStyle(this.cross, 'height', cs + 'px');
    setStyle(this.cross, 'transform', `translate(${input.mouseX - cs / 2}px,${input.mouseY - cs / 2}px)`);
    setStyle(this.cross, 'filter', c.kind !== CandidateKind.None ? 'sepia(1) saturate(4) hue-rotate(-10deg)' : 'none');
    setStyle(this.cross, 'display', menuOpen ? 'none' : 'block');

    // toasts
    for (let i = this.toasts.length - 1; i >= 0; i--) {
      const t = this.toasts[i];
      t.life -= dt;
      if (t.life <= 0) { t.el.remove(); this.toasts.splice(i, 1); continue; }
      setStyle(t.el, 'opacity', Math.min(1, t.life * 2).toFixed(2));
    }

    // subtitles & radio bars above NPCs
    const W = window.innerWidth, H = window.innerHeight;
    for (const [id, f] of this.subs) {
      f.life -= dt;
      const n = s.npcs.find(q => q.id === id);
      if (f.life <= 0 || !n || n.down) { f.el.remove(); this.subs.delete(id); continue; }
      const [x, y] = this.r.worldToScreen(n.pos.x, n.pos.y + 1.6);
      const on = x > 0 && x < W && y > 0 && y < H;
      setStyle(f.el, 'display', on ? 'block' : 'none');
      setStyle(f.el, 'transform', `translate(${Math.round(x)}px,${Math.round(y)}px) translate(-50%,-50%)`);
    }
    for (const [id, f] of this.radios) {
      f.t! += simDt;
      const n = s.npcs.find(q => q.id === id);
      if (!n || n.down || f.t! > f.dur! + 0.2) { f.el.remove(); this.radios.delete(id); continue; }
      setStyle(f.bar!, 'width', (Math.min(1, f.t! / f.dur!) * 100).toFixed(1) + '%');
      const [x, y] = this.r.worldToScreen(n.pos.x, n.pos.y + 2.2);
      setStyle(f.el, 'transform', `translate(${Math.round(x)}px,${Math.round(y)}px) translate(-50%,-50%)`);
    }

    this.updateArrows(s, W, H);
    this.updateMinimap(s);

    // banner / warning / vignette fades
    this.bannerTime -= dt;
    const ba = Math.max(0, Math.min(1, this.bannerTime)).toFixed(2);
    setStyle(this.banner, 'opacity', ba); setStyle(this.bannerSub, 'opacity', ba);
    this.warnTime -= dt;
    setStyle(this.warning, 'opacity', (this.warnTime > 0 ? (0.6 + 0.4 * pp(now * 5)) * Math.min(1, this.warnTime) : 0).toFixed(2));
    const lowHp = hf < 0.3 ? 0.25 + 0.1 * Math.sin(now * 5) : 0;
    this.damageAlpha = Math.max(0, this.damageAlpha - dt * 1.5);
    setStyle(this.vignette, 'opacity', Math.max(this.damageAlpha * 0.6, lowHp).toFixed(2));
    this.flashAlpha = Math.max(0, this.flashAlpha - dt * 2);
    setStyle(this.flashEl, 'opacity', this.flashAlpha.toFixed(2));
  }

  /** Screen-edge arrows towards living targets that are off screen. */
  private updateArrows(s: GameSession, W: number, H: number) {
    let k = 0;
    for (const n of s.npcs) {
      if (!n.isTarget || n.down) continue;
      const [x, y] = this.r.worldToScreen(n.pos.x, n.pos.y);
      const on = x > 30 && x < W - 30 && y > 30 && y < H - 30;
      if (k >= this.arrows.length) { const a = el('img', 'arrow', this.floatRoot); a.src = src('UI/target_marker'); this.arrows.push(a); }
      const a = this.arrows[k++];
      setStyle(a, 'display', on ? 'none' : 'block');
      if (on) continue;
      const cx = W / 2, cy = H / 2;
      let dx = x - cx, dy = y - cy; const l = Math.hypot(dx, dy) || 1; dx /= l; dy /= l;
      const m = Math.min((W / 2 - 40) / Math.max(0.001, Math.abs(dx)), (H / 2 - 40) / Math.max(0.001, Math.abs(dy)));
      // the marker sprite points down; rotate it to face the target
      setStyle(a, 'transform', `translate(${Math.round(cx + dx * m)}px,${Math.round(cy + dy * m)}px) translate(-50%,-50%) rotate(${Math.atan2(dy, dx) - Math.PI / 2}rad)`);
    }
    for (let i = k; i < this.arrows.length; i++) setStyle(this.arrows[i], 'display', 'none');
  }

  private updateMinimap(s: GameSession) {
    const im = img('Maps/' + s.map.id + '_preview');
    const c = this.mmx, S = this.mm.width;
    c.imageSmoothingEnabled = false;
    c.fillStyle = '#05070a'; c.fillRect(0, 0, S, S);
    if (!im) return;
    const span = 36, k = im.width / s.world.width, sc = S / span;
    const p = s.player.pos, Hh = s.world.height;
    c.globalAlpha = 0.85;
    c.drawImage(im, (p.x - span / 2) * k, (Hh - p.y - span / 2) * k, span * k, span * k, 0, 0, S, S);
    c.globalAlpha = 1;
    const dot = (x: number, y: number, col: string, sz: number) => {
      const px = S / 2 + (x - p.x) * sc, py = S / 2 - (y - p.y) * sc;
      if (px < 0 || py < 0 || px > S || py > S) return;
      c.fillStyle = col; c.fillRect(Math.round(px - sz / 2), Math.round(py - sz / 2), sz, sz);
    };
    for (const n of s.npcs) {
      if (n.down) continue;
      if (n.isTarget) dot(n.pos.x, n.pos.y, '#d14040', 7);
      else if (n.isCamera) continue;
      else if (n.isHostile && [AIState.Attack, AIState.Chase, AIState.Search, AIState.Investigate].includes(n.state)) dot(n.pos.x, n.pos.y, '#ffb34d', 5);
    }
    for (const pk of s.pickups) if (!pk.taken && (pk.kind === PickupKind.Intel || pk.kind === PickupKind.Keycard)) dot(pk.pos.x, pk.pos.y, '#f0c75e', 6);
    if (s.requiredDone) for (const e of s.extracts) { const cc = e.rect.center; dot(cc.x, cc.y, '#73d980', 9); }
    // player arrow
    c.save(); c.translate(S / 2, S / 2); c.rotate(-s.player.facing);
    c.fillStyle = '#ffffff'; c.beginPath(); c.moveTo(6, 0); c.lineTo(-4, -4); c.lineTo(-2, 0); c.lineTo(-4, 4); c.closePath(); c.fill();
    c.restore();
  }
}
