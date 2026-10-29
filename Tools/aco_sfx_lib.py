# -*- coding: utf-8 -*-
"""ACO 효과음 합성 도구 모음(순수 파이썬 — numpy 없이). aco_make_sfx.py / aco_radio_filter.py가 쓴다.
신호 = float 리스트(-1~1), 샘플레이트 SR. 결과는 16bit PCM WAV."""
import math, random, wave, struct

SR = 44100


def silence(sec):
    return [0.0] * int(sec * SR)


def tone(freq, sec, shape='sine', phase=0.0):
    """기본 파형. shape: sine / square / saw / tri — square/saw는 배음을 제한한 합(에일리어싱 줄임)."""
    n = int(sec * SR)
    out = [0.0] * n
    if shape == 'sine':
        w = 2 * math.pi * freq / SR
        for i in range(n):
            out[i] = math.sin(phase + w * i)
        return out
    harmonics = max(1, int((SR * 0.45) // max(freq, 1.0)))
    harmonics = min(harmonics, 40)
    for k in range(1, harmonics + 1):
        if shape == 'square' and k % 2 == 0:
            continue
        if shape == 'tri' and k % 2 == 0:
            continue
        if shape == 'square':
            amp = 1.0 / k
        elif shape == 'saw':
            amp = (1.0 / k) * (1 if k % 2 else -1)
        else:  # tri
            amp = (1.0 / (k * k)) * (1 if (k // 2) % 2 == 0 else -1)
        w = 2 * math.pi * freq * k / SR
        for i in range(n):
            out[i] += amp * math.sin(phase * k + w * i)
    peak = max(1e-9, max(abs(v) for v in out))
    return [v / peak for v in out]


def sweep(f0, f1, sec, shape='sine', curve='exp'):
    """주파수가 f0 → f1로 변하는 사인(위상 누적)."""
    n = int(sec * SR)
    out = [0.0] * n
    ph = 0.0
    for i in range(n):
        t = i / max(n - 1, 1)
        f = f0 * (f1 / f0) ** t if curve == 'exp' else f0 + (f1 - f0) * t
        ph += 2 * math.pi * f / SR
        s = math.sin(ph)
        if shape == 'square':
            s = math.tanh(4 * s)
        out[i] = s
    return out


def noise(sec, seed=None):
    r = random.Random(seed)
    return [r.uniform(-1.0, 1.0) for _ in range(int(sec * SR))]


def crackle(sec, rate, seed=None):
    """드문드문 튀는 짧은 임펄스(무전 지글거림)."""
    r = random.Random(seed)
    n = int(sec * SR)
    out = [0.0] * n
    p = rate / SR
    i = 0
    while i < n:
        if r.random() < p:
            amp = r.uniform(0.3, 1.0) * (1 if r.random() < 0.5 else -1)
            length = r.randint(8, 60)
            for j in range(length):
                if i + j < n:
                    out[i + j] += amp * math.exp(-j / (length * 0.3)) * r.uniform(0.5, 1.0)
            i += length
        i += 1
    return out


# ───── 필터 (RBJ 바이쿼드) ─────

def biquad(x, kind, freq, q=0.707, gain_db=0.0):
    w0 = 2 * math.pi * freq / SR
    cw, sw = math.cos(w0), math.sin(w0)
    alpha = sw / (2 * q)
    if kind == 'lp':
        b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    elif kind == 'hp':
        b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    elif kind == 'bp':
        b0, b1, b2 = alpha, 0.0, -alpha
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    elif kind == 'peak':
        A = 10 ** (gain_db / 40)
        b0, b1, b2 = 1 + alpha * A, -2 * cw, 1 - alpha * A
        a0, a1, a2 = 1 + alpha / A, -2 * cw, 1 - alpha / A
    else:
        raise ValueError(kind)
    b0, b1, b2, a1, a2 = b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0
    y = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, v in enumerate(x):
        o = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y[i] = o
    return y


def band(x, lo, hi, order=2):
    """대역 통과(고역 통과 + 저역 통과를 order번)."""
    for _ in range(order):
        x = biquad(x, 'hp', lo)
        x = biquad(x, 'lp', hi)
    return x


def onepole_lp_var(x, cutoff_fn):
    """시간에 따라 바뀌는 차단 주파수의 1차 저역 통과(cutoff_fn(t초) → Hz)."""
    y = [0.0] * len(x)
    s = 0.0
    for i, v in enumerate(x):
        fc = cutoff_fn(i / SR)
        a = 1 - math.exp(-2 * math.pi * fc / SR)
        s += a * (v - s)
        y[i] = s
    return y


# ───── 엔벨로프/혼합 ─────

def env_adsr(n, a, d, s, r, sustain_sec=None):
    """a/d/r 초, s 레벨. sustain은 남은 길이."""
    out = [0.0] * n
    na, nd, nr = int(a * SR), int(d * SR), int(r * SR)
    ns = max(n - na - nd - nr, 0)
    for i in range(n):
        if i < na:
            out[i] = i / max(na, 1)
        elif i < na + nd:
            out[i] = 1 - (1 - s) * (i - na) / max(nd, 1)
        elif i < na + nd + ns:
            out[i] = s
        else:
            out[i] = s * max(0.0, 1 - (i - na - nd - ns) / max(nr, 1))
    return out


def env_exp(n, attack, tau):
    """빠른 시작(attack 초) + 지수 감쇠(tau 초)."""
    na = max(int(attack * SR), 1)
    return [(i / na) if i < na else math.exp(-(i - na) / (tau * SR)) for i in range(n)]


def mul(x, e):
    return [a * b for a, b in zip(x, e)]


def gain(x, g):
    return [v * g for v in x]


def mix(dst, src, at=0.0, g=1.0):
    """dst에 src를 at초 위치부터 더한다(길이 늘림)."""
    start = int(at * SR)
    need = start + len(src)
    if need > len(dst):
        dst.extend([0.0] * (need - len(dst)))
    for i, v in enumerate(src):
        dst[start + i] += v * g
    return dst


def fade(x, fin=0.003, fout=0.003):
    n = len(x)
    a, b = int(fin * SR), int(fout * SR)
    for i in range(min(a, n)):
        x[i] *= i / max(a, 1)
    for i in range(min(b, n)):
        x[n - 1 - i] *= i / max(b, 1)
    return x


def softclip(x, drive=1.0):
    k = math.tanh(drive)
    return [math.tanh(v * drive) / k for v in x]


def normalize(x, peak=0.89):
    m = max(1e-9, max(abs(v) for v in x))
    return [v * peak / m for v in x]


def loopify(x, xfade):
    """끝 xfade초를 앞에 겹쳐 이음매 없는 루프로(길이는 xfade만큼 줄어든다)."""
    n = int(xfade * SR)
    body = x[:len(x) - n]
    tail = x[len(x) - n:]
    for i in range(n):
        t = i / n
        body[i] = body[i] * math.sqrt(t) + tail[i] * math.sqrt(1 - t)
    return body


def note(name):
    """'A4' → Hz."""
    names = {'C': -9, 'C#': -8, 'D': -7, 'D#': -6, 'E': -5, 'F': -4, 'F#': -3, 'G': -2, 'G#': -1, 'A': 0, 'A#': 1, 'B': 2}
    key, octave = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((names[key] + (octave - 4) * 12) / 12)


def write_wav(path, x):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1.0, min(1.0, v)) * 32767)) for v in x))


def read_wav(path):
    """16bit PCM WAV → (모노 float 리스트, 샘플레이트). 스테레오는 평균."""
    with wave.open(path, 'rb') as w:
        ch, width, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        if width != 2:
            raise SystemExit('16bit PCM WAV만 지원합니다: ' + path)
        data = struct.unpack('<%dh' % (n * ch), w.readframes(n))
    mono = [sum(data[i * ch:(i + 1) * ch]) / (ch * 32768.0) for i in range(n)]
    return mono, rate


def resample(x, src_rate):
    """선형 보간 리샘플(SR로)."""
    if src_rate == SR:
        return x
    ratio = src_rate / SR
    n = int(len(x) / ratio)
    out = [0.0] * n
    for i in range(n):
        p = i * ratio
        j = int(p)
        f = p - j
        a = x[j] if j < len(x) else 0.0
        b = x[j + 1] if j + 1 < len(x) else a
        out[i] = a + (b - a) * f
    return out
