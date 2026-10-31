# 뭉게구름 퍼프 아틀라스 생성기(순수 파이썬) — 셰이더 ACO/CloudPuffs용.
# 사용: python Tools/aco_cloud_tex.py <출력 폴더> [미리보기.png]
#   ACO_CloudPuffs.png  512²  2×2 칸(칸마다 다른 퍼프 모양), 데이터 텍스처(sRGB 끔)
#     A = 모양(둥근 덩어리 몇 개 + 꽃양배추 같은 가장자리 노이즈, 칸 테두리는 0)
#     R = 두께(가운데일수록 1) — 셰이더가 햇빛 반대쪽 어둡게 / 얇은 가장자리 역광(은빛 테두리)에 쓴다
import math, os, struct, sys, zlib

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None
SIZE = 512
CELL = SIZE // 2


def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + r for r in rows)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


def clamp01(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def smoothstep(a, b, x):
    t = clamp01((x - a) / (b - a))
    return t * t * (3 - 2 * t)


def make_perm(seed):
    p = list(range(256)); s = seed
    for i in range(255, 0, -1):
        s = (s * 1103515245 + 12345) & 0x7fffffff
        j = s % (i + 1); p[i], p[j] = p[j], p[i]
    return p * 2


GR = [(math.cos(i * math.pi / 8 + 0.3), math.sin(i * math.pi / 8 + 0.3)) for i in range(16)]


def pnoise(x, y, p):
    xi = math.floor(x); yi = math.floor(y); xf = x - xi; yf = y - yi
    x0 = xi & 255; y0 = yi & 255; x1 = (x0 + 1) & 255; y1 = (y0 + 1) & 255
    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10); v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
    ga = GR[p[p[x0] + y0] & 15]; gb = GR[p[p[x1] + y0] & 15]; gc = GR[p[p[x0] + y1] & 15]; gd = GR[p[p[x1] + y1] & 15]
    a = ga[0] * xf + ga[1] * yf; b = gb[0] * (xf - 1) + gb[1] * yf
    c = gc[0] * xf + gc[1] * (yf - 1); d = gd[0] * (xf - 1) + gd[1] * (yf - 1)
    t = a + (b - a) * u; bt = c + (d - c) * u
    return 0.5 + 0.75 * (t + (bt - t) * v)


def billow(u, v, base, octaves, p):
    # 꽃양배추 같은 둥근 혹(|2n-1|)을 여러 배율로 — 구름 가장자리
    s = 0.0; amp = 0.5; norm = 0.0; f = base
    for o in range(octaves):
        n = abs(2 * pnoise(u * f + o * 7.1, v * f + o * 13.7, p) - 1)
        s += amp * n; norm += amp; amp *= 0.5; f *= 2.03
    return s / norm


def lcg(seed):
    s = seed
    while True:
        s = (s * 1103515245 + 12345) & 0x7fffffff
        yield s / 0x7fffffff


def make_cell(seed, lobes_count, stretch):
    """칸 하나: 둥근 덩어리(lobe) 몇 개를 합친 장 + 가장자리 노이즈."""
    rnd = lcg(seed)
    p = make_perm(seed * 31 + 7)
    lobes = [(0.0, 0.0, 0.6)]   # 가운데 큰 덩어리
    for i in range(lobes_count):
        a = next(rnd) * math.tau
        d = 0.2 + 0.16 * next(rnd)
        lobes.append((math.cos(a) * d * stretch, math.sin(a) * d * 0.8, 0.34 + 0.12 * next(rnd)))
    alpha = [[0.0] * CELL for _ in range(CELL)]
    thick = [[0.0] * CELL for _ in range(CELL)]
    for y in range(CELL):
        v = (y + 0.5) / CELL * 2 - 1
        for x in range(CELL):
            u = (x + 0.5) / CELL * 2 - 1
            f = 0.0
            for (cx, cy, r) in lobes:
                dx = (u - cx) / stretch; dy = v - cy
                f = max(f, 1 - math.sqrt(dx * dx + dy * dy) / r)
            if f < -0.15:
                continue
            n = billow(u * 0.5 + 0.5, v * 0.5 + 0.5, 3.0, 4, p)
            edge = f + (n - 0.45) * 0.5 * smoothstep(-0.15, 0.05, f)
            # 칸 테두리에서는 반드시 0(밉맵·이웃 칸 번짐 방지)
            border = smoothstep(1.0, 0.86, max(abs(u), abs(v))) * smoothstep(0.98, 0.8, math.sqrt(u * u + v * v))
            alpha[y][x] = smoothstep(0.0, 0.22, edge) * border
            thick[y][x] = clamp01(f * 1.6 + (n - 0.5) * 0.3) * border
    return alpha, thick


def main():
    os.makedirs(OUT, exist_ok=True)
    cells = [make_cell(11, 3, 1.0), make_cell(23, 4, 1.15), make_cell(37, 2, 0.95), make_cell(53, 5, 1.25)]
    rows = []
    prev = []
    for Y in range(SIZE):
        row = bytearray(); prow = bytearray()
        cy, y = divmod(Y, CELL)
        for X in range(SIZE):
            cx, x = divmod(X, CELL)
            a, t = cells[cy * 2 + cx]
            av = a[y][x]; tv = t[y][x]
            row += bytes((int(tv * 255 + 0.5), int(tv * 255 + 0.5), int(tv * 255 + 0.5), int(av * 255 + 0.5)))
            # 미리보기: 하늘색 바탕 위에 흰 구름(두꺼운 곳 약간 어둡게)
            sky = (110, 150, 200); cl = 255 - int(tv * 60)
            prow += bytes((int(sky[0] + (cl - sky[0]) * av), int(sky[1] + (cl - sky[1]) * av), int(sky[2] + (cl - sky[2]) * av), 255))
        rows.append(bytes(row)); prev.append(bytes(prow))
    # PNG는 위에서 아래로 — Unity UV(아래가 0)와 칸 순서만 다를 뿐 셰이더는 칸을 무작위로 고르므로 상관없다
    write_png(os.path.join(OUT, "ACO_CloudPuffs.png"), SIZE, SIZE, rows)
    if PREVIEW:
        write_png(PREVIEW, SIZE, SIZE, prev)
    print("ok", OUT)


main()
