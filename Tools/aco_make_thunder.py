# -*- coding: utf-8 -*-
"""천둥(합성): python aco_make_thunder.py [출력 폴더] (기본 Assets/ACO_Addon/Audio/Generated)
폭풍 날씨(ACO_Lightning)가 번개 거리에 따라 고른다 — 가까운 천둥(2km 안) / 먼 천둥.
  - 가까이: 찢어지는 소리(아주 짧은 밝은 충격 + 0.3초 지글거림) → 크게 터지는 저역 폭음 → 몇 번 울렁이며 잦아드는 우르릉(4~6초).
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
    # 1) 찢어지는 충격: N파(3ms) + 고역 잡음 20ms
    crack = [0.0] * n
    nw = int(0.003 * SR)
    for j in range(nw):
        crack[j] = 1.0 - 2.0 * j / nw
    burst = biquad(noise(0.03, seed), 'hp', 1500)
    burst = mul(burst, env_exp(len(burst), 0.0003, 0.008))
    mix(crack, burst, 0.0, 0.8)
    mix(x, crack, 0.0, 0.55)
    # 2) 지글거림(번개 줄기가 갈라지는 소리): 0.35초, 밝다가 어두워진다
    rip_src = [a + 0.7 * b for a, b in zip(noise(0.5, seed + 1), crackle(0.5, 1400, seed + 2))]
    rip = onepole_lp_var(rip_src, lambda t: 300 + 5000 * math.exp(-t / 0.08))
    rip = mul(rip, env_exp(len(rip), 0.002, 0.12))
    mix(x, rip, 0.005, 0.7)
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
    x = biquad(x, 'hp', 28)
    x = fade(softclip(x, 1.6), 0.001, 0.6)
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


near('ACO_Thunder_Near_1', 101)
near('ACO_Thunder_Near_2', 202, 5.5)
far('ACO_Thunder_Far_1', 303)
far('ACO_Thunder_Far_2', 404, 7.0)
