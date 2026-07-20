using System.Collections.Generic;

namespace F1Game.Art
{
    /// <summary>Code-defined visual identity for a class of circuits (no assets).</summary>
    public sealed class RuntimeCircuitProfile
    {
        public string id;
        public string primaryBarrier = RuntimeArtLibrary.Armco;
        public string secondaryBarrier = RuntimeArtLibrary.ConcreteBarrier;
        public bool concreteOnStraights = true;
        public bool tyreStacks = true;
        public string curb = RuntimeArtLibrary.KerbPainted;
        public bool sausageKerbs = true;
        public float barrierMargin = 3.5f;
        public float vegetationDensity = 4f;   // scatter attempts / 100 m
        public bool grandstands = true;
        public float clutterDensity = 1f;
        public bool pitBuildings = true;
        public bool lightTowers = false;
        public string roadSurface = RuntimeArtLibrary.SurfAsphaltWorn;
        public bool wetCapable = true;
        public int seedOffset = 0;
    }

    /// <summary>
    /// The nine code profiles + automatic selection from circuit / environment
    /// metadata. No <c>CircuitVisualProfile</c> assets, no catalog asset required.
    /// </summary>
    public static class RuntimeProfileLibrary
    {
        static readonly Dictionary<string, RuntimeCircuitProfile> profiles = Build();

        static Dictionary<string, RuntimeCircuitProfile> Build()
        {
            var d = new Dictionary<string, RuntimeCircuitProfile>();

            d["green_permanent"] = new RuntimeCircuitProfile
            { id = "green_permanent", vegetationDensity = 6f, grandstands = true, seedOffset = 11 };

            d["dry_permanent"] = new RuntimeCircuitProfile
            { id = "dry_permanent", vegetationDensity = 3f, roadSurface = RuntimeArtLibrary.SurfAsphalt, seedOffset = 23 };

            d["desert"] = new RuntimeCircuitProfile
            {
                id = "desert", primaryBarrier = RuntimeArtLibrary.ConcreteBarrier,
                secondaryBarrier = RuntimeArtLibrary.ConcreteBarrier, vegetationDensity = 0.5f,
                roadSurface = RuntimeArtLibrary.SurfAsphalt, tyreStacks = true, seedOffset = 31,
            };

            d["tropical"] = new RuntimeCircuitProfile
            { id = "tropical", vegetationDensity = 8f, concreteOnStraights = false, seedOffset = 41 };

            d["forest"] = new RuntimeCircuitProfile
            { id = "forest", vegetationDensity = 9f, concreteOnStraights = false, seedOffset = 53 };

            d["urban_street"] = new RuntimeCircuitProfile
            {
                id = "urban_street", primaryBarrier = RuntimeArtLibrary.ConcreteBarrier,
                secondaryBarrier = RuntimeArtLibrary.ConcreteBarrier, tyreStacks = true,
                vegetationDensity = 1f, clutterDensity = 2f, grandstands = true,
                barrierMargin = 2.5f, seedOffset = 61,
            };

            d["night_urban"] = new RuntimeCircuitProfile
            {
                id = "night_urban", primaryBarrier = RuntimeArtLibrary.ConcreteBarrier,
                secondaryBarrier = RuntimeArtLibrary.ConcreteBarrier, tyreStacks = true,
                vegetationDensity = 1f, clutterDensity = 2f, lightTowers = true,
                barrierMargin = 2.5f, seedOffset = 67,
            };

            d["coastal"] = new RuntimeCircuitProfile
            { id = "coastal", vegetationDensity = 4f, seedOffset = 71 };

            d["high_altitude"] = new RuntimeCircuitProfile
            { id = "high_altitude", vegetationDensity = 4f, seedOffset = 83 };

            return d;
        }

        static readonly (string profile, string[] track, string[] style)[] Mappings =
        {
            ("night_urban",   new[] { "singapore", "las_vegas", "vegas" }, new[] { "night", "neon" }),
            ("urban_street",  new[] { "monaco", "baku", "azerbaijan", "miami", "jeddah", "saudi" }, new[] { "street", "urban" }),
            ("desert",        new[] { "bahrain", "qatar", "abu_dhabi", "yas" }, new[] { "desert", "hot", "sand" }),
            ("coastal",       new[] { "zandvoort", "melbourne", "albert_park" }, new[] { "coastal", "sea", "harbour" }),
            ("high_altitude", new[] { "mexico", "interlagos", "sao_paulo" }, new[] { "altitude", "mountain" }),
            ("forest",        new[] { "spa", "red_bull_ring", "austria", "suzuka" }, new[] { "forest", "wood" }),
            ("dry_permanent", new[] { "barcelona", "catalunya", "hungary", "hungaroring", "imola" }, new[] { "european", "dry" }),
            ("green_permanent", new[] { "silverstone", "monza", "canada", "gilles" }, new[] { "permanent", "green", "parkland" }),
        };

        /// <summary>Pick a profile from circuit id then environment style; fallback green_permanent.</summary>
        public static RuntimeCircuitProfile Select(string trackId, string environmentStyle)
        {
            string id = (trackId ?? "").ToLowerInvariant();
            string style = (environmentStyle ?? "").ToLowerInvariant();

            foreach (var m in Mappings)
                foreach (var kw in m.track)
                    if (kw.Length > 0 && id.Contains(kw)) return profiles[m.profile];

            foreach (var m in Mappings)
                foreach (var kw in m.style)
                    if (kw.Length > 0 && style.Contains(kw)) return profiles[m.profile];

            return profiles["green_permanent"];
        }
    }
}
