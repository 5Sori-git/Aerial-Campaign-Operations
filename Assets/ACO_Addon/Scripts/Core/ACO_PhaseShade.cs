using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// SHADE 편대 페이즈(모듈, 동기화 없음, 일반 페이즈 — 3~5번째). 생성기 풀의 SHADE(NPC_Shade) Aces기가 편대로 나타난다(호위 없음 — 모두 목표 태그).
    ///  - 성공: 모두 격추. 이 페이즈만의 실패 조건은 없다(미션 제한 시간만).
    ///  - 코브라·전방위 미사일·교차 플레어는 기체 쪽(ACO_ShadeAce·런처·ACO_CrossManeuver)이 맡는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseShade : ACO_PhaseModule
    {
        [Header("SHADE 편대")]
        [Tooltip("함께 나오는 SHADE 수(풀에 있는 만큼)")]
        public int Aces = 4;

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            return Generator != null && Generator.CanDeployShade();
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, Mathf.Max(Aces, 1), false);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SHADE, tag, Mathf.Max(Aces, 1), -1, focus, Manager.FlightDistanceMin, Manager.FlightDistanceMax);
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

        public override string GetName()
        {
            return PhaseName.Length > 0 && PhaseName != "MODULE" ? PhaseName : "SHADE SQUADRON";
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description; }
            return "SHOOT DOWN SHADE 1-" + Mathf.Max(Aces, 1);
        }
    }
}
