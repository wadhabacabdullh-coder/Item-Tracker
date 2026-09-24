"""Generates every non-map sprite: characters, weapons, items, effects, doors, UI icons, menu backdrop.

All characters face RIGHT (+x); the game rotates them. Sheets are sliced at runtime (fixed cell sizes),
so no import-time slicing is needed. See Assets/Scripts/Game/Visual/SpriteLibrary.cs for the layout contract.
"""
import math
import os
from pix import Canvas, P, hexc, shade, mix, rng_for

ROOT = os.path.join(os.path.dirname(__file__), '..', '..')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'Sprites')
OUTLINE = (8, 10, 14, 255)


def save(c, *path):
    full = os.path.join(OUT, *path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    c.save(full)


# ================================================================== characters

CHARACTERS = {
    #            torso        shoulders/arms  head/hair     skin         legs         accent
    'player':    (hexc('23272f'), hexc('191c22'), hexc('15171b'), P['skin1'], hexc('15171b'), P['red3']),
    'guard':     (P['blue1'], P['blue0'], P['blue0'], P['skin1'], hexc('1a2233'), P['gold2']),
    'elite':     (P['steel'], P['slate'], P['iron'], P['skin2'], P['night'], P['red3']),
    'staff':     (P['snow'], P['ink'], P['wood1'], P['skin0'], P['ink'], P['ink']),
    'guest':     (P['pur1'], P['pur0'], P['gold1'], P['skin0'], P['ink'], P['gold3']),
    'guest2':    (P['red2'], P['red1'], P['wood0'], P['skin2'], P['ink'], P['gold3']),
    'scientist': (P['coat'], P['mist'], P['wood2'], P['skin1'], P['slate'], P['teal3']),
    'worker':    (P['blue3'], P['blue2'], P['wood1'], P['skin2'], P['slate'], P['snow']),
    'target':    (P['cream'], P['sand'], P['ash'], P['skin0'], P['sand2'], P['gold3']),
}

POSES = ['unarmed', 'onehand', 'twohand', 'knife', 'dead', 'hidden']
CELL = 24


def draw_body(c, ox, oy, pal, pose, kind):
    torso, arms, hair, skin, legs, accent = pal
    cx, cy = ox + 11, oy + 12
    if pose == 'dead':
        # lying on the back, head to the left, arms out
        c.ellipse(cx + 2, cy, 6, 4, torso)
        c.rect(cx - 2, cy - 1, 8, 2, shade(torso, 0.85))
        c.ellipse(cx + 8, cy - 2, 3, 1.5, legs); c.ellipse(cx + 8, cy + 2, 3, 1.5, legs)
        c.disc(cx - 6, cy, 3.2, skin); c.disc(cx - 7, cy, 2.6, hair)
        c.disc(cx, cy - 6, 1.6, skin); c.disc(cx + 1, cy + 6, 1.6, skin)
        c.line(cx, cy - 5, cx + 1, cy - 3, arms); c.line(cx + 1, cy + 5, cx + 2, cy + 3, arms)
        c.px(cx + 2, cy, accent)
        c.outline_alpha(OUTLINE)
        return
    if pose == 'hidden':
        c.disc(cx, cy, 5, (255, 255, 255, 60))
        return
    # hands / arms first (under the torso edge)
    if pose == 'unarmed':
        c.disc(cx + 3, cy - 5, 1.7, skin); c.disc(cx + 3, cy + 5, 1.7, skin)
    elif pose == 'onehand':
        c.line(cx + 1, cy - 4, cx + 6, cy - 1, arms); c.line(cx + 1, cy + 4, cx + 6, cy + 1, arms)
        c.line(cx + 1, cy - 3, cx + 6, cy - 1, arms); c.line(cx + 1, cy + 3, cx + 6, cy + 1, arms)
        c.disc(cx + 7, cy, 1.6, skin)
    elif pose == 'twohand':
        c.line(cx + 1, cy + 4, cx + 5, cy + 2, arms); c.line(cx + 1, cy + 3, cx + 5, cy + 1, arms)
        c.line(cx + 1, cy - 4, cx + 8, cy - 1, arms); c.line(cx + 1, cy - 3, cx + 8, cy, arms)
        c.disc(cx + 5, cy + 2, 1.5, skin); c.disc(cx + 9, cy, 1.5, skin)
    elif pose == 'knife':
        c.line(cx + 1, cy + 4, cx + 6, cy + 4, arms); c.line(cx + 1, cy + 3, cx + 6, cy + 3, arms)
        c.disc(cx + 7, cy + 4, 1.6, skin)
        c.disc(cx + 3, cy - 5, 1.6, skin)
    # torso (shoulders span vertically because we face right)
    c.ellipse(cx - 1, cy, 4.2, 6.2, arms)
    c.ellipse(cx - 1, cy, 3.4, 5.4, torso)
    c.rect(cx - 3, cy - 1, 2, 2, shade(torso, 0.8))
    # outfit details
    if kind == 'player':
        c.rect(cx, cy - 1, 2, 3, P['snow']); c.px(cx + 1, cy, accent); c.px(cx + 1, cy + 1, accent)
    elif kind == 'guard':
        c.px(cx - 1, cy - 4, accent); c.px(cx + 1, cy + 3, P['snow'])
    elif kind == 'elite':
        c.rect(cx - 3, cy - 3, 5, 7, P['slate']); c.rect(cx - 2, cy - 2, 3, 5, shade(P['slate'], 1.2))
        c.px(cx + 1, cy - 4, accent)
    elif kind == 'staff':
        c.rect(cx - 3, cy - 4, 2, 9, P['ink'])
        c.px(cx + 1, cy, P['ink'])
    elif kind == 'scientist':
        c.rect(cx - 1, cy - 1, 2, 3, P['blue3']); c.px(cx + 1, cy - 3, accent)
    elif kind == 'target':
        c.rect(cx, cy - 1, 2, 3, P['snow']); c.px(cx + 1, cy, accent); c.px(cx - 2, cy - 3, accent)
    elif kind.startswith('guest'):
        c.px(cx, cy - 2, accent); c.px(cx, cy + 2, accent)
    # head
    hx = cx + 1
    c.disc(hx, cy, 3.3, skin)
    if kind == 'guard':
        c.disc(hx - 0.5, cy, 3.1, hair); c.rect(hx + 1, cy - 2, 2, 4, shade(hair, 1.3))  # cap + visor
    elif kind == 'elite':
        c.disc(hx - 0.5, cy, 3.3, hair); c.rect(hx + 1, cy - 2, 2, 4, P['ink']); c.px(hx + 2, cy - 1, P['teal3'])
    elif kind == 'player':
        c.disc(hx - 0.8, cy, 2.9, shade(skin, 0.85)); c.px(hx - 1, cy - 1, shade(skin, 1.1))   # shaved head
    else:
        c.disc(hx - 1, cy, 3.0, hair)
        if kind in ('guest', 'target', 'guest2'):
            c.px(hx - 3, cy - 2, hair); c.px(hx - 3, cy + 2, hair)
    c.outline_alpha(OUTLINE)


def draw_legs(c, ox, oy, pal, frame):
    torso, arms, hair, skin, legs, accent = pal
    cx, cy = ox + 11, oy + 12
    swing = [0, 3, 0, -3][frame]
    c.ellipse(cx + swing, cy - 3, 2.6, 1.6, legs)
    c.ellipse(cx - swing, cy + 3, 2.6, 1.6, legs)
    c.px(cx + swing + 2, cy - 3, shade(legs, 0.6)); c.px(cx - swing + 2, cy + 3, shade(legs, 0.6))


def gen_characters():
    for kind, pal in CHARACTERS.items():
        sheet = Canvas(CELL * len(POSES), CELL * 2)
        for i, pose in enumerate(POSES):
            cell = Canvas(CELL, CELL)
            draw_body(cell, 0, 0, pal, pose, kind)
            sheet.blit(cell, i * CELL, 0)
        for f in range(4):
            cell = Canvas(CELL, CELL)
            draw_legs(cell, 0, 0, pal, f)
            cell.outline_alpha(OUTLINE)
            sheet.blit(cell, f * CELL, CELL)
        save(sheet, 'Characters', kind + '.png')


# ================================================================== weapons (held, top-down, facing right)

def held_weapon(wid):
    c = Canvas(20, 8)
    m, d, l = P['steel'], P['ink'], P['ash']
    if wid in ('knife', 'combat_knife', 'throwing_knives'):
        blade = P['mist'] if wid != 'combat_knife' else P['fog']
        n = 7 if wid == 'combat_knife' else 5
        c.rect(2, 3, 3, 2, P['wood1'] if wid == 'knife' else P['ink'])
        c.rect(5, 3, n, 2, blade); c.px(5 + n, 3, blade); c.rect(5, 4, n, 1, shade(blade, 0.8))
    elif wid == 'coin':
        c.disc(4, 4, 2, P['gold3']); c.px(3, 3, P['white'])
    elif wid in ('pistol', 'silenced_pistol'):
        c.rect(2, 3, 7, 2, d); c.rect(3, 3, 5, 1, l)
        if wid == 'silenced_pistol':
            c.rect(9, 3, 6, 2, P['night']); c.rect(9, 3, 6, 1, m)
    elif wid == 'smg':
        c.rect(1, 3, 11, 3, d); c.rect(2, 3, 9, 1, l); c.rect(12, 3, 3, 2, d); c.rect(5, 5, 2, 3, d)
    elif wid == 'shotgun':
        c.rect(0, 3, 16, 2, P['wood2']); c.rect(6, 3, 12, 1, d); c.rect(6, 4, 12, 1, m); c.rect(9, 5, 4, 1, P['wood3'])
    elif wid == 'assault_rifle':
        c.rect(0, 3, 17, 2, d); c.rect(3, 3, 12, 1, l); c.rect(17, 3, 3, 1, d); c.rect(7, 5, 2, 3, d); c.rect(0, 2, 3, 4, d)
    c.outline_alpha((8, 10, 14, 200))
    return c


# ================================================================== icons (side view, for UI and pickups)

def icon_weapon(wid, w=48, h=24):
    c = Canvas(w, h)
    d, m, l, hi = P['ink'], P['steel'], P['iron'], P['fog']
    if wid == 'knife':
        c.rect(6, 11, 12, 4, P['wood2']); c.rect(7, 12, 10, 1, P['wood3'])
        c.rect(18, 10, 20, 4, P['mist']); c.rect(18, 13, 20, 1, P['fog']); c.line(38, 10, 42, 12, P['mist']); c.px(41, 12, P['mist'])
        c.rect(17, 9, 2, 7, P['gold1'])
    elif wid == 'combat_knife':
        c.rect(4, 10, 14, 6, d); c.rect(5, 11, 12, 1, m)
        for i in range(6, 17, 3): c.px(i, 13, l)
        c.rect(17, 7, 2, 11, m)
        c.rect(19, 9, 22, 5, P['fog']); c.rect(19, 9, 22, 1, P['white']); c.line(41, 9, 45, 12, P['fog']); c.rect(19, 13, 16, 1, P['ash'])
    elif wid == 'throwing_knives':
        for k, oy in enumerate((6, 12)):
            c.rect(8 + k * 4, oy + 1, 8, 3, d)
            c.rect(16 + k * 4, oy, 18, 4, P['mist']); c.line(34 + k * 4, oy, 38 + k * 4, oy + 2, P['mist'])
            c.circle = None
    elif wid == 'coin':
        c.disc(24, 12, 8, P['gold2']); c.disc(24, 12, 6, P['gold3']); c.rect(22, 9, 4, 6, P['gold2'])
    elif wid in ('pistol', 'silenced_pistol'):
        c.rect(10, 7, 20, 5, d); c.rect(11, 7, 18, 2, m); c.rect(12, 12, 6, 9, d); c.rect(13, 12, 4, 8, P['night'])
        c.rect(19, 12, 4, 3, d); c.px(28, 6, hi)
        if wid == 'silenced_pistol':
            c.rect(30, 7, 14, 5, P['night']); c.rect(30, 7, 14, 1, m); c.rect(30, 11, 14, 1, d)
    elif wid == 'smg':
        c.rect(6, 7, 26, 7, d); c.rect(7, 7, 24, 2, m); c.rect(32, 9, 8, 3, d)
        c.rect(16, 14, 5, 9, d); c.rect(11, 14, 5, 5, d); c.rect(0, 9, 6, 3, d); c.rect(0, 9, 2, 6, d)
    elif wid == 'shotgun':
        c.rect(0, 10, 14, 5, P['wood2']); c.rect(1, 10, 12, 1, P['wood3']); c.rect(0, 11, 4, 8, P['wood2'])
        c.rect(14, 9, 32, 3, d); c.rect(14, 9, 32, 1, m); c.rect(20, 12, 20, 3, P['night']); c.rect(24, 12, 10, 3, P['wood3'])
        c.rect(14, 12, 6, 3, d)
    elif wid == 'assault_rifle':
        c.rect(0, 8, 8, 6, d); c.rect(8, 9, 4, 3, d)
        c.rect(12, 7, 24, 6, d); c.rect(13, 7, 22, 2, m); c.rect(36, 9, 11, 2, d); c.rect(44, 7, 2, 2, d)
        c.rect(22, 13, 5, 9, d); c.rect(15, 13, 4, 6, d); c.rect(18, 4, 10, 3, P['night']); c.px(27, 5, P['teal3'])
    c.outline_alpha((8, 10, 14, 180))
    return c


def icon_misc(name, s=24):
    c = Canvas(s, s)
    cx, cy = s // 2, s // 2
    if name.startswith('ammo_'):
        kind = name[5:]
        if kind == 'pistol':
            for i in range(3):
                x = 5 + i * 5
                c.rect(x, 9, 4, 10, P['gold2']); c.rect(x, 9, 4, 1, P['gold3']); c.rect(x, 5, 4, 4, P['wood3']); c.px(x + 1, 5, P['wood4'])
        elif kind == 'rifle':
            for i in range(3):
                x = 5 + i * 5
                c.rect(x, 8, 4, 13, P['gold2']); c.rect(x + 1, 3, 2, 5, P['wood3']); c.rect(x, 20, 4, 1, P['gold1'])
        elif kind == 'shells':
            for i in range(3):
                x = 4 + i * 6
                c.rect(x, 5, 5, 11, P['red2']); c.rect(x, 5, 5, 1, P['red3']); c.rect(x, 16, 5, 4, P['gold2'])
        elif kind == 'knives':
            for i in range(3):
                c.line(5 + i * 5, 20, 5 + i * 5, 6, P['mist']); c.line(6 + i * 5, 20, 6 + i * 5, 6, P['fog']); c.rect(4 + i * 5, 17, 4, 4, P['ink'])
    elif name == 'armor_light' or name == 'armor_heavy':
        col = P['iron'] if name == 'armor_heavy' else P['blue2']
        c.rect(5, 4, 14, 17, col); c.rect(4, 4, 3, 6, col); c.rect(17, 4, 3, 6, col)
        c.rect(9, 3, 6, 3, (0, 0, 0, 0)); c.rect(7, 9, 10, 8, shade(col, 1.25))
        if name == 'armor_heavy':
            c.rect(7, 10, 4, 3, P['slate']); c.rect(13, 10, 4, 3, P['slate']); c.rect(7, 14, 10, 2, P['slate'])
    elif name == 'medkit':
        c.rect(3, 6, 18, 14, P['snow']); c.rect(3, 6, 18, 2, P['white']); c.rect(9, 3, 6, 4, P['ash'])
        c.rect(10, 9, 4, 9, P['red3']); c.rect(7, 12, 10, 3, P['red3'])
    elif name == 'coin':
        c.disc(cx, cy, 7, P['gold2']); c.disc(cx, cy, 5, P['gold3']); c.rect(cx - 1, cy - 3, 2, 6, P['gold1'])
    elif name == 'up_suppressor':
        c.rect(2, 9, 20, 7, P['night']); c.rect(2, 9, 20, 2, P['steel']); c.rect(2, 14, 20, 2, P['ink'])
        for i in range(5, 20, 4): c.px(i, 12, P['ash'])
    elif name == 'up_extendedmag':
        c.rect(8, 2, 8, 20, P['ink']); c.rect(9, 3, 6, 18, P['steel']); c.rect(9, 3, 6, 2, P['gold2'])
        for i in range(7, 20, 3): c.rect(9, i, 6, 1, P['iron'])
    elif name == 'up_stabilizer':
        c.rect(4, 10, 16, 4, P['ink']); c.rect(4, 10, 16, 1, P['steel']); c.rect(7, 14, 3, 7, P['ink']); c.rect(14, 14, 3, 7, P['ink'])
        c.rect(6, 20, 5, 2, P['steel']); c.rect(13, 20, 5, 2, P['steel'])
    elif name.startswith('keycard_'):
        col = P['blue3'] if name.endswith('blue') else P['red3']
        c.rect(3, 6, 18, 12, col); c.rect(3, 6, 18, 2, shade(col, 1.3)); c.rect(5, 10, 6, 5, P['gold3']); c.rect(13, 13, 6, 2, P['snow'])
    elif name == 'intel':
        c.rect(4, 5, 16, 15, P['sand']); c.rect(4, 5, 7, 2, P['sand2']); c.rect(6, 9, 11, 1, P['ink']); c.rect(6, 12, 9, 1, P['ink']); c.rect(6, 15, 10, 1, P['ink'])
        c.rect(14, 3, 6, 4, P['red3'])
    elif name == 'ledger':
        c.rect(5, 3, 14, 18, P['red1']); c.rect(5, 3, 3, 18, P['red0']); c.rect(10, 8, 6, 3, P['gold2'])
    elif name == 'samples':
        for i, col in enumerate((P['grn4'], P['teal3'], P['grn4'])):
            x = 5 + i * 5
            c.rect(x, 6, 4, 14, P['mist']); c.rect(x, 12, 4, 8, col); c.rect(x, 4, 4, 2, P['ink'])
    elif name == 'prototype':
        c.rect(4, 6, 16, 12, P['steel']); c.rect(5, 7, 14, 10, P['slate']); c.disc(12, 12, 3, P['teal3']); c.disc(12, 12, 1.5, P['white'])
        c.rect(3, 9, 2, 6, P['gold2']); c.rect(19, 9, 2, 6, P['gold2'])
    elif name == 'drive':
        c.rect(6, 4, 12, 16, P['ink']); c.rect(7, 5, 10, 10, P['steel']); c.rect(9, 16, 6, 2, P['teal3'])
    elif name == 'cash':
        c.rect(3, 8, 18, 9, P['grn3']); c.rect(3, 8, 18, 1, P['grn4']); c.rect(9, 8, 3, 9, P['snow']); c.disc(16, 12, 2, P['grn2'])
        c.rect(5, 5, 18, 9, shade(P['grn3'], 0.85)); c.rect(5, 5, 18, 1, P['grn4']); c.rect(11, 5, 3, 9, P['snow'])
    elif name == 'health':
        c.rect(9, 4, 6, 16, P['red3']); c.rect(4, 9, 16, 6, P['red3']); c.rect(10, 5, 2, 6, P['red4'])
    elif name == 'shield':
        c.rect(5, 4, 14, 10, P['blue3']); c.disc(12, 14, 7, P['blue3']); c.rect(7, 6, 5, 6, P['blue4'])
    elif name == 'money':
        c.disc(cx, cy, 8, P['gold2']); c.disc(cx, cy, 6, P['gold3'])
        c.rect(cx - 1, cy - 5, 2, 10, P['gold0']); c.rect(cx - 3, cy - 3, 6, 2, P['gold0']); c.rect(cx - 3, cy + 1, 6, 2, P['gold0'])
    elif name == 'eye':
        c.ellipse(cx, cy, 9, 5, P['snow']); c.disc(cx, cy, 3.5, P['ink']); c.disc(cx, cy, 2, P['red3']); c.px(cx - 1, cy - 1, P['white'])
    elif name == 'skull':
        c.disc(cx, cy - 2, 7, P['snow']); c.rect(cx - 4, cy + 3, 8, 5, P['snow'])
        c.disc(cx - 3, cy - 2, 2, P['ink']); c.disc(cx + 3, cy - 2, 2, P['ink']); c.rect(cx - 3, cy + 5, 1, 3, P['ink']); c.rect(cx + 2, cy + 5, 1, 3, P['ink'])
    elif name == 'clock':
        c.disc(cx, cy, 9, P['snow']); c.disc(cx, cy, 7, P['night']); c.rect(cx, cy - 5, 1, 6, P['snow']); c.rect(cx, cy, 5, 1, P['gold3'])
    elif name == 'target':
        c.ring(cx, cy, 10, P['red3'], 2); c.ring(cx, cy, 5, P['red3'], 2); c.rect(cx - 1, 0, 2, s, P['red3']); c.rect(0, cy - 1, s, 2, P['red3'])
    elif name == 'lock':
        c.ring(cx, cy - 3, 5, P['fog'], 2); c.rect(cx - 7, cy - 1, 14, 10, P['gold2']); c.rect(cx - 1, cy + 2, 2, 4, P['ink'])
    elif name == 'check':
        c.line(5, 12, 10, 18, P['grn4']); c.line(5, 13, 10, 19, P['grn4']); c.line(10, 18, 19, 5, P['grn4']); c.line(10, 19, 19, 6, P['grn4'])
    c.outline_alpha((8, 10, 14, 200))
    return c


# ================================================================== world objects & effects

def gen_objects():
    # Doors: 16x4 panels, pivot at the left end (the game rotates them open).
    for name, col, trim in (('door_wood', P['wood3'], P['wood1']), ('door_metal', P['iron'], P['slate']), ('door_glass', hexc('7fb0e0', 150), P['blue2']),
                            ('door_blue', P['iron'], P['blue3']), ('door_red', P['iron'], P['red3'])):
        c = Canvas(16, 4)
        c.rect(0, 0, 16, 4, col)
        c.rect(0, 0, 16, 1, shade(col, 1.25) if col[3] == 255 else (220, 240, 255, 190))
        c.rect(0, 3, 16, 1, trim)
        c.rect(12, 1, 2, 2, P['gold2'] if name == 'door_wood' else P['mist'])
        if name in ('door_blue', 'door_red'):
            c.rect(3, 1, 3, 2, trim)
        save(c, 'Objects', name + '.png')
    # secret bookshelf door (full tile)
    c = Canvas(16, 16)
    c.rect(0, 0, 16, 6, shade(P['wood2'], 1.15)); c.rect(0, 5, 16, 1, P['wood1']); c.rect(0, 6, 16, 10, P['wood2'])
    r = rng_for('secret')
    for shelf in (7, 11):
        x = 1
        while x < 15:
            bw = r.randint(1, 2)
            c.rect(x, shelf, bw, 3, r.choice([P['red2'], P['blue2'], P['grn2'], P['gold1'], P['cream']]))
            x += bw
    c.px(9, 8, P['gold3'])
    save(c, 'Objects', 'secret_door.png')
    # windows (horizontal; rotated for vertical walls)
    for broken in (False, True):
        c = Canvas(16, 16)
        c.rect(0, 5, 16, 6, P['slate']); c.rect(0, 5, 16, 1, P['ash']); c.rect(0, 10, 16, 1, P['ink'])
        if not broken:
            c.rect(1, 7, 14, 2, (140, 190, 230, 170)); c.rect(3, 7, 4, 1, (230, 245, 255, 220))
        else:
            for x in (1, 2, 6, 11, 14):
                c.px(x, 7, (140, 190, 230, 200)); c.px(x, 8, (140, 190, 230, 120))
        save(c, 'Objects', 'window_broken.png' if broken else 'window.png')
    # explosive barrel
    c = Canvas(16, 16)
    c.disc(8, 8, 6, P['red2']); c.ring(8, 8, 6, P['red1'], 1); c.ring(8, 8, 3.5, P['red1'], 1); c.disc(8, 8, 2, P['gold3'])
    c.disc(6, 6, 1, P['red4'])
    c.outline_alpha(OUTLINE)
    save(c, 'Objects', 'barrel.png')
    # security camera
    for off in (False, True):
        c = Canvas(12, 8)
        c.rect(0, 2, 3, 4, P['ash']); c.rect(3, 1, 7, 6, P['mist'] if not off else P['ash']); c.rect(10, 2, 2, 4, P['ink'])
        c.px(5, 3, P['red3'] if not off else P['ink'])
        c.outline_alpha(OUTLINE)
        save(c, 'Objects', 'camera_off.png' if off else 'camera.png')
    # interactable markers
    for name, col in (('icon_power', P['gold3']), ('icon_terminal', P['teal3']), ('icon_music', P['pur2']), ('icon_stairs', P['snow'])):
        c = Canvas(10, 10)
        if name == 'icon_power':
            c.line(6, 0, 3, 5, col); c.line(3, 5, 7, 5, col); c.line(7, 5, 4, 9, col)
        elif name == 'icon_terminal':
            c.outline(0, 1, 10, 7, col); c.rect(2, 3, 4, 1, col); c.rect(3, 8, 4, 1, col)
        elif name == 'icon_music':
            c.rect(6, 0, 1, 7, col); c.disc(4.5, 7.5, 2, col); c.rect(6, 0, 3, 2, col)
        else:
            for i in range(4): c.rect(i * 2, 8 - i * 2, 10 - i * 2, 2, col)
        save(c, 'Objects', name + '.png')


def gen_fx():
    # muzzle flash frames
    for f in range(3):
        c = Canvas(16, 16)
        size = [7, 5, 3][f]
        c.disc(4, 8, size * 0.6, P['gold3'])
        for k in range(5):
            a = (k - 2) * 0.35
            x1, y1 = int(4 + math.cos(a) * size * 1.8), int(8 + math.sin(a) * size * 1.8)
            c.line(4, 8, x1, y1, P['gold3'] if k % 2 else P['white'])
        c.disc(4, 8, size * 0.35, P['white'])
        save(c, 'FX', f'muzzle_{f}.png')
    # explosion frames 48x48
    for f in range(6):
        c = Canvas(48, 48)
        r = rng_for('exp', f)
        t = f / 5.0
        rad = 8 + t * 16
        if f < 4:
            c.disc(24, 24, rad, mix(P['gold3'], P['red2'], t))
            c.disc(24, 24, rad * (0.7 - t * 0.3), mix(P['white'], P['gold3'], t))
        for _ in range(14):
            a = r.uniform(0, 6.28)
            d = r.uniform(rad * 0.5, rad * 1.1)
            col = (70, 60, 60, int(220 * (1 - t * 0.6))) if f >= 2 else P['red3']
            c.disc(24 + math.cos(a) * d, 24 + math.sin(a) * d, r.uniform(3, 6) * (0.6 + t), col)
        save(c, 'FX', f'explosion_{f}.png')
    # simple particles
    c = Canvas(4, 4); c.rect(1, 1, 2, 2, P['gold3']); c.px(0, 1, P['white']); save(c, 'FX', 'spark.png')
    c = Canvas(3, 2); c.rect(0, 0, 3, 2, P['gold2']); c.px(0, 0, P['gold3']); save(c, 'FX', 'casing.png')
    c = Canvas(4, 2); c.rect(0, 0, 3, 2, P['red2']); c.rect(3, 0, 1, 2, P['gold2']); save(c, 'FX', 'shell.png')
    c = Canvas(3, 3); c.rect(0, 0, 2, 2, (170, 210, 240, 220)); c.px(2, 2, (220, 240, 255, 200)); save(c, 'FX', 'glass.png')
    c = Canvas(3, 3); c.rect(0, 0, 3, 3, (90, 14, 22, 255)); c.px(1, 1, (130, 24, 32, 255)); save(c, 'FX', 'hit.png')
    c = Canvas(3, 3); c.rect(0, 0, 3, 3, (110, 110, 110, 255)); c.px(1, 1, (160, 160, 160, 255)); save(c, 'FX', 'dust.png')
    c = Canvas(12, 4); c.rect(0, 1, 4, 2, P['ink']); c.rect(4, 1, 7, 2, P['mist']); c.px(11, 2, P['mist']); save(c, 'FX', 'knife_proj.png')
    c = Canvas(4, 4); c.disc(2, 2, 2, P['gold3']); save(c, 'FX', 'coin_proj.png')
    c = Canvas(3, 3); c.disc(1.5, 1.5, 1.5, (20, 20, 22, 230)); save(c, 'FX', 'bullet_hole.png')
    # non-graphic stain decal
    for i in range(3):
        c = Canvas(16, 16)
        r = rng_for('stain', i)
        c.disc(8, 8, 4, (60, 10, 16, 200))
        for _ in range(6):
            c.disc(8 + r.uniform(-5, 5), 8 + r.uniform(-5, 5), r.uniform(1, 2.5), (60, 10, 16, 190))
        save(c, 'FX', f'stain_{i}.png')
    c = Canvas(32, 32)
    for rr in range(15, 0, -1):
        c.disc(16, 16, rr, (20, 16, 14, int(200 * (1 - rr / 16))))
    save(c, 'FX', 'scorch.png')
    # soft shapes (used for lights, glows, smoke)
    c = Canvas(64, 64)
    for y in range(64):
        for x in range(64):
            d = math.hypot(x + 0.5 - 32, y + 0.5 - 32) / 32
            if d < 1:
                a = (1 - d) ** 2
                c.a[y, x] = (255, 255, 255, int(255 * a))
    save(c, 'FX', 'glow.png')
    c = Canvas(16, 16)
    for y in range(16):
        for x in range(16):
            d = math.hypot(x + 0.5 - 8, y + 0.5 - 8) / 8
            if d < 1:
                c.a[y, x] = (200, 200, 205, int(180 * (1 - d)))
    save(c, 'FX', 'smoke.png')
    c = Canvas(64, 64); c.ring(32, 32, 31, (255, 255, 255, 255), 1.5); save(c, 'FX', 'ring.png')
    c = Canvas(4, 4); c.rect(0, 0, 4, 4, P['white']); save(c, 'FX', 'pixel.png')


def gen_ui():
    # crosshair
    c = Canvas(17, 17)
    for (x, y, w, h) in ((8, 0, 1, 5), (8, 12, 1, 5), (0, 8, 5, 1), (12, 8, 5, 1)):
        c.rect(x, y, w, h, P['white'])
    c.px(8, 8, P['red3'])
    c.outline_alpha((0, 0, 0, 200))
    save(c, 'UI', 'crosshair.png')
    # damage / low-health vignette: transparent centre, opaque edges
    c = Canvas(64, 36)
    for y in range(36):
        for x in range(64):
            dx, dy = (x + 0.5 - 32) / 32, (y + 0.5 - 18) / 18
            d = min(1.0, math.hypot(dx, dy) / 1.25)
            a = max(0.0, (d - 0.55) / 0.45) ** 1.6
            c.a[y, x] = (255, 255, 255, int(255 * a))
    save(c, 'UI', 'vignette.png')
    # NPC state icons
    c = Canvas(5, 11); c.rect(1, 0, 3, 7, P['red3']); c.rect(1, 8, 3, 3, P['red3']); c.outline_alpha(OUTLINE); save(c, 'UI', 'alert.png')
    c = Canvas(7, 11); c.rect(1, 0, 5, 2, P['gold3']); c.rect(5, 1, 2, 4, P['gold3']); c.rect(3, 4, 3, 2, P['gold3']); c.rect(3, 6, 2, 2, P['gold3']); c.rect(3, 9, 2, 2, P['gold3'])
    c.outline_alpha(OUTLINE); save(c, 'UI', 'question.png')
    c = Canvas(9, 7); c.line(0, 0, 4, 5, P['red3']); c.line(8, 0, 4, 5, P['red3']); c.line(1, 0, 4, 4, P['red3']); c.line(7, 0, 4, 4, P['red3'])
    c.outline_alpha(OUTLINE); save(c, 'UI', 'target_marker.png')
    c = Canvas(9, 7); c.line(0, 6, 4, 1, P['gold3']); c.line(8, 6, 4, 1, P['gold3']); c.line(1, 6, 4, 2, P['gold3']); c.line(7, 6, 4, 2, P['gold3'])
    c.outline_alpha(OUTLINE); save(c, 'UI', 'objective_arrow.png')
    # UI panel (9-slice) and button
    for name, fill, edge in (('panel', (16, 20, 27, 235), P['steel']), ('button', (30, 36, 46, 255), P['iron']), ('button_hot', (60, 22, 28, 255), P['red3']),
                             ('slot', (12, 15, 20, 240), P['slate'])):
        c = Canvas(16, 16, fill)
        c.outline(0, 0, 16, 16, edge)
        c.px(0, 0, (0, 0, 0, 0)); c.px(15, 0, (0, 0, 0, 0)); c.px(0, 15, (0, 0, 0, 0)); c.px(15, 15, (0, 0, 0, 0))
        c.rect(1, 1, 14, 1, shade(edge, 1.3) if name != 'slot' else edge)
        save(c, 'UI', name + '.png')
    # icons
    for n in ('ammo_pistol', 'ammo_rifle', 'ammo_shells', 'ammo_knives', 'armor_light', 'armor_heavy', 'medkit', 'coin', 'up_suppressor', 'up_extendedmag',
              'up_stabilizer', 'keycard_blue', 'keycard_red', 'intel', 'ledger', 'samples', 'prototype', 'drive', 'cash', 'health', 'shield', 'money', 'eye',
              'skull', 'clock', 'target', 'lock', 'check'):
        save(icon_misc(n), 'Icons', n + '.png')
    for wid in ('knife', 'combat_knife', 'throwing_knives', 'coin', 'pistol', 'silenced_pistol', 'smg', 'shotgun', 'assault_rifle'):
        save(icon_weapon(wid), 'Icons', 'w_' + wid + '.png')
        save(held_weapon(wid), 'Weapons', wid + '.png')


def gen_backdrop():
    """480x270 pixel-art title backdrop: rainy city at night, lone figure on a rooftop."""
    W, H = 480, 270
    c = Canvas(W, H)
    for y in range(H):
        t = y / H
        c.rect(0, y, W, 1, mix(hexc('07090e'), hexc('1a1320'), t))
    r = rng_for('backdrop')
    for _ in range(90):
        c.px(r.randint(0, W - 1), r.randint(0, 120), (200, 210, 230, r.randint(60, 200)))
    c.disc(380, 60, 22, hexc('e8e0cc')); c.disc(372, 55, 20, hexc('d9d0ba')); c.disc(386, 66, 4, hexc('cfc5ad'))
    for layer, (col, base, hmin, hmax, win) in enumerate(((hexc('141824'), 150, 40, 110, 0.12), (hexc('0f121b'), 175, 30, 90, 0.2), (hexc('0a0c12'), 200, 20, 60, 0.25))):
        x = -10
        while x < W:
            bw = r.randint(18, 46)
            bh = r.randint(hmin, hmax)
            top = base - bh
            c.rect(x, top, bw, H - top, col)
            if r.random() < 0.3:
                c.rect(x + bw // 2, top - r.randint(6, 18), 2, 18, col)
                c.px(x + bw // 2, top - 18, P['red3'])
            for wy in range(top + 4, H, 6):
                for wx in range(x + 3, x + bw - 3, 5):
                    if r.random() < win:
                        c.rect(wx, wy, 2, 3, mix(P['gold3'], P['blue4'], r.random() * 0.4) if r.random() < 0.85 else P['red4'])
            x += bw + r.randint(0, 4)
    # foreground rooftop + figure
    c.rect(0, 222, W, 48, hexc('06070a'))
    c.rect(0, 220, W, 2, hexc('1f2630'))
    c.rect(60, 196, 30, 26, hexc('06070a')); c.rect(64, 190, 4, 8, hexc('06070a'))
    fx, fy = 332, 220
    sil = hexc('020203')
    c.rect(fx - 4, fy - 30, 8, 18, sil)           # coat
    c.rect(fx - 6, fy - 16, 12, 4, sil)
    c.rect(fx - 3, fy - 12, 2, 12, sil); c.rect(fx + 1, fy - 12, 2, 12, sil)
    c.disc(fx, fy - 34, 4, sil)
    c.rect(fx + 3, fy - 26, 12, 2, sil)           # arm + pistol
    c.rect(fx + 13, fy - 28, 4, 2, sil)
    c.px(fx + 1, fy - 29, P['red3'])
    for _ in range(420):
        x, y = r.randint(0, W - 1), r.randint(0, H - 1)
        c.line(x, y, x - 2, y + 7, (150, 170, 200, 60))
    save(c, 'UI', 'backdrop.png')


if __name__ == '__main__':
    gen_characters()
    gen_objects()
    gen_fx()
    gen_ui()
    gen_backdrop()
    print('sprites generated')
