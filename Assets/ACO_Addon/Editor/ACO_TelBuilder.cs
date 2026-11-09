using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// TEL NPC_TEL: 시한 표적 전용. 모델은 ACO_TelModel.cs가 넣는다(FBX가 없으면 임시 큐브). 구조는 ACO_TelUnit 설명 참고 —
/// 루트(SaccTarget·히트박스 둘·락온 마커 둘) / Chassis(발사 자리에 남는다) / Rail(일어난다, 실린 탄도탄) / FlyingMissile(발사 후) / Blast.
public static partial class ACO_PrefabBuilder
{
    public static GameObject BuildTel()
    {
        EnsurePrefabFolder();
        Material flameMat = FortressMaterial("ACO_FortressFlame", Color.black, 0f, 0f, new Color(0.5f, 1.4f, 3f));

        var root = new GameObject("NPC_TEL");
        root.layer = LayerHitbox;
        BoxCollider chassisBox = root.AddComponent<BoxCollider>();
        chassisBox.size = new Vector3(6f, 5f, 18f);
        chassisBox.center = new Vector3(0f, 2.5f, 0f);
        BoxCollider missileBox = root.AddComponent<BoxCollider>();
        missileBox.size = new Vector3(5f, 20f, 5f);
        missileBox.center = new Vector3(0f, 12f, 0f);
        missileBox.enabled = false;

        var chassis = new GameObject("Chassis").transform;
        chassis.SetParent(root.transform, false);
        AddBox(chassis, "Bed", new Vector3(0f, 1.5f, 0f), new Vector3(4f, 1.6f, 16f));
        AddBox(chassis, "Cab", new Vector3(0f, 2.6f, 6.5f), new Vector3(3.6f, 2.4f, 3f));

        // 레일: 뒤쪽을 축으로 앞이 일어난다(축 기준 +Z로 눕혀 둔다)
        var rail = new GameObject("Rail").transform;
        rail.SetParent(chassis, false);
        rail.localPosition = new Vector3(0f, 2.6f, -7f);
        AddBox(rail, "RailBeam", new Vector3(0f, 0f, 6.5f), new Vector3(2f, 0.5f, 13f));
        var railMissile = new GameObject("RailMissile");
        railMissile.transform.SetParent(rail, false);
        AddBox(railMissile.transform, "Body", new Vector3(0f, 1f, 6.5f), new Vector3(1.4f, 1.4f, 12f));

        var flying = new GameObject("FlyingMissile");
        flying.transform.SetParent(root.transform, false);
        AddBox(flying.transform, "Body", new Vector3(0f, 12f, 0f), new Vector3(1.4f, 14f, 1.4f));
        GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flame.name = "Flame";
        flame.layer = LayerHitbox;
        Object.DestroyImmediate(flame.GetComponent<Collider>());
        flame.transform.SetParent(flying.transform, false);
        flame.transform.localPosition = new Vector3(0f, 2f, 0f);
        flame.transform.localScale = new Vector3(2f, 7f, 2f);
        flame.GetComponent<MeshRenderer>().sharedMaterial = flameMat;
        CreateFortressSmoke(flying.transform, "Trail", new Vector3(0f, 0f, 0f), 3f, 200f);
        flying.SetActive(false);

        GameObject blast = CreateFortressSmoke(root.transform, "Blast", new Vector3(0f, 8f, 0f), 10f, 80f);
        blast.SetActive(false);

        GameObject ground = NewLockMarker(root.transform, "AAMTarget", LayerEnemyGround, 6f);
        ground.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        GameObject air = NewLockMarker(root.transform, "AAMTargetAir", LayerEnemyAir, 6f);
        air.transform.localPosition = new Vector3(0f, 12f, 0f);
        air.SetActive(false);

        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_TelUnit tel = root.AddUdonSharpComponent<ACO_TelUnit>();
        health.Health = 200f;   // 공대지·공대공 미사일 한 발
        health.ArmorStrength = 1f;
        health.RespawnDelay = 0f;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { tel };

        tel.TargetHealth = health;
        tel.Chassis = chassis;
        tel.Rail = rail;
        tel.RailMissile = railMissile;
        tel.FlyingMissile = flying;
        tel.GroundMarker = ground;
        tel.AirMarker = air;
        tel.ChassisHitbox = chassisBox;
        tel.MissileHitbox = missileBox;
        tel.BlastEffect = blast;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TelFbx) != null) { ApplyTelModel(root); }
        return SaveAndCleanup(root, $"{PrefabDir}/NPC_TEL.prefab");
    }
}
