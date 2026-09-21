#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoofTopStudioResourceCleaner : EditorWindow
{
    private const string WindowTitle = "Resource Cleaner";
    private const string SettingsPath = "Assets/RoofTopStudio/Tools/ResourceCleaner/Editor/ResourceCleanerSettings.json";

    private readonly List<MaterialUsageResult> materialResults = new List<MaterialUsageResult>();
    private readonly List<MeshUsageResult> meshResults = new List<MeshUsageResult>();
    private readonly List<TextureUsageResult> textureResults = new List<TextureUsageResult>();
    private readonly HashSet<int> expandedUsageResults = new HashSet<int>();
    private ToolTab currentTab;
    private ScanScope scanScope;
    private ResultDisplayMode resultDisplayMode;
    private Vector2 scrollPosition;
    private bool includeInactiveObjects = true;
    private string prefabFolderPath = "Assets";
    private string materialFolderPath = "Assets";
    private string statusMessage = "Project 창에서 검사할 리소스를 여러 개 선택한 뒤 검사하세요.";
    private MessageType statusType = MessageType.Info;

    private enum ToolTab
    {
        Materials,
        Meshes,
        Textures
    }

    private enum ScanScope
    {
        SceneHierarchy,
        ProjectPrefabs
    }

    private enum ResultDisplayMode
    {
        Unused,
        Used
    }

    [MenuItem("Tools/RoofTop Studio/Resource Cleaner")]
    public static void ShowWindow()
    {
        GetWindow<RoofTopStudioResourceCleaner>(WindowTitle);
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Find Unused Materials In Hierarchy", true)]
    private static bool ValidateFindUnusedMaterialsFromProject()
    {
        return GetSelectedMaterials().Count > 0;
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Find Unused Materials In Hierarchy")]
    private static void FindUnusedMaterialsFromProject()
    {
        RoofTopStudioResourceCleaner window = GetWindow<RoofTopStudioResourceCleaner>(WindowTitle);
        window.currentTab = ToolTab.Materials;
        window.resultDisplayMode = ResultDisplayMode.Unused;
        window.ScanSelectedMaterials();
        window.Focus();
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Find Unused Meshes In Hierarchy", true)]
    private static bool ValidateFindUnusedMeshesFromProject()
    {
        return GetSelectedMeshes().Count > 0;
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Find Unused Meshes In Hierarchy")]
    private static void FindUnusedMeshesFromProject()
    {
        RoofTopStudioResourceCleaner window = GetWindow<RoofTopStudioResourceCleaner>(WindowTitle);
        window.currentTab = ToolTab.Meshes;
        window.resultDisplayMode = ResultDisplayMode.Unused;
        window.ScanSelectedMeshes();
        window.Focus();
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Find Unused Textures In Materials", true)]
    private static bool ValidateFindUnusedTexturesFromProject()
    {
        return GetSelectedTextures().Count > 0;
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Find Unused Textures In Materials")]
    private static void FindUnusedTexturesFromProject()
    {
        RoofTopStudioResourceCleaner window = GetWindow<RoofTopStudioResourceCleaner>(WindowTitle);
        window.currentTab = ToolTab.Textures;
        window.resultDisplayMode = ResultDisplayMode.Unused;
        window.ScanSelectedTextures();
        window.Focus();
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Clear Selected Material Textures", true)]
    private static bool ValidateClearTextures()
    {
        return GetSelectedMaterials().Count > 0;
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Clear Selected Material Textures")]
    private static void ClearSelectedMaterialTextures()
    {
        ClearTextures(GetSelectedMaterials());
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Reset Selected Materials To Shader Defaults", true)]
    private static bool ValidateResetMaterials()
    {
        return GetSelectedMaterials().Count > 0;
    }

    [MenuItem("Assets/RoofTop Studio/Resource Cleaner/Reset Selected Materials To Shader Defaults")]
    private static void ResetSelectedMaterials()
    {
        ResetToShaderDefaults(GetSelectedMaterials());
    }

    private void OnEnable()
    {
        LoadSettings();
    }

    private void OnGUI()
    {
        ToolTab selectedTab = (ToolTab)GUILayout.Toolbar((int)currentTab, new[] { "Material", "Mesh", "Texture" }, GUILayout.Height(28f));
        if (selectedTab != currentTab)
        {
            currentTab = selectedTab;
            scrollPosition = Vector2.zero;
            statusMessage = "Project 창에서 검사할 리소스를 여러 개 선택한 뒤 검사하세요.";
            statusType = MessageType.Info;
        }

        EditorGUILayout.Space(8f);
        DrawScanScopeOptions();
        if (currentTab != ToolTab.Textures)
        {
            EditorGUILayout.Space(4f);
            includeInactiveObjects = EditorGUILayout.ToggleLeft("비활성 오브젝트 포함", includeInactiveObjects, GUILayout.Width(150f));
        }
        EditorGUILayout.Space(4f);

        if (currentTab == ToolTab.Materials)
        {
            DrawMaterialTab();
        }
        else if (currentTab == ToolTab.Meshes)
        {
            DrawMeshTab();
        }
        else
        {
            DrawTextureTab();
        }
    }

    private void DrawScanScopeOptions()
    {
        if (currentTab == ToolTab.Textures)
        {
            DrawMaterialFolderOptions();
            return;
        }

        ScanScope selectedScope = (ScanScope)GUILayout.Toolbar((int)scanScope, new[] { "Scene Hierarchy", "Project Prefabs" }, GUILayout.Height(24f));

        if (selectedScope != scanScope)
        {
            scanScope = selectedScope;
            materialResults.Clear();
            meshResults.Clear();
            textureResults.Clear();
            scrollPosition = Vector2.zero;
            SaveSettings();
        }

        if (scanScope != ScanScope.ProjectPrefabs)
        {
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Prefab Folder", GUILayout.Width(86f));
            prefabFolderPath = EditorGUILayout.TextField(prefabFolderPath);

            if (GUILayout.Button("선택", GUILayout.Width(52f)))
            {
                SelectPrefabFolder();
            }

            if (GUILayout.Button("저장", GUILayout.Width(52f)))
            {
                NormalizeAndSavePrefabFolderPath();
            }
        }
    }

    private void DrawMaterialFolderOptions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Material Folder", GUILayout.Width(96f));
            materialFolderPath = EditorGUILayout.TextField(materialFolderPath);

            if (GUILayout.Button("선택", GUILayout.Width(52f)))
            {
                SelectMaterialFolder();
            }

            if (GUILayout.Button("저장", GUILayout.Width(52f)))
            {
                NormalizeAndSaveMaterialFolderPath();
            }
        }
    }

    private void DrawMaterialTab()
    {
        DrawResultDisplayMode();
        GUILayout.Label(GetUsageTitle("Materials"), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(GetScopeDescription("Material"), MessageType.None);

        DrawScanButtons(GetSelectedMaterials().Count > 0, materialResults.Count > 0, ScanSelectedMaterials, SelectDisplayedMaterials, ClearSelection);
        DrawStatusAndMaterialResults();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
        DrawMaterialResetTools();
    }

    private void DrawMeshTab()
    {
        DrawResultDisplayMode();
        GUILayout.Label(GetUsageTitle("Meshes"), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(GetScopeDescription("Mesh"), MessageType.None);

        DrawScanButtons(GetSelectedMeshes().Count > 0, meshResults.Count > 0, ScanSelectedMeshes, SelectDisplayedMeshes, ClearSelection);
        DrawStatusAndMeshResults();
    }

    private void DrawTextureTab()
    {
        DrawResultDisplayMode();
        GUILayout.Label(GetUsageTitle("Textures"), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(GetScopeDescription("Texture"), MessageType.None);

        DrawScanButtons(GetSelectedTextures().Count > 0, textureResults.Count > 0, ScanSelectedTextures, SelectDisplayedTextures, ClearSelection);
        DrawStatusAndTextureResults();
    }

    private string GetScopeDescription(string assetTypeName)
    {
        if (currentTab == ToolTab.Textures)
        {
            return resultDisplayMode == ResultDisplayMode.Unused
                ? "Project 창에서 선택한 Texture 중 지정된 Material 폴더의 머티리얼에서 참조되지 않는 항목을 표시합니다."
                : "Project 창에서 선택한 Texture 중 지정된 Material 폴더에서 참조 중인 항목과 사용 머티리얼을 표시합니다.";
        }

        string usageDescription = resultDisplayMode == ResultDisplayMode.Unused
            ? "참조되지 않는 항목"
            : "참조 중인 항목과 사용 프리팹";

        if (scanScope == ScanScope.ProjectPrefabs)
        {
            return $"Project 창에서 선택한 {assetTypeName} 중 지정된 폴더의 프리팹에서 {usageDescription}을 표시합니다.";
        }

        return $"Project 창에서 선택한 {assetTypeName} 중 현재 열린 씬 하이어라키에서 {usageDescription}을 표시합니다.";
    }

    private void DrawResultDisplayMode()
    {
        ResultDisplayMode selectedMode = (ResultDisplayMode)GUILayout.Toolbar(
            (int)resultDisplayMode,
            new[] { "미사용 표시", "사용 중 표시" },
            GUILayout.Height(24f));

        if (selectedMode != resultDisplayMode)
        {
            resultDisplayMode = selectedMode;
            scrollPosition = Vector2.zero;
        }
    }

    private string GetUsageTitle(string assetTypeName)
    {
        string usage = resultDisplayMode == ResultDisplayMode.Unused ? "Unused" : "Used";
        if (currentTab == ToolTab.Textures)
        {
            return $"{usage} {assetTypeName} In Project Materials";
        }

        string scope = scanScope == ScanScope.ProjectPrefabs ? "Project Prefabs" : "Scene Hierarchy";
        return $"{usage} {assetTypeName} In {scope}";
    }

    private void DrawScanButtons(bool canScan, bool hasResults, System.Action scanAction, System.Action selectAllAction, System.Action clearSelectionAction)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = canScan;
            if (GUILayout.Button("선택 리소스 검사", GUILayout.Height(28f)))
            {
                scanAction();
            }

            GUI.enabled = hasResults;
            if (GUILayout.Button("전체 선택", GUILayout.Height(28f), GUILayout.Width(110f)))
            {
                selectAllAction();
            }

            GUI.enabled = true;
            if (GUILayout.Button("선택 초기화", GUILayout.Height(28f), GUILayout.Width(90f)))
            {
                clearSelectionAction();
            }
        }
    }

    private void DrawStatusAndMaterialResults()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(statusMessage, statusType);

        if (materialResults.Count == 0)
        {
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(160f));
        foreach (MaterialUsageResult result in materialResults.Where(IsVisibleResult))
        {
            DrawUsageObjectRow(result.Material, result);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawStatusAndMeshResults()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(statusMessage, statusType);

        if (meshResults.Count == 0)
        {
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(160f));
        foreach (MeshUsageResult result in meshResults.Where(IsVisibleResult))
        {
            DrawUsageObjectRow(result.Mesh, result);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawStatusAndTextureResults()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(statusMessage, statusType);

        if (textureResults.Count == 0)
        {
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(160f));
        foreach (TextureUsageResult result in textureResults.Where(
                     result => result.IsUsed == (resultDisplayMode == ResultDisplayMode.Used)))
        {
            DrawTextureUsageRow(result);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawTextureUsageRow(TextureUsageResult result)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(result.Texture, typeof(Texture), false);
                GUILayout.Label(result.IsUsed ? "사용 중" : "미사용", EditorStyles.boldLabel, GUILayout.Width(56f));
                if (GUILayout.Button("선택", GUILayout.Width(46f)))
                {
                    Selection.activeObject = result.Texture;
                    EditorGUIUtility.PingObject(result.Texture);
                }
            }

            if (result.IsUsed)
            {
                DrawTextureUsageDetails(result);
            }
        }
    }

    private void DrawTextureUsageDetails(TextureUsageResult result)
    {
        int assetId = result.Texture.GetInstanceID();
        bool isExpanded = expandedUsageResults.Contains(assetId);
        bool nextExpanded = EditorGUILayout.Foldout(isExpanded, $"사용 머티리얼 {result.Materials.Count}개", true);

        if (nextExpanded != isExpanded)
        {
            if (nextExpanded)
            {
                expandedUsageResults.Add(assetId);
            }
            else
            {
                expandedUsageResults.Remove(assetId);
            }
        }

        if (!nextExpanded)
        {
            return;
        }

        foreach (Material material in result.Materials)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(material, typeof(Material), false);
                if (GUILayout.Button("선택", GUILayout.Width(46f)))
                {
                    Selection.activeObject = material;
                    EditorGUIUtility.PingObject(material);
                }
            }
        }
    }

    private bool IsVisibleResult<T>(UsageResult<T> result) where T : Object
    {
        return result.IsUsed == (resultDisplayMode == ResultDisplayMode.Used);
    }

    private void DrawUsageObjectRow<T>(Object asset, UsageResult<T> result) where T : Object
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(asset, asset.GetType(), false);
                GUILayout.Label(result.IsUsed ? "사용 중" : "미사용", EditorStyles.boldLabel, GUILayout.Width(56f));

                if (GUILayout.Button("선택", GUILayout.Width(46f)))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }

            if (result.IsUsed && result.PrefabUsages.Count > 0)
            {
                DrawUsageDetails(asset, result);
            }
        }
    }

    private void DrawUsageDetails<T>(Object asset, UsageResult<T> result) where T : Object
    {
        int assetId = asset.GetInstanceID();
        bool isExpanded = expandedUsageResults.Contains(assetId);
        bool nextExpanded = EditorGUILayout.Foldout(
            isExpanded,
            $"사용 프리팹 {result.PrefabUsages.Count}개",
            true);

        if (nextExpanded != isExpanded)
        {
            if (nextExpanded)
            {
                expandedUsageResults.Add(assetId);
            }
            else
            {
                expandedUsageResults.Remove(assetId);
            }
        }

        if (!nextExpanded)
        {
            return;
        }

        foreach (PrefabUsageInfo prefabUsage in result.PrefabUsages)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(prefabUsage.Prefab, typeof(GameObject), false);
                if (GUILayout.Button("선택", GUILayout.Width(46f)))
                {
                    Selection.activeObject = prefabUsage.Prefab;
                    EditorGUIUtility.PingObject(prefabUsage.Prefab);
                }
            }

            foreach (string referencePath in prefabUsage.ReferencePaths)
            {
                EditorGUILayout.LabelField(referencePath, EditorStyles.miniLabel);
            }
        }
    }

    private void DrawMaterialResetTools()
    {
        GUILayout.Label("Material Reset", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("복사한 머테리얼에 남아 있는 텍스처를 비우거나, 현재 셰이더의 기본값으로 머테리얼 프로퍼티를 초기화합니다.", MessageType.None);

        List<Material> selectedMaterials = GetSelectedMaterials();
        GUI.enabled = selectedMaterials.Count > 0;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("선택 머테리얼 텍스처 비우기", GUILayout.Height(28f)))
            {
                ClearTextures(selectedMaterials);
            }

            if (GUILayout.Button("선택 머테리얼 셰이더 기본값으로 초기화", GUILayout.Height(28f)))
            {
                if (EditorUtility.DisplayDialog(
                        "Reset Materials",
                        $"{selectedMaterials.Count}개 머테리얼을 현재 셰이더 기본값으로 초기화합니다.\nUndo는 가능하지만, 진행 전에 대상이 맞는지 확인하세요.",
                        "Reset",
                        "Cancel"))
                {
                    ResetToShaderDefaults(selectedMaterials);
                }
            }
        }

        GUI.enabled = true;
    }

    private void ScanSelectedMaterials()
    {
        List<Material> selectedMaterials = GetSelectedMaterials();
        if (selectedMaterials.Count == 0)
        {
            materialResults.Clear();
            statusMessage = "Project 창에서 Material 에셋을 하나 이상 선택하세요.";
            statusType = MessageType.Warning;
            return;
        }

        if (!ValidateCurrentScanScope())
        {
            materialResults.Clear();
            return;
        }

        expandedUsageResults.Clear();

        Dictionary<Material, MaterialUsageResult> resultMap = selectedMaterials
            .Distinct()
            .ToDictionary(material => material, material => new MaterialUsageResult(material));

        HashSet<int> targetIds = new HashSet<int>(resultMap.Keys.Select(material => material.GetInstanceID()));
        ScanOpenScenesForMaterials(resultMap, targetIds);

        materialResults.Clear();
        materialResults.AddRange(resultMap.Values.OrderBy(result => result.IsUsed).ThenBy(result => result.Material.name));

        int unusedCount = materialResults.Count(result => !result.IsUsed);
        statusMessage = $"{selectedMaterials.Count}개 검사 완료. 미사용 {unusedCount}개 / 사용 중 {selectedMaterials.Count - unusedCount}개";
        statusType = unusedCount > 0 ? MessageType.Warning : MessageType.Info;
    }

    private void ScanSelectedMeshes()
    {
        List<Mesh> selectedMeshes = GetSelectedMeshes();
        if (selectedMeshes.Count == 0)
        {
            meshResults.Clear();
            statusMessage = "Project 창에서 Mesh 에셋을 하나 이상 선택하세요.";
            statusType = MessageType.Warning;
            return;
        }

        if (!ValidateCurrentScanScope())
        {
            meshResults.Clear();
            return;
        }

        expandedUsageResults.Clear();

        Dictionary<Mesh, MeshUsageResult> resultMap = selectedMeshes
            .Distinct()
            .ToDictionary(mesh => mesh, mesh => new MeshUsageResult(mesh));

        HashSet<int> targetIds = new HashSet<int>(resultMap.Keys.Select(mesh => mesh.GetInstanceID()));
        ScanOpenScenesForMeshes(resultMap, targetIds);

        meshResults.Clear();
        meshResults.AddRange(resultMap.Values.OrderBy(result => result.IsUsed).ThenBy(result => result.Mesh.name));

        int unusedCount = meshResults.Count(result => !result.IsUsed);
        statusMessage = $"{selectedMeshes.Count}개 검사 완료. 미사용 {unusedCount}개 / 사용 중 {selectedMeshes.Count - unusedCount}개";
        statusType = unusedCount > 0 ? MessageType.Warning : MessageType.Info;
    }

    private void ScanSelectedTextures()
    {
        List<Texture> selectedTextures = GetSelectedTextures();
        if (selectedTextures.Count == 0)
        {
            textureResults.Clear();
            statusMessage = "Project 창에서 Texture 에셋을 하나 이상 선택하세요.";
            statusType = MessageType.Warning;
            return;
        }

        if (!ValidateMaterialFolder())
        {
            textureResults.Clear();
            return;
        }

        expandedUsageResults.Clear();

        Dictionary<Texture, TextureUsageResult> resultMap = selectedTextures
            .Distinct()
            .ToDictionary(texture => texture, texture => new TextureUsageResult(texture));
        HashSet<int> targetIds = new HashSet<int>(resultMap.Keys.Select(texture => texture.GetInstanceID()));

        foreach (Material material in GetTargetMaterials())
        {
            if (material == null || material.shader == null)
            {
                continue;
            }

            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(propertyName);
                if (texture == null || !targetIds.Contains(texture.GetInstanceID()))
                {
                    continue;
                }

                if (resultMap.TryGetValue(texture, out TextureUsageResult result))
                {
                    result.AddMaterial(material);
                }
            }
        }

        textureResults.Clear();
        textureResults.AddRange(resultMap.Values.OrderBy(result => result.IsUsed).ThenBy(result => result.Texture.name));

        int unusedCount = textureResults.Count(result => !result.IsUsed);
        statusMessage = $"{selectedTextures.Count}개 검사 완료. 미사용 {unusedCount}개 / 사용 중 {selectedTextures.Count - unusedCount}개";
        statusType = unusedCount > 0 ? MessageType.Warning : MessageType.Info;
    }

    private IEnumerable<Material> GetTargetMaterials()
    {
        string searchPath = NormalizeAssetFolderPath(materialFolderPath);
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { searchPath });
        foreach (string guid in materialGuids)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
            {
                yield return material;
            }
        }
    }

    private bool ValidateMaterialFolder()
    {
        string searchPath = NormalizeAssetFolderPath(materialFolderPath);
        if (AssetDatabase.IsValidFolder(searchPath))
        {
            return true;
        }

        statusMessage = "유효한 머티리얼 폴더 경로를 지정하세요.";
        statusType = MessageType.Warning;
        return false;
    }

    private bool ValidateCurrentScanScope()
    {
        if (scanScope != ScanScope.ProjectPrefabs)
        {
            return true;
        }

        string searchPath = NormalizeAssetFolderPath(prefabFolderPath);
        if (AssetDatabase.IsValidFolder(searchPath))
        {
            return true;
        }

        statusMessage = "유효한 프리팹 폴더 경로를 지정하세요.";
        statusType = MessageType.Warning;
        return false;
    }

    private void ScanOpenScenesForMaterials(Dictionary<Material, MaterialUsageResult> resultMap, HashSet<int> targetIds)
    {
        foreach (TargetComponent target in GetTargetComponents())
        {
            ScanRendererMaterials(target, resultMap, targetIds);
            ScanSerializedObjectReferences<Material>(target.Component, targetIds, (material, componentType, propertyName, hierarchyPath) =>
            {
                if (resultMap.TryGetValue(material, out MaterialUsageResult result))
                {
                    result.AddReference(hierarchyPath, componentType, propertyName, target.Prefab, target.PrefabPath);
                }
            });
        }
    }

    private void ScanOpenScenesForMeshes(Dictionary<Mesh, MeshUsageResult> resultMap, HashSet<int> targetIds)
    {
        foreach (TargetComponent target in GetTargetComponents())
        {
            ScanKnownMeshComponents(target, resultMap, targetIds);
            ScanSerializedObjectReferences<Mesh>(target.Component, targetIds, (mesh, componentType, propertyName, hierarchyPath) =>
            {
                if (resultMap.TryGetValue(mesh, out MeshUsageResult result))
                {
                    result.AddReference(hierarchyPath, componentType, propertyName, target.Prefab, target.PrefabPath);
                }
            });
        }
    }

    private IEnumerable<TargetComponent> GetTargetComponents()
    {
        if (scanScope == ScanScope.ProjectPrefabs)
        {
            return GetProjectPrefabComponents();
        }

        return GetSceneComponents();
    }

    private IEnumerable<TargetComponent> GetSceneComponents()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(includeInactiveObjects))
                {
                    if (component != null)
                    {
                        GameObject prefab = GetPrefabSource(component, out string prefabPath);
                        yield return new TargetComponent(component, prefab, prefabPath);
                    }
                }
            }
        }
    }

    private IEnumerable<TargetComponent> GetProjectPrefabComponents()
    {
        string searchPath = NormalizeAssetFolderPath(prefabFolderPath);
        if (string.IsNullOrEmpty(searchPath) || !AssetDatabase.IsValidFolder(searchPath))
        {
            statusMessage = "유효한 프리팹 폴더 경로를 지정하세요.";
            statusType = MessageType.Warning;
            yield break;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });
        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                continue;
            }

            foreach (Component component in prefab.GetComponentsInChildren<Component>(includeInactiveObjects))
            {
                if (component != null)
                {
                    yield return new TargetComponent(component, prefab, prefabPath);
                }
            }
        }
    }

    private static GameObject GetPrefabSource(Component component, out string prefabPath)
    {
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(component.gameObject);
        prefabPath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        return string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private void ScanRendererMaterials(TargetComponent target, Dictionary<Material, MaterialUsageResult> resultMap, HashSet<int> targetIds)
    {
        Renderer renderer = target.Component as Renderer;
        if (renderer == null)
        {
            return;
        }

        Material[] sharedMaterials = renderer.sharedMaterials;
        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            Material material = sharedMaterials[i];
            if (material == null || !targetIds.Contains(material.GetInstanceID()))
            {
                continue;
            }

            if (resultMap.TryGetValue(material, out MaterialUsageResult result))
            {
                result.AddReference(
                    BuildHierarchyPath(renderer.transform),
                    renderer.GetType().Name,
                    $"Shared Material {i}",
                    target.Prefab,
                    target.PrefabPath);
            }
        }
    }

    private void ScanKnownMeshComponents(TargetComponent target, Dictionary<Mesh, MeshUsageResult> resultMap, HashSet<int> targetIds)
    {
        Component component = target.Component;
        MeshFilter meshFilter = component as MeshFilter;
        if (meshFilter != null)
        {
            AddMeshReference(meshFilter.sharedMesh, meshFilter, "Shared Mesh", resultMap, targetIds, target.Prefab, target.PrefabPath);
        }

        SkinnedMeshRenderer skinnedMeshRenderer = component as SkinnedMeshRenderer;
        if (skinnedMeshRenderer != null)
        {
            AddMeshReference(skinnedMeshRenderer.sharedMesh, skinnedMeshRenderer, "Shared Mesh", resultMap, targetIds, target.Prefab, target.PrefabPath);
        }

        MeshCollider meshCollider = component as MeshCollider;
        if (meshCollider != null)
        {
            AddMeshReference(meshCollider.sharedMesh, meshCollider, "Shared Mesh", resultMap, targetIds, target.Prefab, target.PrefabPath);
        }
    }

    private void AddMeshReference(
        Mesh mesh,
        Component component,
        string propertyName,
        Dictionary<Mesh, MeshUsageResult> resultMap,
        HashSet<int> targetIds,
        GameObject prefab,
        string prefabPath)
    {
        if (mesh == null || !targetIds.Contains(mesh.GetInstanceID()))
        {
            return;
        }

        if (resultMap.TryGetValue(mesh, out MeshUsageResult result))
        {
            result.AddReference(BuildHierarchyPath(component.transform), component.GetType().Name, propertyName, prefab, prefabPath);
        }
    }

    private void ScanSerializedObjectReferences<T>(Component component, HashSet<int> targetIds, System.Action<T, string, string, string> onFound)
        where T : Object
    {
        SerializedObject serializedObject;
        try
        {
            serializedObject = new SerializedObject(component);
        }
        catch
        {
            return;
        }

        SerializedProperty property = serializedObject.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
            {
                continue;
            }

            if (targetIds != null && !targetIds.Contains(property.objectReferenceValue.GetInstanceID()))
            {
                continue;
            }

            T asset = property.objectReferenceValue as T;
            if (asset == null)
            {
                continue;
            }

            onFound(asset, component.GetType().Name, property.displayName, BuildHierarchyPath(component.transform));
        }
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return "(Missing Transform)";
        }

        Stack<string> names = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private void SelectDisplayedMaterials()
    {
        Object[] displayedMaterials = materialResults
            .Where(IsVisibleResult)
            .Select(result => (Object)result.Material)
            .ToArray();

        Selection.objects = displayedMaterials;
        string usageLabel = resultDisplayMode == ResultDisplayMode.Unused ? "미사용" : "사용 중";
        statusMessage = displayedMaterials.Length > 0
            ? $"표시된 {usageLabel} 머테리얼 {displayedMaterials.Length}개를 모두 선택했습니다."
            : $"{usageLabel} 머테리얼이 없습니다.";
        statusType = displayedMaterials.Length > 0 ? MessageType.Info : MessageType.Warning;
    }

    private void SelectDisplayedMeshes()
    {
        Object[] displayedMeshes = meshResults
            .Where(IsVisibleResult)
            .Select(result => (Object)result.Mesh)
            .ToArray();

        Selection.objects = displayedMeshes;
        string usageLabel = resultDisplayMode == ResultDisplayMode.Unused ? "미사용" : "사용 중";
        statusMessage = displayedMeshes.Length > 0
            ? $"표시된 {usageLabel} 메쉬 {displayedMeshes.Length}개를 모두 선택했습니다."
            : $"{usageLabel} 메쉬가 없습니다.";
        statusType = displayedMeshes.Length > 0 ? MessageType.Info : MessageType.Warning;
    }

    private void SelectDisplayedTextures()
    {
        bool showUsed = resultDisplayMode == ResultDisplayMode.Used;
        Object[] displayedTextures = textureResults
            .Where(result => result.IsUsed == showUsed)
            .Select(result => (Object)result.Texture)
            .ToArray();

        Selection.objects = displayedTextures;
        string usageLabel = resultDisplayMode == ResultDisplayMode.Unused ? "미사용" : "사용 중";
        statusMessage = displayedTextures.Length > 0
            ? $"표시된 {usageLabel} 텍스처 {displayedTextures.Length}개를 모두 선택했습니다."
            : $"{usageLabel} 텍스처가 없습니다.";
        statusType = displayedTextures.Length > 0 ? MessageType.Info : MessageType.Warning;
    }

    private void ClearSelection()
    {
        Selection.objects = new Object[0];
        if (currentTab == ToolTab.Materials)
        {
            materialResults.Clear();
        }
        else if (currentTab == ToolTab.Meshes)
        {
            meshResults.Clear();
        }
        else
        {
            textureResults.Clear();
        }

        scrollPosition = Vector2.zero;
        expandedUsageResults.Clear();
        statusMessage = "선택과 결과 리스트를 초기화했습니다.";
        statusType = MessageType.Info;
    }

    private void SelectPrefabFolder()
    {
        string startPath = NormalizeAssetFolderPath(prefabFolderPath);
        if (string.IsNullOrEmpty(startPath) || !AssetDatabase.IsValidFolder(startPath))
        {
            startPath = "Assets";
        }

        string absoluteStartPath = Path.GetFullPath(startPath);
        string selectedPath = EditorUtility.OpenFolderPanel("Prefab Folder", absoluteStartPath, string.Empty);
        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        selectedPath = selectedPath.Replace("\\", "/");
        if (!selectedPath.StartsWith(projectRoot))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "프로젝트 내부의 Assets 폴더 아래 경로를 선택하세요.", "OK");
            return;
        }

        prefabFolderPath = selectedPath.Substring(projectRoot.Length + 1);
        NormalizeAndSavePrefabFolderPath();
    }

    private void SelectMaterialFolder()
    {
        string startPath = NormalizeAssetFolderPath(materialFolderPath);
        if (string.IsNullOrEmpty(startPath) || !AssetDatabase.IsValidFolder(startPath))
        {
            startPath = "Assets";
        }

        string absoluteStartPath = Path.GetFullPath(startPath);
        string selectedPath = EditorUtility.OpenFolderPanel("Material Folder", absoluteStartPath, string.Empty);
        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        selectedPath = selectedPath.Replace("\\", "/");
        if (!selectedPath.StartsWith(projectRoot))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "프로젝트 내부의 Assets 폴더 아래 경로를 선택하세요.", "OK");
            return;
        }

        materialFolderPath = selectedPath.Substring(projectRoot.Length + 1);
        NormalizeAndSaveMaterialFolderPath();
    }

    private void NormalizeAndSavePrefabFolderPath()
    {
        prefabFolderPath = NormalizeAssetFolderPath(prefabFolderPath);
        SaveSettings();
        statusMessage = $"프리팹 검사 경로를 저장했습니다: {prefabFolderPath}";
        statusType = MessageType.Info;
    }

    private void NormalizeAndSaveMaterialFolderPath()
    {
        materialFolderPath = NormalizeAssetFolderPath(materialFolderPath);
        SaveSettings();
        textureResults.Clear();
        scrollPosition = Vector2.zero;
        statusMessage = $"머티리얼 검사 경로를 저장했습니다: {materialFolderPath}";
        statusType = MessageType.Info;
    }

    private static string NormalizeAssetFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Assets";
        }

        path = path.Trim().Replace("\\", "/");
        if (path.EndsWith("/"))
        {
            path = path.Substring(0, path.Length - 1);
        }

        if (!path.StartsWith("Assets"))
        {
            path = "Assets";
        }

        return path;
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        try
        {
            ResourceCleanerSettings settings = JsonUtility.FromJson<ResourceCleanerSettings>(File.ReadAllText(SettingsPath));
            if (settings == null)
            {
                return;
            }

            prefabFolderPath = NormalizeAssetFolderPath(settings.prefabFolderPath);
            materialFolderPath = NormalizeAssetFolderPath(settings.materialFolderPath);
            scanScope = settings.scanScope;
        }
        catch
        {
            prefabFolderPath = "Assets";
            materialFolderPath = "Assets";
            scanScope = ScanScope.SceneHierarchy;
        }
    }

    private void SaveSettings()
    {
        ResourceCleanerSettings settings = new ResourceCleanerSettings
        {
            prefabFolderPath = NormalizeAssetFolderPath(prefabFolderPath),
            materialFolderPath = NormalizeAssetFolderPath(materialFolderPath),
            scanScope = scanScope
        };

        string settingsDirectory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(settingsDirectory) && !Directory.Exists(settingsDirectory))
        {
            Directory.CreateDirectory(settingsDirectory);
        }

        File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
        AssetDatabase.ImportAsset(SettingsPath);
    }

    private static void ClearTextures(List<Material> materials)
    {
        foreach (Material material in materials)
        {
            if (material == null || material.shader == null)
            {
                continue;
            }

            Undo.RecordObject(material, "Clear Material Textures");
            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }

                material.SetTexture(ShaderUtil.GetPropertyName(material.shader, i), null);
            }

            EditorUtility.SetDirty(material);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Resource Cleaner] Cleared textures from {materials.Count} material(s).");
    }

    private static void ResetToShaderDefaults(List<Material> materials)
    {
        foreach (Material material in materials)
        {
            if (material == null || material.shader == null)
            {
                continue;
            }

            Undo.RecordObject(material, "Reset Material To Shader Defaults");
            string materialName = material.name;

            Material defaultMaterial = new Material(material.shader);
            material.CopyPropertiesFromMaterial(defaultMaterial);
            DestroyImmediate(defaultMaterial);

            material.name = materialName;
            EditorUtility.SetDirty(material);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Resource Cleaner] Reset {materials.Count} material(s) to shader defaults.");
    }

    private static List<Material> GetSelectedMaterials()
    {
        return Selection.objects
            .Select(obj => obj as Material)
            .Where(material => material != null && AssetDatabase.Contains(material))
            .ToList();
    }

    private static List<Mesh> GetSelectedMeshes()
    {
        List<Mesh> meshes = new List<Mesh>();
        foreach (Object selectedObject in Selection.objects)
        {
            Mesh selectedMesh = selectedObject as Mesh;
            if (selectedMesh != null && AssetDatabase.Contains(selectedMesh))
            {
                meshes.Add(selectedMesh);
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                continue;
            }

            meshes.AddRange(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>());
        }

        return meshes.Distinct().ToList();
    }

    private static List<Texture> GetSelectedTextures()
    {
        List<Texture> textures = new List<Texture>();
        foreach (Object selectedObject in Selection.objects)
        {
            Texture selectedTexture = selectedObject as Texture;
            if (selectedTexture != null && AssetDatabase.Contains(selectedTexture))
            {
                textures.Add(selectedTexture);
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                continue;
            }

            textures.AddRange(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Texture>());
        }

        return textures.Distinct().ToList();
    }

    private abstract class UsageResult<T> where T : Object
    {
        private readonly HashSet<string> referenceSet = new HashSet<string>();
        private readonly Dictionary<string, PrefabUsageInfo> prefabUsageMap = new Dictionary<string, PrefabUsageInfo>();

        protected UsageResult(T asset)
        {
            Asset = asset;
        }

        protected T Asset { get; }
        public List<string> ReferencePaths { get; } = new List<string>();
        public List<PrefabUsageInfo> PrefabUsages { get; } = new List<PrefabUsageInfo>();
        public bool IsUsed => ReferencePaths.Count > 0;

        public void AddReference(
            string hierarchyPath,
            string componentName,
            string propertyName,
            GameObject prefab = null,
            string prefabPath = null)
        {
            string referencePath = $"{hierarchyPath} ({componentName}.{propertyName})";
            string referenceKey = string.IsNullOrEmpty(prefabPath) ? referencePath : $"{prefabPath}|{referencePath}";
            if (referenceSet.Add(referenceKey))
            {
                ReferencePaths.Add(referencePath);
            }

            if (prefab == null || string.IsNullOrEmpty(prefabPath))
            {
                return;
            }

            if (!prefabUsageMap.TryGetValue(prefabPath, out PrefabUsageInfo prefabUsage))
            {
                prefabUsage = new PrefabUsageInfo(prefab, prefabPath);
                prefabUsageMap.Add(prefabPath, prefabUsage);
                PrefabUsages.Add(prefabUsage);
            }

            prefabUsage.AddReference(referencePath);
        }
    }

    private readonly struct TargetComponent
    {
        public TargetComponent(Component component, GameObject prefab, string prefabPath)
        {
            Component = component;
            Prefab = prefab;
            PrefabPath = prefabPath;
        }

        public Component Component { get; }
        public GameObject Prefab { get; }
        public string PrefabPath { get; }
    }

    private sealed class MaterialUsageResult : UsageResult<Material>
    {
        public MaterialUsageResult(Material material)
            : base(material)
        {
            Material = material;
        }

        public Material Material { get; }
    }

    private sealed class MeshUsageResult : UsageResult<Mesh>
    {
        public MeshUsageResult(Mesh mesh)
            : base(mesh)
        {
            Mesh = mesh;
        }

        public Mesh Mesh { get; }
    }

    private sealed class TextureUsageResult
    {
        private readonly HashSet<int> materialIds = new HashSet<int>();

        public TextureUsageResult(Texture texture)
        {
            Texture = texture;
        }

        public Texture Texture { get; }
        public List<Material> Materials { get; } = new List<Material>();
        public bool IsUsed => Materials.Count > 0;

        public void AddMaterial(Material material)
        {
            if (material != null && materialIds.Add(material.GetInstanceID()))
            {
                Materials.Add(material);
            }
        }
    }

    private sealed class PrefabUsageInfo
    {
        private readonly HashSet<string> referenceSet = new HashSet<string>();

        public PrefabUsageInfo(GameObject prefab, string prefabPath)
        {
            Prefab = prefab;
            PrefabPath = prefabPath;
        }

        public GameObject Prefab { get; }
        public string PrefabPath { get; }
        public List<string> ReferencePaths { get; } = new List<string>();

        public void AddReference(string referencePath)
        {
            if (referenceSet.Add(referencePath))
            {
                ReferencePaths.Add(referencePath);
            }
        }
    }

    [System.Serializable]
    private sealed class ResourceCleanerSettings
    {
        public string prefabFolderPath = "Assets";
        public string materialFolderPath = "Assets";
        public ScanScope scanScope = ScanScope.SceneHierarchy;
    }
}

#endif
