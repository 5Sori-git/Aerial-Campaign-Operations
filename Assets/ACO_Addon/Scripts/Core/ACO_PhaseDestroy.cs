using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 파괴형 페이즈(모듈, 동기화 없음) — 태그가 붙은 목표를 모두 부수면 클리어. 종류(Kind):
    ///  - 타격(KIND_STRIKE): 고가치 표적 Count개. 육상 자리가 없으면 함선 고가치 표적.
    ///  - 방공망 제압(KIND_SEAD): 방공 시설(SAM·대공포) Count대.
    ///  - 제공권(KIND_AIR): 적 편대 Count기.
    ///  - 보스 편대(KIND_BOSS): 에이스 편대 Count기. 이름은 BossNames 중 하나 — 태그로 정해서(동기화 없이) 모든 클라이언트가 같다.
    /// 수 = Count + CountPerPhase × (페이즈 번호 − 1). 거리(목표 지상 시설·편대)는 관리자 공통 값(TargetDistance*, FlightDistance*)을 쓴다.
    /// 판정은 생성기 배치 상태와 관리자의 태그 세기(CountTaggedObjectives)만으로 한다 — 오너가 바뀌어도 그대로 이어진다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseDestroy : ACO_PhaseModule
    {
        public const int KIND_STRIKE = 0;
        public const int KIND_SEAD = 1;
        public const int KIND_AIR = 2;
        public const int KIND_BOSS = 3;

        [Header("파괴 목표")]
        [Tooltip("0 = 타격(고가치 표적), 1 = 방공망 제압(방공 시설), 2 = 제공권(적 편대), 3 = 보스 편대(에이스)")]
        public int Kind = KIND_STRIKE;
        [Tooltip("목표 수(1페이즈)")]
        public int Count = 2;
        [Tooltip("페이즈 번호가 하나 오를 때마다 더하는 수")]
        public int CountPerPhase = 0;
        [Tooltip("보스 편대 이름(영문). 비우면 ACE")]
        public string[] BossNames = new string[] { "VIPER", "SPECTRE", "RAVEN", "WARLOCK", "TEMPEST", "NOMAD" };

        // 사전 확인 결과(오너)
        private bool _planHasPoint;
        private Vector3 _planPoint;
        private int _planSite = -1;
        private int _planVariant;

        private int CountFor(int phaseIndex) { return Mathf.Max(Count + CountPerPhase * Mathf.Max(phaseIndex, 0), 1); }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            _planHasPoint = false;
            _planSite = -1;
            _planVariant = 0;
            if (Generator == null || Manager == null) { return false; }
            float min = Manager.TargetDistanceMin;
            float max = Manager.TargetDistanceMax;
            if (Kind == KIND_STRIKE)
            {
                if (Generator.CountFreeHvt(false) > 0 && Generator.HasGroundNear(focus, min, max)) { _planVariant = 0; SetPlanPoint(); return true; }
                if (Generator.CountFreeHvt(true) > 0 && Generator.HasShipSiteNear(focus, min, max)) { _planVariant = 1; SetPlanPoint(); return true; }
                return false;
            }
            if (Kind == KIND_SEAD)
            {
                if (Generator.CountFreeLandGround() >= Mathf.Min(CountFor(phaseIndex), 3) && Generator.HasGroundNear(focus, min, max)) { SetPlanPoint(); return true; }
                return false;
            }
            return Generator.CountFreeFighterUnits() >= 2;
        }

        private void SetPlanPoint()
        {
            _planPoint = Generator.GetCheckedPoint();
            _planHasPoint = true;
            _planSite = Generator.FindSiteNear(_planPoint, Manager.SiteNameRadius);
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            int count = CountFor(phaseIndex);
            float min = Manager.TargetDistanceMin;
            float max = Manager.TargetDistanceMax;
            if (Kind == KIND_STRIKE)
            {
                if (_planHasPoint) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HVT, tag, count, _planVariant, _planPoint, -1f, -1f); }
                else { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HVT, tag, count, -1, focus, min, max); }
            }
            else if (Kind == KIND_SEAD)
            {
                if (_planHasPoint) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SITE, tag, count, -1, _planPoint, -1f, -1f); }
                else { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SITE, tag, count, -1, focus, min, max); }
            }
            else
            {
                int tier = Kind == KIND_BOSS ? ACO_NPCFighterAI.SKILL_ACE : -1;
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, tag, count, tier, focus, Manager.FlightDistanceMin, Manager.FlightDistanceMax);
            }
            Manager.SetModuleSite(_planHasPoint ? _planSite : -1);
            _planHasPoint = false;
        }

        public override int Tick(int now)
        {
            Manager.CountTaggedObjectives(Manager.GetModuleTag());
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                // 아직 배치 동기화 전이거나, 풀/자리가 없어 아무것도 못 놓았다.
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            int done = Manager.GetTaggedDone();
            Manager.SetModuleProgress(done, total, true);
            return done >= total ? RESULT_CLEAR : RESULT_RUNNING;
        }

        // ───────────── 모든 클라이언트 ─────────────

        /// 보스 편대 이름: 지금 목표 태그로 정한다(태그는 동기화돼 있어 모두 같다).
        public string GetBossName()
        {
            if (BossNames == null || BossNames.Length == 0 || Manager == null) { return "ACE"; }
            int k = Mathf.Abs(Manager.GetModuleTag()) % BossNames.Length;
            return string.IsNullOrEmpty(BossNames[k]) ? "ACE" : BossNames[k];
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Kind == KIND_BOSS ? Description.Replace("{BOSS}", GetBossName()) : Description; }
            if (Kind == KIND_STRIKE) { return "DESTROY THE HIGH VALUE TARGETS"; }
            if (Kind == KIND_SEAD) { return "SUPPRESS ENEMY AIR DEFENSES"; }
            if (Kind == KIND_AIR) { return "SHOOT DOWN THE ENEMY FLIGHT"; }
            return "SHOOT DOWN " + GetBossName() + " SQUADRON";
        }
    }
}
