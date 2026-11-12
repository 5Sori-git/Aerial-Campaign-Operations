using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 분신 에이스 형제 페이즈(모듈, 동기화 없음, 일반 페이즈 — 3~5번째). 생성기 풀의 슈퍼에이스(NPC_Heads 형 HEADS, NPC_Tails 동생 TAILS)
    /// Aces기가 편대로 나타난다(호위 없음 — 둘 다 목표 태그).
    ///  - 성공: 모두 격추(태그 목표 모두 파괴). 이 페이즈만의 실패 조건은 없다(미션 제한 시간만).
    ///  - 쌍둥이 규칙(한 기가 떨어지면 남은 쪽이 분노)은 슈퍼에이스 스크립트가 맡는다.
    /// 판정은 관리자의 태그 세기(CountTaggedObjectives)만으로 한다. 배치 안 됨 = 다른 페이즈로 바꾼다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseTwins : ACO_PhaseModule
    {
        [Header("분신 에이스")]
        [Tooltip("함께 나오는 슈퍼에이스 수(풀에 있는 만큼 — 형제 2기)")]
        [UnityEngine.Serialization.FormerlySerializedAs("Escorts")]
        public int Aces = 2;

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            return Generator != null && Generator.CanDeployAce(ACO_AceBase.KIND_TWIN);
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, Mathf.Max(Aces, 1), false);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_TWINS, tag, Mathf.Max(Aces, 1), -1, focus, Manager.FlightDistanceMin, Manager.FlightDistanceMax);
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

        private string AceName() { return Generator != null ? Generator.GetAceNames(ACO_AceBase.KIND_TWIN) : "THE ACES"; }

        public override bool IsBossPhase() { return true; }

        public override string GetName()
        {
            return PhaseName.Length > 0 && PhaseName != "MODULE" ? PhaseName.Replace("{ACE}", AceName()) : "ACES - " + AceName();
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description.Replace("{ACE}", AceName()); }
            return "SHOOT DOWN " + AceName();
        }
    }
}
