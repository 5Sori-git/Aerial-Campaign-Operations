# -*- coding: utf-8 -*-
"""조종석 음성 경고(ACO_VoiceWarning)를 만든다 — Windows 기본 음성(SAPI, 다운로드 없음)으로 읽고 무전 필터(aco_radio_filter)를 약하게 입힌다.

  python aco_make_voice.py            → Audio/Generated/ACO_Voice_*.wav

음성: Microsoft Zira Desktop(영어 여성). 녹음한 음성으로 바꾸려면 같은 이름 파일을 덮어쓰거나 ACO_VoiceWarning의 클립 칸에 넣는다.
※ 2026-11-10부터 이 파일들은 ElevenLabs Betty1로 만든다(aco_elevenlabs.py, 목록의 warn_* 줄). 이 스크립트를 돌리면 Betty 음성을 덮어쓴다 — 예전 음성으로 돌아갈 때만.
"""
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from aco_sfx_lib import read_wav, write_wav  # noqa: E402
from aco_radio_filter import radio  # noqa: E402

OUT = os.path.join(HERE, '..', 'Assets', 'ACO_Addon', 'Audio', 'Generated')
VOICE = 'Microsoft Zira Desktop'
RATE = 1          # SAPI 말 빠르기(-10~10) — 경고는 조금 빠르게

LINES = {
    'ACO_Voice_PullUp': 'Pull up. Pull up.',
    'ACO_Voice_Altitude': 'Altitude. Altitude.',
    'ACO_Voice_Railgun': 'Railgun warning.',
    'ACO_Voice_Laser': 'Laser warning.',
    'ACO_Voice_Damage': 'Damage critical.',
    'ACO_Voice_Stall': 'Stall. Stall.',
    'ACO_Voice_Flares': 'Flares empty.',
    'ACO_Voice_Bingo': 'Bingo fuel.',
    'ACO_Voice_Ammo': 'Ammunition zero.',
}


def speak(text, path):
    ps = (
        "Add-Type -AssemblyName System.Speech;"
        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer;"
        "$s.SelectVoice('" + VOICE + "');"
        "$s.Rate = " + str(RATE) + ";"
        "$f = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(44100, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono);"
        "$s.SetOutputToWaveFile('" + path.replace("'", "''") + "', $f);"
        "$s.Speak('" + text.replace("'", "''") + "');"
        "$s.Dispose()"
    )
    subprocess.run(['powershell', '-NoProfile', '-Command', ps], check=True)


def trim(x, thr=0.02, pad=0.04):
    """앞뒤 무음을 잘라 경고가 바로 나오게(앞뒤 pad초 남김)."""
    n = len(x)
    s = next((i for i in range(n) if abs(x[i]) > thr), 0)
    e = next((i for i in range(n - 1, -1, -1) if abs(x[i]) > thr), n - 1)
    p = int(pad * 44100)
    return x[max(s - p, 0):min(e + p, n)]


def main():
    tmp = tempfile.mkdtemp(prefix='aco_voice_')
    for name, text in LINES.items():
        raw = os.path.join(tmp, name + '_raw.wav')
        speak(text, raw)
        x, rate = read_wav(raw)
        assert rate == 44100, rate
        # 조종석 경고 음성: 무전보다 깨끗하게(찌그러짐·잡음 약하게)
        y = trim(radio(x, grit=1.0, hiss=0.004))
        dst = os.path.join(OUT, name + '.wav')
        write_wav(dst, y)
        print('wrote', os.path.normpath(dst), round(len(y) / 44100.0, 2), 's')


if __name__ == '__main__':
    main()
