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
        /// Chooses and builds the adapter at race start. Live race-layer call
        /// sites (DRS zones, track-limits width) now read the active query, so
        /// the selected backend MUST share the physical world's lap
        /// parameterization: the world is still built by the legacy
        /// TrackManager on every circuit, so the authored adapter (whose
        /// distances come from the generated definition, not the built world)
        /// is only selected behind the explicit validation flag - no longer
        /// auto-selected for the reference circuit. It becomes the ordinary
        /// per-circuit backend once track construction itself is authored.
        /// </summary>
        public static ITrackQuery Select(string trackId, TrackRuntime legacyRuntime)
        {
            bool useAuthored = ForceAuthored;

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
