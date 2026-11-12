"""데칼 건 모델(6-149) — 에어 도장용 스프레이건. 격납고 데칼 스튜디오에서 손에 들고 가까이 보는 도구라 약 1천 삼각형, 텍스처 512 한 장.
 - 형태(길이 약 25cm): 몸통(팔각 단면) · 앞 노즐 몸통 · 공기캡(초록 띠) + 뿔 둘 · 노즐 끝 · 뒤 조절 손잡이 둘(도료량·분사 폭, 널링),
   권총 손잡이(뒤로 15도 기울어짐, 검정 고무) + 아래 공기 호스 연결구(황동), 두 손가락 방아쇠(크롬), 위 도료 컵(흰 플라스틱 — 양옆에 초록 라벨 "ACO DECAL")·목·뚜껑·숨구멍 꼭지.
 - 원점 = 몸통 가운데. 노즐은 블렌더 -Y(유니티 +Z — 데칼 건의 앞), 위 +Z(유니티 +Y).
 - 도료 안개 텍스처(ACO_SprayMist.png, 64px): 가장자리가 부드러운 흰 점(알파) — 유니티 기본 파티클 대신 쓴다.
실행: blender --background --factory-startup --python Tools/aco_decal_gun_model.py   (ACO_DECALGUN_PREVIEW=경로접두 → 미리보기 렌더)"""
import math
import os
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_DECALGUN_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "DecalGun"))
NAME = "ACO_DecalGun"
TEX = 512
SEG = 16

# ── 아틀라스 칸 (u0, v0, u1, v1) ──
METAL = (0.0, 0.0, 0.25, 0.25)
METAL_DK = (0.25, 0.0, 0.5, 0.25)
RUBBER = (0.5, 0.0, 0.75, 0.25)
CHROME = (0.75, 0.0, 1.0, 0.25)
GREEN = (0.0, 0.25, 0.25, 0.5)
KNURL = (0.25, 0.25, 0.5, 0.5)
BRASS = (0.5, 0.25, 0.75, 0.5)
LID = (0.75, 0.25, 0.875, 0.5)
LID_RIB = (0.875, 0.25, 1.0, 0.5)   # 뚜껑 옆 미끄럼 방지 홈
CUP = (0.0, 0.5, 1.0, 1.0)          # 컵 옆면 둘레(u = 둘레, v = 높이) — 라벨 둘

C_METAL = np.array((0.70, 0.71, 0.73))
C_METAL_DK = np.array((0.42, 0.43, 0.45))
C_RUBBER = np.array((0.07, 0.07, 0.075))
C_CHROME = np.array((0.86, 0.87, 0.89))
C_GREEN = np.array((0.12, 0.52, 0.24))
C_BRASS = np.array((0.78, 0.60, 0.26))
C_WHITE = np.array((0.93, 0.93, 0.91))
C_LID = np.array((0.80, 0.80, 0.78))


class Builder:
    def __init__(self):
        self.v = []
        self.f = []
        self.uv = []
        self.smooth = []

    def add(self, pts):
        base = len(self.v)
        self.v.extend(Vector(p) for p in pts)
        return base

    def face(self, idx, uvs, smooth=False):
        self.f.append(tuple(idx))
        self.uv.append(uvs)
        self.smooth.append(smooth)

    def face_proj(self, idx, tile, smooth=False, margin=0.08):
        P = [self.v[i] for i in idx]
        n = Vector((0, 0, 0))
        for i in range(len(P)):
            n += P[i].cross(P[(i + 1) % len(P)])
        ax = max(range(3), key=lambda k: abs(n[k]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        us = [p[a] for p in P]
        vs = [p[b] for p in P]
        u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
        du, dv = max(u1 - u0, 1e-6), max(v1 - v0, 1e-6)
        t0, s0, t1, s1 = tile
        mu, mv = (t1 - t0) * margin, (s1 - s0) * margin
        self.face(idx, [(t0 + mu + (p[a] - u0) / du * (t1 - t0 - 2 * mu), s0 + mv + (p[b] - v0) / dv * (s1 - s0 - 2 * mv)) for p in P], smooth)

    def frame(self, axis):
        ax = Vector(axis).normalized()
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((0, -1, 0))
        r = ref.cross(ax).normalized()
        u = ax.cross(r).normalized()
        return ax, r, u

    def cylinder(self, a, c, r0, tile, r1=None, seg=SEG, cap_a=True, cap_c=True, cap_tile=None, wrap_tile=False, start=0.0):
        """a→c 원기둥(끝 반지름 r1). 옆면은 부드럽게, 뚜껑은 따로 정점(각진 모서리). wrap_tile이면 옆면 UV = 둘레·높이(라벨)."""
        a, c = Vector(a), Vector(c)
        r1 = r0 if r1 is None else r1
        ax, rr, uu = self.frame(c - a)
        def ring(p, r):
            return [p + (rr * math.cos(start + k * 2 * math.pi / seg) + uu * math.sin(start + k * 2 * math.pi / seg)) * r for k in range(seg)]
        ba = self.add(ring(a, r0))
        bc = self.add(ring(c, r1))
        t0, s0, t1, s1 = tile
        for k in range(seg):
            k2 = (k + 1) % seg
            idx = [ba + k, ba + k2, bc + k2, bc + k]
            if wrap_tile:
                uvs = [(t0 + (t1 - t0) * k / seg, s0), (t0 + (t1 - t0) * (k + 1) / seg, s0), (t0 + (t1 - t0) * (k + 1) / seg, s1), (t0 + (t1 - t0) * k / seg, s1)]
            else:
                uvs = [(t0 + (t1 - t0) * (0.1 + 0.8 * k / seg), s0 + (s1 - s0) * 0.1), (t0 + (t1 - t0) * (0.1 + 0.8 * (k + 1) / seg), s0 + (s1 - s0) * 0.1),
                       (t0 + (t1 - t0) * (0.1 + 0.8 * (k + 1) / seg), s0 + (s1 - s0) * 0.9), (t0 + (t1 - t0) * (0.1 + 0.8 * k / seg), s0 + (s1 - s0) * 0.9)]
            self.face(idx, uvs, True)
        ct = cap_tile or tile
        if cap_a:
            b = self.add(ring(a, r0))
            self.face_proj([b + k for k in reversed(range(seg))], ct)
        if cap_c:
            b = self.add(ring(c, r1))
            self.face_proj([b + k for k in range(seg)], ct)

    def prism(self, a, c, sec_a, sec_c, tile, smooth=False):
        """a→c 기둥, 단면 = (옆, 위) 점들(시계 반대). 두 끝 단면이 다를 수 있다(손잡이)."""
        a, c = Vector(a), Vector(c)
        ax, rr, uu = self.frame(c - a)
        n = len(sec_a)
        ba = self.add([a + rr * x + uu * y for x, y in sec_a])
        bc = self.add([c + rr * x + uu * y for x, y in sec_c])
        for k in range(n):
            k2 = (k + 1) % n
            self.face_proj([ba + k, ba + k2, bc + k2, bc + k], tile, smooth)
        b = self.add([a + rr * x + uu * y for x, y in sec_a])
        self.face_proj([b + k for k in reversed(range(n))], tile)
        b = self.add([c + rr * x + uu * y for x, y in sec_c])
        self.face_proj([b + k for k in range(n)], tile)

    def loft(self, ring_a, ring_c, tile, smooth=False, cap_a=True, cap_c=True):
        """두 고리(같은 점 수, 같은 돌림 방향)를 잇는 기둥 — 끝 단면이 기울어 있어도 된다."""
        n = len(ring_a)
        ba = self.add(ring_a)
        bc = self.add(ring_c)
        for k in range(n):
            k2 = (k + 1) % n
            self.face_proj([ba + k, ba + k2, bc + k2, bc + k], tile, smooth)
        if cap_a:
            b = self.add(ring_a)
            self.face_proj([b + k for k in reversed(range(n))], tile)
        if cap_c:
            b = self.add(ring_c)
            self.face_proj([b + k for k in range(n)], tile)

    def beam(self, a, c, w, d, tile):
        hw, hd = w * 0.5, d * 0.5
        sec = [(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)]
        self.prism(a, c, sec, sec, tile)

    def build(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(p) for p in self.v], [], self.f)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        for p, uvs, sm in zip(me.polygons, self.uv, self.smooth):
            p.use_smooth = sm
            for li, uv in zip(p.loop_indices, uvs):
                uvl.data[li].uv = uv
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        return o


def octagon(w, h, ch):
    """폭 w·높이 h 사각형의 모서리를 ch만큼 깎은 팔각(시계 반대)."""
    x, y = w * 0.5, h * 0.5
    return [(-x + ch, -y), (x - ch, -y), (x, -y + ch), (x, y - ch), (x - ch, y), (-x + ch, y), (-x, y - ch), (-x, -y + ch)]


def make_gun():
    b = Builder()
    zc = 0.0
    # 몸통: y +0.07(뒤) → -0.06(앞), 팔각 단면 4.2×4.6cm
    body = octagon(0.042, 0.046, 0.009)
    b.prism((0, 0.07, zc), (0, -0.06, zc), body, body, METAL)
    # 앞 노즐 몸통(조금 가늘어짐) → 공기캡(초록 띠) → 노즐 끝
    b.cylinder((0, -0.058, zc), (0, -0.066, zc), 0.0195, METAL_DK, cap_a=False, seg=20)   # 몸통 앞 이음 고리(노즐 몸통 조임 너트)
    b.cylinder((0, -0.066, zc), (0, -0.095, zc), 0.016, METAL, r1=0.014, cap_a=False)
    b.cylinder((0, -0.095, zc), (0, -0.112, zc), 0.018, GREEN, cap_tile=METAL_DK)
    b.cylinder((0, -0.112, zc), (0, -0.121, zc), 0.006, CHROME, r1=0.004, cap_a=False, seg=10)
    # 공기캡 뿔 둘(옆으로 비스듬히 앞)
    for s in (-1, 1):
        b.beam((0.012 * s, -0.105, zc), (0.02 * s, -0.126, zc), 0.007, 0.010, GREEN)
    # 뒤 조절 손잡이: 도료량(아래 가운데, 널링) · 분사 폭(위) — 둘 다 뒤에서 볼 때 몸통 윤곽(팔각) 안에 들어온다
    b.cylinder((0, 0.07, zc - 0.006), (0, 0.077, zc - 0.006), 0.005, METAL_DK, cap_a=False, seg=10)
    b.cylinder((0, 0.077, zc - 0.006), (0, 0.095, zc - 0.006), 0.009, KNURL, cap_tile=METAL_DK)
    b.cylinder((0, 0.07, zc + 0.0115), (0, 0.075, zc + 0.0115), 0.0035, METAL_DK, cap_a=False, seg=8)
    b.cylinder((0, 0.075, zc + 0.0115), (0, 0.087, zc + 0.0115), 0.0058, KNURL, cap_tile=METAL_DK, seg=12)
    # 걸이 고리(몸통 위 뒤 — 도장 부스에 거는 갈고리)
    hook = [(0.056, zc + 0.022), (0.060, zc + 0.036), (0.069, zc + 0.043), (0.078, zc + 0.041), (0.082, zc + 0.034)]
    for (y0, z0), (y1, z1) in zip(hook, hook[1:]):
        b.beam((0, y0, z0), (0, y1, z1), 0.006, 0.004, METAL_DK)
    # 권총 손잡이: 몸통 아래 뒤쪽에서 뒤로 15도 기울어 11cm. 윗부분은 몸통과 같은 금속(주물)으로 윗면을 수평하게 몸통 안에 묻고
    # (기울어진 단면이 몸통 밑면 밖으로 턱처럼 삐져나오지 않게), 그 아래부터 검정 고무 덮개(조금 두꺼워 윗단이 턱으로 보인다)
    top = Vector((0, 0.032, zc - 0.018))
    tilt = math.radians(15)
    d = Vector((0, math.sin(tilt), -math.cos(tilt)))
    bot = top + d * 0.115
    gax, grr, guu = b.frame(d)
    def ring(c, sec):
        return [c + grr * x + guu * y for x, y in sec]
    def to_plane(pts, z):
        return [p + d * ((z - p.z) / d.z) for p in pts]
    root = octagon(0.030, 0.040, 0.008)
    metal_end = top + d * 0.026
    b.loft(to_plane(ring(top, root), zc - 0.008), ring(metal_end, root), METAL, cap_a=False)
    b.loft(ring(top + d * 0.022, octagon(0.034, 0.044, 0.009)), ring(bot, octagon(0.035, 0.047, 0.0095)), RUBBER, smooth=True)
    # 손잡이 끝 금속 마감 → 공기 호스 연결구(황동, 육각 너트 + 꼭지)
    b.prism(bot - d * 0.002, bot + d * 0.006, octagon(0.036, 0.048, 0.01), octagon(0.030, 0.040, 0.008), METAL_DK)
    end = bot + d * 0.006
    b.cylinder(end, end + d * 0.007, 0.0085, BRASS, seg=6)
    b.cylinder(end + d * 0.007, end + d * 0.022, 0.0055, BRASS, seg=10)
    b.cylinder(end + d * 0.016, end + d * 0.019, 0.0068, BRASS, seg=10)   # 꼭지 걸림 턱
    # 공기 밸브 축(방아쇠가 누르는 크롬 핀 — 몸통 아래)
    b.cylinder((0, -0.016, zc - 0.022), (0, -0.016, zc - 0.034), 0.0035, CHROME, seg=8, cap_a=False)
    # 방아쇠(크롬, 두 손가락): 몸통 앞 아래에서 늘어져 손잡이 앞으로 휘어진다
    pts = []
    for i in range(7):   # 부드러운 곡선(축 → 아래로 늘어졌다 끝이 손잡이 쪽으로 휜다)
        t = i / 6
        pts.append((-0.030 - 0.006 * math.sin(t * math.pi * 0.8) + 0.024 * t * t, zc - 0.014 - 0.074 * t))
    for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
        b.beam((0, y0, z0), (0, y1, z1), 0.026, 0.005, CHROME)
    # 방아쇠 축(몸통 옆 — 머리가 몸통 면에 붙는다)
    b.cylinder((-0.0215, -0.030, zc - 0.012), (0.0215, -0.030, zc - 0.012), 0.0035, CHROME, seg=8)
    # 도료 컵: 목 → 컵(옆 = 라벨) → 뚜껑 → 숨구멍 꼭지
    cy = -0.028
    b.cylinder((0, cy, zc + 0.023), (0, cy, zc + 0.038), 0.011, METAL, cap_a=False, seg=12)
    b.cylinder((0, cy, zc + 0.038), (0, cy, zc + 0.046), 0.022, LID, r1=0.036, cap_a=True, cap_c=False)
    b.cylinder((0, cy, zc + 0.046), (0, cy, zc + 0.125), 0.036, CUP, r1=0.039, cap_a=False, cap_c=False, wrap_tile=True, start=-math.pi / 2, seg=24)
    b.cylinder((0, cy, zc + 0.125), (0, cy, zc + 0.135), 0.041, LID_RIB, cap_a=True, cap_tile=LID, seg=24)
    b.cylinder((0, cy, zc + 0.135), (0, cy, zc + 0.142), 0.005, LID, seg=8)
    return b.build(NAME)


# ───────────── 텍스처 ─────────────

FONT = {   # 5×7 비트맵(위 줄부터)
    'A': ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    'C': ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    'O': ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    'D': ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    'E': ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    'L': ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    ' ': ["00000"] * 7,
}


def noise(h, w, seed, scale):
    g = np.random.default_rng(seed)
    small = g.random((max(h // scale, 2), max(w // scale, 2)))
    ys = np.linspace(0, small.shape[0] - 1, h)
    xs = np.linspace(0, small.shape[1] - 1, w)
    y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
    y1 = np.minimum(y0 + 1, small.shape[0] - 1); x1 = np.minimum(x0 + 1, small.shape[1] - 1)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    a = small[y0][:, x0]; b = small[y0][:, x1]; c = small[y1][:, x0]; d = small[y1][:, x1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def px(t):
    return int(round(t[0] * TEX)), int(round(t[1] * TEX)), int(round(t[2] * TEX)), int(round(t[3] * TEX))


def fill(img, t, c, seed, var=0.05, brushed=False, stripes=0):
    x0, y0, x1, y1 = px(t)
    h, w = y1 - y0, x1 - x0
    if brushed:
        n = noise(h * 8, w, seed, 2)[::8]   # 가로 결
    else:
        n = noise(h, w, seed, 5)
    col = np.tile(c, (h, w, 1)).astype(np.float32) * (1 - var + 2 * var * n)[..., None]
    if stripes:
        u = (np.arange(w) + 0.5) / w
        dark = ((u * stripes) % 1.0) < 0.45
        col[:, dark] *= 0.55
    img[y0:y1, x0:x1] = col


def text(img, x, y, s, scale, c):
    """왼쪽 아래 (x, y)부터 글자(배열 아래 행 = v 0이라 비트맵을 뒤집어 찍는다)."""
    for ch in s:
        g = FONT[ch]
        for r, row in enumerate(g):
            for k, bit in enumerate(row):
                if bit == '1':
                    yy = y + (6 - r) * scale
                    xx = x + k * scale
                    img[yy:yy + scale, xx:xx + scale] = c
        x += 6 * scale


def paint_cup(img):
    x0, y0, x1, y1 = px(CUP)
    h, w = y1 - y0, x1 - x0
    col = np.tile(C_WHITE, (h, w, 1)).astype(np.float32) * (0.97 + 0.04 * noise(h, w, 21, 8))[..., None]
    # 눈금(앞쪽 u 0 근처 — 노즐 쪽에서 보인다)
    for i in range(1, 7):
        yy = int(h * (0.15 + i * 0.1))
        xa = int(w * 0.985)
        col[yy:yy + 2, xa:] = (0.35, 0.36, 0.37)
        col[yy:yy + 2, :int(w * 0.015)] = (0.35, 0.36, 0.37)
    img[y0:y1, x0:x1] = col
    # 라벨: 둘레 u 0.25(+X)·0.75(-X) 가운데, 높이 30~72%
    lw = int(w * 0.30)
    for cu in (0.25, 0.75):
        lx0 = x0 + int(w * cu) - lw // 2
        ly0 = y0 + int(h * 0.30)
        ly1 = y0 + int(h * 0.72)
        img[ly0:ly1, lx0:lx0 + lw] = C_GREEN
        img[ly0 + 3:ly0 + 5, lx0 + 4:lx0 + lw - 4] = C_WHITE * 0.95
        img[ly1 - 5:ly1 - 3, lx0 + 4:lx0 + lw - 4] = C_WHITE * 0.95
        scale = 3
        s1, s2 = "ACO", "DECAL"
        w1, w2 = len(s1) * 6 * scale - scale, len(s2) * 6 * scale - scale
        mid = (ly0 + ly1) // 2
        text(img, lx0 + (lw - w1) // 2, mid + 4, s1, scale, C_WHITE)
        text(img, lx0 + (lw - w2) // 2, mid - 4 - 7 * scale, s2, scale, C_WHITE)


def paint():
    img = np.zeros((TEX, TEX, 3), np.float32)
    fill(img, METAL, C_METAL, 1, var=0.06, brushed=True)
    fill(img, METAL_DK, C_METAL_DK, 2, var=0.05, brushed=True)
    fill(img, RUBBER, C_RUBBER, 3, var=0.15)
    fill(img, CHROME, C_CHROME, 4, var=0.03, brushed=True)
    fill(img, GREEN, C_GREEN, 5, var=0.04)
    fill(img, KNURL, C_METAL, 6, var=0.05, stripes=24)
    fill(img, BRASS, C_BRASS, 7, var=0.06, brushed=True)
    fill(img, LID, C_LID, 8, var=0.04)
    fill(img, LID_RIB, C_LID, 9, var=0.03, stripes=24)
    paint_cup(img)
    im = bpy.data.images.new(NAME + "_Albedo", TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), np.float32)
    rgba[..., :3] = np.clip(img, 0, 1)
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = os.path.join(OUT, NAME + "_Albedo.png")
    im.file_format = 'PNG'
    im.save()
    return im


def paint_mist():
    """도료 안개 점(64px, 흰색 + 부드러운 알파 — 가운데 진하고 가장자리로 사라짐, 살짝 얼룩)."""
    n = 64
    yy, xx = np.mgrid[0:n, 0:n]
    r = np.hypot((xx + 0.5) / n - 0.5, (yy + 0.5) / n - 0.5) * 2
    a = np.clip(1 - r, 0, 1) ** 1.6
    a *= 0.85 + 0.3 * noise(n, n, 31, 6)
    a = np.clip(a, 0, 1)
    rgba = np.ones((n, n, 4), np.float32)
    rgba[..., 3] = a
    im = bpy.data.images.new("ACO_SprayMist", n, n, alpha=True)
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = os.path.join(OUT, "ACO_SprayMist.png")
    im.file_format = 'PNG'
    im.save()


def material(o, im):
    mat = bpy.data.materials.new(NAME)
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = im
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    o.data.materials.append(mat)


def write_fbx(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='OFF',
                             path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def preview(o, path):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.3
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.45, 0.5, 0.56)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 45
    shots = {
        "right34": ((0.62, -0.46, 0.24), (0.0, -0.01, 0.0)),
        "left34": ((-0.60, 0.44, 0.20), (0.0, -0.01, 0.0)),
        "side": ((0.80, -0.01, 0.0), (0.0, -0.01, 0.0)),
        "front": ((0.10, -0.50, 0.08), (0.0, -0.05, 0.02)),
        "pov": ((0.05, 0.40, 0.18), (0.0, -0.08, 0.03)),
    }
    for nm, (e, a) in shots.items():
        cam.location = Vector(e)
        cam.rotation_euler = (Vector(a) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = path + "_" + nm + ".png"
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    o = make_gun()
    im = paint()
    paint_mist()
    material(o, im)
    vs = [v.co for v in o.data.vertices]
    print("TRIS", sum(len(p.vertices) - 2 for p in o.data.polygons),
          "BOUNDS", tuple(round(min(v[k] for v in vs), 3) for k in range(3)), tuple(round(max(v[k] for v in vs), 3) for k in range(3)))
    if os.environ.get("ACO_DECALGUN_PREVIEW"):
        preview(o, os.environ["ACO_DECALGUN_PREVIEW"])
    write_fbx(o)
    print("DONE")
