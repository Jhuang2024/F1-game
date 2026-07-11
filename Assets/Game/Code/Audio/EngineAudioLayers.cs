using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Audio
{
    /// <summary>
    /// Layered engine audio: crossfades a set of RPM-band loops (from the audio
    /// bank) by engine RPM, applies throttle on/off-throttle attenuation, and
    /// switches the layer set between external and onboard perspective. This is
    /// the production engine-audio path; it activates when the bank supplies
    /// engine-layer clips and otherwise leaves the legacy generated loop in
    /// place as the labelled fallback.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class EngineAudioLayers : MonoBehaviour
    {
        struct LiveLayer
        {
            public AudioSource Source;
            public float RpmCenter;
            public float OffThrottleAttenuation;
        }

        readonly List<LiveLayer> layers = new List<LiveLayer>();
        bool onboard;
        float masterVolume = 1f;

        public bool HasLayers => layers.Count > 0;

        /// <summary>Builds the layer voices from the bank for the given perspective.</summary>
        public bool Initialize(AudioBank bank, bool onboardPerspective, float minDistance = 8f, float maxDistance = 95f)
        {
            Clear();
            onboard = onboardPerspective;
            if (bank == null)
            {
                return false;
            }

            List<AudioBank.EngineLayer> defs = onboard ? bank.engineOnboard : bank.engineExternal;
            if (defs == null || defs.Count == 0)
            {
                return false;
            }

            foreach (AudioBank.EngineLayer def in defs)
            {
                if (def.loop == null)
                {
                    continue;
                }

                var go = new GameObject("EngineLayer_" + def.rpmCenter.ToString("0.00"));
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.clip = def.loop;
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = onboard ? 0f : 1f;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.volume = 0f;
                source.Play();

                layers.Add(new LiveLayer
                {
                    Source = source,
                    RpmCenter = Mathf.Clamp01(def.rpmCenter),
                    OffThrottleAttenuation = Mathf.Clamp01(def.offThrottleAttenuation),
                });
            }

            return layers.Count > 0;
        }

        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
        }

        /// <summary>Drive the layer mix. rpm01 0..1, throttle 0..1.</summary>
        public void Tick(float rpm01, float throttle01, float pitchScale = 1f)
        {
            if (layers.Count == 0)
            {
                return;
            }

            rpm01 = Mathf.Clamp01(rpm01);
            float offThrottle = F1Game.Core.EngineAudioMix.OffThrottle(throttle01);

            for (int i = 0; i < layers.Count; i++)
            {
                LiveLayer layer = layers[i];
                // Triangular crossfade weight around each band centre (see EngineAudioMix).
                float weight = F1Game.Core.EngineAudioMix.LayerWeight(rpm01, layer.RpmCenter, layers.Count);
                layer.Source.volume = F1Game.Core.EngineAudioMix.LayerVolume(
                    weight, offThrottle, layer.OffThrottleAttenuation, masterVolume);
                layer.Source.pitch = F1Game.Core.EngineAudioMix.LayerPitch(rpm01, pitchScale);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].Source != null)
                {
                    Destroy(layers[i].Source.gameObject);
                }
            }

            layers.Clear();
        }

        void OnDestroy()
        {
            Clear();
        }
    }
}
