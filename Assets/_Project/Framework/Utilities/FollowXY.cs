using UnityEngine;

public class FollowXY : MonoBehaviour
{
    public Transform ReferenceTransform;
    public Transform SecondaryReferenceTransform;
    private Transform currentReferenceTransform;
    void FixedUpdate()
    {
        if (currentReferenceTransform == null || 
            (currentReferenceTransform != ReferenceTransform && currentReferenceTransform != SecondaryReferenceTransform))
            currentReferenceTransform = ReferenceTransform ?? SecondaryReferenceTransform;

        if (currentReferenceTransform != null)
            transform.position = new Vector3(currentReferenceTransform.position.x, transform.position.y, currentReferenceTransform.position.z);

    }

    public void SwitchToPrimaryReference()
    {
        currentReferenceTransform = ReferenceTransform;
    }

    public void SwitchToSecondaryReference()
    {
        currentReferenceTransform = SecondaryReferenceTransform;
    }
    public void ChangePrimaryReference(Transform transform)
    {
        ReferenceTransform  = transform;
    }

    public void ChangeSecondaryReference(Transform transform)
    {
        SecondaryReferenceTransform = transform;
    }
}
