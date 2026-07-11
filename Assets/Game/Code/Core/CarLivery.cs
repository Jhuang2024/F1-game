using System.Collections.Generic;
using System.Globalization;

namespace F1Game.Core
{
    /// <summary>A 24-bit RGB livery colour with hex parse/format and 0..1 channel access.</summary>
    public readonly struct LiveryColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public LiveryColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R01 => R / 255f;
        public float G01 => G / 255f;
        public float B01 => B / 255f;

        /// <summary>Parse "#RRGGBB" or "RRGGBB" (case-insensitive). False on anything else.</summary>
        public static bool TryParseHex(string hex, out LiveryColor color)
        {
            color = default;
            if (string.IsNullOrEmpty(hex))
            {
                return false;
            }

            string s = hex[0] == '#' ? hex.Substring(1) : hex;
            if (s.Length != 6)
            {
                return false;
            }

            if (byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                color = new LiveryColor(r, g, b);
                return true;
            }

            return false;
        }

        /// <summary>Uppercase "#RRGGBB".</summary>
        public string ToHex() => "#" + R.ToString("X2", CultureInfo.InvariantCulture)
                                     + G.ToString("X2", CultureInfo.InvariantCulture)
                                     + B.ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>Largest per-channel absolute difference to another colour (0..255).</summary>
        public int MaxChannelDelta(LiveryColor other)
        {
            int dr = System.Math.Abs(R - other.R);
            int dg = System.Math.Abs(G - other.G);
            int db = System.Math.Abs(B - other.B);
            return System.Math.Max(dr, System.Math.Max(dg, db));
        }
    }

    /// <summary>
    /// Engine-free car livery / customization model: the primary, secondary and
    /// accent colours a car is painted with. Serializes to a compact, save-friendly
    /// string and round-trips exactly; a defensive parser falls back to a supplied
    /// default so a corrupt/absent value can never produce an invisible car. Pure
    /// data + validation; the Unity layer converts the channels to its colour type
    /// and paints the materials.
    /// </summary>
    public sealed class CarLivery
    {
        public LiveryColor Primary { get; }
        public LiveryColor Secondary { get; }
        public LiveryColor Accent { get; }

        public CarLivery(LiveryColor primary, LiveryColor secondary, LiveryColor accent)
        {
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
        }

        /// <summary>Compact storage form: "#PRIMARY,#SECONDARY,#ACCENT".</summary>
        public string ToStorage() => Primary.ToHex() + "," + Secondary.ToHex() + "," + Accent.ToHex();

        /// <summary>
        /// Parse a stored livery; returns <paramref name="fallback"/> when the text is
        /// missing/malformed or any colour is invalid, so a bad save degrades to a
        /// known-good livery instead of a blank car.
        /// </summary>
        public static CarLivery FromStorage(string text, CarLivery fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            string[] parts = text.Split(',');
            if (parts.Length != 3 ||
                !LiveryColor.TryParseHex(parts[0].Trim(), out LiveryColor p) ||
                !LiveryColor.TryParseHex(parts[1].Trim(), out LiveryColor s) ||
                !LiveryColor.TryParseHex(parts[2].Trim(), out LiveryColor a))
            {
                return fallback;
            }

            return new CarLivery(p, s, a);
        }

        /// <summary>
        /// True when two liveries are visually distinct enough to tell apart on the
        /// grid: their primary colours differ by at least <paramref name="minChannelDelta"/>
        /// (0..255) on some channel.
        /// </summary>
        public static bool AreDistinct(CarLivery a, CarLivery b, int minChannelDelta = 40)
        {
            if (a == null || b == null)
            {
                return true;
            }

            return a.Primary.MaxChannelDelta(b.Primary) >= minChannelDelta;
        }
    }

    /// <summary>
    /// Placeholder livery presets so the customization pipeline is exercisable before
    /// final authored liveries exist. Clearly identified as PLACEHOLDER content (a
    /// small set of distinct high-contrast schemes), retrievable by a stable id.
    /// </summary>
    public static class LiveryPresets
    {
        static readonly List<KeyValuePair<string, CarLivery>> presets = new List<KeyValuePair<string, CarLivery>>
        {
            Make("scarlet", "#E10600", "#1A1A1A", "#FFFFFF"),
            Make("azure", "#0072C6", "#0A0A0A", "#E0F0FF"),
            Make("emerald", "#00A650", "#0B0B0B", "#EAFBE7"),
            Make("amber", "#FF8C00", "#141414", "#FFF3D6"),
            Make("violet", "#7A3CFF", "#101018", "#F0E9FF"),
            Make("graphite", "#3A3F44", "#0C0E10", "#C8CDD2"),
        };

        public static IReadOnlyList<KeyValuePair<string, CarLivery>> All => presets;

        /// <summary>The first preset; a safe default for any car that has no livery yet.</summary>
        public static CarLivery Default => presets[0].Value;

        public static bool TryGet(string id, out CarLivery livery)
        {
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i].Key == id)
                {
                    livery = presets[i].Value;
                    return true;
                }
            }

            livery = Default;
            return false;
        }

        static KeyValuePair<string, CarLivery> Make(string id, string primary, string secondary, string accent)
        {
            LiveryColor.TryParseHex(primary, out LiveryColor p);
            LiveryColor.TryParseHex(secondary, out LiveryColor s);
            LiveryColor.TryParseHex(accent, out LiveryColor a);
            return new KeyValuePair<string, CarLivery>(id, new CarLivery(p, s, a));
        }
    }
}
