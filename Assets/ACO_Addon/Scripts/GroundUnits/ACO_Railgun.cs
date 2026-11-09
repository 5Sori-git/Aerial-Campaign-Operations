using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 랜스 포대(지상 보스) 표현. 체력·부위·스폰은 같은 오브젝트의 ACO_Warship(ExternalPose)이 맡고, 이 스크립트는 단계·주기를 화면에 그린다.
    /// 단계·주기는 ACO_PhaseRailgun이 동기화한다 — 여기선 읽기만 하므로 포신·장갑·발사가 모든 클라이언트에서 같은 시각에 일어난다.
    ///
    /// 모델 교체: 로직은 아래 Transform·배열만 쓴다. 모델은 그 아래의 Visual_* 자식이라 메시를 지우고 다른 모델을 넣어도 동작이 그대로다.
    ///  - BarrelPivot(포신 회전축 — 로컬 X로 들린다) / Muzzle(포구 — forward가 사격 방향, BarrelPivot 자식)
    ///  - ArmorPanels(장갑판 회전축 — 열리면 ArmorOpenEuler만큼 돈다) / Core(코어 자리) / 냉각기는 ACO_FortressPart
    ///  - 큰 모델은 Hull의 상자 콜라이더도 함께 맞춘다. 장갑이 닫히면 ArmorColliders가 코어를 덮고, 열리면 위가 비어 탑어택 무장이 내리꽂을 수 있다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Railgun : UdonSharpBehaviour
    {
        // Bake가 기초를 넓히고 포대를 벌려 놓은 판 번호(ACO_PrefabBuilder.RailgunLayoutVersion)
        [HideInInspector] public int LayoutVersion;

        public const int STAGE_AIR_DEFENSE = 0;
        public const int STAGE_COOLERS = 1;
        public const int STAGE_CORE = 2;

        [Header("참조")]
        public ACO_Warship Warship;
        [Tooltip("Bake가 넣는다 — 동기화된 단계·주기")]
        public ACO_PhaseRailgun Phase;
        [Tooltip("코어 체력(본체 SaccTarget)")]
        public SaccTarget TargetHealth;
        [Tooltip("내가 탄 기체를 알려 준다(Bake가 넣는다) — 사선 피해")]
        public ACO_RadarContacts Contacts;
        [Tooltip("방공 부위 — 1단계 목표. 뒤 단계에 일부가 수리돼 다시 돈다")]
        public UdonSharpBehaviour[] AirDefense;
        [Tooltip("냉각기 — 2단계 목표")]
        public ACO_FortressPart[] Coolers;
        [Tooltip("소형 레일건(AirDefense에도 들어 있다 — Bake가 연결한다)")]
        public ACO_MiniRailgun[] MiniGuns;

        [Header("포신")]
        public Transform BarrelPivot;
        [Tooltip("포구 — forward가 사격 방향")]
        public Transform Muzzle;
        [Tooltip("쉬는 각도(도)")]
        public float RestElevation = 3f;
        [Tooltip("다 충전됐을 때 각도(도)")]
        public float FireElevation = 16f;
        [Tooltip("포신이 움직이는 빠르기(도/초)")]
        public float ElevationRate = 2.5f;
        [Tooltip("충전 빛 — 머티리얼 발광이 충전에 맞춰 밝아진다")]
        public Renderer ChargeGlow;
        public Color ChargeGlowColor = new Color(0.4f, 1.2f, 3f, 1f);

        [Header("코어 장갑")]
        [Tooltip("장갑판 회전축")]
        public Transform[] ArmorPanels;
        [Tooltip("장갑판별 열린 각도(로컬 오일러, 닫힌 자세 기준)")]
        public Vector3[] ArmorOpenEuler;
        [Tooltip("열리고 닫히는 시간(초)")]
        public float ArmorTime = 2.5f;
        [Tooltip("닫혔을 때만 켜는 충돌 — 코어 위를 덮는다")]
        public Collider[] ArmorColliders;
        public Collider CoreHitbox;
        [Tooltip("코어 락온 마커(지상 레이어 — 공대지 무장이 잠긴다). 장갑이 열렸을 때만 켠다")]
        public GameObject[] CoreMarkers;
        [Tooltip("코어 자리(대폭발·표시)")]
        public Transform Core;

        [Header("사격")]
        [Tooltip("발사 전 사선 레이저(붉은 선)")]
        public LineRenderer WarnBeam;
        [Tooltip("발사 빔")]
        public LineRenderer FireBeam;
        public float FireBeamWidth = 45f;
        public float FireBeamTime = 1.4f;
        [Tooltip("사선 길이(m)")]
        public float BeamLength = 30000f;
        [Tooltip("이 거리(m) 안의 기체는 맞는다 — 레이저 둘레")]
        public float BeamRadius = 160f;
        [Tooltip("사선 안의 내 기체가 입는 피해")]
        public float BeamDamage = 120f;
        [Tooltip("피해를 준 쪽으로 넘기는 오브젝트(SaccEntity가 있는 부위)")]
        public GameObject DamageSource;
        [Tooltip("포구 섬광(한 번 Emit)")]
        public ParticleSystem MuzzleFlash;
        [Tooltip("포구에서 한 번 나오는 거대한 고리")]
        public ParticleSystem MuzzleRing;
        [Tooltip("사선을 따라 날아가는 보이지 않는 파티클. 서브 이미터 Rings가 고리를 남긴다")]
        public ParticleSystem RingCarrier;
        [Tooltip("고리가 생겨 나가는 속도(m/s). 고리 간격은 Rings의 Rate over Distance, 사거리는 RingCarrier의 수명으로 정한다")]
        public float RingTravelSpeed = 14000f;

        [Header("지평선 섬광 (아군 기지 착탄)")]
        [Tooltip("번쩍이는 흰 빛(한 번 Emit)")]
        public ParticleSystem HorizonFlash;
        [Tooltip("남는 주황 빛(한 번 Emit)")]
        public ParticleSystem HorizonGlow;
        [Tooltip("발사 뒤 번쩍이기까지(초)")]
        public float HorizonDelay = 2f;
        [Tooltip("사격 방향으로 이 거리(m)의 지평선")]
        public float HorizonDistance = 50000f;
        [Tooltip("빛 중심 높이(m, 포대 바닥 기준) — 절반쯤 지평선 위로 올라오게")]
        public float HorizonHeight = 1000f;
        [Tooltip("구름·안개 색(Bake가 넣는다) — 하늘 전체가 잠깐 밝아진다")]
        public ACO_CloudField Clouds;
        public float SkyFlash = 0.9f;
        [Tooltip("하늘이 어두워지는 시간(초)")]
        public float SkyFlashTime = 1.6f;
        [Tooltip("멀리서 오는 폭음(2D)")]
        public AudioSource HorizonSound;
        [Tooltip("번쩍임 뒤 폭음까지(초) — 발사 4.5초 뒤에 들리게 맞췄다(거리와 상관없는 연출)")]
        public float HorizonSoundDelay = 2.5f;

        [Header("소리")]
        [Tooltip("충전음 루프 — 충전에 맞춰 높아진다")]
        public AudioSource ChargeSound;
        public AudioSource FireSound;
        public float ChargeVolume = 0.9f;

        [Header("격파")]
        public ACO_FortressBlast Blast;
        [Tooltip("코어가 터진 뒤 대폭발까지(초) — 그동안 연쇄 폭발. 격파 대사가 끝나고 여유가 있게 14초(모듈 CrashHoldMax 20초 안에 폭발 + CrashClearDelay가 끝나야 한다)")]
        public float DeathDelay = 14f;
        public float SecondaryInterval = 0.7f;

        private bool _initialized;
        private bool _spawned;
        private bool _dying;
        private bool _gone;
        private float _deathLeft;
        private float _secondaryTimer;
        private float _goneAt;
        private int _appliedStage = -1;
        private bool _coreOpen;
        private float _armor;
        private float _appliedArmor = -1f;
        private Quaternion[] _armorRest;
        private float _elevation;
        private float _lineTimer;
        private int _firedFor;
        private float _beamLeft;
        private Vector3 _horizonPoint;
        private float _skyLeft;
        private SaccTarget[] _defenseHealth;
        private Material _glowMat;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!Warship) { Warship = GetComponent<ACO_Warship>(); }
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            int n = AirDefense != null ? AirDefense.Length : 0;
            _defenseHealth = new SaccTarget[n];
            for (int i = 0; i < n; i++) { if (AirDefense[i] != null) { _defenseHealth[i] = AirDefense[i].GetComponent<SaccTarget>(); } }
            int a = ArmorPanels != null ? ArmorPanels.Length : 0;
            _armorRest = new Quaternion[a];
            for (int i = 0; i < a; i++) { if (ArmorPanels[i]) { _armorRest[i] = ArmorPanels[i].localRotation; } }
            if (ChargeGlow) { _glowMat = ChargeGlow.material; }
            _elevation = RestElevation;
            if (WarnBeam) { WarnBeam.enabled = false; }
            if (FireBeam) { FireBeam.enabled = false; }
            SetCore(false);
            ApplyStage(STAGE_AIR_DEFENSE);
        }

        // ───────────── 함선 이벤트 ─────────────

        /// ACO_Warship.MissionSpawn이 부른다(모든 클라이언트): 스폰 자리에 선다.
        public void ShipSpawned()
        {
            Init();
            _spawned = true;
            _dying = false;
            _gone = false;
            _appliedStage = -1;
            _appliedArmor = -1f;
            _armor = 0f;
            _elevation = RestElevation;
            _beamLeft = 0f;
            if (FireBeam) { FireBeam.enabled = false; }
            if (WarnBeam) { WarnBeam.enabled = false; }
            // 지난 미션이 코어가 열린 채 끝났으면(격파 없이 실패·교체) 열림 상태가 남는다 — 닫고 시작한다(열려 있어야 하면 동기화된 주기가 곧 다시 연다)
            SetCore(false);
            if (Warship != null) { transform.SetPositionAndRotation(Warship.GetCruiseCenter(), Quaternion.Euler(0f, Warship.GetCruiseYaw(), 0f)); }
            // 늦게 들어왔으면 이미 지난 발사는 다시 그리지 않는다
            _firedFor = Phase != null && Phase.IsArmed() && Networking.GetServerTimeInMilliseconds() - Phase.GetFireMs() >= 0 ? Phase.GetFireMs() : 0;
            ApplyStage(CurrentStage());
        }

        public void ShipDespawned()
        {
            _spawned = false;
            _dying = false;
            _gone = false;
            if (ChargeSound && ChargeSound.isPlaying) { ChargeSound.Stop(); }
            if (WarnBeam) { WarnBeam.enabled = false; }
            if (FireBeam) { FireBeam.enabled = false; }
        }

        /// 코어 파괴(SaccTarget.ExplodeOther — 모든 클라이언트): 연쇄 폭발 뒤 대폭발.
        public void Explode()
        {
            if (_gone || _dying) { return; }
            _dying = true;
            _deathLeft = DeathDelay;
            _secondaryTimer = 0f;
            SetCore(false);
            if (ChargeSound && ChargeSound.isPlaying) { ChargeSound.Stop(); }
            if (WarnBeam) { WarnBeam.enabled = false; }
            if (Phase != null && Phase.IsLive()) { Phase.SayDown(); }
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            UpdateSkyFlash(Time.deltaTime);
            if (!_spawned || _gone) { return; }
            float dt = Time.deltaTime;
            UpdateFireBeam(dt);
            if (_dying)
            {
                UpdateDeath(dt);
                return;
            }
            int stage = CurrentStage();
            if (stage != _appliedStage) { ApplyStage(stage); }
            if (Phase == null || !Phase.IsLive()) { return; }

            int now = Networking.GetServerTimeInMilliseconds();
            float charge = Phase.GetChargeRatio(now);
            UpdateBarrel(charge, dt);
            bool open = Phase.IsCoreOpen(now);
            if (open != _coreOpen) { SetCore(open); }
            UpdateArmor(dt);
            UpdateWarn(now);
            int fireMs = Phase.GetFireMs();
            if (Phase.IsArmed() && now - fireMs >= 0 && _firedFor != fireMs)
            {
                _firedFor = fireMs;
                Fire();
            }
            _lineTimer -= dt;
            if (_lineTimer <= 0f)
            {
                _lineTimer = 0.5f;
                Phase.CheckLines(this);
            }
        }

        private int CurrentStage() { return Phase != null && Phase.IsLive() ? Phase.GetStage() : STAGE_AIR_DEFENSE; }

        private void UpdateBarrel(float charge, float dt)
        {
            float target = Mathf.Lerp(RestElevation, FireElevation, charge * charge * (3f - 2f * charge));
            _elevation = Mathf.MoveTowards(_elevation, target, ElevationRate * dt);
            if (BarrelPivot) { BarrelPivot.localRotation = Quaternion.Euler(-_elevation, 0f, 0f); }
            if (_glowMat) { _glowMat.SetColor("_EmissionColor", ChargeGlowColor * (0.04f + charge * charge * 1.6f)); }
            if (ChargeSound)
            {
                if (!ChargeSound.isPlaying) { ChargeSound.Play(); }
                ChargeSound.pitch = 0.55f + charge * 1.1f;
                ChargeSound.volume = ChargeVolume * (0.2f + charge * 0.8f);
            }
        }

        private void UpdateArmor(float dt)
        {
            _armor = Mathf.MoveTowards(_armor, _coreOpen ? 1f : 0f, dt / Mathf.Max(ArmorTime, 0.01f));
            if (_armor == _appliedArmor || ArmorPanels == null || ArmorOpenEuler == null) { return; }
            _appliedArmor = _armor;
            float k = _armor * _armor * (3f - 2f * _armor);
            int n = Mathf.Min(ArmorPanels.Length, ArmorOpenEuler.Length);
            for (int i = 0; i < n; i++)
            {
                if (ArmorPanels[i]) { ArmorPanels[i].localRotation = _armorRest[i] * Quaternion.Euler(ArmorOpenEuler[i] * k); }
            }
        }

        private void UpdateWarn(int now)
        {
            if (!WarnBeam) { return; }
            bool on = Phase.IsWarning(now) && Muzzle != null;
            if (WarnBeam.enabled != on) { WarnBeam.enabled = on; }
            if (!on) { return; }
            WarnBeam.SetPosition(0, Muzzle.position);
            WarnBeam.SetPosition(1, Muzzle.position + Muzzle.forward * BeamLength);
        }

        // ───────────── 사격 ─────────────

        private void Fire()
        {
            if (Muzzle == null) { return; }
            if (WarnBeam) { WarnBeam.enabled = false; }
            Vector3 from = Muzzle.position;
            Vector3 dir = Muzzle.forward;
            if (FireBeam)
            {
                FireBeam.SetPosition(0, from);
                FireBeam.SetPosition(1, from + dir * BeamLength);
                FireBeam.widthMultiplier = FireBeamWidth;
                FireBeam.enabled = true;
                _beamLeft = FireBeamTime;
            }
            if (MuzzleFlash) { MuzzleFlash.Emit(1); }
            if (RingCarrier)
            {
                var ring = new ParticleSystem.EmitParams();
                ring.position = from;
                ring.velocity = dir * RingTravelSpeed;
                RingCarrier.Emit(ring, 1);
            }
            if (MuzzleRing)
            {
                var ring = new ParticleSystem.EmitParams();
                ring.position = from;
                ring.velocity = dir * 80f;   // 고리 방향은 속도가 정한다 — 0이면 안 된다
                MuzzleRing.Emit(ring, 1);
            }
            if (FireSound) { FireSound.Play(); }   // 연출이라 거리와 상관없이 바로 들린다
            HitLocalVehicle(from, dir);
            // 착탄 자리: 수평으로 지평선까지(포신이 들려 있어도 땅에서 번쩍인다)
            Vector3 flat = dir;
            flat.y = 0f;
            flat = flat.normalized;
            _horizonPoint = transform.position + flat * HorizonDistance + Vector3.up * HorizonHeight;
            SendCustomEventDelayedSeconds(nameof(_HorizonFlash), HorizonDelay);
        }

        /// 지평선 착탄: 흰 섬광 + 남는 주황 빛 + 하늘 밝아짐 + 멀리서 오는 폭음.
        public void _HorizonFlash()
        {
            var emit = new ParticleSystem.EmitParams();
            emit.position = _horizonPoint;
            emit.applyShapeToPosition = false;
            if (HorizonFlash) { HorizonFlash.Emit(emit, 1); }
            if (HorizonGlow) { HorizonGlow.Emit(emit, 1); }
            _skyLeft = SkyFlashTime;
            if (HorizonSound) { SendCustomEventDelayedSeconds(nameof(_PlayHorizonSound), HorizonSoundDelay); }
        }

        public void _PlayHorizonSound()
        {
            if (HorizonSound) { HorizonSound.Play(); }
        }

        /// 하늘 밝아짐: 번개와 같은 값이라 구름·안개가 함께 밝아진다.
        private void UpdateSkyFlash(float dt)
        {
            if (_skyLeft <= 0f) { return; }
            _skyLeft -= dt;
            if (Clouds) { Clouds.SetFlash(_skyLeft > 0f ? SkyFlash * _skyLeft / Mathf.Max(SkyFlashTime, 0.01f) : 0f, _horizonPoint); }
        }

        /// 사선 안의 내 기체에 피해(맞는 사람이 판정한다 — NPC 무기와 같다).
        private void HitLocalVehicle(Vector3 from, Vector3 dir)
        {
            if (Contacts == null || Contacts.LocalVehicle == null) { return; }
            SaccEntity e = Contacts.LocalVehicle;
            Vector3 d = e.transform.position - from;
            float along = Vector3.Dot(d, dir);
            if (along < 0f || along > BeamLength) { return; }
            if ((d - dir * along).sqrMagnitude > BeamRadius * BeamRadius) { return; }
            e.WeaponDamageVehicle(BeamDamage, DamageSource != null ? DamageSource : gameObject, 0);
        }

        private void UpdateFireBeam(float dt)
        {
            if (_beamLeft <= 0f) { return; }
            _beamLeft -= dt;
            if (FireBeam)
            {
                float k = Mathf.Clamp01(_beamLeft / Mathf.Max(FireBeamTime, 0.01f));
                FireBeam.widthMultiplier = FireBeamWidth * k;
                if (_beamLeft <= 0f) { FireBeam.enabled = false; }
            }
        }

        // ───────────── 격파 ─────────────

        private void UpdateDeath(float dt)
        {
            _deathLeft -= dt;
            _secondaryTimer -= dt;
            Vector3 c = Core != null ? Core.position : transform.position;
            if (_secondaryTimer <= 0f)
            {
                _secondaryTimer = SecondaryInterval;
                if (Blast) { Blast.PlaySecondary(c + new Vector3(Random.Range(-90f, 90f), Random.Range(0f, 60f), Random.Range(-90f, 90f))); }
            }
            if (_deathLeft > 0f) { return; }
            _dying = false;
            _gone = true;
            _goneAt = Time.time;
            if (Blast) { Blast.Play(c, false, 1f); }
            // 부위가 자식이라 끄면 다음 스폰의 되살리기 순서가 꼬인다 — 옮기는 것으로 숨긴다
            transform.position = transform.position + Vector3.down * 3000f;
            if (Phase != null && Phase.IsLive()) { Phase.SayImpact(); }
        }

        /// 대폭발 뒤 지난 시간(초). 아직이면 -1.
        public float GetImpactAge() { return _gone ? Time.time - _goneAt : -1f; }

        // ───────────── 단계 표현 ─────────────

        private void ApplyStage(int stage)
        {
            _appliedStage = stage;
            // 코어 단계: TGT 마커를 코어 자리에(코어가 닫혀 있는 동안에도 — 열릴 자리를 보여 준다)
            if (Warship != null) { Warship.MarkerAnchor = stage >= STAGE_CORE ? Core : null; }
            if (Coolers == null) { return; }
            for (int i = 0; i < Coolers.Length; i++) { if (Coolers[i] != null) { Coolers[i].SetExposed(stage >= STAGE_COOLERS); } }
        }

        /// 코어 공개: 열리면 마커·히트박스를 켜고 코어 위 덮개 충돌을 끈다.
        private void SetCore(bool open)
        {
            _coreOpen = open;
            if (CoreHitbox) { CoreHitbox.enabled = open; }
            if (CoreMarkers != null) { for (int i = 0; i < CoreMarkers.Length; i++) { if (CoreMarkers[i]) { CoreMarkers[i].SetActive(open); } } }
            if (ArmorColliders != null) { for (int i = 0; i < ArmorColliders.Length; i++) { if (ArmorColliders[i]) { ArmorColliders[i].enabled = !open; } } }
        }

        // ───────────── 부위 상태 (모듈이 읽는다) ─────────────

        public int GetAirDefenseTotal() { return AirDefense != null ? AirDefense.Length : 0; }

        public int CountAirDefenseAlive()
        {
            Init();
            int n = 0;
            for (int i = 0; i < _defenseHealth.Length; i++) { if (_defenseHealth[i] != null && _defenseHealth[i].Health > 0f) { n++; } }
            return n;
        }

        public int GetCoolerTotal() { return Coolers != null ? Coolers.Length : 0; }

        /// 방공 부위 + 코어 체력 합 — 페이즈가 대기 중 "공격을 시작했는가"를 이 값이 줄었는지로 본다.
        public float SumDefenseAndCoreHealth()
        {
            Init();
            float sum = 0f;
            for (int i = 0; i < _defenseHealth.Length; i++) { if (_defenseHealth[i] != null) { sum += Mathf.Max(_defenseHealth[i].Health, 0f); } }
            if (TargetHealth != null) { sum += Mathf.Max(TargetHealth.Health, 0f); }
            return sum;
        }

        public int CountCoolersAlive()
        {
            int n = 0;
            if (Coolers == null) { return 0; }
            for (int i = 0; i < Coolers.Length; i++) { if (Coolers[i] != null && !Coolers[i].IsDestroyed()) { n++; } }
            return n;
        }

        public float GetCoreHealthRatio()
        {
            if (TargetHealth == null || TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(TargetHealth.Health / TargetHealth.FullHealth);
        }

        /// 부서진 방공 부위를 count곳 되살린다(수리) — 무작위 자리부터, 모든 클라이언트에 알린다.
        public int RepairAirDefense(int count)
        {
            Init();
            int n = _defenseHealth.Length;
            if (n == 0) { return 0; }
            int start = Random.Range(0, n);
            int done = 0;
            for (int k = 0; k < n && done < count; k++)
            {
                int i = (start + k) % n;
                if (AirDefense[i] == null || _defenseHealth[i] == null || _defenseHealth[i].Health > 0f) { continue; }
                AirDefense[i].SendCustomNetworkEvent(NetworkEventTarget.All, "NetRespawn");
                done++;
            }
            return done;
        }
    }
}
