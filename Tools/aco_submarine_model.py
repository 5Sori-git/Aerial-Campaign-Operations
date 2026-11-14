# -*- coding: utf-8 -*-
"""잠수함 보스 LEVIATHAN 모델(독자 디자인, 설계 6-204). 가운데 큰 동체 + 뒤쪽 양옆 보조 동체(엔진과 스크류)의 3동선체.
실행: blender --background --factory-startup --python Tools/aco_submarine_model.py [-- 미리보기폴더 [dry] [장면...]]
  dry = 물을 빼고 렌더(물속 부분까지 확인)
결과: Assets/ACO_Addon/Models/Submarine/ACO_Submarine.fbx, ACO_Submarine_Albedo.png (4096)
좌표는 Unity 축(+Z 뱃머리, +Y 위, 단위 m)이고 원점은 부상했을 때의 수면, 선체 가운데다.
FBX 오브젝트:
  SB_Hull: 중앙 동체 · 함교 · 날개(파일런) · 보조 동체 · 꼬리 날개(원점 = 수면 가운데)
  SB_Screw_R / SB_Screw_L: 7날 스크류(원점 = 축 중심 SCREW_POS, 로컬 Z로 돈다). 메시는 같다
  SB_DroneDoor_R: 무인기 사출구 덮개(6-217 — 원점 = 바깥 경첩, 덮개는 로컬 -X(안쪽)로 뻗는다). 왼쪽은 빌더가 같은 메시를 Y 180° 돌려 놓는다
무장(포대 프리팹)은 모델에 없다 — ACO_SubmarineBuilder가 아래 표의 자리에 얹는다(미리보기에서만 색 상자로 보여 준다).
텍스처는 세계 좌표로 투영한다(옆 · 위 · 앞뒤). 함번과 SLCM 해치는 텍스처에 칠한다."""
import math
import os
import sys
import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Submarine")
FBX = os.path.join(OUT, "ACO_Submarine.fbx")
ALBEDO = os.path.join(OUT, "ACO_Submarine_Albedo.png")
FONT = "C:/Windows/Fonts/arialbd.ttf"

TEX = 4096
PX = 12.0                   # 1m당 픽셀
Y_LOW, Y_HIGH = -22.0, 38.0
BAND = int((Y_HIGH - Y_LOW) * PX)     # 옆면 띠 높이 720
SIDE_POS = 0
SIDE_NEG = 740
TOP_BAND = 1480
TOP_HALF = 42.0             # 위 띠의 x 범위 ±42m → 1008px
FRONT_RECT = (0, 2510)      # 앞뒤 면(x, y) — 폭 1008, 높이 BAND
SWATCH_Y = 3300
CELL = 32
WATERLINE = -0.6            # 이 아래는 방오 도료

COLORS = {
    "dark": (0.06, 0.065, 0.07), "metal": (0.32, 0.34, 0.36), "bronze": (0.55, 0.4, 0.2), "glass": (0.04, 0.06, 0.08),
    "vent": (0.1, 0.1, 0.105), "red": (0.28, 0.1, 0.08), "deck": (0.215, 0.23, 0.245),
}
SWATCHES = {name: i for i, name in enumerate(COLORS)}

# ── 치수 ──
HULL_CY = -2.0              # 중앙 동체 중심 높이(갑판 = HULL_CY + DECK_K × 반지름)
DECK_K = 0.78               # 이 높이에서 윗면을 평평하게 깎는다(갑판)
# 중앙 동체 반지름 (z, r): 뱃머리 → 꼬리
NOSE_S = (1.0, 0.993, 0.975, 0.95, 0.91, 0.86, 0.79, 0.71, 0.61, 0.49, 0.35, 0.18, 0.0)    # 뱃머리 곡선 표본(끝에 촘촘하게)
HULL_PROFILE = tuple((99.0 + 32.0 * s, max(14.0 * math.sqrt(max(1.0 - s ** 2.4, 0.0)), 0.6)) for s in NOSE_S) + (
                (-40.0, 14.0), (-58.0, 13.4), (-76.0, 12.0), (-94.0, 9.6), (-110.0, 6.8), (-122.0, 4.2), (-130.0, 2.0), (-133.0, 0.6))
SAIL_Z0, SAIL_Z1 = -7.0, 27.0       # 함교(세일) 앞뒤 — 뱃머리에서 약 120m
SAIL_TOP = 30.0
SAIL_HALF = 4.4
SIDE_X = 32.0               # 보조 동체 중심 x
SIDE_CY = 0.0
SIDE_K = 0.82
SIDE_PROFILE = ((-11.0, 0.5), (-13.0, 3.6), (-17.0, 5.7), (-23.0, 6.8), (-30.0, 7.0), (-84.0, 7.0), (-89.0, 9.4), (-91.0, 9.8),
                (-112.0, 9.8), (-117.0, 8.0), (-121.0, 4.8), (-123.5, 2.0))
SCREW_POS = (SIDE_X, SIDE_CY, -127.0)
SCREW_R = 6.5
SCREW_BLADES = 7

# 무장 자리(미리보기 표시용 — ACO_SubmarineBuilder와 같은 값이어야 한다)
def deck_y(z):
    return HULL_CY + DECK_K * hull_r(z)


def side_top(z):
    return SIDE_CY + SIDE_K * side_r(z)


def to_blender(p):
    return (-p[0], -p[2], p[1])


def interp(profile, z):
    pts = sorted(profile)
    if z <= pts[0][0]:
        return pts[0][1]
    for (z0, r0), (z1, r1) in zip(pts, pts[1:]):
        if z0 <= z <= z1:
            return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return pts[-1][1]


def hull_r(z):
    return interp(HULL_PROFILE, z)


def side_r(z):
    return interp(SIDE_PROFILE, z)


class Part:
    def __init__(self):
        self.bm = bmesh.new()
        self.paint = self.bm.faces.layers.int.new("paint")
        self.smooth = self.bm.faces.layers.int.new("smooth")
        self.uv = self.bm.loops.layers.uv.verify()

    def tag(self, faces, paint, smooth):
        code = -1 if paint == "hull" else SWATCHES[paint]
        for f in faces:
            f[self.paint] = code
            f[self.smooth] = 1 if smooth else 0

    def loft(self, rings, paint="hull", smooth=False, caps=True):
        last = None
        first = None
        faces = []
        for ring in rings:
            verts = [self.bm.verts.new(p) for p in ring]
            if last is not None:
                n = len(verts)
                for i in range(n):
                    faces.append(self.bm.faces.new((last[i], last[(i + 1) % n], verts[(i + 1) % n], verts[i])))
            else:
                first = verts
            last = verts
        flat = []
        if caps:
            flat.append(self.bm.faces.new(list(reversed(first))))
            flat.append(self.bm.faces.new(last))
        self.tag(faces, paint, smooth)
        self.tag(flat, paint, False)
        self.orient(faces + flat, caps)

    def orient(self, piece, closed):
        """부품 하나의 노멀을 바깥으로(닫힌 부품은 부피 부호로)."""
        if not piece:
            return
        if not closed:
            bmesh.ops.recalc_face_normals(self.bm, faces=piece)
            return
        for f in piece:
            f.normal_update()
        center = Vector()
        count = 0
        for f in piece:
            for v in f.verts:
                center += v.co
                count += 1
        center /= max(count, 1)
        vol = sum((f.calc_center_median() - center).dot(f.normal) * f.calc_area() for f in piece)
        if vol < 0.0:
            bmesh.ops.reverse_faces(self.bm, faces=piece)

    def slab(self, poly_xz, y0, y1, inset=0.0, paint="hull"):
        """(x, z) 다각형을 y0~y1로 세운 판. inset이 있으면 윗면이 그만큼 안으로 줄어든다."""
        top = shrink(poly_xz, inset) if inset > 0.0 else poly_xz
        self.loft([[(x, y0, z) for x, z in poly_xz], [(x, y1, z) for x, z in top]], paint)

    def fin(self, poly_zy, x, half, paint="hull"):
        """(z, y) 다각형을 x ± half로 뽑은 얇은 날개."""
        self.loft([[(x - half, y, z) for z, y in poly_zy], [(x + half, y, z) for z, y in poly_zy]], paint)

    def tube(self, a, b, r0, r1=None, n=10, paint="metal", smooth=True):
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

    def foil(self, root, tip, chord_dir, thick_dir, paint="hull"):
        """날개꼴 날개. root · tip = ((앞전 x, y, z), 시위, 두께). 시위는 chord_dir 쪽으로, 두께는 thick_dir 쪽으로 잡는다. 부드러운 음영."""
        cd = Vector(chord_dir).normalized()
        td = Vector(thick_dir).normalized()
        rings = []
        for le, chord, thick in (root, tip):
            le = Vector(le)
            upper = [le + cd * (f * chord) + td * (h * thick * 0.5) for f, h in FOIL[1:]]
            lower = [le + cd * (f * chord) - td * (h * thick * 0.5) for f, h in reversed(FOIL[1:])]
            rings.append([tuple(le)] + [tuple(v) for v in upper] + [tuple(v) for v in lower])
        self.loft(rings, paint, True)

    def box(self, c, h, paint="hull"):
        x, y, z = c
        hx, hy, hz = h
        self.slab([(x - hx, z - hz), (x + hx, z - hz), (x + hx, z + hz), (x - hx, z + hz)], y - hy, y + hy, 0.0, paint)

    def finish(self, name):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        bm.normal_update()
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(40.0):
                e.smooth = False
        twisted = [f for f in bm.faces if len(f.verts) == 4 and twist(f) > 0.02]
        bmesh.ops.triangulate(bm, faces=twisted, quad_method='SHORT_EDGE', ngon_method='BEAUTY')
        bm.normal_update()
        for f in bm.faces:
            set_uv(f, self.uv, f[self.paint])
            f.smooth = f[self.smooth] == 1
        for v in bm.verts:
            v.co = to_blender(v.co)
        # Unity → Blender 축 바꿈은 거울상이라 감는 방향이 뒤집힌다 — 모두 되돌린다
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.normal_update()
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        return me


# 날개꼴 (시위 비율, 두께 비율): 앞전 → 뒷전
FOIL = ((0.0, 0.0), (0.015, 0.42), (0.06, 0.72), (0.16, 0.93), (0.32, 1.0), (0.52, 0.86), (0.74, 0.55), (0.92, 0.22), (1.0, 0.08))


def twist(f):
    a, b, c, d = (v.co for v in f.verts)
    n = (b - a).cross(c - a)
    return abs(n.normalized().dot(d - a)) if n.length > 1e-6 else 0.0


def shrink(poly, d):
    """볼록 다각형을 무게중심 쪽으로 d만큼 줄인다(대략)."""
    cx = sum(p[0] for p in poly) / len(poly)
    cz = sum(p[1] for p in poly) / len(poly)
    out = []
    for x, z in poly:
        v = Vector((x - cx, z - cz))
        ln = v.length
        k = max(ln - d, ln * 0.2) / max(ln, 1e-6)
        out.append((cx + v.x * k, cz + v.y * k))
    return out


def set_uv(f, uv, code):
    if code >= 0:
        p = ((code * CELL + CELL * 0.5) / TEX, 1.0 - (SWATCH_Y + CELL * 0.5) / TEX)
        for loop in f.loops:
            loop[uv].uv = p
        return
    n = f.normal
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    for loop in f.loops:
        c = loop.vert.co
        y = min(max(c.y, Y_LOW), Y_HIGH)
        if ay >= ax and ay >= az and n.y > 0.0:
            px = TEX * 0.5 + min(max(c.z, -168.0), 168.0) * PX
            py = TOP_BAND + TOP_HALF * PX - min(max(c.x, -TOP_HALF), TOP_HALF) * PX
        elif az > ax * 1.4 and az > ay:
            sign = 1.0 if n.z > 0.0 else -1.0
            px = FRONT_RECT[0] + TOP_HALF * PX + min(max(c.x * sign, -TOP_HALF), TOP_HALF) * PX
            py = FRONT_RECT[1] + BAND - (y - Y_LOW) * PX
        else:
            pos = n.x >= 0.0
            z = min(max(c.z, -168.0), 168.0)
            px = TEX * 0.5 + (z if pos else -z) * PX       # 양쪽 모두 바깥에서 글자가 바로 읽힌다
            py = (SIDE_POS if pos else SIDE_NEG) + BAND - (y - Y_LOW) * PX
        loop[uv].uv = (px / TEX, 1.0 - py / TEX)


# ───────────── 선체 ─────────────

def round_ring(z, r, cy, k, n, x0=0.0):
    """둥근 단면. 윗부분은 cy + k·r 높이에서 평평하게 깎인다(갑판)."""
    top = cy + k * r
    ring = []
    for i in range(n):
        a = -math.pi * 0.5 + i * math.tau / n
        ring.append((x0 + math.cos(a) * r, min(cy + math.sin(a) * r, top), z))
    return ring


def central_hull(p):
    rings = [round_ring(z, r, HULL_CY, DECK_K, 32) for z, r in HULL_PROFILE]
    p.loft(rings, "hull", True)


def sail(p):
    """함교: 앞이 둥글고 뒤로 가늘어지는 물방울 평면. 위로 조금 좁아진다. 옆에 수평 잠항타, 위에 잠망경 · 안테나."""
    def plan(scale, dz=0.0):
        # 앞 반원(오른쪽 → 왼쪽) → 왼쪽 옆 → 둥근 꼬리 → 오른쪽 옆. 옆은 코사인으로 가늘어져 앞 반원 · 꼬리와 매끄럽게 이어진다
        r = SAIL_HALF * scale
        rt = 0.38 * r
        front = SAIL_Z1 - SAIL_HALF + dz
        zt = SAIL_Z0 + rt + dz * 0.5
        side = 6

        def half_w(k):
            return rt + (r - rt) * math.cos(k / side * math.pi * 0.5)
        pts = []
        for i in range(13):
            a = i * math.pi / 12.0
            pts.append((math.cos(a) * r, front + math.sin(a) * r))
        for k in range(1, side):
            pts.append((-half_w(k), front - (front - zt) * k / side))
        for i in range(9):
            a = math.pi + i * math.pi / 8.0
            pts.append((math.cos(a) * rt, zt + math.sin(a) * rt))
        for k in range(side - 1, 0, -1):
            pts.append((half_w(k), front - (front - zt) * k / side))
        return pts
    base = deck_y(10.0) - 1.0
    # 위 가장자리는 몇 단으로 둥글게 말린다(부드러운 음영 — 40도 넘게 꺾이는 곳만 각진다)
    levels = ((base, 1.0, 0.0), (SAIL_TOP - 2.6, 0.97, -0.1), (SAIL_TOP - 1.1, 0.92, -0.3), (SAIL_TOP - 0.35, 0.84, -0.6), (SAIL_TOP, 0.72, -0.9))
    p.loft([[(x, y, z) for x, z in plan(s, dz)] for y, s, dz in levels], "hull", True)
    # 수평 잠항타(함교 옆)
    for s in (1.0, -1.0):
        p.foil(((s * 3.6, 21.9, 22.0), 8.5, 1.0), ((s * 13.5, 21.9, 19.5), 4.0, 0.5), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0))
    # 잠망경 · 안테나(함교 지붕 뒤쪽)
    for x, z, h, r in ((0.0, 4.0, 6.5, 0.45), (1.1, 1.0, 5.0, 0.35), (-1.1, 0.5, 4.2, 0.4), (0.0, -2.5, 3.0, 0.6)):
        p.tube((x, SAIL_TOP - 0.2, z), (x, SAIL_TOP + h, z), r, r * 0.8, 8, "metal")
    # 함교 윗면 앞쪽 창(어두운 띠는 텍스처, 여기는 뚜껑 턱)
    p.box((0.0, SAIL_TOP + 0.25, 20.5), (1.8, 0.35, 2.0), "metal")


def stern_fins(p):
    """중앙 동체 꼬리: 위아래 방향타 + 수평 잠항타."""
    back, side_dir, up = (0.0, 0.0, -1.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)
    p.foil(((0.0, 1.0, -107.0), 19.0, 1.6), ((0.0, 17.0, -121.0), 10.0, 0.8), back, side_dir)
    p.foil(((0.0, -5.0, -107.0), 19.0, 1.6), ((0.0, -21.0, -121.0), 10.0, 0.8), back, side_dir)
    for s in (1.0, -1.0):
        p.foil(((s * 1.0, -2.1, -107.0), 20.0, 1.2), ((s * 15.0, -2.1, -121.0), 7.0, 0.6), back, up)


def side_hull(p, s):
    x0 = s * SIDE_X
    rings = [round_ring(z, r, SIDE_CY, SIDE_K, 24, x0) for z, r in SIDE_PROFILE]
    p.loft(rings, "hull", True)
    # 엔진 나셀 위 배기구(어두운 판 세 쌍)
    top = side_top(-102.0)
    for dz in (-97.0, -102.5, -108.0):
        for dx in (-2.6, 2.6):
            p.box((x0 + dx, top + 0.15, dz), (1.5, 0.3, 2.0), "vent")
    # 엔진 흡기구(나셀 앞, 바깥쪽)
    p.box((x0 + s * 6.6, SIDE_CY + 2.2, -90.0), (1.2, 1.6, 3.2), "vent")
    # 스크류 축 받침(나셀 끝에서 허브까지)
    p.tube((x0, SIDE_CY, -123.0), (x0, SIDE_CY, -125.8), 1.7, 1.3, 10, "metal")
    # 날개(파일런): 함교 바로 뒤에서 시작해 보조 동체로 간다. 위가 수면 위로 조금 나온다
    root, tip = s * 9.0, s * (SIDE_X - 4.0)
    wing = [(root, -10.0), (tip, -24.0), (tip, -92.0), (root, -102.0)]
    if s < 0.0:
        wing = list(reversed(wing))
    p.slab(wing, -2.2, 1.6, 0.0)
    p.slab(shrink(wing, 0.01), 1.6, 2.2, 1.2)
    # 날개 아래 버팀대(앞뒤 둘)
    for z in (-40.0, -80.0):
        p.slab([(root, z - 3.0), (tip, z - 3.0), (tip, z + 3.0), (root, z + 3.0)] if s > 0.0 else
               [(tip, z - 3.0), (root, z - 3.0), (root, z + 3.0), (tip, z + 3.0)], -7.5, -2.2, 0.0)


def deck_details(p):
    """앞 갑판: SLCM 해치 줄 사이 받침, 함교 뒤 갑판의 CIWS 받침, 뱃머리 계류 기둥."""
    for z in (REAR_FLAK_Z, REAR_CIWS_Z):
        y = deck_y(z)
        p.tube((0.0, y - 0.6, z), (0.0, y + 0.8, z), 3.2, 3.0, 12, "hull", False)
    for z in (118.0, 112.0):
        for x in (-2.5, 2.5):
            p.tube((x, deck_y(z) - 0.4, z), (x, deck_y(z) + 0.9, z), 0.35, 0.35, 6, "metal")


def hull():
    p = Part()
    central_hull(p)
    sail(p)
    stern_fins(p)
    for s in (1.0, -1.0):
        side_hull(p, s)
    deck_details(p)
    return p.finish("SB_Hull")


def drone_door():
    """무인기 사출구 덮개(6-217): 원점 = 바깥 경첩(위 가장자리), 로컬 -X로 2 x DRONE_HATCH_HX 뻗는 판. 위는 갑판색, 테두리 · 아래는 금속."""
    p = Part()
    w = DRONE_HATCH_HX * 2.0 + 0.2
    hz = DRONE_HATCH_HZ + 0.1
    p.box((-w * 0.5, 0.06, 0.0), (w * 0.5, 0.12, hz), "deck")      # 갑판보다 0.18m 솟은 덮개(갑판 면과 겹쳐 깜박이지 않게)
    # 보강 리브 두 줄(위)
    for z in (-hz * 0.45, hz * 0.45):
        p.box((-w * 0.5, 0.23, z), (w * 0.45, 0.05, 0.12), "metal")
    # 경첩 원통
    p.tube((0.0, 0.06, -hz), (0.0, 0.06, hz), 0.16, 0.16, 8, "metal")
    return p.finish("SB_DroneDoor")


def screw():
    """7날 스크류(원점 = 축 중심, 축 = 로컬 Z). 날은 반경을 따라 비틀리고 뒤로 휘었다(언월형)."""
    p = Part()
    p.tube((0.0, 0.0, 1.2), (0.0, 0.0, -0.8), 1.5, 1.5, 12, "bronze")
    p.tube((0.0, 0.0, -0.8), (0.0, 0.0, -3.0), 1.5, 0.25, 12, "bronze")
    pitch = 11.0
    for b in range(SCREW_BLADES):
        phi0 = b * math.tau / SCREW_BLADES
        rings = []
        steps = 7
        for k in range(steps + 1):
            t = k / steps
            r = 1.2 + (SCREW_R - 1.2) * t
            chord = 2.2 + 1.3 * math.sin(math.pi * t) - 0.9 * t * t
            thick = 0.24 * (1.0 - t) + 0.06
            theta = math.atan2(pitch, math.tau * r)
            phi = phi0 + 0.32 * t * t
            er = Vector((math.cos(phi), math.sin(phi), 0.0))
            et = Vector((-math.sin(phi), math.cos(phi), 0.0))
            ez = Vector((0.0, 0.0, 1.0))
            c = et * math.cos(theta) + ez * math.sin(theta)
            nrm = -et * math.sin(theta) + ez * math.cos(theta)
            center = er * r + ez * 0.2
            h = max(chord, 0.3) * 0.5
            rings.append([tuple(center + c * h), tuple(center + nrm * thick), tuple(center - c * h), tuple(center - nrm * thick)])
        p.loft(rings, "bronze", True)
    return p.finish("SB_Screw")


# ───────────── 텍스처 ─────────────

def noise(h, w, cells, seed, rows=None):
    rng = np.random.default_rng(seed)
    gh = rows + 2 if rows else max(int(h / w * cells), 2) + 2
    gw = cells + 2
    grid = rng.random((gh, gw)).astype(np.float32)
    y = np.linspace(0.0, gh - 1.001, h, dtype=np.float32)
    x = np.linspace(0.0, gw - 1.001, w, dtype=np.float32)
    y0 = y.astype(int)
    x0 = x.astype(int)
    ty = (y - y0)[:, None]
    tx = (x - x0)[None, :]
    ty = ty * ty * (3.0 - 2.0 * ty)
    tx = tx * tx * (3.0 - 2.0 * tx)
    return (grid[y0][:, x0] * (1 - tx) + grid[y0][:, x0 + 1] * tx) * (1 - ty) + (grid[y0 + 1][:, x0] * (1 - tx) + grid[y0 + 1][:, x0 + 1] * tx) * ty


def tiles(h, w, size_px, seed, spread=0.07):
    """흡음 타일: 엇갈린 줄의 작은 판. 판마다 밝기가 조금 다르고 이음매는 어둡다."""
    rng = np.random.default_rng(seed)
    out = np.ones((h, w), np.float32)
    y = 0
    row = 0
    while y < h:
        rh = size_px
        x = -(size_px // 2 if row % 2 else 0)
        while x < w:
            a, b = max(x, 0), min(x + size_px, w)
            if b > a:
                out[y:y + rh, a:b] = 1.0 + (rng.random() - 0.5) * spread
                out[y:y + rh, a:min(a + 1, w)] *= 0.8
            x += size_px
        out[y:min(y + 1, h), :] *= 0.8
        y += rh
        row += 1
    return out


def weather(h, w, seed):
    blotch = noise(h, w, 9, seed) * 0.6 + noise(h, w, 26, seed + 5) * 0.4
    streak = noise(h, w, max(w // 5, 2), seed + 1, max(h // 56, 2))
    where = np.clip(noise(h, w, 14, seed + 9) * 3.0 - 1.6, 0.0, 1.0)
    return (0.92 + 0.14 * blotch) * (1.0 - 0.08 * where * np.clip(streak * 2.6 - 1.5, 0.0, 1.0))


def to_linear(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def text_layer(items):
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
    temp = os.path.join(bpy.app.tempdir, "sb_page.png")
    scene.render.filepath = temp
    cam = bpy.data.cameras.new("page")
    cam.type = 'ORTHO'
    cam.ortho_scale = TEX
    ob = bpy.data.objects.new("page", cam)
    ob.location = (TEX * 0.5, -TEX * 0.5, 100.0)
    scene.collection.objects.link(ob)
    scene.camera = ob
    font = bpy.data.fonts.load(FONT)
    for i, (text, x, y, size, color) in enumerate(items):
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
        scene.collection.objects.link(t)
    bpy.ops.render.render(write_still=True, scene="page")
    image = bpy.data.images.load(temp)
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(TEX, TEX, 4)[::-1]
    bpy.data.images.remove(image)
    bpy.data.scenes.remove(scene)
    return pixels


SLATE = np.array((0.175, 0.19, 0.205), np.float32)
DECK = np.array((0.215, 0.23, 0.245), np.float32)
ANTIFOUL = np.array((0.27, 0.1, 0.08), np.float32)
HATCH_ROWS = (-5.8, 5.8)        # 가운데 16셀 VLS(폭 7m) 양옆
HATCH_Z = tuple(31.0 + i * 7.6 for i in range(10))
HATCH_HALF = 2.1


def make_texture():
    img = np.zeros((TEX, TEX, 3), np.float32)
    tile = int(1.3 * PX)

    def side_row(top, y):
        return int(top + BAND - (y - Y_LOW) * PX)

    def hull_area(h, w, seed):
        area = np.tile(SLATE, (h, w, 1))
        area *= tiles(h, w, tile, seed)[..., None]
        area *= weather(h, w, seed + 2)[..., None]
        return area

    for top, sign, seed in ((SIDE_POS, 1.0, 3), (SIDE_NEG, -1.0, 5)):
        area = hull_area(BAND, TEX, seed)
        boot = side_row(0, WATERLINE)
        area[boot:] = ANTIFOUL * (tiles(BAND - boot, TEX, tile, seed + 1, 0.05) * weather(BAND - boot, TEX, seed + 3))[..., None]
        area[boot - 2:boot + 2] = (0.07, 0.07, 0.075)
        # 물에 젖은 띠(수면 위 2m)는 조금 어둡다
        wet = side_row(0, 2.0)
        area[wet:boot - 2] *= 0.88
        img[top:top + BAND] = area

        def rect(z0, z1, y0, y1, color):
            a, b = sorted((TEX * 0.5 + sign * z0 * PX, TEX * 0.5 + sign * z1 * PX))
            img[side_row(top, y1):side_row(top, y0), int(a):int(b)] = color
        # 함교 앞쪽 창, 함교 문
        rect(17.0, 25.0, SAIL_TOP - 1.6, SAIL_TOP - 0.6, (0.03, 0.045, 0.06))
        rect(2.0, 4.0, deck_y(3.0) + 1.0, deck_y(3.0) + 4.0, (0.12, 0.13, 0.14))
        # 앞 갑판 아래 침수 구멍 줄(앞쪽만 — 뒤쪽은 보조 동체가 같은 칸을 쓴다)
        for z in np.arange(32.0, 106.0, 3.2):
            rect(z, z + 1.8, deck_y(z) - 2.2, deck_y(z) - 1.6, (0.05, 0.05, 0.055))
        # 뱃머리 흘수 표시
        for y in (-6.0, -4.0, -2.0, 0.0, 2.0):
            rect(121.0, 122.6, y - 0.2, y + 0.2, (0.62, 0.62, 0.58))

    fx, fy = FRONT_RECT
    w = int(TOP_HALF * 2 * PX)
    front = hull_area(BAND, w, 41)
    boot = side_row(0, WATERLINE)
    front[boot:] = ANTIFOUL * weather(BAND - boot, w, 42)[..., None]
    img[fy:fy + BAND, fx:fx + w] = front

    # 갑판 띠
    h = int(TOP_HALF * 2 * PX)
    deck = np.tile(DECK, (h, TEX, 1))
    deck *= tiles(h, TEX, tile, 21, 0.05)[..., None]
    deck *= weather(h, TEX, 22)[..., None]
    img[TOP_BAND:TOP_BAND + h] = deck
    mid = TOP_BAND + TOP_HALF * PX

    def deck_rect(z0, z1, x0, x1, color, mul=False):
        a, b = sorted((TEX * 0.5 + z0 * PX, TEX * 0.5 + z1 * PX))
        c, d = sorted((mid - x0 * PX, mid - x1 * PX))
        if mul:
            img[int(c):int(d), int(a):int(b)] *= color
        else:
            img[int(c):int(d), int(a):int(b)] = color
    # SLCM 해치: 두 줄 × 10. 테두리는 어둡고 경첩선이 있다
    for x in HATCH_ROWS:
        for z in HATCH_Z:
            deck_rect(z - HATCH_HALF, z + HATCH_HALF, x - HATCH_HALF, x + HATCH_HALF, (0.06, 0.065, 0.07))
            deck_rect(z - HATCH_HALF + 0.22, z + HATCH_HALF - 0.22, x - HATCH_HALF + 0.22, x + HATCH_HALF - 0.22, DECK * 1.08)
            deck_rect(z - 0.05, z + 0.05, x - HATCH_HALF + 0.22, x + HATCH_HALF - 0.22, 0.6, True)
    # 무인기 사출구(6-217): 덮개 아래 어두운 우물 + 노란 경고 테두리
    for sx in (1.0, -1.0):
        x = sx * DRONE_HATCH_X
        deck_rect(DRONE_HATCH_Z - DRONE_HATCH_HZ - 0.35, DRONE_HATCH_Z + DRONE_HATCH_HZ + 0.35, x - DRONE_HATCH_HX - 0.35, x + DRONE_HATCH_HX + 0.35, (0.5, 0.42, 0.12))
        deck_rect(DRONE_HATCH_Z - DRONE_HATCH_HZ, DRONE_HATCH_Z + DRONE_HATCH_HZ, x - DRONE_HATCH_HX, x + DRONE_HATCH_HX, (0.02, 0.022, 0.025))
    # 갑판 가장자리 안전선(흐린 노랑, 앞 갑판)
    for s in (1.0, -1.0):
        deck_rect(28.0, 112.0, s * 8.6, s * 8.9, (0.5, 0.45, 0.25))
    # 날개 윗면 가장자리
    for s in (1.0, -1.0):
        deck_rect(-100.0, -14.0, s * 9.5, s * 10.0, 0.7, True)

    for name, i in SWATCHES.items():
        img[SWATCH_Y:SWATCH_Y + CELL, i * CELL:(i + 1) * CELL] = COLORS[name]
    items = []
    for top, sign in ((SIDE_POS, 1.0), (SIDE_NEG, -1.0)):
        items.append(("01", TEX * 0.5 + sign * 5.0 * PX, side_row(top, 25.0), 130, (0.66, 0.67, 0.64)))
        items.append(("LV-01", TEX * 0.5 + sign * 108.0 * PX, side_row(top, 4.6), 36, (0.55, 0.56, 0.54)))
    page = text_layer(items)
    img = to_linear(img)
    alpha = page[..., 3:4]
    img = img * (1.0 - alpha) + to_linear(page[..., :3]) * alpha
    os.makedirs(OUT, exist_ok=True)
    tex = bpy.data.images.new("ACO_Submarine_Albedo", TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), np.float32)
    rgba[..., :3] = np.where(img <= 0.0031308, img * 12.92, 1.055 * np.power(np.maximum(img, 1e-6), 1.0 / 2.4) - 0.055)
    tex.pixels.foreach_set(rgba[::-1].ravel())
    tex.filepath_raw = ALBEDO
    tex.file_format = 'PNG'
    tex.save()
    return tex


def material(tex):
    mat = bpy.data.materials.new("ACO_Submarine")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = tex
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    return mat


# ───────────── 조립 · 미리보기 ─────────────

# 무장 자리(Unity 좌표). ACO_SubmarineBuilder의 표와 같은 값
VLS_Z = (80.0, 46.0)        # 16셀 긴 VLS 2(1.4배 — 7 x 27m), 앞 갑판 가운데 앞뒤로
SAIL_CIWS = (0.0, SAIL_TOP, 12.0)
REAR_FLAK_Z = -32.0         # 함교 뒤 갑판: Flak(앞) · CIWS(뒤). 받침 위(갑판 + 0.8m)
REAR_CIWS_Z = -66.0
SIDE_GUN_Z = -52.0          # 보조 동체 위 소형 레일건
# 무인기 사출구(6-217): 함교 뒤 Flak(-32)와 CIWS(-66) 사이 갑판 좌우. 덮개 경첩 = 바깥 가장자리(x = ±(X + HX)). ACO_SubmarineBuilder와 같은 값
DRONE_HATCH_Z = -49.0
DRONE_HATCH_X = 4.3
DRONE_HATCH_HX = 1.5
DRONE_HATCH_HZ = 3.4


def assemble(meshes):
    scene = bpy.context.scene
    obs = []

    def add(name, mesh, loc):
        ob = bpy.data.objects.new(name, meshes[mesh])
        scene.collection.objects.link(ob)
        ob.location = to_blender(loc)
        obs.append(ob)
        return ob
    add("SB_Hull", "SB_Hull", (0.0, 0.0, 0.0))
    add("SB_Screw_R", "SB_Screw", SCREW_POS)
    add("SB_Screw_L", "SB_Screw", (-SCREW_POS[0], SCREW_POS[1], SCREW_POS[2]))
    add("SB_DroneDoor_R", "SB_DroneDoor", (DRONE_HATCH_X + DRONE_HATCH_HX, deck_y(DRONE_HATCH_Z), DRONE_HATCH_Z))
    return obs


def weapon_markers():
    """미리보기 전용: 무장 자리를 색 상자로 보인다(FBX보다 나중에 만든다)."""
    scene = bpy.context.scene
    spots = [("VLS", (0.0, deck_y(z), z), (3.5, 0.9, 13.6), (0.85, 0.15, 0.1)) for z in VLS_Z]
    spots += [("CIWS", (0.0, deck_y(REAR_CIWS_Z) + 0.8, REAR_CIWS_Z), (1.4, 2.2, 1.4), (0.95, 0.8, 0.1)), ("CIWS", SAIL_CIWS, (1.4, 2.2, 1.4), (0.95, 0.8, 0.1))]
    spots.append(("FLAK", (0.0, deck_y(REAR_FLAK_Z) + 0.8, REAR_FLAK_Z), (1.8, 1.8, 1.8), (0.1, 0.75, 0.9)))
    spots += [("RAIL", (s * SIDE_X, side_top(SIDE_GUN_Z), SIDE_GUN_Z), (2.2, 1.5, 5.5), (0.6, 0.3, 0.9)) for s in (1.0, -1.0)]
    for i, (kind, c, h, color) in enumerate(spots):
        p = Part()
        p.box((c[0], c[1] + h[1], c[2]), h, "metal")
        me = p.finish(f"mark{i}")
        mat = bpy.data.materials.new(f"mark{i}")
        mat.use_nodes = True
        mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
        me.materials.append(mat)
        ob = bpy.data.objects.new(f"mark{i}", me)
        scene.collection.objects.link(ob)


def preview(folder, only, dry):
    scene = bpy.context.scene
    weapon_markers()
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 3
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 760
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.72, 0.85, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.85
    scene.world = world
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = 3.2
    sun.angle = math.radians(2.0)
    sun_ob = bpy.data.objects.new("sun", sun)
    sun_ob.rotation_euler = (Vector(to_blender((0.0, 0.0, 0.0))) - Vector(to_blender((300.0, 500.0, 200.0)))).to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(sun_ob)
    scene.render.image_settings.file_format = 'PNG'
    os.makedirs(folder, exist_ok=True)
    if not dry:
        water = bpy.data.meshes.new("water")
        water.from_pydata([(-3000, -3000, 0), (3000, -3000, 0), (3000, 3000, 0), (-3000, 3000, 0)], [], [(0, 1, 2, 3)])
        wm = bpy.data.materials.new("water")
        wm.use_nodes = True
        b = wm.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (0.03, 0.09, 0.14, 1.0)
        b.inputs["Roughness"].default_value = 0.25
        water.materials.append(wm)
        scene.collection.objects.link(bpy.data.objects.new("water", water))
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 50
    cam_data.clip_end = 6000.0
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    tag = "_dry" if dry else ""

    def shoot(name, eye, at):
        if only and name not in only:
            return
        e = Vector(to_blender(eye))
        cam.location = e
        cam.rotation_euler = (Vector(to_blender(at)) - e).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(folder, name + tag + ".png")
        bpy.ops.render.render(write_still=True)

    shoot("sb_quarter", (250.0, 120.0, 250.0), (0.0, 0.0, -5.0))
    shoot("sb_side", (470.0, 30.0, 0.0), (0.0, 4.0, 0.0))
    shoot("sb_top", (4.0, 520.0, 0.0), (0.0, 0.0, 0.0))
    shoot("sb_stern", (-150.0, 55.0, -250.0), (0.0, 0.0, -80.0))
    shoot("sb_bow", (110.0, 35.0, 250.0), (0.0, 6.0, 60.0))
    shoot("sb_sail", (60.0, 42.0, 50.0), (0.0, 18.0, 6.0))
    shoot("sb_screw", (60.0, 12.0, -175.0), (SIDE_X, 0.0, -120.0))
    shoot("sb_hatch", (24.0, 26.0, -26.0), (0.0, 8.0, -49.0))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = make_texture()
    mat = material(tex)
    scene = bpy.context.scene
    meshes = {}
    for me in (hull(), screw(), drone_door()):
        me.materials.append(mat)
        meshes[me.name] = me
        print("MESH", me.name, sum(len(f.vertices) - 2 for f in me.polygons))
    assemble(meshes)
    print("TRIS 전체", sum(sum(len(f.vertices) - 2 for f in ob.data.polygons) for ob in scene.objects if ob.type == 'MESH'))
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    if argv:
        dry = "dry" in argv[1:]
        names = [a for a in argv[1:] if a != "dry"]
        preview(argv[0], names, dry)


main()
