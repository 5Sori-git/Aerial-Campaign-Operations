# 육각 지형용 이음매 없는(타일링) 텍스처 생성기(순수 파이썬, 외부 라이브러리 없음).
# 사용: python Tools/aco_terrain_tex.py <출력 폴더> [미리보기.png]
# 셰이더 ACO/HexTerrain이 월드 좌표로 깔아 쓰는 "데이터" 텍스처(색이 아니라 노이즈 값 — 색은 머티리얼의 색 속성이 정한다):
#   ACO_TerrainDetail.png        512²  R = 잔결(흙·풀 알갱이), G = 중간 얼룩, B = 바위 균열(능선 노이즈), A = 풀 덩어리
#   ACO_TerrainDetailNormal.png  512²  위 잔결·균열·덩어리로 만든 노멀 맵(Unity에서 Normal map으로 가져온다)
#   ACO_TerrainMacro.png         256²  R = 큰 밝기 변화, G = 메마름(마른 풀/초원), B = 숲 분포, A = 경계 흔들림(높이대 경계를 들쭉날쭉하게)
# 모든 노이즈는 주기 격자(정수 주파수)라 가장자리가 이어진다.
import math, os, struct, sys, zlib

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None


def make_perm(seed):
    p = list(range(256))
    s = seed
    for i in range(255, 0, -1):
        s = (s * 1103515245 + 12345) & 0x7fffffff
        j = s % (i + 1)
        p[i], p[j] = p[j], p[i]
    return p * 2


_GRAD = [(math.cos(k * math.pi / 8 + 0.3), math.sin(k * math.pi / 8 + 0.3)) for k in range(16)]


def pnoise(x, y, period, perm):
    """주기 period(정수)로 반복되는 그래디언트 노이즈, 대략 0~1."""
    xi = math.floor(x); yi = math.floor(y)
    xf = x - xi; yf = y - yi
    x0 = xi % period; y0 = yi % period
    x1 = (x0 + 1) % period; y1 = (y0 + 1) % period
    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10); v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
    p = perm; g = _GRAD
    ga = g[p[p[x0] + y0] & 15]; gb = g[p[p[x1] + y0] & 15]
    gc = g[p[p[x0] + y1] & 15]; gd = g[p[p[x1] + y1] & 15]
    a = ga[0] * xf + ga[1] * yf
    b = gb[0] * (xf - 1) + gb[1] * yf
    c = gc[0] * xf + gc[1] * (yf - 1)
    d = gd[0] * (xf - 1) + gd[1] * (yf - 1)
    top = a + (b - a) * u
    bot = c + (d - c) * u
    return 0.5 + 0.75 * (top + (bot - top) * v)


def fbm(u, v, base, octaves, perm, mode=0):
    """u, v: 0~1 타일 좌표. base: 첫 옥타브 주파수(정수). mode 0 = 보통, 1 = 능선(균열), 2 = 덩어리(billow)."""
    s = 0.0; amp = 0.5; norm = 0.0; f = base
    for o in range(octaves):
        n = pnoise(u * f + o * 7, v * f + o * 13, f, perm)
        if mode == 1:
            n = 1.0 - abs(2.0 * n - 1.0)
            n = n * n
        elif mode == 2:
            n = abs(2.0 * n - 1.0)
        s += amp * n
        norm += amp; amp *= 0.5; f *= 2
    return s / norm


def clamp01(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def contrast(v, k, mid=0.5):
    return clamp01((v - mid) * k + 0.5)


def smooth(e0, e1, v):
    t = clamp01((v - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def write_png(path, w, h, rows_rgba):
    raw = b"".join(b"\x00" + r for r in rows_rgba)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


def save(path, w, h, px):
    rows = []
    for y in range(h):
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[y * w + x]
            row += bytes((int(clamp01(r) * 255 + 0.5), int(clamp01(g) * 255 + 0.5), int(clamp01(b) * 255 + 0.5), int(clamp01(a) * 255 + 0.5)))
        rows.append(bytes(row))
    write_png(path, w, h, rows)


os.makedirs(OUT, exist_ok=True)
P1, P2, P3, P4 = make_perm(11), make_perm(23), make_perm(37), make_perm(53)

# ───────────── 디테일 ─────────────
N = 512
detail = [None] * (N * N)
height = [0.0] * (N * N)
for y in range(N):
    v = y / N
    for x in range(N):
        u = x / N
        grain = contrast(fbm(u, v, 32, 4, P1), 1.6)
        blotch = contrast(fbm(u, v, 4, 4, P2), 1.8)
        crack = contrast(fbm(u, v, 6, 4, P3, 1), 1.5, 0.45)
        clump = contrast(fbm(u, v, 16, 3, P4, 2) * 1.6, 1.3)
        detail[y * N + x] = (grain, blotch, crack, clump)
        # 노멀용 높이: 잔결 + 풀 덩어리 + 균열(골이 파이게)
        height[y * N + x] = grain * 0.45 + clump * 0.35 + (1.0 - crack) * 0.35 + blotch * 0.25
save(os.path.join(OUT, "ACO_TerrainDetail.png"), N, N, detail)
print("detail")

# 노멀 맵(가장자리 이음 — 이웃을 감아 읽는다). Unity 노멀 맵: R = +X(오른쪽), G = +Y(위쪽 = 텍스처 v 증가 방향)
normal = [None] * (N * N)
k = 6.0  # 높이 세기
for y in range(N):
    for x in range(N):
        hl = height[y * N + (x - 1) % N]; hr = height[y * N + (x + 1) % N]
        hd = height[((y - 1) % N) * N + x]; hu = height[((y + 1) % N) * N + x]
        # PNG는 위에서 아래로 저장되고 Unity는 아래 행을 v = 0으로 읽는다 — 행이 늘수록 v가 줄어든다
        nx = -(hr - hl) * k
        ny = (hu - hd) * k
        nz = 1.0
        l = math.sqrt(nx * nx + ny * ny + nz * nz)
        normal[y * N + x] = (nx / l * 0.5 + 0.5, ny / l * 0.5 + 0.5, nz / l * 0.5 + 0.5, 1.0)
save(os.path.join(OUT, "ACO_TerrainDetailNormal.png"), N, N, normal)
print("normal")

# ───────────── 매크로 ─────────────
M = 256
macro = [None] * (M * M)
for y in range(M):
    v = y / M
    for x in range(M):
        u = x / M
        bright = contrast(fbm(u, v, 2, 5, P2), 1.5)
        dry = contrast(fbm(u, v, 3, 4, P3), 1.7)
        forest = contrast(fbm(u, v, 5, 4, P4), 1.9)
        edge = contrast(fbm(u, v, 8, 4, P1), 1.6)
        macro[y * M + x] = (bright, dry, forest, edge)
save(os.path.join(OUT, "ACO_TerrainMacro.png"), M, M, macro)
print("macro")

# ───────────── 미리보기: 셰이더와 비슷한 규칙으로 칠한 예(높이 = 매크로 노이즈) ─────────────
if PREVIEW:
    W = 768
    GRASS = (0.30, 0.42, 0.20); DRY = (0.55, 0.52, 0.30); FOREST = (0.13, 0.22, 0.10)
    SAND = (0.78, 0.72, 0.55); ROCK = (0.45, 0.43, 0.40); SNOW = (0.93, 0.94, 0.97); DIRT = (0.42, 0.34, 0.24)

    def mix(a, b, t):
        return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)

    rows = []
    for y in range(W):
        row = bytearray()
        for x in range(W):
            mx = macro[(y * M // W) * M + (x * M // W)]
            dt = detail[((y * 3) % N) * N + (x * 3) % N]
            nrm = normal[((y * 3) % N) * N + (x * 3) % N]
            h = (x / W) * 1300.0 + (mx[3] - 0.5) * 240.0  # 왼쪽 → 오른쪽으로 높아짐
            dry = smooth(0.45, 0.75, mx[1] + (dt[1] - 0.5) * 0.25)
            forest = smooth(0.5, 0.65, mx[2] + (dt[3] - 0.5) * 0.35) * (1 - smooth(350, 650, h))
            c = mix(GRASS, DRY, dry * 0.8)
            c = mix(c, FOREST, forest)
            c = mix(c, DIRT, smooth(450, 650, h) * 0.6)
            c = mix(c, ROCK, smooth(600, 800, h))
            c = mix(c, SAND, 1 - smooth(20, 45, h))
            c = mix(c, SNOW, smooth(980, 1100, h + (dt[0] - 0.5) * 80))
            shade = (0.75 + 0.5 * dt[0]) * (0.88 + 0.24 * mx[0])
            lit = max(0.0, (nrm[0] - 0.5) * -0.6 + (nrm[1] - 0.5) * 0.6 + (nrm[2] - 0.5) * 2.0) * 0.55 + 0.45
            s = shade * lit
            row += bytes((int(clamp01(c[0] * s) * 255), int(clamp01(c[1] * s) * 255), int(clamp01(c[2] * s) * 255), 255))
        rows.append(bytes(row))
    write_png(PREVIEW, W, W, rows)
    print("preview")
