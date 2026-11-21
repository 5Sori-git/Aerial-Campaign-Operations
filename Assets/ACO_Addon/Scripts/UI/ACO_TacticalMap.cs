using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 작전 테이블(로컬 전용, 동기화 없음) — 브리핑실의 Tacview식 3D 미니맵. 육각 지형을 축소한 메시와 모든 유닛(아군 기체, 적 전투기,
    /// 지상 유닛·함선, 고가치 표적, 호위 대상, 기지)을 실시간으로 보여 준다. 적도 전부 보인다.
    ///
    /// - 축척: 지형 반경(FieldRadius) → MapRadius(m). 높이는 VerticalExaggeration배(산과 고도가 납작해 보이지 않게).
    ///   MapRoot의 로컬 축 = 월드 축(지형 중심 기준) — 테이블을 돌리면 지도도 같이 돈다.
    /// - 지형: 지형이 다 세워지면(새 씨앗) 칸마다 육각 기둥(윗면 + 해수면까지 옆면, 높이대별 정점 색)을 메시 하나로 만든다.
    /// - 유닛: 생성기 풀과 게임 진행의 참가자 기체를 처음에 한 번 훑어 마커를 하나씩 만들어 둔다(복제). 배치된 것만 켠다.
    ///   항공기 마커는 꼬리 궤적(TrailRenderer), 참가자 기체는 이름표, 페이즈 목표로 배치된 지상 유닛은 TGT 고리.
    /// - 비용: 내가 테이블 근처(ActiveRadius)에 있을 때만 UpdateInterval마다 갱신하고 지형 메시도 그때 만든다. 네트워크 사용 없음
    ///   (위치는 이미 동기화돼 오는 유닛 트랜스폼을 읽는다).
    /// 브리핑 단계에도 적이 보이는 건 생성기의 미리 배치(ACO_GameFlow가 새 지형마다 요청) 덕분이다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_TacticalMap : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 생성기를 넣는다(유닛 풀·지형)")]
        public ACO_MissionGenerator Generator;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 게임 진행을 넣는다(참가자 기체·명단 이름)")]
        public ACO_GameFlow Flow;
        [Tooltip("TAC 네임(콜사인) — 참가자 기체 이름표에 콜사인 + VRChat 이름(Bake가 넣는다)")]
        public ACO_TacName TacNames;
        [Tooltip("아군 기지(항모 등). 비워도 된다")]
        public Transform HomeBase;

        [Header("테이블")]
        [Tooltip("지도 중심(테이블 윗면 = 해수면). 마커와 지형이 이 아래에 놓인다")]
        public Transform MapRoot;
        [Tooltip("축소 지형 메시를 넣을 곳(ACO/MapVertexColor 머티리얼)")]
        public MeshFilter TerrainFilter;
        [Tooltip("지도 반경(m) — 지형 반경이 이만큼으로 줄어든다")]
        public float MapRadius = 1.4f;
        [Tooltip("높이 과장 배수")]
        public float VerticalExaggeration = 3f;
        [Tooltip("지형이 없을 때의 월드 반경(m)")]
        public float FallbackWorldRadius = 20000f;
        [Tooltip("내가 테이블에서 이 거리(m) 안에 있을 때만 갱신한다(브리핑실 안)")]
        public float ActiveRadius = 15f;
        [Tooltip("갱신 간격(초)")]
        public float UpdateInterval = 0.1f;
        [Tooltip("축소 지형을 만들 때 프레임당 처리할 칸 수(작을수록 끊김이 적고 오래 걸린다). 3,000칸 / 150 = 20프레임")]
        public int CellsPerFrame = 150;
        [Tooltip("지형 색(낮은 곳부터: 모래, 풀, 바위, 눈). 경계는 지형의 HeightThresholds")]
        public Color[] HeightColors = new Color[] {
            new Color(0.82f, 0.76f, 0.52f), new Color(0.33f, 0.56f, 0.28f), new Color(0.48f, 0.44f, 0.40f), new Color(0.94f, 0.95f, 0.97f) };
        [Tooltip("옆면 밝기(윗면 색에 곱한다)")]
        [Range(0f, 1f)] public float SideShade = 0.55f;
        [Tooltip("기둥 옆면도 그린다. 끄면 윗면 육각형만 — 반투명 재질(ACO_MapTerrainHolo)과 함께 홀로그램처럼, 아래 바다 격자가 비친다(6-319)")]
        public bool TerrainSides = false;

        [Header("격자 (6-301 — 작전 기록판 좌표와 같은 격자)")]
        [Tooltip("격자 정의(작전 기록판) — Bake가 넣는다. 비우면 격자를 그리지 않는다")]
        public ACO_MissionLog GridLog;
        [Tooltip("격자 선 메시(해수면, 항상 지형 위에 그린다 — ZTest Always) — 스크립트가 만든다")]
        public MeshFilter GridFilter;
        [Tooltip("격자 이름표 — 네 묶음(북쪽 열 · 남쪽 열 · 서쪽 행 · 동쪽 행). 보는 사람 쪽으로 돈다. Setup Check가 만든다")]
        public Transform[] GridLabels;
        [Tooltip("격자 선 굵기(m)")]
        public float GridLineWidth = 0.003f;
        [Tooltip("이름표와 격자 테두리 사이(m)")]
        public float GridLabelGap = 0.05f;
        [Tooltip("이름표 높이(m, 테이블 위)")]
        public float GridLabelHeight = 0.03f;
        [Tooltip("격자를 이 원(MapRadius 배율) 안으로 자른다 — 둥근 테이블 바다 밖(테두리)으로 선이 나가지 않게. 이름표는 이 원 둘레")]
        public float GridClip = 0.97f;

        [Header("마커 원본 (꺼 둔 자식, 프리팹 빌더가 만든다)")]
        public GameObject PlayerMarker;
        public GameObject EnemyAirMarker;
        public GameObject GroundMarker;
        public GameObject ObjectiveMarker;
        public GameObject FriendlyMarker;
        [Tooltip("아군 호위 대상(VIP) — 초록 + 이름표 \"VIP\"(6-285). 없으면 FriendlyMarker")]
        public GameObject VipMarker;
        [Tooltip("아군 편대 풀(지원 호출 · CIC 편대 — 초록, 이름표 없음, 6-285). Bake가 넣는다")]
        public ACO_SupportManager Support;
        public GameObject BaseMarker;

        private const int KIND_PLAYER = 0;
        private const int KIND_FIGHTER = 1;
        private const int KIND_GROUND = 2;
        private const int KIND_HVT = 3;
        private const int KIND_ESCORT = 4;
        private const int KIND_WINGMAN = 5;
        private const int MAX_VERTICES = 65000;

        private ACO_HexTerrain _terrain;
        private Vector3 _center;
        private float _scale;
        private float _seaLevel;
        private int _mapSeed;
        private float _timer;

        // 지형 메시 만들기(프레임마다 나눠서)
        private bool _building;
        private int _cellN;
        private float[] _cellXs;
        private float[] _cellZs;
        private float[] _cellHs;
        private int _landCap;
        private Vector3[] _meshV;
        private Color[] _meshC;
        private int[] _meshT;
        private float[] _cornerX;
        private float[] _cornerZ;
        private int _buildCell;
        private int _buildPlaced;
        private int _buildV;
        private int _buildT;

        private int _count;
        private int[] _kind;
        private int[] _index;
        private Transform[] _target;
        private Transform[] _marker;
        private TrailRenderer[] _trail;
        private GameObject[] _tgtRing;
        private TextMeshPro[] _label;
        private Transform[] _labelT;   // TextMeshPro.transform은 Udon에 열려 있지 않아 따로 둔다
        private bool[] _shown;
        private Vector3[] _home;   // 참가자 기체의 보관 위치(여기 있으면 숨긴다)
        private Transform _base;
        private bool _gridBuilt;

        void Start()
        {
            EnsureScale();
            BuildMarkers();
        }

        private void EnsureScale()
        {
            if (_scale > 0f) { return; }
            if (!MapRoot) { MapRoot = transform; }
            if (Generator) { _terrain = Generator.HexTerrain; }
            _center = _terrain != null ? _terrain.transform.position : (Generator != null ? Generator.transform.position : MapRoot.position);
            float worldRadius = _terrain != null ? _terrain.FieldRadius : FallbackWorldRadius;
            _scale = MapRadius / Mathf.Max(worldRadius, 1f);
            _seaLevel = _terrain != null ? _terrain.SeaLevel : 0f;
        }

        // ───────────── CIC 도구가 쓰는 변환 (6-275) ─────────────

        /// 지도 1m = 월드 몇 분의 1(축척).
        public float GetScale() { EnsureScale(); return _scale; }

        /// 월드 → MapRoot 기준 지도 자리(높이는 과장 포함).
        public Vector3 WorldToMap(Vector3 world) { EnsureScale(); return ToMap(world); }

        /// MapRoot 기준 지도 자리(x, z) → 월드(높이 = 해수면).
        public Vector3 MapToWorld(Vector3 local)
        {
            EnsureScale();
            float s = Mathf.Max(_scale, 0.0000001f);
            return new Vector3(_center.x + local.x / s, _seaLevel, _center.z + local.z / s);
        }

        // ───────────── 마커 ─────────────

        private void BuildMarkers()
        {
            SaccEntity[] aircraft = Flow != null ? Flow.Aircraft : null;
            int players = aircraft != null ? aircraft.Length : 0;
            int fighters = Generator != null && Generator.Fighters != null ? Generator.Fighters.Length : 0;
            int ground = Generator != null && Generator.GroundUnits != null ? Generator.GroundUnits.Length : 0;
            int hvts = Generator != null && Generator.HighValueTargets != null ? Generator.HighValueTargets.Length : 0;
            int escorts = Generator != null && Generator.EscortUnits != null ? Generator.EscortUnits.Length : 0;
            int wingmen = Support != null && Support.Wingmen != null ? Support.Wingmen.Length : 0;
            int total = players + fighters + ground + hvts + escorts + wingmen;

            _kind = new int[total];
            _index = new int[total];
            _target = new Transform[total];
            _marker = new Transform[total];
            _trail = new TrailRenderer[total];
            _tgtRing = new GameObject[total];
            _label = new TextMeshPro[total];
            _labelT = new Transform[total];
            _shown = new bool[total];
            _home = new Vector3[total];
            _count = 0;

            for (int i = 0; i < players; i++)
            {
                if (aircraft[i]) { AddUnit(KIND_PLAYER, i, aircraft[i].transform, PlayerMarker); }
            }
            for (int i = 0; i < fighters; i++)
            {
                if (Generator.Fighters[i]) { AddUnit(KIND_FIGHTER, i, Generator.Fighters[i].transform, EnemyAirMarker); }
            }
            for (int i = 0; i < ground; i++)
            {
                if (Generator.GroundUnits[i]) { AddUnit(KIND_GROUND, i, Generator.GroundUnits[i].transform, GroundMarker); }
            }
            for (int i = 0; i < hvts; i++)
            {
                if (Generator.HighValueTargets[i]) { AddUnit(KIND_HVT, i, Generator.HighValueTargets[i].transform, ObjectiveMarker); }
            }
            for (int i = 0; i < escorts; i++)
            {
                if (!Generator.EscortUnits[i]) { continue; }
                bool hostile = Generator.EscortUnits[i].Hostile;
                int before = _count;
                AddUnit(KIND_ESCORT, i, Generator.EscortUnits[i].transform, hostile ? EnemyAirMarker : (VipMarker ? VipMarker : FriendlyMarker));
                // 아군 호위 이름표 = HUD 마커 약어(6-294 — VIP · CVY · LCAC · CG · FFG · HEL · LHD). 약어가 비었으면 이름표를 숨긴다
                if (!hostile && _count > before && _label[before] != null)
                {
                    string tag = Generator.EscortUnits[i].MarkerLabel;
                    bool hasTag = !string.IsNullOrEmpty(tag);
                    _label[before].text = hasTag ? tag : "";
                    if (_labelT[before] != null) { _labelT[before].gameObject.SetActive(hasTag); }
                }
            }
            // 아군 편대(6-285): 지원 호출 · CIC 편대 모두 — 초록, 이름표 없음(VIP 이름표는 아군 호위 대상만)
            for (int i = 0; i < wingmen; i++)
            {
                if (Support.Wingmen[i]) { AddUnit(KIND_WINGMAN, i, Support.Wingmen[i].transform, FriendlyMarker); }
            }

            if (HomeBase && BaseMarker)
            {
                GameObject b = Object.Instantiate(BaseMarker);
                b.transform.SetParent(MapRoot, false);
                b.SetActive(true);
                _base = b.transform;
            }
        }

        private void AddUnit(int kind, int index, Transform target, GameObject template)
        {
            if (!template) { return; }
            GameObject m = Object.Instantiate(template);
            m.transform.SetParent(MapRoot, false);
            m.SetActive(false);
            int k = _count;
            _kind[k] = kind;
            _index[k] = index;
            _target[k] = target;
            _marker[k] = m.transform;
            _trail[k] = m.GetComponentInChildren<TrailRenderer>(true);
            Transform ring = m.transform.Find("TGT");
            _tgtRing[k] = ring != null ? ring.gameObject : null;
            _labelT[k] = m.transform.Find("Label");
            _label[k] = _labelT[k] != null ? _labelT[k].GetComponent<TextMeshPro>() : null;
            _home[k] = target.position;
            _count++;
        }

        // ───────────── 갱신 ─────────────

        void Update()
        {
            // 지형 메시는 시작했으면 브리핑실을 나가도 끝까지 만든다(프레임당 조금씩).
            if (_building) { BuildStep(); }

            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = UpdateInterval;

            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me) || !MapRoot) { return; }
            // 브리핑실 밖이면 쉰다(마커는 마지막 모습 그대로).
            if (Vector3.Distance(me.GetPosition(), MapRoot.position) > ActiveRadius) { return; }

            if (_terrain)
            {
                int seed = _terrain.GetSeed();
                if (seed != _mapSeed && _terrain.IsBuilt(seed)) { BeginTerrainMesh(seed); }
            }

            Vector3 head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            for (int k = 0; k < _count; k++) { UpdateMarker(k, head); }
            if (_base) { Place(_base, HomeBase, true); }
            if (!_gridBuilt && GridFilter && GridLog) { BuildGrid(); }
        }

        private void UpdateMarker(int k, Vector3 head)
        {
            Transform t = _target[k];
            bool visible = t && IsVisible(k);
            Vector3 local = visible ? ToMap(t.position) : Vector3.zero;
            // 지도 밖(테두리 1.3배 너머)은 숨긴다.
            if (visible && local.x * local.x + local.z * local.z > MapRadius * MapRadius * 1.69f) { visible = false; }

            if (!visible)
            {
                if (_shown[k]) { _marker[k].gameObject.SetActive(false); _shown[k] = false; }
                return;
            }

            bool air = _kind[k] == KIND_PLAYER || _kind[k] == KIND_FIGHTER || _kind[k] == KIND_ESCORT || _kind[k] == KIND_WINGMAN;
            Place(_marker[k], t, air);
            if (!_shown[k])
            {
                _marker[k].gameObject.SetActive(true);
                _shown[k] = true;
                if (_trail[k]) { _trail[k].Clear(); }   // 숨어 있던 자리에서 선이 그어지지 않게
            }

            if (_tgtRing[k])
            {
                int cat = Category(_kind[k]);
                bool tagged = cat >= 0 && Generator.GetUnitTag(cat, _index[k]) != 0;
                if (_tgtRing[k].activeSelf != tagged) { _tgtRing[k].SetActive(tagged); }
            }

            if (_label[k])
            {
                if (_kind[k] == KIND_PLAYER) { _label[k].text = PilotName(k); }
                Transform lt = _labelT[k];
                Vector3 d = lt.position - head;
                if (d.sqrMagnitude > 0.0001f) { lt.rotation = Quaternion.LookRotation(d); }
            }
        }

        private bool IsVisible(int k)
        {
            if (_kind[k] == KIND_PLAYER)
            {
                // 보관 장소에 있는 기체는 숨긴다(출격했거나 누가 타고 있을 때만).
                SaccEntity e = Flow.Aircraft[_index[k]];
                return e && (e.Occupied || Vector3.Distance(e.transform.position, _home[k]) > 200f);
            }
            if (_kind[k] == KIND_WINGMAN)
            {
                // 떠 있는 동안(빠져나가는 중 포함)만 — 격추되면 바로 지운다
                ACO_Wingman w = Support.Wingmen[_index[k]];
                return w && w.gameObject.activeInHierarchy && w.IsActive() && !w.IsDead();
            }
            int cat = Category(_kind[k]);
            if (_kind[k] == KIND_GROUND && Generator.IsCityContactHidden(_index[k])) { return false; }   // 미확인 · 민간 도시 차량(6-270)
            return Generator.IsUnitUsed(cat, _index[k]) && !Generator.IsUnitDestroyed(cat, _index[k]);
        }

        private int Category(int kind)
        {
            if (kind == KIND_FIGHTER) { return ACO_MissionGenerator.CAT_FIGHTER; }
            if (kind == KIND_GROUND) { return ACO_MissionGenerator.CAT_GROUND; }
            if (kind == KIND_HVT) { return ACO_MissionGenerator.CAT_HVT; }
            if (kind == KIND_ESCORT) { return ACO_MissionGenerator.CAT_ESCORT; }
            return -1;
        }

        private string PilotName(int k)
        {
            SaccEntity e = Flow.Aircraft[_index[k]];
            if (!e || !e.Occupied) { return ""; }
            VRCPlayerApi owner = Networking.GetOwner(e.gameObject);
            if (!Utilities.IsValid(owner)) { return ""; }
            string call = TacNames != null ? TacNames.GetLabel(owner) : "";
            return call.Length > 0 ? call + "\n<size=70%>" + owner.displayName + "</size>" : owner.displayName;   // 콜사인이 있으면 위에 크게
        }

        /// 항공기는 기수 방향 그대로, 지상은 방향(요)만.
        private void Place(Transform marker, Transform target, bool fullRotation)
        {
            marker.localPosition = ToMap(target.position);
            marker.localRotation = fullRotation ? target.rotation : Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        }

        private Vector3 ToMap(Vector3 world)
        {
            Vector3 d = world - _center;
            return new Vector3(d.x * _scale, Mathf.Max(world.y - _seaLevel, 0f) * _scale * VerticalExaggeration, d.z * _scale);
        }

        // ───────────── 격자 (6-301) ─────────────

        /// 작전 기록판 격자(열 A~ 서→동, 행 1~ 북→남)를 해수면 높이 선 메시로 한 번 만들고 이름표를 네 변에 놓는다.
        private void BuildGrid()
        {
            _gridBuilt = true;
            EnsureScale();
            Vector2 min = GridLog.GetGridMin();
            Vector2 max = GridLog.GetGridMax();
            int n = GridLog.GetGridDivisions();
            float x0 = (min.x - _center.x) * _scale;
            float x1 = (max.x - _center.x) * _scale;
            float z0 = (min.y - _center.z) * _scale;
            float z1 = (max.y - _center.z) * _scale;
            int lines = (n + 1) * 2;
            Vector3[] v = new Vector3[lines * 4];
            Color[] c = new Color[lines * 4];
            int[] t = new int[lines * 6];
            float w = GridLineWidth * 0.5f;
            float rr = MapRadius * GridClip;
            rr *= rr;
            for (int i = 0; i <= n; i++)
            {
                float f = i / (float)n;
                float x = Mathf.Lerp(x0, x1, f);
                float z = Mathf.Lerp(z0, z1, f);
                // 둥근 테이블 안으로 자른다(원 밖이면 길이 0)
                float zr = Mathf.Sqrt(Mathf.Max(rr - x * x, 0f));
                float xr = Mathf.Sqrt(Mathf.Max(rr - z * z, 0f));
                GridQuad(v, t, i * 2, x - w, Mathf.Max(z0, -zr), x + w, Mathf.Min(z1, zr));
                GridQuad(v, t, i * 2 + 1, Mathf.Max(x0, -xr), z - w, Mathf.Min(x1, xr), z + w);
            }
            for (int i = 0; i < c.Length; i++) { c[i] = Color.white; }
            Mesh mesh = GridFilter.sharedMesh;
            if (!mesh)
            {
                mesh = new Mesh();
                GridFilter.sharedMesh = mesh;
            }
            else { mesh.Clear(); }
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = t;
            mesh.RecalculateBounds();

            if (GridLabels == null) { return; }
            int q = GridLabels.Length / 4;
            float cw = (x1 - x0) / n;
            float ch = (z1 - z0) / n;
            for (int k = 0; k < GridLabels.Length; k++)
            {
                Transform l = GridLabels[k];
                if (!l) { continue; }
                int side = q > 0 ? k / q : 0;
                int idx = q > 0 ? k % q : k;
                bool on = side < 4 && idx < n;
                if (l.gameObject.activeSelf != on) { l.gameObject.SetActive(on); }
                if (!on) { continue; }
                Vector3 p;
                bool col = side < 2;
                // 이름표는 격자 끝(둥근 테이블이면 원 둘레) 바로 바깥
                float cx = x0 + (idx + 0.5f) * cw;
                float rz = z1 - (idx + 0.5f) * ch;
                // 네 모서리에서 첫 · 끝 열(A · J)과 첫 · 끝 행(1 · 10) 이름표가 원 둘레의 같은 자리에 겹친다 — 안쪽으로 조금씩 민다
                if (idx == 0) { cx += cw * 0.3f; rz -= ch * 0.3f; }
                else if (idx == n - 1) { cx -= cw * 0.3f; rz += ch * 0.3f; }
                float edgeZ = Mathf.Sqrt(Mathf.Max(rr - cx * cx, 0f));
                float edgeX = Mathf.Sqrt(Mathf.Max(rr - rz * rz, 0f));
                if (side == 0) { p = new Vector3(cx, GridLabelHeight, Mathf.Min(z1, edgeZ) + GridLabelGap); }         // 북쪽 열
                else if (side == 1) { p = new Vector3(cx, GridLabelHeight, Mathf.Max(z0, -edgeZ) - GridLabelGap); }   // 남쪽 열
                else if (side == 2) { p = new Vector3(Mathf.Max(x0, -edgeX) - GridLabelGap, GridLabelHeight, rz); }   // 서쪽 행(1 = 북)
                else { p = new Vector3(Mathf.Min(x1, edgeX) + GridLabelGap, GridLabelHeight, rz); }                   // 동쪽 행
                l.localPosition = p;
                // 6-316: 테이블 면에 눕혀 고정, 모두 지도 북쪽이 위(남쪽 — CIC 테이블은 도구 패널 쪽 — 에 선 사람이 바로 읽는다). 예전엔 보는 사람 쪽으로 돌았다
                l.localRotation = Quaternion.Euler(90f, 0f, 0f);
                TextMeshPro tmp = l.GetComponent<TextMeshPro>();
                if (tmp) { tmp.text = col ? "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Substring(idx, 1) : (idx + 1).ToString(); }
            }
        }

        private void GridQuad(Vector3[] v, int[] t, int q, float ax, float az, float bx, float bz)
        {
            int vi = q * 4;
            int ti = q * 6;
            // 위에서 볼 때 앞면(셰이더는 Cull Off라 방향은 상관없다)
            v[vi] = new Vector3(ax, 0f, az);
            v[vi + 1] = new Vector3(ax, 0f, bz);
            v[vi + 2] = new Vector3(bx, 0f, bz);
            v[vi + 3] = new Vector3(bx, 0f, az);
            t[ti] = vi; t[ti + 1] = vi + 1; t[ti + 2] = vi + 2;
            t[ti + 3] = vi; t[ti + 4] = vi + 2; t[ti + 5] = vi + 3;
        }

        // ───────────── 지형 메시 (프레임마다 나눠 만든다) ─────────────

        /// 새 지형으로 다시 만들기 시작한다: 땅 칸 수를 세고 배열을 잡는다. 칸 채우기는 BuildStep이 프레임마다 CellsPerFrame칸씩.
        /// 땅 칸마다 육각 기둥(윗면 꼭짓점 6 + 해수면 꼭짓점 6, 윗면 4삼각형 + 옆면 12삼각형). 칸 수 3,000이면 정점 36,000.
        private void BeginTerrainMesh(int seed)
        {
            _mapSeed = seed;
            if (!TerrainFilter) { return; }
            _cellN = _terrain.GetCellCount();
            _cellXs = _terrain.GetCellXs();
            _cellZs = _terrain.GetCellZs();
            _cellHs = _terrain.GetCellHeights();

            int land = 0;
            for (int i = 0; i < _cellN; i++) { if (_cellHs[i] >= _seaLevel) { land++; } }
            // 칸마다 윗면 꼭짓점 6(+ 옆면을 그리면 해수면 꼭짓점 6), 윗면 삼각형 4(+ 옆면 12)
            int perV = TerrainSides ? 12 : 6;
            _landCap = Mathf.Min(land, MAX_VERTICES / perV);

            _meshV = new Vector3[_landCap * perV];
            _meshC = new Color[_landCap * perV];
            _meshT = new int[_landCap * (TerrainSides ? 48 : 12)];

            // 뾰족한 꼭짓점이 ±Z를 향하는 육각형(지형 격자와 같다): 꼭짓점 각도 30° + 60°k.
            float r = _terrain.HexRadius * _scale;
            _cornerX = new float[6];
            _cornerZ = new float[6];
            for (int k = 0; k < 6; k++)
            {
                float a = (30f + 60f * k) * Mathf.Deg2Rad;
                _cornerX[k] = Mathf.Cos(a) * r;
                _cornerZ[k] = Mathf.Sin(a) * r;
            }

            _buildCell = 0;
            _buildPlaced = 0;
            _buildV = 0;
            _buildT = 0;
            _building = true;
        }

        /// CellsPerFrame칸을 채우고, 다 채우면 메시에 넣는다(기존 지도는 그때까지 그대로 보인다).
        private void BuildStep()
        {
            int end = Mathf.Min(_buildCell + Mathf.Max(CellsPerFrame, 1), _cellN);
            for (; _buildCell < end && _buildPlaced < _landCap; _buildCell++)
            {
                int i = _buildCell;
                if (_cellHs[i] < _seaLevel) { continue; }
                _buildPlaced++;
                int vi = _buildV;
                float px = _cellXs[i] * _scale;
                float pz = _cellZs[i] * _scale;
                float py = Mathf.Max((_cellHs[i] - _seaLevel) * _scale * VerticalExaggeration, 0.001f);
                Color top = HeightColor(_cellHs[i]);
                Color side = new Color(top.r * SideShade, top.g * SideShade, top.b * SideShade, 1f);
                for (int k = 0; k < 6; k++)
                {
                    _meshV[vi + k] = new Vector3(px + _cornerX[k], py, pz + _cornerZ[k]);
                    _meshC[vi + k] = top;
                    if (TerrainSides)
                    {
                        _meshV[vi + 6 + k] = new Vector3(px + _cornerX[k], 0f, pz + _cornerZ[k]);
                        _meshC[vi + 6 + k] = side;
                    }
                }
                int ti = _buildT;
                for (int k = 1; k < 5; k++)
                {
                    _meshT[ti++] = vi;
                    _meshT[ti++] = vi + k + 1;
                    _meshT[ti++] = vi + k;
                }
                for (int k = 0; k < 6 && TerrainSides; k++)
                {
                    int a = k;
                    int b = (k + 1) % 6;
                    _meshT[ti++] = vi + a;
                    _meshT[ti++] = vi + b;
                    _meshT[ti++] = vi + 6 + b;
                    _meshT[ti++] = vi + a;
                    _meshT[ti++] = vi + 6 + b;
                    _meshT[ti++] = vi + 6 + a;
                }
                _buildV += TerrainSides ? 12 : 6;
                _buildT = ti;
            }
            if (_buildCell < _cellN && _buildPlaced < _landCap) { return; }

            _building = false;
            Mesh mesh = TerrainFilter.sharedMesh;
            if (!mesh)
            {
                mesh = new Mesh();
                TerrainFilter.sharedMesh = mesh;
            }
            else { mesh.Clear(); }
            mesh.vertices = _meshV;
            mesh.colors = _meshC;
            mesh.triangles = _meshT;
            mesh.RecalculateBounds();
            // 다 쓴 작업 배열은 놓아 준다.
            _meshV = null;
            _meshC = null;
            _meshT = null;
        }

        private Color HeightColor(float h)
        {
            if (HeightColors == null || HeightColors.Length == 0) { return Color.gray; }
            float[] th = _terrain.HeightThresholds;
            int band = 0;
            if (th != null) { for (int i = 0; i < th.Length; i++) { if (h >= th[i]) { band = i + 1; } } }
            return HeightColors[Mathf.Min(band, HeightColors.Length - 1)];
        }
    }
}
