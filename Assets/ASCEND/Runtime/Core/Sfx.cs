using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Pooled 2D/3D audio for every sound effect, looping beds and music. Clips live in Resources/ASCEND/Audio.</summary>
    public class Sfx : MonoBehaviour
    {
        static Sfx _i;
        static Sfx I
        {
            get
            {
                if (_i == null)
                {
                    _i = FindFirstObjectByType<Sfx>();
                    if (_i == null) _i = new GameObject("Sfx").AddComponent<Sfx>();
                }
                return _i;
            }
        }

        const int PoolSize = 28;
        readonly List<AudioSource> pool = new List<AudioSource>();
        int next;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource music;

        void Awake()
        {
            if (_i != null && _i != this) { Destroy(gameObject); return; }
            _i = this;
            for (int k = 0; k < PoolSize; k++)
            {
                var go = new GameObject("src" + k);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 1.2f; s.maxDistance = 30f;
                s.dopplerLevel = 0f;
                pool.Add(s);
            }
        }

        public static AudioClip Clip(string name)
        {
            AudioClip c;
            if (clips.TryGetValue(name, out c)) return c;
            c = Resources.Load<AudioClip>("ASCEND/Audio/sfx/" + name);
            if (c == null) c = Resources.Load<AudioClip>("ASCEND/Audio/vo/" + name);
            if (c == null) c = Resources.Load<AudioClip>("ASCEND/Audio/music/" + name);
            clips[name] = c;
            return c;
        }

        static float Vol { get { return GameState.Exists ? GameState.I.sfxVolume : 1f; } }

        AudioSource Take()
        {
            for (int k = 0; k < pool.Count; k++)
            {
                var s = pool[(next + k) % pool.Count];
                if (!s.isPlaying) { next = (next + k + 1) % pool.Count; return s; }
            }
            var r = pool[next]; next = (next + 1) % pool.Count; return r;
        }

        /// <summary>Positional one-shot.</summary>
        public static AudioSource Play(string name, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            var c = Clip(name); if (c == null) return null;
            var s = I.Take();
            s.transform.position = pos;
            s.spatialBlend = 1f; s.loop = false; s.clip = c; s.volume = volume * Vol; s.pitch = pitch;
            s.Play();
            return s;
        }

        /// <summary>Non-positional one-shot (UI, celebrations, announcer).</summary>
        public static AudioSource UI(string name, float volume = 1f, float pitch = 1f)
        {
            var c = Clip(name); if (c == null) return null;
            var s = I.Take();
            s.spatialBlend = 0f; s.loop = false; s.clip = c; s.volume = volume * Vol; s.pitch = pitch;
            s.Play();
            return s;
        }

        /// <summary>A random chip clack — chips everywhere use this.</summary>
        public static void Chip(Vector3 pos, float volume = 0.8f)
        {
            Play("chip_clack" + Random.Range(1, 5), pos, volume, Random.Range(0.92f, 1.1f));
        }

        /// <summary>Looping source attached to a transform. Returns the AudioSource so the caller can stop/fade it.</summary>
        public static AudioSource Loop(string name, Transform follow, float volume = 1f, float spatial = 1f, float maxDist = 25f)
        {
            var c = Clip(name); if (c == null || follow == null) return null;
            var go = new GameObject("loop_" + name);
            go.transform.SetParent(follow, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.playOnAwake = false; s.volume = volume * Vol; s.spatialBlend = spatial;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.maxDistance = maxDist; s.dopplerLevel = 0f;
            s.Play();
            return s;
        }

        // ------------------------------------------------------------ music (one global track, cross-faded)
        /// <summary>While set (intro cinematic), floor changes don't swap the music.</summary>
        public static string musicLock;
        /// <summary>The background mix: one long deep-house set split in parts (streamed from disk), played back to back
        /// and looping forever. It keeps its place when the raid music interrupts it and when you change floor.</summary>
        public const string Background = "bg_house";
        static readonly string[] BgParts = { "bg_house_1", "bg_house_2", "bg_house_3", "bg_house_4" };
        int bgIndex; float bgTime; AudioSource music2; double nextStart = -1; bool playlist;

        public static void Music(string name, float volume = 0.5f)
        {
            if (musicLock != null && name != musicLock) return;
            var me = I;
            if (name == Background)
            {
                if (me.playlist && me.music != null && me.music.isPlaying) { me.musicBase = volume; return; }
                me.EnsureSources();
                me.StopAllCoroutines();
                me.playlist = true; me.music.loop = false;
                me.StartCoroutine(me.CrossFade(Clip(BgParts[me.bgIndex]), volume, me.bgTime));
                return;
            }
            if (me.playlist && me.music != null && me.music.isPlaying) { me.bgTime = me.music.time; }
            me.playlist = false; me.nextStart = -1;
            if (me.music2 != null) me.music2.Stop();
            var c = Clip(name);
            me.EnsureSources();
            me.music.loop = true;
            if (me.music.clip == c && me.music.isPlaying) { me.musicBase = volume; return; }
            me.StopAllCoroutines();
            me.StartCoroutine(me.CrossFade(c, volume));
        }
        /// <summary>Respects musicLock too: the intro flies over the roof (no track there) and FloorManager.Activate
        /// used to silence the locked intro anthem.</summary>
        public static void StopMusic() { if (musicLock != null) return; if (I.playlist && I.music != null && I.music.isPlaying) I.bgTime = I.music.time; I.playlist = false; I.nextStart = -1; if (I.music2 != null) I.music2.Stop(); if (I.music != null) { I.StopAllCoroutines(); I.StartCoroutine(I.CrossFade(null, 0f)); } }

        void EnsureSources()
        {
            if (music == null) { music = gameObject.AddComponent<AudioSource>(); music.spatialBlend = 0f; music.playOnAwake = false; music.loop = true; }
            if (music2 == null) { music2 = gameObject.AddComponent<AudioSource>(); music2.spatialBlend = 0f; music2.playOnAwake = false; music2.loop = false; }
        }

        float musicBase = -1f; bool fading;
        static float MusicVol { get { return GameState.Exists ? GameState.I.musicVolume : 0.6f; } }

        IEnumerator CrossFade(AudioClip c, float volume) { return CrossFade(c, volume, 0f); }

        IEnumerator CrossFade(AudioClip c, float volume, float startAt)
        {
            fading = true;
            float v0 = music.volume;
            for (float t = 0; t < 1f; t += Time.deltaTime) { music.volume = Mathf.Lerp(v0, 0f, t); yield return null; }
            music.Stop();
            if (c == null) { fading = false; musicBase = -1f; yield break; }
            music.clip = c;
            if (startAt > 0f && startAt < c.length - 1f) music.time = startAt;
            music.Play();
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { music.volume = Mathf.Lerp(0f, volume * MusicVol, t / 1.5f); yield return null; }
            music.volume = volume * MusicVol;
            musicBase = volume; fading = false;
        }

        /// <summary>The MUSIC slider in Settings applies right away (not only on the next track change).</summary>
        void Update()
        {
            // gapless hand-over to the next part of the set (scheduled on the second source just before the end)
            if (playlist && music != null && music.clip != null && !fading)
            {
                if (nextStart < 0 && music.isPlaying)
                {
                    double left = (music.clip.samples - music.timeSamples) / (double)music.clip.frequency;
                    if (left < 1.0)
                    {
                        bgIndex = (bgIndex + 1) % BgParts.Length;
                        var nc = Clip(BgParts[bgIndex]);
                        if (nc != null)
                        {
                            nextStart = AudioSettings.dspTime + left;
                            music2.clip = nc; music2.volume = music.volume; music2.time = 0f;
                            music2.PlayScheduled(nextStart);
                        }
                    }
                }
                else if (nextStart >= 0 && AudioSettings.dspTime >= nextStart + 0.05)
                {
                    var t = music; music = music2; music2 = t; music2.Stop();
                    nextStart = -1; bgTime = 0f;
                }
                else if (!music.isPlaying && nextStart < 0)
                {
                    // fallback: the part ended without a scheduled successor
                    bgIndex = (bgIndex + 1) % BgParts.Length; bgTime = 0f;
                    music.clip = Clip(BgParts[bgIndex]); if (music.clip != null) music.Play();
                }
                if (music2 != null && music2.isPlaying) music2.volume = music.volume;
            }
            if (fading || musicBase < 0f || music == null || !music.isPlaying) return;
            float v = musicBase * MusicVol;
            if (Mathf.Abs(music.volume - v) > 0.001f) music.volume = v;
        }
    }
}
