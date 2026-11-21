using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ACO;

/// 조종사 기록판(6-341): 크레딧 보드와 같은 판(사물함 폭 0.62m × 0.95m — 캔버스 1240 × 1900 × 0.0005, 앞면 = 루트 +Z).
/// 위: 제목 · 기록 칸(아이콘 2열 × 4줄) · 장착 중 칭호(아이콘). 아래: 칭호 목록 스크롤(끌기 · 스크롤바 · ▲▼) — 줄마다 버튼(ACO_PilotRecordRow), 누르면 장착 · 해제.
/// 스크립트가 쓰는 글자는 오브젝트 이름이 번역 키가 아니다(Bake 라벨 번역에 걸리지 않게). 선원실 크레딧 보드 옆 사물함에 Setup Check가 놓는다.
public partial class ACO_HUDBuilder
{
    public const string PilotRecordPrefabName = "ACO_PilotRecord";
    public const int PilotRecordRows = 104;   // 목록 줄(분류 제목 18 + 칭호 78 = 96 — 여유)
    private const float PilotRowH = 96f;

    public static void CreatePilotRecordPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath}"); return; }

        GameObject root = new GameObject(PilotRecordPrefabName);
        ACO_PilotRecord rec = root.AddUdonSharpComponent<ACO_PilotRecord>();
        RectTransform canvas = MakeUiPanel(root.transform, CreditW, CreditH);
        canvas.localScale = Vector3.one * CreditUnit;
        canvas.Find("Background").GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.04f, 1f);
        Color frame = new Color(0.32f, 0.36f, 0.34f, 1f);
        float t = 14f;
        MakeUiBox(canvas, "Frame_T", new Vector2(0f, CreditH * 0.5f - t * 0.5f), new Vector2(CreditW, t), frame);
        MakeUiBox(canvas, "Frame_B", new Vector2(0f, -CreditH * 0.5f + t * 0.5f), new Vector2(CreditW, t), frame);
        MakeUiBox(canvas, "Frame_L", new Vector2(-CreditW * 0.5f + t * 0.5f, 0f), new Vector2(t, CreditH), frame);
        MakeUiBox(canvas, "Frame_R", new Vector2(CreditW * 0.5f - t * 0.5f, 0f), new Vector2(t, CreditH), frame);
        LocLabel(MakeUiText(canvas, "Title", new Vector2(0f, 865f), new Vector2(1180f, 100f), 64f, font, ""), "record.title");

        // 기록 칸(6-341 아이콘): 2열 × 4줄 — 아이콘 · 이름(번역 라벨) · 값(스크립트)
        string[] statIcons = { "rec_level", "rec_clears", "rec_clears5", "rec_kills", "rec_flight", "rec_cicbest", "rec_cickills" };
        string[] statKeys = { "record.s.level", "record.s.clears", "record.s.clears5", "record.s.kills", "record.s.flight", "record.s.cicbest", "record.s.cickills" };
        var statValues = new TextMeshProUGUI[statIcons.Length];
        for (int k = 0; k < statIcons.Length; k++)
        {
            float cx = k % 2 == 0 ? -295f : 295f;
            float cy = 772f - (k / 2) * 64f;
            Image ic = MakeIcon(canvas, "StatIcon_" + k, LoadIcon(statIcons[k]), new Vector2(cx - 255f, cy), 52f);
            ic.color = new Color(0.35f, 1f, 0.45f, 1f);
            TextMeshProUGUI lab = LocLabel(MakeUiText(canvas, statKeys[k], new Vector2(cx - 55f, cy), new Vector2(290f, 60f), 28f, font, ""), statKeys[k]);
            lab.alignment = TextAlignmentOptions.Left;
            TextMeshProUGUI val = MakeUiText(canvas, "StatValue_" + k, new Vector2(cx + 175f, cy), new Vector2(200f, 60f), 32f, font, "-");
            val.alignment = TextAlignmentOptions.Right;
            val.enableAutoSizing = true;
            val.fontSizeMax = 32f;
            val.fontSizeMin = 20f;
            val.color = new Color(0.85f, 0.95f, 0.88f, 1f);
            statValues[k] = val;
        }
        rec.StatValues = statValues;
        // 장착 중 칭호(아이콘 + 글자)
        rec.TitleIcon = MakeIcon(canvas, "EquippedIcon", LoadIcon("rec_boss"), new Vector2(-545f, 505f), 58f);
        TextMeshProUGUI cur = MakeUiText(canvas, "Equipped", new Vector2(35f, 505f), new Vector2(1060f, 70f), 36f, font, "-");
        cur.alignment = TextAlignmentOptions.Left;
        cur.enableAutoSizing = true;
        cur.fontSizeMax = 36f;
        cur.fontSizeMin = 22f;
        rec.TitleText = cur;
        rec.GroupIcons = new Sprite[] { LoadIcon("rec_level"), LoadIcon("rec_clears"), LoadIcon("rec_clears5"), LoadIcon("rec_kills"), LoadIcon("rec_flight"), LoadIcon("rec_cicbest"), LoadIcon("rec_cickills"), LoadIcon("rec_boss") };
        rec.BossKindIcons = new Sprite[] { LoadIcon("rec_boss_kill"), LoadIcon("rec_boss_s"), LoadIcon("rec_boss_solo"), LoadIcon("rec_boss_party") };
        rec.RoleIcons = new Sprite[] { LoadIcon("tab_sp_aam"), LoadIcon("tab_sp_agm"), LoadIcon("tab_sp_bomb"), LoadIcon("tab_airframe"), LoadIcon("tab_missile"), LoadIcon("rec_role_multirole"), LoadIcon("rec_role_arsenal") };
        rec.RoleHeaderIcon = LoadIcon("upgrade");

        // 스크롤 창: 위 455 ~ 아래 -790, 오른쪽 50은 스크롤바
        GameObject viewGo = new GameObject("ScrollView", typeof(RectTransform));
        RectTransform view = viewGo.GetComponent<RectTransform>();
        view.SetParent(canvas, false);
        view.anchoredPosition = new Vector2(0f, -167.5f);
        view.sizeDelta = new Vector2(1180f, 1245f);
        ScrollRect scroll = viewGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 60f;

        GameObject portGo = new GameObject("Viewport", typeof(RectTransform));
        RectTransform port = portGo.GetComponent<RectTransform>();
        port.SetParent(view, false);
        port.anchorMin = Vector2.zero;
        port.anchorMax = Vector2.one;
        port.offsetMin = Vector2.zero;
        port.offsetMax = new Vector2(-50f, 0f);
        portGo.AddComponent<RectMask2D>();
        Image catcher = portGo.AddComponent<Image>();   // 빈 곳을 잡아도 끌린다
        catcher.color = new Color(0f, 0f, 0f, 0f);

        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        RectTransform content = contentGo.GetComponent<RectTransform>();
        content.SetParent(port, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, -PilotRowH * PilotRecordRows);
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        ContentSizeFitter fit = contentGo.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var rows = new GameObject[PilotRecordRows];
        var backs = new Image[PilotRecordRows];
        var names = new TextMeshProUGUI[PilotRecordRows];
        var progs = new TextMeshProUGUI[PilotRecordRows];
        var conds = new TextMeshProUGUI[PilotRecordRows];
        var rowIcons = new Image[PilotRecordRows];
        for (int i = 0; i < PilotRecordRows; i++)
        {
            GameObject rowGo = new GameObject("Row_" + i, typeof(RectTransform));
            RectTransform rr = rowGo.GetComponent<RectTransform>();
            rr.SetParent(content, false);
            Image back = rowGo.AddComponent<Image>();
            back.color = new Color(0.06f, 0.12f, 0.08f, 1f);
            Button b = rowGo.AddComponent<Button>();
            b.targetGraphic = back;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            LayoutElement le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = PilotRowH;
            le.minHeight = PilotRowH;
            ACO_PilotRecordRow click = rowGo.AddUdonSharpComponent<ACO_PilotRecordRow>();
            click.Record = rec;
            click.Index = i;
            WireClick(b, click);

            names[i] = RowText(rr, "Name", new Vector2(0f, 0.45f), new Vector2(0.74f, 1f), 34f, font, TextAlignmentOptions.Left);
            progs[i] = RowText(rr, "Progress", new Vector2(0.74f, 0.45f), new Vector2(1f, 1f), 28f, font, TextAlignmentOptions.Right);
            conds[i] = RowText(rr, "Condition", new Vector2(0f, 0f), new Vector2(1f, 0.5f), 24f, font, TextAlignmentOptions.Left);
            // 왼쪽 아이콘(등급 색) — 이름 · 조건 글자는 그 오른쪽부터
            Image ri = MakeIcon(rr, "Icon", null, Vector2.zero, 64f);
            ri.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            ri.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            ri.rectTransform.anchoredPosition = new Vector2(46f, 0f);
            rowIcons[i] = ri;
            names[i].rectTransform.offsetMin = new Vector2(96f, 2f);
            conds[i].rectTransform.offsetMin = new Vector2(96f, 2f);
            conds[i].color = new Color(0.55f, 0.7f, 0.6f, 1f);
            rows[i] = rowGo;
            backs[i] = back;
            rowGo.SetActive(i < 4);   // 실행 중에 쓰는 만큼 켠다
        }

        // 스크롤바(오른쪽 30)
        Image track = MakeUiBox(view, "Scrollbar", new Vector2(575f, 0f), new Vector2(30f, 1245f), new Color(0.35f, 1f, 0.45f, 0.12f));
        track.raycastTarget = true;
        GameObject areaGo = new GameObject("Sliding Area", typeof(RectTransform));
        RectTransform area = areaGo.GetComponent<RectTransform>();
        area.SetParent(track.rectTransform, false);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(0f, 6f);
        area.offsetMax = new Vector2(0f, -6f);
        Image handle = MakeUiBox(area, "Handle", Vector2.zero, Vector2.zero, new Color(0.35f, 1f, 0.45f, 0.55f));
        handle.raycastTarget = true;
        handle.rectTransform.anchorMin = Vector2.zero;
        handle.rectTransform.anchorMax = Vector2.one;
        handle.rectTransform.offsetMin = Vector2.zero;
        handle.rectTransform.offsetMax = Vector2.zero;
        Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        var barNav = bar.navigation;
        barNav.mode = Navigation.Mode.None;
        bar.navigation = barNav;

        scroll.content = content;
        scroll.viewport = port;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        // ▲ ▼ 버튼(반 화면씩)
        string[] icons = { "chevron_up", "chevron_down" };
        float[] xs = { -170f, 170f };
        for (int i = 0; i < 2; i++)
        {
            Image img = MakeUiButton(canvas, i == 0 ? "Btn_Up" : "Btn_Down", new Vector2(xs[i], -845f), new Vector2(300f, 90f), font, "", 60f);
            MakeIcon(img.rectTransform, "Icon", LoadIcon(icons[i]), Vector2.zero, 70f);
            ACO_PilotRecordRow btn = img.gameObject.AddUdonSharpComponent<ACO_PilotRecordRow>();
            btn.Record = rec;
            btn.Index = i == 0 ? -1 : -2;
            WireClick(img.GetComponent<Button>(), btn);
        }

        rec.Scroll = scroll;
        rec.Viewport = port;
        rec.Content = content;
        rec.Rows = rows;
        rec.RowBacks = backs;
        rec.RowNames = names;
        rec.RowProgress = progs;
        rec.RowConditions = conds;
        rec.RowIcons = rowIcons;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{PilotRecordPrefabName}.prefab");
        ACO_PrefabVersions.Stamp(PilotRecordPrefabName);
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 조종사 기록판 프리팹을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)} — Setup Check가 선원실 업데이트 노트 보드 옆 사물함에 놓습니다.");
    }

    /// 줄 안 글자(앵커로 칸을 나눈다).
    private static TextMeshProUGUI RowText(RectTransform row, string name, Vector2 min, Vector2 max, float size, TMP_FontAsset font, TextAlignmentOptions align)
    {
        TextMeshProUGUI tx = MakeUiText(row, name, Vector2.zero, Vector2.zero, size, font, "");
        RectTransform r = tx.rectTransform;
        r.anchorMin = min;
        r.anchorMax = max;
        r.offsetMin = new Vector2(16f, 2f);
        r.offsetMax = new Vector2(-16f, -2f);
        tx.alignment = align;
        tx.overflowMode = TextOverflowModes.Ellipsis;
        tx.enableAutoSizing = true;
        tx.fontSizeMax = size;
        tx.fontSizeMin = size * 0.6f;
        return tx;
    }
}
