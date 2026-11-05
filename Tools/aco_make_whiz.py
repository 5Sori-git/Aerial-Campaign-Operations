# -*- coding: utf-8 -*-
"""총탄이 스쳐 지나가는 소리: python aco_make_whiz.py [출력 폴더]
SAV 권총의 도탄음(ricochet2.wav)을 빠르기를 달리해 네 가지로 만들고, 낮은 소리를 걷어낸 뒤 공기를 가르는 짧은 잡음을 얹는다.
ACO_BulletFlyby가 재생할 때 피치를 조금씩 더 흔든다. 합성보다 녹음이 낫다는 판단으로 도탄음을 바탕으로 삼았다."""
import os, sys, wave, struct
from aco_sfx_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, '..', 'Assets', 'SaccFlightAndVehicles', 'AssetFiles', 'SaccPistol', 'ricochet2.wav')
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
os.makedirs(OUT, exist_ok=True)


def read_source():
    w = wave.open(SOURCE)
    n = w.getnframes()
    ch = w.getnchannels()
    data = struct.unpack('<%dh' % (n * ch), w.readframes(n))
    return [v / 32768.0 for v in data[::ch]]


def respeed(x, rate):
    out = [0.0] * int(len(x) / rate)
    for i in range(len(out)):
        p = i * rate
        k = int(p)
        f = p - k
        a = x[k] if k < len(x) else 0.0
        b = x[k + 1] if k + 1 < len(x) else 0.0
        out[i] = a + (b - a) * f
    return out


def whiz(name, rate, air, seed):
    x = biquad(respeed(read_source(), rate), 'hp', 600)
    # 지나가는 순간의 공기 소리: 바로 솟았다가 빠르게 잦아든다
    sec = 0.22
    a = band(noise(sec, seed), 1100, 4200)
    a = mul(a, env_exp(len(a), 0.003, 0.05))
    mix(x, gain(a, air), 0.0)
    path = os.path.join(OUT, name + '.wav')
    write_wav(path, normalize(fade(x, 0.001, 0.03), 0.75))
    print('made', name, '%.2fs' % (len(x) / SR))


whiz('ACO_Bullet_Whiz_1', 1.0, 0.3, 501)
whiz('ACO_Bullet_Whiz_2', 1.25, 0.25, 502)
whiz('ACO_Bullet_Whiz_3', 0.85, 0.35, 503)
whiz('ACO_Bullet_Whiz_4', 1.5, 0.2, 504)
