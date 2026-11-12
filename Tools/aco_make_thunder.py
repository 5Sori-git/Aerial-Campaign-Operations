# -*- coding: utf-8 -*-
"""천둥(합성): python aco_make_thunder.py [출력 폴더] (기본 Assets/ACO_Addon/Audio/Generated)
폭풍 날씨(ACO_Lightning)가 번개 거리에 따라 고른다 — 가까운 천둥(2km 안) / 먼 천둥.
  - 가까이: 어두운 짧은 충격 + 0.3초 찢어지는 중역 → 크게 터지는 저역 폭음
  - 멀리: 충격 없이 낮게 깔리는 우르릉(고역이 공기에 먹혀 어둡다), 느리게 커졌다 몇 번 울렁이며 사라진다.
울렁임 = 번개 길(수 km)의 여러 지점에서 오는 소리가 시간차로 겹친 것 — 무작위 시각·세기의 덩어리 몇 개를 겹쳐 만든다. 44.1kHz 16bit 모노."""
import os, sys, math, random
from aco_sfx_lib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
os.makedirs(OUT, exist_ok=True)


def lumps(length, count, start, spread, tau_lo, tau_hi, seed):
    """우르릉 덩어리 엔벨로프: 무작위 시각(start + 0~spread)·세기·길이의 지수 감쇠를 겹친다(앞쪽이 대체로 크다)."""
    rnd = random.Random(seed)
    n = int(length * SR)
    env = [0.0] * n
    for k in range(count):
        t0 = start + rnd.random() ** 1.6 * spread
        amp = (0.5 + 0.5 * rnd.random()) * (1.0 - 0.55 * (t0 - start) / max(spread, 0.01))
        tau = tau_lo + (tau_hi - tau_lo) * rnd.random()
        att = 0.04 + 0.12 * rnd.random()
        i0 = int(t0 * SR)
        for i in range(i0, n):
            dt = (i - i0) / SR
            a = (dt / att) if dt < att else math.exp(-(dt - att) / tau)
            env[i] += amp * a
            if dt > att + tau * 6:
                break
    return env


def near(name, seed, length=6.0):
    n = int(length * SR)
    x = [0.0] * n
    # 1) 충격: 어두운 잡음을 짧게 — 찰칵하는 N파·고역 잡음은 지지직으로 들린다
    crack = biquad(biquad(noise(0.3, seed), 'lp', 2600), 'lp', 2600)
    crack = mul(crack, env_exp(len(crack), 0.0015, 0.04))
    mix(x, crack, 0.0, 1.6)
    # 2) 찢어지는 소리(번개 줄기가 갈라진다): 중역 잡음 × 작은 덩어리들 — 엔벨로프가 부드러워 낱낱의 잡음으로 들리지 않는다
    rip = band(noise(0.8, seed + 1), 250, 2200, 1)
    rip_env = lumps(0.8, 14, 0.0, 0.4, 0.015, 0.05, seed + 2)
    rip = [v * e for v, e in zip(rip, rip_env)]
    mix(x, rip, 0.005, 0.32)
    # 3) 폭음: 저역 잡음 큰 덩어리(0.05초 뒤 정점, 0.6초 감쇠)
    boom = biquad(biquad(noise(2.5, seed + 3), 'lp', 220), 'lp', 220)
    boom = mul(boom, env_exp(len(boom), 0.05, 0.6))
    mix(x, boom, 0.02, 2.2)
    # 4) 우르릉: 저역 잡음 × 울렁이는 덩어리 5개(0.3~2.5초 사이), 차단 주파수가 천천히 내려간다
    rum_src = noise(length, seed + 4)
    rum = onepole_lp_var(rum_src, lambda t: 140 + 260 * math.exp(-t / 1.5))
    rum = biquad(rum, 'lp', 300)
    env = lumps(length, 5, 0.25, 2.3, 0.4, 1.1, seed + 5)
    rum = [v * e for v, e in zip(rum, env)]
    mix(x, rum, 0.0, 2.6)
    x = biquad(biquad(x, 'lp', 4500), 'hp', 28)
    x = fade(level(normalize(x, 1.0), 0.5), 0.001, 0.6)
    return finish(name, x)


def far(name, seed, length=6.5):
    # 먼 천둥: 충격 없이 저역만(고역은 공기에 먹힌다), 덩어리 6개가 느리게 겹친다
    rum = onepole_lp_var(noise(length, seed), lambda t: 90 + 120 * math.exp(-t / 2.5))
    rum = biquad(biquad(rum, 'lp', 180), 'hp', 25)
    env = lumps(length, 6, 0.1, 3.2, 0.6, 1.4, seed + 1)
    rum = [v * e for v, e in zip(rum, env)]
    x = fade(softclip(rum, 1.3), 0.2, 0.8)
    return finish(name, x)


def finish(name, x):
    x = normalize(x, 0.89)
    path = os.path.join(OUT, name + '.wav')
    write_wav(path, x)
    print(name, '%.2fs' % (len(x) / SR), path)


# 가까운 천둥은 녹음으로 바꿨다 — aco_thunder_rec.py. near()는 쓰지 않는다(지지직거렸다).
far('ACO_Thunder_Far_1', 303)
far('ACO_Thunder_Far_2', 404, 7.0)
