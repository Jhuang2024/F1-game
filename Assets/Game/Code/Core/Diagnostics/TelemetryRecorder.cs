using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace F1Game.Core.Diagnostics
{
    /// <summary>
    /// Records a lap/session telemetry trace and exports CSV. The channels match
    /// the telemetry the HUD already surfaces plus inputs, so a lap can be
    /// compared or engineer-debriefed offline. Bounded by a sample cap.
    /// </summary>
    public sealed class TelemetryRecorder
    {
        public struct Sample
        {
            public float Time;
            public float Distance;
            public float SpeedKph;
            public float Throttle;
            public float Brake;
            public float Steer;
            public int Gear;
            public float Rpm01;
            public float Ers01;
            public bool Drs;
            public float TyreWear01;
            public float DeltaSeconds;
        }

        readonly List<Sample> samples = new List<Sample>();
        readonly int capacity;

        public int Count => samples.Count;
        /// <summary>Read-only view of the captured trace (for in-game debrief analysis).</summary>
        public IReadOnlyList<Sample> Samples => samples;

        public TelemetryRecorder(int capacity = 20000)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
        }

        public void Record(in Sample sample)
        {
            if (samples.Count >= capacity)
            {
                return;
            }

            samples.Add(sample);
        }

        public void Clear()
        {
            samples.Clear();
        }

        /// <summary>Writes a CSV to persistentDataPath and returns the full path.</summary>
        public string ExportCsv(string fileName)
        {
            var sb = new StringBuilder(samples.Count * 64 + 128);
            sb.AppendLine("time,distance,speed_kph,throttle,brake,steer,gear,rpm01,ers01,drs,tyre_wear01,delta_s");
            for (int i = 0; i < samples.Count; i++)
            {
                Sample s = samples[i];
                sb.Append(s.Time.ToString("0.###")).Append(',')
                  .Append(s.Distance.ToString("0.##")).Append(',')
                  .Append(s.SpeedKph.ToString("0.#")).Append(',')
                  .Append(s.Throttle.ToString("0.###")).Append(',')
                  .Append(s.Brake.ToString("0.###")).Append(',')
                  .Append(s.Steer.ToString("0.###")).Append(',')
                  .Append(s.Gear).Append(',')
                  .Append(s.Rpm01.ToString("0.###")).Append(',')
                  .Append(s.Ers01.ToString("0.###")).Append(',')
                  .Append(s.Drs ? '1' : '0').Append(',')
                  .Append(s.TyreWear01.ToString("0.###")).Append(',')
                  .Append(s.DeltaSeconds.ToString("0.###"))
                  .Append('\n');
            }

            string path = Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                File.WriteAllText(path, sb.ToString());
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Telemetry] CSV export failed: " + exception.Message);
                return null;
            }

            return path;
        }
    }
}
