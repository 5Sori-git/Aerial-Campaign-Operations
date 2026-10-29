# -*- coding: utf-8 -*-
"""녹음한 대사에 무전기 효과를 입힌다(순수 파이썬).

  python aco_radio_filter.py 입력.wav [출력.wav]        → 출력 생략 시 입력_radio.wav
  python aco_radio_filter.py 폴더                        → 폴더 안 .wav 전부(이미 _radio인 것은 건너뜀)
  옵션: --ptt   앞뒤에 ACO_PTT_Start/End를 붙인다(ACO_MissionVoice의 PttStart/PttEnd를 쓰면 붙이지 말 것)
        --grit N  찌그러짐 세기(기본 2.5, 0 = 깨끗)
        --hiss N  바닥 잡음 크기(기본 0.02)

처리: 대역 통과(300~3200Hz, 무전기 음역) → 1.8kHz 강조 → 포화(찌그러짐) → 말할 때만 섞이는 잡음/지글거림 → 정규화.
16bit PCM WAV(모노/스테레오, 아무 샘플레이트)만 받는다. 결과는 44.1kHz 모노.
"""
import os, sys, math
from aco_sfx_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
SFX_DIR = os.path.join(HERE, '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')


def radio(x, grit=2.5, hiss=0.02, seed=7):
    x = band(x, 300, 3200, 2)
    x = biquad(x, 'peak', 1800, 1.0, 4.0)
    x = normalize(x, 0.9)
    if grit > 0:
        x = softclip(x, grit)
    # 말소리 크기를 따라가는 엔벨로프(잡음이 말할 때만 들리게 — 무전 스켈치 느낌)
    env = [0.0] * len(x)
    e = 0.0
    up, down = 1 - math.exp(-1 / (0.005 * SR)), 1 - math.exp(-1 / (0.12 * SR))
    for i, v in enumerate(x):
        a = abs(v)
        e += (up if a > e else down) * (a - e)
        env[i] = min(1.0, e * 3)
    hissy = band(noise(len(x) / SR, seed), 500, 3000, 1)
    crk = band(crackle(len(x) / SR, 30, seed + 1), 600, 3200, 1)
    out = [v + (h * hiss + c * hiss * 1.5) * (0.3 + 0.7 * en) for v, h, c, en in zip(x, hissy, crk, env)]
    return normalize(out, 0.89)


def process(src, dst, ptt, grit, hiss):
    x, rate = read_wav(src)
    x = resample(x, rate)
    y = radio(x, grit, hiss)
    if ptt:
        pre_path = os.path.join(SFX_DIR, 'ACO_PTT_Start.wav')
        post_path = os.path.join(SFX_DIR, 'ACO_PTT_End.wav')
        if os.path.exists(pre_path) and os.path.exists(post_path):
            pre, _ = read_wav(pre_path)
            post, _ = read_wav(post_path)
            y = gain(pre, 0.8) + y + gain(post, 0.8)
        else:
            print('  (PTT 소리가 없습니다 — aco_make_sfx.py를 먼저 실행하세요)')
    write_wav(dst, y)
    print('made', dst, '%.2fs' % (len(y) / SR))


def main():
    args = [a for a in sys.argv[1:]]
    if not args:
        print(__doc__)
        return
    ptt = '--ptt' in args
    grit, hiss = 2.5, 0.02
    if '--grit' in args:
        grit = float(args[args.index('--grit') + 1])
    if '--hiss' in args:
        hiss = float(args[args.index('--hiss') + 1])
    pos = []
    skip = False
    for a in args:
        if skip:
            skip = False
            continue
        if a in ('--grit', '--hiss'):
            skip = True
            continue
        if not a.startswith('--'):
            pos.append(a)
    src = pos[0]
    if os.path.isdir(src):
        for name in sorted(os.listdir(src)):
            if name.lower().endswith('.wav') and not name[:-4].endswith('_radio'):
                process(os.path.join(src, name), os.path.join(src, name[:-4] + '_radio.wav'), ptt, grit, hiss)
    else:
        dst = pos[1] if len(pos) > 1 else src[:-4] + '_radio.wav'
        process(src, dst, ptt, grit, hiss)


if __name__ == '__main__':
    main()
