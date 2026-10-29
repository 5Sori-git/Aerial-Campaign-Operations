using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 레이더 경보 수신기(RWR, 로컬 전용 — 동기화 없음). 내가 탄 기체를 노린 위협을 크로스헤어 둘레 링에 그린다.
    /// - 미사일: 오는 방향에 쉐브론(꼭짓점이 가운데를 향한다). 멀면 바깥 반경, 가까울수록 크로스헤어 쪽으로 다가온다(로그 눈금).
    ///   가까울수록 빨갛고 조금 커지며, BlinkRange 안이면 경보음 박자에 맞춰 깜빡인다. 가까운 순으로 MaxShown발까지.
    /// - 락온: 락온 중인 적 방향 바깥 반경에 얇은 호.
    /// - 경보음: 가장 가까운 미사일 방향(머리에서 SoundDistance)에서 울린다. 간격은 멀면 MaxBeepInterval, 가까우면 MinBeepInterval.
    /// 방향은 머리(시선) 기준 수평 방위다: 위 = 보는 쪽, 아래 = 뒤, 좌우 = 좌우.
    /// 미사일은 모든 클라이언트가 로컬로 재현하므로 위치를 그대로 읽는다(네트워크 추가 없음). ACO_Missile이 경고를 켜고 끌 때 등록/해제하고,
    /// ACO_MissileLauncher의 락온 경보가 ReportLock을 부른다. 위협이 없으면 매 프레임 바로 돌아간다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_RWR : UdonSharpBehaviour
    {
        public const int MAX_MISSILES = 8;
        public const int MAX_LOCKS = 6;

        [Header("참조 (프리팹 빌더/Bake가 만든다)")]
        [Tooltip("그릴 거리(CrosshairDistance)와 크로스헤어 크기를 읽는다(링이 크로스헤어와 겹치지 않게)")]
        public ACO_TargetMarkers Markers;
        [Tooltip("링 루트. 매 프레임 시선 정면 CrosshairDistance에 놓는다(쉐브론·호의 부모)")]
        public Transform Ring;
        [Tooltip("미사일 쉐브론(꼭짓점이 +Y, 크기 1). 개수 = 최대 표시 수")]
        public Transform[] Chevrons;
        public MeshRenderer[] ChevronRenderers;
        [Tooltip("락온 호(반지름 1, +Y 쪽 가운데)")]
        public Transform[] LockArcs;
        [Tooltip("미사일 경보음(3D). 위치는 스크립트가 옮긴다")]
        public AudioSource WarnSource;
        [Tooltip("락온 경보음(3D, 새 락온마다 한 번). 비우면 소리 없음 — SAV 기체 자체 락온음은 그대로 난다")]
        public AudioSource LockSource;

        [Header("표시 (HUD 설정 패널이 바꾼다)")]
        [Tooltip("끄면 링과 경보음을 모두 끈다")]
        public bool ShowRwr = true;
        [Tooltip("링 전체 크기 배율. 안쪽 반경은 크로스헤어 바깥(반지름 + 0.4°)보다 작아지지 않는다")]
        public float SizeScale = 1f;

        [Header("링")]
        [Tooltip("한 번에 그리는 미사일 수(가까운 순)")]
        [Range(1, 8)] public int MaxShown = 4;
        [Tooltip("이 거리(m) 이상이면 바깥 반경")]
        public float MaxRange = 10000f;
        [Tooltip("이 거리(m) 이하면 안쪽 반경(크로스헤어 바로 바깥)")]
        public float CloseRange = 300f;
        [Tooltip("바깥 반경(시야각, 도)")]
        public float OuterAngle = 9f;
        [Tooltip("안쪽 반경(시야각, 도). 크로스헤어(CrosshairAngle의 절반)보다 크게")]
        public float InnerAngle = 1.6f;
        [Tooltip("쉐브론 크기(시야각, 도)")]
        public float ChevronAngle = 0.9f;
        [Tooltip("가장 가까울 때 쉐브론 크기 배율")]
        public float CloseScale = 1.5f;
        [Tooltip("락온 호 반경(시야각, 도). 바깥 반경보다 조금 크게")]
        public float LockArcAngle = 10.5f;
        [Tooltip("락온 경보가 끊긴 뒤 호를 남겨 두는 시간(초). 런처 경보 간격(1초)보다 길게")]
        public float LockHoldTime = 1.6f;

        [Header("색 / 깜빡임")]
        [Tooltip("끄면 쉐브론이 늘 FarColor")]
        public bool UseColor = true;
        public Color FarColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        public Color CloseColor = new Color(1f, 0.15f, 0.1f, 1f);
        [Tooltip("이 거리(m) 안의 미사일은 경보음 박자에 맞춰 깜빡인다")]
        public float BlinkRange = 1500f;

        [Header("경보음")]
        [Tooltip("멀 때(MaxRange) 경보음 간격(초)")]
        public float MaxBeepInterval = 1f;
        [Tooltip("가까울 때(CloseRange) 경보음 간격(초)")]
        public float MinBeepInterval = 0.1f;
        [Tooltip("경보음을 머리에서 이만큼(m) 떨어진 위협 방향에 둔다")]
        public float SoundDistance = 1f;

        private Transform[] _missiles = new Transform[MAX_MISSILES];
        private SaccEntity[] _missileVictims = new SaccEntity[MAX_MISSILES];
        private float[] _dist = new float[MAX_MISSILES];
        private bool[] _picked = new bool[MAX_MISSILES];
        private int _missileCount;

        private Transform[] _lockSources = new Transform[MAX_LOCKS];
        private SaccEntity[] _lockVictims = new SaccEntity[MAX_LOCKS];
        private float[] _lockUntil = new float[MAX_LOCKS];

        private Material[] _chevronMats;
        private float _nextBeep;
        private float _blinkOnUntil;
        private bool _visible;
        private float _previewUntil;
        private bool _started;
        private float _innerDeg;   // 이번 프레임 반경(도): 크기 배율 + 크로스헤어 여유
        private float _outerDeg;
        private float _arcDeg;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            int n = ChevronRenderers != null ? ChevronRenderers.Length : 0;
            _chevronMats = new Material[n];
            for (int i = 0; i < n; i++) { if (ChevronRenderers[i]) { _chevronMats[i] = ChevronRenderers[i].material; } }
            HideAll();
        }

        // ───────────── 위협 등록 (ACO_Missile / ACO_MissileLauncher) ─────────────

        /// 나를 노린 미사일이 경고를 켰다(그 표적 기체에 이 클라이언트가 타고 있다).
        public void AddMissile(Transform missile, SaccEntity victim)
        {
            if (!missile) { return; }
            for (int i = 0; i < _missileCount; i++) { if (_missiles[i] == missile) { return; } }
            if (_missileCount >= MAX_MISSILES) { return; }
            _missiles[_missileCount] = missile;
            _missileVictims[_missileCount] = victim;
            _missileCount++;
        }

        public void RemoveMissile(Transform missile)
        {
            for (int i = 0; i < _missileCount; i++)
            {
                if (_missiles[i] != missile) { continue; }
                RemoveMissileAt(i);
                return;
            }
        }

        /// 락온 경보를 받았다(런처가 경보 간격마다 보낸다). 새 락온이면 그 방향에서 락온음을 한 번 낸다.
        public void ReportLock(Transform source, SaccEntity victim)
        {
            if (!source) { return; }
            float now = Time.time;
            int slot = -1;
            for (int i = 0; i < MAX_LOCKS; i++) { if (_lockSources[i] == source) { slot = i; break; } }
            bool fresh = slot < 0 || _lockUntil[slot] < now;
            if (slot < 0)
            {
                for (int i = 0; i < MAX_LOCKS; i++) { if (_lockSources[i] == null || _lockUntil[i] < now) { slot = i; break; } }
                if (slot < 0) { return; }
            }
            _lockSources[slot] = source;
            _lockVictims[slot] = victim;
            _lockUntil[slot] = now + LockHoldTime;
            if (fresh && ShowRwr && LockSource != null)
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (local != null)
                {
                    Vector3 headPos = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    LockSource.transform.position = headPos + (source.position - headPos).normalized * SoundDistance;
                    LockSource.Play();
                }
            }
        }

        /// HUD 설정 패널이 부른다: seconds 동안 예시 위협(먼 미사일, 가까운 미사일, 락온)을 보여 준다.
        public void Preview(float seconds) { _previewUntil = Time.time + seconds; }

        // ───────────── 그리기 ─────────────

        public override void PostLateUpdate()
        {
            float now = Time.time;
            bool anyLock = false;
            for (int i = 0; i < MAX_LOCKS; i++) { if (_lockSources[i] != null && _lockUntil[i] >= now) { anyLock = true; break; } }
            bool preview = now < _previewUntil;
            if (_missileCount == 0 && !anyLock && !preview)
            {
                if (_visible) { HideAll(); }
                return;
            }
            Start();
            PruneMissiles();

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !ShowRwr || Ring == null) { if (_visible) { HideAll(); } return; }
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 headPos = head.position;
            Quaternion headRot = head.rotation;
            Quaternion inv = Quaternion.Inverse(headRot);
            float d0 = Markers != null ? Mathf.Max(Markers.CrosshairDistance, 0.1f) : 30f;
            // 반경: 크기 배율을 곱하되 안쪽은 크로스헤어 바깥, 바깥·호는 안쪽보다 넉넉히(크로스헤어를 키워도 쉐브론이 겹치지 않게).
            _innerDeg = InnerAngle * SizeScale;
            if (Markers != null && Markers.ShowCrosshair) { _innerDeg = Mathf.Max(_innerDeg, Markers.CrosshairAngle * 0.5f + 0.4f); }
            _outerDeg = Mathf.Max(OuterAngle * SizeScale, _innerDeg + 2f);
            _arcDeg = Mathf.Max(LockArcAngle * SizeScale, _outerDeg + 0.8f);

            if (!Ring.gameObject.activeSelf) { Ring.gameObject.SetActive(true); }
            Ring.SetPositionAndRotation(headPos + headRot * Vector3.forward * d0, headRot);
            _visible = true;

            // 경보음 박자: 가장 가까운 미사일 거리로 간격을 정하고, 울릴 때마다 깜빡임 켜짐 구간을 연다.
            int nearest = -1;
            for (int i = 0; i < _missileCount; i++)
            {
                _dist[i] = Vector3.Distance(_missiles[i].position, headPos);
                _picked[i] = false;
                if (nearest < 0 || _dist[i] < _dist[nearest]) { nearest = i; }
            }
            if (nearest >= 0)
            {
                float tn = RangeT(_dist[nearest]);
                float interval = Mathf.Lerp(MinBeepInterval, MaxBeepInterval, tn);
                if (WarnSource) { WarnSource.transform.position = headPos + (_missiles[nearest].position - headPos).normalized * SoundDistance; }
                if (now >= _nextBeep)
                {
                    _nextBeep = now + interval;
                    _blinkOnUntil = now + interval * 0.5f;
                    if (WarnSource) { WarnSource.Play(); }
                }
                // 더 가까워져 간격이 줄었으면 다음 박자를 당긴다(멀 때 1초 간격이 근접 후에도 남지 않게).
                if (_nextBeep - now > interval) { _nextBeep = now + interval; }
            }
            bool blinkOn = now < _blinkOnUntil;

            // 쉐브론: 가까운 순으로 채운다.
            int slots = Chevrons != null ? Mathf.Min(Chevrons.Length, MaxShown) : 0;
            int used = 0;
            for (int s = 0; s < slots; s++)
            {
                int best = -1;
                for (int i = 0; i < _missileCount; i++) { if (!_picked[i] && (best < 0 || _dist[i] < _dist[best])) { best = i; } }
                if (best < 0) { break; }
                _picked[best] = true;
                float b = Bearing(inv * (_missiles[best].position - headPos));
                float t = RangeT(_dist[best]);
                PlaceChevron(s, b, t, _dist[best] < BlinkRange && !blinkOn, d0);
                used++;
            }
            int arcsUsed = 0;
            int arcSlots = LockArcs != null ? LockArcs.Length : 0;
            for (int i = 0; i < MAX_LOCKS && arcsUsed < arcSlots; i++)
            {
                Transform src = _lockSources[i];
                if (src == null || _lockUntil[i] < now) { continue; }
                SaccEntity v = _lockVictims[i];
                if (v != null && !v.InVehicle) { _lockUntil[i] = 0f; continue; }
                PlaceArc(arcsUsed, Bearing(inv * (src.position - headPos)), d0);
                arcsUsed++;
            }

            // 미리보기(위협이 없을 때만): 먼 미사일 1, 가까운 미사일 1(깜빡임), 락온 1.
            if (preview && used == 0 && arcsUsed == 0)
            {
                bool demoBlink = Mathf.Repeat(now, 0.4f) < 0.2f;
                if (slots > 0) { PlaceChevron(0, 40f * Mathf.Deg2Rad, 0.85f, false, d0); used = 1; }
                if (slots > 1) { PlaceChevron(1, 200f * Mathf.Deg2Rad, 0.1f, !demoBlink, d0); used = 2; }
                if (arcSlots > 0) { PlaceArc(0, 300f * Mathf.Deg2Rad, d0); arcsUsed = 1; }
            }

            for (int s = used; Chevrons != null && s < Chevrons.Length; s++) { SetActive(Chevrons[s], false); }
            for (int a = arcsUsed; a < arcSlots; a++) { SetActive(LockArcs[a], false); }
        }

        /// 머리 기준 방향 → 링 위 각도(라디안, 위 = 0, 시계 방향). 수평 방위만 본다(뒤 = 아래).
        private float Bearing(Vector3 local)
        {
            return Mathf.Atan2(local.x, local.z);
        }

        /// 거리 → 0(가까움, CloseRange 이하) ~ 1(멂, MaxRange 이상), 로그 눈금 — 가까울수록 크게 움직인다.
        private float RangeT(float d)
        {
            if (d <= CloseRange || CloseRange <= 0f || MaxRange <= CloseRange) { return d <= CloseRange ? 0f : 1f; }
            return Mathf.Clamp01(Mathf.Log(d / CloseRange) / Mathf.Log(MaxRange / CloseRange));
        }

        private void PlaceChevron(int slot, float bearing, float t, bool hidden, float d0)
        {
            Transform c = Chevrons[slot];
            if (!c) { return; }
            SetActive(c, true);
            float r = d0 * Mathf.Tan(Mathf.Lerp(_innerDeg, _outerDeg, t) * Mathf.Deg2Rad);
            c.localPosition = new Vector3(Mathf.Sin(bearing) * r, Mathf.Cos(bearing) * r, 0f);
            // 꼭짓점(+Y)이 가운데를 향하게: +Y를 (−sin b, −cos b)로 돌린다.
            c.localRotation = Quaternion.Euler(0f, 0f, 180f - bearing * Mathf.Rad2Deg);
            float size = d0 * Mathf.Tan(ChevronAngle * SizeScale * Mathf.Deg2Rad) * Mathf.Lerp(CloseScale, 1f, t);
            c.localScale = new Vector3(size, size, size);
            if (ChevronRenderers != null && slot < ChevronRenderers.Length && ChevronRenderers[slot] != null)
            {
                ChevronRenderers[slot].enabled = !hidden;
                if (_chevronMats != null && slot < _chevronMats.Length && _chevronMats[slot] != null)
                {
                    _chevronMats[slot].SetColor("_Color", UseColor ? Color.Lerp(CloseColor, FarColor, t) : FarColor);
                }
            }
        }

        private void PlaceArc(int slot, float bearing, float d0)
        {
            Transform a = LockArcs[slot];
            if (!a) { return; }
            SetActive(a, true);
            float r = d0 * Mathf.Tan(_arcDeg * Mathf.Deg2Rad);
            a.localPosition = Vector3.zero;
            // 호의 가운데(+Y)를 (sin b, cos b)로 돌린다.
            a.localRotation = Quaternion.Euler(0f, 0f, -bearing * Mathf.Rad2Deg);
            a.localScale = new Vector3(r, r, r);
        }

        /// 터졌거나 회수됐거나, 내가 그 기체에서 내렸으면 목록에서 뺀다(경고 해제를 놓친 경우의 안전장치).
        private void PruneMissiles()
        {
            for (int i = _missileCount - 1; i >= 0; i--)
            {
                Transform m = _missiles[i];
                SaccEntity v = _missileVictims[i];
                if (m == null || !m.gameObject.activeInHierarchy || v == null || !v.InVehicle) { RemoveMissileAt(i); }
            }
        }

        private void RemoveMissileAt(int i)
        {
            int last = _missileCount - 1;
            _missiles[i] = _missiles[last];
            _missileVictims[i] = _missileVictims[last];
            _missiles[last] = null;
            _missileVictims[last] = null;
            _missileCount = last;
        }

        private void HideAll()
        {
            _visible = false;
            if (Ring != null && Ring.gameObject.activeSelf) { Ring.gameObject.SetActive(false); }
        }

        private void SetActive(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) { t.gameObject.SetActive(on); }
        }
    }
}
