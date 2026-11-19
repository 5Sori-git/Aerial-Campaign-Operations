using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// CIC 순항미사일 일제 발사(6-276) 프리팹: 루트 = ACO_CicStrike(씬 루트에 하나 — Setup Check가 놓고 Bake가 잇는다),
/// 미사일 풀 SlotCount × SalvoSize(8발), 연출 묶음(대함미사일 LASM 폭발 · 물기둥 복사, 미사일 격추 파편, VLS 발사음).
/// 미사일 한 발: 루트(레이어 0, 콜라이더 없음 — 플레이어 무장 · 범위 피해에 안 맞는다) = 체력(SaccTarget 20 — SAM 한 발 · 대공포 1~2초) + ACO_CicCruiseMissile,
/// 탄체 · 불꽃(LASM 탄체 × CruiseModelScale), 연기 궤적, 적 대공 표적 자리(레이어 25 트리거 — SaccFlight 빌드가 적 대공포 · SAM · VLS의 AAMTargets에 넣는다).
public static partial class ACO_PrefabBuilder
{
    public const string CicStrikeName = "ACO_CicStrike";
    /// 적 대공이 노리는 아군 공중 표적 레이어(수송기 · 건쉽 AAMTarget과 같다). 아군 대공(29 · 30)과 플레이어 락온 목록엔 없다.
    private const int LayerFriendlyAirTarget = 25;
    private const float CruiseModelScale = 1.8f;   // LASM 탄체(약 3.3m) → 약 6m
    /// 순항미사일 발사음(6-290 — Simple CAS 프로젝트 XQ-9B/FX/VLS_Launchsound.ogg를 복사) · 볼륨(6-312 — 1 → 0.7)
    private const string CruiseLaunchClip = "Assets/ACO_Addon/Audio/SFX/VLS_Launchsound.ogg";
    private const float CruiseLaunchVolume = 0.7f;

    public static void CreateCicStrikePrefab()
    {
        GameObject lasm = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_LASM"));
        Transform srcMesh = lasm != null ? lasm.transform.Find("Model/Mesh") : null;
        Transform srcTrail = lasm != null ? lasm.transform.Find("Trail") : null;
        if (srcMesh == null || srcTrail == null)
        {
            Debug.LogError("[ACO] CIC 순항미사일: ACO_Missile_LASM의 Model/Mesh · Trail이 없습니다(탄체 · 연기에 씁니다). 프리팹은 만들지 않았습니다.");
            return;
        }

        var root = new GameObject(CicStrikeName);
        ACO_CicStrike strike = root.AddUdonSharpComponent<ACO_CicStrike>();
        int count = Mathf.Max(strike.SlotCount, 1) * Mathf.Max(strike.SalvoSize, 1);
        var missiles = new ACO_CicCruiseMissile[count];
        Transform pool = new GameObject("Missiles").transform;
        pool.SetParent(root.transform, false);
        for (int i = 0; i < count; i++) { missiles[i] = BuildCruiseMissile(pool, i, srcMesh, srcTrail); }
        strike.Missiles = missiles;
        foreach (ACO_CicCruiseMissile m in missiles) { m.Strike = strike; }

        Transform fx = new GameObject("Fx").transform;
        fx.SetParent(root.transform, false);
        strike.FxBlasts = SubFxCopies(fx, lasm.transform.Find("Explosion"), "Blast", 12);
        // 발사 불꽃 · 연기(6-312): 같은 폭발 복사에서 소리만 뺀다 — 발사 때 폭발음이 같이 나던 것. 한 칸 일제 발사(4발)가 두 번 겹쳐도 되게 8개
        GameObject[] launches = SubFxCopies(fx, lasm.transform.Find("Explosion"), "LaunchFx", 8);
        foreach (GameObject g in launches)
        {
            foreach (AudioSource a in g.GetComponentsInChildren<AudioSource>(true)) { Object.DestroyImmediate(a.gameObject == g ? (Object)a : a.gameObject); }
        }
        strike.FxLaunches = launches;
        strike.FxWaters = SubFxCopies(fx, lasm.transform.Find("Fx_Water"), "Water", 6);
        var wrecks = new ParticleSystem[4];
        GameObject wreckPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_WreckFx_Missile"));
        for (int i = 0; i < wrecks.Length && wreckPrefab != null; i++)
        {
            var w = (GameObject)PrefabUtility.InstantiatePrefab(wreckPrefab, fx);
            w.name = "Wreck_" + i;
            wrecks[i] = w.GetComponent<ParticleSystem>();
        }
        strike.FxWrecks = wrecks;
        // 발사음은 미사일마다 탄체 쪽에(BuildCruiseMissile — 6-312). 예전 공용 발사음은 만들지 않는다
        strike.LaunchSound = null;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PathOf(CicStrikeName));
        ACO_PrefabVersions.Stamp(CicStrikeName);
        DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] CIC 순항미사일 프리팹을 만들었습니다(미사일 {count}발): {AssetDatabase.GetAssetPath(saved)} — Setup Check가 씬 루트에 놓고 Bake가 순양함 VLS · 콘솔에 잇습니다.", saved);
    }

    /// 순항미사일 탄체 모델(6-313, 사용자 "왼쪽 아래 걸로 모델 교체하자, 방향 신경 써서"): CartoonMilitaryModelPack의 Cruise(토마호크 꼴) — LASM · SLCM과 구별된다.
    private const string CruiseBodyPrefab = "Assets/CartoonMilitaryModelPack/Prefebs/CustomPart_Prefebs/Missiles_Prefebs/Cruise_Prefeb.prefab";

    private static void ReplaceCruiseBody(Transform mesh)
    {
        ReplaceMissileBody(mesh, CruiseBodyPrefab, null, "CIC 순항미사일");
    }

    /// LASM에서 복사한 탄체(Mesh 아래 GM84)를 다른 미사일 모델로 바꾼다(6-313 순항미사일 · 6-314 SLCM). 엔진 불꽃(Effects) · 연기 · 발사음 자리는 그대로 두고,
    /// 새 탄체의 꼬리 끝 · 길이 · 굵기 가운데를 GM84에 맞춘다.
    /// - 메시: MeshFilter면 그대로, SkinnedMeshRenderer(날개 뼈대 — Kh-35)면 프리팹 자세 그대로 구워 bakedPath에 저장해 쓴다(같은 경로면 내용만 바꿔 GUID 유지).
    /// - 방향(자동): 가장 긴 축 = 몸통, 그 축의 두 끝 12% 구간 중 단면이 좁은 쪽 = 기수(날개 · 꼬리 날개가 있는 쪽이 넓다), 나머지 두 축 중 긴 쪽 = 날개 폭.
    ///   미사일 루트는 +Z가 비행 방향(ACO_CicCruiseMissile · ACO_Slcm이 LookRotation)이라 기수 → +Z, 날개 폭 → +X로 돌린다(뒤집힘 없는 회전).
    /// 모델 · GM84를 못 찾으면 경고만 하고 예전 탄체를 쓴다.
    private static void ReplaceMissileBody(Transform mesh, string prefabPath, string bakedPath, string label)
    {
        Transform oldBody = mesh.Find("GM84");
        Mesh body = null;
        Material[] mats = null;
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (src != null)
        {
            MeshFilter mf = src.GetComponentInChildren<MeshFilter>(true);
            SkinnedMeshRenderer smr = src.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (mf != null && mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null)
            {
                body = mf.sharedMesh;
                mats = mf.GetComponent<MeshRenderer>().sharedMaterials;
            }
            else if (smr != null && smr.sharedMesh != null && !string.IsNullOrEmpty(bakedPath))
            {
                body = BakeSkinnedBody(src, bakedPath);
                mats = smr.sharedMaterials;
            }
        }
        if (oldBody == null || body == null)
        {
            Debug.LogWarning("[ACO] " + label + ": 모델(" + prefabPath + ") 또는 LASM 탄체(GM84)를 찾지 못해 예전 탄체를 그대로 씁니다.");
            return;
        }
        // GM84가 Mesh 안에서 차지하는 상자(메시 꼭짓점 상자 8점을 Mesh 공간으로)
        Bounds mb = oldBody.GetComponent<MeshFilter>().sharedMesh.bounds;
        Matrix4x4 toMesh = mesh.worldToLocalMatrix * oldBody.localToWorldMatrix;
        var old = new Bounds(toMesh.MultiplyPoint3x4(mb.min), Vector3.zero);
        for (int i = 0; i < 8; i++) { old.Encapsulate(toMesh.MultiplyPoint3x4(BoxCorner(mb, i))); }
        ShadowCastingMode shadows = oldBody.GetComponent<MeshRenderer>().shadowCastingMode;
        Object.DestroyImmediate(oldBody.gameObject);

        Quaternion rot = BodyRotation(body);
        Bounds nb = body.bounds;
        // 돌린 뒤 상자(Mesh 공간 방향, 크기 1)
        var rb = new Bounds(rot * BoxCorner(nb, 0), Vector3.zero);
        for (int i = 1; i < 8; i++) { rb.Encapsulate(rot * BoxCorner(nb, i)); }
        float scale = old.size.z / Mathf.Max(rb.size.z, 0.001f);
        var go = new GameObject("Body");
        go.transform.SetParent(mesh, false);
        go.transform.localRotation = rot;
        go.transform.localScale = Vector3.one * scale;
        go.transform.localPosition = new Vector3(old.center.x - rb.center.x * scale, old.center.y - rb.center.y * scale, old.min.z - rb.min.z * scale);
        go.AddComponent<MeshFilter>().sharedMesh = body;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = shadows;
    }

    private static Vector3 BoxCorner(Bounds b, int i)
    {
        return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    /// 메시 공간 → 미사일 공간 회전: 기수(긴 축의 좁은 끝) → +Z, 날개 폭(남은 축 중 긴 쪽) → +X.
    private static Quaternion BodyRotation(Mesh m)
    {
        Bounds b = m.bounds;
        Vector3 s = b.size;
        int ax = s.x >= s.y && s.x >= s.z ? 0 : (s.y >= s.z ? 1 : 2);
        int o1 = (ax + 1) % 3;
        int o2 = (ax + 2) % 3;
        int span = s[o1] >= s[o2] ? o1 : o2;
        float lo = b.min[ax];
        float hi = b.max[ax];
        float band = (hi - lo) * 0.12f;
        float wHi = 0f;
        float wLo = 0f;
        foreach (Vector3 p in m.vertices)
        {
            float w = Mathf.Max(Mathf.Abs(p[o1] - b.center[o1]), Mathf.Abs(p[o2] - b.center[o2]));
            if (p[ax] > hi - band) { wHi = Mathf.Max(wHi, w); }
            if (p[ax] < lo + band) { wLo = Mathf.Max(wLo, w); }
        }
        Vector3 nose = Vector3.zero;
        nose[ax] = wHi <= wLo ? 1f : -1f;
        Vector3 wing = Vector3.zero;
        wing[span] = 1f;
        // Q: 미사일 공간 → 메시 공간(Z → 기수, X → 날개 폭). 그 역이 메시 → 미사일
        Quaternion q = Quaternion.LookRotation(nose, Vector3.Cross(nose, wing));
        return Quaternion.Inverse(q);
    }

    /// 스킨 메시(날개 뼈대)를 프리팹 자세 그대로 구워 메시 에셋으로 둔다. 이미 있으면 내용만 바꾼다(GUID 유지 — 씬 · 프리팹 참조가 안 끊긴다).
    private static Mesh BakeSkinnedBody(GameObject src, string bakedPath)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        try
        {
            SkinnedMeshRenderer smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            baked.RecalculateBounds();
            baked.name = System.IO.Path.GetFileNameWithoutExtension(bakedPath);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(bakedPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(baked, existing);
                existing.name = baked.name;
                Object.DestroyImmediate(baked);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(baked, bakedPath);
            return baked;
        }
        finally { Object.DestroyImmediate(inst); }
    }

    private static ACO_CicCruiseMissile BuildCruiseMissile(Transform parent, int index, Transform srcMesh, Transform srcTrail)
    {
        var go = new GameObject("CruiseMissile_" + index);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -200f, 0f);

        Transform model = new GameObject("Model").transform;
        model.SetParent(go.transform, false);
        Transform mesh = Object.Instantiate(srcMesh, model);
        mesh.name = "Mesh";
        mesh.localPosition = srcMesh.localPosition * CruiseModelScale;
        mesh.localRotation = Quaternion.identity;   // LASM은 Model(90°) · Mesh(270°)가 서로 상쇄 — 루트 기준 회전이 없다(SLCM과 같다)
        mesh.localScale = srcMesh.localScale * CruiseModelScale;
        ReplaceCruiseBody(mesh);
        // 발사음(6-312, 사용자 "VLS 발사음만 모델 쪽 런치 사운드에 넣고 나머지 사운드는 끄자, 소리는 70%"):
        // LASM에서 딸려 온 소리(LaunchMissile.wav — 탄체가 켜질 때 저절로 나던 것)는 지우고, VLS 발사 소리를 볼륨 0.7로 탄체 쪽에 둔다.
        // 저절로 재생하지 않는다 — 미사일이 발사 순간에만 Play(늦게 들어와 날던 미사일이 켜질 때 안 나게)
        foreach (AudioSource a in mesh.GetComponentsInChildren<AudioSource>(true)) { Object.DestroyImmediate(a.gameObject == mesh.gameObject ? (Object)a : a.gameObject); }
        AudioSource launch = CreateBlastSound(model, "LaunchSound", CruiseLaunchClip, 300f, 9000f);
        launch.volume = CruiseLaunchVolume;
        model.gameObject.SetActive(false);

        // 연기 궤적은 탄체 밖 — 나는 동안만 뿜고, 멈추면 남은 연기는 수명대로 사라진다(SLCM과 같다)
        Transform trail = Object.Instantiate(srcTrail, go.transform);
        trail.name = "Trail";
        trail.localPosition = srcTrail.localPosition * CruiseModelScale;
        trail.localRotation = srcTrail.localRotation;
        ParticleSystem trailPs = trail.GetComponent<ParticleSystem>();
        ParticleSystem.EmissionModule emission = trailPs.emission;
        emission.enabled = true;
        ParticleSystem.MainModule main = trailPs.main;
        main.playOnAwake = false;

        // 적 대공 표적 자리 — "AAMTarget" 이름은 SAV Target 애니메이터가 다루므로 다른 이름
        GameObject aim = NewLockMarker(go.transform, "CruiseAim", LayerFriendlyAirTarget, 6f);
        aim.SetActive(false);

        AddTargetAnimator(go);
        SaccTarget hp = go.AddUdonSharpComponent<SaccTarget>();
        ACO_CicCruiseMissile m = go.AddUdonSharpComponent<ACO_CicCruiseMissile>();
        hp.Health = 20f;   // SAM 한 발(45~150) · 대공포 1~2초
        hp.ArmorStrength = 1f;
        hp.RespawnDelay = 0f;
        hp.PredictExplosion = false;   // 파괴는 소유자 기준(6-248) — 켜 두면 틀릴 때 쏜 화면에서만 투명 · 무적(재점검 2026-11-18)
        hp.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { m };
        m.TargetHealth = hp;
        m.Model = model.gameObject;
        m.Trail = trailPs;
        m.AimPoint = aim;
        m.LaunchAudio = launch;
        return m;
    }
}
