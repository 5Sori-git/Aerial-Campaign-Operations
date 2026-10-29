using UnityEngine;

namespace ACO
{
    /// 특수무장 정의(에디터 전용 데이터 — 씬에 두지 않는다). 프리팹 하나 = 상점에서 파는 무장 하나:
    /// 루트에 이 컴포넌트(ID·이름·설명·가격·아이콘·목록 순서), 자식에 무장 모듈(ACO_DFUNC_Missile — 성능 값이 모두 여기 있다).
    /// Assets/ACO_Addon/SpecialWeapons의 정의를
    ///  1) Bake가 격납고(ACO_Loadout) 목록에 맞추고(이름·설명·가격·아이콘은 정의 값),
    ///  2) 기체 셋업(ACO → Tools)·셋업 검사 [고치기]가 특수무장 슬롯(ACO_DFUNC_Special)에 모듈로 복제해 넣는다(이미 같은 ID가 있으면 건너뛴다 — 기체별로 고친 값을 지키게).
    /// 새 무장 = 예제 프리팹(ACO_Special_*, 프리팹 생성 창 → 플레이어 특수무장)을 복제해 ID와 모듈 값을 바꾼다. 기체·HUD에 묶인 칸(발사 지점·소리·잔탄 글자·HUD)은 설치가 기체에서 채운다.
    /// 일반 MonoBehaviour라 VRChat 월드에 올릴 수 없다 — 프리팹 파일로만 쓴다(셋업 검사가 씬에 있으면 알린다).
    [DisallowMultipleComponent]
    public class ACO_SpecialWeaponDef : MonoBehaviour
    {
        [Tooltip("무장 ID — 해금 저장 키(aco_unlock_ID)와 기체 모듈 이름에 쓴다. 한 번 정하면 바꾸지 않는다(바꾸면 산 기록이 사라진다). 영문 대문자·숫자")]
        public string Id = "NEW";
        [Tooltip("격납고 버튼·설명에 보이는 이름")]
        public string DisplayName = "NEW";
        [Tooltip("격납고 설명(기본 TMP 폰트라 영문). 첫 줄은 종류, 둘째 줄은 특징")]
        [TextArea] public string Description = "NEW WEAPON\nDESCRIBE IT HERE";
        [Tooltip("해금 가격(크레딧). 0 = 처음부터 해금")]
        public int Price = 10000;
        [Tooltip("격납고 버튼 아이콘(흰 선 + 투명 배경 Sprite). 비우면 미사일 그림")]
        public Sprite Icon;
        [Tooltip("격납고 목록 순서(작을수록 앞). 이미 목록에 있는 무장은 자리를 옮기지 않는다")]
        public int SortOrder = 100;
        [Tooltip("무장 모듈(이 프리팹 자식의 ACO_DFUNC_Missile). 미사일 프리팹(Missile)을 비우면 그 기체의 일반 미사일 모델을 쓴다")]
        public ACO_DFUNC_Missile Module;
    }
}
