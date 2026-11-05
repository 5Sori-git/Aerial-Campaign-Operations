using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 시한 표적 페이즈(모듈) — 이동식 탄도탄 발사대(TEL, 전용 풀 ACO_TelUnit) Count대가 하나씩 나타난다. 각 표적은 배치된 순간부터 TimeLimit초 안에 부숴야 하고, 놓치면(요격 불가) 미션 실패.
    /// 앞 LaunchDelay초는 발사 준비(지상 표적 — 공대지), 그 뒤는 탄도탄 상승(공중 표적 — 공대공). 체력은 하나라 어느 쪽이든 부수면 그 표적은 끝이다.
    /// 무전: 발견·발사·요격 한계·파괴(발사 전/후) 관제 대사 — 모든 클라이언트가 TEL 상태를 보고 각자 재생한다.
    /// 표적 사이 간격 Interval초. 첫 표적을 못 놓으면 다른 종류로 바뀌고, 이후 표적을 못 놓으면 그 표적만 건너뛴다.
    /// 성공 보너스 = 잡은 수 × HitBonus(관리자가 페이즈 클리어 보너스·배율을 더한다).
    ///
    /// 자체 동기화(Manual) — Tools/aco_sync_sim.py로 확인한 규칙:
    ///  - 동기화 값: 도장(_stamp — 이번 페이즈 시도), 표적 순번(_index), 잡은 수(_hits), 배치했는가(_deployed), 다음 배치 시각(_spawnMs). 바뀔 때마다 바로 보낸다.
    ///  - 표적마다 다른 태그(관리자 MakePhaseTag(1 + 순번)) — 오너가 바뀌어 마지막 전송이 유실돼도 새 오너가 생성기 상태에서 같은 전이를 다시 하고,
    ///    이미 센 표적은 다시 세지 않는다. 같은 태그 배치 요청이 두 번 와도 생성기가 막는다.
    ///  - 마감 = 표적의 생성기 배치 시각(동기화) + TimeLimit — 모든 클라이언트가 같게 계산(동기화하지 않는다).
    ///  - 도주 판정은 마감 + EscapeGrace(3초) — 마감 직전에 맞힌 피해가 오너에게 늦게 와도 도주가 되지 않는다(시뮬레이션: 0초 0.9%, 3초 0%).
    ///  - 보는 쪽은 도장이 관리자 페이즈 시작 시각과 다르면(이전 페이즈 값) 무시한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseTimeSensitive : ACO_PhaseModule
    {
        [Header("시한 표적")]
        [Tooltip("차례로 나타날 표적 수")]
        public int Count = 3;
        [Tooltip("표적마다 주어지는 시간(초) — 배치된 순간부터")]
        public float TimeLimit = 90f;
        [Tooltip("배치부터 탄도탄 발사까지(초) — 남은 시간은 상승(공대공) 구간. Bake가 TEL에 넣는다")]
        public float LaunchDelay = 30f;
        [Tooltip("표적 사이 간격(초)")]
        public float Interval = 8f;
        [Tooltip("표적을 놓을 거리(m) — 플레이어 평균 위치 기준")]
        public float DistanceMin = 5000f;
        public float DistanceMax = 12000f;
        [Tooltip("마감 뒤 이만큼(초) 더 기다린 뒤 도주로 판정한다(피해 이벤트 지연)")]
        public float EscapeGrace = 3f;
        [Tooltip("잡은 표적 하나당 보너스(페이즈 배율을 곱한다)")]
        public int HitBonus = 300;

        [Header("대사 (관제 — 비우면 안 나온다)")]
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        [Tooltip("음성이 이 시간(초)보다 오래 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;
        [Tooltip("요격 불가까지 이 시간(초) 남으면 LineLimit")]
        public float LimitWarnTime = 15f;
        [TextArea] public string LineSpotted = "New TEL spotted! It is setting up to fire. Hit it on the ground!";
        public AudioClip LineSpottedClip;
        [TextArea] public string LineLaunch = "Launch detected! Missile in the air! Intercept it before it leaves the atmosphere!";
        public AudioClip LineLaunchClip;
        [TextArea] public string LineLimit = "It is almost out of reach! Take the shot now!";
        public AudioClip LineLimitClip;
        [TextArea] public string LineTelDown = "TEL destroyed on the ground. Good kill.";
        public AudioClip LineTelDownClip;
        [TextArea] public string LineMissileDown = "Missile intercepted! Splash one!";
        public AudioClip LineMissileDownClip;

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _index;
        [UdonSynced] private int _hits;
        [UdonSynced] private bool _deployed;
        [UdonSynced] private int _spawnMs;

        private bool _planHasPoint;
        private Vector3 _planPoint;

        // 지금 표적(대사·진행 글자 — 모든 클라이언트 지역 상태)
        private ACO_TelUnit _tel;
        private int _telTag;
        private bool _saidLaunch;
        private bool _saidLimit;
        private bool _saidDown;
        private float _lineTimer;

        private int TargetCount() { return Mathf.Max(Count, 1); }
        private bool IsCurrent() { return Manager != null && _stamp == CurrentStamp(); }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            _planHasPoint = false;
            if (Generator == null) { return false; }
            if (!Generator.CanDeployTel() || !Generator.HasGroundNear(focus, DistanceMin, DistanceMax)) { return false; }
            _planPoint = Generator.GetCheckedPoint();
            _planHasPoint = true;
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleActiveTag(0);   // 표적마다 따로 태그를 단다
            Manager.SetModuleProgress(0, TargetCount(), false);
            Manager.SetModuleSite(_planHasPoint ? Generator.FindSiteNear(_planPoint, Manager.SiteNameRadius) : -1);
            _stamp = CurrentStamp();
            _index = 0;
            _hits = 0;
            _deployed = false;
            _spawnMs = now;
            SyncNow();
        }

        public override int Tick(int now)
        {
            if (!IsCurrent())
            {
                // 오너가 바뀌었는데 이번 페이즈 값을 못 받았다 — 생성기 상태에서 다시 찾는다: 이미 배치된 표적 중 가장 뒤 순번부터.
                Recover(now);
            }
            int count = TargetCount();
            int tag = Manager.MakePhaseTag(1 + _index);
            if (!_deployed)
            {
                if (now - _spawnMs < 0) { return RESULT_RUNNING; }
                _deployed = true;
                _spawnMs = now;
                Manager.SetModuleActiveTag(tag);
                if (_index == 0 && _planHasPoint)
                {
                    // 첫 표적은 사전 확인이 찾은 자리에(확인이 통과하면 배치도 된다).
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_TEL, tag, 1, -1, _planPoint, -1f, -1f);
                    _planHasPoint = false;
                }
                else
                {
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_TEL, tag, 1, -1, Manager.GetPlayerCenter(), DistanceMin, DistanceMax);
                }
                SyncNow();
                return RESULT_RUNNING;
            }
            Manager.SetModuleActiveTag(tag);
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - _spawnMs >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f))
                {
                    // 첫 표적부터 못 놓으면 이 페이즈는 성립하지 않는다 — 다른 종류로. 이후 표적은 그 표적만 건너뛴다.
                    if (_index == 0 && _hits == 0) { return RESULT_REPLACE; }
                    Debug.LogWarning("[ACO] 시한 표적 " + (_index + 1) + " 배치 실패 — 건너뜀");
                    return Next(now, count);
                }
                return RESULT_RUNNING;
            }
            if (Manager.GetTaggedDone() >= total)
            {
                _hits++;
                Manager.SetModuleProgress(_hits, count, true);
                Debug.Log("[ACO] 시한 표적 " + (_index + 1) + "/" + count + " 파괴");
                return Next(now, count);
            }
            int deadline = DeadlineMs(tag);
            if (deadline != 0 && now - deadline >= Mathf.RoundToInt(Mathf.Max(EscapeGrace, 0f) * 1000f))
            {
                Debug.Log("[ACO] 시한 표적 " + (_index + 1) + "/" + count + " 도주 — 미션 실패");
                Generator.RequestReleaseTag(tag);
                return RESULT_FAIL;
            }
            return RESULT_RUNNING;
        }

        private int Next(int now, int count)
        {
            _index++;
            _deployed = false;
            Manager.SetModuleActiveTag(0);
            if (_index >= count)
            {
                SyncNow();
                return RESULT_CLEAR;   // 다 지나갔다 = 모두 잡았다(도주하면 그 자리에서 미션 실패)
            }
            _spawnMs = now + Mathf.RoundToInt(Interval * 1000f);
            SyncNow();
            return RESULT_RUNNING;
        }

        /// 이번 페이즈 동기화 값이 없을 때: 순번 0부터 올라가며 배치돼 있는 표적을 찾는다 — 부서진 표적은 잡은 것으로 센다.
        /// 마지막으로 찾은(살아 있거나 막 놓인) 표적이 지금 순번. 아무것도 없으면 처음부터.
        private void Recover(int now)
        {
            _stamp = CurrentStamp();
            _index = 0;
            _hits = 0;
            _deployed = false;
            _spawnMs = now;
            int count = TargetCount();
            for (int k = 0; k < count; k++)
            {
                int tag = Manager.MakePhaseTag(1 + k);
                Manager.CountTaggedObjectives(tag);
                if (Manager.GetTaggedTotal() == 0) { break; }
                _index = k;
                _deployed = true;
                if (Manager.GetTaggedDone() >= Manager.GetTaggedTotal())
                {
                    _hits++;
                    _index = k + 1;
                    _deployed = false;
                    _spawnMs = now + Mathf.RoundToInt(Interval * 1000f);
                }
            }
            Manager.SetModuleProgress(_hits, count, false);
            SyncNow();
        }

        public override int GetClearBonus() { return _hits * HitBonus; }
        public override bool WaveOnStart() { return false; }

        public override void End()
        {
            _planHasPoint = false;
        }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.25f;
            if (!IsCurrent() || !_deployed || Generator == null) { return; }
            CheckLines();
        }

        private void CheckLines()
        {
            // 앞 표적의 파괴를 먼저 — 순번이 바로 넘어가 태그가 바뀌어도 놓치지 않게
            if (_tel != null && !_saidDown && _tel.IsDestroyed())
            {
                _saidDown = true;
                if (_saidLaunch) { Say(LineMissileDown, LineMissileDownClip); }
                else { Say(LineTelDown, LineTelDownClip); }
            }
            int tag = Manager.MakePhaseTag(1 + _index);
            if (tag != _telTag)
            {
                ACO_TelUnit tel = Generator.GetTelByTag(tag);
                if (tel == null) { return; }
                _telTag = tag;
                _tel = tel;
                // 처음 본 표적: 지금 상태를 기준으로 삼는다(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _saidLaunch = tel.IsAirborne();
                _saidLimit = false;
                _saidDown = tel.IsDestroyed();
                if (!_saidLaunch && !_saidDown) { Say(LineSpotted, LineSpottedClip); }
                return;
            }
            if (_tel == null || _tel.IsDestroyed()) { return; }
            if (!_saidLaunch && _tel.IsAirborne())
            {
                _saidLaunch = true;
                Say(LineLaunch, LineLaunchClip);
            }
            if (_saidLimit || !_tel.IsAirborne()) { return; }
            int deadline = DeadlineMs(tag);
            if (deadline != 0 && (deadline - Networking.GetServerTimeInMilliseconds()) * 0.001f <= LimitWarnTime)
            {
                _saidLimit = true;
                Say(LineLimit, LineLimitClip);
            }
        }

        private void Say(string text, AudioClip clip)
        {
            if (Chatter != null && !string.IsNullOrEmpty(text)) { Chatter.SayControl(text, clip, VoiceQueueDelay); }
        }

        // ───────────── 모든 클라이언트 ─────────────

        /// 태그 tag 표적의 마감(서버 ms) = 생성기 배치 시각 + TimeLimit. 아직 안 보이면 0.
        private int DeadlineMs(int tag)
        {
            if (Generator == null || tag == 0) { return 0; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_GROUND);
            int first = 0;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_GROUND, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_GROUND, i) != tag) { continue; }
                int t = Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, i);
                if (!found || t - first < 0) { first = t; found = true; }
            }
            return found ? first + Mathf.RoundToInt(TimeLimit * 1000f) : 0;
        }

        /// "2/3  LAUNCH 0:12"(발사까지) → "2/3  INTERCEPT 0:42  ALT 24000ft"(요격 불가까지, 탄도탄 고도) — 남은 시간은 빨갛게. 이번 페이즈 값을 아직 못 받았으면 "-".
        public override string GetProgressText()
        {
            if (!IsCurrent()) { return "-"; }
            string s = _hits + "/" + TargetCount();
            if (_deployed)
            {
                int deadline = DeadlineMs(Manager.MakePhaseTag(1 + _index));
                if (deadline != 0)
                {
                    int now = Networking.GetServerTimeInMilliseconds();
                    bool airborne = _tel != null && _telTag == Manager.MakePhaseTag(1 + _index) && _tel.IsAirborne();
                    int until = airborne || _tel == null ? deadline : _tel.GetLaunchMs();
                    int sec = Mathf.CeilToInt(Mathf.Max((until - now) * 0.001f, 0f));
                    s += "  <color=#FF5544>" + (airborne ? "INTERCEPT " : "LAUNCH ") + (sec / 60) + ":" + (sec % 60).ToString("00") + "</color>";
                    if (airborne) { s += "  ALT " + Mathf.RoundToInt(_tel.GetAltitude() * 3.28084f) + "ft"; }
                }
            }
            return s;
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description; }
            return "STOP " + TargetCount() + " BALLISTIC MISSILE LAUNCHERS - HIT THEM ON THE GROUND OR INTERCEPT THE MISSILE";
        }
    }
}
