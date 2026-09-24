"""Minimal pixel-art drawing helpers on numpy RGBA arrays (uint8), plus the shared palette."""
import random
import numpy as np
from PIL import Image


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# A single dark-tactical palette used by every generated asset keeps the art direction consistent.
P = {k: hexc(v) for k, v in dict(
    black='0b0d12', ink='151a22', night='1f2630', slate='2b3440', steel='3a4552', iron='4f5d6c', ash='6b7b8c',
    fog='8d9bab', mist='b5c0cc', snow='e1e6ec', white='f4f6f8',
    wood0='2a1a14', wood1='432a1e', wood2='5e3b26', wood3='7d5234', wood4='a06c43', wood5='c48d5a',
    red0='3d0f14', red1='6b1a22', red2='9c2632', red3='d13f3f', red4='ef7a6a',
    gold0='5a4318', gold1='8a6a2a', gold2='c79a3a', gold3='f0c75e',
    blue0='13203a', blue1='1d3357', blue2='2d4e7e', blue3='4a78b0', blue4='7fb0e0',
    grn0='13261c', grn1='1f3d2a', grn2='2f5a3a', grn3='4a7d4c', grn4='7aa865',
    teal0='12343b', teal1='1b4d57', teal2='2f8190', teal3='59c1c9',
    pur0='2a1a3a', pur1='4a2d63', pur2='7a4f9a',
    skin0='f2c7a5', skin1='d9a07a', skin2='a86a4a', skin3='6e4430',
    coat='dfe6ea', cream='e9dcc0', sand='b8a17a', sand2='8f7a57',
).items()}


def shade(c, f):
    """Multiply RGB by f (keeps alpha)."""
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))), max(0, min(255, int(c[2] * f))), c[3])


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


class Canvas:
    def __init__(self, w, h, fill=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.a = np.zeros((h, w, 4), dtype=np.uint8)
        if fill[3]:
            self.a[:, :] = fill

    def px(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            if c[3] == 255 or c[3] == 0:
                if c[3]:
                    self.a[y, x] = c
            else:
                t = c[3] / 255.0
                old = self.a[y, x].astype(float)
                new = np.array(c[:3], dtype=float) * t + old[:3] * (1 - t)
                self.a[y, x, :3] = new.astype(np.uint8)
                self.a[y, x, 3] = max(int(old[3]), c[3])

    def rect(self, x, y, w, h, c):
        x0, y0, x1, y1 = max(0, x), max(0, y), min(self.w, x + w), min(self.h, y + h)
        if x1 <= x0 or y1 <= y0:
            return
        if c[3] == 255:
            self.a[y0:y1, x0:x1] = c
        else:
            for yy in range(y0, y1):
                for xx in range(x0, x1):
                    self.px(xx, yy, c)

    def outline(self, x, y, w, h, c):
        self.rect(x, y, w, 1, c)
        self.rect(x, y + h - 1, w, 1, c)
        self.rect(x, y, 1, h, c)
        self.rect(x + w - 1, y, 1, h, c)

    def line(self, x0, y0, x1, y1, c):
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        while True:
            self.px(x0, y0, c)
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def disc(self, cx, cy, r, c):
        for y in range(int(cy - r - 1), int(cy + r + 2)):
            for x in range(int(cx - r - 1), int(cx + r + 2)):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.px(x, y, c)

    def ring(self, cx, cy, r, c, width=1.0):
        for y in range(int(cy - r - 2), int(cy + r + 3)):
            for x in range(int(cx - r - 2), int(cx + r + 3)):
                d = ((x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2) ** 0.5
                if r - width <= d <= r:
                    self.px(x, y, c)

    def ellipse(self, cx, cy, rx, ry, c):
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1:
                    self.px(x, y, c)

    def blit(self, other, x, y):
        for yy in range(other.h):
            for xx in range(other.w):
                c = tuple(int(v) for v in other.a[yy, xx])
                if c[3]:
                    self.px(x + xx, y + yy, c)

    def noise(self, x, y, w, h, base, amount, rng, step=1):
        """Fill a rect with base colour plus random brightness jitter (+/-amount)."""
        for yy in range(y, y + h, step):
            for xx in range(x, x + w, step):
                f = 1 + rng.uniform(-amount, amount)
                self.rect(xx, yy, step, step, shade(base, f))

    def outline_alpha(self, c):
        """1px outline around all opaque pixels (for sprites)."""
        a = self.a
        mask = a[:, :, 3] > 0
        out = np.zeros_like(mask)
        out[1:, :] |= mask[:-1, :]
        out[:-1, :] |= mask[1:, :]
        out[:, 1:] |= mask[:, :-1]
        out[:, :-1] |= mask[:, 1:]
        out &= ~mask
        a[out] = c

    def image(self):
        return Image.fromarray(self.a, 'RGBA')

    def save(self, path):
        self.image().save(path)


def rng_for(*keys):
    """Deterministic RNG from any keys (stable across runs, unlike hash())."""
    import zlib
    return random.Random(zlib.crc32(repr(keys).encode()))
