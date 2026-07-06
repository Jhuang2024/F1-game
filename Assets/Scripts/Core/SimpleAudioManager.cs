using UnityEngine;

namespace LocalFormulaRacing
{
    public class SimpleAudioManager : MonoBehaviour
    {
        static SimpleAudioManager instance;

        AudioSource source;
        AudioSource rainSource;
        AudioClip clickClip;
        AudioClip shiftClip;
        AudioClip collisionClip;
        AudioClip drsClip;
        AudioClip rainClip;
        bool enabledAudio = true;

        public static void Ensure(Transform parent)
        {
            if (instance != null)
            {
                return;
            }

            GameObject audioObject = new GameObject("Generated placeholder audio");
            audioObject.transform.SetParent(parent, false);
            instance = audioObject.AddComponent<SimpleAudioManager>();
            instance.Initialize();
        }

        public static void SetEnabled(bool enabled)
        {
            if (instance != null)
            {
                instance.enabledAudio = enabled;
                if (!enabled && instance.rainSource != null)
                {
                    instance.rainSource.Stop();
                }
            }
        }

        public static void PlayClick()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.source.PlayOneShot(instance.clickClip, 0.22f);
            }
        }

        public static void PlayShift(Vector3 position)
        {
            if (instance != null && instance.enabledAudio)
            {
                AudioSource.PlayClipAtPoint(instance.shiftClip, position, 0.22f);
            }
        }

        public static void PlayCollision(Vector3 position, float strength)
        {
            if (instance != null && instance.enabledAudio)
            {
                AudioSource.PlayClipAtPoint(instance.collisionClip, position, Mathf.Clamp01(strength / 16f) * 0.45f);
            }
        }

        public static void PlayDrsAvailable()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.source.PlayOneShot(instance.drsClip, 0.18f);
            }
        }

        public static void SetRain(bool active)
        {
            if (instance == null || instance.rainSource == null)
            {
                return;
            }

            if (active && instance.enabledAudio)
            {
                if (!instance.rainSource.isPlaying)
                {
                    instance.rainSource.Play();
                }
            }
            else
            {
                instance.rainSource.Stop();
            }
        }

        void Initialize()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            clickClip = CreateTone("menu click", 880f, 0.045f, 0.35f);
            shiftClip = CreateTone("gear shift", 520f, 0.07f, 0.6f);
            drsClip = CreateTone("drs available", 1320f, 0.08f, 0.55f);
            collisionClip = CreateNoise("collision", 0.12f);
            rainClip = CreateRainLoop();
            rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.clip = rainClip;
            rainSource.loop = true;
            rainSource.spatialBlend = 0f;
            rainSource.volume = 0.18f;
        }

        AudioClip CreateTone(string clipName, float frequency, float duration, float decay)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t / Mathf.Max(0.01f, decay));
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        AudioClip CreateNoise(string clipName, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t * 20f);
                samples[i] = Random.Range(-1f, 1f) * envelope;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        AudioClip CreateRainLoop()
        {
            int sampleRate = 44100;
            int sampleCount = sampleRate * 2;
            float[] samples = new float[sampleCount];
            float smoothed = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                smoothed = Mathf.Lerp(smoothed, Random.Range(-1f, 1f), 0.18f);
                samples[i] = smoothed * 0.22f;
            }

            AudioClip clip = AudioClip.Create("rain ambience", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
