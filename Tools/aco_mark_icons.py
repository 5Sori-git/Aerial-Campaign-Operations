# -*- coding: utf-8 -*-
"""킬마크 아이콘 아틀라스: python aco_mark_icons.py [출력 png]
칸 3개(가로로 1, 10, 100 단위): 전투기 / 전투기 3기 편대 / 날개 달린 별. 알파는 거리값(0.5 = 경계)이라 셰이더 ACO/MarkText가 글자처럼 또렷하게 그린다."""
import math, os, struct, sys, zlib

CELL = 128
SPREAD = 14.0   # 경계에서 이 픽셀까지 알파가 0 또는 1이 된다
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'Assets', 'ACO_Addon', 'Textures', 'Marks', 'ACO_KillMarks.png')


def poly_dist(px, py, pts):
    """다각형까지 거리. 안쪽이 양수."""
    best = 1e9
    inside = False
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        dx, dy = bx - ax, by - ay
        t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
        best = min(best, math.hypot(px - ax - dx * t, py - ay - dy * t))
        if (ay > py) != (by > py) and px < ax + (py - ay) / (by - ay) * dx:
            inside = not inside
    return best if inside else -best


# 도형은 위가 +Y, 칸 가운데가 원점인 -50~50 좌표. SCALE을 곱해 칸을 채운다
SCALE = 1.15


def shape(pts):
    return [(x * SCALE, y * SCALE) for x, y in pts]


JET = shape([(0, 46), (7, 20), (8, 6), (42, -16), (42, -25), (8, -13), (6, -30), (18, -39), (18, -45), (0, -40),
             (-18, -45), (-18, -39), (-6, -30), (-8, -13), (-42, -25), (-42, -16), (-8, 6), (-7, 20)])
LEAD = shape([(0, 46), (5, 28), (6, 20), (26, 6), (26, 0), (5, 6), (4, -4), (11, -10), (11, -14), (0, -11),
              (-11, -14), (-11, -10), (-4, -4), (-5, 6), (-26, 0), (-26, 6), (-6, 20), (-5, 28)])
LEFT_WING = shape([(-27, 8), (-23, -2), (-22, -8), (-8, -18), (-8, -22), (-22, -17), (-23, -26), (-18, -30), (-18, -33), (-27, -31),
                   (-36, -33), (-36, -30), (-31, -26), (-32, -17), (-46, -22), (-46, -18), (-32, -8), (-31, 2)])
RIGHT_WING = [(-x, y) for x, y in LEFT_WING]
STAR = shape([(0, 30), (8, 9), (30, 9), (12, -5), (19, -27), (0, -14), (-19, -27), (-12, -5), (-30, 9), (-8, 9)])
FEATHER_L = shape([(-34, 20), (-48, 26), (-44, 12), (-49, 8), (-42, 0), (-46, -6), (-36, -12), (-31, 2)])
FEATHER_R = [(-x, y) for x, y in FEATHER_L]


def icon(kind, x, y):
    if kind == 0:
        return poly_dist(x, y, JET)
    if kind == 1:
        return max(poly_dist(x, y, LEAD), poly_dist(x, y, LEFT_WING), poly_dist(x, y, RIGHT_WING))
    return max(poly_dist(x, y, STAR), poly_dist(x, y, FEATHER_L), poly_dist(x, y, FEATHER_R))


w, h = CELL * 3, CELL
rows = []
for py in range(h):
    row = bytearray([0])
    for px in range(w):
        kind = px // CELL
        x = (px % CELL) + 0.5 - CELL / 2
        y = CELL / 2 - (py + 0.5)
        a = max(0.0, min(1.0, 0.5 + icon(kind, x, y) / (2 * SPREAD)))
        row += bytes((255, 255, 255, int(a * 255 + 0.5)))
    rows.append(bytes(row))


def chunk(tag, data):
    return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)


os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, 'wb') as f:
    f.write(b'\x89PNG\r\n\x1a\n')
    f.write(chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)))
    f.write(chunk(b'IDAT', zlib.compress(b''.join(rows), 9)))
    f.write(chunk(b'IEND', b''))
print('made', OUT)
