using UnityEngine;

namespace Assets._Project.Features.Boat
{
    /// <summary>
    /// Visual-only sail rig animation. Reads deployment and trim angle from
    /// BoatController; it does not apply sailing forces or own boat input.
    /// Add this as a separate component to the Boat root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatSailAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BoatController boat;
        [Tooltip("Transform that rotates to trim the sail. Usually the SailBoat prefab root or a dedicated sail pivot.")]
        [SerializeField] private Transform sailRotationPivot;
        [Tooltip("Optional cloth/mesh transform that opens and closes. Leave empty to scale the rotation pivot.")]
        [SerializeField] private Transform sailCloth;

        [Header("Open / Close")]
        [SerializeField] private Vector3 furledScale = new Vector3(0.08f, 1f, 1f);
        [SerializeField] private Vector3 deployedScale = Vector3.one;
        [SerializeField, Min(0.01f)] private float deploymentSmoothTime = 0.22f;
        [SerializeField, Min(0.1f)] private float deploymentAnimationSpeed = 4f;

        [Header("Sail Rotation")]
        [SerializeField, Min(0.1f)] private float rotationSmoothSpeed = 7f;

        private Quaternion baseLocalRotation;
        private Vector3 baseClothScale;
        private float animatedDeployment;
        private float deploymentVelocity;

        private void Reset()
        {
            boat = GetComponent<BoatController>();
            FindSailTransforms();
        }

        private void Awake()
        {
            if (boat == null)
                boat = GetComponent<BoatController>();
            FindSailTransforms();
            CacheBasePose();
            animatedDeployment = boat != null ? boat.SailTrim : 0f;
            ApplyDeployment(animatedDeployment);
        }

        private void LateUpdate()
        {
            if (boat == null)
                return;

            float targetDeployment = Mathf.Clamp01(boat.SailTrim);
            animatedDeployment = Mathf.SmoothDamp(
                animatedDeployment,
                targetDeployment,
                ref deploymentVelocity,
                Mathf.Max(0.01f, deploymentSmoothTime),
                deploymentAnimationSpeed,
                Time.deltaTime);
            ApplyDeployment(animatedDeployment);

            if (sailRotationPivot != null)
            {
                Quaternion targetRotation = baseLocalRotation
                    * Quaternion.AngleAxis(boat.SailRotationDegrees, Vector3.up);
                sailRotationPivot.localRotation = Quaternion.Slerp(
                    sailRotationPivot.localRotation,
                    targetRotation,
                    1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
            }
        }

        private void FindSailTransforms()
        {
            if (sailRotationPivot == null)
            {
                Transform candidate = transform.Find("SailBoat");
                if (candidate == null)
                    candidate = transform.Find("SailRoot");
                if (candidate == null)
                    candidate = transform.Find("Sail");
                sailRotationPivot = candidate;
            }

            if (sailCloth == null && sailRotationPivot != null)
            {
                sailCloth = FindChildByName(sailRotationPivot, "SailMesh");
                if (sailCloth == null)
                    sailCloth = FindChildByName(sailRotationPivot, "SailCloth");
                if (sailCloth == null)
                    sailCloth = FindChildByName(sailRotationPivot, "Canvas");
                if (sailCloth == null)
                    sailCloth = FindSailRendererTransform(sailRotationPivot);
            }
        }

        private static Transform FindSailRendererTransform(Transform root)
        {
            if (root == null)
                return null;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].transform == root)
                    continue;
                string objectName = renderers[i].transform.name;
                if (objectName.IndexOf("sail", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("cloth", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return renderers[i].transform;
            }

            return null;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                    return child;

                Transform nested = FindChildByName(child, childName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private void CacheBasePose()
        {
            if (sailRotationPivot != null)
            {
                baseLocalRotation = sailRotationPivot.localRotation;
            }

            if (sailCloth != null)
                baseClothScale = sailCloth.localScale;

        }

        private void ApplyDeployment(float amount)
        {
            // Never scale the entire nested SailBoat prefab: it may contain the
            // mast and rigid sail supports. Only the actual cloth/mesh is animated.
            if (sailCloth == null)
                return;

            Vector3 closed = Vector3.Scale(baseClothScale, furledScale);
            Vector3 opened = Vector3.Scale(baseClothScale, deployedScale);
            sailCloth.localScale = Vector3.Lerp(closed, opened, Mathf.SmoothStep(0f, 1f, amount));
        }
    }
}
