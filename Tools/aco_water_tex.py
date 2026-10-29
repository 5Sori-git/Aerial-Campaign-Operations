# 바다 셰이더(ACO/Ocean)용 이음매 없는 텍스처 생성기(순수 파이썬).
# 사용: python Tools/aco_water_tex.py <출력 폴더> [미리보기.png]
#   ACO_WaterNormal.png  512²  물결 노멀(주기 사인파 묶음 — 바람 방향 쪽으로 몰린 파도 + 잔물결). Unity에서 Normal map으로 가져온다
#   ACO_WaterFoam.png    256²  R = 거품 무늬(덩어리 노이즈), G = 큰 얼룩(바다 색·거품 분포를 넓게 흔든다) — 데이터 텍스처(sRGB 끔)
import math, os, struct, sys, zlib

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None


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


def lcg(seed):
    s = seed
    while True:
        s = (s * 1103515245 + 12345) & 0x7fffffff
        yield s / 0x7fffffff


# ───────────── 거품 / 큰 얼룩 (주기 그래디언트 노이즈) ─────────────
def make_perm(seed):
    p = list(range(256)); s = seed
    for i in range(255, 0, -1):
        s = (s * 1103515245 + 12345) & 0x7fffffff
        j = s % (i + 1); p[i], p[j] = p[j], p[i]
    return p * 2

GR = [(math.cos(i * math.pi / 8 + 0.3), math.sin(i * math.pi / 8 + 0.3)) for i in range(16)]

def pnoise(x, y, period, p):
    xi = math.floor(x); yi = math.floor(y); xf = x - xi; yf = y - yi
    x0 = xi % period; y0 = yi % period; x1 = (x0 + 1) % period; y1 = (y0 + 1) % period
    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10); v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
    ga = GR[p[p[x0] + y0] & 15]; gb = GR[p[p[x1] + y0] & 15]; gc = GR[p[p[x0] + y1] & 15]; gd = GR[p[p[x1] + y1] & 15]
    a = ga[0] * xf + ga[1] * yf; b = gb[0] * (xf - 1) + gb[1] * yf
    c = gc[0] * xf + gc[1] * (yf - 1); d = gd[0] * (xf - 1) + gd[1] * (yf - 1)
    t = a + (b - a) * u; bt = c + (d - c) * u
    return 0.5 + 0.75 * (t + (bt - t) * v)

def fbm(u, v, base, octaves, p, billow=False):
    s = 0.0; amp = 0.5; norm = 0.0; f = base
    for o in range(octaves):
        n = pnoise(u * f + o * 7, v * f + o * 13, f, p)
        if billow:
            n = abs(2 * n - 1)
        s += amp * n; norm += amp; amp *= 0.5; f *= 2
    return s / norm

# ───────────── 물결 높이: 정수 파수 사인파(타일 경계에서 이어진다) ─────────────
rnd = lcg(77)
waves = []
wind = math.radians(25.0)
for i in range(28):
    # 바람 방향 ±60° 안에 몰고, 파장이 짧을수록 작게
    ang = wind + (next(rnd) - 0.5) * math.radians(120)
    kmag = 2.0 + next(rnd) ** 1.5 * 18.0
    kx = round(math.cos(ang) * kmag)
    kz = round(math.sin(ang) * kmag)
    if kx == 0 and kz == 0:
        continue
    amp = 1.0 / (math.hypot(kx, kz) ** 1.25)
    waves.append((kx, kz, amp, next(rnd) * 6.2832))

N = 512
height = [0.0] * (N * N)
for y in range(N):
    v = y / N
    for x in range(N):
        u = x / N
        h = 0.0
        for kx, kz, amp, ph in waves:
            s = math.sin(6.2832 * (kx * u + kz * v) + ph)
            # 마루는 좁고 골은 넓게(부드럽게 — 꺾임선이 생기지 않는 지수형)
            h += amp * math.exp(1.6 * (s - 1.0))
        height[y * N + x] = h

lo = min(height); hi = max(height)
height = [(h - lo) / (hi - lo) for h in height]
# 잔물결: 주기 그래디언트 노이즈(아래 거품과 같은 함수)를 섞어 사인파 격자 느낌을 깬다
P3 = make_perm(21)
for y in range(N):
    for x in range(N):
        r = fbm(x / N, y / N, 16, 3, P3)
        height[y * N + x] = height[y * N + x] + r * 0.12
k = 6.0
rows = []
for y in range(N):
    row = bytearray()
    for x in range(N):
        hl = height[y * N + (x - 1) % N]; hr = height[y * N + (x + 1) % N]
        hd = height[((y - 1) % N) * N + x]; hu = height[((y + 1) % N) * N + x]
        nx = -(hr - hl) * k
        ny = (hu - hd) * k
        l = math.sqrt(nx * nx + ny * ny + 1.0)
        row += bytes((int(clamp01(nx / l * 0.5 + 0.5) * 255), int(clamp01(ny / l * 0.5 + 0.5) * 255), int(clamp01(1.0 / l * 0.5 + 0.5) * 255), 255))
    rows.append(bytes(row))
os.makedirs(OUT, exist_ok=True)
write_png(os.path.join(OUT, "ACO_WaterNormal.png"), N, N, rows)
print("normal")

P1, P2 = make_perm(5), make_perm(9)
M = 256
foam_rows = []
foam = [0.0] * (M * M)
for y in range(M):
    row = bytearray()
    for x in range(M):
        u = x / M; v = y / M
        f = clamp01((fbm(u, v, 8, 4, P1, True) * 1.7 - 0.25) * 1.4)
        big = clamp01((fbm(u, v, 2, 4, P2) - 0.5) * 1.8 + 0.5)
        foam[y * M + x] = f
        row += bytes((int(f * 255), int(big * 255), 0, 255))
    foam_rows.append(bytes(row))
write_png(os.path.join(OUT, "ACO_WaterFoam.png"), M, M, foam_rows)
print("foam")

if PREVIEW:
    # 하늘빛 반사를 흉내 낸 간단한 음영(빛 = 왼쪽 위)
    W = 512
    out = []
    for y in range(W):
        row = bytearray()
        for x in range(W):
            hl = height[y * N + (x - 1) % N]; hr = height[y * N + (x + 1) % N]
            hd = height[((y - 1) % N) * N + x]; hu = height[((y + 1) % N) * N + x]
            sl = (hl - hr + hu - hd) * 6.0
            fo = foam[(y // 2) * M + (x // 2)]
            base = (0.05 + sl * 0.12, 0.22 + sl * 0.2, 0.33 + sl * 0.25)
            fm = max(0.0, fo - 0.75) * 3.0
            c = [clamp01(base[i] + fm * 0.8) for i in range(3)]
            row += bytes((int(c[0] * 255), int(c[1] * 255), int(c[2] * 255), 255))
        out.append(bytes(row))
    write_png(PREVIEW, W, W, out)
    print("preview")
