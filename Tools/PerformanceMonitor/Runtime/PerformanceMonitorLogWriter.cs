using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class PerformanceMonitorLogWriter
{
    public static void WriteAsync(
        string directory,
        string sessionName,
        string metadata,
        List<PerformanceFrameSample> samples,
        List<PerformanceLoadSuspect> suspects,
        double triggerTime,
        Action<string> onCompleted)
    {
        string safeSession = MakeSafeFileName(sessionName);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string baseName = $"FXPerf_{safeSession}_{stamp}";
        string csvPath = Path.Combine(directory, baseName + ".csv");
        string summaryPath = Path.Combine(directory, baseName + "_summary.txt");

        Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(csvPath, BuildCsv(samples), new UTF8Encoding(true));
            File.WriteAllText(
                summaryPath,
                BuildSummary(metadata, samples, suspects, triggerTime),
                new UTF8Encoding(true));
            onCompleted?.Invoke(summaryPath);
        });
    }

    private static string BuildCsv(List<PerformanceFrameSample> samples)
    {
        var builder = new StringBuilder(Math.Max(4096, samples.Count * 180));
        builder.AppendLine(
            "frame,realtime,frame_ms,main_thread_ms,render_thread_ms,gpu_ms," +
            "draw_calls,batches,setpass,triangles,vertices,gc_alloc_bytes," +
            "total_used_bytes,total_reserved_bytes,gfx_used_bytes,timeline_time,timeline,active_clips");

        for (int i = 0; i < samples.Count; i++)
        {
            PerformanceFrameSample s = samples[i];
            builder.Append(s.frame).Append(',')
                .Append(Format(s.realtime)).Append(',')
                .Append(Format(s.frameMs)).Append(',')
                .Append(Format(s.mainThreadMs)).Append(',')
                .Append(Format(s.renderThreadMs)).Append(',')
                .Append(Format(s.gpuMs)).Append(',')
                .Append(s.drawCalls).Append(',')
                .Append(s.batches).Append(',')
                .Append(s.setPassCalls).Append(',')
                .Append(s.triangles).Append(',')
                .Append(s.vertices).Append(',')
                .Append(s.gcAllocBytes).Append(',')
                .Append(s.totalUsedMemoryBytes).Append(',')
                .Append(s.totalReservedMemoryBytes).Append(',')
                .Append(s.gfxUsedMemoryBytes).Append(',')
                .Append(Format(s.timelineTime)).Append(',')
                .Append(Escape(s.timelineName)).Append(',')
                .Append(Escape(s.activeClips)).AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSummary(
        string metadata,
        List<PerformanceFrameSample> samples,
        List<PerformanceLoadSuspect> suspects,
        double triggerTime)
    {
        PerformanceFrameSample peak = default;
        double baseFrame = 0d;
        double baseGpu = 0d;
        double baseMain = 0d;
        double baseDraws = 0d;
        int baselineCount = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            PerformanceFrameSample sample = samples[i];
            if (sample.frameMs > peak.frameMs)
            {
                peak = sample;
            }

            if (sample.realtime >= triggerTime - 1.5d && sample.realtime <= triggerTime - 0.25d)
            {
                baseFrame += sample.frameMs;
                baseGpu += sample.gpuMs;
                baseMain += sample.mainThreadMs;
                baseDraws += sample.drawCalls;
                baselineCount++;
            }
        }

        if (baselineCount > 0)
        {
            baseFrame /= baselineCount;
            baseGpu /= baselineCount;
            baseMain /= baselineCount;
            baseDraws /= baselineCount;
        }

        string bottleneck = DetermineBottleneck(peak, baseGpu, baseMain);
        var builder = new StringBuilder(4096);
        builder.AppendLine("FX 성능 분석 결과")
            .AppendLine("=================")
            .AppendLine(metadata)
            .AppendLine()
            .Append("문제 Timeline 구간: ").Append(Format(peak.timelineTime)).AppendLine("초")
            .Append("가장 느린 프레임: ").Append(Format(peak.frameMs)).AppendLine(" ms")
            .Append("추정 병목: ").AppendLine(bottleneck)
            .AppendLine()
            .AppendLine("문제 전 → 문제 프레임")
            .Append("- Frame: ").Append(Format(baseFrame)).Append(" → ").Append(Format(peak.frameMs)).AppendLine(" ms")
            .Append("- Main Thread: ").Append(Format(baseMain)).Append(" → ").Append(Format(peak.mainThreadMs)).AppendLine(" ms")
            .Append("- GPU: ").Append(Format(baseGpu)).Append(" → ").Append(Format(peak.gpuMs)).AppendLine(" ms")
            .Append("- Draw Calls: ").Append(Format(baseDraws)).Append(" → ").Append(peak.drawCalls).AppendLine()
            .Append("- GC Alloc: ").Append(FormatBytes(peak.gcAllocBytes)).AppendLine()
            .Append("- Active Clips: ").AppendLine(peak.activeClips ?? "-")
            .AppendLine();

        if (suspects.Count == 0)
        {
            builder.AppendLine("같은 시점에 분석 가능한 Timeline 프리팹/바인딩 후보가 없습니다.");
        }
        else
        {
            builder.AppendLine("부하 의심 후보");
            for (int i = 0; i < suspects.Count; i++)
            {
                PerformanceLoadSuspect s = suspects[i];
                builder.Append(i + 1).Append(". ").Append(s.clipName)
                    .Append(" — 신뢰도 ").AppendLine(s.confidence)
                    .Append("   Track: ").AppendLine(s.trackName)
                    .Append("   Object/Prefab: ").AppendLine(s.objectName)
                    .Append("   Renderer: ").Append(s.activeRendererCount).Append('/').Append(s.rendererCount)
                    .Append(", Shadow: ").Append(s.shadowCasterCount)
                    .Append(", Object Motion Vector: ").Append(s.objectMotionVectorCount).AppendLine()
                    .Append("   ParticleSystem: ").Append(s.particleSystemCount)
                    .Append(", Alive Particle: ").Append(s.aliveParticleCount)
                    .Append(", Rigidbody: ").Append(s.rigidbodyCount).AppendLine();
            }
        }

        builder.AppendLine().AppendLine("권장 확인 순서");
        AppendRecommendations(builder, bottleneck, peak, suspects);
        builder.AppendLine()
            .AppendLine("주의: 후보 순위는 spike 시점과 리소스 구성을 이용한 상관관계 분석입니다.")
            .AppendLine("원인을 확정하려면 해당 Root를 완전히 비활성화한 A/B 비교가 필요합니다.");
        return builder.ToString();
    }

    private static string DetermineBottleneck(PerformanceFrameSample peak, double baseGpu, double baseMain)
    {
        double gpuIncrease = peak.gpuMs - baseGpu;
        double mainIncrease = peak.mainThreadMs - baseMain;
        if (peak.gcAllocBytes > 256 * 1024 && mainIncrease > 2d)
        {
            return "CPU/GC 가능성이 높음";
        }

        if (peak.gpuMs > 0f && (gpuIncrease > mainIncrease + 2d || peak.gpuMs >= peak.mainThreadMs * 1.3f))
        {
            return "GPU 가능성이 높음";
        }

        if (mainIncrease > 2d)
        {
            return "CPU Main Thread 가능성이 높음";
        }

        return "혼합 또는 GPU 시간 미지원";
    }

    private static void AppendRecommendations(
        StringBuilder builder,
        string bottleneck,
        PerformanceFrameSample peak,
        List<PerformanceLoadSuspect> suspects)
    {
        if (bottleneck.StartsWith("GPU", StringComparison.Ordinal))
        {
            builder.AppendLine("- Render Scale 1.0을 유지하고 GPU Profiler의 MotionVectors/Transparent/PostProcess 시간을 확인하세요.");
            builder.AppendLine("- 파티클 오버드로우, Bloom/Blur, Distortion GrabPass를 우선 확인하세요.");
        }
        else if (bottleneck.StartsWith("CPU/GC", StringComparison.Ordinal))
        {
            builder.AppendLine("- 프리팹 Instantiate, 배열/문자열 생성, AfterImage Mesh 생성 여부를 확인하세요.");
        }
        else
        {
            builder.AppendLine("- Unity Profiler에서 가장 느린 프레임의 Main Thread와 GPU Usage를 함께 확인하세요.");
        }

        if (suspects.Count > 0)
        {
            PerformanceLoadSuspect first = suspects[0];
            if (first.shadowCasterCount > 50)
            {
                builder.Append("- ").Append(first.objectName).AppendLine("의 작은 파편 Shadow Casting을 끈 상태로 비교하세요.");
            }

            if (first.objectMotionVectorCount > 50)
            {
                builder.Append("- ").Append(first.objectName).AppendLine("의 Motion Vector를 Camera Motion Only로 바꿔 비교하세요.");
            }

            builder.Append("- 가장 먼저 ").Append(first.objectName)
                .AppendLine(" Root를 SetActive(false)한 A/B 테스트를 권장합니다.");
        }

        if (peak.totalUsedMemoryBytes > 0)
        {
            builder.AppendLine("- 같은 Timeline을 10회 반복해 Used Memory가 매회 계단식으로 증가하는지도 확인하세요.");
        }
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Session";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            builder.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
        if (bytes >= 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
        if (bytes >= 1024L)
        {
            return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }
        return bytes + " B";
    }
}
