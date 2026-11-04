"""ACO 동기화 시뮬레이터 — 시한 표적(TST) 페이즈와 모듈 자체 동기화에서 생길 수 있는 오류를 몬테카를로로 센다.

VRChat 동작 모델(가정):
  - 오브젝트마다 Manual 동기화: 오너가 RequestSerialization → 0~0.2초 뒤 전송(그 전에 오너가 나가면 유실) → 받는 쪽마다 지연 후 도착.
    같은 오브젝트는 최신 버전만 적용(순서 보장), 다른 오브젝트끼리는 순서가 보장되지 않는다.
  - 지연: 로그정규(중앙 120ms) + 3% 확률로 1~4초 끊김.
  - 오너가 나가면 그 오브젝트들은 0.5~3초 뒤 새 마스터에게 넘어간다. 새 오너의 값 = 그때까지 받은 마지막 값.
  - 네트워크 이벤트(Owner 대상): 지연 후 도착, 받을 때 원래 오너가 나갔으면 유실.
  - 서버 시각은 클라이언트마다 ±40ms 오차.
  - SaccTarget: 쏜 사람 화면에서 피해 → 표적 오너에게 이벤트 → 오너가 체력 0 → 동기화.
  - 미션 관리자·생성기·표적은 보통 마스터 소유(같은 클라이언트).

시나리오(각 N회):
  A. 기본(오너 교체 없음)
  B. 페이즈 중 마스터 이탈(오너 교체)
  C. 늦게 들어온 플레이어(HUD 불일치 시간)
  D. 호위 이동 시간 보너스 — 오너 교체 때 두 번 더해지는지

설계:
  current  : 지금 관리자 코드(마감 = 오너가 처음 본 시각 + 제한, 마감 즉시 도주, 생성기 중복 배치 막지 않음)
  proposed : 모듈 자체 동기화 개선안(마감 = 생성기 배치 시각 + 제한 — 동기화된 값에서 모두가 같게 계산,
             도주 판정은 마감 + Grace, 생성기는 같은 태그 같은 분류를 다시 놓지 않음, 모듈 상태에 페이즈 번호를 찍음)

사용: python Tools/aco_sync_sim.py [반복 수]
"""
import random, math, sys, copy, statistics

DT = 0.05


def latency(rng):
    base = math.exp(rng.gauss(math.log(0.12), 0.6))
    if rng.random() < 0.03:
        base += rng.uniform(1.0, 4.0)
    return base


class World:
    def __init__(self, rng, clients):
        self.rng = rng
        self.t = 0.0
        self.clients = clients            # id -> {'join':, 'leave':, 'skew':}
        self.objs = {}                    # name -> {'owner':, 'ver':, 'views': {cid: (ver, state)}, 'pending_owner': (t, cid)}
        self.queue = []                   # (time, kind, payload)

    def present(self, cid):
        if cid is None:
            return False
        c = self.clients[cid]
        return c['join'] <= self.t < c['leave']

    def master(self):
        alive = [cid for cid in self.clients if self.present(cid)]
        return min(alive, key=lambda c: self.clients[c]['join']) if alive else None

    def now_ms(self, cid):
        return (self.t + self.clients[cid]['skew']) * 1000.0

    def add_obj(self, name, owner, state):
        self.objs[name] = {'owner': owner, 'ver': 0, 'views': {cid: (0, copy.deepcopy(state)) for cid in self.clients}, 'transfer_at': None}

    def state(self, name, cid):
        return self.objs[name]['views'][cid][1]

    def owner(self, name):
        return self.objs[name]['owner']

    def serialize(self, name):
        o = self.objs[name]
        if o['owner'] is None:
            return   # 오너 교체 중 — 보낼 사람이 없다
        o['ver'] += 1
        snap = copy.deepcopy(o['views'][o['owner']][1])
        send = self.t + self.rng.uniform(0.0, 0.2)
        self.queue.append((send, 'send', (name, o['owner'], o['ver'], snap)))

    def event(self, name, fn):
        """Owner 대상 네트워크 이벤트. 보낸 시점 오너가 받을 때 없으면 유실."""
        target = self.objs[name]['owner']
        self.queue.append((self.t + latency(self.rng), 'event', (name, target, fn)))

    def step(self):
        self.t += DT
        due = [q for q in self.queue if q[0] <= self.t]
        self.queue = [q for q in self.queue if q[0] > self.t]
        for _, kind, payload in sorted(due, key=lambda q: q[0]):
            if kind == 'send':
                name, sender, ver, snap = payload
                if not self.present(sender):
                    continue   # 보내기 전에 나감 — 유실
                for cid in self.clients:
                    if cid != sender:
                        self.queue.append((self.t + latency(self.rng), 'recv', (name, cid, ver, snap)))
            elif kind == 'recv':
                name, cid, ver, snap = payload
                v, _ = self.objs[name]['views'][cid]
                if ver > v and (self.present(cid) or self.clients[cid]['join'] > self.t):
                    self.objs[name]['views'][cid] = (ver, copy.deepcopy(snap))
            elif kind == 'event':
                name, target, fn = payload
                if self.present(target) and self.objs[name]['owner'] == target:
                    fn()
        # 늦게 들어온 사람: 들어오는 순간 오브젝트마다 마지막 값을 따로 받는다(오브젝트끼리 도착 순서 무작위)
        for cid, c in self.clients.items():
            if abs(c['join'] - self.t) < DT / 2 and c['join'] > 0:
                for name, o in self.objs.items():
                    owner = o['owner']
                    if owner is not None and owner != cid:
                        v, st = o['views'][owner]
                        self.queue.append((self.t + latency(self.rng) + self.rng.uniform(0, 1.5), 'recv', (name, cid, v, copy.deepcopy(st))))
        # 오너 이탈 → 새 마스터로
        for name, o in self.objs.items():
            if o['owner'] is not None and not self.present(o['owner']) and o['transfer_at'] is None:
                o['transfer_at'] = self.t + self.rng.uniform(0.5, 3.0)
                o['owner'] = None
            if o['owner'] is None and o['transfer_at'] is not None and self.t >= o['transfer_at']:
                o['owner'] = self.master()
                o['transfer_at'] = None
                if o['owner'] is not None:
                    o['ver'] = max(o['ver'], o['views'][o['owner']][0])


# ───────────── 시한 표적 페이즈 ─────────────

TST_COUNT = 3
TST_LIMIT = 90.0
TST_INTERVAL = 8.0
DEPLOY_TIMEOUT = 20.0
SETTLE = 10.0


def run_tst(rng, design, scenario, grace=3.0):
    clients = {0: {'join': -100, 'leave': 1e9, 'skew': rng.uniform(-0.04, 0.04)},
               1: {'join': -90, 'leave': 1e9, 'skew': rng.uniform(-0.04, 0.04)},
               2: {'join': -80, 'leave': 1e9, 'skew': rng.uniform(-0.04, 0.04)}}
    if scenario == 'B':
        clients[0]['leave'] = rng.uniform(5.0, 300.0)
    if scenario == 'C':
        clients[3] = {'join': rng.uniform(10.0, 250.0), 'leave': 1e9, 'skew': rng.uniform(-0.04, 0.04)}
    w = World(rng, clients)
    # 관리자(시한 표적 상태) · 생성기(배치) · 표적 체력(SaccTarget, 표적 하나씩)
    w.add_obj('mgr', 0, {'phase': 1, 'index': 0, 'hits': 0, 'deployed': False, 'spawn_ms': 0.0, 'deadline_ms': 0.0,
                         'requested_ms': -1.0, 'state': 'running'})
    w.add_obj('gen', 0, {'units': []})   # 각 유닛: {'tag', 'deploy_ms', 'id'}
    OBJ = 'mod' if design == 'module' else 'mgr'
    if design == 'module':
        # 관리자 오브젝트는 페이즈 도장만(페이즈 시작은 오래전 — 모두 받았다), 모듈은 Begin 직후라 오너만 값이 있고 곧 보낸다
        w.add_obj('mod', 0, {'stamp': 0, 'index': 0, 'hits': 0, 'deployed': False, 'spawn_ms': 0.0, 'deadline_ms': 0.0,
                             'requested_ms': -1.0, 'state': 'running'})
        w.state('mod', 0)['stamp'] = 1
        w.serialize('mod')
    w.add_obj('hp', 0, {})               # unit id -> health(0 = 파괴)

    truth = {'deadline_true': {}, 'kill_time': {}, 'dup_deploys': 0, 'false_escape': 0, 'late_hit': 0, 'outcome': None}
    unit_seq = [0]

    def tag_of(index):
        return 100 + 1 + index

    # 생성기 오너가 배치 이벤트를 받는다
    def deploy(tag):
        def fn():
            owner = w.owner('gen')
            st = w.state('gen', owner)
            if design != 'current' and any(u['tag'] == tag for u in st['units']):
                return   # 같은 태그 같은 분류는 다시 놓지 않는다
            if any(u['tag'] == tag for u in st['units']):
                truth['dup_deploys'] += 1
            unit_seq[0] += 1
            uid = unit_seq[0]
            st['units'].append({'tag': tag, 'deploy_ms': w.now_ms(owner), 'id': uid})
            if w.owner('hp') is not None:
                w.state('hp', w.owner('hp'))[uid] = 100.0
            truth['deadline_true'].setdefault(tag, w.t + TST_LIMIT)
            # D: 배치 직후(0~0.4초) 마스터가 나간다 — 생성기 상태는 나갔는데 관리자 상태는 못 보낸 경우를 노린다
            if scenario == 'D' and owner == 0 and clients[0]['leave'] > 1e8:
                clients[0]['leave'] = w.t + rng.uniform(0.0, 0.4)
            w.serialize('gen')
            w.serialize('hp')
            # 플레이어가 이 표적을 언제 잡을지(쏜 사람 화면 시각) — 60% 여유 있게, 25% 마감 근처, 15% 못 잡음
            r = rng.random()
            if r < 0.6:
                tk = w.t + rng.uniform(15, TST_LIMIT - 15)
            elif r < 0.85:
                tk = w.t + rng.uniform(TST_LIMIT - 5, TST_LIMIT + 1)
            else:
                tk = None
            if tk is not None:
                shooter = rng.choice([c for c in clients if c != 3] or [0])
                pending_kills.append((tk, uid, shooter, tag))
        return fn

    pending_kills = []
    pending_retry = []
    recovered = {'n': 0}
    tick_next = 0.0
    end_time = TST_COUNT * (TST_LIMIT + TST_INTERVAL + DEPLOY_TIMEOUT) + 60
    hud_mismatch = 0.0

    while w.t < end_time:
        w.step()
        # 쏜 사람 화면에서 체력 0 → 표적 오너에게 피해 이벤트
        for k in [k for k in pending_kills if k[0] <= w.t]:
            pending_kills.remove(k)
            tk, uid, shooter, tag = k
            if not w.present(shooter):
                continue
            # 플레이어는 자기 화면에 보이는 그 태그의 표적을 쏜다(첫 배치 동기화가 유실돼 다시 놓였으면 새 표적을)
            seen = [u for u in w.state('gen', shooter)['units'] if u['tag'] == tag]
            if not seen:
                if w.t < truth['deadline_true'].get(tag, 0) + 10:
                    pending_kills.append((w.t + 1.0, uid, shooter, tag))
                continue
            uid = seen[-1]['id']
            truth['kill_time'].setdefault(tag, tk)
            truth.setdefault('kill_shooter', {}).setdefault(tag, shooter)

            def kill(uid=uid):
                owner = w.owner('hp')
                if owner is None:
                    return
                hp = w.state('hp', owner)
                # 새 오너가 체력 값을 아직 못 받았어도 SaccTarget은 프리팹 체력(가득)을 들고 있다
                if hp.get(uid, 100) > 0:
                    hp[uid] = 0.0
                    w.serialize('hp')
            if w.owner('hp') == shooter:
                kill()
            else:
                w.event('hp', kill)
            # 쏜 사람은 표적이 아직 살아 보이면(피해 이벤트가 오너 교체 중 유실 등) 3초 뒤 다시 쏜다
            pending_retry.append((w.t + 3.0, uid, shooter, tag))
        for k in [k for k in pending_retry if k[0] <= w.t]:
            pending_retry.remove(k)
            _, uid, shooter, tag = k
            if w.present(shooter) and w.state('hp', shooter).get(uid, 100) > 0 and any(u['id'] == uid for u in w.state('gen', shooter)['units']):
                pending_kills.append((w.t, uid, shooter, tag))

        # 늦게 들어온 사람의 HUD: 관리자 값(맞힌 수)과 생성기·체력 값(표적 상태)이 어긋난 시간
        if scenario == 'C' and w.present(3):
            m = w.state(OBJ, 3)
            g = w.state('gen', 3)
            hp = w.state('hp', 3)
            cur = [u for u in g['units'] if u['tag'] == tag_of(m['index'])]
            # 어긋남: 지금 표적 번호의 유닛이 이미 파괴로 보이는데 맞힌 수가 그대로 / 관리자는 배치했다는데 표적이 안 보임
            if (cur and hp.get(cur[0]['id'], 100) <= 0 and m['state'] == 'running') or (m['deployed'] and not cur and m['state'] == 'running'):
                hud_mismatch += DT

        owner = w.owner('mgr')
        if owner is None or w.t < tick_next:
            continue
        tick_next = w.t + 1.0
        if design == 'module':
            # 모듈 오너를 관리자 오너로 맞춘다(Tick에서 SetOwner — 즉시 내 것이 된다)
            w.objs['mod']['owner'] = owner
            w.objs['mod']['transfer_at'] = None
            w.objs['mod']['ver'] = max(w.objs['mod']['ver'], w.objs['mod']['views'][owner][0])
        m = w.state(OBJ, owner)
        if m['state'] != 'running':
            break
        g = w.state('gen', owner)
        hp = w.state('hp', owner)
        now = w.now_ms(owner)
        if design == 'module' and m['stamp'] != 1:
            # Recover: 순번 0부터 올라가며 배치된 표적을 찾는다 — 부서진 건 잡은 것으로
            recovered['n'] += 1
            m.update({'stamp': 1, 'index': 0, 'hits': 0, 'deployed': False, 'spawn_ms': now, 'deadline_ms': 0.0})
            for k in range(TST_COUNT):
                us = [u for u in g['units'] if u['tag'] == tag_of(k)]
                if not us:
                    break
                m['index'] = k
                m['deployed'] = True
                if all(hp.get(u['id'], 100) <= 0 for u in us):
                    m['hits'] += 1
                    m['index'] = k + 1
                    m['deployed'] = False
                    m['spawn_ms'] = now + TST_INTERVAL * 1000
            w.serialize(OBJ)
        tag = tag_of(m['index'])
        changed = False
        if not m['deployed']:
            if now - m['spawn_ms'] >= 0:
                m['deployed'] = True
                m['deadline_ms'] = 0.0
                m['spawn_ms'] = now
                m['requested_ms'] = now
                changed = True
                if w.owner('gen') == owner:
                    deploy(tag)()
                else:
                    w.event('gen', deploy(tag))
        else:
            units = [u for u in g['units'] if u['tag'] == tag]
            if not units:
                if m['deadline_ms'] == 0 and now - m['spawn_ms'] >= DEPLOY_TIMEOUT * 1000:
                    # 배치 실패 → 이 표적 건너뜀
                    m['index'] += 1
                    m['deployed'] = False
                    m['spawn_ms'] = now + TST_INTERVAL * 1000
                    changed = True
            else:
                if m['deadline_ms'] == 0:
                    if design != 'current':
                        m['deadline_ms'] = min(u['deploy_ms'] for u in units) + TST_LIMIT * 1000
                    else:
                        m['deadline_ms'] = now + TST_LIMIT * 1000
                    changed = True
                destroyed = all(hp.get(u['id'], 100) <= 0 for u in units)
                # 배치 직후 체력 0(부활 동기화 전)은 기다린다 — 여기선 새 유닛이라 해당 없음
                if destroyed:
                    m['hits'] += 1
                    kt = truth['kill_time'].get(tag)
                    if kt is not None and kt > truth['deadline_true'][tag]:
                        truth['late_hit'] += 1
                    m['index'] += 1
                    m['deployed'] = False
                    m['deadline_ms'] = 0.0
                    m['spawn_ms'] = now + TST_INTERVAL * 1000
                    changed = True
                else:
                    limit = m['deadline_ms'] + (grace * 1000 if design != 'current' else 0)
                    if now - limit >= 0:
                        kt = truth['kill_time'].get(tag)
                        # 거짓 도주: 쏜 사람 화면에서 마감 전에 잡았는데 도주 판정
                        player_deadline = m['deadline_ms'] / 1000.0
                        # 쏜 사람이 나가 버렸으면(다시 쏠 사람이 없다) 동기화 오류가 아니다
                        shooter_left = not w.present(truth.get('kill_shooter', {}).get(tag))
                        if kt is not None and kt <= player_deadline and not shooter_left:
                            truth['false_escape'] += 1
                            truth['fe_detail'] = (round(w.t, 2), round(kt, 2), round(player_deadline, 2), owner, w.owner('hp'))
                        m['state'] = 'failed'
                        changed = True
            if m['index'] >= TST_COUNT and m['state'] == 'running':
                m['state'] = 'cleared'
                changed = True
        if changed:
            w.serialize(OBJ)

    owner = w.owner('mgr')
    final = w.state(OBJ, owner if owner is not None else w.master())
    truth['recovered'] = recovered['n']
    # 잡은 수 정확성: 모듈 값 = 실제로 부서진 표적(태그별) 수
    gview = w.state('gen', owner if owner is not None else w.master())
    hview = w.state('hp', owner if owner is not None else w.master())
    real = sum(1 for k in range(TST_COUNT) if [u for u in gview['units'] if u['tag'] == tag_of(k)]
               and all(hview.get(u['id'], 100) <= 0 for u in gview['units'] if u['tag'] == tag_of(k)))
    truth['hits_wrong'] = final['hits'] != real and final['state'] == 'cleared'
    truth['outcome'] = final['state']
    truth['stuck'] = final['state'] == 'running'
    truth['hud_mismatch'] = hud_mismatch
    return truth


# ───────────── 호위 이동 시간 보너스 ─────────────

def run_travel(rng, design):
    """관리자 오브젝트 하나에 '더한 시간'과 '더했음' 표시가 함께 있다(한 번에 동기화)."""
    clients = {0: {'join': -100, 'leave': rng.uniform(0.5, 6.0), 'skew': 0.0},
               1: {'join': -90, 'leave': 1e9, 'skew': rng.uniform(-0.04, 0.04)}}
    w = World(rng, clients)
    w.add_obj('mgr', 0, {'ext': 0.0, 'granted': False})
    arrival_true = 300.0
    visible_at = rng.uniform(0.2, 2.5)     # 호위 대상 배치가 오너 화면에 보이는 시각
    tick_next = 0.0
    while w.t < 20.0:
        w.step()
        owner = w.owner('mgr')
        if owner is None or w.t < tick_next:
            continue
        tick_next = w.t + 1.0
        m = w.state('mgr', owner)
        if not m['granted'] and w.t >= visible_at:
            remaining = arrival_true - w.t
            m['ext'] += remaining + 15.0
            m['granted'] = True
            w.serialize('mgr')
    final = w.state('mgr', w.owner('mgr') or 1)
    ideal = arrival_true - visible_at + 15.0
    return {'double': final['ext'] > ideal * 1.5, 'short_by': ideal - final['ext']}


# ───────────── 모듈 상태의 페이즈 표시 ─────────────

def run_stamp(rng, stamped):
    """관리자(페이즈 번호)와 모듈(맞힌 수)이 다른 오브젝트일 때, 새 페이즈 직후 보는 사람 HUD가 이전 페이즈 값을 보여 주는 시간."""
    clients = {0: {'join': -100, 'leave': 1e9, 'skew': 0.0}, 1: {'join': -90, 'leave': 1e9, 'skew': 0.0}}
    w = World(rng, clients)
    w.add_obj('mgr', 0, {'phase': 0})
    w.add_obj('mod', 0, {'stamp': 0, 'hits': 2})   # 이전 시한 표적 페이즈에서 2개 맞힘
    # 새 페이즈(2번)로 바뀜: 관리자와 모듈이 같은 틱에 각각 동기화
    w.state('mgr', 0)['phase'] = 2
    w.serialize('mgr')
    w.state('mod', 0)['stamp'] = 2
    w.state('mod', 0)['hits'] = 0
    w.serialize('mod')
    wrong = 0.0
    while w.t < 8.0:
        w.step()
        m = w.state('mgr', 1)
        d = w.state('mod', 1)
        if m['phase'] != 2:
            continue   # 아직 새 페이즈를 모른다(이전 페이즈 화면 — 틀린 게 아니다)
        shown = d['hits'] if (not stamped or d['stamp'] == m['phase']) else 0
        if shown != 0:
            wrong += DT
    return wrong


def pct(x, n):
    return f"{100.0 * x / n:5.2f}%"


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    rng = random.Random(12345)
    print(f"시한 표적 {TST_COUNT}개, 제한 {TST_LIMIT:.0f}초, 반복 {n}회\n")
    print(f"{'설계':10} {'시나리오':10} {'거짓 도주':>9} {'표적 중복':>9} {'멈춤':>7} {'마감 뒤 인정':>11} {'HUD 어긋남(초)':>14}")
    for design in ('current', 'proposed', 'module'):
        for sc, label in (('A', '기본'), ('B', '마스터 이탈'), ('C', '늦은 입장'), ('D', '배치 직후 이탈')):
            fe = dup = stuck = late = 0
            rec = wrong = 0
            mism = []
            for _ in range(n):
                r = run_tst(rng, design, sc)
                fe += 1 if r['false_escape'] else 0
                dup += 1 if r['dup_deploys'] else 0
                stuck += 1 if r['stuck'] else 0
                late += r['late_hit']
                rec += 1 if r['recovered'] else 0
                wrong += 1 if r['hits_wrong'] else 0
                if sc == 'C':
                    mism.append(r['hud_mismatch'])
            m = f"{statistics.mean(mism):6.2f} / p95 {sorted(mism)[int(len(mism) * 0.95)]:5.2f}" if mism else "-"
            extra = f"  복구 {pct(rec, n)}, 잡은 수 틀림 {pct(wrong, n)}" if design == 'module' else ""
            print(f"{design:10} {label:10} {pct(fe, n):>9} {pct(dup, n):>9} {pct(stuck, n):>7} {late / n:11.3f} {m:>14}{extra}")
    print("\n거짓 도주 — Grace(초)별(개선안, 마스터 이탈 포함 섞어서)")
    for g in (0.0, 1.0, 2.0, 3.0, 4.0, 5.0):
        fe = 0
        late = 0
        for i in range(n):
            r = run_tst(rng, 'proposed', 'B' if i % 2 else 'A', grace=g)
            fe += 1 if r['false_escape'] else 0
            late += r['late_hit']
        print(f"  Grace {g:3.1f}s : 거짓 도주 {pct(fe, n)}, 마감 뒤 인정 {late / n:.3f}개/미션")
    print("\n관리자·모듈이 다른 오브젝트일 때 새 페이즈 직후 이전 값을 보여 주는 시간")
    for stamped in (False, True):
        ws = [run_stamp(rng, stamped) for _ in range(n)]
        bad = sum(1 for x in ws if x > 0)
        label = '페이즈 번호 찍음' if stamped else '안 찍음'
        print(f"  {label:10}: 틀린 값이 보인 경우 {pct(bad, n)}, 평균 {statistics.mean(ws):.2f}초 / 최대 {max(ws):.2f}초")
    print("\n호위 이동 시간 보너스(오너가 0.5~6초 안에 나감)")
    dbl = 0
    short = []
    for _ in range(n):
        r = run_travel(rng, 'current')
        dbl += 1 if r['double'] else 0
        short.append(r['short_by'])
    print(f"  두 번 더해짐 {pct(dbl, n)}, 덜 더해진 시간 평균 {statistics.mean(short):.2f}초 / 최대 {max(short):.2f}초")


if __name__ == '__main__':
    main()
