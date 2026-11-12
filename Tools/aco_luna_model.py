"""UAV 에이스 LUNA 기체(가공 — 실존 기체 아님). 수직꼬리날개 없는 카나드 + 크랭크 델타(무미익 실험기 같은 미래형), 흰색 바탕에 금색 강조.
 - 기수: 납작한 차인 기수 양옆을 따라 레일건 레일 두 줄이 기수 끝보다 앞으로 뻗고, 레일 사이에 빛나는 틈(포구 = 레일 끝 가운데).
 - 차인 스트레이크가 기수에서 날개 뿌리까지 이어지고(앞이 날카롭다), 날개는 안쪽이 더 젖혀진 크랭크 델타, 뒷전은 살짝 앞으로. 끝에 갈라지는 방향타(무늬).
 - 카나드: 캐노피 옆 작은 후퇴익. 캐노피: 금빛 유리(작고 낮다). 흡입구: 배 밑 사다리꼴 하나. 꼬리: 납작한 뒷판(톱니 뒷전) + 2차원 노즐.
 - 기수 밑 유도 레이저 구(센서 볼). 날개 밑 UAV 거치대 4곳(전개 전 UAV가 붙는 자리).
부품(FBX 오브젝트):
 - Luna_Body: 기체 전체
 - Luna_Uav: 요격 UAV(작은 무미익 날개 — 전개된 UAV와 날개 밑에 붙은 UAV에 함께 쓴다, 원점 = 가운데)
좌표: 유니티 그대로(x 오른쪽, y 위, z 앞 = 기수). 단위 m. 길이 약 20.5m(레일 포함), 폭 약 13m.
텍스처 2048: 흰 동체 면은 위·아래·옆 투영 UV로 패널 라인·금색 띠·초승달 문양을 직접 칠한다. 나머지 재질은 색 칸.
실행: blender --background --factory-startup --python Tools/aco_luna_model.py   (ACO_LUNA_OUT으로 다른 폴더에, ACO_LUNA_PREVIEW=1이면 미리보기 렌더 PNG)"""
import math
import os
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_LUNA_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Luna"))
NAME = "ACO_Luna"
TEX = 2048
BODY_COL = (0.9, 0.9, 0.88)      # 흰 동체
GOLD = (0.86, 0.64, 0.22)        # 금색 강조
GOLD_DARK = (0.55, 0.38, 0.12)
PATCH = {
    "body": ((0.05, 0.9), BODY_COL),
    "gold": ((0.16, 0.9), GOLD),
    "dark": ((0.27, 0.9), (0.16, 0.16, 0.18)),
    "black": ((0.38, 0.9), (0.025, 0.025, 0.03)),
    "glass": ((0.49, 0.9), (0.62, 0.46, 0.16)),      # 금빛 캐노피
    "glow": ((0.6, 0.9), (1.0, 0.86, 0.52)),       # 레일 틈·UAV 렌즈(밝은 금빛)
    "metal": ((0.71, 0.9), (0.55, 0.55, 0.56)),
    "lens": ((0.82, 0.9), (0.08, 0.06, 0.05)),     # 센서 볼 유리
    "nozzle": ((0.93, 0.9), (0.3, 0.24, 0.17)),
    # 둘째 줄(v 0.805)
    "rail": ((0.05, 0.805), (0.045, 0.045, 0.05)),       # 레일건(검정)
    "railside": ((0.16, 0.805), (0.11, 0.11, 0.12)),     # 레일건 옆면·방열판(짙은 회흑)
    "eots": ((0.27, 0.805), (0.2, 0.13, 0.05)),          # 표적 지시 창(호박빛 유리)
    "hot": ((0.38, 0.805), (0.95, 0.55, 0.18)),          # 노즐 안 뜨거운 쪽
    "blue": ((0.49, 0.805), (0.55, 0.85, 1.0)),          # 레일 안쪽 빛(전하)
    "antenna": ((0.6, 0.805), (0.3, 0.3, 0.32)),
}
PATCH_SIZE = 0.08
RING_N = 28

# 투영 영역(유니티 좌표 → uv). 위: (x, z), 아래: (x, z), 옆: (z, y). z −10.5~10.5, x −7.5~7.5
def uv_top(x, z):
    return (0.25 - x / 32.0, 0.02 + (z + 10.5) / 21.0 * 0.56)


def uv_bottom(x, z):
    return (0.75 - x / 32.0, 0.02 + (z + 10.5) / 21.0 * 0.56)


def uv_side(z, y):
    return (0.02 + (z + 10.5) / 21.0 * 0.56, 0.62 + (y + 2.0) / 4.0 * 0.14)


def uav_uv_top(x, z):
    return (0.62 + (1.7 - x) / 3.4 * 0.17, 0.625 + (z + 1.0) / 2.05 * 0.075)


def uav_uv_bottom(x, z):
    return (0.81 + (1.7 - x) / 3.4 * 0.17, 0.625 + (z + 1.0) / 2.05 * 0.075)


def uav_uv_side(z, y):
    return (0.62 + (z + 1.0) / 2.05 * 0.36, 0.705 + (y + 0.35) / 0.7 * 0.05)


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Mesh:
    def __init__(self, proj=None):
        """proj = (위, 아래, 옆) 투영 함수 — 없으면 기체(LUNA) 칸."""
        self.proj = proj
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.body_faces = set()
        self.inward = []     # 안쪽을 봐야 하는 면(덕트 — 법선 정리 뒤 뒤집는다)

    def paint(self, f, kind):
        (u, v), _ = PATCH[kind]
        for loop in f.loops:
            loop[self.uv].uv = (u, v)
        if kind == "body":
            self.body_faces.add(f)
        else:
            self.body_faces.discard(f)

    def project_body(self):
        for f in self.body_faces:
            n = f.normal
            nx, ny = -n.x, n.z
            for loop in f.loops:
                c = loop.vert.co
                x, y, z = -c.x, c.z, -c.y
                ft, fb, fs = self.proj if self.proj else (uv_top, uv_bottom, uv_side)
                if abs(ny) >= abs(nx) * 0.8:
                    loop[self.uv].uv = ft(x, z) if ny > 0 else fb(x, z)
                else:
                    loop[self.uv].uv = fs(z, y)

    def loft(self, rings, kinds, caps=True, smooth=True, around=None, kind_at=None, inward=False):
        """kind_at(i, k): 면마다 재질(있으면 kinds·around보다 먼저 — None을 돌려주면 그대로, "skip"이면 면을 만들지 않는다).
        inward: 안쪽에서 보는 통(덕트) — 법선 정리 뒤 뒤집는다."""
        vs = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = len(rings[0])
        for i in range(len(vs) - 1):
            kind = kinds if isinstance(kinds, str) else kinds[i]
            for k in range(n):
                m = (k + 1) % n
                f = self.bm.faces.new((vs[i][k], vs[i][m], vs[i + 1][m], vs[i + 1][k]))
                f.smooth = smooth
                kk = kind_at(i, k) if kind_at else None
                if kk == "skip":
                    self.bm.faces.remove(f)
                    continue
                self.paint(f, kk if kk else (around[k] if around else kind))
                if inward:
                    self.inward.append(f)
        if caps:
            ends = ((vs[0], kinds if isinstance(kinds, str) else kinds[0]), (vs[-1], kinds if isinstance(kinds, str) else kinds[-1]))
            if caps == "end":
                ends = ends[1:]
            for ring, kind in ends:
                f = self.bm.faces.new(ring)
                self.paint(f, kind)
                if inward:
                    self.inward.append(f)

    def finish(self, name):
        duct = set(self.inward)
        bmesh.ops.recalc_face_normals(self.bm, faces=[f for f in self.bm.faces if f not in duct])
        # 덕트: 따로 정리하면 바깥(통의 바깥)을 본다 — 안에서 보이게 뒤집는다
        if self.inward:
            bmesh.ops.recalc_face_normals(self.bm, faces=self.inward)
            bmesh.ops.reverse_faces(self.bm, faces=self.inward)
        self.project_body()
        for e in self.bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(40):
                e.smooth = False
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def interp(table, z, smooth=False):
    if z >= table[0][0]:
        return table[0][1:]
    for j, (a, b) in enumerate(zip(table, table[1:])):
        if b[0] <= z <= a[0]:
            k = (a[0] - z) / (a[0] - b[0])
            if not smooth:
                return tuple(a[i] + (b[i] - a[i]) * k for i in range(1, len(a)))
            p0 = table[max(j - 1, 0)]
            p3 = table[min(j + 2, len(table) - 1)]
            out = []
            for i in range(1, len(a)):
                v0, v1, v2, v3 = p0[i], a[i], b[i], p3[i]
                v = 0.5 * (2 * v1 + (-v0 + v2) * k + (2 * v0 - 5 * v1 + 4 * v2 - v3) * k * k + (-v0 + 3 * v1 - 3 * v2 + v3) * k ** 3)
                out.append(max(v, 0.005) if i <= 3 else v)
            return tuple(out)
    return table[-1][1:]


def se_ring(cx, cy, z, w, ht, hb, e, n=RING_N):
    pts = []
    for k in range(n):
        th = math.pi / 2 + k * math.tau / n
        c, s = math.cos(th), math.sin(th)
        x = cx + w * math.copysign(abs(c) ** (2 / e), c)
        y = cy + (ht if s > 0 else hb) * math.copysign(abs(s) ** (2 / e), s)
        pts.append((x, y, z))
    return pts


def steps(z0, z1, d):
    n = max(int(math.ceil(abs(z0 - z1) / d)), 1)
    return [z0 + (z1 - z0) * i / n for i in range(n + 1)]


# 동체: (z, 반폭, 위 반높이, 아래 반높이, 중심 y) — 납작한 차인 기수 → 넓은 가운데 → 노즐
FUS = [
    (9.3, 0.04, 0.04, 0.04, -0.12),
    (8.7, 0.3, 0.22, 0.22, -0.1),
    (7.6, 0.52, 0.42, 0.4, -0.05),
    (6.0, 0.72, 0.62, 0.58, 0.0),
    (4.4, 0.86, 0.74, 0.7, 0.02),
    (2.8, 0.98, 0.84, 0.76, 0.04),
    (1.0, 1.06, 0.88, 0.8, 0.05),
    (-1.0, 1.1, 0.84, 0.78, 0.05),
    (-3.5, 1.1, 0.77, 0.7, 0.05),
    (-5.8, 1.1, 0.72, 0.68, 0.05),
    (-7.6, 1.06, 0.66, 0.64, 0.05),
    (-8.4, 0.98, 0.58, 0.58, 0.05),
    (-8.9, 0.84, 0.46, 0.48, 0.05),
]
E_FUS = 2.9   # 단면 각짐(2 = 타원, 클수록 네모)
TAIL_Z = -8.9
WING_Y = 0.0
CHINE_Y = -0.02


def top_y(x, z):
    w, ht, hb, cy = interp(FUS, z, True)
    if abs(x) >= w:
        return WING_Y + 0.1
    return cy + ht * (1 - (abs(x) / w) ** E_FUS) ** (1 / E_FUS)


def bottom_y(x, z):
    w, ht, hb, cy = interp(FUS, z, True)
    if abs(x) >= w:
        return WING_Y - 0.1
    return cy - hb * (1 - (abs(x) / w) ** E_FUS) ** (1 / E_FUS)


def _old_fuselage(m):
    m.loft([se_ring(0, cy, z, w, ht, hb, E_FUS) for z in steps(FUS[0][0], TAIL_Z, 0.45) for w, ht, hb, cy in [interp(FUS, z, True)]], "body", caps=False)
    # 캐노피: 작고 낮은 금빛 유리
    C = [(6.9, 0.04, 0.04), (6.4, 0.3, 0.22), (5.5, 0.42, 0.34), (4.4, 0.4, 0.3), (3.5, 0.24, 0.16), (2.9, 0.04, 0.04)]
    rings = []
    for z, w, h in C:
        base = top_y(0.0, z) - 0.1
        rings.append([(math.cos(a) * w, base + max(math.sin(a), -0.15) * h, z) for a in (k * math.tau / 18 for k in range(18))])
    m.loft(rings, "glass")
    # 2차원 노즐: 금색 테 → 어두운 안쪽
    w, ht, hb, cy = interp(FUS, TAIL_Z)
    def box(z, hw, hh, yc=cy):
        return [(hw, yc + hh, z), (-hw, yc + hh, z), (-hw, yc - hh, z), (hw, yc - hh, z)]
    m.loft([box(TAIL_Z, w * 0.95, ht * 0.95), box(TAIL_Z - 0.55, 0.58, 0.24), box(TAIL_Z - 0.62, 0.5, 0.18), box(TAIL_Z - 0.2, 0.48, 0.16)], ["gold", "nozzle", "black"], smooth=False)
    # 등 흡입구(캐노피 뒤 사다리꼴 — 위 입술이 앞으로, 뒤로 가며 등에 녹아든다). 배 밑은 레일건 용골
    def intake(z, hw, height, rake=0.0, inner=1.0):
        yt = top_y(0.0, z) - 0.06
        y1 = yt + height
        return [(hw * 0.78 * inner, yt, z), (-hw * 0.78 * inner, yt, z), (-hw * inner, y1, z + rake), (hw * inner, y1, z + rake)]
    rings = [intake(1.5, 0.3, 0.16, 0.25, 0.8), intake(2.35, 0.36, 0.22, 0.3, 0.86), intake(2.4, 0.42, 0.26, 0.34), intake(2.27, 0.44, 0.27, 0.34), intake(1.6, 0.44, 0.27),
             intake(-0.4, 0.42, 0.24), intake(-2.4, 0.34, 0.12), intake(-3.8, 0.24, 0.02)]
    m.loft(rings, ["black", "dark", "gold", "body", "body", "body", "body"], smooth=False)
    # 흡입구 가운데 칸막이(스플리터)
    beam(m, (0.0, top_y(0.0, 2.0) + 0.1, 1.6), (0.0, top_y(0.0, 2.0) + 0.1, 2.38), 0.02, 0.09, "dark")


def surface(m, root, tip, kind, thick_root, thick_tip, edge=None, tip_edge=None):
    """날개꼴 판: root/tip = (x, y, z_le, z_te). 6점 단면(앞 둥근 모서리, 뒤 뾰족). edge: 앞전 강조색."""
    def section(x, y, zle, zte, t):
        c = zle - zte
        e = 0.08
        return [(x, y, zle), (x, y + t * 0.45, zle - c * e), (x, y + t * 0.5, zle - c * 0.42), (x, y, zte),
                (x, y - t * 0.4, zle - c * 0.42), (x, y - t * 0.35, zle - c * e)]
    around = [edge, kind, kind, kind, kind, edge] if edge else None
    m.loft([section(*root, thick_root), section(*tip, thick_tip)], kind, smooth=True, around=around, caps=False)
    if tip_edge:
        r = section(*tip, thick_tip)
        f = m.bm.faces.new([m.bm.verts.new(to_blender(p)) for p in r])
        m.paint(f, tip_edge)


# 평면형(x>0): 차인 스트레이크 / 주날개(크랭크 델타) / 카나드 — (x, y, 앞전 z, 뒷전 z)
CHINE = [(0.4, CHINE_Y, 8.6, 1.0), (1.35, CHINE_Y, 4.4, -1.4), (2.25, CHINE_Y + 0.01, 0.6, -2.6)]
WING = [(0.7, WING_Y, -0.6, -8.6), (2.15, WING_Y, -0.9, -7.7), (4.3, WING_Y - 0.02, -3.5, -6.9), (6.5, WING_Y - 0.05, -5.4, -6.25)]
CANARD = [(0.9, 0.16, 4.7, 3.3), (2.75, 0.1, 3.2, 2.6)]


def wing_edge(x, idx):
    """날개 앞전(idx 2)·뒷전(idx 3) z — 블렌디드 평면형(LE_PTS/TE_PTS)."""
    return le_z(x) if idx == 2 else te_z(x)


def _old_wings(m):
    for s in (1, -1):
        def S(p):
            return (s * p[0],) + tuple(p[1:])
        # 차인: 얇고 날카로운 판(앞전 금색)
        surface(m, S(CHINE[0]), S(CHINE[1]), "body", 0.1, 0.12, edge="gold")
        surface(m, S(CHINE[1]), S(CHINE[2]), "body", 0.12, 0.14, edge="gold")
        # 주날개: 안쪽(동체 속) → 크랭크 → 끝. 앞전 금색, 끝 단면 금색
        surface(m, S(WING[0]), S(WING[1]), "body", 0.42, 0.36)
        surface(m, S(WING[1]), S(WING[2]), "body", 0.36, 0.22, edge="gold")
        surface(m, S(WING[2]), S(WING[3]), "body", 0.22, 0.08, edge="gold", tip_edge="gold")
        # 카나드
        surface(m, S(CANARD[0]), S(CANARD[1]), "body", 0.12, 0.05, edge="gold", tip_edge="gold")


def _old_aft_deck(m):
    """납작한 뒷판: 노즐 양옆 톱니 뒷전(위에서 보면 W)."""
    outline = [(1.9, -7.5), (1.75, -8.7), (1.15, -8.2), (0.62, -9.25), (-0.62, -9.25), (-1.15, -8.2), (-1.75, -8.7), (-1.9, -7.5)]
    rings = [[(x, y, z) for x, z in outline] for y in (-0.05, 0.12)]
    m.loft(rings, "body", smooth=False)



# ───────────── 블렌디드 외피 ─────────────
# 평면형(x ≥ 0): 앞전(기수 → 차인 → 크랭크 델타)·뒷전(노즐 → 날개 끝). (x, z)
LE_PTS = [(0.0, 9.3), (0.35, 8.75), (0.8, 7.0), (1.35, 4.6), (2.1, 1.4), (2.7, -1.0), (4.3, -3.5), (6.5, -5.4)]
TE_PTS = [(0.0, -8.9), (1.1, -8.9), (1.35, -8.45), (2.3, -7.7), (4.3, -6.9), (6.5, -6.25)]
SPAN = 6.5


def _poly(pts, x):
    x = min(max(x, pts[0][0]), pts[-1][0])
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if x0 <= x <= x1:
            k = (x - x0) / (x1 - x0) if x1 > x0 else 0.0
            return z0 + (z1 - z0) * k
    return pts[-1][1]


def le_z(x):
    return _poly(LE_PTS, abs(x))


def te_z(x):
    return _poly(TE_PTS, abs(x))


def half_span(z):
    """그 z의 외피 반폭: 앞전·뒷전 안쪽인 가장 바깥 x(이분법)."""
    lo, hi = 0.0, SPAN
    if not (te_z(0.0) <= z <= le_z(0.0)):
        return 0.0
    for _ in range(40):
        mid = (lo + hi) * 0.5
        if te_z(mid) <= z <= le_z(mid):
            lo = mid
        else:
            hi = mid
    return lo


def _bell(r):
    """부드러운 종(가장자리 기울기 0): 동체·융기 모양."""
    if r >= 1.0:
        return 0.0
    return ((1.0 + math.cos(math.pi * r)) * 0.5) ** 0.55


def _airfoil(c):
    """시위 방향 두께 분포(0 = 앞전, 1 = 뒷전, 최대 1)."""
    c = min(max(c, 0.0), 1.0)
    return math.sqrt(c) * (1.0 - c) / 0.3849


def wing_t(x):
    """날개 최대 두께(m) — 뿌리 0.42 → 끝 0.07."""
    x = abs(x)
    return 0.55 + (0.07 - 0.55) * min(x / SPAN, 1.0) ** 0.7


def keel_depth(z):
    """(레일건은 외장 포드 — 외피 용골 없음)"""
    return 0.0


def _old_keel_depth(z):
    if z < 0.6 or z > 8.3:
        return 0.0
    if z < 2.6:
        return 0.24 * _smooth01((z - 0.6) / 2.0)
    if z > 7.4:
        return 0.24 * (1.0 - 0.35 * _smooth01((z - 7.4) / 0.9))
    return 0.24


INTAKE_Z0, INTAKE_Z1 = 2.44, 2.39  # 흡입구 입(융기 앞 벽) 앞·뒤 — 거의 수직(비스듬하면 옆에서 입 안이 삼각형으로 보인다)
INTAKE_J = 4                       # 위 가운데에서 몇 칸까지 입인가(약 ±0.6m)


def hump(z):
    """등 흡입구 융기(캐노피 뒤): 앞(z 2.4)은 서고 뒤로 길게 녹아든다."""
    if z > INTAKE_Z0 - 0.01 or z < -3.8:
        return 0.0
    if z > INTAKE_Z1:
        return 0.34 * _smooth01((INTAKE_Z0 - z) / (INTAKE_Z0 - INTAKE_Z1))
    return 0.34 * _smooth01((z + 3.8) / 6.0) ** 0.6


def _smooth01(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


N_BODY, N_WING = 10, 10
CANOPY = [(7.2, 0.04, 0.04), (6.8, 0.3, 0.26), (6.0, 0.46, 0.5), (4.9, 0.5, 0.6), (3.9, 0.42, 0.48), (3.2, 0.26, 0.26), (2.85, 0.06, 0.06)]


def skin_xs(b, wb):
    """반쪽 표본 x(0 → b): 동체 폭 안은 고르게, 날개는 앞전 쪽(바깥 끝)으로 촘촘히."""
    xb = min(b, wb * 1.25)
    xs = [xb * i / N_BODY for i in range(N_BODY + 1)]
    for i in range(1, N_WING + 1):
        t = i / N_WING
        xs.append(xb + (b - xb) * math.sin(t * math.pi / 2))
    return xs


def skin_y(x, z):
    """외피 위·아래 높이(x는 반폭 안). skin_ring과 같은 식."""
    b = max(half_span(z), 0.02)
    w, ht, hb, cy = interp(FUS, z, True)
    wb = w * 1.1
    x = min(abs(x), b)
    S = _bell(x / wb)
    le, te = le_z(x), te_z(x)
    c = (le - z) / max(le - te, 0.01)
    wt = wing_t(x) * _airfoil(c) * (1.0 - 0.75 * S)
    yt = cy + ht * S + wt * 0.55 + hump(z) * _bell(x / 0.66)
    yb = cy - hb * S - wt * 0.45
    return yt, yb


def skin_ring(z):
    b = max(half_span(z), 0.02)
    w, ht, hb, cy = interp(FUS, z, True)
    wb = w * 1.1
    kd = keel_depth(z)
    hu = hump(z)
    xs = skin_xs(b, wb)
    top, bot = [], []
    for x in xs:
        r = x / wb
        S = _bell(r)
        le, te = le_z(x), te_z(x)
        c = (le - z) / max(le - te, 0.01)
        wt = wing_t(x) * _airfoil(c) * (1.0 - 0.75 * S)
        edge = 0.006 if x >= b - 1e-6 else 0.0
        yt = cy + ht * S + wt * 0.55 + hu * _bell(x / 0.66) + edge
        yb = cy - hb * S - wt * 0.45 - kd * _bell(x / 0.42) ** 0.6 - edge
        top.append((x, yt, z))
        bot.append((x, yb, z))
    # 닫힌 단면: 위(+b → 0 → −b) → 아래(−b → 0 → +b)
    ring = [p for p in reversed(top)] + [(-x, y, zz) for (x, y, zz) in top[1:]]
    ring += [(-x, y, zz) for (x, y, zz) in reversed(bot)][:-1] + bot[:-1]
    # 위 −b와 아래 −b, 위 +b와 아래 +b는 같은 자리 근처 — 그대로 둔다(가장자리 두께 edge)
    return ring


def canopy_ring(zc, wc, hc, grow=1.0, n=18, arc=None):
    """캐노피 단면: 아래 가장자리는 그 자리 외피 위(조금 묻힘), 위는 둥근 지붕. arc = (시작, 끝) 각(없으면 한 바퀴)."""
    pts = []
    count = n if arc is None else n
    for k in range(count):
        if arc is None:
            a = k * math.tau / n
        else:
            a = arc[0] + (arc[1] - arc[0]) * k / (n - 1)
        x = math.cos(a) * wc * grow
        yt, _ = skin_y(x, zc)
        lift = max(math.sin(a), 0.0) * hc * grow
        y = yt - 0.04 + lift if math.sin(a) >= 0 else yt - 0.04 + math.sin(a) * 0.06
        pts.append((x, y, zc))
    return pts


def boattail(last, cy, hw, hh, length, depth, e=4.0):
    """꼬리 보트테일: 마지막 외피 고리(last, 같은 z)의 점마다 가운데(0, cy)에서 본 방향의 초타원(반폭 hw, 반높이 hh) 점으로 오므라든다.
    돌려주는 것: (고리들, 각 고리 사이 재질, z 증가량들) — 외피 고리 뒤에 이어 붙이고 마지막은 막음(뜨거운 면).
    순서: 오므라듦(외피) 3칸 → 금색 테(바깥 → 조금 안) → 안으로 파인 노즐(검정) → 끝(뜨거운 면)."""
    z0 = last[0][2]
    def target(x, y, k=1.0):
        dx, dy = x, y - cy
        L = math.hypot(dx, dy)
        if L < 1e-6:
            return (0.0, cy)
        c, sn = dx / L, dy / L
        r = (abs(c / (hw * k)) ** e + abs(sn / (hh * k)) ** e) ** (-1.0 / e)
        return (c * r, cy + sn * r)
    rings, kinds, dzs = [], [], []
    for t in (0.35, 0.7, 1.0):
        k = _smooth01(t)
        ring = []
        for x, y, _ in last:
            tx, ty = target(x, y)
            ring.append((x + (tx - x) * k, y + (ty - y) * k, z0 - length * t))
        rings.append(ring)
        kinds.append("body")
        dzs.append(-length * t)
    ze = z0 - length
    def at(k, z):
        out = []
        for x, y, _ in last:
            tx, ty = target(x, y, k)
            out.append((tx, ty, z))
        return out
    rings.append(at(0.84, ze - 0.02))      # 금색 테(뒤를 보는 면)
    kinds.append("gold")
    rings.append(at(0.8, ze + depth))      # 안으로 파인 노즐
    kinds.append("black")
    rings.append(at(0.79, ze + depth + 0.01))
    kinds.append("hot")                    # 끝 막음 = 뜨거운 면
    return rings, kinds


def intake_duct(m, rings, zs, n):
    """흡입구 덕트: 구멍 테두리(앞 고리의 아래 가장자리 + 뒤 고리의 위 가장자리)에서 뒤로 들어가며 좁아지는 통, 끝은 압축기 면."""
    i0 = zs.index(INTAKE_Z0)
    i1 = zs.index(INTAKE_Z1)
    ks = list(range(n - INTAKE_J, n + INTAKE_J + 1))
    front = [rings[i0][k] for k in ks]              # 융기 아래(외피 위) 가장자리 — 앞
    back = [rings[i1][k] for k in reversed(ks)]     # 융기 위 가장자리 — 뒤
    mouth = front + back
    cx = 0.0
    cy = sum(p[1] for p in mouth) / len(mouth)
    rings_d = [mouth]
    # 통은 뒤로 가며 아래(동체 속)로 내려가고 폭이 좁아진다 — 융기 옆구리(좁다)를 뚫고 나오지 않게
    for d, kx, ky, dy in ((0.25, 0.9, 0.9, 0.04), (0.7, 0.72, 0.8, 0.14), (1.3, 0.55, 0.7, 0.28), (1.33, 0.53, 0.68, 0.29)):
        rings_d.append([(cx + (x - cx) * kx, cy + (y - cy) * ky - dy, min(z, INTAKE_Z1) - d) for x, y, z in mouth])
    # 안쪽 끝 = 회색 압축기 면(깊이가 보이게), 통은 앞이 조금 밝고 안으로 어둡다
    m.loft(rings_d, ["dark", "black", "black", "metal"], caps="end", smooth=True, inward=True)
    # 카울 입술: 입 위 가장자리를 따라 앞으로 0.16m 튀어나온 얇은 띠(윗면 흰색, 앞면 금색)
    arch = [rings[i1][k] for k in ks]
    bm = m.bm
    def V(p):
        return bm.verts.new(to_blender(p))
    # 입술은 아치 양 끝(외피에 닿는 아래 모서리)에서 0으로 줄어든다 — 끝이 외피 밖으로 삐져나오지 않게
    N = len(arch)
    taper = [math.sin(math.pi * i / (N - 1)) ** 0.6 for i in range(N)]
    rowA = [V(p) for p in arch]
    rowB = [V((x * (1 + 0.02 * t), y + 0.03 * t, z + 0.16 * t)) for (x, y, z), t in zip(arch, taper)]
    rowC = [V((x + (cx + (x - cx) * 0.94 - x) * t, y + (cy + (y - cy) * 0.94 - y) * t, z + 0.16 * t)) for (x, y, z), t in zip(arch, taper)]
    for a in range(len(arch) - 1):
        f = bm.faces.new((rowA[a], rowA[a + 1], rowB[a + 1], rowB[a]))
        f.smooth = True
        m.paint(f, "body")
        f = bm.faces.new((rowB[a], rowB[a + 1], rowC[a + 1], rowC[a]))
        m.paint(f, "gold")
        f = bm.faces.new((rowC[a], rowC[a + 1], rowA[a + 1], rowA[a]))
        m.paint(f, "dark")


def blended_body(m):
    # 흡입구 입(융기 앞 벽 z 2.44 → 2.2)이 한 칸이 되게 고리를 끼운다
    zs = [z for z in steps(9.22, TAIL_Z, 0.36) if abs(z - INTAKE_Z0) > 0.08 and abs(z - INTAKE_Z1) > 0.08]
    zs = sorted(zs + [INTAKE_Z0, INTAKE_Z1], reverse=True)
    rings = [skin_ring(z) for z in zs]
    n = N_BODY + N_WING     # 반쪽 표본 수 − 1 → 아래 가운데(x = 0) 점 번호 3n + 1, 위 가운데 n
    mid = 3 * n + 1
    bands = (2.2, 4.2, 6.4)

    def face_kind(i, k):
        # 흡입구 입: 융기 앞 벽(위 가운데 칸)은 검게
        if zs[i] == INTAKE_Z0 and zs[i + 1] == INTAKE_Z1:
            j = max(abs(k - n), abs(k + 1 - n))
            if j <= INTAKE_J:
                return "skip"
        return keel_kind(i, k)

    def keel_kind(i, k):
        za, zb = zs[i], zs[i + 1]
        if keel_depth(za) < 0.03 or keel_depth(zb) < 0.03:
            return None
        j = max(abs(k - mid), abs(k + 1 - mid))   # 면 양 끝 점 중 가운데에서 먼 칸
        if j > 4:
            return None
        if j == 4:
            return "gold"      # 용골 가장자리 금색 테
        if any(zb <= b <= za for b in bands):
            return "gold"      # 용골 금색 고리
        return "rail"
    lw, lht, lhb, lcy = interp(FUS, TAIL_Z)
    tail, tail_kinds = boattail(rings[-1], lcy, 0.62, 0.24, 0.75, 0.45)
    kinds = ["body"] * (len(rings) - 1) + tail_kinds
    for r in tail:
        rings.append(r)
        zs.append(r[0][2])
    m.loft(rings, kinds, caps=True, kind_at=face_kind)
    intake_duct(m, rings, zs, n)
    # 캐노피: 외피 위 금빛 유리
    rings = [canopy_ring(zc, wc, hc) for zc, wc, hc in CANOPY]
    m.loft(rings, "glass")
    # 카나드(외피에서 자라는 작은 후퇴익 — 뿌리는 외피 속)
    for sd in (1, -1):
        surface(m, (sd * 0.7, 0.14, 5.4, 3.3), (sd * 3.4, -0.1, 3.3, 2.45), "body", 0.16, 0.05, edge="gold", tip_edge="gold")


def wings(m):
    """(블렌디드 외피에 날개가 들어갔다 — 남은 건 카나드뿐, blended_body에서 만든다)"""
    return


def fuselage(m):
    blended_body(m)


def aft_deck(m):
    return


def beam(m, p0, p1, hw, hh, kind):
    """z축을 따라 놓인 네모 막대(반폭 hw, 반높이 hh)."""
    rings = []
    for p in (p0, p1):
        x, y, z = p
        rings.append([(x - hw, y - hh, z), (x + hw, y - hh, z), (x + hw, y + hh, z), (x - hw, y + hh, z)])
    m.loft(rings, kind, smooth=False)


# 레일건(검정): 배 밑 용골(커패시터·방열판) → 기수 앞으로 쌍 레일. 레일 사이 안쪽 면이 푸르게 빛난다(전하). 금색 코일 고리 셋.
RAIL_X = 0.12          # 레일 가운데(좌우 ±)
RAIL_HW, RAIL_HH = 0.05, 0.1
RAIL_Y = -1.2         # 레일·포드 가운데 높이(배 밑 파일런에 매단 외장 포드)
RAIL_Z0, RAIL_Z1 = 2.6, 6.7
POD_Z0, POD_Z1 = -3.6, 3.0     # 포드 몸통 뒤·앞(레일 끝이 EOTS보다 뒤 — 레이저 시야를 가리지 않는다)
POD_HW, POD_HH = 0.2, 0.2
KEEL_Z0, KEEL_Z1 = -2.6, 8.2
MUZZLE = (0.0, -1.2, 6.7)
# 표적 지시 창(EOTS식 각진 창 — 용골 앞 아래)
EOTS = (0.0, None, 7.35)
EOTS_R = 0.21


def bevel_box(cx, cy, z, hw, hh, b=0.35):
    """모서리를 깎은 네모 단면(8점). b = 깎는 비율."""
    bw, bh = hw * b, hh * b
    return [(cx + hw - bw, cy + hh, z), (cx - hw + bw, cy + hh, z), (cx - hw, cy + hh - bh, z), (cx - hw, cy - hh + bh, z),
            (cx - hw + bw, cy - hh, z), (cx + hw - bw, cy - hh, z), (cx + hw, cy - hh + bh, z), (cx + hw, cy + hh - bh, z)]


def railgun(m):
    if True:
        _rails(m)
        return
    # 용골: 배에 반쯤 묻힌 납작한 육각 상자. 앞은 두 레일 뿌리를 감싸며 좁아지고, 뒤는 비스듬히 닫힌다
    K = [(KEEL_Z1, 0.52, 0.2), (7.4, 0.54, 0.26), (5.5, 0.5, 0.3), (2.0, 0.48, 0.3), (-1.0, 0.44, 0.26), (KEEL_Z0, 0.3, 0.12)]
    rings = []
    for z, hw, hh in K:
        cy = min(RAIL_Y, bottom_y(0.0, z) - hh + 0.12)
        rings.append(bevel_box(0.0, cy, z, hw, hh, 0.4))
    m.loft(rings, ["rail", "railside", "railside", "railside", "railside"], smooth=False)
    # 용골 옆 방열판(얇은 판 5장씩)과 금색 띠
    for sd in (1, -1):
        for i in range(5):
            z = 1.0 + i * 0.55
            cy = min(RAIL_Y, bottom_y(0.0, z) - 0.3 + 0.12)
            beam(m, (sd * 0.52, cy, z - 0.04), (sd * 0.52, cy, z + 0.04), 0.06, 0.2, "rail")
        for z in (5.0, 7.0):
            cy = min(RAIL_Y, bottom_y(0.0, z) - 0.3 + 0.12)
            rings = [bevel_box(0.0, cy, zz, 0.53 if z < 7 else 0.55, 0.31, 0.4) for zz in (z - 0.08, z + 0.08)]
            m.loft(rings, "gold", smooth=False)
    # 용골 옆 아래 금색 줄(앞뒤로 길게)
    for sd in (1, -1):
        for z0, z1 in ((-1.6, 0.6), (3.8, 7.2)):
            cy0 = min(RAIL_Y, bottom_y(0.0, z0) - 0.26 + 0.12) - 0.2
            cy1 = min(RAIL_Y, bottom_y(0.0, z1) - 0.3 + 0.12) - 0.2
            rings = [[(sd * (hw + 0.012), cy + 0.03, z), (sd * (hw - 0.01), cy + 0.03, z), (sd * (hw - 0.01), cy - 0.03, z), (sd * (hw + 0.012), cy - 0.03, z)]
                     for z, cy, hw in ((z0, cy0, 0.45), (z1, cy1, 0.5))]
            m.loft(rings, "gold", smooth=False)
    # 쌍 레일: 모서리 깎은 상자. 바깥·위아래 = 검정, 안쪽 면 = 푸른 빛(전하). 끝은 비스듬히 깎는다
    for sd in (1, -1):
        x = sd * RAIL_X
        def rail_ring(z, k=1.0):
            return bevel_box(x, RAIL_Y, z, RAIL_HW * k, RAIL_HH * k, 0.35)
        rings = [rail_ring(RAIL_Z0), rail_ring(RAIL_Z1 - 0.35), rail_ring(RAIL_Z1, 0.7)]
        # 안쪽 면(바깥쪽이 sd>0이면 −x 면)이 빛나게: bevel_box 점 순서 2,3(−x) / 6,7(+x)
        inner = [2] if sd > 0 else [6]
        around = ["glow" if (k in inner) else "rail" for k in range(8)]
        around = ["blue" if a == "glow" else a for a in around]
        m.loft(rings, "rail", smooth=False, around=around)
    # 금색 코일 고리 셋: 두 레일을 감싸는 네모 틀(위·아래 가로대 + 옆 기둥)
    for z in (9.5, 10.25, 10.95):
        span = RAIL_X + RAIL_HW + 0.06
        top = RAIL_Y + RAIL_HH + 0.05
        bot = RAIL_Y - RAIL_HH - 0.05
        for y in (top, bot):
            rings = [[(span, y + 0.05, zz), (-span, y + 0.05, zz), (-span, y - 0.05, zz), (span, y - 0.05, zz)] for zz in (z - 0.06, z + 0.06)]
            m.loft(rings, "gold", smooth=False)
        for sd in (1, -1):
            beam(m, (sd * span, (top + bot) / 2, z - 0.06), (sd * span, (top + bot) / 2, z + 0.06), 0.05, (top - bot) / 2 + 0.05, "gold")
    # 레일 사이 바닥 빛줄(전하 흐름)
    rings = [[(-0.08, RAIL_Y - 0.02, z), (0.08, RAIL_Y - 0.02, z), (0.08, RAIL_Y + 0.02, z), (-0.08, RAIL_Y + 0.02, z)] for z in (RAIL_Z0 + 0.4, RAIL_Z1 - 0.3)]
    m.loft(rings, "blue", smooth=False)


def _rails(m):
    """외장 레일건: 배 밑 파일런 → 검은 포드(커패시터, 금색 띠·방열 살) → 앞으로 쌍 레일 + 금색 코일 고리 + 빛줄."""
    # 포드 몸통: 뒤는 뾰족하게 닫히고 앞은 레일 뿌리를 감싸며 좁아진다
    P = [(POD_Z0, 0.25), (POD_Z0 + 0.6, 0.8), (POD_Z0 + 1.3, 1.0), (POD_Z1 - 0.7, 1.0), (POD_Z1 - 0.2, 0.86), (POD_Z1, 0.72)]
    rings = [bevel_box(0.0, RAIL_Y, z, POD_HW * k, POD_HH * k, 0.38) for z, k in P]
    m.loft(rings, ["railside", "rail", "rail", "rail", "railside"], smooth=False)
    for z in (-2.2, 0.6):
        rings = [bevel_box(0.0, RAIL_Y, zz, POD_HW * 1.04, POD_HH * 1.04, 0.38) for zz in (z - 0.07, z + 0.07)]
        m.loft(rings, "gold", smooth=False)
    # 방열 살(포드 옆)
    for sd in (1, -1):
        for i in range(5):
            z = -1.4 + i * 0.4
            beam(m, (sd * (POD_HW + 0.01), RAIL_Y, z - 0.03), (sd * (POD_HW + 0.01), RAIL_Y, z + 0.03), 0.02, POD_HH * 0.7, "railside")
    # 파일런: 배 → 포드 위(앞뒤 비스듬한 날)
    pz0, pz1 = -2.9, 1.8
    top0 = skin_y(0.0, pz0)[1] + 0.08
    top1 = skin_y(0.0, pz1)[1] + 0.08
    ytop = RAIL_Y + POD_HH * 0.8
    sect = []
    for y in (ytop, None):
        if y is None:
            sect.append([(0.05, top0, pz0 + 0.5), (-0.05, top0, pz0 + 0.5), (-0.05, top1, pz1 + 0.6), (0.05, top1, pz1 + 0.6)])
        else:
            sect.append([(0.05, y, pz0), (-0.05, y, pz0), (-0.05, y, pz1), (0.05, y, pz1)])
    m.loft(sect, "body", smooth=False)
    _rails_only(m)


def _rails_only(m):
    """쌍 레일 + 금색 코일 고리 + 빛줄."""
    for sd in (1, -1):
        x = sd * RAIL_X
        def rail_ring(z, k=1.0):
            return bevel_box(x, RAIL_Y, z, RAIL_HW * k, RAIL_HH * k, 0.35)
        rings = [rail_ring(RAIL_Z0, 0.9), rail_ring(RAIL_Z0 + 0.6), rail_ring(RAIL_Z1 - 0.35), rail_ring(RAIL_Z1, 0.7)]
        inner = [2] if sd > 0 else [6]
        around = ["blue" if (k in inner) else "rail" for k in range(8)]
        m.loft(rings, "rail", smooth=False, around=around)
    for z in (5.0, 5.9):
        span = RAIL_X + RAIL_HW + 0.03
        top = RAIL_Y + RAIL_HH + 0.03
        bot = RAIL_Y - RAIL_HH - 0.03
        for y in (top, bot):
            rings = [[(span, y + 0.03, zz), (-span, y + 0.03, zz), (-span, y - 0.03, zz), (span, y - 0.03, zz)] for zz in (z - 0.04, z + 0.04)]
            m.loft(rings, "gold", smooth=False)
        for sd in (1, -1):
            beam(m, (sd * span, (top + bot) / 2, z - 0.04), (sd * span, (top + bot) / 2, z + 0.04), 0.03, (top - bot) / 2 + 0.03, "gold")
    rings = [[(-0.05, RAIL_Y - 0.015, z), (0.05, RAIL_Y - 0.015, z), (0.05, RAIL_Y + 0.015, z), (-0.05, RAIL_Y + 0.015, z)] for z in (RAIL_Z0 + 0.3, RAIL_Z1 - 0.2)]
    m.loft(rings, "blue", smooth=False)


def sensor(m):
    """표적 지시 창(EOTS식): 용골 아래 붙은 각진 육각 받침 + 아래로 비스듬한 납작 면 유리(면마다 각이 진다). 앞쪽에 금색 테."""
    cx, _, cz = EOTS
    R = EOTS_R
    n = 6
    base_y = skin_y(0.0, cz)[1] + 0.08   # 기수 바닥에 묻힌다
    cy = base_y - 0.24
    def ring(y, r, dz=0.0, stretch=1.35):
        return [(cx + math.cos(a + math.pi / n) * r, y, cz + dz + math.sin(a + math.pi / n) * r * stretch) for a in (k * math.tau / n for k in range(n))]
    # 받침(검정 테) → 유리 면 둘(아래로 좁아지며 앞으로 기운다)
    rings = [ring(base_y, R * 1.15), ring(cy + 0.06, R * 1.1), ring(cy + 0.02, R * 1.02, 0.03), ring(cy - 0.12, R * 0.78, 0.1), ring(cy - 0.2, R * 0.42, 0.16)]
    m.loft(rings, ["body", "gold", "eots", "eots"], smooth=False)


def details(m):
    """디테일: 캐노피 틀, 카나드 뿌리 덮개, 날개 끝 갈라지는 방향타, 노즐 판, 안테나, 등 센서, 조종면 경첩 덮개."""
    # 캐노피 금색 틀(앞·가운데·뒤 고리)
    C = CANOPY
    for zc in (5.9, 4.7, 3.6):
        w, h = interp([(c[0], c[1], c[2]) for c in C], zc, False)
        # 바깥 틀(유리보다 2cm 위) → 안쪽 끝(유리 속)으로 닫힌 띠
        # 유리 면을 따라 1cm 위 — 띠 앞뒤 끝도 그 자리 유리 높이를 쓴다(앞 유리가 기울어 틀이 뜨지 않게)
        outer = []
        for zz in (zc - 0.05, zc + 0.05):
            wz, hz = interp([(c[0], c[1], c[2]) for c in C], zz, False)
            outer.append([(x, y + 0.012, zz) for x, y, _ in canopy_ring(zz, wz, hz, 1.01, 12, (0.0, math.pi))])
        m.loft(outer, "gold", caps=False)
    # 안테나 날 둘(등·배 — 작게 뒤로 젖힘)
    for x, z, up in ((0.0, -4.6, 1), (0.55, -6.2, -1)):
        yt, yb = skin_y(x, z)
        y = yt - 0.02 if up > 0 else yb + 0.02
        rings = [[(x - 0.015, y, z + 0.18), (x + 0.015, y, z + 0.18), (x + 0.015, y, z - 0.18), (x - 0.015, y, z - 0.18)],
                 [(x - 0.01, y + up * 0.32, z - 0.08), (x + 0.01, y + up * 0.32, z - 0.08), (x + 0.01, y + up * 0.32, z - 0.24), (x - 0.01, y + up * 0.32, z - 0.24)]]
        m.loft(rings, "antenna", smooth=False)
    # 등 센서 돔(납작) — 흡입구 뒤
    z = -2.9
    y = top_y(0.0, z)
    rings = [se_ring(0.0, y - 0.02, zz, 0.18 * k, 0.1 * k, 0.02, 2.0, 12) for zz, k in ((z + 0.3, 0.2), (z + 0.15, 1.0), (z - 0.15, 1.0), (z - 0.3, 0.2))]
    m.loft(rings, "eots")
    # 조종면 경첩 덮개(날개 아래, 뒷전 앞 — 날개마다 둘)
    for sd in (1, -1):
        for x in (2.9, 4.6):
            zte = wing_edge(x, 3)
            yb = skin_y(sd * x, zte + 0.5)[1]
            rings = [se_ring(sd * x, yb + 0.01, zz, 0.06 * k, 0.01, 0.05 * k, 2.0, 8) for zz, k in ((zte + 0.75, 0.3), (zte + 0.55, 1.0), (zte + 0.05, 1.0), (zte - 0.08, 0.5))]
            m.loft(rings, "body")


def uav_docks(m):
    """날개 밑 UAV 거치대(짧은 받침 둘씩) — 거치대 자리는 DOCKS."""
    for x, y, z in DOCKS:
        for dz in (-0.35, 0.35):
            beam(m, (x, -0.06, z + dz - 0.1), (x, -0.06, z + dz + 0.1), 0.05, 0.12, "metal")


# UAV 거치대(날개 밑, UAV 원점 자리) — 빌더가 DockedPods를 이 자리에 둔다
DOCKS = [(2.9, -0.33, -4.0), (-2.9, -0.33, -4.0), (4.6, -0.3, -5.4), (-4.6, -0.3, -5.4)]
UAV_DOCK_SCALE = 0.6


# UAV(원점 = 가운데, 앞 +z): 폭 3.1m, 길이 1.8m. 평면형(x ≥ 0)
U_LE = [(0.0, 0.95), (0.22, 0.62), (0.55, 0.18), (1.55, -0.5)]
U_TE = [(0.0, -0.98), (0.14, -0.98), (0.32, -0.68), (0.62, -0.8), (1.05, -0.6), (1.55, -0.66)]
U_SPAN = 1.55
U_FUS = [(0.95, 0.02, 0.02, 0.02, 0.0), (0.7, 0.15, 0.09, 0.07, 0.0), (0.3, 0.27, 0.15, 0.12, 0.0), (-0.2, 0.29, 0.15, 0.12, 0.0),
         (-0.6, 0.23, 0.11, 0.09, 0.0), (-0.84, 0.15, 0.07, 0.06, 0.0), (-0.97, 0.1, 0.045, 0.04, 0.0)]
U_LENS = (0.0, None, 0.3)     # 배 밑 레이저 포탑(빌더: TurretYaw 자리)


def _u_le(x):
    return _poly(U_LE, abs(x))


def _u_te(x):
    return _poly(U_TE, abs(x))


def _u_half(z):
    if not (_u_te(0.0) <= z <= _u_le(0.0)):
        return 0.0
    lo, hi = 0.0, U_SPAN
    for _ in range(36):
        mid = (lo + hi) * 0.5
        if _u_te(mid) <= z <= _u_le(mid):
            lo = mid
        else:
            hi = mid
    return lo


def _u_skin(x, z, b=None):
    b = _u_half(z) if b is None else b
    w, ht, hb, cy = interp(U_FUS, z, True)
    wb = w * 1.15
    x = min(abs(x), max(b, 0.01))
    S = _bell(x / wb)
    le, te = _u_le(x), _u_te(x)
    c = (le - z) / max(le - te, 0.01)
    wt = (0.09 + (0.025 - 0.09) * min(x / U_SPAN, 1.0)) * _airfoil(c) * (1.0 - 0.7 * S)
    return cy + ht * S + wt * 0.55, cy - hb * S - wt * 0.45


def uav():
    """요격 UAV: LUNA를 닮은 작은 블렌디드 무미익기(폭 3.1m) — 톱니 뒷전, 배 밑 레이저 포탑(금 테 + 어두운 유리 구, 렌즈는 빛),
    등 앞 흡입 틈(그림)·꼬리 배기 틈, 아래로 꺾인 금색 날개 끝. 텍스처는 아틀라스의 UAV 칸."""
    m = Mesh((uav_uv_top, uav_uv_bottom, uav_uv_side))
    NB, NW = 4, 5
    rings = []
    for z in steps(0.93, -0.96, 0.135):
        b = max(_u_half(z), 0.01)
        w, ht, hb, cy = interp(U_FUS, z, True)
        xb = min(b, w * 1.15 * 1.2)
        xs = [xb * i / NB for i in range(NB + 1)] + [xb + (b - xb) * math.sin(i / NW * math.pi / 2) for i in range(1, NW + 1)]
        top, bot = [], []
        for x in xs:
            yt, yb = _u_skin(x, z, b)
            e = 0.004 if x >= b - 1e-6 else 0.0
            top.append((x, yt + e, z))
            bot.append((x, yb - e, z))
        ring = [p for p in reversed(top)] + [(-x, y, zz) for (x, y, zz) in top[1:]]
        ring += [(-x, y, zz) for (x, y, zz) in reversed(bot)][:-1] + bot[:-1]
        rings.append(ring)
    w, ht, hb, cy = interp(U_FUS, -0.96)
    tail, tail_kinds = boattail(rings[-1], cy, 0.075, 0.03, 0.07, 0.06)
    m.loft(rings + tail, ["body"] * (len(rings) - 1) + tail_kinds, caps=True)
    # 아래로 꺾인 날개 끝(흰 판 + 금색 앞전)
    for sd in (1, -1):
        x0 = U_SPAN - 0.02
        le, te = _u_le(x0), _u_te(x0)
        yt, yb = _u_skin(x0 - 0.05, (le + te) / 2)
        y0 = (yt + yb) / 2
        drop, out = 0.12, 0.06
        rings = [[(sd * x0, y0, le - 0.02), (sd * x0, y0 + 0.012, (le + te) / 2), (sd * x0, y0, te + 0.02), (sd * x0, y0 - 0.012, (le + te) / 2)],
                 [(sd * (x0 + out), y0 - drop, le - 0.08), (sd * (x0 + out), y0 - drop + 0.008, (le + te) / 2 - 0.03), (sd * (x0 + out), y0 - drop, te + 0.04), (sd * (x0 + out), y0 - drop - 0.008, (le + te) / 2 - 0.03)]]
        m.loft(rings, "body", smooth=False, around=["gold", "body", "body", "gold"])   # 흰 판에 앞전만 금색
    # 배 밑 레이저 포탑: 금 테 받침 → 어두운 유리 구 → 앞쪽 빛나는 렌즈
    cz = U_LENS[2]
    yb = _u_skin(0.0, cz)[1]
    n = 12
    def ring(y, r):
        return [(math.cos(a) * r, y, cz + math.sin(a) * r) for a in (k * math.tau / n for k in range(n))]
    # 받침: 외피에서 내려오는 낮은 금 테(유리 구 허리를 감싼다 — 뜨지 않게)
    m.loft([ring(yb + 0.04, 0.15), ring(yb - 0.03, 0.135), ring(yb - 0.035, 0.105)], ["gold", "gold"], caps=False)
    R = 0.105
    cy0 = yb - 0.035
    rings = []
    for i in range(6):
        t = -math.pi / 2 + i * math.pi / 5
        rings.append([(math.cos(a) * math.cos(t) * R, cy0 + math.sin(t) * R, cz + math.sin(a) * math.cos(t) * R) for a in (k * math.tau / n for k in range(n))])
    rings[0] = [(0.0, cy0 - R, cz)] * n
    m.loft(rings, ["lens"] * 5, caps=False)
    rings = [[(math.cos(a) * r, cy0 - 0.03 + dy, cz + R * 0.92 + dz) for a in (k * math.tau / 10 for k in range(10))] for r, dy, dz in ((0.045, 0.0, 0.0), (0.035, 0.0, 0.012))]
    m.loft(rings, ["glow"], caps="end", smooth=False)
    return m.finish("Luna_Uav")


def uav_paint(P):
    """UAV 칸: 금색 앞전, 패널 선, 등 흡입 틈, 작은 초승달, 항법등, 번호."""
    for s in (1, -1):
        for reg in ("utop", "ubot"):
            xs = [0.25 + (U_SPAN - 0.25) * i / 30 for i in range(31)]
            outer = [(s * x, _u_le(x) - 0.005) for x in xs]
            inner = [(s * x, _u_le(x) - 0.05) for x in reversed(xs)]
            P.fill(reg, outer + inner, GOLD)
            # 조종면(뒷전 엘레본) 선과 날개 패널
            P.line(reg, (s * 0.35, _u_te(0.35) + 0.16), (s * 1.5, _u_te(1.5) + 0.14), w=1.2)
            for x in (0.35, 0.9, 1.5):
                P.line(reg, (s * x, _u_te(x) - 0.01), (s * x, _u_te(x) + 0.16), w=1.2)
            P.line(reg, (s * 0.3, 0.35), (s * 0.3, -0.55), w=1.2)
            P.rect(reg, s * 0.55, -0.05, s * 0.8, -0.3, w=1.1)
            P.disk(reg, (s * 1.5, _u_le(1.5) - 0.06), 0.02, (0.2, 0.85, 0.35) if s > 0 else (0.9, 0.12, 0.1))
    # 위: 등 흡입 틈(어두운 반달)·가운데 점검판·작은 초승달
    P.fill("utop", [(-0.12, 0.42), (0.12, 0.42), (0.1, 0.36), (-0.1, 0.36)], (0.05, 0.05, 0.06))
    P.fill("utop", [(-0.13, 0.43), (0.13, 0.43), (0.13, 0.45), (-0.13, 0.45)], GOLD)
    P.rect("utop", -0.12, 0.2, 0.12, -0.25, w=1.1)
    for z in (0.15, 0.1, 0.05, 0.0):
        P.line("utop", (-0.08, z), (0.08, z), w=1.1, k=0.7, hl=False)
    P.crescent("utop", (0.0, -0.45), 0.09, GOLD, phase=0.45)
    # 아래: 포탑 둘레·점검판
    P.rect("ubot", -0.2, 0.55, 0.2, 0.05, w=1.1)
    P.rect("ubot", -0.14, -0.2, 0.14, -0.6, w=1.1)


# ───────────── 텍스처 ─────────────# ───────────── 텍스처 ─────────────

def _box_blur(a, r):
    if r <= 0:
        return a
    for ax in (0, 1):
        c = np.cumsum(np.pad(a, [(r + 1, r) if i == ax else (0, 0) for i in range(2)], mode='edge'), axis=ax)
        a = (np.take(c, range(2 * r + 1, c.shape[ax]), axis=ax) - np.take(c, range(0, c.shape[ax] - 2 * r - 1), axis=ax)) / (2 * r + 1)
    return a


class Painter:
    def __init__(self, img):
        self.img = img
        self.size = img.shape[0]
        self.lines = np.zeros(img.shape[:2], dtype=np.float32)
        self.hl = np.zeros(img.shape[:2], dtype=np.float32)
        self.dirt = np.zeros(img.shape[:2], dtype=np.float32)
        self.uvf = {"top": uv_top, "bottom": uv_bottom, "side": uv_side, "utop": uav_uv_top, "ubot": uav_uv_bottom, "uside": uav_uv_side}
        self.mpp = 21.0 / (0.56 * self.size)

    def px(self, region, a, b):
        u, v = self.uvf[region](a, b)
        return u * self.size, v * self.size

    def _seg(self, x0, y0, x1, y1, w):
        pad = w + 2
        c0, c1 = max(int(min(x0, x1) - pad), 0), min(int(max(x0, x1) + pad) + 1, self.size)
        r0, r1 = max(int(min(y0, y1) - pad), 0), min(int(max(y0, y1) + pad) + 1, self.size)
        if c0 >= c1 or r0 >= r1:
            return None, None
        yy, xx = np.mgrid[r0:r1, c0:c1].astype(np.float32) + 0.5
        dx, dy = x1 - x0, y1 - y0
        L2 = dx * dx + dy * dy
        tt = np.clip(((xx - x0) * dx + (yy - y0) * dy) / L2, 0, 1) if L2 > 1e-9 else np.zeros_like(xx)
        d = np.hypot(xx - (x0 + tt * dx), yy - (y0 + tt * dy))
        return (slice(r0, r1), slice(c0, c1)), np.clip(w * 0.5 + 0.5 - d, 0, 1)

    def line(self, region, p0, p1, w=1.5, k=1.0, hl=True):
        x0, y0 = self.px(region, *p0)
        x1, y1 = self.px(region, *p1)
        sl, c = self._seg(x0, y0, x1, y1, w)
        if sl is None:
            return
        np.maximum(self.lines[sl], c * k, out=self.lines[sl])
        if hl:
            sl2, c2 = self._seg(x0 + 1.4, y0 - 1.4, x1 + 1.4, y1 - 1.4, w * 0.8)
            if sl2 is not None:
                np.maximum(self.hl[sl2], c2, out=self.hl[sl2])
        np.maximum(self.dirt[sl], c * 0.3 * k, out=self.dirt[sl])

    def poly(self, region, pts, w=1.5, k=1.0, closed=True):
        seq = list(zip(pts, pts[1:] + (pts[:1] if closed else [])))
        for a, b in seq:
            self.line(region, a, b, w, k)

    def rect(self, region, a0, b0, a1, b1, w=1.5):
        self.poly(region, [(a0, b0), (a1, b0), (a1, b1), (a0, b1)], w)

    def _cover(self, region, poly, ss=3):
        xs = [self.px(region, *q) for q in poly]
        c0, c1 = max(int(min(x for x, _ in xs)) - 1, 0), min(int(max(x for x, _ in xs)) + 2, self.size)
        r0, r1 = max(int(min(y for _, y in xs)) - 1, 0), min(int(max(y for _, y in xs)) + 2, self.size)
        if c0 >= c1 or r0 >= r1:
            return None, None
        yy, xx = np.mgrid[r0 * ss:r1 * ss, c0 * ss:c1 * ss].astype(np.float32)
        yy, xx = (yy + 0.5) / ss, (xx + 0.5) / ss
        inside = np.zeros(xx.shape, dtype=bool)
        for (xa, ya), (xb, yb) in zip(xs, xs[1:] + xs[:1]):
            cond = (ya > yy) != (yb > yy)
            xi = xa + (yy - ya) * (xb - xa) / np.where(yb - ya == 0, 1e-9, yb - ya)
            inside ^= cond & (xx < xi)
        cov = inside.reshape(r1 - r0, ss, c1 - c0, ss).mean(axis=(1, 3)).astype(np.float32)
        return (slice(r0, r1), slice(c0, c1)), cov

    def fill(self, region, poly, col, alpha=1.0):
        sl, cov = self._cover(region, poly)
        if sl is None:
            return
        a = (cov * alpha)[..., None]
        self.img[sl] = self.img[sl] * (1 - a) + np.array(col, dtype=np.float32) * a

    def grime(self, region, poly, k=0.5):
        sl, cov = self._cover(region, poly, 2)
        if sl is not None:
            np.maximum(self.dirt[sl], cov * k, out=self.dirt[sl])

    def disk(self, region, c, r, col, n=48):
        self.fill(region, [(c[0] + math.cos(a) * r, c[1] + math.sin(a) * r) for a in (i * math.tau / n for i in range(n))], col)

    def crescent(self, region, c, r, col, phase=0.42, tilt=0.0, n=40):
        """초승달: 바깥 원호 + 안쪽 원호(가운데를 phase × r만큼 옆으로 옮긴 원)."""
        cx, cy = c
        ox, oy = math.cos(tilt) * r * phase, math.sin(tilt) * r * phase
        # 두 원의 교점 각(바깥 원 기준)
        d = r * phase
        a = math.acos(min(d / (2 * r), 1.0))
        base = tilt
        outer = [(cx + math.cos(base + a + t * (math.tau - 2 * a) / n) * r, cy + math.sin(base + a + t * (math.tau - 2 * a) / n) * r) for t in range(n + 1)]
        # 안쪽 원: 가운데 (cx+ox, cy+oy), 교점에서 교점까지 바깥 원 반대쪽으로
        b = math.pi - a
        inner = [(cx + ox + math.cos(base + math.pi + b - t * 2 * b / n) * r, cy + oy + math.sin(base + math.pi + b - t * 2 * b / n) * r) for t in range(n + 1)]
        self.fill(region, outer + inner, col)

    def stencil(self, region, a, b, rows=2, width=0.32):
        for i in range(rows):
            ww = width * (1.0 - 0.25 * (i % 2))
            self.fill(region, [(a, b - i * 0.06), (a + ww, b - i * 0.06), (a + ww, b - i * 0.06 - 0.03), (a, b - i * 0.06 - 0.03)], (0.45, 0.42, 0.36), 0.8)

    def finish(self):
        img = self.img
        dirt = _box_blur(self.dirt, max(int(0.05 / self.mpp), 1))
        img *= (1 - 0.1 * np.clip(dirt, 0, 1))[..., None]
        img *= (1 - 0.38 * np.clip(self.lines, 0, 1))[..., None]
        img += (0.05 * np.clip(self.hl, 0, 1) * (1 - np.clip(self.lines, 0, 1)))[..., None]


def panel_tone(img, rng):
    """패널마다 아주 조금 다른 흰색(칸마다 바랜 정도가 다르다)."""
    N = img.shape[0]
    zb = np.array(sorted([7.8, 7.1, 5.6, 4.0, 2.8, 0.8, -1.0, -2.6, -4.1, -5.6, -7.6]))
    xb = np.array(sorted([0.0, 0.45, 1.1, 2.2, 3.6, 5.0, 6.3, -0.45, -1.1, -2.2, -3.6, -5.0, -6.3]))
    yb = np.array([-1.0, -0.3, 0.0, 0.3, 1.0])
    tab = (1 + 0.02 * (rng.random(4096) - 0.5)).astype(np.float32)
    regions = (("top", 0.0, 0.5, 0.0, 0.6), ("bottom", 0.5, 1.0, 0.0, 0.6), ("side", 0.0, 0.6, 0.6, 0.78))
    for reg, u0, u1, v0, v1 in regions:
        c0, c1, r0, r1 = int(u0 * N), int(u1 * N), int(v0 * N), int(v1 * N)
        vv, uu = np.mgrid[r0:r1, c0:c1].astype(np.float32)
        u, v = (uu + 0.5) / N, (vv + 0.5) / N
        if reg == "side":
            a, b = (u - 0.02) / 0.56 * 21.0 - 10.5, (v - 0.62) / 0.14 * 4.0 - 2.0
            i, j = np.digitize(a, zb), np.digitize(b, yb)
        else:
            a = (0.25 - u) * 32.0 if reg == "top" else (0.75 - u) * 32.0
            b = (v - 0.02) / 0.56 * 21.0 - 10.5
            i, j = np.digitize(b, zb), np.digitize(a, xb)
        f = tab[(i * 31 + j * 7 + (0 if reg == "top" else 977 if reg == "bottom" else 1999)) % 4096]
        img[r0:r1, c0:c1, :3] *= f[..., None]


def digits(P, region, x, z, h, text, col, along_z=True):
    """간단한 7획 숫자(금색 일련번호) — 텍스처에 직접."""
    SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcfgd"}
    w = h * 0.5
    t = h * 0.12
    for ci, ch in enumerate(text):
        ox = ci * (w + h * 0.25)
        segs = {"a": (0, h, w, h), "b": (w, h, w, h / 2), "c": (w, h / 2, w, 0), "d": (0, 0, w, 0), "e": (0, 0, 0, h / 2), "f": (0, h / 2, 0, h), "g": (0, h / 2, w, h / 2)}
        for sg in SEG.get(ch, ""):
            x0, y0, x1, y1 = segs[sg]
            sgn = -1.0 if along_z else 1.0
            ha, hb_ = x + sgn * (ox + x0), x + sgn * (ox + x1)
            va, vb = z + y0, z + y1
            if y0 == y1:
                pts = [(ha, va - t / 2), (hb_, vb - t / 2), (hb_, vb + t / 2), (ha, va + t / 2)]
            else:
                pts = [(ha - t / 2, va), (hb_ - t / 2, vb), (hb_ + t / 2, vb), (ha + t / 2, va)]
            P.fill(region, pts, col)


def keel_paint(P):
    """외피 용골(레일건 커패시터)을 검게: 아래 면 띠 + 옆 아래 띠, 금색 테두리 줄."""
    # 용골 검정은 외피 면 재질(blended_body의 keel_kind)로 칠한다 — 투영 텍스처로는 옆·아래 경계가 계단처럼 깨진다
    # 등 흡입구 입은 외피 면 재질(blended_body의 face_kind)로 칠한다


def detail_paint(P):
    keel_paint(P)
    """더 그리는 것: 날개 점검창·경고 테두리·환기구 살·항법등·접지 경고·일련번호·차인 금색 선."""
    for s in (1, -1):
        for reg in ("top", "bottom"):
            # 날개 점검창(작은 네모)과 연료 칸 이음매
            for x, z in ((2.6, -3.0), (3.4, -4.4), (4.4, -5.4), (5.4, -5.9)):
                P.rect(reg, s * (x - 0.22), z + 0.25, s * (x + 0.22), z - 0.25)
            P.line(reg, (s * 2.25, -1.4), (s * 4.3, -4.0))
            P.line(reg, (s * 4.3, -4.0), (s * 6.2, -5.7))
            # 항법등(오른쪽 초록, 왼쪽 빨강) 날개 끝 앞전
            P.disk(reg, (s * 6.35, wing_edge(6.35, 2) - 0.12), 0.06, (0.2, 0.85, 0.35) if s > 0 else (0.9, 0.12, 0.1))
            # 앞전 금색(외피 앞전 안쪽 띠 — 기수 끝은 빼고 차인부터 날개 끝까지)
            xs = [0.5 + (SPAN - 0.08 - 0.5) * i / 60 for i in range(61)]
            outer = [(s * x, le_z(x) - 0.01) for x in xs]
            inner = [(s * x, le_z(x) - 0.16) for x in reversed(xs)]
            P.fill(reg, outer + inner, GOLD)
        # 엔진 위 환기구 살(뒤쪽 양옆)
        for i in range(6):
            z = -5.2 - i * 0.16
            P.line("top", (s * 0.6, z), (s * 0.95, z), w=2.4, k=0.9, hl=False)
        P.rect("top", s * 0.55, -5.05, s * 1.0, -6.15)
        # 밟지 마시오 점선 테두리(날개 뿌리 위)
        for (a0, b0), (a1, b1) in (((1.5, -1.6), (2.3, -1.6)), ((2.3, -1.6), (2.3, -3.6)), ((2.3, -3.6), (1.5, -3.6)), ((1.5, -3.6), (1.5, -1.6))):
            n = 8
            for i in range(0, n, 2):
                P.line("top", (s * (a0 + (a1 - a0) * i / n), b0 + (b1 - b0) * i / n), (s * (a0 + (a1 - a0) * (i + 1) / n), b0 + (b1 - b0) * (i + 1) / n), w=1.2, k=0.7, hl=False)
        # 아래: 날개 밑 거치대 둘레 테두리, 착륙장치 문
        for x, z in ((2.9, -4.0), (4.6, -5.4)):
            P.rect("bottom", s * (x - 0.15), z + 0.7, s * (x + 0.15), z - 0.7)
        P.rect("bottom", s * 0.9, -2.2, s * 1.5, -5.0)
        P.stencil("bottom", s * 0.92 if s > 0 else -1.45, -5.2)
    # 옆: 일련번호(금색) — 캐노피 앞 아래
    digits(P, "side", 7.4, -0.05, 0.28, "01", GOLD)
    # 옆: 사출 경고 삼각형·급유구
    P.fill("side", [(6.9, 0.18), (6.5, 0.18), (6.7, 0.48)], (0.95, 0.3, 0.1))
    P.fill("side", [(6.82, 0.24), (6.58, 0.24), (6.7, 0.41)], BODY_COL)
    P.rect("side", 1.2, 0.3, 0.6, -0.05)
    P.rect("side", -1.4, 0.25, -2.4, -0.1)
    P.stencil("side", 1.15, -0.12, rows=2, width=-0.5)


def make_texture():
    N = TEX
    img = np.zeros((N, N, 3), dtype=np.float32)
    img[...] = BODY_COL
    rng = np.random.default_rng(11)
    big = _box_blur(rng.random((N, N)).astype(np.float32), 40)
    big = (big - big.mean()) / max(big.std(), 1e-6)
    fine = _box_blur(rng.random((N, N)).astype(np.float32), 2)
    fine = (fine - fine.mean()) / max(fine.std(), 1e-6)
    img *= (1 + 0.012 * big + 0.005 * fine)[..., None]
    # 아랫면은 조금 어둡게(진주빛 회백)
    panel_tone(img, rng)
    P = Painter(img)
    detail_paint(P)
    uav_paint(P)

    for s in (1, -1):
        for reg in ("top", "bottom"):
            # 날개: 앞전 덮개·날개보·조종면(엘레본 둘 + 끝 갈라지는 방향타)
            P.line(reg, (s * 2.2, wing_edge(2.2, 2) - 0.5), (s * 6.3, wing_edge(6.3, 2) - 0.3))
            P.line(reg, (s * 2.2, wing_edge(2.2, 3) + 1.3), (s * 6.3, wing_edge(6.3, 3) + 0.45))
            for x in (2.2, 3.6, 5.0, 6.3):
                P.line(reg, (s * x, wing_edge(x, 3) - 0.05), (s * x, wing_edge(x, 3) + (0.9 if x < 6 else 0.4)))
            P.line(reg, (s * 2.2, wing_edge(2.2, 3) + 0.9), (s * 5.0, wing_edge(5.0, 3) + 0.75))
            P.line(reg, (s * 5.0, wing_edge(5.0, 3) + 0.6), (s * 6.3, wing_edge(6.3, 3) + 0.4))
            P.grime(reg, [(s * 2.2, wing_edge(2.2, 3) + 0.95), (s * 6.3, wing_edge(6.3, 3) + 0.45), (s * 6.3, wing_edge(6.3, 3) + 0.25), (s * 2.2, wing_edge(2.2, 3) + 0.75)], 0.5)
            # 날개 끝 금색 띠
            P.fill(reg, [(s * 5.95, wing_edge(5.95, 2) + 0.15), (s * 6.25, wing_edge(6.25, 2) + 0.15), (s * 6.25, wing_edge(6.25, 3) - 0.15), (s * 5.95, wing_edge(5.95, 3) - 0.15)], GOLD)
            # 차인-동체 이음매
            P.poly(reg, [(s * 0.45, 8.4), (s * 0.85, 6.0), (s * 1.04, 3.0), (s * 1.1, -0.5)], closed=False)
        # ── 위: 등 금색 줄 둘(기수 → 꼬리), 초승달 문양(오른쪽 날개 위 금색, 왼쪽 날개 위 금색), 점검창
        P.fill("top", [(s * 0.36, 2.8), (s * 0.44, 2.8), (s * 0.44, -8.6), (s * 0.36, -8.6)], GOLD)
        P.crescent("top", (s * 3.9, -5.0), 0.85, GOLD, phase=0.45, tilt=(0.0 if s > 0 else math.pi))
        P.rect("top", s * 0.5, 2.6, s * 0.85, 1.6)
        P.rect("top", s * 0.5, -1.0, s * 0.85, -2.6)
        P.rect("top", s * 1.2, -4.0, s * 1.8, -5.4)
        P.stencil("top", s * 0.52 if s > 0 else -0.84, 1.45)
        # ── 아래: 흡입구 옆 금색 셰브론, 무장창 테두리
        P.rect("bottom", s * 0.55, -1.0, s * 0.92, -5.0)
        P.stencil("bottom", s * 0.6 if s > 0 else -0.9, -5.2)
    # 가로 이음매
    for z in (7.8, 7.1, 2.8, 0.8, -2.6, -5.6, -7.6):
        w, _, _, _ = interp(FUS, z, True)
        for reg in ("top", "bottom"):
            P.line(reg, (-w, z), (w, z))
    # 기수 금색 띠(레일 받침 뒤)
    for reg in ("top", "bottom"):
        P.fill(reg, [(-0.75, 7.6), (0.75, 7.6), (0.75, 7.4), (-0.75, 7.4)], GOLD)
    P.fill("side", [(7.4, -1.2), (7.6, -1.2), (7.6, 1.2), (7.4, 1.2)], GOLD)
    # 옆: 이음매·금색 줄·초승달 작은 문양(캐노피 뒤)
    for z in (8.2, 3.4, 0.8, -2.6, -5.6, -7.6):
        P.line("side", (z, -0.6), (z, 0.6))
    P.line("side", (8.5, 0.0), (-8.9, 0.0))
    P.crescent("side", (2.6, 0.28), 0.22, GOLD, phase=0.45)
    P.stencil("side", 6.8, -0.25, rows=2, width=-0.45)
    P.finish()
    q = int(PATCH_SIZE * N)
    for (u, v), col in PATCH.values():
        x, y = int(u * N) - q // 2, int(v * N) - q // 2
        img[y:y + q, x:x + q, :] = col
    out = np.ones((N, N, 4), dtype=np.float32)
    out[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new(NAME + "_Albedo", N, N, alpha=False)
    im.pixels.foreach_set(out.ravel())
    im.filepath_raw = os.path.join(OUT, NAME + "_Albedo.png")
    im.file_format = 'PNG'
    im.save()
    return im


def link(me, loc=(0, 0, 0)):
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


def preview(objs, im, path):
    """미리보기: 텍스처를 입힌 재질 + 워크벤치 렌더(앞 비스듬히 위 / 위 / 옆)."""
    mat = bpy.data.materials.new("LunaPreview")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = im
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    for o in objs:
        o.data.materials.append(mat)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_cavity = True
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.35
    sc.display.shading.cavity_type = 'BOTH'
    sc.display.shading.curvature_ridge_factor = 1.0
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.16, 0.2, 0.26)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    import mathutils
    shots = {
        "front34": ((17.0, 8.0, 17.0), (0.0, -0.5, 0.0)),
        "rear34": ((-15.0, 7.0, -18.0), (0.0, -0.5, 0.0)),
        "top": ((0.0, 34.0, 0.01), (0.0, 0.0, 0.0)),
        "side": ((30.0, 0.0, 0.5), (0.0, 0.0, 0.0)),
        "below": ((10.0, -14.0, 16.0), (0.0, 0.0, 0.0)),
        "nose": ((6.0, 1.0, 15.0), (0.0, -0.3, 7.0)),
        "intake": ((3.2, 2.6, 6.5), (0.0, 0.8, 1.8)),
        "intake_side": ((3.0, 1.8, 0.5), (0.0, 0.9, 1.6)),
        "uav": ((2.6, 6.6, 18.8), (0.0, 5.0, 16.0)),
        "uav_below": ((2.2, 3.4, 18.4), (0.0, 5.0, 16.0)),
        "uav_rear": ((-2.4, 5.9, 13.0), (0.0, 5.0, 16.0)),
        "tail": ((-3.2, 1.6, -13.5), (0.0, 0.1, -8.8)),
    }
    for name, (eye_u, at_u) in shots.items():
        eye = mathutils.Vector(to_blender(eye_u))
        at = mathutils.Vector(to_blender(at_u))
        cam.location = eye
        cam.rotation_euler = (at - eye).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = path + "_" + name + ".png"
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    im = make_texture()
    body = Mesh()
    fuselage(body)
    wings(body)
    aft_deck(body)
    railgun(body)
    sensor(body)
    details(body)
    uav_docks(body)
    b = link(body.finish("Luna_Body"))
    u = link(uav(), (0, 0, 0))
    print("TRIS body", sum(len(p.vertices) - 2 for p in b.data.polygons), "uav", sum(len(p.vertices) - 2 for p in u.data.polygons))
    print("MUZZLE (unity)", (0.0, RAIL_Y, RAIL_Z1), "DESIGNATOR (unity)", (0.0, round(skin_y(0.0, EOTS[2])[1] - 0.36, 3), EOTS[2] + 0.16), "DOCKS", DOCKS)
    write_fbx(os.path.join(OUT, NAME + ".fbx"), [b, u])
    if os.environ.get("ACO_LUNA_PREVIEW"):
        # 미리보기용 UAV 사본을 날개 밑 거치대에 둔다
        docked = []
        for d in DOCKS:
            o = bpy.data.objects.new("dock", u.data)
            o.location = to_blender(d)
            o.scale = (UAV_DOCK_SCALE,) * 3
            bpy.context.scene.collection.objects.link(o)
            docked.append(o)
        u.location = to_blender((0.0, 5.0, 16.0))
        preview([b, u] + docked, im, os.environ["ACO_LUNA_PREVIEW"])
    print("DONE")
