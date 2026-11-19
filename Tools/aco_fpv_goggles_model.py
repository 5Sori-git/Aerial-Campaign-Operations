# -*- coding: utf-8 -*-
"""CIC 1인칭 고글(6-275) — 군용 휴대 관측 장비(열상 · 레이저 거리측정 쌍안경 계열 — 상자형 하우징에 앞 광학부 · 뒤 접안부), 머리끈 없음.
 2026-11-18 사용자 "군용관측장비 같은느낌으로 머리띠없는디자인" → "너무 동글동글, 실사장비 참고해서 그럴듯하게":
 둥근 덩어리 대신 모서리를 깎은(챔퍼) 딱딱한 하우징과 부품 덩어리로 만든다. 텍스처 없이 재질 5개(색만), 약 4천 삼각형.
 - 하우징: 국방색(16 × 11.6 × 7.5cm), 모서리 챔퍼, 위가 살짝 좁다. 양옆 고무 손잡이(가로 홈), 오른쪽 뒤 배터리 캡(널링), 밑면 삼각대 소켓.
 - 앞(블렌더 -Y = 유니티 +Z): 한 단 작은 광학부 판 — 왼쪽 큰 주 대물렌즈(깊은 후드 안에 테 + 유리), 오른쪽 위 주간 카메라 렌즈, 오른쪽 아래 레이저 창, 모서리 나사 넷.
 - 뒤(블렌더 +Y = 유니티 -Z, 눈 쪽): 접안 판 + 널링 시도 조절 링 + 접안 경통 + 속 빈 고무 눈컵(안에 유리) 둘, 나사 둘.
 - 위: 앞쪽 짧은 액세서리 레일(가로 홈), 오른쪽 뒤 고무 버튼 판(버튼 셋 + 큰 버튼 하나), 왼쪽 뒤 널링 전원 스위치(표시 막대) + 초록 표시등.
 - 원점 = 하우징 가운데, 밑면 z = -0.0378(책상에 평평하게).
 - 재질 칸 순서(유니티 서브메시 순서 — 면을 이 순서로 정렬해 내보낸다): Body · Front(유리) · Foam(고무) · Accent(표시등) · Metal(검은 아노다이징).
실행: blender --background --factory-startup --python Tools/aco_fpv_goggles_model.py -- [미리보기 경로접두]"""
import math
import os
import sys
import bpy
import bmesh
from mathutils import Vector, Matrix

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "CicGoggles")
NAME = "ACO_FpvGoggles"
MATS = [("Body", (0.19, 0.21, 0.14, 1)), ("Front", (0.03, 0.04, 0.06, 1)), ("Foam", (0.035, 0.035, 0.035, 1)),
        ("Accent", (0.12, 0.62, 0.30, 1)), ("Metal", (0.10, 0.10, 0.11, 1))]

HW, HL, HH = 0.08, 0.058, 0.0375     # 하우징 반폭 · 반길이 · 반높이
TOP = HH
FRONT = -HL - 0.013                  # 앞 광학부 판 앞면 y
BACK = HL + 0.007                    # 뒤 접안 판 뒷면 y


def mat_index(name):
    return [m[0] for m in MATS].index(name)


def ensure_mats():
    for m in MATS:
        if (NAME + "_" + m[0]) not in bpy.data.materials:
            mat = bpy.data.materials.new(NAME + "_" + m[0])
            mat.diffuse_color = m[1]


def finish(name, bm, mat):
    for f in bm.faces:
        f.material_index = mat_index(mat)
    me = bpy.data.meshes.new(name)
    for m in MATS:
        me.materials.append(bpy.data.materials[NAME + "_" + m[0]])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def box(name, size, center, bevel, mat, taper_top=1.0):
    """챔퍼 상자(모서리를 한 번 깎는다 — 딱딱한 장비 느낌, 평평한 음영)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
        if v.co.z > 0:
            v.co.x *= taper_top
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, profile=0.5, affect='EDGES')
    for v in bm.verts:
        v.co += Vector(center)
    for f in bm.faces:
        f.smooth = False
    return finish(name, bm, mat)


def place(bm, a, b):
    """로컬 z축(0 → 길이)으로 만든 것을 a → b 축으로 옮긴다."""
    a, b = Vector(a), Vector(b)
    rot = (b - a).normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation(a) @ rot, verts=bm.verts)


def profile_solid(name, a, b, radii, mat, flare=1.0):
    """반지름 목록(한 바퀴)으로 만든 막힌 기둥 — 널링(톱니) · 원기둥. flare = 끝쪽 반지름 배율."""
    L = (Vector(b) - Vector(a)).length
    n = len(radii)
    bm = bmesh.new()
    r0 = [bm.verts.new((r * math.cos(k / n * math.tau), r * math.sin(k / n * math.tau), 0)) for k, r in enumerate(radii)]
    r1 = [bm.verts.new((r * flare * math.cos(k / n * math.tau), r * flare * math.sin(k / n * math.tau), L)) for k, r in enumerate(radii)]
    for k in range(n):
        f = bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
        f.smooth = len(set(radii)) == 1
    bm.faces.new(list(reversed(r0))).smooth = False
    bm.faces.new(r1).smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    place(bm, a, b)
    return finish(name, bm, mat)


def cyl(name, a, b, r, mat, segs=32):
    return profile_solid(name, a, b, [r] * segs, mat)


def knurl(name, a, b, r, teeth, depth, mat):
    return profile_solid(name, a, b, [r if k % 2 == 0 else r - depth for k in range(teeth * 2)], mat)


def tube(name, a, b, r_out, r_in, mat, segs=32, r_out_b=None):
    """속 빈 관(후드 · 눈컵): 바깥 r_out(→ r_out_b로 벌어짐), 안 r_in. 양 끝 고리 면."""
    L = (Vector(b) - Vector(a)).length
    rb = r_out if r_out_b is None else r_out_b
    bm = bmesh.new()
    rings = []
    for (z, r) in ((0, r_out), (L, rb), (L, r_in), (0, r_in)):
        rings.append([bm.verts.new((r * math.cos(k / segs * math.tau), r * math.sin(k / segs * math.tau), z)) for k in range(segs)])
    for i in range(4):
        r0, r1 = rings[i], rings[(i + 1) % 4]
        for k in range(segs):
            f = bm.faces.new((r0[k], r0[(k + 1) % segs], r1[(k + 1) % segs], r1[k]))
            f.smooth = i in (0, 2)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    place(bm, a, b)
    return finish(name, bm, mat)


def lens(parts, x, z, y_face, depth, r_out, r_in, rim=True):
    """앞 렌즈: 후드(관) 안 깊이 depth에 테(검은 금속 고리) + 유리."""
    parts.append(tube("Hood", (x, y_face + 0.002, z), (x, y_face - depth, z), r_out, r_in, "Metal"))
    gy = y_face - 0.003
    if rim:
        parts.append(tube("LensRim", (x, gy + 0.001, z), (x, gy - 0.003, z), r_in, r_in * 0.82, "Metal"))
        parts.append(cyl("Glass", (x, gy + 0.001, z), (x, gy - 0.0015, z), r_in * 0.83, "Front"))
    else:
        parts.append(cyl("Glass", (x, gy + 0.001, z), (x, gy - 0.001, z), r_in, "Front"))


def make():
    ensure_mats()
    parts = []
    # ── 하우징 ──
    parts.append(box("Housing", (HW * 2, HL * 2, HH * 2), (0, 0, 0), 0.007, "Body", taper_top=0.95))
    # 앞 광학부 판(한 단 작다 — 이음매가 보인다) · 뒤 접안 판
    parts.append(box("FrontBlock", (0.148, 0.015, 0.068), (0, -HL - 0.0055, 0), 0.004, "Body"))
    parts.append(box("RearPlate", (0.13, 0.009, 0.058), (0, HL + 0.0025, 0.002), 0.003, "Body"))
    # ── 앞: 주 대물렌즈(왼쪽, 깊은 후드) · 주간 카메라(오른쪽 위) · 레이저 창(오른쪽 아래) ──
    lens(parts, -0.036, 0.0, FRONT, 0.017, 0.030, 0.0255)
    lens(parts, 0.035, 0.014, FRONT, 0.008, 0.0155, 0.0115)
    lens(parts, 0.035, -0.017, FRONT, 0.005, 0.0105, 0.0078, rim=False)
    for sx in (-1, 1):
        for sz in (-1, 1):
            parts.append(cyl("Screw", (sx * 0.066, FRONT + 0.0005, sz * 0.028), (sx * 0.066, FRONT - 0.0008, sz * 0.028), 0.0025, "Metal", 12))
    # ── 뒤: 시도 조절 링(널링) · 접안 경통 · 고무 눈컵(속 빈 관, 벌어짐) · 접안 유리 ──
    for sx in (-1, 1):
        x, z = sx * 0.031, 0.004
        parts.append(knurl("Diopter", (x, BACK - 0.001, z), (x, BACK + 0.009, z), 0.0195, 28, 0.0012, "Metal"))
        parts.append(cyl("Ocular", (x, BACK + 0.008, z), (x, BACK + 0.014, z), 0.0168, "Metal"))
        parts.append(tube("Eyecup", (x, BACK + 0.012, z), (x, BACK + 0.034, z), 0.019, 0.0135, "Foam", 32, 0.0225))
        parts.append(cyl("EyeGlass", (x, BACK + 0.0135, z), (x, BACK + 0.0145, z), 0.0136, "Front"))
    for sx in (-1, 1):
        parts.append(cyl("Screw", (sx * 0.058, BACK - 0.0005, -0.021), (sx * 0.058, BACK + 0.0008, -0.021), 0.0022, "Metal", 12))
    # ── 위: 액세서리 레일(앞쪽 가운데) ──
    parts.append(box("RailBase", (0.016, 0.052, 0.004), (0, -0.024, TOP + 0.0015), 0.0008, "Metal"))
    for k in range(5):
        parts.append(box("RailTooth", (0.024, 0.0055, 0.0034), (0, -0.044 + k * 0.01, TOP + 0.0045), 0.0006, "Metal"))
    # 오른쪽 뒤 고무 버튼 판 + 버튼(작은 셋, 큰 하나)
    parts.append(box("ButtonPad", (0.044, 0.034, 0.003), (0.036, 0.031, TOP + 0.0008), 0.001, "Foam"))
    for bx in (0.023, 0.036, 0.049):
        parts.append(cyl("Button", (bx, 0.022, TOP + 0.001), (bx, 0.022, TOP + 0.0045), 0.0042, "Metal", 16))
    parts.append(cyl("FireButton", (0.036, 0.040, TOP + 0.001), (0.036, 0.040, TOP + 0.0055), 0.0068, "Metal", 20))
    # 왼쪽 뒤 널링 전원 스위치 + 표시 막대 + 초록 표시등
    parts.append(knurl("PowerKnob", (-0.040, 0.034, TOP - 0.001), (-0.040, 0.034, TOP + 0.008), 0.0095, 14, 0.001, "Metal"))
    parts.append(box("KnobMark", (0.002, 0.009, 0.0012), (-0.040, 0.0375, TOP + 0.0084), 0, "Foam"))
    parts.append(cyl("Led", (-0.040, 0.016, TOP - 0.001), (-0.040, 0.016, TOP + 0.0012), 0.0022, "Accent", 12))
    # ── 옆: 고무 손잡이(가로 홈) · 오른쪽 뒤 배터리 캡 ──
    for sx in (-1, 1):
        parts.append(box("Grip", (0.004, 0.068, 0.046), (sx * (HW + 0.0003), -0.012, -0.004), 0.0012, "Foam"))
        for k in range(5):
            parts.append(box("Rib", (0.0024, 0.064, 0.0034), (sx * (HW + 0.0023), -0.012, -0.020 + k * 0.008), 0.0007, "Foam"))
    parts.append(knurl("BatteryCap", (HW - 0.002, 0.038, -0.004), (HW + 0.0065, 0.038, -0.004), 0.0125, 22, 0.001, "Metal"))
    # ── 밑: 삼각대 소켓 ──
    parts.append(cyl("TripodBoss", (0, 0.005, -HH + 0.0005), (0, 0.005, -HH - 0.0003), 0.011, "Metal", 24))

    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = NAME
    o.data.name = NAME
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(o.data)
    bm.free()
    # FBX는 재질을 면에 처음 나온 순서로 쓴다 → 면을 재질 칸 순서로 정렬해야 유니티 서브메시 순서 = MATS 순서
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.sort_elements(type='MATERIAL', elements={'FACE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    seen = []
    for p in o.data.polygons:
        if p.material_index not in seen:
            seen.append(p.material_index)
    print("MAT ORDER", seen)
    return o


# ───────────── 텍스처(색 + 마모 + 표기) 굽기 ─────────────
# 메모리 규칙 "표기는 텍스처에": UV를 펴고 글자 · 라벨을 텍스처에 직접 굽는다(얇은 데칼 판 없음).
# 방법: 모델 복사본(절차 셰이더 — 기본색 · 모서리 칠 벗겨짐 · 틈 때 · 얼룩)과 표면 0.6mm 위 글자 · 라벨 메시를
# Cycles "선택 → 활성" EMIT 굽기로 원본 UV 텍스처(2048²)에 옮긴다. 색은 sRGB로 적고 선형으로 바꿔 넣는다(유니티 sRGB 텍스처와 같은 밝기).
TEX_DIR = os.path.join(ROOT, "Assets", "ACO_Addon", "Textures", "CicGoggles")
TEX_NAME = NAME + "_Albedo"
TEX_SIZE = 2048
LIFT = 0.0006

# 재질별 (기본색, 벗겨진 색, 벗겨짐 세기, 틈 때 세기, 얼룩 세기) — sRGB
LOOK = {
    "Body": ((0.19, 0.21, 0.14), (0.40, 0.38, 0.31), 0.9, 0.55, 0.10),
    "Front": ((0.03, 0.04, 0.06), (0.03, 0.04, 0.06), 0.0, 0.0, 0.0),
    "Foam": ((0.035, 0.035, 0.035), (0.10, 0.10, 0.095), 0.6, 0.4, 0.15),
    "Accent": ((0.12, 0.62, 0.30), (0.12, 0.62, 0.30), 0.0, 0.0, 0.0),
    "Metal": ((0.10, 0.10, 0.11), (0.40, 0.40, 0.41), 1.0, 0.5, 0.06),
}
WHITE = (0.82, 0.82, 0.76)
BLACK = (0.03, 0.03, 0.03)
YELLOW = (0.88, 0.72, 0.10)
PLATE = (0.46, 0.46, 0.43)


def lin(c):
    return tuple(v ** 2.2 for v in c) + (1.0,)


def sock(node, name, kind):
    return [s for s in node.inputs if s.name == name and s.type == kind][0]


def mix(nt, fac, a, b, blend='MIX'):
    m = nt.nodes.new('ShaderNodeMix')
    m.data_type = 'RGBA'
    m.blend_type = blend
    for (nm, v) in (("A", a), ("B", b)):
        s = sock(m, nm, 'RGBA')
        if isinstance(v, tuple):
            s.default_value = v
        else:
            nt.links.new(v, s)
    f = sock(m, "Factor", 'VALUE')
    if isinstance(fac, float):
        f.default_value = fac
    else:
        nt.links.new(fac, f)
    return [o for o in m.outputs if o.type == 'RGBA'][0]


def math_node(nt, op, a, b):
    n = nt.nodes.new('ShaderNodeMath')
    n.operation = op
    n.use_clamp = True
    for i, v in enumerate((a, b)):
        if isinstance(v, float):
            n.inputs[i].default_value = v
        else:
            nt.links.new(v, n.inputs[i])
    return n.outputs[0]


def map_range(nt, v, f0, f1, t0=0.0, t1=1.0):
    n = nt.nodes.new('ShaderNodeMapRange')
    n.clamp = True
    n.inputs['From Min'].default_value = f0
    n.inputs['From Max'].default_value = f1
    n.inputs['To Min'].default_value = t0
    n.inputs['To Max'].default_value = t1
    nt.links.new(v, n.inputs['Value'])
    return n.outputs['Result']


def emission_mat(name, color_socket_or_tuple):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    if isinstance(color_socket_or_tuple, tuple):
        em.inputs['Color'].default_value = color_socket_or_tuple
    return m, nt, em


def look_mat(slot):
    base, worn, wear, grime, mottle = LOOK[slot]
    m, nt, em = emission_mat("Src_" + slot, None)
    tc = nt.nodes.new('ShaderNodeTexCoord')
    n1 = nt.nodes.new('ShaderNodeTexNoise')
    n1.inputs['Scale'].default_value = 160.0
    n1.inputs['Detail'].default_value = 6.0
    nt.links.new(tc.outputs['Object'], n1.inputs['Vector'])
    n2 = nt.nodes.new('ShaderNodeTexNoise')
    n2.inputs['Scale'].default_value = 28.0
    n2.inputs['Detail'].default_value = 3.0
    nt.links.new(tc.outputs['Object'], n2.inputs['Vector'])
    col = mix(nt, 0.0, lin(base), lin(base))
    if wear > 0:
        bev = nt.nodes.new('ShaderNodeBevel')
        bev.inputs['Radius'].default_value = 0.0012
        bev.samples = 8
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        dot = nt.nodes.new('ShaderNodeVectorMath')
        dot.operation = 'DOT_PRODUCT'
        nt.links.new(bev.outputs['Normal'], dot.inputs[0])
        nt.links.new(geo.outputs['Normal'], dot.inputs[1])
        edge = map_range(nt, [o for o in dot.outputs if o.name == 'Value'][0], 0.995, 0.93)
        chip = map_range(nt, n1.outputs['Fac'], 0.42, 0.62, 0.3, 1.0)
        wf = math_node(nt, 'MULTIPLY', math_node(nt, 'MULTIPLY', edge, chip), float(wear))
        col = mix(nt, wf, lin(base), lin(worn))
    if mottle > 0:
        g = map_range(nt, n2.outputs['Fac'], 0.3, 0.7, 1.0 - mottle, 1.0 + mottle)
        col = mix(nt, 1.0, col, g, 'MULTIPLY')
    if grime > 0:
        ao = nt.nodes.new('ShaderNodeAmbientOcclusion')
        ao.inputs['Distance'].default_value = 0.008
        ao.samples = 16
        gf = math_node(nt, 'MULTIPLY', math_node(nt, 'SUBTRACT', 1.0, ao.outputs['AO']), float(grime))
        dark = mix(nt, 1.0, col, (0.35, 0.35, 0.33, 1.0), 'MULTIPLY')
        col = mix(nt, gf, col, dark)
    nt.links.new(col, em.inputs['Color'])
    return m


def frame(pos, right, up):
    r, u = Vector(right).normalized(), Vector(up).normalized()
    n = r.cross(u)
    mtx = Matrix((r, u, n)).transposed().to_4x4()
    mtx.translation = Vector(pos) + n * LIFT
    return mtx, n


def text_label(body, pos, right, up, size, color, lift=0.0):
    cu = bpy.data.curves.new("txt", type='FONT')
    cu.body = body
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    ob = bpy.data.objects.new("Txt_" + body[:8], cu)
    bpy.context.scene.collection.objects.link(ob)
    mtx, n = frame(pos, right, up)
    mtx.translation += n * lift
    ob.matrix_world = mtx
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.convert(target='MESH')
    ob = bpy.context.view_layer.objects.active
    ob.data.materials.clear()
    ob.data.materials.append(emission_mat("Txt", lin(color))[0])
    return ob


def plate(w, h, pos, right, up, color):
    bm = bmesh.new()
    vs = [bm.verts.new((x * w / 2, y * h / 2, 0)) for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bm.faces.new(vs)
    me = bpy.data.meshes.new("plate")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Plate", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = frame(pos, right, up)[0]
    me.materials.append(emission_mat("Plate", lin(color))[0])
    return ob


def labels():
    """표기: 위치(블렌더 좌표, 겉면 위) · 글자 오른쪽 · 글자 위 방향. 위 · 뒤 글자는 접안부 쪽(조작하는 사람)에서 바로 읽힌다."""
    obs = []
    top_r, top_u = (-1, 0, 0), (0, -1, 0)      # 위: 뒤에서 보는 사람 기준
    # 위 앞 왼쪽(조작자 기준): 이름 스텐실
    obs.append(text_label("TOV-7", (0.041, -0.037, TOP), top_r, top_u, 0.012, WHITE))
    obs.append(text_label("OBSERVATION VIEWER", (0.041, -0.025, TOP), top_r, top_u, 0.0042, WHITE))
    obs.append(text_label("ACO NAVAL CIC", (0.041, -0.017, TOP), top_r, top_u, 0.0042, WHITE))
    # 위 앞 오른쪽: 금속 명판 + 검은 글자
    obs.append(plate(0.046, 0.027, (-0.041, -0.030, TOP), top_r, top_u, PLATE))
    for k, line in enumerate(("MODEL TOV-7", "SER NO 0412", "LOT 26-11")):
        obs.append(text_label(line, (-0.041, -0.0375 + k * 0.0075, TOP), top_r, top_u, 0.0040, BLACK, LIFT))
    # 전원 스위치 옆
    obs.append(text_label("PWR", (-0.057, 0.034, TOP), top_r, top_u, 0.0042, WHITE))
    # 앞: 레이저 창 오른쪽 노란 라벨
    fr, fu = (1, 0, 0), (0, 0, 1)
    obs.append(plate(0.019, 0.013, (0.0585, FRONT, -0.017), fr, fu, YELLOW))
    obs.append(text_label("LASER", (0.0585, FRONT, -0.0145), fr, fu, 0.0036, BLACK, LIFT))
    obs.append(text_label("APERTURE", (0.0585, FRONT, -0.0205), fr, fu, 0.0021, BLACK, LIFT))
    # 뒤: 눈컵 아래 시도 표시
    br, bu = (-1, 0, 0), (0, 0, 1)
    for sx in (-1, 1):
        obs.append(text_label("-  DIOPT  +", (sx * 0.031, BACK, -0.0217), br, bu, 0.0030, WHITE))
    # 오른쪽 옆(배터리 캡 위): 옆면은 위로 갈수록 안쪽(위 폭 95%)
    zt = 0.019
    xs = HW - 0.004 * (zt + HH) / (2 * HH)
    obs.append(text_label("BATT", (xs, 0.038, zt), (0, 1, 0), (0, 0, 1), 0.0045, WHITE))
    return obs


def scale_islands(o, factors):
    """UV 섬(UV가 이어진 면 묶음)마다 주된 재질의 배율로 섬 가운데 기준 크기를 바꾼다."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    uv = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()
    parent = list(range(len(bm.faces)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in bm.edges:
        lf = list(e.link_faces)
        if len(lf) != 2:
            continue
        a = {l.vert.index: l[uv].uv.copy() for l in lf[0].loops if l.vert in e.verts}
        b = {l.vert.index: l[uv].uv.copy() for l in lf[1].loops if l.vert in e.verts}
        if all((a[k] - b[k]).length < 1e-6 for k in a):
            parent[find(lf[0].index)] = find(lf[1].index)
    islands = {}
    for f in bm.faces:
        islands.setdefault(find(f.index), []).append(f)
    names = [m[0] for m in MATS]
    for faces in islands.values():
        count = {}
        for f in faces:
            count[f.material_index] = count.get(f.material_index, 0) + 1
        s = factors.get(names[max(count, key=count.get)], 1.0)
        loops = [l for f in faces for l in f.loops]
        c = sum((l[uv].uv for l in loops), Vector((0.0, 0.0))) / len(loops)
        for l in loops:
            l[uv].uv = c + (l[uv].uv - c) * s
    bm.to_mesh(o.data)
    bm.free()
    print("UV ISLANDS", len(islands))


def bake_texture(o):
    """o(내보낼 메시)에 UV를 펴고, 색 · 마모 · 표기를 텍스처(TEX_SIZE²)로 굽는다."""
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003)
    bpy.ops.object.mode_set(mode='OBJECT')
    # 글자가 있는 하우징(Body) 섬은 크게, 작은 금속 · 유리 부품 섬은 작게 → 다시 채워 넣기
    scale_islands(o, {"Body": 2.6, "Foam": 1.0, "Metal": 0.55, "Front": 0.6, "Accent": 0.3})
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    # 굽기 원본: 같은 모양 복사본(절차 재질) + 글자 · 라벨
    src = o.copy()
    src.data = o.data.copy()
    src.name = "BakeSource"
    bpy.context.scene.collection.objects.link(src)
    for i, m in enumerate(MATS):
        src.data.materials[i] = look_mat(m[0])
    sources = [src] + labels()
    # 원본(o)은 광선에 안 보이게(틈 때 계산이 겹친 면에 막히지 않게)
    for attr in ("visible_camera", "visible_diffuse", "visible_glossy", "visible_transmission", "visible_volume_scatter", "visible_shadow"):
        setattr(o, attr, False)
    img = bpy.data.images.new(TEX_NAME, TEX_SIZE, TEX_SIZE, alpha=False)
    for slot in o.data.materials:
        slot.use_nodes = True
        node = slot.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = img
        slot.node_tree.nodes.active = node
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 16
    sc.render.bake.margin = 6
    bpy.ops.object.select_all(action='DESELECT')
    for s in sources:
        s.select_set(True)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=0.004, margin=6, use_clear=True)
    os.makedirs(TEX_DIR, exist_ok=True)
    img.filepath_raw = os.path.join(TEX_DIR, TEX_NAME + ".png")
    img.file_format = 'PNG'
    img.save()
    for s in sources:
        bpy.data.objects.remove(s, do_unlink=True)
    for attr in ("visible_camera", "visible_diffuse", "visible_glossy", "visible_transmission", "visible_volume_scatter", "visible_shadow"):
        setattr(o, attr, True)
    print("TEXTURE", img.filepath_raw)


def write_fbx(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
                             path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def preview(path):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.3
    sc.display.shading.show_cavity = True
    sc.display.shading.show_specular_highlight = True
    sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.55, 0.58, 0.62)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    shots = {
        "front34": ((0.27, -0.33, 0.17), (0.0, -0.01, 0.0)),
        "back34": ((-0.27, 0.36, 0.18), (0.0, 0.01, 0.0)),
        "side": ((0.42, 0.0, 0.04), (0.0, 0.0, 0.0)),
    }
    for nm, (e, a) in shots.items():
        cam.location = Vector(e)
        cam.rotation_euler = (Vector(a) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = path + "_" + nm + ".png"
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    os.makedirs(OUT, exist_ok=True)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    o = make()
    bake_texture(o)
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    print("TRIS", sum(len(p.vertices) - 2 for p in o.data.polygons),
          "BOUNDS", tuple(round(min(v[k] for v in vs), 4) for k in range(3)), tuple(round(max(v[k] for v in vs), 4) for k in range(3)))
    if args:
        preview(args[0])
    write_fbx(o)
    print("DONE")
