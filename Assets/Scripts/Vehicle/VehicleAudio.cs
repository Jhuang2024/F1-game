using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(VehicleController))]
    public class VehicleAudio : MonoBehaviour
    {
        VehicleController vehicle;
        AudioSource engineSource;
        int lastGear;
        bool enabledAudio;

        public void Initialize(bool audioEnabled, float volume)
        {
            enabledAudio = audioEnabled;
            vehicle = GetComponent<VehicleController>();
            engineSource = gameObject.AddComponent<AudioSource>();
            engineSource.clip = CreateEngineLoop();
            engineSource.loop = true;
            engineSource.spatialBlend = 1f;
            engineSource.minDistance = 8f;
            engineSource.maxDistance = 95f;
            engineSource.volume = volume;
            if (enabledAudio)
            {
                engineSource.Play();
            }
        }

        void Update()
        {
            if (vehicle == null || engineSource == null)
            {
                return;
            }

            engineSource.mute = !enabledAudio;
            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / 330f);
            engineSource.pitch = Mathf.Lerp(0.65f, 1.95f, speed01) + vehicle.CurrentGear * 0.035f;
            engineSource.volume = Mathf.Lerp(0.18f, 0.5f, speed01);
            if (lastGear != 0 && vehicle.CurrentGear != lastGear)
            {
                SimpleAudioManager.PlayShift(transform.position);
            }

            lastGear = vehicle.CurrentGear;
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
