using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;

/// 물 연출 설치(메뉴 ACO → Environment → Add Water Effects — 다시 눌러도 없는 것만 더한다):
///  - 저공 물보라(ACO_WaterFx, MODE_SPRAY): 미사일 프리팹(ACO_Missile), NPC 전투기 프리팹(ACO_NPCFighterAI — 전투기·공격기·슈퍼에이스),
///    씬의 플레이어 기체(ACO_VehicleUpgrades가 있는 SaccEntity)에 자식 "ACO_WaterSpray"로 — 물보라 프리팹의 인스턴스라 한 곳에서 일괄로 고친다:
///    Prefabs/ACO_WaterSpray(기준 — 기체용, 파티클 모양·색·방출량) + ACO_WaterSpray_Missile(변형 — 미사일용 높이·속도·크기만 다르다).
///    예전 방식(프리팹이 아닌 물보라)은 메뉴를 다시 누르면 프리팹으로 바꾼다.
///  - 함선 항적(MODE_WAKE): 함선 프리팹(ACO_Warship, 해상·공기부양 ACO_EscortUnit)에 자식 "ACO_Wake"로 — 선미 거품 + 선수 물결(거리 비례 방출).
///    위치·폭은 루트 콜라이더 경계에서 잡는다(루트 = 해수면).
///  - 물속(ACO_Underwater): 씬에 하나 만든다(물빛 구·물빛 벽 포함 — 예전에 만든 것엔 빠진 벽만 더한다). 다른 소리는 건드리지 않는다(예전에 붙인 먹먹하게 하기 필터가 있으면 뗀다).
/// 프리팹은 내용을 열어 고치므로 직접 바꿔 끼운 모델은 그대로다. 프리팹 빌더도 새로 만드는 전투기·함선·미사일에 같은 것을 붙인다.
public static class ACO_WaterFxBuilder
{
    public const string SprayName = "ACO_WaterSpray";
    public const string WakeName = "ACO_Wake";
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string TintMaterialPath = "Assets/ACO_Addon/Generated/ACO_UnderwaterTint.mat";
    private const string BackdropMaterialPath = "Assets/ACO_Addon/Generated/ACO_UnderwaterBackdrop.mat";

    [MenuItem("ACO/Environment/Add Water Effects (Spray, Wake, Underwater)")]
    public static void AddAll()
    {
        EnsureSprayPrefabs();
        int sprays = 0, wakes = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path == SprayPrefabPath || path == MissileSprayPrefabPath) { continue; }
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { continue; }
            bool missile = asset.GetComponent<ACO_Missile>() != null;
            bool fighter = asset.GetComponent<ACO_NPCFighterAI>() != null;
            bool ship = IsShip(asset);
            if (!missile && !fighter && !ship) { continue; }
            if (ship && asset.transform.Find(WakeName) != null) { continue; }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            try
            {
                // 물보라: 없거나 예전 방식(프리팹 아님)이면 프리팹으로
                if (missile || fighter) { changed = EnsureSprayOn(root.transform, missile, false); if (changed) { sprays++; } }
                else { AddWake(root.transform); wakes++; changed = true; }
                if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        int vehicles = 0;
        foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_VehicleUpgrades up in sceneRoot.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
            {
                SaccEntity entity = up.GetComponentInParent<SaccEntity>(true);
                Transform t = entity != null ? entity.transform : up.transform;
                if (EnsureSprayOn(t, false, true)) { vehicles++; }
            }
        }

        int removedFilters = SetUpUnderwater();
        ACO_SceneBaker.BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 물 연출: 프리팹 물보라 {sprays}개·항적 {wakes}개, 씬 플레이어 기체 물보라 {vehicles}대를 더했습니다" + (removedFilters > 0 ? $", 예전 물속 소리 필터 {removedFilters}개를 뗐습니다" : "") + ". 씬을 저장하세요.");
    }

    private static bool IsShip(GameObject asset)
    {
        if (asset.GetComponent<ACO_Warship>() != null) { return true; }
        ACO_EscortUnit e = asset.GetComponent<ACO_EscortUnit>();
        return e != null && (e.MoveMode == ACO_EscortUnit.MOVE_SEA || e.MoveMode == ACO_EscortUnit.MOVE_HOVER);
    }

    // ───────────── 물보라 (프리팹 — 한 곳에서 고치면 모두 바뀐다) ─────────────

    /// 기체용 물보라 프리팹(기준). 파티클 모양·색·방출량을 여기서 고치면 기체·미사일 물보라가 모두 바뀐다.
    public const string SprayPrefabPath = PrefabDir + "/ACO_WaterSpray.prefab";
    /// 미사일용 물보라 — 기준 프리팹의 변형(Prefab Variant). 높이·속도 조건과 크기만 다르다(나머지는 기준을 따른다).
    public const string MissileSprayPrefabPath = PrefabDir + "/ACO_WaterSpray_Missile.prefab";

    /// 미사일: 수면 15m 안을 100m/s 이상으로 — 시스키밍(LASM)이면 계속 뿌린다(미사일용 변형 프리팹).
    public static GameObject AddMissileSpray(Transform parent)
    {
        return PlaceSpray(parent, true);
    }

    /// 기체(플레이어·NPC): 수면 30m 안을 50m/s 이상으로(기준 프리팹).
    public static GameObject AddAircraftSpray(Transform parent)
    {
        return PlaceSpray(parent, false);
    }

    /// 물보라 프리팹을 parent 자식으로 놓는다(프리팹 연결 유지). 이름은 SprayName.
    private static GameObject PlaceSpray(Transform parent, bool missile)
    {
        EnsureSprayPrefabs();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(missile ? MissileSprayPrefabPath : SprayPrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = SprayName;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    /// 물보라 프리팹 두 개를 만든다(없을 때만 — 있으면 고친 값을 그대로 둔다).
    public static void EnsureSprayPrefabs()
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Prefabs"); }
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SprayPrefabPath);
        if (basePrefab == null)
        {
            GameObject go = CreateSprayObject(30f, 50f, 250f, 320f, 2.5f);
            basePrefab = PrefabUtility.SaveAsPrefabAsset(go, SprayPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"[ACO] 물보라 프리팹을 만들었습니다: {SprayPrefabPath} — 여기서 고치면 모든 기체·미사일 물보라가 바뀝니다.");
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(MissileSprayPrefabPath) == null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            ACO_WaterFx fx = inst.GetComponent<ACO_WaterFx>();
            fx.MaxHeight = 15f;
            fx.MinSpeed = 100f;
            fx.FullSpeed = 900f;
            fx.MaxRate = 240f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fx);
            ParticleSystem ps = inst.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null)
            {
                ParticleSystem.MainModule main = ps.main;
                main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.3f);
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.radius = 1.5f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ps);
            }
            PrefabUtility.SaveAsPrefabAsset(inst, MissileSprayPrefabPath);   // 기준 프리팹의 변형으로 저장된다
            Object.DestroyImmediate(inst);
            Debug.Log($"[ACO] 미사일용 물보라 프리팹(변형)을 만들었습니다: {MissileSprayPrefabPath}");
        }
    }

    /// 물보라 자식(SprayName)이 이 프리팹의 인스턴스면 true. 예전 방식(프리팹 아님)이거나 없으면 false.
    private static bool HasSprayPrefab(Transform parent, bool missile)
    {
        Transform t = parent.Find(SprayName);
        if (t == null || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) { return false; }
        return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == (missile ? MissileSprayPrefabPath : SprayPrefabPath);
    }

    /// 물보라를 프리팹으로 맞춘다: 예전 방식(프리팹 아님)의 물보라는 떼고 프리팹을 놓는다. 이미 프리팹이면 그대로. 바꿨으면 true.
    private static bool EnsureSprayOn(Transform parent, bool missile, bool undo)
    {
        if (HasSprayPrefab(parent, missile)) { return false; }
        Transform old = parent.Find(SprayName);
        if (old != null)
        {
            if (undo) { Undo.DestroyObjectImmediate(old.gameObject); }
            else { Object.DestroyImmediate(old.gameObject); }
        }
        GameObject go = PlaceSpray(parent, missile);
        if (undo) { Undo.RegisterCreatedObjectUndo(go, "ACO water spray"); }
        return true;
    }

    private static GameObject CreateSprayObject(float maxHeight, float minSpeed, float fullSpeed, float maxRate, float size)
    {
        var go = new GameObject(SprayName);
        ACO_WaterFx fx = go.AddUdonSharpComponent<ACO_WaterFx>();
        fx.Mode = ACO_WaterFx.MODE_SPRAY;
        fx.MaxHeight = maxHeight;
        fx.MinSpeed = minSpeed;
        fx.FullSpeed = fullSpeed;
        fx.MaxRate = maxRate;

        // 방출기는 스크립트가 수면 자리로 옮기고 위로 세운다(축 +Z = 위)
        var psGo = new GameObject("Spray");
        psGo.transform.SetParent(go.transform, false);
        psGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        ParticleSystem ps = psGo.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 16f);
        main.startSize = new ParticleSystem.MinMaxCurve(size, size * 2.2f);
        main.startColor = new Color(0.93f, 0.97f, 1f, 0.7f);
        main.gravityModifier = 1f;
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = size;
        SizeGrow(ps, 1.8f);
        FadeOut(ps, 0.7f);
        UseDefaultMaterial(psGo);
        fx.Particles = new[] { ps };
        return go;
    }

    // ───────────── 항적 ─────────────

    /// 함선 항적: 루트 콜라이더 경계(로컬)에서 선미·선수·폭을 잡는다. 선미 거품(오래 남아 퍼진다) + 선수 물결(짧게).
    public static GameObject AddWake(Transform ship)
    {
        Bounds b = LocalColliderBounds(ship);
        return AddWakeSized(ship, 0f, b.min.z, b.max.z, Mathf.Max(b.size.x, 4f));
    }

    /// 항적을 정한 크기로 단다: 항적 오브젝트를 로컬 높이 y(수면이 될 높이)에 두고 선미 sternZ·선수 bowZ, 폭 width. 같은 거품·물결.
    public static GameObject AddWakeSized(Transform ship, float y, float sternZ, float bowZ, float width)
    {
        var go = new GameObject(WakeName);
        go.transform.SetParent(ship, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        ACO_WaterFx fx = go.AddUdonSharpComponent<ACO_WaterFx>();
        fx.Mode = ACO_WaterFx.MODE_WAKE;
        fx.JumpDistance = 60f;

        ParticleSystem stern = CreateWakeParticles(go.transform, "SternFoam", new Vector3(0f, 0.3f, sternZ), width * 0.8f, width * 0.9f, 1.2f, 22f, 3.5f, 0.55f);
        ParticleSystem bow = CreateWakeParticles(go.transform, "BowWave", new Vector3(0f, 0.3f, bowZ - width * 0.3f), width * 0.9f, width * 0.5f, 0.8f, 7f, 2.5f, 0.45f);
        fx.Particles = new[] { stern, bow };
        ACO_FxBuilder.StyleWake(fx);
        return go;
    }

    private static ParticleSystem CreateWakeParticles(Transform parent, string name, Vector3 localPos, float emitWidth, float size,
        float perMeter, float lifetime, float grow, float alpha)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.95f, 0.98f, 1f, alpha);
        main.maxParticles = 800;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = perMeter;   // 움직인 거리만큼만 — 멈춘 배는 항적이 없다
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
        shape.radius = emitWidth * 0.5f;
        SizeGrow(ps, grow);
        FadeOut(ps, 1f);
        ParticleSystemRenderer r = UseDefaultMaterial(go);
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;   // 수면에 눕는다
        return ps;
    }

    private static Bounds LocalColliderBounds(Transform root)
    {
        bool has = false;
        var b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Collider c in root.GetComponents<Collider>())
        {
            if (c.isTrigger) { continue; }
            Bounds local;
            if (c is BoxCollider box) { local = new Bounds(box.center, box.size); }
            else { local = new Bounds(root.InverseTransformPoint(c.bounds.center), c.bounds.size); }
            if (!has) { b = local; has = true; } else { b.Encapsulate(local); }
        }
        if (!has) { b = new Bounds(Vector3.zero, new Vector3(10f, 5f, 60f)); }
        return b;
    }

    // ───────────── 물속 ─────────────

    /// 씬의 물속 연출을 만든다(없으면 — 있으면 빠진 물빛 구·물빛 벽만 더한다). 예전에 붙인 먹먹하게 하기 필터를 뗀 수를 돌려준다.
    private static int SetUpUnderwater()
    {
        ACO_Underwater uw = null;
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            uw = r.GetComponentInChildren<ACO_Underwater>(true);
            if (uw != null) { break; }
        }
        if (uw == null)
        {
            var go = new GameObject("ACO_Underwater");
            Undo.RegisterCreatedObjectUndo(go, "ACO underwater");
            uw = go.AddUdonSharpComponent<ACO_Underwater>();
        }
        EnsureUnderwaterParts(uw);

        return RemoveMuffleFilters();
    }

    /// 물속 연출에 빠진 물빛 구·물빛 벽을 더한다(Bake도 부른다). 더했으면 true.
    public static bool EnsureUnderwaterParts(ACO_Underwater uw)
    {
        if (uw.Tint != null && uw.Backdrop != null) { return false; }
        Undo.RecordObject(uw, "ACO underwater");
        // 물빛 구: 머리를 감싸는 작은 구(HUD보다 아래 큐 — Bake가 UI 레이어로 옮긴다)
        if (uw.Tint == null) { uw.Tint = CreateSphere(uw.transform, "Tint", 0.5f, TintMaterial()); }
        // 물빛 벽: 안쪽 면만 그리는 큰 구(스크립트가 반경을 맞춘다). 하늘을 가려야 하므로 Default 레이어 그대로
        if (uw.Backdrop == null) { uw.Backdrop = CreateSphere(uw.transform, "Backdrop", 100f, BackdropMaterial()); }
        PrefabUtility.RecordPrefabInstancePropertyModifications(uw);
        return true;
    }

    private static MeshRenderer CreateSphere(Transform parent, string name, float diameter, Material mat)
    {
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        Object.DestroyImmediate(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localScale = Vector3.one * diameter;
        MeshRenderer mr = s.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        s.SetActive(false);
        Undo.RegisterCreatedObjectUndo(s, "ACO underwater " + name);
        return mr;
    }

    private static Material BackdropMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
        if (m != null) { return m; }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        m = new Material(Shader.Find("ACO/UnderwaterBackdrop"));
        AssetDatabase.CreateAsset(m, BackdropMaterialPath);
        return m;
    }

    /// 예전 메뉴가 물속 소리를 먹먹하게 하려고 씬 소리마다 붙였던 필터를 뗀다(꺼진 채 차단 주파수 22000Hz — 메뉴가 붙인 값. Unity 기본값은 5000이라 남의 필터는 건드리지 않는다). 뗀 수.
    public static int RemoveMuffleFilters()
    {
        int removed = 0;
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (AudioLowPassFilter f in r.GetComponentsInChildren<AudioLowPassFilter>(true))
            {
                if (f.enabled || f.cutoffFrequency != 22000f) { continue; }
                Undo.DestroyObjectImmediate(f);
                removed++;
            }
        }
        if (removed > 0) { Debug.Log($"[ACO] 물속 소리 필터 {removed}개를 뗐습니다(소리 먹먹하게 하기를 뺐다)."); }
        return removed;
    }

    private static Material TintMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(TintMaterialPath);
        if (m != null) { return m; }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        m = new Material(Shader.Find("ACO/MarkerOverlay")) { color = new Color(0.05f, 0.24f, 0.32f, 0.2f) };   // 알파는 스크립트 TintAlpha가 정한다
        m.renderQueue = 3990;   // HUD(4000 이상)보다 먼저 — 물속에서도 HUD는 또렷하게
        AssetDatabase.CreateAsset(m, TintMaterialPath);
        return m;
    }

    // ───────────── 공통 ─────────────

    private static void SizeGrow(ParticleSystem ps, float to)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, to));
    }

    private static void FadeOut(ParticleSystem ps, float startAlpha)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    private static ParticleSystemRenderer UseDefaultMaterial(GameObject go)
    {
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        if (mat) { r.sharedMaterial = mat; }
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }
}
