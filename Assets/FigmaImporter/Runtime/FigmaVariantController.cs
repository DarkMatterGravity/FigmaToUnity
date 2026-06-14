using System.Collections.Generic;
using UnityEngine;

namespace FigmaImporter
{
    /// <summary>
    /// Controls variant state for Figma component sets.
    /// Switches between variant children based on state keys (e.g., "State=Default", "State=Hover").
    /// </summary>
    [AddComponentMenu("Figma/Variant Controller")]
    public class FigmaVariantController : MonoBehaviour
    {
        [Tooltip("Variant state keys (parallel with stateChildren)")]
        public string[] stateKeys = new string[0];

        [Tooltip("Variant child GameObjects (parallel with stateKeys)")]
        public GameObject[] stateChildren = new GameObject[0];

        [Tooltip("The default state key that was active in Figma")]
        public string defaultStateKey;

        private Dictionary<string, GameObject> lookup;
        private string currentStateKey;

        private void Awake()
        {
            RebuildLookup();

            if (!string.IsNullOrEmpty(defaultStateKey))
                currentStateKey = defaultStateKey;
            else if (stateKeys != null && stateKeys.Length > 0)
                currentStateKey = stateKeys[0];
        }

        private void RebuildLookup()
        {
            lookup = new Dictionary<string, GameObject>();
            if (stateKeys == null || stateChildren == null) return;

            int count = Mathf.Min(stateKeys.Length, stateChildren.Length);
            for (int i = 0; i < count; i++)
            {
                if (stateKeys[i] != null && stateChildren[i] != null)
                    lookup[stateKeys[i]] = stateChildren[i];
            }
        }

        /// <summary>
        /// Activates the variant child matching the given state key.
        /// Supports exact match or partial match (e.g. "Hover" matches "State=Hover").
        /// </summary>
        public void SetState(string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            if (lookup == null) RebuildLookup();

            // Try exact match first
            GameObject target;
            if (lookup.TryGetValue(state, out target))
            {
                ActivateOnly(target);
                currentStateKey = state;
                return;
            }

            // Try partial match: "Hover" matches "State=Hover" or "State=Hover, Size=Large"
            foreach (var kvp in lookup)
            {
                if (kvp.Key.Contains("=" + state) || kvp.Key.Contains("=" + state + ","))
                {
                    ActivateOnly(kvp.Value);
                    currentStateKey = kvp.Key;
                    return;
                }
            }
        }

        /// <summary>Returns the currently active state key.</summary>
        public string GetCurrentState()
        {
            return currentStateKey;
        }

        /// <summary>Returns all available state keys.</summary>
        public string[] GetAvailableStates()
        {
            return stateKeys != null ? stateKeys : new string[0];
        }

        private void ActivateOnly(GameObject target)
        {
            if (stateChildren == null) return;
            for (int i = 0; i < stateChildren.Length; i++)
            {
                if (stateChildren[i] != null)
                    stateChildren[i].SetActive(stateChildren[i] == target);
            }
        }
    }
}
