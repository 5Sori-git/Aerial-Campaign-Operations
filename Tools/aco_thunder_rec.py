# -*- coding: utf-8 -*-
"""가까운 천둥: BigSoundBank 녹음(CC0, Joseph SARDIN & Axeline T.)을 잘라 게임용으로 만든다.
python -I aco_thunder_rec.py <Thunder2.wav> <Thunder4.wav> <출력 폴더>
원본(48kHz 24bit 모노)은 저장소에 두지 않는다 — Thunder #2(3113), Thunder #4(3115)를 bigsoundbank.com에서 받는다.
앞의 조용한 구간을 잘라 첫 울림이 바로 나오게 하고, 끝을 부드럽게 닫고, 음량을 맞춘다. 파형을 깎지 않는다."""
import os, sys, wave
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from aco_sfx_lib import SR, biquad, fade, level, normalize, resample, write_wav


def read24(path):
    w = wave.open(path)
    assert w.getnchannels() == 1 and w.getsampwidth() == 3
    rate, n = w.getframerate(), w.getnframes()
    raw = w.readframes(n)
    return [int.from_bytes(raw[i:i + 3], 'little', signed=True) / 8388608.0 for i in range(0, len(raw), 3)], rate


def make(src, start, end, tail, name, out):
    x, rate = read24(src)
    x = x[int(start * rate):int(end * rate)]
    x = resample(x, rate)
    x = biquad(x, 'hp', 28)
    x = fade(level(normalize(x, 1.0), 0.45), 0.012, tail)
    x = normalize(x, 0.89)
    write_wav(os.path.join(out, name + '.wav'), x)
    print(name, '%.2fs' % (len(x) / SR))


make(sys.argv[1], 1.05, 4.85, 0.5, 'ACO_Thunder_Near_1', sys.argv[3])
make(sys.argv[2], 0.98, 9.00, 2.2, 'ACO_Thunder_Near_2', sys.argv[3])
