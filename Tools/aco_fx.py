# 폭발 효과 스프라이트 생성기(순수 파이썬, 외부 라이브러리 없음).
# 사용: python Tools/aco_fx.py <출력 폴더> [미리보기.png]
# 만드는 것(모두 RGBA PNG, 알파 = 투명도, RGB는 어둡게 깎지 않은 색 — 알파 블렌드/가산 둘 다 쓸 수 있다):
#   fx_fireball_8x8.png  1024²  불덩이 플립북 64프레임(작은 흰 섬광 → 부푸는 화염 → 검붉은 → 연기로 흩어짐)
#   fx_smoke_8x8.png     512²   연기 뭉치 플립북 64프레임(밝은 회색 — Start Color로 물들여 쓴다)
#   fx_flash.png         256²   섬광(둥근 빛 + 빛줄기)
#   fx_shockwave.png     256²   충격파 고리
#   fx_spark.png         64²    불티(뜨거운 점 — Stretched Billboard로 늘려 쓴다)
import math, os, struct, sys, zlib

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None

# ───────────── 값 노이즈 ─────────────
_perm = list(range(256))
_seed = 1337
for i in range(255, 0, -1):
    _seed = (_seed * 1103515245 + 12345) & 0x7fffffff
    j = _seed % (i + 1)
    _perm[i], _perm[j] = _perm[j], _perm[i]
_perm = _perm * 2
_val = [p / 255.0 for p in _perm]


_GRAD = [(math.cos(k * math.pi / 8 + 0.3), math.sin(k * math.pi / 8 + 0.3)) for k in range(16)]


def noise(x, y):
    """그래디언트(펄린) 노이즈 0~1 — 값 노이즈보다 격자 무늬가 덜 보인다."""
    xi = math.floor(x); yi = math.floor(y)
    xf = x - xi; yf = y - yi
    xi &= 255; yi &= 255
    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10); v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
    p = _perm
    g = _GRAD
    ga = g[p[p[xi] + yi] & 15]; gb = g[p[p[xi + 1] + yi] & 15]
    gc = g[p[p[xi] + yi + 1] & 15]; gd = g[p[p[xi + 1] + yi + 1] & 15]
    a = ga[0] * xf + ga[1] * yf
    b = gb[0] * (xf - 1) + gb[1] * yf
    c = gc[0] * xf + gc[1] * (yf - 1)
    d = gd[0] * (xf - 1) + gd[1] * (yf - 1)
    top = a + (b - a) * u
    bot = c + (d - c) * u
    return 0.5 + 0.7 * (top + (bot - top) * v)


# 옥타브마다 좌표를 돌려 격자 방향이 겹치지 않게 한다
_ROT = [(math.cos(o * 0.9), math.sin(o * 0.9)) for o in range(8)]


def fbm(x, y, t=0.0, octaves=4):
    s = 0.0; amp = 0.5; norm = 0.0; f = 1.0
    for o in range(octaves):
        c, sn = _ROT[o]
        px = (x * c - y * sn) * f; py = (x * sn + y * c) * f
        # 옥타브마다 다른 방향으로 흘려 시간에 따라 꿈틀거리게 한다
        s += amp * noise(px + t * (0.7 + o * 0.45), py - t * (0.4 + o * 0.3) + o * 17.3)
        norm += amp; amp *= 0.5; f *= 2.03
    return s / norm


def billow(x, y, t=0.0, octaves=4):
    """뭉게구름 노이즈: 옥타브마다 |2n−1| — 둥근 덩어리 사이에 접힌 골이 생긴다."""
    s = 0.0; amp = 0.5; norm = 0.0; f = 1.0
    for o in range(octaves):
        c, sn = _ROT[o]
        px = (x * c - y * sn) * f; py = (x * sn + y * c) * f
        v = noise(px + t * (0.7 + o * 0.45), py - t * (0.4 + o * 0.3) + o * 17.3)
        s += amp * abs(2.0 * v - 1.0)
        norm += amp; amp *= 0.5; f *= 2.03
    return s / norm


def cloud(x, y, t=0.0, octaves=4):
    """덩어리감(billow)과 부드러움(fbm)을 섞는다."""
    return 0.55 * contrast(billow(x, y, t, octaves) * 1.7, 1.2) + 0.45 * contrast(fbm(x + 31.0, y + 7.0, t, octaves), 1.6)


def clamp01(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def smooth(e0, e1, v):
    t = clamp01((v - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def ramp(stops, v):
    if v <= stops[0][0]: return stops[0][1]
    for k in range(1, len(stops)):
        if v <= stops[k][0]:
            a0, c0 = stops[k - 1]; a1, c1 = stops[k]
            f = (v - a0) / (a1 - a0)
            return (c0[0] + (c1[0] - c0[0]) * f, c0[1] + (c1[1] - c0[1]) * f, c0[2] + (c1[2] - c0[2]) * f)
    return stops[-1][1]


# 열 → 색: 연기(어두운 회색) → 검붉은 → 주황 → 노랑 → 흰빛
HEAT = [
    (0.00, (0.22, 0.21, 0.20)),
    (0.18, (0.26, 0.17, 0.12)),
    (0.32, (0.55, 0.14, 0.05)),
    (0.50, (0.95, 0.38, 0.08)),
    (0.70, (1.00, 0.68, 0.20)),
    (0.88, (1.00, 0.90, 0.55)),
    (1.00, (1.00, 0.98, 0.88)),
]


class Img:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [(0.0, 0.0, 0.0, 0.0)] * (w * h)

    def set(self, x, y, c):
        self.px[y * self.w + x] = c

    def save(self, path):
        rows = []
        for y in range(self.h):
            row = bytearray()
            for x in range(self.w):
                r, g, b, a = self.px[y * self.w + x]
                row += bytes((int(clamp01(r) * 255 + 0.5), int(clamp01(g) * 255 + 0.5), int(clamp01(b) * 255 + 0.5), int(clamp01(a) * 255 + 0.5)))
            rows.append(bytes(row))
        write_png(path, self.w, self.h, rows)


def write_png(path, w, h, rows_rgba):
    raw = b"".join(b"\x00" + r for r in rows_rgba)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


def flipbook(img, cols, rows, cell, frame_fn):
    """프레임 fn(t, x, y) → rgba, x/y는 −1~1(가운데 0, 위 = +y). Unity 텍스처 시트는 왼쪽 위부터 오른쪽으로 읽는다."""
    n = cols * rows
    for i in range(n):
        t = i / (n - 1)
        ox = (i % cols) * cell
        oy = (i // cols) * cell
        for py in range(cell):
            y = 1.0 - (py + 0.5) / cell * 2.0
            for px in range(cell):
                x = (px + 0.5) / cell * 2.0 - 1.0
                c = frame_fn(t, x, y)
                if c is not None:
                    img.set(ox + px, oy + py, c)


# ───────────── 불덩이 ─────────────
def contrast(v, k):
    return clamp01((v - 0.5) * k + 0.5)


def warp(qx, qy, t, amount):
    wx = fbm(qx + 3.1, qy + 8.7, t * 0.8, 3) - 0.5
    wy = fbm(qx + 13.4, qy + 2.2, t * 0.8, 3) - 0.5
    return qx + wx * amount, qy + wy * amount


def fireball(t, x, y):
    grow = 1.0 - (1.0 - t) ** 2.2
    R = 0.3 + 0.6 * grow
    r = math.sqrt(x * x + y * y)
    if r > R * 1.12:
        return None
    qx, qy = warp(x / R * 1.8, y / R * 1.8, t * 1.5, 0.6)
    edge = contrast(fbm(qx * 0.8 + 40.0, qy * 0.8 + 11.0, t, 3), 1.6)
    rr = r / (R * (0.72 + 0.38 * edge))
    if rr >= 1.0:
        return None
    n = cloud(qx + 7.0, qy + 3.0, t * 1.6, 5)
    n2 = cloud(qx + 7.0 - 0.07, qy + 3.0 + 0.07, t * 1.6, 5)   # 왼쪽 위에서 빛: 기울기로 뭉게 명암
    # 덩어리 질감: 가장자리는 부드럽게, 안은 뭉게뭉게. 시간이 갈수록 노이즈 문턱이 올라가 흩어진다
    den = smooth(1.0, 0.72, rr) * (0.7 + 0.3 * n)
    brk = smooth(0.55, 1.0, t) * 0.8
    den *= smooth(brk - 0.12, brk + 0.12, n)
    # 열: 처음엔 거의 하얗게, 0.45쯤 주황·검붉게, 0.75 넘으면 연기
    heat_t = clamp01(1.2 - 1.55 * t)
    heat = clamp01(heat_t * (1.35 - rr * 0.95) * (0.55 + 0.75 * n))
    col = ramp(HEAT, heat)
    lit = clamp01(0.85 + 1.6 * (n - n2) + 0.12 * (y / R))
    smoke = 1.0 - smooth(0.12, 0.42, heat)
    k = 1.0 + (lit * 1.25 - 1.0) * (0.35 + 0.65 * smoke)
    col = (col[0] * k, col[1] * k, col[2] * k)
    a = den * (1.0 - smooth(0.82, 1.0, t))
    a = clamp01(a * (1.0 + 0.5 * (1.0 - smooth(0.0, 0.12, t))))
    return (col[0], col[1], col[2], a)


# ───────────── 연기 뭉치 ─────────────
def smoke(t, x, y):
    grow = 1.0 - (1.0 - t) ** 2
    R = 0.5 + 0.4 * grow
    r = math.sqrt(x * x + y * y)
    if r > R * 1.12:
        return None
    qx, qy = warp(x / R * 2.0, y / R * 2.0, t, 0.7)
    edge = contrast(fbm(qx + 90.0, qy + 30.0, t * 0.8, 3), 1.6)
    rr = r / (R * (0.72 + 0.38 * edge))
    if rr >= 1.0:
        return None
    n = cloud(qx + 61.0, qy + 5.0, t, 4)
    n2 = cloud(qx + 61.0 - 0.07, qy + 5.0 + 0.07, t, 4)
    den = smooth(1.0, 0.6, rr) * (0.65 + 0.35 * n)
    brk = smooth(0.3, 1.0, t) * 0.6
    den *= smooth(brk - 0.25, brk + 0.25, n)
    a = den * 0.95 * (1.0 - smooth(0.45, 1.0, t)) * smooth(0.0, 0.06, t + 0.01)
    lit = clamp01(0.7 + 1.5 * (n - n2) + 0.15 * (y / R))
    v = 0.5 + 0.45 * lit
    return (v, v, v * 0.98, clamp01(a))


# ───────────── 단일 텍스처 ─────────────
def single(size, fn):
    img = Img(size, size)
    for py in range(size):
        y = 1.0 - (py + 0.5) / size * 2.0
        for px in range(size):
            x = (px + 0.5) / size * 2.0 - 1.0
            c = fn(x, y)
            if c is not None:
                img.set(px, py, c)
    return img


def flash(x, y):
    r = math.sqrt(x * x + y * y)
    if r >= 1.0:
        return None
    ang = math.atan2(y, x)
    core = math.exp(-r * r * 30.0)
    glow = math.exp(-r * r * 6.0) * 0.55
    rays = 0.0
    for k, (w, ln) in enumerate(((0.06, 1.0), (0.05, 0.75), (0.07, 0.9), (0.05, 0.6), (0.06, 0.95), (0.04, 0.7), (0.06, 0.85), (0.05, 0.65))):
        a0 = k * math.pi / 4 + 0.13 * math.sin(k * 2.7)
        d = math.atan2(math.sin(ang - a0), math.cos(ang - a0))
        rays += math.exp(-(d / w) ** 2) * clamp01(1.0 - r / ln) ** 1.6
    a = clamp01(core + glow + rays * 0.55) * smooth(1.0, 0.85, r)
    warm = clamp01(r * 1.6)
    return (1.0, 0.97 - 0.12 * warm, 0.85 - 0.4 * warm, a)


def shockwave(x, y):
    r = math.sqrt(x * x + y * y)
    if r >= 1.0:
        return None
    ang = math.atan2(y, x)
    n = fbm(math.cos(ang) * 3.0 + 5.0, math.sin(ang) * 3.0 + 5.0, 0.0, 3)
    ring_r = 0.78 + 0.05 * (n - 0.5)
    ring = math.exp(-((r - ring_r) / (0.045 + 0.03 * n)) ** 2)
    inner = smooth(ring_r, ring_r - 0.35, r) * smooth(0.0, ring_r, r) * 0.18
    a = clamp01(ring * (0.65 + 0.35 * n) + inner) * smooth(1.0, 0.93, r)
    return (1.0, 1.0, 1.0, a)


def spark(x, y):
    r = math.sqrt(x * x + y * y)
    if r >= 1.0:
        return None
    a = clamp01(math.exp(-r * r * 9.0) * 1.1) * smooth(1.0, 0.8, r)
    hot = math.exp(-r * r * 30.0)
    return (1.0, 0.75 + 0.25 * hot, 0.35 + 0.6 * hot, a)


os.makedirs(OUT, exist_ok=True)
outputs = []

fire = Img(1024, 1024)
flipbook(fire, 8, 8, 128, fireball)
fire.save(os.path.join(OUT, "fx_fireball_8x8.png")); outputs.append(("fireball", fire))
print("fireball")

sm = Img(512, 512)
flipbook(sm, 8, 8, 64, smoke)
sm.save(os.path.join(OUT, "fx_smoke_8x8.png")); outputs.append(("smoke", sm))
print("smoke")

for name, size, fn in (("fx_flash.png", 256, flash), ("fx_shockwave.png", 256, shockwave), ("fx_spark.png", 64, spark)):
    img = single(size, fn)
    img.save(os.path.join(OUT, name)); outputs.append((name, img))
    print(name)

# ───────────── 미리보기: 어두운 배경 위에 합성 ─────────────
if PREVIEW:
    W, H = 1024 + 512 + 256 + 48, 1024 + 32
    bg = (0.09, 0.11, 0.14)
    canvas = [[bg[0], bg[1], bg[2]] for _ in range(W * H)]

    def blit(img, ox, oy):
        for y in range(img.h):
            for x in range(img.w):
                r, g, b, a = img.px[y * img.w + x]
                if a <= 0.0: continue
                c = canvas[(oy + y) * W + ox + x]
                c[0] += (r - c[0]) * a; c[1] += (g - c[1]) * a; c[2] += (b - c[2]) * a

    blit(outputs[0][1], 16, 16)
    blit(outputs[1][1], 1024 + 32, 16)
    blit(outputs[2][1], 1024 + 32, 512 + 32)
    blit(outputs[3][1], 1024 + 32 + 256 + 16, 512 + 32)
    blit(outputs[4][1], 1024 + 32, 512 + 32 + 256 + 16)
    rows = []
    for y in range(H):
        row = bytearray()
        for x in range(W):
            c = canvas[y * W + x]
            row += bytes((int(clamp01(c[0]) * 255), int(clamp01(c[1]) * 255), int(clamp01(c[2]) * 255), 255))
        rows.append(bytes(row))
    write_png(PREVIEW, W, H, rows)
    print("preview")
