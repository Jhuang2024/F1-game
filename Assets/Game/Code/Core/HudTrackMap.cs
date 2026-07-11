namespace F1Game.Core
{
    /// <summary>One car dot on the minimap, in normalized map space (0..1).</summary>
    public struct HudMapDot
    {
        public float X;
        public float Y;
        public bool IsPlayer;
        public bool Retired;
        public bool InPit;
    }

    /// <summary>
    /// Minimap data channel (race → UI), separate from the per-frame telemetry
    /// snapshot because the track outline changes only when the circuit does.
    /// The race layer (RaceEventRelay) publishes the normalized track outline
    /// once per track and the car dots each frame; the production
    /// MinimapModule reads both. Normalized 0..1 so the module maps into its
    /// own rect without knowing world scale.
    /// </summary>
    public static class HudTrackMap
    {
        public const int MaxOutline = 256;
        public const int MaxDots = 32;

        public static readonly UnityEngine.Vector2[] Outline = new UnityEngine.Vector2[MaxOutline];
        public static int OutlineCount;
        // Bumped whenever the outline is republished, so the renderer knows to
        // rebuild its (static) outline dots instead of diffing every frame.
        public static int OutlineVersion;

        public static readonly HudMapDot[] Dots = new HudMapDot[MaxDots];
        public static int DotCount;

        public static void PublishOutline(UnityEngine.Vector2[] normalizedPoints, int count)
        {
            int n = count < 0 ? 0 : (count > MaxOutline ? MaxOutline : count);
            for (int i = 0; i < n; i++)
            {
                Outline[i] = normalizedPoints[i];
            }

            OutlineCount = n;
            OutlineVersion++;
        }

        public static void Clear()
        {
            OutlineCount = 0;
            DotCount = 0;
            OutlineVersion++;
        }
    }
}
