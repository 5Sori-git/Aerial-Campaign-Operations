# -*- coding: utf-8 -*-
"""랜스 포대와 포대(Flak, CIWS, VLS) 모델·텍스처 생성기. ACO_Lance.fbx, ACO_Turrets.fbx, ACO_Lance_*.png를 만든다.
실행: blender --background --factory-startup --python Tools/aco_lance_model.py
좌표는 Unity 축(부위 로컬, 단위 m)으로 적고, 저장할 때 FBX 축으로 바꾼다. 원점은 프리팹의 회전축과 같다.
모델을 고치면 다시 실행하고 Setup Check에 새 ID를 더한다(ACO_PrefabTuning.Entries)."""
import math
import os
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FBX = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Lance", "ACO_Lance.fbx")
TURRET_FBX = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Lance", "ACO_Turrets.fbx")
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Textures", "Railgun")
FONT = "C:/Windows/Fonts/arialbd.ttf"
TILE = 30.0
ATLAS_M = 120.0      # 장갑 텍스처 한 장 = 120m
SIZE = 2048
MINI_SCALE = 3.0     # NPC_Railgun이 소형 레일건에 주는 크기. 프리팹 안 메시는 이만큼 작다
TURRET_TILE = 12.0   # 포대는 프리팹 로컬 크기로 적는다. 2.5배로 놓일 때 강철 무늬가 30m마다 반복된다


class Solid:
    def __init__(self):
        self.bm = bmesh.new()

    def loft(self, rings):
        """같은 점 수의 고리들을 이어 닫힌 입체를 만든다(면은 평평하게)."""
        vs = [[self.bm.verts.new((p[0], p[2], p[1])) for p in r] for r in rings]
        n = len(rings[0])
        for a, b in zip(vs, vs[1:]):
            for k in range(n):
                m = (k + 1) % n
                self.bm.faces.new((a[k], a[m], b[m], b[k]))
        self.bm.faces.new(vs[0])
        self.bm.faces.new(list(reversed(vs[-1])))

    def prism(self, section, z0, z1):
        self.loft([[(x, y, z0) for x, y in section], [(x, y, z1) for x, y in section]])

    def box(self, c, s):
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        self.prism([(c[0] - hx, c[1] - hy), (c[0] + hx, c[1] - hy), (c[0] + hx, c[1] + hy), (c[0] - hx, c[1] + hy)], c[2] - hz, c[2] + hz)

    def tube_z(self, cx, cy, r, z0, z1, n=8):
        self.prism([(cx + r * math.cos(k * math.tau / n), cy + r * math.sin(k * math.tau / n)) for k in range(n)], z0, z1)

    def tube_x(self, cy, cz, r, x0, x1, n=12):
        ring = [(cy + r * math.cos(k * math.tau / n), cz + r * math.sin(k * math.tau / n)) for k in range(n)]
        self.loft([[(x0, y, z) for y, z in ring], [(x1, y, z) for y, z in ring]])

    def mesh(self, name):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def rect8(w, h, c, cx=0.0, cy=0.0):
    x, y = w / 2, h / 2
    return [(cx + a, cy + b) for a, b in ((-x + c, -y), (x - c, -y), (x, -y + c), (x, y - c), (x - c, y), (-x + c, y), (-x, y - c), (-x, -y + c))]


def barrel():
    s = Solid()
    length_end = 282.0
    # 레일 두 줄: 사이의 틈으로 충전 빛이 보인다
    for sign in (1, -1):
        s.prism(rect8(24, 12, 1.5, 0, sign * 12), -30, length_end)
        s.prism(rect8(14, 4, 1, 0, sign * 19.5), -20, length_end - 8)    # 레일 등판
    # 코일 고리: 포미 쪽이 크고 포구로 갈수록 작아진다
    z = 32.0
    index = 0
    while z < 262:
        t = (z - 32) / 230
        w = 40 - 8 * t
        h = 46 - 8 * t
        heavy = index % 3 == 0
        depth = 9 if heavy else 5
        ring = rect8(w, h, 6)
        for sign in (1, -1):
            s.prism([(a, sign * max(sign * b, 9.0)) if sign * b > 0 else (a, sign * 9.0) for a, b in ring[4:6] + ring[3:4] + ring[6:7]] and
                    [(-w / 2 + 6, sign * h / 2), (w / 2 - 6, sign * h / 2), (w / 2, sign * (h / 2 - 6)), (w / 2, sign * 9), (-w / 2, sign * 9), (-w / 2, sign * (h / 2 - 6))], z - depth / 2, z + depth / 2)
            s.box((sign * (w / 2 - 2.5), 0, z), (5, 18, depth))
        if heavy:
            for sign in (1, -1):
                s.box((sign * (w / 2 + 2.2), 0, z), (4.5, 10, depth + 3))
        z += 25.0
        index += 1
    # 전력 관: 고리를 따라 양옆으로 달린다
    for sign in (1, -1):
        s.tube_z(sign * 19.5, 9.5, 1.7, -8, 236)
        s.tube_z(sign * 19.5, -9.5, 1.7, -8, 236)
    # 포미: 각진 뭉치
    s.loft([[(x, y, -44) for x, y in rect8(32, 34, 6)],
            [(x, y, -30) for x, y in rect8(46, 48, 8)],
            [(x, y, 8) for x, y in rect8(46, 48, 8)],
            [(x, y, 24) for x, y in rect8(34, 40, 8)]])
    # 회전축
    s.tube_x(0, 8, 13, -40, 40)
    for sign in (1, -1):
        s.tube_x(0, 8, 16, sign * 30, sign * 40)
    # 포구 제동기: 구멍이 보이게 네 조각으로
    for sign in (1, -1):
        s.loft([[(x, sign * y, z) for x, y in [(-16, 11), (16, 11), (17, 19), (-17, 19)]] for z in (268, 270)] +
               [[(x, sign * y, z) for x, y in [(-19, 11), (19, 11), (21, 23), (-21, 23)]] for z in (273, 286)] +
               [[(x, sign * y, 290) for x, y in [(-15, 11), (15, 11), (16, 18), (-16, 18)]]])
        s.box((sign * 16, 0, 279), (9, 22, 22))
        for k in range(3):
            s.box((sign * 21.5, 0, 271.5 + k * 7.5), (2.5, 16, 3.5))
    return s.mesh("Barrel")


def clam(side):
    """좌우 장갑. side=-1이 Armor_L: x = 0쪽이 바닥 바깥 모서리, 지붕이 가운데(x 52)로 뻗는다."""
    s = Solid()
    sx = -side
    outer = [(-4.5, 11.8), (-4.5, 24), (7, 44), (31, 54), (53.5, 54.5)]   # 닫혔을 때 가운데 틈 1m
    inner = [(53.5, 49), (33, 49), (11, 40), (0, 22), (0, 11.8)]

    def ring(z, drop):
        pts = []
        for i, (x, y) in enumerate(outer):
            pts.append((sx * (x + drop * 0.7 if x < 50 else x), max(y - drop, 11.8), z))
        for x, y in inner:
            pts.append((sx * (x + drop * 0.7 if x < 50 else x), max(y - drop, 11.8), z))
        return pts

    s.loft([ring(-51.6, 7), ring(-44, 0), ring(44, 0), ring(51.6, 7)])

    def plate(a, b, z0, z1, lift=1.4, inset=1.0):
        """외곽선 두 점 사이 면 위에 덧판을 얹는다."""
        ax, ay = a
        bx, by = b
        nx, ny = -(by - ay), bx - ax
        l = math.hypot(nx, ny)
        nx, ny = nx / l, ny / l
        if nx * sx * 0 + ny < 0:
            nx, ny = -nx, -ny
        base = [(ax, ay, z0), (bx, by, z0), (bx, by, z1), (ax, ay, z1)]
        dx, dy = (bx - ax) / max(l, 1), (by - ay) / max(l, 1)
        top = [(ax + dx * inset + nx * lift, ay + dy * inset + ny * lift, z0 + inset),
               (bx - dx * inset + nx * lift, by - dy * inset + ny * lift, z0 + inset),
               (bx - dx * inset + nx * lift, by - dy * inset + ny * lift, z1 - inset),
               (ax + dx * inset + nx * lift, ay + dy * inset + ny * lift, z1 - inset)]
        s.loft([[(sx * x, y, z) for x, y, z in base], [(sx * x, y, z) for x, y, z in top]])

    bays = [(-40, -21), (-18, 18), (21, 40)]
    for z0, z1 in bays:
        plate((-4.5, 13), (-4.5, 23), z0, z1, 1.6)
        plate((30, 54), (51.5, 54.5), z0, z1, 1.2)
    # 경사면: 가운데는 큰 덧판, 양옆은 환기 살
    plate((8, 45), (30, 53.5), -18, 18, 1.8)
    for z0 in (-40, 21):
        for k in range(5):
            t0 = 0.12 + k * 0.17
            t1 = t0 + 0.09
            a = (7 + 24 * t0, 44 + 10 * t0)
            b = (7 + 24 * t1, 44 + 10 * t1)
            plate(a, b, z0, z0 + 19, 0.9, 0.3)
    # 회전축을 받침 윗면의 바깥 모서리로 옮긴다: 열려도 받침 아래로 내려가지 않는다
    for v in s.bm.verts:
        v.co.x += sx * 6.3
        v.co.z -= 11.8
    return s.mesh("Clam")


# 장갑과 같은 윤곽(월드 x, y). 격벽·뒤 장갑이 코어 장갑과 이어져 보이게 한다
SILHOUETTE = [(-58.5, 17), (58.5, 17), (58.5, 41), (47, 61), (23, 71), (-23, 71), (-47, 61), (-58.5, 41)]


def rear():
    """뒤 장갑: 열리지 않는다. 코어 장갑의 깎인 뒤 끝과 같은 윤곽이라 튀어나오지 않는다."""
    s = Solid()
    outline = [(-53.4, 0.4), (53.4, 0.4), (53.4, 5.7), (42, 25.7), (18, 35.5), (-18, 35.5), (-42, 25.7), (-53.4, 5.7)]   # 회전축 높이 y 28 기준
    small = [(x * 0.86, 2 + (y - 0.4) * 0.84) for x, y in outline]
    s.loft([[(x, y, -2) for x, y in outline], [(x, y, 2) for x, y in outline]])
    s.loft([[(x, y, -2) for x, y in small], [(x * 0.98, 0.3 + y * 0.98, -3.2) for x, y in small]])
    for x in (-27, -9, 9, 27):
        s.box((x, 12.5, -3.6), (13, 15, 1.2))
    return s.mesh("Rear")


def circle(r, y, n=24):
    return [(r * math.cos(k * math.tau / n), y, r * math.sin(k * math.tau / n)) for k in range(n)]


def mount():
    s = Solid()
    # 받침 고리
    s.loft([circle(120, 16), circle(118, 22), circle(112, 28)])
    # 코어 칸 바닥과 둘레 틀
    plinth = [(-56, -129), (56, -129), (64, -121), (64, -13), (-64, -13), (-64, -121)]
    wider = [(-58, -132), (58, -132), (68, -122), (68, -13), (-68, -13), (-68, -122)]
    s.loft([[(x, 16, z) for x, z in wider], [(x, 21, z) for x, z in wider], [(x, 28.4, z) for x, z in plinth]])
    # 앞 격벽: 코어 장갑과 같은 윤곽의 뼈대 + 판
    frame = [(x * 0.99, y if y > 20 else 28) for x, y in SILHOUETTE]
    s.loft([[(x * 0.96, 28 + (y - 28) * 0.97, -15.6) for x, y in frame], [(x, y, -14.2) for x, y in frame],
            [(x, y, -9) for x, y in frame], [(x * 0.92, 28 + (y - 28) * 0.94, -6.5) for x, y in frame]])
    for sign in (1, -1):
        # 보강 기둥과 경사 지지대
        s.loft([[(sign * x, y, z) for x, y in ((28, 28), (40, 28), (40, 62), (28, 66))] for z in (-6.5, -2)])
        s.box((sign * 52, 42, -4.5), (10, 26, 5))
        s.tube_z(sign * 24, 60, 2.2, -8, 2)
        s.tube_z(sign * 35, 60, 2.2, -8, 2)
    # 포신 받침 뺨: 곧은 판 대신 각진 뼈대와 늑재
    for sign in (1, -1):
        x0, x1 = sign * 32, sign * 48
        side = [(28, 92), (40, 86), (62, 58), (64, 38), (62, 18), (40, -4), (28, -8)]
        s.loft([[(x0, y, z) for y, z in side], [(x1, y, z) for y, z in side]])
        # 바깥 늑재
        for z in (0, 22, 54, 76):
            s.box((sign * 53, 40, z), (10, 24, 5))
        s.box((sign * 50, 37, 38), (4, 18, 40))
        # 회전축 베어링과 고각 유압기
        s.tube_x(50, 38, 15, sign * 48, sign * 52)
        s.tube_x(50, 38, 9, sign * 52, sign * 62)
        s.loft([[(sign * 40 + 4.2 * math.cos(k * math.tau / 8), 28 + 4.2 * math.sin(k * math.tau / 8), 96) for k in range(8)],
                [(sign * 40 + 4.2 * math.cos(k * math.tau / 8), 52 + 4.2 * math.sin(k * math.tau / 8), 72) for k in range(8)]])
    # 축전기: 장갑이 열리면 보인다
    for sign in (1, -1):
        for k in range(4):
            z = -106 + k * 25
            s.loft([circle(8, 31)[::1], circle(8, 47)] if False else
                   [[(sign * 37 + 7.5 * math.cos(a_ * math.tau / 12), 31, z + 7.5 * math.sin(a_ * math.tau / 12)) for a_ in range(12)],
                    [(sign * 37 + 7.5 * math.cos(a_ * math.tau / 12), 47, z + 7.5 * math.sin(a_ * math.tau / 12)) for a_ in range(12)]])
            s.box((sign * 37, 48, z), (12, 2.5, 12))
        s.box((sign * 37, 50.5, -68), (5, 3, 88))
    return s.mesh("Mount")


def annulus(s, r0, r1, y0, y1, n=24, step=1):
    """가운데가 뚫린 고리."""
    for k in range(0, n, step):
        a0 = k * math.tau / n
        a1 = (k + step) * math.tau / n
        pts = [(r0, a0), (r1, a0), (r1, a1), (r0, a1)]
        s.loft([[(r * math.cos(a_), y, r * math.sin(a_)) for r, a_ in pts] for y in (y0, y1)])


def disc_z(r, z, n=24, cx=0.0, cy=0.0):
    return [(cx + r * math.cos(k * math.tau / n), cy + r * math.sin(k * math.tau / n), z) for k in range(n)]


# ── 원자로: 길이 방향이 Z. 중심 = Core 자리 ──
REACTOR_R = 19.0
REACTOR_HALF = 42.0


def reactor_core(scale=38.0):
    """빛나는 노심. 앞뒤가 평평한 원통. CoreSphere 자리(로컬 크기 38)에 맞춰 줄인다."""
    s = Solid()
    s.loft([[(x / scale, y / scale, z / scale) for x, y, z in disc_z(r, z)] for r, z in
            ((14.5, -36), (15.5, -34), (15.5, 34), (14.5, 36))])
    return s.mesh("Reactor_Core")


def reactor_cage():
    """세로 살과 고리. 살 사이로 노심이 보인다."""
    s = Solid()
    for k in range(10):
        a = k * math.tau / 10
        c, n = math.cos(a), math.sin(a)
        t = (-n, c)
        section = [(16.5, -1.6), (REACTOR_R + 1.2, -2.2), (REACTOR_R + 1.2, 2.2), (16.5, 1.6)]
        s.prism([(c * r + t[0] * w, n * r + t[1] * w) for r, w in section], -36, 36)
    for z in (-24, -8, 8, 24):
        ring = []
        for k in range(24):
            a0, a1 = k * math.tau / 24, (k + 1) * math.tau / 24
            q = [(REACTOR_R - 1.5, a0), (REACTOR_R + 2, a0), (REACTOR_R + 2, a1), (REACTOR_R - 1.5, a1)]
            s.prism([(r * math.cos(a_), r * math.sin(a_)) for r, a_ in q], z - 1.6, z + 1.6)
    return s.mesh("Reactor_Cage")


def reactor_caps():
    """앞뒤의 평평한 마개."""
    s = Solid()
    for sign in (1, -1):
        s.loft([disc_z(r, sign * z) for r, z in ((16, 35), (REACTOR_R + 2.5, 37), (REACTOR_R + 2.5, 41), (REACTOR_R - 1, REACTOR_HALF), (9, REACTOR_HALF))])
        for k in range(8):
            a = k * math.tau / 8
            s.tube_z((REACTOR_R - 3.5) * math.cos(a), (REACTOR_R - 3.5) * math.sin(a), 1.1, sign * 41.5, sign * 43.4, 6)
        s.tube_z(0, 0, 6, sign * 41.5, sign * 45, 12)
    return s.mesh("Reactor_Caps")


def reactor_pipes():
    """냉각 관: 마개에서 나와 양옆 바닥으로 내려간다."""
    s = Solid()
    for sz in (1, -1):
        for sx in (1, -1):
            x = sx * 11
            z = sz * 43
            s.tube_x(13, z + sz * 2.2, 1.8, x * 0.9, sx * 30, 8)
            s.loft([[(sx * 30 + 1.8 * math.cos(k * math.tau / 8), y, z + sz * 2.2 + 1.8 * math.sin(k * math.tau / 8)) for k in range(8)] for y in (-8, 14.5)])
    return s.mesh("Reactor_Pipes")


def reactor_cradle():
    """받침대: 원통을 양옆에서 받친다. 바닥에 묻힌다."""
    s = Solid()
    for z in (-30, 0, 30):
        for sx in (1, -1):
            s.loft([[(sx * x, y, z + dz) for x, y in ((14, -9), (28, -9), (28, -3), (21, 4), (17.5, 0), (14, -6))] for dz in (-3.5, 3.5)])
    return s.mesh("Reactor_Cradle")


def reactor_frame():
    """코어 구조물 전체(살, 마개, 관, 받침대)를 한 메시로."""
    bm = bmesh.new()
    for part in (reactor_cage(), reactor_caps(), reactor_pipes(), reactor_cradle()):
        bm.from_mesh(part)
    me = bpy.data.meshes.new("Reactor_Frame")
    bm.to_mesh(me)
    bm.free()
    return me


# ── 냉각기: 지름 48, 높이 60 안 ──
def cooler():
    s = Solid()
    s.prism(rect8(47, 47, 9), 0, 0) if False else None
    s.loft([[(x, y0, z) for x, z in rect8(47, 47, 9)] for y0 in (0, 5.5)])
    s.loft([circle(20, 9, 16), circle(17, 12, 16), circle(16, 50, 16)])
    # 방열 핀
    for k in range(12):
        a = k * math.tau / 12
        c, n = math.cos(a), math.sin(a)
        t = (-n, c)
        rows = [(16, 12), (23.5, 15), (23.5, 44), (16, 48)]
        s.loft([[(c * r + t[0] * w, y, n * r + t[1] * w) for r, y in rows] for w in (-0.6, 0.6)])
    for y in (20, 34):
        annulus(s, 16, 24.2, y - 0.8, y + 0.8, 16)
    # 위쪽 환풍구: 안이 뚫린 덮개 + 날개
    annulus(s, 17, 23, 48, 51, 16)
    annulus(s, 18.5, 22, 51, 59.5, 16)
    s.loft([circle(4, 50, 8), circle(3.4, 55, 8)])
    for k in range(6):
        a = k * math.tau / 6
        c, n = math.cos(a), math.sin(a)
        t = (-n, c)
        s.loft([[(c * r + t[0] * 2.6, 52.8, n * r + t[1] * 2.6), (c * r + t[0] * -2.6, 53.8, n * r + t[1] * -2.6)] +
                [(c * r + t[0] * -2.6, 54.4, n * r + t[1] * -2.6), (c * r + t[0] * 2.6, 53.4, n * r + t[1] * 2.6)] for r in (3.4, 17.6)])
    # 받침의 배관 두 줄
    for sign in (1, -1):
        s.loft([[(sign * 21 + 1.6 * math.cos(k * math.tau / 8), y, 1.6 * math.sin(k * math.tau / 8)) for k in range(8)] for y in (5, 15)])
    return s.mesh("Cooler")


def cooler_hazard():
    s = Solid()
    s.loft([[(x, y0, z) for x, z in rect8(44, 44, 8)] for y0 in (5.5, 8.5)])
    return s.mesh("CoolerHazard")


# ── 기초: 패드는 그대로 두고 위에 얹는다 ──
def base_detail():
    s = Solid()
    # 내륜 단차의 점검로와 경사로
    annulus(s, 140, 146, 8, 16.4, 48)
    for k in range(8):
        a = k * math.tau / 8 + math.tau / 16
        c, n = math.cos(a), math.sin(a)
        t = (-n, c)
        s.loft([[(c * r + t[0] * 9, y, n * r + t[1] * 9), (c * r - t[0] * 9, y, n * r - t[1] * 9), (c * r - t[0] * 9, 8, n * r - t[1] * 9), (c * r + t[0] * 9, 8, n * r + t[1] * 9)] for r, y in ((138, 16.4), (172, 8.1))])
    # 바깥 방벽: 틈을 두고 이어진다
    n = 40
    for k in range(n):
        if k % 5 == 4:
            continue
        a0, a1 = k * math.tau / n, (k + 0.92) * math.tau / n
        q = [(500, a0), (510, a0), (510, a1), (500, a1)]
        top = [(502, a0), (507, a0), (507, a1), (502, a1)]
        s.loft([[(r * math.cos(a_), 8, r * math.sin(a_)) for r, a_ in q], [(r * math.cos(a_), 15, r * math.sin(a_)) for r, a_ in top]])
    # 조명탑
    for k in range(6):
        a = k * math.tau / 6 + 0.2
        c, n_ = math.cos(a), math.sin(a)
        x, z = c * 330, n_ * 330
        s.box((x, 12, z), (7, 8, 7))
        s.loft([[(x + 1.2 * math.cos(b * math.tau / 6), y, z + 1.2 * math.sin(b * math.tau / 6)) for b in range(6)] for y in (16, 44)])
        s.box((x, 45, z), (9, 3, 3))
    # 포구 쪽 폭풍 방지벽
    for sign in (1, -1):
        s.loft([[(sign * x, y, z) for x, y in ((84, 8), (106, 8), (106, 16), (92, 24))] for z in (215, 300)])
    return s.mesh("BaseDetail")


# ── 소형 레일건: 원점과 축은 프리팹과 같다(월드 크기 기준, 고각 축 높이 4.8) ──
PIVOT_Y = 4.8


def mini_model():
    """낮은 고정 받침. 포탑 뒤쪽이 내려올 자리를 비워 둔다. 원점 = 포대 바닥."""
    s = Solid()
    s.loft([[(x, y, z) for x, z in rect8(w, w, w * 0.24)] for w, y in ((13.5, 0.0), (13.5, 0.6), (11.5, 1.2))])
    return s.mesh("MiniBase")


def mini_yoke():
    """방위 회전부: 회전판 + 포크 두 팔. 원점 = 고각 축 높이(TurretYaw)."""
    s = Solid()
    s.loft([[(5.4 * math.cos(k * math.tau / 16), y - PIVOT_Y, 5.4 * math.sin(k * math.tau / 16)) for k in range(16)] for y in (1.2, 1.5)])
    for sign in (1, -1):
        x0, x1 = sign * 2.35, sign * 3.4
        arm = [(-2.9, -3.3), (2.9, -3.3), (2.3, -1.2), (1.5, 1.3), (0.6, 2.0), (-0.6, 2.0), (-1.5, 1.3), (-2.3, -1.2)]
        s.loft([[(x0, y, z) for z, y in arm], [(x1, y, z) for z, y in arm]])
        s.tube_x(0, 0, 1.35, sign * 3.4, sign * 3.95, 12)
    return s.mesh("MiniYoke")


def mini_gun():
    """고각 회전부: 짧은 요람 + 쌍 레일. 원점 = 고각 축(TurretPitch). 뒤쪽을 짧게 잘라 85도까지 들 수 있다."""
    s = Solid()
    s.tube_x(0, 0, 1.2, -2.3, 2.3, 12)
    # 요람: 축 뒤 2.5m까지만 내려온다
    s.loft([[(x, y, z) for x, y in rect8(w, h, 0.5)] for w, h, z in ((3.0, 2.8, -2.5), (4.2, 3.9, -1.6), (4.2, 3.9, 5.5), (3.0, 3.2, 7.5))])
    for sign in (1, -1):
        s.prism(rect8(2.1, 0.8, 0.2, 0, sign * 0.85), 7, 29)
    for z in (11, 16.5, 22):
        for sign in (1, -1):
            s.box((0, sign * 1.45, z), (2.9, 0.4, 1.2))
            s.box((sign * 1.3, 0, z), (0.3, 2.4, 1.2))
    for sign in (1, -1):
        s.box((0, sign * 1.5, 28.4), (3.3, 1.0, 2.2))
    return s.mesh("MiniGun")


def charge_strip():
    s = Solid()
    s.prism(rect8(14, 6, 1.2), -30, 262)
    return s.mesh("ChargeStrip")


def box_uv(me, tile=TILE):
    """면 방향으로 세 축 중 하나를 골라 평면 투영한다."""
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = abs(n.x)
        ay = abs(n.y)
        az = abs(n.z)
        for loop in f.loops:
            c = loop.vert.co
            if az >= ax and az >= ay:
                t = (c.x, c.y)
            elif ax >= ay:
                t = (c.y, c.z)
            else:
                t = (c.x, c.z)
            loop[uv].uv = (t[0] / tile, t[1] / tile)
    bm.to_mesh(me)
    bm.free()


def armor_uv(me, side):
    """코어 장갑: 벽은 아래 칸, 경사·지붕은 위 칸에 펼친다. 좌우 모두 바깥에서 글자가 바로 읽히도록 앞뒤 방향을 뒤집는다."""
    sx = -side
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    plain = ((114.0 + 2) / ATLAS_M, 2.0 / ATLAS_M)
    for f in bm.faces:
        n = f.normal
        outward_wall = n.x * sx < -0.5
        roof = n.z > 0.3
        for loop in f.loops:
            c = loop.vert.co
            d = sx * c.x
            u = 52.0 - c.y if side < 0 else 52.0 + c.y
            if roof:
                t = (u + 4, 47 + d)
            elif outward_wall:
                t = (u + 4, 2 + c.z)
            else:
                t = (116.0, 2.0)
            loop[uv].uv = (t[0] / ATLAS_M, t[1] / ATLAS_M)
    bm.to_mesh(me)
    bm.free()


def rear_uv(me):
    """뒤 장갑: 벽 칸의 글자 없는 자리를 쓴다."""
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for loop in f.loops:
            c = loop.vert.co
            loop[uv].uv = ((c.x + 58) / ATLAS_M, (13 + c.z * 0.9) / ATLAS_M)
    bm.to_mesh(me)
    bm.free()


def wrap_noise(size, cells, seed):
    rng = np.random.default_rng(seed)
    g = rng.random((cells, cells))
    xs = np.linspace(0, cells, size, endpoint=False)
    xi = xs.astype(int)
    t = xs - xi
    t = t * t * (3 - 2 * t)
    x1 = (xi + 1) % cells
    a = g[np.ix_(xi, xi)]
    b = g[np.ix_(xi, x1)]
    c = g[np.ix_(x1, xi)]
    d = g[np.ix_(x1, x1)]
    top = a * (1 - t) + b * t
    bot = c * (1 - t) + d * t
    return top * (1 - t[:, None]) + bot * t[:, None]


def fbm(size, cells, seed):
    return wrap_noise(size, cells, seed) * 0.55 + wrap_noise(size, cells * 2, seed + 1) * 0.3 + wrap_noise(size, cells * 4, seed + 2) * 0.15


def save_png(name, rgb, alpha=None, size=SIZE):
    im = bpy.data.images.new(name, size, size, alpha=True)
    a = alpha if alpha is not None else np.ones((size, size), dtype=np.float32)
    im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(np.dstack((rgb, a)).astype(np.float32).ravel())
    path = os.path.join(OUT, name + ".png")
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)
    return path


def make_steel():
    """타일되는 어두운 강철: 판 이음매, 리벳, 약한 때."""
    n = SIZE
    px = 2048 / 3.0
    xx, yy = np.meshgrid(np.arange(n), np.arange(n))
    seam = np.zeros((n, n), dtype=np.float32)
    rivet = np.zeros((n, n), dtype=np.float32)
    # 줄마다 어긋난 판: 3줄, 줄마다 2~3칸
    rows = 3
    row_h = n // rows
    for r in range(rows):
        y0 = r * row_h
        seam = np.maximum(seam, np.exp(-(((yy - y0 + n // 2) % n - n // 2) / 2.2) ** 2))
        cols = 3 if r % 2 == 0 else 2
        off = r * 311
        for c in range(cols):
            x0 = (c * n // cols + off) % n
            inside = ((yy - y0) % n) < row_h
            seam = np.maximum(seam, np.exp(-(((xx - x0 + n // 2) % n - n // 2) / 2.2) ** 2) * inside)
        # 이음매를 따라가는 리벳
        for k in range(24):
            x = (k + 0.5) * n / 24
            for yr in (y0 + 16, y0 + row_h - 16):
                dx = (xx - x + n // 2) % n - n // 2
                dy = (yy - yr + n // 2) % n - n // 2
                rivet = np.maximum(rivet, np.exp(-(dx * dx + dy * dy) / 16.0))
    mott = fbm(n, 6, 3) - 0.5
    fine = fbm(n, 64, 11) - 0.5
    streak = wrap_noise(n, 8, 21) * 0.5 + np.roll(wrap_noise(n, 96, 22), 0, axis=0)[:, ::1] * 0.5
    img = np.tile(np.array([0.27, 0.29, 0.31], dtype=np.float32)[None, None, :], (n, n, 1))
    img *= 1 + mott[..., None] * 0.28 + fine[..., None] * 0.12
    img *= 1 - np.clip(wrap_noise(n, 10, 31) - 0.62, 0, 1)[..., None] * 0.7
    img *= 1 - seam[..., None] * 0.55
    img = img * (1 - rivet[..., None] * 0.35) + 0.42 * rivet[..., None] * 0.35
    albedo = save_png("ACO_Lance_Steel_Albedo", np.clip(img, 0, 1))
    height = -seam * 1.0 + rivet * 0.8 + fine * 0.12
    dx = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
    dy = np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)
    strength = 2.2
    nz = np.ones_like(dx)
    length = np.sqrt((dx * strength) ** 2 + (dy * strength) ** 2 + 1)
    normal = np.dstack((-dx * strength / length * 0.5 + 0.5, -dy * strength / length * 0.5 + 0.5, nz / length * 0.5 + 0.5))
    nor = save_png("ACO_Lance_Steel_Normal", normal.astype(np.float32))
    return albedo, nor


def render_page(items):
    """글자·기호를 아틀라스 좌표(m)에 놓고 렌더한다."""
    page = bpy.data.scenes.new("page")
    page.render.engine = 'BLENDER_WORKBENCH'
    page.display.shading.light = 'FLAT'
    page.display.shading.color_type = 'MATERIAL'
    page.display.shading.show_shadows = False
    page.display.render_aa = '32'
    page.render.film_transparent = True
    page.render.resolution_x = SIZE
    page.render.resolution_y = SIZE
    page.render.image_settings.file_format = 'PNG'
    page.render.image_settings.color_mode = 'RGBA'
    page.view_settings.view_transform = 'Standard'
    page.render.filepath = os.path.join(OUT, "_page.png")
    cam = bpy.data.objects.new("pcam", bpy.data.cameras.new("pcam"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ATLAS_M
    cam.location = (ATLAS_M / 2, ATLAS_M / 2, 5)
    page.collection.objects.link(cam)
    page.camera = cam
    font = bpy.data.fonts.load(FONT)
    mats = {}
    z = [0.0]

    def material(col):
        if col not in mats:
            m = bpy.data.materials.new("m")
            m.diffuse_color = tuple(c ** 2.2 for c in col) + (1,)
            mats[col] = m
        return mats[col]

    def place(o, u, v, col):
        z[0] += 0.001
        o.location = (u, v, z[0])
        o.data.materials.append(material(col))
        page.collection.objects.link(o)

    def text(body, u, v, size, col, spacing=1.08):
        c = bpy.data.curves.new("t", 'FONT')
        c.body = body
        c.font = font
        c.size = size
        c.align_x = 'CENTER'
        c.align_y = 'CENTER'
        c.space_character = spacing
        c.space_line = 1.15
        place(bpy.data.objects.new("t", c), u, v, col)

    def poly(points, u, v, col):
        bm = bmesh.new()
        bm.faces.new([bm.verts.new((x, y, 0)) for x, y in points])
        me = bpy.data.meshes.new("p")
        bm.to_mesh(me)
        bm.free()
        place(bpy.data.objects.new("p", me), u, v, col)

    def rect(w, h, u, v, col):
        poly([(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)], u, v, col)

    def circle(r, u, v, col):
        poly([(r * math.cos(k * math.tau / 32), r * math.sin(k * math.tau / 32)) for k in range(32)], u, v, col)

    def trefoil(r, u, v, col, back):
        """위험 표지: 세 날개 + 가운데 원."""
        for k in range(3):
            a0 = math.radians(90 + k * 120 - 30)
            a1 = math.radians(90 + k * 120 + 30)
            pts = [(r * 0.28 * math.cos(a0 + (a1 - a0) * t / 8), r * 0.28 * math.sin(a0 + (a1 - a0) * t / 8)) for t in range(9)]
            pts += [(r * math.cos(a1 - (a1 - a0) * t / 8), r * math.sin(a1 - (a1 - a0) * t / 8)) for t in range(9)]
            poly(pts, u, v, col)
        circle(r * 0.18, u, v, col)

    items(text, poly, rect, circle, trefoil)
    bpy.ops.render.render(write_still=True, scene="page")
    img = bpy.data.images.load(os.path.join(OUT, "_page.png"))
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    bpy.data.scenes.remove(page)
    os.remove(os.path.join(OUT, "_page.png"))
    return px.reshape(h, w, 4)


def make_armor():
    """코어 장갑 전용 텍스처. 아틀라스 좌표: u = 앞뒤(0~104m), 아래 칸 = 벽, 위 칸 = 경사·지붕."""
    n = SIZE
    INK = (0.12, 0.13, 0.11)
    PALE = (0.8, 0.8, 0.7)
    YELLOW = (0.84, 0.68, 0.13)
    RED = (0.6, 0.09, 0.07)

    def items(text, poly, rect, circle, trefoil):
        # 경사면 큰 표기
        text("LANCE", 56, 71, 8.5, PALE, 1.2)
        text("BATTERY 01", 56, 63.5, 3.2, PALE, 1.15)
        # 지붕 위험 표지
        for u, v in ((27, 92), (85, 92)):
            circle(6.2, u, v, YELLOW)
            trefoil(5.3, u, v, INK, YELLOW)
        rect(30, 3.4, 56, 96, RED)
        text("REACTOR CORE BELOW", 56, 96, 2.0, PALE)
        text("NO LANDING ZONE", 56, 89, 2.2, INK)
        # 벽: 고전압 경고
        rect(34, 3.2, 56, 9.5, YELLOW)
        text("DANGER  HIGH VOLTAGE", 56, 9.5, 1.9, INK)
        text("ARMOR HINGE AREA  KEEP CLEAR", 56, 5.0, 1.15, PALE)
        for u in (22, 90):
            poly([(-3, 0), (0, 2.8), (3, 0), (0, -2.8)], u, 7.5, YELLOW)
            text("!", u, 7.5, 2.8, INK)

    page = render_marks = render_page(items)
    xx, yy = np.meshgrid((np.arange(n) + 0.5) / n * ATLAS_M, (np.arange(n) + 0.5) / n * ATLAS_M)
    base = np.array([0.36, 0.385, 0.325], dtype=np.float32)
    img = np.tile(base[None, None, :], (n, n, 1))
    img *= 1 + (fbm(n, 5, 41) - 0.5)[..., None] * 0.22 + (fbm(n, 48, 44) - 0.5)[..., None] * 0.1
    # 앞뒤 방향의 판 이음매: 장갑 덧판과 같은 간격
    lines = np.zeros((n, n), dtype=np.float32)
    for u in (16, 37, 73, 94):
        lines = np.maximum(lines, np.exp(-((xx - u) / 0.09) ** 2))
    for v in (16, 30, 58, 82):
        lines = np.maximum(lines, np.exp(-((yy - v) / 0.09) ** 2))
    img *= 1 - lines[..., None] * 0.5
    # 비에 흘러내린 때: 위에서 아래로
    streak = fbm(n, 64, 51)[:, :]
    streak = np.repeat(streak[:n // 4 + 1 :, :], 1, axis=0)
    drip = wrap_noise(n, 10, 61)
    grime = np.clip(np.roll(fbm(n, 12, 71), 300, axis=1) - 0.55, 0, 1)
    img *= 1 - grime[..., None] * 0.5
    a = page[..., 3:4] * 0.9
    img = img * (1 - a) + page[..., :3] * a
    # 표기가 조금 닳아 보이게
    wear = np.clip(fbm(n, 40, 81) - 0.62, 0, 1)[..., None] * 1.5 * a
    img = img * (1 - wear) + base * wear
    return save_png("ACO_Lance_Armor_Albedo", np.clip(img, 0, 1).astype(np.float32))


# ── 포대: 좌표는 프리팹 로컬(Unity 축) ──
YAW_Y = 3.2       # NPC_Turret_Flak의 TurretYaw 높이
DECK_Y = 1.2      # NPC_Turret_VLS 윗면
CELL = 1.2        # 셀 중심 위치
WELL = 1.9        # 셀 구멍 한 변


def ring(r, y, n=16, cx=0.0, cz=0.0, turn=0.0):
    return [(cx + r * math.cos(turn + k * math.tau / n), y, cz + r * math.sin(turn + k * math.tau / n)) for k in range(n)]


def tube_y(s, cx, cz, r, y0, y1, n=8):
    s.loft([ring(r, y0, n, cx, cz), ring(r, y1, n, cx, cz)])


def side_plate(s, sign, x_in, x_out, profile, taper=0.0, y_base=0.0):
    """옆에서 본 윤곽(z, y)을 x 방향으로 민다. taper만큼 위로 갈수록 바깥면이 안쪽으로 눕는다."""
    inner = [(sign * x_in, y, z) for z, y in profile]
    outer = [(sign * (x_out - taper * max(y - y_base, 0.0)), y, z) for z, y in profile]
    s.loft([inner, outer])


# ── Flak: 쌍열 대공포. 받침(고정) + 포탑(방위) + 포신(고각) ──
def flak_base():
    """원점 = 바닥. 고정 받침과 선회 고리. 바닥 아래 치마는 곡면 선체의 틈을 메운다(평평한 바닥에서는 묻혀 안 보인다)."""
    s = Solid()
    s.loft([ring(3.95, -1.7, 16, turn=math.pi / 16), ring(3.75, 0.3, 16, turn=math.pi / 16), ring(3.25, 0.72, 16, turn=math.pi / 16)])
    s.loft([ring(2.95, 0.72, 24), ring(2.95, 1.0, 24)])
    for k in range(8):
        a = (k + 0.5) * math.tau / 8
        c, n = math.cos(a), math.sin(a)
        s.box((c * 3.35, 0.42, n * 3.35), (0.5, 0.26, 0.5))
    return s.mesh("Flak_Base")


HOUSE_FLOOR = 1.02 - YAW_Y     # 포탑 바닥(TurretYaw 기준)
DECK_TOP = HOUSE_FLOOR + 0.55


def flak_house():
    """원점 = TurretYaw. 양쪽 뺨 사이로 포신이 85도까지 선다."""
    s = Solid()
    f, d = HOUSE_FLOOR, DECK_TOP
    # 바닥판: 앞이 뾰족한 육각
    deck = [(-2.9, -3.7), (2.9, -3.7), (3.2, 0.6), (1.9, 3.5), (-1.9, 3.5), (-3.2, 0.6)]
    s.loft([[(x * 0.97, f, z * 0.97) for x, z in deck], [(x, f + 0.14, z) for x, z in deck], [(x, d, z) for x, z in deck]])
    # 뺨: 앞으로 눕고 뒤는 각지게 꺾인다
    cheek = [(-3.55, d), (3.1, d), (2.55, d + 0.65), (1.15, 1.2), (-1.0, 1.55), (-2.9, 1.25), (-3.55, 0.25)]
    for sign in (1, -1):
        side_plate(s, sign, 1.28, 3.0, cheek, 0.17, d)
        # 뺨 바깥 덧판
        plate = [(-2.6, d + 0.35), (1.7, d + 0.35), (0.65, 0.95), (-2.3, 0.95)]
        side_plate(s, sign, 2.5, 3.02, plate, 0.17, d)
        # 뺨 위 환기 살
        for k in range(4):
            z0 = -2.6 + k * 0.42
            z1 = z0 + 0.22

            def top(z):
                return 1.25 + 0.3 * (z + 2.9) / 1.9

            x0, x1 = sign * 1.55, sign * 2.5
            s.loft([[(x0, top(z) - 0.12, z), (x1, top(z) - 0.12, z), (x1, top(z) + 0.07, z), (x0, top(z) + 0.07, z)] for z in (z0, z1)])
        # 옆 탄약 공급 상자
        s.box((sign * 3.05, d + 0.42, -1.45), (0.5, 0.95, 1.6))
    # 뒤 격벽: 요람이 내려오는 자리(반경 2.3)를 비운다
    s.loft([[(x, y, z) for x, y in ((-1.3, d), (1.3, d), (1.3, 0.75), (-1.3, 0.75))] for z in (-3.5, -2.55)])
    s.box((0, 0.35, -3.62), (2.1, 1.0, 0.16))
    for k in range(5):
        s.box((0, -0.05 + k * 0.2, -3.74), (1.85, 0.08, 0.1))
    # 앞 낮은 경사판
    s.loft([[(x, d, 3.3), (-x, d, 3.3), (-x, d + 0.12, 2.5), (x, d + 0.12, 2.5)] for x in (1.3,)] + [[(1.3, d - 0.01, 3.3), (-1.3, d - 0.01, 3.3), (-1.3, d - 0.01, 2.5), (1.3, d - 0.01, 2.5)]])
    # 사격 통제 센서: 오른쪽 뺨 위
    s.box((2.0, 1.72, -1.3), (0.8, 0.4, 1.0))
    s.loft([ring(0.32, 1.9, 10, 2.0, -1.0), ring(0.32, 2.3, 10, 2.0, -1.0), ring(0.14, 2.42, 10, 2.0, -1.0)])
    # 왼쪽 뺨 안테나
    tube_y(s, -2.1, -0.4, 0.05, 1.5, 3.2, 5)
    s.box((-2.1, 1.56, -0.4), (0.34, 0.2, 0.34))
    return s.mesh("Flak_House")


def flak_guns():
    """원점 = 고각 축(TurretPitch). 축에서 바닥판까지 1.6이라 뒤쪽을 그 안으로 줄여 85도까지 선다. 포구는 z 10 부근."""
    s = Solid()
    s.tube_x(0, 0, 0.62, -1.27, 1.27, 12)
    s.loft([[(x, y, z) for x, y in rect8(w, h, c)] for w, h, c, z in ((1.5, 0.8, 0.2, -1.4), (2.3, 1.55, 0.25, -0.9), (2.3, 1.55, 0.25, 1.5), (1.9, 0.95, 0.2, 2.9))])
    s.box((0, 0.86, 0.4), (1.3, 0.16, 1.7))
    for sign in (1, -1):
        x = sign * 0.55
        s.tube_z(x, 0, 0.34, 2.8, 5.0, 10)
        s.tube_z(x, 0, 0.21, 5.0, 8.9, 10)
        for z in (3.3, 4.5):
            s.tube_z(x, 0, 0.39, z, z + 0.18, 10)
        # 포구 제동기
        s.tube_z(x, 0, 0.27, 8.9, 9.95, 10)
        for z in (9.1, 9.5):
            s.tube_z(x, 0, 0.36, z, z + 0.22, 10)
    # 두 포신을 잇는 띠
    for z in (5.4, 7.6):
        s.box((0, 0, z), (1.45, 0.3, 0.26))
    return s.mesh("Flak_Guns")


# ── CIWS: 회전 포신 근접 방어포. 받침(고정) + 포크(방위) + 포(고각) ──
CIWS_YAW_Y = 2.0


def ciws_base():
    """원점 = 바닥."""
    s = Solid()
    s.loft([ring(1.9, -0.9, 12, turn=math.pi / 12), ring(1.8, 0.14, 12, turn=math.pi / 12), ring(1.35, 0.34, 12, turn=math.pi / 12)])
    s.loft([ring(1.0, 0.34, 12), ring(0.9, 0.7, 12)])
    s.loft([ring(1.15, 0.7, 20), ring(1.15, 0.8, 20)])
    return s.mesh("Ciws_Base")


def ciws_mount():
    """원점 = TurretYaw. 고각 축 높이와 같다."""
    s = Solid()
    f = 0.82 - CIWS_YAW_Y
    d = f + 0.3
    plate = [(-1.05, -1.3), (1.05, -1.3), (1.2, -0.2), (0.75, 1.15), (-0.75, 1.15), (-1.2, -0.2)]
    s.loft([[(x * 0.96, f, z * 0.96) for x, z in plate], [(x, f + 0.08, z) for x, z in plate], [(x, d, z) for x, z in plate]])
    block = [(-1.28, d), (1.1, d), (0.85, -0.42), (-1.02, -0.42), (-1.28, -0.68)]
    arm = [(-0.72, -0.42), (0.72, -0.42), (0.44, 0.28), (0.2, 0.44), (-0.2, 0.44), (-0.44, 0.28)]
    for sign in (1, -1):
        side_plate(s, sign, 0.67, 1.12, block, 0.1, d)
        side_plate(s, sign, 0.67, 0.95, arm)
        s.tube_x(0, 0, 0.3, sign * 0.95, sign * 1.05, 12)
    # 뒤 탄약 상자
    s.box((0, -0.68, -1.2), (1.34, 0.58, 0.26))
    return s.mesh("Ciws_Mount")


def ciws_gun():
    """원점 = 고각 축(TurretPitch). 포구는 z 3.4."""
    s = Solid()
    s.tube_x(0, 0, 0.24, -0.66, 0.66, 10)
    s.loft([[(x, y, z) for x, y in rect8(w, h, c)] for w, h, c, z in ((0.8, 0.6, 0.1, -0.78), (1.1, 0.85, 0.12, -0.5), (1.1, 0.85, 0.12, 0.9), (0.7, 0.7, 0.1, 1.3))])
    # 위쪽 평면 레이더
    s.loft([[(x, y, z) for x, y in rect8(w, 0.42, 0.08, 0, 0.62)] for w, z in ((0.62, -0.35), (0.88, 0.0), (0.88, 0.5), (0.6, 0.8))])
    s.box((0, 0.62, 0.82), (0.44, 0.26, 0.05))
    # 회전 포신
    s.tube_z(0, 0, 0.3, 1.2, 1.7, 12)
    for z0, z1 in ((2.4, 2.47), (3.24, 3.32)):
        s.tube_z(0, 0, 0.27, z0, z1, 12)
    for k in range(6):
        a = k * math.tau / 6
        s.tube_z(0.17 * math.cos(a), 0.17 * math.sin(a), 0.052, 1.7, 3.4, 6)
    return s.mesh("Ciws_Gun")


# ── VLS: 네 셀 발사 상자. 받침 + 덮개 넷 ──
def vls_body():
    """원점 = 바닥. 윗면 y 1.2에 구멍 넷."""
    s = Solid()
    outer = rect8(5.5, 5.5, 0.55)
    wall = rect8(5.0, 5.0, 0.4)
    s.loft([[(x, y, z) for x, z in outer] for y in (-1.2, 0.2)])     # 바닥 아래는 곡면 선체의 틈을 메운다
    s.loft([[(x, 0.2, z) for x, z in rect8(5.5, 5.5, 0.55)], [(x, 0.34, z) for x, z in wall], [(x, 0.75, z) for x, z in wall]])
    # 셀 격자
    h = WELL / 2
    edge = CELL + h
    for sign in (1, -1):
        s.box((sign * 2.325, 0.975, 0), (0.35, 0.45, 5.0))
        s.box((0, 0.975, sign * 2.325), (4.3, 0.45, 0.35))
        s.box((sign * 1.2, 0.975, 0), (1.9, 0.45, 0.5))
    s.box((0, 0.975, 0), (0.5, 0.45, 4.3))
    # 셀 안 미사일 머리
    for sx in (1, -1):
        for sz in (1, -1):
            s.loft([ring(0.72, 0.75, 12, sx * CELL, sz * CELL), ring(0.72, 0.9, 12, sx * CELL, sz * CELL), ring(0.3, 1.1, 12, sx * CELL, sz * CELL)])
    # 옆면: 환기 살과 배기 덕트
    for sign in (1, -1):
        for k in range(6):
            s.box((sign * 2.53, 0.42 + k * 0.05 * 0, -1.6 + k * 0.64), (0.08, 0.5, 0.4))
        s.box((0, 0.55, sign * 2.56), (3.6, 0.55, 0.14))
        s.box((sign * 1.2, 0.55, sign * 2.68), (0.5, 0.35, 0.1))
    return s.mesh("Vls_Body")


def vls_hatch():
    """덮개 하나. 원점 = 경첩(셀 바깥쪽 모서리). -x 쪽이 셀 위다."""
    s = Solid()
    w = WELL - 0.06
    s.loft([[(x - w / 2, y, z) for x, z in rect8(w, w, 0.2)] for y in (0.0, 0.1)])
    s.loft([[(x - w / 2, y, z) for x, z in rect8(w * 0.8, w * 0.8, 0.16)] for y in (0.1, 0.17)])
    s.box((-w / 2, 0.195, 0), (w * 0.68, 0.05, 0.14))
    for sign in (1, -1):
        s.box((-w / 2, 0.195, sign * (0.07 + w * 0.15)), (0.14, 0.05, w * 0.3))
    for sign in (1, -1):
        s.tube_z(0.03, 0.05, 0.09, sign * 0.42 - 0.16, sign * 0.42 + 0.16, 8)
    return s.mesh("Vls_Hatch")


def vls_hazard():
    """윗면 둘레 경고띠."""
    s = Solid()
    h = 2.5
    for sign in (1, -1):
        s.box((sign * (h - 0.125), 1.21, 0), (0.25, 0.02, 5.0))
        s.box((0, 1.21, sign * (h - 0.125)), (4.49, 0.02, 0.25))
    return s.mesh("Vls_Hazard")


# ── 공중 요새 코어: 세워 둔 원자로. 원점 = 코어 가운데, 단위 m. 격납고 바닥에서 천장까지 37m ──
def fort_core():
    """빛나는 노심. 위아래가 평평한 원통."""
    s = Solid()
    s.loft([ring(r, y, 24) for r, y in ((11.0, -15.0), (12.0, -13.8), (12.0, 13.8), (11.0, 15.0))])
    return s.mesh("Fort_Core")


def fort_core_frame():
    """살, 고리, 위아래 마개, 바깥 기둥. 살 사이로 노심이 보인다."""
    s = Solid()
    for k in range(10):
        a = k * math.tau / 10
        c, n = math.cos(a), math.sin(a)
        t = (-n, c)
        section = [(12.8, -1.1), (15.8, -1.6), (15.8, 1.6), (12.8, 1.1)]
        s.loft([[(c * r + t[0] * w, y, n * r + t[1] * w) for r, w in section] for y in (-15.5, 15.5)])
    for y in (-8.0, 0.0, 8.0):
        for k in range(24):
            a0, a1 = k * math.tau / 24, (k + 1) * math.tau / 24
            q = [(14.2, a0), (16.6, a0), (16.6, a1), (14.2, a1)]
            s.loft([[(r * math.cos(a_), y_, r * math.sin(a_)) for r, a_ in q] for y_ in (y - 0.9, y + 0.9)])
    for sign in (1, -1):
        rows = [(12.5, 14.8), (17.5, 15.8), (19.5, 16.2), (19.5, 18.0), (17.0, 19.2), (7.0, 19.2)]
        s.loft([ring(r, sign * y, 24) for r, y in rows])
        for k in range(8):
            a = (k + 0.5) * math.tau / 8
            tube_y(s, 17.2 * math.cos(a), 17.2 * math.sin(a), 0.6, sign * 18.8, sign * 19.8, 6)
    # 바깥 기둥과 냉각 관
    for k in range(4):
        a = (k + 0.5) * math.tau / 4
        c, n = math.cos(a), math.sin(a)
        tube_y(s, c * 22.5, n * 22.5, 1.5, -19.2, 19.2, 10)
        for y in (-13.0, 13.0):
            t = (-n, c)
            s.loft([[(c * r + t[0] * w, y + d, n * r + t[1] * w) for w, d in ((-0.9, -0.7), (0.9, -0.7), (0.9, 0.7), (-0.9, 0.7))] for r in (15.5, 22.5)])
    return s.mesh("Fort_CoreFrame")


def export():
    meshes = {
        "Lance_Barrel": (barrel(), TILE), "Lance_Mount": (mount(), TILE), "Lance_ChargeStrip": (charge_strip(), TILE),
        "Lance_Cooler": (cooler(), TILE), "Lance_CoolerHazard": (cooler_hazard(), 12.0), "Lance_BaseDetail": (base_detail(), 60.0),
        "Lance_ReactorCore": (reactor_core(1.0), TILE), "Lance_ReactorFrame": (reactor_frame(), TILE),
        "Lance_MiniBase": (mini_model(), TILE), "Lance_MiniYoke": (mini_yoke(), TILE), "Lance_MiniGun": (mini_gun(), TILE),
    }
    for me, tile in meshes.values():
        box_uv(me, tile)
    clam_l, clam_r, rear_me = clam(-1), clam(1), rear()
    armor_uv(clam_l, -1)
    armor_uv(clam_r, 1)
    rear_uv(rear_me)
    all_meshes = {name: pair[0] for name, pair in meshes.items()}
    all_meshes.update({"Lance_ClamL": clam_l, "Lance_ClamR": clam_r, "Lance_Rear": rear_me})
    # 메시의 Blender 좌표는 (ux, uz, uy)다. FBX는 Unity = (-bx, bz, -by)로 들어가므로 Z축으로 반 바퀴 돌린다
    turn = Matrix.Rotation(math.pi, 4, 'Z')
    scene = bpy.context.scene
    tris = 0
    for name, me in all_meshes.items():
        if name.startswith("Lance_Mini"):
            me.transform(Matrix.Scale(1.0 / MINI_SCALE, 4))
        me.transform(turn)
        me.name = name
        me.update()
        scene.collection.objects.link(bpy.data.objects.new(name, me))
        n = sum(len(f.vertices) - 2 for f in me.polygons)
        tris += n
        print("MESH", name, n)
    print("TRIS", tris)
    os.makedirs(os.path.dirname(FBX), exist_ok=True)
    write_fbx(FBX)


def write_fbx(path):
    bpy.ops.export_scene.fbx(filepath=path, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False, object_types={'MESH'})


def export_turrets():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    meshes = {
        "Flak_Base": (flak_base(), TURRET_TILE), "Flak_House": (flak_house(), TURRET_TILE), "Flak_Guns": (flak_guns(), TURRET_TILE),
        "Ciws_Base": (ciws_base(), TURRET_TILE), "Ciws_Mount": (ciws_mount(), TURRET_TILE), "Ciws_Gun": (ciws_gun(), TURRET_TILE),
        "Vls_Body": (vls_body(), 10.0), "Vls_Hatch": (vls_hatch(), 10.0), "Vls_Hazard": (vls_hazard(), 4.0),
        "Fort_Core": (fort_core(), TILE), "Fort_CoreFrame": (fort_core_frame(), TILE),
    }
    turn = Matrix.Rotation(math.pi, 4, 'Z')
    for name, (me, tile) in meshes.items():
        box_uv(me, tile)
        me.transform(turn)
        me.name = name
        me.update()
        bpy.context.scene.collection.objects.link(bpy.data.objects.new(name, me))
        print("MESH", name, sum(len(f.vertices) - 2 for f in me.polygons))
    write_fbx(TURRET_FBX)


os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
make_steel()
make_armor()
export()
export_turrets()
print("DONE")
