using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 태양 에이스 MR. S 페이즈(모듈, 동기화 없음, 일반 페이즈 — 4·5번째 공중 보스) — 설계 6-167.
    /// 생성기 풀의 MR. S(NPC_MrS) 1기가 단독으로 나타난다(호위 없음 — 목표 태그).
    ///  - 성공: 격추. 이 페이즈만의 실패 조건은 없다(미션 제한 시간만).
    ///  - 태양 이탈·햇빛·반전 레일건·무전은 기체 쪽(ACO_MrS)이 맡는다. AWACS 등장 설명은 모듈의 시작 대사(StartText).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseMrS : ACO_PhaseModule
    {
        [Header("배치")]
        [Tooltip("MR. S가 나타나는 거리(m) — 작전 지역 중심에서")]
        public float SpawnDistanceMin = 6000f;
        public float SpawnDistanceMax = 9000f;

        private ACO_MrS Ace() { return Generator != null ? (ACO_MrS)Generator.GetAce(ACO_AceBase.KIND_MRS) : null; }

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            return Generator != null && Generator.CanDeployAce(ACO_AceBase.KIND_MRS);
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 100, false);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_MRS, tag, 0, -1, focus, SpawnDistanceMin, SpawnDistanceMax);
            Manager.SetModuleSite(-1);
        }

        public override int Tick(int now)
        {
            Manager.CountTaggedObjectives(Manager.GetModuleTag());
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            if (Manager.GetTaggedDone() >= total) { return RESULT_CLEAR; }
            Manager.SetModuleProgress(Mathf.RoundToInt((1f - HealthRatio()) * 100f), 100, false);
            return RESULT_RUNNING;
        }

        private float HealthRatio()
        {
            ACO_MrS ace = Ace();
            if (ace == null || ace.TargetHealth == null || ace.TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(ace.TargetHealth.Health / ace.TargetHealth.FullHealth);
        }

        public override bool IsBossPhase() { return true; }

        private string AceName()
        {
            ACO_MrS ace = Ace();
            return ace != null ? ace.AceName : "MISTER.S";
        }

        public override string GetName()
        {
            return PhaseName.Length > 0 && PhaseName != "MODULE" ? PhaseName.Replace("{ACE}", AceName()) : "ACE - " + AceName();
        }

        public override string GetDescription()
        {
            return (Description.Length > 0 ? Description : "SHOOT DOWN {ACE}").Replace("{ACE}", AceName());
        }

        /// "MR. S 72%".
        public override string GetProgressText()
        {
            return AceName() + " " + Mathf.RoundToInt(HealthRatio() * 100f) + "%";
        }
    }
}
