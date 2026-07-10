using UnityEngine;

namespace F1Game.Vehicle
{
    /// <summary>
    /// Runtime accessor that resolves and caches a car's rig transforms by
    /// <see cref="CarRigSpec"/> name so gameplay/visual/camera/audio systems bind
    /// to typed handles instead of hard-coded child lookups. Two resolve modes:
    /// authored (full CarRigSpec hierarchy) and placeholder (the primitive car's
    /// actual child names), so the live race depends on this interface rather
    /// than on any specific car-construction code.
    /// </summary>
    public class CarRigBinding : MonoBehaviour
    {
        public bool IsPlaceholder;

        public Color primaryColor = Color.white;
        public Color secondaryColor = Color.grey;

        [Header("Body")]
        public Transform body;
        public Transform frontWing;
        public Transform rearWing;
        public Transform drsFlap;
        public Transform floor;
        public Transform halo;
        public Transform cockpit;
        public Transform steeringWheel;
        public Transform driver;
        public Transform rainLight;

        [Header("Wheels (FL, FR, RL, RR spin pivots)")]
        public Transform[] wheelSpin = new Transform[4];
        public Transform[] suspension = new Transform[4];
        public Transform[] steering = new Transform[2];
        public Transform[] brakeDiscs = new Transform[4];

        [Header("Mounts")]
        public Transform camChase;
        public Transform camTCam;
        public Transform camCockpit;
        public Transform camNose;
        public Transform audioEngine;
        public Transform audioTyres;
        public Transform[] vfxTyreSmoke = new Transform[4];

        /// <summary>Resolve against the authored CarRigSpec hierarchy.</summary>
        public void ResolveFromSpec()
        {
            IsPlaceholder = GetComponentInChildren<PlaceholderArtMarker>() != null;

            body = Find(CarRigSpec.Chassis);
            frontWing = Find(CarRigSpec.FrontWing);
            rearWing = Find(CarRigSpec.RearWing);
            drsFlap = Find(CarRigSpec.DrsFlap);
            floor = Find(CarRigSpec.Floor);
            halo = Find(CarRigSpec.Halo);
            cockpit = Find(CarRigSpec.Cockpit);
            steeringWheel = Find(CarRigSpec.SteeringWheel);
            driver = Find(CarRigSpec.Driver);
            rainLight = Find(CarRigSpec.RainLight);

            for (int i = 0; i < CarRigSpec.WheelCorners.Length; i++)
            {
                string corner = CarRigSpec.WheelCorners[i];
                bool front = corner[0] == 'F';
                suspension[i] = Find(CarRigSpec.Suspension(corner));
                wheelSpin[i] = Find(CarRigSpec.SpinPivot(corner, front));
                brakeDiscs[i] = Find(CarRigSpec.BrakeDisc(corner, front));
                if (front && i < 2)
                {
                    steering[i] = Find(CarRigSpec.SteeringPivot(corner));
                }

                vfxTyreSmoke[i] = Find(CarRigSpec.VfxTyreSmoke(corner));
            }

            camChase = Find(CarRigSpec.CamChase);
            camTCam = Find(CarRigSpec.CamTCam);
            camCockpit = Find(CarRigSpec.CamCockpit);
            camNose = Find(CarRigSpec.CamNose);
            audioEngine = Find(CarRigSpec.AudioEngine);
            audioTyres = Find(CarRigSpec.AudioTyres);
        }

        /// <summary>
        /// Resolve against the primitive placeholder car (wheel pivots named
        /// "wheel pivot front/rear"). Wheel handles are supplied directly by the
        /// factory because their names are ambiguous; the rest stay null.
        /// </summary>
        public void ResolvePlaceholder(Transform fl, Transform fr, Transform rl, Transform rr)
        {
            IsPlaceholder = true;
            wheelSpin[0] = fl;
            wheelSpin[1] = fr;
            wheelSpin[2] = rl;
            wheelSpin[3] = rr;
        }

        Transform Find(string path)
        {
            return transform.Find(path);
        }
    }
}
