"""Packs the Unity project's generated assets into a few files for the browser build (web/dist):
  sprites.json    { "Characters/guard": "data:image/png;base64,...", ... }
  maps.json       { "mansion": "<map text>", ... }
  audio.json      { "SFX/pistol": ["sfx", offset, length, rate], ... }
  sfx.wav / music.wav   little-endian int16 mono PCM, concatenated behind a 44-byte WAV header
                        (a .wav so the artifact host serves it; offsets in audio.json count samples after the header)
Keeps the artifact under its file-count limit and lets the game load everything in 5 requests.
"""
import base64, json, os, wave, sys
import numpy as np

ROOT = os.path.join(os.path.dirname(__file__), '..', '..')
RES = os.path.join(ROOT, 'Assets', 'Resources')
OUT = os.path.join(ROOT, 'web', 'dist')
os.makedirs(OUT, exist_ok=True)

sprites = {}
for dirpath, _, files in os.walk(os.path.join(RES, 'Sprites')):
    for f in sorted(files):
        if not f.endswith('.png'):
            continue
        full = os.path.join(dirpath, f)
        key = os.path.relpath(full, os.path.join(RES, 'Sprites'))[:-4].replace(os.sep, '/')
        sprites[key] = 'data:image/png;base64,' + base64.b64encode(open(full, 'rb').read()).decode()
json.dump(sprites, open(os.path.join(OUT, 'sprites.json'), 'w'))

maps = {os.path.splitext(f)[0]: open(os.path.join(RES, 'Maps', f)).read() for f in os.listdir(os.path.join(RES, 'Maps')) if f.endswith('.txt')}
json.dump(maps, open(os.path.join(OUT, 'maps.json'), 'w'))

index = {}
packs = {'sfx': [], 'music': []}
offsets = {'sfx': 0, 'music': 0}
for dirpath, _, files in os.walk(os.path.join(RES, 'Audio')):
    for f in sorted(files):
        if not f.endswith('.wav'):
            continue
        full = os.path.join(dirpath, f)
        key = os.path.relpath(full, os.path.join(RES, 'Audio'))[:-4].replace(os.sep, '/')
        with wave.open(full) as w:
            rate = w.getframerate()
            data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)
        pack = 'music' if key.startswith('Music/') or key.startswith('Ambience/') else 'sfx'
        if pack == 'music' and rate > 16000:
            # long loops: resample to 16 kHz to halve the download
            n = int(len(data) * 16000 / rate)
            data = np.interp(np.linspace(0, len(data) - 1, n), np.arange(len(data)), data.astype(np.float64)).astype(np.int16)
            rate = 16000
        index[key] = [pack, offsets[pack], len(data), rate]
        packs[pack].append(data.tobytes())
        offsets[pack] += len(data)
for name, chunks in packs.items():
    pcm = b''.join(chunks)
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(16000 if name == 'music' else 44100)
        w.writeframes(pcm)
json.dump(index, open(os.path.join(OUT, 'audio.json'), 'w'))

for f in sorted(os.listdir(OUT)):
    print(f'{f:16s} {os.path.getsize(os.path.join(OUT, f)) / 1e6:6.2f} MB')
