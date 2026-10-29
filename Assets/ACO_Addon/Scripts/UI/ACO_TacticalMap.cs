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

        [Header("마커 원본 (꺼 둔 자식, 프리팹 빌더가 만든다)")]
        public GameObject PlayerMarker;
        public GameObject EnemyAirMarker;
        public GameObject GroundMarker;
        public GameObject ObjectiveMarker;
        public GameObject FriendlyMarker;
        public GameObject BaseMarker;

        private const int KIND_PLAYER = 0;
        private const int KIND_FIGHTER = 1;
        private const int KIND_GROUND = 2;
        private const int KIND_HVT = 3;
        private const int KIND_ESCORT = 4;
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

        void Start()
        {
            if (!MapRoot) { MapRoot = transform; }
            if (Generator) { _terrain = Generator.HexTerrain; }
            _center = _terrain != null ? _terrain.transform.position : (Generator != null ? Generator.transform.position : MapRoot.position);
            float worldRadius = _terrain != null ? _terrain.FieldRadius : FallbackWorldRadius;
            _scale = MapRadius / Mathf.Max(worldRadius, 1f);
            _seaLevel = _terrain != null ? _terrain.SeaLevel : 0f;
            BuildMarkers();
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
            int total = players + fighters + ground + hvts + escorts;

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
                if (Generator.EscortUnits[i]) { AddUnit(KIND_ESCORT, i, Generator.EscortUnits[i].transform, Generator.EscortUnits[i].Hostile ? EnemyAirMarker : FriendlyMarker); }
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

            bool air = _kind[k] == KIND_PLAYER || _kind[k] == KIND_FIGHTER || _kind[k] == KIND_ESCORT;
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
            int cat = Category(_kind[k]);
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
            return Utilities.IsValid(owner) ? owner.displayName : "";
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
            _landCap = Mathf.Min(land, MAX_VERTICES / 12);

            _meshV = new Vector3[_landCap * 12];
            _meshC = new Color[_landCap * 12];
            _meshT = new int[_landCap * 48];

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
                    _meshV[vi + 6 + k] = new Vector3(px + _cornerX[k], 0f, pz + _cornerZ[k]);
                    _meshC[vi + 6 + k] = side;
                }
                int ti = _buildT;
                for (int k = 1; k < 5; k++)
                {
                    _meshT[ti++] = vi;
                    _meshT[ti++] = vi + k + 1;
                    _meshT[ti++] = vi + k;
                }
                for (int k = 0; k < 6; k++)
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
                _buildV += 12;
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
