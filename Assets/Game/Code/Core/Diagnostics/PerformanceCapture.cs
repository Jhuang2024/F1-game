using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace F1Game.Core.Diagnostics
{
    /// <summary>
    /// Repeatable performance capture for the 22-car benchmark: samples frame
    /// time, worst-frame percentiles, GC allocation delta and memory over a
    /// fixed window, then writes a JSON report next to the saves so before/after
    /// pipeline comparisons are one file-diff. Trigger via the Debug action map
    /// (F10) or call Begin() from a benchmark scene bootstrap.
    /// </summary>
    public sealed class PerformanceCapture : MonoBehaviour
    {
        [Serializable]
        public class Report
        {
            public string label;
            public string unityVersion;
            public string renderPipeline;
            public string qualityLevel;
            public int screenWidth;
            public int screenHeight;
            public float durationSeconds;
            public int frames;
            public float avgFps;
            public float avgFrameMs;
            public float p95FrameMs;
            public float p99FrameMs;
            public float worstFrameMs;
            public long gcAllocatedBytesDelta;
            public long totalReservedMemoryBytes;
            public string capturedAtUtc;
        }

        readonly List<float> frameTimes = new List<float>(4096);
        float elapsed;
        float duration;
        string label;
        long gcStartBytes;
        Action<Report> onComplete;
        bool running;

        public static PerformanceCapture Begin(string label, float durationSeconds = 30f, Action<Report> onComplete = null)
        {
            var go = new GameObject("Performance Capture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<PerformanceCapture>();
            capture.label = label;
            capture.duration = durationSeconds;
            capture.onComplete = onComplete;
            capture.gcStartBytes = GC.GetTotalMemory(false);
            capture.running = true;
            Debug.Log("[Perf] Capture started: " + label + " (" + durationSeconds + "s)");
            return capture;
        }

        void Update()
        {
            if (!running)
            {
                return;
            }

            frameTimes.Add(Time.unscaledDeltaTime * 1000f);
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= duration)
            {
                Finish();
            }
        }

        void Finish()
        {
            running = false;
            frameTimes.Sort();

            float total = 0f;
            foreach (float ms in frameTimes)
            {
                total += ms;
            }

            var report = new Report
            {
                label = label,
                unityVersion = Application.unityVersion,
                renderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name
                    : "Built-in",
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                durationSeconds = elapsed,
                frames = frameTimes.Count,
                avgFrameMs = frameTimes.Count > 0 ? total / frameTimes.Count : 0f,
                avgFps = elapsed > 0f ? frameTimes.Count / elapsed : 0f,
                p95FrameMs = Percentile(0.95f),
                p99FrameMs = Percentile(0.99f),
                worstFrameMs = frameTimes.Count > 0 ? frameTimes[frameTimes.Count - 1] : 0f,
                gcAllocatedBytesDelta = GC.GetTotalMemory(false) - gcStartBytes,
                totalReservedMemoryBytes = (long)Profiler.GetTotalReservedMemoryLong(),
                capturedAtUtc = DateTime.UtcNow.ToString("O"),
            };

            string fileName = "perf_" + Sanitize(label) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
            string path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log(string.Format(
                "[Perf] {0}: {1:0.0} fps avg, {2:0.00} ms avg, p95 {3:0.00} ms, p99 {4:0.00} ms, worst {5:0.00} ms -> {6}",
                label, report.avgFps, report.avgFrameMs, report.p95FrameMs, report.p99FrameMs, report.worstFrameMs, path));

            onComplete?.Invoke(report);
            Destroy(gameObject);
        }

        float Percentile(float p)
        {
            if (frameTimes.Count == 0)
            {
                return 0f;
            }

            int index = Mathf.Clamp(Mathf.CeilToInt(p * frameTimes.Count) - 1, 0, frameTimes.Count - 1);
            return frameTimes[index];
        }

        static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value.Replace(' ', '_');
        }
    }
}
