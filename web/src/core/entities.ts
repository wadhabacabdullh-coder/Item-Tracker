// Port of Entities.cs + MissionDef.cs + SessionTypes.cs
import { Vec2, Int2, Rect } from './math';
import { Inventory, WeaponState, AmmoType } from './weapons';
import { Waypoint } from './map';
import { PathNode } from './world';

export class Actor {
  id = 0; pos = Vec2.Zero; velocity = Vec2.Zero; facing = 0; radius = 0.32;
  health = 100; maxHealth = 100; alive = true; moveAnim = 0;
  get forward() { return Vec2.fromAngle(this.facing); }
}

export class Player extends Actor {
  armor = 0; maxArmor = 0; crouched = false; sprinting = false; aiming = false; moving = false;
  hidden = false; hiddenIn = new Int2(0, 0); hideExitPos = Vec2.Zero; inVent = false; draggingBody = -1;
  inventory = new Inventory();
  actionLock = 0; actionLabel: string | null = null; footstepDist = 0; lastShotNoise = 0; damageFlash = 0;
  speedMult = 1; godMode = false; ghost = false;
}

export enum NpcKind { Guard, Elite, Civilian, Target, Camera }
export enum AIState {
  Idle, Patrol, Suspicious, Investigate, Search, Chase, Attack, ReturnToPatrol,
  Follow, Panic, Flee, Cower, Fixing, Dead, Unconscious, Disabled,
}

export class Npc extends Actor {
  key = ''; displayName = ''; kind = NpcKind.Guard; type = 'guard'; state = AIState.Idle; stateTime = 0;
  suspicion = 0; suspicionDecayDelay = 0; seesPlayer = false; seeTime = 0; lastKnownPlayer = Vec2.Zero; lastSeenAgo = 999;
  investigatePos = Vec2.Zero; investigateIsLoud = false; investigateBody = -1; investigateObject: Interactable | null = null;
  weapon: WeaponState | null = null; carriedKey: string | null = null;
  route: Waypoint[] = []; routeIndex = 0; waitTimer = 0; lookAngles: number[] | null = null; lookIndex = 0; lookTimer = 0;
  homePos = Vec2.Zero; homeFacing = 0;
  path: PathNode[] | null = null; pathIndex = 0; pathGoal = Vec2.Zero; repathTimer = 0;
  perceptionTimer = 0; reactionTimer = 0; burstTimer = 0; radioTimer = -1; hasRadioed = false; barkCooldown = 0;
  followKey: string | null = null; followTarget: Npc | null = null; escapePos = Vec2.Zero; escaped = false;
  activity: string | null = null; searchTimer = 0; searchPointsLeft = 0; awareness = 1; speed = 0; turnSpeed = 7;
  viewRange = 9; viewAngle = 100 * Math.PI / 180;
  sweepCenter = 0; sweepHalf = 0; sweepPhase = 0; group: string | null = null; unconscious = false;
  reportTo: Npc | null = null; desiredFacing = NaN; stuckTimer = 0; stuckPos = Vec2.Zero; searchPoint = Vec2.Zero; searchMoving = false; cameraCooldown = 0;
  get armed() { return this.weapon !== null; }
  get isTarget() { return this.kind === NpcKind.Target; }
  get isCivilian() { return this.kind === NpcKind.Civilian; }
  get isCamera() { return this.kind === NpcKind.Camera; }
  get isHostile() { return this.kind === NpcKind.Guard || this.kind === NpcKind.Elite || (this.kind === NpcKind.Target && this.armed); }
  get down() { return !this.alive || this.unconscious; }
}

export class Body {
  id = 0; npc!: Npc; pos = Vec2.Zero; facing = 0; discovered = false; hidden = false; looted = false; dragged = false; knivesInside = 0;
}

export enum PickupKind { Ammo, Medkit, Armor, Cash, Keycard, Intel, Weapon, Knives, Coins }
export class Pickup {
  id = 0; kind = PickupKind.Ammo; itemId = ''; name = ''; amount = 0; ammoType = AmmoType.None; pos = Vec2.Zero; taken = false;
  get requiresInteract() { return this.kind === PickupKind.Weapon; }
}

export enum ProjectileKind { Knife, Coin }
export class Projectile {
  id = 0; kind = ProjectileKind.Knife; pos = Vec2.Zero; vel = Vec2.Zero; angle = 0; life = 0; damage = 0; fromPlayer = true; done = false;
}

export enum InteractKind { PowerBox, CameraTerminal, DownloadTerminal, Distraction, Stairs }
export class Interactable {
  id = 0; kind = InteractKind.PowerBox; key: string | null = null; label = ''; group: string | null = null;
  pos = Vec2.Zero; pos2 = Vec2.Zero; lock: string | null = null; duration = 0; used = false; active = false; timer = 0; sound = ''; secret = false;
}

export interface Zone { name: string; rect: Rect; }
export interface ExtractZone { label: string; rect: Rect; }

// ------------------------------------------------------------------ missions

export enum ObjectiveType { Eliminate, Retrieve, Download, Reach, Extract }
export interface ObjectiveDef { id: string; type: ObjectiveType; text: string; targetIds?: string[]; itemId?: string; zoneName?: string; optional?: boolean; bonus?: number; }
export enum ChallengeType { SilentAssassin, NoCivilianCasualties, Professional, NoBodiesFound, Speed }
export interface ChallengeDef { type: ChallengeType; name: string; description: string; bonus: number; parTime: number; }
export interface MissionDef {
  id: string; mapId: string; name: string; location: string; briefing: string; reward: number; order: number; difficulty: number;
  parTime: number; objectives: ObjectiveDef[]; challenges: ChallengeDef[]; unlockedBy: string | null;
}

const O = ObjectiveType;
export const MISSIONS: MissionDef[] = [
  {
    id: 'mansion_host', mapId: 'mansion', order: 1, difficulty: 1, name: 'The Host', location: 'Villa Moretti, Lake Como', reward: 3500, parTime: 420, unlockedBy: null,
    briefing: 'Victor Moretti launders money for half the cartels in Europe and tonight he is throwing a party. He moves between his guests, his study and his bedroom, always shadowed by a bodyguard. Get in through the gardens, eliminate Moretti and get back to the car. Guests and staff are not the contract.',
    objectives: [
      { id: 'kill', type: O.Eliminate, text: 'Eliminate Victor Moretti', targetIds: ['moretti'] },
      { id: 'extract', type: O.Extract, text: 'Escape to the getaway car' },
    ], challenges: [],
  },
  {
    id: 'facility_voss', mapId: 'facility', order: 2, difficulty: 2, name: 'Patient Zero', location: 'Helix Labs, Rotterdam', reward: 5000, parTime: 540, unlockedBy: 'mansion_host',
    briefing: 'Dr. Helena Voss engineered a pathogen and is about to sell it. She works the biolab, the servers and visits the director. The checkpoint needs a blue keycard - or crawl the vents from the dock storage. Cameras can be shut down from security control. Eliminate Voss. Her samples would be a welcome bonus.',
    objectives: [
      { id: 'kill', type: O.Eliminate, text: 'Eliminate Dr. Helena Voss', targetIds: ['voss'] },
      { id: 'samples', type: O.Retrieve, text: 'Optional: Steal the pathogen samples', itemId: 'samples', optional: true, bonus: 1500 },
      { id: 'extract', type: O.Extract, text: 'Extract via the loading dock or freight elevator' },
    ], challenges: [],
  },
  {
    id: 'office_cfo', mapId: 'office', order: 3, difficulty: 2, name: 'Hostile Takeover', location: 'Kessler Tower, Frankfurt', reward: 6500, parTime: 540, unlockedBy: 'facility_voss',
    briefing: 'Marcus Kessler cooks the books for a private army. He works from the executive floor. The elevator needs a blue access card; the security desk guard carries one and another was left in the cafe. Find a way up, eliminate Kessler, and leave by the street or the rooftop helipad.',
    objectives: [
      { id: 'card', type: O.Retrieve, text: 'Find a blue access card', itemId: 'keycard_blue', optional: true, bonus: 500 },
      { id: 'floor', type: O.Reach, text: 'Get onto the executive floor', zoneName: 'Open Office' },
      { id: 'kill', type: O.Eliminate, text: 'Eliminate Marcus Kessler', targetIds: ['kessler'] },
      { id: 'extract', type: O.Extract, text: 'Escape via the street or the helipad' },
    ], challenges: [],
  },
  {
    id: 'mansion_ledger', mapId: 'mansion', order: 4, difficulty: 3, name: 'Family Secrets', location: 'Villa Moretti, Lake Como', reward: 7500, parTime: 600, unlockedBy: 'office_cfo',
    briefing: "Moretti's accountant Paolo Greco keeps the family ledger in the cellar vault. The vault door needs the security chief's red keycard... or you could look for another way in. Rumour has it the old study hides more than books. Retrieve the ledger and silence Greco.",
    objectives: [
      { id: 'ledger', type: O.Retrieve, text: 'Retrieve the Moretti ledger from the vault', itemId: 'ledger' },
      { id: 'kill', type: O.Eliminate, text: 'Eliminate Paolo Greco', targetIds: ['accountant'] },
      { id: 'extract', type: O.Extract, text: 'Escape to the getaway car' },
    ], challenges: [],
  },
  {
    id: 'facility_prototype', mapId: 'facility', order: 5, difficulty: 3, name: 'Blackout', location: 'Helix Labs, Rotterdam', reward: 9000, parTime: 660, unlockedBy: 'mansion_ledger',
    briefing: 'Helix rebuilt. Security chief Kovac and Director Hale now guard the finished prototype in the containment lab behind a red door. Kovac carries a red keycard; the director keeps a spare in her office. Eliminate both and steal the prototype.',
    objectives: [
      { id: 'kill', type: O.Eliminate, text: 'Eliminate Kovac and Director Hale', targetIds: ['kovac', 'director'] },
      { id: 'proto', type: O.Retrieve, text: 'Steal the Helix prototype', itemId: 'prototype' },
      { id: 'extract', type: O.Extract, text: 'Extract via the loading dock or freight elevator' },
    ], challenges: [],
  },
  {
    id: 'office_breach', mapId: 'office', order: 6, difficulty: 3, name: 'Data Breach', location: 'Kessler Tower, Frankfurt', reward: 12000, parTime: 720, unlockedBy: 'office_cfo',
    briefing: "Kessler's successors are holding a board meeting. Download the mainframe's contents from the server room, then eliminate both executives - Dana Whitmore and Victor Hale. They will run for the exits the moment an alarm goes off.",
    objectives: [
      { id: 'data', type: O.Download, text: 'Download the mainframe data', itemId: 'mainframe' },
      { id: 'kill', type: O.Eliminate, text: 'Eliminate Whitmore and Hale', targetIds: ['exec1', 'exec2'] },
      { id: 'extract', type: O.Extract, text: 'Escape via the street or the helipad' },
    ], challenges: [],
  },
];
for (const m of MISSIONS) {
  m.challenges.push({ type: ChallengeType.SilentAssassin, name: 'Silent Assassin', description: 'Never be spotted.', bonus: Math.floor(m.reward / 2), parTime: 0 });
  m.challenges.push({ type: ChallengeType.NoCivilianCasualties, name: 'Clean Hands', description: 'No civilian casualties.', bonus: 800, parTime: 0 });
  m.challenges.push({ type: ChallengeType.Professional, name: 'Professional', description: 'Kill no one but the targets.', bonus: 1500, parTime: 0 });
  m.challenges.push({ type: ChallengeType.NoBodiesFound, name: 'Ghost', description: 'No bodies found.', bonus: 700, parTime: 0 });
  m.challenges.push({ type: ChallengeType.Speed, name: 'Swift', description: `Finish in under ${Math.floor(m.parTime / 60)} minutes.`, bonus: 600, parTime: m.parTime });
}
export const getMission = (id: string | null) => MISSIONS.find(m => m.id === id);

// ------------------------------------------------------------------ session types

export enum Difficulty { Easy, Normal, Hard }
export interface DifficultySettings { detection: number; enemyDamage: number; enemySpread: number; reaction: number; playerHealth: number; }
export function difficultyFor(d: Difficulty): DifficultySettings {
  if (d === Difficulty.Easy) return { detection: 0.7, enemyDamage: 0.55, enemySpread: 1.5, reaction: 0.75, playerHealth: 130 };
  if (d === Difficulty.Hard) return { detection: 1.35, enemyDamage: 1.35, enemySpread: 0.8, reaction: 0.3, playerHealth: 100 };
  return { detection: 1, enemyDamage: 1, enemySpread: 1, reaction: 0.45, playerHealth: 100 };
}

export class LoadoutConfig {
  slotWeapons: (string | null)[] = ['knife', 'pistol', null, null, 'coin'];
  upgrades = new Set<string>();
  ammo = new Map<AmmoType, number>();
  medkits = 0; armor = 0; speedMult = 1;
}

export class PlayerInput {
  move = Vec2.Zero; aim = Vec2.Zero;
  fireHeld = false; firePressed = false; altHeld = false; altPressed = false; reloadPressed = false;
  interactPressed = false; interactHeld = false; sprintHeld = false; crouchPressed = false; medkitPressed = false;
  selectSlot = -1; cycle = 0;
  clearEdges() {
    this.firePressed = this.altPressed = this.reloadPressed = this.interactPressed = this.crouchPressed = this.medkitPressed = false;
    this.selectSlot = -1; this.cycle = 0;
  }
}

export enum AlertLevel { Calm, Suspicious, Alarmed, Combat }
export enum MissionState { Playing, Complete, Failed, Dead }
export enum NoiseKind { Footstep, Door, Distraction, Takedown, BodyFall, SuppressedShot, Gunshot, GlassBreak, Explosion, Shout }
export enum EvType {
  Shot, Tracer, Impact, Hit, Death, Melee, Throw, ReloadStart, ReloadDone, DryFire, WeaponSwitch,
  DoorOpen, DoorClose, DoorLocked, DoorUnlock, WindowBreak, Vault, Explosion, Pickup, Bark, Footstep, Noise,
  Objective, Message, PlayerHurt, PlayerDied, MissionComplete, MissionFailed, Lights, Teleport, Casing,
  Hide, Unhide, Medkit, Alert, BodyFound, Takedown, Subdue, Spotted, CameraDisabled, Download, Distraction,
  KnifeStuck, CoinLand, Radio, BodyDrag, BodyDrop, BodyStash, SecretFound,
}

export interface GameEvent {
  type: EvType; pos: Vec2; pos2?: Vec2; angle?: number; value?: number; text?: string | null; sound?: string | null; actorId?: number; flag?: boolean;
}

export class MissionResult {
  missionId = ''; success = false; failReason: string | null = null; time = 0;
  kills = 0; targetsKilled = 0; civiliansKilled = 0; nonTargetKills = 0; subdued = 0; bodiesFound = 0; spotted = false;
  baseReward = 0; objectiveBonus = 0; challengeBonus = 0; cashFound = 0; penalty = 0; total = 0; rating = '';
  challengesCompleted: ChallengeDef[] = []; objectivesCompleted: string[] = [];
  ammoLeft = new Map<AmmoType, number>(); medkitsLeft = 0;
}
