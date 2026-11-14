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
        new Entry { Name = "ACO_HUD", Version = 2, Change = "마커 테두리·락온 마름모를 얇게(한 변의 8% → 4%), 녹색을 순녹색·불투명하게, 글자에 은은한 그림자(하늘·안개에서 보이게 — 색은 HUD 설정에서 바꾼다). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateHUDPrefab },
        new Entry { Name = "ACO_FlightHud", Version = 4, Change = "착함 유도(ICLS, 6-201) — HUD 가운데 접근 축 · 글라이드 슬로프 바 · 둘레 마름모와 아래 \"ICLS 거리\"를 더했습니다. 고친 뒤 Bake가 착함 와이어를 잇습니다. (판 3: 녹색(선·글자)을 순녹색·불투명하게, 글자에 은은한 그림자 — 하늘·안개에서 보이게(색은 HUD 설정에서 바꾼다, 장전 막대 16개 2행 8열 포함). 고친 뒤 Bake 하세요.)", Build = ACO_HUDBuilder.CreateFlightHudPrefab },
        new Entry { Name = "ACO_HudSettings", Version = 5, Change = "LANGUAGE 항목(AUTO · 한국어 · 영어 · 일본어)을 더하고 패널 글자를 번역 표 키로 바꿨습니다(6-166). 고친 뒤 Setup Check의 번역 장치 · 글꼴 [고치기]와 Bake 하세요. (판 4: 패널 제목을 HUD SETTINGS에서 SETTINGS로 바꿨습니다(HUD 말고도 갑판 요원 등 설정이 있어서 — 튜토리얼도 '설정'으로 부른다). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateHudSettingsPrefab },
        new Entry { Name = "ACO_TacNamePanel", Version = 2, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.", Build = ACO_HUDBuilder.CreateTacNamePanelPrefab },
        new Entry { Name = "ACO_BgmPanel", Version = 4, Change = "브리핑실 모니터 화면(1.73:1)에 맞춰 패널을 1080 × 625로 넓혔습니다(6-220). 고친 뒤 Setup Check의 BGM 패널 크기 [고치기](모니터 화면 크기로 놓기)와 Bake 하세요. 예전 판 3: 패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 2: 지금 곡 표시(NOW PLAYING + 곡 이름) 칸 추가 — 패널이 위아래로 커진다)", Build = ACO_HUDBuilder.CreateBgmPanelPrefab },
        new Entry { Name = "ACO_SpectatorPanel", Version = 3, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 2: 관전 카메라에 체적 구름을 넣습니다. 고친 뒤 Bake 하세요.)", Build = ACO_HUDBuilder.CreateSpectatorPanelPrefab },
        new Entry { Name = "ACO_DebugPanel", Version = 3, Change = "페이즈 모듈 줄을 18개에서 20개로 늘렸습니다(MR. S 등 새 모듈의 P1 버튼). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateDebugPanelPrefab },
        new Entry { Name = "ACO_DecalStudio", Version = 6, Change = "데칼 / 라운델 탭(6-193): URL 칸 · 불러오기 · 이미지 창은 같이 쓰고, 라운델 탭에 고정 자리 안내 · 상태 · 라운델 지우기. 라운델 탭에선 데칼 건이 아무것도 하지 않습니다. 고친 뒤 Bake 하세요. (판 5: 데칼 건을 들고 있지 않을 때 건 위에 튜토리얼 같은 녹색 홀로그램 화살표가 뜹니다(건이 어디 놓여도 위에 곧게). 고친 뒤 Bake 하세요.) (판 4: 패널 글자를 번역 표 키로 — 6-166 2단계. 판 3: 데칼 건 모델 크기 고침)", Build = ACO_HUDBuilder.CreateDecalStudioPrefab },
        new Entry { Name = "ACO_CreditBoard", Version = 2, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.", Build = ACO_HUDBuilder.CreateCreditBoardPrefab },
        new Entry { Name = ACO_HUDBuilder.HiScoreName, Version = 2, Change = "제목(HIGH SCORE)을 번역 표 키로 바꿨습니다(6-166 2단계). 고친 뒤 Bake 하세요.", Build = () => ACO_HUDBuilder.CreateHiScorePrefab() },
        new Entry { Name = ACO_HUDBuilder.VideoAudioTogglePrefab, Version = 1, Change = "선원실 영상 플레이어 소리 범위 토글(NEAR · GLOBAL — 6-220).", Build = ACO_HUDBuilder.CreateVideoAudioTogglePrefab },
        new Entry { Name = ACO_HUDBuilder.TutorialPrefabName, Version = 9, Change = "개인 설정 · 공유 요소를 로컬 · 글로벌로 통일(6-221 — 내 화면만 → 로컬). 고친 뒤 Bake 하세요. 예전 판 8: BGM 쪽 상황별 BGM 설명을 저작권 오해가 없게 — 제작자가 미리 설정한 임무에 맞는 트랙을 스트리밍해서 들려드린다(6-220). 고친 뒤 Bake 하세요. 예전 판 7: BGM 쪽(7 / 8 — 상황별 BGM · 볼륨 · 켜기 · 선원실 영상 플레이어 NEAR/GLOBAL, 6-220)을 넣고 함내 지도를 8쪽으로. 고친 뒤 Setup Check 튜토리얼 사진(bgm · video)을 찍고 Bake 하세요. 예전 판 6: 기체 꾸미기 · 자유 비행 쪽을 더했습니다(6-200, 7쪽 — 개요 · 출격 · 비행 중 · 강화 · 꾸미기 · 자유 비행 · 함내 지도). 사진 tut_custom이 없으면 Setup Check가 찍습니다. (판 5: 언어를 설정 패널 LANGUAGE로 옮겼습니다(6-166) — 팝업의 한 · EN · 日 버튼을 없앴습니다. (판 4: 한국어 · 영어 · 일본어, 판 3: 브리핑 판으로 옮김) 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateTutorialPrefab },
        new Entry { Name = ACO_InteriorBuilder.PrefabName, Version = 4, Change = "복도 액자 두 개가 브리핑실과 같은 사진(02 · 03)을 쓰던 것을 04(MR. S) · 06(잠수함)으로 바꿉니다(6-222). 사진 렌더러만 다시 만들어 빛은 다시 굽지 않아도 됩니다. (판 3: 구역, 문 텔레포트, CIC 무전을 넣고 조작대 콜라이더를 줄임.)", Build = ACO_InteriorBuilder.UpdatePhotos },
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
