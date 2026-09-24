// Port of Tiles.cs + MapData.cs
import { Vec2, Int2, Rect, DEG2RAD } from './math';

export enum TileKind { Void, Floor, Wall, Window, Door, Furniture, Water, Vent, Stairs }
export enum FloorStyle { None, A, B, C, D, Grass, Path }
export enum FurnitureType {
  None, Table, Desk, Bed, Sofa, Bookshelf, Crate, ServerRack, Counter, Toilet, Sink,
  Piano, Machine, Cabinet, Plant, Closet, Tree, Bush, Chair, Barrel, Bathtub, Pillar,
  Car, Vending, Console, Partition,
}
export enum DoorType { Normal, LockedBlue, LockedRed, Secret }
export const TF = { None: 0, Solid: 1, BlocksSight: 2, LowCover: 4, BlocksBullets: 8, Concealment: 16, HidingSpot: 32 };

export interface Tile { kind: TileKind; floor: FloorStyle; furniture: FurnitureType; flags: number; symbol: string; }
export const tileIs = (t: Tile, f: number) => (t.flags & f) !== 0;

const FURN: Record<string, FurnitureType> = {
  t: FurnitureType.Table, k: FurnitureType.Desk, b: FurnitureType.Bed, s: FurnitureType.Sofa, h: FurnitureType.Bookshelf,
  x: FurnitureType.Crate, r: FurnitureType.ServerRack, u: FurnitureType.Counter, o: FurnitureType.Toilet, n: FurnitureType.Sink,
  P: FurnitureType.Piano, m: FurnitureType.Machine, f: FurnitureType.Cabinet, p: FurnitureType.Plant, C: FurnitureType.Closet,
  T: FurnitureType.Tree, B: FurnitureType.Bush, c: FurnitureType.Chair, e: FurnitureType.Barrel, y: FurnitureType.Bathtub,
  i: FurnitureType.Pillar, g: FurnitureType.Car, j: FurnitureType.Vending, q: FurnitureType.Console, w: FurnitureType.Partition,
};

export function furnitureFlags(f: FurnitureType): number {
  const tall = TF.Solid | TF.BlocksSight | TF.BlocksBullets;
  const low = TF.Solid | TF.LowCover;
  switch (f) {
    case FurnitureType.Bookshelf: case FurnitureType.Crate: case FurnitureType.ServerRack: case FurnitureType.Machine:
    case FurnitureType.Cabinet: case FurnitureType.Tree: case FurnitureType.Pillar: case FurnitureType.Vending:
      return tall;
    case FurnitureType.Closet: return tall | TF.HidingSpot;
    case FurnitureType.Bush: return TF.Concealment | TF.LowCover;
    case FurnitureType.Chair: return TF.None;
    case FurnitureType.Plant: return TF.Solid;
    case FurnitureType.Barrel: return TF.Solid | TF.LowCover | TF.BlocksBullets;
    case FurnitureType.Car: return low | TF.BlocksBullets;
    default: return low;
  }
}

export function tileFromChar(c: string): Tile {
  const t: Tile = { kind: TileKind.Floor, floor: FloorStyle.A, furniture: FurnitureType.None, flags: 0, symbol: c };
  switch (c) {
    case ' ': t.kind = TileKind.Void; t.floor = FloorStyle.None; t.flags = TF.Solid | TF.BlocksSight | TF.BlocksBullets; break;
    case '#': t.kind = TileKind.Wall; t.floor = FloorStyle.None; t.flags = TF.Solid | TF.BlocksSight | TF.BlocksBullets; break;
    case 'W': t.kind = TileKind.Window; t.flags = TF.Solid | TF.BlocksBullets; break;
    case 'D': case 'L': case 'M': t.kind = TileKind.Door; break;
    case 'S': t.kind = TileKind.Door; t.floor = FloorStyle.B; break;
    case '.': t.floor = FloorStyle.A; break;
    case ':': t.floor = FloorStyle.B; break;
    case ';': t.floor = FloorStyle.C; break;
    case '_': t.floor = FloorStyle.D; break;
    case ',': t.floor = FloorStyle.Grass; break;
    case '"': t.floor = FloorStyle.Path; break;
    case '~': t.kind = TileKind.Water; t.floor = FloorStyle.None; t.flags = TF.Solid; break;
    case '=': t.kind = TileKind.Stairs; break;
    case 'v': t.kind = TileKind.Vent; t.floor = FloorStyle.None; t.flags = TF.BlocksSight | TF.BlocksBullets; break;
    default: {
      const f = FURN[c];
      if (f === undefined) throw new Error(`Unknown map tile character '${c}'`);
      t.kind = TileKind.Furniture; t.furniture = f; t.flags = furnitureFlags(f);
    }
  }
  return t;
}

export function doorTypeFromChar(c: string): DoorType {
  return c === 'L' ? DoorType.LockedBlue : c === 'M' ? DoorType.LockedRed : c === 'S' ? DoorType.Secret : DoorType.Normal;
}

export class EntityDef {
  kind = '';
  props: Record<string, string> = {};
  line = 0;
  has(k: string) { return k in this.props; }
  str(k: string, fb: string | null = null): string | null { return k in this.props ? this.props[k] : fb; }
  float(k: string, fb = 0): number { const v = parseFloat(this.props[k]); return k in this.props && !isNaN(v) ? v : fb; }
  int(k: string, fb = 0): number { const v = parseInt(this.props[k], 10); return k in this.props && !isNaN(v) ? v : fb; }
  bool(k: string, fb = false): boolean { if (!(k in this.props)) return fb; const v = this.props[k]; return v === '1' || v.toLowerCase() === 'true' || v === 'yes'; }
}

export interface Waypoint { pos: Vec2; wait: number; lookAngle: number; activity: string | null; }

export const MARKER_CHARS = "AEFGHIJKNOQRUVXYZadlz0123456789!$%&*+?^<>'/\\{}[]()";

export class MapData {
  id = ''; name = ''; theme = ''; description = ''; ambient = 0.3;
  width = 0; height = 0;
  tiles: Tile[] = [];
  markers = new Map<string, Int2>();
  entities: EntityDef[] = [];
  missionEntities = new Map<string, EntityDef[]>();

  inBounds(x: number, y: number) { return x >= 0 && y >= 0 && x < this.width && y < this.height; }
  get(x: number, y: number): Tile { return this.inBounds(x, y) ? this.tiles[y * this.width + x] : VOID_TILE; }
  set(x: number, y: number, t: Tile) { if (this.inBounds(x, y)) this.tiles[y * this.width + x] = t; }

  entitiesFor(missionId: string | null): EntityDef[] {
    const out = this.entities.slice();
    if (missionId && this.missionEntities.has(missionId)) out.push(...this.missionEntities.get(missionId)!);
    return out;
  }

  markerPos(c: string): Vec2 {
    const m = this.markers.get(c);
    if (!m) throw new Error(`Map '${this.id}' has no marker '${c}'`);
    return m.center;
  }

  parsePoint(s: string): Vec2 {
    s = s.trim();
    if (s.length === 1) return this.markerPos(s);
    const comma = s.indexOf(',');
    if (comma < 0) throw new Error(`Bad point '${s}' in map '${this.id}'`);
    const col = parseFloat(s.substring(0, comma)), row = parseFloat(s.substring(comma + 1));
    return new Vec2(col + 0.5, this.height - 1 - row + 0.5);
  }

  parseRoute(route: string | null): Waypoint[] {
    const result: Waypoint[] = [];
    if (!route || !route.trim()) return result;
    for (const part of route.split('|')) {
      const bits = part.trim().split(':');
      const wp: Waypoint = { pos: this.parsePoint(bits[0]), wait: 0, lookAngle: NaN, activity: null };
      if (bits.length > 1 && bits[1].length > 0) wp.wait = parseFloat(bits[1]);
      if (bits.length > 2 && bits[2].length > 0) wp.lookAngle = parseFloat(bits[2]) * DEG2RAD;
      if (bits.length > 3) wp.activity = bits[3].replace(/_/g, ' ');
      result.push(wp);
    }
    return result;
  }

  parseRect(s: string): Rect {
    const b = s.split(',').map(parseFloat);
    if (b.length !== 4) throw new Error(`Bad rect '${s}'`);
    return new Rect(b[0], this.height - b[1] - b[3], b[2], b[3]);
  }

  static parse(text: string): MapData {
    const map = new MapData();
    const tileRows: string[] = [];
    let section: string | null = null;
    let current: EntityDef[] | null = null;
    const lines = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
    for (let i = 0; i < lines.length; i++) {
      const raw = lines[i];
      const trimmed = raw.trim();
      if (trimmed.startsWith('@')) {
        const head = trimmed.substring(1).split(/ (.*)/s).filter(s => s.length > 0);
        section = head[0];
        if (section === 'mission') {
          current = [];
          map.missionEntities.set(head[1].trim(), current);
        } else if (section === 'entities') current = map.entities;
        continue;
      }
      if (section === 'tiles') { if (raw.length > 0 || tileRows.length > 0) tileRows.push(raw); continue; }
      if (trimmed.length === 0 || trimmed.startsWith('//')) continue;
      if (section === 'meta') {
        const colon = trimmed.indexOf(':');
        if (colon < 0) continue;
        const key = trimmed.substring(0, colon).trim(), val = trimmed.substring(colon + 1).trim();
        if (key === 'id') map.id = val;
        else if (key === 'name') map.name = val;
        else if (key === 'theme') map.theme = val;
        else if (key === 'description') map.description = val;
        else if (key === 'ambient') map.ambient = parseFloat(val);
      } else if (section === 'entities' || section === 'mission') {
        const e = parseEntityLine(trimmed);
        e.line = i + 1;
        current!.push(e);
      }
    }
    while (tileRows.length > 0 && tileRows[tileRows.length - 1].length === 0) tileRows.pop();
    if (tileRows.length === 0) throw new Error('Map has no @tiles section');
    map.height = tileRows.length;
    map.width = Math.max(...tileRows.map(r => r.length));
    map.tiles = new Array(map.width * map.height);
    for (let row = 0; row < map.height; row++) {
      const r = tileRows[row];
      const y = map.height - 1 - row;
      for (let x = 0; x < map.width; x++) {
        let c = x < r.length ? r[x] : ' ';
        if (MARKER_CHARS.indexOf(c) >= 0) {
          map.markers.set(c, new Int2(x, y));
          c = inferFloor(tileRows, row, x);
        }
        map.tiles[y * map.width + x] = tileFromChar(c);
      }
    }
    return map;
  }
}

const VOID_TILE = tileFromChar(' ');

function inferFloor(rows: string[], row: number, col: number): string {
  const counts = new Map<string, number>();
  for (let dy = -1; dy <= 1; dy++)
    for (let dx = -1; dx <= 1; dx++) {
      const rr = row + dy, cc = col + dx;
      if ((dx === 0 && dy === 0) || rr < 0 || rr >= rows.length || cc < 0 || cc >= rows[rr].length) continue;
      const n = rows[rr][cc];
      if ('.:;_,"'.indexOf(n) < 0) continue;
      counts.set(n, (counts.get(n) ?? 0) + 1);
    }
  let best = '.', bestN = 0;
  for (const [k, v] of counts) if (v > bestN || (v === bestN && k < best)) { best = k; bestN = v; }
  return best;
}

export function parseEntityLine(line: string): EntityDef {
  const e = new EntityDef();
  let i = 0;
  const skip = () => { while (i < line.length && /\s/.test(line[i])) i++; };
  const token = () => { skip(); let s = ''; while (i < line.length && !/\s/.test(line[i])) s += line[i++]; return s; };
  e.kind = token();
  for (;;) {
    skip();
    if (i >= line.length) break;
    const eq = line.indexOf('=', i);
    if (eq < 0) throw new Error(`Bad entity property near '${line.substring(i)}'`);
    const key = line.substring(i, eq).trim();
    i = eq + 1;
    let val: string;
    if (i < line.length && line[i] === '"') {
      const end = line.indexOf('"', i + 1);
      val = line.substring(i + 1, end);
      i = end + 1;
    } else val = token();
    e.props[key] = val;
  }
  return e;
}
