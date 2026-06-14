using System.Collections;
using UnityEngine;

namespace FigmaImporter
{
    /// <summary>
    /// Camera transition effects that play during screen changes.
    /// Works alongside ScreenTransition to add camera movement/effects.
    /// </summary>
    public class CameraTransition : MonoBehaviour
    {
        public enum CameraEffect
        {
            None,
            Shake,          // Quick shake during transition
            ZoomPulse,      // Zoom in slightly, then back out
            DollyForward,   // Move camera forward, then reset
            Drift           // Smooth drift to new position
        }

        [Header("Camera Reference")]
        [Tooltip("Camera to animate. If null, uses Camera.main")]
        public Camera targetCamera;

        [Header("Effect Settings")]
        public CameraEffect effect = CameraEffect.ZoomPulse;

        [Header("Shake Settings")]
        public float shakeIntensity = 0.1f;
        public float shakeFrequency = 25f;

        [Header("Zoom Settings")]
        public float zoomAmount = 5f;

        [Header("Dolly Settings")]
        public float dollyDistance = 1f;

        [Header("Timing")]
        [Tooltip("When to start the effect (0 = transition start, 0.5 = midpoint)")]
        [Range(0f, 1f)]
        public float startAt = 0f;

        [Tooltip("Duration of the camera effect")]
        public float duration = 0.5f;

        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private float originalFOV;
        private Coroutine currentEffect;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        /// <summary>
        /// Call this when a screen transition starts.
        /// totalDuration is the full transition time (out + hold + in).
        /// </summary>
        public void PlayEffect(float totalDuration)
        {
            if (targetCamera == null) return;
            if (currentEffect != null)
                StopCoroutine(currentEffect);

            currentEffect = StartCoroutine(RunEffect(totalDuration));
        }

        /// <summary>
        /// Immediately stops any running effect and resets camera.
        /// </summary>
        public void StopEffect()
        {
            if (currentEffect != null)
            {
                StopCoroutine(currentEffect);
                currentEffect = null;
            }
            ResetCamera();
        }

        private IEnumerator RunEffect(float totalDuration)
        {
            // Wait for start point
            if (startAt > 0)
                yield return new WaitForSeconds(totalDuration * startAt);

            // Store original values
            originalPosition = targetCamera.transform.localPosition;
            originalRotation = targetCamera.transform.localRotation;
            originalFOV = targetCamera.fieldOfView;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                ApplyEffect(t);
                yield return null;
            }

            ResetCamera();
            currentEffect = null;
        }

        private void ApplyEffect(float t)
        {
            switch (effect)
            {
                case CameraEffect.Shake:
                    ApplyShake(t);
                    break;
                case CameraEffect.ZoomPulse:
                    ApplyZoomPulse(t);
                    break;
                case CameraEffect.DollyForward:
                    ApplyDolly(t);
                    break;
                case CameraEffect.Drift:
                    ApplyDrift(t);
                    break;
            }
        }

        private void ApplyShake(float t)
        {
            float intensity = shakeIntensity * Mathf.Sin(t * Mathf.PI);

            float offsetX = Mathf.PerlinNoise(Time.time * shakeFrequency, 0f) * 2f - 1f;
            float offsetY = Mathf.PerlinNoise(0f, Time.time * shakeFrequency) * 2f - 1f;

            Vector3 shakeOffset = new Vector3(offsetX, offsetY, 0f) * intensity;
            targetCamera.transform.localPosition = originalPosition + shakeOffset;
        }

        private void ApplyZoomPulse(float t)
        {
            float zoomT = Mathf.Sin(t * Mathf.PI);
            targetCamera.fieldOfView = originalFOV - (zoomAmount * zoomT);
        }

        private void ApplyDolly(float t)
        {
            float dollyT = Mathf.Sin(t * Mathf.PI);
            Vector3 forward = targetCamera.transform.forward * dollyDistance * dollyT;
            targetCamera.transform.localPosition = originalPosition + forward;
        }

        private void ApplyDrift(float t)
        {
            float eased = EaseOutQuad(t);
            float driftAmount = Mathf.Sin(t * Mathf.PI) * 0.5f;

            Vector3 offset = new Vector3(0f, driftAmount * 0.2f, driftAmount * dollyDistance);
            targetCamera.transform.localPosition = originalPosition + targetCamera.transform.TransformDirection(offset);
        }

        private void ResetCamera()
        {
            if (targetCamera == null) return;
            targetCamera.transform.localPosition = originalPosition;
            targetCamera.transform.localRotation = originalRotation;
            targetCamera.fieldOfView = originalFOV;
        }

        private float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }
    }
}
