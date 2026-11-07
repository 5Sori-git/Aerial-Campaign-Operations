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
        new Entry { Name = "ACO_Hangar", Version = 4, Change = "강화 칸을 넓히고 특수무장 칸을 줄였습니다. 탭과 강화 줄의 아이콘과 글자 사이를 띄웠습니다.", Build = ACO_HUDBuilder.CreateHangarPrefab },
        new Entry { Name = "ACO_MissionBoard", Version = 5, Change = "최고 획득 순위를 하이스코어판으로 옮기고, 기록 왼쪽에 쉐브론 스크롤을 두고, 이번 미션의 내 결과를 넣었습니다.", Build = ACO_HUDBuilder.CreateMissionBoardPrefab },
        new Entry { Name = "ACO_MissionConsole", Version = 1, Build = ACO_HUDBuilder.CreateMissionConsolePrefab },
        new Entry { Name = "ACO_HUD", Version = 1, Build = ACO_HUDBuilder.CreateHUDPrefab },
        new Entry { Name = "ACO_HudSettings", Version = 1, Build = ACO_HUDBuilder.CreateHudSettingsPrefab },
        new Entry { Name = "ACO_TacNamePanel", Version = 1, Build = ACO_HUDBuilder.CreateTacNamePanelPrefab },
        new Entry { Name = "ACO_SpectatorPanel", Version = 2, Change = "관전 카메라에 체적 구름을 넣습니다. 고친 뒤 Bake 하세요.", Build = ACO_HUDBuilder.CreateSpectatorPanelPrefab },
        new Entry { Name = "ACO_DebugPanel", Version = 1, Build = ACO_HUDBuilder.CreateDebugPanelPrefab },
        new Entry { Name = "ACO_DecalStudio", Version = 1, Build = ACO_HUDBuilder.CreateDecalStudioPrefab },
        new Entry { Name = "ACO_CreditBoard", Version = 1, Build = ACO_HUDBuilder.CreateCreditBoardPrefab },
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
