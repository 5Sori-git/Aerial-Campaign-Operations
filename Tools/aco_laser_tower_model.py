# -*- coding: utf-8 -*-
"""광학 병기 탑(레이저 탑) 모델 · 텍스처 생성기 — 멀리서 보는 거대 구조물(세부 일부는 텍스처).
모양(2026-11-20 사용자 — 마지막: "폴리곤 더 써도 되니 텍스처랑 디테일 손봐 줘, 셰이드 스무스도 필요한 곳은"):
 원통 부품(렌즈 통 · 회전대 · 배관 · 탱크 · 접시 안테나 · 도관 · 축)은 정점을 공유해 부드럽게(smooth), 각진 콘크리트 · 판은 평평하게.
 텍스처: 콘크리트 · 강철 각각 바탕색 + 노멀맵(_Normal — 이음줄 · 타이 구멍 · 리벳 · 타설 줄). 미리보기(워크벤치)는 노멀맵을 안 쓴다.
규모(2026-11-20 사용자: "랜스 포대랑 같은 규모로 — 그만큼 넓게 배치하고 높이도 그만큼, 대신 각 탑의 너비는 키우지 마"):
 랜스 기초 반경 530m에 맞춰 보조탑 반경 215 → 490m(단지 바깥 끝 약 517m), 높이 약 2.2배(코어 꼭대기 360 → 792m, 보조탑 200 → 440m).
 단면 너비(PROFILE의 x · z, 보조탑 반폭)는 그대로 — 더 가늘고 길다. 코어 · 외피 단은 수를 늘려 한 단 높이를 비슷하게.
(이전 요청: "코어 줄기에 외피가 붙은 느낌, 골격이 일부 드러난 모습" → "400m 크기치곤 디테일이 떨어진다, 외부는 트러스라도
내부는 단단해 보이게, 넙데데하니 더 날카롭게, 보조탑 200m로 훨씬 거대하게, 환기구는 훨씬 작게" → "좀 더 콘크리트 느낌 나게, 너무 외계 느낌이라"):
 콘크리트(거푸집 무늬 · 빗물 자국) 덩어리에 강철 트러스 · 기계를 붙인 건축물. 빛은 렌즈와 환풍구에만(코어 이음은 어두운 강철 띠).
 - LaserTower_Base: 각진 기단 + 단단한 코어(양 끝이 뾰족한 마름모꼴 육각 단면, 단마다 끊어 쌓고 단 사이 청백색 빛 띠, 위로 갈수록 가늘어짐)
   + 코어 둘레 트러스(모서리 기둥 · 가로 링 · 드러난 칸 X 버팀 · 코어로 가는 짧은 버팀) + 트러스 바깥 외피판(칸마다, 일부는 뾰족한 윗끝,
   위로 갈수록 많이 비어 트러스와 코어가 드러난다) + 양 끝 모서리 날(뾰족한 지느러미) + 밑동 버팀 날 + 코어 돌출물 + 가로 안테나 팔.
   외피에 가려지는 트러스는 만들지 않는다.
 - LaserTower_Head: 꼭대기 회전대 + 두 팔(요크) + 팔 위 가는 안테나 두 대. 원점 = 좌우 회전축(0, HEAD_Y, 0).
 - LaserTower_Lens: 팔각 렌즈 통 + 앞 둥근 렌즈(청록, 빛남) + 옆 각진 판 + 축 돌기. 원점 = 위아래 회전축(0, LENS_Y, 0), 기본 방향 +Z.
 - LaserTower_Aux1..6: 발전 보조탑(반지름 AUX_R에 60°마다) — 높이 AUX_H의 가는 사다리꼴 오벨리스크(사각뿔대) + 가로 빛 띠. 원점 = 탑 밑.
 - LaserTower_Radiator1..6: 보조탑 끝 작은 환풍구 — 안쪽이 달아오르고(열 그라데이션) 네 면에 가로 살, 위 덮개. 원점 = 오벨리스크 윗면 가운데.
 - LaserTower_Conduits: 보조탑 밑에서 기단까지 땅 위 사각 덕트 6개(위에 빛줄, 받침).
재질(서브메시): Metal(어두운 강철 판 무늬 — 트러스 · 기계 · 이음띠) · Glow(청백색 빛 — 지금은 안 씀) · Lens(청록 빛) · Radiator(열 그라데이션)
 · Concrete(밝은 회색 콘크리트 — 거푸집 판 · 타이 구멍 · 타설 줄 · 빗물 자국, 월드 TILE m마다 반복) · Window(어두운 유리 — 창 · 출입문).
좌표: 유니티 좌표 그대로(x 오른쪽, y 위, z 앞), 단위 m, 원점 = 주탑 바닥 가운데. 외피 · 돌출물 배치는 고정 시드라 다시 돌려도 같다.
실행: blender --background --factory-startup --python Tools/aco_laser_tower_model.py -- [미리보기 폴더] [샷 이름...]"""
import math
import os
import random
import sys
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_LASERTOWER_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "LaserTower"))
NAME = "ACO_LaserTower"
TILE = 64.0          # 쇳빛 판 텍스처 한 장 = 64m
MAT_NAMES = ["Metal", "Glow", "Lens", "Radiator", "Concrete", "Window"]
KIND = {"metal": 0, "glow": 1, "lens": 2, "radiator": 3, "concrete": 4, "window": 5}
SEED = 5

# 주탑 — 코어 단면 반폭(높이, x 끝(뾰족), z 면) 구간 선형
PROFILE = [(0.0, 22.0, 13.0), (264.0, 17.5, 10.0), (550.0, 12.5, 7.2), (704.0, 9.5, 5.4), (792.0, 6.5, 4.0)]
CORE_LEVELS = [round(792.0 * k / 22, 1) for k in range(23)]   # 단 22개(한 단 36m)
TRUSS_OUT = 3.0          # 트러스 = 코어 밖 3m
SKIN_OUT = 3.8           # 외피 안쪽 면 = 코어 밖 3.8m
SKIN_LEVELS = [round(722.0 * k / 14, 1) for k in range(15)]   # 외피 칸 14개(한 칸 약 52m), 722m 위는 트러스만
RING_STEP = 9.0
HEAD_Y = 792.0
LENS_Y = 806.0
# 보조탑
AUX_R = 490.0
AUX_N = 6
AUX_H = 440.0
AUX_B0, AUX_B1 = 13.0, 4.2             # 사다리꼴 반폭 — 밑 · 위
VENT_H = 5.0


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


def vlerp(a, b, t):
    return vadd(a, vmul(vsub(b, a), t))


def centroid(pts):
    n = len(pts)
    return (sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n, sum(p[2] for p in pts) / n)


class Mesh:
    """유니티 좌표로 면을 넣는다. origin = 오브젝트 원점(월드) — 정점은 원점 기준으로 저장하고, 쇳빛 판 UV는 월드 자리로 잡는다."""

    def __init__(self, origin=(0.0, 0.0, 0.0)):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.origin = origin
        self.uv_shift = (0.0, 0.0)

    def poly(self, pts, kind, want, uvfn=None):
        """pts = 월드(유니티) 좌표. want = 면이 바라볼 방향(유니티) — 블렌더로 옮기면 거울이라 감김 대신 방향으로 맞춘다."""
        vs = [self.bm.verts.new(to_blender(vsub(p, self.origin))) for p in pts]
        f = self.bm.faces.new(vs)
        f.normal_update()
        w = to_blender(want)
        if f.normal.x * w[0] + f.normal.y * w[1] + f.normal.z * w[2] < 0:
            f.normal_flip()
        f.material_index = KIND[kind]
        self._uv(f, kind, uvfn)
        return f

    def _uv(self, f, kind, uvfn):
        n = vnorm(from_blender(f.normal))
        su, sv = self.uv_shift
        for loop in f.loops:
            p = vadd(from_blender(loop.vert.co), self.origin)
            if uvfn is not None:
                loop[self.uv].uv = uvfn(p)
            elif kind in ("metal", "concrete"):
                if abs(n[1]) > 0.7:
                    loop[self.uv].uv = (p[0] / TILE + su, p[2] / TILE + sv)
                else:
                    t = vnorm((n[2], 0.0, -n[0]))
                    loop[self.uv].uv = (vdot(p, t) / TILE + su, p[1] / TILE + sv)
            else:
                loop[self.uv].uv = (0.5, 0.5)

    def smooth_loft(self, rings, kind, caps=True, uvfn=None, inward=False):
        """부드러운 셰이딩 입체: 이웃 면이 고리 정점을 같이 써서 옆면이 둥글게 보인다(smooth). 뚜껑은 평평(따로 정점).
        inward = 면이 축 쪽을 본다(안쪽 테두리)."""
        n = len(rings[0])
        cs = [centroid(r) for r in rings]
        vs = [[self.bm.verts.new(to_blender(vsub(p, self.origin))) for p in r] for r in rings]
        for i in range(len(rings) - 1):
            c = vmul(vadd(cs[i], cs[i + 1]), 0.5)
            for k in range(n):
                mm = (k + 1) % n
                f = self.bm.faces.new([vs[i][k], vs[i][mm], vs[i + 1][mm], vs[i + 1][k]])
                f.normal_update()
                q = [rings[i][k], rings[i][mm], rings[i + 1][mm], rings[i + 1][k]]
                want = vsub(centroid(q), c)
                if inward:
                    want = vmul(want, -1.0)
                w = to_blender(want)
                if f.normal.x * w[0] + f.normal.y * w[1] + f.normal.z * w[2] < 0:
                    f.normal_flip()
                f.smooth = True
                f.material_index = KIND[kind]
                self._uv(f, kind, uvfn)
        if caps:
            self.poly(rings[0], kind, vsub(cs[0], cs[1]), uvfn)
            self.poly(rings[-1], kind, vsub(cs[-1], cs[-2]), uvfn)

    def cyl(self, a, b, r, kind, n=16, caps=True, r1=None):
        """a → b 원통(부드럽게). r1 = 끝 반지름(원뿔대)."""
        d = vnorm(vsub(b, a))
        up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.95 else (1.0, 0.0, 0.0)
        u = vnorm(vcross(up, d))
        v = vcross(d, u)
        rb = r if r1 is None else r1

        def ring(c, rr):
            return [vadd(c, vadd(vmul(u, rr * math.cos(k * math.tau / n)), vmul(v, rr * math.sin(k * math.tau / n)))) for k in range(n)]

        self.smooth_loft([ring(a, r), ring(b, rb)], kind, caps)

    def tube(self, pts, r, kind, n=12):
        """점들을 잇는 관(부드럽게) — 고리는 앞뒤 마디 방향의 평균에 수직."""
        rings = []
        for i, p in enumerate(pts):
            d0 = vsub(p, pts[i - 1]) if i > 0 else vsub(pts[1], p)
            d1 = vsub(pts[i + 1], p) if i < len(pts) - 1 else d0
            d = vnorm(vadd(vnorm(d0), vnorm(d1)))
            up = (1.0, 0.0, 0.0) if abs(d[0]) < 0.9 else (0.0, 0.0, 1.0)
            u = vnorm(vcross(up, d))
            v = vcross(d, u)
            rings.append([vadd(p, vadd(vmul(u, r * math.cos(k * math.tau / n)), vmul(v, r * math.sin(k * math.tau / n)))) for k in range(n)])
        self.smooth_loft(rings, kind, True)

    def dish(self, c, axis, r, depth, kind, n=20):
        """접시 안테나(부드럽게): 테두리 c, 안쪽(오목한 면)이 axis 쪽을 본다. 뒤판 + 가운데 받침 + 수신기 막대."""
        axis = vnorm(axis)
        up = (0.0, 1.0, 0.0) if abs(axis[1]) < 0.95 else (1.0, 0.0, 0.0)
        u = vnorm(vcross(up, axis))
        v = vcross(axis, u)
        fr = [1.0, 0.75, 0.45, 0.15]

        def ring(rr, back):
            cc = vsub(c, vmul(axis, back))
            return [vadd(cc, vadd(vmul(u, rr * math.cos(k * math.tau / n)), vmul(v, rr * math.sin(k * math.tau / n)))) for k in range(n)]

        front = [ring(r * f, depth * (1.0 - f * f)) for f in fr]
        back = [ring(r * f, depth * (1.0 - f * f) + 0.35) for f in fr]
        for rings, want in ((front, axis), (back, vmul(axis, -1.0))):
            vs = [[self.bm.verts.new(to_blender(vsub(p, self.origin))) for p in rr] for rr in rings]
            for i in range(len(rings) - 1):
                for k in range(n):
                    mm = (k + 1) % n
                    f = self.bm.faces.new([vs[i][k], vs[i][mm], vs[i + 1][mm], vs[i + 1][k]])
                    f.normal_update()
                    w = to_blender(want)
                    if f.normal.x * w[0] + f.normal.y * w[1] + f.normal.z * w[2] < 0:
                        f.normal_flip()
                    f.smooth = True
                    f.material_index = KIND[kind]
                    self._uv(f, kind, None)
            self.poly(rings[-1], kind, want)
        # 테두리 두께
        for k in range(n):
            mm = (k + 1) % n
            self.poly([front[0][k], front[0][mm], back[0][mm], back[0][k]], kind, vsub(front[0][k], c))
        hub = vsub(c, vmul(axis, depth + 0.35))
        self.cyl(hub, vsub(hub, vmul(axis, 1.2)), r * 0.18, kind, 10)
        self.cyl(vsub(c, vmul(axis, depth * 0.9)), vadd(c, vmul(axis, r * 0.55)), 0.12, kind, 6)

    def annulus(self, center, axis, r0, r1, kind, n=24):
        """평평한 고리 판(축 방향을 본다)."""
        axis = vnorm(axis)
        up = (0.0, 1.0, 0.0) if abs(axis[1]) < 0.95 else (1.0, 0.0, 0.0)
        u = vnorm(vcross(up, axis))
        v = vcross(axis, u)
        for k in range(n):
            a0, a1 = k * math.tau / n, (k + 1) * math.tau / n
            P = lambda rr, a: vadd(center, vadd(vmul(u, rr * math.cos(a)), vmul(v, rr * math.sin(a))))
            self.poly([P(r0, a0), P(r1, a0), P(r1, a1), P(r0, a1)], kind, axis)

    def loft(self, rings, kind, caps=True, uvfn=None):
        """같은 점 수의 고리들(아래 → 위 또는 뒤 → 앞)을 이어 닫힌 입체. 바깥 = 고리 가운데에서 멀어지는 쪽."""
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

    def obox(self, c, ax, ay, az, kind, uvfn=None):
        """방향 상자: c 가운데, ax · ay · az = 반축 벡터(서로 수직). ay 쪽이 고리 방향(아래 → 위)."""
        def P(sx, sy, sz):
            return vadd(c, vadd(vmul(ax, sx), vadd(vmul(ay, sy), vmul(az, sz))))
        bot = [P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)]
        top = [P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1)]
        self.loft([bot, top], kind, uvfn=uvfn)

    def box(self, c, s, kind, uvfn=None):
        self.obox(c, (s[0] / 2, 0, 0), (0, s[1] / 2, 0), (0, 0, s[2] / 2), kind, uvfn)

    def bar(self, a, b, w, kind="metal"):
        """a에서 b까지 사각 막대(단면 w × w)."""
        d = vsub(b, a)
        ln = math.sqrt(vdot(d, d))
        if ln < 1e-6:
            return
        d = vmul(d, 1.0 / ln)
        up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.95 else (1.0, 0.0, 0.0)
        x = vnorm(vcross(up, d))
        z = vcross(d, x)
        self.obox(vmul(vadd(a, b), 0.5), vmul(x, w / 2), vmul(d, ln / 2), vmul(z, w / 2), kind)

    def finish(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def ring_y(r, y, n, phase=0.0, cx=0.0, cz=0.0):
    return [(cx + r * math.sin(phase + k * math.tau / n), y, cz + r * math.cos(phase + k * math.tau / n)) for k in range(n)]


def half_xz(y):
    """코어 단면 반폭(x 끝, z 면) — PROFILE 구간 선형."""
    for (y0, x0, z0), (y1, x1, z1) in zip(PROFILE, PROFILE[1:]):
        if y <= y1:
            t = min(max((y - y0) / (y1 - y0), 0.0), 1.0)
            return x0 + (x1 - x0) * t, z0 + (z1 - z0) * t
    return PROFILE[-1][1], PROFILE[-1][2]


SHOULDER = 0.42   # 면(±z)의 반폭 = 끝(x) 반폭 × SHOULDER — 양 끝(±x)이 뾰족하다


def footprint(y, grow=0.0):
    """마름모꼴 육각 단면 6점(+X 끝부터 반시계, 면 j = 점 j → j + 1). grow = 바깥으로 더(m — 각 점을 바깥 방향으로)."""
    hx, hz = half_xz(y)
    pts = [(hx + grow * 1.6, 0.0), (hx * SHOULDER + grow * 0.5, hz + grow), (-(hx * SHOULDER + grow * 0.5), hz + grow),
           (-(hx + grow * 1.6), 0.0), (-(hx * SHOULDER + grow * 0.5), -(hz + grow)), (hx * SHOULDER + grow * 0.5, -(hz + grow))]
    return [(x, y, z) for x, z in pts]


def face_normal(j, y=150.0):
    fp = footprint(y)
    a, b = fp[j], fp[(j + 1) % 6]
    e = vsub(b, a)
    n = vnorm((e[2], 0.0, -e[0]))
    return n if vdot(n, vmul(vadd(a, b), 0.5)) > 0 else vmul(n, -1.0)


def face_pt(y, j, u, d):
    """면 j 위(가로 비율 u 0..1) 높이 y에서 코어 밖 d m인 점."""
    fp = footprint(y)
    p = vlerp(fp[j], fp[(j + 1) % 6], u)
    return vadd(p, vmul(face_normal(j, y), d))


# ───────────── 주탑 ─────────────

def skin_plan(rng):
    """면 j(0..5) · 칸 s마다 외피가 있는지. 아래는 거의 덮고 위로 갈수록 비운다. 넓은 면(1, 4)은 군데군데 크게 드러낸다."""
    nseg = len(SKIN_LEVELS) - 1
    plan = [[True] * nseg for _ in range(6)]
    for j in range(6):
        for s in range(nseg):
            y = (SKIN_LEVELS[s] + SKIN_LEVELS[s + 1]) / 2
            p = 0.06 if y < 220 else (0.3 if y < 480 else 0.55)
            if rng.random() < p:
                plan[j][s] = False
    for j, s in ((1, 6), (4, 4), (1, 10), (4, 11), (0, 8), (3, 7), (2, 12), (5, 9)):
        plan[j][s] = False
    return plan


def build_base():
    rng = random.Random(SEED)
    m = Mesh()
    nseg = len(SKIN_LEVELS) - 1
    plan = skin_plan(rng)

    def exposed(j, y):
        """면 j의 높이 y가 외피 없이 드러나는지(트러스 · 창 · 설비를 그 자리에만 만든다)."""
        if y >= SKIN_LEVELS[-1]:
            return True
        for s in range(nseg):
            if SKIN_LEVELS[s] <= y < SKIN_LEVELS[s + 1]:
                return not plan[j][s]
        return False

    # 기단: 코어 단면을 키운 2단(각진 경사)
    m.loft([footprint(-2.0, 24.0), footprint(4.0, 24.0), footprint(8.0, 18.0)], "concrete")
    m.loft([footprint(7.0, 11.0), footprint(13.0, 11.0), footprint(15.0, 8.0)], "concrete")
    # 단단한 콘크리트 코어: 단마다 끊어 쌓고, 단 사이에 조금 들어간 어두운 강철 이음띠, 단 위아래 모서리는 살짝 깎는다
    for a, b in zip(CORE_LEVELS, CORE_LEVELS[1:]):
        lo, hi = a + 0.9, b - 0.9
        m.loft([footprint(lo, -0.6), footprint(lo + 1.2, 0.0), footprint(hi - 1.2, 0.0), footprint(hi, -0.6)], "concrete")
        if b < CORE_LEVELS[-1]:
            m.loft([footprint(hi, -0.9), footprint(b + 0.9, -0.9)], "metal")
    # 코어 면의 세로 홈(어두운 강철 — 배관 · 승강로 자리) — 넓은 면 가운데, 단마다 끊긴다
    for j in (1, 4):
        n = face_normal(j)
        for a, b in zip(CORE_LEVELS, CORE_LEVELS[1:]):
            lo, hi = a + 3.0, b - 3.0
            rings = []
            for y in (lo, hi):
                c = face_pt(y, j, 0.5, 0.05)
                t = vnorm(vsub(footprint(y)[(j + 1) % 6], footprint(y)[j]))
                rings.append([vadd(c, vadd(vmul(t, s0 * 0.9), vmul(n, s1 * 0.25))) for s0, s1 in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
            m.loft(rings, "metal")
    # 코어 돌출물(작은 상자 — 기계 · 배관 덩어리)
    for _ in range(150):
        j = rng.randrange(6)
        y = rng.uniform(44.0, 770.0)
        u = rng.uniform(0.15, 0.85)
        n = face_normal(j, y)
        fp = footprint(y)
        t = vnorm(vsub(fp[(j + 1) % 6], fp[j]))
        w, h, d = rng.uniform(1.5, 5.0), rng.uniform(2.0, 9.0), rng.uniform(0.8, 2.4)
        c = face_pt(y, j, u, d / 2)
        m.uv_shift = (rng.random(), rng.random())
        m.obox(c, vmul(t, w / 2), (0.0, h / 2, 0.0), vmul(n, d / 2), "metal" if rng.random() < 0.5 else "concrete")
    # 외피판(트러스 바깥) — 칸마다, 넓은 면은 두 장. 위가 비면 윗끝이 뾰족하게 솟기도 한다
    for j in range(6):
        n = face_normal(j)
        wide = j in (1, 4)
        for s in range(nseg):
            if not plan[j][s]:
                continue
            y0, y1 = SKIN_LEVELS[s], SKIN_LEVELS[s + 1]
            parts = [(0.02, 0.5), (0.5, 0.98)] if (wide and rng.random() < 0.7) else [(0.03, 0.97)]
            open_above = s == nseg - 1 or not plan[j][s + 1]
            for u0, u1 in parts:
                off = SKIN_OUT + rng.uniform(0.0, 1.4)
                th = rng.uniform(1.6, 2.8)
                lo = y0 + (0.0 if s == 0 else rng.uniform(0.5, 2.0))
                hi = y1 + rng.uniform(-2.5, 1.0)
                slant = open_above and rng.random() < 0.65
                m.uv_shift = (rng.random(), rng.random())
                bot = [face_pt(lo, j, u0, off + th), face_pt(lo, j, u1, off + th), face_pt(lo, j, u1, off), face_pt(lo, j, u0, off)]
                # 위가 비면 윗변을 크게 비스듬히 자른다(콘크리트 벽 끝), 아니면 거의 수평
                tilt = (rng.choice((-1.0, 1.0)) * rng.uniform(6.0, 14.0)) if slant else rng.uniform(-1.5, 1.5)
                top = [face_pt(hi + tilt, j, u0, off + th), face_pt(hi - tilt, j, u1, off + th), face_pt(hi - tilt, j, u1, off), face_pt(hi + tilt, j, u0, off)]
                m.loft([bot, top], "concrete")
    m.uv_shift = (0.0, 0.0)

    # 트러스: 모서리 기둥(6), 가로 링 · 코어로 가는 짧은 버팀 · X 버팀(드러난 칸만)
    top_y = CORE_LEVELS[-1] - 2.0
    for k in range(6):
        pts = [footprint(y, TRUSS_OUT)[k] for y in (10.0, 120.0, 250.0, 320.0, top_y)]
        m.loft([[vadd(p, (dx, 0.0, dz)) for dx, dz in ((-0.6, -0.6), (0.6, -0.6), (0.6, 0.6), (-0.6, 0.6))] for p in pts], "metal")
    y = 10.0 + RING_STEP
    i = 0
    while y <= top_y:
        fp = footprint(y, TRUSS_OUT)
        core = footprint(y, 0.0)
        for j in range(6):
            if exposed(j, y) or exposed(j, y - RING_STEP * 0.5):
                m.bar(fp[j], fp[(j + 1) % 6], 0.6)
                if i % 2 == 0:
                    m.bar(vlerp(core[j], core[(j + 1) % 6], 0.5), vlerp(fp[j], fp[(j + 1) % 6], 0.5), 0.5)
        y += RING_STEP
        i += 1
    y = 10.0
    while y + RING_STEP * 2 <= top_y:
        y1 = y + RING_STEP * 2
        for j in range(6):
            if exposed(j, (y + y1) / 2):
                a0, b0 = footprint(y, TRUSS_OUT - 0.2)[j], footprint(y, TRUSS_OUT - 0.2)[(j + 1) % 6]
                a1, b1 = footprint(y1, TRUSS_OUT - 0.2)[j], footprint(y1, TRUSS_OUT - 0.2)[(j + 1) % 6]
                m.bar(a0, b1, 0.4)
                m.bar(b0, a1, 0.4)
        y = y1
    # 양 끝(±X) 모서리 날개벽: 단마다 엇갈린 두꺼운 콘크리트 벽(위로 갈수록 작다)
    for k, sgn in ((0, 1.0), (3, -1.0)):
        for (y0, y1, depth) in ((14.0, 330.0, 16.0), (286.0, 519.0, 11.0), (497.0, 660.0, 8.0), (642.0, 774.0, 5.5)):
            yy0 = y0 + (6.0 if sgn < 0 else 0.0)
            yy1 = y1 + (-8.0 if sgn < 0 else 0.0)
            th = 2.6
            p0 = footprint(yy0, TRUSS_OUT)[k]
            p1 = footprint(yy1, TRUSS_OUT)[k]
            pm = footprint(yy0 + (yy1 - yy0) * 0.35, TRUSS_OUT)[k]
            outer = vadd(pm, (sgn * depth, 0.0, 0.0))
            sec = lambda p: [vadd(p, (0.0, 0.0, -th)), vadd(p, (0.0, 0.0, th))]
            # 단면이 얇은 날: 아래 안쪽 → 바깥 끝 → 위 안쪽(뾰족)
            a, b = sec(p0), sec(outer)
            c = sec(p1)
            m.poly([a[0], b[0], c[0]], "concrete", (0, 0, -1))
            m.poly([a[1], b[1], c[1]], "concrete", (0, 0, 1))
            m.poly([a[0], a[1], b[1], b[0]], "concrete", (sgn, -0.6, 0.0))
            m.poly([b[0], b[1], c[1], c[0]], "concrete", (sgn, 0.6, 0.0))
    # 밑동 버팀 날(넓은 면 쪽 넷, 땅에서 비스듬히 올라와 코어에 붙는다)
    for j in (1, 4):
        n = face_normal(j)
        for u in (0.22, 0.78):
            top = face_pt(260.0, j, u, TRUSS_OUT)
            foot_in = face_pt(10.0, j, u, TRUSS_OUT)
            foot_out = vadd(face_pt(10.0, j, u, TRUSS_OUT), vmul(n, 22.0))
            fp = footprint(60.0)
            t = vnorm(vsub(fp[(j + 1) % 6], fp[j]))
            th = 2.4
            pts = [foot_in, foot_out, top]
            left = [vadd(p, vmul(t, -th)) for p in pts]
            right = [vadd(p, vmul(t, th)) for p in pts]
            m.poly(left, "concrete", vmul(t, -1.0))
            m.poly(right, "concrete", t)
            m.poly([left[1], right[1], right[2], left[2]], "concrete", vadd(n, (0.0, 0.8, 0.0)))
            m.poly([left[0], right[0], right[1], left[1]], "concrete", (0.0, -1.0, 0.0))
    # 가로 안테나 팔(가는 각기둥 + 끝 덩어리)
    for y, j, u, ln in ((330.0, 0, 0.5, 18.0), (387.0, 1, 0.3, 20.0), (440.0, 3, 0.5, 16.0), (502.0, 4, 0.7, 24.0), (590.0, 2, 0.5, 16.0),
                        (664.0, 5, 0.5, 18.0), (726.0, 1, 0.6, 12.0)):
        n = face_normal(j, y)
        p0 = face_pt(y, j, u, 0.0)
        p1 = vadd(p0, vmul(n, TRUSS_OUT + ln))
        m.bar(p0, p1, 0.9)
        m.obox(p1, (0.9, 0, 0), (0.0, 1.4, 0.0), (0, 0, 0.9), "metal")
        m.dish(vadd(p1, vadd(vmul(n, 1.2), (0.0, 2.2, 0.0))), vnorm(vadd(n, (0.0, 0.35, 0.0))), 3.4, 1.1, "metal")
    # ── 디테일 ──
    rng2 = random.Random(SEED * 7)
    # 단마다 아래 처마 턱(콘크리트) + 넓은 면 벽기둥 둘
    for a, b in zip(CORE_LEVELS, CORE_LEVELS[1:]):
        lo, hi = a + 0.9, b - 0.9
        m.loft([footprint(lo + 1.2, 0.0), footprint(lo + 1.4, 0.9), footprint(lo + 2.6, 0.9), footprint(lo + 3.0, 0.0)], "concrete")
        for j in (1, 4):
            n = face_normal(j)
            fp = footprint((lo + hi) / 2)
            t = vnorm(vsub(fp[(j + 1) % 6], fp[j]))
            for u in (0.16, 0.84):
                c = face_pt((lo + hi) / 2 + 1.0, j, u, 0.45)
                m.obox(c, vmul(t, 0.9), (0.0, (hi - lo) / 2 - 3.2, 0.0), vmul(n, 0.6), "concrete")
    # 창 줄(드러난 칸만): 단 가운데 높이에 한두 줄, 면 폭에 맞춰 칸 수
    for a, b in zip(CORE_LEVELS, CORE_LEVELS[1:]):
        ym = (a + b) / 2
        rows = (ym - 3.5, ym + 3.5) if b - a > 34 else (ym,)
        for j in range(6):
            if not exposed(j, ym):
                continue
            fp = footprint(ym)
            width = math.sqrt(vdot(vsub(fp[(j + 1) % 6], fp[j]), vsub(fp[(j + 1) % 6], fp[j])))
            cols = max(2, int(width / 3.6))
            n = face_normal(j, ym)
            t = vnorm(vsub(fp[(j + 1) % 6], fp[j]))
            for yy in rows:
                for k in range(cols):
                    u = 0.12 + 0.76 * (k + 0.5) / cols
                    if j in (1, 4) and (abs(u - 0.16) < 0.06 or abs(u - 0.84) < 0.06):
                        continue
                    m.obox(face_pt(yy, j, u, 0.1), vmul(t, 0.5), (0.0, 1.6, 0.0), vmul(n, 0.2), "window")
    # 세로 배관 둘(비스듬한 면을 따라, 단마다 꺾이고 고정쇠)
    for j, u in ((2, 0.3), (5, 0.7)):
        pts = [face_pt(y, j, u, 1.7) for y in [16.0] + CORE_LEVELS[1:-1] + [CORE_LEVELS[-1] - 8.0]]
        m.tube(pts, 1.05, "metal")
        for pp in pts[1:-1]:
            n = face_normal(j, pp[1])
            m.obox(vsub(pp, vmul(n, 0.6)), (0.9, 0, 0) if abs(n[0]) < 0.7 else (0, 0, 0.9), (0.0, 0.5, 0.0), vmul(n, 1.4), "metal")
    # 저장 탱크(드러난 면에, 세로 원통 + 받침)
    placed = 0
    for _ in range(200):
        if placed >= 36:
            break
        j = rng2.randrange(6)
        y = rng2.uniform(66.0, 726.0)
        if not exposed(j, y):
            continue
        r = rng2.uniform(1.6, 2.8)
        h = rng2.uniform(5.0, 10.0)
        u = rng2.uniform(0.25, 0.75)
        c = face_pt(y, j, u, r + 0.4)
        m.cyl((c[0], y - h / 2, c[2]), (c[0], y + h / 2, c[2]), r, "metal", 16)
        m.cyl((c[0], y + h / 2, c[2]), (c[0], y + h / 2 + r * 0.35, c[2]), r * 0.92, "metal", 16, r1=r * 0.35)
        n = face_normal(j, y)
        m.obox(vsub(c, vmul(n, r * 0.5 + 0.2)), (0.6, 0, 0) if abs(n[0]) < 0.7 else (0, 0, 0.6), (0.0, h * 0.35, 0.0), vmul(n, r * 0.5 + 0.2), "metal")
        placed += 1
    # 정비 발판(드러난 면, 링 네 칸마다 — 코어에서 트러스까지 철판)
    y = 10.0 + RING_STEP * 4
    while y <= CORE_LEVELS[-1] - 6.0:
        core = footprint(y, 0.0)
        tr = footprint(y, TRUSS_OUT)
        for j in range(6):
            if exposed(j, y):
                a, b = core[j], core[(j + 1) % 6]
                c, d = tr[(j + 1) % 6], tr[j]
                m.loft([[a, b, c, d], [vadd(a, (0, 0.35, 0)), vadd(b, (0, 0.35, 0)), vadd(c, (0, 0.35, 0)), vadd(d, (0, 0.35, 0))]], "metal")
        y += RING_STEP * 4
    # 기단 출입구(넓은 면 둘, 각 둘) — 어두운 문 + 콘크리트 차양
    for j in (1, 4):
        n = face_normal(j, 10.0)
        for u in (0.3, 0.7):
            c = face_pt(10.0, j, u, 9.5)
            fp = footprint(10.0)
            t = vnorm(vsub(fp[(j + 1) % 6], fp[j]))
            m.obox(vadd(c, (0.0, 0.0, 0.0)), vmul(t, 2.4), (0.0, 3.0, 0.0), vmul(n, 0.3), "window")
            m.obox(vadd(c, vadd(vmul(n, 1.2), (0.0, 3.6, 0.0))), vmul(t, 3.4), (0.0, 0.4, 0.0), vmul(n, 1.6), "concrete")
    # 코어 꼭대기 받침판(회전대 아래)
    m.loft([footprint(CORE_LEVELS[-1] - 1.0, TRUSS_OUT + 0.6), footprint(CORE_LEVELS[-1] + 0.5, TRUSS_OUT + 0.6), footprint(CORE_LEVELS[-1] + 1.5, 0.5)], "concrete")
    return m.finish("LaserTower_Base")


def build_head():
    o = (0.0, HEAD_Y, 0.0)
    m = Mesh(o)
    # 둥근 회전대(부드럽게) + 볼트 띠
    m.smooth_loft([ring_y(8.0, HEAD_Y - 0.5, 32), ring_y(9.0, HEAD_Y + 1.0, 32), ring_y(9.0, HEAD_Y + 2.6, 32), ring_y(7.6, HEAD_Y + 4.6, 32)], "metal")
    for k in range(24):
        a = k * math.tau / 24
        m.box((9.1 * math.sin(a), HEAD_Y + 1.8, 9.1 * math.cos(a)), (0.5, 0.5, 0.5), "metal")
    for s, ant in ((1, 30.0), (-1, 22.0)):
        bot = [(s * 6.5, HEAD_Y + 3.5, -3.5), (s * 9.5, HEAD_Y + 3.5, -3.5), (s * 9.5, HEAD_Y + 3.5, 3.5), (s * 6.5, HEAD_Y + 3.5, 3.5)]
        top = [(s * 8.6, LENS_Y + 4.0, -2.5), (s * 10.6, LENS_Y + 4.0, -2.5), (s * 10.6, LENS_Y + 4.0, 2.5), (s * 8.6, LENS_Y + 4.0, 2.5)]
        m.loft([bot, top], "metal")
        # 팔 바깥 보강 판 + 축받이(둥근)
        m.obox((s * 10.9, (HEAD_Y + LENS_Y) / 2 + 1.0, 0.0), (0.3, 0, 0), (0, (LENS_Y - HEAD_Y) / 2 - 2.0, 0), (0, 0, 2.0), "metal")
        m.cyl((s * 8.4, LENS_Y, 0.0), (s * 11.2, LENS_Y, 0.0), 2.4, "metal", 20)
        x = s * 9.6
        m.cyl((x, LENS_Y + 4.0, 0.0), (x, LENS_Y + 4.0 + ant, 0.0), 0.55, "metal", 8, r1=0.18)
        for k, yy in enumerate((LENS_Y + 10.0, LENS_Y + 16.0 + 3.0 * (s > 0))):
            w = 3.2 - k * 1.0
            m.box((x, yy, 0.0), (0.4, 0.4, w * 2), "metal")
        m.box((x, LENS_Y + 4.0 + ant + 0.3, 0.0), (0.5, 0.5, 0.5), "glow")
    return m.finish("LaserTower_Head"), o


def build_lens():
    """렌즈 통(둥근, 부드럽게): 뒤 덮개(원뿔) → 통 → 냉각 링 넷 → 앞 후드(바깥 띠 · 앞 고리판 · 안쪽 테두리) → 볼록 렌즈(청록) + 위 감지기 상자 · 아래 판 · 옆 축."""
    o = (0.0, LENS_Y, 0.0)
    m = Mesh(o)
    n = 32

    def rz(r, z):
        return [(r * math.cos(k * math.tau / n), LENS_Y + r * math.sin(k * math.tau / n), z) for k in range(n)]

    m.smooth_loft([rz(3.4, -12.8), rz(5.2, -11.4), rz(6.4, -9.6), rz(6.8, -8.8), rz(6.8, 6.2)], "metal")
    m.smooth_loft([rz(6.7, 6.0), rz(7.8, 7.2), rz(7.8, 10.6)], "metal", caps=False)
    m.annulus((0.0, LENS_Y, 10.6), (0, 0, 1), 6.9, 7.8, "metal", n)
    m.smooth_loft([rz(6.9, 10.6), rz(6.9, 9.4)], "metal", caps=False, inward=True)
    m.smooth_loft([rz(6.9, 9.4), rz(5.6, 10.5), rz(3.8, 11.4), rz(1.4, 11.9)], "lens", caps=False)
    m.poly(rz(1.4, 11.9), "lens", (0, 0, 1))
    for z in (-6.5, -3.5, -0.5, 2.5):
        m.annulus((0.0, LENS_Y, z), (0, 0, -1), 6.8, 8.2, "metal", n)
        m.annulus((0.0, LENS_Y, z + 0.5), (0, 0, 1), 6.8, 8.2, "metal", n)
        m.smooth_loft([rz(8.2, z), rz(8.2, z + 0.5)], "metal", caps=False)
    # 위 감지기 상자 · 아래 판 · 옆 축
    m.obox((0.0, LENS_Y + 7.3, -2.0), (2.0, 0, 0), (0, 0.8, 0), (0, 0, 4.5), "metal")
    m.obox((0.0, LENS_Y + 8.3, 1.8), (1.0, 0, 0), (0, 0.4, 0), (0, 0, 1.0), "lens")
    m.obox((0.0, LENS_Y - 7.1, 0.5), (1.6, 0, 0), (0, 0.4, 0), (0, 0, 5.0), "metal")
    for s in (1, -1):
        m.cyl((s * 6.4, LENS_Y, 0.0), (s * 8.6, LENS_Y, 0.0), 1.7, "metal", 16)
    return m.finish("LaserTower_Lens"), o


# ───────────── 보조탑 · 환풍구 · 도관 ─────────────

def aux_pos(i):
    a = math.pi / 6 + i * math.tau / AUX_N
    return (AUX_R * math.sin(a), 0.0, AUX_R * math.cos(a)), a


def aux_frame(a):
    return (math.sin(a), 0.0, math.cos(a)), (math.cos(a), 0.0, -math.sin(a))


def sq_ring(c, n, t, hw, hd, y):
    """중심 c(x, z), 바깥 n · 옆 t 축에 맞춘 사각 고리(높이 y)."""
    return [(c[0] + n[0] * sd * hd + t[0] * sw * hw, y, c[1] + n[2] * sd * hd + t[2] * sw * hw) for sw, sd in ((-1, -1), (1, -1), (1, 1), (-1, 1))]


def aux_half(y):
    return AUX_B0 + (AUX_B1 - AUX_B0) * y / AUX_H


def build_aux(i):
    p, a = aux_pos(i)
    n, t = aux_frame(a)
    m = Mesh(p)
    c = (p[0], p[2])
    # 2단 받침
    m.loft([sq_ring(c, n, t, AUX_B0 + 6.0, AUX_B0 + 6.0, -1.0), sq_ring(c, n, t, AUX_B0 + 6.0, AUX_B0 + 6.0, 2.0), sq_ring(c, n, t, AUX_B0 + 4.5, AUX_B0 + 4.5, 3.0)], "concrete")
    m.loft([sq_ring(c, n, t, AUX_B0 + 2.5, AUX_B0 + 2.5, 2.5), sq_ring(c, n, t, AUX_B0 + 2.5, AUX_B0 + 2.5, 5.0), sq_ring(c, n, t, AUX_B0 + 1.0, AUX_B0 + 1.0, 6.0)], "concrete")
    # 가는 사다리꼴 콘크리트 오벨리스크 — 가로 강철 이음띠 셋에서 끊어 쌓는다
    gaps = [(70.0 * k, 70.0 * k + 1.6) for k in range(1, 6)]
    y = 5.0
    for g0, g1 in gaps + [(AUX_H, AUX_H)]:
        m.loft([sq_ring(c, n, t, aux_half(y), aux_half(y), y), sq_ring(c, n, t, aux_half(g0), aux_half(g0), g0)], "concrete")
        if g1 > g0:
            h = aux_half(g0) - 0.4
            m.loft([sq_ring(c, n, t, h, h, g0), sq_ring(c, n, t, h - 0.05, h - 0.05, g1)], "metal")
        y = g1
    # 네 면 가운데 세로 홈(강철 띠) + 꼭대기 아래 처마
    for d, along in ((n, t), (vmul(n, -1.0), t), (t, n), (vmul(t, -1.0), n)):
        def strip(yy, w, dd):
            cc = (c[0] + d[0] * (aux_half(yy) + dd), yy, c[1] + d[2] * (aux_half(yy) + dd))
            return [vadd(cc, vmul(along, -w)), vadd(cc, vmul(along, w))]
        y0s, y1s = 8.0, AUX_H - 10.0
        b0, b1 = strip(y0s, 0.7, 0.18), strip(y1s, 0.45, 0.18)
        i0, i1 = strip(y0s, 0.7, -0.05), strip(y1s, 0.45, -0.05)
        m.loft([[b0[0], b0[1], i0[1], i0[0]], [b1[0], b1[1], i1[1], i1[0]]], "metal")
    yc = AUX_H - 7.0
    m.loft([sq_ring(c, n, t, aux_half(yc), aux_half(yc), yc), sq_ring(c, n, t, aux_half(yc) + 0.9, aux_half(yc) + 0.9, yc + 0.4),
            sq_ring(c, n, t, aux_half(yc) + 0.9, aux_half(yc) + 0.9, yc + 1.8), sq_ring(c, n, t, aux_half(yc + 2.2), aux_half(yc + 2.2), yc + 2.2)], "concrete")
    # 주탑 쪽 면: 출입문 + 차양, 위로 작은 창 몇 개
    d = vmul(n, -1.0)
    door = (c[0] + d[0] * (aux_half(9.0) + 0.1), 9.0, c[1] + d[2] * (aux_half(9.0) + 0.1))
    m.obox(door, vmul(t, 2.2), (0.0, 3.0, 0.0), vmul(d, 0.25), "window")
    m.obox(vadd(door, vadd(vmul(d, 1.4), (0.0, 3.6, 0.0))), vmul(t, 3.0), (0.0, 0.35, 0.0), vmul(d, 1.5), "concrete")
    for yy in (60.0, 130.0, 200.0, 270.0, 340.0, 400.0):
        for s in (-1, 1):
            w = (c[0] + d[0] * (aux_half(yy) + 0.06) + t[0] * s * aux_half(yy) * 0.45, yy, c[1] + d[2] * (aux_half(yy) + 0.06) + t[2] * s * aux_half(yy) * 0.45)
            m.obox(w, vmul(t, 0.45), (0.0, 1.4, 0.0), vmul(d, 0.15), "window")
    return m.finish("LaserTower_Aux%d" % (i + 1)), p


def build_radiator(i):
    """끝 작은 환풍구: 오벨리스크 윗면 위 사각 상자(반폭 AUX_B1 - 0.6, 높이 VENT_H). 안쪽이 달아오르고 네 면 가로 살 셋, 위 덮개."""
    p, a = aux_pos(i)
    n, t = aux_frame(a)
    base = (p[0], AUX_H, p[2])
    m = Mesh(base)
    c = (p[0], p[2])
    hb = AUX_B1 - 0.6
    y0, y1 = AUX_H, AUX_H + VENT_H
    hi = hb - 0.5

    def uv(q, y0=y0):
        return (0.5, min(max((q[1] - (y0 + 0.4)) / (VENT_H - 0.8), 0.0), 1.0))

    m.loft([sq_ring(c, n, t, hi, hi, y0), sq_ring(c, n, t, hi, hi, y1 - 0.6)], "radiator", caps=False, uvfn=uv)
    for sx in (-1, 1):
        for sz in (-1, 1):
            cc = (c[0] + n[0] * sz * (hb - 0.3) + t[0] * sx * (hb - 0.3), c[1] + n[2] * sz * (hb - 0.3) + t[2] * sx * (hb - 0.3))
            m.loft([sq_ring(cc, n, t, 0.3, 0.3, y0), sq_ring(cc, n, t, 0.3, 0.3, y1 - 0.6)], "metal")
    for k in range(3):
        yy = y0 + 1.0 + k * (VENT_H - 2.2) / 2
        for d, along in ((n, t), (vmul(n, -1.0), t), (t, n), (vmul(t, -1.0), n)):
            cc = vadd((c[0], yy, c[1]), vmul(d, hb - 0.2))
            m.obox(cc, vmul(along, hb - 0.6), (0.0, 0.22, 0.0), vmul(d, 0.2), "metal")
    m.loft([sq_ring(c, n, t, hb + 0.3, hb + 0.3, y1 - 0.6), sq_ring(c, n, t, hb + 0.3, hb + 0.3, y1), sq_ring(c, n, t, hb - 0.8, hb - 0.8, y1 + 0.8)], "metal")
    # 둥근 배기통 + 꼭대기 작은 피뢰침 · 항공 표지등
    m.cyl((c[0] + t[0] * 1.2, y1 + 0.8, c[1] + t[2] * 1.2), (c[0] + t[0] * 1.2, y1 + 3.2, c[1] + t[2] * 1.2), 0.9, "metal", 16)
    m.cyl((c[0] - t[0] * 1.2, y1 + 0.8, c[1] - t[2] * 1.2), (c[0] - t[0] * 1.2, y1 + 9.0, c[1] - t[2] * 1.2), 0.25, "metal", 8, r1=0.08)
    m.box((c[0] - t[0] * 1.2, y1 + 9.2, c[1] - t[2] * 1.2), (0.45, 0.45, 0.45), "radiator", uvfn=lambda q: (0.5, 0.95))
    return m.finish("LaserTower_Radiator%d" % (i + 1)), base


def build_conduits():
    """땅 위 둥근 관 두 줄(부드럽게, 이음 테 — 플랜지) + 콘크리트 받침."""
    m = Mesh()
    for i in range(AUX_N):
        p, a = aux_pos(i)
        n, t = aux_frame(a)
        r0, r1 = 40.0, AUX_R - AUX_B0 - 7.0
        for s in (-1, 1):
            off = vmul(t, s * 1.7)
            a0 = vadd(vmul(n, r0), vadd(off, (0.0, 3.2, 0.0)))
            a1 = vadd(vmul(n, r1), vadd(off, (0.0, 3.2, 0.0)))
            m.cyl(a0, a1, 1.25, "metal", 14)
            for f in (0.25, 0.5, 0.75):
                cc = vlerp(a0, a1, f)
                m.cyl(vsub(cc, vmul(n, 0.3)), vadd(cc, vmul(n, 0.3)), 1.55, "metal", 14)
        for f in (0.12, 0.3, 0.5, 0.7, 0.88):
            cc = vmul(n, r0 + (r1 - r0) * f)
            m.obox((cc[0], 1.0, cc[2]), vmul(t, 3.6), (0.0, 1.0, 0.0), vmul(n, 0.9), "concrete")
    return m.finish("LaserTower_Conduits")


# ───────────── 텍스처 · 재질 ─────────────

def save_image(name, arr):
    h, w = arr.shape[:2]
    im = bpy.data.images.new(name, w, h, alpha=False)
    im.pixels.foreach_set(arr.ravel())
    im.filepath_raw = os.path.join(OUT, name + ".png")
    im.file_format = 'PNG'
    im.save()
    return im


def normal_map(name, h, strength):
    """높이 배열 h(행 0 = 아래)에서 노멀맵(OpenGL — 초록 = 위)을 만든다. 이웃 픽셀은 감아 돌아(반복 이음매 없음)."""
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * strength
    nz = np.ones_like(h)
    l = np.sqrt(dx * dx + dy * dy + nz * nz)
    img = np.ones(h.shape + (4,), dtype=np.float32)
    img[..., 0] = (-dx / l) * 0.5 + 0.5
    img[..., 1] = (-dy / l) * 0.5 + 0.5
    img[..., 2] = (nz / l) * 0.5 + 0.5
    return save_image(name, img)


def periodic_blot(rng, S, terms, fmax):
    ys, xs = np.mgrid[0:S, 0:S]
    b = np.zeros((S, S), dtype=np.float32)
    for _ in range(terms):
        fx, fy = rng.integers(-fmax, fmax + 1, size=2)
        if fx == 0 and fy == 0:
            continue
        b += np.sin(2 * np.pi * (fx * xs + fy * ys) / S + rng.uniform(0, 6.28)).astype(np.float32)
    return b / max(np.abs(b).max(), 1e-6)


def metal_texture():
    """낡은 강철(64m 한 장, 반복 이음매 없음): 2m × 4m 판(줄마다 반 칸 엇갈림), 판마다 밝기 · 색 차이, 이음줄 + 이음줄 따라 리벳 줄,
    녹 얼룩(낮은 주파수 주기 잡음) · 녹물 자국(세로), 고운 잡티. 노멀맵(_Normal): 이음줄 들어감 · 리벳 볼록."""
    S = 2048
    rng = np.random.default_rng(7)
    nr, nc = 16, 32
    row_h, col_w = S // nr, S // nc
    ys, xs = np.mgrid[0:S, 0:S]
    rows = ys // row_h
    off = (rows % 2) * (col_w // 2)
    cols = ((xs + off) % S) // col_w
    pan = rng.normal(1.0, 0.06, size=(nr, nc)).astype(np.float32)
    shade = pan[rows, cols]
    yin = ys % row_h
    xin = (xs + off) % col_w
    seam = (yin < 2) | (xin < 2)
    rivet = (((yin == 6) | (yin == row_h - 5)) & ((xin % 8) < 2)) | (((xin == 6) | (xin == col_w - 5)) & ((yin % 8) < 2))
    base = np.array([0.30, 0.31, 0.32], dtype=np.float32)
    rust = np.array([0.36, 0.21, 0.12], dtype=np.float32)
    shade = np.where(seam, shade * 0.55, np.where(rivet, shade * 1.25, shade))
    blot = periodic_blot(rng, S, 10, 6)
    rmask = np.clip((blot - 0.35) * 2.5, 0.0, 1.0) * 0.55
    streak = np.zeros(S, dtype=np.float32)
    for f in rng.integers(10, 240, size=40):
        streak += np.sin(2 * np.pi * f * np.arange(S) / S + rng.uniform(0, 6.28)).astype(np.float32)
    streak = np.clip(streak / np.abs(streak).max(), 0.0, 1.0) ** 2
    fall = 0.5 + 0.5 * np.cos(2 * np.pi * ys / row_h)
    rmask = np.clip(rmask + 0.35 * streak[None, :] * fall, 0.0, 0.8)
    col = base[None, None, :] * (1.0 - rmask[..., None]) + rust[None, None, :] * rmask[..., None]
    col = col * shade[..., None] * rng.normal(1.0, 0.03, size=(S, S, 1)).astype(np.float32)
    img = np.ones((S, S, 4), dtype=np.float32)
    img[..., :3] = np.clip(col, 0, 1)
    h = np.where(seam, -1.0, 0.0) + np.where(rivet, 0.8, 0.0) + rng.normal(0.0, 0.05, size=(S, S))
    normal_map(NAME + "_Metal_Normal", h.astype(np.float32), 1.6)
    return save_image(NAME + "_Metal", img)


def concrete_texture():
    """밝은 회색 콘크리트(64m 한 장, 이어 붙여도 이음매 없이 반복): 거푸집 판 2m × 1m(줄마다 반 칸 엇갈림)의 옅은 이음줄과
    판마다 조금 다른 밝기, 판 모서리 안쪽 타이 구멍, 세로 빗물 자국(위에서 흘러내린 듯 아래로 갈수록 옅게 — 주기 함수),
    큰 얼룩(낮은 주파수 주기 잡음), 고운 잡티."""
    S = 2048
    rng = np.random.default_rng(11)
    px = S / TILE
    base = np.array([0.52, 0.51, 0.49], dtype=np.float32)
    img = np.ones((S, S, 4), dtype=np.float32)
    ys, xs = np.mgrid[0:S, 0:S]
    nr, nc = 64, 32              # 판 1m × 2m(64m 한 장에 정확히 나뉜다 — 반복 이음매 없음)
    row_h, col_w = S // nr, S // nc
    rows = ys // row_h
    off = (rows % 2) * (col_w // 2)
    cols = ((xs + off) % S) // col_w
    pan = rng.normal(1.0, 0.035, size=(nr + 1, nc + 1)).astype(np.float32)
    shade = pan[rows % nr, cols % nc]
    yin = ys % row_h
    xin = (xs + off) % col_w
    seam = (yin < 1) | (xin < 1)
    shade = np.where(seam, shade * 0.86, shade)
    # 타이 구멍(판 안 네 귀 근처)
    hx = np.minimum(np.abs(xin - col_w * 0.12), np.abs(xin - col_w * 0.88))
    hy = np.minimum(np.abs(yin - row_h * 0.22), np.abs(yin - row_h * 0.78))
    shade = np.where((hx < 2.2) & (hy < 2.2), shade * 0.62, shade)
    # 타이 구멍 아래로 흘러내린 얼룩(짧게)
    below = (hx < 1.6) & (((row_h * 0.22 - yin > 0) & (row_h * 0.22 - yin < 9)) | ((row_h * 0.78 - yin > 0) & (row_h * 0.78 - yin < 9)))
    shade = np.where(below, shade * 0.9, shade)
    # 빗물 자국(세로, 주기) — 줄마다 위에서 진하게
    streak = np.zeros(S, dtype=np.float32)
    for f in rng.integers(6, 220, size=40):
        streak += np.sin(2 * np.pi * f * np.arange(S) / S + rng.uniform(0, 6.28)).astype(np.float32)
    streak = np.clip(streak / np.abs(streak).max(), 0.0, 1.0) ** 1.5
    fall = 0.55 + 0.45 * np.cos(2 * np.pi * ys / S * 4.0)
    shade = shade * (1.0 - 0.16 * streak[None, :] * fall)
    # 큰 얼룩(주기)
    blot = np.zeros((S, S), dtype=np.float32)
    for _ in range(6):
        fx, fy = rng.integers(1, 6, size=2)
        blot += np.sin(2 * np.pi * (fx * xs + fy * ys) / S + rng.uniform(0, 6.28)).astype(np.float32)
    shade = shade * (1.0 + 0.035 * blot / 3.0)
    # 타설 줄(3m마다 — 거푸집 판 셋째 줄 이음이 진하고 조금 튀어나온 자국)
    lift = (rows % 3 == 0) & (yin < 3)
    shade = np.where(lift, shade * 0.78, shade)
    grain = rng.normal(0.0, 1.0, size=(S, S)).astype(np.float32)
    shade = shade * (1.0 + 0.03 * grain)
    # 판마다 아주 조금 다른 색조(따뜻 · 차가움)
    warm = rng.normal(0.0, 0.02, size=(nr + 1, nc + 1)).astype(np.float32)[rows % nr, cols % nc]
    col = base[None, None, :] * shade[..., None]
    col[..., 0] *= 1.0 + warm
    col[..., 2] *= 1.0 - warm
    img[..., :3] = np.clip(col, 0, 1)
    hole = (hx < 2.2) & (hy < 2.2)
    h = np.where(seam, -0.6, 0.0) + np.where(lift, -1.0, 0.0) + np.where(hole, -1.6, 0.0) + 0.08 * grain + 0.25 * blot / 3.0
    normal_map(NAME + "_Concrete_Normal", h.astype(np.float32), 1.4)
    return save_image(NAME + "_Concrete", img)


def solid(name, col):
    img = np.ones((8, 8, 4), dtype=np.float32)
    img[..., :3] = col
    return save_image(name, img)


def radiator_texture():
    """환풍구 달아오른 안쪽(가로 = 폭, 세로 = 아래 0 → 위 1): 검붉은 → 주황 → 노란 위, 가로 홈 8줄."""
    W, H = 128, 512
    v = (np.arange(H, dtype=np.float32) + 0.5) / H
    keys = [(0.0, (0.30, 0.06, 0.03)), (0.4, (0.70, 0.18, 0.04)), (0.8, (0.98, 0.45, 0.10)), (1.0, (1.0, 0.72, 0.25))]
    col = np.zeros((H, 3), dtype=np.float32)
    for c in range(3):
        col[:, c] = np.interp(v, [k[0] for k in keys], [k[1][c] for k in keys])
    img = np.ones((H, W, 4), dtype=np.float32)
    img[..., :3] = col[:, None, :]
    groove = ((v * 8) % 1.0) < 0.14
    img[groove, :, :3] *= 0.6
    return save_image(NAME + "_Radiator", img)


def materials(images):
    mats = []
    for nm, im in zip(MAT_NAMES, images):
        mat = bpy.data.materials.new(NAME + "_" + nm)
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


def preview(folder, shots):
    import mathutils
    sc = bpy.context.scene
    g = Mesh()
    g.poly([(-4000, -0.5, -4000), (4000, -0.5, -4000), (4000, -0.5, 4000), (-4000, -0.5, 4000)], "metal", (0, 1, 0), uvfn=lambda p: (0.5, 0.5))
    gm = bpy.data.materials.new("ground")
    gm.diffuse_color = (0.30, 0.33, 0.27, 1)
    gme = g.finish("ground")
    gme.materials.append(gm)
    go = bpy.data.objects.new("ground", gme)
    sc.collection.objects.link(go)
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
    cam.data.lens = 35
    cam.data.clip_start = 1.0
    cam.data.clip_end = 20000.0
    ap, _ = aux_pos(0)
    views = {
        "far": ((1800.0, 600.0, 2200.0), (0.0, 420.0, 0.0)),
        "flyby": ((2500.0, 480.0, -1150.0), (0.0, 420.0, 0.0)),
        "side": ((0.0, 440.0, 2400.0), (0.0, 440.0, 0.0)),
        "top": ((0.0, 2900.0, 0.5), (0.0, 0.0, 0.0)),
        "crown": ((70.0, 840.0, 90.0), (0.0, 790.0, 0.0)),
        "mid": ((95.0, 560.0, 120.0), (0.0, 520.0, 0.0)),
        "base": ((160.0, 45.0, 200.0), (0.0, 70.0, 0.0)),
        "aux": ((ap[0] + 90.0, 470.0, ap[2] + 80.0), (ap[0], 420.0, ap[2])),
        "ground": ((40.0, 6.0, 520.0), (0.0, 520.0, 0.0)),
    }
    for name, (eye_u, at_u) in views.items():
        if shots and name not in shots:
            continue
        eye = mathutils.Vector(to_blender(eye_u))
        at = mathutils.Vector(to_blender(at_u))
        cam.location = eye
        cam.rotation_euler = (at - eye).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(folder, "lasertower_" + name + ".png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    imgs = [metal_texture(), solid(NAME + "_Glow", (0.62, 0.85, 1.0)), solid(NAME + "_Lens", (0.35, 0.95, 1.0)), radiator_texture(), concrete_texture(),
            solid(NAME + "_Window", (0.07, 0.08, 0.10))]
    mats = materials(imgs)
    objs = [link(build_base(), mats), link(build_conduits(), mats)]
    me, o = build_head()
    objs.append(link(me, mats, o))
    me, o = build_lens()
    objs.append(link(me, mats, o))
    for i in range(AUX_N):
        me, o = build_aux(i)
        objs.append(link(me, mats, o))
        me, o = build_radiator(i)
        objs.append(link(me, mats, o))
    for ob in objs:
        print("TRIS", ob.name, sum(len(p.vertices) - 2 for p in ob.data.polygons))
    print("TRIS total", sum(len(p.vertices) - 2 for ob in objs for p in ob.data.polygons))
    print("PIVOTS head", (0.0, HEAD_Y, 0.0), "lens", (0.0, LENS_Y, 0.0), "aux", [tuple(round(c, 2) for c in aux_pos(i)[0]) for i in range(AUX_N)], "vent base y", AUX_H)
    write_fbx(os.path.join(OUT, NAME + ".fbx"), objs)
    if args:
        preview(args[0], args[1:])
    print("DONE")
