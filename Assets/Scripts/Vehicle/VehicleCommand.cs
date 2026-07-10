namespace LocalFormulaRacing
{
    public struct VehicleCommand
    {
        public float throttle;
        public float brake;
        public float steer;
        public bool ers;
        public bool drs;
        public bool pitRequest;
        public bool shiftUp;
        public bool shiftDown;
        // AI-only recovery nudge (Part 2/3): a brief, bounded reverse push used to
        // back a stuck-against-a-barrier car away before it turns back toward the
        // track. Never set by player input - throttle/brake still apply normally
        // alongside it, so the caller should zero those out while it's active.
        public bool reverseAssist;
        // AI-only launch boost (0..1): a genuine additive low-speed forward force
        // applied off a standing start and off VSC/SC/yellow restarts. Throttle
        // INPUT ramp alone can't help here - both player and AI reach full
        // throttle almost instantly and then share the identical physics, so the
        // AI could never actually out-accelerate the player off the line no matter
        // how much the input ramp was buffed. This is the physics-level lever:
        // VehicleController turns it into a real forward push that ramps down with
        // speed (a launch/traction tool, gone by the time the car is at pace).
        // Never set by player input.
        public float launchBoost;
    }
}

