// WebAudio port of AudioManager.cs + MusicDirector.cs
import { audioIndex, audioPacks } from './assets';

export enum Mood { Calm, Tension, Combat }

class AudioSystem {
  ctx: AudioContext | null = null;
  private master!: GainNode; private sfx!: GainNode; private music!: GainNode;
  private buffers = new Map<string, AudioBuffer | null>();
  private variants = new Map<string, AudioBuffer[]>();
  private last = new Map<string, number>();
  private stems: { src: AudioBufferSourceNode; gain: GainNode }[] = [];
  private menuSrc: { src: AudioBufferSourceNode; gain: GainNode } | null = null;
  private ambience: AudioBufferSourceNode | null = null;
  private heart: { src: AudioBufferSourceNode; gain: GainNode } | null = null;
  private w = [1, 0, 0];
  private mood = Mood.Calm; private detection = 0; private heartRate = 0;
  listener = { x: 0, y: 0 };
  volumes = { master: 0.8, music: 0.6, sfx: 0.9 };

  /** Must be called from a user gesture (browsers block audio before that). */
  unlock() {
    if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    const AC = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    if (!AC) return;
    this.ctx = new AC();
    this.master = this.ctx.createGain(); this.master.connect(this.ctx.destination);
    this.sfx = this.ctx.createGain(); this.sfx.connect(this.master);
    this.music = this.ctx.createGain(); this.music.connect(this.master);
    this.applyVolumes();
  }

  applyVolumes() {
    if (!this.ctx) return;
    this.master.gain.value = this.volumes.master;
    this.sfx.gain.value = this.volumes.sfx;
    this.music.gain.value = this.volumes.music;
  }

  buffer(key: string): AudioBuffer | null {
    if (this.buffers.has(key)) return this.buffers.get(key)!;
    const e = audioIndex[key];
    let b: AudioBuffer | null = null;
    if (e && this.ctx) {
      const [pack, off, len, rate] = e;
      const pcm = audioPacks.get(pack)!;
      b = this.ctx.createBuffer(1, len, rate);
      const ch = b.getChannelData(0);
      for (let i = 0; i < len; i++) ch[i] = pcm[off + i] / 32768;
    }
    this.buffers.set(key, b);
    return b;
  }

  private variant(base: string): AudioBuffer | null {
    let arr = this.variants.get(base);
    if (!arr) {
      arr = [];
      for (let i = 0; i < 4; i++) { const b = this.buffer(`${base}_${i}`); if (b) arr.push(b); }
      if (arr.length === 0) { const b = this.buffer(base); if (b) arr.push(b); }
      this.variants.set(base, arr);
    }
    return arr.length ? arr[Math.floor(Math.random() * arr.length)] : null;
  }

  private start(buf: AudioBuffer, vol: number, pitch: number, pan: number, dest: AudioNode) {
    const ctx = this.ctx!;
    const src = ctx.createBufferSource();
    src.buffer = buf;
    src.playbackRate.value = pitch;
    const g = ctx.createGain();
    g.gain.value = vol;
    let node: AudioNode = g;
    if (pan !== 0 && ctx.createStereoPanner) { const p = ctx.createStereoPanner(); p.pan.value = pan; g.connect(p); node = p; }
    src.connect(g);
    node.connect(dest);
    src.start();
  }

  play2D(name: string, vol = 1, pitch = 1) {
    if (!this.ctx) return;
    const b = this.buffer('SFX/' + name) ?? this.variant('SFX/' + name);
    if (b) this.start(b, vol, pitch, 0, this.sfx);
  }

  playAt(name: string, x: number, y: number, vol = 1, range = 18, jitter = 0.06, minInterval = 0.02, variants = false) {
    if (!this.ctx) return;
    const now = this.ctx.currentTime;
    if (now - (this.last.get(name) ?? -1) < minInterval) return;
    const b = variants ? this.variant('SFX/' + name) : this.buffer('SFX/' + name) ?? this.variant('SFX/' + name);
    if (!b) return;
    const d = Math.hypot(x - this.listener.x, y - this.listener.y);
    if (d > range) return;
    let att = 1 - d / range;
    att *= att;
    this.last.set(name, now);
    this.start(b, vol * att, 1 + (Math.random() * 2 - 1) * jitter, Math.max(-0.8, Math.min(0.8, (x - this.listener.x) / 12)), this.sfx);
  }

  voice(voice: string, cat: string, x: number, y: number, vol = 0.8) {
    if (!this.ctx) return;
    const b = this.variant(`Voice/${voice}_${cat}`);
    if (!b) return;
    const d = Math.hypot(x - this.listener.x, y - this.listener.y);
    if (d > 20) return;
    this.start(b, vol * (1 - d / 20), 0.94 + Math.random() * 0.12, Math.max(-0.8, Math.min(0.8, (x - this.listener.x) / 12)), this.sfx);
  }

  private loop(key: string, vol: number, when = 0): { src: AudioBufferSourceNode; gain: GainNode } | null {
    const b = this.buffer(key);
    if (!b || !this.ctx) return null;
    const src = this.ctx.createBufferSource();
    src.buffer = b; src.loop = true;
    const gain = this.ctx.createGain();
    gain.gain.value = vol;
    src.connect(gain); gain.connect(this.music);
    src.start(when);
    return { src, gain };
  }

  playMenu() {
    this.stopMission();
    if (!this.ctx || this.menuSrc) return;
    this.menuSrc = this.loop('Music/menu', 0.8);
  }

  startMission(theme: string) {
    if (!this.ctx) return;
    this.stopMenu();
    this.stopMission();
    const when = this.ctx.currentTime + 0.15;
    for (const [i, k] of ['Music/calm', 'Music/tension', 'Music/combat'].entries()) {
      const s = this.loop(k, i === 0 ? 0.7 : 0, when);
      if (s) this.stems.push(s);
    }
    const amb = this.buffer('Ambience/' + theme);
    if (amb) {
      this.ambience = this.ctx.createBufferSource();
      this.ambience.buffer = amb; this.ambience.loop = true;
      const g = this.ctx.createGain(); g.gain.value = 0.45;
      this.ambience.connect(g); g.connect(this.master);
      this.ambience.start();
    }
    this.w = [1, 0, 0];
  }

  stopMenu() { if (this.menuSrc) { try { this.menuSrc.src.stop(); } catch { /* */ } this.menuSrc = null; } }

  stopMission() {
    for (const s of this.stems) { try { s.src.stop(); } catch { /* */ } }
    this.stems = [];
    if (this.ambience) { try { this.ambience.stop(); } catch { /* */ } this.ambience = null; }
    if (this.heart) { try { this.heart.src.stop(); } catch { /* */ } this.heart = null; }
  }

  sting(key: string, vol = 0.9) {
    if (!this.ctx) return;
    this.stopMission();
    const b = this.buffer('Music/' + key);
    if (b) this.start(b, vol, 1, 0, this.music);
  }

  setMood(m: Mood, detection: number) { this.mood = m; this.detection = detection; }
  setHeartbeat(healthFrac: number) { this.heartRate = healthFrac < 0.3 ? 1 - healthFrac / 0.3 : 0; }

  update(dt: number) {
    if (!this.ctx || this.stems.length < 3) return;
    let tc = 0, tt = 0, tk = 0;
    if (this.mood === Mood.Calm) { tc = 1; tt = Math.min(1, this.detection * 1.4) * 0.8; }
    else if (this.mood === Mood.Tension) { tc = 0.35; tt = 1; }
    else { tt = 0.45; tk = 1; }
    const mt = (c: number, t: number, up: number, down: number) => { const r = (t > c ? up : down) * dt; return Math.abs(t - c) <= r ? t : c + Math.sign(t - c) * r; };
    this.w[0] = mt(this.w[0], tc, 0.6, 0.25);
    this.w[1] = mt(this.w[1], tt, 1.2, 0.2);
    this.w[2] = mt(this.w[2], tk, 2.5, 0.15);
    this.stems[0].gain.gain.value = this.w[0] * 0.7;
    this.stems[1].gain.gain.value = this.w[1] * 0.8;
    this.stems[2].gain.gain.value = this.w[2] * 0.85;
    if (this.heartRate > 0.01) {
      if (!this.heart) this.heart = this.loop('SFX/heartbeat', 0);
      if (this.heart) { this.heart.gain.gain.value = this.heartRate; this.heart.src.playbackRate.value = 1 + this.heartRate * 0.4; }
    } else if (this.heart) { try { this.heart.src.stop(); } catch { /* */ } this.heart = null; }
  }
}

export const audio = new AudioSystem();
