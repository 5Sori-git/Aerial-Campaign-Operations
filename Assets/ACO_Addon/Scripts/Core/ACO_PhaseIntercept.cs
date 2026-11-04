using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 폭격기 요격 페이즈(모듈) — 적 폭격기(ACO_EscortUnit Hostile) 편대가 삼각 대형으로 도시 상공을 향해 온다.
    ///  - 성공: 모두 격추. 실패: 한 대라도 도시 상공(항로 끝)에 닿아 폭탄을 떨어뜨림 → 미션 실패(FailText).
    ///  - 처음 폭격기가 보이면 선두 둘레에 적 호위 편대를 한 번 놓는다(목표 아님).
    /// 자체 동기화(Manual): 호위 편대를 보냈는지(_escortsSent)와 페이즈 도장(_stamp) — 오너가 바뀌어도 두 번 보내지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseIntercept : ACO_PhaseModule
    {
        [Header("폭격기 요격")]
        [Tooltip("폭격기 수 = Base + PerPhase × (페이즈 번호 − 1)(내림)")]
        public int BombersBase = 3;
        public float BombersPerPhase = 0.5f;
        [Tooltip("항로 길이(m)")]
        public float RouteLength = 18000f;
        [Tooltip("적 호위 전투기 수 = Base + PerPhase × (페이즈 번호 − 1)")]
        public int EscortsBase = 2;
        public int EscortsPerPhase = 1;

        [UdonSynced] private int _stamp;
        [UdonSynced] private bool _escortsSent;

        private int _planSeed;
        private int _planSite = -1;
        private bool[] _seenAlive;

        private const int MODE = ACO_EscortUnit.MOVE_AIR | ACO_MissionGenerator.ESCORT_HOSTILE;

        private int BomberCount(int phaseIndex) { return Mathf.Max(BombersBase + Mathf.FloorToInt(BombersPerPhase * Mathf.Max(phaseIndex, 0)), 1); }

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null) { return false; }
            int units = Mathf.Min(Generator.CountFreeEscorts(MODE), BomberCount(phaseIndex));
            int seed = Random.Range(1, int.MaxValue);
            if (units < 1 || !Generator.CanBuildEscortRoute(MODE, focus, RouteLength, units, seed)) { return false; }
            _planSeed = seed;
            _planSite = Generator.GetLastRouteSite();
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            int count = BomberCount(phaseIndex);
            Manager.SetModuleProgress(0, count, false);   // 진행 = 격추한 폭격기 수
            Generator.RequestDeployEscort(tag, focus, RouteLength, 0, MODE, count, 0, _planSeed);
            Manager.SetModuleSite(_planSite);
            _planSeed = 0;
            _seenAlive = new bool[Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT)];
            _stamp = CurrentStamp();
            _escortsSent = false;
            SyncNow();
        }

        public override int Tick(int now)
        {
            int tag = Manager.GetModuleTag();
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            if (_seenAlive == null || _seenAlive.Length != n) { _seenAlive = new bool[n]; }
            int settleMs = Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f);
            int total = 0;
            int down = 0;
            bool pending = false;
            ACO_EscortUnit lead = null;
            for (int i = 0; i < n; i++)
            {
                ACO_EscortUnit unit = BomberAt(i, tag);
                if (unit == null) { continue; }
                total++;
                if (unit.IsDestroyed())
                {
                    if (!_seenAlive[i] && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, i) < settleMs) { pending = true; }
                    else { down++; }
                    continue;
                }
                _seenAlive[i] = true;
                if (lead == null) { lead = unit; }
                if (unit.IsArrived())
                {
                    Manager.ScheduleModuleRelease();   // 투하 후 계속 비행하다 거둔다
                    return RESULT_FAIL;
                }
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(down, total, false);   // 기본 요격과 같게 목표 파괴 이벤트로는 세지 않는다
            // 호위 전투기: 선두 폭격기 둘레에서 한 번. 이번 페이즈 값을 못 받은 새 오너는 이미 보냈다고 친다(두 번 보내지 않게).
            if (_stamp != CurrentStamp())
            {
                _stamp = CurrentStamp();
                _escortsSent = true;
                SyncNow();
            }
            if (!_escortsSent && lead != null)
            {
                int escorts = EscortsBase + EscortsPerPhase * Mathf.Max(Manager.GetPhaseIndex(), 0);
                if (escorts > 0) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, escorts, -1, lead.transform.position, 300f, 900f); }
                _escortsSent = true;
                SyncNow();
            }
            if (pending) { return RESULT_RUNNING; }
            return down >= total ? RESULT_CLEAR : RESULT_RUNNING;
        }

        public override void End()
        {
            _seenAlive = null;
            _planSeed = 0;
        }

        private ACO_EscortUnit BomberAt(int i, int tag)
        {
            if (tag == 0 || !Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != tag) { return null; }
            ACO_EscortUnit unit = Generator.GetEscort(i);
            return unit != null && unit.Hostile ? unit : null;
        }

        public override bool ShowsEscortRoute() { return true; }

        /// "BMR 3/4  CITY 4.2km" — 남은 폭격기 / 전체, 도시에 가장 가까운 폭격기의 남은 거리(5km 아래면 빨갛게).
        public override string GetProgressText()
        {
            if (Manager == null || Generator == null) { return ""; }
            int tag = Manager.GetModuleTag();
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            int total = 0;
            int alive = 0;
            float near = -1f;
            for (int i = 0; i < n; i++)
            {
                ACO_EscortUnit unit = BomberAt(i, tag);
                if (unit == null) { continue; }
                total++;
                if (unit.IsDestroyed()) { continue; }
                alive++;
                float d = unit.GetRemainingDistance();
                if (near < 0f || d < near) { near = d; }
            }
            if (total == 0) { return "-"; }
            string dist = near >= 0f ? Dist(near) : "";
            if (near >= 0f && near < 5000f) { dist = "<color=#FF5544>" + dist + "</color>"; }
            return "BMR " + alive + "/" + total + (dist.Length > 0 ? "  CITY " + dist : "");
        }

        public override string GetLocation()
        {
            if (Manager == null) { return ""; }
            string name = Manager.GetPhaseSiteName();
            return name.Length > 0 ? "DEFEND: " + name : "";
        }
    }
}
