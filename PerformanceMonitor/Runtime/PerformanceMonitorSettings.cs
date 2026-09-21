using UnityEngine;

[CreateAssetMenu(
    fileName = "PerformanceMonitorSettings",
    menuName = "RoofTop Studio/Performance Monitor Settings")]
public sealed class PerformanceMonitorSettings : ScriptableObject
{
    [Header("Startup")]
    [Tooltip("Play Mode 또는 Development Build 시작 시 Performance Monitor를 자동으로 실행합니다.")]
    public bool startAutomaticallyOnPlay = true;

    [Header("Frame Budget")]
    [Min(1)] public int targetFrameRate = 60;
    [Min(1f)] public float warningFrameMs = 18f;
    [Min(1f)] public float spikeFrameMs = 25f;
    [Min(1)] public int consecutiveSpikeFrames = 2;

    [Header("Capture")]
    public bool automaticCapture = true;
    [Min(0f)] public float startupWarmupSeconds = 2f;
    [Min(0.5f)] public float preRollSeconds = 3f;
    [Min(0.5f)] public float postRollSeconds = 2f;
    [Min(0f)] public float captureCooldownSeconds = 5f;
    [Min(60)] public int maximumBufferedFrames = 900;
    public string logDirectoryName = "PerformanceMonitor";

    [Header("HUD")]
    public bool showHudOnPlay = true;
    [Range(1f, 10f)] public float hudRefreshRate = 4f;
    [Range(1f, 10f)] public float timelineRefreshRate = 4f;
    [Range(0.7f, 2f)] public float hudScale = 1f;

    [Header("Analysis")]
    [Min(1)] public int maximumSuspects = 8;
    [Range(0.05f, 1f)] public float clipStartCorrelationWindow = 0.25f;
}
