using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager lighting subsystem (partial). Builds the per-track lighting
    /// setup - the sun/key light, ambient and fog mood by track and weather, and
    /// the night-track floodlights (with the vertex-lit fill optimisation). Split
    /// out of the RaceManager monolith verbatim - same class, same members,
    /// identical light colours, intensities and render modes; callers resolve
    /// in-class.
    /// </summary>
    public partial class RaceManager
    {
        void CreateLighting()
        {
            string trackId = EventData == null || string.IsNullOrEmpty(EventData.trackId) ? "" : EventData.trackId;
            bool night = trackId.Contains("singapore") || trackId.Contains("las_vegas") || trackId.Contains("qatar");
            bool twilight = trackId.Contains("abu_dhabi");
            bool desert = trackId.Contains("bahrain") || trackId.Contains("abu_dhabi") || trackId.Contains("qatar");
            bool coastal = trackId.Contains("jeddah") || trackId.Contains("miami") || trackId.Contains("zandvoort") || trackId.Contains("monaco") || trackId.Contains("baku");
            bool mountain = trackId.Contains("austria") || trackId.Contains("spa") || trackId.Contains("austin") || trackId.Contains("mexico");
            bool park = trackId.Contains("silverstone") || trackId.Contains("melbourne") || trackId.Contains("monza") || trackId.Contains("interlagos") || trackId.Contains("suzuka") || trackId.Contains("zandvoort");
            string weatherProfile = EventData == null || string.IsNullOrEmpty(EventData.weatherProfile) ? "" : EventData.weatherProfile.ToLowerInvariant();
            bool rainThreat = weatherProfile.Contains("wet") || weatherProfile.Contains("mixed");

            int quality = Settings == null ? 2 : Mathf.Clamp(Settings.Current.graphicsQuality, 0, 3);
            // Premium visual pass: the post chain follows the same mood the
            // lighting uses, and quality 0 ("Low") turns it off entirely.
            // Both post backends are configured here: the URP Volume service (only
            // active under a scriptable pipeline) and the restored Built-in
            // CameraPostFx OnRenderImage chain (only attached when no SRP is active,
            // see CameraRig) - whichever matches the active pipeline takes effect.
            F1Game.Rendering.RaceVolumeService.GlobalEnabled = quality > 0;
            F1Game.Rendering.RaceVolumeService.ConfigureMood(night, rainThreat, twilight);
            CameraPostFx.GlobalEnabled = quality > 0;
            CameraPostFx.ConfigureMood(night, rainThreat, twilight);
            // URP migration: AA/shadow settings now come from the quality
            // level's pipeline tier asset; direct QualitySettings field writes
            // were inert under URP. The service switches the Unity quality
            // level (and thus the URP-Low/Medium/High asset) from the game's
            // 0-3 quality setting.
            F1Game.Rendering.GraphicsPresetService.Apply(quality);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            if (twilight)
            {
                RenderSettings.ambientSkyColor = new Color(0.3f, 0.2f, 0.34f);
                RenderSettings.ambientEquatorColor = new Color(0.42f, 0.24f, 0.2f);
                RenderSettings.ambientGroundColor = new Color(0.1f, 0.07f, 0.09f);
            }
            else
            {
                // Night readability: the old night ambient (avg ~0.05-0.14) plus a
                // 0.08 directional rendered car bodies at the same luminance as the
                // asphalt - silhouettes vanished. Lifted to a "floodlit circuit"
                // level that keeps the night mood but lets shapes read.
                RenderSettings.ambientSkyColor = night ? new Color(0.14f, 0.19f, 0.30f) : (rainThreat ? new Color(0.28f, 0.36f, 0.42f) : new Color(0.42f, 0.58f, 0.74f));
                RenderSettings.ambientEquatorColor = night ? new Color(0.09f, 0.13f, 0.20f) : (rainThreat ? new Color(0.28f, 0.32f, 0.34f) : new Color(0.45f, 0.42f, 0.38f));
                RenderSettings.ambientGroundColor = night ? new Color(0.025f, 0.028f, 0.045f) : (rainThreat ? new Color(0.08f, 0.09f, 0.1f) : (park ? new Color(0.12f, 0.18f, 0.12f) : new Color(0.18f, 0.16f, 0.14f)));
            }

            RenderSettings.reflectionIntensity = rainThreat ? 0.85f : 0.68f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Premium visual pass: the old densities (0.00015-0.00024, exp-
            // squared) were effectively invisible inside the 2.6km draw
            // distance - the ground plane met the sky as a hard line with no
            // aerial perspective at all. These values leave the first ~300m
            // crisp and fade the far scenery gently into the horizon haze.
            RenderSettings.fogDensity = rainThreat ? 0.0011f : (mountain ? 0.0008f : 0.00065f);
            Color dryFog = desert ? new Color(0.65f, 0.55f, 0.42f)
                : (coastal ? new Color(0.5f, 0.62f, 0.68f)
                : (mountain ? new Color(0.4f, 0.5f, 0.46f)
                : new Color(0.44f, 0.54f, 0.52f)));
            if (twilight)
            {
                dryFog = new Color(0.48f, 0.3f, 0.3f);
            }

            RenderSettings.fogColor = night ? new Color(0.015f, 0.02f, 0.035f) : (rainThreat ? new Color(0.28f, 0.34f, 0.36f) : dryFog);
            GameObject lightObject = new GameObject("Primary Sun");
            lightObject.transform.SetParent(raceWorld.transform);
            lightObject.transform.rotation = Quaternion.Euler(night ? -15f : (twilight ? 12f : (desert ? 32f : (mountain ? 38f : 48f))), desert ? -42f : (coastal ? -30f : -56f), 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            // Night: 0.28 reads as strong track floodlighting (the old 0.08 was
            // effectively unlit and left cars indistinguishable from the road).
            light.intensity = night ? 0.28f : (twilight ? 0.95f : (rainThreat ? 1.0f : (desert ? 1.7f : (coastal ? 1.55f : 1.42f))));
            light.color = night ? new Color(0.6f, 0.7f, 1f)
                : (twilight ? new Color(1f, 0.62f, 0.4f)
                : (rainThreat ? new Color(0.76f, 0.86f, 0.92f)
                : (desert ? new Color(1f, 0.85f, 0.65f)
                : (coastal ? new Color(1f, 0.94f, 0.85f)
                : new Color(0.98f, 0.96f, 0.94f)))));
            // Performance: Hard rather than Soft shadows. Soft directional shadows
            // do a multi-tap PCF filter over the whole shadow map every frame; with
            // 22 cars each built from dozens of small cosmetic primitives plus
            // thousands of procedural track objects all casting into that map, the
            // soft filter was a dominant per-frame cost after the Built-in-RP revert.
            // Hard shadows keep grounded contact shadows at a fraction of the cost.
            light.shadows = LightShadows.Hard;
            light.shadowStrength = rainThreat ? 0.68f : 0.92f;
            light.shadowBias = 0.035f;
            light.shadowNormalBias = 0.22f;

            // Premium visual pass: a real sky. This used to be
            // RenderSettings.skybox = null - the camera cleared to a flat fog
            // color, so every horizon in the game was a solid-colored wall,
            // the single loudest "prototype" signal in any screenshot. The
            // built-in procedural skybox gives a physically-shaded sky
            // gradient, horizon haze, and an actual sun disc driven by the
            // primary sun light above (RenderSettings.sun), tuned per
            // environment mood. Assigned BEFORE the reflection probe below
            // renders, so car paint and glass pick up the sky too.
            RenderSettings.sun = light;
            Shader proceduralSky = Shader.Find("Skybox/Procedural");
            if (proceduralSky != null)
            {
                Material sky = new Material(proceduralSky);
                sky.name = "Race sky";
                sky.SetFloat("_SunSize", twilight ? 0.06f : 0.045f);
                sky.SetFloat("_SunSizeConvergence", 5f);
                sky.SetFloat("_AtmosphereThickness",
                    night ? 0.5f : (twilight ? 1.35f : (rainThreat ? 1.75f : (desert ? 0.9f : 1.05f))));
                sky.SetColor("_SkyTint",
                    night ? new Color(0.18f, 0.22f, 0.38f)
                    : (rainThreat ? new Color(0.38f, 0.42f, 0.46f)
                    : (desert ? new Color(0.55f, 0.5f, 0.42f)
                    : new Color(0.5f, 0.52f, 0.56f))));
                sky.SetColor("_GroundColor",
                    night ? new Color(0.03f, 0.035f, 0.05f)
                    : (rainThreat ? new Color(0.25f, 0.27f, 0.28f)
                    : (desert ? new Color(0.45f, 0.38f, 0.28f)
                    : (park ? new Color(0.26f, 0.32f, 0.24f) : new Color(0.32f, 0.31f, 0.29f)))));
                sky.SetFloat("_Exposure",
                    night ? 0.12f : (twilight ? 1.15f : (rainThreat ? 0.85f : 1.3f)));
                RenderSettings.skybox = sky;
            }

            GameObject fill = new GameObject("Atmospheric Fill");
            fill.transform.SetParent(raceWorld.transform);
            fill.transform.position = new Vector3(40f, 40f, -40f);
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.intensity = night ? 1.8f : 0.64f;
            fillLight.range = 350f;
            fillLight.shadows = LightShadows.None;
            // Performance: a 350m-range pixel point light in Built-in Forward adds an
            // extra per-object forward pass to everything it touches. As a broad
            // ambient fill it does not need per-pixel quality, so render it as a cheap
            // vertex light instead.
            fillLight.renderMode = LightRenderMode.ForceVertex;

            // Performance fix: this was Realtime + EveryFrame - a full 6-face
            // cubemap re-render of the ENTIRE scene, every single frame, on
            // top of the main camera's own render. With a procedurally built
            // track carrying thousands of objects, this alone was enough to
            // crater the frame rate (users reporting ~20fps). A single
            // ViaScripting refresh right after the track/lighting finishes
            // building captures the same reflection once and never re-renders
            // it - correct for a track that doesn't change shape mid-race.
            GameObject probeObject = new GameObject("Runtime reflection probe");
            probeObject.transform.SetParent(raceWorld.transform);
            probeObject.transform.position = new Vector3(40f, 18f, 40f);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.intensity = rainThreat ? 0.85f : 0.68f;
            probe.size = new Vector3(520f, 120f, 520f);
            probe.resolution = 128;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.RenderProbe();

            if (night)
            {
                for (int i = 0; i < 6; i++)
                {
                    GameObject flood = new GameObject("Night floodlight");
                    flood.transform.SetParent(raceWorld.transform);
                    flood.transform.position = new Vector3(-80f + i * 58f, 18f, 30f + (i % 2) * 75f);
                    Light floodLight = flood.AddComponent<Light>();
                    floodLight.type = LightType.Point;
                    floodLight.intensity = 1.8f;
                    floodLight.range = 110f;
                    floodLight.shadows = LightShadows.None;
                    // Performance: six additive pixel point lights on a night track
                    // each add a forward pass to every object in range. Vertex lighting
                    // keeps the floodlit look at a fraction of the fill cost.
                    floodLight.renderMode = LightRenderMode.ForceVertex;
                }
            }
        }

    }
}
