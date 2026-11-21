# -*- coding: utf-8 -*-
"""레이저 탑 보스(6-331)의 곁 유닛 모델 · 텍스처: 반사판 비행선, ECM 차량.
 - ACO_ReflectorAirship.fbx
   · Airship_Hull: 경식 비행선(길이 HULL_L, 반지름 HULL_R — 회전체, 부드럽게) + 곤돌라(창) + 꼬리날개 4장 + 엔진 4기(나셀 · 프로펠러)
     + 반사경 받침대 + 표지등. 원점 = 선체 가운데, 기수 +Z.
   · Airship_Mirror: 반사경(지름 MIRROR_R × 2) + 포크 · 축. 원점 = 곤돌라 밑 관절(MIRROR_PIVOT — 선체 원점 기준), 거울 가운데 = 원점 + (0, -MIRROR_DROP, 0),
     기본 거울 면 = +Z. 반사 방향에 정확히 맞출 필요는 없다(사용자: "잘 안 보일 테니 반사판 각도는 굳이 안 맞춰서 돌려도 돼") — 천천히 돌리기만 한다.
 - ACO_EcmTruck.fbx
   · EcmTruck_Body: 8×8 트럭(길이 약 15m — TEL과 같은 체급) + 운전석(창) + 장비실 + 냉방기 + 지지대 4 + 지붕 접시 안테나 + 접이식 마스트(3단)
     + 꼭대기 재밍 안테나(가로대 · 쌍극 막대 · 판 안테나) + 표지등. 원점 = 지면 가운데, 앞 +Z.
재질(서브메시): Hull(밝은 회색 외피 — 가로 골조 · 세로 이음, 회전체 UV) · Metal(어두운 강철 — 레이저 탑 텍스처를 같이 쓴다) · Window(어두운 유리)
 · Mirror(은빛 거울) · Glow(붉은 표지등) · Paint(국방색 도장 — 판 이음, 월드 평면 UV) · Rubber(타이어).
좌표: 유니티 좌표 그대로(x 오른쪽, y 위, z 앞), 단위 m. 원통 부품은 부드럽게(smooth), 나머지는 평평하게.
실행: blender --background --factory-startup --python Tools/aco_laser_tower_units_model.py -- [미리보기 폴더]"""
import math
import os
import sys
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_LASERTOWER_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "LaserTower"))
METAL_TEX = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "LaserTower", "ACO_LaserTower_Metal.png")
MAT_NAMES = ["Hull", "Metal", "Window", "Mirror", "Glow", "Paint", "Rubber"]
KIND = {k.lower(): i for i, k in enumerate(MAT_NAMES)}
METAL_TILE = 64.0
PAINT_TILE = 8.0

# 비행선
HULL_L, HULL_R = 130.0, 16.0
HULL_PROFILE = [(-65.0, 1.6), (-61.0, 6.0), (-52.0, 10.8), (-38.0, 14.2), (-18.0, 15.8), (4.0, 16.0), (26.0, 15.2), (44.0, 12.6), (56.0, 8.6), (62.0, 4.6), (65.0, 1.2)]
GONDOLA = (-12.0, 20.0, 3.6, -14.6, -21.0)   # z0, z1, 반폭, 위 y, 밑 y
MIRROR_PIVOT = (0.0, -22.6, 4.0)
MIRROR_DROP = 13.5
MIRROR_R = 11.0
# ECM 차량
TRUCK_W = 1.5         # 반폭
WHEEL_Z = (5.5, 3.8, -3.3, -5.0)
WHEEL_R, WHEEL_HW = 0.66, 0.26
MAST_TOP = 22.0


def to_blender(p):
    return (-p[0], -p[2], p[1])


def from_blender(v):
    return (-v[0], v[2], -v[1])


def vsub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def vadd(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def vmul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def vdot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def vnorm(a):
    l = math.sqrt(vdot(a, a))
    return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-9 else (0.0, 1.0, 0.0)


def centroid(pts):
    n = len(pts)
    return (sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n, sum(p[2] for p in pts) / n)


class Mesh:
    """유니티 좌표로 면을 넣는다. origin = 오브젝트 원점(월드). 기본 UV: Metal = 월드 평면 / METAL_TILE, Paint = 월드 평면 / PAINT_TILE, 나머지 한 점."""

    def __init__(self, origin=(0.0, 0.0, 0.0)):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.origin = origin

    def _orient(self, f, want, kind, smooth, uvfn):
        f.normal_update()
        w = to_blender(want)
        if f.normal.x * w[0] + f.normal.y * w[1] + f.normal.z * w[2] < 0:
            f.normal_flip()
        f.material_index = KIND[kind]
        f.smooth = smooth
        n = vnorm(from_blender(f.normal))
        tile = METAL_TILE if kind == "metal" else (PAINT_TILE if kind == "paint" else 0.0)
        for loop in f.loops:
            p = vadd(from_blender(loop.vert.co), self.origin)
            if uvfn is not None:
                loop[self.uv].uv = uvfn(p)
            elif tile > 0.0:
                if abs(n[1]) > 0.7:
                    loop[self.uv].uv = (p[0] / tile, p[2] / tile)
                else:
                    t = vnorm((n[2], 0.0, -n[0]))
                    loop[self.uv].uv = (vdot(p, t) / tile, p[1] / tile)
            else:
                loop[self.uv].uv = (0.5, 0.5)

    def poly(self, pts, kind, want, uvfn=None):
        vs = [self.bm.verts.new(to_blender(vsub(p, self.origin))) for p in pts]
        f = self.bm.faces.new(vs)
        self._orient(f, want, kind, False, uvfn)
        return f

    def loft(self, rings, kind, caps=True, uvfn=None):
        n = len(rings[0])
        cs = [centroid(r) for r in rings]
        for i in range(len(rings) - 1):
            c = vmul(vadd(cs[i], cs[i + 1]), 0.5)
            for k in range(n):
                m = (k + 1) % n
                q = [rings[i][k], rings[i][m], rings[i + 1][m], rings[i + 1][k]]
                self.poly(q, kind, vsub(centroid(q), c), uvfn)
        if caps:
            self.poly(rings[0], kind, vsub(cs[0], cs[1]), uvfn)
            self.poly(rings[-1], kind, vsub(cs[-1], cs[-2]), uvfn)

    def smooth_loft(self, rings, kind, caps=True, uvfn=None):
        n = len(rings[0])
        cs = [centroid(r) for r in rings]
        vs = [[self.bm.verts.new(to_blender(vsub(p, self.origin))) for p in r] for r in rings]
        for i in range(len(rings) - 1):
            c = vmul(vadd(cs[i], cs[i + 1]), 0.5)
            for k in range(n):
                m = (k + 1) % n
                f = self.bm.faces.new([vs[i][k], vs[i][m], vs[i + 1][m], vs[i + 1][k]])
                q = [rings[i][k], rings[i][m], rings[i + 1][m], rings[i + 1][k]]
                self._orient(f, vsub(centroid(q), c), kind, True, uvfn)
        if caps:
            self.poly(rings[0], kind, vsub(cs[0], cs[1]), uvfn)
            self.poly(rings[-1], kind, vsub(cs[-1], cs[-2]), uvfn)

    def obox(self, c, ax, ay, az, kind, uvfn=None):
        def P(sx, sy, sz):
            return vadd(c, vadd(vmul(ax, sx), vadd(vmul(ay, sy), vmul(az, sz))))
        self.loft([[P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)], [P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1)]], kind, uvfn=uvfn)

    def box(self, c, s, kind, uvfn=None):
        self.obox(c, (s[0] / 2, 0, 0), (0, s[1] / 2, 0), (0, 0, s[2] / 2), kind, uvfn)

    def bar(self, a, b, w, kind="metal"):
        d = vsub(b, a)
        ln = math.sqrt(vdot(d, d))
        if ln < 1e-6:
            return
        d = vmul(d, 1.0 / ln)
        up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.95 else (1.0, 0.0, 0.0)
        x = vnorm(vcross(up, d))
        z = vcross(d, x)
        self.obox(vmul(vadd(a, b), 0.5), vmul(x, w / 2), vmul(d, ln / 2), vmul(z, w / 2), kind)

    def cyl(self, a, b, r, kind, n=16, caps=True, r1=None):
        d = vnorm(vsub(b, a))
        up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.95 else (1.0, 0.0, 0.0)
        u = vnorm(vcross(up, d))
        v = vcross(d, u)
        rb = r if r1 is None else r1

        def ring(c, rr):
            return [vadd(c, vadd(vmul(u, rr * math.cos(k * math.tau / n)), vmul(v, rr * math.sin(k * math.tau / n)))) for k in range(n)]

        self.smooth_loft([ring(a, r), ring(b, rb)], kind, caps)

    def prism(self, pts, axis, th, kind):
        """평면 다각형 pts를 axis 방향 두께 th로(가운데 기준) 세운 판."""
        a = vnorm(axis)
        lo = [vsub(p, vmul(a, th / 2)) for p in pts]
        hi = [vadd(p, vmul(a, th / 2)) for p in pts]
        self.loft([lo, hi], kind)

    def finish(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


# ───────────── 비행선 ─────────────

def hull_r(z):
    for (z0, r0), (z1, r1) in zip(HULL_PROFILE, HULL_PROFILE[1:]):
        if z <= z1:
            t = (z - z0) / (z1 - z0)
            return r0 + (r1 - r0) * t
    return HULL_PROFILE[-1][1]


def build_airship_hull():
    m = Mesh()
    n = 32
    # 회전체: 이음(UV u = 0 ↔ 1)은 밑 가운데(곤돌라에 가려진다) — 이음 칸은 정점을 따로 둔다
    rings = []
    for z, r in HULL_PROFILE:
        ring = []
        for k in range(n + 1):
            a = -math.pi / 2 + k * math.tau / n
            ring.append((r * math.cos(a), r * math.sin(a), z))
        rings.append(ring)
    vs = [[m.bm.verts.new(to_blender(p)) for p in rr] for rr in rings]
    z0, z1 = HULL_PROFILE[0][0], HULL_PROFILE[-1][0]
    for i in range(len(rings) - 1):
        for k in range(n):
            f = m.bm.faces.new([vs[i][k], vs[i][k + 1], vs[i + 1][k + 1], vs[i + 1][k]])
            q = [rings[i][k], rings[i][k + 1], rings[i + 1][k + 1], rings[i + 1][k]]
            c = centroid(q)
            m._orient(f, (c[0], c[1], 0.0), "hull", True, None)
            # 회전체 UV: u = 둘레 비율, v = 길이 비율(감김이 뒤집혔어도 정점 자리로 다시 찾는다)
            for loop in f.loops:
                p = from_blender(loop.vert.co)
                ang = math.atan2(p[1], p[0]) + math.pi / 2
                u = (ang % math.tau) / math.tau
                if u < 0.02 and (k >= n - 1):
                    u = 1.0
                loop[m.uv].uv = (u, (p[2] - z0) / (z1 - z0))
    m.poly(rings[0][:n], "hull", (0, 0, -1), lambda p: (0.5, 0.0))
    m.poly(rings[-1][:n], "hull", (0, 0, 1), lambda p: (0.5, 1.0))
    # 곤돌라(앞이 비스듬한 상자, 앞 · 옆 창 띠)
    gz0, gz1, gw, gy0, gy1 = GONDOLA
    sec = lambda z, w, ytop, ybot: [(-w, ybot, z), (w, ybot, z), (w, ytop, z), (-w, ytop, z)]
    m.loft([sec(gz0, gw * 0.8, gy0, gy1 + 1.0), sec(gz0 + 3.0, gw, gy0, gy1), sec(gz1 - 4.0, gw, gy0, gy1), sec(gz1, gw * 0.85, gy0, gy1 + 2.0)], "metal")
    for s in (-1, 1):
        for zc in [gz0 + 5.0 + 3.2 * k for k in range(8)]:
            m.box((s * (gw + 0.06), gy1 + 3.4, zc), (0.12, 1.4, 2.2), "window")
    m.obox((0.0, gy1 + 3.0, gz1 - 1.0), (gw * 0.75, 0, 0), (0, 0.9, 0), (0, 0, 0.12), "window")
    # 반사경 받침대(곤돌라 밑 → 관절)
    px, py, pz = MIRROR_PIVOT
    m.cyl((px, gy1 + 0.2, pz), (px, py + 0.6, pz), 1.4, "metal", 12)
    m.cyl((px, py + 0.8, pz), (px, py - 0.2, pz), 2.2, "metal", 16)
    # 꼬리날개 4장(십자, 뒤로 젖혀짐)
    for ang in (0.0, math.pi / 2, math.pi, -math.pi / 2):
        d = (math.cos(ang), math.sin(ang), 0.0)
        root0, root1 = -58.0, -40.0
        tip0, tip1 = -64.0, -54.0
        span = 13.0 if abs(d[1]) > 0.5 else 11.0
        pts = [vadd(vmul(d, hull_r(root0) - 0.5), (0, 0, root0)), vadd(vmul(d, hull_r(root1) - 0.5), (0, 0, root1)),
               vadd(vmul(d, hull_r(tip1) + span), (0, 0, tip1)), vadd(vmul(d, hull_r(tip0) + span), (0, 0, tip0))]
        side = vcross(d, (0.0, 0.0, 1.0))
        m.prism(pts, side, 0.8, "hull")
        # 방향타(날개 뒤쪽 어두운 판)
        m.prism([pts[0], pts[3], vadd(vmul(d, hull_r(-62.0) + span * 0.98), (0, 0, -66.0)), vadd(vmul(d, hull_r(-60.5)), (0, 0, -61.0))], side, 0.6, "metal")
        m.box(vadd(vmul(d, hull_r(tip0) + span + 0.3), (0, 0, tip0 + 1.5)), (0.6, 0.6, 0.6), "glow")
    # 엔진 4기(짧은 받침 + 나셀 + 프로펠러)
    for zc in (-10.0, 22.0):
        for s in (-1, 1):
            ex, ey = s * (hull_r(zc) + 4.2), -6.5
            m.bar((s * (hull_r(zc) - 1.0), -4.5, zc), (ex, ey, zc), 1.0)
            m.cyl((ex, ey, zc + 4.5), (ex, ey, zc - 4.0), 2.2, "metal", 16)
            m.cyl((ex, ey, zc + 4.5), (ex, ey, zc + 6.0), 2.2, "metal", 16, r1=0.8)
            m.cyl((ex, ey, zc - 4.0), (ex, ey, zc - 5.0), 1.0, "metal", 10)
            for b in range(4):
                a = b * math.pi / 2 + math.pi / 4
                tip = (ex + 3.8 * math.cos(a), ey + 3.8 * math.sin(a), zc - 4.7)
                m.bar((ex, ey, zc - 4.7), tip, 0.5)
    # 표지등(기수 · 등)
    m.box((0.0, 0.0, 65.4), (0.8, 0.8, 0.8), "glow")
    m.box((0.0, hull_r(4.0) + 0.3, 4.0), (0.7, 0.7, 0.7), "glow")
    return m.finish("Airship_Hull")


def build_airship_mirror():
    o = MIRROR_PIVOT
    m = Mesh(o)
    px, py, pz = o
    c = (px, py - MIRROR_DROP, pz)
    n = 32
    R = MIRROR_R
    # 포크: 관절에서 양옆으로 벌어져 거울 축까지
    for s in (-1, 1):
        m.bar((px, py - 0.5, pz), (px + s * (R + 1.2), py - 4.0, pz), 1.0)
        m.bar((px + s * (R + 1.2), py - 4.0, pz), (px + s * (R + 1.2), c[1], pz), 1.0)
        m.cyl((px + s * (R + 1.7), c[1], pz), (px + s * (R - 0.3), c[1], pz), 0.9, "metal", 12)
    m.cyl((px, py + 0.1, pz), (px, py - 1.2, pz), 1.6, "metal", 16)

    def ring(r, dz):
        return [(c[0] + r * math.cos(k * math.tau / n), c[1] + r * math.sin(k * math.tau / n), c[2] + dz) for k in range(n)]

    # 거울 면(앞 +Z, 평평) + 테 + 뒤판(얕은 원뿔) + 뒤 갈비 4
    front = ring(R - 0.6, 0.35)
    m.poly(front, "mirror", (0, 0, 1), lambda p: ((p[0] - c[0]) / (2 * R) + 0.5, (p[1] - c[1]) / (2 * R) + 0.5))
    m.smooth_loft([ring(R - 0.6, 0.35), ring(R, 0.35), ring(R, -0.5), ring(R * 0.85, -1.4), ring(R * 0.3, -2.6)], "metal", caps=False)
    m.poly(ring(R * 0.3, -2.6), "metal", (0, 0, -1))
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        m.bar((c[0] + 3.0 * math.cos(a), c[1] + 3.0 * math.sin(a), c[2] - 2.4), (c[0] + (R - 0.5) * math.cos(a), c[1] + (R - 0.5) * math.sin(a), c[2] - 0.8), 0.6)
    return m.finish("Airship_Mirror"), o


# ───────────── ECM 차량 ─────────────

def build_ecm_truck():
    m = Mesh()
    W = TRUCK_W
    # 차대 · 앞 범퍼
    m.box((0.0, 1.15, 0.0), (W * 1.6, 0.5, 14.6), "metal")
    m.box((0.0, 0.95, 7.45), (W * 2.0, 0.6, 0.4), "metal")
    # 바퀴 8(타이어 부드럽게 + 휠 캡) + 흙받이
    for z in WHEEL_Z:
        for s in (-1, 1):
            x0, x1 = s * (W - 0.05), s * (W - 0.05 + WHEEL_HW * 2)
            m.cyl((x0, WHEEL_R, z), (x1, WHEEL_R, z), WHEEL_R, "rubber", 16)
            m.cyl((x1, WHEEL_R, z), (x1 + s * 0.04, WHEEL_R, z), WHEEL_R * 0.55, "metal", 12)
        m.box((0.0, WHEEL_R * 2 + 0.12, z), (W * 2 + 0.5, 0.12, WHEEL_R * 2.4), "paint")
    # 운전석: 뒤는 곧고 앞은 비스듬한 유리
    cab = lambda z, top, bot, w: [(-w, bot, z), (w, bot, z), (w, top, z), (-w, top, z)]
    m.loft([cab(4.4, 3.9, 1.4, W), cab(6.4, 3.9, 1.4, W), cab(7.3, 3.0, 1.4, W * 0.97)], "paint")
    # 앞 유리: 비스듬한 앞면(z 6.4 · y 3.9 → z 7.3 · y 3.0) 위에 살짝 띄운 판
    nn = vnorm((0.0, 0.9, 0.9))
    lo, hi = (3.12, 7.18), (3.80, 6.50)
    glass = [(-W * 0.82, lo[0], lo[1]), (W * 0.82, lo[0], lo[1]), (W * 0.82, hi[0], hi[1]), (-W * 0.82, hi[0], hi[1])]
    m.prism([vadd(p, vmul(nn, 0.05)) for p in glass], nn, 0.06, "window")
    for s in (-1, 1):
        m.box((s * (W + 0.03), 3.1, 5.6), (0.06, 0.9, 1.4), "window")
        m.box((s * (W + 0.25), 3.2, 6.8), (0.1, 0.5, 0.15), "metal")    # 거울
    # 장비실(지붕 가장자리 턱 · 옆 냉방기 · 뒤 문)
    m.box((0.0, 3.0, -1.6), (W * 2 + 0.1, 3.2, 11.2), "paint")
    m.box((0.0, 4.68, -1.6), (W * 2 + 0.3, 0.16, 11.4), "paint")
    for s in (-1, 1):
        m.box((s * (W + 0.25), 2.9, -0.5), (0.5, 1.0, 1.6), "metal")
        m.box((s * (W + 0.25), 2.9, -4.2), (0.5, 1.0, 1.6), "metal")
        m.box((s * (W + 0.06), 2.6, 2.4), (0.06, 2.0, 0.9), "metal")     # 옆 문
    m.box((0.0, 2.8, -7.24), (1.3, 2.2, 0.06), "metal")
    # 지지대 4(비스듬한 다리 + 발판)
    for zc in (3.0, -6.6):
        for s in (-1, 1):
            a = (s * W, 1.4, zc)
            b = (s * (W + 1.6), 0.15, zc)
            m.bar(a, b, 0.35)
            m.box((b[0], 0.08, zc), (0.9, 0.16, 0.9), "metal")
    # 지붕 접시 안테나(앞쪽, 하늘 · 앞을 본다)
    dc = (0.0, 6.0, 1.6)
    axis = vnorm((0.0, 0.8, 0.6))
    m.cyl((0.0, 4.76, 1.6), (0.0, 5.6, 1.6), 0.25, "metal", 8)
    up = (1.0, 0.0, 0.0)
    u = vnorm(vcross(up, axis))
    v = vcross(axis, u)
    rings = []
    for f, back in ((1.0, 0.0), (0.6, 0.35), (0.2, 0.55)):
        cc = vsub(dc, vmul(axis, back))
        rings.append([vadd(cc, vadd(vmul(u, 1.25 * f * math.cos(k * math.tau / 20)), vmul(v, 1.25 * f * math.sin(k * math.tau / 20)))) for k in range(20)])
    vs = [[m.bm.verts.new(to_blender(p)) for p in rr] for rr in rings]
    for i in range(2):
        for k in range(20):
            kk = (k + 1) % 20
            fce = m.bm.faces.new([vs[i][k], vs[i][kk], vs[i + 1][kk], vs[i + 1][k]])
            m._orient(fce, axis, "metal", True, None)
    m.poly(rings[-1], "metal", axis)
    back = [[vsub(p, vmul(axis, 0.12)) for p in rr] for rr in rings]
    m.smooth_loft(back, "metal", caps=False)
    m.cyl(vsub(dc, vmul(axis, 0.5)), vadd(dc, vmul(axis, 0.8)), 0.06, "metal", 6)
    # 접이식 마스트(장비실 뒤쪽 지붕) 3단 + 마디 테
    mx, mz = 0.0, -5.4
    m.box((mx, 4.95, mz), (1.4, 0.4, 1.4), "metal")
    segs = [(5.1, 10.8, 0.42), (10.6, 16.4, 0.32), (16.2, MAST_TOP, 0.22)]
    for y0, y1, r in segs:
        m.cyl((mx, y0, mz), (mx, y1, mz), r, "metal", 12)
        m.cyl((mx, y1 - 0.3, mz), (mx, y1, mz), r + 0.12, "metal", 12)
    # 꼭대기 재밍 안테나: 가로대 둘(십자) + 쌍극 막대 + 판 안테나 셋 + 표지등
    top = MAST_TOP
    for ax in ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)):
        m.bar(vsub((mx, top - 0.6, mz), vmul(ax, 2.4)), vadd((mx, top - 0.6, mz), vmul(ax, 2.4)), 0.16)
        for s in (-1, 1):
            p = vadd((mx, top - 0.6, mz), vmul(ax, s * 2.3))
            m.cyl((p[0], p[1] - 1.2, p[2]), (p[0], p[1] + 1.2, p[2]), 0.06, "metal", 6)
    for k in range(3):
        a = k * math.tau / 3
        d = (math.sin(a), 0.0, math.cos(a))
        cc = vadd((mx, top - 2.2, mz), vmul(d, 0.55))
        m.obox(cc, vmul((d[2], 0.0, -d[0]), 0.35), (0.0, 0.7, 0.0), vmul(d, 0.08), "paint")
    m.cyl((mx, top, mz), (mx, top + 1.6, mz), 0.05, "metal", 6)
    m.box((mx, top + 1.7, mz), (0.22, 0.22, 0.22), "glow")
    return m.finish("EcmTruck_Body")


# ───────────── 텍스처 · 재질 ─────────────

def save_image(name, arr):
    h, w = arr.shape[:2]
    im = bpy.data.images.new(name, w, h, alpha=False)
    im.pixels.foreach_set(arr.ravel())
    im.filepath_raw = os.path.join(OUT, name + ".png")
    im.file_format = 'PNG'
    im.save()
    return im


def solid(name, col):
    img = np.ones((8, 8, 4), dtype=np.float32)
    img[..., :3] = col
    return save_image(name, img)


def hull_texture():
    """비행선 외피(u = 둘레, v = 기수 쪽으로 길이): 밝은 회색, 세로 이음 24줄 · 가로 골조 마디(약 5m마다) 위 밝은 날 · 아래 얼룩,
    밑(u 0 · 1 근처)은 기름때로 조금 어둡게, 위쪽 등줄기에 짙은 띠(식별 띠)."""
    W, H = 1024, 2048
    rng = np.random.default_rng(3)
    ys, xs = np.mgrid[0:H, 0:W]
    u = xs / W
    v = ys / H
    base = np.array([0.62, 0.63, 0.62], dtype=np.float32)
    shade = np.ones((H, W), dtype=np.float32)
    gore = (np.abs(((u * 24) % 1.0) - 0.5) > 0.485)
    frame_n = 26
    fv = (v * frame_n) % 1.0
    frame = fv < 0.012
    lip = (fv >= 0.012) & (fv < 0.03)
    shade = np.where(gore, shade * 0.88, shade)
    shade = np.where(frame, shade * 0.78, np.where(lip, shade * 1.06, shade))
    bottom = np.minimum(u, 1.0 - u)
    shade = shade * (0.82 + 0.18 * np.clip(bottom / 0.25, 0.0, 1.0))
    stripe = (np.abs(u - 0.5) < 0.012)
    shade = np.where(stripe, shade * 0.45, shade)
    shade = shade * (1.0 + 0.03 * rng.normal(0.0, 1.0, size=(H, W)).astype(np.float32))
    img = np.ones((H, W, 4), dtype=np.float32)
    img[..., :3] = np.clip(base[None, None, :] * shade[..., None], 0, 1)
    return save_image("ACO_ReflectorAirship_Hull", img)


def paint_texture():
    """국방색 도장(8m 한 장, 반복): 2m 판 이음, 판마다 밝기 차이, 아래로 흙먼지, 고운 잡티."""
    S = 1024
    rng = np.random.default_rng(5)
    ys, xs = np.mgrid[0:S, 0:S]
    n = 4                        # 판 2m
    cell = S // n
    pan = rng.normal(1.0, 0.04, size=(n, n)).astype(np.float32)
    shade = pan[ys // cell, xs // cell]
    seam = ((ys % cell) < 2) | ((xs % cell) < 2)
    shade = np.where(seam, shade * 0.86, shade)
    dust = np.clip(1.0 - ys / (S * 0.35), 0.0, 1.0)
    base = np.array([0.30, 0.33, 0.22], dtype=np.float32)
    dirt = np.array([0.36, 0.32, 0.25], dtype=np.float32)
    col = base[None, None, :] * (1 - 0.5 * dust[..., None]) + dirt[None, None, :] * 0.5 * dust[..., None]
    col = col * shade[..., None] * (1.0 + 0.04 * rng.normal(0.0, 1.0, size=(S, S, 1)).astype(np.float32))
    img = np.ones((S, S, 4), dtype=np.float32)
    img[..., :3] = np.clip(col, 0, 1)
    return save_image("ACO_EcmTruck_Paint", img)


def materials(images):
    mats = []
    for nm, im in zip(MAT_NAMES, images):
        mat = bpy.data.materials.new("ACO_LaserTowerUnits_" + nm)
        mat.use_nodes = True
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = im
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        mats.append(mat)
    return mats


def link(me, mats, loc=(0.0, 0.0, 0.0)):
    for mat in mats:
        me.materials.append(mat)
    o = bpy.data.objects.new(me.name, me)
    o.location = to_blender(loc)
    bpy.context.scene.collection.objects.link(o)
    return o


def write_fbx(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def preview(folder, airship, truck):
    import mathutils
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_cavity = True
    sc.display.shading.cavity_type = 'BOTH'
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.4
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.42, 0.5, 0.6)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 40
    cam.data.clip_start = 0.1
    cam.data.clip_end = 5000.0

    def shoot(name, show, eye_u, at_u):
        for o in bpy.data.objects:
            if o.type == 'MESH':
                o.hide_render = o.name not in show
        eye = mathutils.Vector(to_blender(eye_u))
        at = mathutils.Vector(to_blender(at_u))
        cam.location = eye
        cam.rotation_euler = (at - eye).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    a = [o.name for o in airship]
    shoot("airship_side", a, (190.0, -10.0, 30.0), (0.0, -10.0, 0.0))
    shoot("airship_front34", a, (130.0, 25.0, 150.0), (0.0, -12.0, 0.0))
    shoot("airship_below", a, (90.0, -110.0, -70.0), (0.0, -20.0, 0.0))
    shoot("airship_mirror", a, (40.0, -32.0, 45.0), (0.0, -34.0, 4.0))
    # 땅(차량 미리보기)
    g = Mesh()
    g.poly([(-60, 0.0, -60), (60, 0.0, -60), (60, 0.0, 60), (-60, 0.0, 60)], "paint", (0, 1, 0), lambda p: (0.5, 0.5))
    gme = g.finish("ground")
    gm = bpy.data.materials.new("groundmat")
    gm.diffuse_color = (0.3, 0.33, 0.27, 1)
    gme.materials.append(gm)
    go = bpy.data.objects.new("ground", gme)
    sc.collection.objects.link(go)
    t = [o.name for o in truck] + ["ground"]
    shoot("ecm_front34", t, (14.0, 7.0, 18.0), (0.0, 4.0, 0.0))
    shoot("ecm_rear34", t, (-15.0, 9.0, -17.0), (0.0, 6.0, 0.0))
    shoot("ecm_far", t, (45.0, 22.0, 50.0), (0.0, 8.0, 0.0))


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    metal = bpy.data.images.load(METAL_TEX) if os.path.exists(METAL_TEX) else solid("ACO_LaserTowerUnits_MetalFallback", (0.3, 0.31, 0.32))
    imgs = [hull_texture(), metal, solid("ACO_LaserTowerUnits_Window", (0.07, 0.08, 0.10)), solid("ACO_ReflectorAirship_Mirror", (0.78, 0.86, 0.92)),
            solid("ACO_LaserTowerUnits_Glow", (1.0, 0.18, 0.12)), paint_texture(), solid("ACO_EcmTruck_Rubber", (0.06, 0.06, 0.06))]
    mats = materials(imgs)
    hull = link(build_airship_hull(), mats)
    me, o = build_airship_mirror()
    mirror = link(me, mats, o)
    truck = link(build_ecm_truck(), mats)
    for ob in (hull, mirror, truck):
        print("TRIS", ob.name, sum(len(p.vertices) - 2 for p in ob.data.polygons))
    print("PIVOTS mirror", MIRROR_PIVOT, "mirror center", (MIRROR_PIVOT[0], MIRROR_PIVOT[1] - MIRROR_DROP, MIRROR_PIVOT[2]))
    write_fbx(os.path.join(OUT, "ACO_ReflectorAirship.fbx"), [hull, mirror])
    write_fbx(os.path.join(OUT, "ACO_EcmTruck.fbx"), [truck])
    if args:
        preview(args[0], [hull, mirror], [truck])
    print("DONE")
