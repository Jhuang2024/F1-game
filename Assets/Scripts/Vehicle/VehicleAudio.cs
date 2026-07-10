using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(VehicleController))]
    public class VehicleAudio : MonoBehaviour
    {
        VehicleController vehicle;
        AudioSource engineSource;
        AudioSource scrubSource;
        // Production layered-engine path: used when the audio bank supplies
        // engine-layer clips, otherwise null and the generated loop below is the
        // labelled fallback.
        F1Game.Audio.EngineAudioLayers engineLayers;
        int lastGear;
        bool enabledAudio;
        float carVolumeScale;

        public void Initialize(bool audioEnabled, float volume)
        {
            enabledAudio = audioEnabled;
            carVolumeScale = volume;
            vehicle = GetComponent<VehicleController>();

            // Prefer authored layered engine audio when the bank has it.
            F1Game.Audio.AudioBank bank = F1Game.Audio.AudioBankService.Bank;
            if (bank != null && bank.engineExternal != null && bank.engineExternal.Count > 0)
            {
                engineLayers = gameObject.AddComponent<F1Game.Audio.EngineAudioLayers>();
                if (!engineLayers.Initialize(bank, onboardPerspective: false))
                {
                    Destroy(engineLayers);
                    engineLayers = null;
                }
            }

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
            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / 330f);

            if (engineLayers != null && engineLayers.HasLayers)
            {
                // Authored layered engine audio owns the engine voice; the
                // generated loop is silenced to a labelled fallback state.
                engineSource.mute = true;
                float rpm01 = Mathf.Clamp01(speed01 * 0.85f + (vehicle.CurrentGear > 0 ? 0.1f : 0f));
                engineLayers.SetMasterVolume((enabledAudio && categoryVolume > 0f) ? carVolumeScale * categoryVolume : 0f);
                engineLayers.Tick(rpm01, Mathf.Clamp01(vehicle.CurrentSpeedKph > 0f ? speed01 * 1.2f : 0f));
            }
            else
            {
                engineSource.mute = !enabledAudio || categoryVolume <= 0f;
                engineSource.pitch = Mathf.Lerp(0.65f, 1.95f, speed01) + vehicle.CurrentGear * 0.035f;
                engineSource.volume = Mathf.Lerp(0.18f, 0.5f, speed01) * carVolumeScale * 2f * categoryVolume;
            }
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
            // Audio-bank architecture: authored loop (slot "vehicle_scrub_loop")
            // replaces this generated fallback when provided.
            AudioClip authored = F1Game.Audio.AudioBankService.Resolve("vehicle_scrub_loop");
            if (authored != null)
            {
                return authored;
            }

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
            // Audio-bank architecture: authored loop (slot "vehicle_engine_loop")
            // replaces this generated fallback when provided.
            AudioClip authored = F1Game.Audio.AudioBankService.Resolve("vehicle_engine_loop");
            if (authored != null)
            {
                return authored;
            }

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
