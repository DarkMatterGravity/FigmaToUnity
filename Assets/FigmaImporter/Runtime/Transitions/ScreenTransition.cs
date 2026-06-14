using System;
using System.Collections;
using UnityEngine;

namespace FigmaImporter
{
    /// <summary>
    /// Base class for screen transitions. Extend this to create custom transitions.
    /// Transitions have two phases: TransitionOut (hide current) and TransitionIn (reveal new).
    /// </summary>
    public abstract class ScreenTransition : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Duration of the 'out' phase (hiding current screen)")]
        public float outDuration = 0.3f;

        [Tooltip("Duration of the 'in' phase (revealing new screen)")]
        public float inDuration = 0.3f;

        [Tooltip("Optional pause at full coverage before transitioning in")]
        public float holdDuration = 0.1f;

        /// <summary>
        /// Called when transition starts. Use to initialize/show transition visuals.
        /// </summary>
        public virtual void OnTransitionStart() { }

        /// <summary>
        /// Called when transition completes. Use to cleanup/hide transition visuals.
        /// </summary>
        public virtual void OnTransitionComplete() { }

        /// <summary>
        /// Animate the "out" phase (0 = start, 1 = fully covered).
        /// At t=1, the screen should be fully obscured so content can swap.
        /// </summary>
        protected abstract void ApplyTransitionOut(float t);

        /// <summary>
        /// Animate the "in" phase (0 = fully covered, 1 = fully revealed).
        /// </summary>
        protected abstract void ApplyTransitionIn(float t);

        /// <summary>
        /// Optional: Override to provide custom easing. Default is smooth step.
        /// </summary>
        protected virtual float Ease(float t)
        {
            // Smooth step: ease in and out
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Runs the full transition sequence. Called by UIScreenManager.
        /// onSwapContent is invoked at the midpoint when screen is fully covered.
        /// </summary>
        public IEnumerator RunTransition(Action onSwapContent)
        {
            OnTransitionStart();

            // Phase 1: Transition Out (cover the screen)
            if (outDuration > 0)
            {
                float elapsed = 0f;
                while (elapsed < outDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Ease(Mathf.Clamp01(elapsed / outDuration));
                    ApplyTransitionOut(t);
                    yield return null;
                }
            }
            ApplyTransitionOut(1f); // Ensure we hit exactly 1

            // Hold at full coverage
            if (holdDuration > 0)
                yield return new WaitForSeconds(holdDuration);

            // Swap content while fully covered
            onSwapContent?.Invoke();

            // Optional: wait a frame for new content to initialize
            yield return null;

            // Phase 2: Transition In (reveal new screen)
            if (inDuration > 0)
            {
                float elapsed = 0f;
                while (elapsed < inDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Ease(Mathf.Clamp01(elapsed / inDuration));
                    ApplyTransitionIn(t);
                    yield return null;
                }
            }
            ApplyTransitionIn(1f); // Ensure we hit exactly 1

            OnTransitionComplete();
        }
    }
}
