using DG.Tweening;
using UnityEngine;

namespace RoofTopStudio.ProjectilePreviewer
{
    public sealed class ProjectileDOTweenPreviewSource : MonoBehaviour, IProjectilePreviewTweenSource
    {
        [SerializeField] Transform previewRoot;
        [SerializeField] bool useLocalPath;
        [SerializeField] bool useRelativePath = true;
        [SerializeField] Vector3[] path = { Vector3.zero, Vector3.forward * 5f };
        [SerializeField, Min(0.01f)] float duration = 1f;
        [SerializeField] AnimationCurve progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] PathType pathType = PathType.CatmullRom;
        [SerializeField] PathMode pathMode = PathMode.Full3D;
        [SerializeField, Min(1)] int resolution = 10;

        public Transform PreviewRoot => previewRoot != null ? previewRoot : transform;
        public bool IsValid => this != null && PreviewRoot != null;
        public float PreviewDuration => duration;
        public bool SupportsRetargetedPreview => true;
        public bool UsesExternalTimeControl => false;

        public Tween CreatePreviewTween(Transform target)
        {
            Tween tween = useLocalPath
                ? target.DOLocalPath(path, duration, pathType, pathMode, resolution)
                : target.DOPath(path, duration, pathType, pathMode, resolution);

            if (useRelativePath)
            {
                tween.SetRelative();
            }

            return tween.SetEase(progressCurve);
        }

        public void EvaluatePreviewTime(float time)
        {
        }

        public void ApplyPreviewOrientation(Transform target, Vector3 moveDirection)
        {
        }

        public void StopPreview()
        {
        }
    }
}
