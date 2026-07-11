using F1Game.Core.Diagnostics;
using UnityEngine;

namespace LocalFormulaRacing
{
    // Central switch for diagnostic logging. Build/validation summaries always print;
    // per-frame and per-collision spam only prints when Verbose is enabled (F3 in play mode
    // toggles it via PlayerVehicleInput).
    //
    // Category-aware overloads (Info/Warn with a LogCategory) and Error(code,...)
    // delegate their formatting + gating to F1Game.Core.Diagnostics.DiagnosticLog
    // (engine-free, testable). The plain string overloads are unchanged so the
    // hundreds of existing call sites keep their exact behavior.
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

        public static void Info(LogCategory category, string message)
        {
            if (DiagnosticLog.IsEnabled(category, Verbose))
            {
                Debug.Log(DiagnosticLog.Format(category, message));
            }
        }

        public static void Warn(LogCategory category, string message)
        {
            if (DiagnosticLog.IsEnabled(category, Verbose))
            {
                Debug.LogWarning(DiagnosticLog.Format(category, message));
            }
        }

        // Errors always print with their stable code - they are the crash /
        // diagnostics package's anchor points, independent of Verbose.
        public static void Error(DiagnosticCode code, string message)
        {
            Debug.LogError(DiagnosticLog.FormatError(code, message));
        }
    }
}
