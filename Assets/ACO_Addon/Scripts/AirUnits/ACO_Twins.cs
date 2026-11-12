using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 슈퍼에이스(NPC_Heads 안의 자식 오브젝트). 기체·비행·AI·동기화는 일반 NPC 전투기(ACO_NPCFighterAI) 그대로 쓰고, 이 스크립트가 특수무장과 무전을 더한다.
    /// 미션 생성기는 슈퍼에이스를 일반 배치에 쓰지 않는다 — 분신 에이스 형제 페이즈(ACO_PhaseTwins)만 내보낸다(OBJ_TWINS — 형 HEADS·동생 TAILS 2기 편대).
    ///
    /// - 다연장(Salvo): SalvoCooldown마다 SalvoRange 안의 플레이어를 가까운 순으로 SalvoMaxTargets명까지 골라 SalvoWarnTime 동안 락온 경보를 보낸 뒤
    ///   모두에게 동시에 쏜다(전용 런처 SalvoLauncher — 연사 간격 0, 자체 탄수/재장전). 판단은 AI 오너만 한다(발사는 런처가 모두에게 알린다).
    /// - 기만체(Decoys): 나를 노린 플레이어 미사일이 DecoyReactDistance 안으로 오면(DecoyCooldown, 출격당 DecoyCharges번) 가짜 표적을 뿌린다.
    ///   기만체는 플레이어 락온 레이어(29)의 빈 오브젝트로, 시작할 때 기체에서 떼어 낸 뒤 편대처럼 기체 옆(DecoyOffsets, 기체 기준)에 붙어
    ///   기체의 기동을 그대로 따라 난다(DecoySpreadTime 동안 기체 자리에서 벌어지고, DecoySway만큼 살짝 흔들린다). 각 클라이언트가 자기 화면의 기체 위치로
    ///   계산하므로 위치 동기화가 없다. HUD 마커는 진짜와 똑같이(이름·목표 색·체력 바) 그려지고, 락온해 쏜 미사일은 기만체 근처에서 터진다(피해 없음).
    /// - 쌍둥이 규칙: 같이 출격한 짝(Partner — Bake가 잇는다)이 이번 출격에서 살아 있다가 격추되면, 남은 쪽이 분노한다(모든 화면이 각자 — 체력 동기화로 같은 순간을 본다):
    ///   다연장 간격 × EnragedSalvoFactor, 기만체 횟수를 다시 채우고 간격 × EnragedDecoyCooldownFactor, 대사 LinePartnerDown. 다음 출격에서 풀린다.
    /// - 교차 기동(형제 — 짝이 살아 있는 동안): 공통 부품 ACO_CrossManeuver(Cross — 형이 정한다)가 맡는다. 시작하면 교차 대사(OnCrossStart),
    ///   엇갈리는 순간 기만체를 뿌린다(OnCrossPass — 횟수를 쓰지 않는다, 플레어는 FlareWithDecoys로 같이). 분노하면 교차하지 않는다(Cross.Allowed).
    /// - 무전: 등장·다연장·기만체·체력 절반·교차·짝 격추·격추 때 Chatter.Say로 자막(모든 클라이언트가 각자 본다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Twins : ACO_AceBase
    {
        public override int GetAceKind() { return KIND_TWIN; }
        public override string GetAceName() { return AceName; }
        /// 분신(기만체) — 본체와 같은 마커(이름·목표 색·체력)로, 떠 있을 때만 보인다.
        public override Transform[] GetSubUnits() { return Decoys; }

        [Header("이름 / 무전")]
        [Tooltip("HUD 마커·페이즈 배너·무전에 쓰는 이름(영문 대문자)")]
        public string AceName = "HEADS";
        [Tooltip("자막의 이름 색")]
        public Color SpeakerColor = new Color(1f, 0.35f, 0.3f, 1f);
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다")]
        [UnityEngine.Serialization.FormerlySerializedAs("Radio")]
        public ACO_Chatter Chatter;
        public string LineSpawn = "So you're the ones they keep talking about. Let's see what you've got.";
        public string LineSalvo = "Fox three, all of you. Dance.";
        public string LineDecoy = "Which one of me are you aiming at?";
        public string LineHalf = "Not bad... Now I'm getting serious.";
        public string LineDown = "Impossible... Who ARE you people?";
        [Tooltip("짝이 격추됐을 때(쌍둥이 규칙 — 분노)")]
        public string LinePartnerDown = "";
        [Tooltip("교차 기동을 시작할 때")]
        public string LineCross = "";
        [Tooltip("플레어 대사(평소 플레어 — 일반 적 플레어 무전 대신, FlareLineCooldown에 한 번 — 분신과 같이 뿌리는 플레어·교차 중엔 분신·교차 대사가 나오므로 없음). 형·동생 대사는 프리셋이 넣는다")]
        public string LineFlare = "";
        public float FlareLineCooldown = 25f;
        [Header("음성 (선택 — 위 대사와 짝, 비우면 자막만)")]
        public AudioClip ClipSpawn;
        public AudioClip ClipSalvo;
        public AudioClip ClipDecoy;
        public AudioClip ClipHalf;
        public AudioClip ClipDown;
        public AudioClip ClipPartnerDown;
        public AudioClip ClipCross;
        public AudioClip ClipFlare;
        [Tooltip("음성 대사가 무전 줄에서 이보다 오래(초) 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;

        [Header("참조 (같은 기체 — 프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        public SaccTarget TargetHealth;
        public ACO_NPCFlightController Flight;

        [Header("쌍둥이 규칙")]
        [Tooltip("같이 출격하는 짝(Bake가 넣는다 — 풀의 슈퍼에이스끼리). 비우면 분노하지 않는다")]
        public ACO_Twins Partner;
        [Tooltip("분노하면 다연장 간격에 곱한다")]
        public float EnragedSalvoFactor = 0.5f;
        [Tooltip("분노하면 기만체 간격에 곱한다(횟수는 다시 채운다)")]
        public float EnragedDecoyCooldownFactor = 0.5f;

        [Header("교차 기동 (형제)")]
        [Tooltip("교차 기동 부품(같은 기체 — 프리팹 빌더가 붙이고 Bake가 짝을 잇는다). 비우면 교차하지 않는다")]
        public ACO_CrossManeuver Cross;

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
        [Tooltip("기만체를 뿌리는 순간 플레어도 같이 뿌린다(평소 기만체·교차 기동 모두 — 플레어 횟수 제한·확률과 무관)")]
        public bool FlareWithDecoys = true;

        private bool _started;
        private bool _wasAlive;
        private float _nextFlareLine;
        private bool _decoyFlare;   // 분신과 같이 뿌리는 플레어(분신 대사가 나온다)
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
        private bool _partnerSeen;      // 이번 출격에서 짝이 살아 있는 걸 봤다
        private bool _enraged;

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
                if (Chatter != null) { Chatter.DropQueued(AceName); }   // 밀린 기술 대사는 버린다
                SayNow(LineDown, ClipDown);   // 격추 대사는 나오던 대사를 끊고 바로
                HideDecoys();
                _salvoState = 0;
            }
            _wasAlive = alive;
            if (!alive) { return; }

            TwinTick();

            if (!_halfSaid && TargetHealth != null && TargetHealth.FullHealth > 0f && TargetHealth.Health <= TargetHealth.FullHealth * 0.5f)
            {
                _halfSaid = true;
                Say(LineHalf, ClipHalf);
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
            _partnerSeen = false;
            _enraged = false;
            if (Cross != null) { Cross.Allowed = true; }
            HideDecoys();
            Say(LineSpawn, ClipSpawn);
        }

        /// 살아 있는가(등장·격추 판정과 같은 값 — 짝이 본다).
        public bool IsAceAlive() { return _wasAlive; }

        public bool IsEnraged() { return _enraged; }

        // ───────────── 교차 기동 (ACO_CrossManeuver가 부른다 — 모든 화면) ─────────────

        public void OnCrossStart() { Say(LineCross, ClipCross); }

        /// 평소 플레어(플레어 장치 NetFlare — 모든 화면): 전용 대사. 분신과 같이 뿌릴 때·교차 중엔 그 대사가 나오므로 없다.
        public override void OnFlare()
        {
            if (!_wasAlive || _decoyFlare || (Cross != null && Cross.IsCrossing()) || Time.time < _nextFlareLine) { return; }
            _nextFlareLine = Time.time + FlareLineCooldown;
            Say(LineFlare, ClipFlare);
        }

        /// 엇갈리는 순간 기만체(+ 플레어)를 뿌린다 — 기만체 횟수를 쓰지 않는다.
        public void OnCrossPass() { DeployDecoys(false); }

        // ───────────── 쌍둥이 규칙 (모든 화면) ─────────────

        private void TwinTick()
        {
            if (Partner == null || _enraged) { return; }
            if (Partner.IsAceAlive()) { _partnerSeen = true; return; }
            if (!_partnerSeen) { return; }
            _enraged = true;
            if (Cross != null) { Cross.Allowed = false; }
            _decoysLeft = DecoyCharges;
            _nextDecoy = Time.time;
            float cd = SalvoCooldown * EnragedSalvoFactor;
            if (_salvoState == 0 && _nextSalvo > Time.time + cd) { _nextSalvo = Time.time + cd; }
            Say(LinePartnerDown, ClipPartnerDown);
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
                _nextSalvo = Time.time + SalvoCooldown * (_enraged ? EnragedSalvoFactor : 1f);
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

        public void NetSalvoCall() { Say(LineSalvo, ClipSalvo); }

        // ───────────── 기만체 ─────────────

        private void OwnerDecoys()
        {
            if (Decoys == null || Decoys.Length == 0 || _decoysLeft <= 0 || _decoysOut || Time.time < _nextDecoy) { return; }
            if (AI.Flares == null || !AI.Flares.HasIncomingWithin(DecoyReactDistance)) { return; }
            _decoysLeft--;
            _nextDecoy = Time.time + DecoyCooldown * (_enraged ? EnragedDecoyCooldownFactor : 1f);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetDeployDecoys));
        }

        /// 모든 클라이언트: 기만체를 기체 자리에서 켠다 — 이후 MoveDecoys가 편대 자리로 벌리며 기체를 따라 붙인다.
        public void NetDeployDecoys() { DeployDecoys(true); }

        private void DeployDecoys(bool say)
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
            // 플레어도 같이(모든 화면이 이 자리에서 같이 부른다 — 플레어 장치의 NetFlare는 모든 화면용 연출·활성 시간 시작)
            if (FlareWithDecoys && AI != null && AI.Flares != null)
            {
                _decoyFlare = true;
                AI.Flares.NetFlare();
                _decoyFlare = false;
            }
            if (say) { Say(LineDecoy, ClipDecoy); }
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

        private void Say(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAce(AceName, SpeakerColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ENEMY);   // 잡담 급 — 밀리면 버린다(페이즈 대사만 우선)
        }

        /// 격추 대사: 나오던 무전을 끊고 바로 낸다.
        private void SayNow(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAceNow(AceName, SpeakerColor, text, clip, ACO_Chatter.CH_ENEMY);
        }
    }
}
