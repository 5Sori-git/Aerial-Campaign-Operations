"""레이저 에이스 기체(가공 — 실존 기체 아님, 부위별로 실존 기체의 특징을 참고해 새로 짠 조합). 쌍발, 전진익(베르쿠트식), 수평미익·카나드 없음,
처진 긴 기수·높은 캐노피·날카로운 LERX(플랭커식), 동체와 틈을 둔 사각 흡입구·떨어진 나셀·납작한 가운데 몸통·꼬리판(톰캣식), 나셀 위 안으로 기운 쌍수직미익,
좌우 대각선 사다리꼴 뼈대 둘로 받친 대형 레이저 포드. 흰색 계열에 주황 강조색. 부품:
 - LaserAce_Body: 동체·날개·수직미익·흡입구·노즐·캐노피·트러스 받침·포드 몸통
 - LaserAce_Head: 포드 앞머리(원점 = 포드 축 위, 고정)
 - LaserAce_Lens: 렌즈 구(원점 = 렌즈 중심, 헤드의 자식 — 유니티에선 렌즈 포탑(ACO_AAGunSite)의 TurretPitch에 붙어 좌우·위아래로 돈다)
좌표: 유니티 좌표 그대로(x 오른쪽, y 위, z 앞 = 기수). 단위 m. 길이 약 24m, 폭 약 18m.
텍스처 2048: 흰 동체 면은 위·아래·옆 투영 UV(면 방향으로 고른다)로 패널 라인·점검창·주황 띠·미익 문양·그을음을 직접 칠한다. 나머지 재질은 색 칸.
실행: blender --background --factory-startup --python Tools/aco_laser_ace_model.py   (ACO_LACE_OUT으로 다른 폴더에)"""
import math
import os
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_LACE_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "LaserAce"))
NAME = "ACO_LaserAce"
TEX = 2048
BODY_COL = (0.86, 0.87, 0.85)   # 동체(흰색 계열)
ORANGE = (1.0, 0.42, 0.08)      # 강조색
# 색 칸: 텍스처 위쪽 줄(v 0.82~0.98)
PATCH = {
    "body": ((0.05, 0.9), BODY_COL),
    "light": ((0.16, 0.9), (0.3, 0.31, 0.33)),
    "pod": ((0.27, 0.9), (0.24, 0.25, 0.27)),    # 레이저 포드(짙은 회색 — 흰 동체와 대비)
    "black": ((0.38, 0.9), (0.025, 0.025, 0.03)),
    "glass": ((0.49, 0.9), (0.13, 0.2, 0.3)),
    "red": ((0.6, 0.9), ORANGE),   # 강조색(주황) — 날개 앞전·포드 띠·렌즈 테
    "metal": ((0.71, 0.9), (0.42, 0.43, 0.45)),
    "lens": ((0.82, 0.9), (1.0, 0.3, 0.2)),
    "nozzle": ((0.93, 0.9), (0.24, 0.2, 0.18)),
}
PATCH_SIZE = 0.08


# 투영 영역(유니티 좌표 → uv). 위: (x, z), 아래: (x, z), 옆: (z, y). 모두 약 4.2cm/px
def uv_top(x, z):
    return (0.25 - x / 40.0, 0.02 + (z + 11.0) / 24.0 * 0.56)


def uv_bottom(x, z):
    return (0.75 - x / 40.0, 0.02 + (z + 11.0) / 24.0 * 0.56)


def uv_side(z, y):
    return (0.02 + (z + 11.0) / 24.0 * 0.56, 0.62 + (y + 2.5) / 6.0 * 0.14)

POD_Y = 1.95      # 포드 축 높이(동체 윗면에서 약 0.8m 위)
POD_R = 0.75      # 포드 반지름
POD_Z0, POD_Z1 = -7.0, 1.9   # 포드 몸통 뒤·앞(앞에 헤드 — 캐노피 뒤)
HEAD_LEN = 0.9    # 헤드 원점에서 렌즈 중심까지
LENS_R = 0.5


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Mesh:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.body_faces = set()

    def paint(self, f, kind):
        (u, v), _ = PATCH[kind]
        for loop in f.loops:
            loop[self.uv].uv = (u, v)
        if kind == "body":
            self.body_faces.add(f)
        else:
            self.body_faces.discard(f)

    def project_body(self):
        """흰 동체 면: 면 방향에 따라 위·아래·옆 투영 UV(칠한 무늬가 면을 따라 붙는다)."""
        for f in self.body_faces:
            n = f.normal
            nx, ny = -n.x, n.z
            for loop in f.loops:
                c = loop.vert.co
                x, y, z = -c.x, c.z, -c.y
                if abs(ny) >= abs(nx) * 0.8:
                    loop[self.uv].uv = uv_top(x, z) if ny > 0 else uv_bottom(x, z)
                else:
                    loop[self.uv].uv = uv_side(z, y)

    def loft(self, rings, kinds, caps=True, smooth=True, around=None):
        """rings: [[(x, y, z)...]] 같은 점 수. kinds: 고리 사이마다 재질(문자열이면 모두 같다). around: 둘레 칸마다 재질(있으면 kinds 대신)."""
        vs = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = len(rings[0])
        for i in range(len(vs) - 1):
            kind = kinds if isinstance(kinds, str) else kinds[i]
            for k in range(n):
                m = (k + 1) % n
                f = self.bm.faces.new((vs[i][k], vs[i][m], vs[i + 1][m], vs[i + 1][k]))
                f.smooth = smooth
                self.paint(f, around[k] if around else kind)
        if caps:
            ends = ((vs[0], kinds if isinstance(kinds, str) else kinds[0]), (vs[-1], kinds if isinstance(kinds, str) else kinds[-1]))
            if caps == "end":
                ends = ends[1:]
            for ring, kind in ends:
                f = self.bm.faces.new(ring)
                self.paint(f, kind)

    def finish(self, name):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.project_body()
        for e in self.bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(40):
                e.smooth = False
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def ring_shape(cx, cy, z, w, h, chine=0.45, n=16, top=1.0, bottom=1.0):
    """둥근 마름모꼴 단면(옆 모서리 = 차인). w, h = 반폭·반높이."""
    pts = []
    for k in range(n):
        a = k * math.tau / n
        c, s = math.cos(a), math.sin(a)
        e = 0.62   # 각진 정도(작을수록 각짐)
        x = math.copysign(abs(c) ** e, c) * w
        y = math.copysign(abs(s) ** (e * 1.4), s) * h * (top if s > 0 else bottom)
        pts.append((cx + x, cy + y, z))
    return pts


TAIL_Z = -9.6     # 나셀 뒤 끝(노즐 앞)
NAC_X = 2.05      # 엔진 나셀 가운데(좌우 ±)
E_FUS = 2.2       # 앞 동체 단면 각짐(2 = 타원)

# 부위별 참고: 기수·캐노피·LERX = 플랭커식(처진 긴 기수, 높은 거품 캐노피, 캐노피 옆에서 날개 뿌리까지 이어지는 날카로운 앞전 연장, 엔진 사이 꼬리 침),
# 날개 = 베르쿠트식 전진익(뿌리 시위가 크고 LERX와 날개 앞전 사이에 노치, 뒷전도 앞으로), 엔진 = 톰캣식(동체와 틈을 둔 사각 흡입구 — 위 입술이 앞으로,
# 납작한 가운데 몸통 양옆에 떨어진 나셀, 나셀 위 수직미익, 엔진 사이 납작한 꼬리판). 모양은 새로 짠 것(실존 기체 복제가 아님).
# 앞 동체(기수~꼬리 침): (z, 반폭, 위 반높이, 아래 반높이, 중심 y)
FUS = [
    (12.4, 0.03, 0.03, 0.03, -0.32),
    (11.6, 0.26, 0.25, 0.24, -0.24),
    (10.4, 0.5, 0.47, 0.43, -0.13),
    (8.8, 0.7, 0.65, 0.56, -0.04),
    (7.0, 0.83, 0.77, 0.62, 0.0),
    (5.0, 0.9, 0.82, 0.62, 0.02),
    (3.0, 0.88, 0.8, 0.55, 0.05),
    (0.5, 0.8, 0.74, 0.45, 0.08),
    (-3.0, 0.7, 0.66, 0.4, 0.1),
    (-7.0, 0.55, 0.52, 0.35, 0.1),
    (-9.8, 0.34, 0.32, 0.28, 0.1),
    (-11.0, 0.06, 0.06, 0.06, 0.1),
]
# 가운데 납작한 몸통(나셀 사이): (z, 반폭, 위 반높이, 아래 반높이)
PAN = [(2.8, 0.75, 0.05, 0.05), (0.4, 1.1, 0.24, 0.22), (-2.6, 1.6, 0.3, 0.28), (-3.6, 1.75, 0.3, 0.28), (-9.0, 1.85, 0.3, 0.26), (-10.0, 1.55, 0.2, 0.16)]
PAN_Y, PAN_E = 0.05, 5.0
# 나셀: (z, 반폭, 반높이, 중심 y, 각짐 지수) — 흡입구는 각진 사각, 뒤로 가며 둥글어진다
NAC = [(-2.3, 0.56, 0.72, -0.6, 8.0), (-3.2, 0.56, 0.7, -0.6, 7.0), (-5.0, 0.58, 0.64, -0.58, 4.5), (-7.0, 0.6, 0.6, -0.55, 2.8), (-8.6, 0.58, 0.58, -0.55, 2.2), (TAIL_Z, 0.56, 0.56, -0.55, 2.0)]
INTAKE_TOP, INTAKE_BOT = -0.9, -1.9   # 흡입구 위 입술 z, 아래 입술 z(위가 앞으로). 위에서 보면 LERX 밑에 숨는다
WING_Y = 0.15
RING_N = 32


def interp(table, z, smooth=False):
    """z 내림차순 표에서 보간 — 값들(첫 칸 z 제외)의 튜플. smooth면 곡선(캣멀롬 — 표 자리에서 꺾이지 않는다)."""
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
                out.append(max(v, 0.005) if i <= 2 else v)   # 폭·높이만 0 위로(중심 높이는 음수일 수 있다)
            return tuple(out)
    return table[-1][1:]


def se_ring(cx, cy, z, w, ht, hb, e, n=RING_N):
    """초타원 단면(맨 위에서 시작해 시계 반대로). ht/hb = 위·아래 반높이, e 클수록 각진다."""
    pts = []
    for k in range(n):
        th = math.pi / 2 + k * math.tau / n
        c, s = math.cos(th), math.sin(th)
        x = cx + w * math.copysign(abs(c) ** (2 / e), c)
        y = cy + (ht if s > 0 else hb) * math.copysign(abs(s) ** (2 / e), s)
        pts.append((x, y, z))
    return pts


def _se_top(x, w, h, e):
    if abs(x) >= w:
        return None
    return h * (1 - (abs(x) / w) ** e) ** (1 / e)


def top_y(x, z):
    """(x, z) 자리 동체 윗면 높이(앞 동체·가운데 몸통 중 높은 것)."""
    best = -9.0
    w, ht, hb, cy = interp(FUS, z, True)
    v = _se_top(x, w, ht, E_FUS)
    if v is not None:
        best = max(best, cy + v)
    if PAN[-1][0] <= z <= PAN[0][0]:
        pw, pht, phb = interp(PAN, z, True)
        v = _se_top(x, pw, pht, PAN_E)
        if v is not None:
            best = max(best, PAN_Y + v)
    if best < -8.0:   # 몸통 밖이면 LERX·날개 판 윗면
        best = WING_Y + 0.12
    return best


def steps(z0, z1, d):
    n = max(int(math.ceil((z0 - z1) / d)), 1)
    return [z0 + (z1 - z0) * i / n for i in range(n + 1)]


def fuselage(m):
    # 앞 동체: 처진 기수 → 캐노피 → 등 → 엔진 사이 꼬리 침
    m.loft([se_ring(0, cy, z, w, ht, hb, E_FUS) for z in steps(FUS[0][0], FUS[-1][0], 0.4) for w, ht, hb, cy in [interp(FUS, z, True)]], "body", smooth=True)
    # 캐노피: 높은 거품 — 바닥은 그 자리 동체 윗면
    C = [(8.5, 0.04, 0.04), (7.9, 0.36, 0.34), (6.9, 0.5, 0.6), (5.7, 0.5, 0.58), (4.5, 0.36, 0.4), (3.6, 0.04, 0.04)]
    rings = []
    for z, w, h in C:
        base = top_y(0.0, z) - 0.12
        rings.append([(math.cos(a) * w, base + max(math.sin(a), -0.15) * h, z) for a in (k * math.tau / 16 for k in range(16))])
    m.loft(rings, "glass")
    # 가운데 납작한 몸통
    m.loft([se_ring(0, PAN_Y, z, pw, pht, phb, PAN_E) for z in steps(PAN[0][0], PAN[-1][0], 0.5) for pw, pht, phb in [interp(PAN, z, True)]], "body", smooth=True)
    # 나셀: 비스듬한 사각 입구(입술 → 안쪽 어두운 통로) → 둥글어지며 노즐
    for s in (1, -1):
        x = s * NAC_X
        w, h, cy, e = interp(NAC, NAC[0][0])
        def raked(k, dz=0.0):
            ring = se_ring(x, cy, 0.0, w * k, h * k, h * k, e, 24)
            return [(px, py, INTAKE_BOT + (INTAKE_TOP - INTAKE_BOT) * (py - (cy - h)) / (2 * h) + dz) for px, py, _ in ring]
        # 한 덩어리(안쪽 통로 끝 → 통로 → 입술 → 바깥 껍질 → 뒤 끝)로 이어야 면 방향이 맞는다(통로 벽은 안쪽을 본다)
        skin = [se_ring(x, cy_, z, w_, h_, h_, e_, 24) for z in steps(NAC[0][0], TAIL_Z, 0.8) for w_, h_, cy_, e_ in [interp(NAC, z, True)]]
        rings = [raked(0.84, -1.8), raked(0.84, -0.06), raked(1.0)] + skin
        m.loft(rings, ["black", "body", "body"] + ["body"] * (len(skin) - 1), smooth=True)
        w_, h_, cy_, _ = interp(NAC, TAIL_Z)
        rings = [se_ring(x, cy_, z, w_ * k, h_ * k, h_ * k, 2.0, 24) for z, k in ((TAIL_Z + 0.05, 1.0), (TAIL_Z - 0.9, 0.9), (TAIL_Z - 1.0, 0.78))]
        m.loft(rings, ["nozzle", "black"])


def surface(m, root, tip, kind, thick_root, thick_tip, tip_rake=0.0, edge=None):
    """날개꼴 판: root/tip = (x, y, z_le, z_te). 6점 단면(앞 둥근 모서리, 뒤 뾰족).
    tip_rake: 끝 단면의 뒷전 쪽 x를 이만큼 안으로. edge: 앞전 두 칸의 재질(강조색)."""
    def section(x, y, zle, zte, t, rake):
        c = zle - zte
        sx = math.copysign(1.0, x) if x != 0 else 1.0
        def at(f):
            return x - sx * rake * f
        e = 0.07 if edge else 0.15
        return [(at(0.0), y, zle), (at(e), y + t * 0.45, zle - c * e), (at(0.45), y + t * 0.5, zle - c * 0.45), (at(1.0), y, zte),
                (at(0.45), y - t * 0.4, zle - c * 0.45), (at(e), y - t * 0.35, zle - c * e)]
    around = None
    if edge:
        around = [edge, kind, kind, kind, kind, edge]
    m.loft([section(*root, thick_root, 0.0), section(*tip, thick_tip, tip_rake)], kind, smooth=True, around=around)


# 날개 평면형(x>0 기준): LERX 앞전 / 안쪽 날개 / 바깥 날개
LERX = [(0.7, 7.4, -9.0), (1.45, 3.9, -9.2), (2.9, -1.4, -9.4)]
WING = [(2.75, -2.2, -9.5), (4.6, -1.5, -7.6), (8.8, 0.2, -2.6)]


def wings(m):
    for s in (1, -1):
        # LERX: 캐노피 옆에서 날개 뿌리까지 날카롭게 휘어 나간다
        (x0, l0, t0), (x1, l1, t1), (x2, l2, t2) = LERX
        surface(m, (s * x0, WING_Y, l0, t0), (s * x1, WING_Y, l1, t1), "body", 0.22, 0.24)
        surface(m, (s * x1, WING_Y, l1, t1), (s * x2, WING_Y, l2, t2), "body", 0.24, 0.3)
        # 전진익: 뿌리 시위가 크고 앞전·뒷전 모두 앞으로. 앞전 강조색
        (x0, l0, t0), (x1, l1, t1), (x2, l2, t2) = WING
        surface(m, (s * x0, WING_Y, l0, t0), (s * x1, WING_Y - 0.03, l1, t1), "body", 0.32, 0.24, edge="red")
        surface(m, (s * x1, WING_Y - 0.03, l1, t1), (s * x2, WING_Y - 0.1, l2, t2), "body", 0.24, 0.08, edge="red")
        # 안으로 기운 수직미익(15도) — 나셀 위 뒤쪽
        cant = math.radians(15)
        bx, by = s * NAC_X, 0.02
        def fin(h, zle, zte, t):
            x = bx - s * math.sin(cant) * h
            y = by + math.cos(cant) * h
            c = zle - zte
            nx, ny = math.cos(cant) * s, math.sin(cant)
            return [(x, y, zle), (x + nx * t * 0.5, y + ny * t * 0.5, zle - c * 0.3), (x, y, zte), (x - nx * t * 0.5, y - ny * t * 0.5, zle - c * 0.3)]
        m.loft([fin(0.0, -4.8, -9.3, 0.3), fin(3.2, -7.9, -9.7, 0.1)], "body", smooth=True, around=["red", "body", "body", "red"])


def beaver_tail(m):
    """엔진 사이 납작한 꼬리판."""
    outline = [(1.45, -9.3), (1.3, -10.6), (0.9, -11.05), (-0.9, -11.05), (-1.3, -10.6), (-1.45, -9.3)]
    rings = [[(x, y, z) for x, z in outline] for y in (-0.06, 0.1)]
    m.loft(rings, "body", smooth=False)


def beam(m, p0, p1, r, kind="metal"):
    """두 점 사이의 네모 기둥(반폭 r)."""
    d = [p1[i] - p0[i] for i in range(3)]
    L = math.sqrt(sum(v * v for v in d))
    d = [v / L for v in d]
    up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.9 else (1.0, 0.0, 0.0)
    a = [d[1] * up[2] - d[2] * up[1], d[2] * up[0] - d[0] * up[2], d[0] * up[1] - d[1] * up[0]]
    la = math.sqrt(sum(v * v for v in a))
    a = [v / la for v in a]
    b = [d[1] * a[2] - d[2] * a[1], d[2] * a[0] - d[0] * a[2], d[0] * a[1] - d[1] * a[0]]
    rings = []
    for p in (p0, p1):
        rings.append([tuple(p[i] + (a[i] * sa + b[i] * sb) * r for i in range(3)) for sa, sb in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    m.loft(rings, kind, smooth=False)


def laser_pod(m):
    # 받침: 좌우 대각선으로 기울어진 사다리꼴 뼈대 둘 — 긴 밑변은 동체 등 양쪽, 짧은 윗변은 포드 밑을 따라, 두 다리가 비스듬히 잇는다
    zr, zf = -6.4, 1.0          # 밑변(동체)
    tr, tf = -5.0, -0.6         # 윗변(포드 밑)
    for s in (1, -1):
        def low(z):
            return (s * 1.2, top_y(s * 1.2, z) - 0.08, z)
        def high(z):
            return (s * 0.42, POD_Y - POD_R * 0.8, z)
        beam(m, low(zr), low(zf), 0.1)
        beam(m, high(tr), high(tf), 0.1)
        beam(m, low(zr), high(tr), 0.1)
        beam(m, low(zf), high(tf), 0.1)
    # 포드 몸통: 뒤는 좁아지는 방열부, 앞은 헤드 받침 고리
    n = 18
    P = [(POD_Z0, 0.35), (POD_Z0 + 0.6, 0.62), (POD_Z0 + 1.4, POD_R), (POD_Z1 - 0.3, POD_R), (POD_Z1, POD_R * 0.92)]
    rings = [[(math.cos(a) * r, POD_Y + math.sin(a) * r, z) for a in (k * math.tau / n for k in range(n))] for z, r in P]
    m.loft(rings, ["black", "light", "pod", "metal"])
    # 몸통 띠 둘(붉은 경고)·방열 지느러미(뒤쪽 위아래·옆 넷)
    for z in (POD_Z0 + 2.2, POD_Z1 - 1.0):
        rings = [[(math.cos(a) * (POD_R + 0.03), POD_Y + math.sin(a) * (POD_R + 0.03), z + dz) for a in (k * math.tau / n for k in range(n))] for dz in (-0.12, 0.12)]
        m.loft(rings, "red")
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        c, s = math.cos(a), math.sin(a)
        for dz in (0.0, 0.35, 0.7):
            z = POD_Z0 + 0.7 + dz
            r0, r1 = 0.5, 0.95
            rings = []
            for zz in (z - 0.04, z + 0.04):
                rings.append([(c * r0 - s * 0.25, POD_Y + s * r0 + c * 0.25, zz), (c * r1 - s * 0.25, POD_Y + s * r1 + c * 0.25, zz),
                              (c * r1 + s * 0.25, POD_Y + s * r1 - c * 0.25, zz), (c * r0 + s * 0.25, POD_Y + s * r0 - c * 0.25, zz)])
            m.loft(rings, "metal", smooth=False)


def head():
    """앞머리(원점 = 포드 축, 앞 고리 자리). 포드 축 둘레로 도는 짧은 통(굴림) — 앞에 렌즈 구가 반쯤 드러나 얹힌다(타게팅 포드처럼)."""
    m = Mesh()
    n = 18
    R = POD_R * 0.95
    P = [(0.0, R), (0.12, R), (0.5, R), (0.62, R * 0.86)]
    rings = [[(math.cos(a) * r, math.sin(a) * r, z) for a in (k * math.tau / n for k in range(n))] for z, r in P]
    m.loft(rings, ["metal", "pod", "pod"])
    # 렌즈 구를 감싸는 양옆 귀(구가 숙일 때 축 받침)
    for s in (1, -1):
        rings = []
        for x in (s * 0.5, s * 0.72):
            rings.append([(x, dy * 0.32, 0.62 + dz) for dy, dz in ((-1, -0.05), (1, -0.05), (0.7, 0.5), (-0.7, 0.5))])
        m.loft(rings, "pod", smooth=False)
    return m.finish("LaserAce_Head")


def lens():
    """렌즈 구(원점 = 렌즈 중심). 앞(+Z)에 붉게 빛나는 렌즈 원판과 테."""
    m = Mesh()
    n = 16
    rings = []
    for i in range(9):
        t = -math.pi / 2 + i * math.pi / 8
        rings.append([(math.cos(a) * math.cos(t) * LENS_R, math.sin(a) * math.cos(t) * LENS_R, math.sin(t) * LENS_R) for a in (k * math.tau / n for k in range(n))])
    rings[0] = [(p[0] * 0.01, p[1] * 0.01, p[2]) for p in rings[0]]
    rings[-1] = [(p[0] * 0.01, p[1] * 0.01, p[2]) for p in rings[-1]]
    m.loft(rings, ["black"] * 8)
    # 렌즈 테와 유리(앞으로 살짝 솟은 원판)
    rings = [[(math.cos(a) * r, math.sin(a) * r, LENS_R * z) for a in (k * math.tau / n for k in range(n))] for r, z in ((0.42, 0.88), (0.45, 1.02), (0.34, 1.05))]
    m.loft(rings, ["red", "red"])   # 렌즈 테 = 강조색
    rings = [[(math.cos(a) * r, math.sin(a) * r, LENS_R * z) for a in (k * math.tau / n for k in range(n))] for r, z in ((0.34, 1.03), (0.3, 1.1), (0.02, 1.14))]
    m.loft(rings, ["lens", "lens"])
    return m.finish("LaserAce_Lens")


def _box_blur(a, r):
    if r <= 0:
        return a
    for ax in (0, 1):
        c = np.cumsum(np.pad(a, [(r + 1, r) if i == ax else (0, 0) for i in range(2)], mode='edge'), axis=ax)
        a = (np.take(c, range(2 * r + 1, c.shape[ax]), axis=ax) - np.take(c, range(0, c.shape[ax] - 2 * r - 1), axis=ax)) / (2 * r + 1)
    return a


# 투영 영역 역변환(픽셀 → 유니티 좌표) — 패널 색조 칸 나눌 때
def inv_top(u, v):
    return (0.25 - u) * 40.0, (v - 0.02) / 0.56 * 24.0 - 11.0


def inv_bottom(u, v):
    return (0.75 - u) * 40.0, (v - 0.02) / 0.56 * 24.0 - 11.0


def inv_side(u, v):
    return (u - 0.02) / 0.56 * 24.0 - 11.0, (v - 0.62) / 0.14 * 6.0 - 2.5


REGION_UV = {"top": (0.0, 0.5, 0.0, 0.6), "bottom": (0.5, 1.0, 0.0, 0.6), "side": (0.0, 0.6, 0.6, 0.78)}   # u0, u1, v0, v1


class Painter:
    """유니티 좌표로 칠한다. 선·점은 거리로 부드럽게(계단 없음), 면은 3배 표본으로 가장자리를 부드럽게.
    어두운 선(lines)·밝은 선(hl)·때(dirt, 나중에 흐림)는 따로 모아 마지막에 한 번에 얹는다."""
    def __init__(self, img):
        self.img = img
        self.size = img.shape[0]
        self.lines = np.zeros(img.shape[:2], dtype=np.float32)
        self.hl = np.zeros(img.shape[:2], dtype=np.float32)
        self.dirt = np.zeros(img.shape[:2], dtype=np.float32)
        self.uvf = {"top": uv_top, "bottom": uv_bottom, "side": uv_side}
        self.mpp = 24.0 / (0.56 * self.size)   # 1픽셀 = 몇 m

    def px(self, region, a, b):
        u, v = self.uvf[region](a, b)
        return u * self.size, v * self.size

    def _seg(self, x0, y0, x1, y1, w):
        """선분 둘레 덮임(0~1) — (자리, 값)."""
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
        """패널 이음매: 어두운 홈 + 한쪽(아래 픽셀 쪽)에 옅은 빛."""
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
        # 이음매 둘레에 때가 조금 낀다
        np.maximum(self.dirt[sl], c * 0.35 * k, out=self.dirt[sl])

    def dashed(self, region, p0, p1, dash=0.25, w=1.3, k=0.8):
        n = max(int(math.dist(p0, p1) / dash), 1)
        for i in range(0, n, 2):
            a = [p0[j] + (p1[j] - p0[j]) * i / n for j in range(2)]
            b = [p0[j] + (p1[j] - p0[j]) * min(i + 1, n) / n for j in range(2)]
            self.line(region, a, b, w, k, hl=False)

    def poly(self, region, pts, w=1.5, k=1.0, closed=True):
        seq = list(zip(pts, pts[1:] + (pts[:1] if closed else [])))
        for a, b in seq:
            self.line(region, a, b, w, k)

    def rect(self, region, a0, b0, a1, b1, w=1.5, rivets=0.0):
        self.poly(region, [(a0, b0), (a1, b0), (a1, b1), (a0, b1)], w)
        if rivets > 0:
            ins = 0.06
            sa, sb = math.copysign(1, a1 - a0), math.copysign(1, b1 - b0)
            self.rivets(region, (a0 + sa * ins, b0 + sb * ins), (a1 - sa * ins, b0 + sb * ins), rivets)
            self.rivets(region, (a0 + sa * ins, b1 - sb * ins), (a1 - sa * ins, b1 - sb * ins), rivets)
            self.rivets(region, (a0 + sa * ins, b0 + sb * ins), (a0 + sa * ins, b1 - sb * ins), rivets)
            self.rivets(region, (a1 - sa * ins, b0 + sb * ins), (a1 - sa * ins, b1 - sb * ins), rivets)

    def dot(self, region, c, r_px, k=0.55):
        x, y = self.px(region, *c)
        sl, cov = self._seg(x, y, x, y, r_px * 2)
        if sl is not None:
            np.maximum(self.lines[sl], cov * k, out=self.lines[sl])
            sl2, c2 = self._seg(x + 0.8, y - 0.8, x + 0.8, y - 0.8, r_px * 1.2)
            if sl2 is not None:
                np.maximum(self.hl[sl2], c2 * 0.6, out=self.hl[sl2])

    def rivets(self, region, p0, p1, spacing=0.14, r_px=0.75):
        n = max(int(math.dist(p0, p1) / spacing), 1)
        for i in range(n + 1):
            self.dot(region, (p0[0] + (p1[0] - p0[0]) * i / n, p0[1] + (p1[1] - p0[1]) * i / n), r_px)

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

    def circle(self, region, c, r, n=24, w=1.4):
        self.poly(region, [(c[0] + math.cos(a) * r, c[1] + math.sin(a) * r) for a in (i * math.tau / n for i in range(n))], w)

    def stencil(self, region, a, b, rows=2, width=0.32, along=True):
        """작은 안내 문구 덩어리(읽히지 않는 글줄)."""
        for i in range(rows):
            ww = width * (1.0 - 0.25 * (i % 2))
            if along:
                self.fill(region, [(a, b - i * 0.06), (a + ww, b - i * 0.06), (a + ww, b - i * 0.06 - 0.03), (a, b - i * 0.06 - 0.03)], (0.35, 0.36, 0.38), 0.85)

    def finish(self):
        img = self.img
        dirt = _box_blur(self.dirt, max(int(0.05 / self.mpp), 1))
        img *= (1 - 0.13 * np.clip(dirt, 0, 1))[..., None]
        img *= (1 - 0.42 * np.clip(self.lines, 0, 1))[..., None]
        img += (0.06 * np.clip(self.hl, 0, 1) * (1 - np.clip(self.lines, 0, 1)))[..., None]


def _wing_edge(x, idx):
    x = abs(x)
    for a, b in zip(WING, WING[1:]):
        if a[0] <= x <= b[0]:
            k = (x - a[0]) / (b[0] - a[0])
            return a[idx] + (b[idx] - a[idx]) * k
    return WING[-1][idx] if x > WING[-1][0] else WING[0][idx]


def wing_le(x):
    """날개 앞전 z(x>0 기준)."""
    return _wing_edge(x, 1)


def wing_te(x):
    return _wing_edge(x, 2)


def lerx_le(x):
    x = abs(x)
    for a, b in zip(LERX, LERX[1:]):
        if a[0] <= x <= b[0]:
            k = (x - a[0]) / (b[0] - a[0])
            return a[1] + (b[1] - a[1]) * k
    return LERX[-1][1]


ZST = [10.5, 8.6, 3.2, 0.6, -2.6, -5.6, -8.4]   # 동체 가로 이음매 자리


def body_w(z):
    w = interp(FUS, z, True)[0]
    if PAN[-1][0] <= z <= PAN[0][0]:
        w = max(w, interp(PAN, z, True)[0])
    return w


def panel_tone(img, rng):
    """패널마다 아주 조금 다른 색조(같은 흰색도 칸마다 다르게 바랜다)."""
    N = img.shape[0]
    zb = np.array(sorted(ZST + [5.6, -0.9, -4.1, -7.0, -9.4]))
    xb = np.array(sorted([0.0, 0.5, 1.5, 2.65, 3.7, 4.7, 6.3, 7.9] + [-0.5, -1.5, -2.65, -3.7, -4.7, -6.3, -7.9]))
    yb = np.array([-1.3, -0.6, 0.05, 1.0, 2.0, 2.78])
    tab = (1 + 0.035 * (rng.random(4096) - 0.5)).astype(np.float32)
    for reg, inv in (("top", inv_top), ("bottom", inv_bottom), ("side", inv_side)):
        u0, u1, v0, v1 = REGION_UV[reg]
        c0, c1, r0, r1 = int(u0 * N), int(u1 * N), int(v0 * N), int(v1 * N)
        vv, uu = np.mgrid[r0:r1, c0:c1].astype(np.float32)
        a, b = inv((uu + 0.5) / N, (vv + 0.5) / N)
        if reg == "side":
            i, j = np.digitize(a, zb), np.digitize(b, yb)
        else:
            i, j = np.digitize(b, zb), np.digitize(a, xb)
        f = tab[(i * 31 + j * 7 + (0 if reg == "top" else 977 if reg == "bottom" else 1999)) % 4096]
        img[r0:r1, c0:c1, :3] *= f[..., None]


def make_texture():
    N = TEX
    img = np.zeros((N, N, 3), dtype=np.float32)
    img[...] = BODY_COL
    rng = np.random.default_rng(7)
    # 얼룩: 큰 얼룩 + 고운 결
    big = _box_blur(rng.random((N, N)).astype(np.float32), 40)
    big = (big - big.mean()) / max(big.std(), 1e-6)
    fine = _box_blur(rng.random((N, N)).astype(np.float32), 2)
    fine = (fine - fine.mean()) / max(fine.std(), 1e-6)
    img *= (1 + 0.016 * big + 0.006 * fine)[..., None]
    panel_tone(img, rng)
    P = Painter(img)
    RADOME = (0.74, 0.75, 0.74)
    DARK = (0.3, 0.31, 0.33)

    # ── 기수 레이돔(옅은 회색)
    for reg in ("top", "bottom"):
        P.fill(reg, [(-1.5, 10.5), (1.5, 10.5), (1.5, 13.0), (-1.5, 13.0)], RADOME)
    P.fill("side", [(10.5, -1.5), (13.0, -1.5), (13.0, 1.0), (10.5, 1.0)], RADOME)

    for s in (1, -1):
        for reg in ("top", "bottom"):
            # 날개: 앞·뒤 날개보, 플래퍼론·플랩(경계·칸막이), 날개 뿌리 이음매, 날개 칸
            P.line(reg, (s * 2.95, wing_le(2.95) - 0.6), (s * 8.5, wing_le(8.5) - 0.42))
            P.rivets(reg, (s * 2.95, wing_le(2.95) - 0.68), (s * 8.5, wing_le(8.5) - 0.5), 0.16)
            P.line(reg, (s * 2.95, wing_te(2.95) + 1.6), (s * 8.4, wing_te(8.4) + 1.25))
            P.rivets(reg, (s * 2.95, wing_te(2.95) + 1.68), (s * 8.4, wing_te(8.4) + 1.33), 0.16)
            P.line(reg, (s * 4.7, wing_te(4.7) + 1.0), (s * 7.9, wing_te(7.9) + 0.8))
            P.line(reg, (s * 2.95, wing_te(2.95) + 1.0), (s * 4.5, wing_te(4.5) + 1.0))
            for x, d in ((2.95, 1.0), (4.5, 1.0), (4.7, 1.0), (6.3, 0.9), (7.9, 0.8)):
                P.line(reg, (s * x, wing_te(x) - 0.3), (s * x, wing_te(x) + d))
            for x in (3.7, 5.5, 7.1):
                P.line(reg, (s * x, wing_le(x) - 0.6), (s * x, wing_te(x) + 1.6 - (x - 2.95) * 0.06))
            P.line(reg, (s * 2.65, -1.2), (s * 2.65, -9.4))
            # 뒷전 쪽 때(조종면 틈)
            P.grime(reg, [(s * 2.95, wing_te(2.95) + 1.1), (s * 8.4, wing_te(8.4) + 0.9), (s * 8.4, wing_te(8.4) + 0.7), (s * 2.95, wing_te(2.95) + 0.9)], 0.6)
            # 날개 끝 주황 띠(테두리 선) + 항법등(오른쪽 초록, 왼쪽 빨강)
            band = [(s * 7.55, wing_le(7.55) + 0.3), (s * 8.05, wing_le(8.05) + 0.3), (s * 8.05, wing_te(8.05) - 0.3), (s * 7.55, wing_te(7.55) - 0.3)]
            P.fill(reg, band, ORANGE)
            P.disk(reg, (s * 8.6, wing_le(8.6) - 0.25), 0.07, (0.2, 0.85, 0.35) if s > 0 else (0.9, 0.12, 0.1))
            # 하드포인트 판(아래만)
            if reg == "bottom":
                for x in (4.2, 6.0):
                    z = (wing_le(x) + wing_te(x)) * 0.5
                    P.rect(reg, s * (x - 0.12), z + 0.45, s * (x + 0.12), z - 0.45, rivets=0.12)
            # LERX: 앞전 덮개 이음매(앞전 따라 0.35m 안쪽)
            P.line(reg, (s * 0.95, lerx_le(0.95) - 0.35), (s * 1.45, lerx_le(1.45) - 0.35))
            P.line(reg, (s * 1.45, lerx_le(1.45) - 0.35), (s * 2.6, lerx_le(2.6) - 0.35))

        # ── 위
        P.line("top", (s * 0.5, 3.2), (s * 0.5, -9.6))
        P.rivets("top", (s * 0.56, 3.0), (s * 0.56, -9.4), 0.18)
        P.line("top", (s * 1.5, 0.4), (s * 1.5, -9.4))
        zs = [7.0, 5.5, 4.0, 2.5, 1.0]
        P.poly("top", [(s * min(interp(FUS, z, True)[0], 0.95), z) for z in zs], closed=False)   # 동체-LERX 이음매
        P.rect("top", s * 0.95, 1.0, s * 1.35, 0.2, rivets=0.1)
        P.stencil("top", s * 0.95 if s > 0 else -1.35, 0.05)
        P.rect("top", s * 1.0, -3.0, s * 1.4, -4.0, rivets=0.1)
        P.grime("top", [(s * 1.0, -4.0), (s * 1.4, -4.0), (s * 1.35, -5.6), (s * 1.05, -5.6)], 0.5)
        P.rect("top", s * 0.15, -8.2, s * 0.45, -8.9, rivets=0.1)
        P.rect("top", s * 1.75, -6.2, s * 2.45, -7.4, rivets=0.12)
        P.grime("top", [(s * 1.8, -7.4), (s * 2.4, -7.4), (s * 2.3, -9.2), (s * 1.9, -9.2)], 0.45)
        # 밟지 마시오 테두리(점선) — 날개 뿌리
        P.dashed("top", (s * 1.8, -2.4), (s * 2.5, -2.4))
        P.dashed("top", (s * 2.5, -2.4), (s * 2.5, -5.6))
        P.dashed("top", (s * 2.5, -5.6), (s * 1.8, -5.6))
        P.dashed("top", (s * 1.8, -5.6), (s * 1.8, -2.4))
        # 등 환기구 살
        for i in range(5):
            z = -6.5 - i * 0.14
            P.line("top", (s * 0.18, z), (s * 0.36, z), w=2.2)
        # ── 아래
        for dx in (-0.56, 0.0, 0.56):
            P.line("bottom", (s * (NAC_X + dx), -2.3), (s * (NAC_X + dx), -9.6))
        P.rivets("bottom", (s * (NAC_X - 0.5), -2.6), (s * (NAC_X - 0.5), -9.4), 0.2)
        P.rivets("bottom", (s * (NAC_X + 0.5), -2.6), (s * (NAC_X + 0.5), -9.4), 0.2)
        for z in (-4.0, -6.4, -8.2):
            P.line("bottom", (s * (NAC_X - 0.56), z), (s * (NAC_X + 0.56), z))
        P.fill("bottom", [(s * (NAC_X - 0.22), -2.3), (s * (NAC_X + 0.22), -2.3), (s * NAC_X, -2.7)], ORANGE)
        P.rect("bottom", s * 0.15, -1.0, s * 1.2, -6.0)
        P.rect("bottom", s * 1.25, -3.6, s * 1.8, -6.6, rivets=0.12)   # 주 바퀴 문
        P.grime("bottom", [(s * 1.25, -6.6), (s * 1.8, -6.6), (s * 1.7, -9.0), (s * 1.35, -9.0)], 0.55)
        P.rect("bottom", s * 0.05, 7.0, s * 0.35, 5.6, rivets=0.1)     # 앞바퀴 문
        P.grime("bottom", [(s * 0.05, 5.6), (s * 0.35, 5.6), (s * 0.3, 3.8), (s * 0.1, 3.8)], 0.45)
        P.stencil("bottom", s * 0.4 if s > 0 else -0.72, -1.3)
    # 위·아래 가로 이음매(+리벳)
    for z in ZST:
        w = body_w(z)
        for reg in ("top", "bottom"):
            P.line(reg, (-w, z), (w, z))
            if z < 3.5:
                P.rivets(reg, (-w + 0.1, z - 0.07), (w - 0.1, z - 0.07), 0.16)
    # 눈부심 방지 패널·기관포(오른쪽 LERX 뿌리) + 그을음
    P.fill("top", [(-0.2, 10.0), (0.2, 10.0), (0.5, 8.45), (-0.5, 8.45)], DARK)
    P.fill("top", [(0.95, 3.3), (1.13, 3.3), (1.13, 2.9), (0.95, 2.9)], (0.08, 0.08, 0.09))
    P.grime("top", [(0.95, 2.9), (1.13, 2.9), (1.25, 1.2), (0.9, 1.2)], 0.9)
    P.circle("top", (0.3, -1.0), 0.12)   # 급유구
    P.fill("top", [(-0.06, -0.2), (0.06, -0.2), (0.06, -0.75), (-0.06, -0.75)], (0.4, 0.41, 0.43))   # 안테나 받침

    # ── 옆: 이음매·나셀 점검창·사출 경고·편대등·수직미익
    for z in ZST:
        P.line("side", (z, -1.4), (z, 1.0))
    P.line("side", (-2.3, -0.6), (-9.6, -0.6))
    P.line("side", (1.0, 0.05), (-9.6, 0.05))
    P.rivets("side", (-2.5, -0.53), (-9.4, -0.53), 0.18)
    P.rect("side", -4.2, -0.2, -5.2, -1.0, rivets=0.12)
    P.rect("side", -6.9, -0.25, -7.9, -0.95, rivets=0.12)
    P.grime("side", [(-5.2, -0.6), (-5.2, -1.0), (-6.8, -0.95), (-6.8, -0.7)], 0.4)
    P.stencil("side", -4.3, -1.1)
    P.fill("side", [(6.5, 0.05), (5.9, 0.05), (6.2, 0.56)], (0.95, 0.25, 0.08))   # 사출 좌석 경고
    P.fill("side", [(6.33, 0.15), (6.07, 0.15), (6.2, 0.38)], (0.95, 0.95, 0.92))
    P.stencil("side", 5.75, 0.0, rows=2, width=-0.4)
    # 기수 옆 점검창·캐노피 레일
    P.rect("side", 9.9, 0.05, 9.2, -0.3, rivets=0.09)
    P.stencil("side", 9.85, -0.38, rows=1, width=-0.5)
    P.line("side", (8.45, 0.62), (3.6, 0.62))
    P.rivets("side", (8.3, 0.55), (3.8, 0.55), 0.15)
    P.dashed("side", (4.9, -0.1), (4.9, -0.6), 0.12)
    P.dashed("side", (4.5, -0.1), (4.5, -0.6), 0.12)
    P.fill("side", [(9.4, -0.02), (8.6, -0.02), (8.6, 0.06), (9.4, 0.06)], (0.82, 0.92, 0.6))   # 편대등 띠
    P.fill("side", [(-6.15, 0.7), (-6.3, 0.7), (-6.75, 2.5), (-6.6, 2.5)], (0.82, 0.92, 0.6))
    # 수직미익: 끝 주황 띠·방향타·이음매·렌즈 문양
    P.fill("side", [(-11.0, 2.78), (-4.0, 2.78), (-4.0, 3.6), (-11.0, 3.6)], ORANGE)
    P.line("side", (-8.55, 0.4), (-8.95, 2.6))
    P.line("side", (-8.95, 2.6), (-9.75, 2.6))
    P.line("side", (-5.6, 1.0), (-9.4, 1.0))
    P.rivets("side", (-5.3, 0.3), (-9.2, 0.3), 0.15)
    P.disk("side", (-7.9, 1.75), 0.46, ORANGE)
    P.disk("side", (-7.9, 1.75), 0.37, (0.06, 0.06, 0.07))
    P.disk("side", (-7.9, 1.75), 0.2, (0.18, 0.04, 0.03))
    P.disk("side", (-7.9, 1.75), 0.12, ORANGE)

    P.finish()
    # 노즐 그을음(아래·옆 뒤 끝)
    vb = (np.arange(N, dtype=np.float32) + 0.5) / N
    zz = (vb - 0.02) / 0.56 * 24.0 - 11.0
    soot = np.clip((-8.0 - zz) / 1.6, 0, 1) ** 1.5
    rv = int(0.6 * N)
    img[:rv, N // 2:, :] *= (1 - 0.45 * soot[:rv])[:, None, None]
    zs = (vb - 0.02) / 0.56 * 24.0 - 11.0
    soot_s = np.clip((-8.4 - zs) / 1.3, 0, 1) ** 1.5
    r0, r1 = int(0.62 * N), int(0.76 * N)
    yv = ((np.arange(r0, r1, dtype=np.float32) + 0.5) / N - 0.62) / 0.14 * 6.0 - 2.5
    low = np.clip((0.0 - yv) / 0.6, 0, 1)
    img[r0:r1, :, :] *= (1 - 0.4 * low[:, None] * soot_s[None, :])[..., None]
    # 색 칸
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


def link(me, loc=(0, 0, 0), parent=None):
    o = bpy.data.objects.new(me.name, me)
    o.location = to_blender(loc)
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
        o.location = to_blender(loc)
    return o


def write_fbx(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    make_texture()
    body = Mesh()
    fuselage(body)
    wings(body)
    beaver_tail(body)
    laser_pod(body)
    b = link(body.finish("LaserAce_Body"))
    h = link(head(), (0, POD_Y, POD_Z1))
    lz = HEAD_LEN
    l = link(lens(), (0, 0, lz), parent=h)
    print("TRIS", sum(len(p.vertices) - 2 for o in (b, h, l) for p in o.data.polygons))
    print("LENS world (unity)", (0, POD_Y, POD_Z1 + lz))
    write_fbx(os.path.join(OUT, NAME + ".fbx"), [b, h, l])
    print("DONE")
