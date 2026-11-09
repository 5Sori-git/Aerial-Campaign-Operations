"""MMP 마이크로미사일 탄체 모델. 맥주캔 같은 짧은 원통: 둥글게 말린 테두리, 어깨가 좁아지는 기수 쪽 목, 가운데 유리 시커 돔,
꼬리 쪽 오목한 바닥 안의 노즐, 꼬리에 펼쳐진 작은 X자 날개 4장. 지름 9.5cm, 길이 약 26cm.
실행: blender --background --factory-startup --python Tools/aco_mmp_missile.py   (ACO_MMP_OUT으로 다른 폴더에 만들 수 있다)
좌표: Unity 로컬 그대로. +z 기수, +y 위, 단위 m. 원점은 몸통 가운데, 노즐 끝이 z -0.13.
UV: 옆선 길이를 가로(꼬리 → 기수), 몸통 둘레를 세로(아래에서 시작 — 이음매가 아래)로 편다. 글자는 몸통 길이 방향으로 읽힌다. 표기는 텍스처에 직접 그리고 모두 가공이다."""
import math
import os
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_MMP_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "MMP"))
NAME = "ACO_MMP_Missile"
FONT = "C:/Windows/Fonts/arialbd.ttf"
TEX = 1024
ATLAS = 0.40        # 텍스처 한 변(m) — 1m = 2560픽셀
N = 24              # 둘레 칸 수

R = 0.0475          # 몸통 반지름
TAIL = -0.130
# 옆선(z, 반지름): 꼬리 테두리 → 몸통 → 어깨 → 목 → 위 테두리 → 오목한 뚜껑. 이 구간이 몸통 띠(텍스처)다
PROFILE = (
    (TAIL, 0.036),
    (TAIL, 0.0425),
    (-0.1265, 0.0470),
    (-0.1235, R),
    (0.078, R),
    (0.090, 0.0455),
    (0.099, 0.0405),
    (0.104, 0.0398),
    (0.1075, 0.0414),
    (0.1115, 0.0410),
    (0.1125, 0.0378),
    (0.1100, 0.0350),
    (0.1100, 0.0225),
)
# 꼬리 안: 오목한 바닥에서 노즐로(어두운 면)
NOZZLE = (
    (TAIL, 0.036),
    (-0.1235, 0.0330),
    (-0.1215, 0.0230),
    (-0.1290, 0.0185),
    (-0.1150, 0.0100),
    (-0.1100, 0.0),
)
DOME_R = 0.0225     # 시커 돔(뚜껑 가운데, z 0.110에서 솟는다)
DOME_Z = 0.110
STRIP_U = 0.02      # 몸통 띠 가로 시작(꼬리 테두리)
STRIP_V = 0.01      # 몸통 띠 세로 시작(둘레 아래)
PLAIN = (0.345, 0.06)
DARK = (0.355, 0.30)
GLASS = (0.355, 0.20)


def to_blender(p):
    return (-p[0], -p[2], p[1])


def ring(z, r, n=N):
    """둘레 점: 각도 0이 아래(-y), 오른쪽(+x)으로 돈다."""
    return [(r * math.sin(k * math.tau / n), -r * math.cos(k * math.tau / n), z) for k in range(n)]


class Solid:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.maps = []          # (면, {꼭짓점: (u, v)})
        self.flat = []          # (면, 도장면 (u, v))

    def lathe(self, profile, kind, smooth=True):
        """옆선을 돌린 면. kind 0 = 몸통 띠(둘레·옆선 길이로 UV), 1 = 도장면, 2 = 어두운 면, 3 = 유리."""
        rings = []
        for z, r in profile:
            if r <= 0.0:
                v = self.bm.verts.new(to_blender((0.0, 0.0, z)))
                rings.append([v] * (N + 1))
            else:
                vs = [self.bm.verts.new(to_blender(p)) for p in ring(z, r)]
                rings.append(vs + [vs[0]])
        length = [0.0]
        for (z0, r0), (z1, r1) in zip(profile, profile[1:]):
            length.append(length[-1] + math.hypot(z1 - z0, r1 - r0))
        circ = math.tau * R
        for i in range(len(rings) - 1):
            a, b = rings[i], rings[i + 1]
            for k in range(N):
                quad = []
                for v_ in (a[k], a[k + 1], b[k + 1], b[k]):
                    if v_ not in quad:
                        quad.append(v_)
                if len(quad) < 3:
                    continue
                f = self.bm.faces.new(quad)
                f.smooth = smooth
                if kind == 0:
                    table = {}
                    for ringi, col in ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k)):
                        table[rings[ringi][col]] = (STRIP_U + length[ringi], STRIP_V + circ * col / N)
                    self.maps.append((f, table))
                else:
                    self.flat.append((f, {1: PLAIN, 2: DARK, 3: GLASS}[kind]))

    def plate(self, pts, thick, kind=1):
        """평면 다각형 pts(Unity 좌표)를 법선 방향으로 두께를 준 판."""
        ax = [pts[1][i] - pts[0][i] for i in range(3)]
        bx = [pts[2][i] - pts[0][i] for i in range(3)]
        n = (ax[1] * bx[2] - ax[2] * bx[1], ax[2] * bx[0] - ax[0] * bx[2], ax[0] * bx[1] - ax[1] * bx[0])
        l = math.sqrt(sum(c * c for c in n))
        n = tuple(c / l * thick * 0.5 for c in n)
        top = [self.bm.verts.new(to_blender(tuple(p[i] + n[i] for i in range(3)))) for p in pts]
        bot = [self.bm.verts.new(to_blender(tuple(p[i] - n[i] for i in range(3)))) for p in pts]
        faces = [self.bm.faces.new(top), self.bm.faces.new(list(reversed(bot)))]
        m = len(pts)
        for k in range(m):
            j = (k + 1) % m
            faces.append(self.bm.faces.new((top[k], bot[k], bot[j], top[j])))
        for f in faces:
            self.flat.append((f, {1: PLAIN, 2: DARK}[kind]))

    def finish(self):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        for f, table in self.maps:
            for loop in f.loops:
                u, v = table[loop.vert]
                loop[self.uv].uv = (u / ATLAS, v / ATLAS)
        for f, (u, v) in self.flat:
            for loop in f.loops:
                c = loop.vert.co
                loop[self.uv].uv = ((u + (abs(c.x) + abs(c.z)) % 0.02) / ATLAS, (v + (abs(c.y) % 0.02)) / ATLAS)
        me = bpy.data.meshes.new(NAME)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def build():
    s = Solid()
    s.lathe(PROFILE, 0)
    s.lathe(NOZZLE, 2)
    # 시커 돔: 뚜껑 가운데의 낮은 유리 반구
    dome = [(DOME_Z + DOME_R * 0.75 * math.sin(math.radians(a)), DOME_R * math.cos(math.radians(a))) for a in (0, 20, 40, 60, 75, 90)]
    s.lathe(dome[:-1] + [(dome[-1][0], 0.0)], 3)
    # 날개 4장(X자): 꼬리 쪽 몸통에 붙어 바깥으로 펼쳐진 작은 사다리꼴, 접히는 경첩 막대
    for k in range(4):
        a = math.radians(45 + 90 * k)
        ox, oy = math.sin(a), -math.cos(a)

        def at(z, r):
            return (ox * r, oy * r, z)
        s.plate([at(-0.121, R - 0.002), at(-0.068, R - 0.002), at(-0.098, R + 0.030), at(-0.122, R + 0.030)], 0.0028)
        s.plate([at(-0.066, R - 0.001), at(-0.060, R - 0.001), at(-0.060, R + 0.004), at(-0.066, R + 0.004)], 0.008)
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


def profile_u(z):
    """몸통 띠에서 옆선 위치 z의 가로 좌표."""
    length = 0.0
    for (z0, r0), (z1, r1) in zip(PROFILE, PROFILE[1:]):
        seg = math.hypot(z1 - z0, r1 - r0)
        if z1 >= z and z1 > z0 and z0 <= z:
            return STRIP_U + length + seg * (z - z0) / (z1 - z0)
        length += seg
    return STRIP_U + length


def render_page():
    page = bpy.data.scenes.new("page")
    page.render.engine = 'BLENDER_WORKBENCH'
    page.display.shading.light = 'FLAT'
    page.display.shading.color_type = 'MATERIAL'
    page.display.shading.show_shadows = False
    page.display.render_aa = '32'
    page.render.film_transparent = True
    page.render.resolution_x = TEX
    page.render.resolution_y = TEX
    page.render.image_settings.file_format = 'PNG'
    page.render.image_settings.color_mode = 'RGBA'
    page.view_settings.view_transform = 'Standard'
    temp = os.path.join(OUT, "_page.png")
    page.render.filepath = temp
    cam = bpy.data.objects.new("pcam", bpy.data.cameras.new("pcam"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ATLAS
    cam.location = (ATLAS / 2, ATLAS / 2, 5)
    page.collection.objects.link(cam)
    page.camera = cam
    font = bpy.data.fonts.load(FONT)
    mats = {}
    depth = [0.0]

    def place(o, u, v, col, rot=0.0):
        depth[0] += 0.0002
        o.location = (u, v, depth[0])
        o.rotation_euler = (0.0, 0.0, rot)
        if col not in mats:
            m = bpy.data.materials.new("m")
            m.diffuse_color = tuple(c ** 2.2 for c in col) + (1,)
            mats[col] = m
        o.data.materials.append(mats[col])
        page.collection.objects.link(o)

    def text(body, u, v, size, col, spacing=1.06, rot=0.0):
        c = bpy.data.curves.new("t", 'FONT')
        c.body = body
        c.font = font
        c.size = size
        c.align_x = 'CENTER'
        c.align_y = 'CENTER'
        c.space_character = spacing
        place(bpy.data.objects.new("t", c), u, v, col, rot)

    def poly(points, u, v, col):
        bm = bmesh.new()
        bm.faces.new([bm.verts.new((x, y, 0)) for x, y in points])
        me = bpy.data.meshes.new("p")
        bm.to_mesh(me)
        bm.free()
        place(bpy.data.objects.new("p", me), u, v, col)

    def rect(w, h, u, v, col):
        poly([(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)], u, v, col)

    INK = (0.12, 0.13, 0.14)
    RED = (0.62, 0.1, 0.08)
    YELLOW = (0.85, 0.66, 0.12)
    BROWN = (0.42, 0.3, 0.18)
    circ = math.tau * R
    mid = STRIP_V + circ / 2
    # 둘레를 도는 띠: 기수 쪽 노란 띠(실탄), 꼬리 쪽 갈색 띠(추진제), 가는 검은 이음선 둘
    for z, w, col in ((0.064, 0.012, YELLOW), (-0.098, 0.009, BROWN), (0.040, 0.0016, INK), (-0.076, 0.0016, INK)):
        rect(w, circ + 0.004, profile_u(z), mid, col)
    # 표기 둘(양옆 — 이음매가 아래라 옆 가운데가 둘레의 1/4, 3/4). 어느 쪽에서 봐도 바로 읽히게 한쪽은 180도 돌린다(화살표는 늘 기수 쪽)
    for q, flip in ((0.25, 1.0), (0.75, -1.0)):
        v = STRIP_V + circ * q
        rot = 0.0 if flip > 0 else math.pi
        cu = profile_u(-0.020)
        text("MMP-M", cu, v + 0.008 * flip, 0.016, INK, 1.1, rot)
        text("MICRO MISSILE  HE-FRAG", cu, v - 0.0055 * flip, 0.0062, INK, 1.06, rot)
        text("LOT 11-26  SER 4417", cu, v - 0.0135 * flip, 0.0055, INK, 1.06, rot)
        text("DO NOT DROP", cu, v - 0.0210 * flip, 0.0050, RED, 1.06, rot)
        poly([(0.008, 0.0), (0.0, 0.005), (0.0, 0.0018), (-0.008, 0.0018), (-0.008, -0.0018), (0.0, -0.0018), (0.0, -0.005)],
             profile_u(0.029), v + 0.006 * flip, INK)
        text("FWD", profile_u(0.029), v - 0.006 * flip, 0.0055, INK, 1.06, rot)
    # 날개 경첩 옆 펴짐 표시(날개 넷 — 둘레 1/8마다)
    for q in (0.125, 0.375, 0.625, 0.875):
        v = STRIP_V + circ * q
        poly([(0.0, 0.003), (0.0, -0.003), (-0.005, 0.0)], profile_u(-0.056), v, RED)

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
    base = np.array([0.74, 0.75, 0.73], dtype=np.float32)
    img = np.tile(base[None, None, :], (TEX, TEX, 1))
    img *= 1 + ((noise(TEX, 24, 21) - 0.5) * 0.06)[:, :, None] + ((noise(TEX, 120, 23) - 0.5) * 0.03)[:, :, None]

    def fill(c, half, col):
        x0, x1 = int((c[0] - half) / ATLAS * TEX), int((c[0] + half) / ATLAS * TEX)
        y0, y1 = int((c[1] - half) / ATLAS * TEX), int((c[1] + half) / ATLAS * TEX)
        img[y0:y1, x0:x1] = col
    fill(DARK, 0.03, (0.05, 0.05, 0.055))
    fill(GLASS, 0.03, (0.10, 0.16, 0.22))
    fill(PLAIN, 0.03, (0.52, 0.54, 0.55))   # 날개·경첩은 조금 어두운 금속
    page = render_page()
    alpha = page[..., 3:4] * 0.95
    img = img * (1 - alpha) + page[..., :3] * alpha
    out = np.ones((TEX, TEX, 4), dtype=np.float32)
    out[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new(NAME + "_Albedo", TEX, TEX, alpha=False)
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
    print("DONE")
