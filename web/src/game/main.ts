// Browser entry point: GameManager.cs + MissionRunner.cs. Owns the profile, the menus and the running mission.
import { Vec2 } from '../core/math';
import { MapData } from '../core/map';
import { GameSession, TICK, surfaceName } from '../core/session';
import { AlertLevel, EvType, MissionState, NpcKind, PlayerInput, getMission, InteractKind, MISSIONS } from '../core/entities';
import { ProgressData, SettingsData, CATALOG, isUnlocked, ShopItemKind } from '../core/meta';
import { loadAll, maps } from './assets';
import { audio, Mood } from './audio';
import { input } from './input';
import { Renderer } from './render';
import { Hud } from './hud';
import { Menus, GameApi } from './ui';

const PROGRESS_KEY = 'shadow-contract.progress';
const SETTINGS_KEY = 'shadow-contract.settings';
function load(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } }
function save(key: string, v: string) { try { localStorage.setItem(key, v); } catch { /* storage blocked: progress lasts this visit */ } }

class Game implements GameApi {
  progress = ProgressData.fromJson(load(PROGRESS_KEY));
  settings = SettingsData.fromJson(load(SETTINGS_KEY));
  session: GameSession | null = null;
  lastMissionId: string | null = null;
  pendingMissionId: string | null = null;

  readonly canvas: HTMLCanvasElement;
  readonly renderer: Renderer;
  readonly hud: Hud;
  readonly menus: Menus;
  private readonly inp = new PlayerInput();
  private paused = false; private ended = false;
  private accum = 0; private endTimer = -1; private timeScale = 1;
  private distractionTimer = 0;
  private npcSteps = new Map<number, number>();
  private lastT = 0;

  constructor(private app: HTMLElement) {
    this.canvas = document.createElement('canvas');
    this.canvas.className = 'game-canvas';
    app.appendChild(this.canvas);
    this.renderer = new Renderer(this.canvas);
    this.hud = new Hud(app);
    this.menus = new Menus(app, this);
    input.attach(this.canvas);
    window.addEventListener('resize', () => this.renderer.resize());
    this.applySettings();
    // Leaving the tab mid-mission pauses the game.
    document.addEventListener('visibilitychange', () => { if (document.hidden && this.session && !this.paused && !this.ended) this.pauseMission(); });
  }

  // ------------------------------------------------------------------ profile & settings
  saveProgress() { save(PROGRESS_KEY, JSON.stringify(this.progress)); }
  saveSettings() { save(SETTINGS_KEY, JSON.stringify(this.settings)); }
  resetProgress() { this.progress = new ProgressData(); this.progress.sanitize(); this.saveProgress(); }
  applySettings() {
    const s = this.settings;
    input.load(s.bindings);
    audio.volumes.master = s.masterVolume; audio.volumes.music = s.musicVolume; audio.volumes.sfx = s.sfxVolume;
    audio.applyVolumes();
    this.renderer.shakeEnabled = s.screenShake;
    this.renderer.showCones = s.showVisionCones;
  }
  isFullscreen() { return !!document.fullscreenElement; }
  toggleFullscreen() {
    try {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      else document.documentElement.requestFullscreen().catch(() => {});
    } catch { /* not allowed here */ }
  }

  // ------------------------------------------------------------------ missions
  endMissionWorld() {
    if (!this.session) return;
    this.session = null;
    this.hud.unbind(); this.hud.show(false);
    this.renderer.unbind();
    this.canvas.classList.remove('on');
    input.gameActive = false;
    audio.stopMission();
    this.timeScale = 1;
    document.body.classList.remove('playing');
  }

  startMission(id: string) {
    const mission = getMission(id);
    if (!mission || !this.progress.isUnlocked(id)) return;
    this.endMissionWorld();
    const text = maps[mission.mapId];
    if (!text) { console.error('Missing map ' + mission.mapId); return; }
    const map = MapData.parse(text);
    this.lastMissionId = id; this.pendingMissionId = id;
    this.menus.hide();
    this.session = new GameSession(map, mission, this.settings.difficulty, this.progress.buildLoadout(), 1 + Math.floor(Math.random() * (1 << 30)));
    this.paused = false; this.ended = false; this.accum = 0; this.endTimer = -1; this.timeScale = 1;
    this.npcSteps.clear(); this.distractionTimer = 0;
    this.inp.clearEdges();
    this.renderer.bind(this.session);
    this.canvas.classList.add('on');
    this.hud.bind(this.session, this.renderer, this.progress.money);
    this.hud.show(true);
    this.hud.bannerMsg(mission.name.toUpperCase(), mission.location, '#e6e6f2');
    audio.startMission(map.theme);
    input.gameActive = true;
    document.body.classList.add('playing');
  }

  pauseMission() {
    if (!this.session || this.ended) return;
    this.paused = true;
    this.menus.pause();
    document.body.classList.remove('playing');
  }
  resumeMission() {
    if (!this.session) { this.menus.mainMenu(); return; }
    this.menus.hide();
    this.paused = false;
    document.body.classList.add('playing');
  }
  toggleInventory() {
    if (!this.session) return;
    if (this.menus.current === 'inventory') { this.resumeMission(); return; }
    this.paused = true;
    this.menus.inventory();
    document.body.classList.remove('playing');
  }
  restartMission() { if (this.lastMissionId) this.startMission(this.lastMissionId); }

  private onMissionEnded() {
    const s = this.session!;
    const result = s.buildResult();
    const unlockedBefore = new Set(this.progress.unlockedMissions);
    const shopBefore = new Set(CATALOG.filter(i => isUnlocked(this.progress, i)).map(i => i.id));
    this.progress.applyResult(result);
    this.saveProgress();
    const died = s.state === MissionState.Dead;
    this.hud.show(false);
    input.gameActive = false;
    document.body.classList.remove('playing');
    if (result.success) {
      const news: string[] = [];
      for (const m of this.progress.unlockedMissions) if (!unlockedBefore.has(m)) news.push(`contract "${getMission(m)!.name}"`);
      for (const i of CATALOG) if (isUnlocked(this.progress, i) && !shopBefore.has(i.id) && i.kind === ShopItemKind.Weapon) news.push(i.name);
      this.menus.complete(result, s.mission, news);
    } else this.menus.gameOver(result, died);
  }

  // ------------------------------------------------------------------ frame
  frame(t: number) {
    const dt = Math.min(0.1, Math.max(0, (t - (this.lastT || t)) / 1000));
    this.lastT = t;
    const s = this.session;

    if (!s) {
      if (this.menus.visible && !this.menus.capturing && input.wasPressed('Pause')) this.menus.back();
    } else {
      if (!this.ended && !this.paused) {
        if (input.wasPressed('Pause')) this.pauseMission();
        else if (input.wasPressed('Inventory')) this.toggleInventory();
      } else if (this.paused && !this.menus.capturing) {
        if (this.menus.current === 'inventory' && (input.wasPressed('Inventory') || input.wasPressed('Pause'))) this.toggleInventory();
        else if (input.wasPressed('Pause')) this.menus.back();
      } else if (this.ended && this.menus.visible && !this.menus.capturing && input.wasPressed('Pause')) this.menus.back();
    }

    let simDt = 0;
    if (this.session && !this.paused && !this.ended) {
      const ss = this.session;
      this.gatherInput(ss);
      this.accum += dt * this.timeScale;
      let steps = 0;
      while (this.accum >= TICK && steps < 5) {
        ss.update(TICK, this.inp);
        this.inp.clearEdges();
        this.handleEvents(ss);
        this.accum -= TICK; simDt += TICK;
        steps++;
        if (ss.state !== MissionState.Playing) break;
      }
      if (steps === 5) this.accum = 0; // don't spiral after a hitch
      this.updateDistractionAudio(ss, dt);
      this.updateNpcFootsteps(ss);
    }

    if (this.session) {
      const ss = this.session, p = ss.player;
      this.renderer.follow(p.pos.x, p.pos.y, this.inp.aim.x, this.inp.aim.y, p.aiming, dt);
      this.renderer.draw(this.paused ? 0 : dt * this.timeScale);
      audio.listener.x = this.renderer.camX; audio.listener.y = this.renderer.camY;
      audio.setMood(ss.alert === AlertLevel.Combat ? Mood.Combat : ss.alert >= AlertLevel.Alarmed ? Mood.Tension : Mood.Calm, ss.detectionLevel);
      audio.setHeartbeat(p.health / p.maxHealth);
      this.hud.update(dt, simDt, this.paused || this.ended);

      // Mission end: a short beat before the result screen.
      if (!this.ended && ss.state !== MissionState.Playing) {
        if (this.endTimer < 0) {
          this.endTimer = ss.state === MissionState.Complete ? 1.2 : 2.0;
          if (ss.state !== MissionState.Complete) this.timeScale = 0.35;
          if (ss.state === MissionState.Complete) this.hud.bannerMsg('CONTRACT COMPLETE', 'Extraction successful', '#80ff99');
          else this.hud.bannerMsg(ss.state === MissionState.Dead ? 'YOU DIED' : 'CONTRACT FAILED', ss.failReason ?? '', '#ff5959');
        }
        this.endTimer -= dt;
        if (this.endTimer <= 0) { this.ended = true; this.timeScale = 1; this.onMissionEnded(); }
      }
    }
    audio.update(dt);
    input.endFrame();
  }

  private gatherInput(s: GameSession) {
    const i = this.inp;
    let mx = 0, my = 0;
    if (input.held('MoveUp')) my += 1;
    if (input.held('MoveDown')) my -= 1;
    if (input.held('MoveRight')) mx += 1;
    if (input.held('MoveLeft')) mx -= 1;
    i.move = new Vec2(mx, my);
    i.aim = this.renderer.screenToWorld(input.mouseX, input.mouseY);
    i.fireHeld = input.held('Attack');
    i.altHeld = input.held('Aim');
    i.interactHeld = input.held('Interact');
    i.sprintHeld = input.held('Sprint');
    // Edges accumulate until a simulation tick consumes them.
    i.firePressed ||= input.wasPressed('Attack');
    i.altPressed ||= input.wasPressed('Aim');
    i.reloadPressed ||= input.wasPressed('Reload');
    i.interactPressed ||= input.wasPressed('Interact');
    i.crouchPressed ||= input.wasPressed('Crouch');
    i.medkitPressed ||= input.wasPressed('Medkit');
    (['Slot1', 'Slot2', 'Slot3', 'Slot4', 'Slot5'] as const).forEach((a, k) => { if (input.wasPressed(a)) i.selectSlot = k; });
    if (input.wheel < 0) i.cycle = -1; else if (input.wheel > 0) i.cycle = 1;
    void s;
  }

  private updateDistractionAudio(s: GameSession, dt: number) {
    this.distractionTimer -= dt;
    if (this.distractionTimer > 0) return;
    this.distractionTimer = 1.9;
    for (const it of s.interactables)
      if (it.kind === InteractKind.Distraction && it.active) audio.playAt('distraction_' + it.sound, it.pos.x, it.pos.y, 0.8, 16, 0, 0);
  }

  /** Guards' footsteps are audible nearby: a key stealth cue for approaching patrols. */
  private updateNpcFootsteps(s: GameSession) {
    const p = s.player.pos;
    for (const n of s.npcs) {
      if (n.down || n.isCamera) continue;
      if (Vec2.sqrDistance(n.pos, p) > 144) continue;
      const last = this.npcSteps.get(n.id) ?? 0;
      const stride = n.speed > 3.5 ? 1.25 : 0.95;
      if (n.moveAnim - last < stride) continue;
      this.npcSteps.set(n.id, n.moveAnim);
      const t = s.world.tileAt(Math.floor(n.pos.x), Math.floor(n.pos.y));
      const loud = n.speed > 3.5 ? 0.55 : n.isHostile ? 0.32 : 0.22;
      audio.playAt('step_' + surfaceName(t), n.pos.x, n.pos.y, loud, 12, 0.12, 0, true);
    }
  }

  // ------------------------------------------------------------------ events -> presentation
  private handleEvents(s: GameSession) {
    const fx = this.renderer, hud = this.hud;
    for (const e of s.events) {
      const x = e.pos.x, y = e.pos.y, id = e.actorId ?? -1, ang = e.angle ?? 0, val = e.value ?? 0;
      switch (e.type) {
        case EvType.Shot:
          fx.muzzle(x, y, ang, !!e.flag);
          if (e.sound) audio.playAt(e.sound, x, y, e.flag ? 0.7 : 1, e.flag ? 10 : 34, 0.05, 0.03);
          fx.doRecoil(id, e.flag ? 0.05 : 0.09);
          if (id === 0) { fx.kick(ang, 0.05 + val * 0.2); if (val > 0.2) fx.shake(val * 0.5); }
          break;
        case EvType.Tracer: if (e.pos2) fx.tracer(x, y, e.pos2.x, e.pos2.y, id === 0); break;
        case EvType.Casing: fx.casing(x, y, ang, e.sound === 'shell'); break;
        case EvType.Impact: {
          const metal = ['Machine', 'ServerRack', 'Vending', 'Cabinet', 'Car', 'Door'].includes(e.text ?? '');
          const n = e.pos2 ?? Vec2.Zero;
          fx.impact(x, y, n.x, n.y, metal);
          audio.playAt(metal ? 'impact_metal' : 'impact', x, y, 0.5, 14, 0.15, 0.04);
          break;
        }
        case EvType.Hit:
          fx.hit(x, y, ang, !!e.flag);
          audio.playAt('hit', x, y, 0.7, 16, 0.1, 0.03);
          fx.doFlash(id);
          break;
        case EvType.Death:
          if (e.flag && id > 0 && s.npcs.find(n => n.id === id)?.isCamera) { fx.sparks(x, y, 10); audio.playAt('impact_metal', x, y); break; }
          audio.playAt('body_fall', x, y, 0.7, 14);
          if (!e.flag) this.playVoice(s, id, 'pain', x, y);
          break;
        case EvType.Melee: fx.doSwing(id); audio.playAt('knife', x, y, 0.6, 10, 0.12); break;
        case EvType.Takedown: audio.playAt('stab', x, y, 0.8, 10); fx.shake(0.12); break;
        case EvType.Subdue: audio.playAt('body_fall', x, y, 0.4, 8); if (e.text) hud.toast(e.text, '#ffcc66'); break;
        case EvType.Throw: audio.playAt('throw', x, y, 0.5, 8, 0.15); break;
        case EvType.ReloadStart: audio.playAt(e.text === 'shell' || e.sound?.startsWith('shotgun') ? 'shell_insert' : 'reload_start', x, y, 0.6, 10); break;
        case EvType.ReloadDone: audio.playAt(e.sound?.startsWith('shotgun') ? 'pump' : 'reload_done', x, y, 0.6, 10); break;
        case EvType.DryFire: audio.playAt('dryfire', x, y, 0.6, 8); break;
        case EvType.WeaponSwitch: audio.play2D('switch', 0.5); break;
        case EvType.DoorOpen: audio.playAt('door_open', x, y, id === 0 ? 0.6 : 0.45, 14, 0.1, 0.1); break;
        case EvType.DoorClose: audio.playAt('door_close', x, y, 0.5, 14, 0.1, 0.1); break;
        case EvType.DoorLocked: audio.playAt('door_locked', x, y, 0.6, 10, 0, 0.5); break;
        case EvType.DoorUnlock: audio.playAt('door_unlock', x, y, 0.7, 10); hud.toast(`Unlocked with the ${e.text} keycard`, '#99ccff'); break;
        case EvType.WindowBreak: fx.glass(x, y); audio.playAt('glass', x, y, 0.8, 24); break;
        case EvType.Vault: audio.playAt('vault', x, y, 0.6, 10); break;
        case EvType.Explosion: {
          fx.explosion(x, y, val);
          audio.playAt('explosion', x, y, 1, 50, 0.05, 0.05);
          const d = Math.hypot(x - s.player.pos.x, y - s.player.pos.y);
          fx.shake(Math.max(0, Math.min(1, 1.1 - d / 14)));
          break;
        }
        case EvType.Pickup: {
          const snd = e.sound === 'Cash' ? 'cash' : e.sound === 'Keycard' ? 'keycard' : e.sound === 'Medkit' ? 'medkit' : 'pickup';
          audio.play2D(snd, 0.6);
          if (e.text) hud.toast(e.text, e.sound === 'Intel' || e.sound === 'Keycard' ? '#ffd966' : '#ffffff');
          break;
        }
        case EvType.Bark: hud.subtitle(id, e.text); this.playBark(e.sound ?? null, x, y); break;
        case EvType.Footstep: audio.playAt('step_' + e.text, x, y, 0.25 + val * 0.6, 9 + val * 6, 0.12, 0.05, true); break;
        case EvType.Noise:
          if (id === 0 && val >= 2.5) {
            const loud = e.text === 'Gunshot' || e.text === 'Explosion' || e.text === 'GlassBreak';
            fx.ring(x, y, val, loud ? 'rgba(255,102,89,A)' : 'rgba(255,255,255,A)');
          }
          break;
        case EvType.Objective: audio.play2D('objective', 0.8); hud.bannerMsg('OBJECTIVE COMPLETE', e.text ?? '', '#8cffa6'); break;
        case EvType.Message: hud.toast(e.text, '#d9e6ff'); break;
        case EvType.PlayerHurt:
          audio.play2D('player_hurt', 0.7, 0.9 + Math.random() * 0.2);
          hud.damage(); fx.shake(0.12); fx.doFlash(0);
          break;
        case EvType.PlayerDied: audio.play2D('death', 0.9); audio.sting('gameover', 0.8); break;
        case EvType.MissionComplete: audio.sting('victory', 0.9); break;
        case EvType.MissionFailed: audio.sting('gameover', 0.8); break;
        case EvType.Lights: audio.playAt(e.flag ? 'power_off' : 'power_on', x, y, 0.8, 30); break;
        case EvType.Teleport:
          hud.flash(0.25);
          if (e.pos2) { fx.snap(e.pos2.x, e.pos2.y); audio.playAt('step_hard', e.pos2.x, e.pos2.y, 0.6, 10, 0.1, 0, true); }
          break;
        case EvType.Hide: case EvType.Unhide: case EvType.BodyStash: audio.playAt('hide', x, y, 0.6, 10); break;
        case EvType.BodyDrag: audio.playAt('drag', x, y, 0.5, 8); break;
        case EvType.BodyDrop: audio.playAt('body_fall', x, y, 0.35, 8); break;
        case EvType.Medkit: audio.play2D('medkit', 0.7); break;
        case EvType.BodyFound: audio.play2D('body_found', 0.8); hud.warn('A BODY HAS BEEN FOUND'); break;
        case EvType.Spotted: audio.play2D('spotted', 0.7); hud.warn('SPOTTED'); break;
        case EvType.CameraDisabled: case EvType.Download: audio.playAt('terminal_done', x, y, 0.8, 12); break;
        case EvType.KnifeStuck: audio.playAt('knife_hit', x, y, 0.5, 10); break;
        case EvType.CoinLand: audio.playAt('coin', x, y, 0.8, 14, 0.1); break;
        case EvType.Radio:
          if (e.flag) { audio.playAt('radio', x, y, 0.8, 18); hud.radioStarted(id, val); }
          else if (e.text === 'alarm') { audio.play2D('camera_alarm', 0.6); hud.warn('ALARM RAISED'); hud.radioEnded(id); }
          else if (e.text === 'camera') { audio.playAt('camera_alarm', x, y, 0.9, 30); hud.warn('CAMERA ALERT'); }
          else hud.radioEnded(id);
          break;
        case EvType.SecretFound: audio.playAt('secret', x, y, 0.8, 12); break;
        case EvType.Distraction: if (e.flag) audio.playAt('distraction_' + e.text, x, y, 0.8, 16, 0, 0); break;
      }
    }
  }

  private playVoice(s: GameSession, id: number, cat: string, x: number, y: number) {
    const n = s.npcs.find(q => q.id === id);
    if (!n) return;
    const voice = n.isCivilian || (n.isTarget && !n.armed) ? 'civ' : n.kind === NpcKind.Elite ? 'elite' : 'guard';
    audio.voice(voice, cat, x, y);
  }

  /** Bark keys ("guard_spotted") map onto a handful of synthesised voice moods. */
  private playBark(key: string | null, x: number, y: number) {
    if (!key) return;
    const us = key.indexOf('_');
    const voice = key.substring(0, us), what = key.substring(us + 1);
    let cat = 'calm';
    if (['spotted', 'gunshot', 'body', 'found', 'camera'].includes(what)) cat = 'alert';
    else if (['suspicious', 'investigate', 'lost', 'body_seen', 'noise', 'lights'].includes(what)) cat = 'question';
    else if (what === 'hurt') cat = 'pain';
    else if (what === 'panic' || what === 'flee') cat = 'panic';
    else if (['radio', 'report', 'search'].includes(what)) cat = 'report';
    audio.voice(voice, cat, x, y);
  }
}

// ------------------------------------------------------------------ boot
async function boot() {
  const app = document.getElementById('app')!;
  const loading = document.getElementById('loading')!;
  const bar = loading.querySelector('.fill') as HTMLElement;
  const label = loading.querySelector('.label') as HTMLElement;
  try {
    await loadAll((p, l) => { bar.style.width = (p * 100).toFixed(0) + '%'; label.textContent = l; });
  } catch (err) {
    label.textContent = 'Could not load the game files. Reload the page to try again.';
    console.error(err);
    return;
  }
  const game = new Game(app);
  (window as unknown as { __game: Game }).__game = game;
  label.textContent = 'Click to start';
  loading.classList.add('ready');
  const start = () => {
    audio.unlock();
    loading.remove();
    game.menus.mainMenu();
  };
  loading.addEventListener('click', start, { once: true });
  window.addEventListener('keydown', function k(e) { if (e.code === 'Enter' || e.code === 'Space') { window.removeEventListener('keydown', k); if (loading.isConnected) start(); } });
  const loop = (t: number) => { game.frame(t); requestAnimationFrame(loop); };
  requestAnimationFrame(loop);
  void MISSIONS;
}

boot();
