# -*- coding: utf-8 -*-
"""ElevenLabs로 무전 대사 음성을 만든다(순수 파이썬 — 표준 라이브러리만).

  python aco_elevenlabs.py                 → 목록(aco_voice_lines.json)에서 바뀐 줄만 만든다
  python aco_elevenlabs.py --only ID ...   → 그 ID만(바뀌지 않았어도 다시)
  python aco_elevenlabs.py --force         → 전부 다시
  python aco_elevenlabs.py --list          → 목록과 상태만 출력(만들지 않는다)

키: 환경 변수 ELEVENLABS_API_KEY(없으면 Windows 사용자 환경 변수를 레지스트리에서 읽는다). 키는 파일에 쓰지 않는다.
목록(aco_voice_lines.json): 화자(voices) → 목소리 ID(화자에 model이 있으면 그 모델), 줄마다 id·speaker·path·text.
path는 Assets/ACO_Addon/Audio/<base>/ 아래(base 기본 AWACSVoiceLines — 조종석 경고는 base "Generated").
화자에 "radio": {"grit", "hiss"}가 있으면 PCM으로 받아 무전 필터(aco_radio_filter — 대역 통과·찌그러짐·잡음)를 입혀 wav로 쓴다(path는 .wav).
path가 .wav면 PCM 24kHz로 받아(44.1kHz PCM은 Pro 요금제 이상만) 다듬어(tighten — 무음·끝 잡음·반복 간격) WAV로 쓴다(이미 있는 wav를 덮어써 Unity 연결·meta가 그대로), 아니면 mp3.
text의 [대괄호]는 eleven_v3/v4 연기 지시(오디오 태그) — 음성에만 쓰이고 자막은 게임 쪽 글자다.
만든 줄은 aco_voice_cache.json에 (목소리·모델·설정·글자) 해시를 남겨, 바뀐 줄만 다시 만든다.
"""
import os, sys, json, hashlib, time, wave, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO = os.path.join(HERE, '..', 'Assets', 'ACO_Addon', 'Audio')
LINES = os.path.join(HERE, 'aco_voice_lines.json')
CACHE = os.path.join(HERE, 'aco_voice_cache.json')
API = 'https://api.elevenlabs.io/v1/text-to-speech/{voice}?output_format={fmt}'


def api_key():
    # 레지스트리(사용자 환경 변수) 먼저 — 키를 바꾸면 이미 떠 있는 프로세스의 환경 변수는 예전 값이라서
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, 'Environment') as h:
            k = winreg.QueryValueEx(h, 'ELEVENLABS_API_KEY')[0].strip()
            if k:
                return k
    except Exception:
        pass
    k = os.environ.get('ELEVENLABS_API_KEY')
    if k:
        return k.strip()
    sys.exit('ELEVENLABS_API_KEY가 없습니다.')


def digest(voice, model, settings, text):
    s = json.dumps([voice, model, settings, text], sort_keys=True, ensure_ascii=False)
    return hashlib.sha1(s.encode('utf-8')).hexdigest()


def tts(key, voice, model, settings, text, fmt='mp3_44100_128'):
    body = json.dumps({'text': text, 'model_id': model, 'voice_settings': settings}).encode('utf-8')
    req = urllib.request.Request(API.format(voice=voice, fmt=fmt), data=body, method='POST',
                                 headers={'xi-api-key': key, 'Content-Type': 'application/json', 'Accept': 'audio/mpeg'})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            msg = e.read()[:300].decode('utf-8', 'replace')
            if e.code in (429, 500, 502, 503) and attempt < 3:
                time.sleep(3 * (attempt + 1))
                continue
            raise SystemExit('HTTP %d: %s' % (e.code, msg))



def tighten(pcm, sr, threshold=600, pad=0.03, max_gap=0.25, blip=0.08):
    """경고음(wav)을 다듬는다: 앞뒤 무음을 pad초만 남기고 자르고, 끝에 홀로 붙은 짧은 잡음(blip초 미만)을 버리고,
    말 사이 무음이 max_gap초보다 길면 그만큼으로 줄인다(v3가 "Stall. Stall." 사이를 1초씩 띄웠다). 16비트 모노 PCM."""
    import array
    a = array.array('h', pcm)
    n = len(a)
    win = max(int(sr * 0.01), 1)
    loud = [max(abs(x) for x in a[i:i + win]) > threshold for i in range(0, n, win)]
    segs = []   # 소리 구간 [시작 칸, 끝 칸)
    i = 0
    while i < len(loud):
        if loud[i]:
            j = i
            while j < len(loud) and loud[j]:
                j += 1
            segs.append([i, j])
            i = j
        else:
            i += 1
    if not segs:
        return pcm
    # 끝에 홀로 붙은 짧은 잡음 버리기
    while len(segs) > 1 and (segs[-1][1] - segs[-1][0]) * win < blip * sr and (segs[-1][0] - segs[-2][1]) * win > 0.3 * sr:
        segs.pop()
    # 가까운 구간은 하나로(짧은 숨 끊김은 그대로 둔다)
    merged = [segs[0]]
    for sg in segs[1:]:
        if (sg[0] - merged[-1][1]) * win <= max_gap * sr:
            merged[-1][1] = sg[1]
        else:
            merged.append(sg)
    padn = int(pad * sr)
    gapn = int(max_gap * sr)
    out = array.array('h')
    for k, (s0, s1) in enumerate(merged):
        a0 = max(s0 * win - (padn if k == 0 else 0), 0)
        a1 = min(s1 * win + (padn if k == len(merged) - 1 else 0), n)
        if k > 0:
            out.extend([0] * gapn)
        out.extend(a[a0:a1])
    return out.tobytes()

def main():
    args = sys.argv[1:]
    data = json.load(open(LINES, encoding='utf-8'))
    cache = json.load(open(CACHE, encoding='utf-8')) if os.path.exists(CACHE) else {}
    model = data.get('model', 'eleven_v3')
    only = set()
    if '--only' in args:
        only = set(a for a in args[args.index('--only') + 1:] if not a.startswith('--'))
    force = '--force' in args
    key = None if '--list' in args else api_key()
    made = 0
    chars = 0
    for line in data['lines']:
        sp = data['voices'][line['speaker']]
        m = sp.get('model', model)   # 화자별 모델(없으면 전체 모델) — AWACS는 평소 쓰는 목소리와 같게 eleven_v4
        settings = dict(sp.get('settings', {}))
        settings.update(line.get('settings', {}))
        radio_cfg = sp.get('radio')   # 화자에 radio가 있으면 무전 필터(aco_radio_filter)를 입혀 wav로 — 목소리 자체에 무전 느낌이 없는 화자만(선원·시스템)
        h = digest(sp['id'], m, settings, line['text'] + ('|radio' + json.dumps(radio_cfg, sort_keys=True) if radio_cfg else ''))
        base = line.get('base', 'AWACSVoiceLines')
        out = os.path.normpath(os.path.join(AUDIO, base, line['path']))
        ckey = line['path'] if base == 'AWACSVoiceLines' else base + '/' + line['path']
        fresh = os.path.exists(out) and cache.get(ckey) == h
        if only and line['id'] not in only:
            continue
        if '--list' in args:
            print(('ok   ' if fresh else 'TODO ') + line['id'], '|', line['speaker'], m, '|', line['text'])
            continue
        if fresh and not force and not only:
            continue
        os.makedirs(os.path.dirname(out), exist_ok=True)
        if radio_cfg:
            if not out.lower().endswith('.wav'):
                raise SystemExit(line['id'] + ': 무전 필터 화자는 path가 .wav여야 한다')
            import tempfile
            import aco_radio_filter
            pcm = tts(key, sp['id'], m, settings, line['text'], 'pcm_24000')
            tmp = os.path.join(tempfile.gettempdir(), 'aco_radio_src.wav')
            with wave.open(tmp, 'wb') as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(24000)
                w.writeframes(pcm)
            aco_radio_filter.process(tmp, out, False, float(radio_cfg.get('grit', 2.5)), float(radio_cfg.get('hiss', 0.02)))
            audio = open(out, 'rb').read()
        elif out.lower().endswith('.wav'):
            audio = tts(key, sp['id'], m, settings, line['text'], 'pcm_24000')
            audio = tighten(audio, 24000)   # 경고음: 앞뒤 무음·끝 잡음·반복 사이 간격
            with wave.open(out, 'wb') as w:   # 16비트 모노 24kHz
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(24000)
                w.writeframes(audio)
        else:
            audio = tts(key, sp['id'], m, settings, line['text'])
            open(out, 'wb').write(audio)
        cache[ckey] = h
        json.dump(cache, open(CACHE, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
        made += 1
        chars += len(line['text'])
        print('made', line['id'], '->', line['path'], '(%d bytes)' % len(audio))
    if '--list' not in args:
        print('done: %d lines, about %d characters' % (made, chars))


if __name__ == '__main__':
    main()
