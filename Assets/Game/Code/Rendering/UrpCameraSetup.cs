using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace F1Game.Rendering
{
    /// <summary>
    /// Configures a runtime-created camera for URP: enables pipeline
    /// post-processing (so the global race Volume applies), HDR rendering, and
    /// SMAA. Called by CameraRig where it previously attached the legacy
    /// CameraPostFx OnRenderImage component.
    /// </summary>
    public static class UrpCameraSetup
    {
        public static void Apply(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.allowHDR = true;

            if (!ShaderCompat.UrpActive)
            {
                return;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null)
            {
                return;
            }

            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            // High SMAA: the runtime world is built from many small primitives
            // whose silhouette edges are exactly what SMAA cleans up; the
            // Medium preset left visible crawl on wings and barrier edges.
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;

            RaceVolumeService.EnsureVolume();
        }
    }
}
