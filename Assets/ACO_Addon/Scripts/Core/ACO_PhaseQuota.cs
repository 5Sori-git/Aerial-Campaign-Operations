using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 점수 할당량 페이즈(모듈, 동기화 없음) — 페이즈 시작 후 얻은 팀 점수가 목표(QuotaScore × 페이즈 배율)에 닿으면 클리어.
    /// 사냥터(지상 GroundUnits대)와 편대(Fighters기)를 놓지만 무엇을 잡아도 된다 — 표적 마커에 목표 표시를 하지 않는다.
    /// 진행 값 = 얻은 점수(관리자 팀 점수 − 페이즈 시작 점수, 둘 다 동기화) — 오너가 바뀌어도 그대로 이어진다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseQuota : ACO_PhaseModule
    {
        [Header("점수 할당량")]
        [Tooltip("얻어야 하는 점수(페이즈 배율을 곱한다 — 격추 점수도 같은 배율이라 필요한 격추 수는 같다)")]
        public int QuotaScore = 1500;
        [Tooltip("사냥터 지상 유닛 수")]
        public int GroundUnits = 8;
        [Tooltip("사냥터로 오는 전투기 수")]
        public int Fighters = 2;

        private bool _planHasPoint;
        private Vector3 _planPoint;
        private int _planSite = -1;

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            _planHasPoint = false;
            _planSite = -1;
            if (Generator == null || Manager == null) { return false; }
            // 사냥터든 편대든 점수를 얻을 거리가 있으면 된다. 사냥터 자리를 찾았으면 그 자리에.
            bool ground = Generator.CountFreeLandGround() >= 3 && Generator.HasGroundNear(focus, Manager.TargetDistanceMin * 0.5f, Manager.TargetDistanceMax * 0.6f);
            if (ground)
            {
                _planPoint = Generator.GetCheckedPoint();
                _planHasPoint = true;
                _planSite = Generator.FindSiteNear(_planPoint, Manager.SiteNameRadius);
            }
            return ground || Generator.CountFreeFighterUnits() >= 2;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            int total = Mathf.Max(Mathf.RoundToInt(QuotaScore * Manager.GetPhaseMultiplier(phaseIndex)), 1);
            Manager.SetModuleProgress(0, total, false);
            if (_planHasPoint) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HUNT, tag, GroundUnits, -1, _planPoint, -1f, -1f); }
            else { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HUNT, tag, GroundUnits, -1, focus, Manager.TargetDistanceMin * 0.5f, Manager.TargetDistanceMax * 0.6f); }
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, tag, Fighters, -1, focus, Manager.FlightDistanceMin, Manager.FlightDistanceMax);
            Manager.SetModuleSite(_planHasPoint ? _planSite : -1);
            _planHasPoint = false;
        }

        public override int Tick(int now)
        {
            int total = Manager.GetObjectiveTotal();
            int earned = Manager.GetTeamScore() - Manager.GetPhaseScoreStart();
            Manager.SetModuleProgress(earned, total, false);
            return total > 0 && earned >= total ? RESULT_CLEAR : RESULT_RUNNING;
        }

        public override bool MarksObjectiveTag() { return false; }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description; }
            return "EARN " + (Manager != null ? Manager.GetObjectiveTotal() : QuotaScore).ToString("N0") + " POINTS";
        }

        public override string GetProgressText()
        {
            if (Manager == null) { return ""; }
            return Manager.GetObjectivesDone().ToString("N0") + "/" + Manager.GetObjectiveTotal().ToString("N0");
        }
    }
}
