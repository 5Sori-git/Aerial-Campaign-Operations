using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 특수무장 슬롯(다이얼 기능). SaccEntity의 Dial_Functions_L/R 한 칸(예: R3)에 고정으로 넣고, 실제 무장은 모듈로 바꿔 끼운다.
    /// 다이얼 배열은 건드리지 않는다 — SaccEntity가 이 슬롯에 보내는 이벤트(선택/탑승/폭발/보급 등)를 지금 장착된 모듈에 넘긴다.
    ///
    /// 장착: 플레이어별. 조종석에 앉으면 격납고(ACO_Loadout)에서 고른 무장 ID로 모듈을 고르고, 모든 클라이언트에 알린다(NetEquip).
    /// 늦게 들어온 플레이어에게는 조종사가 다시 보낸다. ModuleIds[i]가 모듈 i의 무장 ID다. 고른 ID가 이 기체에 없으면 해금된 첫 모듈을 쓰고,
    /// 해금된 무장이 없으면(격납고에서 아무것도 사지 않았으면) 모듈 없이 출격한다 — 슬롯을 골라도 아무 일도 없다.
    ///
    /// 모듈 = 다이얼 기능. ACO_DFUNC_Missile(4AAM 등)과 SAV 기능(DFUNC_Bomb, DFUNC_Rockets 등)을 모두 쓸 수 있다.
    /// SAV 기능은 다이얼 배열에서 빼고(이중으로 이벤트를 받지 않게) 제자리에 둔다 — 프리팹 인스턴스 안이라 옮길 수 없다.
    /// SAV 기능은 탑승자 수를 세므로(PilotEnter/Exit 짝) 시작할 때 억지로 끄지 않고, 장착/해제 신호도 ACO 모듈에만 보낸다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_DFUNC_Special : UdonSharpBehaviour
    {
        [Header("모듈")]
        [Tooltip("장착할 수 있는 무장 모듈(다이얼 기능). 이 오브젝트의 자식에 둔다")]
        public UdonSharpBehaviour[] Modules;
        [Tooltip("모듈별 무장 ID. 격납고(ACO_Loadout)의 SpecialIds와 같은 값을 쓴다")]
        public string[] ModuleIds;
        [Header("모듈별 표시 (Modules와 같은 순서 — 장착된 모듈 것만 켜고 나머지는 끈다, 비워도 된다)")]
        [Tooltip("모듈별 기체 외형(파일런에 달린 미사일 모델 등). 여러 개면 빈 부모로 묶어 부모를 넣는다")]
        public GameObject[] ModuleVisuals;
        [Tooltip("모듈별 HUD 탄종 표시(\"4AAM\" 글자·아이콘 오브젝트 등)")]
        public GameObject[] ModuleHudLabels;
        [Tooltip("모듈별 MFD 탄종 표시(무장 페이지의 이름·그림 오브젝트 등)")]
        public GameObject[] ModuleMfdLabels;
        [Tooltip("모듈별 다이얼 아이콘")]
        public GameObject[] ModuleDialIcons;
        [Tooltip("장착된 무장이 없을 때만 켤 오브젝트(빈 파일런, HUD/MFD의 \"NO WPN\" 표시 등)")]
        public GameObject[] EmptyObjects;

        [Header("격납고")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 격납고를 넣는다. 비면 0번 모듈만 쓴다")]
        public ACO_Loadout Loadout;

        // SaccEntity가 주입한다(DFUNC 규약).
        [System.NonSerialized] public SaccEntity EntityControl;
        [System.NonSerialized] public bool LeftDial = false;
        [System.NonSerialized] public int DialPosition = -999;

        private const byte NO_MODULE = 255;

        private int _current = -1;   // 장착한 모듈(-1 = 없음)
        private bool _initialized;
        private bool _pilot;      // 이 클라이언트가 조종 중
        private bool _occupied;   // 누군가 조종 중(모든 클라이언트)
        private bool _selected;

        // ───────────── SAV DFUNC 규약 ─────────────

        public void SFEXT_L_EntityStart()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (Modules == null) { Modules = new UdonSharpBehaviour[0]; }

            // 모듈은 다이얼 배열에 없으므로 SaccEntity가 넣어 주는 값을 대신 넣고, 시작 이벤트도 대신 보낸다.
            for (int i = 0; i < Modules.Length; i++)
            {
                UdonSharpBehaviour m = Modules[i];
                if (!m) { continue; }
                m.SetProgramVariable("EntityControl", EntityControl);
                m.SetProgramVariable("LeftDial", LeftDial);
                m.SetProgramVariable("DialPosition", DialPosition);
                m.SendCustomEvent("SFEXT_L_EntityStart");
                if (IsAcoModule(m))
                {
                    m.SendCustomEvent("SFEXT_G_PilotExit");   // 탑승 전엔 꺼 둔다(DFUNC 규약과 같은 상태)
                    m.SendCustomEvent("_ACO_Unequip");
                }
            }
            // 표시 오브젝트는 모두 끄고 시작한다(씬에 켜 둔 채여도 장착된 모듈 것만 남게)
            int count = Mathf.Max(Modules.Length, MaxLength());
            for (int i = 0; i < count; i++) { SetModuleShown(i, false); }
            _current = -1;
            SetEmptyShown(true);
            Equip(0);
        }

        public void SFEXT_O_PilotEnter()
        {
            _pilot = true;
            // 격납고에서 고른 무장으로 바꾸고 모두에게 알린다(내 화면은 바로 바꾼다).
            int want = FindModule(Loadout != null ? Loadout.GetSpecialId() : null);
            if (want != _current)
            {
                Equip(want);
                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetEquip), want < 0 ? NO_MODULE : (byte)want);
            }
            Forward("SFEXT_O_PilotEnter");
        }

        public void SFEXT_O_PilotExit()
        {
            Forward("SFEXT_O_PilotExit");
            _pilot = false;
        }

        public void SFEXT_G_PilotEnter()
        {
            _occupied = true;
            Forward("SFEXT_G_PilotEnter");
        }

        public void SFEXT_G_PilotExit()
        {
            Forward("SFEXT_G_PilotExit");
            _occupied = false;
        }

        public void DFUNC_Selected()
        {
            _selected = true;
            UdonSharpBehaviour m = Current();
            if (m) { m.SetProgramVariable("LeftDial", LeftDial); }
            Forward("DFUNC_Selected");
        }

        public void DFUNC_Deselected()
        {
            Forward("DFUNC_Deselected");
            _selected = false;
        }

        /// 데스크톱 키로 이 슬롯을 선택한다(SaccEntity가 부른다).
        public void KeyboardInput()
        {
            if (EntityControl) { EntityControl.ToggleStickSelection(this); }
        }

        // 탄약 초기화/보급은 장착되지 않은 모듈도 함께 받는다(바꿔 끼웠을 때 가득 찬 상태로).
        public void SFEXT_G_Explode() { ForwardAll("SFEXT_G_Explode"); }
        public void SFEXT_G_RespawnButton() { ForwardAll("SFEXT_G_RespawnButton"); }
        public void SFEXT_G_ReSupply() { Forward("SFEXT_G_ReSupply"); }
        public void SFEXT_G_ReArm() { Forward("SFEXT_G_ReArm"); }
        public void SFEXT_G_TouchDown() { Forward("SFEXT_G_TouchDown"); }

        /// 늦게 들어온 플레이어는 장착 이벤트를 받지 못했으므로 조종사가 다시 보낸다.
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!_pilot || player == null || player.isLocal) { return; }
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetEquip), _current < 0 ? NO_MODULE : (byte)_current);
        }

        [NetworkCallable]
        public void NetEquip(byte module)
        {
            if (!_initialized) { SFEXT_L_EntityStart(); }
            Equip(module == NO_MODULE ? -1 : module);
        }

        // ───────────── 장착 ─────────────

        /// 모듈을 바꾼다(-1 = 모듈 없음). 지금 상태(탑승/조종/선택)를 옛 모듈에서 내리고 새 모듈에 그대로 다시 올린다.
        private void Equip(int index)
        {
            if (Modules.Length == 0) { return; }
            index = index < 0 ? -1 : Mathf.Min(index, Modules.Length - 1);
            if (index == _current) { return; }

            UdonSharpBehaviour old = Current();
            if (old)
            {
                if (_selected) { old.SendCustomEvent("DFUNC_Deselected"); }
                if (_pilot) { old.SendCustomEvent("SFEXT_O_PilotExit"); }
                if (_occupied) { old.SendCustomEvent("SFEXT_G_PilotExit"); }
                if (IsAcoModule(old)) { old.SendCustomEvent("_ACO_Unequip"); }
            }
            SetModuleShown(_current, false);

            _current = index;
            SetModuleShown(_current, true);
            SetEmptyShown(_current < 0);
            UdonSharpBehaviour m = Current();
            if (!m) { return; }
            if (IsAcoModule(m)) { m.SendCustomEvent("_ACO_Equip"); }
            if (_occupied) { m.SendCustomEvent("SFEXT_G_PilotEnter"); }
            if (_pilot) { m.SendCustomEvent("SFEXT_O_PilotEnter"); }
            if (_selected)
            {
                m.SetProgramVariable("LeftDial", LeftDial);
                m.SendCustomEvent("DFUNC_Selected");
            }
        }

        private void SetModuleShown(int index, bool on)
        {
            if (index < 0) { return; }
            SetShown(ModuleVisuals, index, on);
            SetShown(ModuleHudLabels, index, on);
            SetShown(ModuleMfdLabels, index, on);
            SetShown(ModuleDialIcons, index, on);
        }

        private void SetShown(GameObject[] list, int index, bool on)
        {
            if (list != null && index < list.Length && list[index] != null && list[index].activeSelf != on) { list[index].SetActive(on); }
        }

        private void SetEmptyShown(bool on)
        {
            if (EmptyObjects == null) { return; }
            for (int i = 0; i < EmptyObjects.Length; i++) { SetShown(EmptyObjects, i, on); }
        }

        private int MaxLength()
        {
            int n = 0;
            if (ModuleVisuals != null) { n = Mathf.Max(n, ModuleVisuals.Length); }
            if (ModuleHudLabels != null) { n = Mathf.Max(n, ModuleHudLabels.Length); }
            if (ModuleMfdLabels != null) { n = Mathf.Max(n, ModuleMfdLabels.Length); }
            if (ModuleDialIcons != null) { n = Mathf.Max(n, ModuleDialIcons.Length); }
            return n;
        }

        /// ACO 모듈(장착/해제 신호를 받는다)인가. SAV 기능엔 그 이벤트가 없다.
        private bool IsAcoModule(UdonSharpBehaviour m)
        {
            return m != null && m.GetUdonTypeName() == GetUdonTypeName<ACO_DFUNC_Missile>();
        }

        /// 고른 무장 ID의 모듈. 없으면 해금된 첫 모듈, 그것도 없으면 -1(격납고가 없으면 0번).
        private int FindModule(string id)
        {
            if (Loadout == null || ModuleIds == null) { return 0; }
            int n = Mathf.Min(ModuleIds.Length, Modules.Length);
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < n; i++) { if (ModuleIds[i] == id) { return i; } }
            }
            for (int i = 0; i < n; i++) { if (Loadout.IsIdUnlocked(ModuleIds[i])) { return i; } }
            return -1;
        }

        private UdonSharpBehaviour Current()
        {
            if (_current < 0 || Modules == null || _current >= Modules.Length) { return null; }
            return Modules[_current];
        }

        private void Forward(string eventName)
        {
            UdonSharpBehaviour m = Current();
            if (m) { m.SendCustomEvent(eventName); }
        }

        /// 모든 모듈에 보내되 장착된 모듈을 마지막에 — 모듈들이 HUD 잔탄 텍스트를 함께 쓰므로 장착된 모듈의 값이 남게.
        private void ForwardAll(string eventName)
        {
            for (int i = 0; i < Modules.Length; i++)
            {
                if (i != _current && Modules[i]) { Modules[i].SendCustomEvent(eventName); }
            }
            Forward(eventName);
        }
    }
}
