using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FigmaImporter
{
    /// <summary>
    /// Manages UI screen navigation with transitions and history.
    /// Loads screen prefabs from Resources/Screens/{screenName}.
    /// </summary>
    public class UIScreenManager : MonoBehaviour
    {
        public static UIScreenManager Instance { get; private set; }

        [Header("Parents")]
        [Tooltip("Parent transform for instantiated screens. If null, uses this transform.")]
        public Transform screenParent;

        [Tooltip("Parent transform for 3D environment prefabs. If null, environments are disabled.")]
        public Transform environmentParent;

        [Header("Initial Screen")]
        [Tooltip("Screen to load automatically on Start. Leave empty to skip.")]
        public string initialScreen;

        [Header("Transitions")]
        [Tooltip("Default screen transition. If null, screens swap instantly.")]
        public ScreenTransition defaultTransition;

        [Tooltip("Optional camera transition that plays alongside screen transitions.")]
        public CameraTransition cameraTransition;

        [Tooltip("Skip transition for initial screen load")]
        public bool skipInitialTransition = true;

        private GameObject currentScreen;
        private string currentScreenName;
        private GameObject currentEnvironment;
        private Stack<string> historyStack = new Stack<string>();
        private bool isTransitioning;
        private ScreenTransition loadedTransitionInstance;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (screenParent == null)
                screenParent = transform;
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(initialScreen))
            {
                if (skipInitialTransition)
                    ShowImmediate(initialScreen);
                else
                    Show(initialScreen);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Navigate to a screen by name with transition.
        /// </summary>
        public static void Show(string screenName)
        {
            Show(screenName, null);
        }

        /// <summary>
        /// Navigate to a screen with a specific transition override.
        /// </summary>
        public static void Show(string screenName, ScreenTransition transitionOverride)
        {
            if (Instance == null)
            {
                Debug.LogWarning($"UIScreenManager: No instance in scene. Cannot show '{screenName}'.");
                return;
            }
            Instance.ShowInternal(screenName, pushHistory: true, transition: transitionOverride);
        }

        /// <summary>
        /// Navigate to a screen instantly without transition.
        /// </summary>
        public static void ShowImmediate(string screenName)
        {
            if (Instance == null)
            {
                Debug.LogWarning($"UIScreenManager: No instance in scene. Cannot show '{screenName}'.");
                return;
            }
            Instance.ShowInternal(screenName, pushHistory: true, transition: null, forceImmediate: true);
        }

        /// <summary>
        /// Navigate back to the previous screen.
        /// </summary>
        public static void Back()
        {
            Back(null);
        }

        /// <summary>
        /// Navigate back with a specific transition override.
        /// </summary>
        public static void Back(ScreenTransition transitionOverride)
        {
            if (Instance == null)
            {
                Debug.LogWarning("UIScreenManager: No instance in scene. Cannot go back.");
                return;
            }
            Instance.BackInternal(transitionOverride);
        }

        /// <summary>Returns the name of the currently displayed screen, or null.</summary>
        public static string CurrentScreen => Instance != null ? Instance.currentScreenName : null;

        /// <summary>Returns true if a transition is currently playing.</summary>
        public static bool IsTransitioning => Instance != null && Instance.isTransitioning;

        /// <summary>
        /// Event fired when a screen transition starts. Args: (fromScreen, toScreen)
        /// </summary>
        public static event Action<string, string> OnTransitionStart;

        /// <summary>
        /// Event fired when a screen transition completes. Args: (newScreen)
        /// </summary>
        public static event Action<string> OnTransitionComplete;

        private void ShowInternal(string screenName, bool pushHistory, ScreenTransition transition = null, bool forceImmediate = false)
        {
            if (string.IsNullOrEmpty(screenName)) return;

            if (isTransitioning)
            {
                Debug.LogWarning($"UIScreenManager: Transition in progress, ignoring Show('{screenName}')");
                return;
            }

            var prefab = Resources.Load<GameObject>("Screens/" + screenName);
            if (prefab == null)
            {
                Debug.LogWarning($"UIScreenManager: Screen prefab not found at Resources/Screens/{screenName}");
                return;
            }

            ScreenTransition activeTransition = null;
            if (!forceImmediate)
            {
                if (transition != null)
                {
                    activeTransition = transition;
                }
                else
                {
                    var perScreenTransition = LoadScreenTransition(screenName);
                    activeTransition = perScreenTransition != null ? perScreenTransition : defaultTransition;
                }
            }

            if (activeTransition != null)
            {
                StartCoroutine(TransitionToScreen(screenName, prefab, pushHistory, activeTransition));
            }
            else
            {
                SwapScreen(screenName, prefab, pushHistory);
            }
        }

        private IEnumerator TransitionToScreen(string screenName, GameObject prefab, bool pushHistory, ScreenTransition transition)
        {
            isTransitioning = true;
            string fromScreen = currentScreenName;

            OnTransitionStart?.Invoke(fromScreen, screenName);

            float totalDuration = transition.outDuration + transition.holdDuration + transition.inDuration;
            if (cameraTransition != null)
            {
                cameraTransition.PlayEffect(totalDuration);
            }

            yield return transition.RunTransition(() =>
            {
                SwapScreen(screenName, prefab, pushHistory);
            });

            isTransitioning = false;

            OnTransitionComplete?.Invoke(screenName);
        }

        private void SwapScreen(string screenName, GameObject prefab, bool pushHistory)
        {
            if (pushHistory && !string.IsNullOrEmpty(currentScreenName))
                historyStack.Push(currentScreenName);

            if (currentScreen != null)
                Destroy(currentScreen);
            if (currentEnvironment != null)
                Destroy(currentEnvironment);

            currentScreen = Instantiate(prefab, screenParent);
            currentScreen.name = screenName;
            currentScreenName = screenName;

            if (environmentParent != null)
            {
                var envPrefab = Resources.Load<GameObject>("Environments/" + screenName + "_Environment");
                if (envPrefab != null)
                {
                    currentEnvironment = Instantiate(envPrefab, environmentParent);
                    currentEnvironment.name = screenName + "_Environment";
                }
            }
        }

        private ScreenTransition LoadScreenTransition(string screenName)
        {
            var transitionPrefab = Resources.Load<GameObject>("Transitions/" + screenName + "_Transition");
            if (transitionPrefab == null)
                return null;

            if (loadedTransitionInstance != null)
            {
                Destroy(loadedTransitionInstance.gameObject);
                loadedTransitionInstance = null;
            }

            var instance = Instantiate(transitionPrefab, transform);
            instance.name = screenName + "_Transition";
            loadedTransitionInstance = instance.GetComponent<ScreenTransition>();

            return loadedTransitionInstance;
        }

        private void BackInternal(ScreenTransition transition)
        {
            if (historyStack.Count == 0)
            {
                Debug.Log("UIScreenManager: Nothing to go back to.");
                return;
            }

            if (isTransitioning)
            {
                Debug.LogWarning("UIScreenManager: Transition in progress, ignoring Back()");
                return;
            }

            var previousName = historyStack.Pop();

            var prefab = Resources.Load<GameObject>("Screens/" + previousName);
            if (prefab == null)
            {
                Debug.LogWarning($"UIScreenManager: Screen prefab not found at Resources/Screens/{previousName}");
                return;
            }

            ScreenTransition activeTransition;
            if (transition != null)
            {
                activeTransition = transition;
            }
            else
            {
                var perScreenTransition = LoadScreenTransition(previousName);
                activeTransition = perScreenTransition != null ? perScreenTransition : defaultTransition;
            }

            if (activeTransition != null)
            {
                StartCoroutine(TransitionToScreen(previousName, prefab, false, activeTransition));
            }
            else
            {
                SwapScreen(previousName, prefab, false);
            }
        }
    }
}
