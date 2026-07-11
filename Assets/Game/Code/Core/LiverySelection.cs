namespace F1Game.Core
{
    /// <summary>
    /// Engine-free resolver for a stored livery selection - the glue that turns a
    /// persisted string into a concrete <see cref="CarLivery"/>, tying together the
    /// presets, the custom storage form and the procedural generator. A selection is
    /// one of: a preset id ("azure"), a custom storage triple ("#P,#S,#A"), a
    /// generated marker ("generated:index:total"), or empty. Anything unrecognized
    /// falls back to a distinct generated livery so a car is never blank. Pure; the
    /// Unity layer owns the actual PlayerPrefs/JSON persistence and painting.
    /// </summary>
    public static class LiverySelection
    {
        const string GeneratedPrefix = "generated:";

        /// <summary>Selection string for a named preset.</summary>
        public static string ForPreset(string presetId) => presetId ?? "";

        /// <summary>Selection string for a fully custom livery.</summary>
        public static string ForCustom(CarLivery livery) => livery != null ? livery.ToStorage() : "";

        /// <summary>Selection string for a procedurally generated grid slot.</summary>
        public static string ForGenerated(int index, int total) => GeneratedPrefix + index + ":" + total;

        /// <summary>
        /// Resolve a stored selection to a livery. Empty/unrecognized selections fall
        /// back to a generated livery for (<paramref name="fallbackIndex"/>,
        /// <paramref name="total"/>) so the grid stays distinct and no car is blank.
        /// </summary>
        public static CarLivery Resolve(string selection, int fallbackIndex, int total)
        {
            CarLivery fallback = LiveryGenerator.Generate(fallbackIndex, total < 1 ? 1 : total);

            if (string.IsNullOrEmpty(selection))
            {
                return fallback;
            }

            // Custom storage triple ("#..,#..,#..").
            if (selection.IndexOf(',') >= 0)
            {
                return CarLivery.FromStorage(selection, fallback);
            }

            // Procedural marker ("generated:index:total").
            if (selection.StartsWith(GeneratedPrefix))
            {
                string rest = selection.Substring(GeneratedPrefix.Length);
                string[] parts = rest.Split(':');
                if (parts.Length == 2 && TryInt(parts[0], out int gi) && TryInt(parts[1], out int gt))
                {
                    return LiveryGenerator.Generate(gi, gt < 1 ? 1 : gt);
                }

                return fallback;
            }

            // Named preset.
            if (LiveryPresets.TryGet(selection, out CarLivery preset))
            {
                return preset;
            }

            return fallback;
        }

        static bool TryInt(string s, out int value) =>
            int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
