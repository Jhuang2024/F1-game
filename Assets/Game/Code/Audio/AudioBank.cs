using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1Game.Audio
{
    /// <summary>
    /// Authored audio bank: named clip slots for every sound event the game
    /// raises. Entries left empty fall back to the legacy generated tones —
    /// which are thereby demoted to explicitly-labelled fallback assets, not
    /// content. Slot names double as the keys SimpleAudioManager already passes
    /// to its clip generators, so filling a slot silently upgrades that sound.
    ///
    /// Layered vehicle audio (RPM/load/gear per perspective) uses the dedicated
    /// engine-layer section below rather than one-shot slots.
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/Audio/Audio Bank", fileName = "MainAudioBank")]
    public class AudioBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Slot key, e.g. \"ui click\", \"start light\", \"vehicle_engine_loop\".")]
            public string key;
            public AudioClip clip;
            [Range(0f, 2f)] public float gain = 1f;
        }

        [Serializable]
        public class EngineLayer
        {
            [Tooltip("Loop for this RPM band.")]
            public AudioClip loop;
            [Range(0f, 1f)] public float rpmCenter = 0.5f;
            [Tooltip("Gain reduction when off-throttle (overrun character).")]
            [Range(0f, 1f)] public float offThrottleAttenuation = 0.35f;
        }

        [Header("One-shot and loop slots (UI, race control, pit, surfaces, weather, crowd)")]
        public List<Entry> entries = new List<Entry>();

        [Header("Layered engine audio (external perspective)")]
        public List<EngineLayer> engineExternal = new List<EngineLayer>();

        [Header("Layered engine audio (onboard perspective)")]
        public List<EngineLayer> engineOnboard = new List<EngineLayer>();

        [Header("Vehicle surface loops")]
        public AudioClip tyreScrub;
        public AudioClip lockupSquealAndFlatspot;
        public AudioClip wheelspin;
        public AudioClip kerbRumble;
        public AudioClip gravelRumble;
        public AudioClip windRush;

        Dictionary<string, Entry> index;

        public AudioClip Resolve(string key, out float gain)
        {
            gain = 1f;
            if (index == null)
            {
                index = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (Entry entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.key))
                    {
                        index[entry.key] = entry;
                    }
                }
            }

            if (index.TryGetValue(key, out Entry found) && found.clip != null)
            {
                gain = found.gain;
                return found.clip;
            }

            return null;
        }
    }

    /// <summary>
    /// Runtime access point: loads the main bank from Resources once and
    /// resolves slots. Legacy generators call this first; a null result means
    /// "no authored clip yet, use the labelled procedural fallback".
    /// </summary>
    public static class AudioBankService
    {
        const string BankResourcePath = "Audio/MainAudioBank";

        static AudioBank bank;
        static bool loaded;

        public static AudioBank Bank
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    bank = Resources.Load<AudioBank>(BankResourcePath);
                    if (bank == null)
                    {
                        Debug.Log("[Audio] No authored audio bank at Resources/" + BankResourcePath +
                                  " - all sounds fall back to generated placeholder clips.");
                    }
                }

                return bank;
            }
        }

        public static AudioClip Resolve(string key)
        {
            if (Bank == null)
            {
                return null;
            }

            AudioClip clip = Bank.Resolve(key, out _);
            return clip;
        }
    }
}
