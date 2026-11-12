"""ElevenLabs 효과음 생성(text → sound). 키는 aco_elevenlabs.api_key()(환경 변수/레지스트리 — 출력하지 않는다).
  python Tools/aco_sfx_gen.py OUT_DIR NAME "prompt" [seconds] [influence]
결과: OUT_DIR/NAME.mp3"""
import json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from aco_elevenlabs import api_key

URL = 'https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128'


def generate(out_dir, name, prompt, seconds=None, influence=0.6):
    body = {'text': prompt, 'prompt_influence': influence}
    if seconds:
        body['duration_seconds'] = seconds
    req = urllib.request.Request(URL, data=json.dumps(body).encode('utf-8'), method='POST',
                                 headers={'xi-api-key': api_key(), 'Content-Type': 'application/json', 'Accept': 'audio/mpeg'})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
    except urllib.error.HTTPError as e:
        print('error', name, e.code, e.read()[:300])
        return None
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + '.mp3')
    open(path, 'wb').write(data)
    print('made', path, len(data), 'bytes')
    return path


if __name__ == '__main__':
    a = sys.argv[1:]
    generate(a[0], a[1], a[2], float(a[3]) if len(a) > 3 else None, float(a[4]) if len(a) > 4 else 0.6)
