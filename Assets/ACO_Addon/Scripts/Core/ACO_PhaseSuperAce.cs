using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 보너스 페이즈 — 슈퍼에이스(모듈, 동기화 없음, BonusOnly). 생성기 풀의 슈퍼에이스(NPC_SuperAce)와 호위 에이스 Escorts기가 나타난다.
    ///  - 성공: 슈퍼에이스 격추(태그 목표 모두 파괴) — ClearBonus × 보너스 배율.
    ///  - 물러남: BonusTimeLimit 안에 못 잡음(관리자가 판정) — 보너스 없이 미션 클리어. 배치 안 됨 = 건너뜀.
    /// 판정은 관리자의 태그 세기(CountTaggedObjectives)만으로 한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseSuperAce : ACO_PhaseModule
    {
        [Header("슈퍼에이스")]
        [Tooltip("같이 나오는 호위 에이스 수")]
        public int Escorts = 2;

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            return Generator != null && Generator.CanDeploySuperAce();
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SUPER_ACE, tag, Escorts, -1, focus, Manager.FlightDistanceMin, Manager.FlightDistanceMax);
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
            int done = Manager.GetTaggedDone();
            Manager.SetModuleProgress(done, total, true);
            return done >= total ? RESULT_CLEAR : RESULT_RUNNING;
        }

        private string AceName() { return Generator != null ? Generator.GetSuperAceName() : "THE ACE"; }

        public override string GetName()
        {
            return PhaseName.Length > 0 && PhaseName != "MODULE" ? PhaseName.Replace("{ACE}", AceName()) : "BONUS - " + AceName();
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description.Replace("{ACE}", AceName()); }
            return "SHOOT DOWN " + AceName() + " BEFORE IT ESCAPES (" + Mathf.RoundToInt(BonusTimeLimit) + "s)";
        }
    }
}
