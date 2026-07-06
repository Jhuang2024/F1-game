using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(VehicleController))]
    public class AiVehicleController : MonoBehaviour
    {
        public RaceManager raceManager;
        public RaceParticipant participant;

        VehicleController vehicle;
        TrackRuntime track;
        float lateralOffset;
        float mistakeTimer;
        float mistakeSteer;
        float aggressionOffset;
        float damageDecisionTimer;
        float lastProgressDistance;
        bool hasProgressReference;

        public void Initialize(RaceManager manager, RaceParticipant raceParticipant, TrackRuntime raceTrack)
        {
            raceManager = manager;
            participant = raceParticipant;
            track = raceTrack;
            vehicle = GetComponent<VehicleController>();
            lateralOffset = Random.Range(-0.8f, 0.8f);
            mistakeTimer = Random.Range(3f, 8f);
            hasProgressReference = false;
        }

        void Update()
        {
            if (vehicle == null || track == null || raceManager == null || raceManager.IsPaused || raceManager.IsRaceFinished)
            {
                return;
            }

            if (participant != null && participant.retired)
            {
                vehicle.SetCommand(new VehicleCommand { brake = 1f });
                return;
            }

            if (!raceManager.CanDrive)
            {
                vehicle.SetCommand(new VehicleCommand { brake = 1f });
                return;
            }

            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.RaceElapsed < participant.startReactionDelay)
            {
                vehicle.SetCommand(new VehicleCommand { brake = 1f });
                return;
            }

            // Continuity-aware progress lookup so the AI never snaps to the wrong part of
            // the track near the start/finish wrap or where sections run close together.
            TrackProgress progress = hasProgressReference
                ? track.GetProgressNear(transform.position, lastProgressDistance)
                : track.GetProgress(transform.position);
            lastProgressDistance = progress.distance;
            hasProgressReference = true;
            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            DriverData driver = participant == null ? null : participant.driverData;
            int pace = driver == null ? 80 : (raceManager.CurrentSession == RaceWeekendSession.Qualifying ? driver.qualifying : driver.pace);
            int racecraft = driver == null ? 80 : driver.racecraft;
            int consistency = driver == null ? 80 : driver.consistency;
            int aggression = driver == null ? 75 : driver.aggression;
            int tyreManagement = driver == null ? 80 : driver.tyreManagement;
            int defending = driver == null ? 78 : driver.defending;
            int overtaking = driver == null ? 78 : driver.overtaking;

            float cornerSeverity = EstimateCornerSeverity(progress.distance);
            // Look further ahead with speed, but shorten in corners so the AI hits apexes
            // instead of cutting across them.
            float lookAhead = Mathf.Lerp(20f, 54f, Mathf.Clamp01(speedKph / 350f)) * Mathf.Lerp(1.12f, 0.62f, cornerSeverity);
            Vector3 targetPoint;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(progress.distance + lookAhead, out targetPoint, out forward, out right);

            float difficultyPace = raceManager.GetDifficultyPaceMultiplier();
            float carTopSpeed = vehicle.CarData == null || vehicle.CarData.topSpeed <= 0 ? 337f : vehicle.CarData.topSpeed;
            float straightTargetSpeed = Mathf.Clamp(carTopSpeed + 12f, 330f, 350f);
            if (raceManager.IsDrsAvailable(participant))
            {
                straightTargetSpeed = Mathf.Min(350f, straightTargetSpeed + 5f);
            }

            float baseTargetSpeed = Mathf.Lerp(straightTargetSpeed, 98f, cornerSeverity);
            baseTargetSpeed *= Mathf.Lerp(0.92f, 1.08f, pace / 100f);
            baseTargetSpeed *= Mathf.Lerp(0.96f, 1.04f, racecraft / 100f);
            baseTargetSpeed *= raceManager.CurrentSession == RaceWeekendSession.Qualifying ? 1.025f : 1f;
            baseTargetSpeed *= difficultyPace;
            baseTargetSpeed *= vehicle.Tyres.GripMultiplier(track.weather);
            float damagePercent = vehicle.Damage == null ? 0f : vehicle.Damage.OverallPercent;
            baseTargetSpeed *= AiDamagePaceMultiplier(damagePercent);

            UpdateMistake(consistency, aggression);
            UpdateOvertakeOffset(progress, cornerSeverity, aggression, defending, overtaking);

            // Off-track recovery: drive straight back toward the centerline at reduced pace
            // instead of chasing the racing line offset from the grass.
            bool offTrack = Mathf.Abs(progress.lateralDistance) > track.roadHalfWidth + 0.6f;
            if (offTrack)
            {
                baseTargetSpeed = Mathf.Min(baseTargetSpeed, 118f);
                aggressionOffset = 0f;
                mistakeSteer = 0f;
            }

            float desiredOffset = offTrack ? 0f : ConstrainLegalLineOffset(progress, lateralOffset + aggressionOffset + mistakeSteer, cornerSeverity);
            targetPoint += right * desiredOffset;
            TrackProgress targetProgress = track.GetProgress(targetPoint);
            float legalTargetLimit = LegalOffsetLimit(cornerSeverity);
            if (Mathf.Abs(targetProgress.lateralDistance) > legalTargetLimit)
            {
                track.SampleAtDistance(targetProgress.distance, out targetPoint, out forward, out right);
                targetPoint += right * Mathf.Clamp(targetProgress.lateralDistance, -legalTargetLimit, legalTargetLimit);
            }

            Vector3 toTarget = targetPoint - transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, transform.right);

            VehicleCommand command = new VehicleCommand();
            float edgeRecovery = Mathf.Abs(progress.lateralDistance) > track.roadHalfWidth - 1.2f ? Mathf.Sign(-progress.lateralDistance) * 0.45f : 0f;
            command.steer = Mathf.Clamp(localSteer * 2.2f + edgeRecovery, -1f, 1f);
            float brakeMargin = Mathf.Lerp(8f, 22f, raceManager.GetDifficultyBrakeMargin());
            if (speedKph > baseTargetSpeed + brakeMargin)
            {
                command.brake = Mathf.Clamp01((speedKph - baseTargetSpeed) / 55f);
            }
            else
            {
                command.throttle = Mathf.Clamp01((baseTargetSpeed - speedKph) / 62f + 0.34f);
            }

            // Calmer opening seconds: keep a small throttle cap so the pack fans out into
            // turn one instead of piling into the leaders.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.RaceElapsed < 3.5f)
            {
                command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(0.72f, 1f, raceManager.RaceElapsed / 3.5f));
            }

            ApplyTrafficAvoidance(ref command, progress, speedKph);

            float tyrePitThreshold = Mathf.Lerp(0.68f, 0.52f, tyreManagement / 100f);
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                vehicle.Tyres.Wear < tyrePitThreshold &&
                participant.lapTracker.CompletedLaps > 0)
            {
                command.pitRequest = true;
            }

            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                participant.pitStops == 0 &&
                participant.lapTracker.CompletedLaps >= raceManager.RecommendedPitLap(participant))
            {
                command.pitRequest = true;
            }

            ApplyDamageStrategy(ref command, damagePercent);

            command.ers = raceManager.ShouldAiUseErs(participant, cornerSeverity);
            command.drs = raceManager.IsDrsAvailable(participant);
            vehicle.SetCommand(command);
        }

        float AiDamagePaceMultiplier(float damagePercent)
        {
            if (damagePercent < 8f)
            {
                return 1f;
            }

            if (damagePercent < 28f)
            {
                return Mathf.Lerp(0.99f, 0.94f, Mathf.InverseLerp(8f, 28f, damagePercent));
            }

            if (damagePercent < 58f)
            {
                return Mathf.Lerp(0.94f, 0.82f, Mathf.InverseLerp(28f, 58f, damagePercent));
            }

            return Mathf.Lerp(0.82f, 0.62f, Mathf.InverseLerp(58f, 92f, damagePercent));
        }

        void ApplyDamageStrategy(ref VehicleCommand command, float damagePercent)
        {
            if (raceManager.CurrentSession == RaceWeekendSession.Qualifying || participant == null || participant.lapTracker == null)
            {
                return;
            }

            if (damagePercent >= 42f && participant.pitStops == 0)
            {
                command.pitRequest = true;
            }

            damageDecisionTimer -= Time.deltaTime;
            if (damageDecisionTimer > 0f)
            {
                return;
            }

            damageDecisionTimer = Random.Range(2.5f, 5.5f);
            if (damagePercent >= 92f || (damagePercent >= 78f && Random.value < 0.42f))
            {
                raceManager.RetireParticipant(participant, "Damage");
            }
        }

        void ApplyTrafficAvoidance(ref VehicleCommand command, TrackProgress progress, float speedKph)
        {
            float brakeDemand = 0f;
            float throttleLimit = 1f;
            float steerAdjust = 0f;
            for (int i = 0; i < raceManager.Participants.Count; i++)
            {
                RaceParticipant other = raceManager.Participants[i];
                if (other == null || other == participant || other.retired || other.vehicle == null)
                {
                    continue;
                }

                Vector3 local = transform.InverseTransformPoint(other.transform.position);
                float absX = Mathf.Abs(local.x);
                if (local.z > -5f && local.z < 20f && absX < 7.4f)
                {
                    float overlap = Mathf.Clamp01(1f - absX / 7.4f);
                    if (local.z > 0f)
                    {
                        float closing = Mathf.Clamp01((20f - local.z) / 20f) * overlap;
                        brakeDemand = Mathf.Max(brakeDemand, Mathf.Lerp(0.08f, 0.72f, closing));
                        throttleLimit = Mathf.Min(throttleLimit, Mathf.Lerp(1f, 0.38f, closing));
                    }

                    if (absX < 3.8f && Mathf.Abs(local.z) < 9.5f)
                    {
                        float side = Mathf.Abs(local.x) < 0.05f ? Mathf.Sign(Mathf.Sin(progress.distance * 0.07f + Time.time)) : -Mathf.Sign(local.x);
                        float sideOverlap = Mathf.Clamp01(1f - absX / 3.8f);
                        steerAdjust += side * Mathf.Lerp(0.06f, 0.28f, sideOverlap) * Mathf.Lerp(0.8f, 1.15f, Mathf.InverseLerp(60f, 240f, speedKph));
                        throttleLimit = Mathf.Min(throttleLimit, Mathf.Lerp(1f, 0.62f, sideOverlap));
                    }
                }
            }

            if (brakeDemand > 0f)
            {
                command.brake = Mathf.Max(command.brake, brakeDemand);
                command.throttle = Mathf.Min(command.throttle, throttleLimit);
            }
            else
            {
                command.throttle = Mathf.Min(command.throttle, throttleLimit);
            }

            command.steer = Mathf.Clamp(command.steer + steerAdjust, -1f, 1f);
        }

        float EstimateCornerSeverity(float distance)
        {
            Vector3 pointA;
            Vector3 forwardA;
            Vector3 rightA;
            Vector3 pointB;
            Vector3 forwardB;
            Vector3 rightB;
            track.SampleAtDistance(distance + 16f, out pointA, out forwardA, out rightA);
            track.SampleAtDistance(distance + 58f, out pointB, out forwardB, out rightB);
            return Mathf.Clamp01(Vector3.Angle(forwardA, forwardB) / 78f);
        }

        void UpdateMistake(int consistency, int aggression)
        {
            mistakeTimer -= Time.deltaTime;
            if (mistakeTimer > 0f)
            {
                mistakeSteer = Mathf.MoveTowards(mistakeSteer, 0f, Time.deltaTime * 0.8f);
                return;
            }

            RaceDifficulty difficulty = raceManager.Settings.Difficulty;
            float baseChance = difficulty == RaceDifficulty.Easy ? 0.08f : difficulty == RaceDifficulty.Medium ? 0.045f : difficulty == RaceDifficulty.Hard ? 0.025f : 0.012f;
            float consistencyPenalty = Mathf.Lerp(1.7f, 0.35f, consistency / 100f);
            float aggressionPenalty = Mathf.Lerp(0.85f, 1.35f, aggression / 100f);
            if (Random.value < baseChance * consistencyPenalty * aggressionPenalty)
            {
                mistakeSteer = Random.Range(-0.9f, 0.9f);
                mistakeTimer = Random.Range(0.5f, 1.2f);
            }
            else
            {
                mistakeTimer = Random.Range(3f, 8f);
            }
        }

        void UpdateOvertakeOffset(TrackProgress progress, float cornerSeverity, int aggression, int defending, int overtaking)
        {
            RaceParticipant ahead = raceManager.FindCarAhead(participant, 44f);
            RaceParticipant behind = raceManager.FindCarBehind(participant, 30f);
            float targetOffset = 0f;
            float straightBias = Mathf.Lerp(1f, 0.28f, cornerSeverity);
            if (ahead == null || ahead.vehicle == null)
            {
                targetOffset = 0f;
            }
            else
            {
                float side = Mathf.Sign(Mathf.Sin((progress.distance + aggression * 7f) * 0.03f));
                float amount = Mathf.Lerp(2.6f, 6.2f, Mathf.Clamp01((aggression + overtaking) / 200f)) * straightBias;
                targetOffset = side * amount;
            }

            if (behind != null && behind.vehicle != null)
            {
                float blockSide = Mathf.Sign(Vector3.Dot(behind.transform.position - transform.position, transform.right));
                float defendAmount = Mathf.Lerp(1.5f, 4.8f, Mathf.Clamp01((defending + aggression) / 200f)) * Mathf.Lerp(1f, 0.45f, cornerSeverity);
                targetOffset += blockSide * defendAmount;
            }

            float legalLimit = LegalOffsetLimit(cornerSeverity);
            aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(targetOffset, -legalLimit, legalLimit), Time.deltaTime * 5.2f);
        }

        float ConstrainLegalLineOffset(TrackProgress progress, float requestedOffset, float cornerSeverity)
        {
            float legalLimit = LegalOffsetLimit(cornerSeverity);
            float turnSign = EstimateTurnDirection(progress.distance);
            float desired = Mathf.Clamp(requestedOffset, -legalLimit, legalLimit);
            if (Mathf.Abs(turnSign) > 0.01f && cornerSeverity > 0.18f)
            {
                float insideLimit = Mathf.Lerp(legalLimit, legalLimit * 0.42f, cornerSeverity);
                if (turnSign > 0f)
                {
                    desired = Mathf.Clamp(desired, -legalLimit, insideLimit);
                }
                else
                {
                    desired = Mathf.Clamp(desired, -insideLimit, legalLimit);
                }
            }

            if (Mathf.Abs(progress.lateralDistance) > track.roadHalfWidth - 1.6f)
            {
                desired = Mathf.MoveTowards(desired, 0f, Mathf.Lerp(2.2f, 5.2f, cornerSeverity));
            }

            return desired;
        }

        float LegalOffsetLimit(float cornerSeverity)
        {
            float margin = Mathf.Lerp(1.8f, 3.1f, cornerSeverity);
            float kerbLimit = track.kerbStart > 0f ? track.kerbStart - 0.8f : track.roadHalfWidth - margin;
            return Mathf.Max(0.75f, Mathf.Min(track.roadHalfWidth - margin, kerbLimit));
        }

        float EstimateTurnDirection(float distance)
        {
            Vector3 pointA;
            Vector3 forwardA;
            Vector3 rightA;
            Vector3 pointB;
            Vector3 forwardB;
            Vector3 rightB;
            track.SampleAtDistance(distance + 12f, out pointA, out forwardA, out rightA);
            track.SampleAtDistance(distance + 48f, out pointB, out forwardB, out rightB);
            return Mathf.Sign(Vector3.Cross(forwardA, forwardB).y);
        }
    }
}
