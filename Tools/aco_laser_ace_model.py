"""레이저 에이스 기체(가공 — 실존 기체 아님, 부위별로 실존 기체의 특징을 참고해 새로 짠 조합). 쌍발, 전진익(베르쿠트식), 수평미익·카나드 없음,
처진 긴 기수·높은 캐노피·날카로운 LERX(플랭커식), 동체와 틈을 둔 사각 흡입구·떨어진 나셀·납작한 가운데 몸통·꼬리판(톰캣식), 나셀 위 안으로 기운 쌍수직미익,
좌우 대각선 사다리꼴 뼈대 둘로 받친 대형 레이저 포드. 흰색 계열에 붉은 강조색. 부품:
 - LaserAce_Body: 동체·날개·수직미익·흡입구·노즐·캐노피·트러스 받침·포드 몸통
 - LaserAce_Head: 포드 앞머리(원점 = 포드 축 위, 고정)
 - LaserAce_Lens: 렌즈 구(원점 = 렌즈 중심, 헤드의 자식 — 유니티에선 렌즈 포탑(ACO_AAGunSite)의 TurretPitch에 붙어 좌우·위아래로 돈다)
좌표: 유니티 좌표 그대로(x 오른쪽, y 위, z 앞 = 기수). 단위 m. 길이 약 24m, 폭 약 18m.
텍스처 2048: 동체 외피는 펼침 UV(둘레 = 단면 점 번호, 길이 = z)에, 판 부품(날개·미익·카나드·꼬리판)은 위·아래·옆 투영 UV에
패널 라인·점검창·붉은 띠·그을음을 직접 칠한다. 나머지 재질은 색 칸.
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
RED = (0.74, 0.06, 0.06)        # 강조색(붉은색)
BLACK = (0.06, 0.06, 0.07)      # 두 번째 강조색(검정) — 날개·카나드 끝, 기수 끝, 미익 줄, 노즐 둘레 내열 도장
# 아틀라스(2048): 외피 펼침 v 0~0.48 | 위·아래 투영 v 0.5~0.8 | 옆 투영 v 0.8~0.89 | 색 칸 v 0.95
PATCH = {
    "body": ((0.05, 0.95), BODY_COL),
    "light": ((0.15, 0.95), (0.3, 0.31, 0.33)),
    "pod": ((0.25, 0.95), (0.24, 0.25, 0.27)),    # 레이저 포드(짙은 회색 — 흰 동체와 대비)
    "black": ((0.35, 0.95), (0.025, 0.025, 0.03)),
    "glass": ((0.45, 0.95), (0.13, 0.2, 0.3)),
    "red": ((0.55, 0.95), RED),   # 강조색(붉은색) — 날개·LERX 앞전·포드 띠·렌즈 테·벤트럴 핀
    "metal": ((0.65, 0.95), (0.42, 0.43, 0.45)),
    "lens": ((0.75, 0.95), (1.0, 0.16, 0.12)),
    "nozzle": ((0.85, 0.95), (0.24, 0.2, 0.18)),
    "hot": ((0.95, 0.95), (0.5, 0.17, 0.08)),     # 노즐 안 끝(뜨거운 면)
}
PATCH_SIZE = 0.05
SKIN_N = 42                         # 외피 고리 점 수(둘레 UV = 점 번호 / SKIN_N)
SKIN_Z0, SKIN_Z1, SKIN_V1 = -9.7, 12.5, 0.48


def uv_skin(k, z):
    """외피 펼침: k = 고리 점 번호(0 = 척추 위, 21 = 용골, 1~20 오른쪽, 22~41 왼쪽), z = 길이."""
    return (k / SKIN_N, (z - SKIN_Z0) / (SKIN_Z1 - SKIN_Z0) * SKIN_V1)


# 투영 영역(판 부품 — 유니티 좌표 → uv). 위·아래: (x, z) x ±10, z -11.5 ~ 8. 옆: (z, y) y -2 ~ 4
def uv_top(x, z):
    return (0.25 - x / 40.0, 0.5 + (z + 11.5) / 19.5 * 0.3)


def uv_bottom(x, z):
    return (0.75 - x / 40.0, 0.5 + (z + 11.5) / 19.5 * 0.3)


def uv_side(z, y):
    return (0.02 + (z + 11.5) / 19.5 * 0.56, 0.8 + (y + 2.0) / 6.0 * 0.09)

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
        self.weld = []      # 같은 자리 버텍스를 합칠 후보(이음 고리)
        self.creases = []   # 주름(나눌 때 날카롭게 남길 모서리) — (버텍스, 버텍스)

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
            if not f.is_valid:      # 용접(remove_doubles)으로 없어진 면
                continue
            n = f.normal
            nx, ny = -n.x, n.z
            for loop in f.loops:
                c = loop.vert.co
                x, y, z = -c.x, c.z, -c.y
                if abs(ny) >= abs(nx) * 0.8:
                    loop[self.uv].uv = uv_top(x, z) if ny > 0 else uv_bottom(x, z)
                else:
                    loop[self.uv].uv = uv_side(z, y)

    def loft(self, rings, kinds, caps=True, smooth=True, around=None, uvfn=None):
        """rings: [[(x, y, z)...]] 같은 점 수. kinds: 고리 사이마다 재질(문자열이면 모두 같다, 리스트면 그 칸의 둘레 칸마다). around: 둘레 칸마다 재질(있으면 kinds 대신).
        caps: True = 양 끝 막음, "start"/"end" = 한쪽만, False = 열림. uvfn(i, k): 흰 동체 면의 펼침 UV(투영 대신 — k는 0 ~ 점 수)."""
        vs = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = len(rings[0])
        for i in range(len(vs) - 1):
            kind = kinds if isinstance(kinds, str) else kinds[i]
            for k in range(n):
                m = (k + 1) % n
                f = self.bm.faces.new((vs[i][k], vs[i][m], vs[i + 1][m], vs[i + 1][k]))
                f.smooth = smooth
                kd = kind[k] if isinstance(kind, list) else (around[k] if around else kind)
                self.paint(f, kd)
                if uvfn is not None and kd == "body":
                    for loop, (ii, kk) in zip(f.loops, ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k))):
                        loop[self.uv].uv = uvfn(ii, kk)
                    self.body_faces.discard(f)
        if caps:
            ends = ((vs[0], kinds if isinstance(kinds, str) else kinds[0]), (vs[-1], kinds if isinstance(kinds, str) else kinds[-1]))
            if caps == "end":
                ends = ends[1:]
            elif caps == "start":
                ends = ends[:1]
            for ring, kind in ends:
                f = self.bm.faces.new(ring)
                self.paint(f, kind[0] if isinstance(kind, list) else kind)
        return vs

    def finish(self, name):
        if self.weld:
            bmesh.ops.remove_doubles(self.bm, verts=[v for v in self.weld if v.is_valid], dist=1e-4)
        if self.creases:
            cl = self.bm.edges.layers.float.get("crease_edge") or self.bm.edges.layers.float.new("crease_edge")
            for a, b in self.creases:
                if a.is_valid and b.is_valid:
                    e = self.bm.edges.get((a, b))
                    if e is not None:
                        e[cl] = 1.0
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
PAN = [(2.8, 0.75, 0.05, 0.05), (0.4, 1.1, 0.24, 0.32), (-1.2, 1.5, 0.28, 0.36), (-2.6, 1.55, 0.3, 0.38), (-3.6, 1.56, 0.3, 0.42), (-5.0, 1.75, 0.3, 0.45), (-9.0, 1.8, 0.3, 0.38), (-9.6, 1.45, 0.24, 0.24), (-10.0, 1.4, 0.2, 0.16), (-10.6, 1.25, 0.1, 0.08), (-11.05, 0.95, 0.04, 0.035)]   # 노즐 사이(TAIL_Z 뒤)는 노즐 안쪽 벽(1.49) 안
PAN_Y, PAN_E = 0.05, 5.0
# 나셀: (z, 반폭, 반높이, 중심 y, 각짐 지수) — 흡입구는 각진 사각, 뒤로 가며 둥글어진다
NAC = [(-2.3, 0.56, 0.72, -0.6, 8.0), (-3.2, 0.56, 0.7, -0.6, 7.0), (-5.0, 0.58, 0.64, -0.58, 4.5), (-7.0, 0.6, 0.62, -0.53, 2.8), (-8.6, 0.58, 0.61, -0.51, 2.2), (TAIL_Z, 0.56, 0.6, -0.5, 2.0)]   # 뒤로 갈수록 윗면이 날개 밑에 닿게(밑면 높이는 그대로)
INTAKE_TOP, INTAKE_BOT = -0.9, -1.9
DUCT_END_Z = -2.3      # 흡입 덕트 끝(수직 벽 — 압축기 면이 이 앞에)   # 흡입구 위 입술 z, 아래 입술 z(위가 앞으로). 위에서 보면 LERX 밑에 숨는다
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


CANOPY = [(8.5, 0.04, 0.04), (7.9, 0.36, 0.34), (6.9, 0.5, 0.6), (5.7, 0.5, 0.58), (4.5, 0.36, 0.4), (3.6, 0.04, 0.04)]   # (z, 반폭, 높이)


def canopy_ring(z, sc=1.0):
    """캐노피 단면(유리 고리와 같은 직선 보간 — 틀이 유리에 딱 붙는다). sc = 바닥 가운데 기준 배율."""
    for a, b in zip(CANOPY, CANOPY[1:]):
        if b[0] <= z <= a[0]:
            k = (a[0] - z) / (a[0] - b[0])
            w, h = a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k
            break
    else:
        w, h = CANOPY[0][1], CANOPY[0][2]
    base = top_y(0.0, z) - 0.12
    return [(math.cos(t) * w * sc, base + max(math.sin(t), -0.15) * h * sc, z) for t in (k * math.tau / 16 for k in range(16))]


def steps(z0, z1, d):
    n = max(int(math.ceil((z0 - z1) / d)), 1)
    return [z0 + (z1 - z0) * i / n for i in range(n + 1)]


def fuselage(m):
    """캐노피(외피·흡입구·노즐은 skin_cage — 따로 나눈 뒤 합친다)."""
    # 캐노피: 높은 거품 — 바닥은 그 자리 동체 윗면
    rings = [canopy_ring(z) for z, _, _ in CANOPY]
    m.loft(rings, "glass")


WING_TE_Z = -9.5                  # 날개 뿌리 뒷전 — 외피의 LERX 칸은 여기까지 날개 뿌리를 덮는다
AFT_IN0, AFT_IN1 = -2.6, -2.9     # 덕트 끝(-2.3)보다 충분히 뒤 — 나눌 때 바뀌는 면이 덕트 안으로 처져 보이지 않게     # 앞: 몸통만인 고리 → 나셀이 붙은 고리(흡입구 통 속에서 바뀐다)
AFT_OUT = TAIL_Z - 0.02           # 뒤: 나셀 끝(TAIL_Z, 노즐 용접) 바로 뒤 몸통만인 고리 — 나셀 칸은 노즐 시작 자리에서 평평하게 끝난다(노즐 속은 뜨거운 면이 가린다)


def _smooth(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


LERX_T = (0.22, 0.24, 0.3)  # LERX 두께(LERX 점마다)


def _lerx_e(z):
    """z 자리 LERX 바깥 가장자리 x(앞전이 지나는 자리, 앞전 끝 뒤로는 바깥 끝 2.9). 앞전보다 앞이면 0."""
    (x0, l0, _), (x1, l1, _), (x2, l2, _) = LERX
    if z > l0:
        return 0.0
    if z >= l1:
        return x0 + (x1 - x0) * (l0 - z) / (l0 - l1)
    if z >= l2:
        return x1 + (x2 - x1) * (l1 - z) / (l1 - l2)
    return x2


def _lerx_at(x):
    """x 자리 LERX (앞전 z, 뒷전 z, 두께)."""
    if x <= LERX[0][0]:
        return LERX[0][1], LERX[0][2], LERX_T[0]
    for a, b, ta, tb in zip(LERX, LERX[1:], LERX_T, LERX_T[1:]):
        if x <= b[0]:
            k = (x - a[0]) / (b[0] - a[0])
            return a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k, ta + (tb - ta) * k
    return LERX[-1][1], LERX[-1][2], LERX_T[-1]


def se_pt(cx, cy, w, ht, hb, e, ang):
    """초타원 위 점(각도 ang 도, 0 = 바깥 옆, 90 = 위)."""
    a = math.radians(ang)
    c, sn = math.cos(a), math.sin(a)
    return (cx + w * math.copysign(abs(c) ** (2 / e), c), cy + (ht if sn > 0 else hb) * math.copysign(abs(sn) ** (2 / e), sn))


# 케이지 반쪽 점(위 가운데 → 바깥 → 아래 가운데): T0 척추 위, T1·T2 척추 어깨, T3 데크(가운데 몸통·LERX 뿌리), T4 LERX 위 가운데,
# T4b LERX 위 앞전 가까이, T5 LERX 앞전(주름), B0 LERX 밑 앞전 가까이, B1 LERX 밑, B2~B6 나셀(바깥 위 → 바깥 아래 → 밑 → 안쪽 아래),
# (B2~B6 자리는 Bn 9점) B7 터널 천장 모서리, B8·B9 터널 천장·배, K 용골(아래 가운데)
NA_ANG = (45, 15, -15, -45, -70, -110, -135, -155, -172)   # 나셀 칸(Bn 9점) 각도 — 바깥 위 → 바깥 옆 → 밑 → 안쪽 옆
TUBE_ANG = NA_ANG + (165, 140, 115, 90, 65)          # 흡입구 통 고리(앞 9점이 외피와 같은 자리 — 용접)
NOZ_ANG = NA_ANG + (165, 115, 102, 90, 76, 62)       # 노즐 고리(165 말고 모두 외피 뒤 끝 고리와 같은 자리 — 용접)
NOSE_ANG = (65, 35, 24, 14, 6, 0, -4, -8, -12, -16, -20, -24, -28, -32, -36, -40, -46, -56, -66, -78)   # 기수(LERX 없음): T1 ~ B9
I_T5 = 6        # 반쪽 목록(T0 포함)에서 T5 자리


def _wing_root_env(x, z):
    """날개 판 뿌리(x 2.75, 두께 0.32)가 외피 LERX에 꽂히는 자리의 날개 단면 윗면·밑면 — 외피가 이보다 얇으면 날개 뿌리가 삐져나온다."""
    (x0, le, te), _, _ = WING
    if x < x0 - 0.2 or not (te < z < le):
        return None
    f = (le - z) / (le - te)
    t = 0.32
    up = 0.45 * f / 0.07 if f < 0.07 else (0.45 + 0.05 * (f - 0.07) / 0.38 if f < 0.45 else 0.5 * (1.0 - f) / 0.55)
    dn = 0.35 * f / 0.07 if f < 0.07 else (0.35 + 0.05 * (f - 0.07) / 0.38 if f < 0.45 else 0.4 * (1.0 - f) / 0.55)
    return WING_Y + up * t + 0.01, WING_Y - dn * t - 0.01


def _chine(x, z, E):
    """(x, z) 자리 LERX 윗면·밑면 높이(뒷전·앞전에서 두께 0, 앞전 끝 뒤 바깥 끝은 둥글게). 날개 뿌리가 꽂히는 자리는 날개 단면보다 얇지 않게."""
    if E <= 0.0 or x >= E:
        return WING_Y, WING_Y
    le, te, t = _lerx_at(x)
    f = (le - z) / max(le - te, 1e-6)
    top, bot = WING_Y, WING_Y
    if 0.0 < f < 1.0:
        af = 2.6 * math.sqrt(f) * (1.0 - f)
        r = 1.0
        if z < LERX[-1][1] and x > E - 0.12:
            u = (x - (E - 0.12)) / 0.12
            r = math.sqrt(max(1.0 - u * u, 0.0))
        top, bot = WING_Y + 0.5 * t * af * r, WING_Y - 0.4 * t * af * r
    env = _wing_root_env(x, z)
    if env is not None:
        top, bot = max(top, env[0]), min(bot, env[1])
    return top, bot


def cage_half(z):
    """z 자리 케이지 반쪽 점 22개 [(x, y)] — T0 ~ K."""
    w, ht, hb, cy = interp(FUS, z, True)
    has_pan = PAN[-1][0] - 1e-6 <= z <= PAN[0][0]
    pw, pht, phb = interp(PAN, z, True)
    E = _lerx_e(z) if z >= LERX[-1][2] else (LERX[-1][0] if z >= TAIL_Z - 1e-6 else 0.0)   # LERX 뒷전 뒤 ~ 노즐 시작: 두께 0 판
    nw, nh, ncy, ne = interp(NAC, min(max(z, TAIL_Z), NAC[0][0]), True)

    def fp(a):
        return se_pt(0.0, cy, w, ht, hb, E_FUS, a)

    def pan_t(x):
        return PAN_Y + pht * (1 - min(x / pw, 1.0) ** PAN_E) ** (1 / PAN_E) if has_pan and x < pw else -9.0

    def pan_b(x):
        return PAN_Y - phb * (1 - min(x / pw, 1.0) ** PAN_E) ** (1 / PAN_E) if has_pan and x < pw else 9.0

    def fus_t(x):
        return cy + ht * (1 - min(x / w, 1.0) ** E_FUS) ** (1 / E_FUS) if x < w else -9.0

    def fus_b(x):
        return cy - hb * (1 - min(x / w, 1.0) ** E_FUS) ** (1 / E_FUS) if x < w else 9.0

    def top_at(x):
        return max(fus_t(x), pan_t(x), _chine(x, z, E)[0])

    def bot_at(x):
        return min(fus_b(x), pan_b(x), _chine(x, z, E)[1])

    def nac(a):
        return se_pt(NAC_X, ncy, nw, nh, nh, ne, a)

    T0 = (0.0, max(cy + ht, pan_t(0.0)))
    nose = [fp(a) for a in NOSE_ANG]
    # LERX가 있는 몸통(기수 뒤): 위
    x2 = fp(35)[0]
    T1 = fp(65)
    T2 = (x2, max(fp(35)[1], top_at(x2)))
    x3 = max(w * 1.02, pw * 0.92 if has_pan else 0.0)
    has_nac = TAIL_Z - 1e-6 <= z <= AFT_IN1 + 1e-6
    Ee = max(E, x3 + 0.2)
    x4 = x3 + 0.5 * (Ee - x3)
    x4b = Ee - min(0.07, 0.06 * (Ee - x3))
    T3 = (x3, top_at(x3))
    T4 = (x4, top_at(x4))
    T4b = (x4b, _chine(x4b, z, E)[0])
    T5 = (Ee, WING_Y)
    B0 = (x4b, _chine(x4b, z, E)[1])
    if has_nac:
        x1 = NAC_X + nw + 0.6 * max(Ee - NAC_X - nw, 0.0)
        B1 = (x1, _chine(x1, z, E)[1])
        Bn = [nac(a) for a in NA_ANG]
        x7 = NAC_X - nw - 0.1
        B7 = (x7, bot_at(x7) + 0.04)
        # 뒤(LERX·날개 뒷전 ~ 나셀 끝): LERX는 두께 0인 뒷전으로 노즐 시작까지 그대로(접지 않는다 — 접으면 뒤를 보는 벽이 생긴다)
    else:
        x1 = x3 + 0.88 * (Ee - x3)
        B1 = (x1, _chine(x1, z, E)[1])
        # LERX 밑(나셀 없는 앞): 흡입구 통 속에서 나셀 칸으로 내려가도록 바깥 → 안 순서로
        Bn = [(x, bot_at(x)) for x in (x3 + fr * (Ee - x3) for fr in (0.76, 0.68, 0.6, 0.52, 0.44, 0.34, 0.24, 0.16, 0.1))]
        x7 = x3 * 0.92
        B7 = (x7, bot_at(x7))
    B8 = (B7[0] * 0.62, bot_at(B7[0] * 0.62))
    B9 = (B7[0] * 0.3, bot_at(B7[0] * 0.3))
    K = (0.0, bot_at(0.0))
    body = [T1, T2, T3, T4, T4b, T5, B0, B1] + Bn + [B7, B8, B9]
    # 기수 → LERX: LERX가 동체 옆으로 나오는 만큼 섞는다
    s_ch = _smooth((E - w) / 0.4) if E > 0.0 else 0.0
    mixed = [(a[0] + (b[0] - a[0]) * s_ch, a[1] + (b[1] - a[1]) * s_ch) for a, b in zip(nose, body)]
    return [T0] + mixed + [K]


def skin_cage(m):
    """동체 외피 케이지(고리마다 42점) + 흡입구 통 + 노즐 — 나중에 캣멀-클라크 1단(주름은 날카롭게)."""
    zs = steps(FUS[0][0], 7.0, 0.7) + steps(6.4, DUCT_END_Z, 0.7) + [AFT_IN0] + steps(AFT_IN1, WING_TE_Z, 0.6) + [TAIL_Z]   # 노즐 시작(TAIL_Z)에서 끝 — 뒷면은 나눈 뒤 메운다, 꼬리판은 tail_piece
    rings, red = [], []
    for z in zs:
        half = cage_half(z)
        right = half[1:-1]
        ring = [(0.0, half[0][1], z)] + [(x, y, z) for x, y in right] + [(0.0, half[-1][1], z)] + [(-x, y, z) for x, y in reversed(right)]
        rings.append(ring)
        E = _lerx_e(z) if z >= LERX[-1][2] else 0.0
        w = interp(FUS, z, True)[0]
        red.append(LERX[-1][1] <= z <= LERX[0][1] and E > w + 0.25)
    n = len(rings[0])
    assert n == SKIN_N, n          # 반쪽 점 수(T0 ~ B9 = 17) — 왼쪽은 n - k
    kinds = []
    for a, b in zip(red, red[1:]):
        kk = ["body"] * n
        if a and b:
            for k in (I_T5 - 1, I_T5, n - I_T5 - 1, n - I_T5):   # T4b–T5, T5–B0 (양쪽)
                kk[k] = "red"
        kinds.append(kk)
    vs = m.loft(rings, kinds, smooth=True, caps="start", uvfn=lambda i, k: uv_skin(k, zs[i]))
    for r in vs:
        m.weld += r
    # 주름: LERX 앞전(T5) 줄
    for i in range(len(vs) - 1):
        zi, zj = zs[i], zs[i + 1]
        if _lerx_e(zi) > interp(FUS, zi, True)[0] + 0.1 and _lerx_e(zj) > interp(FUS, zj, True)[0] + 0.1:
            m.creases.append((vs[i][I_T5], vs[i + 1][I_T5]))
            m.creases.append((vs[i][n - I_T5], vs[i + 1][n - I_T5]))
    for s in (1, -1):
        x = s * NAC_X
        # 흡입구: 비스듬한 사각 입구(입술 → 짧은 어두운 통로) → 외피 이음 고리(AFT_IN1). 이음 고리 앞 5점은 외피의 나셀 칸과 같은 자리 → 용접
        w, h, cy, e = interp(NAC, NAC[0][0])
        def tube_ring(cx, cy_, w_, h_, e_, z):
            return [(cx + sx, sy, z) for sx, sy in ((s * (px - NAC_X), py) for px, py in (se_pt(NAC_X, cy_, w_, h_, h_, e_, a) for a in TUBE_ANG))]
        def raked(k, dz=0.0):
            ring = tube_ring(x, cy, w * k, h * k, e, 0.0)
            return [(px, py, INTAKE_BOT + (INTAKE_TOP - INTAKE_BOT) * (py - (cy - h)) / (2 * h) + dz) for px, py, _ in ring]
        w_, h_, cy_, e_ = interp(NAC, AFT_IN1, True)
        lip, end = raked(1.0), tube_ring(x, cy_, w_, h_, e_, AFT_IN1)
        mid = [tuple(a[i] + (b[i] - a[i]) * 0.5 for i in range(3)) for a, b in zip(lip, end)]
        duct_end = tube_ring(x, cy, w * 0.84, h * 0.84, e, DUCT_END_Z)   # 덕트 끝은 수직 벽(위 1.4m · 아래 0.4m 깊이)
        if s < 0:
            lip, end, mid = lip[::-1], end[::-1], mid[::-1]
            inner0, inner1 = duct_end[::-1], raked(0.84, -0.06)[::-1]
        else:
            inner0, inner1 = duct_end, raked(0.84, -0.06)
        vs = m.loft([inner0, inner1, lip, mid, end], ["black", "body", "body", "body"], caps="start")
        m.weld += vs[-1]
        for r in (vs[0], vs[1], vs[2]):    # 덕트 끝 벽(평평하게) · 입술 안·밖 테 주름
            m.creases += [(r[k], r[(k + 1) % len(r)]) for k in range(len(r))]
        # 노즐: 외피 끝 고리(TAIL_Z, 용접) → 흰 보트테일(나셀이 부드럽게 오므라든다) → 금속 작동기 테 → 볼록하게 오므라드는 꽃잎 → 뒤 테
        #       → 안으로 파인 통(검정) → 끝 = 뜨거운 면. 노즐 출구는 TAIL_Z - 1.45(꼬리판 끝 -11.05 바로 앞)
        w_, h_, cy_, _ = interp(NAC, TAIL_Z)
        def cr(z, k):
            r = [(x + s * (px - NAC_X), py, z) for px, py in (se_pt(NAC_X, cy_, w_ * k, h_ * k, h_ * k, 2.0, a) for a in NOZ_ANG)]
            return r if s > 0 else r[::-1]
        noz = [cr(TAIL_Z, 1.0), cr(TAIL_Z - 0.3, 0.97), cr(TAIL_Z - 0.52, 0.92),            # 흰 보트테일
               cr(TAIL_Z - 0.55, 0.9), cr(TAIL_Z - 0.66, 0.91),                                 # 작동기 테
               cr(TAIL_Z - 1.1, 0.85), cr(TAIL_Z - 1.45, 0.78),                                 # 꽃잎
               cr(TAIL_Z - 1.5, 0.72), cr(TAIL_Z - 1.0, 0.68), cr(TAIL_Z - 0.97, 0.66)]         # 뒤 테 → 안쪽 통 → 뜨거운 면
        nn = len(NOZ_ANG)
        petals = ["light", "metal"] * (nn // 2) + ["light"] * (nn % 2)
        vs = m.loft(noz, ["body", "body", "metal", "metal", petals, petals, "nozzle", "black", "hot"], caps="end")
        m.weld += vs[0]
        for r in (vs[2], vs[3], vs[4], vs[6], vs[7]):
            m.creases += [(r[k], r[(k + 1) % nn]) for k in range(nn)]


def project_faces(bm, uvl):
    """나눈 뒤 흰 동체 면 UV를 다시 투영(색 칸 면 — 모든 모서리 UV가 한 색 칸 자리 — 은 그대로)."""
    pts = [uvp for (uvp, _) in PATCH.values()]
    for f in bm.faces:
        if all(l[uvl].uv.y <= SKIN_V1 + 1e-4 for l in f.loops):
            continue                       # 펼친 외피
        u0 = f.loops[0][uvl].uv
        if all(abs(l[uvl].uv.x - u0.x) < 2e-3 and abs(l[uvl].uv.y - u0.y) < 2e-3 for l in f.loops) and any(abs(u0.x - a) < 2e-3 and abs(u0.y - b) < 2e-3 for a, b in pts):
            continue
        n = f.normal
        nx, ny = -n.x, n.z
        for loop in f.loops:
            c = loop.vert.co
            x, y, z = -c.x, c.z, -c.y
            if abs(ny) >= abs(nx) * 0.8:
                loop[uvl].uv = uv_top(x, z) if ny > 0 else uv_bottom(x, z)
            else:
                loop[uvl].uv = uv_side(z, y)


def subdivided(me, name):
    """케이지 메시 → 캣멀-클라크 1단(주름 모서리는 날카롭게) → UV 다시 투영 → 새 메시."""
    ob = bpy.data.objects.new(name + "_cage", me)
    bpy.context.scene.collection.objects.link(ob)
    mod = ob.modifiers.new("sub", 'SUBSURF')
    mod.levels = 1
    mod.render_levels = 1
    mod.uv_smooth = 'NONE'     # 색 칸 면 UV가 그대로(다듬으면 칸 자리가 어긋나 흰 동체로 다시 칠해진다)
    dg = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob)
    bm = bmesh.new()
    bm.from_mesh(out)
    uvl = bm.loops.layers.uv.active
    # 뒷면: 노즐 시작 자리(TAIL_Z)의 열린 테두리를 메운다(꼬리판·꼬리 원뿔이 여기서 뒤로 나간다)
    back = [e for e in bm.edges if e.is_boundary and all(abs(v.co.y + TAIL_Z) < 0.03 for v in e.verts)]
    if back:
        made = bmesh.ops.holes_fill(bm, edges=back, sides=0)["faces"]
        # 오목한 다각형 — 직접 귀 자르기로 삼각형(렌더러가 잘못 나누지 않게), 검정(꼬리판·노즐 사이 그늘진 틈)
        made = bmesh.ops.triangulate(bm, faces=made, quad_method='BEAUTY', ngon_method='EAR_CLIP')["faces"]
        (pu, pv), _ = PATCH["black"]
        for f in made:
            for l in f.loops:
                l[uvl].uv = (pu, pv)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    project_faces(bm, uvl)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(40):
            e.smooth = False
    bm.to_mesh(out)
    bm.free()
    out.name = name
    return out


def _smin(a, b, k):
    if k <= 1e-6:
        return min(a, b)
    h = max(k - abs(a - b), 0.0) / k
    return min(a, b) - h * h * k * 0.25


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


# 5각형 카나드(x>0): 단면 셋 = (앞전 x, 뒷전 x, y, 앞전 z, 뒷전 z, 두께).
# 뿌리는 스트레이크(LERX) 앞전을 따라 비스듬히 놓여 LERX 면에서 자라 나온다(뿌리 앞 = 동체 옆, 뿌리 뒤 = LERX 속).
# 평면형 꼭짓점 다섯: 뿌리 앞전, 끝 앞전, 끝 뒷전, 꺾임 뒷전, 뿌리 뒷전
CANARD = [(0.8, 1.2, 0.15, 6.7, 4.85, 0.12), (1.95, 1.95, 0.15, 6.0, 4.6, 0.08), (2.75, 2.75, 0.13, 5.45, 4.98, 0.045)]


def canards(m):
    for s in (1, -1):
        rings = []
        for xl, xt, y, zle, zte, t in CANARD:
            c = zle - zte
            e = 0.07
            def at(f):
                return s * (xl + (xt - xl) * f)
            rings.append([(at(0.0), y, zle), (at(e), y + t * 0.45, zle - c * e), (at(0.45), y + t * 0.5, zle - c * 0.45), (at(1.0), y, zte),
                          (at(0.45), y - t * 0.4, zle - c * 0.45), (at(e), y - t * 0.35, zle - c * e)])
        m.loft(rings, "body", smooth=True, around=["red", "body", "body", "body", "body", "red"])


def wings(m):
    for s in (1, -1):
        # LERX는 동체 외피(skin)의 차인 — 붉은 앞전은 외피의 앞전 띠 면(skin 참고)
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


def sphere(m, c, R, kind, n=12, rows=6):
    rings = []
    for i in range(rows + 1):
        t = -math.pi / 2 + i * math.pi / rows
        rr = max(math.cos(t) * R, 0.002)
        rings.append([(c[0] + math.cos(a) * rr, c[1] + math.sin(t) * R, c[2] + math.sin(a) * rr) for a in (k * math.tau / n for k in range(n))])
    m.loft(rings, kind)


def blade(m, x, y, z, hgt, up=True):
    """블레이드 안테나(앞이 낮고 뒤로 기운 얇은 판). (x, y, z) = 뿌리 가운데."""
    sg = 1.0 if up else -1.0
    def sec(h, zl, zt, t):
        yy = y + sg * h
        return [(x, yy, zl), (x + t, yy, (zl + zt) * 0.5), (x, yy, zt), (x - t, yy, (zl + zt) * 0.5)]
    m.loft([sec(-0.05, z + 0.2, z - 0.2, 0.025), sec(hgt, z - 0.1, z - 0.24, 0.01)], "light", smooth=False)


def details(m):
    """작은 부품: 캐노피 틀 둘, 캐노피 앞 IRST 구, 기수 피토관, 등·배 블레이드 안테나, 나셀 밑 벤트럴 핀(바깥으로 기움)."""
    for z in (7.55, 5.05):
        m.loft([canopy_ring(z + 0.06, 1.01), canopy_ring(z + 0.02, 1.05), canopy_ring(z - 0.02, 1.05), canopy_ring(z - 0.06, 1.01)], "light", caps=False)
    sphere(m, (0.3, top_y(0.3, 8.95) - 0.03, 8.95), 0.13, "glass")
    w, ht, hb, cy = interp(FUS, FUS[0][0])
    beam(m, (0.0, FUS[0][4], FUS[0][0] - 0.2), (0.0, FUS[0][4], FUS[0][0] + 0.9), 0.022, "metal")
    blade(m, 0.0, top_y(0.0, -0.45) - 0.02, -0.45, 0.42)
    w, ht, hb, cy = interp(FUS, 2.0, True)
    blade(m, 0.0, cy - hb + 0.02, 2.0, 0.3, up=False)
    pw, pht, phb = interp(PAN, -5.0, True)
    blade(m, 0.6, PAN_Y - phb + 0.02, -5.0, 0.25, up=False)
    for sd in (1, -1):
        cant = math.radians(25)
        bx, by = sd * (NAC_X + 0.25), -1.02
        def vf(h, zle, zte, t):
            x = bx + sd * math.sin(cant) * h
            y = by - math.cos(cant) * h
            nx, ny = math.cos(cant), sd * math.sin(cant)
            c = zle - zte
            return [(x, y, zle), (x + nx * t * 0.5, y + ny * t * 0.5, zle - c * 0.3), (x, y, zte), (x - nx * t * 0.5, y - ny * t * 0.5, zle - c * 0.3)]
        m.loft([vf(0.0, -6.7, -9.1, 0.12), vf(0.8, -8.3, -9.35, 0.05)], "body", smooth=True, around=["red", "body", "body", "red"])
    for sd in (1, -1):
        x = sd * NAC_X
        # 흡입구 속: 압축기 면(금속 원판 + 허브 + 살) — 덕트 끝 벽 바로 앞
        nw, nh, ncy, _ = NAC[0][1:]
        zc = DUCT_END_Z + 0.04
        n = 16
        def er(rx, ry, z):
            return [(x + math.cos(a) * rx, ncy + math.sin(a) * ry, z) for a in (k * math.tau / n for k in range(n))]
        m.loft([er(nw * 0.5, nh * 0.42, zc), er(0.13, 0.13, zc)], "metal", caps=False)
        m.loft([er(0.13, 0.13, zc), er(0.08, 0.08, zc + 0.1), er(0.01, 0.01, zc + 0.17)], "light", caps="end")
        for k in range(8):
            a = k * math.tau / 8 + 0.2
            beam(m, (x + math.cos(a) * 0.13, ncy + math.sin(a) * 0.13, zc + 0.01), (x + math.cos(a) * nw * 0.48, ncy + math.sin(a) * nh * 0.4, zc + 0.01), 0.018, "black", r2=0.008)
        # 흡입구 윗벽 램프판(입술 안쪽에서 비스듬히 덕트로)
        ytop = ncy + nh * 0.84
        beam(m, (x, ytop - 0.03, INTAKE_TOP - 0.12), (x, ytop - 0.24, INTAKE_TOP - 0.85), nw * 0.78, "metal", r2=0.02)
        # 노즐 속 화염 안정기(뜨거운 면 뒤 — 고리 + 살)
        tw, th, tcy, _ = interp(NAC, TAIL_Z)
        zf = TAIL_Z - 1.06     # 뜨거운 면(TAIL_Z - 0.97) 바로 뒤
        def nr(k, z):
            return [(x + math.cos(a) * tw * k, tcy + math.sin(a) * th * k, z) for a in (j * math.tau / n for j in range(n))]
        m.loft([nr(0.38, zf), nr(0.46, zf), nr(0.46, zf - 0.04), nr(0.38, zf - 0.04), nr(0.38, zf)], "metal", caps=False)
        for k in range(6):
            a = k * math.tau / 6
            beam(m, (x + math.cos(a) * tw * 0.08, tcy + math.sin(a) * th * 0.08, zf - 0.02), (x + math.cos(a) * tw * 0.62, tcy + math.sin(a) * th * 0.62, zf - 0.02), 0.014, "metal")
        # 수직미익 끝 팟(앞은 흰 동체, 뒤 끝은 빛나는 충돌 방지등)
        cant = math.radians(15)
        fx, fy = sd * (NAC_X - math.sin(cant) * 3.2), 0.02 + math.cos(cant) * 3.2
        rings = [se_ring(fx, fy, z, r, r, r, 2.0, 10) for z, r in ((-7.7, 0.01), (-7.9, 0.05), (-8.2, 0.06), (-9.5, 0.06), (-9.8, 0.05), (-9.95, 0.03))]
        m.loft(rings, ["body", "body", "body", "body", "lens"], caps="end")
        # 받음각 센서(기수 옆 작은 날개)
        w, ht, hb, cy = interp(FUS, 10.7, True)
        beam(m, (sd * (w - 0.02), cy + 0.05, 10.75), (sd * (w + 0.11), cy + 0.07, 10.58), 0.035, "metal", r2=0.006)
        # 캐노피 레일(캐노피 아래 양옆 — 유리와 동체 사이)
        pts = [canopy_ring(z)[0 if sd > 0 else 8] for z in steps(8.2, 3.9, 0.43)]
        for a, b in zip(pts, pts[1:]):
            beam(m, a, b, 0.028, "light")
        # 날개 밑 조종면 작동기 페어링 둘(뒷전 뒤로 조금 나온다)
        for xw in (4.4, 6.4):
            if xw <= WING[1][0]:
                k = (xw - WING[0][0]) / (WING[1][0] - WING[0][0])
                yc_, tt = WING_Y + (WING_Y - 0.03 - WING_Y) * k, 0.32 + (0.24 - 0.32) * k
            else:
                k = (xw - WING[1][0]) / (WING[2][0] - WING[1][0])
                yc_, tt = (WING_Y - 0.03) + ((WING_Y - 0.1) - (WING_Y - 0.03)) * k, 0.24 + (0.08 - 0.24) * k
            te = wing_te(xw)
            yb_ = yc_ - 0.12 * tt - 0.04
            rings = [se_ring(sd * xw, yb_, z, r * 1.5, r, r, 2.4, 8) for z, r in ((te + 1.3, 0.01), (te + 1.0, 0.06), (te + 0.5, 0.075), (te, 0.07), (te - 0.25, 0.04), (te - 0.33, 0.01))]
            m.loft(rings, "body", caps=True)


def beam(m, p0, p1, r, kind="metal", r2=None):
    """두 점 사이의 네모 기둥(반폭 r, 두 번째 축 반폭 r2 — 없으면 r)."""
    r2 = r if r2 is None else r2
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
        rings.append([tuple(p[i] + a[i] * sa * r + b[i] * sb * r2 for i in range(3)) for sa, sb in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    m.loft(rings, kind, smooth=False)


def tail_piece(m):
    """꼬리판(가운데 몸통 뒤 — 엔진 사이 납작한 판)과 꼬리 원뿔(척추 끝). 동체 외피 뒷면(TAIL_Z)에서 시작(앞은 열림 — 외피 뒷면이 가린다)."""
    rings = []
    for z in steps(TAIL_Z, PAN[-1][0], 0.3):
        pw, pht, phb = interp(PAN, z, True)
        k = 0.99 + 0.01 * _smooth((TAIL_Z - z) / 0.3)
        rings.append(se_ring(0.0, PAN_Y, z, pw * k, pht * k, phb * k, PAN_E, 24))
    m.loft(rings, "body", caps="end")
    rings = []
    for z in steps(TAIL_Z, FUS[-1][0], 0.3):
        w, ht, hb, cy = interp(FUS, z, True)
        k = 0.95 + 0.05 * _smooth((TAIL_Z - z) / 0.3)
        rings.append(se_ring(0.0, cy, z, w * k, ht * k, hb * k, E_FUS, 16))
    m.loft(rings, ["body"] * (len(rings) - 2) + ["black"], caps="end")   # 끝: 검은 캡(감속 낙하산 덮개)


def laser_pod(m):
    # 받침: 좌우 대각선 뼈대 둘 — 짧은 윗변은 포드 밑을 따라, 두 다리가 비스듬히 동체 등 발판으로(밑변 없음)
    zr, zf = -6.4, 1.0          # 밑변(동체)
    tr, tf = -5.0, -0.6         # 윗변(포드 밑)
    for s in (1, -1):
        def low(z):
            return (s * 1.2, top_y(s * 1.2, z) - 0.08, z)
        def high(z):
            return (s * 0.42, POD_Y - POD_R * 0.8, z)
        beam(m, high(tr), high(tf), 0.1)
        beam(m, low(zr), high(tr), 0.1)
        beam(m, low(zf), high(tf), 0.1)
        # 발판(뼈대 밑변 양 끝이 동체 등에 볼트로 붙는 납작한 판)
        for z in (zr, zf):
            y = top_y(s * 1.2, z) - 0.02
            beam(m, (s * 1.2, y, z - 0.32), (s * 1.2, y, z + 0.32), 0.22, "metal", r2=0.05)
    # 포드 몸통: 뒤는 좁아지는 방열부, 앞은 헤드 받침 고리
    n = 18
    P = [(POD_Z0, 0.35), (POD_Z0 + 0.6, 0.62), (POD_Z0 + 1.4, POD_R), (POD_Z1 - 0.3, POD_R), (POD_Z1, POD_R * 0.92)]
    rings = [[(math.cos(a) * r, POD_Y + math.sin(a) * r, z) for a in (k * math.tau / n for k in range(n))] for z, r in P]
    m.loft(rings, ["black", "light", "pod", "metal"])
    # 몸통 고리(양 끝이 몸통 속으로 들어가는 띠 — 막음 없음): 받침 고리(뼈대 윗변 끝), 붉은 띠, 가는 이음 고리
    def band(z, hw, dr, kind):
        rings = [[(math.cos(a) * r, POD_Y + math.sin(a) * r, zz) for a in (k * math.tau / n for k in range(n))]
                 for r, zz in ((POD_R - 0.01, z - hw - 0.01), (POD_R + dr, z - hw), (POD_R + dr, z + hw), (POD_R - 0.01, z + hw + 0.01))]
        m.loft(rings, kind, caps=False)
    band(tr, 0.16, 0.05, "metal")
    band(tf, 0.16, 0.05, "metal")
    band(POD_Z1 - 0.6, 0.1, 0.03, "red")
    band(-2.8, 0.1, 0.03, "red")
    for z in (-3.9, -1.7, POD_Z0 + 1.9):
        band(z, 0.018, 0.012, "light")
    # 옆 배관 둘(아래 옆, 뼈대 윗변을 피해) — 뒤 방열부에서 앞 헤드 받침까지
    for sd in (1, -1):
        a = -math.pi / 2 + sd * 1.0
        cx, cy = math.cos(a) * (POD_R + 0.03), POD_Y + math.sin(a) * (POD_R + 0.03)
        rings = [[(cx + math.cos(t) * rr, cy + math.sin(t) * rr, z) for t in (k * math.tau / 8 for k in range(8))]
                 for z, rr in ((-4.7, 0.02), (-4.55, 0.06), (POD_Z1 - 0.45, 0.06), (POD_Z1 - 0.32, 0.02))]
        m.loft(rings, "metal")
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
    return (0.25 - u) * 40.0, (v - 0.5) / 0.3 * 19.5 - 11.5


def inv_bottom(u, v):
    return (0.75 - u) * 40.0, (v - 0.5) / 0.3 * 19.5 - 11.5


def inv_side(u, v):
    return (u - 0.02) / 0.56 * 19.5 - 11.5, (v - 0.8) / 0.09 * 6.0 - 2.0


def inv_skin(u, v):
    return u * SKIN_N, v / SKIN_V1 * (SKIN_Z1 - SKIN_Z0) + SKIN_Z0


REGION_UV = {"top": (0.0, 0.5, 0.5, 0.8), "bottom": (0.5, 1.0, 0.5, 0.8), "side": (0.0, 0.6, 0.8, 0.89), "skin": (0.0, 1.0, 0.0, SKIN_V1)}   # u0, u1, v0, v1


class Painter:
    """유니티 좌표로 칠한다. 선·점은 거리로 부드럽게(계단 없음), 면은 3배 표본으로 가장자리를 부드럽게.
    어두운 선(lines)·밝은 선(hl)·때(dirt, 나중에 흐림)는 따로 모아 마지막에 한 번에 얹는다."""
    def __init__(self, img):
        self.img = img
        self.size = img.shape[0]
        self.lines = np.zeros(img.shape[:2], dtype=np.float32)
        self.hl = np.zeros(img.shape[:2], dtype=np.float32)
        self.dirt = np.zeros(img.shape[:2], dtype=np.float32)
        self.uvf = {"top": uv_top, "bottom": uv_bottom, "side": uv_side, "skin": uv_skin}
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


# 외피 패널 칸(점 번호·z 경계) — 이음매와 색조가 같은 칸을 쓴다
SKIN_KB = [0.0, 2.0, 3.5, 5.5, 9.5, 11.5, 14.0, 16.5, 19.0, 21.0]
SKIN_ZB = [11.75, 10.5, 8.6, 6.2, 3.2, 0.6, -2.6, -4.1, -5.6, -7.0, -8.4]


def panel_tone(img, rng):
    """패널마다 아주 조금 다른 색조(같은 흰색도 칸마다 다르게 바랜다)."""
    N = img.shape[0]
    zb = np.array(sorted(SKIN_ZB))
    kb = np.array(sorted(set(SKIN_KB + [SKIN_N - k for k in SKIN_KB])))
    xb = np.array(sorted([0.0, 1.5, 2.65, 3.7, 4.7, 6.3, 7.9] + [-1.5, -2.65, -3.7, -4.7, -6.3, -7.9]))
    yb = np.array([-1.3, -0.6, 0.05, 1.0, 2.0, 2.78])
    tab = (1 + 0.035 * (rng.random(4096) - 0.5)).astype(np.float32)
    for reg, inv in (("top", inv_top), ("bottom", inv_bottom), ("side", inv_side), ("skin", inv_skin)):
        u0, u1, v0, v1 = REGION_UV[reg]
        c0, c1, r0, r1 = int(u0 * N), int(u1 * N), int(v0 * N), int(v1 * N)
        vv, uu = np.mgrid[r0:r1, c0:c1].astype(np.float32)
        a, b = inv((uu + 0.5) / N, (vv + 0.5) / N)
        if reg == "side":
            i, j = np.digitize(a, zb), np.digitize(b, yb)
        elif reg == "skin":
            i, j = np.digitize(b, zb), np.digitize(a, kb)
        else:
            i, j = np.digitize(b, zb), np.digitize(a, xb)
        f = tab[(i * 31 + j * 7 + {"top": 0, "bottom": 977, "side": 1999, "skin": 2999}[reg]) % 4096]
        img[r0:r1, c0:c1, :3] *= f[..., None]


def paint_skin(P):
    """동체 외피(펼침): 점 번호 k(오른쪽 1~20, 왼쪽 = 42 - k)와 z로 칠한다.
    점 번호 뜻(LERX 있는 몸통): 0 척추 위 · 2 척추 어깨 · 3 데크/LERX 뿌리 · 4 LERX 위 · 6 LERX 앞전 · 8 LERX 밑 ·
    9~17 나셀(9 바깥 위, 10~11 바깥 옆, 12~15 밑, 16~17 안쪽 옆 — z -2.9 뒤) · 18 터널 모서리 · 19~20 터널 천장/배 · 21 용골.
    기수(z 6.2 앞)는 같은 번호가 초타원 각도(1: 65°, 3: 24°, 6: 0°=옆, 13: -28°, 21: 아래)."""
    N_ = SKIN_N

    def K(sd, k):
        return k if sd > 0 else N_ - k
    RADOME = (0.74, 0.75, 0.74)
    # 레이돔·기수 끝(둘레 전체)
    P.fill("skin", [(0, 10.5), (N_, 10.5), (N_, 12.6), (0, 12.6)], RADOME)
    P.fill("skin", [(0, 11.75), (N_, 11.75), (N_, 12.6), (0, 12.6)], BLACK)
    # 가로 이음매(둘레 전체) + 리벳
    for z in SKIN_ZB[1:]:
        P.line("skin", (0, z), (N_, z))
        if z < 3.5:
            P.rivets("skin", (0.2, z - 0.07), (N_ - 0.2, z - 0.07), 0.16)
    # 눈부심 방지(캐노피 앞 등 — 둘레 0 양쪽)
    for a, b in ((0.0, 0.9), (N_ - 0.9, N_)):
        P.fill("skin", [(a, 9.6), (b, 9.6), (b, 8.5), (a, 8.5)], (0.52, 0.53, 0.55))
    for sd in (1, -1):
        # 세로 이음매: 척추 어깨, LERX 뿌리, LERX 앞전 덮개, 배
        P.line("skin", (K(sd, 2), 8.4), (K(sd, 2), -9.5))
        P.rivets("skin", (K(sd, 2.15), 3.0), (K(sd, 2.15), -9.4), 0.18)
        P.line("skin", (K(sd, 3.5), 6.0), (K(sd, 3.5), -9.5))
        P.line("skin", (K(sd, 5.5), 6.0), (K(sd, 5.5), -1.4))
        P.line("skin", (K(sd, 19.5), 6.0), (K(sd, 19.5), -9.5))
        # 나셀(z -2.9 뒤): 바깥 위·옆 아래 모서리·밑 안쪽·안쪽 옆
        for k in (9.5, 11.5, 14.0, 16.5):
            P.line("skin", (K(sd, k), -2.95), (K(sd, k), -9.55))
        P.rivets("skin", (K(sd, 9.65), -3.1), (K(sd, 9.65), -9.4), 0.2)
        P.rivets("skin", (K(sd, 11.35), -3.1), (K(sd, 11.35), -9.4), 0.2)
        # ── 기수 옆(초타원 각도 칸): 전자 장비 칸·정압 구멍·편대등·사출 경고·안내 문구
        P.rect("skin", K(sd, 3.6), 9.9, K(sd, 7.6), 9.2, rivets=0.09)
        for j in range(3):
            P.dot("skin", (K(sd, 4.8), 10.25 - j * 0.12), 1.6, k=0.8)
        P.fill("skin", [(K(sd, 5.85), 9.4), (K(sd, 6.15), 9.4), (K(sd, 6.15), 8.65), (K(sd, 5.85), 8.65)], (0.82, 0.92, 0.6))
        P.rect("skin", K(sd, 3.4), 8.3, K(sd, 7.4), 7.6, rivets=0.09)
        P.fill("skin", [(K(sd, 4.4), 7.25), (K(sd, 4.4), 7.85), (K(sd, 2.5), 7.55)], RED)
        P.fill("skin", [(K(sd, 4.0), 7.38), (K(sd, 4.0), 7.72), (K(sd, 3.0), 7.55)], (0.95, 0.95, 0.92))
        P.stencil("skin", K(sd, 5.0) if sd > 0 else K(sd, 6.6), 7.3, rows=2, width=1.6)
        # 기수 밑 앞바퀴 문(용골 양옆)
        P.rect("skin", K(sd, 19.6), 7.0, K(sd, 21.0), 5.6, rivets=0.1)
        P.grime("skin", [(K(sd, 19.8), 5.6), (K(sd, 20.9), 5.6), (K(sd, 20.8), 3.8), (K(sd, 20.0), 3.8)], 0.45)
        # ── 앞 몸통(LERX): 윗면 점검판·걷는 곳 점선·붉은 줄
        P.rect("skin", K(sd, 3.7), 2.6, K(sd, 4.7), 1.6, rivets=0.1)
        P.rect("skin", K(sd, 3.7), 1.0, K(sd, 4.5), 0.2, rivets=0.1)
        P.stencil("skin", K(sd, 3.8) if sd > 0 else K(sd, 4.6), 0.05, rows=2, width=0.8)
        for a, b in (((3.7, -2.4), (5.0, -2.4)), ((5.0, -2.4), (5.0, -5.6)), ((5.0, -5.6), (3.7, -5.6)), ((3.7, -5.6), (3.7, -2.4))):
            P.dashed("skin", (K(sd, a[0]), a[1]), (K(sd, b[0]), b[1]), dash=0.25)
        P.fill("skin", [(K(sd, 4.0), -0.6), (K(sd, 5.3), -0.6), (K(sd, 5.3), -0.68), (K(sd, 4.0), -0.68)], RED)
        P.rect("skin", K(sd, 3.8), -7.8, K(sd, 5.0), -8.9, rivets=0.12)
        P.grime("skin", [(K(sd, 3.9), -8.9), (K(sd, 4.9), -8.9), (K(sd, 4.8), -9.5), (K(sd, 4.0), -9.5)], 0.45)
        # 척추(포드 밑): 점검판·환기 살
        P.rect("skin", K(sd, 0.3), -8.2, K(sd, 1.5), -8.9, rivets=0.1)
        for j in range(5):
            z = -6.5 - j * 0.14
            P.line("skin", (K(sd, 0.4), z), (K(sd, 1.2), z), w=2.2)
        # ── 나셀 바깥 옆(10~11): 점검판·안내 문구·배기 근처 환기 살
        P.rect("skin", K(sd, 10.15), -3.2, K(sd, 10.95), -3.9, rivets=0.12)
        P.rect("skin", K(sd, 10.1), -4.2, K(sd, 10.95), -5.2, rivets=0.12)
        P.rect("skin", K(sd, 10.2), -5.6, K(sd, 10.8), -6.5)
        P.rect("skin", K(sd, 10.1), -6.9, K(sd, 10.95), -7.9, rivets=0.12)
        P.grime("skin", [(K(sd, 10.4), -5.2), (K(sd, 10.9), -5.2), (K(sd, 10.9), -6.8), (K(sd, 10.5), -6.8)], 0.4)
        P.stencil("skin", K(sd, 11.05) if sd > 0 else K(sd, 11.4), -4.3, rows=2, width=0.4)
        for j in range(6):
            z = -8.6 - j * 0.12
            P.line("skin", (K(sd, 10.3), z), (K(sd, 10.8), z), w=2.0, k=0.8, hl=False)
        # 나셀 밑(12~15): 가로 이음매·흡입구 경고 삼각형
        for z in (-4.0, -6.4, -8.2):
            P.line("skin", (K(sd, 11.5), z), (K(sd, 16.5), z))
        P.fill("skin", [(K(sd, 12.9), -3.0), (K(sd, 14.1), -3.0), (K(sd, 13.5), -3.45)], RED)
        # 나셀 둘레 붉은 띠(노즐 앞)
        P.fill("skin", [(K(sd, 9.0), -8.46), (K(sd, 17.9), -8.46), (K(sd, 17.9), -8.56), (K(sd, 9.0), -8.56)], RED)
        # 터널 천장: 주 바퀴 문·무장창 문
        P.rect("skin", K(sd, 18.2), -3.6, K(sd, 19.4), -6.6, rivets=0.12)
        P.grime("skin", [(K(sd, 18.3), -6.6), (K(sd, 19.3), -6.6), (K(sd, 19.2), -9.0), (K(sd, 18.4), -9.0)], 0.55)
        P.rect("skin", K(sd, 19.7), -1.0, K(sd, 21.0), -6.0)
        P.rect("skin", K(sd, 19.0), 4.6, K(sd, 20.5), 2.8, rivets=0.1)
        P.stencil("skin", K(sd, 19.8) if sd > 0 else K(sd, 20.8), -1.3, rows=2, width=0.6)
    # 기관포(오른쪽 LERX 뿌리) + 그을음, 급유구, 안테나 받침
    P.fill("skin", [(3.6, 3.3), (4.2, 3.3), (4.2, 2.9), (3.6, 2.9)], (0.08, 0.08, 0.09))
    P.grime("skin", [(3.6, 2.9), (4.2, 2.9), (4.4, 1.2), (3.4, 1.2)], 0.9)
    P.circle("skin", (0.8, -1.0), 0.12)
    P.fill("skin", [(N_ - 0.1, -0.2), (N_, -0.2), (N_, -0.75), (N_ - 0.1, -0.75)], (0.4, 0.41, 0.43))
    P.fill("skin", [(0.0, -0.2), (0.1, -0.2), (0.1, -0.75), (0.0, -0.75)], (0.4, 0.41, 0.43))


def paint_plates(P):
    """판 부품(투영): 날개·카나드 끝(위·아래), 수직미익·벤트럴 핀(옆), 꼬리판(위·아래), 흡입구 통(옆·아래)."""
    for s in (1, -1):
        for reg in ("top", "bottom"):
            # 날개: 앞·뒤 날개보, 플래퍼론·플랩(경계·칸막이), 날개 칸
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
            P.grime(reg, [(s * 2.95, wing_te(2.95) + 1.1), (s * 8.4, wing_te(8.4) + 0.9), (s * 8.4, wing_te(8.4) + 0.7), (s * 2.95, wing_te(2.95) + 0.9)], 0.6)
            # 날개 끝 붉은 띠 + 검정 끝 + 항법등(오른쪽 초록, 왼쪽 빨강)
            P.fill(reg, [(s * 7.55, wing_le(7.55) + 0.3), (s * 8.05, wing_le(8.05) + 0.3), (s * 8.05, wing_te(8.05) - 0.3), (s * 7.55, wing_te(7.55) - 0.3)], RED)
            P.fill(reg, [(s * 8.15, wing_le(8.15) + 0.3), (s * 9.0, wing_le(8.8) + 0.3), (s * 9.0, wing_te(8.8) - 0.3), (s * 8.15, wing_te(8.15) - 0.3)], BLACK)
            P.disk(reg, (s * 8.6, wing_le(8.6) - 0.25), 0.07, (0.2, 0.85, 0.35) if s > 0 else (0.9, 0.12, 0.1))
            # 카나드: 검정 끝 + 뿌리 이음매
            P.fill(reg, [(s * 2.4, 5.72), (s * 2.9, 5.35), (s * 2.9, 4.94), (s * 2.4, 4.78)], BLACK)
            P.line(reg, (s * 1.3, 6.3), (s * 1.6, 4.75))
            if reg == "bottom":
                for x in (4.2, 6.0):
                    z = (wing_le(x) + wing_te(x)) * 0.5
                    P.rect(reg, s * (x - 0.12), z + 0.45, s * (x + 0.12), z - 0.45, rivets=0.12)
            # 꼬리판: 점검판·이음매
            P.rect(reg, s * 0.15, -9.9, s * 0.9, -10.6, rivets=0.1)
            P.line(reg, (s * 1.0, -9.7), (s * 0.9, -10.9))
    # 꼬리판 밑 그을음(노즐 옆)
    for s in (1, -1):
        P.grime("bottom", [(s * 1.0, -9.7), (s * 1.4, -9.7), (s * 1.25, -10.9), (s * 0.95, -10.9)], 0.8)
    # ── 옆: 수직미익(끝 붉은 띠·검정 줄·방향타·이음매·점검판·편대등·항법등)
    P.fill("side", [(-11.0, 2.78), (-4.0, 2.78), (-4.0, 3.6), (-11.0, 3.6)], RED)
    P.fill("side", [(-11.0, 2.48), (-4.0, 2.48), (-4.0, 2.66), (-11.0, 2.66)], BLACK)
    P.line("side", (-8.55, 0.4), (-8.95, 2.6))
    P.line("side", (-8.95, 2.6), (-9.75, 2.6))
    P.line("side", (-5.6, 1.0), (-9.4, 1.0))
    P.rivets("side", (-5.3, 0.3), (-9.2, 0.3), 0.15)
    P.rect("side", -6.4, 0.75, -7.4, 1.35, rivets=0.12)
    P.rect("side", -7.6, 1.55, -8.4, 2.15)
    P.line("side", (-5.0, 0.3), (-7.6, 2.75))
    P.rivets("side", (-5.2, 0.35), (-7.65, 2.6), 0.15)
    P.stencil("side", -7.5, 1.0, rows=2, width=-0.45)
    P.fill("side", [(-6.15, 0.7), (-6.3, 0.7), (-6.75, 2.5), (-6.6, 2.5)], (0.82, 0.92, 0.6))
    P.disk("side", (-9.55, 2.4), 0.06, (0.95, 0.95, 0.92))
    # 흡입구 통 옆: 이음매·점검판
    P.line("side", (-2.0, -1.3), (-2.0, 0.1))
    P.rect("side", -1.4, -0.3, -1.9, -0.9, rivets=0.1)


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
    paint_skin(P)
    paint_plates(P)
    P.finish()
    # 노즐 앞 그을음(외피: 나셀 둘레 9~17번, z -8.6 뒤로 점점 짙게)
    r1 = int(SKIN_V1 * N)
    vv = (np.arange(r1, dtype=np.float32) + 0.5) / N
    zz = vv / SKIN_V1 * (SKIN_Z1 - SKIN_Z0) + SKIN_Z0
    soot = np.clip((-8.6 - zz) / 1.0, 0, 1) ** 1.3
    kk = (np.arange(N, dtype=np.float32) + 0.5) / N * SKIN_N
    ring = np.minimum(np.clip((kk - 9.0) / 1.0, 0, 1) * np.clip((18.0 - kk) / 1.0, 0, 1)
                      + np.clip((kk - (SKIN_N - 18.0)) / 1.0, 0, 1) * np.clip(((SKIN_N - 9.0) - kk) / 1.0, 0, 1), 1.0)
    img[:r1, :, :] *= (1 - 0.5 * soot[:, None] * ring[None, :])[..., None]
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
    return im


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


def preview(objs, im, path):
    """미리보기(ACO_LACE_PREVIEW=경로 접두어): 텍스처 재질 + 워크벤치 렌더 여러 각도."""
    import mathutils
    mat = bpy.data.materials.new("LacePreview")
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
    sc.display.shading.cavity_type = 'BOTH'
    sc.display.shading.curvature_ridge_factor = 0.0    # 볼록한 곳 밝히기 끔(검은 면이 회색으로 떠 보이지 않게 — 유니티와 비슷하게)
    sc.display.shading.cavity_ridge_factor = 0.0
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.35
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.16, 0.2, 0.26)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    shots = {
        "front34": ((20.0, 9.0, 20.0), (0.0, 0.0, 0.0)),
        "rear34": ((-18.0, 8.0, -21.0), (0.0, 0.0, -1.0)),
        "top": ((0.0, 40.0, 0.01), (0.0, 0.0, 0.0)),
        "side": ((34.0, 0.5, 0.5), (0.0, 0.5, 0.0)),
        "below": ((12.0, -16.0, 18.0), (0.0, 0.0, 0.0)),
        "nose": ((6.0, 2.5, 17.0), (0.0, 0.5, 7.0)),
        "pod": ((5.0, 5.5, 4.0), (0.0, 1.8, -1.5)),
        "tail": ((-5.0, 2.5, -17.0), (0.0, 0.0, -9.5)),
        "intake": ((6.0, -1.6, 6.0), (1.8, -0.4, -2.0)),
        "side_low": ((16.0, -3.5, 0.0), (0.0, -0.3, -3.0)),
        "front_low": ((2.5, -2.5, 16.0), (0.0, -0.3, 0.0)),
        "lerx_top": ((5.0, 3.5, 7.0), (1.6, 0.2, 1.5)),
        "lerx_bot": ((5.0, -3.0, 6.0), (1.6, -0.2, 0.5)),
        "belly_fwd": ((1.5, -3.2, 4.5), (0.6, -0.3, -1.5)),
        "rear_top": ((-5.0, 4.0, -15.0), (1.6, 0.0, -9.0)),
        "rear_side": ((8.0, 0.8, -13.0), (1.8, -0.3, -9.0)),
        "rear_low": ((-4.5, -3.5, -14.5), (1.6, -0.5, -9.0)),
        "rear_in": ((0.2, -2.2, -13.0), (1.5, -0.4, -9.4)),
        "mouth": ((2.4, -1.0, 3.0), (2.05, -0.6, -1.4)),
        "finTip": ((4.2, 4.6, -12.0), (1.22, 3.1, -8.8)),
        "noz_side": ((7.5, -0.4, -9.8), (2.05, -0.4, -9.8)),
        "noz_side_hi": ((6.5, 2.0, -8.5), (2.05, -0.3, -9.8)),
        "noz_side_lo": ((6.5, -2.8, -9.0), (2.05, -0.5, -9.8)),
    }
    if os.environ.get("ACO_LACE_WIRE"):
        black = bpy.data.materials.new("Wire")
        black.diffuse_color = (0.0, 0.0, 0.0, 1.0)
        for o in list(objs):
            w = o.copy()
            w.data = o.data.copy()
            w.data.materials.clear()
            w.data.materials.append(black)
            sc.collection.objects.link(w)
            mod = w.modifiers.new("wire", 'WIREFRAME')
            mod.thickness = 0.012
            mod.use_replace = True
            mod.use_even_offset = False
        sc.display.shading.color_type = 'MATERIAL'
        for o in objs:
            o.data.materials.clear()
            g = bpy.data.materials.new("grey")
            g.diffuse_color = (0.8, 0.8, 0.8, 1.0)
            o.data.materials.append(g)
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
    canards(body)
    details(body)
    laser_pod(body)
    tail_piece(body)
    b = link(body.finish("LaserAce_Body"))
    sk = Mesh()
    skin_cage(sk)
    skin = link(subdivided(sk.finish("LaserAce_SkinCage"), "LaserAce_Skin"))
    with bpy.context.temp_override(active_object=b, selected_editable_objects=[b, skin], object=b):
        bpy.ops.object.join()
    b.data.name = "LaserAce_Body"
    h = link(head(), (0, POD_Y, POD_Z1))
    lz = HEAD_LEN
    l = link(lens(), (0, 0, lz), parent=h)
    print("TRIS", sum(len(p.vertices) - 2 for o in (b, h, l) for p in o.data.polygons))
    print("LENS world (unity)", (0, POD_Y, POD_Z1 + lz))
    write_fbx(os.path.join(OUT, NAME + ".fbx"), [b, h, l])
    if os.environ.get("ACO_LACE_PREVIEW"):
        preview([b, h, l], im, os.environ["ACO_LACE_PREVIEW"])
    print("DONE")
