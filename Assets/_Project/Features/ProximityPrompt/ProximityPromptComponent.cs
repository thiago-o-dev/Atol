using TMPro;
using UnityEngine;

namespace Assets._Project.Features.ProximityPrompt
{
    /// <summary>
    /// Small world-space interaction label and optional QuickOutline highlight.
    /// Assign a world-space TMP_Text (or a TMP_Text under this boat) in the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProximityPromptComponent : MonoBehaviour
    {
        [Header("Prompt Visual")]
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private Transform billboard;
        [SerializeField] private string promptFormat = "[E] Board";
        [SerializeField] private Camera promptCamera;

        [Header("Boat Highlight")]
        [Tooltip("Model root to outline. If empty, this GameObject is used.")]
        [SerializeField] private GameObject outlineTarget;
        [SerializeField] private Color outlineColor = new Color(1f, 0.92f, 0.68f, 1f);
        [SerializeField, Range(1f, 10f)] private float outlineWidth = 4f;

        private Outline outline;
        private bool isVisible;
        private bool hasVisibilityState;

        public bool IsVisible => isVisible;

        private void Awake()
        {
            if (promptText == null)
                promptText = GetComponentInChildren<TMP_Text>(true);

            if (promptText == null)
            {
                GameObject labelObject = new GameObject("Board Prompt (World Space)", typeof(TextMeshPro));
                labelObject.transform.SetParent(transform, false);
                labelObject.transform.localPosition = CalculatePromptLocalPosition();
                TextMeshPro generatedLabel = labelObject.GetComponent<TextMeshPro>();
                generatedLabel.text = promptFormat;
                generatedLabel.fontSize = 3f;
                generatedLabel.alignment = TextAlignmentOptions.Center;
                generatedLabel.textWrappingMode = TextWrappingModes.NoWrap;
                generatedLabel.color = Color.white;
                generatedLabel.outlineWidth = 0.12f;
                generatedLabel.outlineColor = new Color(0f, 0f, 0f, 0.75f);
                promptText = generatedLabel;
            }

            if (billboard == null && promptText != null)
                billboard = promptText.transform;

            if (outlineTarget == null)
                outlineTarget = gameObject;

            if (outline == null && outlineTarget != null)
                outline = outlineTarget.GetComponent<Outline>();
            if (outline == null)
                outline = outlineTarget.GetComponentInChildren<Outline>(true);

            if (outline == null && outlineTarget != null)
                outline = outlineTarget.AddComponent<Outline>();

            if (outline != null)
            {
                outline.OutlineMode = Outline.Mode.OutlineVisible;
                outline.OutlineColor = outlineColor;
                outline.OutlineWidth = outlineWidth;
                outline.enabled = false;
            }

            if (promptText != null)
                promptText.text = promptFormat;

            SetVisible(false);
        }

        private Vector3 CalculatePromptLocalPosition()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return Vector3.up * 2f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 worldPosition = new Vector3(
                bounds.center.x, bounds.max.y + 0.65f, bounds.center.z);
            return transform.InverseTransformPoint(worldPosition);
        }

        private void LateUpdate()
        {
            if (!isVisible || billboard == null)
                return;

            if (promptCamera == null)
                promptCamera = Camera.main;

            if (promptCamera != null)
                billboard.rotation = promptCamera.transform.rotation;
        }

        public void SetVisible(bool visible)
        {
            if (hasVisibilityState && isVisible == visible)
                return;

            hasVisibilityState = true;
            isVisible = visible;

            if (promptText != null)
            {
                if (promptText.text != promptFormat)
                    promptText.text = promptFormat;

                promptText.enabled = visible;
            }

            if (outline != null)
                outline.enabled = visible;
        }

        public void SetPrompt(string text)
        {
            string nextPrompt = string.IsNullOrWhiteSpace(text) ? "[E] Board" : text;
            if (promptFormat == nextPrompt)
                return;

            promptFormat = nextPrompt;
            if (promptText != null)
                promptText.text = promptFormat;
        }

        private void OnValidate()
        {
            outlineWidth = Mathf.Clamp(outlineWidth, 1f, 10f);
        }
    }
}
