using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FigmaImporter
{
    /// <summary>
    /// Automatically wires button pointer events to FigmaVariantController states.
    /// Maps hover, press, and disabled states to variant children.
    /// </summary>
    [AddComponentMenu("Figma/Button State Wirer")]
    public class FigmaButtonStateWirer : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("The variant controller to drive")]
        public FigmaVariantController controller;

        [Tooltip("State key for normal/default appearance")]
        public string normalStateKey;

        [Tooltip("State key for hover/highlighted appearance")]
        public string hoverStateKey;

        [Tooltip("State key for pressed appearance")]
        public string pressedStateKey;

        [Tooltip("State key for disabled appearance")]
        public string disabledStateKey;

        private bool isPointerInside;
        private bool isPointerDown;
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private bool IsInteractable
        {
            get { return button == null || button.interactable; }
        }

        private void OnEnable()
        {
            UpdateVisualState();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isPointerInside = true;
            if (!IsInteractable) return;
            if (!string.IsNullOrEmpty(hoverStateKey) && controller != null)
                controller.SetState(hoverStateKey);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isPointerInside = false;
            isPointerDown = false;
            if (!IsInteractable) return;
            if (!string.IsNullOrEmpty(normalStateKey) && controller != null)
                controller.SetState(normalStateKey);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPointerDown = true;
            if (!IsInteractable) return;
            if (!string.IsNullOrEmpty(pressedStateKey) && controller != null)
                controller.SetState(pressedStateKey);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPointerDown = false;
            if (!IsInteractable) return;

            if (isPointerInside && !string.IsNullOrEmpty(hoverStateKey) && controller != null)
                controller.SetState(hoverStateKey);
            else if (!string.IsNullOrEmpty(normalStateKey) && controller != null)
                controller.SetState(normalStateKey);
        }

        private void UpdateVisualState()
        {
            if (controller == null) return;

            if (!IsInteractable && !string.IsNullOrEmpty(disabledStateKey))
            {
                controller.SetState(disabledStateKey);
            }
            else if (!string.IsNullOrEmpty(normalStateKey))
            {
                controller.SetState(normalStateKey);
            }
        }
    }
}
