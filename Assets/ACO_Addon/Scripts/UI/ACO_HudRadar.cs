using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// HUD 레이더(로컬 전용, 동기화 없음). 기체에 타고 있을 때 시야 한쪽(HeadOffset)에 원형 스코프를 띄워 내 주위 Range 안의 적·아군·목표와 날아가는 미사일을 점으로 그린다.
    /// - 방위: 바라보는 방향이 위(머리 기준 수평 방위, HeadingFromVehicle이면 기체 기수). 점은 북쪽 기준으로 그리고 Spin을 매 프레임 돌린다 — 테두리의 N 표시가 북쪽.
    /// - 점: 항공기 = 진행 방향 쉐브론(뒤가 V로 파인 화살촉 — 앞·옆이 헷갈리지 않게), 지상·함선 = 사각형, 미사일 = 진행 방향으로 긴 막대, 나 = 흰 쉐브론(가운데, 기수 방향).
    ///   색은 종류별(적 노랑, 목표 빨강, 아군 파랑, 미사일 흰색 — 모양이 막대라 가운데의 나와 헷갈리지 않는다).
    /// - 점 메시는 UpdateInterval마다 한 번에 다시 채운다(정점만 바꾼다 — 오브젝트를 늘리지 않는다). 접촉 목록은 같은 오브젝트의 ACO_RadarContacts.
    /// - 표시 거리(Range, 최대 10km)·점 크기 배율(BlipScale)·켜기·크기·위치·불투명도·따라오는 지연은 HUD 설정 패널이 바꾼다(거리는 점수 패널과 같다).
    ///   패널을 만지는 동안엔 기체 밖에서도 미리보기로 보인다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_HudRadar : UdonSharpBehaviour
    {
        public const int MAX_BLIPS = 200;

        [Header("참조 (프리팹 빌더/Bake가 만든다)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("스코프 루트(배경·고리). 매 프레임 머리 앞 HeadOffset에 놓는다. 지름 1 = Size(m)")]
        public Transform Root;
        [Tooltip("방위에 따라 도는 판(점 메시가 붙는다). Root의 자식")]
        public Transform Spin;
        [Tooltip("점 메시(ACO/RadarVertexColor). 스크립트가 메시를 만든다")]
        public MeshFilter BlipFilter;

        [Header("표시 (HUD 설정 패널이 바꾼다)")]
        public bool ShowRadar = true;
        [Tooltip("머리 기준 위치(m): 오른쪽(+X), 위(+Y), 앞(+Z)")]
        public Vector3 HeadOffset = new Vector3(-0.32f, -0.2f, 0.9f);
        [Tooltip("스코프 지름(m)")]
        public float Size = 0.2f;
        [Tooltip("불투명도(0~1) — 배경·고리·점 모두에 곱한다")]
        [Range(0.1f, 1f)] public float Opacity = 1f;

        [Header("동작")]
        [Tooltip("표시 거리(m) — 스코프 가장자리 거리. HUD 설정 패널이 2~10km로 바꾼다(MaxRange 이하)")]
        public float Range = 10000f;
        [Tooltip("표시 거리 최대(m)")]
        public float MaxRange = 10000f;
        [Tooltip("불투명도를 적용할 렌더러(배경·고리·점). 비우면 Root 아래 전부")]
        public MeshRenderer[] Renderers;

        [Tooltip("켜면 바라보는 방향 대신 기체 기수가 위")]
        public bool HeadingFromVehicle = false;
        [Tooltip("점 메시를 다시 채우는 간격(초). 회전은 매 프레임")]
        public float UpdateInterval = 0.1f;
        [Tooltip("머리 회전을 따라가는 빠르기(1/s, 클수록 바로 따라온다). HUD 설정 패널의 FOLLOW DELAY가 바꾼다")]
        public float FollowSharpness = 12f;
        [Tooltip("점 크기(스코프 지름 대비)")]
        public float BlipSize = 0.045f;
        [Tooltip("미사일 막대 길이(스코프 지름 대비 — 폭은 이것의 30%)")]
        public float MissileSize = 0.035f;
        [Tooltip("점 크기 배율 — HUD 설정 패널이 바꾼다")]
        public float BlipScale = 1f;

        [Header("색")]
        public Color EnemyColor = new Color(1f, 0.82f, 0.2f, 1f);
        public Color ObjectiveColor = new Color(1f, 0.25f, 0.2f, 1f);
        public Color FriendlyColor = new Color(0.35f, 0.65f, 1f, 1f);
        public Color MissileColor = new Color(1f, 1f, 1f, 1f);
        public Color SelfColor = new Color(1f, 1f, 1f, 1f);
        public Color NorthColor = new Color(0.35f, 1f, 0.45f, 1f);

        private Mesh _mesh;
        private Vector3[] _v;
        private Color[] _c;
        private int _n;
        private float _timer;
        private float _previewUntil;
        private bool _shown;
        private bool _started;
        private Quaternion _smoothRot = Quaternion.identity;
        private bool _hasRot;
        private Material[] _mats;
        private Color[] _baseColors;
        private float _appliedOpacity = -1f;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!Contacts) { Contacts = GetComponent<ACO_RadarContacts>(); }
            _v = new Vector3[MAX_BLIPS * 4];
            _c = new Color[MAX_BLIPS * 4];
            int[] tris = new int[MAX_BLIPS * 6];
            for (int b = 0; b < MAX_BLIPS; b++)
            {
                int v = b * 4;
                int t = b * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _mesh = new Mesh();
            _mesh.vertices = _v;
            _mesh.colors = _c;
            _mesh.triangles = tris;
            _mesh.RecalculateBounds();
            if (BlipFilter) { BlipFilter.sharedMesh = _mesh; }
            if ((Renderers == null || Renderers.Length == 0) && Root != null) { Renderers = Root.GetComponentsInChildren<MeshRenderer>(true); }
            int n = Renderers != null ? Renderers.Length : 0;
            _mats = new Material[n];
            _baseColors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                if (!Renderers[i]) { continue; }
                _mats[i] = Renderers[i].material;
                _baseColors[i] = _mats[i].GetColor("_Color");
            }
            SetShown(false);
        }

        /// 불투명도가 바뀌었을 때만 머티리얼 알파를 다시 넣는다.
        private void ApplyOpacity()
        {
            float o = Mathf.Clamp(Opacity, 0.05f, 1f);
            if (Mathf.Abs(o - _appliedOpacity) < 0.001f) { return; }
            _appliedOpacity = o;
            for (int i = 0; i < _mats.Length; i++)
            {
                if (!_mats[i]) { continue; }
                Color c = _baseColors[i];
                c.a *= o;
                _mats[i].SetColor("_Color", c);
            }
        }

        /// HUD 설정 패널이 부른다: seconds 동안 기체 밖에서도 보여 준다(예시 점 포함).
        public void Preview(float seconds)
        {
            _previewUntil = Time.time + seconds;
            if (Contacts) { Contacts.Wake(seconds); }
        }

        public override void PostLateUpdate()
        {
            Start();
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || Root == null) { return; }
            bool preview = Time.time < _previewUntil;
            bool inVehicle = Contacts != null && Contacts.LocalVehicle != null;
            if (!ShowRadar || (!inVehicle && !preview))
            {
                if (_shown) { SetShown(false); }
                _hasRot = false;
                return;
            }
            if (!_shown) { SetShown(true); _timer = 0f; }
            ApplyOpacity();

            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (!_hasRot) { _smoothRot = head.rotation; _hasRot = true; }
            _smoothRot = Quaternion.Slerp(_smoothRot, head.rotation, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            Root.SetPositionAndRotation(head.position + _smoothRot * HeadOffset, _smoothRot);
            // 부모(표적 마커) 크기와 상관없이 지름 = Size(m)
            Transform parent = Root.parent;
            float ps = parent != null ? Mathf.Max(Mathf.Abs(parent.lossyScale.x), 0.0001f) : 1f;
            Root.localScale = Vector3.one * (Mathf.Max(Size, 0.01f) / ps);

            // 위 = 바라보는 방향(또는 기체 기수)
            Vector3 fwd = HeadingFromVehicle && inVehicle ? Contacts.LocalVehicle.transform.forward : head.rotation * Vector3.forward;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (Spin) { Spin.localRotation = Quaternion.Euler(0f, 0f, yaw); }

            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = UpdateInterval;
            Vector3 center = inVehicle ? Contacts.LocalVehicle.transform.position : local.GetPosition();
            Vector3 selfFwd = inVehicle ? Contacts.LocalVehicle.transform.forward : fwd;
            Rebuild(center, selfFwd, preview && !inVehicle);
        }

        // ───────────── 점 메시 ─────────────

        private void Rebuild(Vector3 center, Vector3 selfFwd, bool demo)
        {
            _n = 0;
            float range = Mathf.Clamp(Range, 1f, Mathf.Max(MaxRange, 1f));
            float k = 0.5f / range;   // 월드 m → 스코프(반지름 0.5)
            float rangeSq = range * range;
            float scale = Mathf.Max(BlipScale, 0.1f);
            float s = BlipSize * 0.5f * scale;

            // 북쪽 표시: 테두리 안쪽, 가운데를 향하는 삼각형(점 크기 배율과 상관없이 같은 크기)
            Tri(new Vector3(0f, 0.47f, 0f), 180f, 0.03f, NorthColor);

            ACO_RadarContacts c = Contacts;
            if (c != null && c.Targets != null)
            {
                for (int i = 0; i < c.Count && _n < MAX_BLIPS - 2; i++)
                {
                    if (!c.Visible[i]) { continue; }
                    Transform t = c.Targets[i];
                    if (t == null) { continue; }
                    Vector3 d = t.position - center;
                    float sq = d.x * d.x + d.z * d.z;
                    if (sq > rangeSq) { continue; }
                    Vector3 p = new Vector3(d.x * k, d.z * k, 0f);
                    int kind = c.Kinds[i];
                    Color col = KindColor(kind);
                    bool air = kind == ACO_RadarContacts.KIND_ENEMY_AIR || kind == ACO_RadarContacts.KIND_FRIENDLY_AIR || kind == ACO_RadarContacts.KIND_OBJECTIVE_AIR;
                    if (air)
                    {
                        Vector3 f = t.forward;
                        Chevron(p, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, s, col);
                    }
                    else { Square(p, s * 0.7f, col); }
                }
                float ms = MissileSize * 0.5f * scale;
                for (int i = 0; i < c.MissileCount && _n < MAX_BLIPS - 1; i++)
                {
                    if (!c.IsMissileFlying(i)) { continue; }
                    Transform mt = c.Missiles[i];
                    Vector3 d = mt.position - center;
                    if (d.x * d.x + d.z * d.z > rangeSq) { continue; }
                    Vector3 mf = mt.forward;
                    Bar(new Vector3(d.x * k, d.z * k, 0f), Mathf.Atan2(mf.x, mf.z) * Mathf.Rad2Deg, ms, MissileColor);
                }
            }
            if (demo)
            {
                // 미리보기 예시: 적기 둘, 목표, 아군, 미사일
                Chevron(new Vector3(0.18f, 0.3f, 0f), 200f, s, EnemyColor);
                Chevron(new Vector3(-0.25f, 0.2f, 0f), 120f, s, EnemyColor);
                Square(new Vector3(0.05f, -0.32f, 0f), s * 0.7f, ObjectiveColor);
                Chevron(new Vector3(-0.1f, -0.12f, 0f), 30f, s, FriendlyColor);
                Bar(new Vector3(0.1f, 0.12f, 0f), 215f, MissileSize * 0.5f * scale, MissileColor);
            }
            // 나: 가운데, 기수 방향
            Chevron(Vector3.zero, Mathf.Atan2(selfFwd.x, selfFwd.z) * Mathf.Rad2Deg, s * 1.15f, SelfColor);

            // 남은 칸은 접는다
            for (int v = _n * 4; v < _v.Length; v++) { _v[v] = Vector3.zero; }
            _mesh.vertices = _v;
            _mesh.colors = _c;
            _mesh.RecalculateBounds();
        }

        private Color KindColor(int kind)
        {
            if (kind == ACO_RadarContacts.KIND_FRIENDLY_AIR || kind == ACO_RadarContacts.KIND_FRIENDLY_GROUND) { return FriendlyColor; }
            if (kind == ACO_RadarContacts.KIND_OBJECTIVE_AIR || kind == ACO_RadarContacts.KIND_OBJECTIVE_GROUND) { return ObjectiveColor; }
            return EnemyColor;
        }

        /// 진행 방향(북쪽 기준 방위, 도)을 향하는 삼각형.
        private void Tri(Vector3 p, float bearingDeg, float s, Color col)
        {
            if (_n >= MAX_BLIPS) { return; }
            float a = bearingDeg * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
            Vector3 r = new Vector3(f.y, -f.x, 0f);
            int v = _n * 4;
            _v[v] = p + f * (s * 1.3f);
            _v[v + 1] = p - f * (s * 0.8f) + r * (s * 0.8f);
            _v[v + 2] = p - f * (s * 0.8f) - r * (s * 0.8f);
            _v[v + 3] = _v[v + 2];
            Fill(v, col);
        }

        /// 진행 방향을 향하는 쉐브론(화살촉): 꼭짓점 → 오른쪽 뒤 → 뒤 가운데 파인 곳 → 왼쪽 뒤. 뒤가 V로 파여 앞·옆이 헷갈리지 않는다.
        /// 삼각형 두 개(꼭짓점·오른쪽·파인 곳 / 꼭짓점·파인 곳·왼쪽)라 점 한 칸(정점 4개)에 들어간다.
        private void Chevron(Vector3 p, float bearingDeg, float s, Color col)
        {
            if (_n >= MAX_BLIPS) { return; }
            float a = bearingDeg * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
            Vector3 r = new Vector3(f.y, -f.x, 0f);
            int v = _n * 4;
            _v[v] = p + f * (s * 1.3f);
            _v[v + 1] = p - f * (s * 0.9f) + r * (s * 0.9f);
            _v[v + 2] = p - f * (s * 0.2f);
            _v[v + 3] = p - f * (s * 0.9f) - r * (s * 0.9f);
            Fill(v, col);
        }

        /// 진행 방향으로 긴 막대(미사일): 길이 2 × len, 폭 0.6 × len.
        private void Bar(Vector3 p, float bearingDeg, float len, Color col)
        {
            if (_n >= MAX_BLIPS) { return; }
            float a = bearingDeg * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * len;
            Vector3 r = new Vector3(f.y, -f.x, 0f) * 0.3f;
            int v = _n * 4;
            _v[v] = p + f - r;
            _v[v + 1] = p + f + r;
            _v[v + 2] = p - f + r;
            _v[v + 3] = p - f - r;
            Fill(v, col);
        }

        private void Square(Vector3 p, float s, Color col)
        {
            if (_n >= MAX_BLIPS) { return; }
            int v = _n * 4;
            _v[v] = p + new Vector3(-s, s, 0f);
            _v[v + 1] = p + new Vector3(s, s, 0f);
            _v[v + 2] = p + new Vector3(s, -s, 0f);
            _v[v + 3] = p + new Vector3(-s, -s, 0f);
            Fill(v, col);
        }

        private void Fill(int v, Color col)
        {
            _c[v] = col; _c[v + 1] = col; _c[v + 2] = col; _c[v + 3] = col;
            _n++;
        }

        private void SetShown(bool on)
        {
            _shown = on;
            if (Root != null && Root.gameObject.activeSelf != on) { Root.gameObject.SetActive(on); }
        }
    }
}
