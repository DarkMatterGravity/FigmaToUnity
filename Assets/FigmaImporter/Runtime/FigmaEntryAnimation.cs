using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaImporter
{
    public enum EntryAnimationType
    {
        None,
        FadeIn,
        PopIn,
        SlideFromLeft,
        SlideFromRight,
        SlideFromTop,
        SlideFromBottom,
        FadeSlideUp,
        FadeSlideDown,
        FadeSlideLeft,
        FadeSlideRight,
        FadePopIn
    }

    /// <summary>
    /// Entry animation component for UI elements.
    /// Automatically added by the importer based on layer prefixes (BTN_, IMG_, CTN_).
    /// </summary>
    [AddComponentMenu("Figma/Entry Animation")]
    public class FigmaEntryAnimation : MonoBehaviour
    {
        [Header("Animation Settings")]
        public EntryAnimationType animationType = EntryAnimationType.FadeIn;
        public float duration = 0.4f;
        public float delay = 0f;

        [Header("Easing")]
        [Tooltip("Custom easing curve. Default is ease-out.")]
        public AnimationCurve easingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Pop Settings")]
        [Tooltip("How much to overshoot scale (1.0 = no overshoot, 1.2 = 20% overshoot)")]
        [Range(1f, 1.5f)]
        public float popOvershoot = 1.15f;
        [Tooltip("How quickly the spring settles")]
        public float springDamping = 0.7f;

        [Header("Slide Settings")]
        [Tooltip("Distance to slide in pixels")]
        public float slideDistance = 100f;

        [Header("Playback")]
        public bool playOnStart = true;
        public bool playOnEnable = false;

        // Cached components
        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private Graphic[] graphics;

        // Original state
        private Vector2 originalAnchoredPosition;
        private Vector3 originalScale;
        private Vector2 originalPivot;
        private float[] originalAlphas;
        private bool hasInitialized = false;

        private void Awake()
        {
            CacheComponents();
            SaveOriginalState();
        }

        private void Start()
        {
            if (playOnStart && animationType != EntryAnimationType.None)
            {
                SetInitialState();
                PlayAnimation();
            }
        }

        private void OnEnable()
        {
            if (playOnEnable && hasInitialized && animationType != EntryAnimationType.None)
            {
                SetInitialState();
                PlayAnimation();
            }
        }

        private void CacheComponents()
        {
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
            graphics = GetComponentsInChildren<Graphic>(true);
        }

        private void SaveOriginalState()
        {
            if (rectTransform != null)
            {
                originalAnchoredPosition = rectTransform.anchoredPosition;
                originalScale = rectTransform.localScale;
                originalPivot = rectTransform.pivot;
            }

            // Save original alphas for all graphics
            originalAlphas = new float[graphics.Length];
            for (int i = 0; i < graphics.Length; i++)
            {
                originalAlphas[i] = graphics[i].color.a;
            }

            hasInitialized = true;
        }

        /// <summary>
        /// Sets pivot to center without moving the visual position of the element.
        /// Required for PopIn animations to scale from center.
        /// </summary>
        private void SetPivotToCenter()
        {
            if (rectTransform == null) return;

            Vector2 targetPivot = new Vector2(0.5f, 0.5f);
            Vector2 deltaPivot = targetPivot - rectTransform.pivot;

            // Adjust anchored position to compensate for pivot change
            Vector2 size = rectTransform.rect.size;
            Vector2 deltaPosition = new Vector2(deltaPivot.x * size.x, deltaPivot.y * size.y);

            rectTransform.pivot = targetPivot;
            rectTransform.anchoredPosition += deltaPosition;

            // Update our "original" position to the new compensated position
            originalAnchoredPosition = rectTransform.anchoredPosition;
        }

        private void SetInitialState()
        {
            if (rectTransform == null) return;

            switch (animationType)
            {
                case EntryAnimationType.FadeIn:
                    SetAlpha(0f);
                    break;

                case EntryAnimationType.PopIn:
                    SetPivotToCenter();
                    rectTransform.localScale = Vector3.zero;
                    break;

                case EntryAnimationType.SlideFromLeft:
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.left * slideDistance;
                    break;

                case EntryAnimationType.SlideFromRight:
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.right * slideDistance;
                    break;

                case EntryAnimationType.SlideFromTop:
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.up * slideDistance;
                    break;

                case EntryAnimationType.SlideFromBottom:
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.down * slideDistance;
                    break;

                case EntryAnimationType.FadeSlideUp:
                    SetAlpha(0f);
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.down * slideDistance;
                    break;

                case EntryAnimationType.FadeSlideDown:
                    SetAlpha(0f);
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.up * slideDistance;
                    break;

                case EntryAnimationType.FadeSlideLeft:
                    SetAlpha(0f);
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.right * slideDistance;
                    break;

                case EntryAnimationType.FadeSlideRight:
                    SetAlpha(0f);
                    rectTransform.anchoredPosition = originalAnchoredPosition + Vector2.left * slideDistance;
                    break;

                case EntryAnimationType.FadePopIn:
                    SetPivotToCenter();
                    SetAlpha(0f);
                    rectTransform.localScale = Vector3.zero;
                    break;
            }
        }

        public void PlayAnimation()
        {
            StopAllCoroutines();
            StartCoroutine(AnimateEntry());
        }

        public void ResetToOriginal()
        {
            StopAllCoroutines();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = originalAnchoredPosition;
                rectTransform.localScale = originalScale;
            }
            RestoreOriginalAlphas();
        }

        private IEnumerator AnimateEntry()
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = easingCurve.Evaluate(t);

                ApplyAnimation(easedT);

                yield return null;
            }

            // Ensure we end at exactly the target state
            ApplyAnimation(1f);
        }

        private void ApplyAnimation(float t)
        {
            if (rectTransform == null) return;

            switch (animationType)
            {
                case EntryAnimationType.FadeIn:
                    SetAlphaLerp(t);
                    break;

                case EntryAnimationType.PopIn:
                    float springScale = CalculateSpringValue(t, popOvershoot, springDamping);
                    rectTransform.localScale = originalScale * springScale;
                    break;

                case EntryAnimationType.SlideFromLeft:
                case EntryAnimationType.SlideFromRight:
                case EntryAnimationType.SlideFromTop:
                case EntryAnimationType.SlideFromBottom:
                    Vector2 startPos = GetSlideStartPosition();
                    rectTransform.anchoredPosition = Vector2.Lerp(startPos, originalAnchoredPosition, t);
                    break;

                case EntryAnimationType.FadeSlideUp:
                case EntryAnimationType.FadeSlideDown:
                case EntryAnimationType.FadeSlideLeft:
                case EntryAnimationType.FadeSlideRight:
                    SetAlphaLerp(t);
                    Vector2 fadeSlideStart = GetSlideStartPosition();
                    rectTransform.anchoredPosition = Vector2.Lerp(fadeSlideStart, originalAnchoredPosition, t);
                    break;

                case EntryAnimationType.FadePopIn:
                    SetAlphaLerp(t);
                    float fadePopScale = CalculateSpringValue(t, popOvershoot, springDamping);
                    rectTransform.localScale = originalScale * fadePopScale;
                    break;
            }
        }

        private Vector2 GetSlideStartPosition()
        {
            switch (animationType)
            {
                case EntryAnimationType.SlideFromLeft:
                case EntryAnimationType.FadeSlideRight:
                    return originalAnchoredPosition + Vector2.left * slideDistance;
                case EntryAnimationType.SlideFromRight:
                case EntryAnimationType.FadeSlideLeft:
                    return originalAnchoredPosition + Vector2.right * slideDistance;
                case EntryAnimationType.SlideFromTop:
                case EntryAnimationType.FadeSlideDown:
                    return originalAnchoredPosition + Vector2.up * slideDistance;
                case EntryAnimationType.SlideFromBottom:
                case EntryAnimationType.FadeSlideUp:
                    return originalAnchoredPosition + Vector2.down * slideDistance;
                default:
                    return originalAnchoredPosition;
            }
        }

        /// <summary>
        /// Spring-like overshoot and settle animation.
        /// t goes 0→1, output goes 0 → overshoot → settles to 1
        /// </summary>
        private float CalculateSpringValue(float t, float overshoot, float damping)
        {
            if (t >= 1f) return 1f;
            if (t <= 0f) return 0f;

            // Simple spring approximation using damped sine
            float omega = Mathf.PI * 2f;
            float decay = Mathf.Exp(-damping * t * 5f);
            float oscillation = Mathf.Sin(t * omega * 0.5f) * (overshoot - 1f) * decay;

            // Base progress with ease-out
            float baseProgress = 1f - Mathf.Pow(1f - t, 3f);

            return baseProgress + oscillation;
        }

        private void SetAlpha(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
            else
            {
                for (int i = 0; i < graphics.Length; i++)
                {
                    var color = graphics[i].color;
                    color.a = alpha * (originalAlphas[i] > 0 ? 1f : 0f);
                    graphics[i].color = color;
                }
            }
        }

        private void SetAlphaLerp(float t)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = t;
            }
            else
            {
                for (int i = 0; i < graphics.Length; i++)
                {
                    var color = graphics[i].color;
                    color.a = Mathf.Lerp(0f, originalAlphas[i], t);
                    graphics[i].color = color;
                }
            }
        }

        private void RestoreOriginalAlphas()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }
            else
            {
                for (int i = 0; i < graphics.Length; i++)
                {
                    var color = graphics[i].color;
                    color.a = originalAlphas[i];
                    graphics[i].color = color;
                }
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Preview Animation")]
        private void PreviewAnimation()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("Preview only works in Play mode");
                return;
            }
            ResetToOriginal();
            SetInitialState();
            PlayAnimation();
        }

        [ContextMenu("Reset to Original")]
        private void EditorResetToOriginal()
        {
            CacheComponents();
            SaveOriginalState();
            ResetToOriginal();
        }
#endif
    }
}
