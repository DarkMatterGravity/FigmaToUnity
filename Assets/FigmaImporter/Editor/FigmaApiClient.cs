#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FigmaImporter.Editor
{
    /// <summary>
    /// Client for the Figma REST API.
    /// Fetches file data and exports images directly from Figma.
    /// </summary>
    public static class FigmaApiClient
    {
        private const string API_BASE = "https://api.figma.com/v1";
        private const string PREF_API_TOKEN = "FigmaImporter_ApiToken";

        /// <summary>
        /// Gets or sets the Figma API token.
        /// Stored in EditorPrefs for persistence.
        /// </summary>
        public static string ApiToken
        {
            get => EditorPrefs.GetString(PREF_API_TOKEN, "");
            set => EditorPrefs.SetString(PREF_API_TOKEN, value);
        }

        /// <summary>
        /// Checks if an API token is configured.
        /// </summary>
        public static bool HasToken => !string.IsNullOrEmpty(ApiToken);

        /// <summary>
        /// Extracts the file key from a Figma URL.
        /// Supports URLs like:
        /// - https://www.figma.com/file/ABC123/FileName
        /// - https://www.figma.com/design/ABC123/FileName
        /// </summary>
        public static string ExtractFileKey(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Match patterns: /file/KEY/ or /design/KEY/
            var patterns = new[] { "/file/", "/design/" };
            foreach (var pattern in patterns)
            {
                int idx = url.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int start = idx + pattern.Length;
                    int end = url.IndexOf('/', start);
                    if (end < 0) end = url.IndexOf('?', start);
                    if (end < 0) end = url.Length;
                    return url.Substring(start, end - start);
                }
            }

            // If no pattern found, assume the input is already a file key
            if (!url.Contains("/") && !url.Contains("."))
                return url;

            return null;
        }

        /// <summary>
        /// Extracts the node ID from a Figma URL (node-id parameter).
        /// </summary>
        public static string ExtractNodeId(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Look for node-id= in query string
            int idx = url.IndexOf("node-id=", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int start = idx + 8;
                int end = url.IndexOf('&', start);
                if (end < 0) end = url.Length;
                var nodeId = url.Substring(start, end - start);
                // URL decode (replace %3A with :, etc.)
                return Uri.UnescapeDataString(nodeId);
            }

            return null;
        }

        /// <summary>
        /// Fetches the complete file JSON from Figma.
        /// </summary>
        public static FigmaFileResponse GetFile(string fileKey, string[] nodeIds = null)
        {
            if (!HasToken)
                throw new Exception("Figma API token not configured. Set it in the importer window.");

            var url = $"{API_BASE}/files/{fileKey}";
            if (nodeIds != null && nodeIds.Length > 0)
            {
                url += "?ids=" + string.Join(",", nodeIds);
            }

            var json = MakeRequest(url);
            return JsonUtility.FromJson<FigmaFileResponse>(json);
        }

        /// <summary>
        /// Fetches specific nodes from a Figma file.
        /// </summary>
        public static FigmaNodesResponse GetNodes(string fileKey, string[] nodeIds)
        {
            if (!HasToken)
                throw new Exception("Figma API token not configured.");

            if (nodeIds == null || nodeIds.Length == 0)
                throw new ArgumentException("nodeIds cannot be empty");

            var url = $"{API_BASE}/files/{fileKey}/nodes?ids=" + string.Join(",", nodeIds);
            var json = MakeRequest(url);
            return JsonUtility.FromJson<FigmaNodesResponse>(json);
        }

        /// <summary>
        /// Exports images for specific nodes.
        /// Returns a dictionary mapping node IDs to image URLs.
        /// </summary>
        public static Dictionary<string, string> GetImages(
            string fileKey,
            string[] nodeIds,
            float scale = 2f,
            string format = "png")
        {
            if (!HasToken)
                throw new Exception("Figma API token not configured.");

            if (nodeIds == null || nodeIds.Length == 0)
                return new Dictionary<string, string>();

            var url = $"{API_BASE}/images/{fileKey}?ids={string.Join(",", nodeIds)}&scale={scale}&format={format}";
            var json = MakeRequest(url);
            var response = JsonUtility.FromJson<FigmaImagesResponse>(json);

            var result = new Dictionary<string, string>();
            if (response.images != null)
            {
                // Unity's JsonUtility doesn't handle dictionaries well,
                // so we need to parse manually
                var parsed = ParseImageUrls(json);
                foreach (var kvp in parsed)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Downloads an image from a URL to a local file.
        /// </summary>
        public static bool DownloadImage(string imageUrl, string localPath)
        {
            try
            {
                using (var request = UnityWebRequest.Get(imageUrl))
                {
                    request.SendWebRequest();
                    while (!request.isDone)
                    {
                        System.Threading.Thread.Sleep(10);
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"Failed to download image: {request.error}");
                        return false;
                    }

                    var dir = Path.GetDirectoryName(localPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllBytes(localPath, request.downloadHandler.data);
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to download image: {e.Message}");
                return false;
            }
        }

        private static string MakeRequest(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-Figma-Token", ApiToken);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    System.Threading.Thread.Sleep(10);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Figma API error: {request.error}\n{request.downloadHandler.text}");
                }

                return request.downloadHandler.text;
            }
        }

        private static Dictionary<string, string> ParseImageUrls(string json)
        {
            var result = new Dictionary<string, string>();

            // Simple JSON parsing for the images object
            int imagesStart = json.IndexOf("\"images\"");
            if (imagesStart < 0) return result;

            int braceStart = json.IndexOf('{', imagesStart);
            if (braceStart < 0) return result;

            int braceEnd = json.IndexOf('}', braceStart);
            if (braceEnd < 0) return result;

            var imagesJson = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
            var pairs = imagesJson.Split(',');

            foreach (var pair in pairs)
            {
                var parts = pair.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim().Trim('"');
                    var value = parts[1].Trim().Trim('"');
                    if (!string.IsNullOrEmpty(key) && value != "null")
                    {
                        result[key] = value;
                    }
                }
            }

            return result;
        }
    }

    // ---- API Response DTOs ----

    [Serializable]
    public class FigmaFileResponse
    {
        public string name;
        public string lastModified;
        public string thumbnailUrl;
        public string version;
        public FigmaDocument document;
    }

    [Serializable]
    public class FigmaNodesResponse
    {
        public string name;
        public string lastModified;
        // nodes is a dictionary but Unity can't serialize that directly
    }

    [Serializable]
    public class FigmaImagesResponse
    {
        public string err;
        public object images; // Dictionary<string, string> - parsed manually
    }

    [Serializable]
    public class FigmaDocument
    {
        public string id;
        public string name;
        public string type;
        public FigmaNode[] children;
    }

    [Serializable]
    public class FigmaNode
    {
        public string id;
        public string name;
        public string type;
        public bool visible;
        public FigmaNode[] children;

        // Layout properties
        public FigmaRectangle absoluteBoundingBox;
        public FigmaRectangle absoluteRenderBounds;
        public FigmaConstraints constraints;
        public string layoutMode;
        public string primaryAxisAlignItems;
        public string counterAxisAlignItems;
        public float paddingLeft;
        public float paddingRight;
        public float paddingTop;
        public float paddingBottom;
        public float itemSpacing;

        // Style properties
        public float opacity;
        public FigmaColor backgroundColor;
        public FigmaPaint[] fills;
        public FigmaPaint[] strokes;
        public float strokeWeight;

        // Text properties
        public string characters;
        public FigmaTypeStyle style;

        // Component properties
        public string componentId;
        public string componentSetId;
    }

    [Serializable]
    public class FigmaRectangle
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    public class FigmaConstraints
    {
        public string vertical;
        public string horizontal;
    }

    [Serializable]
    public class FigmaColor
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [Serializable]
    public class FigmaPaint
    {
        public string type;
        public bool visible;
        public float opacity;
        public FigmaColor color;
    }

    [Serializable]
    public class FigmaTypeStyle
    {
        public string fontFamily;
        public string fontPostScriptName;
        public float fontWeight;
        public float fontSize;
        public string textAlignHorizontal;
        public string textAlignVertical;
        public float letterSpacing;
        public float lineHeightPx;
    }
}
#endif
