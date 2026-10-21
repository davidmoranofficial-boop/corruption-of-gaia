using System.Collections.Generic;
using UnityEngine;

namespace Loran.UnityLayer
{
    /// <summary>
    /// Everything you hear: one-off sound effects, a music track that crossfades when it changes, and layers of
    /// ambience (sea, birds, crickets, rain...) that fade in and out. Clips live in Resources/Audio/{Sfx,Music,Ambience}
    /// and are made by Tools/Audio/build.py. A missing clip is simply silent.
    /// Volumes (master, music, effects, ambience) are kept in PlayerPrefs.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        static GameAudio I;

        public static float Master = 1f, Music = 0.6f, Effects = 0.9f, Ambience = 0.7f;
        const float Fade = 1.6f;   // seconds to crossfade music and ambience

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource musicA, musicB;
        string musicId = "";
        float musicMix = 1f;   // 1 = all A
        readonly Dictionary<string, (AudioSource src, float target)> layers = new Dictionary<string, (AudioSource, float)>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        public static void Boot(GameObject host)
        {
            if (I != null) return;
            I = host.AddComponent<GameAudio>();
            if (Object.FindAnyObjectByType<AudioListener>() == null) host.AddComponent<AudioListener>();
            Master = PlayerPrefs.GetFloat("vol_master", Master);
            Music = PlayerPrefs.GetFloat("vol_music", Music);
            Effects = PlayerPrefs.GetFloat("vol_effects", Effects);
            Ambience = PlayerPrefs.GetFloat("vol_ambience", Ambience);
            for (int i = 0; i < 10; i++) I.pool.Add(I.NewSource(false));
            I.musicA = I.NewSource(true);
            I.musicB = I.NewSource(true);
        }

        public static void SaveVolumes()
        {
            PlayerPrefs.SetFloat("vol_master", Master);
            PlayerPrefs.SetFloat("vol_music", Music);
            PlayerPrefs.SetFloat("vol_effects", Effects);
            PlayerPrefs.SetFloat("vol_ambience", Ambience);
            PlayerPrefs.Save();
        }

        AudioSource NewSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0;
            return s;
        }

        AudioClip Clip(string folder, string id)
        {
            string key = folder + "/" + id;
            if (!clips.TryGetValue(key, out var c))
            {
                c = Resources.Load<AudioClip>("Audio/" + key);
                clips[key] = c;
            }
            return c;
        }

        /// <summary>Plays a sound effect. pitch varies a little each time so repeats don't sound mechanical.</summary>
        public static void Play(string id, float volume = 1f, float pitchVary = 0.04f, float delay = 0f)
        {
            if (I == null || string.IsNullOrEmpty(id)) return;
            // the same sound twice in the same instant is just louder, not better
            if (I.lastPlayed.TryGetValue(id, out var t) && Time.unscaledTime - t < 0.04f && delay <= 0) return;
            I.lastPlayed[id] = Time.unscaledTime;
            var clip = I.Clip("Sfx", id);
            if (clip == null) return;
            AudioSource src = null;
            foreach (var s in I.pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) src = I.pool[0];
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * Effects * Master);
            src.pitch = 1f + Random.Range(-pitchVary, pitchVary);
            if (delay > 0) src.PlayDelayed(delay); else src.Play();
        }

        /// <summary>Plays one of id_1, id_2, id_3 at random (footsteps, hammer blows).</summary>
        /// <summary>A voice blip at an exact pitch (each speaker has their own), quiet, a touch of variation.</summary>
        public static void Blip(float pitch)
        {
            if (I == null) return;
            var clip = I.Clip("Sfx", "blip");
            if (clip == null) return;
            AudioSource src = null;
            foreach (var s in I.pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) return;   // never cut off a real sound for a blip
            src.clip = clip;
            src.volume = Mathf.Clamp01(0.35f * Effects * Master);
            src.pitch = pitch * (1f + Random.Range(-0.05f, 0.05f));
            src.Play();
        }

        public static void PlayVariant(string id, int variants = 3, float volume = 1f, float pitchVary = 0.06f)
        {
            Play(id + "_" + Random.Range(1, variants + 1), volume, pitchVary);
        }

        /// <summary>Changes the music, crossfading. "" fades it out.</summary>
        public static void SetMusic(string id)
        {
            if (I == null || id == I.musicId) return;
            I.musicId = id;
            var clip = string.IsNullOrEmpty(id) ? null : I.Clip("Music", id);
            // the quieter source takes the new track and fades up
            var incoming = I.musicMix >= 0.5f ? I.musicB : I.musicA;
            incoming.Stop();
            incoming.clip = clip;
            if (clip != null) { incoming.volume = 0; incoming.Play(); }
        }

        /// <summary>Sets the ambience: each named layer fades to its volume (0-1); layers not named fade out.</summary>
        public static void SetAmbience(Dictionary<string, float> want)
        {
            if (I == null) return;
            foreach (var kv in want)
            {
                if (!I.layers.TryGetValue(kv.Key, out var l))
                {
                    var clip = I.Clip("Ambience", kv.Key);
                    if (clip == null) continue;
                    var src = I.NewSource(true);
                    src.clip = clip;
                    src.volume = 0;
                    src.time = Random.Range(0f, clip.length * 0.9f);
                    src.Play();
                    l = (src, 0);
                }
                I.layers[kv.Key] = (l.src, kv.Value);
            }
            foreach (var key in new List<string>(I.layers.Keys))
                if (!want.ContainsKey(key)) I.layers[key] = (I.layers[key].src, 0);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float step = dt / Fade;

            // music: slide the mix toward whichever source holds the current track
            // fade toward the source playing the track we want
            var wanted = WantedMusicSource();
            float targetMix = wanted == musicA ? 1f : 0f;
            musicMix = Mathf.MoveTowards(musicMix, targetMix, step);
            float mv = Music * Master;
            musicA.volume = (wanted == null ? Mathf.MoveTowards(musicA.volume, 0, step * mv) : musicMix * mv);
            musicB.volume = (wanted == null ? Mathf.MoveTowards(musicB.volume, 0, step * mv) : (1 - musicMix) * mv);
            if (musicA.volume <= 0.001f && wanted != musicA) musicA.Stop();
            if (musicB.volume <= 0.001f && wanted != musicB) musicB.Stop();

            // ambience layers
            float av = Ambience * Master;
            foreach (var key in new List<string>(layers.Keys))
            {
                var (src, target) = layers[key];
                float v = Mathf.MoveTowards(src.volume, target * av, step * Mathf.Max(av, 0.05f));
                src.volume = v;
                if (target <= 0 && v <= 0.001f)
                {
                    src.Stop();
                    Destroy(src);
                    layers.Remove(key);
                }
            }
        }

        AudioSource WantedMusicSource()
        {
            if (string.IsNullOrEmpty(musicId)) return null;
            if (musicA.clip != null && musicA.clip.name == musicId && musicA.isPlaying) return musicA;
            if (musicB.clip != null && musicB.clip.name == musicId && musicB.isPlaying) return musicB;
            return null;
        }
    }
}
