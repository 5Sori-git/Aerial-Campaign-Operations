using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// SAV DFUNC_Catapult 대체(붙는 방식·동기화·사출 물리는 그대로 복사). 다른 점:
    ///  - 풀스로틀 사출: 캐터펄트에 걸린 채 스로틀을 FullThrottleThreshold(0.95) 이상으로 FullThrottleHoldTime(초) 동안 유지하면 사출한다.
    ///    중간에 스로틀을 내리면 다시 센다. SAV는 ThrottleInput == 1(정확히)만 봐서 키보드·VR 스로틀이 1에 못 닿으면 안 나갔다.
    ///  - 트리거 사출도 함께(AllowTriggerLaunch — 다이얼에서 선택 후 트리거, 데스크톱 키). SAV의 AutoLaunch는 트리거를 끈다.
    ///  - 표시: 걸려 있는 동안 AttachedObjects, 풀스로틀을 유지하는 동안 HoldObjects를 켜고, HoldFill의 X 스케일 = 진행도(0 → 1).
    /// 위치(이 오브젝트 = 기체에서 캐터펄트에 물리는 지점)·BoxCollider(트리거)·레이어는 SAV와 같다. 기체의 DFUNC_Catapult를 이걸로 바꾸려면
    /// 메뉴 ACO → Player Vehicle → Replace Catapult on Selected Vehicles(값 복사 + 다이얼·확장 목록 교체).
    [DefaultExecutionOrder(10000)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_DFUNC_Catapult : UdonSharpBehaviour
    {
        [Header("이 오브젝트의 위치 = 기체에서 캐터펄트에 물리는 지점")]
        public UdonSharpBehaviour SAVControl;
        [Tooltip("기능이 켜졌을 때 켤 오브젝트(MFD)")]
        public GameObject Dial_Funcon;
        public GameObject[] Dial_Funcon_Array;
        [Tooltip("캐터펄트에 걸릴 때 한 번 재생")]
        public AudioSource CatapultLock;
        [Tooltip("걸릴 때 기체와 캐터펄트 방향 차이 허용(도)")]
        public float MaxAttachAngle = 15;
        [Tooltip("캐터펄트 트리거 레이어")]
        public int CatapultLayer = 24;
        [Tooltip("착륙 장치 기능(걸려 있는 동안 기어 토글을 막는다)")]
        public UdonSharpBehaviour GearFunc;
        public string AnimTriggerLaunchName = "catapultlaunch";
        [Tooltip("걸릴 때 기체를 캐터펄트에 맞춰 정렬")]
        public bool AlignToCatapult = true;

        [Header("풀스로틀 사출")]
        [Tooltip("끄면 트리거로만 사출")]
        public bool FullThrottleLaunch = true;
        [Tooltip("이 값 이상이면 풀스로틀로 본다(0~1) — 키보드·VR 스로틀이 정확히 1에 못 닿아도 되게")]
        [Range(0.5f, 1f)] public float FullThrottleThreshold = 0.95f;
        [Tooltip("풀스로틀을 이 시간(초) 유지하면 사출. 동기화 여유로 1초 이상 권장")]
        public float FullThrottleHoldTime = 2f;
        [Tooltip("다이얼에서 선택 후 트리거(데스크톱 키)로도 사출")]
        public bool AllowTriggerLaunch = true;

        [Header("표시 (선택, 조종사 화면)")]
        [Tooltip("캐터펄트에 걸려 있는 동안 켠다(HUD 안내 등)")]
        public GameObject[] AttachedObjects;
        [Tooltip("풀스로틀을 유지하는 동안 켠다")]
        public GameObject[] HoldObjects;
        [Tooltip("풀스로틀 유지 진행도 막대 — X 스케일이 0 → 원래 크기(피벗은 왼쪽 끝)")]
        public Transform HoldFill;

        [System.NonSerializedAttribute] public bool LeftDial = false;
        [System.NonSerializedAttribute] public int DialPosition = -999;
        [System.NonSerializedAttribute] public SaccEntity EntityControl;
        [System.NonSerializedAttribute] public bool OnCatapult;
        [System.NonSerializedAttribute] public bool Launching = false;
        [System.NonSerializedAttribute] public Transform CatapultTransform;

        private bool TriggerLastFrame;
        private bool Selected;
        private bool Piloting = false;
        private Transform VehicleTransform;
        private Rigidbody VehicleRigidbody;
        private Animator VehicleAnimator;
        private Vector3 PlaneCatapultOffset;
        private Quaternion PlaneCatapultRotDif;
        private Quaternion CatapultRotLastFrame;
        private Vector3 CatapultPosLastFrame;
        private Animator CatapultAnimator;
        private BoxCollider thisCollider;
        // 엔진 쪽 값에 한 번만 더하고 빼도록
        private bool DisableTaxiRotation = false;
        private bool DisableGearToggle = false;
        private bool DisablePhysicsApplication = false;
        private bool IsOwner;
        private float HoldStart = -1f;
        private float HoldFillScale = 1f;
        private bool enabledToFindAnimator;

        public void SFEXT_L_EntityStart()
        {
            if (Dial_Funcon) { Dial_Funcon.SetActive(false); }
            if (Dial_Funcon_Array != null) { for (int i = 0; i < Dial_Funcon_Array.Length; i++) { if (Dial_Funcon_Array[i]) { Dial_Funcon_Array[i].SetActive(false); } } }
            VehicleTransform = EntityControl.transform;
            VehicleRigidbody = EntityControl.GetComponent<Rigidbody>();
            VehicleAnimator = EntityControl.GetComponent<Animator>();
            thisCollider = GetComponent<BoxCollider>();
            IsOwner = EntityControl.IsOwner;
            if (HoldFill) { HoldFillScale = HoldFill.localScale.x; }
            colliderSmall();
            ShowAttached(false);
            ShowHold(false, 0f);
        }

        // 오너가 아니면 위치 동기화가 어긋나도 캐터펄트를 찾도록 콜라이더를 키운다
        void colliderLarge()
        {
            if (!thisCollider) { return; }
            Vector3 colSize = thisCollider.size;
            colSize.x = 8;
            colSize.z = 40;
            thisCollider.size = colSize;
        }

        void colliderSmall()
        {
            if (!thisCollider) { return; }
            Vector3 colSize = thisCollider.size;
            colSize.x = 0;
            colSize.z = 0;
            thisCollider.size = colSize;
        }

        public void DFUNC_Selected()
        {
            TriggerLastFrame = true;
            Selected = true;
        }

        public void DFUNC_Deselected()
        {
            Selected = false;
        }

        public void SFEXT_O_PilotEnter()
        {
            gameObject.SetActive(true);
            enabledToFindAnimator = false;
            Piloting = true;
            DisableOverrides();
        }

        public void SFEXT_G_PilotExit()
        {
            if (OnCatapult) { CatapultLockOff(); }
        }

        public void SFEXT_O_PilotExit()
        {
            if (!Launching)
            {
                gameObject.SetActive(false);
                Piloting = false;
            }
            Selected = false;
            DisableOverrides();
            ShowAttached(false);
            ShowHold(false, 0f);
        }

        public void SFEXT_L_PassengerEnter()
        {
            if (Dial_Funcon) { Dial_Funcon.SetActive(OnCatapult); }
            if (Dial_Funcon_Array != null) { for (int i = 0; i < Dial_Funcon_Array.Length; i++) { if (Dial_Funcon_Array[i]) { Dial_Funcon_Array[i].SetActive(OnCatapult); } } }
        }

        public void SFEXT_O_TakeOwnership()
        {
            IsOwner = true;
            colliderSmall();
        }

        public void SFEXT_O_LoseOwnership()
        {
            IsOwner = false;
            Launching = false;
            OnCatapult = false;
            Piloting = false;
            gameObject.SetActive(false);
            DisableOverrides();
        }

        public void SFEXT_O_Explode()
        {
            OnCatapult = false;
            DisableOverrides();
            ShowAttached(false);
            ShowHold(false, 0f);
        }

        public void SFEXT_G_RespawnButton()
        {
            if (Dial_Funcon) { Dial_Funcon.SetActive(false); }
            if (Dial_Funcon_Array != null) { for (int i = 0; i < Dial_Funcon_Array.Length; i++) { if (Dial_Funcon_Array[i]) { Dial_Funcon_Array[i].SetActive(false); } } }
        }

        private void EnableToFindAnimator()
        {
            if (!IsOwner)
            {
                enabledToFindAnimator = true;
                colliderLarge();
                gameObject.SetActive(true);
                SendCustomEventDelayedSeconds(nameof(FindAnimator_Disable), 3f);
            }
        }

        public void FindAnimator_Disable()
        {
            if (enabledToFindAnimator)
            {
                colliderSmall();
                enabledToFindAnimator = false;
                gameObject.SetActive(false);
            }
        }

        private bool FindCatapultAnimator(GameObject other)
        {
            if (OnCatapult && CatapultAnimator) { return true; }
            GameObject catapultObjects = other.gameObject;
            CatapultAnimator = other.GetComponent<Animator>();
            while (CatapultAnimator == null && catapultObjects.transform.parent)
            {
                catapultObjects = catapultObjects.transform.parent.gameObject;
                CatapultAnimator = catapultObjects.GetComponent<Animator>();
            }
            return CatapultAnimator != null;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (EntityControl._dead) { return; }
            if (Piloting)
            {
                if (OnCatapult || !other || other.gameObject.layer != CatapultLayer || !FindCatapultAnimator(other.gameObject)) { return; }
                CatapultTransform = other.transform;
                // 기체가 캐터펄트 방향을 보고 있어야 건다
                if (Vector3.Angle(VehicleTransform.forward, CatapultTransform.forward) >= MaxAttachAngle) { return; }
                OnCatapult = true;
                HoldStart = -1f;
                CatapultPosLastFrame = CatapultTransform.position;
                // 캐터펄트에 묶는다(캐터펄트 방향이 어떻든). 피치는 기체 그대로(앞뒤 바퀴 길이가 다른 기체가 있다)
                if (AlignToCatapult)
                {
                    Quaternion newrotation = Quaternion.Euler(new Vector3(VehicleTransform.rotation.eulerAngles.x, CatapultTransform.rotation.eulerAngles.y, CatapultTransform.rotation.eulerAngles.z));
                    if (Quaternion.Dot(VehicleTransform.rotation, newrotation) < 0)
                    {
                        // 원격 클라이언트 보간이 꼬이지 않게 쿼터니언 부호를 맞춘다
                        newrotation = newrotation * Quaternion.Euler(0, 360, 0);
                    }
                    VehicleTransform.rotation = newrotation;
                    // 캐터펄트 '위'로(캐터펄트 기준 y 성분만 남기고) 옮긴 뒤, 물리는 지점이 캐터펄트에 오도록 뒤로
                    float upDistance = CatapultTransform.InverseTransformDirection(CatapultTransform.position - VehicleTransform.position).y;
                    VehicleTransform.position = CatapultTransform.position - (CatapultTransform.up * upDistance);
                    float backDistance = VehicleTransform.InverseTransformDirection(VehicleTransform.position - transform.position).z;
                    VehicleTransform.position += CatapultTransform.forward * backDistance;
                    VehicleRigidbody.position = VehicleTransform.position;
                    VehicleRigidbody.rotation = VehicleTransform.rotation;
                }
                PlaneCatapultOffset = CatapultTransform.InverseTransformDirection(VehicleTransform.position - CatapultTransform.position);
                PlaneCatapultRotDif = VehicleTransform.rotation * Quaternion.Inverse(CatapultTransform.rotation);

                if (!DisableGearToggle && GearFunc)
                {
                    GearFunc.SetProgramVariable("DisableGearToggle", (int)GearFunc.GetProgramVariable("DisableGearToggle") + 1);
                    DisableGearToggle = true;
                }
                if (!DisableTaxiRotation)
                {
                    SAVControl.SetProgramVariable("DisableTaxiRotation", (int)SAVControl.GetProgramVariable("DisableTaxiRotation") + 1);
                    DisableTaxiRotation = true;
                }
                if (!DisablePhysicsApplication)
                {
                    SAVControl.SetProgramVariable("DisablePhysicsApplication", (int)SAVControl.GetProgramVariable("DisablePhysicsApplication") + 1);
                    DisablePhysicsApplication = true;
                }
                // 갑자기 멈춰 G 피해를 받지 않게 잠깐 무적
                EntityControl.invincible = true;
                SendCustomEventDelayedSeconds(nameof(deadfalse), (float)SAVControl.GetProgramVariable("GsAveragingTime") * 2f);
                ShowAttached(true);
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(CatapultLockIn));
            }
            else if (other && FindCatapultAnimator(other.gameObject))
            {
                // EnableToFindAnimator로 잠깐 켰을 때만 온다
                FindAnimator_Disable();
            }
        }

        private void Update()
        {
            if (!((Piloting && OnCatapult) || Launching)) { return; }

            if (!Launching)
            {
                // 풀스로틀 유지 사출
                if (FullThrottleLaunch && SAVControl)
                {
                    float throttle = (float)SAVControl.GetProgramVariable("ThrottleInput");
                    if (throttle >= FullThrottleThreshold)
                    {
                        if (HoldStart < 0f) { HoldStart = Time.time; }
                        float t = FullThrottleHoldTime > 0f ? (Time.time - HoldStart) / FullThrottleHoldTime : 1f;
                        ShowHold(true, t);
                        if (t >= 1f) { CatapultLaunchNow(); }
                    }
                    else if (HoldStart >= 0f)
                    {
                        HoldStart = -1f;
                        ShowHold(false, 0f);
                    }
                }
                // 트리거 사출
                if (!Launching && AllowTriggerLaunch && Selected)
                {
                    float trigger = LeftDial ? Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger") : Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger");
                    if (trigger > 0.75f)
                    {
                        if (!TriggerLastFrame) { CatapultLaunchNow(); }
                        TriggerLastFrame = true;
                    }
                    else { TriggerLastFrame = false; }
                }
            }

            VehicleTransform.rotation = CatapultTransform.rotation * PlaneCatapultRotDif;
            VehicleTransform.position = CatapultTransform.position + CatapultTransform.TransformDirection(PlaneCatapultOffset);
            VehicleRigidbody.position = VehicleTransform.position;
            VehicleRigidbody.rotation = VehicleTransform.rotation;
            VehicleRigidbody.velocity = Vector3.zero;
            VehicleRigidbody.angularVelocity = Vector3.zero;
            Quaternion catapultRotDif = CatapultTransform.rotation * Quaternion.Inverse(CatapultRotLastFrame);
            if (Launching && !CatapultTransform.gameObject.activeInHierarchy)
            {
                // 캐터펄트 애니메이션이 끝났다 — 기체를 던진다
                float deltaTime = Time.deltaTime;
                Launching = false;
                DisableOverrides();
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(CatapultLockOff));
                SAVControl.SetProgramVariable("Taxiinglerper", 0f);
                Vector3 launchVel = (CatapultTransform.position - CatapultPosLastFrame) / deltaTime;
                // 월드 제작자가 캐터펄트 애니메이터 이름에 "speed=숫자"로 사출 속도를 정할 수 있다(SAV와 같다)
                if (CatapultAnimator)
                {
                    string catName = CatapultAnimator.gameObject.name;
                    if (catName.Contains("speed="))
                    {
                        string[] splitCat = catName.Split("=");
                        float launchSpeed;
                        if (splitCat.Length > 1 && float.TryParse(splitCat[1], out launchSpeed)) { launchVel = launchVel.normalized * launchSpeed; }
                    }
                }
                VehicleRigidbody.velocity = launchVel;
                Vector3 rotEuler = catapultRotDif.eulerAngles;
                // 0~360 → -180~180
                if (rotEuler.x > 180) { rotEuler.x -= 360; }
                if (rotEuler.y > 180) { rotEuler.y -= 360; }
                if (rotEuler.z > 180) { rotEuler.z -= 360; }
                VehicleRigidbody.angularVelocity = (rotEuler * Mathf.Deg2Rad) / deltaTime;
                EntityControl.SetInvincibleFor((float)SAVControl.GetProgramVariable("GsAveragingTime") * 2f);
            }
            CatapultRotLastFrame = CatapultTransform.rotation;
            CatapultPosLastFrame = CatapultTransform.position;
        }

        private void DisableOverrides()
        {
            if (DisableGearToggle && GearFunc)
            {
                GearFunc.SetProgramVariable("DisableGearToggle", (int)GearFunc.GetProgramVariable("DisableGearToggle") - 1);
                DisableGearToggle = false;
            }
            if (DisableTaxiRotation)
            {
                SAVControl.SetProgramVariable("DisableTaxiRotation", (int)SAVControl.GetProgramVariable("DisableTaxiRotation") - 1);
                DisableTaxiRotation = false;
            }
            if (DisablePhysicsApplication)
            {
                SAVControl.SetProgramVariable("DisablePhysicsApplication", (int)SAVControl.GetProgramVariable("DisablePhysicsApplication") - 1);
                DisablePhysicsApplication = false;
            }
        }

        public void deadfalse()
        {
            EntityControl.invincible = false;
            if (!Piloting) { SFEXT_O_PilotExit(); }
        }

        public void KeyboardInput()
        {
            if (AllowTriggerLaunch) { CatapultLaunchNow(); }
        }

        public void CatapultLaunchNow()
        {
            if (IsOwner && OnCatapult && !Launching)
            {
                Launching = true;
                HoldStart = -1f;
                ShowHold(false, 0f);
                ShowAttached(false);
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(LaunchCatapult));
                EntityControl.SendEventToExtensions("SFEXT_O_LaunchFromCatapult");
            }
        }

        public void LaunchCatapult()
        {
            if (VehicleAnimator) { VehicleAnimator.SetTrigger(AnimTriggerLaunchName); }
            if (Utilities.IsValid(CatapultAnimator)) { CatapultAnimator.SetTrigger("launch"); }
            CatapultAnimator = null;
            if (Dial_Funcon) { Dial_Funcon.SetActive(false); }
            if (Dial_Funcon_Array != null) { for (int i = 0; i < Dial_Funcon_Array.Length; i++) { if (Dial_Funcon_Array[i]) { Dial_Funcon_Array[i].SetActive(false); } } }
            EntityControl.SendEventToExtensions("SFEXT_G_LaunchFromCatapult");
        }

        public void CatapultLockIn()
        {
            if (!IsOwner)
            {
                EnableToFindAnimator();
                OnCatapult = true;
            }
            if (VehicleAnimator) { VehicleAnimator.SetBool("oncatapult", true); }
            if (CatapultLock) { CatapultLock.Play(); }
            if (Dial_Funcon) { Dial_Funcon.SetActive(true); }
            if (Dial_Funcon_Array != null) { for (int i = 0; i < Dial_Funcon_Array.Length; i++) { if (Dial_Funcon_Array[i]) { Dial_Funcon_Array[i].SetActive(true); } } }
            EntityControl.ShouldTeleport = true;
            EntityControl.SendEventToExtensions("SFEXT_G_CatapultLockIn");
        }

        public void CatapultLockOff()
        {
            OnCatapult = false;
            if (VehicleAnimator) { VehicleAnimator.SetBool("oncatapult", false); }
            ShowAttached(false);
            ShowHold(false, 0f);
            EntityControl.SendEventToExtensions("SFEXT_G_CatapultLockOff");
        }

        // ───────────── 표시 ─────────────

        private void ShowAttached(bool on)
        {
            if (AttachedObjects == null) { return; }
            for (int i = 0; i < AttachedObjects.Length; i++)
            {
                if (AttachedObjects[i] && AttachedObjects[i].activeSelf != on) { AttachedObjects[i].SetActive(on); }
            }
        }

        private void ShowHold(bool on, float t)
        {
            if (HoldObjects != null)
            {
                for (int i = 0; i < HoldObjects.Length; i++)
                {
                    if (HoldObjects[i] && HoldObjects[i].activeSelf != on) { HoldObjects[i].SetActive(on); }
                }
            }
            if (HoldFill)
            {
                Vector3 s = HoldFill.localScale;
                s.x = HoldFillScale * Mathf.Clamp01(t);
                HoldFill.localScale = s;
            }
        }
    }
}
