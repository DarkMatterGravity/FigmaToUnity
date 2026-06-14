#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    /// <summary>
    /// Automatically reimports Figma screens when their JSON or image files change.
    /// </summary>
    public class FigmaAutoReimporter : AssetPostprocessor
    {
        private const string PREF_ENABLED = "FigmaImporter_AutoReimport_Enabled";
        private const string PREF_PPU = "FigmaImporter_AutoReimport_PPU";
        private const string PREF_WATCHED_FOLDERS = "FigmaImporter_AutoReimport_WatchedFolders";

        private static HashSet<string> pendingJsonFiles = new HashSet<string>();
        private static double lastChangeTime;
        private const double DEBOUNCE_SECONDS = 0.5;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!IsEnabled()) return;

            var watchedFolders = GetWatchedFolders();
            if (watchedFolders.Length == 0) return;

            foreach (var assetPath in importedAssets)
            {
                if (!IsInWatchedFolder(assetPath, watchedFolders)) continue;

                if (assetPath.EndsWith("_Screen.json", StringComparison.OrdinalIgnoreCase))
                {
                    pendingJsonFiles.Add(assetPath);
                    lastChangeTime = EditorApplication.timeSinceStartup;
                }
                else if (assetPath.Contains("_Images/") &&
                         (assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                          assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                {
                    var jsonPath = FindJsonForImageFolder(assetPath);
                    if (!string.IsNullOrEmpty(jsonPath))
                    {
                        pendingJsonFiles.Add(jsonPath);
                        lastChangeTime = EditorApplication.timeSinceStartup;
                    }
                }
            }

            if (pendingJsonFiles.Count > 0)
            {
                EditorApplication.delayCall -= ProcessPendingImports;
                EditorApplication.delayCall += ProcessPendingImports;
            }
        }

        private static void ProcessPendingImports()
        {
            if (EditorApplication.timeSinceStartup - lastChangeTime < DEBOUNCE_SECONDS)
            {
                EditorApplication.delayCall -= ProcessPendingImports;
                EditorApplication.delayCall += ProcessPendingImports;
                return;
            }

            if (pendingJsonFiles.Count == 0) return;

            var filesToProcess = pendingJsonFiles.ToArray();
            pendingJsonFiles.Clear();

            var ppu = GetPPU();

            foreach (var jsonPath in filesToProcess)
            {
                try
                {
                    Debug.Log($"[FigmaAutoReimport] Reimporting: {jsonPath}");
                    ImportScreen(jsonPath, ppu);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FigmaAutoReimport] Failed: {e.Message}");
                }
            }
        }

        private static bool IsInWatchedFolder(string assetPath, string[] watchedFolders)
        {
            foreach (var folder in watchedFolders)
            {
                if (assetPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string FindJsonForImageFolder(string imagePath)
        {
            var dir = Path.GetDirectoryName(imagePath).Replace("\\", "/");
            if (!dir.EndsWith("_Images")) return null;

            var baseName = dir.Substring(0, dir.Length - "_Images".Length);
            var jsonPath = baseName + "_Screen.json";

            if (File.Exists(jsonPath))
                return jsonPath;

            return null;
        }

        /// <summary>
        /// Import a single screen JSON file.
        /// </summary>
        public static void ImportScreen(string jsonPath, float pixelsPerUnit = 100f)
        {
            var screenJson = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (screenJson == null)
            {
                Debug.LogError($"[FigmaAutoReimport] Could not load: {jsonPath}");
                return;
            }

            var data = JsonUtility.FromJson<ExportBundle>(screenJson.text);
            if (data == null || data.nodes == null)
            {
                Debug.LogError($"[FigmaAutoReimport] Invalid JSON: {jsonPath}");
                return;
            }

            var rootFolder = Path.GetDirectoryName(jsonPath).Replace("\\", "/");

            var builder = new FigmaNodeBuilder
            {
                PixelsPerUnit = pixelsPerUnit,
                RemoveOrphans = true,
                CreateCanvasIfMissing = true,
                CreateEventSystemIfMissing = true,
                AutoSetupScreenManager = true,
                AddEntryAnimations = true,
                AnimationStaggerDelay = 0.05f
            };

            builder.Import(data, rootFolder);
        }

        // ---- Settings ----

        public static bool IsEnabled()
        {
            return EditorPrefs.GetBool(PREF_ENABLED, true);
        }

        public static void SetEnabled(bool enabled)
        {
            EditorPrefs.SetBool(PREF_ENABLED, enabled);
        }

        public static float GetPPU()
        {
            return EditorPrefs.GetFloat(PREF_PPU, 100f);
        }

        public static void SetPPU(float ppu)
        {
            EditorPrefs.SetFloat(PREF_PPU, ppu);
        }

        public static string[] GetWatchedFolders()
        {
            var raw = EditorPrefs.GetString(PREF_WATCHED_FOLDERS, "");
            if (string.IsNullOrEmpty(raw)) return new string[0];
            return raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static void SetWatchedFolders(string[] folders)
        {
            EditorPrefs.SetString(PREF_WATCHED_FOLDERS, string.Join(";", folders));
        }

        public static void AddWatchedFolder(string folder)
        {
            var folders = GetWatchedFolders().ToList();
            if (!folders.Contains(folder))
            {
                folders.Add(folder);
                SetWatchedFolders(folders.ToArray());
            }
        }

        public static void RemoveWatchedFolder(string folder)
        {
            var folders = GetWatchedFolders().ToList();
            folders.Remove(folder);
            SetWatchedFolders(folders.ToArray());
        }
    }
}
#endif
