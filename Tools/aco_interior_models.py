"""함내 격실 모듈 모델. 플레이어가 가까이서 보는 부품이라 모서리를 깎고 노멀 맵을 쓴다.
Blender 안에서 돌린다: blender --background --factory-startup --python Tools/aco_interior_models.py
만드는 것: Models/Interior/ACO_Interior.fbx, Textures/Interior/ACO_Int_*.png.
작업 축: x 오른쪽, y 앞, z 위, 단위 m. 내보낼 때 Z축으로 180도 돌려 Unity에서 (x, z 위, y 앞)이 된다.
메시 이름은 "모듈__재질"이다. 키 150cm 안팎의 사용자가 앉거나 서서 쓰는 크기로 잡았다."""
import math
import os
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
MODEL_DIR = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Interior")
TEXTURE_DIR = os.path.join(ROOT, "Assets", "ACO_Addon", "Textures", "Interior")
FBX_NAME = "ACO_Interior.fbx"
FONT = "C:/Windows/Fonts/arialbd.ttf"
# 재질 = (타일 크기 m, 노멀 맵 여부)
TILES = {"wall": 1.0, "paint": 1.2, "white": 1.2, "deck": 1.2, "door": 1.2, "alum": 0.6, "steel": 0.6, "fabric": 0.35, "rubber": 0.5, "red": 0.6, "tube": 1.0, "diffuser": 1.0, "label": 1.0, "strip": 0.28, "blue": 0.6, "wood": 0.8, "screen": 1.0, "sign": 1.0, "cable": 0.3, "cork": 0.5, "print": 1.0, "led": 1.0, "mat": 0.5, "keys": 1.0, "ball": 0.1, "bed": 0.5, "blanket": 0.5, "glass": 1.0, "gold": 0.3, "silver": 0.3, "canvas": 0.5, "orange": 0.5, "webbing": 0.25, "curtain": 0.5, "exit": 1.0}
CHAIR_LABELS = ("CO", "XO", "OPS", "MAINT", "SAFETY", "ADMIN", "INTEL", "LSO", "PILOT", "PILOT", "PILOT", "PILOT")


class Part:
    """모듈 하나. 재질별 메시를 모은다."""

    def __init__(self, name):
        self.name = name
        self.bm = {}

    def mesh_for(self, mat):
        if mat not in self.bm:
            self.bm[mat] = bmesh.new()   # UV 층은 from_mesh가 가져온다. 미리 만들면 빈 층이 하나 더 남아 UV0이 0이 된다
        return self.bm[mat]

    def add(self, mat, temp, select=None, uv=None):
        """temp를 재질 메시에 합친다. 면마다 상자 투영 UV를 준다. select면 그 면만, uv면 그 함수로 UV를 낸다."""
        layer = temp.loops.layers.uv.verify()
        tile = TILES[mat]
        temp.normal_update()
        for f in temp.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                c = loop.vert.co
                if uv:
                    loop[layer].uv = uv(c)
                elif ax == 0:
                    loop[layer].uv = (c.y / tile, c.z / tile)
                elif ax == 1:
                    loop[layer].uv = (c.x / tile, c.z / tile)
                else:
                    loop[layer].uv = (c.x / tile, c.y / tile)
        if select:
            bmesh.ops.delete(temp, geom=[f for f in temp.faces if not select(f)], context='FACES')
        me = bpy.data.meshes.new("t")
        temp.to_mesh(me)
        self.mesh_for(mat).from_mesh(me)
        bpy.data.meshes.remove(me)

    @staticmethod
    def cube(center, size, bevel, segs, rot=None):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=size, verts=bm.verts)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=bevel, offset_type='OFFSET', segments=segs, profile=0.5, affect='EDGES', clamp_overlap=True)
        if rot:
            bmesh.ops.transform(bm, matrix=rot, verts=bm.verts)
        bmesh.ops.translate(bm, vec=center, verts=bm.verts)
        return bm

    def box(self, mat, center, size, bevel=0.0, segs=1, rot=None, uv=None):
        bm = self.cube(center, size, bevel, segs, rot)
        self.add(mat, bm, None, uv)
        bm.free()

    def span(self, mat, a, b, bevel=0.0, segs=1, uv=None):
        """a, b = 두 꼭짓점."""
        c = [(a[i] + b[i]) / 2 for i in range(3)]
        s = [abs(a[i] - b[i]) for i in range(3)]
        self.box(mat, c, s, bevel, segs, None, uv)

    def ring(self, mat, center, axis, R, r, n=20, sides=6):
        """고리: axis에 수직인 원을 따라 도는 관."""
        c = Vector(center)
        a = Vector(axis).normalized()
        u = a.orthogonal().normalized()
        v = a.cross(u)
        bm = bmesh.new()
        rings = []
        for i in range(n):
            t = i / n * math.tau
            d = u * math.cos(t) + v * math.sin(t)
            rings.append([bm.verts.new(c + d * (R + r * math.cos(k / sides * math.tau)) + a * (r * math.sin(k / sides * math.tau))) for k in range(sides)])
        for i in range(n):
            for k in range(sides):
                q = [rings[i][k], rings[(i + 1) % n][k], rings[(i + 1) % n][(k + 1) % sides], rings[i][(k + 1) % sides]]
                bm.faces.new(q)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.add(mat, bm)
        bm.free()

    def loop_ring(self, mat, outer, inner, y0, y1):
        """같은 점 수의 두 윤곽 사이를 y0~y1 두께의 고리로 만든다. 윤곽은 (x, z)."""
        bm = bmesh.new()
        rings = [[bm.verts.new((q[0], y, q[1])) for q in loop] for loop, y in ((outer, y0), (outer, y1), (inner, y1), (inner, y0))]
        n = len(outer)
        for a in range(4):
            r0, r1 = rings[a], rings[(a + 1) % 4]
            for k in range(n):
                bm.faces.new([r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.add(mat, bm)
        bm.free()

    def bar(self, mat, a, b, y, w=0.05, t=0.012):
        """y 높이에 놓인 납작한 막대. a, b = (x, z) 양 끝."""
        d = Vector((b[0] - a[0], b[1] - a[1]))
        rot = Matrix.Rotation(-math.atan2(d.y, d.x), 4, 'Y')
        self.box(mat, ((a[0] + b[0]) / 2, y, (a[1] + b[1]) / 2), (d.length + w, t, w), 0.004, 1, rot)

    def move(self, v):
        for bm in self.bm.values():
            bmesh.ops.translate(bm, vec=v, verts=bm.verts)

    def mirror_x(self):
        """x를 뒤집는다. 경첩이 반대쪽인 문을 만든다."""
        for bm in self.bm.values():
            bmesh.ops.scale(bm, vec=(-1, 1, 1), verts=bm.verts)
            bmesh.ops.reverse_faces(bm, faces=bm.faces)

    def sphere(self, mat, c, r, seg=12, rings=8):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
        bmesh.ops.translate(bm, vec=c, verts=bm.verts)
        self.add(mat, bm)
        bm.free()

    def lathe(self, mat, c, profile, n=16):
        """Z축 회전체. profile = (반경, 높이) 목록, 아래에서 위로."""
        bm = bmesh.new()
        rings = []
        for r, z in profile:
            rings.append([bm.verts.new((c[0] + math.cos(k / n * math.tau) * r, c[1] + math.sin(k / n * math.tau) * r, c[2] + z)) for k in range(n)])
        for a, b in zip(rings, rings[1:]):
            for k in range(n):
                bm.faces.new([a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]])
        bm.faces.new(rings[0])
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.add(mat, bm)
        bm.free()

    def tube(self, mat, pts, r, sides=8):
        """점들을 따라 꺾이는 관. 양 끝이 막혀 있다."""
        pts = [Vector(q) for q in pts]
        bm = bmesh.new()
        rings = []
        u = None
        for i in range(len(pts)):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            u = t.orthogonal().normalized() if u is None else (u - t * u.dot(t)).normalized()
            v = t.cross(u)
            rings.append([bm.verts.new(pts[i] + (u * math.cos(k / sides * math.tau) + v * math.sin(k / sides * math.tau)) * r) for k in range(sides)])
        for a, b in zip(rings, rings[1:]):
            for k in range(sides):
                bm.faces.new([a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]])
        bm.faces.new(rings[0])
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.add(mat, bm)
        bm.free()

    def grid(self, mat, rows, closed=False, up=None):
        """점 격자를 면으로 잇는다. closed면 각 줄의 끝과 처음을 잇는다. 열린 면은 up 쪽이 앞면이 된다."""
        bm = bmesh.new()
        vs = [[bm.verts.new(q) for q in row] for row in rows]
        n = len(rows[0])
        for a, b in zip(vs, vs[1:]):
            for k in range(n if closed else n - 1):
                bm.faces.new([a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]])
        if closed:
            bm.faces.new(vs[0])
            bm.faces.new(vs[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if up is not None:
            bm.normal_update()
            if sum(f.normal.dot(Vector(up)) for f in bm.faces) < 0:
                bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
        self.add(mat, bm)
        bm.free()

    def wall_gland(self, x, z, r):
        """관이 벽으로 들어가는 자리의 받침과 고정 나사. 벽면 y = 0."""
        self.cyl("steel", (x, 0, z), (x, 0.006, z), r + 0.016, 12)
        self.cyl("steel", (x, 0.006, z), (x, 0.016, z), r + 0.005, 12)
        self.bolts([(x + s_ * (r + 0.009), 0.006, z) for s_ in (-1, 1)], (0, 1, 0), 0.0035, 0.003, 6)

    def conduit_up(self, mat, x, y, z0, z1, r, sides=8):
        """z0에서 위로 올라가 z1에서 벽 속으로 꺾이는 전선관. 벽면 y = 0."""
        R = min(y, z1 - z0)
        pts = [(x, y, z0), (x, y, z1 - R)]
        pts += arc_pts((x, y, z1 - R), (0, 0, 1), (0, -1, 0), R, 5)
        pts.append((x, -0.012, z1))
        self.tube(mat, pts, r, sides)
        self.wall_gland(x, z1, r)

    def bolts(self, points, axis, r=0.007, h=0.005, n=6, mat="steel"):
        a = Vector(axis).normalized()
        for q in points:
            q = Vector(q)
            self.cyl(mat, q, q + a * h, r, n)

    def cyl(self, mat, a, b, r, n=14, r2=None):
        a, b = Vector(a), Vector(b)
        d = b - a
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=n, radius1=r, radius2=r2 if r2 is not None else r, depth=d.length)
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        bmesh.ops.transform(bm, matrix=q.to_matrix().to_4x4(), verts=bm.verts)
        bmesh.ops.translate(bm, vec=(a + b) / 2, verts=bm.verts)
        self.add(mat, bm)
        bm.free()

    def extrude(self, mat, outline, y0, y1, bevel=0.0):
        """x-z 평면 윤곽을 y 방향으로 민다. 오목해도 된다."""
        bm = bmesh.new()
        verts = [bm.verts.new((x, y0, z)) for x, z in outline]
        face = bm.faces.new(verts)
        res = bmesh.ops.extrude_face_region(bm, geom=[face])
        moved = [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=(0, y1 - y0, 0), verts=moved)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if bevel > 0:
            edges = [e for e in bm.edges if abs(e.verts[0].co.y - e.verts[1].co.y) < 1e-6]
            bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type='OFFSET', segments=2, profile=0.5, affect='EDGES', clamp_overlap=True)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        self.add(mat, bm)
        bm.free()

    def objects(self, scene, mats=None, matrix=None, smooth=40.0):
        out = []
        for mat, bm in self.bm.items():
            if not bm.faces:
                continue
            name = self.name + "__" + mat
            me = bpy.data.meshes.new(name)
            bm.to_mesh(me)
            if is_light_target(self.name, mat):
                add_light_uv(me, 0 if mat == "deck" else 1)
            me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
            me.set_sharp_from_angle(angle=math.radians(smooth))
            me.update()
            if mats:
                me.materials.append(mats[mat])
            o = bpy.data.objects.new(name, me)
            if matrix is not None:
                o.matrix_world = matrix
            scene.collection.objects.link(o)
            out.append(o)
        return out

    def tris(self):
        return sum(len(f.verts) - 2 for bm in self.bm.values() for f in bm.faces)


def arc_pts(a, d0, d1, R, n=5):
    """a에서 d0 방향으로 출발해 d1 방향으로 끝나는 사분원의 점들. a는 뺀다."""
    a, d0, d1 = Vector(a), Vector(d0), Vector(d1)
    c = a + d1 * R
    return [c - d1 * (R * math.cos(i / n * math.pi / 2)) + d0 * (R * math.sin(i / n * math.pi / 2)) for i in range(1, n + 1)]


TRIM_INSET = 0.05       # 코너에서 벽 띠를 줄이는 길이. 맞은편 벽의 띠와 겹치지 않는다
TRIM_RIB = 0.087        # 늑골 가운데에서 벽 띠 끝까지. 늑골 날개 가장자리에 닿는다
TRIM_CORNER_RIB = 0.135 # 코너에 늑골이 있는 벽의 띠 끝
TRIM_Z = 1.05


def rounded(outline, r, n=2, sharp=()):
    """윤곽의 꼭짓점을 둥글린다. sharp 번호의 꼭짓점은 그대로 둔다."""
    out = []
    m = len(outline)
    for i in range(m):
        if i in sharp:
            out.append(tuple(outline[i]))
            continue
        p0, p1, p2 = Vector(outline[i - 1]), Vector(outline[i]), Vector(outline[(i + 1) % m])
        a, b = (p0 - p1), (p2 - p1)
        k = min(r, a.length * 0.45, b.length * 0.45)
        s, e = p1 + a.normalized() * k, p1 + b.normalized() * k
        for j in range(n + 1):
            t = j / n
            q = s * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + e * t * t
            out.append((q.x, q.y))
    return out


# ── 모듈 ──
PANEL_W = 1.4
T_PANEL = PANEL_W


def panel(name, h):
    """타공 벽판. 벽면이 y = 0이고 방 쪽이 +y다. 위아래 눌림 띠에 나사 줄이 있다."""
    p = Part(name)
    p.span("wall", (0, 0, 0), (PANEL_W, 0.02, h), 0.003)
    t = 0.05
    for z0 in (0.0, h - t):
        zc = z0 + t / 2
        p.span("strip", (0, 0, z0), (PANEL_W, 0.027, z0 + t), 0.004, 1, lambda c, zc=zc: (c.x / 0.28, (c.z - zc) / 0.28 + 0.5))
    for x0 in (0.0, PANEL_W - 0.03):
        p.span("paint", (x0, 0, t), (x0 + 0.03, 0.024, h - t), 0.003)
    return p


def rib(h):
    """벽 늑골: T자 보. 벽면 y = 0에서 방 쪽으로 나온다. 앞 날개에 볼트 줄, 위아래에 받침판."""
    p = Part("Int_Rib_%d" % round(h * 100))
    p.span("paint", (-0.088, 0, 0), (0.088, 0.012, h), 0.003)
    p.span("paint", (-0.012, 0, 0), (0.012, 0.09, h), 0.003)
    p.span("strip", (-0.05, 0.09, 0), (0.05, 0.11, h), 0.005, 2, lambda c: (c.z / 0.28, c.x / 0.28 + 0.5))
    # 위아래 받침판
    for sx in (-1, 1):
        p.span("paint", (sx * 0.012 - 0.003, 0.012, 0.0), (sx * 0.08 + 0.003, 0.09, 0.012), 0.002)
        p.span("paint", (sx * 0.012 - 0.003, 0.012, h - 0.034), (sx * 0.08 + 0.003, 0.09, h - 0.022), 0.002)
    return p


def rib_flat(h):
    """열린 문짝이 앞을 지나가는 자리의 낮은 늑골. 벽 띠와 같은 높이까지만 나온다."""
    p = Part("Int_RibFlat_%d" % round(h * 100))
    p.span("paint", (-0.088, 0, 0), (0.088, 0.012, h), 0.003)
    p.span("strip", (-0.05, 0.012, 0), (0.05, 0.022, h), 0.003, 1, lambda c: (c.z / 0.28, c.x / 0.28 + 0.5))
    return p


def beam(length):
    """천장 보. x 방향, 윗면 z = 0에서 아래로."""
    p = Part("Int_Beam_%d" % round(length * 100))
    p.span("paint", (0, -0.075, -0.012), (length, 0.075, 0), 0.003)
    p.span("paint", (0, -0.008, -0.2), (length, 0.008, -0.012), 0.002)
    p.span("paint", (0, -0.07, -0.214), (length, 0.07, -0.2), 0.004, 2)
    return p


def fixture():
    """매달린 형광등 1.25m. 등기구 윗면 z = 0, 가운데가 원점, 길이는 x 방향. 천장에서 봉 둘로 내려온다. 천장은 z = 0.24다."""
    p = Part("Int_Fixture")
    L, W = 1.25, 0.24
    p.span("white", (-L / 2, -W / 2, -0.03), (L / 2, W / 2, 0), 0.006, 2)
    # 반사판: 가운데가 볼록한 V자
    for s in (-1, 1):
        bm = Part.cube((0, s * 0.05, -0.042), (L - 0.1, 0.115, 0.004), 0, 1, Matrix.Rotation(math.radians(-14 * s), 4, 'X'))
        p.add("white", bm)
        bm.free()
    for s in (-1, 1):
        p.span("white", (-L / 2, s * W / 2 - 0.012, -0.08), (L / 2, s * W / 2 + 0.012, -0.03), 0.004, 2)
    for x in (-L / 2 + 0.03, L / 2 - 0.03):
        p.span("white", (x - 0.018, -W / 2, -0.08), (x + 0.018, W / 2, -0.03), 0.004, 2)
        for s in (-1, 1):
            p.span("steel", (x - 0.022, s * 0.055 - 0.02, -0.075), (x + 0.022, s * 0.055 + 0.02, -0.035), 0.004)
    for s in (-1, 1):
        p.cyl("tube", (-L / 2 + 0.055, s * 0.055, -0.056), (L / 2 - 0.055, s * 0.055, -0.056), 0.0165, 8)
        for x in (-L / 2 + 0.06, L / 2 - 0.06):
            p.cyl("steel", (x - 0.012, s * 0.055, -0.056), (x + 0.012, s * 0.055, -0.056), 0.0175, 8)
    # 보호망: 얇은 철선 띠 5줄
    for x in (-0.44, -0.22, 0.0, 0.22, 0.44):
        p.span("steel", (x - 0.004, -W / 2, -0.086), (x + 0.004, W / 2, -0.081))
    for y in (-0.055, 0.055):
        p.span("steel", (-L / 2 + 0.03, y - 0.003, -0.09), (L / 2 - 0.03, y + 0.003, -0.084))
    # 매달기: 윗면 받침 채널, 나사봉 둘, 천장 받침, 전선관
    p.span("steel", (-0.5, -0.02, 0.0), (0.5, 0.02, 0.016), 0.003)
    for x in (-0.42, 0.42):
        p.cyl("steel", (x, 0, 0.016), (x, 0, 0.24), 0.008, 6)
        p.cyl("steel", (x, 0, 0.226), (x, 0, 0.24), 0.03, 8)
    p.cyl("paint", (0.1, 0, 0.016), (0.1, 0, 0.24), 0.011, 6)
    return p


def pipe(L=1.2):
    """보온재를 감은 관. x 방향, 축 높이가 원점. 이음 플랜지, 띠, 천장 행거가 있다. 행거는 축에서 0.33 위 천장까지 간다."""
    p = Part("Int_Pipe_%d" % round(L * 100))
    r = 0.08
    f = L / 1.2
    p.cyl("white", (0, 0, 0), (L, 0, 0), r, 18)
    for x in (0.2, 0.42, 0.78, 1.0):
        p.cyl("steel", (x * f - 0.008, 0, 0), (x * f + 0.008, 0, 0), r + 0.002, 18)
    # 이음 플랜지와 볼트
    c = L / 2
    for x in (c - 0.015, c + 0.015):
        p.cyl("steel", (x - 0.015, 0, 0), (x + 0.015, 0, 0), r + 0.03, 20)
    for k in range(8):
        t = k / 8 * math.tau
        y, z = math.cos(t) * (r + 0.018), math.sin(t) * (r + 0.018)
        p.cyl("steel", (c - 0.04, y, z), (c + 0.04, y, z), 0.007, 6)
    # 행거: 고리 + 나사봉
    hx = 0.25 * L
    p.ring("steel", (hx, 0, 0), (1, 0, 0), r + 0.006, 0.009, 18, 5)
    p.cyl("steel", (hx, 0, r + 0.008), (hx, 0, 0.33), 0.008, 6)
    p.cyl("steel", (hx, 0, r + 0.03), (hx, 0, r + 0.042), 0.014, 6)
    return p


def valve():
    """배관 밸브: 벽 쪽 관에서 나온 몸체와 손잡이 바퀴. 벽면 y = 0, 바퀴 축이 +y."""
    p = Part("Int_Valve")
    p.cyl("red", (0, 0, 0), (0, 0.14, 0), 0.045, 14)
    p.cyl("red", (0, 0.05, 0), (0, 0.08, 0), 0.062, 14)
    p.cyl("steel", (0, 0.14, 0), (0, 0.2, 0), 0.016, 8)
    p.ring("red", (0, 0.2, 0), (0, 1, 0), 0.085, 0.009, 22, 6)
    for k in range(3):
        t = k / 3 * math.tau
        p.cyl("red", (0, 0.2, 0), (math.cos(t) * 0.085, 0.2, math.sin(t) * 0.085), 0.008, 6)
    p.cyl("red", (0, 0.195, 0), (0, 0.215, 0), 0.022, 10)
    return p


def tray(L=1.2):
    """사다리형 케이블 트레이. x 방향, 바닥 z = 0, 가운데 y = 0. 전선 묶음과 천장 행거가 있다. 행거는 0.3 위 천장까지 간다."""
    p = Part("Int_Tray_%d" % round(L * 100))
    W, H = 0.32, 0.07
    f = L / 1.2
    for s in (-1, 1):
        p.span("steel", (0, s * W / 2 - 0.004, 0), (L, s * W / 2 + 0.004, H), 0.0015)
        p.span("steel", (0, s * W / 2 - 0.02, 0), (L, s * W / 2 + 0.02, 0.006), 0.0015)
        p.span("steel", (0, s * W / 2 - 0.016, H - 0.005), (L, s * W / 2 + 0.016, H), 0.0015)
    for k in range(5):
        x = (0.12 + k * 0.24) * f
        p.span("steel", (x - 0.018, -W / 2, 0), (x + 0.018, W / 2, 0.012), 0.002)
    wires = ((-0.11, 0.015, "rubber"), (-0.075, 0.01, "rubber"), (-0.05, 0.012, "blue"), (-0.02, 0.016, "rubber"), (0.02, 0.012, "red"), (0.05, 0.01, "rubber"), (0.085, 0.014, "rubber"), (0.115, 0.009, "blue"))
    for y, r, m in wires:
        p.cyl(m, (0, y, 0.012 + r), (L, y, 0.012 + r), r, 8)
    # 묶음 끈
    for x in (0.3 * L, 0.7 * L):
        p.span("rubber", (x - 0.006, -0.13, 0.04), (x + 0.006, 0.13, 0.05), 0.001)
    # 행거: 양쪽 나사봉과 받침 채널
    hx = L / 2
    p.span("steel", (hx - 0.02, -W / 2 - 0.05, -0.03), (hx + 0.02, W / 2 + 0.05, 0.0), 0.003)
    for s in (-1, 1):
        p.cyl("steel", (hx, s * (W / 2 + 0.03), -0.04), (hx, s * (W / 2 + 0.03), 0.3), 0.007, 6)
        p.cyl("steel", (hx, s * (W / 2 + 0.03), 0.0), (hx, s * (W / 2 + 0.03), 0.012), 0.013, 6)
    return p


HATCH_W, HATCH_H = 1.1, 1.95
# 천장 설비 한 칸 길이: 격실 벽에서 벽까지 딱 맞게 나눈다
BRIEF_RUN, CIC_RUN, CORR_RUN, BERTH_RUN = 1.2, 7.8 / 6, 14.2 / 12, 6.2 / 5


def hatch_outline(halfw, z0, z1, r):
    out = []
    for cx, cz, a0 in ((halfw - r, z1 - r, 0), (-halfw + r, z1 - r, 90), (-halfw + r, z0 + r, 180), (halfw - r, z0 + r, 270)):
        for k in range(7):
            t = math.radians(a0 + k * 15)
            out.append((cx + math.cos(t) * r, cz + math.sin(t) * r))
    return out


def hatch_frame():
    """수밀문 틀. 개구부 폭 1.1m, 높이 1.95m, 가운데 바닥이 원점, 방 쪽이 +y. 벽색으로 칠한 턱과 깊은 틀이다."""
    p = Part("Int_HatchFrame")
    W, H = HATCH_W, HATCH_H
    inner = hatch_outline(W / 2, 0.02, H + 0.02, 0.27)
    mid = hatch_outline(W / 2 + 0.05, -0.0, H + 0.07, 0.31)
    outer = hatch_outline(W / 2 + 0.14, -0.02, H + 0.16, 0.38)
    p.loop_ring("paint", outer, mid, -0.01, 0.032)
    p.loop_ring("white", mid, inner, -0.18, 0.058)
    # 틀판 볼트
    rv = [(s_ * (W / 2 + 0.1), 0.032, z) for s_ in (-1, 1) for z in (0.42, 0.76, 1.1, 1.44, 1.78)]
    rv += [(x, 0.032, H + 0.115) for x in (-0.2, 0.2)]
    p.bolts(rv, (0, 1, 0), 0.01, 0.005, 6)
    # 경첩 받침 3개와 잠금 쇠 6개
    for z in (0.36, H / 2 + 0.02, H - 0.32):
        p.span("steel", (W / 2 + 0.02, 0.0, z - 0.07), (W / 2 + 0.1, 0.06, z + 0.07), 0.004, 2)
        p.cyl("steel", (W / 2 + 0.06, 0.075, z - 0.05), (W / 2 + 0.06, 0.075, z + 0.05), 0.017, 10)
    for x, z in ((-W / 2 - 0.05, 0.34), (-W / 2 - 0.05, H / 2 + 0.02), (-W / 2 - 0.05, H - 0.3), (W / 2 + 0.0, 0.2), (W / 2 + 0.0, H - 0.16)):
        p.span("steel", (x - 0.035, 0.02, z - 0.04), (x + 0.035, 0.05, z + 0.04), 0.008, 2)
    return p


def hatch_leaf():
    """수밀문 문짝. 원점이 경첩 축이고 닫힌 상태에서 -x 쪽으로 뻗는다. 가운데 손잡이 바퀴에서 막대가 둘레의 잠금 레버로 뻗는다."""
    p = Part("Int_HatchLeaf")
    W, H = HATCH_W, HATCH_H
    cz = H / 2 + 0.02
    y0, y1 = 0.066, 0.1
    leaf = hatch_outline(W / 2 + 0.032, 0.0, H + 0.052, 0.295)
    p.extrude("door", leaf, y0, y1, 0.007)
    p.loop_ring("rubber", hatch_outline(W / 2 + 0.03, 0.002, H + 0.05, 0.295), hatch_outline(W / 2 - 0.015, 0.04, H + 0.0, 0.26), 0.056, y0)
    # 바퀴: 양면
    for side, face in ((1, y1), (-1, y0)):
        p.cyl("door", (0, face, cz), (0, face + side * 0.018, cz), 0.1, 18)
        for k in range(12):
            t = k / 12 * math.tau
            c = (math.cos(t) * 0.105, face + side * 0.009, cz + math.sin(t) * 0.105)
            p.box("door", c, (0.02, 0.018, 0.016), 0, 1, Matrix.Rotation(-t, 4, 'Y'))
        hub = face + side * 0.072
        p.cyl("steel", (0, face, cz), (0, hub + side * 0.02, cz), 0.018, 10)
        p.cyl("rubber", (0, hub - side * 0.015, cz), (0, hub + side * 0.015, cz), 0.038, 12)
        p.ring("rubber", (0, hub, cz), (0, 1, 0), 0.165, 0.012, 26, 7)
        for k in range(5):
            t = k / 5 * math.tau + 0.3
            p.cyl("rubber", (0, hub, cz), (math.cos(t) * 0.165, hub, cz + math.sin(t) * 0.165), 0.0085, 6)
    # 방 쪽 면: 막대 6개와 잠금 레버
    dogs = [(-W / 2 + 0.03, 0.34, -1), (-W / 2 + 0.03, cz, -1), (-W / 2 + 0.03, H - 0.3, -1), (W / 2 - 0.09, 0.2, 1), (W / 2 - 0.09, H - 0.16, 1), (0.0, H + 0.0, 0)]
    for k, (x, z, dx) in enumerate(dogs[:5]):
        t = math.atan2(z - cz, x)
        start = (math.cos(t) * 0.075, cz + math.sin(t) * 0.075)
        p.bar("door", start, (x, z), y1 + 0.02, 0.046, 0.012)
        p.cyl("steel", (x, y1, z), (x, y1 + 0.036, z), 0.03, 10)
        p.cyl("steel", (start[0], y1 + 0.018, start[1]), (start[0], y1 + 0.032, start[1]), 0.02, 8)
        # 레버: 틀 쪽으로 넘어간다
        end = (x + (dx or 0) * 0.13, z)
        p.bar("steel", (x, z), end, y1 + 0.036, 0.04, 0.016)
        p.cyl("steel", (x, y1 + 0.036, z), (x, y1 + 0.052, z), 0.016, 8)
    # 경첩 띠
    for z in (0.36, cz, H - 0.32):
        p.span("steel", (W / 2 - 0.32, y1, z - 0.05), (W / 2 + 0.06, y1 + 0.016, z + 0.05), 0.004, 2)
        p.bolts([(W / 2 - dx, y1 + 0.016, z + dz) for dx in (0.06, 0.26) for dz in (-0.028, 0.028)], (0, 1, 0), 0.008, 0.004)
    # D자 손잡이: 양면
    for side, face in ((1, y1), (-1, y0)):
        hx = -W / 2 + 0.22
        z0, z1 = cz + 0.3, cz + 0.62
        for z in (z0, z1):
            p.cyl("steel", (hx, face, z), (hx, face + side * 0.06, z), 0.011, 8)
        p.cyl("steel", (hx, face + side * 0.06, z0 - 0.011), (hx, face + side * 0.06, z1 + 0.011), 0.011, 8)
    # 경첩 축이 원점이 되게 옮긴다
    p.move((-(W / 2 + 0.06), -0.075, 0))
    return p


PICTURE_W, PICTURE_H = 0.62, 0.46
PHOTO_W, PHOTO_H = 0.42, 0.28
PHOTO_COUNT = 7


def hatch_wall(h):
    """수밀문이 들어가는 벽판 1.4m 폭. 원점 = 가운데 바닥, 방 쪽 +y. 구멍은 문틀 판 바깥선보다 조금 안쪽이다."""
    p = Part("Int_HatchWall_%d" % round(h * 100))
    W, H = HATCH_W, HATCH_H
    hole = hatch_outline(W / 2 + 0.1, -0.02, H + 0.12, 0.36)
    x0, x1, z0, z1 = -0.7, 0.7, -0.02, h
    outer = []
    corners = ((x1, z1), (x0, z1), (x0, z0), (x1, z0))
    for g in range(4):
        pts = hole[g * 7:(g + 1) * 7]
        c = Vector(corners[g])
        if g == 0:
            a, b = Vector((x1, pts[0][1])), Vector((pts[6][0], z1))
        elif g == 1:
            a, b = Vector((pts[0][0], z1)), Vector((x0, pts[6][1]))
        elif g == 2:
            a, b = Vector((x0, pts[0][1])), Vector((pts[6][0], z0))
        else:
            a, b = Vector((pts[0][0], z0)), Vector((x1, pts[6][1]))
        for k in range(7):
            q = a.lerp(c, k / 3) if k <= 3 else c.lerp(b, (k - 3) / 3)
            outer.append((q.x, q.y))
    p.loop_ring("wall", outer, hole, 0.0, 0.02)
    p.span("strip", (-0.7, 0, h - 0.05), (0.7, 0.027, h), 0.004, 1, lambda c: (c.x / 0.28, (c.z - h + 0.025) / 0.28 + 0.5))
    return p


def bookcase():
    """벽 책장 폭 0.95m, 깊이 0.3m, 높이 1.9m. 벽면 y = 0, 바닥 가운데가 원점, 방 쪽이 +y."""
    p = Part("Int_Bookcase")
    W, D, H = 0.95, 0.3, 1.9
    t = 0.02
    for s in (-1, 1):
        p.span("wood", (s * W / 2 - t, 0, 0), (s * W / 2 + t, D, H), 0.003)
    p.span("wood", (-W / 2, 0, H - 2 * t), (W / 2, D + 0.015, H), 0.004)
    p.span("wood", (-W / 2, 0, 0), (W / 2, D, 0.08), 0.003)
    p.span("rubber", (-W / 2 + t, 0, 0.08), (W / 2 - t, 0.012, H))
    rows = (0.08, 0.5, 0.92, 1.3, 1.62)
    for z in rows[1:]:
        p.span("wood", (-W / 2, 0, z - t / 2), (W / 2, D - 0.01, z + t / 2), 0.002)
    colors = ("blue", "white", "red", "rubber", "door", "paint", "blue", "white")
    seed = 7
    for r in range(4):
        z0, z1 = rows[r] + (t / 2 if r else 0), rows[r + 1] - t / 2
        x = -W / 2 + t + 0.01
        limit = W / 2 - t - (0.18 if r == 1 else 0.02)
        n = 0
        while x < limit - 0.05:
            seed = (seed * 37 + 11) % 101
            w = 0.03 + (seed % 5) * 0.012
            if x + w > limit:
                break
            h = (z1 - z0) * (0.72 + (seed % 7) * 0.035)
            d = 0.2 + (seed % 3) * 0.02
            p.span(colors[seed % len(colors)], (x, D - 0.03 - d, z0), (x + w - 0.002, D - 0.03, z0 + h), 0.002)
            x += w
            n += 1
            if n % 9 == 0:
                x += 0.09
    # 맨 위칸의 상자
    p.span("door", (-0.4, 0.04, 1.63), (-0.08, 0.27, 1.82), 0.004)
    p.span("paint", (0.03, 0.04, 1.63), (0.4, 0.27, 1.76), 0.004)
    return p


def pc_set():
    """책상 위 조작용 PC: 모니터, 키보드, 트랙볼, 본체. 책상 윗면 z = 0, 사용자는 -y에 앉는다."""
    p = Part("Int_PCSet")
    p.span("rubber", (-0.13, 0.1, 0), (0.13, 0.27, 0.012), 0.004)
    p.span("rubber", (-0.022, 0.155, 0.012), (0.022, 0.19, 0.15), 0.004)
    p.span("rubber", (-0.3, 0.13, 0.11), (0.3, 0.165, 0.47), 0.008, 2)
    p.span("screen", (-0.285, 0.128, 0.125), (0.285, 0.131, 0.455))
    keyboard(p, -0.02, -0.1, 0, -1)
    trackball(p, 0.29, -0.1, 0, -1)
    p.span("rubber", (-0.5, 0.0, 0), (-0.32, 0.27, 0.36), 0.006, 2)
    p.span("steel", (-0.46, -0.003, 0.2), (-0.36, 0.0, 0.3))
    return p


def swivel():
    """회전의자. 앉는 사람이 +y를 본다."""
    p = Part("Int_Swivel")
    for k in range(5):
        t = k / 5 * math.tau
        p.cyl("rubber", (0, 0, 0.07), (math.cos(t) * 0.27, math.sin(t) * 0.27, 0.05), 0.02, 6)
        p.cyl("rubber", (math.cos(t) * 0.27, math.sin(t) * 0.27, 0.0), (math.cos(t) * 0.27, math.sin(t) * 0.27, 0.05), 0.025, 8)
    p.cyl("steel", (0, 0, 0.07), (0, 0, 0.38), 0.03, 10)
    p.box("fabric", (0, 0, 0.41), (0.44, 0.44, 0.07), 0.03, 2)
    tilt = Matrix.Rotation(math.radians(8), 4, 'X')
    p.box("fabric", (0, -0.23, 0.72), (0.4, 0.06, 0.46), 0.03, 2, tilt)
    p.box("steel", (0, -0.235, 0.47), (0.06, 0.03, 0.2), 0.008, 1)
    return p


def hatch_trim():
    """맞은편 방의 문틀 판. 깊은 틀은 반대쪽 Int_HatchFrame이 맡는다."""
    p = Part("Int_HatchTrim")
    W, H = HATCH_W, HATCH_H
    outer = hatch_outline(W / 2 + 0.14, -0.02, H + 0.16, 0.38)
    # 안쪽 선을 문틀 관보다 5mm 안으로 넣어 두 면이 겹치지 않게 한다
    inner = hatch_outline(W / 2 - 0.005, 0.025, H + 0.015, 0.265)
    p.loop_ring("paint", outer, inner, -0.04, 0.032)
    p.bolts([(s_ * (W / 2 + 0.1), 0.032, z) for s_ in (-1, 1) for z in (0.42, 0.76, 1.1, 1.44, 1.78)], (0, 1, 0), 0.01, 0.005, 6)
    return p


def plot_table():
    """전술 테이블. 윗면 0.65m, 반경 1.2m, 바닥 가운데가 원점. 유리판 위에 Unity 지도가 뜬다."""
    p = Part("Int_PlotTable")
    n = 40
    p.cyl("steel", (0, 0, 0), (0, 0, 0.05), 0.9, n)
    p.cyl("paint", (0, 0, 0.05), (0, 0, 0.5), 0.72, n)
    p.cyl("paint", (0, 0, 0.5), (0, 0, 0.59), 0.72, n, 1.17)
    p.cyl("steel", (0, 0, 0.59), (0, 0, 0.645), 1.2, n)
    p.ring("rubber", (0, 0, 0.645), (0, 0, 1), 1.135, 0.016, n, 5)
    p.cyl("screen", (0, 0, 0.645), (0, 0, 0.652), 1.12, n)
    p.bolts([(math.cos(k / 16 * math.tau) * 1.175, math.sin(k / 16 * math.tau) * 1.175, 0.645) for k in range(16)], (0, 0, 1), 0.009, 0.004, 6)
    # 가장자리 조작부 4개: 경사 판, 단추, 표시등
    for k in range(4):
        t = k / 4 * math.tau + math.pi / 4
        c, s_ = math.cos(t), math.sin(t)
        rot = Matrix.Rotation(t + math.pi / 2, 4, 'Z')
        p.box("rubber", (c * 1.255, s_ * 1.255, 0.624), (0.36, 0.13, 0.05), 0.008, 2, rot)
        for j in range(5):
            o = -0.12 + j * 0.06
            x, y = c * 1.255 - s_ * o, s_ * 1.255 + c * o
            p.cyl("steel", (x, y, 0.645), (x, y, 0.653), 0.012, 6)
        x, y = c * 1.285, s_ * 1.285
        p.cyl("led", (x + s_ * 0.13, y - c * 0.13, 0.645), (x + s_ * 0.13, y - c * 0.13, 0.651), 0.006, 6)
    return p


def cic_desk():
    """CIC 철제 책상 1.8m. 벽면 y = 0, 왼쪽 끝이 x = 0, 사용자는 +y에 앉는다. 윗면 0.68m."""
    p = Part("Int_CicDesk")
    L, D, H = 1.8, 0.7, 0.68
    p.span("paint", (0, 0, H - 0.03), (L, D, H), 0.006, 2)
    p.span("rubber", (0.015, 0.02, H), (L - 0.015, D - 0.015, H + 0.003))
    for x0 in (0.0, L - 0.42):
        p.span("paint", (x0, 0.02, 0.06), (x0 + 0.42, D - 0.03, H - 0.03), 0.004, 1)
        p.span("rubber", (x0 + 0.02, 0.02, 0.0), (x0 + 0.4, D - 0.08, 0.06))
        for k in range(3):
            z = 0.09 + k * 0.175
            p.span("paint", (x0 + 0.02, D - 0.03, z), (x0 + 0.4, D - 0.016, z + 0.155), 0.004, 1)
            p.span("steel", (x0 + 0.15, D - 0.016, z + 0.1), (x0 + 0.27, D - 0.006, z + 0.115), 0.002)
    p.span("paint", (0.42, 0.02, 0.2), (L - 0.42, 0.05, H - 0.03), 0.002)
    return p


def keyboard(p, cx, cy, z, toward=1):
    """키보드. 가운데 (cx, cy), 책상면 z. toward = 사용자가 있는 y 방향(+1, -1). 사용자에게서 먼 쪽이 높다. 키는 텍스처에 있다."""
    w, d = 0.43, 0.15
    turn = Matrix.Rotation(0 if toward > 0 else math.pi, 4, 'Z')
    tilt = turn @ Matrix.Rotation(math.radians(-5), 4, 'X')
    t3 = tilt.to_3x3()
    c = Vector((cx, cy, z + 0.0155))
    p.box("rubber", c, (w, d, 0.018), 0.006, 2, tilt)
    top = c + t3 @ Vector((0, 0, 0.0095))

    def uv(co):
        q = t3.transposed() @ (co - top)
        return (0.5 - q.x / (w - 0.016), 0.5 + q.y / (d - 0.014))

    p.box("keys", top, (w - 0.016, d - 0.014, 0.004), 0, 1, tilt, uv)
    fy = cy - toward * (d / 2 - 0.018)
    p.span("rubber", (cx - 0.19, fy - 0.012, z), (cx + 0.19, fy + 0.012, z + 0.022), 0.003)


def trackball(p, cx, cy, z, toward=1):
    """트랙볼: 낮은 몸체, 공, 고리, 앞쪽 단추 둘, 손목 받침. toward = 사용자가 있는 y 방향. 단추는 몸체 윗면 평평한 곳 안에 들어 있다."""
    def y(v):
        return cy + toward * v

    p.span("rubber", (cx - 0.066, y(-0.08), z), (cx + 0.066, y(0.062), z + 0.034), 0.014, 2)
    p.span("rubber", (cx - 0.06, y(0.058), z), (cx + 0.06, y(0.098), z + 0.018), 0.008, 2)
    p.ring("steel", (cx, y(-0.03), z + 0.034), (0, 0, 1), 0.04, 0.005, 16, 5)
    p.sphere("ball", (cx, y(-0.03), z + 0.044), 0.033)
    for s_ in (-1, 1):
        p.span("steel", (cx + s_ * 0.03 - 0.02, y(0.02), z + 0.031), (cx + s_ * 0.03 + 0.02, y(0.044), z + 0.0365), 0.0015)


def station():
    """책상 위 근무석 장비: 큰 모니터 하나, 키보드, 트랙볼. 책상 윗면 z = 0, 벽 쪽이 -y. 키 150cm 기준 눈높이에 맞춘다."""
    p = Part("Int_Station")
    w, h, z0 = 0.7, 0.42, 0.13
    # 받침 기둥은 화면 뒤(-y)에 있다
    p.span("rubber", (-0.13, 0.02, 0), (0.13, 0.2, 0.012), 0.004)
    p.span("rubber", (-0.03, 0.085, 0.012), (0.03, 0.125, z0 + 0.2), 0.004)
    p.span("rubber", (-0.07, 0.115, z0 + 0.12), (0.07, 0.135, z0 + 0.26), 0.003)
    p.span("rubber", (-w / 2, 0.135, z0), (w / 2, 0.172, z0 + h), 0.006, 2)
    p.span("screen", (-w / 2 + 0.014, 0.172, z0 + 0.014), (w / 2 - 0.014, 0.175, z0 + h - 0.014))
    # 사용자는 +y에서 벽을 본다. 사용자의 오른손이 -x 쪽이다
    keyboard(p, 0.05, 0.4, 0)
    trackball(p, -0.28, 0.39, 0)
    return p


def rack():
    """장비 랙. 폭 0.6, 깊이 0.6, 높이 2.0. 벽면 y = 0, 바닥 가운데가 원점, 앞이 +y. 칸마다 손잡이와 표시등."""
    p = Part("Int_Rack")
    W, D, H = 0.6, 0.6, 2.0
    p.span("steel", (-W / 2, 0, 0.06), (W / 2, D - 0.03, H), 0.006, 2)
    p.span("rubber", (-W / 2 + 0.02, 0.02, 0), (W / 2 - 0.02, D - 0.06, 0.06))
    p.span("steel", (-W / 2, D - 0.03, 0.06), (-W / 2 + 0.025, D, H), 0.003)
    p.span("steel", (W / 2 - 0.025, D - 0.03, 0.06), (W / 2, D, H), 0.003)
    z = 0.1
    k = 0
    for h in (0.34, 0.2, 0.2, 0.26, 0.2, 0.2, 0.34):
        p.span("rubber", (-W / 2 + 0.03, D - 0.03, z), (W / 2 - 0.03, D - 0.012, z + h - 0.012), 0.003)
        for sx in (-1, 1):
            p.span("steel", (sx * 0.24 - 0.008, D - 0.012, z + h / 2 - 0.05), (sx * 0.24 + 0.008, D + 0.008, z + h / 2 + 0.05), 0.002)
        if k % 2 == 0:
            for j in range(9):
                p.span("steel", (-0.18, D - 0.012, z + 0.03 + j * 0.03), (0.18, D - 0.008, z + 0.04 + j * 0.03))
        else:
            for j in range(5):
                p.span("led", (-0.17 + j * 0.045, D - 0.012, z + h - 0.05), (-0.145 + j * 0.045, D - 0.008, z + h - 0.035))
            p.cyl("steel", (0.12, D - 0.012, z + h * 0.45), (0.12, D + 0.006, z + h * 0.45), 0.022, 10)
        z += h
        k += 1
    return p


def radio_desk():
    """무전석 책상 2.8m. 벽면 y = 0, 가운데가 원점, 사용자는 +y에 앉는다. 무전기, 스피커, 마이크, 송수화기가 있다."""
    p = Part("Int_RadioDesk")
    L, D, H = 2.8, 0.7, 0.68
    p.span("paint", (-L / 2, 0, H - 0.03), (L / 2, D, H), 0.006, 2)
    p.span("rubber", (-L / 2 + 0.015, 0.02, H), (L / 2 - 0.015, D - 0.015, H + 0.003))
    p.span("paint", (-L / 2, 0.02, 0.06), (L / 2, D - 0.2, H - 0.03), 0.004, 1)
    p.span("rubber", (-L / 2 + 0.02, 0.02, 0), (L / 2 - 0.02, D - 0.26, 0.06))
    for k in range(5):
        x = -L / 2 + 0.28 + k * 0.56
        p.span("paint", (x - 0.25, D - 0.2, 0.09), (x + 0.25, D - 0.188, H - 0.06), 0.003)
        p.span("steel", (x - 0.06, D - 0.188, H - 0.14), (x + 0.06, D - 0.178, H - 0.125), 0.002)
    # 무전기 본체
    tz = H + 0.003
    x0, x1, y0, y1, h = -0.55, 0.5, 0.06, 0.34, 0.24
    p.span("paint", (x0, y0, tz), (x1, y1, tz + h), 0.008, 2)
    p.span("rubber", (x0 + 0.02, y1, tz + 0.02), (x1 - 0.02, y1 + 0.006, tz + h - 0.02), 0.002)
    for k in range(5):
        x = x0 + 0.1 + k * 0.1
        p.cyl("steel", (x, y1 + 0.006, tz + 0.08), (x, y1 + 0.03, tz + 0.08), 0.019, 10)
        p.span("led", (x - 0.012, y1 + 0.006, tz + 0.15), (x + 0.012, y1 + 0.009, tz + 0.17))
    p.span("screen", (x0 + 0.62, y1 + 0.006, tz + 0.1), (x1 - 0.06, y1 + 0.009, tz + 0.2))
    p.cyl("steel", (x1 - 0.16, y1 + 0.006, tz + 0.06), (x1 - 0.16, y1 + 0.03, tz + 0.06), 0.03, 12)
    # 스피커
    p.span("paint", (-1.18, 0.05, tz), (-0.78, 0.25, tz + 0.3), 0.01, 2)
    for k in range(7):
        p.span("rubber", (-1.12, 0.25, tz + 0.05 + k * 0.03), (-0.84, 0.254, tz + 0.062 + k * 0.03))
    # 마이크
    mx, my = 0.72, 0.36
    p.cyl("rubber", (mx, my, tz), (mx, my, tz + 0.03), 0.075, 14)
    pts = [(mx, my, tz + 0.03), (mx, my, tz + 0.14), (mx, my + 0.03, tz + 0.24), (mx, my + 0.09, tz + 0.3)]
    for a, b in zip(pts, pts[1:]):
        p.cyl("steel", a, b, 0.006, 6)
    p.cyl("rubber", pts[-1], (mx, my + 0.15, tz + 0.33), 0.018, 8)
    p.cyl("led", (mx + 0.04, my - 0.03, tz + 0.03), (mx + 0.04, my - 0.03, tz + 0.036), 0.012, 8)
    # 송수화기 받침
    hx = 1.05
    p.span("rubber", (hx - 0.09, 0.05, tz), (hx + 0.09, 0.39, tz + 0.05), 0.012, 2)
    for y in (0.1, 0.34):
        p.cyl("rubber", (hx, y, tz + 0.046), (hx, y, tz + 0.08), 0.038, 12, 0.03)
    p.span("rubber", (hx - 0.024, 0.07, tz + 0.072), (hx + 0.024, 0.37, tz + 0.108), 0.012, 2)
    # 일지와 펜
    p.span("white", (-0.72, 0.4, tz), (-0.42, 0.62, tz + 0.012), 0.002)
    pencil(p, (-0.46, 0.45, tz + 0.016), (-0.62, 0.56, tz + 0.016))
    return p


def spectator_desk():
    """관전 패널 조작대 2.6m. 벽면 y = 0, 가운데가 원점, 사용자는 +y. 철제 장 위에 쐐기꼴 경사 콘솔이 붙어 있다."""
    p = Part("Int_SpectatorDesk")
    L, D, H = 2.6, 0.5, 0.7
    a = math.radians(16)
    ty = D + 0.02
    front, back = H + 0.03, H + 0.03 + ty * math.tan(a)
    p.span("paint", (-L / 2, 0, 0.05), (L / 2, D, H), 0.008, 2)
    p.span("rubber", (-L / 2 + 0.03, 0.02, 0), (L / 2 - 0.03, D - 0.05, 0.05))
    for k in range(4):
        x = -L / 2 + 0.33 + k * 0.64
        p.span("paint", (x - 0.27, D - 0.012, 0.11), (x + 0.27, D + 0.002, 0.64), 0.004, 1)
    # 쐐기: 장 윗면에 그대로 얹힌다. 양끝은 볼록한 옆판
    wedge = [(0.0, H - 0.002), (ty, H - 0.002), (ty, front), (0.0, back)]
    p.grid("paint", [[(x, q[0], q[1]) for q in wedge] for x in (-L / 2 + 0.02, L / 2 - 0.02)], True)
    cheek = [(0.0, H - 0.002), (ty + 0.008, H - 0.002), (ty + 0.008, front + 0.012), (0.0, back + 0.012)]
    for x0, x1 in ((-L / 2, -L / 2 + 0.02), (L / 2 - 0.02, L / 2)):
        p.grid("paint", [[(x, q[0], q[1]) for q in cheek] for x in (x0, x1)], True)
    # 경사면 좌표: x, 판 위 앞뒤(앞이 +), 판에서의 높이
    tilt = Matrix.Rotation(-a, 4, 'X')
    t3 = tilt.to_3x3()
    o = Vector((0, ty / 2, (front + back) / 2))
    n = t3 @ Vector((0, 0, 1))
    length = ty / math.cos(a)

    def at(x, ly, h=0.0):
        return o + t3 @ Vector((x, ly, h))

    def plate(mat, x0, x1, l0, l1, h0, h1, bevel=0.0):
        c = at((x0 + x1) / 2, (l0 + l1) / 2, (h0 + h1) / 2)
        p.box(mat, c, (abs(x1 - x0), abs(l1 - l0), abs(h1 - h0)), bevel, 1, tilt)

    plate("rubber", -L / 2 + 0.04, L / 2 - 0.04, -length / 2 + 0.03, length / 2 - 0.03, -0.002, 0.006)
    # 칸막이 띠
    for x in (-0.8, -0.26, 0.26, 0.8):
        plate("steel", x - 0.006, x + 0.006, -length / 2 + 0.05, length / 2 - 0.05, 0.005, 0.009)

    def keys(cx, l0, cols, rows, pitch=0.05):
        for r in range(rows):
            for c in range(cols):
                q = at(cx + (c - (cols - 1) / 2) * pitch, l0 + r * 0.045)
                p.cyl("steel", q + n * 0.005, q + n * 0.013, 0.014, 6)

    def knob(x, ly):
        q = at(x, ly)
        p.cyl("steel", q + n * 0.005, q + n * 0.01, 0.03, 12)
        p.cyl("rubber", q + n * 0.01, q + n * 0.036, 0.021, 10, 0.017)
        p.cyl("led", q + n * 0.036, q + n * 0.0375, 0.004, 6)

    def lamps(x, ly, count, gap=0.03):
        for k in range(count):
            q = at(x + k * gap, ly)
            p.cyl("led", q + n * 0.005, q + n * 0.009, 0.007, 6)

    for side in (-1, 1):
        # 바깥칸: 키 묶음, 다이얼 둘
        cx = side * 1.03
        keys(cx, -0.03, 6, 4)
        knob(cx - 0.11, -0.12)
        knob(cx + 0.11, -0.12)
        lamps(cx - 0.03, -0.12, 3)
        # 안쪽칸: 작은 화면과 소프트키
        cx = side * 0.53
        plate("steel", cx - 0.21, cx + 0.21, -0.17, 0.05, 0.005, 0.01, 0.002)
        plate("screen", cx - 0.19, cx + 0.19, -0.15, 0.03, 0.01, 0.012)
        for k in range(6):
            x = cx + (k - 2.5) * 0.065
            plate("steel", x - 0.024, x + 0.024, 0.075, 0.105, 0.005, 0.014, 0.002)
        lamps(cx - 0.045, 0.135, 4)
    # 가운데칸: 판에 수직으로 선 스틱, 트랙볼, 키
    q = at(-0.09, -0.03)
    p.cyl("steel", q + n * 0.005, q + n * 0.012, 0.05, 14)
    p.cyl("rubber", q + n * 0.012, q + n * 0.05, 0.04, 12, 0.014)
    p.cyl("steel", q + n * 0.05, q + n * 0.115, 0.007, 8)
    p.cyl("rubber", q + n * 0.115, q + n * 0.17, 0.015, 10, 0.017)
    q = at(0.12, -0.03)
    p.cyl("steel", q + n * 0.005, q + n * 0.012, 0.052, 14)
    p.sphere("ball", q + n * 0.014, 0.036, 12, 8)
    keys(0.0, 0.1, 6, 2, 0.04)
    # 앞 가장자리 손목 받침
    p.cyl("rubber", (-L / 2 + 0.02, ty + 0.012, front - 0.002), (L / 2 - 0.02, ty + 0.012, front - 0.002), 0.024, 10)
    return p


def pipe_small(name, mat, r=0.04, L=1.2):
    """가는 관. x 방향, 축 높이가 원점. 이음과 행거가 있다."""
    p = Part(name)
    p.cyl(mat, (0, 0, 0), (L, 0, 0), r, 12)
    p.cyl("steel", (L / 2 - 0.015, 0, 0), (L / 2 + 0.015, 0, 0), r + 0.016, 12)
    p.ring("steel", (L / 4, 0, 0), (1, 0, 0), r + 0.004, 0.006, 14, 4)
    p.cyl("steel", (L / 4, 0, r), (L / 4, 0, 0.33), 0.006, 6)
    return p


def seal_round(name, r):
    """관이 벽을 지나는 자리. 벽면 y = 0, 관 축이 원점."""
    p = Part(name)
    p.cyl("steel", (0, 0, 0), (0, 0.036, 0), r + 0.05, 18)
    p.cyl("steel", (0, 0.036, 0), (0, 0.06, 0), r + 0.014, 18)
    p.bolts([(math.cos(k / 6 * math.tau) * (r + 0.034), 0.036, math.sin(k / 6 * math.tau) * (r + 0.034)) for k in range(6)], (0, 1, 0), 0.007, 0.005, 6)
    return p


def seal_frame(name, w, z0, z1):
    """트레이나 덕트가 벽을 지나는 자리의 틀. 벽면 y = 0, 가운데가 x = 0."""
    p = Part(name)
    t = 0.035
    p.span("steel", (-w / 2 - t, 0, z0 - t), (w / 2 + t, 0.036, z0))
    p.span("steel", (-w / 2 - t, 0, z1), (w / 2 + t, 0.036, z1 + t))
    for s_ in (-1, 1):
        p.span("steel", (s_ * w / 2, 0, z0), (s_ * (w / 2 + t), 0.036, z1))
    return p


def notices():
    """코르크 판에 붙인 종이. 벽면 y = 0, 가운데가 원점. 코르크 앞면 y = 0.052에 붙는다."""
    p = Part("Int_Notices")
    y = 0.052
    for x, z, w, h in ((-0.48, 0.24, 0.21, 0.3), (-0.22, 0.26, 0.21, 0.3), (0.07, 0.22, 0.3, 0.21), (0.44, 0.24, 0.21, 0.3), (-0.45, -0.2, 0.3, 0.21), (-0.13, -0.22, 0.21, 0.3), (0.13, -0.2, 0.21, 0.3), (0.44, -0.22, 0.21, 0.3)):
        p.span("white", (x - w / 2, y + 0.002, z - h / 2), (x + w / 2, y + 0.004, z + h / 2))
        p.cyl("red", (x, y + 0.002, z + h / 2 - 0.02), (x, y + 0.01, z + h / 2 - 0.02), 0.007, 6)
    return p


def trophy(index, h):
    """트로피. 받침 바닥이 원점."""
    m = "silver" if index in (0, 4) else "gold"
    p = Part("Int_Trophy_%d" % index)
    p.lathe("rubber", (0, 0, 0), [(0.075, 0), (0.075, 0.04), (0.055, 0.05), (0.055, 0.06)], 12)
    t = h - 0.06
    p.lathe(m, (0, 0, 0.06), [(0.03, 0), (0.014, t * 0.05), (0.012, t * 0.28), (0.024, t * 0.34), (0.03, t * 0.42), (0.062, t * 0.72), (0.072, t * 0.96), (0.075, t)], 16)
    for s_ in (-1, 1):
        p.ring(m, (s_ * 0.085, 0, 0.06 + t * 0.68), (0, 1, 0), 0.032, 0.005, 12, 4)
    return p


def trophy_case():
    """트로피 진열장. 폭 2.6, 깊이 0.42. 벽면 y = 0, 바닥 가운데가 원점, 앞이 +y. 아래는 철제 장, 위는 유리 칸이다."""
    p = Part("Int_TrophyCase")
    W, D = 2.6, 0.42
    p.span("paint", (-W / 2, 0, 0.06), (W / 2, D, 0.58), 0.006, 2)
    p.span("rubber", (-W / 2 + 0.02, 0, 0), (W / 2 - 0.02, D - 0.05, 0.06))
    for k in range(4):
        x = -W / 2 + W * (k + 0.5) / 4
        p.span("paint", (x - 0.3, D, 0.09), (x + 0.3, D + 0.012, 0.55), 0.004, 1)
        p.span("steel", (x - 0.05, D + 0.012, 0.46), (x + 0.05, D + 0.022, 0.474), 0.002)
    p.span("wood", (-W / 2, 0, 0.58), (W / 2, D + 0.015, 0.61), 0.004, 1)
    top = 1.02
    for x in (-W / 2 + 0.015, -W / 2 * 0.333, W / 2 * 0.333, W / 2 - 0.015):
        p.span("steel", (x - 0.015, D - 0.03, 0.61), (x + 0.015, D, top))
    for x in (-W / 2 + 0.015, W / 2 - 0.015):
        p.span("steel", (x - 0.015, 0.0, 0.61), (x + 0.015, 0.03, top))
    p.span("wood", (-W / 2, 0, top), (W / 2, D + 0.015, top + 0.03), 0.004, 1)
    p.span("glass", (-W / 2 + 0.03, D - 0.018, 0.61), (W / 2 - 0.03, D - 0.012, top))
    for x in (-W / 2 + 0.008, W / 2 - 0.022):
        p.span("glass", (x, 0.03, 0.61), (x + 0.014, D - 0.03, top))
    p.span("paint", (-W / 2, 0, 0.61), (W / 2, 0.012, top))
    return p


def blanket(p, z, half, y0, y1):
    """매트리스 윗면 z를 덮는 담요. 양옆과 발치로 흘러내리고 주름이 있다."""
    def ripple(x, y):
        return 0.006 * math.sin(x * 11 + y * 5) + 0.004 * math.sin(y * 13 - x * 7 + 1)

    xs = (-half - 0.012, -half - 0.018, -half - 0.008, -half + 0.022, -0.26, -0.13, 0.0, 0.13, 0.26, half - 0.022, half + 0.008, half + 0.018, half + 0.012)
    drop = (-0.11, -0.055, -0.01, 0.015, 0.018, 0.018, 0.018, 0.018, 0.018, 0.015, -0.01, -0.055, -0.11)
    rows = []
    count = 10
    for i in range(count + 1):
        y = y0 + (y1 - y0) * i / count
        rows.append([(x, y, z + d + (ripple(x, y) if 3 <= j <= 9 else 0.0)) for j, (x, d) in enumerate(zip(xs, drop))])
    # 발치: 담요 끝이 아래로 접힌다
    for dy, dz in ((0.016, -0.012), (0.018, -0.055), (0.012, -0.11)):
        rows.append([(x * 0.99, y1 + dy, z + min(d, dz)) for x, d in zip(xs, drop)])
    p.grid("blanket", rows, False, (0, 0, 1))


def pillow(p, y, z):
    """베개: 네 귀가 뾰족하고 가운데가 부푼 쿠션."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=10, radius=1.0)
    for v in bm.verts:
        q = v.co
        px = math.copysign(abs(q.x) ** 0.6, q.x)
        py = math.copysign(abs(q.y) ** 0.6, q.y)
        pz = math.copysign(abs(q.z) ** 0.8, q.z)
        puff = 1 - 0.3 * (abs(px) ** 2 + abs(py) ** 2) * 0.5
        v.co = Vector((px * 0.29, y + py * 0.18, z + 0.055 + pz * 0.06 * puff + 0.012 * px * py))
    p.add("white", bm)
    bm.free()


def curtain(p, x, y0, y1, z0, z1, along_x=False):
    """주름 잡혀 걷힌 커튼. 위쪽이 봉에 걸리고 아래로 갈수록 주름이 벌어진다. 양면이 보이도록 두께가 있다.
    봉의 수직 위치가 x이고 y0~y1을 따라 걸린다. along_x면 x와 y를 맞바꾼다."""
    n = 26
    t = 0.004

    def pt(a, b, z):
        return (b, a, z) if along_x else (a, b, z)

    def ring(z, amp):
        a = [pt(x + amp * math.sin(i * 1.45) + t, y0 + (y1 - y0) * i / n, z) for i in range(n + 1)]
        b = [pt(x + amp * math.sin(i * 1.45) - t, y0 + (y1 - y0) * i / n, z) for i in range(n + 1)]
        return a + b[::-1]

    p.grid("curtain", [ring(z1, 0.022), ring((z0 + z1) / 2, 0.028), ring(z0, 0.036)], True)


def bunk():
    """2단 침대. 폭 0.92, 길이 2.1. 머리가 벽(y = 0), 발이 +y, 바닥이 z = 0. 지붕과 옆판(+x)이 있고,
    열린 옆(-x)과 발치를 ㄱ자 봉의 커튼으로 가린다. 커튼은 걷혀 있다. 사다리는 열린 옆 발치에 있다."""
    p = Part("Int_Bunk")
    W, L = 0.92, 2.1
    R = W / 2
    top = 2.24
    # 옆판, 지붕, 덧판
    p.span("paint", (R - 0.02, 0, 0), (R, L, top), 0.003)
    p.span("paint", (-R, 0, top - 0.03), (R, L, top), 0.004)
    for z0, z1 in ((0.1, 1.08), (1.16, 2.14)):
        p.span("paint", (R, 0.14, z0), (R + 0.006, L - 0.14, z1), 0.003)
    # 기둥: 열린 쪽 둘, 발치 안쪽 하나
    for x, y in ((-R + 0.02, 0.02), (-R + 0.02, L - 0.02), (R - 0.04, L - 0.02)):
        p.span("steel", (x - 0.02, y - 0.02, 0), (x + 0.02, y + 0.02, top - 0.03), 0.004)
    tiers = ((0.44, 1.43), (1.46, top - 0.06))
    for z, rod in tiers:
        p.span("steel", (-R, 0, z), (R - 0.02, L, z + 0.05), 0.006, 1)
        p.span("bed", (-R + 0.03, 0.02, z + 0.05), (R - 0.03, L - 0.07, z + 0.2), 0.03, 2)
        # 침대 머리의 흰 시트와 베개, 담요
        p.box("white", (0, 0.67, z + 0.213), (W - 0.1, 0.16, 0.03), 0.013, 2)
        blanket(p, z + 0.2, R - 0.03, 0.74, L - 0.1)
        pillow(p, 0.27, z + 0.2)
        # 떨어짐 방지 판과 발판, 이름표
        p.span("paint", (-R, 0.35, z + 0.05), (-R + 0.02, L - 0.5, z + 0.3), 0.006, 2)
        p.span("paint", (-R + 0.04, L - 0.05, z + 0.05), (R - 0.06, L - 0.03, z + 0.34), 0.006, 2)
        p.span("steel", (-0.09, L - 0.03, z + 0.17), (0.09, L - 0.027, z + 0.24), 0.001)
        p.span("white", (-0.08, L - 0.027, z + 0.175), (0.08, L - 0.024, z + 0.235))
        # ㄱ자 커튼 봉: 옆과 발치. 커튼은 양 끝으로 걷혀 있다
        rx, ry = -R + 0.012, L - 0.012
        p.cyl("steel", (rx, 0.0, rod), (rx, ry, rod), 0.008, 6)
        p.cyl("steel", (rx, ry, rod), (R - 0.03, ry, rod), 0.008, 6)
        p.sphere("steel", (rx, ry, rod), 0.0105, 8, 6)
        curtain(p, rx, 0.05, 0.62, z + 0.22, rod + 0.004)
        curtain(p, ry, R - 0.42, R - 0.05, z + 0.3, rod + 0.004, True)
    # 사다리: 열린 쪽 발치, 틀에 고정
    for y in (L - 0.42, L - 0.12):
        p.span("steel", (-R - 0.04, y - 0.012, 0), (-R - 0.016, y + 0.012, 1.8), 0.003)
        for z in (0.46, 1.48):
            p.span("steel", (-R - 0.016, y - 0.012, z), (-R + 0.004, y + 0.012, z + 0.03))
    for k in range(5):
        z = 0.32 + k * 0.32
        p.cyl("steel", (-R - 0.028, L - 0.42, z), (-R - 0.028, L - 0.12, z), 0.011, 6)
    # 침대 밑 수납: 서랍은 사다리에 가리지 않게 옆면 머리 쪽
    p.span("paint", (-R + 0.02, 0.02, 0.0), (R - 0.02, L - 0.03, 0.44), 0.003)
    for y in (0.46, 1.2):
        p.span("paint", (-R + 0.01, y - 0.34, 0.05), (-R + 0.02, y + 0.34, 0.4), 0.003)
        p.span("steel", (-R, y - 0.08, 0.3), (-R + 0.01, y + 0.08, 0.312), 0.002)
    return p


def locker():
    """사물함. 폭 0.62, 깊이 0.55, 높이 2.0. 벽면 y = 0, 바닥 가운데가 원점, 앞이 +y."""
    p = Part("Int_Locker")
    W, D, H = 0.62, 0.55, 2.0
    p.span("paint", (-W / 2, 0, 0.08), (W / 2, D, H), 0.005, 1)
    p.span("rubber", (-W / 2 + 0.02, 0, 0), (W / 2 - 0.02, D - 0.05, 0.08))
    p.span("paint", (-W / 2 + 0.02, D, 0.12), (W / 2 - 0.02, D + 0.016, H - 0.04), 0.006, 2)
    for z in (1.72, 1.78, 1.84, 0.28, 0.34, 0.4):
        p.span("rubber", (-0.18, D + 0.016, z), (0.18, D + 0.018, z + 0.024))
    p.span("steel", (0.2, D + 0.016, 0.98), (0.235, D + 0.03, 1.16), 0.004)
    p.span("steel", (-0.09, D + 0.016, 1.52), (0.09, D + 0.019, 1.58), 0.001)
    for z in (0.3, 1.1, 1.85):
        p.cyl("steel", (-W / 2 + 0.015, D + 0.016, z - 0.03), (-W / 2 + 0.015, D + 0.016, z + 0.03), 0.007, 6)
    return p


TV_W, TV_H = 1.6, 0.9


TV_BOTTOM = 0.09        # 책상면에서 화면 틀 아래까지
TV_DESK_H = 0.6


def tv():
    """받침 달린 TV 몸체. 책상면 z = 0, 가운데가 원점, 화면이 +y를 본다. 화면은 Int_TvScreen이 따로다."""
    p = Part("Int_Tv")
    z0, z1 = TV_BOTTOM, TV_BOTTOM + TV_H + 0.044
    p.span("rubber", (-TV_W / 2 - 0.022, -0.02, z0), (TV_W / 2 + 0.022, 0.02, z1), 0.006, 2)
    p.span("rubber", (-0.5, -0.05, z0 + 0.2), (0.5, -0.02, z1 - 0.2), 0.008, 2)
    p.span("rubber", (-0.32, -0.13, 0), (0.32, 0.13, 0.014), 0.005, 2)
    p.span("steel", (-0.04, -0.035, 0.005), (0.04, 0.0, z0 + 0.25), 0.004)
    p.cyl("led", (0.76, 0.02, z0 + 0.01), (0.76, 0.022, z0 + 0.01), 0.004, 6)
    return p


def tv_desk():
    """TV 책상. 폭 1.75, 깊이 0.45, 높이 0.6. 바닥 가운데가 원점, 앞이 +y. 철제 틀에 나무 상판, 아래 선반에 재생기 상자가 있다."""
    p = Part("Int_TvDesk")
    W, D, H = 1.75, 0.45, TV_DESK_H
    p.span("wood", (-W / 2, -D / 2, H - 0.035), (W / 2, D / 2, H), 0.006, 2)
    p.span("wood", (-W / 2 + 0.04, -D / 2 + 0.03, 0.16), (W / 2 - 0.04, D / 2 - 0.03, 0.185), 0.004, 1)
    for x in (-W / 2 + 0.03, W / 2 - 0.03):
        p.span("steel", (x - 0.02, -D / 2 + 0.02, 0), (x + 0.02, D / 2 - 0.02, H - 0.035), 0.004)
    p.span("steel", (-W / 2 + 0.05, -D / 2 + 0.02, H - 0.12), (W / 2 - 0.05, -D / 2 + 0.04, H - 0.035), 0.003)
    # 재생기 상자
    p.span("rubber", (-0.24, -0.13, 0.185), (0.24, 0.15, 0.255), 0.005, 2)
    p.span("screen", (-0.09, 0.15, 0.205), (0.09, 0.153, 0.235))
    p.cyl("led", (0.19, 0.15, 0.22), (0.19, 0.153, 0.22), 0.005, 6)
    return p


def tv_screen():
    """TV 화면 한 장. UV가 0~1을 꽉 채운다. 영상 플레이어가 이 면에 그린다."""
    TILES["tvscreen"] = 1.0
    p = Part("Int_TvScreen")
    bm = bmesh.new()
    x, z = TV_W / 2, TV_H / 2
    y = 0.0215
    c0 = TV_BOTTOM + 0.022 + z
    face = bm.faces.new([bm.verts.new(q) for q in ((x, y, c0 - z), (x, y, c0 + z), (-x, y, c0 + z), (-x, y, c0 - z))])
    bmesh.ops.recalc_face_normals(bm, faces=[face])
    if face.normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=[face])
    # +y에서 보면 x가 왼쪽이다. 영상이 뒤집히지 않게 u를 반대로 준다
    p.add("tvscreen", bm, None, lambda c: (0.5 - c.x / TV_W, 0.5 + (c.z - c0) / TV_H))
    bm.free()
    return p


def duffel(name, mat):
    """군장 더플백. 눕혀 놓은 천 자루로, 바닥 가운데가 원점이고 길이 0.8m다. 조임끈 2개, 손잡이, 옆 주머니, 멜빵, 묶은 입구가 있다."""
    p = Part(name)
    L, R = 0.8, 0.165
    base = 0.76 * R
    n = 20

    def surf(x, t, off=0.0):
        u = min(1.0, abs(x) / (L / 2))
        if x < 0:
            prof = (1 - u ** 7) ** 0.5           # 바닥 쪽 끝: 둥근 어깨와 평평한 끝판
            pleat = 0.0
        else:
            # 입구 쪽 끝: 주름이 모이며 좁아진다
            g = max(0.0, (u - 0.68) / 0.32)
            g = g * g * (3 - 2 * g)
            prof = 1 - 0.8 * g
            pleat = 0.012 * g * math.cos(10 * t)
        pinch = 1 - 0.085 * math.exp(-((abs(x) - 0.2) / 0.04) ** 2)
        wr = 0.006 * math.sin(3 * t + x * 9) + 0.004 * math.sin(5 * t - x * 17 + 1) + 0.003 * math.sin(2 * t + x * 31)
        r = R * prof * pinch + wr * prof + pleat + off
        # 무게로 눌려 위가 처지고 아래가 넓다
        y = math.cos(t) * r * (1.08 - 0.12 * math.sin(t))
        z = math.sin(t) * r * (0.92 if math.sin(t) > 0 else 1.0)
        return (x, y, max(z, -base) + base)

    xs = [-0.4, -0.399, -0.395, -0.387, -0.375, -0.355, -0.325, -0.27, -0.235, -0.2, -0.165, -0.08, 0.0, 0.08, 0.165, 0.2, 0.235, 0.27, 0.3, 0.33, 0.36, 0.385, 0.4]
    p.grid(mat, [[surf(x, k / n * math.tau) for k in range(n)] for x in xs], True)
    # 입구: 끈으로 묶은 목과 주름진 끝
    neck = [(0.395, 0.03, 0.0), (0.42, 0.024, 0.0), (0.436, 0.028, 0.002), (0.448, 0.036, 0.004), (0.456, 0.042, 0.005)]
    p.grid(mat, [[(x, math.cos(k / n * math.tau) * (r + a_ * math.cos(10 * k / n * math.tau)), base - 0.0 + math.sin(k / n * math.tau) * (r + a_ * math.cos(10 * k / n * math.tau))) for k in range(n)] for x, r, a_ in neck], True)
    for x in (0.414, 0.426):
        p.ring("webbing", (x, 0, base), (1, 0, 0), 0.0265, 0.005, 12, 4)
    p.ring("steel", (0.42, 0.04, base - 0.012), (0, 0, 1), 0.016, 0.003, 10, 4)
    # 바닥 쪽 끝의 손잡이: 끝판을 가로지르는 납작한 끈
    arc = [(-0.38, -0.09), (-0.395, -0.085), (-0.408, -0.07), (-0.424, -0.035), (-0.428, 0.0), (-0.424, 0.035), (-0.408, 0.07), (-0.395, 0.085), (-0.38, 0.09)]
    t_ = 0.0025
    p.grid("webbing", [[(x + t_ * sx, y, base + 0.012 + 0.016 * sz) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))] for x, y in arc], True)
    # 조임끈과 버클
    for sx in (-0.2, 0.2):
        w = 0.02
        rows = [[surf(sx + d, k / n * math.tau, off) for k in range(n)] for d, off in ((-w, 0.0), (-w, 0.005), (w, 0.005), (w, 0.0))]
        p.grid("webbing", rows, True)
        q = surf(sx, math.pi * 0.56, 0.006)
        rot = Matrix.Rotation(math.radians(10), 4, 'X')
        p.box("steel", q, (0.056, 0.044, 0.012), 0.003, 1, rot)
        p.box("rubber", (q[0], q[1], q[2] + 0.006), (0.03, 0.03, 0.006), 0.002, 1, rot)
    # 들손: 조임끈 사이 위쪽
    top = surf(0, math.pi / 2)
    p.tube("webbing", [(-0.1, top[1], top[2] - 0.004), (-0.08, top[1], top[2] + 0.02), (-0.04, top[1], top[2] + 0.04), (0.04, top[1], top[2] + 0.04), (0.08, top[1], top[2] + 0.02), (0.1, top[1], top[2] - 0.004)], 0.008, 6)
    p.cyl("rubber", (-0.05, top[1], top[2] + 0.036), (0.05, top[1], top[2] + 0.036), 0.014, 8)
    for x in (-0.1, 0.1):
        p.span("webbing", (x - 0.03, top[1] - 0.02, top[2] - 0.006), (x + 0.03, top[1] + 0.02, top[2] + 0.002), 0.001)
    # 옆 주머니와 덮개: 앞쪽(+y) 조임끈 사이
    def bulge(u, v):
        return 0.03 * math.sin(u * math.pi) ** 0.5 * math.sin(v * math.pi) ** 0.5
    x0, x1, t0, t1 = -0.13, 0.13, -0.55, 0.42
    p.grid(mat, [[surf(x0 + (x1 - x0) * i / 6, t0 + (t1 - t0) * j / 5, bulge(i / 6, j / 5) - 0.002) for j in range(6)] for i in range(7)], False, (0, 1, 0))
    t2 = t1 + 0.04
    p.grid(mat, [[surf(x0 + (x1 - x0) * i / 6 - 0.006 + 0.012 * i / 6, t, off) for t, off in ((t2 - 0.34, 0.034), (t2 - 0.16, 0.036), (t2, 0.018), (t2 + 0.03, -0.002))] for i in range(7)], False, (0, 1, 0.3))
    for x in (-0.07, 0.07):
        q = surf(x, t2 - 0.26, 0.036)
        p.cyl("steel", (x, q[1] - 0.004, q[2]), (x, q[1] + 0.006, q[2]), 0.009, 8)
    # 멜빵: 뒤쪽을 따라 눕혀 놓았다
    tm = math.pi * 0.86
    rows = [[surf(x, tm + d, off) for d, off in ((-0.14, -0.001), (-0.14, 0.006), (0.14, 0.006), (0.14, -0.001))] for x in (-0.33, -0.2, -0.08, 0.08, 0.2, 0.33)]
    p.grid("webbing", rows, False, (0, -1, 0.3))
    for x in (-0.33, 0.33):
        q = surf(x, tm, 0.008)
        p.cyl("steel", (x, q[1], q[2] - 0.022), (x, q[1], q[2] + 0.022), 0.01, 6)
    return p


def vest_rack():
    """구명조끼 걸이: 벽 봉에 주황 조끼 4벌. 벽면 y = 0, 왼쪽 끝이 x = 0, 봉 높이가 z = 0."""
    p = Part("Int_VestRack")
    p.span("steel", (0, 0, -0.02), (1.1, 0.012, 0.02), 0.002)
    for k in range(4):
        x = 0.16 + k * 0.26
        p.cyl("steel", (x, 0.012, 0), (x, 0.07, 0), 0.008, 6)
        p.box("orange", (x, 0.06, -0.24), (0.21, 0.06, 0.4), 0.018, 2)
        p.span("rubber", (x - 0.1, 0.085, -0.3), (x + 0.1, 0.094, -0.27))
        p.span("rubber", (x - 0.1, 0.085, -0.16), (x + 0.1, 0.094, -0.13))
        p.span("orange", (x - 0.012, 0.05, -0.06), (x + 0.012, 0.075, 0.012), 0.003)
    return p


def duct(L=1.2):
    """환기 덕트. x 방향, 윗면 z = 0, 가운데 y = 0. 이음 띠, 멜빵, 아래 격자가 있다. 멜빵은 0.24 위 천장까지 간다."""
    p = Part("Int_Duct_%d" % round(L * 100))
    W, H = 0.4, 0.22
    f = L / 1.2
    p.span("white", (0, -W / 2, -H), (L, W / 2, 0), 0.006, 1)
    p.span("steel", (L - 0.018, -W / 2 - 0.012, -H - 0.012), (L + 0.018, W / 2 + 0.012, 0.012), 0.003)
    for x in (0.3 * f, 0.9 * f):
        for s_ in (-1, 1):
            p.span("steel", (x - 0.015, s_ * (W / 2 + 0.001) - 0.003, -H - 0.006), (x + 0.015, s_ * (W / 2 + 0.001) + 0.003, 0.24), 0.001)
        p.span("steel", (x - 0.015, -W / 2 - 0.006, -H - 0.008), (x + 0.015, W / 2 + 0.006, -H - 0.003))
    # 아래 격자
    c = L / 2
    p.span("steel", (c - 0.18, -0.14, -H - 0.012), (c + 0.18, 0.14, -H), 0.004)
    for k in range(7):
        y = -0.105 + k * 0.035
        p.span("rubber", (c - 0.16, y - 0.008, -H - 0.014), (c + 0.16, y + 0.008, -H - 0.012))
    return p


def speaker():
    """벽걸이 방송 스피커. 벽면 y = 0, 가운데가 원점."""
    p = Part("Int_Speaker")
    p.span("steel", (-0.06, 0, -0.08), (0.06, 0.012, 0.08), 0.002)
    p.cyl("steel", (0, 0.012, 0.0), (0, 0.06, 0.0), 0.02, 8)
    p.cyl("paint", (0, 0.06, 0.0), (0, 0.13, -0.03), 0.05, 12, 0.13)
    p.ring("paint", (0, 0.13, -0.03), (0, 1, -0.4), 0.128, 0.008, 16, 5)
    p.cyl("rubber", (0, 0.12, -0.028), (0, 0.125, -0.03), 0.12, 14)
    return p


def detector():
    """천장 연기 감지기. 천장 z = 0."""
    p = Part("Int_Detector")
    p.cyl("white", (0, 0, 0), (0, 0, -0.03), 0.075, 14, 0.065)
    p.cyl("white", (0, 0, -0.03), (0, 0, -0.045), 0.045, 12)
    p.cyl("led", (0.05, 0, -0.024), (0.05, 0, -0.03), 0.005, 6)
    return p


def wall_phone():
    """벽 전화기. 벽면 y = 0, 가운데가 원점."""
    p = Part("Int_Phone")
    p.span("paint", (-0.1, 0, -0.17), (0.1, 0.075, 0.17), 0.008, 2)
    p.span("rubber", (-0.066, 0.075, -0.1), (0.066, 0.078, 0.08), 0.002)
    for r in range(4):
        for c in range(3):
            x, z = (c - 1) * 0.04, -0.07 + r * 0.04
            p.span("steel", (x - 0.012, 0.078, z - 0.01), (x + 0.012, 0.083, z + 0.01), 0.002)
    # 송수화기
    p.span("rubber", (-0.13, 0.04, -0.16), (-0.095, 0.08, 0.16), 0.012, 2)
    for z in (-0.13, 0.13):
        p.span("rubber", (-0.14, 0.04, z - 0.045), (-0.09, 0.09, z + 0.045), 0.014, 2)
    # 꼬인 전화선: 송수화기 아래에서 늘어졌다가 본체 아래로 다시 들어간다
    pts = []
    n = 66
    a_, b_ = Vector((-0.115, 0.06, -0.17)), Vector((0.03, 0.04, -0.165))
    for k in range(n + 1):
        t = k / n
        q = a_.lerp(b_, t) + Vector((0, 0, -0.17 * math.sin(math.pi * t)))
        ends = min(1.0, min(t, 1 - t) * 12)
        spin = t * math.tau * 11
        pts.append(q + Vector((math.cos(spin) * 0.008 * ends, math.sin(spin) * 0.008 * ends, 0)))
    p.tube("rubber", pts, 0.0028, 4)
    p.cyl("rubber", (0.03, 0.04, -0.175), (0.03, 0.04, -0.163), 0.008, 6)
    return p


def conduit_run(h):
    """벽 전선관 묶음 4줄. 벽면 y = 0, 바닥 쪽 끝이 원점, 높이 h. 위 끝은 천장판을 지난다."""
    p = Part("Int_Conduit_%d" % round(h * 100))
    for i, x in enumerate((-0.06, -0.02, 0.02, 0.06)):
        p.cyl("paint", (x, 0.05, 0), (x, 0.05, h), 0.014, 7)
    k = 0.2
    while k < h - 0.1:
        p.span("steel", (-0.085, 0, k - 0.012), (0.085, 0.07, k + 0.012), 0.003)
        k += 0.55
    p.span("steel", (-0.095, 0, h - 0.014), (0.095, 0.08, h), 0.002)
    return p


def breaker():
    """분전반. 벽면 y = 0, 가운데가 원점. 폭 0.6, 높이 0.8."""
    p = Part("Int_Breaker")
    p.span("paint", (-0.3, 0, -0.4), (0.3, 0.14, 0.4), 0.01, 2)
    p.span("paint", (-0.28, 0.14, -0.38), (0.28, 0.152, 0.38), 0.006, 2)
    p.span("paint", (-0.24, 0.152, -0.34), (0.24, 0.156, 0.34), 0.002)
    for z in (-0.3, 0.3):
        p.cyl("steel", (-0.31, 0.135, z - 0.04), (-0.31, 0.135, z + 0.04), 0.01, 8)
    p.cyl("steel", (0.23, 0.156, 0), (0.23, 0.17, 0), 0.022, 10)
    p.span("steel", (0.222, 0.17, -0.05), (0.238, 0.178, 0.05), 0.003)
    for x in (-0.23, 0.23):
        p.cyl("steel", (x, 0.05, 0.4), (x, 0.05, 0.42), 0.022, 8)
        p.conduit_up("paint", x, 0.05, 0.42, 0.5, 0.015)
    # 문 앞면: 환기 틈과 명판 틀, 잠금. 차단기는 문 안쪽에 있어 보이지 않는다
    for z0 in (-0.3, 0.14):
        for k in range(5):
            z = z0 + k * 0.03
            p.span("rubber", (-0.15, 0.156, z), (0.15, 0.158, z + 0.012))
    p.span("steel", (-0.09, 0.156, 0.02), (0.09, 0.159, 0.08), 0.001)
    p.span("white", (-0.08, 0.159, 0.03), (0.08, 0.162, 0.07))
    p.cyl("steel", (0.23, 0.156, 0.09), (0.23, 0.164, 0.09), 0.012, 8)
    return p


def wall_shelf():
    """벽 선반 0.95m. 벽면 y = 0, 왼쪽 끝이 x = 0, 선반 윗면 z = 0. 파일철이 있다."""
    p = Part("Int_Shelf")
    L, D = 0.95, 0.26
    p.span("steel", (0, 0, -0.016), (L, D, 0), 0.004, 1)
    p.span("steel", (0, D - 0.01, 0), (L, D, 0.03))
    for x in (0.08, L - 0.08):
        p.span("steel", (x - 0.012, 0, -0.2), (x + 0.012, 0.012, 0), 0.002)
        p.span("steel", (x - 0.008, 0, -0.14), (x + 0.008, D * 0.75, -0.016), 0.002)
    colors = ("blue", "red", "white", "door", "blue", "white", "paint", "blue", "red", "white")
    x = 0.04
    for k, m in enumerate(colors):
        w = 0.05 + (k % 3) * 0.012
        p.span(m, (x, 0.03, 0), (x + w - 0.003, 0.22, 0.26 + (k % 2) * 0.03), 0.002)
        x += w
    p.span("white", (0.68, 0.04, 0), (0.9, 0.22, 0.03), 0.002)
    p.span("door", (0.7, 0.05, 0.03), (0.88, 0.21, 0.05), 0.002)
    return p


def clipboards():
    """벽에 건 클립보드 3개. 벽면 y = 0, 가운데가 원점. 가로대의 못에 판이 걸리고, 종이는 판 안에 들어가 집게에 물린다."""
    p = Part("Int_Clipboards")
    p.span("wood", (-0.42, 0, 0.17), (0.42, 0.015, 0.2), 0.002)
    for k, x in enumerate((-0.28, 0.0, 0.28)):
        p.bolts([(x, 0.015, 0.185)], (0, 1, 0), 0.008, 0.02, 6)
        p.span("wood", (x - 0.115, 0.015, -0.2), (x + 0.115, 0.022, 0.175), 0.002)
        p.span("white", (x - 0.1, 0.022, -0.185), (x + 0.1, 0.025, 0.12))
        p.span("steel", (x - 0.055, 0.022, 0.095), (x + 0.055, 0.034, 0.15), 0.003)
        p.cyl("steel", (x - 0.05, 0.036, 0.15), (x + 0.05, 0.036, 0.15), 0.005, 6)
    return p


def wall_fan():
    """벽 선풍기. 벽면 y = 0, 가운데가 원점."""
    p = Part("Int_Fan")
    p.span("steel", (-0.06, 0, -0.06), (0.06, 0.012, 0.06), 0.002)
    p.cyl("steel", (0, 0.012, 0), (0, 0.1, 0), 0.016, 8)
    p.cyl("rubber", (0, 0.1, 0), (0, 0.19, 0), 0.05, 12)
    p.cyl("steel", (0, 0.19, 0), (0, 0.25, 0), 0.01, 8)
    for k in range(4):
        t = k / 4 * math.tau + 0.4
        p.cyl("steel", (0, 0.185, 0), (math.cos(t) * 0.17, 0.2, math.sin(t) * 0.17), 0.004, 4)
    for r, y in ((0.17, 0.2), (0.17, 0.27)):
        p.ring("steel", (0, y, 0), (0, 1, 0), r, 0.005, 22, 4)
    for r in (0.06, 0.115):
        p.ring("steel", (0, 0.275, 0), (0, 1, 0), r, 0.003, 14, 3)
    for k in range(8):
        t = k / 8 * math.tau
        c, s_ = math.cos(t), math.sin(t)
        p.cyl("steel", (c * 0.17, 0.2, s_ * 0.17), (c * 0.17, 0.27, s_ * 0.17), 0.003, 4)
        p.cyl("steel", (c * 0.17, 0.27, s_ * 0.17), (c * 0.02, 0.275, s_ * 0.02), 0.003, 4)
    for k in range(4):
        t = k / 4 * math.tau
        rot = Matrix.Rotation(-t, 4, 'Y') @ Matrix.Rotation(math.radians(25), 4, 'X')
        p.box("white", (math.cos(t) * 0.085, 0.235, math.sin(t) * 0.085), (0.13, 0.004, 0.07), 0.002, 1, rot)
    p.cyl("white", (0, 0.22, 0), (0, 0.25, 0), 0.03, 10)
    return p


def _mug(p, x, y, z, m="white"):
    """속이 빈 머그: 벽 두께가 있고 안쪽에 커피가 담겨 있다."""
    p.lathe(m, (x, y, z), [(0.036, 0.0), (0.04, 0.004), (0.04, 0.095), (0.0365, 0.097), (0.0345, 0.095), (0.0345, 0.012), (0.03, 0.008), (0.002, 0.008)], 18)
    p.cyl("rubber", (x, y, z + 0.07), (x, y, z + 0.0705), 0.0347, 16)
    h = [(x + 0.038, y, z + 0.078), (x + 0.056, y, z + 0.076), (x + 0.067, y, z + 0.062), (x + 0.067, y, z + 0.042), (x + 0.056, y, z + 0.028), (x + 0.038, y, z + 0.024)]
    p.tube(m, h, 0.0055, 6)


def pencil(p, a, b, r=0.0038):
    """연필: 지우개, 금속 띠, 몸통, 깎은 나무, 흑연심. a가 지우개 쪽, b가 심 끝이다."""
    a, b = Vector(a), Vector(b)
    d = b - a
    p.cyl("red", a, a + d * 0.05, r, 6)
    p.cyl("steel", a + d * 0.05, a + d * 0.1, r + 0.0003, 6)
    p.cyl("orange", a + d * 0.1, a + d * 0.86, r, 6)
    p.cyl("wood", a + d * 0.86, a + d * 0.97, r, 6, 0.0012)
    p.cyl("rubber", a + d * 0.97, b, 0.0012, 6, 0.0002)


def headset(p, x, y, z):
    """받침에 걸어 둔 헤드셋: 머리띠, 귀 패드, 마이크 줄, 전선. 책상면이 z다."""
    p.cyl("rubber", (x, y, z), (x, y, z + 0.012), 0.06, 16)
    p.cyl("steel", (x, y, z + 0.012), (x, y, z + 0.2), 0.007, 8)
    cz = z + 0.125
    R = 0.088
    arc = [(x + math.cos(t) * R, y, cz + math.sin(t) * R) for t in [math.radians(a) for a in range(5, 180, 17)]]
    p.tube("steel", arc, 0.0045, 6)
    pad = [(x + math.cos(t) * (R - 0.004), y, cz + math.sin(t) * (R - 0.004)) for t in [math.radians(a) for a in range(52, 132, 13)]]
    p.tube("fabric", pad, 0.014, 8)
    p.span("rubber", (x - 0.016, y - 0.012, z + 0.195), (x + 0.016, y + 0.012, z + 0.205), 0.003)
    for s_ in (-1, 1):
        cx = x + s_ * 0.092
        c2 = cz - 0.03
        p.span("steel", (cx - 0.004, y - 0.008, c2), (cx + 0.004, y + 0.008, cz + 0.012))
        p.cyl("rubber", (cx + s_ * 0.022, y, c2), (cx - s_ * 0.002, y, c2), 0.036, 14, 0.045)
        p.cyl("fabric", (cx - s_ * 0.002, y, c2), (cx - s_ * 0.022, y, c2), 0.044, 14, 0.041)
        p.cyl("rubber", (cx - s_ * 0.0215, y, c2), (cx - s_ * 0.025, y, c2), 0.024, 12)
    # 마이크 줄과 전선
    mx = x - 0.112
    c2 = cz - 0.03
    p.tube("rubber", [(mx, y, c2), (mx - 0.006, y + 0.04, c2 - 0.012), (mx + 0.012, y + 0.085, c2 - 0.03), (mx + 0.05, y + 0.105, c2 - 0.04)], 0.003, 5)
    p.sphere("rubber", (mx + 0.056, y + 0.107, c2 - 0.041), 0.012, 8, 6)
    ex = x + 0.112
    p.tube("cable", [(ex, y, c2 - 0.03), (ex + 0.012, y, c2 - 0.055), (ex, y + 0.012, z + 0.03), (ex - 0.04, y + 0.02, z + 0.004), (x + 0.05, y + 0.012, z + 0.006)], 0.0028, 5)


def clutter_a():
    """근무석 사이 물건: 서류, 펜, 머그. 폭 0.3m. 책상 윗면 z = 0, 벽 쪽이 -y."""
    p = Part("Int_ClutterA")
    p.span("white", (-0.11, 0.2, 0), (0.1, 0.48, 0.016), 0.001)
    p.box("white", (-0.005, 0.34, 0.0165), (0.21, 0.28, 0.003), 0, 1, Matrix.Rotation(math.radians(6), 4, 'Z'))
    pencil(p, (-0.06, 0.26, 0.022), (0.06, 0.37, 0.022))
    _mug(p, -0.03, 0.1, 0)
    return p


def clutter_b():
    """근무석 사이 물건: 헤드셋과 공책. 폭 0.3m. 책상 윗면 z = 0, 벽 쪽이 -y."""
    p = Part("Int_ClutterB")
    headset(p, 0, 0.11, 0)
    p.span("door", (-0.1, 0.22, 0), (0.1, 0.48, 0.014), 0.002)
    p.span("white", (-0.092, 0.225, 0.013), (0.092, 0.472, 0.016))
    return p


def floor_mat():
    """전술 테이블 둘레 고무 깔판. 두께 1cm, 가운데가 원점, 바깥 반경 1.7m."""
    p = Part("Int_FloorMat")
    n = 40
    circle = lambda r: [(math.cos(k / n * math.tau) * r, math.sin(k / n * math.tau) * r) for k in range(n)]
    bm = bmesh.new()
    rings = []
    for r, z in ((1.72, 0.0), (1.7, 0.01), (0.92, 0.01), (0.92, 0.0)):
        rings.append([bm.verts.new((q[0], q[1], z)) for q in circle(r)])
    for a in range(3):
        for k in range(n):
            bm.faces.new([rings[a][k], rings[a][(k + 1) % n], rings[a + 1][(k + 1) % n], rings[a + 1][k]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    p.add("mat", bm)
    bm.free()
    return p


def wall_frame(name, w, h, mat="screen", d=0.05):
    """벽걸이 틀. 벽면 y = 0, 가운데가 원점. Unity 패널이 앞면을 덮는다."""
    p = Part(name)
    p.span("rubber", (-w / 2, 0, -h / 2), (w / 2, d, h / 2), 0.01, 2)
    p.span(mat, (-w / 2 + 0.03, d, -h / 2 + 0.03), (w / 2 - 0.03, d + 0.002, h / 2 - 0.03))
    return p


def picture():
    """액자. 벽면 y = 0, 가운데가 원점."""
    p = Part("Int_Picture")
    w, h = PICTURE_W, PICTURE_H
    p.span("wood", (-w / 2, 0, -h / 2), (w / 2, 0.022, h / 2), 0.004, 1)
    # 매트: 사진 자리를 비운 네 띠
    x, z = PHOTO_W / 2, PHOTO_H / 2
    i, j = w / 2 - 0.03, h / 2 - 0.03
    p.span("red", (-i, 0.022, z), (i, 0.024, j))
    p.span("red", (-i, 0.022, -j), (i, 0.024, -z))
    for s_ in (-1, 1):
        p.span("red", (s_ * x, 0.022, -z), (s_ * i, 0.024, z))
    return p


def photo(index):
    """액자 속 사진 한 장. 재질이 장마다 따로이고 UV가 0~1을 꽉 채워, 텍스처만 바꾸면 사진이 바뀐다. 가로:세로 = 3:2."""
    name = "photo%02d" % index
    TILES[name] = 1.0
    p = Part("Int_Photo_%02d" % index)
    bm = bmesh.new()
    x, z = PHOTO_W / 2, PHOTO_H / 2
    y = 0.0245
    face = bm.faces.new([bm.verts.new(q) for q in ((x, y, -z), (x, y, z), (-x, y, z), (-x, y, -z))])
    bmesh.ops.recalc_face_normals(bm, faces=[face])
    if face.normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=[face])
    # +y에서 보면 x가 왼쪽이다. 글자가 뒤집히지 않게 u를 반대로 준다
    p.add(name, bm, None, lambda c: (0.5 - c.x / PHOTO_W, 0.5 + c.z / PHOTO_H))
    bm.free()
    return p


SIGN_GRID = 4
PLATES = ("BRIEFING", "CIC", "BERTHING", "FLIGHT DECK", "HANGAR", "RADIO", "AUTHORIZED ONLY", "DANGER 440V", "NO SMOKING", "FIRE STATION", "SOUND POWERED", "SEC 3-58")


def sign_uv(cell, c, w, h, axis_x=True):
    """표지 칸 cell의 UV. c는 판 가운데(x, z), 판 크기 w, h."""
    cu, cv = (cell % SIGN_GRID + 0.5) / SIGN_GRID, 1 - (cell // SIGN_GRID + 0.5) / SIGN_GRID

    def f(co):
        return (cu - (co.x - c[0]) / w / SIGN_GRID * 0.96, cv + (co.z - c[1]) / h / SIGN_GRID * 0.96 * (h / w if h < w else 1))

    return f


def plate(index):
    """문 명판. 벽면 y = 0, 가운데가 원점. 글자는 텍스처에 있다."""
    p = Part("Int_Plate_%d" % index)
    w, h = 0.48, 0.14
    p.span("sign", (-w / 2, 0, -h / 2), (w / 2, 0.006, h / 2), 0.0015, 1, sign_uv(index + 2, (0, 0), w, h))
    p.bolts([(x, 0.006, z) for x in (-w / 2 + 0.018, w / 2 - 0.018) for z in (-h / 2 + 0.018, h / 2 - 0.018)], (0, 1, 0), 0.006, 0.002, 6)
    return p


def exit_sign():
    """비상구 표지등. 천장 z = 0에 매단다."""
    p = Part("Int_ExitSign")
    w, h = 0.36, 0.16
    p.span("steel", (-w / 2 - 0.012, -0.03, -h - 0.012), (w / 2 + 0.012, 0.03, 0.0), 0.004, 2)
    p.span("exit", (-w / 2, 0.03, -h), (w / 2, 0.033, -0.012), 0.0, 1, sign_uv(0, (0, -h / 2 - 0.006), w, h))
    p.cyl("steel", (0, 0, 0), (0, 0, 0.2), 0.012, 8)
    return p


def clock():
    """벽시계. 벽면 y = 0, 가운데가 원점."""
    p = Part("Int_Clock")
    r = 0.16
    p.cyl("steel", (0, 0, 0), (0, 0.036, 0), r + 0.012, 24)
    p.ring("rubber", (0, 0.04, 0), (0, 1, 0), r + 0.006, 0.008, 24, 5)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=r, radius2=r, depth=0.002)
    bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0.04, 0), verts=bm.verts)
    p.add("sign", bm, None, sign_uv(1, (0, 0), r * 2, r * 2))
    bm.free()
    p.cyl("steel", (0, 0.04, 0), (0, 0.05, 0), 0.008, 8)
    return p


def clock_hand(name, length, width, y, mat="rubber", tail=0.018):
    """시계 바늘. 원점이 회전축이고 12시 방향(+z)을 가리킨다. Unity에서는 로컬 Z축이 회전축이다."""
    p = Part(name)
    p.span(mat, (-width / 2, y, -tail), (width / 2, y + 0.002, length))
    p.cyl(mat, (0, y - 0.004, 0), (0, y + 0.002, 0), width * 0.9 + 0.003, 10)
    return p


def clock_hands():
    return [clock_hand("Int_ClockHour", 0.085, 0.009, 0.043), clock_hand("Int_ClockMinute", 0.125, 0.0065, 0.045), clock_hand("Int_ClockSecond", 0.135, 0.0025, 0.047, "red", 0.03)]


def socket():
    """벽 전원함: 파란 상자, 덮개, 전선관."""
    p = Part("Int_Socket")
    p.span("blue", (-0.07, 0, 0), (0.07, 0.07, 0.2), 0.006, 2)
    p.span("blue", (-0.058, 0.07, 0.012), (0.058, 0.078, 0.188), 0.003, 1)
    for z in (0.06, 0.14):
        p.cyl("rubber", (0, 0.078, z), (0, 0.086, z), 0.03, 12)
    p.bolts([(x, 0.078, z) for x in (-0.048, 0.048) for z in (0.024, 0.176)], (0, 1, 0), 0.005, 0.003, 6)
    p.cyl("steel", (0, 0.04, 0.2), (0, 0.04, 0.214), 0.018, 8)
    p.conduit_up("steel", 0, 0.04, 0.214, 0.4, 0.012)
    p.span("steel", (-0.024, 0.0, 0.29), (0.024, 0.056, 0.306), 0.002)
    return p


def cable_coil():
    """벽 고리에 감아 건 예비 전선. 벽면 y = 0, 고리가 원점. 한 줄이 세 바퀴 감기고 양 끝에 커넥터가 있다."""
    p = Part("Int_CableCoil")
    p.span("steel", (-0.05, 0, -0.04), (0.05, 0.01, 0.04), 0.002)
    p.cyl("steel", (0, 0.01, 0), (0, 0.105, 0), 0.012, 8)
    p.cyl("steel", (0, 0.105, 0), (0, 0.105, 0.05), 0.012, 8)
    R = 0.17
    c = Vector((0, 0, -0.012 - R))
    a0 = -0.25
    turns = (math.pi * 7 - a0) / math.tau
    n = 58
    # 오른쪽 아래에서 올라와 세 바퀴 반 감기고 왼쪽으로 내려간다
    pts = [(0.19, 0.03, -0.5), (0.185, 0.03, -0.42), (0.178, 0.03, -0.34)]
    for k in range(n + 1):
        t = k / n
        a = a0 + t * turns * math.tau
        wobble = 1 + 0.03 * math.sin(a * 2.3 + 1)
        pts.append(c + Vector((math.cos(a) * R * wobble, 0.03 + 0.04 * t + 0.004 * math.sin(a * 3), math.sin(a) * R * wobble)))
    last = pts[-1]
    pts += [last + Vector((0.002, 0.002, -0.08)), last + Vector((0.012, 0.002, -0.17))]
    p.tube("cable", pts, 0.0095, 6)
    # 묶음 끈 둘과 커넥터
    for a in (math.radians(200), math.radians(340)):
        q = c + Vector((math.cos(a) * R, 0.05, math.sin(a) * R))
        p.ring("rubber", q, (math.sin(a), 0, -math.cos(a)), 0.024, 0.004, 10, 4)
    p.cyl("steel", (0.19, 0.03, -0.53), (0.19, 0.03, -0.5), 0.016, 8)
    end = pts[-1]
    p.cyl("steel", (end.x, end.y, end.z - 0.03), (end.x, end.y, end.z), 0.016, 8)
    return p


def valve_bank():
    """벽의 밸브 묶음: 흰 관 3줄, 플랜지, 손잡이. 벽면 y = 0, 왼쪽 아래가 원점. 폭 1.4m, 높이 1.0m."""
    p = Part("Int_ValveBank")
    y = 0.14
    for z, r in ((0.2, 0.05), (0.52, 0.06), (0.86, 0.045)):
        B = 0.1
        pts = [(0.08, -0.012, z), (0.08, y - B, z)] + arc_pts((0.08, y - B, z), (0, 1, 0), (1, 0, 0), B, 4)
        pts += [(1.32 - B, y, z)] + arc_pts((1.32 - B, y, z), (1, 0, 0), (0, -1, 0), B, 4) + [(1.32, -0.012, z)]
        p.tube("white", pts, r, 12)
        for x in (0.08, 1.32):
            p.cyl("steel", (x, 0, z), (x, 0.012, z), r + 0.024, 14)
        for x in (0.32, 1.02):
            p.cyl("steel", (x - 0.018, y, z), (x + 0.018, y, z), r + 0.03, 16)
            for k in range(6):
                t = k / 6 * math.tau
                p.cyl("steel", (x - 0.03, y + math.cos(t) * (r + 0.018), z + math.sin(t) * (r + 0.018)), (x + 0.03, y + math.cos(t) * (r + 0.018), z + math.sin(t) * (r + 0.018)), 0.006, 6)
        for x in (0.25, 1.15):
            p.span("steel", (x - 0.02, 0, z - r - 0.012), (x + 0.02, y, z - r), 0.002)
            p.ring("steel", (x, y, z), (1, 0, 0), r + 0.006, 0.005, 14, 4)
    # 세로 지관과 밸브
    for x, z0, z1 in ((0.66, 0.2, 0.52), (0.84, 0.52, 0.86)):
        p.cyl("white", (x, y, z0), (x, y, z1), 0.032, 10)
        zm = (z0 + z1) / 2
        p.cyl("red", (x, y, zm - 0.05), (x, y, zm + 0.05), 0.048, 10)
        p.cyl("red", (x, y, zm), (x, y + 0.13, zm), 0.018, 8)
        p.ring("red", (x, y + 0.135, zm), (0, 1, 0), 0.06, 0.007, 16, 5)
        for k in range(3):
            t = k / 3 * math.tau
            p.cyl("red", (x, y + 0.135, zm), (x + math.cos(t) * 0.06, y + 0.135, zm + math.sin(t) * 0.06), 0.006, 5)
    return p


def desk():
    """나무 책상. 바닥 가운데가 원점, 앉는 쪽이 -y. 윗면 높이 0.68m(150cm 기준)."""
    p = Part("Int_Desk")
    W, D, H = 1.3, 0.65, 0.68
    p.span("wood", (-W / 2, -D / 2, H - 0.035), (W / 2, D / 2, H), 0.006, 2)
    p.span("wood", (-W / 2 + 0.02, D / 2 - 0.05, 0.05), (W / 2 - 0.02, D / 2 - 0.02, H - 0.035), 0.002)
    for sx in (-1, 1):
        p.span("wood", (sx * (W / 2 - 0.02) - 0.02, -D / 2 + 0.02, 0), (sx * (W / 2 - 0.02) + 0.02, D / 2 - 0.02, H - 0.035), 0.003)
    # 서랍장: 오른쪽
    x0, x1 = W / 2 - 0.42, W / 2 - 0.04
    p.span("wood", (x0, -D / 2 + 0.02, 0.05), (x1, D / 2 - 0.05, H - 0.035), 0.002)
    for k in range(3):
        z0 = 0.075 + k * 0.185
        p.span("wood", (x0 + 0.012, -D / 2 + 0.008, z0), (x1 - 0.012, -D / 2 + 0.024, z0 + 0.165), 0.004, 1)
        p.span("steel", ((x0 + x1) / 2 - 0.05, -D / 2 - 0.004, z0 + 0.08), ((x0 + x1) / 2 + 0.05, -D / 2 + 0.008, z0 + 0.09), 0.002)
    return p


def lamp():
    """스탠드: 무거운 받침, 꺾인 팔, 갓. 받침 바닥이 원점."""
    p = Part("Int_Lamp")
    p.cyl("rubber", (0, 0, 0), (0, 0, 0.022), 0.085, 16)
    a, b, c = (0, 0, 0.02), (-0.04, 0, 0.27), (0.13, 0, 0.47)
    p.cyl("rubber", a, b, 0.009, 7)
    p.cyl("rubber", b, c, 0.009, 7)
    p.cyl("rubber", (-0.04, -0.016, 0.27), (-0.04, 0.016, 0.27), 0.016, 8)
    p.cyl("rubber", (0.13, 0, 0.47), (0.2, 0, 0.42), 0.05, 12, 0.095)
    return p


def lectern():
    """연단. 바닥 가운데가 원점, 사용자가 -y에 선다. 높이 1.0m(150cm 기준). 몸통 윗면이 경사져 있고 윗판이 그 위에 얹힌다."""
    p = Part("Int_Lectern")
    a = math.radians(18)
    s_ = math.tan(a)
    zc = 0.93
    p.span("wood", (-0.28, -0.22, 0), (0.28, 0.22, 0.05), 0.006, 2)
    shape = [(-0.15, 0.05), (0.15, 0.05), (0.15, zc + 0.15 * s_), (-0.15, zc - 0.15 * s_)]
    p.grid("wood", [[(x, q[0], q[1]) for q in shape] for x in (-0.2, 0.2)], True)
    p.span("wood", (-0.17, 0.15, 0.12), (0.17, 0.158, 0.76), 0.003)
    tilt = Matrix.Rotation(a, 4, 'X')
    t3 = tilt.to_3x3()
    o = Vector((0, 0, zc))
    p.box("wood", o + t3 @ Vector((0, 0, 0.0165)), (0.56, 0.46, 0.035), 0.006, 2, tilt)
    # 원고 받침 턱
    p.box("rubber", o + t3 @ Vector((0, -0.2, 0.045)), (0.5, 0.02, 0.024), 0.004, 1, tilt)
    return p


def wall_tv():
    """벽걸이 상황판 틀. 벽면 y = 0, 가운데가 원점. 화면 판은 Unity 패널이 덮는다."""
    p = Part("Int_WallFrame")
    w, h = 1.6, 0.95
    p.span("rubber", (-w / 2, 0, -h / 2), (w / 2, 0.05, h / 2), 0.01, 2)
    p.span("screen", (-w / 2 + 0.03, 0.05, -h / 2 + 0.03), (w / 2 - 0.03, 0.052, h / 2 - 0.03))
    p.span("steel", (-0.2, -0.0, -0.1), (0.2, 0.012, 0.1))
    return p


def junction():
    """배전함과 전선관. 벽면 y = 0. 경첩, 잠금쇠, 명판, 전선 클램프가 있다."""
    p = Part("Int_Junction")
    p.span("paint", (-0.2, 0, 0.0), (0.2, 0.12, 0.5), 0.008, 2)
    p.span("paint", (-0.185, 0.12, 0.015), (0.185, 0.132, 0.485), 0.005, 2)
    p.span("paint", (-0.15, 0.132, 0.06), (0.15, 0.135, 0.44), 0.002)
    for z in (0.09, 0.41):
        p.cyl("steel", (-0.202, 0.128, z - 0.035), (-0.202, 0.128, z + 0.035), 0.009, 8)
    p.cyl("steel", (0.15, 0.132, 0.25), (0.15, 0.145, 0.25), 0.02, 12)
    p.span("steel", (0.143, 0.145, 0.205), (0.157, 0.152, 0.295), 0.003)
    p.span("white", (-0.09, 0.135, 0.34), (0.09, 0.137, 0.4), 0.001)
    p.bolts([(x, 0.132, z) for x in (-0.17, 0.17) for z in (0.03, 0.47)], (0, 1, 0), 0.008, 0.005)
    for x in (-0.09, 0.0, 0.09):
        p.cyl("steel", (x, 0.05, 0.5), (x, 0.05, 0.535), 0.024, 8)
        p.conduit_up("paint", x, 0.05, 0.535, 0.95, 0.016)
        p.span("steel", (x - 0.024, 0.0, 0.7 - 0.012), (x + 0.024, 0.07, 0.7 + 0.012), 0.003)
    return p


def extinguisher():
    """벽걸이 소화기. 벽면 y = 0, 바닥 z = 0. 호스, 게이지, 고정 띠가 있다."""
    p = Part("Int_Extinguisher")
    y = 0.11
    p.cyl("red", (0, y, 0.21), (0, y, 0.22), 0.078, 18, 0.085)
    p.cyl("red", (0, y, 0.22), (0, y, 0.74), 0.085, 18)
    p.cyl("red", (0, y, 0.74), (0, y, 0.77), 0.085, 18, 0.07)
    p.cyl("red", (0, y, 0.77), (0, y, 0.795), 0.07, 18, 0.03)
    p.cyl("steel", (0, y, 0.795), (0, y, 0.885), 0.024, 10)
    p.cyl("steel", (0.015, y, 0.82), (0.05, y, 0.82), 0.012, 8)
    p.cyl("white", (0.05, y, 0.82), (0.056, y, 0.82), 0.019, 10)
    p.span("rubber", (-0.015, y, 0.845), (0.015, y + 0.14, 0.862), 0.004)
    p.span("steel", (-0.012, y - 0.02, 0.875), (0.012, y + 0.09, 0.895), 0.004)
    p.cyl("steel", (-0.018, y, 0.868), (0.018, y, 0.868), 0.01, 8)
    # 호스: 위에서 옆으로 내려와 몸통에 고정
    pts = [(-0.02, y + 0.02, 0.83), (-0.08, y + 0.05, 0.78), (-0.115, y + 0.05, 0.62), (-0.1, y + 0.05, 0.42)]
    for a, b in zip(pts, pts[1:]):
        p.cyl("rubber", a, b, 0.009, 7)
    p.cyl("rubber", pts[-1], (-0.098, y + 0.05, 0.36), 0.015, 8, 0.009)
    for z in (0.34, 0.62):
        p.ring("rubber", (0, y, z), (0, 0, 1), 0.088, 0.004, 22, 4)
        p.span("steel", (-0.05, 0, z - 0.012), (0.05, 0.03, z + 0.012), 0.003)
    p.span("steel", (-0.045, 0, 0.26), (0.045, 0.008, 0.74), 0.003)
    p.bolts([(x, 0.008, z) for x in (-0.03, 0.03) for z in (0.28, 0.72)], (0, 1, 0), 0.007, 0.004)
    return p


def chair(index):
    """대기실 의자. 앉는 사람이 +y를 본다. 폭 0.68m, 등받이 1.04m. 양옆 알루미늄 틀, 분리된 쿠션과 테두리 줄, 등 뒤에 직책 패치."""
    p = Part("Int_Chair_%02d" % index)
    half = 0.27

    # 좌석 쿠션: 받침 + 윗 방석
    p.box("fabric", (0, 0.03, 0.385), (half * 2, 0.5, 0.075), 0.015, 1)
    p.box("fabric", (0, 0.035, 0.455), (half * 2 - 0.02, 0.48, 0.075), 0.032, 2)
    # 등받이: 뒤판(패치) + 앞 쿠션 2개
    tilt3 = Matrix.Rotation(math.radians(9), 3, 'X')
    tilt = tilt3.to_4x4()      # 위쪽이 뒤(-y)로 눕는다
    c = Vector((0, -0.285, 0.735))
    place = Matrix.Translation(c) @ tilt
    bm = Part.cube(c, (half * 2 - 0.02, 0.06, 0.62), 0.02, 1, tilt)
    n = tilt3 @ Vector((0, -1, 0))
    cell = index % 4, index // 4
    u = (cell[0] + 0.5) / 4
    v = 1 - (cell[1] + 0.5) / 3

    def rear_uv(co):
        d = tilt3.transposed() @ (co - c)
        return (u + d.x / 0.56 / 4, v + d.z / 0.62 / 3 * 0.96)

    bm2 = bm.copy()
    p.add("fabric", bm, lambda f: f.normal.dot(n) < 0.98)
    p.add("label", bm2, lambda f: f.normal.dot(n) >= 0.98, rear_uv)
    bm.free()
    bm2.free()
    q = place @ Vector((0, 0.06, -0.155))
    p.box("fabric", q, (half * 2 - 0.045, 0.075, 0.27), 0.032, 2, tilt)
    # 윗 쿠션 앞면에도 직책을 넣는다. 앞에서 보면 +x가 왼쪽이라 u를 뒤집는다
    q = place @ Vector((0, 0.06, 0.145))
    front = tilt3 @ Vector((0, 1, 0))

    def front_uv(co):
        d = tilt3.transposed() @ (co - q)
        return (u - d.x / 0.56 / 4, v + (d.z + 0.161) / 0.62 / 3 * 0.96)

    bm = Part.cube(q, (half * 2 - 0.045, 0.075, 0.27), 0.032, 2, tilt)
    bm2 = bm.copy()
    bm.normal_update()
    bm2.normal_update()
    p.add("fabric", bm, lambda f: f.normal.dot(front) < 0.98)
    p.add("label", bm2, lambda f: f.normal.dot(front) >= 0.98, front_uv)
    bm.free()
    bm2.free()
    # 양옆 틀: 팔걸이, 앞뒤 다리, 아래 가로대가 고리를 이루고 가운데는 뚫려 있다. 앞뒤 절반을 y = 0에서 잇는다
    rear = rounded([(-0.335, 0.0), (-0.27, 0.0), (-0.225, 0.27), (0.0, 0.27), (0.0, 0.33), (-0.25, 0.33), (-0.25, 0.6), (0.0, 0.6), (0.0, 0.655), (-0.3, 0.655), (-0.335, 0.6)], 0.022, 2, (3, 4, 7, 8))
    front = rounded([(0.0, 0.27), (0.265, 0.27), (0.3, 0.0), (0.365, 0.0), (0.335, 0.34), (0.335, 0.55), (0.36, 0.63), (0.33, 0.655), (0.0, 0.655), (0.0, 0.6), (0.275, 0.6), (0.275, 0.33), (0.0, 0.33)], 0.022, 2, (0, 8, 9, 12))
    for s in (-1, 1):
        x0, x1 = s * (half + 0.018), s * (half + 0.072)
        xa = min(x0, x1)
        for shape in (rear, front):
            bm = bmesh.new()
            verts = [bm.verts.new((xa, a, b)) for a, b in shape]
            face = bm.faces.new(verts)
            res = bmesh.ops.extrude_face_region(bm, geom=[face])
            bmesh.ops.translate(bm, vec=(abs(x1 - x0), 0, 0), verts=[e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)])
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            edges = [e for e in bm.edges if abs(e.verts[0].co.x - e.verts[1].co.x) < 1e-6 and not (abs(e.verts[0].co.y) < 1e-6 and abs(e.verts[1].co.y) < 1e-6)]
            bmesh.ops.bevel(bm, geom=edges, offset=0.005, offset_type='OFFSET', segments=1, profile=0.5, affect='EDGES', clamp_overlap=True)
            bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
            p.add("alum", bm)
            bm.free()
        xc = s * (half + 0.045)
        # 팔걸이 패드: 앞쪽이 둥글다
        p.span("rubber", (xc - 0.042, -0.25, 0.655), (xc + 0.042, 0.33, 0.69), 0.016, 2)
        # 등받이 지지대와 아래 가로대 보강
        p.box("alum", (s * (half + 0.006), -0.285, 0.64), (0.024, 0.045, 0.5), 0.006, 1, tilt)
        # 바닥 고정 판
        for y in (-0.3, 0.33):
            p.span("steel", (xc - 0.04, y - 0.07, 0), (xc + 0.04, y + 0.07, 0.01), 0.002)
    # 오른쪽 팔걸이의 접이식 받침판(메모판)
    tx = half + 0.045
    p.span("steel", (tx - 0.02, 0.1, 0.692), (tx + 0.02, 0.16, 0.71), 0.003)
    p.box("white", (tx - 0.16, 0.13, 0.715), (0.34, 0.24, 0.012), 0.004, 1)
    p.span("rubber", (tx - 0.31, 0.03, 0.72), (tx - 0.01, 0.042, 0.726), 0.002)
    # 좌판 틀, 앞뒤 가로대
    p.span("alum", (-half - 0.024, -0.25, 0.285), (half + 0.024, 0.28, 0.345), 0.008, 2)
    p.cyl("alum", (-half - 0.02, -0.255, 0.14), (half + 0.02, -0.255, 0.14), 0.014, 8)
    return p


def mirrored(p, name):
    q = p
    q.name = name
    q.mirror_x()
    return q


def all_parts():
    parts = [panel("Int_Panel_035", 0.35), panel("Int_Panel_075", 0.75), panel("Int_Panel_105", 1.05), panel("Int_Panel_140", 1.4), rib(2.6), rib(2.8), rib(3.2), rib_flat(2.6), rib_flat(2.8), beam(2.2), beam(3.3), beam(5.8), beam(7.0),
             fixture(), pipe(BRIEF_RUN), pipe(CIC_RUN), pipe(CORR_RUN), pipe(BERTH_RUN), valve(), valve_bank(), tray(BRIEF_RUN), tray(CIC_RUN), tray(CORR_RUN), tray(BERTH_RUN), hatch_frame(), hatch_leaf(), mirrored(hatch_frame(), "Int_HatchFrame_L"), mirrored(hatch_leaf(), "Int_HatchLeaf_L"),
             hatch_wall(2.6), hatch_wall(2.8), hatch_wall(3.2), junction(), extinguisher(), socket(), cable_coil(), clock(), exit_sign(), desk(), lamp(), lectern(), wall_tv(), picture(), bookcase(), pc_set(), swivel(),
             wall_frame("Int_Frame_300", 3.0, 1.9), wall_frame("Int_Frame_160", 1.6, 1.6), wall_frame("Int_Frame_Cork", 1.6, 1.3, "cork"), wall_frame("Int_Frame_Side", 2.4, 1.52, "screen", 0.125), wall_frame("Int_Frame_Hangar", 2.4, 1.65, "screen", 0.125), wall_frame("Int_Frame_260", 2.54, 1.455, "screen", 0.125), wall_frame("Int_Frame_SpecCtrl", 2.46, 0.375, "rubber", 0.02),
             hatch_trim(), panel("Int_Panel_015", 0.15), pipe_small("Int_PipeRed", "red", 0.04, CORR_RUN), seal_round("Int_SealPipe", 0.08), seal_round("Int_SealSmall", 0.04), seal_frame("Int_SealTray", 0.34, -0.012, 0.075), seal_frame("Int_SealDuct", 0.42, -0.232, 0.012), notices(), trophy_case(), bunk(), locker(), duffel("Int_Duffel", "canvas"), tv(), tv_desk(), tv_screen(), vest_rack(),
             wall_frame("Int_Frame_120", 1.6, 1.2), wall_frame("Int_Frame_Notice", 1.26, 0.95, "cork"), wall_frame("Int_Frame_Hud", 2.1, 1.46, "screen", 0.125), beam(6.2),
             trophy(0, 0.24), trophy(1, 0.3), trophy(2, 0.38), trophy(3, 0.28), trophy(4, 0.22),
             plot_table(), cic_desk(), station(), rack(), radio_desk(), spectator_desk(),
             duct(CIC_RUN), speaker(), detector(), wall_phone(), conduit_run(0.8), conduit_run(1.4), conduit_run(2.02), breaker(), wall_shelf(), clipboards(), wall_fan(), clutter_a(), clutter_b(), floor_mat()]
    parts += [plate(i) for i in range(len(PLATES))]
    parts += [chair(i) for i in range(len(CHAIR_LABELS))]
    parts += clock_hands()
    parts += [photo(i) for i in range(PHOTO_COUNT)]
    return parts


# ── 구획 조립 ──
OPEN_ANGLE = 160        # 열어 고정한 문짝의 각도. 낮은 늑골 앞을 지나간다
FIXTURE_DROP = 0.24     # 형광등 윗면: 보 아래까지 봉으로 내려온다
PIPE_DROP = 0.33        # 보 아래를 지나는 관의 축
TRAY_DROP = 0.3         # 보 아래를 지나는 트레이 바닥


def put_clock(put, x, y, z, rz):
    """시계와 바늘 셋은 같은 자리, 같은 방향이다. 바늘 메시의 원점이 축이다."""
    for name in ("Int_Clock", "Int_ClockHour", "Int_ClockMinute", "Int_ClockSecond"):
        put(name, x, y, z, rz)


def put_picture(put, index, x, y, z, rz):
    put("Int_Picture", x, y, z, rz)
    put("Int_Photo_%02d" % index, x, y, z, rz)


def briefing_room():
    """브리핑실: x 0~6(앞벽 x = 0), y -3.5~3.5, 높이 3.2. 작업 축 기준. 돌려주는 값 = (고유 메시, 배치 목록)."""
    D, W2, H = 6.0, 3.5, 3.2
    place = []

    def put(name, x, y, z, rz=0.0):
        place.append((name, x, y, z, rz))

    shell = Part("Int_Brief_Shell")
    plane(shell, "deck", (0, -W2), (D, W2), 0.0, 1)
    ceil = Part("Int_Brief_Ceil")
    plane(ceil, "paint", (0, -W2), (D, W2), H, -1)
    levels = (("Int_Panel_105", 0.0), ("Int_Panel_140", 1.05), ("Int_Panel_075", 2.45))
    # 옆벽: 판 1.4m + 늑골이 덮는 틈 0.1m
    for k in range(4):
        x = 0.05 + k * 1.5
        for name, z in levels:
            put(name, x, -W2, z)
            put(name, x + T_PANEL, W2, z, 180)
        a, b = k * 1.5 + TRIM_RIB, (k + 1) * 1.5 - TRIM_RIB
        if k != 0:      # 첫 칸은 책장 뒤다
            wall_trim(shell, "S", -W2, a, b)
        wall_trim(shell, "N", W2, a, b)
    for k in range(5):
        x = k * 1.5
        put("Int_Rib_320", x, -W2 + 0.02, 0)
        put("Int_Rib_320", x, W2 - 0.02, 0, 180)
    # 앞벽
    for k in range(5):
        y = W2 - k * T_PANEL
        for name, z in levels:
            put(name, 0, y, z, -90)
    # 뒷벽: 문 두 개 + 사이 벽
    for y, h in ((-1.5, "R"), (2.2, "L")):
        put("Int_HatchWall_320", D, y, 0, 90)
    for y0, y1 in ((-W2, -2.2), (-0.8, 1.5), (2.9, W2)):
        shell.span("wall", (D - 0.02, y0, 0), (D, y1, H))
        shell.span("strip", (D - 0.027, y0, 0), (D, y1, 0.05), 0.004, 1, lambda c: (c.y / 0.28, (c.z - 0.025) / 0.28 + 0.5))
        for z in (1.05,):
            a = y0 + (TRIM_CORNER_RIB if y0 == -W2 else TRIM_INSET)
            b = y1 - (TRIM_CORNER_RIB if y1 == W2 else TRIM_INSET)
            wall_trim(shell, "E", D, a, b)
    # 문: CIC 쪽은 오른쪽 경첩, 복도 쪽은 왼쪽 경첩. 열어서 벽에 붙여 고정한다
    arm = HATCH_W / 2 + 0.06
    put("Int_HatchFrame", D, -1.5, 0, 90)
    put("Int_HatchLeaf", D - 0.075, -1.5 + arm, 0, 90 - OPEN_ANGLE)
    put("Int_HatchFrame_L", D, 2.2, 0, 90)
    put("Int_HatchLeaf_L", D - 0.075, 2.2 - arm, 0, 90 + OPEN_ANGLE)
    put("Int_Plate_1", D - 0.02, -1.5, 2.3, 90)
    # 천장: 보는 늑골마다 벽에서 벽까지 간다. 관과 트레이는 보 아래를 지나 양쪽 벽으로 들어간다
    for x in (1.5, 3.0, 4.5):
        put("Int_Beam_700", x, -W2, H, 90)
    pipe_run(put, 0.0, D, BRIEF_RUN, W2 - 0.35, -W2 + 0.5, H)
    for x in (2.25, 3.75, 5.25):
        for y in (-1.9, 0.0, 1.9):
            put("Int_Fixture", x, y, H - FIXTURE_DROP)
    put("Int_Detector", 2.25, 1.0, H)
    put("Int_Detector", 5.25, -1.0, H)
    put("Int_Speaker", 5.25, -W2 + 0.02, 2.75)
    # 패널 틀
    put("Int_Frame_300", 0.02, 0.9, 1.8, -90)
    put("Int_Frame_160", 0.02, -2.0, 1.75, -90)
    put("Int_Frame_Side", 4.4, -W2 + 0.02, 1.6)
    put("Int_Frame_Hangar", 4.4, W2 - 0.02, 1.6, 180)
    # 좌석 3 + 3, 두 줄. 앞벽을 본다
    n = 0
    for x in (2.8, 4.0):
        for y in (-2.29, -1.57, -0.85, 0.85, 1.57, 2.29):
            put("Int_Chair_%02d" % n, x, y, 0, 90)
            n += 1
    # 소품
    put("Int_Lectern", 0.95, -0.95, 0, -90)
    # 단상 맞은편: 조작 책상은 오른쪽 벽에 붙이고, 조작자는 벽을 보고 앉아 왼쪽으로 작전판을 본다
    put("Int_Desk", 0.95, 3.0, 0, 0)
    put("Int_PCSet", 0.95, 2.95, 0.68, 0)
    put("Int_Lamp", 1.45, 3.12, 0.68, 235)
    put("Int_Swivel", 0.95, 2.32, 0, 0)
    put_picture(put, 0, 0.95, W2 - 0.02, 1.75, 180)
    # 책장은 단상 쪽 왼쪽 벽. 앞이 비어 있어 꺼낼 수 있다
    put("Int_Bookcase", 0.75, -W2 + 0.02, 0, 0)
    put_picture(put, 1, 0.02, 2.97, 1.6, -90)
    put_picture(put, 2, 2.3, -W2 + 0.02, 1.75, 0)
    put_picture(put, 3, 2.3, W2 - 0.02, 1.75, 180)
    put("Int_Extinguisher", 2.25, W2 - 0.02, 0.0, 180)
    put("Int_Junction", D - 0.02, -2.85, 1.15, 90)
    put("Int_Phone", D - 0.02, 3.2, 1.45, 90)
    put("Int_Plate_8", D - 0.02, -2.85, 2.25, 90)
    put_clock(put, 0.02, 2.97, 2.12, -90)
    return [shell, ceil], place


def cic_room():
    """CIC: x 6.2~14, y -4.9~0.9, 높이 2.8. 좌표는 브리핑실과 같은 원점이다."""
    X0, X1, Y0, Y1, H = 6.2, 14.0, -4.9, 0.9, 2.8
    place = []

    def put(name, x, y, z, rz=0.0):
        place.append((name, x, y, z, rz))

    shell = Part("Int_Cic_Shell")
    plane(shell, "deck", (X0, Y0), (X1, Y1), 0.0, 1)
    ceil = Part("Int_Cic_Ceil")
    plane(ceil, "paint", (X0, Y0), (X1, Y1), H, -1)
    levels = (("Int_Panel_105", 0.0), ("Int_Panel_140", 1.05), ("Int_Panel_035", 2.45))
    # 앞뒤 벽: 판 1.4 + 늑골이 덮는 틈. 북쪽 벽 가운데 칸이 복도 문이다
    pitch = (X1 - X0) / 5
    for k in range(5):
        x = X0 + 0.08 + k * pitch
        for name, z in levels:
            put(name, x, Y0, z)
            if k != 2:
                put(name, x + T_PANEL, Y1, z, 180)
        a, b = X0 + k * pitch + TRIM_RIB, X0 + (k + 1) * pitch - TRIM_RIB
        wall_trim(shell, "S", Y0, a, b)
        if k != 2:
            wall_trim(shell, "N", Y1, a, b)
    for k in range(6):
        x = X0 + k * pitch
        put("Int_Rib_280", x, Y0 + 0.02, 0)
        # 열린 문짝이 앞을 지나가는 늑골만 낮다
        put("Int_RibFlat_280" if k == 3 else "Int_Rib_280", x, Y1 - 0.02, 0, 180)
    door_x = (X0 + X1) / 2
    put("Int_HatchWall_280", door_x, Y1, 0, 180)
    # 동쪽 벽
    pitch2 = (Y1 - Y0) / 4
    for k in range(4):
        y = Y0 + 0.025 + k * pitch2     # 90도 돌리면 판이 +y로 뻗는다
        for name, z in levels:
            put(name, X1, y, z, 90)
        wall_trim(shell, "E", X1, Y0 + TRIM_CORNER_RIB if k == 0 else Y0 + k * pitch2 + TRIM_RIB, Y1 - TRIM_CORNER_RIB if k == 3 else Y0 + (k + 1) * pitch2 - TRIM_RIB)
    for k in range(1, 4):
        put("Int_Rib_280", X1 - 0.02, Y1 - k * pitch2, 0, 90)
    # 서쪽 벽: 브리핑실 문 + 벽
    put("Int_HatchWall_280", X0, -1.5, 0, -90)
    put("Int_HatchTrim", X0, -1.5, 0, -90)
    for y0, y1 in ((Y0, -2.2), (-0.8, Y1)):
        shell.span("wall", (X0, y0, 0), (X0 + 0.02, y1, H))
        shell.span("strip", (X0, y0, 0), (X0 + 0.027, y1, 0.05), 0.004, 1, lambda c: (c.y / 0.28, (c.z - 0.025) / 0.28 + 0.5))
        for z in (1.05,):
            a = y0 + (TRIM_CORNER_RIB if y0 == Y0 else TRIM_INSET)
            b = y1 - (TRIM_CORNER_RIB if y1 == Y1 else TRIM_INSET)
            wall_trim(shell, "W", X0, a, b)
    # 복도 문: 왼쪽 경첩, 열어서 벽에 고정
    arm = HATCH_W / 2 + 0.06
    put("Int_HatchFrame_L", door_x, Y1, 0, 180)
    put("Int_HatchLeaf_L", door_x + arm, Y1 - 0.075, 0, 180 + OPEN_ANGLE)
    put("Int_Plate_0", X0 + 0.02, -1.5, 2.3, -90)
    # 천장: 보는 늑골마다, 관과 트레이는 벽에서 벽까지
    for k in range(1, 5):
        put("Int_Beam_580", X0 + k * pitch, Y0, H, 90)
    pipe_run(put, X0, X1, CIC_RUN, Y1 - 0.32, Y0 + 0.42, H)
    for x in (6.98, 8.54, 11.66, 13.22):
        put("Int_Fixture", x, -2.0, H - FIXTURE_DROP, 90)
    # 전술 테이블과 둘레 깔판
    put("Int_PlotTable", door_x, -2.0, 0)
    put("Int_FloorMat", door_x, -2.0, 0)
    # 환기 덕트: 북쪽 의자 위. 보 아래로 지나가 양쪽 벽으로 들어간다
    name = "Int_Duct_%d" % round(CIC_RUN * 100)
    for k in range(6):
        put(name, X0 + k * CIC_RUN, -0.2, H - 0.24)
    put("Int_SealDuct", X0 + 0.02, -0.2, H - 0.24, -90)
    put("Int_SealDuct", X1 - 0.02, -0.2, H - 0.24, 90)
    put("Int_Detector", 10.1, -3.4, H)
    put("Int_Detector", 13.22, -1.0, H)
    put("Int_Speaker", 13.22, Y0 + 0.02, 2.2)
    put("Int_Speaker", 6.98, Y1 - 0.02, 2.2, 180)
    # 랙에서 트레이로 올라가는 전선
    for y in (-4.1, -3.46, -2.82):
        put("Int_Conduit_80", X0 + 0.32, y, 2.0, -90)
    # 벽 설비
    put("Int_Breaker", 13.22, Y1 - 0.02, 1.72, 180)
    put("Int_Plate_7", 13.22, Y1 - 0.02, 1.2, 180)
    put("Int_Conduit_202", X1 - 0.02, -2.3, 0.78, 90)
    put("Int_Phone", X1 - 0.02, -1.27, 1.45, 90)
    put("Int_Plate_10", X1 - 0.02, -1.27, 1.75, 90)
    put("Int_Fan", X1 - 0.02, -4.18, 2.1, 90)
    put("Int_Shelf", 7.5, Y1 - 0.02, 1.72, 180)
    put("Int_Clipboards", 7.2, Y0 + 0.02, 1.72)
    put("Int_Shelf", 12.75, Y0 + 0.02, 1.62)
    put("Int_Plate_8", 7.2, Y0 + 0.02, 2.2)
    put("Int_Plate_11", 8.54, Y1 - 0.02, 1.5, 180)
    put("Int_Plate_9", X1 - 0.02, -4.18, 1.3, 90)
    # 안쪽 벽: 관전 스크린과 조작대
    put("Int_Frame_260", door_x, Y0 + 0.02, 1.68)
    put("Int_SpectatorDesk", door_x, Y0 + 0.13, 0)
    put("Int_Frame_SpecCtrl", door_x, Y0 + 0.13 + 0.502, 0.46)
    # 근무석 4개
    for x0, x_mon in ((6.9, (7.35, 8.25)), (11.5, (11.95, 12.85))):
        put("Int_CicDesk", x0, Y0 + 0.13, 0)
        for x in x_mon:
            put("Int_Station", x, Y0 + 0.13, 0.683)
            put("Int_Swivel", x, Y0 + 1.15, 0, 180)
    for x0, x_mon in ((6.9, (7.35, 8.25)), (12.0, (12.45, 13.35))):
        put("Int_CicDesk", x0 + 1.8, Y1 - 0.13, 0, 180)
        for x in x_mon:
            put("Int_Station", x, Y1 - 0.13, 0.683, 180)
            put("Int_Swivel", x, Y1 - 1.15, 0, 0)
    put("Int_ClutterA", 7.76, Y0 + 0.13, 0.683)
    put("Int_ClutterB", 12.36, Y0 + 0.13, 0.683)
    put("Int_ClutterB", 7.84, Y1 - 0.13, 0.683, 180)
    put("Int_ClutterA", 12.94, Y1 - 0.13, 0.683, 180)
    # 장비 랙: 서쪽 벽, 문 남쪽
    for y in (-4.1, -3.46, -2.82):
        put("Int_Rack", X0 + 0.02, y, 0, -90)
    # 무전석: 동쪽 벽
    put("Int_RadioDesk", X1 - 0.13, -2.0, 0, 90)
    put("Int_Swivel", X1 - 1.2, -2.0, 0, -90)
    # 명판은 늑골 사이 칸 가운데
    put("Int_Plate_5", X1 - 0.02, -2.73, 1.62, 90)
    put("Int_CableCoil", X1 - 0.02, -3.0, 2.3, 90)
    # 벽 소품
    put_picture(put, 5, X0 + 0.02, 0.05, 1.75, -90)
    put("Int_Junction", X1 - 0.02, 0.42, 1.15, 90)
    put("Int_Extinguisher", X1 - 0.02, -4.3, 0.0, 90)
    put_clock(put, 11.95, Y0 + 0.02, 2.05, 0)
    put("Int_Socket", 8.54, Y0 + 0.02, 1.45)
    return [shell, ceil], place


LEVELS_260 = (("Int_Panel_105", 0.0), ("Int_Panel_140", 1.05), ("Int_Panel_015", 2.45))


def wall_run(shell, axis, fixed, a, b, into, H, trims=(1.05,)):
    """판을 쓸 수 없는 짧은 벽. axis = "x"면 y = fixed에 x 방향으로 놓인다. into = 방 쪽 부호."""
    def box(a0, a1, d0, d1, z0, z1, mat, bevel=0.0, segs=1, uv=None):
        if axis == "x":
            shell.span(mat, (a0, fixed + into * min(d0, d1), z0) if into > 0 else (a0, fixed - max(d0, d1), z0), (a1, fixed + into * max(d0, d1), z1) if into > 0 else (a1, fixed - min(d0, d1), z1), bevel, segs, uv)
        else:
            shell.span(mat, (fixed + into * min(d0, d1), a0, z0) if into > 0 else (fixed - max(d0, d1), a0, z0), (fixed + into * max(d0, d1), a1, z1) if into > 0 else (fixed - min(d0, d1), a1, z1), bevel, segs, uv)

    along = (lambda c: c.x) if axis == "x" else (lambda c: c.y)
    box(a, b, 0.0, 0.02, 0, H, "wall")
    box(a, b, 0.0, 0.027, 0, 0.05, "strip", 0.004, 1, lambda c: (along(c) / 0.28, (c.z - 0.025) / 0.28 + 0.5))
    box(a, b, 0.0, 0.027, H - 0.05, H, "strip", 0.004, 1, lambda c: (along(c) / 0.28, (c.z - H + 0.025) / 0.28 + 0.5))
    for z in trims:
        box(a + TRIM_INSET, b - TRIM_INSET, 0.0, 0.05, z - 0.035, z + 0.035, "paint", 0.008, 2)


def panel_bay(put, shell, side, c, fixed, wall0, wall1):
    """벽판 한 칸. c = 칸 가운데, side = 벽 방향(S 벽은 방이 +y, N은 -y, E는 -x, W는 +x), wall0~wall1 = 벽 전체 범위.
    벽 띠는 양옆 늑골까지 가고, 코너 쪽만 맞은편 띠에 닿지 않게 줄인다."""
    half = T_PANEL / 2
    if side == "S":
        at, rz = (c - half, fixed), 0
    elif side == "N":
        at, rz = (c + half, fixed), 180
    elif side == "E":
        at, rz = (fixed, c - half), 90
    else:
        at, rz = (fixed, c + half), -90
    for name, z in LEVELS_260:
        put(name, at[0], at[1], z, rz)
    a = max(c - 0.8 + TRIM_RIB, wall0 + TRIM_INSET)
    b = min(c + 0.8 - TRIM_RIB, wall1 - TRIM_INSET)
    wall_trim(shell, side, fixed, a, b)


def plane(part, mat, a, b, z, up, step=0.5):
    """바닥과 천장: 꼭짓점을 공유하는 촘촘한 격자 한 장. a, b = (x, y) 두 모서리, up = 앞면 방향(+1, -1).
    큰 사각형 한 장은 라이트맵이 까맣게 구워지고, 떨어진 판으로 나누면 조각마다 밝기가 달라진다."""
    nx = max(1, round((b[0] - a[0]) / step))
    ny = max(1, round((b[1] - a[1]) / step))
    rows = [[(a[0] + (b[0] - a[0]) * i / nx, a[1] + (b[1] - a[1]) * j / ny, z) for j in range(ny + 1)] for i in range(nx + 1)]
    part.grid(mat, rows, False, (0, 0, up))


def wall_trim(shell, side, fixed, a, b):
    """벽 띠 한 토막. a~b는 벽을 따라간 범위다. 벽판 앞면(벽에서 2cm)에 붙는다."""
    z0, z1 = TRIM_Z - 0.035, TRIM_Z + 0.035
    if side == "S":
        lo, hi = (a, fixed + 0.02, z0), (b, fixed + 0.05, z1)
    elif side == "N":
        lo, hi = (a, fixed - 0.05, z0), (b, fixed - 0.02, z1)
    elif side == "E":
        lo, hi = (fixed - 0.05, a, z0), (fixed - 0.02, b, z1)
    else:
        lo, hi = (fixed + 0.02, a, z0), (fixed + 0.05, b, z1)
    shell.span("paint", lo, hi, 0.008, 2)


def wall_rib(put, shell, side, c, fixed, H, flat=False):
    """벽판 사이 이음: 틈을 막는 띠와 늑골. flat이면 문짝이 앞을 지나가는 낮은 늑골이다."""
    if side in ("S", "N"):
        y0, y1 = (fixed, fixed + 0.017) if side == "S" else (fixed - 0.017, fixed)
        shell.span("paint", (c - 0.104, y0, 0), (c + 0.104, y1, H))
    else:
        x0, x1 = (fixed, fixed + 0.017) if side == "W" else (fixed - 0.017, fixed)
        shell.span("paint", (x0, c - 0.104, 0), (x1, c + 0.104, H))
    name = ("Int_RibFlat_%d" if flat else "Int_Rib_%d") % round(H * 100)
    if side == "S":
        put(name, c, fixed + 0.02, 0, 0)
    elif side == "N":
        put(name, c, fixed - 0.02, 0, 180)
    elif side == "E":
        put(name, fixed - 0.02, c, 0, 90)
    else:
        put(name, fixed + 0.02, c, 0, -90)


def pipe_run(put, x0, x1, run, pipe_y, tray_y, H, red_y=None):
    """벽에서 벽까지 이어지는 보온관과 케이블 트레이. 보 아래를 지나고 양 끝이 벽 통과 틀로 들어간다."""
    n = round((x1 - x0) / run)
    tag = round(run * 100)
    for k in range(n):
        x = x0 + k * run
        put("Int_Pipe_%d" % tag, x, pipe_y, H - PIPE_DROP)
        put("Int_Tray_%d" % tag, x, tray_y, H - TRAY_DROP)
        if red_y is not None:
            put("Int_PipeRed", x, red_y, H - PIPE_DROP)
    for x, rz in ((x0 + 0.02, -90), (x1 - 0.02, 90)):
        put("Int_SealPipe", x, pipe_y, H - PIPE_DROP, rz)
        put("Int_SealTray", x, tray_y, H - TRAY_DROP, rz)
        if red_y is not None:
            put("Int_SealSmall", x, red_y, H - PIPE_DROP, rz)


def corridor_room():
    """복도: x 6.2~20.4, y 1.1~3.3, 높이 2.6. 벽은 1.6m 칸(판 1.4 + 늑골)이고 문은 모두 칸 가운데에 있다.
    칸 가운데 = 6.9 + 1.6k. 남쪽: 2 CIC, 6 침실. 북쪽: 3~4 게시 공간(y ~4.4), 7 격납고. 동쪽 끝이 비행갑판 해치."""
    X0, X1, Y0, Y1, H = 6.2, 20.4, 1.1, 3.3, 2.6
    place = []

    def put(name, x, y, z, rz=0.0):
        place.append((name, x, y, z, rz))

    def bay(k):
        return round(6.9 + 1.6 * k, 3)

    def rib_x(k):
        return round(7.7 + 1.6 * k, 3)

    shell = Part("Int_Corr_Shell")
    plane(shell, "deck", (X0, Y0), (X1, Y1), 0.0, 1)
    plane(shell, "deck", (11.0, Y1), (14.0, 4.4), 0.0, 1)
    ceil = Part("Int_Corr_Ceil")
    plane(ceil, "paint", (X0, Y0), (X1, Y1), H, -1)
    plane(ceil, "paint", (11.0, Y1), (14.0, 4.4), H, -1)
    cic_x, berth_x, hangar_x = bay(2), bay(6), bay(7)
    for k in range(9):
        if k not in (2, 6):
            panel_bay(put, shell, "S", bay(k), Y0, X0, X1)
        if k not in (3, 4, 7):
            panel_bay(put, shell, "N", bay(k), Y1, X0, X1)
    for k in range(8):
        x = rib_x(k)
        wall_rib(put, shell, "S", x, Y0, H)
        if k != 3:
            wall_rib(put, shell, "N", x, Y1, H)
        # 게시 공간 가운데 보는 뒷벽까지 간다
        put("Int_Beam_330" if k == 3 else "Int_Beam_220", x, Y0, H, 90)
    # 문
    for x in (cic_x, berth_x):
        put("Int_HatchWall_260", x, Y0, 0, 0)
        put("Int_HatchTrim", x, Y0, 0, 0)
    west = 2.2
    for a, b in ((Y0, west - 0.7), (west + 0.7, Y1)):
        wall_run(shell, "y", X0, a, b, 1, H)
        wall_run(shell, "y", X1, a, b, -1, H)
    put("Int_HatchWall_260", X0, west, 0, -90)
    put("Int_HatchTrim", X0, west, 0, -90)
    arm = HATCH_W / 2 + 0.06
    # 닫힌 문 둘: 격납고와 비행갑판으로 텔레포트한다
    put("Int_HatchWall_260", X1, west, 0, 90)
    put("Int_HatchFrame", X1, west, 0, 90)
    put("Int_HatchLeaf", X1 - 0.075, west + arm, 0, 90)
    put("Int_HatchWall_260", hangar_x, Y1, 0, 180)
    put("Int_HatchFrame", hangar_x, Y1, 0, 180)
    put("Int_HatchLeaf", hangar_x - arm, Y1 - 0.075, 0, 180)
    # 게시 공간
    wall_run(shell, "x", 4.4, 11.0, 14.0, -1, H)
    wall_run(shell, "y", 11.0, Y1, 4.4, 1, H)
    wall_run(shell, "y", 14.0, Y1, 4.4, -1, H)
    put("Int_Frame_120", 12.5, 4.38, 1.75, 180)
    put("Int_TrophyCase", 12.5, 4.38, 0, 180)
    for k, x in enumerate((11.6, 12.05, 12.5, 12.95, 13.4)):
        put("Int_Trophy_%d" % k, x, 4.17, 0.61)
    for x in (11.74, 13.26):
        put("Int_Fixture", x, 3.85, H - FIXTURE_DROP)
    # 천장 설비: 벽에서 벽까지 잇는다
    pipe_run(put, X0, X1, CORR_RUN, Y0 + 0.3, Y1 - 0.32, H, Y0 + 0.62)
    for k in (1, 3, 5, 7):
        put("Int_Fixture", bay(k), 2.2, H - FIXTURE_DROP)
    put("Int_Detector", cic_x, 2.2, H)
    put("Int_Detector", berth_x, 2.2, H)
    put("Int_ExitSign", 19.7, 2.2, H - 0.2, 90)
    # 남쪽 벽
    put_clock(put, bay(0), Y0 + 0.02, 1.75, 0)
    put("Int_Junction", bay(1), Y0 + 0.02, 1.15)
    put("Int_Plate_7", bay(1), Y0 + 0.02, 0.9)
    put("Int_ValveBank", bay(3) - 0.7, Y0 + 0.02, 1.2)
    put_picture(put, 2, bay(4), Y0 + 0.02, 1.75, 0)
    put("Int_Breaker", bay(5), Y0 + 0.02, 1.62)
    put("Int_VestRack", bay(7) - 0.55, Y0 + 0.02, 1.9)
    put("Int_Socket", bay(8), Y0 + 0.02, 0.5)
    put("Int_Plate_1", cic_x, Y0 + 0.02, 2.27)
    put("Int_Plate_2", berth_x, Y0 + 0.02, 2.27)
    # 북쪽 벽
    put("Int_Extinguisher", bay(0) - 0.25, Y1 - 0.02, 0, 180)
    put("Int_Extinguisher", bay(0) + 0.25, Y1 - 0.02, 0, 180)
    put("Int_Plate_9", bay(0), Y1 - 0.02, 1.35, 180)
    put("Int_Frame_Notice", bay(1), Y1 - 0.02, 1.72, 180)
    put("Int_Notices", bay(1), Y1 - 0.02, 1.72, 180)
    put("Int_Socket", bay(1), Y1 - 0.02, 0.5, 180)
    put("Int_Phone", bay(2), Y1 - 0.02, 1.45, 180)
    put("Int_Plate_10", bay(2), Y1 - 0.02, 1.75, 180)
    put("Int_Speaker", bay(2), Y1 - 0.02, 2.14, 180)
    put("Int_CableCoil", bay(5), Y1 - 0.02, 1.7, 180)
    put_picture(put, 3, bay(6), Y1 - 0.02, 1.75, 180)
    put("Int_Plate_4", hangar_x, Y1 - 0.02, 2.27, 180)
    # 양 끝
    put("Int_Plate_3", X1 - 0.02, west, 2.27, 90)
    put("Int_Plate_0", X0 + 0.02, west, 2.27, -90)
    return [shell, ceil], place


def berth_room():
    """침실: x 14.2~20.4, y -5.3~0.9, 높이 2.6. 칸 가운데 x = 14.9 + 1.6k, y = -4.6 + 1.6k. 문은 북쪽 벽 x = 16.5, 열어서 벽에 고정한다."""
    X0, X1, Y0, Y1, H = 14.2, 20.4, -5.3, 0.9, 2.6
    place = []

    def put(name, x, y, z, rz=0.0):
        place.append((name, x, y, z, rz))

    shell = Part("Int_Berth_Shell")
    plane(shell, "deck", (X0, Y0), (X1, Y1), 0.0, 1)
    ceil = Part("Int_Berth_Ceil")
    plane(ceil, "paint", (X0, Y0), (X1, Y1), H, -1)
    bays_x = (14.9, 16.5, 18.1, 19.7)
    bays_y = (-4.6, -3.0, -1.4, 0.2)
    ribs_x = (15.7, 17.3, 18.9)
    ribs_y = (-3.8, -2.2, -0.6)
    door = 16.5
    for x in bays_x:
        panel_bay(put, shell, "S", x, Y0, X0, X1)
        if x != door:
            panel_bay(put, shell, "N", x, Y1, X0, X1)
    for y in bays_y:
        panel_bay(put, shell, "E", y, X1, Y0, Y1)
        panel_bay(put, shell, "W", y, X0, Y0, Y1)
    for x in ribs_x:
        wall_rib(put, shell, "S", x, Y0, H)
        # 열린 문짝이 앞을 지나가는 늑골만 낮다
        wall_rib(put, shell, "N", x, Y1, H, x == 17.3)
        put("Int_Beam_620", x, Y0, H, 90)
    for y in ribs_y:
        # HUD 틀은 늑골을 덮는 깊이고, 사물함은 늑골 앞에 선다
        wall_rib(put, shell, "E", y, X1, H)
        wall_rib(put, shell, "W", y, X0, H)
    put("Int_HatchWall_260", door, Y1, 0, 180)
    arm = HATCH_W / 2 + 0.06
    put("Int_HatchFrame_L", door, Y1, 0, 180)
    put("Int_HatchLeaf_L", door + arm, Y1 - 0.075, 0, 180 + OPEN_ANGLE)
    # 천장 설비
    pipe_run(put, X0, X1, BERTH_RUN, Y1 - 0.3, Y1 - 0.75, H)
    for x in bays_x:
        put("Int_Fixture", x, -1.2, H - FIXTURE_DROP)
    put("Int_Detector", 16.5, -2.6, H)
    # 2단 침대 4조: 벽 칸마다 하나, 머리가 남쪽 벽
    for x in bays_x:
        put("Int_Bunk", x, Y0 + 0.03, 0)
    # 사물함: 서쪽 벽
    for k in range(5):
        put("Int_Locker", X0 + 0.135, 0.15 - k * 0.66, 0, -90)
    put("Int_Duffel", X0 + 0.42, -0.5, 2.0, -90)
    put("Int_Duffel", X0 + 0.42, -1.85, 2.0, -98)
    # TV: 책상 위. 화면이 열린 문짝 끝보다 앞에 오게 동쪽, 방 쪽으로 놓는다. 의자는 TV를 본다
    tv_x, tv_y = 19.3, 0.415
    put("Int_TvDesk", tv_x, tv_y, 0, 180)
    put("Int_Tv", tv_x, tv_y, TV_DESK_H, 180)
    put("Int_TvScreen", tv_x, tv_y, TV_DESK_H, 180)
    put("Int_Swivel", tv_x - 0.35, -1.0, 0, 0)
    put("Int_Swivel", tv_x + 0.35, -1.0, 0, 0)
    # 벽 설비
    # 침대 발치에 가리지 않게 북쪽으로 둔다. 2.8 x 1.95m 패널의 75%다
    put("Int_Frame_Hud", X1 - 0.02, -1.55, 1.5, 90)
    put("Int_Shelf", 15.38, Y1 - 0.02, 1.62, 180)
    put("Int_Extinguisher", X1 - 0.02, 0.12, 0, 90)
    put("Int_Plate_8", X1 - 0.02, 0.12, 1.5, 90)
    put("Int_Speaker", X1 - 0.02, -4.6, 2.05, 90)
    return [shell, ceil], place


# 사람이 들어가는 공간(작업 좌표). 격실 + 문 통로
VOLUMES = (
    ((0.0, -3.5, 0.0), (6.0, 3.5, 3.2)), ((6.2, -4.9, 0.0), (14.0, 0.9, 2.8)),
    ((6.2, 1.1, 0.0), (20.4, 3.3, 2.6)), ((11.0, 3.3, 0.0), (14.0, 4.4, 2.6)), ((14.2, -5.3, 0.0), (20.4, 0.9, 2.6)),
    ((5.9, -2.1, 0.0), (6.3, -0.9, 2.2)), ((5.9, 1.6, 0.0), (6.3, 2.8, 2.2)), ((9.5, 0.8, 0.0), (10.7, 1.2, 2.2)), ((15.9, 0.8, 0.0), (17.1, 1.2, 2.2)),
    ((11.0, 3.2, 0.0), (14.0, 3.4, 2.6)),      # 복도에서 게시 공간으로 이어지는 입구
)
KEEP_FACES = ("Int_ClockHour", "Int_ClockMinute", "Int_ClockSecond")     # 도는 바늘
VOLUME_MARGIN = 0.015
HIDDEN_GAP = 0.008      # 이 거리 안에서 막히는 방향은 보이지 않는 방향으로 친다


# 바닥과 천장의 빛은 Blender에서 구워 한 장의 텍스처에 모은다(Tools/aco_interior_bake.py)
LIGHT_ATLAS = 1024
LIGHT_TEXELS = 32       # m당 텍셀
LIGHT_PAD = 8


def is_light_target(part, mat):
    return (part.endswith("_Shell") and mat == "deck") or part.endswith("_Ceil")


def light_rects():
    """(공간 번호, 0 바닥 / 1 천장) → 텍스처 칸 (x, y, 폭, 높이). 줄 단위로 큰 칸부터 채운다."""
    items = []
    for i, (lo, hi) in enumerate(VOLUMES[:5]):
        w = round((hi[0] - lo[0]) * LIGHT_TEXELS)
        h = round((hi[1] - lo[1]) * LIGHT_TEXELS)
        items += [(h, w, i, 0), (h, w, i, 1)]
    rects = {}
    x = y = row = LIGHT_PAD
    y = LIGHT_PAD
    row = 0
    for h, w, i, kind in sorted(items, reverse=True):
        if x + w + LIGHT_PAD > LIGHT_ATLAS:
            x = LIGHT_PAD
            y += row + LIGHT_PAD
            row = 0
        if y + h + LIGHT_PAD > LIGHT_ATLAS:
            raise ValueError("빛 텍스처가 모자랍니다")
        rects[(i, kind)] = (x, y, w, h)
        x += w + LIGHT_PAD
        row = max(row, h)
    return rects


def add_light_uv(me, kind):
    """바닥과 천장 메시에 빛 텍스처용 두 번째 UV를 넣는다. 격실 껍데기는 작업 좌표 그대로다."""
    rects = light_rects()
    layer = me.uv_layers.new(name="LightUV")
    for poly in me.polygons:
        c = poly.center
        index = next(i for i, (lo, hi) in enumerate(VOLUMES[:5]) if lo[0] <= c.x <= hi[0] and lo[1] <= c.y <= hi[1])
        lo, hi = VOLUMES[index]
        x0, y0, w, h = rects[(index, kind)]
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            layer.data[li].uv = ((x0 + (co.x - lo[0]) / (hi[0] - lo[0]) * w) / LIGHT_ATLAS, (y0 + (co.y - lo[1]) / (hi[1] - lo[1]) * h) / LIGHT_ATLAS)
    me.uv_layers.active_index = 0


def in_volume(q):
    """사람이 있을 수 있는 곳인지 본다. 벽판 앞면(벽면에서 2cm)보다 벽 쪽은 뺀다. 문 통로는 벽을 지난다."""
    for i, (lo, hi) in enumerate(VOLUMES):
        m = VOLUME_MARGIN if i < 5 else 0.0
        if lo[0] + m <= q.x <= hi[0] - m and lo[1] + m <= q.y <= hi[1] - m and lo[2] <= q.z <= hi[2]:
            return True
    return False


def view_dirs():
    """법선 주위 방향들. (법선 성분, 옆 성분 u, 옆 성분 v)"""
    dirs = [(1.0, 0.0, 0.0)]
    for a, n in ((40, 8), (72, 12)):
        for k in range(n):
            t = k / n * math.tau
            dirs.append((math.cos(math.radians(a)), math.sin(math.radians(a)) * math.cos(t), math.sin(math.radians(a)) * math.sin(t)))
    return dirs


def prune_hidden(parts, rooms):
    """어느 배치에서도 사람 눈에 닿지 않는 면을 지운다. parts = 이름 → Part. 격실 껍데기도 parts에 더한다.
    면 위의 점에서 법선 쪽으로 광선을 쏴서, 모든 방향이 8mm 안에서 막히거나 물체 안쪽(뒷면)을 만나면 숨은 면이다."""
    placements = {}
    shell_names = set()
    for shells, place in rooms.values():
        for sh in shells:
            parts[sh.name] = sh
            shell_names.add(sh.name)
            placements[sh.name] = [Matrix.Identity(4)]
        for name, x, y, z, rz in place:
            placements.setdefault(name, []).append(Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, 'Z'))
    verts, polys = [], []
    for name, mats in placements.items():
        for bm in parts[name].bm.values():
            bm.verts.index_update()
            bm.normal_update()
            base = [v.co.copy() for v in bm.verts]
            faces = [[v.index for v in f.verts] for f in bm.faces]
            for m in mats:
                off = len(verts)
                verts.extend(m @ c for c in base)
                polys.extend([off + i for i in f] for f in faces)
    tree = BVHTree.FromPolygons(verts, polys)
    dirs = view_dirs()
    removed = total = 0
    for name, mats in placements.items():
        if name in KEEP_FACES:
            continue
        for bm in parts[name].bm.values():
            kill = []
            for f in bm.faces:
                total += 1
                if not face_visible(f, mats, tree, () if name in shell_names else dirs):
                    kill.append(f)
            removed += len(kill)
            if kill:
                bmesh.ops.delete(bm, geom=kill, context='FACES')
    print("PRUNE removed", removed, "of", total, "faces")


def face_visible(f, mats, tree, dirs):
    c = f.calc_center_median()
    corners = [v.co.copy() for v in f.verts]
    step = max(1, len(corners) // 8)
    picks = range(0, len(corners), step)
    samples = [c]
    for i in picks:
        q = corners[i]
        mid = q.lerp(corners[(i + 1) % len(corners)], 0.5)
        samples += [q.lerp(c, 0.015), q.lerp(c, 0.5), mid.lerp(c, 0.015), mid.lerp(c, 0.5)]
    n = f.normal
    u = n.orthogonal().normalized()
    v = n.cross(u)
    for m in mats:
        rot = m.to_3x3()
        nw, uw, vw = rot @ n, rot @ u, rot @ v
        for q in samples:
            o = m @ q + nw * 0.0015
            if not in_volume(o):
                continue
            if not dirs:
                return True
            for a, b, e in dirs:
                d = nw * a + uw * b + vw * e
                loc, normal, index, dist = tree.ray_cast(o, d)
                # 벽 틈으로 바깥에 나간 광선은 사람이 있는 쪽이 아니다
                if loc is None or dist <= HIDDEN_GAP or normal.dot(d) > 0:
                    continue
                if in_volume(o + d * min(dist, 0.1)):
                    return True
    return False


def all_rooms():
    return {"Briefing": briefing_room(), "Cic": cic_room(), "Corridor": corridor_room(), "Berth": berth_room()}


# 격실별 공간: VOLUMES의 번호. 라이트 프로브와 반사 프로브가 쓴다
ROOM_VOLUMES = {"Briefing": (0,), "Cic": (1,), "Corridor": (2, 3), "Berth": (4,)}


def write_rooms(path):
    """격실 공간 목록: 격실 최소 x y z 최대 x y z. Unity 좌표다."""
    with open(path, "w", encoding="utf8", newline="\n") as f:
        for room, ids in ROOM_VOLUMES.items():
            for i in ids:
                lo, hi = VOLUMES[i]
                f.write("%s %.3f %.3f %.3f %.3f %.3f %.3f\n" % (room, lo[0], lo[2], lo[1], hi[0], hi[2], hi[1]))


def write_layout(path, rooms):
    """Unity 배치 목록: 모듈 x y z Y회전. Unity 좌표는 (작업 x, 작업 z, 작업 y), Y회전은 작업 Z회전의 반대 부호다."""
    with open(path, "w", encoding="utf8", newline="\n") as f:
        for room, (shells, place) in rooms.items():
            for sh in shells:
                f.write("%s %s 0 0 0 0\n" % (room, sh.name))
            for name, x, y, z, rz in place:
                f.write("%s %s %.3f %.3f %.3f %.1f\n" % (room, name, x, z, y, -rz))


# ── 텍스처 ──
def noise(n, cells, seed):
    rng = np.random.RandomState(seed)
    g = rng.rand(cells + 1, cells + 1)
    g[-1, :] = g[0, :]
    g[:, -1] = g[:, 0]
    t = (np.arange(n) + 0.5) / n * cells
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    a, b = g[i][:, i], g[i + 1][:, i]
    c, d = g[i][:, i + 1], g[i + 1][:, i + 1]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fy) + b * fy) * (1 - fx) + (c * (1 - fy) + d * fy) * fx


def fbm(n, seed):
    return noise(n, 6, seed) * 0.5 + noise(n, 24, seed + 1) * 0.3 + noise(n, 96, seed + 2) * 0.2


def normal_map(h, strength):
    """높이 → 노멀(0~1). 타일이 이어지게 감싼다."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    z = np.ones_like(h)
    L = np.sqrt(dx * dx + dy * dy + z * z)
    return np.stack((-dx / L * 0.5 + 0.5, -dy / L * 0.5 + 0.5, z / L * 0.5 + 0.5), -1)


def paint_base(n, col, seed, grime=0.08):
    g = fbm(n, seed)
    return np.array(col, dtype=np.float32)[None, None, :] * (1 + (g - 0.5)[..., None] * grime * 2)


def wall_maps(n):
    """밝은 타공판: 5cm 간격 구멍. 1m 타일."""
    pitch = n / 20
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    d = np.sqrt(((xx % pitch) - pitch / 2) ** 2 + ((yy % pitch) - pitch / 2) ** 2)
    r = pitch * 0.15
    hole = np.clip((r - d) / 1.5, 0, 1)
    rim = np.clip(1 - np.abs(d - r) / 3, 0, 1) * 0.2
    albedo = paint_base(n, (0.7, 0.67, 0.59), 3, 0.07)
    albedo = albedo * (1 - hole[..., None] * 0.92)
    height = -hole + rim + (fbm(n, 5) - 0.5) * 0.03
    return albedo, normal_map(height, 2.0)


def fabric_maps(n):
    """짙은 갈색 직물: 짜임 + 얼룩."""
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    w = n / 180
    weave = np.sin(xx / w * math.tau) * np.sin(yy / w * math.tau)
    albedo = paint_base(n, (0.2, 0.115, 0.07), 11, 0.2) * (1 + weave[..., None] * 0.03)
    return albedo, normal_map(weave * 0.1 + (fbm(n, 12) - 0.5) * 0.8, 1.0)


def deck_maps(n):
    """짙은 녹색 광택 바닥. 깔판 이음매 1.2m."""
    albedo = paint_base(n, (0.045, 0.115, 0.09), 21, 0.2)
    w = max(n // 300, 2)
    albedo[:w] *= 0.35
    albedo[:, :w] *= 0.35
    h = (fbm(n, 22) - 0.5) * 0.08
    h[:w] -= 0.4
    h[:, :w] -= 0.4
    return albedo, normal_map(h, 1.2)


def keys_maps(n):
    """키보드 윗면: 5줄 배열. 키마다 경사가 있고 글자 자리에 밝은 점을 둔다."""
    rows = ((1,) * 15, (1.5,) + (1,) * 13 + (1.5,), (1.75,) + (1,) * 12 + (2.25,), (2.25,) + (1,) * 11 + (2.75,), (1.25, 1.25, 1.25, 6.25, 1.25, 1.25, 1.25, 1.25, 1.25))
    img = np.full((n, n, 3), 0.018, dtype=np.float32)
    h = np.zeros((n, n), dtype=np.float32)
    rh = n / 5
    for r, widths in enumerate(rows):
        y0, y1 = int(r * rh + rh * 0.07), int((r + 1) * rh - rh * 0.07)
        x = 0.0
        unit = n / sum(widths)
        for k, w in enumerate(widths):
            x0, x1 = int(x + unit * 0.07), int(x + w * unit - unit * 0.07)
            img[y0:y1, x0:x1] = 0.085 + 0.01 * ((r + k) % 2)
            # 윗면이 안쪽으로 살짝 좁아지는 키캡
            yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
            e = np.minimum(np.minimum(xx - x0, x1 - 1 - xx) / max((x1 - x0) * 0.16, 1), np.minimum(yy - y0, y1 - 1 - yy) / max((y1 - y0) * 0.16, 1))
            cap = np.clip(e, 0, 1)
            h[y0:y1, x0:x1] = cap
            img[y0:y1, x0:x1] *= (0.75 + 0.45 * cap)[..., None]
            if w <= 2.3:
                # 글자 자리
                ly, lx = int(y0 + (y1 - y0) * 0.3), int(x0 + (x1 - x0) * 0.3)
                img[ly:ly + max(n // 110, 2), lx:lx + int((x1 - x0) * 0.32)] = 0.42
            x += w * unit
    return img, normal_map(h, 3.0)


def mat_maps(n):
    """고무 깔판: 짙은 회색, 잔 다이아몬드 무늬. 50cm 타일."""
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    k = math.tau * 24 / n
    h = np.sin((xx + yy) * k) + np.sin((xx - yy) * k)
    return paint_base(n, (0.1, 0.105, 0.105), 59, 0.16), normal_map(h * 0.5, 1.4)


def strip_maps(n):
    """눌림 띠용 페인트: 가운데에 십자 홈 나사 하나. 28cm 타일."""
    albedo = paint_base(n, (0.66, 0.63, 0.55), 51, 0.09)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    d = np.sqrt((xx - n / 2) ** 2 + (yy - n / 2) ** 2)
    r = n * 0.045
    dome = np.clip(1 - (d / r) ** 2, 0, 1)
    cross = ((np.abs(xx - n / 2) < n * 0.003) | (np.abs(yy - n / 2) < n * 0.003)) & (d < r * 0.7)
    h = np.sqrt(dome) * 0.9 - cross * 0.5 + (fbm(n, 52) - 0.5) * 0.02
    albedo = albedo * (1 - (cross * 0.6)[..., None]) * (1 - (np.clip(1 - np.abs(d - r) / 3, 0, 1) * 0.35)[..., None])
    return albedo, normal_map(h, 2.2)


def text_layer(n, items):
    """items = (글자, x, y, 크기, 가로 배율). 흰 글자 마스크를 돌려준다. x, y는 0~1."""
    sc = bpy.data.scenes.new("labels")
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 16
    sc.render.resolution_x = sc.render.resolution_y = n
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("lw")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)
    sc.world = w
    ink = bpy.data.materials.new("ink")
    ink.use_nodes = True
    b = ink.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0, 0, 0, 1)
    b.inputs["Emission Color"].default_value = (1, 1, 1, 1)
    b.inputs["Emission Strength"].default_value = 1
    font = bpy.data.fonts.load(FONT)
    for body, x, y, size, sx in items:
        c = bpy.data.curves.new("t", 'FONT')
        c.body = body
        c.font = font
        c.size = size
        c.align_x = 'CENTER'
        c.align_y = 'CENTER'
        o = bpy.data.objects.new("t", c)
        c.materials.append(ink)
        o.location = (x, y, 0)
        o.scale = (sx, 1, 1)
        sc.collection.objects.link(o)
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 1.0
    cam.location = (0.5, 0.5, 1)
    sc.collection.objects.link(cam)
    sc.camera = cam
    path = os.path.join(bpy.app.tempdir, "aco_labels_%d.png" % len(items))
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=sc.name)
    im = bpy.data.images.load(path)
    a = np.array(im.pixels[:], dtype=np.float32).reshape(n, n, 4)
    bpy.data.images.remove(im)
    bpy.data.scenes.remove(sc)
    return a[:, :, 0]


def text_mask(n, labels):
    """의자 패치 12칸의 글자."""
    return text_layer(n, [(t, (i % 4 + 0.5) / 4, 1 - (i // 4 + 0.25) / 3, 0.055 if len(t) > 4 else 0.07, 0.84) for i, t in enumerate(labels)])


def sign_maps(n):
    """표지 4 x 4칸: 0 비상구, 1 시계판, 2~6 문 명판."""
    g = SIGN_GRID
    img = np.tile(np.array((0.62, 0.63, 0.62), dtype=np.float32)[None, None, :], (n, n, 1))
    img *= (1 + (fbm(n, 61) - 0.5)[..., None] * 0.12)
    cell = n // g

    def box_cell(i):
        c, r = i % g, i // g
        y0 = n - (r + 1) * cell
        return y0, y0 + cell, c * cell, (c + 1) * cell

    y0, y1, x0, x1 = box_cell(0)
    img[y0:y1, x0:x1] = (0.03, 0.42, 0.2)
    y0, y1, x0, x1 = box_cell(1)
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    d = np.sqrt((xx - (x0 + x1) / 2) ** 2 + (yy - (y0 + y1) / 2) ** 2) / (cell * 0.48)
    img[y0:y1, x0:x1] = np.where((d < 1)[..., None], np.array((0.86, 0.85, 0.8), dtype=np.float32), img[y0:y1, x0:x1])
    items = [("EXIT", 0.5 / g, 1 - 0.5 / g, 0.055, 1.0)]
    cx, cy = 1.5 / g, 1 - 0.5 / g
    for h in range(1, 13):
        t = math.radians(90 - h * 30)
        items.append((str(h), cx + math.cos(t) * 0.09, cy + math.sin(t) * 0.09, 0.022, 1.0))
    for k, name in enumerate(PLATES):
        i = k + 2
        items.append((name, (i % g + 0.5) / g, 1 - (i // g + 0.5) / g, 0.026 if len(name) > 8 else 0.03, 1.0))
    m = text_layer(n, items)[..., None]
    ink = np.full((n, n, 3), 0.04, dtype=np.float32)
    ink[n - cell:n, 0:cell] = (0.95, 1.0, 0.95)
    return img * (1 - m) + ink * m


def label_maps(n, fabric, fabric_n):
    """등판 뒤쪽 12칸: 직물 + 어두운 패치 + 흰 글자. 칸마다 같은 직물을 깐다. 픽셀 행은 아래에서 위로."""
    albedo = np.zeros((n, n, 3), dtype=np.float32)
    normal = np.zeros((n, n, 3), dtype=np.float32)
    fn = fabric.shape[0]
    idx = (np.arange(n) * 3) % fn
    albedo[...] = fabric[idx][:, idx]
    normal[...] = fabric_n[idx][:, idx]
    mask = text_mask(n, CHAIR_LABELS)
    cw, ch = n / 4, n / 3
    for r in range(3):
        for c in range(4):
            x0, x1 = int((c + 0.14) * cw), int((c + 0.86) * cw)
            y0, y1 = int((2 - r + 0.63) * ch), int((2 - r + 0.87) * ch)
            albedo[y0:y1, x0:x1] = albedo[y0:y1, x0:x1] * 0.22 + 0.012
            e = 5
            albedo[y0:y0 + e, x0:x1] *= 1.6
            albedo[y1 - e:y1, x0:x1] *= 1.6
            albedo[y0:y1, x0:x0 + e] *= 1.6
            albedo[y0:y1, x1 - e:x1] *= 1.6
            normal[y0:y1, x0:x1] = (0.5, 0.5, 1.0)
    albedo = albedo * (1 - mask[..., None]) + np.array((0.8, 0.74, 0.55), dtype=np.float32)[None, None, :] * mask[..., None]
    return albedo, normal


def photo_texture(index, w=1024, h=683):
    """교체용 임시 사진 3:2. 흑백 하늘과 바다에 어두운 윤곽 하나."""
    rng = np.random.RandomState(100 + index)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    u, v = xx / w, yy / h
    sky = 0.32 + 0.3 * v + (noise(max(w, h), 8, 200 + index)[:h, :w] - 0.5) * 0.12
    img = np.repeat(sky[..., None], 3, axis=2).astype(np.float32)
    line = 0.3 + rng.uniform(-0.06, 0.08)
    img[v < line] = (0.17, 0.19, 0.2)
    x0 = rng.uniform(0.12, 0.3)
    x1 = rng.uniform(0.7, 0.9)
    hull = (v > line - 0.02) & (v < line + 0.08) & (u > x0 + (line + 0.08 - v) * 0.5) & (u < x1)
    c = rng.uniform(x0 + 0.2, x1 - 0.15)
    top = (v >= line + 0.08) & (v < line + 0.2) & (u > c) & (u < c + 0.1)
    img[hull | top] = 0.06
    return img


def save_rect(out, name, img):
    h, w = img.shape[:2]
    a = np.ones((h, w, 4), dtype=np.float32)
    a[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new("r_" + name, w, h, alpha=False)
    im.pixels.foreach_set(a.ravel())
    p = os.path.join(out, name + ".png")
    im.filepath_raw = p
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)


def save_image(out, name, img):
    n = img.shape[0]
    a = np.ones((n, n, 4), dtype=np.float32)
    a[..., :3] = np.clip(img, 0, 1)
    im = bpy.data.images.new("i_" + name, n, n, alpha=False)
    im.pixels.foreach_set(a.ravel())
    p = os.path.join(out, "ACO_Int_" + name + ".png")
    im.filepath_raw = p
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)
    return p


def make_textures(out):
    os.makedirs(out, exist_ok=True)
    res = {}

    def put(name, img):
        res[name] = save_image(out, name, img)

    n = 1024
    a, b = wall_maps(2048)
    put("wall", a)
    put("wall_n", b)
    f, fn = fabric_maps(n)
    put("fabric", f)
    put("fabric_n", fn)
    a, b = deck_maps(2048)
    put("deck", a)
    put("deck_n", b)
    a, b = label_maps(2048, f, fn)
    put("label", a)
    put("label_n", b)
    put("paint", paint_base(n, (0.66, 0.63, 0.55), 31, 0.09))
    a, b = keys_maps(n)
    put("keys", a)
    put("keys_n", b)
    a, b = mat_maps(n)
    put("mat", a)
    put("mat_n", b)
    a, b = strip_maps(n)
    put("strip", a)
    put("strip_n", b)
    put("blue", paint_base(n, (0.06, 0.16, 0.42), 45, 0.1))
    w = paint_base(n, (0.42, 0.24, 0.11), 47, 0.2)
    w *= (0.93 + 0.07 * np.sin(np.arange(n)[None, :] / n * math.tau * 60 + fbm(n, 48) * 2.2))[..., None]
    put("wood", w)
    put("screen", paint_base(n, (0.012, 0.018, 0.025), 49, 0.2))
    put("led", paint_base(n // 4, (0.12, 0.7, 0.25), 58, 0.05))
    put("bed", paint_base(n // 2, (0.72, 0.72, 0.68), 62, 0.12))
    put("blanket", paint_base(n // 2, (0.07, 0.11, 0.2), 63, 0.25))
    put("gold", paint_base(n // 4, (0.8, 0.56, 0.18), 64, 0.06))
    put("silver", paint_base(n // 4, (0.72, 0.73, 0.75), 65, 0.06))
    put("canvas", paint_base(n // 2, (0.2, 0.24, 0.13), 66, 0.3))
    put("orange", paint_base(n // 2, (0.85, 0.42, 0.06), 67, 0.15))
    put("webbing", paint_base(n // 4, (0.08, 0.09, 0.05), 69, 0.2))
    put("curtain", paint_base(n // 2, (0.16, 0.2, 0.24), 70, 0.2))
    put("tvscreen", np.zeros((16, 16, 3), dtype=np.float32))
    put("glass", paint_base(n // 4, (0.75, 0.82, 0.84), 68, 0.02))
    put("ball", paint_base(n // 4, (0.02, 0.05, 0.13), 60, 0.1))
    put("cable", paint_base(n, (0.03, 0.03, 0.032), 53, 0.3))
    put("sign", sign_maps(n))
    c = paint_base(n, (0.46, 0.3, 0.13), 55, 0.25)
    put("cork", c * (0.9 + 0.2 * noise(n, 200, 56)[..., None]))
    photos = os.path.join(out, "Photos")
    os.makedirs(photos, exist_ok=True)
    for i in range(PHOTO_COUNT):
        target = os.path.join(photos, "ACO_Photo_%02d.png" % i)
        if not os.path.exists(target):     # 이미 바꿔 넣은 사진은 덮어쓰지 않는다
            save_rect(photos, "ACO_Photo_%02d" % i, photo_texture(i))
    put("white", paint_base(n, (0.8, 0.79, 0.75), 33, 0.05))
    put("door", paint_base(n, (0.05, 0.135, 0.105), 35, 0.2))
    put("alum", paint_base(n, (0.62, 0.62, 0.6), 37, 0.08))
    put("steel", paint_base(n, (0.36, 0.37, 0.38), 39, 0.15))
    put("rubber", paint_base(n, (0.035, 0.035, 0.038), 41, 0.2))
    put("red", paint_base(n, (0.6, 0.05, 0.035), 43, 0.08))
    return res


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    os.makedirs(MODEL_DIR, exist_ok=True)
    make_textures(TEXTURE_DIR)
    rot = Matrix.Rotation(math.pi, 4, 'Z')
    total = 0
    parts = {p.name: p for p in all_parts()}
    rooms = all_rooms()
    prune_hidden(parts, rooms)
    shell_names = {sh.name for shells, place in rooms.values() for sh in shells}
    for p in parts.values():
        if p.name in shell_names:
            continue
        print("PART", p.name, p.tris())
        total += p.tris()
        for o in p.objects(scene):
            o.data.transform(rot)
            o.data.update()
    print("TOTAL", total)
    global LAST_PARTS, LAST_ROOMS
    LAST_PARTS, LAST_ROOMS = parts, rooms
    for shells, place in rooms.values():
        print("ROOM modules", len(place))
        for sh in shells:
            print("SHELL", sh.name, sh.tris())
            for o in sh.objects(scene):
                o.data.transform(rot)
                o.data.update()
    write_layout(os.path.join(MODEL_DIR, "ACO_Interior_Layout.txt"), rooms)
    write_rooms(os.path.join(MODEL_DIR, "ACO_Interior_Rooms.txt"))
    bpy.ops.export_scene.fbx(filepath=os.path.join(MODEL_DIR, FBX_NAME), object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_triangles=True, path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    print("DONE")


if __name__ == "__main__":
    main()
