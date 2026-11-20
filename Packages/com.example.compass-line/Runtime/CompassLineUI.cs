using System;
using System.Collections;
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

            [Tooltip("Turn off to fade this location out without removing it from the list.")]
            public bool isActive = true;

            [Header("Landmark")]
            [Tooltip("Show an additional rotated-square diamond above the compass line for this location.")]
            public bool isLandmark;

            [Tooltip("Landmark diamond size in UI units before distance scaling.")]
            [Min(1f)] public float landmarkDiamondSize = 10f;

            [Tooltip("Landmark diamond vertical offset from the compass line. Positive values move it upward.")]
            public float landmarkYOffset = 0f;
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
        [Header("Line edge decoration")]
        [Tooltip("Show matching equilateral arrowhead triangles at both ends of the compass line.")]
        [SerializeField] private bool showEdgeTriangles = true;
        [Tooltip("Length of each equilateral triangle side, in UI units.")]
        [SerializeField, Min(1f)] private float edgeTriangleSize = 12f;

        [Header("Visible compass range")]
        [Tooltip("Total horizontal field of view shown by the strip. 180 means 90 degrees to either side of forward.")]
        [SerializeField, Range(30f, 360f)] private float visibleArcDegrees = 180f;
        [Tooltip("Start fading overlays at this fraction of the half-width. 0.8 means the final 20% fades to transparent.")]
        [SerializeField, Range(0f, 0.99f)] private float edgeFadeStart = 0.78f;
        [Tooltip("Angular hysteresis for marker visibility. After a marker leaves the visible arc, it must move this many degrees inside the arc before fading back in. This prevents boundary jitter from restarting fades every frame.")]
        [SerializeField, Range(0f, 15f)] private float outsideArcHysteresisDegrees = 2f;

        [Header("Location markers")]
        [SerializeField] private List<CompassLocation> locations = new List<CompassLocation>();
        [Tooltip("Markers closer than this distance are hidden. Assumes 1 Unity unit = 1 metre.")]
        [SerializeField, Min(0f)] private float minimumVisibleDistance = 100f;
        // Proximity filtering and distance-based sizing always use the XZ plane.
        [SerializeField, Min(4f)] private float markerSize = 24f;
        [Tooltip("Vertical UI offset of location icons relative to the line. Positive values move icons upward.")]
        [SerializeField] private float markerYOffset = 13f;
        [SerializeField] private Color markerTint = Color.white;
        [SerializeField] private Color landmarkTint = Color.white;

        [Header("Marker animation")]
        [Tooltip("Duration in seconds for marker fade/scale transitions.")]
        [SerializeField, Min(0f)] private float markerTransitionDuration = 0.2f;
        [Tooltip("A marker grows to this scale multiplier while fading out because it is inside the minimum distance. It shrinks back to 1 when it reappears.")]
        [SerializeField, Min(1f)] private float proximityHideScale = 1.25f;
        [Tooltip("Use unscaled time so compass animations continue while gameplay is paused.")]
        [SerializeField] private bool useUnscaledTime = true;

        [Header("Distance-based marker sizing")]
        [Tooltip("At or below this XZ distance, markers use their full intended size (scale 1).")]
        [SerializeField, Min(0f)] private float distanceScaleStart = 150f;
        [Tooltip("At or beyond this XZ distance, markers reach the configured minimum scale.")]
        [SerializeField, Min(0.01f)] private float distanceScaleEnd = 700f;
        [Tooltip("Smallest distance-based size multiplier. Maximum scale is always 1.")]
        [SerializeField, Range(0.01f, 1f)] private float minimumDistanceScale = 0.5f;

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
        private Texture2D generatedTriangleTexture;
        private Sprite generatedTriangleSprite;
        private Image leftEdgeTriangle;
        private Image rightEdgeTriangle;
        private readonly Dictionary<string, Coroutine> pendingTargetDisables = new Dictionary<string, Coroutine>();
        private readonly List<MarkerView> markerViews = new List<MarkerView>();
        private readonly List<CardinalView> cardinalViews = new List<CardinalView>();
        private readonly List<DegreeView> degreeViews = new List<DegreeView>();
        private bool isBuilt;
        private int lastLocationCount = -1;

        private static readonly float[] CardinalBearings = { 0f, 90f, 180f, 270f };

        private enum MarkerVisibilityState
        {
            Unknown,
            Visible,
            Inactive,
            Invalid,
            TooClose,
            OutsideArc
        }

        private sealed class MarkerView
        {
            public RectTransform rect;
            public Image image;
            public RectTransform diamondRect;
            public Image diamond;

            public MarkerVisibilityState state = MarkerVisibilityState.Unknown;
            public bool hasState;
            public float alpha = 1f;
            public float animatedScale = 1f;
            public float startAlpha = 1f;
            public float targetAlpha = 1f;
            public float startScale = 1f;
            public float targetScale = 1f;
            public float transitionElapsed;
            public float transitionDuration;
            // Latched separately from the transition state so an inactive/proximity
            // state cannot clear the outside-arc dead band and cause boundary flicker.
            public bool outsideArcLatched;
            // Last spatial edge fade is retained through inactive/invalid transitions,
            // preventing alpha from jumping back to 1 when target state changes mid-fade.
            public float edgeFade = 1f;
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
            ResetAllMarkerVisuals(disableGraphics: true);
            if (generatedLineSprite != null)
            {
                Destroy(generatedLineSprite);
            }

            if (generatedLineTexture != null)
            {
                Destroy(generatedLineTexture);
            }

            if (generatedTriangleSprite != null)
            {
                Destroy(generatedTriangleSprite);
            }

            if (generatedTriangleTexture != null)
            {
                Destroy(generatedTriangleTexture);
            }
        }

        private void LateUpdate()
        {
            if (!isBuilt)
            {
                return;
            }

            if ((rebuildWhenLocationCountChanges && lastLocationCount != locations.Count) || !AreMarkerViewsValid())
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

        /// <summary>
        /// Safely activates or deactivates the GameObject used by a location. When deactivating,
        /// the location marker is first marked inactive and the target GameObject is disabled
        /// only after its fade-out duration has elapsed. Use this instead of calling
        /// target.gameObject.SetActive(false) directly when the marker should animate away.
        /// </summary>
        public bool SetLocationTargetActiveWithFade(string id, bool active)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < locations.Count; i++)
            {
                CompassLocation location = locations[i];
                if (location == null || !string.Equals(location.id, id, StringComparison.Ordinal) || location.target == null)
                {
                    continue;
                }

                if (!active && (location.target == transform || transform.IsChildOf(location.target)))
                {
                    Debug.LogWarning("CompassLineUI cannot defer target deactivation when the target is the compass object or one of its parents. Disable the compass separately after handling its UI.", this);
                    return false;
                }

                Coroutine pending;
                if (pendingTargetDisables.TryGetValue(id, out pending) && pending != null)
                {
                    StopCoroutine(pending);
                    pendingTargetDisables.Remove(id);
                }

                if (active)
                {
                    if (!location.target.gameObject.activeSelf)
                    {
                        location.target.gameObject.SetActive(true);
                    }

                    location.isActive = true;
                    return true;
                }

                location.isActive = false;
                if (markerTransitionDuration <= 0f)
                {
                    location.target.gameObject.SetActive(false);
                }
                else
                {
                    pendingTargetDisables[id] = StartCoroutine(DisableLocationTargetAfterFade(id, location, location.target));
                }
                return true;
            }

            return false;
        }

        private IEnumerator DisableLocationTargetAfterFade(string id, CompassLocation location, Transform target)
        {
            float duration = Mathf.Max(0f, markerTransitionDuration);
            if (duration > 0f)
            {
                if (useUnscaledTime)
                {
                    yield return new WaitForSecondsRealtime(duration);
                }
                else
                {
                    yield return new WaitForSeconds(duration);
                }
            }

            // A location may have been re-enabled, removed, or retargeted while waiting.
            bool stillRegistered = locations.Contains(location);
            bool sameTarget = location != null && location.target == target;
            if (stillRegistered && sameTarget && !location.isActive && target != null)
            {
                target.gameObject.SetActive(false);
            }

            pendingTargetDisables.Remove(id);
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

            BuildEdgeTriangles();
        }

        private void BuildEdgeTriangles()
        {
            if (!showEdgeTriangles)
            {
                return;
            }

            Sprite triangleSprite = GetGeneratedTriangleSprite();
            float side = Mathf.Max(1f, edgeTriangleSize);
            float triangleWidth = side * 0.8660254f;
            Vector2 triangleSize = new Vector2(triangleWidth, side);

            leftEdgeTriangle = CreateImage("Compass Left Edge Triangle", rootRect);
            leftEdgeTriangle.sprite = triangleSprite;
            leftEdgeTriangle.type = Image.Type.Simple;
            leftEdgeTriangle.preserveAspect = false;
            leftEdgeTriangle.color = backgroundColor;
            SetRect(leftEdgeTriangle.rectTransform,
                new Vector2(-compassWidth * 0.5f + triangleWidth * 0.5f, 0f), triangleSize);
            // The generated sprite points right, toward the center from the left edge.

            rightEdgeTriangle = CreateImage("Compass Right Edge Triangle", rootRect);
            rightEdgeTriangle.sprite = triangleSprite;
            rightEdgeTriangle.type = Image.Type.Simple;
            rightEdgeTriangle.preserveAspect = false;
            rightEdgeTriangle.color = backgroundColor;
            SetRect(rightEdgeTriangle.rectTransform,
                new Vector2(compassWidth * 0.5f - triangleWidth * 0.5f, 0f), triangleSize);
            rightEdgeTriangle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        }

        private Sprite GetGeneratedTriangleSprite()
        {
            if (generatedTriangleSprite != null)
            {
                return generatedTriangleSprite;
            }

            // A right-pointing equilateral triangle. Its vertical base is 128 px and
            // its horizontal altitude is approximately 110.85 px (sqrt(3) / 2 * side).
            const int width = 111;
            const int height = 128;
            generatedTriangleTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "CompassLine_EquilateralEdgeTriangle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color32[] pixels = new Color32[width * height];
            float tipX = width - 1f;
            float bottomY = height - 1f;
            float middleY = bottomY * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Triangle vertices: (0,0), (0,height-1), (tipX,middleY).
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float normalizedHalfHeight = Mathf.Abs(py - middleY) / middleY;
                    float maxX = tipX * (1f - normalizedHalfHeight);
                    if (px <= maxX)
                    {
                        pixels[y * width + x] = new Color32(255, 255, 255, 255);
                    }
                    else
                    {
                        pixels[y * width + x] = new Color32(255, 255, 255, 0);
                    }
                }
            }

            generatedTriangleTexture.SetPixels32(pixels);
            generatedTriangleTexture.Apply(false, true);
            generatedTriangleSprite = Sprite.Create(
                generatedTriangleTexture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f);
            generatedTriangleSprite.name = "CompassLine_EquilateralEdgeTriangle_Sprite";
            generatedTriangleSprite.hideFlags = HideFlags.DontSave;
            return generatedTriangleSprite;
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

        private bool AreMarkerViewsValid()
        {
            // Count changes are controlled by rebuildWhenLocationCountChanges; this
            // check only repairs UI children that were externally deleted or destroyed.
            for (int i = 0; i < markerViews.Count; i++)
            {
                MarkerView view = markerViews[i];
                if (view == null || view.rect == null || view.image == null ||
                    view.diamondRect == null || view.diamond == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildLocationMarkers()
        {
            for (int i = 0; i < markerViews.Count; i++)
            {
                MarkerView oldView = markerViews[i];
                ResetMarkerScale(oldView);
                if (oldView.image != null)
                {
                    Destroy(oldView.image.gameObject);
                }

                if (oldView.diamond != null)
                {
                    Destroy(oldView.diamond.gameObject);
                }
            }

            markerViews.Clear();

            for (int i = 0; i < locations.Count; i++)
            {
                Image image = CreateImage("Location Marker " + i, rootRect);
                image.preserveAspect = true;
                image.color = markerTint;
                image.enabled = false;
                RectTransform markerRect = image.rectTransform;
                SetRect(markerRect, new Vector2(0f, markerYOffset), new Vector2(markerSize, markerSize));

                // A separate UI square is used for landmarks. Its RectTransform is also
                // animation-controlled, so no target/world object is ever scaled or rotated.
                Image diamond = CreateImage("Landmark Diamond " + i, rootRect);
                diamond.color = landmarkTint;
                diamond.enabled = false;
                RectTransform diamondRect = diamond.rectTransform;
                SetRect(diamondRect, new Vector2(0f, 0f), new Vector2(10f, 10f));
                diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

                markerViews.Add(new MarkerView
                {
                    rect = markerRect,
                    image = image,
                    diamondRect = diamondRect,
                    diamond = diamond
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
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            for (int i = 0; i < markerViews.Count; i++)
            {
                MarkerView view = markerViews[i];
                if (view == null || view.rect == null || view.image == null ||
                    view.diamondRect == null || view.diamond == null)
                {
                    continue;
                }

                CompassLocation location = i < locations.Count ? locations[i] : null;

                MarkerVisibilityState desiredState = MarkerVisibilityState.Invalid;
                float x = view.rect.anchoredPosition.x;
                float edgeFade = view.edgeFade;
                float distanceScale = 1f;
                float horizontalDistance = 0f;

                if (location == null)
                {
                    desiredState = MarkerVisibilityState.Invalid;
                }
                else if (!location.isActive || location.target == null ||
                         !location.target.gameObject.activeInHierarchy || origin == null)
                {
                    desiredState = MarkerVisibilityState.Inactive;
                }
                else if (location.icon == null && !location.isLandmark)
                {
                    desiredState = MarkerVisibilityState.Invalid;
                }
                else
                {
                    Vector3 toTarget = location.target.position - origin.position;
                    // Proximity filtering and distance sizing are deliberately XZ-only,
                    // so elevation differences never affect either behavior.
                    toTarget.y = 0f;
                    horizontalDistance = new Vector2(toTarget.x, toTarget.z).magnitude;

                    if (horizontalDistance < minimumVisibleDistance || horizontalDistance < 0.0001f)
                    {
                        desiredState = MarkerVisibilityState.TooClose;
                    }
                    else
                    {
                        float bearing = Mathf.Repeat(
                            Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg - northYawOffset,
                            360f);

                        distanceScale = GetDistanceScale(horizontalDistance);
                        float markerExtent = Mathf.Max(
                            markerSize,
                            location.isLandmark ? Mathf.Max(1f, location.landmarkDiamondSize) : 0f) * distanceScale;
                        float ignoredFade;
                        bool inRange = TryGetMarkerStripPosition(
                            view, bearing, headingBearing, markerExtent * 0.5f, out x, out ignoredFade);
                        edgeFade = ignoredFade;
                        view.edgeFade = edgeFade;
                        if (!inRange)
                        {
                            desiredState = MarkerVisibilityState.OutsideArc;
                        }
                        else
                        {
                            desiredState = MarkerVisibilityState.Visible;
                        }
                    }
                }

                // Keep a valid marker positioned safely inside the strip edge while it
                // fades out of range. If the Transform is moved outside the arc, do not
                // freeze it at its last in-range position or let RectMask2D clip it early.
                if (location != null && desiredState != MarkerVisibilityState.Invalid &&
                    desiredState != MarkerVisibilityState.Inactive)
                {
                    view.rect.anchoredPosition = new Vector2(x, markerYOffset);
                    view.rect.sizeDelta = new Vector2(markerSize * distanceScale, markerSize * distanceScale);

                    view.image.sprite = location.icon;

                    float diamondSize = Mathf.Max(1f, location.landmarkDiamondSize) * distanceScale;
                    view.diamondRect.anchoredPosition = new Vector2(x, location.landmarkYOffset);
                    view.diamondRect.sizeDelta = new Vector2(diamondSize, diamondSize);
                    view.diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                }
                UpdateMarkerTransition(view, desiredState, deltaTime);

                // Preserve the spatial edge fade even while the marker is in the
                // OutsideArc transition. Dropping edgeFade on the first out-of-range
                // frame used to make alpha jump back up before the state fade ran.
                float finalAlpha = view.alpha * edgeFade;
                SetGraphicAlpha(view.image, markerTint, finalAlpha);
                SetGraphicAlpha(view.diamond, landmarkTint, finalAlpha);

                // Keep the CanvasRenderer enabled for the complete transition. Using
                // finalAlpha here caused an enable/disable edge toggle when the marker
                // crossed the arc boundary or edge-fade threshold due to tiny yaw changes.
                // The Graphic's color alpha handles opacity; the Graphic is disabled only
                // once its transition has actually reached zero.
                bool transitionStillVisible = view.alpha > 0.001f || desiredState == MarkerVisibilityState.Visible;
                bool iconShouldRender = location != null && location.icon != null && transitionStillVisible;
                bool diamondShouldRender = location != null && location.isLandmark && transitionStillVisible;
                view.image.enabled = iconShouldRender;
                view.diamond.enabled = diamondShouldRender;

                ApplyAnimatedScale(view);
            }
        }

        private float GetDistanceScale(float horizontalDistance)
        {
            float minimumScale = Mathf.Clamp(minimumDistanceScale, 0.01f, 1f);
            float start = Mathf.Max(0f, distanceScaleStart);
            float end = Mathf.Max(start + 0.01f, distanceScaleEnd);
            float t = Mathf.InverseLerp(start, end, horizontalDistance);
            // The maximum is clamped to exactly 1; beyond the end range the marker
            // remains at the configured minimum rather than shrinking indefinitely.
            return Mathf.Clamp(Mathf.Lerp(1f, minimumScale, t), minimumScale, 1f);
        }

        private void UpdateMarkerTransition(MarkerView view, MarkerVisibilityState desiredState, float deltaTime)
        {
            if (!view.hasState)
            {
                view.hasState = true;
                view.state = desiredState;
                view.alpha = desiredState == MarkerVisibilityState.Visible ? 1f : 0f;
                view.animatedScale = 1f;
                view.startAlpha = view.targetAlpha = view.alpha;
                view.startScale = view.targetScale = 1f;
                view.transitionElapsed = 0f;
                view.transitionDuration = 0f;
                return;
            }

            if (view.state != desiredState)
            {
                view.state = desiredState;
                view.startAlpha = view.alpha;
                view.targetAlpha = desiredState == MarkerVisibilityState.Visible ? 1f : 0f;
                view.startScale = view.animatedScale;
                view.targetScale = desiredState == MarkerVisibilityState.TooClose
                    ? Mathf.Max(1f, proximityHideScale)
                    : 1f;
                view.transitionElapsed = 0f;
                view.transitionDuration = Mathf.Max(0f, markerTransitionDuration);

                if (view.transitionDuration <= 0f)
                {
                    view.alpha = view.targetAlpha;
                    view.animatedScale = view.targetScale;
                }
            }

            if (view.transitionElapsed < view.transitionDuration)
            {
                view.transitionElapsed += Mathf.Max(0f, deltaTime);
                float t = view.transitionDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(view.transitionElapsed / view.transitionDuration);
                // SmoothStep gives the fade and scale a gentle ease at both ends.
                float eased = t * t * (3f - 2f * t);
                view.alpha = Mathf.Lerp(view.startAlpha, view.targetAlpha, eased);
                view.animatedScale = Mathf.Lerp(view.startScale, view.targetScale, eased);

                if (t >= 1f)
                {
                    view.alpha = view.targetAlpha;
                    view.animatedScale = view.targetScale;
                }
            }
        }

        private static void ApplyAnimatedScale(MarkerView view)
        {
            if (view.rect != null)
            {
                if (Mathf.Approximately(view.animatedScale, 1f))
                {
                    // Explicitly restore the identity scale at the end of normal transitions.
                    view.rect.localScale = Vector3.one;
                }
                else
                {
                    view.rect.localScale = Vector3.one * view.animatedScale;
                }
            }

            if (view.diamondRect != null)
            {
                if (Mathf.Approximately(view.animatedScale, 1f))
                {
                    view.diamondRect.localScale = Vector3.one;
                }
                else
                {
                    view.diamondRect.localScale = Vector3.one * view.animatedScale;
                }
            }
        }

        private static void ResetMarkerScale(MarkerView view)
        {
            if (view == null)
            {
                return;
            }

            if (view.rect != null)
            {
                view.rect.localScale = Vector3.one;
            }

            if (view.diamondRect != null)
            {
                view.diamondRect.localScale = Vector3.one;
                view.diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            view.animatedScale = 1f;
            view.startScale = 1f;
            view.targetScale = 1f;
        }

        private void ResetAllMarkerVisuals(bool disableGraphics)
        {
            for (int i = 0; i < markerViews.Count; i++)
            {
                MarkerView view = markerViews[i];
                ResetMarkerScale(view);
                view.alpha = 1f;
                view.hasState = false;
                view.state = MarkerVisibilityState.Unknown;
                view.transitionElapsed = 0f;
                view.transitionDuration = 0f;
                view.outsideArcLatched = false;
                view.edgeFade = 1f;

                if (disableGraphics)
                {
                    if (view.image != null)
                    {
                        view.image.enabled = false;
                        SetGraphicAlpha(view.image, markerTint, 0f);
                    }

                    if (view.diamond != null)
                    {
                        view.diamond.enabled = false;
                        SetGraphicAlpha(view.diamond, landmarkTint, 0f);
                    }
                }
            }
        }


        /// <summary>
        /// Marker-specific range test with hysteresis. A marker exits at the configured
        /// arc boundary, but cannot re-enter until it is safely inside by the hysteresis
        /// angle. This prevents rapid Visible/OutsideArc state reversals caused by tiny
        /// camera-yaw or target-position changes at the edge.
        /// </summary>
        private bool TryGetMarkerStripPosition(
            MarkerView view,
            float targetBearing,
            float headingBearing,
            float markerHalfExtent,
            out float x,
            out float fade)
        {
            float relativeBearing = Mathf.DeltaAngle(headingBearing, targetBearing);
            float halfArc = Mathf.Max(0.5f, visibleArcDegrees * 0.5f);
            float absoluteRelative = Mathf.Abs(relativeBearing);

            // A 360-degree compass has no outside region; don't create a dead band
            // around the +/-180-degree wrap point.
            if (visibleArcDegrees >= 359.9f)
            {
                view.outsideArcLatched = false;
                x = (relativeBearing / 180f) * (compassWidth * 0.5f);
                fade = 1f;
                return true;
            }

            float hysteresis = Mathf.Clamp(outsideArcHysteresisDegrees, 0f, Mathf.Max(0f, halfArc - 0.1f));
            if (view.outsideArcLatched)
            {
                // Stay outside until the target is well inside the visible arc.
                if (absoluteRelative <= halfArc - hysteresis)
                {
                    view.outsideArcLatched = false;
                }
            }
            else if (absoluteRelative > halfArc)
            {
                // Latch the exit; small movement around the boundary cannot restart it.
                view.outsideArcLatched = true;
            }

            float normalized = absoluteRelative / halfArc;
            float rawX = (relativeBearing / halfArc) * (compassWidth * 0.5f);
            // Keep the fading icon within RectMask2D instead of letting it jump outside
            // the viewport and get clipped before its fade can finish.
            float halfWidth = compassWidth * 0.5f;
            float safeHalfWidth = Mathf.Max(0f, halfWidth - Mathf.Max(0f, markerHalfExtent));
            x = Mathf.Clamp(rawX, -safeHalfWidth, safeHalfWidth);

            // Continue the same edge fade just beyond the nominal arc boundary.
            // Because the fade's end is extended by the hysteresis angle, crossing
            // from Visible to OutsideArc never resets opacity from edge-faded to full.
            float fadeEndNormalized = 1f + (hysteresis / halfArc);
            if (normalized <= edgeFadeStart)
            {
                fade = 1f;
            }
            else
            {
                fade = 1f - Mathf.InverseLerp(edgeFadeStart, fadeEndNormalized, normalized);
            }

            return !view.outsideArcLatched;
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
            if (graphic == null)
            {
                return;
            }

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
            edgeTriangleSize = Mathf.Max(1f, edgeTriangleSize);
            visibleArcDegrees = Mathf.Clamp(visibleArcDegrees, 30f, 360f);
            outsideArcHysteresisDegrees = Mathf.Clamp(outsideArcHysteresisDegrees, 0f, 15f);
            degreeStep = Mathf.Clamp(degreeStep, 1, 180);
            minimumVisibleDistance = Mathf.Max(0f, minimumVisibleDistance);
            markerSize = Mathf.Max(4f, markerSize);
            markerTransitionDuration = Mathf.Max(0f, markerTransitionDuration);
            proximityHideScale = Mathf.Max(1f, proximityHideScale);
            distanceScaleStart = Mathf.Max(0f, distanceScaleStart);
            distanceScaleEnd = Mathf.Max(distanceScaleStart + 0.01f, distanceScaleEnd);
            minimumDistanceScale = Mathf.Clamp(minimumDistanceScale, 0.01f, 1f);
            tickLength = Mathf.Max(1f, tickLength);

            if (locations != null)
            {
                for (int i = 0; i < locations.Count; i++)
                {
                    if (locations[i] != null)
                    {
                        locations[i].landmarkDiamondSize = Mathf.Max(1f, locations[i].landmarkDiamondSize);
                    }
                }
            }
        }
#endif
    }
}
