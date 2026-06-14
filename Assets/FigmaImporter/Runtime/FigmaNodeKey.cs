using UnityEngine;

namespace FigmaImporter
{
    /// <summary>
    /// Marker component that stores the Figma node ID.
    /// Used by the importer to track nodes across re-imports.
    /// </summary>
    [AddComponentMenu("Figma/Node Key")]
    public class FigmaNodeKey : MonoBehaviour
    {
        [Tooltip("The Figma node ID this GameObject corresponds to")]
        public string Id;
    }
}
