// Port of ProgressData.cs + Shop.cs (saved as JSON in localStorage by the browser layer)
import { clamp, clamp01 } from './math';
import { WEAPONS, UPGRADES, getUpgrade, applyUpgrades, AmmoType, WeaponClass, WeaponSlot, WeaponDef, UpgradeKind } from './weapons';
import { MISSIONS, getMission, LoadoutConfig, MissionResult, Difficulty } from './entities';

export const MAX_MEDKITS = 3;

export interface MissionRecord { bestTime: number; bestRating: string | null; bestPayout: number; silentAssassin: boolean; completions: number; }

export class ProgressData {
  version = 1;
  money = 1500;
  ownedWeapons: string[] = ['knife', 'pistol', 'coin'];
  upgrades: string[] = [];
  ammo: Record<string, number> = { Pistol: 48 };
  medkits = 1;
  armor = 'none';
  loadout: (string | null)[] = ['knife', 'pistol', null, null, 'coin'];
  unlockedMissions: string[] = ['mansion_host'];
  completedMissions: string[] = [];
  records: Record<string, MissionRecord> = {};
  totalKills = 0; totalEarned = 0; missionsPlayed = 0;

  owns(id: string) { return this.ownedWeapons.includes(id); }
  hasUpgrade(id: string) { return this.upgrades.includes(id); }
  getAmmo(t: AmmoType) { return this.ammo[AmmoType[t]] ?? 0; }
  setAmmo(t: AmmoType, v: number) { this.ammo[AmmoType[t]] = v; }
  isCompleted(id: string) { return this.completedMissions.includes(id); }
  isUnlocked(id: string) { return this.unlockedMissions.includes(id); }

  static fromJson(json: string | null): ProgressData {
    const p = new ProgressData();
    if (json) { try { Object.assign(p, JSON.parse(json)); } catch { /* corrupt: fresh profile */ } }
    p.sanitize();
    return p;
  }

  sanitize() {
    if (!Array.isArray(this.ownedWeapons)) this.ownedWeapons = [];
    for (const b of ['knife', 'pistol', 'coin']) if (!this.ownedWeapons.includes(b)) this.ownedWeapons.push(b);
    this.ownedWeapons = this.ownedWeapons.filter(id => WEAPONS.has(id));
    if (!Array.isArray(this.upgrades)) this.upgrades = [];
    this.upgrades = this.upgrades.filter(id => getUpgrade(id) || id === 'coin.pouch');
    if (typeof this.ammo !== 'object' || !this.ammo) this.ammo = {};
    if (!Array.isArray(this.unlockedMissions)) this.unlockedMissions = [];
    if (!this.unlockedMissions.includes('mansion_host')) this.unlockedMissions.push('mansion_host');
    if (!Array.isArray(this.completedMissions)) this.completedMissions = [];
    if (typeof this.records !== 'object' || !this.records) this.records = {};
    if (this.armor !== 'light' && this.armor !== 'heavy') this.armor = 'none';
    this.money = Math.max(0, Math.floor(this.money || 0));
    this.medkits = clamp(this.medkits | 0, 0, MAX_MEDKITS);
    if (!Array.isArray(this.loadout) || this.loadout.length !== 5) this.loadout = ['knife', 'pistol', null, null, 'coin'];
    for (let i = 0; i < 5; i++) {
      const d = this.loadout[i] ? WEAPONS.get(this.loadout[i]!) : undefined;
      if (!d || !this.owns(d.id) || d.slot !== i) this.loadout[i] = null;
    }
    if (!this.loadout[0]) this.loadout[0] = 'knife';
    if (!this.loadout[4]) this.loadout[4] = 'coin';
  }

  static armorValue(a: string) { return a === 'heavy' ? 100 : a === 'light' ? 50 : 0; }

  buildLoadout(): LoadoutConfig {
    const lo = new LoadoutConfig();
    lo.slotWeapons = this.loadout.slice();
    lo.upgrades = new Set(this.upgrades);
    lo.medkits = this.medkits;
    lo.armor = ProgressData.armorValue(this.armor);
    lo.speedMult = this.armor === 'heavy' ? 0.94 : 1;
    for (const k of Object.keys(this.ammo)) lo.ammo.set(AmmoType[k as keyof typeof AmmoType], this.ammo[k]);
    lo.ammo.set(AmmoType.Pistol, Math.max(this.getAmmo(AmmoType.Pistol), 36));
    lo.ammo.set(AmmoType.Coins, 3 + (this.hasUpgrade('coin.pouch') ? 3 : 0));
    if (this.owns('throwing_knives') && this.getAmmo(AmmoType.Knives) <= 0 && this.loadout[3]) lo.ammo.set(AmmoType.Knives, 2);
    return lo;
  }

  applyResult(r: MissionResult) {
    this.missionsPlayed++;
    if (!r.success) return;
    this.money += r.total; this.totalEarned += r.total; this.totalKills += r.kills;
    for (const [k, v] of r.ammoLeft) if (k !== AmmoType.Coins) this.setAmmo(k, Math.max(0, v));
    this.medkits = clamp(r.medkitsLeft, 0, MAX_MEDKITS);
    if (!this.completedMissions.includes(r.missionId)) this.completedMissions.push(r.missionId);
    for (const m of MISSIONS) if (m.unlockedBy === r.missionId && !this.unlockedMissions.includes(m.id)) this.unlockedMissions.push(m.id);
    let rec = this.records[r.missionId];
    if (!rec) rec = this.records[r.missionId] = { bestTime: 1e9, bestRating: null, bestPayout: 0, silentAssassin: false, completions: 0 };
    rec.completions++;
    rec.bestTime = Math.min(rec.bestTime, r.time);
    rec.bestPayout = Math.max(rec.bestPayout, r.total);
    if (!r.spotted) rec.silentAssassin = true;
    if (!rec.bestRating || ratingRank(r.rating) > ratingRank(rec.bestRating)) rec.bestRating = r.rating;
  }
}

export function ratingRank(r: string) {
  return ({ 'Silent Assassin': 5, Shadow: 4, Professional: 3, Mercenary: 2, Butcher: 1 } as Record<string, number>)[r] ?? 0;
}

export class SettingsData {
  masterVolume = 0.8; musicVolume = 0.6; sfxVolume = 0.9; screenShake = true; showVisionCones = true;
  difficulty = Difficulty.Normal;
  bindings: Record<string, string> = {};
  static fromJson(json: string | null): SettingsData {
    const s = new SettingsData();
    if (json) { try { Object.assign(s, JSON.parse(json)); } catch { /* ignore */ } }
    s.masterVolume = clamp01(+s.masterVolume); s.musicVolume = clamp01(+s.musicVolume); s.sfxVolume = clamp01(+s.sfxVolume);
    if (![0, 1, 2].includes(s.difficulty)) s.difficulty = Difficulty.Normal;
    if (typeof s.bindings !== 'object' || !s.bindings) s.bindings = {};
    return s;
  }
}

// ------------------------------------------------------------------ shop

export enum ShopCategory { Melee, Pistols, SMGs, Rifles, Shotguns, Throwables, Ammo, Gear, Upgrades }
export enum ShopItemKind { Weapon, Upgrade, Ammo, Armor, Medkit, Gadget }
export enum BuyResult { Ok, NotEnoughMoney, AlreadyOwned, Locked, Maxed, RequiresWeapon }
export interface StatLine { label: string; value: number; text: string; }
export interface ShopItem {
  id: string; name: string; description: string; category: ShopCategory; kind: ShopItemKind; price: number; unlockAfter: string | null;
  weaponId?: string; upgradeId?: string; ammoType?: AmmoType; amount?: number; armorTier?: string; icon: string;
}

const stat = (label: string, v: number, text: string): StatLine => ({ label, value: clamp01(v), text });

export function weaponStats(w: WeaponDef): StatLine[] {
  const l: StatLine[] = [];
  l.push(stat('Damage', w.damage * w.pellets / 120, w.pellets > 1 ? `${w.damage.toFixed(0)} x${w.pellets}` : w.damage.toFixed(0)));
  if (w.isMelee) {
    l.push(stat('Speed', w.fireRate / 3, `${w.fireRate.toFixed(1)}/s`));
    l.push(stat('Reach', w.meleeRange / 1.5, `${w.meleeRange.toFixed(1)} m`));
    l.push(stat('Noise', 0.05, 'Silent'));
    return l;
  }
  l.push(stat('Fire rate', w.fireRate / 12, `${(w.fireRate * 60).toFixed(0)} rpm${w.automatic ? ' auto' : ''}`));
  if (w.isGun) {
    l.push(stat('Magazine', w.magSize / 45, `${w.magSize}`));
    l.push(stat('Reload', 1 - (w.shellReload ? w.reloadTime * 3 : w.reloadTime) / 3, w.shellReload ? `${w.reloadTime.toFixed(2)}s/shell` : `${w.reloadTime.toFixed(1)}s`));
    l.push(stat('Accuracy', 1 - w.spread / 12, `${w.spread.toFixed(1)}deg`));
    l.push(stat('Recoil', 1 - w.recoil / 7, w.recoil.toFixed(1)));
  }
  l.push(stat('Range', w.range / 22, `${w.range.toFixed(0)} m`));
  l.push(stat('Noise', w.noise / 22, w.suppressed ? 'Suppressed' : `${w.noise.toFixed(0)} m`));
  return l;
}

export function itemStats(it: ShopItem): StatLine[] {
  const w = it.weaponId ? WEAPONS.get(it.weaponId) : undefined;
  if (it.kind === ShopItemKind.Weapon && w) return weaponStats(w);
  if (it.kind === ShopItemKind.Upgrade && w) {
    const up = getUpgrade(it.upgradeId!)!;
    const a = applyUpgrades(w, [it.upgradeId!]);
    if (up.kind === UpgradeKind.Suppressor) return [stat('Noise', a.noise / 22, `${w.noise.toFixed(0)} -> ${a.noise.toFixed(1)} m`), stat('Damage', a.damage / 100, `${w.damage.toFixed(0)} -> ${a.damage.toFixed(0)}`)];
    if (up.kind === UpgradeKind.ExtendedMag) return [stat('Magazine', a.magSize / 45, `${w.magSize} -> ${a.magSize}`)];
    return [stat('Recoil', 1 - a.recoil / 7, `${w.recoil.toFixed(1)} -> ${a.recoil.toFixed(1)}`), stat('Accuracy', 1 - a.spread / 12, `${w.spread.toFixed(1)} -> ${a.spread.toFixed(1)}deg`)];
  }
  if (it.kind === ShopItemKind.Ammo) return [stat('Rounds', it.amount! / 60, `+${it.amount}`)];
  if (it.kind === ShopItemKind.Armor) return [stat('Armor', ProgressData.armorValue(it.armorTier!) / 100, `${ProgressData.armorValue(it.armorTier!)} points`)];
  if (it.kind === ShopItemKind.Medkit) return [stat('Heals', 0.6, '+60 health')];
  return [];
}

function catFor(c: WeaponClass): ShopCategory {
  switch (c) {
    case WeaponClass.Melee: return ShopCategory.Melee; case WeaponClass.Pistol: return ShopCategory.Pistols; case WeaponClass.SMG: return ShopCategory.SMGs;
    case WeaponClass.Rifle: return ShopCategory.Rifles; case WeaponClass.Shotgun: return ShopCategory.Shotguns; default: return ShopCategory.Throwables;
  }
}

export const CATALOG: ShopItem[] = [];
for (const w of WEAPONS.values()) {
  if (w.price <= 0) continue;
  CATALOG.push({ id: 'w.' + w.id, name: w.name, description: w.description, kind: ShopItemKind.Weapon, weaponId: w.id, price: w.price, unlockAfter: w.unlockAfter, category: catFor(w.cls), icon: 'w_' + w.id });
}
for (const u of UPGRADES)
  CATALOG.push({ id: 'u.' + u.id, name: u.name, description: u.description, kind: ShopItemKind.Upgrade, weaponId: u.weaponId, upgradeId: u.id, price: u.price, unlockAfter: u.unlockAfter, category: ShopCategory.Upgrades, icon: 'up_' + UpgradeKind[u.kind].toLowerCase() });
CATALOG.push(
  { id: 'a.pistol', name: '9mm Ammo (48)', description: 'Fits pistols and the Vektor SMG.', kind: ShopItemKind.Ammo, ammoType: AmmoType.Pistol, amount: 48, price: 160, category: ShopCategory.Ammo, unlockAfter: null, icon: 'ammo_pistol' },
  { id: 'a.rifle', name: '5.56mm Ammo (60)', description: 'For the KR-7 rifle.', kind: ShopItemKind.Ammo, ammoType: AmmoType.Rifle, amount: 60, price: 320, category: ShopCategory.Ammo, unlockAfter: 'office_cfo', icon: 'ammo_rifle' },
  { id: 'a.shells', name: '12G Shells (16)', description: 'Buckshot for the Breacher.', kind: ShopItemKind.Ammo, ammoType: AmmoType.Shells, amount: 16, price: 240, category: ShopCategory.Ammo, unlockAfter: 'facility_voss', icon: 'ammo_shells' },
  { id: 'a.knives', name: 'Throwing Knives (3)', description: 'Balanced blades. Recover them after use.', kind: ShopItemKind.Ammo, ammoType: AmmoType.Knives, amount: 3, price: 300, category: ShopCategory.Ammo, weaponId: 'throwing_knives', unlockAfter: null, icon: 'ammo_knives' },
  { id: 'g.armor_light', name: 'Kevlar Vest', description: '50 armor every mission. Absorbs 60% of incoming damage.', kind: ShopItemKind.Armor, armorTier: 'light', price: 1800, category: ShopCategory.Gear, unlockAfter: null, icon: 'armor_light' },
  { id: 'g.armor_heavy', name: 'Tactical Plate Carrier', description: '100 armor every mission. Slightly slower.', kind: ShopItemKind.Armor, armorTier: 'heavy', price: 4500, category: ShopCategory.Gear, unlockAfter: 'facility_voss', icon: 'armor_heavy' },
  { id: 'g.medkit', name: 'Medkit', description: `Restores 60 health (press H). Carry up to ${MAX_MEDKITS}.`, kind: ShopItemKind.Medkit, price: 450, category: ShopCategory.Gear, unlockAfter: null, icon: 'medkit' },
  { id: 'g.coin_pouch', name: 'Coin Pouch', description: '+3 distraction coins every mission.', kind: ShopItemKind.Gadget, upgradeId: 'coin.pouch', price: 600, category: ShopCategory.Gear, unlockAfter: null, icon: 'coin' },
);

export function maxAmmo(t: AmmoType) {
  return t === AmmoType.Pistol ? 240 : t === AmmoType.Rifle ? 300 : t === AmmoType.Shells ? 64 : t === AmmoType.Knives ? 9 : 99;
}
export const isUnlocked = (p: ProgressData, it: ShopItem) => !it.unlockAfter || p.isCompleted(it.unlockAfter);
export function isOwned(p: ProgressData, it: ShopItem) {
  switch (it.kind) {
    case ShopItemKind.Weapon: return p.owns(it.weaponId!);
    case ShopItemKind.Upgrade: case ShopItemKind.Gadget: return p.hasUpgrade(it.upgradeId!);
    case ShopItemKind.Armor: return p.armor === it.armorTier || (it.armorTier === 'light' && p.armor === 'heavy');
    default: return false;
  }
}
export function isEquipped(p: ProgressData, it: ShopItem) {
  if (it.kind === ShopItemKind.Weapon) return p.loadout.includes(it.weaponId!);
  if (it.kind === ShopItemKind.Armor) return p.armor === it.armorTier;
  return isOwned(p, it);
}
export function canBuy(p: ProgressData, it: ShopItem): BuyResult {
  if (!isUnlocked(p, it)) return BuyResult.Locked;
  if (isOwned(p, it)) return BuyResult.AlreadyOwned;
  if (it.kind === ShopItemKind.Upgrade && !p.owns(it.weaponId!)) return BuyResult.RequiresWeapon;
  if (it.kind === ShopItemKind.Ammo && it.weaponId && !p.owns(it.weaponId)) return BuyResult.RequiresWeapon;
  if (it.kind === ShopItemKind.Medkit && p.medkits >= MAX_MEDKITS) return BuyResult.Maxed;
  if (it.kind === ShopItemKind.Ammo && p.getAmmo(it.ammoType!) >= maxAmmo(it.ammoType!)) return BuyResult.Maxed;
  if (p.money < it.price) return BuyResult.NotEnoughMoney;
  return BuyResult.Ok;
}
export function buy(p: ProgressData, it: ShopItem): BuyResult {
  const r = canBuy(p, it);
  if (r !== BuyResult.Ok) return r;
  p.money -= it.price;
  switch (it.kind) {
    case ShopItemKind.Weapon: {
      p.ownedWeapons.push(it.weaponId!);
      const d = WEAPONS.get(it.weaponId!)!;
      const slot = d.slot;
      if (!p.loadout[slot] || (p.loadout[slot] === 'pistol' && d.cls === WeaponClass.Pistol)) p.loadout[slot] = d.id;
      if (d.isGun) p.setAmmo(d.ammo, Math.min(maxAmmo(d.ammo), p.getAmmo(d.ammo) + d.magSize * 2));
      if (d.cls === WeaponClass.Throwing) p.setAmmo(AmmoType.Knives, Math.min(maxAmmo(AmmoType.Knives), p.getAmmo(AmmoType.Knives) + 3));
      break;
    }
    case ShopItemKind.Upgrade: case ShopItemKind.Gadget: p.upgrades.push(it.upgradeId!); break;
    case ShopItemKind.Ammo: p.setAmmo(it.ammoType!, Math.min(maxAmmo(it.ammoType!), p.getAmmo(it.ammoType!) + it.amount!)); break;
    case ShopItemKind.Armor: p.armor = it.armorTier!; break;
    case ShopItemKind.Medkit: p.medkits++; break;
  }
  return BuyResult.Ok;
}
export function equip(p: ProgressData, slot: WeaponSlot, weaponId: string | null): boolean {
  if (!weaponId) { if (slot === WeaponSlot.Melee) return false; p.loadout[slot] = null; return true; }
  const d = WEAPONS.get(weaponId);
  if (!d || !p.owns(weaponId) || d.slot !== slot) return false;
  p.loadout[slot] = weaponId;
  return true;
}
export function describeBuy(r: BuyResult) {
  return ['Purchased', 'Not enough money', 'Already owned', 'Locked - complete more contracts', 'Carrying the maximum', 'Requires the weapon'][r];
}
export { getMission };
