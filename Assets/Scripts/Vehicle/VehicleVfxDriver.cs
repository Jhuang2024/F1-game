using F1Game.Core.Pooling;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Wires live vehicle state to the pooled VFX controller: tyre smoke/lockup
    /// under braking lockups, wheelspin on power, dust/gravel/grass off-track,
    /// kerb sparks, and impact bursts on damage. Attached to each spawned car by
    /// ProductionCarSpawner. This makes the pooled VFX system a live consumer of
    /// real game state (art is still placeholder — see RaceVfxController).
    /// </summary>
    public sealed class VehicleVfxDriver : MonoBehaviour
    {
        VehicleController vehicle;
        float lastDamagePercent = -1f;
        float smokeCooldown;
        float dustCooldown;
        static bool loggedFault;

        static RaceVfxController shared;

        static RaceVfxController Controller
        {
            get
            {
                if (shared == null)
                {
                    shared = RaceVfxController.Create(null);
                    Object.DontDestroyOnLoad(shared.gameObject);
                }

                return shared;
            }
        }

        void Update()
        {
            // Fetched lazily: the VehicleController may be added after this
            // component (spawn ordering). No VehicleController → no VFX (e.g. ghost).
            if (vehicle == null)
            {
                vehicle = GetComponent<VehicleController>();
                if (vehicle == null)
                {
                    return;
                }
            }

            // This is a purely cosmetic, interim bridge. It must never throw into
            // the game loop: a per-frame exception here would spam the console and
            // could mask real issues. Guard the whole body and disable-on-fault.
            try
            {
                Drive();
            }
            catch (System.Exception exception)
            {
                if (!loggedFault)
                {
                    loggedFault = true;
                    Debug.LogWarning("[VehicleVfxDriver] disabled after fault (cosmetic VFX only): " + exception);
                }

                enabled = false;
            }
        }

        void Drive()
        {
            float dt = Time.deltaTime;
            smokeCooldown -= dt;
            dustCooldown -= dt;

            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / 300f);
            Vector3 rear = transform.position - transform.forward * 1.6f + Vector3.up * 0.15f;

            // Lockup smoke under braking (tyre locked at speed).
            bool locked = vehicle.Tyres != null && vehicle.Tyres.IsLocked;
            if (locked && speed01 > 0.15f && smokeCooldown <= 0f)
            {
                Controller.Spawn(RaceVfxController.VfxKind.Lockup, FrontContact(), transform.rotation);
                smokeCooldown = 0.06f;
            }

            // Wheelspin / power smoke: high slip while accelerating out of low speed.
            float slip = Mathf.Clamp01(vehicle.OversteerAmount + vehicle.UndersteerAmount * 0.5f);
            if (slip > 0.4f && speed01 > 0.05f && smokeCooldown <= 0f)
            {
                Controller.Spawn(RaceVfxController.VfxKind.TyreSmoke, rear, transform.rotation);
                smokeCooldown = 0.08f;
            }

            // Off-track dust/gravel/grass.
            if (vehicle.IsOffTrackSlowdown && speed01 > 0.08f && dustCooldown <= 0f)
            {
                Controller.Spawn(RaceVfxController.VfxKind.Gravel, rear, transform.rotation);
                dustCooldown = 0.1f;
            }

            // Kerb sparks.
            if (vehicle.IsOnKerb && speed01 > 0.25f && dustCooldown <= 0f)
            {
                Controller.Spawn(RaceVfxController.VfxKind.Sparks, FrontContact(), transform.rotation);
                dustCooldown = 0.12f;
            }

            // Impact burst on a fresh damage jump.
            if (vehicle.Damage != null)
            {
                float dmg = vehicle.Damage.OverallPercent;
                if (lastDamagePercent >= 0f && dmg > lastDamagePercent + 0.04f)
                {
                    Controller.Spawn(RaceVfxController.VfxKind.Impact, transform.position + Vector3.up * 0.4f, transform.rotation);
                    Controller.Spawn(RaceVfxController.VfxKind.Debris, transform.position + Vector3.up * 0.3f, transform.rotation);
                }

                lastDamagePercent = dmg;
            }
        }

        Vector3 FrontContact()
        {
            return transform.position + transform.forward * 1.3f + Vector3.up * 0.1f;
        }
    }
}
