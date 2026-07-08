using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(VehicleController))]
    public class VehicleAudio : MonoBehaviour
    {
        VehicleController vehicle;
        AudioSource engineSource;
        AudioSource scrubSource;
        int lastGear;
        bool enabledAudio;
        float carVolumeScale;

        public void Initialize(bool audioEnabled, float volume)
        {
            enabledAudio = audioEnabled;
            carVolumeScale = volume;
            vehicle = GetComponent<VehicleController>();
            engineSource = gameObject.AddComponent<AudioSource>();
            engineSource.clip = CreateEngineLoop();
            engineSource.loop = true;
            engineSource.spatialBlend = 1f;
            engineSource.minDistance = 8f;
            engineSource.maxDistance = 95f;
            engineSource.volume = volume;

            scrubSource = gameObject.AddComponent<AudioSource>();
            scrubSource.clip = CreateScrubLoop();
            scrubSource.loop = true;
            scrubSource.spatialBlend = 1f;
            scrubSource.minDistance = 6f;
            scrubSource.maxDistance = 60f;
            scrubSource.volume = 0f;
            if (enabledAudio)
            {
                engineSource.Play();
                scrubSource.Play();
            }
        }

        void Update()
        {
            if (vehicle == null || engineSource == null)
            {
                return;
            }

            float categoryVolume = SimpleAudioManager.EngineVolumeScale;
            engineSource.mute = !enabledAudio || categoryVolume <= 0f;
            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / 330f);
            engineSource.pitch = Mathf.Lerp(0.65f, 1.95f, speed01) + vehicle.CurrentGear * 0.035f;
            engineSource.volume = Mathf.Lerp(0.18f, 0.5f, speed01) * carVolumeScale * 2f * categoryVolume;
            if (lastGear != 0 && vehicle.CurrentGear != lastGear)
            {
                SimpleAudioManager.PlayShift(transform.position);
            }

            lastGear = vehicle.CurrentGear;

            // Tyre scrub when sliding, kerb rumble when riding kerbs, a duller
            // rumble when running through gravel/runoff.
            if (scrubSource != null)
            {
                scrubSource.mute = !enabledAudio || categoryVolume <= 0f;
                float slip = Mathf.Clamp01(vehicle.OversteerAmount + vehicle.UndersteerAmount * 0.5f);
                float scrub = slip * Mathf.Clamp01(speed01 * 2.2f) * 0.28f;
                float kerb = vehicle.IsOnKerb && speed01 > 0.1f ? 0.22f : 0f;
                float gravel = vehicle.IsOffTrackSlowdown && speed01 > 0.08f ? 0.3f : 0f;
                float target = Mathf.Max(scrub, Mathf.Max(kerb, gravel)) * carVolumeScale * 2f * categoryVolume;
                scrubSource.volume = Mathf.MoveTowards(scrubSource.volume, target, Time.deltaTime * 1.8f);
                scrubSource.pitch = gravel > 0f ? 0.4f : (vehicle.IsOnKerb ? 0.55f : Mathf.Lerp(0.85f, 1.25f, slip));
            }
        }

        AudioClip CreateScrubLoop()
        {
            int sampleRate = 44100;
            int sampleCount = sampleRate;
            float[] samples = new float[sampleCount];
            float smoothed = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                smoothed = Mathf.Lerp(smoothed, Random.Range(-1f, 1f), 0.4f);
                samples[i] = smoothed * 0.5f;
            }

            AudioClip clip = AudioClip.Create("tyre scrub loop", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        AudioClip CreateEngineLoop()
        {
            int sampleRate = 44100;
            int sampleCount = sampleRate;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float wave = Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.55f;
                wave += Mathf.Sin(2f * Mathf.PI * 240f * t) * 0.25f;
                wave += Mathf.Sin(2f * Mathf.PI * 360f * t) * 0.12f;
                samples[i] = wave * 0.24f;
            }

            AudioClip clip = AudioClip.Create("generated engine loop", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
