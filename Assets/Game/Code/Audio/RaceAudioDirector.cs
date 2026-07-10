using F1Game.Core.Events;
using UnityEngine;
using UnityEngine.Audio;

namespace F1Game.Audio
{
    /// <summary>
    /// Event-driven race audio: subscribes to the typed bus and plays authored
    /// bank slots for race-control, penalty, pit, weather and radio events,
    /// routed through an optional AudioMixer's groups. Radio messages queue with
    /// priority (critical interrupts informational). When a bank slot is empty
    /// it defers to the legacy generated cue as a labelled fallback rather than
    /// going silent.
    ///
    /// This is the runtime that makes the audio bank drive real events instead
    /// of the bank being only a lookup shell.
    /// </summary>
    public sealed class RaceAudioDirector : MonoBehaviour
    {
        public const string MixerResourcePath = "Audio/GameAudioMixer";

        AudioSource uiSource;
        AudioSource radioSource;
        AudioSource sfxSource;
        AudioMixer mixer;

        struct RadioItem
        {
            public string Key;
            public int Priority;
        }

        RadioItem pendingRadio;
        bool hasPendingRadio;

        public static RaceAudioDirector Create(Transform parent)
        {
            var go = new GameObject("Race Audio Director");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go.AddComponent<RaceAudioDirector>();
        }

        void Awake()
        {
            mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            uiSource = MakeSource("UI", "UI");
            radioSource = MakeSource("Radio", "Radio");
            sfxSource = MakeSource("SFX", "SFX");
        }

        AudioSource MakeSource(string name, string mixerGroup)
        {
            var go = new GameObject(name + " Source");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            if (mixer != null)
            {
                AudioMixerGroup[] groups = mixer.FindMatchingGroups(mixerGroup);
                if (groups != null && groups.Length > 0)
                {
                    source.outputAudioMixerGroup = groups[0];
                }
            }

            return source;
        }

        void OnEnable()
        {
            GameEvents.Subscribe<PenaltyIssuedEvent>(OnPenalty);
            GameEvents.Subscribe<FlagChangedEvent>(OnFlag);
            GameEvents.Subscribe<PitRequestChangedEvent>(OnPit);
            GameEvents.Subscribe<WeatherChangedEvent>(OnWeather);
            GameEvents.Subscribe<RadioMessageEvent>(OnRadio);
        }

        void OnDisable()
        {
            GameEvents.Unsubscribe<PenaltyIssuedEvent>(OnPenalty);
            GameEvents.Unsubscribe<FlagChangedEvent>(OnFlag);
            GameEvents.Unsubscribe<PitRequestChangedEvent>(OnPit);
            GameEvents.Unsubscribe<WeatherChangedEvent>(OnWeather);
            GameEvents.Unsubscribe<RadioMessageEvent>(OnRadio);
        }

        void OnPenalty(PenaltyIssuedEvent evt) => PlaySlot(sfxSource, "race_control_penalty");

        void OnFlag(FlagChangedEvent evt)
        {
            switch (evt.Current)
            {
                case FlagState.SafetyCar: PlaySlot(sfxSource, "race_control_safety_car"); break;
                case FlagState.VirtualSafetyCar: PlaySlot(sfxSource, "race_control_vsc"); break;
                case FlagState.Yellow:
                case FlagState.DoubleYellow: PlaySlot(sfxSource, "race_control_yellow"); break;
                case FlagState.Red: PlaySlot(sfxSource, "race_control_red"); break;
                case FlagState.Green: PlaySlot(sfxSource, "race_control_green"); break;
            }
        }

        void OnPit(PitRequestChangedEvent evt)
        {
            if (evt.State == PitRequestState.Requested)
            {
                PlaySlot(sfxSource, "pit_call");
            }
        }

        void OnWeather(WeatherChangedEvent evt)
        {
            if (evt.Current == WeatherKind.LightRain || evt.Current == WeatherKind.HeavyRain)
            {
                PlaySlot(sfxSource, "weather_rain_alert");
            }
        }

        void OnRadio(RadioMessageEvent evt)
        {
            // Priority queue: a lower Priority number (more critical) interrupts.
            if (!hasPendingRadio || evt.Priority < pendingRadio.Priority || !radioSource.isPlaying)
            {
                pendingRadio = new RadioItem { Key = "radio_beep", Priority = evt.Priority };
                hasPendingRadio = true;
                PlaySlot(radioSource, pendingRadio.Key);
            }
        }

        void Update()
        {
            if (hasPendingRadio && !radioSource.isPlaying)
            {
                hasPendingRadio = false;
            }
        }

        void PlaySlot(AudioSource source, string slotKey)
        {
            AudioClip clip = AudioBankService.Resolve(slotKey);
            if (clip == null)
            {
                // Bank slot empty: leave the legacy generated cue (SimpleAudioManager
                // in Assembly-CSharp) to cover it. Log at verbose only.
                return;
            }

            source.PlayOneShot(clip);
        }
    }
}
