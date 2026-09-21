using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoofTopStudio.SimpleMeshEditor
{
    public sealed class SimpleMeshEditor : EditorWindow
    {
        private const string WindowTitle = "Simple Mesh Editor";
        private const string EditedMeshFolder = "Assets/VFXMeshEdited";
        private const string ExportedFbxFolder = "Assets/VFXMeshExported";
        private const string EditorAssetFolder = "Assets/RoofTopStudio/Tools/SimpleMeshEditor/Editor";
        private const string ExportMaterialPath = EditorAssetFolder + "/SimpleMeshEditor_Default.mat";
        private const string LegacyEditedMeshFolder = "Assets/VertexColorEdited";
        private const float PreviewRotationSensitivity = 0.6f;
        private const float PreviewRotationSnapAngle = 90f;
        private static readonly string[] EditorTabs = { "Vertex Color", "UV Editor" };
        private static readonly Vector2 DefaultPreviewRotation = new Vector2(20f, -35f);

        private readonly HashSet<int> selectedVertices = new HashSet<int>();

        private GameObject targetObject;
        private Mesh targetMesh;
        private Mesh standaloneMesh;
        private MeshFilter meshFilter;
        private SkinnedMeshRenderer skinnedMeshRenderer;
        private Material previewMaterial;
        private Material vertexColorPreviewMaterial;
        private Material wireMaterial;
        private PreviewRenderUtility previewUtility;
        private Vector2 scroll;
        private Rect previewRect;
        private Matrix4x4 previewModelMatrix;
        private Matrix4x4 previewViewProjectionMatrix;
        private Vector2 previewRotation = DefaultPreviewRotation;
        private Vector2 previewRotationRaw = DefaultPreviewRotation;
        private Vector2 previewPan = Vector2.zero;
        private Vector2 leftMouseDownPosition;
        private Rect selectionRect;
        private float previewDistance = 3.2f;
        private string lastCreatedAssetPath = string.Empty;
        private int pendingClickedVertex = -1;
        private bool leftMouseDragging;
        private bool boxSelecting;
        private bool boxToggleSelection;
        private bool previewRotationSnappedLastFrame;
        private int activeTab;

        private Color editColor = Color.white;
        private bool editR = true;
        private bool editG = true;
        private bool editB = true;
        private bool editA = true;
        private bool drawLitMesh = true;
        private bool drawOnlySelected = false;
        private bool mirrorSamePositionVertices = true;
        private float handleSize = 5f;
        private float samePositionTolerance = 0.0001f;
        private int uvChannelIndex;

        [MenuItem("Tools/RoofTop Studio/Simple Mesh Editor")]
        public static void ShowWindow()
        {
            GetWindow<SimpleMeshEditor>(WindowTitle);
        }

        private void OnEnable()
        {
            CreatePreviewUtility();
        }

        private void OnDisable()
        {
            if (previewMaterial != null)
            {
                DestroyImmediate(previewMaterial);
            }

            if (vertexColorPreviewMaterial != null)
            {
                DestroyImmediate(vertexColorPreviewMaterial);
            }

            if (wireMaterial != null)
            {
                DestroyImmediate(wireMaterial);
            }

            if (previewUtility != null)
            {
                previewUtility.Cleanup();
                previewUtility = null;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            GUILayout.Label("대상", EditorStyles.boldLabel);

            DrawDropArea();

            if (targetMesh == null)
            {
                EditorGUILayout.HelpBox("MeshFilter/SkinnedMeshRenderer가 있는 오브젝트 또는 Mesh asset을 드래그 앤 드롭하세요.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawControlPanel();
                DrawPreviewPanel();
            }
        }

        private void DrawControlPanel()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Width(360f));

            EditorGUILayout.LabelField("메쉬", targetMesh.name);
            EditorGUILayout.LabelField("버텍스 수", targetMesh.vertexCount.ToString());
            EditorGUILayout.LabelField("선택 수", selectedVertices.Count.ToString());

            DrawImportWarning();
            bool editableMesh = IsEditableMeshCopy();

            if (editableMesh)
            {
                EditorGUILayout.HelpBox("현재 Mesh는 편집용 복사본입니다. 아래 메뉴에서 버텍스 선택과 채널 편집을 진행할 수 있습니다.", MessageType.Info);
            }
            else
            {
                if (GUILayout.Button("편집 시작: 복사본 생성", GUILayout.Height(34f)))
                {
                    CreateEditableMeshCopy();
                    editableMesh = IsEditableMeshCopy();
                }

                EditorGUILayout.HelpBox("'편집 시작: 복사본 생성'은 버튼을 누르는 즉시 현재 Mesh를 Assets/VFXMeshEdited 폴더 아래의 별도 .asset으로 복사하고, 선택 오브젝트가 그 복사본을 사용하도록 바꿉니다. FBX 원본을 임시로 들고 있다가 나중에 저장하는 방식이 아니라, 편집 전에 안전한 복사본을 먼저 만드는 흐름입니다.", MessageType.Warning);

                if (!editableMesh)
                {
                    EditorGUILayout.HelpBox("복사본을 생성해야 Vertex Color/UV 편집 메뉴가 열립니다.", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }
            }

            activeTab = GUILayout.Toolbar(activeTab, EditorTabs);
            EditorGUILayout.Space(8f);

            if (activeTab == 1)
            {
                DrawUvEditorControls();
                DrawFbxExportControls();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (GUILayout.Button("에디터 초기화", GUILayout.Height(28f)))
            {
                ResetEditorState();
            }

            EditorGUILayout.HelpBox("'에디터 초기화'는 현재 편집 대상을 비우고 최초 기동 상태로 되돌립니다. Mesh asset 자체의 색상이나 UV 데이터는 변경하지 않습니다.", MessageType.Info);

            EditorGUILayout.Space(8f);
            GUILayout.Label("프리뷰 표시", EditorStyles.boldLabel);
            drawLitMesh = EditorGUILayout.Toggle("셰이딩 스타일 전환", drawLitMesh);
            handleSize = EditorGUILayout.Slider("버텍스 점 크기", handleSize, 2f, 12f);
            mirrorSamePositionVertices = EditorGUILayout.Toggle("같은 위치 버텍스 함께 선택", mirrorSamePositionVertices);
            samePositionTolerance = EditorGUILayout.Slider("같은 위치 허용 오차", samePositionTolerance, 0.00001f, 0.01f);
            drawOnlySelected = EditorGUILayout.Toggle("선택된 버텍스만 표시", drawOnlySelected);

            EditorGUILayout.HelpBox("'같은 위치 버텍스 함께 선택'은 FBX에서 UV seam, hard normal, material split 때문에 같은 공간 위치에 중복 생성된 버텍스들을 한 번에 선택합니다. 겉보기로는 하나의 점인데 실제 Mesh 데이터는 여러 버텍스인 경우에 색이 끊기지 않게 해줍니다.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("전체 선택"))
                {
                    SelectAllVertices();
                }

                if (GUILayout.Button("선택 해제"))
                {
                    selectedVertices.Clear();
                    Repaint();
                }
            }

            EditorGUILayout.HelpBox("오른쪽 프리뷰 창에서 버텍스를 클릭해 선택합니다. Shift 클릭은 추가 선택, Ctrl 클릭은 선택 토글, 일반 클릭은 기존 선택을 교체합니다. Shift+좌클릭 드래그는 박스 다중 선택, Ctrl+Shift+좌클릭 드래그는 박스 토글 선택입니다. 좌/우클릭 드래그는 회전, 회전 중 Shift는 90도 단위 스냅, Middle 드래그는 이동, 휠은 확대/축소입니다.", MessageType.None);

            EditorGUILayout.Space(8f);
            GUILayout.Label("채널 편집", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                editR = GUILayout.Toggle(editR, "R", "Button");
                editG = GUILayout.Toggle(editG, "G", "Button");
                editB = GUILayout.Toggle(editB, "B", "Button");
                editA = GUILayout.Toggle(editA, "A", "Button");
            }

            editColor = EditorGUILayout.ColorField(new GUIContent("적용 값"), editColor, true, editA, false);
            EditorGUILayout.HelpBox("컬러 피커의 알파 값은 A 채널 버튼이 켜져 있을 때만 적용됩니다. R/G/B만 편집할 때는 알파 값이 무시되므로 의미가 없습니다.", MessageType.Info);
            EditorGUILayout.HelpBox("'셰이딩 스타일 전환'을 켜면 기본 Lit 셰이딩으로 보고, 끄면 선택한 버텍스 컬러 채널값을 Unlit 표면으로 봅니다. A 채널만 켜면 A=0은 검정, A=1은 흰색으로 보입니다.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = selectedVertices.Count > 0;
                if (GUILayout.Button("선택 영역에 적용", GUILayout.Height(30f)))
                {
                    ApplyColorToSelection();
                }

                GUI.enabled = true;
                if (GUILayout.Button("전체에 적용", GUILayout.Height(30f)))
                {
                    SelectAllVertices();
                    ApplyColorToSelection();
                }
            }

            EditorGUILayout.Space(8f);
            DrawSelectedColorPreview();
            DrawFbxExportControls();

            EditorGUILayout.EndScrollView();
        }

        private void DrawFbxExportControls()
        {
            EditorGUILayout.Space(12f);
            GUILayout.Label("FBX Export", EditorStyles.boldLabel);
            if (GUILayout.Button("FBX로 내보내기", GUILayout.Height(32f)))
            {
                ExportCurrentMeshToFbx();
            }

            EditorGUILayout.HelpBox("현재 편집 Mesh를 임시 GameObject로 만든 뒤 FBX Exporter로 내보냅니다. Vertex Color와 UV 채널은 FBX Exporter 패키지의 기본 Mesh export 경로를 사용합니다.", MessageType.Info);
        }

        private void DrawDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "GameObject 또는 Mesh Asset 드래그 앤 드롭", EditorStyles.helpBox);

            Event current = Event.current;
            if (!dropRect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            {
                return;
            }

            Object droppedObject = GetFirstSupportedDraggedObject();
            DragAndDrop.visualMode = droppedObject != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (current.type == EventType.DragPerform && droppedObject != null)
            {
                DragAndDrop.AcceptDrag();
                SetDraggedTarget(droppedObject);
            }

            current.Use();
        }

        private Object GetFirstSupportedDraggedObject()
        {
            foreach (Object draggedObject in DragAndDrop.objectReferences)
            {
                if (draggedObject is GameObject gameObject && GetMeshFromGameObject(gameObject) != null)
                {
                    return draggedObject;
                }

                if (draggedObject is Mesh)
                {
                    return draggedObject;
                }
            }

            return null;
        }

        private Mesh GetMeshFromGameObject(GameObject gameObject)
        {
            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                return filter.sharedMesh;
            }

            SkinnedMeshRenderer renderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                return renderer.sharedMesh;
            }

            return null;
        }

        private void SetDraggedTarget(Object draggedObject)
        {
            if (draggedObject is GameObject gameObject)
            {
                RefreshTarget(gameObject);
                Selection.activeGameObject = gameObject;
                return;
            }

            if (draggedObject is Mesh mesh)
            {
                RefreshTarget(mesh);
            }
        }

        private void DrawPreviewPanel()
        {
            previewRect = GUILayoutUtility.GetRect(10f, 10000f, 420f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);

            if (activeTab == 1)
            {
                DrawUvPreview(previewRect);
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                RenderMeshPreview(previewRect);
            }

            HandlePreviewInput(previewRect);
            DrawPreviewOverlay(previewRect);
            DrawPreviewRotationControls(previewRect);
        }

        private void DrawPreviewRotationControls(Rect rect)
        {
            Rect toolbarRect = GetPreviewRotationToolbarRect(rect);
            GUI.Box(toolbarRect, GUIContent.none, EditorStyles.toolbar);

            float x = toolbarRect.x + 4f;
            float y = toolbarRect.y + 2f;
            float height = toolbarRect.height - 4f;

            if (DrawPreviewToolbarButton(ref x, y, height, 42f, "Top"))
            {
                SetPreviewRotation(90f, 0f);
            }

            if (DrawPreviewToolbarButton(ref x, y, height, 58f, "Bottom"))
            {
                SetPreviewRotation(-90f, 0f);
            }

            if (DrawPreviewToolbarButton(ref x, y, height, 42f, "Left"))
            {
                SetPreviewRotation(0f, 90f);
            }

            if (DrawPreviewToolbarButton(ref x, y, height, 48f, "Right"))
            {
                SetPreviewRotation(0f, -90f);
            }

            if (DrawPreviewToolbarButton(ref x, y, height, 48f, "Front"))
            {
                SetPreviewRotation(0f, 0f);
            }

            if (DrawPreviewToolbarButton(ref x, y, height, 46f, "Back"))
            {
                SetPreviewRotation(0f, 180f);
            }

            float resetWidth = 78f;
            float resetX = toolbarRect.xMax - resetWidth - 4f;
            if (resetX > x + 4f && GUI.Button(new Rect(resetX, y, resetWidth, height), "각도 초기화", EditorStyles.toolbarButton))
            {
                SetPreviewRotation(DefaultPreviewRotation.x, DefaultPreviewRotation.y);
            }
        }

        private bool DrawPreviewToolbarButton(ref float x, float y, float height, float width, string label)
        {
            bool clicked = GUI.Button(new Rect(x, y, width, height), label, EditorStyles.toolbarButton);
            x += width + 2f;
            return clicked;
        }

        private Rect GetPreviewRotationToolbarRect(Rect rect)
        {
            return new Rect(rect.x + 6f, rect.y + 6f, Mathf.Max(rect.width - 12f, 10f), 22f);
        }

        private void DrawUvEditorControls()
        {
            GUILayout.Label("UV Editor", EditorStyles.boldLabel);
            uvChannelIndex = GUILayout.Toolbar(uvChannelIndex, new[] { "UV1", "UV2" });

            bool hasSelectedUv = HasUvChannel(uvChannelIndex);
            if (!hasSelectedUv)
            {
                EditorGUILayout.HelpBox((uvChannelIndex + 1) + "번 UV 채널이 없습니다. UV2는 'UV1 -> UV2 복사'로 생성할 수 있습니다.", MessageType.Warning);
            }

            EditorGUILayout.Space(6f);
            GUI.enabled = HasUvChannel(0);
            if (GUILayout.Button("UV1 -> UV2 복사", GUILayout.Height(30f)))
            {
                CopyUv1ToUv2();
            }

            GUI.enabled = hasSelectedUv;
            EditorGUILayout.Space(8f);
            GUILayout.Label("선택 UV 채널 변환", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("90도 시계"))
                {
                    TransformUvChannel(uvChannelIndex, UvTransform.RotateClockwise);
                }

                if (GUILayout.Button("90도 반시계"))
                {
                    TransformUvChannel(uvChannelIndex, UvTransform.RotateCounterClockwise);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("좌우 반전"))
                {
                    TransformUvChannel(uvChannelIndex, UvTransform.FlipHorizontal);
                }

                if (GUILayout.Button("상하 반전"))
                {
                    TransformUvChannel(uvChannelIndex, UvTransform.FlipVertical);
                }
            }

            GUI.enabled = true;
            EditorGUILayout.HelpBox("UV 좌표 직접 이동은 하지 않고, 선택한 UV 채널 전체를 0~1 기준으로 회전/반전합니다. UV2가 없으면 UV1을 복사해 UV2 채널을 생성할 수 있습니다.", MessageType.Info);
        }

        private void DrawUvPreview(Rect rect)
        {
            if (Event.current.type != EventType.Repaint || targetMesh == null)
            {
                return;
            }

            Handles.BeginGUI();
            Handles.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            Handles.DrawSolidRectangleWithOutline(rect, new Color(0.18f, 0.18f, 0.18f, 1f), new Color(0.45f, 0.45f, 0.45f, 1f));

            Rect uvRect = GetUvPreviewRect(rect);
            DrawUvGrid(uvRect);

            List<Vector2> uvs = GetUvList(uvChannelIndex);
            if (uvs.Count == targetMesh.vertexCount)
            {
                DrawUvTriangles(uvRect, uvs);
            }
            else
            {
                GUI.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 24f), "선택한 UV 채널이 없습니다.", EditorStyles.whiteLabel);
            }

            Handles.EndGUI();
        }

        private void RenderMeshPreview(Rect rect)
        {
            CreatePreviewUtility();
            if (previewUtility == null || targetMesh == null)
            {
                return;
            }

            Bounds bounds = targetMesh.bounds;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);

            Quaternion rotation = Quaternion.Euler(previewRotation.x, previewRotation.y, 0f);
            Vector3 panOffset = new Vector3(previewPan.x * radius, previewPan.y * radius, 0f);
            previewModelMatrix = Matrix4x4.Translate(panOffset) * Matrix4x4.Rotate(rotation) * Matrix4x4.Translate(-bounds.center);

            Camera camera = previewUtility.camera;
            camera.transform.position = new Vector3(0f, 0f, -previewDistance * radius);
            camera.transform.rotation = Quaternion.identity;
            camera.aspect = Mathf.Max(rect.width / Mathf.Max(rect.height, 1f), 0.01f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = previewDistance * radius + radius * 4f;
            camera.fieldOfView = 35f;
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            previewViewProjectionMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;

            previewUtility.BeginPreview(rect, GUIStyle.none);

            if (drawLitMesh)
            {
                Material material = GetPreviewMaterial();
                if (material != null)
                {
                    for (int subMeshIndex = 0; subMeshIndex < targetMesh.subMeshCount; subMeshIndex++)
                    {
                        previewUtility.DrawMesh(targetMesh, previewModelMatrix, material, subMeshIndex);
                    }
                }
            }
            else
            {
                Material material = GetVertexColorPreviewMaterial();
                if (material != null)
                {
                    UpdateVertexColorPreviewMaterial(material);
                    for (int subMeshIndex = 0; subMeshIndex < targetMesh.subMeshCount; subMeshIndex++)
                    {
                        previewUtility.DrawMesh(targetMesh, previewModelMatrix, material, subMeshIndex);
                    }
                }
            }

            previewUtility.camera.Render();
            DrawDepthTestedWireframe(!drawLitMesh);

            Texture texture = previewUtility.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        private void DrawPreviewOverlay(Rect rect)
        {
            if (targetMesh == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Vector3[] vertices = targetMesh.vertices;
            Vector3[] normals = targetMesh.normals;

            Handles.BeginGUI();
            for (int i = 0; i < vertices.Length; i++)
            {
                bool isSelected = selectedVertices.Contains(i);
                if (drawOnlySelected && !isSelected)
                {
                    continue;
                }

                if (!IsFrontVertex(vertices, normals, i))
                {
                    continue;
                }

                if (!TryProjectVertex(rect, vertices[i], out Vector2 guiPosition))
                {
                    continue;
                }

                Handles.color = isSelected ? Color.yellow : GetVertexDisplayColor(i);
                Handles.DrawSolidDisc(guiPosition, Vector3.forward, handleSize);
                Handles.color = Color.black;
                Handles.DrawWireDisc(guiPosition, Vector3.forward, handleSize);
            }
            Handles.EndGUI();
            DrawSelectionBox();
        }

        private void HandlePreviewInput(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition))
            {
                return;
            }

            if (GetPreviewRotationToolbarRect(rect).Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && IsEditableMeshCopy())
            {
                pendingClickedVertex = FindNearestVertex(rect, current.mousePosition, handleSize + 5f);
                leftMouseDownPosition = current.mousePosition;
                leftMouseDragging = false;
                boxSelecting = current.shift;
                boxToggleSelection = current.control || current.command;
                selectionRect = Rect.zero;
                current.Use();
            }

            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                if ((current.mousePosition - leftMouseDownPosition).sqrMagnitude > 9f)
                {
                    leftMouseDragging = true;
                }

                if (leftMouseDragging && boxSelecting)
                {
                    selectionRect = MakeRect(leftMouseDownPosition, current.mousePosition);
                    Repaint();
                }
                else if (leftMouseDragging)
                {
                    RotatePreview(current.delta, current.shift);
                    Repaint();
                }

                current.Use();
            }

            if (current.type == EventType.MouseUp && current.button == 0 && IsEditableMeshCopy())
            {
                if (!leftMouseDragging)
                {
                    if (pendingClickedVertex >= 0)
                    {
                        PickVertex(pendingClickedVertex, current);
                    }
                    else if (!current.shift && !current.control && !current.command)
                    {
                        selectedVertices.Clear();
                        Repaint();
                    }
                }
                else if (boxSelecting)
                {
                    SelectVerticesInRect(rect, selectionRect, boxToggleSelection);
                }

                pendingClickedVertex = -1;
                leftMouseDragging = false;
                boxSelecting = false;
                boxToggleSelection = false;
                selectionRect = Rect.zero;
                current.Use();
            }

            if (current.type == EventType.MouseDrag && current.button == 1)
            {
                RotatePreview(current.delta, current.shift);
                current.Use();
                Repaint();
            }

            if (current.type == EventType.MouseDrag && current.button == 2)
            {
                previewPan.x += current.delta.x * 0.004f * previewDistance;
                previewPan.y -= current.delta.y * 0.004f * previewDistance;
                current.Use();
                Repaint();
            }

            if (current.type == EventType.ScrollWheel)
            {
                previewDistance = Mathf.Clamp(previewDistance + current.delta.y * 0.08f, 1.2f, 8f);
                current.Use();
                Repaint();
            }
        }

        private void SetPreviewRotation(float x, float y)
        {
            previewRotation = new Vector2(Mathf.Clamp(x, -90f, 90f), NormalizePreviewAngle(y));
            previewRotationRaw = previewRotation;
            previewRotationSnappedLastFrame = false;
            Repaint();
        }

        private void RotatePreview(Vector2 delta, bool snapToRightAngles)
        {
            if (previewRotationSnappedLastFrame != snapToRightAngles)
            {
                previewRotationRaw = previewRotation;
            }

            previewRotationRaw.y += delta.x * PreviewRotationSensitivity;
            previewRotationRaw.x += delta.y * PreviewRotationSensitivity;
            previewRotationRaw.x = Mathf.Clamp(previewRotationRaw.x, -89f, 89f);
            previewRotationRaw.y = NormalizePreviewAngle(previewRotationRaw.y);

            if (snapToRightAngles)
            {
                previewRotation.x = Mathf.Round(previewRotationRaw.x / PreviewRotationSnapAngle) * PreviewRotationSnapAngle;
                previewRotation.y = Mathf.Round(previewRotationRaw.y / PreviewRotationSnapAngle) * PreviewRotationSnapAngle;
                previewRotation.x = Mathf.Clamp(previewRotation.x, -90f, 90f);
                previewRotation.y = NormalizePreviewAngle(previewRotation.y);
                previewRotationSnappedLastFrame = true;
                return;
            }

            previewRotation = previewRotationRaw;
            previewRotationSnappedLastFrame = false;
        }

        private float NormalizePreviewAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle <= -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        private Rect MakeRect(Vector2 start, Vector2 end)
        {
            return new Rect(
                Mathf.Min(start.x, end.x),
                Mathf.Min(start.y, end.y),
                Mathf.Abs(end.x - start.x),
                Mathf.Abs(end.y - start.y));
        }

        private void DrawSelectionBox()
        {
            if (!boxSelecting || selectionRect.width <= 0f || selectionRect.height <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(selectionRect, new Color(1f, 0.85f, 0.05f, 0.12f), new Color(1f, 0.85f, 0.05f, 0.95f));
            Handles.EndGUI();
        }

        private void SelectVerticesInRect(Rect previewArea, Rect rect, bool toggle)
        {
            if (targetMesh == null || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Vector3[] vertices = targetMesh.vertices;
            Vector3[] normals = targetMesh.normals;

            if (!toggle)
            {
                selectedVertices.Clear();
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFrontVertex(vertices, normals, i))
                {
                    continue;
                }

                if (!TryProjectVertex(previewArea, vertices[i], out Vector2 guiPosition))
                {
                    continue;
                }

                if (!rect.Contains(guiPosition))
                {
                    continue;
                }

                if (toggle && selectedVertices.Contains(i))
                {
                    RemoveVertexFromSelection(i);
                }
                else
                {
                    AddVertexToSelection(i);
                }
            }

            Repaint();
        }

        private void DrawDepthTestedWireframe(bool backFacingOnly)
        {
            Material material = GetWireMaterial();
            if (material == null || targetMesh == null)
            {
                return;
            }

            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(previewUtility.camera.projectionMatrix);
            GL.modelview = previewUtility.camera.worldToCameraMatrix * previewModelMatrix;
            GL.Begin(GL.LINES);
            GL.Color(new Color(0f, 0f, 0f, 0.65f));

            Vector3[] vertices = targetMesh.vertices;
            for (int subMeshIndex = 0; subMeshIndex < targetMesh.subMeshCount; subMeshIndex++)
            {
                MeshTopology topology = targetMesh.GetTopology(subMeshIndex);
                int[] indices = targetMesh.GetIndices(subMeshIndex);

                if (topology == MeshTopology.Triangles)
                {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        if (backFacingOnly && !IsFrontTriangle(vertices, indices[i], indices[i + 1], indices[i + 2]))
                        {
                            continue;
                        }

                        DrawGlEdge(vertices, indices[i], indices[i + 1]);
                        DrawGlEdge(vertices, indices[i + 1], indices[i + 2]);
                        DrawGlEdge(vertices, indices[i + 2], indices[i]);
                    }
                }
                else if (topology == MeshTopology.Quads)
                {
                    for (int i = 0; i + 3 < indices.Length; i += 4)
                    {
                        if (backFacingOnly && !IsFrontQuad(vertices, indices[i], indices[i + 1], indices[i + 2], indices[i + 3]))
                        {
                            continue;
                        }

                        DrawGlEdge(vertices, indices[i], indices[i + 1]);
                        DrawGlEdge(vertices, indices[i + 1], indices[i + 2]);
                        DrawGlEdge(vertices, indices[i + 2], indices[i + 3]);
                        DrawGlEdge(vertices, indices[i + 3], indices[i]);
                    }
                }
                else if (topology == MeshTopology.Lines)
                {
                    for (int i = 0; i + 1 < indices.Length; i += 2)
                    {
                        DrawGlEdge(vertices, indices[i], indices[i + 1]);
                    }
                }
                else if (topology == MeshTopology.LineStrip)
                {
                    for (int i = 0; i + 1 < indices.Length; i++)
                    {
                        DrawGlEdge(vertices, indices[i], indices[i + 1]);
                    }
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private void DrawGlEdge(Vector3[] vertices, int a, int b)
        {
            if (a < 0 || b < 0 || a >= vertices.Length || b >= vertices.Length)
            {
                return;
            }

            GL.Vertex(vertices[a]);
            GL.Vertex(vertices[b]);
        }

        private bool IsFrontTriangle(Vector3[] vertices, int a, int b, int c)
        {
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
            {
                return false;
            }

            Vector3 pointA = previewModelMatrix.MultiplyPoint3x4(vertices[a]);
            Vector3 pointB = previewModelMatrix.MultiplyPoint3x4(vertices[b]);
            Vector3 pointC = previewModelMatrix.MultiplyPoint3x4(vertices[c]);
            Vector3 normal = Vector3.Cross(pointB - pointA, pointC - pointA).normalized;
            Vector3 center = (pointA + pointB + pointC) / 3f;
            Vector3 cameraToCenter = center - previewUtility.camera.transform.position;
            return Vector3.Dot(normal, cameraToCenter) < 0f;
        }

        private bool IsFrontQuad(Vector3[] vertices, int a, int b, int c, int d)
        {
            return IsFrontTriangle(vertices, a, b, c) || IsFrontTriangle(vertices, a, c, d);
        }

        private int FindNearestVertex(Rect rect, Vector2 mousePosition, float maxDistance)
        {
            Vector3[] vertices = targetMesh.vertices;
            int nearest = -1;
            float nearestDistance = maxDistance * maxDistance;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (drawOnlySelected && !selectedVertices.Contains(i))
                {
                    continue;
                }

                if (!IsFrontVertex(vertices, targetMesh.normals, i))
                {
                    continue;
                }

                if (!TryProjectVertex(rect, vertices[i], out Vector2 guiPosition))
                {
                    continue;
                }

                float distance = (guiPosition - mousePosition).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private bool TryProjectVertex(Rect rect, Vector3 localPosition, out Vector2 guiPosition)
        {
            Vector4 clip = previewViewProjectionMatrix * previewModelMatrix * new Vector4(localPosition.x, localPosition.y, localPosition.z, 1f);
            if (clip.w <= 0f)
            {
                guiPosition = Vector2.zero;
                return false;
            }

            Vector3 normalized = new Vector3(clip.x / clip.w, clip.y / clip.w, clip.z / clip.w);
            if (normalized.z < -1f || normalized.z > 1f)
            {
                guiPosition = Vector2.zero;
                return false;
            }

            guiPosition = new Vector2(
                rect.x + (normalized.x * 0.5f + 0.5f) * rect.width,
                rect.y + (1f - (normalized.y * 0.5f + 0.5f)) * rect.height);
            return rect.Contains(guiPosition);
        }

        private bool IsFrontVertex(Vector3[] vertices, Vector3[] normals, int vertexIndex)
        {
            if (normals == null || normals.Length != vertices.Length)
            {
                return true;
            }

            Vector3 point = previewModelMatrix.MultiplyPoint3x4(vertices[vertexIndex]);
            Vector3 normal = previewModelMatrix.MultiplyVector(normals[vertexIndex]).normalized;
            Vector3 cameraToPoint = point - previewUtility.camera.transform.position;
            return Vector3.Dot(normal, cameraToPoint) < 0f;
        }

        private Material GetPreviewMaterial()
        {
            if (previewMaterial != null)
            {
                return previewMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/Internal-Colored");
            }

            if (shader == null)
            {
                return null;
            }

            previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            previewMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
            previewMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
            previewMaterial.SetInt("_Cull", (int)CullMode.Off);
            previewMaterial.SetInt("_ZWrite", 1);
            previewMaterial.SetFloat("_Surface", 0f);
            previewMaterial.SetFloat("_Mode", 0f);
            previewMaterial.SetColor("_Color", new Color(0.72f, 0.76f, 0.8f, 1f));
            previewMaterial.SetColor("_BaseColor", new Color(0.72f, 0.76f, 0.8f, 1f));
            previewMaterial.renderQueue = (int)RenderQueue.Geometry;
            return previewMaterial;
        }

        private Material GetVertexColorPreviewMaterial()
        {
            if (vertexColorPreviewMaterial != null)
            {
                return vertexColorPreviewMaterial;
            }

            Shader shader = Shader.Find("Hidden/RoofTopStudio/SimpleMeshEditor/VertexColorPreview");
            if (shader == null)
            {
                return null;
            }

            vertexColorPreviewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            vertexColorPreviewMaterial.renderQueue = (int)RenderQueue.Geometry;
            return vertexColorPreviewMaterial;
        }

        private Material GetWireMaterial()
        {
            if (wireMaterial != null)
            {
                return wireMaterial;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return null;
            }

            wireMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            wireMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            wireMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            wireMaterial.SetInt("_Cull", (int)CullMode.Off);
            wireMaterial.SetInt("_ZWrite", 0);
            wireMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return wireMaterial;
        }

        private void UpdateVertexColorPreviewMaterial(Material material)
        {
            bool alphaOnly = editA && !editR && !editG && !editB;
            material.SetVector("_ChannelMask", new Vector4(editR ? 1f : 0f, editG ? 1f : 0f, editB ? 1f : 0f, 0f));
            material.SetFloat("_AlphaOnly", alphaOnly ? 1f : 0f);
        }

        private void CreatePreviewUtility()
        {
            if (previewUtility != null)
            {
                return;
            }

            previewUtility = new PreviewRenderUtility();
            previewUtility.cameraFieldOfView = 35f;
            previewUtility.lights[0].intensity = 1.1f;
            previewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            previewUtility.lights[1].intensity = 0.5f;
        }

        private void RefreshTarget(GameObject gameObject)
        {
            targetObject = gameObject;
            standaloneMesh = null;
            meshFilter = null;
            skinnedMeshRenderer = null;
            targetMesh = null;
            selectedVertices.Clear();
            previewPan = Vector2.zero;

            if (targetObject == null)
            {
                return;
            }

            meshFilter = targetObject.GetComponent<MeshFilter>();
            skinnedMeshRenderer = targetObject.GetComponent<SkinnedMeshRenderer>();

            if (meshFilter != null)
            {
                targetMesh = meshFilter.sharedMesh;
            }
            else if (skinnedMeshRenderer != null)
            {
                targetMesh = skinnedMeshRenderer.sharedMesh;
            }

            Repaint();
        }

        private void RefreshTarget(Mesh mesh)
        {
            targetObject = null;
            standaloneMesh = mesh;
            meshFilter = null;
            skinnedMeshRenderer = null;
            targetMesh = mesh;
            selectedVertices.Clear();
            previewPan = Vector2.zero;
            Repaint();
        }

        private void ResetEditorState()
        {
            targetObject = null;
            targetMesh = null;
            standaloneMesh = null;
            meshFilter = null;
            skinnedMeshRenderer = null;
            selectedVertices.Clear();
            previewRect = Rect.zero;
            previewModelMatrix = Matrix4x4.identity;
            previewViewProjectionMatrix = Matrix4x4.identity;
            previewRotation = DefaultPreviewRotation;
            previewRotationRaw = DefaultPreviewRotation;
            previewRotationSnappedLastFrame = false;
            previewPan = Vector2.zero;
            leftMouseDownPosition = Vector2.zero;
            selectionRect = Rect.zero;
            previewDistance = 3.2f;
            lastCreatedAssetPath = string.Empty;
            pendingClickedVertex = -1;
            leftMouseDragging = false;
            boxSelecting = false;
            boxToggleSelection = false;
            activeTab = 0;
            editColor = Color.white;
            editR = true;
            editG = true;
            editB = true;
            editA = true;
            drawLitMesh = true;
            drawOnlySelected = false;
            mirrorSamePositionVertices = true;
            handleSize = 5f;
            samePositionTolerance = 0.0001f;
            uvChannelIndex = 0;
            Repaint();
        }

        private bool IsEditableMeshCopy()
        {
            if (targetMesh == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(targetMesh);
            string normalizedPath = path.Replace('\\', '/');
            return !string.IsNullOrEmpty(path)
                && Path.GetExtension(path).ToLowerInvariant() == ".asset"
                && (normalizedPath.StartsWith(EditedMeshFolder + "/") || normalizedPath.StartsWith(LegacyEditedMeshFolder + "/"));
        }

        private void DrawImportWarning()
        {
            string path = AssetDatabase.GetAssetPath(targetMesh);
            bool importedModelMesh = !string.IsNullOrEmpty(path) && Path.GetExtension(path).ToLowerInvariant() == ".fbx";

            if (importedModelMesh)
            {
                EditorGUILayout.HelpBox("이 Mesh는 FBX에서 임포트된 데이터입니다. Reimport 시 변경 내용이 사라질 수 있으므로, 버텍스 컬러를 저장하기 전에 '편집용 복사본 생성'으로 별도 Mesh Asset을 만드는 것을 권장합니다.", MessageType.Warning);
            }
        }

        private void DrawSelectedColorPreview()
        {
            if (selectedVertices.Count != 1 || !TryGetColors(out Color32[] colors))
            {
                return;
            }

            foreach (int vertexIndex in selectedVertices)
            {
                Color color = colors[vertexIndex];
                EditorGUILayout.ColorField("선택 버텍스 색", color);
                EditorGUILayout.LabelField("선택 버텍스 인덱스", vertexIndex.ToString());
                break;
            }
        }

        private void CreateEditableMeshCopy()
        {
            if (targetMesh == null)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(EditedMeshFolder))
            {
                EnsureFolder(EditedMeshFolder);
            }

            Mesh copy = Instantiate(targetMesh);
            string sourceName = targetObject != null ? targetObject.name : targetMesh.name;
            copy.name = sourceName + "_" + targetMesh.name + "_VertexColor";

            string path = AssetDatabase.GenerateUniqueAssetPath(EditedMeshFolder + "/" + copy.name + ".asset");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            lastCreatedAssetPath = path;

            if (meshFilter != null)
            {
                Undo.RecordObject(meshFilter, "Assign Editable Vertex Color Mesh");
                meshFilter.sharedMesh = copy;
            }
            else if (skinnedMeshRenderer != null)
            {
                Undo.RecordObject(skinnedMeshRenderer, "Assign Editable Vertex Color Mesh");
                skinnedMeshRenderer.sharedMesh = copy;
            }
            else
            {
                standaloneMesh = copy;
            }

            targetMesh = copy;
            if (targetObject != null)
            {
                EditorUtility.SetDirty(targetObject);
                Selection.activeGameObject = targetObject;
            }

            EditorGUIUtility.PingObject(copy);
            Repaint();
        }

        private void ExportCurrentMeshToFbx()
        {
            if (targetMesh == null)
            {
                return;
            }

            EnsureFolder(ExportedFbxFolder);

            string defaultName = GetCleanExportName(targetMesh.name) + ".fbx";
            string path = EditorUtility.SaveFilePanelInProject(
                "FBX로 내보내기",
                defaultName,
                "fbx",
                "내보낼 FBX 파일 위치를 선택하세요.",
                ExportedFbxFolder);

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            GameObject exportObject = null;
            try
            {
                exportObject = new GameObject(GetCleanExportName(Path.GetFileNameWithoutExtension(path)));
                MeshFilter exportMeshFilter = exportObject.AddComponent<MeshFilter>();
                MeshRenderer exportRenderer = exportObject.AddComponent<MeshRenderer>();
                exportMeshFilter.sharedMesh = targetMesh;
                exportRenderer.sharedMaterial = GetOrCreateExportMaterial();

                string exportedPath = ModelExporter.ExportObject(path, exportObject);
                AssetDatabase.Refresh();

                if (string.IsNullOrEmpty(exportedPath))
                {
                    EditorUtility.DisplayDialog("FBX Export 실패", "FBX 파일을 내보내지 못했습니다.", "확인");
                    return;
                }

                Object exportedAsset = AssetDatabase.LoadAssetAtPath<Object>(exportedPath);
                if (exportedAsset != null)
                {
                    Selection.activeObject = exportedAsset;
                    EditorGUIUtility.PingObject(exportedAsset);
                }

                EditorUtility.DisplayDialog("FBX Export 완료", "FBX 파일을 내보냈습니다.\n" + exportedPath, "확인");
            }
            finally
            {
                if (exportObject != null)
                {
                    DestroyImmediate(exportObject);
                }
            }
        }

        private Material GetOrCreateExportMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ExportMaterialPath);
            if (material != null)
            {
                return material;
            }

            EnsureFolder(EditorAssetFolder);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/Internal-Colored");
            }

            if (shader == null)
            {
                return null;
            }

            material = new Material(shader)
            {
                name = "SimpleMeshEditor_Default"
            };
            material.SetColor("_BaseColor", new Color(0.72f, 0.76f, 0.8f, 1f));
            material.SetColor("_Color", new Color(0.72f, 0.76f, 0.8f, 1f));
            AssetDatabase.CreateAsset(material, ExportMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private string GetCleanExportName(string name)
        {
            string cleanName = name
                .Replace("_VertexColor", string.Empty)
                .Replace("_EDIT", string.Empty)
                .Replace("_Edit", string.Empty);

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                cleanName = cleanName.Replace(invalidChar, '_');
            }

            return string.IsNullOrEmpty(cleanName) ? "VFXMesh" : cleanName;
        }

        private void EnsureFolder(string folderPath)
        {
            string normalized = folderPath.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private void InitializeColors(Color color)
        {
            if (targetMesh == null)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(targetMesh, "Initialize Vertex Colors");
            Color32[] colors = new Color32[targetMesh.vertexCount];
            Color32 color32 = color;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color32;
            }

            targetMesh.colors32 = colors;
            EditorUtility.SetDirty(targetMesh);
            Repaint();
        }

        private void SelectAllVertices()
        {
            selectedVertices.Clear();
            for (int i = 0; i < targetMesh.vertexCount; i++)
            {
                selectedVertices.Add(i);
            }

            Repaint();
        }

        private void PickVertex(int vertexIndex, Event current)
        {
            bool additive = current.shift;
            bool toggle = current.control || current.command;

            if (!additive && !toggle)
            {
                selectedVertices.Clear();
            }

            if (toggle && selectedVertices.Contains(vertexIndex))
            {
                RemoveVertexFromSelection(vertexIndex);
            }
            else
            {
                AddVertexToSelection(vertexIndex);
            }

            Repaint();
        }

        private void AddVertexToSelection(int vertexIndex)
        {
            selectedVertices.Add(vertexIndex);

            if (!mirrorSamePositionVertices || targetMesh == null)
            {
                return;
            }

            Vector3[] vertices = targetMesh.vertices;
            Vector3 pickedPosition = vertices[vertexIndex];
            float sqrTolerance = samePositionTolerance * samePositionTolerance;

            for (int i = 0; i < vertices.Length; i++)
            {
                if ((vertices[i] - pickedPosition).sqrMagnitude <= sqrTolerance)
                {
                    selectedVertices.Add(i);
                }
            }
        }

        private void RemoveVertexFromSelection(int vertexIndex)
        {
            selectedVertices.Remove(vertexIndex);

            if (!mirrorSamePositionVertices || targetMesh == null)
            {
                return;
            }

            Vector3[] vertices = targetMesh.vertices;
            Vector3 pickedPosition = vertices[vertexIndex];
            float sqrTolerance = samePositionTolerance * samePositionTolerance;

            for (int i = 0; i < vertices.Length; i++)
            {
                if ((vertices[i] - pickedPosition).sqrMagnitude <= sqrTolerance)
                {
                    selectedVertices.Remove(i);
                }
            }
        }

        private void ApplyColorToSelection()
        {
            if (targetMesh == null || selectedVertices.Count == 0)
            {
                return;
            }

            Color32[] colors = GetOrCreateColors();
            Color32 value = editColor;

            Undo.RegisterCompleteObjectUndo(targetMesh, "Edit Vertex Colors");

            foreach (int index in selectedVertices)
            {
                Color32 color = colors[index];
                if (editR)
                {
                    color.r = value.r;
                }

                if (editG)
                {
                    color.g = value.g;
                }

                if (editB)
                {
                    color.b = value.b;
                }

                if (editA)
                {
                    color.a = value.a;
                }

                colors[index] = color;
            }

            targetMesh.colors32 = colors;
            EditorUtility.SetDirty(targetMesh);
            Repaint();
        }

        private bool TryGetColors(out Color32[] colors)
        {
            colors = targetMesh.colors32;
            return colors != null && colors.Length == targetMesh.vertexCount;
        }

        private Color32[] GetOrCreateColors()
        {
            Color32[] colors = targetMesh.colors32;
            if (colors != null && colors.Length == targetMesh.vertexCount)
            {
                return colors;
            }

            colors = new Color32[targetMesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(255, 255, 255, 255);
            }

            return colors;
        }

        private Color GetVertexDisplayColor(int vertexIndex)
        {
            if (TryGetColors(out Color32[] colors))
            {
                Color color = colors[vertexIndex];
                if (!drawLitMesh)
                {
                    color = GetChannelPreviewColor(color);
                }

                color.a = 1f;
                return color;
            }

            return new Color(0.2f, 0.7f, 1f, 1f);
        }

        private Color GetChannelPreviewColor(Color color)
        {
            bool anyRgb = editR || editG || editB;
            if (!anyRgb && editA)
            {
                return new Color(color.a, color.a, color.a, 1f);
            }

            return new Color(
                editR ? color.r : 0f,
                editG ? color.g : 0f,
                editB ? color.b : 0f,
                1f);
        }

        private enum UvTransform
        {
            RotateClockwise,
            RotateCounterClockwise,
            FlipHorizontal,
            FlipVertical
        }

        private bool HasUvChannel(int channel)
        {
            return GetUvList(channel).Count == targetMesh.vertexCount;
        }

        private List<Vector2> GetUvList(int channel)
        {
            List<Vector2> uvs = new List<Vector2>();
            if (targetMesh != null)
            {
                targetMesh.GetUVs(channel, uvs);
            }

            return uvs;
        }

        private void CopyUv1ToUv2()
        {
            List<Vector2> uv1 = GetUvList(0);
            if (uv1.Count != targetMesh.vertexCount)
            {
                EditorUtility.DisplayDialog("UV 복사 실패", "UV1 채널이 없거나 버텍스 수와 맞지 않습니다.", "확인");
                return;
            }

            Undo.RegisterCompleteObjectUndo(targetMesh, "Copy UV1 To UV2");
            targetMesh.SetUVs(1, uv1);
            EditorUtility.SetDirty(targetMesh);
            uvChannelIndex = 1;
            Repaint();
        }

        private void TransformUvChannel(int channel, UvTransform transform)
        {
            List<Vector2> uvs = GetUvList(channel);
            if (uvs.Count != targetMesh.vertexCount)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(targetMesh, "Edit UV Channel");
            for (int i = 0; i < uvs.Count; i++)
            {
                Vector2 uv = uvs[i];
                switch (transform)
                {
                    case UvTransform.RotateClockwise:
                        uv = new Vector2(uv.y, 1f - uv.x);
                        break;
                    case UvTransform.RotateCounterClockwise:
                        uv = new Vector2(1f - uv.y, uv.x);
                        break;
                    case UvTransform.FlipHorizontal:
                        uv = new Vector2(1f - uv.x, uv.y);
                        break;
                    case UvTransform.FlipVertical:
                        uv = new Vector2(uv.x, 1f - uv.y);
                        break;
                }

                uvs[i] = uv;
            }

            targetMesh.SetUVs(channel, uvs);
            EditorUtility.SetDirty(targetMesh);
            Repaint();
        }

        private Rect GetUvPreviewRect(Rect rect)
        {
            float margin = 28f;
            float size = Mathf.Min(rect.width, rect.height) - margin * 2f;
            size = Mathf.Max(size, 10f);
            return new Rect(
                rect.x + (rect.width - size) * 0.5f,
                rect.y + (rect.height - size) * 0.5f,
                size,
                size);
        }

        private void DrawUvGrid(Rect rect)
        {
            Handles.color = new Color(0.32f, 0.32f, 0.32f, 1f);
            Handles.DrawSolidRectangleWithOutline(rect, new Color(0.11f, 0.11f, 0.11f, 1f), new Color(0.6f, 0.6f, 0.6f, 1f));

            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                float x = Mathf.Lerp(rect.xMin, rect.xMax, t);
                float y = Mathf.Lerp(rect.yMin, rect.yMax, t);
                Handles.DrawLine(new Vector2(x, rect.yMin), new Vector2(x, rect.yMax));
                Handles.DrawLine(new Vector2(rect.xMin, y), new Vector2(rect.xMax, y));
            }

            GUI.Label(new Rect(rect.xMin, rect.yMax + 4f, 80f, 20f), "0,0", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.xMax - 32f, rect.yMin - 18f, 80f, 20f), "1,1", EditorStyles.miniLabel);
        }

        private void DrawUvTriangles(Rect rect, List<Vector2> uvs)
        {
            Handles.color = new Color(0.1f, 0.65f, 1f, 0.95f);
            for (int subMeshIndex = 0; subMeshIndex < targetMesh.subMeshCount; subMeshIndex++)
            {
                MeshTopology topology = targetMesh.GetTopology(subMeshIndex);
                int[] indices = targetMesh.GetIndices(subMeshIndex);

                if (topology == MeshTopology.Triangles)
                {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        DrawUvEdge(rect, uvs, indices[i], indices[i + 1]);
                        DrawUvEdge(rect, uvs, indices[i + 1], indices[i + 2]);
                        DrawUvEdge(rect, uvs, indices[i + 2], indices[i]);
                    }
                }
                else if (topology == MeshTopology.Quads)
                {
                    for (int i = 0; i + 3 < indices.Length; i += 4)
                    {
                        DrawUvEdge(rect, uvs, indices[i], indices[i + 1]);
                        DrawUvEdge(rect, uvs, indices[i + 1], indices[i + 2]);
                        DrawUvEdge(rect, uvs, indices[i + 2], indices[i + 3]);
                        DrawUvEdge(rect, uvs, indices[i + 3], indices[i]);
                    }
                }
                else if (topology == MeshTopology.Lines)
                {
                    for (int i = 0; i + 1 < indices.Length; i += 2)
                    {
                        DrawUvEdge(rect, uvs, indices[i], indices[i + 1]);
                    }
                }
                else if (topology == MeshTopology.LineStrip)
                {
                    for (int i = 0; i + 1 < indices.Length; i++)
                    {
                        DrawUvEdge(rect, uvs, indices[i], indices[i + 1]);
                    }
                }
            }
        }

        private void DrawUvEdge(Rect rect, List<Vector2> uvs, int a, int b)
        {
            if (a < 0 || b < 0 || a >= uvs.Count || b >= uvs.Count)
            {
                return;
            }

            Handles.DrawLine(UvToPreviewPoint(rect, uvs[a]), UvToPreviewPoint(rect, uvs[b]));
        }

        private Vector2 UvToPreviewPoint(Rect rect, Vector2 uv)
        {
            return new Vector2(
                rect.xMin + uv.x * rect.width,
                rect.yMax - uv.y * rect.height);
        }
    }
}
