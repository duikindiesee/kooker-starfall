using System;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Procedurally synthesizes and spatializes location-specific ambient audio:
    /// 1. Freshwater River Flow / Trickle: Rises near the riverbanks and ford shallows.
    /// 2. Waterfall Plunge Pool Roar: Deep low-frequency cascade reverberation in the south gorge.
    /// 3. Canyon Plateau Wind Whisper: Dry desert breeze rustling across the upper terrace and First Refuge.
    /// Zero external audio files required (0 MB build size impact).
    /// </summary>
    public sealed class StarfallAmbientSoundscape : MonoBehaviour
    {
        private AudioSource riverAudio;
        private AudioSource waterfallAudio;
        private AudioSource windAudio;

        private AudioClip riverClip;
        private AudioClip waterfallClip;
        private AudioClip windClip;

        private bool initialized;

        private void Awake()
        {
            InitializeAudioSources();
        }

        private void InitializeAudioSources()
        {
            if (initialized) return;
            initialized = true;

            int sampleRate = 22050;
            int length = sampleRate * 2; // 2.0s seamless loop

            // 1. Procedural River Flow Clip (gentle bubbling & rushing water band)
            {
                float[] samples = new float[length];
                var rng = new System.Random(101);
                float filter = 0f;
                for (int i = 0; i < length; i++)
                {
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    // Bandpass modulation around river rush frequency
                    filter = filter * 0.75f + white * 0.25f;
                    float mod = 0.85f + 0.15f * Mathf.Sin((i / (float)sampleRate) * Mathf.PI * 3f);
                    samples[i] = filter * mod * 0.18f;
                }
                riverClip = AudioClip.Create("Procedural_River_Flow", length, 1, sampleRate, false);
                riverClip.SetData(samples, 0);

                riverAudio = gameObject.AddComponent<AudioSource>();
                riverAudio.clip = riverClip;
                riverAudio.loop = true;
                riverAudio.playOnAwake = false;
                riverAudio.volume = 0f;
                riverAudio.spatialBlend = 0f; // Ambient stereo bed
                riverAudio.Play();
            }

            // 2. Procedural Waterfall Roar Clip (deep bass plunge cascade)
            {
                float[] samples = new float[length];
                var rng = new System.Random(202);
                float brown = 0f;
                for (int i = 0; i < length; i++)
                {
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    // Lowpass brownian integration for powerful cavernous roar
                    brown = (brown * 0.96f) + (white * 0.04f);
                    float surge = 0.88f + 0.12f * Mathf.Sin((i / (float)sampleRate) * Mathf.PI * 1.5f);
                    samples[i] = brown * surge * 0.40f;
                }
                waterfallClip = AudioClip.Create("Procedural_Waterfall_Roar", length, 1, sampleRate, false);
                waterfallClip.SetData(samples, 0);

                waterfallAudio = gameObject.AddComponent<AudioSource>();
                waterfallAudio.clip = waterfallClip;
                waterfallAudio.loop = true;
                waterfallAudio.playOnAwake = false;
                waterfallAudio.volume = 0f;
                waterfallAudio.spatialBlend = 0f;
                waterfallAudio.Play();
            }

            // 3. Procedural Canyon Wind Clip (soft high-altitude ridge breeze)
            {
                float[] samples = new float[length];
                var rng = new System.Random(303);
                float pink = 0f;
                for (int i = 0; i < length; i++)
                {
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    pink = (pink * 0.85f) + (white * 0.15f);
                    float gust = 0.80f + 0.20f * Mathf.Sin((i / (float)sampleRate) * Mathf.PI * 0.8f);
                    samples[i] = pink * gust * 0.12f;
                }
                windClip = AudioClip.Create("Procedural_Canyon_Wind", length, 1, sampleRate, false);
                windClip.SetData(samples, 0);

                windAudio = gameObject.AddComponent<AudioSource>();
                windAudio.clip = windClip;
                windAudio.loop = true;
                windAudio.playOnAwake = false;
                windAudio.volume = 0f;
                windAudio.spatialBlend = 0f;
                windAudio.Play();
            }
        }

        private void Update()
        {
            if (!initialized) InitializeAudioSources();

            Vector3 pos = transform.position;

            // 1. River Proximity: Channel runs along X ~ -25 to +15
            float distToChannel = Mathf.Abs(pos.x);
            float riverProximity = Mathf.Clamp01(1.0f - (distToChannel - 8f) / 45f);
            float nearWaterElevation = Mathf.Clamp01(1.0f - (pos.y - CoastalWater.Level) / 12f);
            float targetRiverVol = riverProximity * nearWaterElevation * 0.45f;

            // 2. Waterfall Proximity: Plunge pool at (0, -2.0, -240)
            float distToWaterfall = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(0f, 0f, -240f));
            float targetWaterfallVol = Mathf.Clamp01(1.0f - distToWaterfall / 160f) * 0.70f;

            // 3. Canyon Wind: Stronger on elevated slopes (Y > 5m) and terrace refuge (X < -80)
            float elevationFactor = Mathf.Clamp01((pos.y - CoastalWater.Level) / 18f);
            float refugePlateau = Mathf.Clamp01((-pos.x - 50f) / 60f);
            float targetWindVol = Mathf.Max(elevationFactor * 0.25f, refugePlateau * 0.35f);

            // Smooth crossfade transitions
            if (riverAudio != null)
                riverAudio.volume = Mathf.MoveTowards(riverAudio.volume, targetRiverVol, Time.deltaTime * 0.3f);

            if (waterfallAudio != null)
                waterfallAudio.volume = Mathf.MoveTowards(waterfallAudio.volume, targetWaterfallVol, Time.deltaTime * 0.3f);

            if (windAudio != null)
                windAudio.volume = Mathf.MoveTowards(windAudio.volume, targetWindVol, Time.deltaTime * 0.3f);
        }
    }
}
