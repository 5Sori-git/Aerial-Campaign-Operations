using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 호위 페이즈(모듈) — 아군 수송기(공중 호위 대상, Role VIP) 하나가 항로를 따라 목적지(도시/비행장)까지 간다.
    ///  - 성공: 도착(투하/착륙). 보너스 = EscortBonus × 남은 체력 비율. 실패: 격추 → 미션 실패(FailText).
    ///  - 요격 편대: 진행률 5% → 80% 사이에 InterceptWaves번, 수송기보다 앞쪽 항로 위에 적 편대를 놓는다.
    /// 자체 동기화(Manual): 보낸 요격 편대 수(_waves)와 페이즈 도장(_stamp) — 오너가 바뀌어도 같은 편대를 두 번 보내지 않는다.
    /// 나머지(호위 대상 생사·진행)는 생성기 배치 상태에서 읽는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseEscort : ACO_PhaseModule
    {
        [Header("호위")]
        [Tooltip("항로 길이(m)")]
        public float RouteLength = 32000f;
        [Tooltip("항로 아래 방공 시설 수")]
        public int Sites = 2;
        [Tooltip("도착 보너스(남은 체력 비율 × 페이즈 배율)")]
        public int EscortBonus = 1000;

        [Header("요격 편대")]
        public int InterceptWaves = 3;
        [Tooltip("편대 크기 = Base + PerPhase × (페이즈 번호 − 1)")]
        public int InterceptBase = 2;
        public int InterceptPerPhase = 1;
        [Tooltip("편대를 놓는 자리: 수송기 진행률 + 이만큼 앞")]
        public float InterceptLead = 0.12f;
        public float InterceptDistanceMin = 7000f;
        public float InterceptDistanceMax = 11000f;

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _waves;

        private int _planSeed;
        private int _planSite = -1;
        private bool _seenAlive;
        private int _bonus;

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null) { return false; }
            int seed = Random.Range(1, int.MaxValue);
            if (Generator.CountFreeEscorts(ACO_EscortUnit.MOVE_AIR) < 1 || !Generator.CanBuildEscortRoute(ACO_EscortUnit.MOVE_AIR, focus, RouteLength, 1, seed)) { return false; }
            _planSeed = seed;
            _planSite = Generator.GetLastRouteSite();
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(100, 100, false);   // 진행 = 호위 대상 체력(%)
            Generator.RequestDeployEscort(tag, focus, RouteLength, Sites, ACO_EscortUnit.MOVE_AIR, 1, 0, _planSeed);
            Manager.SetModuleSite(_planSite);
            _planSeed = 0;
            _seenAlive = false;
            _bonus = 0;
            _stamp = CurrentStamp();
            _waves = 0;
            SyncNow();
        }

        public override int Tick(int now)
        {
            int idx = FindVip(Manager.GetModuleTag());
            if (idx < 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            ACO_EscortUnit unit = Generator.GetEscort(idx);
            if (unit.IsDestroyed())
            {
                // 부활 동기화 전의 체력 0은 격추로 보지 않는다.
                int settleMs = Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f);
                if (!_seenAlive && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, idx) < settleMs) { return RESULT_RUNNING; }
                return RESULT_FAIL;
            }
            _seenAlive = true;
            int hp = Mathf.RoundToInt(unit.GetHealthFraction() * 100f);
            Manager.SetModuleProgress(hp, 100, false);
            if (unit.IsArrived())
            {
                _bonus = Mathf.RoundToInt(EscortBonus * unit.GetHealthFraction());
                Manager.ScheduleModuleRelease();   // 투하 후 계속 비행/착륙 정지 연출이 끝나면 거둔다
                return RESULT_CLEAR;
            }
            int waves = Mathf.Max(InterceptWaves, 0);
            if (_stamp != CurrentStamp())
            {
                // 오너가 바뀌었는데 이번 페이즈 값을 못 받았다 — 지금 진행률까지의 편대는 이미 보냈다고 친다(두 번 보내지 않게 — 하나 빠질 수는 있다)
                _stamp = CurrentStamp();
                _waves = 0;
                float p = unit.GetProgress();
                while (_waves < waves && p >= 0.05f + 0.75f * _waves / Mathf.Max(waves - 1, 1)) { _waves++; }
                SyncNow();
            }
            if (_waves < waves)
            {
                float threshold = 0.05f + 0.75f * _waves / Mathf.Max(waves - 1, 1);
                float progress = unit.GetProgress();
                if (progress >= threshold)
                {
                    Vector3 ahead = unit.PointAtProgress(progress + InterceptLead);
                    int count = InterceptBase + InterceptPerPhase * Mathf.Max(Manager.GetPhaseIndex(), 0);
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, count, -1, ahead, InterceptDistanceMin, InterceptDistanceMax);
                    _waves++;
                    SyncNow();
                    Manager.MarkModuleProgress();
                }
            }
            return RESULT_RUNNING;
        }

        public override int GetClearBonus() { return _bonus; }

        public override void End()
        {
            _seenAlive = false;
            _planSeed = 0;
        }

        // ───────────── 모든 클라이언트 ─────────────

        private int FindVip(int tag)
        {
            if (Generator == null || tag == 0) { return -1; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != tag) { continue; }
                ACO_EscortUnit u = Generator.GetEscort(i);
                if (u != null && u.Role == ACO_EscortUnit.ROLE_VIP) { return i; }
            }
            return -1;
        }

        public override bool ShowsEscortRoute() { return true; }

        /// "VIP 85%  12.3km" — 체력이 30% 이하면 빨갛게.
        public override string GetProgressText()
        {
            if (Manager == null) { return ""; }
            int idx = FindVip(Manager.GetModuleTag());
            if (idx < 0) { return "-"; }
            ACO_EscortUnit unit = Generator.GetEscort(idx);
            int hp = Mathf.RoundToInt(unit.GetHealthFraction() * 100f);
            string hpText = hp <= 30 ? "<color=#FF5544>" + unit.MarkerLabel + " " + hp + "%</color>" : unit.MarkerLabel + " " + hp + "%";
            return hpText + "  " + Dist(unit.GetRemainingDistance());
        }

        public override string GetLocation()
        {
            if (Manager == null) { return ""; }
            string name = Manager.GetPhaseSiteName();
            return name.Length > 0 ? "DEST: " + name : "";
        }
    }
}
