using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoofTopStudio.TexturePacker
{
    public class TexturePacker : EditorWindow
    {
        private const string WindowTitle = "Texture Packer";
        private const string DefaultOutputFolder = "Assets";
        private const float PackControlWidth = 360f;
        private const float PackPanelGap = 6f;

        private ToolTab currentTab;
        private Vector2 scrollPosition;
        private string statusMessage = "Drop a texture asset to begin.";
        private MessageType statusType = MessageType.Info;

        private Texture2D sourceTexture;
        private Texture2D rPreview;
        private Texture2D gPreview;
        private Texture2D bPreview;
        private Texture2D aPreview;
        private bool unpackR = true;
        private bool unpackG = true;
        private bool unpackB = true;
        private bool unpackA = true;
        private string unpackOutputFolder = DefaultOutputFolder;

        private Texture2D packR;
        private Texture2D packG;
        private Texture2D packB;
        private Texture2D packA;
        private Texture2D packPreview;
        private Texture2D packAlphaPreview;
        private string packOutputFolder = DefaultOutputFolder;
        private string packFileName = "Packed";

        [MenuItem("Tools/RoofTop Studio/Texture Packer")]
        public static void ShowWindow()
        {
            GetWindow<TexturePacker>(WindowTitle);
        }

        private void OnDisable()
        {
            ClearUnpackPreviews();
            DestroyPreview(ref packPreview);
            DestroyPreview(ref packAlphaPreview);
        }

        private void OnGUI()
        {
            ToolTab selectedTab = (ToolTab)GUILayout.Toolbar((int)currentTab, new[] { "Unpack", "Pack" }, GUILayout.Height(28f));
            if (selectedTab != currentTab)
            {
                currentTab = selectedTab;
                scrollPosition = Vector2.zero;

                if (currentTab == ToolTab.Pack)
                {
                    FitWindowForPackTab();
                }
            }

            EditorGUILayout.Space(8f);
            if (currentTab == ToolTab.Unpack)
            {
                DrawUnpackTab();
            }
            else
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                DrawPackTab();
                EditorGUILayout.EndScrollView();
            }

            if (currentTab == ToolTab.Unpack && !string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space(6f);
                DrawStatusBox();
            }
        }

        private void DrawUnpackTab()
        {
            GUILayout.Label("RGBA Channel Separator", EditorStyles.boldLabel);
            DrawUnpackDropArea();

            EditorGUILayout.Space(8f);
            DrawUnpackChannelOptions();

            EditorGUILayout.Space(8f);
            DrawOutputSettings("Unpack Output Path", ref unpackOutputFolder, sourceTexture != null, GetSourceDirectory);

            EditorGUILayout.Space(8f);
            DrawUnpackButtons();
        }

        private void DrawUnpackDropArea()
        {
            float previewHeight = GetTightUnpackPreviewHeight();
            Rect dropRect = GUILayoutUtility.GetRect(0f, previewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(dropRect, new Color(0.08f, 0.1f, 0.1f));

            if (sourceTexture == null)
            {
                GUI.Label(dropRect, "Drop Source Texture Here", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                Rect headerRect = new Rect(dropRect.x + 8f, dropRect.y + 8f, dropRect.width - 16f, 22f);
                GUI.Label(headerRect, $"{sourceTexture.name}  ({sourceTexture.width} x {sourceTexture.height})", EditorStyles.centeredGreyMiniLabel);

                Rect gridRect = new Rect(dropRect.x + 8f, dropRect.y + 36f, dropRect.width - 16f, dropRect.height - 44f);
                DrawFourChannelSquareGrid(gridRect, rPreview, gPreview, bPreview, aPreview, unpackR, unpackG, unpackB, unpackA);
            }

            HandleSingleTextureDrop(dropRect, SetSourceTexture);
        }

        private float GetTightUnpackPreviewHeight()
        {
            float gap = 8f;
            float horizontalPadding = 16f;
            float headerAndPadding = 44f;
            float availableWidth = Mathf.Max(1f, position.width - horizontalPadding);
            float widthBasedTileSize = availableWidth >= 860f
                ? (availableWidth - gap * 3f) * 0.25f
                : (availableWidth - gap) * 0.5f;

            float reservedUiHeight = 245f;
            float availableHeight = Mathf.Max(180f, position.height - reservedUiHeight);
            float tileSize = Mathf.Min(widthBasedTileSize, availableHeight - headerAndPadding);
            return Mathf.Clamp(tileSize + headerAndPadding, 220f, availableHeight);
        }

        private void DrawUnpackChannelOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Channels to Unpack", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    unpackR = EditorGUILayout.ToggleLeft("R", unpackR, GUILayout.Width(60f));
                    unpackG = EditorGUILayout.ToggleLeft("G", unpackG, GUILayout.Width(60f));
                    unpackB = EditorGUILayout.ToggleLeft("B", unpackB, GUILayout.Width(60f));
                    unpackA = EditorGUILayout.ToggleLeft("A", unpackA, GUILayout.Width(60f));
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawUnpackButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load Source Texture", GUILayout.Height(30f)))
                {
                    BrowseSourceTexture();
                }

                if (GUILayout.Button("Clear", GUILayout.Height(30f), GUILayout.Width(70f)))
                {
                    SetSourceTexture(null);
                }

                GUI.enabled = sourceTexture != null && HasSelectedUnpackChannel();
                if (GUILayout.Button("Unpack Channels", GUILayout.Height(30f), GUILayout.Width(140f)))
                {
                    UnpackChannels();
                }
                GUI.enabled = true;
            }

            if (sourceTexture != null && !HasSelectedUnpackChannel())
            {
                EditorGUILayout.HelpBox("Please select at least one channel to unpack.", MessageType.Warning);
            }
        }

        private void DrawPackTab()
        {
            GUILayout.Label("RGBA Channel Packer", EditorStyles.boldLabel);
            float contentHeight = Mathf.Max(560f, position.height - 86f);
            Rect contentRect = GUILayoutUtility.GetRect(0f, contentHeight, GUILayout.ExpandWidth(true));

            Rect controlRect = new Rect(contentRect.x, contentRect.y, PackControlWidth, contentRect.height);
            float previewWidth = Mathf.Max(1f, contentRect.width - PackControlWidth - PackPanelGap);
            Rect previewRect = new Rect(controlRect.xMax + PackPanelGap, contentRect.y, previewWidth, contentRect.height);

            GUILayout.BeginArea(controlRect);
            DrawPackControlPanel();
            GUILayout.EndArea();

            DrawPackedPreviewArea(previewRect);
        }

        private void DrawPackControlPanel()
        {
            DrawPackOutputSettings();

            EditorGUILayout.Space(8f);
            DrawPackSlots();

            EditorGUILayout.Space(8f);
            DrawPackButtons();

            EditorGUILayout.Space(8f);
            DrawStatusBox();
        }

        private void DrawPackSlots()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Channel Inputs", EditorStyles.boldLabel);
                DrawPackSlot("R", PackChannel.Red);
                DrawPackSlot("G", PackChannel.Green);
                DrawPackSlot("B", PackChannel.Blue);
                DrawPackSlot("A", PackChannel.Alpha);
            }
        }

        private void DrawPackSlot(string label, PackChannel channel)
        {
            Texture2D channelTexture = GetPackChannel(channel);
            Rect rect = GUILayoutUtility.GetRect(140f, 44f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.02f, 0.03f, 0.03f));
            GUI.Box(rect, GUIContent.none);

            Rect labelRect = new Rect(rect.x + 8f, rect.y + 12f, 26f, 20f);
            GUI.Label(labelRect, label, EditorStyles.boldLabel);

            string textureName = channelTexture == null ? "Drop Texture Here" : channelTexture.name;
            Rect nameRect = new Rect(rect.x + 36f, rect.y + 12f, rect.width - 112f, 20f);
            GUI.Label(nameRect, textureName, EditorStyles.centeredGreyMiniLabel);

            Rect buttonRect = new Rect(rect.xMax - 68f, rect.y + 10f, 58f, 24f);
            if (GUI.Button(buttonRect, channelTexture == null ? "Browse" : "Clear"))
            {
                if (channelTexture == null)
                {
                    Texture2D browsed = BrowseTextureAsset();
                    if (browsed != null)
                    {
                        SetPackChannel(channel, browsed);
                        GeneratePackPreview();
                    }
                }
                else
                {
                    SetPackChannel(channel, null);
                    GeneratePackPreview();
                }
            }

            HandleSingleTextureDrop(rect, texture =>
            {
                SetPackChannel(channel, texture);
                GeneratePackPreview();
            });
        }

        private void DrawPackOutputSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                packFileName = EditorGUILayout.TextField("Export Name", packFileName);
                DrawOutputSettings("Pack Output Path", ref packOutputFolder, HasAnyPackChannel(), GetFirstPackChannelDirectory);
            }
        }

        private void DrawPackedPreviewArea(Rect areaRect)
        {
            EditorGUI.DrawRect(areaRect, new Color(0.08f, 0.1f, 0.1f));

            float gap = 8f;
            Rect contentRect = new Rect(areaRect.x + gap, areaRect.y + gap, areaRect.width - gap * 2f, areaRect.height - gap * 2f);
            float squareSize = Mathf.Max(1f, Mathf.Min((contentRect.width - gap) * 0.5f, contentRect.height));
            float startX = contentRect.x;
            float startY = contentRect.y + (contentRect.height - squareSize) * 0.5f;
            Rect packedRect = new Rect(startX, startY, squareSize, squareSize);
            Rect alphaRect = new Rect(startX + squareSize + gap, startY, squareSize, squareSize);

            DrawPreviewPanel(packedRect, "Packed RGB Preview", packPreview, "Drop channel textures on the left.");
            DrawPreviewPanel(alphaRect, "A Channel Preview", packAlphaPreview, "A defaults to white.");
        }

        private void DrawPackButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = HasAnyPackChannel();
                if (GUILayout.Button("Generate Preview", GUILayout.Height(30f), GUILayout.Width(130f)))
                {
                    GeneratePackPreview();
                }

                if (GUILayout.Button("Pack Texture", GUILayout.Height(30f), GUILayout.Width(120f)))
                {
                    PackChannels();
                }
                GUI.enabled = true;

                if (GUILayout.Button("Clear", GUILayout.Height(30f), GUILayout.Width(70f)))
                {
                    ClearPackInputs();
                }
            }
        }

        private void DrawStatusBox()
        {
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }
        }

        private void FitWindowForPackTab()
        {
            float previewSize = Mathf.Clamp(position.height - 190f, 360f, 560f);
            float desiredWidth = PackControlWidth + PackPanelGap + previewSize * 2f + 32f;
            desiredWidth = Mathf.Clamp(desiredWidth, 980f, 1520f);

            minSize = new Vector2(980f, 560f);
            if (Mathf.Abs(position.width - desiredWidth) > 24f)
            {
                Rect nextPosition = position;
                nextPosition.width = desiredWidth;
                position = nextPosition;
            }
        }

        private void DrawOutputSettings(string label, ref string outputFolder, bool enableSourceButton, System.Func<string> sourceFolderGetter)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(label, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    outputFolder = EditorGUILayout.TextField(outputFolder);

                    if (GUILayout.Button("...", GUILayout.Width(32f)))
                    {
                        string startFolder = ResolveAbsoluteFolder(outputFolder);
                        string selected = EditorUtility.OpenFolderPanel("Select Output Folder", startFolder, string.Empty);
                        if (!string.IsNullOrEmpty(selected))
                        {
                            outputFolder = ConvertToProjectRelativePath(selected);
                            SetStatus($"Output path set to: {outputFolder}", MessageType.Info);
                        }
                    }

                    GUI.enabled = enableSourceButton;
                    if (GUILayout.Button("Source", GUILayout.Width(58f)))
                    {
                        outputFolder = sourceFolderGetter();
                        SetStatus($"Output path set to source folder: {outputFolder}", MessageType.Info);
                    }
                    GUI.enabled = true;
                }
            }
        }

        private void SetSourceTexture(Texture2D texture)
        {
            sourceTexture = texture;
            ClearUnpackPreviews();

            if (sourceTexture == null)
            {
                statusMessage = "Drop a texture asset to begin.";
                statusType = MessageType.Info;
                return;
            }

            unpackOutputFolder = GetSourceDirectory();
            GenerateUnpackPreviews();
        }

        private void GenerateUnpackPreviews()
        {
            if (sourceTexture == null)
            {
                return;
            }

            ClearUnpackPreviews();
            TextureReadResult readResult = ReadTexturePixelsAsDefault(sourceTexture);
            Color[] pixels = readResult.Pixels;
            rPreview = CreateChannelTexture(readResult.Width, readResult.Height, pixels, Channel.Red);
            gPreview = CreateChannelTexture(readResult.Width, readResult.Height, pixels, Channel.Green);
            bPreview = CreateChannelTexture(readResult.Width, readResult.Height, pixels, Channel.Blue);
            aPreview = CreateChannelTexture(readResult.Width, readResult.Height, pixels, Channel.Alpha);

            SetStatus($"Unpack preview ready: {sourceTexture.name}", MessageType.Info);
            Repaint();
        }

        private void UnpackChannels()
        {
            if (sourceTexture == null)
            {
                EditorUtility.DisplayDialog("Error", "Please drop or load a source texture first.", "OK");
                return;
            }

            if (!HasSelectedUnpackChannel())
            {
                EditorUtility.DisplayDialog("Error", "Please select at least one channel to unpack.", "OK");
                return;
            }

            if (rPreview == null || gPreview == null || bPreview == null || aPreview == null)
            {
                GenerateUnpackPreviews();
            }

            string resolvedFolder = ResolveAbsoluteFolder(unpackOutputFolder);
            Directory.CreateDirectory(resolvedFolder);

            string baseName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sourceTexture));
            int savedCount = 0;

            if (unpackR && SaveTexture(rPreview, resolvedFolder, baseName, "_R"))
            {
                savedCount++;
            }

            if (unpackG && SaveTexture(gPreview, resolvedFolder, baseName, "_G"))
            {
                savedCount++;
            }

            if (unpackB && SaveTexture(bPreview, resolvedFolder, baseName, "_B"))
            {
                savedCount++;
            }

            if (unpackA && SaveTexture(aPreview, resolvedFolder, baseName, "_A"))
            {
                savedCount++;
            }

            AssetDatabase.Refresh();
            string displayPath = ConvertToProjectRelativePath(resolvedFolder);
            SetStatus($"{savedCount} channel(s) unpacked to: {displayPath}", MessageType.Info);
            EditorUtility.DisplayDialog("Success", $"{savedCount} channel(s) unpacked successfully!\nSaved to: {displayPath}", "OK");
        }

        private void GeneratePackPreview()
        {
            DestroyPreview(ref packPreview);
            DestroyPreview(ref packAlphaPreview);

            Texture2D reference = GetFirstPackChannel();
            if (reference == null)
            {
                SetStatus("Drop at least one channel texture for packing.", MessageType.Warning);
                return;
            }

            packPreview = CreatePackedTexture(reference.width, reference.height, out packAlphaPreview);
            SetStatus($"Packed preview ready: {packPreview.width} x {packPreview.height}", MessageType.Info);
            Repaint();
        }

        private void PackChannels()
        {
            if (!HasAnyPackChannel())
            {
                EditorUtility.DisplayDialog("Error", "Please assign at least one channel texture.", "OK");
                return;
            }

            if (packPreview == null)
            {
                GeneratePackPreview();
            }

            if (packPreview == null)
            {
                return;
            }

            string fileName = string.IsNullOrWhiteSpace(packFileName) ? "Packed" : packFileName.Trim();
            string resolvedFolder = ResolveAbsoluteFolder(packOutputFolder);
            Directory.CreateDirectory(resolvedFolder);

            string path = Path.Combine(resolvedFolder, $"{fileName}.png");
            File.WriteAllBytes(path, packPreview.EncodeToPNG());
            AssetDatabase.Refresh();

            string displayPath = ConvertToProjectRelativePath(path);
            SetStatus($"Packed texture saved: {displayPath}", MessageType.Info);
            EditorUtility.DisplayDialog("Success", $"Packed texture saved successfully!\n{displayPath}", "OK");
        }

        private Texture2D CreatePackedTexture(int width, int height, out Texture2D alphaPreview)
        {
            float[] rValues = ReadChannelValues(packR, width, height, 0f);
            float[] gValues = ReadChannelValues(packG, width, height, 0f);
            float[] bValues = ReadChannelValues(packB, width, height, 0f);
            float[] aValues = ReadChannelValues(packA, width, height, 1f);

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            alphaPreview = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[width * height];
            Color[] alphaPixels = new Color[width * height];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(rValues[i], gValues[i], bValues[i], aValues[i]);
                alphaPixels[i] = new Color(aValues[i], aValues[i], aValues[i], 1f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            texture.name = "Packed Preview";

            alphaPreview.SetPixels(alphaPixels);
            alphaPreview.Apply();
            alphaPreview.name = "Packed Alpha Preview";
            return texture;
        }

        private static float[] ReadChannelValues(Texture2D texture, int width, int height, float defaultValue)
        {
            float[] values = new float[width * height];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = defaultValue;
            }

            if (texture == null)
            {
                return values;
            }

            TextureReadResult readResult = ReadTexturePixelsAsDefault(texture, width, height);
            for (int i = 0; i < values.Length; i++)
            {
                Color color = readResult.Pixels[i];
                values[i] = color.grayscale;
            }

            return values;
        }

        private void BrowseSourceTexture()
        {
            Texture2D texture = BrowseTextureAsset();
            if (texture != null)
            {
                SetSourceTexture(texture);
            }
        }

        private static Texture2D BrowseTextureAsset()
        {
            string[] filters = { "Texture files", "png,jpg,jpeg,tga,psd,tif,tiff,exr", "All files", "*" };
            string selected = EditorUtility.OpenFilePanelWithFilters("Select Texture", Application.dataPath, filters);
            if (string.IsNullOrEmpty(selected))
            {
                return null;
            }

            string assetPath = ConvertToProjectRelativePath(selected);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private void HandleSingleTextureDrop(Rect dropRect, System.Action<Texture2D> onTextureDropped)
        {
            Event currentEvent = Event.current;
            if (!dropRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            Texture2D draggedTexture = null;
            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                if (reference is Texture2D texture)
                {
                    draggedTexture = texture;
                    break;
                }
            }

            DragAndDrop.visualMode = draggedTexture != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (draggedTexture != null)
                {
                    onTextureDropped(draggedTexture);
                }
            }

            currentEvent.Use();
        }

        private static bool SaveTexture(Texture2D texture, string folderPath, string baseName, string suffix)
        {
            if (texture == null)
            {
                return false;
            }

            string path = Path.Combine(folderPath, $"{baseName}{suffix}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return true;
        }

        private static TextureReadResult ReadTexturePixelsAsDefault(Texture2D texture)
        {
            return ReadTexturePixelsAsDefault(texture, texture.width, texture.height);
        }

        private static TextureReadResult ReadTexturePixelsAsDefault(Texture2D texture, int width, int height)
        {
            string texturePath = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                Color[] fallbackPixels = ReadTexturePixels(texture, width, height);
                return new TextureReadResult(fallbackPixels, width, height);
            }

            TextureImporterType originalType = importer.textureType;
            TextureImporterCompression originalCompression = importer.textureCompression;

            try
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                AssetDatabase.ImportAsset(texturePath);

                Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                Color[] pixels = ReadTexturePixels(importedTexture, width, height);
                return new TextureReadResult(pixels, width, height);
            }
            finally
            {
                importer.textureType = originalType;
                importer.textureCompression = originalCompression;
                AssetDatabase.ImportAsset(texturePath);
            }
        }

        private static Color[] ReadTexturePixels(Texture2D texture, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);

            try
            {
                Graphics.Blit(texture, renderTexture);
                RenderTexture.active = renderTexture;
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                readable.Apply();
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                DestroyImmediate(readable);
            }
        }

        private static Texture2D CreateChannelTexture(int width, int height, Color[] pixels, Channel channel)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            Color[] channelPixels = new Color[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                float value = GetChannelValue(pixels[i], channel);
                channelPixels[i] = new Color(value, value, value, 1f);
            }

            texture.SetPixels(channelPixels);
            texture.Apply();
            texture.name = $"{channel} Preview";
            return texture;
        }

        private static float GetChannelValue(Color color, Channel channel)
        {
            switch (channel)
            {
                case Channel.Red:
                    return color.r;
                case Channel.Green:
                    return color.g;
                case Channel.Blue:
                    return color.b;
                case Channel.Alpha:
                    return color.a;
                default:
                    return 0f;
            }
        }

        private void DrawFourChannelSquareGrid(Rect gridRect, Texture2D r, Texture2D g, Texture2D b, Texture2D a, bool showR, bool showG, bool showB, bool showA)
        {
            float gap = 8f;
            if (gridRect.width >= 860f)
            {
                float tileSize = Mathf.Max(1f, Mathf.Min((gridRect.width - gap * 3f) * 0.25f, gridRect.height));
                float totalWidth = tileSize * 4f + gap * 3f;
                float startX = gridRect.x + (gridRect.width - totalWidth) * 0.5f;
                float startY = gridRect.y + (gridRect.height - tileSize) * 0.5f;

                DrawPreviewTile(new Rect(startX, startY, tileSize, tileSize), "R Channel", r, showR);
                DrawPreviewTile(new Rect(startX + (tileSize + gap), startY, tileSize, tileSize), "G Channel", g, showG);
                DrawPreviewTile(new Rect(startX + (tileSize + gap) * 2f, startY, tileSize, tileSize), "B Channel", b, showB);
                DrawPreviewTile(new Rect(startX + (tileSize + gap) * 3f, startY, tileSize, tileSize), "A Channel", a, showA);
                return;
            }

            float tileSizeSmall = Mathf.Max(1f, Mathf.Min((gridRect.width - gap) * 0.5f, (gridRect.height - gap) * 0.5f));
            float totalWidthSmall = tileSizeSmall * 2f + gap;
            float totalHeightSmall = tileSizeSmall * 2f + gap;
            float startXSmall = gridRect.x + (gridRect.width - totalWidthSmall) * 0.5f;
            float startYSmall = gridRect.y + (gridRect.height - totalHeightSmall) * 0.5f;

            DrawPreviewTile(new Rect(startXSmall, startYSmall, tileSizeSmall, tileSizeSmall), "R Channel", r, showR);
            DrawPreviewTile(new Rect(startXSmall + tileSizeSmall + gap, startYSmall, tileSizeSmall, tileSizeSmall), "G Channel", g, showG);
            DrawPreviewTile(new Rect(startXSmall, startYSmall + tileSizeSmall + gap, tileSizeSmall, tileSizeSmall), "B Channel", b, showB);
            DrawPreviewTile(new Rect(startXSmall + tileSizeSmall + gap, startYSmall + tileSizeSmall + gap, tileSizeSmall, tileSizeSmall), "A Channel", a, showA);
        }

        private static void DrawPreviewTile(Rect rect, string label, Texture2D texture, bool selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.02f, 0.03f, 0.03f));
            GUI.Box(rect, GUIContent.none);

            Rect labelRect = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 18f);
            GUI.Label(labelRect, selected ? label : $"{label} (Skip)", EditorStyles.centeredGreyMiniLabel);

            if (texture == null)
            {
                GUI.Label(rect, "No Preview", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Rect previewRect = new Rect(rect.x + 6f, rect.y + 26f, rect.width - 12f, rect.height - 32f);
            Rect fitted = FitRect(previewRect, texture.width, texture.height);
            EditorGUI.DrawPreviewTexture(fitted, texture, null, ScaleMode.ScaleToFit);
        }

        private static void DrawPreviewPanel(Rect rect, string label, Texture2D texture, string emptyMessage)
        {
            EditorGUI.DrawRect(rect, new Color(0.02f, 0.03f, 0.03f));
            GUI.Box(rect, GUIContent.none);

            Rect labelRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 20f);
            GUI.Label(labelRect, label, EditorStyles.centeredGreyMiniLabel);

            Rect previewRect = new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, rect.height - 42f);
            if (texture == null)
            {
                GUI.Label(previewRect, emptyMessage, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Rect fitted = FitRect(previewRect, texture.width, texture.height);
            EditorGUI.DrawPreviewTexture(fitted, texture, null, ScaleMode.ScaleToFit);
        }

        private static Rect FitRect(Rect rect, int width, int height)
        {
            float textureAspect = (float)width / height;
            float rectAspect = rect.width / rect.height;

            if (textureAspect > rectAspect)
            {
                float fittedHeight = rect.width / textureAspect;
                return new Rect(rect.x, rect.y + (rect.height - fittedHeight) * 0.5f, rect.width, fittedHeight);
            }

            float fittedWidth = rect.height * textureAspect;
            return new Rect(rect.x + (rect.width - fittedWidth) * 0.5f, rect.y, fittedWidth, rect.height);
        }

        private void ClearUnpackPreviews()
        {
            DestroyPreview(ref rPreview);
            DestroyPreview(ref gPreview);
            DestroyPreview(ref bPreview);
            DestroyPreview(ref aPreview);
        }

        private void ClearPackInputs()
        {
            packR = null;
            packG = null;
            packB = null;
            packA = null;
            DestroyPreview(ref packPreview);
            DestroyPreview(ref packAlphaPreview);
            SetStatus("Pack inputs cleared.", MessageType.Info);
        }

        private static void DestroyPreview(ref Texture2D texture)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        private bool HasSelectedUnpackChannel()
        {
            return unpackR || unpackG || unpackB || unpackA;
        }

        private bool HasAnyPackChannel()
        {
            return packR != null || packG != null || packB != null || packA != null;
        }

        private Texture2D GetFirstPackChannel()
        {
            if (packR != null)
            {
                return packR;
            }

            if (packG != null)
            {
                return packG;
            }

            if (packB != null)
            {
                return packB;
            }

            return packA;
        }

        private Texture2D GetPackChannel(PackChannel channel)
        {
            switch (channel)
            {
                case PackChannel.Red:
                    return packR;
                case PackChannel.Green:
                    return packG;
                case PackChannel.Blue:
                    return packB;
                case PackChannel.Alpha:
                    return packA;
                default:
                    return null;
            }
        }

        private void SetPackChannel(PackChannel channel, Texture2D texture)
        {
            switch (channel)
            {
                case PackChannel.Red:
                    packR = texture;
                    break;
                case PackChannel.Green:
                    packG = texture;
                    break;
                case PackChannel.Blue:
                    packB = texture;
                    break;
                case PackChannel.Alpha:
                    packA = texture;
                    break;
            }
        }

        private string GetSourceDirectory()
        {
            if (sourceTexture == null)
            {
                return DefaultOutputFolder;
            }

            return GetTextureDirectory(sourceTexture);
        }

        private string GetFirstPackChannelDirectory()
        {
            Texture2D texture = GetFirstPackChannel();
            return texture == null ? DefaultOutputFolder : GetTextureDirectory(texture);
        }

        private static string GetTextureDirectory(Texture2D texture)
        {
            string texturePath = AssetDatabase.GetAssetPath(texture);
            string directory = Path.GetDirectoryName(texturePath);
            return string.IsNullOrEmpty(directory) ? DefaultOutputFolder : directory.Replace("\\", "/");
        }

        private static string ResolveAbsoluteFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = DefaultOutputFolder;
            }

            if (Path.IsPathRooted(folder))
            {
                return folder;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, folder));
        }

        private static string ConvertToProjectRelativePath(string path)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
            string normalizedPath = path.Replace("\\", "/");

            if (normalizedPath.StartsWith(projectRoot))
            {
                string relativePath = normalizedPath.Substring(projectRoot.Length).TrimStart('/');
                return string.IsNullOrEmpty(relativePath) ? "." : relativePath;
            }

            return normalizedPath;
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
        }

        private enum ToolTab
        {
            Unpack,
            Pack
        }

        private enum PackChannel
        {
            Red,
            Green,
            Blue,
            Alpha
        }

        private enum Channel
        {
            Red,
            Green,
            Blue,
            Alpha
        }

        private struct TextureReadResult
        {
            public TextureReadResult(Color[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }

            public Color[] Pixels { get; }
            public int Width { get; }
            public int Height { get; }
        }
    }
}
