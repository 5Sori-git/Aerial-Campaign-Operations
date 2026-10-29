# ACO HUD 라인 아이콘 생성기 (표준 라이브러리만). 좌표는 0..100 단위(y 아래로), 흰색 + 알파(안티앨리어싱).
import math, os, struct, sys, zlib

OUT = sys.argv[1]
SHEET = sys.argv[2]
SIZE = 256
W = 6.0  # 기본 선 두께(단위)


class Canvas:
    def __init__(self, w, h, k, ox=0.0, oy=0.0, buf=None):
        self.w, self.h, self.k, self.ox, self.oy = w, h, k, ox, oy
        self.a = buf if buf is not None else [0.0] * (w * h)

    def region(self, x0, y0, x1, y1, f):
        k = self.k
        ix0 = max(0, int(self.ox + x0 * k) - 2); ix1 = min(self.w - 1, int(self.ox + x1 * k) + 2)
        iy0 = max(0, int(self.oy + y0 * k) - 2); iy1 = min(self.h - 1, int(self.oy + y1 * k) + 2)
        a = self.a
        for py in range(iy0, iy1 + 1):
            uy = (py + 0.5 - self.oy) / k
            row = py * self.w
            for px in range(ix0, ix1 + 1):
                ux = (px + 0.5 - self.ox) / k
                c = f(ux, uy)
                if c > 0.0:
                    i = row + px
                    if c > a[i]:
                        a[i] = c if c < 1.0 else 1.0

    def cov(self, signed):  # signed distance(단위, 안 = 음수) → 덮임
        c = 0.5 - signed * self.k
        return 0.0 if c <= 0.0 else (1.0 if c >= 1.0 else c)


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    l2 = dx * dx + dy * dy
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / l2))
    qx, qy = ax + t * dx - px, ay + t * dy - py
    return math.sqrt(qx * qx + qy * qy)


def seg(c, a, b, w=W):
    hw = w * 0.5
    c.region(min(a[0], b[0]) - hw, min(a[1], b[1]) - hw, max(a[0], b[0]) + hw, max(a[1], b[1]) + hw,
             lambda x, y: c.cov(seg_dist(x, y, a[0], a[1], b[0], b[1]) - hw))


def poly(c, pts, w=W, closed=True):
    n = len(pts)
    for i in range(n if closed else n - 1):
        seg(c, pts[i], pts[(i + 1) % n], w)


def fill_poly(c, pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    n = len(pts)

    def f(x, y):
        inside = False
        d = 1e9
        j = n - 1
        for i in range(n):
            xi, yi = pts[i]; xj, yj = pts[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                inside = not inside
            dd = seg_dist(x, y, xi, yi, xj, yj)
            if dd < d: d = dd
            j = i
        return c.cov(-d if inside else d)
    c.region(min(xs), min(ys), max(xs), max(ys), f)


def fill_circle(c, cx, cy, r):
    c.region(cx - r, cy - r, cx + r, cy + r, lambda x, y: c.cov(math.hypot(x - cx, y - cy) - r))


def arc_pts(cx, cy, r, a0, a1, step=3.0):
    n = max(2, int(abs(a1 - a0) / step) + 1)
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / (n - 1))),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / (n - 1)))) for i in range(n)]


def arc(c, cx, cy, r, a0, a1, w=W):
    poly(c, arc_pts(cx, cy, r, a0, a1), w, closed=False)


def circle(c, cx, cy, r, w=W):
    poly(c, arc_pts(cx, cy, r, 0, 360, 4.0)[:-1], w, closed=True)


def head(c, tip, ang, size=13.0, spread=30.0):
    """채운 화살촉: tip에서 ang(도, 진행 방향) 쪽을 가리킨다."""
    a = math.radians(ang)
    b1 = math.radians(ang + 180 - spread); b2 = math.radians(ang + 180 + spread)
    fill_poly(c, [tip, (tip[0] + size * math.cos(b1), tip[1] + size * math.sin(b1)),
                  (tip[0] + size * math.cos(b2), tip[1] + size * math.sin(b2))])


def xform(pts, cx, cy, ang, s=1.0):
    a = math.radians(ang); ca, sa = math.cos(a), math.sin(a)
    return [(cx + s * (x * ca - y * sa), cy + s * (x * sa + y * ca)) for x, y in pts]


def missile_pts(L, bw, nl, fw, fl):
    h = L * 0.5
    return [(-h, -bw - fw), (-h + fl, -bw), (h - nl, -bw), (h, 0), (h - nl, bw), (-h + fl, bw), (-h, bw + fw)]


def missile(c, cx, cy, ang, L, bw, nl, fw, fl, fill=False, w=W):
    pts = xform(missile_pts(L, bw, nl, fw, fl), cx, cy, ang)
    if fill: fill_poly(c, pts)
    else: poly(c, pts, w)


JET = [(50, 6), (56, 24), (57, 42), (90, 62), (90, 70), (57, 63), (56, 78), (70, 88), (70, 94), (50, 90),
       (30, 94), (30, 88), (44, 78), (43, 63), (10, 70), (10, 62), (43, 42), (44, 24)]


def jet(c, cx, cy, s, fill=False, w=W):
    pts = [(cx + (x - 50) * s, cy + (y - 50) * s) for x, y in JET]
    if fill: fill_poly(c, pts)
    else: poly(c, pts, w)


def bomb(c, cx, cy, s=1.0, w=W):
    # 기수가 아래. 몸통 타원 + 꼬리 상자.
    body = [(cx + 14 * s * math.cos(math.radians(t)), cy + 6 * s + 28 * s * math.sin(math.radians(t))) for t in range(0, 360, 8)]
    poly(c, body, w)
    poly(c, [(cx - 12 * s, cy - 40 * s), (cx + 12 * s, cy - 40 * s), (cx + 12 * s, cy - 30 * s), (cx - 12 * s, cy - 30 * s)], w)
    seg(c, (cx - 6 * s, cy - 30 * s), (cx - 7 * s, cy - 20 * s), w)
    seg(c, (cx + 6 * s, cy - 30 * s), (cx + 7 * s, cy - 20 * s), w)


def rrect(c, x0, y0, x1, y1, r, w=W):
    pts = (arc_pts(x1 - r, y0 + r, r, 270, 360) + arc_pts(x1 - r, y1 - r, r, 0, 90)
           + arc_pts(x0 + r, y1 - r, r, 90, 180) + arc_pts(x0 + r, y0 + r, r, 180, 270))
    poly(c, pts, w)


# ───────────── 아이콘 ─────────────

def i_hull(c):
    poly(c, [(50, 8), (84, 20), (82, 52), (70, 72), (50, 90), (30, 72), (18, 52), (16, 20)])
    seg(c, (50, 34), (50, 66)); seg(c, (34, 50), (66, 50))


def i_mobility(c):
    arc(c, 50, 50, 32, 205, 335); head(c, (50 + 32 * math.cos(math.radians(345)), 50 + 32 * math.sin(math.radians(345))), 345 + 90)
    arc(c, 50, 50, 32, 25, 155); head(c, (50 + 32 * math.cos(math.radians(165)), 50 + 32 * math.sin(math.radians(165))), 165 + 90)
    jet(c, 50, 50, 0.32, fill=True)


def i_engine(c):
    poly(c, [(12, 34), (42, 28), (42, 72), (12, 66)])
    poly(c, [(50, 30), (92, 50), (50, 70)])
    fill_poly(c, [(50, 41), (73, 50), (50, 59)])


def i_fuel(c):
    cx, cy, r = 50, 60, 26
    t = math.degrees(math.asin(r / 50.0))
    a0, a1 = -90 + (90 - t), -90 - (90 - t) + 360
    poly(c, [(50, 10)] + arc_pts(cx, cy, r, a0, a1), closed=True)
    arc(c, cx, cy, 14, 105, 165, 5)


def i_stability(c):
    poly(c, [(10, 74), (14, 68), (24, 63), (40, 61), (56, 62), (72, 66), (90, 75),
             (72, 77), (56, 78), (40, 79), (24, 79), (14, 78)])
    for x, top in ((28, 30), (50, 20), (72, 30)):
        seg(c, (x, 54), (x, top + 8)); head(c, (x, top - 2), 270, 14)


def i_damage(c):
    pts = []
    for i in range(16):
        r = 44 if i % 2 == 0 else 20
        a = math.radians(-90 + i * 22.5)
        pts.append((50 + r * math.cos(a), 50 + r * math.sin(a)))
    poly(c, pts)
    fill_circle(c, 50, 50, 6)


def i_ammo(c):
    for x in (20, 40, 60):
        missile(c, x, 50, -90, 80, 5, 14, 5, 12, fill=True)
    seg(c, (82, 38), (82, 62)); seg(c, (70, 50), (94, 50))


def i_turn(c):
    seg(c, (28, 90), (28, 46))
    arc(c, 50, 46, 22, 180, 360)
    seg(c, (72, 46), (72, 64))
    head(c, (72, 82), 90, 15)


def i_lock_speed(c):
    circle(c, 62, 50, 24)
    seg(c, (62, 16), (62, 30)); seg(c, (62, 70), (62, 84)); seg(c, (82, 50), (96, 50)); seg(c, (28, 50), (42, 50))
    fill_circle(c, 62, 50, 4.5)
    seg(c, (4, 34), (22, 34)); seg(c, (10, 66), (22, 66))


def i_lock_range(c):
    fill_circle(c, 50, 88, 6)
    for r in (26, 48, 70):
        arc(c, 50, 88, r, 232, 308)


def i_lock_angle(c):
    ax, ay = 50, 88
    for a in (232, 308):
        seg(c, (ax, ay), (ax + 76 * math.cos(math.radians(a)), ay + 76 * math.sin(math.radians(a))))
    arc(c, ax, ay, 50, 246, 294)
    head(c, (ax + 50 * math.cos(math.radians(238)), ay + 50 * math.sin(math.radians(238))), 238 - 90, 13)
    head(c, (ax + 50 * math.cos(math.radians(302)), ay + 50 * math.sin(math.radians(302))), 302 + 90, 13)
    fill_circle(c, ax, ay, 6)


def i_blast(c):
    fill_circle(c, 50, 50, 9)
    circle(c, 50, 50, 22)
    for i in range(4):
        m = 45 + i * 90
        arc(c, 50, 50, 36, m - 16, m + 16)
    for a in (0, 90, 180, 270):
        head(c, (50 + 48 * math.cos(math.radians(a)), 50 + 48 * math.sin(math.radians(a))), a, 12)


def i_reload(c):
    arc(c, 50, 50, 36, -60, 230)
    head(c, (50 + 36 * math.cos(math.radians(-60)), 50 + 36 * math.sin(math.radians(-60))), -60 + 90 + 180, 15)
    missile(c, 50, 52, -90, 46, 5, 11, 6, 9, fill=True)


def i_tab_airframe(c):
    jet(c, 50, 50, 1.0, w=5)


def i_tab_missile(c):
    missile(c, 50, 50, -45, 92, 7, 18, 9, 16)


def i_tab_sp_aam(c):
    missile(c, 40, 60, -45, 70, 6, 14, 8, 13)
    jet(c, 80, 20, 0.30, fill=True)


def i_tab_sp_agm(c):
    missile(c, 40, 40, 45, 66, 6, 14, 8, 13)
    poly(c, [(70, 70), (88, 70), (88, 88), (70, 88)], 5)
    seg(c, (6, 94), (94, 94), 5)


def i_tab_sp_bomb(c):
    bomb(c, 50, 50, 1.0)


def i_w_4aam(c):
    for i, x in enumerate((17, 39, 61, 83)):
        missile(c, x, 54 if i % 2 == 0 else 46, -90, 74, 4.5, 13, 5, 11, fill=True)


def i_w_ugb(c):
    bomb(c, 50, 60, 0.85)
    for x in (40, 50, 60):
        seg(c, (x, 4), (x, 16), 4)


def i_w_rkt(c):
    rrect(c, 8, 30, 64, 70, 8)
    for y in (40, 50, 60):
        seg(c, (64, y), (80, y), 4)
        fill_poly(c, [(78, y - 4.5), (92, y), (78, y + 4.5)])


def dashed(c, a, b, dash=7.0, gap=5.0, w=5.0):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    if L == 0: return
    ux, uy = dx / L, dy / L
    t = 0.0
    while t < L:
        e = min(t + dash, L)
        seg(c, (a[0] + ux * t, a[1] + uy * t), (a[0] + ux * e, a[1] + uy * e), w)
        t = e + gap


def i_w_4agm(c):
    for i, x in enumerate((17, 39, 61, 83)):
        missile(c, x, 40 if i % 2 == 0 else 46, 90, 62, 4.5, 12, 5, 10, fill=True)
    seg(c, (6, 92), (94, 92), 5)


def i_w_lagm(c):
    missile(c, 26, 26, 45, 46, 5.5, 12, 7, 10)
    dashed(c, (46, 46), (70, 70))
    poly(c, [(72, 72), (90, 72), (90, 90), (72, 90)], 5)


def i_w_laam(c):
    missile(c, 24, 50, 0, 42, 5.5, 12, 7, 10)
    dashed(c, (50, 50), (74, 50))
    jet(c, 87, 50, 0.24, fill=True)


def i_w_sasm(c):
    # 근거리 대공 제압: 미사일 → 큰 근접 신관 원(점선) 안의 적기 두 대
    missile(c, 22, 78, -45, 42, 5.5, 12, 7, 10)
    for i in range(12):
        a = i * 30
        arc(c, 64, 36, 30, a, a + 16, 4.5)
    jet(c, 56, 30, 0.22, fill=True)
    jet(c, 74, 44, 0.22, fill=True)


def i_w_qaam(c):
    pts = arc_pts(52, 82, 34, 180, 270, 6.0)
    for i in range(0, len(pts) - 1, 2):
        seg(c, pts[i], pts[i + 1], 5)
    missile(c, 72, 48, 0, 40, 5.5, 12, 7, 10)


def i_w_hpaa(c):
    missile(c, 42, 58, -45, 62, 9, 15, 9, 12, fill=True)
    pts = []
    for i in range(12):
        r = 15 if i % 2 == 0 else 7
        a = math.radians(-90 + i * 30)
        pts.append((80 + r * math.cos(a), 20 + r * math.sin(a)))
    poly(c, pts, 4.5)


def i_w_hvaa(c):
    missile(c, 60, 50, 0, 62, 6, 14, 8, 12)
    seg(c, (6, 36), (24, 36), 5); seg(c, (2, 50), (24, 50), 5); seg(c, (6, 64), (24, 64), 5)


def i_w_none(c):
    circle(c, 50, 50, 38)
    seg(c, (23, 77), (77, 23))


def i_lock(c):
    arc(c, 50, 42, 18, 180, 360)
    seg(c, (32, 42), (32, 50)); seg(c, (68, 42), (68, 50))
    rrect(c, 22, 50, 78, 90, 7)
    fill_circle(c, 50, 66, 6)
    seg(c, (50, 66), (50, 80), 5)


def i_credits(c):
    # 동전 두 개: 뒤 동전은 앞 동전에 가려지지 않는 테두리만, 앞 동전은 안쪽 고리 + 마름모.
    arc(c, 60, 40, 30, 200, 425)
    circle(c, 42, 58, 30)
    circle(c, 42, 58, 20, 3)
    fill_poly(c, [(42, 48), (50, 58), (42, 68), (34, 58)])


ICONS = [
    ("hull", i_hull), ("mobility", i_mobility), ("engine", i_engine), ("fuel", i_fuel), ("stability", i_stability),
    ("damage", i_damage), ("ammo", i_ammo), ("turn", i_turn), ("lock_speed", i_lock_speed), ("lock_range", i_lock_range),
    ("lock_angle", i_lock_angle), ("blast", i_blast), ("reload", i_reload),
    ("tab_airframe", i_tab_airframe), ("tab_missile", i_tab_missile), ("tab_sp_aam", i_tab_sp_aam),
    ("tab_sp_agm", i_tab_sp_agm), ("tab_sp_bomb", i_tab_sp_bomb),
    ("weapon_4aam", i_w_4aam), ("weapon_ugb", i_w_ugb), ("weapon_rkt", i_w_rkt), ("weapon_none", i_w_none),
    ("weapon_4agm", i_w_4agm), ("weapon_lagm", i_w_lagm), ("weapon_laam", i_w_laam), ("weapon_sasm", i_w_sasm),
    ("weapon_qaam", i_w_qaam), ("weapon_hpaa", i_w_hpaa), ("weapon_hvaa", i_w_hvaa),
    ("lock", i_lock), ("credits", i_credits),
]


def write_png(path, w, h, rows_rgba):
    raw = b"".join(b"\x00" + r for r in rows_rgba)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


os.makedirs(OUT, exist_ok=True)
cols, cell, pad = 6, 120, 12
rows = (len(ICONS) + cols - 1) // cols
sw, sh = cols * cell, rows * cell
sheet = Canvas(sw, sh, 1.0)
for idx, (name, fn) in enumerate(ICONS):
    c = Canvas(SIZE, SIZE, SIZE / 100.0)
    fn(c)
    rows_rgba = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            row += bytes((255, 255, 255, int(c.a[y * SIZE + x] * 255 + 0.5)))
        rows_rgba.append(bytes(row))
    write_png(os.path.join(OUT, "aco_icon_" + name + ".png"), SIZE, SIZE, rows_rgba)
    sc = Canvas(sw, sh, (cell - 2 * pad) / 100.0, (idx % cols) * cell + pad, (idx // cols) * cell + pad, sheet.a)
    fn(sc)
    print(name)

bg = (14, 38, 22); fg = (110, 255, 140)
rows_rgba = []
for y in range(sh):
    row = bytearray()
    for x in range(sw):
        a = sheet.a[y * sw + x]
        cx, cy = x % cell, y % cell
        b = (24, 60, 34) if (cx < 2 or cy < 2) else bg
        row += bytes((int(b[0] + (fg[0] - b[0]) * a), int(b[1] + (fg[1] - b[1]) * a), int(b[2] + (fg[2] - b[2]) * a), 255))
    rows_rgba.append(bytes(row))
write_png(SHEET, sw, sh, rows_rgba)
print("sheet", SHEET)
