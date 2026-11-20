using UnityEngine;
using Assets._Project.Features.Boat;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Camera-following shallow-water wake simulation. The two ping-pong render
    /// textures store height (R) and vertical velocity (G) as a supplemental local
    /// displacement. Visible movement wakes are separate analytic Kelvin V packets
    /// evaluated by Wave.shader; ordinary impact and idle ripples remain circular.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(190)]
    public sealed class WaterWakeField : MonoBehaviour
    {
        private const int MaxImpulses = 32;
        private const string UpdateShaderResource = "WaterWakeFieldUpdate";

        private static readonly int WakeTextureId = Shader.PropertyToID("_WaterWakeField");
        private static readonly int WakeParamsId = Shader.PropertyToID("_WaterWakeFieldParams");
        private static readonly int WakeEnabledId = Shader.PropertyToID("_WaterWakeFieldEnabled");
        private static readonly int ScrollId = Shader.PropertyToID("_ScrollUV");
        private static readonly int WindowId = Shader.PropertyToID("_Window");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int DampingId = Shader.PropertyToID("_Damping");
        private static readonly int BlurId = Shader.PropertyToID("_BlurAmount");
        private static readonly int MaximumHeightId = Shader.PropertyToID("_MaximumHeight");
        private static readonly int TexelWorldId = Shader.PropertyToID("_TexelWorldSize");
        private static readonly int ImpulseCountId = Shader.PropertyToID("_ImpulseCount");
        private static readonly int ImpulsesId = Shader.PropertyToID("_Impulses");

        public static WaterWakeField Instance { get; private set; }

        [Header("Simulation Field")]
        [SerializeField, Range(128, 1024)] private int resolution = 512;
        [SerializeField, Min(24f)] private float worldSize = 128f;
        [SerializeField, Min(0.1f)] private float waveSpeed = 2.6f;
        [Tooltip("Per-frame damping at 60 Hz. 0.98 follows a slowly decaying surface wake.")]
        [SerializeField, Range(0.9f, 1f)] private float dampingPerFrame = 0.98f;
        [Tooltip("Small amount of height smoothing each solver step to remove grid noise.")]
        [SerializeField, Range(0f, 0.08f)] private float blurAmount = 0.012f;
        [SerializeField, Min(1f)] private float maximumHeight = 0.35f;
        [SerializeField, Min(0.5f)] private float activeLifetime = 7f;

        [Header("Follow Target")]
        [Tooltip("Empty follows the main camera. Assign a stable world-space camera/target to override.")]
        public Transform FollowTarget;

        [Header("Resources")]
        [SerializeField] private Shader updateShader;

        private readonly Vector4[] impulses = new Vector4[MaxImpulses];
        private int impulseCount;
        private RenderTexture stateA;
        private RenderTexture stateB;
        private Material updateMaterial;
        private Vector2 windowMin;
        private Vector2 lastImpulseCenter;
        private bool windowValid;
        private bool hasImpulseCenter;
        private float lastImpulseTime = -1000f;
        private Transform cachedCameraTransform;
        private bool warnedMissingShader;
        private float nextBoatDiscoveryTime;

        public static void EmitImpulse(Vector3 worldPoint, float radius, float strength)
        {
            if (!Application.isPlaying || radius <= 0.01f || Mathf.Abs(strength) <= 0.001f)
                return;

            WaterWakeField field = GetOrCreateField();
            if (field != null)
                field.QueueImpulse(worldPoint, radius, strength);

            // Keep the visible wake independent from the local vertex-heightfield.
            // The analytic ring path is already consumed by Wave.shader through
            // WaterFoamField and remains visible even on water meshes whose vertex
            // density cannot show the heightfield displacement.
            float startRadius = Mathf.Max(0.08f, radius * 0.22f);
            float maxRadius = Mathf.Max(startRadius + 0.75f, radius * 7.5f);
            float visibleStrength = Mathf.Clamp01(Mathf.Abs(strength) * 1.8f + 0.08f);
            float visibleLifetime = Mathf.Clamp(maxRadius / 2.1f, 1.8f, 4.2f);
            WaterFoamField.EmitRipple(
                worldPoint,
                startRadius,
                maxRadius,
                visibleStrength,
                0f,
                visibleLifetime,
                Vector2.zero,
                0f,
                1.15f);
        }

        /// <summary>
        /// Deposits one circular wave source at a point on an actor's travelled path.
        /// Call at a distance interval proportional to actor speed. The ping-pong height/
        /// velocity solver propagates each source radially; overlapping wavefronts left
        /// along the path create the transverse crests and diverging Kelvin wake envelope.
        /// </summary>
        public static void EmitTrailRipple(Vector3 worldPoint, float radius, float strength)
        {
            EmitImpulse(worldPoint, radius, strength);
        }

        /// <summary>
        /// Compatibility wrapper for older movement emitters. The wake is now built from
        /// one circular source per movement interval, not a pre-drawn fan of offset stamps.
        /// </summary>
        public static void EmitKelvinWake(Vector3 trailingOrigin, Vector3 travelDirection,
            float radius, float strength, float wakeLength, float halfAngleDegrees = 19.47f, float forwardSpeed = 0f)
        {
            if (!Application.isPlaying || strength <= 0.001f)
                return;

            Vector3 direction = Vector3.ProjectOnPlane(travelDirection, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.001f)
                return;

            WaterWakeField field = GetOrCreateField();
            if (field != null)
                // Keep a very small heightfield impulse as a supplemental height cue.
                // The visible shape is the directional V packet below, not this circle.
                field.QueueImpulse(trailingOrigin, Mathf.Max(0.12f, radius * 0.55f), strength * 0.10f);

            WaterFoamField.EmitKelvinWakeRipple(
                trailingOrigin, direction, wakeLength, Mathf.Clamp01(strength),
                Mathf.Max(0.16f, radius * 0.48f), Mathf.Clamp(radius * 4.5f, 1.2f, 4.5f),
                4.0f, halfAngleDegrees, forwardSpeed);
        }

        private static WaterWakeField GetOrCreateField()
        {
            WaterWakeField field = Instance != null ? Instance : FindAnyObjectByType<WaterWakeField>();
            if (field != null)
                return field;

            GameObject go = new GameObject("Water Wake Field");
            return go.AddComponent<WaterWakeField>();
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }

            Instance = this;
            Shader.SetGlobalFloat(WakeEnabledId, 0f);
            Shader.SetGlobalVector(WakeParamsId, Vector4.zero);
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            Shader.SetGlobalFloat(WakeEnabledId, 0f);
            Shader.SetGlobalVector(WakeParamsId, Vector4.zero);
            Shader.SetGlobalTexture(WakeTextureId, Texture2D.blackTexture);
            ReleaseResources();
        }

        private void OnValidate()
        {
            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 128, 1024);
            worldSize = Mathf.Max(24f, worldSize);
            waveSpeed = Mathf.Max(0.1f, waveSpeed);
            maximumHeight = Mathf.Max(0.05f, maximumHeight);
            activeLifetime = Mathf.Max(1f, activeLifetime);
        }

        private void QueueImpulse(Vector3 worldPoint, float radius, float strength)
        {
            if (impulseCount >= MaxImpulses)
            {
                // Keep the most recent sources; old queued impulses have already
                // become part of the simulated field and are less useful than new ones.
                for (int i = 1; i < MaxImpulses; i++)
                    impulses[i - 1] = impulses[i];
                impulseCount = MaxImpulses - 1;
            }

            impulses[impulseCount++] = new Vector4(
                worldPoint.x,
                worldPoint.z,
                Mathf.Max(0.08f, radius),
                Mathf.Clamp(strength, -3f, 3f));
            lastImpulseCenter = new Vector2(worldPoint.x, worldPoint.z);
            hasImpulseCenter = true;
            lastImpulseTime = Time.time;
        }

        private void Update()
        {
            if (Time.time < nextBoatDiscoveryTime)
                return;
            nextBoatDiscoveryTime = Time.time + 1f;

            // The feature package does not own the game's boat prefab. Attach the
            // emitter to each live BoatController root so boats always register wakes.
            BoatController[] boats = FindObjectsByType<BoatController>(FindObjectsSortMode.None);
            for (int i = 0; i < boats.Length; i++)
            {
                BoatController boat = boats[i];
                if (boat == null)
                    continue;
                if (!boat.TryGetComponent<BoatFoamEmitter>(out _))
                    boat.gameObject.AddComponent<BoatFoamEmitter>();
            }
        }

        private void LateUpdate()
        {
            if (lastImpulseTime < 0f)
            {
                SetGlobalDisabled();
                return;
            }

            if (Time.time - lastImpulseTime > activeLifetime)
            {
                if (stateA != null)
                    ClearFields();
                impulseCount = 0;
                windowValid = false;
                lastImpulseTime = -1000f;
                SetGlobalDisabled();
                return;
            }

            if (!EnsureResources())
            {
                SetGlobalDisabled();
                return;
            }

            float frameDt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector2 center = GetFollowCenter();
            float texelWorld = worldSize / resolution;
            Vector2 newMin = new Vector2(
                Mathf.Floor((center.x - worldSize * 0.5f) / texelWorld) * texelWorld,
                Mathf.Floor((center.y - worldSize * 0.5f) / texelWorld) * texelWorld);
            Vector2 scroll = windowValid ? (newMin - windowMin) / worldSize : Vector2.zero;

            int substeps = Mathf.Clamp(Mathf.CeilToInt(frameDt / (1f / 60f)), 1, 8);
            float stepDt = frameDt / substeps;
            updateMaterial.SetVector(ScrollId, scroll);
            updateMaterial.SetVector(WindowId, new Vector4(newMin.x, newMin.y, worldSize, worldSize));
            updateMaterial.SetFloat(WaveSpeedId, waveSpeed);
            updateMaterial.SetFloat(BlurId, blurAmount);
            updateMaterial.SetFloat(MaximumHeightId, maximumHeight);
            updateMaterial.SetFloat(TexelWorldId, texelWorld);
            updateMaterial.SetVectorArray(ImpulsesId, impulses);

            for (int step = 0; step < substeps; step++)
            {
                updateMaterial.SetFloat(DeltaTimeId, stepDt);
                updateMaterial.SetFloat(DampingId, Mathf.Pow(dampingPerFrame, stepDt * 60f));
                updateMaterial.SetInt(ImpulseCountId, step == 0 ? impulseCount : 0);
                Graphics.Blit(stateA, stateB, updateMaterial);
                RenderTexture swap = stateA;
                stateA = stateB;
                stateB = swap;
            }

            impulseCount = 0;
            windowMin = newMin;
            windowValid = true;

            Shader.SetGlobalTexture(WakeTextureId, stateA);
            Shader.SetGlobalVector(WakeParamsId,
                new Vector4(windowMin.x, windowMin.y, 1f / worldSize, 1f / resolution));
            Shader.SetGlobalFloat(WakeEnabledId, 1f);
        }

        private Vector2 GetFollowCenter()
        {
            Transform target = FollowTarget;
            if (target == null)
            {
                if (cachedCameraTransform == null)
                {
                    Camera camera = Camera.main;
                    if (camera == null && Camera.allCamerasCount > 0)
                        camera = Camera.allCameras[0];
                    cachedCameraTransform = camera != null ? camera.transform : null;
                }
                target = cachedCameraTransform;
            }

            if (target != null)
                return new Vector2(target.position.x, target.position.z);
            return hasImpulseCenter ? lastImpulseCenter : Vector2.zero;
        }

        private bool EnsureResources()
        {
            if (updateMaterial == null)
            {
                Shader shader = updateShader != null ? updateShader : Resources.Load<Shader>(UpdateShaderResource);
                if (shader == null)
                    shader = Shader.Find("Hidden/CoastalBreakers/WaterWakeFieldUpdate");
                if (shader == null || !shader.isSupported)
                {
                    if (!warnedMissingShader)
                    {
                        Debug.LogWarning("WaterWakeField: update shader is unavailable; local vertex displacement is disabled. " +
                                         "Analytic wake rings remain active through WaterFoamField.", this);
                        warnedMissingShader = true;
                    }
                    return false;
                }
                updateMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (stateA == null || stateB == null || stateA.width != resolution)
            {
                ReleaseTextures();
                RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)
                    ? RenderTextureFormat.RGHalf
                    : RenderTextureFormat.ARGBHalf;
                stateA = CreateStateTexture("WaterWakeStateA", format);
                stateB = CreateStateTexture("WaterWakeStateB", format);
                ClearFields();
                windowValid = false;
            }

            return stateA != null && stateB != null && stateA.IsCreated() && stateB.IsCreated();
        }

        private RenderTexture CreateStateTexture(string textureName, RenderTextureFormat format)
        {
            RenderTexture texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        private void ClearFields()
        {
            ClearTexture(stateA);
            ClearTexture(stateB);
        }

        private static void ClearTexture(RenderTexture texture)
        {
            if (texture == null)
                return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        private void SetGlobalDisabled()
        {
            Shader.SetGlobalFloat(WakeEnabledId, 0f);
            Shader.SetGlobalVector(WakeParamsId, Vector4.zero);
        }

        private void ReleaseTextures()
        {
            if (stateA != null) { stateA.Release(); Destroy(stateA); stateA = null; }
            if (stateB != null) { stateB.Release(); Destroy(stateB); stateB = null; }
        }

        private void ReleaseResources()
        {
            ReleaseTextures();
            if (updateMaterial != null) { Destroy(updateMaterial); updateMaterial = null; }
            impulseCount = 0;
            windowValid = false;
        }
    }
}
