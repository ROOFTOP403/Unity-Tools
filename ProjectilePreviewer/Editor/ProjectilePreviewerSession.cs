using System;
using DG.Tweening;
using RoofTopStudio.ProjectilePreviewer;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoofTopStudio.ProjectilePreviewer.Editor
{
    internal sealed class ProjectilePreviewerSession : IDisposable
    {
        const float SimulationStep = 1f / 60f;
        static readonly Vector3[] EmptyPathPoints = new Vector3[0];

        readonly IProjectilePreviewTweenSource source;
        readonly Transform root;
        readonly GameObject previewObject;
        readonly ParticleSystem[] particleSystems;
        readonly TrailRenderer[] trailRenderers;
        readonly Vector3 initialPosition;
        readonly Quaternion initialRotation;
        readonly Vector3 initialLocalPosition;
        readonly Quaternion initialLocalRotation;
        readonly Vector3 initialLocalScale;

        Tween tween;
        Vector3[] cachedPathPoints;
        int cachedPathSampleCount;
        float time;

        public ProjectilePreviewerSession(IProjectilePreviewTweenSource source)
        {
            this.source = source;
            Transform originalRoot = source.PreviewRoot;
            previewObject = Object.Instantiate(originalRoot.gameObject, originalRoot.parent);
            previewObject.name = originalRoot.name + " (Projectile Previewer)";
            SetHideFlagsRecursive(previewObject, HideFlags.HideAndDontSave);
            root = previewObject.transform;
            particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            trailRenderers = root.GetComponentsInChildren<TrailRenderer>(true);
            initialPosition = root.position;
            initialRotation = root.rotation;
            initialLocalPosition = root.localPosition;
            initialLocalRotation = root.localRotation;
            initialLocalScale = root.localScale;
            Reset();
        }

        public Transform Root => root;
        public bool IsValid => root != null && source.IsValid;
        public float Duration => Mathf.Max(0.01f, source.PreviewDuration);
        public float Time => time;
        public bool CanSamplePath => source.SupportsRetargetedPreview;
        public bool IsComplete => time >= Duration;

        public void Dispose()
        {
            KillTween();
            if (previewObject != null)
            {
                Object.DestroyImmediate(previewObject);
            }

            SceneView.RepaintAll();
        }

        public void Restart()
        {
            Reset();
            SceneView.RepaintAll();
        }

        public void SetTime(float targetTime)
        {
            targetTime = Mathf.Clamp(targetTime, 0f, Duration);
            Reset();

            if (targetTime <= 0f)
            {
                time = 0f;
                return;
            }

            for (float stepTime = 0f; stepTime < targetTime; stepTime += SimulationStep)
            {
                float nextTime = Mathf.Min(stepTime + SimulationStep, targetTime);
                Evaluate(nextTime, nextTime - stepTime);
            }

            time = targetTime;
            SceneView.RepaintAll();
        }

        public void Tick(float deltaTime, bool loop)
        {
            if (deltaTime <= 0f || !IsValid)
            {
                return;
            }

            float targetTime = time + deltaTime;
            if (targetTime > Duration)
            {
                if (!loop)
                {
                    SetTime(Duration);
                    return;
                }

                Reset();
                targetTime %= Duration;
            }

            for (float stepTime = time; stepTime < targetTime; stepTime += SimulationStep)
            {
                float nextTime = Mathf.Min(stepTime + SimulationStep, targetTime);
                Evaluate(nextTime, nextTime - stepTime);
            }

            time = targetTime;
            SceneView.RepaintAll();
        }

        public Vector3[] SamplePath(int sampleCount)
        {
            sampleCount = Mathf.Max(2, sampleCount);
            if (!source.SupportsRetargetedPreview)
            {
                return EmptyPathPoints;
            }

            if (cachedPathPoints != null && cachedPathSampleCount == sampleCount)
            {
                return cachedPathPoints;
            }

            Vector3[] points = new Vector3[sampleCount];
            GameObject samplerObject = new GameObject("Projectile Previewer Path Sampler")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Transform sampler = samplerObject.transform;
            sampler.SetParent(root.parent, false);
            sampler.localPosition = initialLocalPosition;
            sampler.localRotation = initialLocalRotation;
            sampler.localScale = initialLocalScale;

            Tween sampleTween = null;

            try
            {
                sampleTween = source.CreatePreviewTween(sampler);
                sampleTween.SetUpdate(UpdateType.Manual)
                    .SetAutoKill(false)
                    .Pause();

                for (int i = 0; i < sampleCount; i++)
                {
                    float sampleTime = Duration * i / (sampleCount - 1);
                    sampleTween.Goto(sampleTime, false);
                    points[i] = sampler.position;
                }
            }
            finally
            {
                if (sampleTween != null)
                {
                    sampleTween.Kill(false);
                }

                Object.DestroyImmediate(samplerObject);
            }

            cachedPathPoints = points;
            cachedPathSampleCount = sampleCount;
            return points;
        }

        void Reset()
        {
            KillTween();
            if (root == null)
            {
                return;
            }

            RestoreRoot();
            ClearEffects();
            cachedPathPoints = null;
            cachedPathSampleCount = 0;
            CreateTween();
            time = 0f;
        }

        static void SetHideFlagsRecursive(GameObject gameObject, HideFlags hideFlags)
        {
            foreach (Transform child in gameObject.GetComponentsInChildren<Transform>(true))
            {
                if (child != null)
                {
                    child.gameObject.hideFlags = hideFlags;
                }
            }
        }

        void Evaluate(float targetTime, float deltaTime)
        {
            if (root == null)
            {
                return;
            }

            Vector3 previousPosition = root.position;

            if (source.UsesExternalTimeControl)
            {
                source.EvaluatePreviewTime(targetTime);
            }
            else
            {
                tween.Goto(targetTime, false);
            }

            Vector3 moveDirection = root.position - previousPosition;
            if (moveDirection.sqrMagnitude > 0.000001f)
            {
                source.ApplyPreviewOrientation(root, moveDirection.normalized);
            }

            AddTrailPositions();
            SimulateParticles(deltaTime);
        }

        void CreateTween()
        {
            tween = source.CreatePreviewTween(root);
            if (tween == null)
            {
                if (source.UsesExternalTimeControl)
                {
                    source.EvaluatePreviewTime(0f);
                    return;
                }

                throw new InvalidOperationException("Preview tween source returned null.");
            }

            tween.SetUpdate(UpdateType.Manual)
                .SetAutoKill(false)
                .Pause();
        }

        void KillTween()
        {
            if (tween == null)
            {
                source.StopPreview();
                return;
            }

            tween.Kill(false);
            tween = null;
            source.StopPreview();
        }

        void RestoreRoot()
        {
            if (root == null)
            {
                return;
            }

            root.position = initialPosition;
            root.rotation = initialRotation;
            root.localPosition = initialLocalPosition;
            root.localRotation = initialLocalRotation;
            root.localScale = initialLocalScale;
        }

        void ClearEffects()
        {
            if (root == null)
            {
                return;
            }

            foreach (TrailRenderer trailRenderer in trailRenderers)
            {
                if (trailRenderer != null)
                {
                    trailRenderer.Clear();
                }
            }

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                if (particleSystem == null)
                {
                    continue;
                }

                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Simulate(0f, true, true, false);
                particleSystem.Play(false);
            }
        }

        void AddTrailPositions()
        {
            foreach (TrailRenderer trailRenderer in trailRenderers)
            {
                if (trailRenderer != null)
                {
                    trailRenderer.AddPosition(trailRenderer.transform.position);
                }
            }
        }

        void SimulateParticles(float deltaTime)
        {
            foreach (ParticleSystem particleSystem in particleSystems)
            {
                if (particleSystem != null)
                {
                    particleSystem.Simulate(deltaTime, false, false, false);
                }
            }
        }
    }
}
