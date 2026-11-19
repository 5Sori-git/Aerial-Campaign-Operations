using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
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
        // 발사음: VLS 발사 소리(6-290, 사용자 "순항미사일 발사 소리는 이걸로 바꿔줘" — Simple CAS 프로젝트 XQ-9B/FX/VLS_Launchsound.ogg를 복사)
        strike.LaunchSound = CreateBlastSound(fx, "LaunchSound", "Assets/ACO_Addon/Audio/SFX/VLS_Launchsound.ogg", 300f, 9000f);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PathOf(CicStrikeName));
        ACO_PrefabVersions.Stamp(CicStrikeName);
        DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] CIC 순항미사일 프리팹을 만들었습니다(미사일 {count}발): {AssetDatabase.GetAssetPath(saved)} — Setup Check가 씬 루트에 놓고 Bake가 순양함 VLS · 콘솔에 잇습니다.", saved);
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
        return m;
    }
}
