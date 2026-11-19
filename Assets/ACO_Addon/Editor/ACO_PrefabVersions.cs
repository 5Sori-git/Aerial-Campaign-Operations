using System;
using UnityEditor;
using UnityEngine;

/// 패널 프리팹의 판 번호. 패널을 고칠 때는 씬이나 기존 프리팹을 패치하지 않는다.
/// 생성 코드를 고치고 Entries의 Version을 올리면 Setup Check가 구버전이라고 알려 주고, [고치기]가 프리팹을 다시 만든다.
/// 판 번호는 프리팹 .meta의 userData에 있다. 없으면 1이다. 프리팹 생성 함수가 Stamp로 적는다.
public static class ACO_PrefabVersions
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";

    public class Entry
    {
        public string Name;
        public int Version;
        public string Change;   // 이 판에서 바뀐 것
        public Action Build;
    }

    public static readonly Entry[] Entries =
    {
        new Entry { Name = "ACO_Hangar", Version = 10, Change = "강화 SUPPORT 탭과 지원 제목 줄 아이콘을 무전기로 바꿨습니다(6-181 — 작전 지도 아이콘과 겹치지 않게). 고친 뒤 Bake 하세요. (판 9: 지원 줄을 구획으로 바꿨습니다(6-180 — 특수무장처럼 '지원' 제목 줄 + 테두리 칸: 아이콘 · 이름 · 남은 횟수, 고르지 않았으면 안내 문구와 깜빡이는 테두리). 무장 상세 칸을 조금 줄이고 BUY를 올렸습니다. 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 8: 패널 글자를 번역 표 키로 바꿨습니다(6-166). 판 7: 패널 제목을 HANGAR에서 LOADOUT으로 바꿨습니다(6-162 — 진짜 격납고 방과 헷갈리지 않게). 브리핑실 사본과 격납고 방의 패널이 함께 바뀝니다.))", Build = ACO_HUDBuilder.CreateHangarPrefab },
        new Entry { Name = "ACO_MissionBoard", Version = 6, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 5: 최고 획득 순위를 하이스코어판으로 옮기고, 기록 왼쪽에 쉐브론 스크롤을 두고, 이번 미션의 내 결과를 넣었습니다.)", Build = ACO_HUDBuilder.CreateMissionBoardPrefab },
        new Entry { Name = "ACO_MissionConsole", Version = 4, Change = "자유 비행 버튼에 아이콘(굽이치는 궤적 끝에서 날아오르는 기체)을 붙였습니다(6-181). 고친 뒤 Bake 하세요. (판 3: 명단 칸을 4개로 줄이고 크게(590×395) — 각자의 특수무장 · 서포트 두 줄(6-173). 고친 뒤 Bake 하세요. (판 2: 카드 제목(작전 · 페이즈 · 지형 · 명단)을 번역 표 키로 바꿨습니다(6-166 — 배너 · 버튼 · 명단 글자는 게임 진행이 설정 LANGUAGE에 맞춰 쓴다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.))", Build = ACO_HUDBuilder.CreateMissionConsolePrefab },
        new Entry { Name = "ACO_HUD", Version = 3, Change = "표적 마커 풀을 64개에서 96개로 늘렸습니다(6-270 — 시가지 정밀 타격의 도시 차량 17대가 함께 뜨면 아군기 · 폭격기 마커가 모자랐다). 고친 뒤 Bake 하세요. (판 2: 마커 테두리·락온 마름모를 얇게(한 변의 8% → 4%), 녹색을 순녹색·불투명하게, 글자에 은은한 그림자(하늘·안개에서 보이게 — 색은 HUD 설정에서 바꾼다). 고친 뒤 Bake 하세요.)", Build = ACO_HUDBuilder.CreateHUDPrefab },
        new Entry { Name = "ACO_FlightHud", Version = 4, Change = "착함 유도(ICLS, 6-201) — HUD 가운데 접근 축 · 글라이드 슬로프 바 · 둘레 마름모와 아래 \"ICLS 거리\"를 더했습니다. 고친 뒤 Bake가 착함 와이어를 잇습니다. (판 3: 녹색(선·글자)을 순녹색·불투명하게, 글자에 은은한 그림자 — 하늘·안개에서 보이게(색은 HUD 설정에서 바꾼다, 장전 막대 16개 2행 8열 포함). 고친 뒤 Bake 하세요.)", Build = ACO_HUDBuilder.CreateFlightHudPrefab },
        new Entry { Name = "ACO_HudSettings", Version = 5, Change = "LANGUAGE 항목(AUTO · 한국어 · 영어 · 일본어)을 더하고 패널 글자를 번역 표 키로 바꿨습니다(6-166). 고친 뒤 Setup Check의 번역 장치 · 글꼴 [고치기]와 Bake 하세요. (판 4: 패널 제목을 HUD SETTINGS에서 SETTINGS로 바꿨습니다(HUD 말고도 갑판 요원 등 설정이 있어서 — 튜토리얼도 '설정'으로 부른다). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateHudSettingsPrefab },
        new Entry { Name = "ACO_TacNamePanel", Version = 2, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.", Build = ACO_HUDBuilder.CreateTacNamePanelPrefab },
        new Entry { Name = "ACO_BgmPanel", Version = 4, Change = "브리핑실 모니터 화면(1.73:1)에 맞춰 패널을 1080 × 625로 넓혔습니다(6-220). 고친 뒤 Setup Check의 BGM 패널 크기 [고치기](모니터 화면 크기로 놓기)와 Bake 하세요. 예전 판 3: 패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 2: 지금 곡 표시(NOW PLAYING + 곡 이름) 칸 추가 — 패널이 위아래로 커진다)", Build = ACO_HUDBuilder.CreateBgmPanelPrefab },
        new Entry { Name = "ACO_SpectatorPanel", Version = 3, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 2: 관전 카메라에 체적 구름을 넣습니다. 고친 뒤 Bake 하세요.)", Build = ACO_HUDBuilder.CreateSpectatorPanelPrefab },
        new Entry { Name = "ACO_DebugPanel", Version = 4, Change = "페이즈 모듈 줄을 20개에서 30개로 늘리고 줄 간격을 좁혔습니다(시가지 · 터널 등 새 모듈의 P1 버튼). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateDebugPanelPrefab },
        new Entry { Name = "ACO_DecalStudio", Version = 6, Change = "데칼 / 라운델 탭(6-193): URL 칸 · 불러오기 · 이미지 창은 같이 쓰고, 라운델 탭에 고정 자리 안내 · 상태 · 라운델 지우기. 라운델 탭에선 데칼 건이 아무것도 하지 않습니다. 고친 뒤 Bake 하세요. (판 5: 데칼 건을 들고 있지 않을 때 건 위에 튜토리얼 같은 녹색 홀로그램 화살표가 뜹니다(건이 어디 놓여도 위에 곧게). 고친 뒤 Bake 하세요.) (판 4: 패널 글자를 번역 표 키로 — 6-166 2단계. 판 3: 데칼 건 모델 크기 고침)", Build = ACO_HUDBuilder.CreateDecalStudioPrefab },
        new Entry { Name = ACO_HUDBuilder.UpdateBoardPrefabName, Version = 1, Change = "업데이트 노트 보드(6-251) — 크레딧 보드와 같은 판, 본문은 언어별 파일(Credits/ACO_Updates_ko · en · ja.txt). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateUpdateBoardPrefab },
        new Entry { Name = "ACO_CreditBoard", Version = 3, Change = "사물함 폭(0.62 x 0.95m) 세워 두는 판 + 스크롤(끌기 · 스크롤바 · ▲▼ 버튼, 6-228), 불투명 판 · 얇은 틀. 고친 뒤 Bake 하세요. 예전 판 2: 패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.", Build = ACO_HUDBuilder.CreateCreditBoardPrefab },
        new Entry { Name = ACO_HUDBuilder.HiScoreName, Version = 2, Change = "제목(HIGH SCORE)을 번역 표 키로 바꿨습니다(6-166 2단계). 고친 뒤 Bake 하세요.", Build = () => ACO_HUDBuilder.CreateHiScorePrefab() },
        new Entry { Name = ACO_HUDBuilder.VideoAudioTogglePrefab, Version = 1, Change = "선원실 영상 플레이어 소리 범위 토글(NEAR · GLOBAL — 6-220).", Build = ACO_HUDBuilder.CreateVideoAudioTogglePrefab },
        new Entry { Name = ACO_HUDBuilder.TutorialPrefabName, Version = 15, Change = "튜토리얼 창 바탕(Frame)이 클릭을 받아 막게 했습니다(6-291 — 창의 빈 자리를 누르면 뒤 작전 콘솔 버튼이 눌렸다). 고친 뒤 Bake. (판 14: CIC 작전 지원 쪽 정찰 UAV 설명의 '그 자리'를 '찍은 좌표 상공'으로(웨이포인트 지점으로 읽히지 않게, 6-279). 고친 뒤 Bake 하세요. 예전 판 13: CIC 작전 지원 쪽 아군 편대 · 순항미사일 설명에서 '확정을 눌러야 실행됩니다'를 뺐습니다(보면 안다, 6-279). 고친 뒤 Bake 하세요. 예전 판 12: CIC 작전 지원 쪽 문구: 정찰 UAV는 선회하며 정찰(정밀 타격 임무의 타겟도 식별), 아래 안내는 도구의 쿨타임을 모두가 함께 쓴다(6-279). 고친 뒤 Bake 하세요. 예전 판 11: CIC 작전 지원 쪽(5 / 9 — 좌표 찍기 · 웨이포인트 · 정찰 UAV · 아군 편대 · 순항미사일 · 1인칭 고글, 사진 tut_cicops, 6-279)을 넣고 함내 지도의 CIC 이름표를 작전 테이블 · 관전으로. 사진이 없으면 Setup Check가 찍습니다. 고친 뒤 Bake 하세요. 예전 판 10: 비행 중 쪽(3 / 8)에 애프터버너 줄을 더했습니다(6-250 — 데스크톱 T 켜기·끄기, VR 스로틀 끝까지). 고친 뒤 Bake 하세요. 예전 판 9: 개인 설정 · 공유 요소를 로컬 · 글로벌로 통일(6-221 — 내 화면만 → 로컬). 고친 뒤 Bake 하세요. 예전 판 8: BGM 쪽 상황별 BGM 설명을 저작권 오해가 없게 — 제작자가 미리 설정한 임무에 맞는 트랙을 스트리밍해서 들려드린다(6-220). 고친 뒤 Bake 하세요. 예전 판 7: BGM 쪽(7 / 8 — 상황별 BGM · 볼륨 · 켜기 · 선원실 영상 플레이어 NEAR/GLOBAL, 6-220)을 넣고 함내 지도를 8쪽으로. 고친 뒤 Setup Check 튜토리얼 사진(bgm · video)을 찍고 Bake 하세요. 예전 판 6: 기체 꾸미기 · 자유 비행 쪽을 더했습니다(6-200, 7쪽 — 개요 · 출격 · 비행 중 · 강화 · 꾸미기 · 자유 비행 · 함내 지도). 사진 tut_custom이 없으면 Setup Check가 찍습니다. (판 5: 언어를 설정 패널 LANGUAGE로 옮겼습니다(6-166) — 팝업의 한 · EN · 日 버튼을 없앴습니다. (판 4: 한국어 · 영어 · 일본어, 판 3: 브리핑 판으로 옮김) 고친 뒤 Bake 하세요.))", Build = ACO_HUDBuilder.CreateTutorialPrefab },
        new Entry { Name = ACO_InteriorBuilder.PrefabName, Version = 5, Change = "TV 받침 목 앞면이 지워져 안이 들여다보이던 것을 고치고(함내 FBX 다시 만듦), TV 책상 콜라이더를 위판 · 선반 · 다리로 나눠 셋톱박스 앞 영상 소리 버튼이 눌리게 합니다(6-237). 철 · 고무 · LED 묶음과 가구 콜라이더만 다시 만들어 빛은 다시 굽지 않아도 됩니다. 고친 뒤 Bake 하세요.", Build = ACO_InteriorBuilder.UpdateTvAndDesk },
        new Entry { Name = ACO_HUDBuilder.CicConsoleName, Version = 17, Change = "도구 패널(ToolCanvas)의 UI 콜라이더를 트리거로 넣어 지나다닐 때 걸리지 않게 했습니다(6-291). 고친 뒤 Bake. (판 16: 정찰 UAV 이동 속도를 1.5배(450m/s)로(6-280 — 판 15의 2배 600m/s에서 낮춤, 맵 끝에서 끝까지 약 1분 49초). 고친 뒤 Bake. (판 15: 정찰 UAV 이동 속도 300 → 600m/s. 판 14: UAV 경로 동기화를 자식 DroneSync(ACO_CicDroneSync)로 나누고(두 사람이 동시에 웨이포인트 · UAV를 누르면 한쪽이 지워지던 것), 도구 패널 판(ToolPanel)을 연결해 패널 버튼 누름이 지도 커서를 옮기지 않게 했습니다. 판 13: 테이블의 순항미사일(LACM) 표시 점을 주황에서 파랑으로 바꿨습니다(아군 색). 판 12: 도구 패널 행 순서를 웨이포인트 · 정찰 UAV · 아군 편대 · 순항미사일로(편대와 UAV 자리 바꿈). 판 11: 도구 패널을 D안(하드웨어 버튼 — 금속 판 · 백라이트 키 LED · LCD 표시창, 위 LCD에 커서 거리)으로 다시 만들고, 순항미사일 · 아군 편대에 확정 · 취소 팝업(ConfirmPopup)을 더했습니다. 판 10: 도구 패널에 4행째 CRUISE MSL(순항미사일 일제 발사 — 아이콘 cic_cruise)과 테이블 순항미사일 표시(주황 점 8개)를 더하고 패널을 1000 × 850으로 키웠습니다. 판 9: 빛기둥(테이블 커서 · 찍은 지면의 큰 빛기둥)을 ACO/LightBeam 셰이더로 — 반투명, 위로 갈수록 흐려짐, 부드러운 가장자리 · 흰 심, 위로 흐르는 옅은 띠. 테이블 커서 기둥 폭 1.2 → 1.8cm, 받침은 반투명 빛 고리(ACO/LightDisc, 지름 5 → 7cm, 가운데 비움), 둘레에 옅고 넓은 빛무리(Halo — 큰 기둥 40m, 커서 5.5cm). 기둥 바닥을 비워(큰 기둥 15m, 테이블 2.5cm까지 거의 투명) 찍은 자리의 유닛 · 마커가 덮이지 않게, 심은 덜 하얗게, 위에서 내려다볼수록 옅게(_TopFade). 판 8: 테이블의 UAV 표시에 이름표(정찰 UAV — 플레이어 이름표처럼)를 달았습니다. 예전 판: 7 편대 고리 · WP 마름모를 지형 위로, 6 찍은 지면 빛기둥, 5 RECON UAV 이름, 4 3행 + 아이콘, 3 크기 반, 2 받침대 없애고 테이블 테두리, 1 처음.))", Build = ACO_HUDBuilder.CreateCicConsolePrefab },
        new Entry { Name = ACO_HUDBuilder.CicGogglesName, Version = 10, Change = "고글 콜라이더를 트리거로(6-293 — 들고 기체에 타면 기체를 밀었다), 조종사 시점 자리를 기체 기준 위 3.66 · 앞 3.04m로(스크립트 기본값). 고친 뒤 Bake. (판 9: 고글 패널에 화질 버튼(HD 1024 ↔ FHD 1536 — 관전 카메라 HD/FHD처럼, 로컬, 렌더 텍스처 ACO_GogglesRT_FHD)을 더하고 RESET 버튼을 왼쪽으로 줄였습니다. 고친 뒤 Bake. (판 8: 고글에 굽힌 텍스처(Textures/CicGoggles/ACO_FpvGoggles_Albedo.png 2048² — 칠 벗겨짐 · 틈 때 · 얼룩 + TOV-7 스텐실 · 명판 · LASER 라벨 · DIOPT · PWR · BATT 표기)를 입혔습니다. 판 7: 고글 모양을 실제 장비에 가까운 상자형 군용 관측 장비(챔퍼 하우징, 앞 광학부 판 — 깊은 후드 주 대물렌즈 · 주간 카메라 · 레이저 창, 뒤 널링 시도 링 + 고무 눈컵 둘, 위 레일 · 버튼 판 · 전원 스위치, 옆 고무 손잡이 · 배터리 캡, 머리끈 없음 — Models/CicGoggles/ACO_FpvGoggles.fbx, Tools/aco_fpv_goggles_model.py)로 바꾸고 책상에 바로 세워 놓았습니다. 고친 뒤 Bake. 예전 판: 6 둥근 쌍안경 모양, 5 FPV 고글 모델, 4 UAV 시점 카메라 포드 자리, 3 사용 글자 MARK, 2 모니터 화면을 덮는 자리, 1 처음.))", Build = ACO_HUDBuilder.CreateCicGogglesPrefab },
        new Entry { Name = ACO_PrefabBuilder.CicStrikeName, Version = 6, Change = "순항미사일 발사음을 VLS 발사 소리(Audio/SFX/VLS_Launchsound.ogg)로 바꿨습니다(6-290 — 연속 발사도 끊기지 않게 겹쳐 재생). 고친 뒤 Bake. (판 5: 순항미사일 약하게(6-284): 범위 피해 600 → 450, 속도를 원래대로(순항 300 · 급강하 600m/s — 판 2 · 3에서 올렸던 것). 고친 뒤 Bake. (판 4: 순항미사일 체력(SaccTarget)의 미리 보기 격추(PredictExplosion)를 껐습니다(6-248 규칙 — Setup Check 경고). 고친 뒤 Bake. (판 3: 순항미사일을 1.5배(순항 450, 급강하 900m/s)로(6-280 — 판 2의 2배는 SAM 미끼가 약해져 낮춤, 작전 지역 끝까지 약 58초). 고친 뒤 Bake. (판 2: 순항 600, 급강하 1200m/s. 판 1: CIC 순항미사일 일제 발사(6-276): 미사일 8발 풀(체력 20 · 적 대공 표적 레이어 25) · LASM 폭발 · 물기둥 · 파편 · VLS 발사음. 고친 뒤 Setup Check CIC 도구 [고치기] → Bake.))))", Build = ACO_PrefabBuilder.CreateCicStrikePrefab },
        new Entry { Name = ACO_HUDBuilder.CicUavName, Version = 3, Change = "도장을 회색 단색(Synty Jet_01_A)으로, 머티리얼을 우리 Standard(ACO_CicUav_Gray)로 바꿨습니다 — Synty 머티리얼은 VR에서 이상하게 보였다. (예전 판: 2 Synty SM_Veh_Drone_01 모델, 1 가디언 드론 겉모양.)", Build = ACO_HUDBuilder.CreateCicUavPrefab },
        // 예전 판 4: new Entry { Name = ACO_InteriorBuilder.PrefabName, Version = 4, Change = "복도 액자 두 개가 브리핑실과 같은 사진(02 · 03)을 쓰던 것을 04(MR. S) · 06(잠수함)으로 바꿉니다(6-222). 사진 렌더러만 다시 만들어 빛은 다시 굽지 않아도 됩니다. (판 3: 구역, 문 텔레포트, CIC 무전을 넣고 조작대 콜라이더를 줄임.)", Build = ACO_InteriorBuilder.UpdatePhotos },
    };

    private static string PathOf(string name) { return $"{PrefabDir}/{name}.prefab"; }

    public static GameObject Asset(Entry entry) { return AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(entry.Name)); }

    public static bool IsOutdated(Entry entry)
    {
        if (Asset(entry) == null) { return false; }
        AssetImporter importer = AssetImporter.GetAtPath(PathOf(entry.Name));
        int stored;
        if (importer == null || !int.TryParse(importer.userData, out stored)) { stored = 1; }
        return stored < entry.Version;
    }

    /// 프리팹 생성 함수가 저장한 뒤 부른다.
    public static void Stamp(string name)
    {
        Entry entry = Array.Find(Entries, e => e.Name == name);
        AssetImporter importer = AssetImporter.GetAtPath(PathOf(name));
        if (entry == null || importer == null) { return; }
        string version = entry.Version.ToString();
        if (importer.userData == version) { return; }
        importer.userData = version;
        importer.SaveAndReimport();
    }
}
