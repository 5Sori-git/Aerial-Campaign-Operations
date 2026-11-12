"""XF-W(가칭) — NPC 기체. YF-23 느낌 동체 + W 주익(안쪽 뒤로 · 바깥 앞으로 젖힘, 뒷전도 같이 꺾음) + 5각형 카나드 + 바깥으로 50° 기운 러더베이터 두 장.
형상안: 가는 기수(폭 2m 안쪽)가 조종석 뒤까지 이어지다 날개 뿌리에서 벌어지고, 날개 밑 흡입구에서 엔진 나셀이 위로 휘어 올라
낮고 얇은 등 양옆으로 솟는다. 두 나셀 사이 아래는 홈(동체 아래를 판다).
NPC 기체라 한 덩어리 로우폴리(움직이는 면 · 기어 · 조종석 없음).

좌표(블렌더): x 옆(+ 오른쪽), y 뒤(+ = 꼬리, 기수는 -y), z 위. 단위 m. 길이 20, 폭 15.2. 원점 = 동체 가운데(기수 끝 y -10, 꼬리 y +10).
아래 숫자는 형상안 그림과 같은 '앞이 +' 좌표(F(x, 앞, z))로 적고 만들 때 y를 뒤집는다.
실행: blender --background --factory-startup --python Tools/aco_xfw_model.py
      ACO_XFW_PREVIEW=경로접두 → 위 · 옆 · 앞 · 비스듬히 미리보기(한 장으로 붙임), ACO_XFW_BLEND=경로 → .blend 저장"""
import math
import os
import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAME = "ACO_XFW"


def F(x, fwd, z):
    """형상안 좌표(앞 +) → 블렌더(뒤 +)."""
    return Vector((x, -fwd, z))


# ───────────── 재질 ─────────────

def material(name, color, rough=0.6, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.roughness = rough
    m.metallic = metal
    return m


MAT_BODY = 0
MAT_DARK = 1      # 흡입구 안 · 노즐 안
MAT_CANOPY = 2
MAT_NOZZLE = 3
MAT_WING = 4      # 주익(도장 구분용 — 내보낼 때는 몸체 재질로 합친다)
MAT_RAIL = 5      # 레일건 금속
MAT_GLOW = 6      # 레일 사이(파란 빛)
MATS = [
    ("XFW_Body", (0.42, 0.45, 0.48), 0.65, 0.0),
    ("XFW_Dark", (0.02, 0.02, 0.025), 0.9, 0.0),
    ("XFW_Canopy", (0.35, 0.26, 0.08), 0.15, 0.6),
    ("XFW_Nozzle", (0.16, 0.15, 0.14), 0.4, 0.8),
    ("XFW_Wing", (0.42, 0.45, 0.48), 0.65, 0.0),
    ("XFW_Rail", (0.2, 0.21, 0.23), 0.35, 0.8),
    ("XFW_Glow", (0.3, 0.85, 1.0), 0.3, 0.0),
]


# ───────────── 도우미 ─────────────

def loft(bm, rings, cap_front=None, cap_back=None):
    """rings: [[Vector,...], ...] 같은 점 수의 단면들(앞 → 뒤). 이웃 단면을 사각형으로 잇는다. cap_*: 막을 재질(None이면 열어 둔다)."""
    verts = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for a, b in zip(verts[:-1], verts[1:]):
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((a[i], a[j], b[j], b[i]))
            f.material_index = MAT_BODY
    caps = []
    if cap_front is not None:
        f = bm.faces.new(list(reversed(verts[0])))
        f.material_index = cap_front
        caps.append(f)
    if cap_back is not None:
        f = bm.faces.new(verts[-1])
        f.material_index = cap_back
        caps.append(f)
    return verts, caps


def body_section(fwd, w, zc, zt, zb):
    """마름모에 가까운 8각 단면(위 등선 · 어깨 · 차인 · 아래 어깨 · 바닥). w = 차인 반폭."""
    pts = [(0.0, zt), (0.45 * w, zc + 0.72 * (zt - zc)), (w, zc), (0.6 * w, zc + 0.62 * (zb - zc)),
           (0.0, zb), (-0.6 * w, zc + 0.62 * (zb - zc)), (-w, zc), (-0.45 * w, zc + 0.72 * (zt - zc))]
    return [F(x, fwd, z) for x, z in pts]


def nacelle_section(fwd, cx, hw, zt, zb, scale=1.0):
    """엔진 나셀 8각 단면. 바깥 벽은 아래로 조금 벌어지고 안쪽 벽은 곧다(동체 아래 홈 쪽)."""
    h = zt - zb
    cz = (zt + zb) * 0.5
    pts = [(cx - 0.55 * hw, zt), (cx + 0.55 * hw, zt), (cx + hw, zt - 0.28 * h), (cx + 1.05 * hw, zb + 0.3 * h),
           (cx + 0.7 * hw, zb), (cx - 0.7 * hw, zb), (cx - hw, zb + 0.3 * h), (cx - hw, zt - 0.28 * h)]
    out = []
    for x, z in pts:
        out.append(F(cx + (x - cx) * scale, fwd, cz + (z - cz) * scale))
    return out


def lerp(a, b, t):
    return a + (b - a) * t


def table(rows, fwd):
    """rows: [(앞, 값...), ...] 앞이 큰 쪽부터. fwd에서 선형 보간한 값들."""
    for (f0, *v0), (f1, *v1) in zip(rows[:-1], rows[1:]):
        if f1 <= fwd <= f0:
            t = (f0 - fwd) / (f0 - f1) if f0 != f1 else 0.0
            return [lerp(a, b, t) for a, b in zip(v0, v1)]
    return list(rows[0][1:]) if fwd > rows[0][0] else list(rows[-1][1:])


# ───────────── 부품 ─────────────

# 동체(한 겹 피부): 단면마다 한 바퀴 — 등 가운데 → 등 어깨 → 골 → 나셀 윗면(안 · 바깥) → 바깥 차인(날개가 붙는 곳) → 바깥 벽 →
# 나셀 바닥(바깥 · 안) → 나셀 안쪽 벽 위 → 터널 천장 모서리 → 터널 천장 가운데. 위는 이어지고 아래는 두 나셀 사이가 터널로 빈다(톰캣 · 수호이처럼).
# 기수 쪽 단면은 이 점들이 마름모 기수 위에 겹쳐 있다가 흡입구에서 아래로 벌어진다.
# 열: 앞, 등 높이 zt, 어깨 xs zs, 골 xv zv, 나셀 윗면 안 xni · 바깥 xno · 높이 zn, 차인 xc zc, 바깥 벽 xw zw, 바닥 바깥 xbo · 안 xbi · 높이 zbn, 안쪽 벽 위 xi zi, 터널 천장 xtr ztr
BODY = [
    (10.0, 0.02, 0.01, 0.01, 0.015, 0.01, 0.018, 0.019, 0.005, 0.02, 0.0, 0.015, -0.01, 0.01, 0.008, -0.015, 0.005, -0.017, 0.002, -0.02),
    (9.2, 0.28, 0.11, 0.22, 0.16, 0.17, 0.2, 0.23, 0.08, 0.25, 0.02, 0.18, -0.08, 0.13, 0.09, -0.14, 0.06, -0.16, 0.03, -0.18),
    (8.4, 0.48, 0.2, 0.37, 0.3, 0.28, 0.37, 0.42, 0.14, 0.45, -0.1, 0.32, -0.15, 0.22, 0.15, -0.25, 0.1, -0.29, 0.05, -0.32),
    (7.0, 0.82, 0.38, 0.63, 0.56, 0.47, 0.7, 0.8, 0.26, 0.9, -0.18, 0.6, -0.27, 0.43, 0.3, -0.46, 0.2, -0.52, 0.1, -0.55),
    (5.5, 1.0, 0.43, 0.78, 0.62, 0.58, 0.8, 0.9, 0.32, 1.0, -0.22, 0.68, -0.33, 0.5, 0.35, -0.56, 0.24, -0.62, 0.12, -0.66),
    (4.0, 1.02, 0.45, 0.79, 0.65, 0.6, 0.84, 0.95, 0.33, 1.1, -0.24, 0.72, -0.34, 0.53, 0.37, -0.6, 0.25, -0.66, 0.12, -0.7),
    (2.5, 0.98, 0.48, 0.79, 0.78, 0.6, 0.9, 1.0, 0.33, 1.35, -0.24, 0.85, -0.35, 0.62, 0.43, -0.62, 0.3, -0.66, 0.15, -0.7),   # 흡입구 앞: 차인은 스트레이크 안쪽(스트레이크가 옆 모서리)
    (0.9, 0.95, 0.49, 0.79, 0.85, 0.59, 1.0, 1.1, 0.33, 1.85, -0.15, 1.0, -0.35, 0.72, 0.5, -0.62, 0.36, -0.66, 0.18, -0.7),
    # 흡입구 덕트(DUCT)가 덮는 구간: 아래쪽은 덕트 바닥(입구 안쪽) 뒤까지 동체 크기로 접어 둔다 — 입구 안으로 동체 면이 보이지 않게
    (-0.3, 0.92, 0.5, 0.78, 0.98, 0.58, 1.25, 2.1, 0.4, 2.35, 0.1, 1.0, -0.35, 0.72, 0.5, -0.62, 0.36, -0.66, 0.18, -0.7),
    (-0.9, 0.9, 0.5, 0.8, 1.0, 0.6, 1.25, 2.2, 0.55, 2.4, 0.2, 1.0, -0.35, 0.72, 0.5, -0.62, 0.36, -0.66, 0.18, -0.7),
    (-1.7, 0.88, 0.5, 0.8, 0.95, 0.66, 1.2, 2.2, 0.82, 2.37, 0.4, 0.9, -0.35, 0.68, 0.48, -0.62, 0.36, -0.62, 0.18, -0.64),
    (-2.5, 0.87, 0.5, 0.79, 0.95, 0.69, 1.2, 2.2, 0.9, 2.33, 0.56, 2.3, -0.69, 2.16, 1.28, -0.95, 1.22, -0.48, 0.6, -0.53),
    (-4.0, 0.86, 0.5, 0.78, 0.95, 0.72, 1.2, 2.2, 1.04, 2.3, 0.7, 2.3, -0.6, 2.15, 1.25, -0.88, 1.15, -0.45, 0.6, -0.5),
    (-6.5, 0.84, 0.5, 0.76, 0.95, 0.72, 1.2, 2.18, 1.04, 2.28, 0.7, 2.28, -0.58, 2.12, 1.25, -0.82, 1.15, -0.43, 0.6, -0.48),
    (-8.0, 0.74, 0.45, 0.68, 0.92, 0.66, 1.25, 2.0, 0.9, 2.2, 0.62, 2.2, -0.45, 2.0, 1.3, -0.7, 1.18, -0.35, 0.6, -0.35),
    # 끝: 나셀은 노즐 크기의 8각 단면(노즐이 이어 붙는다), 두 노즐 사이는 낮고 납작한 꼬리
    (-9.3, 0.45, 0.4, 0.42, 0.9, 0.45, 1.42, 1.82, 0.62, 2.08, 0.38, 2.08, -0.08, 1.82, 1.42, -0.32, 1.16, -0.05, 0.6, 0.05),
]
BODY_STATIONS = [10.0, 9.6, 9.2, 8.8, 8.4, 7.7, 7.0, 6.2, 5.5, 4.7, 4.0, 3.2, 2.5, 1.7, 0.9, -0.3, -0.9, -1.7, -2.5, -4.0, -5.2, -6.5, -7.3, -8.0, -8.7, -9.3]
INTAKE_STATION = -0.3

INTAKE_RAKE = 0.8      # 흡입구 아래 입술이 위보다 앞으로 나온 길이(m)

# 흡입구 덕트(한쪽): 앞, 가운데 x, 반폭, 위, 아래 — 사각 입구(아래 입술이 앞으로)에서 몸체 나셀 속으로 들어간다(몸체보다 조금 크게 덮는다)
DUCT = [      # 윗면은 스트레이크(차인) 바로 밑에 붙는다 — 바깥 벽이 차인 끝과 같은 선
    (-0.3, 1.74, 0.62, 0.12, -1.3),      # 안쪽 벽 x 1.12 — 몸체 나셀 안쪽 벽(1.2)과 겹치지 않게(거의 같은 면이면 불리언이 두께 0 조각을 남긴다)
    (-1.0, 1.74, 0.62, 0.28, -1.24),
    (-1.8, 1.74, 0.62, 0.45, -1.12),
    (-2.6, 1.74, 0.62, 0.62, -1.0),
]


def duct_section(fwd, cx, hw, zt, zb):
    """흡입구 덕트 단면: 윗면은 평평(몸체 아래에 붙는 면), 바닥 모서리만 깎는다."""
    h = zt - zb
    pts = [(cx - 0.95 * hw, zt), (cx + 0.95 * hw, zt), (cx + hw, zt - 0.08 * h), (cx + 1.05 * hw, zb + 0.3 * h),
           (cx + 0.7 * hw, zb), (cx - 0.7 * hw, zb), (cx - hw, zb + 0.3 * h), (cx - hw, zt - 0.08 * h)]
    return [F(x, fwd, z) for x, z in pts]

# W 주익 평면(한쪽, 형상안 좌표): 뿌리 · 꺾임 · 끝의 앞전/뒷전
WING_ROOT = ((1.4, -0.6), (1.4, -4.4))      # 뿌리를 나셀 속까지 늘린다(앞전 · 뒷전)
WING_ROOT_VIS = ((2.3, -0.6), (2.3, -4.4))
WING_KINK = ((4.6, -3.2), (4.7, -7.0))
WING_TIP = ((7.8, -0.7), (7.8, -2.1))
WING_Z = 0.35
ANHEDRAL = math.radians(-2.0)

# 카나드(한쪽): 5각형 + 뿌리(동체 속으로)
# 뿌리는 스트레이크 바깥 가장자리를 따른다(같은 높이 · 같은 면 — 스트레이크에서 이어 나온다, 겹치지 않는다)
CANARD = [(1.0, 4.5), (3.8, 1.5), (3.8, 0.9), (2.8, 0.25), (2.05, 0.25), (1.6, 1.7), (1.28, 2.6)]
CANARD_Z = -0.22
CANARD_DIHEDRAL = math.radians(5.0)

# 스트레이크(한쪽): 기수 아래쪽 옆(앞 8.0)에서 흡입구 위 · 날개 뿌리(2.35, -0.6)까지. (x, 앞, z) — 낮게 시작해 뒤로 갈수록 차인 높이로 오른다
# 카나드 자리에서는 좁게(카나드 뿌리가 이 가장자리), 카나드 뒤에서 날개 뿌리로 넓어지며 오른다
STRAKE = [(0.42, 8.0, -0.3), (0.95, 5.0, -0.27), (1.15, 2.6, -0.25), (1.45, 0.6, -0.24), (1.75, 0.0, -0.2), (2.35, -0.6, 0.22),
          (1.6, -0.6, 0.22), (0.7, 0.6, -0.24), (0.6, 2.6, -0.25), (0.5, 5.0, -0.27), (0.3, 8.0, -0.3)]

# 러더베이터(한쪽): 뿌리 앞 · 뿌리 뒤(나셀 위), 바깥 50°, 펼친 길이
RV_ROOT_LE = (1.85, -5.2)
RV_ROOT_TE = (1.9, -9.0)
RV_TIP_LE_F = -7.7
RV_TIP_TE_F = -9.0
RV_CANT = math.radians(50.0)
RV_SPAN = 3.13
RV_ROOT_Z = 0.85


def body_ring(fwd, rake=0.0):
    """한 단면(오른쪽 반 10점 + 위 · 아래 가운데, 왼쪽은 거울). rake: 흡입구 입술 — 아래쪽 점을 앞으로 민다."""
    (zt, xs, zs, xv, zv, xni, xno, zn, xc, zc, xw, zw, xbo, xbi, zbn, xi, zi, xtr, ztr) = table(BODY, fwd)
    half = [(xs, zs, 0.0), (xv, zv, 0.0), (xni, zn, 0.0), (xno, zn, 0.0), (xc, zc, 0.0),
            (xw, zw, 0.45), (xbo, zbn, 1.0), (xbi, zbn, 1.0), (xi, zi, 0.6), (xtr, ztr, 0.3)]
    ring = [F(0.0, fwd, zt)]
    ring += [F(x, fwd + rake * r, z) for x, z, r in half]
    ring.append(F(0.0, fwd + rake * 0.3, ztr))
    ring += [F(-x, fwd + rake * r, z) for x, z, r in reversed(half)]
    return ring


def build_body(bm):
    rings = [body_ring(f) for f in BODY_STATIONS]
    rings[0] = [F(0.0, BODY_STATIONS[0], 0.0) for _ in rings[0]]     # 기수 끝은 한 점(퇴화 면 없이 — 불리언이 깨지지 않게)
    verts, _ = loft(bm, rings)
    # 꼬리 끝 마개: 터널 홈 때문에 오목한 둘레 — 제약 들로네 삼각분할(귀 자르기는 겹친 삼각형을 만들어 불리언이 구멍을 낸다)
    tail = verts[-1]
    n = len(tail)
    ring_edges = [bm.edges.get((tail[i], tail[(i + 1) % n])) for i in range(n)]
    filled = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=ring_edges, normal=Vector((0.0, 1.0, 0.0)))
    for f in filled["geom"]:
        if isinstance(f, bmesh.types.BMFace):
            f.material_index = MAT_NOZZLE
    bmesh.ops.remove_doubles(bm, verts=verts[0], dist=0.001)
    verts[0] = [v for v in verts[0] if v.is_valid]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # 흡입구: 입술 바로 앞 구간에서 앞을 보는 면 = 입구(어둡게)


def build_nozzles(bm):
    """노즐 두 개: 나셀 끝(반폭 약 0.46) 안쪽에서 시작하는 12각 통 — 0.65m 뒤로 나오며 반지름 0.43 → 0.39, 끝 테두리 안쪽으로 짙은 원뿔."""
    cz = 0.15
    n = 12
    for side in (1, -1):
        cx = 1.62 * side
        rings = []
        for fwd, r in ((-9.0, 0.43), (-9.6, 0.42), (-9.95, 0.39)):     # 나셀 끝 단면(반폭 약 0.46) 안쪽 — 나셀 속에서 시작해 이음매를 숨긴다
            rings.append([F(cx + math.cos(math.tau * i / n) * r, fwd, cz + math.sin(math.tau * i / n) * r * 0.92) for i in range(n)])
        lip = [F(cx + math.cos(math.tau * i / n) * 0.33, -9.95, cz + math.sin(math.tau * i / n) * 0.33 * 0.92) for i in range(n)]
        cone = F(cx, -9.45, cz)
        vs = [[bm.verts.new(p) for p in r] for r in rings]
        vl = [bm.verts.new(p) for p in lip]
        vc = bm.verts.new(cone)
        for a, b in zip(vs[:-1], vs[1:]):
            for i in range(n):
                j = (i + 1) % n
                bm.faces.new((a[i], b[i], b[j], a[j])).material_index = MAT_NOZZLE
        last = vs[-1]
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((last[i], vl[i], vl[j], last[j])).material_index = MAT_NOZZLE
            bm.faces.new((vl[i], vc, vl[j])).material_index = MAT_DARK
        bm.faces.new(list(reversed(vs[0]))).material_index = MAT_NOZZLE

def build_duct(bm, side):
    stations = [-0.3, -0.65, -1.0, -1.4, -1.8, -2.2, -2.6]
    rings = []
    for fwd in stations:
        cx, hw, zt, zb = table(DUCT, fwd)
        r = duct_section(fwd, cx, hw, zt, zb)
        rings.append([Vector((p.x * side, p.y, p.z)) for p in r])
    for k, amount in ((0, INTAKE_RAKE), (1, INTAKE_RAKE * 0.5)):
        ring = rings[k]
        zt = max(p.z for p in ring)
        zb = min(p.z for p in ring)
        for p in ring:
            p.y -= amount * (zt - p.z) / (zt - zb)
    if side < 0:
        rings = [list(reversed(r)) for r in rings]
    verts, _ = loft(bm, rings, cap_back=MAT_BODY)
    # 입구: 테두리(입술 두께) → 안으로 들어간 검은 덕트
    inlet = verts[0]
    c = sum((v.co for v in inlet), Vector()) / len(inlet)
    lip = [bm.verts.new(c + (v.co - c) * 0.84) for v in inlet]
    deep = [bm.verts.new(c + (v.co - c) * 0.8 + Vector((0, 1.3, 0))) for v in inlet]
    n = len(inlet)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((inlet[j], inlet[i], lip[i], lip[j])).material_index = MAT_BODY
        bm.faces.new((lip[j], lip[i], deep[i], deep[j])).material_index = MAT_DARK
    bm.faces.new(deep).material_index = MAT_DARK


# 레일건(기체 아래 가운데, 나셀 사이 터널): 앞, 반폭, 위 높이, 아래 높이 — 위는 터널 천장 속에 묻힌다
RAIL_BODY = [(2.6, 0.3, -0.92, -1.36), (2.2, 0.32, -0.9, -1.38), (-4.8, 0.32, -0.88, -1.38), (-6.0, 0.22, -0.88, -1.12)]   # 터널 천장(약 -0.55) 아래로 띄운다
RAIL_COILS = (1.7, 0.1, -1.5)          # 코일 띠(조금 굵게)
RAIL_TIP = 4.7                          # 레일 끝(앞)
RAIL_UP = (-1.0, -1.12)                 # 위 레일(위, 아래 높이)
RAIL_LO = (-1.26, -1.38)                # 아래 레일
RAIL_PYLONS = ((2.0, 1.1), (-2.6, -3.5))   # 하우징을 터널 천장에 매다는 파일런(앞, 뒤)
RAIL_HW = 0.13


def box(bm, x0, x1, f0, f1, z0, z1, mat, side_mats=None):
    """닫힌 상자(형상안 좌표: x, 앞, z). side_mats: {'top','bot','in'} 면별 재질(없으면 mat)."""
    side_mats = side_mats or {}
    c = [F(x, f, z) for f in (f0, f1) for z in (z0, z1) for x in (x0, x1)]
    v = [bm.verts.new(p) for p in c]
    # 점 번호: f0: (z0: x0 0, x1 1), (z1: x0 2, x1 3) / f1: 4 5 6 7
    faces = {
        'front': (4, 5, 7, 6), 'back': (0, 2, 3, 1), 'bot': (0, 1, 5, 4), 'top': (2, 6, 7, 3),
        'left': (0, 4, 6, 2), 'right': (1, 3, 7, 5)}
    for k, idx in faces.items():
        bm.faces.new([v[i] for i in idx]).material_index = side_mats.get(k, mat)


def build_railgun(bm):
    stations = [2.6, 2.2, 1.9, 1.5, 0.3, -0.1, -1.3, -1.7, -4.8, -6.0]
    rings = []
    for fwd in stations:
        hw, zt, zb = table(RAIL_BODY, fwd)
        grow = 1.12 if any(abs(fwd - c) < 0.25 for c in RAIL_COILS) else 1.0   # 코일 띠
        cz = (zt + zb) * 0.5
        h = (zt - zb) * 0.5 * grow
        w = hw * grow
        pts = [(-0.7 * w, cz + h), (0.7 * w, cz + h), (w, cz + 0.6 * h), (w, cz - 0.6 * h),
               (0.7 * w, cz - h), (-0.7 * w, cz - h), (-w, cz - 0.6 * h), (-w, cz + 0.6 * h)]
        rings.append([F(x, fwd, z) for x, z in pts])
    verts, caps = loft(bm, rings, cap_front=MAT_RAIL, cap_back=MAT_RAIL)
    for f in bm.faces:
        f.material_index = MAT_RAIL
    # 위 · 아래 레일(사이가 빈다 — 마주 보는 면은 파란 빛)
    box(bm, -RAIL_HW, RAIL_HW, 2.5, RAIL_TIP, RAIL_UP[1], RAIL_UP[0], MAT_RAIL, {'bot': MAT_GLOW})
    box(bm, -RAIL_HW, RAIL_HW, 2.5, RAIL_TIP, RAIL_LO[1], RAIL_LO[0], MAT_RAIL, {'top': MAT_GLOW})
    # 파일런(하우징 위 → 터널 천장 속) · 레일 앞 받침(위 레일 → 기수 바닥 속)
    for f0, f1 in RAIL_PYLONS:
        box(bm, -0.07, 0.07, f1, f0, -0.95, -0.4, MAT_RAIL)
    box(bm, -0.05, 0.05, 3.42, 3.6, -1.02, -0.55, MAT_RAIL)
    # 레일 사이 안쪽 벽(뒤) — 빛나는 면
    box(bm, -RAIL_HW * 0.8, RAIL_HW * 0.8, 2.5, 2.62, RAIL_LO[0], RAIL_UP[1], MAT_GLOW)
    # 포구 브래킷(레일 두 개를 옆에서 잡는 틀)
    for sx in (-1, 1):
        x0, x1 = (RAIL_HW, RAIL_HW + 0.05) if sx > 0 else (-RAIL_HW - 0.05, -RAIL_HW)
        box(bm, x0, x1, RAIL_TIP - 0.35, RAIL_TIP - 0.1, RAIL_LO[1] - 0.02, RAIL_UP[0] + 0.02, MAT_RAIL)
        box(bm, x0, x1, 3.35, 3.55, RAIL_LO[1] - 0.02, RAIL_UP[0] + 0.02, MAT_RAIL)


def plate(bm, outline_top, outline_bot):
    """위 · 아래 윤곽(같은 점 수, 둘레 순서)으로 닫힌 판."""
    top = [bm.verts.new(p) for p in outline_top]
    bot = [bm.verts.new(p) for p in outline_bot]
    n = len(top)
    bm.faces.new(top).material_index = MAT_BODY
    bm.faces.new(list(reversed(bot))).material_index = MAT_BODY
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((top[j], top[i], bot[i], bot[j])).material_index = MAT_BODY


def build_wing(bm, side):
    """다이아몬드 날개 단면(앞전 · 가운데 능선 · 뒷전). 뿌리 두께 0.3, 꺾임 0.18, 끝 0.06. 하반각 -2°."""
    sections = []
    for (le, te), thick in ((WING_ROOT, 0.34), (WING_KINK, 0.18), (WING_TIP, 0.06)):
        x = le[0]
        dz = (x - WING_ROOT_VIS[0][0]) * math.tan(ANHEDRAL) if x > WING_ROOT_VIS[0][0] else 0.0
        z = WING_Z + dz
        ridge = (lerp(le[1], te[1], 0.45))
        xr = lerp(le[0], te[0], 0.45)
        sections.append((F(le[0], le[1], z), F(xr, ridge, z + thick * 0.5), F(te[0], te[1], z), F(xr, ridge, z - thick * 0.5)))
    vs = [[bm.verts.new(Vector((p.x * side, p.y, p.z))) for p in s] for s in sections]
    for a, b in zip(vs[:-1], vs[1:]):
        for i in range(4):
            j = (i + 1) % 4
            quad = (a[i], a[j], b[j], b[i]) if side > 0 else (b[i], b[j], a[j], a[i])
            bm.faces.new(quad).material_index = MAT_WING
    root = vs[0] if side > 0 else list(reversed(vs[0]))
    tip = vs[-1]
    bm.faces.new(list(reversed(root)) if side > 0 else root).material_index = MAT_WING
    bm.faces.new(tip if side > 0 else list(reversed(tip))).material_index = MAT_WING


def build_canard(bm, side):
    top, bot = [], []
    for x, fwd in CANARD:
        z = CANARD_Z + max(0.0, x - 2.0) * math.tan(CANARD_DIHEDRAL)
        t = 0.09 if x < 2.0 else 0.05
        top.append(F(x * side, fwd, z + t * 0.5))
        bot.append(F(x * side, fwd, z - t * 0.5))
    if side < 0:
        top.reverse()
        bot.reverse()
    plate(bm, top, bot)


def build_strake(bm, side):
    top, bot = [], []
    for x, fwd, z in STRAKE:
        t = 0.18
        top.append(F(x * side, fwd, z + t * 0.5))
        bot.append(F(x * side, fwd, z - t * 0.5))
    if side < 0:
        top.reverse()
        bot.reverse()
    plate(bm, top, bot)


def build_ruddervator(bm, side):
    """나셀 위 뿌리에서 바깥으로 50° 기운 판. 펼친 길이 RV_SPAN, 두께 0.12 → 0.05."""
    out = Vector((math.sin(RV_CANT), 0.0, math.cos(RV_CANT)))
    root_le = F(RV_ROOT_LE[0], RV_ROOT_LE[1], RV_ROOT_Z)
    root_te = F(RV_ROOT_TE[0], RV_ROOT_TE[1], RV_ROOT_Z)
    tip_le = F(RV_ROOT_LE[0], RV_TIP_LE_F, RV_ROOT_Z) + out * RV_SPAN
    tip_te = F(RV_ROOT_LE[0], RV_TIP_TE_F, RV_ROOT_Z) + out * RV_SPAN
    normal = Vector((math.cos(RV_CANT), 0.0, -math.sin(RV_CANT)))
    outline = [root_le, tip_le, tip_te, root_te]
    # 뿌리는 나셀 속으로 조금 내린다
    outline[0] = outline[0] - out * 0.35
    outline[3] = outline[3] - out * 0.35
    th = [0.12, 0.05, 0.05, 0.12]
    top = [p + normal * (t * 0.5) for p, t in zip(outline, th)]
    bot = [p - normal * (t * 0.5) for p, t in zip(outline, th)]
    if side < 0:
        top = [Vector((-p.x, p.y, p.z)) for p in reversed(top)]
        bot = [Vector((-p.x, p.y, p.z)) for p in reversed(bot)]
    plate(bm, top, bot)


CANOPY = [   # 앞, 반폭, 등 위로 솟은 높이
    (7.25, 0.04, 0.02), (6.9, 0.22, 0.2), (6.4, 0.36, 0.38), (5.8, 0.43, 0.5), (5.0, 0.45, 0.52),
    (4.3, 0.41, 0.44), (3.7, 0.3, 0.3), (3.15, 0.14, 0.12), (2.7, 0.04, 0.02)]


def build_canopy():
    """캐노피: 단면(아치 9점 + 바닥)을 이어 만든다. 바닥은 동체 등 속(0.15 아래)에 묻힌다."""
    me = bpy.data.meshes.new(NAME + "_Canopy")
    c = bpy.data.objects.new(NAME + "_Canopy", me)
    bpy.context.collection.objects.link(c)
    bm = bmesh.new()
    rings = []
    for fwd, w, rise in CANOPY:
        zt = table(BODY, fwd)[0]
        base = zt - 0.15
        top = zt + rise
        ring = []
        for k in range(9):
            t = math.pi * k / 8
            ring.append(F(w * math.cos(t), fwd, base + (top - base) * (math.sin(t) ** 0.75)))
        rings.append(ring)
    loft(bm, rings, cap_front=MAT_CANOPY, cap_back=MAT_CANOPY)
    for f in bm.faces:
        f.material_index = MAT_CANOPY
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    bpy.context.view_layer.objects.active = c
    return c

def build():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    mats = [material(*m) for m in MATS]

    parts = []

    def new_obj(label, fn):
        me = bpy.data.meshes.new(label)
        ob = bpy.data.objects.new(label, me)
        bpy.context.collection.objects.link(ob)
        for m in mats:
            me.materials.append(m)
        bm = bmesh.new()
        fn(bm)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        parts.append(ob)
        return ob

    body = new_obj("Body", build_body)
    merge = []
    for s in (1, -1):
        new_obj("Wing" + ("R" if s > 0 else "L"), lambda bm, s=s: build_wing(bm, s))
        new_obj("Canard" + ("R" if s > 0 else "L"), lambda bm, s=s: build_canard(bm, s))
        new_obj("Rudder" + ("R" if s > 0 else "L"), lambda bm, s=s: build_ruddervator(bm, s))
    new_obj("Nozzles", build_nozzles)
    new_obj("Railgun", build_railgun)
    # 흡입구 덕트 두 개를 한 오브젝트로 만들어 몸체와 한 번에 합집합(겹친 틈 · 깨진 이음새 없이 한 덩어리)
    merge.append(new_obj("Ducts", lambda bm: (build_duct(bm, 1), build_duct(bm, -1))))
    bpy.context.view_layer.objects.active = body
    for m in merge:
        mod = body.modifiers.new("U_" + m.name, 'BOOLEAN')
        mod.operation = 'UNION'
        mod.solver = 'EXACT'
        mod.use_hole_tolerant = True
        mod.object = m
        bpy.ops.object.modifier_apply(modifier=mod.name)
        parts.remove(m)
        bpy.data.objects.remove(m, do_unlink=True)
    # 불리언 뒤 정리: 3mm 안의 점을 합치고 두께 0 조각(퇴화 면)을 없앤다 — 삼각분할하면 비다양체 선이 되는 얇은 지느러미
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.003)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=0.003)
    tri = bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    bad = [e for e in bm.edges if len(e.link_faces) > 2]
    if bad:
        # 같은 점들을 공유하는 맞붙은 면 쌍(지느러미)을 지운다
        fins = {f for e in bad for f in e.link_faces if f.calc_area() < 0.02}
        bmesh.ops.delete(bm, geom=list(fins), context='FACES')
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    canopy = build_canopy()
    canopy.data.materials.clear()
    for m in mats:
        canopy.data.materials.append(m)
    for p in canopy.data.polygons:
        p.material_index = MAT_CANOPY
    parts.append(canopy)

    # 한 오브젝트로(로우폴리 NPC — 부품은 겹쳐 둔다)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.active_object
    ob.name = NAME
    ob.data.name = NAME
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(ob.data)
    bm.free()
    for poly in ob.data.polygons:
        poly.use_smooth = False
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[XFW] {NAME}: 면 {len(ob.data.polygons)}, 삼각형 {tris}, 크기 {tuple(round(d, 2) for d in ob.dimensions)}")
    return ob


# ───────────── 미리보기 ─────────────

def preview(prefix):
    import numpy as np
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'MATERIAL'
    sh.show_cavity = True
    sh.cavity_type = 'BOTH'
    sh.show_object_outline = True
    sh.object_outline_color = (0.05, 0.05, 0.05)
    scene.render.film_transparent = False
    scene.world = scene.world or bpy.data.worlds.new("W")
    sh.background_type = 'VIEWPORT'
    sh.background_color = (0.08, 0.1, 0.12)
    scene.render.resolution_percentage = 100
    W = 900
    views = [
        ("top", (0, 0, 40), (0, 0, math.radians(180)), True, 30.0),
        ("side", (40, 0, 1.0), (math.radians(90), 0, math.radians(90)), True, 21.0),
        ("front", (0, -40, 1.0), (math.radians(90), 0, 0), True, 17.0),
        ("persp", (-17, -19, 11), None, False, 0),
    ]
    tiles = []
    for name, loc, rot, ortho, size in views:
        cam_data = bpy.data.cameras.new(name)
        cam = bpy.data.objects.new(name, cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        if rot is not None:
            cam.rotation_euler = rot
        else:
            d = Vector((0, 0.5, 0.6)) - Vector(loc)
            cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
            cam_data.lens = 50
        if ortho:
            cam_data.type = 'ORTHO'
            cam_data.ortho_scale = size
        cam_data.clip_end = 200
        scene.camera = cam
        h = int(W * 0.7)
        scene.render.resolution_x = W
        scene.render.resolution_y = h
        path = f"{prefix}_{name}.png"
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
        tiles.append(px)
    # 붙이기: 왼쪽 위(top) | 오른쪽(side · front · persp 세로)
    # 2x2: 위 줄 = 위에서 | 비스듬히, 아래 줄 = 옆 | 앞(블렌더 픽셀은 아래 줄이 0)
    top_row = np.concatenate([tiles[0], tiles[3]], axis=1)
    bottom_row = np.concatenate([tiles[1], tiles[2]], axis=1)
    sheet = np.concatenate([bottom_row, top_row], axis=0)
    out = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0])
    out.pixels = sheet.ravel()
    out.filepath_raw = f"{prefix}_sheet.png"
    out.file_format = 'PNG'
    out.save()
    print(f"[XFW] 미리보기 {prefix}_sheet.png")


# ───────────── 셰이딩 · UV · 도장 · 내보내기 ─────────────

import numpy as np

OUT = os.environ.get("ACO_XFW_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "XFW"))
TEX = 4096
SMOOTH_ANGLE = 32.0       # 이보다 완만하게 꺾인 면 사이는 부드럽게(동체 곡면), 날개 · 미익 앞전 같은 얇은 판 모서리는 날카롭게
WINGTIP_X = 6.4           # 이 바깥(|x|)의 주익이 주황 날개 끝

GRAY_TOP = np.array((0.40, 0.43, 0.46))     # 윗면(조금 짙게)
GRAY_LOW = np.array((0.60, 0.63, 0.65))     # 아랫면(밝게)
LINE = np.array((0.31, 0.33, 0.35))         # 패널 라인
ORANGE = np.array((0.95, 0.43, 0.07))
DARK = np.array((0.035, 0.035, 0.04))       # 흡입구 안
NOZ = np.array((0.30, 0.29, 0.28))
NOZ_IN = np.array((0.10, 0.09, 0.09))
GLASS = np.array((0.30, 0.23, 0.09))
FRAME = np.array((0.24, 0.26, 0.28))

T_BODY, T_DARK, T_CANOPY, T_NOZ, T_WING, T_RAIL, T_GLOW = MAT_BODY, MAT_DARK, MAT_CANOPY, MAT_NOZZLE, MAT_WING, MAT_RAIL, MAT_GLOW
HARD = (T_DARK, T_CANOPY, T_NOZ, T_RAIL, T_GLOW)
RAIL = np.array((0.2, 0.21, 0.23))
GLOW = np.array((0.35, 0.85, 1.0))


def shade(ob):
    """면은 모두 부드럽게 표시하되, SMOOTH_ANGLE보다 꺾인 모서리와 재질 경계(캐노피 · 흡입구 · 노즐)는 날카롭게. 그 뒤 가중 노멀(큰 면 기준)."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    lim = math.radians(SMOOTH_ANGLE)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) != 2:
            e.smooth = False
            continue
        a, b = e.link_faces
        hard = a.material_index != b.material_index and (a.material_index in HARD or b.material_index in HARD)
        e.smooth = (a.normal.angle(b.normal, 0.0) < lim) and not hard
    bm.to_mesh(me)
    bm.free()
    wn = ob.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL')
    wn.mode = 'FACE_AREA'
    wn.weight = 50
    wn.keep_sharp = True
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=wn.name)


def unwrap(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    me = ob.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(55.0), island_margin=0.006, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.006, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def hash3(p):
    h = np.sin(p[..., 0] * 127.1 + p[..., 1] * 311.7 + p[..., 2] * 74.7) * 43758.5453
    return h - np.floor(h)


def vnoise(P, scale):
    q = P * scale
    i = np.floor(q)
    f = q - i
    f = f * f * (3 - 2 * f)
    acc = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                acc = acc + w * hash3(i + np.array((dx, dy, dz)))
    return acc


def near(v, stations, w):
    d = np.full(v.shape, 1e9)
    for st in stations:
        d = np.minimum(d, np.abs(v - st))
    return d < w


WHITE = np.array((0.86, 0.87, 0.86))
RED = np.array((0.72, 0.1, 0.08))
YELLOW = np.array((0.92, 0.72, 0.1))
CHARCOAL = np.array((0.17, 0.18, 0.19))
LE_DARK = np.array((0.27, 0.29, 0.31))

BODY_STATIONS_PAINT = (8.6, 7.2, 5.9, 4.6, 3.6, 2.6, 1.6, 0.6, -0.9, -1.9, -2.9, -4.0, -5.0, -6.1, -7.2, -8.3)
ACCESS_PANELS = (   # 앞 f0, f1, 옆 x0, x1 — 윗면 점검창(모서리 나사)
    (-1.6, -0.6, 0.15, 0.45), (-4.6, -3.6, 1.35, 1.9), (2.3, 3.1, 0.2, 0.55), (-6.9, -6.0, 0.15, 0.45),
    (5.2, 5.7, 0.25, 0.55), (0.9, 1.4, 1.2, 1.6), (-2.6, -2.1, 1.4, 1.95), (-5.6, -5.0, 1.45, 2.0))
WING_TE = (((2.3, -4.4), (4.7, -7.0)), ((4.7, -7.0), (7.8, -2.1)))   # 주익 뒷전(|x|, 앞)
INSIGNIA = (3.9, -4.3, 0.6)            # 날개 윗면 휘장(|x|, 앞, 반지름)
TAIL_NUMBER = "07"
SEG7 = {'a': ((0, 2), (1, 2)), 'b': ((1, 2), (1, 1)), 'c': ((1, 1), (1, 0)), 'd': ((0, 0), (1, 0)),
        'e': ((0, 0), (0, 1)), 'f': ((0, 1), (0, 2)), 'g': ((0, 1), (1, 1))}
DIGITS = {'0': 'abcdef', '1': 'bc', '2': 'abged', '3': 'abgcd', '4': 'fgbc', '5': 'afgcd', '6': 'afgedc', '7': 'abc', '8': 'abcdefg', '9': 'abcfgd'}


def segdist2(px, py, segs):
    """2D 점들에서 선분들까지 가장 가까운 거리."""
    d = np.full(px.shape, 1e9)
    for (ax, ay), (bx, by) in segs:
        dx, dy = bx - ax, by - ay
        L = max(dx * dx + dy * dy, 1e-12)
        t = np.clip(((px - ax) * dx + (py - ay) * dy) / L, 0.0, 1.0)
        d = np.minimum(d, np.hypot(px - (ax + t * dx), py - (ay + t * dy)))
    return d


def panel_lines(c, coord, stations, mask, along):
    """패널 라인: 가운데 짙은 홈 + 한쪽 옅은 그늘 · 다른 쪽 옅은 빛(입체감), 홈 옆 리벳 줄(along 방향 0.11m 간격)."""
    d = np.full(coord.shape, 1e9)
    sgn = np.zeros(coord.shape)
    for st in stations:
        dd = coord - st
        closer = np.abs(dd) < d
        d = np.where(closer, np.abs(dd), d)
        sgn = np.where(closer, np.sign(dd), sgn)
    core = mask & (d < 0.011)
    bevel = mask & (d >= 0.011) & (d < 0.032)
    c[bevel] += (0.028 * sgn[bevel])[:, None]
    rivet = mask & (np.abs(d - 0.05) < 0.0075) & ((np.abs(along) % 0.11) < 0.015)
    c[rivet] *= 0.8
    c[core] = LINE
    return c


def color(P, n, tag):
    """P: 블렌더 좌표 점들(y 뒤 +), n: 면 노멀. 형상안 좌표 앞 = -y."""
    k = len(P)
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    fwd = -y
    sx = np.abs(x)
    side = np.where(x >= 0, 1.0, -1.0)
    if tag == T_DARK:
        return np.tile(DARK, (k, 1)) * (0.8 + 0.4 * vnoise(P, 3.0))[:, None]
    if tag == T_GLOW:
        return np.tile(GLOW, (k, 1))
    if tag == T_RAIL:
        c = np.tile(RAIL, (k, 1)) * (0.88 + 0.24 * vnoise(P, 4.0))[:, None]
        c = panel_lines(c, fwd, (2.4, 0.8, -0.6, -2.2, -3.8), np.ones(k, bool), sx + z)
        c[near(fwd, RAIL_COILS, 0.22)] = RAIL * 1.45                      # 코일 띠는 밝은 금속
        c[near(fwd, RAIL_COILS, 0.245) & ~near(fwd, RAIL_COILS, 0.2)] = GLOW * 0.8
        muzzle = fwd > RAIL_TIP - 0.45                                       # 포구 경고 띠(노랑 · 검정 빗금)
        stripe = ((fwd + z * 0.8 + sx * 0.8) / 0.09) % 2.0 < 1.0
        c[muzzle & stripe] = YELLOW
        c[muzzle & ~stripe] = CHARCOAL * 0.6
        return c
    if tag == T_CANOPY:
        c = np.tile(GLASS, (k, 1))
        refl = np.clip(n[:, 2], 0.0, 1.0)[:, None]                          # 위를 볼수록 하늘이 비친다
        c = c * (1 - 0.45 * refl) + np.array((0.55, 0.52, 0.42)) * 0.45 * refl
        c = c + ((vnoise(P * np.array((1.0, 0.25, 1.0)), 6.0) - 0.5) * 0.06)[:, None]
        c[near(fwd, (6.4, 4.85), 0.045)] = FRAME
        return c
    if tag == T_NOZ:
        inner = fwd < -9.4
        c = np.tile(NOZ, (k, 1)) * (0.82 + 0.36 * vnoise(P, 5.0))[:, None]
        heat = np.clip((-9.0 - fwd) / 0.95, 0.0, 1.0)[:, None]                # 끝으로 갈수록 그을린 청동빛
        c = c * (1 - 0.5 * heat) + np.array((0.22, 0.17, 0.12)) * 0.5 * heat
        c[inner] = NOZ_IN
        ang = np.arctan2(z - 0.15, x - np.where(x > 0, 1.62, -1.62))
        petal = (np.abs(((ang / (2 * math.pi) * 12) % 1.0) - 0.5) > 0.45) & ~inner
        c[petal] *= 0.55
        return c

    # ── 바탕: 위는 짙게, 아래는 밝게 + 얼룩 · 판마다 다른 톤 · 기류 방향 때 줄무늬
    t = np.clip((n[:, 2] + 0.25) / 0.7, 0.0, 1.0)[:, None]
    c = GRAY_LOW * (1 - t) + GRAY_TOP * t
    c = c + ((vnoise(P, 0.5) - 0.5) * 0.035 + (vnoise(P, 2.5) - 0.5) * 0.014 + (vnoise(P, 9.0) - 0.5) * 0.008)[:, None]
    cell = np.stack((np.floor(fwd / 1.1), np.floor(sx / 0.9), np.floor(z / 0.7)), 1)
    c = c + ((hash3(cell) - 0.5) * 0.024)[:, None]
    streak = vnoise(P * np.array((3.0, 0.22, 3.0)), 1.0)                     # 앞뒤로 길게 늘어진 때
    c = c - (np.clip(streak - 0.45, 0, 1) * 0.07)[:, None]
    low = n[:, 2] < -0.3
    c[low] -= ((vnoise(P[low], 1.2) * 0.04)[:, None])

    is_rudder = ((z > 1.12) | ((fwd < -5.0) & (sx > 2.35) & (z > 0.75))) & (tag == T_BODY)
    is_canard = (tag == T_BODY) & (fwd > 0.1) & (fwd < 4.7) & (sx > 1.45) & (np.abs(z + 0.2) < 0.3) & (np.abs(n[:, 2]) > 0.8)
    plate_le = (n[:, 1] < -0.35) & ((tag == T_WING) | is_rudder | is_canard)
    c[plate_le] = LE_DARK * (0.9 + 0.2 * vnoise(P[plate_le], 3.0))[:, None]

    if tag == T_BODY:
        body = ~is_rudder & ~is_canard
        top = n[:, 2] > 0.45
        sidef = np.abs(n[:, 0]) > 0.55
        c = panel_lines(c, fwd, BODY_STATIONS_PAINT, body & (fwd < 8.9), sx + z)
        c = panel_lines(c, sx, (0.42, 0.95, 1.55, 2.0), body & top & (fwd < 3.0) & (fwd > -8.9), fwd)
        c = panel_lines(c, z, (0.0, -0.5), body & sidef & (fwd < 0.5), fwd)
        # 점검창 + 모서리 나사
        for (f0, f1, x0, x1) in ACCESS_PANELS:
            inside = body & (fwd > f0) & (fwd < f1) & (sx > x0) & (sx < x1) & (n[:, 2] > 0.3)
            edge = inside & ((np.abs(fwd - f0) < 0.011) | (np.abs(fwd - f1) < 0.011) | (np.abs(sx - x0) < 0.011) | (np.abs(sx - x1) < 0.011))
            c[edge] = LINE
            for fc in (f0 + 0.06, f1 - 0.06):
                for xc in (x0 + 0.06, x1 - 0.06):
                    screw = inside & (np.hypot(fwd - fc, sx - xc) < 0.017)
                    c[screw] = c[screw] * 0.75
        # 흡입구 경고: 입구 바로 뒤 바깥 옆면의 빨강 · 흰 빗금 띠
        lip = -0.3 + INTAKE_RAKE * np.clip((0.12 - z) / 1.42, 0.0, 1.0)
        warn = body & (sx > 2.15) & (side * n[:, 0] > 0.5) & (fwd < lip) & (fwd > lip - 0.32) & (z < 0.1) & (z > -1.32)
        stripe = ((fwd - z) / 0.1) % 2.0 < 1.0
        c[warn & stripe] = RED
        c[warn & ~stripe] = WHITE
        # 꼬리 그을음(나셀 끝 · 납작한 꼬리)
        soot = np.clip((-8.0 - fwd) / 1.3, 0.0, 1.0) * (0.55 + 0.45 * vnoise(P, 2.0))
        c = c * (1 - 0.6 * soot)[:, None]
    if is_rudder.any():
        c = panel_lines(c, fwd, (-6.4, -7.9), is_rudder, z)
        # 바깥 면에 부대 번호(7세그먼트 글자) — 밖에서 왼쪽 → 오른쪽으로 읽힌다
        out = is_rudder & (side * n[:, 0] > 0.4)
        if out.any():
            ox, oz = math.sin(RV_CANT), math.cos(RV_CANT)
            v = (sx[out] - RV_ROOT_LE[0]) * ox + (z[out] - RV_ROOT_Z) * oz        # 뿌리에서 판을 따라 위로(m)
            u = (y[out] - 7.75) * side[out]                                        # 밖에서 본 오른쪽 +
            unit = 0.32
            total = len(TAIL_NUMBER) * 1.0 + (len(TAIL_NUMBER) - 1) * 0.55
            tx = u / unit + total * 0.5
            ty = (v - 1.65) / unit + 1.0
            segs = []
            for ci, ch in enumerate(TAIL_NUMBER):
                ox0 = ci * 1.55
                for sname in DIGITS.get(ch, ''):
                    (a0, a1), (b0, b1) = SEG7[sname]
                    segs.append(((a0 + ox0, a1), (b0 + ox0, b1)))
            glyph = segdist2(tx, ty, segs) < 0.16
            sub = c[out]
            sub[glyph] = WHITE
            c[out] = sub
    if is_canard.any():
        c = panel_lines(c, sx, (2.2, 2.9), is_canard, fwd)
    if tag == T_WING:
        top = n[:, 2] > 0.5
        c = panel_lines(c, sx, (3.0, 3.8, 4.6, 5.4, 6.0), np.ones(k, bool), fwd)
        c = panel_lines(c, fwd, (-1.5, -3.0, -4.5, -6.0), sx < WINGTIP_X, sx)
        # 조종면 경첩선(뒷전에서 0.55m)과 조종면 나눔
        dte = segdist2(sx, fwd, WING_TE)
        hinge = (np.abs(dte - 0.55) < 0.011) & (sx < WINGTIP_X)
        c[hinge] = LINE
        c[(dte < 0.55) & near(sx, (3.6, 5.9), 0.011)] = LINE
        # 휘장(윗면): 주황 고리 + 짙은 원 + 흰 쉐브론 두 개(앞을 가리킨다)
        ix, ifw, r = INSIGNIA
        dx = sx - ix
        dy = fwd - ifw
        rr = np.hypot(dx, dy)
        ins = top & (rr < r)
        c[ins & (rr > r * 0.82)] = ORANGE
        c[ins & (rr <= r * 0.82)] = CHARCOAL
        chev = segdist2(dx, dy, (((-0.28, -0.12), (0.0, 0.2)), ((0.0, 0.2), (0.28, -0.12)),
                                 ((-0.28, -0.32), (0.0, 0.0)), ((0.0, 0.0), (0.28, -0.32)))) < 0.045
        c[ins & chev & (rr <= r * 0.82)] = WHITE
        # 주황 날개 끝
        tip = sx > WINGTIP_X
        c[tip] = ORANGE * (0.9 + 0.14 * vnoise(P[tip], 1.5))[:, None]
        c[tip & plate_le] = ORANGE * 0.7
        c[np.abs(sx - WINGTIP_X) < 0.03] = LINE * 0.8
        c[tip & (np.abs(sx - (WINGTIP_X + 0.12)) < 0.018)] = WHITE * 0.95      # 경계 옆 흰 가는 줄
    return np.clip(c, 0.0, 1.0)


def paint(ob):
    me = ob.data
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    tags = [p.material_index for p in me.polygons]
    img = np.zeros((TEX, TEX, 3), np.float32)
    filled = np.zeros((TEX, TEX), bool)
    for tri in me.loop_triangles:
        tag = tags[tri.polygon_index]
        n = np.array(me.polygons[tri.polygon_index].normal[:])
        U = np.array([uv[l].uv[:] for l in tri.loops]) * TEX - 0.5
        P = np.array([me.vertices[v].co[:] for v in tri.vertices])
        x0, y0 = np.floor(U.min(0)).astype(int) - 1
        x1, y1 = np.ceil(U.max(0)).astype(int) + 1
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, TEX - 1), min(y1, TEX - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        px = gx.ravel().astype(np.float64)
        py = gy.ravel().astype(np.float64)
        (ax, ay), (bx, by), (cx, cy) = U
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12:
            continue
        l0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den
        l1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den
        l2 = 1 - l0 - l1
        m = (l0 >= -0.02) & (l1 >= -0.02) & (l2 >= -0.02)
        if not m.any():
            continue
        Pw = np.stack((l0[m], l1[m], l2[m]), 1) @ P
        col = color(Pw, np.tile(n, (len(Pw), 1)), tag)
        ix = px[m].astype(int)
        iy = py[m].astype(int)
        img[iy, ix] = col
        filled[iy, ix] = True
    for _ in range(10):        # 섬 둘레 번짐(밉맵 · 이음매)
        if filled.all():
            break
        acc = np.zeros_like(img)
        cnt = np.zeros(filled.shape, np.float32)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sf = np.roll(filled, (dy, dx), (0, 1))
            acc += np.roll(img, (dy, dx), (0, 1)) * sf[..., None]
            cnt += sf
        grow = (~filled) & (cnt > 0)
        img[grow] = acc[grow] / cnt[grow][:, None]
        filled |= grow
    img[~filled] = GRAY_TOP
    os.makedirs(OUT, exist_ok=True)
    im = bpy.data.images.new(NAME + "_Albedo", TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), np.float32)
    rgba[..., :3] = np.clip(img, 0, 1)
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = os.path.join(OUT, NAME + "_Albedo.png")
    im.file_format = 'PNG'
    im.save()
    return im


def textured_materials(ob, im):
    """내보낼 재질 두 칸: 0 몸체(날개 · 흡입구 · 노즐 포함), 1 유리. 둘 다 같은 텍스처."""
    me = ob.data
    glass = [p.material_index == MAT_CANOPY for p in me.polygons]
    me.materials.clear()
    for nm in (NAME + "_Body", NAME + "_Glass"):
        mat = bpy.data.materials.get(nm) or bpy.data.materials.new(nm)
        mat.use_nodes = True
        nt = mat.node_tree
        tex = nt.nodes.get("Albedo") or nt.nodes.new("ShaderNodeTexImage")
        tex.name = "Albedo"
        tex.image = im
        bsdf = nt.nodes.get("Principled BSDF")
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        me.materials.append(mat)
    for p, g in zip(me.polygons, glass):
        p.material_index = 1 if g else 0


def write_fbx(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='OFF',   # LUNA와 같게 — 유니티에서 회전·배율 없이 메시가 미터·+Z 앞
                             path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    print(f"[XFW] 내보냄 {os.path.join(OUT, NAME + '.fbx')}")


def preview_textured(prefix):
    """도장 미리보기 2x2: 비스듬히 앞 · 비스듬히 뒤 / 위 · 아래."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_cavity = False
    sh.show_shadows = True
    sh.shadow_intensity = 0.25
    sh.show_object_outline = False
    sh.background_type = 'VIEWPORT'
    sh.background_color = (0.42, 0.5, 0.6)
    W, H = 900, 600
    shots = [("p_front", (-15, -17, 8), (0, 0.5, 0.3), False, 0),
             ("p_rear", (13, 17, 7), (0, 0.5, 0.4), False, 0),
             ("p_top", (0, 0, 40), None, True, 32.0),
             ("p_below", (0, 0, -40), None, True, 32.0)]
    tiles = []
    for name, loc, tgt, ortho, size in shots:
        cd = bpy.data.cameras.new(name)
        cam = bpy.data.objects.new(name, cd)
        scene.collection.objects.link(cam)
        cam.location = loc
        if tgt is None:
            cam.rotation_euler = (0, 0, math.radians(180)) if loc[2] > 0 else (math.radians(180), 0, 0)
            cd.type = 'ORTHO'
            cd.ortho_scale = size
        else:
            cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
            cd.lens = 50
        cd.clip_end = 200
        scene.camera = cam
        scene.render.resolution_x, scene.render.resolution_y = W, H
        path = f"{prefix}_{name}.png"
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        tiles.append(np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4))
    top_row = np.concatenate([tiles[0], tiles[1]], axis=1)
    bottom_row = np.concatenate([tiles[2], tiles[3]], axis=1)
    sheet = np.concatenate([bottom_row, top_row], axis=0)
    out = bpy.data.images.new("psheet", sheet.shape[1], sheet.shape[0])
    out.pixels = sheet.ravel()
    out.filepath_raw = f"{prefix}_paint.png"
    out.file_format = 'PNG'
    out.save()
    print(f"[XFW] 도장 미리보기 {prefix}_paint.png")


if __name__ == "__main__" and os.environ.get("ACO_XFW_PAINT"):
    ob = build()
    shade(ob)
    unwrap(ob)
    im = paint(ob)
    textured_materials(ob, im)
    if os.environ.get("ACO_XFW_EXPORT"):
        write_fbx(ob)
    pv = os.environ.get("ACO_XFW_PREVIEW")
    if pv:
        preview_textured(pv)
    bl = os.environ.get("ACO_XFW_BLEND")
    if bl:
        bpy.ops.wm.save_as_mainfile(filepath=bl)
elif __name__ == "__main__":
    build()
    pv = os.environ.get("ACO_XFW_PREVIEW")
    if pv:
        preview(pv)
    bl = os.environ.get("ACO_XFW_BLEND")
    if bl:
        bpy.ops.wm.save_as_mainfile(filepath=bl)
