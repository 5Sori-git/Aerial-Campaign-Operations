using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UdonSharpEditor;
using ACO;

/// 셋업 검사: Bake가 하지 않는 구조 변경과 프리팹 수치 변경.
public partial class ACO_SetupValidator
{
    /// 함대전·요새·레일건·TEL에 필요한 유닛. 프리팹이 없으면 만들어 넣는다.
    private void CheckPool()
    {
        const string g = "유닛 풀";
        foreach (ACO_MissionGenerator gen in FindAll<ACO_MissionGenerator>())
        {
            Transform poolRoot = gen.UnitPoolRoot != null ? gen.UnitPoolRoot : gen.transform;
            List<string> missing = ACO_SceneBaker.MissingPoolUnits(poolRoot);
            if (missing.Count == 0) { Add(g, OK, "필수 유닛이 풀에 있습니다.", gen); continue; }
            Add(g, WARN, $"풀에 없는 유닛: {string.Join(", ", missing)}", gen, () => ACO_SceneBaker.AddPoolUnits(poolRoot));
        }

        // 함선 부위 목록(Parts)에 부위 스크립트 대신 SaccEntity·SaccTarget이 들어간 칸 — 그 부위는 격침·부활 통지를 못 받는다
        List<string> wrongParts = ACO_ShipParts.FindWrong();
        if (wrongParts.Count == 0) { Add(g, OK, "함선 부위 목록(Parts)이 부위 스크립트를 가리킵니다."); }
        else
        {
            Add(g, WARN, $"함선 부위 목록(Parts)에 부위 스크립트 대신 SaccEntity·SaccTarget이 들어간 칸: {string.Join(", ", wrongParts)} — 그 포탑은 본체가 격침돼도 계속 쏘고 다음 미션에 함께 부활하지 않습니다(오브젝트를 칸에 끌어 넣으면 첫 컴포넌트 SaccEntity가 들어간다). [고치기]가 같은 오브젝트의 ACO_SAMSite·ACO_AAGunSite로 바꿉니다.", null, ACO_ShipParts.FixAll);
        }
    }

    private void CheckCrew()
    {
        const string g = "갑판 요원";
        List<Transform> triggers = ACO_SceneBaker.CatapultTriggers();
        if (triggers.Count == 0) { return; }
        List<ACO_CatapultCrew> crews = FindAll<ACO_CatapultCrew>();
        var problems = new List<string>();
        int noCrew = triggers.FindAll(t => !crews.Exists(c => c.Trigger == t)).Count;
        if (noCrew > 0) { problems.Add($"요원 없는 캐터펄트 {noCrew}곳"); }
        if (crews.Exists(c => c.Jbd == null)) { problems.Add("JBD 미연결"); }
        if (FindOne<ACO_LaunchOperator>() == null) { problems.Add("지휘소 요원 없음"); }
        if (!ACO_PrefabBuilder.CrewClipsCurrent()) { problems.Add("애니메이션이 옛 판"); }
        if (problems.Count == 0) { Add(g, OK, $"갑판 요원 {crews.Count}조가 설치되어 있습니다."); return; }
        Add(g, WARN, string.Join(", ", problems), null, ACO_SceneBaker.InstallCatapultCrew);
    }


    /// 패널 프리팹을 다시 만들면 예전 Bake가 씬에 더한 오브젝트와 겹친다. 옛 스크롤 버튼은 더 쓰지 않는다.
    private void CheckBoard()
    {
        const string g = "패널";
        foreach (Transform t in FindAll<Transform>())
        {
            bool scroll = t.name == ACO_HUDBuilder.LogScrollName;
            if (!scroll && t.name != ACO_HUDBuilder.FlowCountdownName && t.name != "SpectatorRain" && t.name != "RADAR") { continue; }
            if (!PrefabUtility.IsAddedGameObjectOverride(t.gameObject)) { continue; }
            if (!scroll && !HasPrefabTwin(t)) { continue; }
            GameObject old = t.gameObject;
            Add(g, WARN, $"씬에만 있는 옛 '{old.name}'이 프리팹의 것과 겹칩니다. 고치기를 누르면 지웁니다(Undo 가능).", old, () => Undo.DestroyObjectImmediate(old));
            _items[_items.Count - 1].Manual = true;
        }
    }

    private static bool HasPrefabTwin(Transform t)
    {
        foreach (Transform sibling in t.parent)
        {
            if (sibling != t && sibling.name == t.name && !PrefabUtility.IsAddedGameObjectOverride(sibling.gameObject)) { return true; }
        }
        return false;
    }

    /// 씬에 하나씩 있어야 하는 부품과 코드가 늘면 맞춰야 하는 표.
    private void CheckSceneParts()
    {
        const string g = "씬 부품";
        ACO_MissionManager manager = FindOne<ACO_MissionManager>();
        if (manager != null)
        {
            List<string> modules = ACO_PhaseModuleSetup.MissingBossModules(manager);
            int noWeights = ACO_PhaseModuleSetup.CountEmptyWeights(manager);
            if (modules.Count > 0 || noWeights > 0)
            {
                string text = modules.Count > 0 ? $"빠진 페이즈 모듈: {string.Join(", ", modules)}" : $"가중치가 빈 페이즈 모듈 {noWeights}개";
                Add(g, WARN, text, manager, () => ACO_PhaseModuleSetup.AddBossModules(manager));
            }
        }
        ACO_Underwater underwater = FindOne<ACO_Underwater>();
        if (underwater != null && (underwater.Tint == null || underwater.Backdrop == null))
        {
            Add(g, WARN, "물속 연출의 물빛 구나 벽이 없습니다.", underwater, () => ACO_WaterFxBuilder.EnsureUnderwaterParts(underwater));
        }
        if (FindOne<ACO_UiSound>() == null)
        {
            Add(g, WARN, "패널 버튼 효과음(ACO_UiSound)이 없습니다.", null, () => { bool created; ACO_HUDBuilder.EnsureUiSound(out created); });
        }
        if (ACO_PrefabTuning.IsApplied("fx-death-1"))
        {
            int noDeath = ACO_FxBuilder.CountMissingDeathFx();
            if (noDeath > 0)
            {
                Add(g, WARN, $"NPC 프리팹 {noDeath}개에 파괴 효과가 없습니다(NPC 프리팹을 다시 만들면 빠집니다).", null, ACO_FxBuilder.ApplyToDeath);
            }
        }
        if (ACO_SceneBaker.BulletFlybyMissing())
        {
            Add(g, WARN, "총탄이 스치는 소리(BulletFlyby)가 없습니다.", null, ACO_SceneBaker.InstallBulletFlyby);
        }
        List<GameObject> noIgnore = ACO_SceneBaker.AirUnitRoots().FindAll(go => go.GetComponent<ACO_AirCollisionIgnore>() == null);
        if (noIgnore.Count > 0)
        {
            Add(g, WARN, $"공중 유닛 {noIgnore.Count}대에 충돌 방지가 없습니다.", noIgnore[0], () =>
            {
                foreach (GameObject go in noIgnore) { go.AddUdonSharpComponent<ACO_AirCollisionIgnore>(); }
            });
        }
        List<ACO_VehicleDecal> sharedDecals = ACO_SceneBaker.DecalsSharingMaterials();
        if (sharedDecals.Count > 0)
        {
            Add(g, ERROR, $"기체 {sharedDecals.Count}대가 다른 기체와 데칼 머티리얼을 같이 씁니다. 다른 사람의 데칼 이미지가 내 기체에 보입니다(기체 프리팹 안에 데칼이 들어가 있으면 생긴다).",
                sharedDecals[0], ACO_SceneBaker.SplitDecalMaterials);
        }
        int noColliders = ACO_SceneBaker.DecalSampleMeshesMissing();
        if (noColliders > 0)
        {
            Add(g, WARN, $"데칼 샘플 기체 메시 {noColliders}개에 데칼 건용 콜라이더가 없습니다.", null, ACO_SceneBaker.AddDecalSampleColliders);
        }
        List<ACO_RWR> rwrs = FindAll<ACO_RWR>();
        if (rwrs.Count > 1)
        {
            Add(g, WARN, $"RWR이 {rwrs.Count}개입니다. Bake는 첫 번째만 연결합니다. 쓰지 않는 쪽은 지우거나 끄세요.", rwrs[1]);
        }

        if (ACO_SceneBaker.TerrainArraysShort())
        {
            Add(g, WARN, "지형 프리셋이 늘어 이름·비율 표가 짧습니다. 고치기는 새 칸만 더합니다.", null, ACO_SceneBaker.GrowTerrainArrays);
        }
        ACO_Chatter chatter = FindOne<ACO_Chatter>();
        if (chatter != null && ACO_SceneBaker.ChatterTableShort(chatter))
        {
            Add(g, WARN, "코드에 새 무전 대사가 있습니다. 고치기는 표 끝에 더합니다.", chatter, () => ACO_SceneBaker.AppendMissingChatterLines(chatter));
        }
        ACO_PlayerProgress progress = FindOne<ACO_PlayerProgress>();
        if (progress != null && !ACO_SceneBaker.UpgradeArraysCurrent(progress))
        {
            Add(g, WARN, "강화 항목 수가 바뀌었습니다. 고치기를 누르면 강화 이름·배수·가격이 모두 기본값으로 돌아갑니다.", progress, () => ACO_SceneBaker.MigrateUpgradeArrays(progress));
            _items[_items.Count - 1].Manual = true;
        }
    }

    private void CheckSky()
    {
        const string g = "하늘";
        List<ACO_Weather> all = FindAll<ACO_Weather>();
        if (all.Count == 0) { return; }
        List<ACO_Weather> active = all.FindAll(w => w.gameObject.activeInHierarchy);
        if (active.Count == 0)
        {
            ACO_Weather first = all[0];
            Add(g, ERROR, "하늘(ACO_Sky)이 모두 꺼져 있어 날씨가 맑음에서 바뀌지 않습니다.", first, () => SetSkyActive(first, true));
        }
        else if (active.Count > 1)
        {
            List<ACO_Weather> extra = active.GetRange(1, active.Count - 1);
            Add(g, WARN, $"켜진 하늘(ACO_Sky)이 {active.Count}개라 날씨가 따로 돕니다. 고치기를 누르면 '{active[0].transform.parent?.name}'의 하늘만 남기고 끕니다.", extra[0], () =>
            {
                foreach (ACO_Weather w in extra) { SetSkyActive(w, false); }
            });
            _items[_items.Count - 1].Manual = true;
        }
        else
        {
            Add(g, OK, "하늘(ACO_Sky)이 하나만 켜져 있습니다.");
        }

        ACO_Weather main = active.Count > 0 ? active[0] : all[0];
        ACO_CloudField field = main.Clouds;
        if (field != null && (field.SkyFog == null || field.DeckBottomRenderer == null || field.DeckTopRenderer == null
            || field.FogTopRenderer == null || field.CloudVolume == null || field.CloudVeil == null))
        {
            Add(g, WARN, "하늘 안개 돔·구름층·물안개 막 중 빠진 부품이 있습니다.", field, () => ACO_SkyBuilder.EnsureSkyParts(field));
        }
        if (main.Lightning == null || (main.Rain != null && main.Rain.CanopyRenderer == null))
        {
            Add(g, WARN, "캐노피 빗방울이나 번개가 없습니다.", main, () => ACO_SkyBuilder.EnsureStormParts(main));
        }
    }

    private static void SetSkyActive(ACO_Weather weather, bool on)
    {
        GameObject go = weather.gameObject;
        Undo.RecordObject(go, "ACO sky");
        go.SetActive(on);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        EditorUtility.SetDirty(go);
    }

    /// 패널 생성 코드가 바뀌면 그 프리팹의 판을 올린다. 다시 만들면 직접 고친 내용이 사라지므로 하나씩 누른다.
    private void CheckPrefabVersions()
    {
        const string g = "프리팹 판";
        foreach (ACO_PrefabVersions.Entry entry in ACO_PrefabVersions.Entries)
        {
            if (!ACO_PrefabVersions.IsOutdated(entry)) { continue; }
            ACO_PrefabVersions.Entry e = entry;
            Add(g, WARN, $"'{e.Name}'가 구버전입니다: {e.Change} [고치기]로 다시 만듭니다. 프리팹을 직접 고친 내용은 사라집니다.", ACO_PrefabVersions.Asset(e), e.Build);
            _items[_items.Count - 1].Manual = true;
        }
    }

    private void CheckTuning()
    {
        const string g = "프리팹 수치";
        foreach (ACO_PrefabTuning.Entry entry in ACO_PrefabTuning.Entries)
        {
            if (ACO_PrefabTuning.IsApplied(entry.Id)) { continue; }
            ACO_PrefabTuning.Entry e = entry;
            Add(g, WARN, e.Text, null, () => ACO_PrefabTuning.Apply(e));
            _items[_items.Count - 1].IgnoreId = e.Id;
        }
    }
}
