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
        new Entry { Name = "ACO_Hangar", Version = 8, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 — 상점 · 무장 목록 · 지원 줄이 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 7: 패널 제목을 HANGAR에서 LOADOUT으로 바꿨습니다(6-162 — 진짜 격납고 방과 헷갈리지 않게). 브리핑실 사본과 격납고 방의 패널이 함께 바뀝니다.)", Build = ACO_HUDBuilder.CreateHangarPrefab },
        new Entry { Name = "ACO_MissionBoard", Version = 6, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 5: 최고 획득 순위를 하이스코어판으로 옮기고, 기록 왼쪽에 쉐브론 스크롤을 두고, 이번 미션의 내 결과를 넣었습니다.)", Build = ACO_HUDBuilder.CreateMissionBoardPrefab },
        new Entry { Name = "ACO_MissionConsole", Version = 3, Change = "명단 칸을 4개로 줄이고 크게(590×395) — 각자의 특수무장 · 서포트 두 줄(6-173). 고친 뒤 Bake 하세요. (판 2: 카드 제목(작전 · 페이즈 · 지형 · 명단)을 번역 표 키로 바꿨습니다(6-166 — 배너 · 버튼 · 명단 글자는 게임 진행이 설정 LANGUAGE에 맞춰 쓴다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.)", Build = ACO_HUDBuilder.CreateMissionConsolePrefab },
        new Entry { Name = "ACO_HUD", Version = 2, Change = "마커 테두리·락온 마름모를 얇게(한 변의 8% → 4%), 녹색을 순녹색·불투명하게, 글자에 은은한 그림자(하늘·안개에서 보이게 — 색은 HUD 설정에서 바꾼다). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateHUDPrefab },
        new Entry { Name = "ACO_FlightHud", Version = 3, Change = "녹색(선·글자)을 순녹색·불투명하게, 글자에 은은한 그림자 — 하늘·안개에서 보이게(색은 HUD 설정에서 바꾼다, 장전 막대 16개 2행 8열 포함). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateFlightHudPrefab },
        new Entry { Name = "ACO_HudSettings", Version = 5, Change = "LANGUAGE 항목(AUTO · 한국어 · 영어 · 일본어)을 더하고 패널 글자를 번역 표 키로 바꿨습니다(6-166). 고친 뒤 Setup Check의 번역 장치 · 글꼴 [고치기]와 Bake 하세요. (판 4: 패널 제목을 HUD SETTINGS에서 SETTINGS로 바꿨습니다(HUD 말고도 갑판 요원 등 설정이 있어서 — 튜토리얼도 '설정'으로 부른다). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateHudSettingsPrefab },
        new Entry { Name = "ACO_TacNamePanel", Version = 2, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.", Build = ACO_HUDBuilder.CreateTacNamePanelPrefab },
        new Entry { Name = "ACO_BgmPanel", Version = 3, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 2: 지금 곡 표시(NOW PLAYING + 곡 이름) 칸 추가 — 패널이 위아래로 커진다)", Build = ACO_HUDBuilder.CreateBgmPanelPrefab },
        new Entry { Name = "ACO_SpectatorPanel", Version = 3, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요. (판 2: 관전 카메라에 체적 구름을 넣습니다. 고친 뒤 Bake 하세요.)", Build = ACO_HUDBuilder.CreateSpectatorPanelPrefab },
        new Entry { Name = "ACO_DebugPanel", Version = 3, Change = "페이즈 모듈 줄을 18개에서 20개로 늘렸습니다(MR. S 등 새 모듈의 P1 버튼). 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateDebugPanelPrefab },
        new Entry { Name = "ACO_DecalStudio", Version = 5, Change = "데칼 건을 들고 있지 않을 때 건 위에 튜토리얼 같은 녹색 홀로그램 화살표가 뜹니다(건이 어디 놓여도 위에 곧게). 고친 뒤 Bake 하세요. (판 4: 패널 글자를 번역 표 키로 — 6-166 2단계. 판 3: 데칼 건 모델 크기 고침)", Build = ACO_HUDBuilder.CreateDecalStudioPrefab },
        new Entry { Name = "ACO_CreditBoard", Version = 2, Change = "패널 글자를 번역 표 키로 바꿨습니다(6-166 2단계 — 설정 LANGUAGE를 따른다). 고친 뒤 Setup Check의 번역 장치 [고치기]와 Bake 하세요.", Build = ACO_HUDBuilder.CreateCreditBoardPrefab },
        new Entry { Name = ACO_HUDBuilder.HiScoreName, Version = 2, Change = "제목(HIGH SCORE)을 번역 표 키로 바꿨습니다(6-166 2단계). 고친 뒤 Bake 하세요.", Build = () => ACO_HUDBuilder.CreateHiScorePrefab() },
        new Entry { Name = ACO_HUDBuilder.TutorialPrefabName, Version = 5, Change = "언어를 설정 패널 LANGUAGE로 옮겼습니다(6-166) — 팝업의 한 · EN · 日 버튼을 없앴습니다. (판 4: 한국어 · 영어 · 일본어, 판 3: 브리핑 판으로 옮김) 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateTutorialPrefab },
        new Entry { Name = ACO_InteriorBuilder.PrefabName, Version = 3, Change = "구역, 문 텔레포트, CIC 무전을 넣고, 관전 패널 버튼을 막던 조작대 콜라이더를 줄입니다. 메시와 조명은 그대로라 다시 굽지 않아도 됩니다. 고친 뒤 Bake 하세요.", Build = ACO_InteriorBuilder.UpdateSystems },
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
