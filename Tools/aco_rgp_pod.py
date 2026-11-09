"""RGP(레일건 포드) 파일런 모델 두 개 — 왼쪽 파일런의 축전기 포드(ACO_RGP_Cap), 오른쪽 파일런의 레일건 포드(ACO_RGP_Rail).
MMP와 같은 계열: 옆구리 모서리(차인)가 있는 납작한 육각 단면, 각진 기수, 좁아지다 평평한 끝면으로 끝나는 꼬리, 날개 없음, 고리형 걸이 둘.
 - 레일건 포드: 몸통 앞이 좁아져 가는 배럴로 이어진다. 배럴은 위아래 레일 두 줄(가운데 가로 슬롯), 뿌리 덮개와 죔쇠 띠 둘. 슬롯 안쪽 면에 구리 레일, 가운데 빛나는 판(_Glow — 늘 켜진 빛나는 재질).
   윗면 뒤쪽에 방열 리브, 꼬리 끝면에 배기 슬롯, 안쪽 옆면(+x — 오른쪽 파일런은 로컬 +x가 동체 쪽)에 전원 연결구.
 - 축전기 포드: 조금 높고 짧다. 양쪽 윗 경사면에 충전 표시창(6칸 — 막대 Bar0~5, Unity에서 늘 켜진 빛나는 재질), 몸통을 감는 띠 셋, 아랫 경사면에 방열판(앞뒤로 긴 지느러미), 안쪽 옆면(-x — 왼쪽 파일런은 로컬 -x가 동체 쪽)에 전원 연결구.
실행: blender --background --factory-startup --python Tools/aco_rgp_pod.py   (ACO_RGP_OUT으로 다른 폴더에 만들 수 있다)
좌표: 외곽 파일런 로컬. +y 기수, +z 위, +x 오른쪽, 단위 m. Unity에서 파일런 아래에 Transform 0으로 넣으면 제자리다(MMP와 같다).
UV: 몸통을 아래, 위, 오른쪽, 왼쪽 네 띠로 따로 편다(MMP와 같은 방식). 표기는 모두 가공이다.
포구 자리(레일 끝 가운데와 방향)는 끝에 LP 줄로 찍는다 — Unity 쪽 표와 같아야 한다."""
import math
import os
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_RGP_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "RGP"))
FONT = "C:/Windows/Fonts/arialbd.ttf"
TEX_W, TEX_H = 4096, 2048
ATLAS_W, ATLAS_H = 4.5, 2.25    # 텍스처 크기(m) — 1m = 910픽셀

TOP = -0.40          # 몸통 윗면 높이. 파일런 아래 끝이 z -0.354다
BEVEL = 0.018
RIVET_STEP = 0.045
LUGS = (0.781, 0.292)
# 크기: 몸통은 아래 치수로 만든 뒤 SCALE배로 키운다(윗면 높이 TOP과 두 걸이 사이 SCALE_Y0을 기준으로 — 걸이는 키우지 않고 같은 자리에 단다).
# 텍스처 띠도 같이 늘어난다(표기가 SCALE배로 커진다).
SCALE = 1.5
SCALE_Y0 = 0.54


def scaled(p):
    return (p[0] * SCALE, SCALE_Y0 + (p[1] - SCALE_Y0) * SCALE, TOP + (p[2] - TOP) * SCALE)

PLAIN = (4.22, 0.10)    # 작은 부품이 쓰는 도장면
DARK = (4.33, 1.30)     # 슬롯·창 속
COPPER = (4.33, 1.62)   # 레일
GLOW = (4.33, 1.92)     # 빛나는 막대·판(Unity에선 따로 빛나는 재질)
KIND_BODY, KIND_PLAIN, KIND_DARK, KIND_COPPER, KIND_GLOW = 0, 1, 2, 3, 4

STRIPS = {
    "bottom": ((12, 13, 14, 15, 0), 0.30, False, False),
    "top": ((5, 6, 7), 0.85, False, False),
    "right": ((1, 2, 3, 4), 1.35, False, False),
    "left": ((8, 9, 10, 11), 1.90, True, True),
}
STRIP_U = 0.05

RAIL = dict(
    name="ACO_RGP_Rail", height=0.30, half_w=0.19, bevel=0.04, chine=0.5,
    nose=1.22, tail=-0.78, seams=(0.28, -0.46),
    rows=((-0.48, 1.0), (-0.78, 0.62)),               # 뒤쪽(앞쪽 몸통·배럴 덮개로 모이는 부분은 rail_rings가 만든다)
    fair=(0.40, 1.22), fair_top=0.86,                  # 덮개: 몸통 단면 → 배럴 뿌리 단면. 윗면은 걸이 뒤(fair_top)부터 내려온다
    barrel_y=(1.21, 2.00, 2.05), barrel_tip=0.75, slot=0.04, rail_hw=0.05, rail_h=0.045,
)
CAP = dict(
    name="ACO_RGP_Cap", height=0.38, half_w=0.21, bevel=0.045, chine=0.5,
    nose=1.12, tail=-0.72, seams=(0.90,),
    rows=((1.12, 0.12), (1.07, 0.30), (0.82, 1.0), (-0.42, 1.0), (-0.72, 0.62)),
    bars=(0.565, 0.475, 0.385, 0.295, 0.205, 0.115), bar_len=0.07,
    bands=(0.66, -0.02, -0.42), sink=(0.02, 0.58), fins=5,
)
C = RAIL   # 지금 만드는 포드


def section(s):
    """점 16개 — 모서리를 비스듬히 깎은(베벨) 사각 단면: 아랫면 오른쪽 끝, 오른쪽 옆면 다섯 점(아래 베벨 끝 → 위 베벨 시작), 윗면 오른쪽 끝,
    왼쪽(거울), 아랫면 가운데 둘. 가운데 높이를 중심으로 닮은꼴로 줄인다. 띠(STRIPS): 아래 = 아랫면 + 아래 베벨 둘, 위 = 윗면 + 위 베벨 둘, 옆 = 옆면."""
    H = C["height"]
    h = H * s
    zc0 = TOP - H + H * C["chine"]
    zb = zc0 - h * C["chine"]
    zt = zb + h
    W, b = C["half_w"] * s, C["bevel"] * s
    right = [(W - b, zb)] + [(W, zb + b + (h - 2 * b) * k / 4.0) for k in range(5)] + [(W - b, zt)]
    left = [(-x, z) for x, z in reversed(right)]
    return right + left + [(-(W - b) / 3, zb), ((W - b) / 3, zb)]


def chine_z():
    return TOP - C["height"] + C["height"] * C["chine"]


def to_blender(p):
    return (-p[0], -p[2], p[1])


def strip_u(name, y):
    s = C["nose"] - y
    if STRIPS[name][2]:
        s = C["nose"] - C["tail"] - s
    return STRIP_U + s


def norm(v):
    l = math.sqrt(sum(a * a for a in v))
    return tuple(a / l for a in v)


def strip_half(name):
    pts = section(1.0)
    segs = STRIPS[name][0]
    return sum(math.hypot(pts[s][0] - pts[(s + 1) % 16][0], pts[s][1] - pts[(s + 1) % 16][1]) for s in segs) * 0.5 - 0.005


def side_v(seg, frac):
    """오른쪽 띠의 seg번째 변(0 = 아래 경사면 시작) 위 frac 자리의 v 오프셋(+ = 위). 왼쪽 띠도 같은 값이다."""
    pts = section(1.0)
    segs = STRIPS["right"][0]
    lens = [math.hypot(pts[s][0] - pts[s + 1][0], pts[s][1] - pts[s + 1][1]) for s in segs]
    return -sum(lens) * 0.5 + sum(lens[:seg]) + lens[seg] * frac


def facet(side, a_idx, b_idx):
    """경사면(a → b)의 가운데 점, 바깥 방향, 면을 따라 위로 가는 방향(x, z)."""
    pts = section(1.0)
    a, b = pts[a_idx], pts[b_idx]
    if side < 0:
        a, b = (-a[0], a[1]), (-b[0], b[1])
    mid = ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5)
    up = norm((b[0] - a[0], b[1] - a[1]))
    if up[1] < 0:
        up = (-up[0], -up[1])
    n = (up[1], -up[0])
    if n[0] * side < 0:
        n = (-n[0], -n[1])
    return mid, n, up


def clip(poly, z, keep_above):
    """볼록 다각형(x, z)을 z 평면으로 자른다."""
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        ia = (a[1] >= z) if keep_above else (a[1] <= z)
        ib = (b[1] >= z) if keep_above else (b[1] <= z)
        if ia:
            out.append(a)
        if ia != ib:
            t = (z - a[1]) / (b[1] - a[1])
            out.append((a[0] + (b[0] - a[0]) * t, z))
    clean = []
    for p in out:
        if not clean or math.hypot(p[0] - clean[-1][0], p[1] - clean[-1][1]) > 1e-5:
            clean.append(p)
    if math.hypot(clean[0][0] - clean[-1][0], clean[0][1] - clean[-1][1]) < 1e-5:
        clean.pop()
    return clean


class Solid:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.kind = self.bm.faces.layers.int.new("kind")
        self.maps = []

    def loft(self, rings, kind=KIND_PLAIN, smooth=False, caps=(True, True)):
        vs = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = len(rings[0])
        faces = []
        for a, b in zip(vs, vs[1:]):
            for k in range(n):
                m = (k + 1) % n
                faces.append(self.bm.faces.new((a[k], a[m], b[m], b[k])))
        for f in faces:
            f.smooth = smooth
        if caps[0]:
            faces.append(self.bm.faces.new(vs[0]))
        if caps[1]:
            faces.append(self.bm.faces.new(list(reversed(vs[-1]))))
        for f in faces:
            f[self.kind] = kind
        return vs, faces

    def box(self, c, s, kind=KIND_PLAIN):
        x, y, z = s[0] * 0.5, s[1] * 0.5, s[2] * 0.5
        q = ((-x, -z), (x, -z), (x, z), (-x, z))
        self.loft([[(c[0] + a, c[1] + d, c[2] + b) for a, b in q] for d in (-y, y)], kind)

    def axis_tube(self, c, d, r, l0, l1, n=14, kind=KIND_PLAIN, smooth=True):
        up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.9 else (1.0, 0.0, 0.0)
        a = norm((d[1] * up[2] - d[2] * up[1], d[2] * up[0] - d[0] * up[2], d[0] * up[1] - d[1] * up[0]))
        b = (d[1] * a[2] - d[2] * a[1], d[2] * a[0] - d[0] * a[2], d[0] * a[1] - d[1] * a[0])
        rings = []
        for l in (l0, l1):
            ring = []
            for k in range(n):
                t = k * math.tau / n
                ring.append(tuple(c[i] + d[i] * l + (a[i] * math.cos(t) + b[i] * math.sin(t)) * r for i in range(3)))
            rings.append(ring)
        self.loft(rings, kind, smooth)

    def facet_quad(self, side, a_idx, b_idx, y, cv, hy, hv, off0, off1, kind):
        """경사면 위 사각 판(경사면 방향 cv ± hv, 앞뒤 y ± hy, 면에서 off0 ~ off1)."""
        (mx, mz), n2, up2 = facet(side, a_idx, b_idx)

        def q(off):
            return [(mx + up2[0] * (cv + sv * hv) + n2[0] * off, y + sy * hy, mz + up2[1] * (cv + sv * hv) + n2[1] * off)
                    for sy, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        self.loft([q(off0), q(off1)], kind)

    def body(self, front_kind=KIND_PLAIN, front_rings=None):
        rings = [[(x, y, z_) for x, z_ in section(s)] for y, s in C["rows"]]
        if front_rings:
            rings = front_rings + rings
        verts = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = 16
        for name, (segments, vc, flip_u, flip_v) in STRIPS.items():
            for r in range(len(rings) - 1):
                params = []
                for ring in (r, r + 1):
                    pts = rings[ring]
                    lengths = [math.hypot(pts[s][0] - pts[(s + 1) % n][0], pts[s][2] - pts[(s + 1) % n][2]) for s in segments]
                    total = sum(lengths)
                    t = [-total * 0.5]
                    for dd in lengths:
                        t.append(t[-1] + dd)
                    params.append(t)
                y0, y1 = rings[r][0][1], rings[r + 1][0][1]
                for i, s in enumerate(segments):
                    m = (s + 1) % n
                    f = self.bm.faces.new((verts[r][s], verts[r][m], verts[r + 1][m], verts[r + 1][s]))
                    f[self.kind] = KIND_BODY
                    f.smooth = True
                    sign = -1.0 if flip_v else 1.0
                    self.maps.append((f, {
                        verts[r][s]: (strip_u(name, y0), vc + sign * params[0][i]),
                        verts[r][m]: (strip_u(name, y0), vc + sign * params[0][i + 1]),
                        verts[r + 1][m]: (strip_u(name, y1), vc + sign * params[1][i + 1]),
                        verts[r + 1][s]: (strip_u(name, y1), vc + sign * params[1][i]),
                    }))
        for ring, kind in ((verts[0], front_kind), (list(reversed(verts[-1])), KIND_PLAIN)):
            f = self.bm.faces.new(ring)
            f[self.kind] = kind

    def scale_all(self):
        """지금까지 만든 꼭짓점을 SCALE배로(블렌더 좌표 ↔ 파일런 좌표를 거쳐)."""
        for v in self.bm.verts:
            x, y, z = -v.co.x, v.co.z, -v.co.y
            x, y, z = scaled((x, y, z))
            v.co = to_blender((x, y, z))

    def lugs(self):
        for y in LUGS:
            self.box((0, y, TOP + 0.006), (0.075, 0.14, 0.02))
            cz = TOP + 0.026
            steps = 9
            for k in range(steps):
                a0 = math.radians(-20 + k * 220 / steps)
                a1 = math.radians(-20 + (k + 1) * 220 / steps)
                q = [(r * math.cos(t_), r * math.sin(t_)) for r, t_ in ((0.029, a0), (0.049, a0), (0.049, a1), (0.029, a1))]
                self.loft([[(x, y + a, cz + b) for a, b in q] for x in (-0.016, 0.016)])

    def power_link(self, side, y):
        """안쪽 차인의 전원 연결구: 받침 판 + 짧은 원통 + 어두운 끝."""
        x = C["half_w"] * side
        zc = chine_z()
        self.box((x - side * 0.004, y, zc), (0.012, 0.11, 0.06))
        self.axis_tube((x, y, zc), (side, 0.0, 0.0), 0.022, 0.0, 0.03)
        self.axis_tube((x, y, zc), (side, 0.0, 0.0), 0.026, 0.024, 0.036)
        self.axis_tube((x, y, zc), (side, 0.0, 0.0), 0.014, 0.035, 0.038, kind=KIND_DARK)

    def finish(self, name):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        # 베벨 모서리(45도)처럼 꺾인 모서리는 날카롭게 — 몸통 면은 스무스라 길이 방향(덮개 곡면)만 매끈하다
        for e in self.bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(35):
                e.smooth = False
        for f, table in self.maps:
            for loop in f.loops:
                u, v = table[loop.vert]
                loop[self.uv].uv = (u / ATLAS_W, v / ATLAS_H)
        for f in self.bm.faces:
            k = f[self.kind]
            if k == KIND_BODY:
                continue
            for loop in f.loops:
                c = loop.vert.co
                lx, ly, lz = -c.x, c.z, -c.y
                if k == KIND_DARK:
                    u, v = DARK
                elif k == KIND_COPPER:
                    u, v = COPPER[0] + (ly % 0.1) * 0.5, COPPER[1] + (lx % 0.1) * 0.5
                elif k == KIND_GLOW:
                    u, v = GLOW
                else:
                    u = PLAIN[0] + (ly % 0.15) + 0.02
                    v = PLAIN[1] + ((lx + lz) % 0.15)
                loop[self.uv].uv = (u / ATLAS_W, v / ATLAS_H)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


# ───────────── 레일건 포드 ─────────────

def rail_poly(upper):
    """레일 한 줄의 단면(x, z) — 모서리를 깎은 납작한 사각. 슬롯 쪽 면이 평평하다."""
    zc = chine_z()
    g, hw, h, cf = C["slot"] * 0.5, C["rail_hw"], C["rail_h"], 0.01
    if upper:
        z0, z1 = zc + g, zc + g + h
        return [(-hw, z0), (hw, z0), (hw, z1 - cf), (hw - cf, z1), (-hw + cf, z1), (-hw, z1 - cf)], z0
    z0, z1 = zc - g, zc - g - h
    return [(-hw, z1 + cf), (-hw + cf, z1), (hw - cf, z1), (hw, z1 + cf), (hw, z0), (-hw, z0)], z0


def collar_poly(extra):
    """두 레일을 함께 감는 띠의 단면."""
    zc = chine_z()
    hw = C["rail_hw"] + extra
    hz = C["slot"] * 0.5 + C["rail_h"] + extra
    cf = 0.012
    return [(-hw, zc - hz + cf), (-hw + cf, zc - hz), (hw - cf, zc - hz), (hw, zc - hz + cf),
            (hw, zc + hz - cf), (hw - cf, zc + hz), (-hw + cf, zc + hz), (-hw, zc + hz - cf)]


def ray_hit(poly, cx, cz, dx, dz):
    """(cx, cz)에서 (dx, dz) 방향 반직선이 볼록 다각형 둘레와 만나는 점."""
    best = None
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        ex, ez = bx - ax, bz - az
        den = dx * ez - dz * ex
        if abs(den) < 1e-9:
            continue
        s = ((ax - cx) * ez - (az - cz) * ex) / den
        u = ((ax - cx) * dz - (az - cz) * dx) / den
        if s > 0 and -1e-6 <= u <= 1 + 1e-6 and (best is None or s < best):
            best = s
    return (cx + dx * best, cz + dz * best)


def rail_rings():
    """앞쪽 몸통이 배럴 뿌리로 매끈하게 모이는 고리들(앞 → 뒤). 몸통 단면의 각 점을 가운데에서 같은 방향의 배럴 뿌리 둘레 점으로 옮긴다.
    아래·옆은 일찍부터, 위는 늦게(걸이 뒤부터) 모인다 — 위가 곧게 이어져 파일런에 붙고 아래에서 깎여 올라가는 모양."""
    zc = chine_z()
    full = section(1.0)
    collar = collar_poly(0.016)
    target = []
    for x, z in full:
        d = math.hypot(x, z - zc)
        target.append(ray_hit(collar, 0.0, zc, x / d, (z - zc) / d))
    zt = TOP
    y0, y1 = C["fair"]
    ys = [y1, 1.20, 1.15, 1.08, 1.00, 0.91, 0.81, 0.70, 0.56, y0]

    def smooth(a):
        a = min(max(a, 0.0), 1.0)
        return a * a * (3 - 2 * a)
    rings = []
    for y in ys:
        ring = []
        for (x, z), (tx, tz) in zip(full, target):
            w = min(max((z - zc) / (zt - zc), 0.0), 1.0)   # 0 = 차인 아래, 1 = 윗면
            t_low = smooth((y - y0) / (y1 - y0))
            t_top = smooth((y - C["fair_top"]) / (y1 - C["fair_top"]))
            k = t_low + (t_top - t_low) * w
            ring.append((x + (tx - x) * k, y, z + (tz - z) * k))
        rings.append(ring)
    return rings


def build_rail():
    s = Solid()
    s.body(front_rings=rail_rings())
    zc = chine_z()
    g = C["slot"] * 0.5
    y0, y1, y2 = C["barrel_y"]
    # 배럴: 위아래 레일 두 줄(가운데 가로 슬롯). 끝은 슬롯 쪽 모서리를 축으로 줄여 비스듬히 깎는다
    for upper in (True, False):
        poly, z = rail_poly(upper)
        rings = [[(x * sc, y, z + (pz - z) * sc) for x, pz in poly] for y, sc in ((y0, 1.0), (y1, 1.0), (y2, C["barrel_tip"]))]
        s.loft(rings, KIND_PLAIN, smooth=False, caps=(False, True))
        # 구리 레일: 슬롯 쪽 면에 붙은 얇은 판
        zz = z - 0.002 if upper else z + 0.002
        s.box((0.0, (y0 + y1) * 0.5 + 0.02, zz), (C["rail_hw"] * 1.6, y1 - y0 - 0.04, 0.004), KIND_COPPER)
    # 슬롯 안쪽 끝(약실)
    s.box((0.0, y0 + 0.012, zc), (C["rail_hw"] * 2.0, 0.008, C["slot"]), KIND_DARK)
    # 죔쇠 띠 셋
    for yc, half, extra in ((1.42, 0.013, 0.008), (1.63, 0.013, 0.008), (1.84, 0.013, 0.008)):
        s.loft([[(x, yc + dy, z_) for x, z_ in collar_poly(extra)] for dy in (-half, half)], KIND_PLAIN)
    # 방열 리브: 윗면 뒤쪽, 가로로 9장
    for k in range(9):
        y = 0.12 - k * 0.07
        s.box((0.0, y, TOP + 0.012), (0.20, 0.012, 0.024))
    # 꼬리 끝면 배기 슬롯
    yt = C["rows"][-1][0]
    for dz in (-0.035, 0.0, 0.035):
        s.box((0.0, yt - 0.002, zc + dz), (0.16, 0.006, 0.014), KIND_DARK)
    # 안쪽(+x — 파일런 로컬 x는 기체 x와 반대라 오른쪽 파일런에선 +x가 동체 쪽) 전원 연결구
    s.power_link(1, 0.20)
    s.scale_all()
    s.lugs()
    body = s.finish(C["name"])

    glow = Solid()
    glow.box((0.0, (y0 + 0.1 + y1) * 0.5, zc), (C["rail_hw"] * 1.4, y1 - y0 - 0.1, C["slot"] * 0.35), KIND_GLOW)
    glow.scale_all()
    return body, glow.finish(C["name"] + "_Glow")


def rail_muzzle():
    return scaled((0.0, C["barrel_y"][2], chine_z())), (0.0, 1.0, 0.0)


# ───────────── 축전기 포드 ─────────────

def build_cap():
    s = Solid()
    s.body()
    zc = chine_z()
    # 몸통을 감는 띠 셋(살짝 굵은 단면 고리)
    for y in C["bands"]:
        rings = [[(x * 1.0, y + dy, zc + (z - zc) * 1.0) for x, z in section(1.035)] for dy in (-0.022, 0.022)]
        s.loft(rings, KIND_PLAIN, smooth=True)
    # 충전 표시창: 양쪽 윗 경사면(p4 → p5). 창틀 판 + 칸마다 어두운 유리
    bars = []
    ys = C["bars"]
    span = (ys[0] + C["bar_len"] * 0.5 + 0.02, ys[-1] - C["bar_len"] * 0.5 - 0.02)
    for side in (1, -1):
        mid_y = (span[0] + span[1]) * 0.5
        s.facet_quad(side, 4, 5, mid_y, 0.0, (span[0] - span[1]) * 0.5, 0.034, -0.003, 0.005, KIND_PLAIN)
        for y in ys:
            s.facet_quad(side, 4, 5, y, 0.0, C["bar_len"] * 0.5, 0.022, 0.0, 0.0065, KIND_DARK)
    for y in ys:
        b = Solid()
        for side in (1, -1):
            b.facet_quad(side, 4, 5, y, 0.0, C["bar_len"] * 0.5 - 0.006, 0.016, 0.0, 0.0085, KIND_GLOW)
        bars.append(b)
    # 방열판: 양쪽 아랫 경사면(p1 → p3)에 받침 판 + 앞뒤로 긴 얇은 지느러미 여러 장
    y_a, y_b = C["sink"]
    ym, hy = (y_a + y_b) * 0.5, (y_b - y_a) * 0.5
    for side in (1, -1):
        s.facet_quad(side, 1, 3, ym, -0.008, hy + 0.012, 0.05, -0.003, 0.004, KIND_PLAIN)
        n = C["fins"]
        for k in range(n):
            v = -0.008 + (k - (n - 1) * 0.5) * 0.02
            s.facet_quad(side, 1, 3, ym, v, hy, 0.0018, 0.004, 0.034, KIND_PLAIN)
        # 지느러미 앞뒤 끝을 잇는 막이
        for yy in (y_a, y_b):
            s.facet_quad(side, 1, 3, yy, -0.008, 0.003, 0.042, 0.004, 0.012, KIND_PLAIN)
    # 아랫면 방전 단자(작은 원통 둘)
    zb = TOP - C["height"]
    for y in (-0.22, -0.30):
        s.axis_tube((0.0, y, zb), (0.0, 0.0, -1.0), 0.012, -0.002, 0.03)
    # 안쪽(-x — 왼쪽 파일런에선 -x가 동체 쪽) 전원 연결구
    s.power_link(-1, 0.02)
    s.scale_all()
    s.lugs()
    for b in bars:
        b.scale_all()
    return s.finish(C["name"]), [b.finish(C["name"] + "_Bar%d" % i) for i, b in enumerate(bars)]


# ───────────── 텍스처 ─────────────

def noise(size, cells, seed):
    rng = np.random.RandomState(seed)
    g = rng.rand(cells + 1, cells + 1)
    g[-1, :] = g[0, :]
    g[:, -1] = g[:, 0]
    t = (np.arange(size) + 0.5) / size * cells
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    a, b = g[i][:, i], g[i + 1][:, i]
    c, d = g[i][:, i + 1], g[i + 1][:, i + 1]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fy) + b * fy) * (1 - fx) + (c * (1 - fy) + d * fy) * fx


INK = (0.13, 0.14, 0.15)
LINE = (0.27, 0.285, 0.295)
RED = (0.62, 0.1, 0.08)
YELLOW = (0.8, 0.62, 0.12)
PAINT = (0.56, 0.585, 0.59)
BLUE = (0.16, 0.38, 0.62)


def render_page(draw):
    page = bpy.data.scenes.new("page")
    page.render.engine = 'BLENDER_WORKBENCH'
    page.display.shading.light = 'FLAT'
    page.display.shading.color_type = 'MATERIAL'
    page.display.shading.show_shadows = False
    page.display.render_aa = '32'
    page.render.film_transparent = True
    page.render.resolution_x = TEX_W
    page.render.resolution_y = TEX_H
    page.render.image_settings.file_format = 'PNG'
    page.render.image_settings.color_mode = 'RGBA'
    page.view_settings.view_transform = 'Standard'
    temp = os.path.join(OUT, "_page.png")
    page.render.filepath = temp
    cam = bpy.data.objects.new("pcam", bpy.data.cameras.new("pcam"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ATLAS_W
    cam.location = (ATLAS_W / 2, ATLAS_H / 2, 5)
    page.collection.objects.link(cam)
    page.camera = cam
    font = bpy.data.fonts.load(FONT)
    mats = {}
    depth = [0.0]

    class Pen:
        def place(self, o, u, v, col, rot=0.0):
            depth[0] += 0.0002
            o.location = (u, v, depth[0])
            o.rotation_euler = (0, 0, rot)
            if col not in mats:
                m = bpy.data.materials.new("m")
                m.diffuse_color = tuple(c ** 2.2 for c in col) + (1,)
                mats[col] = m
            o.data.materials.append(mats[col])
            page.collection.objects.link(o)

        def text(self, body, u, v, size, col, spacing=1.06, rot=0.0):
            c = bpy.data.curves.new("t", 'FONT')
            c.body = body
            c.font = font
            c.size = size
            c.align_x = 'CENTER'
            c.align_y = 'CENTER'
            c.space_character = spacing
            c.space_line = 1.2
            self.place(bpy.data.objects.new("t", c), u, v, col, rot)

        def poly(self, points, u, v, col, rot=0.0):
            bm = bmesh.new()
            bm.faces.new([bm.verts.new((x, y, 0)) for x, y in points])
            me = bpy.data.meshes.new("p")
            bm.to_mesh(me)
            bm.free()
            self.place(bpy.data.objects.new("p", me), u, v, col, rot)

        def rect(self, w, h, u, v, col):
            self.poly([(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)], u, v, col)

        def outline(self, w, h, u, v, col, th=0.004):
            self.rect(w, th, u, v + h / 2, col)
            self.rect(w, th, u, v - h / 2, col)
            self.rect(th, h, u + w / 2, v, col)
            self.rect(th, h, u - w / 2, v, col)

        def bolt(self, u, v, size, col):
            k = size / 0.07
            pts = [(-0.012, 0.035), (0.016, 0.035), (0.002, 0.006), (0.018, 0.006), (-0.014, -0.035), (-0.002, -0.004), (-0.018, -0.004)]
            self.poly([(x * k, y * k) for x, y in pts], u, v, col)

        def hazard(self, w, h, u, v, step=0.03):
            self.rect(w, h, u, v, YELLOW)
            n = int(w / step) + 2
            for i in range(-n, n):
                x0 = i * step
                pts = [(x0, -h / 2), (x0 + step * 0.5, -h / 2), (x0 + step * 0.5 + h * 0.6, h / 2), (x0 + h * 0.6, h / 2)]
                pts = [(min(max(x, -w / 2), w / 2), y) for x, y in pts]
                if abs(pts[0][0] - pts[1][0]) + abs(pts[2][0] - pts[3][0]) > 1e-4:
                    self.poly(pts, u, v, INK)

    pen = Pen()
    for name, (_, vc, _, _) in STRIPS.items():
        half = strip_half(name)
        for y in C["seams"]:
            u = strip_u(name, y)
            pen.rect(0.004, half * 2, u, vc, LINE)
            k = int(half / RIVET_STEP)
            for i in range(-k, k + 1):
                pen.rect(0.007, 0.007, u + 0.018, vc + i * RIVET_STEP, LINE)
                pen.rect(0.007, 0.007, u - 0.018, vc + i * RIVET_STEP, LINE)
    draw(pen)

    bpy.ops.render.render(write_still=True, scene="page")
    img = bpy.data.images.load(temp)
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    bpy.data.scenes.remove(page)
    os.remove(temp)
    return px.reshape(h, w, 4)


def data_plate(pen, name, vc, y, lines):
    u = strip_u(name, y)
    pen.rect(0.34, 0.108, u, vc, INK)
    pen.rect(0.332, 0.100, u, vc, PAINT)
    for k, line in enumerate(lines):
        pen.text(line, u - 0.045, vc + 0.027 - k * 0.027, 0.016, INK)
    for i, w in enumerate((2, 1, 3, 1, 1, 2, 3, 1, 2, 1, 1, 3, 2, 1)):
        pen.rect(0.0012 * w, 0.07, u + 0.105 + i * 0.0045, vc, INK)


def draw_rail(pen):
    up_v = side_v(3, 0.5)     # 윗 경사면 가운데
    low_v = side_v(0, 0.55)   # 아랫 경사면 가운데
    for name in ("right", "left"):
        vc = STRIPS[name][1]
        # 레일 집 앞쪽 노란 띠(실탄) + 경고
        a, b = strip_u(name, 0.38), strip_u(name, 0.33)
        pen.rect(abs(a - b), strip_half(name) * 2, (a + b) * 0.5, vc, YELLOW)
        pen.text("RGP-16  RAIL GUN POD", strip_u(name, 0.0), vc + up_v - 0.005, 0.026, INK)
        pen.text("16 RDS   LOT 03-27   SER 71204", strip_u(name, 0.0), vc + up_v - 0.032, 0.014, INK)
        pen.text("DANGER  MUZZLE BLAST - KEEP CLEAR AHEAD", strip_u(name, 0.58), vc + low_v, 0.017, RED)
        data_plate(pen, name, vc - 0.005, -0.28, ("RGP-16   CONT R2-0419", "WT 512 KG    CG 0.94 M", "INSP 10-26   QA 2"))
        # 전원 연결 표기(안쪽 = 왼쪽 띠)
        if name == "right":
            u = strip_u(name, 0.20)
            pen.outline(0.13, 0.075, u, vc, LINE)
            pen.text("PWR LINK", u, vc - 0.052, 0.014, INK)
            pen.bolt(u + 0.095, vc, 0.045, RED)
        else:
            u = strip_u(name, 0.20)
            pen.text("COIL BUS A", u, vc - 0.03, 0.016, INK)
        pen.text("NO PUSH", strip_u(name, -0.60), vc + 0.01, 0.016, INK)
        # 아랫 경사면 점검창
        u = strip_u(name, 0.05)
        pen.outline(0.24, 0.05, u, vc + low_v, LINE)
        for su in (-1, 1):
            for sv in (-1, 1):
                pen.rect(0.008, 0.008, u + su * 0.108, vc + low_v + sv * 0.016, LINE)
        pen.text("CAP COUPLING", u, vc + low_v, 0.015, INK)
    vc = STRIPS["top"][1]
    for y, label in ((LUGS[0] - 0.13, "FWD LUG"), (LUGS[1] - 0.125, "AFT LUG")):
        pen.text(label, strip_u("top", y), vc, 0.02, INK)
    pen.text("HOT SURFACE", strip_u("top", -0.52), vc, 0.022, RED)
    pen.text("NO STEP", strip_u("top", 0.92), vc, 0.024, INK)
    vc = STRIPS["bottom"][1]
    u = strip_u("bottom", 0.50)
    pen.poly([(-0.09, 0.0), (-0.03, 0.04), (-0.03, 0.016), (0.03, 0.016), (0.03, -0.016), (-0.03, -0.016), (-0.03, -0.04)], u - 0.05, vc, INK)
    pen.text("FWD", u + 0.07, vc, 0.04, INK)
    pen.text("RGP-16", strip_u("bottom", 0.05), vc, 0.034, INK)
    u = strip_u("bottom", -0.28)
    pen.rect(0.30, 0.085, u, vc, RED)
    pen.rect(0.29, 0.075, u, vc, PAINT)
    pen.text("KEEP CLEAR OF\nMUZZLE LINE", u, vc, 0.02, RED)


def draw_cap(pen):
    up_v = side_v(3, 0.5)
    low_v = side_v(0, 0.55)
    ys = C["bars"]
    for name in ("right", "left"):
        vc = STRIPS[name][1]
        # 충전 표시창 위·아래 표기
        u0, u1 = strip_u(name, ys[0]), strip_u(name, ys[-1])
        sgn = 1 if name == "right" else -1
        pen.text("CHARGE", u1 + sgn * 0.068, vc + up_v, 0.014, INK, 1.1)
        for k, y in enumerate(ys):
            pen.rect(0.003, 0.010, strip_u(name, y), vc + up_v - 0.041, INK)
        pen.text("0", u1 + sgn * 0.03, vc + up_v - 0.041, 0.013, INK)
        pen.text("FULL", u0 - sgn * 0.04, vc + up_v - 0.041, 0.013, INK)
        # 기수 쪽 위험 띠
        a, b = strip_u(name, 0.80), strip_u(name, 0.76)
        pen.hazard(0.12, 0.05, strip_u(name, 0.75), vc - 0.03)
        pen.text("RGP-C  CAPACITOR POD", strip_u(name, 0.36), vc + 0.024, 0.022, INK)
        pen.text("DANGER\nHIGH VOLTAGE", strip_u(name, 0.75), vc + up_v - 0.01, 0.016, RED)
        data_plate(pen, name, vc + 0.04, -0.22, ("RGP-C   CONT C1-0388", "BANK 18 MJ   WT 466 KG", "INSP 10-26   QA 2"))
        # 아랫 경사면 점검창
        u = strip_u(name, -0.24)
        pen.outline(0.25, 0.07, u, vc + low_v + 0.008, RED)
        pen.text("DISCHARGE BEFORE\nSERVICE", u, vc + low_v + 0.008, 0.014, RED)
        pen.text("HOT", strip_u(name, C["sink"][1] + 0.035), vc + low_v + 0.01, 0.016, RED)
        if name == "left":
            u = strip_u(name, 0.02)
            pen.outline(0.16, 0.085, u, vc, LINE)
            pen.text("PWR LINK", u, vc - 0.058, 0.016, INK)
            pen.bolt(u - 0.11, vc, 0.05, RED)
        pen.text("NO PUSH", strip_u(name, -0.61), vc + 0.01, 0.016, INK)
    vc = STRIPS["top"][1]
    for y, label in ((LUGS[0] - 0.13, "FWD LUG"), (LUGS[1] - 0.125, "AFT LUG")):
        pen.text(label, strip_u("top", y), vc, 0.02, INK)
    pen.text("NO STEP", strip_u("top", -0.22), vc, 0.032, INK, 1.2)
    vc = STRIPS["bottom"][1]
    u = strip_u("bottom", 0.86)
    pen.poly([(-0.09, 0.0), (-0.03, 0.04), (-0.03, 0.016), (0.03, 0.016), (0.03, -0.016), (-0.03, -0.016), (-0.03, -0.04)], u - 0.05, vc, INK)
    pen.text("FWD", u + 0.07, vc, 0.04, INK)
    pen.text("RGP-C", strip_u("bottom", 0.30), vc, 0.034, INK)
    pen.bolt(strip_u("bottom", 0.05), vc, 0.08, RED)
    pen.text("HV", strip_u("bottom", 0.05) + 0.06, vc, 0.03, RED)
    u = strip_u("bottom", -0.26)
    pen.text("GND", u - 0.09, vc, 0.016, INK)
    for k, w in enumerate((0.03, 0.02, 0.01)):
        pen.rect(w, 0.0035, u - 0.13, vc - 0.004 - k * 0.008, INK)


def make_texture(draw):
    base = np.array([0.56, 0.585, 0.59], dtype=np.float32)
    img = np.tile(base[None, None, :], (TEX_H, TEX_W, 1))
    n = TEX_W
    img *= 1 + ((noise(n, 36, 11) - 0.5) * 0.1)[:TEX_H, :, None] + ((noise(n, 160, 13) - 0.5) * 0.04)[:TEX_H, :, None]

    def patch(uv, col, r=0.1):
        x0, x1 = int((uv[0] - r) / ATLAS_W * TEX_W), int((uv[0] + r) / ATLAS_W * TEX_W)
        y0, y1 = int((uv[1] - r) / ATLAS_H * TEX_H), int((uv[1] + r) / ATLAS_H * TEX_H)
        img[y0:y1, x0:x1] = col
    patch(DARK, (0.03, 0.032, 0.036))
    patch(COPPER, (0.62, 0.36, 0.2), 0.14)
    patch(GLOW, (0.55, 0.85, 1.0))
    page = render_page(draw)
    alpha = page[..., 3:4] * 0.92
    img = img * (1 - alpha) + page[..., :3] * alpha
    out = np.ones((TEX_H, TEX_W, 4), dtype=np.float32)
    out[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new(C["name"] + "_Albedo", TEX_W, TEX_H, alpha=False)
    im.pixels.foreach_set(out.ravel())
    path = os.path.join(OUT, C["name"] + "_Albedo.png")
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return path


def write_fbx(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def link(me):
    obj = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    C = RAIL
    make_texture(draw_rail)
    body, glow = build_rail()
    objs = [link(body), link(glow)]
    print("RAIL TRIS", sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons))
    write_fbx(os.path.join(OUT, C["name"] + ".fbx"), objs)
    m, d = rail_muzzle()
    print("MUZZLE pos (%.4f, %.4f, %.4f)  dir (%.4f, %.4f, %.4f)" % (m + d))

    C = CAP
    make_texture(draw_cap)
    body, bars = build_cap()
    objs = [link(body)] + [link(b) for b in bars]
    print("CAP TRIS", sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons))
    write_fbx(os.path.join(OUT, C["name"] + ".fbx"), objs)
    print("DONE")
