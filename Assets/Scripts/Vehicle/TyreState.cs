using UnityEngine;

namespace LocalFormulaRacing
{
    public class TyreState
    {
        public TyreCompound Compound { get; private set; }
        public float Wear { get; private set; }
        public float Temperature { get; private set; }
        public bool IsLocked { get; private set; }

        float targetMin;
        float targetMax;
        float baseGrip;
        float baseWear;
        float warmup;
        float wetPerformance;
        float lastGripMultiplier = 1f;

        public void Reset(TyreCompound compound)
        {
            Compound = compound;
            Wear = 1f;
            IsLocked = false;

            if (compound == TyreCompound.Soft)
            {
                baseGrip = 1.08f;
                baseWear = 1.22f;
                targetMin = 82f;
                targetMax = 105f;
                warmup = 1.25f;
                wetPerformance = 0.42f;
                Temperature = 78f;
            }
            else if (compound == TyreCompound.Medium)
            {
                baseGrip = 1f;
                baseWear = 1f;
                targetMin = 78f;
                targetMax = 102f;
                warmup = 1f;
                wetPerformance = 0.45f;
                Temperature = 74f;
            }
            else if (compound == TyreCompound.Hard)
            {
                baseGrip = 0.94f;
                baseWear = 0.72f;
                targetMin = 74f;
                targetMax = 100f;
                warmup = 0.78f;
                wetPerformance = 0.48f;
                Temperature = 68f;
            }
            else if (compound == TyreCompound.Intermediate)
            {
                baseGrip = 0.88f;
                baseWear = 1.08f;
                targetMin = 58f;
                targetMax = 82f;
                warmup = 1.05f;
                wetPerformance = 0.9f;
                Temperature = 58f;
            }
            else
            {
                baseGrip = 0.78f;
                baseWear = 1.18f;
                targetMin = 45f;
                targetMax = 70f;
                warmup = 1.1f;
                wetPerformance = 1f;
                Temperature = 48f;
            }
        }

        public void Tick(float speedKph, float brake, float steer, float throttle, float slipEnergy, WeatherState weather, int tyreManagement, float deltaTime)
        {
            float speedHeat = speedKph / 310f;
            float brakeHeat = brake * brake * Mathf.Lerp(0.72f, 1.45f, Mathf.InverseLerp(80f, 270f, speedKph));
            float steerHeat = Mathf.Abs(steer) * speedHeat * 0.72f;
            float tractionHeat = throttle * slipEnergy * 0.58f;
            float slidingHeat = slipEnergy * speedHeat * 1.05f;
            float ambient = weather == WeatherState.HeavyRain ? 34f : (weather == WeatherState.LightRain ? 44f : 64f);
            float heatGain = (speedHeat + brakeHeat + steerHeat + tractionHeat + slidingHeat) * warmup;
            float targetTemperature = ambient + heatGain * 9.2f;
            float cooling = speedKph < 75f && throttle < 0.25f ? 1.55f : 1f;
            Temperature = Mathf.MoveTowards(Temperature, targetTemperature, deltaTime * (2.45f + heatGain * 3.1f) * cooling);

            IsLocked = brake > 0.84f && speedKph > 105f && Random.value < deltaTime * Mathf.Lerp(0.65f, 1.25f, Mathf.Clamp01(1f - TemperatureWindowScore));
            float management = Mathf.Lerp(1.35f, 0.72f, Mathf.Clamp01(tyreManagement / 100f));
            float weatherWear = weather == WeatherState.Clear || weather == WeatherState.Cloudy ? 1.04f : 1.24f;
            float lockupWear = IsLocked ? 0.024f : 0f;
            float overheatWear = Mathf.Lerp(1f, 2.15f, Mathf.InverseLerp(targetMax, targetMax + 22f, Temperature));
            float slideWear = slipEnergy * 0.00165f;
            float baselineWear = speedHeat * 0.00124f + Mathf.Abs(steer) * 0.00068f + brake * 0.00058f + slideWear;
            float wearLoss = (baselineWear * baseWear * management * weatherWear * overheatWear) + lockupWear;
            Wear = Mathf.Clamp01(Wear - wearLoss * deltaTime);
        }

        public float GripMultiplier(WeatherState weather)
        {
            float tempGrip = TemperatureGripMultiplier;
            // Aggressive wear drop: Significant linear drop followed by a steep cliff.
            float wearGrip = Wear > 0.65f ? Mathf.Lerp(0.82f, 1f, (Wear - 0.65f) / 0.35f) :
                             (Wear > 0.35f ? Mathf.Lerp(0.55f, 0.82f, (Wear - 0.35f) / 0.30f) :
                                             Mathf.Lerp(0.12f, 0.55f, Wear / 0.35f));
            float rainGrip = 1f;
            if (weather == WeatherState.LightRain)
            {
                rainGrip = Mathf.Lerp(0.56f, 0.95f, wetPerformance);
            }
            else if (weather == WeatherState.HeavyRain)
            {
                rainGrip = Mathf.Lerp(0.34f, 0.92f, wetPerformance);
            }

            float lockupGrip = IsLocked ? 0.82f : 1f;
            lastGripMultiplier = baseGrip * tempGrip * wearGrip * rainGrip * lockupGrip;
            return lastGripMultiplier;
        }

        public float TemperatureGripMultiplier
        {
            get
            {
                if (Temperature < targetMin)
                {
                    return Mathf.Lerp(0.66f, 1f, Mathf.InverseLerp(35f, targetMin, Temperature));
                }

                if (Temperature > targetMax)
                {
                    if (Temperature <= targetMax + 8f)
                    {
                        return 1f;
                    }

                    return Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(targetMax + 8f, targetMax + 46f, Temperature));
                }

                float center = (targetMin + targetMax) * 0.5f;
                float halfWindow = Mathf.Max(1f, (targetMax - targetMin) * 0.5f);
                return Mathf.Lerp(1.03f, 1.08f, 1f - Mathf.Clamp01(Mathf.Abs(Temperature - center) / halfWindow));
            }
        }

        public float BrakingMultiplier
        {
            get { return Mathf.Lerp(0.68f, 1.12f, TemperatureWindowScore) * (Wear > 0.5f ? Mathf.Lerp(0.72f, 1f, Wear) : Mathf.Lerp(0.35f, 0.72f, Wear / 0.5f)); }
        }

        public float TractionMultiplier
        {
            get { return Mathf.Lerp(0.60f, 1.08f, TemperatureWindowScore) * (Wear > 0.5f ? Mathf.Lerp(0.68f, 1f, Wear) : Mathf.Lerp(0.28f, 0.68f, Wear / 0.5f)); }
        }

        public float TemperatureWindowScore
        {
            get
            {
                if (Temperature >= targetMin && Temperature <= targetMax)
                {
                    return 1f;
                }

                if (Temperature < targetMin)
                {
                    return Mathf.InverseLerp(targetMin - 42f, targetMin, Temperature);
                }

                if (Temperature <= targetMax + 8f)
                {
                    return 1f;
                }

                return 1f - Mathf.InverseLerp(targetMax + 8f, targetMax + 50f, Temperature);
            }
        }

        public string TemperatureStatus
        {
            get
            {
                if (Temperature < targetMin - 6f)
                {
                    return "COLD";
                }

                if (Temperature > targetMax + 12f)
                {
                    return "HOT";
                }

                if (Temperature < targetMin || Temperature > targetMax)
                {
                    return "EDGE";
                }

                return "OPT";
            }
        }

        public float LastGripMultiplier
        {
            get { return lastGripMultiplier; }
        }

        public float WearPercent
        {
            get { return (1f - Wear) * 100f; }
        }
    }
}
