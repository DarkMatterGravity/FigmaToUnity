using UnityEngine;

namespace FigmaImporter
{
    /// <summary>
    /// Marker component for 3D environment prefabs created by the Figma importer.
    /// Add your 3D scene content (models, lights, cameras) as children of this GameObject.
    /// The UIScreenManager will load/unload this prefab when navigating to the matching screen.
    /// </summary>
    [AddComponentMenu("Figma/Environment Marker")]
    public class FigmaEnvironmentMarker : MonoBehaviour
    {
    }
}
