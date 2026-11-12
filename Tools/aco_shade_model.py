"""SHADE 기체(6-136 SHADE 편대) — 로우폴리 Su-57(LowPolyMiliteryVehicles/Models/Su57.fbx)을 읽어 다듬은 사본. 원본 에셋은 건드리지 않는다.
 - 형태: 실루엣·크기·원점은 원본 그대로(히트박스·코브라 발사 자리가 그대로 맞게). 곡면은 각도(SMOOTH_ANGLE 55도)로 부드럽게 + 가중 노멀(큰 면 기준 그늘),
   날개 앞전·끝 같은 얇은 판 모서리와 유리·노즐 안·흡입구 경계는 날카롭게.
   노즐 끝판을 안으로 밀어 넣어 깊이(안쪽 벽 + 뜨거운 바닥), 흡입구 입구를 안으로 밀어 넣어 깊이(검은 안쪽). 캐노피는 유리 재질(두 번째 재질 칸).
 - UV: 새로 펼친다(Smart UV — 섬끼리 여백). 텍스처 2048 한 장에 직접 칠한다(UV 삼각형마다 3D 위치·면 방향으로 색을 정해 래스터).
 - 도장: 무광 검정 바탕(아래는 조금 밝게, 아주 옅은 얼룩), 패널 라인·점검창, 날개·미익 앞전은 더 짙게, 노란 강조(날개·수평미익 끝 띠,
   수직미익 끝, 기수 옆 줄, 날개·미익 윤곽 모서리의 노란 가는 줄 — 실루엣이 드러나게), 동체 꺾인 모서리의 밝은 회색 선, 수직미익 바깥 면에 SHADE 엠블럼(노란 초승달 — 가려진 달), 노즐은 그을린 금속·안쪽은 뜨거운 색, 흡입구 안은 검정, 캐노피는 어두운 금빛.
좌표: 원본 FBX를 읽은 블렌더 좌표 그대로(x 옆, y 뒤 = 꼬리, z 위). 내보내기도 원본과 같은 기본 설정(축 -Z 앞·Y 위, 공간 변환 굽지 않음) —
유니티에서 원본과 같은 축으로 들어온다(Setup Check가 원본 메시 자리에 맞춰 바꿔 끼운다).
실행: blender --background --factory-startup --python Tools/aco_shade_model.py   (ACO_SHADE_PREVIEW=경로접두 → 미리보기 렌더, ACO_SHADE_DEBUG=1 → 부위 색)"""
import math
import os
import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Assets", "LowPolyMiliteryVehicles", "Models", "Su57.fbx")
OUT = os.environ.get("ACO_SHADE_OUT", os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Shade"))
NAME = "ACO_Shade"
TEX = 2048
SMOOTH_ANGLE = 55.0     # 이보다 완만하면 부드럽게(곡면) — 날개 앞전·끝처럼 얇은 판의 모서리는 훨씬 커서 그대로 날카롭다
OUTLINE_ANGLE = 60.0    # 이보다 꺾인 판(날개·미익) 모서리에 노란 가는 줄
CHINE_ANGLE = 38.0      # 이보다 꺾인 동체 모서리에 밝은 회색 선
# 캐노피 쪽 기수 윗면(nose_top): 모서리 없이 전부 부드럽게(Shade Smooth — 캐노피와의 경계도), 회색 선(패널 라인·점검창·모서리 선) 없음


def nose_top(c):
    """둥글게 보일 곳: 캐노피 쪽 기수 윗면 + 캐노피 뒤 등과 기수가 이어지는 곳(날개 뿌리 LERX 바깥은 빼서 그 모서리는 날카롭게)."""
    return (c.y < -2.6 and c.z > 0.2) or (c.y < 0.5 and c.z > 0.25 and abs(c.x) < 1.3)

# 부위
T_BODY, T_WING, T_FIN, T_TAIL, T_CANOPY, T_NOZ, T_NOZ_IN, T_HOT, T_INTAKE = range(9)
HARD = (T_CANOPY, T_NOZ, T_NOZ_IN, T_HOT, T_INTAKE)
FLAT = (T_WING, T_TAIL, T_FIN)
DEBUG_COL = [(0.5, 0.5, 0.5), (0.3, 0.5, 0.9), (0.9, 0.5, 0.2), (0.2, 0.8, 0.8), (0.9, 0.9, 0.2), (0.6, 0.3, 0.1), (0.3, 0.1, 0.1), (1, 0.2, 0.1), (0.1, 0.9, 0.1)]

# 색
BLACK = np.array((0.165, 0.165, 0.18))
BLACK_LOW = np.array((0.205, 0.205, 0.22))
LINE = np.array((0.33, 0.33, 0.35))
CHINE = np.array((0.46, 0.46, 0.48))
EDGE = np.array((0.11, 0.11, 0.12))
YELLOW = np.array((0.96, 0.74, 0.08))
GLASS = np.array((0.11, 0.085, 0.03))
FRAME = np.array((0.03, 0.03, 0.033))
NOZ = np.array((0.20, 0.18, 0.16))
NOZ_IN = np.array((0.05, 0.045, 0.045))
HOT = np.array((0.95, 0.42, 0.08))
INTAKE = np.array((0.008, 0.008, 0.01))

ENGINES = [(-1.2, 0.12), (1.2, 0.12)]   # 노즐 축(x, z)
NOZ_END = 7.1                           # 이보다 뒤가 노즐
NOZ_R = 0.56
CANOPY_Y = (-5.8, -3.24)
CANOPY_Z = 0.86


def import_src():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    o = [x for x in bpy.context.scene.objects if x.type == 'MESH'][0]
    o.name = NAME
    o.data.name = NAME
    return o


def engine_dist(c):
    return min(math.hypot(c.x - ex, c.z - ez) for ex, ez in ENGINES)


def classify(f, wing_x):
    """면 하나의 부위."""
    c = f.calc_center_median()
    n = f.normal
    sx = abs(c.x)
    if c.y > 6.95 and engine_dist(c) < NOZ_R:
        # 노즐(원본도 속이 빈 관 — 바깥 7.0~7.6, 뒤 테두리 ~7.8, 안쪽 바닥 7.25)
        ex, ez = ENGINES[0] if c.x < 0 else ENGINES[1]
        rx, rz = c.x - ex, c.z - ez
        if n.y > 0.7 and c.y < 7.5 and engine_dist(c) < 0.4:
            return T_HOT
        if n.y < 0.7 and (n.x * rx + n.z * rz) < 0.0:
            return T_NOZ_IN
        return T_NOZ
    if CANOPY_Y[0] < c.y < CANOPY_Y[1] and c.z > 0.66 and sx < 0.40 and n.z > -0.05:
        return T_CANOPY
    if c.y > 4.6 and c.z > 0.5 and 0.9 < sx < 3.4 and abs(n.z) < 0.6:
        return T_FIN
    if n.y < -0.3 and n.z < -0.75 and c.z < 0.0 and 0.5 < sx < 1.8 and -1.6 < c.y < 0.2:
        return T_INTAKE
    if c.y > 6.1 and sx > 1.75 and c.z < 0.75:
        return T_TAIL
    if sx > wing_x:
        return T_WING
    return T_BODY


def build(o):
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bm.normal_update()
    fin_top = max(v.co.z for v in bm.verts)
    wing_x = 1.85
    tags = {f: classify(f, wing_x) for f in bm.faces}
    # 재질: 0 몸체, 1 유리
    for f in bm.faces:
        f.material_index = 1 if tags[f] == T_CANOPY else 0
        f.smooth = True
    # 부드럽게: 면 사이 각도가 SMOOTH_ANGLE보다 크거나 부위가 다르면 날카로운 모서리
    lim = math.radians(SMOOTH_ANGLE)
    for e in bm.edges:
        if len(e.link_faces) != 2:
            e.smooth = False
            continue
        a, b = e.link_faces
        # 유리·노즐 안·흡입구처럼 재질이 다른 부위와의 경계만 늘 날카롭게(동체↔날개 같은 경계는 각도로)
        hard = (tags[a] != tags[b]) and (tags[a] in HARD or tags[b] in HARD)
        if nose_top(a.calc_center_median()) and nose_top(b.calc_center_median()):
            # 캐노피 쪽 기수 윗면: 캐노피와의 경계까지 모두 부드럽게
            e.smooth = True
            continue
        e.smooth = a.normal.angle(b.normal, 0.0) < lim and not hard
    bm.faces.ensure_lookup_table()
    tag_list = [tags[f] for f in bm.faces]
    bm.to_mesh(me)
    bm.free()
    me.update()
    return tag_list, fin_top


def unwrap(o):
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    me = o.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(55.0), island_margin=0.006, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.006, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')


# ───────────── 도장 ─────────────

def hash3(p):
    """격자 점의 의사 난수(0~1)."""
    h = np.sin(p[..., 0] * 127.1 + p[..., 1] * 311.7 + p[..., 2] * 74.7) * 43758.5453
    return h - np.floor(h)


def vnoise(P, scale):
    q = P * scale
    i = np.floor(q)
    f = q - i
    f = f * f * (3 - 2 * f)
    acc = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                acc = acc + w * hash3(i + np.array((dx, dy, dz)))
    return acc


def near(v, stations, w):
    d = np.full(v.shape, 1e9)
    for s in stations:
        d = np.minimum(d, np.abs(v - s))
    return d < w


class Painter:
    def __init__(self, fin_top, fin_info):
        self.fin_top = fin_top
        self.fin_info = fin_info   # 수직미익 바깥 면 엠블럼 중심(부호별 (y, z))

    @staticmethod
    def seg_dist(P, segs):
        """점들에서 선분들까지 가장 가까운 거리."""
        d = np.full(len(P), 1e9)
        for a, b in segs:
            ab = b - a
            t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-12), 0.0, 1.0)
            q = a + t[:, None] * ab
            d = np.minimum(d, np.linalg.norm(P - q, axis=1))
        return d

    def color(self, P, n, tag, ysegs=(), gsegs=()):
        c = self.base_color(P, n, tag)
        if ysegs:
            c[self.seg_dist(P, ysegs) < 0.045] = YELLOW     # 날개·미익 윤곽의 노란 가는 줄
        if gsegs:
            c[self.seg_dist(P, gsegs) < 0.028] = CHINE      # 동체 꺾인 모서리의 밝은 선
        return c

    def base_color(self, P, n, tag):
        k = len(P)
        x, y, z = P[:, 0], P[:, 1], P[:, 2]
        sx = np.abs(x)
        if tag == T_CANOPY:
            c = np.tile(GLASS, (k, 1))
            # 틀: 캐노피 앞뒤 끝·가운데 틀
            frame = near(y, (CANOPY_Y[0] + 0.25, -4.55, CANOPY_Y[1] - 0.2), 0.035)
            c[frame] = FRAME
            return c
        if tag == T_HOT:
            r = np.array([min(math.hypot(px - ex, pz - ez) for ex, ez in ENGINES) for px, pz in zip(x, z)])
            t = np.clip(r / 0.3, 0, 1)[:, None]
            return HOT * (1.0 - 0.55 * t) + NOZ_IN * 0.55 * t
        if tag == T_NOZ_IN:
            t = np.clip((y - (NOZ_END - 0.6)) / 0.7, 0, 1)[:, None]   # 안쪽(앞)일수록 붉게 달아오름
            return NOZ_IN * t + np.array((0.35, 0.12, 0.05)) * (1 - t)
        if tag == T_INTAKE:
            return np.tile(INTAKE, (k, 1))
        if tag == T_NOZ:
            g = (0.85 + 0.3 * vnoise(P, 6.0))[:, None]
            c = NOZ * g
            # 꽃잎 줄(노즐 둘레 판)
            ang = np.array([math.atan2(pz - ez, px - ex) for px, pz, (ex, ez) in zip(x, z, [ENGINES[0] if px < 0 else ENGINES[1] for px in x])])
            petal = np.abs(((ang / (2 * math.pi) * 16) % 1.0) - 0.5) > 0.45
            c[petal] *= 0.55
            return c
        # 바탕
        low = (n[:, 2] < -0.35)
        base = np.where(low, 1, 0)[:, None] * BLACK_LOW + np.where(low, 0, 1)[:, None] * BLACK
        mott = (vnoise(P, 0.6) - 0.5) * 0.03 + (vnoise(P, 3.0) - 0.5) * 0.012
        # 판마다 조금씩 다른 톤(도장 보수 흔적)
        cell = np.stack((np.floor(y / 1.5), np.floor(np.abs(x) / 1.1), np.floor(z / 0.8)), 1)
        tone = (hash3(cell) - 0.5) * 0.022
        c = base + (mott + tone)[:, None]
        w = 0.022
        lines = np.zeros(k, bool)
        if tag == T_BODY:
            lines |= near(y, (-7.1, -6.0, -3.25, -2.1, -0.6, 0.9, 2.4, 3.9, 5.3, 6.4), w)
            lines |= (z > 0.45) & near(sx, (0.42,), w)
            lines |= (np.abs(n[:, 0]) > 0.55) & near(z, (0.05,), w)
            # 점검창(윤곽만)
            for (y0, y1, x0, x1) in ((-2.9, -2.3, 0.15, 0.38), (0.2, 0.8, 0.5, 0.85), (1.4, 2.1, 0.15, 0.4), (3.0, 3.6, 0.55, 0.95)):
                inside = (y > y0) & (y < y1) & (sx > x0) & (sx < x1) & (n[:, 2] > 0.3)
                edge = inside & ((np.abs(y - y0) < w) | (np.abs(y - y1) < w) | (np.abs(sx - x0) < w) | (np.abs(sx - x1) < w))
                lines |= edge
            # 기수 옆 노란 줄(기수 쪽 옆면)
            stripe = (y > -7.4) & (y < -5.75) & (np.abs(n[:, 0]) > 0.45) & (np.abs(z - (0.16 + (y + 7.4) * 0.09)) < 0.035)
            lines &= ~(y < -2.6)   # 캐노피 쪽 기수(앞 동체)엔 패널 라인 없음 — 잘린 토막이 남지 않게 앞 동체 전체
            c[lines] = LINE
            c[stripe] = YELLOW
            return c
        if tag == T_WING:
            lines |= near(sx, (2.6, 3.6, 4.6, 5.4), w)
            lines |= near(y, (4.1,), w) & (sx > 2.4)
            edge = n[:, 1] < -0.45
            c[lines] = LINE
            c[edge] = EDGE
            tip = sx > 5.62
            c[tip] = YELLOW
            return c
        if tag == T_TAIL:
            lines |= near(sx, (2.6, 3.4), w)
            edge = n[:, 1] < -0.45
            c[lines] = LINE
            c[edge] = EDGE
            c[sx > 3.9] = YELLOW
            return c
        if tag == T_FIN:
            lines |= near(z, (1.1, 1.6), w)
            edge = n[:, 1] < -0.45
            c[lines] = LINE
            c[edge] = EDGE
            c[z > self.fin_top - 0.3] = YELLOW
            # 엠블럼: 바깥 면(면 방향 x 부호 = 위치 x 부호)에 노란 초승달
            outer = np.sign(n[:, 0]) == np.sign(x)
            for s, (ey, ez) in self.fin_info.items():
                m = outer & (np.sign(x) == s)
                if not m.any():
                    continue
                dy = y - ey
                dz = z - ez
                r1 = np.hypot(dy, dz)
                r2 = np.hypot(dy - 0.11, dz + 0.06)
                cres = m & (r1 < 0.27) & (r2 > 0.23)
                ring = m & (np.abs(r1 - 0.34) < 0.022)
                c[cres | ring] = YELLOW
            return c
        return c


def paint(o, tags, painter, debug):
    me = o.data
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    # 모서리마다 붙은 면 → 윤곽(노란 줄)·꺾인 모서리(회색 선)
    edge_faces = {}
    for p in me.polygons:
        for k in p.edge_keys:
            edge_faces.setdefault(k, []).append(p.index)
    ysegs = {}
    gsegs = {}
    co = [np.array(v.co[:]) for v in me.vertices]
    for k, fs in edge_faces.items():
        seg = (co[k[0]], co[k[1]])
        if len(fs) == 1:
            ang = 180.0
        else:
            ang = math.degrees(me.polygons[fs[0]].normal.angle(me.polygons[fs[1]].normal, 0.0))
        ft = [tags[f] for f in fs]
        if all(t in FLAT for t in ft) and ang > OUTLINE_ANGLE:
            for f in fs:
                ysegs.setdefault(f, []).append(seg)
        elif any(t == T_BODY for t in ft) and all(t in (T_BODY, T_WING, T_TAIL, T_FIN) for t in ft) and ang > CHINE_ANGLE                 and not all(nose_top(me.polygons[f].center) for f in fs):
            for f in fs:
                gsegs.setdefault(f, []).append(seg)
    img = np.zeros((TEX, TEX, 3), np.float32)
    filled = np.zeros((TEX, TEX), bool)
    for tri in me.loop_triangles:
        tag = tags[tri.polygon_index]
        n = np.array(me.polygons[tri.polygon_index].normal[:])
        U = np.array([uv[l].uv[:] for l in tri.loops]) * TEX - 0.5
        P = np.array([me.vertices[v].co[:] for v in tri.vertices])
        x0, y0 = np.floor(U.min(0)).astype(int) - 1
        x1, y1 = np.ceil(U.max(0)).astype(int) + 1
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, TEX - 1), min(y1, TEX - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        px = gx.ravel().astype(np.float64)
        py = gy.ravel().astype(np.float64)
        (ax, ay), (bx, by), (cx, cy) = U
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12:
            continue
        l0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den
        l1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den
        l2 = 1 - l0 - l1
        eps = -0.02
        m = (l0 >= eps) & (l1 >= eps) & (l2 >= eps)
        if not m.any():
            continue
        L = np.stack((l0[m], l1[m], l2[m]), 1)
        Pw = L @ P
        if debug:
            col = np.tile(np.array(DEBUG_COL[tag]), (len(Pw), 1))
        else:
            pi = tri.polygon_index
            col = painter.color(Pw, np.tile(n, (len(Pw), 1)), tag, ysegs.get(pi, ()), gsegs.get(pi, ()))
        ix = px[m].astype(int)
        iy = py[m].astype(int)
        img[iy, ix] = col
        filled[iy, ix] = True
    # 섬 둘레 번짐(밉맵·이음매)
    for _ in range(10):
        if filled.all():
            break
        acc = np.zeros_like(img)
        cnt = np.zeros(filled.shape, np.float32)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sf = np.roll(filled, (dy, dx), (0, 1))
            si = np.roll(img, (dy, dx), (0, 1))
            acc += si * sf[..., None]
            cnt += sf
        grow = (~filled) & (cnt > 0)
        img[grow] = acc[grow] / cnt[grow][:, None]
        filled |= grow
    img[~filled] = BLACK
    im = bpy.data.images.new(NAME + "_Albedo", TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), np.float32)
    rgba[..., :3] = np.clip(img, 0, 1)
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = os.path.join(OUT, NAME + "_Albedo.png")
    im.file_format = 'PNG'
    im.save()
    return im


def fin_centers(o, tags):
    me = o.data
    out = {}
    for s in (-1, 1):
        pts = []
        for p in me.polygons:
            if tags[p.index] != T_FIN:
                continue
            c = p.center
            if np.sign(c.x) == s and np.sign(p.normal.x) == s:
                pts.append((c.y, c.z, p.area))
        if pts:
            a = np.array(pts)
            w = a[:, 2] / a[:, 2].sum()
            out[s] = (float((a[:, 0] * w).sum()) + 0.25, float((a[:, 1] * w).sum()))
    return out


def materials(o, im):
    me = o.data
    me.materials.clear()
    for nm in ("ACO_Shade_Body", "ACO_Shade_Glass"):
        mat = bpy.data.materials.new(nm)
        mat.use_nodes = True
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = im
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        me.materials.append(mat)


def write_fbx(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='OFF',
                             path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)


def preview(o, path):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_cavity = True
    sc.display.shading.cavity_type = 'BOTH'
    sc.display.shading.show_shadows = True
    sc.display.shading.shadow_intensity = 0.3
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.42, 0.5, 0.6)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    shots = {
        "front34": ((11.0, -14.0, 6.0), (0.0, 0.0, 0.3)),
        "rear34": ((-10.0, 15.0, 6.5), (0.0, 1.0, 0.4)),
        "top": ((0.0, 0.4, 30.0), (0.0, 0.4, 0.0)),
        "side": ((24.0, 0.4, 0.6), (0.0, 0.4, 0.6)),
        "below": ((8.0, -6.0, -14.0), (0.0, 0.0, 0.0)),
        "nozzle": ((2.2, 13.0, 1.6), (0.0, 7.2, 0.2)),
        "fin": ((7.5, 9.5, 3.0), (2.0, 5.8, 1.4)),
        "nose": ((4.5, -10.5, 3.6), (0.0, -4.6, 0.7)),
        "nose_side": ((6.5, -4.8, 1.6), (0.0, -4.8, 0.6)),
        "shoulder": ((6.0, -9.0, 3.4), (0.4, -3.4, 0.5)),
    }
    for nm, (e, a) in shots.items():
        cam.location = Vector(e)
        cam.rotation_euler = (Vector(a) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = path + "_" + nm + ".png"
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    debug = bool(os.environ.get("ACO_SHADE_DEBUG"))
    o = import_src()
    tags, fin_top = build(o)
    wn = o.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL')
    wn.mode = 'FACE_AREA'
    wn.weight = 50
    wn.keep_sharp = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=wn.name)
    unwrap(o)
    painter = Painter(fin_top, fin_centers(o, tags))
    im = paint(o, tags, painter, debug)
    materials(o, im)
    print("TRIS", sum(len(p.vertices) - 2 for p in o.data.polygons), "FIN", painter.fin_info, "TOP", fin_top,
          "TAGS", {t: tags.count(t) for t in range(9)})
    if not debug:
        write_fbx(o)
    if os.environ.get("ACO_SHADE_PREVIEW"):
        preview(o, os.environ["ACO_SHADE_PREVIEW"])
    print("DONE")
