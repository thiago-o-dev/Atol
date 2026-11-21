using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// Small screen-space hint shown only while the player is aboard a boat.
    /// Creates its own lightweight overlay when no TMP label is assigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatControlHintUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private string controlsText =
            "<b>A / D</b>  Steer     <b>W / S</b>  Sail     <b>Hold Q / E</b>  Rotate sail\n<b>Hold F</b>  Bail water     <b>Click</b>  Strike     <b>Space</b>  Leave boat";
        [Tooltip("Seconds fully visible after boarding before the hint dims.")]
        [SerializeField] private float dimAfter = 8f;
        [SerializeField, Range(0f, 1f)] private float dimmedAlpha = 0.3f;
        [SerializeField] private int fontSize = 21;
        [SerializeField] private Vector2 bottomLeftOffset = new Vector2(28f, 28f);
        [SerializeField] private bool visible;
        private bool hasVisibilityState;
        private float shownTime;

        private void Awake()
        {
            if (hintText == null)
                CreateOverlay();
            SetVisible(visible);
        }

        public void SetVisible(bool value)
        {
            if (hasVisibilityState && visible == value)
                return;

            visible = value;
            hasVisibilityState = true;
            if (hintText != null)
            {
                if (hintText.text != controlsText)
                    hintText.text = controlsText;
                hintText.enabled = value;
                hintText.alpha = 1f;
                shownTime = Time.unscaledTime;
            }
        }

        private void Update()
        {
            if (!visible || hintText == null)
                return;

            float alpha = Time.unscaledTime - shownTime > dimAfter ? dimmedAlpha : 1f;
            hintText.alpha = Mathf.MoveTowards(hintText.alpha, alpha, Time.unscaledDeltaTime);
        }

        private void CreateOverlay()
        {
            Canvas existingCanvas = FindAnyObjectByType<Canvas>();
            Canvas canvas = existingCanvas != null && existingCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? existingCanvas
                : null;

            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Boat Controls Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 80;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            GameObject labelObject = new GameObject("Boat Controls Hint", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(canvas.transform, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = bottomLeftOffset;
            rect.sizeDelta = new Vector2(1080f, 78f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = controlsText;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.richText = true;
            label.color = new Color(1f, 1f, 1f, 0.94f);
            label.outlineWidth = 0.18f;
            label.outlineColor = new Color(0f, 0f, 0f, 0.8f);
            hintText = label;
        }

        private void OnValidate()
        {
            fontSize = Mathf.Clamp(fontSize, 12, 40);
        }
    }
}
