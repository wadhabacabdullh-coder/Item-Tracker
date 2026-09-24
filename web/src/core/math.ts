// Port of Assets/Scripts/Core/Math (Vec2, Int2, Rect, MathUtil, Rng).
// Vectors are immutable here (C# uses value-type structs), so every operation returns a new instance.

export class Vec2 {
  constructor(public readonly x: number, public readonly y: number) {}
  static readonly Zero = new Vec2(0, 0);
  get length(): number { return Math.sqrt(this.x * this.x + this.y * this.y); }
  get sqrLength(): number { return this.x * this.x + this.y * this.y; }
  get normalized(): Vec2 { const l = this.length; return l > 1e-6 ? new Vec2(this.x / l, this.y / l) : Vec2.Zero; }
  get angle(): number { return Math.atan2(this.y, this.x); }
  get perp(): Vec2 { return new Vec2(-this.y, this.x); }
  static fromAngle(r: number): Vec2 { return new Vec2(Math.cos(r), Math.sin(r)); }
  add(b: Vec2): Vec2 { return new Vec2(this.x + b.x, this.y + b.y); }
  sub(b: Vec2): Vec2 { return new Vec2(this.x - b.x, this.y - b.y); }
  mul(s: number): Vec2 { return new Vec2(this.x * s, this.y * s); }
  div(s: number): Vec2 { return new Vec2(this.x / s, this.y / s); }
  neg(): Vec2 { return new Vec2(-this.x, -this.y); }
  eq(b: Vec2): boolean { return this.x === b.x && this.y === b.y; }
  static dot(a: Vec2, b: Vec2): number { return a.x * b.x + a.y * b.y; }
  static distance(a: Vec2, b: Vec2): number { const dx = a.x - b.x, dy = a.y - b.y; return Math.sqrt(dx * dx + dy * dy); }
  static sqrDistance(a: Vec2, b: Vec2): number { const dx = a.x - b.x, dy = a.y - b.y; return dx * dx + dy * dy; }
  toString(): string { return `(${this.x.toFixed(2)}, ${this.y.toFixed(2)})`; }
}

export class Int2 {
  constructor(public readonly x: number, public readonly y: number) {}
  get center(): Vec2 { return new Vec2(this.x + 0.5, this.y + 0.5); }
  static fromWorld(p: Vec2): Int2 { return new Int2(Math.floor(p.x), Math.floor(p.y)); }
  add(b: Int2): Int2 { return new Int2(this.x + b.x, this.y + b.y); }
  sub(b: Int2): Int2 { return new Int2(this.x - b.x, this.y - b.y); }
  eq(b: Int2): boolean { return this.x === b.x && this.y === b.y; }
  get key(): number { return this.x * 100000 + this.y; }
}

export class Rect {
  constructor(public x: number, public y: number, public w: number, public h: number) {}
  get center(): Vec2 { return new Vec2(this.x + this.w * 0.5, this.y + this.h * 0.5); }
  contains(p: Vec2): boolean { return p.x >= this.x && p.x <= this.x + this.w && p.y >= this.y && p.y <= this.y + this.h; }
}

export const PI = Math.PI;
export const TWO_PI = Math.PI * 2;
export const DEG2RAD = Math.PI / 180;
export const RAD2DEG = 180 / Math.PI;

export const clamp = (v: number, a: number, b: number) => (v < a ? a : v > b ? b : v);
export const clamp01 = (v: number) => clamp(v, 0, 1);
export const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
export function moveTowards(cur: number, target: number, maxDelta: number): number {
  if (Math.abs(target - cur) <= maxDelta) return target;
  return cur + Math.sign(target - cur) * maxDelta;
}
export function wrapAngle(a: number): number {
  while (a > PI) a -= TWO_PI;
  while (a <= -PI) a += TWO_PI;
  return a;
}
export const angleDelta = (from: number, to: number) => wrapAngle(to - from);
export function rotateTowards(cur: number, target: number, maxDelta: number): number {
  const d = angleDelta(cur, target);
  if (Math.abs(d) <= maxDelta) return target;
  return wrapAngle(cur + Math.sign(d) * maxDelta);
}
export function vecMoveTowards(cur: Vec2, target: Vec2, maxDelta: number): Vec2 {
  const d = target.sub(cur);
  const len = d.length;
  if (len <= maxDelta || len < 1e-6) return target;
  return cur.add(d.div(len).mul(maxDelta));
}

/** Deterministic xorshift32, bit-identical to the C# Rng. */
export class Rng {
  private state: number;
  constructor(seed: number) {
    let s = (Math.imul(seed | 0, 2654435761 | 0) + 1) >>> 0;
    if (s === 0) s = 1;
    this.state = s;
  }
  nextUInt(): number {
    let x = this.state;
    x ^= x << 13; x >>>= 0;
    x ^= x >>> 17;
    x ^= x << 5; x >>>= 0;
    this.state = x;
    return x;
  }
  value(): number { return (this.nextUInt() & 0xffffff) / 16777216; }
  range(min: number, max: number): number { return min + (max - min) * this.value(); }
  rangeInt(min: number, maxExclusive: number): number {
    return maxExclusive <= min ? min : min + (this.nextUInt() % (maxExclusive - min));
  }
  chance(p: number): boolean { return this.value() < p; }
  spread(): number { return (this.value() + this.value() + this.value()) / 1.5 - 1; }
}
