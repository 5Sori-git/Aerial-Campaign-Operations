using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 전장 지휘. 한 타겟을 동시에 공격하는 NPC 수(공격 정원)를 제한하고, 오래 붙어 있던 NPC를 대기 중인 NPC와 교대시킨다.
    /// 정원이 없으면 탐지 범위 안의 NPC가 전부 한 플레이어에게 몰려 PvE가 성립하지 않는다(테스트 로그: 수신 상태의 91%가 추격 모드).
    ///
    /// 동기화하지 않는다. 판단은 각 NPC의 오너가 자기 화면에서 하고, 근거가 되는 다른 NPC의 모드/타겟은
    /// ACO_NPCFighterAI가 이미 동기화하고 있으므로 추가 트래픽이 없다. 오너가 여럿으로 나뉘어도 그대로 동작한다
    /// (같은 순간 두 오너가 동시에 자리를 차지하면 잠깐 정원을 1 넘을 수 있지만, 교대 시 다시 맞춰진다).
    ///
    /// Update가 없다. NPC가 타겟을 재탐색할 때만 호출한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_CombatDirector : UdonSharpBehaviour
    {
        [Header("공격 정원")]
        [Tooltip("한 타겟을 동시에 공격할 수 있는 NPC 수. 나머지는 타겟 주위에서 선회하며 차례를 기다린다")]
        public int MaxAttackersPerTarget = 2;
        [Tooltip("공격을 이 시간(초) 이상 계속한 NPC는 대기 중인 NPC가 있으면 자리를 넘긴다. 0이면 교대하지 않는다")]
        public float MaxEngageTime = 30f;
        [Tooltip("자리를 넘긴 NPC가 다시 공격을 신청하기까지 기다리는 시간(초)")]
        public float YieldCooldown = 10f;
        [Tooltip("타겟을 고를 때 정원이 찬 타겟에 더하는 거리(m). 클수록 플레이어가 여럿이면 NPC가 고르게 흩어진다")]
        public float FullTargetPenalty = 1500f;

        [Header("대기 선회 / 자기 방어 (모든 NPC 공통)")]
        [Tooltip("차례를 기다릴 때 타겟 주위를 도는 반경(m). 각 NPC의 LoseTargetRange 80%를 넘으면 그 NPC에선 자동으로 줄인다(타겟을 놓치지 않게)")]
        public float WaitOrbitRadius = 2500f;
        [Tooltip("대기 선회 고도 = 타겟 고도 + 이 값(m)")]
        public float WaitAltitudeAboveTarget = 400f;
        [Tooltip("대기 중이라도 타겟이 이 거리(m) 안으로 들어오면 정원과 무관하게 교전한다")]
        public float DefendDistance = 800f;

        [Header("에디터 시각화 (게임에는 영향 없음)")]
        [Tooltip("씬 뷰에 공격/대기 흐름을 그린다. 플레이 모드에서 각 NPC의 실제 상태를 읽어 표시한다(ACO_CombatDirectorGizmo)")]
        public bool ShowGizmos = true;
        [Tooltip("NPC/타겟 머리 위에 모드·순번·시간 라벨을 표시한다")]
        public bool ShowLabels = true;

        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 자동으로 채운다. 수동: ACO → Bake NPC References")]
        public ACO_NPCFighterAI[] Fighters;

        /// 이 타겟을 공격 중(추격 또는 이탈 후 재진입 대기)인 NPC 수. except는 세지 않는다.
        public int CountAttackers(int targetIdx, ACO_NPCFighterAI except)
        {
            if (targetIdx < 0 || Fighters == null) { return 0; }
            int count = 0;
            for (int i = 0; i < Fighters.Length; i++)
            {
                ACO_NPCFighterAI f = Fighters[i];
                if (f == null || f == except) { continue; }
                if (f.IsAttacking(targetIdx)) { count++; }
            }
            return count;
        }

        /// 자리가 비어 있고, 이 NPC보다 먼저 기다린 NPC들에게 돌아갈 몫을 빼고도 남으면 공격할 수 있다.
        /// 순서를 안 지키면 빈자리를 먼저 확인한 NPC가 가져가서, 시뮬레이션에서 600초 동안 한 번도 공격하지 못한 NPC가 생겼다.
        /// 대기 시작 시각은 로컬 시간이라 같은 오너의 NPC끼리만 비교한다.
        public bool CanAttack(ACO_NPCFighterAI requester, int targetIdx)
        {
            int free = Mathf.Max(MaxAttackersPerTarget, 1) - CountAttackers(targetIdx, requester);
            if (free <= 0) { return false; }
            if (requester == null || Fighters == null || !requester.IsWaitingOn(targetIdx)) { return true; }

            float myStart = requester.GetWaitStartTime();
            int ahead = 0;
            for (int i = 0; i < Fighters.Length; i++)
            {
                ACO_NPCFighterAI f = Fighters[i];
                if (f == null || f == requester || !f.IsLocallyOwned()) { continue; }
                if (f.IsWaitingOn(targetIdx) && f.GetWaitStartTime() < myStart) { ahead++; }
            }
            return ahead < free;
        }

        public bool IsTargetFull(int targetIdx, ACO_NPCFighterAI except)
        {
            return CountAttackers(targetIdx, except) >= Mathf.Max(MaxAttackersPerTarget, 1);
        }

        /// 자기 방어로 정원을 넘겨 교전한 뒤 위협이 지나갔으면, 가장 늦게 합류한 공격자가 대기로 돌아간다.
        /// 모두가 "내가 초과분"이라고 판단해 한꺼번에 빠지지 않도록 합류 순서로 한 명만 고른다(같은 오너의 NPC끼리 비교).
        public bool ShouldStepDown(ACO_NPCFighterAI attacker, int targetIdx)
        {
            if (CountAttackers(targetIdx, attacker) < Mathf.Max(MaxAttackersPerTarget, 1)) { return false; }
            if (Fighters == null) { return false; }

            float myTime = attacker.GetEngageTime();
            for (int i = 0; i < Fighters.Length; i++)
            {
                ACO_NPCFighterAI f = Fighters[i];
                if (f == null || f == attacker || !f.IsLocallyOwned()) { continue; }
                if (f.IsAttacking(targetIdx) && f.GetEngageTime() < myTime) { return false; }
            }
            return true;
        }

        /// 이 타겟을 기다리는 NPC가 있는지. 없으면 교대할 필요가 없다.
        public bool HasWaiting(int targetIdx, ACO_NPCFighterAI except)
        {
            if (targetIdx < 0 || Fighters == null) { return false; }
            for (int i = 0; i < Fighters.Length; i++)
            {
                ACO_NPCFighterAI f = Fighters[i];
                if (f == null || f == except) { continue; }
                if (f.IsWaitingOn(targetIdx)) { return true; }
            }
            return false;
        }
    }
}
