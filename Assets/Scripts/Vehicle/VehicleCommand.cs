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
    }
}

