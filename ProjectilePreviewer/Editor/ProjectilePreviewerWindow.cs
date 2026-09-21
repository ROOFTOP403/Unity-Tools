using System.Linq;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;
using RoofTopStudio.ProjectilePreviewer;
using UnityEditor;
using UnityEngine;

namespace RoofTopStudio.ProjectilePreviewer.Editor
{
    public sealed class ProjectilePreviewerWindow : EditorWindow
    {
        const int PathSamples = 48;

        ProjectilePreviewerSession session;
        GameObject previewTarget;
        bool initializedTargetFromSelection;
        bool isPlaying;
        bool loopPlayback;
        bool drawPath;
        double lastUpdateTime;
        float previewTime;

        [MenuItem("Tools/RoofTop Studio/Projectile Previewer")]
        static void Open()
        {
            GetWindow<ProjectilePreviewerWindow>("Projectile Previewer");
        }

        void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            StopSession();
            Selection.selectionChanged -= Repaint;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnGUI()
        {
            if (!initializedTargetFromSelection)
            {
                previewTarget = Selection.activeGameObject;
                initializedTargetFromSelection = true;
            }

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            previewTarget = (GameObject)EditorGUILayout.ObjectField("Target", previewTarget, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                StopSession();
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
            {
                if (GUILayout.Button("Use Selection"))
                {
                    previewTarget = Selection.activeGameObject;
                    StopSession();
                }
            }

            using (new EditorGUI.DisabledScope(previewTarget == null))
            {
                if (GUILayout.Button("Reset Target"))
                {
                    previewTarget = null;
                    StopSession();
                }
            }
            EditorGUILayout.EndHorizontal();

            IProjectilePreviewTweenSource source = FindSource(previewTarget);
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button(session == null ? "Start Preview" : "Refresh Preview"))
                {
                    StartSession(source);
                }
            }

            if (source == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with a DOTweenPath component.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(session == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(isPlaying ? "Pause" : "Play"))
                {
                    isPlaying = !isPlaying;
                    lastUpdateTime = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("Restart"))
                {
                    previewTime = 0f;
                    session.Restart();
                    isPlaying = false;
                }

                if (GUILayout.Button("Stop"))
                {
                    StopSession();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                previewTime = EditorGUILayout.Slider("Time", previewTime, 0f, session != null ? session.Duration : source.PreviewDuration);
                if (EditorGUI.EndChangeCheck() && session != null)
                {
                    isPlaying = false;
                    session.SetTime(previewTime);
                }

                loopPlayback = EditorGUILayout.Toggle("Loop", loopPlayback);

                using (new EditorGUI.DisabledScope(session != null && !session.CanSamplePath))
                {
                    drawPath = EditorGUILayout.Toggle("Draw Path", drawPath);
                }

                if (session != null && !session.CanSamplePath)
                {
                    EditorGUILayout.HelpBox("This source cannot draw a sampled path. Playback, particles, and TrailRenderer preview still work.", MessageType.None);
                }
            }
        }

        void OnEditorUpdate()
        {
            if (session == null || !isPlaying)
            {
                lastUpdateTime = EditorApplication.timeSinceStartup;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Min((float)(now - lastUpdateTime), 0.1f);
            lastUpdateTime = now;

            session.Tick(deltaTime, loopPlayback);
            if (!session.IsValid)
            {
                StopSession();
                Repaint();
                return;
            }

            previewTime = session.Time;
            if (!loopPlayback && session.IsComplete)
            {
                isPlaying = false;
            }

            Repaint();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (!drawPath || session == null || session.Root == null || !session.CanSamplePath)
            {
                return;
            }

            Vector3[] points = session.SamplePath(PathSamples);
            if (points.Length < 2)
            {
                return;
            }

            Handles.color = new Color(0.1f, 0.75f, 1f, 0.9f);
            Handles.DrawAAPolyLine(4f, points);

            Handles.color = new Color(1f, 1f, 1f, 0.85f);
            Handles.SphereHandleCap(0, points[0], Quaternion.identity, HandleUtility.GetHandleSize(points[0]) * 0.08f, EventType.Repaint);
            Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Handles.SphereHandleCap(0, points[points.Length - 1], Quaternion.identity, HandleUtility.GetHandleSize(points[points.Length - 1]) * 0.08f, EventType.Repaint);
        }

        void StartSession(IProjectilePreviewTweenSource source)
        {
            StopSession();
            if (source == null)
            {
                return;
            }

            session = new ProjectilePreviewerSession(source);
            previewTime = 0f;
            isPlaying = false;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            SceneView.RepaintAll();
        }

        void StopSession()
        {
            if (session != null)
            {
                session.Dispose();
                session = null;
            }

            previewTime = 0f;
            isPlaying = false;
        }

        static IProjectilePreviewTweenSource FindSource(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            IProjectilePreviewTweenSource source = gameObject.GetComponents<MonoBehaviour>()
                .OfType<IProjectilePreviewTweenSource>()
                .FirstOrDefault();

            if (source != null)
            {
                return source;
            }

            DOTweenPath path = gameObject.GetComponentsInChildren<DOTweenPath>(true)
                .FirstOrDefault(pathComponent => pathComponent != null);
            if (path == null)
            {
                path = gameObject.GetComponentInParent<DOTweenPath>();
            }

            return path != null ? new DOTweenPathPreviewSourceAdapter(path) : null;
        }

        sealed class DOTweenPathPreviewSourceAdapter : IProjectilePreviewTweenSource
        {
            readonly DOTweenPath path;
            DOTweenPathData data;

            public DOTweenPathPreviewSourceAdapter(DOTweenPath path)
            {
                this.path = path;
            }

            public Transform PreviewRoot => path.transform;
            public bool IsValid => path != null;

            public float PreviewDuration
            {
                get
                {
                    if (!IsValid)
                    {
                        return 0.01f;
                    }

                    DOTweenPathData data = DOTweenPathData.Read(path);
                    return data.Delay + data.Duration * data.Loops;
                }
            }

            public bool SupportsRetargetedPreview => true;
            public bool UsesExternalTimeControl => false;

            public Tween CreatePreviewTween(Transform target)
            {
                if (!IsValid)
                {
                    return null;
                }

                data = DOTweenPathData.Read(path);
                if (data.Waypoints.Length == 0)
                {
                    return null;
                }

                TweenerCore<Vector3, Path, PathOptions> tween = data.IsLocal
                    ? target.DOLocalPath(data.Waypoints, data.Duration, data.PathType, data.PathMode, data.PathResolution)
                    : target.DOPath(data.Waypoints, data.Duration, data.PathType, data.PathMode, data.PathResolution);

                tween.SetOptions(data.IsClosedPath, AxisConstraint.None, data.LockRotation);
                ApplyOrientation(tween, data);

                if (data.Relative)
                {
                    tween.SetRelative();
                }

                if (data.EaseType == Ease.INTERNAL_Custom)
                {
                    tween.SetEase(data.EaseCurve);
                }
                else
                {
                    tween.SetEase(data.EaseType);
                }

                tween.SetDelay(data.Delay)
                    .SetLoops(data.Loops, data.LoopType)
                    .SetAutoKill(false);

                return tween;
            }

            static void ApplyOrientation(TweenerCore<Vector3, Path, PathOptions> tween, DOTweenPathData data)
            {
                Vector3? forwardDirection = data.AssignForwardAndUp ? data.ForwardDirection : (Vector3?)null;
                Vector3? upDirection = data.AssignForwardAndUp ? data.UpDirection : (Vector3?)null;

                switch (data.OrientType)
                {
                    case 0:
                        tween.SetLookAt(data.LookAhead, forwardDirection, upDirection);
                        break;
                    case 1:
                        if (data.LookAtTransform != null)
                        {
                            tween.SetLookAt(data.LookAtTransform, forwardDirection, upDirection);
                        }
                        break;
                    case 2:
                        tween.SetLookAt(data.LookAtPosition, forwardDirection, upDirection);
                        break;
                }
            }

            public void EvaluatePreviewTime(float time)
            {
            }

            public void ApplyPreviewOrientation(Transform target, Vector3 moveDirection)
            {
                if (moveDirection.sqrMagnitude <= 0.000001f)
                {
                    return;
                }

                Vector3 forward = data.AssignForwardAndUp && data.ForwardDirection.sqrMagnitude > 0.000001f
                    ? data.ForwardDirection.normalized
                    : Vector3.forward;
                Vector3 up = data.AssignForwardAndUp && data.UpDirection.sqrMagnitude > 0.000001f
                    ? data.UpDirection.normalized
                    : Vector3.up;

                Quaternion desiredRotation = Quaternion.LookRotation(moveDirection, up)
                    * Quaternion.Inverse(Quaternion.LookRotation(forward, up));

                if (data.LockRotation != AxisConstraint.None)
                {
                    Vector3 currentEuler = target.rotation.eulerAngles;
                    Vector3 desiredEuler = desiredRotation.eulerAngles;

                    if ((data.LockRotation & AxisConstraint.X) != 0)
                    {
                        desiredEuler.x = currentEuler.x;
                    }

                    if ((data.LockRotation & AxisConstraint.Y) != 0)
                    {
                        desiredEuler.y = currentEuler.y;
                    }

                    if ((data.LockRotation & AxisConstraint.Z) != 0)
                    {
                        desiredEuler.z = currentEuler.z;
                    }

                    desiredRotation = Quaternion.Euler(desiredEuler);
                }

                target.rotation = desiredRotation;
            }

            public void StopPreview()
            {
            }

            struct DOTweenPathData
            {
                public Vector3[] Waypoints;
                public float Duration;
                public float Delay;
                public int Loops;
                public Ease EaseType;
                public AnimationCurve EaseCurve;
                public LoopType LoopType;
                public PathType PathType;
                public PathMode PathMode;
                public int PathResolution;
                public int OrientType;
                public Transform LookAtTransform;
                public Vector3 LookAtPosition;
                public float LookAhead;
                public AxisConstraint LockRotation;
                public bool AssignForwardAndUp;
                public Vector3 ForwardDirection;
                public Vector3 UpDirection;
                public bool IsLocal;
                public bool IsClosedPath;
                public bool Relative;

                public static DOTweenPathData Read(DOTweenPath path)
                {
                    if (path == null)
                    {
                        return Default;
                    }

                    SerializedObject serializedObject = new SerializedObject(path);
                    return new DOTweenPathData
                    {
                        Waypoints = ReadVector3Array(serializedObject.FindProperty("wps")),
                        Duration = Mathf.Max(0.01f, ReadFloat(serializedObject, "duration", 1f)),
                        Delay = Mathf.Max(0f, ReadFloat(serializedObject, "delay", 0f)),
                        Loops = Mathf.Max(1, ReadInt(serializedObject, "loops", 1)),
                        EaseType = (Ease)ReadInt(serializedObject, "easeType", (int)Ease.OutQuad),
                        EaseCurve = ReadCurve(serializedObject, "easeCurve"),
                        LoopType = (LoopType)ReadInt(serializedObject, "loopType", (int)LoopType.Restart),
                        PathType = (PathType)ReadInt(serializedObject, "pathType", (int)PathType.CatmullRom),
                        PathMode = (PathMode)ReadInt(serializedObject, "pathMode", (int)PathMode.Full3D),
                        PathResolution = Mathf.Max(1, ReadInt(serializedObject, "pathResolution", 10)),
                        OrientType = ReadInt(serializedObject, "orientType", 0),
                        LookAtTransform = ReadObject<Transform>(serializedObject, "lookAtTransform"),
                        LookAtPosition = ReadVector3(serializedObject, "lookAtPosition", Vector3.zero),
                        LookAhead = Mathf.Clamp01(ReadFloat(serializedObject, "lookAhead", 0.01f)),
                        LockRotation = (AxisConstraint)ReadInt(serializedObject, "lockRotation", (int)AxisConstraint.None),
                        AssignForwardAndUp = ReadBool(serializedObject, "assignForwardAndUp", false),
                        ForwardDirection = ReadVector3(serializedObject, "forwardDirection", Vector3.forward),
                        UpDirection = ReadVector3(serializedObject, "upDirection", Vector3.up),
                        IsLocal = ReadBool(serializedObject, "isLocal", false),
                        IsClosedPath = ReadBool(serializedObject, "isClosedPath", false),
                        Relative = ReadBool(serializedObject, "relative", false)
                    };
                }

                static DOTweenPathData Default => new DOTweenPathData
                {
                    Waypoints = new Vector3[0],
                    Duration = 0.01f,
                    Delay = 0f,
                    Loops = 1,
                    EaseType = Ease.OutQuad,
                    EaseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    LoopType = LoopType.Restart,
                    PathType = PathType.CatmullRom,
                    PathMode = PathMode.Full3D,
                    PathResolution = 10,
                    LookAhead = 0.01f,
                    ForwardDirection = Vector3.forward,
                    UpDirection = Vector3.up
                };

                static Vector3[] ReadVector3Array(SerializedProperty property)
                {
                    if (property == null || !property.isArray)
                    {
                        return new Vector3[0];
                    }

                    Vector3[] values = new Vector3[property.arraySize];
                    for (int i = 0; i < property.arraySize; i++)
                    {
                        values[i] = property.GetArrayElementAtIndex(i).vector3Value;
                    }

                    return values;
                }

                static float ReadFloat(SerializedObject serializedObject, string propertyName, float fallback)
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyName);
                    return property != null ? property.floatValue : fallback;
                }

                static int ReadInt(SerializedObject serializedObject, string propertyName, int fallback)
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyName);
                    return property != null ? property.intValue : fallback;
                }

                static bool ReadBool(SerializedObject serializedObject, string propertyName, bool fallback)
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyName);
                    return property != null ? property.boolValue : fallback;
                }

                static Vector3 ReadVector3(SerializedObject serializedObject, string propertyName, Vector3 fallback)
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyName);
                    return property != null ? property.vector3Value : fallback;
                }

                static T ReadObject<T>(SerializedObject serializedObject, string propertyName) where T : Object
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyName);
                    return property != null ? property.objectReferenceValue as T : null;
                }

                static AnimationCurve ReadCurve(SerializedObject serializedObject, string propertyName)
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyName);
                    return property != null ? property.animationCurveValue : AnimationCurve.Linear(0f, 0f, 1f, 1f);
                }
            }
        }
    }
}
