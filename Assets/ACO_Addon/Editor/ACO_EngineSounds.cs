using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;

/// NPC 항공기 엔진 소리 · 탄도탄 로켓 소리(6-156). 스크립트 없이 AudioSource만 — 반복·켜지면 재생·3D·도플러 약하게, 음높이·볼륨 고정.
///  - 엔진: SAV SF-1 Thrust.wav. 격추 때 꺼지는 모델(Model) 아래에 두어 격추하면 같이 꺼지고, 부활·풀에서 켜지면 다시 난다.
///    호위 유닛(폭격기·수송기·건쉽)은 모델이 추락 뒤에야 꺼지므로 소리 오브젝트를 HideOnDeath에도 넣는다(추락 중엔 조용히).
///    공격헬기(NPC_FriendlyGunship)는 제트 소리 대신 NPC_Helicopter의 프로펠러 소리.
///  - 로켓: Audio/Generated/ACO_Rocket_Loop.wav(ElevenLabs, 이음매 겹침) — TEL의 FlyingMissile, ICBM의 Flame 아래. 10km까지.
/// 쓰는 곳: Setup Check npc-engine-sound-1(이미 있는 프리팹·씬 ICBM), 프리팹 생성 창·지원 생성기·TEL/ICBM 생성기(새로 만들 때).
public static class ACO_EngineSounds
{
    public const string SoundName = "EngineSound";
    public const string RocketName = "RocketSound";
    private const string ThrustClipPath = "Assets/SaccFlightAndVehicles/AssetFiles/SF-1/Sounds/Thrust.wav";
    private const string RocketClipPath = "Assets/ACO_Addon/Audio/Generated/ACO_Rocket_Loop.wav";
    private const string HeliPrefabPath = "Assets/ACO_Addon/Prefabs/NPC_Helicopter.prefab";

    // 기체 이름 → 볼륨·들리는 거리(m)
    private static readonly string[] Names =
    {
        "NPC_Fighter", "NPC_Attacker", "NPC_Borderless", "NPC_Heads", "NPC_Tails", "NPC_Luna", "NPC_Shade",
        "NPC_Bomber", "NPC_Transport", "ACO_SupportBomber",
        "NPC_FriendlyGunship",
        "ACO_TankerDrone", "ACO_GuardianDrone",
        "NPC_MrS",
    };
    private static readonly float[] Volumes = { 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.7f, 0.35f, 0.35f, 0.7f };
    private static readonly float[] Ranges = { 2500f, 2500f, 2500f, 2500f, 2500f, 2500f, 2500f, 3500f, 3500f, 3500f, 2500f, 1200f, 1200f, 2500f };

    public const float RocketRange = 10000f;
    public const float RocketVolume = 1f;

    private static int IndexOf(string prefabName) { return System.Array.IndexOf(Names, prefabName); }

    /// Setup Check npc-engine-sound-1: 항공기 프리팹 전부 + TEL + 씬의 ICBM 사이트.
    public static void ApplyAll()
    {
        int n = 0;
        foreach (string name in Names) { if (EnsureOnPrefabPath($"Assets/ACO_Addon/Prefabs/{name}.prefab")) { n++; } }
        if (EnsureRocketOnPrefab("Assets/ACO_Addon/Prefabs/NPC_TEL.prefab")) { n++; }
        foreach (ACO_IcbmSite site in Object.FindObjectsOfType<ACO_IcbmSite>(true))
        {
            if (site.Flame == null) { Debug.LogWarning("[ACO] 로켓 소리: ICBM 사이트에 Flame이 없어 건너뜁니다.", site); continue; }
            if (AddRocket(site.Flame.transform))
            {
                EditorUtility.SetDirty(site.Flame);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(site.gameObject.scene);
                n++;
            }
        }
        Debug.Log($"[ACO] 엔진·로켓 소리: {n}곳을 맞췄습니다.");
    }

    /// 프리팹 생성 창·지원 생성기가 새로 만든 프리팹에 부른다(목록에 없는 프리팹이면 아무것도 안 한다).
    public static void EnsureOnPrefab(GameObject prefabAsset)
    {
        if (prefabAsset == null) { return; }
        string path = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrEmpty(path)) { return; }
        if (prefabAsset.name == "NPC_TEL") { EnsureRocketOnPrefab(path); return; }
        EnsureOnPrefabPath(path);
    }

    private static bool EnsureOnPrefabPath(string path)
    {
        int k = IndexOf(System.IO.Path.GetFileNameWithoutExtension(path));
        if (k < 0 || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return false; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!Ensure(root, k)) { return false; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] 엔진 소리: {System.IO.Path.GetFileName(path)}");
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static bool EnsureRocketOnPrefab(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return false; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_TelUnit tel = root.GetComponentInChildren<ACO_TelUnit>(true);
            Transform flying = tel != null && tel.FlyingMissile != null ? tel.FlyingMissile.transform : root.transform.Find("FlyingMissile");
            if (flying == null) { Debug.LogWarning($"[ACO] 로켓 소리: {path}에 FlyingMissile이 없습니다."); return false; }
            if (!AddRocket(flying)) { return false; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] 로켓 소리: {System.IO.Path.GetFileName(path)}");
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 기체 하나. 바꿨으면 true.
    private static bool Ensure(GameObject root, int k)
    {
        Transform model = root.transform.Find("Model");
        Transform parent = model != null ? model : root.transform;
        bool heli = Names[k] == "NPC_FriendlyGunship";
        AudioClip clip;
        if (heli)
        {
            clip = HeliClip();
            if (clip == null) { Debug.LogWarning("[ACO] 엔진 소리: NPC_Helicopter의 PropSound 클립을 찾지 못해 공격헬기는 건너뜁니다."); return false; }
        }
        else
        {
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ThrustClipPath);
            if (clip == null) { Debug.LogWarning("[ACO] 엔진 소리: " + ThrustClipPath + "이 없습니다."); return false; }
        }

        // 이미 있는 엔진 소리(손으로 넣은 것 포함 — 예전 EngineDistant 한 번 재생)를 고쳐 쓴다
        // (지원 폭격기는 폭격기 모델을 본떠 소리 부품을 지운 채 복사한다 — 이름만 남은 오브젝트면 소리를 다시 단다)
        AudioSource src = null;
        bool changed = false;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != SoundName) { continue; }
            src = t.GetComponent<AudioSource>();
            if (src == null) { src = t.gameObject.AddComponent<AudioSource>(); changed = true; }
            break;
        }
        if (src == null)
        {
            var go = new GameObject(SoundName);
            go.transform.SetParent(parent, false);
            src = go.AddComponent<AudioSource>();
            changed = true;
        }
        if (model != null && !src.transform.IsChildOf(model))
        {
            src.transform.SetParent(model, false);
            changed = true;
        }
        changed |= Configure(src, clip, Volumes[k], Ranges[k], EngineCurve(), 200);

        // 호위 유닛: 모델은 추락이 끝나야 꺼진다 — 소리는 격추 순간에 끈다
        ACO_EscortUnit escort = root.GetComponent<ACO_EscortUnit>();
        if (escort != null)
        {
            var list = new List<GameObject>(escort.HideOnDeath ?? new GameObject[0]);
            if (!list.Contains(src.gameObject))
            {
                list.Add(src.gameObject);
                escort.HideOnDeath = list.ToArray();
                EditorUtility.SetDirty(escort);
                changed = true;
            }
        }
        return changed;
    }

    /// 로켓 소리를 parent 아래에(그 오브젝트가 켜져 있는 동안 — 날 때만). 이미 있는 TEL EngineSound(2D)는 고쳐 쓴다. 바꿨으면 true.
    public static bool AddRocket(Transform parent)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(RocketClipPath);
        if (clip == null) { Debug.LogWarning("[ACO] 로켓 소리: " + RocketClipPath + "이 없습니다."); return false; }
        AudioSource src = null;
        foreach (Transform c in parent)
        {
            if (c.name == RocketName || c.name == SoundName) { src = c.GetComponent<AudioSource>(); if (src != null) { break; } }
        }
        bool changed = false;
        if (src == null)
        {
            var go = new GameObject(RocketName);
            go.transform.SetParent(parent, false);
            src = go.AddComponent<AudioSource>();
            changed = true;
        }
        changed |= Configure(src, clip, RocketVolume, RocketRange, RocketCurve(), 90);
        return changed;
    }

    private static AudioClip HeliClip()
    {
        GameObject heli = AssetDatabase.LoadAssetAtPath<GameObject>(HeliPrefabPath);
        if (heli == null) { return null; }
        Transform prop = heli.transform.Find("PropSound");
        AudioSource s = prop != null ? prop.GetComponent<AudioSource>() : null;   // 에디터에서 프리팹 에셋 읽기(Udon 아님)
        if (s == null)
        {
            foreach (AudioSource a in heli.GetComponentsInChildren<AudioSource>(true)) { if (a.name == "PropSound") { s = a; break; } }
        }
        return s != null ? s.clip : null;
    }

    /// 반복·켜지면 재생·3D·도플러 0.3·음높이 1, 거리 곡선은 0~range. 바꿨으면 true.
    private static bool Configure(AudioSource s, AudioClip clip, float volume, float range, AnimationCurve curve, int priority)
    {
        bool same = s.clip == clip && s.loop && s.playOnAwake && Mathf.Approximately(s.volume, volume)
            && Mathf.Approximately(s.spatialBlend, 1f) && Mathf.Approximately(s.dopplerLevel, 0.3f)
            && Mathf.Approximately(s.pitch, 1f) && Mathf.Approximately(s.maxDistance, range)
            && s.rolloffMode == AudioRolloffMode.Custom && s.priority == priority && SameCurve(s.GetCustomCurve(AudioSourceCurveType.CustomRolloff), curve);
        if (same) { return false; }
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = true;
        s.volume = volume;
        s.pitch = 1f;
        s.spatialBlend = 1f;
        s.dopplerLevel = 0.3f;
        s.priority = priority;   // 무전·경고음보다 뒤(숫자가 클수록 낮다)
        s.minDistance = 1f;
        s.maxDistance = range;
        s.rolloffMode = AudioRolloffMode.Custom;
        s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        EditorUtility.SetDirty(s);
        return true;
    }

    // 거리 곡선(가로 = 거리 / 최대 거리): 엔진은 가까이서 빨리 줄고, 로켓은 멀리까지 끈다
    private static AnimationCurve EngineCurve()
    {
        return new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.04f, 0.8f), new Keyframe(0.15f, 0.42f), new Keyframe(0.4f, 0.15f), new Keyframe(1f, 0f));
    }

    private static AnimationCurve RocketCurve()
    {
        return new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.75f), new Keyframe(0.35f, 0.42f), new Keyframe(0.7f, 0.15f), new Keyframe(1f, 0f));
    }

    private static bool SameCurve(AnimationCurve a, AnimationCurve b)
    {
        if (a == null || b == null || a.length != b.length) { return false; }
        for (int i = 0; i < a.length; i++)
        {
            if (!Mathf.Approximately(a[i].time, b[i].time) || !Mathf.Approximately(a[i].value, b[i].value)) { return false; }
        }
        return true;
    }
}
