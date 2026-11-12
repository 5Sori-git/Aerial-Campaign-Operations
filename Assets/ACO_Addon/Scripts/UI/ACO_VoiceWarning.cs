using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 조종석 음성 경고(로컬 전용, 동기화 없음 — 씬에 하나). 내가 조종 중인 기체 기준으로 음성을 낸다.
    ///  - PULL UP: 지금 하강 속도로 지면까지 PullUpTime초 안, 또는 진행 방향 앞 지형까지 PullUpTime초 안.
    ///  - RAILGUN·LASER: 다른 스크립트가 상황이 이어지는 동안 Warn(종류)를 부른다 — 랜스 포대 사선 경고 안(ACO_Railgun),
    ///    소형 레일건이 나를 조준(ACO_MiniRailgun), 레이저 에이스가 나를 비춤(ACO_Borderless), LUNA 레일건이 나를 겨눔(ACO_LunaAce).
    ///  - ALTITUDE: 지면 위 AltitudeFloor m 아래로 내려가는 중.
    ///  - STALL: 속도가 StallSpeed 아래이거나 받음각이 한계(MaxAngleOfAttackPitch)의 StallAoaRatio 넘음.
    ///  - DAMAGE CRITICAL: 체력이 DamageRatio 아래로 떨어지는 순간 한 번(다시 오르면 다시 센다).
    ///  - FLARES EMPTY: 플레어가 0이 되는 순간 한 번 + 0인데 플레어를 쏘려 할 때(키·트리거).
    ///  - BINGO FUEL: 연료가 BingoRatio 아래로 떨어지는 순간 한 번.
    ///  - AMMUNITION ZERO: 탄이 다 떨어진 무장으로 바꿨을 때(기총·ACO 미사일·특수무장 모듈·SAV 미사일/폭탄).
    /// PULL UP·ALTITUDE·STALL은 기어를 내렸거나 지상(Taxiing)·안전지대 안에서 끈다(고도 둘은 저속에서도).
    /// 한 번에 한 음성 — 더 급한 경고(Priority 작을수록)가 덜 급한 음성을 끊는다. 이어지는 경고는 종류별 간격(Repeat)마다 다시,
    /// 한 번만 말하는 경고는 못 말했으면 PendingTime초 안에 자리가 나면 말한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_VoiceWarning : UdonSharpBehaviour
    {
        public const int W_PULLUP = 0;
        public const int W_RAILGUN = 1;
        public const int W_LASER = 2;
        public const int W_ALTITUDE = 3;
        public const int W_DAMAGE = 4;
        public const int W_STALL = 5;
        public const int W_FLARES = 6;
        public const int W_BINGO = 7;
        public const int W_AMMO = 8;
        public const int COUNT = 9;

        [Header("연결 (Bake)")]
        [Tooltip("내 기체·기어·플레어·무장(비행 HUD의 기체 목록을 같이 쓴다)")]
        public ACO_FlightHud FlightHud;
        public ACO_RadarContacts Contacts;
        [Tooltip("안전지대(항모 갑판 등) 안에선 고도·실속 경고를 끈다")]
        public ACO_PlayerProgress Progress;

        [Header("음성 (Tools/aco_make_voice.py — 순서: PULL UP, RAILGUN, LASER, ALTITUDE, DAMAGE, STALL, FLARES, BINGO, AMMO)")]
        public AudioSource Voice;
        public AudioClip[] Clips = new AudioClip[COUNT];
        [Range(0f, 1f)] public float Volume = 0.9f;
        [Tooltip("급한 정도(작을수록 급하다 — 급한 음성이 덜 급한 음성을 끊는다)")]
        public int[] Priority = new int[] { 0, 1, 1, 2, 1, 1, 2, 3, 2 };
        [Tooltip("이어지는 경고를 다시 말하는 간격(초)")]
        public float[] Repeat = new float[] { 2f, 4f, 5f, 4f, 0f, 3f, 1.5f, 0f, 0f };
        [Tooltip("다른 스크립트의 Warn이 이 시간(초) 넘게 끊기면 상황이 끝난 것으로 본다(다음 Warn에서 바로 말한다)")]
        public float WarnHold = 0.6f;
        [Tooltip("한 번만 말하는 경고가 다른 음성에 밀렸을 때 이 시간(초) 안에 자리가 나면 말한다")]
        public float PendingTime = 3f;

        [Header("고도")]
        public float PullUpTime = 3f;
        public float AltitudeFloor = 150f;
        public float AltitudeSinkRate = 2f;
        [Tooltip("이 속도(m/s) 아래면 고도 경고를 끈다(이착륙)")]
        public float MinSpeed = 60f;
        public LayerMask GroundLayers = 2049 | 16;

        [Header("실속·손상·연료")]
        [Tooltip("이 속도(m/s) 아래면 STALL")]
        public float StallSpeed = 55f;
        [Tooltip("받음각이 기체 한계의 이 비율을 넘으면 STALL")]
        public float StallAoaRatio = 0.9f;
        [Tooltip("체력이 이 비율 아래로 떨어지면 DAMAGE CRITICAL(이보다 0.05 위로 오르면 다시 센다)")]
        public float DamageRatio = 0.3f;
        [Tooltip("연료가 이 비율 아래로 떨어지면 BINGO FUEL(0.05 위로 오르면 다시 센다)")]
        public float BingoRatio = 0.2f;
        public float CheckInterval = 0.1f;

        private float[] _lastPlay = new float[COUNT];
        private float[] _lastWarn = new float[COUNT];
        private float[] _pendingUntil = new float[COUNT];
        private int _playing = -1;
        private float _checkTimer;
        private bool _started;

        // 내 기체 상태(바뀌면 다시 센다)
        private SaccEntity _vehicle;
        private bool _damageSaid;
        private bool _bingoSaid;
        private int _lastFlares = -1;
        private bool _flareHeld;
        private UdonSharpBehaviour _lastSelR;
        private UdonSharpBehaviour _lastSelL;
        private UdonSharpBehaviour _lastModR;
        private UdonSharpBehaviour _lastModL;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            for (int i = 0; i < COUNT; i++) { _lastPlay[i] = -100f; _lastWarn[i] = -100f; _pendingUntil[i] = -100f; }
        }

        /// 상황이 이어지는 동안 부른다(매 프레임이어도 된다) — 간격(Repeat)마다 다시 말한다.
        public void Warn(int kind)
        {
            if (!_started) { Start(); }
            if (kind < 0 || kind >= COUNT) { return; }
            float now = Time.time;
            bool fresh = now - _lastWarn[kind] > WarnHold;   // 끊겼다 다시 시작 — 간격과 상관없이 바로
            _lastWarn[kind] = now;
            if (fresh || now - _lastPlay[kind] >= Repeat[kind]) { Play(kind); }
        }

        /// 한 번만 말한다(밀리면 PendingTime 안에 자리가 나면).
        public void Announce(int kind)
        {
            if (!_started) { Start(); }
            if (kind < 0 || kind >= COUNT) { return; }
            if (!Play(kind)) { _pendingUntil[kind] = Time.time + PendingTime; }
        }

        void Update()
        {
            if (!_started) { Start(); }
            if (_playing >= 0 && Voice != null && !Voice.isPlaying) { _playing = -1; }
            if (_playing < 0) { PlayPending(); }

            int v = PilotedVehicle();
            if (v < 0)
            {
                if (_vehicle != null) { ResetVehicle(null); }
                return;
            }
            SaccEntity e = FlightHud.Vehicles[v];
            if (e != _vehicle) { ResetVehicle(e); }
            CheckFlareTrigger(v, e);     // 누르는 순간을 잡으려고 매 프레임
            CheckSelection(v, e);

            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) { return; }
            _checkTimer = CheckInterval;
            CheckFlight(v, e);
        }

        private int PilotedVehicle()
        {
            if (FlightHud == null || Contacts == null || Contacts.LocalVehicle == null || FlightHud.Vehicles == null) { return -1; }
            SaccEntity e = Contacts.LocalVehicle;
            if (!e.Piloting) { return -1; }
            for (int i = 0; i < FlightHud.Vehicles.Length; i++) { if (FlightHud.Vehicles[i] == e) { return i; } }
            return -1;
        }

        private void ResetVehicle(SaccEntity e)
        {
            _vehicle = e;
            _damageSaid = false;
            _bingoSaid = false;
            _lastFlares = -1;
            _flareHeld = true;   // 탈 때 누르고 있던 건 세지 않는다
            _lastSelR = null;
            _lastSelL = null;
            _lastModR = null;
            _lastModL = null;
            if (e == null) { return; }
            // 탈 때 고른 무장은 "바꿈"으로 세지 않는다
            _lastSelR = Sel(e.Dial_Functions_R, e.RStickSelection);
            _lastSelL = Sel(e.Dial_Functions_L, e.LStickSelection);
            int v = PilotedVehicle();
            _lastModR = v >= 0 ? ModuleOf(v, _lastSelR) : null;
            _lastModL = v >= 0 ? ModuleOf(v, _lastSelL) : null;
        }

        // ───────────── 비행(고도·실속·손상·연료) ─────────────

        private void CheckFlight(int v, SaccEntity e)
        {
            SaccAirVehicle air = FlightHud.Air != null && v < FlightHud.Air.Length ? FlightHud.Air[v] : null;
            if (air == null) { return; }

            // 손상·연료: 떨어지는 순간 한 번, 다시 오르면 다시 센다
            if (air.FullHealth > 0f)
            {
                float hp = air.Health / air.FullHealth;
                if (!_damageSaid && hp > 0f && hp < DamageRatio) { _damageSaid = true; Announce(W_DAMAGE); }
                else if (_damageSaid && hp > DamageRatio + 0.05f) { _damageSaid = false; }
            }
            if (air.FullFuel > 0f)
            {
                float fuel = air.Fuel / air.FullFuel;
                if (!_bingoSaid && fuel < BingoRatio) { _bingoSaid = true; Announce(W_BINGO); }
                else if (_bingoSaid && fuel > BingoRatio + 0.05f) { _bingoSaid = false; }
            }

            // 고도·실속은 공중에서만(기어 올림, 지상·안전지대 아님)
            if (air.Taxiing) { return; }
            DFUNC_Gear gear = FlightHud.Gears != null && v < FlightHud.Gears.Length ? FlightHud.Gears[v] : null;
            if (gear != null && !gear.GearUp) { return; }
            Vector3 pos = e.transform.position;
            if (Progress != null && Progress.SafeZones != null && Progress.SafeZones.Length > 0 && Progress.IsInSafeZone(pos)) { return; }

            if (air.AirSpeed < StallSpeed || (air.MaxAngleOfAttackPitch > 0f && air.AngleOfAttack > air.MaxAngleOfAttackPitch * StallAoaRatio)) { Warn(W_STALL); }

            if (air.AirSpeed < MinSpeed) { return; }
            Vector3 vel = air.CurrentVel;
            float speed = vel.magnitude;
            RaycastHit hit;
            float agl = 100000f;
            if (Physics.Raycast(pos, Vector3.down, out hit, 5000f, GroundLayers, QueryTriggerInteraction.Ignore)) { agl = hit.distance; }
            float sink = -vel.y;
            bool pull = sink > 5f && agl / sink < PullUpTime;
            if (!pull && speed > 1f && Physics.Raycast(pos, vel / speed, out hit, speed * PullUpTime, GroundLayers, QueryTriggerInteraction.Ignore)) { pull = true; }
            if (pull) { Warn(W_PULLUP); return; }
            if (agl < AltitudeFloor && sink > AltitudeSinkRate) { Warn(W_ALTITUDE); }
        }

        // ───────────── 플레어 ─────────────

        /// 플레어가 0이 되는 순간 한 번, 0인데 쏘려 하면(키·트리거를 누르는 순간) — SAV 플레어 기능과 같은 입력을 본다.
        private void CheckFlareTrigger(int v, SaccEntity e)
        {
            DFUNC_Flares f = FlightHud.FlareFuncs != null && v < FlightHud.FlareFuncs.Length ? FlightHud.FlareFuncs[v] : null;
            if (f == null) { return; }
            int n = f.NumFlares;
            if (_lastFlares > 0 && n <= 0) { Announce(W_FLARES); }
            _lastFlares = n;

            bool selected = Sel(e.Dial_Functions_R, e.RStickSelection) == (UdonSharpBehaviour)f || Sel(e.Dial_Functions_L, e.LStickSelection) == (UdonSharpBehaviour)f;
            float trig = 0f;
            if (selected) { trig = Input.GetAxisRaw(f.LeftDial ? "Oculus_CrossPlatform_PrimaryIndexTrigger" : "Oculus_CrossPlatform_SecondaryIndexTrigger"); }
            bool held = Input.GetKey(f.LaunchKey) || trig > 0.75f;
            if (held && !_flareHeld && n <= 0) { Warn(W_FLARES); }
            _flareHeld = held;
        }

        // ───────────── 무장 바꿈 → 탄 없음 ─────────────

        private void CheckSelection(int v, SaccEntity e)
        {
            UdonSharpBehaviour r = Sel(e.Dial_Functions_R, e.RStickSelection);
            UdonSharpBehaviour l = Sel(e.Dial_Functions_L, e.LStickSelection);
            UdonSharpBehaviour mr = ModuleOf(v, r);
            UdonSharpBehaviour ml = ModuleOf(v, l);
            if (r != _lastSelR || mr != _lastModR) { OnSelect(v, r, mr); }
            if (l != _lastSelL || ml != _lastModL) { OnSelect(v, l, ml); }
            _lastSelR = r;
            _lastSelL = l;
            _lastModR = mr;
            _lastModL = ml;
        }

        private void OnSelect(int v, UdonSharpBehaviour sel, UdonSharpBehaviour module)
        {
            if (sel == null) { return; }
            DFUNC_Flares f = FlightHud.FlareFuncs != null && v < FlightHud.FlareFuncs.Length ? FlightHud.FlareFuncs[v] : null;
            if (f != null && sel == (UdonSharpBehaviour)f)
            {
                if (f.NumFlares <= 0) { Announce(W_FLARES); }
                return;
            }
            if (IsEmpty(v, module != null ? module : sel)) { Announce(W_AMMO); }
        }

        /// 특수무장 슬롯이면 지금 모듈, 아니면 null.
        private UdonSharpBehaviour ModuleOf(int v, UdonSharpBehaviour sel)
        {
            if (sel == null || FlightHud.Specials == null || v >= FlightHud.Specials.Length) { return null; }
            ACO_DFUNC_Special s = FlightHud.Specials[v];
            if (s == null || sel != (UdonSharpBehaviour)s) { return null; }
            return s.GetModule();
        }

        /// 탄이 다 떨어졌는가(알 수 없는 기능이면 false).
        private bool IsEmpty(int v, UdonSharpBehaviour w)
        {
            if (w == null) { return false; }
            DFUNC_Gun gun = FlightHud.Guns != null && v < FlightHud.Guns.Length ? FlightHud.Guns[v] : null;
            if (gun != null && w == (UdonSharpBehaviour)gun) { return gun.GunAmmoInSeconds <= 0.01f; }
            string type = w.GetUdonTypeName();
            if (type == GetUdonTypeName<ACO_DFUNC_Missile>()) { return ((ACO_DFUNC_Missile)w).GetAmmo() <= 0; }
            if (type == GetUdonTypeName<DFUNC_AAM>()) { return (int)w.GetProgramVariable("NumAAM") <= 0; }
            if (type == GetUdonTypeName<DFUNC_AGM>()) { return (int)w.GetProgramVariable("NumAGM") <= 0; }
            if (type == GetUdonTypeName<DFUNC_Bomb>()) { return (int)w.GetProgramVariable("NumBomb") <= 0; }
            return false;
        }

        private UdonSharpBehaviour Sel(UdonSharpBehaviour[] dial, int index)
        {
            return dial != null && index >= 0 && index < dial.Length ? dial[index] : null;
        }

        // ───────────── 재생 ─────────────

        private void PlayPending()
        {
            float now = Time.time;
            int best = -1;
            for (int k = 0; k < COUNT; k++)
            {
                if (_pendingUntil[k] < now) { continue; }
                if (best < 0 || Priority[k] < Priority[best]) { best = k; }
            }
            if (best >= 0 && Play(best)) { _pendingUntil[best] = -100f; }
        }

        private bool Play(int kind)
        {
            if (Voice == null || Clips == null || kind >= Clips.Length || Clips[kind] == null) { return false; }
            // 더 급하거나 같은 급의 음성이 나오는 중이면 끊지 않는다
            if (_playing >= 0 && Voice.isPlaying && Priority[_playing] <= Priority[kind]) { return false; }
            Voice.Stop();
            Voice.clip = Clips[kind];
            Voice.volume = Volume;
            Voice.Play();
            _playing = kind;
            _lastPlay[kind] = Time.time;
            _pendingUntil[kind] = -100f;
            return true;
        }
    }
}
