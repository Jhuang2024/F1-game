namespace F1Game.Race
{
    /// <summary>
    /// Engine-free replay transport: the play/pause/seek/speed state a replay
    /// camera and scrub bar drive, over a <see cref="ReplayRecording"/>. It owns the
    /// playhead and advances it in real time scaled by the playback speed, clamps to
    /// the recording bounds, stops at the end, and exposes the interpolated car
    /// transform at the current playhead via <see cref="ReplayPlayback"/>. Pure state
    /// logic - the UI only reads NormalizedPlayhead and renders SampleCar; no engine
    /// dependency, so the transport is unit-testable in isolation.
    /// </summary>
    public sealed class ReplayPlaybackController
    {
        const float SpeedMin = 0.05f;
        const float SpeedMax = 16f;
        const float Epsilon = 0.0000001f;

        ReplayRecording recording;

        public float PlayheadSeconds { get; private set; }
        public bool IsPlaying { get; private set; }
        public float SpeedMultiplier { get; private set; } = 1f;

        public bool HasRecording => recording != null && recording.FrameCount > 0;
        public float StartSeconds => HasRecording ? recording.StartTime : 0f;
        public float EndSeconds => HasRecording ? recording.EndTime : 0f;
        public float DurationSeconds => EndSeconds - StartSeconds;
        public bool AtEnd => HasRecording && PlayheadSeconds >= EndSeconds - Epsilon;
        public float NormalizedPlayhead => ReplayPlayback.NormalizedTime(recording, PlayheadSeconds);

        /// <summary>Attach a recording; resets to its start, paused. Null clears.</summary>
        public void SetRecording(ReplayRecording rec)
        {
            recording = rec;
            PlayheadSeconds = rec != null ? rec.StartTime : 0f;
            IsPlaying = false;
        }

        /// <summary>Start playing. From the end, rewinds to the start first (replay again).</summary>
        public void Play()
        {
            if (!HasRecording)
            {
                return;
            }

            if (AtEnd)
            {
                PlayheadSeconds = StartSeconds;
            }

            IsPlaying = true;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void TogglePlay()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        /// <summary>Playback rate multiplier (e.g. 0.25x-4x), clamped to a sane band.</summary>
        public void SetSpeed(float multiplier)
        {
            SpeedMultiplier = multiplier < SpeedMin ? SpeedMin : (multiplier > SpeedMax ? SpeedMax : multiplier);
        }

        /// <summary>Jump the playhead to an absolute time, clamped to the recording.</summary>
        public void Seek(float seconds)
        {
            if (!HasRecording)
            {
                PlayheadSeconds = 0f;
                return;
            }

            float start = StartSeconds;
            float end = EndSeconds;
            PlayheadSeconds = seconds < start ? start : (seconds > end ? end : seconds);
        }

        /// <summary>Jump the playhead to a 0-1 scrub-bar position.</summary>
        public void SeekNormalized(float normalized)
        {
            Seek(ReplayPlayback.TimeForNormalized(recording, normalized));
        }

        /// <summary>Rewind to the start (keeps the play/pause state).</summary>
        public void Restart()
        {
            PlayheadSeconds = StartSeconds;
        }

        /// <summary>
        /// Advance the playhead by one frame of real time (seconds) scaled by the
        /// playback speed, when playing. Stops and pauses at the end. A non-positive
        /// delta or a paused/empty transport is a no-op.
        /// </summary>
        public void Advance(float realDeltaSeconds)
        {
            if (!IsPlaying || !HasRecording || realDeltaSeconds <= 0f)
            {
                return;
            }

            PlayheadSeconds += realDeltaSeconds * SpeedMultiplier;
            if (PlayheadSeconds >= EndSeconds)
            {
                PlayheadSeconds = EndSeconds;
                IsPlaying = false;
            }
        }

        /// <summary>The interpolated transform of a car at the current playhead.</summary>
        public ReplayRecording.CarFrame SampleCar(int carIndex)
        {
            return ReplayPlayback.SampleCar(recording, PlayheadSeconds, carIndex);
        }
    }
}
