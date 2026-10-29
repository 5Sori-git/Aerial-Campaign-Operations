# -*- coding: utf-8 -*-
"""ACO 효과음 만들기: python aco_make_sfx.py [출력 폴더] (기본 Assets/ACO_Addon/Audio/Generated)
모두 합성(녹음 아님) — 44.1kHz 16bit 모노 WAV. 루프용(_Loop)은 이음매 없이 이어진다."""
import os, sys, math, random
from aco_sfx_lib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
os.makedirs(OUT, exist_ok=True)
made = []


def save(name, x, peak=0.89):
    x = normalize(x, peak)
    write_wav(os.path.join(OUT, name + '.wav'), x)
    made.append((name, len(x) / SR))


def beep(freq, sec, shape='square', a=0.004, r=0.01, lp=4000):
    """짧은 경보음 한 번(배음 제한 + 저역 통과로 귀 따갑지 않게)."""
    x = tone(freq, sec, shape)
    x = mul(x, env_adsr(len(x), a, 0.0, 1.0, r))
    return biquad(x, 'lp', lp)


def click(sec=0.006, freq=2600, seed=1):
    """기계식 스위치 '딸깍': 아주 짧은 잡음 + 높은 사인, 빠른 감쇠."""
    n = noise(sec, seed)
    t = tone(freq, sec)
    x = [0.6 * a + 0.5 * b for a, b in zip(n, t)]
    x = mul(x, env_exp(len(x), 0.0003, sec * 0.25))
    return biquad(x, 'hp', 800)


def radio_noise(sec, seed, lo=400, hi=2800):
    return band(noise(sec, seed), lo, hi, 2)


# ───────────── 무전 ─────────────

def ptt_start():
    x = silence(0.0)
    mix(x, click(0.006, 2400, 11), 0.0, 1.0)
    sq = radio_noise(0.2, 12)
    sq = mul(sq, env_adsr(len(sq), 0.012, 0.05, 0.45, 0.06))
    mix(x, sq, 0.008, 0.9)
    mix(x, gain(band(crackle(0.2, 60, 13), 500, 3000, 1), 0.6), 0.008)
    save('ACO_PTT_Start', fade(x, 0.0005, 0.01))


def ptt_end():
    # 놓을 때: 짧은 '딸깍' + 스켈치 꼬리 '치익'
    x = silence(0.0)
    mix(x, click(0.005, 2000, 21), 0.0, 0.8)
    tail = radio_noise(0.26, 22, 350, 3200)
    tail = mul(tail, env_exp(len(tail), 0.004, 0.07))
    mix(x, tail, 0.012, 1.1)
    mix(x, gain(band(crackle(0.2, 80, 23), 500, 3000, 1), 0.5), 0.015)
    save('ACO_PTT_End', fade(x, 0.0005, 0.02))


def roger_beep():
    x = silence(0.0)
    mix(x, beep(1200, 0.07, 'square', lp=3000), 0.0)
    mix(x, beep(1600, 0.08, 'square', lp=3200), 0.085)
    x = band(x, 300, 3200, 1)
    save('ACO_Roger_Beep', fade(x), 0.6)


def radio_static_loop():
    sec = 4.2
    x = radio_noise(sec, 31, 500, 2600)
    wob = [0.55 + 0.25 * math.sin(2 * math.pi * 0.7 * i / SR) + 0.2 * math.sin(2 * math.pi * 2.3 * i / SR + 1) for i in range(len(x))]
    x = mul(x, wob)
    mix(x, gain(band(crackle(sec, 25, 32), 600, 3500, 1), 0.9), 0.0)
    save('ACO_Radio_Static_Loop', loopify(x, 0.2), 0.35)


# ───────────── HUD/경보 ─────────────

def lock_searching_loop():
    # 락온 시도 중: 1초에 4번 짧은 삑
    x = silence(1.0)
    for k in range(4):
        mix(x, beep(1000, 0.06, 'square', lp=3500), k * 0.25)
    save('ACO_Lock_Searching_Loop', x[:SR], 0.6)


def lock_on_loop():
    # 락온: 끊김 없는 높은 음(1초에 정확히 정수 주기라 루프 이음매가 없다)
    # 필터 시작 과도 현상이 이음매에 남지 않게 2초를 만들어 뒤 1초(정상 상태)만 쓴다.
    x = tone(1000, 2.0, 'square')
    x = biquad(x, 'lp', 3500)
    save('ACO_Lock_On_Loop', x[SR:2 * SR], 0.55)


def missile_alert_loop():
    # 미사일 경보: 높낮이 두 음을 빠르게 번갈아(1초 8번)
    x = silence(1.0)
    for k in range(8):
        mix(x, beep(1500 if k % 2 == 0 else 1150, 0.11, 'square', a=0.002, r=0.008, lp=4000), k * 0.125)
    save('ACO_Missile_Alert_Loop', x[:SR], 0.65)


def caution_alarm_loop():
    # 주의 경보(고도/연료 등): 낮은 두 음 + 쉼
    x = silence(1.0)
    mix(x, beep(620, 0.22, 'saw', a=0.01, r=0.03, lp=2500), 0.0)
    mix(x, beep(880, 0.22, 'saw', a=0.01, r=0.03, lp=2500), 0.26)
    save('ACO_Caution_Alarm_Loop', x[:SR], 0.6)


def target_destroyed():
    # 목표 파괴 확인: 높은 두 음 '띠링'
    x = silence(0.0)
    for f, at in ((note('E6'), 0.0), (note('A6'), 0.08)):
        t = tone(f, 0.35, 'tri')
        mix(x, mul(t, env_exp(len(t), 0.003, 0.09)), at, 0.8)
    save('ACO_Target_Destroyed', fade(x), 0.7)


# ───── 징글: 기계/무전 장비에서 나는 소리(멜로디/화음 없이 같은 높이의 전자음, 데이터 신호음, 버저) ─────
# PTT처럼 무전기 음역(대역 통과)으로 깎고, 소리 나는 동안 옅은 잡음을 깐다.

def fsk(sec, f0, f1, baud, seed):
    """데이터 송신음(모뎀처럼 두 주파수를 무작위로 빠르게 오간다, 위상 연속)."""
    r = random.Random(seed)
    n = int(sec * SR)
    per = max(int(SR / baud), 1)
    out = [0.0] * n
    ph = 0.0
    f = f0
    for i in range(n):
        if i % per == 0:
            f = f0 if r.random() < 0.5 else f1
        ph += 2 * math.pi * f / SR
        out[i] = math.sin(ph)
    return mul(out, env_adsr(n, 0.004, 0.0, 1.0, 0.006))


def pulse(freq, sec, shape='square', lp=3000):
    """장비 신호음 한 번(딱 떨어지는 시작/끝)."""
    return beep(freq, sec, shape, a=0.003, r=0.006, lp=lp)


def buzzer(freq, sec):
    """낮은 버저: 사각파 + 50Hz 떨림(전기 장치 '부-')."""
    x = tone(freq, sec, 'square')
    x = [v * (0.75 + 0.25 * math.sin(2 * math.pi * 50 * i / SR)) for i, v in enumerate(x)]
    x = mul(x, env_adsr(len(x), 0.004, 0.0, 1.0, 0.012))
    return biquad(x, 'lp', 1800)


def equipment(x, seed, hiss=0.05):
    """무전기 음역으로 깎고, 소리가 있는 구간에 옅은 잡음을 깐다."""
    x = band(x, 300, 3200, 2)
    h = radio_noise(len(x) / SR, seed, 500, 2800)
    x = [v + n * hiss for v, n in zip(x, h)]
    return fade(softclip(normalize(x, 0.9), 1.4), 0.003, 0.02)


def phase_start():
    # 페이즈 시작(새 임무 수신): 짧은 데이터 송신음 → 같은 높이 '삑삑'
    x = silence(0.0)
    mix(x, fsk(0.32, 1200, 2200, 90, 101), 0.0, 0.55)
    mix(x, pulse(1400, 0.07), 0.40)
    mix(x, pulse(1400, 0.07), 0.52)
    save('ACO_Phase_Start', equipment(x, 102), 0.5)


def phase_complete():
    # 페이즈 성공(확인): '삑, 삑, 삐-' — 마지막만 길게
    x = silence(0.0)
    mix(x, pulse(1000, 0.06), 0.0)
    mix(x, pulse(1000, 0.06), 0.12)
    mix(x, pulse(1000, 0.32), 0.24)
    save('ACO_Phase_Complete', equipment(x, 103), 0.5)


def phase_failed():
    # 페이즈 실패: 낮은 버저 '부-부-'
    x = silence(0.0)
    mix(x, buzzer(330, 0.22), 0.0)
    mix(x, buzzer(330, 0.38), 0.30)
    save('ACO_Phase_Failed', equipment(x, 104), 0.5)


def mission_complete():
    # 미션 완료: 데이터 송신음 → '삑, 삑, 삑' → 긴 '삐-'
    x = silence(0.0)
    mix(x, fsk(0.55, 1200, 2200, 90, 105), 0.0, 0.5)
    t = 0.65
    for _ in range(3):
        mix(x, pulse(1000, 0.06), t)
        t += 0.12
    mix(x, pulse(1000, 0.7), t + 0.04)
    save('ACO_Mission_Complete', equipment(x, 106), 0.5)


def mission_failed():
    # 미션 실패: 낮은 버저 세 번 → 송신이 끊기듯 잡음 꼬리
    x = silence(0.0)
    for k in range(3):
        mix(x, buzzer(300, 0.3), k * 0.42)
    tail = radio_noise(0.9, 107, 350, 3000)
    mix(x, mul(tail, env_exp(len(tail), 0.005, 0.25)), 1.26, 0.6)
    save('ACO_Mission_Failed', equipment(x, 108, hiss=0.03), 0.6)


def ui_click():
    x = click(0.012, 3000, 51)
    mix(x, mul(tone(1800, 0.02), env_exp(int(0.02 * SR), 0.0005, 0.005)), 0.0, 0.4)
    save('ACO_UI_Click', fade(x, 0.0005, 0.005), 0.6)


def ui_select():
    x = sweep(900, 1800, 0.09)
    x = mul(x, env_adsr(len(x), 0.004, 0.0, 1.0, 0.04))
    y = silence(0.0)
    mix(y, x, 0.0, 0.7)
    mix(y, mul(tone(2400, 0.08, 'tri'), env_exp(int(0.08 * SR), 0.002, 0.03)), 0.07, 0.5)
    save('ACO_UI_Select', fade(y), 0.6)


# ───────────── 환경 ─────────────

def wind_loop():
    sec = 6.3
    x = noise(sec, 61)
    # 차단 주파수가 천천히 오르내려 '휘잉'
    x = onepole_lp_var(x, lambda t: 350 + 250 * math.sin(2 * math.pi * 0.17 * t) + 150 * math.sin(2 * math.pi * 0.41 * t + 2))
    x = onepole_lp_var(x, lambda t: 900)
    gust = [0.6 + 0.3 * math.sin(2 * math.pi * 0.11 * i / SR) + 0.1 * math.sin(2 * math.pi * 0.53 * i / SR) for i in range(len(x))]
    x = mul(x, gust)
    save('ACO_Wind_Loop', loopify(x, 0.3), 0.5)


def distant_boom():
    sec = 3.0
    x = silence(0.0)
    thump = sweep(70, 32, 0.6)
    mix(x, mul(thump, env_exp(len(thump), 0.02, 0.18)), 0.0, 1.0)
    rumble = biquad(biquad(noise(sec, 71), 'lp', 220), 'lp', 160)
    mix(x, mul(rumble, env_exp(len(rumble), 0.05, 0.7)), 0.0, 1.6)
    save('ACO_Distant_Boom', fade(x, 0.003, 0.3), 0.85)


def explosion_small():
    sec = 1.6
    n = noise(sec, 81)
    # 밝게 터졌다가 빠르게 어두워지는 잡음
    burst = onepole_lp_var(n, lambda t: 200 + 6000 * math.exp(-t / 0.08))
    burst = mul(burst, env_exp(len(burst), 0.002, 0.28))
    x = silence(0.0)
    mix(x, burst, 0.0, 1.0)
    thump = sweep(90, 40, 0.4)
    mix(x, mul(thump, env_exp(len(thump), 0.002, 0.1)), 0.0, 0.8)
    mix(x, gain(crackle(0.8, 120, 82), 0.25), 0.05)
    save('ACO_Explosion_Small', fade(softclip(x, 1.6), 0.001, 0.2), 0.89)


for fn in (ptt_start, ptt_end, roger_beep, radio_static_loop,
           lock_searching_loop, lock_on_loop, missile_alert_loop, caution_alarm_loop, target_destroyed,
           phase_start, phase_complete, phase_failed, mission_complete, mission_failed, ui_click, ui_select,
           wind_loop, distant_boom, explosion_small):
    random.seed(1)
    fn()
    print('made', made[-1][0], '%.2fs' % made[-1][1])
print('->', os.path.abspath(OUT))
