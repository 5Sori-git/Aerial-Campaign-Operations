"""레이저 에이스 페이즈의 미사일 사일로와 탄도미사일(저폴리 — 멀리서 보는 물체).
 - Icbm_Pad: 콘크리트 받침(34 x 34m, 윗면 y 1.2) 가운데 10 x 10m 구멍, 구멍 둘레 노란 테두리, 구멍 아래로 깊이 23m 어두운 수직 갱도, 구석 환기탑 둘.
 - Icbm_DoorL / Icbm_DoorR: 구멍을 덮는 문 두 장(각 5 x 10m, 두께 0.6m). 원점 = 경첩(구멍 왼쪽·오른쪽 가장자리), 로컬 Z축(앞) 둘레로 젖혀 연다.
 - Icbm_Missile: 탄도미사일(지름 2.2m, 길이 20m). 원점 = 바닥, +Y = 위(기수). 흰 몸통에 검은 띠, 끝이 뾰족한 기수, 꼬리 날개 넷.
좌표: 유니티 좌표 그대로(x 오른쪽, y 위, z 앞). 단위 m. 텍스처는 색 칸 몇 개(색만 — 무늬 없음).
실행: blender --background --factory-startup --python Tools/aco_icbm_model.py   (ACO_ICBM_OUT으로 다른 폴더에)"""
import math
import os
import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_ICBM_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Icbm"))
NAME = "ACO_Icbm"
TEX = 256
# 색 칸(u, v 가운데 — 0..1), 칸 크기 1/4
PATCH = {
    "concrete": ((0.125, 0.125), (0.52, 0.52, 0.5)),
    "dark": ((0.375, 0.125), (0.07, 0.07, 0.075)),
    "yellow": ((0.625, 0.125), (0.85, 0.66, 0.1)),
    "white": ((0.875, 0.125), (0.9, 0.9, 0.88)),
    "black": ((0.125, 0.375), (0.08, 0.08, 0.09)),
    "metal": ((0.375, 0.375), (0.35, 0.37, 0.38)),
    "red": ((0.625, 0.375), (0.62, 0.1, 0.08)),
    "door": ((0.875, 0.375), (0.42, 0.44, 0.4)),
}

PAD = 17.0      # 받침 반폭
TOP = 1.2       # 받침 윗면 높이
HOLE = 5.0      # 구멍 반폭
DEPTH = 23.0    # 갱도 깊이(받침 윗면 아래)


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Mesh:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()

    def quad(self, pts, kind, want):
        """want = 면이 바라볼 방향(유니티 좌표) — 블렌더로 옮기면 거울이 되므로 감김 대신 방향으로 맞춘다."""
        vs = [self.bm.verts.new(to_blender(p)) for p in pts]
        f = self.bm.faces.new(vs)
        self.face(f, kind, want)
        return f

    def face(self, f, kind, want):
        f.normal_update()
        w = to_blender(want)
        if f.normal.x * w[0] + f.normal.y * w[1] + f.normal.z * w[2] < 0:
            f.normal_flip()
        (u, v), _ = PATCH[kind]
        for loop in f.loops:
            loop[self.uv].uv = (u, v)

    def box(self, c, s, kind):
        x0, x1 = c[0] - s[0] / 2, c[0] + s[0] / 2
        y0, y1 = c[1] - s[1] / 2, c[1] + s[1] / 2
        z0, z1 = c[2] - s[2] / 2, c[2] + s[2] / 2
        P = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        for idx, want in (((0, 3, 2, 1), (0, 0, -1)), ((4, 5, 6, 7), (0, 0, 1)), ((0, 1, 5, 4), (0, -1, 0)), ((2, 3, 7, 6), (0, 1, 0)),
                          ((1, 2, 6, 5), (1, 0, 0)), ((3, 0, 4, 7), (-1, 0, 0))):
            self.quad([P[i] for i in idx], kind, want)

    def lathe(self, profile, n, kinds, y0=0.0):
        """profile: [(r, y)] 아래 → 위. kinds: 칸마다 재질 이름(len(profile) - 1)."""
        rings = []
        for r, y in profile:
            rings.append([(r * math.cos(k * math.tau / n), y + y0, r * math.sin(k * math.tau / n)) for k in range(n)])
        for i in range(len(rings) - 1):
            for k in range(n):
                m = (k + 1) % n
                a, b, c, d = rings[i][k], rings[i][m], rings[i + 1][m], rings[i + 1][k]
                ang = (k + 0.5) * math.tau / n
                dr = profile[i + 1][0] - profile[i][0]
                dy = profile[i + 1][1] - profile[i][1]
                want = (math.cos(ang) * dy, -dr, math.sin(ang) * dy)   # 옆면 바깥(기울기 반영)
                if profile[i + 1][0] == 0:
                    self.tri(a, b, c, kinds[i], want)
                elif profile[i][0] == 0:
                    self.tri(a, c, d, kinds[i], want)
                else:
                    self.quad([a, d, c, b], kinds[i], want)

    def tri(self, a, b, c, kind, want):
        vs = [self.bm.verts.new(to_blender(p)) for p in (a, b, c)]
        f = self.bm.faces.new(vs)
        self.face(f, kind, want)

    def finish(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def build_pad():
    m = Mesh()
    t = TOP
    # 받침: 구멍 둘레 네 판(윗면 높이 t, 두께 t+1 — 땅에 1m 묻힌다)
    h = t + 1.0
    yc = t - h / 2
    w = PAD - HOLE
    m.box(((HOLE + PAD) / 2, yc, 0), (w, h, PAD * 2), "concrete")
    m.box((-(HOLE + PAD) / 2, yc, 0), (w, h, PAD * 2), "concrete")
    m.box((0, yc, (HOLE + PAD) / 2), (HOLE * 2, h, w), "concrete")
    m.box((0, yc, -(HOLE + PAD) / 2), (HOLE * 2, h, w), "concrete")
    # 구멍 둘레 노란 테두리(살짝 솟은 띠)
    for s in (1, -1):
        m.box((s * (HOLE + 0.6), t + 0.06, 0), (1.2, 0.12, HOLE * 2 + 2.4), "yellow")
        m.box((0, t + 0.06, s * (HOLE + 0.6)), (HOLE * 2, 0.12, 1.2), "yellow")
    # 갱도: 안쪽 벽 넷(안을 향한다)과 바닥
    y0, y1 = t - DEPTH, t
    H = HOLE
    m.quad([(-H, y0, -H), (H, y0, -H), (H, y1, -H), (-H, y1, -H)], "dark", (0, 0, 1))
    m.quad([(H, y0, H), (-H, y0, H), (-H, y1, H), (H, y1, H)], "dark", (0, 0, -1))
    m.quad([(-H, y0, H), (-H, y0, -H), (-H, y1, -H), (-H, y1, H)], "dark", (1, 0, 0))
    m.quad([(H, y0, -H), (H, y0, H), (H, y1, H), (H, y1, -H)], "dark", (-1, 0, 0))
    m.quad([(-H, y0, -H), (-H, y0, H), (H, y0, H), (H, y0, -H)], "dark", (0, 1, 0))
    # 구석 환기탑 둘, 문 경첩 받침
    for (x, z) in ((PAD - 3, PAD - 3), (-(PAD - 3), -(PAD - 3))):
        m.box((x, t + 1.5, z), (2.4, 3.0, 2.4), "metal")
        m.box((x, t + 3.15, z), (3.0, 0.3, 3.0), "metal")
    for s in (1, -1):
        m.box((s * (HOLE + 0.3), t + 0.4, 0), (0.6, 0.8, HOLE * 2), "metal")
    return m.finish("Icbm_Pad")


def build_door(side):
    """side -1 = 왼쪽(경첩 x -HOLE, 판은 +x로), +1 = 오른쪽. 원점 = 경첩."""
    m = Mesh()
    w = HOLE
    x0, x1 = (0.0, w) if side < 0 else (-w, 0.0)
    m.box(((x0 + x1) / 2, 0.3, 0), (w, 0.6, HOLE * 2), "door")
    # 문 위 빨간 경고 띠
    m.box(((x0 + x1) / 2, 0.62, 0), (w * 0.8, 0.04, 0.8), "red")
    return m.finish("Icbm_Door" + ("L" if side < 0 else "R"))


def build_missile():
    m = Mesh()
    n = 16
    r = 1.1
    prof = [(0.0, 0.0), (r * 0.9, 0.0), (r, 0.4), (r, 3.0), (r, 3.8), (r, 9.0), (r, 9.8), (r, 15.5), (r * 0.82, 17.4), (r * 0.45, 19.1), (0.0, 20.0)]
    kinds = ["black", "black", "white", "black", "white", "black", "white", "white", "white", "white"]
    m.lathe(prof, n, kinds)
    # 꼬리 날개 넷
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        cx, cz = math.cos(a), math.sin(a)
        px, pz = -math.sin(a), math.cos(a)
        th = 0.08
        root0, root1, tip0, tip1 = (r, 0.3), (r, 3.0), (r + 1.0, 0.0), (r + 1.0, 1.4)
        pts = [root0, tip0, tip1, root1]
        for s in (1, -1):
            quad = [(cx * rr + px * th * s, y, cz * rr + pz * th * s) for rr, y in pts]
            m.quad(quad, "black", (px * s, 0, pz * s))
    return m.finish("Icbm_Missile")


def make_texture():
    img = np.zeros((TEX, TEX, 4), dtype=np.float32)
    img[..., 3] = 1
    q = TEX // 4
    for (u, v), col in PATCH.values():
        x, y = int(u * TEX) - q // 2, int(v * TEX) - q // 2
        img[y:y + q, x:x + q, :3] = col
    im = bpy.data.images.new(NAME + "_Albedo", TEX, TEX, alpha=False)
    im.pixels.foreach_set(img.ravel())
    im.filepath_raw = os.path.join(OUT, NAME + "_Albedo.png")
    im.file_format = 'PNG'
    im.save()


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


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    make_texture()
    objs = [link(build_pad()), link(build_door(-1), (-HOLE, TOP, 0)), link(build_door(1), (HOLE, TOP, 0)), link(build_missile())]
    print("TRIS", sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons))
    write_fbx(os.path.join(OUT, NAME + ".fbx"), objs)
    print("DONE")
