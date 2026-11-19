using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// CIC 순항미사일 일제 발사(6-276) — 씬에 하나. CIC 도구 패널 CRUISE MSL이 부른다(ACO_CicConsole.ClickCruise).
    ///  - 일제 발사 = SalvoSize(4)발: 아군 순양함 VLS(LaunchPoints)에서 LaunchInterval(0.4초) 간격으로 솟아, 1발은 커서 자리, 나머지는 커서 둘레 SpreadRadius(80m)로.
    ///  - 칸 SlotCount(2) — 비행이 쿨타임보다 길어 두 번의 일제 발사가 함께 날 수 있다. 쿨타임 Cooldown(40초)은 모두가 함께(발사 순간부터).
    ///  - 순항 높이: 쏜 사람이 발사 자리 → 표적 항로의 가장 높은 지형 + CruiseClearance(150m)를 재서 보낸다(모든 화면이 같은 값).
    ///  - 피해: 미사일마다 명중 자리에서 SplashDamage(600) · SplashRadius(100m), 가장자리 SplashMinFraction(25%) — 벙커버스터(ACO_SupportManager)와 같은 규칙,
    ///    미사일 오너(쏜 사람 — 나가면 마스터) 화면만 준다. 격추 점수는 판정한 사람.
    ///  - 모항 MinHomeDistance(3km) 안은 쏘지 않는다. 작전이 끝나거나 바뀌면 날던 미사일은 터지지 않고 사라진다.
    /// 동기화(Manual, 쏜 사람이 오너): 칸마다 작전 · 발사 시각 · 커서 자리 · 순항 높이, 마지막 발사(쿨타임). 미사일 자리는 서버 시각 함수.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_CicStrike : UdonSharpBehaviour
    {
        [Header("연결 (Bake)")]
        public ACO_MissionManager Mission;
        [Tooltip("미사일 풀(SlotCount × SalvoSize — 프리팹 빌더)")]
        public ACO_CicCruiseMissile[] Missiles;
        [Tooltip("아군 순양함 VLS 발사 자리(Bake — 없으면 항모 자리)")]
        public Transform[] LaunchPoints;
        public Transform HomeBase;
        [Tooltip("지면(미션 생성기 GroundLayers) · 해수면 · 피해 레이어 · 물 레이어(Bake — 지원 관리자 값)")]
        public LayerMask GroundLayers = 2049;
        public float SeaLevel = 0f;
        public LayerMask DamageLayers;
        public LayerMask WaterLayers = 16;

        [Header("연출 (프리팹 빌더 — 대함미사일 LASM 연출 복사, 켤 때 재생 · 차례로 돌려 쓴다)")]
        public GameObject[] FxBlasts;
        public GameObject[] FxWaters;
        [Tooltip("격추 파편(ACO_WreckFx_Missile — 로컬 +Z로 쏜다)")]
        public ParticleSystem[] FxWrecks;
        [Tooltip("VLS 발사음")]
        public AudioSource LaunchSound;

        [Header("값")]
        public int SlotCount = 2;
        public int SalvoSize = 4;
        public float LaunchInterval = 0.4f;
        public float SpreadRadius = 80f;
        public float Cooldown = 40f;
        public float MinHomeDistance = 3000f;
        public float CruiseClearance = 150f;
        [Tooltip("항로 지형을 재는 간격(m)")]
        public float RouteSampleStep = 200f;
        public float SplashDamage = 450f;   // 2026-11-18 600 → 450(6-284 — "생각보다 세다")
        public float SplashRadius = 100f;
        [Range(0f, 1f)] public float SplashMinFraction = 0.25f;
        [Tooltip("결과(명중 · 격추)를 패널에 보여 주는 시간(초, 마지막 미사일이 끝난 뒤)")]
        public float ResultTime = 15f;

        [UdonSynced] private int[] _sMission = new int[2];
        [UdonSynced] private int[] _sMs = new int[2];
        [UdonSynced] private Vector3[] _sCenter = new Vector3[2];
        [UdonSynced] private float[] _sCruiseY = new float[2];
        [UdonSynced] private int _lastMs;
        [UdonSynced] private int _lastMission;
        [UdonSynced] private int _lastSlot = -1;

        private int _blastNext;
        private int _waterNext;
        private int _wreckNext;
        private Collider[] _hits = new Collider[256];
        private SaccTarget[] _damaged = new SaccTarget[64];

        void Start()
        {
            if (Missiles != null)
            {
                for (int i = 0; i < Missiles.Length; i++) { if (Missiles[i]) { Missiles[i].Strike = this; } }
            }
        }

        // ───────────── 상태 (모든 화면) ─────────────

        private int Now() { return Networking.GetServerTimeInMilliseconds(); }

        private int CurrentMission()
        {
            if (Mission == null || Mission.GetState() != ACO_MissionManager.STATE_RUNNING) { return 0; }
            int m = Mission.GetMissionStartMs();
            return m == 0 ? 1 : m;
        }

        /// 칸 s가 이번 작전에서 날고 있는가(발사 뒤 모든 미사일이 끝나기 전).
        private bool SlotLive(int s)
        {
            int m = CurrentMission();
            if (m == 0 || s < 0 || s >= _sMission.Length || _sMission[s] != m || _sMs[s] == 0) { return false; }
            return !SlotDone(s);
        }

        private bool SlotDone(int s)
        {
            int n = SalvoSize;
            int seen = 0;
            for (int k = 0; k < n; k++)
            {
                ACO_CicCruiseMissile mm = MissileAt(s, k);
                if (mm == null) { continue; }
                if (!mm.IsArmed() || mm.GetKey() != _sMs[s]) { return false; }
                if (mm.IsImpacted() || mm.IsShotDown()) { seen++; }
            }
            return seen >= n;
        }

        private ACO_CicCruiseMissile MissileAt(int s, int k)
        {
            int i = s * SalvoSize + k;
            return Missiles != null && i >= 0 && i < Missiles.Length ? Missiles[i] : null;
        }

        /// 쿨타임 남은 초(모두가 함께). 작전이 바뀌었으면 0.
        public float GetCooldown()
        {
            int m = CurrentMission();
            if (m == 0 || _lastMission != m) { return 0f; }
            return Mathf.Max(Cooldown - (Now() - _lastMs) * 0.001f, 0f);
        }

        /// 마지막 일제 발사가 날고 있으면 첫 명중까지 남은 초(지나면 마지막 명중까지), 아니면 -1.
        public float GetFlightLeft()
        {
            int s = _lastSlot;
            if (!SlotLive(s)) { return -1f; }
            int now = Now();
            // 서버 시각은 음수 구간도 있고 넘어가며 돈다 — 첫 값에서 시작해 빼기로 비교한다(디버깅 2026-11-18)
            bool any = false;
            int first = 0;
            int last = 0;
            for (int k = 0; k < SalvoSize; k++)
            {
                ACO_CicCruiseMissile mm = MissileAt(s, k);
                if (mm == null || !mm.IsArmed()) { continue; }
                int a = mm.GetArrivalMs();
                if (!any) { first = a; last = a; any = true; continue; }
                if (a - first < 0) { first = a; }
                if (a - last > 0) { last = a; }
            }
            if (!any) { return -1f; }
            int target = now - first < 0 ? first : last;
            return Mathf.Max((target - now) * 0.001f, 0f);
        }

        [Header("무전 (6-282 — 잡담 급, 글로벌)")]
        [Tooltip("무전 채터(Bake가 넣는다)")]
        public ACO_Chatter Chatter;
        public string LaunchLineWho = "CREW";
        public Color LaunchLineColor = new Color(0.6f, 0.88f, 1f, 1f);
        [Tooltip("일제 발사가 새로 동기화되면 모두에게 — 아군 함정 승조원 목소리")]
        [TextArea] public string LaunchLineText = "Aye aye, sir! Cruise missiles away!";
        public AudioClip LaunchLineClip;
        public float LineMaxDelay = 4f;
        [Tooltip("발사된 지 이 시간(ms) 안의 일제 발사만 말한다(늦게 들어온 사람은 지난 발사를 듣지 않는다)")]
        public int LineFreshMs = 5000;
        private int[] _saidMs;

        /// 범위 피해 공격자: SaccTarget이 피해를 준 오브젝트의 UdonBehaviour에서 이 이름(EntityControl)을 읽는다(ACO_SupportManager와 같은 칸).
        /// CIC 순항미사일은 기체가 쏜 게 아니라 비워 둔다(공격자 없음 — 점수는 LastHitByPlayer). 없으면 피해마다 "Could not find symbol" 오류 로그(재점검 2026-11-18).
        [System.NonSerialized] public SaccEntity EntityControl;

        private int _resHits;
        private int _resDowns;
        public int GetResultHits() { return _resHits; }
        public int GetResultDowns() { return _resDowns; }
        // 이 화면에서 결과를 처음 본 일제 발사(칸 · 키)와 그 시각(로컬 Time.time, -1 = 아직)
        private int _seenSlot = -1;
        private int _seenKey;
        private float _seenAt = -1f;

        /// 마지막 일제 발사의 결과(명중 · 격추 수 — GetResultHits/Downs). 둘 중 먼저 끝나는 쪽까지만 true(로컬 표시):
        ///  - 가장 늦은 도착 예정 + ResultTime초(끝나고 한참 뒤 테이블에 온 사람에겐 안 뜬다)
        ///  - 이 화면에서 결과를 처음 본 뒤 ResultTime초(일찍 다 격추돼도 15초 뒤 꺼진다 — 2026-11-18 사용자 "로컬에서 확인된 이후 15초")
        public bool HasResult()
        {
            int hits = 0;
            int downs = 0;
            _resHits = 0;
            _resDowns = 0;
            int s = _lastSlot;
            int m = CurrentMission();
            if (m == 0 || s < 0 || s >= _sMission.Length || _sMission[s] != m || _sMs[s] == 0) { return false; }
            int lastArrival = 0;
            for (int k = 0; k < SalvoSize; k++)
            {
                ACO_CicCruiseMissile mm = MissileAt(s, k);
                if (mm == null || !mm.IsArmed() || mm.GetKey() != _sMs[s]) { return false; }
                if (mm.IsImpacted()) { hits++; }
                else if (mm.IsShotDown()) { downs++; }
                else { return false; }
                int a = mm.GetArrivalMs();
                if (k == 0 || a - lastArrival > 0) { lastArrival = a; }   // 빼기로 비교(서버 시각 음수 · 넘어감)
            }
            _resHits = hits;
            _resDowns = downs;
            if (Now() - lastArrival >= Mathf.RoundToInt(ResultTime * 1000f)) { return false; }
            if (_seenSlot != s || _seenKey != _sMs[s] || _seenAt < 0f)
            {
                _seenSlot = s;
                _seenKey = _sMs[s];
                _seenAt = Time.time;
            }
            return Time.time - _seenAt < ResultTime;
        }

        public int GetMissileCount() { return Missiles != null ? Missiles.Length : 0; }

        /// 커서까지 대략 비행 시간(초) — 확정 팝업 안내용(발사 간격 · 상승 · 급강하는 대충 더한다).
        public float EstimateFlight(Vector3 center)
        {
            Vector3 from = LaunchPoint(0);
            float d = new Vector2(center.x - from.x, center.z - from.z).magnitude;
            float v = Missiles != null && Missiles.Length > 0 && Missiles[0] != null ? Mathf.Max(Missiles[0].CruiseSpeed, 1f) : 300f;
            return d / v + 4f;
        }
        public bool IsMissileFlying(int i) { return Missiles != null && i >= 0 && i < Missiles.Length && Missiles[i] != null && Missiles[i].IsFlying(); }
        public Vector3 GetMissilePosition(int i) { return Missiles[i].transform.position; }

        // ───────────── 발사 (쏜 사람 화면) ─────────────

        /// 발사할 수 있으면 0, 아니면 이유: 1 작전 아님 · 2 쿨타임 · 3 모항에 너무 가까움 · 4 빈 칸 없음.
        public int CanLaunch(Vector3 center)
        {
            if (CurrentMission() == 0) { return 1; }
            if (GetCooldown() > 0f) { return 2; }
            Vector3 home = HomeBase != null ? HomeBase.position : Vector3.zero;
            if (new Vector2(center.x - home.x, center.z - home.z).sqrMagnitude < MinHomeDistance * MinHomeDistance) { return 3; }
            if (FreeSlot() < 0) { return 4; }
            return 0;
        }

        private int FreeSlot()
        {
            for (int s = 0; s < SlotCount && s < _sMission.Length; s++) { if (!SlotLive(s)) { return s; } }
            return -1;
        }

        /// CIC 콘솔이 부른다. 쏘면 true.
        public bool Launch(Vector3 center)
        {
            if (CanLaunch(center) != 0) { return false; }
            int s = FreeSlot();
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Networking.IsOwner(gameObject)) { Networking.SetOwner(me, gameObject); }
            for (int k = 0; k < SalvoSize; k++)
            {
                ACO_CicCruiseMissile mm = MissileAt(s, k);
                if (mm == null) { continue; }
                if (!Networking.IsOwner(mm.gameObject)) { Networking.SetOwner(me, mm.gameObject); }
                mm.Revive();
            }
            int now = Now();
            _sMission[s] = CurrentMission();
            _sMs[s] = now + 300;   // 체력을 채울 틈(2프레임) + 전송 지연 — 모든 화면이 같은 시각에 쏜다
            _sCenter[s] = center;
            _sCruiseY[s] = RouteTop(LaunchPoint(0), center) + CruiseClearance;
            _lastMs = now;
            _lastMission = _sMission[s];
            _lastSlot = s;
            RequestSerialization();
            return true;
        }

        /// 발사 자리 → 표적 사이 지형의 가장 높은 곳(바다면 해수면).
        private float RouteTop(Vector3 from, Vector3 to)
        {
            Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float len = flat.magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(RouteSampleStep, 10f)), 1, 400);
            float top = SeaLevel;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = from + flat * (i / (float)n);
                top = Mathf.Max(top, GroundHeight(p));
            }
            return top;
        }

        private float GroundHeight(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, 6000f, p.z), Vector3.down, out hit, 12000f, GroundLayers.value, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(hit.point.y, SeaLevel);
            }
            return SeaLevel;
        }

        private Vector3 LaunchPoint(int k)
        {
            if (LaunchPoints != null && LaunchPoints.Length > 0)
            {
                Transform t = LaunchPoints[k % LaunchPoints.Length];
                if (t != null) { return t.position; }
            }
            Vector3 h = HomeBase != null ? HomeBase.position : Vector3.zero;
            return new Vector3(h.x, SeaLevel + 20f, h.z);
        }

        /// 칸 s의 k번째 미사일 명중 자리: 0번은 커서, 나머지는 커서 둘레 SpreadRadius에 고르게(날아오는 방향 기준으로 돌려 놓는다).
        private Vector3 ImpactPoint(int s, int k)
        {
            Vector3 c = _sCenter[s];
            if (k > 0)
            {
                Vector3 from = LaunchPoint(0);
                Vector3 toward = new Vector3(c.x - from.x, 0f, c.z - from.z);
                float yaw = toward.sqrMagnitude > 1f ? Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg : 0f;
                int ring = Mathf.Max(SalvoSize - 1, 1);
                float a = yaw + 360f * (k - 1) / ring;
                c = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * SpreadRadius;
            }
            c.y = GroundHeight(c);
            return c;
        }

        public override void OnDeserialization() { }

        // ───────────── 매 프레임 (모든 화면) ─────────────

        void Update()
        {
            if (Missiles == null) { return; }
            int m = CurrentMission();
            if (_saidMs == null || _saidMs.Length != _sMs.Length) { _saidMs = new int[_sMs.Length]; }
            for (int s = 0; s < SlotCount && s < _sMission.Length; s++)
            {
                bool live = m != 0 && _sMission[s] == m && _sMs[s] != 0;
                if (live && _saidMs[s] != _sMs[s])
                {
                    // 발사 무전(글로벌, 6-282): 새로 받은 일제 발사를 한 번 — 지난 발사(늦게 들어옴)는 넘긴다
                    _saidMs[s] = _sMs[s];
                    int age = Now() - _sMs[s];
                    if (age >= -2000 && age <= LineFreshMs && Chatter) { Chatter.SayLow(LaunchLineWho, LaunchLineColor, LaunchLineText, LaunchLineClip, LineMaxDelay, ACO_Chatter.CH_ALLY); }
                }
                for (int k = 0; k < SalvoSize; k++)
                {
                    ACO_CicCruiseMissile mm = MissileAt(s, k);
                    if (mm == null) { continue; }
                    if (!live) { mm.Disarm(); continue; }
                    if (mm.IsArmed() && mm.GetKey() == _sMs[s]) { continue; }
                    mm.Arm(_sMs[s], _sMs[s] + Mathf.RoundToInt(k * LaunchInterval * 1000f), LaunchPoint(k), ImpactPoint(s, k), _sCruiseY[s]);
                }
            }
        }

        // ───────────── 연출 · 피해 (미사일이 부른다) ─────────────

        /// VLS 발사(모든 화면): 갑판 위 작은 폭발(불꽃 · 연기) + 발사음. 연기 기둥은 미사일 궤적이 만든다.
        public void PlayLaunch(Vector3 at)
        {
            PlayBlast(at);
            if (LaunchSound && LaunchSound.clip)
            {
                LaunchSound.transform.position = at;
                LaunchSound.PlayOneShot(LaunchSound.clip);   // 0.4초 간격 연속 발사 — Play는 앞 소리를 끊는다(6-290 VLS 발사음은 길다)
            }
        }

        /// 공중 격추(모든 화면): 폭발 + 날던 방향으로 파편. 지상 피해 없음.
        public void PlayShootDown(Vector3 at, Vector3 forward)
        {
            PlayBlast(at);
            if (FxWrecks == null || FxWrecks.Length == 0) { return; }
            ParticleSystem ps = FxWrecks[_wreckNext % FxWrecks.Length];
            _wreckNext++;
            if (!ps) { return; }
            ps.transform.SetPositionAndRotation(at, forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward) : Quaternion.identity);
            ps.Play(true);
        }

        /// 명중(모든 화면): 폭발 둘(땅 · 조금 위) — 물이면 물기둥, 미사일 오너 화면만 범위 피해.
        public void PlayImpact(ACO_CicCruiseMissile missile, Vector3 point, bool judge)
        {
            bool water = Physics.Raycast(point + Vector3.up * 20f, Vector3.down, 40f, WaterLayers, QueryTriggerInteraction.Collide);
            if (water && FxWaters != null && FxWaters.Length > 0)
            {
                Replay(FxWaters[_waterNext % FxWaters.Length], new Vector3(point.x, SeaLevel, point.z));
                _waterNext++;
            }
            PlayBlast(point);
            PlayBlast(point + Vector3.up * 10f);
            if (judge) { ApplySplash(point); }
        }

        private void PlayBlast(Vector3 point)
        {
            if (FxBlasts == null || FxBlasts.Length == 0) { return; }
            Replay(FxBlasts[_blastNext % FxBlasts.Length], point);
            _blastNext++;
        }

        private void Replay(GameObject g, Vector3 point)
        {
            if (!g) { return; }
            g.transform.SetPositionAndRotation(point, Quaternion.identity);
            if (g.activeSelf) { g.SetActive(false); }
            g.SetActive(true);   // 파티클 · 폭발음이 켜질 때 재생된다(기존 미사일과 같다)
        }

        /// 범위 피해 — ACO_SupportManager.ApplySplash(벙커버스터)와 같은 규칙: 유닛마다 가장 가까운 콜라이더 한 번, 아군 호위 대상 · 편대 제외, 함선 부위 배율, 터널 너머 막힘.
        private void ApplySplash(Vector3 center)
        {
            if (SplashRadius <= 0f) { return; }
            int n = Physics.OverlapSphereNonAlloc(center, SplashRadius, _hits, DamageLayers);
            int damagedCount = 0;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i];
                if (!c) { continue; }
                SaccTarget target = c.GetComponentInParent<SaccTarget>();
                if (!target || target.Health <= 0f) { continue; }
                if (target.GetComponent<ACO_CicCruiseMissile>() != null) { continue; }
                ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>();
                if (escort != null && !escort.Hostile) { continue; }
                if (c.GetComponentInParent<ACO_Wingman>() != null) { continue; }
                bool done = false;
                for (int k = 0; k < damagedCount; k++) { if (_damaged[k] == target) { done = true; break; } }
                if (done) { continue; }
                if (ACO_TerrainFeatures.SplashBlockedByTunnel(center, c.bounds.center)) { continue; }
                float best = float.MaxValue;
                for (int j = i; j < n; j++)
                {
                    Collider cj = _hits[j];
                    if (!cj || cj.GetComponentInParent<SaccTarget>() != target) { continue; }
                    float d = Vector3.Distance(center, cj.bounds.ClosestPoint(center));
                    if (d < best) { best = d; }
                }
                float dmg = SplashDamage * Mathf.Lerp(1f, SplashMinFraction, Mathf.Clamp01(best / SplashRadius));
                ACO_Warship ship = target.GetComponentInParent<ACO_Warship>();
                if (ship != null)
                {
                    if (ship.IsSplashImmune(target)) { continue; }
                    dmg *= ship.GetSplashScale(target);
                }
                target.LastHitByPlayer = Networking.LocalPlayer;
                target.LastHitWeaponType = WeaponType;
                target.WeaponDamageTarget(dmg, gameObject, WeaponType);
                if (damagedCount < _damaged.Length) { _damaged[damagedCount] = target; damagedCount++; }
            }
        }

        [Tooltip("피해 무기 번호(SAV — 작전판 · 점수가 플레이어 무기로 센다)")]
        public byte WeaponType = 4;
    }
}
