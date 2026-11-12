using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ACO;

public partial class ACO_SceneBaker
{
    /// 지원 호출(6-142): 관리자 ↔ SUPPORT 다이얼(번호 = Supports 순서)·진행도·무전, 상점 호출권 아이콘·격납고 SUPPORT 아이콘.
    private static void BakeSupport()
    {
        ACO_SupportManager manager = Object.FindObjectOfType<ACO_SupportManager>(true);
        ACO_PlayerProgress progress = Object.FindObjectOfType<ACO_PlayerProgress>(true);
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        var supports = new List<ACO_DFUNC_Support>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            supports.AddRange(root.GetComponentsInChildren<ACO_DFUNC_Support>(true));
        }

        for (int i = 0; i < supports.Count; i++)
        {
            ACO_DFUNC_Support s = supports[i];
            if (s.Manager == manager && s.Progress == progress && s.SupportIndex == i) { continue; }
            Undo.RecordObject(s, "ACO bake support");
            s.Manager = manager;
            s.Progress = progress;
            s.SupportIndex = i;
            MarkChanged(s);
        }
        if (manager != null)
        {
            Undo.RecordObject(manager, "ACO bake support");
            if (!SameRefs(manager.Supports, supports.ToArray())) { manager.Supports = supports.ToArray(); }
            manager.Progress = progress;
            manager.Chatter = chatter;
            // 무전 음성: 빈 칸만 만든 파일로(LINE_* 순서 — Tools/aco_voice_lines.json의 support_*)
            string[] clipNames = { "StrikeCall", "StrikeRelease", "StrikeImpact", "TankerCall", "TankerContact", "TankerDone", "TankerBreak", "GuardianCall", "GuardianEnd" };
            if (manager.LineClips == null || manager.LineClips.Length < clipNames.Length)
            {
                var grown = new AudioClip[clipNames.Length];
                if (manager.LineClips != null) { System.Array.Copy(manager.LineClips, grown, manager.LineClips.Length); }
                manager.LineClips = grown;
            }
            for (int i = 0; i < clipNames.Length; i++)
            {
                if (manager.LineClips[i] != null) { continue; }
                manager.LineClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/AWACSVoiceLines/Support/Support_{clipNames[i]}.mp3");
            }
            MarkChanged(manager);
        }

        Sprite[] icons = ACO_HUDBuilder.SupportCallIcons();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Shop shop in root.GetComponentsInChildren<ACO_Shop>(true))
            {
                if (SameRefs(shop.CallIcons, icons)) { continue; }
                Undo.RecordObject(shop, "ACO bake support");
                shop.CallIcons = icons;
                MarkChanged(shop);
            }
            foreach (ACO_Loadout loadout in root.GetComponentsInChildren<ACO_Loadout>(true))
            {
                if (loadout.SupportText == null || SameRefs(loadout.SupportIcons, icons)) { continue; }
                Undo.RecordObject(loadout, "ACO bake support");
                loadout.SupportIcons = icons;
                MarkChanged(loadout);
            }
        }

        if (supports.Count > 0 || manager != null)
        {
            Debug.Log(manager != null
                ? $"[ACO] 지원 호출: SUPPORT 다이얼 {supports.Count}개를 관리자 '{manager.name}'에 연결했습니다."
                : $"[ACO] 지원 호출: SUPPORT 다이얼이 {supports.Count}개 있지만 지원 관리자가 없습니다 — Setup Check에서 만드세요.", manager);
        }
    }
}
