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
    ///   연료 효율: FuelConsumption ÷ 배수(+애프터버너 소모량), 안정성: Lift × 배수 + ExtraVelLift에 (배수 − 1)을 더함(내릴 때 뺀다).
    /// - 소모품(진행도 CONS_*, 사 둔 것만): 장갑판 = 체력 배수, 연료탱크 = 연료 용량(SaccAirVehicle.FullFuel — 남은 연료는 같은 비율로),
    ///   대응탄 포드 = SAV 플레어(DFUNC_Flares.FullFlares, 채워서 시작), 탄약 증량 = 특수무장(대공·대지·폭탄 계열) 탄수 배수. 내릴 때 되돌린다.
    /// - 무장(Weapons, ACO_DFUNC_Missile): 무장 인스펙터에서 고른 계열(UpgradeFamily)의 강화로 ACO_DFUNC_Missile.ApplyUpgrades. None이면 그대로.
    ///   SAV 무장(DFUNC_Bomb 등)과 기관포는 강화하지 않는다.
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
        [Header("공중 충돌 방지 (Bake가 채운다)")]
        [Tooltip("이 기체의 충돌 콜라이더(트리거·바퀴 제외) — NPC 전투기와는 서로 통과한다(충돌 데미지 없음)")]
        public Collider[] BodyColliders;
        [Tooltip("씬의 NPC 전투기 전부")]
        public ACO_NPCFighterAI[] NPCFighters;
        [Tooltip("씬의 다른 공중 유닛(헬기·폭격기·수송기·아군 공격헬기)의 충돌 방지 — 전투기 다음에 한 대씩 건다")]
        public ACO_AirCollisionIgnore[] OtherAirUnits;
        [Tooltip("이 기체의 데칼(Bake가 만든다) — 탑승하면 내 데칼을 보내고 내리면 끈다")]
        public ACO_VehicleDecal Decal;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(내가/아군이 격추됨 대사)")]
        [UnityEngine.Serialization.FormerlySerializedAs("Radio")]
        public ACO_Chatter Chatter;

        [Header("기동성 강화 배분")]
        [Tooltip("기동성 강화 중 피치·요에 주는 몫(0~1). 롤은 강화를 다 받는다. 피치까지 다 키우면 받음각이 순간 튀어 리미터를 지나 실속하기 쉬워서 기본 절반")]
        [Range(0f, 1f)] public float PitchYawShare = 0.5f;

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
            if (Progress) { Progress.ReportPiloting(true); }
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
            if (Progress) { Progress.ReportPiloting(false); }
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
                }
                // 체력 = 강화 × 장갑판
                float hull = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_HULL) * Progress.GetConsumableFactor(ACO_PlayerProgress.CONS_ARMOR) : 1f;
                float mobility = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_MOBILITY) : 1f;
                float engine = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_ENGINE) : 1f;
                float fuel = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_FUEL) : 1f;
                float stability = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_STABILITY) : 1f;

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
            }

            // 기총 피해: 탄 파티클의 덤 피해 배수(충돌은 조종사 화면에서만 일어난다)
            if (GunBonuses != null)
            {
                float gun = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_GUN) : 1f;
                for (int i = 0; i < GunBonuses.Length; i++) { if (GunBonuses[i]) { GunBonuses[i].Factor = gun; } }
            }

            // 대응탄 포드: SAV 플레어 최대 수를 늘리고 채운다(내릴 때 원래 수로 채운다)
            if (Flares != null)
            {
                if (_baseFlares == null || _baseFlares.Length != Flares.Length)
                {
                    _baseFlares = new int[Flares.Length];
                    for (int i = 0; i < Flares.Length; i++) { _baseFlares[i] = Flares[i] ? Flares[i].FullFlares : 0; }
                }
                float cm = on ? Progress.GetConsumableFactor(ACO_PlayerProgress.CONS_CM) : 1f;
                for (int i = 0; i < Flares.Length; i++)
                {
                    DFUNC_Flares f = Flares[i];
                    if (!f) { continue; }
                    int want = Mathf.RoundToInt(_baseFlares[i] * cm);
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
                // 탄약 증량은 특수무장(대공·대지·폭탄 계열)만
                bool special = f == ACO_PlayerProgress.FAMILY_SP_AAM || f == ACO_PlayerProgress.FAMILY_SP_AGM || f == ACO_PlayerProgress.FAMILY_SP_BOMB;
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
