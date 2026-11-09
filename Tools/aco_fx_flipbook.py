# -*- coding: utf-8 -*-
# 폭발 플립북 생성기(numpy). 시스템 파이썬에 numpy가 없으므로 블렌더 파이썬으로 돌린다.
# 사용: blender --background --factory-startup --python Tools/aco_fx_flipbook.py -- <출력 폴더> [미리보기 폴더]
# 만드는 것(RGBA PNG, 알파 = 투명도, 빈 칸에도 색을 채워 가장자리 어두운 테두리가 없다):
#   fx_fireball_8x8.png  2048²  불덩이 64프레임(흰 섬광 → 부풀어 오르는 화염 → 식어 검은 그을음)
#   fx_smoke_8x8.png     1024²  연기 뭉치 64프레임(위에서 빛을 받은 밝은 회색 — 파티클 색으로 물들여 쓴다)
#   fx_trail.png         64²    연기 줄기(u = 길이, v = 폭 — 파티클 트레일용)
#   fx_ribbon.png        512×256 미사일 연기 리본(u 방향으로 이어진다 — 뭉친 무늬, 가장자리가 흩어진다)
#   fx_foam.png          512²   항적 거품 조각(방향 없음 — 가운데 진한 거품, 가장자리는 레이스처럼 찢어진다)
# aco_fx.py가 만든 fx_flash·fx_shockwave·fx_spark는 그대로 쓴다.
import os
import struct
import sys
import zlib

import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
OUT = argv[0]
PREVIEW = argv[1] if len(argv) > 1 else None

COLS = 8
FRAMES = COLS * COLS

rng = np.random.RandomState(1337)
LATTICE = 64
_grid = rng.rand(LATTICE, LATTICE, LATTICE).astype(np.float32)


def noise3(x, y, z):
    """주기 격자 값 노이즈(5차 보간) 0~1."""
    xi = np.floor(x).astype(np.int32)
    yi = np.floor(y).astype(np.int32)
    zi = np.floor(z).astype(np.int32)
    fx = x - xi
    fy = y - yi
    fz = z - zi
    ux = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    uy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
    uz = fz * fz * fz * (fz * (fz * 6 - 15) + 10)
    m = LATTICE - 1
    x0 = xi & m
    x1 = (xi + 1) & m
    y0 = yi & m
    y1 = (yi + 1) & m
    z0 = zi & m
    z1 = (zi + 1) & m
    g = _grid
    a = g[x0, y0, z0] * (1 - ux) + g[x1, y0, z0] * ux
    b = g[x0, y1, z0] * (1 - ux) + g[x1, y1, z0] * ux
    c = g[x0, y0, z1] * (1 - ux) + g[x1, y0, z1] * ux
    d = g[x0, y1, z1] * (1 - ux) + g[x1, y1, z1] * ux
    return (a * (1 - uy) + b * uy) * (1 - uz) + (c * (1 - uy) + d * uy) * uz


def fbm(x, y, z, octaves=6, gain=0.5):
    v = 0.0
    amp = 1.0
    total = 0.0
    for i in range(octaves):
        v = v + noise3(x, y, z) * amp
        total += amp
        amp *= gain
        x = x * 2.03 + 17.0
        y = y * 2.03 + 9.0
        z = z * 2.03 + 3.0
    return v / total


def smooth(e0, e1, v):
    t = np.clip((v - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def ramp(stops, v):
    xs = [s[0] for s in stops]
    out = [np.interp(v, xs, [s[1][k] for s in stops]) for k in range(3)]
    return np.stack(out, axis=-1)


def cell_grid(cell):
    c = (np.arange(cell, dtype=np.float32) + 0.5) / cell * 2.0 - 1.0
    x, y = np.meshgrid(c, c)
    return x, -y   # y는 위가 +


FIRE = [
    (0.00, (0.13, 0.115, 0.105)),
    (0.14, (0.20, 0.13, 0.09)),
    (0.28, (0.50, 0.15, 0.04)),
    (0.44, (0.96, 0.30, 0.04)),
    (0.60, (1.00, 0.55, 0.09)),
    (0.78, (1.00, 0.80, 0.34)),
    (1.00, (1.00, 0.97, 0.84)),
]


def make_blobs(seed, big, small):
    """단위 구 안의 뭉치들: 방향, 거리, 반지름, 움직임 위상. 큰 뭉치가 몸통, 작은 뭉치가 표면 혹."""
    r = np.random.RandomState(seed)
    n = big + small
    d = r.normal(size=(n, 3))
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    dist = np.concatenate([r.uniform(0.0, 0.55, big), r.uniform(0.60, 0.95, small)])
    rad = np.concatenate([r.uniform(0.34, 0.52, big), r.uniform(0.12, 0.26, small)])
    return d.astype(np.float32), dist.astype(np.float32), rad.astype(np.float32)


def puff_field(blobs, x, y, radius, spread):
    """뭉치 합집합의 높이장 0~1(부드러운 최댓값). spread: 뭉치가 중심에서 벌어진 정도."""
    d, dist, rad = blobs
    k = 6.0
    acc = np.zeros_like(x)
    for i in range(len(rad)):
        cx = d[i, 0] * dist[i] * spread * radius
        cy = d[i, 1] * dist[i] * spread * radius
        cz = d[i, 2] * dist[i] * spread * radius
        rr = rad[i] * radius
        q = 1.0 - ((x - cx) ** 2 + (y - cy) ** 2) / (rr * rr)
        q = np.clip(q, 0.0, 1.0)
        hh = np.sqrt(q) * rr + np.where(q > 0.0, cz + radius, 0.0)
        hh = np.maximum(hh, 0.0)
        acc = acc + hh ** k
    return (acc ** (1.0 / k)) / (2.0 * radius)


def warp(x, y, t, seed):
    """뭉치 윤곽을 깨는 좌표 흔들기(큰 물결 + 잔 물결)."""
    ax = fbm(x * 2.2 + seed, y * 2.2, t * 0.8, 3) - 0.5
    ay = fbm(x * 2.2, y * 2.2 + seed, t * 0.8 + 4.0, 3) - 0.5
    bx = fbm(x * 7.0 + seed, y * 7.0 + 3.0, t * 1.2, 4) - 0.5
    by = fbm(x * 7.0 + 5.0, y * 7.0 + seed, t * 1.2 + 2.0, 4) - 0.5
    cx = fbm(x * 21.0 + seed, y * 21.0, t * 1.6, 3) - 0.5
    cy = fbm(x * 21.0, y * 21.0 + seed, t * 1.6 + 1.0, 3) - 0.5
    return x + ax * 0.30 + bx * 0.17 + cx * 0.06, y + ay * 0.30 + by * 0.17 + cy * 0.06


def lit(h, cell, relief):
    gy, gx = np.gradient(h)
    k = cell * relief
    nx = -gx * k
    ny = gy * k
    nz = np.ones_like(h)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    return (nx * -0.42 + ny * 0.72 + nz * 0.55) / length


FIRE_BLOBS = make_blobs(21, 14, 90)
SMOKE_BLOBS = make_blobs(52, 14, 80)


def fireball_frame(t, cell):
    x, y = cell_grid(cell)
    radius = 0.24 + 0.50 * (1.0 - (1.0 - t) ** 2.4)
    spread = 0.72 + 0.34 * t
    wx, wy = warp(x / radius * 0.55, y / radius * 0.55, t, 40.0)
    h = puff_field(FIRE_BLOBS, wx * radius / 0.55, wy * radius / 0.55, radius, spread)
    h = gaussian(h, 1.6)
    detail = fbm(x / radius * 3.2 + 40.0, y / radius * 3.2 + 40.0, t * 0.9, 5) - 0.5
    fine = fbm(x / radius * 9.0 + 7.0, y / radius * 9.0 + 3.0, t * 1.4, 3) - 0.5
    h2 = h * (1.0 + detail * 0.62 + fine * 0.22)
    alpha = smooth(0.02, 0.26 + 0.20 * t, h2) * edge_mask(x, y)
    n = np.clip(h / max(float(h.max()), 0.001), 0.0, 1.0)
    cool = np.clip(1.0 - t * 1.22, 0.0, 1.0)
    # 두꺼운 안쪽이 뜨겁고, 얇은 표면 혹부터 그을음으로 식는다
    temp = (n ** (0.75 + 1.6 * t)) * (0.42 + 1.08 * cool ** 1.15) + detail * (0.62 * cool + 0.10)
    temp = np.clip(temp + 0.22 * cool ** 3.0 - 0.34 * t, 0.0, 1.0)
    rgb = ramp(FIRE, temp)
    light = np.clip(lit(gaussian(h2, 1.0), cell, 0.55), -0.2, 1.0)
    tone = 0.55 + 0.95 * np.clip(light, 0.0, 1.0)
    glow = smooth(0.26, 0.62, temp)[..., None]
    rgb = rgb * (tone[..., None] * (1.0 - glow) + glow)
    return np.clip(rgb, 0.0, 1.0), alpha


def smoke_frame(t, cell):
    x, y = cell_grid(cell)
    radius = 0.44 + 0.30 * (1.0 - (1.0 - t) ** 1.7)
    spread = 0.8 + 0.3 * t
    wx, wy = warp(x / radius * 0.55, y / radius * 0.55, t * 0.7, 90.0)
    h = puff_field(SMOKE_BLOBS, wx * radius / 0.55, wy * radius / 0.55, radius, spread)
    h = gaussian(h, 1.4)
    detail = fbm(x / radius * 2.8 + 90.0, y / radius * 2.8 + 90.0, t * 0.7, 5) - 0.5
    fine = fbm(x / radius * 8.0 + 3.0, y / radius * 8.0 + 8.0, t, 3) - 0.5
    h2 = h * (1.0 + detail * 0.75 + fine * 0.25)
    thin = smooth(0.40, 1.0, t)
    alpha = smooth(0.0 + thin * 0.28, 0.42 + thin * 0.34, h2) * edge_mask(x, y)
    alpha = alpha * smooth(0.0, 0.06, t) ** 0.5 * (1.0 - smooth(0.80, 1.0, t))
    light = lit(gaussian(h2, 1.2), cell, 0.42)
    tone = 0.50 + 0.52 * np.clip(light * 0.5 + 0.5, 0.0, 1.0) ** 1.4
    rgb = np.repeat(np.clip(tone, 0.0, 1.0)[..., None], 3, axis=2)
    return rgb, alpha


def edge_mask(x, y):
    """칸 가장자리에서 알파를 0으로(옆 칸에 번지지 않게)."""
    return 1.0 - smooth(0.86, 0.985, np.maximum(np.abs(x), np.abs(y)))


def gaussian(a, sigma):
    """분리형 가우시안(가장자리는 복제)."""
    r = int(sigma * 3 + 0.5)
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k = (k / k.sum()).astype(np.float32)
    b = np.pad(a, r, mode='edge')
    b = np.apply_along_axis(lambda v: np.convolve(v, k, mode='same'), 0, b)
    b = np.apply_along_axis(lambda v: np.convolve(v, k, mode='same'), 1, b)
    return b[r:-r, r:-r]


def flipbook(size, frame_fn):
    cell = size // COLS
    img = np.zeros((size, size, 4), dtype=np.float32)
    for n in range(FRAMES):
        t = n / float(FRAMES - 1)
        rgb, a = frame_fn(t, cell)
        ox = (n % COLS) * cell
        oy = (n // COLS) * cell
        img[oy:oy + cell, ox:ox + cell, :3] = rgb
        img[oy:oy + cell, ox:ox + cell, 3] = a
        print('frame', n, flush=True)
    return img


def trail(size):
    v = (np.arange(size, dtype=np.float32) + 0.5) / size * 2.0 - 1.0
    a = np.exp(-(v * v) * 5.0)[:, None] * (1.0 - smooth(0.82, 1.0, np.abs(v)))[:, None]
    u = np.ones((size, size), dtype=np.float32)
    # 줄기에 얼룩을 조금 준다(가로로 이어지게 주기 잡음)
    c = (np.arange(size, dtype=np.float32) / size) * 8.0
    x, y = np.meshgrid(c, c * 0.5)
    mottling = 0.72 + 0.28 * noise3(x, y, np.zeros_like(x))
    img = np.zeros((size, size, 4), dtype=np.float32)
    img[..., :3] = 1.0
    img[..., 3] = (a * u) * mottling
    return img


def ribbon(w, h):
    """u로 이어지는 연기 띠: 잡음을 한 주기 어긋나게 섞어 이음매를 없앤다."""
    u = (np.arange(w, dtype=np.float32) + 0.5) / w
    v = (np.arange(h, dtype=np.float32) + 0.5) / h
    uu, vv = np.meshgrid(u, v)
    span = 4.0

    def field(x):
        wx = fbm(x * 0.7 + 3.0, vv * 2.0, 0.2, 3) - 0.5
        return fbm(x * 1.2 + wx * 0.9 + 11.0, vv * 2.2 + wx * 0.7, 1.7, 3)
    n = field(uu * span) * (1.0 - uu) + field((uu - 1.0) * span) * uu
    n = (n - float(n.min())) / max(float(n.max() - n.min()), 0.001)
    edge = 1.0 - np.abs(vv * 2.0 - 1.0)
    alpha = smooth(0.0, 0.8, edge + (n - 0.5) * 0.5) * (0.78 + 0.22 * n)
    alpha = alpha * (1.0 - smooth(0.92, 1.0, np.abs(vv * 2.0 - 1.0)))
    gy, gx = np.gradient(gaussian(n, 5.0))
    tone = np.clip(0.88 + (gy * 4.0 - gx * 1.2) + (n - 0.5) * 0.14, 0.7, 1.0)
    img = np.zeros((h, w, 4), dtype=np.float32)
    img[..., :3] = tone[..., None]
    img[..., 3] = alpha
    return img


def foam(size):
    x, y = cell_grid(size)
    r = np.sqrt(x * x + y * y)
    wx = fbm(x * 2.0 + 5.0, y * 2.0, 0.4, 4) - 0.5
    wy = fbm(x * 2.0, y * 2.0 + 5.0, 0.9, 4) - 0.5
    px = x + wx * 0.5
    py = y + wy * 0.5
    # 접힌 잡음의 능선 = 거품 줄기
    lace = 1.0 - np.abs(2.0 * fbm(px * 5.5 + 21.0, py * 5.5 + 3.0, 2.1, 5) - 1.0)
    fine = 1.0 - np.abs(2.0 * fbm(px * 15.0 + 8.0, py * 15.0 + 1.0, 3.3, 3) - 1.0)
    body = smooth(0.62, 0.92, lace) * 0.75 + smooth(0.70, 0.95, fine) * 0.35
    shape = 1.0 - smooth(0.42, 0.97, r + wx * 0.45)
    dense = 1.0 - smooth(0.05, 0.62, r + wy * 0.5)
    alpha = np.clip(body * shape + dense * 0.72, 0.0, 1.0) * (1.0 - smooth(0.9, 0.99, r))
    tone = np.clip(0.86 + (lace - 0.6) * 0.3, 0.7, 1.0)
    img = np.zeros((size, size, 4), dtype=np.float32)
    img[..., 0] = tone * 0.95
    img[..., 1] = tone * 0.985
    img[..., 2] = tone
    img[..., 3] = alpha
    return img


def write_png(path, img):
    h, w = img.shape[:2]
    data = (np.clip(img, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    rows = np.zeros((h, w * data.shape[2] + 1), dtype=np.uint8)
    rows[:, 1:] = data.reshape(h, -1)

    def chunk(tag, body):
        return struct.pack('>I', len(body)) + tag + body + struct.pack('>I', zlib.crc32(tag + body) & 0xffffffff)
    kind = 6 if data.shape[2] == 4 else 2
    blob = b'\x89PNG\r\n\x1a\n'
    blob += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, kind, 0, 0, 0))
    blob += chunk(b'IDAT', zlib.compress(rows.tobytes(), 6))
    blob += chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(blob)


def over_sky(img):
    a = img[..., 3:4]
    sky = np.array([0.42, 0.58, 0.78], dtype=np.float32)
    return img[..., :3] * a + sky * (1.0 - a)


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    fire = flipbook(2048, fireball_frame)
    write_png(os.path.join(OUT, 'fx_fireball_8x8.png'), fire)
    smoke = flipbook(1024, smoke_frame)
    write_png(os.path.join(OUT, 'fx_smoke_8x8.png'), smoke)
    write_png(os.path.join(OUT, 'fx_trail.png'), trail(64))
    band = ribbon(512, 256)
    write_png(os.path.join(OUT, 'fx_ribbon.png'), band)
    foam_img = foam(512)
    write_png(os.path.join(OUT, 'fx_foam.png'), foam_img)
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        write_png(os.path.join(PREVIEW, 'preview_fireball.png'), over_sky(fire)[::2, ::2])
        write_png(os.path.join(PREVIEW, 'preview_smoke.png'), over_sky(smoke))
        write_png(os.path.join(PREVIEW, 'preview_ribbon.png'), over_sky(np.concatenate([band, band], axis=1)))
        write_png(os.path.join(PREVIEW, 'preview_foam.png'), over_sky(foam_img))
    print('done', flush=True)
