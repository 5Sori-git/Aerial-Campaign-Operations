# -*- coding: utf-8 -*-
"""나를 노리고 빗나간 적 미사일이 스쳐 지나간 소리(합성): python aco_make_flyby.py [출력 폴더] (기본 Assets/ACO_Addon/Audio/Generated)
미사일은 음속보다 빨라 지나간 뒤에 들린다 — 게임은 미사일이 나를 놓친 순간(ACO_Missile.LoseTarget) 재생하므로 소리는 최근접(PASS초, 거의 시작)부터:
  - 최근접 충격음(초음속 — 부드럽게 다듬은 N파) → 멀어지는 로켓 굉음(중저역 잡음)
미사일이 직선으로 지나간다고 보고(속력 V, 최근접 거리 D) 거리 r(t)로 세기·밝기를 정한다. 44.1kHz 16bit 모노."""
import os, sys, math, random
from aco_sfx_lib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
os.makedirs(OUT, exist_ok=True)

PASS = 0.03      # 최근접 시각(초) — 거의 바로 충격음(지나간 뒤에 들리는 소리)
LENGTH = 1.5
C_SOUND = 343.0


def flyby(name, speed, dist, seed):
    n = int(LENGTH * SR)
    r_at = lambda t: math.sqrt(dist * dist + (speed * (t - PASS)) ** 2)
    near = lambda t: dist / r_at(t)                       # 1 = 최근접
    after = lambda t: max(t - PASS, 0.0)

    # 1) 다가오는 쉭: 고역 잡음, 최근접까지 커진다(지나간 뒤엔 빨리 사라진다)
    hiss = band(noise(LENGTH, seed), 1500, 5000, 1)
    hiss = [v * (near(i / SR) ** 1.3 if i / SR <= PASS else near(i / SR) ** 3.0) for i, v in enumerate(hiss)]

    # 2) 최근접 충격음: N파(앞 압축 → 뒤 팽창, 수 ms) + 아주 짧은 밝은 잡음
    crack = [0.0] * n
    p0 = int(PASS * SR)
    nw = int(0.004 * SR)
    for j in range(nw):
        crack[p0 + j] = 1.0 - 2.0 * j / nw
    burst = biquad(noise(0.1, seed + 7), 'lp', 3000)
    burst = mul(burst, env_exp(len(burst), 0.001, 0.02))
    mix(crack, burst, PASS, 0.9)
    crack = biquad(crack[:n], 'lp', 3500)

    # 3) 멀어지는 로켓 굉음: 중저역 잡음 + 지글거림, 차단 주파수 3500 → 450Hz(멀어질수록 어둡게), 세기는 1/r
    roar = onepole_lp_var(noise(LENGTH, seed + 1), lambda t: 350 + 2200 * math.exp(-after(t) / 0.3) if t >= PASS - 0.05 else 2500)
    roar = biquad(biquad(roar, 'lp', 5000), 'hp', 90)
    roar = [v * (near(i / SR) ** (0.8 if i / SR > PASS else 2.2)) for i, v in enumerate(roar)]

    # 4) 엔진 음: 도플러 — 다가올 땐 높고(1/(1 - v/c)는 초음속이라 상한) 지나가면 낮게(1/(1 + v/c)). 위상 누적
    f0 = 520.0
    up = min(1.0 / max(1.0 - speed / C_SOUND, 0.35), 2.2)
    down = 1.0 / (1.0 + speed / C_SOUND)
    hum = [0.0] * n
    ph = 0.0
    for i in range(n):
        t = i / SR
        k = 0.5 + 0.5 * math.tanh((t - PASS) / 0.05)        # 최근접에서 높은 쪽 → 낮은 쪽
        f = f0 * (up * (1 - k) + down * k)
        ph += 2 * math.pi * f / SR
        hum[i] = (math.sin(ph) + 0.35 * math.sin(2 * ph)) * near(t) ** 1.5
    hum = biquad(hum, 'lp', 2400)

    x = [0.0] * n
    mix(x, hiss, 0.0, 0.3)
    mix(x, crack, 0.0, 0.5)
    mix(x, roar, 0.0, 1.0)
    mix(x, hum, 0.0, 0.16)
    x = x[:n]
    x = biquad(biquad(x, 'lp', 6500), 'lp', 6500)
    x = fade(level(normalize(x, 1.0), 0.35), 0.004, 0.25)
    x = normalize(x, 0.89)
    path = os.path.join(OUT, name + '.wav')
    write_wav(path, x)
    print(name, '%.2fs' % (len(x) / SR), path)


# 두 가지: 아주 가까이(15m, 빠름) / 조금 떨어져(35m)
flyby('ACO_Missile_Flyby_1', 520.0, 15.0, 11)
flyby('ACO_Missile_Flyby_2', 430.0, 35.0, 23)
