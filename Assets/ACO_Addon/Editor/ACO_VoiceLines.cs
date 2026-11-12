using UnityEditor;
using UnityEngine;
using ACO;

/// 무전 대사 음성(ElevenLabs — Tools/aco_elevenlabs.py, 목록 Tools/aco_voice_lines.json)을 대사 칸에 넣는다.
/// 파일은 Audio/AWACSVoiceLines/<페이즈>/ 아래(LandShip, Borderless, Twins, Shade). 비어 있는 칸만 채운다 — 손으로 넣은 클립은 그대로 둔다.
/// 프리팹 빌더(형제·SHADE·레이저 에이스), 페이즈 모듈 생성(Ensure*), Setup Check 프리팹 수치(voice-lines-1)가 같이 쓴다.
public static class ACO_VoiceLines
{
    private const string Dir = "Assets/ACO_Addon/Audio/AWACSVoiceLines/";

    // 발사 무전 음성 파일 이름(ACO_LaunchCall 순서 — 0 없음)
    private static readonly string[] LaunchNames = { "", "FoxTwo", "FoxThree", "Rifle", "Magnum", "Greyhound", "Bruiser", "Pickle", "Rockets", "Spear", "Music", "Drive" };
    // 코드 표가 바뀐 칸: 예전 기본 글자 → 새 글자(손으로 바꾼 글자는 그대로)
    private static readonly string[] LaunchTextDefaults = { "", "Fox two.", "Fox three.", "Rifle.", "Magnum.", "Greyhound.", "Bruiser.", "Pickle.", "Rockets away.", "Spear!", "Music on.", "Drive! Drive!" };

    /// 발사 무전(관제기가 코드만 — Launch/Launch_*): 무전(ACO_Chatter) LaunchClips 1~11의 빈 칸, 글자 칸이 모자라면 늘리고 빈 칸·예전 기본 글자(9 "Rail shot.")를 새 글자로. 바꿨으면 true.
    public static bool ApplyLaunch(ACO_Chatter c)
    {
        if (c == null) { return false; }
        bool changed = false;
        if (c.LaunchTexts == null || c.LaunchTexts.Length < LaunchTextDefaults.Length)
        {
            var grown = new string[LaunchTextDefaults.Length];
            if (c.LaunchTexts != null) { for (int i = 0; i < c.LaunchTexts.Length && i < grown.Length; i++) { grown[i] = c.LaunchTexts[i]; } }
            c.LaunchTexts = grown;
            changed = true;
        }
        for (int i = 1; i < LaunchTextDefaults.Length; i++)
        {
            string t = c.LaunchTexts[i];
            if (string.IsNullOrEmpty(t) || (i == 9 && t == "Rail shot."))
            {
                c.LaunchTexts[i] = LaunchTextDefaults[i];
                changed = true;
            }
        }
        if (c.LaunchClips == null || c.LaunchClips.Length < LaunchNames.Length)
        {
            var grown = new AudioClip[LaunchNames.Length];
            if (c.LaunchClips != null) { for (int i = 0; i < c.LaunchClips.Length && i < grown.Length; i++) { grown[i] = c.LaunchClips[i]; } }
            c.LaunchClips = grown;
            changed = true;
        }
        for (int i = 1; i < LaunchNames.Length; i++)
        {
            if (Fill(ref c.LaunchClips[i], "Launch/Launch_" + LaunchNames[i])) { changed = true; }
        }
        return changed;
    }

    /// Setup Check(launch-voice-1): 씬 무전의 발사 무전 음성 빈 칸을 채운다.
    /// Setup Check landship-lines-3: 씬 육상전함 모듈의 대사 표를 스크립트 기본값(3판 — 적 함장 1판 대사 + AWACS 2판 대사, 총력 복구·코어 폭발 새 줄)으로 바꾸고 음성을 넣는다.
    public static void ApplyLandShipV3() { ApplyLandShipV2(); }

    /// Setup Check landship-lines-2(이미 적용된 항목): 씬 육상전함 모듈의 대사 표를 스크립트 기본값으로 바꾸고 음성을 넣는다(예전 표 칸은 버린다).
    public static void ApplyLandShipV2()
    {
        var temp = new GameObject("ACO_TempLandShip");
        try
        {
            ACO_PhaseLandShip d = temp.AddComponent<ACO_PhaseLandShip>();
            int n = 0;
            foreach (ACO_PhaseLandShip m in Object.FindObjectsOfType<ACO_PhaseLandShip>(true))
            {
                Undo.RecordObject(m, "ACO landship lines v2");
                m.LineEvent = (int[])d.LineEvent.Clone();
                m.LineSpeaker = (int[])d.LineSpeaker.Clone();
                m.LineText = (string[])d.LineText.Clone();
                m.LineClip = new AudioClip[d.LineText.Length];
                ApplyPhase(m);
                PrefabUtility.RecordPrefabInstancePropertyModifications(m);
                EditorUtility.SetDirty(m);
                n++;
            }
            if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
            Debug.Log($"[ACO] 육상전함 대사 표: 모듈 {n}개 — 기본 대사 표({d.LineText.Length}줄)와 음성을 넣었습니다. 씬을 저장하세요.");
        }
        finally { Object.DestroyImmediate(temp); }
    }

    public static void ApplyLaunchToScene()
    {
        int n = 0;
        foreach (ACO_Chatter c in Object.FindObjectsOfType<ACO_Chatter>(true))
        {
            Undo.RecordObject(c, "ACO launch voice");
            if (!ApplyLaunch(c)) { continue; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(c);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 발사 무전 음성: 무전 {n}개에 넣었습니다. 씬을 저장하세요.");
    }

    private static AudioClip Clip(string path)
    {
        AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + path + ".mp3");
        return c != null ? c : AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + path + ".wav");   // 무전 필터를 입힌 음성은 wav
    }

    // 육상전함 대사 표 2판(6-146 — 함장의 광기·선원·자동 사통): 줄마다 음성 파일(바뀌지 않은 AWACS 줄은 예전 파일을 그대로)
    // 3판(6-150 — 적 함장 1판 대사 + AWACS 2판 대사, 총력 복구·코어 폭발은 새 줄): 23줄
    private static readonly string[] LandShipV3Clips = { "LandShip/LandShip_L01", "LandShip/LandShip_L03", "LandShip/LandShip_L04", "LandShip/LandShip_L05", "LandShip/LandShip_N04", "LandShip/LandShip_L06", "LandShip/LandShip_N07", "LandShip/LandShip_L09", "LandShip/LandShip_N23", "LandShip/LandShip_N25", "LandShip/LandShip_L11", "LandShip/LandShip_L12", "LandShip/LandShip_L13", "LandShip/LandShip_L14", "LandShip/LandShip_L15", "LandShip/LandShip_L16", "LandShip/LandShip_N26", "LandShip/LandShip_L18", "LandShip/LandShip_L19", "LandShip/LandShip_L20", "LandShip/LandShip_L21", "LandShip/LandShip_L22", "LandShip/LandShip_L22" };
    private static readonly string[] LandShipV2Clips = { "LandShip/LandShip_N01", "LandShip/LandShip_L03", "LandShip/LandShip_L05", "LandShip/LandShip_N04", "LandShip/LandShip_N05", "LandShip/LandShip_N06", "LandShip/LandShip_N07", "LandShip/LandShip_N08", "LandShip/LandShip_N09", "LandShip/LandShip_N10", "LandShip/LandShip_N11", "LandShip/LandShip_L12", "LandShip/LandShip_N13", "LandShip/LandShip_L13", "LandShip/LandShip_L14", "LandShip/LandShip_L16", "LandShip/LandShip_N17", "LandShip/LandShip_L18", "LandShip/LandShip_N19", "LandShip/LandShip_L20", "LandShip/LandShip_L22", "LandShip/LandShip_N22" };

    /// 비어 있으면 넣는다. 바꿨으면 true.
    private static bool Fill(ref AudioClip slot, string path)
    {
        if (slot != null) { return false; }
        AudioClip c = Clip(path);
        if (c == null) { return false; }
        slot = c;
        return true;
    }

    private static bool FillArray(ref AudioClip[] slots, int size, string pathFormat, int first, int count, int numberBase = 1)
    {
        bool changed = false;
        if (slots == null || slots.Length < size)
        {
            var grown = new AudioClip[size];
            if (slots != null) { for (int i = 0; i < slots.Length; i++) { grown[i] = slots[i]; } }
            slots = grown;
            changed = true;
        }
        for (int i = first; i < first + count && i < slots.Length; i++)
        {
            if (Fill(ref slots[i], string.Format(pathFormat, i + numberBase))) { changed = true; }
        }
        return changed;
    }

    // ───────────── 에이스 (프리팹) ─────────────

    /// 형제(HEADS / TAILS). 이름이 HEADS·TAILS가 아니면(예전 대사) 넣지 않는다.
    public static bool ApplyTwin(ACO_Twins ace)
    {
        if (ace == null) { return false; }
        string who = ace.AceName == "TAILS" ? "Tails" : ace.AceName == "HEADS" ? "Heads" : null;
        if (who == null) { return false; }
        string p = "Twins/" + who + "_";
        bool c = false;
        // 플레어 대사 글자(비어 있을 때만 — 손으로 바꾼 글자는 그대로)
        if (string.IsNullOrEmpty(ace.LineFlare))
        {
            ace.LineFlare = who == "Heads" ? "Coin's still in the air." : "Ooh, shiny! Your missile likes those better!";
            c = true;
        }
        c |= Fill(ref ace.ClipSpawn, p + "Spawn");
        c |= Fill(ref ace.ClipSalvo, p + "Salvo");
        c |= Fill(ref ace.ClipDecoy, p + "Decoy");
        c |= Fill(ref ace.ClipHalf, p + "Half");
        c |= Fill(ref ace.ClipDown, p + "Down");
        c |= Fill(ref ace.ClipPartnerDown, p + "PartnerDown");
        c |= Fill(ref ace.ClipCross, p + "Cross");
        c |= Fill(ref ace.ClipFlare, p + "Flare");
        if (c) { EditorUtility.SetDirty(ace); }
        return c;
    }

    public static bool ApplyBorderless(ACO_Borderless ace)
    {
        if (ace == null) { return false; }
        bool c = false;
        c |= Fill(ref ace.ClipSpawn, "Borderless/Borderless_Spawn");
        c |= Fill(ref ace.ClipEscape, "Borderless/Borderless_Escape");
        c |= Fill(ref ace.ClipDown, "Borderless/Borderless_Down");
        c |= Fill(ref ace.ClipFlare, "Borderless/Borderless_Flare");
        if (c) { EditorUtility.SetDirty(ace); }
        return c;
    }

    public static bool ApplyShade(ACO_ShadeAce s)
    {
        if (s == null) { return false; }
        bool c = false;
        c |= Fill(ref s.ClipCheckIn, "Shade/Shade_CheckIn");
        c |= Fill(ref s.ClipCobra, "Shade/Shade_Cobra");
        c |= Fill(ref s.ClipCross, "Shade/Shade_Cross");
        c |= Fill(ref s.ClipFlare, "Shade/Shade_Flare");
        c |= FillArray(ref s.ClipReply, 4, "Shade/Shade_Reply{0}", 1, 3);   // 칸 0(1번)은 체크인이라 비운다
        c |= FillArray(ref s.ClipMemberDown, 4, "Shade/Shade_MemberDown{0}", 0, 4);
        c |= FillArray(ref s.ClipDown, 4, "Shade/Shade_Down{0}", 0, 4);
        if (c) { EditorUtility.SetDirty(s); }
        return c;
    }

    public static bool ApplyLuna(ACO_LunaAce ace)
    {
        if (ace == null) { return false; }
        bool c = false;
        c |= Fill(ref ace.ClipSpawn, "Luna/Luna_Spawn");
        c |= Fill(ref ace.ClipDown, "Luna/Luna_Down");
        c |= Fill(ref ace.ClipSnipe, "Luna/Luna_Snipe");
        c |= Fill(ref ace.ClipFlare, "Luna/Luna_Flare");
        if (c) { EditorUtility.SetDirty(ace); }
        return c;
    }

    public static bool ApplyMrS(ACO_MrS ace)
    {
        if (ace == null) { return false; }
        bool c = false;
        c |= Fill(ref ace.ClipSpawn, "MrS/MrS_Spawn");
        c |= Fill(ref ace.ClipSunMissile, "MrS/MrS_SunMissile");
        c |= Fill(ref ace.ClipSunLure, "MrS/MrS_SunLure");
        c |= Fill(ref ace.ClipGlare, "MrS/MrS_Glare");
        c |= Fill(ref ace.ClipReverse, "MrS/MrS_Reverse");
        c |= Fill(ref ace.ClipFlare, "MrS/MrS_Flare");
        c |= Fill(ref ace.ClipHalf, "MrS/MrS_Half");
        c |= Fill(ref ace.ClipDown, "MrS/MrS_Down");
        c |= Fill(ref ace.ClipAwacsRail, "MrS/MrS_AwacsRail");
        c |= Fill(ref ace.ClipAwacsHalf, "MrS/MrS_AwacsHalf");
        if (c) { EditorUtility.SetDirty(ace); }
        return c;
    }

    // ───────────── 페이즈 모듈 (씬) ─────────────

    /// 시작·성공·실패 음성과 상황별 대사 표 음성. 대사 표는 줄 수가 생성할 때와 같을 때만(순서가 바뀌었으면 넣지 않는다).
    public static bool ApplyPhase(ACO_PhaseModule m)
    {
        if (m == null) { return false; }
        bool c = false;
        if (m is ACO_PhaseLandShip landShip)
        {
            c |= Common(m, "LandShip/LandShip_", true);
            bool v2 = landShip.LineEvent != null && System.Array.IndexOf(landShip.LineEvent, ACO_PhaseLandShip.SIT_ALL_REPAIR) >= 0;
            if (v2 && landShip.LineText != null && landShip.LineText.Length == LandShipV3Clips.Length)
            {
                if (landShip.LineClip == null || landShip.LineClip.Length != LandShipV3Clips.Length) { landShip.LineClip = new AudioClip[LandShipV3Clips.Length]; c = true; }
                for (int i = 0; i < LandShipV3Clips.Length; i++) { c |= Fill(ref landShip.LineClip[i], LandShipV3Clips[i]); }
            }
            else if (v2 && landShip.LineText != null && landShip.LineText.Length == LandShipV2Clips.Length)
            {
                if (landShip.LineClip == null || landShip.LineClip.Length != LandShipV2Clips.Length) { landShip.LineClip = new AudioClip[LandShipV2Clips.Length]; c = true; }
                for (int i = 0; i < LandShipV2Clips.Length; i++) { c |= Fill(ref landShip.LineClip[i], LandShipV2Clips[i]); }
            }
            else if (landShip.LineText != null && landShip.LineText.Length == 22) { c |= FillArray(ref landShip.LineClip, 22, "LandShip/LandShip_L{0:00}", 0, 22); }
        }
        else if (m is ACO_PhaseBorderless laser)
        {
            c |= Common(m, "Borderless/Borderless_", true);
            if (laser.LineText != null && laser.LineText.Length == 15) { c |= FillArray(ref laser.LineClip, 15, "Borderless/Borderless_L{0:00}", 0, 15); }
        }
        else if (m is ACO_PhaseTwins twins && !twins.BonusOnly) { c |= Common(m, "Twins/Twins_", false); }
        else if (m is ACO_PhaseShade) { c |= Common(m, "Shade/Shade_", false); }
        else if (m is ACO_PhaseLuna luna)
        {
            c |= Common(m, "Luna/Luna_", true);
            // 대사 표 15줄 = 음성 L02~L16(L01은 뺐다)
            if (luna.LineText != null && luna.LineText.Length == 15) { c |= FillArray(ref luna.LineClip, 15, "Luna/Luna_L{0:00}", 0, 15, 2); }
        }
        else if (m is ACO_PhaseMrS) { c |= Common(m, "MrS/MrS_", false); }
        if (c)
        {
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        return c;
    }

    private static bool Common(ACO_PhaseModule m, string prefix, bool fail)
    {
        bool c = false;
        if (!string.IsNullOrEmpty(m.StartText)) { c |= Fill(ref m.StartClip, prefix + "Start"); }
        if (!string.IsNullOrEmpty(m.SuccessText)) { c |= Fill(ref m.SuccessClip, prefix + "Clear"); }
        if (fail && !string.IsNullOrEmpty(m.FailChatterText)) { c |= Fill(ref m.FailClip, prefix + "Fail"); }
        return c;
    }

    // ───────────── Setup Check 프리팹 수치(voice-lines-1) ─────────────

    /// 이미 있는 형제·SHADE·레이저 에이스 프리팹과 씬의 페이즈 모듈(육상전함·레이저 에이스·형제·SHADE)에 음성을 넣는다.
    public static void ApplyAll()
    {
        ACO_PrefabTuning.EditPrefabs(root =>
        {
            bool c = false;
            foreach (ACO_Twins a in root.GetComponentsInChildren<ACO_Twins>(true)) { c |= ApplyTwin(a); }
            foreach (ACO_ShadeAce s in root.GetComponentsInChildren<ACO_ShadeAce>(true)) { c |= ApplyShade(s); }
            foreach (ACO_Borderless l in root.GetComponentsInChildren<ACO_Borderless>(true)) { c |= ApplyBorderless(l); }
            foreach (ACO_LunaAce l in root.GetComponentsInChildren<ACO_LunaAce>(true)) { c |= ApplyLuna(l); }
            foreach (ACO_MrS l in root.GetComponentsInChildren<ACO_MrS>(true)) { c |= ApplyMrS(l); }
            return c;
        });
        int modules = 0;
        foreach (ACO_PhaseModule m in Object.FindObjectsOfType<ACO_PhaseModule>(true)) { if (ApplyPhase(m)) { modules++; } }
        Debug.Log($"[ACO] 무전 음성: 에이스 프리팹과 페이즈 모듈 {modules}개의 빈 음성 칸을 채웠습니다. 씬을 저장하세요. "
            + "(형제·SHADE 페이즈 모듈이 아직 없으면 Setup Check로 만든 뒤 자동으로 들어갑니다)");
    }
}
