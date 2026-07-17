using UnityEngine;

namespace F1Game.UI
{
    /// <summary>
    /// Shared runtime-generated sprites for the production UI assembly
    /// (which cannot reference the legacy UiFactory). Anti-aliased and
    /// mipmapped so they stay clean both magnified (avatars, glows) and
    /// minified (minimap car dots, chart joints).
    /// </summary>
    public static class UiSprites
    {
        static Sprite circle;

        /// <summary>Anti-aliased filled circle, 128px, mipmapped.</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle == null)
                {
                    const int size = 128;
                    var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
                    {
                        name = "Ui circle (production)",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Trilinear,
                    };
                    var pixels = new Color[size * size];
                    float radius = size * 0.5f;
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x + 0.5f - radius;
                            float dy = y + 0.5f - radius;
                            float distance = Mathf.Sqrt(dx * dx + dy * dy);
                            pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f));
                        }
                    }

                    texture.SetPixels(pixels);
                    texture.Apply(true);
                    circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                }

                return circle;
            }
        }
    }
}
