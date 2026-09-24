// Loads the packed assets (see web/tools/pack_assets.py).
export const images = new Map<string, HTMLImageElement>();
export let maps: Record<string, string> = {};
export let audioIndex: Record<string, [string, number, number, number]> = {};
export const audioPacks = new Map<string, Int16Array>();

async function loadImage(key: string, src: string): Promise<void> {
  const img = new Image();
  img.src = src;
  try { await img.decode(); } catch { /* keep going: a missing sprite just won't draw */ }
  images.set(key, img);
}

/** Audio packs are one long WAV each: skip the 44-byte header, the rest is int16 PCM that audio.json indexes into. */
async function loadPack(url: string): Promise<Int16Array> {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url}: ${r.status}`);
  const buf = await r.arrayBuffer();
  return new Int16Array(buf, 44, (buf.byteLength - 44) >> 1);
}

export async function loadAll(progress: (p: number, label: string) => void): Promise<void> {
  progress(0.02, 'Loading maps');
  maps = await (await fetch('maps.json')).json();
  progress(0.08, 'Loading art');
  const sprites: Record<string, string> = await (await fetch('sprites.json')).json();
  const keys = Object.keys(sprites);
  let done = 0;
  await Promise.all(keys.map(k => loadImage(k, sprites[k]).then(() => { done++; progress(0.08 + 0.3 * done / keys.length, 'Loading art'); })));
  progress(0.4, 'Loading sound');
  audioIndex = await (await fetch('audio.json')).json();
  audioPacks.set('sfx', await loadPack('sfx.wav'));
  progress(0.6, 'Loading music');
  audioPacks.set('music', await loadPack('music.wav'));
  progress(1, 'Ready');
}

export const img = (key: string): HTMLImageElement | undefined => images.get(key);
