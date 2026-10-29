using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 미션 생성기 / 런타임 재배치. 오너(기본: 인스턴스 마스터)가 무작위로 배치를 정하고, 결과(유닛별 배치)만 동기화한다.
    /// 모든 클라이언트는 받은 결과를 그대로 적용한다 — 지상 유닛은 위치를 동기화하지 않으므로 여기서 받은 위치가 곧 위치다.
    ///
    /// 미리 배치(브리핑): RequestPreviewDeploy(지형 씨앗) — 전체 배치와 같지만 미션 시작으로 세지 않는다(전체 배치 번호를 올리지 않음).
    /// 그 뒤 RequestStartMission이 오면 같은 지형이면 다시 배치하지 않고 그 배치를 그대로 미션으로 확정한다(게임 진행의 작전 테이블 미리보기).
    /// 전체 배치 요청은 대기열 하나로 합쳐 지형이 다 세워진 뒤 한 번만 한다(QueueDeploy) — 지형 대기 루프는 늘 하나, 미션 요청이 미리 배치보다
    /// 우선이고, 미리 배치는 명시적인 미션 요청 없이는 절대 미션이 되지 않는다(전체 배치 번호 = 미션 시작 신호).
    ///
    /// 흐름: 월드 시작 시 풀을 숨긴다(HidePoolOnStart) → 트리거로 RequestStartMission() → 전체 배치
    ///       → 미션 중 이벤트로 RequestReinforce(지상 수, 전투기 수) → 파괴된 유닛 중 일부만 다시 배치.
    ///
    /// 전체 배치(미션 시작):
    ///  - 고가치 표적(공장/벙커 등)을 지면에 놓고, 그 주위에 방공 호위(SAM/대공포) 링을 두른다
    ///  - 나머지 지상 유닛은 4~8대 그룹(콘보이 또는 부대)
    ///  - 전투기는 2기/4기 편대마다 임무(지상군·표적 엄호 또는 CAP 지점 초계)를 받고, 그 근처 "공역 안"에서 스폰
    /// 부분 재배치(증원): 지상 그룹은 같은 규칙, 전투기는 임무를 받고 "공역 밖"에서 진입한다.
    ///
    /// 유닛마다 배치 번호(serial)와 배치 시각을 동기화하고, 클라이언트는 번호가 바뀐 유닛만 적용한다.
    /// 늦게 들어온 클라이언트는 자기가 들어오기 전에 배치된 유닛은 위치만 맞추고 부활시키지 않는다 — 생사는 SaccTarget 동기화가 맞춘다.
    ///
    /// 페이즈 모드(PhasedMode, 미션 관리자 페이즈용): 전체 배치는 배경 병력(BackgroundGround/BackgroundFighters)만 놓고,
    /// 페이즈 목표는 관리자가 RequestDeployObjective(종류, 태그, ...)로 그때그때 배치한다. 목표로 놓인 유닛은 태그(동기화)를 달아
    /// 관리자/마커가 "지금 목표인 유닛"을 알 수 있다. 태그 0 = 일반 유닛.
    ///
    /// 지상 풀의 전차(ACO_Tank)는 지상 그룹에 섞지 않고 순찰 소대(BackgroundTankPlatoons)나 호위 항로 매복조로 놓는다 — 항로를 동기화하고 전차가 서버 시간으로 따라간다.
    /// 지상 풀의 함선(HighValue를 끈 ACO_Warship — 구축함)은 지상 그룹에 섞지 않고 바다에 함대로 놓는다(BackgroundShipGroups, 전함 호위 EscortCount).
    /// 함선(ACO_Warship이 붙은 고가치 표적)은 바다(해수면 SeaLevel, 수심 MinSeaDepth, 순항 원 전체)에 놓고, 그 부위는 CAT_PART로 조회된다.
    ///
    /// 스폰은 Instantiate가 아니라 미리 배치된 풀의 재배치다. SAV가 락온 대상 목록(AAMTargets)을 빌드 때 굽기 때문에
    /// 런타임에 새로 만든 유닛은 락온할 수 없다. 풀은 UnitPoolRoot(기본: 이 오브젝트) 아래의 NPC들이며 빌드 시 자동 수집된다.
    /// 풀의 유닛은 씬에서 "활성" 상태로 두어야 한다(Start가 한 번은 돌아야 재활성화 후 동작한다). 숨기는 건 이 스크립트가 한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_MissionGenerator : UdonSharpBehaviour
    {
        [Header("작전 공역 (월드 X/Z, m)")]
        public Vector2 AreaMin = new Vector2(-200000f, -200000f);
        public Vector2 AreaMax = new Vector2(200000f, 200000f);

        [Header("안전지대 (지상 유닛 스폰 금지)")]
        [Tooltip("이 콜라이더들의 영역(바운딩 박스, 수평 X/Z 기준) 안에는 지상 유닛을 스폰하지 않는다. 트리거 박스로 기지 주변을 덮어 두면 된다")]
        public Collider[] SafeZones;
        [Tooltip("안전지대 경계에서 이만큼(m) 더 떨어져야 스폰한다")]
        public float SafeZoneMargin = 500f;

        [Header("고가치 표적 (공장/벙커 등)")]
        [Tooltip("전체 배치 때 놓을 고가치 표적 수. 음수면 풀 전체")]
        public int HighValueCount = 2;
        [Tooltip("고가치 표적끼리 최소 거리(m)")]
        public float HighValueMinSeparation = 8000f;
        [Tooltip("고가치 표적 주위에 방공 호위(SAM/대공포) 링을 두를 확률")]
        [Range(0f, 1f)] public float DefenseChance = 1f;
        public int DefenseSizeMin = 3;
        public int DefenseSizeMax = 5;
        [Tooltip("방공 호위 링 반경(m)")]
        public float DefenseRadius = 450f;

        [Header("지상 유닛 그룹")]
        public int GroupSizeMin = 4;
        public int GroupSizeMax = 8;
        [Tooltip("그룹이 콘보이(일렬)로 배치될 확률. 나머지는 부대(밀집) 배치")]
        [Range(0f, 1f)] public float ConvoyChance = 0.5f;
        [Tooltip("콘보이 차량 간격(m)")]
        public float ConvoySpacing = 40f;
        [Tooltip("부대 배치 반경(m)")]
        public float ClusterRadius = 150f;
        [Tooltip("부대 배치 시 유닛 사이 최소 간격(m)")]
        public float MinUnitSpacing = 25f;
        [Tooltip("전체 배치 때 쓸 최대 지상 유닛 수(방공 호위 포함). 음수면 풀 전체")]
        public int MaxGroundUnits = -1;

        [Header("지면 판정")]
        public LayerMask GroundLayers = 2049;
        [Tooltip("이 높이에서 아래로 Raycast를 쏜다(지형 최고점보다 높게)")]
        public float RaycastHeight = 20000f;
        public float RaycastDistance = 40000f;
        [Tooltip("이보다 가파른 경사(도)에는 놓지 않는다")]
        public float MaxGroundSlope = 25f;
        [Tooltip("이보다 낮은 지면은 버린다(바다 콜라이더 등을 피할 때)")]
        public float MinGroundHeight = -100000f;
        [Tooltip("지면이 맞아도 해수면(SeaLevel) + LandMinAboveSea보다 낮거나 물 콜라이더(SeaSurfaceLayers)에 먼저 맞으면 육상 유닛을 놓지 않는다(물에 잠긴 해저 지형 위 스폰 방지). 해수면 아래 마른 땅이 있는 맵이면 끈다")]
        public bool RejectBelowSeaLevel = true;
        public float LandMinAboveSea = 1f;
        [Tooltip("그룹/표적 하나를 놓을 자리를 찾는 최대 시도 횟수")]
        public int MaxPlacementAttempts = 40;

        [Header("해상 배치 (함선 — ACO_Warship이 붙은 고가치 표적)")]
        [Tooltip("해수면 높이(m). 함선은 이 높이에 놓인다")]
        public float SeaLevel = 0f;
        [Tooltip("지면 Raycast가 해수면 - 이 값보다 낮은 곳에 맞으면 바다로 친다(수심, m)")]
        public float MinSeaDepth = 15f;
        [Tooltip("이 레이어에 맞으면 바다(물 콜라이더). GroundLayers에 물 콜라이더 레이어가 들어 있으면 여기에도 넣어야 바다를 찾는다")]
        public LayerMask SeaSurfaceLayers = 1 << 4;
        [Tooltip("Raycast가 아무것도 안 맞으면 바다로 친다(물에 콜라이더가 없는 맵). 지형이 공역 전체를 덮지 않는 맵이면 끈다")]
        public bool NoHitIsSea = true;
        [Tooltip("함선 순항 원 위에서 바다인지 확인할 점 수")]
        public int SeaCheckPoints = 12;
        [Tooltip("순항 원 바깥으로 이만큼(m) 더 바다여야 한다(해안에 너무 붙지 않게)")]
        public float SeaClearance = 300f;

        [Header("함대 (HighValue를 끈 ACO_Warship — 구축함 등, 지상 풀)")]
        [Tooltip("전체 배치 때 바다에 놓을 함대 수(공역 아무 곳). 함대 = 기함 1척 + 호위")]
        public int BackgroundShipGroups = 2;
        [Tooltip("함대 하나의 척수")]
        public int ShipGroupSizeMin = 1;
        public int ShipGroupSizeMax = 3;
        [Tooltip("전함(고가치 표적 함선)을 놓을 때 붙일 호위 구축함 수")]
        public int EscortCount = 2;

        [Header("전차 (ACO_Tank — 지상 풀)")]
        [Tooltip("전체 배치 때 놓을 순찰 소대 수(지면 위 닫힌 고리를 계속 돈다)")]
        public int BackgroundTankPlatoons = 2;
        [Tooltip("소대/매복조 하나의 전차 수")]
        public int TankGroupSize = 3;
        [Tooltip("순찰 고리 반경(m)")]
        public float TankPatrolRadius = 700f;
        [Tooltip("같은 항로를 가는 전차 사이 간격(m)")]
        public float TankSpacing = 40f;
        [Tooltip("매복 지점: 호위 항로에서 옆으로 이 거리(m) 범위")]
        public float AmbushDistanceMin = 400f;
        public float AmbushDistanceMax = 900f;
        [Tooltip("매복조 출발점: 매복 지점에서 항로 반대쪽으로 이만큼(m) 더 떨어진 곳")]
        public float AmbushApproachDistance = 1500f;

        [Header("호위 대상 (ACO_EscortUnit — 아군)")]
        [Tooltip("호위 항로의 경유점 수(출발/도착 포함, 2~6)")]
        [Range(2, 6)] public int EscortRoutePoints = 4;
        [Tooltip("중간 경유점을 항로 옆으로 흩뜨리는 최대 거리(m)")]
        public float EscortRouteLateral = 4000f;
        [Tooltip("공중 호위 고도(m). 아래 지형보다 EscortTerrainClearance만큼은 높게 잡는다")]
        public float EscortAltitude = 1800f;
        public float EscortTerrainClearance = 500f;
        [Tooltip("공중/지상 호위의 최종 목적지: 지형 장식에 도시/비행장이 있으면 이 확률로 그중 하나(없으면 공역 아무 곳). 출발점은 공역 어디든")]
        [Range(0f, 1f)] public float EscortDestinationChance = 1f;
        [Tooltip("지상 호위는 도시/비행장 가장자리에서 이만큼(m) 바깥에 멈춘다(건물 자리 밖)")]
        public float EscortDestinationMargin = 500f;
        [Tooltip("공중 호위가 비행장으로 가면 착륙한다: 최종 접근 시작점은 접지점에서 이 거리(m), 활주로 위 이 높이(m)")]
        public float LandingApproachDistance = 6000f;
        public float LandingApproachHeight = 350f;
        [Tooltip("착륙 전 중간 접근점: 최종 접근 시작점에서 이 거리(m) 앞, 활주로 위 이 높이(m) 이상")]
        public float LandingIntermediateDistance = 8000f;
        public float LandingIntermediateHeight = 1000f;
        [Tooltip("공중 호위가 도시/아무 곳에 보급을 떨굴 고도(목적지 지면 위, m) — 항로 마지막 구간에서 이 고도로 내려온다")]
        public float EscortDropAltitude = 600f;
        [Tooltip("콘보이(여러 대 호위) 차량 사이 간격(m)")]
        public float EscortConvoySpacing = 60f;
        [Tooltip("적 폭격기 편대(삼각 대형 — 선두 뒤로 좌우 한 대씩 줄마다): 한 줄 뒤로 처지는 거리(m)")]
        public float BomberFormationBack = 90f;
        [Tooltip("적 폭격기 편대(삼각 대형): 한 줄마다 옆으로 벌어지는 거리(m)")]
        public float BomberFormationSide = 80f;
        [Tooltip("지상/해상 항로를 차량이 실제로 달리는 곡선을 따라 이 간격(m)마다 확인한다")]
        public float RouteCheckSpacing = 150f;
        [Tooltip("지상 항로: 확인점 사이 오르내림 경사(도)가 이보다 크면 버린다(절벽/급한 둑)")]
        public float MaxRouteGrade = 18f;
        [Tooltip("지상 항로와 그 옆 배치(매복 전차/방공 시설): 물 콜라이더(SeaSurfaceLayers)나 해수면 + RouteWaterMargin보다 낮은 지면은 버린다(강/바다 건너기 방지). 해수면 아래 육지가 있는 맵이면 끈다")]
        public bool AvoidWaterOnGroundRoutes = true;
        public float RouteWaterMargin = 1f;
        [Tooltip("항로 아래 방공 시설 하나의 유닛 수(지면이 있는 곳에만 놓는다)")]
        public int EscortSiteSize = 3;
        [Tooltip("방공 시설을 항로에서 옆으로 떨어뜨리는 거리(m)")]
        public float EscortSiteOffsetMin = 800f;
        public float EscortSiteOffsetMax = 2500f;

        [Header("전투기 편대")]
        [Tooltip("편대가 4기일 확률. 나머지는 2기 편대")]
        [Range(0f, 1f)] public float FourShipChance = 0.5f;
        [Tooltip("전체 배치 때 쓸 최대 전투기 수. 음수면 풀 전체")]
        public int MaxFighters = -1;
        [Tooltip("편대 대형 간격(m)")]
        public float FormationSpacing = 150f;

        [Header("전투기 임무")]
        [Tooltip("초계(CAP) 지점. 각 지점의 위치(고도 포함) 상공에서 선회 대기한다")]
        public Transform[] CapPoints;
        [Tooltip("편대가 CAP 대신 아군 지상군/고가치 표적 상공 엄호를 맡을 확률(엄호할 대상이 있을 때)")]
        [Range(0f, 1f)] public float CoverGroundChance = 0.5f;
        [Tooltip("지상 엄호 고도 = 지상 유닛 고도 + 이 범위의 무작위 값(m)")]
        public float CoverAltitudeMin = 1500f;
        public float CoverAltitudeMax = 3000f;
        [Tooltip("엄호/CAP 대상이 없을 때의 집결 지점은 공역 경계에서 이만큼(m) 안쪽 범위에서 고른다")]
        public float IngressInset = 20000f;

        [Header("전투기 스폰 위치")]
        [Tooltip("전체 배치(미션 시작) 때 임무 지점 근처 공역 안에서 스폰한다. 끄면 증원처럼 공역 밖에서 진입한다")]
        public bool FullDeploySpawnInside = true;
        [Tooltip("공역 안 스폰: 임무 지점에서 이 거리(m) 범위 떨어진 곳")]
        public float InsideSpawnDistanceMin = 3000f;
        public float InsideSpawnDistanceMax = 8000f;
        [Tooltip("공역 밖 스폰(증원): 공역 경계에서 바깥으로 이 거리(m) 범위")]
        public float SpawnOutsideMin = 5000f;
        public float SpawnOutsideMax = 15000f;
        [Tooltip("공역 밖 스폰 고도(m). 임무 지점이 정해지면 그 고도로 맞춰 간다")]
        public float SpawnAltitudeMin = 2000f;
        public float SpawnAltitudeMax = 6000f;

        [Header("전투기 숙련도 (편대 단위)")]
        [Tooltip("미션 시작/일반 증원에서 편대가 베테랑일 확률")]
        [Range(0f, 1f)] public float VeteranChance = 0.35f;
        [Tooltip("미션 시작/일반 증원에서 편대가 에이스일 확률(베테랑보다 먼저 판정)")]
        [Range(0f, 1f)] public float AceChance = 0.05f;
        [Tooltip("단계별 배율 [신병, 베테랑, 에이스]. 인스펙터에 맞춰 둔 전투기 값이 베테랑(1.0) 기준이다")]
        public float[] TierTurnGMultiplier = new float[] { 0.8f, 1f, 1.25f };
        public float[] TierSpeedMultiplier = new float[] { 0.9f, 1f, 1.1f };
        [Tooltip("락온에 걸리는 시간 배율. 작을수록 빨리 쏜다")]
        public float[] TierLockTimeMultiplier = new float[] { 1.4f, 1f, 0.6f };
        [Tooltip("기총 체류 시간(데미지가 들어가기까지) 배율. 작을수록 위험하다")]
        public float[] TierGunDwellMultiplier = new float[] { 1.5f, 1f, 0.6f };
        [Tooltip("미사일 발사 허용각 배율. 클수록 공격적이다")]
        public float[] TierMissileConeMultiplier = new float[] { 0.7f, 1f, 1.3f };

        [Header("목표 지점 증원 (ReinforceAt — 미션 관리자가 사용)")]
        [Tooltip("목표 지점 증원 시 지상 그룹을 놓을 반경(m)")]
        public float FocusGroundRadius = 2500f;
        [Tooltip("목표 지점 증원 전투기는 공역 경계 중 목표에 가장 가까운 쪽 바깥에서, 목표 좌표에서 이만큼(m) 옆으로 흩어져 진입한다")]
        public float FocusEntrySpread = 10000f;

        [Header("부분 재배치 (Reinforce)")]
        [Tooltip("RequestReinforceDefault()가 쓰는 지상 유닛 수(UI 버튼처럼 인자 없이 부를 때)")]
        public int ReinforceGroundCount = 6;
        [Tooltip("RequestReinforceDefault()가 쓰는 전투기 수")]
        public int ReinforceFighterCount = 2;
        [Tooltip("파괴된 유닛이 모자라면 이번 배치에서 쓰지 않던(꺼져 있는) 풀 유닛도 쓴다")]
        public bool ReinforceAlsoUsesUnused = false;

        [Header("페이즈 모드 (미션 관리자 페이즈)")]
        [Tooltip("켜면 전체 배치(미션 시작)에서 고가치 표적을 놓지 않고 배경 병력만 놓는다. 목표는 미션 관리자가 페이즈마다 배치한다")]
        public bool PhasedMode = true;
        [Tooltip("페이즈 모드 전체 배치의 지상 유닛 수(나머지 풀은 페이즈 목표용으로 남긴다). 음수면 MaxGroundUnits")]
        public int BackgroundGround = 12;
        [Tooltip("페이즈 모드 전체 배치의 전투기 수. 음수면 MaxFighters")]
        public int BackgroundFighters = 4;
        [Tooltip("목표 배치 시 시설 링 반경 = DefenseRadius x 이 값(방공망 제압 페이즈)")]
        public float SiteRadiusScale = 1.5f;

        [Header("지형 (선택)")]
        [Tooltip("육각 기둥 지형. 있으면 지형이 다 세워진 뒤 유닛을 배치한다")]
        public ACO_HexTerrain HexTerrain;
        [Tooltip("켜면 미션 시작(전체 배치) 때 새 씨앗으로 지형을 다시 만든다. 끄면 지금 지형 그대로 배치한다. "
            + "게임 진행(ACO_GameFlow)이 있으면 시작할 때 끈다 — 지형은 브리핑 리롤과 미션 뒤 자동 갱신으로 바뀐다")]
        public bool RegenerateTerrainEachMission = true;
        [Tooltip("지형이 이 시간(초) 안에 안 세워지면 그냥 배치한다(안전장치)")]
        public float TerrainWaitTimeout = 30f;
        [Tooltip("지형 장식(도시/마을/비행장/랜드마크, Bake가 자동 연결). 건물 자리에는 유닛을 놓지 않고, 둘레를 주둔지로 쓴다")]
        public ACO_TerrainFeatures Features;
        [Tooltip("지상 그룹/육상 고가치 표적/페이즈 목표 자리를 고를 때 이 확률로 도시/마을/비행장/레이더 둘레(주둔지)에서 고른다")]
        [Range(0f, 1f)] public float SettlementPreference = 0.6f;

        [Header("실행")]
        [Tooltip("한 프레임에 적용(스폰/숨김)할 최대 유닛 수. 큰 배치와 늦게 들어온 클라이언트의 순간 끊김을 프레임에 나눈다")]
        public int ApplyPerFrame = 25;
        [Tooltip("월드에 들어오면 풀 유닛을 모두 숨긴다(미션 시작 트리거 전까지 보이지 않게)")]
        public bool HidePoolOnStart = true;
        [Tooltip("오너가 월드에 들어왔을 때 배치가 없으면 자동으로 미션을 시작한다")]
        public bool GenerateOnStart = false;
        [Tooltip("이 오브젝트를 Interact(클릭)하면 오너에게 미션 시작(전체 배치)을 요청한다(콜라이더 필요)")]
        public bool AllowInteract = true;

        [Header("풀 (빌드 시 자동 수집)")]
        [Tooltip("이 아래의 NPC를 미션에 쓴다. 비우면 이 오브젝트 아래")]
        public Transform UnitPoolRoot;
        [Tooltip("빌드 시 ACO_SceneBaker가 채운다. 수동: ACO → Bake NPC References")]
        public ACO_NPCFighterAI[] Fighters;
        [Tooltip("ACO_SAMSite / ACO_AAGunSite 등. MissionSpawnPosition/MissionSpawnRotation/MissionRevive 필드와 MissionSpawn/MissionDespawn 이벤트를 가진 스크립트")]
        public UdonSharpBehaviour[] GroundUnits;
        [Tooltip("ACO_StaticTarget(공장/벙커 등). 계약은 지상 유닛과 같다")]
        public UdonSharpBehaviour[] HighValueTargets;
        [Tooltip("호위 대상(ACO_EscortUnit — 아군 수송기 등)")]
        public ACO_EscortUnit[] EscortUnits;

        // 유닛별 배치 상태. serial이 바뀐 유닛만 클라이언트가 적용한다. time은 늦게 들어온 클라이언트의 부활 판단용(서버 ms).
        [UdonSynced] private int _deployCount;
        // 전체 배치(미션 시작) 횟수. 증원은 세지 않는다 — 미션 관리자가 "새 미션이 시작됐다"를 알아채는 기준.
        [UdonSynced] private int _fullDeployCount;
        // 미리 배치(브리핑)가 놓인 지형 씨앗. 0 = 미리 배치 없음. 미션 시작 때 같은 지형이면 이 배치를 그대로 쓴다.
        [UdonSynced] private int _previewSeed;
        // 전체 배치 요청이 대기 중이다(지형을 기다리는 중 포함). 모두에게 보여 준다 — 게임 진행이 브리핑 준비 완료(READY 가능)를 판단한다.
        [UdonSynced] private bool _deploying;
        [UdonSynced] private bool[] _groundUsed = new bool[0];
        [UdonSynced] private Vector3[] _groundPos = new Vector3[0];
        // 회전은 전부 방위(yaw)뿐이라 0~65535(= 0~360°)로 보낸다. 배치 번호는 같은지만 보므로 byte(256번째에 한 바퀴 — 문제없음).
        [UdonSynced] private ushort[] _groundYaw = new ushort[0];
        [UdonSynced] private byte[] _groundSerial = new byte[0];
        [UdonSynced] private int[] _groundTimeMs = new int[0];
        [UdonSynced] private bool[] _hvtUsed = new bool[0];
        [UdonSynced] private Vector3[] _hvtPos = new Vector3[0];
        [UdonSynced] private ushort[] _hvtYaw = new ushort[0];
        [UdonSynced] private byte[] _hvtSerial = new byte[0];
        [UdonSynced] private int[] _hvtTimeMs = new int[0];
        [UdonSynced] private bool[] _fighterUsed = new bool[0];
        [UdonSynced] private Vector3[] _fighterPos = new Vector3[0];
        [UdonSynced] private ushort[] _fighterYaw = new ushort[0];
        [UdonSynced] private Vector3[] _fighterIngress = new Vector3[0];
        [UdonSynced] private byte[] _fighterSerial = new byte[0];
        [UdonSynced] private int[] _fighterTimeMs = new int[0];
        [UdonSynced] private byte[] _fighterTier = new byte[0];
        // 주요 목표 = 이번 미션에 배치된 고가치 표적, 배치 순서대로(HVT 인덱스)
        [UdonSynced] private int[] _objectiveOrder = new int[0];
        // 페이즈 목표 태그(0 = 일반). 마지막으로 배치한 목표 태그의 유닛은 증원이 재사용하지 않는다(_protectTag).
        [UdonSynced] private short[] _groundTag = new short[0];
        [UdonSynced] private short[] _hvtTag = new short[0];
        [UdonSynced] private short[] _fighterTag = new short[0];
        [UdonSynced] private int _protectTag;
        // 지상 풀 함선의 편대: -1 = 스스로 순항, 아니면 기함코드 x 16 + 편대 슬롯(기함코드 = 지상 인덱스, 또는 LEADER_HVT + 고가치 표적 인덱스).
        // 호위함의 배치 위치/회전은 기함과 같은 값(순항 중심/방위)이라 같은 궤도를 돈다.
        [UdonSynced] private short[] _groundLeader = new short[0];
        // 기함코드 x 16 + 슬롯이 short에 들어가게: 지상 풀 < 1000, 고가치 표적 < 1000
        private const int LEADER_HVT = 1000;
        // 호위 대상: 사용 여부/배치 번호/출발 시각/태그 + 항로(유닛당 MAX_ROUTE_POINTS칸)
        [UdonSynced] private bool[] _escortUsed = new bool[0];
        [UdonSynced] private byte[] _escortSerial = new byte[0];
        [UdonSynced] private int[] _escortTimeMs = new int[0];
        [UdonSynced] private short[] _escortTag = new short[0];
        [UdonSynced] private byte[] _escortRouteCount = new byte[0];
        [UdonSynced] private Vector3[] _escortRoute = new Vector3[0];
        [UdonSynced] private short[] _escortOffset = new short[0];   // 콘보이 행렬에서 앞선 거리(m)
        [UdonSynced] private short[] _escortLateral = new short[0];  // 편대에서 옆으로 벌어진 거리(m, 오른쪽 +) — 적 폭격기 삼각 대형
        [UdonSynced] private byte[] _escortArrival = new byte[0];   // 도착 방식(ACO_EscortUnit.ARRIVE_*)
        // 전차 항로(전차마다 MAX_ROUTE_POINTS칸). 지상 풀 인덱스 → 전차 칸은 _tankSlot(풀 순서로 정해져 모든 클라이언트가 같다).
        // 전차 항로는 수평 좌표만(높이는 전차가 지면을 재서 따라간다)
        [UdonSynced] private Vector2[] _tankRoute = new Vector2[0];
        [UdonSynced] private byte[] _tankRouteCount = new byte[0];
        [UdonSynced] private bool[] _tankLoop = new bool[0];
        [UdonSynced] private short[] _tankOffset = new short[0];
        private int[] _tankSlot;
        private int _tankCount;
        private Vector3[] _routeScratch;
        // 마지막으로 만든 호위 항로의 목적지 지형 장식 자리(-1 = 없음), 경유점 수, 도착 방식, 목적지 후보 목록
        private int _routeDestSite = -1;
        private int _routeBuiltCount;
        private int _routeArrival;
        private int[] _destList;
        private Vector3[] _tankRouteTmp;
        // 적용 나누기(프레임당 ApplyPerFrame대)
        private int _applyBudget;
        private bool _applyMore;
        private bool _applyContinueQueued;
        private int[] _appliedEscortSerial = new int[0];

        // 목표 배치 종류(RequestDeployObjective)
        /// 고가치 표적 count개(+ 방공 호위 링, 링은 태그 없음)
        public const int OBJ_HVT = 0;
        /// 방공 시설: 지상 유닛 count대를 한 지점 둘레에 링으로
        public const int OBJ_SITE = 1;
        /// 지상 유닛 count대를 각각 따로(시한 표적)
        public const int OBJ_GROUND = 2;
        /// 전투기 count기(편대), focus 쪽으로 진입
        public const int OBJ_FLIGHT = 3;
        /// 지상 그룹 count대를 한 지역에(사냥터)
        public const int OBJ_HUNT = 4;
        /// 슈퍼에이스 1기(태그) + 호위 에이스 count기(태그 없음), focus 쪽으로 진입 — 보너스 페이즈
        public const int OBJ_SUPER_ACE = 5;

        // 로컬: 이 클라이언트가 마지막으로 적용한 serial(-1 = 아직 없음)
        private int[] _appliedGroundSerial = new int[0];
        private int[] _appliedHvtSerial = new int[0];
        private int[] _appliedFighterSerial = new int[0];
        private int _joinTimeMs;
        private bool _hasJoinTime;
        private bool _applyQueued;

        // 배치 중 임시 값(U#은 out 매개변수를 못 쓰므로 필드로 돌려받는다)
        private Vector3[] _tmpPos;
        private Quaternion[] _tmpRot;
        private float _sampleY;
        private int _lastGroups;
        private int _lastPlaced;
        private Vector3 _taskPoint;
        private int[] _groundOrder;
        private int _groundNext;
        private int _groundBudget;
        private int _deployNowMs; // 지금 진행 중인 배치의 시각(이번에 놓은 유닛 구분용)
        // 지금 배치의 숙련도 확률과 목표 지점(ReinforceAt일 때만)
        private float _vetChance;
        private float _aceChance;
        private bool _focusActive;
        private Vector3 _focusPos;
        // 함선 부위(카테고리 CAT_PART): 고가치 표적 중 ACO_Warship의 Parts를 처음 조회할 때 모은다. 배치/생사 기록은 소속 함선을 따른다.
        private UdonSharpBehaviour[] _parts;
        private int[] _partOwner;
        private int[] _partOwnerCat;
        // 지상 풀 중 함선(바다에만 놓는다)
        private bool[] _groundIsShip;
        private bool _partsBuilt;
        // 전체 배치 요청 대기열(오너 로컬). 요청은 하나로 합쳐지고, 지형을 기다리는 루프도 하나만 돈다.
        // 종류: 미션이 미리 배치보다 우선(미리 배치를 기다리는 중에 미션 시작이 오면 미션으로 바뀐다 — 반대는 없다).
        private const int DEPLOY_NONE = 0;
        private const int DEPLOY_PREVIEW = 1;
        private const int DEPLOY_MISSION = 2;
        private int _pendingDeploy = DEPLOY_NONE;
        private int _pendingSeed;          // 기다릴 지형 씨앗(0 = 지금 지형). 나중 요청의 씨앗이 이긴다
        private bool _waitingTerrain;      // 지형 대기 루프가 돌고 있다
        private float _terrainWaitStart;
        // 지금 표시(Mark*)하는 유닛에 달 태그, 목표 지점 찾기 결과
        private int _markTag;
        private float _pickX;
        // PickGarrisonPoint 결과(지형 장식 자리 둘레 한 점)
        private float _garrisonX;
        private float _garrisonZ;
        private float _pickZ;

        void Start()
        {
            MarkJoinTime();
            DisableInteractive = !AllowInteract;

            // 유닛들의 Start가 한 번 돈 뒤에 숨겨야 나중에 다시 켜도 정상 동작한다.
            if (HidePoolOnStart) { SendCustomEventDelayedFrames(nameof(HidePoolIfIdle), 2); }
            if (GenerateOnStart && Networking.IsOwner(gameObject)) { SendCustomEventDelayedFrames(nameof(GenerateIfEmpty), 3); }
        }

        private void MarkJoinTime()
        {
            if (_hasJoinTime) { return; }
            _hasJoinTime = true;
            _joinTimeMs = Networking.GetServerTimeInMilliseconds();
        }

        public override void Interact()
        {
            if (AllowInteract) { RequestStartMission(); }
        }

        // ───────────── 진입점 (누구나 호출 → 오너가 실행) ─────────────

        /// 미션 시작 = 풀 전체 부활 + 전체 배치(고가치 표적, 방공 호위, 지상 그룹, 임무를 받은 전투기 편대).
        /// 지금 지형에 미리 배치가 있으면 다시 배치하지 않고 그대로 확정한다.
        public void RequestStartMission()
        {
            if (Networking.IsOwner(gameObject)) { StartMissionDeploy(); }
            else { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(StartMissionDeploy)); }
        }

        /// 미션 시작(오너). 같은 지형에 미리 배치가 있으면 다시 배치하지 않고 그대로 확정하고, 미리 배치가 진행 중이면 그 요청을 미션으로 바꾼다.
        /// 이미 미션 배치가 대기 중이면 무시한다(중복 시작 방지).
        public void StartMissionDeploy()
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            if (_pendingDeploy == DEPLOY_MISSION) { return; }
            if (_pendingDeploy == DEPLOY_PREVIEW)
            {
                _pendingDeploy = DEPLOY_MISSION;
                Debug.Log("[ACO] 미션 시작: 진행 중인 미리 배치를 미션으로 확정합니다");
                return;
            }
            if (_previewSeed != 0 && _previewSeed == CurrentTerrainSeed())
            {
                _previewSeed = 0;
                _fullDeployCount++;
                RequestSerialization();
                Debug.Log("[ACO] 미션 시작: 브리핑 때 미리 배치한 병력을 그대로 씁니다");
                return;
            }
            RedeployAll();
        }

        /// 브리핑용 미리 배치. terrainSeed가 0이 아니면 그 씨앗의 지형이 다 세워질 때까지 기다린다(방금 리롤한 지형 — 씨앗이 아직 동기화되지
        /// 않았을 수 있으므로 직접 넘긴다). 0이면 지금 지형. 미션 시작으로 세지 않는다.
        public void RequestPreviewDeploy(int terrainSeed)
        {
            if (Networking.IsOwner(gameObject)) { PreviewDeploy(terrainSeed); }
            else { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetPreviewDeploy), terrainSeed); }
        }

        [NetworkCallable]
        public void NetPreviewDeploy(int terrainSeed)
        {
            PreviewDeploy(terrainSeed);
        }

        public void PreviewDeploy(int terrainSeed)
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            QueueDeploy(DEPLOY_PREVIEW, terrainSeed);
        }

        private int CurrentTerrainSeed() { return HexTerrain != null ? HexTerrain.GetSeed() : 1; }

        /// RequestStartMission과 같다(이전 이름).
        public void RequestGenerate() { RequestStartMission(); }

        /// 파괴된 NPC 중 지상 groundCount대, 전투기 fighterCount기를 같은 규칙으로 다시 배치한다. 고가치 표적은 건드리지 않는다.
        public void RequestReinforce(int groundCount, int fighterCount)
        {
            if (Networking.IsOwner(gameObject)) { Reinforce(groundCount, fighterCount); }
            else { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetReinforce), groundCount, fighterCount); }
        }

        /// 인자 없이 부르는 버전(UI 버튼/SendCustomEvent용). 수는 인스펙터의 ReinforceGroundCount/ReinforceFighterCount.
        public void RequestReinforceDefault()
        {
            RequestReinforce(ReinforceGroundCount, ReinforceFighterCount);
        }

        [NetworkCallable]
        public void NetReinforce(int groundCount, int fighterCount)
        {
            Reinforce(groundCount, fighterCount);
        }

        /// 목표 지점 증원: 지상 그룹은 focus 주변(FocusGroundRadius)에, 전투기는 focus 상공 엄호 임무로 공역 밖(목표 쪽 경계)에서 진입.
        /// 숙련도 확률은 호출자가 정한다(웨이브가 거듭될수록 올리는 용도).
        public void RequestReinforceAt(Vector3 focus, int groundCount, int fighterCount, float veteranChance, float aceChance)
        {
            if (Networking.IsOwner(gameObject)) { ReinforceAt(focus, groundCount, fighterCount, veteranChance, aceChance); }
            else
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetReinforceAt),
                    focus, groundCount, fighterCount, veteranChance, aceChance);
            }
        }

        [NetworkCallable]
        public void NetReinforceAt(Vector3 focus, int groundCount, int fighterCount, float veteranChance, float aceChance)
        {
            ReinforceAt(focus, groundCount, fighterCount, veteranChance, aceChance);
        }

        /// 페이즈 목표 배치: 종류(OBJ_*)별로 count개를 focus에서 minDist~maxDist(m) 떨어진 곳에 놓고 tag를 단다(maxDist <= 0이면 공역 아무 곳).
        /// 쓰지 않던 풀 유닛을 먼저, 모자라면 파괴된 유닛을 쓴다(살아 있는 유닛은 건드리지 않는다). tier >= 0이면 전투기 숙련도 고정.
        public void RequestDeployObjective(int kind, int tag, int count, int tier, Vector3 focus, float minDist, float maxDist)
        {
            if (Networking.IsOwner(gameObject)) { DeployObjective(kind, tag, count, tier, focus, minDist, maxDist); }
            else
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetDeployObjective),
                    kind, tag, count, tier, focus, minDist, maxDist);
            }
        }

        [NetworkCallable]
        public void NetDeployObjective(int kind, int tag, int count, int tier, Vector3 focus, float minDist, float maxDist)
        {
            DeployObjective(kind, tag, count, tier, focus, minDist, maxDist);
        }

        /// 태그가 tag인 유닛을 거둬들인다(시한 표적이 시간 안에 안 잡혀 "도주"했을 때).
        public void RequestReleaseTag(int tag)
        {
            if (Networking.IsOwner(gameObject)) { ReleaseTag(tag); }
            else { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetReleaseTag), tag); }
        }

        [NetworkCallable]
        public void NetReleaseTag(int tag)
        {
            ReleaseTag(tag);
        }

        public void GenerateIfEmpty()
        {
            if (_deployCount == 0) { RedeployAll(); }
        }

        // ───────────── 배치 (오너) ─────────────

        /// 미션 전체 배치(오너). RegenerateTerrainEachMission이면 새 지형을 만들고 그 지형을 기다린다.
        public void RedeployAll()
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            int seed = 0;
            if (HexTerrain != null && RegenerateTerrainEachMission)
            {
                seed = Random.Range(1, int.MaxValue);
                HexTerrain.RequestGenerate(seed);
            }
            QueueDeploy(DEPLOY_MISSION, seed);
        }

        /// 전체 배치 요청을 대기열에 합친다. 지형이 다 세워진 뒤에 한 번만 배치한다 — 지면/바다 판정이 새 지형을 봐야 한다.
        /// 기다리는 중에 또 요청이 오면 루프를 새로 띄우지 않고 종류(미션 우선)·씨앗(나중 것)만 바꾼다.
        private void QueueDeploy(int kind, int terrainSeed)
        {
            if (kind > _pendingDeploy) { _pendingDeploy = kind; }
            if (terrainSeed != 0) { _pendingSeed = terrainSeed; }
            _terrainWaitStart = Time.time;
            if (!_deploying)
            {
                _deploying = true;
                RequestSerialization();
            }
            if (HexTerrain == null)
            {
                ExecutePendingDeploy(1);
                return;
            }
            if (_waitingTerrain) { return; }
            _waitingTerrain = true;
            SendCustomEventDelayedFrames(nameof(WaitTerrainThenDeploy), 2);
        }

        /// 기다리는 지형이 이 클라이언트에서 다 세워지면(동기화가 와서 로컬로 만든 뒤) 대기 중인 배치를 한 번 한다.
        public void WaitTerrainThenDeploy()
        {
            if (!_waitingTerrain) { return; }
            int seed = _pendingSeed != 0 ? _pendingSeed : HexTerrain.GetSeed();
            bool built = HexTerrain.IsBuilt(seed);
            if (!built && Time.time - _terrainWaitStart < TerrainWaitTimeout)
            {
                SendCustomEventDelayedFrames(nameof(WaitTerrainThenDeploy), 2);
                return;
            }
            if (!built) { Debug.LogWarning("[ACO] 지형이 제한 시간 안에 세워지지 않아 그대로 배치합니다"); }
            _waitingTerrain = false;
            ExecutePendingDeploy(seed);
        }

        private void ExecutePendingDeploy(int terrainSeed)
        {
            int kind = _pendingDeploy;
            _pendingDeploy = DEPLOY_NONE;
            _pendingSeed = 0;
            _deploying = false;
            if (!Networking.IsOwner(gameObject) || kind == DEPLOY_NONE)
            {
                RequestSerialization();
                return;
            }
            DoFullDeploy(kind == DEPLOY_PREVIEW, terrainSeed);   // Publish가 동기화한다
        }

        /// 배치 도중 오너가 나가 새 오너가 됐다: 진행 중이던 요청은 이전 오너와 함께 사라졌으므로 "배치 중"을 푼다
        /// (게임 진행이 미리 배치가 없는 걸 보고 다시 요청한다).
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            if (_deploying && _pendingDeploy == DEPLOY_NONE)
            {
                _deploying = false;
                RequestSerialization();
            }
        }

        /// 전체 배치 요청이 대기 중인가(지형 대기 포함, 모든 클라이언트).
        public bool IsDeploying() { return _deploying; }

        /// 지금 지형에 미리 배치가 끝나 있는가(모든 클라이언트) — 브리핑 준비 완료. 미션 시작 때 이 배치를 그대로 쓴다.
        public bool IsPreviewReady() { return !_deploying && _previewSeed != 0 && _previewSeed == CurrentTerrainSeed(); }

        /// 미리 배치가 놓인 지형 씨앗(0 = 없음).
        public int GetPreviewSeed() { return _previewSeed; }

        /// 전체 배치 본체. preview면 미션 시작으로 세지 않고(전체 배치 번호 그대로) 그 지형 씨앗을 기록해 둔다 —
        /// 미션 시작 때 StartMissionDeploy가 같은 지형이면 이 배치를 확정한다.
        private void DoFullDeploy(bool preview, int terrainSeed)
        {
            EnsureArrays();
            int now = Networking.GetServerTimeInMilliseconds();

            // 모두 "이번엔 안 씀"으로 바꾼 뒤 한도만큼 다시 고른다. 바뀌는 유닛은 전부 serial이 오른다.
            for (int i = 0; i < _groundUsed.Length; i++) { MarkGround(i, false, Vector3.zero, Quaternion.identity, now); }
            for (int i = 0; i < _hvtUsed.Length; i++) { MarkHvt(i, false, Vector3.zero, Quaternion.identity, now); }
            for (int i = 0; i < _fighterUsed.Length; i++) { MarkFighter(i, false, Vector3.zero, Quaternion.identity, Vector3.zero, ACO_NPCFighterAI.SKILL_VETERAN, now); }
            for (int i = 0; i < _escortUsed.Length; i++) { MarkEscort(i, false, 0, now); }

            // 지상 유닛은 방공 호위와 일반 그룹이 같은 풀을 나눠 쓴다.
            // 함선은 지상 그룹/방공 링에 쓰지 않는다(아래 함대 배치에서 바다에 놓는다).
            _groundOrder = ShuffledLand();
            _groundNext = 0;
            _groundBudget = MaxGroundUnits < 0 ? _groundOrder.Length : Mathf.Min(MaxGroundUnits, _groundOrder.Length);

            _objectiveOrder = new int[0];
            _protectTag = 0;
            _markTag = 0;
            _focusActive = false;
            _vetChance = VeteranChance;
            _aceChance = AceChance;
            // 페이즈 모드: 고가치 표적은 페이즈 목표로 남겨 두고, 배경 병력만 놓는다.
            int hvtCount = 0;
            int defensePlaced = 0;
            if (!PhasedMode)
            {
                hvtCount = PlaceHighValueTargets(now);
                defensePlaced = _lastPlaced;
            }
            else if (BackgroundGround >= 0)
            {
                _groundBudget = Mathf.Min(_groundBudget, BackgroundGround);
            }

            PlaceGround(_groundOrder, _groundNext, _groundBudget, now);
            int groups = _lastGroups;
            int groundPlaced = _lastPlaced + defensePlaced;
            groundPlaced += PlaceShipGroups(BackgroundShipGroups, now);
            groundPlaced += PlaceTankPlatoons(BackgroundTankPlatoons, now);

            int fighterBudget = MaxFighters < 0 ? _fighterUsed.Length : Mathf.Min(MaxFighters, _fighterUsed.Length);
            if (PhasedMode && BackgroundFighters >= 0) { fighterBudget = Mathf.Min(fighterBudget, BackgroundFighters); }
            int[] regular = RegularFighterOrder();
            PlaceFighters(regular, regular.Length, fighterBudget, now, FullDeploySpawnInside);

            if (preview)
            {
                _previewSeed = terrainSeed;
                Publish("미리 배치(브리핑)", hvtCount, groups, groundPlaced, _lastGroups, _lastPlaced);
                return;
            }
            _previewSeed = 0;
            _fullDeployCount++;
            Publish("미션 시작", hvtCount, groups, groundPlaced, _lastGroups, _lastPlaced);
        }

        public void Reinforce(int groundCount, int fighterCount)
        {
            _focusActive = false;
            DoReinforce("증원", groundCount, fighterCount, VeteranChance, AceChance);
        }

        public void ReinforceAt(Vector3 focus, int groundCount, int fighterCount, float veteranChance, float aceChance)
        {
            _focusActive = true;
            _focusPos = focus;
            DoReinforce("목표 지점 증원", groundCount, fighterCount, veteranChance, aceChance);
            _focusActive = false;
        }

        private void DoReinforce(string label, int groundCount, int fighterCount, float veteranChance, float aceChance)
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            EnsureArrays();
            int now = Networking.GetServerTimeInMilliseconds();
            _vetChance = veteranChance;
            _aceChance = aceChance;

            // 후보: 파괴된 유닛(먼저) → 모자라면 옵션에 따라 쓰지 않던 유닛. 각각 섞어서 고른다.
            int[] groundCandidates = new int[_groundUsed.Length];
            int groundCandCount = CollectCandidates(GroundUnits, _groundUsed, _groundTag, groundCandidates);
            int[] fighterCandidates = new int[_fighterUsed.Length];
            int fighterCandCount = CollectFighterCandidates(fighterCandidates);

            PlaceGround(groundCandidates, 0, Mathf.Min(Mathf.Max(groundCount, 0), groundCandCount), now);
            int groups = _lastGroups;
            int groundPlaced = _lastPlaced;
            PlaceFighters(fighterCandidates, fighterCandCount, Mathf.Min(Mathf.Max(fighterCount, 0), fighterCandCount), now, false);

            Publish(label, 0, groups, groundPlaced, _lastGroups, _lastPlaced);
        }

        private void Publish(string label, int hvtCount, int groundGroups, int groundPlaced, int flights, int fightersPlaced)
        {
            _deployCount++;
            RequestSerialization();
            Debug.Log("[ACO] " + label + " #" + _deployCount + ": 고가치 표적 " + hvtCount + "개, 지상 그룹 " + groundGroups + "개(총 "
                + groundPlaced + "대), 전투기 편대 " + flights + "개(" + fightersPlaced + "기)");
            ApplyChanges();
        }

        /// 풀 크기에 맞게 동기화 배열을 준비한다. 처음이거나 풀이 바뀌었을 때만 새로 만든다(기존 배치는 유지).
        private void EnsureArrays()
        {
            int groundCount = GroundUnits != null ? GroundUnits.Length : 0;
            int hvtCount = HighValueTargets != null ? HighValueTargets.Length : 0;
            int fighterCount = Fighters != null ? Fighters.Length : 0;

            if (_groundUsed.Length != groundCount)
            {
                _groundUsed = new bool[groundCount];
                _groundPos = new Vector3[groundCount];
                _groundYaw = new ushort[groundCount];
                _groundSerial = new byte[groundCount];
                _groundTimeMs = new int[groundCount];
                _groundTag = new short[groundCount];
                _groundLeader = new short[groundCount];
                for (int i = 0; i < groundCount; i++) { _groundLeader[i] = -1; }
            }
            if (_hvtUsed.Length != hvtCount)
            {
                _hvtUsed = new bool[hvtCount];
                _hvtPos = new Vector3[hvtCount];
                _hvtYaw = new ushort[hvtCount];
                _hvtSerial = new byte[hvtCount];
                _hvtTimeMs = new int[hvtCount];
                _hvtTag = new short[hvtCount];
            }
            int escortCount = EscortUnits != null ? EscortUnits.Length : 0;
            if (_escortUsed.Length != escortCount)
            {
                _escortUsed = new bool[escortCount];
                _escortSerial = new byte[escortCount];
                _escortTimeMs = new int[escortCount];
                _escortTag = new short[escortCount];
                _escortRouteCount = new byte[escortCount];
                _escortRoute = new Vector3[escortCount * ACO_EscortUnit.MAX_ROUTE_POINTS];
                _escortOffset = new short[escortCount];
                _escortLateral = new short[escortCount];
                _escortArrival = new byte[escortCount];
            }
            EnsureTankSlots();
            if (_tankRouteCount.Length != _tankCount)
            {
                _tankRoute = new Vector2[_tankCount * ACO_Tank.MAX_ROUTE_POINTS];
                _tankRouteCount = new byte[_tankCount];
                _tankLoop = new bool[_tankCount];
                _tankOffset = new short[_tankCount];
            }
            if (_fighterUsed.Length != fighterCount)
            {
                _fighterUsed = new bool[fighterCount];
                _fighterPos = new Vector3[fighterCount];
                _fighterYaw = new ushort[fighterCount];
                _fighterIngress = new Vector3[fighterCount];
                _fighterSerial = new byte[fighterCount];
                _fighterTimeMs = new int[fighterCount];
                _fighterTier = new byte[fighterCount];
                _fighterTag = new short[fighterCount];
            }
        }

        private void MarkGround(int i, bool used, Vector3 pos, Quaternion rot, int now)
        {
            _groundUsed[i] = used;
            _groundPos[i] = pos;
            _groundYaw[i] = YawCode(rot);
            _groundSerial[i] = (byte)((_groundSerial[i] + 1) & 0xFF);
            _groundTimeMs[i] = now;
            _groundTag[i] = (short)(used ? _markTag : 0);
            _groundLeader[i] = -1;
        }

        private void MarkHvt(int i, bool used, Vector3 pos, Quaternion rot, int now)
        {
            _hvtUsed[i] = used;
            _hvtPos[i] = pos;
            _hvtYaw[i] = YawCode(rot);
            _hvtSerial[i] = (byte)((_hvtSerial[i] + 1) & 0xFF);
            _hvtTimeMs[i] = now;
            _hvtTag[i] = (short)(used ? _markTag : 0);
        }

        private void MarkFighter(int i, bool used, Vector3 pos, Quaternion rot, Vector3 ingress, int tier, int now)
        {
            _fighterTier[i] = (byte)Mathf.Clamp(tier, 0, 255);
            _fighterUsed[i] = used;
            _fighterPos[i] = pos;
            _fighterYaw[i] = YawCode(rot);
            _fighterIngress[i] = ingress;
            _fighterSerial[i] = (byte)((_fighterSerial[i] + 1) & 0xFF);
            _fighterTimeMs[i] = now;
            _fighterTag[i] = (short)(used ? _markTag : 0);
        }

        /// 증원 후보. 지금 페이즈 목표(_protectTag)인 유닛은 파괴됐어도 재사용하지 않는다 — 목표 달성 판정이 끝나기 전에 사라지면 안 된다.
        private int CollectCandidates(UdonSharpBehaviour[] units, bool[] used, short[] tags, int[] outList)
        {
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && !ReinforceAlsoUsesUnused) { break; }
                int start = n;
                for (int i = 0; i < used.Length; i++)
                {
                    UdonSharpBehaviour unit = units[i];
                    if (unit == null) { continue; }
                    if (IsProtected(tags, i)) { continue; }
                    if (units == GroundUnits && IsGroundMover(i)) { continue; }
                    bool candidate = pass == 0 ? (used[i] && IsDestroyed(unit.GetComponent<SaccTarget>())) : !used[i];
                    if (candidate) { outList[n] = i; n++; }
                }
                ShuffleRange(outList, start, n);
            }
            return n;
        }

        private int CollectFighterCandidates(int[] outList)
        {
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && !ReinforceAlsoUsesUnused) { break; }
                int start = n;
                for (int i = 0; i < _fighterUsed.Length; i++)
                {
                    ACO_NPCFighterAI fighter = Fighters[i];
                    if (fighter == null || fighter.SuperAce != null) { continue; }
                    if (IsProtected(_fighterTag, i)) { continue; }
                    bool candidate = pass == 0 ? (_fighterUsed[i] && IsDestroyed(fighter.TargetHealth)) : !_fighterUsed[i];
                    if (candidate) { outList[n] = i; n++; }
                }
                ShuffleRange(outList, start, n);
            }
            return n;
        }

        private bool IsProtected(short[] tags, int i)
        {
            return _protectTag != 0 && i < tags.Length && tags[i] == _protectTag;
        }

        /// 체력은 SaccTarget이 동기화하므로 오너가 아니어도 같은 답이 나온다.
        private bool IsDestroyed(SaccTarget health)
        {
            return health != null && health.Health <= 0f;
        }

        // ───────────── 고가치 표적 + 방공 호위 ─────────────

        /// 고가치 표적을 서로 떨어뜨려 지면에 놓고, 확률에 따라 주위에 방공 호위 링을 두른다(_groundOrder에서 꺼내 쓴다).
        /// 반환: 놓은 표적 수. 호위로 놓은 지상 유닛 수는 _lastPlaced.
        private int PlaceHighValueTargets(int now)
        {
            int poolCount = _hvtUsed.Length;
            int budget = HighValueCount < 0 ? poolCount : Mathf.Min(HighValueCount, poolCount);
            int[] order = Shuffled(poolCount);
            int placed = 0;
            int defensePlaced = 0;

            for (int n = 0; n < poolCount && placed < budget; n++)
            {
                ACO_Warship ship = HighValueTargets[order[n]] != null ? HighValueTargets[order[n]].GetComponent<ACO_Warship>() : null;
                bool found = false;
                float x = 0f;
                float z = 0f;
                for (int attempt = 0; attempt < MaxPlacementAttempts && !found; attempt++)
                {
                    if (ship == null && PickGarrisonPoint(Vector3.zero, 0f, -1f))
                    {
                        x = _garrisonX;
                        z = _garrisonZ;
                    }
                    else
                    {
                        x = Random.Range(AreaMin.x, AreaMax.x);
                        z = Random.Range(AreaMin.y, AreaMax.y);
                    }
                    if (!FarFromPlacedHvts(x, z)) { continue; }
                    if (ship != null ? !SampleSea(x, z, FleetRadius(ship)) : !SampleGround(x, z)) { continue; }
                    found = true;
                }
                // 자리가 없으면(바다가 없는데 함선 등) 다음 후보로 넘어간다.
                if (!found) { continue; }

                Vector3 center = new Vector3(x, _sampleY, z);
                MarkHvt(order[n], true, center, HvtRotation(ship != null), now);
                AppendObjective(order[n]);
                placed++;
                if (ship != null) { PlaceEscorts(LEADER_HVT + order[n], center, YawRot(_hvtYaw[order[n]]), EscortCount, now); }

                if (ship == null && Random.value < DefenseChance) { defensePlaced += PlaceDefenseRing(center, now); }
            }

            _lastPlaced = defensePlaced;
            return placed;
        }

        private bool FarFromPlacedHvts(float x, float z)
        {
            float minSq = HighValueMinSeparation * HighValueMinSeparation;
            for (int i = 0; i < _hvtUsed.Length; i++)
            {
                if (!_hvtUsed[i]) { continue; }
                float dx = _hvtPos[i].x - x;
                float dz = _hvtPos[i].z - z;
                if (dx * dx + dz * dz < minSq) { return false; }
            }
            return true;
        }

        /// 표적 주위 원 위에 고르게 방공 유닛을 놓는다(지면을 못 찾은 자리는 건너뛴다). 바깥을 바라보게 둔다.
        private int PlaceDefenseRing(Vector3 center, int now)
        {
            int size = Random.Range(Mathf.Max(DefenseSizeMin, 1), Mathf.Max(DefenseSizeMax, DefenseSizeMin) + 1);
            return PlaceRing(center, size, DefenseRadius, now);
        }

        /// center 둘레(반경 baseRadius)에 _groundOrder[_groundNext..]에서 size대를 꺼내 바깥을 보게 놓는다.
        private int PlaceRing(Vector3 center, int size, float baseRadius, int now)
        {
            size = Mathf.Min(size, _groundBudget - _groundNext);
            int placed = 0;
            float startAngle = Random.Range(0f, 360f);

            for (int k = 0; k < size; k++)
            {
                float angle = (startAngle + k * 360f / size + Random.Range(-10f, 10f)) * Mathf.Deg2Rad;
                float radius = baseRadius * Random.Range(0.85f, 1.15f);
                float px = center.x + Mathf.Cos(angle) * radius;
                float pz = center.z + Mathf.Sin(angle) * radius;
                if (!SampleGround(px, pz)) { continue; }

                Vector3 outward = new Vector3(px - center.x, 0f, pz - center.z);
                Quaternion rot = outward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(outward.normalized, Vector3.up) : Quaternion.identity;
                MarkGround(_groundOrder[_groundNext], true, new Vector3(px, _sampleY, pz), rot, now);
                _groundNext++;
                placed++;
            }
            return placed;
        }

        // ───────────── 지상 그룹 ─────────────

        /// order[start..budget) 를 4~8대 그룹으로 배치한다. 결과는 _lastGroups/_lastPlaced.
        private void PlaceGround(int[] order, int start, int budget, int now)
        {
            _lastGroups = 0;
            _lastPlaced = 0;
            int maxSize = Mathf.Max(GroupSizeMax, Mathf.Max(GroupSizeMin, 1));
            if (_tmpPos == null || _tmpPos.Length < maxSize)
            {
                _tmpPos = new Vector3[maxSize];
                _tmpRot = new Quaternion[maxSize];
            }

            budget = Mathf.Min(budget, order.Length);
            int next = start;
            int failures = 0;
            while (next < budget)
            {
                int size = Random.Range(Mathf.Max(GroupSizeMin, 1), maxSize + 1);
                size = Mathf.Clamp(size, 1, budget - next);
                bool convoy = Random.value < ConvoyChance;

                // 자리를 못 찾으면 몇 번 더 해 보고, 그래도 안 되면 남은 유닛은 이번에 배치하지 않는다.
                if (!TryPlaceGroup(size, convoy))
                {
                    failures++;
                    if (failures >= 3) { break; }
                    continue;
                }

                for (int k = 0; k < size; k++)
                {
                    MarkGround(order[next + k], true, _tmpPos[k], _tmpRot[k], now);
                }
                next += size;
                _lastGroups++;
                _lastPlaced += size;
            }
        }

        /// 그룹 하나를 놓을 자리를 찾아 _tmpPos/_tmpRot에 채운다. 유닛마다 지면을 다시 재므로 언덕에서도 땅에 붙는다.
        private bool TryPlaceGroup(int size, bool convoy)
        {
            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                float cx;
                float cz;
                if (_focusActive)
                {
                    // 목표 지점 증원: 목표 주변 원 안에서 고른다(목표 표적 바로 위는 피하도록 반경의 30% 바깥).
                    float r = Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(Random.value)) * FocusGroundRadius;
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    cx = _focusPos.x + Mathf.Cos(a) * r;
                    cz = _focusPos.z + Mathf.Sin(a) * r;
                }
                else if (PickGarrisonPoint(Vector3.zero, 0f, -1f))
                {
                    // 도시/마을/비행장/레이더 둘레 주둔지
                    cx = _garrisonX;
                    cz = _garrisonZ;
                }
                else
                {
                    cx = Random.Range(AreaMin.x, AreaMax.x);
                    cz = Random.Range(AreaMin.y, AreaMax.y);
                }
                if (!SampleGround(cx, cz)) { continue; }

                float yaw = Random.Range(0f, 360f);
                Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
                Vector3 dir = heading * Vector3.forward;
                bool ok = true;

                for (int k = 0; k < size && ok; k++)
                {
                    float px;
                    float pz;
                    if (convoy)
                    {
                        // 콘보이: 진행 방향으로 일렬. 모두 같은 방향을 본다.
                        float along = (k - (size - 1) * 0.5f) * ConvoySpacing;
                        px = cx + dir.x * along;
                        pz = cz + dir.z * along;
                        if (!SampleGround(px, pz)) { ok = false; break; }
                        _tmpRot[k] = heading;
                    }
                    else
                    {
                        // 부대: 반경 안에 서로 최소 간격을 두고 흩어 놓는다.
                        bool found = false;
                        px = cx;
                        pz = cz;
                        for (int tryPos = 0; tryPos < 12 && !found; tryPos++)
                        {
                            float r = Mathf.Sqrt(Random.value) * ClusterRadius;
                            float a = Random.Range(0f, Mathf.PI * 2f);
                            px = cx + Mathf.Cos(a) * r;
                            pz = cz + Mathf.Sin(a) * r;
                            if (!FarFromPlaced(px, pz, k)) { continue; }
                            if (!SampleGround(px, pz)) { continue; }
                            found = true;
                        }
                        if (!found) { ok = false; break; }
                        _tmpRot[k] = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    }
                    _tmpPos[k] = new Vector3(px, _sampleY, pz);
                }

                if (ok) { return true; }
            }
            return false;
        }

        private bool FarFromPlaced(float x, float z, int count)
        {
            float minSq = MinUnitSpacing * MinUnitSpacing;
            for (int i = 0; i < count; i++)
            {
                float dx = _tmpPos[i].x - x;
                float dz = _tmpPos[i].z - z;
                if (dx * dx + dz * dz < minSq) { return false; }
            }
            return true;
        }

        /// (x, z)가 스폰 가능한 지면이면 true, 높이는 _sampleY. 공역 밖/안전지대/허공/급경사/너무 낮은 곳은 false.
        private bool SampleGround(float x, float z)
        {
            if (x < AreaMin.x || x > AreaMax.x || z < AreaMin.y || z > AreaMax.y) { return false; }
            if (InSafeZone(x, z)) { return false; }
            // 건물 위(지붕)에 놓지 않는다
            if (Features != null && Features.IsBlocked(x, z)) { return false; }

            RaycastHit hit;
            Vector3 origin = new Vector3(x, RaycastHeight, z);
            if (!Physics.Raycast(origin, Vector3.down, out hit, RaycastDistance, GroundLayers, QueryTriggerInteraction.Ignore)) { return false; }
            if (hit.normal.y < Mathf.Cos(MaxGroundSlope * Mathf.Deg2Rad)) { return false; }
            if (hit.point.y < MinGroundHeight) { return false; }
            if (IsUnderSea(x, z)) { return false; }

            _sampleY = hit.point.y;
            return true;
        }

        /// (x, z)의 지면이 물에 잠겼으면 true: 물 콜라이더에 먼저 맞거나 지면이 해수면 + LandMinAboveSea보다 낮음. RejectBelowSeaLevel이 꺼져 있거나 아무것도 안 맞으면 false.
        private bool IsUnderSea(float x, float z)
        {
            if (!RejectBelowSeaLevel) { return false; }
            int ground = GroundLayers;
            int sea = SeaSurfaceLayers;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(x, RaycastHeight, z), Vector3.down, out hit, RaycastDistance, ground | sea, QueryTriggerInteraction.Ignore)) { return false; }
            if (((1 << hit.collider.gameObject.layer) & sea) != 0) { return true; }
            return hit.point.y < SeaLevel + LandMinAboveSea;
        }

        // ───────────── 함대 ─────────────

        /// 함대 순항에 필요한 바다 반경: 기함 순항 원 + 편대 폭.
        private float FleetRadius(ACO_Warship leader)
        {
            return leader.CruiseRadius + leader.EscortSpacing * 2f;
        }

        private bool IsGroundShip(int i)
        {
            if (_groundIsShip == null || _groundIsShip.Length != (GroundUnits != null ? GroundUnits.Length : 0))
            {
                int n = GroundUnits != null ? GroundUnits.Length : 0;
                _groundIsShip = new bool[n];
                for (int k = 0; k < n; k++) { _groundIsShip[k] = GroundUnits[k] != null && GroundUnits[k].GetComponent<ACO_Warship>() != null; }
            }
            return i >= 0 && i < _groundIsShip.Length && _groundIsShip[i];
        }

        /// 지상 풀에서 스스로 움직이는 유닛(함선/전차)을 뺀 섞인 인덱스 — 지상 그룹/방공 링용.
        private int[] ShuffledLand()
        {
            int count = 0;
            for (int i = 0; i < _groundUsed.Length; i++) { if (!IsGroundMover(i)) { count++; } }
            int[] order = new int[count];
            int n = 0;
            for (int i = 0; i < _groundUsed.Length; i++) { if (!IsGroundMover(i)) { order[n] = i; n++; } }
            ShuffleRange(order, 0, count);
            return order;
        }

        /// 함대에 쓸 함선 후보: 쓰지 않던 함선(섞어서) → 파괴된 함선(섞어서). 지금 목표 태그인 함선과 살아 있는 함선은 제외.
        private int CollectFreeShips(int[] outList)
        {
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = n;
                for (int i = 0; i < _groundUsed.Length; i++)
                {
                    if (!IsGroundShip(i) || IsProtected(_groundTag, i)) { continue; }
                    bool candidate = pass == 0 ? !_groundUsed[i] : (_groundUsed[i] && IsDestroyed(GroundUnits[i].GetComponent<SaccTarget>()));
                    if (candidate) { outList[n] = i; n++; }
                }
                ShuffleRange(outList, start, n);
            }
            return n;
        }

        /// 공역 아무 곳의 바다에 함대 groups개를 놓는다. 첫 척이 기함(스스로 순항), 나머지는 기함 궤도를 따라가는 호위.
        private int PlaceShipGroups(int groups, int now)
        {
            if (groups <= 0 || GroundUnits == null) { return 0; }
            int[] cands = new int[_groundUsed.Length];
            int candCount = CollectFreeShips(cands);
            int next = 0;
            int placed = 0;
            for (int g = 0; g < groups && next < candCount; g++)
            {
                int size = Random.Range(Mathf.Max(ShipGroupSizeMin, 1), Mathf.Max(ShipGroupSizeMax, ShipGroupSizeMin) + 1);
                size = Mathf.Min(size, candCount - next);
                int lead = cands[next];
                ACO_Warship leader = GroundUnits[lead].GetComponent<ACO_Warship>();
                bool found = false;
                float x = 0f;
                float z = 0f;
                for (int attempt = 0; attempt < MaxPlacementAttempts && !found; attempt++)
                {
                    x = Random.Range(AreaMin.x, AreaMax.x);
                    z = Random.Range(AreaMin.y, AreaMax.y);
                    found = SampleSea(x, z, FleetRadius(leader));
                }
                if (!found) { break; }

                Vector3 center = new Vector3(x, SeaLevel, z);
                Quaternion rot = HvtRotation(true);
                MarkGround(lead, true, center, rot, now);
                next++;
                placed++;
                for (int k = 0; k < size - 1; k++)
                {
                    MarkGround(cands[next], true, center, rot, now);
                    _groundLeader[cands[next]] = (short)(lead * 16 + k);
                    next++;
                    placed++;
                }
            }
            return placed;
        }

        /// 기함(leaderCode) 주위에 호위함 count척. 호위함의 배치 위치/회전은 기함과 같다(같은 궤도) — 슬롯은 _groundLeader에.
        private int PlaceEscorts(int leaderCode, Vector3 center, Quaternion rot, int count, int now)
        {
            if (count <= 0 || GroundUnits == null) { return 0; }
            int saved = _markTag;
            _markTag = 0;
            int[] cands = new int[_groundUsed.Length];
            int candCount = CollectFreeShips(cands);
            int n = Mathf.Min(count, Mathf.Min(candCount, 15));
            for (int k = 0; k < n; k++)
            {
                MarkGround(cands[k], true, center, rot, now);
                _groundLeader[cands[k]] = (short)(leaderCode * 16 + k);
            }
            _markTag = saved;
            return n;
        }

        // ───────────── 전차 ─────────────

        /// 함선/전차 — 지상 그룹/방공 링/증원에 쓰지 않고 따로 놓는 유닛.
        private bool IsGroundMover(int i)
        {
            return IsGroundShip(i) || IsGroundTank(i);
        }

        private bool IsGroundTank(int i)
        {
            EnsureTankSlots();
            return i >= 0 && i < _tankSlot.Length && _tankSlot[i] >= 0;
        }

        /// 지상 풀 인덱스 → 전차 칸(풀 순서). 베이크된 배열이라 모든 클라이언트가 같다.
        private void EnsureTankSlots()
        {
            int n = GroundUnits != null ? GroundUnits.Length : 0;
            if (_tankSlot != null && _tankSlot.Length == n) { return; }
            _tankSlot = new int[n];
            _tankCount = 0;
            for (int i = 0; i < n; i++)
            {
                bool tank = GroundUnits[i] != null && GroundUnits[i].GetComponent<ACO_Tank>() != null;
                _tankSlot[i] = tank ? _tankCount : -1;
                if (tank) { _tankCount++; }
            }
        }

        /// 전차 후보: 쓰지 않던 전차(섞어서) → 파괴된 전차(섞어서). 살아 있는 전차는 제외.
        private int CollectFreeTanks(int[] outList)
        {
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = n;
                for (int i = 0; i < _groundUsed.Length; i++)
                {
                    if (!IsGroundTank(i) || IsProtected(_groundTag, i)) { continue; }
                    bool candidate = pass == 0 ? !_groundUsed[i] : (_groundUsed[i] && IsDestroyed(GroundUnits[i].GetComponent<SaccTarget>()));
                    if (candidate) { outList[n] = i; n++; }
                }
                ShuffleRange(outList, start, n);
            }
            return n;
        }

        /// 전차 하나에 항로를 주고 배치한다(배치 시각 = 출발 시각).
        private void MarkTank(int i, Vector3[] pts, int count, bool loop, float offset, int now)
        {
            Vector3 dir = count > 1 ? pts[1] - pts[0] : Vector3.forward;
            dir.y = 0f;
            Quaternion rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
            MarkGround(i, true, pts[0], rot, now);
            int slot = _tankSlot[i];
            int baseIdx = slot * ACO_Tank.MAX_ROUTE_POINTS;
            for (int k = 0; k < count; k++) { _tankRoute[baseIdx + k] = new Vector2(pts[k].x, pts[k].z); }
            _tankRouteCount[slot] = (byte)count;
            _tankLoop[slot] = loop;
            _tankOffset[slot] = (short)Mathf.RoundToInt(offset);
        }

        /// 공역 아무 곳 지면에 순찰 소대 groups개: 반경 TankPatrolRadius의 사각 고리(네 점 모두 지면), 소대원은 같은 고리를 간격을 두고 돈다.
        private int PlaceTankPlatoons(int groups, int now)
        {
            if (groups <= 0 || GroundUnits == null) { return 0; }
            int[] cands = new int[_groundUsed.Length];
            int candCount = CollectFreeTanks(cands);
            Vector3[] pts = new Vector3[4];
            int next = 0;
            int placed = 0;
            for (int g = 0; g < groups && next < candCount; g++)
            {
                bool found = false;
                for (int attempt = 0; attempt < MaxPlacementAttempts && !found; attempt++)
                {
                    float cx = Random.Range(AreaMin.x, AreaMax.x);
                    float cz = Random.Range(AreaMin.y, AreaMax.y);
                    float a0 = Random.Range(0f, Mathf.PI * 2f);
                    found = true;
                    for (int k = 0; k < 4 && found; k++)
                    {
                        float a = a0 + k * Mathf.PI * 0.5f + Random.Range(-0.3f, 0.3f);
                        float r = TankPatrolRadius * Random.Range(0.7f, 1.2f);
                        float x = cx + Mathf.Cos(a) * r;
                        float z = cz + Mathf.Sin(a) * r;
                        if (!SampleGround(x, z)) { found = false; break; }
                        pts[k] = new Vector3(x, _sampleY, z);
                    }
                    for (int k = 0; k < 4 && found; k++) { found = PathClear(pts[k], pts[(k + 1) % 4]); }
                }
                if (!found) { break; }
                int size = Mathf.Min(Mathf.Max(TankGroupSize, 1), candCount - next);
                for (int k = 0; k < size; k++)
                {
                    MarkTank(cands[next], pts, 4, true, k * TankSpacing, now);
                    next++;
                    placed++;
                }
            }
            return placed;
        }

        /// 호위 항로(_escortRoute[offset..]) 옆 groups곳에 매복조: 항로에서 멀리 떨어진 출발점 → 항로 옆 매복 지점까지 가서 멈춘다.
        /// 매복 지점은 항로를 따라 고르게(앞쪽부터), 지면이 있는 곳만. 목표 아님(태그 없음).
        private int PlaceAmbushes(int offset, int count, int groups, int now)
        {
            if (groups <= 0 || GroundUnits == null) { return 0; }
            int saved = _markTag;
            _markTag = 0;
            int[] cands = new int[_groundUsed.Length];
            int candCount = CollectFreeTanks(cands);
            Vector3[] pts = new Vector3[2];
            int next = 0;
            int placed = 0;
            for (int g = 0; g < groups && next < candCount; g++)
            {
                float f = (g + 1) / (float)(groups + 1) * (count - 1);
                int k = Mathf.Min(Mathf.FloorToInt(f), count - 2);
                Vector3 a = _escortRoute[offset + k];
                Vector3 b = _escortRoute[offset + k + 1];
                Vector3 onRoute = Vector3.Lerp(a, b, f - k);
                Vector3 dir = b - a;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1f) { dir = Vector3.forward; }
                Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized;
                bool found = false;
                for (int attempt = 0; attempt < 8 && !found; attempt++)
                {
                    float sign = Random.value < 0.5f ? -1f : 1f;
                    float d = Random.Range(AmbushDistanceMin, AmbushDistanceMax);
                    Vector3 ambush = onRoute + side * (d * sign) + dir.normalized * Random.Range(-300f, 300f);
                    Vector3 start = ambush + side * (AmbushApproachDistance * sign);
                    if (!SampleDryGround(ambush.x, ambush.z)) { continue; }
                    pts[1] = new Vector3(ambush.x, _sampleY, ambush.z);
                    if (!SampleDryGround(start.x, start.z)) { continue; }
                    pts[0] = new Vector3(start.x, _sampleY, start.z);
                    if (!PathClear(pts[0], pts[1])) { continue; }
                    found = true;
                }
                if (!found) { continue; }
                int size = Mathf.Min(Mathf.Max(TankGroupSize, 1), candCount - next);
                for (int m = 0; m < size; m++)
                {
                    // 매복조는 끝점에서 멈추므로, 앞선 거리를 주면 끝에서 겹치지 않게 출발점을 조금씩 옆으로 민다.
                    Vector3 shift = dir.normalized * ((m - (size - 1) * 0.5f) * TankSpacing);
                    Vector3[] own = new Vector3[2];
                    own[0] = pts[0] + shift;
                    own[1] = pts[1] + shift;
                    MarkTank(cands[next], own, 2, false, 0f, now);
                    next++;
                    placed++;
                }
            }
            _markTag = saved;
            return placed;
        }

        private ACO_Warship ResolveLeader(int leaderCode)
        {
            UdonSharpBehaviour unit = leaderCode >= LEADER_HVT ? UnitAt(CAT_HVT, leaderCode - LEADER_HVT) : UnitAt(CAT_GROUND, leaderCode);
            return unit != null ? unit.GetComponent<ACO_Warship>() : null;
        }

        /// 고가치 표적 방위. 함선은 정수+0.5도 — 함선이 방위각의 정수 부분으로 도는 방향을 정하므로, 동기화 오차로 경계에서 갈리지 않게 한다.
        private Quaternion HvtRotation(bool ship)
        {
            float yaw = Random.Range(0f, 360f);
            if (ship) { yaw = Mathf.Floor(yaw) + 0.5f; }
            return Quaternion.Euler(0f, yaw, 0f);
        }

        /// (x, z)를 중심으로 반경 radius(+SeaClearance) 순항 원 전체가 바다면 true, 높이는 _sampleY(= SeaLevel).
        private bool SampleSea(float x, float z, float radius)
        {
            if (x < AreaMin.x || x > AreaMax.x || z < AreaMin.y || z > AreaMax.y) { return false; }
            if (InSafeZone(x, z) || !IsSeaAt(x, z)) { return false; }
            float r = Mathf.Max(radius, 0f) + SeaClearance;
            int n = Mathf.Max(SeaCheckPoints, 0);
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                float px = x + Mathf.Cos(a) * r;
                float pz = z + Mathf.Sin(a) * r;
                if (InSafeZone(px, pz) || !IsSeaAt(px, pz)) { return false; }
            }
            _sampleY = SeaLevel;
            return true;
        }

        private bool IsSeaAt(float x, float z)
        {
            int ground = GroundLayers;
            int sea = SeaSurfaceLayers;
            RaycastHit hit;
            Vector3 origin = new Vector3(x, RaycastHeight, z);
            if (!Physics.Raycast(origin, Vector3.down, out hit, RaycastDistance, ground | sea, QueryTriggerInteraction.Ignore)) { return NoHitIsSea; }
            if (((1 << hit.collider.gameObject.layer) & sea) != 0) { return true; }
            return hit.point.y < SeaLevel - MinSeaDepth;
        }

        /// focus에서 minDist~maxDist 떨어진 바다 지점(순항 원 포함)을 찾아 _pickX/_pickZ에 넣는다. maxDist <= 0이면 공역 아무 곳.
        private bool PickSeaNear(Vector3 focus, float minDist, float maxDist, float radius)
        {
            if (minDist < 0f)
            {
                if (!SampleSea(focus.x, focus.z, radius)) { return false; }
                _pickX = focus.x;
                _pickZ = focus.z;
                return true;
            }
            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                float x;
                float z;
                if (maxDist > 0f)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    float lo = Mathf.Max(minDist, 0f);
                    float hi = Mathf.Max(maxDist, lo);
                    float d = Mathf.Sqrt(Random.Range(lo * lo, hi * hi));
                    x = focus.x + Mathf.Cos(a) * d;
                    z = focus.z + Mathf.Sin(a) * d;
                }
                else
                {
                    x = Random.Range(AreaMin.x, AreaMax.x);
                    z = Random.Range(AreaMin.y, AreaMax.y);
                }
                if (!SampleSea(x, z, radius)) { continue; }
                _pickX = x;
                _pickZ = z;
                return true;
            }
            return false;
        }

        private bool InSafeZone(float x, float z)
        {
            if (SafeZones == null) { return false; }
            for (int i = 0; i < SafeZones.Length; i++)
            {
                Collider zone = SafeZones[i];
                if (zone == null) { continue; }
                Bounds b = zone.bounds;
                if (x >= b.min.x - SafeZoneMargin && x <= b.max.x + SafeZoneMargin
                    && z >= b.min.z - SafeZoneMargin && z <= b.max.z + SafeZoneMargin)
                {
                    return true;
                }
            }
            return false;
        }

        // ───────────── 전투기 편대 + 임무 ─────────────

        /// order[0..budget) 를 2기/4기 편대로 배치한다. 편대마다 임무 지점(엄호/CAP)을 정하고,
        /// inside면 그 근처 공역 안에서, 아니면 공역 밖에서 그 지점을 향해 스폰한다. 결과는 _lastGroups(편대 수)/_lastPlaced.
        private void PlaceFighters(int[] order, int count, int budget, int now, bool inside)
        {
            _lastGroups = 0;
            _lastPlaced = 0;
            _deployNowMs = now;
            budget = Mathf.Min(budget, count);
            int next = 0;

            while (next < budget)
            {
                int size = Random.value < FourShipChance ? 4 : 2;
                size = Mathf.Min(size, budget - next);

                PickTaskPoint();
                Vector3 task = _taskPoint;
                int tier = RollTier();

                Vector3 spawn;
                if (inside)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    float d = Random.Range(InsideSpawnDistanceMin, InsideSpawnDistanceMax);
                    spawn = new Vector3(task.x + Mathf.Cos(a) * d, task.y, task.z + Mathf.Sin(a) * d);
                    spawn.x = Mathf.Clamp(spawn.x, AreaMin.x, AreaMax.x);
                    spawn.z = Mathf.Clamp(spawn.z, AreaMin.y, AreaMax.y);
                }
                else
                {
                    // 목표 지점 증원은 목표에 가까운 쪽 경계 밖에서 들어온다 — 공역이 넓으면 무작위 방향은 도착까지 너무 오래 걸린다.
                    spawn = _focusActive ? PointOutsideNear(_focusPos) : RandomPointOutsideArea();
                    spawn.y = Random.Range(SpawnAltitudeMin, SpawnAltitudeMax);
                }

                Vector3 flat = task - spawn;
                flat.y = 0f;
                if (flat.sqrMagnitude < 1f) { flat = Vector3.forward; }
                Quaternion heading = Quaternion.LookRotation(flat.normalized, Vector3.up);

                for (int k = 0; k < size; k++)
                {
                    MarkFighter(order[next + k], true, spawn + heading * FormationOffset(k), heading, task, tier, now);
                }
                next += size;
                _lastGroups++;
                _lastPlaced += size;
            }
        }

        /// 편대 임무 지점을 _taskPoint에 넣는다.
        /// 엄호: 지금 배치돼 있고 살아 있는 지상 유닛/고가치 표적 중 하나의 상공. CAP: CapPoints 중 하나. 둘 다 없으면 공역 안 무작위.
        private void PickTaskPoint()
        {
            if (_focusActive)
            {
                _taskPoint = new Vector3(_focusPos.x, _focusPos.y + Random.Range(CoverAltitudeMin, CoverAltitudeMax), _focusPos.z);
                return;
            }

            int capCount = CountValidCapPoints();
            bool hasCover = CountCoverCandidates() > 0;
            bool doCover = hasCover && (capCount == 0 || Random.value < CoverGroundChance);

            if (doCover)
            {
                Vector3 ground = PickCoverCandidate(Random.Range(0, CountCoverCandidates()));
                _taskPoint = new Vector3(ground.x, ground.y + Random.Range(CoverAltitudeMin, CoverAltitudeMax), ground.z);
                return;
            }
            if (capCount > 0)
            {
                _taskPoint = PickCapPoint(Random.Range(0, capCount));
                return;
            }
            _taskPoint = RandomIngressPoint();
        }

        private int CountValidCapPoints()
        {
            int n = 0;
            if (CapPoints == null) { return 0; }
            for (int i = 0; i < CapPoints.Length; i++) { if (CapPoints[i] != null) { n++; } }
            return n;
        }

        private Vector3 PickCapPoint(int nth)
        {
            for (int i = 0; i < CapPoints.Length; i++)
            {
                if (CapPoints[i] == null) { continue; }
                if (nth == 0)
                {
                    // CAP 지점을 실수로 지면 높이에 두면 그 고도에서 스폰/선회하므로, 스폰 최저 고도 아래로는 내리지 않는다.
                    Vector3 p = CapPoints[i].position;
                    p.y = Mathf.Max(p.y, SpawnAltitudeMin);
                    return p;
                }
                nth--;
            }
            return RandomIngressPoint();
        }

        private int CountCoverCandidates()
        {
            int n = 0;
            for (int i = 0; i < _groundUsed.Length; i++) { if (IsCoverable(GroundUnits[i], _groundUsed[i], _groundTimeMs[i])) { n++; } }
            for (int i = 0; i < _hvtUsed.Length; i++) { if (IsCoverable(HighValueTargets[i], _hvtUsed[i], _hvtTimeMs[i])) { n++; } }
            return n;
        }

        private Vector3 PickCoverCandidate(int nth)
        {
            for (int i = 0; i < _groundUsed.Length; i++)
            {
                if (!IsCoverable(GroundUnits[i], _groundUsed[i], _groundTimeMs[i])) { continue; }
                if (nth == 0) { return _groundPos[i]; }
                nth--;
            }
            for (int i = 0; i < _hvtUsed.Length; i++)
            {
                if (!IsCoverable(HighValueTargets[i], _hvtUsed[i], _hvtTimeMs[i])) { continue; }
                if (nth == 0) { return _hvtPos[i]; }
                nth--;
            }
            return RandomIngressPoint();
        }

        /// 배치돼 있고 살아 있는 유닛. 이번 배치에서 방금 놓은 유닛은 아직 부활 전이라 체력이 0일 수 있지만 곧 살아나므로 포함한다.
        private bool IsCoverable(UdonSharpBehaviour unit, bool used, int placedTimeMs)
        {
            if (!used || unit == null) { return false; }
            // 순항하는 함선은 배치 위치가 실제 위치가 아니라 엄호 대상에서 뺀다.
            if (unit.GetComponent<ACO_Warship>() != null || unit.GetComponent<ACO_Tank>() != null) { return false; }
            if (placedTimeMs == _deployNowMs) { return true; }
            return !IsDestroyed(unit.GetComponent<SaccTarget>());
        }

        /// 편대 숙련도: 에이스를 먼저 판정하고, 아니면 베테랑, 둘 다 아니면 신병.
        private int RollTier()
        {
            float r = Random.value;
            if (r < _aceChance) { return ACO_NPCFighterAI.SKILL_ACE; }
            if (r < _aceChance + _vetChance) { return ACO_NPCFighterAI.SKILL_VETERAN; }
            return ACO_NPCFighterAI.SKILL_ROOKIE;
        }

        /// focus에 가장 가까운 공역 경계 바깥 지점(경계를 따라 FocusEntrySpread만큼 흩뜨린다).
        private Vector3 PointOutsideNear(Vector3 focus)
        {
            float push = Random.Range(SpawnOutsideMin, SpawnOutsideMax);
            float spread = Random.Range(-FocusEntrySpread, FocusEntrySpread);
            float dMinX = focus.x - AreaMin.x;
            float dMaxX = AreaMax.x - focus.x;
            float dMinZ = focus.z - AreaMin.y;
            float dMaxZ = AreaMax.y - focus.z;
            float best = Mathf.Min(Mathf.Min(dMinX, dMaxX), Mathf.Min(dMinZ, dMaxZ));

            float x;
            float z;
            if (best == dMinX) { x = AreaMin.x - push; z = Mathf.Clamp(focus.z + spread, AreaMin.y, AreaMax.y); }
            else if (best == dMaxX) { x = AreaMax.x + push; z = Mathf.Clamp(focus.z + spread, AreaMin.y, AreaMax.y); }
            else if (best == dMinZ) { z = AreaMin.y - push; x = Mathf.Clamp(focus.x + spread, AreaMin.x, AreaMax.x); }
            else { z = AreaMax.y + push; x = Mathf.Clamp(focus.x + spread, AreaMin.x, AreaMax.x); }
            return new Vector3(x, 0f, z);
        }

        private void AppendObjective(int hvtIndex)
        {
            int[] grown = new int[_objectiveOrder.Length + 1];
            for (int i = 0; i < _objectiveOrder.Length; i++) { grown[i] = _objectiveOrder[i]; }
            grown[_objectiveOrder.Length] = hvtIndex;
            _objectiveOrder = grown;
        }

        // ───────────── 호위 대상 (오너) ─────────────

        /// 호위 대상 하나를 focus 근처에서 출발시킨다: 항로 길이 routeLength(m), 아래 방공 시설 sites개(지면이 있는 곳만), 태그 tag.
        /// 이동 방식은 고른 유닛의 MoveMode를 따른다(공중 = 고도 EscortAltitude, 해상 = 바다 항로, 지상 = 지면 항로).
        /// routeSeed != 0이면 사전 확인(CanBuildEscortRoute)과 같은 씨앗으로 항로를 만든다 — 같은 항로가 나온다.
        /// moveMode에 ESCORT_HOSTILE을 더하면(|) 적 폭격기(ACO_EscortUnit.Hostile)를 고르고 항로 목적지를 도시로 잡는다(요격 페이즈).
        public const int ESCORT_HOSTILE = 0x100;

        public void RequestDeployEscort(int tag, Vector3 focus, float routeLength, int sites, int moveMode, int units, int ambushGroups, int routeSeed)
        {
            if (Networking.IsOwner(gameObject)) { DeployEscort(tag, focus, routeLength, sites, moveMode, units, ambushGroups, routeSeed); }
            else
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetDeployEscort),
                    tag, focus, routeLength, sites, moveMode, units, ambushGroups, routeSeed);
            }
        }

        [NetworkCallable]
        public void NetDeployEscort(int tag, Vector3 focus, float routeLength, int sites, int moveMode, int units, int ambushGroups, int routeSeed)
        {
            DeployEscort(tag, focus, routeLength, sites, moveMode, units, ambushGroups, routeSeed);
        }

        /// 이동 방식이 moveMode인 호위 대상 units대를 같은 항로로 출발시킨다(여러 대면 EscortConvoySpacing 간격 행렬, 선두가 앞).
        /// 항로 아래(옆) 방공 시설 sites곳, 매복 전차조 ambushGroups곳 — 둘 다 지면이 있는 곳만.
        public void DeployEscort(int tag, Vector3 focus, float routeLength, int sites, int moveMode, int units, int ambushGroups, int routeSeed)
        {
            if (!Networking.IsOwner(gameObject) || EscortUnits == null) { return; }
            EnsureArrays();
            int now = Networking.GetServerTimeInMilliseconds();
            bool hostile = (moveMode & ESCORT_HOSTILE) != 0;
            moveMode &= 0xFF;

            // 후보: 이동 방식(과 적/아군)이 맞는 것 중 쓰지 않던 것 → 파괴된 것. 살아서 가는 중인 것은 건드리지 않는다.
            units = Mathf.Max(units, 1);
            int[] picked = new int[units];
            int pickedCount = 0;
            for (int pass = 0; pass < 2 && pickedCount < units; pass++)
            {
                for (int i = 0; i < _escortUsed.Length && pickedCount < units; i++)
                {
                    ACO_EscortUnit u = EscortUnits[i];
                    if (u == null || u.MoveMode != moveMode || u.Hostile != hostile) { continue; }
                    bool candidate = pass == 0 ? !_escortUsed[i] : (_escortUsed[i] && IsDestroyed(u.TargetHealth));
                    if (!candidate) { continue; }
                    bool dup = false;
                    for (int k = 0; k < pickedCount; k++) { if (picked[k] == i) { dup = true; } }
                    if (dup) { continue; }
                    picked[pickedCount] = i;
                    pickedCount++;
                }
            }
            if (pickedCount == 0) { Debug.LogWarning("[ACO] 이동 방식 " + moveMode + (hostile ? "인 적 폭격기" : "인 호위 대상") + "가 풀에 없습니다(EscortUnits)"); return; }

            // 항로는 첫 유닛 칸에 만들고 나머지에 복사한다(행렬 길이만큼 길게).
            int count = Mathf.Clamp(EscortRoutePoints, 2, ACO_EscortUnit.MAX_ROUTE_POINTS);
            int lead = picked[0];
            int leadOffset = lead * ACO_EscortUnit.MAX_ROUTE_POINTS;
            float column = FormationLength(pickedCount, hostile);
            // 사전 확인과 같은 씨앗 → 같은 무작위 순서 → 같은 항로(항로 만들기 전까지 다른 무작위 호출이 없어야 한다).
            if (routeSeed != 0) { Random.InitState(routeSeed); }
            _routeHostile = hostile;
            bool built = BuildEscortRoute(moveMode, focus, routeLength + column, count, _escortRoute, leadOffset);
            _routeHostile = false;
            if (routeSeed != 0) { ReseedRandom(); }
            if (!built)
            {
                Debug.LogWarning("[ACO] 호위 항로를 만들지 못했습니다(공역/지형 조건)");
                return;
            }
            count = _routeBuiltCount; // 착륙 항로는 경유점이 더 많다
            for (int k = 0; k < pickedCount; k++)
            {
                int idx = picked[k];
                int dst = idx * ACO_EscortUnit.MAX_ROUTE_POINTS;
                if (idx != lead) { for (int j = 0; j < count; j++) { _escortRoute[dst + j] = _escortRoute[leadOffset + j]; } }
                _escortRouteCount[idx] = (byte)count;
                _escortArrival[idx] = (byte)_routeArrival;
                // 선두(k = 0)가 가장 앞선다. 적 폭격기는 삼각 대형: k = 1, 2가 첫 줄 왼쪽/오른쪽, 3, 4가 둘째 줄 … (콘보이는 한 줄)
                if (hostile)
                {
                    int rank = (k + 1) / 2;
                    _escortOffset[idx] = (short)Mathf.RoundToInt(column - rank * BomberFormationBack);
                    _escortLateral[idx] = (short)Mathf.RoundToInt(k == 0 ? 0f : (k % 2 == 1 ? -1f : 1f) * rank * BomberFormationSide);
                }
                else
                {
                    _escortOffset[idx] = (short)Mathf.RoundToInt((pickedCount - 1 - k) * EscortConvoySpacing);
                    _escortLateral[idx] = 0;
                }
                MarkEscort(idx, true, tag, now);
            }

            int placedSites = PlaceRouteSites(leadOffset, count, sites, now);
            int placedAmbush = PlaceAmbushes(leadOffset, count, ambushGroups, now);

            _deployCount++;
            RequestSerialization();
            Debug.Log("[ACO] 호위 출발 #" + _deployCount + ": 태그 " + tag + ", " + pickedCount + "대, 경유점 " + count
                + ", 방공 시설 " + placedSites + "곳, 매복 전차 " + placedAmbush + "대");
            ApplyChanges();
        }

        /// 행렬 길이(m) — 선두가 맨 뒤보다 앞선 거리: 콘보이 = 한 줄(EscortConvoySpacing 간격), 적 폭격기 = 삼각 대형(줄마다 BomberFormationBack).
        private float FormationLength(int units, bool hostile)
        {
            int n = Mathf.Max(units - 1, 0);
            return hostile ? ((n + 1) / 2) * BomberFormationBack : n * EscortConvoySpacing;
        }

        private void MarkEscort(int i, bool used, int tag, int now)
        {
            _escortUsed[i] = used;
            _escortSerial[i] = (byte)((_escortSerial[i] + 1) & 0xFF);
            _escortTimeMs[i] = now;
            _escortTag[i] = (short)(used ? tag : 0);
        }

        /// 목적지에서 거꾸로 항로를 만든다: 목적지 = 도시/비행장(지형 장식, EscortDestinationChance) — 지상은 그 가장자리 바깥에서
        /// 도시 쪽을 보며 들어오고, 공중은 그 상공에서 끝난다. 장식이 없으면(또는 해상) 공역 아무 곳. 출발점 = 목적지에서 routeLength 거슬러.
        /// 중간 경유점은 옆으로 흩뜨린다. 이동 방식에 맞는지(공역 안, 해상 = 바다, 지상 = 완만한 마른 지면 + 건물 자리 밖) 확인하고,
        /// 안 맞으면 다른 목적지/방향으로 다시 시도한다(후반 시도는 장식 없이 아무 곳). 결과는 dst[offset..], 목적지 자리는 _routeDestSite.
        /// focus는 쓰지 않는다(예전 출발 기준 — 호위 항로는 플레이어와 무관하게 공역 어디든).
        private bool BuildEscortRoute(int moveMode, Vector3 focus, float routeLength, int count, Vector3[] dst, int offset)
        {
            int destCount = moveMode == ACO_EscortUnit.MOVE_SEA ? 0 : CollectDestinations();
            float margin = Mathf.Min(1000f, Mathf.Min(AreaMax.x - AreaMin.x, AreaMax.y - AreaMin.y) * 0.25f);
            _routeBuiltCount = count;
            // 도착 방식: 공중 = 투하(비행장이면 아래에서 착륙으로), 지상 = 정차, 해상 = 계속 진행
            int arrival = moveMode == ACO_EscortUnit.MOVE_AIR ? ACO_EscortUnit.ARRIVE_DROP
                : (moveMode == ACO_EscortUnit.MOVE_GROUND ? ACO_EscortUnit.ARRIVE_STOP : ACO_EscortUnit.ARRIVE_CONTINUE);
            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                _routeDestSite = -1;
                _routeArrival = arrival;
                Vector3 end;
                float heading; // 진행 방향(출발 → 목적지)
                bool useSite = destCount > 0 && attempt < MaxPlacementAttempts * 3 / 4 && (_routeHostile || Random.value < EscortDestinationChance);
                if (useSite)
                {
                    int site = _destList[Random.Range(0, destCount)];
                    Vector3 c = Features.GetSitePosition(site);
                    float ea = Random.Range(0f, Mathf.PI * 2f);
                    if (moveMode == ACO_EscortUnit.MOVE_AIR && Features.GetSiteKind(site) == ACO_TerrainFeatures.SITE_AIRFIELD)
                    {
                        // 비행장: 활주로에 착륙하는 항로(경유점 6개)
                        if (BuildLandingRoute(site, routeLength, dst, offset))
                        {
                            _routeDestSite = site;
                            _routeArrival = ACO_EscortUnit.ARRIVE_LAND;
                            _routeBuiltCount = MAX_LANDING_POINTS;
                            return true;
                        }
                        continue;
                    }
                    if (moveMode == ACO_EscortUnit.MOVE_GROUND)
                    {
                        // 도시 가장자리 바깥에 멈추고, 바깥에서 도시 쪽으로(±60°) 들어온다.
                        float er = Features.GetSiteRadius(site) + EscortDestinationMargin;
                        end = new Vector3(c.x + Mathf.Cos(ea) * er, 0f, c.z + Mathf.Sin(ea) * er);
                        heading = ea + Mathf.PI + Random.Range(-60f, 60f) * Mathf.Deg2Rad;
                    }
                    else
                    {
                        end = new Vector3(c.x, 0f, c.z);
                        heading = ea;
                    }
                    _routeDestSite = site;
                }
                else
                {
                    end = new Vector3(Random.Range(AreaMin.x + margin, AreaMax.x - margin), 0f, Random.Range(AreaMin.y + margin, AreaMax.y - margin));
                    heading = Random.Range(0f, Mathf.PI * 2f);
                }
                Vector3 dir = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
                Vector3 side = new Vector3(-dir.z, 0f, dir.x);

                bool ok = true;
                float spread = Mathf.Min(EscortRouteLateral, routeLength * 0.15f);
                for (int k = 0; k < count && ok; k++)
                {
                    float f = k / (float)(count - 1);
                    float lateral = (k == 0 || k == count - 1) ? 0f : Random.Range(-spread, spread);
                    Vector3 pt = end - dir * (routeLength * (1f - f)) + side * lateral;
                    if (pt.x < AreaMin.x || pt.x > AreaMax.x || pt.z < AreaMin.y || pt.z > AreaMax.y) { ok = false; break; }
                    if (!RoutePointHeight(moveMode, pt.x, pt.z)) { ok = false; break; }
                    pt.y = _sampleY;
                    // 공중 투하: 마지막 경유점은 목적지 지면 위 EscortDropAltitude로 내려온다(내려오는 구간에 산이 있으면 순항 고도 그대로).
                    if (moveMode == ACO_EscortUnit.MOVE_AIR && !_routeHostile && k == count - 1 && k > 0)
                    {
                        float low = Mathf.Max(GroundHeightAt(pt.x, pt.z) + Mathf.Max(EscortDropAltitude, EscortTerrainClearance * 0.5f), SeaLevel + EscortDropAltitude);
                        Vector3 lowered = new Vector3(pt.x, Mathf.Min(low, pt.y), pt.z);
                        if (LineClearsGround(dst[offset + k - 1], lowered, 150f)) { pt = lowered; }
                    }
                    dst[offset + k] = pt;
                    // 해상/지상은 이전 경유점과의 사이도 빠르게 확인한다(세밀한 확인 전에 일찍 버리려고).
                    if (k > 0 && moveMode != ACO_EscortUnit.MOVE_AIR)
                    {
                        Vector3 prev = dst[offset + k - 1];
                        for (int j = 1; j < 4 && ok; j++)
                        {
                            Vector3 mid = Vector3.Lerp(prev, pt, j / 4f);
                            if (!RoutePointHeight(moveMode, mid.x, mid.z)) { ok = false; }
                        }
                    }
                }
                // 해상/지상: 차량이 실제로 달리는 곡선(Catmull-Rom)을 촘촘히 따라가며 확인한다. 무작위를 쓰지 않아 씨앗 재현에 영향이 없다.
                if (ok && moveMode != ACO_EscortUnit.MOVE_AIR) { ok = CheckRouteCurve(moveMode, dst, offset, count); }
                if (ok) { return true; }
            }
            _routeDestSite = -1;
            return false;
        }

        private const int MAX_LANDING_POINTS = 6;

        /// 비행장 착륙 항로(경유점 6개): 출발 → 중간 → 중간 접근점(IAF) → 최종 접근 시작점(FAF, 활주로 축 위 LandingApproachDistance,
        /// 높이 LandingApproachHeight) → 접지점(활주로 시작에서 200m) → 정지점(활주로 가운데 너머 300m). 착륙 방향은 무작위(활주로 양쪽).
        /// 출발~IAF는 순항 고도, 전체 길이 ≈ routeLength. 접근 구간은 지형에 걸리지 않는지 확인한다. 성공하면 dst[offset..offset+6).
        private bool BuildLandingRoute(int site, float routeLength, Vector3[] dst, int offset)
        {
            Vector3 c = Features.GetSitePosition(site);
            float yaw = Features.GetSiteYaw(site) * Mathf.Deg2Rad;
            Vector3 rw = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            if (Random.value < 0.5f) { rw = -rw; }
            float rwyY = c.y + 0.5f;
            Vector3 stop = new Vector3(c.x + rw.x * 300f, rwyY, c.z + rw.z * 300f);
            Vector3 td = new Vector3(c.x - rw.x * 1000f, rwyY, c.z - rw.z * 1000f);
            Vector3 faf = td - rw * LandingApproachDistance;
            faf.y = rwyY + LandingApproachHeight;
            // IAF: 활주로 축에서 ±40° 비껴 들어온다
            float ia = Mathf.Atan2(rw.z, rw.x) + Random.Range(-40f, 40f) * Mathf.Deg2Rad;
            Vector3 iDir = new Vector3(Mathf.Cos(ia), 0f, Mathf.Sin(ia));
            Vector3 iaf = faf - iDir * LandingIntermediateDistance;
            iaf.y = Mathf.Max(rwyY + LandingIntermediateHeight, GroundHeightAt(iaf.x, iaf.z) + EscortTerrainClearance);
            // 순항: IAF 앞 남은 길이만큼, IAF 방향에서 ±50° 비껴
            float remaining = Mathf.Max(routeLength - LandingIntermediateDistance - LandingApproachDistance - 1300f, 3000f);
            float ca = ia + Random.Range(-50f, 50f) * Mathf.Deg2Rad;
            Vector3 cDir = new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca));
            Vector3 start = iaf - cDir * remaining;
            Vector3 side = new Vector3(-cDir.z, 0f, cDir.x);
            Vector3 mid = (start + iaf) * 0.5f + side * Random.Range(-1f, 1f) * Mathf.Min(EscortRouteLateral, remaining * 0.15f);
            if (!InArea(start) || !InArea(mid) || !InArea(iaf) || !InArea(faf)) { return false; }
            if (!RoutePointHeight(ACO_EscortUnit.MOVE_AIR, start.x, start.z)) { return false; }
            start.y = _sampleY;
            if (!RoutePointHeight(ACO_EscortUnit.MOVE_AIR, mid.x, mid.z)) { return false; }
            mid.y = _sampleY;
            // 접근 구간: IAF → FAF는 150m, FAF → 접지점은 40m 이상 지형 위
            if (!LineClearsGround(iaf, faf, 150f) || !LineClearsGround(faf, td + Vector3.up * 40f, 40f)) { return false; }
            dst[offset] = start;
            dst[offset + 1] = mid;
            dst[offset + 2] = iaf;
            dst[offset + 3] = faf;
            dst[offset + 4] = td;
            dst[offset + 5] = stop;
            return true;
        }

        private bool InArea(Vector3 p)
        {
            return p.x >= AreaMin.x && p.x <= AreaMax.x && p.z >= AreaMin.y && p.z <= AreaMax.y;
        }

        /// (x, z)의 지면 높이(맞는 게 없으면 해수면).
        private float GroundHeightAt(float x, float z)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(x, RaycastHeight, z), Vector3.down, out hit, RaycastDistance, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(hit.point.y, SeaLevel);
            }
            return SeaLevel;
        }

        /// a → b 직선 아래 지형이 clearance(m)보다 가깝지 않은지(8점 확인).
        private bool LineClearsGround(Vector3 a, Vector3 b, float clearance)
        {
            for (int j = 1; j <= 8; j++)
            {
                Vector3 p = Vector3.Lerp(a, b, j / 8f);
                if (GroundHeightAt(p.x, p.z) > p.y - clearance) { return false; }
            }
            return true;
        }

        /// 호위 목적지 후보(도시/비행장)를 _destList에 모은다(순서는 자리 번호 — 씨앗 재현이 같도록 무작위를 쓰지 않는다).
        private bool _routeHostile;   // 지금 만드는 항로가 적 폭격기 것(목적지 = 도시만, 늘 도시로, 착륙·저공 투하 없음)

        private int CollectDestinations()
        {
            if (Features == null || (!_routeHostile && EscortDestinationChance <= 0f) || !Features.HasSites()) { return 0; }
            int total = Features.GetSiteCount();
            if (_destList == null || _destList.Length < total) { _destList = new int[Mathf.Max(total, 8)]; }
            int n = 0;
            for (int i = 0; i < total; i++)
            {
                int kind = Features.GetSiteKind(i);
                if (kind == ACO_TerrainFeatures.SITE_CITY || (!_routeHostile && kind == ACO_TerrainFeatures.SITE_AIRFIELD)) { _destList[n] = i; n++; }
            }
            return n;
        }

        /// 마지막으로 만든(확인한) 호위 항로의 목적지 자리(도시/비행장). 없으면 -1 — 관리자가 배너에 목적지 이름을 띄운다.
        public int GetLastRouteSite() { return _routeDestSite; }

        /// 경유점 dst[offset..offset+count)을 잇는 곡선을 RouteCheckSpacing마다 확인한다.
        /// 지상: 마른 지면(SampleDryGround) + 확인점 사이 경사 MaxRouteGrade 이하. 해상: 전부 바다.
        private bool CheckRouteCurve(int moveMode, Vector3[] dst, int offset, int count)
        {
            float spacing = Mathf.Max(RouteCheckSpacing, 10f);
            float maxGrade = Mathf.Tan(Mathf.Clamp(MaxRouteGrade, 1f, 89f) * Mathf.Deg2Rad);
            bool hasPrev = false;
            Vector3 prev = Vector3.zero;
            for (int k = 0; k < count - 1; k++)
            {
                Vector3 a = dst[offset + k];
                Vector3 b = dst[offset + k + 1];
                float flat = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                int steps = Mathf.Max(Mathf.CeilToInt(flat / spacing), 1);
                for (int j = (k == 0 ? 0 : 1); j <= steps; j++)
                {
                    Vector3 c = RouteCurvePoint(dst, offset, count, k, j / (float)steps);
                    if (!RoutePointHeight(moveMode, c.x, c.z)) { return false; }
                    if (moveMode == ACO_EscortUnit.MOVE_GROUND)
                    {
                        Vector3 here = new Vector3(c.x, _sampleY, c.z);
                        if (hasPrev)
                        {
                            float dh = new Vector2(here.x - prev.x, here.z - prev.z).magnitude;
                            if (dh > 0.5f && Mathf.Abs(here.y - prev.y) / dh > maxGrade) { return false; }
                        }
                        prev = here;
                        hasPrev = true;
                    }
                }
            }
            return true;
        }

        /// ACO_EscortUnit과 같은 Catmull-Rom(끝점 복제) — 확인하는 곡선이 차량이 달리는 곡선과 같도록.
        private Vector3 RouteCurvePoint(Vector3[] dst, int offset, int count, int k, float u)
        {
            Vector3 p1 = dst[offset + k];
            Vector3 p2 = dst[offset + k + 1];
            Vector3 p0 = k > 0 ? dst[offset + k - 1] : p1 + (p1 - p2);
            Vector3 p3 = k + 2 < count ? dst[offset + k + 2] : p2 + (p2 - p1);
            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// SampleGround + 물 피하기: 물 콜라이더에 먼저 맞거나 지면이 해수면 + RouteWaterMargin보다 낮으면 false. 높이는 _sampleY.
        private bool SampleDryGround(float x, float z)
        {
            if (AvoidWaterOnGroundRoutes)
            {
                int ground = GroundLayers;
                int sea = SeaSurfaceLayers;
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(x, RaycastHeight, z), Vector3.down, out hit, RaycastDistance, ground | sea, QueryTriggerInteraction.Ignore))
                {
                    if (((1 << hit.collider.gameObject.layer) & sea) != 0) { return false; }
                    if (hit.point.y < SeaLevel + RouteWaterMargin) { return false; }
                }
            }
            return SampleGround(x, z);
        }

        /// 항로 한 점의 높이를 _sampleY에. 공중 = 지형 + 여유 이상의 EscortAltitude, 해상 = 바다면 해수면, 지상 = 마른 지면.
        private bool RoutePointHeight(int moveMode, float x, float z)
        {
            if (moveMode == ACO_EscortUnit.MOVE_SEA)
            {
                if (!IsSeaAt(x, z)) { return false; }
                _sampleY = SeaLevel;
                return true;
            }
            if (moveMode == ACO_EscortUnit.MOVE_GROUND) { return SampleDryGround(x, z); }

            float ground = SeaLevel;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(x, RaycastHeight, z), Vector3.down, out hit, RaycastDistance, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                ground = Mathf.Max(hit.point.y, SeaLevel);
            }
            _sampleY = Mathf.Max(EscortAltitude, ground + EscortTerrainClearance);
            return true;
        }

        /// 항로를 따라 sites개 방공 시설(SAM/대공포 링)을 항로 옆에 놓는다 — 그 자리에 지면이 있을 때만(바다 위 구간은 건너뛴다). 목표 아님(태그 없음).
        private int PlaceRouteSites(int offset, int count, int sites, int now)
        {
            if (sites <= 0 || GroundUnits == null) { return 0; }
            _groundOrder = new int[_groundUsed.Length];
            _groundBudget = CollectFree(GroundUnits, _groundUsed, _groundOrder);
            _groundNext = 0;
            _markTag = 0;
            int placed = 0;
            for (int s = 0; s < sites && _groundNext < _groundBudget; s++)
            {
                // 항로 위 (s+1)/(sites+1) 지점 근처(경유점 사이 직선 보간 — 배치용이라 충분하다)
                float f = (s + 1) / (float)(sites + 1) * (count - 1);
                int k = Mathf.Min(Mathf.FloorToInt(f), count - 2);
                Vector3 a = _escortRoute[offset + k];
                Vector3 b = _escortRoute[offset + k + 1];
                Vector3 onRoute = Vector3.Lerp(a, b, f - k);
                Vector3 dir = b - a;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1f) { dir = Vector3.forward; }
                Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized;
                bool found = false;
                for (int attempt = 0; attempt < 6 && !found; attempt++)
                {
                    float off = Random.Range(EscortSiteOffsetMin, EscortSiteOffsetMax) * (Random.value < 0.5f ? -1f : 1f);
                    float along = Random.Range(-1500f, 1500f);
                    Vector3 c = onRoute + side * off + dir.normalized * along;
                    if (!SampleDryGround(c.x, c.z)) { continue; }
                    found = true;
                    if (PlaceRing(new Vector3(c.x, _sampleY, c.z), EscortSiteSize, DefenseRadius, now) > 0) { placed++; }
                }
            }
            return placed;
        }

        // ───────────── 배치 가능 여부 (미션 관리자가 페이즈를 고르기 전에 확인) ─────────────
        // 동기화된 배치 상태와 정적인 지형만 보므로 어느 클라이언트에서 불러도 된다. 자리 찾기는 실제 배치와 같은 무작위 표본이라
        // "이 근처에 놓을 자리가 있다"는 확인이지 같은 자리를 보장하지는 않는다(실제 배치가 실패하면 관리자가 다른 페이즈로 바꾼다).

        /// 지상 그룹/방공 시설/시한 표적에 쓸 수 있는 지상 유닛 수(쓰지 않던 것 + 파괴된 것, 함선/전차 제외).
        public int CountFreeLandGround()
        {
            EnsureArrays();
            return CollectFree(GroundUnits, _groundUsed, new int[_groundUsed.Length]);
        }

        /// 쓸 수 있는 고가치 표적 수. ships면 함선만, 아니면 육상 표적만.
        public int CountFreeHvt(bool ships)
        {
            EnsureArrays();
            int[] list = new int[_hvtUsed.Length];
            int n = CollectFree(HighValueTargets, _hvtUsed, list);
            int c = 0;
            for (int k = 0; k < n; k++)
            {
                bool isShip = HighValueTargets[list[k]].GetComponent<ACO_Warship>() != null;
                if (isShip == ships) { c++; }
            }
            return c;
        }

        public int CountFreeFighterUnits()
        {
            EnsureArrays();
            return CollectFreeFighters(new int[_fighterUsed.Length]);
        }

        public int CountFreeTankUnits()
        {
            EnsureArrays();
            return CollectFreeTanks(new int[_groundUsed.Length]);
        }

        /// 이동 방식이 moveMode인 호위 대상 중 쓸 수 있는 수.
        /// 쉬는(또는 격추된) 호위 대상 수. moveMode에 ESCORT_HOSTILE을 더하면 적 폭격기를 센다.
        public int CountFreeEscorts(int moveMode)
        {
            EnsureArrays();
            int c = 0;
            if (EscortUnits == null) { return 0; }
            bool hostile = (moveMode & ESCORT_HOSTILE) != 0;
            moveMode &= 0xFF;
            for (int i = 0; i < _escortUsed.Length && i < EscortUnits.Length; i++)
            {
                ACO_EscortUnit u = EscortUnits[i];
                if (u == null || u.MoveMode != moveMode || u.Hostile != hostile) { continue; }
                if (!_escortUsed[i] || IsDestroyed(u.TargetHealth)) { c++; }
            }
            return c;
        }

        /// focus에서 minDist~maxDist 안에 지상 유닛을 놓을 지면이 있는지.
        public bool HasGroundNear(Vector3 focus, float minDist, float maxDist)
        {
            return PickPointNear(focus, minDist, maxDist);
        }

        /// 쓸 수 있는 함선(고가치 표적)을 focus 근처 바다에 함대째 놓을 자리가 있는지.
        public bool HasShipSiteNear(Vector3 focus, float minDist, float maxDist)
        {
            EnsureArrays();
            int[] list = new int[_hvtUsed.Length];
            int n = CollectFree(HighValueTargets, _hvtUsed, list);
            for (int k = 0; k < n; k++)
            {
                ACO_Warship ship = HighValueTargets[list[k]].GetComponent<ACO_Warship>();
                if (ship != null) { return PickSeaNear(focus, minDist, maxDist, FleetRadius(ship)); }
            }
            return false;
        }

        /// 이동 방식 moveMode로 focus 근처에서 길이 routeLength(+ units대 행렬) 항로를 만들 수 있는지.
        /// seed로 무작위를 고정해 확인하므로, 같은 seed로 RequestDeployEscort하면 같은 항로가 만들어진다.
        public bool CanBuildEscortRoute(int moveMode, Vector3 focus, float routeLength, int units, int seed)
        {
            if (_routeScratch == null) { _routeScratch = new Vector3[ACO_EscortUnit.MAX_ROUTE_POINTS]; }
            int count = Mathf.Clamp(EscortRoutePoints, 2, ACO_EscortUnit.MAX_ROUTE_POINTS);
            Random.InitState(seed);
            _routeHostile = (moveMode & ESCORT_HOSTILE) != 0;
            bool ok = BuildEscortRoute(moveMode & 0xFF, focus, routeLength + FormationLength(units, _routeHostile), count, _routeScratch, 0);
            _routeHostile = false;
            ReseedRandom();
            return ok;
        }

        /// 씨앗을 고정해 쓴 뒤 무작위를 다시 흩뜨린다(이후 배치가 매번 같은 순서가 되지 않게).
        private void ReseedRandom()
        {
            Random.InitState(Networking.GetServerTimeInMilliseconds() ^ 0x2545F491);
        }

        /// 마지막 확인(HasGroundNear/HasShipSiteNear)이 찾은 자리 — 관리자가 이 자리를 "정한 자리"로 배치에 넘긴다.
        public Vector3 GetCheckedPoint()
        {
            return new Vector3(_pickX, _sampleY, _pickZ);
        }

        /// 호위 대상 유닛(UI/관리자용).
        public ACO_EscortUnit GetEscort(int i)
        {
            return (EscortUnits != null && i >= 0 && i < EscortUnits.Length) ? EscortUnits[i] : null;
        }

        // ───────────── 페이즈 목표 배치 (오너) ─────────────

        public void DeployObjective(int kind, int tag, int count, int tier, Vector3 focus, float minDist, float maxDist)
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            EnsureArrays();
            int now = Networking.GetServerTimeInMilliseconds();
            _deployNowMs = now;
            _vetChance = VeteranChance;
            _aceChance = AceChance;
            _focusActive = false;
            // 이 태그가 페이즈 목표가 된다. 이전 목표의 파괴된 유닛은 이제 증원/다음 목표에 다시 쓸 수 있다.
            _protectTag = tag;
            count = Mathf.Max(count, 0);

            int placed;
            string label;
            if (kind == OBJ_HVT) { placed = DeployHvts(tag, count, tier, focus, minDist, maxDist, now); label = "목표 배치(고가치 표적)"; }
            else if (kind == OBJ_SITE) { placed = DeploySite(tag, count, focus, minDist, maxDist, now); label = "목표 배치(방공 시설)"; }
            else if (kind == OBJ_GROUND) { placed = DeploySingles(tag, count, focus, minDist, maxDist, now); label = "목표 배치(시한 표적)"; }
            else if (kind == OBJ_HUNT) { placed = DeployHunt(tag, count, focus, minDist, maxDist, now); label = "목표 배치(사냥터)"; }
            else if (kind == OBJ_SUPER_ACE) { placed = DeploySuperAce(tag, count, focus, minDist, maxDist, now); label = "목표 배치(슈퍼에이스)"; }
            else { placed = DeployFlight(tag, count, tier, focus, minDist, maxDist, now); label = "목표 배치(편대)"; }
            _markTag = 0;

            _deployCount++;
            RequestSerialization();
            Debug.Log("[ACO] " + label + " #" + _deployCount + ": 태그 " + tag + ", " + placed + "/" + count);
            ApplyChanges();
        }

        public void ReleaseTag(int tag)
        {
            if (!Networking.IsOwner(gameObject) || tag == 0) { return; }
            EnsureArrays();
            int now = Networking.GetServerTimeInMilliseconds();
            int released = 0;
            _markTag = 0;
            for (int i = 0; i < _groundUsed.Length; i++)
            {
                if (_groundUsed[i] && _groundTag[i] == tag) { MarkGround(i, false, Vector3.zero, Quaternion.identity, now); released++; }
            }
            for (int i = 0; i < _hvtUsed.Length; i++)
            {
                if (_hvtUsed[i] && _hvtTag[i] == tag) { MarkHvt(i, false, Vector3.zero, Quaternion.identity, now); released++; }
            }
            for (int i = 0; i < _escortUsed.Length; i++)
            {
                if (_escortUsed[i] && _escortTag[i] == tag) { MarkEscort(i, false, 0, now); released++; }
            }
            for (int i = 0; i < _fighterUsed.Length; i++)
            {
                if (_fighterUsed[i] && _fighterTag[i] == tag)
                {
                    MarkFighter(i, false, Vector3.zero, Quaternion.identity, Vector3.zero, ACO_NPCFighterAI.SKILL_VETERAN, now);
                    released++;
                }
            }
            if (released == 0) { return; }
            _deployCount++;
            RequestSerialization();
            Debug.Log("[ACO] 태그 " + tag + " 철수: " + released + "대");
            ApplyChanges();
        }

        /// 목표 후보: 쓰지 않던 유닛(섞어서) → 파괴된 유닛(섞어서). 살아 있는 유닛은 제외.
        private int CollectFree(UdonSharpBehaviour[] units, bool[] used, int[] outList)
        {
            int n = 0;
            if (units == null) { return 0; }
            for (int pass = 0; pass < 2; pass++)
            {
                int start = n;
                for (int i = 0; i < used.Length && i < units.Length; i++)
                {
                    UdonSharpBehaviour unit = units[i];
                    if (unit == null) { continue; }
                    if (units == GroundUnits && IsGroundMover(i)) { continue; }
                    bool candidate = pass == 0 ? !used[i] : (used[i] && IsDestroyed(unit.GetComponent<SaccTarget>()));
                    if (candidate) { outList[n] = i; n++; }
                }
                ShuffleRange(outList, start, n);
            }
            return n;
        }

        private int CollectFreeFighters(int[] outList)
        {
            int n = 0;
            if (Fighters == null) { return 0; }
            for (int pass = 0; pass < 2; pass++)
            {
                int start = n;
                for (int i = 0; i < _fighterUsed.Length && i < Fighters.Length; i++)
                {
                    ACO_NPCFighterAI fighter = Fighters[i];
                    if (fighter == null || fighter.SuperAce != null) { continue; }
                    bool candidate = pass == 0 ? !_fighterUsed[i] : (_fighterUsed[i] && IsDestroyed(fighter.TargetHealth));
                    if (candidate) { outList[n] = i; n++; }
                }
                ShuffleRange(outList, start, n);
            }
            return n;
        }

        /// focus에서 minDist~maxDist 떨어진 스폰 가능한 지면을 찾아 _pickX/_pickZ/_sampleY에 넣는다. maxDist <= 0이면 공역 아무 곳.
        /// minDist < 0이면 "정한 자리": focus 바로 그 자리만 본다(관리자가 사전 확인에서 찾은 자리 — 같은 Raycast라 같은 답).
        /// a→b 직선(전차 순찰/매복 이동)이 건물 자리를 지나지 않는지 — 지면을 따라가는 전차가 지붕 위로 올라타지 않게.
        private bool PathClear(Vector3 a, Vector3 b)
        {
            if (Features == null && !RejectBelowSeaLevel) { return true; }
            float len = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int steps = Mathf.Max(Mathf.CeilToInt(len / 150f), 1);
            for (int i = 1; i < steps; i++)
            {
                Vector3 c = Vector3.Lerp(a, b, i / (float)steps);
                if (Features != null && Features.IsBlocked(c.x, c.z)) { return false; }
                // 전차가 지나갈 길이 물에 잠긴 곳을 건너지 않게
                if (IsUnderSea(c.x, c.z)) { return false; }
            }
            return true;
        }

        /// SettlementPreference 확률로 지형 장식 자리(focus에서 minDist~maxDist, maxDist ≤ 0이면 어디든) 둘레 한 점을 _garrisonX/Z에. 지면 확인은 부른 쪽이.
        private bool PickGarrisonPoint(Vector3 focus, float minDist, float maxDist)
        {
            if (Features == null || SettlementPreference <= 0f || !Features.HasSites()) { return false; }
            if (Random.value >= SettlementPreference) { return false; }
            if (!Features.PickSitePoint(focus, minDist, maxDist)) { return false; }
            _garrisonX = Features.GetPickX();
            _garrisonZ = Features.GetPickZ();
            return true;
        }

        /// pos 근처(가장자리까지 maxDist 안) 지형 장식 자리 번호 — 페이즈 작전 지역 이름용. 없으면 -1.
        public int FindSiteNear(Vector3 pos, float maxDist)
        {
            return Features != null ? Features.FindSiteNear(pos.x, pos.z, maxDist) : -1;
        }

        public string GetSiteName(int site)
        {
            return Features != null ? Features.GetSiteName(site) : "";
        }

        private bool PickPointNear(Vector3 focus, float minDist, float maxDist)
        {
            if (minDist < 0f)
            {
                if (!SampleGround(focus.x, focus.z)) { return false; }
                _pickX = focus.x;
                _pickZ = focus.z;
                return true;
            }
            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                float x;
                float z;
                if (PickGarrisonPoint(focus, minDist, maxDist))
                {
                    // 페이즈 목표도 도시/마을/비행장/레이더 둘레를 먼저 노린다(작전 지역 이름이 붙는다)
                    x = _garrisonX;
                    z = _garrisonZ;
                }
                else if (maxDist > 0f)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    // 면적이 고르게 퍼지도록 반지름 제곱을 균일하게 뽑는다.
                    float lo = Mathf.Max(minDist, 0f);
                    float hi = Mathf.Max(maxDist, lo);
                    float d = Mathf.Sqrt(Random.Range(lo * lo, hi * hi));
                    x = focus.x + Mathf.Cos(a) * d;
                    z = focus.z + Mathf.Sin(a) * d;
                }
                else
                {
                    x = Random.Range(AreaMin.x, AreaMax.x);
                    z = Random.Range(AreaMin.y, AreaMax.y);
                }
                if (!SampleGround(x, z)) { continue; }
                _pickX = x;
                _pickZ = z;
                return true;
            }
            return false;
        }

        /// variant: 정한 자리 모드에서 첫 표적의 종류(0 = 육상, 1 = 함선).
        private int DeployHvts(int tag, int count, int variant, Vector3 focus, float minDist, float maxDist, int now)
        {
            int[] hvts = new int[_hvtUsed.Length];
            int hvtCount = CollectFree(HighValueTargets, _hvtUsed, hvts);
            _groundOrder = new int[_groundUsed.Length];
            _groundBudget = CollectFree(GroundUnits, _groundUsed, _groundOrder);
            _groundNext = 0;

            int target = Mathf.Min(count, hvtCount);
            Vector3[] placedPos = new Vector3[target];
            int placed = 0;
            // 이번에 놓는 표적끼리 떨어뜨린다. 근거리 배치면 범위의 절반까지만 요구한다(너무 멀면 자리를 못 찾는다).
            float sepSq = HighValueMinSeparation * HighValueMinSeparation;
            if (maxDist > 0f) { sepSq = Mathf.Min(sepSq, maxDist * maxDist * 0.25f); }
            // 정한 자리(minDist < 0): 첫 표적은 그 자리에(variant 0 = 육상 표적, 1 = 함선), 나머지는 그 근처에.
            bool exact = minDist < 0f;
            for (int c = 0; c < hvtCount && placed < target; c++)
            {
                int idx = hvts[c];
                ACO_Warship ship = HighValueTargets[idx] != null ? HighValueTargets[idx].GetComponent<ACO_Warship>() : null;
                if (exact && placed == 0 && (ship != null) != (variant == 1)) { continue; }
                float lo = minDist;
                float hi = maxDist;
                if (exact && placed > 0) { lo = HighValueMinSeparation * 0.5f; hi = HighValueMinSeparation * 1.5f; }
                bool found = false;
                for (int attempt = 0; attempt < 8 && !found; attempt++)
                {
                    bool ok = ship != null ? PickSeaNear(focus, lo, hi, FleetRadius(ship)) : PickPointNear(focus, lo, hi);
                    if (!ok) { break; }
                    found = true;
                    for (int k = 0; k < placed; k++)
                    {
                        float dx = placedPos[k].x - _pickX;
                        float dz = placedPos[k].z - _pickZ;
                        if (dx * dx + dz * dz < sepSq) { found = false; break; }
                    }
                }
                // 이 표적은 놓을 자리가 없다(근처에 바다가 없으면 함선) — 다음 후보로.
                if (!found) { continue; }

                Vector3 center = new Vector3(_pickX, _sampleY, _pickZ);
                _markTag = tag;
                MarkHvt(idx, true, center, HvtRotation(ship != null), now);
                placedPos[placed] = center;
                placed++;
                // 방공 호위 링/호위함은 목표가 아니다(태그 없음). 함선은 링 대신 호위 구축함을 붙인다.
                _markTag = 0;
                if (ship != null) { PlaceEscorts(LEADER_HVT + idx, center, YawRot(_hvtYaw[idx]), EscortCount, now); }
                if (ship == null && Random.value < DefenseChance) { PlaceDefenseRing(center, now); }
            }
            return placed;
        }

        private int DeploySite(int tag, int count, Vector3 focus, float minDist, float maxDist, int now)
        {
            _groundOrder = new int[_groundUsed.Length];
            _groundBudget = CollectFree(GroundUnits, _groundUsed, _groundOrder);
            _groundNext = 0;
            if (!PickPointNear(focus, minDist, maxDist)) { return 0; }
            _markTag = tag;
            return PlaceRing(new Vector3(_pickX, _sampleY, _pickZ), count, DefenseRadius * SiteRadiusScale, now);
        }

        private int DeploySingles(int tag, int count, Vector3 focus, float minDist, float maxDist, int now)
        {
            int[] cands = new int[_groundUsed.Length];
            int candCount = CollectFree(GroundUnits, _groundUsed, cands);
            int target = Mathf.Min(count, candCount);
            int placed = 0;
            _markTag = tag;
            for (int k = 0; k < target; k++)
            {
                if (!PickPointNear(focus, minDist, maxDist)) { break; }
                MarkGround(cands[k], true, new Vector3(_pickX, _sampleY, _pickZ), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), now);
                placed++;
            }
            return placed;
        }

        private int DeployHunt(int tag, int count, Vector3 focus, float minDist, float maxDist, int now)
        {
            int[] cands = new int[_groundUsed.Length];
            int candCount = CollectFree(GroundUnits, _groundUsed, cands);
            if (!PickPointNear(focus, minDist, maxDist)) { return 0; }
            // 찾은 지점 둘레(FocusGroundRadius)에 그룹들을 놓는다.
            _focusActive = true;
            _focusPos = new Vector3(_pickX, _sampleY, _pickZ);
            _markTag = tag;
            PlaceGround(cands, 0, Mathf.Min(count, candCount), now);
            _focusActive = false;
            return _lastPlaced;
        }

        /// 일반 배치에 쓸 전투기 번호(슈퍼에이스 제외), 섞어서.
        private int[] RegularFighterOrder()
        {
            int count = 0;
            for (int i = 0; i < _fighterUsed.Length; i++) { if (!IsSuperAceSlot(i)) { count++; } }
            int[] order = new int[count];
            int n = 0;
            for (int i = 0; i < _fighterUsed.Length; i++) { if (!IsSuperAceSlot(i)) { order[n] = i; n++; } }
            ShuffleRange(order, 0, count);
            return order;
        }

        private bool IsSuperAceSlot(int i)
        {
            return Fighters != null && i < Fighters.Length && Fighters[i] != null && Fighters[i].SuperAce != null;
        }

        /// 내보낼 수 있는 슈퍼에이스 번호(쓰지 않거나 격추된 것), 없으면 -1.
        private int FreeSuperAce()
        {
            if (Fighters == null) { return -1; }
            for (int i = 0; i < _fighterUsed.Length && i < Fighters.Length; i++)
            {
                if (!IsSuperAceSlot(i)) { continue; }
                if (!_fighterUsed[i] || IsDestroyed(Fighters[i].TargetHealth)) { return i; }
            }
            return -1;
        }

        /// 보너스 페이즈를 열 수 있는가(풀에 쉬고 있는 슈퍼에이스가 있다). 미션 관리자 오너가 굴리기 전에 본다.
        public bool CanDeploySuperAce()
        {
            return FreeSuperAce() >= 0;
        }

        /// 슈퍼에이스 이름(풀의 첫 슈퍼에이스). 없으면 "ACE".
        public string GetSuperAceName()
        {
            if (Fighters == null) { return "ACE"; }
            for (int i = 0; i < Fighters.Length; i++)
            {
                if (Fighters[i] != null && Fighters[i].SuperAce != null) { return Fighters[i].SuperAce.AceName; }
            }
            return "ACE";
        }

        /// 슈퍼에이스(태그) + 호위 에이스 escorts기(태그 없음 — 목표는 슈퍼에이스 하나). focus에서 minDist~maxDist 떨어진 곳에서 focus 상공으로 진입.
        private int DeploySuperAce(int tag, int escorts, Vector3 focus, float minDist, float maxDist, int now)
        {
            int ace = FreeSuperAce();
            if (ace < 0) { return 0; }
            Vector3 spawn;
            if (maxDist > 0f)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float d = Random.Range(Mathf.Max(minDist, 0f), Mathf.Max(maxDist, minDist));
                spawn = new Vector3(focus.x + Mathf.Cos(a) * d, 0f, focus.z + Mathf.Sin(a) * d);
            }
            else { spawn = RandomPointOutsideArea(); }
            spawn.y = Random.Range(SpawnAltitudeMin, SpawnAltitudeMax);
            float taskY = focus.y >= SpawnAltitudeMin * 0.5f ? Mathf.Clamp(focus.y, SpawnAltitudeMin * 0.5f, SpawnAltitudeMax) : Random.Range(SpawnAltitudeMin, SpawnAltitudeMax);
            Vector3 task = new Vector3(focus.x, taskY, focus.z);
            Vector3 flat = task - spawn;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1f) { flat = Vector3.forward; }
            Quaternion heading = Quaternion.LookRotation(flat.normalized, Vector3.up);

            _markTag = tag;
            MarkFighter(ace, true, spawn, heading, task, ACO_NPCFighterAI.SKILL_ACE, now);
            int placed = 1;
            _markTag = 0;
            int[] cands = new int[_fighterUsed.Length];
            int candCount = CollectFreeFighters(cands);
            int n = Mathf.Min(Mathf.Max(escorts, 0), candCount);
            for (int k = 0; k < n; k++)
            {
                MarkFighter(cands[k], true, spawn + heading * FormationOffset(k + 1), heading, task, ACO_NPCFighterAI.SKILL_ACE, now);
                placed++;
            }
            return placed;
        }

        private int DeployFlight(int tag, int count, int tier, Vector3 focus, float minDist, float maxDist, int now)
        {
            int[] cands = new int[_fighterUsed.Length];
            int candCount = CollectFreeFighters(cands);
            int budget = Mathf.Min(count, candCount);
            int next = 0;
            _markTag = tag;

            while (next < budget)
            {
                // 숙련도를 고정한 편대(보스)는 4기 편대로 묶는다.
                int size = (tier >= 0 || Random.value < FourShipChance) ? 4 : 2;
                size = Mathf.Min(size, budget - next);

                Vector3 spawn;
                if (maxDist > 0f)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    float d = Random.Range(Mathf.Max(minDist, 0f), Mathf.Max(maxDist, minDist));
                    spawn = new Vector3(focus.x + Mathf.Cos(a) * d, 0f, focus.z + Mathf.Sin(a) * d);
                }
                else { spawn = RandomPointOutsideArea(); }
                spawn.y = Random.Range(SpawnAltitudeMin, SpawnAltitudeMax);
                // 편대는 focus(플레이어들/호위 항로) 상공으로 온다. focus가 공중이면 그 고도로(탐지 거리 안에서 만나게).
                float taskY = focus.y >= SpawnAltitudeMin * 0.5f ? Mathf.Clamp(focus.y, SpawnAltitudeMin * 0.5f, SpawnAltitudeMax) : Random.Range(SpawnAltitudeMin, SpawnAltitudeMax);
                Vector3 task = new Vector3(focus.x, taskY, focus.z);
                int flightTier = tier >= 0 ? Mathf.Clamp(tier, 0, 2) : RollTier();

                Vector3 flat = task - spawn;
                flat.y = 0f;
                if (flat.sqrMagnitude < 1f) { flat = Vector3.forward; }
                Quaternion heading = Quaternion.LookRotation(flat.normalized, Vector3.up);
                for (int k = 0; k < size; k++)
                {
                    MarkFighter(cands[next + k], true, spawn + heading * FormationOffset(k), heading, task, flightTier, now);
                }
                next += size;
            }
            return next;
        }

        // ───────────── 조회 (미션 관리자/UI/점수용, 모든 클라이언트에서 같은 값) ─────────────

        /// 배치돼 있고 살아 있는 적(전투기 + 지상, 고가치 표적 제외).
        public int CountAliveEnemies()
        {
            int n = 0;
            for (int i = 0; i < _groundUsed.Length; i++)
            {
                if (_groundUsed[i] && GroundUnits[i] != null && !IsDestroyed(GroundUnits[i].GetComponent<SaccTarget>())) { n++; }
            }
            for (int i = 0; i < _fighterUsed.Length; i++)
            {
                if (_fighterUsed[i] && Fighters[i] != null && !IsDestroyed(Fighters[i].TargetHealth)) { n++; }
            }
            return n;
        }

        /// 배치된 적 수(전투기 + 지상, 생사 무관).
        public int CountDeployedEnemies()
        {
            int n = 0;
            for (int i = 0; i < _groundUsed.Length; i++) { if (_groundUsed[i]) { n++; } }
            for (int i = 0; i < _fighterUsed.Length; i++) { if (_fighterUsed[i]) { n++; } }
            return n;
        }

        /// 전체 배치(미션 시작) 횟수. 생성기 자동 시작/클릭/관리자 요청 중 무엇으로 시작했든 오른다.
        public int GetFullDeployCount() { return _fullDeployCount; }

        // 유닛별 조회(점수 계산용). 카테고리: 0 = 전투기, 1 = 지상 유닛, 2 = 고가치 표적.
        // serial은 그 유닛이 다시 배치될 때마다 오르므로, "같은 serial 동안 살아 있다가 파괴됨"으로 격추를 한 번만 셀 수 있다.
        public const int CAT_FIGHTER = 0;
        public const int CAT_GROUND = 1;
        public const int CAT_HVT = 2;
        /// 함선 부위(VLS/함포/CIWS). 배치·배치 번호는 소속 함선(고가치 표적)을 따르고, 생사는 부위 자신의 SaccTarget.
        public const int CAT_PART = 3;
        /// 호위 대상(아군). 점수/적 전력에는 들어가지 않는다.
        public const int CAT_ESCORT = 4;

        public int GetUnitCount(int category)
        {
            if (category == CAT_FIGHTER) { return _fighterUsed.Length; }
            if (category == CAT_GROUND) { return _groundUsed.Length; }
            if (category == CAT_PART) { EnsureParts(); return _parts.Length; }
            if (category == CAT_ESCORT) { return _escortUsed.Length; }
            return _hvtUsed.Length;
        }

        public bool IsUnitUsed(int category, int i)
        {
            if (category == CAT_FIGHTER) { return i < _fighterUsed.Length && _fighterUsed[i]; }
            if (category == CAT_GROUND) { return i < _groundUsed.Length && _groundUsed[i]; }
            if (category == CAT_PART) { int h = PartOwner(i); return h >= 0 && IsUnitUsed(PartOwnerCategory(i), h); }
            if (category == CAT_ESCORT) { return i >= 0 && i < _escortUsed.Length && _escortUsed[i]; }
            return i < _hvtUsed.Length && _hvtUsed[i];
        }

        public int GetUnitSerial(int category, int i)
        {
            if (category == CAT_FIGHTER) { return i < _fighterSerial.Length ? _fighterSerial[i] : -1; }
            if (category == CAT_GROUND) { return i < _groundSerial.Length ? _groundSerial[i] : -1; }
            if (category == CAT_PART) { int h = PartOwner(i); return h >= 0 ? GetUnitSerial(PartOwnerCategory(i), h) : -1; }
            if (category == CAT_ESCORT) { return (i >= 0 && i < _escortSerial.Length) ? _escortSerial[i] : -1; }
            return i < _hvtSerial.Length ? _hvtSerial[i] : -1;
        }

        public bool IsUnitDestroyed(int category, int i)
        {
            if (category == CAT_FIGHTER)
            {
                return Fighters != null && i < Fighters.Length && Fighters[i] != null && IsDestroyed(Fighters[i].TargetHealth);
            }
            UdonSharpBehaviour unit = UnitAt(category, i);
            return unit != null && IsDestroyed(unit.GetComponent<SaccTarget>());
        }

        /// 페이즈 목표 태그(0 = 일반 유닛).
        public int GetUnitTag(int category, int i)
        {
            if (category == CAT_FIGHTER) { return i < _fighterTag.Length ? _fighterTag[i] : 0; }
            if (category == CAT_GROUND) { return i < _groundTag.Length ? _groundTag[i] : 0; }
            if (category == CAT_PART) { return 0; }
            if (category == CAT_ESCORT) { return (i >= 0 && i < _escortTag.Length) ? _escortTag[i] : 0; }
            return i < _hvtTag.Length ? _hvtTag[i] : 0;
        }

        /// 마지막으로 배치된 서버 시각(ms).
        public int GetUnitDeployTimeMs(int category, int i)
        {
            if (category == CAT_FIGHTER) { return i < _fighterTimeMs.Length ? _fighterTimeMs[i] : 0; }
            if (category == CAT_GROUND) { return i < _groundTimeMs.Length ? _groundTimeMs[i] : 0; }
            if (category == CAT_PART) { int h = PartOwner(i); return h >= 0 ? GetUnitDeployTimeMs(PartOwnerCategory(i), h) : 0; }
            if (category == CAT_ESCORT) { return (i >= 0 && i < _escortTimeMs.Length) ? _escortTimeMs[i] : 0; }
            return i < _hvtTimeMs.Length ? _hvtTimeMs[i] : 0;
        }

        /// 현재 위치. 전투기는 실제 기체 위치, 지상/표적은 배치 위치.
        public Vector3 GetUnitPosition(int category, int i)
        {
            if (category == CAT_FIGHTER)
            {
                return (Fighters != null && i < Fighters.Length && Fighters[i] != null) ? Fighters[i].transform.position : Vector3.zero;
            }
            if (category == CAT_GROUND)
            {
                if (IsGroundMover(i)) { return GroundUnits[i].transform.position; }
                return i < _groundPos.Length ? _groundPos[i] : Vector3.zero;
            }
            if (category == CAT_PART || category == CAT_ESCORT) { UdonSharpBehaviour u = UnitAt(category, i); return u != null ? u.transform.position : Vector3.zero; }
            // 함선은 순항하므로 배치 위치(원의 중심)가 아니라 실제 위치
            UdonSharpBehaviour hvt = UnitAt(CAT_HVT, i);
            if (hvt != null && hvt.GetComponent<ACO_Warship>() != null) { return hvt.transform.position; }
            return i < _hvtPos.Length ? _hvtPos[i] : Vector3.zero;
        }

        /// 전투기 숙련도(0 신병, 1 베테랑, 2 에이스).
        public int GetFighterTier(int i)
        {
            return i < _fighterTier.Length ? _fighterTier[i] : ACO_NPCFighterAI.SKILL_VETERAN;
        }

        /// 유닛 오브젝트(지상/고가치 표적/부위). 전투기는 Fighters 배열을 쓴다.
        public UdonSharpBehaviour GetUnitBehaviour(int category, int i)
        {
            return UnitAt(category, i);
        }

        /// 지상 유닛/고가치 표적/부위의 ScoreValue 필드(없으면 fallback).
        public int GetUnitScoreValue(int category, int i, int fallback)
        {
            UdonSharpBehaviour unit = UnitAt(category, i);
            if (unit == null) { return fallback; }
            object value = unit.GetProgramVariable("ScoreValue");
            return value != null ? (int)value : fallback;
        }

        /// 함선이 격침돼 같이 가라앉는 부위 — 살아 있지만 표시/교전 대상이 아니다(점수도 없다).
        public bool IsUnitSuppressed(int category, int i)
        {
            if (category != CAT_PART) { return false; }
            int h = PartOwner(i);
            UdonSharpBehaviour ship = UnitAt(PartOwnerCategory(i), h);
            return ship != null && IsDestroyed(ship.GetComponent<SaccTarget>());
        }

        /// 부위 i가 속한 함선의 인덱스(-1 = 없음). 카테고리는 PartOwnerCategory(전함 = CAT_HVT, 구축함 = CAT_GROUND).
        public int PartOwner(int i)
        {
            EnsureParts();
            return (i >= 0 && i < _partOwner.Length) ? _partOwner[i] : -1;
        }

        public int PartOwnerCategory(int i)
        {
            EnsureParts();
            return (i >= 0 && i < _partOwnerCat.Length) ? _partOwnerCat[i] : CAT_HVT;
        }

        /// 함선 부위 목록을 만든다: 고가치 표적 풀의 함선(전함) + 지상 풀의 함선(구축함).
        private void EnsureParts()
        {
            if (_partsBuilt) { return; }
            _partsBuilt = true;
            int total = CountParts(HighValueTargets) + CountParts(GroundUnits);
            _parts = new UdonSharpBehaviour[total];
            _partOwner = new int[total];
            _partOwnerCat = new int[total];
            int n = AddParts(HighValueTargets, CAT_HVT, 0);
            AddParts(GroundUnits, CAT_GROUND, n);
        }

        private int CountParts(UdonSharpBehaviour[] units)
        {
            int total = 0;
            if (units == null) { return 0; }
            for (int h = 0; h < units.Length; h++)
            {
                ACO_Warship ship = units[h] != null ? units[h].GetComponent<ACO_Warship>() : null;
                if (ship == null || ship.Parts == null) { continue; }
                for (int k = 0; k < ship.Parts.Length; k++) { if (ship.Parts[k] != null) { total++; } }
            }
            return total;
        }

        private int AddParts(UdonSharpBehaviour[] units, int category, int n)
        {
            if (units == null) { return n; }
            for (int h = 0; h < units.Length; h++)
            {
                ACO_Warship ship = units[h] != null ? units[h].GetComponent<ACO_Warship>() : null;
                if (ship == null || ship.Parts == null) { continue; }
                for (int k = 0; k < ship.Parts.Length; k++)
                {
                    if (ship.Parts[k] == null) { continue; }
                    _parts[n] = ship.Parts[k];
                    _partOwner[n] = h;
                    _partOwnerCat[n] = category;
                    n++;
                }
            }
            return n;
        }

        private UdonSharpBehaviour UnitAt(int category, int i)
        {
            if (category == CAT_PART)
            {
                EnsureParts();
                return (i >= 0 && i < _parts.Length) ? _parts[i] : null;
            }
            if (category == CAT_ESCORT) { return GetEscort(i); }
            UdonSharpBehaviour[] list = category == CAT_GROUND ? GroundUnits : HighValueTargets;
            if (list == null || i < 0 || i >= list.Length) { return null; }
            return list[i];
        }

        /// 핑거팁 대형(편대장 기준 로컬 오프셋). 2기: 편대장 + 왼쪽 뒤. 4기: 편대장, 왼쪽 뒤, 오른쪽 뒤, 오른쪽 더 뒤.
        private Vector3 FormationOffset(int slot)
        {
            float s = FormationSpacing;
            if (slot == 1) { return new Vector3(-s, 0f, -s); }
            if (slot == 2) { return new Vector3(s, 0f, -s); }
            if (slot == 3) { return new Vector3(s * 2f, 0f, -s * 2f); }
            return Vector3.zero;
        }

        /// 공역 사각형 경계의 무작위 지점에서 바깥쪽으로 SpawnOutsideMin~Max만큼 떨어진 곳.
        private Vector3 RandomPointOutsideArea()
        {
            int side = Random.Range(0, 4);
            float t = Random.value;
            float push = Random.Range(SpawnOutsideMin, SpawnOutsideMax);
            float x;
            float z;
            if (side == 0) { x = Mathf.Lerp(AreaMin.x, AreaMax.x, t); z = AreaMin.y - push; }
            else if (side == 1) { x = Mathf.Lerp(AreaMin.x, AreaMax.x, t); z = AreaMax.y + push; }
            else if (side == 2) { x = AreaMin.x - push; z = Mathf.Lerp(AreaMin.y, AreaMax.y, t); }
            else { x = AreaMax.x + push; z = Mathf.Lerp(AreaMin.y, AreaMax.y, t); }
            return new Vector3(x, 0f, z);
        }

        private Vector3 RandomIngressPoint()
        {
            float minX = AreaMin.x + IngressInset;
            float maxX = AreaMax.x - IngressInset;
            float minZ = AreaMin.y + IngressInset;
            float maxZ = AreaMax.y - IngressInset;
            // 안쪽 범위가 뒤집히면(공역이 작으면) 중앙으로 모은다.
            if (minX > maxX) { minX = (AreaMin.x + AreaMax.x) * 0.5f; maxX = minX; }
            if (minZ > maxZ) { minZ = (AreaMin.y + AreaMax.y) * 0.5f; maxZ = minZ; }
            return new Vector3(Random.Range(minX, maxX), Random.Range(SpawnAltitudeMin, SpawnAltitudeMax), Random.Range(minZ, maxZ));
        }

        private int[] Shuffled(int count)
        {
            int[] order = new int[count];
            for (int i = 0; i < count; i++) { order[i] = i; }
            ShuffleRange(order, 0, count);
            return order;
        }

        private void ShuffleRange(int[] list, int start, int end)
        {
            for (int i = end - 1; i > start; i--)
            {
                int j = Random.Range(start, i + 1);
                int tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        // ───────────── 적용 (모든 클라이언트) ─────────────

        public override void OnDeserialization()
        {
            MarkJoinTime();
            if (_applyQueued) { return; }
            _applyQueued = true;
            // 늦게 들어온 경우 유닛들의 Start가 아직일 수 있어 몇 프레임 뒤에 적용한다.
            SendCustomEventDelayedFrames(nameof(ApplyQueued), 2);
        }

        public void ApplyQueued()
        {
            _applyQueued = false;
            ApplyChanges();
        }

        /// 아직 아무 배치도 적용하지 않은 풀 유닛을 숨긴다(월드 시작 시). 이미 배치를 받은 유닛은 건드리지 않는다.
        public void HidePoolIfIdle()
        {
            HideIdle(GroundUnits, _appliedGroundSerial);
            HideIdle(HighValueTargets, _appliedHvtSerial);
            if (EscortUnits != null)
            {
                for (int i = 0; i < EscortUnits.Length; i++)
                {
                    ACO_EscortUnit unit = EscortUnits[i];
                    if (unit == null || (i < _appliedEscortSerial.Length && _appliedEscortSerial[i] != -1)) { continue; }
                    unit.MissionDespawn();
                    unit.gameObject.SetActive(false);
                }
            }
            if (Fighters == null) { return; }
            for (int i = 0; i < Fighters.Length; i++)
            {
                ACO_NPCFighterAI fighter = Fighters[i];
                if (fighter == null || (i < _appliedFighterSerial.Length && _appliedFighterSerial[i] != -1)) { continue; }
                fighter.MissionDespawn();
                fighter.gameObject.SetActive(false);
            }
        }

        private void HideIdle(UdonSharpBehaviour[] units, int[] applied)
        {
            if (units == null) { return; }
            for (int i = 0; i < units.Length; i++)
            {
                UdonSharpBehaviour unit = units[i];
                if (unit == null || (i < applied.Length && applied[i] != -1)) { continue; }
                unit.SendCustomEvent("MissionDespawn");
                unit.gameObject.SetActive(false);
            }
        }

        /// serial이 바뀐 유닛만 적용한다. 이 클라이언트가 들어오기 전에 배치된 유닛은 위치만 맞추고 부활시키지 않는다 —
        /// 그 사이 격추된 유닛은 SaccTarget의 체력 동기화가 격추 상태로 맞춘다. int 뺄셈이라 서버 시간 오버플로에도 맞다.
        /// 바뀐 유닛을 적용한다 — 한 번에 ApplyPerFrame대까지, 나머지는 다음 프레임에(큰 배치/늦게 들어온 클라이언트의 순간 끊김을 나눈다).
        private void ApplyChanges()
        {
            _applyBudget = Mathf.Max(ApplyPerFrame, 1);
            _applyMore = false;
            int groundCount = GroundUnits != null ? Mathf.Min(GroundUnits.Length, _groundUsed.Length) : 0;
            int hvtCount = HighValueTargets != null ? Mathf.Min(HighValueTargets.Length, _hvtUsed.Length) : 0;
            int fighterCount = Fighters != null ? Mathf.Min(Fighters.Length, _fighterUsed.Length) : 0;
            _appliedGroundSerial = GrowApplied(_appliedGroundSerial, groundCount);
            _appliedHvtSerial = GrowApplied(_appliedHvtSerial, hvtCount);
            _appliedFighterSerial = GrowApplied(_appliedFighterSerial, fighterCount);

            ApplyGroundLike(GroundUnits, groundCount, _groundUsed, _groundPos, _groundYaw, _groundSerial, _groundTimeMs, _appliedGroundSerial, _groundLeader);
            ApplyGroundLike(HighValueTargets, hvtCount, _hvtUsed, _hvtPos, _hvtYaw, _hvtSerial, _hvtTimeMs, _appliedHvtSerial, null);
            ApplyEscorts();

            for (int i = 0; i < fighterCount; i++)
            {
                if (_appliedFighterSerial[i] == _fighterSerial[i]) { continue; }
                if (!TakeApplyBudget()) { continue; }
                _appliedFighterSerial[i] = _fighterSerial[i];

                ACO_NPCFighterAI fighter = Fighters[i];
                if (fighter == null) { continue; }

                if (_fighterUsed[i])
                {
                    fighter.gameObject.SetActive(true);
                    int tier = i < _fighterTier.Length ? Mathf.Clamp(_fighterTier[i], 0, 2) : ACO_NPCFighterAI.SKILL_VETERAN;
                    fighter.ApplySkill(tier, TierValue(TierTurnGMultiplier, tier), TierValue(TierSpeedMultiplier, tier),
                        TierValue(TierLockTimeMultiplier, tier), TierValue(TierGunDwellMultiplier, tier), TierValue(TierMissileConeMultiplier, tier));
                    fighter.MissionSpawn(_fighterPos[i], YawRot(_fighterYaw[i]), _fighterIngress[i], _fighterTimeMs[i] - _joinTimeMs > 0);
                }
                else
                {
                    fighter.MissionDespawn();
                    fighter.gameObject.SetActive(false);
                }
            }

            if (_applyMore && !_applyContinueQueued)
            {
                _applyContinueQueued = true;
                SendCustomEventDelayedFrames(nameof(ApplyContinue), 1);
            }
        }

        public void ApplyContinue()
        {
            _applyContinueQueued = false;
            ApplyChanges();
        }

        /// 이번 프레임 적용 예산을 하나 쓴다. 다 썼으면 false(다음 프레임에 이어서).
        private bool TakeApplyBudget()
        {
            if (_applyBudget <= 0) { _applyMore = true; return false; }
            _applyBudget--;
            return true;
        }

        /// 방위 → 0~65535. 0.0055° 단위라 함선의 "정수+0.5도" 방위도 정수 부분이 그대로 남는다.
        private ushort YawCode(Quaternion rot)
        {
            int c = Mathf.RoundToInt(rot.eulerAngles.y * (65536f / 360f)) & 0xFFFF;
            return (ushort)c;
        }

        private Quaternion YawRot(ushort code)
        {
            return Quaternion.Euler(0f, code * (360f / 65536f), 0f);
        }

        /// 지상 유닛과 고가치 표적은 같은 계약(필드 + MissionSpawn/MissionDespawn 이벤트)이라 같은 방식으로 적용한다.
        /// 같은 태그 행렬(콘보이)의 선두가 앞선 거리 — 각 유닛은 선두보다 (선두 - 자기) 만큼 뒤에 멈춘다.
        private float ColumnLeadOffset(int tag)
        {
            float lead = 0f;
            for (int j = 0; j < _escortUsed.Length && j < _escortOffset.Length; j++)
            {
                if (_escortUsed[j] && _escortTag[j] == tag && _escortOffset[j] > lead) { lead = _escortOffset[j]; }
            }
            return lead;
        }

        private void ApplyEscorts()
        {
            int count = EscortUnits != null ? Mathf.Min(EscortUnits.Length, _escortUsed.Length) : 0;
            _appliedEscortSerial = GrowApplied(_appliedEscortSerial, count);
            for (int i = 0; i < count; i++)
            {
                if (_appliedEscortSerial[i] == _escortSerial[i]) { continue; }
                if (!TakeApplyBudget()) { continue; }
                _appliedEscortSerial[i] = _escortSerial[i];
                ACO_EscortUnit unit = EscortUnits[i];
                if (unit == null) { continue; }
                if (_escortUsed[i])
                {
                    unit.gameObject.SetActive(true);
                    float own = i < _escortOffset.Length ? (float)_escortOffset[i] : 0f;
                    unit.MissionStart(_escortRoute, i * ACO_EscortUnit.MAX_ROUTE_POINTS, _escortRouteCount[i], _escortTimeMs[i], _escortTimeMs[i] - _joinTimeMs > 0,
                        own, ColumnLeadOffset(_escortTag[i]) - own, i < _escortArrival.Length ? _escortArrival[i] : 0,
                        i < _escortLateral.Length ? (float)_escortLateral[i] : 0f);
                }
                else
                {
                    unit.MissionDespawn();
                    unit.gameObject.SetActive(false);
                }
            }
        }

        /// leader: 지상 풀 함선의 편대 정보(고가치 표적은 null).
        private void ApplyGroundLike(UdonSharpBehaviour[] units, int count, bool[] used, Vector3[] pos, ushort[] yaw,
            byte[] serial, int[] timeMs, int[] applied, short[] leader)
        {
            for (int i = 0; i < count; i++)
            {
                if (applied[i] == serial[i]) { continue; }
                if (!TakeApplyBudget()) { continue; }
                applied[i] = serial[i];

                UdonSharpBehaviour unit = units[i];
                if (unit == null) { continue; }

                if (used[i])
                {
                    unit.gameObject.SetActive(true);
                    unit.SetProgramVariable("MissionSpawnPosition", pos[i]);
                    unit.SetProgramVariable("MissionSpawnRotation", YawRot(yaw[i]));
                    unit.SetProgramVariable("MissionRevive", timeMs[i] - _joinTimeMs > 0);
                    if (units == GroundUnits && IsGroundTank(i))
                    {
                        ACO_Tank tank = unit.GetComponent<ACO_Tank>();
                        int slot = _tankSlot[i];
                        if (tank != null && slot < _tankRouteCount.Length)
                        {
                            // 수평 좌표만 받았으니 높이 0으로 펼쳐 넘긴다(전차가 지면을 재서 높이를 맞춘다).
                            if (_tankRouteTmp == null) { _tankRouteTmp = new Vector3[ACO_Tank.MAX_ROUTE_POINTS]; }
                            int baseIdx = slot * ACO_Tank.MAX_ROUTE_POINTS;
                            for (int k = 0; k < ACO_Tank.MAX_ROUTE_POINTS; k++)
                            {
                                Vector2 q = _tankRoute[baseIdx + k];
                                _tankRouteTmp[k] = new Vector3(q.x, 0f, q.y);
                            }
                            tank.SetRoute(_tankRouteTmp, 0, _tankRouteCount[slot], _tankLoop[slot], _tankOffset[slot]);
                            tank.SetStartTime(timeMs[i]);
                        }
                    }
                    if (leader != null && i < leader.Length)
                    {
                        ACO_Warship ship = unit.GetComponent<ACO_Warship>();
                        if (ship != null)
                        {
                            int code = leader[i];
                            ship.MissionLeader = code >= 0 ? ResolveLeader(code / 16) : null;
                            ship.MissionSlot = code >= 0 ? code % 16 : -1;
                        }
                    }
                    unit.SendCustomEvent("MissionSpawn");
                }
                else
                {
                    unit.SendCustomEvent("MissionDespawn");
                    unit.gameObject.SetActive(false);
                }
            }
        }

        private float TierValue(float[] table, int tier)
        {
            if (table == null || tier < 0 || tier >= table.Length) { return 1f; }
            return table[tier];
        }

        /// 적용 기록 배열을 늘린다. 새 칸은 -1(아직 적용 안 함)이라 처음 받은 배치는 전부 적용된다.
        private int[] GrowApplied(int[] applied, int count)
        {
            if (applied.Length == count) { return applied; }
            int[] grown = new int[count];
            for (int i = 0; i < count; i++) { grown[i] = i < applied.Length ? applied[i] : -1; }
            return grown;
        }
    }
}
