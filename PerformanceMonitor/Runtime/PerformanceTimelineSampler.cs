using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

public sealed class PerformanceTimelineSampler
{
    private sealed class RootProfile
    {
        public GameObject root;
        public Renderer[] renderers = Array.Empty<Renderer>();
        public ParticleSystem[] particles = Array.Empty<ParticleSystem>();
        public int shadowCasters;
        public int objectMotionVectors;
        public int skinnedRenderers;
        public int rigidbodies;
    }

    private sealed class ClipProfile
    {
        public TimelineClip clip;
        public TrackAsset track;
        public string clipName;
        public string trackName;
        public string objectName;
        public RootProfile root;
    }

    private readonly List<PlayableDirector> directors = new List<PlayableDirector>();
    private readonly List<ClipProfile> clips = new List<ClipProfile>();
    private readonly Dictionary<int, RootProfile> rootProfiles = new Dictionary<int, RootProfile>();
    private readonly StringBuilder activeBuilder = new StringBuilder(256);

    private PlayableDirector activeDirector;
    private PlayableAsset cachedAsset;
    private float nextDirectorSearchTime;

    public string TimelineName { get; private set; } = "-";
    public string ActiveClips { get; private set; } = "-";
    public double TimelineTime => activeDirector != null ? activeDirector.time : 0d;

    public void Update(float realtime)
    {
        if (activeDirector == null || activeDirector.state != PlayState.Playing)
        {
            FindActiveDirector(realtime);
        }

        if (activeDirector == null)
        {
            TimelineName = "-";
            ActiveClips = "-";
            return;
        }

        if (cachedAsset != activeDirector.playableAsset)
        {
            RebuildCache();
        }

        TimelineName = activeDirector.playableAsset != null
            ? activeDirector.playableAsset.name
            : activeDirector.name;
        BuildActiveClipText(activeDirector.time);
    }

    public List<PerformanceLoadSuspect> CaptureSuspects(float correlationWindow, int maximumCount)
    {
        var result = new List<PerformanceLoadSuspect>();
        if (activeDirector == null)
        {
            return result;
        }

        double time = activeDirector.time;
        for (int i = 0; i < clips.Count; i++)
        {
            ClipProfile profile = clips[i];
            TimelineClip clip = profile.clip;
            if (clip == null || profile.track == null || profile.track.muted)
            {
                continue;
            }

            bool active = time >= clip.start && time <= clip.end;
            bool recentlyStarted = Math.Abs(time - clip.start) <= correlationWindow;
            if (!active && !recentlyStarted)
            {
                continue;
            }

            PerformanceLoadSuspect suspect = CreateSuspect(profile, time, recentlyStarted);
            result.Add(suspect);
        }

        result.Sort((a, b) => b.score.CompareTo(a.score));
        if (result.Count > maximumCount)
        {
            result.RemoveRange(maximumCount, result.Count - maximumCount);
        }

        return result;
    }

    public void Clear()
    {
        activeDirector = null;
        cachedAsset = null;
        directors.Clear();
        clips.Clear();
        rootProfiles.Clear();
        TimelineName = "-";
        ActiveClips = "-";
    }

    private void FindActiveDirector(float realtime)
    {
        bool hasKnownDirector = false;
        for (int i = directors.Count - 1; i >= 0; i--)
        {
            PlayableDirector director = directors[i];
            if (director == null)
            {
                directors.RemoveAt(i);
                continue;
            }

            hasKnownDirector = true;
            if (director.state == PlayState.Playing)
            {
                SelectDirector(director);
                return;
            }
        }

        SelectDirector(null);

        if (realtime < nextDirectorSearchTime)
        {
            return;
        }

        nextDirectorSearchTime = realtime + (hasKnownDirector ? 2f : 1f);
        directors.Clear();
        PlayableDirector[] found = UnityEngine.Object.FindObjectsOfType<PlayableDirector>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
            {
                directors.Add(found[i]);
            }
        }

        PlayableDirector selected = null;
        for (int i = 0; i < directors.Count; i++)
        {
            if (directors[i].state == PlayState.Playing)
            {
                selected = directors[i];
                break;
            }
        }

        SelectDirector(selected);
    }

    private void SelectDirector(PlayableDirector selected)
    {
        if (selected != activeDirector)
        {
            activeDirector = selected;
            cachedAsset = null;
            clips.Clear();
            rootProfiles.Clear();
        }
    }

    private void RebuildCache()
    {
        clips.Clear();
        rootProfiles.Clear();
        cachedAsset = activeDirector != null ? activeDirector.playableAsset : null;

        TimelineAsset timeline = cachedAsset as TimelineAsset;
        if (timeline == null)
        {
            return;
        }

        foreach (TrackAsset rootTrack in timeline.GetRootTracks())
        {
            CacheTrackRecursive(rootTrack);
        }
    }

    private void CacheTrackRecursive(TrackAsset track)
    {
        if (track == null)
        {
            return;
        }

        GameObject boundRoot = ResolveBoundRoot(track);
        foreach (TimelineClip clip in track.GetClips())
        {
            GameObject candidateRoot = ResolveClipRoot(clip) ?? boundRoot;
            RootProfile rootProfile = candidateRoot != null ? GetOrCreateRootProfile(candidateRoot) : null;
            clips.Add(new ClipProfile
            {
                clip = clip,
                track = track,
                clipName = string.IsNullOrEmpty(clip.displayName) ? clip.asset?.name ?? "Unnamed Clip" : clip.displayName,
                trackName = track.name,
                objectName = candidateRoot != null ? candidateRoot.name : "-",
                root = rootProfile
            });
        }

        foreach (TrackAsset child in track.GetChildTracks())
        {
            CacheTrackRecursive(child);
        }
    }

    private GameObject ResolveBoundRoot(TrackAsset track)
    {
        if (activeDirector == null)
        {
            return null;
        }

        UnityEngine.Object binding = activeDirector.GetGenericBinding(track);
        if (binding is GameObject gameObject)
        {
            return gameObject;
        }

        if (binding is Component component)
        {
            return component.gameObject;
        }

        return null;
    }

    private GameObject ResolveClipRoot(TimelineClip clip)
    {
        if (!(clip.asset is ControlPlayableAsset control) || activeDirector == null)
        {
            return null;
        }

        GameObject source = control.sourceGameObject.Resolve(activeDirector);
        if (source != null)
        {
            return source;
        }

        return control.prefabGameObject;
    }

    private RootProfile GetOrCreateRootProfile(GameObject root)
    {
        int id = root.GetInstanceID();
        if (rootProfiles.TryGetValue(id, out RootProfile profile))
        {
            return profile;
        }

        profile = new RootProfile
        {
            root = root,
            renderers = root.GetComponentsInChildren<Renderer>(true),
            particles = root.GetComponentsInChildren<ParticleSystem>(true),
            rigidbodies = root.GetComponentsInChildren<Rigidbody>(true).Length
        };

        for (int i = 0; i < profile.renderers.Length; i++)
        {
            Renderer renderer = profile.renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (renderer.shadowCastingMode != ShadowCastingMode.Off)
            {
                profile.shadowCasters++;
            }

            if (renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object)
            {
                profile.objectMotionVectors++;
            }

            if (renderer is SkinnedMeshRenderer)
            {
                profile.skinnedRenderers++;
            }
        }

        rootProfiles.Add(id, profile);
        return profile;
    }

    private PerformanceLoadSuspect CreateSuspect(ClipProfile profile, double time, bool recentlyStarted)
    {
        RootProfile root = profile.root;
        int activeRenderers = 0;
        int aliveParticles = 0;
        bool rootActive = false;

        if (root != null && root.root != null)
        {
            rootActive = root.root.activeInHierarchy;
            for (int i = 0; i < root.renderers.Length; i++)
            {
                Renderer renderer = root.renderers[i];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    activeRenderers++;
                }
            }

            for (int i = 0; i < root.particles.Length; i++)
            {
                ParticleSystem particle = root.particles[i];
                if (particle != null && particle.gameObject.activeInHierarchy)
                {
                    aliveParticles += particle.particleCount;
                }
            }
        }

        int rendererCount = root?.renderers.Length ?? 0;
        int shadowCount = root?.shadowCasters ?? 0;
        int motionCount = root?.objectMotionVectors ?? 0;
        int particleCount = root?.particles.Length ?? 0;
        int score = recentlyStarted ? 35 : 15;
        score += Mathf.Min(25, activeRenderers / 10);
        score += Mathf.Min(15, shadowCount / 15);
        score += Mathf.Min(15, motionCount / 15);
        score += Mathf.Min(10, aliveParticles / 1000);

        string confidence = score >= 70 ? "높음" : score >= 45 ? "중간" : "관련 가능성";
        return new PerformanceLoadSuspect
        {
            clipName = profile.clipName,
            trackName = profile.trackName,
            objectName = profile.objectName,
            clipStart = profile.clip.start,
            clipEnd = profile.clip.end,
            rendererCount = rendererCount,
            activeRendererCount = activeRenderers,
            shadowCasterCount = shadowCount,
            objectMotionVectorCount = motionCount,
            particleSystemCount = particleCount,
            aliveParticleCount = aliveParticles,
            skinnedRendererCount = root?.skinnedRenderers ?? 0,
            rigidbodyCount = root?.rigidbodies ?? 0,
            rootActive = rootActive,
            score = score,
            confidence = confidence
        };
    }

    private void BuildActiveClipText(double time)
    {
        activeBuilder.Length = 0;
        int shown = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            ClipProfile profile = clips[i];
            if (profile.track == null || profile.track.muted || time < profile.clip.start || time > profile.clip.end)
            {
                continue;
            }

            if (shown > 0)
            {
                activeBuilder.Append(", ");
            }

            activeBuilder.Append(profile.clipName);
            shown++;
            if (shown >= 5)
            {
                activeBuilder.Append(" ...");
                break;
            }
        }

        ActiveClips = shown == 0 ? "-" : activeBuilder.ToString();
    }
}
