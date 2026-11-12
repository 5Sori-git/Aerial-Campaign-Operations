# -*- coding: utf-8 -*-
"""레이저가 내 기체 곁을 스칠 때 나는 소리: python aco_make_laser_zap.py [출력 폴더]
총탄 스치는 소리(ACO_Bullet_Whiz_*) 대신 ACO_BulletFlyby가 레이저 기총(ACO_NPCGun + ACO_NPCLaser)에 쓴다.
"붕" — 낮게 웅웅거리는 톱니파 울림(배음 + 살짝 어긋난 두 번째 울림이 맥놀이)이 다가왔다 지나가며 높이가 내려가고(도플러), 세기는 가운데서 부풀었다 잦아든다.
위에 아주 옅은 전기 지직거림. 세 가지 높이로 만든다(파일 이름은 그대로 — 씬 연결 유지).
→ Audio/Generated/ACO_Laser_Zap_1~3.wav"""
import os, sys, math
from aco_sfx_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
os.makedirs(OUT, exist_ok=True)


def saw_sweep(f0, f1, sec):
    """f0 → f1로 내려가는 톱니파(배음이 많아 웅웅거린다 — 뒤에서 저역 통과로 다듬는다)."""
    n = int(sec * SR)
    out = [0.0] * n
    ph = 0.0
    for i in range(n):
        t = i / max(n - 1, 1)
        f = f0 * (f1 / f0) ** t
        ph = (ph + f / SR) % 1.0
        out[i] = 2.0 * ph - 1.0
    return out


def swell(n, peak=0.42):
    """다가왔다 지나가는 세기: 0 → 1(peak 자리) → 0, 지나간 뒤가 조금 더 길다."""
    out = [0.0] * n
    k = max(int(n * peak), 1)
    for i in range(n):
        if i < k:
            t = i / k
            out[i] = t * t * (3 - 2 * t)
        else:
            t = (i - k) / max(n - k, 1)
            out[i] = (1 - t) ** 1.6
    return out


def hum(name, f_hi, f_lo, sec, seed):
    body = saw_sweep(f_hi, f_lo, sec)
    beat = gain(saw_sweep(f_hi * 1.012, f_lo * 1.012, sec), 0.8)   # 맥놀이 — 웅웅거림
    mix(body, beat, 0.0)
    sub = gain(sweep(f_hi * 0.5, f_lo * 0.5, sec, 'sine'), 0.9)        # 아래로 묵직하게
    mix(body, sub, 0.0)
    body = biquad(body, 'lp', 1400)
    body = biquad(body, 'hp', 45)
    fizz = band(crackle(sec, 400, seed), 3000, 8000)
    mix(body, gain(fizz, 0.06), 0.0)
    body = mul(body, swell(len(body)))
    body = softclip(body, 1.3)
    write_wav(os.path.join(OUT, name + '.wav'), fade(normalize(body, 0.85), 0.01, 0.04))
    print('wrote', name)


hum('ACO_Laser_Zap_1', 150, 105, 0.55, 11)
hum('ACO_Laser_Zap_2', 175, 120, 0.48, 23)
hum('ACO_Laser_Zap_3', 130, 92, 0.62, 37)
