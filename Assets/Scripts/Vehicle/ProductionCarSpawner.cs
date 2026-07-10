using F1Game.Vehicle;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// The production car spawn interface the race path now calls instead of
    /// touching CarVisualFactory directly. It resolves a <see cref="CarDefinition"/>
    /// and instantiates the authored prefab through <see cref="CarRuntimeFactory"/>
    /// when one exists; otherwise it instantiates the explicitly-placeholder
    /// primitive car (still via this interface) and attaches a
    /// <see cref="CarRigBinding"/> so downstream systems bind to typed rig handles
    /// either way. The live race no longer depends structurally on primitive
    /// construction — only this bridge does, as the documented fallback.
    /// </summary>
    public static class ProductionCarSpawner
    {
        public static GameObject SpawnCar(string driverName, Color primary, Color secondary)
        {
            CarDefinition definition = CarRuntimeFactory.LoadDefaultDefinition();
            GameObject authored = CarRuntimeFactory.Instantiate(definition, driverName + " car");
            if (authored != null)
            {
                // Gameplay contract: a spawned car is physics-driven, so it must
                // carry a Rigidbody (the placeholder path adds one). Guard the
                // authored prefab in case its art was exported without one, so
                // the participant/ghost setup that expects a Rigidbody is safe.
                if (authored.GetComponent<Rigidbody>() == null)
                {
                    authored.AddComponent<Rigidbody>();
                }

                CarRigBinding binding = authored.GetComponent<CarRigBinding>();
                if (binding != null)
                {
                    binding.primaryColor = primary;
                    binding.secondaryColor = secondary;
                }

                ApplyLivery(authored, definition, primary, secondary);
                return authored;
            }

            // Placeholder path: the primitive car, already tagged
            // PlaceholderArtMarker, wrapped in the production rig binding.
            GameObject placeholder = CarVisualFactory.CreateOpenWheelCar(driverName, primary, secondary);
            AttachPlaceholderBinding(placeholder, primary, secondary);
            return placeholder;
        }

        static void AttachPlaceholderBinding(GameObject car, Color primary, Color secondary)
        {
            CarRigBinding binding = car.GetComponent<CarRigBinding>();
            if (binding == null)
            {
                binding = car.AddComponent<CarRigBinding>();
            }

            binding.primaryColor = primary;
            binding.secondaryColor = secondary;

            // Resolve the four "wheel pivot front/rear" transforms into FL,FR,RL,RR.
            Transform fl = null, fr = null, rl = null, rr = null;
            foreach (Transform child in car.transform)
            {
                if (child.name.StartsWith("wheel pivot"))
                {
                    bool front = child.localPosition.z > 0f;
                    bool left = child.localPosition.x < 0f;
                    if (front && left) fl = child;
                    else if (front) fr = child;
                    else if (left) rl = child;
                    else rr = child;
                }
            }

            binding.ResolvePlaceholder(fl, fr, rl, rr);
        }

        static void ApplyLivery(GameObject car, CarDefinition definition, Color primary, Color secondary)
        {
            // Central material-instance ownership: per-car colour via
            // MaterialPropertyBlock, no material clones.
            F1Game.Rendering.MaterialInstanceService.ApplyLivery(car, primary);
        }
    }
}
