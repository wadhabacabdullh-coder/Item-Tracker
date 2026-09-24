// Canvas2D renderer: port of WorldView/CharacterView/LightingView/VisionConeView/FxManager.
import { Vec2, Int2 } from '../core/math';
import { TileKind, FurnitureType } from '../core/map';
import { bakeLights, RayMode } from '../core/world';
import { GameSession } from '../core/session';
import { Npc, NpcKind, AIState, Actor, PickupKind, Pickup, ProjectileKind, InteractKind, AlertLevel } from '../core/entities';
import { WeaponDef, WeaponClass } from '../core/weapons';
import { img } from './assets';

const PPU = 16;
const LIGHT_SAMPLES = 2;

interface Particle {
  image?: HTMLImageElement; frames?: HTMLImageElement[]; color?: string;
  x: number; y: number; vx: number; vy: number; life: number; max: number; rot: number; spin: number;
  scale: number; grow: number; alpha: number; drag: number; add: boolean; layer: 0 | 1; w?: number; h?: number;
}

export class Renderer {
  canvas: HTMLCanvasElement; ctx: CanvasRenderingContext2D;
  buf: HTMLCanvasElement; b: CanvasRenderingContext2D;
  scale = 3; bw = 0; bh = 0;
  camX = 0; camY = 0; private velX = 0; private velY = 0;
  private trauma = 0; private kickX = 0; private kickY = 0;
  shakeEnabled = true; showCones = true;

  private s: GameSession | null = null;
  private decals!: HTMLCanvasElement; private dctx!: CanvasRenderingContext2D;
  private light!: HTMLCanvasElement; private lctx!: CanvasRenderingContext2D;
  private lightHi!: HTMLCanvasElement; private hctx!: CanvasRenderingContext2D; private hiImg!: ImageData; private hasHi = false;
  private tmp = document.createElement('canvas'); private tctx = this.tmp.getContext('2d')!;
  private lightRgb!: Float32Array; private lightImg!: ImageData;
  private rebakeCd = 0;
  private particles: Particle[] = [];
  private tracers: { x1: number; y1: number; x2: number; y2: number; life: number; player: boolean }[] = [];
  private rings: { x: number; y: number; r: number; life: number; color: string }[] = [];
  private cones: { pts: number[]; color: number[]; range: number }[] = [];
  private coneFrame = 0;
  private swing = new Map<number, number>();
  private recoil = new Map<number, number>();
  private flash = new Map<number, number>();
  private stainedBodies = new Set<number>();

  constructor(canvas: HTMLCanvasElement) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d')!;
    this.buf = document.createElement('canvas');
    this.b = this.buf.getContext('2d')!;
    this.resize();
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const W = Math.floor(window.innerWidth * dpr), H = Math.floor(window.innerHeight * dpr);
    this.canvas.width = W; this.canvas.height = H;
    this.scale = Math.max(1, Math.round(H / 290));
    this.bw = Math.ceil(W / this.scale); this.bh = Math.ceil(H / this.scale);
    this.buf.width = this.bw; this.buf.height = this.bh;
  }

  bind(s: GameSession) {
    this.s = s;
    const W = s.world.width, H = s.world.height;
    this.decals = document.createElement('canvas');
    this.decals.width = W * PPU; this.decals.height = H * PPU;
    this.dctx = this.decals.getContext('2d')!;
    this.light = document.createElement('canvas');
    this.light.width = W * LIGHT_SAMPLES; this.light.height = H * LIGHT_SAMPLES;
    this.lctx = this.light.getContext('2d')!;
    this.lightRgb = new Float32Array(W * H * LIGHT_SAMPLES * LIGHT_SAMPLES * 3);
    this.lightImg = this.lctx.createImageData(this.light.width, this.light.height);
    this.lightHi = document.createElement('canvas');
    this.lightHi.width = this.light.width; this.lightHi.height = this.light.height;
    this.hctx = this.lightHi.getContext('2d')!;
    this.hiImg = this.hctx.createImageData(this.light.width, this.light.height);
    this.particles = []; this.tracers = []; this.rings = []; this.cones = [];
    this.swing.clear(); this.recoil.clear(); this.flash.clear(); this.stainedBodies.clear();
    this.rebake();
    this.camX = s.player.pos.x; this.camY = s.player.pos.y; this.velX = this.velY = 0;
  }

  /** Same maths as the LightMultiply shader: final = 2 * light * scene. Canvas multiply clamps at 1, so the part
   *  above albedo (light > 0.5) goes into a second texture that is added back on top. */
  private rebake() {
    const s = this.s!;
    bakeLights(s.world, s.lights, LIGHT_SAMPLES, this.lightRgb);
    const w = this.light.width, h = this.light.height, d = this.lightImg.data, e = this.hiImg.data;
    let hi = false;
    for (let sy = 0; sy < h; sy++)
      for (let sx = 0; sx < w; sx++) {
        const src = ((h - 1 - sy) * w + sx) * 3, dst = (sy * w + sx) * 4;
        for (let c = 0; c < 3; c++) {
          const v = 2 * this.lightRgb[src + c];
          d[dst + c] = Math.min(255, v * 255);
          const x = Math.min(255, Math.max(0, v - 1) * 255);
          e[dst + c] = x;
          if (x > 2) hi = true;
        }
        d[dst + 3] = 255; e[dst + 3] = 255;
      }
    this.lctx.putImageData(this.lightImg, 0, 0);
    this.hctx.putImageData(this.hiImg, 0, 0);
    this.hasHi = hi;
    s.lightsDirty = false;
  }

  unbind() { this.s = null; this.particles = []; this.tracers = []; this.rings = []; }

  // ------------------------------------------------------------------ camera & feedback
  private wx(x: number) { return x * PPU; }
  private wy(y: number) { return (this.s!.world.height - y) * PPU; }
  screenToWorld(px: number, py: number): Vec2 {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const bx = px * dpr / this.scale, by = py * dpr / this.scale;
    return new Vec2(this.camX + (bx - this.bw / 2) / PPU, this.camY - (by - this.bh / 2) / PPU);
  }
  worldToScreen(x: number, y: number): [number, number] {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    return [((x - this.camX) * PPU + this.bw / 2) * this.scale / dpr, ((this.camY - y) * PPU + this.bh / 2) * this.scale / dpr];
  }
  follow(fx: number, fy: number, ax: number, ay: number, aiming: boolean, dt: number) {
    const k = aiming ? 0.45 : 0.22, max = aiming ? 5.5 : 2.2;
    let lx = (ax - fx) * k, ly = (ay - fy) * k;
    const l = Math.hypot(lx, ly);
    if (l > max) { lx *= max / l; ly *= max / l; }
    const tx = fx + lx, ty = fy + ly;
    const t = 1 - Math.exp(-dt / (aiming ? 0.16 : 0.11) * 2.2);
    this.camX += (tx - this.camX) * t; this.camY += (ty - this.camY) * t;
    this.trauma = Math.max(0, this.trauma - dt * 2.2);
    this.kickX *= Math.max(0, 1 - dt * 18); this.kickY *= Math.max(0, 1 - dt * 18);
  }
  snap(x: number, y: number) { this.camX = x; this.camY = y; }
  shake(a: number) { if (this.shakeEnabled) this.trauma = Math.min(1, this.trauma + a); }
  kick(angle: number, a: number) { this.kickX -= Math.cos(angle) * a; this.kickY -= Math.sin(angle) * a; }
  doSwing(id: number) { this.swing.set(id, 0.18); }
  doRecoil(id: number, a: number) { this.recoil.set(id, Math.max(this.recoil.get(id) ?? 0, a)); }
  doFlash(id: number) { this.flash.set(id, 0.12); }

  // ------------------------------------------------------------------ fx
  private spawn(p: Partial<Particle> & { x: number; y: number }): Particle {
    const q: Particle = { vx: 0, vy: 0, life: 0.3, max: 0.3, rot: 0, spin: 0, scale: 1, grow: 0, alpha: 1, drag: 4, add: false, layer: 1, ...p };
    q.max = q.life;
    if (this.particles.length > 500) this.particles.shift();
    this.particles.push(q);
    return q;
  }
  private decal(name: string, x: number, y: number, rot = 0, alpha = 1, scale = 1) {
    const im = img(name); if (!im || !this.dctx) return;
    const c = this.dctx;
    c.save(); c.globalAlpha = alpha; c.translate(this.wx(x), this.wy(y)); c.rotate(rot); c.scale(scale, scale);
    c.drawImage(im, -im.width / 2, -im.height / 2); c.restore();
  }
  muzzle(x: number, y: number, a: number, suppressed: boolean) {
    if (!suppressed) {
      this.spawn({ frames: [img('FX/muzzle_0')!, img('FX/muzzle_1')!, img('FX/muzzle_2')!], x, y, life: 0.06, rot: a, add: true, w: 0.25 });
      this.spawn({ image: img('FX/glow'), x, y, life: 0.08, scale: 1.6, alpha: 0.55, add: true, color: '#ffcc73' });
    } else this.spawn({ image: img('FX/smoke'), x, y, life: 0.25, scale: 0.35, grow: 0.8, alpha: 0.35 });
  }
  tracer(x1: number, y1: number, x2: number, y2: number, player: boolean) { this.tracers.push({ x1, y1, x2, y2, life: 0.05, player }); }
  casing(x: number, y: number, a: number, shell: boolean) {
    const sp = 2.5 + Math.random() * 1.5;
    const vx = Math.cos(a) * sp, vy = Math.sin(a) * sp;
    this.spawn({ image: img(shell ? 'FX/shell' : 'FX/casing'), x, y, vx, vy, life: 0.45, rot: Math.random() * 6, spin: (Math.random() * 2 - 1) * 15, drag: 7 });
    this.decal(shell ? 'FX/shell' : 'FX/casing', x + vx * 0.14, y + vy * 0.14, Math.random() * 6, 0.9);
  }
  impact(x: number, y: number, nx: number, ny: number, metal: boolean) {
    this.decal('FX/bullet_hole', x - nx * 0.02, y - ny * 0.02, 0, 0.8);
    for (let i = 0; i < (metal ? 6 : 4); i++) {
      const a = Math.atan2(ny, nx) + (Math.random() * 2 - 1) * 1.1, sp = 2 + Math.random() * 3;
      this.spawn({ image: img(metal ? 'FX/spark' : 'FX/dust'), x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, life: 0.12 + Math.random() * 0.18, drag: 6, add: metal });
    }
    if (!metal) this.spawn({ image: img('FX/smoke'), x, y, vx: nx * 0.6, vy: ny * 0.6, life: 0.4, scale: 0.4, grow: 0.8, alpha: 0.3 });
  }
  hit(x: number, y: number, a: number, camera: boolean) {
    if (camera) { for (let i = 0; i < 6; i++) this.spawn({ image: img('FX/spark'), x, y, vx: (Math.random() * 2 - 1) * 4, vy: (Math.random() * 2 - 1) * 4, life: 0.25, drag: 6, add: true }); return; }
    for (let i = 0; i < 5; i++) {
      const b = a + (Math.random() * 2 - 1) * 0.6, sp = 1.5 + Math.random() * 2;
      this.spawn({ image: img('FX/hit'), x, y, vx: Math.cos(b) * sp, vy: Math.sin(b) * sp, life: 0.2 + Math.random() * 0.15, drag: 9 });
    }
    if (Math.random() < 0.5) this.decal('FX/stain_' + Math.floor(Math.random() * 3), x + Math.cos(a) * 0.4, y + Math.sin(a) * 0.4, Math.random() * 6, 0.55, 0.45);
  }
  explosion(x: number, y: number, radius: number) {
    const frames = [0, 1, 2, 3, 4, 5].map(i => img('FX/explosion_' + i)!);
    this.spawn({ frames, x, y, life: 0.55, scale: radius / 1.5 });
    this.spawn({ image: img('FX/glow'), x, y, life: 0.35, scale: radius * 2.5, add: true, color: '#ff9940' });
    for (let i = 0; i < 18; i++) { const a = Math.random() * 6.28, sp = 4 + Math.random() * 6; this.spawn({ image: img('FX/spark'), x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, life: 0.3 + Math.random() * 0.4, drag: 3, add: true }); }
    for (let i = 0; i < 8; i++) this.spawn({ image: img('FX/smoke'), x: x + Math.random() - 0.5, y: y + Math.random() - 0.5, vx: Math.random() - 0.5, vy: Math.random() - 0.5, life: 1 + Math.random() * 0.8, scale: 1.2, grow: 1.2, alpha: 0.7, drag: 1.5 });
    this.decal('FX/scorch', x, y, Math.random() * 6, 0.9, radius / 1.2);
  }
  glass(x: number, y: number) {
    for (let i = 0; i < 12; i++) {
      this.spawn({ image: img('FX/glass'), x, y, vx: (Math.random() * 2 - 1) * 4, vy: (Math.random() * 2 - 1) * 4, life: 0.3 + Math.random() * 0.3, spin: (Math.random() * 2 - 1) * 10, drag: 5 });
      this.decal('FX/glass', x + (Math.random() * 2 - 1) * 0.9, y + (Math.random() * 2 - 1) * 0.9, Math.random() * 6, 0.8);
    }
  }
  sparks(x: number, y: number, n: number) { for (let i = 0; i < n; i++) this.spawn({ image: img('FX/spark'), x, y, vx: (Math.random() * 2 - 1) * 3, vy: (Math.random() * 2 - 1) * 3, life: 0.3, add: true }); }
  ring(x: number, y: number, r: number, color: string) { this.rings.push({ x, y, r, life: 0.5, color }); }
  stain(x: number, y: number) { this.decal('FX/stain_' + Math.floor(Math.random() * 3), x, y, Math.random() * 6, 0.7); }

  // ------------------------------------------------------------------ frame
  draw(dt: number) {
    const s = this.s;
    const ctx = this.ctx, b = this.b;
    b.imageSmoothingEnabled = false;
    b.setTransform(1, 0, 0, 1, 0, 0);
    b.fillStyle = '#07090d';
    b.fillRect(0, 0, this.bw, this.bh);
    if (!s) { this.present(); return; }

    this.rebakeCd -= dt;
    if (s.lightsDirty && this.rebakeCd <= 0) { this.rebake(); this.rebakeCd = 0.2; }

    let cx = this.camX + this.kickX, cy = this.camY + this.kickY;
    if (this.trauma > 0) {
      const a = this.trauma * this.trauma * 0.35, t = performance.now() / 25;
      cx += Math.sin(t * 1.3) * a; cy += Math.cos(t * 1.7) * a;
    }
    const ox = Math.round(cx * PPU - this.bw / 2), oy = Math.round((s.world.height - cy) * PPU - this.bh / 2);
    b.setTransform(1, 0, 0, 1, -ox, -oy);
    const id = s.map.id;
    const base = img(`Maps/${id}_base`);
    if (base) b.drawImage(base, 0, 0);
    b.drawImage(this.decals, 0, 0);

    this.drawExtracts(s);
    this.drawDoorsWindows(s);
    this.drawPickups(s);
    this.drawBodies(s);
    for (const n of s.npcs) if (!n.down && !n.isCamera) this.drawCharacter(n, dt, s);
    if (s.player.alive) this.drawCharacter(s.player, dt, s);
    this.drawProjectiles(s);
    this.drawParticles(dt, 1);

    const over = img(`Maps/${id}_overlay`);
    if (over) b.drawImage(over, 0, 0);

    // lighting
    const LW = s.world.width * PPU, LH = s.world.height * PPU;
    if (this.hasHi) {
      const t = this.tctx, m = b.getTransform();
      if (this.tmp.width !== this.bw || this.tmp.height !== this.bh) { this.tmp.width = this.bw; this.tmp.height = this.bh; }
      t.setTransform(1, 0, 0, 1, 0, 0);
      t.globalCompositeOperation = 'copy';
      t.drawImage(this.buf, 0, 0);
      t.setTransform(m);
      t.globalCompositeOperation = 'multiply';
      t.imageSmoothingEnabled = true;
      t.drawImage(this.lightHi, 0, 0, LW, LH);
    }
    b.save();
    b.globalCompositeOperation = 'multiply';
    b.imageSmoothingEnabled = true;
    b.drawImage(this.light, 0, 0, LW, LH);
    if (this.hasHi) {
      b.setTransform(1, 0, 0, 1, 0, 0);
      b.globalCompositeOperation = 'lighter';
      b.drawImage(this.tmp, 0, 0);
    }
    b.restore();
    b.imageSmoothingEnabled = false;
    this.drawGlows(s);
    if (s.player.alive && !s.player.hidden) { b.save(); b.globalAlpha = 0.22; this.drawCharacter(s.player, 0, s, true); b.restore(); }
    if (this.showCones) this.drawCones(s);
    this.drawParticles(dt, 0);
    this.drawTracers(dt);
    this.drawRings(dt);
    this.drawCameras(s);
    this.drawIcons(s);
    this.present();
  }

  private present() {
    const ctx = this.ctx;
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.drawImage(this.buf, 0, 0, this.bw * this.scale, this.bh * this.scale);
  }

  private drawSprite(name: string | HTMLImageElement | undefined, x: number, y: number, rot = 0, sc = 1, px = 0.5, py = 0.5) {
    const im = typeof name === 'string' ? img(name) : name;
    if (!im) return;
    const b = this.b;
    b.save();
    b.translate(Math.round(this.wx(x)), Math.round(this.wy(y)));
    if (rot) b.rotate(rot);
    if (sc !== 1) b.scale(sc, sc);
    b.drawImage(im, -im.width * px, -im.height * py);
    b.restore();
  }

  private drawExtracts(s: GameSession) {
    const b = this.b;
    b.save();
    b.setLineDash([5, 3]);
    b.lineWidth = 1;
    b.strokeStyle = s.requiredDone ? `rgba(110,255,140,${0.55 + 0.3 * Math.sin(performance.now() / 200)})` : 'rgba(130,200,150,0.25)';
    for (const e of s.extracts) b.strokeRect(this.wx(e.rect.x) + 0.5, this.wy(e.rect.y + e.rect.h) + 0.5, e.rect.w * PPU - 1, e.rect.h * PPU - 1);
    b.restore();
  }

  private drawDoorsWindows(s: GameSession) {
    const theme = s.map.theme;
    const W = s.world;
    for (const d of W.doors) {
      if (d.type === 3) {
        const off = d.anim * 0.9;
        const x = d.horizontal ? d.cell.x - off : d.cell.x, y = d.horizontal ? d.cell.y : d.cell.y + off;
        this.drawSprite('Objects/secret_door', x, y + 1, 0, 1, 0, 0);
        continue;
      }
      const spr = d.type === 1 ? 'Objects/door_blue' : d.type === 2 ? 'Objects/door_red' : theme === 'mansion' ? 'Objects/door_wood' : theme === 'office' ? 'Objects/door_glass' : 'Objects/door_metal';
      const closedDeg = d.horizontal ? 0 : 90;
      const deg = closedDeg + d.anim * 88;
      const [x, y] = d.horizontal ? [d.cell.x, d.cell.y + 0.5] : [d.cell.x + 0.5, d.cell.y];
      this.drawSprite(spr, x, y, -deg * Math.PI / 180, 1, 0, 0.5);
    }
    for (let y = 0; y < W.height; y++)
      for (let x = 0; x < W.width; x++) {
        const t = W.tileAt(x, y);
        if (t.kind === TileKind.Window) {
          const vert = (W.tileAt(x, y + 1).kind === TileKind.Wall || W.tileAt(x, y - 1).kind === TileKind.Wall)
            && !(W.tileAt(x - 1, y).kind === TileKind.Wall || W.tileAt(x + 1, y).kind === TileKind.Wall);
          this.drawSprite(W.isWindowBroken(x, y) ? 'Objects/window_broken' : 'Objects/window', x + 0.5, y + 0.5, vert ? -Math.PI / 2 : 0);
        } else if (t.kind === TileKind.Furniture && t.furniture === FurnitureType.Barrel) this.drawSprite('Objects/barrel', x + 0.5, y + 0.5);
      }
  }

  private pickupSprite(p: Pickup): string {
    switch (p.kind) {
      case PickupKind.Ammo: return 'Icons/ammo_pistol';
      case PickupKind.Medkit: return 'Icons/medkit';
      case PickupKind.Armor: return 'Icons/armor_light';
      case PickupKind.Cash: return 'Icons/cash';
      case PickupKind.Keycard: return p.itemId === 'red' ? 'Icons/keycard_red' : 'Icons/keycard_blue';
      case PickupKind.Intel: return ['ledger', 'samples', 'prototype'].includes(p.itemId) ? 'Icons/' + p.itemId : 'Icons/intel';
      case PickupKind.Weapon: return 'Icons/w_' + p.itemId;
      case PickupKind.Knives: return p.amount === 1 ? 'FX/knife_proj' : 'Icons/ammo_knives';
      default: return 'Icons/coin';
    }
  }

  private drawPickups(s: GameSession) {
    const bob = Math.sin(performance.now() / 333) * 0.05;
    for (const p of s.pickups) {
      if (p.taken) continue;
      const sc = p.kind === PickupKind.Weapon ? 0.55 : p.kind === PickupKind.Knives && p.amount === 1 ? 0.5 : 0.6;
      this.drawSprite(this.pickupSprite(p), p.pos.x, p.pos.y + bob, 0, sc);
    }
  }

  private charType(n: Npc): string {
    if (n.isTarget) return 'target';
    if (n.kind === NpcKind.Elite) return 'elite';
    if (n.kind === NpcKind.Guard) return 'guard';
    if (n.type === 'scientist' || n.type === 'worker') return n.type;
    if (n.type === 'guest') return n.id % 2 === 0 ? 'guest' : 'guest2';
    return 'staff';
  }

  private drawBodies(s: GameSession) {
    const b = this.b;
    for (const body of s.bodies) {
      if (body.hidden) continue;
      if (!this.stainedBodies.has(body.id)) { this.stainedBodies.add(body.id); if (!body.npc.alive) this.stain(body.pos.x, body.pos.y); }
      const sheet = img('Characters/' + this.charType(body.npc));
      if (!sheet) continue;
      b.save();
      b.translate(Math.round(this.wx(body.pos.x)), Math.round(this.wy(body.pos.y)));
      b.rotate(-(body.facing + Math.PI));
      if (body.npc.unconscious) b.filter = 'saturate(0.6) brightness(0.95)';
      b.drawImage(sheet, 4 * 24, 0, 24, 24, -11.5, -12, 24, 24);
      b.restore();
    }
  }

  private drawCharacter(a: Actor, dt: number, s: GameSession, rimOnly = false) {
    const isPlayer = a === s.player;
    const n = a instanceof Npc ? a : null;
    if (isPlayer && s.player.hidden) return;
    const sheet = img('Characters/' + (isPlayer ? 'player' : this.charType(n!)));
    if (!sheet) return;
    let w: WeaponDef | null = isPlayer ? s.player.inventory.currentWeapon?.def ?? null : n!.weapon?.def ?? null;
    if (n && !n.isHostile && n.state !== AIState.Attack) w = null;
    if (n && n.isHostile && [AIState.Idle, AIState.Patrol, AIState.Follow, AIState.ReturnToPatrol].includes(n.state) && s.alert < AlertLevel.Alarmed) w = null;
    const pose = !w ? 0 : w.isMelee || w.isThrown ? 3 : w.cls === WeaponClass.Pistol ? 1 : 2;
    const moving = isPlayer ? s.player.moving : n!.speed > 0.15;
    const frame = moving ? Math.floor(a.moveAnim * 2.6) % 4 : 0;
    let swing = this.swing.get(a.id) ?? 0, swingOff = 0;
    if (!rimOnly && swing > 0) { swing -= dt; this.swing.set(a.id, swing); swingOff = (55 + (-65 - 55) * (1 - swing / 0.18)) * Math.PI / 180; }
    else if (swing > 0) swingOff = (55 + (-65 - 55) * (1 - swing / 0.18)) * Math.PI / 180;
    let rec = this.recoil.get(a.id) ?? 0;
    if (!rimOnly && rec > 0) { rec = Math.max(0, rec - dt * 1.2); this.recoil.set(a.id, rec); }
    let fl = this.flash.get(a.id) ?? 0;
    if (!rimOnly && fl > 0) { fl -= dt; this.flash.set(a.id, fl); }
    const crouched = isPlayer && s.player.crouched;
    const sc = crouched ? 0.86 : 1;
    const bob = moving && !crouched ? Math.sin(a.moveAnim * 5.2) * 0.02 : 0;
    const b = this.b;
    b.save();
    b.translate(Math.round(this.wx(a.pos.x)), Math.round(this.wy(a.pos.y)));
    b.rotate(-(a.facing + swingOff * 0.35));
    b.scale(sc + bob, sc - bob);
    if (rimOnly) b.filter = 'brightness(2.2) sepia(1) hue-rotate(170deg)';
    else if (fl > 0) b.filter = 'sepia(1) saturate(4) hue-rotate(-30deg)';
    else if (isPlayer && s.player.inVent) b.filter = 'brightness(0.6)';
    b.drawImage(sheet, frame * 24, 24, 24, 24, -11.5, -12, 24, 24);
    const rp = rec * PPU;
    if (w) {
      const wim = img('Weapons/' + w.id);
      if (wim) {
        const hand = pose === 1 ? [0.38, 0] : pose === 2 ? [0.28, 0.02] : [0.36, 0.26];
        b.save();
        b.translate(hand[0] * PPU - rp * 1.5, hand[1] * PPU);
        b.rotate(-swingOff);
        b.drawImage(wim, -2, -4);
        b.restore();
      }
    }
    b.drawImage(sheet, pose * 24, 0, 24, 24, -11.5 - rp, -12, 24, 24);
    b.restore();
  }

  private drawProjectiles(s: GameSession) {
    for (const p of s.projectiles) if (!p.done) this.drawSprite(p.kind === ProjectileKind.Knife ? 'FX/knife_proj' : 'FX/coin_proj', p.pos.x, p.pos.y, -p.angle);
  }

  private drawParticles(dt: number, layer: 0 | 1) {
    const b = this.b;
    for (let i = this.particles.length - 1; i >= 0; i--) {
      const p = this.particles[i];
      const glowLayer = p.add ? 0 : 1;
      if (glowLayer !== layer) continue;
      p.life -= dt;
      if (p.life <= 0) { this.particles.splice(i, 1); continue; }
      p.x += p.vx * dt; p.y += p.vy * dt;
      const k = Math.max(0, 1 - p.drag * dt);
      p.vx *= k; p.vy *= k;
      p.rot += p.spin * dt;
      p.scale += p.grow * dt;
      const im = p.frames ? p.frames[Math.min(p.frames.length - 1, Math.floor((1 - p.life / p.max) * p.frames.length))] : p.image;
      if (!im) continue;
      b.save();
      b.globalAlpha = p.alpha * Math.min(1, (p.life / p.max) * 2.5);
      if (p.add) b.globalCompositeOperation = 'lighter';
      b.translate(this.wx(p.x), this.wy(p.y));
      b.rotate(-p.rot);
      b.scale(p.scale, p.scale);
      if (p.color && p.add) {
        b.drawImage(tinted(im, p.color), -im.width / 2, -im.height / 2);
      } else b.drawImage(im, p.w ? -im.width * p.w : -im.width / 2, -im.height / 2);
      b.restore();
    }
  }

  private drawTracers(dt: number) {
    const b = this.b;
    b.save();
    b.globalCompositeOperation = 'lighter';
    b.lineWidth = 1;
    for (let i = this.tracers.length - 1; i >= 0; i--) {
      const t = this.tracers[i];
      t.life -= dt;
      if (t.life <= 0) { this.tracers.splice(i, 1); continue; }
      b.strokeStyle = t.player ? 'rgba(255,230,150,0.7)' : 'rgba(255,140,100,0.7)';
      b.beginPath(); b.moveTo(this.wx(t.x1), this.wy(t.y1)); b.lineTo(this.wx(t.x2), this.wy(t.y2)); b.stroke();
    }
    b.restore();
  }

  private drawRings(dt: number) {
    const b = this.b;
    for (let i = this.rings.length - 1; i >= 0; i--) {
      const r = this.rings[i];
      r.life -= dt;
      if (r.life <= 0) { this.rings.splice(i, 1); continue; }
      const k = 1 - r.life / 0.5;
      b.strokeStyle = r.color.replace('A', (0.5 * (1 - k)).toFixed(2));
      b.lineWidth = 1;
      b.beginPath(); b.arc(this.wx(r.x), this.wy(r.y), Math.max(1, r.r * PPU * k), 0, Math.PI * 2); b.stroke();
    }
  }

  private drawGlows(s: GameSession) {
    const b = this.b, g = img('FX/glow');
    if (!g) return;
    b.save();
    b.globalCompositeOperation = 'lighter';
    const t = performance.now() / 1000;
    for (const l of s.lights) {
      if (!l.on) continue;
      let a = 0.18 * l.intensity;
      if (l.flicker) { const n = 0.5 + 0.5 * Math.sin(t * 23 + l.pos.x * 7) * Math.sin(t * 7.1 + l.pos.y); a *= n > 0.25 ? 0.7 + n * 0.5 : 0.1; }
      const size = Math.min(l.radius * 0.28, 1.6);
      b.globalAlpha = a;
      const col = `rgb(${l.r * 255 | 0},${l.g * 255 | 0},${l.b * 255 | 0})`;
      const im = tinted(g, col);
      const px = size * 64;
      b.drawImage(im, this.wx(l.pos.x) - px / 2, this.wy(l.pos.y) - px / 2, px, px);
    }
    b.restore();
  }

  /** RGBA (alpha 0..1) per state, as VisionConeView.StateColor. */
  private coneColor(n: Npc): number[] | null {
    if (n.isCamera) return [255, 90, 90, 38 / 255];
    switch (n.state) {
      case AIState.Attack: case AIState.Chase: return [255, 60, 60, 46 / 255];
      case AIState.Search: case AIState.Investigate: return [255, 150, 60, 40 / 255];
      case AIState.Suspicious: return [255, 220, 90, 40 / 255];
      case AIState.Panic: case AIState.Flee: case AIState.Cower: return null;
    }
    const base = n.isCivilian ? [200, 230, 255, 16 / 255] : [235, 240, 255, 26 / 255];
    if (n.suspicion > 0.05) {
      const t = Math.min(1, n.suspicion * 2), to = [255, 220, 90, 40 / 255];
      return [235, 240, 255, 26 / 255].map((v, i) => v + (to[i] - v) * t);
    }
    return base;
  }

  private drawCones(s: GameSession) {
    if ((this.coneFrame++ & 1) === 0) {
      this.cones = [];
      for (const n of s.npcs) {
        if (n.down || n.state === AIState.Disabled) continue;
        const col = this.coneColor(n);
        if (!col) continue;
        if (Math.hypot(n.pos.x - this.camX, n.pos.y - this.camY) > 28) continue;
        const alerted = [AIState.Attack, AIState.Chase, AIState.Search, AIState.Investigate].includes(n.state);
        const fov = n.viewAngle * (alerted && !n.isCamera ? 1.45 : 1);
        const range = n.viewRange * (alerted ? 1 : 0.85);
        const pts = [n.pos.x, n.pos.y];
        const R = 22;
        for (let i = 0; i <= R; i++) {
          const a = n.facing - fov / 2 + fov * i / R;
          const dir = Vec2.fromAngle(a);
          const hit = s.world.raycast(n.pos, dir, range, RayMode.Sight);
          const p = hit.hit ? hit.point : n.pos.add(dir.mul(range));
          pts.push(p.x, p.y);
        }
        this.cones.push({ pts, color: col, range });
      }
    }
    const b = this.b;
    for (const c of this.cones) {
      const [r, g, bl, a] = c.color;
      const ox = this.wx(c.pts[0]), oy = this.wy(c.pts[1]);
      // bright at the eyes, fading towards the edge of sight (the mesh's vertex-colour falloff)
      const grad = b.createRadialGradient(ox, oy, 0, ox, oy, c.range * PPU);
      grad.addColorStop(0, `rgba(${r | 0},${g | 0},${bl | 0},${Math.min(1, a * 2).toFixed(3)})`);
      grad.addColorStop(1, `rgba(${r | 0},${g | 0},${bl | 0},${(a * 0.35).toFixed(3)})`);
      b.fillStyle = grad;
      b.beginPath();
      b.moveTo(ox, oy);
      for (let i = 2; i < c.pts.length; i += 2) b.lineTo(this.wx(c.pts[i]), this.wy(c.pts[i + 1]));
      b.closePath();
      b.fill();
    }
  }

  private drawCameras(s: GameSession) {
    for (const n of s.npcs) {
      if (!n.isCamera) continue;
      const off = n.down || n.state === AIState.Disabled;
      this.drawSprite(off ? 'Objects/camera_off' : 'Objects/camera', n.pos.x, n.pos.y, -n.facing, 1, 0.2, 0.5);
    }
  }

  private drawIcons(s: GameSession) {
    const t = performance.now() / 1000;
    const pulse = 0.75 + 0.25 * Math.sin(t * 4);
    const b = this.b;
    for (const it of s.interactables) {
      if (it.secret) continue;
      const done = (it.kind === InteractKind.CameraTerminal || it.kind === InteractKind.DownloadTerminal) && it.used;
      const d = Vec2.distance(it.pos, s.player.pos);
      b.globalAlpha = done ? 0.25 : d < 6 ? pulse : 0.45;
      const icon = it.kind === InteractKind.PowerBox ? 'Objects/icon_power' : it.kind === InteractKind.Distraction ? 'Objects/icon_music' : it.kind === InteractKind.Stairs ? 'Objects/icon_stairs' : 'Objects/icon_terminal';
      this.drawSprite(icon, it.pos.x, it.pos.y + 0.55);
    }
    b.globalAlpha = 1;
    for (const n of s.npcs) {
      if (n.down || n.isCamera) continue;
      let icon: string | null = null, alpha = 1;
      if ([AIState.Attack, AIState.Chase, AIState.Panic, AIState.Flee].includes(n.state)) icon = 'UI/alert';
      else if ([AIState.Suspicious, AIState.Investigate, AIState.Search].includes(n.state)) icon = 'UI/question';
      else if (n.suspicion > 0.15) { icon = 'UI/question'; alpha = Math.min(1, n.suspicion * 2); }
      if (icon) {
        b.globalAlpha = alpha;
        const sc = icon === 'UI/alert' ? 1 + Math.sin(t * 8) * 0.1 : 1;
        this.drawSprite(icon, n.pos.x, n.pos.y + 0.95, 0, sc);
        b.globalAlpha = 1;
      }
      if (n.isTarget) this.drawSprite('UI/target_marker', n.pos.x, n.pos.y + (icon ? 1.55 : 1.0) + Math.sin(t * 3) * 0.08);
    }
  }
}

// Tinted copies of white sprites (glows) are cached per colour.
const tintCache = new WeakMap<HTMLImageElement, Map<string, HTMLCanvasElement>>();
function tinted(im: HTMLImageElement, color: string): HTMLCanvasElement {
  let per = tintCache.get(im);
  if (!per) { per = new Map(); tintCache.set(im, per); }
  let c = per.get(color);
  if (c) return c;
  c = document.createElement('canvas');
  c.width = im.width; c.height = im.height;
  const x = c.getContext('2d')!;
  x.drawImage(im, 0, 0);
  x.globalCompositeOperation = 'source-in';
  x.fillStyle = color;
  x.fillRect(0, 0, c.width, c.height);
  per.set(color, c);
  return c;
}
