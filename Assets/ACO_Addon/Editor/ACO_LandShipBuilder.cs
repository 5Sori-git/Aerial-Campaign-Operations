using System;
using System.Collections.Generic;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 육상전함 NPC_LandShip: 모델은 Models/LandShip/ACO_LandShip.fbx(Tools/aco_landship_model.py)의 메시를 꺼내 이 빌더가 자리에 놓는다.
/// 아래 자리 값은 그 스크립트의 POD_*, SKIRT_*, MAIN_TURRETS, SECOND_TURRETS, MAIN_GUN_PIVOT, CORE_* 와 같아야 한다.
/// 구조(루트 = 함선 규칙 — 코어 SaccTarget·히트박스·Animator는 루트):
///  - Model(메시만, 충돌 없음) / Hull(선체 메시 충돌 — 자체 SaccTarget이 피해를 흡수해 코어로 번지지 않는다, Environment 레이어)
///  - Parts: 스커트 12·궤도 12·배기구 8(ACO_FortressPart) + 주포 3(소형 레일건 프리팹, 2연발) + 부포 4(Flak 프리팹). 포대 프리팹의 모델은 끄고 이 함선의 포탑·포신 메시를 그 회전축에 단다.
///  - 방공(VLS·CIWS·Flak)은 공용 포대 프리팹을 LsVlsSites·LsCiwsSites·LsFlakSites에 놓는다. 프리팹에 더 놓은 포대도 Bake가 부위로 묶는다.
public static partial class ACO_PrefabBuilder
{
    private const string LandShipFbx = "Assets/ACO_Addon/Models/LandShip/ACO_LandShip.fbx";
    private const string LandShipAlbedo = "Assets/ACO_Addon/Models/LandShip/ACO_LandShip_Albedo.png";
    public const string LandShipPrefab = "NPC_LandShip";

    // aco_landship_model.py와 같은 값
    private static readonly float[] LsPodZ = { -81f, -27f, 27f, 81f };
    private static readonly float[] LsInnerPodZ = { -138f, 138f };
    private const float LsPodX = 42f;
    private const float LsInnerPodX = 21.5f;
    private const float LsSkirtX = 49.2f;
    private const float LsInnerSkirtX = LsInnerPodX + LsSkirtX - LsPodX;
    private static readonly Vector4[] LsMainTurrets = { new Vector4(0f, 52f, 200f, 0f), new Vector4(0f, 58f, 144f, 0f), new Vector4(0f, 52f, -170f, 180f) };
    private static readonly Vector4[] LsSecondTurrets = { new Vector4(-22.5f, 51f, 118f, 0f), new Vector4(22.5f, 51f, 118f, 0f), new Vector4(-23f, 51f, -122f, 180f), new Vector4(23f, 51f, -122f, 180f) };
    private const float LsSecondScale = 0.45f;
    private static readonly Vector3 LsMainGunPivot = new Vector3(0f, 4.6f, 12.5f);
    private const float LsCoreZ = -76f;
    /// 방공(공용 포대 프리팹) 자리 — 갑판 y 48, 1단 지붕 y 57. VLS는 갑판의 어두운 자리(텍스처)에 둔다.
    /// 네 셀 VLS: 1단 지붕 앞 1기.
    private static readonly Vector3[] LsVlsSites = { new Vector3(0f, 57f, 66f) };
    /// 긴 VLS(16셀, NPC_Turret_VLS16): 궤도 위 넓은 갑판(y 48, 평평한 띠 x ±33..50, z -102..102) 바깥 줄에 한쪽 2기.
    /// 크기 2 → 11 x 40m. 수리 대상이 아니다(한 번 부수면 끝).
    private static readonly Vector3[] LsLongVlsSites = { new Vector3(42f, 48f, -60f), new Vector3(-42f, 48f, -60f), new Vector3(42f, 48f, 15f), new Vector3(-42f, 48f, 15f) };
    private const float LsLongVlsScale = 2f;
    private static readonly Vector3[] LsCiwsSites = { new Vector3(0f, 57f, 83f), new Vector3(9f, 57f, -96f), new Vector3(-9f, 57f, -96f) };
    private static readonly Vector3[] LsFlakSites = { new Vector3(40f, 48f, 96f), new Vector3(-40f, 48f, 96f), new Vector3(22.5f, 57f, -20f), new Vector3(-22.5f, 57f, -20f) };
    private const float LsGunScale = 2.5f;
    private const float LsVlsScale = 3f;
    private static readonly Vector3 LsCoreHinge = new Vector3(13.6f, 63.95f, LsCoreZ);

    /// 육상전함 선체 레이어(Environment) — 무장·포대 시야가 지형처럼 막힌다. 땅을 재는 쪽은 Bake가 이 레이어를 뺀다(요새와 같은 레이어).
    private const int LayerLandShipHull = LayerFortressHull;

    private static Mesh LsMesh(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(LandShipFbx);
        Transform node = asset != null ? asset.transform.Find(path) : null;
        MeshFilter filter = node != null ? node.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) { throw new InvalidOperationException($"[ACO] {LandShipFbx}에 {path}가 없습니다. Tools/aco_landship_model.py를 실행하세요."); }
        return filter.sharedMesh;
    }

    private static Material LandShipMaterial()
    {
        var importer = AssetImporter.GetAtPath(LandShipAlbedo) as TextureImporter;
        if (importer != null && importer.maxTextureSize != 4096)
        {
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_LandShip.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_LandShip" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(LandShipAlbedo));
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", 0.15f);
        m.SetFloat("_Glossiness", 0.35f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 코어·배기구의 빛(둘째 재질 칸).
    private static Material LandShipGlowMaterial()
    {
        return FortressMaterial("ACO_LandShip_Glow", new Color(0.3f, 0.75f, 1f), 0f, 0.6f, new Color(0.9f, 2.4f, 3.6f));
    }

    /// 메시를 붙인 오브젝트. 재질 칸 수는 메시의 서브메시 수(둘째 칸 = 빛).
    private static GameObject LsVisual(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(name);
        go.layer = LayerHitbox;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        var used = new Material[Mathf.Max(mesh.subMeshCount, 1)];
        for (int i = 0; i < used.Length; i++) { used[i] = mats[Mathf.Min(i, mats.Length - 1)]; }
        r.sharedMaterials = used;
        return go;
    }

    /// 주포 최소 폭발 거리(m) — 이보다 가까우면 직격만(소형 레일건 기본 1000m의 두 배). 폭발 반경은 ZoneRadius 그대로
    private const float LsMainGunMinRange = 2000f;
    /// 주포 폭발 지역이 남는 시간(초) — 깔아 두는 사격(3.5초 뒤 자리)이라 표적이 닿을 때까지 남고 조금 더 머문다
    public const float LsMainGunZoneTime = 5f;
    /// 주포 폭발 지역 반경(m) — 320에서 1.5배(껍질이 반경의 두 배로 보이던 것을 고친 뒤 너무 작아 보였다)
    public const float LsMainGunZoneRadius = 480f;
    // 물 위 자세: 루트 = 해수면 - SeaDraft(14m) → 항적은 루트 위 14m(수면)
    private const float LsWakeHeight = 14f;

    /// 이동 연출: 앞뒤 끝 궤도(배기구가 없는 궤도) 아래 흙먼지 + 물 위 항적(기존 ACO_WaterFx 항적). 이미 있으면 그대로.
    private static bool AddLandShipMoveFx(Transform root, ACO_LandShip ls)
    {
        bool changed = false;
        if (root.Find(ACO_WaterFxBuilder.WakeName) == null)
        {
            GameObject wake = ACO_WaterFxBuilder.AddWakeSized(root, LsWakeHeight, -240f, 240f, 90f);
            // 땅 판정은 지형(Default)만 — 선체(Environment) 안에서 쏜 선이 선체에 맞아 물을 못 보지 않게
            wake.GetComponent<ACO_WaterFx>().GroundLayers = 1;
            changed = true;
        }
        if (ls.Tracks == null) { return changed; }
        var dust = new List<ParticleSystem>(ls.Dust ?? new ParticleSystem[0]);
        var dustTrack = new List<int>(ls.DustTrack ?? new int[0]);
        for (int i = 0; i < ls.Tracks.Length; i++)
        {
            ACO_FortressPart tp = ls.Tracks[i];
            if (tp == null || (ls.VentTrack != null && System.Array.IndexOf(ls.VentTrack, i) >= 0)) { continue; }
            if (dustTrack.Contains(i)) { continue; }
            float side = tp.transform.localPosition.x >= 0f ? 1f : -1f;
            Vector3 p = tp.transform.localPosition;
            // 궤도 뒤쪽 아래(앞 궤도는 가운데 쪽으로, 뒤 궤도는 선미 쪽으로 흘린다 — 둘 다 진행 방향 뒤)
            dust.Add(CreateTrackDust(root, "TrackDust_" + tp.name, new Vector3(p.x + side * 0.5f, 3f, p.z - 18f)));
            dustTrack.Add(i);
            changed = true;
        }
        if (changed)
        {
            ls.Dust = dust.ToArray();
            ls.DustTrack = dustTrack.ToArray();
            EditorUtility.SetDirty(ls);
        }
        return changed;
    }

    /// 흙먼지: 움직인 거리만큼 방출(멈추면 없음), 크게 부풀며 옅어진다. 월드 좌표.
    private static ParticleSystem CreateTrackDust(Transform parent, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 원뿔 축이 위로
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(14f, 26f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.01f;
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0.6f;   // 최고 속도(10m/s)에 초당 6개
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 8f;
        ACO_FxBuilder.StyleDust(ps);   // 지상 폭발 흙먼지와 같은 연기 텍스처·색
        return ps;
    }

    /// Setup Check: 이미 만든 NPC_LandShip에 주포 최소 폭발 거리 2km, 앞뒤 끝 궤도 흙먼지, 물 위 항적을 넣는다(다시 만들지 않는다).
    public static void PlaceLandShipMoveFx()
    {
        string path = PathOf(LandShipPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, root =>
        {
            ACO_LandShip ls = root.GetComponent<ACO_LandShip>();
            if (ls == null) { return; }
            int guns = 0;
            if (ls.RepairGuns != null)
            {
                foreach (UdonSharp.UdonSharpBehaviour g in ls.RepairGuns)
                {
                    ACO_MiniRailgun mini = g != null ? g.GetComponent<ACO_MiniRailgun>() : null;
                    if (mini == null || Mathf.Approximately(mini.MinRange, LsMainGunMinRange)) { continue; }
                    mini.MinRange = LsMainGunMinRange;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(mini);
                    EditorUtility.SetDirty(mini);
                    guns++;
                }
            }
            bool fx = AddLandShipMoveFx(root.transform, ls);
            // 이미 단 먼지·항적도 지금 FX 재질로 다시 입힌다(먼지 = 연기 플립북, 항적 = 함선 항적 거품)
            if (ls.Dust != null) { foreach (ParticleSystem d in ls.Dust) { ACO_FxBuilder.StyleDust(d); } }
            Transform wake = root.transform.Find(ACO_WaterFxBuilder.WakeName);
            if (wake != null) { ACO_FxBuilder.StyleWake(wake.GetComponent<ACO_WaterFx>()); }
            Debug.Log($"[ACO] 육상전함: 주포 {guns}문 최소 폭발 거리 {LsMainGunMinRange:0}m, 흙먼지·항적 {(fx ? "넣음" : "이미 있음")}.");
        });
    }

    /// 앞뒤 끝 궤도(배기구 없음 — 선택 목표): 부서져도 궤도 메시를 숨기지 않고 남겨, 한 번 터진 뒤 계속 연기를 낸다.
    /// 이미 연기가 있으면 그대로(여러 번 불러도 같다). side = +1 오른쪽, -1 왼쪽.
    private static bool MakeEndTrackWreck(ACO_FortressPart tp, float side)
    {
        bool changed = false;
        if (tp.HideOnDeath != null && tp.HideOnDeath.Length > 0) { tp.HideOnDeath = new GameObject[0]; changed = true; }
        if (tp.transform.Find("TrackSmoke") == null)
        {
            var smoke = new GameObject("TrackSmoke");
            smoke.transform.SetParent(tp.transform, false);
            smoke.transform.localPosition = new Vector3(side * 2f, 8f, 0f);
            CreateFortressSmoke(smoke.transform, "Smoke", Vector3.zero, 7f, 40f);
            smoke.SetActive(false);
            var show = new List<GameObject>(tp.ShowOnDeath ?? new GameObject[0]);
            show.Add(smoke);
            tp.ShowOnDeath = show.ToArray();
            changed = true;
        }
        if (changed) { EditorUtility.SetDirty(tp); }
        return changed;
    }

    /// Setup Check: 이미 만든 NPC_LandShip의 앞뒤 끝 궤도(배기구가 없는 궤도)를 부서져도 남고 연기를 내게 바꾼다(다시 만들지 않는다).
    public static void PlaceLandShipEndTracks()
    {
        string path = PathOf(LandShipPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, root =>
        {
            ACO_LandShip ls = root.GetComponent<ACO_LandShip>();
            if (ls == null || ls.Tracks == null) { return; }
            int n = 0;
            for (int i = 0; i < ls.Tracks.Length; i++)
            {
                ACO_FortressPart tp = ls.Tracks[i];
                if (tp == null || (ls.VentTrack != null && System.Array.IndexOf(ls.VentTrack, i) >= 0)) { continue; }
                if (MakeEndTrackWreck(tp, tp.transform.localPosition.x >= 0f ? 1f : -1f)) { n++; }
            }
            Debug.Log($"[ACO] 육상전함 앞뒤 끝 궤도 {n}개: 부서져도 남고 연기를 냅니다.");
        });
    }

    /// 부위 하나(스커트·궤도·배기구): 히트박스, 락온 마커(지상 레이어), 파괴 시 숨길 메시·켤 연기.
    private static ACO_FortressPart AddLandShipPart(Transform parts, string name, string label, Vector3 center, Vector3 size, float health, int score, GameObject hide, GameObject show)
    {
        var root = new GameObject(name);
        root.layer = LayerHitbox;
        root.transform.SetParent(parts, false);
        root.transform.localPosition = center;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = size;
        box.enabled = false;
        GameObject marker = NewLockMarker(root.transform, PartMarkerName, LayerEnemyGround, Mathf.Min(size.y, 12f));
        marker.SetActive(false);
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        if (show != null) { show.SetActive(false); }

        AddTargetAnimator(root);
        SaccTarget hp = root.AddUdonSharpComponent<SaccTarget>();
        ACO_FortressPart part = root.AddUdonSharpComponent<ACO_FortressPart>();
        hp.Health = health;
        hp.ArmorStrength = 1f;
        hp.RespawnDelay = 0f;
        hp.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { part };
        part.TargetHealth = hp;
        part.ScoreValue = score;
        part.MarkerLabel = label;
        part.LockMarkers = new[] { marker };
        part.Hitboxes = new Collider[] { box };
        part.HideOnDeath = hide != null ? new[] { hide } : new GameObject[0];
        part.ShowOnDeath = show != null ? new[] { show } : new GameObject[0];
        return part;
    }

    /// 포대 프리팹(인스턴스)의 회전축을 이 함선 포탑 자리에 옮기고, 포대 모델 대신 이 함선의 포탑·포신 메시를 단다.
    /// 포대 루트 = 포탑 선회축(받침 기준 조준 — 함선이 기울어도 갑판 위에서 돈다), TurretPitch = 포신 고각 축, 사격점 = 포구.
    private static ACO_AAGunSite FitLandShipTurret(GameObject part, Mesh turretMesh, Mesh gunMesh, Material[] mats, Vector3 pivot, float muzzle,
        Vector3 boxCenter, Vector3 boxSize, float minPitch, float maxPitch)
    {
        ACO_AAGunSite site = part.GetComponent<ACO_AAGunSite>();
        Transform yaw = site.TurretYaw;
        Transform pitch = site.TurretPitch;
        if (yaw == null || pitch == null || !pitch.IsChildOf(yaw)) { throw new InvalidOperationException($"[ACO] {part.name}: 포대 프리팹의 TurretYaw/TurretPitch 구조가 예상과 다릅니다."); }
        foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.transform.parent != null && r.transform.parent.name == "ZoneShells") { continue; }   // 폭발 지역 껍질은 포대 모델이 아니다
            r.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        Transform root = part.transform;
        yaw.SetPositionAndRotation(root.position, root.rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(yaw);
        pitch.SetPositionAndRotation(root.TransformPoint(pivot), root.rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(pitch);

        GameObject house = LsVisual(yaw, "LS_Turret", turretMesh, mats, Vector3.zero, Quaternion.identity);
        house.transform.SetPositionAndRotation(root.position, root.rotation);
        UnscaleChild(house.transform);
        GameObject barrel = LsVisual(pitch, "LS_Gun", gunMesh, mats, Vector3.zero, Quaternion.identity);
        barrel.transform.SetPositionAndRotation(pitch.position, root.rotation);
        UnscaleChild(barrel.transform);

        Vector3 tip = pitch.position + root.rotation * Vector3.forward * muzzle;
        ACO_MiniRailgun mini = part.GetComponent<ACO_MiniRailgun>();
        if (mini != null && mini.Muzzle != null)
        {
            mini.Muzzle.SetPositionAndRotation(tip, root.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mini.Muzzle);
        }
        if (site.Gun != null)
        {
            site.Gun.transform.SetPositionAndRotation(tip, root.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(site.Gun.transform);
        }
        BoxCollider box = part.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = boxCenter;
            box.size = boxSize;
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
        }
        Transform marker = root.Find("AAMTarget");
        if (marker != null)
        {
            marker.localPosition = boxCenter;
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
        }
        site.MountRelativeAim = true;
        site.MinPitch = minPitch;
        site.MaxPitch = maxPitch;
        PrefabUtility.RecordPrefabInstancePropertyModifications(site);
        return site;
    }

    /// 한 번 터지는 폭발(지상 파괴 효과에서 남는 불·연기를 끈 것). 처음엔 꺼 두고 파괴 때 켠다(ShowOnDeath).
    private static GameObject LsBlast(Transform parent, string name, Vector3 localPos)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = localPos;
        GameObject fx = (GameObject)PrefabUtility.InstantiatePrefab(ACO_FxBuilder.GroundDeathFx(), holder.transform);
        foreach (Transform child in fx.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != "Fx_Flame" && child.name != "Fx_Plume") { continue; }
            child.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
        }
        holder.SetActive(false);
        return holder;
    }

    /// 포대 파괴 연출: 포대 프리팹의 작은 파괴 효과를 포탑 높이로 올리고, 이 함선의 효과를 더한다.
    /// hideBarrel이면 포신(LS_Gun)이 사라진다(부포). 아니면 포탑·포신이 그대로 남는다(주포 — 고장 상태).
    private static void SetTurretDeath(ACO_AAGunSite site, bool hideBarrel, GameObject extra, Vector3 effectsPos)
    {
        var hide = new List<GameObject>();
        Transform model = site.transform.Find("Model");
        if (model != null) { hide.Add(model.gameObject); }
        if (hideBarrel && site.TurretPitch != null)
        {
            Transform barrel = site.TurretPitch.Find("LS_Gun");
            if (barrel != null) { hide.Add(barrel.gameObject); }
        }
        site.HideOnDeath = hide.ToArray();
        var show = new List<GameObject>();
        if (site.ShowOnDeath != null) { foreach (GameObject g in site.ShowOnDeath) { if (g != null) { show.Add(g); } } }
        show.Add(extra);
        site.ShowOnDeath = show.ToArray();
        Transform effects = site.transform.Find("DeathEffects");
        if (effects != null)
        {
            effects.localPosition = effectsPos;
            PrefabUtility.RecordPrefabInstancePropertyModifications(effects);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(site);
    }

    /// 주포 탄·공중 폭발 파티클(랜스 포대와 같은 꼴, 이 함선 것). 폭발 지역이 넓어(320m) 불덩이를 크게, 오래 둔다.
    private static void LinkLandShipMiniFx(Transform root, List<ACO_MiniRailgun> minis)
    {
        var fx = new GameObject("MiniFx").transform;
        fx.SetParent(root, false);
        Color white = new Color(0.8f, 0.95f, 1f);
        ParticleSystem shell = CreateBlastSystem(fx, "Shell", new Vector2(3f, 3f), Vector2.zero, new Vector2(16f, 16f), 0f, 24, white, white, 1f);
        SetBlastShape(shell, ParticleSystemShapeType.Sphere, 0.1f);
        ParticleSystemRenderer shellRenderer = shell.GetComponent<ParticleSystemRenderer>();
        shellRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        shellRenderer.velocityScale = MiniShellStretch;
        shellRenderer.lengthScale = 1f;
        ParticleSystem burst = CreateBlastSystem(fx, "Burst", new Vector2(3f, 3.8f), new Vector2(4f, 22f), new Vector2(70f, 140f), 0f, 400, new Color(1f, 0.55f, 0.18f), new Color(0.12f, 0.1f, 0.1f), 0.95f);
        SetBlastShape(burst, ParticleSystemShapeType.Sphere, 125f);
        ParticleSystem flash = CreateBlastSystem(fx, "BurstFlash", new Vector2(0.4f, 0.4f), Vector2.zero, new Vector2(420f, 420f), 0f, 12, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.2f), 1f);
        SetBlastShape(flash, ParticleSystemShapeType.Sphere, 0.1f);
        foreach (ACO_MiniRailgun mini in minis)
        {
            mini.ShellParticle = shell;
            mini.BurstParticle = burst;
            mini.BurstFlash = flash;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mini);
        }
        ACO_FxBuilder.StyleMiniFx(minis.ToArray());
        // 랜스 포대 값(지역 170m)보다 크고 오래 — 지역 320m, 8초
        ParticleSystem.MainModule burstMain = burst.main;
        burstMain.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
        burstMain.startSize = new ParticleSystem.MinMaxCurve(200f, 430f);
        SetBlastShape(burst, ParticleSystemShapeType.Sphere, 235f);
        ParticleSystem.MainModule flashMain = flash.main;
        flashMain.startSize = 1100f;
        if (minis.Count > 0 && minis[0].BurstSparks != null)
        {
            ParticleSystem.ShapeModule sparkShape = minis[0].BurstSparks.shape;
            sparkShape.radius = 110f;
        }
    }

    /// 갑판에 놓는 공용 대공포(CIWS·Flak) — 받침 기준 조준으로 바꾼다(함선이 최대 8도 기운다).
    private static ACO_AAGunSite LsDeckGun(string prefab, string name, Transform parts, Vector3 pos, float yaw)
    {
        GameObject gun = NewTurret(prefab, name, parts, pos, Quaternion.Euler(0f, yaw, 0f), LsGunScale);
        ACO_AAGunSite aa = gun.GetComponent<ACO_AAGunSite>();
        aa.MountRelativeAim = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(aa);
        return aa;
    }

    /// 부모 크기가 1이 아니어도 메시가 원래 크기로 보이게 한다.
    private static void UnscaleChild(Transform t)
    {
        Vector3 s = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(1f / Mathf.Max(s.x, 0.0001f), 1f / Mathf.Max(s.y, 0.0001f), 1f / Mathf.Max(s.z, 0.0001f));
    }

    /// 파괴된 배기구를 덮는 검게 그을린 판(파괴 연출).
    private static GameObject LsScorch(Transform parent, Vector3 center, Vector3 size)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "Scorch";
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = center;
        plate.transform.localScale = size;
        plate.GetComponent<MeshRenderer>().sharedMaterial = FortressMaterial("ACO_LandShipScorch", new Color(0.05f, 0.045f, 0.04f), 0f, 0.1f, Color.black);
        return plate;
    }

    /// U# 스크립트는 프로그램 에셋(Udon/*.asset)이 있어야 컴포넌트에 프로그램이 붙는다. 새 스크립트라 없으면 만들고 컴파일한다.
    /// 없는 채로 붙이면 "프로그램이 빠진 UdonBehaviour"가 된다.
    public static void EnsureLandShipPrograms()
    {
        bool made = false;
        made |= EnsureProgram("Assets/ACO_Addon/Scripts/GroundUnits/ACO_LandShip.cs", "Assets/ACO_Addon/Udon/ACO_LandShip.asset");
        made |= EnsureProgram("Assets/ACO_Addon/Scripts/Core/ACO_PhaseLandShip.cs", "Assets/ACO_Addon/Udon/ACO_PhaseLandShip.asset");
        if (!made) { return; }
        AssetDatabase.SaveAssets();
        UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
    }

    private static bool EnsureProgram(string scriptPath, string programPath)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null) { return false; }
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null) { throw new InvalidOperationException($"[ACO] {scriptPath}가 없습니다."); }
        var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, programPath);
        Debug.Log($"[ACO] U# 프로그램 에셋을 만들었습니다: {programPath}");
        return true;
    }

    public static GameObject BuildLandShip()
    {
        EnsurePrefabFolder();
        EnsureLandShipPrograms();
        string path = PathOf(LandShipPrefab);
        ConfigureModelImporter(LandShipFbx);
        Mesh hullMesh = LsMesh("LS_Hull");
        Mesh podMesh = LsMesh("LS_TrackPod_R1");
        Mesh skirtMesh = LsMesh("LS_Skirt_R1");
        Mesh mainTurretMesh = LsMesh("LS_MainTurret_1");
        Mesh mainGunMesh = LsMesh("LS_MainTurret_1/LS_MainGun_1");
        Mesh secTurretMesh = LsMesh("LS_SecondaryTurret_FR");
        Mesh secGunMesh = LsMesh("LS_SecondaryTurret_FR/LS_SecondaryGun_FR");
        Mesh shutterMesh = LsMesh("LS_CoreShutter_R");
        Bounds pod = podMesh.bounds;
        Bounds hullBounds = hullMesh.bounds;
        if (pod.min.x < -0.5f || pod.max.x > 8f || pod.max.y < 30f || hullBounds.max.z < 280f || hullBounds.min.z > -260f)
        {
            throw new InvalidOperationException($"[ACO] ACO_LandShip.fbx의 축이나 크기가 예상과 다릅니다(궤도 {pod.min} ~ {pod.max}, 선체 {hullBounds.min} ~ {hullBounds.max}). 프리팹은 만들지 않았습니다.");
        }
        var mats = new[] { LandShipMaterial(), LandShipGlowMaterial() };

        var root = new GameObject(LandShipPrefab);
        root.layer = LayerHitbox;
        Vector3 corePos = new Vector3(0f, 64f, LsCoreZ);
        BoxCollider coreCol = root.AddComponent<BoxCollider>();
        coreCol.center = corePos;
        coreCol.size = new Vector3(14f, 14f, 14f);
        coreCol.enabled = false;

        // 모델(충돌 없음)
        Transform model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        LsVisual(model, "LS_Hull", hullMesh, mats, Vector3.zero, Quaternion.identity);

        // 선체 충돌: 자체 SaccTarget이 무장 피해를 흡수한다(콜라이더가 루트 바로 아래면 GetComponentInParent로 코어가 맞는다)
        var hull = new GameObject("Hull");
        hull.layer = LayerLandShipHull;
        hull.transform.SetParent(root.transform, false);
        hull.AddComponent<MeshCollider>().sharedMesh = hullMesh;
        AddTargetAnimator(hull);
        SaccTarget armor = hull.AddUdonSharpComponent<SaccTarget>();
        armor.Health = 1000000f;
        armor.RespawnDelay = 0f;
        armor.DisableBulletHitEvent = true;

        // 궤도·스커트: 함수부터 번호(R1~R6 = 오른쪽, L1~L6 = 왼쪽). 같은 번호의 스커트가 그 궤도를 덮는다
        var pods = new List<Vector2>();
        foreach (float z in LsPodZ) { pods.Add(new Vector2(LsPodX, z)); }
        foreach (float z in LsInnerPodZ) { pods.Add(new Vector2(LsInnerPodX, z)); }
        pods.Sort((a, b) => b.y.CompareTo(a.y));
        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(root.transform, false);
        var skirts = new List<ACO_FortressPart>();
        var tracks = new List<ACO_FortressPart>();
        var vents = new List<ACO_FortressPart>();
        var ventTrack = new List<int>();
        foreach (float side in new[] { 1f, -1f })
        {
            string tagSide = side > 0f ? "R" : "L";
            Quaternion rot = Quaternion.Euler(0f, side > 0f ? 0f : 180f, 0f);
            for (int i = 0; i < pods.Count; i++)
            {
                float px = pods[i].x;
                float z = pods[i].y;
                bool inner = Mathf.Approximately(px, LsInnerPodX);
                float sx = inner ? LsInnerSkirtX : LsSkirtX;
                string id = tagSide + (i + 1);
                GameObject podVis = LsVisual(model, "LS_TrackPod_" + id, podMesh, mats, new Vector3(side * px, 0f, z), rot);
                GameObject skirtVis = LsVisual(model, "LS_Skirt_" + id, skirtMesh, mats, new Vector3(side * sx, 0f, z), rot);

                GameObject skirtBlast = LsBlast(parts, "SkirtBlast_" + id, new Vector3(side * (sx + 3f), 28f, z));
                ACO_FortressPart sp = AddLandShipPart(parts, "Skirt_" + id, "ARM", new Vector3(side * (sx + 2f), 26.5f, z), new Vector3(4.6f, 35f, 46f), 400f, 300, skirtVis, skirtBlast);
                skirtBlast.transform.SetParent(sp.transform, true);
                GameObject trackBlast = LsBlast(parts, "TrackBlast_" + id, new Vector3(side * (px + 4f), 14f, z));
                ACO_FortressPart tp = AddLandShipPart(parts, "Track_" + id, "TRK", new Vector3(side * (px + 3.5f), 17f, z), new Vector3(7.6f, 34f, 40f), 600f, 400, podVis, trackBlast);
                trackBlast.transform.SetParent(tp.transform, true);
                int trackIndex = tracks.Count;
                skirts.Add(sp);
                tracks.Add(tp);
                if (inner)
                {
                    MakeEndTrackWreck(tp, side);   // 앞뒤 끝 궤도: 배기구가 없어 부서져도 남고 연기를 낸다
                    continue;
                }
                // 가운데 궤도 뒤 선체 벽의 배기구(모델 y 16.6~21.8, z ±15.6)
                Vector3 ventCenter = new Vector3(side * 29.4f, 19.2f, z);
                GameObject ventFx = new GameObject("VentFx_" + id);
                ventFx.transform.SetParent(parts, false);
                ventFx.transform.localPosition = ventCenter;
                LsScorch(ventFx.transform, new Vector3(side * 0.9f, 0f, 0f), new Vector3(0.6f, 5.6f, 31f));
                CreateFortressSmoke(ventFx.transform, "Fire", new Vector3(side * 2f, 2f, 0f), 8f, 50f);
                ACO_FortressPart vp = AddLandShipPart(parts, "Vent_" + id, "VNT", ventCenter, new Vector3(3.2f, 6f, 32f), 400f, 500, null, ventFx);
                ventFx.transform.SetParent(vp.transform, true);
                vents.Add(vp);
                ventTrack.Add(trackIndex);
            }
        }

        // 주포(소형 레일건 프리팹 — 2연발, 넓고 오래 남는 폭발)·부포(Flak 프리팹)
        var weapons = new List<UdonSharp.UdonSharpBehaviour>();
        var mainGuns = new List<UdonSharp.UdonSharpBehaviour>();
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        var minis = new List<ACO_MiniRailgun>();
        for (int i = 0; i < LsMainTurrets.Length; i++)
        {
            Vector4 t = LsMainTurrets[i];
            GameObject gun = NewTurret(TurretMini, "MainGun_" + (i + 1), parts, new Vector3(t.x, t.y, t.z), Quaternion.Euler(0f, t.w, 0f), 1f);
            ACO_AAGunSite site = FitLandShipTurret(gun, mainTurretMesh, mainGunMesh, mats, LsMainGunPivot, 44.2f, new Vector3(0f, 4f, -1f), new Vector3(24f, 9f, 38f), -2f, 45f);
            ACO_MiniRailgun mini = gun.GetComponent<ACO_MiniRailgun>();
            mini.VolleyShots = 2;
            mini.VolleyGap = 0.8f;
            mini.ZoneRadius = LsMainGunZoneRadius;
            mini.MinRange = LsMainGunMinRange;
            mini.ZoneTime = LsMainGunZoneTime;
            mini.ZoneDamage = 15f;
            mini.BurstSpread = 6;
            mini.ZoneReburst = 1.2f;
            mini.FireInterval = 9f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mini);
            // 부서지면 사라지지 않고 고장 상태로 남는다(한 번 터지고 연기를 내다가 GunRepairDelay 뒤 되살아난다)
            GameObject broken = LsBlast(gun.transform, "Broken", new Vector3(0f, 6f, -2f));
            CreateFortressSmoke(broken.transform, "Smoke", Vector3.up * 3f, 5f, 25f);
            SetTurretDeath(site, false, broken, new Vector3(0f, 4f, -1f));
            weapons.Add(site);
            mainGuns.Add(site);
            minis.Add(mini);
        }
        LinkLandShipMiniFx(root.transform, minis);
        string[] secNames = { "FL", "FR", "RL", "RR" };
        for (int i = 0; i < LsSecondTurrets.Length; i++)
        {
            Vector4 t = LsSecondTurrets[i];
            GameObject gun = NewTurret(TurretFlak, "SecGun_" + secNames[i], parts, new Vector3(t.x, t.y, t.z), Quaternion.Euler(0f, t.w, 0f), 1f);
            ACO_AAGunSite sec = FitLandShipTurret(gun, secTurretMesh, secGunMesh, mats, LsMainGunPivot * LsSecondScale, 37f * LsSecondScale,
                new Vector3(0f, 1.8f, -0.5f), new Vector3(11f, 4.2f, 17f), -2f, 55f);
            // 부서지면 포신이 날아가고 한 번 터진다(남는 연기 없음)
            SetTurretDeath(sec, true, LsBlast(gun.transform, "Blast", new Vector3(0f, 3f, 0f)), new Vector3(0f, 2f, 0f));
            weapons.Add(sec);
            defense.Add(sec);
        }

        AddLandShipDeckDefense(parts, weapons, defense);

        // 코어: 셔터(경첩이 원점 — 로컬 Z로 젖힌다), 히트박스·락온 마커는 코어 단계에 켠다
        Transform coreRoot = new GameObject("Core").transform;
        coreRoot.SetParent(root.transform, false);
        GameObject coreMarker = NewLockMarker(coreRoot, "AAMTarget", LayerEnemyGround, 10f);
        coreMarker.transform.localPosition = corePos + Vector3.up * 2f;
        coreMarker.SetActive(false);
        var shutters = new[]
        {
            LsVisual(model, "LS_CoreShutter_R", shutterMesh, mats, LsCoreHinge, Quaternion.identity).transform,
            LsVisual(model, "LS_CoreShutter_L", shutterMesh, mats, new Vector3(-LsCoreHinge.x, LsCoreHinge.y, LsCoreHinge.z), Quaternion.Euler(0f, 180f, 0f)).transform,
        };

        // SAV 격침 애니메이션이 "Effects"를 켠다. 그 밖의 연출은 DeathEffects.
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        var deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        CreateFortressSmoke(deathFx.transform, "Fire_Core", corePos, 30f, 120f);
        CreateFortressSmoke(deathFx.transform, "Fire_Bow", new Vector3(0f, 55f, 120f), 30f, 90f);
        CreateFortressSmoke(deathFx.transform, "Fire_Stern", new Vector3(0f, 55f, -150f), 30f, 90f);
        deathFx.SetActive(false);

        AddTargetAnimator(root);
        SaccTarget core = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship = root.AddUdonSharpComponent<ACO_Warship>();
        ACO_LandShip landShip = root.AddUdonSharpComponent<ACO_LandShip>();
        core.Health = 3000f;
        core.ArmorStrength = 1f;
        core.RespawnDelay = 0f;
        core.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ship, landShip };

        var partList = new List<UdonSharp.UdonSharpBehaviour>(weapons);
        partList.AddRange(skirts);
        partList.AddRange(tracks);
        partList.AddRange(vents);
        ship.TargetHealth = core;
        ship.Parts = partList.ToArray();
        ship.TargetName = "Land Battleship";
        ship.MarkerLabel = "LBB";
        ship.ScoreValue = 5000;
        ship.HighValue = true;
        ship.PartHullDamage = 0f;
        ship.PartSplashScale = SeaPartSplash;
        ship.CoreSplashImmune = true;   // 코어는 직접 맞혀야만 — 예외 무기(LASM) 범위 피해로 열리자마자 녹지 않게
        ship.CruiseSpeed = 0f;
        ship.BobAngle = 0f;
        ship.ExternalPose = true;
        ship.PoseOwner = landShip;
        ship.ShowOnDeath = new[] { deathFx };
        ship.HideOnDeath = new GameObject[0];
        ship.DisableCollidersOnDeath = new Collider[] { coreCol };

        landShip.Warship = ship;
        landShip.TargetHealth = core;
        landShip.Skirts = skirts.ToArray();
        landShip.Tracks = tracks.ToArray();
        landShip.Vents = vents.ToArray();
        landShip.VentTrack = ventTrack.ToArray();
        landShip.RepairGuns = mainGuns.ToArray();
        landShip.AirDefense = defense.ToArray();
        landShip.CoreHitbox = coreCol;
        landShip.CoreMarkers = new[] { coreMarker };
        landShip.Shutters = shutters;
        landShip.Blast = CreateFortressBlast(root.transform);
        AddLandShipMoveFx(root.transform, landShip);

        return SaveAndCleanup(root, path);
    }

    /// 갑판 방공(네 셀 VLS, 긴 VLS, CIWS, Flak)을 표대로 놓는다. 공용 포대 프리팹(요새·랜스 포대와 같은 값)이고 받침 기준 조준 —
    /// 함선이 기울어도 갑판 위에서 돈다. 긴 VLS는 수리 목록(defense)에 넣지 않는다.
    private static void AddLandShipDeckDefense(Transform parts, List<UdonSharp.UdonSharpBehaviour> weapons, List<UdonSharp.UdonSharpBehaviour> defense)
    {
        for (int i = 0; i < LsVlsSites.Length; i++)
        {
            GameObject vls = NewTurret(TurretVls, "VLS_" + i, parts, LsVlsSites[i], Quaternion.identity, LsVlsScale);
            weapons.Add(vls.GetComponent<ACO_SAMSite>());
            defense.Add(vls.GetComponent<ACO_SAMSite>());
        }
        for (int i = 0; i < LsLongVlsSites.Length; i++)
        {
            GameObject vls = NewTurret(TurretVls16, "VLS16_" + i, parts, LsLongVlsSites[i], Quaternion.identity, LsLongVlsScale);
            weapons.Add(vls.GetComponent<ACO_SAMSite>());
        }
        for (int i = 0; i < LsCiwsSites.Length; i++)
        {
            Vector3 p = LsCiwsSites[i];
            float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;   // 바깥을 보고 쉰다
            ACO_AAGunSite ciws = LsDeckGun(TurretCiws, "CIWS_" + i, parts, p, yaw);
            weapons.Add(ciws);
            defense.Add(ciws);
        }
        for (int i = 0; i < LsFlakSites.Length; i++)
        {
            Vector3 p = LsFlakSites[i];
            ACO_AAGunSite flak = LsDeckGun(TurretFlak, "Flak_" + i, parts, p, p.x > 0f ? 90f : -90f);
            weapons.Add(flak);
            defense.Add(flak);
        }
    }

    private static bool IsDeckDefenseName(string name)
    {
        return name.StartsWith("VLS_") || name.StartsWith("VLS16_") || name.StartsWith("CIWS_") || name.StartsWith("Flak_");
    }

    /// Setup Check: 기존 NPC_LandShip 프리팹의 갑판 방공을 표에 맞춘다(다시 만들지 않는다 — 손으로 고친 부분은 그대로).
    /// Parts 아래 VLS_/VLS16_/CIWS_/Flak_ 포대만 지우고 표대로 다시 놓은 뒤 부위·수리 목록을 다시 묶는다. 주포·부포·스커트·궤도는 건드리지 않는다.
    public static void PlaceLandShipTurrets()
    {
        string path = PathOf(LandShipPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, root =>
        {
            ACO_LandShip landShip = root.GetComponent<ACO_LandShip>();
            Transform parts = root.transform.Find("Parts");
            if (landShip == null || landShip.Warship == null || parts == null)
            {
                throw new System.InvalidOperationException("[ACO] NPC_LandShip에 ACO_LandShip·Warship·Parts가 없습니다. Create Prefabs [40]으로 다시 만드세요.");
            }
            int removed = 0;
            for (int i = parts.childCount - 1; i >= 0; i--)
            {
                Transform child = parts.GetChild(i);
                if (!IsDeckDefenseName(child.name)) { continue; }
                Object.DestroyImmediate(child.gameObject);
                removed++;
            }
            var weapons = new List<UdonSharp.UdonSharpBehaviour>();
            var defense = new List<UdonSharp.UdonSharpBehaviour>();
            AddLandShipDeckDefense(parts, weapons, defense);
            // 지운 포대는 목록에서 빠지고(null), 새 포대와 수리 목록은 LinkLandShipWeapons가 다시 묶는다
            landShip.AirDefense = null;
            LinkLandShipWeapons(landShip);
            EditorUtility.SetDirty(landShip);
            EditorUtility.SetDirty(landShip.Warship);
            Debug.Log($"[ACO] 육상전함 갑판 방공: 예전 포대 {removed}개를 지우고 {weapons.Count}개를 놓았습니다(수리 대상 {landShip.AirDefense.Length}곳, 부포 포함).");
        });
    }

    /// Setup Check: 선체 최대 기울기를 8→15도(육각 지형 비탈과 같다)로 넓힌다(항로 확인은 생성기의 콘보이 경사 MaxRouteGrade를 쓴다).
    public static void TuneLandShipRoute()
    {
        string path = PathOf(LandShipPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, root =>
        {
            ACO_LandShip landShip = root.GetComponent<ACO_LandShip>();
            if (landShip == null) { return; }
            landShip.MaxPitch = 15f;
            EditorUtility.SetDirty(landShip);
            Debug.Log("[ACO] 육상전함: 최대 기울기를 15도로 넓혔습니다.");
        });
    }

    /// Bake: 프리팹에 직접 놓은 공용 포대(VLS·CIWS 등)를 부위 목록에 더한다(이미 있으면 그대로).
    public static bool LinkLandShipWeapons(ACO_LandShip landShip)
    {
        ACO_Warship ship = landShip.Warship;
        if (ship == null) { return false; }
        var list = new List<UdonSharp.UdonSharpBehaviour>();
        if (ship.Parts != null) { foreach (UdonSharp.UdonSharpBehaviour p in ship.Parts) { if (p != null) { list.Add(p); } } }
        bool changed = ship.Parts == null || list.Count != ship.Parts.Length;
        foreach (ACO_AAGunSite aa in landShip.GetComponentsInChildren<ACO_AAGunSite>(true))
        {
            if (!list.Contains(aa)) { list.Add(aa); changed = true; }
        }
        foreach (ACO_SAMSite sam in landShip.GetComponentsInChildren<ACO_SAMSite>(true))
        {
            if (!list.Contains(sam)) { list.Add(sam); changed = true; }
        }
        if (changed) { ship.Parts = list.ToArray(); }
        // 수리 목록(방공·부포): 부위 중 주포가 아닌 포대
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        foreach (UdonSharp.UdonSharpBehaviour p in list)
        {
            if (!(p is ACO_AAGunSite) && !(p is ACO_SAMSite)) { continue; }
            if (landShip.RepairGuns != null && System.Array.IndexOf(landShip.RepairGuns, p) >= 0) { continue; }
            if (IsLongVls(p)) { continue; }   // 긴 VLS는 한 번 부수면 끝
            defense.Add(p);
        }
        if (landShip.AirDefense == null || landShip.AirDefense.Length != defense.Count)
        {
            landShip.AirDefense = defense.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(landShip);
            EditorUtility.SetDirty(landShip);
            changed = true;
        }
        return changed;
    }

    /// 셀이 4개보다 많은 발사기(긴 VLS)인가 — 수리 목록에서 뺀다.
    private static bool IsLongVls(UdonSharp.UdonSharpBehaviour p)
    {
        var sam = p as ACO_SAMSite;
        return sam != null && sam.MissileLauncher != null && sam.MissileLauncher.LaunchPoints != null && sam.MissileLauncher.LaunchPoints.Length > 4;
    }
}
