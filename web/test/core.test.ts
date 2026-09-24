// Node port of the key NUnit tests (tests/Core.Tests) run against the TypeScript core.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { Vec2, Int2, Rng } from '../src/core/math';
import { MapData, TileKind, tileIs, TF } from '../src/core/map';
import { World, Pathfinder, MoverKind, PathNode } from '../src/core/world';
import { GameSession, TICK } from '../src/core/session';
import { MISSIONS, getMission, Difficulty, LoadoutConfig, PlayerInput, AIState, MissionState, EvType, ObjectiveType, MissionDef } from '../src/core/entities';
import { AmmoType, WEAPONS, WeaponState, FireBlock } from '../src/core/weapons';
import { ProgressData, CATALOG, buy, BuyResult } from '../src/core/meta';

const mapText = (id: string) => readFileSync(`${process.env.MAPS_DIR ?? '../Assets/Resources/Maps'}/${id}.txt`, 'utf8');
const load = (id: string) => MapData.parse(mapText(id));

class Bot {
  private path: PathNode[] | null = null; private idx = 0; private goal = Vec2.Zero; private repath = 0;
  constructor(private s: GameSession) {}
  moveTowards(goal: Vec2, inp: PlayerInput, dt: number, tol = 0.4): boolean {
    const p = this.s.player;
    if (Vec2.distance(p.pos, goal) < tol) { inp.move = Vec2.Zero; return true; }
    this.repath -= dt;
    if (!this.path || this.repath <= 0 || Vec2.distance(goal, this.goal) > 1) {
      this.path = this.s.paths.findPath(p.pos, goal, 0.34); this.idx = 0; this.goal = goal; this.repath = 0.5;
    }
    if (!this.path) { inp.move = goal.sub(p.pos).normalized; return false; }
    while (this.idx < this.path.length && !this.path[this.idx].teleport && Vec2.distance(this.path[this.idx].pos, p.pos) < 0.3) this.idx++;
    if (this.idx >= this.path.length) { inp.move = goal.sub(p.pos).normalized; return false; }
    const node = this.path[this.idx];
    if (node.teleport) { inp.interactPressed = true; this.path = null; return false; }
    inp.move = node.pos.sub(p.pos).normalized;
    inp.aim = node.pos.add(inp.move);
    return false;
  }
}

test('maps parse and player can reach all objectives', () => {
  for (const id of ['mansion', 'facility', 'office']) {
    const map = load(id);
    assert.equal(map.id, id);
    const world = new World(map);
    const links = map.entities.filter(e => e.kind === 'stairs').map(s => [Int2.fromWorld(map.parsePoint(s.str('a')!)), Int2.fromWorld(map.parsePoint(s.str('b')!))]);
    const start = Int2.fromWorld(map.parsePoint(map.entities.find(e => e.kind === 'spawn')!.str('at')!));
    const reached = new Set<number>([start.key]);
    const q = [start];
    const pass = (x: number, y: number) => {
      const t = world.tileAt(x, y);
      return t.kind === TileKind.Floor || t.kind === TileKind.Stairs || t.kind === TileKind.Door || t.kind === TileKind.Vent || (t.kind === TileKind.Furniture && !tileIs(t, TF.Solid));
    };
    while (q.length) {
      const c = q.shift()!;
      const visit = (n: Int2) => { if (!reached.has(n.key)) { reached.add(n.key); q.push(n); } };
      for (const d of [new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1)]) {
        const n = c.add(d);
        if (pass(n.x, n.y)) visit(n);
        else if (world.tileAt(n.x, n.y).kind === TileKind.Window && pass(n.x + d.x, n.y + d.y)) visit(n.add(d));
      }
      for (const [a, b] of links) { if (c.eq(a)) visit(b); if (c.eq(b)) visit(a); }
    }
    for (const mid of map.missionEntities.keys())
      for (const e of map.entitiesFor(mid)) {
        if (!['target', 'pickup', 'terminal'].includes(e.kind)) continue;
        const p = Int2.fromWorld(map.parsePoint(e.str('at')!));
        let ok = reached.has(p.key);
        for (let dy = -1; dy <= 1 && !ok; dy++) for (let dx = -1; dx <= 1 && !ok; dx++) ok = reached.has(new Int2(p.x + dx, p.y + dy).key);
        assert.ok(ok, `${id}/${mid}: ${e.kind} at ${e.str('at')} unreachable`);
      }
  }
});

const testMission: MissionDef = { ...MISSIONS[0], id: 't', mapId: 'test', objectives: [{ id: 'x', type: ObjectiveType.Extract, text: 'Leave' }], challenges: [] };
function room(ambient: number, entities: string): GameSession {
  const text = `@meta\nid: test\nname: Test\nambient: ${ambient}\n@tiles\n################\n#..............#\n#..............#\n#..............#\n#..............#\n#.C............#\n#..............#\n################\n@entities\n@mission t\n${entities}\n`;
  const lo = new LoadoutConfig();
  lo.slotWeapons = ['knife', 'pistol', null, 'throwing_knives', 'coin'];
  lo.ammo.set(AmmoType.Pistol, 36); lo.ammo.set(AmmoType.Coins, 3); lo.ammo.set(AmmoType.Knives, 3);
  return new GameSession(MapData.parse(text), testMission, Difficulty.Normal, lo);
}
function run(s: GameSession, seconds: number, input?: (i: PlayerInput) => void, each?: (s: GameSession) => void) {
  const inp = new PlayerInput();
  inp.aim = s.player.pos.add(new Vec2(1, 0));
  for (let t = 0; t < seconds; t += TICK) {
    inp.clearEdges(); inp.move = Vec2.Zero; inp.fireHeld = false;
    input?.(inp);
    s.update(TICK, inp);
    each?.(s);
    if (s.state !== MissionState.Playing) break;
  }
}

test('guard spots player in a lit room and shoots, but not instantly', () => {
  const s0 = room(0.9, 'spawn at=2,3\nguard id=g at=8,3 facing=180');
  run(s0, 0.25);
  assert.equal(s0.spotted, false);
  const s = room(0.9, 'spawn at=2,3\nguard id=g at=8,3 facing=180');
  let shot = false;
  run(s, 4, undefined, ss => { shot ||= ss.events.some(e => e.type === EvType.Shot && e.actorId !== 0); });
  assert.ok(s.spotted); assert.equal(s.findNpc('g')!.state, AIState.Attack); assert.ok(shot);
});

test('darkness + crouching hides; sprinting is heard; silent takedown works', () => {
  const s = room(0.05, 'spawn at=3,3\nguard id=g at=12,3 facing=180');
  s.player.crouched = true;
  run(s, 4);
  assert.equal(s.spotted, false);
  const s2 = room(0.9, 'spawn at=3,3\nguard id=g at=10,3 facing=0');
  run(s2, 1.2, i => { i.move = new Vec2(1, 0); i.sprintHeld = true; i.aim = new Vec2(20, 4.5); });
  assert.ok([AIState.Suspicious, AIState.Investigate, AIState.Attack].includes(s2.findNpc('g')!.state));
  const s3 = room(0.9, 'spawn at=9,3\nguard id=g at=10,3 facing=0');
  s3.player.inventory.select(0);
  run(s3, 0.3, i => { i.aim = s3.findNpc('g')!.pos; i.firePressed = true; i.fireHeld = true; });
  assert.equal(s3.findNpc('g')!.alive, false); assert.equal(s3.spotted, false); assert.equal(s3.kills, 1);
});

test('coins distract, bodies raise alarm, closets hide', () => {
  const s = room(0.2, 'spawn at=2,6\nguard id=g at=12,2 facing=90');
  s.player.inventory.select(4);
  let thrown = false;
  run(s, 3, i => { i.aim = new Vec2(8.5, 5.5); if (!thrown) { i.firePressed = i.fireHeld = true; thrown = true; } });
  assert.ok([AIState.Investigate, AIState.Search, AIState.Suspicious].includes(s.findNpc('g')!.state));
  const b = room(0.9, 'spawn at=2,6\nguard id=a at=5,3 facing=270\nguard id=b at=13,1 route="13,1:1|6,3:3"');
  b.killNpc(b.findNpc('a')!, new Vec2(1, 0), true);
  b.player.hidden = true;
  let found = false;
  run(b, 15, undefined, ss => { found ||= ss.events.some(e => e.type === EvType.BodyFound); });
  assert.ok(found);
  const c = room(0.9, 'spawn at=3,5\nguard id=g at=12,5 facing=180');
  run(c, TICK, i => { i.interactPressed = true; });
  assert.ok(c.player.hidden, c.candidate.prompt);
  run(c, 3);
  assert.equal(c.spotted, false);
});

test('weapons: fire rate, reload, shells', () => {
  const w = new WeaponState(WEAPONS.get('smg')!);
  const rng = new Rng(1);
  let shots = 0;
  for (let t = 0; t < 1; t += 1 / 120) { w.tick(1 / 120); if (w.canFire(false) === FireBlock.None) { w.fire(rng, 0, false, false); shots++; } }
  assert.ok(Math.abs(shots - 12) <= 1);
  const sg = new WeaponState(WEAPONS.get('shotgun')!, 3);
  assert.ok(sg.startReload(10));
  let total = 0;
  for (let i = 0; i < 20 && sg.reloading; i++) total += sg.tickReload(0.5, 10 - total);
  assert.equal(sg.mag, 6); assert.equal(total, 3);
});

test('shop and progress', () => {
  const p = ProgressData.fromJson(null);
  p.money = 100;
  assert.equal(buy(p, CATALOG.find(i => i.id === 'w.silenced_pistol')!), BuyResult.NotEnoughMoney);
  p.money = 5000;
  assert.equal(buy(p, CATALOG.find(i => i.id === 'w.silenced_pistol')!), BuyResult.Ok);
  assert.equal(p.loadout[1], 'silenced_pistol');
  assert.equal(buy(p, CATALOG.find(i => i.id === 'w.smg')!), BuyResult.Locked);
  const p2 = ProgressData.fromJson(JSON.stringify(p));
  assert.ok(p2.owns('silenced_pistol'));
  assert.equal(ProgressData.fromJson('{broken').money, 1500);
});

for (const mid of ['mansion_host', 'facility_voss', 'office_cfo', 'mansion_ledger', 'facility_prototype', 'office_breach']) {
  test(`bot completes ${mid}`, () => {
    const mission = getMission(mid)!;
    const s = new GameSession(load(mission.mapId), mission, Difficulty.Normal, ProgressData.fromJson(null).buildLoadout());
    s.player.godMode = true; s.player.ghost = true;
    const bot = new Bot(s);
    s.player.inventory.select(0);
    s.player.inventory.keys.add('blue'); s.player.inventory.keys.add('red');
    for (const d of s.world.doors) if (d.type !== 3) d.locked = false;
    const targets = mission.objectives.filter(o => o.type === ObjectiveType.Eliminate).flatMap(o => o.targetIds!).map(id => s.findNpc(id)!);
    const inp = new PlayerInput();
    let t = 0;
    while (t < 900 && s.state === MissionState.Playing) {
      inp.clearEdges(); inp.fireHeld = false;
      const alive = targets.find(x => !x.down);
      const dl = s.interactables.find(it => it.kind === 2 && !it.used && mission.objectives.some(o => o.itemId === it.key));
      const intel = s.pickups.find(p => !p.taken && p.kind === 5 && mission.objectives.some(o => !o.optional && o.itemId === p.itemId));
      if (dl) {
        if (bot.moveTowards(dl.pos, inp, TICK, 0.9)) { inp.move = Vec2.Zero; inp.interactHeld = true; inp.aim = dl.pos; }
      } else if (alive) {
        if (Vec2.distance(s.player.pos, alive.pos) < 0.95) { inp.move = Vec2.Zero; inp.aim = alive.pos; inp.fireHeld = inp.firePressed = true; }
        else bot.moveTowards(alive.pos, inp, TICK, 0.8);
      } else bot.moveTowards(intel ? intel.pos : s.extracts[0].rect.center, inp, TICK, 0.2);
      s.update(TICK, inp);
      t += TICK;
    }
    assert.equal(s.state, MissionState.Complete, `${mid}: state ${MissionState[s.state]} at ${s.player.pos} after ${t.toFixed(0)}s, objective ${s.currentObjective?.text}`);
    assert.ok(s.buildResult().total > 0);
  });
}

test('chaos simulation is stable on every mission', () => {
  for (const mission of MISSIONS) {
    for (let seed = 1; seed <= 4; seed++) {
      const p = ProgressData.fromJson(null);
      p.ownedWeapons.push('smg', 'shotgun', 'throwing_knives', 'combat_knife');
      p.loadout = ['combat_knife', 'pistol', 'shotgun', 'throwing_knives', 'coin'];
      p.setAmmo(AmmoType.Shells, 30); p.setAmmo(AmmoType.Knives, 4);
      const s = new GameSession(load(mission.mapId), mission, Difficulty.Hard, p.buildLoadout(), seed);
      s.player.godMode = true; s.debugNoFail = true;
      const rng = new Rng(seed * 7);
      const inp = new PlayerInput();
      const bot = new Bot(s);
      let wander = s.player.pos;
      for (let t = 0; t < 90 && s.state === MissionState.Playing; t += TICK) {
        inp.clearEdges();
        if (rng.chance(0.01)) wander = s.npcs[rng.rangeInt(0, s.npcs.length)].pos;
        bot.moveTowards(wander, inp, TICK);
        inp.aim = s.player.pos.add(Vec2.fromAngle(rng.range(0, 6.28)).mul(3));
        inp.fireHeld = rng.chance(0.3); inp.firePressed = inp.fireHeld && rng.chance(0.3);
        inp.sprintHeld = rng.chance(0.5); inp.crouchPressed = rng.chance(0.005);
        inp.interactPressed = rng.chance(0.02); inp.interactHeld = rng.chance(0.5);
        inp.reloadPressed = rng.chance(0.01); inp.altPressed = rng.chance(0.01);
        if (rng.chance(0.01)) inp.selectSlot = rng.rangeInt(0, 5);
        s.update(TICK, inp);
        for (const n of s.npcs)
          if (!n.down && !n.isCamera) assert.ok(!s.world.circleOverlapsSolid(n.pos, n.radius * 0.7, MoverKind.Npc), `${mission.id}: ${n.key} in geometry ${n.pos}`);
      }
    }
  }
});
