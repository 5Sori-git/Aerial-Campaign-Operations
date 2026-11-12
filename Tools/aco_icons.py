# ACO HUD 라인 아이콘 생성기 (표준 라이브러리만). 좌표는 0..100 단위(y 아래로), 흰색 + 알파(안티앨리어싱).
import math, os, struct, sys, zlib

OUT = sys.argv[1]
SHEET = sys.argv[2]
SIZE = 256
W = 6.0  # 기본 선 두께(단위)


class Canvas:
    def __init__(self, w, h, k, ox=0.0, oy=0.0, buf=None):
        self.w, self.h, self.k, self.ox, self.oy = w, h, k, ox, oy
        self.a = buf if buf is not None else [0.0] * (w * h)

    def region(self, x0, y0, x1, y1, f):
        k = self.k
        ix0 = max(0, int(self.ox + x0 * k) - 2); ix1 = min(self.w - 1, int(self.ox + x1 * k) + 2)
        iy0 = max(0, int(self.oy + y0 * k) - 2); iy1 = min(self.h - 1, int(self.oy + y1 * k) + 2)
        a = self.a
        for py in range(iy0, iy1 + 1):
            uy = (py + 0.5 - self.oy) / k
            row = py * self.w
            for px in range(ix0, ix1 + 1):
                ux = (px + 0.5 - self.ox) / k
                c = f(ux, uy)
                if c > 0.0:
                    i = row + px
                    if c > a[i]:
                        a[i] = c if c < 1.0 else 1.0

    def cov(self, signed):  # signed distance(단위, 안 = 음수) → 덮임
        c = 0.5 - signed * self.k
        return 0.0 if c <= 0.0 else (1.0 if c >= 1.0 else c)


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    l2 = dx * dx + dy * dy
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / l2))
    qx, qy = ax + t * dx - px, ay + t * dy - py
    return math.sqrt(qx * qx + qy * qy)


def seg(c, a, b, w=W):
    hw = w * 0.5
    c.region(min(a[0], b[0]) - hw, min(a[1], b[1]) - hw, max(a[0], b[0]) + hw, max(a[1], b[1]) + hw,
             lambda x, y: c.cov(seg_dist(x, y, a[0], a[1], b[0], b[1]) - hw))


def poly(c, pts, w=W, closed=True):
    n = len(pts)
    for i in range(n if closed else n - 1):
        seg(c, pts[i], pts[(i + 1) % n], w)


def fill_poly(c, pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    n = len(pts)

    def f(x, y):
        inside = False
        d = 1e9
        j = n - 1
        for i in range(n):
            xi, yi = pts[i]; xj, yj = pts[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                inside = not inside
            dd = seg_dist(x, y, xi, yi, xj, yj)
            if dd < d: d = dd
            j = i
        return c.cov(-d if inside else d)
    c.region(min(xs), min(ys), max(xs), max(ys), f)


def fill_circle(c, cx, cy, r):
    c.region(cx - r, cy - r, cx + r, cy + r, lambda x, y: c.cov(math.hypot(x - cx, y - cy) - r))


def arc_pts(cx, cy, r, a0, a1, step=3.0):
    n = max(2, int(abs(a1 - a0) / step) + 1)
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / (n - 1))),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / (n - 1)))) for i in range(n)]


def arc(c, cx, cy, r, a0, a1, w=W):
    poly(c, arc_pts(cx, cy, r, a0, a1), w, closed=False)


def circle(c, cx, cy, r, w=W):
    poly(c, arc_pts(cx, cy, r, 0, 360, 4.0)[:-1], w, closed=True)


def head(c, tip, ang, size=13.0, spread=30.0):
    """채운 화살촉: tip에서 ang(도, 진행 방향) 쪽을 가리킨다."""
    a = math.radians(ang)
    b1 = math.radians(ang + 180 - spread); b2 = math.radians(ang + 180 + spread)
    fill_poly(c, [tip, (tip[0] + size * math.cos(b1), tip[1] + size * math.sin(b1)),
                  (tip[0] + size * math.cos(b2), tip[1] + size * math.sin(b2))])


def xform(pts, cx, cy, ang, s=1.0):
    a = math.radians(ang); ca, sa = math.cos(a), math.sin(a)
    return [(cx + s * (x * ca - y * sa), cy + s * (x * sa + y * ca)) for x, y in pts]


def missile_pts(L, bw, nl, fw, fl):
    h = L * 0.5
    return [(-h, -bw - fw), (-h + fl, -bw), (h - nl, -bw), (h, 0), (h - nl, bw), (-h + fl, bw), (-h, bw + fw)]


def missile(c, cx, cy, ang, L, bw, nl, fw, fl, fill=False, w=W):
    pts = xform(missile_pts(L, bw, nl, fw, fl), cx, cy, ang)
    if fill: fill_poly(c, pts)
    else: poly(c, pts, w)


JET = [(50, 6), (56, 24), (57, 42), (90, 62), (90, 70), (57, 63), (56, 78), (70, 88), (70, 94), (50, 90),
       (30, 94), (30, 88), (44, 78), (43, 63), (10, 70), (10, 62), (43, 42), (44, 24)]


def jet(c, cx, cy, s, fill=False, w=W):
    pts = [(cx + (x - 50) * s, cy + (y - 50) * s) for x, y in JET]
    if fill: fill_poly(c, pts)
    else: poly(c, pts, w)


def bomb(c, cx, cy, s=1.0, w=W):
    # 기수가 아래. 몸통 타원 + 꼬리 상자.
    body = [(cx + 14 * s * math.cos(math.radians(t)), cy + 6 * s + 28 * s * math.sin(math.radians(t))) for t in range(0, 360, 8)]
    poly(c, body, w)
    poly(c, [(cx - 12 * s, cy - 40 * s), (cx + 12 * s, cy - 40 * s), (cx + 12 * s, cy - 30 * s), (cx - 12 * s, cy - 30 * s)], w)
    seg(c, (cx - 6 * s, cy - 30 * s), (cx - 7 * s, cy - 20 * s), w)
    seg(c, (cx + 6 * s, cy - 30 * s), (cx + 7 * s, cy - 20 * s), w)


def rrect(c, x0, y0, x1, y1, r, w=W):
    pts = (arc_pts(x1 - r, y0 + r, r, 270, 360) + arc_pts(x1 - r, y1 - r, r, 0, 90)
           + arc_pts(x0 + r, y1 - r, r, 90, 180) + arc_pts(x0 + r, y0 + r, r, 180, 270))
    poly(c, pts, w)


# ───────────── 아이콘 ─────────────

def i_hull(c):
    poly(c, [(50, 8), (84, 20), (82, 52), (70, 72), (50, 90), (30, 72), (18, 52), (16, 20)])
    seg(c, (50, 34), (50, 66)); seg(c, (34, 50), (66, 50))


def i_mobility(c):
    arc(c, 50, 50, 32, 205, 335); head(c, (50 + 32 * math.cos(math.radians(345)), 50 + 32 * math.sin(math.radians(345))), 345 + 90)
    arc(c, 50, 50, 32, 25, 155); head(c, (50 + 32 * math.cos(math.radians(165)), 50 + 32 * math.sin(math.radians(165))), 165 + 90)
    jet(c, 50, 50, 0.32, fill=True)


def i_engine(c):
    poly(c, [(12, 34), (42, 28), (42, 72), (12, 66)])
    poly(c, [(50, 30), (92, 50), (50, 70)])
    fill_poly(c, [(50, 41), (73, 50), (50, 59)])


def i_fuel(c):
    cx, cy, r = 50, 60, 26
    t = math.degrees(math.asin(r / 50.0))
    a0, a1 = -90 + (90 - t), -90 - (90 - t) + 360
    poly(c, [(50, 10)] + arc_pts(cx, cy, r, a0, a1), closed=True)
    arc(c, cx, cy, 14, 105, 165, 5)


def i_stability(c):
    poly(c, [(10, 74), (14, 68), (24, 63), (40, 61), (56, 62), (72, 66), (90, 75),
             (72, 77), (56, 78), (40, 79), (24, 79), (14, 78)])
    for x, top in ((28, 30), (50, 20), (72, 30)):
        seg(c, (x, 54), (x, top + 8)); head(c, (x, top - 2), 270, 14)


def i_damage(c):
    pts = []
    for i in range(16):
        r = 44 if i % 2 == 0 else 20
        a = math.radians(-90 + i * 22.5)
        pts.append((50 + r * math.cos(a), 50 + r * math.sin(a)))
    poly(c, pts)
    fill_circle(c, 50, 50, 6)


def i_ammo(c):
    for x in (20, 40, 60):
        missile(c, x, 50, -90, 80, 5, 14, 5, 12, fill=True)
    seg(c, (82, 38), (82, 62)); seg(c, (70, 50), (94, 50))


def i_turn(c):
    seg(c, (28, 90), (28, 46))
    arc(c, 50, 46, 22, 180, 360)
    seg(c, (72, 46), (72, 64))
    head(c, (72, 82), 90, 15)


def i_lock_speed(c):
    circle(c, 62, 50, 24)
    seg(c, (62, 16), (62, 30)); seg(c, (62, 70), (62, 84)); seg(c, (82, 50), (96, 50)); seg(c, (28, 50), (42, 50))
    fill_circle(c, 62, 50, 4.5)
    seg(c, (4, 34), (22, 34)); seg(c, (10, 66), (22, 66))


def i_lock_range(c):
    fill_circle(c, 50, 88, 6)
    for r in (26, 48, 70):
        arc(c, 50, 88, r, 232, 308)


def i_seeker(c):
    ax, ay = 50, 88
    for a in (232, 308):
        seg(c, (ax, ay), (ax + 76 * math.cos(math.radians(a)), ay + 76 * math.sin(math.radians(a))))
    arc(c, ax, ay, 50, 246, 294)
    head(c, (ax + 50 * math.cos(math.radians(238)), ay + 50 * math.sin(math.radians(238))), 238 - 90, 13)
    head(c, (ax + 50 * math.cos(math.radians(302)), ay + 50 * math.sin(math.radians(302))), 302 + 90, 13)
    fill_circle(c, ax, ay, 6)


def i_blast(c):
    fill_circle(c, 50, 50, 9)
    circle(c, 50, 50, 22)
    for i in range(4):
        m = 45 + i * 90
        arc(c, 50, 50, 36, m - 16, m + 16)
    for a in (0, 90, 180, 270):
        head(c, (50 + 48 * math.cos(math.radians(a)), 50 + 48 * math.sin(math.radians(a))), a, 12)


def i_reload(c):
    arc(c, 50, 50, 36, -60, 230)
    head(c, (50 + 36 * math.cos(math.radians(-60)), 50 + 36 * math.sin(math.radians(-60))), -60 + 90 + 180, 15)
    missile(c, 50, 52, -90, 46, 5, 11, 6, 9, fill=True)


def i_tab_airframe(c):
    jet(c, 50, 50, 1.0, w=5)


def i_tab_missile(c):
    missile(c, 50, 50, -45, 92, 7, 18, 9, 16)


def i_tab_sp_aam(c):
    missile(c, 40, 60, -45, 70, 6, 14, 8, 13)
    jet(c, 80, 20, 0.30, fill=True)


def i_tab_sp_agm(c):
    missile(c, 40, 40, 45, 66, 6, 14, 8, 13)
    poly(c, [(70, 70), (88, 70), (88, 88), (70, 88)], 5)
    seg(c, (6, 94), (94, 94), 5)


def i_tab_sp_bomb(c):
    bomb(c, 50, 50, 1.0)


def i_w_4aam(c):
    for i, x in enumerate((17, 39, 61, 83)):
        missile(c, x, 54 if i % 2 == 0 else 46, -90, 74, 4.5, 13, 5, 11, fill=True)


def i_w_ugb(c):
    bomb(c, 50, 60, 0.85)
    for x in (40, 50, 60):
        seg(c, (x, 4), (x, 16), 4)


def i_w_rkt(c):
    rrect(c, 8, 30, 64, 70, 8)
    for y in (40, 50, 60):
        seg(c, (64, y), (80, y), 4)
        fill_poly(c, [(78, y - 4.5), (92, y), (78, y + 4.5)])


def dashed(c, a, b, dash=7.0, gap=5.0, w=5.0):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    if L == 0: return
    ux, uy = dx / L, dy / L
    t = 0.0
    while t < L:
        e = min(t + dash, L)
        seg(c, (a[0] + ux * t, a[1] + uy * t), (a[0] + ux * e, a[1] + uy * e), w)
        t = e + gap


def dashed_arc_to(c, cx, cy, r, a0, a1, step=8.0, w=4.5):
    # 점선 호: 끝점(a1)부터 점·빈칸을 세어 끝이 늘 점으로 끝난다(표적 상자에 정확히 닿게).
    pts = arc_pts(cx, cy, r, a0, a1, step)
    pts.reverse()
    for i in range(0, len(pts) - 1, 2):
        seg(c, pts[i], pts[i + 1], w)


def target_box_below(c, x, y, size=20.0, w=5.0):
    # 위 변 가운데가 (x, y)인 표적 상자 — 점선이 위에서 수직으로 내려와 여기서 끝난다.
    h = size * 0.5
    poly(c, [(x - h, y), (x + h, y), (x + h, y + size), (x - h, y + size)], w)


def i_w_lgb(c):
    # 유도 폭탄: 폭탄 + 휘어 내려가는 점선(폭탄 밑에서 시작 — 호 중심이 폭탄 바로 아래라 끝에서 수직) → 위 변 가운데에 닿는 표적 상자
    bomb(c, 26, 40, 0.6)
    dashed_arc_to(c, 26, 76, 46, 270, 360)
    target_box_below(c, 72, 76)


def i_w_lgr(c):
    # 유도 로켓: 로켓 포드 → 오른쪽으로 나가 휘어 내려가는 점선(포드 출구에서 수평으로 시작, 끝에서 수직) → 위 변 가운데에 닿는 표적 상자
    rrect(c, 6, 18, 42, 42, 6)
    for y in (26, 34):
        seg(c, (42, y), (50, y), 4)
    dashed_arc_to(c, 50, 66, 36, 270, 360)
    target_box_below(c, 86, 66)


def i_w_4agm(c):
    for i, x in enumerate((17, 39, 61, 83)):
        missile(c, x, 40 if i % 2 == 0 else 46, 90, 62, 4.5, 12, 5, 10, fill=True)
    seg(c, (6, 92), (94, 92), 5)


def i_w_lagm(c):
    missile(c, 26, 26, 45, 46, 5.5, 12, 7, 10)
    dashed(c, (46, 46), (70, 70))
    poly(c, [(72, 72), (90, 72), (90, 90), (72, 90)], 5)


def i_w_scm(c):
    # 클러스터: 내려오던 탄체가 터져 자탄이 지면 위로 부채꼴로 퍼진다
    missile(c, 20, 14, 40, 34, 5.5, 12, 7, 10)
    for x, y in ((46, 48), (60, 44), (72, 52), (40, 62), (54, 62), (68, 66), (84, 62),
                 (32, 76), (46, 78), (60, 80), (74, 80), (88, 76)):
        seg(c, (x, y), (x + 0.1, y), 6.5)
    seg(c, (6, 92), (94, 92), 5)


def i_w_ecm(c):
    # 전자전: 기체에서 양쪽으로 퍼지는 전파
    jet(c, 50, 50, 0.34, True)
    for r in (26, 36, 46):
        for side in (-1, 1):
            pts = [(50 + side * r * math.cos(math.radians(a)), 50 + r * math.sin(math.radians(a))) for a in range(-34, 35, 8)]
            for p0, p1 in zip(pts, pts[1:]):
                seg(c, p0, p1, 5)


def i_w_lasm(c):
    # 대함: 파도 바로 위로 깔려 가는 미사일 → 함선
    missile(c, 30, 56, 0, 40, 5.5, 12, 7, 10)
    dashed(c, (52, 56), (64, 56))
    fill_poly(c, [(66, 60), (96, 60), (90, 70), (70, 70)])
    seg(c, (78, 60), (78, 48), 4.5)
    seg(c, (74, 52), (84, 52), 4.5)
    pts = []
    for i in range(25):
        x = 4 + i * 3.8
        pts.append((x, 80 + 3.5 * math.sin(i * 1.1)))
    for i in range(len(pts) - 1):
        seg(c, pts[i], pts[i + 1], 4)


def i_w_laam(c):
    missile(c, 24, 50, 0, 42, 5.5, 12, 7, 10)
    dashed(c, (50, 50), (74, 50))
    jet(c, 87, 50, 0.24, fill=True)


def i_w_sasm(c):
    # 근거리 대공 제압: 미사일 → 큰 근접 신관 원(점선) 안의 적기 두 대
    missile(c, 22, 78, -45, 42, 5.5, 12, 7, 10)
    for i in range(12):
        a = i * 30
        arc(c, 64, 36, 30, a, a + 16, 4.5)
    jet(c, 56, 30, 0.22, fill=True)
    jet(c, 74, 44, 0.22, fill=True)


def i_w_qaam(c):
    pts = arc_pts(52, 82, 34, 180, 270, 6.0)
    for i in range(0, len(pts) - 1, 2):
        seg(c, pts[i], pts[i + 1], 5)
    missile(c, 72, 48, 0, 40, 5.5, 12, 7, 10)


def i_w_hpaa(c):
    missile(c, 42, 58, -45, 62, 9, 15, 9, 12, fill=True)
    pts = []
    for i in range(12):
        r = 15 if i % 2 == 0 else 7
        a = math.radians(-90 + i * 30)
        pts.append((80 + r * math.cos(a), 20 + r * math.sin(a)))
    poly(c, pts, 4.5)


def i_w_hvaa(c):
    missile(c, 60, 50, 0, 62, 6, 14, 8, 12)
    seg(c, (6, 36), (24, 36), 5); seg(c, (2, 50), (24, 50), 5); seg(c, (6, 64), (24, 64), 5)


def i_w_flak(c):
    # 무유도 대공 로켓: 로켓 세 발 → 적기 곁의 근접 폭발(별)
    for i, y in enumerate((30, 50, 70)):
        x = 14 + (i % 2) * 8
        seg(c, (x, y), (x + 26, y), 4)
        fill_poly(c, [(x + 24, y - 4.5), (x + 36, y), (x + 24, y + 4.5)])
    jet(c, 80, 34, 0.22, fill=True)
    pts = []
    for i in range(16):
        r = 16 if i % 2 == 0 else 8
        a = math.radians(i * 22.5)
        pts.append((68 + r * math.cos(a), 62 + r * math.sin(a)))
    poly(c, pts, 4)


def i_w_ccaa(c):
    # 대응책 대응 시커(CCAA): 미사일 → 영상 틀(모서리 괄호) 안의 적기, 아래로 떨어지는 플레어는 무시
    missile(c, 24, 50, 0, 40, 5.5, 12, 7, 10)
    x0, y0, x1, y1, k = 58, 28, 94, 64, 9
    for (cx, cy, dx, dy) in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):
        seg(c, (cx, cy), (cx + dx * k, cy), 4.5)
        seg(c, (cx, cy), (cx, cy + dy * k), 4.5)
    jet(c, 76, 46, 0.22, fill=True)
    for (fx, fy) in ((64, 80), (76, 88), (88, 80)):
        fill_circle(c, fx, fy, 3.2)


def i_w_adam(c):
    # 투하 후 추적, 큰 폭발: 점선으로 떨어지는 궤적 → 미사일 → 큰 점선 원 안의 적기들
    dashed(c, (8, 14), (16, 30), 5.0, 4.0, 4.0)
    missile(c, 30, 42, 25, 34, 5, 10, 6, 8, fill=True)
    for i in range(14):
        a = i * (360 / 14)
        arc(c, 68, 58, 28, a, a + 14, 4.5)
    jet(c, 60, 52, 0.2, fill=True)
    jet(c, 78, 66, 0.2, fill=True)


def i_w_mmp(c):
    # 마이크로미사일 포드: 포드 아래에서 작은 미사일들이 비스듬히 사출돼 부채꼴로 여러 적에게 갈라진다
    rrect(c, 8, 10, 60, 28, 7)
    for x in (18, 30, 42, 54):
        fill_circle(c, x, 28, 3.2)
    for (x0, y0, ang) in ((16, 38, 80), (30, 42, 62), (44, 44, 44), (58, 42, 26)):
        dashed(c, (x0 - 6 * math.cos(math.radians(ang)), y0 - 6 * math.sin(math.radians(ang))),
               (x0 - 2 * math.cos(math.radians(ang)), y0 - 2 * math.sin(math.radians(ang))), 3.0, 3.0, 3.5)
        missile(c, x0 + 9 * math.cos(math.radians(ang)), y0 + 9 * math.sin(math.radians(ang)), ang, 18, 2.6, 5, 3.5, 5, fill=True)
    jet(c, 30, 84, 0.16, fill=True)
    jet(c, 62, 80, 0.16, fill=True)
    jet(c, 86, 62, 0.16, fill=True)


def i_w_rgp(c):
    # 레일건 포드: 포드(번개 마크) → 가는 배럴(위아래 레일) → 곧은 고속탄 화살표(속도선)
    rrect(c, 6, 34, 46, 66, 7)
    fill_poly(c, [(28, 38), (18, 52), (26, 52), (22, 62), (34, 47), (26, 47), (30, 38)])
    seg(c, (46, 45), (62, 45), 4.5)
    seg(c, (46, 55), (62, 55), 4.5)
    seg(c, (68, 50), (86, 50), 5)
    fill_poly(c, [(84, 43), (96, 50), (84, 57)])
    for (x, y, l) in ((68, 38, 14), (70, 62, 12)):
        seg(c, (x, y), (x + l, y), 3)


def i_w_none(c):
    circle(c, 50, 50, 38)
    seg(c, (23, 77), (77, 23))


def i_lock(c):
    arc(c, 50, 42, 18, 180, 360)
    seg(c, (32, 42), (32, 50)); seg(c, (68, 42), (68, 50))
    rrect(c, 22, 50, 78, 90, 7)
    fill_circle(c, 50, 66, 6)
    seg(c, (50, 66), (50, 80), 5)


# 지원 호출(서포트) 아이콘 — 인게임 드론을 위에서 본 모양(코를 위로, 오른쪽 반을 적어 좌우 대칭으로 닫는다)
TANKER_HALF = [(50, 4), (53, 14), (55, 28), (90, 56), (90, 62), (58, 62), (54, 67), (50, 67)]   # 급유 드론: 뾰족한 기수 + 넓은 삼각 날개(뒷전 곧게)
GUARD_HALF = [(50, 4), (54, 14), (55, 24), (55, 46), (80, 58), (80, 66), (55, 64), (55, 82), (63, 89), (63, 95), (55, 93), (50, 96)]   # 가디언 드론: 긴 동체 + 가운데 후퇴익 + 꼬리 작은 핀


def sym(half):
    return half + [(100 - x, y) for x, y in reversed(half[1:-1])]


def shape(c, half, cx, cy, s, fill=False, w=W):
    pts = [(cx + (x - 50) * s, cy + (y - 50) * s) for x, y in sym(half)]
    if fill: fill_poly(c, pts)
    else: poly(c, pts, w)


def i_sup_strike(c):
    # 벙커버스터: 떨어지는 폭탄 + 땅에 묻힌 벙커(반구 + 입구) + 충격선
    bomb(c, 50, 30, 0.6)
    seg(c, (8, 88), (92, 88))
    arc(c, 50, 88, 22, 180, 360)
    poly(c, [(43, 88), (43, 79), (57, 79), (57, 88)], 4.5, closed=False)
    for x0, x1 in ((22, 14), (78, 86)):
        seg(c, (x0, 64), (x1, 56), 4.5)
    seg(c, (26, 74), (16, 72), 4.5); seg(c, (74, 74), (84, 72), 4.5)


def i_sup_tanker(c):
    # 급유 드론: 위에서 본 드론 + 꼬리에서 늘어진 급유 호스(점선) + 작은 드로그(사다리꼴)
    shape(c, TANKER_HALF, 50, 41, 0.74)
    dashed(c, (50, 55), (50, 75), 5.0, 4.0, 4.0)
    poly(c, [(45, 76), (55, 76), (60, 88), (40, 88)], 4.5)


def i_sup_guardian(c):
    # 가디언 드론: 방패 안의 드론 + 방패에 막혀 튕기는 미사일(불꽃)
    poly(c, [(46, 14), (78, 24), (76, 54), (66, 72), (46, 88), (26, 72), (16, 54), (14, 24)])
    shape(c, GUARD_HALF, 46, 50, 0.5, fill=True)
    missile(c, 86, 13, 135, 18, 2.8, 5, 3.0, 4, fill=True)
    for ang in (300, 345, 30):
        r0, r1 = 4, 10
        cx, cy = 80, 20
        a = math.radians(ang)
        seg(c, (cx + r0 * math.cos(a), cy + r0 * math.sin(a)), (cx + r1 * math.cos(a), cy + r1 * math.sin(a)), 3.5)


def i_cm_pod(c):
    # 대응탄 포드: 원통 포드 + 아래로 떨어지는 플레어 셋(꼬리 곡선)
    rrect(c, 20, 18, 80, 40, 10)
    seg(c, (30, 29), (70, 29), 4)
    for x, dx in ((32, -10), (50, 0), (68, 10)):
        seg(c, (x, 46), (x + dx * 0.6, 64), 4)
        fill_circle(c, x + dx, 76, 7)


def i_tab_supply(c):
    # 보급 상자: 상자 + 낙하산 줄
    rrect(c, 24, 50, 76, 88, 5)
    seg(c, (24, 66), (76, 66), 4); seg(c, (50, 50), (50, 88), 4)
    arc(c, 50, 30, 26, 180, 360)
    seg(c, (24, 30), (36, 50), 4); seg(c, (76, 30), (64, 50), 4)


def i_credits(c):
    # 동전 두 개: 뒤 동전은 앞 동전에 가려지지 않는 테두리만, 앞 동전은 안쪽 고리 + 마름모.
    arc(c, 60, 40, 30, 200, 425)
    circle(c, 42, 58, 30)
    circle(c, 42, 58, 20, 3)
    fill_poly(c, [(42, 48), (50, 58), (42, 68), (34, 58)])


def i_hangar(c):
    # 격납고: 둥근 지붕 + 기둥 + 바닥, 안에 기체
    arc(c, 50, 58, 42, 180, 360)
    seg(c, (8, 58), (8, 88)); seg(c, (92, 58), (92, 88)); seg(c, (2, 88), (98, 88))
    jet(c, 50, 62, 0.5, False, 4.5)


def i_upgrade(c):
    # 강화: 위로 겹친 갈매기 둘 + 받침
    poly(c, [(22, 50), (50, 22), (78, 50)], 8, False)
    poly(c, [(22, 72), (50, 44), (78, 72)], 8, False)
    seg(c, (26, 88), (74, 88), 7)


def i_roster(c):
    # 출격 명단: 가운데 조종사 + 양옆 윙맨
    circle(c, 50, 34, 15)
    arc(c, 50, 86, 30, 180, 360)
    seg(c, (20, 86), (80, 86))
    circle(c, 17, 46, 9, 5); circle(c, 83, 46, 9, 5)
    arc(c, 15, 84, 18, 215, 330, 5); arc(c, 85, 84, 18, 210, 325, 5)


def i_ready(c):
    # 준비: 원 안의 체크
    circle(c, 50, 50, 40)
    poly(c, [(30, 52), (45, 67), (72, 34)], 9, False)


def i_launch(c):
    # 출격: 위로 치솟는 기체 + 뒤쪽 궤적
    jet(c, 50, 36, 0.62, True)
    dashed(c, (50, 92), (50, 68), 7.0, 5.0, 6.0)
    dashed(c, (30, 92), (30, 76), 6.0, 5.0, 5.0)
    dashed(c, (70, 92), (70, 76), 6.0, 5.0, 5.0)


def i_operation(c):
    # 작전: 접힌 지도 + 위치 표시
    poly(c, [(10, 28), (36, 18), (64, 28), (90, 18), (90, 76), (64, 86), (36, 76), (10, 86)])
    seg(c, (36, 18), (36, 76), 4); seg(c, (64, 28), (64, 86), 4)
    circle(c, 50, 46, 9, 5)


def i_phases(c):
    # 페이즈: 깃발
    seg(c, (26, 12), (26, 90), 7)
    fill_poly(c, [(26, 16), (80, 30), (26, 48)])


def mountains(c, y=74):
    poly(c, [(8, y), (36, 26), (52, 50), (66, 34), (92, y)], W, True)


def i_terrain(c):
    # 지형: 산 + 해
    mountains(c, 80)
    circle(c, 76, 22, 9, 5)


def i_reroll(c):
    # 새 지형: 산을 두른 순환 화살표
    poly(c, [(26, 66), (42, 40), (52, 54), (60, 44), (74, 66)], 5, True)
    arc(c, 50, 54, 40, 200, 340, 6)
    head(c, (87, 39), 72, 15)
    arc(c, 50, 54, 40, 20, 160, 6)
    head(c, (13, 69), 252, 15)


def i_leave(c):
    # 나가기: 문 + 밖으로 나가는 화살표
    poly(c, [(58, 30), (58, 14), (14, 14), (14, 86), (58, 86), (58, 70)], W, False)
    seg(c, (36, 50), (86, 50), 7)
    head(c, (90, 50), 0, 18)


def i_power(c):
    # 전원
    arc(c, 50, 54, 34, 300, 600)
    seg(c, (50, 12), (50, 52), 8)


def i_screen(c):
    # 화면 해상도: 모니터 + 받침
    rrect(c, 8, 16, 92, 70, 5)
    seg(c, (50, 70), (50, 86)); seg(c, (32, 86), (68, 86))
    poly(c, [(34, 32), (22, 43), (34, 54)], 5, False)
    poly(c, [(66, 32), (78, 43), (66, 54)], 5, False)


def i_target(c):
    # 대상: 조준 틀 + 기체
    for x, y, sx, sy in ((10, 10, 1, 1), (90, 10, -1, 1), (10, 90, 1, -1), (90, 90, -1, -1)):
        seg(c, (x, y), (x + 24 * sx, y)); seg(c, (x, y), (x, y + 24 * sy))
    jet(c, 50, 50, 0.46, True)


def i_camera(c):
    # 컷: 영화 카메라
    rrect(c, 8, 40, 66, 84, 6)
    circle(c, 24, 24, 13, 5); circle(c, 52, 24, 13, 5)
    poly(c, [(66, 54), (92, 42), (92, 82), (66, 70)], 6, True)


def i_score(c):
    # 점수: 우승컵
    poly(c, [(28, 14), (72, 14), (68, 46), (58, 58), (42, 58), (32, 46)], W, True)
    arc(c, 28, 28, 14, 90, 270, 5); arc(c, 72, 28, 14, 270, 450, 5)
    seg(c, (50, 58), (50, 76), 7); seg(c, (30, 86), (70, 86), 8); seg(c, (38, 76), (62, 76), 6)


def eagle_wing(c, s, feathers, span):
    # 펼친 날개(s = 1 오른쪽, -1 왼쪽): 끝이 깃털로 갈라진다
    x = lambda v: 50 + s * v
    pts = [(x(8), 38), (x(span * 0.5), 24 - span * 0.1), (x(span), 30 - span * 0.1)]
    for i in range(feathers):
        t = i / float(feathers)
        fx = span - t * (span - 12) * 0.92
        fy = 58 + t * 22 - span * 0.1
        pts.append((x(fx), fy))
        pts.append((x(fx - span / feathers * 0.42), fy - 12))
    pts.append((x(8), 58))
    fill_poly(c, pts)


def eagle_body(c):
    fill_poly(c, [(50, 22), (58, 28), (58, 40), (61, 62), (66, 84), (56, 78), (50, 90), (44, 78), (34, 84), (39, 62), (42, 40), (42, 28)])
    fill_poly(c, [(46, 25), (50, 12), (58, 26)])


# 난이도(페이즈 수): 병아리 → 참새 → 매 → 독수리 → 왕관 쓴 독수리. 색은 게임 진행이 입힌다.
def i_phase_1(c):
    # 병아리: 달걀 껍데기에서 나온 머리
    arc(c, 50, 42, 26, 180, 360)
    seg(c, (24, 42), (24, 54)); seg(c, (76, 42), (76, 54))
    fill_circle(c, 41, 38, 4.5); fill_circle(c, 59, 38, 4.5)
    fill_poly(c, [(44, 46), (56, 46), (50, 55)])
    poly(c, [(16, 54), (28, 64), (38, 54), (50, 64), (62, 54), (72, 64), (84, 54)], 5.5, False)
    arc(c, 50, 54, 34, 15, 165)
    seg(c, (16, 54), (17, 62), 5.5); seg(c, (84, 54), (83, 62), 5.5)


def i_phase_2(c):
    # 참새: 동그란 몸 + 접은 날개 + 치켜든 꼬리, 나뭇가지에 앉아 있다
    circle(c, 44, 46, 27, 6)
    fill_poly(c, [(18, 36), (5, 41), (18, 46)])
    fill_circle(c, 32, 37, 4)
    poly(c, [(40, 44), (60, 40), (67, 52), (50, 62)], 5)
    seg(c, (68, 38), (92, 18), 7); seg(c, (70, 46), (94, 30), 7)
    seg(c, (36, 72), (34, 86), 5); seg(c, (52, 72), (54, 86), 5)
    seg(c, (14, 87), (78, 87), 6)


def i_phase_3(c):
    # 매: 위에서 본 활공 — 넓은 날개와 부채 꼬리, 선으로만
    poly(c, [(46, 22), (50, 12), (54, 22), (56, 34), (72, 30), (97, 44), (84, 46), (90, 53), (76, 53), (79, 60), (58, 56),
             (63, 80), (50, 90), (37, 80), (42, 56), (21, 60), (24, 53), (10, 53), (16, 46), (3, 44), (28, 30), (44, 34)], 5.5)


def i_phase_4(c):
    # 독수리: 넓게 편 날개, 채움
    for s in (1, -1): eagle_wing(c, s, 4, 44)
    eagle_body(c)


def i_phase_5(c):
    # 왕관 쓴 독수리
    for s in (1, -1): eagle_wing(c, s, 5, 46)
    eagle_body(c)
    fill_poly(c, [(36, 14), (40, 2), (46, 10), (50, 0), (54, 10), (60, 2), (64, 14)])


def i_decal(c):
    # 데칼: 스프레이 캔 + 뿜는 점
    rrect(c, 18, 38, 58, 92, 6)
    rrect(c, 28, 24, 48, 38, 3)
    seg(c, (38, 24), (38, 14), 6)
    for x, y, r in ((62, 14, 4), (76, 10, 4), (90, 8, 4), (72, 24, 4), (86, 24, 4), (88, 40, 4)):
        fill_circle(c, x, y, r)


def i_download(c):
    # 불러오기: 받침으로 내려오는 화살표
    seg(c, (50, 10), (50, 56), 8)
    head(c, (50, 66), 90, 22)
    poly(c, [(14, 66), (14, 88), (86, 88), (86, 66)], 7, False)


def i_swap(c):
    # 데칼 1·2: 겹친 두 장
    rrect(c, 10, 10, 62, 62, 6)
    rrect(c, 38, 38, 90, 90, 6)


def i_size(c):
    # 크기: 대각선 양쪽 화살표
    seg(c, (24, 76), (76, 24), 7)
    head(c, (86, 14), -45, 22)
    head(c, (14, 86), 135, 22)


def i_rotate(c):
    # 회전
    arc(c, 50, 52, 34, 330, 590, 7)
    head(c, (82, 38), 90, 20)


def i_flip(c):
    # 뒤집기: 축 양쪽의 삼각형
    dashed(c, (50, 8), (50, 92), 9.0, 6.0, 5.0)
    poly(c, [(38, 24), (38, 76), (8, 76)], 6)
    fill_poly(c, [(62, 24), (62, 76), (92, 76)])


def i_mirror(c):
    # 대칭: 축 양쪽의 원
    dashed(c, (50, 8), (50, 92), 9.0, 6.0, 5.0)
    circle(c, 24, 50, 17, 6)
    fill_circle(c, 76, 50, 19)


def i_clear(c):
    # 지우기: 휴지통
    seg(c, (16, 26), (84, 26), 7); seg(c, (38, 14), (62, 14), 7)
    poly(c, [(26, 26), (32, 90), (68, 90), (74, 26)], 6, False)
    seg(c, (42, 40), (43, 76), 5); seg(c, (58, 40), (57, 76), 5)


def i_hide(c):
    # 숨기기: 눈 + 사선
    arc(c, 50, 84, 50, 225, 315, 6)
    arc(c, 50, 16, 50, 45, 135, 6)
    circle(c, 50, 50, 9, 6)
    seg(c, (16, 86), (84, 14), 8)


def i_gun_reset(c):
    # 총 되돌리기: 권총 모양을 두른 되돌림 화살표
    fill_poly(c, [(30, 38), (70, 38), (70, 50), (52, 50), (48, 70), (36, 70), (38, 50), (30, 50)])
    arc(c, 50, 52, 41, 200, 500, 6)
    head(c, (10, 44), 280, 18)


ICONS = [
    ("hull", i_hull), ("mobility", i_mobility), ("engine", i_engine), ("fuel", i_fuel), ("stability", i_stability),
    ("damage", i_damage), ("ammo", i_ammo), ("turn", i_turn), ("lock_speed", i_lock_speed), ("lock_range", i_lock_range),
    ("seeker", i_seeker), ("blast", i_blast), ("reload", i_reload),
    ("tab_airframe", i_tab_airframe), ("tab_missile", i_tab_missile), ("tab_sp_aam", i_tab_sp_aam),
    ("tab_sp_agm", i_tab_sp_agm), ("tab_sp_bomb", i_tab_sp_bomb),
    ("weapon_4aam", i_w_4aam), ("weapon_ugb", i_w_ugb), ("weapon_rkt", i_w_rkt), ("weapon_none", i_w_none),
    ("weapon_4agm", i_w_4agm), ("weapon_lagm", i_w_lagm), ("weapon_laam", i_w_laam), ("weapon_sasm", i_w_sasm),
    ("weapon_qaam", i_w_qaam), ("weapon_hpaa", i_w_hpaa), ("weapon_hvaa", i_w_hvaa),
    ("weapon_flak", i_w_flak), ("weapon_ccaa", i_w_ccaa), ("weapon_adam", i_w_adam), ("weapon_lasm", i_w_lasm),
    ("weapon_lgb", i_w_lgb), ("weapon_lgr", i_w_lgr), ("weapon_scm", i_w_scm), ("weapon_ecm", i_w_ecm), ("weapon_mmp", i_w_mmp), ("weapon_rgp", i_w_rgp),
    ("lock", i_lock), ("credits", i_credits), ("cm_pod", i_cm_pod), ("tab_supply", i_tab_supply),
    ("sup_strike", i_sup_strike), ("sup_tanker", i_sup_tanker), ("sup_guardian", i_sup_guardian),
    ("hangar", i_hangar), ("upgrade", i_upgrade), ("roster", i_roster), ("ready", i_ready), ("launch", i_launch),
    ("operation", i_operation), ("phases", i_phases), ("terrain", i_terrain), ("reroll", i_reroll), ("leave", i_leave),
    ("power", i_power), ("screen", i_screen), ("target", i_target), ("camera", i_camera), ("score", i_score),
    ("decal", i_decal), ("download", i_download), ("swap", i_swap), ("size", i_size), ("rotate", i_rotate), ("flip", i_flip),
    ("mirror", i_mirror), ("clear", i_clear), ("hide", i_hide), ("gun_reset", i_gun_reset),
    ("phase_1", i_phase_1), ("phase_2", i_phase_2), ("phase_3", i_phase_3), ("phase_4", i_phase_4), ("phase_5", i_phase_5),
]


def write_png(path, w, h, rows_rgba):
    raw = b"".join(b"\x00" + r for r in rows_rgba)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


os.makedirs(OUT, exist_ok=True)
cols, cell, pad = 6, 120, 12
rows = (len(ICONS) + cols - 1) // cols
sw, sh = cols * cell, rows * cell
sheet = Canvas(sw, sh, 1.0)
for idx, (name, fn) in enumerate(ICONS):
    c = Canvas(SIZE, SIZE, SIZE / 100.0)
    fn(c)
    rows_rgba = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            row += bytes((255, 255, 255, int(c.a[y * SIZE + x] * 255 + 0.5)))
        rows_rgba.append(bytes(row))
    write_png(os.path.join(OUT, "aco_icon_" + name + ".png"), SIZE, SIZE, rows_rgba)
    sc = Canvas(sw, sh, (cell - 2 * pad) / 100.0, (idx % cols) * cell + pad, (idx // cols) * cell + pad, sheet.a)
    fn(sc)
    print(name)

bg = (14, 38, 22); fg = (110, 255, 140)
rows_rgba = []
for y in range(sh):
    row = bytearray()
    for x in range(sw):
        a = sheet.a[y * sw + x]
        cx, cy = x % cell, y % cell
        b = (24, 60, 34) if (cx < 2 or cy < 2) else bg
        row += bytes((int(b[0] + (fg[0] - b[0]) * a), int(b[1] + (fg[1] - b[1]) * a), int(b[2] + (fg[2] - b[2]) * a), 255))
    rows_rgba.append(bytes(row))
write_png(SHEET, sw, sh, rows_rgba)
print("sheet", SHEET)
