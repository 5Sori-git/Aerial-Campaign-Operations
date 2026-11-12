using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 기체 자동화(SaccEntity.ExtensionUdonBehaviours에 넣는다, 동기화 없음 — 조종사 화면에서 SAV 기능의 네트워크 이벤트를 부른다).
    /// - 자동 캐노피(SAV DFUNC_Canopy): 조금이라도 움직이거나(속도 > CloseSpeed) 캐터펄트에 걸리면 닫고, 지상에서 멈춰(속도 < OpenSpeed)
    ///   OpenDelay초 지나면 연다. 열기는 "움직이다 멈췄을 때" 한 번만 — 멈춰 있는 동안 손으로 닫은 캐노피를 다시 열지 않는다.
    ///   닫기는 움직이는 동안 계속(손으로 열어도 다시 닫힌다, 명령 간격 CommandInterval).
    /// - 기어·후크 연동(SAV DFUNC_Gear + DFUNC_Hook): 기어를 내리면 후크도 내리고, 올리면 후크도 올린다(탑승할 때 기어 상태로 맞춘다).
    ///   지면(Taxiing)에 HookUpGroundTime초 이상 있으면 기어가 내려가 있어도 후크를 올린다(와이어에 걸려 있으면 멈춘 뒤부터 센다).
    ///   지면에서는 기어 연동으로 후크를 내리지 않는다 — 다음에 기어를 올렸다 내리면(이륙 후 착함 준비) 다시 따라 내려온다.
    /// - 리스폰 복구: 격추 뒤 다시 나타날 때(SFEXT_G_ReAppear + RespawnFixDelay) 기체 애니메이터의 폭발 상태가 끈 오브젝트(모델·좌석·콜라이더 —
    ///   셋업 메뉴가 컨트롤러의 explode 상태 클립에서 굽는다)를 처음 상태로 켜고 애니메이터를 다시 바인딩한다(남은 트리거·잘못 잡힌 기본값 정리).
    ///   그 뒤 기어·캐노피·후크 파라미터를 지금 상태로 다시 넣는다. 탑승할 때도 어긋나 있으면 한 번 더 한다(모든 클라이언트가 각자).
    /// - 지상 기어 잠금: 땅이나 갑판에 닿아 있는 동안(Taxiing) 기어 토글을 막아 기어가 올라가지 않는다. 떠오르면 풀린다.
    /// - 갑판 택싱 제한: 갑판(진행도 SafeZones) 위에서 DeckTaxiSpeed 아래로 움직이기 시작하면 수평 속도를 그 값에서 묶는다.
    ///   스로틀은 건드리지 않는다(놓은 자리가 그대로 남아 캐터펄트로 이어진다). 캐터펄트에 걸리면 풀리고,
    ///   그보다 빠르게 내린 기체(착함 실패 뒤 다시 올라갈 때)에는 걸리지 않는다. 비행장 활주로에는 걸리지 않는다.
    /// 캐노피·후크를 다이얼에서 빼서 확장 목록에 두면 다이얼 칸이 빈다(트리거로 조작은 못 하고 자동으로만 움직인다).
    /// 셋업: 메뉴 ACO → Player Vehicle → Add Auto Canopy + Gear/Hook Link to Selected Vehicles.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_VehicleAutomation : UdonSharpBehaviour
    {
        [Header("참조 (셋업 메뉴가 채운다)")]
        [Tooltip("SaccAirVehicle")]
        public UdonSharpBehaviour SAVControl;
        public DFUNC_Canopy Canopy;
        public DFUNC_Gear Gear;
        public DFUNC_Hook Hook;

        [Header("자동 캐노피")]
        public bool AutoCanopy = true;
        [Tooltip("이 속도(m/s)를 넘으면 닫는다")]
        public float CloseSpeed = 1.5f;
        [Tooltip("지상에서 이 속도(m/s) 아래로 OpenDelay초 머물면 연다")]
        public float OpenSpeed = 0.5f;
        public float OpenDelay = 2f;
        [Tooltip("같은 명령(닫기)을 다시 보내기까지 최소 간격(초) — 캐노피 애니메이션 시간보다 길게")]
        public float CommandInterval = 2.2f;

        [Header("기어·후크 연동")]
        [Tooltip("기어를 내리면 후크도 내리고, 올리면 후크도 올린다")]
        public bool LinkHookToGear = true;
        [Tooltip("지면에 이 시간(초) 이상 있으면 후크를 자동으로 올린다(0 = 끔). 와이어에 걸려 있으면 거의 멈춘 뒤부터 센다")]
        public float HookUpGroundTime = 3f;

        [Header("지상 안전")]
        [Tooltip("땅이나 갑판에 닿아 있는 동안 기어를 올리지 못하게 한다")]
        public bool LockGearOnGround = true;
        [Tooltip("갑판 위 택싱 속도를 제한한다(이륙은 캐터펄트로만)")]
        public bool LimitDeckTaxi = true;
        [Tooltip("갑판 택싱 최고 속도(m/s). 10 = 약 19kt")]
        public float DeckTaxiSpeed = 10f;
        [Tooltip("갑판 영역(SafeZones)을 읽는 진행도 — Bake가 채운다")]
        public ACO_PlayerProgress Progress;

        [Header("리스폰 복구 (셋업 메뉴가 굽는다)")]
        [Tooltip("기체 루트의 애니메이터")]
        public Animator VehicleAnimator;
        [Tooltip("폭발 애니메이션이 끄는 오브젝트")]
        public GameObject[] RespawnRestoreObjects;
        [Tooltip("그 오브젝트의 처음 켜짐 상태(같은 순서)")]
        public bool[] RespawnRestoreActive;
        [Tooltip("복구할 때 애니메이터를 다시 바인딩한다")]
        public bool RebindOnRespawn = true;
        [Tooltip("다시 나타난 뒤 복구까지 기다리는 시간(초)")]
        public float RespawnFixDelay = 0.3f;

        [System.NonSerialized] public SaccEntity EntityControl;

        private bool _piloting;
        private bool _onCatapult;
        private bool _moving;
        private float _stillSince = -1f;
        private float _lastClose = -100f;
        private float _tick;
        private float _groundTime;
        private bool _gearLocked;
        private bool _deckLimited;
        private Rigidbody _body;

        // ───────────── SAV 이벤트 ─────────────

        public void SFEXT_G_ReAppear() { SendCustomEventDelayedSeconds(nameof(_RespawnFix), RespawnFixDelay); }

        public void _RespawnFix()
        {
            if (EntityControl && EntityControl.wrecked) { return; }
            RestoreAndRebind();
        }

        // 탑승할 때(모든 클라이언트) 아직 어긋나 있으면 한 번 더
        public void SFEXT_G_PilotEnter()
        {
            if (NeedsRestore()) { RestoreAndRebind(); }
        }

        public void SFEXT_O_PilotEnter()
        {
            _piloting = true;
            _moving = _onCatapult || Speed() > CloseSpeed;
            _stillSince = -1f;
            // 탑승 때는 후크를 내리지 않는다(막 옮겨진 기체는 접지 판정 전이라 지상인데도 내려갈 수 있다) — 기어가 올라가 있을 때만 올린다
            if (LinkHookToGear && Hook && Gear && Gear.GearUp) { SetHook(false); }
        }

        public void SFEXT_O_PilotExit()
        {
            _piloting = false;
            ClearGroundSafety();
        }

        public void SFEXT_G_CatapultLockIn() { _onCatapult = true; }
        public void SFEXT_G_CatapultLockOff() { _onCatapult = false; }
        public void SFEXT_G_Explode() { _onCatapult = false; ClearGroundSafety(); }
        public void SFEXT_G_RespawnButton() { _onCatapult = false; ClearGroundSafety(); }

        // 기어 함수가 모든 클라이언트에서 보낸다 — 조종사 화면에서만 후크를 맞춘다
        public void SFEXT_G_GearDown() { if (_piloting) { SyncHook(); } }
        public void SFEXT_G_GearUp() { if (_piloting) { SyncHook(); } }

        // ───────────── 리스폰 복구 ─────────────

        private bool NeedsRestore()
        {
            if (RespawnRestoreObjects == null || RespawnRestoreActive == null) { return false; }
            int n = Mathf.Min(RespawnRestoreObjects.Length, RespawnRestoreActive.Length);
            // 켜져 있어야 할 것(모델·좌석 등)이 꺼졌을 때만 — 처음부터 꺼져 있던 이펙트가 켜진 정도로는 다시 바인딩하지 않는다
            // (첫 탑승 때 다시 바인딩하면 애니메이터가 기본 상태로 돌아가 기어가 올라갔다 내려온다)
            for (int i = 0; i < n; i++)
            {
                GameObject g = RespawnRestoreObjects[i];
                if (g && RespawnRestoreActive[i] && !g.activeSelf) { return true; }
            }
            return false;
        }

        private void RestoreAndRebind()
        {
            if (RespawnRestoreObjects != null && RespawnRestoreActive != null)
            {
                int n = Mathf.Min(RespawnRestoreObjects.Length, RespawnRestoreActive.Length);
                for (int i = 0; i < n; i++)
                {
                    GameObject g = RespawnRestoreObjects[i];
                    if (g && RespawnRestoreActive[i] && !g.activeSelf) { g.SetActive(true); }
                }
            }
            if (!RebindOnRespawn || !VehicleAnimator) { return; }
            // 오브젝트를 켠 뒤 다시 바인딩해야 기본값(Write Defaults)이 '켜짐'으로 잡힌다
            VehicleAnimator.Rebind();
            if (Gear && Gear.GearAnimator)
            {
                Gear.GearAnimator.SetBool("gearup", Gear.GearUp);
                // 다시 바인딩하면 기어 레이어가 기본 상태에서 시작한다 — 내려가 있어야 하면 애니메이션 없이 바로(SAV 리스폰과 같은 트리거)
                if (!Gear.GearUp) { Gear.GearAnimator.SetTrigger("instantgeardown"); }
            }
            if (Canopy && Canopy.CanopyAnimator) { Canopy.CanopyAnimator.SetBool(Canopy.AnimCanopyBool, Canopy.CanopyOpen); }
            if (Hook) { VehicleAnimator.SetBool("hookdown", Hook.HookDown); }
            if (EntityControl) { VehicleAnimator.SetBool("occupied", EntityControl.Occupied); }
        }

        // ───────────── 자동 캐노피 ─────────────

        private void Update()
        {
            if (!_piloting || !SAVControl) { return; }
            _tick -= Time.deltaTime;
            if (_tick > 0f) { return; }
            _tick = 0.2f;
            float spd = Speed();
            bool grounded = (bool)SAVControl.GetProgramVariable("Taxiing");
            UpdateHookGround(grounded, spd, 0.2f);
            SetGearLock(LockGearOnGround && grounded && Gear && !Gear.GearUp);
            UpdateDeckLimit(grounded, spd);
            if (AutoCanopy && Canopy && !Canopy.CanopyBroken) { UpdateCanopy(grounded, spd); }
        }

        private void FixedUpdate()
        {
            if (!_deckLimited) { return; }
            if (_body == null) { _body = EntityControl ? EntityControl.VehicleRigidbody : null; }
            if (_body == null) { return; }
            Vector3 v = _body.velocity;
            float h = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            if (h <= DeckTaxiSpeed) { return; }
            float k = DeckTaxiSpeed / h;
            v.x *= k;
            v.z *= k;
            _body.velocity = v;
        }

        /// 갑판에서 제한 속도 아래일 때만 건다 — 착함에 실패해 빠르게 구르는 기체는 스로틀을 올려 다시 떠야 한다.
        private void UpdateDeckLimit(bool grounded, float spd)
        {
            if (!LimitDeckTaxi || !grounded || _onCatapult || !OnDeck())
            {
                _deckLimited = false;
                return;
            }
            if (spd <= DeckTaxiSpeed) { _deckLimited = true; }
        }

        // 영역이 없으면 갑판이 아니다(IsInSafeZone은 빈 목록을 어디서나 참으로 본다 — 활주로까지 묶이면 안 된다)
        private bool OnDeck()
        {
            return Progress != null && Progress.SafeZones != null && Progress.SafeZones.Length > 0
                && EntityControl && Progress.IsInSafeZone(EntityControl.transform.position);
        }

        private void SetGearLock(bool on)
        {
            if (on == _gearLocked || !Gear) { return; }
            _gearLocked = on;
            Gear.SetProgramVariable("DisableGearToggle", (int)Gear.GetProgramVariable("DisableGearToggle") + (on ? 1 : -1));
        }

        private void ClearGroundSafety()
        {
            _deckLimited = false;
            SetGearLock(false);
        }

        /// 지면에 오래 있으면 후크를 올린다.
        private void UpdateHookGround(bool grounded, float spd, float dt)
        {
            if (HookUpGroundTime <= 0f || !Hook || !Hook.HookDown || !grounded || (Hook.Hooked && spd > OpenSpeed))
            {
                _groundTime = 0f;
                return;
            }
            _groundTime += dt;
            if (_groundTime >= HookUpGroundTime)
            {
                _groundTime = 0f;
                SetHook(false);
            }
        }

        private void UpdateCanopy(bool grounded, float spd)
        {
            float now = Time.time;
            if (_onCatapult || spd > CloseSpeed)
            {
                _moving = true;
                _stillSince = -1f;
                if (Canopy.CanopyOpen && now - _lastClose > CommandInterval)
                {
                    _lastClose = now;
                    Canopy.SendCustomNetworkEvent(NetworkEventTarget.All, "CanopyClosing");
                }
                return;
            }
            if (spd < OpenSpeed && grounded)
            {
                if (_stillSince < 0f) { _stillSince = now; }
                if (_moving && now - _stillSince >= OpenDelay)
                {
                    _moving = false;
                    if (!Canopy.CanopyOpen) { Canopy.SendCustomNetworkEvent(NetworkEventTarget.All, "CanopyOpening"); }
                }
            }
            else { _stillSince = -1f; }
        }

        private float Speed()
        {
            return SAVControl ? (float)SAVControl.GetProgramVariable("Speed") : 0f;
        }

        // ───────────── 기어·후크 ─────────────

        /// 후크를 기어 상태에 맞춘다(SAV DFUNC_Hook.ToggleHook과 같은 순서 — 방향만 정해서).
        private void SyncHook()
        {
            if (!LinkHookToGear || !Hook || !Gear) { return; }
            bool down = !Gear.GearUp;
            // 지면에서는 내리지 않는다(지면에 오래 있으면 올리는 것과 맞부딪히지 않게)
            if (down && SAVControl && (bool)SAVControl.GetProgramVariable("Taxiing")) { return; }
            SetHook(down);
        }

        private void SetHook(bool down)
        {
            if (Hook.HookDown == down) { return; }
            if (down)
            {
                // 케이블을 잡는 검사는 후크 오브젝트가 켜져 있어야 돈다
                Hook.gameObject.SetActive(true);
                SetHookDial(true);
                Hook.SendCustomNetworkEvent(NetworkEventTarget.All, "SetHookDown");
            }
            else
            {
                if (SAVControl && !(bool)SAVControl.GetProgramVariable("InVR")) { Hook.gameObject.SetActive(false); }
                SetHookDial(false);
                Hook.SendCustomNetworkEvent(NetworkEventTarget.All, "SetHookUp");
            }
        }

        private void SetHookDial(bool on)
        {
            if (Hook.Dial_Funcon) { Hook.Dial_Funcon.SetActive(on); }
            if (Hook.Dial_Funcon_Array == null) { return; }
            for (int i = 0; i < Hook.Dial_Funcon_Array.Length; i++) { if (Hook.Dial_Funcon_Array[i]) { Hook.Dial_Funcon_Array[i].SetActive(on); } }
        }
    }
}
