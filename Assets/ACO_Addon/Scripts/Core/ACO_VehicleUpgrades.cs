using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 플레이어 기체 확장(SaccEntity.ExtensionUdonBehaviours에 넣는다, 동기화 없음). 내 강화(ACO_PlayerProgress)를 탑승할 때 기체와 무장에
    /// 적용하고 내릴 때 원래대로 돌린다 — 기체는 여럿이 같이 쓰므로 조종사 화면에서만 바꾼다. 비행은 조종사(오너)가 계산하고, SAV 체력도
    /// 오너가 깎고 동기화하므로 조종사 값만 바꾸면 된다. 미사일 비행 성능은 발사 이벤트에 실려 가고 피해는 쏜 사람 값을 쓴다.
    ///
    /// - 체력: SaccAirVehicle.FullHealth(남은 체력은 같은 비율로), 기동: RollStrength × 배수, PitchStrength/YawStrength는 PitchYawShare만큼만, 추력: ThrottleStrength(+애프터버너),
    ///   연료 효율: FuelConsumption ÷ 배수(+애프터버너 소모량).
    ///   기동성 강화는 양력과 리미터 한계도 함께 올린다(안정성 강화를 합쳤다 — 회전만 세지면 받음각이 튀어 실속하기 쉬웠다): 강화 비율 t(= 단계 / 최대 단계)만큼
    ///   Lift × (1 + LiftBonus·t) + ExtraVelLift에 LiftBonus·t를 더함(내릴 때 뺀다), SAV 리미터(DFUNC_Limits)의 받음각·하중 한계를 LimiterAoABonus·t도, LimiterGBonus·t G 올린다
    ///   (시작점과 한계를 같이 올린다 — 리미터가 구간 폭을 시작할 때 한 번만 재므로). 받음각 한계는 실속각의 LimiterAoAMaxRatio, 하중 한계는 MaxGs − 2를 넘지 않는다.
    ///   후반 단계는 하이알파를 연다(6-179): HighAlphaLevels 칸마다(기본 8 · 9 · 10단계) 리미터 받음각 한계(40° · 55° · 해제), 실속각, 실속 뒤 조종력, TVC(피치 · 요 · 롤).
    /// - 소모품(진행도 CONS_*, 사 둔 것만): 장갑판 = 체력 배수, 연료탱크 = 연료 용량(SaccAirVehicle.FullFuel — 남은 연료는 같은 비율로),
    ///   대응탄 포드 = SAV 플레어(DFUNC_Flares.FullFlares, 채워서 시작), 탄약 증량 = 특수무장(대공·대지·폭탄 계열) 탄수 배수. 내릴 때 되돌린다.
    /// - 무장(Weapons, ACO_DFUNC_Missile): 무장 인스펙터에서 고른 계열(UpgradeFamily)의 강화로 ACO_DFUNC_Missile.ApplyUpgrades. None이면 그대로.
    ///   SAV 무장(DFUNC_Bomb 등)은 강화하지 않는다. 기관포(DFUNC_Gun)는 피해(ACO_GunDamageBonus — 덤 피해)와 탄약(사격 가능 시간 × 배수, 6-257)만.
    /// 또 진행도에 출격(탑승)과 격추(조종 중 폭발 — 추락 포함), 내림(안전지대 밖이면 격추로 센다 — 폭발로 내려진 경우는 빼고)을 알린다.
    /// - 소모품 외형(ConsumableVisuals, CONS_* 순서): 사 둔 소모품으로 탑승하면 그 오브젝트를 켠다 — 조종사가 모든 클라이언트에 알린다(NetConsumableVisuals,
    ///   늦게 들어온 사람에게는 다시 보낸다). 내리면(SFEXT_G_PilotExit, 모든 클라이언트) 끈다.
    /// - 플레어 연사 연출: SAV 플레어가 나갈 때마다(SFEXT_G_LaunchFlare, 모든 클라이언트) FlareBurstDuration 동안 FlareBurstInterval마다 플레어 파티클을
    ///   더 뿌린다(보이기만 — 대응탄 수·효과는 SAV 그대로 한 번). 파티클은 FlareBurstEmitters, 비우면 Flares의 FlareParticles.
    /// Weapons/AirVehicle/Progress는 빌드 시 ACO_SceneBaker가 굽는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_VehicleUpgrades : UdonSharpBehaviour
    {
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다")]
        public ACO_PlayerProgress Progress;
        [Tooltip("이 기체의 SAV 비행 스크립트(빌드 시 자동)")]
        public SaccAirVehicle AirVehicle;
        [Tooltip("이 기체의 ACO 무장(빌드 시 자동 — 특수무장 모듈 포함)")]
        public ACO_DFUNC_Missile[] Weapons;
        [Tooltip("이 기체의 SAV 플레어 기능(빌드 시 자동) — 대응탄 포드 소모품이 수를 늘린다")]
        public DFUNC_Flares[] Flares;
        [Tooltip("이 기체의 기총 탄 파티클에 붙은 피해 강화(빌드 시 Bake가 붙이고 넣는다)")]
        public ACO_GunDamageBonus[] GunBonuses;
        [Tooltip("이 기체의 SAV 기총(빌드 시 Bake가 넣는다) — 기총 탄약 강화가 사격 가능 시간을 늘린다(6-257)")]
        public DFUNC_Gun[] Guns;
        [Header("공중 충돌 방지 (Bake가 채운다)")]
        [Tooltip("이 기체의 충돌 콜라이더(트리거·바퀴 제외) — NPC 전투기와는 서로 통과한다(충돌 데미지 없음)")]
        public Collider[] BodyColliders;
        [Tooltip("씬의 NPC 전투기 전부")]
        public ACO_NPCFighterAI[] NPCFighters;
        [Tooltip("씬의 다른 공중 유닛(헬기·폭격기·수송기·아군 공격헬기)의 충돌 방지 — 전투기 다음에 한 대씩 건다")]
        public ACO_AirCollisionIgnore[] OtherAirUnits;
        [Tooltip("다른 플레이어 기체(Bake가 채운다) — 플레이어 기체끼리도 서로 통과한다(충돌 데미지 없음). 탑승 중 PlayerIgnoreInterval초마다 PlayerIgnoreDistance m 안의 기체와 다시 건다")]
        public ACO_VehicleUpgrades[] OtherPlayerVehicles;
        public float PlayerIgnoreInterval = 1f;
        public float PlayerIgnoreDistance = 1500f;
        [Tooltip("이 기체의 데칼(Bake가 만든다) — 탑승하면 내 데칼을 보내고 내리면 끈다")]
        public ACO_VehicleDecal Decal;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(내가/아군이 격추됨 대사)")]
        [UnityEngine.Serialization.FormerlySerializedAs("Radio")]
        public ACO_Chatter Chatter;

        [Header("기동성 강화 배분")]
        [Tooltip("기동성 강화 중 피치·요에 주는 몫(0~1). 롤은 강화를 다 받는다. 피치까지 다 키우면 받음각이 순간 튀어 리미터를 지나 실속하기 쉬워서 기본 절반")]
        [Range(0f, 1f)] public float PitchYawShare = 0.5f;
        [Tooltip("기동성 최대 단계에서 양력 증가(0.5 = ×1.5 — 예전 안정성 강화 10단계와 같다). 저속에서 쳐들어도 버티고 기수가 덜 처진다")]
        public float LiftBonus = 0.5f;
        [Tooltip("기동성 최대 단계에서 리미터 받음각 한계를 이만큼(도) 올린다(시작점도 같이)")]
        public float LimiterAoABonus = 5f;
        [Tooltip("기동성 최대 단계에서 리미터 하중 한계를 이만큼(G) 올린다(시작점도 같이)")]
        public float LimiterGBonus = 10f;
        [Tooltip("올린 받음각 한계가 실속각(MaxAngleOfAttackPitch)의 이 비율을 넘지 않는다")]
        [Range(0.5f, 0.95f)] public float LimiterAoAMaxRatio = 0.8f;

        [Header("하이알파 해금 (기동성 강화 후반 — 6-179, 칸마다 한 단계: 그 기동성 단계 이상이면 그 칸 값)")]
        [Tooltip("이 기동성 단계부터(오름차순)")]
        public int[] HighAlphaLevels = new int[] { 8, 9, 10 };
        [Tooltip("리미터 받음각 한계(도 — 시작점은 한계 − 원래 구간 폭). 0 이하 = 받음각 제한 끔(하중 제한은 그대로)")]
        public float[] HighAlphaAoALimit = new float[] { 40f, 55f, 0f };
        [Tooltip("SAV MaxAngleOfAttackPitch(실속각) — 넘으면 양력 · 조종력이 줄어 속도가 떨어진다(하이알파의 대가)")]
        public float[] HighAlphaStall = new float[] { 30f, 33f, 35f };
        [Tooltip("SAV HighPitchAoaMinControl — 실속각을 넘었을 때 남는 피치 조종력(기본 0.2)")]
        public float[] HighAlphaControl = new float[] { 0.4f, 0.5f, 0.6f };
        [Tooltip("SAV TVC — 엔진이 켜져 있으면 속도 · 받음각과 상관없이 남는 최소 조종력(피치 · 요 · 롤)")]
        public float[] HighAlphaPitchTvc = new float[] { 0.3f, 0.45f, 0.6f };
        public float[] HighAlphaYawTvc = new float[] { 0f, 0.2f, 0.4f };
        public float[] HighAlphaRollTvc = new float[] { 0f, 0.15f, 0.3f };

        [Header("소모품 외형 (CONS_* 순서: 장갑판, 대응탄 포드, 탄약 증량, 연료탱크 — 비워도 된다)")]
        [Tooltip("사 둔 소모품으로 탑승하면 켜고(모든 플레이어 화면) 내리면 끈다. 여러 개면 빈 부모로 묶어 부모를 넣는다")]
        public GameObject[] ConsumableVisuals;

        [Header("플레어 연사 연출 (보이기만)")]
        public bool FlareBurst = true;
        [Tooltip("한 번 쏠 때 연사가 이어지는 시간(초). 이어서 쏘면 늘어난다")]
        public float FlareBurstDuration = 1.5f;
        [Tooltip("연사 간격(초)")]
        public float FlareBurstInterval = 0.12f;
        [Tooltip("간격마다 파티클 시스템 하나에서 뿌리는 수")]
        public int FlareBurstPerEmit = 1;
        [Tooltip("뿌릴 파티클 시스템. 비우면 Flares(SAV DFUNC_Flares)의 FlareParticles")]
        public ParticleSystem[] FlareBurstEmitters;

        // SaccEntity가 주입한다.
        [System.NonSerialized] public SaccEntity EntityControl;
        [Tooltip("기체 형식 스위치(같은 기체 — 설치 도구가 넣는다, 6-343). 폭발 때 뒷자리 승객을 브리핑실로")]
        public ACO_AirframeVariant Airframe;

        [Header("안전지대 보호")]
        [Tooltip("이 기체의 락온 마커(적이 노리는 'AAMTarget', 아군 항공기 레이어 25 — Bake가 넣는다). 안전지대(Progress.SafeZones — 보급 트리거) 안에 있으면 "
            + "모든 클라이언트가 이 마커를 끈다: 적 대공포·SAM·전투기가 노리지 않고, 날아오던 적 미사일도 표적을 잃는다. 나오면 다시 켠다")]
        public GameObject LockTarget;
        [Tooltip("안전지대 안인지 보는 간격(초)")]
        public float SafeCheckInterval = 0.5f;
        private bool _inSafe;
        private bool _safeLoop;

        /// 이 기체가 지금 안전지대(보급 트리거) 안인가 — 적이 노리지 않는다.
        public bool IsInSafeZone() { return _inSafe; }

        private bool _pilot;
        private bool _exploded;   // 이번 탑승 중 폭발했다(SAV가 폭발 뒤 조종사를 내린다 — 그 내림은 따로 세지 않는다)
        private bool _applied;
        private bool _baseSet;
        private float _baseHealth;
        private float _basePitch;
        private float _baseYaw;
        private float _baseRoll;
        private float _baseThrottle;
        private float _baseFuelConsumption;
        private float _baseLift;
        private float _baseFullFuel;
        private int[] _baseFlares;
        private float[] _baseGunAmmo;   // 기총마다 원래 사격 가능 시간(초)
        private DFUNC_Limits _limits;
        private bool _limitsSearched;
        private float _baseAoABegin;
        private float _baseAoA;
        private float _baseGBegin;
        private float _baseG;
        private bool _baseNoAoALimit;
        private float _baseStall;
        private float _baseStallControl;
        private float _basePitchTvc;
        private float _baseYawTvc;
        private float _baseRollTvc;
        private float _velLiftAdded;   // ExtraVelLift에 더해 둔 값(플랩도 같은 값을 더하고 빼므로 곱하지 않고 더한 만큼만 되돌린다)
        private float _burstUntil;
        private bool _bursting;

        void Start()
        {
            ShowConsumables(0);
            if (LockTarget != null && !_safeLoop)
            {
                _safeLoop = true;
                SendCustomEventDelayedSeconds(nameof(_SafeTick), Random.Range(0.1f, Mathf.Max(SafeCheckInterval, 0.1f)));
            }
        }

        /// 모든 클라이언트: 기체가 안전지대 콜라이더(보급 트리거) 안이면 락온 마커를 끈다. 위치는 SAV가 동기화하므로 화면마다 같은 답이 나온다.
        public void _SafeTick()
        {
            SendCustomEventDelayedSeconds(nameof(_SafeTick), Mathf.Max(SafeCheckInterval, 0.1f));
            if (LockTarget == null) { return; }
            Vector3 p = EntityControl != null ? EntityControl.transform.position : transform.position;
            bool inside = false;
            Collider[] zones = Progress != null ? Progress.SafeZones : null;
            if (zones != null)
            {
                for (int i = 0; i < zones.Length && !inside; i++)
                {
                    Collider z = zones[i];
                    if (z == null || !z.gameObject.activeInHierarchy) { continue; }
                    // 콜라이더 안이면 가장 가까운 점이 그 점 자신이다(회전한 상자도 맞다)
                    if ((z.ClosestPoint(p) - p).sqrMagnitude < 0.01f) { inside = true; }
                }
            }
            if (inside == _inSafe && LockTarget.activeSelf == !inside) { return; }
            _inSafe = inside;
            LockTarget.SetActive(!inside);
        }

        public void SFEXT_O_PilotEnter()
        {
            _pilot = true;
            _exploded = false;
            if (Progress) { Progress.ReportPiloting(true); Progress.ReportFlightVehicle(AirVehicle); }
            _applied = false;   // 자유 비행에서 남겨 둔 사양도 지금 조종사 값으로 다시 맞춘다
            Apply(true);
            SendConsumableVisuals();
            if (Decal) { Decal.LocalPilotEnter(); }
            // 충돌 데미지는 조종사(오너) 화면에서 계산한다 — 탑승 때 SAV가 콜라이더 레이어를 바꾼 다음에 다시 건다.
            // 한 프레임에 몰지 않고 NPC 한 대씩 프레임마다 나눠 건다(끊김 방지).
            _ignoreIndex = 0;
            if (!_ignoring)
            {
                _ignoring = true;
                SendCustomEventDelayedFrames(nameof(IgnoreNextNPC), 2);
            }
            if (!_playerIgnoreLoop)
            {
                _playerIgnoreLoop = true;
                SendCustomEventDelayedFrames(nameof(_RefreshPlayerIgnore), 3);
            }
        }

        private bool _playerIgnoreLoop;

        /// 플레이어 기체끼리 충돌 끄기(이 클라이언트에서 내가 조종 중인 기체만 — 충돌 데미지는 조종사 화면에서 계산한다).
        /// 상대가 격추 · 부활하며 콜라이더를 껐다 켜면 무시 상태가 풀리므로 탑승 중 주기적으로 다시 건다(가까운 기체만).
        public void _RefreshPlayerIgnore()
        {
            if (!_pilot) { _playerIgnoreLoop = false; return; }
            if (OtherPlayerVehicles != null && BodyColliders != null)
            {
                float maxSq = PlayerIgnoreDistance * PlayerIgnoreDistance;
                for (int k = 0; k < OtherPlayerVehicles.Length; k++)
                {
                    ACO_VehicleUpgrades other = OtherPlayerVehicles[k];
                    if (!other || other == this || other.BodyColliders == null) { continue; }
                    if ((other.transform.position - transform.position).sqrMagnitude > maxSq) { continue; }
                    for (int i = 0; i < BodyColliders.Length; i++)
                    {
                        Collider a = BodyColliders[i];
                        if (!a) { continue; }
                        for (int j = 0; j < other.BodyColliders.Length; j++)
                        {
                            Collider b = other.BodyColliders[j];
                            if (b) { Physics.IgnoreCollision(a, b, true); }
                        }
                    }
                }
            }
            SendCustomEventDelayedSeconds(nameof(_RefreshPlayerIgnore), Mathf.Max(PlayerIgnoreInterval, 0.2f));
        }

        private int _ignoreIndex;
        private bool _ignoring;

        /// 이 클라이언트가 지금 이 기체를 조종 중인가(NPC가 켜질 때 내 기체를 찾는다).
        public bool IsLocalPilot() { return _pilot; }

        /// 전투기 → 다른 공중 유닛(헬기·폭격기·수송기) 순으로 프레임마다 한 대씩.
        public void IgnoreNextNPC()
        {
            int fighters = NPCFighters != null ? NPCFighters.Length : 0;
            int others = OtherAirUnits != null ? OtherAirUnits.Length : 0;
            if (_ignoreIndex >= fighters + others) { _ignoring = false; return; }
            if (_ignoreIndex < fighters)
            {
                ACO_NPCFighterAI npc = NPCFighters[_ignoreIndex];
                if (npc) { npc.IgnoreWith(BodyColliders); }
            }
            else
            {
                ACO_AirCollisionIgnore unit = OtherAirUnits[_ignoreIndex - fighters];
                if (unit) { unit.IgnoreWith(BodyColliders); }
            }
            _ignoreIndex++;
            SendCustomEventDelayedFrames(nameof(IgnoreNextNPC), 1);
        }

        public void SFEXT_O_PilotExit()
        {
            // 자유 비행: 내려도 사양을 남긴다(격추됐거나 기체가 회수되면 ClearKeptSpec이 지운다)
            if (!KeepSpec())
            {
                Apply(false);
                if (Decal) { Decal.LocalPilotExit(); }
            }
            // 스스로 내렸으면(폭발이 아니면) 내린 자리를 알린다 — 안전지대 밖이면 격추로 센다.
            if (_pilot && !_exploded && Progress && EntityControl && !EntityControl.dead)
            {
                Progress.ReportPilotExit(EntityControl.transform.position);
            }
            _pilot = false;
            if (Progress) { Progress.ReportPiloting(false); Progress.ReportFlightVehicle(null); }
        }

        // ───────────── 소모품 외형 ─────────────

        /// 조종사: 사 둔 소모품 외형을 내 화면에 바로 켜고 다른 클라이언트에 알린다
        /// (네트워크 이벤트는 UdonBehaviour 동기화가 None이면 나가지 않는다 — 내 화면만큼은 그와 상관없이 보이게).
        private void SendConsumableVisuals()
        {
            int mask = 0;
            if (Progress)
            {
                for (int c = 0; c < ACO_PlayerProgress.CONSUMABLE_COUNT; c++) { if (Progress.HasConsumable(c)) { mask |= 1 << c; } }
            }
            ShowConsumables(mask);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetConsumableVisuals), (byte)mask);
        }

        [NetworkCallable]
        public void NetConsumableVisuals(byte mask) { ShowConsumables(mask); }

        private bool KeepSpec() { return Progress && Progress.FreeFlight && !_exploded; }

        // 내리면(모든 클라이언트) 끈다
        /// 뒷자리 승객이 내렸다(이 화면의 나 — 6-344): 공중이거나 안전 구역(항모 등) 밖이면 브리핑실로, 안이면 그냥 내린다.
        public void SFEXT_P_PassengerExit()
        {
            if (Airframe && Airframe.Flow) { Airframe.Flow.PassengerLeft(AirVehicle && !AirVehicle.Taxiing); }
        }

        public void SFEXT_G_PilotExit()
        {
            if (!Progress || !Progress.FreeFlight) { ShowConsumables(0); }
        }

        public void SFEXT_G_RespawnButton() { ClearKeptSpec(); }
        public void SFEXT_G_ReAppear() { ClearKeptSpec(); }

        /// 빈 기체가 회수됐다: 자유 비행에서 남겨 둔 사양을 지운다.
        private void ClearKeptSpec()
        {
            if (_pilot || (EntityControl && EntityControl.Occupied)) { return; }
            ShowConsumables(0);
            if (!_applied) { return; }
            Apply(false);
            if (Decal) { Decal.LocalPilotExit(); }
        }

        // 늦게 들어온 사람에게 다시
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (_pilot) { SendConsumableVisuals(); }
        }

        private void ShowConsumables(int mask)
        {
            if (ConsumableVisuals == null) { return; }
            for (int c = 0; c < ConsumableVisuals.Length; c++)
            {
                GameObject g = ConsumableVisuals[c];
                if (!g) { continue; }
                bool on = (mask & (1 << c)) != 0;
                if (g.activeSelf != on) { g.SetActive(on); }
            }
        }

        // ───────────── 플레어 연사 연출 ─────────────

        public void SFEXT_G_LaunchFlare()
        {
            if (!FlareBurst || FlareBurstDuration <= 0f) { return; }
            _burstUntil = Time.time + FlareBurstDuration;
            if (_bursting) { return; }
            _bursting = true;
            SendCustomEventDelayedSeconds(nameof(_FlareBurstTick), Mathf.Max(FlareBurstInterval, 0.02f));
        }

        public void _FlareBurstTick()
        {
            if (Time.time > _burstUntil || (EntityControl && EntityControl.dead))
            {
                _bursting = false;
                return;
            }
            int n = Mathf.Max(FlareBurstPerEmit, 1);
            if (FlareBurstEmitters != null && FlareBurstEmitters.Length > 0)
            {
                for (int i = 0; i < FlareBurstEmitters.Length; i++) { if (FlareBurstEmitters[i]) { FlareBurstEmitters[i].Emit(n); } }
            }
            else if (Flares != null)
            {
                for (int i = 0; i < Flares.Length; i++)
                {
                    DFUNC_Flares f = Flares[i];
                    if (!f || f.FlareParticles == null) { continue; }
                    for (int k = 0; k < f.FlareParticles.Length; k++) { if (f.FlareParticles[k]) { f.FlareParticles[k].Emit(n); } }
                }
            }
            SendCustomEventDelayedSeconds(nameof(_FlareBurstTick), Mathf.Max(FlareBurstInterval, 0.02f));
        }

        /// 모든 클라이언트에서 오지만, 이 클라이언트가 조종 중일 때만 센다(폭발 뒤 SAV가 조종사를 내린다).
        public void SFEXT_G_Explode()
        {
            // 뒷자리 승객(6-343): SAV가 좌석에서 내리는데 그 자리(공중 · 바다)에 남는다 — 브리핑실로
            if (!_pilot && Airframe && Airframe.IsLocalPassenger()) { Airframe.ForceExitPassengers(); }
            if (!_pilot)
            {
                // 다른 사람이 조종하던 기체가 터졌다 — 아군 격추 대사(이름 = 조종사)
                if (Chatter && EntityControl && EntityControl.Occupied)
                {
                    VRCPlayerApi pilot = Networking.GetOwner(EntityControl.gameObject);
                    Chatter.Report(ACO_Chatter.EV_ALLY_DOWN, Utilities.IsValid(pilot) ? pilot.displayName : "");
                }
                return;
            }
            _exploded = true;
            if (Progress) { Progress.ReportShotDown(); }
            if (Chatter) { Chatter.Report(ACO_Chatter.EV_SELF_DOWN, ""); }
        }

        /// SAV 리미터(DFUNC_Limits) 한계를 기동성 강화 비율 t만큼 올린다(t = 0이면 원래 값). 시작점과 한계를 같은 양만큼 — 리미터가 구간 폭을 시작할 때만 잰다.
        /// 하이알파(6-179): 해금 칸이면 받음각 한계를 그 칸 값으로(실속각 비율 제한 없이), 0 이하면 받음각 제한을 끈다(하중 제한은 그대로).
        private void ApplyLimiter(float t, int level)
        {
            if (!FindLimits() || !AirVehicle) { return; }
            int tier = HighAlphaTier(level);
            float tierLimit = tier >= 0 ? At(HighAlphaAoALimit, tier, 0f) : 0f;
            bool aoaOff = tier >= 0 && tierLimit <= 0f;
            float aoaShift;
            if (tier >= 0 && !aoaOff) { aoaShift = Mathf.Max(tierLimit - _baseAoA, 0f); }
            else if (aoaOff) { aoaShift = 0f; }
            else
            {
                float aoaMax = BaseStall() * LimiterAoAMaxRatio;   // 원래 실속각 기준(하이알파가 올린 값이 아니라)
                aoaShift = Mathf.Clamp(LimiterAoABonus * t, 0f, Mathf.Max(aoaMax - _baseAoA, 0f));
            }
            float gShift = Mathf.Clamp(LimiterGBonus * t, 0f, Mathf.Max(AirVehicle.MaxGs - 2f - _baseG, 0f));
            _limits.AoALimiter_Begin = _baseAoABegin + aoaShift;
            _limits.AoALimiter = _baseAoA + aoaShift;
            _limits.GLimiter_Begin_Pos = _baseGBegin + gShift;
            _limits.GLimiter_Pos = _baseG + gShift;
            _limits.DisableAoALimiter = aoaOff ? true : _baseNoAoALimit;
        }

        /// 상점 미리보기(6-179): 기동성 단계 level의 리미터 받음각 한계(도). 0 = 받음각 제한 없음(PSM). ApplyLimiter와 같은 계산.
        public float GetAoALimitAt(int level)
        {
            if (!FindLimits() || Progress == null) { return 0f; }
            int maxLevel = Progress.GetMaxLevel();
            float t = maxLevel > 0 ? Mathf.Clamp01(level / (float)maxLevel) : 0f;
            int tier = HighAlphaTier(level);
            if (tier >= 0)
            {
                float lim = At(HighAlphaAoALimit, tier, 0f);
                return lim <= 0f ? 0f : Mathf.Max(lim, _baseAoA);
            }
            float aoaMax = BaseStall() * LimiterAoAMaxRatio;
            return _baseAoA + Mathf.Clamp(LimiterAoABonus * t, 0f, Mathf.Max(aoaMax - _baseAoA, 0f));
        }

        /// 상점 미리보기(6-179): 기동성 단계 level의 리미터 하중 한계(G).
        public float GetGLimitAt(int level)
        {
            if (!FindLimits() || Progress == null || !AirVehicle) { return 0f; }
            int maxLevel = Progress.GetMaxLevel();
            float t = maxLevel > 0 ? Mathf.Clamp01(level / (float)maxLevel) : 0f;
            return _baseG + Mathf.Clamp(LimiterGBonus * t, 0f, Mathf.Max(AirVehicle.MaxGs - 2f - _baseG, 0f));
        }

        /// 원래 실속각(하이알파가 올리기 전).
        private float BaseStall() { return _baseSet ? _baseStall : (AirVehicle ? AirVehicle.MaxAngleOfAttackPitch : 25f); }

        /// 리미터를 찾고 원래 값을 한 번 적어 둔다(강화를 적용하기 전에 불러야 원래 값이다 — 처음 탑승 전 상점 미리보기도 여기서).
        private bool FindLimits()
        {
            if (!_limitsSearched)
            {
                _limitsSearched = true;
                SaccEntity entity = EntityControl;
                // 기체가 아직 시작 전이면(EntityControl은 SAV가 시작할 때 넣는다 — 상점 미리보기) SAV 비행 스크립트의 부모에서 찾는다
                if (!entity && AirVehicle) { entity = AirVehicle.GetComponentInParent<SaccEntity>(); }
                if (entity) { _limits = entity.GetComponentInChildren<DFUNC_Limits>(true); }
                if (_limits)
                {
                    _baseAoABegin = _limits.AoALimiter_Begin;
                    _baseAoA = _limits.AoALimiter;
                    _baseGBegin = _limits.GLimiter_Begin_Pos;
                    _baseG = _limits.GLimiter_Pos;
                    _baseNoAoALimit = _limits.DisableAoALimiter;
                }
            }
            return _limits != null;
        }

        /// 기동성 단계 level에 맞는 하이알파 칸(없으면 -1) — HighAlphaLevels 중 level 이하인 가장 높은 칸.
        private int HighAlphaTier(int level)
        {
            int tier = -1;
            if (HighAlphaLevels == null) { return tier; }
            for (int i = 0; i < HighAlphaLevels.Length; i++) { if (HighAlphaLevels[i] > 0 && level >= HighAlphaLevels[i]) { tier = i; } }
            return tier;
        }

        private float At(float[] values, int i, float fallback)
        {
            return values != null && i >= 0 && i < values.Length ? values[i] : fallback;
        }

        /// 하이알파(6-179): 기동성 단계에 따라 SAV 실속각 · 고받음각 조종력 · TVC(엔진이 켜져 있으면 남는 최소 조종력)를 올린다. level 0이면 원래 값.
        /// SAV는 엔진을 켤 때 TVC를 *Start 값으로 되돌리므로 그 값도 같이 바꾼다(엔진이 꺼져 있으면 지금 값은 0 그대로).
        private void ApplyHighAlpha(int level)
        {
            if (!AirVehicle) { return; }
            int tier = HighAlphaTier(level);
            float stall = _baseStall;
            float control = _baseStallControl;
            float pitchTvc = _basePitchTvc;
            float yawTvc = _baseYawTvc;
            float rollTvc = _baseRollTvc;
            if (tier >= 0)
            {
                stall = Mathf.Max(At(HighAlphaStall, tier, _baseStall), _baseStall);
                control = Mathf.Max(At(HighAlphaControl, tier, _baseStallControl), _baseStallControl);
                pitchTvc = Mathf.Max(At(HighAlphaPitchTvc, tier, 0f), _basePitchTvc);
                yawTvc = Mathf.Max(At(HighAlphaYawTvc, tier, 0f), _baseYawTvc);
                rollTvc = Mathf.Max(At(HighAlphaRollTvc, tier, 0f), _baseRollTvc);
            }
            AirVehicle.MaxAngleOfAttackPitch = stall;
            AirVehicle.HighPitchAoaMinControl = control;
            AirVehicle.SetProgramVariable("PitchThrustVecMultiStart", pitchTvc);
            AirVehicle.SetProgramVariable("YawThrustVecMultiStart", yawTvc);
            AirVehicle.SetProgramVariable("RollThrustVecMultiStart", rollTvc);
            if (AirVehicle._EngineOn)
            {
                AirVehicle.PitchThrustVecMulti = pitchTvc;
                AirVehicle.YawThrustVecMulti = yawTvc;
                AirVehicle.RollThrustVecMulti = rollTvc;
            }
            // 뒤로 미끄러질 때(속도 기준 받음각 90° 넘음) 조작 뒤집기 끔(6-295, 사용자 "그냥 기본적으로 뒤집히지 않게 하자"):
            // SAV는 시작 때 TVC가 0인 축을 −2 · −2.4 · −1.6배로 정해 두어 PSM 중 조작이 반대로 걸렸다. 모든 단계 · 모든 축 1(뒤집지 않음).
            // 엔진을 켤 때 SAV가 *Start 값으로 되돌리므로 그것도 1로.
            AirVehicle.SetProgramVariable("ReversingPitchStrengthZero", 1f);
            AirVehicle.SetProgramVariable("ReversingYawStrengthZero", 1f);
            AirVehicle.SetProgramVariable("ReversingRollStrengthZero", 1f);
            AirVehicle.SetProgramVariable("ReversingPitchStrengthZeroStart", 1f);
            AirVehicle.SetProgramVariable("ReversingYawStrengthZeroStart", 1f);
            AirVehicle.SetProgramVariable("ReversingRollStrengthZeroStart", 1f);
        }

        private void Apply(bool on)
        {
            if (!Progress || on == _applied) { return; }
            _applied = on;

            if (AirVehicle)
            {
                if (!_baseSet)
                {
                    _baseSet = true;
                    _baseHealth = AirVehicle.FullHealth;
                    _basePitch = AirVehicle.PitchStrength;
                    _baseYaw = AirVehicle.YawStrength;
                    _baseRoll = AirVehicle.RollStrength;
                    _baseThrottle = AirVehicle.ThrottleStrength;
                    _baseFuelConsumption = AirVehicle.FuelConsumption;
                    _baseLift = AirVehicle.Lift;
                    _baseFullFuel = AirVehicle.FullFuel;
                    _baseStall = AirVehicle.MaxAngleOfAttackPitch;
                    _baseStallControl = AirVehicle.HighPitchAoaMinControl;
                    _basePitchTvc = (float)AirVehicle.GetProgramVariable("PitchThrustVecMultiStart");
                    _baseYawTvc = (float)AirVehicle.GetProgramVariable("YawThrustVecMultiStart");
                    _baseRollTvc = (float)AirVehicle.GetProgramVariable("RollThrustVecMultiStart");
                }
                // 체력 = 강화 × 장갑판
                float hull = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_HULL) * Progress.GetConsumableFactor(ACO_PlayerProgress.CONS_ARMOR) : 1f;
                // 출력 · 기동성은 상점에서 고른 적용 단계(6-345 — 산 단계보다 낮출 수 있다)
                float mobility = on ? Progress.GetAppliedFactor(ACO_PlayerProgress.UP_MOBILITY) : 1f;
                float engine = on ? Progress.GetAppliedFactor(ACO_PlayerProgress.UP_ENGINE) : 1f;
                float fuel = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_FUEL) : 1f;
                // 기동성 강화 비율(0 ~ 1) — 양력·리미터 한계도 이만큼 함께 올린다(안정성 강화는 기동성에 합쳤다)
                int maxLevel = Progress.GetMaxLevel();
                float t = on && maxLevel > 0 ? Mathf.Clamp01(Progress.GetAppliedLevel(ACO_PlayerProgress.UP_MOBILITY) / (float)maxLevel) : 0f;
                float stability = 1f + LiftBonus * t;

                float oldFull = AirVehicle.FullHealth;
                float newFull = _baseHealth * hull;
                if (oldFull > 0f) { AirVehicle.Health = AirVehicle.Health * (newFull / oldFull); }
                AirVehicle.FullHealth = newFull;

                // 롤은 강화를 다 받고, 피치·요는 PitchYawShare만큼만(10단계 ×1.3이면 피치·요 ×1.15)
                float pitchYaw = 1f + (mobility - 1f) * PitchYawShare;
                AirVehicle.PitchStrength = _basePitch * pitchYaw;
                AirVehicle.YawStrength = _baseYaw * pitchYaw;
                AirVehicle.RollStrength = _baseRoll * mobility;

                AirVehicle.ThrottleStrength = _baseThrottle * engine;
                // SAV가 시작할 때 한 번 계산해 두는 값이라 같이 맞춘다.
                AirVehicle.ThrottleStrengthAB = AirVehicle.ThrottleStrength * (AirVehicle.AfterburnerThrustMulti - 1f);

                // 연료 효율: 소모량을 배수로 나눈다(10단계 x2.5 → 60% 덜 쓴다). 애프터버너 소모량도 SAV가 시작할 때 계산해 두므로 같이 맞춘다.
                AirVehicle.FuelConsumption = _baseFuelConsumption / Mathf.Max(fuel, 0.01f);
                AirVehicle.FuelConsumptionAB = AirVehicle.FuelConsumption * (AirVehicle.FuelConsumptionABMulti - 1f);

                // 연료탱크: 용량을 늘리고 남은 연료도 같은 비율로(탑승 때 가득이면 가득으로)
                float tank = on ? Progress.GetConsumableFactor(ACO_PlayerProgress.CONS_FUEL) : 1f;
                float oldFuel = AirVehicle.FullFuel;
                float newFuel = _baseFullFuel * tank;
                if (oldFuel > 0f) { AirVehicle.Fuel = AirVehicle.Fuel * (newFuel / oldFuel); }
                AirVehicle.FullFuel = newFuel;

                // 안정성: 받음각 양력(Lift)을 키워 저속에서 쳐들어도 버티고, 속도 양력(VelLift — SAV가 매 프레임 VelLiftStart × ExtraVelLift로
                // 다시 계산한다)을 ExtraVelLift로 올려 기수가 덜 처진다. ExtraVelLift는 플랩이 더하고 빼는 값이라 더한 만큼만 되돌린다.
                AirVehicle.Lift = _baseLift * stability;
                float velLiftDelta = stability - 1f;
                AirVehicle.ExtraVelLift += velLiftDelta - _velLiftAdded;
                _velLiftAdded = velLiftDelta;

                int mobilityLevel = on ? Progress.GetAppliedLevel(ACO_PlayerProgress.UP_MOBILITY) : 0;
                ApplyHighAlpha(mobilityLevel);
                ApplyLimiter(t, mobilityLevel);
            }

            // 기총 피해: 탄 파티클의 덤 피해 배수(충돌은 조종사 화면에서만 일어난다)
            if (GunBonuses != null)
            {
                float gun = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_GUN) : 1f;
                for (int i = 0; i < GunBonuses.Length; i++) { if (GunBonuses[i]) { GunBonuses[i].Factor = gun; } }
            }

            // 기총 탄약(6-257): SAV 기총의 사격 가능 시간 × 배수. 남은 탄은 같은 비율로(가득이면 가득으로), 내리면 원래 시간으로.
            // SAV가 시작할 때 계산해 두는 재보급 속도(reloadspeed)와 탄 막대 눈금(FullGunAmmoDivider)도 같이 맞춘다.
            if (Guns != null)
            {
                if (_baseGunAmmo == null || _baseGunAmmo.Length != Guns.Length)
                {
                    _baseGunAmmo = new float[Guns.Length];
                    for (int i = 0; i < Guns.Length; i++) { _baseGunAmmo[i] = Guns[i] ? Guns[i].FullGunAmmoInSeconds : 0f; }
                }
                float ammo = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_GUN_AMMO) : 1f;
                for (int i = 0; i < Guns.Length; i++)
                {
                    DFUNC_Gun g = Guns[i];
                    if (!g) { continue; }
                    if (_baseGunAmmo[i] <= 0f) { _baseGunAmmo[i] = g.FullGunAmmoInSeconds; }   // SAV가 아직 시작 전이었다
                    if (_baseGunAmmo[i] <= 0f) { continue; }
                    float oldFull = g.FullGunAmmoInSeconds;
                    float newFull = _baseGunAmmo[i] * ammo;
                    if (Mathf.Approximately(oldFull, newFull)) { continue; }
                    if (oldFull > 0f) { g.GunAmmoInSeconds = Mathf.Min(g.GunAmmoInSeconds * (newFull / oldFull), newFull); }
                    g.FullGunAmmoInSeconds = newFull;
                    g.SetProgramVariable("reloadspeed", newFull / Mathf.Max(g.FullReloadTimeSec, 0.01f));
                    g.SetProgramVariable("FullGunAmmoDivider", 1f / newFull);
                }
            }

            // 대응탄 수 강화(단계마다 한 발씩 더함) → 대응탄 포드(소모품 배수): SAV 플레어 최대 수를 늘리고 채운다(내릴 때 원래 수로 채운다)
            if (Flares != null)
            {
                if (_baseFlares == null || _baseFlares.Length != Flares.Length)
                {
                    _baseFlares = new int[Flares.Length];
                    for (int i = 0; i < Flares.Length; i++) { _baseFlares[i] = Flares[i] ? Flares[i].FullFlares : 0; }
                }
                float cm = on ? Progress.GetConsumableFactor(ACO_PlayerProgress.CONS_CM) : 1f;
                int extra = on ? Progress.GetCountermeasureBonus() : 0;
                for (int i = 0; i < Flares.Length; i++)
                {
                    DFUNC_Flares f = Flares[i];
                    if (!f) { continue; }
                    int want = Mathf.RoundToInt((_baseFlares[i] + extra) * cm);
                    if (f.FullFlares == want) { continue; }
                    f.FullFlares = want;
                    f.ReInitNumFlares();
                }
            }

            if (Weapons == null) { return; }
            float extraAmmo = Progress.GetConsumableFactor(ACO_PlayerProgress.CONS_AMMO);
            for (int i = 0; i < Weapons.Length; i++)
            {
                ACO_DFUNC_Missile w = Weapons[i];
                if (!w) { continue; }
                if (!on)
                {
                    w.ApplyUpgrades(1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f);
                    continue;
                }
                // None(4) 등 계열 밖의 값이면 항목이 없어 모든 배수가 1이다.
                int f = (int)w.UpgradeFamily;
                // 탄약 증량은 특수무장(대공·대지·폭탄 계열)과 따로 켠 OTHER 무장(MMP·RGP — 강화 트리는 받지 않는다)만
                bool special = f == ACO_PlayerProgress.FAMILY_SP_AAM || f == ACO_PlayerProgress.FAMILY_SP_AGM || f == ACO_PlayerProgress.FAMILY_SP_BOMB || w.TakesExtraAmmo;
                w.ApplyUpgrades(
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_DAMAGE),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_AMMO) * (special ? extraAmmo : 1f),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_TURN),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_LOCK_SPEED),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_LOCK_RANGE),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_SEEKER),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_BLAST),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_RELOAD));
            }
        }
    }
}
