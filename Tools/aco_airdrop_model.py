"""수송기 투하 파티클 메시(6-148) — 보급 상자·공수부대원 + 둥근 낙하산. 파티클 메시라 저폴리(하나에 약 400~500 삼각형), 텍스처 512 한 장(아틀라스).
 - 보급 상자: 나무 팔레트(판 + 받침 3줄) 위 합판 상자 1.6×1.3×1.6m, 십자 고정 끈. 낙하산 지름 9m(흰색·주황 천 조각 번갈아).
   줄 8가닥이 상자 위 1m 고리로 모이고, 고리에서 상자 윗모서리 넷으로 슬링.
 - 공수부대원: 키 1.8m, 헬멧·몸통·주 낙하산 배낭(등)·예비 낙하산(배)·팔은 위로 조종줄(라이저)을 잡은 자세·다리·군화. 낙하산 지름 7m(국방색 두 톤).
   줄은 왼쪽 반·오른쪽 반이 각각 어깨 위 라이저 끝으로 모인다.
 - 낙하산: 천 조각 12장(조각마다 가운데가 살짝 부푼 2칸), 꼭대기 구멍(벤트), 끝단이 조금 오므라든 둥근 갓. 양면으로 그린다(머티리얼 Cull Off).
 - 원점: 보급 상자 = 상자 가운데, 공수부대원 = 몸 가운데(예전 메시와 같은 자리 — 파티클 위치·충돌이 그대로).
좌표: 블렌더 z 위. 내보내기는 다른 ACO 모델과 같은 설정(축 -Z 앞·Y 위). 유니티(ACO_DecoBuilder)가 FBX 자식의 변환을 메시에 구워 넣는다.
실행: blender --background --factory-startup --python Tools/aco_airdrop_model.py   (ACO_AIRDROP_PREVIEW=경로접두 → 미리보기 렌더)"""
import math
import os
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("ACO_AIRDROP_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "AirDrop"))
NAME = "ACO_AirDrop"
TEX = 512
GORES = 12
SEGS = GORES * 2
# 갓 모양(꼭대기 구멍 → 끝단): (반지름 비, 높이 비)
PROFILE = [(0.10, 1.00), (0.40, 0.93), (0.68, 0.76), (0.88, 0.50), (0.97, 0.24), (0.93, 0.06)]
CANOPY_H = 0.6      # 갓 높이 = 반지름 × 이 값
BULGE = 1.05        # 천 조각 가운데가 부푸는 정도

# ── 아틀라스 칸 (u0, v0, u1, v1) ──
CAN_S = (0.0, 0.5, 0.5, 1.0)
CAN_T = (0.5, 0.5, 1.0, 1.0)
CRATE_SIDE = (0.0, 0.25, 0.25, 0.5)
CRATE_TOP = (0.25, 0.25, 0.5, 0.5)
PALLET = (0.0, 0.125, 0.25, 0.25)
LINE_S = (0.25, 0.125, 0.375, 0.25)
LINE_T = (0.375, 0.125, 0.5, 0.25)


def cell(i, j):
    """공수부대원 색 칸(아틀라스 오른쪽 아래 4×4)."""
    return (0.5 + i * 0.125, j * 0.125, 0.5 + (i + 1) * 0.125, (j + 1) * 0.125)


UNI, PANTS, HELMET, SKIN = cell(0, 0), cell(1, 0), cell(2, 0), cell(3, 0)
BOOTS, GEAR, PACK, RESERVE = cell(0, 1), cell(1, 1), cell(2, 1), cell(3, 1)

# 색
WHITE = np.array((0.90, 0.90, 0.87))
ORANGE = np.array((0.93, 0.42, 0.10))
OLIVE = np.array((0.34, 0.38, 0.24))
OLIVE_DK = np.array((0.24, 0.28, 0.17))
PLY = np.array((0.74, 0.64, 0.46))
PLY_DK = np.array((0.55, 0.46, 0.32))
STRAP = np.array((0.16, 0.17, 0.12))
RAW = np.array((0.66, 0.55, 0.38))


class Builder:
    """정점·면·면마다 UV를 모았다가 메시 하나로."""

    def __init__(self):
        self.v = []
        self.f = []
        self.uv = []
        self.smooth = []

    def add(self, pts):
        base = len(self.v)
        self.v.extend(Vector(p) for p in pts)
        return base

    def face(self, idx, uvs, smooth=False):
        self.f.append(tuple(idx))
        self.uv.append(uvs)
        self.smooth.append(smooth)

    def face_proj(self, idx, tile, smooth=False, margin=0.1):
        """면을 가장 넓게 보이는 두 축으로 투영해 칸 안쪽에 채운다."""
        P = [self.v[i] for i in idx]
        n = (P[1] - P[0]).cross(P[2] - P[0])
        ax = max(range(3), key=lambda k: abs(n[k]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        us = [p[a] for p in P]
        vs = [p[b] for p in P]
        u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
        du, dv = max(u1 - u0, 1e-6), max(v1 - v0, 1e-6)
        t0, s0, t1, s1 = tile
        mu, mv = (t1 - t0) * margin, (s1 - s0) * margin
        uvs = [(t0 + mu + (p[a] - u0) / du * (t1 - t0 - 2 * mu), s0 + mv + (p[b] - v0) / dv * (s1 - s0 - 2 * mv)) for p in P]
        self.face(idx, uvs, smooth)

    def box(self, center, size, tile, top_tile=None, bottom_tile=None):
        cx, cy, cz = center
        sx, sy, sz = (s * 0.5 for s in size)
        b = self.add([(cx + x * sx, cy + y * sy, cz + z * sz) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)])
        # 0..3 아래(z-), 4..7 위(z+) — 순서: (x-,y-), (x+,y-), (x-,y+), (x+,y+)
        quads = [((0, 2, 3, 1), bottom_tile or tile), ((4, 5, 7, 6), top_tile or tile),
                 ((0, 1, 5, 4), tile), ((1, 3, 7, 5), tile), ((3, 2, 6, 7), tile), ((2, 0, 4, 6), tile)]
        for q, t in quads:
            self.face_proj([b + i for i in q], t)

    def beam(self, a, c, w, d, tile, sides=4):
        """a→c 기둥(단면 w×d, sides=3이면 삼각 기둥 — 줄)."""
        a, c = Vector(a), Vector(c)
        ax = (c - a).normalized()
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        r = ax.cross(ref).normalized()
        u = r.cross(ax).normalized()
        if sides == 3:
            offs = [r * (w * math.cos(k * 2 * math.pi / 3)) + u * (w * math.sin(k * 2 * math.pi / 3)) for k in range(3)]
        else:
            offs = [r * (w * 0.5) * sx + u * (d * 0.5) * sy for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        n = len(offs)
        b = self.add([a + o for o in offs] + [c + o for o in offs])
        for k in range(n):
            q = [b + k, b + (k + 1) % n, b + n + (k + 1) % n, b + n + k]
            self.face_proj(q, tile, margin=0.25)
        if sides == 4:
            self.face_proj([b + 3, b + 2, b + 1, b], tile)
            self.face_proj([b + n, b + n + 1, b + n + 2, b + n + 3], tile)

    def canopy(self, radius, skirt_z, tile):
        """둥근 갓: 천 조각 GORES장 × 2칸, 꼭대기 구멍. 끝단 점들(줄을 달 자리)을 돌려준다."""
        h = radius * CANOPY_H
        rings = []
        for ri, (rr, hh) in enumerate(PROFILE):
            pts = []
            for s in range(SEGS):
                ang = s * 2 * math.pi / SEGS
                bulge = BULGE if (s % 2 == 1 and ri > 0) else 1.0
                pts.append((math.cos(ang) * rr * radius * bulge, math.sin(ang) * rr * radius * bulge, skirt_z + (hh - PROFILE[-1][1]) * h))
            rings.append(self.add(pts))
        t0, s0, t1, s1 = tile
        nr = len(PROFILE)
        for ri in range(nr - 1):
            for s in range(SEGS):
                s2 = (s + 1) % SEGS
                idx = [rings[ri] + s, rings[ri + 1] + s, rings[ri + 1] + s2, rings[ri] + s2]
                uu = [s / SEGS, s / SEGS, (s + 1) / SEGS, (s + 1) / SEGS]
                vv = [1 - ri / (nr - 1), 1 - (ri + 1) / (nr - 1), 1 - (ri + 1) / (nr - 1), 1 - ri / (nr - 1)]
                uvs = [(t0 + (t1 - t0) * (0.01 + 0.98 * x), s0 + (s1 - s0) * (0.01 + 0.98 * y)) for x, y in zip(uu, vv)]
                self.face(idx, uvs, True)
        last = rings[-1]
        return [self.v[last + s].copy() for s in range(0, SEGS, SEGS // 8)]

    def build(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(p) for p in self.v], [], self.f)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        for p, uvs, sm in zip(me.polygons, self.uv, self.smooth):
            p.use_smooth = sm
            for li, uv in zip(p.loop_indices, uvs):
                uvl.data[li].uv = uv
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        return o


def make_crate():
    b = Builder()
    # 상자 1.6×1.3×1.6(가운데가 원점), 팔레트는 아래
    b.box((0, 0, 0), (1.6, 1.6, 1.3), CRATE_SIDE, top_tile=CRATE_TOP, bottom_tile=PALLET)
    b.box((0, 0, -0.68), (1.76, 1.76, 0.06), PALLET)
    for y in (-0.72, 0.0, 0.72):
        b.box((0, y, -0.77), (1.76, 0.14, 0.12), PALLET)
    hook = Vector((0, 0, 0.65 + 1.0))
    for x in (-0.78, 0.78):
        for y in (-0.78, 0.78):
            b.beam((x, y, 0.65), hook, 0.05, 0.05, LINE_S, sides=3)
    radius = 4.5
    skirt = b.canopy(radius, 0.65 + 1.0 + 0.62 * 9.0, CAN_S)
    for p in skirt:
        b.beam(p, hook, 0.045, 0.045, LINE_S, sides=3)
    return b.build("ACO_DropCrate")


def make_trooper():
    b = Builder()
    # 몸 가운데가 원점(발 -0.9, 정수리 +0.9) — y+ 앞
    for s in (-1, 1):
        x = 0.1 * s
        b.box((x, 0.03, -0.84), (0.13, 0.27, 0.12), BOOTS)
        b.beam((x, 0, -0.78), (x * 0.9, 0, -0.04), 0.15, 0.17, PANTS)
    b.box((0, 0, 0.22), (0.44, 0.25, 0.54), UNI)
    b.box((0, 0.17, 0.12), (0.30, 0.10, 0.18), RESERVE)      # 예비 낙하산(배)
    b.box((0, -0.24, 0.26), (0.38, 0.20, 0.50), PACK)        # 주 낙하산 배낭(등)
    b.box((0, 0.0, 0.54), (0.46, 0.27, 0.05), GEAR)          # 어깨 하네스 띠
    b.box((0, 0.01, 0.66), (0.19, 0.21, 0.22), SKIN)         # 머리
    b.box((0, 0.0, 0.79), (0.27, 0.30, 0.10), HELMET)        # 헬멧 몸통
    b.box((0, 0.0, 0.86), (0.19, 0.22, 0.06), HELMET)        # 헬멧 꼭대기
    b.box((0, -0.08, 0.69), (0.23, 0.08, 0.16), HELMET)      # 헬멧 뒷덮개(뒤통수)
    conf = {}
    for s in (-1, 1):
        sh = Vector((0.25 * s, 0, 0.45))
        el = Vector((0.31 * s, 0.02, 0.74))
        hand = Vector((0.24 * s, 0.03, 1.02))
        b.beam(sh, el, 0.10, 0.11, UNI)
        b.beam(el, hand, 0.09, 0.09, UNI)
        b.box(tuple(hand + Vector((0, 0, 0.04))), (0.08, 0.09, 0.10), GEAR)   # 장갑
        top = Vector((0.26 * s, 0.0, 1.62))
        b.beam(Vector((0.25 * s, 0, 0.5)), top, 0.035, 0.035, LINE_T, sides=3)   # 라이저(어깨 → 위)
        conf[s] = top
    radius = 3.5
    skirt = b.canopy(radius, 1.62 + 0.5 * 7.0, CAN_T)
    for p in skirt:
        s = 1 if p.x >= 0 else -1
        b.beam(p, conf[s], 0.035, 0.035, LINE_T, sides=3)
    return b.build("ACO_DropTrooper")


# ───────────── 텍스처 ─────────────

def rng_noise(h, w, seed, scale):
    g = np.random.default_rng(seed)
    small = g.random((max(h // scale, 2), max(w // scale, 2)))
    ys = np.linspace(0, small.shape[0] - 1, h)
    xs = np.linspace(0, small.shape[1] - 1, w)
    y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
    y1 = np.minimum(y0 + 1, small.shape[0] - 1); x1 = np.minimum(x0 + 1, small.shape[1] - 1)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    a = small[y0][:, x0]; b = small[y0][:, x1]; c = small[y1][:, x0]; d = small[y1][:, x1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def tile_px(t):
    u0, v0, u1, v1 = t
    return int(round(u0 * TEX)), int(round(v0 * TEX)), int(round(u1 * TEX)), int(round(v1 * TEX))


def paint_canopy(img, t, c0, c1, seam, seed):
    x0, y0, x1, y1 = tile_px(t)
    w, h = x1 - x0, y1 - y0
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    U, V = np.meshgrid(u, v)
    gore = np.floor(U * GORES).astype(int)
    col = np.where((gore % 2 == 0)[..., None], c0, c1).astype(np.float32)
    # 조각 가운데는 조금 밝게(부푼 천), 이음매는 짙은 선
    f = (U * GORES) % 1.0
    col *= (0.9 + 0.1 * np.sin(f * math.pi))[..., None]
    col[(f < 0.035) | (f > 0.965)] = seam
    # 가로 보강 띠(고리마다)와 끝단 단(아래 = v 0)
    ring = (V * (len(PROFILE) - 1)) % 1.0
    col[(ring < 0.025) & (V > 0.05)] *= 0.86
    col[V < 0.05] = col[V < 0.05] * 0.7 + seam * 0.3
    col *= (0.94 + 0.06 * rng_noise(h, w, seed, 16))[..., None]
    img[y0:y1, x0:x1] = col


def paint_wood(img, t, base, dark, planks, seed, straps=0):
    x0, y0, x1, y1 = tile_px(t)
    w, h = x1 - x0, y1 - y0
    v = (np.arange(h) + 0.5) / h
    u = (np.arange(w) + 0.5) / w
    U, V = np.meshgrid(u, v)
    grain = rng_noise(h, w * 4, seed, 3)[:, ::4]
    col = base * (0.88 + 0.16 * grain)[..., None]
    pv = (V * planks) % 1.0
    col[(pv < 0.04) | (pv > 0.97)] = dark
    pid = np.floor(V * planks)
    col *= (0.95 + 0.08 * np.sin(pid * 12.9898) * 0.5)[..., None]
    if straps:
        band = np.abs(U - 0.5) < 0.07
        col[band] = STRAP * (0.9 + 0.2 * grain[band])[..., None]
        if straps == 2:
            band2 = np.abs(V - 0.5) < 0.07
            col[band2] = STRAP * (0.9 + 0.2 * grain[band2])[..., None]
        # 끈 가장자리 박음질(밝은 점선)
        stitch = (np.abs(np.abs(U - 0.5) - 0.055) < 0.006) & ((V * 40) % 1.0 < 0.5)
        col[stitch] = STRAP * 1.8
    img[y0:y1, x0:x1] = col


def paint_flat(img, t, c, seed, camo=None, var=0.08):
    x0, y0, x1, y1 = tile_px(t)
    w, h = x1 - x0, y1 - y0
    n = rng_noise(h, w, seed, 6)
    col = np.tile(c, (h, w, 1)).astype(np.float32) * (1 - var + 2 * var * n)[..., None]
    if camo is not None:
        blot = rng_noise(h, w, seed + 7, 10) > 0.6
        col[blot] = camo * (0.95 + 0.1 * n[blot])[..., None]
    img[y0:y1, x0:x1] = col


def paint():
    img = np.zeros((TEX, TEX, 3), np.float32)
    img[:] = OLIVE_DK
    paint_canopy(img, CAN_S, WHITE, ORANGE, np.array((0.45, 0.42, 0.38)), 1)
    paint_canopy(img, CAN_T, OLIVE, OLIVE_DK, np.array((0.15, 0.17, 0.11)), 2)
    paint_wood(img, CRATE_SIDE, PLY, PLY_DK, 6, 3, straps=1)
    paint_wood(img, CRATE_TOP, PLY, PLY_DK, 6, 4, straps=2)
    paint_wood(img, PALLET, RAW, RAW * 0.6, 5, 5)
    paint_flat(img, LINE_S, np.array((0.86, 0.85, 0.82)), 6, var=0.03)
    paint_flat(img, LINE_T, np.array((0.42, 0.44, 0.36)), 7, var=0.03)
    paint_flat(img, UNI, np.array((0.36, 0.38, 0.26)), 8, camo=np.array((0.24, 0.26, 0.17)))
    paint_flat(img, PANTS, np.array((0.32, 0.34, 0.23)), 9, camo=np.array((0.22, 0.24, 0.16)))
    paint_flat(img, HELMET, np.array((0.27, 0.30, 0.20)), 10, var=0.05)
    paint_flat(img, SKIN, np.array((0.72, 0.55, 0.42)), 11, var=0.04)
    paint_flat(img, BOOTS, np.array((0.09, 0.08, 0.07)), 12, var=0.05)
    paint_flat(img, GEAR, np.array((0.40, 0.33, 0.22)), 13, var=0.06)
    paint_flat(img, PACK, np.array((0.30, 0.33, 0.21)), 14, var=0.06)
    paint_flat(img, RESERVE, np.array((0.27, 0.30, 0.19)), 15, var=0.06)
    im = bpy.data.images.new(NAME + "_Albedo", TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), np.float32)
    rgba[..., :3] = np.clip(img, 0, 1)
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = os.path.join(OUT, NAME + "_Albedo.png")
    im.file_format = 'PNG'
    im.save()
    return im


def material(objs, im):
    mat = bpy.data.materials.new(NAME)
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = im
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    mat.use_backface_culling = False
    for o in objs:
        o.data.materials.append(mat)


def write_fbx(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='OFF',
                             path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def preview(objs, path):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_backface_culling = False
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.3
    sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.45, 0.55, 0.68)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    crate, trooper = objs
    crate.location = (-6.0, 0, 0)
    trooper.location = (6.0, 0, 0)
    shots = {
        "both": ((0.0, -34.0, 6.0), (0.0, 0.0, 4.5)),
        "below": ((0.0, -20.0, -10.0), (0.0, 0.0, 4.0)),
        "trooper": ((9.0, -4.0, 0.6), (6.0, 0.0, 0.2)),
        "crate": ((-2.0, -5.0, 1.2), (-6.0, 0.0, -0.2)),
        "top": ((0.0, -6.0, 30.0), (0.0, 0.0, 6.0)),
    }
    for nm, (e, a) in shots.items():
        cam.location = Vector(e)
        cam.rotation_euler = (Vector(a) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = path + "_" + nm + ".png"
        bpy.ops.render.render(write_still=True)
    crate.location = (0, 0, 0)
    trooper.location = (0, 0, 0)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    crate = make_crate()
    trooper = make_trooper()
    im = paint()
    material((crate, trooper), im)
    for o in (crate, trooper):
        print("TRIS", o.name, sum(len(p.vertices) - 2 for p in o.data.polygons))
    if os.environ.get("ACO_AIRDROP_PREVIEW"):
        preview((crate, trooper), os.environ["ACO_AIRDROP_PREVIEW"])
    write_fbx((crate, trooper))
    print("DONE")
