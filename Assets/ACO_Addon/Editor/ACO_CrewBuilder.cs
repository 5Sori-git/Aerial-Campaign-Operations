using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ACO;

/// 갑판 요원: 수신호 클립, 애니메이터 컨트롤러, 프리팹 NPC_CatapultCrew.
/// 자세는 팔다리 뼈의 방향(캐릭터 기준 x = 오른쪽, y = 위, z = 앞)으로 적고, 실제 모델에 입혀 Unity가 Humanoid 값으로 바꾸게 한다 —
/// 근육 수치를 어림하지 않아 눈으로 보지 않고도 방향이 맞고, 자세를 고칠 때도 방향만 고치면 된다.
/// 표를 고치면 CrewClipVersion을 올린다 — Setup Check가 Crew_*.anim을 다시 만든다(수동: 메뉴 ACO → Deck Crew → Rebuild Animations).
/// 컨트롤러는 없는 상태만 더하므로 상태의 클립을 직접 바꿔 끼워도 남는다. 프리팹도 없을 때만 만든다(모델·색을 직접 고쳐도 남는다).
public static partial class ACO_PrefabBuilder
{
    private const string CrewAnimDir = "Assets/ACO_Addon/animation/Crew";
    private const string CrewControllerPath = CrewAnimDir + "/ACO_Crew.controller";
    private const string CrewModelPath = "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Pilot_Male_01.prefab";
    private const string CrewPrefab = "NPC_CatapultCrew";
    // 요원 모델(EEI DeckCrewSet, 역할별 색). 클립은 Humanoid라 그대로 맞는다
    private const string CrewSetDir = "Assets/EEIproducts/Other/DeckCrewSet/Deck Crew/Prefab/liltoon/";
    private const string CrewDirectorModel = "DeckCrewSet_Yellow.prefab";
    private const string CrewShooterModel = "DeckCrewSet_Shooter.prefab";
    private const string CrewCheckerModel = "DeckCrewSet_FinalChecker Variant.prefab";
    private const string CrewGreenModel = "DeckCrewSet_Green.prefab";
    private const string CrewOperatorModel = "DeckCrewSet_White.prefab";
    private const string OperatorPrefab = "NPC_LaunchOperator";
    private const string CrewMaterialDir = "Assets/ACO_Addon/Generated/CrewMaterials";
    private const string CrewFbxPath = "Assets/EEIproducts/Other/DeckCrewSet/Deck Crew/FBX/DCS.fbx";
    private const float CrewHeadHeight = 1.6f;   // 발목에서 머리 뼈까지(m) — 키 약 1.8m
    private const int CrewClipVersion = 5;

    private const int HandOpen = 0;
    private const int HandFist = 1;
    private const int HandPoint = 2;
    private const int HandThumb = 3;

    /// 자세 하나. 값은 뼈가 뻗는 방향이고 길이는 상관없다.
    private sealed class CrewPose
    {
        public Vector3 LeftArm = new Vector3(-0.18f, -1f, 0f);
        public Vector3 LeftFore = new Vector3(-0.1f, -1f, 0.15f);
        public Vector3 RightArm = new Vector3(0.18f, -1f, 0f);
        public Vector3 RightFore = new Vector3(0.1f, -1f, 0.15f);
        public Vector3 LeftThigh = new Vector3(-0.07f, -1f, 0f);
        public Vector3 LeftShin = new Vector3(-0.02f, -1f, -0.02f);
        public Vector3 RightThigh = new Vector3(0.07f, -1f, 0f);
        public Vector3 RightShin = new Vector3(0.02f, -1f, -0.02f);
        public float Lean;        // 도, + = 앞으로
        public float HeadPitch;   // 도, + = 아래로
        public float HeadYaw;     // 도, + = 오른쪽으로
        public bool RightThumbSky; // 오른손 엄지 쪽이 하늘을 보게 손목을 돌리고 꺾는다
        public int LeftHand = HandOpen;
        public int RightHand = HandOpen;
    }

    private struct CrewFrame
    {
        public float[] Muscles;
        public Vector3 BodyPosition;
        public Quaternion BodyRotation;
    }

    private struct CrewKey
    {
        public float Time;
        public CrewPose Pose;
        public CrewKey(float time, CrewPose pose) { Time = time; Pose = pose; }
    }

    private static Vector3 MirrorX(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }

    // ───────────── 동작 표 ─────────────

    private static CrewKey[] CrewIdle()
    {
        var a = new CrewPose();
        var b = new CrewPose { Lean = 1.5f, HeadPitch = 3f };
        return new[] { new CrewKey(0f, a), new CrewKey(1.6f, b), new CrewKey(3.2f, a) };
    }

    // 날개 밑과 흡기구 옆을 지나므로 허리와 무릎을 굽혀 낮게 움직인다
    private static CrewPose WalkPose(bool leftForward, bool contact)
    {
        float s = leftForward ? 1f : -1f;
        var p = new CrewPose
        {
            Lean = 38f, HeadPitch = -10f, LeftHand = HandFist, RightHand = HandFist,
            LeftArm = new Vector3(-0.25f, -0.9f, 0.45f - 0.2f * s), LeftFore = new Vector3(-0.1f, -0.65f, 0.75f),
            RightArm = new Vector3(0.25f, -0.9f, 0.45f + 0.2f * s), RightFore = new Vector3(0.1f, -0.65f, 0.75f),
        };
        if (contact)
        {
            Vector3 frontThigh = new Vector3(0f, -0.7f, 0.75f);
            Vector3 frontShin = new Vector3(0f, -1f, 0.15f);
            Vector3 backThigh = new Vector3(0f, -1f, -0.1f);
            Vector3 backShin = new Vector3(0f, -0.75f, -0.75f);
            p.LeftThigh = s > 0f ? frontThigh : backThigh;
            p.LeftShin = s > 0f ? frontShin : backShin;
            p.RightThigh = s > 0f ? backThigh : frontThigh;
            p.RightShin = s > 0f ? backShin : frontShin;
        }
        else
        {
            // 굽힌 한 다리로 서고 다른 다리가 앞으로 지나간다
            Vector3 standThigh = new Vector3(0f, -0.85f, 0.45f);
            Vector3 standShin = new Vector3(0f, -1f, -0.2f);
            Vector3 swingThigh = new Vector3(0f, -0.75f, 0.6f);
            Vector3 swingShin = new Vector3(0f, -0.7f, -0.8f);
            p.LeftThigh = s > 0f ? standThigh : swingThigh;
            p.LeftShin = s > 0f ? standShin : swingShin;
            p.RightThigh = s > 0f ? swingThigh : standThigh;
            p.RightShin = s > 0f ? swingShin : standShin;
        }
        return p;
    }

    // 왼무릎을 갑판에 댄 자세. 팔은 왼손을 앞 무릎에 얹고 오른팔은 내린다
    private static CrewPose KneelPose()
    {
        return new CrewPose
        {
            LeftThigh = new Vector3(-0.08f, -1f, -0.15f), LeftShin = new Vector3(0f, -0.15f, -1f),
            RightThigh = new Vector3(0.15f, -0.25f, 1f), RightShin = new Vector3(0.05f, -1f, 0f),
            Lean = 6f,
            LeftArm = new Vector3(0.05f, -0.8f, 0.6f), LeftFore = new Vector3(0.35f, -0.55f, 0.75f), LeftHand = HandFist,
            RightArm = new Vector3(0.2f, -1f, 0.2f), RightFore = new Vector3(0.1f, -0.8f, 0.6f), RightHand = HandFist,
        };
    }

    private static CrewKey[] CrewSit()
    {
        CrewPose a = KneelPose();
        return new[] { new CrewKey(0f, a), new CrewKey(1f, a) };
    }

    private static CrewKey[] CrewWalk()
    {
        const float t = 0.17f;
        CrewPose first = WalkPose(true, true);
        return new[]
        {
            new CrewKey(0f, first), new CrewKey(t, WalkPose(false, false)),
            new CrewKey(t * 2f, WalkPose(false, true)), new CrewKey(t * 3f, WalkPose(true, false)),
            new CrewKey(t * 4f, first),
        };
    }

    private static CrewKey[] CrewKneel()
    {
        // 왼무릎을 대고 앉아 오른손을 앞으로 뻗어 살핀다
        var a = new CrewPose
        {
            LeftThigh = new Vector3(-0.08f, -1f, -0.15f),
            LeftShin = new Vector3(0f, -0.15f, -1f),
            RightThigh = new Vector3(0.15f, -0.25f, 1f),
            RightShin = new Vector3(0.05f, -1f, 0f),
            Lean = 28f,
            HeadPitch = 18f,
            RightArm = new Vector3(0.25f, -0.55f, 1f),
            RightFore = new Vector3(0.1f, -0.55f, 1f),
            LeftArm = new Vector3(-0.3f, -1f, 0.25f),
            LeftFore = new Vector3(0.15f, -0.5f, 1f),
        };
        var b = new CrewPose
        {
            LeftThigh = a.LeftThigh, LeftShin = a.LeftShin, RightThigh = a.RightThigh, RightShin = a.RightShin,
            Lean = 32f, HeadPitch = 24f,
            RightArm = new Vector3(0.1f, -0.45f, 1f), RightFore = new Vector3(-0.1f, -0.6f, 1f),
            LeftArm = a.LeftArm, LeftFore = a.LeftFore,
        };
        return new[] { new CrewKey(0f, a), new CrewKey(0.6f, b), new CrewKey(1.2f, a) };
    }

    // 두 팔을 앞으로 들고 팔꿈치 아래를 까딱여 부른다
    private static CrewPose Beckon(CrewPose p, bool left, bool right, float wave)
    {
        Vector3 arm = new Vector3(0.55f, 0.1f, 0.85f);
        Vector3 fore = Vector3.Lerp(new Vector3(0.25f, 0.9f, 0.5f), new Vector3(0.1f, 0.75f, -0.5f), wave);
        if (right) { p.RightArm = arm; p.RightFore = fore; }
        if (left) { p.LeftArm = MirrorX(arm); p.LeftFore = MirrorX(fore); }
        return p;
    }

    private static CrewKey[] CrewCome()
    {
        CrewPose a = Beckon(new CrewPose(), true, true, 0f);
        return new[] { new CrewKey(0f, a), new CrewKey(0.45f, Beckon(new CrewPose(), true, true, 1f)), new CrewKey(0.9f, a) };
    }

    /// 마주 보는 유도요원의 오른팔 = 조종사의 왼쪽. pilotLeft면 오른팔로 방향을 가리키고 왼팔로 부른다.
    private static CrewKey[] CrewTurn(bool pilotLeft)
    {
        Vector3 point = new Vector3(1f, 0.08f, 0.1f);
        CrewPose Make(float wave)
        {
            var p = new CrewPose { RightHand = HandPoint, LeftHand = HandPoint };
            Beckon(p, pilotLeft, !pilotLeft, wave);
            if (pilotLeft) { p.RightArm = point; p.RightFore = point; }
            else { p.LeftArm = MirrorX(point); p.LeftFore = MirrorX(point); }
            return p;
        }
        CrewPose a = Make(0f);
        return new[] { new CrewKey(0f, a), new CrewKey(0.45f, Make(1f)), new CrewKey(0.9f, a) };
    }

    private static CrewKey[] CrewSlow()
    {
        // 손바닥을 아래로 눌러 내린다
        CrewPose Make(float h)
        {
            return new CrewPose
            {
                RightArm = new Vector3(0.45f, -0.35f + h * 0.4f, 1f), RightFore = new Vector3(0.3f, -0.05f + h * 0.4f, 1f),
                LeftArm = new Vector3(-0.45f, -0.35f + h * 0.4f, 1f), LeftFore = new Vector3(-0.3f, -0.05f + h * 0.4f, 1f),
            };
        }
        CrewPose a = Make(1f);
        return new[] { new CrewKey(0f, a), new CrewKey(0.5f, Make(0f)), new CrewKey(1f, a) };
    }

    private static CrewKey[] CrewStop()
    {
        // 머리 위에서 주먹을 교차한다
        var p = new CrewPose
        {
            RightArm = new Vector3(0.6f, 0.85f, 0.1f), RightFore = new Vector3(-0.75f, 0.65f, 0.05f),
            LeftArm = new Vector3(-0.6f, 0.85f, 0.1f), LeftFore = new Vector3(0.75f, 0.65f, 0.12f),
            RightHand = HandFist, LeftHand = HandFist,
        };
        return new[] { new CrewKey(0f, p), new CrewKey(1f, p) };
    }

    private static CrewKey[] CrewThumb()
    {
        // 무릎을 꿇고 팔은 수평에서 50도쯤만 든 채, 손목을 꺾어 엄지가 하늘을 보게 한다
        CrewPose up = KneelPose();
        up.HeadPitch = -6f;
        up.RightArm = new Vector3(0.25f, 0.77f, 0.6f);
        up.RightFore = new Vector3(0.25f, 0.77f, 0.6f);
        up.RightHand = HandThumb;
        up.RightThumbSky = true;
        return new[] { new CrewKey(0f, KneelPose()), new CrewKey(0.35f, up), new CrewKey(1f, up) };
    }

    // 패널 앞에 서서 양손을 패널에 올린다. 오른손 · 왼손 · 양손 순으로 옮기며 바깥을 살핀다
    private static CrewPose ConsolePose(float right, float left, float yaw)
    {
        return new CrewPose
        {
            Lean = 12f, HeadPitch = 2f, HeadYaw = yaw,
            RightArm = new Vector3(0.22f, -0.9f, 0.45f), RightFore = new Vector3(0.12f + right, -0.35f, 1f), RightHand = HandPoint,
            LeftArm = new Vector3(-0.22f, -0.9f, 0.45f), LeftFore = new Vector3(-0.12f + left, -0.35f, 1f), LeftHand = HandOpen,
        };
    }

    private static CrewKey[] CrewOperate()
    {
        CrewPose a = ConsolePose(0f, 0f, 0f);
        return new[]
        {
            new CrewKey(0f, a), new CrewKey(1.2f, ConsolePose(0.25f, 0f, -14f)), new CrewKey(2.6f, ConsolePose(0.05f, 0.2f, 16f)),
            new CrewKey(3.8f, ConsolePose(-0.1f, 0f, 0f)), new CrewKey(5f, a),
        };
    }

    private static CrewKey[] CrewPress()
    {
        // 사출 버튼을 누른 채 앞을 본다
        CrewPose ready = ConsolePose(0f, 0f, 0f);
        CrewPose down = ConsolePose(0f, 0f, 0f);
        down.Lean = 16f;
        down.HeadPitch = -6f;
        down.RightArm = new Vector3(0.1f, -0.7f, 0.75f);
        down.RightFore = new Vector3(0.02f, -0.7f, 0.75f);
        return new[] { new CrewKey(0f, ready), new CrewKey(0.5f, down), new CrewKey(1f, down) };
    }

    private static CrewKey[] CrewThrottle()
    {
        // 손을 머리 위로 올려 돌린다 — 엔진을 올리라는 신호
        CrewPose Make(float x, float z)
        {
            return new CrewPose
            {
                RightArm = new Vector3(0.3f, 1f, 0.15f), RightFore = new Vector3(0.1f + x, 1f, 0.1f + z),
                RightHand = HandPoint, HeadPitch = -4f,
            };
        }
        CrewPose a = Make(0.3f, 0f);
        return new[]
        {
            new CrewKey(0f, a), new CrewKey(0.15f, Make(0f, 0.3f)), new CrewKey(0.3f, Make(-0.3f, 0f)),
            new CrewKey(0.45f, Make(0f, -0.3f)), new CrewKey(0.6f, a),
        };
    }

    private static CrewKey[] CrewPoint()
    {
        // 손가락으로 요원을 가리킨다(거의 수평). 한 명씩 몸을 돌리는 동안 손을 작게 끊어 친다 — 몸은 ACO_CatapultCrew가 돌린다
        CrewPose Make(float y)
        {
            return new CrewPose
            {
                RightArm = new Vector3(0.05f, y, 1f), RightFore = new Vector3(0f, y, 1f), RightHand = HandPoint,
                LeftArm = new Vector3(-0.35f, -1f, -0.1f), LeftFore = new Vector3(0.45f, -0.6f, 0.5f), LeftHand = HandFist,
            };
        }
        CrewPose a = Make(-0.02f);
        return new[] { new CrewKey(0f, a), new CrewKey(0.2f, Make(-0.14f)), new CrewKey(0.6f, a) };
    }

    private static CrewKey[] CrewLaunch()
    {
        // 무릎을 꿇고, 고개를 돌려 전방을 확인한 뒤, 팔을 사출 방향으로 곧게 뻗는다
        CrewPose Look(float yaw)
        {
            CrewPose p = KneelPose();
            p.HeadYaw = yaw;
            p.HeadPitch = -8f;
            return p;
        }
        CrewPose thrust = Look(0f);
        thrust.Lean = 14f;
        thrust.RightArm = new Vector3(0.08f, 0.1f, 1f);
        thrust.RightFore = new Vector3(0.03f, 0.1f, 1f);
        thrust.RightHand = HandPoint;
        return new[]
        {
            new CrewKey(0f, new CrewPose()), new CrewKey(0.45f, Look(0f)), new CrewKey(0.8f, Look(-22f)),
            new CrewKey(1.15f, Look(22f)), new CrewKey(1.4f, Look(0f)), new CrewKey(1.75f, thrust), new CrewKey(2.6f, thrust),
        };
    }

    // ACO_CatapultCrew.SIG_* 순서
    private static readonly string[] CrewStates =
    {
        "Idle", "Walk", "Kneel", "Come", "TurnLeft", "TurnRight", "Slow", "Stop", "Thumb", "Throttle", "Point", "Launch", "Sit", "Operate", "Press",
    };

    private static CrewKey[] CrewClip(int signal)
    {
        switch (signal)
        {
            case ACO_CatapultCrew.SIG_WALK: return CrewWalk();
            case ACO_CatapultCrew.SIG_KNEEL: return CrewKneel();
            case ACO_CatapultCrew.SIG_COME: return CrewCome();
            case ACO_CatapultCrew.SIG_TURN_LEFT: return CrewTurn(true);
            case ACO_CatapultCrew.SIG_TURN_RIGHT: return CrewTurn(false);
            case ACO_CatapultCrew.SIG_SLOW: return CrewSlow();
            case ACO_CatapultCrew.SIG_STOP: return CrewStop();
            case ACO_CatapultCrew.SIG_THUMB: return CrewThumb();
            case ACO_CatapultCrew.SIG_THROTTLE: return CrewThrottle();
            case ACO_CatapultCrew.SIG_POINT: return CrewPoint();
            case ACO_CatapultCrew.SIG_LAUNCH: return CrewLaunch();
            case ACO_CatapultCrew.SIG_SIT: return CrewSit();
            case ACO_CatapultCrew.SIG_OPERATE: return CrewOperate();
            case ACO_CatapultCrew.SIG_PRESS: return CrewPress();
            default: return CrewIdle();
        }
    }

    private static bool CrewClipLoops(int signal)
    {
        return signal != ACO_CatapultCrew.SIG_LAUNCH && signal != ACO_CatapultCrew.SIG_THUMB && signal != ACO_CatapultCrew.SIG_PRESS;
    }

    // ───────────── 자세를 모델에 입혀 Humanoid 값으로 ─────────────

    private sealed class CrewRig
    {
        public Transform Root;
        public Animator Animator;
        public HumanPoseHandler Handler;
        public Transform[] All;
        public Quaternion[] BindRotation;
        public Vector3[] BindPosition;
        public Vector3 Right;
        public Vector3 Forward;
        public float FootHeight;
        public Quaternion LeftFootBind;
        public Quaternion RightFootBind;
        public string[] MuscleNames;

        public CrewRig(GameObject model)
        {
            Root = model.transform;
            Animator = model.GetComponent<Animator>();
            Handler = new HumanPoseHandler(Animator.avatar, Root);
            All = Root.GetComponentsInChildren<Transform>(true);
            BindRotation = new Quaternion[All.Length];
            BindPosition = new Vector3[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                BindRotation[i] = All[i].localRotation;
                BindPosition[i] = All[i].localPosition;
            }
            Vector3 across = Bone(HumanBodyBones.RightUpperArm).position - Bone(HumanBodyBones.LeftUpperArm).position;
            across.y = 0f;
            Right = across.normalized;
            Forward = Vector3.Cross(Right, Vector3.up);
            if (Vector3.Dot(Forward, Root.forward) < 0.5f)
            {
                Debug.LogWarning("[ACO] 갑판 요원 모델의 정면이 루트 +Z가 아닙니다 — 요원이 엉뚱한 쪽을 볼 수 있습니다.");
            }
            FootHeight = Mathf.Min(Bone(HumanBodyBones.LeftFoot).position.y, Bone(HumanBodyBones.RightFoot).position.y);
            LeftFootBind = Bone(HumanBodyBones.LeftFoot).rotation;
            RightFootBind = Bone(HumanBodyBones.RightFoot).rotation;
            MuscleNames = HumanTrait.MuscleName;
        }

        public Transform Bone(HumanBodyBones b) { return Animator.GetBoneTransform(b); }

        public Vector3 World(Vector3 d) { return (Right * d.x + Vector3.up * d.y + Forward * d.z).normalized; }

        public void Reset()
        {
            for (int i = 0; i < All.Length; i++)
            {
                All[i].localRotation = BindRotation[i];
                All[i].localPosition = BindPosition[i];
            }
        }

        public void Aim(HumanBodyBones bone, HumanBodyBones child, Vector3 direction)
        {
            Transform b = Bone(bone);
            Transform c = Bone(child);
            if (b == null || c == null) { return; }
            b.rotation = Quaternion.FromToRotation(c.position - b.position, World(direction)) * b.rotation;
        }

        public void Pitch(HumanBodyBones bone, float degrees)
        {
            Transform b = Bone(bone);
            if (b != null) { b.rotation = Quaternion.AngleAxis(degrees, Right) * b.rotation; }
        }

        public CrewFrame Capture(CrewPose p)
        {
            Reset();
            Pitch(HumanBodyBones.Spine, p.Lean * 0.5f);
            Pitch(HumanBodyBones.Chest, p.Lean * 0.5f);
            Pitch(HumanBodyBones.Head, p.HeadPitch - p.Lean * 0.6f);
            Transform head = Bone(HumanBodyBones.Head);
            if (head != null) { head.rotation = Quaternion.AngleAxis(p.HeadYaw, Vector3.up) * head.rotation; }
            Aim(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, p.LeftArm);
            Aim(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, p.LeftFore);
            Aim(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, p.RightArm);
            Aim(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, p.RightFore);
            if (p.RightThumbSky) { TurnThumbSideUp(); }
            Aim(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, p.LeftThigh);
            Aim(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, p.LeftShin);
            Aim(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, p.RightThigh);
            Aim(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, p.RightShin);
            Bone(HumanBodyBones.LeftFoot).rotation = LeftFootBind;
            Bone(HumanBodyBones.RightFoot).rotation = RightFootBind;
            // 무릎이나 발 중 가장 낮은 곳을 갑판에 맞춘다
            float foot = Mathf.Min(Bone(HumanBodyBones.LeftFoot).position.y, Bone(HumanBodyBones.RightFoot).position.y) - FootHeight;
            float knee = Mathf.Min(Bone(HumanBodyBones.LeftLowerLeg).position.y, Bone(HumanBodyBones.RightLowerLeg).position.y) - 0.08f;
            Bone(HumanBodyBones.Hips).position += Vector3.up * -Mathf.Min(foot, knee);

            var human = new HumanPose();
            Handler.GetHumanPose(ref human);
            var frame = new CrewFrame { Muscles = human.muscles, BodyPosition = human.bodyPosition, BodyRotation = human.bodyRotation };
            SetHand("Left", p.LeftHand, frame.Muscles);
            SetHand("Right", p.RightHand, frame.Muscles);
            return frame;
        }

        // 손바닥의 엄지 쪽 = 중지 뿌리 → 검지 뿌리
        private Vector3 ThumbSide()
        {
            return Bone(HumanBodyBones.RightIndexProximal).position - Bone(HumanBodyBones.RightMiddleProximal).position;
        }

        /// 팔뚝을 비틀어 엄지 쪽을 위로 돌리고(비틂은 위팔과 팔뚝이 나눠 맡는다), 남은 기울기는 손목으로 꺾는다.
        private void TurnThumbSideUp()
        {
            Transform upper = Bone(HumanBodyBones.RightUpperArm);
            Transform lower = Bone(HumanBodyBones.RightLowerArm);
            Transform hand = Bone(HumanBodyBones.RightHand);
            if (Bone(HumanBodyBones.RightIndexProximal) == null || Bone(HumanBodyBones.RightMiddleProximal) == null) { return; }
            Vector3 axis = (hand.position - lower.position).normalized;
            float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(ThumbSide(), axis), Vector3.ProjectOnPlane(Vector3.up, axis), axis);
            upper.rotation = Quaternion.AngleAxis(roll * 0.4f, axis) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis(roll * 0.6f, axis) * lower.rotation;
            hand.rotation = Quaternion.FromToRotation(ThumbSide(), Vector3.up) * hand.rotation;
        }

        // 손가락은 뼈보다 Humanoid 값이 확실하다(-1 = 다 굽힘, 1 = 다 폄). Synty 리그는 엄지·검지·중지만 있다
        private void SetHand(string side, int hand, float[] muscles)
        {
            float index = hand == HandOpen || hand == HandPoint ? 0.75f : -0.75f;
            float middle = hand == HandOpen ? 0.75f : -0.75f;
            float thumb = hand == HandOpen || hand == HandThumb ? 0.6f : -0.3f;
            if (hand == HandThumb) { thumb = 0.9f; }
            for (int i = 0; i < muscles.Length; i++)
            {
                string n = MuscleNames[i];
                // 엄지를 벌려 손가락과 떨어뜨린다
                if (hand == HandThumb && n == side + " Thumb Spread") { muscles[i] = 1f; }
                if (!n.StartsWith(side + " ") || !n.EndsWith("Stretched")) { continue; }
                if (n.Contains(" Index ")) { muscles[i] = index; }
                else if (n.Contains(" Middle ")) { muscles[i] = middle; }
                else if (n.Contains(" Thumb ")) { muscles[i] = thumb; }
            }
        }
    }

    // 클립의 속성 이름은 손가락만 다르다: "Left Index 1 Stretched" → "LeftHand.Index.1 Stretched"
    private static string CrewMuscleAttribute(string muscle)
    {
        Match m = Regex.Match(muscle, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
        return m.Success ? $"{m.Groups[1].Value}Hand.{m.Groups[2].Value}.{m.Groups[3].Value}" : muscle;
    }

    private static void SetCrewCurve(AnimationClip clip, string attribute, float[] times, float[] values)
    {
        var keys = new Keyframe[times.Length];
        for (int i = 0; i < times.Length; i++) { keys[i] = new Keyframe(times[i], values[i]); }
        var binding = new EditorCurveBinding { path = "", type = typeof(Animator), propertyName = attribute };
        AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
    }

    private static AnimationClip WriteCrewClip(CrewRig rig, int signal, bool overwrite)
    {
        string path = $"{CrewAnimDir}/Crew_{CrewStates[signal]}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip != null && !overwrite) { return clip; }
        if (clip == null)
        {
            clip = new AnimationClip { name = $"Crew_{CrewStates[signal]}" };
            AssetDatabase.CreateAsset(clip, path);
        }
        else { clip.ClearCurves(); }

        CrewKey[] keys = CrewClip(signal);
        int n = keys.Length;
        var times = new float[n];
        var frames = new CrewFrame[n];
        for (int i = 0; i < n; i++)
        {
            times[i] = keys[i].Time;
            frames[i] = rig.Capture(keys[i].Pose);
            // 쿼터니언 부호가 뒤집히면 몸이 한 바퀴 돈다
            if (i > 0 && Quaternion.Dot(frames[i - 1].BodyRotation, frames[i].BodyRotation) < 0f)
            {
                Quaternion q = frames[i].BodyRotation;
                frames[i].BodyRotation = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }
        }
        var values = new float[n];
        for (int m = 0; m < rig.MuscleNames.Length; m++)
        {
            for (int i = 0; i < n; i++) { values[i] = frames[i].Muscles[m]; }
            SetCrewCurve(clip, CrewMuscleAttribute(rig.MuscleNames[m]), times, values);
        }
        for (int axis = 0; axis < 7; axis++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 t = frames[i].BodyPosition;
                Quaternion r = frames[i].BodyRotation;
                values[i] = axis < 3 ? t[axis] : r[axis - 3];
            }
            string attribute = axis < 3 ? "RootT." + "xyz"[axis] : "RootQ." + "xyzw"[axis - 3];
            SetCrewCurve(clip, attribute, times, values);
        }

        // 몸의 높이·방향을 클립 그대로 쓴다(루트 모션을 쓰지 않는다 — 위치는 스크립트가 옮긴다)
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = CrewClipLoops(signal);
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimationClip[] BuildCrewClips(bool overwrite)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CrewModelPath);
        if (model == null)
        {
            Debug.LogError($"[ACO] 갑판 요원 모델이 없습니다: {CrewModelPath}");
            return null;
        }
        if (!AssetDatabase.IsValidFolder(CrewAnimDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/animation", "Crew"); }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = new CrewRig(go);
            var clips = new AnimationClip[CrewStates.Length];
            for (int i = 0; i < clips.Length; i++) { clips[i] = WriteCrewClip(rig, i, overwrite); }
            rig.Handler.Dispose();
            AssetDatabase.SaveAssets();
            // 버전은 첫 클립의 메타에 적어 둔다(프리팹·씬을 건드리지 않는다)
            AssetImporter stamp = AssetImporter.GetAtPath(CrewVersionClip);
            if (stamp != null && stamp.userData != CrewClipVersion.ToString())
            {
                stamp.userData = CrewClipVersion.ToString();
                stamp.SaveAndReimport();
            }
            return clips;
        }
        finally { Object.DestroyImmediate(go); }
    }

    private static RuntimeAnimatorController EnsureCrewController()
    {
        AnimationClip[] clips = BuildCrewClips(!CrewClipsCurrent());
        if (clips == null) { return null; }
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrewControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(CrewControllerPath);
            controller.AddParameter("Signal", AnimatorControllerParameterType.Int);
        }
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var existing = new HashSet<string>();
        foreach (ChildAnimatorState child in machine.states) { existing.Add(child.state.name); }
        for (int i = 0; i < clips.Length; i++)
        {
            if (existing.Contains(CrewStates[i])) { continue; }
            AnimatorState state = machine.AddState(CrewStates[i]);
            state.motion = clips[i];
            if (i == 0) { machine.defaultState = state; }
            AnimatorStateTransition t = machine.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.Equals, i, "Signal");
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.2f;
            t.canTransitionToSelf = false;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static string CrewVersionClip => $"{CrewAnimDir}/Crew_{CrewStates[0]}.anim";

    public static bool CrewClipsCurrent()
    {
        AssetImporter stamp = AssetImporter.GetAtPath(CrewVersionClip);
        return stamp == null || stamp.userData == CrewClipVersion.ToString();
    }

    /// 표가 바뀌었으면 Crew_*.anim을 다시 만들고, 컨트롤러에 없는 상태를 더한다.
    public static void EnsureCrewAnimations()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CrewModelPath) != null) { EnsureCrewController(); }
    }

    [MenuItem("ACO/Deck Crew/Rebuild Animations")]
    public static void RebuildCrewAnimations()
    {
        if (BuildCrewClips(true) != null) { Debug.Log($"[ACO] 갑판 요원 클립을 다시 만들었습니다: {CrewAnimDir}"); }
    }

    // ───────────── 프리팹 ─────────────

    /// 역할 모델. DeckCrewSet이 없으면 Synty 조종사로 만든다.
    private static GameObject CrewModel(string file)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CrewSetDir + file);
        return model != null ? model : AssetDatabase.LoadAssetAtPath<GameObject>(CrewModelPath);
    }

    private static Avatar CrewAvatar()
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(CrewFbxPath))
        {
            if (o is Avatar avatar) { return avatar; }
        }
        Debug.LogWarning($"[ACO] {CrewFbxPath}에 Avatar가 없습니다 — FBX의 Rig가 Humanoid인지 확인하세요.");
        return null;
    }

    /// DeckCrewSet 머티리얼은 lilToon이 없으면 분홍색이다. 그때는 같은 색·노멀 텍스처로 Standard 머티리얼을 만들어 끼운다.
    private static Material CrewMaterial(Material source)
    {
        if (source == null || (source.shader != null && source.shader.name != "Hidden/InternalErrorShader")) { return source; }
        string path = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(path)) { return source; }
        if (!AssetDatabase.IsValidFolder(CrewMaterialDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "CrewMaterials"); }
        string dest = $"{CrewMaterialDir}/{source.name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(dest);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Standard")) { name = source.name };
        // 셰이더가 없으면 값을 읽을 수 없어 머티리얼 파일에서 찾는다
        string text = File.ReadAllText(path);
        Texture2D main = CrewTexture(text, "_MainTex");
        if (main != null) { m.mainTexture = main; }
        Texture2D bump = CrewTexture(text, "_BumpMap");
        string bumpPath = bump != null ? AssetDatabase.GetAssetPath(bump) : "";
        if (AssetImporter.GetAtPath(bumpPath) is TextureImporter importer && importer.textureType == TextureImporterType.NormalMap)
        {
            m.SetTexture("_BumpMap", bump);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Glossiness", source.name.Contains("Lens") ? 0.85f : 0.3f);
        AssetDatabase.CreateAsset(m, dest);
        return m;
    }

    private static Texture2D CrewTexture(string materialText, string property)
    {
        Match found = Regex.Match(materialText, "- " + property + @":\s*\n\s*m_Texture: \{fileID: -?\d+, guid: (\w+)");
        return found.Success ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(found.Groups[1].Value)) : null;
    }

    private static void PrepareCrewModel(GameObject go, Animator a)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = CrewMaterial(mats[i]);
                changed |= m != mats[i];
                mats[i] = m;
            }
            if (!changed) { continue; }
            r.sharedMaterials = mats;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        if (!a.isHuman)
        {
            Debug.LogWarning($"[ACO] 갑판 요원 모델 '{go.name}'의 Avatar가 Humanoid가 아닙니다 — 동작이 재생되지 않습니다. FBX의 Rig를 확인하세요.");
            return;
        }
        // 모델 단위가 달라도 사람 크기로
        Transform head = a.GetBoneTransform(HumanBodyBones.Head);
        Transform foot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
        float height = head != null && foot != null ? head.position.y - foot.position.y : 0f;
        if (height > 0.01f && Mathf.Abs(CrewHeadHeight / height - 1f) > 0.1f) { go.transform.localScale *= CrewHeadHeight / height; }
    }

    private static Animator AddCrewMember(Transform root, GameObject model, RuntimeAnimatorController controller, string name, Vector3 pos, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        go.name = name;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        // DeckCrewSet 프리팹에는 Animator가 없다 — 달고 FBX의 Avatar를 넣는다
        Animator a = go.GetComponent<Animator>();
        if (a == null) { a = go.AddComponent<Animator>(); }
        if (a.avatar == null) { a.avatar = CrewAvatar(); }
        a.runtimeAnimatorController = controller;
        a.Rebind();
        PrepareCrewModel(go, a);
        a.applyRootMotion = false;
        a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        PrefabUtility.RecordPrefabInstancePropertyModifications(a);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        return a;
    }

    private static Transform[] AddCrewStops(Transform root, string side, Vector3[] points, float yaw)
    {
        var stops = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var go = new GameObject($"Stop_{side}_{i}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = points[i];
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            stops[i] = go.transform;
        }
        return stops;
    }

    /// 없으면 만든다. 기준점은 캐터펄트의 앞바퀴 자리(기체가 물리는 곳), +Z = 사출 방향.
    /// 사출 경로(x = 0, 날개 폭 ±7)를 비켜 선다. 두 캐터펄트 사이가 22m라 오른쪽에 유도·사출, 점검요원은 양옆.
    public static GameObject EnsureCatapultCrewPrefab()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(CrewPrefab));
        if (asset != null) { return asset; }
        RuntimeAnimatorController controller = EnsureCrewController();
        if (CrewModel(CrewDirectorModel) == null || controller == null) { return null; }
        EnsurePrefabFolder();
        var root = new GameObject(CrewPrefab);
        ACO_CatapultCrew crew = root.AddUdonSharpComponent<ACO_CatapultCrew>();
        crew.Director = AddCrewMember(root.transform, CrewModel(CrewDirectorModel), controller, "Director", new Vector3(9f, 0f, 26f), 200f);
        crew.Shooter = AddCrewMember(root.transform, CrewModel(CrewShooterModel), controller, "Shooter", new Vector3(9f, 0f, 8f), 250f);
        crew.CheckerLeft = AddCrewMember(root.transform, CrewModel(CrewCheckerModel), controller, "Checker_L", new Vector3(-10f, 0f, -4f), 90f);
        crew.CheckerRight = AddCrewMember(root.transform, CrewModel(CrewCheckerModel), controller, "Checker_R", new Vector3(10f, 0f, -2f), 270f);
        crew.StopsLeft = AddCrewStops(root.transform, "L", new[] { new Vector3(-2.2f, 0f, -0.4f), new Vector3(-3.4f, 0f, -6.8f) }, 90f);
        crew.StopsRight = AddCrewStops(root.transform, "R", new[] { new Vector3(2.2f, 0f, -0.4f), new Vector3(3.4f, 0f, -6.8f) }, 270f);
        AddCrewExtras(root.transform, crew, controller);
        Debug.Log("[ACO] 갑판 요원 프리팹을 만들었습니다: NPC_CatapultCrew — 자리·경유점·모델은 프리팹에서 고치면 됩니다.");
        return SaveAndCleanup(root, PathOf(CrewPrefab));
    }

    /// 추가 점검요원(오른쪽 — 왼쪽의 홀드백 요원 반대편)과 홀드백 요원, 펜던트.
    private static void AddCrewExtras(Transform root, ACO_CatapultCrew crew, RuntimeAnimatorController controller)
    {
        crew.CheckerExtra = AddCrewMember(root, CrewModel(CrewGreenModel), controller, "Checker_3", new Vector3(10f, 0f, -7f), 270f);
        crew.PendantCrew = AddCrewMember(root, CrewModel(CrewGreenModel), controller, "Pendant_Crew", new Vector3(-10f, 0f, -8f), 90f);
        crew.StopsExtra = AddCrewStops(root, "X", new[] { new Vector3(5.2f, 0f, -8.6f), new Vector3(2.6f, 0f, -12.2f) }, 270f);
        crew.StopsPendant = AddCrewStops(root, "P", new[] { new Vector3(-0.9f, 0f, -2.3f) }, 40f);

        // 앞바퀴 뒤에서 갑판으로 비스듬히 내려가는 막대. 모델은 Pendant를 바꾸면 되고, 자리는 PendantPoint·PendantGrip을 옮기면 된다
        var point = new GameObject("PendantPoint");
        point.transform.SetParent(root, false);
        point.transform.localPosition = new Vector3(0f, 0.3f, -0.8f);
        point.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
        crew.PendantPoint = point.transform;

        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rod.name = "HoldbackPendant";
        Object.DestroyImmediate(rod.GetComponent<Collider>());
        rod.GetComponent<MeshRenderer>().sharedMaterial = FortressMaterial("ACO_FortressPart", new Color(0.12f, 0.13f, 0.15f), 0.6f, 0.4f, Color.black);
        rod.transform.SetParent(root, false);
        rod.transform.localScale = new Vector3(0.05f, 0.4f, 0.05f);
        crew.Pendant = rod.transform;

        MakeCrewGrip(crew);
    }

    // 펜던트를 쥐는 자리: 홀드백 요원의 오른손 뼈 아래
    private static void MakeCrewGrip(ACO_CatapultCrew crew)
    {
        Transform hand = crew.PendantCrew.GetBoneTransform(HumanBodyBones.RightHand);
        Transform middle = crew.PendantCrew.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        if (hand == null) { return; }
        var grip = new GameObject("PendantGrip");
        grip.transform.SetParent(hand, false);
        if (middle != null) { grip.transform.position = hand.position + (middle.position - hand.position).normalized * 0.09f; }
        grip.transform.rotation = Quaternion.identity;
        grip.transform.localScale = Vector3.one / Mathf.Max(hand.lossyScale.x, 0.0001f);
        crew.PendantGrip = grip.transform;
    }

    private static Animator SwapCrewModel(Transform root, Animator old, string file, RuntimeAnimatorController controller)
    {
        Vector3 pos = old.transform.localPosition;
        Quaternion rot = old.transform.localRotation;
        int order = old.transform.GetSiblingIndex();
        string name = old.gameObject.name;
        Animator fresh = AddCrewMember(root, CrewModel(file), controller, name, pos, 0f);
        fresh.transform.localRotation = rot;
        fresh.transform.SetSiblingIndex(order);
        PrefabUtility.RecordPrefabInstancePropertyModifications(fresh.transform);
        Object.DestroyImmediate(old.gameObject);
        return fresh;
    }

    /// 발함지휘소 요원 프리팹. 없을 때만 만든다.
    public static GameObject EnsureLaunchOperatorPrefab()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(OperatorPrefab));
        if (asset != null) { return asset; }
        RuntimeAnimatorController controller = EnsureCrewController();
        GameObject model = CrewModel(CrewOperatorModel);
        if (controller == null || model == null) { return null; }
        var root = new GameObject(OperatorPrefab);
        ACO_LaunchOperator op = root.AddUdonSharpComponent<ACO_LaunchOperator>();
        op.Operator = AddCrewMember(root.transform, model, controller, "Operator", Vector3.zero, 0f);
        return SaveAndCleanup(root, PathOf(OperatorPrefab));
    }

    /// 먼저 만든 프리팹의 요원을 DeckCrewSet 모델로 바꾼다 — 자리·경유점·펜던트는 그대로, 펜던트 쥐는 자리만 새 손으로 옮긴다.
    public static void EnsureCatapultCrewModels()
    {
        string path = PathOf(CrewPrefab);
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CrewControllerPath);
        if (controller == null || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CrewSetDir + CrewDirectorModel) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_CatapultCrew crew = root.GetComponent<ACO_CatapultCrew>();
            if (crew == null || crew.Director == null || crew.PendantCrew == null) { return; }
            string current = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(crew.Director.gameObject);
            if (current != null && current.Contains("DeckCrewSet")) { return; }
            crew.Director = SwapCrewModel(root.transform, crew.Director, CrewDirectorModel, controller);
            crew.Shooter = SwapCrewModel(root.transform, crew.Shooter, CrewShooterModel, controller);
            crew.CheckerLeft = SwapCrewModel(root.transform, crew.CheckerLeft, CrewCheckerModel, controller);
            crew.CheckerRight = SwapCrewModel(root.transform, crew.CheckerRight, CrewCheckerModel, controller);
            crew.CheckerExtra = SwapCrewModel(root.transform, crew.CheckerExtra, CrewGreenModel, controller);
            crew.PendantCrew = SwapCrewModel(root.transform, crew.PendantCrew, CrewGreenModel, controller);
            MakeCrewGrip(crew);
            ACO_SyncModeFixer.FixHierarchy(root, false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 갑판 요원 모델을 DeckCrewSet으로 바꿨습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 먼저 만든 프리팹에 추가 요원과 펜던트를 넣는다 — 다시 만들면 프리팹을 직접 고친 내용이 사라진다.
    public static void EnsureCatapultCrewExtras()
    {
        string path = PathOf(CrewPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CrewControllerPath);
        if (controller == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_CatapultCrew crew = root.GetComponent<ACO_CatapultCrew>();
            if (crew == null || crew.PendantCrew != null) { return; }
            AddCrewExtras(root.transform, crew, controller);
            ACO_SyncModeFixer.FixHierarchy(root, false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 갑판 요원: 점검요원과 홀드백 요원, 펜던트를 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
