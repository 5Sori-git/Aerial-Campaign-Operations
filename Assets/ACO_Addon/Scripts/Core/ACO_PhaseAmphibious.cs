using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 상륙전 페이즈(모듈) — 초수평선 상륙. 상륙 지점 = 1순위 항구, 2순위 도시 근처 해안, 3순위 아무 해변(절벽 아님 — 생성기 BuildAmphibiousRoute).
    /// 강습상륙함(해상, Role MOTHERSHIP)이 상륙 지점에서 RouteLength 떨어진 후방 바다에 자리 잡고 대기하고,
    /// 호위함(해상, Role GUARD)이 그 좌우에서 대기한다. 상륙함에서 LCAC(공기부양, Role VIP — 지켜야 하는 대상)가 횡대로 나와 바다를 건너
    /// 해변까지 올라가고, 아군 공격헬기(공중, Role GUARD)가 같은 길을 먼저 날아가 해안 상공에서 적과 싸운다.
    /// 항구 둘레엔 해안 방어(방공 시설, 해안 전차, 곡사포, 적 헬리콥터), 항로 옆엔 적 함대가 기다리고, 공격기(GroundAttacker) 편대가 해변 쪽에서 들어온다.
    /// LCAC 항로는 해변 직전까지 바다만 지난다.
    ///  - 성공: 살아 있는 LCAC가 모두 해변에 도착(정지). 보너스 = ClearBonus × 생존 비율.
    ///  - 실패: LCAC가 모두 격파 → 미션 실패(FailText). 상륙함·호위함·공격헬기는 잃어도 실패가 아니다.
    ///  - 배치가 DeployTimeout 안에 안 보이면 다른 종류로 바뀐다(상륙할 해안이 없는 지형 등은 사전 확인에서 걸러진다).
    /// 생성기 배치: ACO_MissionGenerator.RequestDeployAmphibious(거리·간격 값은 생성기 인스펙터 "상륙전 호위").
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseAmphibious : ACO_PhaseModule
    {
        [Header("상륙 부대 (생성기 유닛 풀의 아군 호위 대상에서 고른다)")]
        [Tooltip("LCAC 수(공기부양 MoveMode 3, Role VIP) — 지켜야 하는 대상")]
        [Range(1, 8)] public int LandingCraft = 3;
        [Tooltip("아군 공격헬기 수(공중, Role GUARD) — LCAC보다 먼저 해안에 가서 싸운다")]
        [Range(0, 8)] public int AttackHelicopters = 2;
        [Tooltip("강습상륙함 수(해상, Role MOTHERSHIP) — 후방에서 대기")]
        [Range(0, 4)] public int AssaultShips = 1;
        [Tooltip("호위함 수(해상, Role GUARD) — 상륙함 좌우에서 대기하며 지원")]
        [Range(0, 6)] public int GuardShips = 2;
        [Tooltip("상륙함 대기 거리 = LCAC 항로 길이(m). 해안 곡사포(사거리 6km)·방공 밖이 되게")]
        public float RouteLength = 10000f;

        [Header("해안 방어 (페이즈 번호가 오를수록 PerPhase만큼 늘어난다, 각 최대 15)")]
        public int ShoreSites = 2;
        public float ShoreSitesPerPhase = 0.5f;
        [Tooltip("해안 전차 조 수(조당 생성기 TankGroupSize대)")]
        public int TankGroups = 2;
        public float TankGroupsPerPhase = 0.5f;
        [Tooltip("곡사포 조 수(조당 ArtilleryGroupSize문)")]
        public int ArtilleryGroups = 1;
        public float ArtilleryGroupsPerPhase = 0.25f;
        [Tooltip("적 헬리콥터 조 수(조당 생성기 HelicopterGroupSize기)")]
        public int EnemyHelicopterGroups = 1;
        public float EnemyHelicopterGroupsPerPhase = 0.25f;
        [Tooltip("항로 옆 적 함대 수")]
        public int EnemyFleets = 1;
        public float EnemyFleetsPerPhase = 0f;

        [Header("공격기 (지상·해상 호위에만 나온다 — 생성기 전투기 풀의 공격기 NPC_Attacker)")]
        [Tooltip("공격기 수 = Base + PerPhase × (페이즈 번호 − 1). 2기 편대로 상륙 해변 쪽에서 들어와 LCAC·호위함을 노린다. 풀에 공격기가 없으면 나오지 않는다")]
        public int AttackersBase = 2;
        public int AttackersPerPhase = 1;
        [Tooltip("공격기가 나타나는 거리(m, 상륙 해변에서)")]
        public float AttackerDistanceMin = 6000f;
        public float AttackerDistanceMax = 9000f;

        [Header("2차 공습 (6-265) — 선두 LCAC가 해변까지 StrikeRemaining 안에 들어오면 한 번")]
        [Tooltip("해변까지 남은 거리(m) — 항로 10km 중 7km 지점")]
        public float StrikeRemaining = 3000f;
        [Tooltip("공격기 수 = Base + PerPhase × (페이즈 번호 − 1). 해변 쪽 AttackerDistance에서 들어온다. 풀에 공격기가 없으면 헬기만")]
        public int StrikeAttackersBase = 2;
        public int StrikeAttackersPerPhase = 1;
        [Tooltip("적 헬리콥터 조 수(조당 생성기 HelicopterGroupSize기) — LCAC 앞 바다 위 항로 옆으로 나온다")]
        public int StrikeHelicopterGroups = 1;
        [Tooltip("헬기가 자리 잡는 곳: 선두 LCAC 앞 이 거리(m)의 항로 옆")]
        public float StrikeHelicopterAhead = 1200f;

        [Header("무전 — 2차 공습 (Bake가 Chatter를 넣는다)")]
        public ACO_Chatter Chatter;
        // 공격기 · 헬기가 실제로 놓였는지(풀)는 화면마다 알 수 없어 무엇이 오는지 말하지 않는 증원 대사로(6-265)
        public const string DefaultStrikeLine = "Enemy reinforcements inbound! Cover the landing craft!";
        [TextArea] public string StrikeLine = DefaultStrikeLine;
        public AudioClip StrikeClip;
        [Tooltip("음성이 이 시간(초)보다 오래 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;

        // 이동 시간만큼 제한 시간을 늘리는 건 기반 클래스 GrantTravelTime(모듈 추가 메뉴가 켠다)

        // 사전 확인 결과(오너)
        private int _planSeed;
        private int _planSite = -1;
        private Vector3 _planLanding;
        // 진행 판정(오너 지역 상태 — 오너가 바뀌면 처음부터 다시 본다)
        private bool[] _seenAlive;
        // 2차 공습(오너 지역 상태 — 페이즈 시작 시각 도장으로 이번 페이즈 값인지 본다)
        private int _strikeStamp;
        private bool _strikeSent;
        private int _phaseIndex;
        // 2차 공습 대사(모든 클라이언트)
        private int _lineStamp;
        private bool _lineSaid;
        private float _lineTimer;

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null) { return false; }
            if (Generator.CountFreeEscortsRole(ACO_EscortUnit.MOVE_HOVER, ACO_EscortUnit.ROLE_VIP) < 1) { return false; }
            int seed = Random.Range(1, int.MaxValue);
            if (!Generator.CanBuildAmphibiousRoute(RouteLength, seed)) { return false; }
            _planSeed = seed;
            _planSite = Generator.GetLastRouteSite();
            _planLanding = Generator.GetLastLandingPoint();
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            int units = Mathf.Clamp(LandingCraft, 1, 15) | (Mathf.Clamp(AssaultShips, 0, 15) << 4)
                | (Mathf.Clamp(GuardShips, 0, 15) << 8) | (Mathf.Clamp(AttackHelicopters, 0, 15) << 12);
            int defense = Scale(ShoreSites, ShoreSitesPerPhase, phaseIndex)
                | (Scale(TankGroups, TankGroupsPerPhase, phaseIndex) << 4)
                | (Scale(ArtilleryGroups, ArtilleryGroupsPerPhase, phaseIndex) << 8)
                | (Scale(EnemyHelicopterGroups, EnemyHelicopterGroupsPerPhase, phaseIndex) << 12)
                | (Scale(EnemyFleets, EnemyFleetsPerPhase, phaseIndex) << 16);
            Generator.RequestDeployAmphibious(tag, RouteLength, units, defense, _planSeed);
            // 공격기: 상륙 지점(항구·해안선) 쪽으로
            int attackers = AttackersBase + AttackersPerPhase * Mathf.Max(phaseIndex, 0);
            if (attackers > 0 && Generator.CountFreeAttackers() > 0)
            {
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_ATTACK, 0, attackers, -1, _planLanding, AttackerDistanceMin, AttackerDistanceMax);
            }
            Manager.SetModuleSite(_planSite);
            Manager.SetModuleProgress(0, 0, false);
            _survived = 1f;
            _seenAlive = new bool[Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT)];
            _planSeed = 0;
            _phaseIndex = Mathf.Max(phaseIndex, 0);
            _strikeStamp = CurrentStamp();
            _strikeSent = false;
        }

        private int Scale(int baseCount, float perPhase, int phaseIndex)
        {
            return Mathf.Clamp(baseCount + Mathf.FloorToInt(perPhase * Mathf.Max(phaseIndex, 0)), 0, 15);
        }

        public override int Tick(int now)
        {
            int tag = Manager.GetModuleTag();
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            if (_seenAlive == null || _seenAlive.Length != n) { _seenAlive = new bool[n]; }
            int settleMs = Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f);
            int total = 0;
            int alive = 0;
            int arrived = 0;
            bool pending = false;
            for (int i = 0; i < n; i++)
            {
                ACO_EscortUnit unit = VipAt(i, tag);
                if (unit == null) { continue; }
                total++;
                if (Manager.IsUnitGone(ACO_MissionGenerator.CAT_ESCORT, i))   // 6-338
                {
                    // 부활 동기화 전의 체력 0은 격파로 보지 않는다.
                    if (!_seenAlive[i] && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, i) < settleMs) { pending = true; alive++; }
                    continue;
                }
                if (!unit.IsDestroyed()) { _seenAlive[i] = true; }   // 확정 전의 체력 0은 "살아 있는 걸 봤다"가 아니다(6-338)
                alive++;
                if (unit.IsArrived()) { arrived++; }
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(alive, total, false);
            if (pending) { return RESULT_RUNNING; }
            if (alive == 0) { return RESULT_FAIL; }
            if (arrived >= alive)
            {
                // 보너스는 살아서 해변에 닿은 LCAC 비율만큼(관리자가 페이즈 배율을 곱한다)
                _survived = alive / (float)total;
                Manager.ScheduleModuleRelease();
                return RESULT_CLEAR;
            }
            TickStrike(tag);
            return RESULT_RUNNING;
        }

        /// 2차 공습(오너): 선두 LCAC가 해변까지 StrikeRemaining 안에 들어오면 한 번 — 공격기 편대 + LCAC 앞 바다 위 적 헬기.
        private void TickStrike(int tag)
        {
            ACO_EscortUnit lead = LeadVip(tag);
            if (lead == null) { return; }
            bool inside = lead.GetRemainingDistance() <= StrikeRemaining;
            int stamp = CurrentStamp();
            if (_strikeStamp != stamp)
            {
                // 페이즈 중간에 오너가 됐다: 이미 안쪽이면 앞 오너가 냈다고 본다
                _strikeStamp = stamp;
                _strikeSent = inside;
                _phaseIndex = Mathf.Max(Manager.GetPhaseIndex(), 0);
                return;
            }
            if (_strikeSent || !inside) { return; }
            _strikeSent = true;
            int last = lead.GetWaypointCount() - 1;
            Vector3 landing = lead.GetWaypoint(last);
            int attackers = StrikeAttackersBase + StrikeAttackersPerPhase * _phaseIndex;
            if (attackers > 0 && Generator.CountFreeAttackers() > 0)
            {
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_ATTACK, 0, attackers, -1, landing, AttackerDistanceMin, AttackerDistanceMax);
            }
            if (StrikeHelicopterGroups > 0)
            {
                float s = lead.GetDistanceTravelled() + StrikeHelicopterAhead;
                Vector3 at = lead.PointAtDistance(s);
                Vector3 dir = lead.PointAtDistance(s + 100f) - lead.PointAtDistance(s - 100f);
                Generator.RequestAmbushAt(at, dir, StrikeHelicopterGroups, ACO_EscortUnit.MOVE_HOVER, ACO_MobileUnit.KIND_HELICOPTER);
            }
            Manager.MarkModuleProgress();   // 새 위협이 나왔다 — 교착 웨이브 기준 시각을 지금으로
        }

        /// 살아 있는 LCAC 중 가장 앞선 것(항로가 들어온 것만).
        private ACO_EscortUnit LeadVip(int tag)
        {
            if (Generator == null || tag == 0) { return null; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            ACO_EscortUnit best = null;
            float bestS = -1f;
            for (int i = 0; i < n; i++)
            {
                ACO_EscortUnit unit = VipAt(i, tag);
                if (unit == null || unit.IsDestroyed() || unit.GetWaypointCount() < 2) { continue; }
                float s = unit.GetDistanceTravelled();
                if (s > bestS) { bestS = s; best = unit; }
            }
            return best;
        }

        // ───────────── 2차 공습 대사 (모든 클라이언트) ─────────────

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.5f;
            if (Manager == null || Generator == null || Chatter == null) { return; }
            if (!Manager.IsPhaseActive() || Manager.GetModule(Manager.GetCurrentPhaseType()) != this) { return; }
            ACO_EscortUnit lead = LeadVip(Manager.GetModuleTag());
            if (lead == null) { return; }
            bool inside = lead.GetRemainingDistance() <= StrikeRemaining;
            int stamp = CurrentStamp();
            if (_lineStamp != stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = stamp;
                _lineSaid = inside;
                return;
            }
            if (_lineSaid || !inside) { return; }
            _lineSaid = true;
            if (!string.IsNullOrEmpty(StrikeLine) || StrikeClip != null) { Chatter.SayControl(StrikeLine, StrikeClip, VoiceQueueDelay); }
        }

        private float _survived = 1f;

        /// 성공 보너스 = ClearBonus × 살아서 해변에 닿은 LCAC 비율.
        public override int GetClearBonus() { return Mathf.RoundToInt(ClearBonus * _survived); }

        public override void End()
        {
            _seenAlive = null;
            _planSeed = 0;
        }

        // ───────────── 모든 클라이언트 ─────────────

        /// 태그가 tag인 배치된 LCAC(VIP)이면 그 유닛.
        private ACO_EscortUnit VipAt(int i, int tag)
        {
            if (tag == 0 || !Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != tag) { return null; }
            ACO_EscortUnit unit = Generator.GetEscort(i);
            if (unit == null || unit.Role != ACO_EscortUnit.ROLE_VIP) { return null; }
            return unit;
        }

        /// "LCAC 3/3  3.4km" — 살아 있는 LCAC / 전체, 가장 뒤처진 LCAC의 남은 거리. 절반 이하면 빨갛게.
        public override string GetProgressText()
        {
            if (Manager == null || Generator == null) { return ""; }
            int tag = Manager.GetModuleTag();
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            int total = 0;
            int alive = 0;
            float worst = -1f;
            for (int i = 0; i < n; i++)
            {
                ACO_EscortUnit unit = VipAt(i, tag);
                if (unit == null) { continue; }
                total++;
                if (unit.IsDestroyed()) { continue; }
                alive++;
                worst = Mathf.Max(worst, unit.GetRemainingDistance());
            }
            if (total == 0) { return "-"; }
            string lst = "LCAC " + alive + "/" + total;
            if (alive * 2 <= total) { lst = "<color=#FF5544>" + lst + "</color>"; }
            return lst + (worst >= 0f ? "  " + Dist(worst) : "");
        }

        public override string GetLocation()
        {
            // 작전 지역 이름(항구·도시·가까운 자리 — 해변 근처에 자리가 없으면 비어 있다)은 관리자가 "AO: 이름"으로 만든다 — 상륙은 목적지라 "LANDING: 이름"
            if (Manager == null || Generator == null) { return ""; }
            string name = Manager.GetPhaseSiteName();
            return name.Length > 0 ? "LANDING: " + name : "";
        }
    }
}
