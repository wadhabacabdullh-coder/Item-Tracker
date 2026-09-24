"""Pre-renders each map's static art from the same text files the game uses.

Outputs (Assets/Resources/Sprites/Maps):
  <id>_base.png     floors, walls (3/4 view with front faces), furniture, baked shadows
  <id>_overlay.png  tree canopies and hedges, drawn above characters
The game draws doors, windows, barrels, lights and characters dynamically on top.
"""
import os
import sys
import numpy as np
from pix import Canvas, P, hexc, shade, mix, rng_for

T = 16
ROOT = os.path.join(os.path.dirname(__file__), '..', '..')
MAPS = os.path.join(ROOT, 'Assets', 'Resources', 'Maps')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'Sprites', 'Maps')

FLOORS = '.:;_,"'
WALLISH = '# '
TALL = set('hxrmfCTijy') - set('y')  # bookshelf crate rack machine cabinet closet tree pillar vending
DYNAMIC = set('DLMSWe')               # drawn by the game


def parse(path):
    meta, rows, ents, section = {}, [], [], None
    for raw in open(path).read().split('\n'):
        t = raw.strip()
        if t.startswith('@'):
            section = t[1:].split()[0]
            continue
        if section == 'tiles':
            rows.append(raw)
        elif section == 'meta' and ':' in t:
            k, v = t.split(':', 1)
            meta[k.strip()] = v.strip()
        elif section in ('entities', 'mission') and t:
            ents.append(t)
    while rows and rows[-1] == '':
        rows.pop()
    w = max(len(r) for r in rows)
    rows = [r.ljust(w) for r in rows]
    return meta, rows, ents


# ------------------------------------------------------------------ themes

THEMES = {
    'mansion': dict(
        A=('marble', hexc('cfc6b4'), hexc('b3a894')), B=('parquet', P['wood3'], P['wood4']), C=('carpet', P['red1'], P['gold1']),
        D=('checker', hexc('c9ccd0'), hexc('6b7078')), G=('grass', P['grn1'], P['grn2']), Pth=('gravel', hexc('8f8676'), hexc('6f675a')),
        wall_top=hexc('3b3238'), wall_rim=hexc('6a5a60'), wall_face=hexc('4a2c2e'), wall_base=P['wood1'], ext_face=hexc('5a5550'),
        sofa=P['red2'], bed=hexc('7a2231'), table=P['wood3'], desk=P['wood2'], counter=hexc('d8d4cc'), accent=P['gold2'],
    ),
    'facility': dict(
        A=('concrete', hexc('5f6873'), hexc('4c545e')), B=('plate', hexc('55616e'), hexc('3c4652')), C=('rubber', hexc('2d3a4f'), hexc('24303f')),
        D=('labtile', hexc('c5d2da'), hexc('9fb0bb')), G=('grass', P['grn1'], P['grn2']), Pth=('asphalt', hexc('34383e'), hexc('2a2d32')),
        wall_top=hexc('263039'), wall_rim=hexc('3f6f78'), wall_face=hexc('3a4552'), wall_base=hexc('c79a3a'), ext_face=hexc('3a4552'),
        sofa=hexc('3a4a60'), bed=hexc('4a5a4a'), table=hexc('8d9bab'), desk=hexc('6b7b8c'), counter=hexc('b5c0cc'), accent=P['teal3'],
    ),
    'office': dict(
        A=('granite', hexc('3d4249'), hexc('30343a')), B=('planks', P['wood4'], P['wood3']), C=('carpettile', hexc('46566b'), hexc('3c4a5d')),
        D=('polished', hexc('d6dbe0'), hexc('bcc3ca')), G=('paint', hexc('3d4249'), hexc('e8e8e8')), Pth=('sidewalk', hexc('7b8087'), hexc('666b72')),
        wall_top=hexc('1f2630'), wall_rim=hexc('4a78b0'), wall_face=hexc('2b3440'), wall_base=hexc('151a22'), ext_face=hexc('2b3440'),
        sofa=hexc('5a6a80'), bed=hexc('4a5a6a'), table=hexc('c8ccd2'), desk=hexc('d9d4c8'), counter=hexc('e1e6ec'), accent=P['blue4'],
    ),
}


class MapArt:
    def __init__(self, map_id):
        self.meta, self.rows, self.ents = parse(os.path.join(MAPS, map_id + '.txt'))
        self.id = map_id
        self.th = THEMES[self.meta['theme']]
        self.h = len(self.rows)
        self.w = len(self.rows[0])
        self.base = Canvas(self.w * T, self.h * T, P['black'])
        self.over = Canvas(self.w * T, self.h * T)
        self.floor_of = {}
        self._infer_floors()

    def ch(self, x, y):
        if 0 <= y < self.h and 0 <= x < self.w:
            return self.rows[y][x]
        return ' '

    def is_wall(self, x, y):
        return self.ch(x, y) in WALLISH or self.ch(x, y) == 'v' and False

    def _infer_floors(self):
        """Every non-floor walkable/furniture tile gets the most common neighbouring floor."""
        for y in range(self.h):
            for x in range(self.w):
                c = self.ch(x, y)
                if c in FLOORS:
                    self.floor_of[(x, y)] = c
        for rnd in range(4):
            for y in range(self.h):
                for x in range(self.w):
                    if (x, y) in self.floor_of or self.ch(x, y) in '# ':
                        continue
                    counts = {}
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            f = self.floor_of.get((x + dx, y + dy))
                            if f:
                                counts[f] = counts.get(f, 0) + (2 if dx == 0 or dy == 0 else 1)
                    if counts:
                        self.floor_of[(x, y)] = max(sorted(counts), key=lambda k: counts[k])

    # ------------------------------------------------------------------ floors
    def draw_floor(self, x, y, fch):
        key = {'.': 'A', ':': 'B', ';': 'C', '_': 'D', ',': 'G', '"': 'Pth'}[fch]
        kind, c1, c2 = self.th[key]
        c = self.base
        px, py = x * T, y * T
        r = rng_for(self.id, x, y)
        if kind == 'marble':
            c.noise(px, py, T, T, c1, 0.03, r, 2)
            for (ox, oy) in ((0, 0), (8, 8)):
                pass
            c.rect(px, py, T, 1, shade(c2, 0.9)); c.rect(px, py, 1, T, shade(c2, 0.9))
            c.rect(px + 8, py, 1, T, shade(c2, 1.0)); c.rect(px, py + 8, T, 1, shade(c2, 1.0))
            if r.random() < 0.5:
                vx = r.randint(1, 14)
                for i in range(6):
                    c.px(px + vx + i // 2, py + 2 + i * 2, shade(c2, 0.95))
        elif kind == 'parquet':
            for j in range(4):
                for i in range(2):
                    col = c1 if (i + j + (x + y)) % 2 else c2
                    col = shade(col, 1 + r.uniform(-0.06, 0.06))
                    if (x + y) % 2:
                        c.rect(px + i * 8, py + j * 4, 8, 4, col)
                        c.rect(px + i * 8, py + j * 4 + 3, 8, 1, shade(col, 0.8))
                    else:
                        c.rect(px + j * 4, py + i * 8, 4, 8, col)
                        c.rect(px + j * 4 + 3, py + i * 8, 1, 8, shade(col, 0.8))
        elif kind == 'planks':
            off = (x * 7 + y * 3) % 16
            for j in range(4):
                col = shade(c1 if j % 2 else mix(c1, c2, 0.3), 1 + r.uniform(-0.05, 0.05))
                c.rect(px, py + j * 4, T, 4, col)
                c.rect(px, py + j * 4 + 3, T, 1, shade(col, 0.82))
                c.px(px + (off + j * 5) % 16, py + j * 4 + 1, shade(col, 0.85))
        elif kind == 'carpet':
            c.noise(px, py, T, T, c1, 0.05, r, 1)
            if (x + y) % 2 == 0:
                c.px(px + 7, py + 7, c2); c.px(px + 8, py + 8, c2); c.px(px + 7, py + 8, shade(c2, 0.8)); c.px(px + 8, py + 7, shade(c2, 0.8))
            # gold border along walls
            for (dx, dy, rx, ry, rw, rh) in ((0, -1, 0, 1, T, 1), (0, 1, 0, T - 2, T, 1), (-1, 0, 1, 0, 1, T), (1, 0, T - 2, 0, 1, T)):
                if self.ch(x + dx, y + dy) in '#':
                    c.rect(px + rx, py + ry, rw, rh, c2)
        elif kind in ('checker', 'labtile'):
            for j in range(2):
                for i in range(2):
                    col = c1 if kind == 'labtile' or (i + j) % 2 == 0 else c2
                    c.noise(px + i * 8, py + j * 8, 8, 8, col, 0.02, r, 4)
                    c.rect(px + i * 8, py + j * 8, 8, 1, shade(col, 0.85))
                    c.rect(px + i * 8, py + j * 8, 1, 8, shade(col, 0.85))
        elif kind == 'grass':
            c.noise(px, py, T, T, c1, 0.12, r, 2)
            for _ in range(5):
                gx, gy = r.randint(0, 14), r.randint(1, 15)
                c.px(px + gx, py + gy, c2); c.px(px + gx, py + gy - 1, shade(c2, 1.25))
            if r.random() < 0.08:
                c.px(px + r.randint(2, 13), py + r.randint(2, 13), P['gold3'] if r.random() < 0.5 else P['snow'])
        elif kind in ('gravel', 'asphalt', 'sidewalk', 'concrete', 'granite', 'rubber'):
            c.noise(px, py, T, T, c1, 0.07 if kind != 'granite' else 0.1, r, 1 if kind in ('gravel', 'granite') else 2)
            if kind == 'sidewalk' or kind == 'concrete':
                c.rect(px, py, T, 1, shade(c2, 0.95)); c.rect(px, py, 1, T, shade(c2, 0.95))
            if kind == 'gravel':
                for _ in range(4):
                    c.px(px + r.randint(0, 15), py + r.randint(0, 15), shade(c2, 0.8))
            if kind == 'asphalt' and r.random() < 0.06:
                c.ellipse(px + 8, py + 8, 4, 3, shade(c1, 0.75))
        elif kind == 'plate':
            c.rect(px, py, T, T, c1)
            for j in range(0, T, 4):
                for i in range(0, T, 4):
                    c.px(px + i + (j // 4) % 2 * 2, py + j, shade(c1, 1.2))
            c.outline(px, py, T, T, shade(c2, 0.9))
            for (ix, iy) in ((1, 1), (14, 1), (1, 14), (14, 14)):
                c.px(px + ix, py + iy, shade(c1, 1.35))
        elif kind == 'carpettile':
            col = c1 if (x + y) % 2 else c2
            c.noise(px, py, T, T, col, 0.04, r, 1)
        elif kind == 'polished':
            c.rect(px, py, T, T, c1)
            c.rect(px, py, T, 1, c2); c.rect(px, py, 1, T, c2)
            c.line(px + 3, py + 12, px + 12, py + 3, shade(c1, 1.05))
        elif kind == 'paint':
            c.noise(px, py, T, T, c1, 0.05, r, 2)
            c.rect(px + 1, py + 1, 14, 14, hexc('e8e8e8'))
        else:
            c.rect(px, py, T, T, c1)

    # ------------------------------------------------------------------ walls
    def draw_wall(self, x, y):
        c, th = self.base, self.th
        px, py = x * T, y * T
        below = self.ch(x, y + 1)
        exterior = self.ch(x, y + 1) in ',"' and self.meta['theme'] != 'office'
        top = th['wall_top']
        c.rect(px, py, T, T, top)
        r = rng_for(self.id, 'w', x, y)
        c.noise(px, py, T, T, top, 0.04, r, 2)
        # rim on edges that border a non-wall
        rim = th['wall_rim']
        if self.ch(x, y - 1) not in WALLISH: c.rect(px, py, T, 1, rim)
        if self.ch(x - 1, y) not in WALLISH: c.rect(px, py, 1, T, shade(rim, 0.85))
        if self.ch(x + 1, y) not in WALLISH: c.rect(px + T - 1, py, 1, T, shade(rim, 0.7))
        if below not in WALLISH:
            # front face (3/4 view): visible because the tile to the south is open
            face = th['ext_face'] if exterior else th['wall_face']
            c.rect(px, py + 9, T, 7, face)
            c.noise(px, py + 9, T, 7, face, 0.05, r, 1)
            c.rect(px, py + 9, T, 1, shade(rim, 0.9))
            c.rect(px, py + 14, T, 2, th['wall_base'] if not exterior else shade(face, 0.7))
            if self.meta['theme'] == 'mansion' and not exterior:
                for i in range(1, T, 4):
                    c.px(px + i, py + 11, shade(face, 1.3))
            if self.meta['theme'] == 'facility':
                for i in range(0, T, 4):
                    c.px(px + i, py + 13, P['gold2']); c.px(px + i + 1, py + 13, P['gold2'])
            if self.meta['theme'] == 'office':
                c.rect(px, py + 11, T, 1, shade(th['wall_rim'], 0.6))

    # ------------------------------------------------------------------ furniture
    def same(self, x, y, ch):
        return self.ch(x, y) == ch

    def edges(self, x, y, ch):
        return (not self.same(x, y - 1, ch), not self.same(x + 1, y, ch), not self.same(x, y + 1, ch), not self.same(x - 1, y, ch))

    def cluster(self, x, y, ch):
        """Bounding box (x0,y0,x1,y1) of the connected cluster of ch containing (x,y)."""
        seen, stack = {(x, y)}, [(x, y)]
        while stack:
            cx, cy = stack.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (cx + dx, cy + dy)
                if n not in seen and self.same(n[0], n[1], ch):
                    seen.add(n)
                    stack.append(n)
        xs = [p[0] for p in seen]
        ys = [p[1] for p in seen]
        return min(xs), min(ys), max(xs), max(ys)

    def slab(self, x, y, ch, col, inset=1, border=None, top_light=True):
        """A flat surface that merges with same-char neighbours (tables, counters, desks)."""
        c = self.base
        px, py = x * T, y * T
        n, e, s, w = self.edges(x, y, ch)
        x0 = px + (inset if w else 0)
        y0 = py + (inset if n else 0)
        x1 = px + T - (inset if e else 0)
        y1 = py + T - (inset + 2 if s else 0)
        c.rect(x0, y0, x1 - x0, y1 - y0, col)
        b = border or shade(col, 0.7)
        if n: c.rect(x0, y0, x1 - x0, 1, shade(col, 1.25) if top_light else b)
        if w: c.rect(x0, y0, 1, y1 - y0, shade(col, 1.1))
        if e: c.rect(x1 - 1, y0, 1, y1 - y0, b)
        if s:
            c.rect(x0, y1 - 1, x1 - x0, 1, b)
            c.rect(x0, y1, x1 - x0, 2, shade(col, 0.5))   # front edge / legs shadow

    def draw_furniture(self, x, y, ch):
        c, th = self.base, self.th
        px, py = x * T, y * T
        r = rng_for(self.id, 'f', x, y)
        if ch == 't':
            self.slab(x, y, 't', th['table'])
            if r.random() < 0.3:
                c.disc(px + 8, py + 7, 2, P['snow'] if self.meta['theme'] != 'facility' else P['mist'])
        elif ch == 'k':
            self.slab(x, y, 'k', th['desk'])
            n, e, s, w = self.edges(x, y, 'k')
            if r.random() < 0.6:
                c.rect(px + 4, py + 2, 8, 5, P['ink']); c.rect(px + 5, py + 3, 6, 3, P['blue3'] if self.meta['theme'] != 'mansion' else P['gold1'])
                c.rect(px + 7, py + 7, 2, 1, P['ink'])
                c.rect(px + 4, py + 9, 7, 2, P['steel'])
            else:
                c.rect(px + 3, py + 4, 5, 6, P['snow']); c.rect(px + 9, py + 5, 4, 5, P['mist'])
        elif ch == 'u':
            self.slab(x, y, 'u', th['counter'])
            if r.random() < 0.25:
                c.rect(px + 5, py + 4, 5, 4, P['fog'])
        elif ch == 'q':
            self.slab(x, y, 'q', P['steel'])
            c.rect(px + 2, py + 1, 12, 7, P['ink'])
            c.rect(px + 3, py + 2, 10, 5, P['teal2'] if r.random() < 0.5 else P['blue3'])
            for i in range(3):
                c.rect(px + 4, py + 3 + i, r.randint(3, 8), 1, P['teal3'])
        elif ch == 'b':
            x0, y0, x1, y1 = self.cluster(x, y, 'b')
            self.slab(x, y, 'b', th['bed'], inset=1)
            if y == y0:
                c.rect(px + 2, py + 2, 12, 5, P['snow']); c.rect(px + 2, py + 6, 12, 1, P['mist'])
            else:
                c.rect(px + 1, py, 14, 2, shade(th['bed'], 1.25))
        elif ch == 's':
            col = th['sofa']
            self.slab(x, y, 's', col, inset=1)
            n, e, s, w = self.edges(x, y, 's')
            c.rect(px + 1, py + 1, 14, 4, shade(col, 0.75))  # backrest
            c.rect(px + 7, py + 5, 1, 8, shade(col, 0.8))
            if w: c.rect(px + 1, py + 1, 3, 13, shade(col, 0.85))
            if e: c.rect(px + 12, py + 1, 3, 13, shade(col, 0.85))
        elif ch == 'P':
            x0, y0, x1, y1 = self.cluster(x, y, 'P')
            if (x, y) == (x0, y0):
                w, h = (x1 - x0 + 1) * T, (y1 - y0 + 1) * T
                c.rect(px + 2, py + 2, w - 4, h - 6, P['black'])
                c.rect(px + 3, py + 3, w - 6, h - 8, P['ink'])
                c.rect(px + 5, py + 4, w - 12, 3, P['night'])
                c.rect(px + 2, py + h - 7, w - 4, 3, P['white'])
                for i in range(2, w - 4, 3):
                    c.rect(px + 2 + i, py + h - 7, 1, 2, P['black'])
                c.rect(px + 2, py + h - 4, w - 4, 2, shade(P['black'], 1))
        elif ch == 'g':
            x0, y0, x1, y1 = self.cluster(x, y, 'g')
            if (x, y) == (x0, y0):
                self.draw_car(x0, y0, x1, y1)
        elif ch in ('h', 'S'):
            self.draw_tall(x, y, P['wood2'], P['wood1'], books=True)
        elif ch == 'x':
            if self.meta['theme'] == 'mansion' and self.floor_of.get((x, y)) == ':':
                self.draw_tall(x, y, P['wood2'], P['wood1'], wine=True)
            else:
                self.draw_crate(x, y)
        elif ch == 'r':
            self.draw_tall(x, y, P['ink'], P['black'], leds=True)
        elif ch == 'm':
            self.draw_tall(x, y, P['iron'], P['steel'], machine=True)
        elif ch == 'f':
            col = P['ash'] if self.meta['theme'] != 'mansion' else P['wood2']
            self.draw_tall(x, y, col, shade(col, 0.7), cabinet=True)
        elif ch == 'C':
            self.draw_tall(x, y, P['wood3'], P['wood1'], closet=True)
        elif ch == 'j':
            self.draw_tall(x, y, P['red2'] if r.random() < 0.5 else P['blue2'], P['night'], vending=True)
        elif ch == 'i':
            self.draw_pillar(x, y)
        elif ch == 'p':
            c.disc(px + 8, py + 10, 4, P['wood2'])
            c.disc(px + 8, py + 10, 3, P['wood1'])
            for (dx, dy, rr) in ((0, -2, 5), (-3, 0, 3), (3, 0, 3), (0, 2, 3)):
                c.disc(px + 8 + dx, py + 7 + dy, rr, P['grn2'])
            for _ in range(6):
                c.px(px + r.randint(3, 12), py + r.randint(2, 11), P['grn4'])
        elif ch == 'c':
            col = th['sofa'] if self.meta['theme'] == 'mansion' else P['night']
            c.rect(px + 4, py + 4, 8, 8, shade(col, 0.9)); c.rect(px + 4, py + 4, 8, 2, shade(col, 0.6))
            c.rect(px + 5, py + 6, 6, 5, col)
        elif ch == 'o':
            c.ellipse(px + 8, py + 9, 4, 5, P['snow']); c.ellipse(px + 8, py + 9, 2.5, 3.5, P['mist'])
            c.rect(px + 4, py + 2, 8, 4, P['white']); c.rect(px + 4, py + 5, 8, 1, P['fog'])
        elif ch == 'n':
            self.slab(x, y, 'n', P['snow'])
            c.ellipse(px + 8, py + 7, 4, 3, P['fog']); c.px(px + 8, py + 3, P['ash'])
        elif ch == 'y':
            self.slab(x, y, 'y', P['white'], inset=1)
            n, e, s, w = self.edges(x, y, 'y')
            c.rect(px + (3 if w else 0), py + 3, T - (3 if w else 0) - (3 if e else 0), 9, P['blue4'])
        elif ch == 'w':
            self.draw_partition(x, y)
        elif ch == 'B':
            pass  # overlay
        elif ch == 'T':
            c.disc(px + 8, py + 9, 3, P['wood1'])
        elif ch == 'e':
            pass  # dynamic barrel sprite

    def draw_tall(self, x, y, col, dark, books=False, wine=False, leds=False, machine=False, cabinet=False, closet=False, vending=False):
        c = self.base
        px, py = x * T, y * T
        r = rng_for(self.id, 't', x, y)
        # top face
        c.rect(px, py, T, 6, shade(col, 1.15))
        c.rect(px, py, T, 1, shade(col, 1.35))
        c.rect(px, py + 5, T, 1, dark)
        # front face
        c.rect(px, py + 6, T, 10, col)
        c.rect(px, py + 15, T, 1, shade(dark, 0.7))
        if books:
            for shelf in (7, 11):
                xx = 1
                while xx < 15:
                    bw = r.randint(1, 2)
                    bc = r.choice([P['red2'], P['blue2'], P['grn2'], P['gold1'], P['pur1'], P['cream'], P['teal1']])
                    c.rect(px + xx, py + shelf, bw, 3, bc)
                    xx += bw + (1 if r.random() < 0.2 else 0)
                c.rect(px, py + shelf + 3, T, 1, dark)
        elif wine:
            for sy in (7, 10, 13):
                for sx in range(1, 15, 3):
                    c.disc(px + sx + 1, py + sy + 1, 1.2, P['red0'] if r.random() < 0.7 else P['grn1'])
        elif leds:
            for sy in range(7, 15, 2):
                c.rect(px + 2, py + sy, 12, 1, P['night'])
                for sx in range(3, 13, 3):
                    if r.random() < 0.5:
                        c.px(px + sx, py + sy, r.choice([P['grn4'], P['teal3'], P['gold3'], P['red3']]))
        elif machine:
            c.rect(px + 2, py + 7, 12, 6, shade(col, 0.8))
            for sy in range(8, 12, 2):
                c.rect(px + 3, py + sy, 10, 1, dark)
            c.px(px + 13, py + 7, P['grn4'] if r.random() < 0.6 else P['red3'])
            c.rect(px + 3, py + 1, 4, 3, shade(col, 0.8))
        elif cabinet:
            c.rect(px + 1, py + 10, 14, 1, dark)
            c.rect(px + 7, py + 7, 2, 1, P['mist']); c.rect(px + 7, py + 12, 2, 1, P['mist'])
        elif closet:
            c.rect(px + 7, py + 6, 1, 10, dark)
            c.px(px + 5, py + 10, P['gold2']); c.px(px + 9, py + 10, P['gold2'])
            c.rect(px + 1, py + 7, 5, 8, shade(col, 0.9)); c.rect(px + 9, py + 7, 5, 8, shade(col, 0.9))
        elif vending:
            c.rect(px + 2, py + 7, 8, 7, P['snow'])
            for sy in range(8, 13, 2):
                for sx in range(3, 9, 2):
                    c.px(px + sx, py + sy, r.choice([P['red3'], P['gold3'], P['blue4'], P['grn4']]))
            c.rect(px + 11, py + 8, 3, 2, P['ink'])

    def draw_crate(self, x, y):
        c = self.base
        px, py = x * T, y * T
        facility = self.meta['theme'] != 'mansion'
        col = P['wood3'] if not facility else hexc('5a6a52')
        c.rect(px + 1, py + 1, 14, 5, shade(col, 1.2))
        c.rect(px + 1, py + 6, 14, 9, col)
        c.outline(px + 1, py + 1, 14, 14, shade(col, 0.6))
        c.rect(px + 1, py + 6, 14, 1, shade(col, 0.6))
        if not facility:
            c.line(px + 2, py + 7, px + 13, py + 13, shade(col, 0.7)); c.line(px + 13, py + 7, px + 2, py + 13, shade(col, 0.7))
        else:
            c.rect(px + 5, py + 9, 6, 3, P['snow']); c.rect(px + 6, py + 10, 4, 1, P['ink'])

    def draw_pillar(self, x, y):
        c = self.base
        px, py = x * T, y * T
        col = hexc('d8d2c4') if self.meta['theme'] == 'mansion' else P['ash']
        c.disc(px + 8, py + 6, 6, shade(col, 1.1))
        c.rect(px + 2, py + 6, 12, 9, col)
        c.rect(px + 2, py + 14, 12, 2, shade(col, 0.7))
        for i in (4, 8, 12):
            c.rect(px + i, py + 7, 1, 7, shade(col, 0.85))
        c.disc(px + 8, py + 6, 4, shade(col, 1.2))

    def draw_partition(self, x, y):
        c = self.base
        px, py = x * T, y * T
        col = hexc('6f7f96')
        n, e, s, w = [not self.same(*p, 'w') for p in ((x, y - 1), (x + 1, y), (x, y + 1), (x - 1, y))]
        c.rect(px + 6, py + 6, 4, 4, col)
        if not n: c.rect(px + 6, py, 4, 8, col)
        if not s: c.rect(px + 6, py + 8, 4, 8, col)
        if not w: c.rect(px, py + 6, 8, 4, col)
        if not e: c.rect(px + 8, py + 6, 8, 4, col)
        c.rect(px + 6, py + 9, 4, 1, shade(col, 0.6))

    def draw_car(self, x0, y0, x1, y1):
        c = self.base
        r = rng_for(self.id, 'car', x0, y0)
        w, h = (x1 - x0 + 1) * T, (y1 - y0 + 1) * T
        px, py = x0 * T, y0 * T
        col = r.choice([P['red2'], P['blue2'], P['black'], P['snow'], P['ash'], P['gold1']])
        horiz = w >= h
        c.rect(px + 2, py + 3, w - 4, h - 5, shade(col, 0.5))  # shadow/underside
        c.rect(px + 2, py + 2, w - 4, h - 6, col)
        c.outline(px + 2, py + 2, w - 4, h - 6, shade(col, 0.6))
        if horiz:
            c.rect(px + w // 3, py + 4, w // 3, h - 10, P['night'])      # roof / windows
            c.rect(px + w // 3 + 2, py + 5, w // 3 - 4, h - 12, shade(col, 0.9))
            c.rect(px + 3, py + 4, 2, 3, P['gold3']); c.rect(px + 3, py + h - 9, 2, 3, P['gold3'])
            c.rect(px + w - 5, py + 4, 2, 3, P['red3']); c.rect(px + w - 5, py + h - 9, 2, 3, P['red3'])
        else:
            c.rect(px + 4, py + h // 3, w - 8, h // 3, P['night'])

    # ------------------------------------------------------------------ special tiles
    def draw_water(self, x, y):
        c = self.base
        px, py = x * T, y * T
        r = rng_for(self.id, 'water', x, y)
        c.noise(px, py, T, T, hexc('1f5f7a'), 0.06, r, 2)
        for _ in range(3):
            wx, wy = r.randint(1, 12), r.randint(1, 14)
            c.rect(px + wx, py + wy, 3, 1, hexc('5fb0cc'))
        n, e, s, w = [self.ch(*p) != '~' for p in ((x, y - 1), (x + 1, y), (x, y + 1), (x - 1, y))]
        edge = P['mist']
        if n: c.rect(px, py, T, 2, edge); c.rect(px, py + 2, T, 2, hexc('123f52'))
        if s: c.rect(px, py + T - 2, T, 2, edge)
        if w: c.rect(px, py, 2, T, edge)
        if e: c.rect(px + T - 2, py, 2, T, edge)

    def draw_stairs(self, x, y):
        c = self.base
        px, py = x * T, y * T
        col = P['ash'] if self.meta['theme'] != 'mansion' else hexc('b3a894')
        c.rect(px, py, T, T, shade(col, 0.6))
        for i in range(0, T, 4):
            c.rect(px, py + i, T, 3, shade(col, 1 - i / 40))
            c.rect(px, py + i + 3, T, 1, shade(col, 0.45))

    def draw_vent(self, x, y):
        c = self.base
        px, py = x * T, y * T
        c.rect(px, py, T, T, P['ink'])
        c.rect(px + 1, py + 1, T - 2, T - 2, P['night'])
        for i in range(2, T - 2, 3):
            c.rect(px + 2, py + i, T - 4, 1, P['slate'])
        # duct walls where the vent turns
        for (dx, dy, rx, ry, rw, rh) in ((0, -1, 0, 0, T, 1), (0, 1, 0, T - 1, T, 1), (-1, 0, 0, 0, 1, T), (1, 0, T - 1, 0, 1, T)):
            if self.ch(x + dx, y + dy) not in 'v':
                c.rect(px + rx, py + ry, rw, rh, P['iron'])

    # ------------------------------------------------------------------ shadows & overlay
    def shadows(self):
        """Soft drop shadows cast south-east by walls and tall furniture onto floors."""
        a = self.base.a
        dark = np.ones((self.h * T, self.w * T), dtype=np.float32)
        for y in range(self.h):
            for x in range(self.w):
                ch = self.ch(x, y)
                caster = ch in '#' or ch in TALL or ch in 'hS'
                if not caster:
                    continue
                px, py = x * T, y * T
                # east shadow
                if self.ch(x + 1, y) not in '# ' and not (self.ch(x + 1, y) in TALL):
                    dark[py + 3:py + T, px + T:px + T + 4] *= 0.72
                # south shadow (below the front face)
                if self.ch(x, y + 1) not in '# ' and not (self.ch(x, y + 1) in TALL):
                    dark[py + T:py + T + 4, px + 2:px + T + 2] *= 0.7
        # only darken floor-ish pixels (not walls themselves)
        mask = np.zeros_like(dark, dtype=bool)
        for y in range(self.h):
            for x in range(self.w):
                if self.ch(x, y) not in '# ' and self.ch(x, y) not in TALL and self.ch(x, y) not in 'hS':
                    mask[y * T:(y + 1) * T, x * T:(x + 1) * T] = True
        f = np.where(mask, dark, 1.0)[:, :, None]
        a[:, :, :3] = (a[:, :, :3].astype(np.float32) * f).astype(np.uint8)

    def draw_overlay(self):
        o = self.over
        for y in range(self.h):
            for x in range(self.w):
                ch = self.ch(x, y)
                px, py = x * T, y * T
                r = rng_for(self.id, 'ov', x, y)
                if ch == 'T':
                    rad = r.uniform(12, 16)
                    cx, cy = px + 8, py + 6
                    o.disc(cx + 3, cy + 5, rad, (0, 0, 0, 70))
                    o.disc(cx, cy, rad, P['grn1'])
                    o.disc(cx - 2, cy - 2, rad * 0.8, P['grn2'])
                    o.disc(cx - 4, cy - 4, rad * 0.45, P['grn3'])
                    for _ in range(30):
                        a = r.uniform(0, 6.28)
                        d = r.uniform(0, rad * 0.9)
                        import math
                        o.px(int(cx + math.cos(a) * d), int(cy + math.sin(a) * d), r.choice([P['grn3'], P['grn1'], P['grn4']]))
                elif ch == 'B':
                    for (dx, dy, rr) in ((4, 6, 5), (11, 6, 5), (8, 10, 6), (8, 4, 4)):
                        o.disc(px + dx, py + dy, rr, P['grn1'])
                    for (dx, dy, rr) in ((5, 5, 3), (11, 5, 3), (8, 8, 4)):
                        o.disc(px + dx, py + dy, rr, P['grn2'])
                    for _ in range(8):
                        o.px(px + r.randint(1, 14), py + r.randint(1, 13), P['grn3'])
                    if r.random() < 0.15:
                        o.px(px + r.randint(3, 12), py + r.randint(3, 10), P['red3'] if self.meta['theme'] == 'mansion' else P['gold3'])
        # hedges are semi-transparent so the player can see themselves inside
        m = o.a[:, :, 3] > 0
        o.a[m, 3] = np.minimum(o.a[m, 3], 225)

    def render(self):
        for y in range(self.h):
            for x in range(self.w):
                ch = self.ch(x, y)
                if ch == ' ':
                    continue
                if ch == '#':
                    continue
                f = self.floor_of.get((x, y))
                if f:
                    self.draw_floor(x, y, f)
        self.shadows()
        for y in range(self.h):
            for x in range(self.w):
                ch = self.ch(x, y)
                if ch == '#':
                    self.draw_wall(x, y)
                elif ch == '~':
                    self.draw_water(x, y)
                elif ch == '=':
                    self.draw_stairs(x, y)
                elif ch == 'v':
                    self.draw_vent(x, y)
                elif ch in FLOORS or ch in DYNAMIC or ch == ' ':
                    continue
                else:
                    self.draw_furniture(x, y, ch)
        self.draw_overlay()
        os.makedirs(OUT, exist_ok=True)
        self.base.save(os.path.join(OUT, f'{self.id}_base.png'))
        self.over.save(os.path.join(OUT, f'{self.id}_overlay.png'))
        # small preview for the mission-select screen (composited)
        from PIL import Image
        img = Image.alpha_composite(self.base.image(), self.over.image())
        prev = img.resize((img.width // 4, img.height // 4), Image.BILINEAR)
        prev.save(os.path.join(OUT, f'{self.id}_preview.png'))
        return img


if __name__ == '__main__':
    ids = sys.argv[1:] or ['mansion', 'facility', 'office']
    for mid in ids:
        img = MapArt(mid).render()
        print('rendered', mid, img.size)
