"""터널 랜드마크 모델(6-266): 같은 높이 육각 칸 1 · 2 · 3개(일렬) 윗면을 덮는 바위산 · 능선 + 관통 터널(폭 50m x 높이 40m 아치 — 약 0.65 · 1.35 · 2.0km)
+ 입구 포털 + 바닥 도로 + 유도등. 지형 장식이 놓을 수 있는 가장 긴 것을 고른다.
Blender 안에서 돌린다: blender --background --factory-startup --python Tools/aco_tunnel_model.py
만드는 것: Assets/ACO_Addon/Models/Tunnel/ACO_Tunnel.fbx (메시 이름 = "프리팹__재질": ACO_Tunnel(1칸) · ACO_Tunnel2 · ACO_Tunnel3 × rock, concrete, asphalt, glow).
좌표는 Unity 축(x, y 위, z), 단위 m, 원점 = 칸 윗면 가운데, 터널 축 = +Z(ACO_TerrainFeatures가 일렬 방향으로 돌린다).
육각 칸(반지름 400m)은 이웃 방향(= 터널 축)으로 평평한 변이 오고 꼭짓점이 +X 축에 있다 — 바닥은 세 칸을 이은 모양에서 가장자리 MARGIN만큼 안쪽(옆 칸 위로 튀어나오지 않게).
바위산 겉면은 Unity에서 지형 머티리얼(ACO/HexTerrain — 월드 좌표로 색을 정한다)을 써서 기둥과 이어 보인다.
내보낼 때 장식 모델(aco_deco_models.py)과 같이 (x, y, z) → Blender (-x, -z, y)로 바꿔 FBX를 거친 뒤 Unity에서 같은 좌표가 되게 한다."""
import math
import os
import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
MODEL_DIR = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Tunnel")
FBX_NAME = "ACO_Tunnel.fbx"
NAME = "ACO_Tunnel"

CELL_R = 400.0            # 지형 칸 반지름(ACO_HexTerrain.HexRadius)
CELLS = 3                 # 산이 덮는 칸 수(일렬, 가운데 칸 = 원점)
CELL_APOTHEM = CELL_R * math.cos(math.radians(30))   # 346.4 — 칸 간격은 그 두 배
MARGIN = 20.0             # 칸 가장자리에서 안쪽으로
HALF_LEN = CELL_APOTHEM * CELLS - MARGIN   # 산 반길이(터널 입구 쪽) ≈ 1019m
APOTHEM = HALF_LEN        # 아래 코드가 쓰는 이름(입구까지 거리)
BASE_Y = -3.0             # 바닥(칸 윗면보다 조금 아래 — 이음매 안 보이게)
PEAK = 140.0              # 산 높이(사용자 "꼭대기를 좀 낮추고" — 200 → 140)
BORE_W = 50.0             # 터널 폭
BORE_H = 40.0             # 터널 높이(아치 꼭대기)
WALL_H = BORE_H - BORE_W * 0.5   # 수직 벽 높이(15m) 위로 반원 아치
PORTAL_HALF = 80.0        # 입구 절벽 구간 반폭(터널 축 양옆)
PORTAL_BLEND = 35.0
PORTAL_H = 90.0           # 입구 절벽 위 산 높이
PORTAL_RAMP = 45.0        # 평평한 변에서 절벽이 다 서는 거리
FRAME_T = 4.0             # 포털 두께(아치 둘레)
FRAME_Z1 = HALF_LEN - 12.0          # 포털 앞(입구 쪽 |z|)
FRAME_Z0 = FRAME_Z1 - 14.0          # 포털 뒤
ROWS = 72                 # 길이 방향 격자
COLS = 18                 # 폭 방향 격자


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def hash01(i, j):
    h = (i * 73856093) ^ (j * 19349663) ^ 0x5bd1e995
    h = (h * 1103515245 + 12345) & 0x7fffffff
    return (h % 10007) / 10006.0


def half_width(z):
    """세 칸을 이은 바닥의 반폭(z에서): 칸마다 꼭짓점(+X, 400m)에서 평평한 변 쪽으로 30도씩 좁아지는 육각 — 가장 넓은 칸 값 - MARGIN."""
    best = 0.0
    for c in range(CELLS):
        zc = (c - (CELLS - 1) * 0.5) * CELL_APOTHEM * 2.0
        dz = abs(z - zc)
        if dz <= CELL_APOTHEM:
            best = max(best, CELL_R - dz * math.tan(math.radians(30)))
    return max(best - MARGIN, 40.0)


CURVE = 0.0   # 곡선형(6-266 3판): 터널 가운데가 +X 쪽으로 휘는 폭(m). 0이면 직선. 한쪽 활(포물선 ≈ 원호), 양 끝은 x = 0


def center_x(z):
    """터널 중심선의 x. 양 끝 밖(포털 앞 절단용 연장)은 끝의 접선 방향으로 곧게."""
    if CURVE == 0.0:
        return 0.0
    zz = max(-HALF_LEN, min(HALF_LEN, z))
    x = CURVE * (1.0 - (zz / HALF_LEN) ** 2)
    if abs(z) > HALF_LEN:
        x += center_slope(zz) * (z - zz)
    return x


def center_slope(z):
    """dx/dz(양 끝 밖은 끝 값)."""
    zz = max(-HALF_LEN, min(HALF_LEN, z))
    return -2.0 * CURVE * zz / (HALF_LEN ** 2)


def height(x, z, u, row, col):
    """u = 폭 방향 -1~1(가장자리 ±1). 능선 등줄기는 터널 중심선(center_x)을 따라 휜다."""
    if row == 0 or row == ROWS or col == 0 or col == COLS:
        return BASE_Y
    xc = center_x(z)
    w = half_width(z)
    if x >= xc:
        s = 1.0 - (x - xc) / max(w - xc, 1.0)
    else:
        s = 1.0 - (xc - x) / max(w + xc, 1.0)
    s = max(0.0, min(1.0, s))
    along = smooth(0.0, 1.0, (HALF_LEN - abs(z)) / 260.0)
    body = PEAK * (smooth(0.0, 0.85, s) ** 1.2) * along
    wx = 1.0 - smooth(PORTAL_HALF, PORTAL_HALF + PORTAL_BLEND, abs(x - xc))
    fz = max(0.0, min(1.0, (HALF_LEN - abs(z)) / PORTAL_RAMP))
    h = max(body, PORTAL_H * fz * wx)
    if 1 < row < ROWS - 1 and 1 < col < COLS - 1:
        h *= 0.9 + 0.2 * hash01(row, col)
    return max(h, BASE_Y)


def mountain_mesh():
    """바닥이 막힌 닫힌 능선(불리언용): 길이(z) x 폭(u) 격자, 가장자리 정점은 위아래가 같은 정점."""
    bm = bmesh.new()
    top = [[None] * (COLS + 1) for _ in range(ROWS + 1)]
    bot = [[None] * (COLS + 1) for _ in range(ROWS + 1)]
    for i in range(ROWS + 1):
        # 입구 쪽(양 끝)으로 갈수록 촘촘하게 — 절벽이 각지게 서도록
        t = i / ROWS * 2.0 - 1.0
        z = math.copysign(abs(t) ** 0.8, t) * HALF_LEN
        w = half_width(z)
        for j in range(COLS + 1):
            u = j / COLS * 2.0 - 1.0
            x = u * w
            border = i == 0 or i == ROWS or j == 0 or j == COLS
            v = bm.verts.new((x, height(x, z, u, i, j), z))
            top[i][j] = v
            bot[i][j] = v if border else bm.verts.new((x, BASE_Y, z))
    for i in range(ROWS):
        for j in range(COLS):
            bm.faces.new((top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j]))
            bm.faces.new((bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.01)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=0.01)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def arch_profile(half_w, wall_h, y0, n=16):
    pts = [(-half_w, y0), (half_w, y0), (half_w, wall_h)]
    for i in range(1, n):
        a = math.pi * i / n
        pts.append((half_w * math.cos(a), wall_h + half_w * math.sin(a)))
    pts.append((-half_w, wall_h))
    return pts


def prism(profile, z0, z1):
    bm = bmesh.new()
    front = [bm.verts.new((x, y, z0)) for x, y in profile]
    back = [bm.verts.new((x, y, z1)) for x, y in profile]
    n = len(profile)
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(n):
        i2 = (i + 1) % n
        bm.faces.new((front[i], front[i2], back[i2], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def sweep_into(bm, profile, z0, z1, n):
    """단면(profile — (가로, 높이), 닫힌 다각형)을 중심선을 따라 z0 → z1로 n칸 끌어 닫힌 입체를 bm에 더한다. 가로 = 진행 방향에 수직인 수평축."""
    rings = []
    for i in range(n + 1):
        z = z0 + (z1 - z0) * i / n
        cx = center_x(z)
        sl = center_slope(z) if abs(z) <= HALF_LEN else center_slope(math.copysign(HALF_LEN, z))
        inv = 1.0 / math.sqrt(1.0 + sl * sl)
        tx, tz = sl * inv, inv            # 진행 방향(x, z)
        lx, lz = tz, -tx                  # 가로(+X 쪽)
        rings.append([bm.verts.new((cx + lx * px, py, z + lz * px)) for px, py in profile])
    m = len(profile)
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    for i in range(n):
        a, b = rings[i], rings[i + 1]
        for k in range(m):
            k2 = (k + 1) % m
            bm.faces.new((a[k], a[k2], b[k2], b[k]))


def sweep(profile, z0, z1, n):
    bm = bmesh.new()
    sweep_into(bm, profile, z0, z1, n)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def steps(z0, z1, step=20.0):
    """곡선형이면 step(m)마다 한 칸, 직선이면 한 칸."""
    return max(1, int(math.ceil(abs(z1 - z0) / step))) if CURVE != 0.0 else 1


def rect(x0, x1, y0, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def box(bm, x0, x1, y0, y1, z0, z1):
    v = [bm.verts.new((x, y, z)) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]
    # v index = xi*4 + yi*2 + zi
    quads = ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3))
    return [bm.faces.new([v[i] for i in q]) for q in quads]


def to_object(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def difference(target, cutter):
    mod = target.modifiers.new("cut", 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'
    mod.object = cutter
    bpy.context.view_layer.objects.active = target
    target.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    target.select_set(False)
    bpy.data.objects.remove(cutter, do_unlink=True)


def in_bore(c):
    """면 가운데가 터널 안쪽 면인가(Unity 좌표)."""
    x, y, z = c
    x = (x - center_x(z)) / math.sqrt(1.0 + center_slope(z) ** 2)   # 중심선에서의 가로 거리
    if abs(z) > APOTHEM + 1.0 or y > BORE_H + 0.5 or abs(x) > BORE_W * 0.5 + 0.5:
        return False
    if y <= WALL_H:
        return True
    r = math.hypot(x, y - WALL_H)
    return r <= BORE_W * 0.5 + 0.5


def split(ob, rock_name, concrete_name):
    """불리언 결과를 터널 안쪽(콘크리트)과 겉면(바위)으로 나눈다."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    inner = [f for f in bm.faces if in_bore(f.calc_center_median())]
    outer = [f for f in bm.faces if f not in set(inner)]
    parts = []
    for faces, name in ((outer, rock_name), (inner, concrete_name)):
        nb = bm.copy()
        keep = set(f.index for f in faces)
        nb.faces.ensure_lookup_table()
        bmesh.ops.delete(nb, geom=[f for f in nb.faces if f.index not in keep], context='FACES')
        parts.append(to_object(nb, name))
    bm.free()
    bpy.data.objects.remove(ob, do_unlink=True)
    return parts


def to_unity_export(ob):
    """(x, y, z) → Blender (-x, -z, y). 반사 변환이라 면을 뒤집어 바깥 방향을 지킨다."""
    for v in ob.data.vertices:
        x, y, z = v.co
        v.co = Vector((-x, -z, y))
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def set_cells(n, curve=0.0):
    """칸 수 · 휨 폭에 따라 길이 관련 값을 바꾼다(원점 = 덮는 칸들의 가운데 — 2칸이면 두 칸 사이 변의 가운데)."""
    global CELLS, HALF_LEN, APOTHEM, FRAME_Z1, FRAME_Z0, ROWS, CURVE
    CELLS = n
    CURVE = curve
    HALF_LEN = CELL_APOTHEM * CELLS - MARGIN
    APOTHEM = HALF_LEN
    FRAME_Z1 = HALF_LEN - 12.0
    FRAME_Z0 = FRAME_Z1 - 14.0
    ROWS = 24 * CELLS


# (프리팹 이름, 덮는 칸 수, 휨 폭 m) — 1칸은 처음 만든 이름 그대로(씬 참조 유지).
# 곡선형(3판, 한쪽 활): 2칸 50m = 입구 가운데서 반대편 출구가 안 보임(능선 허리가 좁아 더 못 휜다), 3칸 130m = 입구 가운데서 터널 한가운데 물체가 안 보임.
VARIANTS = (("ACO_Tunnel", 1, 0.0), ("ACO_Tunnel2", 2, 0.0), ("ACO_Tunnel3", 3, 0.0), ("ACO_Tunnel2C", 2, 50.0), ("ACO_Tunnel3C", 3, 130.0))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(MODEL_DIR, exist_ok=True)
    for name, cells, curve in VARIANTS:
        set_cells(cells, curve)
        build(name)
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.export_scene.fbx(filepath=os.path.join(MODEL_DIR, FBX_NAME), object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_triangles=True, path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    print("DONE")


def build(NAME):
    print("VARIANT", NAME, "cells", CELLS, "curve", CURVE, "length", round(HALF_LEN * 2))
    # 산 - 터널(중심선을 따라 끈 아치)
    mountain = to_object(mountain_mesh(), "mountain")
    z0, z1 = -APOTHEM - 60.0, APOTHEM + 60.0
    bore = to_object(sweep(arch_profile(BORE_W * 0.5, WALL_H, BASE_Y - 3.0), z0, z1, steps(z0, z1)), "bore")
    difference(mountain, bore)
    rock, inner = split(mountain, NAME + "__rock", NAME + "__concrete_in")

    # 포털(입구 아치 테두리) 두 개 - 터널
    frames = []
    for sign in (-1.0, 1.0):
        z0, z1 = (FRAME_Z0, FRAME_Z1) if sign > 0 else (-FRAME_Z1, -FRAME_Z0)
        fr = to_object(sweep(arch_profile(BORE_W * 0.5 + FRAME_T, WALL_H, BASE_Y), z0, z1, 1), "frame")
        cut = to_object(sweep(arch_profile(BORE_W * 0.5, WALL_H, BASE_Y - 3.0), z0 - 5.0, z1 + 5.0, 1), "framecut")
        difference(fr, cut)
        frames.append(fr)

    # 콘크리트 = 터널 안쪽 + 포털
    concrete_bm = bmesh.new()
    for ob in [inner] + frames:
        concrete_bm.from_mesh(ob.data)
        bpy.data.objects.remove(ob, do_unlink=True)
    concrete = to_object(concrete_bm, NAME + "__concrete")

    # 바닥 도로(칸 윗면 위 얇은 판 — 충돌 없음)
    road = to_object(sweep(rect(-BORE_W * 0.5 + 0.1, BORE_W * 0.5 - 0.1, 0.02, 0.25), -FRAME_Z1, FRAME_Z1, steps(-FRAME_Z1, FRAME_Z1)), NAME + "__asphalt")   # 벽까지

    # 유도등: 양쪽 벽 아래 띠 + 천장 가운데 점선(중심선을 따라)
    glow_bm = bmesh.new()
    zl = FRAME_Z0 - 2.0
    for sx in (-1.0, 1.0):
        x = sx * (BORE_W * 0.5 - 0.25)
        sweep_into(glow_bm, rect(x - 0.2, x + 0.2, 1.0, 1.7), -zl, zl, steps(-zl, zl))
    z = -zl
    while z + 6.0 <= zl:
        sweep_into(glow_bm, rect(-0.6, 0.6, BORE_H - 0.6, BORE_H - 0.25), z, z + 6.0, 1)
        z += 24.0
    bmesh.ops.recalc_face_normals(glow_bm, faces=glow_bm.faces)
    glow = to_object(glow_bm, NAME + "__glow")

    for ob in (rock, concrete, road, glow):
        print("PART", ob.name, "tris", tris(ob))
        to_unity_export(ob)


if __name__ == "__main__":
    main()
