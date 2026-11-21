using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// 조종사 이름표(6-340): 이름표 글자 32개를 가진 프리팹. 위치는 스크립트가 매 프레임 정하니 어디에 놓아도 된다(Setup Check가 씬 루트에 놓는다).
public partial class ACO_HUDBuilder
{
    public const string PilotTagsPrefabName = "ACO_PilotTags";
    public const int PilotTagCount = 32;

    public static void CreatePilotTagsPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = TutorialFont();
        if (font == null) { Debug.LogError("[ACO] 조종사 이름표: TMP 폰트를 찾지 못했습니다."); return; }

        GameObject root = new GameObject(PilotTagsPrefabName);
        ACO_PilotTag tags = root.AddUdonSharpComponent<ACO_PilotTag>();
        TMP_SpriteAsset icons = RecIconSpriteAsset();
        Material tagMat = PilotTagMaterial(font);
        var labels = new TextMeshPro[PilotTagCount];
        for (int i = 0; i < PilotTagCount; i++)
        {
            // 글자 크기 1 ≈ 0.1m. 칸은 넉넉하게(THOUSAND POINTS OF LIGHT · 언어 전환 — 6-340) — 줄바꿈 없이 가운데 아래 기준
            TextMeshPro t = MakeText(root.transform, "Tag" + i, Vector3.zero, 1.0f, Color.white,
                TextAlignmentOptions.Bottom, new Vector2(4f, 0.6f), new Vector2(0.5f, 0f), font, tagMat, "");
            t.overflowMode = TextOverflowModes.Overflow;
            t.spriteAsset = icons;   // 칭호 앞 아이콘(6-341)
            t.gameObject.SetActive(false);
            labels[i] = t;
        }
        tags.Labels = labels;
        tags.HeightAboveHead = 0.64f;   // VRChat 이름표 위(판 2 — 0.32는 겹쳤다)

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{PilotTagsPrefabName}.prefab");
        ACO_PrefabVersions.Stamp(PilotTagsPrefabName);
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 조종사 이름표 프리팹을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)} — Setup Check가 씬에 놓습니다.");
    }

    /// 이름표 글자 재질(판 4): 글꼴 재질 복사 + 짙은 그림자(밑깔림) + 얇은 검은 외곽선 — 밝은 하늘 · 갑판 · 흰 벽 앞에서도 읽히게.
    /// HUD 글자 그림자 설정(TEXT SHADOW)과는 따로다(그쪽은 HUD 재질만).
    private static Material PilotTagMaterial(TMP_FontAsset font)
    {
        string path = $"{GeneratedDir}/ACO_PilotTagText.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(font.material);
            AssetDatabase.CreateAsset(m, path);
        }
        else
        {
            m.shader = font.material.shader;
            m.CopyPropertiesFromMaterial(font.material);
        }
        m.name = "ACO_PilotTagText";
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 1f));
        m.SetFloat("_UnderlayOffsetX", 0.7f);
        m.SetFloat("_UnderlayOffsetY", -0.7f);
        m.SetFloat("_UnderlayDilate", 0.8f);
        m.SetFloat("_UnderlaySoftness", 0.25f);
        m.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 1f));
        m.SetFloat("_OutlineWidth", 0.24f);
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return m;
    }
}
