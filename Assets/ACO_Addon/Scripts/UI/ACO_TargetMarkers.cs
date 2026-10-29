using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 표적 마커(로컬 전용, 동기화 없음). 미션 생성기 풀의 살아 있는 유닛마다 테두리를 그린다.
    ///  - 항공기 = 사각형, 지상 유닛 = 육각형, 지금 페이즈 목표 = 빨간 테두리 + "TGT", 호위 대상(아군) = 파란 테두리 + 이름(VIP)
    ///    (목표와 아군은 ObjectiveMaxDistance까지 보인다)
    ///  - MaxDistance(10km) 안의 유닛만, 거리와 무관하게 항상 같은 화면 크기로, 지형/구름 뒤에서도 보이게 그린다
    ///  - 호위 페이즈(공중/지상)에는 호위 대상 항로의 남은 경유점(옅은 파란 마름모 + WP 번호)과 목적지(큰 마름모 + DEST + 거리)를 그린다
    ///  - 시선 방향(머리 정면)에서 LookAngle 안에 있는 유닛 중 가장 가운데 것 하나에 종류와 거리를 띄운다
    ///    (전투기: ACE/VET/NOV, 지상: SAM/AAG, 고가치 표적: FAC/BNK 등 — 각 유닛의 MarkerLabel)
    ///  - 락온(내가 조종 중인 기체의 ACO_DFUNC_Missile): 락온 대상이 되면 그 테두리가 깜빡이고 이름표(종류/거리/체력)가 그 표적에 고정된다.
    ///    조준하는 동안 마름모가 무작위 방향에서 다가오고(락온 진행도), 락온되면 테두리에 겹쳐지며 테두리와 함께 빨갛게 바뀐다.
    ///    다중 락온 무기(4AAM 류)는 락온 칸마다 따로 그린다(마름모 풀 LockDiamonds, 최대 MAX_LOCKS개).
    ///
    /// 같은 화면 크기: 마커를 실제 위치가 아니라 머리에서 그 방향으로 DisplayDistance(30m)만큼 떨어진 곳에 고정 크기로 놓는다.
    /// 그리기는 ZTest Always 셰이더(ACO/MarkerOverlay, TMP Distance Field Overlay)라 무엇에도 가려지지 않는다.
    /// 머리 위치는 매 프레임 PostLateUpdate에서 새로 읽으므로 고속으로 날아도 마커가 뒤처지지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_TargetMarkers : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 생성기를 자동으로 넣는다")]
        public ACO_MissionGenerator Generator;
        [Tooltip("빌드 시 ACO_SceneBaker가 넣는다. 있으면 지금 페이즈 목표(태그)만 빨간 TGT로, 없으면 고가치 표적을 목표로 표시")]
        public ACO_MissionManager Manager;

        [Header("표시")]
        [Tooltip("이 거리(m) 안의 유닛만 표시한다")]
        public float MaxDistance = 10000f;
        [Tooltip("지금 목표는 이 거리(m)까지 표시한다(목표를 찾아갈 수 있게)")]
        public float ObjectiveMaxDistance = 60000f;
        [Tooltip("목표 여부를 다시 확인하는 주기(초)")]
        public float ObjectiveRefreshInterval = 0.5f;
        [Tooltip("가까운 마커의 화면상 크기(도). NearDistance까지는 이 크기로 보인다")]
        public float MarkerAngle = 2.5f;
        [Tooltip("이 거리(m)까지는 MarkerAngle 크기 그대로, 그보다 멀면 점점 작아진다")]
        public float NearDistance = 1000f;
        [Tooltip("MaxDistance에서의 크기 배율. 그 사이는 거리의 로그에 비례해 줄어든다(가까이선 빠르게, 멀리선 완만하게 — 원근감)")]
        [Range(0.1f, 1f)] public float FarScale = 0.45f;
        [Tooltip("주요 목표 마커는 이 배율만큼 크게")]
        public float ObjectiveScale = 1.3f;
        [Tooltip("시선에서 이 각도(도) 안의 유닛에 종류/거리를 띄운다")]
        public float LookAngle = 4f;
        [Tooltip("마커를 그릴 가상 거리(m). 화면 크기에는 영향이 없고, 너무 가까우면 조종석 기물과 겹쳐 보일 수 있다")]
        public float DisplayDistance = 30f;

        [Header("크로스헤어 (화면 가운데 작은 원 — 시선 정보 보기의 기준)")]
        [Tooltip("시선 방향(VR이면 두 눈 사이 정면) DisplayDistance에 그리는 작은 원. 프리팹 빌더/Bake가 만든다")]
        public Transform Crosshair;
        [Tooltip("미션 중에 크로스헤어를 보여 준다(HUD 설정 패널에서 켜고 끈다)")]
        public bool ShowCrosshair = true;
        [Tooltip("크로스헤어 지름(도)")]
        public float CrosshairAngle = 1.2f;
        [Tooltip("크로스헤어·RWR 링을 그릴 가상 거리(m). 화면 크기는 그대로(각도 기준)이고 VR에서 보이는 깊이만 바뀐다. HUD 설정 패널이 바꾼다")]
        public float CrosshairDistance = 30f;

        [Header("모양/색 (프리팹 빌더가 채운다)")]
        public Mesh AirMesh;
        public Mesh GroundMesh;
        public Material AirMaterial;
        public Material GroundMaterial;
        public Material ObjectiveMaterial;
        [Tooltip("아군(호위 대상) 테두리. 비우면 항공기/지상 머티리얼")]
        public Material FriendlyMaterial;
        [Tooltip("목표 위 TGT 글자 색 / 아군 위 이름(VIP 등) 글자 색")]
        public Color ObjectiveLabelColor = new Color(1f, 0.25f, 0.2f, 0.9f);
        public Color FriendlyLabelColor = new Color(0.4f, 0.75f, 1f, 0.9f);

        [Header("호위 항로")]
        [Tooltip("호위 페이즈에 호위 대상의 남은 경유점과 목적지를 표시한다")]
        public bool ShowEscortRoute = true;
        [Tooltip("경유점/목적지 마름모 머티리얼(비우면 아군 머티리얼 — Bake가 채운다)")]
        public Material WaypointMaterial;
        public Material DestinationMaterial;
        public Color WaypointLabelColor = new Color(0.55f, 0.8f, 1f, 0.75f);
        public Color DestinationLabelColor = new Color(0.4f, 0.9f, 1f, 0.95f);
        [Tooltip("경유점 마커 크기 배율(목적지는 ObjectiveScale)")]
        public float WaypointScale = 0.7f;
        [Tooltip("목적지에서 이 거리(m) 안의 경유점은 그리지 않는다(착륙 접지점 등이 목적지와 겹치지 않게)")]
        public float WaypointMinGap = 1500f;

        [Header("마커 풀 (프리팹 빌더가 채운다)")]
        public Transform[] Markers;
        public MeshFilter[] Frames;
        public MeshRenderer[] FrameRenderers;
        public TextMeshPro[] TgtLabels;
        public TextMeshPro[] InfoLabels;

        [Header("락온 표시 (플레이어 미사일)")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 ACO_DFUNC_Missile을 넣는다. 내가 조종 중이고 고른 무기의 락온 상태를 그린다")]
        public ACO_DFUNC_Missile[] Weapons;
        [Tooltip("락온 진행 마름모 풀(사각 테두리 메시를 45° 돌려 쓴다). 락온 칸마다 하나. 빌드 시 모자라면 ACO_SceneBaker가 채운다")]
        public Transform[] LockDiamonds;
        public MeshRenderer[] LockDiamondRenderers;
        [Tooltip("조준 중(락온 전) 마름모")]
        public Material LockingMaterial;
        [Tooltip("락온 완료: 마름모와 표적 테두리")]
        public Material LockedMaterial;
        [Tooltip("락온을 시도하는 동안(표적이 잡혔고 아직 락온 전) 테두리 깜빡임 횟수(초당). 락온되면 멈추고 빨갛게 켜진다")]
        public float LockBlinkRate = 8f;
        [Tooltip("마름모가 출발하는 거리(마커 크기 배수). 락온 진행도에 따라 표적 중심으로 다가온다")]
        public float DiamondStartDistance = 2.5f;
        [Tooltip("마름모 크기(마커 크기 배수). 0.71이면 꼭짓점이 테두리 변의 가운데에 닿는다 — 조금 크게 해서 테두리와 겹치게 한다")]
        public float DiamondScale = 0.8f;
        [Tooltip("특수무장(ACO_DFUNC_Missile.CrossLockIndicator)의 락온 진행 표시 — 십자 메시(빌더/Bake가 만든다). 같은 표시 오브젝트의 메시만 바꿔 쓴다")]
        public Mesh LockCrossMesh;
        [Tooltip("십자 크기(마커 크기 배수)")]
        public float CrossScale = 1f;

        [Header("체력 바 (프리팹 빌더가 채운다)")]
        [Tooltip("시선 정보 아래에 남은 체력을 바로 보여 준다")]
        public bool ShowHealthBar = true;
        [Tooltip("체력 비율이 이 값 이하면 중간 색(노랑)")]
        [Range(0f, 1f)] public float HealthMidThreshold = 0.5f;
        [Tooltip("체력 비율이 이 값 이하면 낮은 색(빨강)")]
        [Range(0f, 1f)] public float HealthLowThreshold = 0.25f;
        public Material HealthHighMaterial;
        public Material HealthMidMaterial;
        public Material HealthLowMaterial;
        [Tooltip("마커별 체력 바 루트(배경 포함)")]
        public Transform[] HealthBars;
        [Tooltip("채움 막대의 왼쪽 끝 기준점 — X 스케일 = 체력 비율")]
        public Transform[] HealthFills;
        public MeshRenderer[] HealthFillRenderers;

        private const int KIND_AIR = 0;
        private const int KIND_GROUND = 1;
        private const int KIND_OBJECTIVE = 2;
        private const int KIND_OBJECTIVE_AIR = 3;
        private const int KIND_FRIENDLY_AIR = 4;
        private const int KIND_FRIENDLY_GROUND = 5;
        private const int KIND_WAYPOINT = 6;
        private const int KIND_DESTINATION = 7;

        // 시작 시 생성기 풀에서 만든 유닛 목록(캐시)
        private Transform[] _unitTransform;
        private SaccTarget[] _unitHealth;
        private int[] _unitKind;
        private string[] _unitLabel;
        private ACO_NPCFighterAI[] _unitFighter;
        private int[] _unitCat;
        private int[] _unitIdx;
        private bool[] _unitObjective;
        private bool[] _unitHidden;      // 격침된 함선과 같이 가라앉는 부위
        private bool[] _unitFriendly;    // 호위 대상(아군) — 파란 테두리 + 이름, 목표처럼 멀리서도 보인다
        private int _unitCount;
        private float _objectiveTimer;

        // 마커별 현재 상태(바뀔 때만 SetActive/머티리얼을 건드린다)
        private bool[] _markerOn;
        private int[] _markerKind;
        private bool[] _markerInfoOn;
        private bool[] _markerBarOn;
        private int[] _markerBarLevel;
        private int _usedLastFrame;
        private string[] _markerText;    // 위 글자 캐시(같은 글이면 TMP를 건드리지 않는다)
        private ACO_EscortUnit _routeUnit;
        private float _routeTimer;

        // 락온 표시(락온 칸별). 칸 k = 무기의 락온 칸 k.
        public const int MAX_LOCKS = 8;
        private GameObject[] _lockObj = new GameObject[MAX_LOCKS];   // 그 칸의 표적(락온 마커)
        private int[] _lockUnit = new int[MAX_LOCKS];                // 표적이 속한 유닛(없으면 -1 — 마름모만 그린다)
        private float[] _lockProgress = new float[MAX_LOCKS];
        private bool[] _lockLocked = new bool[MAX_LOCKS];
        private float[] _blinkStart = new float[MAX_LOCKS];
        private bool[] _diamondArmed = new bool[MAX_LOCKS];          // 이번 조준에서 마름모가 출발했는가(진행도가 0으로 떨어지면 새 방향에서 다시)
        private Vector2[] _diamondFrom = new Vector2[MAX_LOCKS];
        private bool[] _diamondLockedLook = new bool[MAX_LOCKS];
        private bool[] _diamondOn = new bool[MAX_LOCKS];
        private int[] _lockMarker = new int[MAX_LOCKS];              // 이번 프레임 그 칸 표적이 쓴 마커(-1 = 화면에 없음)
        private float[] _lockDist = new float[MAX_LOCKS];
        private float[] _lockSize = new float[MAX_LOCKS];
        private int[] _hiddenFrames = new int[MAX_LOCKS];            // 깜빡임으로 꺼 둔 테두리(다음 프레임에 되돌린다)
        private int _hiddenCount;
        private bool _lockStateReady;

        // 정보 글자: 이번 프레임에 띄울 마커별 유닛/거리(-1 = 끔)
        private int[] _infoUnitFor;
        private float[] _infoDistFor;

        void Start()
        {
            BuildUnitList();
            int n = Markers != null ? Markers.Length : 0;
            _markerOn = new bool[n];
            _markerKind = new int[n];
            _markerInfoOn = new bool[n];
            _markerBarOn = new bool[n];
            _markerBarLevel = new int[n];
            _markerText = new string[n];
            _infoUnitFor = new int[n];
            _infoDistFor = new float[n];
            for (int i = 0; i < n; i++)
            {
                _markerKind[i] = -1;
                _markerBarLevel[i] = -1;
                if (Markers[i]) { Markers[i].gameObject.SetActive(false); }
                if (InfoLabels != null && i < InfoLabels.Length && InfoLabels[i]) { InfoLabels[i].gameObject.SetActive(false); }
                if (HealthBars != null && i < HealthBars.Length && HealthBars[i]) { HealthBars[i].gameObject.SetActive(false); }
            }
        }

        private void BuildUnitList()
        {
            int fighters = (Generator != null && Generator.Fighters != null) ? Generator.Fighters.Length : 0;
            int ground = (Generator != null && Generator.GroundUnits != null) ? Generator.GroundUnits.Length : 0;
            int hvts = (Generator != null && Generator.HighValueTargets != null) ? Generator.HighValueTargets.Length : 0;
            int parts = Generator != null ? Generator.GetUnitCount(ACO_MissionGenerator.CAT_PART) : 0;
            int escorts = (Generator != null && Generator.EscortUnits != null) ? Generator.EscortUnits.Length : 0;
            // 슈퍼에이스 기만체: 진짜와 같은 마커(이름·목표 색·체력 바)로 그린다 — 기만체가 떠 있을 때만 보인다.
            int decoys = 0;
            for (int i = 0; i < fighters; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f != null && f.SuperAce != null && f.SuperAce.Decoys != null) { decoys += f.SuperAce.Decoys.Length; }
            }
            int total = fighters + ground + hvts + parts + escorts + decoys;

            _unitTransform = new Transform[total];
            _unitHealth = new SaccTarget[total];
            _unitKind = new int[total];
            _unitLabel = new string[total];
            _unitFighter = new ACO_NPCFighterAI[total];
            _unitCat = new int[total];
            _unitIdx = new int[total];
            _unitObjective = new bool[total];
            _unitHidden = new bool[total];
            _unitFriendly = new bool[total];
            _unitCount = 0;

            for (int i = 0; i < fighters; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f == null) { continue; }
                AddUnit(f.transform, f.TargetHealth, KIND_AIR, "FTR", f, ACO_MissionGenerator.CAT_FIGHTER, i);
                if (f.SuperAce == null || f.SuperAce.Decoys == null) { continue; }
                for (int k = 0; k < f.SuperAce.Decoys.Length; k++)
                {
                    Transform d = f.SuperAce.Decoys[k];
                    if (d != null) { AddUnit(d, f.TargetHealth, KIND_AIR, "FTR", f, ACO_MissionGenerator.CAT_FIGHTER, i); }
                }
            }
            for (int i = 0; i < ground; i++)
            {
                UdonSharpBehaviour g = Generator.GroundUnits[i];
                if (g == null) { continue; }
                AddUnit(g.transform, g.GetComponent<SaccTarget>(), KIND_GROUND, ReadLabel(g, "GND"), null, ACO_MissionGenerator.CAT_GROUND, i);
            }
            for (int i = 0; i < hvts; i++)
            {
                UdonSharpBehaviour h = Generator.HighValueTargets[i];
                if (h == null) { continue; }
                AddUnit(h.transform, h.GetComponent<SaccTarget>(), KIND_GROUND, ReadLabel(h, "TGT"), null, ACO_MissionGenerator.CAT_HVT, i);
            }
            // 함선 부위(VLS/함포/CIWS)는 각자 따로 표시된다.
            for (int i = 0; i < parts; i++)
            {
                UdonSharpBehaviour pt = Generator.GetUnitBehaviour(ACO_MissionGenerator.CAT_PART, i);
                if (pt == null) { continue; }
                AddUnit(pt.transform, pt.GetComponent<SaccTarget>(), KIND_GROUND, ReadLabel(pt, "PRT"), null, ACO_MissionGenerator.CAT_PART, i);
            }
            for (int i = 0; i < escorts; i++)
            {
                ACO_EscortUnit e = Generator.EscortUnits[i];
                if (e == null) { continue; }
                _unitFriendly[_unitCount] = !e.Hostile;   // 적 폭격기는 적 마커(목표면 빨강)
                int baseKind = e.MoveMode == ACO_EscortUnit.MOVE_AIR ? KIND_AIR : KIND_GROUND;
                AddUnit(e.transform, e.TargetHealth, baseKind, string.IsNullOrEmpty(e.MarkerLabel) ? (e.Hostile ? "BMR" : "VIP") : e.MarkerLabel, null, ACO_MissionGenerator.CAT_ESCORT, i);
            }
        }

        private void AddUnit(Transform t, SaccTarget health, int kind, string label, ACO_NPCFighterAI fighter, int category, int index)
        {
            _unitTransform[_unitCount] = t;
            _unitHealth[_unitCount] = health;
            _unitKind[_unitCount] = kind;
            _unitLabel[_unitCount] = label;
            _unitFighter[_unitCount] = fighter;
            _unitCat[_unitCount] = category;
            _unitIdx[_unitCount] = index;
            _unitCount++;
        }

        /// 목표 여부: 관리자가 있으면 지금 목표 태그와 같은 유닛, 없으면 고가치 표적 전부.
        private void RefreshObjectives()
        {
            int tag = Manager != null ? Manager.GetActiveObjectiveTag() : 0;
            for (int u = 0; u < _unitCount; u++)
            {
                _unitHidden[u] = _unitCat[u] == ACO_MissionGenerator.CAT_PART && Generator.IsUnitSuppressed(_unitCat[u], _unitIdx[u]);
                if (_unitFriendly[u]) { _unitObjective[u] = false; }
                else if (Manager == null) { _unitObjective[u] = _unitCat[u] == ACO_MissionGenerator.CAT_HVT; }
                else { _unitObjective[u] = tag != 0 && Generator.GetUnitTag(_unitCat[u], _unitIdx[u]) == tag; }
            }
        }

        private string ReadLabel(UdonSharpBehaviour unit, string fallback)
        {
            object value = unit.GetProgramVariable("MarkerLabel");
            if (value == null) { return fallback; }
            string s = (string)value;
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        private float _previewUntil;   // HUD 설정 패널에서 조정하는 동안 미션 전에도 크로스헤어를 보여 준다

        /// HUD 설정 패널(ACO_HudSettings)이 부른다: seconds 동안 미션 전에도 크로스헤어를 보여 준다.
        public void Preview(float seconds) { _previewUntil = Time.time + seconds; }

        public override void PostLateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) { return; }
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            UpdateCrosshair(head.position, head.rotation);
            if (Markers == null) { return; }

            Vector3 headPos = head.position;
            Vector3 headFwd = head.rotation * Vector3.forward;
            Vector3 headUp = head.rotation * Vector3.up;

            _objectiveTimer -= Time.deltaTime;
            if (_objectiveTimer <= 0f && Generator != null)
            {
                _objectiveTimer = ObjectiveRefreshInterval;
                RefreshObjectives();
            }

            float maxSq = MaxDistance * MaxDistance;
            float objectiveMaxSq = Mathf.Max(ObjectiveMaxDistance, MaxDistance);
            objectiveMaxSq *= objectiveMaxSq;
            float baseSize = 2f * DisplayDistance * Mathf.Tan(MarkerAngle * 0.5f * Mathf.Deg2Rad);
            float nearDist = Mathf.Max(NearDistance, 1f);
            float logRange = Mathf.Log(Mathf.Max(MaxDistance / nearDist, 1.0001f));

            // 락온 상태: 내가 조종 중이고 고른 무기에서 칸별로 읽는다.
            ACO_DFUNC_Missile weapon = FindActiveWeapon();
            _lockCross = weapon != null && weapon.CrossLockIndicator;
            ReadLockState(weapon);
            for (int h = 0; h < _hiddenCount; h++)
            {
                int f = _hiddenFrames[h];
                if (FrameRenderers != null && f < FrameRenderers.Length && FrameRenderers[f]) { FrameRenderers[f].enabled = true; }
            }
            _hiddenCount = 0;
            for (int k = 0; k < MAX_LOCKS; k++) { _lockMarker[k] = -1; _lockSize[k] = baseSize; }

            int used = 0;
            int focusMarker = -1;
            int focusUnit = -1;
            float focusAngle = LookAngle;
            float focusDist = 0f;

            for (int u = 0; u < _unitCount && used < Markers.Length; u++)
            {
                Transform t = _unitTransform[u];
                if (t == null || !t.gameObject.activeInHierarchy) { continue; }
                SaccTarget health = _unitHealth[u];
                if (health != null && health.Health <= 0f) { continue; }
                if (_unitHidden[u]) { continue; }

                bool objective = _unitObjective[u];
                bool friendly = _unitFriendly[u];
                Vector3 d = t.position - headPos;
                float sq = d.sqrMagnitude;
                if (sq > ((objective || friendly) ? objectiveMaxSq : maxSq) || sq < 1f) { continue; }

                float dist = Mathf.Sqrt(sq);
                Vector3 dir = d / dist;
                int kind = _unitKind[u];
                if (friendly) { kind = kind == KIND_AIR ? KIND_FRIENDLY_AIR : KIND_FRIENDLY_GROUND; }
                else if (objective) { kind = kind == KIND_AIR ? KIND_OBJECTIVE_AIR : KIND_OBJECTIVE; }

                Transform m = Markers[used];
                if (!_markerOn[used]) { m.gameObject.SetActive(true); _markerOn[used] = true; }
                m.SetPositionAndRotation(headPos + dir * DisplayDistance, Quaternion.LookRotation(dir, headUp));
                // 원근감: NearDistance까지 1배, MaxDistance에서 FarScale배, 사이는 log(거리)에 비례.
                float farT = dist <= nearDist ? 0f : Mathf.Clamp01(Mathf.Log(dist / nearDist) / logRange);
                float size = baseSize * Mathf.Lerp(1f, FarScale, farT);
                if (kind >= KIND_OBJECTIVE) { size *= ObjectiveScale; }
                m.localScale = new Vector3(size, size, size);

                if (_markerKind[used] != kind) { SetMarkerKind(used, kind); }
                // 아군 위 이름은 유닛마다 다를 수 있다.
                if (friendly) { SetTopText(used, _unitLabel[u]); }

                for (int k = 0; k < MAX_LOCKS; k++)
                {
                    if (_lockUnit[k] != u) { continue; }
                    _lockMarker[k] = used;
                    _lockDist[k] = dist;
                    _lockSize[k] = size;
                    StyleLockMarker(used, k);
                    break;
                }

                float angle = Vector3.Angle(headFwd, dir);
                if (angle < focusAngle)
                {
                    focusAngle = angle;
                    focusMarker = used;
                    focusUnit = u;
                    focusDist = dist;
                }
                used++;
            }

            // 호위 항로: 남은 경유점 + 목적지
            if (ShowEscortRoute && Manager != null)
            {
                _routeTimer -= Time.deltaTime;
                if (_routeTimer <= 0f)
                {
                    _routeTimer = ObjectiveRefreshInterval;
                    _routeUnit = Manager.GetRouteEscort();
                }
                if (_routeUnit != null && _routeUnit.gameObject.activeInHierarchy)
                {
                    used = DrawRoute(used, headPos, headUp, baseSize, objectiveMaxSq);
                }
            }

            // 이번 프레임에 안 쓴 마커는 끈다.
            for (int i = used; i < _usedLastFrame; i++)
            {
                if (_markerOn[i]) { Markers[i].gameObject.SetActive(false); _markerOn[i] = false; }
            }
            _usedLastFrame = used;

            // 이름표(종류·거리·체력)는 락온 대상에 고정한다. 주 표적(첫 칸)이 체력 바를 갖고, 나머지 락온 표적엔 이름표만.
            int primary = -1;
            for (int k = 0; k < MAX_LOCKS; k++) { if (_lockMarker[k] >= 0) { primary = k; break; } }
            if (primary >= 0)
            {
                focusMarker = _lockMarker[primary];
                focusUnit = _lockUnit[primary];
                focusDist = _lockDist[primary];
            }
            for (int i = 0; i < _infoUnitFor.Length; i++) { _infoUnitFor[i] = -1; }
            if (focusMarker >= 0 && focusMarker < _infoUnitFor.Length) { _infoUnitFor[focusMarker] = focusUnit; _infoDistFor[focusMarker] = focusDist; }
            for (int k = 0; k < MAX_LOCKS; k++)
            {
                int m = _lockMarker[k];
                if (m < 0 || m >= _infoUnitFor.Length) { continue; }
                _infoUnitFor[m] = _lockUnit[k];
                _infoDistFor[m] = _lockDist[k];
            }
            UpdateInfo(focusMarker, focusUnit);
            UpdateDiamonds(headPos, headUp);
        }

        /// 무기의 락온 칸들을 읽어 칸별 표시 상태를 맞춘다. 칸의 표적이 바뀌면 깜빡임을 다시 시작하고 마름모를 새로 출발시킨다.
        private void ReadLockState(ACO_DFUNC_Missile weapon)
        {
            int count = weapon != null ? Mathf.Min(weapon.GetLockSlotCount(), MAX_LOCKS) : 0;
            for (int k = 0; k < MAX_LOCKS; k++)
            {
                GameObject obj = k < count ? weapon.GetLockObject(k) : null;
                float progress = obj != null ? weapon.GetLockProgressAt(k) : 0f;
                bool locked = obj != null && weapon.IsLockedAt(k);
                if (!_lockStateReady || obj != _lockObj[k])
                {
                    _lockObj[k] = obj;
                    _lockUnit[k] = FindUnitOf(obj);
                    _blinkStart[k] = Time.time;
                    _diamondArmed[k] = false;
                }
                _lockProgress[k] = progress;
                _lockLocked[k] = locked;

                if (obj == null || (progress <= 0f && !locked)) { _diamondArmed[k] = false; }
                else if (!_diamondArmed[k])
                {
                    // 조준을 새로 시작할 때마다 다른 방향에서 다가온다.
                    _diamondArmed[k] = true;
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    _diamondFrom[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
            }
            _lockStateReady = true;
        }

        private ACO_DFUNC_Missile FindActiveWeapon()
        {
            if (Weapons == null) { return null; }
            for (int i = 0; i < Weapons.Length; i++)
            {
                ACO_DFUNC_Missile w = Weapons[i];
                if (w != null && w.IsHudActive()) { return w; }
            }
            return null;
        }

        /// 락온 마커가 속한 유닛 번호. 표적이 바뀔 때만 찾는다.
        private int FindUnitOf(GameObject marker)
        {
            if (marker == null) { return -1; }
            Transform t = marker.transform;
            for (int u = 0; u < _unitCount; u++)
            {
                Transform root = _unitTransform[u];
                if (root != null && t.IsChildOf(root)) { return u; }
            }
            return -1;
        }

        /// 락온 대상 테두리: 락온을 시도하는 동안 일정한 속도로 깜빡이고, 락온되면 빨간색으로 켜진다. k = 락온 칸.
        private void StyleLockMarker(int i, int k)
        {
            bool locked = _lockLocked[k];
            if (FrameRenderers == null || i >= FrameRenderers.Length || !FrameRenderers[i]) { return; }
            MeshRenderer r = FrameRenderers[i];
            if (locked)
            {
                if (LockedMaterial != null)
                {
                    r.sharedMaterial = LockedMaterial;
                    _markerKind[i] = -1; // 락온이 풀리거나 다른 유닛이 이 마커를 쓰면 원래 머티리얼로 되돌린다
                }
                return;
            }

            // 표적이 잡힌 순간부터 켜진 상태로 시작해 반 주기마다 꺼졌다 켜진다.
            float t = Time.time - _blinkStart[k];
            if (Mathf.FloorToInt(t * LockBlinkRate * 2f) % 2 == 1 && _hiddenCount < MAX_LOCKS)
            {
                r.enabled = false;
                _hiddenFrames[_hiddenCount] = i;
                _hiddenCount++;
            }
        }

        // 락온 표시 모양: 고른 무기가 특수무장이면 십자, 아니면 마름모(사각 테두리를 45° 돌린 것).
        private bool _lockCross;
        private bool _shapeCross;
        private Mesh _diamondMesh;
        private MeshFilter[] _lockFilters;

        private void SetLockShape(bool cross)
        {
            if (_lockFilters == null)
            {
                _lockFilters = new MeshFilter[LockDiamonds.Length];
                for (int k = 0; k < LockDiamonds.Length; k++)
                {
                    if (LockDiamonds[k]) { _lockFilters[k] = LockDiamonds[k].GetComponent<MeshFilter>(); }
                    if (_diamondMesh == null && _lockFilters[k]) { _diamondMesh = _lockFilters[k].sharedMesh; }
                }
            }
            if (cross == _shapeCross) { return; }
            _shapeCross = cross;
            Mesh mesh = cross ? LockCrossMesh : _diamondMesh;
            for (int k = 0; k < _lockFilters.Length; k++) { if (_lockFilters[k]) { _lockFilters[k].sharedMesh = mesh; } }
        }

        /// 락온 진행 마름모(칸별, 특수무장은 십자): 진행도만큼 출발점에서 표적 중심으로 다가오고, 락온되면 테두리에 겹쳐 빨갛게.
        private void UpdateDiamonds(Vector3 headPos, Vector3 headUp)
        {
            if (LockDiamonds == null) { return; }
            SetLockShape(_lockCross && LockCrossMesh != null);
            float baseSize = 2f * DisplayDistance * Mathf.Tan(MarkerAngle * 0.5f * Mathf.Deg2Rad);
            for (int k = 0; k < MAX_LOCKS; k++)
            {
                Transform diamond = k < LockDiamonds.Length ? LockDiamonds[k] : null;
                GameObject obj = _lockObj[k];
                bool show = diamond != null && obj != null && obj.activeInHierarchy && _diamondArmed[k];
                Vector3 d = show ? obj.transform.position - headPos : Vector3.zero;
                if (show && d.sqrMagnitude < 1f) { show = false; }
                bool wasOn = _diamondOn[k];
                if (wasOn != show)
                {
                    _diamondOn[k] = show;
                    if (diamond != null) { diamond.gameObject.SetActive(show); }
                }
                if (!show) { continue; }

                // 화면 밖이라 마커가 없으면 기본 크기로 그린다.
                float size = _lockMarker[k] >= 0 ? _lockSize[k] : baseSize;
                bool locked = _lockLocked[k];
                Vector3 dir = d.normalized;
                Quaternion rot = Quaternion.LookRotation(dir, headUp);
                // 빨리 다가오다 천천히 붙는다(조준 막바지에 겹쳐지는 순간이 잘 보이게).
                float p = locked ? 1f : Mathf.Clamp01(_lockProgress[k]);
                float remain = (1f - p) * (1f - p);
                Vector2 from = _diamondFrom[k];
                Vector3 offset = rot * new Vector3(from.x, from.y, 0f) * (size * DiamondStartDistance * remain);
                diamond.SetPositionAndRotation(headPos + dir * DisplayDistance + offset, _shapeCross ? rot : rot * Quaternion.Euler(0f, 0f, 45f));
                float sc = size * (_shapeCross ? CrossScale : DiamondScale);
                diamond.localScale = new Vector3(sc, sc, sc);

                if (_diamondLockedLook[k] != locked || !wasOn)
                {
                    _diamondLockedLook[k] = locked;
                    MeshRenderer r = LockDiamondRenderers != null && k < LockDiamondRenderers.Length ? LockDiamondRenderers[k] : null;
                    Material m = locked ? LockedMaterial : LockingMaterial;
                    if (r != null && m != null) { r.sharedMaterial = m; }
                }
            }
        }

        /// 호위 대상이 아직 지나지 않은 경유점(마름모, WP n)과 목적지(큰 마름모, DEST + 거리)를 마커 풀 used부터 놓는다. 반환: 다음 빈 마커.
        private int DrawRoute(int used, Vector3 headPos, Vector3 headUp, float baseSize, float maxSq)
        {
            int n = _routeUnit.GetWaypointCount();
            if (n < 2) { return used; }
            float travelled = _routeUnit.GetDistanceTravelled();
            Vector3 dest = _routeUnit.GetWaypoint(n - 1);
            float gapSq = WaypointMinGap * WaypointMinGap;
            for (int k = 1; k < n && used < Markers.Length; k++)
            {
                bool isDest = k == n - 1;
                if (!isDest && _routeUnit.GetWaypointDistance(k) <= travelled) { continue; } // 지난 경유점
                Vector3 p = _routeUnit.GetWaypoint(k);
                if (!isDest && (p - dest).sqrMagnitude < gapSq) { continue; }
                Vector3 d = p - headPos;
                float sq = d.sqrMagnitude;
                if (sq > maxSq || sq < 1f) { continue; }
                float dist = Mathf.Sqrt(sq);
                Vector3 dir = d / dist;
                int kind = isDest ? KIND_DESTINATION : KIND_WAYPOINT;

                Transform m = Markers[used];
                if (!_markerOn[used]) { m.gameObject.SetActive(true); _markerOn[used] = true; }
                // 마름모는 테두리 메시만 45° 돌린다(SetMarkerKind) — 글자는 그대로 위에
                m.SetPositionAndRotation(headPos + dir * DisplayDistance, Quaternion.LookRotation(dir, headUp));
                float size = baseSize * (isDest ? ObjectiveScale : WaypointScale);
                m.localScale = new Vector3(size, size, size);
                if (_markerKind[used] != kind) { SetMarkerKind(used, kind); }
                SetTopText(used, isDest ? "DEST " + FormatDistance(dist) : "WP" + k);
                used++;
            }
            return used;
        }

        /// 마커 위 글자(TGT/아군 이름/항로) — 바뀔 때만 TMP에 넣는다.
        private void SetTopText(int i, string text)
        {
            if (TgtLabels == null || i >= TgtLabels.Length || !TgtLabels[i]) { return; }
            if (_markerText[i] == text) { return; }
            _markerText[i] = text;
            TgtLabels[i].text = text;
        }

        /// 마커 모양/색/TGT 표시를 종류에 맞게 바꾼다(종류가 바뀔 때만).
        private void SetMarkerKind(int i, int kind)
        {
            _markerKind[i] = kind;
            if (Frames != null && i < Frames.Length && Frames[i])
            {
                Frames[i].sharedMesh = (kind == KIND_AIR || kind == KIND_OBJECTIVE_AIR || kind == KIND_FRIENDLY_AIR || kind >= KIND_WAYPOINT) ? AirMesh : GroundMesh;
                // 경유점/목적지: 사각형 테두리만 45° 돌려 마름모로(마커 루트를 돌리면 위 글자까지 기운다)
                Frames[i].transform.localRotation = kind >= KIND_WAYPOINT ? Quaternion.Euler(0f, 0f, 45f) : Quaternion.identity;
            }
            if (FrameRenderers != null && i < FrameRenderers.Length && FrameRenderers[i])
            {
                Material mat = GroundMaterial;
                if (kind == KIND_AIR) { mat = AirMaterial; }
                else if (kind == KIND_OBJECTIVE || kind == KIND_OBJECTIVE_AIR) { mat = ObjectiveMaterial; }
                else if (kind == KIND_FRIENDLY_AIR || kind == KIND_FRIENDLY_GROUND)
                {
                    mat = FriendlyMaterial != null ? FriendlyMaterial : (kind == KIND_FRIENDLY_AIR ? AirMaterial : GroundMaterial);
                }
                else if (kind == KIND_WAYPOINT) { mat = WaypointMaterial != null ? WaypointMaterial : (FriendlyMaterial != null ? FriendlyMaterial : AirMaterial); }
                else if (kind == KIND_DESTINATION) { mat = DestinationMaterial != null ? DestinationMaterial : (FriendlyMaterial != null ? FriendlyMaterial : AirMaterial); }
                FrameRenderers[i].sharedMaterial = mat;
            }
            if (TgtLabels != null && i < TgtLabels.Length && TgtLabels[i])
            {
                bool friendly = kind == KIND_FRIENDLY_AIR || kind == KIND_FRIENDLY_GROUND;
                bool objective = kind == KIND_OBJECTIVE || kind == KIND_OBJECTIVE_AIR;
                bool route = kind == KIND_WAYPOINT || kind == KIND_DESTINATION;
                TgtLabels[i].gameObject.SetActive(friendly || objective || route);
                if (objective) { SetTopText(i, "TGT"); TgtLabels[i].color = ObjectiveLabelColor; }
                else if (friendly) { TgtLabels[i].color = FriendlyLabelColor; }
                else if (kind == KIND_WAYPOINT) { TgtLabels[i].color = WaypointLabelColor; }
                else if (kind == KIND_DESTINATION) { TgtLabels[i].color = DestinationLabelColor; }
            }
        }

        /// 크로스헤어: 미션 중(또는 미리보기 중)에만, 시선 정면 CrosshairDistance에 CrosshairAngle 크기로.
        private void UpdateCrosshair(Vector3 headPos, Quaternion headRot)
        {
            if (!Crosshair) { return; }
            bool show = ShowCrosshair && ((Manager != null && Manager.GetState() == ACO_MissionManager.STATE_RUNNING) || Time.time < _previewUntil);
            if (Crosshair.gameObject.activeSelf != show) { Crosshair.gameObject.SetActive(show); }
            if (!show) { return; }
            float d = Mathf.Max(CrosshairDistance, 0.1f);
            Crosshair.SetPositionAndRotation(headPos + headRot * Vector3.forward * d, headRot);
            float size = 2f * d * Mathf.Tan(CrosshairAngle * 0.5f * Mathf.Deg2Rad);
            Crosshair.localScale = new Vector3(size, size, size);
        }

        /// "종류 거리"를 띄운다: 시선이 향한 유닛(락온 중이면 주 표적) + 나머지 락온 표적. 체력 바는 focusMarker에만.
        private void UpdateInfo(int focusMarker, int focusUnit)
        {
            if (InfoLabels == null) { return; }
            for (int i = 0; i < _markerInfoOn.Length; i++)
            {
                int u = _infoUnitFor[i];
                bool on = u >= 0;
                if (_markerInfoOn[i] != on)
                {
                    _markerInfoOn[i] = on;
                    if (i < InfoLabels.Length && InfoLabels[i]) { InfoLabels[i].gameObject.SetActive(on); }
                }
                if (on && i < InfoLabels.Length && InfoLabels[i]) { InfoLabels[i].text = UnitLabel(u) + "  " + FormatDistance(_infoDistFor[i]); }
            }
            UpdateHealthBar(focusMarker, focusUnit);
        }

        /// 시선 정보가 뜬 유닛에만 체력 바를 켜고, 채움 길이/색을 남은 체력 비율에 맞춘다.
        private void UpdateHealthBar(int focusMarker, int focusUnit)
        {
            if (HealthBars == null) { return; }
            float ratio = -1f;
            if (ShowHealthBar && focusUnit >= 0)
            {
                SaccTarget health = _unitHealth[focusUnit];
                // FullHealth는 SaccTarget.Start에서 채워진다 — 그 전이면 표시하지 않는다.
                if (health != null && health.FullHealth > 0f) { ratio = Mathf.Clamp01(health.Health / health.FullHealth); }
            }

            for (int i = 0; i < _markerBarOn.Length && i < HealthBars.Length; i++)
            {
                bool on = i == focusMarker && ratio >= 0f;
                if (_markerBarOn[i] == on) { continue; }
                _markerBarOn[i] = on;
                if (HealthBars[i]) { HealthBars[i].gameObject.SetActive(on); }
            }
            if (ratio < 0f || focusMarker >= HealthBars.Length) { return; }

            if (HealthFills != null && focusMarker < HealthFills.Length && HealthFills[focusMarker])
            {
                HealthFills[focusMarker].localScale = new Vector3(Mathf.Max(ratio, 0.001f), 1f, 1f);
            }
            int level = ratio <= HealthLowThreshold ? 2 : (ratio <= HealthMidThreshold ? 1 : 0);
            if (_markerBarLevel[focusMarker] != level && HealthFillRenderers != null
                && focusMarker < HealthFillRenderers.Length && HealthFillRenderers[focusMarker])
            {
                _markerBarLevel[focusMarker] = level;
                Material mat = HealthHighMaterial;
                if (level == 1) { mat = HealthMidMaterial; }
                else if (level == 2) { mat = HealthLowMaterial; }
                HealthFillRenderers[focusMarker].sharedMaterial = mat;
            }
        }

        private string UnitLabel(int u)
        {
            ACO_NPCFighterAI fighter = _unitFighter[u];
            if (fighter != null)
            {
                if (fighter.SuperAce != null) { return fighter.SuperAce.AceName; }
                int tier = fighter.GetSkillTier();
                if (tier == ACO_NPCFighterAI.SKILL_ACE) { return "ACE"; }
                if (tier == ACO_NPCFighterAI.SKILL_VETERAN) { return "VET"; }
                return "NOV";
            }
            return _unitLabel[u];
        }

        private string FormatDistance(float meters)
        {
            if (meters < 1000f) { return Mathf.RoundToInt(meters) + "m"; }
            return (meters * 0.001f).ToString("0.0") + "km";
        }
    }
}
