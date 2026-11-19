using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ACO;

/// 버전 확인 알림(6-296): 작전판(브리핑 판 — 작전 콘솔) 위 가장자리 바로 위에 띄우는 알림 띠. 막지 않는다(클릭을 받지 않는다).
/// 루트 = 판 화면 가운데, +Z = 방 쪽(튜토리얼과 같은 자리). Setup Check가 놓는다.
public partial class ACO_HUDBuilder
{
    public const string VersionGuardPrefabName = "ACO_VersionGuard";
    private static readonly Vector2 VerBannerSize = new Vector2(2400f, 170f);   // 판 캔버스 단위(판 너비 2400 · 높이 1500)

    public static void CreateVersionGuardPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = TutorialFont();
        if (font == null) { Debug.LogError("[ACO] 버전 확인 알림: TMP 폰트를 찾지 못했습니다."); return; }

        GameObject root = new GameObject(VersionGuardPrefabName);
        ACO_VersionGuard guard = root.AddUdonSharpComponent<ACO_VersionGuard>();

        // 판 위 가장자리(+750) 바로 위, 판 앞 1cm. 상호작용이 없으니 UI 판정(VRCUiShape · 레이캐스터)을 뺀다 — 판 버튼을 가리지 않는다
        var bannerRoot = new GameObject("Banner");
        bannerRoot.transform.SetParent(root.transform, false);
        bannerRoot.transform.localPosition = TutBoardPoint(new Vector2(0f, 750f + VerBannerSize.y * 0.5f + 10f), 0.01f);
        RectTransform canvas = MakeUiPanel(bannerRoot.transform, VerBannerSize.x, VerBannerSize.y);
        canvas.localScale = Vector3.one * BoardUnit;
        Object.DestroyImmediate(canvas.GetComponent<VRC.SDK3.Components.VRCUiShape>());
        Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());
        canvas.GetComponent<Canvas>().sortingOrder = 3;
        Image back = canvas.Find("Background").GetComponent<Image>();
        back.color = guard.MismatchColor;
        // 글자 오브젝트 이름은 번역 키가 아니다(상태에 따라 스크립트가 문구를 바꾼다 — Bake 라벨 번역에 걸리지 않게)
        TextMeshProUGUI text = MakeUiText(canvas, "Message", Vector2.zero, new Vector2(VerBannerSize.x - 60f, VerBannerSize.y - 16f), 56f, font, "Checking the world version...");
        text.color = new Color(1f, 0.95f, 0.88f, 1f);
        text.fontStyle = FontStyles.Bold;
        text.enableWordWrapping = true;
        text.enableAutoSizing = true;   // 언어마다 길이가 달라 칸 안에 맞춘다(두 줄까지)
        text.fontSizeMax = 56f;
        text.fontSizeMin = 30f;

        guard.Banner = bannerRoot;
        guard.BannerBack = back;
        guard.BannerText = text;
        bannerRoot.SetActive(false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{VersionGuardPrefabName}.prefab");
        ACO_PrefabVersions.Stamp(VersionGuardPrefabName);
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 버전 확인 알림 프리팹을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)} — Setup Check가 작전판 자리에 놓습니다.");
    }

    /// 빌드 번호(6-296): 2020-01-01 UTC부터 지난 분. 빌드할 때마다 씬의 버전 확인에 넣는다(ACO_SceneBaker.OnBuildRequested).
    public static int CurrentBuildStamp()
    {
        System.DateTime epoch = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        return (int)((System.DateTime.UtcNow - epoch).TotalMinutes);
    }

    public static int StampVersionGuards()
    {
        int stamp = CurrentBuildStamp();
        int n = 0;
        foreach (ACO_VersionGuard g in Object.FindObjectsOfType<ACO_VersionGuard>(true))
        {
            if (g.BuildStamp != stamp) { g.BuildStamp = stamp; EditorUtility.SetDirty(g); }
            n++;
        }
        if (n > 0) { Debug.Log($"[ACO] 버전 확인(6-296): 빌드 번호 {stamp}을(를) {n}곳에 넣었습니다."); }
        else { Debug.LogWarning("[ACO] 버전 확인(6-296): 씬에 버전 확인(ACO_VersionGuard)이 없습니다 — Setup Check [고치기]로 놓으세요. 이전 버전 인스턴스를 알리지 못합니다."); }
        return n;
    }
}
