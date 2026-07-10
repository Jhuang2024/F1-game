using UnityEngine;

namespace F1Game.Vehicle
{
    /// <summary>
    /// Data asset describing a production car: the authored prefab (when one
    /// exists), wheel dimensions and livery slot metadata. The live spawn path
    /// resolves this; when <see cref="prefab"/> is null the spawner falls back to
    /// the explicitly-placeholder primitive car, still routed through the same
    /// production interface. Final art fills the prefab slot (Docs/ART_PIPELINE.md).
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/Vehicle/Car Definition", fileName = "Car_New")]
    public class CarDefinition : ScriptableObject
    {
        [Tooltip("Authored car prefab built to CarRigSpec. Null = use the placeholder primitive car.")]
        public GameObject prefab;

        public string displayId = "generic-f1";

        [Header("Wheels")]
        public float wheelRadiusFront = CarRigSpec.WheelRadiusFront;
        public float wheelRadiusRear = CarRigSpec.WheelRadiusRear;

        [Header("Livery")]
        [Tooltip("Shader colour property driven by team primary/secondary (URP/Lit uses _BaseColor).")]
        public string paintColorProperty = "_BaseColor";
        public string secondaryColorProperty = "_EmissionColor";

        public bool IsPlaceholder => prefab == null;
    }
}
