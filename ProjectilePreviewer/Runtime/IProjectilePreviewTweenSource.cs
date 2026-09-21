using DG.Tweening;
using UnityEngine;

namespace RoofTopStudio.ProjectilePreviewer
{
    public interface IProjectilePreviewTweenSource
    {
        Transform PreviewRoot { get; }
        bool IsValid { get; }
        float PreviewDuration { get; }
        bool SupportsRetargetedPreview { get; }
        bool UsesExternalTimeControl { get; }

        Tween CreatePreviewTween(Transform target);
        void EvaluatePreviewTime(float time);
        void ApplyPreviewOrientation(Transform target, Vector3 moveDirection);
        void StopPreview();
    }
}
