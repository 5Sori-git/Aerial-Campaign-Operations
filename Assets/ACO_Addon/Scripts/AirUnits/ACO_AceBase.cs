using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 보스 에이스 공통 부모(분신 형제 ACO_Twins, 레이저 에이스 ACO_Borderless, SHADE ACO_ShadeAce, LUNA ACO_LunaAce …).
    /// 일반 NPC 전투기(ACO_NPCFighterAI) 안의 자식에 붙고, AI의 Ace 칸 하나로 연결된다(빌더·Bake가 넣는다). AI·생성기·HUD는 이 부모만 안다:
    ///  - Ace가 있으면 보스 에이스 — 일반 배치·증원·호위에 쓰지 않고(생성기), 공격 정원 밖이며 숙련도 체력 배율을 받지 않는다(AI).
    ///  - 생성기는 종류(GetAceKind)로 풀에서 찾는다(FreeAceOfKind·CanDeployAce·GetAce) — 페이즈는 GetAce(종류)를 자기 에이스 타입으로 바꿔 쓴다.
    ///  - HUD 표적 마커는 이름(GetAceName)과 딸린 표적(GetSubUnits — 형제 분신, LUNA UAV)을 여기서 읽는다.
    /// 새 에이스: 이 부모를 상속하고 KIND_ 번호를 하나 더 두면 AI·생성기·HUD는 고치지 않는다. 동기화 방식은 자식이 정한다.
    public abstract class ACO_AceBase : UdonSharpBehaviour
    {
        public const int KIND_TWIN = 1;    // 분신 에이스 형제(HEADS·TAILS)
        public const int KIND_SHADE = 2;   // SHADE 편대
        public const int KIND_BORDERLESS = 3;   // 레이저 에이스 BORDERLESS
        public const int KIND_LUNA = 4;    // UAV 에이스 LUNA
        public const int KIND_MRS = 5;     // 태양 에이스 MR. S

        [Tooltip("격추되면 기체 메시를 앞으로 쏘아 떨어뜨리는 추락 파티클(6-145 — Setup Check ace-wreck-1이 넣는다, 비면 예전처럼 사라진다)")]
        public ACO_AirWreck Wreck;

        /// 종류(KIND_*).
        public virtual int GetAceKind() { return 0; }

        /// HUD 마커·페이즈 배너·무전에 쓰는 이름.
        public virtual string GetAceName() { return "ACE"; }

        /// HUD에 따로 띄울 딸린 표적(없으면 null). 켜져 있을 때만 보인다.
        public virtual Transform[] GetSubUnits() { return null; }

        /// 딸린 표적 i의 체력 — null이면 본체 체력을 같이 쓰고 본체처럼 표시한다(분신), 있으면 따로 표시한다(UAV — GetSubUnitLabel).
        public virtual SaccTarget GetSubUnitHealth(int i) { return null; }

        /// 따로 표시하는 딸린 표적의 이름(3~4글자).
        public virtual string GetSubUnitLabel() { return "SUB"; }

        /// 이 에이스가 플레어를 뿌렸다(모든 화면 — ACO_FlareDispenser.NetFlare). 에이스는 일반 적 플레어 무전("Deploying countermeasures!" 등 — 남성 목소리) 대신 이걸 받는다.
        /// 기본은 아무 말도 하지 않는다. 전용 대사가 있는 에이스만 덮어쓴다(LUNA).
        public virtual void OnFlare() { }
    }
}
