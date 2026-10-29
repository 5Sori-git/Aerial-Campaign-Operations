using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 플레이어 기체 확장(SaccEntity.ExtensionUdonBehaviours에 넣는다, 동기화 없음). 내 강화(ACO_PlayerProgress)를 탑승할 때 기체와 무장에
    /// 적용하고 내릴 때 원래대로 돌린다 — 기체는 여럿이 같이 쓰므로 조종사 화면에서만 바꾼다. 비행은 조종사(오너)가 계산하고, SAV 체력도
    /// 오너가 깎고 동기화하므로 조종사 값만 바꾸면 된다. 미사일 비행 성능은 발사 이벤트에 실려 가고 피해는 쏜 사람 값을 쓴다.
    ///
    /// - 체력: SaccAirVehicle.FullHealth(남은 체력은 같은 비율로), 기동: PitchStrength/YawStrength/RollStrength, 추력: ThrottleStrength(+애프터버너),
    ///   연료 효율: FuelConsumption ÷ 배수(+애프터버너 소모량), 안정성: Lift × 배수 + ExtraVelLift에 (배수 − 1)을 더함(내릴 때 뺀다).
    /// - 무장(Weapons, ACO_DFUNC_Missile): 무장 인스펙터에서 고른 계열(UpgradeFamily)의 강화로 ACO_DFUNC_Missile.ApplyUpgrades. None이면 그대로.
    ///   SAV 무장(DFUNC_Bomb 등)과 기관포는 강화하지 않는다.
    /// 또 진행도에 출격(탑승)과 격추(조종 중 폭발 — 추락 포함), 내림(안전지대 밖이면 격추로 센다 — 폭발로 내려진 경우는 빼고)을 알린다.
    /// Weapons/AirVehicle/Progress는 빌드 시 ACO_SceneBaker가 굽는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_VehicleUpgrades : UdonSharpBehaviour
    {
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다")]
        public ACO_PlayerProgress Progress;
        [Tooltip("이 기체의 SAV 비행 스크립트(빌드 시 자동)")]
        public SaccAirVehicle AirVehicle;
        [Tooltip("이 기체의 ACO 무장(빌드 시 자동 — 특수무장 모듈 포함)")]
        public ACO_DFUNC_Missile[] Weapons;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(내가/아군이 격추됨 대사)")]
        public ACO_Radio Radio;

        // SaccEntity가 주입한다.
        [System.NonSerialized] public SaccEntity EntityControl;

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
        private float _velLiftAdded;   // ExtraVelLift에 더해 둔 값(플랩도 같은 값을 더하고 빼므로 곱하지 않고 더한 만큼만 되돌린다)

        public void SFEXT_O_PilotEnter()
        {
            _pilot = true;
            _exploded = false;
            if (Progress) { Progress.ReportPiloting(true); }
            Apply(true);
        }

        public void SFEXT_O_PilotExit()
        {
            Apply(false);
            // 스스로 내렸으면(폭발이 아니면) 내린 자리를 알린다 — 안전지대 밖이면 격추로 센다.
            if (_pilot && !_exploded && Progress && EntityControl && !EntityControl.dead)
            {
                Progress.ReportPilotExit(EntityControl.transform.position);
            }
            _pilot = false;
            if (Progress) { Progress.ReportPiloting(false); }
        }

        /// 모든 클라이언트에서 오지만, 이 클라이언트가 조종 중일 때만 센다(폭발 뒤 SAV가 조종사를 내린다).
        public void SFEXT_G_Explode()
        {
            if (!_pilot)
            {
                // 다른 사람이 조종하던 기체가 터졌다 — 아군 격추 대사(이름 = 조종사)
                if (Radio && EntityControl && EntityControl.Occupied)
                {
                    VRCPlayerApi pilot = Networking.GetOwner(EntityControl.gameObject);
                    Radio.Report(ACO_Radio.EV_ALLY_DOWN, Utilities.IsValid(pilot) ? pilot.displayName : "");
                }
                return;
            }
            _exploded = true;
            if (Progress) { Progress.ReportShotDown(); }
            if (Radio) { Radio.Report(ACO_Radio.EV_SELF_DOWN, ""); }
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
                }
                float hull = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_HULL) : 1f;
                float mobility = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_MOBILITY) : 1f;
                float engine = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_ENGINE) : 1f;
                float fuel = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_FUEL) : 1f;
                float stability = on ? Progress.GetUpgradeFactor(ACO_PlayerProgress.UP_STABILITY) : 1f;

                float oldFull = AirVehicle.FullHealth;
                float newFull = _baseHealth * hull;
                if (oldFull > 0f) { AirVehicle.Health = AirVehicle.Health * (newFull / oldFull); }
                AirVehicle.FullHealth = newFull;

                AirVehicle.PitchStrength = _basePitch * mobility;
                AirVehicle.YawStrength = _baseYaw * mobility;
                AirVehicle.RollStrength = _baseRoll * mobility;

                AirVehicle.ThrottleStrength = _baseThrottle * engine;
                // SAV가 시작할 때 한 번 계산해 두는 값이라 같이 맞춘다.
                AirVehicle.ThrottleStrengthAB = AirVehicle.ThrottleStrength * (AirVehicle.AfterburnerThrustMulti - 1f);

                // 연료 효율: 소모량을 배수로 나눈다(5단계 x1.4 → 약 29% 덜 쓴다). 애프터버너 소모량도 SAV가 시작할 때 계산해 두므로 같이 맞춘다.
                AirVehicle.FuelConsumption = _baseFuelConsumption / Mathf.Max(fuel, 0.01f);
                AirVehicle.FuelConsumptionAB = AirVehicle.FuelConsumption * (AirVehicle.FuelConsumptionABMulti - 1f);

                // 안정성: 받음각 양력(Lift)을 키워 저속에서 쳐들어도 버티고, 속도 양력(VelLift — SAV가 매 프레임 VelLiftStart × ExtraVelLift로
                // 다시 계산한다)을 ExtraVelLift로 올려 기수가 덜 처진다. ExtraVelLift는 플랩이 더하고 빼는 값이라 더한 만큼만 되돌린다.
                AirVehicle.Lift = _baseLift * stability;
                float velLiftDelta = stability - 1f;
                AirVehicle.ExtraVelLift += velLiftDelta - _velLiftAdded;
                _velLiftAdded = velLiftDelta;
            }

            if (Weapons == null) { return; }
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
                w.ApplyUpgrades(
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_DAMAGE),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_AMMO),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_TURN),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_LOCK_SPEED),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_LOCK_RANGE),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_LOCK_ANGLE),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_BLAST),
                    Progress.GetFamilyFactor(f, ACO_PlayerProgress.STAT_RELOAD));
            }
        }
    }
}
