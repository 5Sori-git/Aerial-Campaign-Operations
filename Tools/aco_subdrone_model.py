# -*- coding: utf-8 -*-
"""잠수함 사출 전투 무인기 모델(독자 디자인, 설계 6-217). 사출관에서 쏘아 올리는 소형 무인 전투기.
실행: blender --background --factory-startup --python Tools/aco_subdrone_model.py [-- 미리보기폴더]
결과: Assets/ACO_Addon/Models/SubDrone/ACO_SubDrone.fbx, ACO_SubDrone_Albedo.png (256 색 견본)
좌표는 Unity 축(+Z 기수, +Y 위, 단위 m), 원점은 기체 무게중심 근처. 멀리서 보는 유닛이라 저폴리(삼각형 1천 개 안팎).
FBX 오브젝트: SD_Body(동체 · 날개 · V꼬리 · 흡입구 · 노즐 — 한 덩어리)
모양: 길이 6.2m, 날개폭 4.4m. 납작한 동체(차인), 꺾인 델타 날개, 바깥으로 35° 기운 V꼬리, 등 위 흡입구, 기수 아래 센서 창."""
import math
import os
import sys
import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "SubDrone")
FBX = os.path.join(OUT, "ACO_SubDrone.fbx")
ALBEDO = os.path.join(OUT, "ACO_SubDrone_Albedo.png")

TEX_W, TEX_H, CELL = 256, 16, 16
COLORS = {
    "body": (0.40, 0.43, 0.46), "belly": (0.47, 0.50, 0.53), "dark": (0.07, 0.075, 0.08), "glass": (0.05, 0.08, 0.11),
    "nozzle": (0.16, 0.15, 0.14), "edge": (0.30, 0.32, 0.34), "red": (0.55, 0.12, 0.08), "white": (0.80, 0.80, 0.78),
}
SWATCH = {name: i for i, name in enumerate(COLORS)}

# 동체 단면 (z, 반폭, 반높이): 기수 → 꼬리
BODY = ((3.15, 0.02, 0.02), (2.9, 0.16, 0.12), (2.4, 0.33, 0.24), (1.6, 0.48, 0.32), (0.6, 0.55, 0.36), (-0.6, 0.55, 0.35),
        (-1.8, 0.46, 0.31), (-2.6, 0.34, 0.25), (-3.05, 0.27, 0.21))
RING = 12


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Part:
    def __init__(self):
        self.bm = bmesh.new()
        self.paint = self.bm.faces.layers.int.new("paint")
        self.smooth = self.bm.faces.layers.int.new("smooth")
        self.uv = self.bm.loops.layers.uv.verify()

    def tag(self, faces, paint, smooth):
        for f in faces:
            f[self.paint] = SWATCH[paint]
            f[self.smooth] = 1 if smooth else 0

    def loft(self, rings, paint, smooth, caps=True):
        last = first = None
        faces = []
        for ring in rings:
            verts = [self.bm.verts.new(p) for p in ring]
            if last is not None:
                n = len(verts)
                for i in range(n):
                    faces.append(self.bm.faces.new((last[i], last[(i + 1) % n], verts[(i + 1) % n], verts[i])))
            else:
                first = verts
            last = verts
        flat = []
        if caps:
            flat.append(self.bm.faces.new(list(reversed(first))))
            flat.append(self.bm.faces.new(last))
        self.tag(faces, paint, smooth)
        self.tag(flat, paint, False)
        piece = faces + flat
        for f in piece:
            f.normal_update()
        center = Vector()
        count = 0
        for f in piece:
            for v in f.verts:
                center += v.co
                count += 1
        center /= max(count, 1)
        vol = sum((f.calc_center_median() - center).dot(f.normal) * f.calc_area() for f in piece)
        if vol < 0.0:
            bmesh.ops.reverse_faces(self.bm, faces=piece)
        return faces

    def foil(self, root, tip, chord_dir, thick_dir, paint):
        """날개꼴 판. root · tip = ((앞전 x, y, z), 시위, 두께)."""
        cd = Vector(chord_dir).normalized()
        td = Vector(thick_dir).normalized()
        rings = []
        for le, chord, thick in (root, tip):
            le = Vector(le)
            upper = [le + cd * (f * chord) + td * (h * thick * 0.5) for f, h in FOIL[1:]]
            lower = [le + cd * (f * chord) - td * (h * thick * 0.5) for f, h in reversed(FOIL[1:])]
            rings.append([tuple(le)] + [tuple(v) for v in upper] + [tuple(v) for v in lower])
        self.loft(rings, paint, True)

    def tube(self, a, b, r0, r1, n, paint, smooth=True):
        a = Vector(a)
        b = Vector(b)
        axis = (b - a).normalized()
        side = axis.orthogonal().normalized()
        up = axis.cross(side).normalized()
        rings = []
        for c, r in ((a, r0), (b, r1)):
            rings.append([tuple(c + side * (math.cos(i * math.tau / n) * r) + up * (math.sin(i * math.tau / n) * r)) for i in range(n)])
        self.loft(rings, paint, smooth)

    def finish(self, name):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        bm.normal_update()
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(45.0):
                e.smooth = False
        for f in bm.faces:
            code = f[self.paint]
            uv = ((code * CELL + CELL * 0.5) / TEX_W, 0.5)
            for loop in f.loops:
                loop[self.uv].uv = uv
            f.smooth = f[self.smooth] == 1
        for v in bm.verts:
            v.co = to_blender(v.co)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)   # Unity → Blender 축 바꿈은 거울상
        bm.normal_update()
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        return me


FOIL = ((0.0, 0.0), (0.03, 0.55), (0.12, 0.85), (0.3, 1.0), (0.55, 0.85), (0.8, 0.45), (1.0, 0.1))


def body_ring(z, hw, hh):
    """납작한 동체 단면 — 옆구리에 차인(옆으로 뾰족)이 있는 렌즈꼴."""
    ring = []
    for i in range(RING):
        a = i * math.tau / RING
        c, s = math.cos(a), math.sin(a)
        w = hw * (abs(c) ** 0.7) * (1.0 if c >= 0 else -1.0)
        h = hh * s * (0.85 if s < 0 else 1.0)
        ring.append((w, h, z))
    return ring


def drone():
    p = Part()
    # 동체(위는 회색, 아래 밝은 회색은 같은 견본 — 단순하게)
    p.loft([body_ring(z, w, h) for z, w, h in BODY], "body", True)
    # 기수 아래 센서 창
    p.tube((0.0, -0.18, 2.35), (0.0, -0.18, 1.95), 0.13, 0.13, 8, "glass")
    # 등 위 흡입구(앞이 열린 납작한 상자 — 앞면 검게)
    p.loft([[(-0.26, 0.24, 1.0), (0.26, 0.24, 1.0), (0.26, 0.5, 1.0), (-0.26, 0.5, 1.0)],
            [(-0.24, 0.24, -0.9), (0.24, 0.24, -0.9), (0.2, 0.36, -0.9), (-0.2, 0.36, -0.9)]], "edge", False)
    p.loft([[(-0.2, 0.28, 1.01), (0.2, 0.28, 1.01), (0.2, 0.46, 1.01), (-0.2, 0.46, 1.01)],
            [(-0.2, 0.28, 0.96), (0.2, 0.28, 0.96), (0.2, 0.46, 0.96), (-0.2, 0.46, 0.96)]], "dark", False)
    # 노즐
    p.tube((0.0, 0.02, -2.95), (0.0, 0.02, -3.35), 0.22, 0.2, 10, "nozzle")
    p.tube((0.0, 0.02, -3.3), (0.0, 0.02, -3.36), 0.15, 0.15, 10, "dark", False)
    # 꺾인 델타 날개(양쪽): 안쪽 큰 델타 + 바깥 판
    for s in (1.0, -1.0):
        p.foil(((0.45 * s, 0.0, 1.2), 2.9, 0.16), ((1.35 * s, 0.0, -0.6), 1.25, 0.08), (0, 0, -1), (0, 1, 0), "body")
        p.foil(((1.33 * s, 0.0, -0.58), 1.27, 0.08), ((2.2 * s, 0.0, -1.35), 0.6, 0.04), (0, 0, -1), (0, 1, 0), "body")
        # 날개 끝 빨간 띠(얇은 판)
        p.foil(((2.18 * s, 0.0, -1.33), 0.62, 0.05), ((2.22 * s, 0.0, -1.37), 0.58, 0.05), (0, 0, -1), (0, 1, 0), "red")
        # V꼬리 — 바깥으로 35° 기운다
        cant = math.radians(35.0)
        up = (math.sin(cant) * s, math.cos(cant), 0.0)
        root = (0.28 * s, 0.22, -1.65)
        tip = (root[0] + up[0] * 1.15, root[1] + up[1] * 1.15, -2.45)
        p.foil((root, 1.25, 0.08), (tip, 0.55, 0.04), (0, 0, -1), (math.cos(cant), -math.sin(cant) * s, 0.0), "body")
    return p.finish("SD_Body")


def make_texture():
    img = np.zeros((TEX_H, TEX_W, 4), np.float32)
    img[..., 3] = 1.0
    for name, i in SWATCH.items():
        c = [v ** 2.2 for v in COLORS[name]]   # 선형 공간으로(블렌더 이미지 픽셀은 선형)
        img[:, i * CELL:(i + 1) * CELL, :3] = c
    tex = bpy.data.images.new("ACO_SubDrone_Albedo", TEX_W, TEX_H, alpha=False)
    tex.pixels.foreach_set(img.ravel())
    tex.filepath_raw = ALBEDO
    tex.file_format = 'PNG'
    tex.save()
    return tex


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    tex = make_texture()
    mat = bpy.data.materials.new("ACO_SubDrone")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = tex
    node.interpolation = 'Closest'
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    me = drone()
    me.materials.append(mat)
    ob = bpy.data.objects.new("SD_Body", me)
    bpy.context.scene.collection.objects.link(ob)
    print("TRIS", sum(len(f.vertices) - 2 for f in me.polygons))
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    if argv:
        preview(argv[0])


def preview(folder):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = 900
    scene.render.resolution_y = 600
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.72, 0.85, 1.0)
    scene.world = world
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = 3.5
    sun_ob = bpy.data.objects.new("sun", sun)
    sun_ob.rotation_euler = (math.radians(50), 0.0, math.radians(30))
    scene.collection.objects.link(sun_ob)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    os.makedirs(folder, exist_ok=True)

    def shoot(name, eye, at):
        e = Vector(to_blender(eye))
        cam.location = e
        cam.rotation_euler = (Vector(to_blender(at)) - e).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    shoot("sd_quarter", (7.0, 4.5, 8.0), (0.0, 0.0, 0.0))
    shoot("sd_top", (0.0, 14.0, 0.01), (0.0, 0.0, 0.0))
    shoot("sd_rear", (-4.0, 2.0, -9.0), (0.0, 0.3, 0.0))


main()
