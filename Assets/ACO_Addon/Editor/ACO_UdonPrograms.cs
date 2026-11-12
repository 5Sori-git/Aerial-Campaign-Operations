using System;
using System.Collections.Generic;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

/// U# 스크립트마다 짝이 되는 프로그램 에셋(Udon/<이름>.asset)을 만들고, 프로그램 없이 만들어진 U# 컴포넌트를 다시 잇는다.
/// Unity를 켜지 않고 스크립트만 추가하면 프로그램 에셋이 생기지 않는다. 그 상태에서 [고치기]·프리팹 빌더가 그 컴포넌트를 붙이면
/// 뒤의 UdonBehaviour가 프로그램 없이(None) 남는다("Selected U# behaviour program source reference is not valid").
/// 에셋을 만든 뒤 다시 이으면 인스펙터에 넣어 둔 값은 그대로다(값은 U# 컴포넌트 쪽에 있다).
public static class ACO_UdonPrograms
{
    private const string ScriptDir = "Assets/ACO_Addon/Scripts";
    private const string ProgramDir = "Assets/ACO_Addon/Udon";
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";

    private static Dictionary<MonoScript, UdonSharpProgramAsset> ProgramsByScript()
    {
        var map = new Dictionary<MonoScript, UdonSharpProgramAsset>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(UdonSharpProgramAsset)))
        {
            var p = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null && p.sourceCsScript != null && !map.ContainsKey(p.sourceCsScript)) { map.Add(p.sourceCsScript, p); }
        }
        return map;
    }

    /// 프로그램 에셋이 없는 U# 스크립트(추상 클래스 제외).
    public static List<MonoScript> MissingPrograms()
    {
        Dictionary<MonoScript, UdonSharpProgramAsset> have = ProgramsByScript();
        var list = new List<MonoScript>();
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { ScriptDir }))
        {
            var s = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (s == null || have.ContainsKey(s)) { continue; }
            Type t = s.GetClass();
            if (t == null || t.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(t)) { continue; }
            list.Add(s);
        }
        return list;
    }

    private static bool IsBroken(UdonSharpBehaviour proxy)
    {
        if (proxy == null) { return false; }
        UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        return backing != null && backing.programSource == null;
    }

    /// 씬에서 뒤의 UdonBehaviour에 프로그램이 없는 U# 컴포넌트.
    public static List<UdonSharpBehaviour> BrokenInScene()
    {
        var list = new List<UdonSharpBehaviour>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (UdonSharpBehaviour b in root.GetComponentsInChildren<UdonSharpBehaviour>(true)) { if (IsBroken(b)) { list.Add(b); } }
        }
        return list;
    }

    /// 씬의 U# 컴포넌트 뒤에 붙은 UdonBehaviour(프로그램이 빠졌어도 지우면 안 되는 것 — 다시 이으면 된다).
    public static HashSet<UdonBehaviour> SceneBackings()
    {
        var set = new HashSet<UdonBehaviour>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (UdonSharpBehaviour b in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
                if (backing != null) { set.Add(backing); }
            }
        }
        return set;
    }

    /// Prefabs 폴더에서 끊긴 U# 컴포넌트가 있는 프리팹 경로.
    public static List<string> BrokenPrefabs()
    {
        var list = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) { continue; }
            foreach (UdonSharpBehaviour b in go.GetComponentsInChildren<UdonSharpBehaviour>(true)) { if (IsBroken(b)) { list.Add(path); break; } }
        }
        return list;
    }

    /// 없는 프로그램 에셋을 만든다. 만들었으면 U# 컴파일까지 한다. 만든 수.
    public static int CreateMissing()
    {
        List<MonoScript> missing = MissingPrograms();
        if (missing.Count == 0) { return 0; }
        if (!AssetDatabase.IsValidFolder(ProgramDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Udon"); }
        foreach (MonoScript s in missing)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ProgramDir}/{s.name}.asset");
            var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = s;
            // 새 스크립트는 지금 형식으로 쓰였다 — 버전을 비워 두면 U# 업그레이드 단계가 끝날 때까지 값 복사가 막힌다(그 사이 값이 기본값으로 덮였다)
            program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            AssetDatabase.CreateAsset(program, path);
            Debug.Log($"[ACO] U# 프로그램 에셋을 만들었습니다: {path}");
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ClearUdonSharpCaches();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions());   // 끝날 때까지 기다린다
        return missing.Count;
    }

    /// U#은 프로그램 에셋 목록을 캐시한다 — 새로 만든 에셋이 바로 보이게 비운다(내부 함수라 리플렉션).
    private static void ClearUdonSharpCaches()
    {
        try
        {
            MethodInfo m = typeof(UdonSharpProgramAsset).GetMethod("ClearProgramAssetCache", BindingFlags.NonPublic | BindingFlags.Static);
            if (m != null) { m.Invoke(null, null); }
        }
        catch (Exception e) { Debug.LogWarning("[ACO] U# 캐시 비우기 실패(무시): " + e.Message); }
    }

    /// 끊긴 U# 컴포넌트 하나를 프로그램에 다시 잇는다.
    private static bool Relink(UdonSharpBehaviour proxy, Dictionary<MonoScript, UdonSharpProgramAsset> programs)
    {
        UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        if (backing == null || backing.programSource != null) { return false; }
        MonoScript script = MonoScript.FromMonoBehaviour(proxy);
        if (script == null || !programs.TryGetValue(script, out UdonSharpProgramAsset program)) { return false; }
        // 컴파일이 안 끝났으면 잇지 않는다: 이은 뒤 값 복사(컴포넌트 → Udon)가 실패하면, 다음 로드 때 U#가 빈 Udon 쪽 값으로
        // 컴포넌트를 덮어 인스펙터 값이 기본값으로 돌아간다. 다음 검사([고치기])에서 다시 시도한다.
        if (program.ScriptVersion < UdonSharpProgramVersion.CurrentVersion || program.CompiledVersion < UdonSharpProgramVersion.CurrentVersion)
        {
            Debug.LogWarning($"[ACO] U# 프로그램 '{program.name}' 컴파일이 아직 안 끝나 '{proxy.name}'를 잇지 않았습니다 — 컴파일 뒤 Setup Check [고치기]를 다시 누르세요.", proxy);
            return false;
        }
        backing.programSource = program;
        var so = new SerializedObject(backing);
        SerializedProperty serialized = so.FindProperty("serializedProgramAsset");
        if (serialized != null) { serialized.objectReferenceValue = program.SerializedProgramAsset; }
        so.ApplyModifiedPropertiesWithoutUndo();
        try { UdonSharpEditorUtility.CopyProxyToUdon(proxy); }
        catch (Exception e)
        {
            // 값을 못 옮겼으면 연결을 되돌린다(이은 채로 두면 값이 덮인다)
            backing.programSource = null;
            if (serialized != null) { so.Update(); so.FindProperty("serializedProgramAsset").objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo(); }
            Debug.LogWarning($"[ACO] '{proxy.name}' 값 옮기기 실패로 잇기를 되돌렸습니다 — 컴파일 뒤 다시 [고치기]: {e.Message}", proxy);
            return false;
        }
        EditorUtility.SetDirty(backing);
        EditorUtility.SetDirty(proxy);
        return true;
    }

    /// 프리팹(먼저 — 씬 인스턴스가 따라온다)과 씬의 끊긴 U# 컴포넌트를 다시 잇는다. 이은 수.
    public static int RelinkAll()
    {
        Dictionary<MonoScript, UdonSharpProgramAsset> programs = ProgramsByScript();
        int count = 0;
        foreach (string path in BrokenPrefabs())
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = 0;
                foreach (UdonSharpBehaviour b in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
                {
                    // 중첩 프리팹 안의 것은 그 프리팹 차례에 고친다(여기서 고치면 덮어쓰기가 남는다)
                    if (PrefabUtility.IsPartOfPrefabInstance(b)) { continue; }
                    if (Relink(b, programs)) { n++; }
                }
                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] U# 프로그램 다시 잇기: {path} ({n}개)");
                    count += n;
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int scene = 0;
        foreach (UdonSharpBehaviour b in BrokenInScene()) { if (Relink(b, programs)) { scene++; } }
        if (scene > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[ACO] U# 프로그램 다시 잇기: 씬 컴포넌트 {scene}개. 씬을 저장하세요.");
        }
        return count + scene;
    }

    /// Setup Check [고치기]: 에셋 만들기 + 다시 잇기.
    public static void Fix()
    {
        int made = CreateMissing();
        int linked = RelinkAll();
        Debug.Log($"[ACO] U# 프로그램: 에셋 {made}개를 만들고 컴포넌트 {linked}개를 다시 이었습니다.");
    }
}
