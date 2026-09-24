// Port of GameSession.cs and its partials (Player, Combat, Interaction, Perception, AI).
import { Vec2, Int2, Rect, Rng, clamp, clamp01, moveTowards, rotateTowards, angleDelta, PI, DEG2RAD } from './math';
import { MapData, EntityDef, TileKind, FurnitureType, FloorStyle, DoorType, TF, tileIs, tileFromChar, Tile } from './map';
import { World, Pathfinder, LightDef, bakeLights, MoverKind, RayMode, DoorState, RayHit } from './world';
import { WeaponState, WeaponDef, WEAPONS, applyUpgrades, AmmoType, WeaponSlot, WeaponClass, FireBlock } from './weapons';
import {
  Actor, Player, Npc, NpcKind, AIState, Body, Pickup, PickupKind, Projectile, ProjectileKind, Interactable, InteractKind, Zone, ExtractZone,
  MissionDef, ObjectiveDef, ObjectiveType, ChallengeType, Difficulty, DifficultySettings, difficultyFor, LoadoutConfig, PlayerInput,
  AlertLevel, MissionState, NoiseKind, EvType, GameEvent, MissionResult,
} from './entities';

export enum CandidateKind { None, LeaveHiding, StashBody, DropBody, Hide, DragBody, Interactable, WeaponPickup, Door, Window }
export interface InteractCandidate {
  kind: CandidateKind; prompt: string; pos: Vec2; holdTime: number; enabled: boolean;
  interactable?: Interactable; body?: Body; door?: DoorState; pickup?: Pickup; cell?: Int2; dir?: Int2;
}
const NONE_CANDIDATE: InteractCandidate = { kind: CandidateKind.None, prompt: '', pos: Vec2.Zero, holdTime: 0, enabled: false };

export const TICK = 1 / 60;
const PERCEPTION = 0.1;
const WALK = 3.7, SPRINT = 6.1, CROUCH = 1.9;

export function ammoName(t: AmmoType): string {
  switch (t) {
    case AmmoType.Pistol: return '9mm'; case AmmoType.Rifle: return '5.56mm'; case AmmoType.Shells: return 'shells';
    case AmmoType.Knives: return 'knives'; case AmmoType.Coins: return 'coins'; default: return '';
  }
}

export function surfaceName(t: Tile): string {
  if (t.kind === TileKind.Vent) return 'metal';
  switch (t.floor) {
    case FloorStyle.Grass: return 'grass'; case FloorStyle.C: return 'carpet'; case FloorStyle.B: return 'wood';
    case FloorStyle.D: return 'tile'; case FloorStyle.Path: return 'gravel'; default: return 'hard';
  }
}

const isAlertState = (s: AIState) => s === AIState.Attack || s === AIState.Chase || s === AIState.Search || s === AIState.Investigate;
const isCalmState = (s: AIState) => s === AIState.Idle || s === AIState.Patrol || s === AIState.Suspicious || s === AIState.ReturnToPatrol || s === AIState.Follow;

const BARKS: Record<string, string[]> = {
  suspicious: ['Hm?', 'What was that?', 'Hello?', 'Did something move?'],
  investigate: ["I'll check it out.", 'Someone there?', 'Show yourself!'],
  spotted: ['Intruder!', 'Contact!', 'There! Hostile!', 'Freeze!'],
  radio: ['Control, we have an intruder!', 'All units, hostile on site!'],
  lost: ['Lost visual!', "Where'd he go?", "He's slipping away!"],
  search: ['Search the area.', "He's here somewhere...", 'Check every corner.'],
  calm: ["Must've been nothing.", 'Probably rats.', 'Back to it.', 'Hm. Nothing.'],
  body: ["Man down! We've got a body!", 'Oh god... Raise the alarm!'],
  body_seen: ['What the...?', 'Is that... someone on the floor?'],
  gunshot: ['Shots fired!', 'Gunfire!', 'Was that a gunshot?'],
  hurt: ['Argh!', "I'm hit!", 'Taking fire!'],
  lights: ['Who killed the lights?', "Power's out... I'll check the box."],
  noise: ['Who left that on?', "What's that racket?"],
  found: ['Gotcha!', 'Found you!'],
  panic: ['Help! Security!', "He's got a weapon!", 'Somebody help!'],
  flee: ['Get me out of here!', "I'm leaving, now!"],
  report: ["Over there! Someone's in the building!", 'Security! I saw something!'],
  camera: ['Camera picked something up!', 'Movement on the cameras!'],
};

export class GameSession {
  readonly world: World; readonly paths: Pathfinder; readonly diff: DifficultySettings; readonly rng: Rng;
  readonly player = new Player();
  readonly npcs: Npc[] = []; readonly bodies: Body[] = []; readonly pickups: Pickup[] = []; readonly projectiles: Projectile[] = [];
  readonly interactables: Interactable[] = []; readonly lights: LightDef[] = []; readonly zones: Zone[] = []; readonly extracts: ExtractZone[] = [];
  readonly events: GameEvent[] = [];
  time = 0; state = MissionState.Playing; failReason: string | null = null; alert = AlertLevel.Calm; alertTimer = 0; spotted = false;
  lightsDirty = true; debugNoFail = false;
  kills = 0; targetsKilled = 0; civiliansKilled = 0; nonTargetKills = 0; subdued = 0; bodiesFound = 0; shotsFired = 0;
  candidate: InteractCandidate = NONE_CANDIDATE;
  private objectiveDone: boolean[];
  private nextId = 1;
  private extractMsgCooldown = 0;
  private msgThrottle = 0;
  private result: MissionResult | null = null;
  private closetBodies = new Map<number, number>();
  private holdProgress = 0;
  private holdTarget: InteractCandidate = NONE_CANDIDATE;

  constructor(readonly map: MapData, readonly mission: MissionDef, difficulty: Difficulty, loadout: LoadoutConfig | null, seed = 12345) {
    this.world = new World(map);
    this.paths = new Pathfinder(this.world);
    this.diff = difficultyFor(difficulty);
    this.rng = new Rng(seed);
    this.objectiveDone = mission.objectives.map(() => false);
    this.build(loadout ?? new LoadoutConfig());
    bakeLights(this.world, this.lights, 1, null);
  }

  newId() { return this.nextId++; }
  get holdProgressValue() { return this.holdProgress; }

  // ================================================================== construction
  private build(lo: LoadoutConfig) {
    const map = this.map;
    const targets = new Map<string, Npc>();
    for (const e of map.entitiesFor(this.mission.id)) {
      switch (e.kind) {
        case 'spawn': this.player.pos = map.parsePoint(e.str('at')!); this.player.facing = PI / 2; break;
        case 'extract': this.extracts.push({ label: e.str('label', 'Extraction')!, rect: map.parseRect(e.str('rect')!) }); break;
        case 'zone': this.zones.push({ name: e.str('name')!, rect: map.parseRect(e.str('rect')!) }); break;
        case 'light': this.lights.push(LightDef.fromEntity(e, map)); break;
        case 'stairs': {
          const a = map.parsePoint(e.str('a')!), b = map.parsePoint(e.str('b')!);
          this.paths.addLink(Int2.fromWorld(a), Int2.fromWorld(b));
          const label = e.str('label', 'Stairs')!, lk = e.str('lock');
          for (const [p, q] of [[a, b], [b, a]]) {
            const it = new Interactable();
            Object.assign(it, { id: this.newId(), kind: InteractKind.Stairs, pos: p, pos2: q, label, lock: lk, secret: e.bool('secret') });
            this.interactables.push(it);
          }
          break;
        }
        case 'powerbox': {
          const it = new Interactable();
          Object.assign(it, { id: this.newId(), kind: InteractKind.PowerBox, pos: map.parsePoint(e.str('at')!), group: e.str('group'), label: e.str('label', 'Fuse box') });
          this.interactables.push(it);
          break;
        }
        case 'terminal': {
          const kind = e.str('action') === 'cameras' ? InteractKind.CameraTerminal : InteractKind.DownloadTerminal;
          const it = new Interactable();
          Object.assign(it, {
            id: this.newId(), kind, key: e.str('id'), pos: map.parsePoint(e.str('at')!), group: e.str('group'),
            label: e.str('label', 'Terminal'), duration: e.float('time', kind === InteractKind.CameraTerminal ? 2 : 5),
          });
          this.interactables.push(it);
          break;
        }
        case 'distraction': {
          const kind = e.str('kind', 'radio')!;
          const it = new Interactable();
          Object.assign(it, { id: this.newId(), kind: InteractKind.Distraction, pos: map.parsePoint(e.str('at')!), sound: kind, label: distractionLabel(kind) });
          this.interactables.push(it);
          break;
        }
        case 'pickup': this.pickups.push(this.makePickup(e)); break;
        case 'camera': {
          const cam = new Npc();
          Object.assign(cam, {
            id: this.newId(), key: 'cam' + this.npcs.length, kind: NpcKind.Camera, type: 'camera', pos: map.parsePoint(e.str('at')!),
            group: e.str('group'), viewRange: 8, viewAngle: 60 * DEG2RAD, health: 30, maxHealth: 30, radius: 0.25, state: AIState.Idle,
          });
          cam.sweepCenter = e.float('facing') * DEG2RAD;
          cam.sweepHalf = e.float('sweep', 45) * DEG2RAD;
          cam.facing = cam.sweepCenter;
          cam.perceptionTimer = this.rng.value() * 0.1;
          this.npcs.push(cam);
          break;
        }
        case 'guard': case 'civilian': case 'target': {
          const n = this.makeNpc(e);
          this.npcs.push(n);
          if (n.isTarget) targets.set(n.key, n);
          break;
        }
      }
    }
    for (const n of this.npcs) {
      if (n.followKey && targets.has(n.followKey)) { n.followTarget = targets.get(n.followKey)!; n.state = AIState.Follow; }
      else n.followKey = null;
    }
    this.setupPlayer(lo);
  }

  private makePickup(e: EntityDef): Pickup {
    const item = e.str('item')!;
    const p = new Pickup();
    p.id = this.newId(); p.pos = this.map.parsePoint(e.str('at')!); p.amount = e.int('amount', 1); p.itemId = item;
    switch (item) {
      case 'medkit': p.kind = PickupKind.Medkit; p.name = 'Medkit'; break;
      case 'armor': p.kind = PickupKind.Armor; p.name = 'Armor plate'; p.amount = e.int('amount', 35); break;
      case 'cash': p.kind = PickupKind.Cash; p.name = 'Cash'; break;
      case 'ammo': p.kind = PickupKind.Ammo; p.name = 'Ammo'; p.ammoType = AmmoType.Pistol; p.amount = e.int('amount', 24); break;
      case 'throwing_knives': p.kind = PickupKind.Knives; p.name = 'Throwing knives'; p.ammoType = AmmoType.Knives; break;
      case 'keycard_blue': p.kind = PickupKind.Keycard; p.itemId = 'blue'; p.name = 'Blue Keycard'; break;
      case 'keycard_red': p.kind = PickupKind.Keycard; p.itemId = 'red'; p.name = 'Red Keycard'; break;
      case 'intel': p.kind = PickupKind.Intel; p.itemId = e.str('id')!; p.name = e.str('name', 'Intel')!; break;
      default: {
        if (!item.startsWith('weapon_')) throw new Error(`Unknown pickup '${item}'`);
        let wid = item.substring(7);
        if (wid === 'rifle') wid = 'assault_rifle';
        const def = WEAPONS.get(wid)!;
        p.kind = PickupKind.Weapon; p.itemId = def.id; p.name = def.name; p.amount = def.magSize * 2; p.ammoType = def.ammo;
      }
    }
    return p;
  }

  private makeNpc(e: EntityDef): Npc {
    const n = new Npc();
    n.id = this.newId(); n.key = e.str('id')!; n.pos = this.map.parsePoint(e.str('at')!);
    n.facing = e.float('facing', 270) * DEG2RAD; n.route = this.map.parseRoute(e.str('route'));
    n.carriedKey = e.str('key'); n.followKey = e.str('follow'); n.perceptionTimer = this.rng.value() * 0.1;
    const type = e.str('type', e.kind === 'civilian' ? 'staff' : e.kind)!;
    n.type = type;
    if (e.kind === 'civilian') { n.kind = NpcKind.Civilian; n.viewRange = 8; }
    else if (e.kind === 'target') { n.kind = NpcKind.Target; n.type = 'target'; n.displayName = e.str('name', 'Target')!; }
    else n.kind = type === 'elite' ? NpcKind.Elite : NpcKind.Guard;
    let weapon = e.str('weapon', n.kind === NpcKind.Guard || n.kind === NpcKind.Elite ? 'pistol' : 'none')!;
    if (weapon !== 'none') {
      if (weapon === 'rifle') weapon = 'assault_rifle';
      n.weapon = new WeaponState(WEAPONS.get(weapon) ?? WEAPONS.get('pistol')!);
    }
    n.maxHealth = n.health = e.float('hp', n.kind === NpcKind.Elite ? 150 : 100);
    if (n.kind === NpcKind.Elite) n.viewRange = 10;
    n.homePos = n.pos; n.homeFacing = n.facing;
    if (e.has('look')) n.lookAngles = e.str('look')!.split(',').map(s => parseFloat(s) * DEG2RAD);
    if (e.has('escape')) n.escapePos = this.map.parsePoint(e.str('escape')!);
    n.state = n.route.length > 0 ? AIState.Patrol : AIState.Idle;
    n.lookTimer = 2 + this.rng.range(0, 2);
    if (n.route.length > 0) n.activity = n.route[0].activity;
    return n;
  }

  private setupPlayer(lo: LoadoutConfig) {
    const p = this.player, inv = p.inventory;
    p.id = 0; p.maxHealth = p.health = this.diff.playerHealth; p.maxArmor = p.armor = lo.armor; p.speedMult = lo.speedMult;
    for (const [k, v] of lo.ammo) inv.reserve.set(k, v);
    inv.medkits = lo.medkits;
    for (let i = 0; i < 5; i++) {
      const id = lo.slotWeapons[i];
      const def = id ? WEAPONS.get(id) : undefined;
      if (!def) continue;
      const applied = applyUpgrades(def, lo.upgrades);
      const ws = new WeaponState(applied, 0);
      if (applied.isGun) {
        const take = Math.min(applied.magSize, inv.getReserve(applied.ammo));
        ws.mag = take;
        inv.addReserve(applied.ammo, -take);
      }
      inv.setSlot(applied.slot, ws);
    }
    if (!inv.slots[0]) inv.setSlot(WeaponSlot.Melee, new WeaponState(WEAPONS.get('knife')!));
    inv.current = inv.slots[1] ? 1 : 0;
  }

  // ================================================================== main loop
  update(dt: number, input: PlayerInput) {
    this.events.length = 0;
    if (this.state !== MissionState.Playing) return;
    this.time += dt;
    this.extractMsgCooldown -= dt;
    this.updatePlayer(dt, input);
    this.updateProjectiles(dt);
    this.updateNpcs(dt);
    this.updateDoors(dt);
    this.updateInteractables(dt);
    this.updateAlert(dt);
    this.updateObjectives();
  }

  emit(type: EvType, pos: Vec2, text: string | null = null, value = 0, actor = -1, sound: string | null = null) {
    this.events.push({ type, pos, text, value, actorId: actor, sound });
  }
  ev(e: GameEvent) { this.events.push(e); }
  message(text: string) { this.emit(EvType.Message, this.player.pos, text); }

  currentZoneName(): string {
    let best: string | null = null, area = Number.MAX_VALUE;
    for (const z of this.zones) {
      if (!z.rect.contains(this.player.pos)) continue;
      const a = z.rect.w * z.rect.h;
      if (a < area) { area = a; best = z.name; }
    }
    return best ?? this.map.name;
  }

  findNpc(key: string) { return this.npcs.find(n => n.key === key) ?? null; }

  get detectionLevel(): number {
    let m = 0;
    for (const n of this.npcs) if (!n.down && n.state !== AIState.Disabled) m = Math.max(m, n.suspicion);
    return m;
  }

  // ================================================================== doors
  private updateDoors(dt: number) {
    for (const d of this.world.doors) {
      d.anim = moveTowards(d.anim, d.open ? 1 : 0, dt * 6);
      if (d.autoClose > 0) {
        d.autoClose -= dt;
        if (d.autoClose <= 0) {
          if (this.anyActorInCell(d.cell)) d.autoClose = 0.5;
          else { d.open = false; this.emit(EvType.DoorClose, d.cell.center, null, d.index); this.lightsDirty = true; }
        }
      }
    }
  }

  private anyActorInCell(cell: Int2, margin = 0.08): boolean {
    const p = this.player;
    if (!p.hidden && touches(p.pos, p.radius + margin, cell)) return true;
    for (const n of this.npcs) if (!n.isCamera && touches(n.pos, n.radius + margin, cell)) return true;
    for (const b of this.bodies) if (!b.hidden && touches(b.pos, 0.3, cell)) return true;
    return false;
  }

  openDoor(d: DoorState, opener: Actor | null, npc: boolean) {
    if (d.open) return;
    d.open = true;
    if (npc && d.locked) d.autoClose = 2.2;
    const step = d.horizontal ? new Int2(1, 0) : new Int2(0, 1);
    for (const nb of [d.cell.add(step), d.cell.sub(step)]) {
      const o = this.world.doorAt(nb.x, nb.y);
      if (!o || o.open || o.type !== d.type) continue;
      if (o.locked && !npc && d.locked) continue;
      o.open = true;
      if (npc && o.locked) o.autoClose = d.autoClose;
      if (!npc && d.type !== DoorType.Normal) o.locked = false;
    }
    this.emit(EvType.DoorOpen, d.cell.center, null, d.index, opener?.id ?? -1);
    this.lightsDirty = true;
    if (!npc) this.emitNoise(d.cell.center, this.player.crouched ? 1.8 : 3.2, NoiseKind.Door, opener);
  }

  closeDoor(d: DoorState) {
    if (!d.open || this.anyActorInCell(d.cell)) return;
    d.open = false;
    this.emit(EvType.DoorClose, d.cell.center, null, d.index);
    this.lightsDirty = true;
  }

  // ================================================================== alert
  private updateAlert(dt: number) {
    let combat = false, searching = false, suspicious = false;
    for (const n of this.npcs) {
      if (n.down || n.isCamera) continue;
      if ((n.state === AIState.Attack || n.state === AIState.Chase) && n.lastSeenAgo < 12) combat = true;
      else if (n.state === AIState.Search || n.state === AIState.Panic || n.state === AIState.Flee) searching = true;
      else if (n.state === AIState.Suspicious || n.state === AIState.Investigate) suspicious = true;
    }
    const prev = this.alert;
    if (combat) { this.alert = AlertLevel.Combat; this.alertTimer = 25; }
    else if (this.alert === AlertLevel.Combat) { this.alertTimer -= dt; if (this.alertTimer <= 0) { this.alert = AlertLevel.Alarmed; this.alertTimer = 45; } }
    else if (this.alert === AlertLevel.Alarmed) { this.alertTimer -= dt; if (this.alertTimer <= 0 && !searching) this.alert = suspicious ? AlertLevel.Suspicious : AlertLevel.Calm; }
    else if (searching) { this.alert = AlertLevel.Alarmed; this.alertTimer = 30; }
    else this.alert = suspicious ? AlertLevel.Suspicious : AlertLevel.Calm;
    if (this.alert !== prev) this.emit(EvType.Alert, this.player.pos, AlertLevel[this.alert], this.alert);
  }

  raiseAlarm(duration = 45) {
    if (this.alert < AlertLevel.Alarmed) { this.alert = AlertLevel.Alarmed; this.emit(EvType.Alert, this.player.pos, AlertLevel[this.alert], this.alert); }
    this.alertTimer = Math.max(this.alertTimer, duration);
    for (const n of this.npcs) n.awareness = Math.min(1.6, n.awareness + 0.15);
  }

  // ================================================================== objectives
  objectives(): { def: ObjectiveDef; done: boolean }[] { return this.mission.objectives.map((d, i) => ({ def: d, done: this.objectiveDone[i] })); }

  get currentObjective(): ObjectiveDef | null {
    const obs = this.mission.objectives;
    for (let i = 0; i < obs.length; i++)
      if (!this.objectiveDone[i] && !obs[i].optional && (obs[i].type !== ObjectiveType.Extract || this.requiredDone)) return obs[i];
    return obs[obs.length - 1] ?? null;
  }

  get requiredDone(): boolean {
    const obs = this.mission.objectives;
    for (let i = 0; i < obs.length; i++) if (!obs[i].optional && obs[i].type !== ObjectiveType.Extract && !this.objectiveDone[i]) return false;
    return true;
  }

  private updateObjectives() {
    const obs = this.mission.objectives, inv = this.player.inventory;
    for (let i = 0; i < obs.length; i++) {
      if (this.objectiveDone[i]) continue;
      const o = obs[i];
      let done = false;
      switch (o.type) {
        case ObjectiveType.Eliminate: done = o.targetIds!.every(id => { const t = this.findNpc(id); return !t || t.down; }); break;
        case ObjectiveType.Retrieve: done = inv.intel.includes(o.itemId!) || (o.itemId!.startsWith('keycard_') && inv.keys.has(o.itemId!.substring(8))); break;
        case ObjectiveType.Download: done = this.interactables.some(t => t.kind === InteractKind.DownloadTerminal && t.key === o.itemId && t.used); break;
        case ObjectiveType.Reach: done = this.zones.some(z => z.name === o.zoneName && z.rect.contains(this.player.pos)); break;
      }
      if (done) {
        this.objectiveDone[i] = true;
        this.emit(EvType.Objective, this.player.pos, o.text, i);
        const cur = this.currentObjective;
        if (this.requiredDone && cur && cur.type === ObjectiveType.Extract) this.message('All objectives complete - get to an extraction point');
      }
    }
    for (const n of this.npcs) if (n.isTarget && n.escaped && !n.down) { this.fail(`${n.displayName} escaped`); return; }
    if (this.state === MissionState.Playing)
      for (const ez of this.extracts) {
        if (!ez.rect.contains(this.player.pos)) continue;
        if (this.requiredDone) {
          for (let i = 0; i < obs.length; i++) if (obs[i].type === ObjectiveType.Extract) this.objectiveDone[i] = true;
          this.complete();
        } else if (this.extractMsgCooldown <= 0) { this.message('Objectives incomplete - extraction unavailable'); this.extractMsgCooldown = 4; }
        break;
      }
  }

  fail(reason: string) {
    if (this.state !== MissionState.Playing || this.debugNoFail) return;
    this.state = MissionState.Failed; this.failReason = reason;
    this.emit(EvType.MissionFailed, this.player.pos, reason);
  }
  private complete() {
    if (this.state !== MissionState.Playing) return;
    this.state = MissionState.Complete;
    this.emit(EvType.MissionComplete, this.player.pos, this.mission.name);
  }
  die() {
    if (this.state !== MissionState.Playing) return;
    this.player.alive = false; this.state = MissionState.Dead; this.failReason = 'You were killed';
    this.emit(EvType.PlayerDied, this.player.pos);
  }

  buildResult(): MissionResult {
    if (this.result) return this.result;
    const r = new MissionResult(), inv = this.player.inventory;
    Object.assign(r, {
      missionId: this.mission.id, success: this.state === MissionState.Complete, failReason: this.failReason, time: this.time,
      kills: this.kills, targetsKilled: this.targetsKilled, civiliansKilled: this.civiliansKilled, nonTargetKills: this.nonTargetKills,
      subdued: this.subdued, bodiesFound: this.bodiesFound, spotted: this.spotted, cashFound: inv.cashFound, medkitsLeft: inv.medkits,
    });
    for (const [k, v] of inv.reserve) r.ammoLeft.set(k, v);
    for (const w of inv.slots) if (w && w.def.isGun) r.ammoLeft.set(w.def.ammo, (r.ammoLeft.get(w.def.ammo) ?? 0) + w.mag);
    if (r.success) {
      r.baseReward = this.mission.reward;
      this.mission.objectives.forEach((o, i) => {
        if (!this.objectiveDone[i]) return;
        r.objectivesCompleted.push(o.id);
        if (o.optional) r.objectiveBonus += o.bonus ?? 0;
      });
      for (const c of this.mission.challenges) {
        let ok = false;
        switch (c.type) {
          case ChallengeType.SilentAssassin: ok = !this.spotted; break;
          case ChallengeType.NoCivilianCasualties: ok = this.civiliansKilled === 0; break;
          case ChallengeType.Professional: ok = this.nonTargetKills === 0 && this.civiliansKilled === 0; break;
          case ChallengeType.NoBodiesFound: ok = this.bodiesFound === 0; break;
          case ChallengeType.Speed: ok = this.time <= c.parTime; break;
        }
        if (!ok) continue;
        r.challengesCompleted.push(c); r.challengeBonus += c.bonus;
      }
      r.penalty = this.civiliansKilled * 750;
      r.total = Math.max(0, r.baseReward + r.objectiveBonus + r.challengeBonus + r.cashFound - r.penalty);
    }
    r.rating = !r.success ? 'Contract Failed'
      : !r.spotted && r.nonTargetKills === 0 && r.civiliansKilled === 0 && r.bodiesFound === 0 ? 'Silent Assassin'
      : !r.spotted ? 'Shadow' : r.civiliansKilled > 0 ? 'Butcher' : r.nonTargetKills <= 3 ? 'Professional' : 'Mercenary';
    this.result = r;
    return r;
  }

  // ================================================================== player
  private updatePlayer(dt: number, input: PlayerInput) {
    const p = this.player, inv = p.inventory, W = this.world;
    this.msgThrottle -= dt;
    p.lastShotNoise += dt;
    p.damageFlash = Math.max(0, p.damageFlash - dt);
    for (const w of inv.slots) w?.tick(dt);
    if (!input.fireHeld && inv.currentWeapon) inv.currentWeapon.triggerReleased = true;

    if (p.actionLock > 0) {
      p.actionLock -= dt; p.moving = false;
      if (p.actionLock <= 0) p.actionLabel = null;
      return;
    }
    if (p.hidden) { p.moving = false; p.sprinting = false; if (input.interactPressed) this.leaveHiding(); return; }

    const inVentTile = W.tileAt(Math.floor(p.pos.x), Math.floor(p.pos.y)).kind === TileKind.Vent;
    if (input.crouchPressed) {
      if (p.crouched) {
        if (!W.circleOverlapsSolid(p.pos, p.radius, MoverKind.Player)) p.crouched = false;
        else if (this.msgThrottle <= 0) { this.message('Not enough room to stand'); this.msgThrottle = 2; }
      } else p.crouched = true;
    }
    let cur = inv.currentWeapon;
    const dragging = p.draggingBody >= 0;
    p.aiming = input.altHeld && !!cur && (cur.def.isGun || cur.def.isThrown) && !dragging;
    let move = input.move;
    if (move.sqrLength > 1) move = move.normalized;
    p.moving = move.sqrLength > 0.01;
    p.sprinting = input.sprintHeld && p.moving && !p.aiming && !dragging && !inVentTile;
    if (p.sprinting && p.crouched) {
      if (!W.circleOverlapsSolid(p.pos, p.radius, MoverKind.Player)) p.crouched = false; else p.sprinting = false;
    }
    let speed = p.sprinting ? SPRINT : p.crouched ? CROUCH : WALK;
    if (p.aiming) speed *= 0.65;
    if (dragging) speed = Math.min(speed, 2.1);
    if (cur) speed *= cur.def.moveSpeedMult;
    speed *= p.speedMult;

    if (p.moving) {
      const delta = move.mul(speed * dt);
      this.bumpDoors(p.pos, move);
      const before = p.pos;
      p.pos = W.moveCircle(p.pos, p.radius, delta, p.crouched ? MoverKind.PlayerCrouched : MoverKind.Player);
      const moved = Vec2.distance(before, p.pos);
      p.moveAnim += moved;
      p.velocity = p.pos.sub(before).div(dt);
      this.footstepCheck(moved);
    } else p.velocity = Vec2.Zero;

    p.inVent = W.tileAt(Math.floor(p.pos.x), Math.floor(p.pos.y)).kind === TileKind.Vent;
    if (p.inVent) p.crouched = true;

    const aimDir = input.aim.sub(p.pos);
    if (aimDir.sqrLength > 0.0001) p.facing = aimDir.angle;

    if (dragging) {
      const body = this.bodies.find(b => b.id === p.draggingBody);
      if (!body || body.hidden) p.draggingBody = -1;
      else {
        const back = p.pos.sub(Vec2.fromAngle(p.facing).mul(0.55));
        body.pos = W.moveCircle(body.pos, 0.2, back.sub(body.pos), MoverKind.Player);
        body.facing = p.pos.sub(body.pos).angle;
      }
    }

    if (input.selectSlot >= 0 && inv.select(input.selectSlot))
      this.emit(EvType.WeaponSwitch, p.pos, inv.currentWeapon!.def.name, input.selectSlot, 0, inv.currentWeapon!.def.sound);
    else if (input.cycle !== 0 && inv.cycle(input.cycle))
      this.emit(EvType.WeaponSwitch, p.pos, inv.currentWeapon!.def.name, inv.current, 0, inv.currentWeapon!.def.sound);
    cur = inv.currentWeapon;

    if (cur && cur.def.isGun) {
      if (input.reloadPressed && cur.startReload(inv.getReserve(cur.def.ammo)))
        this.emit(EvType.ReloadStart, p.pos, cur.def.name, cur.def.reloadTime, 0, cur.def.sound);
      if (cur.reloading) {
        const moved = cur.tickReload(dt, inv.getReserve(cur.def.ammo));
        if (moved > 0) {
          inv.addReserve(cur.def.ammo, -moved);
          if (!cur.reloading) this.emit(EvType.ReloadDone, p.pos, cur.def.name, 0, 0, cur.def.sound);
          else if (cur.def.shellReload) this.emit(EvType.ReloadStart, p.pos, 'shell', cur.def.reloadTime, 0, cur.def.sound);
        }
      }
    }

    if (cur && !dragging && !p.inVent) {
      const wantFire = cur.def.automatic ? input.fireHeld : input.firePressed || (cur.def.isMelee && input.fireHeld);
      if (wantFire) this.playerAttack(cur, input.firePressed);
      if (input.altPressed && cur.def.isMelee) this.trySubdue();
    }
    if (input.medkitPressed) this.useMedkit();
    this.updateInteraction(dt, input);
    this.autoPickups();
  }

  private footstepCheck(moved: number) {
    const p = this.player;
    p.footstepDist += moved;
    const stride = p.sprinting ? 1.25 : p.crouched ? 0.8 : 0.95;
    if (p.footstepDist < stride) return;
    p.footstepDist = 0;
    const t = this.world.tileAt(Math.floor(p.pos.x), Math.floor(p.pos.y));
    const loud = p.sprinting ? 1 : p.crouched ? 0.15 : 0.45;
    this.ev({ type: EvType.Footstep, pos: p.pos, value: loud, actorId: 0, text: surfaceName(t) });
    if (p.sprinting) this.emitNoise(p.pos, 5.5, NoiseKind.Footstep, p);
    else if (!p.crouched) this.emitNoise(p.pos, 1.7, NoiseKind.Footstep, p);
  }

  private bumpDoors(pos: Vec2, dir: Vec2) {
    const probe = pos.add(dir.normalized.mul(this.player.radius + 0.25));
    const d = this.world.doorAt(Math.floor(probe.x), Math.floor(probe.y));
    if (!d || d.open) return;
    if (!d.locked) this.openDoor(d, this.player, false);
    else if (d.type !== DoorType.Secret && this.msgThrottle <= 0) {
      if (this.player.inventory.keys.has(d.keyId!)) this.message('Press [Interact] to unlock with the ' + d.keyId + ' keycard');
      else { this.message(`Locked - requires a ${d.keyId} keycard`); this.emit(EvType.DoorLocked, d.cell.center); }
      this.msgThrottle = 2.5;
    }
  }

  private useMedkit() {
    const p = this.player;
    if (p.inventory.medkits <= 0) { this.message('No medkits'); return; }
    if (p.health >= p.maxHealth) { this.message('Health is full'); return; }
    p.inventory.medkits--;
    p.health = Math.min(p.maxHealth, p.health + 60);
    p.actionLock = 0.6; p.actionLabel = 'Healing';
    this.emit(EvType.Medkit, p.pos, null, p.health);
  }

  private autoPickups() {
    const p = this.player;
    for (const pk of this.pickups) {
      if (pk.taken || pk.requiresInteract) continue;
      if (Vec2.sqrDistance(pk.pos, p.pos) > 0.75 * 0.75) continue;
      this.takePickup(pk);
    }
    for (const b of this.bodies) {
      if (b.looted || b.hidden || Vec2.sqrDistance(b.pos, p.pos) > 0.9 * 0.9) continue;
      this.lootBody(b);
    }
  }

  takePickup(pk: Pickup) {
    const inv = this.player.inventory;
    let msg: string | null = null;
    switch (pk.kind) {
      case PickupKind.Ammo: {
        const gun = inv.currentWeapon && inv.currentWeapon.def.isGun ? inv.currentWeapon.def : inv.slots[1]?.def;
        const type = gun?.ammo ?? pk.ammoType;
        const amount = type === AmmoType.Shells ? Math.max(4, Math.floor(pk.amount / 4)) : pk.amount;
        inv.addReserve(type, amount);
        msg = `+${amount} ${ammoName(type)}`;
        break;
      }
      case PickupKind.Medkit:
        if (inv.medkits >= 3) { if (this.msgThrottle <= 0) { this.message('Medkits full (3)'); this.msgThrottle = 3; } return; }
        inv.medkits++; msg = '+1 Medkit'; break;
      case PickupKind.Armor: {
        const p = this.player;
        if (p.armor >= Math.max(p.maxArmor, 50)) return;
        p.maxArmor = Math.max(p.maxArmor, 50);
        p.armor = Math.min(p.maxArmor, p.armor + pk.amount);
        msg = `+${pk.amount} Armor`; break;
      }
      case PickupKind.Cash: inv.cashFound += pk.amount; msg = `+$${pk.amount}`; break;
      case PickupKind.Keycard: inv.keys.add(pk.itemId); msg = `Picked up ${pk.name}`; break;
      case PickupKind.Intel: inv.intel.push(pk.itemId); msg = `Acquired: ${pk.name}`; break;
      case PickupKind.Knives:
        inv.addReserve(AmmoType.Knives, pk.amount);
        if (!inv.slots[WeaponSlot.Throwing]) inv.setSlot(WeaponSlot.Throwing, new WeaponState(WEAPONS.get('throwing_knives')!));
        msg = pk.amount === 1 ? 'Recovered throwing knife' : `+${pk.amount} Throwing knives`; break;
      case PickupKind.Coins: inv.addReserve(AmmoType.Coins, pk.amount); msg = '+1 Coin'; break;
      case PickupKind.Weapon: {
        const def = WEAPONS.get(pk.itemId)!;
        inv.setSlot(def.slot, new WeaponState(def, def.magSize));
        inv.addReserve(def.ammo, pk.amount);
        inv.select(def.slot);
        msg = `Picked up ${def.name}`;
        break;
      }
    }
    pk.taken = true;
    this.ev({ type: EvType.Pickup, pos: pk.pos, text: msg, sound: PickupKind[pk.kind], actorId: pk.id });
  }

  private lootBody(b: Body) {
    b.looted = true;
    const inv = this.player.inventory, n = b.npc;
    let msg: string | null = null;
    if (n.carriedKey && !inv.keys.has(n.carriedKey)) { inv.keys.add(n.carriedKey); msg = `Took ${cap(n.carriedKey)} Keycard`; }
    if (n.weapon && n.weapon.def.isGun) {
      const amount = Math.max(4, Math.floor(n.weapon.def.magSize / 2));
      inv.addReserve(n.weapon.def.ammo, amount);
      msg = (msg ? msg + ', ' : '') + `+${amount} ${ammoName(n.weapon.def.ammo)}`;
    }
    if (b.knivesInside > 0) {
      inv.addReserve(AmmoType.Knives, b.knivesInside);
      msg = (msg ? msg + ', ' : '') + `recovered ${b.knivesInside} knife`;
      b.knivesInside = 0;
    }
    if (msg) this.ev({ type: EvType.Pickup, pos: b.pos, text: msg, sound: 'Loot' });
  }

  // ================================================================== combat
  private playerAttack(w: WeaponState, pressed: boolean) {
    const p = this.player, inv = p.inventory;
    const block = w.canFire(pressed);
    if (block === FireBlock.Empty) {
      if (pressed) {
        this.emit(EvType.DryFire, p.pos, null, 0, 0, w.def.sound);
        if (w.startReload(inv.getReserve(w.def.ammo))) this.emit(EvType.ReloadStart, p.pos, w.def.name, w.def.reloadTime, 0, w.def.sound);
        else if (this.msgThrottle <= 0) { this.message(`Out of ${ammoName(w.def.ammo)}`); this.msgThrottle = 2; }
      }
      return;
    }
    if (block !== FireBlock.None) return;
    if (w.def.isMelee) { this.meleeAttack(w); return; }
    if (w.def.isThrown) {
      if (inv.getReserve(w.def.ammo) <= 0) {
        if (pressed && this.msgThrottle <= 0) { this.message(`No ${ammoName(w.def.ammo)} left`); this.msgThrottle = 2; }
        return;
      }
      inv.addReserve(w.def.ammo, -1);
      const dirs = w.fire(this.rng, p.facing, p.moving, p.aiming);
      this.throwProjectile(w.def, dirs[0]);
      return;
    }
    const angles = w.fire(this.rng, p.facing, p.moving, p.aiming);
    this.shotsFired++;
    let muzzle = p.pos.add(Vec2.fromAngle(p.facing).mul(0.55));
    if (!this.world.hasLineOfSight(p.pos, muzzle)) muzzle = p.pos;
    for (const a of angles) this.hitscan(p, muzzle, a, w.def.range, w.def.damage);
    this.ev({ type: EvType.Shot, pos: muzzle, angle: p.facing, sound: w.def.sound, actorId: 0, flag: w.def.suppressed, value: w.def.shake });
    this.ev({ type: EvType.Casing, pos: p.pos, angle: p.facing - PI / 2, sound: w.def.cls === WeaponClass.Shotgun ? 'shell' : 'casing' });
    this.emitNoise(p.pos, w.def.noise, w.def.suppressed ? NoiseKind.SuppressedShot : NoiseKind.Gunshot, p);
    p.lastShotNoise = 0;
    if (!w.def.suppressed) this.witnessesSeePlayer(1);
    if (w.mag === 0 && inv.getReserve(w.def.ammo) > 0 && w.startReload(inv.getReserve(w.def.ammo)))
      this.emit(EvType.ReloadStart, p.pos, w.def.name, w.def.reloadTime, 0, w.def.sound);
  }

  private meleeAttack(w: WeaponState) {
    const p = this.player;
    w.fire(this.rng, p.facing, false, false);
    this.ev({ type: EvType.Melee, pos: p.pos, angle: p.facing, sound: w.def.sound, actorId: 0, value: w.def.meleeArc });
    let best: Npc | null = null, bestD = Number.MAX_VALUE;
    for (const n of this.npcs) {
      if (n.down || n.isCamera) continue;
      const to = n.pos.sub(p.pos), d = to.length;
      if (d > w.def.meleeRange + n.radius) continue;
      if (Math.abs(angleDelta(p.facing, to.angle)) > w.def.meleeArc * 0.5 * DEG2RAD + 0.2) continue;
      if (!this.world.hasLineOfSight(p.pos, n.pos)) continue;
      if (d < bestD) { bestD = d; best = n; }
    }
    if (!best) return;
    if (this.isUnawareOfPlayer(best) && this.isBehind(best)) {
      best.health = 0;
      this.ev({ type: EvType.Takedown, pos: best.pos, angle: p.facing, actorId: best.id, sound: 'stab' });
      this.killNpc(best, best.pos.sub(p.pos).normalized, true);
      p.actionLock = 0.35;
      this.emitNoise(best.pos, w.def.noise, NoiseKind.Takedown, p);
    } else {
      this.damageNpc(best, w.def.damage * (this.isUnawareOfPlayer(best) ? 1.6 : 1), best.pos.sub(p.pos).normalized, p);
      this.emitNoise(best.pos, 3, NoiseKind.Takedown, p);
    }
  }

  private trySubdue() {
    const p = this.player;
    for (const n of this.npcs) {
      if (n.down || n.isCamera) continue;
      const to = n.pos.sub(p.pos);
      if (to.length > 1.15 + n.radius) continue;
      if (Math.abs(angleDelta(p.facing, to.angle)) > 1.0) continue;
      if (!this.isUnawareOfPlayer(n) || !this.isBehind(n)) {
        if (this.msgThrottle <= 0) { this.message('Get behind an unaware target to subdue'); this.msgThrottle = 2; }
        return;
      }
      n.unconscious = true; n.state = AIState.Unconscious; n.velocity = Vec2.Zero;
      this.subdued++;
      p.actionLock = 0.9; p.actionLabel = 'Subduing';
      this.createBody(n);
      this.cancelRadio(n);
      this.ev({ type: EvType.Subdue, pos: n.pos, angle: p.facing, actorId: n.id, text: n.isTarget ? `${n.displayName} subdued` : null });
      this.emitNoise(n.pos, 1.2, NoiseKind.BodyFall, p);
      this.witnessesSeeKill(n);
      return;
    }
  }

  isUnawareOfPlayer(n: Npc): boolean {
    if (n.isCamera) return false;
    if (n.state === AIState.Attack || n.state === AIState.Chase || n.state === AIState.Panic) return false;
    return !(n.seesPlayer && n.suspicion > 0.5);
  }

  isBehind(n: Npc): boolean {
    const to = this.player.pos.sub(n.pos);
    return Math.abs(angleDelta(n.facing, to.angle)) > 70 * DEG2RAD || !n.seesPlayer;
  }

  private throwProjectile(def: WeaponDef, angle: number) {
    const p = this.player;
    const pr = new Projectile();
    pr.id = this.newId(); pr.kind = def.cls === WeaponClass.Throwing ? ProjectileKind.Knife : ProjectileKind.Coin;
    pr.pos = p.pos.add(Vec2.fromAngle(angle).mul(0.4)); pr.vel = Vec2.fromAngle(angle).mul(def.projectileSpeed);
    pr.angle = angle; pr.life = def.range / def.projectileSpeed; pr.damage = def.damage; pr.fromPlayer = true;
    if (!this.world.hasLineOfSight(p.pos, pr.pos)) pr.pos = p.pos;
    this.projectiles.push(pr);
    this.ev({ type: EvType.Throw, pos: pr.pos, angle, sound: def.sound, actorId: pr.id });
  }

  private updateProjectiles(dt: number) {
    for (let i = this.projectiles.length - 1; i >= 0; i--) {
      const pr = this.projectiles[i];
      if (pr.done) { this.projectiles.splice(i, 1); continue; }
      const step = pr.vel.mul(dt), len = step.length;
      const dir = step.div(Math.max(len, 1e-6));
      const hit = this.world.raycast(pr.pos, dir, len, RayMode.Projectile);
      const next = hit.hit ? hit.point.sub(dir.mul(0.05)) : pr.pos.add(step);
      if (pr.kind === ProjectileKind.Knife) {
        for (const n of this.npcs) {
          if (n.down || n.isCamera) continue;
          if (segmentCircle(pr.pos, next, n.pos, n.radius + 0.05)) {
            const dmg = this.isUnawareOfPlayer(n) ? 150 : 70;
            this.damageNpc(n, dmg, pr.vel.normalized, this.player);
            const body = n.down ? this.bodies.find(b => b.npc === n) : undefined;
            if (body) body.knivesInside++; else this.dropKnife(n.pos.sub(pr.vel.normalized.mul(0.4)));
            pr.done = true;
            break;
          }
        }
        if (pr.done) continue;
      }
      pr.pos = next;
      pr.angle += dt * (pr.kind === ProjectileKind.Knife ? 25 : 12);
      pr.life -= dt;
      if (hit.hit || pr.life <= 0) {
        pr.done = true;
        if (pr.kind === ProjectileKind.Knife) {
          this.ev({ type: EvType.KnifeStuck, pos: pr.pos, angle: pr.vel.angle, actorId: pr.id });
          this.emitNoise(pr.pos, 2.5, NoiseKind.Distraction, this.player);
          this.dropKnife(pr.pos);
        } else {
          this.ev({ type: EvType.CoinLand, pos: pr.pos, actorId: pr.id, sound: 'coin' });
          this.emitNoise(pr.pos, 6.5, NoiseKind.Distraction, this.player);
        }
      }
    }
  }

  private dropKnife(pos: Vec2) {
    const c = Int2.fromWorld(pos);
    const at = this.world.isWalkableForNpc(c.x, c.y) ? pos : this.world.nearestWalkable(c, 2).center;
    const p = new Pickup();
    Object.assign(p, { id: this.newId(), kind: PickupKind.Knives, ammoType: AmmoType.Knives, amount: 1, name: 'Throwing knife', pos: at, itemId: 'knife' });
    this.pickups.push(p);
  }

  hitscan(shooter: Actor, origin: Vec2, angle: number, range: number, damage: number) {
    const dir = Vec2.fromAngle(angle);
    let start = origin, remaining = range, end = origin.add(dir.mul(range));
    let wall: RayHit = { hit: false, distance: 0, point: end, cell: new Int2(0, 0), normal: Vec2.Zero };
    for (let g = 0; g < 6; g++) {
      wall = this.world.raycast(start, dir, remaining, RayMode.Bullets);
      if (!wall.hit) { end = start.add(dir.mul(remaining)); break; }
      const t = this.world.tileAt(wall.cell.x, wall.cell.y);
      if (t.kind === TileKind.Window && !this.world.isWindowBroken(wall.cell.x, wall.cell.y)) {
        this.world.breakWindow(wall.cell.x, wall.cell.y);
        this.ev({ type: EvType.WindowBreak, pos: wall.cell.center, value: wall.cell.x, angle: wall.cell.y });
        this.emitNoise(wall.cell.center, 9, NoiseKind.GlassBreak, shooter);
        const used = wall.distance + 0.01;
        start = start.add(dir.mul(used)); remaining -= used;
        continue;
      }
      end = wall.point;
      break;
    }
    const segLen = Vec2.distance(origin, end);
    let victim: Actor | null = null, bestT = Number.MAX_VALUE;
    const test = (a: Actor) => {
      const oc = a.pos.sub(origin), t = Vec2.dot(oc, dir);
      if (t < 0 || t > segLen) return;
      const d2 = oc.sqrLength - t * t, r = a.radius + 0.06;
      if (d2 > r * r) return;
      const tHit = t - Math.sqrt(Math.max(0, r * r - d2));
      if (tHit < bestT) { bestT = Math.max(0, tHit); victim = a; }
    };
    const p = this.player;
    if (shooter !== p && !p.hidden && !p.inVent) test(p);
    for (const n of this.npcs) {
      if (n === shooter || n.down) continue;
      if (shooter instanceof Npc && n.isHostile && shooter.isHostile) continue;
      test(n);
    }
    if (victim) {
      const v = victim as Actor;
      end = origin.add(dir.mul(bestT));
      if (v === p) this.damagePlayer(damage * this.diff.enemyDamage, dir);
      else this.damageNpc(v as Npc, damage, dir, shooter);
      this.ev({ type: EvType.Hit, pos: end, angle, actorId: v.id, flag: v instanceof Npc && v.isCamera });
    } else if (wall.hit) {
      const tile = this.world.tileAt(wall.cell.x, wall.cell.y);
      this.ev({ type: EvType.Impact, pos: end, pos2: wall.normal, angle, text: tile.kind === TileKind.Furniture ? FurnitureType[tile.furniture] : TileKind[tile.kind] });
      if (tile.kind === TileKind.Furniture && tile.furniture === FurnitureType.Barrel) this.explode(wall.cell.center, shooter, wall.cell);
    }
    this.ev({ type: EvType.Tracer, pos: origin, pos2: end, angle, actorId: shooter.id });
  }

  damagePlayer(dmg: number, dir: Vec2) {
    const p = this.player;
    if (p.godMode || !p.alive) return;
    if (p.armor > 0) { const a = Math.min(p.armor, dmg * 0.6); p.armor -= a; dmg -= a; }
    p.health -= dmg;
    p.damageFlash = 0.35;
    this.ev({ type: EvType.PlayerHurt, pos: p.pos, angle: dir.angle, value: dmg, actorId: 0 });
    if (p.health <= 0) { p.health = 0; this.die(); }
  }

  damageNpc(n: Npc, dmg: number, dir: Vec2, source: Actor | null) {
    if (n.down) return;
    n.health -= dmg;
    if (n.health <= 0) { this.killNpc(n, dir, false); return; }
    if (n.isCamera) return;
    if (source === this.player) {
      n.lastKnownPlayer = this.player.pos; n.lastSeenAgo = 0.5;
      if (n.isHostile) {
        n.suspicion = Math.max(n.suspicion, 0.95);
        n.facing = this.player.pos.sub(n.pos).angle;
        this.setState(n, this.canSee(n, this.player.pos, this.player.crouched) > 0 ? AIState.Attack : AIState.Chase);
        this.bark(n, 'hurt');
      } else this.panic(n, this.player.pos);
    }
  }

  killNpc(n: Npc, dir: Vec2, silent: boolean) {
    if (!n.alive) return;
    n.alive = false; n.health = 0; n.velocity = Vec2.Zero; n.state = AIState.Dead;
    this.cancelRadio(n);
    if (n.isCamera) {
      this.ev({ type: EvType.Death, pos: n.pos, actorId: n.id, flag: true });
      this.emitNoise(n.pos, 4, NoiseKind.GlassBreak, this.player);
      return;
    }
    this.kills++;
    if (n.isTarget) { this.targetsKilled++; this.message(`Target eliminated: ${n.displayName}`); }
    else if (n.isCivilian) this.civiliansKilled++;
    else this.nonTargetKills++;
    this.createBody(n, dir.angle);
    this.ev({ type: EvType.Death, pos: n.pos, angle: dir.angle, actorId: n.id, flag: silent });
    this.emitNoise(n.pos, silent ? 1.2 : 2.2, NoiseKind.BodyFall, this.player);
    this.witnessesSeeKill(n);
  }

  private createBody(n: Npc, facing = NaN): Body {
    const b = new Body();
    b.id = this.newId(); b.npc = n; b.pos = n.pos; b.facing = isNaN(facing) ? n.facing : facing;
    this.bodies.push(b);
    return b;
  }

  private witnessesSeeKill(victim: Npc) {
    for (const w of this.npcs) {
      if (w === victim || w.down || w.state === AIState.Disabled) continue;
      if (this.canSee(w, victim.pos, false) <= 0) continue;
      if (this.canSee(w, this.player.pos, this.player.crouched) > 0) this.spotPlayer(w);
      else this.onBodySeen(w, this.bodies.find(b => b.npc === victim) ?? null);
    }
  }

  private witnessesSeePlayer(amount: number) {
    for (const w of this.npcs) {
      if (w.down || w.state === AIState.Disabled) continue;
      if (this.canSee(w, this.player.pos, this.player.crouched) > 0) {
        w.suspicion = Math.min(1, w.suspicion + amount);
        if (w.suspicion >= 1) this.spotPlayer(w);
      }
    }
  }

  explode(pos: Vec2, source: Actor | null, barrelCell: Int2 | null = null) {
    if (barrelCell) {
      const t = this.world.tileAt(barrelCell.x, barrelCell.y);
      if (!(t.kind === TileKind.Furniture && t.furniture === FurnitureType.Barrel)) return;
      const floor = tileFromChar('.');
      floor.floor = FloorStyle.D;
      this.map.set(barrelCell.x, barrelCell.y, floor);
    }
    const radius = 2.8;
    this.ev({ type: EvType.Explosion, pos, value: radius, actorId: source?.id ?? -1 });
    this.emitNoise(pos, 26, NoiseKind.Explosion, source);
    this.lightsDirty = true;
    const p = this.player;
    const d = Vec2.distance(p.pos, pos);
    if (d < radius && !p.hidden && this.world.hasLineOfSight(pos, p.pos)) this.damagePlayer(140 * (1 - d / radius), p.pos.sub(pos).normalized);
    for (const n of this.npcs.slice()) {
      if (n.down) continue;
      const nd = Vec2.distance(n.pos, pos);
      if (nd < radius && this.world.hasLineOfSight(pos, n.pos)) this.damageNpc(n, 180 * (1 - nd / radius) + 20, n.pos.sub(pos).normalized, source);
    }
    const r = Math.ceil(radius), cell = Int2.fromWorld(pos);
    for (let y = cell.y - r; y <= cell.y + r; y++)
      for (let x = cell.x - r; x <= cell.x + r; x++) {
        const t = this.world.tileAt(x, y);
        const c = new Int2(x, y);
        if (t.kind === TileKind.Furniture && t.furniture === FurnitureType.Barrel && Vec2.distance(c.center, pos) < radius) this.explode(c.center, source, c);
      }
  }

  private npcFire(n: Npc) {
    const w = n.weapon;
    if (!w) return;
    if (w.mag <= 0) {
      if (!w.reloading) { w.startReload(999); this.emit(EvType.ReloadStart, n.pos, null, w.def.reloadTime, n.id, w.def.sound); }
      return;
    }
    if (w.canFire(true) !== FireBlock.None) return;
    const p = this.player;
    const target = p.pos.add(p.velocity.mul(0.08));
    const aim = target.sub(n.pos).angle;
    const moving = p.velocity.length > 0.5 ? 1.4 : 1;
    const angles = w.fire(this.rng, aim, n.speed > 0.3, false, this.diff.enemySpread * moving * (n.kind === NpcKind.Elite ? 0.8 : 1.2));
    let muzzle = n.pos.add(Vec2.fromAngle(n.facing).mul(0.5));
    if (!this.world.hasLineOfSight(n.pos, muzzle)) muzzle = n.pos;
    for (const a of angles) this.hitscan(n, muzzle, a, w.def.range, w.def.damage * 0.8);
    this.ev({ type: EvType.Shot, pos: muzzle, angle: n.facing, sound: w.def.sound, actorId: n.id, flag: w.def.suppressed });
    this.ev({ type: EvType.Casing, pos: n.pos, angle: n.facing - PI / 2, sound: 'casing' });
    this.emitNoise(n.pos, w.def.noise, NoiseKind.Gunshot, n);
  }

  // ================================================================== interaction
  private updateInteraction(dt: number, input: PlayerInput) {
    this.candidate = this.findCandidate();
    const c = this.candidate;
    if (c.kind === CandidateKind.None || !c.enabled) {
      this.holdProgress = 0;
      if (c.kind !== CandidateKind.None && input.interactPressed && c.door) this.emit(EvType.DoorLocked, c.door.cell.center);
      return;
    }
    if (c.holdTime > 0) {
      const same = this.holdTarget.kind === c.kind && this.holdTarget.interactable === c.interactable;
      if (input.interactHeld && same && !this.player.moving) {
        this.holdProgress += dt / c.holdTime;
        this.player.actionLabel = c.prompt;
        if (this.holdProgress >= 1) { this.holdProgress = 0; this.player.actionLabel = null; this.execute(c); }
      } else {
        this.holdProgress = input.interactPressed ? 0.0001 : 0;
        if (!input.interactHeld) this.player.actionLabel = null;
      }
      this.holdTarget = c;
      return;
    }
    this.holdProgress = 0;
    if (input.interactPressed) this.execute(c);
  }

  private findCandidate(): InteractCandidate {
    const p = this.player, W = this.world;
    let best: InteractCandidate = NONE_CANDIDATE, bestScore = Number.MAX_VALUE;
    const fwd = Vec2.fromAngle(p.facing);
    const consider = (c: InteractCandidate, dist: number, prio: number) => {
      const to = c.pos.sub(p.pos);
      const face = to.sqrLength > 0.01 ? (1 - Vec2.dot(to.normalized, fwd)) * 0.4 : 0;
      const score = dist + face + prio;
      if (score < bestScore) { bestScore = score; best = c; }
    };
    const mk = (o: Partial<InteractCandidate>): InteractCandidate => Object.assign({ kind: CandidateKind.None, prompt: '', pos: Vec2.Zero, holdTime: 0, enabled: true }, o);
    const pc = Int2.fromWorld(p.pos);
    const dragging = p.draggingBody >= 0;
    for (let dy = -1; dy <= 1; dy++)
      for (let dx = -1; dx <= 1; dx++) {
        const cell = new Int2(pc.x + dx, pc.y + dy);
        if (!tileIs(W.tileAt(cell.x, cell.y), TF.HidingSpot)) continue;
        const d = Vec2.distance(cell.center, p.pos);
        if (d > 1.25) continue;
        const occupied = this.closetBodies.has(cell.key);
        if (dragging) consider(mk({ kind: CandidateKind.StashBody, pos: cell.center, cell, enabled: !occupied, prompt: occupied ? 'Closet is full' : 'Stash body in closet' }), d, -0.5);
        else consider(mk({ kind: CandidateKind.Hide, pos: cell.center, cell, enabled: !occupied, prompt: occupied ? 'Closet is full' : 'Hide in closet' }), d, 0.1);
      }
    if (dragging) {
      consider(mk({ kind: CandidateKind.DropBody, pos: p.pos, prompt: 'Drop body' }), 1.2, 0);
      return best;
    }
    for (const b of this.bodies) {
      if (b.hidden) continue;
      const d = Vec2.distance(b.pos, p.pos);
      if (d > 1.1) continue;
      consider(mk({ kind: CandidateKind.DragBody, pos: b.pos, body: b, prompt: 'Drag body' }), d, 0.2);
    }
    for (const it of this.interactables) {
      const d = Vec2.distance(it.pos, p.pos);
      if (d > 1.35) continue;
      if (it.kind !== InteractKind.Stairs && !W.hasLineOfSight(p.pos, it.pos) && d > 0.8) continue;
      const c = mk({ kind: CandidateKind.Interactable, pos: it.pos, interactable: it });
      switch (it.kind) {
        case InteractKind.PowerBox: c.prompt = it.active ? `Restore power (${it.label})` : `Cut power (${it.label})`; break;
        case InteractKind.CameraTerminal: c.prompt = it.used ? 'Cameras offline' : 'Disable security cameras'; c.enabled = !it.used; c.holdTime = it.duration; break;
        case InteractKind.DownloadTerminal: c.prompt = it.used ? 'Download complete' : `Download data (${it.label})`; c.enabled = !it.used; c.holdTime = it.duration; break;
        case InteractKind.Distraction: c.prompt = it.active ? `${it.label} is running` : `Turn on ${it.label.toLowerCase()}`; c.enabled = !it.active; break;
        case InteractKind.Stairs: {
          const locked = !!it.lock && !p.inventory.keys.has(it.lock);
          c.prompt = locked ? `${it.label} - requires ${it.lock} keycard` : it.label === 'Stairs' ? 'Take the stairs' : `Use ${it.label.toLowerCase()}`;
          c.enabled = !locked;
          break;
        }
      }
      consider(c, d, 0);
    }
    for (const pk of this.pickups) {
      if (pk.taken || !pk.requiresInteract) continue;
      const d = Vec2.distance(pk.pos, p.pos);
      if (d > 1.0) continue;
      consider(mk({ kind: CandidateKind.WeaponPickup, pos: pk.pos, pickup: pk, prompt: `Pick up ${pk.name}` }), d, 0.1);
    }
    for (const dir of [new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1)]) {
      const cell = pc.add(dir);
      const d = Vec2.distance(cell.center, p.pos);
      if (d > 1.3) continue;
      const door = W.doorAt(cell.x, cell.y);
      if (door) {
        const c = mk({ kind: CandidateKind.Door, pos: cell.center, door, cell });
        if (door.type === DoorType.Secret && !door.open) c.prompt = 'Examine bookshelf';
        else if (door.open) c.prompt = 'Close door';
        else if (door.locked) {
          const has = p.inventory.keys.has(door.keyId!);
          c.prompt = has ? `Unlock with ${door.keyId} keycard` : `Locked - requires ${door.keyId} keycard`;
          c.enabled = has;
        } else c.prompt = 'Open door';
        consider(c, d, 0.15);
        continue;
      }
      if (W.tileAt(cell.x, cell.y).kind === TileKind.Window) {
        const beyond = cell.add(dir);
        if (!W.isSolid(beyond.x, beyond.y, MoverKind.Player))
          consider(mk({ kind: CandidateKind.Window, pos: cell.center, cell, dir, prompt: 'Climb through window' }), d, 0.3);
      }
    }
    return best;
  }

  private execute(c: InteractCandidate) {
    const p = this.player;
    switch (c.kind) {
      case CandidateKind.Hide:
        p.hidden = true; p.hiddenIn = c.cell!; p.hideExitPos = p.pos; p.crouched = true;
        p.inventory.currentWeapon?.cancelReload();
        this.ev({ type: EvType.Hide, pos: c.cell!.center, actorId: 0 });
        break;
      case CandidateKind.StashBody: {
        const body = this.bodies.find(b => b.id === p.draggingBody);
        if (body) {
          body.hidden = true; body.dragged = false; body.pos = c.cell!.center;
          this.closetBodies.set(c.cell!.key, body.id);
          this.ev({ type: EvType.BodyStash, pos: c.cell!.center, actorId: body.id });
        }
        p.draggingBody = -1;
        break;
      }
      case CandidateKind.DropBody: {
        const body = this.bodies.find(b => b.id === p.draggingBody);
        if (body) { body.dragged = false; this.ev({ type: EvType.BodyDrop, pos: body.pos, actorId: body.id }); }
        p.draggingBody = -1;
        break;
      }
      case CandidateKind.DragBody:
        p.draggingBody = c.body!.id; c.body!.dragged = true;
        if (p.inventory.currentWeapon?.reloading) p.inventory.currentWeapon.cancelReload();
        this.ev({ type: EvType.BodyDrag, pos: c.body!.pos, actorId: c.body!.id });
        break;
      case CandidateKind.WeaponPickup: this.takePickup(c.pickup!); break;
      case CandidateKind.Door: this.useDoor(c.door!); break;
      case CandidateKind.Window: {
        const dest = c.cell!.add(c.dir!).center;
        p.pos = dest; p.actionLock = 0.55; p.actionLabel = 'Climbing';
        this.ev({ type: EvType.Vault, pos: c.cell!.center, pos2: dest, actorId: 0 });
        this.emitNoise(c.cell!.center, p.crouched ? 2.5 : 3.5, NoiseKind.Door, p);
        break;
      }
      case CandidateKind.Interactable: this.useInteractable(c.interactable!); break;
    }
  }

  private leaveHiding() {
    const p = this.player;
    p.hidden = false;
    p.pos = this.world.resolveCircle(p.hideExitPos, p.radius, MoverKind.PlayerCrouched);
    this.ev({ type: EvType.Unhide, pos: p.hiddenIn.center, actorId: 0 });
    for (const n of this.npcs)
      if (!n.down && this.canSee(n, p.pos, p.crouched) > 0 && Vec2.distance(n.pos, p.pos) < 4) n.suspicion = Math.min(1, n.suspicion + 0.6);
  }

  checkCloset(n: Npc, cell: Int2): boolean {
    const p = this.player;
    if (p.hidden && p.hiddenIn.eq(cell)) { this.leaveHiding(); this.spotPlayer(n); this.bark(n, 'found'); return true; }
    const bodyId = this.closetBodies.get(cell.key);
    if (bodyId !== undefined) {
      const body = this.bodies.find(b => b.id === bodyId);
      if (body && !body.discovered) { body.hidden = false; this.closetBodies.delete(cell.key); this.onBodySeen(n, body); }
    }
    return false;
  }

  private useDoor(d: DoorState) {
    const p = this.player;
    if (d.open) { this.closeDoor(d); return; }
    if (d.type === DoorType.Secret) {
      d.locked = false;
      this.openDoor(d, p, false);
      this.ev({ type: EvType.SecretFound, pos: d.cell.center, text: 'A hidden passage!' });
      this.message('A hidden passage!');
      return;
    }
    if (d.locked) {
      if (!p.inventory.keys.has(d.keyId!)) { this.emit(EvType.DoorLocked, d.cell.center); return; }
      d.locked = false;
      this.ev({ type: EvType.DoorUnlock, pos: d.cell.center, text: d.keyId });
    }
    this.openDoor(d, p, false);
  }

  private useInteractable(it: Interactable) {
    const p = this.player;
    switch (it.kind) {
      case InteractKind.PowerBox:
        this.setLightGroup(it, !it.active);
        this.emitNoise(it.pos, 3, NoiseKind.Door, p);
        if (it.active) this.assignFixer(it);
        break;
      case InteractKind.CameraTerminal:
        it.used = true;
        for (const n of this.npcs) if (n.isCamera && n.group === it.group && n.alive) { n.state = AIState.Disabled; n.suspicion = 0; }
        this.ev({ type: EvType.CameraDisabled, pos: it.pos, text: it.group });
        this.message('Security cameras disabled');
        break;
      case InteractKind.DownloadTerminal:
        it.used = true;
        this.ev({ type: EvType.Download, pos: it.pos, text: it.key });
        this.message('Download complete');
        break;
      case InteractKind.Distraction:
        it.active = true; it.timer = 10;
        this.ev({ type: EvType.Distraction, pos: it.pos, text: it.sound, flag: true, actorId: it.id });
        this.emitNoise(it.pos, 7.5, NoiseKind.Distraction, null, it);
        break;
      case InteractKind.Stairs: {
        const from = p.pos;
        p.pos = this.world.resolveCircle(it.pos2, p.radius, MoverKind.Player);
        if (p.draggingBody >= 0) { const body = this.bodies.find(b => b.id === p.draggingBody); if (body) body.pos = p.pos; }
        p.actionLock = 0.3;
        this.ev({ type: EvType.Teleport, pos: from, pos2: p.pos, text: it.label, actorId: 0 });
        break;
      }
    }
  }

  setLightGroup(box: Interactable, off: boolean) {
    box.active = off;
    for (const l of this.lights) if (l.group === box.group) l.on = !off;
    bakeLights(this.world, this.lights, 1, null);
    this.lightsDirty = true;
    this.ev({ type: EvType.Lights, pos: box.pos, text: box.group, flag: off });
    if (off) this.message('Power cut - the area goes dark');
  }

  private assignFixer(box: Interactable) {
    let best: Npc | null = null, bestD = 22;
    for (const n of this.npcs) {
      if (n.down || !n.isHostile || n.isTarget || n.state === AIState.Attack || n.state === AIState.Chase || n.state === AIState.Follow) continue;
      const d = Vec2.distance(n.pos, box.pos);
      if (d < bestD) { bestD = d; best = n; }
    }
    if (!best) return;
    best.investigateObject = box; best.investigatePos = box.pos; best.investigateIsLoud = false;
    this.setState(best, AIState.Investigate);
    this.bark(best, 'lights');
  }

  private updateInteractables(dt: number) {
    for (const it of this.interactables) {
      if (it.kind !== InteractKind.Distraction || !it.active) continue;
      it.timer -= dt;
      if (it.timer <= 0) { this.stopDistraction(it); continue; }
      if (Math.floor(it.timer / 2.5) !== Math.floor((it.timer + dt) / 2.5)) this.emitNoise(it.pos, 7.5, NoiseKind.Distraction, null, it);
    }
  }

  stopDistraction(it: Interactable) {
    if (!it.active) return;
    it.active = false;
    this.ev({ type: EvType.Distraction, pos: it.pos, text: it.sound, flag: false, actorId: it.id });
  }

  // ================================================================== perception
  canSee(n: Npc, pos: Vec2, crouched: boolean): number {
    if (n.down || n.state === AIState.Disabled) return 0;
    const to = pos.sub(n.pos), d = to.length;
    const alerted = isAlertState(n.state) || this.alert >= AlertLevel.Alarmed;
    const light = this.world.lightAtPos(pos);
    let range = n.viewRange * (0.3 + 0.7 * Math.min(1, light * 1.15));
    if (crouched) range *= 0.85;
    const tile = this.world.tileAt(Math.floor(pos.x), Math.floor(pos.y));
    if (tileIs(tile, TF.Concealment)) range *= crouched ? 0.35 : 0.6;
    if (alerted) range *= 1.2;
    if (n.isCamera) range = n.viewRange * (0.5 + 0.5 * Math.min(1, light * 1.2));
    const peripheral = !n.isCamera && d < 1.3 && !(crouched && !this.player.moving);
    if (d > range && !peripheral) return 0;
    const fov = n.viewAngle * (alerted && !n.isCamera ? 1.45 : 1);
    if (Math.abs(angleDelta(n.facing, to.angle)) > fov * 0.5 && !peripheral) return 0;
    if (!this.world.hasLineOfSight(n.pos, pos, crouched)) return 0;
    return clamp(1.4 - d / Math.max(range, 0.5), 0.25, 1.4);
  }

  private get playerTargetable() { const p = this.player; return p.alive && !p.hidden && !p.inVent && !p.ghost; }

  private perceive(n: Npc, dt: number) {
    const p = this.player;
    const v = this.playerTargetable ? this.canSee(n, p.pos, p.crouched) : 0;
    if (v > 0) {
      n.seesPlayer = true; n.seeTime += dt; n.lastKnownPlayer = p.pos; n.lastSeenAgo = 0;
      let stateMult = n.state === AIState.Attack || n.state === AIState.Chase ? 4
        : n.state === AIState.Search || n.state === AIState.Investigate ? 1.6 : n.state === AIState.Suspicious ? 1.25 : 1;
      if (this.alert >= AlertLevel.Alarmed) stateMult *= 1.3;
      const moveMult = p.sprinting ? 1.35 : p.moving ? 1 : 0.55;
      let kindMult = n.isCivilian ? 0.8 : n.isCamera ? 1.25 : 1;
      if (p.draggingBody >= 0) kindMult *= 1.8;
      const gain = 1.05 * v * stateMult * moveMult * kindMult * this.diff.detection * n.awareness;
      n.suspicion = Math.min(1, n.suspicion + gain * dt);
      n.suspicionDecayDelay = 2.5;
      if (n.suspicion >= 1 && n.state !== AIState.Attack && n.state !== AIState.Chase && n.state !== AIState.Panic && n.state !== AIState.Flee) this.spotPlayer(n);
    } else {
      n.seesPlayer = false; n.seeTime = 0; n.lastSeenAgo += dt; n.suspicionDecayDelay -= dt;
      if (n.suspicionDecayDelay <= 0 && (isCalmState(n.state) || n.isCamera)) n.suspicion = Math.max(0, n.suspicion - 0.14 * dt);
    }
    if (n.isCamera || n.state === AIState.Attack || n.state === AIState.Chase || n.state === AIState.Panic || n.state === AIState.Flee) return;
    for (const b of this.bodies) {
      if (b.discovered || b.hidden || b.npc === n) continue;
      if (n.investigateBody === b.id) continue;
      if (Vec2.sqrDistance(b.pos, n.pos) > n.viewRange * n.viewRange) continue;
      if (this.canSee(n, b.pos, false) <= 0) continue;
      this.onBodySeen(n, b);
      break;
    }
  }

  spotPlayer(n: Npc) {
    if (n.down) return;
    n.suspicion = 1; n.lastKnownPlayer = this.player.pos; n.lastSeenAgo = 0;
    if (!this.spotted) { this.spotted = true; this.ev({ type: EvType.Spotted, pos: n.pos, actorId: n.id }); }
    if (n.isCamera) { this.cameraAlarm(n); return; }
    if (n.isCivilian || (n.isTarget && !n.armed)) { this.panic(n, this.player.pos); return; }
    if (n.state !== AIState.Attack && n.state !== AIState.Chase) {
      this.bark(n, 'spotted', true);
      n.reactionTimer = this.diff.reaction * (n.kind === NpcKind.Elite ? 0.7 : 1);
      this.setState(n, AIState.Attack);
    }
    if (!n.hasRadioed && n.radioTimer < 0) {
      n.radioTimer = 1.6;
      this.ev({ type: EvType.Radio, pos: n.pos, actorId: n.id, flag: true, value: 1.6 });
    }
  }

  cancelRadio(n: Npc) {
    if (n.radioTimer > 0) this.ev({ type: EvType.Radio, pos: n.pos, actorId: n.id, flag: false });
    n.radioTimer = -1;
  }

  private completeRadio(caller: Npc) {
    caller.radioTimer = -1; caller.hasRadioed = true;
    this.bark(caller, 'radio', true);
    this.ev({ type: EvType.Radio, pos: caller.pos, actorId: caller.id, flag: false, text: 'alarm' });
    this.raiseAlarm(60);
    for (const n of this.npcs) {
      if (n === caller || n.down || n.isCamera) continue;
      if (Vec2.distance(n.pos, caller.pos) > 40) continue;
      n.lastKnownPlayer = caller.lastKnownPlayer;
      if (n.isHostile) {
        if (n.state !== AIState.Attack && n.state !== AIState.Chase) { n.lastSeenAgo = 1; this.setState(n, AIState.Chase); }
      } else if (n.isTarget) this.setState(n, AIState.Flee);
    }
  }

  private cameraAlarm(cam: Npc) {
    cam.cameraCooldown = 8;
    this.ev({ type: EvType.Radio, pos: cam.pos, actorId: cam.id, text: 'camera' });
    this.raiseAlarm(45);
    for (let k = 0; k < 3; k++) {
      let best: Npc | null = null, bestD = 45;
      for (const n of this.npcs) {
        if (n.down || !n.isHostile || n.isTarget || n.state === AIState.Attack || n.state === AIState.Chase) continue;
        if (n.investigateIsLoud && n.state === AIState.Investigate && Vec2.distance(n.investigatePos, this.player.pos) < 3) continue;
        const d = Vec2.distance(n.pos, this.player.pos);
        if (d < bestD) { bestD = d; best = n; }
      }
      if (!best) break;
      best.investigatePos = this.player.pos; best.investigateIsLoud = true; best.investigateObject = null;
      best.suspicion = Math.max(best.suspicion, 0.7);
      this.setState(best, AIState.Investigate);
      if (k === 0) this.bark(best, 'camera', true);
    }
  }

  onBodySeen(n: Npc, b: Body | null) {
    if (!b || b.discovered || n.down) return;
    if (n.isHostile && !n.isTarget) {
      if (n.state === AIState.Attack || n.state === AIState.Chase) return;
      n.investigateBody = b.id; n.investigatePos = b.pos; n.investigateIsLoud = true; n.investigateObject = null;
      this.bark(n, 'body_seen', true);
      this.setState(n, AIState.Investigate);
    } else {
      this.discoverBody(n, b);
      this.panic(n, b.pos);
    }
  }

  private discoverBody(n: Npc, b: Body) {
    if (b.discovered) return;
    b.discovered = true;
    this.bodiesFound++;
    this.ev({ type: EvType.BodyFound, pos: b.pos, actorId: n.id });
    this.bark(n, 'body', true);
    this.raiseAlarm(70);
    if (!n.isHostile) return;
    for (const o of this.npcs) {
      if (o === n || o.down || !o.isHostile || o.isTarget) continue;
      if (o.state === AIState.Attack || o.state === AIState.Chase || o.state === AIState.Follow) continue;
      if (Vec2.distance(o.pos, b.pos) > 16) continue;
      o.investigatePos = b.pos; o.investigateIsLoud = true; o.investigateObject = null;
      this.setState(o, AIState.Investigate);
    }
  }

  panic(n: Npc, threat: Vec2) {
    if (n.down || n.state === AIState.Panic || n.state === AIState.Flee || n.state === AIState.Cower) return;
    n.lastKnownPlayer = threat;
    if (n.isTarget && !n.armed) {
      this.bark(n, 'flee', true);
      this.setState(n, AIState.Flee);
      this.message(`${n.displayName} is trying to escape!`);
      return;
    }
    let best: Npc | null = null, bestD = 30;
    for (const g of this.npcs) {
      if (g.down || !g.isHostile || g.isTarget) continue;
      const d = Vec2.distance(g.pos, n.pos);
      if (d < bestD) { bestD = d; best = g; }
    }
    this.bark(n, 'panic', true);
    if (!best) { this.setState(n, AIState.Cower); return; }
    n.reportTo = best;
    this.setState(n, AIState.Panic);
  }

  emitNoise(pos: Vec2, radius: number, kind: NoiseKind, source: Actor | null, obj: Interactable | null = null) {
    if (radius <= 0) return;
    this.ev({ type: EvType.Noise, pos, value: radius, text: NoiseKind[kind], actorId: source?.id ?? -1 });
    for (const n of this.npcs) {
      if (n.down || n.isCamera || n === source) continue;
      const d = Vec2.distance(n.pos, pos);
      if (d > radius) continue;
      if (d > radius * 0.6 && !this.world.hasLineOfSight(n.pos, pos)) continue;
      this.hearNoise(n, pos, kind, source, obj);
    }
  }

  private hearNoise(n: Npc, pos: Vec2, kind: NoiseKind, source: Actor | null, obj: Interactable | null) {
    const loud = kind === NoiseKind.Gunshot || kind === NoiseKind.Explosion || kind === NoiseKind.GlassBreak || kind === NoiseKind.Shout;
    if (!n.isHostile) {
      if (loud && (source === this.player || kind === NoiseKind.Explosion)) {
        if (n.isTarget) this.panic(n, pos);
        else if (n.state !== AIState.Panic && n.state !== AIState.Cower) { this.bark(n, 'panic'); n.lastKnownPlayer = pos; this.setState(n, AIState.Cower); }
      } else if (isCalmState(n.state) && n.state !== AIState.Suspicious && kind !== NoiseKind.Footstep) {
        n.desiredFacing = pos.sub(n.pos).angle; n.lastKnownPlayer = pos;
      }
      return;
    }
    if (n.state === AIState.Attack || n.state === AIState.Chase) { if (loud && source === this.player) n.lastKnownPlayer = pos; return; }
    if (loud) {
      n.investigatePos = source instanceof Npc && source.isHostile ? source.lastKnownPlayer : pos;
      n.investigateIsLoud = true; n.investigateObject = null;
      n.suspicion = Math.max(n.suspicion, 0.7);
      if (n.state !== AIState.Investigate || !n.investigateIsLoud) this.bark(n, 'gunshot');
      if (n.state !== AIState.Follow) this.setState(n, AIState.Investigate); else n.desiredFacing = pos.sub(n.pos).angle;
      this.raiseAlarm(40);
      return;
    }
    if (n.state === AIState.Search || n.state === AIState.Fixing) return;
    if (n.state === AIState.Investigate && Vec2.distance(n.investigatePos, pos) < 3) return;
    if (kind === NoiseKind.Footstep) {
      n.suspicion = Math.min(0.75, n.suspicion + 0.3); n.lastKnownPlayer = pos; n.suspicionDecayDelay = 2;
      if (n.state === AIState.Follow) { n.desiredFacing = pos.sub(n.pos).angle; return; }
      if (n.state !== AIState.Investigate) this.setState(n, AIState.Suspicious);
      return;
    }
    if (n.state === AIState.Follow) { n.desiredFacing = pos.sub(n.pos).angle; return; }
    for (const o of this.npcs)
      if (o !== n && !o.down && o.state === AIState.Investigate && Vec2.distance(o.investigatePos, pos) < 3) {
        n.desiredFacing = pos.sub(n.pos).angle;
        if (n.state === AIState.Idle || n.state === AIState.Patrol) this.setState(n, AIState.Suspicious);
        n.lastKnownPlayer = pos;
        return;
      }
    n.investigatePos = pos;
    n.investigateIsLoud = kind === NoiseKind.SuppressedShot || kind === NoiseKind.Takedown;
    n.investigateObject = obj;
    n.suspicion = Math.max(n.suspicion, 0.4);
    this.bark(n, kind === NoiseKind.Distraction && obj ? 'noise' : 'suspicious');
    this.setState(n, AIState.Investigate);
  }

  // ================================================================== AI
  private updateNpcs(dt: number) {
    for (const n of this.npcs) {
      if (n.down) continue;
      n.stateTime += dt;
      n.barkCooldown -= dt;
      if (n.isCamera) { this.updateCamera(n, dt); continue; }
      if (n.weapon) {
        n.weapon.tick(dt);
        if (n.weapon.reloading) {
          n.weapon.tickReload(dt, 999);
          if (!n.weapon.reloading) this.emit(EvType.ReloadDone, n.pos, null, 0, n.id, n.weapon.def.sound);
        }
      }
      n.perceptionTimer -= dt;
      if (n.perceptionTimer <= 0) { n.perceptionTimer += PERCEPTION; this.perceive(n, PERCEPTION); }
      if (n.radioTimer > 0) { n.radioTimer -= dt; if (n.radioTimer <= 0) this.completeRadio(n); }
      n.speed = 0;
      if (n.isHostile) this.thinkHostile(n, dt); else this.thinkCivilian(n, dt);
      if (!isNaN(n.desiredFacing)) {
        n.facing = rotateTowards(n.facing, n.desiredFacing, n.turnSpeed * dt);
        if (Math.abs(angleDelta(n.facing, n.desiredFacing)) < 0.01 && isCalmState(n.state) && n.state !== AIState.Suspicious) n.desiredFacing = NaN;
      }
    }
    this.separate();
  }

  setState(n: Npc, s: AIState) {
    if (n.down) return;
    if (n.state === s && s !== AIState.Investigate) return;
    const prev = n.state;
    n.state = s; n.stateTime = 0; n.path = null; n.waitTimer = 0; n.lookTimer = 0;
    switch (s) {
      case AIState.Suspicious:
        n.desiredFacing = n.lastKnownPlayer.sub(n.pos).angle;
        if (prev !== AIState.Suspicious) this.bark(n, 'suspicious');
        break;
      case AIState.Search: n.searchTimer = 14 + this.rng.range(0, 6); n.searchMoving = false; this.bark(n, 'search'); break;
      case AIState.ReturnToPatrol: n.investigateBody = -1; n.investigateObject = null; n.investigateIsLoud = false; break;
      case AIState.Attack: if (prev !== AIState.Chase) n.reactionTimer = Math.max(n.reactionTimer, this.diff.reaction); break;
    }
  }

  private thinkHostile(n: Npc, dt: number) {
    const patrolSpeed = 1.9;
    switch (n.state) {
      case AIState.Idle:
        if (Vec2.distance(n.pos, n.homePos) > 0.6) { this.setState(n, AIState.ReturnToPatrol); break; }
        this.lookAround(n, dt, n.lookAngles ?? (n.lookAngles = postGlances(n.homeFacing)), 3.5);
        break;
      case AIState.Patrol: this.followRoute(n, dt, patrolSpeed); break;
      case AIState.Follow: this.followPrincipal(n, dt); break;
      case AIState.Suspicious:
        n.desiredFacing = n.lastKnownPlayer.sub(n.pos).angle;
        if (n.suspicion >= 0.6) {
          n.investigatePos = n.lastKnownPlayer; n.investigateIsLoud = false; n.investigateObject = null;
          this.bark(n, 'investigate');
          this.setState(n, AIState.Investigate);
        } else if ((n.stateTime > 4 && n.suspicion < 0.35) || n.suspicion < 0.08) {
          this.bark(n, 'calm');
          this.setState(n, n.followTarget ? AIState.Follow : AIState.ReturnToPatrol);
        }
        break;
      case AIState.Investigate: this.investigate(n, dt); break;
      case AIState.Fixing:
        n.desiredFacing = n.investigatePos.sub(n.pos).angle;
        if (n.stateTime > 2.5) {
          const it = n.investigateObject;
          if (it) {
            if (it.kind === InteractKind.Distraction) this.stopDistraction(it);
            else if (it.kind === InteractKind.PowerBox && it.active) this.setLightGroup(it, false);
          }
          this.bark(n, 'calm');
          this.setState(n, AIState.ReturnToPatrol);
        }
        break;
      case AIState.Search: this.search(n, dt); break;
      case AIState.Chase: {
        if (n.seesPlayer && n.suspicion >= 0.9) { this.setState(n, AIState.Attack); break; }
        const speed = n.kind === NpcKind.Elite ? 4.5 : 4.1;
        const arrived = this.moveTo(n, n.lastKnownPlayer, speed, dt, 0.6);
        if (arrived || n.lastSeenAgo > 12) { this.bark(n, 'lost'); n.investigatePos = n.lastKnownPlayer; this.setState(n, AIState.Search); }
        break;
      }
      case AIState.Attack: this.attack(n, dt); break;
      case AIState.ReturnToPatrol: {
        if (n.followTarget && !n.followTarget.down) { this.setState(n, AIState.Follow); break; }
        const goal = n.route.length > 0 ? n.route[n.routeIndex].pos : n.homePos;
        if (this.moveTo(n, goal, patrolSpeed, dt)) {
          if (n.route.length > 0) this.setState(n, AIState.Patrol);
          else { n.desiredFacing = n.homeFacing; this.setState(n, AIState.Idle); }
          n.suspicion = Math.min(n.suspicion, 0.2);
        }
        break;
      }
      default: this.setState(n, AIState.ReturnToPatrol);
    }
  }

  private attack(n: Npc, dt: number) {
    if (!this.playerTargetable) { n.lastSeenAgo = Math.max(n.lastSeenAgo, 1); this.setState(n, AIState.Chase); return; }
    const p = this.player;
    const to = p.pos.sub(n.pos), dist = to.length;
    n.desiredFacing = to.angle;
    n.facing = rotateTowards(n.facing, to.angle, 9 * dt);
    if (!n.seesPlayer) { if (n.lastSeenAgo > 0.8) this.setState(n, AIState.Chase); return; }
    if (!n.weapon) { this.setState(n, AIState.Chase); return; }
    const range = n.weapon.def.range;
    if (dist > range * 0.85) { this.moveTo(n, p.pos, 3.0, dt, 0.6); return; }
    if (n.kind === NpcKind.Elite && dist > 2.5) {
      const side = to.normalized.perp.mul(Math.floor(this.time / 1.7 + n.id) % 2 === 0 ? 1 : -1);
      n.pos = this.world.moveCircle(n.pos, n.radius, side.mul(1.6 * dt), MoverKind.Npc);
      n.speed = 1.6; n.moveAnim += 1.6 * dt;
    }
    n.reactionTimer -= dt;
    if (n.reactionTimer > 0) return;
    if (Math.abs(angleDelta(n.facing, to.angle)) > 0.25) return;
    if (n.weapon.def.automatic) { n.burstTimer += dt; if (n.burstTimer % 1.3 > 0.55) return; }
    this.npcFire(n);
  }

  private investigate(n: Npc, dt: number) {
    const speed = n.investigateIsLoud ? 3.3 : 2.3;
    const arrived = this.moveTo(n, n.investigatePos, speed, dt, n.investigateObject ? 1.0 : 0.7);
    if (!arrived) return;
    if (n.investigateBody >= 0) {
      const b = this.bodies.find(x => x.id === n.investigateBody);
      n.investigateBody = -1;
      if (b && !b.discovered && !b.hidden) { this.discoverBody(n, b); n.investigatePos = b.pos; this.setState(n, AIState.Search); return; }
    }
    if (n.investigateObject && n.investigateObject.active) { this.setState(n, AIState.Fixing); return; }
    if (n.suspicion > 0.55) this.checkNearbyClosets(n, n.investigatePos, 1.6);
    if (n.waitTimer <= 0) n.waitTimer = 3.2;
    this.lookAround(n, dt, null, 1.1);
    n.waitTimer -= dt;
    if (n.waitTimer <= 0.05) {
      if (n.investigateIsLoud || n.suspicion > 0.6 || this.alert >= AlertLevel.Alarmed) this.setState(n, AIState.Search);
      else { this.bark(n, 'calm'); this.setState(n, AIState.ReturnToPatrol); }
    }
  }

  private checkNearbyClosets(n: Npc, around: Vec2, radius: number) {
    const c = Int2.fromWorld(around), r = Math.ceil(radius);
    for (let y = c.y - r; y <= c.y + r; y++)
      for (let x = c.x - r; x <= c.x + r; x++) {
        if (!tileIs(this.world.tileAt(x, y), TF.HidingSpot)) continue;
        const cell = new Int2(x, y);
        if (Vec2.distance(cell.center, n.pos) > 1.6) continue;
        if (this.checkCloset(n, cell)) return;
      }
  }

  private search(n: Npc, dt: number) {
    n.searchTimer -= dt;
    if (n.searchTimer <= 0) {
      this.bark(n, 'calm');
      n.suspicion = 0.2; n.awareness = Math.min(1.6, n.awareness + 0.1);
      this.setState(n, AIState.ReturnToPatrol);
      return;
    }
    if (!n.searchMoving) {
      n.waitTimer -= dt;
      this.lookAround(n, dt, null, 0.9);
      if (n.waitTimer > 0) return;
      for (let a = 0; a < 8; a++) {
        const p = n.investigatePos.add(new Vec2(this.rng.range(-6, 6), this.rng.range(-6, 6)));
        const cell = Int2.fromWorld(p);
        if (!this.world.isWalkableForNpc(cell.x, cell.y)) continue;
        n.searchPoint = cell.center; n.searchMoving = true; n.path = null;
        break;
      }
      if (!n.searchMoving) n.waitTimer = 1;
      return;
    }
    if (this.moveTo(n, n.searchPoint, 2.7, dt)) {
      n.searchMoving = false;
      n.waitTimer = 1.4 + this.rng.range(0, 1);
      if (this.rng.chance(0.5)) this.checkNearbyClosets(n, n.pos, 1.5);
    }
  }

  private followRoute(n: Npc, dt: number, speed: number) {
    if (n.route.length === 0) { this.setState(n, AIState.Idle); return; }
    const wp = n.route[n.routeIndex];
    if (n.waitTimer > 0) {
      n.waitTimer -= dt;
      n.activity = wp.activity;
      if (!isNaN(wp.lookAngle)) n.desiredFacing = wp.lookAngle; else this.lookAround(n, dt, null, 2.2);
      if (n.waitTimer <= 0) n.routeIndex = (n.routeIndex + 1) % n.route.length;
      return;
    }
    if (this.moveTo(n, wp.pos, speed, dt)) { n.waitTimer = wp.wait > 0 ? wp.wait : 0.01; n.path = null; }
  }

  private followPrincipal(n: Npc, dt: number) {
    const t = n.followTarget;
    if (!t || t.down) {
      n.followTarget = null;
      n.investigatePos = t?.pos ?? n.pos; n.investigateIsLoud = true;
      this.raiseAlarm(60);
      this.setState(n, AIState.Search);
      return;
    }
    let spot = t.pos.sub(Vec2.fromAngle(t.facing).mul(1.3));
    const cell = Int2.fromWorld(spot);
    if (!this.world.isWalkableForNpc(cell.x, cell.y)) spot = t.pos;
    const dist = Vec2.distance(n.pos, spot);
    if (dist > 0.8) {
      const speed = t.state === AIState.Flee ? 4.2 : dist > 4 ? 3.2 : 2.1;
      if (n.repathTimer <= 0 || !n.path) { n.path = null; n.repathTimer = 0.7; }
      n.repathTimer -= dt;
      this.moveTo(n, spot, speed, dt, 0.5);
    } else n.desiredFacing = t.speed > 0.1 ? t.facing : t.pos.sub(n.pos).angle + 1.2 * Math.sin(this.time * 0.5 + n.id);
  }

  private lookAround(n: Npc, dt: number, angles: number[] | null, interval: number) {
    n.lookTimer -= dt;
    if (n.lookTimer > 0) return;
    n.lookTimer = interval + this.rng.range(0, interval * 0.5);
    if (angles && angles.length > 0) { n.lookIndex = (n.lookIndex + 1) % angles.length; n.desiredFacing = angles[n.lookIndex]; }
    else n.desiredFacing = n.facing + this.rng.range(-1.6, 1.6);
  }

  private thinkCivilian(n: Npc, dt: number) {
    switch (n.state) {
      case AIState.Idle:
        this.lookAround(n, dt, n.lookAngles ?? (n.lookAngles = postGlances(n.homeFacing)), 4);
        if (n.isTarget && this.alert === AlertLevel.Combat) this.panic(n, n.lastKnownPlayer);
        break;
      case AIState.Patrol:
        this.followRoute(n, dt, n.isTarget ? 1.6 : 1.5);
        if (n.isTarget && this.alert === AlertLevel.Combat) this.panic(n, n.lastKnownPlayer);
        break;
      case AIState.Suspicious:
        n.desiredFacing = n.lastKnownPlayer.sub(n.pos).angle;
        if (n.stateTime > 3 && n.suspicion < 0.3) this.setState(n, AIState.ReturnToPatrol);
        break;
      case AIState.Panic: {
        const g = n.reportTo;
        if (!g || g.down) { this.setState(n, AIState.Cower); break; }
        if (n.repathTimer <= 0) { n.path = null; n.repathTimer = 1; }
        n.repathTimer -= dt;
        if (this.moveTo(n, g.pos, 4.4, dt, 1.6) || Vec2.distance(n.pos, g.pos) < 1.8) {
          this.bark(n, 'report', true);
          if (g.state !== AIState.Attack && g.state !== AIState.Chase) {
            g.investigatePos = n.lastKnownPlayer; g.investigateIsLoud = true; g.investigateObject = null;
            g.suspicion = Math.max(g.suspicion, 0.75);
            this.setState(g, AIState.Investigate);
          }
          this.raiseAlarm(45);
          this.setState(n, AIState.Cower);
        }
        break;
      }
      case AIState.Cower:
        if (n.stateTime > 25 && this.alert < AlertLevel.Combat) { n.suspicion = 0; this.setState(n, AIState.ReturnToPatrol); }
        break;
      case AIState.Flee:
        if (this.moveTo(n, n.escapePos, 4.2, dt, 0.6)) n.escaped = true;
        break;
      case AIState.ReturnToPatrol: {
        const goal = n.route.length > 0 ? n.route[n.routeIndex].pos : n.homePos;
        if (this.moveTo(n, goal, 1.6, dt)) this.setState(n, n.route.length > 0 ? AIState.Patrol : AIState.Idle);
        break;
      }
      default: this.setState(n, AIState.ReturnToPatrol);
    }
  }

  private updateCamera(n: Npc, dt: number) {
    if (n.state === AIState.Disabled) { n.seesPlayer = false; return; }
    n.cameraCooldown -= dt;
    n.perceptionTimer -= dt;
    if (n.perceptionTimer <= 0) {
      n.perceptionTimer += PERCEPTION;
      if (n.cameraCooldown <= 0) this.perceive(n, PERCEPTION);
      else { n.seesPlayer = false; n.suspicion = Math.max(0, n.suspicion - 0.1 * PERCEPTION); }
    }
    if (n.seesPlayer && n.suspicion > 0.15) {
      const target = this.player.pos.sub(n.pos).angle;
      const rel = clamp(angleDelta(n.sweepCenter, target), -n.sweepHalf, n.sweepHalf);
      n.facing = rotateTowards(n.facing, n.sweepCenter + rel, 2 * dt);
    } else {
      n.sweepPhase += dt * 0.55;
      n.facing = n.sweepCenter + Math.sin(n.sweepPhase) * n.sweepHalf;
    }
  }

  private moveTo(n: Npc, goal: Vec2, speed: number, dt: number, tolerance = 0.25): boolean {
    if (Vec2.distance(n.pos, goal) <= tolerance) { n.path = null; return true; }
    if (!n.path || Vec2.distance(n.pathGoal, goal) > 0.75) {
      n.path = this.paths.findPath(n.pos, goal, n.radius);
      n.pathIndex = 0; n.pathGoal = goal; n.stuckTimer = 0; n.stuckPos = n.pos;
      if (!n.path || n.path.length === 0) { n.path = null; return true; }
    }
    let node = n.path[n.pathIndex];
    if (node.teleport) {
      n.pos = node.pos;
      n.pathIndex++;
      if (n.pathIndex >= n.path.length) { n.path = null; return Vec2.distance(n.pos, goal) <= tolerance + 0.5; }
      node = n.path[n.pathIndex];
    }
    const to = node.pos.sub(n.pos), dist = to.length;
    const last = n.pathIndex === n.path.length - 1;
    if (dist < (last ? Math.min(tolerance, 0.2) : 0.3)) {
      n.pathIndex++;
      if (n.pathIndex >= n.path.length) { n.path = null; return true; }
      return false;
    }
    const dir = to.div(dist);
    const probe = n.pos.add(dir.mul(n.radius + 0.35));
    const door = this.world.doorAt(Math.floor(probe.x), Math.floor(probe.y));
    if (door && !door.open && door.type !== DoorType.Secret) this.openDoor(door, n, true);
    const step = Math.min(speed * dt, dist);
    const before = n.pos;
    n.pos = this.world.moveCircle(n.pos, n.radius, dir.mul(step), MoverKind.Npc);
    const moved = Vec2.distance(before, n.pos);
    n.moveAnim += moved;
    n.speed = moved / Math.max(dt, 1e-5);
    n.velocity = n.pos.sub(before).div(Math.max(dt, 1e-5));
    if (n.state !== AIState.Attack) n.facing = rotateTowards(n.facing, dir.angle, n.turnSpeed * dt);
    n.desiredFacing = NaN;
    n.stuckTimer += dt;
    if (n.stuckTimer > 1.2) {
      if (Vec2.distance(n.stuckPos, n.pos) < 0.2) { if (n.pathIndex < n.path.length - 1) n.pathIndex++; else n.path = null; }
      n.stuckTimer = 0; n.stuckPos = n.pos;
    }
    return false;
  }

  private separate() {
    const ns = this.npcs, p = this.player;
    for (let i = 0; i < ns.length; i++) {
      const a = ns[i];
      if (a.down || a.isCamera) continue;
      for (let j = i + 1; j < ns.length; j++) {
        const b = ns[j];
        if (b.down || b.isCamera) continue;
        this.push(a, b, 0.5);
      }
      if (p.alive && !p.hidden) this.push(a, p, 0.85);
    }
  }

  private push(a: Actor, b: Actor, aShare: number) {
    const d = a.pos.sub(b.pos), min = a.radius + b.radius, len2 = d.sqrLength;
    if (len2 >= min * min || len2 < 1e-8) return;
    const len = Math.sqrt(len2);
    const push = d.div(len).mul(min - len);
    a.pos = this.world.resolveCircle(a.pos.add(push.mul(aShare)), a.radius, MoverKind.Npc);
    const mb = b === this.player ? (this.player.crouched ? MoverKind.PlayerCrouched : MoverKind.Player) : MoverKind.Npc;
    b.pos = this.world.resolveCircle(b.pos.sub(push.mul(1 - aShare)), b.radius, mb);
  }

  bark(n: Npc, key: string, force = false) {
    if (n.down || (!force && n.barkCooldown > 0)) return;
    const lines = BARKS[key];
    if (!lines) return;
    n.barkCooldown = 3;
    const line = lines[this.rng.rangeInt(0, lines.length)];
    const voice = n.isCivilian || (n.isTarget && !n.armed) ? 'civ' : n.kind === NpcKind.Elite ? 'elite' : 'guard';
    this.ev({ type: EvType.Bark, pos: n.pos, text: line, actorId: n.id, sound: voice + '_' + key });
  }
}

function postGlances(home: number) { return [home, home + 0.9, home, home - 0.9]; }
function touches(p: Vec2, r: number, c: Int2) {
  const cx = clamp(p.x, c.x, c.x + 1), cy = clamp(p.y, c.y, c.y + 1);
  const dx = p.x - cx, dy = p.y - cy;
  return dx * dx + dy * dy < r * r;
}
function segmentCircle(a: Vec2, b: Vec2, c: Vec2, r: number) {
  const ab = b.sub(a), len2 = ab.sqrLength;
  const t = len2 < 1e-8 ? 0 : clamp01(Vec2.dot(c.sub(a), ab) / len2);
  return Vec2.sqrDistance(a.add(ab.mul(t)), c) <= r * r;
}
function distractionLabel(k: string) { return k === 'piano' ? 'Piano' : k === 'vending' ? 'Vending machine' : k === 'printer' ? 'Printer' : 'Radio'; }
function cap(s: string) { return s ? s[0].toUpperCase() + s.substring(1) : s; }
