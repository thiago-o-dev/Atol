using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CompassLine
{
    /// <summary>
    /// A horizontal, north-referenced compass strip built from Unity uGUI elements.
    /// World convention: +Z is north and +X is east. One Unity unit is treated as one metre
    /// for the minimum-distance filter.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class CompassLineUI : MonoBehaviour
    {
        public enum CardinalLanguage
        {
            Portuguese,
            English
        }

        [Serializable]
        public sealed class CompassLocation
        {
            [Tooltip("Optional stable key used by SetLocationActive().")]
            public string id;

            [Tooltip("World transform whose direction and distance are shown on the compass.")]
            public Transform target;

            [Tooltip("Sprite displayed for this location. A missing sprite hides this marker.")]
            public Sprite icon;

            [Tooltip("Turn off to hide this location without removing it from the list.")]
            public bool isActive = true;
        }

        [Header("Heading and world reference")]
        [Tooltip("Usually the player camera or player root. If empty, Camera.main is used, then this transform.")]
        [SerializeField] private Transform headingSource;

        [Tooltip("Optional separate point used for distance checks. Defaults to Heading Source.")]
        [SerializeField] private Transform distanceOrigin;

        [Tooltip("Yaw offset, in degrees, between Unity +Z and your game's north. Leave at 0 when +Z is north.")]
        [SerializeField] private float northYawOffset;

        [Header("Language and labels")]
        [SerializeField] private CardinalLanguage language = CardinalLanguage.Portuguese;
        [Tooltip("When enabled, show Norte/Leste/etc. or North/East/etc. instead of single-letter abbreviations.")]
        [SerializeField] private bool showFullCardinalNames;
        [SerializeField] private bool showCardinalLabels = true;
        [SerializeField] private bool showDegreeLabels = true;
        [SerializeField, Min(1)] private int degreeStep = 45;
        [SerializeField] private Font labelFont;
        [SerializeField, Min(8)] private int cardinalFontSize = 14;
        [SerializeField, Min(8)] private int degreeFontSize = 11;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Strip background")]
        [Tooltip("Optional custom sprite. When empty, a simple 256-pixel white line is generated at runtime.")]
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Color backgroundColor = Color.white;
        [SerializeField, Min(1f)] private float compassWidth = 256f;
        [Tooltip("The default generated line is 2 UI units tall. Increase this when using a custom background image.")]
        [SerializeField, Min(1f)] private float backgroundHeight = 2f;
        [SerializeField, Min(40f)] private float rootHeight = 104f;

        [Header("Visible compass range")]
        [Tooltip("Total horizontal field of view shown by the strip. 180 means 90 degrees to either side of forward.")]
        [SerializeField, Range(30f, 360f)] private float visibleArcDegrees = 180f;
        [Tooltip("Start fading overlays at this fraction of the half-width. 0.8 means the final 20% fades to transparent.")]
        [SerializeField, Range(0f, 0.99f)] private float edgeFadeStart = 0.78f;

        [Header("Location markers")]
        [SerializeField] private List<CompassLocation> locations = new List<CompassLocation>();
        [Tooltip("Markers closer than this distance are hidden. Assumes 1 Unity unit = 1 metre.")]
        [SerializeField, Min(0f)] private float minimumVisibleDistance = 100f;
        [Tooltip("When enabled, distance is measured on the XZ plane and ignores elevation.")]
        [SerializeField] private bool useHorizontalDistance = true;
        [SerializeField, Min(4f)] private float markerSize = 24f;
        [Tooltip("Vertical UI offset of location icons relative to the line. Positive values move icons upward.")]
        [SerializeField] private float markerYOffset = 13f;
        [SerializeField] private Color markerTint = Color.white;

        [Header("Vertical layout")]
        [SerializeField] private float cardinalLabelYOffset = 38f;
        [SerializeField] private float tickLength = 7f;
        [SerializeField] private float degreeLabelYOffset = -25f;
        [SerializeField] private float tickYOffset = -4f;

        [Header("Runtime")]
        [Tooltip("Rebuild marker UI automatically when the number of locations changes.")]
        [SerializeField] private bool rebuildWhenLocationCountChanges = true;

        private RectTransform rootRect;
        private Image backgroundImage;
        private Texture2D generatedLineTexture;
        private Sprite generatedLineSprite;
        private readonly List<MarkerView> markerViews = new List<MarkerView>();
        private readonly List<CardinalView> cardinalViews = new List<CardinalView>();
        private readonly List<DegreeView> degreeViews = new List<DegreeView>();
        private bool isBuilt;
        private int lastLocationCount = -1;

        private static readonly float[] CardinalBearings = { 0f, 90f, 180f, 270f };

        private sealed class MarkerView
        {
            public RectTransform rect;
            public Image image;
        }

        private sealed class CardinalView
        {
            public RectTransform rect;
            public Text text;
            public int index;
        }

        private sealed class DegreeView
        {
            public RectTransform tickRect;
            public Image tick;
            public RectTransform labelRect;
            public Text label;
            public float bearing;
        }

        public IReadOnlyList<CompassLocation> Locations => locations;

        private void Awake()
        {
            ResolveHeadingSource();
            BuildUI();
        }

        private void OnEnable()
        {
            if (!isBuilt && Application.isPlaying)
            {
                ResolveHeadingSource();
                BuildUI();
            }
        }

        private void OnDestroy()
        {
            if (generatedLineSprite != null)
            {
                Destroy(generatedLineSprite);
            }

            if (generatedLineTexture != null)
            {
                Destroy(generatedLineTexture);
            }
        }

        private void LateUpdate()
        {
            if (!isBuilt)
            {
                return;
            }

            if (rebuildWhenLocationCountChanges && lastLocationCount != locations.Count)
            {
                RebuildLocationMarkers();
            }

            if (headingSource == null)
            {
                ResolveHeadingSource();
                if (headingSource == null)
                {
                    return;
                }
            }

            float headingBearing = Mathf.Repeat(headingSource.eulerAngles.y - northYawOffset, 360f);
            UpdateCardinals(headingBearing);
            UpdateDegreeTicks(headingBearing);
            UpdateLocationMarkers(headingBearing);
        }

        /// <summary>
        /// Rebuilds UI marker objects after changing the location list at runtime.
        /// The list itself is serialized and can also be edited in the Inspector.
        /// </summary>
        public void RefreshLocations()
        {
            if (!isBuilt)
            {
                return;
            }

            RebuildLocationMarkers();
        }

        /// <summary>
        /// Enables or disables the first location with the supplied ID.
        /// Returns false when no matching ID exists.
        /// </summary>
        public bool SetLocationActive(string id, bool active)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < locations.Count; i++)
            {
                CompassLocation location = locations[i];
                if (location != null && string.Equals(location.id, id, StringComparison.Ordinal))
                {
                    location.isActive = active;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Changes the active flag by list index. Returns false for an invalid index.
        /// </summary>
        public bool SetLocationActive(int index, bool active)
        {
            if (index < 0 || index >= locations.Count || locations[index] == null)
            {
                return false;
            }

            locations[index].isActive = active;
            return true;
        }

        /// <summary>Adds a location at runtime and refreshes the marker views.</summary>
        public void AddLocation(CompassLocation location)
        {
            if (location == null)
            {
                return;
            }

            locations.Add(location);
            RefreshLocations();
        }

        /// <summary>Removes the first location with the supplied ID and refreshes the marker views.</summary>
        public bool RemoveLocation(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i] != null && string.Equals(locations[i].id, id, StringComparison.Ordinal))
                {
                    locations.RemoveAt(i);
                    RefreshLocations();
                    return true;
                }
            }

            return false;
        }

        private void ResolveHeadingSource()
        {
            if (headingSource == null)
            {
                Camera mainCamera = Camera.main;
                headingSource = mainCamera != null ? mainCamera.transform : transform;
            }

            if (distanceOrigin == null)
            {
                distanceOrigin = headingSource;
            }
        }

        private void BuildUI()
        {
            if (isBuilt)
            {
                return;
            }

            rootRect = GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(compassWidth, rootHeight);

            if (GetComponent<RectMask2D>() == null)
            {
                gameObject.AddComponent<RectMask2D>();
            }

            BuildBackground();
            BuildCardinalLabels();
            BuildDegreeTicks();
            RebuildLocationMarkers();

            isBuilt = true;
        }

        private void BuildBackground()
        {
            backgroundImage = CreateImage("Compass Background", rootRect);
            backgroundImage.sprite = backgroundSprite != null ? backgroundSprite : GetGeneratedLineSprite();
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.preserveAspect = false;
            backgroundImage.color = backgroundColor;
            SetRect(backgroundImage.rectTransform, Vector2.zero, new Vector2(compassWidth, backgroundHeight));
        }

        private Sprite GetGeneratedLineSprite()
        {
            if (generatedLineSprite != null)
            {
                return generatedLineSprite;
            }

            generatedLineTexture = new Texture2D(256, 2, TextureFormat.RGBA32, false)
            {
                name = "CompassLine_Default_256px",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color32[] pixels = new Color32[256 * 2];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            generatedLineTexture.SetPixels32(pixels);
            generatedLineTexture.Apply(false, true);
            generatedLineSprite = Sprite.Create(
                generatedLineTexture,
                new Rect(0f, 0f, 256f, 2f),
                new Vector2(0.5f, 0.5f),
                100f);
            generatedLineSprite.name = "CompassLine_Default_256px_Sprite";
            generatedLineSprite.hideFlags = HideFlags.DontSave;
            return generatedLineSprite;
        }

        private void BuildCardinalLabels()
        {
            cardinalViews.Clear();
            if (!showCardinalLabels)
            {
                return;
            }

            for (int i = 0; i < CardinalBearings.Length; i++)
            {
                Text text = CreateText("Cardinal " + i, rootRect, cardinalFontSize, labelColor);
                text.fontStyle = FontStyle.Bold;
                RectTransform rect = text.rectTransform;
                float labelWidth = showFullCardinalNames ? 76f : 44f;
                SetRect(rect, new Vector2(0f, cardinalLabelYOffset), new Vector2(labelWidth, 22f));

                cardinalViews.Add(new CardinalView
                {
                    rect = rect,
                    text = text,
                    index = i
                });
            }
        }

        private void BuildDegreeTicks()
        {
            ClearDegreeViews();
            if (!showDegreeLabels)
            {
                return;
            }

            int step = Mathf.Clamp(degreeStep, 1, 180);
            for (int bearing = 0; bearing < 360; bearing += step)
            {
                Image tick = CreateImage("Bearing Tick " + bearing, rootRect);
                tick.color = labelColor;
                RectTransform tickRect = tick.rectTransform;
                SetRect(tickRect, new Vector2(0f, tickYOffset), new Vector2(1.5f, tickLength));

                Text label = CreateText("Bearing Label " + bearing, rootRect, degreeFontSize, labelColor);
                RectTransform labelRect = label.rectTransform;
                SetRect(labelRect, new Vector2(0f, degreeLabelYOffset), new Vector2(34f, 18f));
                label.text = bearing.ToString();

                degreeViews.Add(new DegreeView
                {
                    tickRect = tickRect,
                    tick = tick,
                    labelRect = labelRect,
                    label = label,
                    bearing = bearing
                });
            }
        }

        private void RebuildLocationMarkers()
        {
            for (int i = 0; i < markerViews.Count; i++)
            {
                if (markerViews[i] != null && markerViews[i].image != null)
                {
                    Destroy(markerViews[i].image.gameObject);
                }
            }

            markerViews.Clear();

            for (int i = 0; i < locations.Count; i++)
            {
                Image image = CreateImage("Location Marker " + i, rootRect);
                image.preserveAspect = true;
                image.color = markerTint;
                SetRect(image.rectTransform, new Vector2(0f, markerYOffset), new Vector2(markerSize, markerSize));
                markerViews.Add(new MarkerView
                {
                    rect = image.rectTransform,
                    image = image
                });
            }

            lastLocationCount = locations.Count;
        }

        private void UpdateCardinals(float headingBearing)
        {
            for (int i = 0; i < cardinalViews.Count; i++)
            {
                CardinalView view = cardinalViews[i];
                float x;
                float fade;
                bool inRange = TryGetStripPosition(CardinalBearings[view.index], headingBearing, out x, out fade);
                view.text.text = GetCardinalLabel(view.index);
                view.rect.sizeDelta = new Vector2(showFullCardinalNames ? 76f : 44f, 22f);
                view.rect.anchoredPosition = new Vector2(x, cardinalLabelYOffset);
                SetGraphicAlpha(view.text, labelColor, fade);
                view.text.gameObject.SetActive(inRange && fade > 0.001f);
            }
        }

        private void UpdateDegreeTicks(float headingBearing)
        {
            for (int i = 0; i < degreeViews.Count; i++)
            {
                DegreeView view = degreeViews[i];
                float x;
                float fade;
                bool inRange = TryGetStripPosition(view.bearing, headingBearing, out x, out fade);

                view.tickRect.anchoredPosition = new Vector2(x, tickYOffset);
                view.labelRect.anchoredPosition = new Vector2(x, degreeLabelYOffset);
                SetGraphicAlpha(view.tick, labelColor, fade);
                SetGraphicAlpha(view.label, labelColor, fade);
                bool visible = inRange && fade > 0.001f;
                view.tick.gameObject.SetActive(visible);
                view.label.gameObject.SetActive(visible);
            }
        }

        private void UpdateLocationMarkers(float headingBearing)
        {
            Transform origin = distanceOrigin != null ? distanceOrigin : headingSource;
            float minimumDistanceSquared = minimumVisibleDistance * minimumVisibleDistance;

            for (int i = 0; i < markerViews.Count; i++)
            {
                MarkerView view = markerViews[i];
                CompassLocation location = i < locations.Count ? locations[i] : null;

                if (location == null || !location.isActive || location.target == null ||
                    !location.target.gameObject.activeInHierarchy || location.icon == null || origin == null)
                {
                    view.image.gameObject.SetActive(false);
                    continue;
                }

                Vector3 toTarget = location.target.position - origin.position;
                if (useHorizontalDistance)
                {
                    toTarget.y = 0f;
                }

                float distanceSquared = toTarget.sqrMagnitude;
                if (distanceSquared < minimumDistanceSquared || distanceSquared < 0.0001f)
                {
                    view.image.gameObject.SetActive(false);
                    continue;
                }

                float bearing = Mathf.Repeat(Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg - northYawOffset, 360f);
                float x;
                float fade;
                bool inRange = TryGetStripPosition(bearing, headingBearing, out x, out fade);
                if (!inRange || fade <= 0.001f)
                {
                    view.image.gameObject.SetActive(false);
                    continue;
                }

                view.image.sprite = location.icon;
                view.rect.anchoredPosition = new Vector2(x, markerYOffset);
                view.rect.sizeDelta = new Vector2(markerSize, markerSize);
                SetGraphicAlpha(view.image, markerTint, fade);
                view.image.gameObject.SetActive(true);
            }
        }

        private bool TryGetStripPosition(float targetBearing, float headingBearing, out float x, out float fade)
        {
            float relativeBearing = Mathf.DeltaAngle(headingBearing, targetBearing);
            float halfArc = Mathf.Max(0.5f, visibleArcDegrees * 0.5f);
            float normalized = Mathf.Abs(relativeBearing) / halfArc;
            x = (relativeBearing / halfArc) * (compassWidth * 0.5f);

            if (normalized > 1f)
            {
                fade = 0f;
                return false;
            }

            if (normalized <= edgeFadeStart)
            {
                fade = 1f;
            }
            else
            {
                fade = 1f - Mathf.InverseLerp(edgeFadeStart, 1f, normalized);
            }

            return true;
        }

        private string GetCardinalLabel(int index)
        {
            if (language == CardinalLanguage.Portuguese)
            {
                if (showFullCardinalNames)
                {
                    switch (index)
                    {
                        case 0: return "Norte";
                        case 1: return "Leste";
                        case 2: return "Sul";
                        default: return "Oeste";
                    }
                }

                switch (index)
                {
                    case 0: return "N";
                    case 1: return "L";
                    case 2: return "S";
                    default: return "O";
                }
            }

            if (showFullCardinalNames)
            {
                switch (index)
                {
                    case 0: return "North";
                    case 1: return "East";
                    case 2: return "South";
                    default: return "West";
                }
            }

            switch (index)
            {
                case 0: return "N";
                case 1: return "E";
                case 2: return "S";
                default: return "W";
            }
        }

        private Image CreateImage(string objectName, Transform parent)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string objectName, Transform parent, int fontSize, Color color)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void SetGraphicAlpha(Graphic graphic, Color baseColor, float alphaMultiplier)
        {
            Color color = baseColor;
            color.a *= Mathf.Clamp01(alphaMultiplier);
            graphic.color = color;
        }

        private void ClearDegreeViews()
        {
            for (int i = 0; i < degreeViews.Count; i++)
            {
                if (degreeViews[i].tick != null)
                {
                    Destroy(degreeViews[i].tick.gameObject);
                }

                if (degreeViews[i].label != null)
                {
                    Destroy(degreeViews[i].label.gameObject);
                }
            }

            degreeViews.Clear();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            compassWidth = Mathf.Max(1f, compassWidth);
            backgroundHeight = Mathf.Max(1f, backgroundHeight);
            rootHeight = Mathf.Max(40f, rootHeight);
            visibleArcDegrees = Mathf.Clamp(visibleArcDegrees, 30f, 360f);
            degreeStep = Mathf.Clamp(degreeStep, 1, 180);
            minimumVisibleDistance = Mathf.Max(0f, minimumVisibleDistance);
            markerSize = Mathf.Max(4f, markerSize);
            tickLength = Mathf.Max(1f, tickLength);
        }
#endif
    }
}
