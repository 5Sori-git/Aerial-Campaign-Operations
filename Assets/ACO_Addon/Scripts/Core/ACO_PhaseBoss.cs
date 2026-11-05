using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 상황별 대사 표를 쓰는 페이즈의 공통 부모(공중 요새, 랜스 포대, 함대전): 무전 참조와 대사 표 재생.
    /// 대사 표는 모듈마다 자기 배열(LineEvent·LineSpeaker·LineText·LineClip — 같은 순서, 한 줄 = 대사 하나)을 갖고 SayTable로 넘긴다.
    /// 같은 상황의 줄은 위에서 아래로 차례로 나온다 — 적 줄 뒤에 AWACS 줄을 두면 이어서 설명한다. 인스펙터는 ACO_PhaseFortressEditor가 한 줄씩 보여 준다.
    public abstract class ACO_PhaseBoss : ACO_PhaseModule
    {
        public const int SPEAKER_ENEMY = 0;
        public const int SPEAKER_AWACS = 1;
        public const int SPEAKER_ALLY = 2;

        [Header("대사 — 공통")]
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        [Tooltip("적 지휘관 대사의 이름·색")]
        public string EnemyName = "FORTRESS COMMAND";
        public Color EnemyColor = new Color(1f, 0.35f, 0.3f, 1f);
        [Tooltip("아군 지휘관 대사의 이름·색(화자 2번 — 함대전 함대장)")]
        public string AllyName = "FLAGSHIP";
        public Color AllyColor = new Color(0.5f, 0.8f, 1f, 1f);
        [Tooltip("음성이 이 시간(초)보다 오래 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;

        /// 이 상황의 줄을 모두 말하는 데 걸리는 시간(초) — 다음 일을 대사가 끝난 뒤로 미룰 때 쓴다.
        protected float TableTime(int situation, int[] events, string[] texts, AudioClip[] clips)
        {
            if (Chatter == null || events == null || texts == null) { return 0f; }
            float total = 0f;
            int n = Mathf.Min(events.Length, texts.Length);
            for (int i = 0; i < n; i++)
            {
                if (events[i] != situation) { continue; }
                AudioClip clip = clips != null && i < clips.Length ? clips[i] : null;
                if (string.IsNullOrEmpty(texts[i]) && clip == null) { continue; }
                total += Chatter.GetLineTime(texts[i], clip);
            }
            return total;
        }

        /// 표에서 이 상황의 줄을 위에서 아래로 차례로 넣는다. 글자·음성이 모두 빈 줄은 건너뛴다. {left} = left.
        protected void SayTable(int situation, string left, int[] events, int[] speakers, string[] texts, AudioClip[] clips)
        {
            if (Chatter == null || events == null || texts == null) { return; }
            int n = Mathf.Min(events.Length, texts.Length);
            for (int i = 0; i < n; i++)
            {
                if (events[i] != situation) { continue; }
                AudioClip clip = clips != null && i < clips.Length ? clips[i] : null;
                string text = texts[i];
                if (string.IsNullOrEmpty(text) && clip == null) { continue; }
                if (!string.IsNullOrEmpty(text)) { text = text.Replace("{left}", left); }
                int speaker = speakers != null && i < speakers.Length ? speakers[i] : SPEAKER_AWACS;
                if (speaker == SPEAKER_ENEMY) { Chatter.SayVoice(EnemyName, EnemyColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ENEMY); }
                else if (speaker == SPEAKER_ALLY) { Chatter.SayVoice(AllyName, AllyColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ALLY); }
                else { Chatter.SayControl(text, clip, VoiceQueueDelay); }
            }
        }
    }
}
