using UnityEngine;
using UnityEngine.UI;

namespace FigmaImporter
{
    /// <summary>
    /// Auto-wires button navigation based on Figma prototype interactions.
    /// Supports navigate, back, and URL actions.
    /// </summary>
    [AddComponentMenu("Figma/Navigation Button")]
    public class FigmaNavigationButton : MonoBehaviour
    {
        [Tooltip("Action type: navigate, back, or url")]
        public string action;

        [Tooltip("Target screen name (for navigate action)")]
        public string targetName;

        [Tooltip("URL to open (for url action)")]
        public string url;

        private void Start()
        {
            var btn = GetComponent<Button>();
            if (btn == null) return;

            if (action == "navigate" && !string.IsNullOrEmpty(targetName))
            {
                var t = targetName; // local copy for closure
                btn.onClick.AddListener(() => UIScreenManager.Show(t));
            }
            else if (action == "back")
            {
                btn.onClick.AddListener(() => UIScreenManager.Back());
            }
            else if (action == "url" && !string.IsNullOrEmpty(url))
            {
                var u = url; // local copy for closure
                btn.onClick.AddListener(() => Application.OpenURL(u));
            }
        }
    }
}
