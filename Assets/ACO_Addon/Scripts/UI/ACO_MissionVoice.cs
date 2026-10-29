using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 페이즈 음성(로컬 전용, 동기화 없음). 미션 관리자의 동기화 상태를 읽어 페이즈가 시작/성공/실패할 때 유형별 음성을 튼다.
    ///  - 유형별 칸(StartClips/SuccessClips/FailClips, 순서 = 페이즈 유형 1~8)이 비어 있으면 공통 음성(Default*)을 튼다. 둘 다 없으면 조용히.
    ///  - 배치가 안 돼 같은 페이즈가 다른 유형으로 바뀌면 새 유형의 시작 음성만 튼다(실패 음성 없음).
    ///  - 마지막 페이즈가 끝나면 그 페이즈의 성공 음성 다음에 MissionComplete, 제한 시간 실패면 MissionFailed.
    ///  - 음성이 겹치면 줄을 세워 앞 음성이 끝난 뒤 차례로 튼다(MaxQueueDelay보다 오래 밀린 음성은 버린다).
    ///  - 처음 본 상태(늦게 들어온 클라이언트 포함)는 기준만 잡고 틀지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_MissionVoice : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 관리자를 자동으로 넣는다")]
        public ACO_MissionManager Manager;
        [Tooltip("음성을 낼 AudioSource(2D 권장 — Spatial Blend 0)")]
        public AudioSource Voice;
        [Range(0f, 1f)] public float Volume = 1f;

        [Header("페이즈 유형별 (칸 순서: 1 타격, 2 SEAD, 3 점수 할당, 4 시한 표적, 5 제공권, 6 보스, 7 공중 호위, 8 지상 호위)")]
        public AudioClip[] StartClips = new AudioClip[8];
        public AudioClip[] SuccessClips = new AudioClip[8];
        public AudioClip[] FailClips = new AudioClip[8];

        [Header("공통 (유형별 칸이 비었을 때)")]
        public AudioClip DefaultStart;
        public AudioClip DefaultSuccess;
        public AudioClip DefaultFail;

        [Header("미션 결과 (선택)")]
        public AudioClip MissionComplete;
        public AudioClip MissionFailed;

        [Header("무전 효과")]
        [Tooltip("음성 앞에 재생할 무전 송신 소리(PTT 누름 '딸깍/치익'). 비우면 없음")]
        public AudioClip PttStart;
        [Tooltip("음성 뒤에 재생할 무전 끝 소리(PTT 놓음). 비우면 없음")]
        public AudioClip PttEnd;
        [Tooltip("PTT 소리 크기(음성 Volume에 곱한다)")]
        [Range(0f, 1f)] public float PttVolume = 0.8f;
        [Tooltip("PTT 소리 시작에서 음성 시작까지(초). 음수면 PTT 소리 길이만큼 기다린다")]
        public float PttLead = -1f;

        [Header("재생")]
        [Tooltip("상태를 확인하는 주기(초)")]
        public float PollInterval = 0.25f;
        [Tooltip("앞 음성이 끝난 뒤 다음 음성까지 쉬는 시간(초)")]
        public float Gap = 0.3f;
        [Tooltip("줄에서 이보다 오래(초) 기다린 음성은 버린다(오래된 안내가 늦게 나오지 않게)")]
        public float MaxQueueDelay = 10f;

        private const int QUEUE_SIZE = 6;
        private AudioClip[] _queue = new AudioClip[QUEUE_SIZE];
        private float[] _queuedAt = new float[QUEUE_SIZE];
        private int _queueCount;
        private float _gapTimer;
        // PTT 소리 뒤에 틀 음성과 남은 시간, 음성이 재생 중이었는지(끝나면 PTT 끝 소리)
        private AudioClip _pending;
        private float _pendingTimer;
        private bool _voiceOn;

        private float _pollTimer;
        private int _lastState = -1;
        private int _lastPhaseIndex = -2; // -2 = 아직 기준 없음
        private int _lastCleared;
        private int _lastPhaseType;

        void Update()
        {
            PumpQueue();
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f || Manager == null) { return; }
            _pollTimer = PollInterval;
            Poll();
        }

        private void Poll()
        {
            int state = Manager.GetState();
            bool running = state == ACO_MissionManager.STATE_RUNNING;
            bool finished = state == ACO_MissionManager.STATE_COMPLETE || state == ACO_MissionManager.STATE_FAILED;
            int phase = Manager.GetPhaseIndex();
            int cleared = Manager.GetPhasesCleared();

            if (_lastPhaseIndex == -2)
            {
                // 처음 본 상태(늦게 들어옴 포함): 기준만 잡고 틀지 않는다.
                _lastPhaseIndex = phase;
                _lastCleared = cleared;
                _lastPhaseType = Manager.GetCurrentPhaseType();
                _lastState = state;
                return;
            }
            if ((running && _lastState != ACO_MissionManager.STATE_RUNNING) || cleared < _lastCleared || phase < _lastPhaseIndex)
            {
                // 새 미션: "아직 페이즈 없음"을 기준으로 — 첫 페이즈가 이미 시작됐어도 아래에서 시작 음성을 튼다.
                _lastPhaseIndex = -1;
                _lastCleared = 0;
                _lastPhaseType = 0;
            }

            if (running || finished)
            {
                // 페이즈 끝: 성공/실패(시작할 때 본 유형으로)
                if (cleared > _lastCleared)
                {
                    _lastCleared = cleared;
                    bool failed = Manager.GetLastPhaseFailed();
                    Enqueue(Pick(failed ? FailClips : SuccessClips, _lastPhaseType, failed ? DefaultFail : DefaultSuccess));
                }
                // 페이즈 시작, 또는 배치가 안 돼 같은 페이즈가 다른 유형으로 바뀜
                int type = Manager.GetCurrentPhaseType();
                bool changed = running && phase == _lastPhaseIndex && phase >= 0 && Manager.IsPhaseActive() && type != _lastPhaseType;
                if (running && (phase > _lastPhaseIndex || changed) && phase >= 0)
                {
                    _lastPhaseIndex = phase;
                    _lastPhaseType = type;
                    Enqueue(Pick(StartClips, type, DefaultStart));
                }
            }

            // 미션 결과
            if (finished && _lastState == ACO_MissionManager.STATE_RUNNING)
            {
                Enqueue(state == ACO_MissionManager.STATE_COMPLETE ? MissionComplete : MissionFailed);
            }
            _lastState = state;
        }

        /// 유형별 칸(유형 1~8 → 0~7), 없으면 공통.
        private AudioClip Pick(AudioClip[] clips, int type, AudioClip fallback)
        {
            int k = type - 1;
            if (clips != null && k >= 0 && k < clips.Length && clips[k] != null) { return clips[k]; }
            return fallback;
        }

        private void Enqueue(AudioClip clip)
        {
            if (clip == null) { return; }
            if (_queueCount >= QUEUE_SIZE)
            {
                // 가장 오래된 것을 버린다
                for (int i = 1; i < _queueCount; i++) { _queue[i - 1] = _queue[i]; _queuedAt[i - 1] = _queuedAt[i]; }
                _queueCount--;
            }
            _queue[_queueCount] = clip;
            _queuedAt[_queueCount] = Time.time;
            _queueCount++;
        }

        /// 앞 음성이 끝났으면(+ PTT 끝 소리 + Gap) 줄의 다음 음성을 튼다: PTT 누름 소리 → PttLead 뒤 음성 → 끝나면 PTT 놓음 소리.
        private void PumpQueue()
        {
            if (Voice == null) { return; }
            // PTT 누름 소리가 나는 중: 때가 되면 음성
            if (_pending != null)
            {
                _pendingTimer -= Time.deltaTime;
                if (_pendingTimer > 0f) { return; }
                PlayVoice(_pending);
                _pending = null;
                return;
            }
            if (Voice.isPlaying) { _gapTimer = Gap; return; }
            // 음성이 막 끝났다: PTT 놓음 소리
            if (_voiceOn)
            {
                _voiceOn = false;
                if (PttEnd != null)
                {
                    Voice.PlayOneShot(PttEnd, PttVolume);
                    _gapTimer = Gap + PttEnd.length;
                }
                return;
            }
            if (_queueCount == 0) { return; }
            if (_gapTimer > 0f) { _gapTimer -= Time.deltaTime; return; }
            AudioClip clip = _queue[0];
            float age = Time.time - _queuedAt[0];
            for (int i = 1; i < _queueCount; i++) { _queue[i - 1] = _queue[i]; _queuedAt[i - 1] = _queuedAt[i]; }
            _queueCount--;
            if (clip == null || age > MaxQueueDelay) { return; }
            Voice.volume = Volume;
            if (PttStart != null)
            {
                Voice.PlayOneShot(PttStart, PttVolume);
                _pending = clip;
                _pendingTimer = PttLead >= 0f ? PttLead : PttStart.length;
                return;
            }
            PlayVoice(clip);
        }

        private void PlayVoice(AudioClip clip)
        {
            Voice.clip = clip;
            Voice.volume = Volume;
            Voice.Play();
            _voiceOn = true;
        }
    }
}
