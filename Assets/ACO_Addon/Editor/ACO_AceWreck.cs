using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// 에이스 추락 파티클(6-145, ACO_AirWreck): 에이스 프리팹마다 동체(격추 때 숨기는 모델에서 가장 큰 메시)를 메시 파티클 하나로 던진다.
/// 메시는 원래 메시를 그대로 쓴다(굽지 않는다) — 파티클을 그 렌더러와 같은 위치·회전·크기에 두면 기체 자세 그대로 나온다.
/// 재질은 탄 듯한 검갈색 하나(ACO_WreckBurnt — 모든 에이스가 같이). 나머지 작은 부품은 던지지 않는다(파편 파티클 ACO_WreckFx_Air가 같이 튄다).
/// Setup Check ace-wreck-1이 쓰고, SHADE 모델을 바꾸면(shade-model-1) 다시 잇는다. 다시 돌리면 파티클만 새로 만든다.
public static class ACO_AceWreck
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string MaterialPath = "Assets/ACO_Addon/Generated/ACO_WreckBurnt.mat";
    private const string Holder = "WreckFall";
    private static readonly string[] Aces = { "NPC_Borderless", "NPC_Shade", "NPC_Heads", "NPC_Tails", "NPC_Luna", "NPC_MrS" };
    private const int GroundLayers = 2049 | 16;   // 기본·지형(11)·물(4)

    /// Setup Check ace-wreck-1.
    public static void ApplyAll()
    {
        foreach (string name in Aces)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Build(root, name))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] {name}: 추락 파티클(WreckFall)을 넣었습니다.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }

    /// 이미 추락 파티클이 있으면 다시 만든다(모델을 바꾼 뒤). 열어 둔 프리팹 루트에. 바꿨으면 true.
    public static bool Rebuild(GameObject root, string prefabName)
    {
        ACO_NPCFighterAI ai = root.GetComponentInChildren<ACO_NPCFighterAI>(true);
        return ai != null && ai.transform.Find(Holder) != null && Build(root, prefabName);
    }

    /// 탄 듯한 검갈색(무광) — 추락하는 동체 모두가 같이 쓴다.
    private static Material Burnt()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (m != null) { return m; }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        m = new Material(Shader.Find("Standard")) { name = "ACO_WreckBurnt" };
        m.color = new Color(0.075f, 0.058f, 0.045f);
        m.SetFloat("_Metallic", 0.15f);
        m.SetFloat("_Glossiness", 0.12f);
        AssetDatabase.CreateAsset(m, MaterialPath);
        return m;
    }

    /// 열어 둔 에이스 프리팹 하나에 추락 파티클을 만든다(있으면 새로). 바꿨으면 true.
    public static bool Build(GameObject root, string prefabName)
    {
        ACO_NPCFighterAI ai = root.GetComponentInChildren<ACO_NPCFighterAI>(true);
        if (ai == null) { return false; }
        ACO_AceBase ace = ai.Ace != null ? ai.Ace : root.GetComponentInChildren<ACO_AceBase>(true);
        if (ace == null || ai.HideOnDeath == null) { return false; }

        // 동체: 격추 때 숨기는 모델 아래 켜진 메시 중 가장 큰 것
        MeshRenderer body = null;
        float best = -1f;
        foreach (GameObject h in ai.HideOnDeath)
        {
            if (h == null || !h.activeSelf) { continue; }
            foreach (MeshRenderer r in h.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.name.Contains("Glow")) { continue; }
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { continue; }
                float size = r.bounds.size.sqrMagnitude;
                if (size > best) { best = size; body = r; }
            }
        }
        if (body == null)
        {
            Debug.LogWarning($"[ACO] {prefabName}: 추락 파티클에 쓸 동체 메시가 없습니다(HideOnDeath 아래 켜진 MeshRenderer).", root);
            return false;
        }

        Transform holder = ai.transform.Find(Holder);
        if (holder == null)
        {
            holder = new GameObject(Holder).transform;
            holder.SetParent(ai.transform, false);
        }
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one;
        for (int i = holder.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(holder.GetChild(i).gameObject); }
        ACO_AirWreck wreck = holder.GetComponent<ACO_AirWreck>();
        if (wreck == null) { wreck = holder.gameObject.AddUdonSharpComponent<ACO_AirWreck>(); }

        // 도는 방향은 에이스마다(이름 글자 합) — 같은 에이스는 늘 같다
        int sum = 0;
        foreach (char c in prefabName) { sum += c; }
        float sign = (sum & 1) == 0 ? 1f : -1f;
        wreck.Body = ai.transform;
        wreck.Pieces = new[] { MakePiece(holder, body) };
        wreck.PieceKick = new[] { Vector3.zero };
        wreck.PieceSpin = new[] { new Vector3(-8f, 0f, 150f * sign) };   // 기수가 조금씩 숙여지며 옆으로 빙글빙글
        EditorUtility.SetDirty(wreck);
        ace.Wreck = wreck;
        EditorUtility.SetDirty(ace);
        return true;
    }

    /// 재질 칸이 여럿인 메시(몸체 · 유리)를 칸 하나로 합친 사본(Generated/{이름}_Wreck.asset — 다시 만들면 덮어쓴다). 칸이 하나면 그대로.
    private static Mesh SingleSubmesh(Mesh m)
    {
        if (m == null || m.subMeshCount <= 1) { return m; }
        string path = $"Assets/ACO_Addon/Generated/{m.name}_Wreck.asset";
        Mesh w = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = w == null;
        if (create) { w = new Mesh(); }
        w.Clear();
        w.name = m.name + "_Wreck";
        w.indexFormat = m.indexFormat;
        w.vertices = m.vertices;
        w.normals = m.normals;
        w.uv = m.uv;
        w.triangles = m.triangles;   // 모든 칸의 삼각형
        w.RecalculateBounds();
        if (create) { AssetDatabase.CreateAsset(w, path); }
        else { EditorUtility.SetDirty(w); }
        return w;
    }

    /// 동체 메시 파티클: 렌더러와 같은 위치·회전·크기, 원래 메시, 탄 재질 하나.
    private static ParticleSystem MakePiece(Transform holder, MeshRenderer source)
    {
        var go = new GameObject("Wreck_Body");
        go.transform.SetParent(holder, false);
        go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        Vector3 ls = source.transform.lossyScale;
        Vector3 hs = holder.lossyScale;
        go.transform.localScale = new Vector3(ls.x / hs.x, ls.y / hs.y, ls.z / hs.z);
        Mesh mesh = SingleSubmesh(source.GetComponent<MeshFilter>().sharedMesh);   // 메시 파티클은 칸 하나만 그린다

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 8f;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startColor = Color.white;
        main.gravityModifier = 1.5f;
        main.maxParticles = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // 렌더러와 같은 크기
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
        ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 3000f;
        drag.drag = 0.12f;   // 앞으로 가는 속도가 서서히 줄어 포물선
        drag.multiplyDragByParticleSize = false;
        drag.multiplyDragByParticleVelocity = false;
        ParticleSystem.CollisionModule col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.collidesWith = GroundLayers;
        col.lifetimeLoss = 1f;   // 땅에 닿으면 사라진다 → 끝 폭발
        col.dampen = 0f;
        col.bounce = 0f;
        col.quality = ParticleSystemCollisionQuality.High;
        col.radiusScale = 0.05f;
        col.enableDynamicColliders = false;
        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Mesh;
        pr.mesh = mesh;
        pr.sharedMaterial = Burnt();
        pr.alignment = ParticleSystemRenderSpace.Local;   // 렌더러 자세 그대로
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        pr.receiveShadows = true;
        ACO_FxBuilder.BuildFallFx(ps, source.bounds.size.magnitude);
        return ps;
    }
}
