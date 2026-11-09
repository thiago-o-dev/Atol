using UnityEngine;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Keeps ocean water out of the boat's interior. Builds ONE simplified plane from the hull
    /// rim outline (measured from the many hull meshes by <see cref="HullOutline"/>) and renders it
    /// with the "CoastalBreakers/Hull Water Mask" material, which stamps a stencil bit that
    /// Wave.shader refuses to draw over.
    ///
    /// Add it to the boat root (BoatController adds it automatically). To hand-place a plane
    /// instead, assign <c>HullWaterMask.mat</c> to any plane at rim height and turn Auto Build off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HullWaterMask : MonoBehaviour
    {
        public const string ChildName = "HullWaterMask";

        [Header("Build")]
        [Tooltip("Build the plane from the measured hull. Turn off to use your own plane.")]
        [SerializeField] private bool autoBuild = true;
        [Tooltip("Hull mesh object names used for measuring (e.g. \"Boat\"). Empty = every non-ignored mesh.")]
        [SerializeField] private string[] hullNames = { "Boat" };
        [Tooltip("Words that exclude a mesh from measuring when no hull name matches.")]
        [SerializeField] private string[] ignoreWords = { "sail", "mast", "rope", "sprit", "block", "tiller", "foot" };
        [Tooltip("Optional manual hull size (x width, y height, z length). Zero = measure.")]
        [SerializeField] private Vector3 hullSizeOverride;

        [Header("Shape")]
        [Tooltip("Shrinks the plane inwards (m) so the hull rim and walls stay visible.")]
        [SerializeField, Min(0f)] private float inset = 0.06f;
        [Tooltip("Plane height relative to the hull rim (m). Negative = slightly below the rim.")]
        [SerializeField] private float heightOffset = -0.03f;

        [Header("Material")]
        [Tooltip("Defaults to Resources/HullWaterMask material (or one built from the shader).")]
        [SerializeField] private Material maskMaterial;

        private GameObject maskObject;
        private Mesh maskMesh;

        private void OnEnable()
        {
            if (autoBuild)
                Build();
        }

        private void OnDisable()
        {
            if (maskObject != null)
                Destroy(maskObject);
            if (maskMesh != null)
                Destroy(maskMesh);
            maskObject = null;
            maskMesh = null;
        }

        [ContextMenu("Rebuild mask")]
        public void Build()
        {
            if (maskObject != null)
            {
                Destroy(maskObject);
                maskObject = null;
            }

            // Measure with the old mask gone so it can never measure itself.
            HullOutline hull = HullOutline.Measure(transform, hullNames, ignoreWords, hullSizeOverride, 0.4f);
            if (hull.Rim == null || hull.Rim.Length < 3)
                return;

            Material material = ResolveMaterial();
            if (material == null)
            {
                Debug.LogWarning("HullWaterMask: mask shader/material not found.", this);
                return;
            }

            if (maskMesh == null)
                maskMesh = new Mesh { name = "HullWaterMaskMesh", hideFlags = HideFlags.HideAndDontSave };
            FillMesh(maskMesh, hull);

            maskObject = new GameObject(ChildName) { layer = gameObject.layer, hideFlags = HideFlags.DontSave };
            maskObject.transform.SetParent(transform, false);
            maskObject.AddComponent<MeshFilter>().sharedMesh = maskMesh;
            MeshRenderer renderer = maskObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void FillMesh(Mesh mesh, HullOutline hull)
        {
            Vector2[] rim = hull.Rim;
            int n = rim.Length;
            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < n; i++)
                centroid += rim[i];
            centroid /= n;

            float y = hull.RimY + heightOffset;
            Vector3[] vertices = new Vector3[n + 1];
            vertices[0] = new Vector3(centroid.x, y, centroid.y);
            for (int i = 0; i < n; i++)
            {
                Vector2 toCentre = centroid - rim[i];
                float len = toCentre.magnitude;
                Vector2 p = len > inset ? rim[i] + toCentre / len * inset : centroid;
                vertices[i + 1] = new Vector3(p.x, y, p.y);
            }

            int[] triangles = new int[n * 3];
            for (int i = 0; i < n; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + (i + 1) % n;
                triangles[i * 3 + 2] = 1 + i;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private Material ResolveMaterial()
        {
            if (maskMaterial != null)
                return maskMaterial;

            Material loaded = Resources.Load<Material>("HullWaterMask");
            if (loaded != null)
                return maskMaterial = loaded;

            Shader shader = Resources.Load<Shader>("HullWaterMask");
            if (shader == null)
                shader = Shader.Find("CoastalBreakers/Hull Water Mask");
            if (shader == null)
                return null;

            return maskMaterial = new Material(shader) { name = "HullWaterMask (runtime)" };
        }
    }
}
