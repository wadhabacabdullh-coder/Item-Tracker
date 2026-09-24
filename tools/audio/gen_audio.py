"""Synthesises every sound and music track in the game (no samples, no licences).

Output: Assets/Resources/Audio/{SFX,Voice,Music,Ambience}/*.wav  (22.05 kHz mono 16-bit; Unity compresses on import)
Music stems for one map share tempo, key and length so the game can crossfade calm/tension/combat in sync.
"""
import os
import wave
import numpy as np
from scipy import signal

SR = 22050
ROOT = os.path.join(os.path.dirname(__file__), '..', '..')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'Audio')
rng = np.random.default_rng(1234)


def t_axis(dur):
    return np.arange(int(SR * dur)) / SR


def noise(dur):
    return rng.uniform(-1, 1, int(SR * dur))


def env(n, attack=0.002, decay=0.1, sustain=0.0, curve=4.0):
    t = np.arange(n) / SR
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    d = np.exp(-np.maximum(t - attack, 0) / max(decay, 1e-4) * (curve / 4.0) * 4.0 / 4.0)
    return a * (sustain + (1 - sustain) * d)


def exp_env(n, tau):
    return np.exp(-np.arange(n) / SR / tau)


def lp(x, f, order=2):
    b, a = signal.butter(order, min(f / (SR / 2), 0.99), 'low')
    return signal.lfilter(b, a, x)


def hp(x, f, order=2):
    b, a = signal.butter(order, min(f / (SR / 2), 0.99), 'high')
    return signal.lfilter(b, a, x)


def bp(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), min(hi / (SR / 2), 0.99)], 'band')
    return signal.lfilter(b, a, x)


def sine(freq, dur, phase=0.0):
    t = t_axis(dur)
    if callable(freq):
        f = freq(t)
        return np.sin(2 * np.pi * np.cumsum(f) / SR + phase)
    return np.sin(2 * np.pi * freq * t + phase)


def saw(freq, dur):
    t = t_axis(dur)
    return 2 * ((t * freq) % 1.0) - 1


def square(freq, dur, duty=0.5):
    t = t_axis(dur)
    return np.where((t * freq) % 1.0 < duty, 1.0, -1.0)


def pad_to(x, n):
    if len(x) >= n:
        return x[:n]
    return np.concatenate([x, np.zeros(n - len(x))])


def mixdown(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


def reverb(x, room=0.25, decay=0.35, wet=0.25):
    """Cheap feedback-comb reverb."""
    out = x.copy()
    for delay_ms, g in ((29.7, 0.7), (37.1, 0.65), (41.1, 0.6), (43.7, 0.55)):
        d = int(SR * delay_ms / 1000 * (1 + room))
        y = np.zeros(len(x) + int(SR * decay * 3))
        y[:len(x)] = x
        for i in range(d, len(y), d):
            seg = y[i - d:i] * g * decay * 2
            y[i:i + d] += seg[:len(y[i:i + d])]
        out = pad_to(out, len(y)) + y * wet * 0.25
    return out


def norm(x, peak=0.9):
    m = np.max(np.abs(x)) or 1.0
    return x / m * peak


def fade(x, fin=0.002, fout=0.01):
    n = len(x)
    a, b = int(SR * fin), int(SR * fout)
    if a:
        x[:a] *= np.linspace(0, 1, a)
    if b:
        x[-b:] *= np.linspace(1, 0, b)
    return x


def write(path, x, peak=0.9):
    full = os.path.join(OUT, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    x = fade(norm(np.nan_to_num(x), peak))
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(full, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


# ================================================================== weapons

def gunshot(body_hz=90, crack=1.0, tail=0.35, dur=0.6, bright=4000, punch=1.0):
    n = int(SR * dur)
    thump = sine(lambda t: body_hz * (1 + 2.5 * np.exp(-t * 60)), dur) * exp_env(n, 0.06) * punch
    crackn = hp(noise(dur), 2500) * exp_env(n, 0.008) * crack
    blast = lp(noise(dur), bright) * exp_env(n, 0.05)
    tailn = lp(noise(dur), 1200) * exp_env(n, tail) * 0.35
    x = thump * 0.9 + crackn * 0.8 + blast * 0.9 + tailn
    return reverb(np.tanh(x * 2.2), room=0.4, decay=0.3, wet=0.3)


def suppressed(dur=0.25, hz=700):
    n = int(SR * dur)
    puff = bp(noise(dur), 300, 2200) * exp_env(n, 0.035)
    click = hp(noise(dur), 3000) * exp_env(n, 0.004) * 0.6
    mech = sine(hz * 2.3, dur) * exp_env(n, 0.02) * 0.2
    return puff + click + mech


def click(freq=3000, dur=0.03, tau=0.004, amt=1.0):
    n = int(SR * dur)
    return (hp(noise(dur), freq) * exp_env(n, tau) + sine(freq * 0.7, dur) * exp_env(n, tau) * 0.3) * amt


def metal_ping(freqs=(2100, 3350, 5200), dur=0.5, tau=0.12):
    n = int(SR * dur)
    x = sum(sine(f, dur) * exp_env(n, tau * (1 - i * 0.2)) / (i + 1) for i, f in enumerate(freqs))
    return x


def whoosh(dur=0.18, lo=400, hi=2500):
    n = int(SR * dur)
    x = noise(dur)
    e = np.sin(np.linspace(0, np.pi, n)) ** 2
    return bp(x, lo, hi) * e


def thud(freq=70, dur=0.25, tau=0.07):
    n = int(SR * dur)
    return sine(lambda t: freq * (1 + np.exp(-t * 30)), dur) * exp_env(n, tau) + lp(noise(dur), 400) * exp_env(n, tau * 0.6) * 0.6


def gen_weapons():
    write('SFX/pistol.wav', gunshot(110, 1.0, 0.3, 0.55, 4500))
    write('SFX/smg.wav', gunshot(130, 0.8, 0.18, 0.35, 5000, 0.8), 0.8)
    write('SFX/rifle.wav', gunshot(85, 1.3, 0.45, 0.8, 6000, 1.1))
    write('SFX/shotgun.wav', gunshot(55, 1.1, 0.6, 1.0, 3000, 1.5))
    write('SFX/pistol_sd.wav', suppressed(0.22, 650), 0.6)
    write('SFX/smg_sd.wav', suppressed(0.18, 800), 0.55)
    write('SFX/rifle_sd.wav', suppressed(0.28, 500), 0.65)
    write('SFX/knife.wav', whoosh(0.16, 900, 4000), 0.6)
    write('SFX/stab.wav', mixdown(thud(90, 0.2, 0.05), whoosh(0.08, 1500, 4000) * 0.4), 0.7)
    write('SFX/knife_hit.wav', mixdown(thud(100, 0.18, 0.04), click(2500, 0.05, 0.01, 0.3)), 0.7)
    write('SFX/throw.wav', whoosh(0.22, 600, 3000), 0.55)
    coin = mixdown(metal_ping((3100, 4700, 6900), 0.35, 0.08), pad_to(np.zeros(int(SR * 0.12)), 1))
    coin2 = np.concatenate([np.zeros(int(SR * 0.11)), metal_ping((3000, 4600, 6700), 0.3, 0.05) * 0.5])
    write('SFX/coin.wav', mixdown(coin, coin2), 0.6)
    write('SFX/dryfire.wav', click(2000, 0.06, 0.006), 0.5)
    mag_out = mixdown(click(1800, 0.08, 0.01), np.concatenate([np.zeros(int(SR * 0.05)), metal_ping((900, 1500), 0.12, 0.03) * 0.3]))
    mag_in = mixdown(click(1200, 0.1, 0.012, 1.0), np.concatenate([np.zeros(int(SR * 0.04)), click(2600, 0.06, 0.008, 0.8)]))
    write('SFX/reload_start.wav', mag_out, 0.6)
    write('SFX/reload_done.wav', mixdown(mag_in, np.concatenate([np.zeros(int(SR * 0.12)), click(3000, 0.05, 0.005, 0.9)])), 0.6)
    write('SFX/shell_insert.wav', mixdown(click(900, 0.1, 0.02), metal_ping((1300, 2100), 0.1, 0.02) * 0.3), 0.55)
    write('SFX/pump.wav', mixdown(click(700, 0.12, 0.03), np.concatenate([np.zeros(int(SR * 0.1)), click(1100, 0.1, 0.02)])), 0.6)
    write('SFX/casing.wav', mixdown(metal_ping((5200, 7400), 0.15, 0.03), np.concatenate([np.zeros(int(SR * 0.07)), metal_ping((5000, 7100), 0.1, 0.02) * 0.4])), 0.25)
    write('SFX/shell_drop.wav', mixdown(thud(300, 0.1, 0.02) * 0.4, metal_ping((2500, 3600), 0.12, 0.03) * 0.3), 0.3)
    write('SFX/switch.wav', mixdown(click(1500, 0.07, 0.01), np.concatenate([np.zeros(int(SR * 0.05)), click(900, 0.06, 0.01, 0.6)])), 0.4)
    write('SFX/impact.wav', mixdown(click(2000, 0.1, 0.01), lp(noise(0.12), 2500) * exp_env(int(SR * 0.12), 0.02) * 0.6), 0.5)
    write('SFX/impact_metal.wav', mixdown(click(3000, 0.05, 0.005), metal_ping((1800, 2900, 4100), 0.3, 0.06) * 0.6), 0.5)
    write('SFX/hit.wav', thud(80, 0.2, 0.05), 0.7)
    write('SFX/ricochet.wav', sine(lambda t: 3500 - t * 6000, 0.25) * exp_env(int(SR * 0.25), 0.07), 0.3)


def gen_world():
    # footsteps: two variants per surface
    for surf in ('hard', 'wood', 'carpet', 'tile', 'grass', 'gravel', 'metal'):
        for v in range(2):
            d = 0.12
            n = int(SR * d)
            if surf == 'hard':
                x = click(1800 + v * 200, d, 0.012, 0.6) + thud(120, d, 0.02) * 0.5
            elif surf == 'wood':
                x = thud(180 + v * 20, d, 0.03) + bp(noise(d), 400, 1500) * exp_env(n, 0.02) * 0.5
            elif surf == 'carpet':
                x = lp(noise(d), 600) * exp_env(n, 0.025) + thud(90, d, 0.02) * 0.3
            elif surf == 'tile':
                x = click(3000 + v * 300, d, 0.008, 0.8) + thud(150, d, 0.015) * 0.3
            elif surf == 'grass':
                x = bp(noise(0.16), 1500, 6000) * exp_env(int(SR * 0.16), 0.05) * 0.8
            elif surf == 'gravel':
                g = np.zeros(int(SR * 0.16))
                for _ in range(25):
                    i = rng.integers(0, len(g) - 200)
                    g[i:i + 200] += hp(noise(200 / SR), 2000) * exp_env(200, 0.002)
                x = g * np.linspace(1, 0.2, len(g))
            else:
                x = metal_ping((600 + v * 50, 1450, 2700), 0.2, 0.04) * 0.6 + thud(140, 0.2, 0.02) * 0.4
            write(f'SFX/step_{surf}_{v}.wav', x, 0.5)
    creak = sine(lambda t: 280 + 60 * np.sin(t * 30) + 40 * t, 0.45) * env(int(SR * 0.45), 0.05, 0.2, 0.3)
    write('SFX/door_open.wav', mixdown(lp(creak + saw(140, 0.45) * 0.1, 1800) * 0.6, click(800, 0.1, 0.02)), 0.5)
    write('SFX/door_close.wav', mixdown(thud(75, 0.35, 0.08), click(1200, 0.08, 0.01, 0.6)), 0.7)
    rattle = np.concatenate([click(1500, 0.07, 0.01) for _ in range(4)])
    write('SFX/door_locked.wav', rattle, 0.5)
    write('SFX/door_unlock.wav', np.concatenate([sine(1200, 0.08) * 0.6, sine(1800, 0.12) * 0.6]) + pad_to(click(1500, 0.1, 0.01), int(SR * 0.2)), 0.5)
    rumble = lp(noise(1.2), 300) * env(int(SR * 1.2), 0.2, 0.6, 0.2)
    write('SFX/secret.wav', rumble + sine(55, 1.2) * env(int(SR * 1.2), 0.3, 0.5) * 0.4, 0.7)
    glass = np.zeros(int(SR * 0.8))
    for _ in range(40):
        i = rng.integers(0, int(SR * 0.4))
        p = metal_ping((rng.uniform(3000, 7000), rng.uniform(5000, 9000)), 0.25, rng.uniform(0.02, 0.06)) * rng.uniform(0.2, 0.6)
        glass[i:i + len(p)] += p[:len(glass) - i]
    glass += hp(noise(0.8), 2500) * exp_env(int(SR * 0.8), 0.08)
    write('SFX/glass.wav', glass, 0.7)
    n = int(SR * 2.2)
    boom = sine(lambda t: 45 + 60 * np.exp(-t * 8), 2.2) * exp_env(n, 0.5) + lp(noise(2.2), 900) * exp_env(n, 0.45) + hp(noise(2.2), 2000) * exp_env(n, 0.03)
    write('SFX/explosion.wav', np.tanh(reverb(boom * 1.8, 0.8, 0.6, 0.35)), 0.95)
    write('SFX/body_fall.wav', mixdown(thud(60, 0.4, 0.08), np.concatenate([np.zeros(int(SR * 0.12)), thud(70, 0.3, 0.05) * 0.5])), 0.7)
    write('SFX/drag.wav', lp(noise(0.5), 900) * env(int(SR * 0.5), 0.1, 0.3, 0.5) * 0.7, 0.4)
    write('SFX/vault.wav', mixdown(whoosh(0.25, 300, 1500), np.concatenate([np.zeros(int(SR * 0.3)), thud(90, 0.25, 0.05)])), 0.6)
    write('SFX/hide.wav', mixdown(lp(creak, 1400) * 0.5, np.concatenate([np.zeros(int(SR * 0.3)), thud(110, 0.2, 0.04)])), 0.5)
    write('SFX/pickup.wav', np.concatenate([sine(880, 0.05), sine(1320, 0.08)]) * 0.6 * env(int(SR * 0.13), 0.003, 0.08, 0.5), 0.45)
    jingle = mixdown(*[np.concatenate([np.zeros(int(SR * k * 0.045)), metal_ping((rng.uniform(3500, 5500), rng.uniform(6000, 8000)), 0.25, 0.05)]) for k in range(6)])
    write('SFX/cash.wav', jingle, 0.5)
    write('SFX/keycard.wav', np.concatenate([sine(1500, 0.06), np.zeros(int(SR * 0.03)), sine(2200, 0.1)]) * 0.5, 0.45)
    write('SFX/medkit.wav', mixdown(whoosh(0.3, 2000, 7000) * 0.6, np.concatenate([np.zeros(int(SR * 0.3)), sine(660, 0.1), sine(990, 0.15)])), 0.5)
    write('SFX/terminal.wav', np.concatenate([square(1000, 0.04, 0.3), np.zeros(int(SR * 0.03)), square(1400, 0.04, 0.3)]) * 0.3, 0.35)
    write('SFX/terminal_done.wav', np.concatenate([square(f, 0.07, 0.3) for f in (880, 1175, 1760)]) * 0.35, 0.45)
    pdn = sine(lambda t: 220 * np.exp(-t * 2.5), 0.9) * env(int(SR * 0.9), 0.01, 0.4) + sine(lambda t: 60 * np.exp(-t), 0.9) * 0.4
    write('SFX/power_off.wav', mixdown(pdn, click(900, 0.1, 0.02)), 0.6)
    write('SFX/power_on.wav', sine(lambda t: 60 + 200 * (1 - np.exp(-t * 3)), 0.8) * env(int(SR * 0.8), 0.1, 0.5) * 0.7 + pad_to(click(900, 0.1, 0.02), int(SR * 0.8)), 0.55)
    write('SFX/camera_alarm.wav', np.concatenate([square(1650, 0.12, 0.5), np.zeros(int(SR * 0.06)), square(1650, 0.12, 0.5), np.zeros(int(SR * 0.06)), square(1650, 0.12, 0.5)]) * 0.35, 0.5)
    static = bp(noise(0.5), 800, 3500) * (0.5 + 0.5 * np.sign(np.sin(t_axis(0.5) * 90)))
    write('SFX/radio.wav', mixdown(static * 0.6, np.concatenate([sine(1000, 0.08), np.zeros(int(SR * 0.35)), sine(1000, 0.06)]) * 0.4), 0.5)
    write('SFX/alarm_loop.wav', np.concatenate([sine(lambda t: 700 + 300 * np.sin(t * 2 * np.pi * 1.25), 1.6)]) * 0.5, 0.35)
    hb = mixdown(thud(50, 0.18, 0.05), np.concatenate([np.zeros(int(SR * 0.22)), thud(45, 0.18, 0.05) * 0.8]))
    write('SFX/heartbeat.wav', pad_to(hb, int(SR * 0.9)), 0.8)
    write('SFX/player_hurt.wav', mixdown(thud(70, 0.25, 0.06), lp(noise(0.2), 700) * exp_env(int(SR * 0.2), 0.04) * 0.6), 0.75)
    write('SFX/death.wav', mixdown(thud(50, 0.8, 0.2), sine(lambda t: 220 * np.exp(-t * 1.5), 1.2) * env(int(SR * 1.2), 0.01, 0.5) * 0.5), 0.8)
    for kind, f in (('radio', 440), ('piano', 262), ('vending', 120), ('printer', 180)):
        d = 2.0
        if kind == 'radio':
            x = bp(noise(d), 500, 3000) * 0.3 + sum(sine(fr, d) for fr in (440, 554, 659)) * 0.15 * (0.5 + 0.5 * np.sin(t_axis(d) * 6))
        elif kind == 'piano':
            x = np.zeros(int(SR * d))
            for k, note in enumerate((262, 311, 392, 466, 392, 311, 262, 196)):
                s = int(SR * k * 0.25)
                tone = (sine(note, 1.0) + sine(note * 2, 1.0) * 0.4 + sine(note * 3, 1.0) * 0.15) * exp_env(int(SR * 1.0), 0.35)
                x[s:s + len(tone)] += tone[:len(x) - s]
        elif kind == 'vending':
            x = sine(120, d) * 0.4 + sine(240, d) * 0.2 + lp(noise(d), 500) * 0.3
        else:
            x = square(180, d, 0.3) * 0.2 * (0.5 + 0.5 * np.sign(np.sin(t_axis(d) * 20))) + hp(noise(d), 2000) * 0.2
        write(f'SFX/distraction_{kind}.wav', x, 0.5)


def gen_ui():
    write('SFX/ui_click.wav', click(2500, 0.05, 0.006) + sine(1200, 0.05) * exp_env(int(SR * 0.05), 0.01) * 0.5, 0.4)
    write('SFX/ui_hover.wav', sine(1800, 0.03) * exp_env(int(SR * 0.03), 0.008), 0.2)
    write('SFX/ui_back.wav', np.concatenate([sine(900, 0.04), sine(600, 0.06)]) * exp_env(int(SR * 0.1), 0.05), 0.35)
    write('SFX/ui_buy.wav', mixdown(np.concatenate([sine(1047, 0.06), sine(1319, 0.06), sine(1568, 0.18)]) * 0.5 * env(int(SR * 0.3), 0.003, 0.2, 0.3),
                                     metal_ping((4200, 6300), 0.4, 0.08) * 0.3), 0.5)
    write('SFX/ui_error.wav', square(150, 0.25, 0.5) * env(int(SR * 0.25), 0.005, 0.2, 0.5) * 0.4, 0.45)
    arp = np.concatenate([(sine(f, 0.09) + sine(f * 2, 0.09) * 0.3) * exp_env(int(SR * 0.09), 0.06) for f in (523, 659, 784, 1047)])
    write('SFX/objective.wav', mixdown(arp, np.concatenate([np.zeros(int(SR * 0.3)), (sine(1047, 0.6) + sine(1319, 0.6) * 0.6) * exp_env(int(SR * 0.6), 0.25)])), 0.55)
    hit = mixdown(lp(noise(1.2), 900) * exp_env(int(SR * 1.2), 0.2), sum(sine(f, 1.2) for f in (146.8, 155.6, 220)) * exp_env(int(SR * 1.2), 0.5) * 0.4)
    write('SFX/spotted.wav', np.tanh(hit * 2), 0.7)
    write('SFX/body_found.wav', np.tanh(mixdown(sum(sine(f, 1.0) for f in (110, 116.5, 164.8)) * exp_env(int(SR * 1.0), 0.4) * 0.5, lp(noise(0.5), 500) * exp_env(int(SR * 0.5), 0.1)) * 1.5), 0.6)


# ================================================================== voices (formant "gibberish" with emotion)

FORMANTS = {'a': (730, 1090, 2440), 'e': (530, 1840, 2480), 'i': (390, 1990, 2550), 'o': (570, 840, 2410), 'u': (440, 1020, 2240)}


def voice(pitch, syllables, contour, rate=0.11, breath=0.08, rough=0.0):
    out = []
    for k, vowel in enumerate(syllables):
        d = rate * rng.uniform(0.8, 1.3)
        n = int(SR * d)
        f0 = pitch * contour(k / max(1, len(syllables) - 1)) * rng.uniform(0.95, 1.05)
        t = t_axis(d)
        vib = 1 + 0.02 * np.sin(2 * np.pi * 5.5 * t)
        phase = np.cumsum(f0 * vib) / SR
        src = 2 * (phase % 1.0) - 1
        src += noise(d) * (breath + rough)
        y = np.zeros(n)
        for i, f in enumerate(FORMANTS[vowel]):
            y += bp(src, f * 0.85, f * 1.15) / (i + 1)
        y *= np.sin(np.linspace(0, np.pi, n)) ** 0.6
        # consonant burst
        c = hp(noise(0.02), 2500) * exp_env(int(SR * 0.02), 0.004) * 0.4
        out.append(np.concatenate([c, y]))
        out.append(np.zeros(int(SR * rng.uniform(0.0, 0.03))))
    return np.concatenate(out)


def gen_voices():
    types = {'guard': 115, 'elite': 92, 'civ': 195}
    cats = {
        'alert': (lambda p: 1.25 - 0.1 * p, 3, 0.09, 0.1),
        'question': (lambda p: 0.95 + 0.35 * p, 2, 0.13, 0.05),
        'calm': (lambda p: 1.0 - 0.1 * p, 3, 0.12, 0.03),
        'pain': (lambda p: 1.5 - 0.5 * p, 1, 0.18, 0.35),
        'panic': (lambda p: 1.45 + 0.1 * np.sin(p * 9), 5, 0.07, 0.15),
        'report': (lambda p: 1.2 - 0.05 * p, 5, 0.09, 0.08),
    }
    vowels = list(FORMANTS)
    for vt, pitch in types.items():
        for cat, (contour, syl, rate, rough) in cats.items():
            for v in range(2):
                sy = [vowels[rng.integers(0, 5)] for _ in range(syl + rng.integers(0, 2))]
                x = voice(pitch, sy, contour, rate, 0.06, rough)
                x = reverb(lp(x, 3800), 0.1, 0.15, 0.15)
                write(f'Voice/{vt}_{cat}_{v}.wav', x, 0.55)


# ================================================================== music

BPM = 92
BEAT = 60.0 / BPM
BARS = 16
LOOP = BARS * 4 * BEAT

# D minor progression: Dm - Bb - Gm - A (4 bars each chord x4 bars)
CHORDS = [(62, 65, 69), (58, 62, 65), (55, 58, 62), (57, 61, 64)]


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def place(buf, x, start):
    s = int(start * SR)
    if s >= len(buf):
        return
    e = min(len(buf), s + len(x))
    buf[s:e] += x[:e - s]


def synth_pad(freq, dur):
    x = sum(saw(freq * d, dur) for d in (1.0, 1.004, 0.996)) / 3
    x = lp(x, 900 + 300 * 1)
    return x * env(len(x), 0.8, 2.0, 0.7)


def synth_pluck(freq, dur=0.5):
    x = (saw(freq, dur) + square(freq * 2, dur, 0.3) * 0.3)
    return lp(x, 2500) * exp_env(len(x), 0.12)


def synth_bass(freq, dur):
    x = sine(freq, dur) + saw(freq, dur) * 0.3
    return lp(x, 400) * env(len(x), 0.01, dur * 0.6, 0.4)


def kick():
    return sine(lambda t: 45 + 110 * np.exp(-t * 35), 0.35) * exp_env(int(SR * 0.35), 0.12)


def snare():
    n = int(SR * 0.25)
    return (bp(noise(0.25), 1200, 6000) * exp_env(n, 0.06) + sine(190, 0.25) * exp_env(n, 0.04) * 0.5)


def hat(open_=False):
    d = 0.2 if open_ else 0.05
    return hp(noise(d), 7000) * exp_env(int(SR * d), 0.05 if open_ else 0.012)


def gen_music():
    n = int(SR * LOOP)
    bar = 4 * BEAT
    calm = np.zeros(n)
    tension = np.zeros(n)
    combat = np.zeros(n)
    for b in range(BARS):
        chord = CHORDS[(b // 4) % 4]
        t0 = b * bar
        if b % 2 == 0:
            for m in chord:
                place(calm, synth_pad(mtof(m - 12), bar * 2) * 0.22, t0)
        place(calm, synth_bass(mtof(chord[0] - 24), bar) * 0.5, t0)
        # sparse melody plucks
        for k, step in enumerate((0, 3, 6, 10, 13)):
            if (b + k) % 3 == 0:
                note = chord[k % 3] + (12 if k % 2 else 0)
                place(calm, synth_pluck(mtof(note), 0.6) * 0.16, t0 + step * BEAT / 4)
        # tension: pulsing bass eighths + ticking hats + high dissonant drone
        for e in range(8):
            place(tension, synth_bass(mtof(chord[0] - 24 + (7 if e % 4 == 3 else 0)), BEAT / 2 * 0.9) * 0.45, t0 + e * BEAT / 2)
            place(tension, hat() * 0.18, t0 + e * BEAT / 2 + BEAT / 4)
        if b % 4 == 0:
            drone = (sine(mtof(chord[0] + 13), bar * 4) * 0.5 + sine(mtof(chord[0] + 12), bar * 4) * 0.5) * env(int(SR * bar * 4), 1.5, 3, 0.6)
            place(tension, lp(drone, 3000) * 0.08, t0)
        # combat: drums + driving bass 16ths + stabs
        for q in range(4):
            place(combat, kick() * 0.7, t0 + q * BEAT)
            if q % 2 == 1:
                place(combat, snare() * 0.45, t0 + q * BEAT)
            place(combat, hat() * 0.2, t0 + q * BEAT + BEAT / 2)
        if b % 2 == 1:
            place(combat, kick() * 0.5, t0 + 3.5 * BEAT)
        for s in range(16):
            note = chord[0] - 24 + (12 if s % 4 == 2 else 0)
            place(combat, synth_bass(mtof(note), BEAT / 4 * 0.8) * 0.4, t0 + s * BEAT / 4)
        for q in (0, 1.5, 2.5):
            stab = sum(saw(mtof(m), 0.2) for m in chord) / 3
            place(combat, lp(stab, 2200) * exp_env(len(stab), 0.08) * 0.22, t0 + q * BEAT)
    write('Music/calm.wav', calm, 0.6)
    write('Music/tension.wav', tension, 0.6)
    write('Music/combat.wav', np.tanh(combat * 1.3), 0.7)

    # menu theme: slow arpeggios + pad + rain
    mbpm = 70
    mbeat = 60.0 / mbpm
    mlen = 16 * 4 * mbeat
    menu = np.zeros(int(SR * mlen))
    for b in range(16):
        chord = CHORDS[(b // 2) % 4]
        t0 = b * 4 * mbeat
        if b % 2 == 0:
            for m in chord:
                place(menu, synth_pad(mtof(m - 12), 8 * mbeat) * 0.2, t0)
            place(menu, synth_bass(mtof(chord[0] - 24), 8 * mbeat) * 0.35, t0)
        for k in range(8):
            place(menu, synth_pluck(mtof(chord[k % 3] + 12 * (k // 3 % 2)), 0.8) * 0.12, t0 + k * mbeat / 2)
    menu += lp(noise(mlen), 3000) * 0.02
    write('Music/menu.wav', menu, 0.6)

    # stings
    win = np.zeros(int(SR * 5))
    for k, ch in enumerate(((62, 65, 69), (58, 62, 65), (60, 64, 67), (62, 66, 69))):
        for m in ch:
            place(win, synth_pad(mtof(m), 2.2) * 0.25, k * 0.55)
            place(win, synth_pluck(mtof(m + 12), 0.8) * 0.15, k * 0.55)
    write('Music/victory.wav', reverb(win, 0.5, 0.5, 0.3), 0.65)
    lose = np.zeros(int(SR * 4))
    for k, m in enumerate((62, 61, 58, 50)):
        place(lose, synth_pad(mtof(m - 12), 2.5) * 0.3, k * 0.6)
    lose += sine(lambda t: 55 * np.exp(-t * 0.2), 4) * env(int(SR * 4), 0.5, 2) * 0.3
    write('Music/gameover.wav', reverb(lose, 0.5, 0.6, 0.3), 0.65)


def gen_ambience():
    d = 20.0
    n = int(SR * d)
    # mansion: crickets + distant party murmur
    crick = np.zeros(n)
    for _ in range(60):
        s = rng.uniform(0, d - 0.5)
        chirp = sine(rng.uniform(4200, 5200), 0.25) * (0.5 + 0.5 * np.sign(np.sin(t_axis(0.25) * 2 * np.pi * 30))) * env(int(SR * 0.25), 0.01, 0.1, 0.4)
        place(crick, chirp * rng.uniform(0.05, 0.15), s)
    party = lp(noise(d), 500) * 0.15
    for k in range(int(d / BEAT)):
        place(party, kick() * 0.08, k * BEAT)
    write('Ambience/mansion.wav', crick + party + lp(noise(d), 1500) * 0.03, 0.45)
    # facility: hum + vents
    hum = sine(60, d) * 0.3 + sine(120, d) * 0.15 + lp(noise(d), 300) * 0.4
    for _ in range(8):
        place(hum, metal_ping((rng.uniform(300, 900), rng.uniform(1200, 2000)), 1.0, 0.3) * 0.05, rng.uniform(0, d - 1))
    write('Ambience/facility.wav', hum, 0.4)
    # office: city traffic + AC
    city = lp(noise(d), 700) * 0.5 + sine(50, d) * 0.1
    for _ in range(6):
        s = rng.uniform(0, d - 3)
        car = lp(noise(3), 900) * np.sin(np.linspace(0, np.pi, int(SR * 3))) ** 2 * 0.4
        place(city, car, s)
    for _ in range(3):
        place(city, sine(lambda t: 500 + 50 * np.sin(t * 8), 1.2) * env(int(SR * 1.2), 0.05, 0.6) * 0.04, rng.uniform(0, d - 2))
    write('Ambience/office.wav', city, 0.4)
    write('Ambience/rain.wav', hp(lp(noise(d), 6000), 500) * 0.5 + lp(noise(d), 300) * 0.2, 0.35)


if __name__ == '__main__':
    gen_weapons()
    gen_world()
    gen_ui()
    gen_voices()
    gen_music()
    gen_ambience()
    count = sum(len(f) for _, _, f in os.walk(OUT))
    print('audio generated:', count, 'files')
