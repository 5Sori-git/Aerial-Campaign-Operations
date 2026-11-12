# -*- coding: utf-8 -*-
"""ACO 효과음 만들기: python aco_make_sfx.py [출력 폴더] (기본 Assets/ACO_Addon/Audio/Generated)
폭발음 둘은 SAV 녹음을 가공, 나머지는 합성 — 44.1kHz 16bit 모노 WAV. 루프용(_Loop)은 이음매 없이 이어진다."""
import os, sys, math, random
import wave, struct
from aco_sfx_lib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
os.makedirs(OUT, exist_ok=True)
made = []


# ElevenLabs 효과음으로 바꾼 것(2026-11-11 — 대포 + 천둥). 다시 만들면 덮어쓰므로 건너뛴다(예전 합성 소리를 원하면 이 목록에서 빼면 된다)
ELEVEN_REPLACED = {'ACO_Railgun_Fire', 'ACO_Railgun_MiniFire', 'ACO_Missile_Flyby_1', 'ACO_Missile_Flyby_2'}


def save(name, x, peak=0.89):
    if name in ELEVEN_REPLACED:
        print('skip (ElevenLabs):', name)
        return
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


# SAV 폭발 녹음(MIT 라이선스, 읽기만 한다)을 바탕으로 만든다 — 합성 잡음 + softclip은 지지직거렸다.
SAV_SOUNDS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'SaccFlightAndVehicles', 'AssetFiles', 'SF-1', 'Sounds')


def read_sav(name):
    w = wave.open(os.path.join(SAV_SOUNDS, name))
    n = w.getnframes()
    ch = w.getnchannels()
    data = struct.unpack('<%dh' % (n * ch), w.readframes(n))
    return [v / 32768.0 for v in data[::ch]]


def respeed(x, rate):
    """rate < 1이면 낮고 길어진다(선형 보간)."""
    out = [0.0] * int(len(x) / rate)
    for i in range(len(out)):
        t = i * rate
        k = int(t)
        f = t - k
        out[i] = x[k] * (1 - f) + (x[k + 1] if k + 1 < len(x) else 0.0) * f
    return out


def explosion_small():
    # 중거리(약 1km)에서 터진 미사일: 공기에 고역이 먹혀 어둡고, 첫 충격이 무뎌지고, 땅·구름에 부딪힌 메아리가 뒤따른다.
    # SAV 미사일 폭발 녹음을 조금 낮춰 바탕으로 쓰되 소리가 그대로 들리지 않게 다듬는다.
    src = respeed(read_sav('MissileExplode.wav'), 0.92)[:int(3.0 * SR)]
    direct = biquad(biquad(biquad(src, 'lp', 2400), 'lp', 2400), 'hp', 70)
    x = silence(0.0)
    mix(x, direct, 0.0, 0.8)
    mix(x, direct, 0.012, 0.35)   # 짧은 겹침 — 먼 소리는 앞머리가 번진다
    wet = biquad(biquad(direct, 'lp', 1100), 'lp', 1100)
    for at, g in ((0.07, 0.42), (0.13, 0.36), (0.21, 0.32), (0.34, 0.26), (0.5, 0.19), (0.72, 0.13), (1.0, 0.08)):
        mix(x, wet, at, g)
    x = level(normalize(x, 1.0), 0.6)
    save('ACO_Explosion_Small', fade(x[:int(3.4 * SR)], 0.004, 1.0), 0.89)


def explosion_big():
    # 공중 요새 충돌: 미사일 폭발의 첫 충격 + 낮춘 기체 폭발 몸통 + 부드럽게 남는 울림. 클립 없이 정규화만 한다.
    sec = 9.0
    x = silence(0.0)
    hit = respeed(read_sav('MissileExplode.wav'), 0.75)
    mix(x, hit, 0.0, 1.0)
    body = respeed(read_sav('PlaneExplode.wav'), 0.6)
    mix(x, body, 0.05, 0.8)
    rumble = biquad(biquad(noise(sec, 92), 'lp', 160), 'lp', 110)
    mix(x, mul(rumble, env_exp(len(rumble), 0.3, 2.6)), 0.0, 0.8)
    x = biquad(biquad(x, 'lp', 3000), 'hp', 22)
    save('ACO_Explosion_Big', fade(level(normalize(x, 1.0), 0.5), 0.001, 1.2), 0.89)


def decal_spray():
    # 스프레이 한 번: 밸브 소리 + 높은 치익 소리가 빠르게 잦아든다
    sec = 0.6
    hiss = band(noise(sec, 101), 2200, 9000)
    x = silence(0.0)
    mix(x, mul(hiss, env_exp(len(hiss), 0.012, 0.2)), 0.0, 1.0)
    valve = band(noise(0.03, 102), 900, 2500)
    mix(x, mul(valve, env_exp(len(valve), 0.001, 0.01)), 0.0, 0.5)
    save('ACO_Decal_Spray', fade(x, 0.002, 0.08), 0.7)


def railgun_charge_loop():
    # 충전 윙윙 — 이음매 없는 루프, 높낮이는 게임이 바꾼다
    sec = 4.0
    x = gain(tone(62, sec, 'saw'), 0.35)
    mix(x, gain(tone(124.5, sec, 'square'), 0.12), 0.0)
    mix(x, gain(tone(311, sec), 0.1), 0.0)
    mix(x, gain(band(noise(sec, 111), 900, 2600), 0.18), 0.0)
    save('ACO_Railgun_Charge_Loop', loopify(biquad(x, 'lp', 1800), 0.4), 0.7)


def ecm_loop():
    # ECM 구역: 레일건 충전처럼 윙윙대는 전자음(높은 쪽) + 살짝 지직거리는 잡음 — 이음매 없는 루프
    sec = 4.0
    x = gain(tone(150, sec, 'saw'), 0.22)
    mix(x, gain(tone(301, sec, 'square'), 0.09), 0.0)
    mix(x, gain(tone(1240, sec), 0.05), 0.0)
    mix(x, gain(band(noise(sec, 131), 1200, 3600), 0.1), 0.0)
    mix(x, gain(crackle(sec, 45, 132), 0.1), 0.0)
    save('ACO_Ecm_Loop', loopify(biquad(x, 'lp', 4200), 0.4), 0.6)


# Audio/SFX/Explode_small.ogg(로켓·SRAAM이 쓰는 실제 폭발음 0.39초)를 디코딩한 것
EXPLODE_SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'aco_explode_small_src.wav')
THUNDER_SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated', 'ACO_Thunder_Near_1.wav')   # 소형 레일건에 섞는다


def ring(x, freq):
    """링 변조: 폭발음에 사인을 곱한다 — 쇳소리·전기 느낌이 난다."""
    w = 2 * math.pi * freq / SR
    return [v * math.sin(w * i) for i, v in enumerate(x)]


def echoes(x, src, taps):
    for at, g in taps:
        mix(x, src, at, g)


def railgun_fire():
    # 발사: 폭발 녹음을 낮춰 겹쳐 키우고, 링 변조 + 맑은 방전 스윕 + 콘덴서 험으로 전기 느낌을 얹는다
    src = read_wav(EXPLODE_SRC)[0]
    crisp = src
    body = respeed(src, 0.6)
    deep = biquad(respeed(src, 0.33), 'lp', 1400)
    sub = biquad(respeed(src, 0.2), 'lp', 500)
    x = silence(0.0)
    mix(x, crisp, 0.0, 0.7)
    mix(x, body, 0.0, 1.0)
    mix(x, deep, 0.008, 1.2)
    mix(x, sub, 0.02, 1.3)
    mix(x, ring(body, 310.0), 0.0, 0.55)
    mix(x, ring(crisp, 760.0), 0.0, 0.45)
    zap = sweep(4200, 180, 0.55)
    mix(x, mul(zap, env_exp(len(zap), 0.001, 0.14)), 0.0, 0.3)
    zap2 = sweep(2600, 90, 0.9)
    mix(x, mul(zap2, env_exp(len(zap2), 0.002, 0.25)), 0.0, 0.3)
    hum = biquad(tone(120, 1.4, 'saw'), 'lp', 900)
    mix(x, mul(hum, env_exp(len(hum), 0.01, 0.35)), 0.03, 0.2)
    # 넓은 공간에 퍼지는 메아리
    wet = biquad(biquad(deep, 'lp', 800), 'hp', 40)
    echoes(x, wet, ((0.12, 0.6), (0.24, 0.5), (0.4, 0.42), (0.62, 0.34), (0.9, 0.26), (1.3, 0.2), (1.8, 0.14), (2.5, 0.09)))
    x = biquad(x, 'hp', 28)
    save('ACO_Railgun_Fire', fade(level(normalize(x, 1.0), 0.35)[:int(6.0 * SR)], 0.001, 1.5), 0.89)


def crack(seed, sec=0.09, tau=0.022, thump=0.6):
    """파열음: 아주 빠르게 솟는 광대역 '딱' + 낮게 꺼지는 '쿵'(공기가 찢어지는 첫 충격)."""
    n = biquad(biquad(noise(sec, seed), 'hp', 350), 'lp', 7000)
    x = mul(n, env_exp(len(n), 0.0004, tau))
    body = sweep(150, 45, 0.16)
    mix(x, mul(body, env_exp(len(body), 0.001, 0.05)), 0.0, thump)
    return x


def railgun_mini_fire():
    # 소형 레일건 발사: 맨 앞에 짧은 파열음(딱·쿵), 그 위에 가까운 천둥의 찢어지는 첫 소리를 겹쳐 뒤에 우르릉이 남게 한다.
    # 폭발음을 낮춰 무게를 싣고, 전기음은 줄여 레이저 느낌을 뺐다
    src = read_wav(EXPLODE_SRC)[0]
    x = silence(0.0)
    mix(x, crack(31, 0.12, 0.03, 0.8), 0.0, 2.2)   # 몸통보다 15ms 먼저 — 따로 들리게
    mix(x, respeed(src, 0.85), 0.015, 0.9)
    mix(x, biquad(respeed(src, 0.55), 'lp', 1600), 0.02, 0.7)
    mix(x, ring(src, 900.0), 0.02, 0.35)
    zap = sweep(5200, 500, 0.28)
    mix(x, mul(zap, env_exp(len(zap), 0.001, 0.07)), 0.02, 0.16)
    hum = biquad(tone(240, 0.5, 'saw'), 'lp', 1400)
    mix(x, mul(hum, env_exp(len(hum), 0.005, 0.1)), 0.01, 0.16)
    wet = biquad(respeed(src, 0.6), 'lp', 1200)
    echoes(x, wet, ((0.09, 0.3), (0.2, 0.2), (0.36, 0.12)))
    # 천둥(ACO_Thunder_Near_1 — aco_make_thunder.py): 0.12초부터(찢어지는 첫 소리를 파열음 바로 뒤에) 2.3초, 고역을 깎아 무겁게
    th = read_wav(THUNDER_SRC)[0][int(0.12 * SR):int(2.42 * SR)]
    th = biquad(th, 'lp', 3200)
    mix(x, mul(th, env_exp(len(th), 0.003, 0.9)), 0.008, 3.0)
    save('ACO_Railgun_MiniFire', fade(level(normalize(x, 1.0), 0.6)[:int(2.2 * SR)], 0.001, 0.9), 0.89)


# ACO_SFX_ONLY=이름,이름 이면 그 함수만 만든다(손으로 바꾼 소리를 덮어쓰지 않게 — 예: 거대 레일건 발사음은 사용자가 바꿨다)
ONLY = [s.strip() for s in os.environ.get('ACO_SFX_ONLY', '').split(',') if s.strip()]
for fn in (ptt_start, ptt_end, roger_beep, radio_static_loop,
           lock_searching_loop, lock_on_loop, missile_alert_loop, caution_alarm_loop, target_destroyed,
           phase_start, phase_complete, phase_failed, mission_complete, mission_failed, ui_click, ui_select,
           wind_loop, distant_boom, explosion_small, explosion_big, decal_spray, railgun_charge_loop, railgun_mini_fire, ecm_loop):
    # railgun_fire(거대 레일건 발사음)는 목록에서 뺐다 — 사용자가 직접 만든 소리로 바꿨다. 다시 만들려면 목록에 다시 넣는다
    if ONLY and fn.__name__ not in ONLY:
        continue
    random.seed(1)
    fn()
    print('made', made[-1][0], '%.2fs' % made[-1][1])
print('->', os.path.abspath(OUT))
