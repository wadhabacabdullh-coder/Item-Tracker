using UnityEngine;

namespace ShadowContract.Game
{
    public enum MusicMood { Calm, Tension, Combat }

    /// <summary>
    /// Adaptive music: three stems (calm / tension / combat) of identical length and tempo start on the same DSP tick and
    /// are crossfaded according to the stealth situation. Also plays menu music, stings, map ambience and a heartbeat.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        public static MusicDirector I { get; private set; }

        private AudioSource _calm, _tension, _combat, _menu, _ambience, _sting, _heart;
        private float _wCalm, _wTension, _wCombat;
        private MusicMood _mood;
        private float _detection;
        private bool _inMission;
        private float _heartRate;

        public float MusicVolume = 0.6f;
        public float MasterVolume = 0.8f;

        private void Awake()
        {
            I = this;
            _calm = Make(true);
            _tension = Make(true);
            _combat = Make(true);
            _menu = Make(true);
            _ambience = Make(true);
            _sting = Make(false);
            _heart = Make(true);
        }

        private AudioSource Make(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        private static AudioClip Load(string p) => Resources.Load<AudioClip>("Audio/" + p);

        public void PlayMenu()
        {
            _inMission = false;
            StopMission();
            if (_menu.clip == null) _menu.clip = Load("Music/menu");
            if (!_menu.isPlaying) _menu.Play();
        }

        public void StartMission(string theme)
        {
            _menu.Stop();
            _calm.clip = Load("Music/calm");
            _tension.clip = Load("Music/tension");
            _combat.clip = Load("Music/combat");
            double start = AudioSettings.dspTime + 0.2;
            _calm.PlayScheduled(start);
            _tension.PlayScheduled(start);
            _combat.PlayScheduled(start);
            _ambience.clip = Load("Ambience/" + theme);
            _ambience.Play();
            _wCalm = 1f;
            _wTension = _wCombat = 0f;
            _mood = MusicMood.Calm;
            _inMission = true;
        }

        public void StopMission()
        {
            _calm.Stop();
            _tension.Stop();
            _combat.Stop();
            _ambience.Stop();
            _heart.Stop();
            _inMission = false;
        }

        public void SetMood(MusicMood mood, float detection)
        {
            _mood = mood;
            _detection = detection;
        }

        public void SetHeartbeat(float healthFraction)
        {
            _heartRate = healthFraction < 0.3f ? 1f - healthFraction / 0.3f : 0f;
        }

        public void Sting(string clip, float volume = 1f, bool duckMusic = true)
        {
            var c = Load(clip.Contains("/") ? clip : "Music/" + clip);
            if (c == null) return;
            _sting.clip = c;
            _sting.volume = volume * MusicVolume * MasterVolume;
            _sting.Play();
            if (duckMusic) StopMission();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float vol = MusicVolume * MasterVolume;
            _menu.volume = vol * 0.8f;
            if (!_inMission) return;

            float tc = 0f, tt = 0f, tk = 0f;
            switch (_mood)
            {
                case MusicMood.Calm:
                    tc = 1f;
                    tt = Mathf.Clamp01(_detection * 1.4f) * 0.8f;
                    break;
                case MusicMood.Tension:
                    tc = 0.35f;
                    tt = 1f;
                    break;
                case MusicMood.Combat:
                    tt = 0.45f;
                    tk = 1f;
                    break;
            }
            // Rise fast, fall slowly.
            _wCalm = Mathf.MoveTowards(_wCalm, tc, dt * (tc > _wCalm ? 0.6f : 0.25f));
            _wTension = Mathf.MoveTowards(_wTension, tt, dt * (tt > _wTension ? 1.2f : 0.2f));
            _wCombat = Mathf.MoveTowards(_wCombat, tk, dt * (tk > _wCombat ? 2.5f : 0.15f));
            _calm.volume = _wCalm * vol * 0.7f;
            _tension.volume = _wTension * vol * 0.8f;
            _combat.volume = _wCombat * vol * 0.85f;
            _ambience.volume = MasterVolume * 0.45f;

            if (_heartRate > 0.01f)
            {
                if (_heart.clip == null) _heart.clip = Load("SFX/heartbeat");
                if (!_heart.isPlaying) _heart.Play();
                _heart.volume = _heartRate * MasterVolume;
                _heart.pitch = 1f + _heartRate * 0.4f;
            }
            else if (_heart.isPlaying) _heart.Stop();
        }
    }
}
