using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class PerformanceMonitorWindow : EditorWindow
{
    private const string SettingsFolderPath = "Assets/RoofTopStudio/Tools/PerformanceMonitor/Resources";
    private const string SettingsAssetPath = SettingsFolderPath + "/PerformanceMonitorSettings.asset";
    private Vector2 scroll;
    private PerformanceMonitorSettings settings;
    private SerializedObject serializedSettings;

    [MenuItem("Tools/RoofTop Studio/Performance Monitor")]
    public static void Open()
    {
        GetWindow<PerformanceMonitorWindow>("Performance Monitor");
    }

    private void OnEnable()
    {
        LoadSettings();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Performance Diagnostics", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Play Mode에서만 작동합니다. F8은 HUD 표시, F9는 수동 캡처입니다. " +
            "프레임 spike가 감지되면 CSV와 한국어 원인 요약을 자동 저장합니다.",
            MessageType.Info);

        DrawRuntimeControls();
        EditorGUILayout.Space(8f);
        DrawSettings();
    }

    private void DrawRuntimeControls()
    {
        EditorGUILayout.LabelField("실행 상태", EditorStyles.boldLabel);
        DrawAutoStartToggle();

        PerformanceMonitor monitor = PerformanceMonitor.Instance;
        if (!EditorApplication.isPlaying)
        {
            string message = settings != null && !settings.startAutomaticallyOnPlay
                ? "Play 시 Monitor를 자동으로 시작하지 않습니다."
                : "Play 버튼을 누르면 Monitor가 자동으로 시작됩니다.";
            EditorGUILayout.HelpBox(message, MessageType.None);
        }
        else if (monitor == null)
        {
            EditorGUILayout.HelpBox(
                settings != null && !settings.startAutomaticallyOnPlay
                    ? "자동 실행이 꺼져 있어 Monitor가 실행되지 않았습니다."
                    : "Monitor 초기화를 기다리는 중입니다.",
                MessageType.None);
            if (GUILayout.Button("Monitor 수동 시작"))
            {
                PerformanceMonitor.CreateMonitor();
            }
        }
        else
        {
            EditorGUILayout.LabelField("Monitor", monitor.enabled ? "실행 중" : "중지됨");
            EditorGUILayout.LabelField("HUD", monitor.HudVisible ? "표시 중" : "숨김");
            EditorGUILayout.LabelField("Capture", monitor.IsCapturing ? "기록 중" : "대기");
            EditorGUILayout.LabelField("최근 로그", monitor.LastLogPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(monitor.enabled ? "Monitor 중지" : "Monitor 시작"))
            {
                if (monitor.enabled) monitor.StopMonitoring();
                else monitor.StartMonitoring();
            }
            if (GUILayout.Button("HUD 켜기/끄기"))
            {
                monitor.ToggleHud();
            }
            if (GUILayout.Button("지금 캡처"))
            {
                monitor.CaptureNow();
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("로그 폴더 열기"))
        {
            string directoryName = settings != null && !string.IsNullOrWhiteSpace(settings.logDirectoryName)
                ? settings.logDirectoryName
                : "PerformanceMonitor";
            string path = Path.Combine(Application.persistentDataPath, directoryName);
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }
    }

    private void DrawAutoStartToggle()
    {
        if (settings == null)
        {
            return;
        }

        if (serializedSettings == null || serializedSettings.targetObject != settings)
        {
            serializedSettings = new SerializedObject(settings);
        }

        serializedSettings.Update();
        SerializedProperty property = serializedSettings.FindProperty("startAutomaticallyOnPlay");
        EditorGUILayout.PropertyField(
            property,
            new GUIContent(
                "Play 시 자동 실행",
                "꺼면 Play Mode에 진입해도 Performance Monitor와 HUD가 시작되지 않습니다."));
        serializedSettings.ApplyModifiedProperties();
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("설정", EditorStyles.boldLabel);
        if (settings == null)
        {
            EditorGUILayout.HelpBox(
                "설정 에셋이 없어 기본값으로 동작합니다. 프로젝트별 설정을 저장하려면 아래 버튼을 누르세요.",
                MessageType.Warning);
            if (GUILayout.Button("기본 설정 에셋 생성"))
            {
                CreateSettingsAsset();
            }
            return;
        }

        if (serializedSettings == null || serializedSettings.targetObject != settings)
        {
            serializedSettings = new SerializedObject(settings);
        }

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(280f));
        serializedSettings.Update();
        SerializedProperty iterator = serializedSettings.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }
            else if (iterator.propertyPath == "startAutomaticallyOnPlay")
            {
                continue;
            }
            else
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
        serializedSettings.ApplyModifiedProperties();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("설정 선택"))
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
        if (GUILayout.Button("설정 다시 불러오기"))
        {
            LoadSettings();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void LoadSettings()
    {
        settings = AssetDatabase.LoadAssetAtPath<PerformanceMonitorSettings>(SettingsAssetPath);
        serializedSettings = settings != null ? new SerializedObject(settings) : null;
        Repaint();
    }

    private void CreateSettingsAsset()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolderPath))
        {
            AssetDatabase.CreateFolder("Assets/RoofTopStudio/Tools/PerformanceMonitor", "Resources");
        }

        settings = CreateInstance<PerformanceMonitorSettings>();
        AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        serializedSettings = new SerializedObject(settings);
        Selection.activeObject = settings;
    }
}
