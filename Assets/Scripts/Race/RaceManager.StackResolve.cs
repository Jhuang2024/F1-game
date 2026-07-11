using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager low-speed stack-resolution subsystem (partial). A gentle
    /// anti-pile pass: when two active cars end up nearly stationary and
    /// overlapping (turn-one scrums, restart concertinas), ease them apart along
    /// track-right instead of letting physics grind them together - damage-free and
    /// far too small to launch a car, clamped inside the road surface. Split out of
    /// the RaceManager monolith verbatim - same class, same members, identical
    /// overlap/speed thresholds, nudge magnitude and call order; callers resolve
    /// in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Gentle anti-pile pass: when two active cars end up nearly stationary and
        // overlapping (turn-one scrums, restart concertinas), ease them apart along
        // track-right instead of letting physics grind them together. The nudge is
        // damage-free and far too small to launch a car.
        void ResolveLowSpeedStacks()
        {
            stackResolveTimer -= Time.deltaTime;
            if (stackResolveTimer > 0f)
            {
                return;
            }

            stackResolveTimer = 0.12f;
            const float overlapDistance = 3.4f;
            const float maxSpeedKph = 34f;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant a = Participants[i];
                if (!IsStackResolveCandidate(a))
                {
                    continue;
                }

                for (int j = i + 1; j < Participants.Count; j++)
                {
                    RaceParticipant b = Participants[j];
                    if (!IsStackResolveCandidate(b))
                    {
                        continue;
                    }

                    Vector3 delta = b.transform.position - a.transform.position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > overlapDistance * overlapDistance)
                    {
                        continue;
                    }

                    if (Mathf.Abs(a.vehicle.CurrentSpeedKph) > maxSpeedKph || Mathf.Abs(b.vehicle.CurrentSpeedKph) > maxSpeedKph)
                    {
                        continue;
                    }

                    TrackProgress progress = Track.GetProgress(a.transform.position);
                    Vector3 trackRight = Vector3.Cross(Vector3.up, progress.forward).normalized;
                    float side = Vector3.Dot(delta, trackRight);
                    if (Mathf.Abs(side) < 0.05f)
                    {
                        side = (i + j) % 2 == 0 ? 1f : -1f;
                    }

                    Vector3 separation = trackRight * Mathf.Sign(side) * 0.55f;
                    NudgeStackedCar(a, -separation, progress);
                    NudgeStackedCar(b, separation, progress);
                }
            }
        }

        bool IsStackResolveCandidate(RaceParticipant participant)
        {
            return participant != null &&
                   participant.vehicle != null &&
                   !participant.retired &&
                   !participant.finished &&
                   !participant.isPitting &&
                   participant.pitPhase == PitPhase.None &&
                   !participant.vehicle.IsHeldOnGrid &&
                   !participant.vehicle.IsPitGuided &&
                   participant.gameObject.activeSelf;
        }

        void NudgeStackedCar(RaceParticipant participant, Vector3 separation, TrackProgress reference)
        {
            // Never push a car off the road; clamp the nudge inside the surface.
            Vector3 target = participant.transform.position + separation;
            TrackProgress targetProgress = Track.GetProgress(target);
            if (Mathf.Abs(targetProgress.lateralDistance) > LocalHalfWidthAt(targetProgress.distance) - 1.2f)
            {
                return;
            }

            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
            {
                return;
            }

            body.position = target;
            Vector3 velocity = body.velocity;
            velocity.x *= 0.9f;
            velocity.z *= 0.9f;
            body.velocity = velocity;
        }

    }
}
