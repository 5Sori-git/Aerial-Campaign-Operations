# -*- coding: utf-8 -*-
"""TEL(이동식 탄도탄 발사대) 모델과 탄도탄. 전방에 운전석이 좌우로 갈라진 8×8 트럭, 격자 이렉터, 3단 유압 실린더, 날개 달린 탄도탄.
실행: blender --background --factory-startup --python Tools/aco_tel_model.py [-- 미리보기폴더]
결과: Assets/ACO_Addon/Models/TEL/ACO_TEL.fbx, ACO_TEL_Albedo.png
좌표는 Unity 축(+Z 앞, +Y 위, 단위 m)으로 적고 저장할 때 Blender 축으로 바꾼다. 치수는 프리팹 판정(18×5×6m)에 맞춘 값이다.
원거리에서 보는 유닛이라 작은 부품은 넣지 않는다. 멀리서 보이지 않는 것은 빼고 윤곽만 남긴다.
메시: TEL_Chassis(원점 지면), TEL_Erector(원점 이렉터 축), TEL_Missile(원점 꼬리, 기수 +Z), TEL_RamCylinder·TEL_RamMiddle·TEL_RamRod(+Z로 늘어난다).
PIVOT, RAM_BASE, RAM_TOP, MISSILE_* 값을 바꾸면 ACO_TelModel.cs의 같은 값도 바꾼다."""
import math
import os
import sys
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "TEL")
FBX = os.path.join(OUT, "ACO_TEL.fbx")
ALBEDO = os.path.join(OUT, "ACO_TEL_Albedo.png")
FONT = "C:/Windows/Fonts/arialbd.ttf"

TEX = 4096
PX = 80.0                       # 차체 텍스처 1m당 픽셀
SIDE_POS = (0, 0)               # 띠 왼쪽 위(픽셀): +X 옆면
SIDE_NEG = (0, 600)             # -X 옆면
TOP_BAND = (0, 1200)            # 윗면
ENDS = (0, 1800)                # 앞뒷면
BAND_H = 560
MISSILE_BAND = (0, 2400)        # 탄도탄 띠: 가로 = 길이, 세로 = 둘레 한 바퀴 + 겹침
MISSILE_W = 3800
MISSILE_H = 1200
MISSILE_WRAP = 1.1
SWATCH_Y = 3760                 # 단색 칸

PIVOT = (0.0, 1.9, -8.6)        # 이렉터 축(Chassis 로컬)
CRADLE_Y = 2.2                  # 이렉터 축에서 탄도탄 중심까지
MISSILE_TAIL = 0.4              # 이렉터 축에서 탄도탄 꼬리까지(이렉터 +Z)
MISSILE_LEN = 15.0
MISSILE_R = 0.72
RAM_BASE = (0.0, 2.55, -3.6)    # 실린더 아래 축(Chassis 로컬)
RAM_TOP = (0.0, 1.2, 2.6)       # 실린더 위 축(이렉터 로컬)

COLORS = {
    "olive": (0.30, 0.32, 0.20), "dark": (0.13, 0.14, 0.11), "metal": (0.36, 0.37, 0.36), "steel": (0.62, 0.64, 0.65),
    "rubber": (0.055, 0.055, 0.06), "glass": (0.10, 0.15, 0.18), "lamp": (0.92, 0.88, 0.70), "red": (0.62, 0.10, 0.07),
    "hub": (0.24, 0.26, 0.18), "black": (0.05, 0.05, 0.055),
}
SWATCHES = {name: i for i, name in enumerate(COLORS)}
CELL = 32


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Part:
    """Unity 좌표로 쌓는다. 면마다 paint 번호를 적어 두고 마지막에 UV를 한꺼번에 편다."""

    def __init__(self):
        self.bm = bmesh.new()
        self.paint = self.bm.faces.layers.int.new("paint")
        self.smooth = self.bm.faces.layers.int.new("smooth")
        self.uv = self.bm.loops.layers.uv.verify()

    def tag(self, faces, paint, smooth):
        code = -1 if paint == "camo" else (-2 if paint == "missile" else SWATCHES[paint])
        for f in faces:
            f[self.paint] = code
            f[self.smooth] = 1 if smooth else 0

    def box(self, center, size, paint="camo", bevel=0.0):
        made = bmesh.ops.create_cube(self.bm, size=1.0, matrix=Matrix.Translation(center) @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
        verts = made["verts"]
        faces = set()
        for v in verts:
            faces.update(v.link_faces)
        if bevel > 0.0:
            edges = set()
            for v in verts:
                edges.update(v.link_edges)
            result = bmesh.ops.bevel(self.bm, geom=list(verts) + list(edges), offset=bevel, offset_type='OFFSET', segments=1, profile=0.5, affect='EDGES')
            faces = set(f for f in faces if f.is_valid) | set(result["faces"])
        self.tag(faces, paint, False)

    def loft(self, rings, paint="camo", smooth=False, caps=True):
        """같은 점 수의 고리를 잇는다."""
        last = None
        first = None
        faces = []
        for ring in rings:
            verts = [self.bm.verts.new(p) for p in ring]
            if last is not None:
                n = len(verts)
                for i in range(n):
                    j = (i + 1) % n
                    faces.append(self.bm.faces.new((last[i], last[j], verts[j], verts[i])))
            else:
                first = verts
            last = verts
        flat = []
        if caps:
            flat.append(self.bm.faces.new(first))
            flat.append(self.bm.faces.new(last))
        self.tag(faces, paint, smooth)
        self.tag(flat, paint, False)

    def prism(self, poly, x0, x1, paint="camo"):
        """(z, y) 다각형을 x로 뽑는다. 차체 옆모습."""
        self.loft([[(x0, y, z) for z, y in poly], [(x1, y, z) for z, y in poly]], paint, False)

    def tube(self, a, b, r0, r1=None, n=12, paint="metal", smooth=True):
        a = Vector(a)
        b = Vector(b)
        r1 = r0 if r1 is None else r1
        axis = (b - a).normalized()
        side = axis.orthogonal().normalized()
        up = axis.cross(side).normalized()
        rings = []
        for c, r in ((a, r0), (b, r1)):
            rings.append([tuple(c + side * (math.cos(i * math.tau / n) * r) + up * (math.sin(i * math.tau / n) * r)) for i in range(n)])
        self.loft(rings, paint, smooth)

    def finish(self, name):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.faces.ensure_lookup_table()
        for f in bm.faces:
            set_uv(f, self.uv, f[self.paint])
            f.smooth = f[self.smooth] == 1
        for v in bm.verts:
            v.co = to_blender(v.co)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        return me


def swatch_uv(index):
    x = (index % 40) * CELL + CELL * 0.5
    y = SWATCH_Y + (index // 40) * CELL + CELL * 0.5
    return (x / TEX, 1.0 - y / TEX)


def band_uv(band, u_m, v_m, centered_u=True, floor=0.0):
    x = band[0] + (u_m + (12.8 if centered_u else 0.0)) * PX
    y = band[1] + BAND_H - (v_m - floor) * PX
    return (x / TEX, 1.0 - y / TEX)


def set_uv(f, uv, code):
    """-1 위장(면 방향 투영), -2 탄도탄(원통 전개), 그 밖은 단색."""
    if code >= 0:
        p = swatch_uv(code)
        for loop in f.loops:
            loop[uv].uv = p
        return
    if code == -2:
        t = []
        for loop in f.loops:
            c = loop.vert.co
            t.append((math.atan2(c.y, c.x) + math.pi * 0.5) / math.tau % 1.0)   # 이음매는 아래쪽
        low, high = min(t), max(t)
        for loop, a in zip(f.loops, t):
            c = loop.vert.co
            if high - low > 0.5 and a < 0.5:
                a += 1.0
            x = MISSILE_BAND[0] + c.z / MISSILE_LEN * MISSILE_W
            y = MISSILE_BAND[1] + MISSILE_H - a / MISSILE_WRAP * MISSILE_H
            loop[uv].uv = (x / TEX, 1.0 - y / TEX)
        return
    n = f.normal
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    for loop in f.loops:
        c = loop.vert.co
        if ay >= ax and ay >= az:
            loop[uv].uv = band_uv(TOP_BAND, c.z, c.x + 3.5)
        elif ax >= az:
            if n.x > 0.0:   # +X에서 보면 +Z가 오른쪽, -X에서 보면 왼쪽이다 — 글자가 거울상이 되지 않게 나눈다
                loop[uv].uv = band_uv(SIDE_POS, c.z, c.y)
            else:
                loop[uv].uv = band_uv(SIDE_NEG, -c.z, c.y)
        else:
            s = 1.0 if n.z > 0.0 else -1.0
            loop[uv].uv = band_uv(ENDS, c.x * s + (5.0 if n.z > 0.0 else -5.0), c.y)


# ───────────── 차체 ─────────────

AXLES = (6.25, 3.55, -2.3, -5.0)
WHEEL_R = 1.0
WHEEL_W = 0.92
WHEEL_X = 2.02


def wheel(p, x, z):
    side = 1.0 if x > 0.0 else -1.0
    outer = x + side * WHEEL_W * 0.5
    inner = x - side * WHEEL_W * 0.5
    y = WHEEL_R
    n = 12
    profile = ((inner, 0.6), (inner + side * 0.1, 1.0), (outer - side * 0.1, 1.0), (outer, 0.6))
    rings = []
    for wx, r in profile:
        rings.append([(wx, y + math.sin(i * math.tau / n) * r * WHEEL_R, z + math.cos(i * math.tau / n) * r * WHEEL_R) for i in range(n)])
    p.loft(rings, "rubber", True)
    p.tube((outer - side * 0.02, y, z), (outer + side * 0.03, y, z), 0.58, 0.56, 8, "hub", True)


def cab(p, side):
    x0 = side * 0.98
    x1 = side * 2.42
    shape = ((4.55, 1.55), (9.0, 1.55), (9.05, 2.75), (8.72, 3.0), (8.1, 4.35), (5.0, 4.5), (4.55, 4.5))
    p.prism(shape, x0, x1, "camo")
    # 앞유리·옆유리·문: 면보다 조금 나온 판이 아니라 틀을 두른 창이다
    xa, xb = side * 1.05, side * 2.35
    p.loft([[(xa, y, z + 0.05) for z, y in reversed(((8.78, 3.1), (8.19, 4.28), (8.19, 4.28), (8.78, 3.1)))], [(xb, y, z + 0.05) for z, y in reversed(((8.78, 3.1), (8.19, 4.28), (8.19, 4.28), (8.78, 3.1)))]], "glass", False)
    for z0, z1 in ((5.25, 6.35), (6.75, 7.75)):
        p.box((side * 2.42, 3.72, (z0 + z1) * 0.5), (0.06, 0.92, z1 - z0), "glass")
    p.box((side * 2.44, 3.0, 6.55), (0.04, 2.8, 0.05), "dark")
    p.tube((side * 1.7, 4.5, 6.3), (side * 1.7, 4.62, 6.3), 0.42, 0.38, 8, "dark", False)
    p.box((side * 1.7, 2.2, 9.04), (1.15, 0.8, 0.04), "dark")          # 그릴
    p.tube((side * 2.12, 2.85, 9.02), (side * 2.12, 2.85, 9.1), 0.2, 0.2, 8, "lamp", False)
    p.box((side * 2.5, 3.86, 8.0), (0.2, 0.56, 0.3), "dark")          # 거울


def fender(p, side, z0, z1):
    """바퀴 위 흙받이: 옆에서 보면 둥근 아치."""
    x0, x1 = side * 1.46, side * 2.56
    n = 6
    arc = []
    cz = (z0 + z1) * 0.5
    half = (z1 - z0) * 0.5
    for i in range(n + 1):
        a = math.pi * i / n
        arc.append((cz + math.cos(a) * half, 2.05 + math.sin(a) * 0.42))
    poly = [(z, y) for z, y in arc] + [(z, y - 0.09) for z, y in reversed(arc)]
    p.prism(poly, x0, x1, "camo")


def jack(p, x, z):
    """보조 지지대: 접힌 유압 다리와 패드."""
    p.tube((x, 2.2, z), (x, 0.28, z), 0.19, 0.15, 6, "camo", True)
    p.tube((x, 0.3, z), (x, 0.2, z), 0.42, 0.42, 8, "dark", False)


def chassis():
    p = Part()
    # 사다리 프레임, 갑판
    for side in (-1.0, 1.0):
        p.box((side * 0.9, 1.5, 0.0), (0.28, 0.5, 17.8), "dark")
    p.box((0.0, 2.2, -3.1), (4.7, 0.3, 10.2), "camo")
    p.box((0.0, 2.2, 3.0), (4.7, 0.3, 2.2), "camo")
    # 가운데 엔진 덮개: 탄도탄 기수가 이 위로 지나간다
    p.prism(((3.9, 2.05), (8.95, 2.05), (8.95, 2.7), (8.3, 3.18), (4.5, 3.18), (3.9, 2.8)), -0.97, 0.97, "camo")
    for side in (-1.0, 1.0):
        cab(p, side)
    # 바퀴, 차축, 흙받이
    for z in AXLES:
        for side in (-1.0, 1.0):
            wheel(p, side * WHEEL_X, z)
    for side in (-1.0, 1.0):
        fender(p, side, AXLES[1] - 1.22, AXLES[0] + 1.22)
        fender(p, side, AXLES[3] - 1.22, AXLES[2] + 1.22)
        # 갑판 옆 사물함, 공구, 예비 탱크
        p.box((side * 2.5, 2.0, 0.65), (0.5, 1.0, 3.1), "camo")
        p.tube((side * 2.0, 2.75, -1.4), (side * 2.0, 2.75, 1.9), 0.36, 0.36, 8, "camo", True)
        # 보조 다리: 뒤와 가운데
        jack(p, side * 2.28, -7.0)
        jack(p, side * 2.72, 2.6)
        p.box((side * 2.5, 2.2, 2.6), (0.6, 0.3, 0.6), "dark")
        # 뒤 등과 범퍼
        p.box((side * 1.6, 1.55, -8.92), (0.5, 0.2, 0.04), "red")
    p.box((0.0, 1.2, -8.82), (4.2, 0.24, 0.22), "dark")
    # 이렉터 축 받침: 프레임 끝에 양쪽으로 선다
    for side in (-1.0, 1.0):
        x = side * 1.14
        p.prism(((-8.1, 1.25), (-8.95, 1.25), (-8.95, 2.25), (-8.3, 2.25)), x - 0.1, x + 0.1, "dark")
    p.tube((-1.4, PIVOT[1], PIVOT[2]), (1.4, PIVOT[1], PIVOT[2]), 0.14, 0.14, 6, "steel", False)
    # 실린더 아래 축 받침
    for dx in (-0.42, 0.42):
        p.prism(((-3.2, 2.35), (-4.0, 2.35), (-3.82, 2.8), (-3.38, 2.8)), dx - 0.07, dx + 0.07, "dark")
    p.tube((-0.5, RAM_BASE[1], RAM_BASE[2]), (0.5, RAM_BASE[1], RAM_BASE[2]), 0.09, 0.09, 6, "steel", False)
    return p.finish("TEL_Chassis")


def erector():
    p = Part()
    length = 13.8
    # 축에서 올라가는 두 팔과 축 고리
    for side in (-1.0, 1.0):
        x = side * 0.95
        p.tube((x - 0.12, 0.0, 0.0), (x + 0.12, 0.0, 0.0), 0.32, 0.32, 8, "dark", True)
        p.prism(((-0.28, 0.0), (0.28, 0.0), (0.42, 1.0), (0.42, CRADLE_Y - 0.1), (-0.05, CRADLE_Y - 0.1), (-0.05, 1.0)), x - 0.1, x + 0.1, "olive")
    # 긴 보 두 줄과 격자
    for side in (-1.0, 1.0):
        x = side * 0.95
        p.box((x, 1.3, length * 0.5 + 0.1), (0.24, 0.3, length), "olive")
        p.box((x, 1.72, length * 0.5 + 0.1), (0.14, 0.14, length), "olive")
        for i in range(5):
            z0 = 0.3 + i * 2.7
            p.tube((x, 1.3, z0), (x, 1.72, z0 + 1.35), 0.06, 0.06, 4, "olive", False)
            p.tube((x, 1.72, z0 + 1.35), (x, 1.3, z0 + 2.7), 0.06, 0.06, 4, "olive", False)
    for z in (3.3, 6.0, 8.7, 11.4, 13.8):      # 꼬리 쪽은 아래 날개가 지나간다
        p.box((0.0, 1.22, z), (1.9, 0.12, 0.14), "olive")
    # 고리: 탄도탄을 잡는 아랫 받침과 윗 띠
    for z in (3.4, 8.6, 12.8):
        n = 8
        ring_out = []
        ring_in = []
        r_in = MISSILE_R + 0.03
        r_out = MISSILE_R + 0.14
        for i in range(n + 1):
            a = math.pi * (1.0 + i / n)
            ring_in.append((math.cos(a) * r_in, CRADLE_Y + math.sin(a) * r_in))
            ring_out.append((math.cos(a) * r_out, CRADLE_Y + math.sin(a) * r_out))
        poly = ring_out + list(reversed(ring_in))
        p.loft([[(x, y, z - 0.16) for x, y in poly], [(x, y, z + 0.16) for x, y in poly]], "olive", False)
        p.box((0.0, 1.45, z), (1.9, 0.22, 0.3), "olive")
        for side in (-1.0, 1.0):
            p.box((side * 0.82, 1.82, z), (0.3, 0.54, 0.2), "olive")
    # 꼬리 받침 고리와 지지대
    n = 12
    r0, r1 = MISSILE_R + 0.12, MISSILE_R + 0.5
    section = ((r0, 0.0), (r1, 0.0), (r1, 0.3), (r0, 0.3), (r0, 0.0))
    rings = [[(math.cos(i * math.tau / n) * rad, CRADLE_Y + math.sin(i * math.tau / n) * rad, z) for i in range(n)] for rad, z in section]
    p.loft(rings, "olive", True, caps=False)
    # 실린더 위 축
    for dx in (-0.42, 0.42):
        p.prism(((RAM_TOP[2] - 0.45, 1.16), (RAM_TOP[2] + 0.45, 1.16), (RAM_TOP[2] + 0.3, RAM_TOP[1] - 0.26), (RAM_TOP[2] - 0.3, RAM_TOP[1] - 0.26)), dx - 0.07, dx + 0.07, "dark")
    return p.finish("TEL_Erector")


def ram_stage(name, radius, length, paint, gland):
    p = Part()
    p.tube((0.0, 0.0, 0.0), (0.0, 0.0, length), radius, radius, 8, paint, True)
    if gland:
        p.tube((0.0, 0.0, length - 0.16), (0.0, 0.0, length), radius + 0.04, radius + 0.04, 8, "dark", True)
    return p.finish(name)


def missile():
    p = Part()
    r = MISSILE_R
    n = 12
    # (z, 반지름): 꼬리 노즐부터 기수까지
    profile = [(0.0, 0.34), (0.42, 0.62), (0.75, r), (10.4, r)]
    for i in range(1, 6):
        t = i / 5.0
        profile.append((10.4 + (MISSILE_LEN - 10.4 - 0.03) * t, r * max(math.sqrt(max(1.0 - t * t * 0.985, 0.0)) * (1.0 - 0.32 * t), 0.02)))
    rings = []
    for z, rad in profile:
        rings.append([(math.cos(i * math.tau / n) * rad, math.sin(i * math.tau / n) * rad, z) for i in range(n)])
    p.loft(rings, "missile", True)
    # 날개 네 장
    for k in range(4):
        a = k * math.pi * 0.5       # + 배치: 위아래 날개는 이렉터 보 사이로, 옆 날개는 보 위로 지나간다
        c, s = math.cos(a), math.sin(a)
        root_z = (0.5, 2.5)
        tip_z = (0.02, 1.15)
        span = 0.85
        t0, t1 = 0.07, 0.02
        pts = []
        for z, d, tk in ((root_z[0], r - 0.02, t0), (root_z[1], r - 0.02, t0), (tip_z[1], r + span, t1), (tip_z[0], r + span, t1)):
            pts.append((z, d, tk))
        side_a = []
        side_b = []
        for z, d, tk in pts:
            side_a.append((c * d - s * tk, s * d + c * tk, z))
            side_b.append((c * d + s * tk, s * d - c * tk, z))
        p.loft([side_a, side_b], "black", False)
    return p.finish("TEL_Missile")


# ───────────── 텍스처 ─────────────

def noise(h, w, cells, seed):
    """값 노이즈 한 겹(가로세로 비를 유지한다)."""
    rng = np.random.default_rng(seed)
    gh = max(int(h / w * cells), 2) + 2
    gw = cells + 2
    grid = rng.random((gh, gw))
    y = np.linspace(0.0, gh - 1.001, h)
    x = np.linspace(0.0, gw - 1.001, w)
    y0 = y.astype(int)
    x0 = x.astype(int)
    ty = (y - y0)[:, None]
    tx = (x - x0)[None, :]
    ty = ty * ty * (3.0 - 2.0 * ty)
    tx = tx * tx * (3.0 - 2.0 * tx)
    a = grid[y0][:, x0]
    b = grid[y0][:, x0 + 1]
    c = grid[y0 + 1][:, x0]
    d = grid[y0 + 1][:, x0 + 1]
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty


def fbm(h, w, cells, seed, octaves=4):
    out = np.zeros((h, w), np.float32)
    amp = 1.0
    total = 0.0
    for i in range(octaves):
        out += noise(h, w, cells * (2 ** i), seed + i * 13).astype(np.float32) * amp
        total += amp
        amp *= 0.5
    return out / total


def text_layer(items):
    """글자를 그린 RGBA 판. items = (글자, x, y(픽셀), 높이(픽셀), 색, 회전(도))."""
    scene = bpy.data.scenes.new("page")
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'FLAT'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.render_aa = '16'
    scene.render.film_transparent = True
    scene.render.resolution_x = TEX
    scene.render.resolution_y = TEX
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    temp = os.path.join(bpy.app.tempdir, "tel_page.png")
    scene.render.filepath = temp
    cam = bpy.data.cameras.new("page")
    cam.type = 'ORTHO'
    cam.ortho_scale = TEX
    ob = bpy.data.objects.new("page", cam)
    ob.location = (TEX * 0.5, -TEX * 0.5, 100.0)
    scene.collection.objects.link(ob)
    scene.camera = ob
    font = bpy.data.fonts.load(FONT)
    for i, (text, x, y, size, color, turn) in enumerate(items):
        cu = bpy.data.curves.new(f"t{i}", 'FONT')
        cu.body = text
        cu.font = font
        cu.size = size
        cu.align_x = 'CENTER'
        cu.align_y = 'CENTER'
        mat = bpy.data.materials.new(f"t{i}")
        mat.diffuse_color = (*color, 1.0)
        cu.materials.append(mat)
        t = bpy.data.objects.new(f"t{i}", cu)
        t.location = (x, -y, 0.0)
        t.rotation_euler = (0.0, 0.0, math.radians(turn))
        scene.collection.objects.link(t)
    bpy.ops.render.render(write_still=True, scene="page")
    image = bpy.data.images.load(temp)
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(TEX, TEX, 4)[::-1]
    bpy.data.images.remove(image)
    bpy.data.scenes.remove(scene)
    return pixels


def to_linear(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def make_texture():
    img = np.zeros((TEX, TEX, 3), np.float32)
    img[...] = COLORS["olive"]
    # 위장: 세 가지 색 얼룩
    camo = fbm(2400, TEX, 7, 11, 4)
    second = fbm(2400, TEX, 9, 41, 4)
    grime = fbm(2400, TEX, 40, 77, 4)
    streak = fbm(2400, TEX, 3, 91, 2)
    base = np.array(COLORS["olive"], np.float32)
    dark = np.array((0.17, 0.19, 0.125), np.float32)
    sand = np.array((0.42, 0.38, 0.27), np.float32)
    area = np.tile(base, (2400, TEX, 1))
    area[camo > 0.54] = dark
    area[(second > 0.56) & (camo <= 0.54)] = sand
    area *= (0.82 + 0.3 * grime)[..., None] * (0.92 + 0.14 * streak)[..., None]
    img[:2400] = area
    # 탄도탄: 밝은 몸통, 검은 띠, 어두운 기수
    my, mx = MISSILE_BAND[1], MISSILE_BAND[0]
    body = np.tile(np.array((0.74, 0.75, 0.70), np.float32), (MISSILE_H, MISSILE_W, 1))
    def band(z0, z1, color):
        a = int(z0 / MISSILE_LEN * MISSILE_W)
        b = int(z1 / MISSILE_LEN * MISSILE_W)
        body[:, a:b] = color
    black = np.array(COLORS["black"], np.float32)
    band(0.0, 0.75, black)
    band(2.9, 3.5, black)
    band(7.4, 7.9, black)
    band(12.9, 15.0, np.array((0.28, 0.31, 0.2), np.float32))
    for z in (5.4, 10.4):
        band(z - 0.04, z + 0.04, black)
    # 롤 무늬: 둘레의 절반만 검다 — 돌아가는 모습이 보인다
    a = int(3.5 / MISSILE_LEN * MISSILE_W)
    b = int(7.4 / MISSILE_LEN * MISSILE_W)
    quarter = int(MISSILE_H / MISSILE_WRAP * 0.25)
    for k in (0, 2, 4):
        body[k * quarter + quarter // 2:(k + 1) * quarter + quarter // 2, a:b] = black
    wear = fbm(MISSILE_H, MISSILE_W, 30, 7, 4)
    body *= (0.86 + 0.26 * wear)[..., None]
    img[my:my + MISSILE_H, mx:mx + MISSILE_W] = body
    # 단색 칸
    for name, i in SWATCHES.items():
        x = (i % 40) * CELL
        y = SWATCH_Y + (i // 40) * CELL
        img[y:y + CELL, x:x + CELL] = COLORS[name]
    # 표기: 문, 차체 옆, 탄도탄. 표기는 텍스처에 칠한다
    white = (0.86, 0.85, 0.78)
    black3 = (0.03, 0.03, 0.03)
    red = (0.6, 0.09, 0.06)
    def side(band_pos, z, y, text, size, color, flip):
        u = band_uv(band_pos, -z if flip else z, y)
        return (text, u[0] * TEX, (1.0 - u[1]) * TEX, size, color, 0.0)
    items = []
    for band_pos, flip in ((SIDE_POS, False), (SIDE_NEG, True)):
        items.append(side(band_pos, 7.35, 2.65, "704", 58, white, flip))
        items.append(side(band_pos, -0.2, 2.0, "HYD 210 BAR", 16, white, flip))
        items.append(side(band_pos, 1.45, 2.0, "NO STEP", 16, white, flip))
    # 앞면 번호
    u = band_uv(ENDS, 3.2, 2.05)
    items.append(("704", u[0] * TEX, (1.0 - u[1]) * TEX, 26, white, 0.0))
    def around(z, turn_at, text, size, color, rotated):
        x = MISSILE_BAND[0] + z / MISSILE_LEN * MISSILE_W
        y = MISSILE_BAND[1] + MISSILE_H - turn_at / MISSILE_WRAP * MISSILE_H
        return (text, x, y, size, color, 180.0 if rotated else 0.0)
    # 이음매가 아래쪽이므로 v 0.25 = +X 옆, 0.75 = -X 옆
    for at, rotated in ((0.25, False), (0.75, True)):
        items.append(around(9.2, at, "TBM-7", 110, black3, rotated))
        items.append(around(11.6, at, "DANGER - PROPELLANT", 34, red, rotated))
        items.append(around(2.1, at, "ARM BEFORE LAUNCH", 22, black3, rotated))
    page = text_layer(items)
    img = to_linear(img)
    alpha = page[..., 3:4]
    img = img * (1.0 - alpha) + page[..., :3] * alpha
    os.makedirs(OUT, exist_ok=True)
    tex = bpy.data.images.new("ACO_TEL_Albedo", TEX, TEX, alpha=False, float_buffer=False)
    tex.colorspace_settings.name = 'sRGB'
    rgba = np.ones((TEX, TEX, 4), np.float32)
    srgb = np.where(img <= 0.0031308, img * 12.92, 1.055 * np.power(np.maximum(img, 1e-6), 1.0 / 2.4) - 0.055)
    rgba[..., :3] = srgb
    tex.pixels.foreach_set(rgba[::-1].ravel())
    tex.filepath_raw = ALBEDO
    tex.file_format = 'PNG'
    tex.save()
    return tex


# ───────────── 내보내기와 미리보기 ─────────────

def material(tex):
    mat = bpy.data.materials.new("ACO_TEL")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = tex
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def preview(objects, folder):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = 'BOTH'
    scene.display.render_aa = '16'
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 800
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.color = (0.55, 0.6, 0.65)
    scene.render.image_settings.file_format = 'PNG'
    os.makedirs(folder, exist_ok=True)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shoot(name, unity_eye, unity_at):
        eye = Vector(to_blender(unity_eye))
        at = Vector(to_blender(unity_at))
        cam.location = eye
        cam.rotation_euler = (at - eye).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    ground = bpy.data.meshes.new("ground")
    ground.from_pydata([(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [], [(0, 1, 2, 3)])
    gm = bpy.data.materials.new("g")
    gm.diffuse_color = (0.3, 0.33, 0.27, 1.0)
    ground.materials.append(gm)
    scene.collection.objects.link(bpy.data.objects.new("ground", ground))

    pivot = Vector(to_blender(PIVOT))
    rail = [objects["TEL_Erector"], objects["TEL_Missile"]]
    objects["TEL_Erector"].location = pivot
    objects["TEL_Missile"].location = pivot + Vector(to_blender((0.0, CRADLE_Y, MISSILE_TAIL)))
    base = Vector(to_blender(RAM_BASE))

    def pose(angle):
        """이렉터 각도(도, 0 = 누움). 실린더 세 단을 두 축 사이에 놓는다."""
        rot = Matrix.Rotation(math.radians(-angle), 4, 'X')
        objects["TEL_Erector"].rotation_euler = rot.to_euler()
        objects["TEL_Missile"].rotation_euler = rot.to_euler()
        objects["TEL_Missile"].location = pivot + rot.to_3x3() @ Vector(to_blender((0.0, CRADLE_Y, MISSILE_TAIL)))
        top = pivot + rot.to_3x3() @ Vector(to_blender(RAM_TOP))
        forward = (top - base).normalized()
        # 모델의 +Z(Unity)는 Blender에서 -Y다
        quat = (forward).to_track_quat('-Y', 'Z')
        objects["TEL_RamCylinder"].location = base
        objects["TEL_RamCylinder"].rotation_mode = 'QUATERNION'
        objects["TEL_RamCylinder"].rotation_quaternion = quat
        objects["TEL_RamMiddle"].location = (base + top) * 0.5 - forward * RAM_MIDDLE_LEN * 0.5
        objects["TEL_RamMiddle"].rotation_mode = 'QUATERNION'
        objects["TEL_RamMiddle"].rotation_quaternion = quat
        objects["TEL_RamRod"].location = top
        objects["TEL_RamRod"].rotation_mode = 'QUATERNION'
        objects["TEL_RamRod"].rotation_quaternion = (-forward).to_track_quat('-Y', 'Z')

    pose(0.0)
    shoot("tel_front", (16.0, 7.0, 21.0), (0.0, 2.6, 0.0))
    shoot("tel_rear", (-15.0, 8.0, -20.0), (0.0, 2.6, -2.0))
    shoot("tel_side", (32.0, 3.0, 0.0), (0.0, 3.2, 0.0))
    shoot("tel_top", (9.0, 30.0, 1.0), (0.0, 2.0, 0.0))
    pose(40.0)
    shoot("tel_mid", (28.0, 8.0, -4.0), (0.0, 5.0, -5.0))
    pose(90.0)
    shoot("tel_erect", (26.0, 9.0, 13.0), (0.0, 8.0, -4.0))
    pose(0.0)
    for o in rail:
        pass


RAM_CYL_LEN = 2.5
RAM_MIDDLE_LEN = 2.4
RAM_ROD_LEN = 2.4


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = make_texture()
    mat = material(tex)
    meshes = [
        chassis(), erector(), missile(),
        ram_stage("TEL_RamCylinder", 0.3, RAM_CYL_LEN, "olive", True),
        ram_stage("TEL_RamMiddle", 0.24, RAM_MIDDLE_LEN, "steel", True),
        ram_stage("TEL_RamRod", 0.17, RAM_ROD_LEN, "steel", False),
    ]
    scene = bpy.context.scene
    objects = {}
    total = 0
    for me in meshes:
        me.materials.append(mat)
        ob = bpy.data.objects.new(me.name, me)
        scene.collection.objects.link(ob)
        objects[me.name] = ob
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        total += tris
        box = [Vector(v) for v in ob.bound_box]
        print("MESH", me.name, tris, "bounds", [round(min(v[i] for v in box), 2) for i in range(3)], [round(max(v[i] for v in box), 2) for i in range(3)])
    print("TRIS", total)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    if argv:
        preview(objects, argv[0])


main()
