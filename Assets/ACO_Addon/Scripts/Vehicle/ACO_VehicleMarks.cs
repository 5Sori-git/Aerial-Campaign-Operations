using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 기체 표시(헐넘버·콜사인·킬마크, 양쪽 면). 동기화 코드 없음 — 조종사의 PlayerData는 VRChat이 모두에게 보내 준다.
    /// 프로젝터가 아니라 기체에 붙인 판이다(ACO/MarkText: 카메라 쪽으로 당겨 Z파이팅이 없고, 멀면 사라진다).
    /// 탑승자가 있는 동안만 보이고, 내가 ShowRange 밖이면 끈다. 값이 바뀔 때만 다시 만든다.
    /// 글자는 Black Ops One(A-Z 0-9 - 공백만 담은 고정 폰트 — 콜사인·헐넘버 입력 규칙과 같다).
    /// 킬마크: 한 미션 최고 격추수를 100·10·1 단위 아이콘으로 줄여 그린다(날개 달린 별 / 전투기 3기 편대 / 전투기). 폰트에 기호가 없어 메시로 만든다.
    /// 격납고 미리보기(PreviewMode): 데칼 샘플 기체에 얹혀 탑승자 대신 내 값을 보여 준다.
    /// 자리: 기체 프리팹의 ACO_Marks/Marks 아래를 옮기면 된다 — Bake가 미리보기로 같은 자리를 복사한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_VehicleMarks : UdonSharpBehaviour
    {
        private const int ICON_KINDS = 3;

        [Header("참조 (Bake가 채운다)")]
        public SaccEntity EntityControl;
        [Tooltip("격납고 샘플 기체용: 탑승자와 상관없이 내 헐넘버·콜사인·킬마크를 보여 준다")]
        public bool PreviewMode;
        [Tooltip("글자 묶음 — 보이지 않을 때 이것만 끈다")]
        public GameObject Root;
        public TextMeshPro[] HullTexts;
        public TextMeshPro[] NameTexts;
        [Tooltip("킬마크 메시를 넣을 곳(양쪽 면이 같은 메시를 쓴다)")]
        public MeshFilter[] KillFilters;

        [Header("표시")]
        [Tooltip("내가 이 거리(m) 밖이면 끈다 — 글자가 사라지는 거리(셰이더 _FadeEnd)보다 길게")]
        public float ShowRange = 400f;
        [Tooltip("다시 읽는 주기(초)")]
        public float Interval = 1f;
        [Tooltip("킬마크 아이콘 한 변(m)")]
        public float IconSize = 0.1f;
        [Tooltip("킬마크 한 줄의 칸 수 — 넘으면 아랫줄로")]
        public int IconColumns = 10;
        [Tooltip("그리는 아이콘 최대 수")]
        public int MaxIcons = 30;
        [Tooltip("킬마크 색")]
        public Color IconColor = Color.black;

        private float _timer;
        private bool _shown = true;
        private string _hull;
        private string _name;
        private int _kills = -1;
        private Mesh _killMesh;

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = Interval;
            Refresh();
        }

        private void Refresh()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            bool show = Utilities.IsValid(me) && (me.GetPosition() - transform.position).sqrMagnitude < ShowRange * ShowRange;
            if (!PreviewMode) { show = show && EntityControl != null && EntityControl.Occupied && !EntityControl.dead; }
            VRCPlayerApi pilot = !show ? null : (PreviewMode ? me : Networking.GetOwner(EntityControl.gameObject));
            if (!Utilities.IsValid(pilot)) { show = false; }
            SetShown(show);
            if (!show) { return; }

            string hull;
            if (!PlayerData.TryGetString(pilot, ACO_TacName.KEY_HULL, out hull)) { hull = ""; }
            if (hull != _hull)
            {
                _hull = hull;
                SetAll(HullTexts, hull);
            }
            string callsign;
            if (!PlayerData.TryGetString(pilot, ACO_TacName.KEY, out callsign)) { callsign = ""; }
            if (callsign != _name)
            {
                _name = callsign;
                SetAll(NameTexts, callsign);
            }
            int kills;
            if (!PlayerData.TryGetInt(pilot, ACO_PlayerProgress.KEY_BEST_KILLS, out kills)) { kills = 0; }
            if (kills != _kills)
            {
                _kills = kills;
                BuildKills(kills);
            }
        }

        private void SetShown(bool on)
        {
            if (on == _shown) { return; }
            _shown = on;
            if (Root) { Root.SetActive(on); }
        }

        private void SetAll(TextMeshPro[] texts, string value)
        {
            if (texts == null) { return; }
            for (int i = 0; i < texts.Length; i++) { if (texts[i]) { texts[i].text = value; } }
        }

        /// 123 = 날개 달린 별 1, 편대 2, 전투기 3. 큰 단위부터 왼쪽 위에서 채우고, 줄마다 가운데 맞춘다.
        private void BuildKills(int kills)
        {
            if (kills < 0) { kills = 0; }
            int hundreds = kills / 100;
            int tens = kills / 10 % 10;
            int ones = kills % 10;
            int n = Mathf.Min(hundreds + tens + ones, Mathf.Max(MaxIcons, 1));
            var v = new Vector3[n * 4];
            var uv = new Vector2[n * 4];
            var c = new Color[n * 4];
            var tris = new int[n * 6];
            int cols = Mathf.Max(IconColumns, 1);
            int rows = (n + cols - 1) / cols;
            float cellU = 1f / ICON_KINDS;
            for (int i = 0; i < n; i++)
            {
                int kind = i < hundreds ? 2 : (i < hundreds + tens ? 1 : 0);
                int row = i / cols;
                int inRow = row < rows - 1 ? cols : n - row * cols;
                float x = (i % cols - inRow * 0.5f) * IconSize;
                float y = (rows * 0.5f - row - 1f) * IconSize;
                int b = i * 4;
                v[b] = new Vector3(x, y, 0f);
                v[b + 1] = new Vector3(x, y + IconSize, 0f);
                v[b + 2] = new Vector3(x + IconSize, y + IconSize, 0f);
                v[b + 3] = new Vector3(x + IconSize, y, 0f);
                float u0 = kind * cellU;
                uv[b] = new Vector2(u0, 0f);
                uv[b + 1] = new Vector2(u0, 1f);
                uv[b + 2] = new Vector2(u0 + cellU, 1f);
                uv[b + 3] = new Vector2(u0 + cellU, 0f);
                c[b] = IconColor;
                c[b + 1] = IconColor;
                c[b + 2] = IconColor;
                c[b + 3] = IconColor;
                int t = i * 6;
                tris[t] = b;
                tris[t + 1] = b + 1;
                tris[t + 2] = b + 2;
                tris[t + 3] = b;
                tris[t + 4] = b + 2;
                tris[t + 5] = b + 3;
            }
            if (_killMesh == null)
            {
                _killMesh = new Mesh();
                if (KillFilters != null)
                {
                    for (int i = 0; i < KillFilters.Length; i++) { if (KillFilters[i]) { KillFilters[i].sharedMesh = _killMesh; } }
                }
            }
            _killMesh.Clear();
            _killMesh.vertices = v;
            _killMesh.uv = uv;
            _killMesh.colors = c;
            _killMesh.triangles = tris;
            _killMesh.RecalculateBounds();
        }
    }
}
