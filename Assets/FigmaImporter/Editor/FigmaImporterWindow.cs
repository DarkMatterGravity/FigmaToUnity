#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    /// <summary>
    /// Main editor window for importing Figma designs into Unity.
    /// Supports both JSON file import and direct Figma URL import via REST API.
    /// </summary>
    public class FigmaImporterWindow : EditorWindow
    {
        private enum ImportMode
        {
            JsonFile,
            FigmaUrl
        }

        // Import mode
        private ImportMode importMode = ImportMode.JsonFile;

        // JSON mode
        private TextAsset screenJson;

        // URL mode
        private string figmaUrl = "";
        private string apiToken = "";
        private bool showToken = false;

        // Common settings
        private float pixelsPerUnit = 100f;
        private bool removeOrphans = true;
        private bool createCanvasIfMissing = true;
        private bool createEventSystemIfMissing = true;
        private bool autoSetupScreenManager = true;
        private bool createEnvironmentPrefab = false;
        private bool createTransitionPrefab = false;
        private bool createVideoPlane = false;
        private bool addEntryAnimations = true;
        private float animationStaggerDelay = 0.05f;

        // Auto-reimport UI state
        private bool showAutoReimportSettings = false;
        private Vector2 watchedFoldersScroll;
        private string newWatchedFolder = "Assets/";

        // Scroll position for the whole window
        private Vector2 scrollPosition;

        [MenuItem("Tools/Figma/Import to Unity...")]
        public static void Open() => GetWindow<FigmaImporterWindow>("Figma Importer");

        private void OnEnable()
        {
            apiToken = FigmaApiClient.ApiToken;
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Figma to Unity Importer", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // Import Mode Selection
            importMode = (ImportMode)EditorGUILayout.EnumPopup("Import Mode", importMode);
            EditorGUILayout.Space(5);

            if (importMode == ImportMode.JsonFile)
            {
                DrawJsonImportMode();
            }
            else
            {
                DrawUrlImportMode();
            }

            EditorGUILayout.Space(10);
            DrawCommonSettings();

            EditorGUILayout.Space(10);
            DrawImportButton();

            EditorGUILayout.Space(10);
            DrawHelpBox();

            EditorGUILayout.Space(10);
            DrawAutoReimportSettings();

            EditorGUILayout.EndScrollView();
        }

        private void DrawJsonImportMode()
        {
            EditorGUILayout.HelpBox(
                "Import from a JSON file exported by the Figma plugin.",
                MessageType.Info);

            screenJson = (TextAsset)EditorGUILayout.ObjectField(
                "Screen JSON",
                screenJson,
                typeof(TextAsset),
                false);
        }

        private void DrawUrlImportMode()
        {
            EditorGUILayout.HelpBox(
                "Import directly from a Figma URL using the REST API.\n" +
                "Requires a Figma API token (get one at figma.com/developers).",
                MessageType.Info);

            // API Token
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Token", GUILayout.Width(80));
            if (showToken)
            {
                apiToken = EditorGUILayout.TextField(apiToken);
            }
            else
            {
                apiToken = EditorGUILayout.PasswordField(apiToken);
            }
            if (GUILayout.Button(showToken ? "Hide" : "Show", GUILayout.Width(50)))
            {
                showToken = !showToken;
            }
            EditorGUILayout.EndHorizontal();

            if (apiToken != FigmaApiClient.ApiToken)
            {
                FigmaApiClient.ApiToken = apiToken;
            }

            // Figma URL
            EditorGUILayout.LabelField("Figma URL");
            figmaUrl = EditorGUILayout.TextField(figmaUrl);

            // Parse and show what we found
            var fileKey = FigmaApiClient.ExtractFileKey(figmaUrl);
            var nodeId = FigmaApiClient.ExtractNodeId(figmaUrl);

            if (!string.IsNullOrEmpty(figmaUrl))
            {
                var status = "";
                if (!string.IsNullOrEmpty(fileKey))
                {
                    status = $"File: {fileKey}";
                    if (!string.IsNullOrEmpty(nodeId))
                        status += $" | Node: {nodeId}";
                }
                else
                {
                    status = "Could not parse Figma URL";
                }
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            }
        }

        private void DrawCommonSettings()
        {
            EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);

            pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);
            removeOrphans = EditorGUILayout.ToggleLeft("Remove orphaned nodes", removeOrphans);
            createCanvasIfMissing = EditorGUILayout.ToggleLeft("Create Canvas if missing", createCanvasIfMissing);
            createEventSystemIfMissing = EditorGUILayout.ToggleLeft("Create EventSystem if missing", createEventSystemIfMissing);
            autoSetupScreenManager = EditorGUILayout.ToggleLeft("Auto-setup UIScreenManager + Resources/Screens", autoSetupScreenManager);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Optional Prefabs", EditorStyles.boldLabel);
            createEnvironmentPrefab = EditorGUILayout.ToggleLeft("Create 3D Environment Prefab", createEnvironmentPrefab);
            createTransitionPrefab = EditorGUILayout.ToggleLeft("Create Screen Transition Prefab", createTransitionPrefab);
            createVideoPlane = EditorGUILayout.ToggleLeft("Create Video Plane (for AI video)", createVideoPlane);

            EditorGUILayout.Space(5);
            addEntryAnimations = EditorGUILayout.ToggleLeft("Add Entry Animations (BTN_, IMG_, CTN_)", addEntryAnimations);
            if (addEntryAnimations)
            {
                EditorGUI.indentLevel++;
                animationStaggerDelay = EditorGUILayout.FloatField("Stagger Delay (per sibling)", animationStaggerDelay);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawImportButton()
        {
            bool canImport = false;
            if (importMode == ImportMode.JsonFile)
            {
                canImport = screenJson != null;
            }
            else
            {
                var fileKey = FigmaApiClient.ExtractFileKey(figmaUrl);
                canImport = !string.IsNullOrEmpty(apiToken) && !string.IsNullOrEmpty(fileKey);
            }

            using (new EditorGUI.DisabledScope(!canImport))
            {
                if (GUILayout.Button("Import / Rebuild Prefab", GUILayout.Height(30)))
                {
                    if (importMode == ImportMode.JsonFile)
                    {
                        ImportFromJson();
                    }
                    else
                    {
                        ImportFromUrl();
                    }
                }
            }
        }

        private void DrawHelpBox()
        {
            EditorGUILayout.HelpBox(
                "Layer Prefixes:\n" +
                "  BTN_ = Button (baked image + click handling)\n" +
                "  IMG_ = Image (baked as PNG)\n" +
                "  CTN_ = Container only (children imported)\n" +
                "  9SLICE_ = 9-slice scalable image\n\n" +
                "Features:\n" +
                "  - Auto-layout (row/column) → LayoutGroups\n" +
                "  - Constraints → Anchor positioning\n" +
                "  - Component variants → FigmaVariantController\n" +
                "  - Prototype navigation → Auto-wired buttons\n\n" +
                "Re-import Safety:\n" +
                "  - Preserves onClick handlers and custom scripts\n" +
                "  - Updates layout, images, and structure only",
                MessageType.None);
        }

        private void DrawAutoReimportSettings()
        {
            showAutoReimportSettings = EditorGUILayout.Foldout(
                showAutoReimportSettings,
                "Auto-Reimport Settings",
                true,
                EditorStyles.foldoutHeader);

            if (!showAutoReimportSettings)
                return;

            EditorGUI.indentLevel++;

            var autoEnabled = FigmaAutoReimporter.IsEnabled();
            var newAutoEnabled = EditorGUILayout.Toggle("Auto-Reimport Enabled", autoEnabled);
            if (newAutoEnabled != autoEnabled)
                FigmaAutoReimporter.SetEnabled(newAutoEnabled);

            var autoPpu = FigmaAutoReimporter.GetPPU();
            var newAutoPpu = EditorGUILayout.FloatField("Auto-Reimport PPU", autoPpu);
            if (Math.Abs(newAutoPpu - autoPpu) > 0.01f)
                FigmaAutoReimporter.SetPPU(newAutoPpu);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Watched Folders", EditorStyles.boldLabel);

            // Add folder row
            EditorGUILayout.BeginHorizontal();
            newWatchedFolder = EditorGUILayout.TextField(newWatchedFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var selected = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    var dataPath = Application.dataPath;
                    if (selected.StartsWith(dataPath))
                        newWatchedFolder = "Assets" + selected.Substring(dataPath.Length);
                    else
                        newWatchedFolder = selected;
                }
            }
            if (GUILayout.Button("Add", GUILayout.Width(40)))
            {
                if (!string.IsNullOrEmpty(newWatchedFolder))
                {
                    FigmaAutoReimporter.AddWatchedFolder(newWatchedFolder.TrimEnd('/'));
                    newWatchedFolder = "Assets/";
                }
            }
            EditorGUILayout.EndHorizontal();

            // List watched folders
            var folders = FigmaAutoReimporter.GetWatchedFolders();
            if (folders.Length == 0)
            {
                EditorGUILayout.LabelField("No folders being watched.", EditorStyles.miniLabel);
            }
            else
            {
                watchedFoldersScroll = EditorGUILayout.BeginScrollView(watchedFoldersScroll, GUILayout.Height(60));
                string toRemove = null;
                foreach (var folder in folders)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(folder);
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                        toRemove = folder;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                if (toRemove != null)
                    FigmaAutoReimporter.RemoveWatchedFolder(toRemove);
            }

            EditorGUI.indentLevel--;
        }

        private void ImportFromJson()
        {
            if (screenJson == null)
            {
                Debug.LogError("Assign screen.json first.");
                return;
            }

            try
            {
                var data = JsonUtility.FromJson<ExportBundle>(screenJson.text);
                if (data == null || data.nodes == null)
                {
                    Debug.LogError("Invalid screen.json format");
                    return;
                }

                var jsonPath = AssetDatabase.GetAssetPath(screenJson);
                var rootFolder = Path.GetDirectoryName(jsonPath).Replace("\\", "/");

                var builder = new FigmaNodeBuilder
                {
                    PixelsPerUnit = pixelsPerUnit,
                    RemoveOrphans = removeOrphans,
                    CreateCanvasIfMissing = createCanvasIfMissing,
                    CreateEventSystemIfMissing = createEventSystemIfMissing,
                    AutoSetupScreenManager = autoSetupScreenManager,
                    CreateEnvironmentPrefab = createEnvironmentPrefab,
                    CreateTransitionPrefab = createTransitionPrefab,
                    CreateVideoPlane = createVideoPlane,
                    AddEntryAnimations = addEntryAnimations,
                    AnimationStaggerDelay = animationStaggerDelay
                };

                builder.Import(data, rootFolder);

                Debug.Log($"Figma Import: Successfully imported {data.frameName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Figma Import failed: {e.Message}\n{e.StackTrace}");
            }
        }

        private void ImportFromUrl()
        {
            var fileKey = FigmaApiClient.ExtractFileKey(figmaUrl);
            var nodeId = FigmaApiClient.ExtractNodeId(figmaUrl);

            if (string.IsNullOrEmpty(fileKey))
            {
                Debug.LogError("Could not parse Figma file key from URL");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Figma Import", "Fetching file data...", 0.1f);

                // This is a placeholder - full implementation would:
                // 1. Fetch file structure with GetFile()
                // 2. Convert Figma nodes to ExportBundle format
                // 3. Export images with GetImages()
                // 4. Call builder.Import()

                Debug.Log($"Figma API: Would fetch file {fileKey}" +
                    (nodeId != null ? $" node {nodeId}" : ""));

                // For now, show a helpful message
                EditorUtility.DisplayDialog(
                    "Coming Soon",
                    "Direct URL import is in development.\n\n" +
                    "For now, use the Figma plugin to export JSON,\n" +
                    "then import that JSON file here.\n\n" +
                    $"File Key: {fileKey}\n" +
                    $"Node ID: {nodeId ?? "(all frames)"}",
                    "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"Figma API error: {e.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
