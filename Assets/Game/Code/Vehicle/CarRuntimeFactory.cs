using UnityEngine;

namespace F1Game.Vehicle
{
    /// <summary>
    /// Production car instantiation from a <see cref="CarDefinition"/>. Returns
    /// the authored prefab instance with a resolved <see cref="CarRigBinding"/>,
    /// or null when no prefab is authored yet (the caller then falls back to the
    /// placeholder path — still through the production spawn interface).
    /// </summary>
    public static class CarRuntimeFactory
    {
        const string DefaultDefinitionPath = "Cars/DefaultCarDefinition";

        public static CarDefinition LoadDefaultDefinition()
        {
            return Resources.Load<CarDefinition>(DefaultDefinitionPath);
        }

        public static GameObject Instantiate(CarDefinition definition, string carName)
        {
            if (definition == null || definition.prefab == null)
            {
                return null;
            }

            GameObject car = Object.Instantiate(definition.prefab);
            car.name = carName;

            CarRigBinding binding = car.GetComponent<CarRigBinding>();
            if (binding == null)
            {
                binding = car.AddComponent<CarRigBinding>();
            }

            binding.ResolveFromSpec();
            return car;
        }
    }
}
