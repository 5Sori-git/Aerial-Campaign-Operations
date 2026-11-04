# -*- coding: utf-8 -*-
"""CIWS 미사일 요격(구역 방어, 시간 방식) 시뮬레이션 — 표준 파이썬만.

ACO_Missile/ACO_AAGunSite에 넣을 규칙을 그대로 재현한다:
  - 미사일이 0.1초마다 주변 CIWS를 훑어 '덮는' CIWS(살아 있음, 지키는 레이어, 미사일이 요격 사거리 안, 표적이 방어 반경 안)를 고른다.
  - 매 프레임 맡은 CIWS 하나에만 InterceptTick: CIWS는 한 번에 한 발. 다른 미사일을 쏘는 중(0.15초 안에 틱)이면 거절 → 미사일은 다음 CIWS.
  - 필요 시간 = (쉬다가 처음이면 반응 시간 + 격추 시간) × (표적이 이 CIWS의 함선이 아니면 다른 함선 배수). 다 차면 요격.
  - 화면(클라이언트)마다 프레임 간격·미사일 갱신 순서·발사 이벤트 도착 시각이 다르다. 판정자(쏜 사람) 화면과 구경꾼 화면의 결과를 비교한다.
실행: python Tools/aco_ciws_sim.py
"""
import math, random

DT = 1.0 / 90.0
IC_HOLD = 0.15
IC_IDLE = 1.0
SCAN = 0.1


class Ciws:
    def __init__(self, pos, ship, rng=1200.0, radius=600.0, react=0.5, kill=0.5, other=2.0):
        self.pos, self.ship, self.rng, self.radius, self.react, self.kill, self.other = pos, ship, rng, radius, react, kill, other
        self.reset()

    def reset(self):
        self.cur = None
        self.last = -100.0
        self.prog = 0.0
        self.need = 0.0
        self.kills = 0

    def covers(self, mpos, tpos):
        return dist(self.pos, mpos) <= self.rng and dist(self.pos, tpos) <= self.radius

    def tick(self, m, now, dt, frame=0):
        if not self.covers(m.pos, m.tpos):
            if self.cur is m: self.cur = None
            return -1
        if self.cur is not m:
            if self.cur is not None and now - self.last < IC_HOLD: return -1
            idle = now - self.last > IC_IDLE
            self.cur = m
            self.prog = 0.0
            own = m.target_ship == self.ship
            self.need = ((self.react if idle else 0.0) + self.kill) * (1.0 if own else self.other)
        self.last = now
        self.prog += dt
        if self.prog < self.need: return 0
        self.cur = None
        self.kills += 1
        return 1


class Missile:
    def __init__(self, mid, start, tpos, target_ship, speed, t_launch, immune=False):
        self.id, self.pos, self.tpos, self.target_ship, self.speed, self.t0, self.immune = mid, list(start), tpos, target_ship, speed, t_launch, immune
        self.alive = False; self.done = None; self.site = None; self.near = []; self.scan = 0.0


def dist(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def run_client(sites, specs, latency, jitter, rnd):
    """specs: (id, start, tpos, ship, speed, launch time, immune). latency: 발사 이벤트 도착 지연(쏜 사람별). 결과 {id: 'hit'|'icp'}."""
    for s in sites: s.reset()
    ms = [Missile(*sp) for sp in specs]
    now = 0.0
    out = {}
    frame = 0
    while now < 30.0 and len(out) < len(ms):
        dt = DT * (1.0 + rnd.uniform(-jitter, jitter))
        now += dt
        frame += 1
        order = list(ms); rnd.shuffle(order)   # Udon 갱신 순서는 정해져 있지 않다
        for m in order:
            if m.id in out: continue
            arrive = m.t0 + latency.get(m.id, 0.0)
            if not m.alive:
                if now < arrive: continue
                m.alive = True
                # 늦게 받은 화면은 같은 발사 시각으로 맞추지 않는다(ACO 미사일은 받은 순간 발사 지점에서 출발) — 궤적은 같고 시작이 늦다
            # 이동
            d = [m.tpos[i] - m.pos[i] for i in range(3)]
            L = math.sqrt(sum(x * x for x in d))
            step = m.speed * dt
            if L <= step + 6.0:
                out[m.id] = 'hit'
                if m.site and m.site.cur is m: m.site.cur = None
                continue
            m.pos = [m.pos[i] + d[i] / L * step for i in range(3)]
            if m.immune: continue
            m.scan -= dt
            if m.scan <= 0:
                m.scan = SCAN
                m.near = [s for s in sites if s.covers(m.pos, m.tpos)]
            r = -1
            if m.site is not None:
                r = m.site.tick(m, now, dt, frame)
                if r < 0: m.site = None
            if r < 0:
                for s in m.near:
                    if s is m.site: continue
                    r = s.tick(m, now, dt, frame)
                    if r >= 0: m.site = s; break
            if r == 1:
                out[m.id] = 'icp'
    return out


def fleet():
    # 전함(0,0,0) CIWS 2 + 구축함 2척(500m 간격, 각 CIWS 1). 높이 15m.
    return [Ciws((10, 16, 4), 'BB'), Ciws((-10, 16, -14), 'BB'), Ciws((500, 12, -100), 'DD1'), Ciws((-500, 12, -100), 'DD2')]


SHIPS = {'BB': (0, 8, 0), 'DD1': (500, 6, -100), 'DD2': (-500, 6, -100)}


def salvo(n, ship, speed, shooter='A', t=0.0, rng=4000.0, bearing=0.0, immune=False, base=0):
    tp = SHIPS[ship]
    out = []
    for k in range(n):
        a = math.radians(bearing + (k - (n - 1) / 2) * 3.0)
        start = (tp[0] + rng * math.sin(a), 800.0, tp[2] + rng * math.cos(a))
        out.append((f'{shooter}{base + k}', start, tp, ship, speed, t + k * 0.0, immune))
    return out


def scenario(name, specs, shooters, runs=400, sites_fn=fleet):
    rnd = random.Random(7)
    leaks = []
    diverge = 0
    total = 0
    count_diff = 0
    for r in range(runs):
        sites = sites_fn()
        # 판정자 = 각 미사일을 쏜 사람. 쏜 사람 화면: 자기 미사일 지연 0, 남의 미사일 0.05~0.25초.
        views = {}
        for sh in shooters + ['X']:   # X = 구경꾼
            lat = {}
            for sp in specs:
                own = sp[0].startswith(sh)
                lat[sp[0]] = 0.0 if own else rnd.uniform(0.05, 0.25)
            views[sh] = run_client(sites, specs, lat, 0.3, rnd)
        # 결과 = 판정자 화면
        auth = {sp[0]: views[sp[0][0]][sp[0]] for sp in specs}
        leaks.append(sum(1 for v in auth.values() if v == 'hit'))
        for sp in specs:
            total += 1
            if views['X'][sp[0]] != auth[sp[0]]: diverge += 1
        # 구경꾼이 보는 '배에 꽂힌 수'가 실제(판정자 기준)와 다른가 — 똑같은 일제 발사에서 어느 한 발이 떨어지는지는 눈에 띄지 않는다
        if sum(1 for sp in specs if views['X'][sp[0]] == 'hit') != leaks[-1]: count_diff += 1
    avg = sum(leaks) / len(leaks)
    dist_ = {}
    for l in leaks: dist_[l] = dist_.get(l, 0) + 1
    hist = ' '.join(f'{k}발:{v * 100 // runs}%' for k, v in sorted(dist_.items()))
    print(f'{name:<44} 명중 평균 {avg:4.2f}/{len(specs)}  [{hist}]  구경꾼: 어느 발인지 다름 {diverge * 100.0 / max(total, 1):4.1f}%, 명중 수 다름 {count_diff * 100.0 / runs:4.1f}%')


def main():
    print('함대: 전함 CIWS 2 + 구축함 2척(500m, CIWS 1씩). CIWS 사거리 1.2km, 방어 반경 600m, 반응 0.5 + 격추 0.5초, 다른 함선 ×2')
    scenario('LAGM 1발 → 전함 (900m/s)', salvo(1, 'BB', 900), ['A'])
    scenario('4AGM 일제 → 전함', salvo(4, 'BB', 900), ['A'])
    scenario('4AGM 일제 → 구축함', salvo(4, 'DD1', 900, bearing=60), ['A'])
    scenario('4AGM 일제 → 전함, 구축함 쪽에서 접근', salvo(4, 'BB', 900, bearing=90), ['A'])
    scenario('HVAA 속도 1발 → 전함 (1900m/s)', salvo(1, 'BB', 1900), ['A'])
    scenario('2명 동시 4AGM → 전함 (A·B)', salvo(4, 'BB', 900, 'A') + salvo(4, 'BB', 900, 'B', t=0.1, bearing=20), ['A', 'B'])
    scenario('2명 0.5초 간격 4AGM → 전함', salvo(4, 'BB', 900, 'A') + salvo(4, 'BB', 900, 'B', t=0.5, bearing=-30), ['A', 'B'])
    scenario('LASM 2발 → 전함 (요격 무시)', salvo(2, 'BB', 900, immune=True), ['A'])
    scenario('ADAM 속도(300m/s) 1발 → 전함', salvo(1, 'BB', 300), ['A'])
    # 아군 CIWS: 호위함 2척(아군) CIWS가 1.5km 안의 플레이어를 노린 적 SAM(800m/s)을 막는다
    def friendly():
        return [Ciws((0, 18, 18), 'FFG1', rng=1500, radius=1500, other=1.0), Ciws((0, 10, -45), 'FFG1', rng=1500, radius=1500, other=1.0)]
    SHIPS['P'] = (300, 600, 400)
    sam = [('S0', (5000, 50, 5000), SHIPS['P'], 'P', 800, 0.0, False)]
    scenario('아군 CIWS: 함대 위 플레이어를 노린 SAM 1발', sam, ['S'], sites_fn=friendly)
    sam2 = [(f'S{k}', (5000 + k * 50, 50, 5000), SHIPS['P'], 'P', 800, k * 0.3, False) for k in range(3)]
    scenario('아군 CIWS: SAM 3발(0.3초 간격)', sam2, ['S'], sites_fn=friendly)


if __name__ == '__main__':
    main()
