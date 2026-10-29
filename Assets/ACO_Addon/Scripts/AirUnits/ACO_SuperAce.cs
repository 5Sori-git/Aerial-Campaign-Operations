using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 슈퍼에이스(NPC_SuperAce 안의 자식 오브젝트). 기체·비행·AI·동기화는 일반 NPC 전투기(ACO_NPCFighterAI) 그대로 쓰고, 이 스크립트가 특수무장과 무전을 더한다.
    /// 미션 생성기는 슈퍼에이스를 일반 배치에 쓰지 않는다 — 미션 관리자의 보너스 페이즈만 내보낸다(OBJ_SUPER_ACE).
    ///
    /// - 다연장(Salvo): SalvoCooldown마다 SalvoRange 안의 플레이어를 가까운 순으로 SalvoMaxTargets명까지 골라 SalvoWarnTime 동안 락온 경보를 보낸 뒤
    ///   모두에게 동시에 쏜다(전용 런처 SalvoLauncher — 연사 간격 0, 자체 탄수/재장전). 판단은 AI 오너만 한다(발사는 런처가 모두에게 알린다).
    /// - 기만체(Decoys): 나를 노린 플레이어 미사일이 DecoyReactDistance 안으로 오면(DecoyCooldown, 출격당 DecoyCharges번) 가짜 표적을 뿌린다.
    ///   기만체는 플레이어 락온 레이어(29)의 빈 오브젝트로, 시작할 때 기체에서 떼어 낸 뒤 편대처럼 기체 옆(DecoyOffsets, 기체 기준)에 붙어
    ///   기체의 기동을 그대로 따라 난다(DecoySpreadTime 동안 기체 자리에서 벌어지고, DecoySway만큼 살짝 흔들린다). 각 클라이언트가 자기 화면의 기체 위치로
    ///   계산하므로 위치 동기화가 없다. HUD 마커는 진짜와 똑같이(이름·목표 색·체력 바) 그려지고, 락온해 쏜 미사일은 기만체 근처에서 터진다(피해 없음).
    /// - 무전: 등장·다연장·기만체·체력 절반·격추 때 Radio.Say로 자막(모든 클라이언트가 각자 본다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_SuperAce : UdonSharpBehaviour
    {
        [Header("이름 / 무전")]
        [Tooltip("HUD 마커·페이즈 배너·무전에 쓰는 이름(영문 대문자)")]
        public string AceName = "REAPER";
        [Tooltip("자막의 이름 색")]
        public Color SpeakerColor = new Color(1f, 0.35f, 0.3f, 1f);
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다")]
        public ACO_Radio Radio;
        public string LineSpawn = "So you're the ones they keep talking about. Let's see what you've got.";
        public string LineSalvo = "Fox three, all of you. Dance.";
        public string LineDecoy = "Which one of me are you aiming at?";
        public string LineHalf = "Not bad... Now I'm getting serious.";
        public string LineDown = "Impossible... Who ARE you people?";

        [Header("참조 (같은 기체 — 프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        public SaccTarget TargetHealth;
        public ACO_NPCFlightController Flight;

        [Header("다연장")]
        [Tooltip("다연장 전용 런처(연사 간격 0). 비우면 다연장을 쓰지 않는다")]
        public ACO_MissileLauncher SalvoLauncher;
        [Tooltip("한 번에 노리는 플레이어 수(가까운 순)")]
        public int SalvoMaxTargets = 4;
        [Tooltip("플레이어 한 명에게 쏘는 수")]
        public int SalvoMissilesPerTarget = 1;
        public float SalvoRange = 5000f;
        [Tooltip("다연장 사이 간격(초)")]
        public float SalvoCooldown = 25f;
        [Tooltip("등장 뒤 첫 다연장까지(초)")]
        public float SalvoFirstDelay = 12f;
        [Tooltip("락온 경보를 보내고 쏘기까지(초) — 플레이어가 대비할 시간")]
        public float SalvoWarnTime = 1.5f;

        [Header("기만체")]
        [Tooltip("가짜 표적(레이어 29의 빈 오브젝트, 처음엔 꺼 둔다). 비우면 기만체를 쓰지 않는다")]
        public Transform[] Decoys;
        [Tooltip("기만체별 편대 자리(기체 기준 m: 오른쪽 +X, 위 +Y, 앞 +Z). 기체의 기동을 그대로 따라 이 자리에 붙어 난다")]
        public Vector3[] DecoyOffsets = new Vector3[] { new Vector3(-180f, 10f, -40f), new Vector3(180f, -10f, -40f), new Vector3(0f, 60f, -220f) };
        [Tooltip("뿌린 뒤 기체 자리에서 편대 자리까지 벌어지는 시간(초)")]
        public float DecoySpreadTime = 1.2f;
        [Tooltip("편대 자리에서 살짝 흔들리는 폭(m) — 완전히 굳은 편대로 보이지 않게")]
        public float DecoySway = 25f;
        [Tooltip("기만체가 떠 있는 시간(초)")]
        public float DecoyLifetime = 12f;
        public float DecoyCooldown = 18f;
        [Tooltip("출격당 뿌릴 수 있는 횟수")]
        public int DecoyCharges = 4;
        [Tooltip("나를 노린 미사일이 이 거리(m) 안이면 뿌린다")]
        public float DecoyReactDistance = 2500f;

        private bool _started;
        private bool _wasAlive;
        private bool _halfSaid;
        private float _spawnTime;
        private float _nextSalvo;
        private int _salvoState;        // 0 = 대기, 1 = 경보 중
        private float _salvoFireAt;
        private float _salvoWarnTimer;
        private int[] _salvoTargets = new int[8];
        private int _salvoCount;
        private float _nextDecoy;
        private int _decoysLeft;
        private float _decoyUntil;
        private bool _decoysOut;
        private float _decoyStart;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!AI) { AI = GetComponentInParent<ACO_NPCFighterAI>(); }
            if (AI && !TargetHealth) { TargetHealth = AI.TargetHealth; }
            if (AI && !Flight) { Flight = AI.Flight; }
            int n = Decoys != null ? Decoys.Length : 0;
            // 기만체는 기체에서 떼어 낸다 — 기체 아래에 있으면 미사일·HUD가 진짜 기체로 본다(부모의 SaccTarget을 찾는다).
            for (int i = 0; i < n; i++)
            {
                if (!Decoys[i]) { continue; }
                Decoys[i].SetParent(null, true);
                Decoys[i].gameObject.SetActive(false);
            }
        }

        void OnDisable()
        {
            // 생성기가 기체를 거둬들이면(비활성) 떠 있던 기만체도 치운다.
            HideDecoys();
            _wasAlive = false;
        }

        void Update()
        {
            if (!_started) { Start(); }
            bool alive = AI != null && AI.IsAlive() && (TargetHealth == null || TargetHealth.Health > 0f);

            // 등장/격추(모든 클라이언트 — 생성기 배치와 체력 동기화로 같은 순간을 본다)
            if (alive && !_wasAlive) { OnAceSpawn(); }
            if (!alive && _wasAlive)
            {
                Say(LineDown);
                HideDecoys();
                _salvoState = 0;
            }
            _wasAlive = alive;
            if (!alive) { return; }

            if (!_halfSaid && TargetHealth != null && TargetHealth.FullHealth > 0f && TargetHealth.Health <= TargetHealth.FullHealth * 0.5f)
            {
                _halfSaid = true;
                Say(LineHalf);
            }

            MoveDecoys(Time.deltaTime);

            if (!Networking.IsOwner(AI.gameObject)) { return; }
            OwnerSalvo();
            OwnerDecoys();
        }

        private void OnAceSpawn()
        {
            _spawnTime = Time.time;
            _halfSaid = false;
            _salvoState = 0;
            _nextSalvo = Time.time + SalvoFirstDelay;
            _nextDecoy = Time.time;
            _decoysLeft = DecoyCharges;
            HideDecoys();
            Say(LineSpawn);
        }

        // ───────────── 다연장 (오너) ─────────────

        private void OwnerSalvo()
        {
            if (!SalvoLauncher) { return; }
            if (_salvoState == 0)
            {
                if (Time.time < _nextSalvo) { return; }
                _nextSalvo = Time.time + 2f;   // 쏠 대상이 없으면 2초 뒤 다시 본다
                if (!SalvoLauncher.CanFire()) { return; }
                PickSalvoTargets();
                if (_salvoCount == 0) { return; }
                _salvoState = 1;
                _salvoFireAt = Time.time + SalvoWarnTime;
                _salvoWarnTimer = 0f;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSalvoCall));
            }
            if (_salvoState == 1)
            {
                // 경보 중: 0.5초마다 고른 플레이어 모두에게 락온 경보.
                _salvoWarnTimer -= Time.deltaTime;
                if (_salvoWarnTimer <= 0f)
                {
                    _salvoWarnTimer = 0.5f;
                    for (int k = 0; k < _salvoCount; k++) { SalvoLauncher.WarnTarget(_salvoTargets[k]); }
                }
                if (Time.time < _salvoFireAt) { return; }
                for (int k = 0; k < _salvoCount; k++)
                {
                    for (int m = 0; m < Mathf.Max(SalvoMissilesPerTarget, 1); m++) { SalvoLauncher.FireAt(_salvoTargets[k]); }
                }
                _salvoState = 0;
                _nextSalvo = Time.time + SalvoCooldown;
            }
        }

        /// SalvoRange 안의 플레이어 표적(AI의 TargetLayers, 활성)을 가까운 순으로 SalvoMaxTargets개.
        private void PickSalvoTargets()
        {
            _salvoCount = 0;
            SaccEntity e = AI.EntityControl;
            if (!e) { return; }
            GameObject[] targets = e.AAMTargets;
            if (targets == null) { return; }
            int max = Mathf.Clamp(SalvoMaxTargets, 1, _salvoTargets.Length);
            int mask = AI.TargetLayers;
            Vector3 me = transform.position;
            float rangeSq = SalvoRange * SalvoRange;
            for (int pick = 0; pick < max; pick++)
            {
                int best = -1;
                float bestSq = rangeSq;
                for (int i = 0; i < targets.Length; i++)
                {
                    GameObject g = targets[i];
                    if (!g || !g.activeInHierarchy || ((1 << g.layer) & mask) == 0) { continue; }
                    bool taken = false;
                    for (int k = 0; k < _salvoCount; k++) { if (_salvoTargets[k] == i) { taken = true; break; } }
                    if (taken) { continue; }
                    float sq = (g.transform.position - me).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = i; }
                }
                if (best < 0) { break; }
                _salvoTargets[_salvoCount] = best;
                _salvoCount++;
            }
        }

        public void NetSalvoCall() { Say(LineSalvo); }

        // ───────────── 기만체 ─────────────

        private void OwnerDecoys()
        {
            if (Decoys == null || Decoys.Length == 0 || _decoysLeft <= 0 || _decoysOut || Time.time < _nextDecoy) { return; }
            if (AI.Flares == null || !AI.Flares.HasIncomingWithin(DecoyReactDistance)) { return; }
            _decoysLeft--;
            _nextDecoy = Time.time + DecoyCooldown;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetDeployDecoys));
        }

        /// 모든 클라이언트: 기만체를 기체 자리에서 켠다 — 이후 MoveDecoys가 편대 자리로 벌리며 기체를 따라 붙인다.
        public void NetDeployDecoys()
        {
            if (!_started) { Start(); }
            if (Decoys == null) { return; }
            Transform body = AI != null ? AI.transform : transform;
            for (int i = 0; i < Decoys.Length; i++)
            {
                Transform d = Decoys[i];
                if (!d) { continue; }
                d.SetPositionAndRotation(body.position, body.rotation);
                d.gameObject.SetActive(true);
            }
            _decoysOut = true;
            _decoyStart = Time.time;
            _decoyUntil = Time.time + DecoyLifetime;
            Say(LineDecoy);
        }

        /// 기체 기준 편대 자리(DecoyOffsets)에 붙인다. 처음 DecoySpreadTime 동안은 기체 자리에서 부드럽게 벌어지고, 자리마다 다른 박자로 살짝 흔들린다.
        private void MoveDecoys(float dt)
        {
            if (!_decoysOut) { return; }
            if (Time.time >= _decoyUntil) { HideDecoys(); return; }
            Transform body = AI != null ? AI.transform : transform;
            float t = Time.time - _decoyStart;
            float spread = DecoySpreadTime > 0f ? Mathf.SmoothStep(0f, 1f, t / DecoySpreadTime) : 1f;
            for (int i = 0; i < Decoys.Length; i++)
            {
                Transform d = Decoys[i];
                if (!d || !d.gameObject.activeSelf) { continue; }
                Vector3 offset = DecoyOffsets != null && DecoyOffsets.Length > 0 ? DecoyOffsets[i % DecoyOffsets.Length] : new Vector3(i % 2 == 0 ? -150f : 150f, 0f, -50f);
                float phase = i * 2.1f;
                Vector3 sway = new Vector3(Mathf.Sin(t * 0.7f + phase), Mathf.Sin(t * 0.9f + phase * 1.3f) * 0.6f, Mathf.Sin(t * 0.5f + phase * 0.7f)) * DecoySway;
                d.SetPositionAndRotation(body.position + body.rotation * ((offset + sway) * spread), body.rotation);
            }
        }

        private void HideDecoys()
        {
            _decoysOut = false;
            if (Decoys == null) { return; }
            for (int i = 0; i < Decoys.Length; i++)
            {
                if (Decoys[i] && Decoys[i].gameObject.activeSelf) { Decoys[i].gameObject.SetActive(false); }
            }
        }

        private void Say(string text)
        {
            if (Radio != null && !string.IsNullOrEmpty(text)) { Radio.Say(AceName, SpeakerColor, text); }
        }
    }
}
