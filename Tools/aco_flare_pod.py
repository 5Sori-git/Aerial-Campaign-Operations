"""FA-18E 대응탄 포드: CONS_CM 소모품의 외형. 엔진 나셀 아랫면에 붙이는 납작한 발사 상자 한 쌍.
플레어는 기존 방출 지점에서 나간다. 이 모델은 외형만 맡는다. 본체 도장색을 포드 자리에서 읽어 맞춘다.
실행: blender --background --factory-startup --python Tools/aco_flare_pod.py
좌표: 기수 -Y, 위 +Z, 단위 m. 항공기 원점 기준 FBX로 쓴다(Unity에서 +Z가 기수)."""
import math
import os
import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FA18 = os.path.join(ROOT, "Assets", "Pit-bool", "FA-18")
FBX = os.path.join(FA18, "FBX", "FA-18E.fbx")
BODY = os.path.join(FA18, "material", "E", "F-18.E_Base_color.png")
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "FlarePod")
NAME = "ACO_FlarePod_FA18E"

# 자리: 바퀴 수납부 뒤, 톱니 패널선 앞의 나셀 바닥
CENTER = Vector((0.72, 5.45, 0.0))
LENGTH = 0.9
WIDTH = 0.4
SHADE = 0.8        # 본체 도장색에 곱해 조금 어둡게
CELL = 0.09
BAR = 0.015
ROWS = 3
COLS = 6
FLOOR = 0.06       # 셀 바닥 높이
TOP = 0.095        # 격자 윗면 높이
TEX = 1024
PAINT_UV = 0.5     # 텍스처 왼쪽 절반 = 도장면, 오른쪽 위 = 플레어 끝, 오른쪽 아래 = 어두운 안쪽


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    main = bpy.data.objects["Main.E"]
    mw = main.matrix_world
    tree = BVHTree.FromPolygons([mw @ v.co for v in main.data.vertices], [list(p.vertices) for p in main.data.polygons])
    return main, tree


def surface(tree, x, y):
    hit = tree.ray_cast(Vector((x, y, -6.0)), Vector((0, 0, 1)), 12.0)
    return hit[0], hit[2]


def fit_plane(tree):
    """포드 자리 표면에 평면을 맞춘다. 나셀 바닥은 거의 평평하다."""
    pts, faces = [], []
    for i in range(-4, 5):
        for j in range(-4, 5):
            p, f = surface(tree, CENTER.x + WIDTH * 0.62 * j / 4, CENTER.y + LENGTH * 0.58 * i / 4)
            if p is not None:
                pts.append(p)
                faces.append(f)
    a = np.array([[p.x, p.y, 1.0] for p in pts])
    z = np.array([p.z for p in pts])
    (gx, gy, c), _, _, _ = np.linalg.lstsq(a, z, rcond=None)
    residual = float(np.abs(a @ np.array([gx, gy, c]) - z).max())
    normal = Vector((gx, gy, -1.0)).normalized()     # 아래쪽(바깥)
    print("PLANE", round(gx, 3), round(gy, 3), "residual", round(residual, 4), "samples", len(pts))
    return normal, Vector((CENTER.x, CENTER.y, gx * CENTER.x + gy * CENTER.y + c)), residual, faces


class Pod:
    """(u 뒤쪽, v 바깥쪽, h 표면에서 바깥) 좌표로 입체를 쌓는다."""

    def __init__(self, origin, eu, ev, en):
        self.o, self.eu, self.ev, self.en = origin, eu, ev, en
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.grid = self.bm.faces.layers.int.new("grid")   # 셀 격자: 윗면만 도장이고 안쪽 벽은 어둡다

    def p(self, u, v, h):
        return self.o + self.eu * u + self.ev * v + self.en * h

    def loft(self, rings, grid=False):
        vs = [[self.bm.verts.new(self.p(*q)) for q in r] for r in rings]
        n = len(rings[0])
        faces = []
        for a, b in zip(vs, vs[1:]):
            for k in range(n):
                m = (k + 1) % n
                faces.append(self.bm.faces.new((a[k], a[m], b[m], b[k])))
        faces.append(self.bm.faces.new(vs[0]))
        faces.append(self.bm.faces.new(list(reversed(vs[-1]))))
        for f in faces:
            f[self.grid] = 1 if grid else 0
        return faces

    def box(self, u0, u1, v0, v1, h0, h1, grid=False):
        q = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))
        return self.loft([[(u, v, h0) for u, v in q], [(u, v, h1) for u, v in q]], grid)

    def finish(self, name):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        for f in self.bm.faces:
            n = f.normal
            ax = (abs(n.dot(self.eu)), abs(n.dot(self.ev)), abs(n.dot(self.en)))
            for loop in f.loops:
                d = loop.vert.co - self.o
                u, v, h = d.dot(self.eu), d.dot(self.ev), d.dot(self.en)
                if f[self.grid] and ax[2] < 0.9:
                    loop[self.uv].uv = (0.76, 0.12)
                elif ax[2] >= ax[0] and ax[2] >= ax[1]:
                    loop[self.uv].uv = ((u + 0.6) * 0.38, (v + 0.3) * 0.38 + 0.1)
                elif ax[1] >= ax[0]:
                    loop[self.uv].uv = ((u + 0.6) * 0.38, h * 0.38 + 0.02)
                else:
                    loop[self.uv].uv = ((v + 0.3) * 0.38, h * 0.38 + 0.02)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def build(pod, sink):
    """앞이 낮은 경사로이고 뒤로 갈수록 셀 격자가 열린다. 톱니는 기수 쪽을 향한다."""
    w = WIDTH * 0.5
    flange = [(-0.46, -w - 0.04), (-0.53, -0.12), (-0.46, 0.0), (-0.53, 0.12), (-0.46, w + 0.04), (0.47, w + 0.04), (0.51, w), (0.51, -w), (0.47, -w - 0.04)]
    pod.loft([[(u, v, -sink) for u, v in flange], [(u, v, 0.012) for u, v in flange]])

    def section(half, h):
        c = min(0.016, h * 0.4)
        return [(-half, 0.0), (half, 0.0), (half, h - c), (half - c, h), (-half + c, h), (-half, h - c)]

    length = COLS * CELL + (COLS + 1) * BAR
    width = ROWS * CELL + (ROWS + 1) * BAR
    u0 = -0.25
    u1 = u0 + length
    half = width * 0.5
    ramp = [(-0.45, 0.14, 0.016), (-0.34, 0.165, 0.058), (u0, half, TOP)]
    pod.loft([[(u, v, h_) for v, h_ in section(hw, h)] for u, hw, h in ramp])
    pod.box(u0, u1, -half, half, 0.0, FLOOR)
    tail = [(u1, half, TOP), (0.45, 0.15, 0.04)]
    pod.loft([[(u, v, h_) for v, h_ in section(hw, h)] for u, hw, h in tail])
    # 셀 격자
    for r in range(ROWS + 1):
        v = -half + r * (CELL + BAR)
        pod.box(u0, u1, v, v + BAR, FLOOR, TOP, True)
    for c in range(COLS + 1):
        u = u0 + c * (CELL + BAR)
        for r in range(ROWS):
            v = -half + BAR + r * (CELL + BAR)
            pod.box(u, u + BAR, v, v + CELL, FLOOR, TOP, True)
    # 셀 바닥: 칸마다 플레어 끝 그림을 붙인다
    cells = []
    for c in range(COLS):
        for r in range(ROWS):
            a = u0 + BAR + c * (CELL + BAR)
            b = -half + BAR + r * (CELL + BAR)
            q = [pod.bm.verts.new(pod.p(a + x * CELL, b + y * CELL, FLOOR + 0.002)) for x, y in ((0, 0), (1, 0), (1, 1), (0, 1))]
            cells.append(pod.bm.faces.new(q))
    return cells


def floating(tree, pod, sink):
    """밑면 꼭짓점이 표면 밖으로 뜬 거리의 최댓값(m). 0 이하면 모두 묻혀 있다."""
    worst = -1.0
    for v in pod.bm.verts:
        if abs((v.co - pod.o).dot(pod.en) + sink) > 0.0001:
            continue
        hit = tree.ray_cast(Vector((v.co.x, v.co.y, -6.0)), Vector((0, 0, 1)), 12.0)
        if hit[0] is None:
            return 1.0
        worst = max(worst, hit[0].z - v.co.z)
    return worst


def make_side(tree, sign, normal, centre, sink):
    aft = Vector((0, 1, 0))
    eu = (aft - normal * aft.dot(normal)).normalized()
    ev = Vector((sign, 0, 0))
    ev = (ev - normal * ev.dot(normal) - eu * ev.dot(eu)).normalized()
    origin = Vector((centre.x * sign, centre.y, centre.z))
    pod = Pod(origin, eu, ev, normal)
    cells = build(pod, sink)
    print("FLOAT", sign, round(floating(tree, pod, sink), 4))
    pod.bm.faces.ensure_lookup_table()
    # 셀 바닥 UV는 finish 뒤에 덮어쓴다
    keep = [[v.co.copy() for v in f.verts] for f in cells]
    me = pod.finish("Side")
    uv = me.uv_layers.active.data
    tiles = ((0.52, 0.52), (0.98, 0.52), (0.98, 0.98), (0.52, 0.98))
    wanted = {tuple(round(c, 5) for c in q[0]) for q in keep}
    for poly in me.polygons:
        pts = [me.vertices[i].co for i in poly.vertices]
        if len(pts) != 4 or tuple(round(c, 5) for c in pts[0]) not in wanted:
            continue
        for k, li in enumerate(poly.loop_indices):
            uv[li].uv = tiles[k]
    return me


def paint_colour(main, faces):
    img = bpy.data.images.load(BODY)
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.empty(w * h * img.channels, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, img.channels)
    uv = main.data.uv_layers.active.data
    out = []
    for idx in set(faces):
        p = main.data.polygons[idx]
        c = Vector((0, 0))
        for li in p.loop_indices:
            c += uv[li].uv
        c /= p.loop_total
        out.append(px[int((c.y % 1.0) * (h - 1)), int((c.x % 1.0) * (w - 1)), :3])
    bpy.data.images.remove(img)
    return np.array(out).mean(axis=0)


def noise(size, cells, seed):
    rng = np.random.RandomState(seed)
    g = rng.rand(cells + 1, cells + 1)
    g[-1, :] = g[0, :]
    g[:, -1] = g[:, 0]
    t = (np.arange(size) + 0.5) / size * cells
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    a = g[i][:, i]
    b = g[i + 1][:, i]
    c = g[i][:, i + 1]
    d = g[i + 1][:, i + 1]
    fy = f[:, None]
    fx = f[None, :]
    return (a * (1 - fy) + b * fy) * (1 - fx) + (c * (1 - fy) + d * fy) * fx


def make_texture(paint):
    n = TEX
    base = paint * SHADE
    img = np.tile(base[None, None, :], (n, n, 1)).astype(np.float32)
    img *= 1 + (noise(n, 24, 3) - 0.5)[..., None] * 0.1 + (noise(n, 90, 5) - 0.5)[..., None] * 0.05
    half = n // 2
    # 어두운 안쪽
    img[:half, half:, :] = (0.035, 0.036, 0.04)
    # 플레어 끝: 금속 테 + 어두운 뚜껑
    yy, xx = np.mgrid[half:n, half:n]
    cx = cy = n * 0.75
    r = np.hypot(xx - cx, yy - cy) / (n * 0.25)
    tile = np.zeros((half, half, 3), dtype=np.float32) + (0.05, 0.05, 0.055)
    tile[r < 0.82] = (0.42, 0.37, 0.25)
    tile[r < 0.68] = (0.13, 0.13, 0.12)
    img[half:, half:, :] = tile
    out = np.ones((n, n, 4), dtype=np.float32)
    out[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new(NAME + "_Albedo", n, n, alpha=False)
    im.colorspace_settings.name = 'sRGB'
    im.pixels.foreach_set(out.ravel())
    path = os.path.join(OUT, "ACO_FlarePod_Albedo.png")
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return path


def main_run():
    os.makedirs(OUT, exist_ok=True)
    main, tree = setup()
    normal, centre, residual, faces = fit_plane(tree)
    sink = residual + 0.006
    paint = paint_colour(main, faces)
    print("PAINT", [round(float(c), 3) for c in paint])
    tex = make_texture(paint)
    bm = bmesh.new()
    for sign in (1, -1):
        mirrored = Vector((normal.x * sign, normal.y, normal.z))
        bm.from_mesh(make_side(tree, sign, mirrored, centre, sink))
    me = bpy.data.meshes.new(NAME)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(obj)
    print("TRIS", sum(len(p.vertices) - 2 for p in me.polygons), "sink", round(sink, 4))
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE',
                             add_leaf_bones=False, bake_anim=False)
    return main, tree, obj, tex


if __name__ == "__main__":
    main_run()
    print("DONE")
