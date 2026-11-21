using System.Collections.Generic;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// 기체 형식 스위치 설치(6-343): ACO E형 프리팹(FA-18E Main_ACO) 안에 F형 부품을 더하고 ACO_AirframeVariant를 단다. 여러 번 눌러도 같은 결과.
///  - F 부품 = E 부품(동체 · 캐노피 · 조종석 · 주 착륙장치 · 앞 착륙장치 · 좌석 — 모두 자식 · 스크립트 참조 없음) 옆의 복사본 "X.F"(F 메시 · FA-18F 재질, 처음엔 꺼짐).
///    동체 · 캐노피는 E처럼 가늘게 · 볼트를 박은 F 메시(Generated/Canopy/Main_ACOThin · Canopy_ACOThin — ACO_CanopyThin.ApplyF가 만든다). 스킨 메시는 같은 뼈대.
///  - 뒷자리 좌석 모델(F 원본의 PasengerSeat)을 꺼 둔 채 넣고, 승객석(PassengerSeat)을 F 뒷자리 위치로. 승객석 오브젝트는 기체 애니메이터(Respawn 상태)가
///    매 프레임 켜 두므로 켜고 끄지 않고, 상호작용 콜라이더만 꺼 둔다(형식이 F일 때만 켠다).
///  - 기체 확장(ACO_VehicleUpgrades)의 Airframe에 잇는다(폭발 때 뒷자리 승객을 브리핑실로). 게임 진행 · Flow 연결은 Bake.
public static class ACO_AirframeInstaller
{
    public const string EPath = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
    private const string FSourcePath = "Assets/Pit-bool/FA-18/prefab/FA-18F Main.prefab";
    private const string EBodyMat = "Assets/Pit-bool/FA-18/material/E/FA-18E.mat";
    private const string FBodyMat = "Assets/Pit-bool/FA-18/material/F/FA-18F.mat";
    private const string ThinDir = "Assets/ACO_Addon/Generated/Canopy";
    // 모델 루트(E 프리팹 = 기체 아래 FA-18F, 데칼 스튜디오 미리보기 = FA-18F(preview)) 기준 경로
    private static readonly string[] Parts = { "Main.E", "Canopy.E", "cockpit.E", "MainGear", "Nosegear", "Armature/Seat" };

    [MenuItem("ACO/Airframe Variant/Install In E Prefab")]
    public static void Install()
    {
        GameObject fSrc = AssetDatabase.LoadAssetAtPath<GameObject>(FSourcePath);
        if (fSrc == null) { Debug.LogError($"[ACO] 기체 형식: F 원본이 없습니다: {FSourcePath}"); return; }
        // 가늘게 만든 F 메시를 매번 다시 만든다(캐노피 프레임 값을 고친 뒤 다시 설치하면 F도 따라가게)
        {
            GameObject tmp = Object.Instantiate(fSrc);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            try { ACO_CanopyThin.ApplyF(tmp); } finally { Object.DestroyImmediate(tmp); }
        }
        Mesh mainF = AssetDatabase.LoadAssetAtPath<Mesh>($"{ThinDir}/Main_ACOThin.asset");
        Mesh canopyF = AssetDatabase.LoadAssetAtPath<Mesh>($"{ThinDir}/Canopy_ACOThin.asset");
        Material eMat = AssetDatabase.LoadAssetAtPath<Material>(EBodyMat), fMat = AssetDatabase.LoadAssetAtPath<Material>(FBodyMat);

        GameObject root = PrefabUtility.LoadPrefabContents(EPath);
        GameObject f = Object.Instantiate(fSrc);
        f.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var entity = root.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            var fEntity = f.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null || fEntity == null) { Debug.LogError("[ACO] 기체 형식: SaccEntity를 찾지 못했습니다."); return; }
            Transform eRoot = entity.transform, fRoot = fEntity.transform;
            var eObjs = new List<GameObject>();
            var fObjs = new List<GameObject>();
            CopyFParts(eRoot.Find("FA-18F"), fRoot.Find("FA-18F"), mainF, canopyF, eMat, fMat, eObjs, fObjs, true);
            // 승객석: F 뒷자리 위치. 오브젝트는 켜 둔다(기체 애니메이터의 Respawn 상태가 어차피 켠다) — 상호작용 콜라이더만 꺼 두고 형식이 F일 때 켠다
            Transform ps = eRoot.Find("PassengerSeat"), psF = fRoot.Find("PassengerSeat");
            Collider psCol = null;
            if (ps != null && psF != null)
            {
                ps.localPosition = psF.localPosition; ps.localRotation = psF.localRotation; ps.gameObject.SetActive(true);
                psCol = ps.GetComponent<Collider>();
                if (psCol != null) { psCol.enabled = false; }
            }
            // 스위치
            Transform holder = eRoot.Find("ACO_Airframe");
            if (holder == null) { holder = new GameObject("ACO_Airframe").transform; holder.SetParent(eRoot, false); }
            ACO_AirframeVariant v = holder.GetComponent<ACO_AirframeVariant>();
            if (v == null) { v = holder.gameObject.AddUdonSharpComponent<ACO_AirframeVariant>(); }
            v.Entity = entity;
            // 뒷좌석 화면(6-346): F형 부품 묶음에 넣는다(E형이면 통째로 꺼짐). 앞좌석 지도는 조종석에서만 켠다
            GameObject rearScreens = ACO_HUDBuilder.BuildRearCockpit(eRoot, entity, root.GetComponentInChildren<SaccFlightAndVehicles.SaccAirVehicle>(true), v);
            rearScreens.SetActive(false);
            fObjs.Add(rearScreens);
            foreach (ACO_CockpitMap m in root.GetComponentsInChildren<ACO_CockpitMap>(true))
            {
                if (!m.transform.IsChildOf(rearScreens.transform)) { m.SeatFilter = ACO_CockpitMap.SEAT_PILOT; }
            }
            v.EObjects = eObjs.ToArray();
            v.FObjects = fObjs.ToArray();
            v.PassengerSeat = ps != null ? ps.gameObject : null;
            v.PassengerCollider = psCol;
            foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true)) { up.Airframe = v; }
            PrefabUtility.SaveAsPrefabAsset(root, EPath);
            Debug.Log($"[ACO] 기체 형식 스위치를 E형 프리팹에 넣었습니다: F 부품 {fObjs.Count}개(동체 · 캐노피 · 조종석 · 착륙장치 · 좌석 · 뒷자리 좌석 · 뒷좌석 화면), 승객석 F 위치. Bake 하세요.");
        }
        finally
        {
            Object.DestroyImmediate(f);
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
    }

    /// 씬의 F형 변형 기체(예전 3 · 4번기)를 E형 프리팹으로 되돌린다(계층 이름으로 맞춤 — 참조 · 덮어쓰기 그대로). 형식은 스위치로 고른다.
    [MenuItem("ACO/Airframe Variant/Revert Scene F Variants To E")]
    public static int RevertSceneVariants()
    {
        GameObject eAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EPath);
        var settings = new PrefabReplacingSettings { logInfo = false, objectMatchMode = ObjectMatchMode.ByHierarchy, prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides };
        int n = 0;
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) { continue; }
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) != ACO_FVariantBuilder.VariantPath) { continue; }
            PrefabUtility.ReplacePrefabAssetOfPrefabInstance(go, eAsset, settings, InteractionMode.UserAction);
            go.name = go.name.Replace("FA-18F", "FA-18E");
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 기체 형식: 씬의 F형 변형 {n}대를 E형 프리팹으로 되돌렸습니다(형식은 로스터 스위치로).");
        return n;
    }

    /// 스킨 메시의 컬링 범위(뿌리 뼈 기준)를 지금 자세로 구운 정점 전체 + 원래 범위를 감싸게 넓힌다(여유 5cm).
    private static void FitSkinnedBounds(SkinnedMeshRenderer smr)
    {
        if (smr.sharedMesh == null || smr.rootBone == null) { return; }
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        Vector3[] v = baked.vertices;
        Object.DestroyImmediate(baked);
        if (v.Length == 0) { return; }
        Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        Transform rb = smr.rootBone;
        Bounds b = smr.localBounds;
        foreach (Vector3 p in v) { b.Encapsulate(rb.InverseTransformPoint(toWorld.MultiplyPoint3x4(p))); }
        b.Expand(0.1f);
        smr.localBounds = b;
    }

    /// E 모델 루트(eModel) 안의 E 부품 옆에 F 복사본 "X.F"(F 메시 · F 재질, 꺼 둠)와 뒷자리 좌석 모델을 만든다(이미 있으면 다시 맞춘다).
    /// fModel = F 원본 모델 루트. eObjs · fObjs에 켜고 끌 묶음을 넣는다. fitBounds = 컬링 범위를 실제 메시에 맞춘다(뒷좌석에서 보일 때).
    private static void CopyFParts(Transform eModel, Transform fModel, Mesh mainF, Mesh canopyF, Material eMat, Material fMat, List<GameObject> eObjs, List<GameObject> fObjs, bool fitBounds)
    {
        if (eModel == null || fModel == null) { Debug.LogError("[ACO] 기체 형식: 모델 루트를 찾지 못했습니다."); return; }
        foreach (string path in Parts)
        {
            Transform te = eModel.Find(path), tf = fModel.Find(path);
            if (te == null || tf == null) { Debug.LogWarning($"[ACO] 기체 형식: 부품을 찾지 못했습니다: {eModel.name}/{path}"); continue; }
            string fName = te.name.Replace(".E", "") + ".F";
            Transform copy = te.parent.Find(fName);
            if (copy == null)
            {
                copy = Object.Instantiate(te.gameObject, te.parent, false).transform;
                copy.name = fName;
                copy.SetSiblingIndex(te.GetSiblingIndex() + 1);
                Undo.RegisterCreatedObjectUndo(copy.gameObject, "ACO airframe F part");
            }
            copy.localPosition = te.localPosition; copy.localRotation = te.localRotation; copy.localScale = te.localScale;
            Mesh fm = te.name == "Main.E" ? mainF : te.name == "Canopy.E" ? canopyF : MeshOf(tf);
            Renderer rc = copy.GetComponent<Renderer>();
            if (rc is SkinnedMeshRenderer smr)
            {
                smr.sharedMesh = fm;
                // 컬링 범위: F 원본은 동체 · 캐노피 · 조종석의 범위가 앞좌석까지만이라 뒷좌석에서 고개를 돌리면 통째로 사라졌다 — 실제 메시에 맞춰 넓힌다
                if (fitBounds && (te.name == "Main.E" || te.name == "Canopy.E" || te.name == "cockpit.E")) { FitSkinnedBounds(smr); }
            }
            else if (copy.TryGetComponent(out MeshFilter mf)) { mf.sharedMesh = fm; }
            // 재질: E 재질 칸을 그대로 두고 몸체만 F로(동체는 볼트 칸 포함 — 메시 submesh 수에 맞춘다)
            Material[] mats = te.GetComponent<Renderer>().sharedMaterials;
            int want = fm != null ? Mathf.Max(fm.subMeshCount, 1) : mats.Length;
            var nm = new Material[want];
            for (int i = 0; i < want; i++) { Material m = i < mats.Length ? mats[i] : mats[mats.Length - 1]; nm[i] = m == eMat ? fMat : m; }
            rc.sharedMaterials = nm;
            // 복사본에 딸려 온 충돌체는 지운다(E와 겹친다 — 데칼 건 · 물리는 E 쪽 그대로)
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(c); }
            copy.gameObject.SetActive(false);
            eObjs.Add(te.gameObject);
            fObjs.Add(copy.gameObject);
        }
        // 뒷자리 좌석 모델
        Transform rearF = fModel.Find("Armature/PasengerSeat");
        Transform armE = eModel.Find("Armature");
        if (rearF != null && armE != null)
        {
            Transform rear = armE.Find("PasengerSeat");
            if (rear == null)
            {
                rear = Object.Instantiate(rearF.gameObject, armE, false).transform;
                rear.name = "PasengerSeat";
                rear.gameObject.hideFlags = HideFlags.None;
                Undo.RegisterCreatedObjectUndo(rear.gameObject, "ACO airframe rear seat");
            }
            rear.localPosition = rearF.localPosition; rear.localRotation = rearF.localRotation; rear.localScale = rearF.localScale;
            Renderer rr = rear.GetComponent<Renderer>();
            if (rr != null) { Material[] m = rr.sharedMaterials; for (int i = 0; i < m.Length; i++) { if (m[i] == eMat) { m[i] = fMat; } } rr.sharedMaterials = m; }
            rear.gameObject.SetActive(false);
            fObjs.Add(rear.gameObject);
        }
    }

    /// 데칼 스튜디오 미리보기 기체(씬 — SampleRoot 아래 Main.E가 있는 모델)에도 F 부품을 넣고 스튜디오의 E/F 묶음에 잇는다(6-347). 여러 번 눌러도 같은 결과.
    [MenuItem("ACO/Airframe Variant/Install In Decal Studio Preview")]
    public static void InstallPreview()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null) { Debug.LogError("[ACO] 기체 형식: 데칼 스튜디오 · SampleRoot가 없습니다."); return; }
        Transform model = null;
        foreach (Transform t in studio.SampleRoot) { if (t.Find("Main.E") != null) { model = t; break; } }
        GameObject fSrc = AssetDatabase.LoadAssetAtPath<GameObject>(FSourcePath);
        if (model == null || fSrc == null) { Debug.LogError("[ACO] 기체 형식: 미리보기 모델(Main.E) 또는 F 원본이 없습니다."); return; }
        Mesh mainF = AssetDatabase.LoadAssetAtPath<Mesh>($"{ThinDir}/Main_ACOThin.asset");
        Mesh canopyF = AssetDatabase.LoadAssetAtPath<Mesh>($"{ThinDir}/Canopy_ACOThin.asset");
        Material eMat = AssetDatabase.LoadAssetAtPath<Material>(EBodyMat), fMat = AssetDatabase.LoadAssetAtPath<Material>(FBodyMat);
        GameObject f = Object.Instantiate(fSrc);
        f.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var fEntity = f.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            Transform fModel = fEntity != null ? fEntity.transform.Find("FA-18F") : null;
            var eObjs = new List<GameObject>();
            var fObjs = new List<GameObject>();
            Undo.RecordObject(studio, "ACO decal studio airframe");
            CopyFParts(model, fModel, mainF, canopyF, eMat, fMat, eObjs, fObjs, false);
            MatchPreviewCanopy(model);
            // 데칼 건이 맞힐 충돌체: 미리보기 E 좌석처럼 F 좌석 · 뒷자리 좌석(일반 메시)에도 붙인다(Setup Check 데칼 샘플 콜라이더)
            foreach (GameObject g in fObjs)
            {
                foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) { continue; }
                    Undo.AddComponent<MeshCollider>(mf.gameObject).sharedMesh = mf.sharedMesh;
                }
            }
            studio.PreviewEObjects = eObjs.ToArray();
            studio.PreviewFObjects = fObjs.ToArray();
            EditorUtility.SetDirty(studio);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);
            Debug.Log($"[ACO] 데칼 스튜디오 미리보기에 F 부품 {fObjs.Count}개를 넣었습니다(처음엔 E). 씬을 저장하세요.");
        }
        finally { Object.DestroyImmediate(f); }
    }

    /// 미리보기 F 캐노피(뼈 canopy)를 E 캐노피(뼈 canopy.E)와 같은 만큼 연다 — 열림 비율을 FA-18F_CanopyOpen 클립의 두 뼈 범위로 옮긴다
    /// (E: x −89.98 → −53.744, F: x 89.98 → 50.545 · y −180). 실기체는 애니메이터가 두 뼈를 함께 움직인다.
    private static void MatchPreviewCanopy(Transform model)
    {
        Transform ce = null, cf = null;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "canopy.E") { ce = t; }
            else if (t.name == "canopy") { cf = t; }
        }
        if (ce == null || cf == null) { return; }
        float ex = Mathf.DeltaAngle(0f, ce.localEulerAngles.x);
        float open = Mathf.Clamp01(Mathf.InverseLerp(-89.98f, -53.744f, ex));
        Undo.RecordObject(cf, "ACO preview canopy");
        cf.localRotation = Quaternion.Euler(Mathf.Lerp(89.98f, 50.545f, open), -180f, 0f);
        Debug.Log($"[ACO] 데칼 스튜디오 미리보기: F 캐노피를 E와 같이 {Mathf.RoundToInt(open * 100f)}% 열었습니다.");
    }

    private static Mesh MeshOf(Transform t)
    {
        if (t.TryGetComponent(out SkinnedMeshRenderer s)) { return s.sharedMesh; }
        return t.TryGetComponent(out MeshFilter m) ? m.sharedMesh : null;
    }
}
