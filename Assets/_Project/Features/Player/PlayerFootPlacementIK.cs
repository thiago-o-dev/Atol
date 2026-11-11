using UnityEngine;

/// <summary>
/// Humanoid foot-placement IK for grounded walking and landing. Put this component
/// on the same GameObject as the Animator and enable IK Pass on the Animator layer.
/// It automatically disables itself while swimming or aboard a boat.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class PlayerFootPlacementIK : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerController player;

    [Header("Ground Probe")]
    [Tooltip("Include terrain and walkable geometry. Exclude the Player layer where possible.")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [Min(0f)] [SerializeField] private float raycastStartHeight = 0.3f;
    [Min(0.05f)] [SerializeField] private float raycastDistance = 0.8f;
    [Min(-0.1f)] [SerializeField] private float footHeightOffset = 0.025f;
    [Min(0.01f)] [SerializeField] private float maximumFootCorrection = 0.35f;

    [Header("IK Weights")]
    [Range(0f, 1f)] [SerializeField] private float positionWeight = 1f;
    [Range(0f, 1f)] [SerializeField] private float rotationWeight = 1f;
    [Min(0f)] [SerializeField] private float blendSpeed = 12f;

    private float currentWeight;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        currentWeight = 0f;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        ResolveReferences();
        if (animator == null)
            return;

        bool shouldApplyIK = player != null &&
                             player.GroundedMovement != null &&
                             player.ActiveMovement == player.GroundedMovement &&
                             !player.IsAboardBoat &&
                             player.GroundedMovement.IsGrounded;

        float targetWeight = shouldApplyIK ? 1f : 0f;
        currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, blendSpeed * Time.deltaTime);

        ApplyFoot(AvatarIKGoal.LeftFoot);
        ApplyFoot(AvatarIKGoal.RightFoot);
    }

    private void ResolveReferences()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (player == null)
            player = GetComponentInParent<PlayerController>();
    }

    private void ApplyFoot(AvatarIKGoal footGoal)
    {
        float effectiveWeight = currentWeight;
        if (effectiveWeight <= 0.001f || animator == null || !animator.isHuman)
        {
            SetFootWeights(footGoal, 0f);
            return;
        }

        Transform footBone = animator.GetBoneTransform(
            footGoal == AvatarIKGoal.LeftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        if (footBone == null)
        {
            SetFootWeights(footGoal, 0f);
            return;
        }

        Vector3 rayOrigin = footBone.position + Vector3.up * raycastStartHeight;
        float rayLength = raycastStartHeight + raycastDistance;
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLength,
                groundLayers, QueryTriggerInteraction.Ignore))
        {
            SetFootWeights(footGoal, 0f);
            return;
        }

        // Keep the animated X/Z position and only correct its height. The correction
        // limit avoids snapping when a probe catches a wall, ledge, or distant floor.
        Vector3 targetPosition = footBone.position;
        float desiredY = hit.point.y + footHeightOffset;
        float correction = Mathf.Clamp(
            desiredY - footBone.position.y,
            -maximumFootCorrection,
            maximumFootCorrection);
        targetPosition.y += correction;

        Vector3 groundForward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
        if (groundForward.sqrMagnitude < 0.0001f)
            groundForward = Vector3.ProjectOnPlane(transform.right, hit.normal);
        Quaternion targetRotation = Quaternion.LookRotation(groundForward.normalized, hit.normal);

        animator.SetIKPositionWeight(footGoal, effectiveWeight * positionWeight);
        animator.SetIKRotationWeight(footGoal, effectiveWeight * rotationWeight);
        animator.SetIKPosition(footGoal, targetPosition);
        animator.SetIKRotation(footGoal, targetRotation);
    }

    private void SetFootWeights(AvatarIKGoal footGoal, float weight)
    {
        animator.SetIKPositionWeight(footGoal, weight);
        animator.SetIKRotationWeight(footGoal, weight);
    }
}
