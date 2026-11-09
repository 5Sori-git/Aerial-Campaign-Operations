"""SCM(장거리 집속탄 미사일) 파일런 모델. 꼬리로 갈수록 가늘어지는 몸통, 고리형 걸이, 좌우 축에 하나씩 달려 나란히 접히는 대칭 날개, 꼬리날개 6장, 터보제트 흡입구와 배기구.
실행: blender --background --factory-startup --python Tools/aco_scm_missile.py
좌표: 외곽 파일런(pyronout) 로컬. +y 기수, +z 위, 단위 m. Unity에서 파일런 아래에 Transform 0으로 넣으면 제자리다.
ACO_SCM_Flight.fbx는 날개를 편 탄체용이다. +Z 기수, +Y 위, 배기구가 탄체 트레일(z -1.9) 자리에 온다.
UV: 몸통을 아래, 위, 오른쪽, 왼쪽 네 띠로 따로 편다. 텍스처 1m = 910픽셀로 어디서나 같다. 표기는 텍스처에 직접 그리고, 문구는 모두 가공이다."""
import math
import os
import bmesh
import bpy
import numpy as np
from mathutils import Matrix

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "SCM")
NAME = "ACO_SCM"
FLIGHT = "ACO_SCM_Flight"
FLIGHT_CENTER_Y = -0.33   # 파일런 좌표의 이 y가 탄체 원점이다. 배기구가 z -1.95에 온다
FONT = "C:/Windows/Fonts/arialbd.ttf"
TEX_W, TEX_H = 4096, 2048
ATLAS_W, ATLAS_H = 4.5, 2.25    # 텍스처 크기(m)

TOP = -0.40        # 몸통 윗면 높이. 파일런 아래 끝이 z -0.354다
HEIGHT = 0.40
ZM = TOP - HEIGHT * 0.5
NOSE = 1.90
TAIL = -2.20
SEAMS = (1.15, 0.55, -0.42, -1.1, -1.75)
BAND = (0.98, 0.88)
RIVET_STEP = 0.045

# (y, 폭, 높이, 베벨 반경, 가운데 z). 뒤쪽은 윗면을 평평하게 두고 배가 올라온다
ROWS = (
    (NOSE, 0.09, 0.05, 0.018, ZM - 0.03),
    (1.64, 0.25, 0.16, 0.05, ZM - 0.015),
    (1.18, 0.44, 0.31, 0.085, ZM),
    (0.65, 0.53, 0.40, 0.10, ZM),
    (-0.20, 0.53, 0.40, 0.10, ZM),
    (-1.00, 0.47, 0.35, 0.095, TOP - 0.175),
    (-1.70, 0.36, 0.27, 0.08, TOP - 0.135),
    (TAIL, 0.22, 0.17, 0.055, TOP - 0.085),
)

# 몸통 띠: 이름 → (단면 한 바퀴의 구간 번호, 아틀라스 세로 가운데(m), 가로를 뒤집는가, 세로를 뒤집는가)
# 뒤집기는 바깥에서 볼 때 글자가 거울상이 되지 않게 정한 값이다. 이름의 오른쪽, 왼쪽은 파일런 로컬 +x, -x다
STRIPS = {
    "bottom": ((13, 14, 15, 0), 0.29, False, False),
    "top": ((5, 6, 7, 8), 0.85, False, False),
    "right": ((1, 2, 3, 4), 1.34, False, False),
    "left": ((9, 10, 11, 12), 1.77, True, True),
}
STRIP_U = 0.05       # 띠가 시작하는 가로 위치(m)
WING_V = 2.11
WING_U = (0.05, 2.25)
PLAIN = (4.22, 0.10)   # 날개 말고 작은 부품이 쓰는 도장면
DARK = (4.33, 1.30)    # 흡입구, 배기구, 시커 창
WING_PIVOT = -0.02     # 날개 축의 y. 앞으로 옮겨 접힌 길이를 몸통 전체로 쓴다
WING_AXIS_X = 0.16     # 좌우 날개 축의 x
WING_STOW = 1.8        # 접힌 날개 끝을 안쪽으로 모으는 각도. 뒤로 갈수록 몸통이 좁아진다
WING_SPAN = 2.05       # 축에서 날개 끝까지
WING_OPEN = 55.0       # 편 날개가 뒤로 접힌 자세에서 돈 각도. 뒤로 35도 젖힌 자세가 된다
LUGS = (0.781, 0.292)  # 고리형 걸이의 y. 파일런 갈고리 끝(z -0.354)이 고리 구멍에 들어간다


def hull(y):
    """몸통 단면 (폭, 높이, 베벨, 가운데 z)을 y에서 선형으로 구한다."""
    for a, b in zip(ROWS, ROWS[1:]):
        if b[0] <= y <= a[0]:
            t = (a[0] - y) / (a[0] - b[0])
            return tuple(a[k] + (b[k] - a[k]) * t for k in range(1, 5))
    raise ValueError(y)


def bottom(y):
    w, h, c, z = hull(y)
    return z - h * 0.5


def rounded(w, h, c, zc):
    """모서리마다 세 번 꺾는 단면(점 16개). 각이 부드럽게 보인다."""
    x, z = w * 0.5 - c, h * 0.5 - c
    pts = []
    for cx, cz, start in ((x, -z, -90.0), (x, z, 0.0), (-x, z, 90.0), (-x, -z, 180.0)):
        for k in range(4):
            a = math.radians(start + k * 30.0)
            pts.append((cx + c * math.cos(a), zc + cz + c * math.sin(a)))
    return pts


def to_blender(p):
    return (-p[0], -p[2], p[1])


def strip_u(name, y):
    """몸통 y → 띠의 가로 위치(m). 왼쪽 띠는 앞뒤가 반대다."""
    s = NOSE - y
    if STRIPS[name][2]:
        s = NOSE - TAIL - s
    return STRIP_U + s


class Solid:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.kind = self.bm.faces.layers.int.new("kind")   # 0 표에서 UV를 받는 면, 1 도장면, 2 어두운 면
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
            f.smooth = smooth      # 옆면만 부드럽게. 양 끝 마개는 각을 남긴다
        faces.append(self.bm.faces.new(vs[0]))
        faces.append(self.bm.faces.new(list(reversed(vs[-1]))))
        for f in faces:
            f[self.kind] = kind
        return vs, faces

    def box(self, c, s, kind=1):
        x, y, z = s[0] * 0.5, s[1] * 0.5, s[2] * 0.5
        q = ((-x, -z), (x, -z), (x, z), (-x, z))
        self.loft([[(c[0] + a, c[1] + d, c[2] + b) for a, b in q] for d in (-y, y)], kind)

    def tube(self, x, z, r, y0, y1, n=12, kind=1):
        ring = [(x + r * math.cos(k * math.tau / n), z + r * math.sin(k * math.tau / n)) for k in range(n)]
        self.loft([[(a, y0, b) for a, b in ring], [(a, y1, b) for a, b in ring]], kind, True)

    def wing(self, index, sign, angle):
        """날개 한 장. 왼쪽은 오른쪽의 거울상이다. UV는 접힌 자세로 재서, 펴도 표기가 그대로 따라간다."""
        # (시위 방향, 축에서 끝 쪽 거리). 시위 +쪽이 앞전이고, 접으면 앞전이 바깥을 본다
        plan = ((0.14, -0.10), (0.15, 0.0), (0.085, WING_SPAN - 0.08), (0.0, WING_SPAN), (-0.08, WING_SPAN - 0.02), (-0.15, 0.0), (-0.135, -0.10))
        c, n = math.cos(math.radians(angle)), math.sin(math.radians(angle))
        h0 = 0.016

        def turned(x, y):
            dy = y - WING_PIVOT
            return sign * WING_AXIS_X + x * c - dy * n, x * n + dy * c + WING_PIVOT

        def ring(k, z):
            return [(*turned(sign * a * k, WING_PIVOT - d), TOP + h0 + z) for a, d in plan]

        vs, faces = self.loft([ring(1.0, 0.0), ring(1.0, 0.007), ring(0.8, 0.017)], 1)
        # 표기는 윗면에만 있다. 오른쪽 날개는 글자를 반 바퀴 돌려, 폈을 때 양쪽이 같은 방향으로 읽힌다
        top = faces[-1]
        top[self.kind] = 0
        table = {}
        for v, (a, d) in zip(vs[-1], plan):
            u, w = d + 0.1, -sign * a * 0.8
            if index == 0:
                u, w = 1.8 - u, -w
            table[v] = (WING_U[index] + u, WING_V + w)
        self.maps.append((top, table))

    def body(self):
        """몸통은 띠별로 UV를 편다. 띠 경계는 모서리 베벨 가운데라 잘 보이지 않는다."""
        rings = [[(x, y, z_) for x, z_ in rounded(w, h, c, z)] for y, w, h, c, z in ROWS]
        verts = [[self.bm.verts.new(to_blender(p)) for p in r] for r in rings]
        n = 16
        for name, (segments, vc, flip_u, flip_v) in STRIPS.items():
            for r in range(len(rings) - 1):
                # 띠 가운데에서 잰 둘레 위치: 이음매 없이 같은 높이에 글자가 놓인다
                params = []
                for ring in (r, r + 1):
                    pts = rings[ring]
                    lengths = []
                    for s in segments:
                        a, b = pts[s], pts[(s + 1) % n]
                        lengths.append(math.hypot(a[0] - b[0], a[2] - b[2]))
                    total = sum(lengths)
                    t = [-total * 0.5]
                    for d in lengths:
                        t.append(t[-1] + d)
                    params.append(t)
                y0, y1 = rings[r][0][1], rings[r + 1][0][1]
                for i, s in enumerate(segments):
                    m = (s + 1) % n
                    f = self.bm.faces.new((verts[r][s], verts[r][m], verts[r + 1][m], verts[r + 1][s]))
                    f[self.kind] = 0
                    f.smooth = True
                    sign = -1.0 if flip_v else 1.0
                    self.maps.append((f, {
                        verts[r][s]: (strip_u(name, y0), vc + sign * params[0][i]),
                        verts[r][m]: (strip_u(name, y0), vc + sign * params[0][i + 1]),
                        verts[r + 1][m]: (strip_u(name, y1), vc + sign * params[1][i + 1]),
                        verts[r + 1][s]: (strip_u(name, y1), vc + sign * params[1][i]),
                    }))
        for ring in (verts[0], list(reversed(verts[-1]))):
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


def build(deployed=False):
    """deployed = 날개를 편 비행 자세."""
    s = Solid()
    s.body()
    # 시커 창: 턱 아래 어두운 다면
    s.loft([[(x, y, z + bottom(y) + 0.006) for x, z in ((-w, -0.01), (w, -0.01), (w, 0.01), (-w, 0.01))] for y, w in ((1.72, 0.045), (1.40, 0.10))], 2)
    # 파일런 걸이: 받침 위 고리. 구멍은 좌우로 뚫려 있고 파일런 갈고리가 걸린다
    # 받침 아래는 몸통 윗면(기수 쪽으로 내려간다)까지 내려 박는다 — 앞 걸이 자리는 윗면이 TOP보다 1cm 넘게 낮아 받침이 떠 보였다
    for y in LUGS:
        rings = []
        for yy in (y - 0.07, y + 0.07):
            w, h, c, zc = hull(yy)
            zb = zc + h * 0.5 - 0.008
            rings.append([(-0.0375, yy, zb), (0.0375, yy, zb), (0.0375, yy, TOP + 0.016), (-0.0375, yy, TOP + 0.016)])
        s.loft(rings)
        cz = TOP + 0.026
        steps = 9
        for k in range(steps):
            a0 = math.radians(-20 + k * 220 / steps)
            a1 = math.radians(-20 + (k + 1) * 220 / steps)
            q = [(r * math.cos(t), r * math.sin(t)) for r, t in ((0.029, a0), (0.049, a0), (0.049, a1), (0.029, a1))]
            s.loft([[(x, y + a, cz + b) for a, b in q] for x in (-0.016, 0.016)])
    # 날개 축과 날개: 좌우에 하나씩, 같은 높이로 나란히 접힌다
    for index, sign in ((0, 1), (1, -1)):
        hub = [(sign * WING_AXIS_X + 0.1 * math.cos(k * math.tau / 16), WING_PIVOT + 0.1 * math.sin(k * math.tau / 16)) for k in range(16)]
        s.loft([[(x, y, TOP + h) for x, y in hub] for h in (-0.005, 0.016)])
        s.wing(index, sign, sign * WING_OPEN if deployed else -sign * WING_STOW)
    # 꼬리날개 6장
    zt = hull(-2.0)[3]
    for k in range(6):
        a = k * math.tau / 6
        c, n = math.cos(a), math.sin(a)
        t = (-n, c)
        root = 0.09
        profile = [(-1.80, root), (-2.15, root), (-2.20, root + 0.17), (-1.97, root + 0.17)]
        s.loft([[(c * r + t[0] * w, y, zt + n * r + t[1] * w) for y, r in profile] for w in (-0.007, 0.007)])

    # 터보제트: 배 쪽 흡입구, 꼬리 배기구
    def scoop(y, k, f):
        b = bottom(y)
        return [(-0.13 * k, y, b + 0.012), (0.13 * k, y, b + 0.012), (0.10 * k, y, b - 0.09 * f), (-0.10 * k, y, b - 0.09 * f)]

    s.loft([scoop(-0.42, 1.0, 1.0), scoop(-1.05, 1.0, 1.0), scoop(-1.5, 0.72, 0.05)], 1, True)
    s.loft([[(x * 0.84, y + 0.008, z + 0.006) for x, y, z in scoop(-0.42, 1.0, 0.86)], [(x * 0.84, y + 0.02, z + 0.006) for x, y, z in scoop(-0.42, 1.0, 0.86)]], 2)
    zc = hull(TAIL)[3]
    s.tube(0, zc, 0.075, TAIL - 0.08, TAIL + 0.03, 14)
    s.tube(0, zc, 0.056, TAIL - 0.086, TAIL - 0.078, 14, 2)
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
    """이음매, 리벳, 띠, 표기를 아틀라스 좌표(m)에 놓고 렌더한다."""
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
        half = 0.27 if name in ("top", "bottom") else 0.21
        # 판 이음매와 리벳
        for y in SEAMS:
            u = strip_u(name, y)
            rect(0.004, half * 2, u, vc, LINE)
            k = int(half / RIVET_STEP)
            for i in range(-k, k + 1):
                rect(0.007, 0.007, u + 0.018, vc + i * RIVET_STEP, LINE)
                rect(0.007, 0.007, u - 0.018, vc + i * RIVET_STEP, LINE)
        # 탄두 표시 띠
        a, b = strip_u(name, BAND[0]), strip_u(name, BAND[1])
        rect(abs(a - b), half * 2, (a + b) * 0.5, vc, YELLOW)

    for name in ("right", "left"):
        vc = STRIPS[name][1]
        # 이름 표기
        u = strip_u(name, 0.19)
        text("SCM-7  STAND-OFF MUNITION", u, vc + 0.022, 0.036, INK)
        text("LOT 07-24   SER 20418   32 BOMBLETS", u, vc - 0.024, 0.02, INK)
        # 실탄 표시
        u = strip_u(name, 0.75)
        rect(0.17, 0.03, u, vc + 0.016, YELLOW)
        text("LIVE", u, vc + 0.016, 0.022, INK)
        text("ORDNANCE", u, vc - 0.019, 0.017, INK)
        # 인양 지점
        u = strip_u(name, -0.74)
        poly([(-0.022, -0.02), (0.022, -0.02), (0.0, 0.02)], u - 0.165, vc, RED)
        text("HOIST POINT", u + 0.02, vc + 0.017, 0.028, INK)
        text("DO NOT LIFT BY WINGS OR FINS", u + 0.02, vc - 0.02, 0.0165, RED)
        # 연료
        u = strip_u(name, -1.42)
        text("TURBOJET FUEL JP-10", u, vc + 0.015, 0.027, INK)
        text("KEEP CLEAR OF EXHAUST", u, vc - 0.02, 0.0175, RED)

    # 배: 흡입구 위험 표시, 진행 방향
    vc = STRIPS["bottom"][1]
    u = strip_u("bottom", -0.20)
    rect(0.36, 0.15, u, vc, RED)
    rect(0.345, 0.135, u, vc, PAINT)
    text("DANGER", u, vc + 0.03, 0.04, RED)
    text("JET INTAKE", u, vc - 0.024, 0.03, RED)
    u = strip_u("bottom", 0.27)
    poly([(-0.09, 0.0), (-0.03, 0.04), (-0.03, 0.016), (0.03, 0.016), (0.03, -0.016), (-0.03, -0.016), (-0.03, -0.04)], u - 0.05, vc, INK)
    text("FWD", u + 0.07, vc, 0.04, INK)

    # 날개
    for u0 in WING_U:
        u = u0 + 0.9
        rect(0.46, 0.003, u, WING_V + 0.03, INK)
        rect(0.46, 0.003, u, WING_V - 0.03, INK)
        text("NO STEP", u, WING_V, 0.038, INK, 1.2)

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
    img *= 1 + ((noise(n, 36, 7) - 0.5) * 0.1)[:TEX_H, :, None] + ((noise(n, 160, 9) - 0.5) * 0.04)[:TEX_H, :, None]
    # 어두운 면
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
    path = os.path.join(OUT, "ACO_SCM_Albedo.png")
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return path


def write_fbx(path):
    bpy.ops.export_scene.fbx(filepath=path, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def export_flight():
    """파일런 좌표 → 탄체 좌표. 파일런이 메시를 돌리는 것과 같은 회전이라 글자가 뒤집히지 않는다."""
    me = build(True)
    yc = FLIGHT_CENTER_Y
    # Blender 축: 파일런 (-lx, -lz, ly) → 탄체 (lx, yc - ly, lz - ZM)
    me.transform(Matrix(((-1, 0, 0, 0), (0, 0, -1, yc), (0, -1, 0, -ZM), (0, 0, 0, 1))))
    me.name = FLIGHT
    me.update()
    obj = bpy.data.objects.new(FLIGHT, me)
    scene = bpy.context.scene
    for o in list(scene.objects):
        o.hide_set(False)
        scene.collection.objects.unlink(o)
    scene.collection.objects.link(obj)
    write_fbx(os.path.join(OUT, FLIGHT + ".fbx"))
    return obj


def main_run():
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = make_texture()
    me = build()
    obj = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(obj)
    print("TRIS", sum(len(p.vertices) - 2 for p in me.polygons))
    write_fbx(os.path.join(OUT, NAME + ".fbx"))
    return obj, tex


if __name__ == "__main__":
    main_run()
    export_flight()
    print("DONE")
