using SaccFlightAndVehicles;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 조종사 이름표(6-340, 로컬 전용 — 동기화 코드 없음): 다른 플레이어 머리 위에 칭호 · 레벨 · TAC 네임을 띄운다.
    ///  - 값은 그 사람의 PlayerData(레벨 = 강화 단계 합, 칭호 ID)를 ACO_PlayerProgress로 읽는다. 내 이름표는 내 화면에 그리지 않는다.
    ///  - 기체 좌석(Vehicles의 SAV 좌석 — 조종석 · 뒷자리)에 앉은 사람은 숨긴다. 갑판 · 브리핑실 · CIC에서는 미션 중에도 보인다.
    ///  - MaxDistance 안의 가까운 순으로 Labels 개수(프리팹 32개)까지. 고르기는 PickInterval마다, 글자는 TextInterval마다, 위치 · 방향은 매 프레임(보이는 것만).
    ///  - HUD 설정 PILOT TAGS(로컬)로 끄고 켠다(SetShown).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PilotTag : UdonSharpBehaviour
    {
        [Header("참조 (Bake가 넣는다)")]
        public ACO_PlayerProgress Progress;
        public ACO_TacName TacNames;
        [Tooltip("플레이어 기체(ACO_VehicleUpgrades가 있는 SaccEntity) — 이 좌석에 앉은 사람은 이름표를 숨긴다")]
        public SaccEntity[] Vehicles;

        [Header("이름표 (프리팹 빌더가 채운다)")]
        public TextMeshPro[] Labels;

        [Header("표시")]
        [Tooltip("이 거리(m) 안의 사람만")]
        public float MaxDistance = 25f;
        [Tooltip("머리 위 높이(m) — 눈높이 1.6m 아바타 기준, 아바타 크기에 비례")]
        public float HeightAboveHead = 0.64f;   // 2026-11-21 0.32 → 0.64(VRChat 이름표와 겹쳤다 — 사용자 "2배 정도")
        [Tooltip("글자 크기 배율 — 눈높이 1.6m 아바타 기준, 아바타 크기에 비례(MinScale ~ MaxScale)")]
        public float BaseScale = 1f;
        public float MinScale = 0.4f;
        public float MaxScale = 4f;
        [Tooltip("칭호 줄 색")]
        public Color TitleColor = new Color(1f, 0.82f, 0.36f, 1f);
        [Tooltip("레벨 색(최대 레벨이면 MaxLevelColor)")]
        public Color LevelColor = new Color(0.5f, 0.85f, 1f, 1f);
        public Color MaxLevelColor = new Color(1f, 0.8f, 0.2f, 1f);
        public float PickInterval = 0.5f;
        public float TextInterval = 2f;

        private const int MAX_PLAYERS = 100;
        private VRCPlayerApi[] _players = new VRCPlayerApi[MAX_PLAYERS];
        private VRCPlayerApi[] _shownPlayers = new VRCPlayerApi[0];
        private int[] _shownIds = new int[0];
        private string[] _shownText = new string[0];
        private Transform[] _labelT = new Transform[0];   // TextMeshPro.transform은 Udon에 없다 — gameObject.transform을 모아 둔다
        private float[] _dist = new float[MAX_PLAYERS];
        private bool _shown = true;
        private bool _ready;
        private float _pickTimer;
        private float _textTimer;
        private string _lvColorHex = "";
        private string _maxColorHex = "";
        private string _titleColorHex = "";

        void Start()
        {
            if (_ready) { return; }
            _ready = true;
            int n = Labels != null ? Labels.Length : 0;
            _shownPlayers = new VRCPlayerApi[n];
            _shownIds = new int[n];
            _shownText = new string[n];
            _labelT = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                _shownIds[i] = -1;
                _shownText[i] = "";
                if (Labels[i]) { _labelT[i] = Labels[i].gameObject.transform; Labels[i].gameObject.SetActive(false); }
            }
            _lvColorHex = Hex(LevelColor);
            _maxColorHex = Hex(MaxLevelColor);
            _titleColorHex = Hex(TitleColor);
        }

        /// HUD 설정 PILOT TAGS(로컬).
        public void SetShown(bool on)
        {
            Start();
            if (_shown == on) { return; }
            _shown = on;
            if (!on) { HideAll(); }
            else { _pickTimer = 0f; _textTimer = 0f; }
        }

        void Update()
        {
            if (!_ready || !_shown || Labels == null || Labels.Length == 0) { return; }
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) { return; }

            _pickTimer -= Time.deltaTime;
            _textTimer -= Time.deltaTime;
            bool picked = false;
            if (_pickTimer <= 0f)
            {
                _pickTimer = PickInterval;
                Pick(local);
                picked = true;
            }
            if (picked || _textTimer <= 0f)
            {
                bool all = _textTimer <= 0f;
                if (all) { _textTimer = TextInterval; }
                RefreshTexts(all);
            }
            Place(local);
        }

        /// 가까운 순으로 이름표를 줄 사람을 고른다(나 · 기체 좌석 · 먼 사람 제외). 이미 보이던 사람은 같은 칸을 그대로 쓴다(깜빡임 없음).
        private void Pick(VRCPlayerApi local)
        {
            int count = Mathf.Min(VRCPlayerApi.GetPlayerCount(), MAX_PLAYERS);
            VRCPlayerApi.GetPlayers(_players);
            Vector3 me = local.GetPosition();
            float maxSq = MaxDistance * MaxDistance;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi p = _players[i];
                _dist[i] = -1f;
                if (!Utilities.IsValid(p) || p.isLocal) { continue; }
                float d = (p.GetPosition() - me).sqrMagnitude;
                if (d > maxSq || IsSeated(p.playerId)) { continue; }
                _dist[i] = d;
            }
            int slots = Labels.Length;
            // 남길 사람: 가까운 순 slots명
            int keep = 0;
            int[] chosen = new int[slots];
            for (int k = 0; k < slots; k++)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (_dist[i] < 0f) { continue; }
                    if (best < 0 || _dist[i] < _dist[best]) { best = i; }
                }
                if (best < 0) { break; }
                chosen[keep++] = _players[best].playerId;
                _dist[best] = -1f;
            }
            // 칸 비우기: 고른 사람에 없는 칸
            for (int s = 0; s < slots; s++)
            {
                if (_shownIds[s] < 0) { continue; }
                bool stay = false;
                for (int k = 0; k < keep; k++) { if (chosen[k] == _shownIds[s]) { stay = true; chosen[k] = -1; break; } }
                if (!stay) { ClearSlot(s); }
            }
            // 새 사람을 빈 칸에
            for (int k = 0; k < keep; k++)
            {
                if (chosen[k] < 0) { continue; }
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(chosen[k]);
                if (!Utilities.IsValid(p)) { continue; }
                for (int s = 0; s < slots; s++)
                {
                    if (_shownIds[s] >= 0 || Labels[s] == null) { continue; }
                    _shownIds[s] = chosen[k];
                    _shownPlayers[s] = p;
                    _shownText[s] = "";
                    break;
                }
            }
        }

        private bool IsSeated(int playerId)
        {
            if (Vehicles == null) { return false; }
            for (int v = 0; v < Vehicles.Length; v++)
            {
                SaccEntity e = Vehicles[v];
                if (e == null || e.SeatedPlayers == null) { continue; }
                int[] seated = e.SeatedPlayers;
                for (int i = 0; i < seated.Length; i++) { if (seated[i] == playerId) { return true; } }
            }
            return false;
        }

        /// 글자: 칭호(없으면 줄 생략) / 레벨 + TAC 네임(없으면 VRChat 이름). all = false면 새로 받은 칸만.
        private void RefreshTexts(bool all)
        {
            int maxLv = Progress != null ? Progress.GetPlayerLevelMax() : 0;
            for (int s = 0; s < Labels.Length; s++)
            {
                if (_shownIds[s] < 0 || Labels[s] == null) { continue; }
                if (!all && _shownText[s].Length > 0) { continue; }
                VRCPlayerApi p = _shownPlayers[s];
                if (!Utilities.IsValid(p)) { ClearSlot(s); continue; }
                string name = TacNames != null ? TacNames.GetLabel(p) : "";
                if (name.Length == 0) { name = p.displayName; }
                name = Clean(name);
                string text = "";
                if (Progress != null)
                {
                    string title = Progress.GetPlayerTitleText(p);
                    if (title.Length > 0) { text = "<size=70%><color=" + _titleColorHex + ">" + title + "</color></size>\n"; }
                    int lv = Progress.GetPlayerLevel(p);
                    if (lv >= 0)
                    {
                        string hex = maxLv > 0 && lv >= maxLv ? _maxColorHex : _lvColorHex;
                        text += "<color=" + hex + ">" + Progress.FormatLevel(lv) + "</color>  ";
                    }
                }
                text += name;
                if (text == _shownText[s]) { continue; }
                _shownText[s] = text;
                Labels[s].text = text;
                if (!Labels[s].gameObject.activeSelf) { Labels[s].gameObject.SetActive(true); }
            }
        }

        /// 머리 위에, 나(카메라)를 향하게. 크기 · 높이는 그 아바타의 눈높이에 비례.
        private void Place(VRCPlayerApi local)
        {
            Vector3 eye = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            for (int s = 0; s < Labels.Length; s++)
            {
                if (_shownIds[s] < 0 || Labels[s] == null || _labelT[s] == null || !Labels[s].gameObject.activeSelf) { continue; }
                VRCPlayerApi p = _shownPlayers[s];
                if (!Utilities.IsValid(p)) { ClearSlot(s); continue; }
                float eyeH = p.GetAvatarEyeHeightAsMeters();
                float k = eyeH > 0.05f ? eyeH / 1.6f : 1f;
                Vector3 head = p.GetBonePosition(HumanBodyBones.Head);
                if (head.sqrMagnitude < 0.0001f) { head = p.GetPosition() + Vector3.up * (eyeH > 0.05f ? eyeH : 1.6f); }
                Vector3 pos = head + Vector3.up * (HeightAboveHead * k);
                Transform t = _labelT[s];
                Vector3 look = pos - eye;
                if (look.sqrMagnitude > 0.0001f) { t.rotation = Quaternion.LookRotation(look.normalized, Vector3.up); }
                t.position = pos;
                float sc = Mathf.Clamp(BaseScale * k, MinScale, MaxScale);
                t.localScale = new Vector3(sc, sc, sc);
            }
        }

        private void ClearSlot(int s)
        {
            _shownIds[s] = -1;
            _shownPlayers[s] = null;
            _shownText[s] = "";
            if (Labels[s]) { Labels[s].gameObject.SetActive(false); }
        }

        private void HideAll()
        {
            if (Labels == null) { return; }
            for (int s = 0; s < Labels.Length; s++) { if (s < _shownIds.Length) { ClearSlot(s); } }
        }

        /// 색 → "#RRGGBB"(ColorUtility는 Udon에 없다).
        private string Hex(Color c)
        {
            return "#" + Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255).ToString("X2") + Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255).ToString("X2") + Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255).ToString("X2");
        }

        /// TMP 태그로 해석되지 않게 꺾쇠를 바꾼다.
        private string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) { return ""; }
            name = name.Replace("<", "(").Replace(">", ")");
            return name.Length > 24 ? name.Substring(0, 24) : name;
        }
    }
}
