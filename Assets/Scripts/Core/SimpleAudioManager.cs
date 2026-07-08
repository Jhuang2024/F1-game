using UnityEngine;

namespace LocalFormulaRacing
{
    // Short race-control stingers routed through PostEngineerMessage's cue
    // parameter - kept as a small closed set rather than a free-text lookup so
    // every category the game actually raises (flags, safety car phases, pit
    // calls, damage/penalty warnings, final lap) gets its own distinct tone.
    public enum RaceAudioCue
    {
        None,
        Yellow,
        SafetyCar,
        Vsc,
        Green,
        PitCall,
        PitConfirm,
        Damage,
        Penalty,
        FinalLap,
        RedFlag
    }

    // Central audio hub: one instance for the whole game (DontDestroyOnLoad'd
    // bootstrap parent). Everything here is procedurally generated at startup
    // (AudioClip.Create + a written waveform) since there are no real audio
    // asset files in the project - every Play*/clip field is keyed by a plain
    // string/enum lookup so a real .wav could be swapped in later without
    // touching any call site.
    public class SimpleAudioManager : MonoBehaviour
    {
        static SimpleAudioManager instance;

        AudioSource oneShotSource;   // UI clicks, transitions, pings, flourishes
        AudioSource radioSource;     // race-control cue stingers
        AudioSource collisionSource; // one-shot impact sounds
        AudioSource rainSource;
        AudioSource crowdSource;
        AudioSource windSource;
        AudioSource pitAmbienceSource;

        AudioClip clickClip;
        AudioClip panelTransitionClip;
        AudioClip warningPingClip;
        AudioClip resultsFlourishClip;
        AudioClip shiftClip;
        AudioClip collisionCarClip;
        AudioClip collisionWallClip;
        AudioClip drsClip;
        AudioClip rainClip;
        AudioClip crowdClip;
        AudioClip windClip;
        AudioClip pitAmbienceClip;
        AudioClip[] startLightClips;
        AudioClip[] cueClips;

        bool enabledAudio = true;
        float masterVolume = 0.8f;
        float engineVolumeSetting = 0.8f;
        float uiVolumeSetting = 0.7f;
        float radioVolumeSetting = 0.85f;
        float ambienceVolumeSetting = 0.6f;

        float lastCueTime = -10f;
        const float CueMinGap = 1.15f;
        float pitAmbienceTarget;

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

        // Single entry point for the audio-enabled toggle and every volume
        // category slider - called everywhere the game used to call the old
        // SetEnabled(bool) so a settings change (or a scene transition, which
        // re-reads settings) always keeps every source in sync.
        public static void ApplySettings(GameSettingsData settings)
        {
            if (instance == null || settings == null)
            {
                return;
            }

            instance.enabledAudio = settings.audioEnabled;
            instance.masterVolume = settings.masterVolume;
            instance.engineVolumeSetting = settings.engineVolume;
            instance.uiVolumeSetting = settings.uiVolume;
            instance.radioVolumeSetting = settings.radioVolume;
            instance.ambienceVolumeSetting = settings.ambienceVolume;
            instance.RefreshAmbienceVolumes();
            if (!settings.audioEnabled)
            {
                instance.StopAmbience();
            }
        }

        // Read by VehicleAudio each frame so per-car engine/scrub loops scale
        // with the master and engine category sliders without VehicleAudio
        // needing its own settings reference.
        public static float EngineVolumeScale
        {
            get { return instance == null || !instance.enabledAudio ? 0f : instance.masterVolume * instance.engineVolumeSetting; }
        }

        public static void PlayClick()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.oneShotSource.PlayOneShot(instance.clickClip, 0.22f * instance.UiVolume);
            }
        }

        public static void PlayPanelTransition()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.oneShotSource.PlayOneShot(instance.panelTransitionClip, 0.16f * instance.UiVolume);
            }
        }

        public static void PlayWarningPing()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.oneShotSource.PlayOneShot(instance.warningPingClip, 0.3f * instance.UiVolume);
            }
        }

        public static void PlayResultsFlourish()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.oneShotSource.PlayOneShot(instance.resultsFlourishClip, 0.4f * instance.UiVolume);
            }
        }

        // lightIndex 0-4 for the five build-up lights, 5 for lights-out - pitch
        // rises through the sequence so "lights out" reads as the release.
        public static void PlayStartLight(int lightIndex)
        {
            if (instance == null || !instance.enabledAudio || instance.startLightClips == null)
            {
                return;
            }

            int clamped = Mathf.Clamp(lightIndex, 0, instance.startLightClips.Length - 1);
            instance.oneShotSource.PlayOneShot(instance.startLightClips[clamped], 0.32f * instance.UiVolume);
        }

        public static void PlayShift(Vector3 position)
        {
            if (instance != null && instance.enabledAudio)
            {
                AudioSource.PlayClipAtPoint(instance.shiftClip, position, 0.22f * instance.EngineVolume);
            }
        }

        // impactType distinguishes a car-to-car tap (softer, higher-pitched
        // "clack") from a wall/barrier/solid hit (a heavier, lower thud) so the
        // two read as physically different events instead of one generic bang.
        public static void PlayCollision(Vector3 position, float strength, DamageImpactType impactType)
        {
            if (instance == null || !instance.enabledAudio)
            {
                return;
            }

            AudioClip clip = impactType == DamageImpactType.Car ? instance.collisionCarClip : instance.collisionWallClip;
            float volume = Mathf.Clamp01(strength / 16f) * 0.45f * instance.EngineVolume;
            AudioSource.PlayClipAtPoint(clip, position, volume);
        }

        public static void PlayDrsAvailable()
        {
            if (instance != null && instance.enabledAudio)
            {
                instance.oneShotSource.PlayOneShot(instance.drsClip, 0.18f * instance.UiVolume);
            }
        }

        // Every race-control text callout that matters for atmosphere routes
        // through here via PostEngineerMessage's cue parameter. Throttled by a
        // single shared cooldown so a burst of several messages firing in the
        // same lap (safety car deployment alone posts three) plays one stinger,
        // not a stack of overlapping ones.
        public static void PlayRaceControlCue(RaceAudioCue cue)
        {
            if (instance == null || !instance.enabledAudio || cue == RaceAudioCue.None || instance.cueClips == null)
            {
                return;
            }

            if (Time.time - instance.lastCueTime < CueMinGap)
            {
                return;
            }

            AudioClip clip = instance.cueClips[(int)cue];
            if (clip == null)
            {
                return;
            }

            instance.lastCueTime = Time.time;
            instance.radioSource.PlayOneShot(clip, 0.3f * instance.RadioVolume);
        }

        public static void SetRain(bool active)
        {
            if (instance == null || instance.rainSource == null)
            {
                return;
            }

            if (active && instance.enabledAudio)
            {
                instance.rainSource.volume = 0.2f * instance.AmbienceVolume;
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

        // Race-wide ambience bed (crowd + wind), on at a low, constant level
        // for the duration of a session - deliberately simple rather than
        // event-reactive, per the "keep it simple, a low bed is fine" brief.
        public static void SetRaceAmbience(bool active)
        {
            if (instance == null)
            {
                return;
            }

            if (active && instance.enabledAudio)
            {
                instance.RefreshAmbienceVolumes();
                if (!instance.crowdSource.isPlaying) instance.crowdSource.Play();
                if (!instance.windSource.isPlaying) instance.windSource.Play();
            }
            else
            {
                instance.crowdSource.Stop();
                instance.windSource.Stop();
                instance.pitAmbienceSource.Stop();
            }
        }

        // Smoothly swells in as the player's own car enters the pit lane
        // corridor and fades back out on release - called every frame from
        // RaceManager with the player's current pit-lane state.
        public static void SetPitAmbienceTarget(bool inPitLane)
        {
            if (instance == null)
            {
                return;
            }

            instance.pitAmbienceTarget = inPitLane && instance.enabledAudio ? 0.28f * instance.AmbienceVolume : 0f;
            if (instance.pitAmbienceTarget > 0f && !instance.pitAmbienceSource.isPlaying)
            {
                instance.pitAmbienceSource.Play();
            }
        }

        void Update()
        {
            if (pitAmbienceSource != null)
            {
                pitAmbienceSource.volume = Mathf.MoveTowards(pitAmbienceSource.volume, pitAmbienceTarget, Time.deltaTime * 0.6f);
                if (pitAmbienceSource.volume <= 0.001f && pitAmbienceSource.isPlaying && pitAmbienceTarget <= 0f)
                {
                    pitAmbienceSource.Stop();
                }
            }
        }

        float UiVolume { get { return masterVolume * uiVolumeSetting; } }
        float EngineVolume { get { return masterVolume * engineVolumeSetting; } }
        float RadioVolume { get { return masterVolume * radioVolumeSetting; } }
        float AmbienceVolume { get { return masterVolume * ambienceVolumeSetting; } }

        void RefreshAmbienceVolumes()
        {
            if (crowdSource != null) crowdSource.volume = 0.14f * AmbienceVolume;
            if (windSource != null) windSource.volume = 0.1f * AmbienceVolume;
            if (rainSource != null && rainSource.isPlaying) rainSource.volume = 0.2f * AmbienceVolume;
        }

        void StopAmbience()
        {
            if (rainSource != null) rainSource.Stop();
            if (crowdSource != null) crowdSource.Stop();
            if (windSource != null) windSource.Stop();
            if (pitAmbienceSource != null) pitAmbienceSource.Stop();
        }

        void Initialize()
        {
            oneShotSource = gameObject.AddComponent<AudioSource>();
            oneShotSource.spatialBlend = 0f;
            radioSource = gameObject.AddComponent<AudioSource>();
            radioSource.spatialBlend = 0f;

            clickClip = CreateTone("menu click", 880f, 0.045f, 0.35f);
            panelTransitionClip = CreateSweep("panel transition", 420f, 720f, 0.09f);
            warningPingClip = CreateTone("warning ping", 1180f, 0.16f, 0.25f);
            resultsFlourishClip = CreateChord("results flourish", new[] { 523.25f, 659.25f, 783.99f }, 0.6f);
            shiftClip = CreateTone("gear shift", 520f, 0.07f, 0.6f);
            drsClip = CreateTone("drs available", 1320f, 0.08f, 0.55f);
            collisionCarClip = CreateNoise("car contact", 0.1f, 1600f);
            collisionWallClip = CreateNoise("wall contact", 0.16f, 340f);

            startLightClips = new AudioClip[6];
            for (int i = 0; i < startLightClips.Length; i++)
            {
                startLightClips[i] = CreateTone("start light " + i, Mathf.Lerp(440f, 880f, i / 5f), 0.09f, i == 5 ? 0.5f : 0.2f);
            }

            cueClips = new AudioClip[System.Enum.GetValues(typeof(RaceAudioCue)).Length];
            cueClips[(int)RaceAudioCue.Yellow] = CreateTone("cue yellow", 660f, 0.22f, 0.4f);
            cueClips[(int)RaceAudioCue.SafetyCar] = CreateTone("cue safety car", 392f, 0.3f, 0.5f);
            cueClips[(int)RaceAudioCue.Vsc] = CreateTone("cue vsc", 494f, 0.26f, 0.45f);
            cueClips[(int)RaceAudioCue.Green] = CreateChord("cue green", new[] { 659.25f, 987.77f }, 0.28f);
            cueClips[(int)RaceAudioCue.PitCall] = CreateSweep("cue pit call", 700f, 1100f, 0.14f);
            cueClips[(int)RaceAudioCue.PitConfirm] = CreateTone("cue pit confirm", 900f, 0.12f, 0.3f);
            cueClips[(int)RaceAudioCue.Damage] = CreateTone("cue damage", 220f, 0.3f, 0.6f);
            cueClips[(int)RaceAudioCue.Penalty] = CreateTone("cue penalty", 260f, 0.35f, 0.7f);
            cueClips[(int)RaceAudioCue.FinalLap] = CreateChord("cue final lap", new[] { 523.25f, 783.99f }, 0.35f);
            // Deliberately the most severe-sounding cue in the set (lowest
            // pitch, longest decay, widest dissonant interval) - a red flag is
            // an extreme outlier event and should be unmistakable from every
            // other race-control stinger.
            cueClips[(int)RaceAudioCue.RedFlag] = CreateChord("cue red flag", new[] { 196f, 207.65f }, 0.55f);

            rainClip = CreateSmoothedNoiseLoop("rain ambience", 2f, 0.18f);
            rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.clip = rainClip;
            rainSource.loop = true;
            rainSource.spatialBlend = 0f;
            rainSource.volume = 0.2f;

            crowdClip = CreateSmoothedNoiseLoop("crowd ambience", 3f, 0.05f);
            crowdSource = gameObject.AddComponent<AudioSource>();
            crowdSource.clip = crowdClip;
            crowdSource.loop = true;
            crowdSource.spatialBlend = 0f;
            crowdSource.volume = 0.14f;

            windClip = CreateSmoothedNoiseLoop("wind ambience", 4f, 0.03f);
            windSource = gameObject.AddComponent<AudioSource>();
            windSource.clip = windClip;
            windSource.loop = true;
            windSource.spatialBlend = 0f;
            windSource.volume = 0.1f;

            pitAmbienceClip = CreateSmoothedNoiseLoop("pit lane ambience", 2.5f, 0.12f);
            pitAmbienceSource = gameObject.AddComponent<AudioSource>();
            pitAmbienceSource.clip = pitAmbienceClip;
            pitAmbienceSource.loop = true;
            pitAmbienceSource.spatialBlend = 0f;
            pitAmbienceSource.volume = 0f;
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

        // Linear frequency sweep from startFrequency to endFrequency over the
        // clip's duration - used for panel-transition whooshes and the pit-call
        // stinger so they read as "rising" rather than a flat tone.
        AudioClip CreateSweep(string clipName, float startFrequency, float endFrequency, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float frequency = Mathf.Lerp(startFrequency, endFrequency, t / Mathf.Max(0.001f, duration));
                phase += frequency / sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / duration));
                samples[i] = Mathf.Sin(2f * Mathf.PI * phase) * envelope * 0.45f;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Simple additive chord (used for the green-flag/final-lap/results
        // stingers) so those distinctly "good news" cues sound harmonically
        // richer than the single-tone flag/warning cues.
        AudioClip CreateChord(string clipName, float[] frequencies, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float perNoteGain = 0.4f / Mathf.Max(1, frequencies.Length);
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t / (duration * 0.6f));
                float sum = 0f;
                for (int n = 0; n < frequencies.Length; n++)
                {
                    sum += Mathf.Sin(2f * Mathf.PI * frequencies[n] * t);
                }

                samples[i] = sum * perNoteGain * envelope;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Filtered-noise one-shot (car/wall contact, gravel) - centerFrequency
        // biases the random walk's step size so a "car" hit reads brighter/
        // higher-pitched than a heavier "wall" thud without needing a real FFT
        // filter.
        AudioClip CreateNoise(string clipName, float duration, float centerFrequency)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float smoothed = 0f;
            float smoothing = Mathf.Clamp01(centerFrequency / sampleRate * 8f);
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t * 20f);
                smoothed = Mathf.Lerp(smoothed, Random.Range(-1f, 1f), Mathf.Clamp01(smoothing + 0.35f));
                samples[i] = smoothed * envelope;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Shared smoothed-noise loop generator for every ambience bed (rain,
        // crowd, wind, pit lane) - only the loop length and smoothing factor
        // differ, which is enough to give each one a distinct character
        // (rain hisses faster, wind is slower/deeper, crowd sits in between).
        AudioClip CreateSmoothedNoiseLoop(string clipName, float duration, float smoothing)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float smoothed = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                smoothed = Mathf.Lerp(smoothed, Random.Range(-1f, 1f), smoothing);
                samples[i] = smoothed;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
