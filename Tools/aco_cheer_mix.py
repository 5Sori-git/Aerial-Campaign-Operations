# -*- coding: utf-8 -*-
"""무전 대사 밑에 군중 환호를 깐다(Blender aud + numpy). 잠수함 격침 항모 대사(Submarine_L19 — 6-205)에 쓴다.
실행: blender --background --factory-startup --python Tools/aco_cheer_mix.py -- 환호.mp3 목소리.mp3 결과(.mp3 또는 .wav)
  잠수함: -- Tools/aco_sub_cheer_src.mp3 Tools/aco_sub_l19_voice_src.mp3 Assets/ACO_Addon/Audio/AWACSVoiceLines/Submarine/Submarine_L19.mp3
환호는 첫 말("It's down!" — 0.35초보다 긴 첫 소리 덩어리, 앞의 숨소리는 건너뛴다)이 끝나는 곳에서 터지고, 대사 동안은 낮게(덕킹),
대사가 끝나면 1.2초 올라왔다가 1.5초에 걸쳐 사라진다. 환호는 무전처럼 대역을 좁힌다(300Hz~3.4kHz).
원본: 환호 = ElevenLabs 효과음("Deep baritone roar of a large group of men cheering and clapping, military crew, no women, heard through a radio transmission"),
목소리 = captain(eleven_v4) "[proud, steady] It's down! The whole crew's cheering on the flight deck. [grateful] Thank you, all of you!"."""
import os, sys
import numpy as np
import aud

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
cheer_path, voice_path, out_path = argv[0], argv[1], argv[2]
RATE = 44100


def load(path):
    s = aud.Sound(path).resample(RATE, aud.RESAMPLE_QUALITY_HIGH if hasattr(aud, 'RESAMPLE_QUALITY_HIGH') else 2).rechannel(aud.CHANNELS_MONO)
    a = np.asarray(s.data(), dtype=np.float32)
    return a[:, 0] if a.ndim > 1 else a


def bandpass(path):
    s = aud.Sound(path).resample(RATE, 2).rechannel(aud.CHANNELS_MONO).highpass(300.0).lowpass(3400.0)
    a = np.asarray(s.data(), dtype=np.float32)
    return a[:, 0] if a.ndim > 1 else a


def envelope(x, win):
    k = np.ones(win, np.float32) / win
    return np.convolve(np.abs(x), k, mode='same')


def first_phrase_end(v):
    """첫 말 덩어리가 끝나는 곳(샘플): 소리가 난 뒤 0.12초 넘게 조용해지는 첫 자리."""
    env = envelope(v, int(RATE * 0.02))
    thr = env.max() * 0.08
    loud = env > thr
    # 소리 덩어리(0.12초 넘는 조용함으로 나뉜다) 중 0.35초보다 긴 첫 덩어리 — 앞의 숨소리는 건너뛴다
    quiet = int(RATE * 0.12)
    i, n = 0, len(loud)
    while i < n:
        if not loud[i]:
            i += 1
            continue
        s0, run, j = i, 0, i
        while j < n:
            run = run + 1 if not loud[j] else 0
            if run >= quiet:
                break
            j += 1
        e0 = j - run
        if e0 - s0 > int(RATE * 0.35):
            return e0
        i = j
    return int(np.argmax(loud)) + int(RATE * 0.8)


def last_sound(v):
    env = envelope(v, int(RATE * 0.02))
    idx = np.nonzero(env > env.max() * 0.05)[0]
    return int(idx[-1]) if len(idx) else len(v)


cheer = bandpass(cheer_path)
cheer /= max(np.abs(cheer).max(), 1e-6)
for vp in (voice_path,):
    v = load(vp)
    v /= max(np.abs(v).max(), 1e-6)
    hit = first_phrase_end(v)
    end = last_sound(v)
    tail_up, tail_fade = int(RATE * 1.2), int(RATE * 1.5)
    n = max(len(v), min(end + tail_up + tail_fade, hit + len(cheer)))
    out = np.zeros(n, np.float32)
    out[:len(v)] += v
    # 환호 볼륨: 터질 때 0.55, 0.4초 뒤 대사 밑 0.22, 대사가 끝나면 0.5로 올라왔다가 사라짐
    g = np.zeros(n, np.float32)
    t = np.arange(n)
    attack = int(RATE * 0.08)
    g = np.where(t < hit, 0.0, g)
    rise = (t >= hit) & (t < hit + attack)
    g[rise] = 0.55 * (t[rise] - hit) / attack
    burst_end = hit + int(RATE * 0.4)
    m = (t >= hit + attack) & (t < burst_end)
    g[m] = 0.55
    duck_in = burst_end + int(RATE * 0.3)
    m = (t >= burst_end) & (t < duck_in)
    g[m] = 0.55 + (0.22 - 0.55) * (t[m] - burst_end) / (duck_in - burst_end)
    m = (t >= duck_in) & (t < end)
    g[m] = 0.22
    up = end + int(RATE * 0.3)
    m = (t >= end) & (t < up)
    g[m] = 0.22 + (0.5 - 0.22) * (t[m] - end) / (up - end)
    m = (t >= up) & (t < end + tail_up)
    g[m] = 0.5
    m = t >= end + tail_up
    g[m] = 0.5 * np.clip(1.0 - (t[m] - end - tail_up) / tail_fade, 0.0, 1.0)
    seg = cheer[:max(n - hit, 0)]
    out[hit:hit + len(seg)] += seg * g[hit:hit + len(seg)]
    out *= 0.95 / max(np.abs(out).max(), 1e-6)
    snd = aud.Sound.buffer(out.reshape(-1, 1), RATE)
    path = out_path
    if path.lower().endswith('.mp3'):
        snd.write(path, rate=RATE, channels=aud.CHANNELS_MONO, format=aud.FORMAT_S16, container=aud.CONTAINER_MP3, codec=aud.CODEC_MP3, bitrate=128000)
    else:
        snd.write(path, rate=RATE, channels=aud.CHANNELS_MONO, format=aud.FORMAT_S16, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM)
    print('wrote', path, 'hit %.2fs end %.2fs len %.2fs' % (hit / RATE, end / RATE, n / RATE))
