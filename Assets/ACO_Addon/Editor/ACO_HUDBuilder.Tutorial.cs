using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;
using TMPro;
using ACO;

/// 튜토리얼(6-160, 브리핑 판으로 옮김 — 6-164): 브리핑 판 머리줄의 튜토리얼 버튼 + 그 위 홀로그램 화살표 + 판을 덮는 5페이지 팝업(ACO_Tutorial 프리팹)과 함내 사진 촬영.
/// 글자는 아래 표(TutorialTexts) 한 곳에 키별로 모은다 — 글자 오브젝트 이름 = 키. 언어를 바꿀 때는 이 표만 바꾼다.
/// 한글 폰트(TutorialFontPath)가 없으면 기본 폰트로 만들고 경고한다(한글이 네모로 나온다).
public static partial class ACO_HUDBuilder
{
    public const string TutorialPrefabName = "ACO_Tutorial";
    public const string TutorialFontPath = "Assets/ACO_Addon/Fonts/ACO_Pretendard SDF.asset";
    public const string TutorialPhotoDir = "Assets/ACO_Addon/Textures/Tutorial";
    public static readonly string[] TutorialMapPhotos = { "briefing", "cic", "corridor", "berth", "hangar", "deck" };

    private const float TutW = 1600f;
    private const float TutH = 1000f;
    // 팝업 = 브리핑 판(작전 콘솔 캔버스 2400 x 1500, 배율 0.001225 = 2.94 x 1.84m)과 같은 크기 — 비율이 같아(16:10) 판을 꼭 맞게 덮는다.
    // 콘솔 판 크기를 바꾸면 이 값과 버튼 자리(TutButtonPos · TutButtonSize)를 함께 고친다.
    private const float TutScale = 2.94f / 1600f;
    private const float TutPopupGap = 0.04f;        // 판 앞으로 띄우는 거리(m)
    private const float BoardUnit = 0.001225f;      // 콘솔 캔버스 한 단위(m)
    private static readonly Vector2 TutButtonPos = new Vector2(840f, 600f);    // 콘솔 캔버스 좌표(가운데 원점, 오른쪽 · 위 +) — 머리줄 오른쪽 빈칸
    private static readonly Vector2 TutButtonSize = new Vector2(520f, 130f);
    private static readonly Color TutBright = new Color(0.37f, 0.88f, 0.56f, 1f);
    private static readonly Color TutBody = new Color(0.78f, 0.94f, 0.83f, 1f);
    private static readonly Color TutDim = new Color(0.5f, 0.75f, 0.58f, 1f);
    private static readonly Color TutCard = new Color(0.05f, 0.12f, 0.08f, 1f);
    private static readonly Color TutLine = new Color(0.12f, 0.27f, 0.18f, 1f);

    // 키 → 글자: 한국어 · 영어 · 일본어(6-165). 글자 오브젝트 이름 = 키 — 생성기가 오브젝트와 세 언어 문장을 스크립트에 넣고 실행 중 바꿔 끼운다.
    // 언어를 늘리면 칸을 하나 더하고 ACO_TutorialPopup의 언어 번호 · 버튼을 함께 늘린다.
    private static readonly string[,] TutorialTexts =
    {
        { "tut.button", "?  튜토리얼", "?  TUTORIAL", "?  チュートリアル" },
        { "tut.p1.title", "1 / 5   개요", "1 / 5   Overview", "1 / 5   概要" },
        { "tut.p1.name", "AERIAL CAMPAIGN OPERATIONS", "AERIAL CAMPAIGN OPERATIONS", "AERIAL CAMPAIGN OPERATIONS" },
        { "tut.p1.tagline", "최대 4명이 함께하는 협동 공중전", "Co-op air combat for up to 4 pilots", "最大4人で挑む協力空戦" },
        { "tut.p1.c1.title", "매번 새로운 전장", "Fresh Battlefields", "毎回新しい戦場" },
        { "tut.p1.c1.body", "미션마다 섬 지형이 새로 만들어집니다. 군도, 협곡, 환초, 화산섬처럼 매번 다른 전장", "Island terrain is generated fresh for every mission: archipelagos, canyons, atolls, volcanic islands and more", "ミッションごとに島の地形が新しく生成されます。群島、峡谷、環礁、火山島など毎回違う戦場" },
        { "tut.p1.c2.title", "다양한 적과 작전", "Varied Missions", "多彩な敵と作戦" },
        { "tut.p1.c2.body", "제공권 장악, 호위, 상륙전, 함대전부터 공중 요새, 육상전함, 에이스 편대까지", "From air superiority, escorts, landings and fleet battles to flying fortresses, land battleships and ace squadrons", "制空権の確保、護衛、上陸戦、艦隊戦から空中要塞、陸上戦艦、エース編隊まで" },
        { "tut.p1.c3.title", "캠페인과 난이도", "Campaigns & Difficulty", "キャンペーンと難易度" },
        { "tut.p1.c3.body", "작전 콘솔에서 연속 임무 수를 고릅니다. 길게 이어갈수록 플레이 시간이 늘고 강한 적이 나올 확률이 높아집니다", "Choose how many missions to chain on the operations console. Longer campaigns take more time and bring stronger enemies more often", "作戦コンソールで連続ミッション数を選びます。長く続けるほどプレイ時間が延び、強敵の出現率が上がります" },
        { "tut.p1.c4.title", "크레딧과 성장", "Credits & Upgrades", "クレジットと強化" },
        { "tut.p1.c4.body", "보상으로 다양한 특수무장을 해금하고 기체와 무장을 강화합니다. 출격 중에 부를 수 있는 지원도 있습니다", "Spend rewards to unlock special weapons and upgrade your aircraft and weapons. You can also call in support mid-sortie", "報酬で様々な特殊兵装を解放し、機体と兵装を強化します。出撃中に呼べる支援もあります" },

        { "tut.p2.title", "2 / 5   출격 방법", "2 / 5   How to Sortie", "2 / 5   出撃の手順" },
        { "tut.p2.s1.label", "참가", "Join", "参加" },
        { "tut.p2.s1.text", "정면 브리핑 콘솔에서 빈 칸(JOIN)을 누르세요", "Tap an open slot (JOIN) on the briefing console in front", "正面のブリーフィングコンソールで空き枠(JOIN)を押します" },
        { "tut.p2.s2.label", "무장", "Arm", "兵装" },
        { "tut.p2.s2.text", "오른쪽 벽 로드아웃 패널에서 특수무장, 지원 호출, 강화를 고르세요", "Pick special weapons, support and upgrades on the LOADOUT panel (right wall)", "右壁のロードアウトパネルで特殊兵装、支援要請、強化を選びます" },
        { "tut.p2.s3.label", "준비", "Ready", "準備" },
        { "tut.p2.s3.text", "모두 READY를 누르면 10초 카운트다운이 시작됩니다", "When everyone presses READY, a 10-second countdown starts", "全員がREADYを押すと10秒のカウントダウンが始まります" },
        { "tut.p2.s4.label", "출격", "Launch", "出撃" },
        { "tut.p2.s4.text", "모든 조종사가 비행갑판에서 함께 출격합니다", "All pilots launch together from the flight deck", "全パイロットが飛行甲板から一緒に出撃します" },
        { "tut.p2.tip", "작전 콘솔 맨 위에 지금 할 일이 표시됩니다", "The top of the operations console shows what to do next", "作戦コンソールの一番上に今やることが表示されます" },
        { "tut.p2.tip2", "격추되면 로드아웃 패널의 LAUNCH로 다시 출격합니다", "If you are shot down, press LAUNCH on the LOADOUT panel to sortie again", "撃墜されたらロードアウトパネルのLAUNCHで再出撃します" },

        { "tut.p3.title", "3 / 5   비행 중", "3 / 5   In Flight", "3 / 5   飛行中" },
        { "tut.p3.desktop", "데스크톱", "Desktop", "デスクトップ" },
        { "tut.p3.d1", "스로틀  <color=#F0FFF4><b>Shift / Ctrl</b></color>", "Throttle  <color=#F0FFF4><b>Shift / Ctrl</b></color>", "スロットル  <color=#F0FFF4><b>Shift / Ctrl</b></color>" },
        { "tut.p3.d2", "피치·롤  <color=#F0FFF4><b>W A S D</b></color>    요  <color=#F0FFF4><b>Q E</b></color>", "Pitch/Roll  <color=#F0FFF4><b>W A S D</b></color>    Yaw  <color=#F0FFF4><b>Q E</b></color>", "ピッチ・ロール  <color=#F0FFF4><b>W A S D</b></color>    ヨー  <color=#F0FFF4><b>Q E</b></color>" },
        { "tut.p3.d3", "<color=#F0FFF4><b>1</b></color> 기관포    <color=#F0FFF4><b>2</b></color> 미사일", "<color=#F0FFF4><b>1</b></color> Gun    <color=#F0FFF4><b>2</b></color> Missiles", "<color=#F0FFF4><b>1</b></color> 機関砲    <color=#F0FFF4><b>2</b></color> ミサイル" },
        { "tut.p3.d4", "<color=#F0FFF4><b>3</b></color> 특수무장    <color=#F0FFF4><b>4</b></color> 지원 호출", "<color=#F0FFF4><b>3</b></color> Special    <color=#F0FFF4><b>4</b></color> Support", "<color=#F0FFF4><b>3</b></color> 特殊兵装    <color=#F0FFF4><b>4</b></color> 支援要請" },
        { "tut.p3.d5", "발사  <color=#F0FFF4><b>Space</b></color>    플레어  <color=#F0FFF4><b>X</b></color>", "Fire  <color=#F0FFF4><b>Space</b></color>    Flares  <color=#F0FFF4><b>X</b></color>", "発射  <color=#F0FFF4><b>Space</b></color>    フレア  <color=#F0FFF4><b>X</b></color>" },
        { "tut.p3.d6", "내리기  <color=#F0FFF4><b>Enter</b></color> 두 번", "Exit  <color=#F0FFF4><b>Enter</b></color> twice", "降機  <color=#F0FFF4><b>Enter</b></color> を2回" },
        { "tut.p3.vr", "VR", "VR", "VR" },
        { "tut.p3.v1", "왼손 그립 = 스로틀 (손을 앞뒤로)", "Left grip = throttle (move hand fore/aft)", "左グリップ = スロットル(手を前後に)" },
        { "tut.p3.v2", "오른손 그립 = 조종간 (손을 기울이기)", "Right grip = stick (tilt your hand)", "右グリップ = 操縦桿(手を傾ける)" },
        { "tut.p3.v3", "오른쪽 다이얼: 기관포, 미사일, 특수무장, 지원", "Right dial: gun, missiles, special, support", "右ダイヤル：機関砲、ミサイル、特殊兵装、支援" },
        { "tut.p3.v4", "왼쪽 다이얼: 플랩, 플레어, 브레이크, 기어", "Left dial: flaps, flares, brake, gear", "左ダイヤル：フラップ、フレア、ブレーキ、ギア" },
        { "tut.p3.v5", "스틱으로 고르고 트리거로 사용", "Select with the stick, use with the trigger", "スティックで選び、トリガーで使用" },
        { "tut.p3.v6", "점프 한 번 = 좌석 높이 맞추기, 두 번 = 내리기", "Jump once = adjust seat height, twice = exit", "ジャンプ1回 = 座席の高さ調整、2回 = 降機" },
        { "tut.p3.n1", "미사일·특수무장: 기수 앞의 적을 자동으로 조준합니다. 락온 마커가 빨갛게 바뀌면 발사하세요. 쏜 칸은 몇 초 뒤 다시 장전됩니다", "Missiles and special weapons aim at enemies ahead of your nose automatically. Fire when the lock-on marker turns red. Fired slots reload after a few seconds", "ミサイル・特殊兵装：機首前方の敵を自動で狙います。ロックオンマーカーが赤くなったら発射。撃った枠は数秒後に再装填されます" },
        { "tut.p3.n2", "지원 호출: 로드아웃 패널에서 고른 지원을 부릅니다. 위치가 필요한 지원은 바라보는 곳을 지정합니다", "Support: calls the support picked on the LOADOUT panel. Support that needs a location targets where you are looking", "支援要請：ロードアウトパネルで選んだ支援を呼びます。位置が必要な支援は視線の先を指定します" },
        { "tut.p3.n3", "캐터펄트: 노란 옷 유도요원의 신호를 따라 캐터펄트에 오르고, 사출장교가 신호하면 스로틀을 끝까지(애프터버너) 올리세요. 점검이 끝나면 사출됩니다(설정에서 갑판 요원을 끄면 풀스로틀 2초)", "Catapult: follow the yellow-shirt director onto the catapult. When the shooter signals, push the throttle all the way (afterburner). You launch after the final check (with deck crew off in Settings: full throttle for 2 s)", "カタパルト：黄色い服の誘導員の合図でカタパルトに乗り、射出士官の合図でスロットルを最大(アフターバーナー)に。点検後に射出されます(設定で甲板要員をオフにすると全開2秒)" },
        { "tut.p3.n4", "착함하지 않아도 미션이 끝나면 브리핑실로 돌아옵니다", "No need to land: when the mission ends, you return to the briefing room", "着艦しなくても、ミッションが終わるとブリーフィングルームに戻ります" },

        { "tut.p4.title", "4 / 5   크레딧과 강화", "4 / 5   Credits and Upgrades", "4 / 5   クレジットと強化" },
        { "tut.p4.r1", "미션이 끝나면 보상을 받습니다: 팀 점수의 절반 + 내 격추 점수", "Rewards are paid when a mission ends: half the team score + your kill score", "ミッション終了時に報酬を獲得：チームスコアの半分 + 自分の撃墜スコア" },
        { "tut.p4.r2", "격추되거나 함선 밖에서 조종석을 내리면 보상이 줄어듭니다", "Getting shot down or leaving the cockpit away from the ship reduces your reward", "撃墜されたり、艦の外で降機すると報酬が減ります" },
        { "tut.p4.r3", "로드아웃 패널에서 특수무장 해금, 기체와 무장 강화, 장비(미션 1회 적용), 지원 호출권(쓸 때까지 보관)", "On the LOADOUT panel: unlock special weapons, upgrade aircraft and weapons, buy equipment (lasts one mission) and support tickets (kept until used)", "ロードアウトパネルで特殊兵装の解放、機体と兵装の強化、装備(ミッション1回分)、支援要請券(使うまで保管)" },
        { "tut.p4.r4", "진행은 VRChat 계정에 저장되어 다음에도 이어집니다", "Progress is saved to your VRChat account and carries over", "進行状況はVRChatアカウントに保存され、次回も引き継がれます" },

        { "tut.p5.title", "5 / 5   함내 지도", "5 / 5   Ship Map", "5 / 5   艦内マップ" },
        { "tut.p5.briefing", "브리핑실 · 지금 위치", "Briefing · You are here", "ブリーフィング · 現在地" },
        { "tut.p5.cic", "CIC · 관전 화면", "CIC · Spectator screens", "CIC · 観戦画面" },
        { "tut.p5.corridor", "복도 · 하이스코어", "Corridor · High scores", "廊下 · ハイスコア" },
        { "tut.p5.berth", "선원실 · 설정", "Crew quarters · Settings", "船員室 · 設定" },
        { "tut.p5.hangar", "격납고 · 커스터마이징", "Hangar · Customization", "格納庫 · カスタマイズ" },
        { "tut.p5.deck", "비행갑판 · 관전", "Flight deck · Spectate", "飛行甲板 · 観戦" },
        { "tut.p5.note", "격납고와 비행갑판은 복도 끝 문을 눌러(Interact) 이동합니다", "Reach the hangar and flight deck by pressing (Interact) the doors at the end of the corridor", "格納庫と飛行甲板へは廊下の端のドアを押して(Interact)移動します" },
    };

    // ───────────── 폰트 (Pretendard — SIL OFL 1.1, Fonts/Pretendard-OFL.txt) ─────────────
    // 범용 패널 폰트(튜토리얼과 앞으로 한글을 쓰는 패널이 같이 쓴다): 동적 TMP 폰트 — 어떤 글자든 쓸 때 아틀라스에 그린다.
    // 빌드에는 TTF가 들어가고, 에디터에서 채운 글자는 빌드 때 지운다(m_ClearDynamicDataOnBuild — 실행 중 다시 그린다).
    // 한글 폰트가 기본, 일본어(Pretendard JP)는 그 보조 폰트(Fallback).
    public const string PanelFontPath = TutorialFontPath;
    public const string TutorialFontSource = "Assets/ACO_Addon/Fonts/Pretendard-Medium.ttf";
    public const string TutorialJpFontPath = "Assets/ACO_Addon/Fonts/ACO_PretendardJP SDF.asset";
    public const string TutorialJpFontSource = "Assets/ACO_Addon/Fonts/PretendardJP-Medium.ttf";

    /// 범용 폰트가 준비됐는가: 한글 · 일본어 폰트가 있고, 둘 다 동적이고, 일본어가 한글의 보조 폰트다.
    public static bool PanelFontsReady()
    {
        TMP_FontAsset kr = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TutorialFontPath);
        TMP_FontAsset jp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TutorialJpFontPath);
        return kr != null && jp != null && kr.atlasPopulationMode == AtlasPopulationMode.Dynamic && jp.atlasPopulationMode == AtlasPopulationMode.Dynamic
            && kr.fallbackFontAssetTable != null && kr.fallbackFontAssetTable.Contains(jp);
    }

    /// 한글(Pretendard) · 일본어(Pretendard JP, 보조) 동적 TMP 폰트를 만들거나 맞춘다.
    public static void EnsureTutorialFonts()
    {
        TMP_FontAsset kr = EnsureDynamicFont(TutorialFontPath, TutorialFontSource, "ACO_Pretendard");
        TMP_FontAsset jp = EnsureDynamicFont(TutorialJpFontPath, TutorialJpFontSource, "ACO_PretendardJP");
        if (kr != null && jp != null)
        {
            if (kr.fallbackFontAssetTable == null) { kr.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>(); }
            if (!kr.fallbackFontAssetTable.Contains(jp)) { kr.fallbackFontAssetTable.Add(jp); EditorUtility.SetDirty(kr); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log(PanelFontsReady() ? "[ACO] 범용 패널 폰트(Pretendard 한글 + 일본어 보조, 동적)를 맞췄습니다." : "[ACO] 범용 패널 폰트를 다 맞추지 못했습니다 — Fonts의 TTF를 확인하세요.");
    }

    private static TMP_FontAsset EnsureDynamicFont(string path, string sourcePath, string name)
    {
        TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (asset == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null) { Debug.LogWarning($"[ACO] 폰트 파일이 없습니다: {sourcePath}"); return null; }
            asset = TMP_FontAsset.CreateFontAsset(source, 64, 7, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (asset == null) { Debug.LogWarning($"[ACO] 폰트를 만들지 못했습니다: {sourcePath}"); return null; }
            asset.name = name;
            AssetDatabase.CreateAsset(asset, path);
            // 아틀라스와 머티리얼은 폰트 에셋 안에 넣어야 저장된다
            asset.atlasTexture.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
        }
        if (asset.atlasPopulationMode != AtlasPopulationMode.Dynamic) { asset.atlasPopulationMode = AtlasPopulationMode.Dynamic; }
        var so = new SerializedObject(asset);
        SerializedProperty clear = so.FindProperty("m_ClearDynamicDataOnBuild");
        if (clear != null && !clear.boolValue) { clear.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
        EditorUtility.SetDirty(asset);
        return asset;
    }

    /// 키의 글자(lang: 0 한국어, 1 영어, 2 일본어). 만들 때는 한국어로 넣고, 실행 중 ACO_TutorialPopup이 언어에 맞게 바꾼다.
    private static string Tut(string key, int lang = 0)
    {
        for (int i = 0; i < TutorialTexts.GetLength(0); i++) { if (TutorialTexts[i, 0] == key) { return TutorialTexts[i, 1 + lang]; } }
        Debug.LogWarning($"[ACO] 튜토리얼 글자 키가 없습니다: {key}");
        return key;
    }

    private static bool TutHasKey(string key)
    {
        for (int i = 0; i < TutorialTexts.GetLength(0); i++) { if (TutorialTexts[i, 0] == key) { return true; } }
        return false;
    }

    /// 프리팹 안의 글자 오브젝트(이름 = 키)를 모아 세 언어 문장과 함께 스크립트에 넣는다.
    private static void BindTutorialLanguages(GameObject root, ACO_TutorialPopup pop)
    {
        var texts = new System.Collections.Generic.List<TextMeshProUGUI>();
        foreach (TextMeshProUGUI t in root.GetComponentsInChildren<TextMeshProUGUI>(true)) { if (TutHasKey(t.name)) { texts.Add(t); } }
        pop.LocTexts = texts.ToArray();
        pop.LocKo = new string[texts.Count];
        pop.LocEn = new string[texts.Count];
        pop.LocJa = new string[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            pop.LocKo[i] = Tut(texts[i].name, 0);
            pop.LocEn[i] = Tut(texts[i].name, 1);
            pop.LocJa[i] = Tut(texts[i].name, 2);
        }
    }

    private static TMP_FontAsset TutorialFont()
    {
        TMP_FontAsset kr = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TutorialFontPath);
        if (kr != null) { return kr; }
        Debug.LogWarning($"[ACO] 튜토리얼: 한글 폰트({TutorialFontPath})가 없어 기본 폰트로 만듭니다 — 한글이 네모로 나옵니다. 폰트를 만든 뒤 Setup Check [고치기]로 다시 만드세요.");
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
    }

    /// 튜토리얼 프리팹(ACO_Tutorial): 루트 = 브리핑 판(작전 콘솔 캔버스) 가운데, +Z = 방 쪽. Setup Check가 판 자리에 놓는다.
    /// 자식: 판 머리줄 오른쪽 튜토리얼 버튼(OpenButton), 그 위 홀로그램 화살표(Marker), 판을 덮는 팝업(Popup).
    public static void CreateTutorialPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        EnsureFolder("Assets/ACO_Addon", "Generated");
        TMP_FontAsset font = TutorialFont();
        if (font == null) { Debug.LogError("[ACO] 튜토리얼: TMP 폰트를 찾지 못했습니다."); return; }

        GameObject root = new GameObject(TutorialPrefabName);
        ACO_TutorialPopup pop = root.AddUdonSharpComponent<ACO_TutorialPopup>();

        BuildTutorialOpenButton(root.transform, pop, font);
        BuildTutorialHologram(root.transform, pop);
        BuildTutorialPopup(root.transform, pop, font);
        BindTutorialLanguages(root, pop);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{TutorialPrefabName}.prefab");
        ACO_PrefabVersions.Stamp(TutorialPrefabName);
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 튜토리얼 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — Setup Check가 브리핑 판 자리에 놓습니다.");
        Selection.activeObject = prefab;
    }

    /// 판 캔버스 좌표(가운데 원점, 오른쪽 · 위 +)를 루트 로컬로. 방에서 보면 오른쪽 = 루트 -X(+Z가 방 쪽이라).
    private static Vector3 TutBoardPoint(Vector2 boardPos, float forward)
    {
        return new Vector3(-boardPos.x * BoardUnit, boardPos.y * BoardUnit, forward);
    }

    // 판 머리줄 오른쪽 빈칸에 붙는 튜토리얼 버튼(판 앞 3cm — 판 화면 앞면보다 앞, 팝업(4cm)보다 뒤라 열면 팝업에 가려진다)
    private static void BuildTutorialOpenButton(Transform root, ACO_TutorialPopup pop, TMP_FontAsset font)
    {
        var holder = new GameObject("OpenButton").transform;
        holder.SetParent(root, false);
        holder.localPosition = TutBoardPoint(TutButtonPos, 0.03f);
        RectTransform canvas = MakeUiPanel(holder, TutButtonSize.x, TutButtonSize.y);
        canvas.localScale = Vector3.one * BoardUnit;
        // 월드 UI는 캔버스 가운데까지 거리로 그리는 순서를 정한다 — 버튼 캔버스 가운데가 판 가운데보다 멀어 판에 덮이므로 순서를 올린다
        canvas.GetComponent<Canvas>().sortingOrder = 1;
        Image bg = canvas.Find("Background").GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);
        Image btn = MakeUiButton(canvas, "Open", Vector2.zero, TutButtonSize, font, Tut("tut.button"), 64f);
        btn.color = new Color(0.12f, 0.45f, 0.24f, 1f);
        TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>();
        label.name = "tut.button";
        label.color = new Color(0.85f, 1f, 0.9f, 1f);
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;   // 언어마다 길이가 달라(일본어 チュートリアル) 버튼 안에 맞춘다
        label.fontSizeMax = 64f;
        label.fontSizeMin = 36f;
        label.margin = new Vector4(16f, 0f, 16f, 0f);
        WireEvent(btn.GetComponent<Button>(), pop, nameof(ACO_TutorialPopup.Open));
    }

    // 홀로그램 화살표: 버튼 바로 위(판 앞 0.18m)에서 아래를 가리킨다. 천장(판 가운데 위 약 1.4m)에 닿지 않게 길이 0.42m.
    // 가산(Additive) 녹색 두 겹(속 = 진하게, 겉 = 조금 크게 옅게) + 화살 머리 위 링 두 개. 재질은 Unity 기본 셰이더(Legacy Particles/Additive).
    private static void BuildTutorialHologram(Transform root, ACO_TutorialPopup pop)
    {
        const float len = 0.42f;
        Mesh arrow = SaveMesh(TutArrowMesh(len, len * 0.22f, len * 0.62f, len * 0.44f), "ACO_TutArrow");
        Mesh ring = SaveMesh(TutRingMesh(len * 0.36f, len * 0.42f, 48), "ACO_TutRing");
        Material fill = TutHoloMaterial("ACO_TutHoloFill", new Color(0.08f, 0.5f, 0.18f, 0.45f));
        Material glow = TutHoloMaterial("ACO_TutHoloGlow", new Color(0.15f, 0.9f, 0.32f, 0.28f));

        var marker = new GameObject("Marker").transform;
        marker.SetParent(root, false);
        Vector3 tip = TutBoardPoint(TutButtonPos, 0.18f);
        tip.y += TutButtonSize.y * 0.5f * BoardUnit + 0.05f;
        marker.localPosition = tip;
        for (int k = 0; k < 3; k++)
        {
            GameObject g = TutMeshObject(marker, k == 0 ? "ArrowCore" : $"ArrowGlow{k}", arrow, k == 0 ? fill : glow);
            g.transform.localPosition = Vector3.up * (-0.01f * k);
            g.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            g.transform.localScale = Vector3.one * (1f + 0.06f * k);
        }
        var rings = new Transform[2];
        for (int k = 0; k < 2; k++)
        {
            GameObject g = TutMeshObject(marker, $"Ring{k + 1}", ring, glow);
            g.transform.localPosition = Vector3.up * (len * 0.16f + len * 0.2f * k);
            g.transform.localScale = new Vector3(1f - 0.25f * k, 1f, 1f - 0.25f * k);
            rings[k] = g.transform;
        }
        pop.Marker = marker;
        pop.Rings = rings;
    }

    private static GameObject TutMeshObject(Transform parent, string name, Mesh mesh, Material mat)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return g;
    }

    private static Material TutHoloMaterial(string name, Color tint)
    {
        Shader s = Shader.Find("Legacy Shaders/Particles/Additive");
        var m = new Material(s) { name = name };
        m.SetColor("_TintColor", tint);
        return SaveAsset(m, $"{GeneratedDir}/{name}.mat");
    }

    /// 아래를 가리키는 3D 화살표(사각 기둥 + 사각뿔). 원점 = 화살 끝, 위로 len.
    private static Mesh TutArrowMesh(float len, float shaftW, float headW, float headL)
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        float s = shaftW * 0.5f, h = headW * 0.5f;
        Vector3[] q = { new Vector3(-s, headL, -s), new Vector3(s, headL, -s), new Vector3(s, headL, s), new Vector3(-s, headL, s) };
        Vector3[] u = { new Vector3(-s, len, -s), new Vector3(s, len, -s), new Vector3(s, len, s), new Vector3(-s, len, s) };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4, b = v.Count;
            v.Add(q[i]); v.Add(q[j]); v.Add(u[j]); v.Add(u[i]);
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        { int b = v.Count; v.AddRange(u); t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 }); }
        Vector3[] hb = { new Vector3(-h, headL, -h), new Vector3(h, headL, -h), new Vector3(h, headL, h), new Vector3(-h, headL, h) };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4, b = v.Count;
            v.Add(hb[i]); v.Add(hb[j]); v.Add(Vector3.zero);
            t.AddRange(new[] { b, b + 1, b + 2 });
        }
        { int b = v.Count; v.AddRange(hb); t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }); }
        var m = new Mesh { name = "ACO_TutArrow" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// 얇은 링(양면, 높이 1cm 띠).
    private static Mesh TutRingMesh(float r0, float r1, int seg)
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            v.Add(new Vector3(Mathf.Cos(a) * r0, 0f, Mathf.Sin(a) * r0));
            v.Add(new Vector3(Mathf.Cos(a) * r1, 0.01f, Mathf.Sin(a) * r1));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2, b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        var m = new Mesh { name = "ACO_TutRing" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    private static void BuildTutorialPopup(Transform root, ACO_TutorialPopup pop, TMP_FontAsset font)
    {
        var popupRoot = new GameObject("Popup");
        popupRoot.transform.SetParent(root, false);
        popupRoot.transform.localPosition = new Vector3(0f, 0f, TutPopupGap);   // 판 바로 앞(판 전체를 덮는다)
        RectTransform canvas = MakeUiPanel(popupRoot.transform, TutW, TutH);
        canvas.localScale = Vector3.one * TutScale;
        canvas.GetComponent<Canvas>().sortingOrder = 2;   // 판(0)과 튜토리얼 버튼(1) 위에 그린다
        Image frame = MakeUiBox(canvas, "Frame", Vector2.zero, new Vector2(TutW + 8f, TutH + 8f), new Color(0.18f, 0.42f, 0.27f, 1f));
        frame.transform.SetAsFirstSibling();

        var pages = new GameObject[5];
        for (int i = 0; i < pages.Length; i++)
        {
            RectTransform p = new GameObject($"Page{i + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
            p.SetParent(canvas, false);
            p.sizeDelta = new Vector2(TutW, TutH);
            pages[i] = p.gameObject;
            TutTitle(p, $"tut.p{i + 1}.title", font);
        }
        BuildTutPage1((RectTransform)pages[0].transform, font);
        BuildTutPage2((RectTransform)pages[1].transform, font);
        BuildTutPage3((RectTransform)pages[2].transform, font);
        BuildTutPage4((RectTransform)pages[3].transform, font);
        BuildTutPage5((RectTransform)pages[4].transform, font);
        for (int i = 1; i < pages.Length; i++) { pages[i].SetActive(false); }

        // 아래 띠: X · 페이지 점 · < >
        MakeUiBox(canvas, "FooterLine", new Vector2(0f, -398f), new Vector2(1520f, 3f), TutLine);
        Image close = MakeUiButton(canvas, "Close", new Vector2(-705f, -450f), new Vector2(120f, 76f), font, "X", 46f);
        WireEvent(close.GetComponent<Button>(), pop, nameof(ACO_TutorialPopup.ClosePopup));
        // 언어는 설정 패널 LANGUAGE(6-166) — 팝업에 언어 버튼을 두지 않는다
        Image prev = MakeUiButton(canvas, "Prev", new Vector2(575f, -450f), new Vector2(120f, 76f), font, "<", 50f);
        WireEvent(prev.GetComponent<Button>(), pop, nameof(ACO_TutorialPopup.PrevPage));
        Image next = MakeUiButton(canvas, "Next", new Vector2(705f, -450f), new Vector2(120f, 76f), font, ">", 50f);
        WireEvent(next.GetComponent<Button>(), pop, nameof(ACO_TutorialPopup.NextPage));
        pop.NextButton = next.gameObject;   // 마지막 페이지에선 숨긴다(다음 = 닫기 아님 — 두 번 눌러 실수로 닫히지 않게)
        var dots = new Image[pages.Length];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i] = MakeUiBox(canvas, $"Dot{i + 1}", new Vector2((i - (dots.Length - 1) * 0.5f) * 44f, -450f), new Vector2(24f, 24f), i == 0 ? pop.DotOn : pop.DotOff);
        }

        pop.Popup = popupRoot;
        pop.Pages = pages;
        pop.Dots = dots;
        popupRoot.SetActive(false);
    }

    private static TextMeshProUGUI TutTitle(RectTransform page, string key, TMP_FontAsset font)
    {
        TextMeshProUGUI t = TutText(page, key, new Vector2(0f, 438f), new Vector2(1520f, 80f), 56f, font, TutBright, TextAlignmentOptions.Left);
        t.fontStyle = FontStyles.Bold;
        return t;
    }

    private static TextMeshProUGUI TutText(RectTransform parent, string key, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, Color color, TextAlignmentOptions align, bool wrap = false)
    {
        TextMeshProUGUI t = MakeUiText(parent, key, pos, size, fontSize, font, Tut(key));
        t.color = color;
        t.alignment = align;
        t.enableWordWrapping = wrap;
        // 언어마다 길이가 달라(영어가 길다) 칸을 넘으면 75%까지 줄인다
        t.enableAutoSizing = true;
        t.fontSizeMax = fontSize;
        t.fontSizeMin = fontSize * 0.75f;
        return t;
    }

    // 1 개요(6-160, 표지 배경 — 6-163): 표지 사진을 페이지 전체(팝업 1600 x 1000)에 깔고, 위아래를 어둡게 덮은 뒤
    // 위 = 페이지 제목 · 이름 · 한 줄 소개, 아래 = 반투명 카드 4장 한 줄(기체가 화면 가운데·위를 지나가므로 카드는 아래 흐린 땅 위에).
    private static void BuildTutPage1(RectTransform p, TMP_FontAsset font)
    {
        RawImage cover = TutPhoto(p, "Cover", "cover", Vector2.zero, new Vector2(TutW, TutH));
        cover.uvRect = TutCoverUv(TutW / TutH);
        // 위아래 어둡게(띠를 겹쳐 그라데이션처럼). 페이지 제목(먼저 만들어진 첫 자식)은 사진·그늘 위로 올린다
        Transform pageTitle = p.childCount > 0 ? p.GetChild(0) : null;
        TutShade(p, "ShadeTop", TutH * 0.5f, -330f, 0.85f, 18);
        TutShade(p, "ShadeBottom", -TutH * 0.5f, 520f, 0.9f, 24);
        cover.transform.SetAsFirstSibling();
        if (pageTitle != null) { pageTitle.SetAsLastSibling(); }

        TextMeshProUGUI name = TutText(p, "tut.p1.name", new Vector2(0f, 360f), new Vector2(1520f, 66f), 54f, font, new Color(0.92f, 1f, 0.94f, 1f), TextAlignmentOptions.Left);
        name.fontStyle = FontStyles.Bold;
        TutText(p, "tut.p1.tagline", new Vector2(0f, 302f), new Vector2(1520f, 44f), 31f, font, TutBright, TextAlignmentOptions.Left);

        Color card = new Color(TutCard.r, TutCard.g, TutCard.b, 0.78f);
        Color accent = new Color(TutBright.r, TutBright.g, TutBright.b, 0.8f);
        for (int i = 0; i < 4; i++)
        {
            float cx = -582f + i * 382f;
            const float cy = -250f, cw = 356f, ch = 250f;
            MakeUiBox(p, $"Card{i + 1}", new Vector2(cx, cy), new Vector2(cw, ch), card);
            MakeUiBox(p, $"CardLine{i + 1}", new Vector2(cx, cy + ch * 0.5f - 1.5f), new Vector2(cw, 3f), accent);
            TutText(p, $"tut.p1.c{i + 1}.title", new Vector2(cx, cy + 82f), new Vector2(cw - 40f, 48f), 34f, font, TutBright, TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            TutText(p, $"tut.p1.c{i + 1}.body", new Vector2(cx, cy - 28f), new Vector2(cw - 40f, 158f), 26f, font, TutBody, TextAlignmentOptions.TopLeft, true);
        }
    }

    /// 표지 사진을 가로세로 비율 aspect 칸에 꽉 채우도록 가운데 기준으로 자르는 UV(사진 원본 크기로 계산 — 2의 거듭제곱으로 늘어난 텍스처 크기가 아니라).
    private static Rect TutCoverUv(float aspect)
    {
        float src = 16f / 9f;
        var importer = AssetImporter.GetAtPath(TutorialPhotoPath("cover")) as TextureImporter;
        if (importer != null)
        {
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w > 0 && h > 0) { src = (float)w / h; }
        }
        if (src > aspect) { float u = aspect / src; return new Rect((1f - u) * 0.5f, 0f, u, 1f); }
        float v = src / aspect;
        return new Rect(0f, (1f - v) * 0.5f, 1f, v);
    }

    /// edgeY(페이지 가장자리)에서 안쪽으로 length만큼(+ = 위로, - = 아래로) 검은 띠를 steps장 겹쳐 가장자리 maxAlpha → 안쪽 0 그라데이션을 만든다.
    private static void TutShade(RectTransform p, string name, float edgeY, float length, float maxAlpha, int steps)
    {
        float step = Mathf.Abs(length) / steps;
        float dir = Mathf.Sign(length);
        for (int i = 0; i < steps; i++)
        {
            // 띠 i는 가장자리에서 (i+1)*step까지 덮는다 — 겹친 수가 가장자리일수록 많아 진하다
            float len = (i + 1) * step;
            float a = 1f - Mathf.Pow(1f - maxAlpha, 1f / steps);
            MakeUiBox(p, $"{name}{i}", new Vector2(0f, edgeY + dir * len * 0.5f), new Vector2(TutW, len), new Color(0.012f, 0.03f, 0.02f, a));
        }
    }

    // 2 출격 방법: 4단계 + 안내
    private static void BuildTutPage2(RectTransform p, TMP_FontAsset font)
    {
        for (int i = 0; i < 4; i++)
        {
            float y = 300f - i * 115f;
            TextMeshProUGUI n = MakeUiText(p, $"Num{i + 1}", new Vector2(-730f, y), new Vector2(60f, 70f), 46f, font, (i + 1).ToString());
            n.color = TutBright;
            TutText(p, $"tut.p2.s{i + 1}.label", new Vector2(-570f, y), new Vector2(200f, 70f), 46f, font, new Color(0.94f, 1f, 0.96f, 1f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            TutText(p, $"tut.p2.s{i + 1}.text", new Vector2(150f, y), new Vector2(1220f, 70f), 42f, font, TutBody, TextAlignmentOptions.Left);
        }
        TutText(p, "tut.p2.tip", new Vector2(0f, -195f), new Vector2(1520f, 60f), 36f, font, TutDim, TextAlignmentOptions.Left);
        TutText(p, "tut.p2.tip2", new Vector2(0f, -255f), new Vector2(1520f, 60f), 36f, font, TutDim, TextAlignmentOptions.Left);
    }

    // 3 비행 중: 데스크톱 | VR 두 칸 + 아래 다섯 줄
    private static void BuildTutPage3(RectTransform p, TMP_FontAsset font)
    {
        TutText(p, "tut.p3.desktop", new Vector2(-390f, 355f), new Vector2(740f, 50f), 34f, font, TutDim, TextAlignmentOptions.Left);
        TutText(p, "tut.p3.vr", new Vector2(400f, 355f), new Vector2(740f, 50f), 34f, font, TutDim, TextAlignmentOptions.Left);
        for (int i = 0; i < 6; i++)
        {
            float y = 300f - i * 52f;
            TutText(p, $"tut.p3.d{i + 1}", new Vector2(-390f, y), new Vector2(740f, 54f), 37f, font, TutBody, TextAlignmentOptions.Left);
            TutText(p, $"tut.p3.v{i + 1}", new Vector2(400f, y), new Vector2(740f, 54f), 35f, font, TutBody, TextAlignmentOptions.Left);
        }
        MakeUiBox(p, "Divider", new Vector2(0f, 2f), new Vector2(1520f, 3f), TutLine);
        float[] ys = { -55f, -132f, -212f, -292f };
        float[] hs = { 92f, 54f, 92f, 54f };
        for (int i = 0; i < ys.Length; i++)
        {
            TutText(p, $"tut.p3.n{i + 1}", new Vector2(0f, ys[i]), new Vector2(1520f, hs[i]), 33f, font, TutBody, TextAlignmentOptions.Left, hs[i] > 60f);
        }
    }

    // 4 크레딧과 강화
    private static void BuildTutPage4(RectTransform p, TMP_FontAsset font)
    {
        string[] marks = { "+", "-", "•", "•" };
        float[] ys = { 300f, 185f, 50f, -110f };
        for (int i = 0; i < 4; i++)
        {
            TextMeshProUGUI m = MakeUiText(p, $"Mark{i + 1}", new Vector2(-730f, ys[i] + 2f), new Vector2(60f, 60f), 44f, font, marks[i]);
            m.color = TutBright;
            TutText(p, $"tut.p4.r{i + 1}", new Vector2(70f, ys[i]), new Vector2(1380f, i == 2 ? 120f : 64f), 40f, font, TutBody, i == 2 ? TextAlignmentOptions.Left : TextAlignmentOptions.Left, i == 2);
        }
    }

    // 5 함내 지도: 가운데 위에서 본 도면(tut_plan.png — 월드 X 오른쪽, Z 위) + 위아래 방 사진 6장, 도면의 그 자리에서 사진까지 선으로 잇는다.
    // 위 줄 = 도면 위쪽(복도 쪽) 시설, 아래 줄 = 아래쪽 시설 — 선이 엇갈리지 않게 왼쪽부터 같은 순서로 둔다.
    // 도면 범위·자리는 지금 함내 배치 기준(월드 좌표) — 배치를 옮기면 이 값과 촬영 범위·카메라 자리를 함께 고친다.
    private const float PlanCenterX = 10.5f;
    private const float PlanWidthM = 22f;     // 도면 가로(m) — 세로는 그 절반(2:1)
    private static readonly Vector2 PlanPos = new Vector2(0f, 10f);
    private const float PlanW = 800f;
    private static readonly Vector2 TutPhotoSize = new Vector2(288f, 162f);

    private static Vector2 PlanPoint(float x, float z)
    {
        float k = PlanW / PlanWidthM;
        return PlanPos + new Vector2((x - PlanCenterX) * k, z * k);
    }

    private static void BuildTutPage5(RectTransform p, TMP_FontAsset font)
    {
        RawImage plan = TutPhoto(p, "Plan", "plan", PlanPos, new Vector2(PlanW, PlanW * 0.5f));
        Color line = new Color(0.45f, 1f, 0.6f, 0.95f);
        // 사진 순서 = TutorialMapPhotos(briefing, cic, corridor, berth, hangar, deck). 도면 자리(월드 X, Z)와 사진 자리
        Vector2[] spots = { PlanPoint(3.6f, 0f), PlanPoint(10.1f, -2.0f), PlanPoint(12.5f, 3.4f), PlanPoint(17.3f, -2.2f), PlanPoint(18.1f, 3.2f), PlanPoint(20.2f, 2.2f) };
        Vector2[] photos = { new Vector2(-400f, -280f), new Vector2(0f, -280f), new Vector2(-400f, 310f), new Vector2(400f, -280f), new Vector2(0f, 310f), new Vector2(400f, 310f) };
        for (int i = 0; i < TutorialMapPhotos.Length; i++)
        {
            string id = TutorialMapPhotos[i];
            bool top = photos[i].y > 0f;
            Vector2 edge = photos[i] + new Vector2(0f, top ? -TutPhotoSize.y * 0.5f : TutPhotoSize.y * 0.5f);
            TutRoute(p, line, spots[i], edge);
            MakeUiBox(p, $"Spot_{id}", spots[i], new Vector2(18f, 18f), i == 0 ? Color.white : line);
            if (i == 0) { MakeUiBox(p, "Here", photos[i], TutPhotoSize + new Vector2(12f, 12f), Color.white); }
            else { MakeUiBox(p, $"Frame_{id}", photos[i], TutPhotoSize + new Vector2(6f, 6f), line); }
            TutPhoto(p, $"Photo_{id}", id, photos[i], TutPhotoSize);
            Vector2 tag = photos[i] + new Vector2(0f, -TutPhotoSize.y * 0.5f + 22f);
            MakeUiBox(p, $"TagPlate_{id}", tag, new Vector2(TutPhotoSize.x, 44f), new Color(0f, 0f, 0f, 0.72f));
            TutText(p, $"tut.p5.{id}", tag + new Vector2(4f, 0f), new Vector2(TutPhotoSize.x - 16f, 44f), 26f, font, new Color(0.94f, 1f, 0.96f, 1f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
        }
        TutText(p, "tut.p5.note", new Vector2(600f, 10f), new Vector2(320f, 200f), 28f, font, TutDim, TextAlignmentOptions.Left, true);
        plan.transform.SetAsFirstSibling();
    }

    /// 꺾인 선(UI 막대). 꺾이는 자리에 작은 사각형을 둬 이음매가 보이지 않게 한다.
    private static void TutRoute(RectTransform p, Color color, params Vector2[] pts)
    {
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 d = b - a;
            Image seg = MakeUiBox(p, "Line", (a + b) * 0.5f, new Vector2(d.magnitude, 5f), color);
            seg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }
    }

    private static RawImage TutPhoto(RectTransform parent, string name, string photo, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 0;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        RawImage img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        img.texture = LoadTutorialPhoto(photo);
        if (img.texture == null) { img.color = TutCard; }
        return img;
    }

    private static string TutorialPhotoPath(string id) { return $"{TutorialPhotoDir}/tut_{id}.png"; }

    private static Texture2D LoadTutorialPhoto(string id)
    {
        string path = TutorialPhotoPath(id);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { Debug.LogWarning($"[ACO] 튜토리얼 사진이 없습니다: {path} — Setup Check [고치기]로 찍으세요."); return null; }
        int max = id == "plan" || id == "cover" ? 2048 : 512;   // 표지는 판 크기(2.94m) 전체에 깔려 2048
        if (importer.maxTextureSize != max || !importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = max;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static bool TutorialPhotosExist()
    {
        foreach (string id in TutorialMapPhotos) { if (!File.Exists(TutorialPhotoPath(id))) { return false; } }
        return File.Exists(TutorialPhotoPath("plan")) && File.Exists(TutorialPhotoPath("cover"));
    }

    /// 함내 도면(위에서 본 직교 촬영 — 천장 보·배관 아래에서 찍어 바닥과 가구만), 방 사진 6장, 표지가 없으면 항모 사진을 찍어 Textures/Tutorial에 저장한다.
    /// 표지는 있으면 그대로 둔다(전투 장면 스크린샷으로 바꿔 넣을 수 있게). 범위는 PlanCenterX · PlanWidthM — 함내 배치를 옮기면 고친다.
    public static void CaptureTutorialPhotos()
    {
        EnsureFolder("Assets/ACO_Addon/Textures", "Tutorial");
        TutPlanShot("plan", new Vector3(PlanCenterX, -247.9f, 0f), PlanWidthM * 0.25f);
        Vector3 hp = new Vector3(3.1f, 7.7f, -92.8f);
        Vector3 hf = new Vector3(-0.71f, 0f, -0.71f);
        TutShot("briefing", new Vector3(5.6f, -248.4f, 2.6f), new Vector3(0f, -248.9f, -0.6f), 75f, 300f);
        TutShot("cic", new Vector3(13.6f, -248.3f, 0.4f), new Vector3(8.5f, -249.3f, -3.6f), 75f, 300f);
        TutShot("corridor", new Vector3(6.8f, -248.5f, 2.7f), new Vector3(20f, -248.7f, 2.9f), 70f, 300f);
        TutShot("berth", new Vector3(14.8f, -248.4f, 0.6f), new Vector3(20.2f, -248.8f, -1.8f), 75f, 300f);
        TutShot("hangar", hp + hf * 5.5f + Vector3.up * 0.4f, hp + new Vector3(-2.5f, -0.6f, 1.2f), 75f, 300f);
        TutShot("deck", new Vector3(-15f, 32f, -120f), new Vector3(15f, 21f, 10f), 65f, 20000f);
        if (!File.Exists(TutorialPhotoPath("cover"))) { TutShot("cover", new Vector3(150f, 35f, -190f), new Vector3(10f, 15f, 0f), 32f, 20000f); }
        AssetDatabase.Refresh();
        Debug.Log($"[ACO] 튜토리얼 도면·표지를 {TutorialPhotoDir}에 찍었습니다. 표지(tut_cover.png)는 전투 장면 스크린샷으로 바꿔 넣어도 됩니다 — 바꾼 뒤 튜토리얼 프리팹을 다시 만드세요.");
    }

    private static void TutPlanShot(string id, Vector3 pos, float halfHeight)
    {
        var go = new GameObject("ACO_TutorialCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = halfHeight;
        cam.aspect = 2f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 10f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.07f, 0.05f, 1f);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        var rt = new RenderTexture(2048, 1024, 24);
        var tex = new Texture2D(2048, 1024, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 2048, 1024), 0, 0);
            tex.Apply();
            File.WriteAllBytes(TutorialPhotoPath(id), tex.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }

    private static void TutShot(string id, Vector3 pos, Vector3 look, float fov, float far)
    {
        var go = new GameObject("ACO_TutorialCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = far;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        var rt = new RenderTexture(1280, 720, 24);
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes(TutorialPhotoPath(id), tex.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }
}
