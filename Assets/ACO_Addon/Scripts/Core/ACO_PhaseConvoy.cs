using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 지상 호위 페이즈(모듈, 동기화 없음) — 아군 콘보이(지상 호위 대상, Role VIP) Size대가 항로를 따라 목적지까지 간다.
    /// 항로 옆에 방공 시설이 놓이고, 공격기(GroundAttacker) 편대가 콘보이를 노리고 들어온다.
    /// 웨이포인트 매복(6-265): 매복조(전차·헬리콥터·곡사포 — 생성기가 종류를 고른다)는 처음에 한꺼번에 깔지 않고 웨이포인트마다 차례로 —
    /// 콘보이 선두가 앞 웨이포인트를 지나는 순간 다음 웨이포인트(중간 경유점들, 마지막은 목적지) 옆에 나온다(첫 웨이포인트 몫은 출발과 함께).
    /// 판단은 콘보이 진행(배치 시각 기준 — 모든 화면이 같은 값)만으로 — 매복 대사도 각 화면이 같은 순간에 낸다. 오너가 바뀌면 새 오너는 이미 지난 웨이포인트 몫은 나간 것으로 본다.
    ///  - 성공: 살아 있는 차량이 모두 도착(정차). 보너스 = ConvoyBonus × 생존 비율. 실패: 전멸 → 미션 실패(FailText).
    /// 판정은 생성기 배치 상태만으로 한다(배치 직후 체력 0은 기다림) — 오너가 바뀌어도 그대로 이어진다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseConvoy : ACO_PhaseModule
    {
        [Header("지상 호위")]
        [Tooltip("콘보이 차량 수")]
        public int Size = 3;
        [Tooltip("항로 길이(m)")]
        public float RouteLength = 6000f;
        [Tooltip("항로 옆 방공 시설 수")]
        public int Sites = 1;
        [Tooltip("도착 보너스(생존 비율 × 페이즈 배율)")]
        public int ConvoyBonus = 1200;

        [Header("웨이포인트 매복 (6-265)")]
        [Tooltip("웨이포인트 하나에 나오는 매복조 수(조당 생성기 TankGroupSize대 · HelicopterGroupSize기 · ArtilleryGroupSize문)")]
        public int WaypointAmbushGroups = 1;
        [Tooltip("이 페이즈 번호(1부터)부터 목적지 매복조를 하나 더")]
        public int FinalAmbushExtraFromPhase = 3;

        [Header("무전 — 매복 (Bake가 Chatter를 넣는다)")]
        public ACO_Chatter Chatter;
        // 매복이 실제로 놓였는지(풀 · 자리)는 화면마다 알 수 없어 무엇이 오는지 말하지 않는 증원 대사로(6-265)
        public const string DefaultAmbushLine = "Enemy reinforcements inbound! Cover the convoy!";
        public const string DefaultFinalAmbushLine = "Enemy reinforcements inbound! Get the convoy home!";
        [Tooltip("중간 웨이포인트 매복이 나올 때(첫 웨이포인트는 시작 대사가 대신한다)")]
        [TextArea] public string AmbushLine = DefaultAmbushLine;
        public AudioClip AmbushClip;
        [Tooltip("목적지 매복이 나올 때")]
        [TextArea] public string FinalAmbushLine = DefaultFinalAmbushLine;
        public AudioClip FinalAmbushClip;
        [Tooltip("음성이 이 시간(초)보다 오래 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;

        [Header("공격기 (지상·해상 호위에만 나온다 — 생성기 전투기 풀의 공격기 NPC_Attacker)")]
        [Tooltip("공격기 수 = Base + PerPhase × (페이즈 번호 − 1). 2기 편대로 항로 쪽에서 들어와 콘보이를 노린다. 풀에 공격기가 없으면 나오지 않는다")]
        public int AttackersBase = 2;
        public int AttackersPerPhase = 1;
        [Tooltip("공격기가 나타나는 거리(m, 항로 중심에서)")]
        public float AttackerDistanceMin = 6000f;
        public float AttackerDistanceMax = 9000f;

        private int _planSeed;
        private int _planSite = -1;
        private bool[] _seenAlive;
        private float _survived = 1f;
        private int _phaseIndex;
        // 웨이포인트 매복(오너 지역 상태 — 페이즈 시작 시각 도장으로 이번 페이즈 값인지 본다)
        private int _ambushStamp;
        private int _ambushSent;
        // 매복 대사(모든 클라이언트)
        private int _lineStamp;
        private int _linesSaid;
        private float _lineTimer;

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null) { return false; }
            int units = Mathf.Min(Generator.CountFreeEscorts(ACO_EscortUnit.MOVE_GROUND), Mathf.Max(Size, 1));
            int seed = Random.Range(1, int.MaxValue);
            // 콘보이를 위협할 이동 유닛이 있어야 지상 호위가 성립한다.
            if (units < 1 || Generator.CountFreeMobileUnits() < 1 || !Generator.CanBuildEscortRoute(ACO_EscortUnit.MOVE_GROUND, focus, RouteLength, units, seed)) { return false; }
            _planSeed = seed;
            _planSite = Generator.GetLastRouteSite();
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            int size = Mathf.Max(Size, 1);
            Manager.SetModuleProgress(size, size, false);   // 진행 = 살아 있는 차량 수
            // 매복은 처음에 깔지 않는다(0) — 웨이포인트마다 Tick이 낸다
            Generator.RequestDeployEscort(tag, focus, RouteLength, Sites, ACO_EscortUnit.MOVE_GROUND, size, 0, _planSeed);
            _phaseIndex = Mathf.Max(phaseIndex, 0);
            _ambushStamp = CurrentStamp();
            _ambushSent = 0;
            int attackers = AttackersBase + AttackersPerPhase * Mathf.Max(phaseIndex, 0);
            if (attackers > 0 && Generator.CountFreeAttackers() > 0)
            {
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_ATTACK, 0, attackers, -1, focus, AttackerDistanceMin, AttackerDistanceMax);
            }
            Manager.SetModuleSite(_planSite);
            _planSeed = 0;
            _survived = 1f;
            _seenAlive = new bool[Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT)];
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
                if (unit.IsDestroyed())
                {
                    if (!_seenAlive[i] && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, i) < settleMs) { pending = true; alive++; }
                    continue;
                }
                _seenAlive[i] = true;
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
                _survived = alive / (float)total;
                Manager.ScheduleModuleRelease();   // 정차한 채로 잠시 뒀다가 거둔다
                return RESULT_CLEAR;
            }
            TickAmbush(tag);
            return RESULT_RUNNING;
        }

        /// 웨이포인트 매복(오너): 선두가 지난 웨이포인트 수만큼 매복을 낸다 — 한 번에 하나씩.
        private void TickAmbush(int tag)
        {
            ACO_EscortUnit lead = LeadVip(tag);
            if (lead == null) { return; }
            int due = AmbushDue(lead);
            int stamp = CurrentStamp();
            if (_ambushStamp != stamp)
            {
                // 페이즈 중간에 오너가 됐다: 이미 지난 웨이포인트 몫은 앞 오너가 냈다고 본다
                _ambushStamp = stamp;
                _ambushSent = due;
                _phaseIndex = Mathf.Max(Manager.GetPhaseIndex(), 0);
                return;
            }
            if (_ambushSent >= due) { return; }
            int k = _ambushSent + 1;   // 이번 매복 웨이포인트(1 = 첫 경유점, 마지막 = 목적지)
            int last = lead.GetWaypointCount() - 1;
            Vector3 at = lead.GetWaypoint(k);
            Vector3 dir = lead.GetWaypoint(Mathf.Min(k + 1, last)) - lead.GetWaypoint(k - 1);
            int groups = Mathf.Max(WaypointAmbushGroups, 0);
            if (k == last && _phaseIndex + 1 >= FinalAmbushExtraFromPhase) { groups++; }
            if (groups > 0) { Generator.RequestAmbushAt(at, dir, groups, ACO_EscortUnit.MOVE_GROUND, -1); }
            _ambushSent = k;
            Manager.MarkModuleProgress();   // 새 위협이 나왔다 — 교착 웨이브 기준 시각을 지금으로
        }

        /// 지금까지 나왔어야 할 매복 수: 웨이포인트 k(1부터)의 매복은 선두가 웨이포인트 k − 1을 지나면 나온다(k = 1은 출발과 함께).
        private int AmbushDue(ACO_EscortUnit lead)
        {
            int count = lead.GetWaypointCount();
            if (count < 2) { return 0; }
            float s = lead.GetDistanceTravelled();
            int due = 0;
            for (int k = 1; k < count; k++)
            {
                if (s >= lead.GetWaypointDistance(k - 1)) { due = k; }
            }
            return due;
        }

        /// 살아 있는 VIP 중 가장 앞선 차량(경유점이 들어온 것만).
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

        // ───────────── 매복 대사 (모든 클라이언트) ─────────────

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.5f;
            if (Manager == null || Generator == null || Chatter == null) { return; }
            if (!Manager.IsPhaseActive() || Manager.GetModule(Manager.GetCurrentPhaseType()) != this) { return; }
            ACO_EscortUnit lead = LeadVip(Manager.GetModuleTag());
            if (lead == null) { return; }
            int due = AmbushDue(lead);
            int stamp = CurrentStamp();
            if (_lineStamp != stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = stamp;
                _linesSaid = due;
                return;
            }
            if (due <= _linesSaid) { return; }
            _linesSaid = due;
            if (due < 2) { return; }   // 첫 웨이포인트 매복은 시작 대사가 대신한다
            bool isFinal = due >= lead.GetWaypointCount() - 1;
            string text = isFinal ? FinalAmbushLine : AmbushLine;
            AudioClip clip = isFinal ? FinalAmbushClip : AmbushClip;
            if (!string.IsNullOrEmpty(text) || clip != null) { Chatter.SayControl(text, clip, VoiceQueueDelay); }
        }

        public override int GetClearBonus() { return Mathf.RoundToInt(ConvoyBonus * _survived); }

        public override void End()
        {
            _seenAlive = null;
            _planSeed = 0;
        }

        private ACO_EscortUnit VipAt(int i, int tag)
        {
            if (tag == 0 || !Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != tag) { return null; }
            ACO_EscortUnit unit = Generator.GetEscort(i);
            return unit != null && unit.Role == ACO_EscortUnit.ROLE_VIP ? unit : null;
        }

        public override bool ShowsEscortRoute() { return true; }

        /// "CVY 3/4  2.1km" — 살아 있는 차량 / 전체, 가장 뒤처진 차량의 남은 거리. 절반 이하면 빨갛게.
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
            string cvy = "CVY " + alive + "/" + total;
            if (alive * 2 <= total) { cvy = "<color=#FF5544>" + cvy + "</color>"; }
            return cvy + (worst >= 0f ? "  " + Dist(worst) : "");
        }

        public override string GetLocation()
        {
            if (Manager == null) { return ""; }
            string name = Manager.GetPhaseSiteName();
            return name.Length > 0 ? "DEST: " + name : "";
        }
    }
}
