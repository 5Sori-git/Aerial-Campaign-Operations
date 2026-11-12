"""FA-18E 장갑판 + 전용 텍스처. 본체 도장색을 판 자리에서 읽어 맞춘다. 원본 파일은 건드리지 않는다.
좌표: 기수 -Y, 위 +Z, 단위 m."""
import io
import math
import os
import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = r"C:\Users\KYJ\AppData\Local\VRChatProjects\ACO_Claude\Assets\Pit-bool\FA-18"
FBX = ROOT + r"\FBX\FA-18E.fbx"
BODY = ROOT + r"\material\E\F-18.E_Base_color.png"
BODY_M = ROOT + r"\material\E\F-18.E_Metallic.png"
OUT = "C:/Users/KYJ/AppData/Local/VRChatProjects/ACO_Claude/Assets/ACO_Addon/Models/Armor"
os.makedirs(OUT, exist_ok=True)

STEP = 0.1
HEIGHT = 0.024
RIM = 0.003
CHAMFER = 0.035
BOLT_INSET = 0.075
BOLT_STEP = 0.24
BOLT_RADIUS = 0.013
BOLT_HEIGHT = 0.006
TOOTH = 0.1
SHADE = 0.86     # 본체 도장색에 곱해 판을 조금 어둡게

# (이름, u0, u1, v0, v1, 톱니 수, 아틀라스 x(m), 아틀라스 y(m))
PLATES = [
    ("Cockpit", -4.44, -3.32, -0.76, -0.41, 1, 0.05, 0.85),
    ("Intake", 1.14, 4.0, -1.28, -0.58, 2, 0.05, 0.05),
]
ATLAS_W = 2048
ATLAS_H = 1024
PPM = 600.0        # 미터당 픽셀

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
main = bpy.data.objects["Main.E"]
scene = bpy.context.scene
mw = main.matrix_world
tree = BVHTree.FromPolygons([mw @ v.co for v in main.data.vertices], [list(p.vertices) for p in main.data.polygons])


def cast(sign, y, z):
    loc, nor, idx, _ = tree.ray_cast(Vector((sign * 8, y, z)), Vector((-sign, 0, 0)), 20)
    if loc is None:
        return None, None, -1
    if nor.x * sign < 0:
        nor = -nor
    return loc, nor, idx


def smooth(values):
    nu, nv = len(values), len(values[0])
    out = [[None] * nv for _ in range(nu)]
    for i in range(nu):
        for j in range(nv):
            s = Vector()
            for a in range(max(i - 2, 0), min(i + 3, nu)):
                for b in range(max(j - 2, 0), min(j + 3, nv)):
                    if values[a][b] is not None:
                        s += values[a][b]
            out[i][j] = s.normalized() if s.length > 0 else None
    return out


def tooth(v, h, teeth):
    p = v / h * teeth * 2
    k = math.floor(p)
    f = p - k
    return TOOTH * (f if k % 2 == 0 else 1 - f)


def shape(v, h, teeth):
    """판 윤곽의 u 오프셋. 톱니 끝이 기수 쪽을 향한다."""
    return TOOTH - tooth(v, h, teeth)


def v_rows(h, teeth):
    rows = {0.0, CHAMFER, h - CHAMFER, h}
    for k in range(1, teeth * 2):
        rows.add(h * k / (teeth * 2))
    n = max(int((h - 2 * CHAMFER) / STEP), 1)
    for k in range(1, n):
        rows.add(CHAMFER + (h - 2 * CHAMFER) * k / n)
    rows = sorted(rows)
    out = [rows[0]]
    for r in rows[1:]:
        if r - out[-1] > 0.012:
            out.append(r)
    return out


def atlas_uv(plate, u, v):
    """판 좌표(m) → 아틀라스 UV."""
    _, u0, _, _, _, _, ax, ay = plate
    return ((ax + (u - u0)) * PPM / ATLAS_W, (ay + v) * PPM / ATLAS_H)


def add_bolt(bm, uv_layer, centre, normal, uv):
    t = normal.orthogonal().normalized()
    b = normal.cross(t)
    base = []
    top = []
    for k in range(6):
        ang = k * math.tau / 6
        d = t * math.cos(ang) + b * math.sin(ang)
        base.append(bm.verts.new(centre + d * BOLT_RADIUS - normal * 0.003))
        top.append(bm.verts.new(centre + d * BOLT_RADIUS * 0.8 + normal * BOLT_HEIGHT))
    faces = [bm.faces.new(top)]
    for k in range(6):
        n = (k + 1) % 6
        faces.append(bm.faces.new((base[k], base[n], top[n], top[k])))
    for f in faces:
        for loop in f.loops:
            loop[uv_layer].uv = uv


def build_plate(plate, sign, samples):
    name, u0, u1, v0, v1, teeth, _, _ = plate
    h = v1 - v0
    rows = v_rows(h, teeth)
    nv = len(rows)
    nu = max(int((u1 - u0) / STEP), 2) + 3
    pos = [[None] * nv for _ in range(nu)]
    normals = [[None] * nv for _ in range(nu)]
    rim = [[False] * nv for _ in range(nu)]
    coord = [[None] * nv for _ in range(nu)]
    for j, rv in enumerate(rows):
        s = shape(rv, h, teeth)
        left, right = u0 + s, u1 + s
        for i in range(nu):
            if i == 0:
                u = left
            elif i == nu - 1:
                u = right
            else:
                u = left + CHAMFER + (right - left - 2 * CHAMFER) * (i - 1) / (nu - 3)
            loc, nor, idx = cast(sign, u, v0 + rv)
            pos[i][j] = loc
            normals[i][j] = nor
            rim[i][j] = i == 0 or i == nu - 1 or j == 0 or j == nv - 1
            coord[i][j] = atlas_uv(plate, u, rv)
            if idx >= 0 and not rim[i][j]:
                samples.append(idx)
    for _ in range(4):
        normals = smooth(normals)
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    verts = {}
    for i in range(nu):
        for j in range(nv):
            if pos[i][j] is not None and normals[i][j] is not None:
                verts[(i, j)] = bm.verts.new(pos[i][j] + normals[i][j] * (RIM if rim[i][j] else HEIGHT))
    flip = None
    for i in range(nu - 1):
        for j in range(nv - 1):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if not all(k in verts for k in q):
                continue
            f = [verts[k] for k in q]
            if flip is None:
                normal = (f[1].co - f[0].co).cross(f[2].co - f[0].co)
                flip = normal.x * sign < 0
            order = list(reversed(q)) if flip else q
            face = bm.faces.new([verts[k] for k in order])
            face.smooth = not any(rim[a][b] for a, b in q)
            for loop, k in zip(face.loops, order):
                loop[uv_layer].uv = coord[k[0]][k[1]]
    count = max(int((u1 - u0 - 0.5) / BOLT_STEP), 1)
    bolts = []
    for rv in (BOLT_INSET, h - BOLT_INSET):
        shift = shape(rv, h, teeth)
        for k in range(count + 1):
            u = u0 + 0.25 + (u1 - u0 - 0.5) * k / count + shift
            parts = 3 if u1 - u0 > 2 else 2
            for n in range(1, parts):
                seam = u0 + shift + (u1 - u0) * n / parts
                if abs(u - seam) < 0.05:
                    u = seam + (0.06 if u >= seam else -0.06)
            loc, nor, _ = cast(sign, u, v0 + rv)
            if loc is not None:
                add_bolt(bm, uv_layer, loc + nor * HEIGHT, nor, atlas_uv(plate, u, rv))
                bolts.append((u - u0, rv))
    me = bpy.data.meshes.new("plate")
    bm.to_mesh(me)
    bm.free()
    return me, bolts


def load_image(path):
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.empty(w * h * img.channels, dtype=np.float32)
    img.pixels.foreach_get(px)
    return img, px.reshape(h, w, img.channels)


def sample_paint(indices):
    """판 자리의 본체 도장색 평균·표준편차(sRGB 0~1)."""
    img, px = load_image(BODY)
    h, w = px.shape[:2]
    uv = main.data.uv_layers.active.data
    out = []
    for idx in indices:
        p = main.data.polygons[idx]
        c = Vector((0, 0))
        for li in p.loop_indices:
            c += uv[li].uv
        c /= p.loop_total
        x = int((c.x % 1.0) * (w - 1))
        y = int((c.y % 1.0) * (h - 1))
        out.append(px[y, x, :3])
    out = np.array(out)
    bpy.data.images.remove(img)
    return out.mean(axis=0), out.std(axis=0)


def value_noise(w, h, cell_x, cell_y, seed):
    rng = np.random.default_rng(seed)
    gx = max(int(w / cell_x), 2) + 2
    gy = max(int(h / cell_y), 2) + 2
    g = rng.random((gy, gx))
    xs = np.linspace(0, gx - 1.001, w)
    ys = np.linspace(0, gy - 1.001, h)
    xi = xs.astype(int)
    yi = ys.astype(int)
    tx = xs - xi
    ty = ys - yi
    tx = tx * tx * (3 - 2 * tx)
    ty = ty * ty * (3 - 2 * ty)
    a = g[np.ix_(yi, xi)]
    b = g[np.ix_(yi, xi + 1)]
    c = g[np.ix_(yi + 1, xi)]
    d = g[np.ix_(yi + 1, xi + 1)]
    top = a * (1 - tx) + b * tx
    bot = c * (1 - tx) + d * tx
    return top * (1 - ty[:, None]) + bot * ty[:, None]


def fbm(w, h, cell_x, cell_y, seed):
    return (value_noise(w, h, cell_x, cell_y, seed) * 0.55
            + value_noise(w, h, cell_x / 2, cell_y / 2, seed + 1) * 0.3
            + value_noise(w, h, cell_x / 4, cell_y / 4, seed + 2) * 0.15)


def make_textures(paint, bolts_by_plate):
    """본체처럼 깔끔한 도장: 얇은 패널선과 리벳 줄, 아주 약한 얼룩만 넣는다."""
    w, h = ATLAS_W, ATLAS_H
    xx, yy = np.meshgrid((np.arange(w) + 0.5) / PPM, (np.arange(h) + 0.5) / PPM)
    dist = np.full((h, w), -1.0)
    seams = np.zeros((h, w), dtype=np.float32)
    for plate in PLATES:
        _, u0, u1, v0, v1, teeth, ax, ay = plate
        ph = v1 - v0
        u = xx - ax
        v = yy - ay
        inside = (v > -0.02) & (v < ph + 0.02)
        p = np.clip(v, 0, ph) / ph * teeth * 2
        k = np.floor(p)
        f = p - k
        off = TOOTH - TOOTH * np.where(k % 2 == 0, f, 1 - f)
        left = off
        right = (u1 - u0) + off
        d = np.minimum(np.minimum(u - left, right - u), np.minimum(v, ph - v))
        d[~inside] = -1
        dist = np.maximum(dist, d)
        # 구획선: 판 끝의 톱니를 그대로 따라간다
        parts = 3 if u1 - u0 > 2 else 2
        for n in range(1, parts):
            x = left + (u1 - u0) * n / parts
            seams = np.maximum(seams, np.exp(-((u - x) / 0.0035) ** 2) * inside * (d > 0.045))
    mott = fbm(w, h, 260, 170, 1) - 0.5
    base = paint[None, None, :] * SHADE
    img = np.tile(base, (h, w, 1)).astype(np.float32)
    img *= (1 + mott[..., None] * 0.035)
    # 판 둘레 안쪽 패널선(본체 패널선과 같은 굵기·농도)
    border = np.exp(-((dist - 0.058) / 0.0032) ** 2) * (dist > 0)
    lines = np.maximum(border, seams)
    img *= (1 - lines[..., None] * 0.42)
    # 경사면은 살짝 밝게 닳았다
    edge = np.clip((CHAMFER - dist) / CHAMFER, 0, 1) * (dist > 0)
    img = img * (1 - edge[..., None] * 0.35) + paint[None, None, :] * edge[..., None] * 0.35
    for (plate, bolts) in bolts_by_plate:
        _, _, _, _, _, _, ax, ay = plate
        for (bu, bv) in bolts:
            r2 = (xx - (ax + bu)) ** 2 + (yy - (ay + bv)) ** 2
            img *= (1 - np.exp(-r2 / (0.018 ** 2))[..., None] * 0.14)
    img = np.clip(img, 0, 1)
    metal = np.full((h, w), 0.08, dtype=np.float32)
    gloss = np.clip(0.44 + mott * 0.05 - edge * 0.06, 0.1, 0.7).astype(np.float32)

    def save(name, rgb, alpha=None):
        im = bpy.data.images.new(name, w, h, alpha=True)
        a = alpha if alpha is not None else np.ones((h, w), dtype=np.float32)
        im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(np.dstack((rgb, a)).astype(np.float32).ravel())
        im.filepath_raw = os.path.join(OUT, name + ".png")
        im.file_format = 'PNG'
        im.save()
        return im

    albedo = save("ACO_ArmorKit_Albedo", img)
    mg = save("ACO_ArmorKit_MetallicGloss", np.dstack((metal, metal, metal)), gloss)
    return albedo, mg


samples = []
bolts_by_plate = []
meshes = []
for plate in PLATES:
    for sign in (1, -1):
        me, bolts = build_plate(plate, sign, samples)
        meshes.append((f"{plate[0]}_{'L' if sign > 0 else 'R'}", me))
    bolts_by_plate.append((plate, bolts))

paint, spread = sample_paint(samples)
print("PAINT", [round(float(v), 3) for v in paint], "SPREAD", [round(float(v), 3) for v in spread], "SAMPLES", len(samples))
albedo, mg = make_textures(paint, bolts_by_plate)
print("TEXTURES OK")

# ── 재질 ──
mat = bpy.data.materials.new("armor")
mat.use_nodes = True
nodes = mat.node_tree.nodes
bsdf = nodes["Principled BSDF"]
tex = nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(os.path.join(OUT, "ACO_ArmorKit_Albedo.png"))
mat.node_tree.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
bsdf.inputs["Metallic"].default_value = 0.08
bsdf.inputs["Roughness"].default_value = 0.58
tris = 0
for name, me in meshes:
    me.materials.append(mat)
    tris += sum(len(p.vertices) - 2 for p in me.polygons)
    scene.collection.objects.link(bpy.data.objects.new(name, me))
print("TRIS", tris)

# ── 간격 측정: 판 꼭짓점과 면 가운데의 동체까지 부호 있는 거리(+ = 바깥) ──
def signed_gap(point, sign):
    loc, nor, _, _ = tree.find_nearest(point)
    side = (point - loc).dot(nor)
    # 동체 면 법선이 뒤집힌 경우가 있어 바깥쪽(X 방향)으로 부호를 정한다
    hit, hn, _ = cast(sign, point.y, point.z)
    if hit is None:
        return None
    return (point.x - hit.x) * sign

import json
report = {}
export = []
for name, me in meshes:
    sign = 1 if name.endswith("_L") else -1
    rim = []
    top = []
    mids = []
    for p_ in me.polygons:
        if len(p_.vertices) != 4 or p_.use_smooth is None:
            continue
        pts = [me.vertices[i].co for i in p_.vertices]
        c = sum(pts, Vector()) * 0.25
        g = signed_gap(c, sign)
        if g is not None:
            (mids if p_.use_smooth else rim).append(g)
    for v in me.vertices:
        g = signed_gap(v.co, sign)
        if g is not None:
            top.append(g)
    report[name] = {
        "verts_min_mm": round(min(top) * 1000, 1), "verts_max_mm": round(max(top) * 1000, 1),
        "top_faces_min_mm": round(min(mids) * 1000, 1), "top_faces_max_mm": round(max(mids) * 1000, 1),
        "rim_faces_min_mm": round(min(rim) * 1000, 1), "rim_faces_max_mm": round(max(rim) * 1000, 1)}
    print("GAP", name, report[name])

# ── 내보내기: 판을 한 오브젝트로 합쳐 항공기 원점 기준 FBX로 쓴다(Unity에서 +Z가 기수, 1단위 = 1m) ──
armor_objects = [bpy.data.objects[name] for name, _ in meshes]
bpy.ops.object.select_all(action='DESELECT')
for o in armor_objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = armor_objects[0]
bpy.ops.object.join()
kit = bpy.context.view_layer.objects.active
kit.name = "ACO_ArmorKit_FA18E"
kit.data.name = "ACO_ArmorKit_FA18E"
bpy.ops.object.select_all(action='DESELECT')
kit.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "ACO_ArmorKit_FA18E.fbx"),
    use_selection=True,
    object_types={'MESH'},
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',
    axis_up='Y',
    bake_space_transform=True,
    mesh_smooth_type='FACE',
    path_mode='RELATIVE',
    add_leaf_bones=False,
    bake_anim=False)
print("EXPORT", kit.name, "tris", sum(len(p.vertices) - 2 for p in kit.data.polygons))
print("DONE")
