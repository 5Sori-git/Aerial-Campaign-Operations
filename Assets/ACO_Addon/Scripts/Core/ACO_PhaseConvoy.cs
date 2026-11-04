using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 지상 호위 페이즈(모듈, 동기화 없음) — 아군 콘보이(지상 호위 대상, Role VIP) Size대가 항로를 따라 목적지까지 간다.
    /// 항로 옆에 매복조(전차·헬리콥터·곡사포 — 생성기가 종류를 고른다)와 방공 시설이 놓이고, 공격기(GroundAttacker) 편대가 콘보이를 노리고 들어온다.
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
        [Tooltip("매복조 수 = Base + PerPhase × (페이즈 번호 − 1)")]
        public int AmbushBase = 2;
        public int AmbushPerPhase = 1;
        [Tooltip("도착 보너스(생존 비율 × 페이즈 배율)")]
        public int ConvoyBonus = 1200;

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
            int ambush = AmbushBase + AmbushPerPhase * Mathf.Max(phaseIndex, 0);
            Generator.RequestDeployEscort(tag, focus, RouteLength, Sites, ACO_EscortUnit.MOVE_GROUND, size, ambush, _planSeed);
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
            return RESULT_RUNNING;
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
