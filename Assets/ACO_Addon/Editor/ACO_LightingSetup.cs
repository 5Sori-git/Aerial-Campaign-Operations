using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#endif

/// 조명 한 번에 설정(ACO → Lighting → Set Up Lighting, Tools 창 "조명 설정"):
///  - 태양: 방향광 하나를 Mixed(직사광·그림자는 실시간 — 런타임에 생기는 지형·기체도 밝다, 간접광만 굽는다), 따뜻한 흰빛, 부드러운 그림자, 고도 약 50°
///  - 하늘: 절차적 스카이박스(대기 두께·노출·해 원반) + 환경광(하늘·지평·땅 3색) + 기본 반사 = 스카이박스 + 원거리 안개(공중 원근감)
///  - 굽기 설정: 실시간 GI 끔(무겁다), 굽는 GI = 간접광만(Baked Indirect), 해상도 4텍셀/m, AO, 2048 아틀라스
///  - 격납고: HangarPoint(없으면 BriefingPoint)에서 콜라이더로 벽·천장을 재서 방 상자를 정하고 — 천장 구운 조명 격자, 라이트 프로브 격자(기체·아바타용), 상자 투영 반사 프로브
///  - 항모(HZI_CVN…): 렌더러를 GI 기여/반사 프로브 정적으로, 라이트맵 UV가 없는 모델은 Generate Lightmap UVs를 켠다
///  - 포스트 프로세싱(Post Processing 패키지가 있을 때만): 전역 볼륨(블룸, ACES 톤매핑 + 노출 보정, 채도·대비 약간) + 레퍼런스 카메라에 레이어
/// 마지막에 [라이트 굽기]로 굽는다(항모 내부 간접광·격납고 조명·반사 프로브·환경광).
public static class ACO_LightingSetup
{
    private const string GenDir = "Assets/ACO_Addon/Generated";
    private const string RootName = "ACO_Lighting";
    private const string HangarName = "ACO_HangarLighting";

    // 하늘·안개 색(맑은 낮, 바다 위)
    private static readonly Color SunColor = new Color(1f, 0.95f, 0.87f);
    private static readonly Color AmbientSky = new Color(0.58f, 0.68f, 0.82f);
    private static readonly Color AmbientEquator = new Color(0.52f, 0.56f, 0.6f);
    private static readonly Color AmbientGround = new Color(0.28f, 0.27f, 0.25f);
    private static readonly Color FogColor = new Color(0.66f, 0.75f, 0.84f);
    private static readonly Color HangarLightColor = new Color(1f, 0.94f, 0.82f);

    [MenuItem("ACO/Lighting/Set Up Lighting")]
    public static void SetUp()
    {
        var log = new System.Text.StringBuilder("[ACO] 조명 설정:");
        Light sun = SetUpSun(log);
        SetUpSky(sun, log);
        SetUpBakeSettings(log);
        SetUpCarrier(log);
        SetUpHangar(log);
        SetUpPostProcessing(log);
        EditorSceneManagerMarkDirty();
        AssetDatabase.SaveAssets();
        log.Append("\n→ 다음: Tools 창 [라이트 굽기](또는 Window → Rendering → Lighting → Generate Lighting).");
        Debug.Log(log.ToString());
    }

    [MenuItem("ACO/Lighting/Bake Lighting")]
    public static void Bake()
    {
        if (Lightmapping.isRunning) { Debug.Log("[ACO] 이미 굽는 중입니다."); return; }
        Lightmapping.BakeAsync();
        Debug.Log("[ACO] 라이트 굽기를 시작했습니다(오른쪽 아래 진행 막대). 항모가 커서 몇 분 걸릴 수 있습니다.");
    }

    public static bool IsSetUp()
    {
        Light sun = RenderSettings.sun;
        return sun != null && sun.lightmapBakeType == LightmapBakeType.Mixed && RenderSettings.fog && FindInScene(HangarName) != null;
    }

    // ───────────── 태양 ─────────────

    private static Light SetUpSun(System.Text.StringBuilder log)
    {
        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            foreach (Light l in Object.FindObjectsOfType<Light>(true)) { if (l.type == LightType.Directional) { sun = l; break; } }
        }
        if (sun == null)
        {
            var go = new GameObject("Sun");
            Undo.RegisterCreatedObjectUndo(go, "ACO sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            go.transform.rotation = Quaternion.Euler(50f, 210f, 0f);
        }
        Undo.RecordObject(sun, "ACO sun");
        Undo.RecordObject(sun.transform, "ACO sun");
        // Baked였으면 런타임 지형·기체에 직사광이 안 들어갔다 — Mixed(직사광 실시간 + 간접광 굽기)
        sun.lightmapBakeType = LightmapBakeType.Mixed;
        sun.color = SunColor;
        sun.intensity = 1.25f;
        sun.bounceIntensity = 1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.8f;
        sun.shadowBias = 0.05f;
        sun.shadowNormalBias = 0.4f;
        Vector3 e = sun.transform.eulerAngles;
        if (e.x < 25f || e.x > 75f) { sun.transform.rotation = Quaternion.Euler(50f, e.y, 0f); }
        RenderSettings.sun = sun;
        EditorUtility.SetDirty(sun);
        log.Append($"\n- 태양 '{sun.name}': Mixed, 1.25, 부드러운 그림자, 고도 {sun.transform.eulerAngles.x:0}°");
        return sun;
    }

    // ───────────── 하늘·환경광·안개 ─────────────

    private static void SetUpSky(Light sun, System.Text.StringBuilder log)
    {
        string path = $"{GenDir}/ACO_Sky.mat";
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural")) { name = "ACO_Sky" };
            sky.SetFloat("_SunDisk", 2f);            // 고품질 해 원반
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.95f);
            sky.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));
            sky.SetColor("_GroundColor", new Color(0.33f, 0.38f, 0.43f));
            sky.SetFloat("_Exposure", 1.25f);
            AssetDatabase.CreateAsset(sky, path);
        }
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;   // 굽지 않아도 바로 보이는 환경광(굽기 간접광에도 쓰인다)
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 256;
        RenderSettings.reflectionIntensity = 1f;
        // 공중 원근감: 20km 밖이 하늘빛으로 옅어진다
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.000028f;
        RenderSettings.fogColor = FogColor;
        log.Append("\n- 하늘: 절차적 스카이박스 ACO_Sky, 환경광 3색(하늘/지평/땅), 기본 반사 = 스카이박스, 원거리 안개(지수², 0.000028)");
    }

    // ───────────── 굽기 설정 ─────────────

    private static void SetUpBakeSettings(System.Text.StringBuilder log)
    {
        LightingSettings ls = null;
        try { ls = Lightmapping.lightingSettings; } catch (System.Exception) { }   // 씬에 설정 에셋이 없으면 예외
        if (ls == null || !AssetDatabase.Contains(ls))
        {
            string path = $"{GenDir}/ACO_LightingSettings.lighting";
            ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null)
            {
                ls = new LightingSettings { name = "ACO_LightingSettings" };
                AssetDatabase.CreateAsset(ls, path);
            }
            Lightmapping.lightingSettings = ls;
        }
        Undo.RecordObject(ls, "ACO lighting settings");
        ls.realtimeGI = false;                                  // 실시간 GI(Enlighten)는 무겁고 이 월드엔 필요 없다
        ls.bakedGI = true;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;      // 직사광·그림자는 실시간, 간접광만 굽는다
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.lightmapResolution = 4f;
        ls.lightmapPadding = 4;
        ls.lightmapMaxSize = 2048;
        ls.directSampleCount = 32;
        ls.indirectSampleCount = 256;
        ls.environmentSampleCount = 256;
        ls.maxBounces = 2;
        ls.ao = true;
        ls.aoMaxDistance = 1.5f;
        ls.aoExponentIndirect = 1f;
        ls.aoExponentDirect = 0f;
        ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.lightmapCompression = LightmapCompression.NormalQuality;
        ls.autoGenerate = false;
        EditorUtility.SetDirty(ls);
        log.Append("\n- 굽기: 실시간 GI 끔, Baked Indirect, GPU 라이트매퍼, 4텍셀/m, AO, 최대 2048");
    }

    // ───────────── 항모 ─────────────

    private static void SetUpCarrier(System.Text.StringBuilder log)
    {
        int renderers = 0;
        var models = new HashSet<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("HZI_CVN")) { continue; }
                foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    GameObject go = r.gameObject;
                    StaticEditorFlags f = GameObjectUtility.GetStaticEditorFlags(go);
                    StaticEditorFlags want = f | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
                    if (want != f) { Undo.RecordObject(go, "ACO carrier static"); GameObjectUtility.SetStaticEditorFlags(go, want); }
                    r.receiveGI = ReceiveGI.Lightmaps;
                    renderers++;
                    MeshFilter mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.uv2.Length == 0)
                    {
                        string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                        if (!string.IsNullOrEmpty(p)) { models.Add(p); }
                    }
                }
            }
        }
        foreach (string p in models)
        {
            if (AssetImporter.GetAtPath(p) is ModelImporter mi && !mi.generateSecondaryUV)
            {
                mi.generateSecondaryUV = true;
                mi.SaveAndReimport();
            }
        }
        log.Append(renderers > 0
            ? $"\n- 항모: 렌더러 {renderers}개를 GI 기여·반사 프로브 정적으로" + (models.Count > 0 ? $", 라이트맵 UV 생성 켬({models.Count}개 모델)" : "")
            : "\n- 항모(HZI_CVN…)를 찾지 못했습니다 — 격납고 벽·천장을 직접 Contribute GI 정적으로 표시하세요");
    }

    // ───────────── 격납고 ─────────────

    private static void SetUpHangar(System.Text.StringBuilder log)
    {
        // 이미 격납고 조명이 있으면(손으로 옮기거나 더 놓았을 수 있다) 지우지 않고 조명 배치에 맞춰 프로브만 다시 깐다.
        GameObject existing = FindInScene(HangarName);
        if (existing != null && existing.GetComponentsInChildren<Light>(true).Length > 0)
        {
            RefitProbes(existing, log);
            return;
        }
        Transform point = FindPoint("HangarPoint");
        if (point == null) { point = FindPoint("BriefingPoint"); }
        if (point == null) { log.Append("\n- 격납고: HangarPoint/BriefingPoint를 찾지 못해 건너뜀"); return; }

        // 방 상자: 사람 눈높이에서 여섯 방향으로 콜라이더를 재고, 못 맞히면 기본 크기
        Physics.SyncTransforms();
        Vector3 eye = point.position + Vector3.up * 1.6f;
        float px = Cast(eye, Vector3.right, 30f), nx = Cast(eye, Vector3.left, 30f);
        float pz = Cast(eye, Vector3.forward, 30f), nz = Cast(eye, Vector3.back, 30f);
        float up = Cast(eye, Vector3.up, 6f), down = Cast(eye, Vector3.down, 1.6f);
        var min = new Vector3(eye.x - nx, eye.y - down, eye.z - nz);
        var max = new Vector3(eye.x + px, eye.y + up, eye.z + pz);
        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;

        GameObject old = FindInScene(HangarName);
        if (old != null) { Undo.DestroyObjectImmediate(old); }
        var root = new GameObject(HangarName);
        Undo.RegisterCreatedObjectUndo(root, "ACO hangar lighting");
        root.transform.position = center;

        // 천장 구운 조명: 약 10m 격자(최대 30개), 천장 0.6m 아래. 구운 조명이라 실행 중 비용 없음(기체·아바타는 라이트 프로브로 받는다)
        int cx = Mathf.Clamp(Mathf.RoundToInt(size.x / 10f), 1, 6);
        int cz = Mathf.Clamp(Mathf.RoundToInt(size.z / 10f), 1, 5);
        float range = Mathf.Clamp(size.y * 2.2f, 8f, 25f);
        for (int i = 0; i < cx; i++)
        {
            for (int k = 0; k < cz; k++)
            {
                var go = new GameObject($"CeilingLight_{i}_{k}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(min.x + size.x * (i + 0.5f) / cx, max.y - 0.6f, min.z + size.z * (k + 0.5f) / cz);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.color = HangarLightColor;
                l.intensity = 1.6f;
                l.range = range;
                l.shadows = LightShadows.Soft;   // 구울 때만 쓰인다(기둥·기체 그림자)
            }
        }

        int probeCount = BuildProbes(root, min, max);
        log.Append($"\n- 격납고('{point.name}' 둘레): 방 {size.x:0}x{size.y:0}x{size.z:0}m — 천장 구운 조명 {cx * cz}개, 라이트 프로브 {probeCount}개, 상자 투영 반사 프로브"
            + " (조명이 안 맞으면 CeilingLight를 직접 옮긴 뒤 [격납고 프로브 맞추기])");
    }

    /// 격납고 조명(손으로 놓은 것 포함)에 맞춰 라이트 프로브·반사 프로브만 다시 깐다 — 조명은 건드리지 않는다.
    [MenuItem("ACO/Lighting/Refit Hangar Probes")]
    public static void RefitHangarProbes()
    {
        GameObject root = FindInScene(HangarName);
        if (root == null) { Debug.LogWarning($"[ACO] {HangarName}이 없습니다 — 먼저 조명 설정을 하세요."); return; }
        var log = new System.Text.StringBuilder("[ACO] 격납고 프로브 맞추기:");
        RefitProbes(root, log);
        EditorSceneManagerMarkDirty();
        log.Append("\n→ 다음: [라이트 굽기].");
        Debug.Log(log.ToString(), root);
    }

    /// 방 상자 = 조명들의 수평 범위 + 조명 간격의 절반(벽에 먼저 닿으면 벽까지), 바닥 = 조명 아래로 쏜 레이가 닿은 가장 낮은 곳, 천장 = 가장 높은 조명 + 0.8m.
    private static void RefitProbes(GameObject root, System.Text.StringBuilder log)
    {
        var lights = new List<Light>();
        foreach (Light l in root.GetComponentsInChildren<Light>(true)) { if (l.type != LightType.Directional) { lights.Add(l); } }
        if (lights.Count == 0) { log.Append("\n- 격납고: 조명이 없어 건너뜀"); return; }
        Physics.SyncTransforms();

        var b = new Bounds(lights[0].transform.position, Vector3.zero);
        foreach (Light l in lights) { b.Encapsulate(l.transform.position); }
        float floorY = float.PositiveInfinity;
        foreach (Light l in lights)
        {
            RaycastHit hit;
            if (Physics.Raycast(l.transform.position, Vector3.down, out hit, 60f, ~0, QueryTriggerInteraction.Ignore)) { floorY = Mathf.Min(floorY, hit.point.y); }
        }
        if (float.IsInfinity(floorY)) { floorY = b.min.y - 4f; }

        // 조명 간격(가장 가까운 이웃 거리의 평균)의 절반만큼 바깥까지
        float spacing = 8f;
        if (lights.Count > 1)
        {
            float sum = 0f;
            foreach (Light a in lights)
            {
                float best = float.MaxValue;
                foreach (Light c in lights) { if (c != a) { best = Mathf.Min(best, Vector3.Distance(a.transform.position, c.transform.position)); } }
                sum += best;
            }
            spacing = sum / lights.Count;
        }
        float margin = Mathf.Clamp(spacing * 0.5f, 2f, 8f);
        var min = new Vector3(b.min.x - margin, floorY, b.min.z - margin);
        var max = new Vector3(b.max.x + margin, b.max.y + 0.8f, b.max.z + margin);

        // 벽이 그보다 가까우면 벽까지(가운데 눈높이에서 네 방향)
        var eye = new Vector3(b.center.x, floorY + 1.6f, b.center.z);
        RaycastHit w;
        if (Physics.Raycast(eye, Vector3.right, out w, 200f, ~0, QueryTriggerInteraction.Ignore)) { max.x = Mathf.Min(max.x, w.point.x); }
        if (Physics.Raycast(eye, Vector3.left, out w, 200f, ~0, QueryTriggerInteraction.Ignore)) { min.x = Mathf.Max(min.x, w.point.x); }
        if (Physics.Raycast(eye, Vector3.forward, out w, 200f, ~0, QueryTriggerInteraction.Ignore)) { max.z = Mathf.Min(max.z, w.point.z); }
        if (Physics.Raycast(eye, Vector3.back, out w, 200f, ~0, QueryTriggerInteraction.Ignore)) { min.z = Mathf.Max(min.z, w.point.z); }

        int count = BuildProbes(root, min, max);
        int notBaked = 0;
        foreach (Light l in lights) { if (l.lightmapBakeType != LightmapBakeType.Baked) { notBaked++; } }
        Vector3 size = max - min;
        log.Append($"\n- 격납고: 조명 {lights.Count}개에 맞춰 방 {size.x:0}x{size.y:0}x{size.z:0}m — 라이트 프로브 {count}개, 상자 투영 반사 프로브 다시 깖(조명은 그대로)");
        if (notBaked > 0) { log.Append($"\n  ! 조명 {notBaked}개가 Baked가 아닙니다 — 실행 중 비용이 들어요(구운 조명으로 하려면 Mode = Baked)"); }
    }

    /// 방 상자(min~max, 월드)에 라이트 프로브 격자와 상자 투영 반사 프로브를 만든다(예전 것은 지운다). 프로브 수.
    private static int BuildProbes(GameObject root, Vector3 min, Vector3 max)
    {
        foreach (string n in new[] { "LightProbes", "ReflectionProbe" })
        {
            Transform oldChild = root.transform.Find(n);
            if (oldChild != null) { Undo.DestroyObjectImmediate(oldChild.gameObject); }
        }
        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;

        // 라이트 프로브: 바닥 위 0.5m·2m·천장 아래, 약 5m 간격(최대 8 x 3 x 8)
        var probes = new List<Vector3>();
        int prX = Mathf.Clamp(Mathf.RoundToInt(size.x / 5f) + 1, 2, 8);
        int prZ = Mathf.Clamp(Mathf.RoundToInt(size.z / 5f) + 1, 2, 8);
        float[] ys = { min.y + 0.5f, min.y + 2f, Mathf.Max(min.y + 3f, max.y - 1f) };
        for (int i = 0; i < prX; i++)
        {
            for (int k = 0; k < prZ; k++)
            {
                foreach (float y in ys)
                {
                    probes.Add(new Vector3(Mathf.Lerp(min.x + 0.5f, max.x - 0.5f, i / (float)(prX - 1)), y, Mathf.Lerp(min.z + 0.5f, max.z - 0.5f, k / (float)(prZ - 1))));
                }
            }
        }
        var probeGo = new GameObject("LightProbes");
        Undo.RegisterCreatedObjectUndo(probeGo, "ACO hangar probes");
        probeGo.transform.SetParent(root.transform, false);
        probeGo.transform.position = center;
        var local = new Vector3[probes.Count];
        for (int i = 0; i < probes.Count; i++) { local[i] = probeGo.transform.InverseTransformPoint(probes[i]); }
        probeGo.AddComponent<LightProbeGroup>().probePositions = local;

        // 반사 프로브: 방 상자 투영(벽·바닥에 비치는 반사가 방 모양에 맞게)
        var rpGo = new GameObject("ReflectionProbe");
        Undo.RegisterCreatedObjectUndo(rpGo, "ACO hangar probes");
        rpGo.transform.SetParent(root.transform, false);
        rpGo.transform.SetPositionAndRotation(center, Quaternion.identity);
        ReflectionProbe rp = rpGo.AddComponent<ReflectionProbe>();
        rp.mode = ReflectionProbeMode.Baked;
        rp.size = size + Vector3.one * 0.5f;
        rp.boxProjection = true;
        rp.resolution = 128;
        rp.importance = 2;
        rp.blendDistance = 1f;
        return probes.Count;
    }

    private static float Cast(Vector3 from, Vector3 dir, float fallback)
    {
        RaycastHit hit;
        if (Physics.Raycast(from, dir, out hit, 150f, ~0, QueryTriggerInteraction.Ignore)) { return Mathf.Max(hit.distance - 0.3f, 1f); }
        return fallback;
    }

    // ───────────── 포스트 프로세싱 ─────────────

    private static void SetUpPostProcessing(System.Text.StringBuilder log)
    {
#if UNITY_POST_PROCESSING_STACK_V2
        string path = $"{GenDir}/ACO_PostProcess.asset";
        PostProcessProfile profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        Bloom bloom = profile.GetSetting<Bloom>() ?? AddSetting<Bloom>(profile);
        bloom.enabled.Override(true);
        bloom.intensity.Override(1.2f);
        bloom.threshold.Override(1.1f);
        bloom.softKnee.Override(0.6f);
        ColorGrading cg = profile.GetSetting<ColorGrading>() ?? AddSetting<ColorGrading>(profile);
        cg.enabled.Override(true);
        cg.gradingMode.Override(GradingMode.HighDefinitionRange);
        cg.tonemapper.Override(Tonemapper.ACES);
        cg.postExposure.Override(0.45f);   // ACES는 조금 어두워지므로 보정
        cg.temperature.Override(4f);
        cg.saturation.Override(8f);
        cg.contrast.Override(8f);
        EditorUtility.SetDirty(profile);

        GameObject vol = FindInScene("ACO_PostProcess");
        if (vol == null)
        {
            vol = new GameObject("ACO_PostProcess");
            Undo.RegisterCreatedObjectUndo(vol, "ACO post process");
        }
        vol.layer = 0;
        PostProcessVolume v = vol.GetComponent<PostProcessVolume>();
        if (v == null) { v = vol.AddComponent<PostProcessVolume>(); }
        v.isGlobal = true;
        v.priority = 0f;
        v.sharedProfile = profile;

        Camera cam = ReferenceCamera();
        if (cam != null)
        {
            PostProcessLayer layer = cam.GetComponent<PostProcessLayer>();
            if (layer == null) { layer = Undo.AddComponent<PostProcessLayer>(cam.gameObject); }
            string[] res = AssetDatabase.FindAssets("t:PostProcessResources");
            if (res.Length > 0) { layer.Init(AssetDatabase.LoadAssetAtPath<PostProcessResources>(AssetDatabase.GUIDToAssetPath(res[0]))); }
            layer.volumeLayer = 1 << 0;
            layer.volumeTrigger = cam.transform;
            layer.antialiasingMode = PostProcessLayer.Antialiasing.None;   // VR은 MSAA
            EditorUtility.SetDirty(layer);
            log.Append($"\n- 포스트 프로세싱: 전역 볼륨(블룸 1.2, ACES + 노출 +0.45, 채도·대비 +8) + 레퍼런스 카메라 '{cam.name}'에 레이어");
        }
        else
        {
            log.Append("\n- 포스트 프로세싱: 볼륨은 만들었지만 레퍼런스 카메라가 없습니다 — VRC Scene Descriptor의 Reference Camera에 카메라를 넣고 다시 설정");
        }
#else
        log.Append("\n- 포스트 프로세싱: Post Processing 패키지가 없어 건너뜀 — Window → Package Manager → Unity Registry → 'Post Processing' 설치 후 다시 설정");
#endif
    }

#if UNITY_POST_PROCESSING_STACK_V2
    private static T AddSetting<T>(PostProcessProfile profile) where T : PostProcessEffectSettings
    {
        T s = profile.AddSettings<T>();
        s.name = typeof(T).Name;
        s.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(s, profile);
        return s;
    }

    private static Camera ReferenceCamera()
    {
        var desc = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        if (desc != null && desc.ReferenceCamera != null)
        {
            Camera c = desc.ReferenceCamera.GetComponent<Camera>();
            if (c != null) { return c; }
        }
        return null;
    }
#endif

    // ───────────── 도우미 ─────────────

    private static Transform FindPoint(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (t.name == name) { return t; } }
        }
        return null;
    }

    private static GameObject FindInScene(string name)
    {
        Transform t = FindPoint(name);
        return t != null ? t.gameObject : null;
    }

    private static void EditorSceneManagerMarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}
