using UnityEditor;
using UnityEngine;
using UdonSharpEditor;
using SaccFlightAndVehicles;
using ACO;

/// NPC/표적 프리팹 생성 함수 모음. 메뉴는 ACO → Create Prefabs... 창(ACO_PrefabBuilderWindow) 하나로 묶었다.
/// U# 스크립트는 AddUdonSharpComponent로 붙여야 UdonBehaviour + 프로그램 에셋 연결이 올바르게 만들어진다.
public static class ACO_PrefabBuilder
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    // SaccTarget은 같은 오브젝트의 Animator를 null 체크 없이 사용하므로 반드시 필요하다.
    // SAV의 Target 프리팹이 쓰는 컨트롤러를 그대로 재사용한다(dead/healthpc/hit 파라미터 보유).
    private const string TargetControllerPath = "Assets/SaccFlightAndVehicles/AssetFiles/Target/Target.controller";

    // 레이어 배정 (DESIGN.md 3.1절)
    private const int LayerFriendlyAir = 25;
    private const int LayerFriendlyGround = 26;
    private const int LayerEnemyAir = 29;
    private const int LayerEnemyGround = 30;
    private const int LayerHitbox = 17; // Walkthrough — SAV가 LOS Raycast에 쓰는 레이어

    // ───────────── 개별 생성 (ACO → Create Prefabs... 창에서 부른다. 저장/새로고침은 창이 한다) ─────────────

    public static string PathOf(string prefabName) { return $"{PrefabDir}/{prefabName}.prefab"; }

    public static bool Exists(string prefabName) { return AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(prefabName)) != null; }

    public static GameObject BuildMissile()
    {
        EnsurePrefabFolder();
        return BuildMissilePrefab();
    }

    /// 플레이어 특수무장용 무유도 폭탄(UGB)과 로켓(RKT). 콜라이더 없음(명중은 Linecast). 범위 피해는 ACO_Ordnance.
    public static GameObject BuildBomb()
    {
        EnsurePrefabFolder();
        GameObject root = CreateOrdnanceObject("ACO_Bomb", new Vector3(0.45f, 1.1f, 0.45f));
        ACO_Ordnance o = root.GetComponent<ACO_Ordnance>();
        o.LaunchSpeed = 0f;
        o.Acceleration = 0f;
        o.GravityScale = 1f;
        o.Drag = 0.02f;
        o.ArmTime = 0.6f;
        o.Damage = 150f;
        o.SplashRadius = 35f;
        o.WeaponType = 4;
        return SaveAndCleanup(root, PathOf("ACO_Bomb"));
    }

    public static GameObject BuildRocket()
    {
        EnsurePrefabFolder();
        GameObject root = CreateOrdnanceObject("ACO_Rocket", new Vector3(0.12f, 0.6f, 0.12f));
        ACO_Ordnance o = root.GetComponent<ACO_Ordnance>();
        o.LaunchSpeed = 30f;
        o.Acceleration = 600f;
        o.MaxSpeed = 450f;
        o.GravityScale = 0.15f;
        o.Drag = 0f;
        o.ArmTime = 0.1f;
        o.MaxLifetime = 8f;
        o.Damage = 45f;
        o.SplashRadius = 12f;
        o.WeaponType = 5;
        return SaveAndCleanup(root, PathOf("ACO_Rocket"));
    }

    private static GameObject CreateOrdnanceObject(string name, Vector3 modelScale)
    {
        GameObject root = new GameObject(name);
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = modelScale;
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Object.DestroyImmediate(model.GetComponent<Collider>());

        GameObject explosion = new GameObject("Explosion");
        explosion.transform.SetParent(root.transform, false);
        explosion.SetActive(false);

        ACO_Ordnance o = root.AddUdonSharpComponent<ACO_Ordnance>();
        o.ExplosionObject = explosion;
        o.DisableOnExplode = new GameObject[] { model };
        return root;
    }

    /// 전투기/SAM은 미사일 프리팹을 참조한다. 없으면 같이 만든다.
    public static GameObject BuildFighter()
    {
        EnsurePrefabFolder();
        return BuildFighterPrefab(LoadOrBuildMissile());
    }

    /// 슈퍼에이스: 일반 NPC 전투기 + 체력 6배·플레어 12번 + 자식 ACO_SuperAce(다연장 런처 8발·기만체 3개). 미션 생성기 풀(UnitPoolRoot) 아래에 하나 둔다 —
    /// 일반 배치에는 안 쓰이고 보너스 페이즈에만 나온다. 모델은 일반 전투기와 같은 상자(빨간색) — 직접 만든 모델로 바꿔 끼운다.
    public static GameObject BuildSuperAce()
    {
        EnsurePrefabFolder();
        GameObject missile = LoadOrBuildMissile();
        GameObject root = CreateFighterObject("NPC_SuperAce", missile);
        ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
        SaccTarget health = root.GetComponent<SaccTarget>();
        ACO_NPCFlightController flight = root.GetComponent<ACO_NPCFlightController>();
        SaccEntity entity = root.GetComponent<SaccEntity>();
        health.Health = 600f;
        ai.Flares.FlareSalvos = 12;
        ai.AceFlareChance = 1f;

        // 모델을 빨갛게(일반 전투기와 구분)
        Transform model = root.transform.Find("Model");
        if (model != null)
        {
            var mat = new Material(Shader.Find("Standard")) { color = new Color(0.75f, 0.12f, 0.1f) };
            if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
            string matPath = "Assets/ACO_Addon/Generated/ACO_SuperAceBody.mat";
            Material saved = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (saved == null) { AssetDatabase.CreateAsset(mat, matPath); saved = mat; }
            model.GetComponent<MeshRenderer>().sharedMaterial = saved;
        }

        var aceGo = new GameObject("ACO_SuperAce");
        aceGo.transform.SetParent(root.transform, false);
        ACO_SuperAce ace = aceGo.AddUdonSharpComponent<ACO_SuperAce>();
        ace.AI = ai;
        ace.TargetHealth = health;
        ace.Flight = flight;
        ai.SuperAce = ace;

        // 다연장 런처: 날개 아래 4곳, 연사 간격 0, 8발, 다 쓰면 40초 재장전. 락온 경보는 슈퍼에이스가 직접 보낸다.
        var salvoGo = new GameObject("SalvoLauncher");
        salvoGo.transform.SetParent(root.transform, false);
        var points = new Transform[4];
        for (int k = 0; k < 4; k++) { points[k] = NewChild(salvoGo, $"LaunchPoint_{k}", new Vector3(k < 2 ? -1.6f : 1.6f, -0.4f, k % 2 == 0 ? 0.8f : -0.4f)); }
        ACO_MissileLauncher salvo = salvoGo.AddUdonSharpComponent<ACO_MissileLauncher>();
        salvo.EntityControl = entity;
        salvo.AAM = missile;
        salvo.LaunchPoints = points;
        salvo.InheritVelocityFrom = flight;
        salvo.NumMissiles = 8;
        salvo.NumPreInstantiated = 4;
        salvo.FireInterval = 0f;
        salvo.ReloadTime = 40f;
        salvo.LockTime = 0f;
        salvo.SendLockWarning = false;
        ace.SalvoLauncher = salvo;

        // 기만체: 플레이어 락온 레이어(29)의 빈 오브젝트. 시작할 때 기체에서 떼어 내고 처음엔 꺼 둔다.
        var decoys = new Transform[3];
        for (int k = 0; k < decoys.Length; k++)
        {
            var d = new GameObject($"Decoy_{k}");
            d.layer = LayerEnemyAir;
            d.transform.SetParent(root.transform, false);
            SphereCollider col = d.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 3f;
            d.SetActive(false);
            decoys[k] = d.transform;
        }
        ace.Decoys = decoys;

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_SuperAce.prefab");
    }

    public static GameObject BuildSAM()
    {
        EnsurePrefabFolder();
        return BuildSAMPrefab(LoadOrBuildMissile());
    }

    public static GameObject BuildAAGun()
    {
        EnsurePrefabFolder();
        return BuildAAGunPrefab();
    }

    /// 공장: 크고 체력이 중간 — 미사일 몇 발이면 무너진다.
    public static GameObject BuildFactory()
    {
        EnsurePrefabFolder();
        return BuildStaticTargetPrefab("NPC_Factory", "Factory", new Vector3(40f, 15f, 30f),
            health: 300f, armor: 1f, noDamageBelow: 0f, score: 500, chimney: true);
    }

    /// 벙커: 낮고 단단하다 — 작은 데미지(기총)는 무시하고 장갑으로 기본 탄 데미지도 줄인다. 폭탄/미사일로 부숴야 한다.
    public static GameObject BuildBunker()
    {
        EnsurePrefabFolder();
        return BuildStaticTargetPrefab("NPC_Bunker", "Bunker", new Vector3(18f, 5f, 18f),
            health: 400f, armor: 4f, noDamageBelow: 20f, score: 800, chimney: false);
    }

    /// 전함(고가치 표적): 본체 120m + VLS 2 + 함포 1 + CIWS 2. VLS용 콜드런치 미사일이 없으면 같이 만든다.
    public static GameObject BuildWarship()
    {
        EnsurePrefabFolder();
        return BuildShipPrefab(BattleshipSpec(), LoadOrBuildVlsMissile());
    }

    // 육각 기둥 지형 조각 치수(ACO_HexTerrain.HexRadius와 같아야 한다)
    public const float HexRadius = 400f;
    private const float HexSkirtWidth = 800f;   // 윗면 가장자리에서 바깥으로 경사가 이어지는 수평 거리
    private const float HexSkirtGrade = 15f;    // 경사(도) — 콘보이 항로 MaxRouteGrade(18°) 아래
    private const float HexWallDepth = 2500f;   // 경사 끝에서 아래로 내려가는 벽(절벽/해안이 비어 보이지 않게)

    /// 육각 기둥 지형 조각: 평평한 육각 윗면(꼭짓점 반지름 HexRadius) + 바깥으로 HexSkirtWidth만큼 HexSkirtGrade 경사 치마 + 수직 벽.
    /// 메시/머티리얼(높이대 4종)은 Generated에, 프리팹은 ACO_HexColumn. 이웃 높이 차가 치마 높이(약 214m) 이하면 비탈로 이어진다.
    public static GameObject BuildHexColumn()
    {
        EnsurePrefabFolder();
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }

        // 메시는 기존 에셋에 덮어 복사하지 않고 지우고 새로 만든다 — 덮어 복사는 편집기 메모리의 메시와 디스크가 어긋나
        // 크기를 바꿔도 프리팹이 예전 크기로 보이는 일이 있었다. 프리팹은 바로 아래에서 새 메시로 다시 만든다.
        const string meshPath = "Assets/ACO_Addon/Generated/ACO_HexColumn.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) { AssetDatabase.DeleteAsset(meshPath); }
        Mesh mesh = BuildHexColumnMesh();
        AssetDatabase.CreateAsset(mesh, meshPath);
        Debug.Log($"[ACO] 육각 기둥 메시: 윗면 반지름 {HexRadius}m, 경사 {HexSkirtWidth}m/{HexSkirtGrade}°, 바운딩 {mesh.bounds.size}");

        // 자연 머티리얼(월드 높이·경사로 칠하는 ACO/HexTerrain)이 있으면 그것을, 없으면 예전 높이대 머티리얼(풀)을 쓴다.
        Material natural = ACO_TerrainLook.GetOrCreateMaterial();
        Material[] mats = natural == null ? HexMaterials() : null;

        GameObject root = new GameObject("ACO_HexColumn");
        root.isStatic = false; // 런타임에 옮긴다
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = root.AddComponent<MeshRenderer>();
        r.sharedMaterial = natural != null ? natural : mats[1];
        MeshCollider col = root.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        // 나무 묶음(자식 "Trees", 충돌 없음): 셰이더가 지면 숲 분포대로 세운다 — 자연 머티리얼일 때만.
        if (natural != null)
        {
            Material treeMat = ACO_TerrainLook.GetOrCreateTreeMaterial(natural);
            if (treeMat != null) { ACO_TerrainLook.EnsureTrees(root, treeMat, HexRadius); }
        }
        return SaveAndCleanup(root, $"{PrefabDir}/ACO_HexColumn.prefab");
    }

    /// 높이대 머티리얼 [모래, 풀, 바위, 눈] — GPU 인스턴싱 켬(같은 메시 수천 개를 적은 드로우콜로).
    public static Material[] HexMaterials()
    {
        string[] names = { "ACO_Hex_Sand", "ACO_Hex_Grass", "ACO_Hex_Rock", "ACO_Hex_Snow" };
        Color[] colors = { new Color(0.76f, 0.7f, 0.5f), new Color(0.32f, 0.5f, 0.26f), new Color(0.45f, 0.42f, 0.38f), new Color(0.92f, 0.93f, 0.96f) };
        var mats = new Material[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"Assets/ACO_Addon/Generated/{names[i]}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard")) { name = names[i] };
                m.color = colors[i];
                m.enableInstancing = true;
                AssetDatabase.CreateAsset(m, path);
            }
            mats[i] = m;
        }
        return mats;
    }

    private static Mesh BuildHexColumnMesh()
    {
        float r1 = HexRadius;
        float r2 = HexRadius + HexSkirtWidth / Mathf.Cos(30f * Mathf.Deg2Rad); // 변까지 거리가 HexSkirtWidth만큼 늘어나게
        float drop = HexSkirtWidth * Mathf.Tan(HexSkirtGrade * Mathf.Deg2Rad);
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        Vector3[] top = new Vector3[6];
        Vector3[] outer = new Vector3[6];
        Vector3[] bottom = new Vector3[6];
        for (int k = 0; k < 6; k++)
        {
            // 꼭짓점이 ±Z를 향하는 육각형(ACO_HexTerrain 격자와 같다)
            float a = (90f + 60f * k) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            top[k] = dir * r1;
            outer[k] = dir * r2 + Vector3.down * drop;
            bottom[k] = dir * r2 + Vector3.down * HexWallDepth;
        }
        for (int k = 0; k < 6; k++)
        {
            int n = (k + 1) % 6;
            AddTri(verts, tris, Vector3.zero, top[k], top[n], Vector3.up);
            Vector3 mid = (top[k] + top[n]) * 0.5f;
            Vector3 outward = new Vector3(mid.x, 0f, mid.z).normalized;
            AddQuad(verts, tris, top[k], top[n], outer[n], outer[k], outward + Vector3.up);
            AddQuad(verts, tris, outer[k], outer[n], bottom[n], bottom[k], outward);
        }
        var mesh = new Mesh { name = "ACO_HexColumn" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 삼각형 하나(면마다 정점 따로 — 각진 음영). 앞면이 expected 쪽을 보게 순서를 맞춘다.
    private static void AddTri(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 expected)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expected) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    private static void AddQuad(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expected)
    {
        AddTri(v, t, a, b, c, expected);
        AddTri(v, t, a, c, d, expected);
    }

    /// 아군 수송기(호위 대상): 느린 대형기, 무장 없음. 락온 마커를 아군 항공기 레이어(25)에 둬서 적이 노린다.
    /// 플레이어 기총 오사는 DisableBulletHitEvent로 막는다(NPC 기총/미사일은 직접 판정이라 영향 없음).
    public static GameObject BuildTransport()
    {
        EnsurePrefabFolder();
        GameObject root = new GameObject("NPC_Transport");
        root.layer = LayerHitbox;

        BoxCollider fuselageCol = root.AddComponent<BoxCollider>();
        fuselageCol.size = new Vector3(6f, 6f, 40f);
        BoxCollider wingCol = root.AddComponent<BoxCollider>();
        wingCol.size = new Vector3(44f, 1.5f, 8f);
        wingCol.center = new Vector3(0f, 1.5f, 2f);

        GameObject model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        AddBox(model.transform, "Fuselage", Vector3.zero, new Vector3(6f, 6f, 40f));
        AddBox(model.transform, "Wing", new Vector3(0f, 1.5f, 2f), new Vector3(44f, 1.5f, 8f));
        AddBox(model.transform, "Tail", new Vector3(0f, 5f, -17f), new Vector3(1f, 8f, 5f));
        AddBox(model.transform, "Stabilizer", new Vector3(0f, 2f, -17f), new Vector3(16f, 1f, 4f));

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerFriendlyAir;
        lockMarker.transform.SetParent(root.transform, false);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 15f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        GameObject deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        deathFx.SetActive(false);
        // 목적지 투하: 꼬리(화물 램프)에서 보급 상자/공수부대가 낙하산을 펴고 내려온다
        ParticleSystem supplies = CreateAirDrop(root.transform, "AirDrop_Supplies", ACO_DecoBuilder.DropCrateMesh(), ACO_DecoBuilder.DropMaterial("Supply", new Color(0.82f, 0.78f, 0.62f)), 0f);
        ParticleSystem troopers = CreateAirDrop(root.transform, "AirDrop_Paratroopers", ACO_DecoBuilder.DropTrooperMesh(), ACO_DecoBuilder.DropMaterial("Troop", new Color(0.36f, 0.42f, 0.28f)), 0.25f);

        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_EscortUnit escort = root.AddUdonSharpComponent<ACO_EscortUnit>();

        health.Health = 600f;
        health.RespawnDelay = 0f;
        health.DisableBulletHitEvent = true;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { escort };

        escort.TargetHealth = health;
        escort.TargetName = "Transport";
        escort.MarkerLabel = "VIP";
        escort.MoveMode = ACO_EscortUnit.MOVE_AIR;
        escort.Speed = 120f;
        escort.ShowOnDeath = new GameObject[] { deathFx };
        escort.HideOnDeath = new GameObject[0];
        escort.DisableCollidersOnDeath = new Collider[] { fuselageCol, wingCol };
        escort.HideAfterCrash = new GameObject[] { model };
        escort.DropEffects = new ParticleSystem[] { supplies, troopers };
        escort.GearHeight = 3f; // 동체 아래면(-3m)이 활주로에 닿게
        escort.Flares = AddFlareDispenser(root, escort, health, new Vector3(0f, -2f, -18f), 0.5f, 6);

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_Transport.prefab");
    }

    /// 적 폭격기(요격 페이즈): 수송기와 같은 항로 비행(ACO_EscortUnit, Hostile)으로 도시 상공까지 와서 폭탄을 떨어뜨린다 — 그 순간 미션 실패.
    /// 락온 마커는 적 항공기 레이어(29), 기총·미사일·범위 피해를 모두 받는다. 미션 생성기 풀(UnitPoolRoot) 아래에 여러 대(4~6) 둔다.
    public static GameObject BuildBomber()
    {
        EnsurePrefabFolder();
        GameObject root = new GameObject("NPC_Bomber");
        root.layer = LayerHitbox;

        BoxCollider fuselageCol = root.AddComponent<BoxCollider>();
        fuselageCol.size = new Vector3(5f, 5f, 34f);
        BoxCollider wingCol = root.AddComponent<BoxCollider>();
        wingCol.size = new Vector3(40f, 1.2f, 9f);
        wingCol.center = new Vector3(0f, 0.5f, 1f);

        GameObject model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        AddBox(model.transform, "Fuselage", Vector3.zero, new Vector3(5f, 5f, 34f));
        AddBox(model.transform, "Wing", new Vector3(0f, 0.5f, 1f), new Vector3(40f, 1.2f, 9f));
        AddBox(model.transform, "Tail", new Vector3(0f, 4f, -14f), new Vector3(1f, 7f, 5f));
        AddBox(model.transform, "Stabilizer", new Vector3(0f, 1f, -14f), new Vector3(14f, 1f, 4f));
        for (int k = 0; k < 4; k++) { AddBox(model.transform, $"Engine_{k}", new Vector3(-13.5f + k * 9f, -0.6f, 3f), new Vector3(1.6f, 1.6f, 5f)); }

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyAir;
        lockMarker.transform.SetParent(root.transform, false);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 12f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        GameObject deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        deathFx.SetActive(false);
        // 도시 상공 투하: 폭탄창에서 폭탄이 줄지어 떨어진다(낙하산 없음)
        ParticleSystem bombs = CreateAirDrop(root.transform, "BombDrop", ACO_DecoBuilder.DropCrateMesh(), ACO_DecoBuilder.DropMaterial("Bomb", new Color(0.18f, 0.2f, 0.18f)), 0f, false);

        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_EscortUnit unit = root.AddUdonSharpComponent<ACO_EscortUnit>();

        health.Health = 350f;
        health.RespawnDelay = 0f;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { unit };

        unit.Hostile = true;
        unit.TargetHealth = health;
        unit.TargetName = "Bomber";
        unit.MarkerLabel = "BMR";
        unit.MoveMode = ACO_EscortUnit.MOVE_AIR;
        unit.Speed = 110f;
        unit.AttackPriority = 0f;
        unit.ShowOnDeath = new GameObject[] { deathFx };
        unit.HideOnDeath = new GameObject[0];
        unit.DisableCollidersOnDeath = new Collider[] { fuselageCol, wingCol };
        unit.HideAfterCrash = new GameObject[] { model };
        unit.DropEffects = new ParticleSystem[] { bombs };
        unit.Flares = AddFlareDispenser(root, unit, health, new Vector3(0f, -1.5f, -15f), 0.5f, 6);

        // 방어 기총: 상부(앞쪽, 전방위) + 꼬리(뒤를 본다). 별도 체력·락온 마커·콜라이더 없음 — 기총을 맞히면 폭격기가 맞는다.
        GameObject top = CreateBomberTurret(root.transform, "TopTurret", new Vector3(0f, 2.5f, 5f), 0f);
        GameObject tail = CreateBomberTurret(root.transform, "TailTurret", new Vector3(0f, 0.6f, -17.5f), 180f);
        unit.DefenseGuns = new UdonSharp.UdonSharpBehaviour[] { top.GetComponent<ACO_AAGunSite>(), tail.GetComponent<ACO_AAGunSite>() };
        unit.HideAfterCrash = new GameObject[] { model, top, tail };

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_Bomber.prefab");
    }

    /// 폭격기 방어 기총: 대공포(ACO_AAGunSite)를 작게 만들어 기체에 붙인다 — 사거리 1.2km, 약한 연사, 아래(−60°)까지 돈다.
    /// 자기 콜라이더·락온 마커는 지운다(맞으면 폭격기 본체가 맞는다). 살리고 멈추는 건 ACO_EscortUnit(DefenseGuns)이 한다.
    private static GameObject CreateBomberTurret(Transform parent, string name, Vector3 localPos, float yaw)
    {
        var spec = new GunSpec
        {
            BodySize = new Vector3(1.4f, 0.8f, 1.4f), BarrelLength = 1.8f, BarrelThickness = 0.15f,
            Health = 100f, Armor = 1f, Score = 0, Label = "GUN",
            DetectRange = 1600f, LoseTargetRange = 2000f, YawRate = 110f, PitchRate = 90f, TrackingLag = 0.5f, AimError = 0.9f,
            MaxRange = 1200f, MuzzleVelocity = 900f, FireStartAngle = 8f, EffectiveAngle = 2.5f, DwellTime = 0.5f,
            BurstDuration = 1.5f, BurstCooldown = 1.5f, DamagePerTick = 2f, MinDamageInterval = 0.15f, MaxDamageInterval = 0.5f,
            Spread = 0.9f, TracerRate = 30f,
        };
        GameObject turret = CreateAAGunObject(name, spec);
        turret.transform.SetParent(parent, false);
        turret.transform.localPosition = localPos;
        turret.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Object.DestroyImmediate(turret.GetComponent<BoxCollider>());
        Transform marker = turret.transform.Find("AAMTarget");
        if (marker != null) { Object.DestroyImmediate(marker.gameObject); }
        ACO_AAGunSite aa = turret.GetComponent<ACO_AAGunSite>();
        aa.DisableCollidersOnDeath = new Collider[0];
        aa.MinPitch = -60f;
        aa.MaxPitch = 85f;
        aa.MinTargetAltitude = 0f;
        return turret;
    }

    /// 플레어 장치(ACO_FlareDispenser)를 유닛 루트에 붙이고 연출 파티클을 만든다 — 미사일이 표적 마커에서 부모 쪽으로 찾는다.
    private static ACO_FlareDispenser AddFlareDispenser(GameObject root, ACO_EscortUnit unit, SaccTarget health, Vector3 particlePos, float chance, int salvos)
    {
        ACO_FlareDispenser d = root.AddUdonSharpComponent<ACO_FlareDispenser>();
        d.TargetHealth = health;
        d.Unit = unit;
        d.FlareChance = chance;
        d.FlareSalvos = salvos;
        d.FlareCooldown = 4f;
        d.FlareParticleCount = 12;
        d.FlareParticles = CreateFlareParticle(root.transform, particlePos);
        return d;
    }

    /// 투하 파티클: 꼬리 아래에서 0.5초마다 2개씩 6번(12개), 월드 공간, 기체 속도를 조금 물려받은 뒤 낙하산 종단 속도(6m/s)로 떨어지고
    /// 땅(Default/Environment)에 닿으면 사라진다. delay만큼 늦게 시작(두 종류가 엇갈려 나오게). parachute를 끄면 그대로 떨어진다(폭탄).
    private static ParticleSystem CreateAirDrop(Transform parent, string name, Mesh mesh, Material material, float delay, bool parachute = true)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -2.5f, -19f);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 3.5f;
        main.startDelay = delay;
        main.startLifetime = 180f;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.gravityModifier = 1f;
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 2, 6, 0.5f) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(3f, 1f, 3f);

        ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;
        inherit.enabled = true;
        inherit.mode = ParticleSystemInheritVelocityMode.Initial;
        inherit.curveMultiplier = 0.25f;

        // 낙하산: 속도를 종단 속도까지 서서히 줄인다
        ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
        limit.enabled = parachute;
        limit.limit = 6f;
        limit.dampen = 0.08f;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.5f;
        noise.frequency = 0.15f;

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.collidesWith = 2049; // Default + Environment(지형/건물)
        collision.lifetimeLoss = 1f;
        collision.quality = ParticleSystemCollisionQuality.Medium;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = mesh;
        r.alignment = ParticleSystemRenderSpace.World;
        r.sharedMaterial = material;
        return ps;
    }

    /// 전차(적 지상 유닛): 대공포 포대 구조를 그대로 쓰되 포탑은 느리고 낮게, 표적은 아군 지상(레이어 26 — 콘보이, 플레이어 지상 차량),
    /// 사격은 묵직한 단발(체류 1.5초 뒤 한 발 35). 이동은 ACO_Tank가 생성기 항로를 따라간다.
    public static GameObject BuildTank()
    {
        EnsurePrefabFolder();
        var spec = new GunSpec
        {
            BodySize = new Vector3(3.4f, 1.6f, 7f), BarrelLength = 4.5f, BarrelThickness = 0.25f,
            Health = 150f, Armor = 2f, Score = 200, Label = "TNK",
            DetectRange = 2500f, LoseTargetRange = 3000f, YawRate = 30f, PitchRate = 10f, TrackingLag = 0.3f, AimError = 0.4f,
            MaxRange = 2500f, MuzzleVelocity = 900f, FireStartAngle = 3f, EffectiveAngle = 1.5f, DwellTime = 1.5f,
            BurstDuration = 0.25f, BurstCooldown = 4f, DamagePerTick = 35f, MinDamageInterval = 0.2f, MaxDamageInterval = 0.2f,
            Spread = 0.2f, TracerRate = 4f,
        };
        GameObject root = CreateAAGunObject("NPC_Tank", spec);
        ACO_AAGunSite turret = root.GetComponent<ACO_AAGunSite>();
        SaccEntity entity = root.GetComponent<SaccEntity>();
        SaccTarget health = root.GetComponent<SaccTarget>();
        // 아군 지상 표적만 노린다(빌드 시 AAMTargets가 레이어 26 마커로 채워진다). 지면 높이 표적이라 최소 고도 제한을 푼다.
        entity.AAMTargetsLayer = 1 << LayerFriendlyGround;
        turret.TargetLayers = 1 << LayerFriendlyGround;
        turret.MinTargetAltitude = -100000f;
        turret.MinPitch = -8f;
        turret.MaxPitch = 20f;
        // 포신은 수평이 쉬는 자세
        if (turret.TurretPitch) { turret.TurretPitch.localRotation = Quaternion.identity; }

        GameObject deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        deathFx.transform.localPosition = new Vector3(0f, 2f, 0f);
        deathFx.SetActive(false);
        // 격파되면 포탑만 숨기고 차체(잔해)는 남긴다.
        turret.HideOnDeath = turret.TurretYaw != null ? new GameObject[] { turret.TurretYaw.gameObject } : new GameObject[0];
        turret.ShowOnDeath = new GameObject[] { deathFx };

        ACO_Tank tank = root.AddUdonSharpComponent<ACO_Tank>();
        tank.TargetHealth = health;
        tank.Turret = turret;
        tank.ScoreValue = spec.Score;
        tank.MarkerLabel = spec.Label;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { turret, tank };
        entity.ExtensionUdonBehaviours = new UdonSharp.UdonSharpBehaviour[] { turret };

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_Tank.prefab");
    }

    /// 아군 콘보이 차량(지상 호위 대상): 지면을 따라가는 트럭. 락온 마커를 아군 지상 레이어(26)에 둬서 적 전차가 노린다.
    public static GameObject BuildConvoyTruck()
    {
        EnsurePrefabFolder();
        GameObject root = new GameObject("NPC_ConvoyTruck");
        root.layer = LayerHitbox;

        BoxCollider col = root.AddComponent<BoxCollider>();
        col.size = new Vector3(2.6f, 3f, 9f);
        col.center = new Vector3(0f, 1.5f, 0f);

        GameObject model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        AddBox(model.transform, "Cab", new Vector3(0f, 1.3f, 3.3f), new Vector3(2.5f, 2.4f, 2.2f));
        AddBox(model.transform, "Cargo", new Vector3(0f, 1.6f, -1f), new Vector3(2.6f, 3f, 6.2f));

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerFriendlyGround;
        lockMarker.transform.SetParent(root.transform, false);
        lockMarker.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 4f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        GameObject deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        deathFx.SetActive(false);

        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_EscortUnit escort = root.AddUdonSharpComponent<ACO_EscortUnit>();

        health.Health = 250f;
        health.RespawnDelay = 0f;
        health.DisableBulletHitEvent = true;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { escort };

        escort.TargetHealth = health;
        escort.TargetName = "Convoy";
        escort.MarkerLabel = "CVY";
        escort.MoveMode = ACO_EscortUnit.MOVE_GROUND;
        escort.Speed = 20f;
        escort.AttackPriority = 0f;
        escort.ShowOnDeath = new GameObject[] { deathFx };
        escort.HideOnDeath = new GameObject[0];
        escort.DisableCollidersOnDeath = new Collider[] { col };
        escort.HideAfterCrash = new GameObject[0];

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_ConvoyTruck.prefab");
    }

    /// 구축함(일반 유닛 — 지상 풀): 본체 80m + VLS 1 + CIWS 1. 바다에 함대로 스폰되거나 전함 호위로 붙는다.
    public static GameObject BuildDestroyer()
    {
        EnsurePrefabFolder();
        return BuildShipPrefab(DestroyerSpec(), LoadOrBuildVlsMissile());
    }

    private static GameObject LoadOrBuildVlsMissile()
    {
        GameObject vls = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_VLS"));
        return vls != null ? vls : BuildVlsMissilePrefab();
    }

    private static GameObject LoadOrBuildMissile()
    {
        GameObject missile = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_AAM"));
        return missile != null ? missile : BuildMissilePrefab();
    }

    private static void EnsurePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Prefabs"); }
    }

    private static GameObject BuildStaticTargetPrefab(string prefabName, string targetName, Vector3 size,
        float health, float armor, float noDamageBelow, int score, bool chimney)
    {
        GameObject root = new GameObject(prefabName);
        root.layer = LayerHitbox;

        BoxCollider hitbox = root.AddComponent<BoxCollider>();
        hitbox.size = size;
        hitbox.center = new Vector3(0f, size.y * 0.5f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Model";
        body.layer = LayerHitbox;
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        body.transform.localScale = size;
        Object.DestroyImmediate(body.GetComponent<Collider>());

        if (chimney)
        {
            GameObject stack = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stack.name = "Chimney";
            stack.layer = LayerHitbox;
            stack.transform.SetParent(body.transform, true);
            stack.transform.position = root.transform.position + new Vector3(size.x * 0.3f, size.y + 8f, size.z * 0.25f);
            stack.transform.localScale = new Vector3(3f / size.x, 8f / size.y, 3f / size.z);
            Object.DestroyImmediate(stack.GetComponent<Collider>());
        }

        // 파괴 후 잔해(처음엔 꺼 둠). 실제 잔해 모델로 바꿔 쓴다.
        GameObject rubble = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rubble.name = "Rubble";
        rubble.layer = LayerHitbox;
        rubble.transform.SetParent(root.transform, false);
        rubble.transform.localPosition = new Vector3(0f, size.y * 0.1f, 0f);
        rubble.transform.localScale = new Vector3(size.x, size.y * 0.2f, size.z);
        Object.DestroyImmediate(rubble.GetComponent<Collider>());
        rubble.SetActive(false);

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyGround;
        lockMarker.transform.SetParent(root.transform, false);
        lockMarker.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = Mathf.Max(size.x, size.z) * 0.5f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        AddTargetAnimator(root);

        SaccTarget targetHealth = root.AddUdonSharpComponent<SaccTarget>();
        ACO_StaticTarget st = root.AddUdonSharpComponent<ACO_StaticTarget>();

        targetHealth.Health = health;
        targetHealth.ArmorStrength = armor;
        targetHealth.NoDamageBelow = noDamageBelow;
        // 0 = 자동 부활 안 함. 미션 생성기가 다시 배치할 때 부활시킨다.
        targetHealth.RespawnDelay = 0f;
        targetHealth.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { st };

        st.TargetHealth = targetHealth;
        st.TargetName = targetName;
        // 표적 마커 약어(3글자): Factory → FAC, Bunker → BNK
        st.MarkerLabel = targetName == "Bunker" ? "BNK" : targetName.Substring(0, Mathf.Min(3, targetName.Length)).ToUpperInvariant();
        st.ScoreValue = score;
        st.HideOnDeath = new GameObject[] { body };
        st.ShowOnDeath = new GameObject[] { rubble };
        // 잔해에 미사일이 계속 터지지 않게 히트박스를 끈다.
        st.DisableCollidersOnDeath = new Collider[] { hitbox };

        return SaveAndCleanup(root, $"{PrefabDir}/{prefabName}.prefab");
    }

    private static GameObject BuildMissilePrefab()
    {
        return SaveAndCleanup(CreateMissileObject("ACO_Missile_AAM"), $"{PrefabDir}/ACO_Missile_AAM.prefab");
    }

    /// 함선 VLS용: 수직으로 튀어나와(콜드런치) 표적 쪽으로 돈 뒤 점화한다. 비례항법.
    private static GameObject BuildVlsMissilePrefab()
    {
        GameObject root = CreateMissileObject("ACO_Missile_VLS");
        ACO_Missile missile = root.GetComponent<ACO_Missile>();
        missile.ColdLaunchTime = 1f;
        missile.ColdLaunchSpeed = 25f;
        missile.ColdLaunchTurnRate = 90f;
        missile.ColdLaunchGravity = 1f;
        missile.GuidanceMode = 2;
        missile.MaxSpeed = 500f;
        missile.MaxLifetime = 25f;
        return SaveAndCleanup(root, $"{PrefabDir}/ACO_Missile_VLS.prefab");
    }

    private static GameObject CreateMissileObject(string name)
    {
        GameObject root = new GameObject(name);

        // 콜라이더를 두지 않는다 — 명중은 근접 신관 + Linecast로 판정하므로 물리 충돌이 필요 없고,
        // 콜라이더가 있으면 자기 Linecast에 걸릴 수 있다.
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = new Vector3(0.25f, 0.9f, 0.25f);
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Object.DestroyImmediate(model.GetComponent<Collider>());

        GameObject explosion = new GameObject("Explosion");
        explosion.transform.SetParent(root.transform, false);
        explosion.SetActive(false);

        ACO_Missile missile = root.AddUdonSharpComponent<ACO_Missile>();
        missile.ExplosionObject = explosion;
        missile.DisableOnExplode = new GameObject[] { model };
        return root;
    }

    private static GameObject BuildFighterPrefab(GameObject missilePrefab)
    {
        return SaveAndCleanup(CreateFighterObject("NPC_Fighter", missilePrefab), $"{PrefabDir}/NPC_Fighter.prefab");
    }

    /// NPC 전투기 오브젝트(저장 전). 일반 전투기와 슈퍼에이스가 같이 쓴다.
    private static GameObject CreateFighterObject(string name, GameObject missilePrefab)
    {
        // SAV 미사일의 직접충돌 데미지 분기는 other.gameObject에서 SaccTarget을 찾고 부모를 타지 않으므로,
        // 히트박스 콜라이더와 SaccTarget이 같은 오브젝트(루트)에 있어야 한다.
        // 루트 레이어는 17(Walkthrough) — 락온 LOS Raycast가 이 레이어를 요구한다.
        GameObject root = new GameObject(name);
        root.layer = LayerHitbox;

        BoxCollider hitbox = root.AddComponent<BoxCollider>();
        hitbox.size = new Vector3(2f, 0.6f, 6f);

        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.name = "Model";
        model.layer = LayerHitbox;
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = new Vector3(2f, 0.6f, 6f);
        Object.DestroyImmediate(model.GetComponent<Collider>());

        // 플레이어 미사일이 락온하는 마커. 트리거 + 소속 레이어여야 하고, 빌드 시 AAMTargets에 수집된다.
        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyAir;
        lockMarker.transform.SetParent(root.transform, false);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 3f;

        // SAV의 TargetExplode 애니메이션이 "Effects"와 "AAMTarget"의 활성 상태를 토글하므로 이름을 맞춘다.
        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        GameObject launcherObj = new GameObject("MissileLauncher");
        launcherObj.transform.SetParent(root.transform, false);
        Transform lpLeft = NewChild(launcherObj, "LaunchPoint_L", new Vector3(-1f, -0.3f, 1f));
        Transform lpRight = NewChild(launcherObj, "LaunchPoint_R", new Vector3(1f, -0.3f, 1f));

        GameObject gunObj = new GameObject("Gun");
        gunObj.transform.SetParent(root.transform, false);
        gunObj.transform.localPosition = new Vector3(0f, 0f, 3.2f);

        AddTargetAnimator(root);

        SaccEntity entity = root.AddUdonSharpComponent<SaccEntity>();
        SaccTarget targetHealth = root.AddUdonSharpComponent<SaccTarget>();
        ACO_NPCFlightController flight = root.AddUdonSharpComponent<ACO_NPCFlightController>();
        ACO_NPCFighterAI ai = root.AddUdonSharpComponent<ACO_NPCFighterAI>();
        // 동기화는 별도 스크립트. 소유권이 같아야 하므로 AI와 같은 오브젝트에 둔다.
        ACO_NPCFighterNet net = root.AddUdonSharpComponent<ACO_NPCFighterNet>();
        ACO_MissileLauncher launcher = launcherObj.AddUdonSharpComponent<ACO_MissileLauncher>();
        ACO_NPCGun gun = gunObj.AddUdonSharpComponent<ACO_NPCGun>();

        // 적 NPC는 아군 항공기(25)를 노린다 → 빌드 시 이 엔티티의 AAMTargets가 아군 마커로 채워진다.
        entity.AAMTargetsLayer = 1 << LayerFriendlyAir;
        entity.ExtensionUdonBehaviours = new UdonSharp.UdonSharpBehaviour[] { ai };

        // 체력/데미지/리스폰은 SaccTarget이 소유하고, 격추되면 ExplodeOther로 AI에 직접 통지한다.
        targetHealth.Health = 100f;
        // 0 = 자동 부활 안 함. 스폰/웨이브 매니저가 ACO_NPCFighterAI.Respawn()을 호출한다.
        targetHealth.RespawnDelay = 0f;
        targetHealth.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ai };

        launcher.EntityControl = entity;
        launcher.AAM = missilePrefab;
        launcher.LaunchPoints = new Transform[] { lpLeft, lpRight };
        launcher.InheritVelocityFrom = flight;
        launcher.LockTime = 1.5f; // 열추적 근접전 — 짧게

        ai.EntityControl = entity;
        ai.TargetHealth = targetHealth;
        ai.Flight = flight;
        ai.MissileLauncher = launcher;
        ai.Gun = gun;
        ai.Net = net;
        net.AI = ai;
        net.Flight = flight;

        gun.EntityControl = entity;
        gun.Muzzle = gunObj.transform;
        gun.ShooterFlight = flight;
        // 파티클은 기총의 탄도 값으로 만들어야 보이는 탄과 명중 계산이 일치한다.
        gun.VisualParticle = CreateTracerParticle(gunObj.transform, gun.MuzzleVelocity, gun.MaxRange, gun.Spread);
        ai.TargetLayers = 1 << LayerFriendlyAir;
        // SAV 사망 애니메이션은 루트의 MeshRenderer/MeshCollider만 끄므로 자식 모델과 BoxCollider는 직접 처리한다.
        ai.HideOnDeath = new GameObject[] { model };
        ai.DisableCollidersOnDeath = new Collider[] { hitbox };
        // 플레어 장치: 꼬리 쪽 아래로 뿌린다(오너가 미사일 접근을 보고 NetFlare로 모두에게 방출). 확률·반응 거리는 AI가 등급에 맞춰 넣는다.
        ACO_FlareDispenser flares = root.AddUdonSharpComponent<ACO_FlareDispenser>();
        flares.TargetHealth = targetHealth;
        flares.Flight = flight;
        flares.FlareParticles = CreateFlareParticle(root.transform, new Vector3(0f, -0.4f, -2.8f));
        ai.Flares = flares;

        return root;
    }

    private static GameObject BuildSAMPrefab(GameObject missilePrefab)
    {
        return SaveAndCleanup(CreateSAMObject("NPC_SAM", missilePrefab), $"{PrefabDir}/NPC_SAM.prefab");
    }

    /// 함선 VLS: 포탑 없는 SAM. 갑판에 납작한 셀 블록, 발사구는 위를 본다(콜드런치 미사일과 같이 쓴다).
    private static GameObject CreateVLSObject(string name, GameObject missilePrefab)
    {
        GameObject root = new GameObject(name);
        root.layer = LayerHitbox;
        Vector3 size = new Vector3(5f, 1.2f, 5f);

        BoxCollider hitbox = root.AddComponent<BoxCollider>();
        hitbox.size = size;
        hitbox.center = new Vector3(0f, size.y * 0.5f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Model";
        body.layer = LayerHitbox;
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        body.transform.localScale = size;
        Object.DestroyImmediate(body.GetComponent<Collider>());

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyGround;
        lockMarker.transform.SetParent(root.transform, false);
        lockMarker.transform.localPosition = new Vector3(0f, 1f, 0f);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 3f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        // 셀 4개(2x2), 발사구는 위(-90도 X).
        GameObject launcherObj = new GameObject("MissileLauncher");
        launcherObj.transform.SetParent(root.transform, false);
        var cells = new Transform[4];
        for (int k = 0; k < 4; k++)
        {
            Transform cell = NewChild(launcherObj, $"Cell_{k}", new Vector3((k % 2 == 0 ? -1.2f : 1.2f), size.y + 0.2f, (k < 2 ? 1.2f : -1.2f)));
            cell.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            cells[k] = cell;
        }

        AddTargetAnimator(root);

        SaccEntity entity = root.AddUdonSharpComponent<SaccEntity>();
        SaccTarget targetHealth = root.AddUdonSharpComponent<SaccTarget>();
        ACO_SAMSite sam = root.AddUdonSharpComponent<ACO_SAMSite>();
        ACO_MissileLauncher launcher = launcherObj.AddUdonSharpComponent<ACO_MissileLauncher>();

        entity.AAMTargetsLayer = 1 << LayerFriendlyAir;
        entity.ExtensionUdonBehaviours = new UdonSharp.UdonSharpBehaviour[] { sam };

        targetHealth.Health = 150f;
        targetHealth.RespawnDelay = 0f;
        targetHealth.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { sam };

        launcher.EntityControl = entity;
        launcher.AAM = missilePrefab;
        launcher.LaunchPoints = cells;
        launcher.InheritVelocityFrom = null;
        launcher.MissileType = 0;
        launcher.NumMissiles = 8;
        launcher.FireInterval = 3f;
        launcher.ReloadTime = 40f;
        launcher.LockTime = 3.5f;
        launcher.AAMMaxTargetDistance = 9000f;

        sam.EntityControl = entity;
        sam.TargetHealth = targetHealth;
        sam.MissileLauncher = launcher;
        sam.TargetLayers = 1 << LayerFriendlyAir;
        sam.DetectRange = 7000f;
        sam.LoseTargetRange = 8000f;
        sam.ScoreValue = 300;
        sam.MarkerLabel = "VLS";
        sam.HideOnDeath = new GameObject[] { body };
        sam.DisableCollidersOnDeath = new Collider[] { hitbox };
        return root;
    }

    private static GameObject CreateSAMObject(string name, GameObject missilePrefab)
    {
        GameObject root = new GameObject(name);
        root.layer = LayerHitbox;

        BoxCollider hitbox = root.AddComponent<BoxCollider>();
        hitbox.size = new Vector3(3f, 1f, 4f);
        hitbox.center = new Vector3(0f, 0.5f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Model";
        body.layer = LayerHitbox;
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        body.transform.localScale = new Vector3(3f, 1f, 4f);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyGround;
        lockMarker.transform.SetParent(root.transform, false);
        lockMarker.transform.localPosition = new Vector3(0f, 1f, 0f);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 3f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        GameObject turretYaw = new GameObject("TurretYaw");
        turretYaw.transform.SetParent(root.transform, false);
        turretYaw.transform.localPosition = new Vector3(0f, 1f, 0f);

        GameObject turretPitch = new GameObject("TurretPitch");
        turretPitch.transform.SetParent(turretYaw.transform, false);

        GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "Rail";
        rail.layer = LayerHitbox;
        rail.transform.SetParent(turretPitch.transform, false);
        rail.transform.localScale = new Vector3(1.4f, 0.3f, 2.5f);
        Object.DestroyImmediate(rail.GetComponent<Collider>());

        GameObject launcherObj = new GameObject("MissileLauncher");
        launcherObj.transform.SetParent(turretPitch.transform, false);
        Transform lp = NewChild(launcherObj, "LaunchPoint", new Vector3(0f, 0.4f, 1f));

        AddTargetAnimator(root);

        SaccEntity entity = root.AddUdonSharpComponent<SaccEntity>();
        SaccTarget targetHealth = root.AddUdonSharpComponent<SaccTarget>();
        ACO_SAMSite sam = root.AddUdonSharpComponent<ACO_SAMSite>();
        ACO_MissileLauncher launcher = launcherObj.AddUdonSharpComponent<ACO_MissileLauncher>();

        entity.AAMTargetsLayer = 1 << LayerFriendlyAir;
        entity.ExtensionUdonBehaviours = new UdonSharp.UdonSharpBehaviour[] { sam };

        targetHealth.Health = 200f;
        // 0 = 자동 부활 안 함. 스폰/웨이브 매니저가 ACO_SAMSite.Respawn()을 호출한다.
        targetHealth.RespawnDelay = 0f;
        targetHealth.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { sam };

        launcher.EntityControl = entity;
        launcher.AAM = missilePrefab;
        launcher.LaunchPoints = new Transform[] { lp };
        launcher.InheritVelocityFrom = null;
        launcher.MissileType = 0; // 레이더 유도
        launcher.NumMissiles = 4;
        launcher.LockTime = 3f;  // 장거리 레이더 — 길게 잡아 회피 기회를 준다

        sam.EntityControl = entity;
        sam.TargetHealth = targetHealth;
        sam.MissileLauncher = launcher;
        sam.TargetLayers = 1 << LayerFriendlyAir;
        sam.HideOnDeath = new GameObject[] { body, turretYaw };
        sam.DisableCollidersOnDeath = new Collider[] { hitbox };
        sam.TurretYaw = turretYaw.transform;
        sam.TurretPitch = turretPitch.transform;
        return root;
    }

    /// 대공포 계열(지상 대공포, 함포, CIWS) 수치. 기본값 = 지상 대공포(NPC_AAGun, DESIGN.md 6-1의 시뮬레이션 값).
    private class GunSpec
    {
        public Vector3 BodySize = new Vector3(2.5f, 1.2f, 2.5f);
        public float BarrelLength = 3f;
        public float BarrelThickness = 0.3f;
        public float Health = 120f;
        public float Armor = 1f;
        public int Score = 100;
        public string Label = "AAG";
        public float DetectRange = 2500f;
        public float LoseTargetRange = 3000f;
        public float YawRate = 70f;
        public float PitchRate = 50f;
        public float TrackingLag = 0.6f;
        public float AimError = 0.8f;
        public float MaxRange = 2000f;
        public float MuzzleVelocity = 1000f;
        public float FireStartAngle = 8f;
        public float EffectiveAngle = 2.5f;
        public float DwellTime = 0.6f;
        public float BurstDuration = 2.5f;
        public float BurstCooldown = 1.5f;
        public float DamagePerTick = 3f;
        public float MinDamageInterval = 0.15f;
        public float MaxDamageInterval = 0.6f;
        public float Spread = 0.8f;
        public float TracerRate = 25f;
    }

    private static GameObject BuildAAGunPrefab()
    {
        return SaveAndCleanup(CreateAAGunObject("NPC_AAGun", new GunSpec()), $"{PrefabDir}/NPC_AAGun.prefab");
    }

    private static GameObject CreateAAGunObject(string name, GunSpec spec)
    {
        GameObject root = new GameObject(name);
        root.layer = LayerHitbox;
        Vector3 bs = spec.BodySize;

        BoxCollider hitbox = root.AddComponent<BoxCollider>();
        hitbox.size = bs;
        hitbox.center = new Vector3(0f, bs.y * 0.5f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Model";
        body.layer = LayerHitbox;
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, bs.y * 0.5f, 0f);
        body.transform.localScale = bs;
        Object.DestroyImmediate(body.GetComponent<Collider>());

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyGround;
        lockMarker.transform.SetParent(root.transform, false);
        lockMarker.transform.localPosition = new Vector3(0f, 1f, 0f);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = 3f;

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        GameObject turretYaw = new GameObject("TurretYaw");
        turretYaw.transform.SetParent(root.transform, false);
        turretYaw.transform.localPosition = new Vector3(0f, bs.y + 0.2f, 0f);

        GameObject turretPitch = new GameObject("TurretPitch");
        turretPitch.transform.SetParent(turretYaw.transform, false);
        turretPitch.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);

        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrel.name = "Barrel";
        barrel.layer = LayerHitbox;
        barrel.transform.SetParent(turretPitch.transform, false);
        barrel.transform.localPosition = new Vector3(0f, 0f, spec.BarrelLength * 0.5f);
        barrel.transform.localScale = new Vector3(spec.BarrelThickness, spec.BarrelThickness, spec.BarrelLength);
        Object.DestroyImmediate(barrel.GetComponent<Collider>());

        GameObject gunObj = new GameObject("Gun");
        gunObj.transform.SetParent(turretPitch.transform, false);
        gunObj.transform.localPosition = new Vector3(0f, 0f, spec.BarrelLength + 0.1f);

        AddTargetAnimator(root);

        SaccEntity entity = root.AddUdonSharpComponent<SaccEntity>();
        SaccTarget targetHealth = root.AddUdonSharpComponent<SaccTarget>();
        ACO_AAGunSite aa = root.AddUdonSharpComponent<ACO_AAGunSite>();
        ACO_NPCGun gun = gunObj.AddUdonSharpComponent<ACO_NPCGun>();

        entity.AAMTargetsLayer = 1 << LayerFriendlyAir;
        entity.ExtensionUdonBehaviours = new UdonSharp.UdonSharpBehaviour[] { aa };

        targetHealth.Health = spec.Health;
        targetHealth.ArmorStrength = spec.Armor;
        // 0 = 자동 부활 안 함. 스폰/웨이브 매니저가 ACO_AAGunSite.Respawn()을 호출한다.
        targetHealth.RespawnDelay = 0f;
        targetHealth.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { aa };

        // 지상 대공포 기본 수치는 시뮬레이션으로 정했다(DESIGN.md 6-1): 직진하면 한 번 통과에 거의 격추, 지그재그하면 거의 무피해.
        gun.EntityControl = entity;
        gun.Muzzle = gunObj.transform;
        gun.ShooterFlight = null;
        gun.InheritShooterVelocity = false; // 고정 포대(함선은 느려서 무시)
        gun.MaxRange = spec.MaxRange;
        gun.MuzzleVelocity = spec.MuzzleVelocity;
        gun.FireStartAngle = spec.FireStartAngle;   // 넓게 — 피하는 중에도 계속 쏴서 위협이 보이게
        gun.EffectiveAngle = spec.EffectiveAngle;
        gun.DwellTime = spec.DwellTime;
        gun.BurstDuration = spec.BurstDuration;
        gun.BurstCooldown = spec.BurstCooldown;
        gun.DamagePerTick = spec.DamagePerTick;
        gun.MinDamageInterval = spec.MinDamageInterval;
        gun.MaxDamageInterval = spec.MaxDamageInterval;
        gun.Spread = spec.Spread;
        gun.VisualParticle = CreateTracerParticle(gunObj.transform, gun.MuzzleVelocity, gun.MaxRange, gun.Spread, spec.TracerRate);

        aa.EntityControl = entity;
        aa.TargetHealth = targetHealth;
        aa.Gun = gun;
        aa.TargetLayers = 1 << LayerFriendlyAir;
        aa.TurretYaw = turretYaw.transform;
        aa.TurretPitch = turretPitch.transform;
        aa.HideOnDeath = new GameObject[] { body, turretYaw };
        aa.DisableCollidersOnDeath = new Collider[] { hitbox };
        aa.ScoreValue = spec.Score;
        aa.MarkerLabel = spec.Label;
        aa.DetectRange = spec.DetectRange;
        aa.LoseTargetRange = spec.LoseTargetRange;
        aa.YawRate = spec.YawRate;
        aa.PitchRate = spec.PitchRate;
        aa.TrackingLag = spec.TrackingLag;
        aa.AimError = spec.AimError;
        return root;
    }

    private enum PartKind { VLS, MainGun, CIWS }

    private class PartSpec
    {
        public PartKind Kind;
        public string Name;
        public Vector3 Position;
        public PartSpec(PartKind kind, string name, Vector3 position) { Kind = kind; Name = name; Position = position; }
    }

    /// 함선 수치. 루트 = 해수면. 선체 박스는 흘수선 아래 3m부터.
    private class ShipSpec
    {
        public string PrefabName;
        public string TargetName;
        public string Label;
        public bool HighValue;
        public int Score;
        public float Health;
        public float Armor;
        public float NoDamageBelow;
        public float PartHullDamage;
        public float CruiseSpeed;
        public float CruiseRadius;
        public float EscortSpacing;
        public Vector3 HullSize;
        public Vector3 BridgeSize;
        public Vector3 BridgeCenter;
        public Vector3 FunnelCenter;
        public Vector3 FunnelSize;
        public float MastHeight;
        public PartSpec[] Parts;
    }

    // 장갑 4: SAV 미사일 직격 = 최대 체력의 25%. 기총(파티클 10/4 = 2.5)은 NoDamageBelow 3에 걸려 선체엔 안 들어간다.
    private static ShipSpec BattleshipSpec()
    {
        return new ShipSpec
        {
            PrefabName = "NPC_Warship", TargetName = "Battleship", Label = "BB", HighValue = true, Score = 1500,
            Health = 1000f, Armor = 4f, NoDamageBelow = 3f, PartHullDamage = 0.12f,
            CruiseSpeed = 12f, CruiseRadius = 2500f, EscortSpacing = 500f,
            HullSize = new Vector3(16f, 9f, 120f),
            BridgeSize = new Vector3(10f, 10f, 24f), BridgeCenter = new Vector3(0f, 11f, -5f),
            FunnelSize = new Vector3(5f, 6f, 7f), FunnelCenter = new Vector3(0f, 19f, -14f), MastHeight = 12f,
            Parts = new[]
            {
                new PartSpec(PartKind.MainGun, "MainGun", new Vector3(0f, 6f, 36f)),
                new PartSpec(PartKind.VLS, "VLS_Fore", new Vector3(0f, 6f, 22f)),
                new PartSpec(PartKind.VLS, "VLS_Aft", new Vector3(0f, 6f, -34f)),
                new PartSpec(PartKind.CIWS, "CIWS_Fore", new Vector3(3.5f, 16f, 4f)),
                new PartSpec(PartKind.CIWS, "CIWS_Aft", new Vector3(-3.5f, 16f, -14f)),
            },
        };
    }

    // 장갑 2.5: 미사일 직격 = 40%. 부위 하나가 20% — 부위 둘을 부수고 미사일 2발.
    private static ShipSpec DestroyerSpec()
    {
        return new ShipSpec
        {
            PrefabName = "NPC_Destroyer", TargetName = "Destroyer", Label = "DD", HighValue = false, Score = 500,
            Health = 500f, Armor = 2.5f, NoDamageBelow = 3f, PartHullDamage = 0.2f,
            CruiseSpeed = 14f, CruiseRadius = 1500f, EscortSpacing = 350f,
            HullSize = new Vector3(11f, 7f, 80f),
            BridgeSize = new Vector3(8f, 7f, 14f), BridgeCenter = new Vector3(0f, 8f, -2f),
            FunnelSize = new Vector3(4f, 5f, 5f), FunnelCenter = new Vector3(0f, 6.5f, -14f), MastHeight = 9f,
            Parts = new[]
            {
                new PartSpec(PartKind.VLS, "VLS", new Vector3(0f, 4f, 20f)),
                new PartSpec(PartKind.CIWS, "CIWS", new Vector3(0f, 11.5f, 2f)),
            },
        };
    }

    private static GunSpec MainGunSpec()
    {
        return new GunSpec
        {
            BodySize = new Vector3(6f, 3f, 7f), BarrelLength = 10f, BarrelThickness = 0.6f,
            Health = 250f, Armor = 1.5f, Score = 250, Label = "GUN",
            DetectRange = 5000f, LoseTargetRange = 6000f, YawRate = 25f, PitchRate = 15f, TrackingLag = 1.2f, AimError = 1.5f,
            MaxRange = 4500f, MuzzleVelocity = 850f, FireStartAngle = 10f, EffectiveAngle = 3.5f, DwellTime = 1.2f,
            BurstDuration = 2f, BurstCooldown = 3f, DamagePerTick = 10f, MinDamageInterval = 0.5f, MaxDamageInterval = 1.2f,
            Spread = 1.5f, TracerRate = 4f,
        };
    }

    private static GunSpec CiwsSpec()
    {
        return new GunSpec
        {
            BodySize = new Vector3(2f, 2f, 2f), BarrelLength = 2.5f, BarrelThickness = 0.25f,
            Health = 100f, Armor = 1f, Score = 150, Label = "CIWS",
            DetectRange = 1800f, LoseTargetRange = 2200f, YawRate = 140f, PitchRate = 110f, TrackingLag = 0.35f, AimError = 0.6f,
            MaxRange = 1500f, MuzzleVelocity = 1100f, FireStartAngle = 6f, EffectiveAngle = 2f, DwellTime = 0.35f,
            BurstDuration = 1.5f, BurstCooldown = 1f, DamagePerTick = 2f, MinDamageInterval = 0.1f, MaxDamageInterval = 0.4f,
            Spread = 0.6f, TracerRate = 60f,
        };
    }

    /// 함선 프리팹. 루트 = 해수면, 본체 콜라이더/SaccTarget/Animator는 루트(SaccTarget 규칙).
    /// 모델과 부위는 "Body" 아래 — 격침 애니메이션은 Body를 움직이면 선체와 부위가 같이 가라앉는다(루트는 스크립트가 순항시키므로 애니메이션으로 건드리지 않는다).
    private static GameObject BuildShipPrefab(ShipSpec spec, GameObject vlsMissile)
    {
        GameObject root = new GameObject(spec.PrefabName);
        root.layer = LayerHitbox;

        Vector3 hullCenter = new Vector3(0f, spec.HullSize.y * 0.5f - 3f, 0f);
        float deckY = spec.HullSize.y - 3f;

        BoxCollider hullCol = root.AddComponent<BoxCollider>();
        hullCol.size = spec.HullSize;
        hullCol.center = hullCenter;
        BoxCollider bridgeCol = root.AddComponent<BoxCollider>();
        bridgeCol.size = spec.BridgeSize;
        bridgeCol.center = spec.BridgeCenter;

        GameObject body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        GameObject model = new GameObject("Model");
        model.transform.SetParent(body.transform, false);
        AddBox(model.transform, "Hull", hullCenter, spec.HullSize);
        AddBox(model.transform, "Bridge", spec.BridgeCenter, spec.BridgeSize);
        AddBox(model.transform, "Funnel", spec.FunnelCenter, spec.FunnelSize);
        float bridgeTop = spec.BridgeCenter.y + spec.BridgeSize.y * 0.5f;
        AddBox(model.transform, "Mast", new Vector3(0f, bridgeTop + spec.MastHeight * 0.5f, spec.BridgeCenter.z + 3f), new Vector3(1f, spec.MastHeight, 1f));

        GameObject lockMarker = new GameObject("AAMTarget");
        lockMarker.layer = LayerEnemyGround;
        lockMarker.transform.SetParent(root.transform, false);
        lockMarker.transform.localPosition = new Vector3(0f, deckY + 2f, 0f);
        SphereCollider markerCol = lockMarker.AddComponent<SphereCollider>();
        markerCol.isTrigger = true;
        markerCol.radius = spec.HullSize.z * 0.15f;

        // SAV 격침 애니메이션(TargetExplode)이 "Effects"를 켠다. 그 밖의 격침 연출은 DeathEffects(ShowOnDeath)에 넣는다.
        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        GameObject deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(body.transform, false);
        deathFx.transform.localPosition = new Vector3(0f, deckY + 2f, 0f);
        deathFx.SetActive(false);

        var parts = new UdonSharp.UdonSharpBehaviour[spec.Parts.Length];
        for (int i = 0; i < spec.Parts.Length; i++)
        {
            PartSpec ps = spec.Parts[i];
            GameObject part;
            if (ps.Kind == PartKind.VLS) { part = CreateVLSObject(ps.Name, vlsMissile); }
            else if (ps.Kind == PartKind.MainGun) { part = CreateAAGunObject(ps.Name, MainGunSpec()); }
            else { part = CreateAAGunObject(ps.Name, CiwsSpec()); }
            AttachPart(body, part, ps.Position);
            ACO_SAMSite sam = part.GetComponent<ACO_SAMSite>();
            ACO_AAGunSite aa = part.GetComponent<ACO_AAGunSite>();
            parts[i] = sam != null ? (UdonSharp.UdonSharpBehaviour)sam : aa;
        }

        AddTargetAnimator(root);
        SaccTarget hull = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship = root.AddUdonSharpComponent<ACO_Warship>();

        hull.Health = spec.Health;
        hull.ArmorStrength = spec.Armor;
        hull.NoDamageBelow = spec.NoDamageBelow;
        hull.RespawnDelay = 0f;
        hull.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ship };

        ship.TargetHealth = hull;
        ship.Parts = parts;
        ship.TargetName = spec.TargetName;
        ship.MarkerLabel = spec.Label;
        ship.ScoreValue = spec.Score;
        ship.HighValue = spec.HighValue;
        ship.PartHullDamage = spec.PartHullDamage;
        ship.CruiseSpeed = spec.CruiseSpeed;
        ship.CruiseRadius = spec.CruiseRadius;
        ship.EscortSpacing = spec.EscortSpacing;
        ship.ShowOnDeath = new GameObject[] { deathFx };
        ship.HideOnDeath = new GameObject[0];
        ship.DisableCollidersOnDeath = new Collider[] { hullCol, bridgeCol };

        return SaveAndCleanup(root, $"{PrefabDir}/{spec.PrefabName}.prefab");
    }

    private static GameObject AttachPart(GameObject ship, GameObject part, Vector3 localPos)
    {
        part.transform.SetParent(ship.transform, false);
        part.transform.localPosition = localPos;
        return part;
    }

    private static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = LayerHitbox;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = center;
        box.transform.localScale = size;
        Object.DestroyImmediate(box.GetComponent<Collider>());
    }

    /// 시각 전용 탄 파티클. 충돌은 끈다 — 명중은 ACO_NPCGun이 타겟 오너에서 해석적으로 계산한다.
    /// NPC 플레어 연출: 스크립트가 Emit으로 한 번에 뿌린다(자동 방출 없음). 월드 시뮬레이션 + 기체 속도 상속 + 약한 중력.
    private static ParticleSystem CreateFlareParticle(Transform parent, Vector3 localPos)
    {
        GameObject go = new GameObject("Flares");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        // 콘의 축(+Z)이 뒤쪽 아래를 향하게
        go.transform.localRotation = Quaternion.Euler(30f, 180f, 0f);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(15f, 35f);
        main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.6f);
        main.startColor = new Color(1f, 0.8f, 0.45f, 1f);
        main.gravityModifier = 0.35f;
        main.maxParticles = 64;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // NPC는 Transform으로 움직이므로 이동량으로 방출기 속도를 구해야 플레어가 기체 속도를 물려받는다.
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.2f;

        ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;
        inherit.enabled = true;
        inherit.mode = ParticleSystemInheritVelocityMode.Initial;
        inherit.curveMultiplier = 0.8f;

        // 타면서 작아진다
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        if (mat) { renderer.sharedMaterial = mat; }
        return ps;
    }

    private static ParticleSystem CreateTracerParticle(Transform parent, float muzzleVelocity, float maxRange, float spread, float rate = 25f)
    {
        GameObject go = new GameObject("Tracers");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startSpeed = muzzleVelocity;
        main.startLifetime = maxRange / Mathf.Max(muzzleVelocity, 1f);
        main.startSize = 0.25f;
        main.maxParticles = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // NPC는 Transform으로 움직이므로 이동량으로 방출기 속도를 구해야 탄이 기체 속도를 물려받는다.
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = spread;
        shape.radius = 0.01f;

        ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;
        inherit.enabled = true;
        inherit.mode = ParticleSystemInheritVelocityMode.Initial;
        inherit.curveMultiplier = 1f;

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled = false;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.02f;
        renderer.lengthScale = 1f;
        Material mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        if (mat) { renderer.sharedMaterial = mat; }

        return ps;
    }

    private static void AddTargetAnimator(GameObject go)
    {
        Animator animator = go.AddComponent<Animator>();
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(TargetControllerPath);
        if (controller) { animator.runtimeAnimatorController = controller; }
        else { Debug.LogWarning($"[ACO] Target.controller를 찾지 못했습니다: {TargetControllerPath} — SaccTarget이 Animator 파라미터를 못 찾아 경고를 낼 수 있습니다."); }
        // 풀에서 껐다 켤 때 다시 바인딩하지 않게(아래 ACO_SceneBaker.BakeTargetAnimators 설명)
        animator.keepAnimatorStateOnDisable = true;
    }

    private static Transform NewChild(GameObject parent, string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

    private static GameObject SaveAndCleanup(GameObject root, string path)
    {
        ACO_SyncModeFixer.FixHierarchy(root, false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }
}
