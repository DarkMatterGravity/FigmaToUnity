using UnityEngine;
using UnityEngine.UI;

namespace FigmaImporter
{
    /// <summary>
    /// Simple fade to color transition. Creates a full-screen overlay that fades in/out.
    /// </summary>
    public class FadeTransition : ScreenTransition
    {
        [Header("Fade Settings")]
        public Color fadeColor = Color.black;

        [Tooltip("Only used if no Canvas exists on prefab")]
        public int sortOrder = 9999;

        [Header("References (auto-found if empty)")]
        [Tooltip("Drag your custom fade Image here, or leave empty to auto-find/create")]
        public Image fadeImage;

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private bool createdCanvas;
        private bool createdImage;

        private void Awake()
        {
            FindOrCreateComponents();
        }

        private void FindOrCreateComponents()
        {
            // 1. Look for existing Canvas on this GameObject or parents
            canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                // No canvas found - create one
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = sortOrder;
                createdCanvas = true;
            }

            // 2. Look for existing CanvasGroup
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // Start hidden
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            // 3. Look for fade Image (use reference, or find by tag, or find first Image child)
            if (fadeImage == null)
            {
                // Try to find by tag
                var tagged = transform.Find("FadeImage");
                if (tagged != null)
                    fadeImage = tagged.GetComponent<Image>();
            }

            if (fadeImage == null)
            {
                // Try to find any Image in children
                fadeImage = GetComponentInChildren<Image>();
            }

            if (fadeImage == null)
            {
                // No image found - create one
                var imageGO = new GameObject("FadeImage");
                imageGO.transform.SetParent(transform, false);

                fadeImage = imageGO.AddComponent<Image>();
                fadeImage.raycastTarget = false;

                // Stretch to fill
                var rect = fadeImage.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                createdImage = true;
            }

            // Apply fade color (only override if we created the image, or color is default white)
            if (createdImage || fadeImage.color == Color.white)
            {
                fadeImage.color = fadeColor;
            }
        }

        public override void OnTransitionStart()
        {
            canvasGroup.blocksRaycasts = true;
        }

        public override void OnTransitionComplete()
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }

        protected override void ApplyTransitionOut(float t)
        {
            canvasGroup.alpha = t;
        }

        protected override void ApplyTransitionIn(float t)
        {
            canvasGroup.alpha = 1f - t;
        }
    }
}
