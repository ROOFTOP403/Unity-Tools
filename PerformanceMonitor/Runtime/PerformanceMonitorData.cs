using System;

[Serializable]
public struct PerformanceFrameSample
{
    public int frame;
    public double realtime;
    public float frameMs;
    public float mainThreadMs;
    public float renderThreadMs;
    public float gpuMs;
    public long drawCalls;
    public long batches;
    public long setPassCalls;
    public long triangles;
    public long vertices;
    public long gcAllocBytes;
    public long totalUsedMemoryBytes;
    public long totalReservedMemoryBytes;
    public long gfxUsedMemoryBytes;
    public double timelineTime;
    public string timelineName;
    public string activeClips;
}

[Serializable]
public sealed class PerformanceLoadSuspect
{
    public string clipName;
    public string trackName;
    public string objectName;
    public double clipStart;
    public double clipEnd;
    public int rendererCount;
    public int activeRendererCount;
    public int shadowCasterCount;
    public int objectMotionVectorCount;
    public int particleSystemCount;
    public int aliveParticleCount;
    public int skinnedRendererCount;
    public int rigidbodyCount;
    public bool rootActive;
    public int score;
    public string confidence;
}
