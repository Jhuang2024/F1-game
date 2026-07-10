using F1Game.Track;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Selects the active <see cref="ITrackQuery"/> for the current race: the
    /// authored adapter when the event is the reference circuit (or the authored
    /// path is forced on), otherwise the legacy adapter over the live
    /// TrackRuntime. This is the seam race-layer call sites migrate onto so the
    /// reference circuit can run through the authored data while every other
    /// circuit keeps the legacy procedural backend.
    /// </summary>
    public static class TrackQueryProvider
    {
        const string ForceAuthoredKey = "f1game_authored_track";

        public static ITrackQuery Active { get; private set; }

        /// <summary>Force the authored path regardless of circuit (validation).</summary>
        public static bool ForceAuthored => PlayerPrefs.GetInt(ForceAuthoredKey, 0) == 1;

        public static void SetForceAuthored(bool value)
        {
            PlayerPrefs.SetInt(ForceAuthoredKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Chooses and builds the adapter at race start. When the event is the
        /// reference circuit (or authored is forced), builds the authored runtime
        /// from the generated definition; otherwise wraps the live TrackRuntime.
        /// </summary>
        public static ITrackQuery Select(string trackId, TrackRuntime legacyRuntime)
        {
            bool useAuthored = ForceAuthored ||
                (!string.IsNullOrEmpty(trackId) && trackId == ReferenceTrackGenerator.ReferenceTrackId);

            if (useAuthored)
            {
                var definition = ReferenceTrackGenerator.Generate();
                var authored = new AuthoredTrackRuntime(definition);
                if (authored.Length > 0f)
                {
                    Active = new AuthoredTrackQueryAdapter(authored);
                    return Active;
                }
            }

            Active = new LegacyTrackQueryAdapter(legacyRuntime);
            return Active;
        }

        public static void Clear()
        {
            Active = null;
        }
    }
}
