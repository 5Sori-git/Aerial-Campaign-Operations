using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 프리팹 수치 변경. 씬이 아니라 프리팹 에셋을 고치고, 씬 인스턴스에 남은 같은 값의 오버라이드를 지운다.
/// Setup Check의 "프리팹 수치" 항목이 부른다. 적용하거나 무시한 항목은 ID를 AppliedPath에 적어 다시 묻지 않는다.
/// 수치를 또 바꾸려면 Entries에 새 ID로 항목을 더한다.
public static class ACO_PrefabTuning
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string AppliedPath = "Assets/ACO_Addon/Editor/ACO_AppliedTuning.txt";
    private const int FriendlyShips = 1 << 26;

    public class Entry
    {
        public string Id;
        public string Text;
        public Action Apply;
    }

    /// 씬의 보스 페이즈 모듈(공중 요새·랜스 포대) 방공 수리 값. 모듈은 프리팹이 아니라 씬 오브젝트라 씬을 고친다.
    private static void TuneBossRepairs()
    {
        foreach (ACO_PhaseFortress m in Object.FindObjectsOfType<ACO_PhaseFortress>(true))
        {
            Undo.RecordObject(m, "ACO boss repairs");
            m.RepairCount = 3;
            m.MaxRepairs = 6;
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        foreach (ACO_PhaseRailgun m in Object.FindObjectsOfType<ACO_PhaseRailgun>(true))
        {
            Undo.RecordObject(m, "ACO boss repairs");
            m.RepairCount = 3;
            m.MaxRepairs = 6;
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        Debug.Log("[ACO] 보스 방공 수리: 한 번에 3곳, 최대 6곳으로 맞췄습니다. 씬을 저장하세요.");
    }

    /// 랜스 포대 페이즈 모듈: 코어 열림 30초, 첫 충전 대기 최대 180초(직접 바꾼 값 — 25가 아니면 열림 시간은 건드리지 않는다).
    private static void TuneRailgunArm()
    {
        foreach (ACO_PhaseRailgun m in Object.FindObjectsOfType<ACO_PhaseRailgun>(true))
        {
            Undo.RecordObject(m, "ACO railgun arm");
            if (Mathf.Approximately(m.CoreOpenBefore, 25f)) { m.CoreOpenBefore = 30f; }
            m.ArmMaxWait = 180f;
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        Debug.Log("[ACO] 랜스 포대: 코어 열림 30초, 첫 충전은 공격할 때(최대 180초 대기)로 맞췄습니다. 씬을 저장하세요.");
    }

    /// 해상 유닛(함선·항공모함·구축함·육상전함) 부위의 범위 피해 배율을 0.2로, LASM 무장은 부위에도 그대로 주게 한다.
    /// 공중 요새·랜스 포대는 1 그대로. 무장은 이름이 Module_LASM인 ACO_DFUNC_Missile(기체 프리팹·씬)이다.
    private static void TuneSeaPartSplash()
    {
        EditPrefabs(root =>
        {
            ACO_Warship ship = root.GetComponent<ACO_Warship>();
            if (ship == null || root.name == "NPC_Fortress" || root.name == "NPC_Railgun") { return false; }
            if (Mathf.Approximately(ship.PartSplashScale, ACO_PrefabBuilder.SeaPartSplash)) { return false; }
            ship.PartSplashScale = ACO_PrefabBuilder.SeaPartSplash;
            Mark(ship);
            return true;
        });
        int lasm = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("SaccFlightAndVehicles") || !File.ReadAllText(path).Contains("m_Name: Module_LASM")) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool any = false;
                foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
                {
                    if (m.gameObject.name != "Module_LASM" || m.FullPartSplash) { continue; }
                    m.FullPartSplash = true;
                    Mark(m);
                    any = true;
                    lasm++;
                }
                if (any) { PrefabUtility.SaveAsPrefabAsset(root, path); Debug.Log($"[ACO] 프리팹 수치: {Path.GetFileName(path)}의 LASM이 함선 부위에도 범위 피해를 그대로 줍니다."); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (m.gameObject.name != "Module_LASM" || m.FullPartSplash) { continue; }
            Undo.RecordObject(m, "ACO LASM part splash");
            m.FullPartSplash = true;
            Mark(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
            lasm++;
        }
        Debug.Log($"[ACO] 해상 유닛 부위 범위 피해 0.2배, LASM 예외 {lasm}곳. 씬이 바뀌었으면 저장하세요.");
    }

    /// 공중 요새 추락 충돌 층에서 Environment를 빼고(선체에 맞아 바로 터지던 것), 랜스 포대 대폭발을 코어 파괴 14초 뒤로.
    private static void TuneBossDeath()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            ACO_FlyingFortress fortress = root.GetComponent<ACO_FlyingFortress>();
            if (fortress != null && fortress.ImpactLayers.value != 17) { fortress.ImpactLayers = 17; Mark(fortress); any = true; }
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            if (railgun != null && !Mathf.Approximately(railgun.DeathDelay, 14f)) { railgun.DeathDelay = 14f; Mark(railgun); any = true; }
            return any;
        });
    }

    /// 소형 레일건(랜스 포대·육상전함 주포)을 깔아 두는 사격으로: 3.5초 뒤 자리를 노린다(고정하자마자 쏘는 것은 코드).
    /// 폭발 지역 유지 시간: 육상전함 주포 5초(8초에서 줄임), 랜스 포대는 표적이 닿을 때까지 남게 4.5초 이상.
    private static void TuneMiniRailgunLay()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            bool landShip = root.GetComponent<ACO_LandShip>() != null;
            foreach (ACO_MiniRailgun m in root.GetComponentsInChildren<ACO_MiniRailgun>(true))
            {
                bool dirty = false;
                if (!Mathf.Approximately(m.LeadTime, 3.5f)) { m.LeadTime = 3.5f; dirty = true; }
                if (landShip && !Mathf.Approximately(m.ZoneTime, ACO_PrefabBuilder.LsMainGunZoneTime)) { m.ZoneTime = ACO_PrefabBuilder.LsMainGunZoneTime; dirty = true; }
                else if (!landShip && m.ZoneTime < 4.5f) { m.ZoneTime = 4.5f; dirty = true; }
                if (dirty) { Mark(m); any = true; }
            }
            return any;
        });
    }

    /// 보스 코어(본체)를 범위 피해 면역으로 — 코어를 직접 맞혀야만 깎인다. 처음엔 육상전함만(landship-core-splash-1), 이제 공중 요새·랜스 포대도(boss-core-splash-1).
    private static void TuneLandShipCoreSplash()
    {
        EditPrefabs(root =>
        {
            ACO_Warship ship = root.GetComponent<ACO_Warship>();
            bool boss = root.GetComponent<ACO_LandShip>() != null || root.GetComponent<ACO_FlyingFortress>() != null || root.GetComponent<ACO_Railgun>() != null;
            if (ship == null || !boss || ship.CoreSplashImmune) { return false; }
            ship.CoreSplashImmune = true;
            Mark(ship);
            return true;
        });
    }

    public static readonly Entry[] Entries =
    {
        new Entry { Id = "ciws-1", Text = "NPC 프리팹에 CIWS 요격 기본값과 탄 판정 반경을 넣습니다", Apply = TuneCiws },
        new Entry { Id = "escort-priority-1", Text = "NPC 프리팹에 호위 유닛 공격 우선순위를 넣습니다", Apply = TuneEscortPriority },
        new Entry { Id = "fleet-1", Text = "적 함선 프리팹의 VLS·ASM·함포가 해수면의 아군 함선을 노리게 합니다(순양함 VLS 사거리 5km)", Apply = TuneFleet },
        new Entry { Id = "railgun-mini-1", Text = "랜스 포대 소형 레일건 4문을 NPC_Turret_RailgunMini 프리팹으로 바꿉니다", Apply = ACO_PrefabBuilder.ConvertRailgunMinis },
        new Entry { Id = "railgun-model-1", Text = "랜스 포대와 소형 레일건을 새 모델로 바꿉니다. 옮긴 부위의 충돌도 맞춥니다. 씬 참조는 유지됩니다", Apply = ACO_PrefabBuilder.InstallLanceModel },
        new Entry { Id = "turret-model-1", Text = "Flak, CIWS, VLS 포대를 새 모델로 바꿉니다. 공중 요새의 포대도 함께 바뀝니다. 팩 모델 자식은 프리팹에서 지웁니다", Apply = ACO_PrefabBuilder.InstallTurretModels },
        new Entry { Id = "fortress-turrets-3", Text = "공중 요새 포대를 Flak 4, CIWS 8, VLS 6으로 바꾸고 선체 경사에 맞춰 좌우 대칭으로 놓습니다. 배 쪽 VLS 2문과 후면 해치 CIWS 2문이 늘고 Flak 4문이 빠집니다", Apply = ACO_PrefabBuilder.PlaceFortressTurrets },
        new Entry { Id = "fortress-core-1", Text = "공중 요새 코어를 세운 원자로로 바꾸고, 코어 히트박스와 증원 발진 지점을 실제 자리에 맞춥니다", Apply = ACO_PrefabBuilder.PlaceFortressCore },
        new Entry { Id = "scm-model-1", Text = "SCM 탄체를 날개를 편 새 모델로 바꿉니다. 예전 모델은 프리팹에 꺼 둡니다", Apply = ACO_PrefabBuilder.InstallScmModel },
        new Entry { Id = "fx-tracer-blast-1", Text = "유닛 탄환(적 노랑, 아군 빨강)과 대폭발을 새 텍스처로 바꿉니다. 대폭발은 이 항목을 고쳐야 나옵니다", Apply = ACO_FxBuilder.ApplyToPrefabs },
        new Entry { Id = "fx-tracer-blast-2", Text = "대폭발을 1.5배로 키우고 탄환 색을 바꿉니다(적 노랑, 아군 빨강). 미리보기도 다시 만듭니다", Apply = ACO_FxBuilder.ApplyToPrefabs },
        new Entry { Id = "fx-missile-1", Text = "미사일·폭탄·로켓·포탄 폭발을 새 텍스처로 바꿉니다(공중·땅·물). 예전 파티클은 꺼 둡니다", Apply = ACO_FxBuilder.ApplyToWeapons },
        new Entry { Id = "fx-railgun-1", Text = "레일건 탄, 공중 폭발, 진행 방향 고리를 넣습니다(주포·소형 레일건)", Apply = ACO_FxBuilder.ApplyToRailgun },
        new Entry { Id = "fx-railgun-2", Text = "레일건 고리를 서브 이미터로 바꿉니다. 소형은 탄이, 주포는 보이지 않는 파티클이 궤적에 고리를 남깁니다", Apply = ACO_FxBuilder.ApplyToRailgun },
        new Entry { Id = "fx-railgun-3", Text = "주포 착탄을 50km 수평선, 발사 2초 뒤로 옮기고(크기도 키움) 포구에 거대한 고리를 넣습니다", Apply = ACO_FxBuilder.ApplyToRailgun },
        new Entry { Id = "fx-trail-1", Text = "미사일·로켓·포탄 연기 궤적을 부드러운 곡선과 연기 무늬로 바꿉니다", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-2", Text = "연기 궤적을 막 나온 곳은 좁고 흐리게, 뒤로 갈수록 넓고 선명하게 바꿉니다. 띠가 접히지 않게 점의 무작위 움직임을 없애고 무늬를 부드럽게 합니다", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-3", Text = "연기 궤적을 회색으로 바꾸고 흔들림을 늘립니다. 붉은 구간을 줄이고 끝을 천천히 흐리게 합니다. 로켓·포탄 연기가 터진 뒤 한순간에 사라지지 않게 수명을 맞춥니다", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-4", Text = "ACO_Missile_SRAAM에서 손으로 맞춘 연기 궤적 값을 모든 미사일·로켓·포탄에 넣습니다(무기별 수명과 폭은 그대로입니다)", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-5", Text = "연기 궤적이 탄 뒤로 나가게 돌리고 방출 반경을 SRAAM 값으로 맞춥니다. 대형 미사일 궤적을 조금 키웁니다. 로켓과 포탄의 값, 손으로 돌린 프리팹은 그대로입니다", Apply = ACO_FxBuilder.ApplyTrailAim },
        new Entry { Id = "fx-wreck-1", Text = "전투기·공격기·에이스와 비행 중인 탄도탄이 터지면 진행 방향으로 불붙은 파편이 날아갑니다. 탄도탄은 공중 폭발을 따로 씁니다", Apply = ACO_FxBuilder.ApplyToWreck },
        new Entry { Id = "tel-model-1", Text = "TEL을 8×8 발사 차량과 새 탄도탄 모델로 바꿉니다. 이렉터가 90도까지 서고 유압 실린더가 함께 움직입니다. 예전 팩 모델은 꺼 둡니다", Apply = ACO_PrefabBuilder.InstallTelModel },
        new Entry { Id = "fx-wake-1", Text = "함선 항적을 거품 텍스처로 바꾸고 파티클 수를 줄입니다(선미 거품, 선수 V자 물결)", Apply = ACO_FxBuilder.ApplyToWakes },
        new Entry { Id = "fx-death-1", Text = "유닛 파괴 효과를 프리팹 5종(공중, 지상, 함선, 건물, 부위)으로 만들어 모든 NPC가 파괴 때 켜는 오브젝트(ShowOnDeath)에 넣습니다. 예전 파티클과 소리는 지웁니다(잔해 모델은 남습니다)", Apply = ACO_FxBuilder.ApplyToDeath },
        new Entry { Id = "deco-layer-1", Text = "건물, 크레인, 탑 콜라이더를 Environment로 옮깁니다. 땅을 재는 유닛이 구조물 위로 올라가지 않습니다. 충돌과 무기 판정은 그대로이고 활주로는 땅으로 남습니다", Apply = ACO_DecoBuilder.ApplyColliderLayers },
        new Entry { Id = "landship-turrets-1", Text = "육상전함 갑판 방공을 놓습니다: 16셀 긴 VLS 4(넓은 갑판 양옆), 네 셀 VLS 1, CIWS 3, Flak 4. 프리팹을 다시 만들지 않고 이 포대들만 바꿉니다(주포·부포·손으로 고친 부분은 그대로)", Apply = ACO_PrefabBuilder.PlaceLandShipTurrets },
        new Entry { Id = "landship-route-1", Text = "육상전함 선체 최대 기울기를 8→15도(육각 지형 비탈과 같게)로 넓힙니다(항로 확인은 콘보이와 같은 경사 검사로 바뀌었습니다)", Apply = ACO_PrefabBuilder.TuneLandShipRoute },
        new Entry { Id = "sea-part-splash-1", Text = "해상 유닛(함선·항공모함·구축함·육상전함)의 부위가 범위 피해를 0.2배만 받습니다(탄이 직접 맞은 부위는 그대로). LASM은 예외로 부위에도 그대로 줍니다", Apply = TuneSeaPartSplash },
        new Entry { Id = "landship-lines-1", Text = "씬의 육상전함 페이즈: 이름 LAND DREADNOUGHT, 배너·성공·실패 글자와 상황별 대사를 새로 넣습니다(궤도는 절반 파괴 때 한 번, 방공 수리 대사 추가). 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneLandShipTexts },
        new Entry { Id = "boss-death-1", Text = "공중 요새: 추락 충돌 층에서 Environment를 빼 공중에서 바로 터지지 않고 땅·바다에 닿을 때 터집니다. 랜스 포대: 코어 파괴 14초 뒤 대폭발합니다(그동안 연쇄 폭발)", Apply = TuneBossDeath },
        new Entry { Id = "landship-endtracks-1", Text = "육상전함 앞뒤 끝 궤도 4개: 부서져도 사라지지 않고 남아 연기를 냅니다(배기구가 있는 가운데 궤도는 그대로 사라집니다)", Apply = ACO_PrefabBuilder.PlaceLandShipEndTracks },
        new Entry { Id = "landship-movefx-1", Text = "육상전함: 주포 최소 폭발 거리 1km→2km(폭발 반경은 그대로), 앞뒤 끝 궤도 흙먼지(뭍에서 움직일 때), 물 위 항적(기존 함선 항적)을 넣습니다", Apply = ACO_PrefabBuilder.PlaceLandShipMoveFx },
        new Entry { Id = "landship-movefx-2", Text = "육상전함 궤도 흙먼지를 지상 폭발 흙먼지와 같은 연기 텍스처(플립북)로, 항적을 함선 항적 거품으로 다시 입힙니다", Apply = ACO_PrefabBuilder.PlaceLandShipMoveFx },
        new Entry { Id = "mini-railgun-lay-1", Text = "소형 레일건(랜스 포대·육상전함 주포): 3.5초 뒤 자리를 노리고 고정하자마자 쏩니다 — 폭발이 표적 앞에 먼저 생겨 남습니다. 폭발 지역 유지 시간은 육상전함 주포 5초, 랜스 포대 4.5초", Apply = TuneMiniRailgunLay },
        new Entry { Id = "mini-railgun-shell-1", Text = "소형 레일건(랜스 포대·육상전함 주포) 폭발 지역에 피해 구와 같은 크기의 반투명 불빛 껍질을 띄웁니다 — 피해가 끝나는 순간 사라집니다", Apply = ACO_PrefabBuilder.PlaceMiniZoneShells },
        new Entry { Id = "landship-core-splash-1", Text = "육상전함 코어가 범위 피해를 받지 않습니다 — 코어를 락온해 직접 맞혀야만 깎입니다(LASM 같은 예외 무기도)", Apply = TuneLandShipCoreSplash },
        new Entry { Id = "boss-core-splash-1", Text = "공중 요새·랜스 포대 코어도 범위 피해를 받지 않습니다 — 코어를 직접 맞혀야만 깎입니다(육상전함은 이미 적용)", Apply = TuneLandShipCoreSplash },
        new Entry { Id = "boss-repair-1", Text = "씬의 공중 요새·랜스 포대 페이즈 모듈: 방공 수리를 한 번에 3곳, 페이즈 전체 최대 6곳으로 맞춥니다", Apply = TuneBossRepairs },
        new Entry { Id = "mmp-missile-model-1", Text = "MMP 탄체(ACO_Missile_MMP)를 맥주캔형 마이크로미사일 모델로 다시 만듭니다 — 엔진 불꽃·궤적이 캔 꼬리 노즐에서 나옵니다", Apply = ACO_PrefabBuilder.RebuildMmpMissile },
        new Entry { Id = "mmp-other-1", Text = "MMP를 격납고 OTHER 분류로 옮기고 강화를 받지 않게 합니다(정의 프리팹·기체 프리팹의 Module_MMP·격납고 목록)", Apply = ACO_SpecialWeaponCatalog.TuneMmpOther },
        new Entry { Id = "superace-twins-1", Text = "슈퍼에이스(예전 REAPER)를 분신 에이스 형제의 형 HEADS로 바꾸고(이름·대사·쌍둥이 규칙) 교차 기동 부품을 붙입니다. 프리팹을 다시 만들지 않아 씬 참조는 그대로입니다", Apply = ACO_PrefabBuilder.ApplyHeadsPreset },
        new Entry { Id = "railgun-arm-1", Text = "씬의 랜스 포대 페이즈 모듈: 코어 장갑이 발사 30초 전부터 열리고(25초였다), 공격을 시작할 때까지 첫 충전을 기다립니다(공격이 없으면 180초 뒤)", Apply = TuneRailgunArm },
        new Entry { Id = "deco-model-1", Text = "도시, 마을, 항구, 비행장, 등대, 레이더, 통신탑, 유적을 새 모델로 다시 만듭니다. 프리팹을 손으로 고쳤다면 그 내용은 사라집니다", Apply = ACO_DecoBuilder.InstallModels },
    };

    public static bool IsApplied(string id)
    {
        if (!File.Exists(AppliedPath)) { return false; }
        return Array.IndexOf(File.ReadAllLines(AppliedPath), id) >= 0;
    }

    public static void MarkApplied(string id)
    {
        if (IsApplied(id)) { return; }
        File.AppendAllText(AppliedPath, id + "\n");
        AssetDatabase.ImportAsset(AppliedPath);
    }

    public static void Apply(Entry entry)
    {
        entry.Apply();
        AssetDatabase.SaveAssets();
        MarkApplied(entry.Id);
        Debug.Log($"[ACO] 프리팹 수치 '{entry.Id}'를 적용했습니다.");
    }

    // ───────────── CIWS ─────────────

    private static void TuneCiws()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                bool dirty = false;
                if (!aa.InterceptDefaultsApplied) { ACO_SceneBaker.ApplyInterceptDefaults(aa); dirty = true; }
                if (aa.InterceptTuneVersion < ACO_SceneBaker.CiwsTuneVersion && (aa.InterceptProtectLayers.value & (1 << 29)) != 0)
                {
                    ACO_SceneBaker.ApplyEnemyCiwsTune(aa);
                    dirty = true;
                }
                if (ACO_SceneBaker.ApplyShellRadius(aa)) { Mark(aa.Gun); dirty = true; }
                if (dirty) { Mark(aa); any = true; }
            }
            return any;
        });
        RevertInScene<ACO_AAGunSite>("InterceptProtectLayers", "InterceptRange", "InterceptReactionTime", "InterceptKillTime",
            "InterceptCooldown", "DefenseRadius", "OtherDefenseFactor", "InterceptTuneVersion", "InterceptDefaultsApplied");
        RevertInScene<ACO_NPCGun>("ShellHitRadius");
    }

    // ───────────── 호위 유닛 ─────────────

    private static void TuneEscortPriority()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_EscortUnit e in root.GetComponentsInChildren<ACO_EscortUnit>(true))
            {
                if (e.PriorityDefaultsApplied) { continue; }
                e.PriorityDefaultsApplied = true;
                bool flagship = e.Role == ACO_EscortUnit.ROLE_FLAGSHIP;
                if (flagship) { e.AttackPriority = 2500f; }
                if (flagship || (e.MoveMode == ACO_EscortUnit.MOVE_AIR && !e.Hostile && e.Role == ACO_EscortUnit.ROLE_VIP)) { e.PriorityForSites = true; }
                Mark(e);
                any = true;
            }
            return any;
        });
        RevertInScene<ACO_EscortUnit>("AttackPriority", "PriorityForSites", "PriorityDefaultsApplied");
    }

    // ───────────── 함대전 ─────────────

    private static void TuneFleet()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_Warship ship in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (TuneShip(ship)) { any = true; }
            }
            return any;
        });
        RevertInScene<ACO_SAMSite>("TargetLayers", "MinTargetAltitude", "DetectRange", "LoseTargetRange");
        foreach (ACO_AAGunSite gun in FindInScene<ACO_AAGunSite>())
        {
            if (IsMainGun(gun)) { Revert(gun, "TargetLayers", "MinTargetAltitude"); }
        }
    }

    /// 함선 VLS·ASM·함포가 해수면의 아군 함선(26)을 노리게 한다. 두 함대는 3.5km에서 멈추므로 그보다 짧은 사거리만 올린다.
    public static bool TuneShip(ACO_Warship ship)
    {
        if (ship.ExternalPose || ship.Parts == null) { return false; }   // 공중 요새는 함대전에 나오지 않는다
        bool any = false;
        foreach (UdonSharp.UdonSharpBehaviour part in ship.Parts)
        {
            ACO_SAMSite sam = part as ACO_SAMSite;
            if (sam != null)
            {
                bool dirty = false;
                if ((sam.TargetLayers.value & FriendlyShips) == 0) { sam.TargetLayers = sam.TargetLayers.value | FriendlyShips; dirty = true; }
                if (sam.MinTargetAltitude > -100000f) { sam.MinTargetAltitude = -100000f; dirty = true; }
                if (sam.DetectRange < 4500f) { sam.DetectRange = 5000f; dirty = true; }
                if (sam.LoseTargetRange < 4500f) { sam.LoseTargetRange = 6000f; dirty = true; }
                if (AddShipTargets(sam.EntityControl)) { dirty = true; }
                if (dirty) { Mark(sam); any = true; }
            }
            ACO_AAGunSite gun = part as ACO_AAGunSite;
            if (gun != null && IsMainGun(gun))   // CIWS는 대공·요격 그대로
            {
                bool dirty = false;
                if ((gun.TargetLayers.value & FriendlyShips) == 0) { gun.TargetLayers = gun.TargetLayers.value | FriendlyShips; dirty = true; }
                if (gun.MinTargetAltitude > -100000f) { gun.MinTargetAltitude = -100000f; dirty = true; }
                if (AddShipTargets(gun.EntityControl)) { dirty = true; }
                if (dirty) { Mark(gun); any = true; }
            }
        }
        return any;
    }

    private static bool IsMainGun(ACO_AAGunSite gun) { return gun.gameObject.name.Contains("MainGun"); }

    private static bool AddShipTargets(SaccEntity entity)
    {
        if (entity == null || (entity.AAMTargetsLayer.value & FriendlyShips) != 0) { return false; }
        entity.AAMTargetsLayer |= FriendlyShips;
        Mark(entity);
        return true;
    }

    // ───────────── 공통 ─────────────

    /// NPC 프리팹을 하나씩 열어 고친다. 포대 프리팹을 먼저 고쳐야 포대를 품은 함선·요새가 새 값을 받는다.
    public static void EditPrefabs(Func<GameObject, bool> edit)
    {
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("NPC_ t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path).StartsWith("NPC_")) { paths.Add(path); }
        }
        paths.Sort((a, b) =>
        {
            int ta = a.Contains("NPC_Turret") ? 0 : 1;
            int tb = b.Contains("NPC_Turret") ? 0 : 1;
            return ta != tb ? ta - tb : string.CompareOrdinal(a, b);
        });
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!edit(root)) { continue; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 프리팹 수치: {Path.GetFileName(path)}를 고쳤습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void Mark(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }

    private static List<T> FindInScene<T>() where T : Component
    {
        var list = new List<T>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            list.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return list;
    }

    private static void RevertInScene<T>(params string[] fields) where T : Component
    {
        foreach (T c in FindInScene<T>()) { Revert(c, fields); }
    }

    /// 씬 인스턴스의 오버라이드를 지운다 — 프리팹 값이 보이게. 프리팹이 아닌 씬 오브젝트는 그대로다.
    private static void Revert(Component c, params string[] fields)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(c)) { return; }
        var so = new SerializedObject(c);
        foreach (string field in fields)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null && p.prefabOverride) { PrefabUtility.RevertPropertyOverride(p, InteractionMode.UserAction); }
        }
    }
}
