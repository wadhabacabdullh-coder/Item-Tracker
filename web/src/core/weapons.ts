// Port of WeaponDef.cs + WeaponState.cs
import { Rng, DEG2RAD } from './math';

export enum WeaponClass { Melee, Throwing, Pistol, SMG, Rifle, Shotgun, Gadget }
export enum AmmoType { None, Pistol, Rifle, Shells, Knives, Coins }
export enum WeaponSlot { Melee = 0, Sidearm = 1, Primary = 2, Throwing = 3, Gadget = 4 }
export enum FireBlock { None, Cooldown, Reloading, Empty, NoWeapon }

export class WeaponDef {
  id = ''; name = ''; description = ''; cls = WeaponClass.Melee; ammo = AmmoType.None;
  damage = 0; pellets = 1; fireRate = 1; automatic = false; magSize = 0; reloadTime = 0; shellReload = false;
  spread = 0; moveSpread = 2; range = 0; recoil = 0; recoilRecovery = 20; noise = 0; suppressed = false;
  meleeArc = 100; meleeRange = 1.15; projectileSpeed = 0; moveSpeedMult = 1; shake = 0; canSuppress = false;
  price = 0; unlockAfter: string | null = null; sound = '';

  get slot(): WeaponSlot {
    switch (this.cls) {
      case WeaponClass.Melee: return WeaponSlot.Melee;
      case WeaponClass.Pistol: return WeaponSlot.Sidearm;
      case WeaponClass.Throwing: return WeaponSlot.Throwing;
      case WeaponClass.Gadget: return WeaponSlot.Gadget;
      default: return WeaponSlot.Primary;
    }
  }
  get isMelee() { return this.cls === WeaponClass.Melee; }
  get isThrown() { return this.cls === WeaponClass.Throwing || this.cls === WeaponClass.Gadget; }
  get isGun() { return !this.isMelee && !this.isThrown; }
  clone(): WeaponDef { return Object.assign(new WeaponDef(), this); }
}

function def(p: Partial<WeaponDef>): WeaponDef { return Object.assign(new WeaponDef(), p); }

export const WEAPONS = new Map<string, WeaponDef>();
for (const w of [
  def({ id: 'knife', name: 'Knife', cls: WeaponClass.Melee, damage: 55, fireRate: 2.2, meleeRange: 1.1, meleeArc: 100, noise: 1.5, sound: 'knife', description: 'A simple folding blade. Silent takedowns from behind.' }),
  def({ id: 'combat_knife', name: 'Combat Knife', cls: WeaponClass.Melee, damage: 80, fireRate: 2.8, meleeRange: 1.3, meleeArc: 115, noise: 1.2, sound: 'knife', price: 1200, description: 'Heavier, faster and longer reach. Two slashes drop most guards.' }),
  def({ id: 'throwing_knives', name: 'Throwing Knives', cls: WeaponClass.Throwing, ammo: AmmoType.Knives, damage: 100, fireRate: 1.6, magSize: 1, projectileSpeed: 17, range: 12, spread: 1.5, noise: 1.5, sound: 'throw', price: 1500, description: 'Silent at range. Lethal on unaware targets. Retrieve them from bodies.' }),
  def({ id: 'coin', name: 'Coins', cls: WeaponClass.Gadget, ammo: AmmoType.Coins, damage: 0, fireRate: 2, magSize: 1, projectileSpeed: 12, range: 9, spread: 3, noise: 6.5, sound: 'coin', description: 'Toss to make noise and lure guards away.' }),
  def({ id: 'pistol', name: 'Pistol', cls: WeaponClass.Pistol, ammo: AmmoType.Pistol, damage: 34, fireRate: 5, magSize: 12, reloadTime: 1.3, spread: 2.5, moveSpread: 3, range: 16, recoil: 2.5, noise: 16, shake: 0.12, canSuppress: true, sound: 'pistol', description: 'Reliable 9mm sidearm. Loud without a suppressor.' }),
  def({ id: 'silenced_pistol', name: 'Silverballer SD', cls: WeaponClass.Pistol, ammo: AmmoType.Pistol, damage: 38, fireRate: 4, magSize: 10, reloadTime: 1.4, spread: 1.8, moveSpread: 2.5, range: 15, recoil: 2, noise: 3.2, suppressed: true, shake: 0.06, sound: 'pistol_sd', price: 2600, description: "Integrally suppressed precision pistol. The assassin's choice." }),
  def({ id: 'smg', name: 'Vektor SMG', cls: WeaponClass.SMG, ammo: AmmoType.Pistol, damage: 21, fireRate: 12, automatic: true, magSize: 30, reloadTime: 1.8, spread: 4.5, moveSpread: 3, range: 12, recoil: 1.1, recoilRecovery: 16, noise: 17, shake: 0.08, canSuppress: true, moveSpeedMult: 0.96, sound: 'smg', price: 4200, unlockAfter: 'mansion_host', description: 'Compact bullet hose. Great up close, sprays at range.' }),
  def({ id: 'shotgun', name: 'Breacher 12G', cls: WeaponClass.Shotgun, ammo: AmmoType.Shells, damage: 15, pellets: 8, fireRate: 1.3, magSize: 6, reloadTime: 0.45, shellReload: true, spread: 11, moveSpread: 2, range: 8, recoil: 6, recoilRecovery: 18, noise: 22, shake: 0.35, moveSpeedMult: 0.92, sound: 'shotgun', price: 5200, unlockAfter: 'facility_voss', description: 'Pump-action. Devastating in corridors, useless at range.' }),
  def({ id: 'assault_rifle', name: 'KR-7 Rifle', cls: WeaponClass.Rifle, ammo: AmmoType.Rifle, damage: 33, fireRate: 9, automatic: true, magSize: 30, reloadTime: 2.2, spread: 2.2, moveSpread: 4, range: 22, recoil: 1.6, recoilRecovery: 14, noise: 22, shake: 0.14, canSuppress: true, moveSpeedMult: 0.9, sound: 'rifle', price: 7800, unlockAfter: 'office_cfo', description: 'Accurate, hard hitting and loud. For when stealth is over.' }),
]) WEAPONS.set(w.id, w);

export enum UpgradeKind { Suppressor, ExtendedMag, Stabilizer }
export interface UpgradeDef { id: string; weaponId: string; kind: UpgradeKind; name: string; description: string; price: number; unlockAfter: string | null; }
export const upgradeId = (w: string, k: UpgradeKind) => w + '.' + UpgradeKind[k].toLowerCase();

export const UPGRADES: UpgradeDef[] = [];
function addUp(weapon: string, k: UpgradeKind, price: number, unlock: string | null = null) {
  const w = WEAPONS.get(weapon)!;
  const [n, d] = k === UpgradeKind.Suppressor ? ['Suppressor', 'Gunshots are barely audible (-80% noise, -10% damage).']
    : k === UpgradeKind.ExtendedMag ? ['Extended Magazine', '+50% magazine capacity.'] : ['Stabilizer', '-40% recoil, -20% spread.'];
  UPGRADES.push({ id: upgradeId(weapon, k), weaponId: weapon, kind: k, name: `${w.name} ${n}`, description: d, price, unlockAfter: unlock ?? w.unlockAfter });
}
addUp('pistol', UpgradeKind.Suppressor, 1200); addUp('pistol', UpgradeKind.ExtendedMag, 700); addUp('pistol', UpgradeKind.Stabilizer, 600);
addUp('silenced_pistol', UpgradeKind.ExtendedMag, 900); addUp('silenced_pistol', UpgradeKind.Stabilizer, 800);
addUp('smg', UpgradeKind.Suppressor, 1800, 'mansion_host'); addUp('smg', UpgradeKind.ExtendedMag, 1200, 'mansion_host'); addUp('smg', UpgradeKind.Stabilizer, 1000, 'mansion_host');
addUp('shotgun', UpgradeKind.ExtendedMag, 1200, 'facility_voss'); addUp('shotgun', UpgradeKind.Stabilizer, 900, 'facility_voss');
addUp('assault_rifle', UpgradeKind.Suppressor, 2600, 'office_cfo'); addUp('assault_rifle', UpgradeKind.ExtendedMag, 1500, 'office_cfo'); addUp('assault_rifle', UpgradeKind.Stabilizer, 1400, 'office_cfo');

export const getUpgrade = (id: string) => UPGRADES.find(u => u.id === id);

export function applyUpgrades(base: WeaponDef, ups: Set<string> | string[] | null): WeaponDef {
  const d = base.clone();
  if (!ups) return d;
  const has = (id: string) => (Array.isArray(ups) ? ups.includes(id) : ups.has(id));
  if (has(upgradeId(d.id, UpgradeKind.Suppressor)) && d.canSuppress && !d.suppressed) {
    d.suppressed = true; d.noise *= 0.2; d.damage *= 0.9; d.shake *= 0.6; d.sound += '_sd';
  }
  if (has(upgradeId(d.id, UpgradeKind.ExtendedMag))) d.magSize = Math.round(d.magSize * 1.5);
  if (has(upgradeId(d.id, UpgradeKind.Stabilizer))) { d.recoil *= 0.6; d.spread *= 0.8; d.moveSpread *= 0.8; }
  return d;
}

export class WeaponState {
  mag: number; cooldown = 0; reloadTimer = 0; bloom = 0; triggerReleased = true;
  constructor(public readonly def: WeaponDef, mag = -1) { this.mag = mag < 0 ? def.magSize : mag; }
  get reloading() { return this.reloadTimer > 0; }
  tick(dt: number) {
    if (this.cooldown > 0) this.cooldown = Math.max(0, this.cooldown - dt);
    this.bloom = Math.max(0, this.bloom - this.def.recoilRecovery * dt);
  }
  canFire(pressedThisFrame: boolean): FireBlock {
    const d = this.def;
    if (this.reloading && !(d.shellReload && this.mag > 0)) return FireBlock.Reloading;
    if (this.cooldown > 0) return FireBlock.Cooldown;
    if (!d.automatic && !d.isMelee && !this.triggerReleased && !pressedThisFrame) return FireBlock.Cooldown;
    if (d.isGun && this.mag <= 0) return FireBlock.Empty;
    return FireBlock.None;
  }
  fire(rng: Rng, aim: number, moving: boolean, aiming: boolean, accuracyMult = 1): number[] {
    const d = this.def;
    const dirs: number[] = [];
    if (d.shellReload && this.reloading) this.reloadTimer = 0;
    this.cooldown = 1 / d.fireRate;
    this.triggerReleased = false;
    if (d.isGun) this.mag--;
    const spreadDeg = this.currentSpread(moving, aiming) * accuracyMult;
    for (let i = 0; i < d.pellets; i++) {
      const s = d.pellets > 1 ? rng.range(-1, 1) : rng.spread();
      dirs.push(aim + s * spreadDeg * DEG2RAD);
    }
    this.bloom = Math.min(this.bloom + d.recoil, d.recoil * 8 + 4);
    return dirs;
  }
  currentSpread(moving: boolean, aiming: boolean): number {
    let s = this.def.spread + this.bloom + (moving ? this.def.moveSpread : 0);
    if (aiming) s *= this.def.pellets > 1 ? 0.8 : 0.55;
    return s;
  }
  startReload(reserve: number): boolean {
    if (!this.def.isGun || this.reloading || this.mag >= this.def.magSize || reserve <= 0) return false;
    this.reloadTimer = this.def.reloadTime;
    return true;
  }
  tickReload(dt: number, reserve: number): number {
    if (!this.reloading) return 0;
    this.reloadTimer -= dt;
    if (this.reloadTimer > 0) return 0;
    if (this.def.shellReload) {
      const moved = reserve > 0 && this.mag < this.def.magSize ? 1 : 0;
      this.mag += moved;
      this.reloadTimer = this.mag < this.def.magSize && reserve - moved > 0 ? this.def.reloadTime : 0;
      return moved;
    }
    const take = Math.min(this.def.magSize - this.mag, reserve);
    this.mag += take;
    this.reloadTimer = 0;
    return take;
  }
  cancelReload() { this.reloadTimer = 0; }
  get reloadProgress() { return this.reloading && this.def.reloadTime > 0 ? 1 - this.reloadTimer / this.def.reloadTime : 0; }
}

export class Inventory {
  slots: (WeaponState | null)[] = [null, null, null, null, null];
  reserve = new Map<AmmoType, number>();
  keys = new Set<string>();
  intel: string[] = [];
  medkits = 0; cashFound = 0; current = 0;
  get currentWeapon() { return this.slots[this.current]; }
  getReserve(t: AmmoType) { return this.reserve.get(t) ?? 0; }
  addReserve(t: AmmoType, n: number) { if (t !== AmmoType.None) this.reserve.set(t, Math.max(0, this.getReserve(t) + n)); }
  setSlot(slot: WeaponSlot, w: WeaponState) { this.slots[slot] = w; }
  select(i: number): boolean {
    if (i < 0 || i >= 5 || !this.slots[i] || i === this.current) return false;
    this.currentWeapon?.cancelReload();
    this.current = i;
    return true;
  }
  cycle(dir: number): boolean {
    for (let i = 1; i <= 5; i++) {
      const idx = (((this.current + dir * i) % 5) + 5) % 5;
      if (this.slots[idx]) return this.select(idx);
    }
    return false;
  }
}
