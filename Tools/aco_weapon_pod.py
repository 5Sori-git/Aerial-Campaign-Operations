"""파일런용 외부 무장 컨테이너 시안. 각진 다면체 포드, 닫힌 상태. 원본 FBX는 건드리지 않는다.
좌표: 기수 -Y, 위 +Z, 단위 m, 원점 = 파일런과 닿는 자리."""
import math
import os
import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = r"C:\Users\KYJ\AppData\Local\VRChatProjects\ACO_Claude\Assets\Pit-bool\FA-18"
FBX = ROOT + r"\FBX\FA-18E.fbx"
BODY = ROOT + r"\material\E\F-18.E_Base_color.png"
OUT = "C:/Users/KYJ/AppData/Local/VRChatProjects/ACO_Claude/Assets/ACO_Addon/Models/WeaponPod"
os.makedirs(OUT, exist_ok=True)

PAINT = np.array([0.519, 0.549, 0.56], dtype=np.float32)
SHADE = 0.84
TEX_W, TEX_H = 2048, 1024
PPM = 420.0
TOP_DROP = -0.23         # 포드 윗면 높이의 음수: 원점은 기존 Tank 메시의 축(파일런 바닥 0.25m 아래)
MOUNT_Z = 0.28           # 받침 윗면 = 파일런 바닥
TOOTH_H = 0.135           # 도어 톱니 높이의 절반
TOOTH_D = 0.1           # 도어 톱니 깊이

# 단면: 폭, 높이, 윗면 높이(0이면 몸통 윗면과 같다)
STATIONS = [
    (-2.2, 0.10, 0.05, 0.24),
    (-1.7, 0.62, 0.46, 0.09),
    (-1.35, 0.82, 0.64, 0.0),
    (1.6, 0.82, 0.64, 0.0),
    (2.0, 0.6, 0.44, 0.08),
    (2.35, 0.08, 0.05, 0.22),
]


def section(y, w, h, drop):
    """윗면 ─ 어깨 ─ 아랫면의 육각 단면(시계 방향)."""
    z = -TOP_DROP - drop
    return [
        Vector((-w * 0.33, y, z)), Vector((w * 0.33, y, z)),
        Vector((w * 0.5, y, z - h * 0.42)),
        Vector((w * 0.27, y, z - h)), Vector((-w * 0.27, y, z - h)),
        Vector((-w * 0.5, y, z - h * 0.42)),
    ]


DOOR_FRONT = 1.0        # 도어 앞끝(텍스처 U, m)
DOOR_REAR = 3.62        # 도어 뒤끝
DOOR_TOP = 0.12         # 어깨 모서리에서 도어 경첩까지
DOOR_T = 0.025          # 도어 두께
INNER = (4.72, 1.0)     # 내부 색 자리(텍스처 U, V)
RAIL = (4.72, 0.3)      # 레일 색 자리
ROUND = (4.72, 1.9)     # 미사일 색 자리


def saw_at(v, bottom):
    """도어 앞뒤 끝의 톱니: 아랫면은 일자, 옆면만 톱니. 끝이 기수 쪽을 향한다."""
    d = max(bottom[0] - v, v - bottom[1], 0.0)
    p = d / (TOOTH_H * 2)
    k = math.floor(p)
    f = p - k
    return -TOOTH_D * (f if k % 2 == 0 else 1 - f)


class Builder:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def face(self, points, uvs, outward):
        """outward: 면이 바라볼 방향. 감기를 맞춘다."""
        n = (points[1] - points[0]).cross(points[2] - points[0])
        if n.dot(outward) < 0:
            points = list(reversed(points))
            uvs = list(reversed(uvs))
        f = self.bm.faces.new([self.bm.verts.new(p) for p in points])
        for loop, t in zip(f.loops, uvs):
            loop[self.uv].uv = t

    def mesh(self):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def build_pod():
    rings = [section(*st) for st in STATIONS]
    mid = rings[2]
    perimeter = [0.0]
    for j in range(6):
        perimeter.append(perimeter[-1] + (mid[(j + 1) % 6] - mid[j]).length)
    y0 = STATIONS[0][0]
    zc = sum((p.z for p in mid), 0.0) / 6

    def uv(y, v):
        return (((y - y0) + 0.1) * PPM / TEX_W, (v + 0.1) * PPM / TEX_H)

    def flat(t):
        return ((t[0] + 0.1) * PPM / TEX_W, (t[1] + 0.1) * PPM / TEX_H)

    def on_body(v, y):
        v = v % perimeter[6]
        for j in range(6):
            if v <= perimeter[j + 1]:
                t = (v - perimeter[j]) / (perimeter[j + 1] - perimeter[j])
                p = mid[j].lerp(mid[(j + 1) % 6], t)
                return Vector((p.x, y, p.z))
        return Vector((mid[0].x, y, mid[0].z))

    def out(p):
        return Vector((p.x, 0, p.z - zc))

    vc = (perimeter[3] + perimeter[4]) * 0.5
    v0 = perimeter[2] - DOOR_TOP
    v1 = perimeter[5] + DOOR_TOP
    stops = {v0, v1, vc, perimeter[2], perimeter[3], perimeter[4], perimeter[5]}
    bottom = (perimeter[3], perimeter[4])
    for k in range(1, 8):
        stops.add(perimeter[3] - k * TOOTH_H)
        stops.add(perimeter[4] + k * TOOTH_H)
    rows = sorted(r for r in stops if v0 <= r <= v1)
    yf = lambda v: y0 + DOOR_FRONT + saw_at(v, bottom)
    yr = lambda v: y0 + DOOR_REAR + saw_at(v, bottom)
    body_front = STATIONS[2][0]
    body_rear = STATIONS[3][0]

    # 몸체: 앞뒤 끝 + 위쪽 면 + 도어를 둘러싼 띠
    shell = Builder("ACO_WeaponPod_Shell")
    for i in (0, 1, 3, 4):
        for j in range(6):
            n = (j + 1) % 6
            q = [rings[i][j], rings[i][n], rings[i + 1][n], rings[i + 1][j]]
            yi, yk = STATIONS[i][0], STATIONS[i + 1][0]
            shell.face(q, [uv(yi, perimeter[j]), uv(yi, perimeter[j + 1]), uv(yk, perimeter[j + 1]), uv(yk, perimeter[j])],
                       out(sum(q, Vector()) * 0.25) + Vector((0, (yi + yk) * 0.05, 0)))
    for ring, st, sign in ((rings[0], STATIONS[0], -1), (rings[-1], STATIONS[-1], 1)):
        shell.face(list(ring), [uv(st[0], 0)] * 6, Vector((0, sign, 0)))
    tops = [(perimeter[5] + DOOR_TOP, perimeter[5] + 0.0, None)]
    bands = [(v1, perimeter[6]), (0.0, perimeter[1]), (perimeter[1], v0)]
    extra = []
    for a_, b_ in bands:
        cuts = [a_] + [p_ for p_ in perimeter if a_ < p_ < b_] + [b_]
        for c0, c1 in zip(cuts, cuts[1:]):
            q = [on_body(c0, body_front), on_body(c1, body_front), on_body(c1, body_rear), on_body(c0, body_rear)]
            shell.face(q, [uv(body_front, c0), uv(body_front, c1), uv(body_rear, c1), uv(body_rear, c0)], out(sum(q, Vector()) * 0.25))
    for c0, c1 in zip(rows, rows[1:]):
        q = [on_body(c0, body_front), on_body(c1, body_front), on_body(c1, yf(c1)), on_body(c0, yf(c0))]
        shell.face(q, [uv(body_front, c0), uv(body_front, c1), uv(yf(c1), c1), uv(yf(c0), c0)], out(sum(q, Vector()) * 0.25))
        q = [on_body(c0, yr(c0)), on_body(c1, yr(c1)), on_body(c1, body_rear), on_body(c0, body_rear)]
        shell.face(q, [uv(yr(c0), c0), uv(yr(c1), c1), uv(body_rear, c1), uv(body_rear, c0)], out(sum(q, Vector()) * 0.25))
    # 파일런 받침
    w, l = 0.14, 0.8
    top = [Vector((-w * 0.7, -l * 0.9, MOUNT_Z)), Vector((w * 0.7, -l * 0.9, MOUNT_Z)), Vector((w * 0.7, l * 0.9, MOUNT_Z)), Vector((-w * 0.7, l * 0.9, MOUNT_Z))]
    bot = [Vector((-w, -l, -TOP_DROP - 0.01)), Vector((w, -l, -TOP_DROP - 0.01)), Vector((w, l, -TOP_DROP - 0.01)), Vector((-w, l, -TOP_DROP - 0.01))]
    centre = Vector((0, 0, (MOUNT_Z - TOP_DROP) * 0.5))
    for k_ in range(4):
        n = (k_ + 1) % 4
        q = [top[n], top[k_], bot[k_], bot[n]]
        shell.face(q, [uv(0.0, 0.0)] * 4, sum(q, Vector()) * 0.25 - centre)
    shell.face(list(top), [uv(0.0, 0.0)] * 4, Vector((0, 0, 1)))

    # 도어: 원점은 경첩(쪽 끝). 경첩축은 포드 길이 방향이다
    def door(name, a_, b_, hinge_v):
        hinge = on_body(hinge_v, 0.0)
        d = Builder(name)
        cuts = [r for r in rows if a_ <= r <= b_]
        inner = flat(INNER)
        for c0, c1 in zip(cuts, cuts[1:]):
            q = [on_body(c0, yf(c0)), on_body(c1, yf(c1)), on_body(c1, yr(c1)), on_body(c0, yr(c0))]
            normal = out(sum(q, Vector()) * 0.25)
            d.face([p - hinge for p in q], [uv(yf(c0), c0), uv(yf(c1), c1), uv(yr(c1), c1), uv(yr(c0), c0)], normal)
            back = [p - normal.normalized() * DOOR_T - hinge for p in q]
            d.face(back, [inner] * 4, -normal)
            d.face([q[0] - hinge, q[1] - hinge, back[1], back[0]], [inner] * 4, Vector((0, -1, 0)))
            d.face([q[3] - hinge, q[2] - hinge, back[2], back[3]], [inner] * 4, Vector((0, 1, 0)))
        return d.mesh(), hinge

    door_r, hinge_r = door("ACO_WeaponPod_DoorR", v0, vc, v0)
    door_l, hinge_l = door("ACO_WeaponPod_DoorL", vc, v1, v1)

    # 내부: 벽, 앞뒤 격벽, 레일, 미사일
    bay = Builder("ACO_WeaponPod_Bay")
    inner = flat(INNER)
    rail = flat(RAIL)
    rnd = flat(ROUND)
    y_a = y0 + DOOR_FRONT - 0.03
    y_b = y0 + DOOR_REAR + TOOTH_D + 0.03
    ring = [Vector((p.x * 0.96, 0, zc + (p.z - zc) * 0.96)) for p in mid]
    for j in (5, 0, 1):   # 아래쪽은 도어 자리라 비운다
        n = (j + 1) % 6
        q = [Vector((ring[j].x, y_a, ring[j].z)), Vector((ring[n].x, y_a, ring[n].z)), Vector((ring[n].x, y_b, ring[n].z)), Vector((ring[j].x, y_b, ring[j].z))]
        bay.face(q, [inner] * 4, -out(sum(q, Vector()) * 0.25))
    bay.face([Vector((p.x, y_a, p.z)) for p in ring], [inner] * 6, Vector((0, 1, 0)))
    bay.face([Vector((p.x, y_b, p.z)) for p in ring], [inner] * 6, Vector((0, -1, 0)))

    def box(cx, cz, wx, wz, ya, yb, t):
        c = [Vector((cx + sx * wx, y, cz + sz * wz)) for y in (ya, yb) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        centre = Vector((cx, (ya + yb) * 0.5, cz))
        for f in ((0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (0, 1, 2, 3), (4, 5, 6, 7)):
            q = [c[i] for i in f]
            bay.face(q, [t] * 4, sum(q, Vector()) * 0.25 - centre)

    rail_z = mid[0].z - 0.04
    box(0, rail_z, 0.2, 0.03, y_a + 0.05, y_b - 0.05, rail)
    for cx in (-0.19, 0.0, 0.19):
        box(cx, rail_z - 0.05, 0.025, 0.03, y_a + 0.4, y_b - 0.4, rail)
    return {"shell": shell.mesh(), "door_r": door_r, "door_l": door_l, "bay": bay.mesh(), "hinge_r": hinge_r, "hinge_l": hinge_l}, perimeter


def value_noise(w, h, cell, seed):
    rng = np.random.default_rng(seed)
    gx, gy = int(w / cell) + 2, int(h / cell) + 2
    g = rng.random((gy, gx))
    xs = np.linspace(0, gx - 1.001, w)
    ys = np.linspace(0, gy - 1.001, h)
    xi, yi = xs.astype(int), ys.astype(int)
    tx, ty = xs - xi, ys - yi
    a, b = g[np.ix_(yi, xi)], g[np.ix_(yi, xi + 1)]
    c, d = g[np.ix_(yi + 1, xi)], g[np.ix_(yi + 1, xi + 1)]
    top = a * (1 - tx) + b * tx
    bot = c * (1 - tx) + d * tx
    return top * (1 - ty[:, None]) + bot * ty[:, None]


FONT = r"C:\Windows\Fonts\arialbd.ttf"
INK = (0.13, 0.14, 0.15)
RED = (0.62, 0.08, 0.07)
WHITE = (0.93, 0.93, 0.9)
YELLOW = (0.84, 0.68, 0.13)
ORANGE = (0.9, 0.47, 0.09)


def render_marks(perimeter):
    """글자·기호를 텍스처 좌표에 놓고 한 장으로 렌더한다. 면마다 바깥에서 읽히도록 뒤집어 놓는다."""
    page = bpy.data.scenes.new("page")
    page.render.engine = 'BLENDER_WORKBENCH'
    page.display.shading.light = 'FLAT'
    page.display.shading.color_type = 'MATERIAL'
    page.display.shading.show_shadows = False
    page.display.render_aa = '32'
    page.render.film_transparent = True
    page.render.resolution_x = TEX_W
    page.render.resolution_y = TEX_H
    page.render.image_settings.file_format = 'PNG'
    page.render.image_settings.color_mode = 'RGBA'
    page.view_settings.view_transform = 'Standard'
    page.render.filepath = os.path.join(OUT, "_marks.png")
    cam = bpy.data.objects.new("page_cam", bpy.data.cameras.new("page_cam"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = TEX_W / PPM
    cam.location = (TEX_W / PPM * 0.5, TEX_H / PPM * 0.5, 5)
    page.collection.objects.link(cam)
    page.camera = cam
    font = bpy.data.fonts.load(FONT)
    mats = {}

    def material(color):
        if color not in mats:
            m = bpy.data.materials.new("m")
            m.diffuse_color = tuple(c ** 2.2 for c in color) + (1,)
            mats[color] = m
        return mats[color]

    order = [0.0]

    def place(o, u, v, mirror, color):
        order[0] += 0.001
        o.location = (u + 0.1, v + 0.1, order[0])
        o.scale = (1, -1, 1) if mirror == 'R' else (-1, 1, 1)
        o.data.materials.append(material(color))
        page.collection.objects.link(o)

    def text(body, u, v, side, size, color=INK, align='CENTER'):
        c = bpy.data.curves.new("t", 'FONT')
        c.body = body
        c.font = font
        c.size = size
        c.align_x = align
        c.align_y = 'CENTER'
        c.space_line = 1.15
        c.space_character = 1.04
        place(bpy.data.objects.new("t", c), u, v, side, color)

    def polygon(points, u, v, side, color):
        bm = bmesh.new()
        bm.faces.new([bm.verts.new((x, y, 0)) for x, y in points])
        me = bpy.data.meshes.new("p")
        bm.to_mesh(me)
        bm.free()
        place(bpy.data.objects.new("p", me), u, v, side, color)

    def rect(w, h, u, v, side, color):
        polygon([(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)], u, v, side, color)

    def frame(w, h, u, v, side, color, t=0.004):
        rect(w, t, u, v + h / 2, side, color)
        rect(w, t, u, v - h / 2, side, color)
        rect(t, h, u - w / 2, v, side, color)
        rect(t, h, u + w / 2, v, side, color)

    def circle(r, u, v, side, color, n=28):
        polygon([(r * math.cos(k * math.tau / n), r * math.sin(k * math.tau / n)) for k in range(n)], u, v, side, color)

    def side_marks(side, vmap):
        """vmap: 오른쪽 기준 둘레 좌표 → 이쪽 둘레 좌표."""
        shoulder = vmap(perimeter[1] + 0.09)   # 위 패널선과 도어 리벳 사이
        upper = vmap(perimeter[2] + 0.1)       # 어깨 패널선 아래
        lower = vmap(perimeter[2] + 0.28)
        # 무장 경고 띠
        rect(0.86, 0.085, 1.68, upper, side, RED)
        text("DANGER  LIVE ORDNANCE", 1.68, upper, side, 0.05, WHITE)
        # 위험 줄무늬(도어 앞쪽)
        for k in range(7):
            x = 1.2 + k * 0.03
            polygon([(-0.012, -0.03), (0.012, -0.03), (0.032, 0.03), (0.008, 0.03)], x, lower, side, YELLOW if k % 2 == 0 else INK)
        # 도어 주의 문구
        text("CAUTION\nDOORS OPEN AUTOMATICALLY\nKEEP CLEAR OF BAY AREA", 1.82, lower, side, 0.036)
        # 폭발물 등급 표지(주황 마름모)
        polygon([(0, 0.075), (0.075, 0), (0, -0.075), (-0.075, 0)], 2.5, lower, side, ORANGE)
        text("1.3", 2.5, lower, side, 0.052)
        # 지상 안전핀
        circle(0.034, 2.82, upper, side, RED)
        circle(0.021, 2.82, upper, side, WHITE)
        text("GROUND LOCK PIN\nREMOVE BEFORE FLIGHT", 3.14, upper, side, 0.031)
        # 재원 표
        frame(0.78, 0.15, 3.04, lower, side, INK)
        text("WPN CONTAINER ACW-7\nMISSILE LOAD 6 RDS\nGROSS WT 2860 LB", 3.04, lower, side, 0.036)
        # 어깨면: 발판 금지, 인양점
        frame(0.3, 0.075, 1.3, shoulder, side, INK)
        text("NO STEP", 1.3, shoulder, side, 0.042)
        polygon([(0, -0.035), (0.04, 0.03), (-0.04, 0.03)], 2.05, shoulder, side, INK)
        text("HOIST POINT", 2.34, shoulder, side, 0.034)
        polygon([(0, -0.035), (0.04, 0.03), (-0.04, 0.03)], 2.63, shoulder, side, INK)
        text("ESD SENSITIVE  GROUND BEFORE SERVICE", 3.2, shoulder, side, 0.027)

    side_marks('R', lambda v: v)
    vc = (perimeter[3] + perimeter[4]) * 0.5
    side_marks('L', lambda v: 2 * vc - v)
    # 아랫면: 방향과 해치 문구
    bottom = vc + 0.11
    polygon([(0.09, 0), (0, 0.05), (0, 0.022), (-0.1, 0.022), (-0.1, -0.022), (0, -0.022), (0, -0.05)], 1.5, bottom, 'R', INK)
    # 화살표가 꼬리 쪽이 아니라 기수 쪽을 가리키게 뒤집는다
    bpy.data.objects[page.collection.objects[-1].name].rotation_euler = (0, 0, math.pi)
    text("FWD", 1.72, bottom, 'R', 0.05)
    text("JETTISON HAZARD AREA  STAY CLEAR", 2.75, vc - 0.11, 'R', 0.034)
    bpy.ops.render.render(write_still=True, scene="page")
    img = bpy.data.images.load(os.path.join(OUT, "_marks.png"))
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def make_texture(perimeter):
    w, h = TEX_W, TEX_H
    xx, yy = np.meshgrid((np.arange(w) + 0.5) / PPM - 0.1, (np.arange(h) + 0.5) / PPM - 0.1)
    img = np.tile(PAINT[None, None, :] * SHADE, (h, w, 1)).astype(np.float32)
    img *= 1 + (value_noise(w, h, 240, 3) - 0.5)[..., None] * 0.04

    def line(d, width=0.0032):
        return np.exp(-(d / width) ** 2)

    lines = np.zeros((h, w), dtype=np.float32)
    # 면 경계 안쪽 패널선
    for p in perimeter:
        lines = np.maximum(lines, line(yy - p - 0.03) * 0.35)
        lines = np.maximum(lines, line(yy - p + 0.03) * 0.35)
    # 무장창 도어: 옆면 → 아랫면 → 옆면, 앞뒤 끝은 톱니
    v0 = perimeter[2] - 0.12
    v1 = perimeter[5] + 0.12
    y_front = DOOR_FRONT
    y_rear = DOOR_REAR
    vc = (v0 + v1) * 0.5
    d_side = np.maximum(np.maximum(perimeter[3] - yy, yy - perimeter[4]), 0.0)
    p = d_side / (TOOTH_H * 2)
    k = np.floor(p)
    f = p - k
    saw = -TOOTH_D * np.where(k % 2 == 0, f, 1 - f)
    inside = (yy > v0) & (yy < v1)
    band = (xx > y_front - 0.15) & (xx < y_rear + 0.15)
    lines = np.maximum(lines, line(xx - (y_front + saw)) * inside)
    lines = np.maximum(lines, line(xx - (y_rear + saw)) * inside)
    lines = np.maximum(lines, line(yy - v0) * band * (xx > y_front) * (xx < y_rear + TOOTH_D))
    lines = np.maximum(lines, line(yy - v1) * band * (xx > y_front) * (xx < y_rear + TOOTH_D))
    # 가운데 맞닿는 선과 경첩 쪽 리벳
    lines = np.maximum(lines, line(yy - vc) * band * (xx > y_front) * (xx < y_rear))
    rivet = np.zeros((h, w), dtype=np.float32)
    for v in (v0 - 0.035, v1 + 0.035):
        for y in np.arange(y_front + 0.08, y_rear, 0.16):
            rivet = np.maximum(rivet, np.exp(-((xx - y) ** 2 + (yy - v) ** 2) / 0.006 ** 2))
    img *= 1 - lines[..., None] * 0.42
    img *= 1 - rivet[..., None] * 0.3
    img = np.clip(img, 0, 1)
    # 내부 색 자리(몸체 UV 바깥)
    colours = ((xx > 4.62, (0.34, 0.39, 0.36)), ((xx > 4.62) & (yy < 0.6), (0.14, 0.15, 0.16)), ((xx > 4.62) & (yy > 1.6), (0.82, 0.83, 0.8)))
    for mask, colour in colours:
        img[mask] = colour
    page = render_marks(perimeter)
    alpha = page[..., 3:4] * 0.94
    img = img * (1 - alpha) + page[..., :3] * alpha
    im = bpy.data.images.new("ACO_WeaponPod_Albedo", w, h, alpha=True)
    im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(np.dstack((img, np.ones((h, w), dtype=np.float32))).ravel())
    im.filepath_raw = os.path.join(OUT, "ACO_WeaponPod_Albedo.png")
    im.file_format = 'PNG'
    im.save()


bpy.ops.wm.read_factory_settings(use_empty=True)
parts, perimeter = build_pod()
make_texture(perimeter)
for k_, v_ in parts.items():
    if hasattr(v_, "polygons"):
        print("POD", k_, "tris", sum(len(p.vertices) - 2 for p in v_.polygons))

# ── 내보내기: 기존 Tank와 같은 축(Unity에서 +Z = 기수, +Y = 아래), 원점 = Tank 자리 ──
scene = bpy.context.scene
mat = bpy.data.materials.new("ACO_WeaponPod")
mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(os.path.join(OUT, "ACO_WeaponPod_Albedo.png"))
mat.node_tree.links.new(tex.outputs[0], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
items = (("ACO_WeaponPod_Shell", "shell", None), ("ACO_WeaponPod_Bay", "bay", None),
         ("ACO_WeaponPod_DoorR", "door_r", parts["hinge_r"]), ("ACO_WeaponPod_DoorL", "door_l", parts["hinge_l"]))
objects = []
for name, key, hinge in items:
    me = parts[key]
    me.materials.append(mat)
    for v in me.vertices:
        v.co = Vector((-v.co.x, v.co.y, -v.co.z))
    me.update()
    o = bpy.data.objects.new(name, me)
    if hinge is not None:
        o.location = (-hinge.x, hinge.y, -hinge.z)
    scene.collection.objects.link(o)
    objects.append(o)
bpy.ops.object.select_all(action='DESELECT')
for o in objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "ACO_WeaponPod.fbx"),
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
os.remove(os.path.join(OUT, "_marks.png"))
print("DONE")
