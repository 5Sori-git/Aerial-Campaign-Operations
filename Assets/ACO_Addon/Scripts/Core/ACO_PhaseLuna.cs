using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// UAV 에이스 LUNA 페이즈(공중 보스, 단독 + 요격 UAV) — 설계 6-137.
    /// LUNA(ACO_LunaAce)가 생성된 도시 상공을 돌며 레이저로 도시를 조준(유도)한다. DesignateTime초 안에 격추하지 못하면 궤도 폭격이 도시에 떨어져 미션 실패,
    /// 격추하면 유도가 끊겨 폭격이 도시에서 MissDistance 떨어진 곳에 빗나간다(성공). 폭격 연출은 ACO_OrbitalStrike(이 모듈의 자식)가 동기화 값으로 모든 화면에서 같게 그린다.
    /// UAV 전개: 처음 LUNA를 노린 미사일이 날아오거나 플레이어가 DeployPlayerDistance 안에 들어오면 4기 전개(1차). 다 잃으면 RedeployDelay초 뒤 한 번 더(2차).
    /// UAV 움직임·요격·생사는 ACO_LunaAce와 UAV의 ACO_AAGunSite(CIWS 요격)가 한다 — 이 모듈은 전개 시각만 동기화한다.
    ///
    /// 자체 동기화(Manual): 도시 자리, 유도 시작 시각, 빗나감 방위, UAV 차수·전개 시각·전멸 시각, 격추 시각, 실패 시각. 판정은 관리자 오너만. 페이즈 도장으로 지난 값을 무시한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseLuna : ACO_PhaseBoss
    {
        [Header("배치")]
        [Tooltip("LUNA가 나타나는 거리: 도시에서(m)")]
        public float SpawnDistanceMin = 5000f;
        public float SpawnDistanceMax = 8000f;
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초) — 유도 시간보다 넉넉히")]
        public float ExtraTime = 600f;

        [Header("유도·궤도 폭격")]
        [Tooltip("유도가 끝나 궤도 폭격이 도시에 떨어지기까지(초) — 페이즈 시작부터")]
        public float DesignateTime = 300f;
        [Tooltip("LUNA를 격추하고 이만큼(초) 있다가 빗나간 폭격이 하늘에서 내려오기 시작한다(착탄은 그 뒤 StrikeFallTime)")]
        public float MissDelay = 5f;
        [Tooltip("빗나간 폭격이 떨어지는 자리: 도시에서 이 거리(m)")]
        public float MissDistance = 4000f;
        [Tooltip("폭격이 하늘에서 내려오는 시간(초) — 착탄 이만큼 전부터 빛줄기가 보인다")]
        public float StrikeFallTime = 4f;

        [Header("UAV 전개")]
        [Tooltip("LUNA를 노린 미사일이 이 거리(m) 안에 있으면 전개")]
        public float DeployMissileDistance = 8000f;
        [Tooltip("플레이어가 LUNA에서 이 거리(m) 안에 들어오면 전개")]
        public float DeployPlayerDistance = 3000f;
        [Tooltip("UAV를 다 잃고 이만큼(초) 뒤 한 번 더 전개(2차 — 그 뒤로는 없다)")]
        public float RedeployDelay = 60f;

        [Header("끝맺음")]
        [Tooltip("빗나간 폭격이 떨어진 뒤 성공으로 넘어가기까지(초)")]
        public float ClearDelay = 5f;
        [Tooltip("도시에 폭격이 떨어진 뒤 미션 실패로 넘어가기까지(초)")]
        public float FailDelay = 7f;

        [Header("연출")]
        [Tooltip("궤도 폭격 빛줄기·대폭발(이 모듈의 자식 — 빌더가 만든다)")]
        public ACO_OrbitalStrike Strike;

        public const int SIT_SPAWN = 0;
        public const int SIT_UAV = 1;
        public const int SIT_UAV_LOST = 2;
        public const int SIT_UAV_AGAIN = 3;
        public const int SIT_T120 = 4;
        public const int SIT_T60 = 5;
        public const int SIT_T30 = 6;
        public const int SIT_DOWN = 7;
        public const int SIT_IMPACT = 8;

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        // LUNA 대사 주제: 달처럼 위에서 내려다보는 차가운 관측자 — 도시를 비추는 빛이 곧 심판. 등장·격추 대사는 에이스 스크립트(ACO_LunaAce)에 있다.
        // (AWACS 등장 설명 L01은 페이즈 시작 대사와 겹쳐 뺐다 — 음성 파일 번호는 L02부터)
        public int[] LineEvent = new int[] {
            SIT_SPAWN,
            SIT_UAV, SIT_UAV,
            SIT_UAV_LOST,
            SIT_UAV_AGAIN, SIT_UAV_AGAIN,
            SIT_T120, SIT_T120,
            SIT_T60, SIT_T60,
            SIT_T30, SIT_T30,
            SIT_DOWN,
            SIT_IMPACT, SIT_IMPACT };
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS, SPEAKER_ENEMY };
        public string[] LineText = new string[] {
            "The city is lit. All that is left is for the sky to fall.",
            "Little moons, rise. Keep them away from me.",
            "She has launched interceptor drones! They will shoot down missiles near her. Kill the drones first, or fire everything at once!",
            "All drones are down! She is wide open, hit her now!",
            "Again, little moons. Rise.",
            "She is launching a second wave of drones!",
            "Two minutes until the strike!",
            "Two minutes. Look up, and remember how small you are.",
            "One minute! Bring her down!",
            "One minute. The light never misses.",
            "Thirty seconds! Do it now!",
            "Thirty seconds. Close your eyes.",
            "LUNA is down! The beam is gone... the strike has lost its target!",
            "Impact on the city... It is gone.",
            "And the night falls quiet." };
        public AudioClip[] LineClip = new AudioClip[15];

        [UdonSynced] private int _stamp;
        [UdonSynced] private Vector3 _city;
        [UdonSynced] private int _startMs;     // 유도 시작(= 페이즈 시작)
        [UdonSynced] private int _missYaw;     // 빗나간 폭격 방위(도)
        [UdonSynced] private int _uavWave;     // 0 = 전개 전, 1 = 1차, 2 = 2차
        [UdonSynced] private int _uavMs;       // 이번 차수 전개 시각
        [UdonSynced] private int _uavLostMs;   // 이번 차수 전멸 시각(0 = 아직)
        [UdonSynced] private int _downMs;      // LUNA 격추 시각(0 = 아직)
        [UdonSynced] private int _failMs;      // 도시 착탄 시각(0 = 아직)

        // 오너 지역 상태
        private Vector3 _planCity;
        private int _planSite = -1;
        private bool _planOk;

        // 대사(모든 클라이언트)
        private int _lineStamp;
        private int _saidWave;
        private bool _saidLost;
        private int _saidLeft;
        private bool _saidDown;
        private bool _saidImpact;
        private float _lineTimer;

        private ACO_LunaAce Ace() { return Generator != null ? (ACO_LunaAce)Generator.GetAce(ACO_AceBase.KIND_LUNA) : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployAce(ACO_AceBase.KIND_LUNA)) { return false; }
            _planSite = Generator.FindCitySite(focus);
            _planOk = _planSite >= 0;
            if (_planOk) { _planCity = Generator.GetSitePosition(_planSite); }
            return _planOk;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 100, false);
            if (!_planOk && !IsFeasible(phaseIndex, focus)) { return; }
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_LUNA, tag, 0, -1, _planCity, SpawnDistanceMin, SpawnDistanceMax);
            Manager.SetModuleSite(_planSite);
            Manager.AddModuleTime(ExtraTime);
            StartAt(_planCity, now);
        }

        private void StartAt(Vector3 city, int now)
        {
            _stamp = CurrentStamp();
            _city = city;
            _startMs = now;
            _missYaw = Random.Range(0, 360);
            _uavWave = 0;
            _uavMs = 0;
            _uavLostMs = 0;
            _downMs = 0;
            _failMs = 0;
            SyncNow();
        }

        public override int Tick(int now)
        {
            ACO_LunaAce ace = Ace();
            if (ace == null) { return RESULT_REPLACE; }
            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (_stamp != CurrentStamp())
            {
                // 이번 페이즈 값을 못 받은 새 오너: 가장 가까운 도시를 다시 정한다(드문 경우)
                if (total == 0) { return now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f) ? RESULT_REPLACE : RESULT_RUNNING; }
                int site = Generator.FindCitySite(ace.transform.position);
                Vector3 city = site >= 0 ? Generator.GetSitePosition(site) : new Vector3(ace.transform.position.x, 0f, ace.transform.position.z);
                StartAt(city, Manager.GetPhaseStartMs());
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            // 실패: 도시에 떨어졌다
            if (_failMs != 0)
            {
                if (now - _failMs < Mathf.RoundToInt(FailDelay * 1000f)) { return RESULT_RUNNING; }
                Generator.RequestReleaseTag(tag);
                return RESULT_FAIL;
            }
            // 성공: 격추 → 빗나간 폭격이 떨어지고 ClearDelay 뒤
            if (Manager.GetTaggedDone() >= total)
            {
                if (_downMs == 0)
                {
                    _downMs = now;
                    SyncNow();
                }
                if (now - GetStrikeMs() < Mathf.RoundToInt(ClearDelay * 1000f)) { return RESULT_RUNNING; }
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            if (now - GetDesignateEndMs() >= 0)
            {
                _failMs = GetDesignateEndMs();
                SyncNow();
                Debug.Log("[ACO] LUNA: 유도 완료 — 궤도 폭격이 도시에 떨어졌다");
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(Mathf.RoundToInt((1f - AceHealthRatio(ace)) * 100f), 100, false);
            OwnerUavs(ace, now);
            return RESULT_RUNNING;
        }

        /// UAV 전개 판정: 1차(위협이 보이면), 전멸 기록, 2차(전멸 뒤 RedeployDelay).
        private void OwnerUavs(ACO_LunaAce ace, int now)
        {
            if (!ace.IsAceAlive()) { return; }
            if (_uavWave == 0)
            {
                if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return; }
                if (!ace.ThreatNear(DeployMissileDistance, DeployPlayerDistance)) { return; }
                _uavWave = 1;
                _uavMs = now;
                _uavLostMs = 0;
                SyncNow();
                Debug.Log("[ACO] LUNA: UAV 1차 전개");
                return;
            }
            if (_uavLostMs == 0)
            {
                // 전개 직후엔 부활(체력 복구)이 퍼지기를 기다린다
                if (now - _uavMs < 4000 || ace.UavAliveCount() > 0) { return; }
                _uavLostMs = now;
                SyncNow();
                return;
            }
            if (_uavWave == 1 && now - _uavLostMs >= Mathf.RoundToInt(RedeployDelay * 1000f))
            {
                _uavWave = 2;
                _uavMs = now;
                _uavLostMs = 0;
                SyncNow();
                Debug.Log("[ACO] LUNA: UAV 2차 전개");
            }
        }

        private float AceHealthRatio(ACO_LunaAce ace)
        {
            if (ace == null || ace.TargetHealth == null || ace.TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(ace.TargetHealth.Health / ace.TargetHealth.FullHealth);
        }

        public override void End()
        {
            _planOk = false;
            _planSite = -1;
        }

        public override bool HoldsResult() { return IsLive() && (_downMs != 0 || _failMs != 0); }

        // ───────────── 모든 클라이언트 ─────────────

        /// 이번 페이즈 값을 받았는가.
        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        public Vector3 GetCity() { return _city; }
        public int GetStartMs() { return _startMs; }
        public int GetDesignateEndMs() { return _startMs + Mathf.RoundToInt(DesignateTime * 1000f); }
        public int GetUavWave() { return IsLive() ? _uavWave : 0; }
        public int GetUavMs() { return _uavMs; }
        public bool IsUavLost() { return IsLive() && _uavLostMs != 0; }
        public int GetDownMs() { return IsLive() ? _downMs : 0; }
        public bool IsFailed() { return IsLive() && _failMs != 0; }

        /// 폭격이 떨어지는(착탄) 시각: 격추했으면 격추 + MissDelay(기다림) + StrikeFallTime(내려옴), 아니면 유도 끝.
        public int GetStrikeMs() { return _downMs != 0 ? _downMs + Mathf.RoundToInt((MissDelay + StrikeFallTime) * 1000f) : GetDesignateEndMs(); }

        /// 폭격이 떨어지는 자리(땅 높이는 연출이 잰다): 격추했으면 빗나간 자리, 아니면 도시.
        public Vector3 GetStrikePoint()
        {
            if (_downMs == 0) { return _city; }
            float a = _missYaw * Mathf.Deg2Rad;
            return _city + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * MissDistance;
        }

        /// 아직 유도 중인가(LUNA가 살아 있고 시간이 남았다) — LUNA가 도시를 비춘다.
        public bool IsDesignating()
        {
            return IsLive() && _downMs == 0 && _failMs == 0 && Networking.GetServerTimeInMilliseconds() - GetDesignateEndMs() < 0;
        }

        /// 착탄까지 남은 초(끝났으면 0).
        public float GetSecondsLeft()
        {
            if (!IsLive()) { return -1f; }
            return Mathf.Max((GetDesignateEndMs() - Networking.GetServerTimeInMilliseconds()) * 0.001f, 0f);
        }

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.5f;
            if (IsLive()) { CheckLines(); }
        }

        /// 대사: 동기화된 값이 바뀌었으면 그 상황의 대사를 한 번 말한다.
        private void CheckLines()
        {
            if (Chatter == null) { return; }
            float left = GetSecondsLeft();
            bool down = _downMs != 0;
            bool impact = _failMs != 0;
            bool lost = _uavLostMs != 0;
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidWave = _uavWave;
                _saidLost = lost;
                _saidLeft = Mathf.CeilToInt(left);
                _saidDown = down;
                _saidImpact = impact;
                if (_uavWave == 0 && !down && !impact) { Say(SIT_SPAWN); }
                return;
            }
            if (impact && !_saidImpact)
            {
                _saidImpact = true;
                Chatter.ClearQueue();
                Say(SIT_IMPACT);
                return;
            }
            if (down && !_saidDown)
            {
                _saidDown = true;
                if (!impact) { Say(SIT_DOWN); }
                return;
            }
            if (down || impact) { return; }
            if (_uavWave != _saidWave)
            {
                _saidWave = _uavWave;
                _saidLost = false;
                if (_uavWave == 1) { Say(SIT_UAV); }
                else if (_uavWave == 2) { Say(SIT_UAV_AGAIN); }
                return;
            }
            if (lost && !_saidLost)
            {
                _saidLost = true;
                Say(SIT_UAV_LOST);
                return;
            }
            int s = Mathf.CeilToInt(left);
            if (_saidLeft > 120 && s <= 120) { Say(SIT_T120); }
            else if (_saidLeft > 60 && s <= 60) { Say(SIT_T60); }
            else if (_saidLeft > 30 && s <= 30) { Say(SIT_T30); }
            _saidLeft = s;
        }

        private void Say(int situation) { SayTable(situation, "", LineEvent, LineSpeaker, LineText, LineClip); }

        private string AceName()
        {
            ACO_LunaAce ace = Ace();
            return ace != null ? ace.AceName : "LUNA";
        }

        public override string GetName()
        {
            return PhaseName.Length > 0 && PhaseName != "MODULE" ? PhaseName.Replace("{ACE}", AceName()) : "ACE - " + AceName();
        }

        public override string GetDescription()
        {
            return (Description.Length > 0 ? Description : "SHOOT DOWN {ACE} BEFORE THE ORBITAL STRIKE HITS THE CITY").Replace("{ACE}", AceName());
        }

        /// "STRIKE 4:12  LUNA 72%  UAV 3"(1분 아래면 빨갛게).
        public override string GetProgressText()
        {
            if (!IsLive()) { return ""; }
            ACO_LunaAce ace = Ace();
            string t = "";
            float left = GetSecondsLeft();
            if (_downMs == 0 && left >= 0f)
            {
                int s = Mathf.CeilToInt(left);
                t = "STRIKE " + (s / 60) + ":" + (s % 60).ToString("00");
                if (s <= 60) { t = "<color=#FF5544>" + t + "</color>"; }
                t += "  ";
            }
            t += AceName() + " " + Mathf.RoundToInt(AceHealthRatio(ace) * 100f) + "%";
            if (ace != null && _uavWave > 0) { t += "  UAV " + ace.UavAliveCount(); }
            return t;
        }
    }
}
