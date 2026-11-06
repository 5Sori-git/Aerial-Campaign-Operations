"""지형 장식 모델: 도시 칸 3종, 마을 2종, 항구, 비행장, 등대, 레이더, 통신탑, 유적.
Blender 안에서 돌린다: blender --background --factory-startup --python Tools/aco_deco_models.py
만드는 것: Models/Deco/ACO_Deco.fbx, Models/Deco/ACO_Deco_Colliders.txt, Textures/Deco/ACO_Deco_*.png.
좌표는 Unity 축(x, y 위, z), 단위 m. 내보낼 때 Z축으로 180도 돌려 FBX를 거친 뒤 같은 좌표가 되게 한다.
메시 이름은 "프리팹__재질"이다. ACO_DecoBuilder가 재질마다 자식 렌더러 하나로 프리팹을 만든다.
도시 격자는 육각 칸 간격의 정수 배라서 칸끼리 그대로 이어진다. 그래서 칸은 0도와 180도로만 돌린다.
비탈 큰길은 칸 메시에 들어 있고 지형 치마(15도)와 같은 면에 놓인다. 낮은 이웃 쪽에서만 보이고 같은 높이나 높은 이웃 쪽에서는 지형에 덮인다."""
import math
import os
import random
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
MODEL_DIR = os.path.join(ROOT, "Assets", "ACO_Addon", "Models", "Deco")
TEXTURE_DIR = os.path.join(ROOT, "Assets", "ACO_Addon", "Textures", "Deco")
FBX_NAME = "ACO_Deco.fbx"
COLLIDER_NAME = "ACO_Deco_Colliders.txt"

# 재질: (타일 가로 m, 세로 m)
TILE = {
    "glass": (64, 64), "office": (80, 64), "plaster": (96, 48), "brick": (96, 48), "metal": (20, 20), "roofflat": (40, 40), "rooftile": (10, 10),
    "asphalt": (60, 60), "road": (24, 18), "pave": (16, 16), "stone": (12, 12), "field": (40, 40), "grass": (50, 50), "concrete": (24, 24),
    "white": (20, 20), "red": (20, 20), "steel": (10, 10), "lamp": (10, 10), "glow": (10, 10), "copper": (10, 10), "leaf": (10, 10), "container": (24, 5.2), "runway": (60, 30),
}
# 창이 있는 재질의 가로 창 수. 불 켜진 창이 되풀이되어 보이지 않게 타일 하나에 창을 많이 담는다
WINDOW_COLS = {"glass": 32, "office": 20, "plaster": 16, "brick": 16}


class D:
    def __init__(self, seed=1):
        self.f = {}
        self.rnd = random.Random(seed)
        self.colliders = []
        self.struts = []

    def R(self, a, b):
        return a + self.rnd.random() * (b - a)

    def col(self, x, z, w, l, y0, y1, yaw=0.0):
        """상자 콜라이더. yaw는 rect와 같은 뜻이고 Unity Y 회전으로는 부호가 반대다."""
        self.colliders.append((x, (y0 + y1) * 0.5, z, w, y1 - y0, l, -yaw))

    def col_strut(self, a, b, w):
        """비스듬한 부재의 상자 콜라이더. a, b는 (x, y, z) 양 끝, w는 단면 폭."""
        self.struts.append((a, b, w))

    def beam(self, mat, a, b, w, col_w=None):
        """부재 + 같은 자리의 콜라이더."""
        self.strut(mat, a, b, w)
        self.col_strut(a, b, col_w or w)

    def face(self, mat, pts, uvs, out):
        self.f.setdefault(mat, []).append((pts, uvs, out))

    def walls(self, mat, outline, y0, y1, r=None):
        """outline을 세운 벽. 창 무늬가 잘리지 않게 칸 단위로 시작한다."""
        tx, ty = TILE[mat]
        cols = WINDOW_COLS.get(mat, 4)
        u = self.rnd.randint(0, cols - 1) * tx / cols
        n = len(outline)
        c = (sum(p[0] for p in outline) / n, sum(p[1] for p in outline) / n)
        for k in range(n):
            a, b = outline[k], outline[(k + 1) % n]
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            pts = [(a[0], y0, a[1]), (b[0], y0, b[1]), (b[0], y1, b[1]), (a[0], y1, a[1])]
            uv = [(u / tx, y0 / ty), ((u + L) / tx, y0 / ty), ((u + L) / tx, y1 / ty), (u / tx, y1 / ty)]
            out = ((a[0] + b[0]) / 2 - c[0], 0, (a[1] + b[1]) / 2 - c[1])
            self.face(mat, pts, uv, out)
            u += L

    def cap(self, mat, outline, y, up=True):
        tx, ty = TILE[mat]
        self.face(mat, [(p[0], y, p[1]) for p in outline], [(p[0] / tx, p[1] / ty) for p in outline], (0, 1 if up else -1, 0))

    def prism(self, mat, outline, y0, y1, top="roofflat"):
        self.walls(mat, outline, y0, y1)
        if top:
            self.cap(top, outline, y1)

    def rect(self, cx, cz, w, l, yaw=0.0):
        c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        return [(cx + x * c - z * s, cz + x * s + z * c) for x, z in ((-w / 2, -l / 2), (w / 2, -l / 2), (w / 2, l / 2), (-w / 2, l / 2))]

    def box(self, mat, cx, cz, w, l, y0, y1, top="roofflat", yaw=0.0):
        self.prism(mat, self.rect(cx, cz, w, l, yaw), y0, y1, top or mat)

    def octagon(self, cx, cz, w, l, c, yaw=0.0):
        co, si = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        x, z = w / 2, l / 2
        pts = ((-x + c, -z), (x - c, -z), (x, -z + c), (x, z - c), (x - c, z), (-x + c, z), (-x, z - c), (-x, -z + c))
        return [(cx + a * co - b * si, cz + a * si + b * co) for a, b in pts]

    def circle(self, cx, cz, r, n):
        return [(cx + r * math.cos(k * math.tau / n), cz + r * math.sin(k * math.tau / n)) for k in range(n)]

    def loft(self, mat, rings, top=None):
        """(원 반경 또는 윤곽, y) 고리들을 잇는다."""
        n = len(rings[0][0])
        cx = sum(p[0] for p in rings[0][0]) / n
        cz = sum(p[1] for p in rings[0][0]) / n
        tx, ty = TILE[mat]
        for (a, ya), (b, yb) in zip(rings, rings[1:]):
            u = 0.0
            for k in range(n):
                m = (k + 1) % n
                L = math.hypot(a[m][0] - a[k][0], a[m][1] - a[k][1])
                pts = [(a[k][0], ya, a[k][1]), (a[m][0], ya, a[m][1]), (b[m][0], yb, b[m][1]), (b[k][0], yb, b[k][1])]
                uv = [(u / tx, ya / ty), ((u + L) / tx, ya / ty), ((u + L) / tx, yb / ty), (u / tx, yb / ty)]
                ox, oz = (a[k][0] + a[m][0]) / 2 - cx, (a[k][1] + a[m][1]) / 2 - cz
                self.face(mat, pts, uv, (ox, (ya - yb) * 0.2 if abs(ox) + abs(oz) < 1e-6 else 0, oz))
                u += L
        if top:
            self.cap(top, rings[-1][0], rings[-1][1])

    def cyl(self, mat, cx, cz, r0, r1, y0, y1, n=12, top=None):
        self.loft(mat, [(self.circle(cx, cz, r0, n), y0), (self.circle(cx, cz, r1, n), y1)], top if top else mat)

    def dome(self, mat, cx, cz, r, y, rows=5, n=14):
        rings = []
        for k in range(rows + 1):
            a = k / rows * math.pi / 2 * 0.97
            rings.append((self.circle(cx, cz, max(r * math.cos(a), 0.05), n), y + r * math.sin(a)))
        self.loft(mat, rings)

    def strut(self, mat, a, b, w):
        """사각 단면 부재."""
        A, B = Vector(a), Vector(b)
        d = (B - A).normalized()
        s = d.orthogonal().normalized() * w * 0.5
        t = d.cross(s).normalized() * w * 0.5
        ring = [s + t, -s + t, -s - t, s - t]
        for k in range(4):
            p = [tuple(A + ring[k]), tuple(A + ring[(k + 1) % 4]), tuple(B + ring[(k + 1) % 4]), tuple(B + ring[k])]
            mid = (ring[k] + ring[(k + 1) % 4]) * 0.5
            self.face(mat, p, [(0, 0), (0.2, 0), (0.2, 1), (0, 1)], tuple(mid))

    def gable(self, wall, roof, cx, cz, w, l, h, rise, yaw=0.0, over=0.6):
        """박공지붕 집. 용마루는 길이(l) 방향."""
        c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

        def P(x, y, z):
            return (cx + x * c - z * s, y, cz + x * s + z * c)

        self.walls(wall, self.rect(cx, cz, w, l, yaw), 0, h)
        tx, ty = TILE[wall]
        for sign in (1, -1):
            pts = [P(-w / 2, h, sign * l / 2), P(w / 2, h, sign * l / 2), P(0, h + rise, sign * l / 2)]
            self.face(wall, pts, [(0, h / ty), (w / tx, h / ty), (w / 2 / tx, (h + rise) / ty)], tuple((Vector(P(0, 0, sign)) - Vector(P(0, 0, 0)))))
        tx, ty = TILE[roof]
        rl = math.hypot(w / 2 + over, rise + over * rise / (w / 2))
        for sign in (1, -1):
            e = P(sign * (w / 2 + over), h - over * rise / (w / 2), -l / 2 - over)
            f = P(sign * (w / 2 + over), h - over * rise / (w / 2), l / 2 + over)
            g = P(0, h + rise, l / 2 + over)
            i = P(0, h + rise, -l / 2 - over)
            out = (Vector(P(sign, 1, 0)) - Vector(P(0, 0, 0)))
            self.face(roof, [e, f, g, i], [(0, 0), ((l + 2 * over) / tx, 0), ((l + 2 * over) / tx, rl / ty), (0, rl / ty)], tuple(out))

    def tree(self, x, z, y=0.0, h=None):
        h = h or self.R(9, 15)
        self.cyl("leaf", x, z, h * 0.34, 0.05, y + h * 0.15, y + h, 5)

    def tris(self):
        return sum(len(f[0]) - 2 for v in self.f.values() for f in v)

    def objects(self, scene, mats=None, offset=(0, 0, 0), yaw=0.0, prefix=""):
        """메시 만들기. Blender 축 = (x, z, y). mats가 없으면 재질 없이 만든다(내보내기용)."""
        c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        for mat, faces in self.f.items():
            verts, polys, uvs = [], [], []
            for pts, uv, out in faces:
                q = [Vector((p[0] * c - p[2] * s + offset[0], p[0] * s + p[2] * c + offset[2], p[1] + offset[1])) for p in pts]
                n = (q[1] - q[0]).cross(q[2] - q[0])
                o = Vector((out[0] * c - out[2] * s, out[0] * s + out[2] * c, out[1]))
                order = list(range(len(q)))
                if n.dot(o) < 0:
                    order.reverse()
                base = len(verts)
                verts += [q[i] for i in order]
                uvs += [uv[i] for i in order]
                polys.append(list(range(base, base + len(q))))
            name = prefix + mat
            me = bpy.data.meshes.new(name)
            me.from_pydata(verts, [], polys)
            layer = me.uv_layers.new(name="UVMap")
            for i, loop in enumerate(me.loops):
                layer.data[i].uv = uvs[loop.vertex_index]
            me.update()
            if mats:
                me.materials.append(mats[mat])
            scene.collection.objects.link(bpy.data.objects.new(name, me))


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
    return (noise(n, 8, seed) * 0.5 + noise(n, 32, seed + 1) * 0.3 + noise(n, 128, seed + 2) * 0.2)


def base(n, col, amount, seed):
    img = np.tile(np.array(col, dtype=np.float32)[None, None, :], (n, n, 1))
    return img * (1 + (fbm(n, seed) - 0.5)[..., None] * amount)


def windows(img, cols, rows, wx, wy, seed, mask, lit, frame=None):
    """칸마다 창 하나. wx, wy = 칸에서 창이 차지하는 비율. mask에는 켜진 창만 칠한다."""
    n = img.shape[0]
    rng = np.random.RandomState(seed)
    cw, ch = n / cols, n / rows
    for r in range(rows):
        for c in range(cols):
            x0, x1 = int((c + (1 - wx) / 2) * cw), int((c + (1 + wx) / 2) * cw)
            y0, y1 = int((r + (1 - wy) / 2) * ch), int((r + (1 + wy) / 2) * ch)
            v = rng.uniform(0.06, 0.2)
            if frame is not None:
                img[y0 - 3:y1 + 3, x0 - 3:x1 + 3] = frame
            img[y0:y1, x0:x1] = (v * 0.9, v, v * 1.2)
            img[y0 - 4:y0 - 1, x0:x1] *= 0.75
            if rng.rand() < lit:
                glow = rng.uniform(0.55, 1.0)
                warm = rng.uniform(0.0, 1.0)
                mask[y0:y1, x0:x1] = (glow, glow * (0.72 + warm * 0.2), glow * (0.38 + warm * 0.4))


def glass_texture(n, lit):
    """유리 커튼월: 칸 2m, 층 4m."""
    cols, floors = 32, 16
    img = base(n, (0.13, 0.19, 0.25), 0.2, 3)
    mask = np.zeros((n, n, 3), dtype=np.float32)
    rng = np.random.RandomState(4)
    cw, fh = n // cols, n // floors
    for f in range(floors):
        for c in range(cols):
            x0, y0 = c * cw, f * fh
            img[y0:y0 + fh, x0:x0 + cw] *= rng.uniform(0.75, 1.35)
            if rng.rand() < lit:
                g = rng.uniform(0.5, 1.0)
                mask[y0 + 8:y0 + fh - 8, x0 + 4:x0 + cw - 4] = (g, g * 0.86, g * 0.62)
    for k in range(cols):
        img[:, k * cw:k * cw + 5] = (0.42, 0.45, 0.48)
    for k in range(floors):
        img[k * fh:k * fh + 12] = (0.3, 0.33, 0.36)
    return img, mask


def container_texture(n):
    """색 띠 여섯. 띠 하나가 컨테이너 한 칸(긴 면 12m, 높이 2.6m)이다. U = 길이, V = 높이."""
    cols = ((0.6, 0.16, 0.12), (0.14, 0.3, 0.5), (0.72, 0.56, 0.16), (0.2, 0.42, 0.3), (0.72, 0.72, 0.7), (0.5, 0.24, 0.14))
    img = np.zeros((n, n, 3), dtype=np.float32)
    band = n // 6
    rail = n // 14
    post = band // 28
    for k, c in enumerate(cols):
        x0, x1 = k * band, (k + 1) * band
        img[:, x0:x1] = c
        img[:, x0:x1] *= 0.88 + 0.12 * np.sin(np.arange(band) / band * math.tau * 22)[None, :, None]    # 골판
        # 표기 자리: 글자 대신 밝은 판 하나
        img[n * 5 // 8:n * 13 // 16, x0 + band * 3 // 4:x0 + band * 9 // 10] = (0.86, 0.86, 0.82)
        img[:, x0:x0 + post] *= 0.55
        img[:, x1 - post:x1] *= 0.55
    img[:rail] *= 0.7
    img[-rail:] *= 0.7
    return img * (1 + (fbm(n, 66) - 0.5)[..., None] * 0.14)


def make_textures(out):
    os.makedirs(out, exist_ok=True)
    res = {}

    def save(name, img):
        n = img.shape[0]
        a = np.ones((n, n, 4), dtype=np.float32)
        a[..., :3] = np.clip(img, 0, 1)
        im = bpy.data.images.new("d_" + name, n, n, alpha=False)
        im.pixels.foreach_set(a.ravel())
        p = os.path.join(out, "ACO_Deco_" + name + ".png")
        im.filepath_raw = p
        im.file_format = 'PNG'
        im.save()
        res[name] = p
        bpy.data.images.remove(im)

    # 창 재질: 타일이 넓어 2048. 불 켜진 창은 전체의 16~18%
    big, n, small = 2048, 1024, 512
    img, mask = glass_texture(big, 0.16)
    save("glass", img)
    save("glass_e", mask)
    img = base(big, (0.6, 0.59, 0.56), 0.16, 7)
    mask = np.zeros((big, big, 3), dtype=np.float32)
    windows(img, 20, 16, 0.7, 0.55, 8, mask, 0.16)
    save("office", img)
    save("office_e", mask)
    img = base(big, (0.8, 0.77, 0.7), 0.14, 11)
    mask = np.zeros((big, big, 3), dtype=np.float32)
    windows(img, 16, 16, 0.5, 0.58, 12, mask, 0.18, (0.92, 0.9, 0.86))
    for k in range(16):
        y = int((k + 0.08) * big / 16)
        img[y:y + 7] *= 0.86
    save("plaster", img)
    save("plaster_e", mask)
    img = base(big, (0.42, 0.24, 0.18), 0.18, 15)
    for k in range(0, big, 5):
        img[k:k + 1] *= 0.8
    mask = np.zeros((big, big, 3), dtype=np.float32)
    windows(img, 16, 16, 0.48, 0.58, 16, mask, 0.18, (0.75, 0.72, 0.66))
    save("brick", img)
    save("brick_e", mask)

    img = base(n, (0.5, 0.53, 0.56), 0.16, 19)
    img *= (0.9 + 0.1 * np.sin(np.arange(n) / n * math.tau * 40))[None, :, None]
    for k in range(0, n, n // 3):
        img[k:k + 5] *= 0.6
    save("metal", img)
    save("roofflat", base(small, (0.34, 0.34, 0.33), 0.5, 23))
    img = base(small, (0.5, 0.21, 0.13), 0.28, 27)
    for k in range(0, small, small // 12):
        img[k:k + 12] *= 0.72
    save("rooftile", img)
    save("asphalt", base(small, (0.15, 0.15, 0.16), 0.5, 31))
    # 도로: U = 길이, V = 폭. 가운데 점선, 양쪽 실선
    img = base(n, (0.14, 0.14, 0.15), 0.5, 33)
    img[n // 2 - 6:n // 2 + 6, n // 8:n * 5 // 8] = (0.8, 0.78, 0.6)
    for y in (n // 12, n - n // 12):
        img[y - 4:y + 4, :] = (0.72, 0.72, 0.7)
    save("road", img)
    img = base(n, (0.58, 0.57, 0.54), 0.14, 37)
    for k in range(0, n, n // 4):
        img[k:k + 5] *= 0.72
        img[:, k:k + 5] *= 0.72
    save("pave", img)
    img = base(n, (0.68, 0.62, 0.5), 0.26, 41)
    for k in range(0, n, n // 6):
        img[k:k + 7] *= 0.66
        s = (k // (n // 6)) % 2 * n // 8
        for j in range(s, n + s, n // 4):
            img[k:k + n // 6, j % n:j % n + 6] *= 0.72
    save("stone", img)
    img = base(small, (0.55, 0.52, 0.27), 0.25, 45)
    img *= (0.86 + 0.14 * np.sin(np.arange(small) / small * math.tau * 16))[None, :, None]
    save("field", img)
    save("grass", base(small, (0.27, 0.38, 0.17), 0.5, 49))
    save("concrete", base(small, (0.6, 0.59, 0.56), 0.22, 53))
    save("white", base(small, (0.87, 0.87, 0.84), 0.08, 57))
    save("red", base(small, (0.66, 0.13, 0.1), 0.1, 59))
    save("steel", base(small, (0.42, 0.44, 0.47), 0.16, 61))
    save("copper", base(small, (0.3, 0.46, 0.4), 0.22, 63))
    save("leaf", base(small, (0.17, 0.3, 0.13), 0.5, 65))
    save("container", container_texture(n))
    # 활주로: 어두운 아스팔트 + 가운데 점선 + 가장자리 실선
    img = base(n, (0.11, 0.11, 0.12), 0.45, 67)
    img[n // 2 - 8:n // 2 + 8, :n // 2] = (0.8, 0.8, 0.78)
    for y in (24, n - 24):
        img[y - 8:y + 8, :] = (0.8, 0.8, 0.78)
    save("runway", img)
    return res


def hex_outline(r):
    return [(r * math.sin(k * math.tau / 6), r * math.cos(k * math.tau / 6)) for k in range(6)]


def rooftop(d, x, z, w, l, y):
    for k in range(d.rnd.randint(2, 4)):
        d.box("steel", x + d.R(-w, w) * 0.3, z + d.R(-l, l) * 0.3, d.R(3, 7), d.R(3, 7), y, y + d.R(1.5, 3.5), "steel")


def containers(d, x, z, size, y0=0.5):
    """컨테이너 야적. 간격을 고정해 서로 겹치지 않게 한다. y0 = 바닥 높이.
    한 칸마다 색을 고른다. 긴 면은 텍스처 띠 전체, 짧은 면은 같은 축척으로 앞부분, 윗면은 레일을 뺀 부분을 쓴다."""
    cols = max(int((size * 0.8 + 12) / 13.5), 1)
    rows = max(int((size * 0.4 + 2.6) / 3.4), 1)
    d.col(x, z, cols * 13.5, rows * 3.4, y0, y0 + 7.8)
    for r in range(rows):
        for c in range(cols):
            if d.rnd.random() < 0.25:
                continue
            px, pz = x + (c - (cols - 1) / 2) * 13.5, z + (r - (rows - 1) / 2) * 3.4
            outline = d.rect(px, pz, 12, 2.6)
            levels = d.rnd.randint(1, 3)
            u0 = u1 = 0.0
            top = y0 + levels * 2.6
            for level in range(levels):
                k = d.rnd.randint(0, 5)
                u0, u1 = (k + 0.01) / 6, (k + 0.99) / 6
                y1, y2 = y0 + level * 2.6, y0 + (level + 1) * 2.6
                for i in range(4):
                    a, b = outline[i], outline[(i + 1) % 4]
                    end = u1 if i % 2 == 0 else u0 + 0.21 / 6     # i가 짝수면 긴 면
                    d.face("container", [(a[0], y1, a[1]), (b[0], y1, b[1]), (b[0], y2, b[1]), (a[0], y2, a[1])],
                           [(u0, 0), (end, 0), (end, 1), (u0, 1)], ((a[0] + b[0]) / 2 - px, 0, (a[1] + b[1]) / 2 - pz))
            d.face("container", [(p[0], top, p[1]) for p in outline], [(u0, 0.1), (u1, 0.1), (u1, 0.5), (u0, 0.5)], (0, 1, 0))



# ── 도시 칸 ──
R = 400.0
A = R * 0.8660254
AX = 346.4102 / 4       # 칸 간격 692.8m의 1/8 (한 줄 건너 346.4m 어긋난다)
BZ = 100.0              # 칸 세로 간격 600m의 1/6
SLOPE = math.tan(math.radians(15.0))    # 지형 치마 경사
SLOPE_DEPTH = 224.0     # 비탈 큰길이 내려가는 수평 거리


def clip(poly, hull):
    """볼록 다각형 hull로 poly를 자른다."""
    out = poly
    n = len(hull)
    area = sum(hull[k][0] * hull[(k + 1) % n][1] - hull[(k + 1) % n][0] * hull[k][1] for k in range(n))
    s = 1 if area > 0 else -1
    for k in range(n):
        a, b = hull[k], hull[(k + 1) % n]
        side = lambda p: s * ((b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]))
        src, out = out, []
        for i in range(len(src)):
            p, q = src[i], src[(i + 1) % len(src)]
            sp, sq = side(p), side(q)
            if sp >= 0:
                out.append(p)
            if sp * sq < 0:
                t = sp / (sp - sq)
                out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
        if not out:
            return []
    return out


def area(poly):
    n = len(poly)
    return abs(sum(poly[k][0] * poly[(k + 1) % n][1] - poly[(k + 1) % n][0] * poly[k][1] for k in range(n))) * 0.5


def is_big_x(i):
    return i % 4 == 0


def is_big_z(j):
    return j % 3 == 0


def hw_x(i):
    return 10.0 if is_big_x(i) else 6.5


def hw_z(j):
    return 10.0 if is_big_z(j) else 6.5


def skeleton(d, hexa, edge):
    """바닥판과 도로. 판 옆면은 블록 바닥과 같은 재질이다. 낮은 이웃 쪽에서 판 옆면이 선처럼 보이지 않게 한다."""
    d.cap("asphalt", hexa, 0.4)
    d.walls(edge, hexa, -6, 0.4)
    for i in range(-5, 6):
        x, w = i * AX, hw_x(i)
        poly = clip([(x - w, -500), (x + w, -500), (x + w, 500), (x - w, 500)], hexa)
        if poly:
            d.face("road", [(p[0], 0.47 if is_big_x(i) else 0.46, p[1]) for p in poly], [(p[1] / 24, (p[0] - x + w) / (2 * w)) for p in poly], (0, 1, 0))
    tile = AX * 4 / 14
    for j in range(-4, 5):
        z, w = j * BZ, hw_z(j)
        poly = clip([(-500, z - w), (500, z - w), (500, z + w), (-500, z + w)], hexa)
        if poly:
            d.face("road", [(p[0], 0.45 if is_big_z(j) else 0.44, p[1]) for p in poly], [(p[0] / tile, (p[1] - z + w) / (2 * w)) for p in poly], (0, 1, 0))


def lots(d, hexa, mat):
    """길 사이 블록. 칸 경계에서 잘린 블록은 옆 칸의 나머지 반쪽과 만나 이어진다."""
    out = []
    for i in range(-5, 5):
        for j in range(-5, 4):
            x0, x1 = i * AX + hw_x(i) + 1, (i + 1) * AX - hw_x(i + 1) - 1
            z0, z1 = j * BZ + hw_z(j) + 1, (j + 1) * BZ - hw_z(j + 1) - 1
            rect = [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
            poly = clip(rect, hexa)
            if len(poly) < 3:
                continue
            d.cap(mat, poly, 0.5)
            full = area(poly) > area(rect) * 0.995
            out.append(((x0 + x1) / 2, (z0 + z1) / 2, x1 - x0, z1 - z0, full, area(poly) / area(rect), poly))
    return out


def pick_inside(hexa, x, z, w, l):
    """잘린 블록 안에 들어가는 건물 자리. 안쪽으로 당겨 본다."""
    d = math.hypot(x, z)
    for t in (0.0, 0.15, 0.3, 0.45):
        px, pz = x - x / d * w * t * 2, z - z / d * l * t * 2
        rect = [(px - w / 2, pz - l / 2), (px + w / 2, pz - l / 2), (px + w / 2, pz + l / 2), (px - w / 2, pz + l / 2)]
        if len(clip(rect, hexa)) == 4 and area(clip(rect, hexa)) > area(rect) * 0.999:
            return px, pz
    return None


def slope_roads(d, k):
    """k번 변에서 비탈을 따라 내려가는 큰길. y는 지형 치마 면 + 0.45m라서 평지 도로와 높이가 같게 시작한다."""
    a = math.radians(k * 60.0)
    nx, nz = math.cos(a), math.sin(a)
    tx, tz = -nz, nx
    half = R * 0.5
    e0 = (nx * A + tx * half, nz * A + tz * half)
    e1 = (nx * A - tx * half, nz * A - tz * half)
    up = (nx * SLOPE, 1, nz * SLOPE)

    def y(p):
        return 0.45 - ((p[0] * nx + p[1] * nz) - A) * SLOPE

    if k % 3 == 0:
        # ±X 변: 가로 큰길이 변을 곧게 가로질러 나간다
        band = [e0, e1, (e1[0] + nx * SLOPE_DEPTH, e1[1]), (e0[0] + nx * SLOPE_DEPTH, e0[1])]
        tile = AX * 4 / 14
        for j in range(-3, 4):
            if is_big_z(j):
                z, w = j * BZ, hw_z(j)
                poly = clip([(-1400, z - w), (1400, z - w), (1400, z + w), (-1400, z + w)], band)
                if len(poly) >= 3:
                    d.face("road", [(p[0], y(p), p[1]) for p in poly], [(p[0] / tile, (p[1] - z + w) / (2 * w)) for p in poly], up)
        return
    # 빗변: 세로 큰길. 비탈 면 전체(치마 사다리꼴)로 잘라 칸 모서리의 큰길도 온 폭이 된다
    grow = 1 + SLOPE_DEPTH / A
    band = [e0, e1, (e1[0] * grow, e1[1] * grow), (e0[0] * grow, e0[1] * grow)]
    for i in range(-8, 9):
        if is_big_x(i):
            x, w = i * AX, hw_x(i)
            poly = clip([(x - w, -1400), (x + w, -1400), (x + w, 1400), (x - w, 1400)], band)
            if len(poly) >= 3:
                d.face("road", [(p[0], y(p), p[1]) for p in poly], [(p[1] / 24, (p[0] - x + w) / (2 * w)) for p in poly], up)


def tower(d, x, z, w, l, h, kind):
    """고층: 저층부 + 몸통 + 옥상 설비."""
    pod = d.R(10, 16)
    d.box("office", x, z, w * 1.2, l * 1.2, 0.5, pod)
    d.col(x, z, w * 1.2, l * 1.2, 0.5, pod)
    shaft = "glass" if d.rnd.random() < 0.8 else "office"
    if kind == 0:
        d.prism(shaft, d.rect(x, z, w, l), pod, h * 0.72)
        d.prism(shaft, d.rect(x, z, w * 0.74, l * 0.74), h * 0.72, h)
        d.col(x, z, w, l, pod, h * 0.72)
        d.col(x, z, w * 0.74, l * 0.74, h * 0.72, h)
    elif kind == 1:
        d.prism(shaft, d.octagon(x, z, w, l, min(w, l) * 0.2), pod, h)
        d.col(x, z, w * 0.95, l * 0.95, pod, h)
    else:
        d.loft(shaft, [(d.circle(x, z, w * 0.52, 16), pod), (d.circle(x, z, w * 0.46, 16), h)], "roofflat")
        d.col(x, z, w * 0.9, w * 0.9, pod, h)
    d.box("metal", x, z, w * 0.34, l * 0.34, h, h + 7, "steel")
    if h > 110:
        d.cyl("steel", x, z, 0.8, 0.2, h + 7, h + 26, 5)


def slab(d, mat, x, z, w, l, h):
    """네모난 건물 + 옥상 설비 + 콜라이더."""
    d.box(mat, x, z, w, l, 0.5, h)
    rooftop(d, x, z, w * 0.92, l * 0.92, h)
    d.col(x, z, w, l, 0.5, h)


def tank(d, x, z, r, h):
    d.cyl("white", x, z, r, r, 0.5, h, 14, "white")
    d.cyl("white", x, z, r, r * 0.25, h, h + r * 0.3, 14)
    d.col(x, z, r * 1.8, r * 1.8, 0.5, h + r * 0.15)


def downtown():
    d = D(121)
    hexa = hex_outline(R)
    skeleton(d, hexa, "pave")
    for x, z, w, l, full, frac, poly in lots(d, hexa, "pave"):
        dist = math.hypot(x, z) / 350
        h = max(15, max(1 - dist, 0.0) ** 1.25 * 215 * d.R(0.6, 1.15))
        if full:
            if h > 62:
                tower(d, x, z, w * 0.62, l * 0.56, h, d.rnd.randint(0, 2))
            else:
                for sx in (-1, 1):
                    slab(d, d.rnd.choice(("office", "brick", "plaster")), x + sx * w * 0.25, z, w * 0.44, l * 0.84, h * d.R(0.7, 1.1))
        elif frac > 0.25:
            spot = pick_inside(hexa, x, z, w * 0.46, l * 0.46)
            if spot:
                slab(d, d.rnd.choice(("office", "brick", "plaster")), spot[0], spot[1], w * 0.46, l * 0.46, d.R(12, 26))
    return d


def residential():
    d = D(232)
    hexa = hex_outline(R)
    skeleton(d, hexa, "grass")
    for x, z, w, l, full, frac, poly in lots(d, hexa, "grass"):
        r = d.rnd.random()
        if full and r < 0.16:
            for k in range(8):
                d.tree(x + d.R(-0.42, 0.42) * w, z + d.R(-0.42, 0.42) * l, 0.5)
        elif full and r < 0.5:
            m = d.rnd.choice(("plaster", "brick"))
            for sz in (-1, 1):
                slab(d, m, x, z + sz * l * 0.24, w * 0.84, l * 0.3, d.R(20, 38))
        else:
            for rr in (-1, 1):
                xs = []
                pz = z + rr * l * 0.24
                for c in range(4):
                    px = x + (c - 1.5) * w * 0.24
                    if full or pick_inside(hexa, px, pz, 14, 18) == (px, pz):
                        d.gable(d.rnd.choice(("plaster", "plaster", "brick")), "rooftile", px, pz, w * 0.18, l * 0.32, d.R(6, 9), 3.4)
                        xs.append(px)
                if xs:
                    # 집 한 줄을 상자 하나로 막는다. 칸마다 콜라이더가 수백 개가 되지 않게 한다
                    d.col((xs[0] + xs[-1]) * 0.5, pz, xs[-1] - xs[0] + w * 0.18, l * 0.32, 0.5, 10.5)
    return d


def industrial():
    d = D(343)
    hexa = hex_outline(R)
    skeleton(d, hexa, "concrete")
    for x, z, w, l, full, frac, poly in lots(d, hexa, "concrete"):
        if not full:
            spot = pick_inside(hexa, x, z, w * 0.42, l * 0.42) if frac > 0.25 else None
            if spot:
                d.cyl("white", spot[0], spot[1], w * 0.2, w * 0.2, 0.5, 11, 12, "white")
                d.col(spot[0], spot[1], w * 0.36, w * 0.36, 0.5, 11)
            continue
        r = d.rnd.random()
        if r < 0.45:
            h = d.R(11, 15)
            d.gable("metal", "roofflat", x, z, w * 0.82, l * 0.82, h, 5)
            d.col(x, z, w * 0.82, l * 0.82, 0.5, h + 3)
        elif r < 0.68:
            for sx in (-1, 1):
                for sz in (-1, 1):
                    tank(d, x + sx * w * 0.24, z + sz * l * 0.24, d.R(12, 16), d.R(12, 20))
        elif r < 0.86:
            d.box("brick", x - w * 0.14, z, w * 0.56, l * 0.8, 0.5, 18)
            d.col(x - w * 0.14, z, w * 0.56, l * 0.8, 0.5, 18)
            for s in (-1, 1):
                d.cyl("concrete", x + w * 0.3, z + s * l * 0.22, 5.2, 3.2, 0.5, 58, 10)
                d.cyl("red", x + w * 0.3, z + s * l * 0.22, 3.4, 3.2, 50, 58, 10)
                d.col(x + w * 0.3, z + s * l * 0.22, 7, 7, 0.5, 58)
        else:
            containers(d, x, z, w * 0.82)
    return d


def city(builder):
    d = builder()
    for k in range(6):
        slope_roads(d, k)
    return d


def lighthouse():
    d = D(404)
    d.col(0, 0, 25, 25, 0, 3.6)
    d.col(0, 0, 10, 10, 3.6, 45)
    d.col(0, 0, 11, 11, 45, 55)
    d.col(-17, -14, 8, 14, 0, 7, 30)
    d.col(-7, -20, 6, 9, 0, 5.5, -60)
    d.loft("stone", [(d.circle(0, 0, 14, 16), 0), (d.circle(0, 0, 11, 16), 3.6)], "stone")
    bands = ((3.6, 13), (13, 19), (19, 30), (30, 36), (36, 44))
    r = lambda y: 6.4 - (y - 3.6) * 0.058
    for k, (y0, y1) in enumerate(bands):
        d.loft("red" if k % 2 else "white", [(d.circle(0, 0, r(y0), 16), y0), (d.circle(0, 0, r(y1), 16), y1)])
    d.loft("steel", [(d.circle(0, 0, 3.9, 16), 44), (d.circle(0, 0, 6.4, 16), 45.2), (d.circle(0, 0, 6.4, 16), 45.8)], "steel")
    d.loft("steel", [(d.circle(0, 0, 6.2, 16), 45.8), (d.circle(0, 0, 6.2, 16), 47.0)])
    for k in range(12):
        a = k * math.tau / 12
        d.strut("steel", (3.3 * math.cos(a), 45.8, 3.3 * math.sin(a)), (3.3 * math.cos(a), 51, 3.3 * math.sin(a)), 0.22)
    d.cyl("glow", 0, 0, 1.6, 1.6, 46.4, 50, 8)
    d.cyl("lamp", 0, 0, 3.3, 3.3, 45.8, 51, 12)
    d.loft("copper", [(d.circle(0, 0, 3.9, 12), 51), (d.circle(0, 0, 2.2, 12), 53), (d.circle(0, 0, 0.4, 12), 55)])
    d.cyl("steel", 0, 0, 0.12, 0.05, 55, 58, 5)
    d.gable("plaster", "rooftile", -17, -14, 8, 14, 5, 3, 30)
    d.gable("plaster", "rooftile", -7, -20, 6, 9, 4, 2.4, -60)
    for k in range(5):
        d.tree(24 * math.cos(k * 1.3 + 2.4), 24 * math.sin(k * 1.3 + 2.4))
    return d


def radar():
    d = D(505)
    d.cap("concrete", d.circle(0, 0, 58, 20), 0.15)
    d.col(0, 0, 40, 26, 0, 11)
    d.col(0, 0, 21, 21, 11, 20)
    d.col(0, 0, 30, 30, 20, 47)
    d.col(-33, -8, 18, 14, 0, 7.5)
    d.col(31, 12, 12, 10, 0, 6)
    d.col(26, -20, 4, 4, 0, 40)
    d.col(-34, 26, 13, 13, 0, 7.6)
    d.box("concrete", 0, 0, 40, 26, 0, 11, "roofflat")
    d.prism("concrete", d.octagon(0, 0, 22, 22, 5), 11, 20, "roofflat")
    # 레이더 돔: 격자가 보이게 면을 나눈다
    d.loft("white", [(d.circle(0, 0, 11.5, 14), 20), (d.circle(0, 0, 16.4, 14), 26), (d.circle(0, 0, 17.6, 14), 32.5), (d.circle(0, 0, 16.4, 14), 39), (d.circle(0, 0, 12.2, 14), 44.5), (d.circle(0, 0, 6.2, 14), 48.4), (d.circle(0, 0, 0.3, 14), 49.6)])
    d.box("metal", -33, -8, 18, 14, 0, 7.5, "roofflat")
    d.box("metal", 31, 12, 12, 10, 0, 6, "roofflat")
    rooftop(d, 12, 6, 12, 10, 11)
    # 전파탑
    for s in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        d.strut("steel", (26 + s[0] * 2.4, 0, -20 + s[1] * 2.4), (26 + s[0] * 0.5, 34, -20 + s[1] * 0.5), 0.5)
    for k in range(5):
        y0, y1 = k * 7, (k + 1) * 7
        w0, w1 = 2.4 - y0 * 0.056, 2.4 - y1 * 0.056
        d.strut("steel", (26 - w0, y0, -20 - w0), (26 + w1, y1, -20 - w1), 0.26)
        d.strut("steel", (26 + w0, y0, -20 - w0), (26 + w1, y1, -20 + w1), 0.26)
    d.cyl("lamp", 26, -20, 0.5, 0.1, 34, 40, 6)
    # 접시 안테나
    d.loft("white", [(d.circle(-34, 26, 0.5, 12), 3), (d.circle(-34, 26, 5.5, 12), 5.6), (d.circle(-34, 26, 8, 12), 7.6)])
    d.cyl("concrete", -34, 26, 1.6, 1.0, 0, 3.2, 8)
    # 울타리 기둥
    for k in range(24):
        a = k * math.tau / 24
        d.strut("steel", (54 * math.cos(a), 0, 54 * math.sin(a)), (54 * math.cos(a), 3, 54 * math.sin(a)), 0.2)
    return d


def comm_tower():
    d = D(606)
    d.cap("concrete", d.circle(0, 0, 26, 14), 0.15)
    d.col(14, 8, 10, 8, 0, 4.4)
    d.col(14, -10, 6, 5, 0, 3)
    for y0, y1, side in ((0, 50, 10), (50, 100, 7), (100, 150, 3.5), (150, 164, 1)):
        d.col(0, 0, side, side, y0, y1)
    d.box("concrete", 14, 8, 10, 8, 0, 4.4, "roofflat")
    d.box("metal", 14, -10, 6, 5, 0, 3, "roofflat")
    H = 150.0
    w = lambda y: 7.5 * (1 - y / (H * 1.04)) + 0.7
    pts = [(math.cos(a), math.sin(a)) for a in (0.5236, 2.618, 4.712)]
    sect = 10
    for k in range(sect):
        y0, y1 = k * H / sect, (k + 1) * H / sect
        mat = "red" if k % 2 else "white"
        for i in range(3):
            a, b = pts[i], pts[(i + 1) % 3]
            d.strut(mat, (a[0] * w(y0), y0, a[1] * w(y0)), (a[0] * w(y1), y1, a[1] * w(y1)), 0.7)
            d.strut(mat, (a[0] * w(y0), y0, a[1] * w(y0)), (b[0] * w(y1), y1, b[1] * w(y1)), 0.28)
            d.strut(mat, (b[0] * w(y0), y0, b[1] * w(y0)), (a[0] * w(y1), y1, a[1] * w(y1)), 0.28)
            d.strut(mat, (a[0] * w(y1), y1, a[1] * w(y1)), (b[0] * w(y1), y1, b[1] * w(y1)), 0.28)
    for y, r in ((60, 7.6), (100, 5.4), (135, 3.6)):
        d.loft("steel", [(d.circle(0, 0, r, 10), y), (d.circle(0, 0, r, 10), y + 1.2)], "steel")
    for y, a, r in ((94, 0.4, 3.2), (112, 2.6, 2.6), (126, 4.6, 2.2)):
        c, s = math.cos(a), math.sin(a)
        rings = []
        for k in range(4):
            t = k / 3
            rad = r * (0.15 + 0.85 * math.sqrt(t))
            center = 4 + t * 1.2
            ring = []
            for j in range(12):
                b = j * math.tau / 12
                px = center * c + rad * math.cos(b) * -s
                pz = center * s + rad * math.cos(b) * c
                ring.append((px, pz, y + rad * math.sin(b)))
            rings.append(ring)
        for ra, rb in zip(rings, rings[1:]):
            for j in range(12):
                m = (j + 1) % 12
                d.face("white", [ra[j][:1] + (ra[j][2],) + ra[j][1:2], ra[m][:1] + (ra[m][2],) + ra[m][1:2], rb[m][:1] + (rb[m][2],) + rb[m][1:2], rb[j][:1] + (rb[j][2],) + rb[j][1:2]], [(0, 0), (1, 0), (1, 1), (0, 1)], (c, 0, s))
    d.cyl("steel", 0, 0, 0.4, 0.1, H, H + 14, 6)
    d.cyl("glow", 0, 0, 0.7, 0.7, H + 14, H + 15, 6)
    for a in (0.5236, 2.618, 4.712):
        c, s = math.cos(a), math.sin(a)
        for y, r in ((55, 58), (110, 84)):
            d.strut("steel", (c * w(y), y, s * w(y)), (c * r, 0, s * r), 0.16)
        d.box("concrete", c * 84, s * 84, 4, 4, 0, 1.6, "concrete")
        d.box("concrete", c * 58, s * 58, 3, 3, 0, 1.2, "concrete")
    return d


def fluted(d, x, z, y0, y1, r):
    d.loft("stone", [(d.circle(x, z, r * 1.32, 10), y0), (d.circle(x, z, r * 1.32, 10), y0 + 1.2), (d.circle(x, z, r, 10), y0 + 2)])
    d.loft("stone", [(d.circle(x, z, r, 10), y0 + 2), (d.circle(x, z, r * 0.88, 10), y1)], "stone")


def ruins():
    d = D(707)
    d.col(0, 0, 130, 130, 0, 4.2)
    d.col(0, 0, 8, 8, 4.2, 40)
    d.col(0, 0, 5, 5, 40, 77)
    for k, (r, y0, y1) in enumerate(((74, 0, 1.4), (68, 1.4, 2.8), (62, 2.8, 4.2))):
        d.prism("stone", d.circle(0, 0, r, 28), y0, y1, "stone")
    n = 14
    pos = []
    for k in range(n):
        a = k / n * math.tau
        x, z = 50 * math.cos(a), 50 * math.sin(a)
        full = d.rnd.random() > 0.32
        h = 40 if full else d.R(9, 22)
        fluted(d, x, z, 4.2, 4.2 + h, 3.5)
        d.col(x, z, 6.2, 6.2, 4.2, 4.2 + h)
        if full:
            d.loft("stone", [(d.circle(x, z, 3.2, 10), 44.2), (d.circle(x, z, 4.4, 10), 45.4)], None)
            d.box("stone", x, z, 8.8, 8.8, 45.4, 46.8, "stone")
        pos.append((x, z, full))
    for k in range(0, n, 2):      # 두 기둥씩 짝지어 얹는다. 이어지면 머리 위에서 면이 겹친다
        a, b = pos[k], pos[(k + 1) % n]
        if a[2] and b[2]:
            yaw = math.degrees(math.atan2(b[0] - a[0], b[1] - a[1]))
            L = math.hypot(b[0] - a[0], b[1] - a[1]) + 7
            d.box("stone", (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, 5, L, 46.9, 50, "stone", -yaw)
            d.col((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, 5, L, 46.9, 50, -yaw)
    d.loft("stone", [(d.rect(0, 0, 13, 13), 4.2), (d.rect(0, 0, 11, 11), 7), (d.rect(0, 0, 8.4, 8.4), 8), (d.rect(0, 0, 4.2, 4.2), 72), (d.rect(0, 0, 0.3, 0.3), 77)])
    for k in range(4):
        a = d.R(0, math.tau)
        x, z = 62 * math.cos(a), 62 * math.sin(a)
        c, s = math.cos(a + 1.2), math.sin(a + 1.2)
        for j in range(3):
            d.cyl("stone", x + c * j * 7, z + s * j * 7, d.R(3.2, 3.6), 3.2, 4.2, 4.2 + d.R(2, 4), 10)
    return d


def gantry(d, x, zc):
    """부두 컨테이너 크레인. 문형 틀 안(폭 23m, 높이 30m)으로 앞뒤, 옆(폭 27m)으로 기체가 지나갈 수 있다.
    콜라이더는 부재마다 따로 있어 틀 사이가 비어 있다. 호이스트 줄에는 충돌이 없다."""
    hx = 13.0
    z0, z1 = zc - 15.0, zc + 15.0
    deck, bot, top = 3.5, 34.0, 36.5
    # 다리 네 개와 레일 위 바퀴 대차
    for sx in (-1, 1):
        px = x + sx * hx
        for z in (z0, z1):
            d.box("steel", px, z, 7, 3.4, deck, 5.5, "steel")
            d.col(px, z, 7, 3.4, deck, 5.5)
            d.loft("red", [(d.rect(px, z, 3.4, 3.4), 5.5), (d.rect(px, z, 2.4, 2.4), bot)])
            d.col(px, z, 3, 3, 5.5, bot)
            inward = 7.0 if z == z0 else -7.0
            d.beam("red", (px, 27, z), (px - sx * 7, bot, z), 1.1)
            d.beam("red", (px, 27, z), (px, bot, z + inward), 1.1)
    # 문형 보
    for z in (z0, z1):
        # 끝이 세로보와 겹치면 같은 높이의 면이 깜빡이므로 세로보 안쪽에서 끝낸다
        d.box("red", x, z, hx * 2 - 2.6, 2.6, bot, top, "red")
        d.col(x, z, hx * 2 - 2.6, 2.6, bot, top)
    for sx in (-1, 1):
        d.box("red", x + sx * hx, zc, 2.6, 32.6, bot, top, "red")
        d.col(x + sx * hx, zc, 2.6, 32.6, bot, top)
    # 전기실
    d.box("metal", x - 9.2, zc, 5.4, 12, top, 39.8, "roofflat")
    d.col(x - 9.2, zc, 5.4, 12, top, 39.8)
    # 붐: 양옆 트러스 + 윗면 가로대. 끝 24m는 높이가 줄어든다
    back, tip = zc - 42.0, z1 + 58.0
    low, high, end = 40.5, 46.5, 43.0
    neck = tip - 24.0

    def chord(z):
        return high if z <= neck else high + (end - high) * (z - neck) / (tip - neck)

    bays = 13
    step = (tip - back) / bays
    for sx in (-1, 1):
        bx = x + sx * 4.2
        d.strut("white", (bx, low, back), (bx, low, tip), 1.5)
        d.strut("white", (bx, high, back), (bx, high, neck), 1.3)
        d.strut("white", (bx, high, neck), (bx, end, tip), 1.3)
        for k in range(bays + 1):
            z = back + k * step
            d.strut("white", (bx, low, z), (bx, chord(z), z), 0.55)
            if k < bays:
                z2 = z + step
                a, b = ((bx, low, z), (bx, chord(z2), z2)) if k % 2 else ((bx, chord(z), z), (bx, low, z2))
                d.strut("white", a, b, 0.45)
    for k in range(bays + 1):
        z = back + k * step
        d.strut("white", (x - 4.2, chord(z), z), (x + 4.2, chord(z), z), 0.5)
    for z in (back, tip):
        d.strut("white", (x - 4.2, low, z), (x + 4.2, low, z), 0.8)
    d.col(x, (back + neck) * 0.5, 10, neck - back, low - 0.8, high + 0.7)
    d.col(x, (neck + tip) * 0.5, 10, tip - neck, low - 0.8, (high + end) * 0.5 + 0.7)
    # 꼭대기 틀: 앞뒤 두 쌍의 기둥이 문형 보에서 모인다
    apex, peak = 74.0, 5.5
    for sx in (-1, 1):
        for sz in (-1, 1):
            d.beam("red", (x + sx * 12, top, zc + sz * 15), (x + sx * peak, apex, zc + sz * 1.5), 1.7)
    d.box("red", x, zc, peak * 2 + 1.7, 4.2, apex - 1, apex + 1.5, "red")
    d.col(x, zc, peak * 2 + 1.7, 4.2, apex - 1, apex + 1.5)
    # 꼭대기 틀 가로대(붐보다 충분히 위)
    y = 56.0
    t = (y - top) / (apex - top)
    bx, bz = 12 + (peak - 12) * t, 15 + (1.5 - 15) * t
    for sz in (-1, 1):
        d.beam("red", (x - bx, y, zc + sz * bz), (x + bx, y, zc + sz * bz), 0.9)
    for sx in (-1, 1):
        d.beam("red", (x + sx * bx, y, zc - bz), (x + sx * bx, y, zc + bz), 0.9)
    # 붐을 잡아 주는 강봉
    for sx in (-1, 1):
        fx, gx = x + sx * peak, x + sx * 4.2
        d.beam("steel", (fx, apex, zc + 1.5), (gx, high, z1 + 22), 0.45, 1.0)
        d.beam("steel", (fx, apex, zc + 1.5), (gx, chord(tip - 8), tip - 8), 0.45, 1.0)
        d.beam("steel", (fx, apex, zc - 1.5), (gx, high, back + 6), 0.45, 1.0)
    # 뒤쪽 기계실
    d.box("white", x, back + 11, 12, 17, high + 0.6, 53, "roofflat")
    d.box("metal", x + 2, back + 6, 3, 3, 53, 54.6, "steel")
    d.col(x, back + 11, 12, 17, high + 0.6, 53)
    # 트롤리, 운전실, 스프레더
    t = z1 + 38.0
    d.box("steel", x, t, 6.2, 7.5, low - 2.2, low + 0.6, "steel")
    d.box("white", x - 1.6, t + 1, 3, 3.8, low - 5.6, low - 2.2, "white")
    d.box("lamp", x - 1.6, t + 2.95, 2.6, 0.1, low - 5.2, low - 2.8, "lamp")
    d.col(x, t, 6.2, 7.5, low - 5.6, low + 0.6)
    for sx in (-1, 1):
        for sz in (-1, 1):
            d.strut("steel", (x + sx * 2.2, low - 2.2, t + sz * 3.2), (x + sx * 1, 24, t + sz * 5), 0.16)
    d.box("metal", x, t, 2.6, 12.4, 22.4, 24, "metal")
    d.col(x, t, 2.6, 12.4, 22.4, 24)


def port():
    d = D(808)
    d.box("concrete", 0, 40, 320, 80, -8, 3.5, "pave")
    d.box("concrete", 70, 200, 30, 240, -8, 3, "pave")
    d.box("concrete", -90, 150, 25, 140, -8, 3, "pave")
    d.col(0, 40, 320, 80, -8, 3.5)
    d.col(70, 200, 30, 240, -8, 3)
    d.col(-90, 150, 25, 140, -8, 3)
    for x, z, w, l, h in ((-105, 20, 62, 34, 14), (105, 20, 62, 34, 14), (0, 12, 54, 26, 11.5)):
        d.col(x, z, w, l, 3.5, h)
    for k, z in enumerate((60, 120, 180, 240, 300)):
        for sx in (-1, 1):
            d.cyl("steel", 70 + sx * 14, z + 18, 0.7, 0.7, 3, 4.2, 6)
    gantry(d, -36, 61)
    gantry(d, 36, 61)
    # 크레인 레일
    for z in (46, 76):
        d.cap("steel", d.rect(0, z, 300, 1.2), 3.53)
    d.gable("metal", "roofflat", -105, 20, 34, 62, 11, 5, 90, 0.8)
    d.gable("metal", "roofflat", 105, 20, 34, 62, 11, 5, 90, 0.8)
    d.gable("metal", "roofflat", 0, 12, 26, 54, 9, 4, 90, 0.8)
    for sx, sz in ((130, 62), (-140, 60)):
        d.cyl("white", sx, sz, 11, 11, 3.5, 17, 14, "white")
        d.col(sx, sz, 20, 20, 3.5, 17)
    containers(d, 0, 60, 36, 3.5)
    containers(d, -76, 60, 40, 3.5)
    containers(d, 84, 60, 56, 3.5)
    containers(d, 70, 170, 18, 3.0)
    return d


def airfield():
    d = D(909)
    tx, ty = 60.0, 30.0
    L = 1200.0
    d.face("runway", [(-30, 0.5, -L), (30, 0.5, -L), (30, 0.5, L), (-30, 0.5, L)], [(0, 0), (0, 1), (L * 2 / tx, 1), (L * 2 / tx, 0)], (0, 1, 0))
    for end in (-1, 1):
        for k in range(8):
            x = -24 + k * 6.8
            z = end * (L - 30)
            d.face("white", [(x - 1.5, 0.6, z - 20), (x + 1.5, 0.6, z - 20), (x + 1.5, 0.6, z + 20), (x - 1.5, 0.6, z + 20)], [(0, 0), (1, 0), (1, 1), (0, 1)], (0, 1, 0))
        for k in range(6):
            z = end * (L - 240 - k * 60)
            for sx in (-1, 1):
                x = sx * 16
                d.face("white", [(x - 5, 0.6, z - 14), (x + 5, 0.6, z - 14), (x + 5, 0.6, z + 14), (x - 5, 0.6, z + 14)], [(0, 0), (1, 0), (1, 1), (0, 1)], (0, 1, 0))
    d.col(0, 0, 60, 2400, 0, 0.5)
    d.cap("asphalt", d.rect(150, 0, 25, 1800), 0.4)
    d.col(150, 0, 25, 1800, 0, 0.4)
    for k in (-1, 0, 1):
        # 유도로와 겹치면 같은 높이의 면이 깜빡이므로 유도로 가장자리에서 끝낸다
        d.cap("asphalt", d.rect(83.75, k * 700, 107.5, 25), 0.4)
        d.col(83.75, k * 700, 107.5, 25, 0, 0.4)
    d.cap("concrete", d.rect(240, 346, 160, 260), 0.45)
    d.col(240, 346, 160, 260, 0, 0.45)
    # 아치 격납고
    for k in (-1, 0, 1):
        z = 346 + k * 72
        d.col(290, z, 50, 56, 0.4, 18)
        n = 8
        rings = []
        for j in range(n + 1):
            a = j / n * math.pi
            rings.append((26 * math.cos(a), 18 * math.sin(a)))
        for a, b in zip(rings, rings[1:]):
            d.face("metal", [(290 + a[0], 0.4 + a[1], z - 28), (290 + b[0], 0.4 + b[1], z - 28), (290 + b[0], 0.4 + b[1], z + 28), (290 + a[0], 0.4 + a[1], z + 28)], [(0, 0), (1, 0), (1, 2), (0, 2)], (a[0] + b[0], a[1] + b[1], 0))
        d.face("metal", [(290 + p[0], 0.4 + p[1], z + 28) for p in rings], [(p[0] / 20, p[1] / 20) for p in rings], (0, 0, 1))
        d.face("steel", [(290 + p[0], 0.4 + p[1], z - 28) for p in rings], [(p[0] / 10, p[1] / 10) for p in rings], (0, 0, -1))
    # 관제탑
    d.prism("concrete", d.octagon(200, 200, 14, 14, 3), 0, 34, None)
    d.loft("lamp", [(d.octagon(200, 200, 15, 15, 3.4), 34), (d.octagon(200, 200, 20, 20, 4.6), 41)], None)
    d.prism("steel", d.octagon(200, 200, 21.5, 21.5, 5), 41, 42.2, "steel")
    d.cyl("steel", 200, 200, 0.4, 0.1, 42.2, 50, 5)
    d.box("office", 232, 214, 44, 22, 0.4, 13, "roofflat")
    d.col(232, 214, 44, 22, 0.4, 13)
    d.col(200, 200, 14, 14, 0, 34)
    d.col(200, 200, 20, 20, 34, 42)
    for k in range(3):
        d.cyl("white", 230, 480 + k * 25, 9, 9, 0, 11, 14, "white")
        d.col(230, 480 + k * 25, 16, 16, 0, 11)
    return d


def village(seed, houses):
    """집은 교회 둘레에 흩어 놓는다. 마을에는 도로가 없다."""
    d = D(seed)
    d.cap("stone", d.rect(0, 0, 40, 40), 0.15)
    d.gable("stone", "rooftile", 0, 0, 16, 30, 12, 6)
    d.col(0, 0, 16, 30, 0, 15)
    d.col(0, 19, 9, 9, 0, 30)
    d.col(0, 19, 4, 4, 30, 46)
    d.box("stone", 0, 19, 9, 9, 0, 30, "stone")
    d.loft("copper", [(d.rect(0, 19, 9, 9), 30), (d.rect(0, 19, 0.4, 0.4), 46)])
    d.cyl("steel", 0, 19, 0.15, 0.08, 46, 49, 4)
    placed = [(0, 0)]
    for k in range(80):
        a = d.R(0, math.tau)
        r = math.sqrt(d.rnd.random()) * 170 + 34
        x, z = r * math.cos(a), r * math.sin(a)
        if any(math.hypot(x - p, z - q) < 30 for p, q in placed) or len(placed) > houses:
            continue
        placed.append((x, z))
        w, l, h, rise, yaw = d.R(9, 12), d.R(12, 16), d.R(5, 6.5), d.R(3, 4), d.R(0, 180)
        d.gable(d.rnd.choice(("plaster", "plaster", "brick")), "rooftile", x, z, w, l, h, rise, yaw)
        d.col(x, z, w, l, 0, h + rise * 0.6, yaw)
        if d.rnd.random() < 0.5:
            d.tree(x + d.R(-10, 10), z + d.R(10, 16))
    for f in range(7):
        a = f / 7 * math.tau + 0.3
        r = d.R(240, 285)
        d.cap(d.rnd.choice(("field", "field", "grass")), d.rect(r * math.cos(a), r * math.sin(a), d.R(70, 100), d.R(46, 70), math.degrees(a)), 0.25)
    return d


# ── 내보내기 ──
PREFABS = (
    ("ACO_CityBlock_Downtown", lambda: city(downtown)), ("ACO_CityBlock_Residential", lambda: city(residential)), ("ACO_CityBlock_Industrial", lambda: city(industrial)),
    ("ACO_Village_A", lambda: village(1010, 14)), ("ACO_Village_B", lambda: village(1021, 18)), ("ACO_Port", port), ("ACO_Airfield", airfield),
    ("ACO_Lighthouse", lighthouse), ("ACO_Radar", radar), ("ACO_CommTower", comm_tower), ("ACO_Ruins", ruins),
)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    os.makedirs(MODEL_DIR, exist_ok=True)
    make_textures(TEXTURE_DIR)
    lines = []
    for name, build in PREFABS:
        d = build()
        print("PREFAB", name, "tris", d.tris(), "colliders", len(d.colliders) + len(d.struts))
        d.objects(scene, None, (0, 0, 0), 180.0, name + "__")
        for c in d.colliders:
            lines.append(name + " " + " ".join("%.3f" % v for v in c))
        for a, b, w in d.struts:
            lines.append(name + " strut " + " ".join("%.3f" % v for v in tuple(a) + tuple(b) + (w,)))
    with open(os.path.join(MODEL_DIR, COLLIDER_NAME), "w", encoding="utf8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    bpy.ops.export_scene.fbx(filepath=os.path.join(MODEL_DIR, FBX_NAME), object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_triangles=True, path_mode='RELATIVE', add_leaf_bones=False, bake_anim=False)
    print("DONE")


if __name__ == "__main__":
    main()
