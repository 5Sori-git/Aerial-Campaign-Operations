# -*- coding: utf-8 -*-
"""랜스 포대 텍스처(표준 라이브러리만): python aco_railgun_tex.py [출력 폴더]
Steel(강철 패널 — 이음매·리벳·긁힘·녹물) + Steel_N(노멀맵), Concrete(콘크리트 판 — 줄눈·얼룩), Hazard(경고 줄무늬). 모두 이어 붙는다(타일)."""
import math, os, random, struct, sys, zlib

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'ACO_Addon', 'Textures', 'Railgun')
os.makedirs(OUT, exist_ok=True)


def write_png(path, w, h, rgb):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgb[y * w * 3:(y + 1) * w * 3]
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(data)
    print('made', os.path.basename(path))


def value_noise(size, cells, seed):
    """타일되는 값 노이즈(0~1)."""
    r = random.Random(seed)
    grid = [[r.random() for _ in range(cells)] for _ in range(cells)]
    out = [0.0] * (size * size)
    k = cells / float(size)
    for y in range(size):
        fy = y * k
        y0 = int(fy); ty = fy - y0
        ty = ty * ty * (3 - 2 * ty)
        ra = grid[y0 % cells]; rb = grid[(y0 + 1) % cells]
        for x in range(size):
            fx = x * k
            x0 = int(fx); tx = fx - x0
            tx = tx * tx * (3 - 2 * tx)
            x1 = (x0 + 1) % cells; x0 %= cells
            a = ra[x0] + (ra[x1] - ra[x0]) * tx
            b = rb[x0] + (rb[x1] - rb[x0]) * tx
            out[y * size + x] = a + (b - a) * ty
    return out


def fbm(size, cells, octaves, seed):
    total = [0.0] * (size * size)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        n = value_noise(size, cells << o, seed + o)
        for i in range(len(total)):
            total[i] += n[i] * amp
        norm += amp
        amp *= 0.5
    return [v / norm for v in total]


def steel():
    S = 1024
    grime = fbm(S, 8, 5, 11)
    fine = fbm(S, 64, 3, 12)
    streak = value_noise(S, 96, 13)
    height = [0.0] * (S * S)
    tint = [1.0] * (S * S)
    wear = [0.0] * (S * S)      # 이음매 둘레 까짐
    r = random.Random(7)
    # 패널: 8칸 격자를 무작위로 합친다(이어 붙게 경계에서 잘라 감싼다)
    cell = S // 8
    used = [[False] * 8 for _ in range(8)]
    panels = []
    for gy in range(8):
        for gx in range(8):
            if used[gy][gx]:
                continue
            w = r.choice((1, 1, 2, 2, 3))
            h = r.choice((1, 2, 2, 3))
            ok_w = 0
            for k in range(w):
                if gx + k < 8 and not used[gy][gx + k]:
                    ok_w += 1
                else:
                    break
            ok_h = min(h, 8 - gy)
            for yy in range(gy, gy + ok_h):
                for xx in range(gx, gx + ok_w):
                    used[yy][xx] = True
            panels.append((gx * cell, gy * cell, ok_w * cell, ok_h * cell, r.uniform(0.86, 1.1)))
    for px, py, pw, ph, shade in panels:
        for y in range(py, py + ph):
            row = y % S
            dy = min(y - py, py + ph - 1 - y)
            for x in range(px, px + pw):
                i = row * S + x % S
                dx = min(x - px, px + pw - 1 - x)
                d = min(dx, dy)
                tint[i] = shade
                if d < 3:
                    height[i] = 0.0            # 이음매 홈
                elif d < 9:
                    height[i] = (d - 3) / 6.0  # 모서리 경사
                    wear[i] = max(wear[i], 1.0 - (d - 3) / 6.0)
                else:
                    height[i] = 1.0
        # 리벳: 패널 둘레
        step = 40
        def rivet(cx, cy):
            for v in range(-5, 6):
                for u in range(-5, 6):
                    q = u * u + v * v
                    if q <= 25:
                        i = ((cy + v) % S) * S + (cx + u) % S
                        height[i] = 1.0 + 0.9 * math.sqrt(max(1.0 - q / 25.0, 0.0))
                        wear[i] = max(wear[i], 0.35)
        for x in range(px + 20, px + pw - 10, step):
            rivet(x, py + 15); rivet(x, py + ph - 15)
        for y in range(py + 20 + step, py + ph - 20, step):
            rivet(px + 15, y); rivet(px + pw - 15, y)
    # 긁힘
    for _ in range(160):
        x, y = r.randrange(S), r.randrange(S)
        a = r.uniform(0, math.pi * 2)
        l = r.randrange(20, 140)
        for t in range(l):
            i = (int(y + math.sin(a) * t) % S) * S + int(x + math.cos(a) * t) % S
            wear[i] = max(wear[i], 0.6)
            height[i] -= 0.12
    rgb = bytearray(S * S * 3)
    nrm = bytearray(S * S * 3)
    base = (86, 92, 98)
    rust = (84, 58, 42)
    edge = (150, 154, 156)
    for y in range(S):
        for x in range(S):
            i = y * S + x
            g = grime[i]
            f = fine[i]
            # 아래로 흐른 얼룩: 세로로 늘인 노이즈
            s = streak[(y // 6) * S + x]
            dark = 0.62 + 0.5 * g + 0.2 * (f - 0.5)
            if s > 0.6:
                dark *= 1.0 - (s - 0.6) * 0.6
            shade = tint[i] * dark
            c = [base[k] * shade for k in range(3)]
            # 녹
            ru = max(min((g - 0.56) * 7.0, 1.0), 0.0) * max(min((f - 0.42) * 4.0, 1.0), 0.0)
            ru = min(ru + wear[i] * 0.25 * (g > 0.5), 1.0)
            for k in range(3):
                c[k] += (rust[k] * dark - c[k]) * ru
            # 이음매 어둡게, 모서리 벗겨짐
            if height[i] <= 0.01:
                c = [v * 0.35 for v in c]
            w = wear[i] * 0.55
            for k in range(3):
                c[k] += (edge[k] - c[k]) * w
            j = i * 3
            rgb[j] = max(min(int(c[0]), 255), 0); rgb[j + 1] = max(min(int(c[1]), 255), 0); rgb[j + 2] = max(min(int(c[2]), 255), 0)
            # 노멀(높이 기울기)
            hx = height[y * S + (x + 1) % S] - height[y * S + (x - 1) % S] + (fine[y * S + (x + 1) % S] - fine[y * S + (x - 1) % S]) * 0.6
            hy = height[((y + 1) % S) * S + x] - height[((y - 1) % S) * S + x] + (fine[((y + 1) % S) * S + x] - fine[((y - 1) % S) * S + x]) * 0.6
            nx, ny, nz = -hx * 1.6, hy * 1.6, 1.0
            m = math.sqrt(nx * nx + ny * ny + nz * nz)
            nrm[j] = int((nx / m * 0.5 + 0.5) * 255); nrm[j + 1] = int((ny / m * 0.5 + 0.5) * 255); nrm[j + 2] = int((nz / m * 0.5 + 0.5) * 255)
    write_png(os.path.join(OUT, 'ACO_Railgun_Steel.png'), S, S, rgb)
    write_png(os.path.join(OUT, 'ACO_Railgun_Steel_N.png'), S, S, nrm)


def concrete():
    S = 1024
    large = fbm(S, 6, 5, 21)
    fine = fbm(S, 128, 2, 22)
    stain = fbm(S, 10, 4, 23)
    rgb = bytearray(S * S * 3)
    joint = S // 4
    for y in range(S):
        for x in range(S):
            i = y * S + x
            v = 118 * (0.72 + 0.5 * large[i]) + (fine[i] - 0.5) * 34
            if stain[i] > 0.58:
                v *= 1.0 - (stain[i] - 0.58) * 2.2
            dj = min(x % joint, joint - x % joint, y % joint, joint - y % joint)
            if dj < 3:
                v *= 0.42
            elif dj < 7:
                v *= 0.86
            j = i * 3
            rgb[j] = max(min(int(v * 1.0), 255), 0); rgb[j + 1] = max(min(int(v * 0.985), 255), 0); rgb[j + 2] = max(min(int(v * 0.95), 255), 0)
    write_png(os.path.join(OUT, 'ACO_Railgun_Concrete.png'), S, S, rgb)


def hazard():
    S = 256
    wear = fbm(S, 16, 3, 31)
    rgb = bytearray(S * S * 3)
    for y in range(S):
        for x in range(S):
            band = ((x + y) // (S // 4)) % 2 == 0
            c = (214, 166, 34) if band else (26, 26, 28)
            d = 0.74 + 0.5 * wear[y * S + x]
            if wear[y * S + x] > 0.68:
                c = (96, 94, 92)
            j = (y * S + x) * 3
            rgb[j] = min(int(c[0] * d), 255); rgb[j + 1] = min(int(c[1] * d), 255); rgb[j + 2] = min(int(c[2] * d), 255)
    write_png(os.path.join(OUT, 'ACO_Railgun_Hazard.png'), S, S, rgb)


steel()
concrete()
hazard()
