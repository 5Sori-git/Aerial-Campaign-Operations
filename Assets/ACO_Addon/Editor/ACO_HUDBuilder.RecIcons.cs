using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

/// 칭호 아이콘 TMP 스프라이트(6-341): aco_icon_rec_* 12개를 한 장(4 × 3칸, 칸 256)으로 묶고 TMP 스프라이트 묶음을 만든다.
/// 글자 안에 `<sprite name="rec_kills" tint=1>`로 끼운다(색은 바깥 <color> 태그) — 머리 위 이름표 · 로스터의 칭호 앞 아이콘.
public partial class ACO_HUDBuilder
{
    public const string RecIconAtlasPath = "Assets/ACO_Addon/Textures/Icons/aco_rec_icons.png";
    public const string RecIconSpriteAssetPath = "Assets/ACO_Addon/Textures/Icons/aco_rec_icons.asset";
    public static readonly string[] RecIconNames =
    {
        "rec_level", "rec_clears", "rec_clears5", "rec_kills", "rec_flight", "rec_cicbest", "rec_cickills", "rec_boss",
        "rec_boss_kill", "rec_boss_s", "rec_boss_solo", "rec_boss_party",
        "tab_sp_aam", "tab_sp_agm", "tab_sp_bomb", "tab_airframe", "tab_missile", "rec_role_multirole", "rec_role_arsenal",   // 전문 분야
    };

    /// 아틀라스 PNG + TMP 스프라이트 묶음을 (다시) 만든다. 이미 있으면 그 에셋을 고쳐 쓴다(참조가 끊기지 않게).
    public static TMP_SpriteAsset CreateRecIconSpriteAsset()
    {
        const int cell = 256, cols = 5;
        int rows = (RecIconNames.Length + cols - 1) / cols;
        var atlas = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
        var clear = new Color32[cols * cell * rows * cell];
        atlas.SetPixels32(clear);
        var rects = new GlyphRect[RecIconNames.Length];
        for (int i = 0; i < RecIconNames.Length; i++)
        {
            string src = $"{IconDir}/aco_icon_{RecIconNames[i]}.png";
            if (!File.Exists(src)) { Debug.LogError($"[ACO] 칭호 아이콘이 없습니다: {src} — Tools/aco_icons.py로 만드세요."); return null; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(src));
            int col = i % cols, row = i / cols;
            int x0 = col * cell, y0 = (rows - 1 - row) * cell;   // 텍스처는 아래가 0
            atlas.SetPixels(x0, y0, cell, cell, t.GetPixels(0, 0, Mathf.Min(cell, t.width), Mathf.Min(cell, t.height)));
            Object.DestroyImmediate(t);
            rects[i] = new GlyphRect(x0, y0, cell, cell);
        }
        atlas.Apply();
        File.WriteAllBytes(RecIconAtlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(RecIconAtlasPath, ImportAssetOptions.ForceSynchronousImport);
        // 한 장 텍스처(스프라이트로 나누지 않는다 — TMP는 칸 위치(glyphRect)로 그린다. 나누면 칸을 더할 때 내부 ID가 겹쳤다)
        var imp = (TextureImporter)AssetImporter.GetAtPath(RecIconAtlasPath);
        imp.textureType = TextureImporterType.Default;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Trilinear;
        imp.maxTextureSize = 2048;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(RecIconAtlasPath);

        TMP_SpriteAsset asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(RecIconSpriteAssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(asset, RecIconSpriteAssetPath);
        }
        asset.spriteSheet = sheet;
        asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);
        // 새로 만든 에셋은 목록이 아직 없다(TMP가 처음 불러올 때 만든다) — 직접 만든다
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        if (asset.spriteGlyphTable == null) { typeof(TMP_SpriteAsset).GetField("m_SpriteGlyphTable", flags)?.SetValue(asset, new System.Collections.Generic.List<TMP_SpriteGlyph>()); }
        if (asset.spriteCharacterTable == null) { typeof(TMP_SpriteAsset).GetField("m_SpriteCharacterTable", flags)?.SetValue(asset, new System.Collections.Generic.List<TMP_SpriteCharacter>()); }
        var glyphs = asset.spriteGlyphTable;
        var chars = asset.spriteCharacterTable;
        glyphs.Clear();
        chars.Clear();
        for (int i = 0; i < rects.Length; i++)
        {
            // 글자 높이에 맞춘 정사각 아이콘: 기준선 아래로 조금 내려 대문자 가운데에 오게, 오른쪽에 여백
            var g = new TMP_SpriteGlyph
            {
                index = (uint)i,
                metrics = new GlyphMetrics(cell, cell, 0f, cell * 0.82f, cell * 1.12f),
                glyphRect = rects[i],
                scale = 1f,
            };
            glyphs.Add(g);
            var c = new TMP_SpriteCharacter(0xFFFE, g) { name = RecIconNames[i], scale = 1f };
            chars.Add(c);
        }
        if (asset.material == null)
        {
            var mat = new Material(Shader.Find("TextMeshPro/Sprite"));
            mat.name = "aco_rec_icons Material";
            mat.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(mat, asset);
            asset.material = mat;
        }
        asset.material.SetTexture(ShaderUtilities.ID_MainTex, sheet);
        asset.UpdateLookupTables();
        EditorUtility.SetDirty(asset);
        EditorUtility.SetDirty(asset.material);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 칭호 아이콘 TMP 스프라이트 {chars.Count}개를 만들었습니다: {RecIconSpriteAssetPath}");
        return asset;
    }

    /// 있으면 불러오고, 없으면 만든다.
    public static TMP_SpriteAsset RecIconSpriteAsset()
    {
        TMP_SpriteAsset a = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(RecIconSpriteAssetPath);
        return a != null ? a : CreateRecIconSpriteAsset();
    }
}
