using UnityEngine;

namespace LocalFormulaRacing
{
    // Lightweight per-car visual feedback: the rear rain-light glows with brake input
    // and blinks while the ERS battery is harvesting, like modern formula cars.
    public class VehicleVisuals : MonoBehaviour
    {
        VehicleController vehicle;
        Material brakeLightMaterial;
        float previousErs;

        static readonly Color GlowColor = new Color(1f, 0.06f, 0.04f);

        public void Initialize(VehicleController controller, Material lightMaterial)
        {
            vehicle = controller;
            brakeLightMaterial = lightMaterial;
            if (brakeLightMaterial != null)
            {
                brakeLightMaterial.EnableKeyword("_EMISSION");
            }
        }

        void Update()
        {
            // The car body is built before VehicleController is attached, so bind lazily.
            if (vehicle == null)
            {
                vehicle = GetComponent<VehicleController>();
            }

            if (vehicle == null || brakeLightMaterial == null)
            {
                return;
            }

            float brake = vehicle.EffectiveBrake;
            bool harvesting = vehicle.ErsBattery > previousErs + 0.0001f && Mathf.Abs(vehicle.CurrentSpeedKph) > 60f;
            previousErs = vehicle.ErsBattery;

            float intensity = brake;
            if (harvesting && brake < 0.2f)
            {
                intensity = Mathf.PingPong(Time.time * 6f, 1f) * 0.6f;
            }

            brakeLightMaterial.SetColor("_EmissionColor", GlowColor * Mathf.Clamp01(intensity) * 1.6f);
        }
    }
}
