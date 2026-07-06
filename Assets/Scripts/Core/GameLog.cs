using UnityEngine;

namespace LocalFormulaRacing
{
    // Central switch for diagnostic logging. Build/validation summaries always print;
    // per-frame and per-collision spam only prints when Verbose is enabled (F3 in play mode
    // toggles it via PlayerVehicleInput).
    public static class GameLog
    {
        public static bool Verbose;

        public static void Info(string message)
        {
            if (Verbose)
            {
                Debug.Log(message);
            }
        }

        public static void Warn(string message)
        {
            if (Verbose)
            {
                Debug.LogWarning(message);
            }
        }
    }
}
