using UnityEngine;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// A scrolling, top-down "foam field": a small render texture that follows the camera,
    /// fades over time and receives soft foam stamps from boats and swimmers. Wave.shader
    /// samples it in the water fragment pass, so the foam is painted ON the water surface
    /// (it moves with the displaced mesh and is occluded/lit like the water itself).
    ///
    /// Use it through <see cref="WaterContactFoam"/> (per-actor tuning) or call
    /// <see cref="Emit"/> directly. One instance lives in the scene; it is created on demand.
    ///
    /// Shader contract (globals):
    ///   _WaterFoamField        R channel foam amount 0..1
    ///   _WaterFoamFieldParams  (windowMin.x, windowMin.y, 1/windowSize, enabled 0/1)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class WaterFoamField : MonoBehaviour
    {
        private const int MaxStamps = 48;
        private const int MaxRipples = 32;
        private const string UpdateShaderResource = "WaterFoamFieldUpdate";

        private static readonly int FieldId = Shader.PropertyToID("_WaterFoamField");
        private static readonly int FieldParamsId = Shader.PropertyToID("_WaterFoamFieldParams");
        private static readonly int ScrollId = Shader.PropertyToID("_ScrollUV");
        private static readonly int WindowId = Shader.PropertyToID("_Window");
        private static readonly int RetainId = Shader.PropertyToID("_Retain");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int StampCountId = Shader.PropertyToID("_StampCount");
        private static readonly int StampsAId = Shader.PropertyToID("_StampsA");
        private static readonly int StampsBId = Shader.PropertyToID("_StampsB");

        private static readonly int RippleAId = Shader.PropertyToID("_WaterRippleDataA");
        private static readonly int RippleBId = Shader.PropertyToID("_WaterRippleDataB");
        private static readonly int RippleCId = Shader.PropertyToID("_WaterRippleDataC");
        private static readonly int RippleParamsId = Shader.PropertyToID("_WaterRippleParams");

        public static WaterFoamField Instance { get; private set; }

        [Header("Field")]
        [Tooltip("Transform the field window follows. Empty = main camera.")]
        public Transform FollowTarget;
        [Tooltip("Texture resolution. 1024 over 160 m is about 16 cm per texel.")]
        [SerializeField] private int resolution = 1024;
        [Tooltip("Width of the square world window covered by the field (metres).")]
        [SerializeField, Min(20f)] private float worldSize = 160f;

        [Header("Look")]
        [Tooltip("Seconds for fresh foam to fade out almost completely.")]
        [SerializeField, Min(0.5f)] private float lifetime = 6f;
        [Tooltip("Global multiplier on how visible contact foam is in the water shader. 0 disables it.")]
        [SerializeField, Range(0f, 2f)] private float intensity = 1f;

        [Header("Resources")]
        [Tooltip("Leave empty: loaded from Resources/WaterFoamFieldUpdate.")]
        [SerializeField] private Shader updateShader;

        private RenderTexture fieldA;
        private RenderTexture fieldB;
        private Material updateMaterial;
        private readonly Vector4[] stampsA = new Vector4[MaxStamps];
        private readonly Vector4[] stampsB = new Vector4[MaxStamps];
        private int stampCount;
        private Vector2 windowMin;
        private bool windowValid;
        private float lastStampTime = -1000f;
        private Transform cachedCameraTransform;
        private Vector2 lastStampCenter;
        private bool hasStampCenter;

        [Header("Debug")]
        [Tooltip("Draws the foam field in the top-left corner of the Game view.")]
        [SerializeField] private bool showDebugView;

        public float Lifetime => lifetime;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Adds foam along the segment from -> to (world positions, only XZ is used).
        /// Radius in metres, strength 0..1 (screen-blended, so repeated stamps never overshoot).
        /// Safe to call from any update loop; does nothing outside play mode.
        /// </summary>
        public static void Emit(Vector3 from, Vector3 to, float radius, float strength)
        {
            if (!Application.isPlaying || strength <= 0.0001f || radius <= 0.001f)
                return;

            WaterFoamField field = Instance != null ? Instance : CreateInstance();
            field.Queue(from, to, radius, strength);
        }

        public static void Emit(Vector3 point, float radius, float strength)
        {
            Emit(point, point, radius, strength);
        }

        /// <summary>
        /// Starts an expanding ripple ring on the water at <paramref name="center"/> (XZ used).
        /// The ring grows from startRadius to maxRadius over lifetime seconds, tilting the water
        /// normal, tinting crests and shedding small foam bits (foamAmount 0..1).
        /// Evaluated analytically in Wave.shader; at most 16 rings are alive (oldest is replaced).
        /// </summary>
        public static void EmitRipple(Vector3 center, float startRadius, float maxRadius,
            float strength, float foamAmount, float lifetime)
        {
            EmitRipple(center, startRadius, maxRadius, strength, foamAmount, lifetime, Vector2.zero, 0f);
        }

        /// <summary>
        /// Same, but with an arc: arc 0 = full circle (slams, idle), arc 1 = half circle on the side
        /// of <paramref name="facing"/> (XZ direction), used for moving objects.
        /// </summary>
        public static void EmitRipple(Vector3 center, float startRadius, float maxRadius,
            float strength, float foamAmount, float lifetime, Vector2 facing, float arc)
        {
            if (!Application.isPlaying || strength <= 0.001f || maxRadius <= startRadius)
                return;

            WaterFoamField field = Instance != null ? Instance : CreateInstance();
            field.QueueRipple(center, startRadius, maxRadius, strength, foamAmount, lifetime, facing, arc);
        }

        private static WaterFoamField CreateInstance()
        {
            WaterFoamField existing = FindAnyObjectByType<WaterFoamField>();
            if (existing != null)
                return existing;

            GameObject go = new GameObject("WaterFoamField");
            return go.AddComponent<WaterFoamField>();
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            Shader.SetGlobalVector(FieldParamsId, Vector4.zero);
            Shader.SetGlobalVector(RippleParamsId, Vector4.zero);
            ReleaseResources();
        }

        private void OnValidate()
        {
            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 128, 4096);
        }

        private readonly Vector4[] rippleA = new Vector4[MaxRipples];
        private readonly Vector4[] rippleB = new Vector4[MaxRipples];
        private readonly Vector4[] rippleC = new Vector4[MaxRipples];
        private int rippleCount;

        private void QueueRipple(Vector3 center, float startRadius, float maxRadius,
            float strength, float foamAmount, float rippleLifetime, Vector2 facing, float arc)
        {
            int slot;
            if (rippleCount < MaxRipples)
            {
                slot = rippleCount++;
            }
            else
            {
                // Replace the oldest ripple.
                slot = 0;
                for (int i = 1; i < rippleCount; i++)
                    if (rippleA[i].z < rippleA[slot].z)
                        slot = i;
            }

            rippleA[slot] = new Vector4(center.x, center.z, Time.time, Mathf.Max(0.05f, startRadius));
            rippleB[slot] = new Vector4(maxRadius, Mathf.Clamp01(strength), Mathf.Clamp01(foamAmount),
                Mathf.Max(0.3f, rippleLifetime));
            Vector2 f = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector2.up;
            rippleC[slot] = new Vector4(f.x, f.y, facing.sqrMagnitude > 1e-6f ? Mathf.Clamp01(arc) : 0f, 0f);
        }

        private void UpdateRipples()
        {
            float now = Time.time;
            for (int i = rippleCount - 1; i >= 0; i--)
            {
                if (now - rippleA[i].z <= rippleB[i].w)
                    continue;
                rippleCount--;
                rippleA[i] = rippleA[rippleCount];
                rippleB[i] = rippleB[rippleCount];
                rippleC[i] = rippleC[rippleCount];
            }

            Shader.SetGlobalVector(RippleParamsId, new Vector4(intensity > 0f ? rippleCount : 0, now, 0f, 0f));
            if (rippleCount > 0)
            {
                Shader.SetGlobalVectorArray(RippleAId, rippleA);
                Shader.SetGlobalVectorArray(RippleBId, rippleB);
                Shader.SetGlobalVectorArray(RippleCId, rippleC);
            }
        }

        private void Queue(Vector3 from, Vector3 to, float radius, float strength)
        {
            if (stampCount >= MaxStamps)
                return;

            lastStampCenter = new Vector2(to.x, to.z);
            hasStampCenter = true;
            stampsA[stampCount] = new Vector4(from.x, from.z, to.x, to.z);
            stampsB[stampCount] = new Vector4(radius, Mathf.Clamp01(strength), 0f, 0f);
            stampCount++;
        }

        private void LateUpdate()
        {
            UpdateRipples();

            if (intensity <= 0f || !EnsureResources())
            {
                stampCount = 0;
                Shader.SetGlobalVector(FieldParamsId, Vector4.zero);
                return;
            }

            float texel = worldSize / resolution;
            Vector2 center = GetFollowCenter();
            Vector2 newMin = new Vector2(
                Mathf.Floor((center.x - worldSize * 0.5f) / texel) * texel,
                Mathf.Floor((center.y - worldSize * 0.5f) / texel) * texel);

            Vector2 scroll = windowValid ? (newMin - windowMin) / worldSize : Vector2.zero;
            bool alive = Time.time - lastStampTime < lifetime * 1.5f;

            if (stampCount > 0)
                lastStampTime = Time.time;

            // Idle and fully faded: skip the GPU pass entirely.
            if (stampCount > 0 || alive)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.1f);
                updateMaterial.SetVector(ScrollId, scroll);
                updateMaterial.SetVector(WindowId, new Vector4(newMin.x, newMin.y, worldSize, worldSize));
                updateMaterial.SetFloat(RetainId, Mathf.Exp(-dt * 4f / lifetime));
                updateMaterial.SetFloat(CutoffId, 0.0015f);
                updateMaterial.SetInt(StampCountId, stampCount);
                if (stampCount > 0)
                {
                    updateMaterial.SetVectorArray(StampsAId, stampsA);
                    updateMaterial.SetVectorArray(StampsBId, stampsB);
                }

                Graphics.Blit(fieldA, fieldB, updateMaterial);
                RenderTexture swap = fieldA;
                fieldA = fieldB;
                fieldB = swap;
            }
            else if (!alive && windowValid && lastStampTime > 0f)
            {
                ClearFields();
                lastStampTime = -1000f;
            }

            windowMin = newMin;
            windowValid = true;
            stampCount = 0;

            Shader.SetGlobalTexture(FieldId, fieldA);
            Shader.SetGlobalVector(FieldParamsId,
                new Vector4(windowMin.x, windowMin.y, 1f / worldSize, intensity));
        }

        private void OnGUI()
        {
            if (showDebugView && fieldA != null)
                GUI.DrawTexture(new Rect(10f, 10f, 256f, 256f), fieldA, ScaleMode.ScaleToFit, false);
        }

        // ------------------------------------------------------------------ helpers

        private Vector2 GetFollowCenter()
        {
            Transform target = FollowTarget;
            if (target == null)
            {
                if (cachedCameraTransform == null)
                {
                    Camera cam = Camera.main;
                    if (cam == null && Camera.allCamerasCount > 0)
                        cam = Camera.allCameras[0];
                    cachedCameraTransform = cam != null ? cam.transform : null;
                }
                target = cachedCameraTransform;
            }

            if (target != null)
                return new Vector2(target.position.x, target.position.z);

            // No camera: centre on the most recent foam so it can never fall outside the window.
            return hasStampCenter ? lastStampCenter : Vector2.zero;
        }

        private bool EnsureResources()
        {
            if (updateMaterial == null)
            {
                Shader shader = updateShader != null ? updateShader : Resources.Load<Shader>(UpdateShaderResource);
                if (shader == null)
                    shader = Shader.Find("Hidden/CoastalBreakers/WaterFoamFieldUpdate");
                if (shader == null || !shader.isSupported)
                {
                    Debug.LogWarning("WaterFoamField: update shader not found/supported; contact foam disabled.", this);
                    intensity = 0f;
                    return false;
                }
                updateMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (fieldA == null || fieldB == null || fieldA.width != resolution)
            {
                ReleaseTextures();
                fieldA = CreateField("WaterFoamFieldA");
                fieldB = CreateField("WaterFoamFieldB");
                ClearFields();
                windowValid = false;
            }
            return true;
        }

        private RenderTexture CreateField(string textureName)
        {
            RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)
                ? RenderTextureFormat.RHalf
                : RenderTextureFormat.ARGBHalf;
            RenderTexture rt = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
            return rt;
        }

        private void ClearFields()
        {
            ClearTexture(fieldA);
            ClearTexture(fieldB);
        }

        private static void ClearTexture(RenderTexture rt)
        {
            if (rt == null)
                return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = previous;
        }

        private void ReleaseTextures()
        {
            if (fieldA != null) { fieldA.Release(); Destroy(fieldA); fieldA = null; }
            if (fieldB != null) { fieldB.Release(); Destroy(fieldB); fieldB = null; }
        }

        private void ReleaseResources()
        {
            ReleaseTextures();
            if (updateMaterial != null) { Destroy(updateMaterial); updateMaterial = null; }
            windowValid = false;
            stampCount = 0;
        }
    }
}
