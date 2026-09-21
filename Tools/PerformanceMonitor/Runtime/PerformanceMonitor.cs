using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(10000)]
public sealed class PerformanceMonitor : MonoBehaviour
{
    private const string SettingsResourceName = "PerformanceMonitorSettings";

    private static PerformanceMonitor instance;
    public static PerformanceMonitor Instance => instance;
    public static bool IsRunning => instance != null;

    private PerformanceMonitorSettings settings;
    private PerformanceFrameSample[] ringBuffer;
    private int ringWriteIndex;
    private int ringCount;
    private readonly FrameTiming[] frameTimings = new FrameTiming[2];
    private readonly PerformanceTimelineSampler timelineSampler = new PerformanceTimelineSampler();
    private readonly StringBuilder hudBuilder = new StringBuilder(768);
    private readonly float[] percentileScratch = new float[300];
    private readonly ConcurrentQueue<string> completedLogPaths = new ConcurrentQueue<string>();
    private GUIStyle hudStyle;
    private GUIStyle boxStyle;
    private string hudText = "Performance Monitor 준비 중...";
    private string lastLogPath = "-";
    private bool hudVisible;
    private bool captureActive;
    private bool lastFrameWasWarning;
    private int consecutiveSpikes;
    private double captureTriggerTime;
    private double captureEndTime;
    private double nextAllowedCaptureTime;
    private float nextHudUpdateTime;
    private float nextTimelineUpdateTime;
    private float smoothedFrameMs;
    private float onePercentLowFps;
    private double monitorStartTime;
    private List<PerformanceLoadSuspect> capturedSuspects = new List<PerformanceLoadSuspect>();

    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder renderThreadRecorder;
    private ProfilerRecorder gpuFrameRecorder;
    private ProfilerRecorder drawCallsRecorder;
    private ProfilerRecorder batchesRecorder;
    private ProfilerRecorder setPassRecorder;
    private ProfilerRecorder trianglesRecorder;
    private ProfilerRecorder verticesRecorder;
    private ProfilerRecorder gcAllocRecorder;
    private ProfilerRecorder totalUsedRecorder;
    private ProfilerRecorder totalReservedRecorder;
    private ProfilerRecorder gfxUsedRecorder;

    public PerformanceMonitorSettings Settings => settings;
    public bool HudVisible => hudVisible;
    public string LastLogPath => lastLogPath;
    public bool IsCapturing => captureActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!CanRun() || instance != null)
        {
            return;
        }

        PerformanceMonitorSettings startupSettings = Resources.Load<PerformanceMonitorSettings>(SettingsResourceName);
        if (startupSettings != null && !startupSettings.startAutomaticallyOnPlay)
        {
            return;
        }

        CreateMonitor();
    }

    public static PerformanceMonitor CreateMonitor()
    {
        if (!CanRun())
        {
            return null;
        }

        if (instance != null)
        {
            return instance;
        }

        var gameObject = new GameObject("[Performance Monitor]");
        DontDestroyOnLoad(gameObject);
        instance = gameObject.AddComponent<PerformanceMonitor>();
        return instance;
    }

    private static bool CanRun()
    {
        return Application.isPlaying && (Application.isEditor || Debug.isDebugBuild);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        settings = Resources.Load<PerformanceMonitorSettings>(SettingsResourceName);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PerformanceMonitorSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
        }

        ringBuffer = new PerformanceFrameSample[Mathf.Max(60, settings.maximumBufferedFrames)];
        hudVisible = settings.showHudOnPlay;
        monitorStartTime = Time.realtimeSinceStartupAsDouble;
        StartRecorders();
        FrameTimingManager.CaptureFrameTimings();
    }

    private void Update()
    {
        while (completedLogPaths.TryDequeue(out string completedPath))
        {
            lastLogPath = completedPath;
            Debug.Log("[Performance Monitor] 캡처 저장 완료: " + completedPath);
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            ToggleHud();
        }
        if (Input.GetKeyDown(KeyCode.F9))
        {
            CaptureNow();
        }

        float realtime = Time.realtimeSinceStartup;
        if (realtime >= nextTimelineUpdateTime)
        {
            nextTimelineUpdateTime = realtime + 1f / Mathf.Max(1f, settings.timelineRefreshRate);
            timelineSampler.Update(realtime);
        }

        FrameTimingManager.CaptureFrameTimings();
        PerformanceFrameSample sample = CollectSample(realtime);
        AddSample(sample);
        DetectSpike(sample);

        if (captureActive && sample.realtime >= captureEndTime)
        {
            CompleteCapture();
        }

        if (realtime >= nextHudUpdateTime)
        {
            nextHudUpdateTime = realtime + 1f / Mathf.Max(1f, settings.hudRefreshRate);
            RefreshHud(sample);
        }
    }

    private void OnGUI()
    {
        if (!hudVisible || Event.current.type != EventType.Repaint)
        {
            return;
        }

        EnsureHudStyles();
        float scale = settings != null ? settings.hudScale : 1f;
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        Rect rect = new Rect(12f, 12f, 480f, 230f);
        GUI.Box(rect, GUIContent.none, boxStyle);
        GUI.Label(new Rect(24f, 20f, 455f, 215f), hudText, hudStyle);
        GUI.matrix = previous;
    }

    private void OnDestroy()
    {
        DisposeRecorders();
        timelineSampler.Clear();
        if (instance == this)
        {
            instance = null;
        }
    }

    public void ToggleHud()
    {
        hudVisible = !hudVisible;
    }

    public void CaptureNow()
    {
        BeginCapture(true);
    }

    public void StopMonitoring()
    {
        enabled = false;
    }

    public void StartMonitoring()
    {
        enabled = true;
    }

    private PerformanceFrameSample CollectSample(float realtime)
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        smoothedFrameMs = smoothedFrameMs <= 0f
            ? frameMs
            : Mathf.Lerp(smoothedFrameMs, frameMs, 0.08f);

        float gpuMs = ReadNanoseconds(gpuFrameRecorder);
        uint timingCount = FrameTimingManager.GetLatestTimings((uint)frameTimings.Length, frameTimings);
        if (gpuMs <= 0f && timingCount > 0)
        {
            gpuMs = (float)frameTimings[0].gpuFrameTime;
        }

        return new PerformanceFrameSample
        {
            frame = Time.frameCount,
            realtime = realtime,
            frameMs = frameMs,
            mainThreadMs = ReadNanoseconds(mainThreadRecorder),
            renderThreadMs = ReadNanoseconds(renderThreadRecorder),
            gpuMs = gpuMs,
            drawCalls = ReadValue(drawCallsRecorder),
            batches = ReadValue(batchesRecorder),
            setPassCalls = ReadValue(setPassRecorder),
            triangles = ReadValue(trianglesRecorder),
            vertices = ReadValue(verticesRecorder),
            gcAllocBytes = ReadValue(gcAllocRecorder),
            totalUsedMemoryBytes = ReadValue(totalUsedRecorder),
            totalReservedMemoryBytes = ReadValue(totalReservedRecorder),
            gfxUsedMemoryBytes = ReadValue(gfxUsedRecorder),
            timelineTime = timelineSampler.TimelineTime,
            timelineName = timelineSampler.TimelineName,
            activeClips = timelineSampler.ActiveClips
        };
    }

    private void AddSample(PerformanceFrameSample sample)
    {
        ringBuffer[ringWriteIndex] = sample;
        ringWriteIndex = (ringWriteIndex + 1) % ringBuffer.Length;
        ringCount = Mathf.Min(ringCount + 1, ringBuffer.Length);
    }

    private void DetectSpike(PerformanceFrameSample sample)
    {
        lastFrameWasWarning = sample.frameMs >= settings.warningFrameMs;
        if (!settings.automaticCapture ||
            captureActive ||
            sample.realtime < monitorStartTime + settings.startupWarmupSeconds ||
            sample.realtime < nextAllowedCaptureTime)
        {
            return;
        }

        consecutiveSpikes = sample.frameMs >= settings.spikeFrameMs ? consecutiveSpikes + 1 : 0;
        if (consecutiveSpikes >= settings.consecutiveSpikeFrames)
        {
            BeginCapture(false);
        }
    }

    private void BeginCapture(bool manual)
    {
        if (captureActive)
        {
            captureEndTime = Time.realtimeSinceStartupAsDouble + settings.postRollSeconds;
            return;
        }

        captureActive = true;
        consecutiveSpikes = 0;
        captureTriggerTime = Time.realtimeSinceStartupAsDouble;
        captureEndTime = captureTriggerTime + settings.postRollSeconds;
        capturedSuspects = timelineSampler.CaptureSuspects(
            settings.clipStartCorrelationWindow,
            settings.maximumSuspects);

        if (manual)
        {
            Debug.Log("[Performance Monitor] 수동 캡처를 시작했습니다.");
        }
    }

    private void CompleteCapture()
    {
        captureActive = false;
        nextAllowedCaptureTime = Time.realtimeSinceStartupAsDouble + settings.captureCooldownSeconds;
        List<PerformanceFrameSample> samples = CopyCaptureSamples(
            captureTriggerTime - settings.preRollSeconds,
            captureEndTime);

        string timelineName = timelineSampler.TimelineName == "-"
            ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            : timelineSampler.TimelineName;
        string directoryName = string.IsNullOrWhiteSpace(settings.logDirectoryName)
            ? "PerformanceMonitor"
            : settings.logDirectoryName;
        string directory = Path.Combine(Application.persistentDataPath, directoryName);
        string metadata = BuildMetadata();

        PerformanceMonitorLogWriter.WriteAsync(
            directory,
            timelineName,
            metadata,
            samples,
            capturedSuspects,
            captureTriggerTime,
            path => completedLogPaths.Enqueue(path));
    }

    private List<PerformanceFrameSample> CopyCaptureSamples(double from, double to)
    {
        var result = new List<PerformanceFrameSample>(ringCount);
        int oldest = (ringWriteIndex - ringCount + ringBuffer.Length) % ringBuffer.Length;
        for (int i = 0; i < ringCount; i++)
        {
            PerformanceFrameSample sample = ringBuffer[(oldest + i) % ringBuffer.Length];
            if (sample.realtime >= from && sample.realtime <= to)
            {
                result.Add(sample);
            }
        }

        return result;
    }

    private void RefreshHud(PerformanceFrameSample sample)
    {
        onePercentLowFps = CalculateOnePercentLow();
        float fps = smoothedFrameMs > 0.001f ? 1000f / smoothedFrameMs : 0f;
        float renderScale = GetRenderScale();
        hudBuilder.Length = 0;
        hudBuilder.Append("PERFORMANCE MONITOR  ")
            .Append(captureActive ? "[CAPTURE]" : lastFrameWasWarning ? "[WARNING]" : "[MONITOR]")
            .AppendLine()
            .Append("FPS  ").Append(fps.ToString("0.0"))
            .Append("   1% Low ").Append(onePercentLowFps.ToString("0.0"))
            .Append("   Frame ").Append(sample.frameMs.ToString("0.0")).AppendLine(" ms")
            .Append("CPU  Main ").Append(FormatMs(sample.mainThreadMs))
            .Append("   Render ").Append(FormatMs(sample.renderThreadMs)).AppendLine()
            .Append("GPU  ").Append(FormatMs(sample.gpuMs)).AppendLine()
            .Append("Render  Draw ").Append(sample.drawCalls)
            .Append("   Batch ").Append(sample.batches)
            .Append("   SetPass ").Append(sample.setPassCalls).AppendLine()
            .Append("Geometry  ").Append(FormatCount(sample.triangles)).Append(" tris   ")
            .Append(FormatCount(sample.vertices)).AppendLine(" verts")
            .Append("Memory  Used ").Append(FormatBytes(sample.totalUsedMemoryBytes))
            .Append("   Reserved ").Append(FormatBytes(sample.totalReservedMemoryBytes)).AppendLine()
            .Append("Gfx  ").Append(FormatBytes(sample.gfxUsedMemoryBytes))
            .Append("   GC/frame ").Append(FormatBytes(sample.gcAllocBytes)).AppendLine()
            .Append("Quality  ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()])
            .Append("   Render Scale ").Append(renderScale.ToString("0.00")).AppendLine()
            .Append("Timeline  ").Append(sample.timelineName).Append("  ")
            .Append(sample.timelineTime.ToString("0.000")).AppendLine(" s")
            .Append("Active  ").AppendLine(sample.activeClips)
            .Append("F8 HUD   F9 Capture");
        hudText = hudBuilder.ToString();
    }

    private float CalculateOnePercentLow()
    {
        if (ringCount < 10)
        {
            return 0f;
        }

        int sampleCount = Mathf.Min(ringCount, 300);
        int start = (ringWriteIndex - sampleCount + ringBuffer.Length) % ringBuffer.Length;
        for (int i = 0; i < sampleCount; i++)
        {
            percentileScratch[i] = ringBuffer[(start + i) % ringBuffer.Length].frameMs;
        }

        Array.Sort(percentileScratch, 0, sampleCount);
        int percentileIndex = Mathf.Clamp(Mathf.FloorToInt(sampleCount * 0.99f), 0, sampleCount - 1);
        float slowFrameMs = percentileScratch[percentileIndex];
        return slowFrameMs > 0.001f ? 1000f / slowFrameMs : 0f;
    }

    private string BuildMetadata()
    {
        return "Scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "\n" +
               "Unity: " + Application.unityVersion + "\n" +
               "Platform: " + Application.platform + "\n" +
               "GPU: " + SystemInfo.graphicsDeviceName + "\n" +
               "Resolution: " + Screen.width + "x" + Screen.height + "\n" +
               "Quality: " + QualitySettings.names[QualitySettings.GetQualityLevel()] + "\n" +
               "Render Scale: " + GetRenderScale().ToString("0.00");
    }

    private static float GetRenderScale()
    {
        RenderPipelineAsset asset = QualitySettings.renderPipeline ?? GraphicsSettings.currentRenderPipeline;
        return asset is UniversalRenderPipelineAsset urp ? urp.renderScale : 1f;
    }

    private void StartRecorders()
    {
        mainThreadRecorder = StartRecorder(ProfilerCategory.Internal, "Main Thread", 15);
        renderThreadRecorder = StartRecorder(ProfilerCategory.Internal, "Render Thread", 15);
        gpuFrameRecorder = StartRecorder(ProfilerCategory.Internal, "GPU Frame Time", 15);
        drawCallsRecorder = StartRecorder(ProfilerCategory.Render, "Draw Calls Count", 1);
        batchesRecorder = StartRecorder(ProfilerCategory.Render, "Batches Count", 1);
        setPassRecorder = StartRecorder(ProfilerCategory.Render, "SetPass Calls Count", 1);
        trianglesRecorder = StartRecorder(ProfilerCategory.Render, "Triangles Count", 1);
        verticesRecorder = StartRecorder(ProfilerCategory.Render, "Vertices Count", 1);
        gcAllocRecorder = StartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        totalUsedRecorder = StartRecorder(ProfilerCategory.Memory, "Total Used Memory", 1);
        totalReservedRecorder = StartRecorder(ProfilerCategory.Memory, "Total Reserved Memory", 1);
        gfxUsedRecorder = StartRecorder(ProfilerCategory.Memory, "Gfx Used Memory", 1);
    }

    private static ProfilerRecorder StartRecorder(ProfilerCategory category, string name, int capacity)
    {
        try
        {
            return ProfilerRecorder.StartNew(category, name, capacity);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Performance Monitor] Profiler counter unavailable: " + name + " (" + exception.Message + ")");
            return default;
        }
    }

    private void DisposeRecorders()
    {
        Dispose(ref mainThreadRecorder);
        Dispose(ref renderThreadRecorder);
        Dispose(ref gpuFrameRecorder);
        Dispose(ref drawCallsRecorder);
        Dispose(ref batchesRecorder);
        Dispose(ref setPassRecorder);
        Dispose(ref trianglesRecorder);
        Dispose(ref verticesRecorder);
        Dispose(ref gcAllocRecorder);
        Dispose(ref totalUsedRecorder);
        Dispose(ref totalReservedRecorder);
        Dispose(ref gfxUsedRecorder);
    }

    private static void Dispose(ref ProfilerRecorder recorder)
    {
        if (recorder.Valid)
        {
            recorder.Dispose();
        }
        recorder = default;
    }

    private static long ReadValue(ProfilerRecorder recorder)
    {
        return recorder.Valid ? recorder.LastValue : 0L;
    }

    private static float ReadNanoseconds(ProfilerRecorder recorder)
    {
        return recorder.Valid ? recorder.LastValue * 0.000001f : 0f;
    }

    private void EnsureHudStyles()
    {
        if (hudStyle != null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            richText = false,
            wordWrap = false,
            normal = { textColor = Color.white }
        };
        boxStyle = new GUIStyle(GUI.skin.box);
    }

    private static string FormatMs(float value) => value > 0f ? value.ToString("0.0") + " ms" : "N/A";

    private static string FormatCount(long value)
    {
        if (value >= 1000000L) return (value / 1000000d).ToString("0.00") + "M";
        if (value >= 1000L) return (value / 1000d).ToString("0.0") + "K";
        return value.ToString();
    }

    private static string FormatBytes(long value)
    {
        if (value <= 0L) return "N/A";
        if (value >= 1024L * 1024L * 1024L) return (value / (1024d * 1024d * 1024d)).ToString("0.00") + " GB";
        if (value >= 1024L * 1024L) return (value / (1024d * 1024d)).ToString("0.0") + " MB";
        if (value >= 1024L) return (value / 1024d).ToString("0.0") + " KB";
        return value + " B";
    }
}
