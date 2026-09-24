// Port of World.cs, LightMap.cs, Pathfinder.cs
import { Vec2, Int2, clamp, clamp01 } from './math';
import { MapData, Tile, TileKind, FurnitureType, DoorType, TF, tileIs, doorTypeFromChar, EntityDef } from './map';

export enum MoverKind { Npc, Player, PlayerCrouched }
export enum RayMode { Sight, Bullets, Light, NpcWalk, Projectile }

export class DoorState {
  index = 0; cell!: Int2; type = DoorType.Normal; open = false; locked = false; horizontal = false; autoClose = 0; anim = 0;
  get keyId(): string | null { return this.type === DoorType.LockedBlue ? 'blue' : this.type === DoorType.LockedRed ? 'red' : null; }
}

export interface RayHit { hit: boolean; distance: number; point: Vec2; cell: Int2; normal: Vec2; }

export class World {
  readonly width: number; readonly height: number;
  readonly doors: DoorState[] = [];
  private doorIndex: Int32Array;
  private broken: Uint8Array;
  private light: Float32Array;

  constructor(readonly map: MapData) {
    this.width = map.width; this.height = map.height;
    const n = this.width * this.height;
    this.doorIndex = new Int32Array(n).fill(-1);
    this.broken = new Uint8Array(n);
    this.light = new Float32Array(n).fill(map.ambient);
    for (let y = 0; y < this.height; y++)
      for (let x = 0; x < this.width; x++) {
        const t = map.get(x, y);
        if (t.kind !== TileKind.Door) continue;
        const d = new DoorState();
        d.index = this.doors.length; d.cell = new Int2(x, y); d.type = doorTypeFromChar(t.symbol);
        d.locked = d.type !== DoorType.Normal;
        d.horizontal = this.isWallLike(x - 1, y) || this.isWallLike(x + 1, y);
        this.doorIndex[y * this.width + x] = d.index;
        this.doors.push(d);
      }
  }

  private isWallLike(x: number, y: number) {
    const t = this.map.get(x, y);
    return t.kind === TileKind.Wall || t.kind === TileKind.Window || t.kind === TileKind.Door || t.kind === TileKind.Void
      || (t.kind === TileKind.Furniture && t.furniture === FurnitureType.Bookshelf);
  }

  inBounds(x: number, y: number) { return x >= 0 && y >= 0 && x < this.width && y < this.height; }
  tileAt(x: number, y: number): Tile { return this.map.get(x, y); }
  doorAt(x: number, y: number): DoorState | null {
    if (!this.inBounds(x, y)) return null;
    const i = this.doorIndex[y * this.width + x];
    return i < 0 ? null : this.doors[i];
  }
  isWindowBroken(x: number, y: number) { return this.inBounds(x, y) && this.broken[y * this.width + x] === 1; }
  breakWindow(x: number, y: number) { if (this.inBounds(x, y)) this.broken[y * this.width + x] = 1; }
  lightAt(x: number, y: number) { return this.inBounds(x, y) ? this.light[y * this.width + x] : 0; }
  lightAtPos(p: Vec2) { return this.lightAt(Math.floor(p.x), Math.floor(p.y)); }
  setLight(x: number, y: number, v: number) { if (this.inBounds(x, y)) this.light[y * this.width + x] = v; }

  isSolid(x: number, y: number, mover: MoverKind): boolean {
    if (!this.inBounds(x, y)) return true;
    const t = this.map.tiles[y * this.width + x];
    switch (t.kind) {
      case TileKind.Void: case TileKind.Wall: case TileKind.Water: case TileKind.Window: return true;
      case TileKind.Door: return !this.doors[this.doorIndex[y * this.width + x]].open;
      case TileKind.Vent: return mover !== MoverKind.PlayerCrouched;
      case TileKind.Furniture: return tileIs(t, TF.Solid);
      default: return false;
    }
  }

  blocksSight(x: number, y: number, crouched: boolean, target: Vec2): boolean {
    if (!this.inBounds(x, y)) return true;
    const t = this.map.tiles[y * this.width + x];
    switch (t.kind) {
      case TileKind.Void: case TileKind.Wall: case TileKind.Vent: return true;
      case TileKind.Door: return !this.doors[this.doorIndex[y * this.width + x]].open;
      case TileKind.Furniture:
        if (tileIs(t, TF.BlocksSight)) return true;
        if (crouched && tileIs(t, TF.LowCover)) {
          const dx = x + 0.5 - target.x, dy = y + 0.5 - target.y;
          return dx * dx + dy * dy < 2.6 * 2.6;
        }
        return false;
      default: return false;
    }
  }

  blocksBullets(x: number, y: number): boolean {
    if (!this.inBounds(x, y)) return true;
    const t = this.map.tiles[y * this.width + x];
    switch (t.kind) {
      case TileKind.Void: case TileKind.Wall: case TileKind.Vent: return true;
      case TileKind.Window: return !this.broken[y * this.width + x];
      case TileKind.Door: return !this.doors[this.doorIndex[y * this.width + x]].open;
      case TileKind.Furniture: return tileIs(t, TF.BlocksBullets);
      default: return false;
    }
  }

  isWalkableForNpc(x: number, y: number): boolean {
    if (!this.inBounds(x, y)) return false;
    const t = this.map.tiles[y * this.width + x];
    switch (t.kind) {
      case TileKind.Floor: case TileKind.Stairs: return true;
      case TileKind.Door: { const d = this.doors[this.doorIndex[y * this.width + x]]; return d.type !== DoorType.Secret || d.open; }
      case TileKind.Furniture: return !tileIs(t, TF.Solid);
      default: return false;
    }
  }

  moveCircle(pos: Vec2, radius: number, delta: Vec2, mover: MoverKind): Vec2 {
    const len = delta.length;
    if (len < 1e-7) return pos;
    const steps = Math.max(1, Math.ceil(len / (radius * 0.5)));
    const step = delta.div(steps);
    for (let s = 0; s < steps; s++) pos = this.resolveCircle(pos.add(step), radius, mover);
    return pos;
  }

  resolveCircle(pos: Vec2, radius: number, mover: MoverKind): Vec2 {
    let px = pos.x, py = pos.y;
    for (let iter = 0; iter < 3; iter++) {
      let moved = false;
      const minX = Math.floor(px - radius), maxX = Math.floor(px + radius);
      const minY = Math.floor(py - radius), maxY = Math.floor(py + radius);
      for (let y = minY; y <= maxY; y++)
        for (let x = minX; x <= maxX; x++) {
          if (!this.isSolid(x, y, mover)) continue;
          const cx = clamp(px, x, x + 1), cy = clamp(py, y, y + 1);
          const dx = px - cx, dy = py - cy;
          const d2 = dx * dx + dy * dy;
          if (d2 >= radius * radius) continue;
          if (d2 > 1e-10) {
            const d = Math.sqrt(d2), push = radius - d;
            px += dx / d * push; py += dy / d * push;
          } else {
            const left = px - x, right = x + 1 - px, down = py - y, up = y + 1 - py;
            const m = Math.min(left, right, down, up);
            if (m === left) px = x - radius;
            else if (m === right) px = x + 1 + radius;
            else if (m === down) py = y - radius;
            else py = y + 1 + radius;
          }
          moved = true;
        }
      if (!moved) break;
    }
    return new Vec2(px, py);
  }

  circleOverlapsSolid(pos: Vec2, radius: number, mover: MoverKind): boolean {
    const minX = Math.floor(pos.x - radius), maxX = Math.floor(pos.x + radius);
    const minY = Math.floor(pos.y - radius), maxY = Math.floor(pos.y + radius);
    for (let y = minY; y <= maxY; y++)
      for (let x = minX; x <= maxX; x++) {
        if (!this.isSolid(x, y, mover)) continue;
        const cx = clamp(pos.x, x, x + 1), cy = clamp(pos.y, y, y + 1);
        const dx = pos.x - cx, dy = pos.y - cy;
        if (dx * dx + dy * dy < radius * radius - 1e-5) return true;
      }
    return false;
  }

  private blocks(x: number, y: number, mode: RayMode, crouched: boolean, target: Vec2): boolean {
    switch (mode) {
      case RayMode.Sight: return this.blocksSight(x, y, crouched, target);
      case RayMode.Bullets: return this.blocksBullets(x, y);
      case RayMode.NpcWalk: return !this.isWalkableForNpc(x, y);
      case RayMode.Projectile: return this.blocksBullets(x, y) || this.tileAt(x, y).kind === TileKind.Window;
      default: {
        const t = this.tileAt(x, y);
        if (t.kind === TileKind.Wall || t.kind === TileKind.Void) return true;
        if (t.kind === TileKind.Door) { const d = this.doorAt(x, y); return d !== null && !d.open; }
        return t.kind === TileKind.Furniture && tileIs(t, TF.BlocksSight) && t.furniture !== FurnitureType.Tree;
      }
    }
  }

  /** Grid DDA raycast (C# World.Raycast with RayMode). */
  raycast(origin: Vec2, dirIn: Vec2, maxDist: number, mode: RayMode, crouched = false, target: Vec2 = Vec2.Zero): RayHit {
    const miss: RayHit = { hit: false, distance: maxDist, point: origin.add(dirIn.mul(maxDist)), cell: new Int2(0, 0), normal: Vec2.Zero };
    if (dirIn.sqrLength < 1e-10) return miss;
    const dir = dirIn.normalized;
    miss.point = origin.add(dir.mul(maxDist));
    let x = Math.floor(origin.x), y = Math.floor(origin.y);
    const stepX = dir.x > 0 ? 1 : -1, stepY = dir.y > 0 ? 1 : -1;
    const tDX = Math.abs(dir.x) < 1e-9 ? Number.MAX_VALUE : Math.abs(1 / dir.x);
    const tDY = Math.abs(dir.y) < 1e-9 ? Number.MAX_VALUE : Math.abs(1 / dir.y);
    let tMaxX = Math.abs(dir.x) < 1e-9 ? Number.MAX_VALUE : (dir.x > 0 ? (x + 1 - origin.x) : (origin.x - x)) * tDX;
    let tMaxY = Math.abs(dir.y) < 1e-9 ? Number.MAX_VALUE : (dir.y > 0 ? (y + 1 - origin.y) : (origin.y - y)) * tDY;
    let t = 0, nx = 0, ny = 0, guard = 0;
    while (t <= maxDist && guard++ < 4096) {
      if (t > 0 && this.blocks(x, y, mode, crouched, target))
        return { hit: true, distance: t, point: origin.add(dir.mul(t)), cell: new Int2(x, y), normal: new Vec2(nx, ny) };
      if (tMaxX < tMaxY) { t = tMaxX; tMaxX += tDX; x += stepX; nx = -stepX; ny = 0; }
      else { t = tMaxY; tMaxY += tDY; y += stepY; nx = 0; ny = -stepY; }
    }
    return miss;
  }

  hasLineOfSight(from: Vec2, to: Vec2, crouched = false): boolean {
    const d = to.sub(from);
    const dist = d.length;
    if (dist < 1e-4) return true;
    const hit = this.raycast(from, d.div(dist), dist, RayMode.Sight, crouched, to);
    if (!hit.hit) return true;
    return hit.cell.eq(Int2.fromWorld(to));
  }

  hasClearWalk(from: Vec2, to: Vec2, radius: number): boolean {
    const d = to.sub(from);
    const dist = d.length;
    if (dist < 1e-4) return true;
    const n = d.div(dist);
    const side = n.perp.mul(radius);
    return !this.raycast(from, n, dist, RayMode.NpcWalk).hit
      && !this.raycast(from.add(side), n, dist, RayMode.NpcWalk).hit
      && !this.raycast(from.sub(side), n, dist, RayMode.NpcWalk).hit;
  }

  nearestWalkable(c: Int2, maxRadius = 6): Int2 {
    if (this.isWalkableForNpc(c.x, c.y)) return c;
    for (let r = 1; r <= maxRadius; r++)
      for (let dy = -r; dy <= r; dy++)
        for (let dx = -r; dx <= r; dx++) {
          if (Math.abs(dx) !== r && Math.abs(dy) !== r) continue;
          if (this.isWalkableForNpc(c.x + dx, c.y + dy)) return new Int2(c.x + dx, c.y + dy);
        }
    return c;
  }
}

// ------------------------------------------------------------------ lighting

export class LightDef {
  pos = Vec2.Zero; radius = 6; intensity = 1; r = 1; g = 0.9; b = 0.75; group: string | null = null; flicker = false; on = true;
  static fromEntity(e: EntityDef, map: MapData): LightDef {
    const l = new LightDef();
    l.pos = map.parsePoint(e.str('at')!);
    l.radius = e.float('r', 6); l.intensity = e.float('i', 1); l.group = e.str('group'); l.flicker = e.bool('flicker');
    const v = parseInt((e.str('color', 'ffe6c0') as string).replace('#', ''), 16);
    l.r = ((v >> 16) & 255) / 255; l.g = ((v >> 8) & 255) / 255; l.b = (v & 255) / 255;
    return l;
  }
}

export const AMBIENT_RGB = [0.55, 0.62, 0.85];

export function bakeLights(world: World, lights: LightDef[], samples: number, rgbOut: Float32Array | null) {
  const amb = world.map.ambient;
  for (let y = 0; y < world.height; y++)
    for (let x = 0; x < world.width; x++) {
      let b = amb;
      const p = new Vec2(x + 0.5, y + 0.5);
      for (const l of lights) b += contribution(world, l, p);
      world.setLight(x, y, clamp01(b));
    }
  if (!rgbOut) return;
  const w = world.width * samples, h = world.height * samples, inv = 1 / samples;
  for (let sy = 0; sy < h; sy++)
    for (let sx = 0; sx < w; sx++) {
      const p = new Vec2((sx + 0.5) * inv, (sy + 0.5) * inv);
      let r = amb * AMBIENT_RGB[0], g = amb * AMBIENT_RGB[1], bl = amb * AMBIENT_RGB[2];
      for (const l of lights) {
        const c = contribution(world, l, p);
        if (c <= 0) continue;
        r += c * l.r; g += c * l.g; bl += c * l.b;
      }
      const i = (sy * w + sx) * 3;
      rgbOut[i] = Math.min(r, 1.4); rgbOut[i + 1] = Math.min(g, 1.4); rgbOut[i + 2] = Math.min(bl, 1.4);
    }
}

function contribution(world: World, l: LightDef, p: Vec2): number {
  if (!l.on) return 0;
  const dx = p.x - l.pos.x, dy = p.y - l.pos.y;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d >= l.radius) return 0;
  let f = 1 - d / l.radius;
  f = f * f * (3 - 2 * f);
  if (d > 0.75) {
    const hit = world.raycast(l.pos, new Vec2(dx / d, dy / d), d - 0.05, RayMode.Light);
    if (hit.hit && !hit.cell.eq(Int2.fromWorld(p))) return 0;
  }
  return f * l.intensity;
}

// ------------------------------------------------------------------ pathfinding

export interface PathNode { pos: Vec2; teleport: boolean; }

export class Pathfinder {
  private w: number; private h: number;
  private g: Float32Array; private parent: Int32Array; private stamp: Int32Array; private viaLink: Uint8Array;
  private search = 0;
  private heap: MinHeap;
  private links = new Map<number, number[]>();
  maxExpansions = 12000;

  constructor(private world: World) {
    this.w = world.width; this.h = world.height;
    const n = this.w * this.h;
    this.g = new Float32Array(n); this.parent = new Int32Array(n); this.stamp = new Int32Array(n); this.viaLink = new Uint8Array(n);
    this.heap = new MinHeap(n);
  }

  addLink(a: Int2, b: Int2) { this.add(a, b); this.add(b, a); }
  private add(a: Int2, b: Int2) {
    const ia = a.y * this.w + a.x, ib = b.y * this.w + b.x;
    if (!this.links.has(ia)) this.links.set(ia, []);
    this.links.get(ia)!.push(ib);
  }

  findPath(from: Vec2, to: Vec2, radius = 0.3): PathNode[] | null {
    const W = this.world;
    const start = W.nearestWalkable(Int2.fromWorld(from), 2);
    const goal = W.nearestWalkable(Int2.fromWorld(to), 3);
    if (!W.isWalkableForNpc(start.x, start.y) || !W.isWalkableForNpc(goal.x, goal.y)) return null;
    this.search++;
    this.heap.clear();
    const si = start.y * this.w + start.x, gi = goal.y * this.w + goal.x;
    this.touch(si); this.g[si] = 0; this.parent[si] = -1;
    this.heap.push(si, heur(start.x, start.y, goal.x, goal.y));
    let exp = 0, found = false;
    const DX = [1, -1, 0, 0, 1, 1, -1, -1], DY = [0, 0, 1, -1, 1, -1, 1, -1];
    while (this.heap.count > 0) {
      const cur = this.heap.pop();
      if (cur === gi) { found = true; break; }
      if (++exp > this.maxExpansions) break;
      const cx = cur % this.w, cy = (cur / this.w) | 0;
      const gc = this.g[cur];
      for (let d = 0; d < 8; d++) {
        const nx = cx + DX[d], ny = cy + DY[d];
        if (!W.isWalkableForNpc(nx, ny)) continue;
        if (d >= 4 && (!W.isWalkableForNpc(cx + DX[d], cy) || !W.isWalkableForNpc(cx, cy + DY[d]))) continue;
        let cost = d >= 4 ? 1.4142 : 1;
        if (W.tileAt(nx, ny).kind === TileKind.Door) cost += 0.6;
        this.relax(cur, ny * this.w + nx, gc + cost, goal, false);
      }
      const lk = this.links.get(cur);
      if (lk) for (const li of lk) this.relax(cur, li, gc + 2, goal, true);
    }
    if (!found) return null;
    const cells: number[] = [];
    for (let c = gi; c !== -1; c = this.parent[c]) cells.push(c);
    cells.reverse();
    const raw: PathNode[] = cells.map((c, i) => ({ pos: new Vec2(c % this.w + 0.5, ((c / this.w) | 0) + 0.5), teleport: i > 0 && this.viaLink[c] === 1 }));
    const tc = Int2.fromWorld(to);
    if (raw.length > 0 && Vec2.distance(to, raw[raw.length - 1].pos) < 0.75 && W.isWalkableForNpc(tc.x, tc.y))
      raw[raw.length - 1] = { pos: to, teleport: raw[raw.length - 1].teleport };
    return this.smooth(from, raw, radius);
  }

  tryGetLink(c: Int2): Int2 | null {
    const l = this.links.get(c.y * this.w + c.x);
    if (!l || l.length === 0) return null;
    return new Int2(l[0] % this.w, (l[0] / this.w) | 0);
  }

  private relax(from: number, to: number, g: number, goal: Int2, viaLink: boolean) {
    if (this.stamp[to] !== this.search) { this.touch(to); this.g[to] = Number.MAX_VALUE; }
    if (g >= this.g[to]) return;
    this.g[to] = g; this.parent[to] = from; this.viaLink[to] = viaLink ? 1 : 0;
    this.heap.pushOrDecrease(to, g + heur(to % this.w, (to / this.w) | 0, goal.x, goal.y));
  }
  private touch(i: number) { this.stamp[i] = this.search; this.viaLink[i] = 0; }

  private smooth(from: Vec2, raw: PathNode[], radius: number): PathNode[] {
    const result: PathNode[] = [];
    let anchor = from, i = 0;
    while (i < raw.length) {
      let best = i;
      for (let j = i + 1; j < raw.length; j++) {
        if (raw[j].teleport) break;
        if (this.world.hasClearWalk(anchor, raw[j].pos, radius + 0.05)) best = j; else break;
      }
      result.push(raw[best]);
      anchor = raw[best].pos;
      i = best + 1;
    }
    if (result.length > 1 && Vec2.distance(result[0].pos, from) < 0.2 && !result[0].teleport) result.shift();
    return result;
  }
}

function heur(ax: number, ay: number, bx: number, by: number) {
  const dx = Math.abs(ax - bx), dy = Math.abs(ay - by);
  return dx + dy + (1.4142 - 2) * Math.min(dx, dy);
}

class MinHeap {
  private heap: Int32Array; private key: Float64Array; private pos: Int32Array;
  count = 0;
  constructor(n: number) { this.heap = new Int32Array(n); this.key = new Float64Array(n); this.pos = new Int32Array(n); }
  clear() { for (let i = 0; i < this.count; i++) this.pos[this.heap[i]] = 0; this.count = 0; }
  push(item: number, key: number) { this.pushOrDecrease(item, key); }
  pushOrDecrease(item: number, key: number) {
    const p = this.pos[item] - 1;
    if (p < 0) { const q = this.count++; this.heap[q] = item; this.pos[item] = q + 1; this.key[item] = key; this.up(q); }
    else if (key < this.key[item]) { this.key[item] = key; this.up(p); }
  }
  pop(): number {
    const top = this.heap[0];
    this.pos[top] = 0;
    this.count--;
    if (this.count > 0) { this.heap[0] = this.heap[this.count]; this.pos[this.heap[0]] = 1; this.down(0); }
    return top;
  }
  private up(i: number) {
    while (i > 0) { const p = (i - 1) >> 1; if (this.key[this.heap[p]] <= this.key[this.heap[i]]) break; this.swap(i, p); i = p; }
  }
  private down(i: number) {
    for (;;) {
      const l = i * 2 + 1, r = l + 1; let s = i;
      if (l < this.count && this.key[this.heap[l]] < this.key[this.heap[s]]) s = l;
      if (r < this.count && this.key[this.heap[r]] < this.key[this.heap[s]]) s = r;
      if (s === i) break;
      this.swap(i, s); i = s;
    }
  }
  private swap(a: number, b: number) {
    const t = this.heap[a]; this.heap[a] = this.heap[b]; this.heap[b] = t;
    this.pos[this.heap[a]] = a + 1; this.pos[this.heap[b]] = b + 1;
  }
}
