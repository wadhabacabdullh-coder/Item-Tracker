using System.Collections.Generic;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Pooled sound playback. Top-down 2D: positional sounds get distance attenuation and stereo pan relative to the camera
    /// computed here (cheaper and more predictable than 3D spatialisation).
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip[]> _variants = new Dictionary<string, AudioClip[]>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private AudioSource _loopA;
        private int _next;

        public float SfxVolume = 0.9f;
        public float MasterVolume = 0.8f;
        public Transform Listener;

        private void Awake()
        {
            I = this;
            for (int i = 0; i < 28; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _pool.Add(s);
            }
            _loopA = gameObject.AddComponent<AudioSource>();
            _loopA.loop = true;
            _loopA.playOnAwake = false;
        }

        public AudioClip Clip(string path)
        {
            if (_clips.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + path);
            _clips[path] = c;
            return c;
        }

        /// <summary>Clips named base_0, base_1... are picked at random.</summary>
        private AudioClip Variant(string basePath)
        {
            if (!_variants.TryGetValue(basePath, out var arr))
            {
                var list = new List<AudioClip>();
                for (int i = 0; i < 4; i++)
                {
                    var c = Clip(basePath + "_" + i);
                    if (c != null) list.Add(c);
                }
                if (list.Count == 0 && Clip(basePath) != null) list.Add(Clip(basePath));
                arr = list.ToArray();
                _variants[basePath] = arr;
            }
            return arr.Length == 0 ? null : arr[Random.Range(0, arr.Length)];
        }

        private AudioSource NextSource()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[(_next + i) % _pool.Count];
                if (!s.isPlaying) { _next = (_next + i + 1) % _pool.Count; return s; }
            }
            _next = (_next + 1) % _pool.Count;
            return _pool[_next];
        }

        /// <summary>Plays a UI/non-positional sound.</summary>
        public void Play2D(string clip, float volume = 1f, float pitch = 1f)
        {
            var c = Clip("SFX/" + clip) ?? Variant("SFX/" + clip);
            if (c == null) return;
            var s = NextSource();
            s.clip = c;
            s.volume = volume * SfxVolume * MasterVolume;
            s.pitch = pitch;
            s.panStereo = 0f;
            s.Play();
        }

        /// <summary>
        /// Plays a sound at a world position. <paramref name="range"/> is the distance (tiles) at which it fades out.
        /// Repeats of the same clip within <paramref name="minInterval"/> seconds are dropped to avoid phasing.
        /// </summary>
        public void PlayAt(string clip, Vector2 pos, float volume = 1f, float range = 18f, float pitchJitter = 0.06f, float minInterval = 0.02f, bool variants = false)
        {
            if (_lastPlayed.TryGetValue(clip, out float last) && Time.unscaledTime - last < minInterval) return;
            var c = variants ? Variant("SFX/" + clip) : (Clip("SFX/" + clip) ?? Variant("SFX/" + clip));
            if (c == null) return;
            Vector2 lp = Listener != null ? (Vector2)Listener.position : Vector2.zero;
            float d = Vector2.Distance(lp, pos);
            if (d > range) return;
            float att = 1f - d / range;
            att *= att;
            _lastPlayed[clip] = Time.unscaledTime;
            var s = NextSource();
            s.clip = c;
            s.volume = volume * att * SfxVolume * MasterVolume;
            s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            s.panStereo = Mathf.Clamp((pos.x - lp.x) / 12f, -0.8f, 0.8f);
            s.Play();
        }

        public void PlayVoice(string voice, string category, Vector2 pos, float volume = 0.8f)
        {
            var c = Variant("Voice/" + voice + "_" + category);
            if (c == null) return;
            Vector2 lp = Listener != null ? (Vector2)Listener.position : Vector2.zero;
            float d = Vector2.Distance(lp, pos);
            if (d > 20f) return;
            var s = NextSource();
            s.clip = c;
            s.volume = volume * (1f - d / 20f) * SfxVolume * MasterVolume;
            s.pitch = Random.Range(0.94f, 1.06f);
            s.panStereo = Mathf.Clamp((pos.x - lp.x) / 12f, -0.8f, 0.8f);
            s.Play();
        }

        /// <summary>One looping effect channel (alarm, distraction music...).</summary>
        public void SetLoop(string clip, float volume)
        {
            if (string.IsNullOrEmpty(clip) || volume <= 0f)
            {
                if (_loopA.isPlaying) _loopA.Stop();
                return;
            }
            var c = Clip("SFX/" + clip);
            if (_loopA.clip != c) { _loopA.clip = c; _loopA.Play(); }
            if (!_loopA.isPlaying) _loopA.Play();
            _loopA.volume = volume * SfxVolume * MasterVolume;
        }

        public void StopAll()
        {
            foreach (var s in _pool) s.Stop();
            _loopA.Stop();
        }
    }
}
