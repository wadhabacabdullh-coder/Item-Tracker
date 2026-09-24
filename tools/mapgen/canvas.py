"""Tiny authoring helper for Shadow Contract maps.

Maps are drawn on a character canvas using (col, row) coordinates where row 0 is the TOP line,
exactly like the text file. Entities are written with the same coordinates ("col,row").
See Assets/Scripts/Core/World/Tiles.cs for the tile legend.
"""


class Canvas:
    def __init__(self, w, h, fill=' '):
        self.w, self.h = w, h
        self.g = [[fill] * w for _ in range(h)]
        self.entities = []          # base entity lines
        self.missions = {}          # mission id -> entity lines
        self.meta = {}

    # ------------------------------------------------------------ drawing
    def put(self, x, y, ch):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.g[y][x] = ch

    def get(self, x, y):
        return self.g[y][x] if 0 <= x < self.w and 0 <= y < self.h else ' '

    def fill(self, x0, y0, x1, y1, ch):
        """Fill inclusive rectangle."""
        for y in range(min(y0, y1), max(y0, y1) + 1):
            for x in range(min(x0, x1), max(x0, x1) + 1):
                self.put(x, y, ch)

    def room(self, x0, y0, x1, y1, floor='.', wall='#'):
        """Walls on the border of the inclusive rectangle, floor inside."""
        self.fill(x0, y0, x1, y1, wall)
        self.fill(x0 + 1, y0 + 1, x1 - 1, y1 - 1, floor)

    def hline(self, x0, x1, y, ch='#'):
        self.fill(x0, y, x1, y, ch)

    def vline(self, x, y0, y1, ch='#'):
        self.fill(x, y0, x, y1, ch)

    def text(self, x, y, s):
        for i, ch in enumerate(s):
            if ch != '`':           # backtick = leave unchanged
                self.put(x + i, y, ch)

    def block(self, x, y, rows):
        for j, r in enumerate(rows):
            self.text(x, y + j, r)

    def scatter(self, x0, y0, x1, y1, ch, every, on='.,:;_"'):
        """Place ch on every n-th floor tile of a rectangle (deterministic pattern)."""
        i = 0
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if self.get(x, y) in on:
                    i += 1
                    if i % every == 0:
                        self.put(x, y, ch)

    # ------------------------------------------------------------ entities
    def ent(self, line, mission=None):
        (self.missions.setdefault(mission, []) if mission else self.entities).append(line)

    @staticmethod
    def pt(x, y):
        return f"{x},{y}"

    @staticmethod
    def route(*pts):
        """route((x,y,wait,look,activity), ...) -> '12,3:2:90|...'"""
        out = []
        for p in pts:
            s = f"{p[0]},{p[1]}"
            rest = [str(v) if v is not None else '' for v in p[2:]]
            while rest and rest[-1] == '':
                rest.pop()
            if rest:
                s += ':' + ':'.join(rest)
            out.append(s)
        return '"' + '|'.join(out) + '"'

    # ------------------------------------------------------------ output
    def validate(self, walkable_needed=()):
        for (x, y) in walkable_needed:
            ch = self.get(x, y)
            if ch in '# W~ht rxmfCTipPjy':
                raise ValueError(f"point {x},{y} is on blocking tile '{ch}'")

    def render(self):
        out = ["@meta"]
        for k, v in self.meta.items():
            out.append(f"{k}: {v}")
        out.append("")
        out.append("@tiles")
        out += [''.join(r).rstrip() or ' ' for r in self.g]
        out.append("")
        out.append("@entities")
        out += self.entities
        for mid, lines in self.missions.items():
            out.append("")
            out.append(f"@mission {mid}")
            out += lines
        return '\n'.join(out) + '\n'

    WALKABLE = set('.:;_,"=cB')

    def check_points(self):
        """Every coordinate used by an entity must be on a walkable tile."""
        import re
        errors = []
        lines = list(self.entities) + [l for ls in self.missions.values() for l in ls]
        for line in lines:
            kind = line.split()[0]
            if kind in ('light', 'zone', 'extract', 'camera', 'powerbox', 'terminal', 'distraction'):
                continue
            for key, body in re.findall(r'(\w+)=("[^"]*"|\S+)', line):
                if key not in ('at', 'a', 'b', 'escape', 'route'):
                    continue
                for m in re.finditer(r'(\d+),(\d+)', body):
                    x, y = int(m.group(1)), int(m.group(2))
                    ch = self.get(x, y)
                    if ch not in self.WALKABLE:
                        errors.append(f"{kind} {key}: {x},{y} is '{ch}'  <- {line[:60]}")
        if errors:
            raise ValueError("\n".join(errors))

    def save(self, path):
        self.check_points()
        with open(path, 'w') as f:
            f.write(self.render())

    def show(self):
        for i, r in enumerate(self.g):
            print(f"{i:3d} " + ''.join(r))
