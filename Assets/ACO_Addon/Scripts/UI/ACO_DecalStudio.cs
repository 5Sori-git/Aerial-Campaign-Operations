using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.Image;
using VRC.SDK3.Persistence;
using VRC.Udon.Common.Interfaces;

namespace ACO
{
    /// 데칼 스튜디오(격납고, 로컬 — 동기화 없음): 내 데칼 이미지(URL)와 자리를 정한다. 실기체에는 탑승할 때 ACO_VehicleDecal이 보낸다.
    ///  - 이미지: URL 입력 칸(VRCUrlInputField — VRChat 키보드)에 붙여 넣고 LOAD. VRChat 제약상 URL은 저장할 수 없어 접속마다 다시 넣는다.
    ///    받기는 이 스크립트 하나가 모든 데칼(내 것·다른 사람 기체 것)을 맡는다: 같은 URL은 한 번만(캐시), 월드 전체 제한(5초에 1장)에 맞춰 차례로(DownloadGap).
    ///  - 자리: 격납고 샘플 기체(SampleRoot — 실기체와 같은 모델, 같은 기준점)에 데칼 건(ACO_DecalGun)을 겨누고 방아쇠를 누르는 순간 그 자리에 찍고 저장. 데칼 2개(1·2를 골라 놓는다),
    ///    SIZE ±(×1.15), ROTATE ±(15°), FLIP(좌우), MIRROR(1을 기체 좌우 대칭 자리에 2로 — 좌우도 뒤집어 밖에서 바로 읽힌다), CLEAR(고른 데칼 끄기).
    ///    자리는 샘플 기준 상대값(위치·회전·크기·뒤집기·켜짐)으로 플레이어 영구 저장에 기종(DecalKey)별로 남는다 — 확정·조정할 때마다 바로.
    ///  - HIDE OTHERS: 다른 사람 기체의 데칼을 내 화면에서 숨긴다(저장됨).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_DecalStudio : UdonSharpBehaviour
    {
        public const int ACT_LOAD = 0;
        public const int ACT_SELECT = 1;
        public const int ACT_SIZE = 2;
        public const int ACT_ROTATE = 3;
        public const int ACT_FLIP = 4;
        public const int ACT_MIRROR = 5;
        public const int ACT_CLEAR = 6;
        public const int ACT_HIDE_OTHERS = 7;
        private const int CACHE = 16;
        private const string KEY_PREFIX = "aco_decal_";
        private const string KEY_HIDE = "aco_decal_hide_others";

        [Header("샘플 기체 (씬 — 직접 넣는다)")]
        [Tooltip("격납고 샘플 기체의 기준점 — 실기체의 기준점(SaccEntity 루트)과 같은 자리·같은 모델이어야 자리가 그대로 옮겨진다")]
        public Transform SampleRoot;
        [Tooltip("이 샘플이 나타내는 기종(실기체 ACO_VehicleDecal.DecalKey와 같아야 한다)")]
        public string DecalKey = "default";

        [Header("구성 (프리팹 빌더·Bake)")]
        public VRCUrlInputField UrlInput;
        [Tooltip("샘플 기체의 미리보기 프로젝터 2개(SampleRoot 자식으로 Bake가 옮긴다)와 각 머티리얼(프로젝터마다 따로)")]
        public Transform[] PreviewProjectors;
        public Material[] PreviewMaterials;
        public TextMeshProUGUI StatusText;
        public TextMeshProUGUI SelectText;
        public TextMeshProUGUI SizeText;
        public TextMeshProUGUI HideText;
        public ACO_UiSound Sound;
        [Tooltip("실기체 데칼 전부(Bake) — HIDE OTHERS를 바꾸면 바로 다시 그린다")]
        public ACO_VehicleDecal[] VehicleDecals;

        [Header("값")]
        public float MinSize = 0.2f;
        public float MaxSize = 6f;
        [Tooltip("프로젝터 깊이(m) — 표면 앞뒤로 이만큼만 비춘다")]
        public float Depth = 0.6f;
        [Tooltip("다운로드 간격(초) — VRChat 제한 5초")]
        public float DownloadGap = 5.5f;

        private VRCUrl _url = VRCUrl.Empty;
        private int _selected;
        private bool _restored;
        private bool _hideOthers;
        // 자리(샘플 기준) — 0·1
        private Vector3[] _pos = new Vector3[2];
        private Quaternion[] _rot = new Quaternion[2];
        private float[] _size = new float[2];
        private bool[] _flip = new bool[2];
        private bool[] _on = new bool[2];
        // 이미지 캐시·대기열
        private VRCImageDownloader _downloader;
        private string[] _cacheUrl = new string[CACHE];
        private Texture2D[] _cacheTex = new Texture2D[CACHE];
        private int[] _cacheState = new int[CACHE];   // 0 없음, 1 대기, 2 받는 중, 3 성공, 4 실패
        private VRCUrl[] _cacheVrcUrl = new VRCUrl[CACHE];
        private float _nextDownload;
        private int _downloading = -1;

        void Start()
        {
            _downloader = new VRCImageDownloader();
            for (int i = 0; i < 2; i++) { _rot[i] = Quaternion.identity; _size[i] = 1f; }
            ApplyPreview();
            UpdateTexts("PASTE AN IMAGE URL AND PRESS LOAD");
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            _restored = true;
            int h;
            _hideOthers = PlayerData.TryGetInt(player, KEY_HIDE, out h) && h > 0;
            for (int i = 0; i < 2; i++)
            {
                _on[i] = HasPose(DecalKey, i);
                if (!_on[i]) { continue; }
                _pos[i] = GetPos(DecalKey, i);
                _rot[i] = GetRot(DecalKey, i);
                _size[i] = GetSize(DecalKey, i);
                _flip[i] = GetFlip(DecalKey, i);
            }
            ApplyPreview();
            UpdateTexts("");
        }

        void OnDestroy()
        {
            if (_downloader != null) { _downloader.Dispose(); }
        }

        // ───────────── 패널 ─────────────

        /// URL 입력 칸 입력 끝.
        public void OnUrlEntered() { Press(ACT_LOAD, 0); }

        /// 패널 버튼(ACO_DecalButton). step = ±1.
        public int Press(int action, int step)
        {
            if (action == ACT_LOAD)
            {
                VRCUrl u = UrlInput != null ? UrlInput.GetUrl() : VRCUrl.Empty;
                if (u == null || string.IsNullOrEmpty(u.Get())) { UpdateTexts("<color=#FF5544>NO URL</color>"); return ACO_UiSound.KIND_DENY; }
                _url = u;
                Request(_url);
                UpdateTexts("LOADING IMAGE...");
                return ACO_UiSound.KIND_CONFIRM;
            }
            if (action == ACT_SELECT) { _selected = _selected == 0 ? 1 : 0; UpdateTexts(""); return ACO_UiSound.KIND_CLICK; }
            if (action == ACT_HIDE_OTHERS)
            {
                _hideOthers = !_hideOthers;
                if (_restored) { PlayerData.SetInt(KEY_HIDE, _hideOthers ? 1 : 0); }
                if (VehicleDecals != null) { for (int i = 0; i < VehicleDecals.Length; i++) { if (VehicleDecals[i]) { VehicleDecals[i].Refresh(); } } }
                UpdateTexts("");
                return ACO_UiSound.KIND_CLICK;
            }
            if (!_restored) { UpdateTexts("<color=#FF5544>LOADING SAVE DATA...</color>"); return ACO_UiSound.KIND_DENY; }
            int k = _selected;
            if (action == ACT_CLEAR)
            {
                _on[k] = false;
                Save(k);
                ApplyPreview();
                UpdateTexts("DECAL " + (k + 1) + " CLEARED");
                return ACO_UiSound.KIND_CLICK;
            }
            if (action == ACT_MIRROR)
            {
                if (!_on[0]) { UpdateTexts("<color=#FF5544>PLACE DECAL 1 FIRST</color>"); return ACO_UiSound.KIND_DENY; }
                // 기체 좌우 대칭(샘플 기준 X = 0 면): 위치 x 반대, 회전 = 거울 반사(qx, -qy, -qz, qw), 좌우 뒤집기
                Quaternion q = _rot[0];
                _pos[1] = new Vector3(-_pos[0].x, _pos[0].y, _pos[0].z);
                _rot[1] = new Quaternion(q.x, -q.y, -q.z, q.w);
                _size[1] = _size[0];
                _flip[1] = !_flip[0];
                _on[1] = true;
                Save(1);
                ApplyPreview();
                UpdateTexts("DECAL 1 MIRRORED TO DECAL 2");
                return ACO_UiSound.KIND_CONFIRM;
            }
            if (!_on[k]) { UpdateTexts("<color=#FF5544>PLACE DECAL " + (k + 1) + " FIRST (AIM THE DECAL GUN, PULL TRIGGER)</color>"); return ACO_UiSound.KIND_DENY; }
            if (action == ACT_SIZE) { _size[k] = Mathf.Clamp(_size[k] * (step >= 0 ? 1.15f : 1f / 1.15f), MinSize, MaxSize); }
            else if (action == ACT_ROTATE) { _rot[k] = _rot[k] * Quaternion.Euler(0f, 0f, step >= 0 ? -15f : 15f); }
            else if (action == ACT_FLIP) { _flip[k] = !_flip[k]; }
            Save(k);
            ApplyPreview();
            UpdateTexts("");
            return ACO_UiSound.KIND_CLICK;
        }

        // ───────────── 데칼 건 ─────────────

        /// 방아쇠를 누른 순간: 데칼 건이 맞힌 표면(point·normal)에 고른 데칼을 바로 찍고 저장한다(건의 위쪽 = 데칼 위쪽).
        /// 겨누는 동안 따라다니는 미리보기는 없다 — 누르자마자 그 자리에 깔린다. 크기·뒤집기는 그대로.
        public bool PlaceAt(Vector3 point, Vector3 normal, Vector3 gunUp)
        {
            if (SampleRoot == null) { return false; }
            Vector3 fwd = -normal;
            Vector3 up = Vector3.ProjectOnPlane(gunUp, fwd);
            if (up.sqrMagnitude < 0.0001f) { up = Vector3.ProjectOnPlane(Vector3.up, fwd); }
            Quaternion world = Quaternion.LookRotation(fwd, up);
            Vector3 p = point + normal * (Depth * 0.5f);
            int k = _selected;
            _pos[k] = SampleRoot.InverseTransformPoint(p);
            _rot[k] = Quaternion.Inverse(SampleRoot.rotation) * world;
            if (_size[k] <= 0f) { _size[k] = 1f; }
            _on[k] = true;
            ApplyPreviewOne(k, true);
            // 저장은 플레이어 데이터를 받은 뒤에만(접속 직후 잠깐) — 그 전엔 화면에만 찍힌다
            if (_restored) { Save(k); }
            UpdateTexts("DECAL " + (k + 1) + (_restored ? " PLACED (SAVED)" : " PLACED (NOT SAVED — DATA LOADING)"));
            return true;
        }

        // ───────────── 미리보기 ─────────────

        private void ApplyPreview()
        {
            for (int i = 0; i < 2; i++) { ApplyPreviewOne(i, _on[i]); }
        }

        private void ApplyPreviewOne(int i, bool visible)
        {
            if (PreviewProjectors == null || i >= PreviewProjectors.Length || PreviewProjectors[i] == null) { return; }
            Transform t = PreviewProjectors[i];
            // 샘플 기준 상대값 → 월드(프로젝터의 부모와 상관없이 — Bake가 SampleRoot 아래로 못 옮겼어도 샘플에 찍힌다)
            if (SampleRoot != null) { t.SetPositionAndRotation(SampleRoot.TransformPoint(_pos[i]), SampleRoot.rotation * _rot[i]); }
            else { t.localPosition = _pos[i]; t.localRotation = _rot[i]; }
            Material m = PreviewMaterials != null && i < PreviewMaterials.Length ? PreviewMaterials[i] : null;
            if (m == null) { return; }
            Texture2D tex = GetTexture(_url);
            m.SetFloat("_Size", _size[i]);
            m.SetFloat("_Flip", _flip[i] ? 1f : 0f);
            if (tex != null) { m.SetTexture("_MainTex", tex); }
            // 이미지가 아직 없으면 흰 사각형으로 자리만 보인다
            m.SetFloat("_Visible", visible ? 1f : 0f);
        }

        private void UpdateTexts(string message)
        {
            if (SelectText) { SelectText.text = "EDITING: DECAL " + (_selected + 1) + (_on[_selected] ? "" : "  <color=#7FBF8A>(NOT PLACED)</color>"); }
            if (SizeText) { SizeText.text = _on[_selected] ? "SIZE " + _size[_selected].ToString("0.0") + " m" + (_flip[_selected] ? "  FLIPPED" : "") : "SIZE -"; }
            if (HideText) { HideText.text = _hideOthers ? "OTHERS' DECALS: <color=#FF5544>HIDDEN</color>" : "OTHERS' DECALS: SHOWN"; }
            if (StatusText && message.Length > 0) { StatusText.text = message; }
        }

        // ───────────── 저장 (플레이어 영구 저장 — 기종별, 샘플 기준 상대값) ─────────────

        private string Key(string key, int i, string field) { return KEY_PREFIX + key + "_" + i + "_" + field; }

        private void Save(int i)
        {
            if (!_restored) { return; }
            string key = DecalKey;
            PlayerData.SetInt(Key(key, i, "on"), _on[i] ? 1 : 0);
            PlayerData.SetFloat(Key(key, i, "px"), _pos[i].x);
            PlayerData.SetFloat(Key(key, i, "py"), _pos[i].y);
            PlayerData.SetFloat(Key(key, i, "pz"), _pos[i].z);
            PlayerData.SetFloat(Key(key, i, "rx"), _rot[i].x);
            PlayerData.SetFloat(Key(key, i, "ry"), _rot[i].y);
            PlayerData.SetFloat(Key(key, i, "rz"), _rot[i].z);
            PlayerData.SetFloat(Key(key, i, "rw"), _rot[i].w);
            PlayerData.SetFloat(Key(key, i, "size"), _size[i]);
            PlayerData.SetInt(Key(key, i, "flip"), _flip[i] ? 1 : 0);
        }

        private float F(string key, int i, string field, float fallback)
        {
            float v;
            VRCPlayerApi me = Networking.LocalPlayer;
            return Utilities.IsValid(me) && PlayerData.TryGetFloat(me, Key(key, i, field), out v) ? v : fallback;
        }

        private int I(string key, int i, string field)
        {
            int v;
            VRCPlayerApi me = Networking.LocalPlayer;
            return Utilities.IsValid(me) && PlayerData.TryGetInt(me, Key(key, i, field), out v) ? v : 0;
        }

        /// 내 저장된 데칼 i(기종 key)가 있고 켜져 있는가 — ACO_VehicleDecal이 탑승할 때 읽는다.
        public bool HasPose(string key, int i) { return I(key, i, "on") > 0; }
        public Vector3 GetPos(string key, int i) { return new Vector3(F(key, i, "px", 0f), F(key, i, "py", 0f), F(key, i, "pz", 0f)); }
        public Quaternion GetRot(string key, int i)
        {
            Quaternion q = new Quaternion(F(key, i, "rx", 0f), F(key, i, "ry", 0f), F(key, i, "rz", 0f), F(key, i, "rw", 1f));
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 0.0001f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
        public float GetSize(string key, int i) { return Mathf.Clamp(F(key, i, "size", 1f), MinSize, MaxSize); }
        public bool GetFlip(string key, int i) { return I(key, i, "flip") > 0; }

        public VRCUrl GetUrl() { return _url; }
        public bool HideOthers() { return _hideOthers; }

        // ───────────── 이미지 (캐시·대기열 — 모든 데칼이 같이 쓴다) ─────────────

        /// 이 URL의 이미지를 받아 달라(이미 있거나 대기 중이면 그대로).
        public void Request(VRCUrl url)
        {
            if (url == null) { return; }
            string s = url.Get();
            if (string.IsNullOrEmpty(s) || Find(s) >= 0) { return; }
            int slot = FreeSlot();
            _cacheUrl[slot] = s;
            _cacheVrcUrl[slot] = url;
            _cacheTex[slot] = null;
            _cacheState[slot] = 1;
        }

        /// 받아 둔 이미지(없거나 아직이면 null).
        public Texture2D GetTexture(VRCUrl url)
        {
            if (url == null) { return null; }
            int i = Find(url.Get());
            return i >= 0 && _cacheState[i] == 3 ? _cacheTex[i] : null;
        }

        /// 받기가 끝났는가(성공·실패) — 실패면 데칼을 끈다.
        public bool IsFailed(VRCUrl url)
        {
            if (url == null) { return true; }
            int i = Find(url.Get());
            return i >= 0 && _cacheState[i] == 4;
        }

        private int Find(string s)
        {
            if (string.IsNullOrEmpty(s)) { return -1; }
            for (int i = 0; i < CACHE; i++) { if (_cacheState[i] != 0 && _cacheUrl[i] == s) { return i; } }
            return -1;
        }

        /// 빈 칸, 없으면 받는 중이 아닌 가장 앞 칸(오래된 것)을 다시 쓴다.
        private int FreeSlot()
        {
            for (int i = 0; i < CACHE; i++) { if (_cacheState[i] == 0) { return i; } }
            for (int i = 0; i < CACHE; i++) { if (_cacheState[i] != 2) { return i; } }
            return 0;
        }

        void Update()
        {
            if (_downloading >= 0 || Time.time < _nextDownload || _downloader == null) { return; }
            for (int i = 0; i < CACHE; i++)
            {
                if (_cacheState[i] != 1) { continue; }
                _cacheState[i] = 2;
                _downloading = i;
                _nextDownload = Time.time + DownloadGap;
                TextureInfo info = new TextureInfo();
                info.GenerateMipMaps = true;
                info.WrapModeU = TextureWrapMode.Clamp;
                info.WrapModeV = TextureWrapMode.Clamp;
                _downloader.DownloadImage(_cacheVrcUrl[i], null, (IUdonEventReceiver)this, info);
                return;
            }
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int i = Find(result.Url.Get());
            if (i >= 0) { _cacheTex[i] = result.Result; _cacheState[i] = 3; }
            _downloading = -1;
            if (_url != null && result.Url.Get() == _url.Get()) { ApplyPreview(); UpdateTexts("IMAGE LOADED - AIM THE DECAL GUN AT THE SAMPLE AIRCRAFT"); }
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            int i = Find(result.Url.Get());
            if (i >= 0) { _cacheState[i] = 4; }
            _downloading = -1;
            if (_url != null && result.Url.Get() == _url.Get())
            {
                UpdateTexts("<color=#FF5544>IMAGE FAILED: " + result.Error + " (ALLOWED HOSTS: IMGUR, GITHUB... OR ENABLE UNTRUSTED URLS)</color>");
            }
        }
    }
}
