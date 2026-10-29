using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SaccFlightAndVehicles;
using ACO;

/// 플레이어 기체 셋업(ACO → Player Vehicle → Set Up Selected Vehicles — 일반 미사일 교체·강화 확장·좌석 재조정·특수무장 슬롯·무장 카탈로그·키보드를 한 번에).
/// SAV 기체의 DFUNC_AAM을 ACO_DFUNC_Missile로 바꾸고, HUD에 슬롯 장전 막대를 만든다. 씬의 기체(프리팹 인스턴스)만 고친다 —
/// SAV 폴더의 프리팹 에셋은 건드리지 않는다(인스턴스 오버라이드로 남는다).
///
/// 교체: DFUNC_AAM과 같은 오브젝트에 ACO_DFUNC_Missile을 붙이고 참조(SAVControl, HUD, 조준 기준, 소리, 표적 표시, 잔탄 텍스트,
/// 애니메이터)를 옮긴다. 발사 지점은 기존 것 + 기체 좌우 대칭 위치에 하나 더(슬롯을 번갈아 쓴다). SaccEntity의 다이얼 배열에서
/// DFUNC_AAM 자리를 새 기능으로 바꾸고 DFUNC_AAM은 꺼 둔다(지우지 않으므로 되돌릴 수 있다).
///
/// HUD 막대: SAV HUD의 미사일 잔탄 텍스트(SF-2 기준 HudCanvas/AAMs — 아래로 AGM/폭탄/로켓 텍스트가 같은 간격으로 쌓인다) 오른쪽에
/// 슬롯 수만큼 막대를 늘어놓는다. 세로(기본): 세로 막대를 가로로 나란히, 아래에서 위로 찬다. 가로: 가로 막대를 세로로 쌓고 왼쪽부터 찬다. 같은 캔버스·같은 HUDText 머티리얼·같은 색을 써서 HUD와 똑같이 그려진다
/// (가산 블렌딩·ZTest Off·HUD 스텐실). HUDText 셰이더는 텍스처가 없는 Image도 색 그대로 그린다.
public static class ACO_PlayerMissileSetup
{
    private const string MissilePrefabPath = "Assets/ACO_Addon/Prefabs/ACO_Missile_AAM.prefab";
    private const string BarsName = "ACO_MissileSlotBars";

    // 막대 치수(캔버스 단위). SF-2 HUD의 AAMs 텍스트는 155.8 × 56.3 에 스케일 1.74 → 약 271 × 98.
    // 막대 길이는 텍스트 높이(98)보다 조금 짧게 해서 아래 AGM 줄과 겹치지 않게 한다.
    private const float BarLength = 84f;
    private const float BarThickness = 16f;
    private const float BarGap = 10f;
    private const float TextGap = 12f;
    // 가산 블렌딩이라 알파가 곧 밝기다.
    private const float BackAlpha = 0.18f;
    private const float FillAlpha = 0.5f;

    /// 고른 SAV 기체를 한 번에 ACO 플레이어 기체로 만든다(여러 번 해도 된다 — 이미 된 단계는 건너뛴다):
    ///  1) DFUNC_AAM → ACO_DFUNC_Missile(+ HUD 슬롯 막대)  2) 강화 확장  3) 좌석 재조정·더블탭 내리기
    ///  4) 특수무장 슬롯(오른쪽 다이얼 Element 2, 데스크톱 키 3)  5) 무장 카탈로그(Assets/ACO_Addon/SpecialWeapons — 없으면 예제 10종을 만든다)의
    ///     모듈 중 이 기체에 없는 것(4AAM·UGB·RKT 포함, 예전 SAV 폭탄/로켓 모듈은 ACO 모듈로 바꾼다)  6) 키보드 단축키 정리
    /// 끝나면 격납고 목록을 카탈로그에 맞추고 Bake.
    [MenuItem("ACO/Player Vehicle/Set Up Selected Vehicles")]
    public static void SetupSelectedVehicles()
    {
        List<SaccEntity> entities = SelectedEntities();
        if (entities.Count == 0)
        {
            Debug.LogWarning("[ACO] Hierarchy에서 SAV 기체(또는 그 안의 오브젝트)를 선택하세요.");
            return;
        }
        ACO_SpecialWeaponCatalog.EnsureExamples();
        int changed = 0;
        foreach (SaccEntity entity in entities) { if (SetupVehicle(entity)) { changed++; } }
        ACO_SpecialWeaponCatalog.SyncLoadouts();
        ACO_SceneBaker.BakeNeighbors();
        Debug.Log($"[ACO] 기체 셋업: {entities.Count}대 중 {changed}대를 바꿨습니다(나머지는 이미 셋업됨). 씬을 저장하세요.");
    }

    /// 기체 하나를 셋업한다(위 1~6). 바꾼 게 있으면 true. 셋업 검사의 [고치기]도 이걸 부른다.
    public static bool SetupVehicle(SaccEntity entity)
    {
        bool changed = false;
        // 1) 일반 미사일
        ACO_DFUNC_Missile primary = FindPrimaryMissile(entity);
        if (primary == null)
        {
            foreach (DFUNC_AAM aam in entity.GetComponentsInChildren<DFUNC_AAM>(true))
            {
                if (aam.GetComponent<ACO_DFUNC_Missile>() != null) { continue; }
                ACO_DFUNC_Missile weapon = Replace(entity, aam);
                BuildSlotBars(weapon);
                if (primary == null) { primary = weapon; }
                changed = true;
            }
        }
        if (primary == null)
        {
            Debug.LogWarning($"[ACO] '{entity.name}': 일반 미사일(DFUNC_AAM 또는 ACO_DFUNC_Missile)이 없어 셋업하지 못했습니다 — 소리·HUD·발사 지점을 거기서 가져온다.", entity);
            return false;
        }
        // 2) 3)
        if (EnsureUpgradeExtension(entity)) { changed = true; }
        if (EnsureSeatRecenter(entity)) { changed = true; }
        // 4) 특수무장 슬롯
        ACO_DFUNC_Special special = entity.GetComponentInChildren<ACO_DFUNC_Special>(true);
        Text fallbackAmmo = null;
        if (special == null)
        {
            if (entity.Dial_Functions_R == null || entity.Dial_Functions_R.Length <= SpecialDialIndex)
            {
                Debug.LogWarning($"[ACO] '{entity.name}'의 오른쪽 다이얼(Dial_Functions_R)에 Element {SpecialDialIndex} 칸이 없어 특수무장 슬롯을 만들지 못했습니다.", entity);
            }
            else
            {
                special = AddSpecialSlot(entity, primary, out fallbackAmmo);
                changed = true;
            }
        }
        // 5) 무장 모듈
        if (special != null && ACO_SpecialWeaponCatalog.InstallVehicle(entity, special, primary, fallbackAmmo) > 0) { changed = true; }
        // 6) 키보드
        if (FixKeyboardBindings(entity) > 0) { changed = true; }
        return changed;
    }

    /// 기체에 강화 확장(ACO_VehicleUpgrades, 자식 오브젝트 "ACO_Upgrades")을 만들고 ExtensionUdonBehaviours에 넣는다. 이미 있으면 false.
    public static bool EnsureUpgradeExtension(SaccEntity entity)
    {
        if (entity.GetComponentInChildren<ACO_VehicleUpgrades>(true) != null) { return false; }
        GameObject go = new GameObject("ACO_Upgrades");
        Undo.RegisterCreatedObjectUndo(go, "ACO upgrade extension");
        go.transform.SetParent(entity.transform, false);
        ACO_VehicleUpgrades up = UdonSharpUndo.AddComponent<ACO_VehicleUpgrades>(go);
        RegisterExtension(entity, up);
        Debug.Log($"[ACO] '{entity.name}'에 강화 확장을 넣었습니다.", up);
        return true;
    }

    /// 좌석 재조정 확장(점프 한 번 = 눈높이 재조정)을 넣고 SAV 더블탭 내리기(DoubleTapToExit)를 켠다. 바꿨으면 true.
    public static bool EnsureSeatRecenter(SaccEntity entity)
    {
        bool changed = false;
        ACO_SeatRecenter rc = entity.GetComponentInChildren<ACO_SeatRecenter>(true);
        if (rc == null)
        {
            GameObject go = new GameObject("ACO_SeatRecenter");
            Undo.RegisterCreatedObjectUndo(go, "ACO seat recenter");
            go.transform.SetParent(entity.transform, false);
            rc = UdonSharpUndo.AddComponent<ACO_SeatRecenter>(go);
            changed = true;
        }
        if (RegisterExtension(entity, rc)) { changed = true; }
        if (!entity.DoubleTapToExit)
        {
            Undo.RecordObject(entity, "ACO double tap exit");
            entity.DoubleTapToExit = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
            EditorUtility.SetDirty(entity);
            changed = true;
        }
        if (changed) { Debug.Log($"[ACO] '{entity.name}'에 좌석 재조정(점프 한 번)과 더블탭 내리기를 넣었습니다.", rc); }
        return changed;
    }

    /// SaccEntity.ExtensionUdonBehaviours에 없으면 끝에 더한다. 더했으면 true.
    public static bool RegisterExtension(SaccEntity entity, UdonSharpBehaviour ext)
    {
        UdonSharpBehaviour[] list = entity.ExtensionUdonBehaviours ?? new UdonSharpBehaviour[0];
        foreach (UdonSharpBehaviour e in list) { if (e == ext) { return false; } }
        Undo.RecordObject(entity, "ACO register extension");
        var grown = new List<UdonSharpBehaviour>(list) { ext };
        entity.ExtensionUdonBehaviours = grown.ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
        EditorUtility.SetDirty(entity);
        return true;
    }

    [MenuItem("ACO/Player Vehicle/Rebuild HUD Slot Bars on Selected Vehicles")]
    public static void RebuildBarsOnSelection()
    {
        int count = 0;
        foreach (SaccEntity entity in SelectedEntities())
        {
            foreach (ACO_DFUNC_Missile weapon in entity.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (BuildSlotBars(weapon)) { count++; }
            }
        }
        Debug.Log($"[ACO] HUD 슬롯 막대 {count}세트를 만들었습니다.");
    }

    // ───────────── 특수무장 슬롯 ─────────────

    // 특수무장 슬롯 자리: 오른쪽 다이얼(Dial_Functions_R) Element 2. 데스크톱 키는 숫자 3(SAV_KeyboardControls).
    private const int SpecialDialIndex = 2;
    private const KeyCode SpecialKey = KeyCode.Alpha3;

    /// UGB: 무유도 폭탄. 누르고 있으면 0.3초마다 투하. 슬롯 2(좌우) / 총 24발 / 쏜 슬롯마다 4초 뒤 재장전. 범위 35m.
    public static void ApplyBombPreset(ACO_DFUNC_Missile m)
    {
        m.UpgradeFamily = ACO_UpgradeFamily.SpecialBomb;
        m.TargetLayers = 0;
        m.MaxLockTargets = 1;
        m.AllowUnguidedFire = true;
        m.HoldToFire = true;
        m.FireInterval = 0.3f;
        m.SlotCount = 2;
        m.MaxAmmo = 24;
        m.SlotReloadTime = 4f;
        m.Damage = 150f;
        m.SplashRadius = 35f;
        m.MinSplashFraction = 0.25f;
        m.MaxSpeed = 600f;
        m.Acceleration = 0f;
        m.InheritVelocity = true;
        m.AllowFiringWhenGrounded = false;
        m.NumPreInstantiated = 4;
    }

    /// RKT: 무유도 로켓. 누르고 있으면 0.08초마다 연사. 슬롯 8 / 총 72발 / 쏜 슬롯마다 3초 뒤 재장전. 범위 12m.
    public static void ApplyRocketPreset(ACO_DFUNC_Missile m)
    {
        m.UpgradeFamily = ACO_UpgradeFamily.SpecialBomb;
        m.TargetLayers = 0;
        m.MaxLockTargets = 1;
        m.AllowUnguidedFire = true;
        m.HoldToFire = true;
        m.FireInterval = 0.08f;
        m.SlotCount = 8;
        m.MaxAmmo = 72;
        m.SlotReloadTime = 3f;
        m.Damage = 45f;
        m.SplashRadius = 12f;
        m.MinSplashFraction = 0.3f;
        m.MaxSpeed = 450f;
        m.Acceleration = 600f;
        m.InheritVelocity = true;
        m.AllowFiringWhenGrounded = false;
        m.NumPreInstantiated = 8;
    }

    public static T[] RemoveAt<T>(T[] array, int index)
    {
        var list = new List<T>(array);
        list.RemoveAt(index);
        return list.ToArray();
    }

    public static bool ClearFrom(UdonSharpBehaviour[] dials, UdonSharpBehaviour target)
    {
        if (dials == null) { return false; }
        bool found = false;
        for (int i = 0; i < dials.Length; i++)
        {
            if (dials[i] == target) { dials[i] = null; found = true; }
        }
        return found;
    }

    private static T[] Append<T>(T[] array, T item)
    {
        var list = new List<T>();
        if (array != null) { list.AddRange(array); }
        list.Add(item);
        return list.ToArray();
    }

    /// 특수무장 슬롯이 아닌(일반) 미사일 기능.
    private static ACO_DFUNC_Missile FindPrimaryMissile(SaccEntity entity)
    {
        foreach (ACO_DFUNC_Missile m in entity.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
        {
            if (m.GetComponentInParent<ACO_DFUNC_Special>(true) == null) { return m; }
        }
        return null;
    }

    /// 오른쪽 다이얼 Element 2를 빈 특수무장 슬롯으로 바꾼다(모듈은 카탈로그 설치가 넣는다). 바꿔 낀 SAV 기능의 잔탄 텍스트를 ammo로 돌려준다.
    private static ACO_DFUNC_Special AddSpecialSlot(SaccEntity entity, ACO_DFUNC_Missile primary, out Text ammo)
    {
        UdonSharpBehaviour old = entity.Dial_Functions_R[SpecialDialIndex];
        Transform parent = old != null ? old.transform.parent : primary.transform.parent;

        GameObject slotGo = new GameObject("ACO_SpecialWeapon");
        Undo.RegisterCreatedObjectUndo(slotGo, "ACO special weapon slot");
        slotGo.transform.SetParent(parent, false);
        if (old != null) { slotGo.transform.SetSiblingIndex(old.transform.GetSiblingIndex() + 1); }
        ACO_DFUNC_Special special = UdonSharpUndo.AddComponent<ACO_DFUNC_Special>(slotGo);

        // 잔탄 텍스트: 바꿔 낀 SAV 기능의 잔탄 텍스트(SF-2: HudCanvas/Bombs)를 모듈들이 같이 쓴다(카탈로그 설치가 넣는다).
        ammo = old != null ? FindAmmoText(old) : null;
        if (ammo == null) { Debug.LogWarning($"[ACO] '{entity.name}': 오른쪽 다이얼 Element {SpecialDialIndex} 기능에서 잔탄 텍스트를 찾지 못했습니다. 특수무장 모듈의 AmmoText를 직접 넣으세요.", special); }

        special.Modules = new UdonSharpBehaviour[0];
        special.ModuleIds = new string[0];

        if (old != null)
        {
            Undo.RecordObject(entity, "ACO special weapon slot");
            entity.Dial_Functions_R[SpecialDialIndex] = special;
            PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
            EditorUtility.SetDirty(entity);
            UdonBehaviourEnabled(old, false);
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(special);
        EditorUtility.SetDirty(special);
        FixKeyboardBindings(entity);
        BindSpecialKey(entity, special);
        Debug.Log($"[ACO] '{entity.name}': 오른쪽 다이얼 Element {SpecialDialIndex}({(old != null ? old.name : "빈 칸")})을 특수무장 슬롯으로 바꿨습니다(키보드 {SpecialKey}).", special);
        return special;
    }

    /// 데스크톱 키 SpecialKey(숫자 3)로 특수무장 슬롯을 고르게 한다(SAV_KeyboardControls). 슬롯을 가리키는 칸이 있으면 그 칸의 키를 바꾸고,
    /// 없으면 빈 칸(오른쪽부터)에 넣는다. 다른 기능이 같은 키를 쓰고 있으면 겹치지 않게 그 키를 비운다(로그로 알린다).
    private static void BindSpecialKey(SaccEntity entity, ACO_DFUNC_Special special)
    {
        SAV_KeyboardControls[] all = entity.GetComponentsInChildren<SAV_KeyboardControls>(true);
        if (all.Length == 0)
        {
            Debug.LogWarning($"[ACO] '{entity.name}'에 SAV_KeyboardControls가 없어 특수무장 키({SpecialKey})를 연결하지 못했습니다.", entity);
            return;
        }
        SAV_KeyboardControls kb = all[0];
        Undo.RecordObject(kb, "ACO special key");
        string slot = null;
        string empty = null;
        foreach (string side in new[] { "Rfunc", "Lfunc" })
        {
            for (int i = 1; i <= 10; i++)
            {
                var f = typeof(SAV_KeyboardControls).GetField(side + i);
                if (f == null) { continue; }
                var value = f.GetValue(kb) as UdonSharpBehaviour;
                if (value == special && slot == null) { slot = side + i; }
                if (value == null && empty == null) { empty = side + i; }
            }
        }
        if (slot == null)
        {
            if (empty == null)
            {
                Debug.LogWarning($"[ACO] '{entity.name}': SAV_KeyboardControls에 빈 칸이 없어 특수무장 키를 연결하지 못했습니다.", kb);
                return;
            }
            slot = empty;
            typeof(SAV_KeyboardControls).GetField(slot).SetValue(kb, special);
        }
        typeof(SAV_KeyboardControls).GetField(slot + "key").SetValue(kb, SpecialKey);

        // 같은 키를 쓰는 다른 칸은 비운다(한 키에 두 기능이 걸리지 않게).
        foreach (string side in new[] { "Rfunc", "Lfunc" })
        {
            for (int i = 1; i <= 10; i++)
            {
                string name = side + i;
                if (name == slot) { continue; }
                var keyField = typeof(SAV_KeyboardControls).GetField(name + "key");
                var funcField = typeof(SAV_KeyboardControls).GetField(name);
                if (keyField == null || funcField == null || (KeyCode)keyField.GetValue(kb) != SpecialKey) { continue; }
                var other = funcField.GetValue(kb) as UdonSharpBehaviour;
                if (other == special) { continue; }
                keyField.SetValue(kb, KeyCode.None);
                Debug.LogWarning($"[ACO] '{entity.name}': 키보드 {name}('{(other != null ? other.name : "없음")}')도 {SpecialKey}를 쓰고 있어 그 키를 비웠습니다 — 필요하면 다른 키를 넣으세요.", kb);
            }
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(kb);
        EditorUtility.SetDirty(kb);
    }

    /// 4AAM: 공중 표적 4개 동시 락온, 일제 발사. 슬롯 4 / 총 16발 / 일제 발사 뒤 12초에 다시 장전.
    public static void Apply4AAMPreset(ACO_DFUNC_Missile m)
    {
        m.UpgradeFamily = ACO_UpgradeFamily.SpecialAAM;
        m.CrossLockIndicator = true;
        m.TargetLayers = 1 << 29;
        m.MaxLockTargets = 4;
        m.OneMissilePerTarget = true;
        m.SlotCount = 4;
        m.MaxAmmo = 16;
        m.SlotReloadTime = 12f;
        m.LockRange = 3500f;
        m.LockAngle = 25f;
        m.LockTime = 1.2f;
        m.Damage = 100f;
        m.MaxSpeed = 400f;
        m.Acceleration = 300f;
        m.TurnRate = 110f;
        m.GuidanceMode = 2;
        m.AllowUnguidedFire = false;
        m.NumPreInstantiated = 4;
    }

    /// SAV 무장 기능의 잔탄 텍스트(UnityEngine.UI.Text 필드 중 이름에 ammo가 들어간 것, 없으면 첫 번째).
    private static Text FindAmmoText(UdonSharpBehaviour dfunc)
    {
        Text first = null;
        foreach (System.Reflection.FieldInfo f in dfunc.GetType().GetFields())
        {
            if (f.FieldType != typeof(Text)) { continue; }
            Text t = f.GetValue(dfunc) as Text;
            if (t == null) { continue; }
            if (f.Name.ToLowerInvariant().Contains("ammo")) { return t; }
            if (first == null) { first = t; }
        }
        return first;
    }

    private static List<SaccEntity> SelectedEntities()
    {
        var result = new List<SaccEntity>();
        foreach (GameObject go in Selection.gameObjects)
        {
            SaccEntity e = go.GetComponentInParent<SaccEntity>(true);
            if (e == null) { e = go.GetComponentInChildren<SaccEntity>(true); }
            if (e != null && !result.Contains(e)) { result.Add(e); }
        }
        return result;
    }

    private static ACO_DFUNC_Missile Replace(SaccEntity entity, DFUNC_AAM aam)
    {
        GameObject go = aam.gameObject;
        ACO_DFUNC_Missile w = UdonSharpUndo.AddComponent<ACO_DFUNC_Missile>(go);

        w.SAVControl = aam.SAVControl;
        w.HUDControl = aam.HUDControl;
        w.TargetingTransform = aam.TargetingTransform;
        w.FireKey = aam.FireKey;
        w.MissileType = aam.MissileType;
        w.LockAngle = aam.AAMLockAngle;
        w.LockTime = aam.AAMLockTime;
        w.LockRange = aam.AAMMaxTargetDistance;
        w.AllowFiringWhenGrounded = aam.AllowFiringWhenGrounded;
        w.InheritVelocity = aam.AAMInheritVelocity;
        w.WorldParent = aam.WorldParent;

        w.LaunchSound = aam.AAMLaunchSound;
        w.IdleSound = aam.AAMIdle;
        w.LockingSound = aam.AAMTargeting;
        w.LockedSound = aam.AAMTargetLock;

        w.TargetIndicator = aam.AAMTargetIndicator;
        w.AmmoText = aam.HUDText_AAM_ammo;
        w.AmmoTextTMP = aam.HUDText_AAM_ammo_TMP;
        w.AmmoTextTMPUGUI = aam.HUDText_AAM_ammo_TMPUGUI;

        w.WeaponAnimator = aam.AAMAnimator;
        w.AnimFloatName = aam.AnimFloatName;
        w.AnimFiredTriggerName = aam.AnimFiredTriggerName;

        w.Missile = AssetDatabase.LoadAssetAtPath<GameObject>(MissilePrefabPath);
        if (w.Missile == null) { Debug.LogWarning($"[ACO] 미사일 프리팹({MissilePrefabPath})이 없습니다. ACO → Create Prefabs로 만든 뒤 Missile에 넣으세요.", w); }

        w.LaunchPoints = MakeLaunchPoints(entity, aam.AAMLaunchPoint);

        ReplaceInDials(entity, aam, w);

        // DFUNC_AAM은 끈다(다이얼에서 빠졌으므로 이벤트도 받지 않는다). 지우지 않아 되돌리기 쉽다.
        UdonBehaviourEnabled(aam, false);

        PrefabUtility.RecordPrefabInstancePropertyModifications(w);
        EditorUtility.SetDirty(w);
        Debug.Log($"[ACO] '{entity.name}': '{go.name}'의 DFUNC_AAM을 ACO_DFUNC_Missile로 바꿨습니다.", w);
        return w;
    }

    /// 기존 발사 지점 + 기체 기준 좌우 대칭 위치. 기존 지점이 기체 중심선에 있으면 하나만 쓴다.
    private static Transform[] MakeLaunchPoints(SaccEntity entity, Transform original)
    {
        if (original == null) { return new Transform[0]; }
        Transform vehicle = entity.transform;
        Vector3 localPos = vehicle.InverseTransformPoint(original.position);
        if (Mathf.Abs(localPos.x) < 0.05f) { return new[] { original }; }

        // 좌우(YZ 평면) 대칭: 위치 x를 뒤집고, 회전은 (x, -y, -z, w)로 뒤집는다.
        Quaternion localRot = Quaternion.Inverse(vehicle.rotation) * original.rotation;
        Vector3 mirroredPos = new Vector3(-localPos.x, localPos.y, localPos.z);
        Quaternion mirroredRot = new Quaternion(localRot.x, -localRot.y, -localRot.z, localRot.w);

        string mirrorName = original.name + "_Mirror";
        Transform existing = original.parent != null ? original.parent.Find(mirrorName) : null;
        Transform mirror = existing;
        if (mirror == null)
        {
            GameObject g = new GameObject(mirrorName);
            Undo.RegisterCreatedObjectUndo(g, "ACO mirror launch point");
            mirror = g.transform;
            mirror.SetParent(original.parent, false);
        }
        mirror.position = vehicle.TransformPoint(mirroredPos);
        mirror.rotation = vehicle.rotation * mirroredRot;
        return new[] { original, mirror };
    }

    private static void ReplaceInDials(SaccEntity entity, UdonSharpBehaviour from, UdonSharpBehaviour to)
    {
        Undo.RecordObject(entity, "ACO replace dial function");
        bool found = ReplaceIn(entity.Dial_Functions_L, from, to) | ReplaceIn(entity.Dial_Functions_R, from, to);
        if (!found) { Debug.LogWarning($"[ACO] '{entity.name}'의 다이얼 배열에서 DFUNC_AAM을 찾지 못했습니다. Dial_Functions_L/R에 직접 넣으세요.", entity); }
        PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
        EditorUtility.SetDirty(entity);
    }

    private static bool ReplaceIn(UdonSharpBehaviour[] dials, UdonSharpBehaviour from, UdonSharpBehaviour to)
    {
        if (dials == null) { return false; }
        bool found = false;
        for (int i = 0; i < dials.Length; i++)
        {
            if (dials[i] == from) { dials[i] = to; found = true; }
        }
        return found;
    }

    // ───────────── 키보드 단축키 ─────────────

    /// SAV_KeyboardControls의 단축키(Lfunc1~10 / Rfunc1~10 = 다이얼 기능 ↔ 키)가 우리가 바꿔 낀 옛 기능을 가리키면 새 기능으로 옮긴다:
    ///  - DFUNC_AAM → 같은 오브젝트의 ACO_DFUNC_Missile
    ///  - 특수무장 슬롯의 모듈(SAV 폭탄/로켓 등) 또는 특수무장 자리에서 빠져 꺼진 옛 기능 → 특수무장 슬롯
    /// 다이얼에 있는 기능을 가리키는 키, 다이얼에 없지만 켜져 있는 기능(키보드 전용 기능)은 건드리지 않는다. 바꾼 키 수를 돌려준다.
    public static int FixKeyboardBindings(SaccEntity entity)
    {
        int fixedCount = 0;
        ACO_DFUNC_Special special = entity.GetComponentInChildren<ACO_DFUNC_Special>(true);
        bool specialInDials = special != null && InDials(entity, special);
        foreach (SAV_KeyboardControls kb in entity.GetComponentsInChildren<SAV_KeyboardControls>(true))
        {
            bool changed = false;
            foreach (string side in new[] { "Lfunc", "Rfunc" })
            {
                for (int i = 1; i <= 10; i++)
                {
                    System.Reflection.FieldInfo field = typeof(SAV_KeyboardControls).GetField(side + i);
                    if (field == null) { continue; }
                    var current = field.GetValue(kb) as UdonSharpBehaviour;
                    UdonSharpBehaviour next = KeyboardTarget(entity, current, special, specialInDials);
                    if (next == null || next == current) { continue; }
                    if (!changed) { Undo.RecordObject(kb, "ACO keyboard bindings"); }
                    field.SetValue(kb, next);
                    changed = true;
                    fixedCount++;
                    Debug.Log($"[ACO] '{entity.name}': 키보드 {side}{i}을(를) '{current.name}' → '{next.name}'(으)로 옮겼습니다.", kb);
                }
            }
            if (changed)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(kb);
                EditorUtility.SetDirty(kb);
            }
        }
        return fixedCount;
    }

    /// 단축키가 가리켜야 할 기능. 바꿀 필요가 없으면 null.
    private static UdonSharpBehaviour KeyboardTarget(SaccEntity entity, UdonSharpBehaviour current, ACO_DFUNC_Special special, bool specialInDials)
    {
        if (current == null || InDials(entity, current)) { return null; }
        ACO_DFUNC_Missile aco = current.GetComponent<ACO_DFUNC_Missile>();
        if (current is DFUNC_AAM && aco != null && InDials(entity, aco)) { return aco; }
        if (!specialInDials) { return null; }
        if (special.Modules != null && System.Array.IndexOf(special.Modules, current) >= 0) { return special; }
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(current);
        if (backing != null && !backing.enabled) { return special; }   // 특수무장 자리에서 빠져 꺼진 옛 기능
        return null;
    }

    private static bool InDials(SaccEntity entity, UdonSharpBehaviour b)
    {
        return (entity.Dial_Functions_L != null && System.Array.IndexOf(entity.Dial_Functions_L, b) >= 0)
            || (entity.Dial_Functions_R != null && System.Array.IndexOf(entity.Dial_Functions_R, b) >= 0);
    }

    /// 단축키가 옛 기능을 가리키는 수(셋업 검사용, 바꾸지 않는다).
    public static int CountStaleKeyboardBindings(SaccEntity entity)
    {
        int n = 0;
        ACO_DFUNC_Special special = entity.GetComponentInChildren<ACO_DFUNC_Special>(true);
        bool specialInDials = special != null && InDials(entity, special);
        foreach (SAV_KeyboardControls kb in entity.GetComponentsInChildren<SAV_KeyboardControls>(true))
        {
            foreach (string side in new[] { "Lfunc", "Rfunc" })
            {
                for (int i = 1; i <= 10; i++)
                {
                    System.Reflection.FieldInfo field = typeof(SAV_KeyboardControls).GetField(side + i);
                    if (field == null) { continue; }
                    if (KeyboardTarget(entity, field.GetValue(kb) as UdonSharpBehaviour, special, specialInDials) != null) { n++; }
                }
            }
        }
        return n;
    }

    public static void UdonBehaviourEnabled(UdonSharpBehaviour behaviour, bool on)
    {
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
        if (backing == null) { return; }
        Undo.RecordObject(backing, "ACO disable DFUNC_AAM");
        backing.enabled = on;
        PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
    }

    // ───────────── HUD 슬롯 막대 ─────────────

    /// 잔탄 텍스트 오른쪽에 막대 원본(SlotBar)을 만들어 SlotBarTemplate에 넣는다. 실제 막대는 게임 중 슬롯 수만큼 복제된다.
    public static bool BuildSlotBars(ACO_DFUNC_Missile w)
    {
        Graphic anchor = w.AmmoText != null ? (Graphic)w.AmmoText : (Graphic)w.AmmoTextTMPUGUI;
        if (anchor == null)
        {
            Debug.LogWarning($"[ACO] '{w.name}': 잔탄 텍스트(AmmoText)가 없어 HUD 막대 위치를 정할 수 없습니다. SlotBarTemplate을 직접 만드세요.", w);
            return false;
        }
        RectTransform text = anchor.rectTransform;
        Transform canvas = text.parent;

        // 무기마다 묶음 이름이 달라야 다른 무기의 막대를 지우지 않는다(특수무장 모듈은 모듈 이름을 붙인다).
        bool isModule = w.GetComponentInParent<ACO_DFUNC_Special>(true) != null;
        string barsName = isModule ? BarsName + "_" + w.gameObject.name : BarsName;
        Transform old = canvas.Find(barsName);
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
        // 예전 버전은 특수무장 막대를 한 이름(_Special)으로 만들었다 — 남아 있으면 지운다(모듈마다 다시 만든다).
        Transform legacy = isModule ? canvas.Find(BarsName + "_Special") : null;
        if (legacy != null) { Undo.DestroyObjectImmediate(legacy.gameObject); }

        Material mat = anchor.material;
        Color color = anchor.color;
        int slots = Mathf.Max(w.SlotCount, 1);
        bool vertical = w.SlotBarVertical;
        float pitch = BarThickness + BarGap;
        // 세로: 세로 막대(두께 × 길이)를 가로로 나란히. 가로: 가로 막대(길이 × 두께)를 세로로 쌓는다.
        Vector2 barSize = vertical ? new Vector2(BarThickness, BarLength) : new Vector2(BarLength, BarThickness);

        // 막대 묶음: 텍스트 오른쪽 끝 + 간격. 텍스트의 기준점·회전을 따르고, 크기는 캔버스 단위로 다시 잰다.
        RectTransform group = NewRect(barsName, canvas);
        Undo.RegisterCreatedObjectUndo(group.gameObject, "ACO slot bars");
        group.anchorMin = text.anchorMin;
        group.anchorMax = text.anchorMax;
        group.pivot = new Vector2(0f, 0.5f);
        group.localRotation = text.localRotation;
        group.localScale = Vector3.one;
        float textHalfWidth = text.rect.width * text.localScale.x * 0.5f;
        group.anchoredPosition = text.anchoredPosition + new Vector2(textHalfWidth + TextGap, 0f);
        group.sizeDelta = vertical ? new Vector2(pitch * slots, BarLength) : new Vector2(BarLength, pitch * slots);

        // 원본 하나 = 첫 슬롯(세로: 맨 왼쪽, 가로: 맨 위). 슬롯 i는 SlotBarSpacing × i 만큼 옮겨진다.
        RectTransform template = NewRect("SlotBar", group);
        template.anchorMin = template.anchorMax = new Vector2(0f, 0.5f);
        template.pivot = new Vector2(0f, 0.5f);
        template.sizeDelta = barSize;
        template.anchoredPosition = vertical ? Vector2.zero : new Vector2(0f, pitch * (slots - 1) * 0.5f);

        // 그리는 순서 = 형제 순서. HUDText 셰이더는 한 번 그린 픽셀의 스텐실을 지워(StencilOp Zero) 겹친 자리엔
        // 먼저 그린 것만 남으므로 밝은 것부터 둔다: Ready → Fill → Back(빈 부분만 채운다).

        // Ready: 장전 완료 시 켜지는 밝은 막대.
        RectTransform ready = NewBar("Ready", template, mat, WithAlpha(color, 1f), Vector2.zero);
        ready.gameObject.SetActive(false);

        // Fill: 피벗이 차기 시작하는 끝(세로: 아래, 가로: 왼쪽)이라 그 축의 스케일을 줄이면 그쪽부터 찬다.
        RectTransform fill = NewRect("Fill", template);
        fill.pivot = vertical ? new Vector2(0.5f, 0f) : new Vector2(0f, 0.5f);
        Stretch(fill);
        NewBar("Bar", fill, mat, WithAlpha(color, FillAlpha), Vector2.zero);

        NewBar("Back", template, mat, WithAlpha(color, BackAlpha), Vector2.zero);

        template.gameObject.SetActive(false);

        Undo.RecordObject(w, "ACO slot bars");
        w.SlotBarTemplate = template;
        w.SlotBarSpacing = vertical ? new Vector3(pitch, 0f, 0f) : new Vector3(0f, -pitch, 0f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(w);
        EditorUtility.SetDirty(w);
        Debug.Log($"[ACO] '{w.name}': HUD 슬롯 막대를 '{canvas.name}/{barsName}'에 만들었습니다(슬롯 {slots}개 기준 배치).", group);
        return true;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.layer = parent.gameObject.layer;
        RectTransform r = g.GetComponent<RectTransform>();
        r.SetParent(parent, false);
        r.localScale = Vector3.one;
        r.localRotation = Quaternion.identity;
        return r;
    }

    private static RectTransform NewBar(string name, Transform parent, Material mat, Color color, Vector2 offset)
    {
        RectTransform r = NewRect(name, parent);
        Stretch(r);
        r.anchoredPosition = offset;
        Image img = r.gameObject.AddComponent<Image>();
        img.material = mat;
        img.color = color;
        img.raycastTarget = false;
        return r;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}
