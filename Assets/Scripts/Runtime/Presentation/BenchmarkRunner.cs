using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

namespace DotsCrowd
{
    /// <summary>
    /// Started with <c>-benchmark</c>: turns on auto-fire, disables vsync, records frame times after a warm-up,
    /// writes a summary next to the executable (benchmark.txt) and quits. In a development build,
    /// <c>-benchmark-profile</c> also records the first frames after the warm-up to benchmark.raw.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        [SerializeField, Min(0f)] float warmupSeconds = 5f;
        [SerializeField, Min(1f)] float measureSeconds = 40f;
        [SerializeField, Min(1)] int profiledFrames = 300;

        readonly List<float> m_FrameTimes = new List<float>(8192);
        readonly List<float> m_FrameStamps = new List<float>(8192);
        readonly List<float> m_MainThread = new List<float>(8192);
        readonly List<float> m_RenderThread = new List<float>(8192);
        readonly List<float> m_Gpu = new List<float>(8192);
        readonly FrameTiming[] m_Timing = new FrameTiming[1];
        float m_StartTime;
        bool m_AutoFireSet;
        int m_ProfilerFrame = -1;

        void Start()
        {
            if (!LaunchArguments.Has("-benchmark"))
            {
                enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            m_StartTime = Time.realtimeSinceStartup;
        }

        void Update()
        {
            if (!m_AutoFireSet)
            {
                var system = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<PlayerInputSystem>();
                if (system != null)
                {
                    system.AutoFire = true;
                    m_AutoFireSet = true;
                }
            }

            float elapsed = Time.realtimeSinceStartup - m_StartTime;
            FrameTimingManager.CaptureFrameTimings();
            if (elapsed < warmupSeconds)
                return;

            if (elapsed < warmupSeconds + measureSeconds)
            {
                UpdateProfilerCapture();
                m_FrameTimes.Add(Time.unscaledDeltaTime * 1000f);
                m_FrameStamps.Add(elapsed);
                RecordFrameTiming();
                return;
            }

            WriteReport();
            enabled = false;
            Application.Quit();
        }

        void UpdateProfilerCapture()
        {
            if (m_ProfilerFrame < 0)
            {
                if (!Debug.isDebugBuild || !LaunchArguments.Has("-benchmark-profile"))
                    return;

                Profiler.logFile = Path.Combine(OutputDirectory, "benchmark");
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;
                m_ProfilerFrame = 0;
            }
            else if (++m_ProfilerFrame == profiledFrames)
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = string.Empty;
            }
        }

        static string OutputDirectory => Path.GetDirectoryName(Application.dataPath) ?? ".";

        void WriteReport()
        {
            var sorted = new List<float>(m_FrameTimes);
            sorted.Sort();
            float total = 0f;
            foreach (float frame in sorted)
                total += frame;

            float average = total / Mathf.Max(1, sorted.Count);
            var stats = default(CrowdStats);
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CrowdStats>());
                if (!query.IsEmptyIgnoreFilter)
                    stats = query.GetSingleton<CrowdStats>();
            }

            var report = new StringBuilder()
                .AppendLine($"agents        {stats.Agents}")
                .AppendLine($"kills         {stats.Kills}")
                .AppendLine($"resolution    {Screen.width}x{Screen.height}")
                .AppendLine($"frames        {sorted.Count}")
                .AppendLine($"average ms    {average:0.00}  ({1000f / average:0} fps)")
                .AppendLine($"p50 ms        {Percentile(sorted, 0.50f):0.00}")
                .AppendLine($"p95 ms        {Percentile(sorted, 0.95f):0.00}")
                .AppendLine($"p99 ms        {Percentile(sorted, 0.99f):0.00}  (1% low {1000f / Mathf.Max(Percentile(sorted, 0.99f), 0.01f):0} fps)")
                .AppendLine($"max ms        {(sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f):0.00}")
                .AppendLine($"main thread   {Average(m_MainThread):0.00} ms avg   p95 {Percentile(Sorted(m_MainThread), 0.95f):0.00}")
                .AppendLine($"render thread {Average(m_RenderThread):0.00} ms avg   p95 {Percentile(Sorted(m_RenderThread), 0.95f):0.00}")
                .AppendLine($"gpu frame     {Average(m_Gpu):0.00} ms avg   p95 {Percentile(Sorted(m_Gpu), 0.95f):0.00}")
                .AppendLine($"gpu           {SystemInfo.graphicsDeviceName}")
                .AppendLine($"cpu           {SystemInfo.processorType} ({SystemInfo.processorCount} threads)")
                .Append(Timeline())
                .ToString();

            Debug.Log("[Benchmark]\n" + report);
            var path = Path.Combine(OutputDirectory, "benchmark.txt");
            File.WriteAllText(path, report);
        }

        /// <summary>Per-5-second windows plus the slowest frames, to tell steady cost from one-off hitches.</summary>
        string Timeline()
        {
            var text = new StringBuilder().AppendLine().AppendLine("window (s)    avg ms   worst ms");
            int index = 0;
            for (float window = warmupSeconds; window < warmupSeconds + measureSeconds; window += 5f)
            {
                float sum = 0f, worst = 0f;
                int count = 0;
                for (; index < m_FrameStamps.Count && m_FrameStamps[index] < window + 5f; index++, count++)
                {
                    sum += m_FrameTimes[index];
                    worst = Mathf.Max(worst, m_FrameTimes[index]);
                }

                if (count > 0)
                    text.AppendLine($"{window,4:0}-{window + 5f,-4:0}      {sum / count,6:0.00}   {worst,6:0.00}");
            }

            var slowest = new List<int>();
            for (int i = 0; i < m_FrameTimes.Count; i++)
                slowest.Add(i);
            slowest.Sort((a, b) => m_FrameTimes[b].CompareTo(m_FrameTimes[a]));
            text.AppendLine().Append("slowest frames (ms @ s):");
            for (int i = 0; i < Mathf.Min(8, slowest.Count); i++)
                text.Append($"  {m_FrameTimes[slowest[i]]:0.0}@{m_FrameStamps[slowest[i]]:0.0}");
            return text.AppendLine().ToString();
        }

        void RecordFrameTiming()
        {
            if (FrameTimingManager.GetLatestTimings(1, m_Timing) == 0)
                return;

            m_MainThread.Add((float)m_Timing[0].cpuMainThreadFrameTime);
            m_RenderThread.Add((float)m_Timing[0].cpuRenderThreadFrameTime);
            m_Gpu.Add((float)m_Timing[0].gpuFrameTime);
        }

        static List<float> Sorted(List<float> values)
        {
            var sorted = new List<float>(values);
            sorted.Sort();
            return sorted;
        }

        static float Average(List<float> values)
        {
            float sum = 0f;
            foreach (float value in values)
                sum += value;
            return values.Count > 0 ? sum / values.Count : 0f;
        }

        static float Percentile(List<float> sorted, float percentile)
        {
            if (sorted.Count == 0)
                return 0f;

            int index = Mathf.Clamp(Mathf.CeilToInt(percentile * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }
    }
}
