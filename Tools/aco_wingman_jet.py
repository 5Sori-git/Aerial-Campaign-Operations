"""아군 편대(6-273) 기체 Jet_02(CartoonMilitaryModelPack) — UV를 다시 펴고 도장 · 패널 라인 · 표식을 텍스처에 직접 그린다.
원본 FBX · 텍스처는 건드리지 않는다. 결과는 Assets/ACO_Addon/Models/Wingman에:
  ACO_WingmanJet_Albedo.png  — 도장 텍스처(2048)
  ACO_WingmanJet_Mesh.json   — 원본 메시(FBX 노드 로컬 좌표) + 새 UV. Unity 빌더(ACO → Build Wingman Prefab)가 메시 에셋으로 만든다
실행:
  blender --background --factory-startup --python Tools/aco_wingman_jet.py -- bake  <scratch>   (UV 펴기 + 위치/법선/원본색/AO 굽기 → scratch/maps.npz, 메시 JSON)
  blender --background --factory-startup --python Tools/aco_wingman_jet.py -- paint <scratch>   (maps.npz → 텍스처 + 미리보기 렌더)
좌표: Blender 월드(가져온 그대로). 기체 길이 축 y, 위 z. 기수 방향은 캐노피(원본 텍스처의 어두운 부분) 위치로 정한다.
표식은 모두 가공이다(실존 국가 · 부대 아님)."""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK = os.path.join(ROOT, "Assets", "CartoonMilitaryModelPack")
SRC = os.path.join(PACK, "OriginalMeshes&Materials", "Jet", "Jet_02.fbx")
SRC_TEX = os.path.join(PACK, "Textures", "Jet_Texture", "Jet_02_Texture.png")
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Wingman")
TEX_NAME = "ACO_WingmanJet_Albedo.png"
MESH_NAME = "ACO_WingmanJet_Mesh.json"
FONT = "C:/Windows/Fonts/arialbd.ttf"
N = 2048


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    obj = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    return obj


def unwrap(obj):
    me = obj.data
    uv = me.uv_layers.new(name="ACO")
    me.uv_layers.active = uv
    uv.active_render = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=0.003, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode='OBJECT')


def export_mesh_json(obj):
    """FBX 노드 로컬 좌표(가져올 때 메시 데이터는 FBX 값 그대로) + 꼭짓점별 법선 + 새 UV. 삼각형으로 나눈다.
    Unity JsonUtility가 읽게 평평한 배열(positions x,y,z,… · normals · uvs u,v,… · triangles i,j,k,…)로 쓴다."""
    me = obj.data
    me.calc_loop_triangles()
    uv = me.uv_layers["ACO"].data
    cn = me.corner_normals
    pos, nrm, uvs, tris = [], [], [], []
    key_index = {}
    for lt in me.loop_triangles:
        tri = []
        for li in lt.loops:
            vi = me.loops[li].vertex_index
            p = me.vertices[vi].co
            n = cn[li].vector
            u = uv[li].uv
            k = (round(p.x, 5), round(p.y, 5), round(p.z, 5), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u.x, 6), round(u.y, 6))
            if k not in key_index:
                key_index[k] = len(pos) // 3
                pos.extend([round(p.x, 6), round(p.y, 6), round(p.z, 6)])
                nrm.extend([round(n.x, 5), round(n.y, 5), round(n.z, 5)])
                uvs.extend([round(u.x, 6), round(u.y, 6)])
            tri.append(key_index[k])
        tris.extend(tri)
    data = {"source": "Jet_02.fbx/Jet_02_Body", "positions": pos, "normals": nrm, "uvs": uvs, "triangles": tris}
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, MESH_NAME), "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"))
    print("MESH verts", len(pos) // 3, "tris", len(tris) // 3)


def bake_maps(obj, scratch):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    world = bpy.data.worlds.new("W")
    scene.world = world
    world.light_settings.distance = 0.06
    mat = bpy.data.materials.new("bake")
    mat.use_nodes = True
    nt = mat.node_tree
    nodes, links = nt.nodes, nt.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    emit = nodes.new("ShaderNodeEmission")
    links.new(emit.outputs[0], out.inputs["Surface"])
    geo = nodes.new("ShaderNodeNewGeometry")
    half = nodes.new("ShaderNodeVectorMath")
    half.operation = 'MULTIPLY_ADD'
    half.inputs[1].default_value = (0.5, 0.5, 0.5)
    half.inputs[2].default_value = (0.5, 0.5, 0.5)
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(SRC_TEX)
    tex.interpolation = 'Closest'
    uvn = nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "map1"
    links.new(uvn.outputs[0], tex.inputs[0])
    rgb = nodes.new("ShaderNodeRGB")
    rgb.outputs[0].default_value = (1, 1, 1, 1)
    target = nodes.new("ShaderNodeTexImage")
    nodes.active = target
    obj.data.materials.clear()
    obj.data.materials.append(mat)

    maps = {}

    def bake(kind, src, samples=1):
        im = bpy.data.images.new(kind, N, N, alpha=True, float_buffer=True)
        im.colorspace_settings.name = 'Non-Color'
        target.image = im
        scene.cycles.samples = samples
        if src is not None:
            for l in list(emit.inputs[0].links):
                links.remove(l)
            links.new(src, emit.inputs[0])
            bpy.ops.object.bake(type='EMIT', margin=0, use_clear=True)
        else:
            bpy.ops.object.bake(type='AO', margin=0, use_clear=True)
        a = np.empty(N * N * 4, dtype=np.float32)
        im.pixels.foreach_get(a)
        maps[kind] = a.reshape(N, N, 4)[..., :3].copy()
        print("BAKED", kind)

    links.new(geo.outputs["Position"], half.inputs[0])
    bake("pos", half.outputs[0])
    links.remove(half.inputs[0].links[0])
    links.new(geo.outputs["Normal"], half.inputs[0])
    bake("nrm", half.outputs[0])
    bake("orig", tex.outputs["Color"])
    bake("cov", rgb.outputs[0])
    # 면 단위 구분: 원본 텍스처의 면 가운데 색 — 어두움(캐노피) R, 중간 회색(흡입구 · 노즐) G. 텍셀 단위로 나누면 칸 경계가 면과 맞지 않아 점이 남는다
    src = tex.image
    sw, sh = src.size
    spx = np.empty(sw * sh * 4, np.float32)
    src.pixels.foreach_get(spx)
    spx = spx.reshape(sh, sw, 4)
    me = obj.data
    uvs = me.uv_layers["map1"].data
    attr = me.color_attributes.new("cls", 'FLOAT_COLOR', 'FACE')
    for poly in me.polygons:
        u = sum(uvs[li].uv.x for li in poly.loop_indices) / poly.loop_total
        v = sum(uvs[li].uv.y for li in poly.loop_indices) / poly.loop_total
        c = spx[min(max(int(v * sh), 0), sh - 1), min(max(int(u * sw), 0), sw - 1), :3]
        l = float(c @ np.array([0.2126, 0.7152, 0.0722], np.float32))
        attr.data[poly.index].color = (1.0 if l < 0.40 else 0.0, 1.0 if 0.40 <= l < 0.62 else 0.0, 0.0, 1.0)   # 픽셀 값은 sRGB(바탕 약 0.7, 회색 칸 0.5, 어두운 칸 0.3)
    an = nodes.new("ShaderNodeAttribute")
    an.attribute_name = "cls"
    bake("cls", an.outputs["Color"])
    bake("ao", None, samples=96)
    np.savez_compressed(os.path.join(scratch, "maps.npz"), **maps)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(scratch, "jet_unwrapped.blend"))


# ───────────── 도장 ─────────────
# 색은 sRGB 값 그대로 칠한다(바이트 PNG로 저장)
TOP_A = np.array([0.42, 0.49, 0.57], np.float32)     # 윗면 짙은 회청색
TOP_B = np.array([0.52, 0.59, 0.67], np.float32)     # 윗면 옅은 회청색(위장 무늬)
BELLY = np.array([0.70, 0.74, 0.78], np.float32)
RADOME = np.array([0.35, 0.38, 0.42], np.float32)
GLARE = np.array([0.27, 0.30, 0.34], np.float32)     # 캐노피 앞 눈부심 방지 판
NAVY = np.array([0.11, 0.17, 0.31], np.float32)
WHITE = np.array([0.92, 0.93, 0.94], np.float32)
LIGHTBLUE = np.array([0.36, 0.62, 0.86], np.float32)
GLASS = np.array([0.06, 0.09, 0.13], np.float32)
GLASS_HI = np.array([0.30, 0.40, 0.50], np.float32)
FRAME = np.array([0.20, 0.23, 0.27], np.float32)
METAL = np.array([0.29, 0.29, 0.30], np.float32)
HEAT = np.array([0.36, 0.33, 0.30], np.float32)
INTAKE = np.array([0.09, 0.09, 0.10], np.float32)
RED = np.array([0.72, 0.13, 0.10], np.float32)
SLIME = np.array([0.74, 0.86, 0.55], np.float32)    # 편대등(옅은 연두 띠)

LINE_W = 0.0016      # 패널 라인 폭(월드 — 기체 길이 1.13 = 실제 약 20m, 약 3cm)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def snoise(P, freq, seed):
    """위치 기반 매끈한 잡음(사인 합 — 섬 경계에서 끊기지 않는다). 대략 0~1."""
    rng = np.random.default_rng(seed)
    acc = np.zeros(P.shape[:2], np.float32)
    for _ in range(6):
        d = rng.normal(size=3)
        d /= np.linalg.norm(d)
        ph = rng.uniform(0, 6.283)
        f = freq * rng.uniform(0.7, 1.4)
        acc += np.sin((P @ d.astype(np.float32)) * f + ph)
    return acc / 6 * 0.5 + 0.5


def render_labels(labels, ppu=6000, cell=(0.22, 0.09)):
    """글자를 한 장에 그려 알파 마스크로 돌려준다. labels: [(문자열, 글자 크기(월드))]. 반환: (page[h,w], cell_w, cell_h, ppu)."""
    cw, ch = cell
    page = bpy.data.scenes.new("page")
    page.render.engine = 'BLENDER_WORKBENCH'
    page.display.shading.light = 'FLAT'
    page.display.shading.color_type = 'SINGLE'
    page.display.shading.single_color = (1, 1, 1)
    page.display.render_aa = '16'
    page.render.film_transparent = True
    page.view_settings.view_transform = 'Standard'
    page.render.resolution_x = int(cw * len(labels) * ppu)
    page.render.resolution_y = int(ch * ppu)
    page.render.resolution_percentage = 100
    cam = bpy.data.objects.new("pcam", bpy.data.cameras.new("pcam"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = cw * len(labels)
    cam.location = (cw * len(labels) / 2, ch / 2, 5)
    page.collection.objects.link(cam)
    page.camera = cam
    font = bpy.data.fonts.load(FONT)
    for i, (body, size) in enumerate(labels):
        c = bpy.data.curves.new("t", 'FONT')
        c.body = body
        c.font = font
        c.size = size
        c.align_x = 'CENTER'
        c.align_y = 'CENTER'
        c.space_character = 1.05
        o = bpy.data.objects.new("t", c)
        o.location = (cw * (i + 0.5), ch / 2, 0)
        page.collection.objects.link(o)
    temp = os.path.join(bpy.app.tempdir, "aco_labels.png")
    page.render.filepath = temp
    bpy.ops.render.render(write_still=True, scene=page.name)
    img = bpy.data.images.load(temp)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    bpy.data.scenes.remove(page)
    return px.reshape(h, w, 4)[..., 3], cw, ch, ppu


def sample_label(page, cw, ch, ppu, index, a, b):
    """라벨 index의 칸 가운데 기준 (a 오른쪽, b 위, 월드)에서 알파."""
    h, w = page.shape
    px = ((index + 0.5) * cw + a) * ppu
    py = (ch / 2 + b) * ppu
    ok = (px >= 0) & (px < w - 1) & (py >= 0) & (py < h - 1)
    x0 = np.clip(px.astype(np.int32), 0, w - 2)
    y0 = np.clip(py.astype(np.int32), 0, h - 2)
    fx = px - x0
    fy = py - y0
    v = (page[y0, x0] * (1 - fx) * (1 - fy) + page[y0, x0 + 1] * fx * (1 - fy)
         + page[y0 + 1, x0] * (1 - fx) * fy + page[y0 + 1, x0 + 1] * fx * fy)
    return np.where(ok, v, 0.0)


def dilate(mask, steps):
    m = mask.copy()
    for _ in range(steps):
        n = m.copy()
        n[1:, :] |= m[:-1, :]
        n[:-1, :] |= m[1:, :]
        n[:, 1:] |= m[:, :-1]
        n[:, :-1] |= m[:, 1:]
        m = n
    return m


def bleed(img, cov, steps=10):
    """섬 바깥으로 색을 번지게 한다(밉맵 · 필터에서 경계선이 생기지 않게)."""
    img = img.copy()
    have = cov.copy()
    for _ in range(steps):
        acc = np.zeros_like(img)
        cnt = np.zeros(have.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sh = np.roll(np.roll(have, dy, 0), dx, 1)
            si = np.roll(np.roll(img, dy, 0), dx, 1)
            acc += si * sh[..., None]
            cnt += sh
        new = (~have) & (cnt > 0)
        img[new] = acc[new] / cnt[new][:, None]
        have |= new
    return img


def fit_edges(t, sp, mask, lo, hi, step=0.005):
    """mask 안에서 t(|x| 또는 z) 띠마다 sp(y)의 앞 · 뒤 끝을 모아 직선으로 맞춘다(날개 · 꼬리 앞전 · 뒷전은 곧다 — 띠 단위로 끊긴 계단 무늬가 생기지 않게).
    반환: (앞 끝 계수, 뒤 끝 계수) — np.polyval로 쓴다."""
    ts = t[mask]
    ss = sp[mask]
    tc, fr, rr = [], [], []
    for b in np.arange(lo, hi, step):
        sel = (ts >= b) & (ts < b + step)
        if sel.sum() < 20:
            continue
        tc.append(b + step / 2)
        fr.append(ss[sel].min())
        rr.append(ss[sel].max())
    return np.polyfit(tc, fr, 1), np.polyfit(tc, rr, 1)


def paint(scratch):
    m = np.load(os.path.join(scratch, "maps.npz"))
    cov = m["cov"][..., 0] > 0.5
    P = (m["pos"] * 2 - 1).astype(np.float32)
    Nn = (m["nrm"] * 2 - 1).astype(np.float32)
    Nn /= np.maximum(np.linalg.norm(Nn, axis=2, keepdims=True), 1e-6)
    ao = m["ao"][..., 0]
    lum = m["orig"] @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    nx, ny, nz = Nn[..., 0], Nn[..., 1], Nn[..., 2]
    ax = np.abs(x)

    # 텍셀 크기(월드) — 선 가장자리를 부드럽게
    d = np.linalg.norm(P[:, 1:] - P[:, :-1], axis=2)
    ok = cov[:, 1:] & cov[:, :-1]
    ts = float(np.median(d[ok]))
    print("texel size", ts)

    def line(dist, w=LINE_W):
        return np.clip(0.5 + (w * 0.5 - np.abs(dist)) / ts, 0, 1)

    def fill(dist):   # dist < 0 안쪽
        return np.clip(0.5 - dist / ts, 0, 1)

    # 기수(−y) 방향 확인: 캐노피(원본의 어두운 부분)가 앞쪽
    cls = m["cls"]
    dark = cov & (cls[..., 0] > 0.5)    # 캐노피(면 단위)
    mid = cov & (cls[..., 1] > 0.5)     # 흡입구 · 노즐(면 단위)
    print("canopy y", float(y[dark].mean()), "body y", float((y[cov].min() + y[cov].max()) / 2))

    flat = np.abs(nz) > 0.7
    fus_w = np.interp(y, [-0.64, -0.49, -0.39, -0.29, -0.19, -0.12, 0.25, 0.32, 0.5], [0.02, 0.045, 0.065, 0.095, 0.116, 0.12, 0.12, 0.10, 0.08])
    wingish = cov & flat & (ax > fus_w + 0.004)
    wing = wingish & (y < 0.26)
    stab = wingish & (y >= 0.26)
    fin = cov & ~flat & (y > 0.10) & (y < 0.36) & (z > 0.078)
    fuselage = cov & ~wing & ~stab & ~fin

    # ── 바탕: 윗면 회청 2색 위장 · 아랫면 옅은 회색 ──
    camo = smooth(0.47, 0.53, snoise(P, 22.0, 3))
    top = TOP_A[None, None, :] * (1 - camo[..., None]) + TOP_B[None, None, :] * camo[..., None]
    up = smooth(-0.35, 0.15, nz)
    up = np.where(fin, 1.0, up)
    col = BELLY[None, None, :] * (1 - up[..., None]) + top * up[..., None]
    # 잔무늬: 얼룩 · 결
    mott = (snoise(P, 70.0, 11) - 0.5) * 0.06 + (snoise(P, 260.0, 17) - 0.5) * 0.025
    col *= (1 + mott)[..., None]

    def put(mask_alpha, c):
        nonlocal col
        a = np.clip(mask_alpha, 0, 1)[..., None]
        col = col * (1 - a) + np.asarray(c, np.float32)[None, None, :] * a

    def darken(mask_alpha, k):
        nonlocal col
        col *= (1 - np.clip(mask_alpha, 0, 1) * k)[..., None]

    # 레이돔 · 눈부심 방지 판
    put(fuselage * fill(y - (-0.565)), RADOME)
    put(fuselage * (nz > 0.35) * fill(np.maximum(y - (-0.47), -0.565 - y)) * fill(ax - 0.028), GLARE)

    # 배기구 그을음(꼬리 윗면이 뒤로 갈수록 짙다)
    darken(cov * ~mid * smooth(0.36, 0.49, y) * (nz > -0.2), 0.28)

    # ── 패널 라인 ──
    L = np.zeros(cov.shape, np.float32)
    for ys in (-0.565, -0.47, -0.18, -0.06, 0.05, 0.16, 0.27, 0.38):
        L = np.maximum(L, fuselage * line(y - ys))
    spine = fuselage * (nz > 0.5) * (y > -0.18) * (y < 0.40)
    L = np.maximum(L, spine * line(ax - 0.026))
    belly = fuselage * (nz < -0.5) * (y > -0.5) * (y < 0.40)
    L = np.maximum(L, belly * line(ax - 0.05))
    side = fuselage * (np.abs(nx) > 0.55) * (y > -0.56) * (y < 0.10)
    L = np.maximum(L, side * line(z - 0.018))
    # 점검 해치(사각 테두리) — 옆면 · 등
    for (y0, y1, z0, z1) in ((-0.30, -0.235, -0.012, 0.012), (-0.10, -0.03, -0.022, 0.006), (0.06, 0.13, -0.02, 0.004)):
        dd = np.maximum(np.maximum(y0 - y, y - y1), np.maximum(z0 - z, z - z1))
        L = np.maximum(L, side * line(dd, LINE_W * 0.9))
    for (y0, y1, x1) in ((-0.15, -0.09, 0.02), (0.20, 0.26, 0.018)):
        dd = np.maximum(np.maximum(y0 - y, y - y1), ax - x1)
        L = np.maximum(L, fuselage * (nz > 0.5) * line(dd, LINE_W * 0.9))

    # 주날개 · 수평꼬리: 같은 x 띠의 앞 · 뒤 끝 → 시위 비율
    for mask, is_wing in ((wing, True), (stab, False)):
        pf, pr = fit_edges(ax, y, mask, 0.17, 0.39) if is_wing else fit_edges(ax, y, mask, 0.14, 0.225)
        f = np.polyval(pf, ax)
        r = np.polyval(pr, ax)
        chord = np.maximum(r - f, 1e-4)
        c = (y - f) / chord
        dist_c = lambda frac: (c - frac) * chord
        if is_wing:
            L = np.maximum(L, mask * line(dist_c(0.13)))                         # 앞전 플랩
            L = np.maximum(L, mask * (c > 0.13) * line(dist_c(0.76)))            # 플랩 · 에일러론 경첩
            L = np.maximum(L, mask * (c > 0.76) * line(ax - 0.27))               # 플랩 | 에일러론
            L = np.maximum(L, mask * (c > 0.13) * (c < 0.76) * line(dist_c(0.45), LINE_W * 0.8) * 0.6)   # 날개보
            for xr in (0.17, 0.22, 0.31, 0.36):
                L = np.maximum(L, mask * (c > 0.13) * (c < 0.76) * line(ax - xr, LINE_W * 0.8) * 0.5)  # 리브
            put(mask * (c < 0.035) * (c > -0.02), np.array([0.60, 0.64, 0.68]))  # 앞전 보호판
            # 날개 끝 남색 띠(윗면)
            put(mask * (nz > 0) * fill(0.378 - ax), NAVY)
            # 편대등
            put(mask * (nz > 0) * fill(np.maximum(np.abs(c - 0.55) * chord - 0.022, np.abs(ax - 0.392) - 0.004)), SLIME)
        else:
            L = np.maximum(L, mask * line(dist_c(0.5), LINE_W * 0.8) * 0.6)
            L = np.maximum(L, mask * line(ax - (fus_w + 0.03)))
        # 표식: 원형 국적 표시(가공) — 왼날개 윗면 · 오른날개 아랫면. 조종석 기준 오른쪽은 −x
        if is_wing:
            for sx, sz in ((1, 1), (-1, -1)):
                xc = 0.27 * sx
                yc = float((np.polyval(pf, 0.27) + np.polyval(pr, 0.27)) / 2)
                rr = np.sqrt((x - xc) ** 2 + (y - yc) ** 2)
                on = mask & (nz * sz > 0)
                put(on * fill(rr - 0.046), NAVY)
                put(on * fill(rr - 0.037), WHITE)
                put(on * fill(rr - 0.028), LIGHTBLUE)
                put(on * fill(np.abs(x - xc) + np.abs(y - yc) - 0.016), WHITE)

    # 수직꼬리: 같은 높이 띠의 앞 · 뒤 끝(좌우 따로) → 방향타
    fin_top = {}
    for sx in (1, -1):
        mk = fin & (x * sx > 0)
        zt = float(z[mk].max())
        zb = float(np.percentile(z[mk], 2))
        pf, pr = fit_edges(z, y, mk, zb + 0.01, zt - 0.008, 0.004)
        f = np.polyval(pf, z)
        r = np.polyval(pr, z)
        chord = np.maximum(r - f, 1e-4)
        c = (y - f) / chord
        fin_top[sx] = (zt, zb, pf, pr)
        L = np.maximum(L, mk * (z < zb + (zt - zb) * 0.72) * line((c - 0.70) * chord))
        L = np.maximum(L, mk * line(z - (zb + (zt - zb) * 0.72)))
        put(mk * fill((zt - 0.022) - z), NAVY)                              # 꼬리 끝 남색
        put(mk * fill(np.maximum(np.abs(z - (zt - 0.034)) - 0.003, np.abs(c - 0.35) * chord - 0.02)), SLIME)

    darken(L, 0.42)

    # ── 캐노피 · 흡입구 · 배기구 ──
    hi = smooth(0.25, 0.9, nz) * 0.55 + smooth(0.006, 0.0, np.abs(x - 0.012) - 0.003) * 0.35
    glass = GLASS[None, None, :] * (1 - hi[..., None]) + GLASS_HI[None, None, :] * hi[..., None]
    a = dark[..., None].astype(np.float32)
    col = col * (1 - a) + glass * a
    rim = dilate(dark, 4) & ~dark & cov
    put(rim, FRAME)
    put(dark * line(y - (-0.40), 0.0035), FRAME)       # 앞 유리 테
    put(dark * line(y - (-0.30), 0.003), FRAME)        # 캐노피 가운데 테
    intake = mid & (y < 0.0) & (np.abs(nz) < 0.6)
    put(intake, INTAKE)
    nozzle = mid & (y > 0.3)
    heat = smooth(0.44, 0.50, y)
    noz = METAL[None, None, :] * (1 - heat[..., None]) + HEAT[None, None, :] * heat[..., None]
    noz *= (1 + 0.035 * np.sin(np.arctan2(z - 0.02, np.abs(x) - 0.04) * 24))[..., None]   # 노즐 꽃잎
    a = nozzle[..., None].astype(np.float32)
    col = col * (1 - a) + noz * a
    belly_bits = mid & (y < 0.3) & (np.abs(nz) >= 0.6)
    put(belly_bits, BELLY)

    # ── 글자 · 경고 표시 ──
    page, cw, chh, ppu = render_labels([("WM", 0.075), ("201", 0.04)])
    for sx in (1, -1):
        # 수직꼬리 바깥면: 큰 남색 WM
        mk = fin & (x * sx > 0) & (nx * sx > 0.2)
        zt, zb, pf, pr = fin_top[sx]
        zc = zb + (zt - zb) * 0.42
        yc = float((np.polyval(pf, zc) + np.polyval(pr, zc)) / 2)
        aa = (y - yc) * sx
        put(mk * sample_label(page, cw, chh, ppu, 0, aa, z - zc), NAVY)
        # 기수 옆: 기번 201
        mk = fuselage & (nx * sx > 0.4) & (y > -0.6) & (y < -0.44)
        put(mk * sample_label(page, cw, chh, ppu, 1, (y - (-0.515)) * sx, z - 0.008), NAVY)
        # 구조 표시(빨간 삼각형) — 캐노피 아래
        mk = fuselage & (nx * sx > 0.4)
        tri = np.maximum(np.maximum(0.022 - z, z - 0.038), np.abs(y - (-0.36)) - (0.038 - z) * 0.6)
        put(mk * fill(tri), RED)
        # 편대등(기수 옆)
        put(mk * fill(np.maximum(np.abs(y - (-0.24)) - 0.02, np.abs(z - 0.045) - 0.0035)), SLIME)

    # 앰비언트 오클루전
    col *= (1 - (1 - ao) * 0.6)[..., None]

    col = np.clip(col, 0, 1)
    col = bleed(col, cov, 12)
    out = np.ones((N, N, 4), np.float32)
    out[..., :3] = col
    im = bpy.data.images.new("ACO_WingmanJet_Albedo", N, N, alpha=False)
    im.pixels.foreach_set(out.ravel())
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, TEX_NAME)
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    print("TEX", path)
    return path


def preview(scratch, tex_path):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(scratch, "jet_unwrapped.blend"))
    from mathutils import Vector
    obj = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    obj.data.uv_layers.active = obj.data.uv_layers["ACO"]
    mat = bpy.data.materials.new("Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(tex_path)
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "ACO"
    nt.links.new(uvn.outputs[0], tn.inputs[0])
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 1200, 800
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.58, 0.7, 1)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    scene.collection.objects.link(sun)
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(35), math.radians(10), math.radians(40))
    bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    center = sum(bb, Vector()) / 8
    size = max((b - center).length for b in bb)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'

    def shot(name, d, zoom=1.0, target=None):
        d = Vector(d).normalized()
        t = center if target is None else Vector(target)
        cam.data.ortho_scale = size * 2.2 / zoom
        cam.location = t + d * size * 4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(scratch, "new_" + name + ".png")
        bpy.ops.render.render(write_still=True)

    shot("top", (0, 0, 1))
    shot("bottom", (0, 0, -1))
    shot("left", (1, 0, 0.15))
    shot("right", (-1, 0, 0.15))
    shot("front34", (0.6, -0.7, 0.45))
    shot("rear34", (-0.6, 0.75, 0.4))
    shot("nose", (0.8, -0.3, 0.3), 3.0, (0.0, -0.42, 0.03))
    shot("tail", (0.7, 0.5, 0.35), 2.6, (0.0, 0.30, 0.10))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stage = argv[0] if argv else "bake"
    scratch = argv[1] if len(argv) > 1 else os.path.join(OUT, "_work")
    os.makedirs(scratch, exist_ok=True)
    if stage == "bake":
        o = load()
        unwrap(o)
        export_mesh_json(o)
        bake_maps(o, scratch)
        print("DONE bake")
    elif stage == "paint":
        tex = paint(scratch)
        preview(scratch, tex)
        print("DONE paint")
