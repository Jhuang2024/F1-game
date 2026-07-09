using UnityEngine;

namespace LocalFormulaRacing
{
    // Drives a purely visual, non-colliding "ghost" car along a previously
    // recorded lap during Time Trial - built once from the same
    // RaceManager.CreateOpenWheelCar geometry every real car uses, then
    // stripped of its Rigidbody/Collider by the spawner (see RaceManager's
    // ghost spawn code) so it can never affect or be affected by physics.
    // This component only ever writes transform.position/rotation directly,
    // the same "kinematic, hand-driven" pattern the pit-guide system already
    // uses elsewhere in this codebase for a car that must move without
    // simulating physics.
    public class GhostCarController : MonoBehaviour
    {
        GhostLapData lap;
        int lastSampleIndex;

        public void Initialize(GhostLapData ghostLap)
        {
            lap = ghostLap;
            lastSampleIndex = 0;
        }

        public bool HasLap
        {
            get { return lap != null && lap.samples != null && lap.samples.Count > 1; }
        }

        public float LapTime
        {
            get { return lap != null ? lap.lapTime : 0f; }
        }

        // Called every frame by RaceManager with the player's current lap
        // elapsed time - looping back to the start once the ghost's own lap
        // time is exceeded, so the ghost keeps circulating even if the
        // player's own lap runs longer (an invalidated/slower lap, a spin).
        public void UpdatePlayback(float elapsedSeconds)
        {
            if (!HasLap)
            {
                return;
            }

            float t = lap.lapTime > 0.01f ? WrapTime(elapsedSeconds, lap.lapTime) : 0f;

            // Small forward-biased linear search from the last resolved index
            // instead of a binary search - playback time only ever advances
            // (or wraps back to 0 once per lap), so consecutive frames almost
            // always resolve in O(1) rather than O(log n).
            if (t < lap.samples[lastSampleIndex].elapsedSeconds)
            {
                lastSampleIndex = 0;
            }

            int index = lastSampleIndex;
            while (index < lap.samples.Count - 2 && lap.samples[index + 1].elapsedSeconds < t)
            {
                index++;
            }

            lastSampleIndex = index;
            GhostSample a = lap.samples[index];
            GhostSample b = lap.samples[Mathf.Min(index + 1, lap.samples.Count - 1)];
            float span = Mathf.Max(0.0001f, b.elapsedSeconds - a.elapsedSeconds);
            float blend = Mathf.Clamp01((t - a.elapsedSeconds) / span);

            transform.position = Vector3.Lerp(a.position, b.position, blend);
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(a.headingDegrees, b.headingDegrees, blend), 0f);
        }

        static float WrapTime(float value, float wrapAt)
        {
            if (wrapAt <= 0.0001f)
            {
                return 0f;
            }

            float wrapped = value % wrapAt;
            return wrapped < 0f ? wrapped + wrapAt : wrapped;
        }

        // Nearest-by-time ghost speed/progress at the given elapsed lap time -
        // used for the HUD delta readout (see RaceManager.GhostDeltaText)
        // without needing the caller to know anything about sample indices.
        public float GhostTimeAtDistance(float distanceAlongLap)
        {
            if (!HasLap)
            {
                return -1f;
            }

            for (int i = 0; i < lap.samples.Count - 1; i++)
            {
                if (lap.samples[i + 1].distanceAlongLap >= distanceAlongLap)
                {
                    GhostSample a = lap.samples[i];
                    GhostSample b = lap.samples[i + 1];
                    float span = Mathf.Max(0.0001f, b.distanceAlongLap - a.distanceAlongLap);
                    float blend = Mathf.Clamp01((distanceAlongLap - a.distanceAlongLap) / span);
                    return Mathf.Lerp(a.elapsedSeconds, b.elapsedSeconds, blend);
                }
            }

            return lap.lapTime;
        }
    }
}
