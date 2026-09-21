using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VFXTools.SDFGenerator
{
    public sealed class SDFGenerator : EditorWindow
    {
        private const string WindowTitle = "SDF Generator";
        private const string ExportPath = "Assets";
        private const string MainFileBaseName = "sdf_main";
        private const string FlippedFileBaseName = "sdf_flipped";
        private const string MaskFileBaseName = "sdf_mask";
        private const string PngExtension = ".png";

        private readonly List<Texture2D> inputTextures = new List<Texture2D>();

        private Vector2 inputScroll;
        private Vector2 previewScroll;
        private Texture2D mainPreview;
        private Texture2D flippedPreview;
        private Texture2D maskPreview;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;

        private int blendIterations = 50;
        private float maskThreshold = 0.5f;
        private bool createFlippedCopy = true;
        private bool createMask = true;
        private bool useAlpha = true;

        [MenuItem("Tools/RoofTop Studio/SDF Generator")]
        public static void ShowWindow()
        {
            GetWindow<SDFGenerator>(WindowTitle);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSettingsPanel();
                DrawPreviewPanel();
            }
        }

        private void DrawSettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(340f)))
            {
                GUILayout.Label("1. Input Images", EditorStyles.boldLabel);
                DrawInputList();

                EditorGUILayout.Space(10f);
                GUILayout.Label("2. Generation Settings", EditorStyles.boldLabel);
                blendIterations = EditorGUILayout.IntSlider("Blending Times", blendIterations, 0, 200);
                maskThreshold = EditorGUILayout.Slider("Mask Threshold", maskThreshold, 0f, 1f);
                useAlpha = EditorGUILayout.Toggle("Use Transparent Alpha", useAlpha);
                createFlippedCopy = EditorGUILayout.Toggle("Create Flipped Copy", createFlippedCopy);
                createMask = EditorGUILayout.Toggle("Create Mask", createMask);

                EditorGUILayout.Space(8f);
                GUI.enabled = inputTextures.Count > 0;
                if (GUILayout.Button("Run SDF Tool", GUILayout.Height(34f)))
                {
                    GenerateAndSave();
                }
                GUI.enabled = true;

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.HelpBox(statusMessage, statusType);
                }
            }
        }

        private void DrawInputList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect dropRect = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(dropRect, new Color(0.16f, 0.16f, 0.16f));
                GUI.Label(dropRect, "Drag Texture Assets Here", EditorStyles.centeredGreyMiniLabel);
                HandleTextureDrop(dropRect);

                inputScroll = EditorGUILayout.BeginScrollView(inputScroll, GUILayout.Height(190f));
                for (int i = 0; i < inputTextures.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        inputTextures[i] = (Texture2D)EditorGUILayout.ObjectField(inputTextures[i], typeof(Texture2D), false);

                        GUI.enabled = i > 0;
                        if (GUILayout.Button("Up", GUILayout.Width(34f)))
                        {
                            Swap(i, i - 1);
                        }

                        GUI.enabled = i < inputTextures.Count - 1;
                        if (GUILayout.Button("Dn", GUILayout.Width(34f)))
                        {
                            Swap(i, i + 1);
                        }

                        GUI.enabled = true;
                        if (GUILayout.Button("X", GUILayout.Width(24f)))
                        {
                            inputTextures.RemoveAt(i);
                            i--;
                        }
                    }
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Sort By Name"))
                    {
                        SortInputsByName();
                    }

                    if (GUILayout.Button("Clear"))
                    {
                        inputTextures.Clear();
                    }
                }
            }
        }

        private void HandleTextureDrop(Rect dropRect)
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

            bool hasTexture = DragAndDrop.objectReferences.Any(reference => reference is Texture2D);
            DragAndDrop.visualMode = hasTexture ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (currentEvent.type != EventType.DragPerform)
            {
                currentEvent.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            int addedCount = 0;

            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                if (reference is Texture2D texture && !inputTextures.Contains(texture))
                {
                    inputTextures.Add(texture);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                SortInputsByName();
                SetStatus($"Added {addedCount} texture(s). Output path: {ExportPath}.", MessageType.Info);
            }
            else
            {
                SetStatus("Drop Texture2D assets from the Project window.", MessageType.Warning);
            }

            currentEvent.Use();
        }

        private void DrawPreviewPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label("Output Previews", EditorStyles.boldLabel);
                previewScroll = EditorGUILayout.BeginScrollView(previewScroll);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawPreview("Main SDF", mainPreview);
                    DrawPreview("Flipped Copy", flippedPreview);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawPreview("Mask", maskPreview);
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawPreview(string label, Texture2D texture)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(300f), GUILayout.Height(260f)))
            {
                GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel);
                Rect rect = GUILayoutUtility.GetRect(256f, 220f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, Color.black);

                if (texture == null)
                {
                    GUI.Label(rect, "No Preview", EditorStyles.centeredGreyMiniLabel);
                    return;
                }

                Rect fitted = FitRect(rect, texture.width, texture.height);
                EditorGUI.DrawPreviewTexture(fitted, texture, null, ScaleMode.ScaleToFit);
            }
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

        private void GenerateAndSave()
        {
            List<Texture2D> validInputs = inputTextures.Where(texture => texture != null).ToList();
            if (validInputs.Count == 0)
            {
                SetStatus("Add at least one input texture.", MessageType.Error);
                return;
            }

            if (!AssetDatabase.IsValidFolder(ExportPath))
            {
                SetStatus("The root Assets folder could not be found.", MessageType.Error);
                return;
            }

            try
            {
                SdfResult result = GenerateSdf(validInputs);
                ReplacePreview(ref mainPreview, result.Main);
                ReplacePreview(ref flippedPreview, createFlippedCopy ? FlipHorizontal(result.Main) : null);
                ReplacePreview(ref maskPreview, createMask ? result.Mask : null);

                string suffix = GetAvailableOutputSuffix(ExportPath, createFlippedCopy, createMask);
                string mainFileName = BuildOutputFileName(MainFileBaseName, suffix);
                SaveTexture(mainPreview, ExportPath, mainFileName);

                if (createFlippedCopy && flippedPreview != null)
                {
                    SaveTexture(flippedPreview, ExportPath, BuildOutputFileName(FlippedFileBaseName, suffix));
                }

                if (createMask && maskPreview != null)
                {
                    SaveTexture(maskPreview, ExportPath, BuildOutputFileName(MaskFileBaseName, suffix));
                }

                AssetDatabase.Refresh();
                SetStatus($"Saved {mainFileName} to {ExportPath}.", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private SdfResult GenerateSdf(IReadOnlyList<Texture2D> textures)
        {
            int width = textures[0].width;
            int height = textures[0].height;
            int pixelCount = width * height;
            int layerCount = textures.Count;

            float[] values = new float[pixelCount];
            float[] mask = new float[pixelCount];

            for (int layer = 0; layer < layerCount; layer++)
            {
                Color[] pixels = ReadTexturePixels(textures[layer], width, height);
                float layerValue = layerCount <= 1 ? 1f : (float)layer / (layerCount - 1);

                for (int i = 0; i < pixelCount; i++)
                {
                    float sample = GetMaskSample(pixels[i], useAlpha);
                    if (sample < maskThreshold)
                    {
                        continue;
                    }

                    values[i] += layerValue;
                    mask[i] += 1f;
                }
            }

            for (int i = 0; i < pixelCount; i++)
            {
                if (mask[i] <= 0f)
                {
                    values[i] = 0f;
                    continue;
                }

                values[i] /= mask[i];
                mask[i] = 1f;
            }

            if (blendIterations > 0)
            {
                values = BlurInsideMask(values, mask, width, height, blendIterations);
            }

            Texture2D main = CreateGrayTexture(width, height, values);
            Texture2D maskTexture = CreateGrayTexture(width, height, mask);
            return new SdfResult(main, maskTexture);
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
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                DestroyImmediate(readable);
            }
        }

        private static float GetMaskSample(Color pixel, bool useTransparentAlpha)
        {
            if (!useTransparentAlpha)
            {
                return pixel.grayscale;
            }

            return pixel.a < 0.999f ? Mathf.Max(pixel.a, pixel.grayscale) : pixel.grayscale;
        }

        private static float[] BlurInsideMask(float[] source, float[] mask, int width, int height, int iterations)
        {
            float[] current = (float[])source.Clone();
            float[] next = new float[source.Length];

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Array.Copy(current, next, current.Length);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        if (mask[index] <= 0f)
                        {
                            next[index] = 0f;
                            continue;
                        }

                        float total = 0f;
                        int count = 0;

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int ny = y + oy;
                            if (ny < 0 || ny >= height)
                            {
                                continue;
                            }

                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int nx = x + ox;
                                if (nx < 0 || nx >= width)
                                {
                                    continue;
                                }

                                int sampleIndex = ny * width + nx;
                                if (mask[sampleIndex] <= 0f)
                                {
                                    continue;
                                }

                                total += current[sampleIndex];
                                count++;
                            }
                        }

                        next[index] = count > 0 ? total / count : current[index];
                    }
                }

                float[] swap = current;
                current = next;
                next = swap;
            }

            return current;
        }

        private static Texture2D CreateGrayTexture(int width, int height, float[] values)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[values.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                float value = Mathf.Clamp01(values[i]);
                pixels[i] = new Color(value, value, value, 1f);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D FlipHorizontal(Texture2D source)
        {
            Texture2D flipped = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);

            for (int y = 0; y < source.height; y++)
            {
                for (int x = 0; x < source.width; x++)
                {
                    flipped.SetPixel(source.width - 1 - x, y, source.GetPixel(x, y));
                }
            }

            flipped.Apply(false, false);
            return flipped;
        }

        private static void SaveTexture(Texture2D texture, string folderPath, string fileName)
        {
            string path = Path.Combine(folderPath, fileName).Replace("\\", "/");
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        private static string GetAvailableOutputSuffix(string folderPath, bool includeFlipped, bool includeMask)
        {
            if (IsOutputNameAvailable(folderPath, string.Empty, includeFlipped, includeMask))
            {
                return string.Empty;
            }

            for (int index = 1; index < 10000; index++)
            {
                string suffix = $"_{index:000}";
                if (IsOutputNameAvailable(folderPath, suffix, includeFlipped, includeMask))
                {
                    return suffix;
                }
            }

            throw new IOException("Could not find an available SDF output file name.");
        }

        private static bool IsOutputNameAvailable(string folderPath, string suffix, bool includeFlipped, bool includeMask)
        {
            if (File.Exists(Path.Combine(folderPath, BuildOutputFileName(MainFileBaseName, suffix))))
            {
                return false;
            }

            if (includeFlipped && File.Exists(Path.Combine(folderPath, BuildOutputFileName(FlippedFileBaseName, suffix))))
            {
                return false;
            }

            if (includeMask && File.Exists(Path.Combine(folderPath, BuildOutputFileName(MaskFileBaseName, suffix))))
            {
                return false;
            }

            return true;
        }

        private static string BuildOutputFileName(string baseName, string suffix)
        {
            return $"{baseName}{suffix}{PngExtension}";
        }

        private void SortInputsByName()
        {
            inputTextures.Sort((left, right) =>
            {
                if (left == null && right == null)
                {
                    return 0;
                }

                if (left == null)
                {
                    return 1;
                }

                if (right == null)
                {
                    return -1;
                }

                return NaturalCompare(left.name, right.name);
            });
        }

        private static int NaturalCompare(string left, string right)
        {
            int leftIndex = 0;
            int rightIndex = 0;

            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                char leftChar = left[leftIndex];
                char rightChar = right[rightIndex];

                if (char.IsDigit(leftChar) && char.IsDigit(rightChar))
                {
                    long leftNumber = ReadNumber(left, ref leftIndex);
                    long rightNumber = ReadNumber(right, ref rightIndex);
                    int numberCompare = leftNumber.CompareTo(rightNumber);
                    if (numberCompare != 0)
                    {
                        return numberCompare;
                    }

                    continue;
                }

                int charCompare = char.ToUpperInvariant(leftChar).CompareTo(char.ToUpperInvariant(rightChar));
                if (charCompare != 0)
                {
                    return charCompare;
                }

                leftIndex++;
                rightIndex++;
            }

            return left.Length.CompareTo(right.Length);
        }

        private static long ReadNumber(string value, ref int index)
        {
            long number = 0;
            while (index < value.Length && char.IsDigit(value[index]))
            {
                number = number * 10 + value[index] - '0';
                index++;
            }

            return number;
        }

        private void Swap(int first, int second)
        {
            Texture2D texture = inputTextures[first];
            inputTextures[first] = inputTextures[second];
            inputTextures[second] = texture;
        }

        private void ReplacePreview(ref Texture2D target, Texture2D replacement)
        {
            if (target != null)
            {
                DestroyImmediate(target);
            }

            target = replacement;
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private readonly struct SdfResult
        {
            public SdfResult(Texture2D main, Texture2D mask)
            {
                Main = main;
                Mask = mask;
            }

            public Texture2D Main { get; }
            public Texture2D Mask { get; }
        }
    }
}
