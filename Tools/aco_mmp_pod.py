"""MMP(마이크로미사일 포드) 파일런 모델. 옆구리에 날카로운 모서리(차인)가 있는 납작한 육각 단면, 평면으로 각지게 깎은 기수와 꼬리(스텔스 형상, 포드라 날개 없음, 꼬리는 평평한 끝면, 길이 2.02m).
발사 셀은 아랫면에 VLS처럼 덮개 달린 사각 셀 8개(2열 × 4) — 아래·조금 앞으로만 사출한다(옆으로 쏘면 같은 날개의 다른 파일런·무장에 걸린다). 사출한 뒤 점화한다.
셀 줄 옆 양쪽 아래 경사면에 사출 가스 벤트(루버 달린 슬롯 3개씩, 셀 줄마다 하나) — 바깥·아래로 뿜는다.
걸이는 SCM과 같은 고리 두 개.
실행: blender --background --factory-startup --python Tools/aco_mmp_pod.py   (ACO_MMP_OUT으로 다른 폴더에 만들 수 있다)
좌표: 외곽 파일런(pyronout) 로컬. +y 기수, +z 위, 단위 m. Unity에서 파일런 아래에 Transform 0으로 넣으면 제자리다(SCM과 같다).
UV: 몸통을 아래, 위, 오른쪽, 왼쪽 네 띠로 따로 편다(SCM과 같은 방식 — 좌우 표기가 모두 바로 읽힌다). 표기는 모두 가공이다.
발사 지점(셀 가운데와 방향)은 끝에 LP 줄로 찍는다 — Unity 쪽 표(ACO_PrefabBuilder.MmpLaunch)와 같아야 한다."""
import math
import os
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_MMP_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "MMP"))
NAME = "ACO_MMP"
FONT = "C:/Windows/Fonts/arialbd.ttf"
TEX_W, TEX_H = 4096, 2048
ATLAS_W, ATLAS_H = 4.5, 2.25    # 텍스처 크기(m) — 1m = 910픽셀

TOP = -0.40        # 몸통 윗면 높이. 파일런 아래 끝이 z -0.354다
HEIGHT = 0.36
HALF_W = 0.29      # 차인(옆 모서리)까지 반폭
TOP_HALF = 0.10    # 윗면 반폭
BOT_HALF = 0.14    # 아랫면 반폭
CHINE = 0.55       # 차인 높이(아래에서 높이의 비율)
BEVEL = 0.018
NOSE = 1.26
TAIL = -0.70
SEAMS = (0.86, 0.31, -0.38)
RIVET_STEP = 0.045

# (y, 폭 배율, 높이 배율, 위로 올림). 기수·꼬리는 같은 비율로 좁아지며 차인 높이로 모인다(올림 = 가운데가 차인 높이에 오게). 기수 끝은 살짝 뭉툭
ROWS = (
    (NOSE, 0.09, 0.09, 0.01638),   # 끝을 살짝 뭉툭하게 — 작은 끝면
    (1.223, 0.22, 0.22, 0.01404),  # 기수: 차인 높이를 향해 같은 비율로 좁아지다(올림 = 0.018 × (1 - 배율))
    (0.88, 1.0, 1.0, 0.0),
    (-0.40, 1.0, 1.0, 0.0),
    (TAIL, 0.60, 0.60, 0.0072),   # 꼬리: 같은 비율로 줄여(닮은꼴) 옆면이 평평한 사다리꼴, 끝은 평평한 면
)

STRIPS = {
    "bottom": ((12, 13, 14, 15, 0), 0.29, False, False),   # 폭 ±0.162
    "top": ((5, 6, 7), 0.85, False, False),                 # 폭 ±0.122
    "right": ((1, 2, 3, 4), 1.33, False, False),            # 폭 ±0.23
    "left": ((8, 9, 10, 11), 1.80, True, True),
}
STRIP_HALF = {"bottom": 0.16, "top": 0.12, "right": 0.225, "left": 0.225}
STRIP_U = 0.05
PLAIN = (4.22, 0.10)   # 작은 부품이 쓰는 도장면
DARK = (4.33, 1.30)    # 셀 속
LUGS = (0.781, 0.292)  # 고리형 걸이의 y(SCM과 같다)

CELL = 0.105           # 셀 한 변
CELL_X = 0.064         # 두 열의 x(±)
CELL_YS = (0.58, 0.40, 0.22, 0.04)   # 네 줄의 y(앞에서 뒤로)
CELL_FWD = 0.30        # 셀 축이 앞으로 기운 정도(아래 1에 대해). 좌우로는 기울이지 않는다


def section(sw, sh, lift):
    """점 16개: 아랫면 셋, 오른쪽 아래 모서리·아래 경사면·차인·위 경사면·위 모서리, 윗면, 왼쪽(거울), 아랫면. 각진 모서리는 작게 깎는다."""
    h = HEIGHT * sh
    zb = TOP - HEIGHT + lift + (HEIGHT - h) * 0.5
    zt = zb + h
    zc = zb + h * CHINE
    W, T, B, d = HALF_W * sw, TOP_HALF * sw, BOT_HALF * sw, BEVEL * min(sw, sh)
    right = [
        (B, zb),                              # p0
        (B + d, zb + d * 0.8),                # p1
        ((B + W) * 0.5, (zb + zc) * 0.5),     # p2
        (W, zc - d),                          # p3
        (W - d * 0.4, zc + d),                # p4
        (T + d, zt - d * 0.7),                # p5
        (T, zt),                              # p6
    ]
    left = [(-x, z) for x, z in reversed(right)]   # p7(-T, zt) ... p13(-B, zb)
    return right + left + [(-B / 3, zb), (B / 3, zb)]


def to_blender(p):
    return (-p[0], -p[2], p[1])


def strip_u(name, y):
    s = NOSE - y
    if STRIPS[name][2]:
        s = NOSE - TAIL - s
    return STRIP_U + s


def norm(v):
    l = math.sqrt(sum(a * a for a in v))
    return tuple(a / l for a in v)


def side_facet(side):
    """아래 경사면(p1 → p3)의 가운데 점과 바깥 방향, 경사면을 따라 위로 가는 방향(파일런 x, z)."""
    pts = section(1.0, 1.0, 0.0)
    a, b = pts[1], pts[3]
    if side < 0:
        a, b = (-a[0], a[1]), (-b[0], b[1])
    mid = ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5)
    up = norm((b[0] - a[0], b[1] - a[1]))
    n = (up[1] * side, -up[0] * side) if side > 0 else (-up[1], up[0])
    n = norm((n[0], n[1])) if (n[0] * side) > 0 else norm((-n[0], -n[1]))
    return mid, n, up


def cells():
    """셀마다 (가운데, 바깥 방향, 면을 따라 가로 방향, 발사 방향). 앞줄부터 오른쪽(+x)·왼쪽 번갈아 — 연사하면 좌우가 번갈아 앞에서 뒤로 나간다."""
    out = []
    zb = TOP - HEIGHT
    d = norm((0.0, CELL_FWD, -1.0))
    for y in CELL_YS:
        for x in (CELL_X, -CELL_X):
            out.append(((x, y, zb), (0.0, 0.0, -1.0), (1.0, 0.0, 0.0), d))
    return out


def launch_points():
    return [((c[0] + n[0] * 0.01, c[1], c[2] + n[2] * 0.01), d) for c, n, a, d in cells()]


class Solid:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.kind = self.bm.faces.layers.int.new("kind")
        self.maps = []

    def loft(self, rings, kind=1, smooth=False):
        vs = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = len(rings[0])
        faces = []
        for a, b in zip(vs, vs[1:]):
            for k in range(n):
                m = (k + 1) % n
                faces.append(self.bm.faces.new((a[k], a[m], b[m], b[k])))
        for f in faces:
            f.smooth = smooth
        faces.append(self.bm.faces.new(vs[0]))
        faces.append(self.bm.faces.new(list(reversed(vs[-1]))))
        for f in faces:
            f[self.kind] = kind
        return vs, faces

    def box(self, c, s, kind=1):
        x, y, z = s[0] * 0.5, s[1] * 0.5, s[2] * 0.5
        q = ((-x, -z), (x, -z), (x, z), (-x, z))
        self.loft([[(c[0] + a, c[1] + d, c[2] + b) for a, b in q] for d in (-y, y)], kind)

    def axis_tube(self, c, d, r, l0, l1, n=14, kind=1, smooth=True):
        """c를 지나는 축 d 위의 원통(l0 ~ l1)."""
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

    def body(self):
        rings = [[(x, y, z_) for x, z_ in section(sw, sh, lift)] for y, sw, sh, lift in ROWS]
        # 크기 0인 단면(기수 끝)은 꼭짓점 하나를 함께 쓴다
        verts = [[self.bm.verts.new(to_blender(r[0]))] * len(r) if all(p == r[0] for p in r)
                 else [self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = 16
        for name, (segments, vc, flip_u, flip_v) in STRIPS.items():
            for r in range(len(rings) - 1):
                params = []
                for ring in (r, r + 1):
                    pts = rings[ring]
                    lengths = []
                    for s in segments:
                        a, b = pts[s], pts[(s + 1) % n]
                        lengths.append(math.hypot(a[0] - b[0], a[2] - b[2]))
                    total = sum(lengths)
                    t = [-total * 0.5]
                    for dd in lengths:
                        t.append(t[-1] + dd)
                    params.append(t)
                y0, y1 = rings[r][0][1], rings[r + 1][0][1]
                for i, s in enumerate(segments):
                    m = (s + 1) % n
                    quad = []
                    for v_ in (verts[r][s], verts[r][m], verts[r + 1][m], verts[r + 1][s]):
                        if v_ not in quad:
                            quad.append(v_)
                    f = self.bm.faces.new(quad)
                    f[self.kind] = 0
                    f.smooth = True   # 몸통·기수·꼬리 모두 스무스(평면·스무스를 섞으면 경계에서 꺾여 보인다)
                    sign = -1.0 if flip_v else 1.0
                    self.maps.append((f, {
                        verts[r][s]: (strip_u(name, y0), vc + sign * params[0][i]),
                        verts[r][m]: (strip_u(name, y0), vc + sign * params[0][i + 1]),
                        verts[r + 1][m]: (strip_u(name, y1), vc + sign * params[1][i + 1]),
                        verts[r + 1][s]: (strip_u(name, y1), vc + sign * params[1][i]),
                    }))
        for ring in (verts[0], list(reversed(verts[-1]))):
            if ring[0] is ring[1]:
                continue
            f = self.bm.faces.new(ring)
            f[self.kind] = 1

    def finish(self):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        for f, table in self.maps:
            for loop in f.loops:
                u, v = table[loop.vert]
                loop[self.uv].uv = (u / ATLAS_W, v / ATLAS_H)
        for f in self.bm.faces:
            k = f[self.kind]
            if k == 0:
                continue
            for loop in f.loops:
                c = loop.vert.co
                lx, ly, lz = -c.x, c.z, -c.y
                if k == 2:
                    u, v = DARK
                else:
                    u = PLAIN[0] + (ly % 0.15) + 0.02
                    v = PLAIN[1] + ((lx + lz) % 0.15)
                loop[self.uv].uv = (u / ATLAS_W, v / ATLAS_H)
        me = bpy.data.meshes.new(NAME)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def build():
    s = Solid()
    s.body()
    # 파일런 걸이(SCM과 같은 고리)
    for y in LUGS:
        s.box((0, y, TOP + 0.006), (0.075, 0.14, 0.02))
        cz = TOP + 0.026
        steps = 9
        for k in range(steps):
            a0 = math.radians(-20 + k * 220 / steps)
            a1 = math.radians(-20 + (k + 1) * 220 / steps)
            q = [(r * math.cos(t_), r * math.sin(t_)) for r, t_ in ((0.029, a0), (0.049, a0), (0.049, a1), (0.029, a1))]
            s.loft([[(x, y + a, cz + b) for a, b in q] for x in (-0.016, 0.016)])
    # 발사 셀(VLS처럼): 면에 붙은 테두리 + 그 안의 덮개 판. 덮개는 앞뒤 두 장이 가운데에서 맞닿고(어두운 이음선), 바깥 끝에 경첩 둘
    for c, n, a, d in cells():
        along = (0.0, 1.0, 0.0)

        def quad(half_u, half_v, off, cu=0.0):
            pts = []
            for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                pts.append(tuple(c[i] + along[i] * (cu + su * half_u) + a[i] * sv * half_v + n[i] * off for i in range(3)))
            return pts

        h = CELL * 0.5
        outer, inner = quad(h + 0.012, h + 0.012, 0.004), quad(h, h, 0.004)
        base_o, base_i = quad(h + 0.012, h + 0.012, -0.004), quad(h, h, -0.004)
        for k in range(4):
            m = (k + 1) % 4
            s.loft([[base_o[k], base_o[m], base_i[m], base_i[k]], [outer[k], outer[m], inner[m], inner[k]]], 1)
        # 덮개 두 장(앞·뒤)과 그 사이 어두운 틈
        for cu in (-h * 0.5 - 0.002, h * 0.5 + 0.002):
            s.loft([quad(h * 0.5 - 0.004, h - 0.004, -0.002, cu), quad(h * 0.5 - 0.004, h - 0.004, 0.0025, cu)], 1)
        s.loft([quad(0.004, h - 0.004, -0.003), quad(0.004, h - 0.004, 0.0012)], 2)
        # 경첩: 앞뒤 끝에 작은 막대 둘씩
        for cu in (-h + 0.008, h - 0.008):
            for sv in (-0.6, 0.6):
                hc = tuple(c[i] + along[i] * cu + a[i] * sv * h + n[i] * 0.004 for i in range(3))
                s.box((hc[0], hc[1], hc[2]), (0.022, 0.010, 0.006))

    # 사출 가스 벤트: 양쪽 아래 경사면, 셀 줄마다 하나. 살짝 솟은 판 + 어두운 슬롯 3개 + 슬롯마다 위쪽에 바깥으로 내민 루버
    for side in (1, -1):
        (mx, mz), n2, up2 = side_facet(side)
        n = (n2[0], 0.0, n2[1])
        up = (up2[0], 0.0, up2[1])
        for y in CELL_YS:
            def vq(cy, cv, hy, hv, off):
                return [(mx + up[0] * (cv + sv * hv) + n[0] * off, cy + sy * hy, mz + up[2] * (cv + sv * hv) + n[2] * off)
                        for sy, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            cv = -0.02
            s.loft([vq(y, cv, 0.072, 0.046, -0.004), vq(y, cv, 0.072, 0.046, 0.005)], 1)
            for k in (-1, 0, 1):
                v = cv + k * 0.026
                s.loft([vq(y, v, 0.058, 0.0065, -0.002), vq(y, v, 0.058, 0.0065, 0.0058)], 2)
                # 루버: 슬롯 위 가장자리에서 바깥·아래로 비스듬히 내민 얇은 판
                top = [(mx + up[0] * (v + 0.009) + n[0] * 0.005, y + sy * 0.06, mz + up[2] * (v + 0.009) + n[2] * 0.005) for sy in (-1, 1)]
                tip = [(mx + up[0] * (v - 0.004) + n[0] * 0.016, y + sy * 0.06, mz + up[2] * (v - 0.004) + n[2] * 0.016) for sy in (-1, 1)]
                th = 0.0025
                s.loft([[top[0], top[1], tip[1], tip[0]],
                        [tuple(top[0][i] + up[i] * th for i in range(3)), tuple(top[1][i] + up[i] * th for i in range(3)),
                         tuple(tip[1][i] + up[i] * th for i in range(3)), tuple(tip[0][i] + up[i] * th for i in range(3))]], 1)
    return s.finish()


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


def render_page():
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

    def place(o, u, v, col):
        depth[0] += 0.0002
        o.location = (u, v, depth[0])
        if col not in mats:
            m = bpy.data.materials.new("m")
            m.diffuse_color = tuple(c ** 2.2 for c in col) + (1,)
            mats[col] = m
        o.data.materials.append(mats[col])
        page.collection.objects.link(o)

    def text(body, u, v, size, col, spacing=1.06):
        c = bpy.data.curves.new("t", 'FONT')
        c.body = body
        c.font = font
        c.size = size
        c.align_x = 'CENTER'
        c.align_y = 'CENTER'
        c.space_character = spacing
        c.space_line = 1.2
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

    INK = (0.13, 0.14, 0.15)
    LINE = (0.27, 0.285, 0.295)
    RED = (0.62, 0.1, 0.08)
    YELLOW = (0.8, 0.62, 0.12)
    PAINT = (0.56, 0.585, 0.59)

    for name, (_, vc, _, _) in STRIPS.items():
        half = STRIP_HALF[name]
        for y in SEAMS:
            if name == "bottom" and CELL_YS[-1] < y < CELL_YS[0]:
                continue   # 셀 묶음은 따로 떼는 판 — 이음선이 셀 사이를 지나지 않는다
            u = strip_u(name, y)
            rect(0.004, half * 2, u, vc, LINE)
            k = int(half / RIVET_STEP)
            for i in range(-k, k + 1):
                rect(0.007, 0.007, u + 0.018, vc + i * RIVET_STEP, LINE)
                rect(0.007, 0.007, u - 0.018, vc + i * RIVET_STEP, LINE)
        # 기수 쪽 노란 띠(실탄)
        a, b = strip_u(name, 1.01), strip_u(name, 0.96)
        rect(abs(a - b), half * 2, (a + b) * 0.5, vc, YELLOW)

    for name in ("right", "left"):
        vc = STRIPS["right"][1] if name == "right" else STRIPS["left"][1]
        u = strip_u(name, 0.58)
        text("MMP-16  MICRO-MISSILE POD", u, vc + 0.085, 0.032, INK)
        text("16 RDS   LOT 11-26   SER 30552", u, vc + 0.05, 0.018, INK)
        # 셀 둘레 경고 문구(아래 경사면 — 셀 사이)
        text("DANGER  EJECTION ZONE BELOW", strip_u(name, -0.21), vc - 0.06, 0.02, RED)
        u = strip_u(name, -0.55)
        poly([(-0.018, -0.016), (0.018, -0.016), (0.0, 0.016)], u - 0.07, vc + 0.06, RED)
        text("HOIST", u + 0.02, vc + 0.06, 0.02, INK)

    def outline(w, h, u, v, col, th=0.004):
        rect(w, th, u, v + h / 2, col)
        rect(w, th, u, v - h / 2, col)
        rect(th, h, u + w / 2, v, col)
        rect(th, h, u - w / 2, v, col)

    for name in ("right", "left"):
        vc = STRIPS[name][1]
        # 자료판(윗 경사면, 꼬리 쪽): 테두리 + 세 줄 + 바코드
        u = strip_u(name, -0.16)
        rect(0.34, 0.108, u, vc + 0.115, INK)
        rect(0.332, 0.100, u, vc + 0.115, PAINT)
        text("MMP-16   CONT A1-7731", u - 0.045, vc + 0.145, 0.016, INK)
        text("WT 418 KG    CG 1.06 M", u - 0.045, vc + 0.118, 0.016, INK)
        text("INSP 09-26   QA 4", u - 0.045, vc + 0.091, 0.016, INK)
        for i, w in enumerate((2, 1, 3, 1, 1, 2, 3, 1, 2, 1, 1, 3, 2, 1)):
            rect(0.0012 * w, 0.07, u + 0.105 + i * 0.0045, vc + 0.118, INK)
        # 장전 안전 표시창(앞 걸이 옆 윗 경사면)
        u = strip_u(name, 0.12)
        rect(0.05, 0.03, u, vc + 0.13, INK)
        rect(0.042, 0.022, u, vc + 0.13, (0.18, 0.42, 0.2))
        text("ARM / SAFE", u, vc + 0.165, 0.014, INK)
        # 전기 점검창(아랫 경사면, 벤트 뒤): 테두리 + 모서리 나사
        u = strip_u(name, -0.22)
        outline(0.22, 0.075, u, vc - 0.145, LINE)
        for su in (-1, 1):
            for sv in (-1, 1):
                rect(0.008, 0.008, u + su * 0.098, vc - 0.145 + sv * 0.026, LINE)
        text("ELEC ACCESS", u, vc - 0.145, 0.016, INK)
        # 접지점(기수 쪽 아랫 경사면)
        u = strip_u(name, 0.78)
        for k, w in enumerate((0.03, 0.02, 0.01)):
            rect(w, 0.0035, u, vc - 0.15 - k * 0.008, INK)
        rect(0.0035, 0.018, u, vc - 0.135, INK)
        text("GND", u + 0.045, vc - 0.15, 0.016, INK)
        # 벤트 위 화살표: 가스가 나가는 쪽
        for y in CELL_YS:
            u = strip_u(name, y)
            poly([(-0.012, 0.0), (0.012, 0.0), (0.0, -0.014)], u, vc - 0.075, RED)
        text("EJECT GAS", strip_u(name, 0.49), vc - 0.045, 0.014, RED)
        # 꼬리 콘
        text("NO PUSH", strip_u(name, -0.57), vc + 0.015, 0.016, INK)

    vc = STRIPS["bottom"][1]
    # 셀 번호(발사 순서) — 각 셀 바로 뒤. +x 셀이 v +쪽
    for i, (c, _n, _a, _d) in enumerate(cells()):
        text(str(i + 1), strip_u("bottom", c[1] - 0.09), vc + (0.064 if c[0] > 0 else -0.064), 0.026, INK)
    # 셀 묶음 둘레 빨간 테두리
    y0, y1 = CELL_YS[0] + 0.078, CELL_YS[-1] - 0.11
    outline(y0 - y1, 0.262, (strip_u("bottom", y0) + strip_u("bottom", y1)) * 0.5, vc, RED, 0.005)
    text("MMP-16", strip_u("bottom", -0.58), vc, 0.03, INK)
    u = strip_u("bottom", -0.225)
    rect(0.28, 0.085, u, vc, RED)
    rect(0.27, 0.075, u, vc, PAINT)
    text("LAUNCH ZONE\nSTAY CLEAR", u, vc, 0.022, RED)
    u = strip_u("bottom", 0.82)
    poly([(-0.09, 0.0), (-0.03, 0.04), (-0.03, 0.016), (0.03, 0.016), (0.03, -0.016), (-0.03, -0.016), (-0.03, -0.04)], u - 0.05, vc, INK)
    text("FWD", u + 0.07, vc, 0.04, INK)

    vc = STRIPS["top"][1]
    for y, label in ((LUGS[0] - 0.13, "FWD LUG"), (LUGS[1] - 0.13, "AFT LUG")):
        text(label, strip_u("top", y), vc, 0.02, INK)
    for y in LUGS:
        for dv in (-0.085, 0.085):
            u = strip_u("top", y)
            rect(0.07, 0.036, u, vc + dv, LINE)
            rect(0.062, 0.028, u, vc + dv, PAINT)
            text("PAD", u, vc + dv, 0.012, LINE)
    for y in (-0.22,):
        u = strip_u("top", y)
        rect(0.26, 0.003, u, vc + 0.03, INK)
        rect(0.26, 0.003, u, vc - 0.03, INK)
        text("NO STEP", u, vc, 0.034, INK, 1.2)

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


def make_texture():
    base = np.array([0.56, 0.585, 0.59], dtype=np.float32)
    img = np.tile(base[None, None, :], (TEX_H, TEX_W, 1))
    n = TEX_W
    img *= 1 + ((noise(n, 36, 11) - 0.5) * 0.1)[:TEX_H, :, None] + ((noise(n, 160, 13) - 0.5) * 0.04)[:TEX_H, :, None]
    du, dv = DARK
    x0, x1 = int((du - 0.1) / ATLAS_W * TEX_W), int((du + 0.1) / ATLAS_W * TEX_W)
    y0, y1 = int((dv - 0.1) / ATLAS_H * TEX_H), int((dv + 0.1) / ATLAS_H * TEX_H)
    img[y0:y1, x0:x1] = (0.03, 0.032, 0.036)
    page = render_page()
    alpha = page[..., 3:4] * 0.92
    img = img * (1 - alpha) + page[..., :3] * alpha
    out = np.ones((TEX_H, TEX_W, 4), dtype=np.float32)
    out[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new(NAME + "_Albedo", TEX_W, TEX_H, alpha=False)
    im.pixels.foreach_set(out.ravel())
    path = os.path.join(OUT, NAME + "_Albedo.png")
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return path


def write_fbx(path):
    bpy.ops.export_scene.fbx(filepath=path, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    make_texture()
    me = build()
    obj = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(obj)
    print("TRIS", sum(len(p.vertices) - 2 for p in me.polygons))
    write_fbx(os.path.join(OUT, NAME + ".fbx"))
    for i, (c, d) in enumerate(launch_points()):
        print("LP %d  pos (%.4f, %.4f, %.4f)  dir (%.4f, %.4f, %.4f)" % (i, c[0], c[1], c[2], d[0], d[1], d[2]))
    print("DONE")
