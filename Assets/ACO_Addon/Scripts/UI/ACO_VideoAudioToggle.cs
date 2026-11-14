using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// 선원실 영상 플레이어(YamaPlayer) 소리 범위 토글(6-220 — 동기화 없음, 내 화면만): NEAR = 3D(선원실 안 — 스피커에서 NearMin까지 그대로, NearMax에서 0),
    /// GLOBAL = 2D(함내 · 격납고 · 출격 중 조종석 어디서나). 영상 재생 · 동기화 · 볼륨은 YamaPlayer가 그대로 한다(소리 오브젝트의 spatialBlend · 거리만 바꾼다 — 패키지는 고치지 않는다).
    /// 상황별 BGM(ACO_Bgm)과는 상관없다. 값은 플레이어 영구 저장(PlayerData, 키 aco_video_global)에 남는다 — 불러오기 전에는 저장하지 않는다.
    /// 버튼(Button.onClick → SendCustomEvent "Toggle")이 부른다. Bake가 Targets(YamaPlayer 소리) · Sound · Loc를 잇는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_VideoAudioToggle : UdonSharpBehaviour
    {
        [Tooltip("Bake가 넣는다 — 선원실 영상 플레이어(YamaPlayer)의 소리")]
        public AudioSource[] Targets;
        [Tooltip("Bake가 넣는다 — 버튼 효과음")]
        public ACO_UiSound Sound;
        [Tooltip("번역 장치(6-166). Bake가 넣는다")]
        public ACO_Localization Loc;
        public TextMeshProUGUI Label;
        public Image Background;
        public Color NearColor = new Color(0.1f, 0.16f, 0.12f, 1f);
        public Color GlobalColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        [Tooltip("NEAR일 때 소리 범위(m): 스피커에서 이 거리까지 그대로, NearMax에서 0(직선으로 줄어든다)")]
        public float NearMin = 5f;
        public float NearMax = 10f;

        private const string KEY_GLOBAL = "aco_video_global";
        private bool _global;
        private bool _restored;

        private void Start() { Apply(); }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) { return; }
            bool b;
            if (PlayerData.TryGetBool(player, KEY_GLOBAL, out b)) { _global = b; }
            _restored = true;
            Apply();
        }

        public void Toggle()
        {
            _global = !_global;
            Apply();
            if (_restored) { PlayerData.SetBool(KEY_GLOBAL, _global); }
            if (Sound != null) { Sound.PlayAt(ACO_UiSound.KIND_CONFIRM, transform.position); }
        }

        /// 번역 장치가 언어를 바꾸면 부른다.
        public void LocalizationChanged() { Refresh(); }

        private void Apply()
        {
            if (Targets != null)
            {
                for (int i = 0; i < Targets.Length; i++)
                {
                    AudioSource a = Targets[i];
                    if (a == null) { continue; }
                    a.spatialBlend = _global ? 0f : 1f;
                    a.rolloffMode = AudioRolloffMode.Linear;
                    a.minDistance = NearMin;
                    a.maxDistance = NearMax;
                }
            }
            Refresh();
        }

        private void Refresh()
        {
            if (Label != null) { Label.text = _global ? T("video.sound.global", "VIDEO SOUND  GLOBAL") : T("video.sound.near", "VIDEO SOUND  NEAR"); }
            if (Background != null) { Background.color = _global ? GlobalColor : NearColor; }
        }

        /// 번역 표의 문장(장치가 없거나 표에 없으면 english)
        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
    }
}
