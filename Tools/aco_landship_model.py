# -*- coding: utf-8 -*-
"""수륙양용 육상전함 모델(독자 디자인). 중앙부 갑판이 삼동선처럼 넓고, 그 바깥벽 자리에 육각형 궤도 유닛과 스커트가 있다. 앞뒤 유닛은 중앙 선체 옆에 노출된다.
실행: blender --background --factory-startup --python Tools/aco_landship_model.py [-- 미리보기폴더 [elev=고각] [open=셔터각] [strip=mid|skirt|track] [장면...]]
  검사용 환경 변수: ACO_NORMAL_CHECK=1 → 면 덩어리마다 노멀이 안쪽을 보는지 출력, ACO_BACKFACE_RED=1 → 미리보기에서 뒷면을 빨갛게(Unity에서 구멍으로 보일 곳)
  strip은 오른쪽(+X) 부품을 빼고 렌더한다(파괴 뒤 모습 확인용). mid: 가운데 궤도 2개와 스커트, skirt: 스커트 전부, track: 스커트 전부와 가운데 궤도 4개
결과: Assets/ACO_Addon/Models/LandShip/ACO_LandShip.fbx, ACO_LandShip_Albedo.png (4096, 임포트 Max Size도 4096으로 둔다)
좌표는 Unity 축(+Z 함수, +Y 위, 단위 m)이고 원점은 지면의 선체 가운데다. 갑판 높이는 DECK_Y다.
FBX는 조립된 상태로 나간다. 오브젝트마다 따로 숨기거나 돌릴 수 있다(메시는 같은 것을 공유한다).
  LS_Hull: 선체와 상부 구조(원점 지면 가운데)
  LS_TrackPod_R1~R6 / L1~L6: 궤도 유닛(원점 지면, 번호는 함수부터). LS_Skirt_R1~R6 / L1~L6: 그 스커트
  LS_MainTurret_1~3: 주포탑(원점 선회축, 로컬 Y로 돈다). 자식 LS_MainGun_1~3: 포신(원점 고각 축 MAIN_GUN_PIVOT, 로컬 X로 든다)
  LS_SecondaryTurret_FL/FR/RL/RR: 부포탑. 자식 LS_SecondaryGun_*: 포신(원점 SECOND_GUN_PIVOT)
  LS_CoreShutter_R/L: 동력 코어 셔터(원점 경첩 CORE_HINGE, 로컬 Z로 젖혀 연다. L은 R을 Y로 180도 돌린 것)
발광 면(동력 코어, 측면 배기구)은 둘째 재질 ACO_LandShip_Glow를 쓴다. 수직 발사관과 근접 방어 기관포는 모델에 없다(Unity 프리팹을 얹는다).
텍스처는 세계 좌표로 투영하므로 창과 문은 make_texture에서 z, y로 칠한다. 스커트와 포탑은 복제되므로 전용 칸을 쓴다."""
import math
import os
import sys
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "LandShip")
FBX = os.path.join(OUT, "ACO_LandShip.fbx")
ALBEDO = os.path.join(OUT, "ACO_LandShip_Albedo.png")
FONT = "C:/Windows/Fonts/arialbd.ttf"

TEX = 4096
PX = 7.0                    # 선체 투영의 1m당 픽셀
BAND_SIDE = 700             # 옆면 띠 높이(100m)
SIDE_POS = 0                # +X 옆면 띠의 윗줄(픽셀)
SIDE_NEG = 704              # -X 옆면 띠
TOP_BAND = 1408             # 갑판 띠
BAND_TOP = 980
SKIRT_RECT = (0, 2400, 18.0)        # x, y, 1m당 픽셀. 폭 1000, 높이 760
TURRET_RECT = (1040, 2400, 20.0)    # 폭 1240, 높이 720
FRONT_RECT = (2320, 2400)           # 앞뒤 면 띠. 폭 980, 높이 700, 배율 PX
SWATCH_Y = 3300
CELL = 32

COLORS = {
    "dark": (0.1, 0.11, 0.125), "metal": (0.34, 0.36, 0.38), "rubber": (0.065, 0.065, 0.07), "glass": (0.05, 0.08, 0.11),
    "white": (0.6, 0.62, 0.635), "steel": (0.5, 0.52, 0.54), "link": (0.17, 0.17, 0.175), "wheel": (0.25, 0.26, 0.27), "panel": (0.2, 0.22, 0.245),
    "glow": (0.55, 0.88, 1.0),
}
SWATCHES = {name: i for i, name in enumerate(COLORS)}

DECK_Y = 48.0
RECESS_X = 20.5             # 앞뒤 궤도 자리의 선체 옆면 x
RECESS_Y = 38.5             # 깎인 면의 윗선 y
# (z, 갑판 반폭, 최대 반폭, 최대 폭 높이, 바닥 반폭, 바닥 y, 윗선 y). 함미 → 함수.
# 벽은 최대 폭에서 갑판까지 안으로 기운다(텀블홈). 함수는 갑판이 먼저 끝나고 아래쪽이 앞으로 튀어나온다
# 여덟째 값이 있으면 그 단면의 옆면이 그 x까지 오목하게 들어간다(궤도 자리에서 함수와 함미까지 이어진다)
# 아홉째 값은 깎인 면의 윗선 y다(없으면 RECESS_Y). 함수에서는 사선으로 내려와 끝에 닿는다
# 함미는 윗부분이 곧게 끝까지 가서 수직으로 끝나고, 그 아래만 안으로 경사진다
STATIONS = (
    (-276.0, 24.0, 29.5, 16.0, 23.0, 37.4, DECK_Y, RECESS_X),
    (-268.0, 24.0, 29.5, 16.0, 23.0, 31.5, DECK_Y, RECESS_X),
    (-256.0, 24.0, 29.5, 16.0, 23.0, 22.0, DECK_Y, RECESS_X),
    (-244.0, 24.0, 29.5, 16.0, 23.0, 12.8, DECK_Y, RECESS_X),
    (-236.0, 24.0, 29.5, 16.0, 23.0, 7.0, DECK_Y, RECESS_X),
    (-220.0, 24.0, 29.5, 16.0, 23.0, 5.0, DECK_Y, RECESS_X),
    (-113.0, 24.0, 29.5, 16.0, 23.0, 5.0, DECK_Y, RECESS_X),
    (-105.0, 24.0, 29.5, 16.0, 23.0, 5.0, DECK_Y),
    (105.0, 24.0, 29.5, 16.0, 23.0, 5.0, DECK_Y),
    (113.0, 24.0, 29.5, 16.0, 23.0, 5.0, DECK_Y, RECESS_X),
    (150.0, 24.0, 29.0, 16.0, 21.0, 6.0, DECK_Y, RECESS_X),
    (163.0, 22.05, 27.4, 16.1, 19.0, 6.5, DECK_Y, RECESS_X),
    (171.0, 20.85, 26.4, 16.2, 17.9, 6.8, DECK_Y, 19.0),
    (190.0, 18.0, 24.0, 16.5, 15.0, 7.5, DECK_Y, 15.8),
    (225.0, 10.5, 15.0, 17.0, 8.0, 9.0, DECK_Y, 9.7, 32.0),
    (240.0, 6.2, 12.0, 17.2, 5.5, 9.5, DECK_Y, 7.6, 28.0),
    (258.0, 0.5, 8.5, 17.5, 3.0, 10.5, DECK_Y, 5.0, 24.0),
    (276.0, 0.3, 4.6, 17.8, 1.5, 12.0, 33.8, 2.6, 21.5),
    (292.0, 0.1, 0.9, 18.0, 0.3, 14.5, 21.2, 0.3, 19.2),
    (296.0, 0.04, 0.08, 18.0, 0.04, 17.0, 18.6),
)

POD_Z = (-81.0, -27.0, 27.0, 81.0)                      # 넓은 갑판 아래 궤도 유닛 자리
INNER_POD_Z = (-138.0, 138.0)                           # 앞뒤 궤도 유닛: 중앙 선체 옆에 붙고 노출된다
INNER_POD_X = 21.5                                      # 앞뒤 궤도 유닛 안쪽 면의 x
SKIRT_Z = POD_Z                                         # 넓은 갑판 아래 유닛의 스커트. 앞뒤 유닛도 INNER_SKIRT_X에 같은 스커트를 씌운다
POD_X = 42.0                # 궤도 유닛 안쪽 면의 x
# 중앙 선체에서 궤도 위까지 뻗는 넓은 갑판의 평면(x, z). 바깥벽은 없고 궤도와 스커트가 그 자리다
WING_DECK = ((24.0, -158.0), (36.0, -136.0), (46.0, -117.0), (50.0, -106.0), (50.0, 102.0), (46.0, 113.0), (36.0, 132.0), (24.0, 154.0))
POD_W = 7.0                 # 궤도 폭
POD_HALF = 20.0             # 궤도 유닛의 앞뒤 절반 길이
POD_H = 34.0                # 궤도 유닛 높이
TRACK_BELT = 2.2            # 궤도 띠 두께
SKIRT_X = 49.2              # 스커트 안쪽 면의 x
INNER_SKIRT_X = INNER_POD_X + SKIRT_X - POD_X      # 앞뒤 유닛 스커트 안쪽 면의 x(궤도와 간격이 같다)

CORE_Z = -76.0              # 동력 코어(1단 지붕 위, 함교 뒤)
CORE_HINGE = (13.6, 63.95, CORE_Z)   # 오른쪽 셔터의 경첩(바깥 아래 모서리)
CORE_HEX = ((0.0, -15.7), (13.6, -7.5), (13.6, 7.5), (0.0, 15.7), (-13.6, 7.5), (-13.6, -7.5))   # 코어 상자 윗면 평면(x, 코어 기준 z). 앞뒤가 뾰족한 육각형
MAIN_GUN_PIVOT = (0.0, 4.6, 12.5)    # 주포 포신의 고각 축(포탑 기준). 축은 앞판 1.2m 안쪽이고 방패가 앞판 밖으로 나온다
MAIN_TURRETS = ((0.0, 52.0, 200.0, 0.0), (0.0, 58.0, 144.0, 0.0), (0.0, 52.0, -170.0, 180.0))      # x, y, z, 선회각
SECOND_TURRETS = ((-22.5, 51.0, 118.0, 0.0), (22.5, 51.0, 118.0, 0.0), (-23.0, 51.0, -122.0, 180.0), (23.0, 51.0, -122.0, 180.0))
SECOND_SCALE = 0.45         # 부포 크기(주포 대비)
SECOND_GUN_PIVOT = tuple(v * SECOND_SCALE for v in MAIN_GUN_PIVOT)     # 부포 포신의 고각 축(포탑 기준)


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Part:
    """region이 있으면 "part" 칠은 그 칸으로 투영한다. uv_scale은 축소한 부품이 같은 칸을 쓰게 한다."""
    def __init__(self, region=None, uv_scale=1.0):
        self.bm = bmesh.new()
        self.paint = self.bm.faces.layers.int.new("paint")
        self.smooth = self.bm.faces.layers.int.new("smooth")
        self.uv = self.bm.loops.layers.uv.verify()
        self.region = region
        self.uv_scale = uv_scale

    def tag(self, faces, paint, smooth):
        code = {"hull": -1, "part": -2}.get(paint, SWATCHES.get(paint))
        for f in faces:
            f[self.paint] = code
            f[self.smooth] = 1 if smooth else 0

    def loft(self, rings, paint="hull", smooth=False, caps=True, close=False):
        """고리들을 잇는다. caps = 양 끝을 막는다, close = 마지막 고리를 첫 고리에 다시 잇는다(속이 빈 띠·벽을 닫힌 덩어리로)."""
        last = None
        first = None
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
        if close and last is not None and first is not None:
            n = len(first)
            for i in range(n):
                faces.append(self.bm.faces.new((last[i], last[(i + 1) % n], first[(i + 1) % n], first[i])))
        flat = []
        if caps and not close:
            # 첫 뚜껑은 반대로 감아야 옆면·끝 뚜껑과 같은 쪽(바깥 또는 안)을 본다
            flat.append(self.bm.faces.new(list(reversed(first))))
            flat.append(self.bm.faces.new(last))
        self.tag(faces, paint, smooth)
        self.tag(flat, paint, False)
        self.orient(faces + flat, caps or close)

    def orient(self, piece, closed):
        """부품 하나의 노멀을 바깥으로 맞춘다. 합친 뒤 전체를 한 번에 다시 계산하면 붙은 부품끼리 방향을 잘못 고른다 —
        그래서 만들 때 부품마다 정한다. 닫힌 부품은 부피 부호로, 열린 부품은 그 부품만 다시 계산한다."""
        if not piece:
            return
        if not closed:
            bmesh.ops.recalc_face_normals(self.bm, faces=piece)
            return
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

    def stack(self, x, levels, paint="hull"):
        """모서리를 깎은 층을 쌓는다. levels는 (y, x 반폭, z 뒤, z 앞, 모서리)."""
        self.loft([oct_ring(x, hx, zb, zf, c, y) for y, hx, zb, zf, c in levels], paint)

    def box(self, x, z, hx, hz, y0, y1, taper=0.0, paint="hull"):
        self.stack(x, ((y0, hx, z - hz, z + hz, 0.1), (y1, hx - taper, z - hz + taper, z + hz - taper, 0.1)), paint)

    def prism(self, poly, x0, x1, paint="hull", smooth=False):
        """(z, y) 다각형을 x로 뽑는다."""
        self.loft([[(x0, y, z) for z, y in poly], [(x1, y, z) for z, y in poly]], paint, smooth)

    def plate(self, pts, out, t=0.2, paint="dark"):
        """네 점의 판을 out 쪽으로 t만큼 띄운다."""
        n = out.normalized() * t
        self.loft([[tuple(Vector(q)) for q in pts], [tuple(Vector(q) + n) for q in pts]], paint)

    def sweep(self, path, y0, y1, w, paint="hull"):
        """(x, z) 경로를 따라 폭 w, 높이 y0~y1의 띠를 놓는다."""
        rings = []
        n = len(path)
        for i in range(n):
            a = Vector(path[max(i - 1, 0)])
            b = Vector(path[min(i + 1, n - 1)])
            t = (b - a).normalized()
            s = Vector((-t.y, t.x)) * (w * 0.5)
            c = Vector(path[i])
            rings.append([(c.x - s.x, y0, c.y - s.y), (c.x + s.x, y0, c.y + s.y), (c.x + s.x, y1, c.y + s.y), (c.x - s.x, y1, c.y - s.y)])
        self.loft(rings, paint)

    def tube(self, a, b, r0, r1=None, n=8, paint="metal", smooth=True):
        a = Vector(a)
        b = Vector(b)
        r1 = r0 if r1 is None else r1
        axis = (b - a).normalized()
        side = axis.orthogonal().normalized()
        up = axis.cross(side).normalized()
        rings = []
        for c, r in ((a, r0), (b, r1)):
            rings.append([tuple(c + side * (math.cos(i * math.tau / n) * r) + up * (math.sin(i * math.tau / n) * r)) for i in range(n)])
        self.loft(rings, paint, smooth)

    def dome(self, c, r, h, paint="white", n=8):
        rings = []
        for k in range(5):
            t = k / 4.0 * math.pi * 0.5
            rr = max(math.cos(t) * r, 0.03)
            rings.append([(c[0] + math.cos(i * math.tau / n) * rr, c[1] + math.sin(t) * h, c[2] + math.sin(i * math.tau / n) * rr) for i in range(n)])
        self.loft(rings, paint, True)

    def finish(self, name):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        # 노멀은 부품을 만들 때 정했다(orient) — 합친 뒤 전체를 다시 계산하지 않는다
        bm.normal_update()
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(50.0):
                e.smooth = False
        twisted = [f for f in bm.faces if len(f.verts) == 4 and twist(f) > 0.02]
        bmesh.ops.triangulate(bm, faces=twisted, quad_method='SHORT_EDGE', ngon_method='BEAUTY')
        bm.normal_update()
        bm.faces.ensure_lookup_table()
        for f in bm.faces:
            set_uv(f, self.uv, f[self.paint], self.region, self.uv_scale)
            f.smooth = f[self.smooth] == 1
            f.material_index = 1 if f[self.paint] == SWATCHES["glow"] else 0
        for v in bm.verts:
            v.co = to_blender(v.co)
        # Unity → Blender 축 바꿈은 거울상(행렬식 -1)이라 감는 방향이 뒤집힌다 — 모두 되돌린다
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.normal_update()
        if os.environ.get("ACO_NORMAL_CHECK"):
            check_islands(bm, name)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        return me


def check_islands(bm, name):
    """검사용: 이어진 면 덩어리마다 부피 부호로 노멀이 안쪽을 보는지 본다(닫힌 덩어리만 확실하다). Unity 좌표로 출력."""
    bm.faces.ensure_lookup_table()
    seen = set()
    bad = 0
    total = 0
    for f0 in bm.faces:
        if f0.index in seen:
            continue
        stack = [f0]
        seen.add(f0.index)
        faces = []
        while stack:
            f = stack.pop()
            faces.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        edges = set(e for f in faces for e in f.edges)
        closed = all(len([g for g in e.link_faces]) == 2 for e in edges)
        vol = sum(f.calc_center_median().dot(f.normal) * f.calc_area() for f in faces) / 3.0
        total += 1
        if vol < 0.0:
            bad += 1
            c = sum((f.calc_center_median() for f in faces), Vector()) / len(faces)
            print("INWARD %s faces %d closed %s vol %.1f at unity (%.1f, %.1f, %.1f)" % (name, len(faces), closed, vol, -c.x, c.z, -c.y))
            outward = sum(1 for f in faces if (f.calc_center_median() - c).dot(f.normal) > 0.0)
            print("   faces pointing away from island center: %d / %d" % (outward, len(faces)))
    print("ISLANDS %s %d inward %d" % (name, total, bad))


def twist(f):
    """사각면이 평면에서 벗어난 거리."""
    a, b, c, d = (v.co for v in f.verts)
    n = (b - a).cross(c - a)
    return abs(n.normalized().dot(d - a)) if n.length > 1e-6 else 0.0


def oct_ring(x, hx, zb, zf, c, y):
    return [(x - hx + c, y, zb), (x + hx - c, y, zb), (x + hx, y, zb + c), (x + hx, y, zf - c),
            (x + hx - c, y, zf), (x - hx + c, y, zf), (x - hx, y, zf - c), (x - hx, y, zb + c)]


def set_uv(f, uv, code, region, s):
    if code >= 0:
        x = code * CELL + CELL * 0.5
        y = SWATCH_Y + CELL * 0.5
        p = (x / TEX, 1.0 - y / TEX)
        for loop in f.loops:
            loop[uv].uv = p
        return
    n = f.normal
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    if code == -2 and region == "skirt":
        ox, oy, ppm = SKIRT_RECT
        for loop in f.loops:
            c = loop.vert.co
            # 옆에서 본 (z, y) 그대로 — 텍스처의 테두리·홈·리벳이 모양과 맞는다. 옆을 보는 테두리 면은 단색 테두리 띠를 받는다
            loop[uv].uv = ((ox + 500.0 + c.z * ppm) / TEX, 1.0 - (oy + 720.0 - (c.y - 9.0) * ppm) / TEX)
        return
    if code == -2:
        ox, oy, ppm = TURRET_RECT
        for loop in f.loops:
            c = loop.vert.co / s
            if ay >= ax and ay >= az:
                px, py = ox + 400.0 + c.z * ppm, oy + 260.0 - c.x * ppm
            elif ax >= az:
                px, py = ox + 400.0 + c.z * ppm, oy + 710.0 - c.y * ppm
            else:
                px, py = ox + 980.0 + c.x * ppm, oy + 300.0 - c.y * ppm
            loop[uv].uv = (px / TEX, 1.0 - py / TEX)
        return
    for loop in f.loops:
        c = loop.vert.co
        uz = max(min(c.z, 290.0), -290.0)
        if ay >= ax and ay >= az and n.y > 0.0:
            px = TEX * 0.5 + uz * PX
            py = TOP_BAND + BAND_TOP * 0.5 - c.x * PX
        elif ax >= az:
            px = TEX * 0.5 + (uz if n.x > 0.0 else -uz) * PX    # 양쪽 모두 바깥에서 글자가 바로 읽힌다
            py = (SIDE_POS if n.x > 0.0 else SIDE_NEG) + BAND_SIDE - min(c.y, 99.0) * PX
        else:
            px = FRONT_RECT[0] + 490.0 + c.x * (1.0 if n.z > 0.0 else -1.0) * PX
            py = FRONT_RECT[1] + BAND_SIDE - min(c.y, 99.0) * PX
        loop[uv].uv = (px / TEX, 1.0 - py / TEX)


# ───────────── 선체 ─────────────

def hull_rings():
    """날카로운 꺾임선만 있는 단면: 바닥, 최대 폭, 얕은 턱 하나, 갑판 가장자리."""
    rings = []
    for station in STATIONS:
        z, deck, wide, wide_y, low, bottom, top = station[:7]
        if len(station) > 7:
            r = station[7]
            ceil = station[8] if len(station) > 8 else RECESS_Y
            wall = wide + (deck - wide) * (ceil + 1.0 - wide_y) / (top - wide_y)     # 천장 바깥 끝은 벽 위에 있다
            right = [(min(low, max(r - 2.0, r * 0.6)), bottom + 0.8), (r, max(wide_y, bottom + 1.0)), (r, ceil), (wall, ceil + 1.0), (deck, top)]
        else:
            ledge_y = wide_y + (top - wide_y) * 0.52
            ledge_x = wide + (deck - wide) * 0.52
            t = min(0.7, wide * 0.05)       # 턱은 함수로 갈수록 얇아져 사라진다
            right = [(low, bottom + 0.8), (wide, wide_y), (ledge_x + t, ledge_y), (ledge_x, ledge_y + t * 0.7), (deck, top)]
        ring = [(0.0, bottom, z)] + [(x, y, z) for x, y in right] + [(-x, y, z) for x, y in reversed(right)]
        rings.append(ring)
    return rings


def deck_half(z):
    """갑판 반폭을 선체 단면에서 보간한다."""
    pts = [(s[0], s[1]) for s in STATIONS]
    for (z0, w0), (z1, w1) in zip(pts, pts[1:]):
        if z0 <= z <= z1:
            return w0 + (w1 - w0) * (z - z0) / (z1 - z0)
    return 0.0


def hexagon(z, half, y0, y1):
    """옆에서 본 육각형: 위아래가 좁고 가운데가 넓다."""
    neck = half * 0.62
    waist = y0 + (y1 - y0) * 0.42
    return ((z - neck, y0), (z + neck, y0), (z + half, waist), (z + half * 0.86, y1), (z - half * 0.86, y1), (z - half, waist))


def inset(poly, d):
    """닫힌 다각형을 안쪽으로 d만큼 줄인다."""
    area = sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly)))
    sign = 1.0 if area > 0.0 else -1.0
    out = []
    n = len(poly)
    for i in range(n):
        a = Vector(poly[i - 1])
        b = Vector(poly[i])
        c = Vector(poly[(i + 1) % n])
        e0 = (b - a).normalized()
        e1 = (c - b).normalized()
        n0 = Vector((-e0.y, e0.x)) * sign
        n1 = Vector((-e1.y, e1.x)) * sign
        bis = (n0 + n1).normalized()
        out.append(tuple(b + bis * (d / max(bis.dot(n0), 0.3))))
    return out


def rounded(poly, r):
    """닫힌 다각형의 모서리를 두 점으로 깎는다."""
    out = []
    n = len(poly)
    for i in range(n):
        a = Vector(poly[i - 1])
        b = Vector(poly[i])
        c = Vector(poly[(i + 1) % n])
        out.append(tuple(b + (a - b).normalized() * r))
        out.append(tuple(b + (c - b).normalized() * r))
    return out


def power_core(p, z):
    """동력 코어: 1단 지붕 위의 속이 빈 육각 장갑 상자 안에 선 팔각 코어. 발광 띠가 있고, 위를 셔터 두 짝이 덮는다."""
    y0, y1 = 57.0, 63.9

    def hexring(d, y):
        return [(x, y, z + zz) for x, zz in inset(CORE_HEX, d)]
    # 상자 벽: 바깥은 위로 좁아지고 안쪽은 곧다. 바닥은 어둡다
    p.loft([hexring(-0.8, y0), hexring(0.0, y1), hexring(1.8, y1), hexring(1.8, 58.0)], "hull", False, caps=False, close=True)
    p.loft([hexring(1.7, 57.9), hexring(1.7, 58.0)], "dark")
    # 코어: 받침, 금속 띠와 발광 띠, 위 덮개와 발광 렌즈
    oct8 = [(math.cos(math.tau * (i + 0.5) / 8), math.sin(math.tau * (i + 0.5) / 8)) for i in range(8)]

    def ring(r, y):
        return [(c * r, y, z + s * r) for c, s in oct8]
    p.loft([ring(7.8, 58.0), ring(7.8, 59.8), ring(6.6, 61.0)], "metal", True)
    bands = ((60.9, 62.0, "metal"), (61.9, 63.0, "glow"), (62.9, 64.0, "metal"), (63.9, 65.0, "glow"), (64.9, 66.0, "metal"), (65.9, 67.0, "glow"), (66.9, 67.6, "metal"))
    for a, b, paint in bands:
        r = 5.6 if paint == "glow" else 6.0
        p.loft([ring(r, a), ring(r, b)], paint, True)
    p.loft([ring(6.0, 67.6), ring(2.6, 70.2)], "metal", True)
    p.loft([ring(2.1, 70.1), ring(1.9, 70.5)], "glow", True)
    # 코어에서 벽으로 가는 관
    for side in (-1.0, 1.0):
        for dz in (-3.0, 3.0):
            p.tube((side * 7.0, 59.0, z + dz), (side * 11.9, 59.0, z + dz), 0.7, 0.7, 8, "metal", True)


def core_shutter():
    """동력 코어 셔터 오른쪽 한 짝: 육각 뚜껑의 절반이다. 옆면은 비스듬하고 윗면은 평평하다. 원점이 경첩(바깥 아래 모서리)이다."""
    p = Part("turret")
    hx, hy, hz = CORE_HINGE

    def half(d, y, shift=0.0):
        return [(x + shift - hx, y - hy, zz) for x, zz in inset(CORE_HEX, d)[:4]]
    p.loft([half(0.0, 63.95), half(4.0, 71.0)], "part")
    # 윗면의 덧댄 판: 가운데 이음매에서 떨어져 홈이 보인다
    p.loft([half(5.2, 70.95, 0.6), half(5.4, 71.35, 0.6)], "metal")
    # 비스듬한 옆면의 걸쇠 덮개
    for zz in (-4.0, 4.0):
        k0, k1 = 0.25, 0.75
        a = (13.6 - 4.0 * k0, 63.95 + 7.05 * k0)
        b = (13.6 - 4.0 * k1, 63.95 + 7.05 * k1)
        p.plate([(a[0] - hx - 0.05, a[1] - hy, zz - 1.2), (a[0] - hx - 0.05, a[1] - hy, zz + 1.2), (b[0] - hx - 0.05, b[1] - hy, zz + 1.2), (b[0] - hx - 0.05, b[1] - hy, zz - 1.2)], Vector((7.05, 4.0, 0.0)), 0.35, "metal")
    return p.finish("LS_CoreShutter")


def lerp(a, b, k):
    return a + (b - a) * k


def strip_panels(p, a0, b0, a1, b1, count, out, paint="glass", gap=0.12):
    """네 모서리 사이를 count칸으로 나눈 판. a0-b0가 아랫변, a1-b1이 윗변이다."""
    a0, b0, a1, b1 = Vector(a0), Vector(b0), Vector(a1), Vector(b1)
    for i in range(count):
        u0 = (i + gap) / count
        u1 = (i + 1.0 - gap) / count
        p.plate([a0.lerp(b0, u0), a0.lerp(b0, u1), a1.lerp(b1, u1), a1.lerp(b1, u0)], out, 0.07, paint)


def octagon_plate(p, a0, b0, a1, b1, r, out, paint="panel"):
    """사각 면 가운데에 놓는 팔각 레이더판."""
    a0, b0, a1, b1 = Vector(a0), Vector(b0), Vector(a1), Vector(b1)
    c = (a0 + b0 + a1 + b1) * 0.25
    u = (b0 - a0).normalized()
    v = ((a1 + b1) - (a0 + b0)).normalized()
    n = u.cross(v).normalized()
    if n.dot(out) < 0.0:
        n = -n
    pts = [c + u * (math.cos((i + 0.5) * math.tau / 8.0) * r) + v * (math.sin((i + 0.5) * math.tau / 8.0) * r) for i in range(8)]
    p.loft([[tuple(q) for q in pts], [tuple(q + n * 0.2) for q in pts]], paint)


def deckhouse(p):
    """계단형 함교: 모서리에 팔각 레이더판을 단 몸체, 옆벽이 수직이고 앞면이 기운 유리 함교, 뒤에 일체형 탑과 공 모양 레이더돔."""
    y0, y1 = 68.0, 78.5
    lv0 = (13.0, -44.0, 26.0, 8.0)
    lv1 = (11.6, -40.0, 21.0, 6.8)
    p.stack(0.0, ((y0,) + lv0, (y1,) + lv1))
    # 네 귀의 레이더판
    for side in (-1.0, 1.0):
        hx0, zb0, zf0, c0 = lv0
        hx1, zb1, zf1, c1 = lv1
        octagon_plate(p, (side * hx0, y0, zf0 - c0), (side * (hx0 - c0), y0, zf0), (side * hx1, y1, zf1 - c1), (side * (hx1 - c1), y1, zf1), 3.7, Vector((side, 0.0, 1.0)))
        octagon_plate(p, (side * hx0, y0, zb0 + c0), (side * (hx0 - c0), y0, zb0), (side * hx1, y1, zb1 + c1), (side * (hx1 - c1), y1, zb1), 3.7, Vector((side, 0.0, -1.0)))

    # 함교: 뒤쪽 벽은 아래 몸체와 한 면이고, 앞으로 갈수록 위가 벌어져 팔각 앞면이 된다
    def ring(y, side_fwd, front, zs, zf):
        right = [(4.8, -40.0), (11.6, -33.2), (11.6, -8.0), (side_fwd, zs), (front, zf)]
        return [(x, y, z) for x, z in right] + [(-x, y, z) for x, z in reversed(right)]

    def cap(y, rear, side_fwd, front, zs, zf):
        right = [(rear, -4.0), (side_fwd, zs), (front, zf)]
        return [(x, y, z) for x, z in right] + [(-x, y, z) for x, z in reversed(right)]

    ya, yb = 79.0, 83.0
    la = (11.6, -1.0, 19.0, 5.0)
    lb = (11.6, -3.0, 21.5, 3.8)
    p.loft([ring(y1, 11.6, 5.6, 12.0, 18.0), ring(ya, 11.6, 6.6, 14.0, 19.0), ring(yb, 11.6, 7.8, 17.7, 21.5), ring(83.6, 11.6, 8.0, 18.4, 22.0)], "hull")
    # 지붕 판은 탑 앞쪽에만 있다
    p.loft([cap(83.6, 11.4, 11.4, 7.9, 18.3, 21.8), cap(84.2, 11.0, 11.0, 7.5, 17.9, 21.4)], "metal")

    wy0, wy1 = 79.6, 82.6
    k0 = (wy0 - ya) / (yb - ya)
    k1 = (wy1 - ya) / (yb - ya)
    ha, fa, ca = lerp(la[0], lb[0], k0), lerp(la[2], lb[2], k0), lerp(la[3], lb[3], k0)
    hb, fb, cb = lerp(la[0], lb[0], k1), lerp(la[2], lb[2], k1), lerp(la[3], lb[3], k1)
    strip_panels(p, (-(ha - ca), wy0, fa), (ha - ca, wy0, fa), (-(hb - cb), wy1, fb), (hb - cb, wy1, fb), 6, Vector((0.0, -0.45, 1.0)))
    for side in (-1.0, 1.0):
        strip_panels(p, (side * (ha - ca), wy0, fa), (side * ha, wy0, fa - ca), (side * (hb - cb), wy1, fb), (side * hb, wy1, fb - cb), 3, Vector((side, -0.6, 1.0)))
        # 옆 창은 수직 옆벽 위에 놓는다(날개 아래까지만)
        za, zb = fa - ca, fb - cb
        wy2 = 81.4
        k2 = (wy2 - wy0) / (wy1 - wy0)
        zc = lerp(za, zb, k2)
        strip_panels(p, (side * 11.6, wy0, za - 3.0), (side * 11.6, wy0, za), (side * 11.6, wy2, zc - 3.0), (side * 11.6, wy2, zc), 2, Vector((side, 0.0, 0.0)))
        # 함교 날개
        p.stack(side * 15.8, ((81.6, 4.6, 2.0, 8.5, 0.6), (83.3, 4.6, 2.0, 8.5, 0.6)))
        p.sweep(((side * 11.0, 5.2), (side * 19.5, 5.2)), 80.0, 81.6, 0.5, "hull")
    for x in (-8.0, 8.0):
        p.tube((x, 84.2, 16.0), (x, 85.4, 16.0), 1.1, 0.9, 8, "metal", True)
        p.dome((x, 85.4, 16.0), 1.4, 1.4, "white")

    # 일체형 탑: 함교 뒤쪽 벽에서 그대로 솟는다
    t0, t1 = 83.6, 104.0
    # 밑면 뒤쪽은 함교 벽과 같은 점이라 벽이 끊기지 않는다
    base = ((4.8, -40.0), (11.6, -33.2), (11.6, -3.0), (10.6, -2.0))
    crown = ((2.2, -24.0), (3.2, -23.0), (3.2, -17.0), (2.2, -16.0))
    p.loft([[(s * x, y, z) for s, x, z in [(1.0, a, b) for a, b in plan] + [(-1.0, a, b) for a, b in reversed(plan)]] for y, plan in ((t0, base), (t1, crown))], "hull")
    ya, yb = 91.0, 98.5
    ka, kb = (ya - t0) / (t1 - t0), (yb - t0) / (t1 - t0)
    ax, af = lerp(11.6, 3.2, ka), lerp(-2.0, -16.0, ka)
    bx, bf = lerp(11.6, 3.2, kb), lerp(-2.0, -16.0, kb)
    p.plate([(-ax * 0.6, ya, af), (ax * 0.6, ya, af), (bx * 0.6, yb, bf), (-bx * 0.6, yb, bf)], Vector((0.0, 14.0, t1 - t0)), 0.08, "panel")
    for side in (-1.0, 1.0):
        p.plate([(side * ax, ya, -28.0), (side * ax, ya, -13.0), (side * bx, yb, -16.5), (side * bx, yb, -24.0)], Vector((side * (t1 - t0), 8.4, 0.0)), 0.08, "panel")
    # 공 모양 레이더돔
    p.tube((0.0, t1, -20.0), (0.0, t1 + 2.0, -20.0), 1.3, 1.1, 8, "metal", True)
    p.dome((0.0, t1 + 5.0, -20.0), 3.6, 3.6, "white", 10)
    p.dome((0.0, t1 + 5.0, -20.0), 3.6, -3.4, "white", 10)


def level_one(y, rear, mid, front, zb, zm0, zm1, zf):
    """1단의 평면: 가운데가 넓고 앞뒤로 좁아진다."""
    right = [(rear, zb), (mid, zm0), (mid, zm1), (front, zf)]
    return [(x, y, z) for x, z in right] + [(-x, y, z) for x, z in reversed(right)]


def roof_rail(p):
    """1단 지붕 가장자리 난간."""
    path = ((16.7, -98.6), (29.7, -50.0), (29.7, 42.0), (12.7, 87.6))
    for side in (-1.0, 1.0):
        p.sweep([(side * x, z) for x, z in path], 57.0, 58.0, 0.18, "metal")
    p.sweep(((-12.7, 87.6), (12.7, 87.6)), 57.0, 58.0, 0.18, "metal")
    p.sweep(((-16.7, -98.6), (16.7, -98.6)), 57.0, 58.0, 0.18, "metal")


def hull():
    p = Part()
    p.loft(hull_rings(), "hull")

    # 넓은 갑판: 아래 모서리를 깎아 판처럼 보이지 않게 한다
    for side in (-1.0, 1.0):
        edge = [(side * x, z) for x, z in WING_DECK]
        low = inset(edge, 7.0)
        low[0] = (side * 20.0, low[0][1])
        low[-1] = (side * 20.0, low[-1][1])
        p.loft([[(x, 37.5, z) for x, z in low], [(x, 44.0, z) for x, z in edge], [(x, DECK_Y, z) for x, z in edge]], "hull")
        # 궤도 유닛을 거는 팔, 팔들을 잇는 보
        for z in POD_Z:
            p.prism(((z - 13.0, 22.0), (z + 13.0, 22.0), (z + 7.0, 40.0), (z - 7.0, 40.0)), side * 24.0, side * 43.0, "hull")
        p.prism(((-100.0, 33.0), (100.0, 33.0), (100.0, 40.0), (-100.0, 40.0)), side * 38.0, side * 41.0, "hull")
        # 스커트를 거는 어깨 장갑: 갑판 가장자리를 따라 이어진다
        shoulder = [(48.0, 41.5), (54.8, 42.4), (54.8, 44.0), (52.0, 45.6), (49.0, 46.2)]
        p.loft([[(side * (x - (3.0 if abs(z) > 103.0 else 0.0)), y, z) for x, y in shoulder] for z in (-108.0, -103.0, 103.0, 108.0)], "hull")
        # 갑판 가장자리의 배기구
        for i in range(9):
            z = -96.0 + i * 24.0
            p.plate([(side * 49.9, 46.4, z - 7.0), (side * 49.9, 46.4, z + 7.0), (side * 49.9, 47.4, z + 7.0), (side * 49.9, 47.4, z - 7.0)], Vector((side, 0.0, 0.0)), 0.2)
        # 궤도 유닛 뒤 선체 벽의 배기구: 어두운 틀, 발광판, 세로 살
        def wall(y, lift, zz):
            xw = 29.5 - (y - 16.0) * 0.1723
            return (side * (xw + lift), y + lift * 0.1723, zz)
        vent_n = Vector((side, 0.1723, 0.0))
        for z in POD_Z:
            a, b = z - 15.6, z + 15.6
            p.plate([wall(16.6, -0.1, a), wall(16.6, -0.1, b), wall(21.8, -0.1, b), wall(21.8, -0.1, a)], vent_n, 0.4, "dark")
            p.plate([wall(17.2, 0.25, a + 0.9), wall(17.2, 0.25, b - 0.9), wall(21.2, 0.25, b - 0.9), wall(21.2, 0.25, a + 0.9)], vent_n, 0.1, "glow")
            for i in range(int((b - a - 3.0) / 1.75) + 1):
                zz = a + 1.6 + i * 1.75
                p.plate([wall(17.2, 0.2, zz - 0.12), wall(17.2, 1.3, zz - 0.12), wall(21.2, 1.3, zz - 0.12), wall(21.2, 0.2, zz - 0.12)], Vector((0.0, 0.0, 1.0)), 0.24, "metal")
        # 앞뒤 스커트를 거는 어깨 장갑: 선체 벽에서 스커트 윗변까지 덮는다
        sx = INNER_SKIRT_X
        shoulder = [(20.0, 41.5), (sx + 4.2, 41.5), (sx + 4.2, 43.6), (sx + 2.4, 45.4), (20.0, 46.4)]
        for z in INNER_POD_Z:
            p.loft([[(side * x, y, zz) for x, y in shoulder] for zz in (z - 25.5, z + 25.5)], "hull")
        # 앞뒤 유닛의 팔: 오목한 자리 안에서 선체에 붙는다
        for z in INNER_POD_Z:
            p.prism(((z - 11.0, 18.0), (z + 11.0, 18.0), (z + 7.0, 34.0), (z - 7.0, 34.0)), side * 16.0, side * 22.2, "hull")

    # 앞갑판 파도막이: 앞으로 뾰족한 V
    p.loft([[(-9.5, DECK_Y, 216.0), (9.5, DECK_Y, 216.0), (0.0, DECK_Y, 232.0)], [(-9.0, DECK_Y + 3.4, 214.5), (9.0, DECK_Y + 3.4, 214.5), (0.0, DECK_Y + 3.0, 229.0)]], "hull")

    # 포탑 받침과 전타구역 구조물
    for x, y, z, _ in MAIN_TURRETS:
        if y > 55.0:    # 2번 포탑은 주변 구조물 위로 쏜다
            p.stack(x, ((DECK_Y, 19.0, z - 19.0, z + 19.0, 7.0), (y - 1.0, 16.0, z - 16.5, z + 15.5, 6.0), (y - 0.6, 15.6, z - 16.1, z + 15.1, 6.0)))
        p.tube((x, y - 6.0, z), (x, y - 0.8, z), 13.4, 13.4, 14, "hull", True)
        p.tube((x, y - 0.8, z), (x, y + 0.1, z), 13.4, 12.6, 14, "metal", True)
    for x, y, z, _ in SECOND_TURRETS:
        p.tube((x, DECK_Y, z), (x, y - 0.3, z), 6.6, 6.4, 12, "hull", True)
        p.tube((x, y - 0.3, z), (x, y + 0.1, z), 6.4, 6.0, 12, "metal", True)

    # 1단: 함 전체를 잇는 긴 몸체. 가운데가 넓고 앞뒤로 좁아진다
    p.loft([level_one(DECK_Y, 20.0, 33.0, 16.0, -106.0, -52.0, 44.0, 100.0), level_one(DECK_Y + 2.0, 20.0, 33.0, 16.0, -106.0, -52.0, 44.0, 99.0),
            level_one(DECK_Y + 2.4, 19.4, 32.2, 15.4, -105.0, -52.0, 44.0, 98.0), level_one(57.0, 17.0, 30.0, 13.0, -99.0, -50.0, 42.0, 88.0)], "hull")
    # 2단: 벽이 안으로 기운다
    p.stack(0.0, ((57.0, 16.0, -58.0, 50.0, 1.6), (67.6, 13.4, -54.0, 36.0, 1.4), (68.0, 13.4, -54.0, 36.0, 1.4)))
    deckhouse(p)
    roof_rail(p)

    # 함교 뒤의 동력 코어(셔터는 LS_CoreShutter)
    power_core(p, CORE_Z)

    # 1단 뒤쪽의 구명정 통, 2단 뒤의 회전 레이더
    for side in (-1.0, 1.0):
        for i in range(3):
            z = -52.0 + i * 8.0
            p.tube((side * 27.0, 58.4, z - 1.8), (side * 27.0, 58.4, z + 1.8), 0.9, 0.9, 8, "white", True)
    p.box(0.0, -49.0, 4.6, 3.4, 68.0, 69.6, 0.5)
    p.tube((0.0, 69.6, -49.0), (0.0, 71.4, -49.0), 0.7, 0.5, 8, "metal", True)
    p.box(0.0, -49.0, 4.6, 0.6, 71.4, 73.0, 0.1, "dark")

    # 갑판 해치
    for z, x in ((172.0, 15.5), (172.0, -15.5), (-148.0, 9.0), (-148.0, -9.0), (-198.0, 14.0), (-198.0, -14.0)):
        p.box(x, z, 3.2, 2.6, DECK_Y, DECK_Y + 0.9, 0.3)
        p.box(x, z, 2.2, 1.6, DECK_Y + 0.9, DECK_Y + 1.1, 0.05, "panel")

    # 갑판 가장자리 턱: 함미에서 함수까지 끊기지 않는다
    outline = [(deck_half(s), s) for s in (-275.5, -200.0, -160.0)]
    outline += [(x, z) for x, z in WING_DECK[1:-2]]
    outline += [(deck_half(s), s) for s in (150.0, 170.0, 190.0, 210.0, 225.0, 240.0, 257.0)]
    for side in (-1.0, 1.0):
        path = [(side * (x - 0.3), z) for x, z in outline]
        p.sweep(path, DECK_Y, DECK_Y + 0.9, 0.6, "hull")
    p.sweep(((-23.7, -275.5), (23.7, -275.5)), DECK_Y, DECK_Y + 0.9, 0.6, "hull")

    return p.finish("LS_Hull")


def wheel(p, z, y, r, x0, x1, n=10):
    """테, 움푹한 면, 허브."""
    ring = lambda x, rr: [(x, y + math.sin(i * math.tau / n) * rr, z + math.cos(i * math.tau / n) * rr) for i in range(n)]
    p.loft([ring(x0, r), ring(x1, r), ring(x1, r * 0.76), ring(x1 - 0.5, r * 0.68), ring(x1 - 0.5, r * 0.36), ring(x1 + 0.3, r * 0.26)], "wheel", True)


def track_pod():
    """육각형 궤도 유닛 하나: 바닥이 지면에 닿는다. 궤도 안쪽을 바퀴가 받친다."""
    p = Part()
    outer = rounded(hexagon(0.0, POD_HALF, 0.0, POD_H), 2.0)
    inner = inset(outer, TRACK_BELT)
    x0, x1 = 0.0, POD_W
    # 궤도 띠
    p.loft([[(x0, y, z) for z, y in outer], [(x1, y, z) for z, y in outer], [(x1, y, z) for z, y in inner], [(x0, y, z) for z, y in inner]], "rubber", False, caps=False, close=True)
    # 안쪽 차대: 뒤판, 앞뒤를 잇는 보, 판을 뚫은 구멍
    back = inset(outer, TRACK_BELT + 0.6)
    p.prism(back, 0.4, 1.1, "dark")
    frame = inset(hexagon(0.0, POD_HALF, 0.0, POD_H), 7.2)
    p.prism(frame, 1.1, 1.9, "metal")
    for z, y in ((-8.5, 17.0), (0.0, 17.0), (8.5, 17.0)):
        p.tube((1.9, y, z), (2.0, y, z), 3.0, 3.0, 8, "dark", False)
    p.prism(((-17.0, 11.0), (17.0, 11.0), (17.0, 13.0), (-17.0, 13.0)), 1.9, 2.5, "metal")
    # 바퀴: 아래 네 개, 허리 둘, 위 둘, 윗면 지지 바퀴
    wx0, wx1 = 1.0, POD_W - 0.8
    for z in (-9.3, -3.1, 3.1, 9.3):
        wheel(p, z, 5.4, 3.0, wx0, wx1)
    for z in (-13.4, 13.4):
        wheel(p, z, 14.3, 4.2, wx0, wx1)
    for z in (-11.2, 11.2):
        wheel(p, z, 27.0, 4.2, wx0, wx1)
    wheel(p, 0.0, 29.6, 2.0, wx0, wx1, 8)
    # 궤도판: 경로를 따라 일정한 간격으로 놓는다
    n = len(outer)
    for i in range(n):
        a = Vector(outer[i])
        b = Vector(outer[(i + 1) % n])
        seg = (b - a).length
        along = (b - a).normalized()
        out = Vector((-along.y, along.x))
        if out.dot((a + b) * 0.5 - Vector((0.0, POD_H * 0.45))) < 0.0:
            out = -out
        count = max(int(seg / 3.0), 1)
        for k in range(count):
            c = a + (b - a) * ((k + 0.5) / count)
            quad = [c - along * 1.05 - out * 0.1, c + along * 1.05 - out * 0.1, c + along * 0.82 + out * 0.75, c - along * 0.82 + out * 0.75]
            p.prism([(q.x, q.y) for q in quad], -0.15, POD_W + 0.15, "link")
    return p.finish("LS_TrackPod")


def skirt():
    """궤도 유닛을 덮는 육각 장갑판 하나: 테두리, 홈, 주 장갑, 가운데 돋은 판, 환기구. 아래로 궤도가 드러난다."""
    p = Part("skirt")
    edge = list(hexagon(0.0, 24.5, 9.0, 44.0))
    p.loft([[(0.0, y, z) for z, y in edge], [(1.7, y, z) for z, y in edge],
            [(2.5, y, z) for z, y in inset(edge, 1.1)], [(2.5, y, z) for z, y in inset(edge, 2.4)],
            [(2.1, y, z) for z, y in inset(edge, 2.7)], [(2.1, y, z) for z, y in inset(edge, 3.3)],
            [(3.3, y, z) for z, y in inset(edge, 4.2)], [(3.3, y, z) for z, y in inset(edge, 8.6)],
            [(4.0, y, z) for z, y in inset(edge, 9.3)]], "part")
    # 윗변 아래의 환기 살
    for k in range(5):
        y = 33.4 + k * 0.62
        p.plate([(4.0, y, -9.0), (4.0, y, 9.0), (4.0, y + 0.3, 9.0), (4.0, y + 0.3, -9.0)], Vector((1.0, 0.0, 0.0)), 0.1, "dark")
    # 앞뒤로 닿는 연결판
    for z in (-1.0, 1.0):
        p.prism(((z * 16.0 - 3.0, 42.0), (z * 16.0 + 3.0, 42.0), (z * 16.0 + 3.0, 44.6), (z * 16.0 - 3.0, 44.6)), 1.7, 3.0, "metal")
    return p.finish("LS_Skirt")


def gun_house(p, s, ears, rail=False):
    """각진 포탑 몸체: 뒤로 튀어나온 꽁무니, 눕힌 앞판과 옆판, 지붕 판. 포신과 방패는 따로(gun_mount).
    rail이면 지붕에 방열 핀과 납작한 센서 창을 단다."""
    def plan(k, shift=0.0):
        return [(a * k * s, (b * k + shift) * s) for a, b in ((-9.0, -19.0), (9.0, -19.0), (12.2, -11.0), (12.2, 4.0), (7.2, 17.5), (-7.2, 17.5), (-12.2, 4.0), (-12.2, -11.0))]
    top = plan(0.7, -1.5)
    h0, h1 = 1.6 * s, 7.8 * s
    p.loft([[(a, -0.4 * s, b) for a, b in plan(0.94)], [(a, h0, b) for a, b in plan(1.0)], [(a, h1, b) for a, b in top]], "part")
    # 지붕 판과 뒤 환기구
    p.loft([[(a * 0.86, h1, b * 0.86 - 0.5 * s) for a, b in top], [(a * 0.82, h1 + 0.5 * s, b * 0.82 - 0.5 * s) for a, b in top]], "part")
    y = h1 + 0.5 * s
    if rail:
        for i in range(5):
            p.box((i - 2) * 2.0 * s, -8.0 * s, 0.18 * s, 3.5 * s, y, y + 0.9 * s, 0.0, "metal")
    else:
        p.box(3.4 * s, -7.0 * s, 1.5 * s, 1.5 * s, y, y + 0.7 * s, 0.1 * s, "metal")
        p.box(-3.4 * s, -7.0 * s, 1.5 * s, 1.5 * s, y, y + 0.7 * s, 0.1 * s, "metal")
    # 뒷면은 아래 (z -19, y 1.6)에서 위 (z -14.8, y 7.8)로 안쪽으로 눕는다
    back_n = Vector((0.0, 4.2, -6.2)).normalized()

    def on_back(x, y):
        return tuple((Vector((x, y, -19.0 + (y - 1.6) * 4.2 / 6.2)) - back_n * 0.05) * s)
    for i in range(3):
        ya = 2.3 + i * 1.2
        p.plate([on_back(-5.0, ya), on_back(5.0, ya), on_back(5.0, ya + 0.6), on_back(-5.0, ya + 0.6)], back_n, 0.15 * s, "dark")
    if ears:    # 거리 측정 귀
        for side in (-1.0, 1.0):
            p.box(side * 12.0 * s, -9.0 * s, 2.6 * s, 2.4 * s, 3.2 * s, 6.0 * s, 0.4 * s, "part")
            if rail:
                p.plate([(side * 10.4 * s, 4.0 * s, -6.65 * s), (side * 13.4 * s, 4.0 * s, -6.65 * s), (side * 13.4 * s, 5.2 * s, -6.65 * s), (side * 10.4 * s, 5.2 * s, -6.65 * s)], Vector((0.0, 0.0, 1.0)), 0.2 * s, "glass")
                continue
            p.tube((side * 13.6 * s, 4.9 * s, -9.0 * s), (side * 16.0 * s, 4.9 * s, -9.0 * s), 0.9 * s, 0.9 * s, 8, "metal", True)
            p.tube((side * 16.0 * s, 4.9 * s, -9.0 * s), (side * 16.3 * s, 4.9 * s, -9.0 * s), 0.9 * s, 0.6 * s, 8, "dark", False)


def rail_barrel(p, s, base, ax):
    """레일건 포신: 포탑 안의 각진 뿌리, 위아래 레일과 그 사이의 어두운 홈, 각진 고정 링, 포구 덮개."""
    up = Vector((0.0, 1.0, 0.0))
    up = (up - ax * up.dot(ax)).normalized()
    side = ax.cross(up).normalized()

    def sect(d, hx, hu, c, cu=0.0):
        """d 위치에서 모서리를 깎은 사각 단면. 단면은 포신 축에 수직이다."""
        pts = ((-hx + c, -hu), (hx - c, -hu), (hx, -hu + c), (hx, hu - c), (hx - c, hu), (-hx + c, hu), (-hx, hu - c), (-hx, -hu + c))
        return [tuple(base + (ax * d + side * x + up * (u + cu)) * s) for x, u in pts]

    def seg(d0, d1, a, b, paint, cu=0.0):
        p.loft([sect(d0, a[0], a[1], a[2], cu), sect(d1, b[0], b[1], b[2], cu)], paint)
    seg(0.0, 7.0, (1.8, 1.9, 0.5), (1.7, 1.8, 0.5), "dark")             # 뿌리: 포탑 안에서 시작해 앞판을 뚫는다
    seg(6.5, 41.5, (0.7, 0.68, 0.15), (0.6, 0.62, 0.15), "dark")        # 레일 사이 홈
    for cu in (-1.05, 1.05):
        seg(6.5, 41.5, (1.25, 0.45, 0.15), (1.0, 0.4, 0.15), "steel", cu)
    for d in (13.0, 20.0, 27.0, 34.0):                                  # 고정 링
        k = (d - 7.0) / 34.0
        hx, hu = 1.55 - 0.2 * k, 1.85 - 0.12 * k
        seg(d, d + 1.2, (hx, hu, 0.4), (hx, hu, 0.4), "metal")
    seg(41.0, 44.0, (1.5, 1.8, 0.5), (1.4, 1.7, 0.5), "metal")          # 포구 덮개
    seg(43.9, 44.15, (0.62, 1.3, 0.15), (0.62, 1.3, 0.15), "dark")      # 포구 홈


def main_turret():
    p = Part("turret")
    gun_house(p, 1.0, True, True)
    return p.finish("LS_MainTurret")


def gun_shield(p, s):
    """고각 축을 감싼 10각 방패: 앞판 밖으로 나오고, 거의 둥글어서 포신을 들어도 앞판에 걸치는 모양이 그대로다."""
    r, half = 3.0 * s, 5.0 * s
    ring = [(r * math.cos(math.tau * (i + 0.5) / 10), r * math.sin(math.tau * (i + 0.5) / 10)) for i in range(10)]
    p.loft([[(x, y, z) for y, z in ring] for x in (-half, half)], "metal", True)


def main_gun():
    """주포 포신 2개와 방패: 원점이 고각 축이다."""
    p = Part("turret")
    gun_shield(p, 1.0)
    for x in (-2.3, 2.3):
        rail_barrel(p, 1.0, Vector((x, 0.0, 0.0)), Vector((0.0, 0.0, 1.0)))
    return p.finish("LS_MainGun")


def second_turret():
    p = Part("turret", SECOND_SCALE)
    gun_house(p, SECOND_SCALE, False)
    return p.finish("LS_SecondaryTurret")


def second_gun():
    """부포 포신 2개와 방패: 원점이 고각 축이다. 포신은 3단 둥근 포신이다."""
    s = SECOND_SCALE
    p = Part("turret", s)
    gun_shield(p, s)
    for x in (-2.3, 2.3):
        def at(d):
            return (x * s, 0.0, d * s)
        p.tube(at(0.0), at(6.0), 1.4 * s, 1.25 * s, 8, "dark", True)
        p.tube(at(6.0), at(12.0), 1.15 * s, 1.05 * s, 8, "steel", True)
        p.tube(at(12.0), at(22.0), 1.0 * s, 0.88 * s, 8, "steel", True)
        p.tube(at(22.0), at(35.0), 0.82 * s, 0.7 * s, 8, "steel", True)
        p.tube(at(35.0), at(37.0), 0.92 * s, 0.86 * s, 8, "steel", True)
    return p.finish("LS_SecondaryGun")


# ───────────── 텍스처 ─────────────

def noise(h, w, cells, seed, rows=None):
    rng = np.random.default_rng(seed)
    gh = rows + 2 if rows else max(int(h / w * cells), 2) + 2
    gw = cells + 2
    grid = rng.random((gh, gw)).astype(np.float32)
    y = np.linspace(0.0, gh - 1.001, h, dtype=np.float32)
    x = np.linspace(0.0, gw - 1.001, w, dtype=np.float32)
    y0 = y.astype(int)
    x0 = x.astype(int)
    ty = (y - y0)[:, None]
    tx = (x - x0)[None, :]
    ty = ty * ty * (3.0 - 2.0 * ty)
    tx = tx * tx * (3.0 - 2.0 * tx)
    return (grid[y0][:, x0] * (1 - tx) + grid[y0][:, x0 + 1] * tx) * (1 - ty) + (grid[y0 + 1][:, x0] * (1 - tx) + grid[y0 + 1][:, x0 + 1] * tx) * ty


def plate_field(h, w, row_px, len_px, seed, spread=0.09):
    """줄마다 길이가 다르고 이음매가 엇갈린 장갑판. 판마다 밝기가 조금 다르고 이음매는 어둡다."""
    rng = np.random.default_rng(seed)
    out = np.ones((h, w), np.float32)
    y = 0
    while y < h:
        rh = int(row_px * rng.uniform(0.75, 1.3))
        x = -int(rng.integers(0, len_px))
        while x < w:
            pw = int(len_px * rng.uniform(0.55, 1.6))
            a, b = max(x, 0), min(x + pw, w)
            if b > a:
                out[y:y + rh, a:b] = 1.0 + (rng.random() - 0.5) * spread
                out[y:y + rh, a:min(a + 2, w)] *= 0.76
            x += pw
        out[y:min(y + 2, h), :] *= 0.76
        y += rh
    return out


def weather(h, w, seed):
    """넓고 부드러운 얼룩과, 군데군데 짧게 흐른 빗물 자국(한 칸 높이 약 8m — 띠 전체로 이어지는 커튼 무늬가 생기지 않게)."""
    blotch = noise(h, w, 9, seed) * 0.6 + noise(h, w, 26, seed + 5) * 0.4
    streak = noise(h, w, max(w // 5, 2), seed + 1, max(h // 56, 2))
    where = np.clip(noise(h, w, 14, seed + 9) * 3.0 - 1.6, 0.0, 1.0)
    return (0.93 + 0.12 * blotch) * (1.0 - 0.06 * where * np.clip(streak * 2.6 - 1.5, 0.0, 1.0))


def to_linear(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def text_layer(items):
    scene = bpy.data.scenes.new("page")
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'FLAT'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.render_aa = '16'
    scene.render.film_transparent = True
    scene.render.resolution_x = TEX
    scene.render.resolution_y = TEX
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    temp = os.path.join(bpy.app.tempdir, "ls_page.png")
    scene.render.filepath = temp
    cam = bpy.data.cameras.new("page")
    cam.type = 'ORTHO'
    cam.ortho_scale = TEX
    ob = bpy.data.objects.new("page", cam)
    ob.location = (TEX * 0.5, -TEX * 0.5, 100.0)
    scene.collection.objects.link(ob)
    scene.camera = ob
    font = bpy.data.fonts.load(FONT)
    for i, (text, x, y, size, color) in enumerate(items):
        cu = bpy.data.curves.new(f"t{i}", 'FONT')
        cu.body = text
        cu.font = font
        cu.size = size
        cu.align_x = 'CENTER'
        cu.align_y = 'CENTER'
        mat = bpy.data.materials.new(f"t{i}")
        mat.diffuse_color = (*color, 1.0)
        cu.materials.append(mat)
        t = bpy.data.objects.new(f"t{i}", cu)
        t.location = (x, -y, 0.0)
        scene.collection.objects.link(t)
    bpy.ops.render.render(write_still=True, scene="page")
    image = bpy.data.images.load(temp)
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(TEX, TEX, 4)[::-1]
    bpy.data.images.remove(image)
    bpy.data.scenes.remove(scene)
    return pixels


def make_texture():
    img = np.zeros((TEX, TEX, 3), np.float32)
    grey = np.array((0.455, 0.48, 0.51), np.float32)
    window = (0.05, 0.075, 0.1)
    door = (0.3, 0.32, 0.34)

    def row(top, y):
        return int(top + BAND_SIDE - y * PX)

    def side_rect(top, sign, z0, z1, y0, y1, color):
        a, b = sorted((TEX * 0.5 + sign * z0 * PX, TEX * 0.5 + sign * z1 * PX))
        img[row(top, y1):row(top, y0), int(a):int(b)] = color

    for top, sign, seed in ((SIDE_POS, 1.0, 3), (SIDE_NEG, -1.0, 5)):
        area = np.tile(grey, (BAND_SIDE, TEX, 1))
        area *= plate_field(BAND_SIDE, TEX, int(7.5 * PX), int(26.0 * PX), seed, 0.05)[..., None]
        area *= weather(BAND_SIDE, TEX, seed + 2)[..., None]
        # 위쪽 구조물은 조금 밝다
        area[:row(0, DECK_Y)] *= 1.06
        # 2단 위는 이음매가 적은 큰 판이다
        upper = row(0, 57.0)
        area[:upper] = (grey * 1.04) * (plate_field(upper, TEX, int(9.0 * PX), int(24.0 * PX), seed + 7, 0.03) * weather(upper, TEX, seed + 8))[..., None]
        # 아래로 갈수록 흙때가 앉고, 맨 아래는 방오 도료
        for r in range(row(0, 32.0), row(0, 16.0)):
            area[r] *= 1.0 - 0.22 * (r - row(0, 32.0)) / (row(0, 16.0) - row(0, 32.0))
        boot = row(0, 16.0)
        low = np.array((0.26, 0.105, 0.08), np.float32) * plate_field(BAND_SIDE - boot, TEX, int(7.5 * PX), int(26.0 * PX), seed + 1, 0.05)[..., None]
        area[boot:] = low * weather(BAND_SIDE - boot, TEX, seed + 3)[..., None]
        area[boot - 3:boot + 3] = (0.09, 0.09, 0.1)
        img[top:top + BAND_SIDE] = area
        # 1단과 2단: 벽은 비우고 문과 환기구만 둔다
        for z in (-92.0, -52.0, 8.0, 62.0):
            side_rect(top, sign, z - 1.2, z + 1.2, 48.4, 51.2, door)
        for z in (-72.0, -24.0, 36.0):
            side_rect(top, sign, z - 4.0, z + 4.0, 51.5, 53.3, (0.13, 0.14, 0.15))
        for z in (-44.0, -10.0, 24.0):
            side_rect(top, sign, z - 1.2, z + 1.2, 57.6, 60.4, door)
    # 앞뒤 면
    fx, fy = FRONT_RECT
    front = np.tile(grey, (BAND_SIDE, 980, 1))
    front *= plate_field(BAND_SIDE, 980, int(4.2 * PX), int(9.0 * PX), 41)[..., None]
    front *= weather(BAND_SIDE, 980, 42)[..., None]
    upper = int(BAND_SIDE - 57.0 * PX)
    front[:upper] = (grey * 1.04) * (plate_field(upper, 980, int(9.0 * PX), int(14.0 * PX), 43, 0.03) * weather(upper, 980, 44))[..., None]
    # 옆면과 같이 아래로 갈수록 흙때, 맨 아래는 방오 도료(함수·함미 아래 경사면도 이 띠를 쓴다)
    for r in range(row(0, 32.0), row(0, 16.0)):
        front[r] *= 1.0 - 0.22 * (r - row(0, 32.0)) / (row(0, 16.0) - row(0, 32.0))
    boot = row(0, 16.0)
    front[boot:] = np.array((0.26, 0.105, 0.08), np.float32) * (plate_field(BAND_SIDE - boot, 980, int(4.2 * PX), int(9.0 * PX), 45, 0.05) * weather(BAND_SIDE - boot, 980, 46))[..., None]
    front[boot - 3:boot + 3] = (0.09, 0.09, 0.1)
    img[fy:fy + BAND_SIDE, fx:fx + 980] = front

    # 갑판: 어두운 미끄럼 방지 도료, 칸마다 다른 짙기, 안내선
    deck_tone = np.array((0.27, 0.29, 0.315), np.float32)
    deck = np.tile(deck_tone, (BAND_TOP, TEX, 1))
    deck *= plate_field(BAND_TOP, TEX, int(6.0 * PX), int(18.0 * PX), 21, 0.06)[..., None]
    deck *= weather(BAND_TOP, TEX, 22)[..., None]
    img[TOP_BAND:TOP_BAND + BAND_TOP] = deck
    mid = TOP_BAND + BAND_TOP * 0.5
    yy, xx = np.mgrid[0:BAND_TOP, 0:TEX]
    yy = (yy + TOP_BAND).astype(np.float32)
    xx = xx.astype(np.float32)
    line = (0.62, 0.63, 0.6)

    def deck_ring(z, x, r, width=1.8):
        d = np.hypot(xx - (TEX * 0.5 + z * PX), yy - (mid - x * PX))
        img[TOP_BAND:TOP_BAND + BAND_TOP][(d > r * PX - width) & (d < r * PX + width)] = line

    def deck_rect(z0, z1, x0, x1, color):
        a, b = sorted((TEX * 0.5 + z0 * PX, TEX * 0.5 + z1 * PX))
        c, d = sorted((mid - x0 * PX, mid - x1 * PX))
        img[int(c):int(d), int(a):int(b)] = color
    for x, y, z, _ in MAIN_TURRETS:
        deck_ring(z, x, 16.5)
    for x, y, z, _ in SECOND_TURRETS:
        deck_ring(z, x, 8.5)
    for side in (-1.0, 1.0):
        deck_rect(-98.0, 98.0, side * 46.8, side * 47.2, line)          # 갑판 가장자리 안내선
        deck_rect(42.0, 96.0, side * 36.0, side * 46.0, deck_tone * 0.78)
        deck_rect(-96.0, -42.0, side * 36.0, side * 46.0, deck_tone * 0.78)
        deck_rect(-146.0, -122.0, side * 3.0, side * 22.0, deck_tone * 0.8)
    # 1단 지붕 자리는 선체 회색이다. 앞뒤로 좁아지는 평면을 따른다
    roof_z = (xx - TEX * 0.5) / PX
    roof_half = np.interp(roof_z, (-106.0, -52.0, 44.0, 100.0), (20.4, 33.4, 33.4, 16.4))
    roof_mask = (np.abs(yy - mid) <= roof_half * PX) & (roof_z >= -106.0) & (roof_z <= 100.0)
    img[TOP_BAND:TOP_BAND + BAND_TOP][roof_mask] = grey * 0.8
    deck_rect(165.0, 212.0, -0.2, 0.2, line)                            # 앞갑판 중앙선
    deck_ring(-236.0, 0.0, 15.0, 3.0)                                   # 함미 헬기 갑판

    # 스커트 칸: 모양에 맞춘 칸 — 바깥 경사(0~1.1m) · 테두리(1.1~2.4) · 홈(2.4~3.3) · 경사(3.3~4.2) · 주 장갑 띠(4.2~8.6) · 경사(8.6~9.3) · 돋은 판(9.3~)
    # (skirt()의 inset 값과 같다). 리벳은 테두리 가운데, 주 장갑 띠 안팎, 돋은 판 가장자리를 따라 고르게 둔다.
    ox, oy, ppm = SKIRT_RECT
    h, w = 760, 1000
    sy, sx = np.mgrid[0:h, 0:w].astype(np.float32)
    sz = (sx - 500.0) / ppm
    syy = (720.0 - sy) / ppm + 9.0
    edge = hexagon(0.0, 24.5, 9.0, 44.0)
    dist = np.full((h, w), 1e9, np.float32)
    for i in range(len(edge)):
        az, ay = edge[i]
        bz, by = edge[(i + 1) % len(edge)]
        ez, ey = bz - az, by - ay
        k = np.clip(((sz - az) * ez + (syy - ay) * ey) / (ez * ez + ey * ey), 0.0, 1.0)
        dist = np.minimum(dist, np.hypot(sz - (az + ez * k), syy - (ay + ey * k)))
    base = grey * 1.02
    skirt_px = np.tile(base, (h, w, 1))
    tone = np.ones((h, w), np.float32)
    tone[dist < 1.1] = 0.9
    tone[(dist >= 1.1) & (dist < 2.4)] = 0.97
    tone[(dist >= 2.4) & (dist < 3.3)] = 0.42
    tone[(dist >= 3.3) & (dist < 4.2)] = 0.9
    tone[(dist >= 8.6) & (dist < 9.3)] = 0.92
    tone[dist >= 9.3] = 1.05
    # 주 장갑 띠는 여섯 장(꼭짓점마다 이음매), 돋은 판은 가로 이음매 하나
    band = (dist >= 4.2) & (dist < 8.6)
    cz, cy = 0.0, 26.5
    for vz, vy in edge:
        dz, dy = cz - vz, cy - vy
        ln = max(np.hypot(dz, dy), 1e-3)
        dz, dy = dz / ln, dy / ln
        along = (sz - vz) * dz + (syy - vy) * dy
        off = np.abs((sz - vz) * dy - (syy - vy) * dz)
        tone[band & (off < 0.08) & (along > 0.0) & (along < 12.0)] *= 0.72
    tone[(dist >= 9.3) & (np.abs(syy - 24.0) < 0.07)] *= 0.75
    skirt_px *= tone[..., None]
    skirt_px *= weather(h, w, 32)[..., None]
    # 아래쪽은 흙때
    grime = np.clip((26.0 - syy) / 17.0, 0.0, 1.0)
    skirt_px *= (1.0 - 0.3 * grime)[..., None]

    def rivets(d, spacing):
        poly = inset(edge, d)
        n = len(poly)
        for i in range(n):
            a = Vector(poly[i])
            b = Vector(poly[(i + 1) % n])
            count = max(int((b - a).length / spacing), 1)
            for k in range(count):
                c = a + (b - a) * ((k + 0.5) / count)
                u = int(500.0 + c.x * ppm)
                v = int(720.0 - (c.y - 9.0) * ppm)
                if 3 <= v < h - 3 and 3 <= u < w - 3:
                    skirt_px[v - 2:v + 3, u - 2:u + 3] *= 0.62
                    skirt_px[v - 2:v, u - 2:u + 1] *= 1.9
    rivets(1.75, 1.6)
    rivets(4.75, 2.0)
    rivets(8.05, 2.0)
    rivets(9.9, 2.0)
    img[oy:oy + h, ox:ox + w] = skirt_px
    img[oy:oy + h, ox:ox + w] = skirt_px

    # 포탑 칸
    ox, oy, ppm = TURRET_RECT
    h, w = 720, 1240
    turret_px = np.tile(grey * 1.04, (h, w, 1))
    turret_px *= plate_field(h, w, int(4.0 * ppm), int(8.0 * ppm), 35, 0.05)[..., None]
    turret_px *= weather(h, w, 36)[..., None]
    img[oy:oy + h, ox:ox + w] = turret_px

    for name, i in SWATCHES.items():
        x = i * CELL
        img[SWATCH_Y:SWATCH_Y + CELL, x:x + CELL] = COLORS[name]
    items = []
    for top, sign in ((SIDE_POS, 1.0), (SIDE_NEG, -1.0)):
        items.append(("LB-01", TEX * 0.5 + sign * 205.0 * PX, top + BAND_SIDE - 42.5 * PX, 62, (0.1, 0.11, 0.12)))
    items.append(("H", TEX * 0.5 - 236.0 * PX, mid, 170, (0.72, 0.72, 0.68)))
    page = text_layer(items)
    img = to_linear(img)
    alpha = page[..., 3:4]
    img = img * (1.0 - alpha) + page[..., :3] * alpha
    os.makedirs(OUT, exist_ok=True)
    tex = bpy.data.images.new("ACO_LandShip_Albedo", TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), np.float32)
    rgba[..., :3] = np.where(img <= 0.0031308, img * 12.92, 1.055 * np.power(np.maximum(img, 1e-6), 1.0 / 2.4) - 0.055)
    tex.pixels.foreach_set(rgba[::-1].ravel())
    tex.filepath_raw = ALBEDO
    tex.file_format = 'PNG'
    tex.save()
    return tex


def material(tex):
    mat = bpy.data.materials.new("ACO_LandShip")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = tex
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.62
    if os.environ.get("ACO_BACKFACE_RED"):
        mark_backfaces(mat)
    return mat


def mark_backfaces(mat):
    """검사용: 뒷면(노멀 반대쪽)을 빨갛게 그린다 — Unity는 뒷면을 그리지 않으므로 빨간 곳이 구멍으로 보인다."""
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    out = nodes["Material Output"]
    shader = out.inputs["Surface"].links[0].from_socket
    geo = nodes.new("ShaderNodeNewGeometry")
    red = nodes.new("ShaderNodeEmission")
    red.inputs["Color"].default_value = (1.0, 0.0, 0.0, 1.0)
    red.inputs["Strength"].default_value = 3.0
    mix = nodes.new("ShaderNodeMixShader")
    links.new(geo.outputs["Backfacing"], mix.inputs["Fac"])
    links.new(shader, mix.inputs[1])
    links.new(red.outputs["Emission"], mix.inputs[2])
    links.new(mix.outputs["Shader"], out.inputs["Surface"])


def glow_material():
    mat = bpy.data.materials.new("ACO_LandShip_Glow")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.3, 0.75, 1.0, 1.0)
    bsdf.inputs["Emission Color" if "Emission Color" in bsdf.inputs else "Emission"].default_value = (0.3, 0.75, 1.0, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 3.0
    if os.environ.get("ACO_BACKFACE_RED"):
        mark_backfaces(mat)
    return mat


def assemble(meshes):
    """오브젝트를 자리에 놓는다. 포신은 포탑의 자식이다. 돌아가는 부품은 원점이 축이라 회전만으로 움직인다."""
    scene = bpy.context.scene
    parts = {"guns": [], "shutters": [], "pods": [], "skirts": []}

    def add(name, mesh, loc, rot=(0.0, 0.0, 0.0), parent=None):
        ob = bpy.data.objects.new(name, meshes[mesh])
        scene.collection.objects.link(ob)
        ob.parent = parent
        ob.location = to_blender(loc)
        ob.rotation_euler = rot
        return ob
    add("LS_Hull", "LS_Hull", (0.0, 0.0, 0.0))
    for i, (x, y, z, yaw) in enumerate(MAIN_TURRETS):
        turret = add(f"LS_MainTurret_{i + 1}", "LS_MainTurret", (x, y, z), (0.0, 0.0, math.radians(yaw)))
        parts["guns"].append(add(f"LS_MainGun_{i + 1}", "LS_MainGun", MAIN_GUN_PIVOT, parent=turret))
    for x, y, z, yaw in SECOND_TURRETS:
        tag = ("F" if z > 0.0 else "R") + ("L" if x < 0.0 else "R")
        turret = add(f"LS_SecondaryTurret_{tag}", "LS_SecondaryTurret", (x, y, z), (0.0, 0.0, math.radians(yaw)))
        parts["guns"].append(add(f"LS_SecondaryGun_{tag}", "LS_SecondaryGun", SECOND_GUN_PIVOT, parent=turret))
    for side in (1.0, -1.0):        # 왼쪽은 오른쪽을 Y로 180도 돌린 것
        parts["shutters"].append(add("LS_CoreShutter_" + ("R" if side > 0.0 else "L"), "LS_CoreShutter",
                                     (side * CORE_HINGE[0], CORE_HINGE[1], CORE_HINGE[2]), (0.0, 0.0, 0.0 if side > 0.0 else math.pi)))
    pods = sorted([(POD_X, z) for z in POD_Z] + [(INNER_POD_X, z) for z in INNER_POD_Z], key=lambda q: -q[1])
    skirts = sorted([(SKIRT_X, z) for z in SKIRT_Z] + [(INNER_SKIRT_X, z) for z in INNER_POD_Z], key=lambda q: -q[1])
    for key, mesh, spots in (("pods", "LS_TrackPod", pods), ("skirts", "LS_Skirt", skirts)):
        for side in (1.0, -1.0):
            for i, (x, z) in enumerate(spots):
                ob = add(f"{mesh}_{'R' if side > 0.0 else 'L'}{i + 1}", mesh, (side * x, 0.0, z), (0.0, 0.0, 0.0 if side > 0.0 else math.pi))
                parts[key].append((ob, side, x, z))
    return parts


def preview(parts, folder, only, elev=3.0, shutter=0.0, strip=""):
    scene = bpy.context.scene
    for gun in parts["guns"]:
        gun.rotation_euler = (-math.radians(elev), 0.0, 0.0)
    for ob in parts["shutters"]:
        ob.rotation_euler[1] = -math.radians(shutter)
    for key in ("pods", "skirts"):
        for ob, side, x, z in parts[key]:
            if side > 0.0:
                skirt = key == "skirts"
                mid = x == POD_X or skirt
                if (strip == "mid" and mid and abs(z) < 30.0) or (strip == "skirt" and skirt) or (strip == "track" and mid):
                    ob.hide_render = True
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 36
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 3
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 760
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.72, 0.85, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.85
    scene.world = world
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = 3.2
    sun.angle = math.radians(2.0)
    sun_ob = bpy.data.objects.new("sun", sun)
    sun_ob.rotation_euler = (Vector(to_blender((0.0, 0.0, 0.0))) - Vector(to_blender((420.0, 620.0, 240.0)))).to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(sun_ob)
    scene.render.image_settings.file_format = 'PNG'
    os.makedirs(folder, exist_ok=True)
    ground = bpy.data.meshes.new("ground")
    ground.from_pydata([(-3000, -3000, 0), (3000, -3000, 0), (3000, 3000, 0), (-3000, 3000, 0)], [], [(0, 1, 2, 3)])
    gm = bpy.data.materials.new("g")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.2, 0.2, 0.15, 1.0)
    ground.materials.append(gm)
    ground_ob = bpy.data.objects.new("ground", ground)
    scene.collection.objects.link(ground_ob)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 50
    cam_data.clip_end = 6000.0
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shoot(name, eye, at, under=False):
        if only and name not in only:
            return
        ground_ob.hide_render = under       # 아래에서 볼 때는 지면을 치운다
        e = Vector(to_blender(eye))
        cam.location = e
        cam.rotation_euler = (Vector(to_blender(at)) - e).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    shoot("ls_quarter", (430.0, 190.0, 560.0), (0.0, 40.0, 40.0))
    shoot("ls_bow", (170.0, 34.0, 430.0), (0.0, 32.0, 245.0))
    shoot("ls_stern", (-150.0, 16.0, -430.0), (0.0, 30.0, -245.0))
    shoot("ls_side", (860.0, 40.0, 5.0), (0.0, 50.0, 5.0))
    shoot("ls_rear", (-350.0, 180.0, -470.0), (0.0, 40.0, -15.0))
    shoot("ls_top", (80.0, 950.0, 6.0), (0.0, 30.0, 5.0))
    shoot("ls_bottom", (80.0, -900.0, 6.0), (0.0, 20.0, 5.0), True)
    shoot("ls_high", (420.0, 520.0, 420.0), (0.0, 30.0, 10.0))
    shoot("ls_low", (150.0, 14.0, 330.0), (0.0, 40.0, 0.0))
    shoot("ls_near_bridge", (95.0, 112.0, 125.0), (0.0, 84.0, -4.0))
    shoot("ls_bridge_front", (0.0, 96.0, 190.0), (0.0, 84.0, 0.0))
    shoot("ls_bridge_side", (125.0, 102.0, 0.0), (0.0, 86.0, -8.0))
    shoot("ls_bridge_rear", (-85.0, 118.0, -125.0), (0.0, 86.0, -18.0))
    shoot("ls_near_vls", (85.0, 92.0, 150.0), (0.0, 58.0, 60.0))
    shoot("ls_near_track", (150.0, 26.0, 70.0), (50.0, 24.0, 60.0))
    shoot("ls_near_front", (90.0, 26.0, 205.0), (24.0, 24.0, 135.0))
    shoot("ls_near_turret", (62.0, 88.0, 236.0), (0.0, 54.0, 172.0))
    shoot("ls_gun_root", (26.0, 66.0, 236.0), (0.0, 56.0, 212.0))
    shoot("ls_gun_side", (95.0, 66.0, 196.0), (0.0, 66.0, 190.0))
    shoot("ls_tex_skirt", (88.0, 30.0, -40.0), (49.0, 26.0, -27.0))
    shoot("ls_tex_hull", (90.0, 40.0, 215.0), (20.0, 30.0, 180.0))
    shoot("ls_tex_deck", (60.0, 95.0, 30.0), (20.0, 48.0, -20.0))
    shoot("ls_tex_stern", (-60.0, 60.0, -330.0), (0.0, 40.0, -250.0))
    shoot("ls_near_core", (-120.0, 100.0, -170.0), (0.0, 66.0, -70.0))
    shoot("ls_core", (66.0, 104.0, -26.0), (0.0, 64.0, -76.0))
    shoot("ls_vent", (140.0, 26.0, -10.0), (29.0, 22.0, 0.0))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = make_texture()
    mat = material(tex)
    scene = bpy.context.scene
    glow = glow_material()
    meshes = {}
    for me in (hull(), track_pod(), skirt(), main_turret(), main_gun(), second_turret(), second_gun(), core_shutter()):
        me.materials.append(mat)
        if any(f.material_index == 1 for f in me.polygons):
            me.materials.append(glow)
        meshes[me.name] = me
        print("MESH", me.name, sum(len(f.vertices) - 2 for f in me.polygons))
    parts = assemble(meshes)
    print("TRIS 함선 전체", sum(sum(len(f.vertices) - 2 for f in ob.data.polygons) for ob in scene.objects if ob.type == 'MESH'))
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    if argv:
        elev, shutter = 3.0, 0.0
        for a in argv[1:]:
            if a.startswith("elev="):
                elev = float(a[5:])
            if a.startswith("open="):
                shutter = float(a[5:])
        strip = ""
        for a in argv[1:]:
            if a == "strip":
                strip = "mid"
            if a.startswith("strip="):
                strip = a[6:]
        names = [a for a in argv[1:] if "=" not in a and a != "strip"]
        preview(parts, argv[0], names, elev, shutter, strip)


main()
