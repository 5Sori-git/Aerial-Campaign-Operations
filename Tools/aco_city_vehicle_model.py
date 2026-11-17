# -*- coding: utf-8 -*-
"""도시 차량 모델(민간 승용차 · 밴 · 버스, 적 지휘 트럭 · 통신 차량) — 시가지 정밀 타격 페이즈(6-270).
실행: blender --background --factory-startup --python Tools/aco_city_vehicle_model.py [-- 미리보기폴더]
결과: Assets/ACO_Addon/Models/CityVehicle/ACO_CityVehicles.fbx, ACO_CityVehicles_Albedo.png
좌표는 Unity 축(+Z 앞, +Y 위, 단위 m)으로 적고 저장할 때 Blender 축으로 바꾼다. 원점 = 바닥 가운데.
멀리서 보는 작은 유닛이라 저폴리곤: 면마다 색 칸(256px 텍스처의 32px 칸) 하나를 UV로 가리킨다.
민간 차량은 같은 모양에 몸체 색만 다른 메시를 여러 개 둔다(배치 때 무작위로 고른다)."""
import math
import os
import sys
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "CityVehicle")
FBX = os.path.join(OUT, "ACO_CityVehicles.fbx")
ALBEDO = os.path.join(OUT, "ACO_CityVehicles_Albedo.png")

TEX = 256
CELL = 32
COLORS = {
    # 몸체 색(민간)
    "red": (0.55, 0.06, 0.05), "white": (0.80, 0.80, 0.78), "blue": (0.08, 0.18, 0.45), "silver": (0.50, 0.52, 0.54),
    "yellow": (0.80, 0.62, 0.08), "green": (0.10, 0.28, 0.16), "busblue": (0.10, 0.32, 0.62), "orange": (0.85, 0.36, 0.06),
    # 군용
    "olive": (0.26, 0.28, 0.17), "olivedark": (0.17, 0.18, 0.12), "khaki": (0.42, 0.40, 0.28),
    # 공통
    "glass": (0.06, 0.09, 0.12), "rubber": (0.04, 0.04, 0.045), "lamp": (0.95, 0.92, 0.78), "tail": (0.60, 0.04, 0.03),
    "black": (0.05, 0.05, 0.055), "grey": (0.30, 0.31, 0.32), "hub": (0.45, 0.46, 0.47), "trim": (0.14, 0.14, 0.15),
}
NAMES = list(COLORS)
BODY = "body"   # 몸체 자리 — 변형마다 다른 색으로 바꾼다


def to_blender(p):
    return (-p[0], -p[2], p[1])


def cell_uv(name):
    i = NAMES.index(name)
    cx = (i % 8) * CELL + CELL * 0.5
    cy = (i // 8) * CELL + CELL * 0.5
    h = CELL * 0.25
    # 텍스처는 위에서부터 칠하므로 v를 뒤집는다
    return [((cx - h) / TEX, 1 - (cy + h) / TEX), ((cx + h) / TEX, 1 - (cy + h) / TEX),
            ((cx + h) / TEX, 1 - (cy - h) / TEX), ((cx - h) / TEX, 1 - (cy - h) / TEX)]


class Part:
    """Unity 좌표로 쌓는다. 면마다 색 이름을 적어 두고 마지막에 색 칸 UV를 준다."""

    def __init__(self):
        self.bm = bmesh.new()
        self.faces = []   # (face, color)

    def _tag(self, faces, color):
        for f in faces:
            self.faces.append((f, color))

    def box(self, center, size, color):
        made = bmesh.ops.create_cube(self.bm, size=1.0, matrix=Matrix.Translation(center) @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
        faces = set()
        for v in made["verts"]:
            faces.update(v.link_faces)
        self._tag(faces, color)

    def prism(self, poly, x0, x1, edge_colors, cap_color):
        """(z, y) 다각형을 x0~x1로 뽑는다. edge_colors[i] = poly[i]→poly[i+1] 면 색, cap_color = 양 옆면."""
        a = [self.bm.verts.new((x0, y, z)) for z, y in poly]
        b = [self.bm.verts.new((x1, y, z)) for z, y in poly]
        n = len(poly)
        for i in range(n):
            j = (i + 1) % n
            f = self.bm.faces.new((a[i], a[j], b[j], b[i]))
            self._tag([f], edge_colors[i] if isinstance(edge_colors, (list, tuple)) else edge_colors)
        self._tag([self.bm.faces.new(a), self.bm.faces.new(list(reversed(b)))], cap_color)

    def wheel(self, x, y, z, r, w, n=8):
        """바퀴(축 = x). 옆면은 휠 색, 둘레는 고무."""
        rings = []
        for dx in (-w * 0.5, w * 0.5):
            rings.append([self.bm.verts.new((x + dx, y + math.cos(k * math.tau / n) * r, z + math.sin(k * math.tau / n) * r)) for k in range(n)])
        side = []
        for k in range(n):
            j = (k + 1) % n
            side.append(self.bm.faces.new((rings[0][k], rings[0][j], rings[1][j], rings[1][k])))
        self._tag(side, "rubber")
        self._tag([self.bm.faces.new(rings[0]), self.bm.faces.new(list(reversed(rings[1])))], "hub")

    def tube(self, a, b, r, color, n=6):
        a = Vector(a)
        b = Vector(b)
        axis = (b - a).normalized()
        s = axis.orthogonal().normalized()
        u = axis.cross(s).normalized()
        rings = [[self.bm.verts.new(c + s * math.cos(k * math.tau / n) * r + u * math.sin(k * math.tau / n) * r) for k in range(n)] for c in (a, b)]
        side = [self.bm.faces.new((rings[0][k], rings[0][(k + 1) % n], rings[1][(k + 1) % n], rings[1][k])) for k in range(n)]
        self._tag(side, color)
        self._tag([self.bm.faces.new(rings[0]), self.bm.faces.new(list(reversed(rings[1])))], color)

    def dish(self, center, normal, r, depth, color, n=10):
        """얕은 접시(원뿔) — 통신 차량 위성 안테나."""
        c = Vector(center)
        nrm = Vector(normal).normalized()
        s = nrm.orthogonal().normalized()
        u = nrm.cross(s).normalized()
        rim = [self.bm.verts.new(c + s * math.cos(k * math.tau / n) * r + u * math.sin(k * math.tau / n) * r) for k in range(n)]
        tip = self.bm.verts.new(c - nrm * depth)
        faces = [self.bm.faces.new((rim[k], rim[(k + 1) % n], tip)) for k in range(n)]
        faces.append(self.bm.faces.new(list(reversed(rim))))
        self._tag(faces, color)

    def build(self, name, body):
        bm = self.bm
        uv = bm.loops.layers.uv.verify()
        for f, color in self.faces:
            if not f.is_valid:
                continue
            c = body if color == BODY else color
            quad = cell_uv(c)
            for k, loop in enumerate(f.loops):
                loop[uv].uv = quad[k % 4]
        for v in bm.verts:
            v.co = to_blender(v.co)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        return me


# ───────────── 차종 ─────────────

def lights(p, front_z, rear_z, y, half_w, size=(0.32, 0.14, 0.04)):
    for sx in (-1, 1):
        p.box((sx * (half_w - size[0] * 0.5 - 0.06), y, front_z + size[2] * 0.5), size, "lamp")
        p.box((sx * (half_w - size[0] * 0.5 - 0.06), y, rear_z - size[2] * 0.5), size, "tail")


def sedan():
    """승용차 4.6 × 1.8 × 1.45m."""
    p = Part()
    w = 0.9
    lower = [(-2.30, 0.30), (2.30, 0.30), (2.32, 0.68), (2.00, 0.86), (-2.12, 0.90), (-2.32, 0.72)]
    p.prism(lower, -w, w, [BODY, BODY, BODY, BODY, BODY, BODY], BODY)
    cabin = [(-1.40, 0.88), (1.10, 0.88), (0.30, 1.42), (-1.05, 1.42)]
    p.prism(cabin, -w + 0.1, w - 0.1, ["trim", "glass", BODY, "glass"], "glass")
    p.box((0, 0.42, 2.31), (1.5, 0.18, 0.06), "trim")       # 앞 범퍼 띠
    lights(p, 2.30, -2.30, 0.62, w)
    for sx in (-1, 1):
        for z in (1.42, -1.42):
            p.wheel(sx * (w - 0.08), 0.33, z, 0.33, 0.22)
    return p


def van():
    """승합차 5.0 × 1.95 × 2.15m."""
    p = Part()
    w = 0.975
    body = [(-2.50, 0.35), (2.45, 0.35), (2.52, 1.05), (1.75, 2.15), (-2.50, 2.15)]
    p.prism(body, -w, w, [BODY, BODY, "glass", BODY, BODY], BODY)
    for sx in (-1, 1):
        p.box((sx * (w + 0.01), 1.62, 1.05), (0.02, 0.62, 1.15), "glass")   # 운전석 옆창
        p.box((sx * (w + 0.01), 1.62, -0.95), (0.02, 0.62, 2.3), "glass")   # 뒤 옆창
    p.box((0, 1.55, -2.51), (1.5, 0.7, 0.02), "glass")
    p.box((0, 0.45, 2.47), (1.7, 0.2, 0.06), "trim")
    lights(p, 2.48, -2.50, 0.85, w)
    for sx in (-1, 1):
        for z in (1.65, -1.6):
            p.wheel(sx * (w - 0.08), 0.36, z, 0.36, 0.24)
    return p


def bus():
    """버스 12 × 2.55 × 3.2m."""
    p = Part()
    w = 1.275
    body = [(-6.0, 0.40), (6.0, 0.40), (6.0, 3.15), (-6.0, 3.15)]
    p.prism(body, -w, w, [BODY, BODY, "white", BODY], BODY)
    for sx in (-1, 1):
        p.box((sx * (w + 0.01), 2.15, -0.3), (0.02, 1.0, 10.6), "glass")    # 옆 창 띠
        p.box((sx * (w + 0.01), 1.3, 5.2), (0.02, 1.6, 0.9), "glass")       # 앞문
    p.box((0, 1.9, 6.01), (2.3, 1.7, 0.02), "glass")                       # 앞 유리
    p.box((0, 2.3, -6.01), (2.0, 0.8, 0.02), "glass")
    p.box((0, 3.25, -1.5), (1.6, 0.2, 2.5), "grey")                        # 지붕 냉방기
    lights(p, 6.0, -6.0, 0.75, w, (0.35, 0.18, 0.04))
    for sx in (-1, 1):
        for z in (3.9, -3.7):
            p.wheel(sx * (w - 0.12), 0.50, z, 0.50, 0.32)
    return p


def command_truck():
    """적 지휘 트럭(6×6, 지휘 상자 + 휘는 안테나) 7.6 × 2.5 × 3.5m."""
    p = Part()
    w = 1.25
    cab = [(1.9, 0.75), (3.75, 0.75), (3.8, 1.55), (3.35, 2.65), (1.9, 2.65)]
    p.prism(cab, -w, w, ["olivedark", "olivedark", "glass", "olive", "olive"], "olive")
    for sx in (-1, 1):
        p.box((sx * (w + 0.01), 2.05, 2.75), (0.02, 0.6, 0.8), "glass")
    p.box((0, 0.62, 0.0), (2.2, 0.35, 7.4), "olivedark")                  # 차대
    p.box((0, 1.9, -1.35), (2.45, 2.2, 5.0), "olive")                      # 지휘 상자
    p.box((0, 3.05, -1.35), (1.2, 0.12, 2.0), "khaki")                     # 지붕 장비 덮개
    p.box((0, 0.75, 3.82), (2.3, 0.3, 0.12), "black")                      # 범퍼
    p.tube((0.9, 3.0, -3.4), (1.1, 6.2, -3.9), 0.035, "black")            # 휘는 안테나 둘
    p.tube((-0.9, 3.0, -3.4), (-1.1, 5.6, -3.7), 0.035, "black")
    p.tube((0.8, 2.65, 2.4), (0.8, 4.2, 2.2), 0.03, "black")
    lights(p, 3.78, -3.85, 1.15, w, (0.25, 0.14, 0.04))
    for sx in (-1, 1):
        for z in (2.7, -1.0, -2.55):
            p.wheel(sx * (w - 0.05), 0.55, z, 0.55, 0.36)
    return p


def comms_truck():
    """적 통신 차량(4×4, 장비 상자 + 마스트 + 위성 접시) 6.6 × 2.5 × 4.4m."""
    p = Part()
    w = 1.25
    cab = [(1.4, 0.75), (3.25, 0.75), (3.3, 1.55), (2.85, 2.55), (1.4, 2.55)]
    p.prism(cab, -w, w, ["olivedark", "olivedark", "glass", "olive", "olive"], "olive")
    for sx in (-1, 1):
        p.box((sx * (w + 0.01), 2.0, 2.25), (0.02, 0.55, 0.75), "glass")
    p.box((0, 0.62, -0.1), (2.2, 0.35, 6.4), "olivedark")
    p.box((0, 1.75, -1.6), (2.4, 1.9, 3.6), "khaki")                       # 장비 상자
    p.tube((0, 2.7, -2.9), (0, 4.6, -2.9), 0.09, "olivedark")             # 마스트
    p.box((0, 4.6, -2.9), (0.8, 0.12, 0.12), "black")
    p.dish((0, 3.25, -0.6), (0.0, 0.75, 0.66), 0.8, 0.28, "grey")          # 위성 접시(앞 위를 본다)
    p.tube((0, 2.7, -0.6), (0, 3.1, -0.6), 0.08, "black")
    p.box((0, 0.75, 3.32), (2.3, 0.3, 0.12), "black")
    lights(p, 3.28, -3.3, 1.15, w, (0.25, 0.14, 0.04))
    for sx in (-1, 1):
        for z in (2.2, -2.2):
            p.wheel(sx * (w - 0.05), 0.55, z, 0.55, 0.36)
    return p


VARIANTS = [
    ("CIV_Sedan_Red", sedan, "red"), ("CIV_Sedan_White", sedan, "white"), ("CIV_Sedan_Blue", sedan, "blue"), ("CIV_Sedan_Silver", sedan, "silver"),
    ("CIV_Van_White", van, "white"), ("CIV_Van_Yellow", van, "yellow"), ("CIV_Van_Green", van, "green"),
    ("CIV_Bus_Blue", bus, "busblue"), ("CIV_Bus_Orange", bus, "orange"),
    ("MIL_Command", command_truck, "olive"), ("MIL_Comms", comms_truck, "olive"),
]


# ───────────── 텍스처 · 내보내기 · 미리보기 ─────────────

def make_texture():
    img = np.zeros((TEX, TEX, 4), np.float32)
    img[..., 3] = 1.0
    for i, name in enumerate(NAMES):
        x0 = (i % 8) * CELL
        y0 = (i // 8) * CELL
        lin = COLORS[name]
        srgb = [12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055 for c in lin]
        img[y0:y0 + CELL, x0:x0 + CELL, :3] = srgb
    os.makedirs(OUT, exist_ok=True)
    tex = bpy.data.images.new("ACO_CityVehicles_Albedo", TEX, TEX, alpha=False)
    tex.colorspace_settings.name = 'sRGB'
    tex.pixels.foreach_set(img[::-1].ravel())
    tex.filepath_raw = ALBEDO
    tex.file_format = 'PNG'
    tex.save()
    return tex


def material(tex):
    mat = bpy.data.materials.new("ACO_CityVehicles")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = tex
    node.interpolation = 'Closest'
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def preview(objects, folder):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.render_aa = '16'
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.color = (0.55, 0.6, 0.65)
    scene.render.image_settings.file_format = 'PNG'
    os.makedirs(folder, exist_ok=True)
    ground = bpy.data.meshes.new("ground")
    ground.from_pydata([(-400, -400, 0), (400, -400, 0), (400, 400, 0), (-400, 400, 0)], [], [(0, 1, 2, 3)])
    gm = bpy.data.materials.new("g")
    gm.diffuse_color = (0.32, 0.33, 0.34, 1.0)
    ground.materials.append(gm)
    scene.collection.objects.link(bpy.data.objects.new("ground", ground))
    # 한 줄로 늘어놓는다(Unity x 방향, 가운데 0)
    widths = [14.0 if "Bus" in n else (9.0 if "MIL" in n else 6.5) for n, _, _ in VARIANTS]
    x = -sum(widths) * 0.5
    spot = {}
    for (name, _, _), width in zip(VARIANTS, widths):
        x += width * 0.5
        objects[name].location = Vector(to_blender((x, 0.0, 0.0)))
        spot[name] = x
        x += width * 0.5
    mid = 0.0
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 35
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shoot(name, eye, at, lens=35):
        cam_data.lens = lens
        e = Vector(to_blender(eye))
        a = Vector(to_blender(at))
        cam.location = e
        cam.rotation_euler = (a - e).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    shoot("cv_lineup_front", (0.0, 22.0, 62.0), (0.0, 0.0, 0.0), 30)
    shoot("cv_lineup_rear", (-6.0, 16.0, -55.0), (0.0, 0.0, 0.0), 32)
    sx = (spot["CIV_Sedan_Red"] + spot["CIV_Sedan_Silver"]) * 0.5
    shoot("cv_sedans", (sx + 6.0, 4.5, 11.0), (sx, 0.7, 0.0), 35)
    mx = (spot["MIL_Command"] + spot["MIL_Comms"]) * 0.5
    shoot("cv_military", (mx + 8.0, 5.0, 13.0), (mx, 1.8, 0.0), 35)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = make_texture()
    mat = material(tex)
    scene = bpy.context.scene
    objects = {}
    for name, make, body in VARIANTS:
        me = make().build(name, body)
        me.materials.append(mat)
        ob = bpy.data.objects.new(name, me)
        scene.collection.objects.link(ob)
        objects[name] = ob
        tris = sum(len(poly.vertices) - 2 for poly in me.polygons)
        box = [Vector(v) for v in ob.bound_box]
        print("MESH", name, tris, "size(unity x,y,z)", round(max(v[0] for v in box) - min(v[0] for v in box), 2),
              round(max(v[2] for v in box) - min(v[2] for v in box), 2), round(max(v[1] for v in box) - min(v[1] for v in box), 2))
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    if argv:
        preview(objects, argv[0])


main()
